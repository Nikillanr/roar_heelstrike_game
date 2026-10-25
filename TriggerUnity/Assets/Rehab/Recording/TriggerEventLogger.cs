using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class TriggerEventLogger : MonoBehaviour
    {
        public RehabSessionContext session;
        [SerializeField] private string outputFile = "";
        private RehabCsvLogger writer;

        public string OutputFile { get { return outputFile; } }

        private void OnDisable()
        {
            if (writer != null) writer.Dispose();
            writer = null;
        }

        public void LogDetection(HeelStrikeEvent value, bool sent)
        {
            EnsureWriter();
            if (writer == null || value == null) return;
            writer.WriteRow(RehabClock.UtcIso8601, "HEEL_STRIKE", value.eventId, value.side.Token(),
                value.trackerId, value.trackerSample, value.detectorSample, value.trackerPcTime,
                value.peakPcTime, value.detectedPcTime, value.sentPcTime, value.rawPeakDegrees,
                value.filteredPeakDegrees, value.prominenceDegrees, value.algorithm,
                value.groundRevision, sent ? 1 : 0, "", "", "");
        }

        public void LogAck(HeelStrikeAck value)
        {
            EnsureWriter();
            if (writer == null || value == null) return;
            writer.WriteRow(RehabClock.UtcIso8601, "HEEL_STRIKE_ACK", value.eventId, "", "", "", "", "",
                "", "", "", "", "", "", "", "", value.accepted ? 1 : 0,
                value.headsetReceiptTime, value.headsetSpawnTime, value.reason);
        }

        private void EnsureWriter()
        {
            if (writer != null || session == null) return;
            outputFile = session.GetFilePath("trigger_events.csv");
            writer = new RehabCsvLogger(outputFile,
                "utc", "recordType", "eventId", "side", "trackerId", "trackerSample", "detectorSample",
                "trackerPc", "peakPc", "detectedPc", "sentPc", "rawPeakDeg", "filteredPeakDeg",
                "prominenceDeg", "algorithm", "groundRevision", "acceptedOrSent",
                "headsetReceipt", "headsetSpawn", "reason");
        }
    }
}
