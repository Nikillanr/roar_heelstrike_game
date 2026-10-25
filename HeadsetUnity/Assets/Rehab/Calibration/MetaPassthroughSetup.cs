using UnityEngine;

namespace MTPAD.Rehab.Headset
{
    [DisallowMultipleComponent]
    public sealed class MetaPassthroughSetup : MonoBehaviour
    {
        public OVRManager manager;
        public OVRCameraRig cameraRig;
        public OVRPassthroughLayer passthroughLayer;
        public bool enablePassthrough = true;

        public Transform HeadTransform
        {
            get
            {
                if (cameraRig != null && cameraRig.centerEyeAnchor != null) return cameraRig.centerEyeAnchor;
                Camera main = Camera.main;
                return main != null ? main.transform : transform;
            }
        }

        private void Awake()
        {
            if (manager == null) manager = FindObjectOfType<OVRManager>();
            if (cameraRig == null) cameraRig = FindObjectOfType<OVRCameraRig>();
            if (passthroughLayer == null) passthroughLayer = FindObjectOfType<OVRPassthroughLayer>();
            if (manager != null)
            {
                manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
                manager.isInsightPassthroughEnabled = enablePassthrough;
            }
            if (passthroughLayer != null)
            {
                passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
                passthroughLayer.hidden = !enablePassthrough;
            }
            Camera main = Camera.main;
            if (main != null)
            {
                main.clearFlags = CameraClearFlags.SolidColor;
                main.backgroundColor = Color.clear;
            }
        }
    }
}
