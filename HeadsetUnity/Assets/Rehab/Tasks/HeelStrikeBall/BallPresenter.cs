using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class BallPresenter : MonoBehaviour
    {
        public GameObject ballPrefab;
        public Transform ballParent;
        [Range(0.02f, 1f)] public float diameterMeters = 0.10f;
        [Range(0.1f, 30f)] public float lifetimeSeconds = 1.5f;
        public Color color = new Color(0.15f, 0.65f, 1f, 1f);

        [Header("Live status")]
        [SerializeField] private GameObject activeBall;
        [SerializeField] private string activeEventId = "";
        [SerializeField] private double spawnTime;

        public GameObject ActiveBall { get { return activeBall; } }
        public string ActiveEventId { get { return activeEventId; } }
        public double SpawnTime { get { return spawnTime; } }

        private void Update()
        {
            if (activeBall == null || lifetimeSeconds <= 0f) return;
            if (MTPAD.Rehab.Core.RehabClock.MonotonicSeconds - spawnTime >= lifetimeSeconds)
                Clear("lifetime_expired");
        }

        public GameObject Spawn(Vector3 position, string eventId, double headsetSpawnTime)
        {
            Clear("replaced");
            if (ballPrefab != null) activeBall = Instantiate(ballPrefab, ballParent);
            else
            {
                activeBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (ballParent != null) activeBall.transform.SetParent(ballParent, false);
            }
            activeBall.name = "HeelStrikeBall_" + Safe(eventId);
            activeBall.transform.position = position;
            activeBall.transform.localScale = Vector3.one * diameterMeters;
            activeBall.SetActive(true);
            Renderer renderer = activeBall.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                Material material = renderer.material;
                material.color = color;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            }
            activeEventId = eventId;
            spawnTime = headsetSpawnTime;
            return activeBall;
        }

        public void Clear(string reason)
        {
            if (activeBall != null) Destroy(activeBall);
            activeBall = null;
            activeEventId = "";
            spawnTime = 0d;
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Unknown";
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }
    }
}
