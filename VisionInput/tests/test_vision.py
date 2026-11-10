import json
import math
import socket
import threading
import time
import unittest
import queue
from types import SimpleNamespace

from hand_state import Observation, PoseHandDetector, StableHand, combine
from transport import StateSender, encode_state
from latest_frame import LatestFrameReader


class GestureTests(unittest.TestCase):
    def test_zero_hold_confirms_both_directions_immediately(self):
        gate = StableHand(hold_seconds=0)
        self.assertEqual(gate.update(.3, .9, 1).state, "raised")
        self.assertEqual(gate.update(-.2, .9, 1.03).state, "lowered")

    def test_dwell_and_deadband(self):
        gate = StableHand()
        self.assertEqual(gate.update(0.3, 0.9, 0).state, "unknown")
        self.assertEqual(gate.update(0.3, 0.9, 0.1).state, "unknown")
        self.assertEqual(gate.update(0.3, 0.9, 0.2).state, "raised")
        self.assertEqual(gate.update(0.05, 0.9, 0.3).state, "raised")
        self.assertEqual(gate.update(-0.2, 0.9, 0.4).state, "raised")
        self.assertEqual(gate.update(-0.2, 0.9, 0.6).state, "lowered")

    def test_jitter_does_not_confirm(self):
        gate = StableHand()
        for i in range(10):
            value = 0.3 if i % 2 else -0.2
            self.assertEqual(gate.update(value, 0.9, i * 0.05).state, "unknown")

    def test_tracking_loss_resets_confirmation(self):
        gate = StableHand()
        gate.update(0.3, 0.9, 0)
        gate.update(0.3, 0.9, 0.2)
        self.assertEqual(gate.update(None, 0, 0.3).state, "unknown")
        self.assertEqual(gate.update(0.3, 0.9, 0.4).state, "unknown")
        self.assertEqual(gate.update(0.3, 0.9, 0.6).state, "raised")

    def test_stall_is_not_hold_time(self):
        gate = StableHand()
        gate.update(0.3, 0.9, 0)
        self.assertEqual(gate.update(0.3, 0.9, 2).state, "unknown")
        self.assertEqual(gate.update(math.nan, 0.9, 2.1).state, "unknown")

    def test_either_hand_semantics(self):
        up, down, unknown = Observation("raised", .9), Observation("lowered", .8), Observation("unknown")
        self.assertEqual(combine(up, down).state, "raised")
        self.assertEqual(combine(unknown, up).state, "raised")
        self.assertEqual(combine(down, unknown).state, "unknown")
        self.assertEqual(combine(down, down).state, "lowered")
        self.assertEqual(combine(up, down, "right").state, "lowered")

    def test_pose_scale_and_confidence(self):
        pose = [SimpleNamespace(x=.5, y=.6, visibility=.9, presence=.9) for _ in range(33)]
        pose[11].x, pose[12].x = .3, .7
        pose[11].y = pose[12].y = .5
        pose[15].y, pose[16].y = .2, .8
        detector = PoseHandDetector(hold_seconds=0)
        self.assertEqual(detector.update(pose, 640, 480, 0).state, "raised")
        self.assertEqual(detector.left_observation.state, "raised")
        self.assertEqual(detector.right_observation.state, "lowered")
        pose[15].visibility = .1
        self.assertEqual(detector.update(pose, 640, 480, .1).state, "unknown")
        self.assertEqual(detector.right_observation.state, "lowered")
        pose[15].visibility, pose[15].y = .9, .8
        self.assertEqual(detector.update(pose, 1280, 960, .2).state, "lowered")
        self.assertEqual(detector.update(None, 640, 480, .3).state, "unknown")


