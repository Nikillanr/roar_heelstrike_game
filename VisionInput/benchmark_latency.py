"""Measure stages independently. No camera frames are saved or uploaded."""
import argparse
import json
import math
from pathlib import Path
import platform
import socket
import statistics
import subprocess
import sys
import threading
import time

from hand_state import Observation
from transport import encode_state

ROOT = Path(__file__).resolve().parent.parent


def summary(values):
    if not values:
        return {"samples": 0}
    ordered = sorted(values)
    return {"samples": len(values), "p50_ms": round(statistics.median(values), 3),
            "p95_ms": round(ordered[max(0, math.ceil(len(ordered) * .95) - 1)], 3),
            "max_ms": round(max(values), 3)}


def transport_benchmark(samples):
    results = {}
    for protocol in ("tcp", "udp"):
        kind = socket.SOCK_STREAM if protocol == "tcp" else socket.SOCK_DGRAM
        stop = threading.Event()
        with socket.socket(socket.AF_INET, kind) as server:
            server.bind(("127.0.0.1", 0))
            server.settimeout(.2)
            if protocol == "tcp":
                server.listen()

            def echo():
                try:
                    if protocol == "tcp":
                        peer, _ = server.accept()
                        with peer, peer.makefile("rb") as stream:
                            peer.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                            while not stop.is_set():
                                line = stream.readline()
                                if not line:
                                    break
                                peer.sendall(line)
                    else:
                        while not stop.is_set():
                            try:
                                data, address = server.recvfrom(4096)
                                server.sendto(data, address)
                            except socket.timeout:
                                pass
                except OSError:
                    pass

            thread = threading.Thread(target=echo, daemon=True)
            thread.start()
            timings = []
            try:
                with socket.socket(socket.AF_INET, kind) as client:
                    client.settimeout(2)
                    client.connect(server.getsockname())
                    if protocol == "tcp":
                        client.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                    stream = client.makefile("rb") if protocol == "tcp" else None
                    try:
                        for index in range(samples + 10):
                            value = Observation("raised" if index % 2 else "lowered", .9)
                            payload = encode_state("benchmark", index, "either", value.state, .9, time.perf_counter(), value, value)
                            started = time.perf_counter()
                            if protocol == "tcp":
                                client.sendall(payload)
                                received = stream.readline()
                            else:
                                client.send(payload)
                                received = client.recv(4096)
                            elapsed = (time.perf_counter() - started) * 1000
                            if received != payload:
                                raise RuntimeError("Echo mismatch")
                            if index >= 10:
                                timings.append(elapsed)
                    finally:
                        if stream:
                            stream.close()
            finally:
                stop.set()
                thread.join(timeout=1)
            results[protocol + "_loopback_roundtrip"] = summary(timings)
    results["scope"] = "Python socket echo on this PC; excludes Unity, camera and inference. Not a production UDP implementation."
    return results


def model_benchmark(samples):
    import cv2
    import mediapipe as mp
    import numpy as np
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    from download_model import MODEL_PATH
    image_path = ROOT / ".tools/test_pose.jpg"
    image = cv2.imread(str(image_path)) if image_path.exists() else None
    has_person = image is not None
    if image is None:
        image = np.zeros((480, 640, 3), dtype=np.uint8)
    rgb = cv2.cvtColor(cv2.resize(image, (640, 480)), cv2.COLOR_BGR2RGB)
    frame = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
    results = {"mediapipe_version": mp.__version__, "input": "sample person image" if has_person else "blank image (no tracked-person benchmark)"}
    for mode in ("video", "image"):
        options = vision.PoseLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=str(MODEL_PATH)),
            running_mode=vision.RunningMode.VIDEO if mode == "video" else vision.RunningMode.IMAGE,
            num_poses=1)
        timings = []
        detected = 0
        with vision.PoseLandmarker.create_from_options(options) as model:
            for index in range(samples + 10):
                started = time.perf_counter()
                result = model.detect_for_video(frame, index * 34 + 1) if mode == "video" else model.detect(frame)
                elapsed = (time.perf_counter() - started) * 1000
                if index >= 10:
                    timings.append(elapsed)
                    detected += bool(result.pose_landmarks)
        results["cpu_" + mode] = dict(summary(timings), frames_with_pose=detected)
    try:
        python.BaseOptions(model_asset_path=str(MODEL_PATH), delegate=python.BaseOptions.Delegate.GPU).to_pb2()
        results["gpu_delegate"] = "Platform check accepted; GPU execution has not been benchmarked."
    except NotImplementedError as error:
        results["gpu_delegate"] = str(error)
    results["scope"] = "Warm inference on a repeated image; no camera, gesture hold or Unity. Video tracking on real motion may differ."
    return results


