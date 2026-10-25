using System;
using UnityEngine;

namespace MTPAD.Rehab.Core
{
    public enum FootSide
    {
        Left,
        Right
    }

    public static class FootSideExtensions
    {
        public static string Token(this FootSide side)
        {
            return side == FootSide.Left ? "L" : "R";
        }

        public static bool TryParseToken(string value, out FootSide side)
        {
            if (string.Equals(value, "L", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase))
            {
                side = FootSide.Left;
                return true;
            }

            if (string.Equals(value, "R", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase))
            {
                side = FootSide.Right;
                return true;
            }

            side = FootSide.Left;
            return false;
        }
    }

    [Serializable]
    public struct TrackerPoseSample
    {
        public FootSide side;
        public string deviceId;
        public long sampleIndex;
        public double trackerTime;
        public Vector3 position;
        public Quaternion rotation;
        public bool valid;
    }

    [Serializable]
    public sealed class HeelStrikeEvent
    {
        public const int ProtocolVersion = 1;

        public string eventId = "";
        public FootSide side;
        public string trackerId = "";
        public long trackerSample;
        public long detectorSample;
        public double trackerPcTime;
        public double peakPcTime;
        public double detectedPcTime;
        public double sentPcTime;
        public float rawPeakDegrees;
        public float filteredPeakDegrees;
        public float prominenceDegrees;
        public string algorithm = "";
        public int groundRevision;
    }

    [Serializable]
    public sealed class HeelStrikeAck
    {
        public string eventId = "";
        public bool accepted;
        public string reason = "";
        public double headsetReceiptTime;
        public double headsetSpawnTime;
    }

    [Serializable]
    public sealed class BallSpawnRecord
    {
        public string eventId = "";
        public FootSide side;
        public double headsetReceiptTime;
        public double headsetSpawnTime;
        public string region = "";
        public Vector3 position;
        public bool accepted;
        public string reason = "";
    }
}