class TransportTests(unittest.TestCase):
    def test_fragmented_unity_ack_and_invalid_session(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            listener.settimeout(3)
            sender = StateSender(port=listener.getsockname()[1], max_observation_age=None)
            sender.update("lowered")
            sender.start()
            try:
                connection, _ = listener.accept()
                with connection, connection.makefile("rb") as stream:
                    message = json.loads(stream.readline())
                    self.assertTrue(message["acknowledge"])
                    ack = dict(type="vision_ack", sessionId="wrong-session", sequence=message["sequence"], unityUpdateMs=7.5)
                    connection.sendall((json.dumps(ack) + "\n").encode())
                    ack["sessionId"] = message["sessionId"]
                    payload = (json.dumps(ack) + "\n").encode()
                    connection.sendall(payload[:12])
                    connection.sendall(payload[12:])
                    deadline = time.perf_counter() + 1
                    while sender.last_unity_ack_ms is None and time.perf_counter() < deadline:
                        time.sleep(.005)
                    self.assertEqual(sender.last_unity_update_ms, 7.5)
                    self.assertGreaterEqual(sender.last_frame_ack_ms, sender.last_unity_ack_ms)
                    self.assertEqual(len(sender.latency_samples), 1)
            finally:
                sender.close()

    def test_state_change_interrupts_heartbeat_wait(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            listener.settimeout(3)
            # Long heartbeat distinguishes notification from waiting for the next tick.
            sender = StateSender(port=listener.getsockname()[1], max_observation_age=None, heartbeat_seconds=2)
            sender.update_hands(Observation("lowered", .9), Observation("lowered", .9))
            sender.start()
            try:
                connection, _ = listener.accept()
                with connection:
                    connection.settimeout(.5)
                    with connection.makefile("rb") as stream:
                        self.assertEqual(json.loads(stream.readline())["leftState"], "lowered")
                        sender.update_hands(Observation("raised", .9), Observation("lowered", .9))
                        self.assertEqual(json.loads(stream.readline())["leftState"], "raised")
            finally:
                sender.close()

    def test_independent_hands_in_one_packet(self):
        left, right = Observation("raised", .9), Observation("lowered", .8)
        value = json.loads(encode_state("s", 4, "either", "raised", .9, 1, left, right))
        self.assertEqual(value["v"], 2)
        self.assertEqual((value["leftState"], value["rightState"]), ("raised", "lowered"))
        self.assertEqual(value["rightConfidence"], .8)
        with self.assertRaises(ValueError):
            encode_state("s", 4, "either", "raised", .9, 1, left, Observation("raised", math.nan))

    def test_per_hand_heartbeat_staleness_and_selection(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            listener.settimeout(3)
            sender = StateSender(port=listener.getsockname()[1], hand="left", max_observation_age=.1)
            sender.update_hands(Observation("raised", .9), Observation("raised", .8))
            sender.start()
            try:
                connection, _ = listener.accept()
                with connection:
                    connection.settimeout(3)
                    with connection.makefile("rb") as stream:
                        initial = json.loads(stream.readline())
                        self.assertEqual((initial["leftState"], initial["rightState"]), ("raised", "lowered"))
                        stale = json.loads(stream.readline())
                        self.assertEqual((stale["leftState"], stale["rightState"]), ("unknown", "unknown"))
            finally:
                sender.close()

    def test_schema_and_invalid_numbers(self):
        payload = encode_state("session", 3, "left", "raised", .8, 12)
        self.assertTrue(payload.endswith(b"\n"))
        value = json.loads(payload)
        self.assertEqual((value["v"], value["type"], value["state"]), (1, "hand_state", "raised"))
        for confidence in (math.nan, math.inf, -1, 2):
            with self.assertRaises(ValueError):
                encode_state("s", 0, "either", "raised", confidence, 0)

    def test_heartbeat_stale_observation_and_reconnect(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            listener.settimeout(3)
            sender = StateSender(port=listener.getsockname()[1], max_observation_age=.05)
            sender.update("raised")
            sender.start()
            try:
                first, _ = listener.accept()
                with first:
                    first.settimeout(3)
                    with first.makefile("rb") as stream:
                        initial = json.loads(stream.readline())
                        self.assertEqual(initial["state"], "raised")
                        following = json.loads(stream.readline())
                        self.assertEqual(following["state"], "unknown")
                        self.assertGreater(following["sequence"], initial["sequence"])
                # The sender must not replay the old raised state on its next connection.
                sender.update("lowered")
                second, _ = listener.accept()
                with second:
                    second.settimeout(3)
                    with second.makefile("rb") as stream:
                        restored = json.loads(stream.readline())
                        self.assertNotEqual(restored["sessionId"], initial["sessionId"])
                        self.assertNotEqual(restored["state"], "raised")
            finally:
                sender.close()


class CameraTests(unittest.TestCase):
    class Capture:
        def __init__(self):
            self.frames = queue.Queue()
            self.released = threading.Event()
            self.third_read = threading.Event()

        def read(self):
            image = self.frames.get(timeout=1)
            if image == 3:
                self.third_read.set()
            return (image is not None), image

        def release(self):
            self.released.set()

    def test_slow_consumer_gets_newest_frame(self):
        capture = self.Capture()
        reader = LatestFrameReader(capture).start()
        try:
            capture.frames.put(1)
            first = reader.read_latest()
            self.assertEqual(first.image, 1)
            capture.frames.put(2)
            capture.frames.put(3)
            self.assertTrue(capture.third_read.wait(1))
            # Wait until publication, not just completion of the mock read.
            with reader._changed:
                self.assertTrue(reader._changed.wait_for(lambda: reader._latest.sequence == 3, 1))
            latest = reader.read_latest(first.sequence)
            self.assertEqual(latest.image, 3)
            self.assertGreaterEqual(latest.received_at, first.received_at)
        finally:
            capture.frames.put(None)
            reader.close()
        self.assertTrue(capture.released.wait(1))

    def test_capture_failure_is_reported(self):
        capture = self.Capture()
        capture.frames.put(None)
        reader = LatestFrameReader(capture).start()
        try:
            with self.assertRaisesRegex(RuntimeError, "stopped producing"):
                reader.read_latest()
        finally:
            reader.close()
        self.assertTrue(capture.released.is_set())


if __name__ == "__main__":
    unittest.main()
