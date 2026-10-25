using System;
using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    public enum SpatialDifficulty
    {
        Easy,
        Medium,
        Hard
    }

    [DisallowMultipleComponent]
    public sealed class BallSpawnRegion : MonoBehaviour
    {
        public MetaPassthroughSetup headsetSetup;
        public RomCalibration romCalibration;

        [Header("Placement")]
        [Range(0.25f, 5f)] public float distanceMeters = 1.5f;
        public float fallbackRomDegrees = 30f;

        [Header("Annular ROM fractions")]
        [Range(0f, 1f)] public float easyMinimum = 0.15f;
        [Range(0f, 1f)] public float easyMaximum = 0.25f;
        [Range(0f, 1f)] public float mediumMinimum = 0.30f;
        [Range(0f, 1f)] public float mediumMaximum = 0.50f;
        [Range(0f, 1f)] public float hardMinimum = 0.55f;
        [Range(0f, 1f)] public float hardMaximum = 0.75f;
        public int randomSeed = 20261015;

        private System.Random random;

        public void ResetRandom()
        {
            random = new System.Random(randomSeed);
        }

        public bool TrySample(SpatialDifficulty difficulty, out Vector3 position, out string region,
            out float yawDegrees, out float pitchDegrees)
        {
            position = Vector3.zero;
            region = difficulty.ToString();
            yawDegrees = 0f;
            pitchDegrees = 0f;
            Transform head = headsetSetup != null ? headsetSetup.HeadTransform : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return false;
            if (random == null) ResetRandom();

            float minimum;
            float maximum;
            GetBounds(difficulty, out minimum, out maximum);
            float lower = Mathf.Clamp01(Mathf.Min(minimum, maximum));
            float upper = Mathf.Clamp01(Mathf.Max(minimum, maximum));
            double u = random.NextDouble();
            float fraction = Mathf.Sqrt((float)(lower * lower + u * (upper * upper - lower * lower)));
            double theta = random.NextDouble() * Math.PI * 2d;
            float unitYaw = (float)Math.Cos(theta);
            float unitPitch = (float)Math.Sin(theta);

            float left = RomOrFallback(romCalibration != null ? romCalibration.LeftRotationDegrees : 0f);
            float right = RomOrFallback(romCalibration != null ? romCalibration.RightRotationDegrees : 0f);
            float flexion = RomOrFallback(romCalibration != null ? romCalibration.FlexionDegrees : 0f);
            float extension = RomOrFallback(romCalibration != null ? romCalibration.ExtensionDegrees : 0f);
            yawDegrees = unitYaw * (unitYaw < 0f ? left : right) * fraction;
            pitchDegrees = unitPitch * (unitPitch < 0f ? flexion : extension) * fraction;

            Quaternion baseRotation;
            if (romCalibration != null) baseRotation = romCalibration.GetPlacementBaseRotation();
            else
            {
                Vector3 planarForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                if (planarForward.sqrMagnitude < 0.0001f) planarForward = Vector3.forward;
                baseRotation = Quaternion.LookRotation(planarForward.normalized, Vector3.up);
            }
            Vector3 direction = baseRotation * Vector3.forward
                + baseRotation * Vector3.right * Mathf.Tan(yawDegrees * Mathf.Deg2Rad)
                + baseRotation * Vector3.up * Mathf.Tan(pitchDegrees * Mathf.Deg2Rad);
            position = head.position + direction.normalized * Mathf.Clamp(distanceMeters, 0.25f, 5f);
            region = difficulty + "_" + lower.ToString("F2") + "-" + upper.ToString("F2") + "ROM";
            return true;
        }

        private float RomOrFallback(float value)
        {
            return value > 0.1f ? value : fallbackRomDegrees;
        }

        private void GetBounds(SpatialDifficulty difficulty, out float minimum, out float maximum)
        {
            if (difficulty == SpatialDifficulty.Medium)
            {
                minimum = mediumMinimum;
                maximum = mediumMaximum;
            }
            else if (difficulty == SpatialDifficulty.Hard)
            {
                minimum = hardMinimum;
                maximum = hardMaximum;
            }
            else
            {
                minimum = easyMinimum;
                maximum = easyMaximum;
            }
        }
    }
}
