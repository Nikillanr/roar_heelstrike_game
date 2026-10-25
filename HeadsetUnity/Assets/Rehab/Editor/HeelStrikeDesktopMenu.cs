using UnityEditor;
using UnityEditor.SceneManagement;

public static class HeelStrikeDesktopMenu
{
    [MenuItem("mTPAD/Heel Strike/Open Desktop Demo")]
    public static void OpenDemo()
    {
        if (EditorApplication.isPlaying) return;
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene("Assets/Scenes/HeelStrike_DesktopDemo.unity");
    }
}
