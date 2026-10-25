using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class HeadsetEventLogger : MonoBehaviour
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

        public void Log(HeelStrikeEvent source, BallSpawnRecord result, string region,
            float yawDegrees, float pitchDegrees)
        {
            EnsureWriter();
            if (writer == null || source == null || result == null) return;
            writer.WriteRow(RehabClock.UtcIso8601, source.eventId, source.side.Token(), source.trackerId,
                source.trackerSample, source.detectorSample, source.trackerPcTime, source.peakPcTime,
                source.detectedPcTime, source.sentPcTime, result.headsetReceiptTime,
                result.headsetSpawnTime, result.accepted ? 1 : 0, result.reason, region,
                yawDegrees, pitchDegrees, result.position.x, result.position.y, result.position.z);
        }

        private void EnsureWriter()
        {
            if (writer != null || session == null) return;
            outputFile = session.GetFilePath("headset_ball_events.csv");
            writer = new RehabCsvLogger(outputFile,
                "utc", "eventId", "side", "trackerId", "trackerSample", "detectorSample",
                "trackerPc", "peakPc", "detectedPc", "sentPc", "headsetReceipt", "headsetSpawn",
                "accepted", "reason", "region", "yawDeg", "pitchDeg", "ballX", "ballY", "ballZ");
        }
    }
}
