using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MTPAD.Rehab.Core
{
    public sealed class RehabCsvLogger : IDisposable
    {
        private readonly object gate = new object();
        private StreamWriter writer;

        public string FilePath { get; private set; }

        public RehabCsvLogger(string filePath, params string[] header)
        {
            FilePath = filePath;
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            bool writeHeader = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;
            writer = new StreamWriter(filePath, true, new UTF8Encoding(false));
            writer.AutoFlush = true;
            if (writeHeader && header != null) WriteRow(header);
        }

        public void WriteRow(params object[] values)
        {
            lock (gate)
            {
                if (writer == null) return;
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0) writer.Write(',');
                    writer.Write(Escape(Format(values[i])));
                }
                writer.WriteLine();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (writer == null) return;
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
        }

        private static string Format(object value)
        {
            if (value == null) return "";
            var formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        private static string Escape(string value)
        {
            value = value ?? "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
