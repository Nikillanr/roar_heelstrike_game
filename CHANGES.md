# What changed

Added a **webcam-controlled vision experiment** and a **laptop simulator for the original heel-strike game**. Original Vive tracking, heel-strike logic, shared code, and main scenes were left intact.

## How it works

```text
Webcam → MediaPipe pose model → stable left/right hand states
       → Python TCP sender (port 7778) → Unity receiver → green balls
```

- **Left hand up:** ball appears randomly on the left. **Right hand up:** ball appears on the right.
- **Both up:** two balls. **Hold:** positions stay fixed. **Lower and raise again:** new position.
- Each hand has its own confidence display and tracking-loss handling. Stale/disconnected input hides both balls.
- The original Vive path stays on **port 7777**. Its blue ball is replaced on each heel strike and expires after **1.5 seconds**.

## New files: what each part does

Paths below are relative to `RehabTemplate/`.

| Location / files | Purpose and how it works |
| --- | --- |
| **`VisionInput/hand_state.py`** | Compares wrists against shoulders; confidence filtering and a short hold stabilize each hand's state. |
| `VisionInput/webcam_sender.py` | Captures webcam frames, runs MediaPipe, draws landmarks/state/confidence, and updates the sender. |
| `VisionInput/transport.py` | Sends both hand states together as JSON every 0.2 seconds; reconnects and replaces stale observations with `unknown`. |
| `VisionInput/manual_sender.py` | Sends simulated hand states from terminal commands, without a camera/model. |
| `VisionInput/download_model.py` | Downloads the pretrained pose model and verifies its checksum. |
| `VisionInput/requirements.txt`, `requirements-lock.txt` | Pin direct dependencies and the complete resolved Python environment. |
| `VisionInput/README.md` | Setup, run commands, and troubleshooting. |
| **`HeadsetUnity/Assets/Rehab/Vision/VisionStateReceiver.cs`** | Receives/validates TCP messages, exposes each hand's state/confidence, and detects stale input. |
| `…/Vision/VisionBallController.cs` | Reuses one sphere per hand; samples its screen-side position on each raise and controls visibility independently. |
| `…/Vision/VisionDesktopDemo.cs` | Creates a normal camera, receiver, controller, and diagnostics panel for laptop use. |
| **`HeadsetUnity/Assets/Rehab/Simulation/HeelStrikeDesktopDemo.cs`** | Buttons, L/R keys, or Auto walk inject simulated strikes directly into the original ball task. No trackers/Python needed. |
| `HeadsetUnity/Assets/Rehab/Editor/VisionExperimentMenu.cs` | Adds menu commands to open the vision demo or add inactive optional vision components to a scene. |
| `…/Editor/HeelStrikeDesktopMenu.cs` | Adds the menu command to open the heel-strike demo. |
| `HeadsetUnity/Assets/Scenes/Vision_DesktopDemo.unity`, `HeelStrike_DesktopDemo.unity` | Separate entry scenes for the two laptop demos. |
| `PROJECT_GUIDE.md`, `VISION_FEATURE_PLAN.md` | Original project overview and ongoing requirements/progress. |

All new Unity assets also have **`.meta` files**; keep them when copying changes.

## Setup, launch, and tests

Run these from the project root. PowerShell scripts are tools you invoke, not gameplay code that runs automatically.

| Script / supporting test file | What it does |
| --- | --- |
| `scripts/Check-Setup.ps1` | Checks Unity version, package paths, manifests, and scenes; installs nothing. |
| `scripts/Setup-Vision.ps1` | Prepares project-local Python, dependencies, and the model; leaves system PATH unchanged. |
| `scripts/Run-Vision.ps1` | Launches `-Mode webcam`, `-Mode manual`, or `-Mode tests`. Supports camera, hand, host, and port options. |
| `VisionInput/tests/test_vision.py` | Tests gesture stability, hand separation, confidence handling, encoding, heartbeats, and reconnects without a camera. |
| `scripts/Test-VisionReceiver.ps1` + `scripts/vision-tests/ReceiverHarness.cs` | Tests the real receiver over local TCP with lightweight Unity substitutes: framing, invalid messages, timeouts, and reconnects. |
| `scripts/Test-VisionUnity.ps1` + `scripts/vision-tests/VisionUnityChecks.cs` | Runs an isolated Unity Play-mode project: native JSON, left/right placement, stable holds, independent visibility, and recovery. |

**Try vision:** Unity → **mTPAD > Vision > Open Desktop Demo** → Play, then:

```powershell
.\scripts\Run-Vision.ps1 -Mode webcam
# Or manual controls: lu/ld = left up/down; ru/rd = right; q = quit
.\scripts\Run-Vision.ps1 -Mode manual
```

Run only one sender at a time. Restart Play mode and the sender after code changes.

**Try heel strikes:** Unity → **mTPAD > Heel Strike > Open Desktop Demo** → Play → click left/right or enable Auto walk. This tests the original task's response, not sensors, gait detection, networking, or recording.

## Other changes and status

- **Edited:** `README.md` (instructions/links), `.gitignore` (local environments, model, and caches excluded).
- **Local/generated:** `.tools/`, `VisionInput/models/`, Python caches, and Unity caches/logs. Setup recreates the runtime/model on another PC.
- **Unity setup rewrote settings:** `HeadsetUnity/ProjectSettings/{EditorBuildSettings,ProjectSettings}.asset` and `Assets/XR/Settings/OpenXRPackageSettings.asset`. Exact content differences are unavailable without an original Git baseline.
- **Verified:** 10 Python tests, 10 receiver checks, and 10 Unity Play-mode checks. User tested the laptop demos; per-hand confidence labels were subsequently restored.
- **Still needs hardware:** real Vive tracking/calibration, Quest rendering, and optional combined operation on the other PC.