def camera_worker(camera_index, backend, samples):
    import cv2
    api = {"any": cv2.CAP_ANY, "dshow": cv2.CAP_DSHOW, "msmf": cv2.CAP_MSMF}[backend]
    capture = cv2.VideoCapture(camera_index, api)
    try:
        if not capture.isOpened():
            return {"error": "Camera did not open", "backend": backend}
        capture.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        capture.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
        capture.set(cv2.CAP_PROP_FPS, 30)
        capture.set(cv2.CAP_PROP_BUFFERSIZE, 1)
        elapsed = []
        first = None
        for index in range(samples + 5):
            started = time.perf_counter()
            valid, frame = capture.read()
            finished = time.perf_counter()
            if not valid:
                return {"error": "Camera read failed", "backend": backend}
            if index >= 5:
                if first is None:
                    first = started
                elapsed.append((finished - started) * 1000)
        return {"backend": capture.getBackendName(), "requested_backend": backend,
                "frame_size": list(frame.shape[:2][::-1]), "reported_fps": capture.get(cv2.CAP_PROP_FPS),
                "actual_fps": round(samples / max(.000001, finished - first), 2),
                "read_time": summary(elapsed)}
    finally:
        capture.release()


def camera_benchmark(camera_index, samples):
    results = {}
    backends = ("any", "dshow", "msmf") if platform.system() == "Windows" else ("any",)
    for backend in backends:
        print(f"Checking camera backend {backend} (no images saved)...", flush=True)
        command = [sys.executable, str(Path(__file__).resolve()), "--stage", "camera-worker",
                   "--camera", str(camera_index), "--backend", backend, "--samples", str(samples)]
        try:
            run = subprocess.run(command, capture_output=True, text=True, timeout=25)
            lines = [line for line in run.stdout.splitlines() if line.startswith("CAMERA_RESULT ")]
            results[backend] = json.loads(lines[-1][14:]) if lines else {"error": run.stderr[-500:] or "No camera result"}
        except subprocess.TimeoutExpired:
            results[backend] = {"error": "Camera open/read exceeded 25 seconds; backend test stopped"}
    results["scope"] = "Capture cadence and blocking read time only. Does not reveal sensor/driver frame age or optical motion-to-display delay."
    return results


def unity_benchmark(host, port, samples):
    """Wait for a Unity post-controller acknowledgment, using only the Python clock."""
    timings, unity_times = [], []
    try:
        with socket.create_connection((host, port), timeout=1) as connection:
            connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            connection.settimeout(2)
            with connection.makefile("rb") as stream:
                for index in range(samples + 5):
                    state = "raised" if index % 2 else "lowered"
                    value = Observation(state, 1.0)
                    started = time.perf_counter()
                    connection.sendall(encode_state("latency-benchmark", index, "either", state, 1.0, started, value, value, acknowledge=True))
                    line = stream.readline(4097)
                    if not line:
                        raise RuntimeError("Unity closed the connection")
                    acknowledgment = json.loads(line)
                    finished = time.perf_counter()
                    if (acknowledgment.get("type") != "vision_ack" or acknowledgment.get("sequence") != index
                            or acknowledgment.get("sessionId") != "latency-benchmark"):
                        raise RuntimeError("Unexpected acknowledgment; stop other vision senders before benchmarking")
                    if (acknowledgment.get("leftVisible") != (state == "raised")
                            or acknowledgment.get("rightVisible") != (state == "raised")):
                        raise RuntimeError("Unity acknowledgment did not match requested ball visibility")
                    if index >= 5:
                        timings.append((finished - started) * 1000)
                        unity_times.append(acknowledgment["unityUpdateMs"])
        return {"send_to_unity_confirmation_roundtrip": summary(timings),
                "unity_receive_to_controller_confirmation": summary(unity_times),
                "scope": "Synthetic hand states through real Unity receiver/controller. Excludes camera, inference and monitor scanout; roundtrip includes return-path time."}
    except (OSError, RuntimeError, ValueError) as error:
        return {"error": str(error), "next_step": "Play the updated Vision Desktop Demo and stop webcam/manual senders, then rerun --stage unity."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", choices=("all", "transport", "model", "camera", "unity", "camera-worker"), default="all")
    parser.add_argument("--samples", type=int, default=60)
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--backend", default="any")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7778)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if not 1 <= args.samples <= 10000:
        parser.error("samples must be between 1 and 10000")
    if args.stage == "camera-worker":
        print("CAMERA_RESULT " + json.dumps(camera_worker(args.camera, args.backend, args.samples)))
        return
    report = {"created_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "platform": platform.platform()}
    stages = ("transport", "model", "camera", "unity") if args.stage == "all" else (args.stage,)
    failed = False
    for stage in stages:
        print(f"Measuring {stage}...", flush=True)
        try:
            if stage == "transport":
                result = transport_benchmark(args.samples)
            elif stage == "model":
                result = model_benchmark(args.samples)
            elif stage == "camera":
                result = camera_benchmark(args.camera, args.samples)
            else:
                result = unity_benchmark(args.host, args.port, args.samples)
        except Exception as error:
            result = {"error": str(error)}
        report[stage] = result
        failed = failed or "error" in result
        print(json.dumps(result, indent=2), flush=True)
    output = args.output or ROOT / ".tools/latency" / (time.strftime("%Y%m%d-%H%M%S") + "-" + args.stage + ".json")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Report: {output}", flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
