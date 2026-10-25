using MTPAD.Rehab.Core;
using MTPAD.Rehab.Headset;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

[InitializeOnLoad]
public static class HeadsetTemplateSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Headset_Main.unity";
    private const string BallPrefabPath = "Assets/Prefabs/HeelStrikeBall.prefab";
    private const string XrSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

    static HeadsetTemplateSceneBuilder()
    {
        EditorApplication.delayCall += EnsureTemplate;
    }

    [MenuItem("mTPAD/Rebuild Headset Template Scene")]
    public static void RebuildTemplate()
    {
        BuildScene(true);
    }

    public static void BuildForAutomation()
    {
        BuildScene(true);
    }

    private static void EnsureTemplate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        ConfigureProject();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) BuildScene(false);
    }

    private static void BuildScene(bool replace)
    {
        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets", "Prefabs");
        if (!replace && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;

        GameObject ballPrefab = EnsureBallPrefab();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject xrRoot = new GameObject("XR and Passthrough");
        GameObject rig = CreateMetaRig(xrRoot.transform);
        OVRManager manager = rig.GetComponentInChildren<OVRManager>(true);
        OVRCameraRig cameraRig = rig.GetComponentInChildren<OVRCameraRig>(true);
        GameObject passthroughObject = new GameObject("Passthrough Layer");
        passthroughObject.transform.SetParent(xrRoot.transform, false);
        OVRPassthroughLayer passthrough = passthroughObject.AddComponent<OVRPassthroughLayer>();
        passthrough.overlayType = OVROverlay.OverlayType.Underlay;
        MetaPassthroughSetup metaSetup = xrRoot.AddComponent<MetaPassthroughSetup>();
        metaSetup.manager = manager;
        metaSetup.cameraRig = cameraRig;
        metaSetup.passthroughLayer = passthrough;

        GameObject root = new GameObject("RehabHeadsetSystem");
        HeadsetBootstrap bootstrap = root.AddComponent<HeadsetBootstrap>();
        RehabSessionContext session = Child<RehabSessionContext>(root.transform, "Session");
        TriggerConnectionServer connection = Child<TriggerConnectionServer>(root.transform, "Communication");

        GameObject calibrationRoot = new GameObject("Calibration");
        calibrationRoot.transform.SetParent(root.transform, false);
        CrossSpaceRegistration registration = Child<CrossSpaceRegistration>(calibrationRoot.transform, "CrossSpaceRegistration");
        CorridorFrame corridor = Child<CorridorFrame>(calibrationRoot.transform, "CorridorFrame");
        RomCalibration rom = Child<RomCalibration>(calibrationRoot.transform, "RomCalibration");

        RehabTaskHost taskHost = Child<RehabTaskHost>(root.transform, "TaskHost");
        GameObject ballTaskObject = new GameObject("HeelStrikeBallTask");
        ballTaskObject.transform.SetParent(taskHost.transform, false);
        HeelStrikeBallTask ballTask = ballTaskObject.AddComponent<HeelStrikeBallTask>();
        BallSpawnRegion region = ballTaskObject.AddComponent<BallSpawnRegion>();
        BallPresenter presenter = ballTaskObject.AddComponent<BallPresenter>();
        GameObject ballSpawnRoot = new GameObject("BallSpawnRoot");
        ballSpawnRoot.transform.SetParent(ballTaskObject.transform, false);
        presenter.ballPrefab = ballPrefab;
        presenter.ballParent = ballSpawnRoot.transform;
        ballTask.requireRomCalibration = false;

        HeadsetEventLogger logger = Child<HeadsetEventLogger>(root.transform, "EventLogger");
        HeadsetStatusPanel status = Child<HeadsetStatusPanel>(root.transform, "LiveStatus");

        bootstrap.session = session;
        bootstrap.metaSetup = metaSetup;
        bootstrap.connection = connection;
        bootstrap.crossSpaceRegistration = registration;
        bootstrap.corridor = corridor;
        bootstrap.romCalibration = rom;
        bootstrap.taskHost = taskHost;
        bootstrap.ballTask = ballTask;
        bootstrap.spawnRegion = region;
        bootstrap.ballPresenter = presenter;
        bootstrap.eventLogger = logger;
        bootstrap.statusPanel = status;
        bootstrap.ResolveAndWire();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        ConfigureProject();
        AssetDatabase.SaveAssets();
        Debug.Log("[mTPAD] Headset template scene created at " + ScenePath);
    }

    private static GameObject CreateMetaRig(Transform parent)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
        GameObject rig;
        if (source != null) rig = (GameObject)PrefabUtility.InstantiatePrefab(source);
        else
        {
            rig = new GameObject("Meta XR Rig");
            rig.AddComponent<OVRManager>();
            rig.AddComponent<OVRCameraRig>();
        }
        rig.name = "Meta XR Rig";
        rig.transform.SetParent(parent, false);
        OVRManager manager = rig.GetComponentInChildren<OVRManager>(true);
        if (manager != null)
        {
            manager.isInsightPassthroughEnabled = true;
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
        }
        return rig;
    }

    private static GameObject EnsureBallPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
        if (existing != null) return existing;
        GameObject temporary = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        temporary.name = "HeelStrikeBall";
        temporary.transform.localScale = Vector3.one;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary, BallPrefabPath);
        Object.DestroyImmediate(temporary);
        return prefab;
    }

    private static void ConfigureProject()
    {
        PlayerSettings.companyName = "ROAR Lab";
        PlayerSettings.productName = "mTPAD Headset Template";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.roarlab.mtpad.rehabtemplate");
        PlayerSettings.Android.forceInternetPermission = true;
        EnsureFolder("Assets", "XR");
        XRGeneralSettingsPerBuildTarget perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
        if (perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, XrSettingsPath);
        }
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        XRGeneralSettings general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        if (general != null) general.InitManagerOnStart = true;
        XRManagerSettings manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        if (manager != null)
            XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android);
        EditorBuildSettings.AddConfigObject("Unity.XR.Management.XRGeneralSettings", perTarget, true);
        AssetDatabase.SaveAssets();

        const string metaFeatureSetId = "com.meta.openxr.featureset.metaxr";
        var metaFeatureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(BuildTargetGroup.Android, metaFeatureSetId);
        if (metaFeatureSet != null)
        {
            metaFeatureSet.isEnabled = true;
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(BuildTargetGroup.Android);
        }

        OpenXRSettings openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (openXr != null)
        {
            OpenXRFeature[] features = openXr.GetFeatures<OpenXRFeature>();
            for (int i = 0; i < features.Length; i++)
            {
                if (features[i] != null && features[i].GetType().FullName == "Meta.XR.MetaXRFeature")
                {
                    features[i].enabled = true;
                    EditorUtility.SetDirty(features[i]);
                }
            }
        }
    }

    private static T Child<T>(Transform parent, string name) where T : Component
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.AddComponent<T>();
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }
}
