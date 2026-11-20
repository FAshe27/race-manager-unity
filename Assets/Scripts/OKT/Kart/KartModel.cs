// The kart's LOOK: Franklin's Blender kart (Kart Parts/exports/OKT_Kart.fbx -> Resources/KartModel/OKT_Kart) laid over
// the physics kart as a skin. Spec §9 says what you see is the collider; that stays true underneath: every collider is
// built exactly as before, only the primitives that draw the body, nose, front bumper, frame box and wheels are hidden
// under the model. Show = false (or no model in Resources) gives the old collider view back. The model has no
// colliders and no say in the physics. Its wheels are hung on the physics wheel pivots, so they steer, spin and ride
// where the tire rays are.
using UnityEngine;

namespace OKT
{
    public static class KartModel
    {
        public static bool Show = true;
        const string ResourcePath = "KartModel/OKT_Kart";
        static readonly string[] WheelNames = { "Wheel_LF", "Wheel_RF", "Wheel_LR", "Wheel_RR" }; // Corner order
        // physics primitives the model replaces (their renderers only; colliders stay)
        static readonly string[] Covered = { "FrameVisual", "Body", "FrontBumperLow", "FrontBumperTop", "RearBumperLow", "RearBumperTop",
            // the physics nerf tubes (Franklin 11-20: "black bars jutting out through the side panels"); the model has its own
            "NerfLLow", "NerfLTop", "NerfRLow", "NerfRTop" };

        static GameObject prefab; static bool loaded;

        public static bool Available
        {
            get { if (!loaded) { loaded = true; prefab = Resources.Load<GameObject>(ResourcePath); } return prefab != null; }
        }

        public static void Attach(GameObject kart, KartPhysics kp, KartSetup setup, Color bodyColor)
        {
            if (!Show || !Available) return;
            var root = kart.transform;
            var model = Object.Instantiate(prefab, root, false);
            model.name = "Model";
            // model origin: front axle centre on the ground, nose +Z -> the physics kart's front axle
            // (the importer's own root rotation is the FBX axis conversion: keep it)
            model.transform.localPosition = prefab.transform.localPosition + new Vector3(0f, 0f, setup.wheelbase * 0.5f);
            model.transform.localRotation = prefab.transform.localRotation;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Kill(c);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;

            // body paint in the kart's colour
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.StartsWith("Body_Paint"))
                    {
                        var m = new Material(mats[i]) { name = "Body_Paint #" + kart.name };
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", bodyColor);
                        if (m.HasProperty("_Color")) m.SetColor("_Color", bodyColor);
                        mats[i] = m; changed = true;
                    }
                if (changed) r.sharedMaterials = mats;
            }

            // wheels onto the physics pivots: keep each wheel's orientation relative to the kart, centred on the pivot
            for (int i = 0; i < 4; i++)
            {
                var pivot = kp.wheelVisuals[i]; if (pivot == null || pivot.childCount == 0) continue;
                var w = Find(model.transform, WheelNames[i]); if (w == null) continue;
                var spin = pivot.GetChild(0);
                foreach (var r in spin.GetComponentsInChildren<Renderer>(true)) r.enabled = false; // the cylinder
                var rel = Quaternion.Inverse(model.transform.rotation) * w.rotation;
                w.SetParent(spin, false);
                w.localPosition = Vector3.zero; w.localRotation = rel;
            }

            if (Hitbox) FitHitbox(kart, kp, setup, model.transform);
            if (ShowDriver) AttachDriver(root, setup, bodyColor);

            // the number (its own layer) sat on the old frame block, which the model hides: put it on the nose deck,
            // where real karts carry it and the overhead camera sees it
            var num = Find(root, "Number"); var noseT = Find(model.transform, "BodyNose_Pointed");
            var noseR = noseT != null ? noseT.GetComponent<Renderer>() : null;
            var noseMf = noseT != null ? noseT.GetComponent<MeshFilter>() : null;
            if (num != null && noseR != null && noseMf != null && noseMf.sharedMesh != null && noseMf.sharedMesh.isReadable)
            {
                // on the deck: the highest point of the shell near the kart's centreline, nudged just above it
                var m = root.worldToLocalMatrix * noseT.localToWorldMatrix;
                Vector3 best = Vector3.zero; bool found = false;
                foreach (var p in noseMf.sharedMesh.vertices)
                {
                    var q = m.MultiplyPoint3x4(p);
                    if (Mathf.Abs(q.x) > 3f * U.In) continue;
                    if (!found || q.y > best.y) { best = q; found = true; }
                }
                if (found) num.localPosition = new Vector3(0f, best.y + 0.15f * U.In, best.z);
            }

