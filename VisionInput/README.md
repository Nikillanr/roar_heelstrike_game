# Webcam hand state → Unity vision ball

Raise your left hand to show a green ball at a random location on the **left** side of the Unity view; raise your right hand for a ball on the **right**. Both raised shows two balls. Lowering a hand hides only its ball. Each new raise picks a new location; holding a hand up keeps its ball still. Left/right refer to your anatomical hands, even though the webcam preview is mirrored. The existing Vive heel-strike ball remains independent. The desktop experiment needs no Quest, SteamVR, or Vive trackers.

## Start here on this PC

Run commands from the `RehabTemplate` directory. A project-local Python runtime, dependencies, and model have been prepared under `.tools/` and `VisionInput/models/`. They are ignored by version control. They do not change PATH or register a system Python installation.

1. Finish installing Unity **2022.3.30f1** through Hub.
2. Add/open the **`HeadsetUnity`** project, leaving `Shared` beside it. Wait for package import and compilation.
3. Open **`Assets/Scenes/Vision_DesktopDemo.unity`**, or use **mTPAD > Vision > Open Desktop Demo**.
4. Press **Play**. The Game view should show diagnostics against a dark background, with the ball initially hidden. If the Game view is not visible, open **Window > General > Game**.
5. First try the manual sender:

```powershell
.\scripts\Run-Vision.ps1 -Mode manual
```

Enter `lu` then Enter to raise the left hand; `ld` lowers it. Use `ru` / `rd` for the right. `u` raises both, `d` lowers both, `x` simulates tracking loss, `s` prints connection status, and `q` exits. Holding a raised state keeps the same ball at the same location. Lower and raise it again to get a new random location on its side. This checks Unity and communication independently of camera inference.

6. Quit the manual sender before starting the webcam sender:

```powershell
.\scripts\Run-Vision.ps1 -Mode webcam
# Alternate camera or one specific anatomical hand:
.\scripts\Run-Vision.ps1 -Mode webcam -CameraIndex 1 -Hand right
```

A separate preview displays arm landmarks, each hand's detected state, and connection status. Keep your shoulders and wrists in frame and use one participant. Raise a wrist above its shoulder and hold briefly. Lower it below shoulder level to hide its ball. Default mode is **either hand**, tracking both independently; `-Hand right` or `-Hand left` enables only that side. `Q` or Esc closes the preview. Restart any previously running Python sender after updating the code.

The preview is mirrored for convenience; left/right refer to your anatomical hand. No camera video is saved or transmitted. Python performs inference locally and sends only state, confidence, sequence/session identifiers, and timestamps to Unity.

The vision controller reuses one sphere per hand, has no 1.5-second expiry, and never calls the heel-strike presenter. Tracking loss shows `unknown` and hides that side after a short grace period. A disconnected or stale connection hides both. Inspector viewport ranges control the random placement: the left horizontal interval is mirrored for the right side. Viewport coordinates keep placement relative to the camera's screen rather than world left/right.

Both hand states are sent atomically in version 2 `hand_state` messages, with `leftState`, `rightState`, `leftConfidence`, and `rightConfidence` fields alongside the existing aggregate state. Version 1 senders remain accepted; an old `either` message controls both balls because it carries no information about which hand was raised. Use the updated webcam sender for independent sides.

## Setup on another Windows x64 PC

Copy the project sources, then run:

```powershell
.\scripts\Setup-Vision.ps1
```

This downloads Python's embeddable 3.11.9 runtime, bootstraps pip, installs the pinned vision dependencies, and downloads Google's versioned Pose Landmarker Lite task bundle. Downloads require internet access; inference itself is local. `.tools/` and `VisionInput/models/` do not travel with a source-only copy.

Alternatively, use a regular **64-bit Python 3.11** installation and a virtual environment:

```powershell
cd VisionInput
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe download_model.py
.\.venv\Scripts\python.exe manual_sender.py
# Quit manual sender before webcam:
.\.venv\Scripts\python.exe webcam_sender.py --camera 0 --hand either
```

