using MTPAD.Rehab.Core;
using MTPAD.Rehab.Trigger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

[InitializeOnLoad]
public static class TriggerTemplateSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Trigger_Main.unity";
    private const string XrSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

    static TriggerTemplateSceneBuilder()
    {
        EditorApplication.delayCall += EnsureTemplate;
    }

    [MenuItem("mTPAD/Rebuild Trigger Template Scene")]
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
        if (!replace && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject root = new GameObject("TriggerSystem");
        TriggerBootstrap bootstrap = root.AddComponent<TriggerBootstrap>();

        RehabSessionContext session = Child<RehabSessionContext>(root.transform, "Session");
        UnityXrTrackerSource trackers = Child<UnityXrTrackerSource>(root.transform, "TrackerInput");
        GroundFrameCalibration ground = Child<GroundFrameCalibration>(root.transform, "GroundFrameCalibration");
        FootAngleEstimator angles = Child<FootAngleEstimator>(root.transform, "FootAngleEstimator");
        HeelStrikeDetector detector = Child<HeelStrikeDetector>(root.transform, "HeelStrikeDetector");
        HeadsetConnectionClient connection = Child<HeadsetConnectionClient>(root.transform, "HeadsetConnection");
        TrackerRecorder recorder = Child<TrackerRecorder>(root.transform, "TrackerRecorder");
        TriggerEventLogger eventLogger = Child<TriggerEventLogger>(root.transform, "EventLogger");
        HeelStrikeTriggerController controller = Child<HeelStrikeTriggerController>(root.transform, "HeelStrikeEventPublisher");
        TriggerStatusPanel status = Child<TriggerStatusPanel>(root.transform, "LiveAngleAndEventMonitor");

        bootstrap.session = session;
        bootstrap.trackerSource = trackers;
        bootstrap.groundFrame = ground;
        bootstrap.angleEstimator = angles;
        bootstrap.heelStrikeDetector = detector;
        bootstrap.headsetConnection = connection;
        bootstrap.trackerRecorder = recorder;
        bootstrap.eventLogger = eventLogger;
        bootstrap.triggerController = controller;
        bootstrap.statusPanel = status;
        bootstrap.ResolveAndWire();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        ConfigureProject();
        AssetDatabase.SaveAssets();
        Debug.Log("[mTPAD] Trigger template scene created at " + ScenePath);
    }

    private static void ConfigureProject()
    {
        PlayerSettings.companyName = "ROAR Lab";
        PlayerSettings.productName = "mTPAD Trigger Template";
        EnsureFolder("Assets", "XR");
        XRGeneralSettingsPerBuildTarget perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
        if (perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, XrSettingsPath);
        }
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
        XRGeneralSettings general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (general != null) general.InitManagerOnStart = true;
        XRManagerSettings manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (manager != null)
            XRPackageMetadataStore.AssignLoader(manager, "Unity.XR.OpenVR.OpenVRLoader", BuildTargetGroup.Standalone);
        EditorBuildSettings.AddConfigObject("Unity.XR.Management.XRGeneralSettings", perTarget, true);
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
