# ROAR heel-strike and vision demo

A webcam input experiment alongside the original Vive/Quest rehabilitation game. Raised hands control separate green balls; heel strikes control the original blue ball. The Vive code is unchanged. Both have laptop demos that need no headset or trackers.

## Run the vision demo

Requirements: **Windows, a webcam, and Unity 2022.3.30f1**. Android modules are only needed for a Quest build.

1. Clone the repository, keeping `HeadsetUnity`, `TriggerUnity`, and `Shared` together for the local package references.
2. Open **HeadsetUnity** through Unity Hub. Wait for compilation; use the Windows desktop build target.
3. Select **mTPAD > Vision > Open Desktop Demo**, then press **Play**.
4. From the repository root, run setup once, then start the webcam:

   ```powershell
   .\scripts\Setup-Vision.ps1
   .\scripts\Run-Vision.ps1 -Mode webcam -InferenceMode image
   ```

Setup downloads local Python 3.11, pinned dependencies, and Google's Pose Landmarker Lite model.

Left/right hands control balls on their matching sides; both raised shows two. Each new raise picks a random position; holding keeps it fixed; lowering hides it. The preview is mirrored, but labels refer to your anatomical hands.

Keep shoulders and wrists visible, including when lowering. `unknown` means unreliable tracking. Stop with Q/Esc or Ctrl+C. Run one sender at a time.

The preferred configuration is **image mode with zero hold**. Hold defaults to zero; select image mode explicitly. Add `-HoldSeconds 0.05` for flicker, `-InferenceMode video` to compare smoothing, or `-CameraIndex 1` for another camera.

## How it works

```text
Vive trackers -> TriggerUnity -> TCP 7777 -> HeadsetUnity heel-strike task
Webcam -> MediaPipe -> Python hand states -> TCP 7778 -> Unity vision balls
```

MediaPipe estimates body landmarks. Python compares wrists with shoulders, normalizes by shoulder width, and checks confidence. Separate up/down thresholds and an optional confirmation hold reduce jitter.

A capture thread keeps only the latest frame, processed at up to 640 pixels wide. Changes are sent immediately; unchanged states get 200 ms heartbeats. Unity hides confirmed lowered hands immediately, allows 150 ms grace for tracking loss, and hides both balls on stale/disconnected input. Saved components retain their Inspector grace value.

Vision messages are newline-delimited UTF-8 JSON: session/sequence IDs, per-hand and aggregate states/confidence, and a source timestamp. Unity rejects malformed/out-of-order input. Acknowledgments confirm controller visibility, not screen rendering. No camera images are transmitted or saved.

## Code map

| Location | What it does |
| --- | --- |
| [VisionInput/webcam_sender.py](VisionInput/webcam_sender.py) | Camera, MediaPipe inference, preview, and timing reports. |
| [VisionInput/hand_state.py](VisionInput/hand_state.py) | Landmark-to-state logic, confidence checks, thresholds, and optional hold. |
| [VisionInput/latest_frame.py](VisionInput/latest_frame.py) / [transport.py](VisionInput/transport.py) | Latest-frame capture; TCP messages, reconnects, heartbeats, and acknowledgments. |
| [VisionInput/benchmark_latency.py](VisionInput/benchmark_latency.py) | TCP/UDP comparison and separate camera, model, and Unity timing tests. |
| [HeadsetUnity/Assets/Rehab/Vision](HeadsetUnity/Assets/Rehab/Vision) | `VisionStateReceiver` validates input; `VisionBallController` places/shows balls; `VisionDesktopDemo` creates the laptop scene and diagnostics. |
| [HeadsetUnity/Assets/Rehab/Simulation](HeadsetUnity/Assets/Rehab/Simulation) | Simulated heel strikes passed into the original ball task. |
| [TriggerUnity/Assets/Rehab](TriggerUnity/Assets/Rehab) | Original tracker input, calibration, detection, networking, and recording. |
| [Shared/com.mtpad.rehab.core](Shared/com.mtpad.rehab.core) | Original event types, protocol, task interface, clocks, and CSV utilities. |
| [scripts](scripts) / [VisionInput/tests](VisionInput/tests) | Setup/launch helpers and automated tests. |