The experiment uses MediaPipe's current Tasks API rather than its legacy `solutions.pose` interface. The initial package versions are MediaPipe 0.10.21, contrib OpenCV 4.11.0.86, and NumPy 1.26.4. `requirements-lock.txt` records the complete resolved Windows Python 3.11 environment when available. Use only the contrib OpenCV package in this environment; multiple cv2 distributions can conflict.

References: [MediaPipe Pose Landmarker Python guide](https://developers.google.com/edge/mediapipe/solutions/vision/pose_landmarker/python), [MediaPipe 0.10.21 package](https://pypi.org/project/mediapipe/0.10.21/), [OpenCV package guidance](https://pypi.org/project/opencv-contrib-python/4.11.0.86/).

## Optional headset integration on the hardware PC

The existing main scenes and bootstrap code are unchanged. The new components can be added without replacing the heel-strike task:

1. Open `Headset_Main.unity` outside Play mode.
2. Use **mTPAD > Vision > Add Optional Vision Input To Current Scene**. This adds an **inactive** `Optional Vision Input` object. Review/save the scene change yourself.
3. On its `VisionBallController`, set **Anchor** to the headset camera/center-eye transform and choose a local position. The menu starts with `(0.35, 0, 1.5)` so the green ball is offset from the center.
4. The receiver starts with bind address `0.0.0.0` and port **7778** for LAN input. Enable the object for the experiment. The existing Vive connection stays on **7777**.
5. Run Python on the camera PC, targeting the device running Unity:

```powershell
.\scripts\Run-Vision.ps1 -Mode webcam -HostName <UNITY_DEVICE_IP>
```

For a Windows Unity editor, use that PC's LAN IP. For a standalone Quest app, use the Quest IP and rebuild the APK with the optional object enabled. Ensure the LAN/firewall allows this connection. The server supports one vision sender at a time, so do not run manual and webcam senders together. This experimental listener has no authentication; keep it on the test LAN.

Keep both balls visible and lower your hand: only the green vision ball should disappear. Quest rendering and simultaneous Vive input still require hardware validation on the other PC.

## Diagnostics and tests

```powershell
.\scripts\Run-Vision.ps1 -Mode tests
.\scripts\Test-VisionReceiver.ps1
.\scripts\Test-VisionUnity.ps1
```

The Python suite tests dwell time, hysteresis, confidence/tracking loss, anatomical hand separation, per-hand encoding, hand selection, heartbeats, stale observations, and reconnects. The C# receiver harness tests receiver source over loopback TCP using minimal Unity API stubs and a .NET JSON adapter. It checks split/coalesced lines, invalid messages, ordering, disconnects, oversized input, timeouts, and socket reuse.

`Test-VisionUnity.ps1` uses the installed Unity editor to run actual Play-mode checks in an isolated project under `.tools/UnityVisionChecks`. It leaves the user's open project alone and verifies native JSON parsing, left/right viewport regions, two-ball visibility, position stability while holding, new positions after re-raising, independent tracking loss, timeouts, and reconnect reset. These headless checks do not validate webcam quality or Quest rendering.

Troubleshooting:

- **Unity compilation errors:** check Console before Play. Use the pinned editor and preserve the `Shared` folder layout.
- **Cannot listen / port occupied:** stop the other Unity demo or receiver. Ports are configurable.
- **Waiting for Unity:** press Play in the desktop demo. Confirm `127.0.0.1:7778` locally and quit any other sender using it.
- **Camera cannot open:** close other camera apps, check Windows camera access for desktop apps, and try `-CameraIndex 1`.
- **Always unknown:** show both shoulders and the selected wrist(s), improve lighting, and avoid multiple people. Pose inference is not yet calibrated for your room/camera.
- **Python missing after copying:** run Setup-Vision again. The local runtime and model are intentionally excluded from source control.
- **PowerShell blocks scripts:** run the same command with `powershell -ExecutionPolicy Bypass -File .\scripts\Run-Vision.ps1 -Mode webcam`. This applies only to that process; no persistent execution-policy change is required.

See `../VISION_FEATURE_PLAN.md` for the decision history, remaining checks, and handoff context.
