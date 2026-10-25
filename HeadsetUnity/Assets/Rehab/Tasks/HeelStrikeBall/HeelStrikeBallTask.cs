using System;
using System.Collections.Generic;
using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class HeelStrikeBallTask : MonoBehaviour, IRehabTask
    {
        public TriggerConnectionServer connection;
        public RomCalibration romCalibration;
        public BallSpawnRegion spawnRegion;
        public BallPresenter presenter;
        public HeadsetEventLogger eventLogger;

        [Header("Task")]
        public bool leftEnabled = true;
        public bool rightEnabled = true;
        public bool requireRomCalibration;
        public SpatialDifficulty difficulty = SpatialDifficulty.Easy;
        [Range(0f, 2f)] public float minimumSpawnIntervalSeconds = 0.20f;
        public bool replaceActiveBall = true;

        [Header("Live status")]
        [SerializeField] private bool running;
        [SerializeField] private int spawnCount;
        [SerializeField] private string lastEventId = "";
        [SerializeField] private string lastEventSide = "";
        [SerializeField] private string status = "Stopped";

        private readonly HashSet<string> processedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> processedOrder = new Queue<string>();
        private double lastAcceptedReceipt = double.NegativeInfinity;

        public string TaskId { get { return "HeelStrikeBall"; } }
        public bool IsRunning { get { return running; } }
        public int SpawnCount { get { return spawnCount; } }
        public string LastEventId { get { return lastEventId; } }
        public string LastEventSide { get { return lastEventSide; } }
        public string Status { get { return status; } }

        [ContextMenu("Start Ball Task")]
        public void StartTask()
        {
            running = true;
            spawnCount = 0;
            lastAcceptedReceipt = double.NegativeInfinity;
            processedIds.Clear();
            processedOrder.Clear();
            if (spawnRegion != null) spawnRegion.ResetRandom();
            status = "Running";
        }

        [ContextMenu("Stop Ball Task")]
        public void StopTask()
        {
            running = false;
            if (presenter != null) presenter.Clear("task_stopped");
            status = "Stopped";
        }

        public void HandleHeelStrike(HeelStrikeEvent heelStrike, double headsetReceiptTime)
        {
            lastEventId = heelStrike != null ? heelStrike.eventId : "";
            lastEventSide = heelStrike != null ? heelStrike.side.Token() : "";
            string rejection = Validate(heelStrike, headsetReceiptTime);
            if (!string.IsNullOrEmpty(rejection))
            {
                Finish(heelStrike, new BallSpawnRecord
                {
                    eventId = lastEventId,
                    side = heelStrike != null ? heelStrike.side : FootSide.Left,
                    headsetReceiptTime = headsetReceiptTime,
                    accepted = false,
                    reason = rejection
                }, "", 0f, 0f);
                status = "Rejected: " + rejection;
                return;
            }

            Vector3 position;
            string region;
            float yaw;
            float pitch;
            if (spawnRegion == null || !spawnRegion.TrySample(difficulty, out position, out region, out yaw, out pitch))
            {
                Finish(heelStrike, new BallSpawnRecord
                {
                    eventId = heelStrike.eventId,
                    side = heelStrike.side,
                    headsetReceiptTime = headsetReceiptTime,
                    accepted = false,
                    reason = "spawn_region_unavailable"
                }, "", 0f, 0f);
                status = "Rejected: spawn region unavailable";
                return;
            }

            double spawnTime = RehabClock.MonotonicSeconds;
            GameObject ball = presenter != null ? presenter.Spawn(position, heelStrike.eventId, spawnTime) : null;
            bool accepted = ball != null;
            var result = new BallSpawnRecord
            {
                eventId = heelStrike.eventId,
                side = heelStrike.side,
                headsetReceiptTime = headsetReceiptTime,
                headsetSpawnTime = spawnTime,
                region = region,
                position = position,
                accepted = accepted,
                reason = accepted ? "" : "presenter_unavailable"
            };
            if (accepted)
            {
                lastAcceptedReceipt = headsetReceiptTime;
                spawnCount++;
                status = "Spawned " + heelStrike.side.Token() + " event " + heelStrike.eventId;
            }
            Finish(heelStrike, result, region, yaw, pitch);
        }

        private string Validate(HeelStrikeEvent value, double receiptTime)
        {
            if (value == null) return "invalid_event";
            if (string.IsNullOrWhiteSpace(value.eventId)) return "missing_event_id";
            if (!Remember(value.eventId)) return "duplicate_event";
            if (!running) return "task_not_running";
            if ((value.side == FootSide.Left && !leftEnabled) || (value.side == FootSide.Right && !rightEnabled))
                return "side_disabled";
            if (requireRomCalibration && (romCalibration == null || !romCalibration.Calibrated))
                return "rom_calibration_unavailable";
            if (receiptTime - lastAcceptedReceipt < minimumSpawnIntervalSeconds)
                return "minimum_spawn_interval";
            if (presenter != null && presenter.ActiveBall != null && !replaceActiveBall)
                return "active_ball_present";
            return "";
        }

        private void Finish(HeelStrikeEvent source, BallSpawnRecord result, string region,
            float yawDegrees, float pitchDegrees)
        {
            if (eventLogger != null) eventLogger.Log(source, result, region, yawDegrees, pitchDegrees);
            if (connection != null)
            {
                connection.SendAck(new HeelStrikeAck
                {
                    eventId = result.eventId,
                    accepted = result.accepted,
                    reason = result.reason,
                    headsetReceiptTime = result.headsetReceiptTime,
                    headsetSpawnTime = result.headsetSpawnTime
                });
            }
        }

        private bool Remember(string eventId)
        {
            if (!processedIds.Add(eventId)) return false;
            processedOrder.Enqueue(eventId);
            while (processedOrder.Count > 512) processedIds.Remove(processedOrder.Dequeue());
            return true;
        }
    }
}
