using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Protobot.Editor
{
    /// <summary>
    /// Brings the original Protobot Rebuilt scene (Assets/ProtobotRebuilt, made for the built-in renderer) over to
    /// URP, keeping how it looks: its UI camera becomes an overlay in the main camera's stack (built-in drew it on
    /// top with "depth only"), the old post-processing and Easy Performant Outline components go (URP's camera
    /// anti-aliasing and Protobot.Rendering.OutlineFeature do their jobs), and the outline feature is on every
    /// renderer. Safe to run again.
    /// </summary>
    public static class ClassicSceneSetup
    {
        public const string ScenePath = "Assets/ProtobotRebuilt/Scenes/Main.unity";

        [MenuItem("Tools/Protobot/Setup Classic Scene")]
        public static void Run()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            var removed = 0;
            foreach (var go in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject))
            {
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                // By name: the original's code is in Assembly-CSharp, which this assembly can't reference.
                foreach (var outliner in go.GetComponents<MonoBehaviour>().Where(m => m != null && m.GetType().FullName == "EPOOutline.Outliner").ToList())
                {
                    Object.DestroyImmediate(outliner);
                    removed++;
                }
            }

            var cameras = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).ToList();
            var main = cameras.FirstOrDefault(c => c.name == "Main Camera");
            var ui = cameras.FirstOrDefault(c => c.name == "UICamera");
            if (main != null)
            {
                var mainData = main.GetUniversalAdditionalCameraData();
                mainData.renderType = CameraRenderType.Base;
                // The original drew in low dynamic range; URP's HDR buffer in Gamma space turns the bright,
                // see-through grid plane teal.
                main.allowHDR = false;
                EditorUtility.SetDirty(main);
                if (ui != null)
                {
                    var uiData = ui.GetUniversalAdditionalCameraData();
                    uiData.renderType = CameraRenderType.Overlay;
                    uiData.renderShadows = false;
                    var serialized = new SerializedObject(uiData);  // "Clear Depth" has no public setter
                    serialized.FindProperty("m_ClearDepth").boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    if (!mainData.cameraStack.Contains(ui))
                        mainData.cameraStack.Add(ui);
                    EditorUtility.SetDirty(uiData);
                }
                EditorUtility.SetDirty(mainData);
            }

            PlayerSetup.SetupOutline();

            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Classic] Scene set up for URP: removed {removed} old components; UI camera stacked on the main camera.");
        }
    }
}
