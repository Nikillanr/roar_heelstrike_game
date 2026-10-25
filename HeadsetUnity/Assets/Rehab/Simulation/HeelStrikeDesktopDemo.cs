using System;
using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Headset.Simulation
{
    // Supplies synthetic detections to the original task; no Vive input or vision process is needed.
    public sealed class HeelStrikeDesktopDemo : MonoBehaviour
    {
        private HeelStrikeBallTask task;
        private BallPresenter presenter;
        private long sequence;
        private bool autoWalk;
        private bool nextLeft = true;
        private double nextStepAt;

        private void Awake()
        {
            var cameraObject = new GameObject("Desktop Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.07f);
            camera.nearClipPlane = 0.05f;

            var lightObject = new GameObject("Desktop Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;

            var taskObject = new GameObject("Original Heel Strike Ball Task");
            taskObject.transform.SetParent(transform, false);
            BallSpawnRegion region = taskObject.AddComponent<BallSpawnRegion>();
            presenter = taskObject.AddComponent<BallPresenter>();
            presenter.ballParent = taskObject.transform;
            task = taskObject.AddComponent<HeelStrikeBallTask>();
            task.spawnRegion = region;
            task.presenter = presenter;
            // Keep the original task/presenter defaults, including fallback ROM and 1.5-second expiry.
            task.StartTask();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.L)) Simulate(FootSide.Left);
            if (Input.GetKeyDown(KeyCode.R)) Simulate(FootSide.Right);
            if (autoWalk && RehabClock.MonotonicSeconds >= nextStepAt)
            {
                Simulate(nextLeft ? FootSide.Left : FootSide.Right);
                nextLeft = !nextLeft;
                nextStepAt = RehabClock.MonotonicSeconds + 0.8d;
            }
        }

        private void Simulate(FootSide side)
        {
            if (task == null) return;
            sequence++;
            double now = RehabClock.MonotonicSeconds;
            task.HandleHeelStrike(new HeelStrikeEvent
            {
                eventId = "SIM_" + Guid.NewGuid().ToString("N"),
                side = side,
                trackerId = "SIMULATED_" + side,
                trackerSample = sequence,
                detectorSample = sequence,
                trackerPcTime = now,
                peakPcTime = now,
                detectedPcTime = now,
                sentPcTime = now,
                rawPeakDegrees = 10f,
                filteredPeakDegrees = 10f,
                prominenceDegrees = 6f,
                algorithm = "DesktopSimulation"
            }, now);
        }

        private void OnGUI()
        {
            if (task == null) return;
            float width = Mathf.Min(700f, Screen.width - 32f);
            GUILayout.BeginArea(new Rect(16f, 16f, width, 125f), GUI.skin.box);
            GUILayout.Label("ORIGINAL HEEL-STRIKE GAME — LAPTOP SIMULATION");
            GUILayout.Label("Each accepted heel strike spawns a blue ball. A new strike replaces it.");
            GUILayout.Label("The ball expires after 1.5 seconds. There is no hit, gaze, or scoring mechanic.");
            GUILayout.Label("Accepted spawns: " + task.SpawnCount + " | Last foot: " + task.LastEventSide
                + " | Ball: " + (presenter.ActiveBall != null ? "visible" : "hidden"));
            GUILayout.Label(task.Status);
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(16f, Mathf.Max(150f, Screen.height - 125f), width, 110f), GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Left heel strike (L)", GUILayout.Height(32f))) Simulate(FootSide.Left);
            if (GUILayout.Button("Right heel strike (R)", GUILayout.Height(32f))) Simulate(FootSide.Right);
            GUILayout.EndHorizontal();
            autoWalk = GUILayout.Toggle(autoWalk, "Auto walk: alternate feet every 0.8 seconds (turn off to watch the ball expire)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Placement difficulty:");
            if (GUILayout.Button("Easy")) task.difficulty = SpatialDifficulty.Easy;
            if (GUILayout.Button("Medium")) task.difficulty = SpatialDifficulty.Medium;
            if (GUILayout.Button("Hard")) task.difficulty = SpatialDifficulty.Hard;
            GUILayout.Label(task.difficulty.ToString());
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void OnDisable()
        {
            autoWalk = false;
            if (task != null) task.StopTask();
        }
    }
}
