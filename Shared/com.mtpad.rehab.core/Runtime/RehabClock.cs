using System;
using System.Diagnostics;
using System.Globalization;

namespace MTPAD.Rehab.Core
{
    public static class RehabClock
    {
        private static readonly double TickToSeconds = 1.0 / Stopwatch.Frequency;

        public static double MonotonicSeconds
        {
            get { return Stopwatch.GetTimestamp() * TickToSeconds; }
        }

        public static string UtcIso8601
        {
            get { return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); }
        }
    }
}
