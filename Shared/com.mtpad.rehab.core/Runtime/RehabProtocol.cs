using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MTPAD.Rehab.Core
{
    public static class RehabProtocol
    {
        public const string HeelStrikeCommand = "HEEL_STRIKE";
        public const string HeelStrikeAckCommand = "HEEL_STRIKE_ACK";
        public const string SyncRequestCommand = "SYNC_REQUEST";
        public const string SyncResponseCommand = "SYNC_RESPONSE";

        public static string EncodeHeelStrike(HeelStrikeEvent value)
        {
            return HeelStrikeCommand
                + " v=1"
                + " eventId=" + Quote(value.eventId)
                + " side=" + value.side.Token()
                + " trackerId=" + Quote(value.trackerId)
                + " trackerSample=" + value.trackerSample.ToString(CultureInfo.InvariantCulture)
                + " detectorSample=" + value.detectorSample.ToString(CultureInfo.InvariantCulture)
                + " trackerPc=" + F(value.trackerPcTime)
                + " peakPc=" + F(value.peakPcTime)
                + " detectedPc=" + F(value.detectedPcTime)
                + " sentPc=" + F(value.sentPcTime)
                + " rawPeak=" + F(value.rawPeakDegrees)
                + " filteredPeak=" + F(value.filteredPeakDegrees)
                + " prominence=" + F(value.prominenceDegrees)
                + " algorithm=" + Quote(value.algorithm)
                + " groundRevision=" + value.groundRevision.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryDecodeHeelStrike(string line, out HeelStrikeEvent value, out string error)
        {
            value = null;
            error = "";
            string command;
            Dictionary<string, string> fields;
            if (!TryParse(line, out command, out fields) || command != HeelStrikeCommand)
            {
                error = "not_heel_strike";
                return false;
            }

            int version;
            if (!TryInt(fields, "v", out version) || version != HeelStrikeEvent.ProtocolVersion)
            {
                error = "unsupported_version";
                return false;
            }

            FootSide side;
            if (!FootSideExtensions.TryParseToken(Get(fields, "side"), out side))
            {
                error = "invalid_side";
                return false;
            }

            string eventId = Get(fields, "eventId");
            if (string.IsNullOrWhiteSpace(eventId))
            {
                error = "missing_event_id";
                return false;
            }

            value = new HeelStrikeEvent
            {
                eventId = eventId,
                side = side,
                trackerId = Get(fields, "trackerId"),
                trackerSample = Long(fields, "trackerSample"),
                detectorSample = Long(fields, "detectorSample"),
                trackerPcTime = Double(fields, "trackerPc"),
                peakPcTime = Double(fields, "peakPc"),
                detectedPcTime = Double(fields, "detectedPc"),
                sentPcTime = Double(fields, "sentPc"),
                rawPeakDegrees = Float(fields, "rawPeak"),
                filteredPeakDegrees = Float(fields, "filteredPeak"),
                prominenceDegrees = Float(fields, "prominence"),
                algorithm = Get(fields, "algorithm"),
                groundRevision = Int(fields, "groundRevision")
            };
            return true;
        }

        public static string EncodeAck(HeelStrikeAck value)
        {
            return HeelStrikeAckCommand
                + " eventId=" + Quote(value.eventId)
                + " accepted=" + (value.accepted ? "1" : "0")
                + " headsetReceipt=" + F(value.headsetReceiptTime)
                + " headsetSpawn=" + F(value.headsetSpawnTime)
                + " reason=" + Quote(value.reason);
        }

        public static bool TryDecodeAck(string line, out HeelStrikeAck value)
        {
            value = null;
            string command;
            Dictionary<string, string> fields;
            if (!TryParse(line, out command, out fields) || command != HeelStrikeAckCommand) return false;
            value = new HeelStrikeAck
            {
                eventId = Get(fields, "eventId"),
                accepted = Get(fields, "accepted") == "1",
                headsetReceiptTime = Double(fields, "headsetReceipt"),
                headsetSpawnTime = Double(fields, "headsetSpawn"),
                reason = Get(fields, "reason")
            };
            return true;
        }

        public static string EncodeSyncRequest(long sequence, double pcSend)
        {
            return SyncRequestCommand + " seq=" + sequence.ToString(CultureInfo.InvariantCulture)
                + " pcSend=" + F(pcSend);
        }

        public static string EncodeSyncResponse(long sequence, double pcSend, double headsetReceipt, double headsetSend)
        {
            return SyncResponseCommand + " seq=" + sequence.ToString(CultureInfo.InvariantCulture)
                + " pcSend=" + F(pcSend)
                + " headsetReceipt=" + F(headsetReceipt)
                + " headsetSend=" + F(headsetSend);
        }

        public static bool TryParse(string line, out string command, out Dictionary<string, string> fields)
        {
            command = "";
            fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(line)) return false;

            int index = 0;
            SkipSpaces(line, ref index);
            int commandStart = index;
            while (index < line.Length && !char.IsWhiteSpace(line[index])) index++;
            command = line.Substring(commandStart, index - commandStart).ToUpperInvariant();

            while (index < line.Length)
            {
                SkipSpaces(line, ref index);
                if (index >= line.Length) break;
                int keyStart = index;
                while (index < line.Length && line[index] != '=' && !char.IsWhiteSpace(line[index])) index++;
                if (index >= line.Length || line[index] != '=')
                {
                    while (index < line.Length && !char.IsWhiteSpace(line[index])) index++;
                    continue;
                }

                string key = line.Substring(keyStart, index - keyStart);
                index++;
                string parsedValue;
                if (index < line.Length && line[index] == '"')
                {
                    index++;
                    var builder = new StringBuilder();
                    bool escape = false;
                    while (index < line.Length)
                    {
                        char c = line[index++];
                        if (escape)
                        {
                            builder.Append(c);
                            escape = false;
                        }
                        else if (c == '\\') escape = true;
                        else if (c == '"') break;
                        else builder.Append(c);
                    }
                    parsedValue = builder.ToString();
                }
                else
                {
                    int valueStart = index;
                    while (index < line.Length && !char.IsWhiteSpace(line[index])) index++;
                    parsedValue = line.Substring(valueStart, index - valueStart);
                }
                fields[key] = parsedValue;
            }
            return command.Length > 0;
        }

        public static string Get(Dictionary<string, string> fields, string key)
        {
            string value;
            return fields != null && fields.TryGetValue(key, out value) ? value : "";
        }

        public static double Double(Dictionary<string, string> fields, string key)
        {
            double value;
            return double.TryParse(Get(fields, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0d;
        }

        public static float Float(Dictionary<string, string> fields, string key)
        {
            float value;
            return float.TryParse(Get(fields, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0f;
        }

        public static long Long(Dictionary<string, string> fields, string key)
        {
            long value;
            return long.TryParse(Get(fields, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0L;
        }

        public static int Int(Dictionary<string, string> fields, string key)
        {
            int value;
            return int.TryParse(Get(fields, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static bool TryInt(Dictionary<string, string> fields, string key, out int value)
        {
            return int.TryParse(Get(fields, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string F(double value)
        {
            return value.ToString("F9", CultureInfo.InvariantCulture);
        }

        private static string F(float value)
        {
            return value.ToString("F6", CultureInfo.InvariantCulture);
        }

        private static void SkipSpaces(string value, ref int index)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
        }
    }
}
