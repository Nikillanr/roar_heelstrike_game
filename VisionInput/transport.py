"""Latest-state sender: no queued gesture replay after reconnect."""
import json
import math
import socket
import threading
import time
import uuid
from collections import deque
from hand_state import Observation, combine


def encode_state(session, sequence, hand, state, confidence, source_time, left=None, right=None, acknowledge=False):
    if state not in ("raised", "lowered", "unknown") or hand not in ("left", "right", "either"):
        raise ValueError("Invalid hand state")
    if not math.isfinite(confidence) or not 0 <= confidence <= 1:
        raise ValueError("Invalid confidence")
    if not math.isfinite(source_time) or source_time < 0 or sequence < 0:
        raise ValueError("Invalid timestamp or sequence")
    value = dict(v=1, type="hand_state", sessionId=session, sequence=sequence,
                 hand=hand, state=state, confidence=confidence, sourceTime=source_time)
    if acknowledge:
        value["acknowledge"] = True
    if left is not None or right is not None:
        for side in (left, right):
            if (side is None or side.state not in ("raised", "lowered", "unknown")
                    or not math.isfinite(side.confidence) or not 0 <= side.confidence <= 1):
                raise ValueError("Invalid per-hand observation")
        value.update(v=2, leftState=left.state, rightState=right.state,
                     leftConfidence=left.confidence, rightConfidence=right.confidence)
    return (json.dumps(value,
                       separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")


class StateSender:
    def __init__(self, host="127.0.0.1", port=7778, hand="either", max_observation_age=0.5, heartbeat_seconds=0.2):
        if not math.isfinite(heartbeat_seconds) or heartbeat_seconds <= 0:
            raise ValueError("heartbeat_seconds must be positive and finite")
        self.host, self.port, self.hand = host, port, hand
        self.max_age = max_observation_age
        self.heartbeat_seconds = heartbeat_seconds
        self.status = "Starting"
        self.last_send_wait_ms = None
        self.last_unity_ack_ms = None
        self.last_send_ack_ms = None
        self.last_frame_ack_ms = None
        self.last_unity_update_ms = None
        self.latency_samples = deque(maxlen=10000)
        now = time.perf_counter()
        self._snapshot = ("unknown", 0.0, now, None, None, now)
        self._lock = threading.Lock()
        self._changed = threading.Condition(self._lock)
        self._state_revision = 0
        self._has_observation = False
        self._stop = threading.Event()
        self._thread = None

    def update(self, state, confidence=1.0, observed_at=None):
        # Validate even if the receiver is currently offline.
        now = time.perf_counter() if observed_at is None else observed_at
        encode_state("validate", 0, self.hand, state, confidence, now)
        self._store(state, confidence, now, None, None)

    def update_hands(self, left, right, observed_at=None):
        now = time.perf_counter() if observed_at is None else observed_at
        # A selected-hand run explicitly disables the other hand in Unity.
        if self.hand == "left":
            right = Observation("lowered", 1.0)
        elif self.hand == "right":
            left = Observation("lowered", 1.0)
        aggregate = combine(left, right, self.hand)
        encode_state("validate", 0, self.hand, aggregate.state, aggregate.confidence, now, left, right)
        self._store(aggregate.state, aggregate.confidence, now, left, right)

    def _store(self, state, confidence, observed_at, left, right):
        ready_at = time.perf_counter()
        with self._changed:
            old = self._snapshot
            old_key = (old[0], old[3].state if old[3] else None, old[4].state if old[4] else None)
            new_key = (state, left.state if left else None, right.state if right else None)
            was_stale = self.max_age is not None and ready_at - old[2] > self.max_age
            self._snapshot = (state, confidence, observed_at, left, right, ready_at)
            self._has_observation = True
            if old_key != new_key or was_stale:
                self._state_revision += 1
                self._changed.notify_all()

    def start(self):
        self._thread = threading.Thread(target=self._run, name="Vision state sender", daemon=True)
        self._thread.start()
        return self

    def close(self):
        self._stop.set()
        with self._changed:
            self._changed.notify_all()
        if self._thread:
            self._thread.join(timeout=2)

    def _read_acknowledgments(self, connection, session, pending, pending_lock, ended):
        buffer = b""
        try:
            while not self._stop.is_set() and not ended.is_set():
                try:
                    data = connection.recv(4096)
                except socket.timeout:
                    continue
                if not data:
                    break
                buffer += data
                if len(buffer) > 16384:
                    break
                while b"\n" in buffer:
                    line, buffer = buffer.split(b"\n", 1)
                    try:
                        value = json.loads(line)
                        if value.get("type") != "vision_ack" or value.get("sessionId") != session:
                            continue
                        with pending_lock:
                            timing = pending.pop(value.get("sequence"), None)
                        unity_ms = value.get("unityUpdateMs")
                        if timing is None or not isinstance(unity_ms, (int, float)) or not math.isfinite(unity_ms) or unity_ms < 0:
                            continue
                        now = time.perf_counter()
                        observed_at, ready_at, sent_at = timing
                        self.last_unity_ack_ms = max(0.0, (now - ready_at) * 1000)
                        self.last_frame_ack_ms = max(0.0, (now - observed_at) * 1000)
                        self.last_unity_update_ms = unity_ms
                        self.last_send_ack_ms = max(0.0, (now - sent_at) * 1000)
                        self.latency_samples.append((self.last_unity_ack_ms, self.last_frame_ack_ms, unity_ms, self.last_send_ack_ms))
                    except (ValueError, TypeError, AttributeError):
                        continue
        except OSError:
            pass
        finally:
            ended.set()
            with self._changed:
                self._changed.notify_all()

    def _run(self):
        while not self._stop.is_set():
            try:
                self.status = f"Connecting to {self.host}:{self.port}"
                with socket.create_connection((self.host, self.port), timeout=0.5) as connection:
                    connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                    session = uuid.uuid4().hex
                    sequence = 0
                    pending, pending_lock, ended = {}, threading.Lock(), threading.Event()
                    self.last_unity_ack_ms = self.last_frame_ack_ms = self.last_unity_update_ms = None
                    self.last_send_ack_ms = None
                    reader = threading.Thread(target=self._read_acknowledgments,
                                              args=(connection, session, pending, pending_lock, ended), daemon=True)
                    reader.start()
                    self.status = "Connected"
                    try:
                        while not self._stop.is_set() and not ended.is_set():
                            with self._changed:
                                state, confidence, observed_at, left, right, ready_at = self._snapshot
                                sent_revision = self._state_revision
                                has_observation = self._has_observation
                            now = time.perf_counter()
                            stale = self.max_age is not None and now - observed_at > self.max_age
                            if stale:
                                state, confidence = "unknown", 0.0
                                if left is not None:
                                    left = right = Observation("unknown")
                            if has_observation and not stale:
                                with pending_lock:
                                    pending[sequence] = (observed_at, ready_at, now)
                                    if len(pending) > 128:
                                        del pending[min(pending)]
                            connection.sendall(encode_state(session, sequence, self.hand, state, confidence, observed_at, left, right, acknowledge=True))
                            self.last_send_wait_ms = max(0.0, (now - ready_at) * 1000.0) if has_observation and not stale else None
                            sequence += 1
                            # State changes wake the sender immediately; unchanged states still get heartbeats.
                            with self._changed:
                                self._changed.wait_for(
                                    lambda: self._stop.is_set() or ended.is_set() or self._state_revision != sent_revision,
                                    timeout=self.heartbeat_seconds)
                    finally:
                        ended.set()
                        try:
                            connection.shutdown(socket.SHUT_RDWR)
                        except OSError:
                            pass
                        reader.join(timeout=1)
            except OSError as error:
                self.status = f"Waiting for Unity: {error}"
                self._stop.wait(0.5)
        self.status = "Stopped"
