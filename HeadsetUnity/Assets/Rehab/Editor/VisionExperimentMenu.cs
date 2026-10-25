using MTPAD.Rehab.Headset.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VisionExperimentMenu
{
    [MenuItem("mTPAD/Vision/Open Desktop Demo")]
    public static void OpenDemo()
    {
        if (EditorApplication.isPlaying) return;
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene("Assets/Scenes/Vision_DesktopDemo.unity");
    }

    [MenuItem("mTPAD/Vision/Add Optional Vision Input To Current Scene")]
    public static void AddVisionInput()
    {
        if (EditorApplication.isPlaying) return;
        var root = new GameObject("Optional Vision Input");
        root.SetActive(false);
        Undo.RegisterCreatedObjectUndo(root, "Add optional vision input");
        var receiver = root.AddComponent<VisionStateReceiver>();
        receiver.bindAddress = "0.0.0.0";
        var controller = root.AddComponent<VisionBallController>();
        controller.receiver = receiver;
        // A serialized material reference keeps this shader available in a Quest build.
        const string materialPath = "Assets/Rehab/Vision/VisionBall.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Unlit/Color"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        controller.materialTemplate = material;
        controller.anchor = Camera.main != null ? Camera.main.transform : null;
        controller.localPosition = new Vector3(0.35f, 0f, 1.5f);
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[Vision] Added inactive input on port 7778. Assign the head/camera anchor, then enable this object to receive LAN vision input.");
    }
}
