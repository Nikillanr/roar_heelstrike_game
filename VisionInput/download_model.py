"""Download Google's versioned Pose Landmarker Lite model (first-time setup)."""
from pathlib import Path
import hashlib
import urllib.request

MODEL_URL = "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task"
MODEL_PATH = Path(__file__).resolve().parent / "models" / "pose_landmarker_lite.task"
MODEL_SHA256 = "59929e1d1ee95287735ddd833b19cf4ac46d29bc7afddbbf6753c459690d574a"


def download():
    if MODEL_PATH.exists():
        if hashlib.sha256(MODEL_PATH.read_bytes()).hexdigest() != MODEL_SHA256:
            raise RuntimeError(f"Model checksum mismatch. Remove {MODEL_PATH} and run this downloader again.")
        print(f"Already present: {MODEL_PATH}")
        return
    MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
    temporary = MODEL_PATH.with_suffix(".download")
    try:
        with urllib.request.urlopen(MODEL_URL, timeout=60) as source, temporary.open("wb") as target:
            while chunk := source.read(1024 * 1024):
                target.write(chunk)
        # MediaPipe task bundles are ZIP archives; reject an HTML/error response.
        import zipfile
        if not zipfile.is_zipfile(temporary):
            raise RuntimeError("Downloaded model is not a task bundle")
        if hashlib.sha256(temporary.read_bytes()).hexdigest() != MODEL_SHA256:
            raise RuntimeError("Downloaded model checksum mismatch")
        temporary.replace(MODEL_PATH)
        print(f"Downloaded: {MODEL_PATH}")
    finally:
        temporary.unlink(missing_ok=True)


if __name__ == "__main__":
    download()
