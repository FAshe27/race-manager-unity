// Rail-end test (2026-11-27, the turn 3 exit breach): a kart that slid out through the track-exit opening is out on
// the run-off when the guardrail starts again. Drive karts along the run-off into the start of that rail run at
// several offsets beyond the edge, headings and speeds, braking (as #93 was for the caution), and report how high each
// one gets and whether it comes back over the rail onto the track (a wall breach).
// Finding (11-27): the launches came from the body panels' rigid hulls catching the ground sliding sideways (fixed in
// World: panels skip the ground). 81 runs: worst 26 ft up + 1 over the rail before, 0.9 ft and none after.
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod OKT.EditorTools.M2RailEndTest.Run -okt-out railend.txt
//     [-okt-ontrack]  same slides on the racing surface      [-okt-case mph,outFt,hdg,slide]  one run, traced per step
//     [-okt-ccd Discrete|Continuous|ContinuousDynamic|ContinuousSpeculative]   [-okt-profile]  ground profile + triangle check
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    [ExecuteAlways]
    public class ContactProbe : MonoBehaviour
    {
        public System.Text.StringBuilder log = new System.Text.StringBuilder();
        void OnCollisionEnter(Collision c) => Rec(c, "enter");
        void OnCollisionStay(Collision c) => Rec(c, "stay");
        void Rec(Collision c, string kind)
        {
            float imp = c.impulse.magnitude; if (imp < 30f) return;
            for (int i = 0; i < c.contactCount; i++)
            {
                var cp = c.GetContact(i);
                log.Append($" [{kind} {cp.thisCollider.name}->{c.collider.name} sep {cp.separation * 100f:F1}cm n({cp.normal.x:F2},{cp.normal.y:F2},{cp.normal.z:F2}) p.y {cp.point.y:F2} imp {imp:F0}]");
            }
        }
    }

    public static class M2RailEndTest
    {
        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs(); string outPath = "railend.txt";
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-okt-out") outPath = a[i + 1];
            var sb = new StringBuilder("Rail-end test: a braking kart on the run-off behind the turn 3 track-exit opening, driven into the start of the next guardrail run.\n" +
                                       "out = ft beyond the outside edge at the start; hdg = degrees toward the track (+) / away (-); peak = highest point above the ground under it.\n");
            int code = 0, breaches = 0, n = 0;
            if (System.Array.IndexOf(a, "-okt-profile") >= 0)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var w = World.BuildRace(File.ReadAllText(M1SceneSetup.JsonPath), 9001, new RaceConfig { fieldSize = 1 });
                var t = w.track;
                foreach (float u in new[] { 0.49f, 0.611f })
                {
                    var f = t.Frame(u * t.N); sb.AppendLine($"profile u {u}: d from edge (ft) -> formula / solid mesh / loose mesh height above the edge (in)");
                    float e = t.SurfaceHeightSD(u * t.N, f.halfWidth);
                    for (float x = -4f; x <= 14f; x += 0.5f)
                    {
                        float d = f.halfWidth + x * U.Ft; var pp = f.pos + f.outN * d;
                        float hs = float.NaN, hl = float.NaN;
                        foreach (var h in Physics.RaycastAll(new Vector3(pp.x, 20f, pp.y), Vector3.down, 40f))
                        { if (h.collider.name == "SurfaceSolid") hs = h.point.y; if (h.collider.name == "Surface") hl = h.point.y; }
                        sb.AppendLine($"  {x,5:F1}  {(t.SurfaceHeightSD(u * t.N, d) - e) / U.In,6:F1}  {(hs - e) / U.In,6:F1}  {(hl - e) / U.In,6:F1}");
                    }
                }
                // triangle check: every SurfaceSolid triangle around the exit -- normal tilt, and any folded (downward) faces
                MeshCollider solid = null; foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)) if (c.name == "SurfaceSolid") solid = c;
                var sm = solid.sharedMesh; var sv = sm.vertices; var st = sm.triangles; int down = 0, steep = 0, tot = 0; float minA = 9f, maxLen = 0f;
                for (int i = 0; i < st.Length; i += 3)
                {
                    Vector3 A = solid.transform.TransformPoint(sv[st[i]]), B = solid.transform.TransformPoint(sv[st[i + 1]]), C = solid.transform.TransformPoint(sv[st[i + 2]]);
                    var cen = (A + B + C) / 3f; int hh = -1; t.Locate(new Vector2(cen.x, cen.z), ref hh, out float cs, out float cd);
                    float cu = cs / t.N; if (cu < 0.58f || cu > 0.66f) continue;
                    float out_ = (cd - t.Frame(cs).halfWidth) / U.Ft; if (out_ < -2f) continue;
                    tot++; var nrm = Vector3.Cross(B - A, C - A); float area = nrm.magnitude * 0.5f; nrm.Normalize(); if (nrm.y < 0f) nrm = -nrm;
                    float longest = Mathf.Max((B - A).magnitude, (C - B).magnitude, (A - C).magnitude);
                    if (Vector3.Dot(Vector3.Cross(B - A, C - A), Vector3.up) < 0f) down++;
                    if (nrm.y < 0.95f) { steep++; if (steep <= 12) sb.AppendLine($"  steep tri u {cu:F4} out {out_:F1} ft normal ({nrm.x:F2},{nrm.y:F2},{nrm.z:F2}) area {area / (U.Ft * U.Ft):F2} sqft longest {longest / U.Ft:F1} ft"); }
                    minA = Mathf.Min(minA, area / (U.Ft * U.Ft)); maxLen = Mathf.Max(maxLen, longest / U.Ft);
                }
                sb.AppendLine($"triangles near the exit (u 0.58-0.66, outside half): {tot}, wound downward {down} (of all-up winding these should be 0 or all), steeper than 18 deg {steep}, smallest {minA:F3} sqft, longest edge {maxLen:F1} ft");
                w.Destroy(); File.WriteAllText(outPath, sb.ToString()); EditorApplication.Exit(0); return;
            }
            int ci = System.Array.IndexOf(a, "-okt-case");
            try
            {
                if (ci >= 0)
                {
                    var c = System.Array.ConvertAll(a[ci + 1].Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
                    Tr = new StringBuilder(); sb.AppendLine(Try(c[0], c[1], c[2], c[3], out _)); sb.Append(Tr); Tr = null;
                }
                else
                foreach (float v in new[] { 20f, 30f, 38f })
                    foreach (float outFt in (System.Array.IndexOf(a, "-okt-ontrack") >= 0 ? new[] { -12f, -8f, -4f } : new[] { 0.5f, 1.5f, 3f }))
                        foreach (float hdg in new[] { 0f, 10f, -15f })
                            foreach (float slide in new[] { 0f, 45f, 90f })
                            { string r = Try(v, outFt, hdg, slide, out bool br); n++; if (br) breaches++; sb.AppendLine(r); }
                sb.AppendLine($"RAIL-END SUMMARY: {n} runs, {breaches} came back over the rail onto the track.");
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); code = 1; }
            File.WriteAllText(outPath, sb.ToString()); Debug.Log(sb.ToString()); EditorApplication.Exit(code);
        }

        static StringBuilder Tr;
        static string Try(float mph, float outFt, float hdgDeg, float slideDeg, out bool breach)
        {
            breach = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = World.BuildRace(File.ReadAllText(M1SceneSetup.JsonPath), 9001, new RaceConfig { fieldSize = 1 });
            var dir = world.director; world.director = null;
            var K = world.karts[0]; var ai = world.drivers[0]; var tr = world.track;
            // the rail run that starts at the end of the track-exit opening
            float uRail = -1f;
            for (int i = 1; i < 2048; i++)
            {
                float uu = 0.5f + i / 2048f * 0.2f;
                if (tr.BarrierAt(uu - 0.2f / 2048f) == BarrierType.None && tr.BarrierAt(uu) == BarrierType.Guardrail) { uRail = uu; break; }
            }
            if (uRail < 0f) { world.director = dir; world.Destroy(); return "no rail start found"; }
            float lapFt = tr.centerLength / U.Ft;
            float u0 = uRail - 12f / lapFt;                                    // start 12 ft before the rail starts
            var f = tr.Frame(u0 * tr.N);
            Vector2 p = f.pos + f.outN * (f.halfWidth + outFt * U.Ft);
            float h = hdgDeg * Mathf.Deg2Rad;
            Vector2 d = f.tan * Mathf.Cos(h) - f.outN * Mathf.Sin(h);
            Place(K, p, d, mph * U.Mph, slideDeg);
            { var ar = System.Environment.GetCommandLineArgs(); int q = System.Array.IndexOf(ar, "-okt-ccd");
              if (q >= 0) K.Body.collisionDetectionMode = (CollisionDetectionMode)System.Enum.Parse(typeof(CollisionDetectionMode), ar[q + 1]); }
            var probe = Tr != null ? K.gameObject.AddComponent<ContactProbe>() : null;
            float peak = 0f, minIn = 9f; int hint = -1; float prevRel = 9f; bool touchedRail = false; string where = "";
            for (int i = 0; i < Mathf.RoundToInt(4f / World.FixedDt); i++)
            {
                ai.recovering = true; ai.recoverySpeed = 0f; ai.speedCap = 0f;   // hard on the brakes for the caution
                if (probe != null) probe.log.Clear();
                world.StepOnce();
                var pos = K.transform.position;
                tr.Locate(new Vector2(pos.x, pos.z), ref hint, out float s, out float dd);
                var fr = tr.Frame(s); float u = s / tr.N;
                float g = tr.SurfaceHeightSD(s, dd);
                peak = Mathf.Max(peak, pos.y - Mathf.Max(g, tr.InfieldHeight));
                if (dd > fr.halfWidth - 0.2f && Mathf.Abs(Mathf.Repeat(u - uRail + 0.5f, 1f) - 0.5f) < 3f / lapFt) touchedRail = true;
                float rel = (dd - fr.halfWidth) / U.Ft; minIn = Mathf.Min(minIn, rel);
                if (!breach && prevRel >= 0f && rel < 0f && tr.BarrierAt(u) != BarrierType.None)
                { breach = true; where = $" BREACH at u {u:F3}, {(pos.y - tr.SurfaceHeightSD(s, fr.halfWidth)) / U.Ft:F1} ft above the rail base"; }
                prevRel = rel;
                if (Tr != null && (i % 3 == 0 || (i * World.FixedDt > 0.05f && i * World.FixedDt < 0.13f)))
                {
                    var vv = K.Body.linearVelocity; var av = K.Body.angularVelocity; var up = K.transform.up;
                    var hitsC = new System.Collections.Generic.List<string>();
                    foreach (var col in K.GetComponentsInChildren<Collider>())
                    {
                        var bb = col.bounds; bb.Expand(0.04f);
                        foreach (var o in Physics.OverlapBox(bb.center, bb.extents, Quaternion.identity, ~(1 << KartPhysics.KartLayer), QueryTriggerInteraction.Ignore))
                        { string nm = col.name + ">" + o.name; if (!hitsC.Contains(nm)) hitsC.Add(nm); }
                    }
                    Tr.AppendLine($"  t {i * World.FixedDt:F3} u {u:F4} dOut {rel:F2}ft yGnd {(pos.y - g) / U.Ft:F2}ft yEdge {(pos.y - tr.SurfaceHeightSD(s, fr.halfWidth)) / U.Ft:F2}ft v ({vv.x:F1},{vv.y:F1},{vv.z:F1}) av ({av.x:F1},{av.y:F1},{av.z:F1}) up.y {up.y:F2} gnd {K.GroundedCount} load {K.load[0]:F0}/{K.load[1]:F0}/{K.load[2]:F0}/{K.load[3]:F0} | PROBE{(probe != null ? probe.log.ToString() : "")} | {string.Join(" ", hitsC)}");
                }
            }
            var endP = K.transform.position; tr.Locate(new Vector2(endP.x, endP.z), ref hint, out float se, out float de);
            float ue = se / tr.N; string ended = (de - tr.Frame(se).halfWidth) / U.Ft < 0f ? "on track" : "outside";
            bool side = K.transform.up.y < 0.3f; world.director = dir; world.Destroy();
            return $"{mph,2:F0} mph out {outFt,3:F1} ft hdg {hdgDeg,3:F0} slide {slideDeg,2:F0}: peak {peak / U.Ft:F1} ft, hit rail end {(touchedRail ? "y" : "n")}, ends {ended} u {ue:F3}{(breach ? where : "")}{(side ? " (on its side/roof)" : "")}";
        }

        static void Place(KartPhysics k, Vector2 p, Vector2 dir, float speed, float slideDeg)
        {
            var b = k.Body; float y = k.transform.position.y;
            if (Physics.Raycast(new Vector3(k.transform.position.x, y + 2f, k.transform.position.z), Vector3.down, out var h0, 5f, ~(1 << k.gameObject.layer)))
                y -= h0.point.y;
            float gy = 0f;
            if (Physics.Raycast(new Vector3(p.x, 10f, p.y), Vector3.down, out var h1, 30f, ~(1 << k.gameObject.layer))) gy = h1.point.y;
            var pos = new Vector3(p.x, gy + y, p.y); var rot = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
            b.position = pos; b.rotation = rot; k.transform.SetPositionAndRotation(pos, rot);
            b.linearVelocity = rot * Vector3.forward * speed; b.angularVelocity = Vector3.zero;
            // sliding: the body yawed left (nose down the track's inside, as a kart loose on entry) while it travels along dir
            var body = rot * Quaternion.Euler(0f, -slideDeg, 0f); b.rotation = body; k.transform.rotation = body;
        }
    }
}
