using System.Collections.Generic;
// Builds the track GameObjects from the TrackModel (spec §5): the heightfield
// ribbon mesh + MeshCollider (racing surface, berm, outside run-off bank),
// the outside barrier as box strips laid only where the JSON's barrier layout
// says (guardrail / fence; openings stay open), a ground plane (infield grass),
// visual-only cones on the inside edge, and line markers. Barrier visuals ARE the
// collider boxes, so mesh and collider can never disagree.
// Franklin (2026-11-14): the inside edge is NOT a wall — flush grass, cones only.
using UnityEngine;
using UnityEngine.Rendering;

namespace OKT
{
    public static class TrackBuilder
    {
        public static PhysicsMaterial DirtMaterial()
        {
            // Chassis-on-dirt contact (only when the frame bottoms out). Tires do NOT
            // use this — tire grip is the raycast model's job. [TEST VALUE]
            return new PhysicsMaterial("Dirt") { dynamicFriction = 0.6f, staticFriction = 0.6f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
        }

        /// <summary>Kart bodywork. Friction combine = Minimum so the rail's low friction decides a scrape.</summary>
        public static PhysicsMaterial BodyMaterial()
        {
            return new PhysicsMaterial("KartBody") { dynamicFriction = 0.3f, staticFriction = 0.3f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
        }

        /// <summary>Guardrail / fence. Low friction: a shallow hit skims along it (Franklin).</summary>
        public static PhysicsMaterial BarrierMaterial(float friction)
        {
            return new PhysicsMaterial("Barrier") { dynamicFriction = friction, staticFriction = friction, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
        }

        public static GameObject Build(TrackModel t, Transform parent)
        {
            var root = new GameObject("Track (" + t.name + ")");
            root.transform.SetParent(parent, false);
            BuildSurface(t, root.transform);
            BuildBarriers(t, root.transform);
            // the outside barrier's own colliders on the barrier layer: wheel rays never stand on the rail (see KartPhysics)
            var ob = root.transform.Find("OutsideBarrier");
            if (ob != null) foreach (Transform ch in ob) if (ch.GetComponent<Collider>() != null && ch.gameObject.layer != 2) ch.gameObject.layer = KartPhysics.BarrierLayer;
            BuildGround(t, root.transform);
            BuildCones(t, root.transform);
            BuildMarkers(t, root.transform);
            BuildFeatures(t, root.transform);
            return root;
        }

        public static GameObject BuildSurface(TrackModel t, Transform parent)
        {
            var cfg = t.cfg;
            int rows = t.M;
            int cols = ColumnCount(cfg);
            var verts = new Vector3[rows * cols];
            var uvs = new Vector2[rows * cols];
            var colors = new Color[rows * cols];
            for (int r = 0; r < rows; r++)
            {
                float s = (float)r / rows * t.N;
                var f = t.Frame(s);
                float arc = t.ArcAt(s);
                for (int j = 0; j < cols; j++)
                {
                    float d = ColumnOffset(f, cfg, j);
                    var p = f.pos + f.outN * d;
                    verts[r * cols + j] = new Vector3(p.x, t.SurfaceHeightSD(s, d), p.y);
                    // u: distance from the groove (texture paints the darker worn strip); v: along the lap.
                    // Off the dirt (run-off bank) u is pushed to the texture's grass end.
                    float u = (d > f.halfWidth + 0.01f || d < -f.halfWidth - cfg.insideRidgeWidth - cfg.insideBand - 0.01f) ? 1f
                            : d < -f.halfWidth - 0.01f ? 0.97f // ridge + graded band: plain dirt
                            : Mathf.Clamp((d - f.LineOffset) / 8f + 0.5f, 0f, 0.97f);
                    uvs[r * cols + j] = new Vector2(u, arc / 4f);
                }
            }
            var tris = new int[rows * (cols - 1) * 6];
            int ti = 0;
            for (int r = 0; r < rows; r++)
            {
                int rn = (r + 1) % rows;
                for (int j = 0; j < cols - 1; j++)
                {
                    int a = r * cols + j, b = r * cols + j + 1, c = rn * cols + j, e = rn * cols + j + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = e;
                }
            }
            var mesh = new Mesh { name = "TrackSurface", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateNormals();
            if (mesh.normals[cols / 2].y < 0f) // flip winding if normals face down
            {
                for (int i = 0; i < tris.Length; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }
                mesh.triangles = tris;
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();

            var go = new GameObject("Surface");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials.Dirt();
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = false;
            mc.sharedMesh = mesh;
            mc.sharedMaterial = DirtMaterial();
            return go;
        }

        public static int ColumnCount(SurfaceConfig cfg) => 7 + cfg.surfaceCols + cfg.bermCols + 1 + 4; // + 7 inside columns (grass, band, ridge)

        /// <summary>Lateral offset of mesh column j: inside edge .. berm start .. outside edge .. flat .. bank.</summary>
        public static float ColumnOffset(in TrackFrame f, SurfaceConfig cfg, int j)
        {
            float hw = f.halfWidth;
            float bermStart = hw - cfg.bermWidth;
            float rw = cfg.insideRidgeWidth, band = cfg.insideBand;
            if (j == 0) return -hw - rw - band - 1.0f;          // grass edge of the mesh
            if (j == 1) return -hw - rw - band;                 // band meets the grass
            if (j <= 6) return -hw - rw * (1f - (j - 2) / 5f);  // ridge: foot (j=2) .. last slice before the edge (j=6)
            j -= 7;
            if (j <= cfg.surfaceCols) return Mathf.Lerp(-hw, bermStart, (float)j / cfg.surfaceCols);
            int jb = j - cfg.surfaceCols;
            if (jb <= cfg.bermCols) return Mathf.Lerp(bermStart, hw, (float)jb / cfg.bermCols);
            int js = jb - cfg.bermCols; // 1..4
            if (js == 1) return hw + cfg.outerFlat;
            return hw + cfg.outerFlat + f.outerBank * (js - 1) / 3f;
        }

        public static void BuildBarriers(TrackModel t, Transform parent)
        {
            var cfg = t.cfg;
            var outer = new GameObject("OutsideBarrier").transform; outer.SetParent(parent, false);
            var railMat = Materials.Solid(new Color(0.82f, 0.82f, 0.84f), 0.5f);
            var fenceMat = Materials.Solid(new Color(0.55f, 0.58f, 0.62f), 0.3f);
            var wallMat = Materials.Solid(new Color(0.92f, 0.92f, 0.92f), 0.2f);
            var pmat = BarrierMaterial(cfg.barrierFriction);
            // One continuous mesh per barrier run (no internal seams on the face: separate
            // end-to-end boxes produced "ghost" contacts that stopped a kart dead on a 10° hit).
            int segs = t.N * 2; // 512 slices around the lap (~1.3 ft each)
            var types = new BarrierType[segs];
            for (int i = 0; i < segs; i++) types[i] = t.BarrierAt((i + 0.5f) / segs);
            // Start at a boundary between runs so no run is split across slice 0.
            int b0 = 0;
            for (int i = 0; i < segs; i++) if (types[i] != types[(i - 1 + segs) % segs]) { b0 = i; break; }
            int done = 0;
            while (done < segs)
            {
                int start = b0 + done;
                var type = types[start % segs];
                int len = 1;
                while (done + len < segs && types[(start + len) % segs] == type) len++;
                if (type != BarrierType.None) // None = an opening (access road / exit / grid entrance)
                {
                    float h = type == BarrierType.Fence ? cfg.fenceHeight : cfg.guardrailHeight;
                    MakeRun(t, outer, type + "_" + (start % segs), start, start + len, segs, h, cfg.barrierThickness,
                        type == BarrierType.Fence ? fenceMat : railMat, pmat);
                    // the solid wall behind the guardrail (photos): stops anything that gets over the rail
                    if (type == BarrierType.Guardrail && cfg.backWallHeightFt > 0f)
                        MakeRun(t, outer, "BackWall_" + (start % segs), start, start + len, segs, cfg.backWallHeightFt * U.Ft, cfg.barrierThickness,
                            wallMat, pmat, cfg.barrierThickness + cfg.backWallGapIn * U.In);
                }
                done += len;
            }
        }

        /// <summary>A barrier run as one closed ribbon (inner face on the outside edge, top, back) + MeshCollider.</summary>
        static void MakeRun(TrackModel t, Transform parent, string name, int start, int end, int segs, float height,
            float thick, Material mat, PhysicsMaterial pmat, float setBack = 0f)
        {
            const float sink = 0.4f; // extend below the surface so nothing slips under
            int n = end - start + 1;
            // 4 corners per slice: inner-bottom, inner-top, outer-top, outer-bottom
            var v = new Vector3[n * 4];
            for (int i = 0; i < n; i++)
            {
                float s = (float)((start + i) % segs) / segs * t.N;
                var f = t.Frame(s);
                float baseY = t.SurfaceHeightSD(s, f.halfWidth);
                var a = f.pos + f.outN * (f.halfWidth + setBack);
                var b = f.pos + f.outN * (f.halfWidth + setBack + thick);
                v[i * 4 + 0] = new Vector3(a.x, baseY - sink, a.y);
                v[i * 4 + 1] = new Vector3(a.x, baseY + height, a.y);
                v[i * 4 + 2] = new Vector3(b.x, baseY + height, b.y);
                v[i * 4 + 3] = new Vector3(b.x, baseY - sink, b.y);
            }
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < n - 1; i++)
                for (int q = 0; q < 3; q++) // faces: inner (0-1), top (1-2), outer (2-3)
                {
                    int a0 = i * 4 + q, a1 = i * 4 + q + 1, b0 = (i + 1) * 4 + q, b1 = (i + 1) * 4 + q + 1;
                    tris.Add(a0); tris.Add(a1); tris.Add(b0);
                    tris.Add(a1); tris.Add(b1); tris.Add(b0);
                }
            // end caps
            foreach (int i in new[] { 0, n - 1 })
            {
                int o = i * 4;
                if (i == 0) { tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2); }
                else { tris.Add(o); tris.Add(o + 1); tris.Add(o + 2); tris.Add(o); tris.Add(o + 2); tris.Add(o + 3); }
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v; mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            // Make sure the inner face points at the track (toward -outN): flip if not.
            var f0 = t.Frame((float)(start % segs) / segs * t.N);
            var nrm = mesh.normals[1];
            if (Vector2.Dot(new Vector2(nrm.x, nrm.z), f0.outN) > 0f)
            {
                for (int i = 0; i < tris.Count; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }
                mesh.SetTriangles(tris, 0); mesh.RecalculateNormals();
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh; mc.convex = false; mc.sharedMaterial = pmat;
            go.isStatic = true;
        }

        // ---------------- scenery from the JSON "features" (visual only: no colliders yet) ----------------
        /// <summary>World point for (u, feet beyond the outside edge), sitting on the ground there.</summary>
        static Vector3 FeaturePoint(TrackModel t, float u, float outFt, float lift = 0.03f) => t.OffTrackPoint(u, outFt, lift);

        /// <summary>The feature's points with extra points between them every ~1 m, so paths follow the track's curve.</summary>
        static List<Vector3> FeaturePath(TrackModel t, TrackFeature ft, float lift)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < ft.points.Count; i++)
            {
                var a = ft.points[i];
                if (i == ft.points.Count - 1) { pts.Add(FeaturePoint(t, a.x, a.y, lift)); break; }
                var b = ft.points[i + 1];
                float du = b.x - a.x; if (du > 0.5f) du -= 1f; else if (du < -0.5f) du += 1f;
                float len = Vector3.Distance(FeaturePoint(t, a.x, a.y), FeaturePoint(t, a.x + du, b.y));
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 1f));
                for (int k = 0; k < n; k++) { float q = (float)k / n; pts.Add(FeaturePoint(t, a.x + du * q, Mathf.Lerp(a.y, b.y, q), lift)); }
            }
            return pts;
        }

        static void BuildFeatures(TrackModel t, Transform parent)
        {
            if (t.features.Count == 0) return;
            var root = new GameObject("Scenery").transform; root.SetParent(parent, false);
            var asphalt = Materials.Solid(new Color(0.36f, 0.36f, 0.37f), 0.1f);
            var dirtRoad = Materials.Solid(new Color(0.62f, 0.48f, 0.34f), 0.05f);
            var fenceMat = Materials.Solid(new Color(0.55f, 0.58f, 0.60f), 0.3f);
            var gateMat = Materials.Solid(new Color(0.80f, 0.12f, 0.10f), 0.3f);
            var houseMat = Materials.Solid(new Color(0.88f, 0.86f, 0.82f), 0.2f);
            foreach (var ft in t.features)
            {
                var go = new GameObject(ft.type + ": " + ft.name); go.transform.SetParent(root, false); go.isStatic = true;
                var verts = new List<Vector3>(); var tris = new List<int>(); Material mat;
                if (ft.type == "road" || ft.type == "dirtroad")
                {
                    var path = FeaturePath(t, ft, ft.type == "road" ? 0.03f : 0.02f); float hw = ft.widthFt * U.Ft * 0.5f;
                    for (int i = 0; i < path.Count; i++)
                    {
                        var dir = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]; dir.y = 0f; dir.Normalize();
                        var side = new Vector3(dir.z, 0f, -dir.x);
                        verts.Add(path[i] - side * hw); verts.Add(path[i] + side * hw);
                        if (i > 0) { int a = 2 * i - 2; tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
                    }
                    mat = ft.type == "road" ? asphalt : dirtRoad;
                }
                else if (ft.type == "pad")
                {
                    var ring = FeaturePath(t, ft, 0.035f); ring.Add(ring[0]);
                    var c = Vector3.zero; foreach (var p in ring) c += p; c /= ring.Count;
                    verts.Add(c); verts.AddRange(ring);
                    for (int i = 1; i < verts.Count - 1; i++) tris.AddRange(new[] { 0, i, i + 1 });
                    mat = asphalt;
                }
                else if (ft.type == "fence" || ft.type == "gate")
                {
                    var path = FeaturePath(t, ft, 0f); float h = (ft.heightFt > 0f ? ft.heightFt : 5f) * U.Ft;
                    // two sheets (front and back) with their own vertices, so each face gets its own normal
                    int n = path.Count;
                    for (int i = 0; i < n; i++) { verts.Add(path[i]); verts.Add(path[i] + Vector3.up * h); }
                    for (int i = 0; i < n; i++) { verts.Add(path[i]); verts.Add(path[i] + Vector3.up * h); }
                    for (int i = 1; i < n; i++)
                    {
                        int a = 2 * i - 2, b = 2 * n + 2 * i - 2;
                        tris.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
                        tris.AddRange(new[] { b, b + 2, b + 1, b + 2, b + 3, b + 1 });
                    }
                    mat = ft.type == "gate" ? gateMat : fenceMat;
                }
                else if (ft.type == "building")
                {
                    var p = FeaturePoint(t, ft.points[0].x, ft.points[0].y, 0f);
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(b.GetComponent<Collider>());
                    b.name = ft.name; b.transform.SetParent(go.transform, false);
                    float s = Mathf.Repeat(ft.points[0].x, 1f) * t.N; var f = t.Frame(s);
                    b.transform.position = p + Vector3.up * ft.heightFt * U.Ft * 0.5f;
                    b.transform.rotation = Quaternion.LookRotation(new Vector3(f.tan.x, 0f, f.tan.y));
                    b.transform.localScale = new Vector3(ft.depthFt * U.Ft, ft.heightFt * U.Ft, ft.widthFt * U.Ft);
                    b.GetComponent<MeshRenderer>().sharedMaterial = houseMat;
                    continue;
                }
                else continue;
                var m = new Mesh { name = ft.name }; m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        static void BuildGround(TrackModel t, Transform parent)
        {
            // Infield + surroundings: grass at y = 0 (= inside-edge height, flush with the dirt).
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Ground";
            g.transform.SetParent(parent, false);
            // big enough for the track and all its scenery (the back rows of the staging grid hung off a fixed
            // 160 x 110 m plane, 2026-11-17), with room to spare
            var mn = new Vector2(-80f, -55f); var mx = new Vector2(80f, 55f);
            foreach (var ft in t.features)
                foreach (var q in ft.points)
                {
                    var w = t.OffTrackPoint(q.x, q.y + 30f);
                    mn = Vector2.Min(mn, new Vector2(w.x, w.z)); mx = Vector2.Max(mx, new Vector2(w.x, w.z));
                }
            var cg = (mn + mx) * 0.5f; var sz = mx - mn;
            g.transform.localScale = new Vector3(sz.x, 1f, sz.y);
            g.transform.position = new Vector3(cg.x, -0.5f - 0.005f + t.InfieldHeight, cg.y);
            g.GetComponent<MeshRenderer>().sharedMaterial = Materials.Solid(new Color(0.30f, 0.40f, 0.20f), 0.05f);
            g.GetComponent<BoxCollider>().sharedMaterial = DirtMaterial();
            g.isStatic = true;
        }

        static void BuildCones(TrackModel t, Transform parent)
        {
            // Visual-only marker cones on the inside edge (as in the Atoka video). No colliders.
            var root = new GameObject("InsideCones").transform; root.SetParent(parent, false);
            var mat = Materials.Solid(new Color(1f, 0.55f, 0.05f), 0.3f);
            float spacing = 30f * U.Ft; // [TEST VALUE] presentation
            int n = Mathf.Max(4, Mathf.RoundToInt(t.centerLength / spacing));
            for (int i = 0; i < n; i++)
            {
                float s = (float)i / n * t.N; var f = t.Frame(s);
                var p = f.pos - f.outN * (f.halfWidth + 0.25f);
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(c.GetComponent<Collider>());
                c.name = "Cone" + i; c.layer = 2;
                c.transform.SetParent(root, false);
                c.transform.position = new Vector3(p.x, 0.15f, p.y);
                c.transform.localScale = new Vector3(0.18f, 0.15f, 0.18f);
                c.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        static void BuildMarkers(TrackModel t, Transform parent)
        {
            // Visual-only stripes (no colliders): start/finish (u = 0) and the go line (u = goU).
            MakeLineMarker(t, parent, "StartFinish", 0f, Color.white);
            MakeLineMarker(t, parent, "GoLine", t.goU * t.N, new Color(0.9f, 0.15f, 0.1f));
        }

        static void MakeLineMarker(TrackModel t, Transform parent, string name, float s, Color col)
        {
            var f = t.Frame(s);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.transform.SetParent(parent, false);
            float h = t.SurfaceHeightSD(s, 0f);
            go.transform.position = new Vector3(f.pos.x, h + 0.02f, f.pos.y);
            var outward = new Vector3(f.outN.x, 0, f.outN.y);
            var fwd = new Vector3(f.tan.x, 0, f.tan.y);
            var n = (Vector3.up - outward * Mathf.Tan(t.cfg.bankingDeg * Mathf.Deg2Rad)).normalized;
            go.transform.rotation = Quaternion.LookRotation(fwd, n);
            go.transform.localScale = new Vector3(f.halfWidth * 2f, 0.02f, 0.25f);
            go.GetComponent<MeshRenderer>().sharedMaterial = Materials.Unlit(col);
        }

        /// <summary>Debug overlay: Franklin's groove (from lineFrac) drawn on the surface.</summary>
        public static LineRenderer BuildLineOverlay(TrackModel t, Transform parent)
        {
            var go = new GameObject("RacingLineOverlay");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.12f;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.sharedMaterial = Materials.Unlit(new Color(1f, 0.85f, 0.1f));
            int n = t.LineCount / 2;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                int k = i * 2; var p = t.LinePoint(k); float s = t.LineS(k); var f = t.Frame(s);
                pts[i] = new Vector3(p.x, t.SurfaceHeightSD(s, f.LineOffset) + 0.04f, p.y);
            }
            lr.positionCount = n;
            lr.SetPositions(pts);
            return lr;
        }
    }

    /// <summary>Runtime-created materials (URP Lit when available).</summary>
    public static class Materials
    {
        static Shader lit, unlit;
        static Material dirt;

        static Shader Lit() => lit != null ? lit : (lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        static Shader UnlitShader() => unlit != null ? unlit : (unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));

        public static Material Solid(Color c, float smooth)
        {
            var m = new Material(Lit());
            SetColor(m, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            return m;
        }

        public static Material Unlit(Color c)
        {
            var m = new Material(UnlitShader());
            SetColor(m, c);
            return m;
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>Dirt with a subtly darker worn strip along the groove (u = 0.5); u ≈ 1 is grass.</summary>
        public static Material Dirt()
        {
            if (dirt != null) return dirt;
            const int W = 256, H = 16;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapModeU = TextureWrapMode.Clamp, wrapModeV = TextureWrapMode.Repeat, name = "DirtGroove" };
            var baseC = new Color(0.50f, 0.36f, 0.24f);
            var groove = new Color(0.36f, 0.25f, 0.17f);
            var grass = new Color(0.30f, 0.40f, 0.20f);
            var rnd = new System.Random(7);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float uu = (x + 0.5f) / W;
                    float dm = Mathf.Abs(uu - 0.5f) * 8f; // metres from the groove centre
                    float g = dm < 1f ? 0.5f * (1f + Mathf.Cos(Mathf.PI * dm)) : 0f;
                    var c = uu > 0.985f ? grass : Color.Lerp(baseC, groove, g);
                    float n = (float)(rnd.NextDouble() - 0.5) * 0.04f;
                    px[y * W + x] = new Color(c.r + n, c.g + n, c.b + n, 1f);
                }
            tex.SetPixels(px); tex.Apply(true);
            dirt = Solid(Color.white, 0.05f);
            dirt.name = "Dirt";
            if (dirt.HasProperty("_BaseMap")) dirt.SetTexture("_BaseMap", tex);
            if (dirt.HasProperty("_MainTex")) dirt.SetTexture("_MainTex", tex);
            return dirt;
        }
    }
}
