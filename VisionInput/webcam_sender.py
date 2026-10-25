"""Webcam pose inference and stable hand state sent to the Unity experiment."""
import argparse
from pathlib import Path
import time
from hand_state import PoseHandDetector
from transport import StateSender
from download_model import MODEL_PATH


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7778)
    parser.add_argument("--hand", choices=("left", "right", "either"), default="either")
    parser.add_argument("--model", type=Path, default=MODEL_PATH)
    parser.add_argument("--confidence", type=float, default=0.6)
    parser.add_argument("--hold", type=float, default=0.2)
    args = parser.parse_args()
    if not 0 <= args.confidence <= 1 or args.hold < 0:
        parser.error("confidence must be 0..1 and hold must be nonnegative")
    if not args.model.is_file():
        parser.error("Model missing. Run: python download_model.py")

    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision

    options = vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(args.model)),
        running_mode=vision.RunningMode.VIDEO, num_poses=1,
        min_pose_detection_confidence=args.confidence,
        min_pose_presence_confidence=args.confidence,
        min_tracking_confidence=args.confidence)
    detector = PoseHandDetector(args.hand, args.confidence, args.hold)
    sender = StateSender(args.host, args.port, args.hand).start()
    camera = cv2.VideoCapture(args.camera)
    previous_timestamp = -1
    last_state = None
    try:
        if not camera.isOpened():
            raise RuntimeError(f"Cannot open camera {args.camera}. Check Windows camera permissions or try --camera 1.")
        camera.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
        with vision.PoseLandmarker.create_from_options(options) as model:
            while True:
                valid, frame = camera.read()
                if not valid:
                    sender.update("unknown", 0.0)
                    raise RuntimeError("Camera stopped producing frames")
                captured_at = time.monotonic()
                # Timestamp the frame before inference; the sender separately handles inference stalls.
                timestamp = max(previous_timestamp + 1, int(captured_at * 1000))
                previous_timestamp = timestamp
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                result = model.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), timestamp)
                landmarks = result.pose_landmarks[0] if result.pose_landmarks else None
                height, width = frame.shape[:2]
                observation = detector.update(landmarks, width, height, captured_at)
                left, right = detector.left_observation, detector.right_observation
                sender.update_hands(left, right, observed_at=captured_at)
                states = (left.state, right.state)
                if states != last_state:
                    print(f"left: {left.state} | right: {right.state}", flush=True)
                    last_state = states
                if landmarks:
                    for a, b in ((11, 12), (11, 13), (13, 15), (12, 14), (14, 16)):
                        points = [landmarks[index] for index in (a, b)]
                        if all(min(p.visibility, p.presence) >= args.confidence for p in points):
                            p0, p1 = [(int(p.x * width), int(p.y * height)) for p in points]
                            cv2.line(frame, p0, p1, (0, 220, 100), 3)
                            cv2.circle(frame, p1, 6, (0, 220, 100), -1)
                # Mirror only for presentation; anatomical indices are unchanged.
                preview = cv2.flip(frame, 1)
                cv2.putText(preview, f"Left: {left.state} | confidence: {left.confidence:.2f}",
                            (12, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
                cv2.putText(preview, f"Right: {right.state} | confidence: {right.confidence:.2f}",
                            (12, 53), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
                cv2.putText(preview, sender.status[:75], (12, 78), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1)
                cv2.putText(preview, "Show shoulders + wrists. Q / Esc to quit.", (12, height - 15),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
                cv2.imshow("Rehab vision input", preview)
                if cv2.waitKey(1) & 0xFF in (ord("q"), 27):
                    break
                if cv2.getWindowProperty("Rehab vision input", cv2.WND_PROP_VISIBLE) < 1:
                    break
    except KeyboardInterrupt:
        pass
    finally:
        sender.close()
        camera.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
