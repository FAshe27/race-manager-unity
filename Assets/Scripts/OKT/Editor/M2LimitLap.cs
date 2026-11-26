// The kart's limit lap (Franklin 11-22: he ran a 10.14 on the keyboard first time out; the best Clone lap he's ever seen
// at Atoka is a 10.2 -- perfect conditions, setup and tires). One dialed kart alone, perfect tires, driven harder and
// harder (corner slowing down, early throttle) until it spins: its fastest clean lap is the kart's limit at that grip.
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod OKT.EditorTools.M2LimitLap.Run -okt-out limit.txt
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M2LimitLap
    {
        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs(); string outPath = "limit.txt";
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-okt-out") outPath = a[i + 1];
            var grips = new[] { 1.3f, 1.2f, 1.1f, 1.0f };
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-okt-grips") grips = System.Array.ConvertAll(a[i + 1].Split(','), float.Parse);
            var sb = new StringBuilder("Limit lap: one dialed kart alone, 45 s, best clean lap (no spin) per corner-slowing setting.\n");
            int code = 0;
            try
            {
                float exit0 = KartAI.ExitSlowFrac;
                foreach (float g in grips)
                {
                    float limit = float.MaxValue; string at = "", atSegs = "", raceSegs = "";
                    var row = new StringBuilder($"grip {g:F2} g:");
                    foreach (float cs in new[] { 0.24f, 0.20f, 0.16f, 0.12f, 0.08f, 0.04f })
                    {
                        KartAI.ExitSlowFrac = 0.5f;
                        var (best, spins, segs) = Try(g, cs);
                        row.Append($"  cs {cs:F2}: {(best < 99f ? best.ToString("F2") : "--")}{(spins > 0 ? $" ({spins} spin)" : "")}");
                        if (spins == 0 && best < limit) { limit = best; at = $"cs {cs:F2}"; atSegs = segs; }
                        if (cs == 0.24f) raceSegs = segs;
                    }
                    KartAI.ExitSlowFrac = exit0;
                    sb.AppendLine(row.ToString());
                    sb.AppendLine($"  -> LIMIT {g:F2} g: {(limit < 99f ? limit.ToString("F2") : "--")} s ({at})   speeds (mph) on that lap: {atSegs}");
                    sb.AppendLine($"     race pace (cs 0.24) speeds (mph): {raceSegs}");
                }
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); code = 1; }
            File.WriteAllText(outPath, sb.ToString()); Debug.Log(sb.ToString()); EditorApplication.Exit(code);
        }

        static (float best, int spins, string segs) Try(float grip, float cornerSlow)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = World.BuildRace(File.ReadAllText(M1SceneSetup.JsonPath), 9001, new RaceConfig { fieldSize = 2 });
            var k = world.karts[0]; var ai = world.drivers[0];
            var other = world.karts[1]; other.transform.position += Vector3.up * 50f; other.Body.isKinematic = true;
            var dir = world.director; world.director = null;
            k.setup.gripG = grip; k.setup.frontGripScale = k.setup.rearGripScale = 1f;   // dialed, perfect tires
            ai.cfg.balanceGrip = 1f; ai.cfg.cornerSlow = cornerSlow; ai.cfg.pace = 1f; ai.reactSec = 0.04f; ai.kartFeel = 1f;
            ai.formation = false; ai.paceMode = false; ai.stagePath = null;
            int steps = Mathf.RoundToInt(45f / World.FixedDt), spins = 0; bool spun = false;
            float lastS = ai.s, lapStart = -1f, best = float.MaxValue, t = 0f;
            var segs = new System.Collections.Generic.List<string>(); string bestSegs = "";
            bool inCorner = world.track.Frame(ai.s).corner > 0.5f; float segMin = float.MaxValue, segMax = 0f;
            for (int i = 0; i < steps; i++)
            {
                world.StepOnce(); t += World.FixedDt;
                var vb = k.Body.linearVelocity; vb.y = 0f; var fb = k.transform.forward; fb.y = 0f;
                float ang = vb.magnitude > 3f ? Mathf.Abs(Vector3.SignedAngle(vb, fb, Vector3.up)) : 0f;
                if (!spun && ang > 60f) { spun = true; spins++; }
                if (spun && ang < 10f && vb.magnitude > 5f) spun = false;
                float vm = k.ForwardSpeed / U.Mph; bool corner = world.track.Frame(ai.s).corner > 0.5f;
                if (corner != inCorner) { segs.Add(inCorner ? $"corner min {segMin:F1}" : $"straight max {segMax:F1}"); segMin = float.MaxValue; segMax = 0f; inCorner = corner; }
                segMin = Mathf.Min(segMin, vm); segMax = Mathf.Max(segMax, vm);
                if (ai.s < lastS - world.track.centerLength * 0.5f)
                {
                    if (lapStart >= 0f && !spun && t - lapStart < best) { best = t - lapStart; bestSegs = string.Join(" | ", segs); }
                    lapStart = t; segs.Clear();
                }
                lastS = ai.s;
            }
            world.director = dir; world.Destroy();
            return (best, spins, bestSegs);
        }
    }
}
