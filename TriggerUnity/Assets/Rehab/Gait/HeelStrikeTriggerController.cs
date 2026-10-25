using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class HeelStrikeTriggerController : MonoBehaviour
    {
        public HeelStrikeDetector detector;
        public HeadsetConnectionClient headsetConnection;
        public TriggerEventLogger eventLogger;

        [Header("Live status")]
        [SerializeField] private int sentCount;
        [SerializeField] private int unsentCount;
        [SerializeField] private string lastResult = "Waiting";

        public int SentCount { get { return sentCount; } }
        public int UnsentCount { get { return unsentCount; } }
        public string LastResult { get { return lastResult; } }

        private void OnEnable()
        {
            Subscribe(true);
        }

        private void OnDisable()
        {
            Subscribe(false);
        }

        public void RefreshSubscriptions()
        {
            Subscribe(false);
            Subscribe(true);
        }

        private void Subscribe(bool subscribe)
        {
            if (detector != null)
            {
                if (subscribe) detector.HeelStrikeDetected += HandleHeelStrike;
                else detector.HeelStrikeDetected -= HandleHeelStrike;
            }
            if (headsetConnection != null)
            {
                if (subscribe) headsetConnection.AckReceived += HandleAck;
                else headsetConnection.AckReceived -= HandleAck;
            }
        }

        private void HandleHeelStrike(HeelStrikeEvent value)
        {
            bool sent = headsetConnection != null && headsetConnection.SendHeelStrike(value);
            if (sent) sentCount++;
            else unsentCount++;
            lastResult = sent
                ? "Sent " + value.side.Token() + " " + value.eventId
                : "Detected but headset disconnected: " + value.eventId;
            if (eventLogger != null) eventLogger.LogDetection(value, sent);
        }

        private void HandleAck(HeelStrikeAck value)
        {
            lastResult = value.accepted
                ? "Headset spawned " + value.eventId
                : "Headset rejected " + value.eventId + ": " + value.reason;
            if (eventLogger != null) eventLogger.LogAck(value);
        }
    }
}
