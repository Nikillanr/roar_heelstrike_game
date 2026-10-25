using System;
using System.Collections.Generic;
using System.Text;
using MTPAD.Rehab.Core;
using UnityEngine;
using UnityEngine.XR;

namespace MTPAD.Rehab.Trigger
{
    [DisallowMultipleComponent]
    public sealed class UnityXrTrackerSource : MonoBehaviour
    {
        [Header("Vive tracker assignment")]
        [Tooltip("A unique part of the left tracker serial number.")]
        public string leftSerialContains = "LHR-1AAD87ED";
        [Tooltip("A unique part of the right tracker serial number.")]
        public string rightSerialContains = "LHR-CC44460A";
        [Tooltip("Optional fallback when the runtime does not expose a serial number.")]
        public string leftNameContains = "";
        [Tooltip("Optional fallback when the runtime does not expose a serial number.")]
        public string rightNameContains = "";

        [Header("Live status")]
        [SerializeField] private bool leftValid;
        [SerializeField] private bool rightValid;
        [SerializeField] private string leftResolvedDevice = "Unassigned";
        [SerializeField] private string rightResolvedDevice = "Unassigned";
        [SerializeField, TextArea(3, 10)] private string discoveredDevices = "";

        private readonly List<InputDevice> devices = new List<InputDevice>();
        private InputDevice leftDevice;
        private InputDevice rightDevice;
        private TrackerPoseSample leftSample;
        private TrackerPoseSample rightSample;
        private long leftSequence;
        private long rightSequence;
        private float nextRefreshTime;

        public bool LeftValid { get { return leftValid; } }
        public bool RightValid { get { return rightValid; } }
        public string LeftResolvedDevice { get { return leftResolvedDevice; } }
        public string RightResolvedDevice { get { return rightResolvedDevice; } }
        public string DiscoveredDevices { get { return discoveredDevices; } }

        private void OnEnable()
        {
            InputDevices.deviceConnected += HandleDeviceChanged;
            InputDevices.deviceDisconnected += HandleDeviceChanged;
            RefreshDevices();
        }

        private void OnDisable()
        {
            InputDevices.deviceConnected -= HandleDeviceChanged;
            InputDevices.deviceDisconnected -= HandleDeviceChanged;
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextRefreshTime)
            {
                nextRefreshTime = Time.unscaledTime + 2f;
                RefreshDevices();
            }

            leftSample = ReadPose(leftDevice, FootSide.Left, ref leftSequence);
            rightSample = ReadPose(rightDevice, FootSide.Right, ref rightSequence);
            leftValid = leftSample.valid;
            rightValid = rightSample.valid;
        }

        public bool TryGetLatestPose(FootSide side, out TrackerPoseSample sample)
        {
            sample = side == FootSide.Left ? leftSample : rightSample;
            return sample.valid;
        }

        public TrackerPoseSample GetLatestPose(FootSide side)
        {
            return side == FootSide.Left ? leftSample : rightSample;
        }

        [ContextMenu("Refresh Tracker Devices")]
        public void RefreshDevices()
        {
            devices.Clear();
            InputDevices.GetDevices(devices);
            leftDevice = FindDevice(leftSerialContains, leftNameContains);
            rightDevice = FindDevice(rightSerialContains, rightNameContains);
            leftResolvedDevice = Describe(leftDevice);
            rightResolvedDevice = Describe(rightDevice);

            var text = new StringBuilder();
            for (int i = 0; i < devices.Count; i++)
            {
                InputDevice device = devices[i];
                if ((device.characteristics & InputDeviceCharacteristics.TrackedDevice) == 0) continue;
                if (text.Length > 0) text.AppendLine();
                text.Append(device.name).Append(" | ")
                    .Append(device.serialNumber).Append(" | ")
                    .Append(device.characteristics);
            }
            discoveredDevices = text.Length > 0 ? text.ToString() : "No XR tracked devices discovered.";
        }

        private InputDevice FindDevice(string serialToken, string nameToken)
        {
            for (int i = 0; i < devices.Count; i++)
            {
                InputDevice device = devices[i];
                if (!device.isValid || (device.characteristics & InputDeviceCharacteristics.TrackedDevice) == 0) continue;
                if (Contains(device.serialNumber, serialToken)) return device;
            }

            if (!string.IsNullOrWhiteSpace(nameToken))
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    InputDevice device = devices[i];
                    if (!device.isValid || (device.characteristics & InputDeviceCharacteristics.TrackedDevice) == 0) continue;
                    if (Contains(device.name, nameToken)) return device;
                }
            }
            return default(InputDevice);
        }

        private static TrackerPoseSample ReadPose(InputDevice device, FootSide side, ref long sequence)
        {
            sequence++;
            var sample = new TrackerPoseSample
            {
                side = side,
                deviceId = Describe(device),
                sampleIndex = sequence,
                trackerTime = RehabClock.MonotonicSeconds,
                rotation = Quaternion.identity,
                valid = false
            };
            if (!device.isValid) return sample;

            bool tracked;
            Vector3 position;
            Quaternion rotation;
            bool hasTrackedFlag = device.TryGetFeatureValue(CommonUsages.isTracked, out tracked);
            bool hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
            bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
            if ((hasTrackedFlag && !tracked) || !hasPosition || !hasRotation) return sample;

            sample.position = position;
            sample.rotation = rotation;
            sample.valid = true;
            return sample;
        }

        private static bool Contains(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(token)
                && !string.IsNullOrEmpty(value)
                && value.IndexOf(token.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Describe(InputDevice device)
        {
            if (!device.isValid) return "Unassigned";
            return string.IsNullOrWhiteSpace(device.serialNumber)
                ? device.name
                : device.serialNumber;
        }

        private void HandleDeviceChanged(InputDevice unused)
        {
            nextRefreshTime = 0f;
        }
    }
}
