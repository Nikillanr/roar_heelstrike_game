"""Latest-state sender: no queued gesture replay after reconnect."""
import json
import math
import socket
import threading
import time
import uuid
from hand_state import Observation, combine


def encode_state(session, sequence, hand, state, confidence, source_time, left=None, right=None):
    if state not in ("raised", "lowered", "unknown") or hand not in ("left", "right", "either"):
        raise ValueError("Invalid hand state")
    if not math.isfinite(confidence) or not 0 <= confidence <= 1:
        raise ValueError("Invalid confidence")
    if not math.isfinite(source_time) or source_time < 0 or sequence < 0:
        raise ValueError("Invalid timestamp or sequence")
    value = dict(v=1, type="hand_state", sessionId=session, sequence=sequence,
                 hand=hand, state=state, confidence=confidence, sourceTime=source_time)
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
    def __init__(self, host="127.0.0.1", port=7778, hand="either", max_observation_age=0.5):
        self.host, self.port, self.hand = host, port, hand
        self.max_age = max_observation_age
        self.status = "Starting"
        self._snapshot = ("unknown", 0.0, time.monotonic(), None, None)
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._thread = None

    def update(self, state, confidence=1.0, observed_at=None):
        # Validate even if the receiver is currently offline.
        now = time.monotonic() if observed_at is None else observed_at
        encode_state("validate", 0, self.hand, state, confidence, now)
        with self._lock:
            self._snapshot = (state, confidence, now, None, None)

    def update_hands(self, left, right, observed_at=None):
        now = time.monotonic() if observed_at is None else observed_at
        # A selected-hand run explicitly disables the other hand in Unity.
        if self.hand == "left":
            right = Observation("lowered", 1.0)
        elif self.hand == "right":
            left = Observation("lowered", 1.0)
        aggregate = combine(left, right, self.hand)
        encode_state("validate", 0, self.hand, aggregate.state, aggregate.confidence, now, left, right)
        with self._lock:
            self._snapshot = (aggregate.state, aggregate.confidence, now, left, right)

    def start(self):
        self._thread = threading.Thread(target=self._run, name="Vision state sender", daemon=True)
        self._thread.start()
        return self

    def close(self):
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=2)

    def _run(self):
        while not self._stop.is_set():
            try:
                self.status = f"Connecting to {self.host}:{self.port}"
                with socket.create_connection((self.host, self.port), timeout=0.5) as connection:
                    connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                    session = uuid.uuid4().hex
                    sequence = 0
                    self.status = "Connected"
                    while not self._stop.is_set():
                        with self._lock:
                            state, confidence, observed_at, left, right = self._snapshot
                        now = time.monotonic()
                        if self.max_age is not None and now - observed_at > self.max_age:
                            state, confidence = "unknown", 0.0
                            if left is not None:
                                left = right = Observation("unknown")
                        connection.sendall(encode_state(session, sequence, self.hand, state, confidence, observed_at, left, right))
                        sequence += 1
                        self._stop.wait(0.2)
            except OSError as error:
                self.status = f"Waiting for Unity: {error}"
                self._stop.wait(0.5)
        self.status = "Stopped"
