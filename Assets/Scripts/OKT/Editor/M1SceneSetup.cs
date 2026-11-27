// Creates Assets/Scenes/M1_KartLapsSurface.unity (one RaceBootstrap object with
// the track JSON assigned) the first time scripts compile, and offers it from
// the menu: OKT > Open M1 Scene. Also writes the spec §3 physics settings
// (fixed timestep, solver iterations) into the project settings.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    [InitializeOnLoad]
    public static class M1SceneSetup
    {
        public const string ScenePath = "Assets/Scenes/M1_KartLapsSurface.unity";
        // v3 = Franklin's 2026-11-14 geometry/barrier rulings (Tools/build_track_v3.py from v2).
        public const string JsonPath = "Assets/Data/test-track-one-v5.json";

        static M1SceneSetup()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                ApplyProjectPhysics();
                try
                {
                    if (!File.Exists(ScenePath)) CreateScene(false);
                    else if (!SceneUsesCurrentJson())
                    {
                        var open = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
                        if (open.IsValid() && open.isLoaded)
                            Debug.LogWarning("[OKT] The M1 scene still points at the old track JSON. Use OKT > Rebuild M1 Scene.");
                        else { CreateScene(true); Debug.Log("[OKT] M1 scene re-wired to " + JsonPath); }
                    }
                }
                catch (System.Exception e) { Debug.LogWarning("[OKT] Couldn't auto-create the M1 scene (" + e.Message + "). Use OKT > Open M1 Scene."); }
            };
        }

        static bool SceneUsesCurrentJson()
        {
            string guid = AssetDatabase.AssetPathToGUID(JsonPath);
            return !string.IsNullOrEmpty(guid) && File.ReadAllText(ScenePath).Contains(guid);
        }

        public static void ApplyProjectPhysics()
        {
            if (Mathf.Abs(Time.fixedDeltaTime - World.FixedDt) > 1e-6f) Time.fixedDeltaTime = World.FixedDt;
            if (Physics.defaultSolverIterations != 4) Physics.defaultSolverIterations = 4;
            if (Physics.defaultSolverVelocityIterations != 8) Physics.defaultSolverVelocityIterations = 8;
        }

        [MenuItem("OKT/Open M1 Scene")]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) CreateScene(false);
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("OKT/Rebuild M1 Scene")]
        public static void Rebuild() => CreateScene(true);

        public static void CreateScene(bool overwrite)
        {
            if (File.Exists(ScenePath) && !overwrite) return;
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (json == null) { Debug.LogError("[OKT] Missing " + JsonPath); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = new GameObject("RaceBootstrap");
            var boot = go.AddComponent<RaceBootstrap>();
            boot.trackJson = json;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath))
            {
                list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
            AssetDatabase.Refresh();
            Debug.Log("[OKT] Created " + ScenePath + " — open it (OKT > Open M1 Scene) and press Play.");
        }
    }
}