            // hide the primitives the model covers
            foreach (var name in Covered)
            {
                var t = Find(root, name); if (t == null || t.IsChildOf(model.transform)) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
        }

        // Franklin 11-19: "make the hitbox of the kart the same as the actual kart ... I wouldn't want cars able to glitch
        // into each other". The body's colliders are the model's own shell pieces (each a convex hull: tight at the
        // edges, the dip in the side panels filled over), the rear bumper sits where the frame's bumper is, and the
        // footprint the drivers judge gaps by comes from the same shapes.
        public static bool Hitbox = true;
        static readonly string[] HullPieces = { "BodyNose_Pointed", "BodySide_L_Pointed", "BodySide_R_Pointed" };
        // seat and Clone engine (parts session 11-19, from the shop overhead photo; sizes estimated) -- solid like the frame
        static readonly string[] SolidPieces = { "SeatShell", "Engine_Block", "Engine_MountClutch", "Engine_Starter", "Engine_AirFilter", "Engine_Exhaust" };
        const float FrameRailTopIn = 1.5f;   // rails 1 in tube, 1/2 in off the ground (Franklin)

        static void FitHitbox(GameObject kart, KartPhysics kp, KartSetup setup, Transform model)
        {
            var root = kart.transform;
            var w2l = root.worldToLocalMatrix;
            // the shell's physics material, from the primitive panel it replaces
            PhysicsMaterial panelMat = null;
            var bodyT = Find(root, "Body");
            if (bodyT != null)
            {
                foreach (var c in bodyT.GetComponentsInChildren<Collider>(true)) { if (panelMat == null) panelMat = c.sharedMaterial; Kill(c); }
            }
            foreach (var n in new[] { "FrontBumperLow", "FrontBumperTop" })
            {
                var t = Find(root, n); if (t == null) continue;
                foreach (var c in t.GetComponents<Collider>()) Kill(c);
            }
            float front = float.NegativeInfinity, left = float.NegativeInfinity, right = float.NegativeInfinity;
            var hulls = new GameObject("BodyHitbox").transform; hulls.SetParent(root, false);
            // the frame block shrinks to the rails (it still drags on the dirt); the seat and engine get their own shapes
            var box = kart.GetComponent<BoxCollider>();
            PhysicsMaterial solidMat = box != null ? box.sharedMaterial : null;
            bool haveSolids = false;
            foreach (var n in SolidPieces) if (Find(model, n) != null) { haveSolids = true; break; }
            if (box != null && haveSolids)
            {
                float y0 = setup.frameClearance, y1 = FrameRailTopIn * U.In;
                var c = box.center; var sz = box.size;
                box.size = new Vector3(sz.x, y1 - y0, sz.z); box.center = new Vector3(c.x, (y0 + y1) * 0.5f, c.z);
            }
            var pieces = new System.Collections.Generic.List<string>(HullPieces);
            if (haveSolids) pieces.AddRange(SolidPieces);
            foreach (var name in pieces)
            {
                bool solid = System.Array.IndexOf(SolidPieces, name) >= 0;
                var t = Find(model, name); var mf = t != null ? t.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) { Debug.LogWarning("KartModel: no readable " + name); continue; }
                var m = w2l * t.localToWorldMatrix;
                var src = mf.sharedMesh.vertices; var v = new Vector3[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    v[i] = m.MultiplyPoint3x4(src[i]);
                    if (solid) continue; // the plan outline is the body's
                    if (v[i].z > front) front = v[i].z;
                    if (v[i].x > right) right = v[i].x;
                    if (-v[i].x > left) left = -v[i].x;
                }
                // one shell is too curved for one convex hull (PhysX caps a hull at 255 faces, then uses a partial one):
                // cut it into ~6 in chunks along its long side, each chunk its own hull -- tighter to the curves too
                if (SquareNoseBottom && name.StartsWith("BodyNose")) v = SquareBottom(v);
                foreach (var chunk in Chunks(v, ChunkIn * U.In))
                {
                    var mesh = new Mesh { name = name + "_Hull" };
                    var tri = new int[(chunk.Length / 3) * 3]; for (int i = 0; i < tri.Length; i++) tri[i] = i;
                    mesh.vertices = chunk; mesh.triangles = tri;
                    var go = new GameObject(name + "_Hitbox"); go.transform.SetParent(hulls, false);
                    var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true; mc.sharedMaterial = solid ? solidMat : panelMat;
                }
            }
            // rear bumper tubes back to the frame's rear bumper
            float rear = setup.FootRear;
            var frame = Find(model, "Frame");
            if (frame != null)
            {
                float minZ = float.PositiveInfinity;
                foreach (var mf in frame.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    var m = w2l * mf.transform.localToWorldMatrix;
                    foreach (var p in mf.sharedMesh.vertices) { float z = m.MultiplyPoint3x4(p).z; if (z < minZ) minZ = z; }
                }
                if (!float.IsInfinity(minZ))
                {
                    float r = setup.TubeRadius;
                    foreach (var n in new[] { "RearBumperLow", "RearBumperTop" })
                    {
                        var t = Find(root, n); if (t == null) continue;
                        var lp = t.localPosition; lp.z = minZ + r; t.localPosition = lp;
                    }
                    rear = Mathf.Max(rear, -minZ);
                }
            }
            if (!float.IsNegativeInfinity(front))
                setup.SetFootprint(front, rear, Mathf.Max(left, setup.FootLeft), Mathf.Max(right, setup.FootRight));
            // the shapes decide contact, not turning: keep the rotational inertia the kart had
            var rb = kp.Body;
            rb.inertiaTensor = setup.inertiaPin; rb.inertiaTensorRotation = Quaternion.Euler(setup.inertiaPinEuler);
        }

        const float ChunkIn = 6f, GridIn = 0.5f;
        // The real nose rolls under at its bottom front edge. A rolled lower edge is a ramp: it climbed the berm and rides
        // up over a rival's bumper (M1 berm ride 11-16, where the old nose went square). Fill the undercut: every point
        // of the nose also stands straight down to the nose's lowest level. [TEST KNOB]
        public static bool SquareNoseBottom = true;
        static Vector3[] SquareBottom(Vector3[] v)
        {
            float ymin = float.PositiveInfinity; foreach (var p in v) ymin = Mathf.Min(ymin, p.y);
            var o = new Vector3[v.Length * 2];
            for (int i = 0; i < v.Length; i++) { o[2 * i] = v[i]; o[2 * i + 1] = new Vector3(v[i].x, ymin, v[i].z); }
            return o;
        }

        /// <summary>Points cut into slabs along the longer horizontal axis (x or z), each thinned to a 1/2 in grid,
        /// neighbouring slabs overlapping by one grid step so the hulls meet without gaps.</summary>
        static System.Collections.Generic.List<Vector3[]> Chunks(Vector3[] v, float size)
        {
            Vector3 mn = v[0], mx = v[0];
            foreach (var p in v) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            bool alongZ = (mx.z - mn.z) >= (mx.x - mn.x);
            float lo = alongZ ? mn.z : mn.x, hi = alongZ ? mx.z : mx.x, g = GridIn * U.In;
            int n = Mathf.Max(1, Mathf.CeilToInt((hi - lo) / size));
            float step = (hi - lo) / n;
            var outList = new System.Collections.Generic.List<Vector3[]>();
            for (int k = 0; k < n; k++)
            {
                float a = lo + k * step - g, b = lo + (k + 1) * step + g;
                var seen = new System.Collections.Generic.HashSet<Vector3Int>(); var pts = new System.Collections.Generic.List<Vector3>();
                foreach (var p in v)
                {
                    float c = alongZ ? p.z : p.x; if (c < a || c > b) continue;
                    var key = new Vector3Int(Mathf.RoundToInt(p.x / g), Mathf.RoundToInt(p.y / g), Mathf.RoundToInt(p.z / g));
                    if (seen.Add(key)) pts.Add(p);
                }
                if (pts.Count >= 4) { while (pts.Count % 3 != 0) pts.Add(pts[pts.Count - 1]); outList.Add(pts.ToArray()); }
            }
            return outList;
        }

        // ---- the driver: Franklin's Racer model (Kart Parts/driver.py -> Resources/KartModel/OKT_Driver), posed in the
        // seat in Blender from his photos. It is only a skin: the driver's collision capsules (KartFactory) are moved onto
        // the model's own joints (Joint_* markers in the FBX), so what touches a rival or the ground is where he is drawn.
        public static bool ShowDriver = true;
        const string DriverPath = "KartModel/OKT_Driver";
        static GameObject driverPrefab; static bool driverLoaded;
        // radii of the capsules laid on his joints (inches) [TEST VALUE: fitted to the model]
        const float TorsoR = 5f, UpperArmR = 2.1f, ForearmR = 1.8f, ThighR = 3f, ShinR = 2.4f, HelmetR = 5.4f;

        static void AttachDriver(Transform root, KartSetup setup, Color bodyColor)
        {
            if (!driverLoaded) { driverLoaded = true; driverPrefab = Resources.Load<GameObject>(DriverPath); }
            var drv = Find(root, "Driver");
            if (driverPrefab == null || drv == null) return;
            var m = Object.Instantiate(driverPrefab, root, false);
            m.name = "DriverModel";
            m.transform.localPosition = driverPrefab.transform.localPosition + new Vector3(0f, 0f, setup.wheelbase * 0.5f);
            m.transform.localRotation = driverPrefab.transform.localRotation;
            foreach (var c in m.GetComponentsInChildren<Collider>(true)) Kill(c);
            foreach (var t in m.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;

            // suit and helmet: stripes and helmet paint in the kart's colour, suit black (Franklin's photos)
            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string n = mats[i].name;
                    Color? col = n.StartsWith("helmet_color") || n.StartsWith("leather_color") ? bodyColor
                        : n.StartsWith("leather") || n.StartsWith("blacklather") ? new Color(0.06f, 0.06f, 0.07f)
                        : n.StartsWith("helmet") ? new Color(0.85f, 0.85f, 0.87f)
                        : n.StartsWith("glass") ? new Color(0.05f, 0.05f, 0.06f)
                        : (Color?)null;
                    if (col == null) continue;
                    var mm = new Material(mats[i]) { name = n + " #" + root.name };
                    if (mm.HasProperty("_BaseColor")) mm.SetColor("_BaseColor", col.Value);
                    if (mm.HasProperty("_Color")) mm.SetColor("_Color", col.Value);
                    if (mm.HasProperty("_BaseMap")) mm.SetTexture("_BaseMap", null);
                    if (mm.HasProperty("_MainTex")) mm.SetTexture("_MainTex", null);
                    // cloth suit matte, helmet and visor glossy
                    float gloss = n.StartsWith("leather") || n.StartsWith("blacklather") ? 0.2f : 0.75f;
                    if (mm.HasProperty("_Smoothness")) mm.SetFloat("_Smoothness", gloss);
                    if (mm.HasProperty("_Glossiness")) mm.SetFloat("_Glossiness", gloss);
                    if (mm.HasProperty("_Metallic")) mm.SetFloat("_Metallic", 0f);
                    mats[i] = mm;
                }
                r.sharedMaterials = mats;
            }

            // capsules onto his joints (kart space -> the Driver object's space)
            bool ok = true;
            Vector3 J(string n)
            {
                var t = Find(m.transform, "Joint_" + n);
                if (t == null) { ok = false; return Vector3.zero; }
                return drv.InverseTransformPoint(t.position);
            }
            void Cap(string name, Vector3 a, Vector3 b, float rIn)
            {
                var t = Find(drv, name); if (t == null) return;
                float r = rIn * U.In;
                t.localPosition = (a + b) * 0.5f;
                t.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                t.localScale = new Vector3(2f * r, ((b - a).magnitude + 2f * r) * 0.5f, 2f * r);
            }
            var pelvis = J("pelvis"); var neck = J("neck"); var head = J("head");
            if (!ok) { Kill(m); return; }
            // torso: pelvis to the base of the neck, held in from each end so it doesn't poke out under the seat
            var up = (neck - pelvis).normalized;
            Cap("Torso", pelvis + up * (TorsoR * 0.6f * U.In), neck - up * (TorsoR * 0.4f * U.In), TorsoR);
            foreach (var s in new[] { "L", "R" })
            {
                Cap("UpperArm" + s, J("sh" + s), J("el" + s), UpperArmR);
                Cap("Forearm" + s, J("el" + s), J("wr" + s), ForearmR);
                Cap("Thigh" + s, J("hip" + s), J("knee" + s), ThighR);
                Cap("Shin" + s, J("knee" + s), J("ankle" + s), ShinR);
            }
            var helm = Find(drv, "Helmet");
            if (helm != null) { helm.localPosition = head; helm.localScale = Vector3.one * (2f * HelmetR * U.In); }
            foreach (var r in drv.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            // hands on the wheel: the column turns with the front wheels and the arms follow it
            DriverArms.Attach(m, root.GetComponent<KartPhysics>(), Find(root, "SteeringShaft"), Find(root, "ColumnTop"));
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }

        static void Kill(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
    }
}
