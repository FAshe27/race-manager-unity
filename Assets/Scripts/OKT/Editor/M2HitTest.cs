// Hit test (2026-11-27, Franklin: "crashes often seem like karts slip off of each other when they hit a spun/stopped
// kart -- the karts don't have much weight to them"). One kart stopped across the backstretch (brakes on, as a spun
// driver holds them), another driven into it at race speed at several angles; reports how much speed the striker
// loses, how far it is deflected, and how far the stopped kart is shoved -- for each set of contact frictions.
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod OKT.EditorTools.M2HitTest.Run -okt-out hit.txt
//         [-okt-fric "panel,tube,tire;panel,tube,tire;..."]   (defaults: the current 0.15,0.3,0.35 plus heavier sets)
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M2HitTest
    {
        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs(); string outPath = "hit.txt";
            string fr = "0.15,0.3,0.35;0.35,0.5,0.7;0.5,0.6,0.9";
            for (int i = 0; i < a.Length - 1; i++) { if (a[i] == "-okt-out") outPath = a[i + 1]; if (a[i] == "-okt-fric") fr = a[i + 1]; }
            var sb = new StringBuilder("Hit test: striker at 38 mph into a stopped kart on the backstretch (target heading vs track; 90 = T-bone, 30 = nearly in line).\n" +
                                       "striker lifts and brakes 0.3 s after touching. dv = striker speed lost in 0.6 s after first touch; defl = striker heading change; shove = how far the stopped kart moved (in 1.5 s).\n");
            int code = 0;
            try
            {
                foreach (var set in fr.Split(';'))
                {
                    var f = System.Array.ConvertAll(set.Split(','), float.Parse);
                    sb.AppendLine($"frictions panel {f[0]:F2} tube {f[1]:F2} tire {f[2]:F2}:");
                    foreach (float ang in new[] { 90f, 60f, 30f, 120f })
                        sb.AppendLine("  " + Try(f[0], f[1], f[2], ang));
                }
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); code = 1; }
            File.WriteAllText(outPath, sb.ToString()); Debug.Log(sb.ToString()); EditorApplication.Exit(code);
        }

        static void SetFriction(KartPhysics k, float panel, float tube, float tire)
        {
            foreach (var c in k.GetComponentsInChildren<Collider>())
            {
                var m = c.sharedMaterial; if (m == null) continue;
                float v = m.name.StartsWith("BodyPanel") ? panel : m.name.StartsWith("TireBody") ? tire : m.name.StartsWith("KartBody") ? tube : -1f;
                if (v < 0f) continue;
                c.sharedMaterial = new PhysicsMaterial(m.name) { dynamicFriction = v, staticFriction = v, bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
            }
        }

        static void Place(KartPhysics k, Vector2 p, Vector2 dir, float speed)
        {
            var b = k.Body; float y = k.transform.position.y;
            if (Physics.Raycast(new Vector3(k.transform.position.x, y + 2f, k.transform.position.z), Vector3.down, out var h0, 5f, ~(1 << k.gameObject.layer)))
                y -= h0.point.y;                                      // ride height above the ground where it spawned
            float gy = 0f;
            if (Physics.Raycast(new Vector3(p.x, 10f, p.y), Vector3.down, out var h1, 30f, ~(1 << k.gameObject.layer))) gy = h1.point.y;
            var pos = new Vector3(p.x, gy + y, p.y); var rot = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
            b.position = pos; b.rotation = rot; k.transform.SetPositionAndRotation(pos, rot);
            b.linearVelocity = rot * Vector3.forward * speed; b.angularVelocity = Vector3.zero;
        }

        static StringBuilder Trace;
        static string Try(float panel, float tube, float tire, float angDeg)
        {
            if (angDeg == 90f && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-okt-trace") >= 0) Trace = new StringBuilder();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = World.BuildRace(File.ReadAllText(M1SceneSetup.JsonPath), 9001, new RaceConfig { fieldSize = 2 });
            var dir = world.director; world.director = null;
            var S = world.karts[0]; var T = world.karts[1]; var sAI = world.drivers[0]; var tAI = world.drivers[1];
            SetFriction(S, panel, tube, tire); SetFriction(T, panel, tube, tire);
            var tr = world.track; float u = 0.49f; var f = tr.Frame(u * tr.N);
            Vector2 tp = f.pos + f.outN * f.LineOffset;
            float a = angDeg * Mathf.Deg2Rad;
            Vector2 tdir = f.tan * Mathf.Cos(a) + f.outN * Mathf.Sin(a);     // target heading: angle off the track direction (toward the outside)
            Place(T, tp, tdir, 0f);
            var fs = tr.Frame((u - 0.035f) * tr.N);
            Place(S, fs.pos + fs.outN * fs.LineOffset, fs.tan, 38f * U.Mph);
            tAI.recovering = true; tAI.recoverySpeed = 0f; tAI.speedCap = 0f;            // stopped, holding the brakes
            sAI.speedCap = 38f * U.Mph; sAI.cfg.pace = 1f; sAI.formation = false; sAI.paceMode = false; sAI.stagePath = null;
            Vector3 t0 = T.transform.position; float touch = -1f, v0 = 0f, vAfter = -1f, h0 = 0f, hAfter = 0f, t = 0f; bool flipped = false;
            var kcS = S.GetComponent<KartContact>();
            for (int i = 0; i < Mathf.RoundToInt(4f / World.FixedDt); i++)
            {
                tAI.recovering = true; tAI.recoverySpeed = 0f; tAI.speedCap = 0f;
                if (touch >= 0f && t - touch > 0.3f) sAI.speedCap = 0f;   // the striker's reflex: off the gas, on the brakes
                world.StepOnce(); t += World.FixedDt;
                var vS = S.Body.linearVelocity; vS.y = 0f;
                bool touching = kcS != null && kcS.lastOther == T && world.simTime - kcS.lastContactTime < 0.05f;
                if (touch < 0f && touching) { touch = t; v0 = vS.magnitude; h0 = Mathf.Atan2(vS.x, vS.z) * Mathf.Rad2Deg; }
                if (touch >= 0f && vAfter < 0f && t - touch >= 0.6f) { vAfter = vS.magnitude; hAfter = Mathf.Atan2(vS.x, vS.z) * Mathf.Rad2Deg; }
                if (S.transform.up.y < 0.3f || T.transform.up.y < 0.3f) flipped = true;
                if (Trace != null && touch >= 0f && t - touch < 1.5f && Mathf.RoundToInt((t - touch) / World.FixedDt) % 6 == 0)
                {
                    var vT = T.Body.linearVelocity;
                    Trace.AppendLine($"    t+{t - touch:F2} S {vS.magnitude / U.Mph:F1} mph | T {new Vector2(vT.x, vT.z).magnitude / U.Mph:F1} mph up{T.transform.up.y:F2} gnd{(T.grounded[0] ? 1 : 0)}{(T.grounded[1] ? 1 : 0)}{(T.grounded[2] ? 1 : 0)}{(T.grounded[3] ? 1 : 0)} load {T.load[0]:F0}/{T.load[1]:F0}/{T.load[2]:F0}/{T.load[3]:F0} slip {T.slipDeg[0]:F0}/{T.slipDeg[1]:F0}/{T.slipDeg[2]:F0}/{T.slipDeg[3]:F0} touching {(touching ? 1 : 0)} lock{(T.lockBrakes ? 1 : 0)} acc{T.accelCmd:F1} thr{T.throttleCmd:F2} brk{T.brakeCmd:F2}");
                }
                if (touch >= 0f && t - touch > 1.5f) break;
            }
            Vector3 d = T.transform.position - t0; d.y = 0f;
            world.director = dir; world.Destroy();
            if (touch < 0f) return $"target {angDeg:F0} deg: no contact";
            if (Trace != null) { var tt = Trace.ToString(); Trace = null; return $"target {angDeg,3:F0} deg: hit at {v0 / U.Mph:F1} mph, shove {d.magnitude / U.Ft:F1} ft\n" + tt; }
            return $"target {angDeg,3:F0} deg: hit at {v0 / U.Mph:F1} mph -> {vAfter / U.Mph:F1} mph (dv {(v0 - vAfter) / U.Mph:F1}), defl {Mathf.Abs(Mathf.DeltaAngle(h0, hAfter)):F0} deg, shove {d.magnitude / U.Ft:F1} ft{(flipped ? "  FLIP" : "")}";
        }
    }
}
