using UnityEditor;
using UnityEditor.SceneManagement;

namespace YoutubeTools.Editor
{
    public static class YoutubeToolsSceneOpener
    {
        [MenuItem("SoraTools/YouTube Tools - Open Scene", false, 9)]
        public static void OpenScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene("Assets/Scenes/YoutubeTools.unity");
                YoutubeToolsSetup.SetupScene();
            }
        }
    }
}
