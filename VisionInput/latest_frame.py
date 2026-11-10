"""Drain camera frames independently of inference; retain only the newest frame."""
from dataclasses import dataclass
import threading
import time


@dataclass(frozen=True)
class CameraFrame:
    image: object
    received_at: float
    sequence: int


class LatestFrameReader:
    def __init__(self, capture):
        self.capture = capture
        self._changed = threading.Condition()
        self._stop = threading.Event()
        self._latest = None
        self._error = None
        self._thread = threading.Thread(target=self._run, name="Latest webcam frame", daemon=True)

    def start(self):
        self._thread.start()
        return self

    def _run(self):
        sequence = 0
        try:
            while not self._stop.is_set():
                valid, image = self.capture.read()
                if not valid:
                    raise RuntimeError("Camera stopped producing frames")
                sequence += 1
                frame = CameraFrame(image, time.perf_counter(), sequence)
                with self._changed:
                    self._latest = frame
                    self._changed.notify_all()
        except Exception as error:
            with self._changed:
                self._error = str(error)
                self._changed.notify_all()
        finally:
            self.capture.release()

    def read_latest(self, after_sequence=-1, timeout=2.0):
        with self._changed:
            ready = self._changed.wait_for(
                lambda: self._error is not None or self._stop.is_set()
                or (self._latest is not None and self._latest.sequence > after_sequence), timeout)
            if self._error is not None:
                raise RuntimeError(self._error)
            if not ready or self._stop.is_set():
                raise RuntimeError("Timed out waiting for a fresh camera frame")
            return self._latest

    def close(self):
        self._stop.set()
        with self._changed:
            self._changed.notify_all()
        self._thread.join(timeout=2.0)
