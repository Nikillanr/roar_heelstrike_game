"""Camera-independent gesture logic. Image Y increases downward."""
from dataclasses import dataclass
import math


@dataclass(frozen=True)
class Observation:
    state: str
    confidence: float = 0.0


class StableHand:
    def __init__(self, hold_seconds=0.2, enter=0.15, exit=-0.05):
        if hold_seconds < 0 or enter <= exit:
            raise ValueError("Require nonnegative hold time and enter > exit")
        self.hold = hold_seconds
        self.enter = enter
        self.exit = exit
        self.stable = "unknown"
        self.candidate = "unknown"
        self.since = 0.0
        self.last_time = None

    def update(self, height, confidence, now):
        # Do not count a camera stall as continuous confirmation.
        if self.last_time is not None and (now < self.last_time or now - self.last_time > 0.5):
            self.stable = self.candidate = "unknown"
        self.last_time = now
        if height is None or not math.isfinite(height):
            self.stable = self.candidate = "unknown"
            return Observation("unknown")
        target = "raised" if height >= self.enter else "lowered" if height <= self.exit else self.stable
        if target == "unknown":
            self.candidate = "unknown"
            return Observation("unknown")
        if target != self.candidate:
            self.candidate, self.since = target, now
        if now - self.since + 1e-9 >= self.hold:
            self.stable = target
        return Observation(self.stable, confidence if self.stable != "unknown" else 0.0)


def combine(left, right, hand="either"):
    if hand == "left":
        return left
    if hand == "right":
        return right
    if hand != "either":
        raise ValueError("hand must be left, right, or either")
    raised = [value for value in (left, right) if value.state == "raised"]
    if raised:
        return Observation("raised", max(value.confidence for value in raised))
    if left.state == right.state == "lowered":
        return Observation("lowered", min(left.confidence, right.confidence))
    return Observation("unknown")


class PoseHandDetector:
    def __init__(self, hand="either", min_confidence=0.6, hold_seconds=0.2):
        self.hand = hand
        self.min_confidence = min_confidence
        self.left = StableHand(hold_seconds)
        self.right = StableHand(hold_seconds)
        self.left_observation = Observation("unknown")
        self.right_observation = Observation("unknown")

    def update(self, landmarks, width, height, now):
        # MediaPipe anatomical indices: shoulders 11/12, wrists 15/16.
        def reliable(index):
            if landmarks is None or len(landmarks) <= index:
                return False
            point = landmarks[index]
            return (all(math.isfinite(v) for v in (point.x, point.y, point.visibility, point.presence))
                    and 0 <= point.x <= 1 and 0 <= point.y <= 1
                    and min(point.visibility, point.presence) >= self.min_confidence)

        scale = 0.0
        if reliable(11) and reliable(12):
            a, b = landmarks[11], landmarks[12]
            scale = math.hypot((a.x - b.x) * width, (a.y - b.y) * height)

        def measure(shoulder, wrist, gate):
            if scale < 20 or not reliable(shoulder) or not reliable(wrist):
                return gate.update(None, 0.0, now)
            points = [landmarks[index] for index in (11, 12, wrist)]
            confidence = min(min(p.visibility, p.presence) for p in points)
            separation = (landmarks[shoulder].y - landmarks[wrist].y) * height / scale
            return gate.update(separation, confidence, now)

        self.left_observation = measure(11, 15, self.left)
        self.right_observation = measure(12, 16, self.right)
        return combine(self.left_observation, self.right_observation, self.hand)
