using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sewer.EditorTools
{
    public static class SewerMenu
    {
        const string ScenePath = "Assets/Scenes/Sewer.unity";
        const string GenDir = "Assets/SewerGenerated";

        [MenuItem("Sewer/Create Game Scene", priority = 1)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("SewerGame").AddComponent<SewerGame>();
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.AddComponent<ThirdPersonCamera>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = EditorBuildSettings.scenes.ToList();
            list.RemoveAll(s => s.path == ScenePath);
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("Sewer scene created: " + ScenePath + " — press Play.");
        }

        [MenuItem("Sewer/Open Game Scene", priority = 2)]
        public static void OpenScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else CreateScene();
        }

        /// <summary>
        /// Builds the whole sewer into the open scene and saves meshes/materials as assets,
        /// so you can see it in the editor and place your own models by hand.
        /// </summary>
        [MenuItem("Sewer/Bake Level Into Scene", priority = 20)]
        public static void Bake()
        {
            var game = Object.FindFirstObjectByType<SewerGame>();
            if (game == null) { EditorUtility.DisplayDialog("Sewer", "Open the Sewer scene first (Sewer → Open Game Scene).", "OK"); return; }
            ClearBaked(game, false);
            SewerTextureSetup.FixImporters();

            MeshKit.Created.Clear();
            var lb = new LevelBuilder();
            var root = lb.Build(game.transform);
            PropDresser.Dress(lb, root);

            if (!AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.CreateFolder("Assets", "SewerGenerated");
            AssetDatabase.StartAssetEditing();
            try
            {
                int mi = 0, ma = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    if (m != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m)) && MeshKit.Created.Contains(m))
                        AssetDatabase.CreateAsset(m, $"{GenDir}/mesh_{mi++:0000}.asset");
                }
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var mat in r.sharedMaterials)
                        if (mat != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat)))
                            AssetDatabase.CreateAsset(mat, $"{GenDir}/mat_{ma++:000}_{Sanitize(mat.name)}.mat");
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            EditorSceneManager.SaveScene(game.gameObject.scene);
            Selection.activeObject = root.gameObject;
            Debug.Log("Sewer level baked into the scene. Put your own models anywhere under 'Level' or use SewerGame → Environment Prefabs.");
        }

        [MenuItem("Sewer/Clear Baked Level", priority = 21)]
        public static void Clear()
        {
            var game = Object.FindFirstObjectByType<SewerGame>();
            if (game != null) ClearBaked(game, true);
        }

        static void ClearBaked(SewerGame game, bool save)
        {
            var old = game.transform.Find("Level");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            if (AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.DeleteAsset(GenDir);
            if (save)
            {
                EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
                EditorSceneManager.SaveScene(game.gameObject.scene);
            }
        }

        static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }

    /// <summary>Makes sure the generated sewer textures import as tiling textures / normal maps.</summary>
    [InitializeOnLoad]
    public class SewerTextureSetup : AssetPostprocessor
    {
        const string Dir = "Assets/Resources/SewerTextures";

        static SewerTextureSetup() => EditorApplication.delayCall += FixImporters;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Dir)) return;
            Apply((TextureImporter)assetImporter, assetPath);
        }

        static bool Apply(TextureImporter ti, string path)
        {
            bool normal = Path.GetFileNameWithoutExtension(path).EndsWith("_n");
            bool changed = false;
            var wantType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != wantType) { ti.textureType = wantType; changed = true; }
            if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
            if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; changed = true; }
            if (ti.anisoLevel != 4) { ti.anisoLevel = 4; changed = true; }
            return changed;
        }

        public static void FixImporters()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(p) is TextureImporter ti && Apply(ti, p)) ti.SaveAndReimport();
            }
        }
    }
}
