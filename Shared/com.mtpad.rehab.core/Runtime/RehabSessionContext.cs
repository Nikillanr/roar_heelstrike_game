using System;
using System.IO;
using UnityEngine;

namespace MTPAD.Rehab.Core
{
    [DisallowMultipleComponent]
    public sealed class RehabSessionContext : MonoBehaviour
    {
        [Header("Participant")]
        public string participantId = "P001";
        public string day = "Day1";
        public string session = "Session1";
        [TextArea] public string details = "";

        [Header("Recording")]
        [Tooltip("Leave blank to use Application.persistentDataPath/RehabRecordings.")]
        public string recordingRoot = "";

        public string GetSessionDirectory()
        {
            string root = string.IsNullOrWhiteSpace(recordingRoot)
                ? Path.Combine(Application.persistentDataPath, "RehabRecordings")
                : recordingRoot;
            return Path.Combine(root, Safe(participantId), Safe(day), Safe(session));
        }

        public string GetFilePath(string fileName)
        {
            return Path.Combine(GetSessionDirectory(), Safe(fileName));
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unspecified";
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Trim();
        }
    }
}
