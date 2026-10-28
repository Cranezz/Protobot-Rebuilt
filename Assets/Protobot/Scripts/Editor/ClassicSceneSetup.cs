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

            SetupOutline();

            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Classic] Scene set up for URP: removed {removed} old components; UI camera stacked on the main camera.");
        }

        const string RendererFolder = "Assets/Protobot/Settings/Rendering/";

        /// <summary>Adds the outline (Protobot.Rendering.OutlineFeature) to every URP renderer, once.</summary>
        internal static void SetupOutline()
        {
            var mask = Shader.Find("Hidden/Protobot/OutlineMask");
            var composite = Shader.Find("Hidden/Protobot/Outline");
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { RendererFolder }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                var feature = data.rendererFeatures.Find(f => f is Protobot.Rendering.OutlineFeature) as Protobot.Rendering.OutlineFeature;
                if (feature == null)
                {
                    // The same steps as the renderer's "Add Renderer Feature" button: a sub-asset, listed with its file id.
                    feature = ScriptableObject.CreateInstance<Protobot.Rendering.OutlineFeature>();
                    feature.name = "Outline";
                    AssetDatabase.AddObjectToAsset(feature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                    var so = new SerializedObject(data);
                    var features = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    features.arraySize++;
                    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                feature.SetShaders(mask, composite);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
