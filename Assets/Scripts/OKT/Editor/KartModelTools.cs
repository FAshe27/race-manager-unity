// The Blender kart (Kart Parts/exports/OKT_Kart.fbx) as the karts' look: inspection and still renders, run headless.
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class KartModelTools
    {
        const string ModelPath = "Assets/Resources/KartModel/OKT_Kart.fbx";

        /// <summary>Log the imported model's hierarchy: world positions in inches and the materials.</summary>
        public static void Inspect()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefab == null) { Debug.LogError("KMT no model at " + ModelPath); return; }
            var go = Object.Instantiate(prefab);
            var sb = new StringBuilder();
            void Walk(Transform t, int d)
            {
                var p = t.position / 0.0254f; var r = t.GetComponent<Renderer>();
                sb.AppendLine($"KMT {new string(' ', 2 * d)}{t.name} at ({p.x:F2},{p.y:F2},{p.z:F2}) rot {t.rotation.eulerAngles} active {t.gameObject.activeSelf}" +
                    (r != null ? $" mats [{string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m == null ? "null" : m.name + "/" + m.shader.name))}] bounds {r.bounds.min / 0.0254f}..{r.bounds.max / 0.0254f}" : ""));
                foreach (Transform c in t) Walk(c, d + 1);
            }
            Walk(go.transform, 0);
            Debug.Log(sb.ToString());
            Object.DestroyImmediate(go);
        }
    
        /// <summary>Still renders of one kart (and a three-kart row) in a plain studio: -okt-out folder. Run without -nographics.</summary>
        public static void Render()
        {
            string outDir = Arg("-okt-out", "Renders");
            Directory.CreateDirectory(outDir);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 4f;
            ground.GetComponent<Renderer>().sharedMaterial = Materials.Solid(new Color(0.55f, 0.38f, 0.27f), 0.1f);
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            var colors = new[] { new Color(0.85f, 0.1f, 0.1f), new Color(0.1f, 0.35f, 0.85f), new Color(0.95f, 0.75f, 0.1f) };
            KartPhysics Make(int n, Color c, Vector3 at)
            {
                var kp = KartFactory.Create("Kart " + n, n, c, new KartSetup(), null);
                kp.transform.position = at; kp.Body.isKinematic = true; kp.UpdateVisuals(0f);
                return kp;
            }
            var one = Make(14, colors[0], Vector3.zero);
            var cam = new GameObject("Cam").AddComponent<Camera>(); cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.72f, 0.66f, 0.6f);
            var c0 = new Vector3(0f, 0.3f, 0f);
            void Shot(string name, Vector3 from, Vector3 look, int w = 1600, int h = 1000)
            {
                cam.transform.position = from; cam.transform.LookAt(look);
                var rt = new RenderTexture(w, h, 24) { antiAliasing = 8 };
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                Debug.Log("KMT rendered " + name);
            }
            Shot("0-warmup", c0 + new Vector3(2.3f, 1.4f, 2.6f), c0);
            Shot("1-three-quarter-front", c0 + new Vector3(2.3f, 1.4f, 2.6f), c0);
            Shot("2-side-right", c0 + new Vector3(4.2f, 0.25f, 0f), c0);
            Shot("3-top", c0 + new Vector3(0f, 5.2f, 0.01f), c0);
            Shot("4-three-quarter-rear", c0 + new Vector3(-2.3f, 1.4f, -2.6f), c0);
            Shot("7-side-left", c0 + new Vector3(-4.2f, 0.25f, 0f), c0);
            Shot("8-rear", c0 + new Vector3(0f, 0.5f, -4.2f), c0);
            // hands on the wheel, steering right and left (driver's-eye view from behind and above)
            var link = one.GetComponent<SteeringLinkage>();
            Debug.Log("KMT steering linkage " + (link != null));
            foreach (var st in new[] { 30f, -30f, -40f })   // -40: the game's full lock, left
            {
                one.steerDeg = st; one.UpdateVisuals(0f); if (link != null) { link.Pose(st); Debug.Log($"KMT front wheels {st:+0;-0} deg: steering wheel {link.ShaftDeg:+0.0;-0.0} deg"); }
                // the editor doesn't re-skin outside play mode: draw a baked copy of the posed driver for the shot
                var smr = one.GetComponentInChildren<SkinnedMeshRenderer>(); GameObject baked = null;
                if (smr != null)
                {
                    var bm = new Mesh(); smr.BakeMesh(bm, true);
                    baked = new GameObject("BakedDriver"); baked.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    baked.AddComponent<MeshFilter>().sharedMesh = bm; baked.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                    smr.enabled = false;
                }
                string sfx = st < -35f ? "-full-lock" : "";
                Shot((st > 0 ? "9-steer-right-30" : "10-steer-left-30") + sfx, c0 + new Vector3(0.5f, 1.1f, -1.4f), c0 + new Vector3(0.05f, 0.1f, 0.35f));
                Shot((st > 0 ? "11-steer-right-30-front" : "12-steer-left-30-front") + sfx, c0 + new Vector3(-0.4f, 0.9f, 2.0f), c0 + new Vector3(-0.1f, 0.2f, 0.1f));
                // the front end from above: spindles, tie rods and the pitman tabs turning together
                Shot((st > 0 ? "13-front-end-right-30" : "14-front-end-left-30") + sfx, c0 + new Vector3(0.05f, 1.6f, 0.75f), c0 + new Vector3(0.02f, 0.05f, 0.5f));
                if (baked != null) { Object.DestroyImmediate(baked); smr.enabled = true; }
            }
            one.steerDeg = 0f; one.UpdateVisuals(0f); if (link != null) link.Pose(0f);
            KartModel.Show = false;
            var bare = Make(14, colors[0], new Vector3(3f, 0f, 0f)); one.gameObject.SetActive(false);
            Shot("5-collider-view-three-quarter-front", new Vector3(3f, 0f, 0f) + c0 + new Vector3(2.3f, 1.4f, 2.6f), new Vector3(3f, 0f, 0f) + c0);
            bare.gameObject.SetActive(false); one.gameObject.SetActive(true); KartModel.Show = true;
            Make(5, colors[1], new Vector3(-1.25f, 0f, -0.6f)); Make(27, colors[2], new Vector3(1.25f, 0f, 0.4f));
            Shot("6-three-karts", new Vector3(3.2f, 2.0f, 4.5f), new Vector3(0f, 0.2f, 0f));
        }

                /// <summary>Hitbox vs model: per body piece, the model's drawn extent and the colliders' extent (inches, kart axes).</summary>
        public static void HitboxCheck()
        {
            var setup = new KartSetup();
            var kp = KartFactory.Create("Check", 1, Color.red, setup, null);
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            Bounds? Union(System.Collections.Generic.IEnumerable<Bounds> bs) { Bounds? u = null; foreach (var b in bs) { if (u == null) u = b; else { var x = u.Value; x.Encapsulate(b); u = x; } } return u; }
            string F(Bounds? b) => b == null ? "none" : $"x {b.Value.min.x / 0.0254f:F2}..{b.Value.max.x / 0.0254f:F2}  y {b.Value.min.y / 0.0254f:F2}..{b.Value.max.y / 0.0254f:F2}  z {b.Value.min.z / 0.0254f:F2}..{b.Value.max.z / 0.0254f:F2}";
            foreach (var piece in new[] { "Tire_LF", "Tire_RF", "Tire_LR", "Tire_RR" })
            {
                Bounds? u = null;
                foreach (var r in kp.GetComponentsInChildren<Renderer>(true)) if (r.name == piece) { if (u == null) u = r.bounds; else { var x = u.Value; x.Encapsulate(r.bounds); u = x; } }
                sb.AppendLine($"KMT {piece}: drawn {F(u)}");
            }
            {
                Bounds? dm = null, dc = null;
                var dmT = kp.transform.Find("DriverModel"); var dcT = kp.transform.Find("Driver");
                if (dmT != null) foreach (var r in dmT.GetComponentsInChildren<Renderer>(true)) { if (dm == null) dm = r.bounds; else { var x = dm.Value; x.Encapsulate(r.bounds); dm = x; } }
                if (dcT != null) foreach (var c in dcT.GetComponentsInChildren<Collider>(true))
                {
                    bool was = c.enabled; c.enabled = true; Physics.SyncTransforms(); var b = c.bounds; c.enabled = was;
                    sb.AppendLine($"KMT driver capsule {c.name}: {F(b)}");
                    if (dc == null) dc = b; else { var x = dc.Value; x.Encapsulate(b); dc = x; }
                }
                sb.AppendLine($"KMT driver model: drawn {F(dm)}");
                sb.AppendLine($"KMT driver capsules: {F(dc)}");
            }
            foreach (var piece in new[] { "BodyNose_Pointed", "BodySide_L_Pointed", "BodySide_R_Pointed", "SeatShell", "Engine_Block", "Engine_Exhaust", "Engine_AirFilter" })
            {
                var rends = new System.Collections.Generic.List<Bounds>(); var cols = new System.Collections.Generic.List<Bounds>();
                foreach (var r in kp.GetComponentsInChildren<Renderer>(true)) if (r.name == piece) rends.Add(r.bounds);
                foreach (var c in kp.GetComponentsInChildren<Collider>(true)) if (c.name == piece + "_Hitbox") cols.Add(c.bounds);
                sb.AppendLine($"KMT {piece}: drawn {F(Union(rends))}");
                sb.AppendLine($"KMT {piece}: hitbox {F(Union(cols))} ({cols.Count} hulls)");
            }
            var all = new System.Collections.Generic.List<Bounds>();
            foreach (var c in kp.GetComponentsInChildren<Collider>(true)) if (c.enabled && !c.transform.IsChildOf(kp.transform.Find("Driver"))) all.Add(c.bounds);
            sb.AppendLine($"KMT all kart colliders (no driver): {F(Union(all))}");
            sb.AppendLine($"KMT footprint front {setup.FootFront / 0.0254f:F2} rear {setup.FootRear / 0.0254f:F2} left {setup.FootLeft / 0.0254f:F2} right {setup.FootRight / 0.0254f:F2} in; inertia {kp.Body.inertiaTensor}");
            Debug.Log(sb.ToString());
        }

        static string Arg(string name, string def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }
    }
}
