# mTPAD Rehab Template

For the full inventory of additions, what each file does, and how it works, see [CHANGES.md](CHANGES.md).

For a source file map, local development setup without hardware, and hardware-PC handoff notes, see [PROJECT_GUIDE.md](PROJECT_GUIDE.md).

The webcam hand-state experiment, remaining validation, and Unity setup checklist are tracked in [VISION_FEATURE_PLAN.md](VISION_FEATURE_PLAN.md).

The separate vision desktop demo is implemented; see [VisionInput/README.md](VisionInput/README.md) for manual and webcam run commands. Open `HeadsetUnity/Assets/Scenes/Vision_DesktopDemo.unity` to try it after installing Unity.

To try the original heel-strike ball task on a laptop, stop Play mode and select **mTPAD > Heel Strike > Open Desktop Demo**, then press Play. Click the left/right heel-strike buttons (or focus the Game view and press L/R). Each simulated strike runs the original `HeelStrikeBallTask`, `BallSpawnRegion`, and `BallPresenter`: a blue ball appears, the next accepted strike replaces it, and it expires after 1.5 seconds. **Auto walk** alternates feet every 0.8 seconds; turn it off to watch the ball expire. Easy/Medium/Hard change the placement range. This scene uses synthetic events directly at the task boundary; it does not test Vive devices, foot-angle detection, TCP, or recording. No Python process or vision input is needed. The scene is `HeadsetUnity/Assets/Scenes/HeelStrike_DesktopDemo.unity`.

This folder contains two small Unity 2022.3.30f1 projects and one shared local package:

- `TriggerUnity`: reads Vive tracker poses, computes foot angle, detects heel strikes, records raw data, and sends events.
- `HeadsetUnity`: receives events, owns calibration and task routing, and presents the active headset task.
- `Shared/com.mtpad.rehab.core`: message contracts, clocks, session metadata, CSV writing, and the task interface used by both projects.

The copied `MetaTrigger` and `TryARVR2` projects remain reference implementations and are not runtime dependencies of these projects.

## First task

`HeelStrikeBallTask` spawns one presentation-only ball for each accepted heel strike. It has no torch, gaze, fill, or scoring interaction.

## Open in Unity Hub

Add these two folders as separate projects, both using Unity `2022.3.30f1`:

- `RehabTemplate/TriggerUnity`
- `RehabTemplate/HeadsetUnity`

Open `Assets/Scenes/Trigger_Main.unity` on the PC and `Assets/Scenes/Headset_Main.unity` for the Meta headset. The important systems appear as separate GameObjects and components in the Hierarchy and Inspector.

On the PC, configure the tracker serials under `TrackerInput` and the headset IP under `HeadsetConnection`. In Play mode, use the component context menus to capture the ground frame, capture flat-foot calibration, and start tracker recording. The live panel plots both foot angles and draws vertical heel-strike markers.

On the headset, `Communication` receives trigger events and `TaskHost/HeelStrikeBallTask` owns the game. A new heel strike replaces the active ball by default; the ball is presentation-only and expires after 1.5 seconds. Difficulty, lifetime, side enables, minimum interval, and ROM-region fractions are visible in the Inspector.

Raw tracker rows include invalid frames so tracker dropouts remain visible offline. Times named `trackerPc`, `detectedPc`, and `sentPc` are from the PC monotonic clock; headset receipt/spawn times are from the headset monotonic clock. Periodic synchronization messages estimate the clock offset and network round trip.

## Adding a task

Create a `MonoBehaviour` that implements `IRehabTask`, place it under `TaskHost` in the headset scene, and assign it as the active task. Networking, calibration, recording, and synchronization do not need to be rewritten.
