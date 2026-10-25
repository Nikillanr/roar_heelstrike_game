import json
import math
import socket
import threading
import time
import unittest
from types import SimpleNamespace

from hand_state import Observation, PoseHandDetector, StableHand, combine
from transport import StateSender, encode_state


class GestureTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
