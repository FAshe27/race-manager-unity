// One kart alone, racing laps, at a set balance (Franklin 11-20: karts should slide; loose ones up to ~15 deg).
// Separates how the kart and driver handle the slip-angle tire from what traffic does to it.
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod OKT.EditorTools.M2SlipSolo.Run -okt-out solo.txt
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M2SlipSolo
    {
        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs(); string outPath = "solo.txt";
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-okt-out") outPath = a[i + 1];
            var sb = new StringBuilder("Solo kart, 60 s of racing. Body angle = nose in from where it's travelling, corners only.\n");
            int code = 0;
            try
            {
                foreach (float peak in new[] { 6f, 10f, 15f })
                {
                    KartSetup.SlipCurve = true; KartSetup.SlipHeld = false; KartAI.VelocityPursuit = false; KartAI.SlipFeedForward = true;
                    KartSetup.DefaultSlipPeakDeg = peak;
                    sb.AppendLine($"slip tire, peak {peak:F0} deg (aim from nose + feedforward):");
                    foreach (float bal in new[] { -0.10f, 0f, 0.07f, 0.14f })
                        sb.AppendLine(Try(bal, 0.10f));
                }
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); code = 1; }
            File.WriteAllText(outPath, sb.ToString()); Debug.Log(sb.ToString()); EditorApplication.Exit(code);
        }

        static string Try(float bal, float react)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = World.BuildRace(File.ReadAllText(M1SceneSetup.JsonPath), 9001, new RaceConfig { fieldSize = 2 });
            var k = world.karts[0]; var ai = world.drivers[0];
            var other = world.karts[1]; other.transform.position += Vector3.up * 50f; other.Body.isKinematic = true;
            var dir = world.director; world.director = null;
            k.setup.rearGripScale = 1f - Mathf.Max(0f, bal); k.setup.frontGripScale = 1f - Mathf.Max(0f, -bal);
            ai.cfg.balanceGrip = 1f - (bal > 0f ? bal * RaceConfig.LooseFeel : -bal * RaceConfig.PushFeel);
            ai.reactSec = react;
            ai.formation = false; ai.paceMode = false; ai.stagePath = null;
            int steps = Mathf.RoundToInt(60f / World.FixedDt), spins = 0; bool spun = false;
            float sum = 0f, max = 0f, wide = 0f, spd = 0f; int n = 0; float lastS = ai.s; int laps = 0; float lapStart = -1f, best = float.MaxValue, t = 0f;
            for (int i = 0; i < steps; i++)
            {
                world.StepOnce(); t += World.FixedDt;
                var fr = world.track.Frame(ai.s);
                var vb = k.Body.linearVelocity; vb.y = 0f; var fb = k.transform.forward; fb.y = 0f;
                float v = vb.magnitude;
                float inside = v > 3f ? -Vector3.SignedAngle(vb, fb, Vector3.up) : 0f;
                if (fr.corner > 0.5f && v > 15f * U.Mph) { sum += inside; max = Mathf.Max(max, inside); wide += ai.d - fr.LineOffset; spd += v; n++; }
                if (!spun && Mathf.Abs(inside) > 60f) { spun = true; spins++; }
                if (spun && Mathf.Abs(inside) < 10f && v > 5f) spun = false;
                if (ai.s < lastS - world.track.centerLength * 0.5f) { if (lapStart >= 0f) best = Mathf.Min(best, t - lapStart); lapStart = t; laps++; }
                lastS = ai.s;
            }
            string s = $"  bal {bal:+0.00;-0.00} react {react:F2}: body mean {(n > 0 ? sum / n : 0):F1} max {max:F0} deg, spins {spins}, " +
                       $"best lap {(best < 99f ? best : 0):F2} s, corner {(n > 0 ? spd / n / U.Mph : 0):F1} mph, wide {(n > 0 ? wide / n / U.Ft : 0):F1} ft";
            world.director = dir; world.Destroy();
            return s;
        }
    }
}
