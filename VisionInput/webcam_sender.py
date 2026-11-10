"""Webcam pose inference and stable hand state sent to the Unity experiment."""
import argparse
import json
from collections import deque
from pathlib import Path
import time
from hand_state import PoseHandDetector
from transport import StateSender
from download_model import MODEL_PATH
from latest_frame import LatestFrameReader
from benchmark_latency import summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7778)
    parser.add_argument("--hand", choices=("left", "right", "either"), default="either")
    parser.add_argument("--model", type=Path, default=MODEL_PATH)
    parser.add_argument("--confidence", type=float, default=0.6)
    parser.add_argument("--hold", type=float, default=0.0)
    parser.add_argument("--backend", choices=("any", "msmf", "dshow"), default="any")
    parser.add_argument("--inference-mode", choices=("video", "image"), default="video")
    parser.add_argument("--no-preview", action="store_true")
    parser.add_argument("--seconds", type=float, default=0, help="Stop after this many seconds; 0 runs until quit")
    parser.add_argument("--report", type=Path, help="Save timing statistics as JSON (no images)")
    args = parser.parse_args()
    if not 0 <= args.confidence <= 1 or args.hold < 0 or args.seconds < 0:
        parser.error("confidence must be 0..1 and hold must be nonnegative")
    if not args.model.is_file():
        parser.error("Model missing. Run: python download_model.py")

    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision

    options = vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(args.model)),
        running_mode=vision.RunningMode.VIDEO if args.inference_mode == "video" else vision.RunningMode.IMAGE, num_poses=1,
        min_pose_detection_confidence=args.confidence,
        min_pose_presence_confidence=args.confidence,
        min_tracking_confidence=args.confidence)
    detector = PoseHandDetector(args.hand, args.confidence, args.hold)
    sender = StateSender(args.host, args.port, args.hand).start()
    camera = cv2.VideoCapture(args.camera, {"any": cv2.CAP_ANY, "msmf": cv2.CAP_MSMF, "dshow": cv2.CAP_DSHOW}[args.backend])
    previous_timestamp = -1
    last_frame_sequence = -1
    last_state = None
    reader = None
    previous_result_at = None
    processing_fps = 0.0
    measurements = {key: deque(maxlen=10000) for key in ("inference", "app_frame_age", "read_to_decision")}
    processed_frames = 0
    state_counts = {side: {state: 0 for state in ("raised", "lowered", "unknown")} for side in ("left", "right")}
    started_at = time.perf_counter()
    capture_info = {}
    run_completed = False
    try:
        if not camera.isOpened():
            raise RuntimeError(f"Cannot open camera {args.camera}. Check Windows camera permissions or try --camera 1.")
        camera.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
        camera.set(cv2.CAP_PROP_FPS, 30)
        capture_info = dict(backend=camera.getBackendName(), width=camera.get(cv2.CAP_PROP_FRAME_WIDTH),
                            height=camera.get(cv2.CAP_PROP_FRAME_HEIGHT), reported_fps=camera.get(cv2.CAP_PROP_FPS))
        print(f"Camera: {capture_info} | mode: {args.inference_mode} | hold: {args.hold * 1000:.0f} ms", flush=True)
        # Backends may ignore this request; the reader thread also drains continuously.
        camera.set(cv2.CAP_PROP_BUFFERSIZE, 1)
        reader = LatestFrameReader(camera).start()
        with vision.PoseLandmarker.create_from_options(options) as model:
            started_at = time.perf_counter()
            while True:
                sample = reader.read_latest(last_frame_sequence)
                last_frame_sequence = sample.sequence
                frame, captured_at = sample.image, sample.received_at
                frame_age_ms = (time.perf_counter() - captured_at) * 1000.0
                # Keep the processing resolution bounded even if the driver ignores our request.
                if frame.shape[1] > 640:
                    frame = cv2.resize(frame, (640, round(frame.shape[0] * 640 / frame.shape[1])))
                # Timestamp the frame before inference; the sender separately handles inference stalls.
                timestamp = max(previous_timestamp + 1, int(captured_at * 1000))
                previous_timestamp = timestamp
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                inference_started_at = time.perf_counter()
                image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                result = model.detect_for_video(image, timestamp) if args.inference_mode == "video" else model.detect(image)
                inference_ms = (time.perf_counter() - inference_started_at) * 1000.0
                landmarks = result.pose_landmarks[0] if result.pose_landmarks else None
                height, width = frame.shape[:2]
                detector.update(landmarks, width, height, captured_at)
                left, right = detector.left_observation, detector.right_observation
                sender.update_hands(left, right, observed_at=captured_at)
                result_at = time.perf_counter()
                measurements["inference"].append(inference_ms)
                processed_frames += 1
                measurements["app_frame_age"].append(frame_age_ms)
                measurements["read_to_decision"].append((result_at - captured_at) * 1000)
                for side, observation in (("left", left), ("right", right)):
                    state_counts[side][observation.state] += 1
                if previous_result_at is not None:
                    fps = 1.0 / max(0.000001, result_at - previous_result_at)
                    processing_fps = fps if processing_fps == 0.0 else processing_fps * 0.9 + fps * 0.1
                previous_result_at = result_at
                states = (left.state, right.state)
                if states != last_state:
                    print(f"left: {left.state} ({left.confidence:.2f}) | right: {right.state} ({right.confidence:.2f}) | model: {inference_ms:.0f} ms | app frame age: {frame_age_ms:.0f} ms", flush=True)
                    last_state = states
                if args.seconds and result_at - started_at >= args.seconds:
                    break
                if args.no_preview:
                    continue
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
                cv2.putText(preview, f"Processing: {processing_fps:.0f} FPS | model: {inference_ms:.0f} ms | app frame age: {frame_age_ms:.0f} ms",
                            (12, 101), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1)
                send_wait = "--" if sender.last_send_wait_ms is None else f"{sender.last_send_wait_ms:.0f}"
                cv2.putText(preview, f"Decision -> send: {send_wait} ms | confirmation hold: {args.hold * 1000:.0f} ms",
                            (12, 124), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1)
                ack = "--" if sender.last_unity_ack_ms is None else f"{sender.last_unity_ack_ms:.0f}"
                frame_ack = "--" if sender.last_frame_ack_ms is None else f"{sender.last_frame_ack_ms:.0f}"
                cv2.putText(preview, f"Decision -> Unity ACK: {ack} ms | read -> ACK: {frame_ack} ms",
                            (12, 147), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1)
                cv2.putText(preview, f"Before hold: L {detector.left.candidate} | R {detector.right.candidate} | {args.inference_mode} mode",
                            (12, 170), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1)
                cv2.putText(preview, "Show shoulders + wrists. Q / Esc to quit.", (12, height - 15),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
                cv2.imshow("Rehab vision input", preview)
                if cv2.waitKey(1) & 0xFF in (ord("q"), 27):
                    break
                if cv2.getWindowProperty("Rehab vision input", cv2.WND_PROP_VISIBLE) < 1:
                    break
            run_completed = True
    except KeyboardInterrupt:
        pass
    finally:
        elapsed = time.perf_counter() - started_at
        sender.close()
        if reader is not None:
            reader.close()
        else:
            camera.release()
        cv2.destroyAllWindows()
        if args.report and measurements["inference"]:
            acknowledgments = list(sender.latency_samples)
            report = dict(completed=run_completed, seconds=round(elapsed, 3), camera=capture_info,
                          inference_mode=args.inference_mode, hold_seconds=args.hold, preview=not args.no_preview,
                          processed_frames=processed_frames,
                          processing_fps=round(processed_frames / max(.001, elapsed), 2),
                          state_frame_counts=state_counts,
                          stages={key: summary(values) for key, values in measurements.items()},
                          decision_to_unity_ack=summary([sample[0] for sample in acknowledgments]),
                          read_to_unity_ack=summary([sample[1] for sample in acknowledgments]),
                          unity_receive_to_confirmation=summary([sample[2] for sample in acknowledgments]),
                          send_to_unity_ack=summary([sample[3] for sample in acknowledgments]),
                          scope="App timings only: excludes sensor/driver buffering, initial gesture confirmation hold, and display scanout. ACK confirms controller state, including any unknown-state grace.")
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
            print(f"Timing report: {args.report.resolve()}", flush=True)


if __name__ == "__main__":
    main()