`download_model.py` verifies the model; requirements files pin dependencies. Local environments, models, reports, and caches are ignored by Git. Unity `.meta` files and project/package settings are required.

## Other ways to test

For manual input, keep the vision demo in Play and run:

```powershell
.\scripts\Run-Vision.ps1 -Mode manual
```

Commands: `lu`/`ld` left up/down, `ru`/`rd` right, `u`/`d` both, `x` tracking loss, `q` quit.

For heel strikes, stop Play, select **mTPAD > Heel Strike > Open Desktop Demo**, then Play. Buttons, L/R keys, or Auto walk inject synthetic strikes into the original task. Each strike replaces the blue ball; it expires after 1.5 seconds. This does not test trackers or networking.

## Latency changes and findings

Changes: newest-frame capture, smaller processing images, immediate sends, background Unity updates, shorter tracking-loss grace, and optional smoothing/hold removal.

November 6 laptop measurements:

| Measurement | Result | Scope |
| --- | --- | --- |
| TCP vs UDP echo, 200 samples | Median **0.055 vs 0.041 ms** | Same-PC Python sockets only. |
| Packet send -> Unity acknowledgment | Median **2-3 ms** | Controller update and return trip; excludes vision/rendering. |
| CPU model inference | About **24 ms video**, **53-68 ms image** | Varied between runs. |
| Physical hand -> visible ball | Roughly **0.5 seconds** | Visual estimate, not a formal measurement. |

TCP remains in use with `TCP_NODELAY`: the local UDP advantage was negligible. UDP is implemented only in the echo benchmark. **MediaPipe 0.10.21 Python rejects GPU delegation on Windows**, so CUDA was not benchmarked; its benefit with another runtime remains unknown.

Image mode with zero hold felt quicker despite slower inference. The sequential trials did not measure smoothing lag or establish a controlled end-to-end comparison.

Reproduce the TCP/UDP comparison without Unity or a camera:

```powershell
.\.tools\python311\python.exe .\VisionInput\benchmark_latency.py --stage transport --samples 200
```

Record a webcam trial, or stop other senders and test synthetic states through the updated Unity demo in Play:

```powershell
.\scripts\Run-Vision.ps1 -Mode webcam -InferenceMode image -MeasureSeconds 30
.\scripts\Run-Vision.ps1 -Mode benchmark -BenchmarkStage unity
```

Reports go under `.tools/latency/`. Software timings exclude camera buffering, initial hold, and display scanout. Monotonic timestamps are not subtracted across processes.

## Hardware setup

Main scenes: `TriggerUnity/Assets/Scenes/Trigger_Main.unity` on the tracker PC and `HeadsetUnity/Assets/Scenes/Headset_Main.unity` for Quest. Configure tracker serials/headset IP, ground/flat-foot calibration, and headset ROM. Original tracker/task CSV recording and port 7777 remain intact.

To add vision, select **mTPAD > Vision > Add Optional Vision Input To Current Scene** outside Play. Assign its camera/head anchor, enable the object, and save. Target port 7778 with `-HostName <UNITY_DEVICE_IP>` on a test LAN; the listener has no authentication. Quest builds need Android SDK/NDK/OpenJDK and Meta/OpenXR validation. Real Quest/Vive operation still needs hardware testing.

## Checks and troubleshooting

```powershell
.\scripts\Run-Vision.ps1 -Mode tests
.\scripts\Test-VisionReceiver.ps1
.\scripts\Test-VisionUnity.ps1
```

Passed: **15 Python tests, 11 receiver checks, 10 Unity Play-mode checks**. Unity checks use `.tools/UnityVisionChecks`; pass `-UnityEditorPath` for a nonstandard editor installation.

Waiting for Unity: check Play mode/other senders. `unknown`: check shoulder/wrist visibility and confidence. Camera error: close other camera apps/check desktop-app camera permissions. Restart sender and Play after code changes.
