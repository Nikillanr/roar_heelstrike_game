using MTPAD.Rehab.Core;
using UnityEngine;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class TrackerRecorder : MonoBehaviour
    {
        public RehabSessionContext session;
        public UnityXrTrackerSource trackerSource;
        public GroundFrameCalibration groundFrame;
        public bool recordOnStart;

        [Header("Live status")]
        [SerializeField] private bool recording;
        [SerializeField] private long rowsWritten;
        [SerializeField] private string outputFile = "";

        private RehabCsvLogger writer;
        private long lastLeftSample;
        private long lastRightSample;

        public bool Recording { get { return recording; } }
        public long RowsWritten { get { return rowsWritten; } }
        public string OutputFile { get { return outputFile; } }

        private void Start()
        {
            if (recordOnStart) StartRecording();
        }

        private void Update()
        {
            if (!recording || trackerSource == null || writer == null) return;
            WriteLatest(FootSide.Left, ref lastLeftSample);
            WriteLatest(FootSide.Right, ref lastRightSample);
        }

        private void OnDisable()
        {
            StopRecording();
        }

        [ContextMenu("Start Tracker Recording")]
        public void StartRecording()
        {
            if (recording) return;
            if (session == null)
            {
                Debug.LogError("[TrackerRecorder] SessionContext is not assigned.", this);
                return;
            }
            outputFile = session.GetFilePath("tracker_raw.csv");
            writer = new RehabCsvLogger(outputFile,
                "utc", "trackerTime", "side", "trackerId", "sample", "valid",
                "rawX", "rawY", "rawZ", "rawQx", "rawQy", "rawQz", "rawQw", "groundRevision");
            recording = true;
            rowsWritten = 0;
            lastLeftSample = 0;
            lastRightSample = 0;
        }

        [ContextMenu("Stop Tracker Recording")]
        public void StopRecording()
        {
            recording = false;
            if (writer != null) writer.Dispose();
            writer = null;
        }

        private void WriteLatest(FootSide side, ref long lastSample)
        {
            TrackerPoseSample sample = trackerSource.GetLatestPose(side);
            if (sample.sampleIndex <= 0 || sample.sampleIndex == lastSample) return;
            lastSample = sample.sampleIndex;
            writer.WriteRow(RehabClock.UtcIso8601, sample.trackerTime, side.Token(), sample.deviceId,
                sample.sampleIndex, sample.valid ? 1 : 0,
                sample.position.x, sample.position.y, sample.position.z,
                sample.rotation.x, sample.rotation.y, sample.rotation.z, sample.rotation.w,
                groundFrame != null ? groundFrame.Revision : 0);
            rowsWritten++;
        }
    }
}
