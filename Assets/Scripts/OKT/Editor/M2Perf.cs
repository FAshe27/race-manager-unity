// Performance harness: where does a physics step's time go, at 12 / 24 / 40 karts?
//   Unity -batchmode [-nographics] -projectPath <proj> -executeMethod OKT.EditorTools.M2Perf.Run
//         [-okt-fields 12,24,40] [-okt-seed 9001] [-okt-secs 60] [-okt-render 1] -okt-out perf.txt
// Runs each field to 5 s after the green, then times the next N sim-seconds of racing, part by part
// (World.StepTimed: same calls, same order). -okt-render 1 (needs graphics) also times what a 60 fps frame
// adds: kart visuals + one whole-track camera render at 1920x1080 every second step.
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M2Perf
    {
        static bool NoCallbacks;   // experiment: what do the contact callbacks cost inside Physics.Simulate?

        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs();
            string fields = "12,24,40", outPath = "perf.txt"; int seed = 9001; float secs = 60f; bool render = false;
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == "-okt-fields") fields = a[i + 1];
                if (a[i] == "-okt-seed") seed = int.Parse(a[i + 1]);
                if (a[i] == "-okt-secs") secs = float.Parse(a[i + 1], CultureInfo.InvariantCulture);
                if (a[i] == "-okt-render") render = a[i + 1] == "1";
                if (a[i] == "-okt-out") outPath = a[i + 1];
                if (a[i] == "-okt-nocb") NoCallbacks = a[i + 1] == "1";
            }
            var sb = new StringBuilder($"OKT perf — seed {seed}, {secs:F0} sim-s of green per field, {Mathf.RoundToInt(1f / World.FixedDt)} Hz\n");
            int code = 0;
            try
            {
                foreach (var fs in fields.Split(','))
                {
                    int n = int.Parse(fs);
                    sb.AppendLine(Measure(n, seed, secs, render && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null));
                    File.WriteAllText(outPath, sb.ToString());
                }
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION: " + e); code = 1; }
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log(sb.ToString());
            EditorApplication.Exit(code);
        }

        static string Measure(int n, int seed, float secs, bool render)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string json = File.ReadAllText(M1SceneSetup.JsonPath);
            var world = World.BuildRace(json, seed, new RaceConfig { fieldSize = n });
            var d = world.director;
            Camera cam = null; RenderTexture rt = null;
            if (render)
            {
                var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
                rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
                var cw = WholeTrackCamera.Ensure(world.track); cam = cw.GetComponent<Camera>(); cam.targetTexture = rt; cw.Fit(); cw.enabled = false;
            }
            d.Start();
            int guard = Mathf.RoundToInt(Soak.MaxRaceSeconds / World.FixedDt);
            for (int i = 0; i < guard && !(d.phase == RacePhase.Green && d.raceTime > d.firstGreenTime + 5f); i++) world.StepOnce();
            if (NoCallbacks) foreach (var k in world.karts) Object.DestroyImmediate(k.GetComponent<KartContact>());
            Perf.Reset(); Perf.On = true;
            long vis = 0, ren = 0, frames = 0; var sw = Perf.Sw;
            int steps = Mathf.RoundToInt(secs / World.FixedDt), cautionSteps = 0;
            for (int i = 0; i < steps && d.phase != RacePhase.Done; i++)
            {
                world.StepOnce();
                if (d.phase == RacePhase.Caution) cautionSteps++;
                if (render && (i & 1) == 1)
                {
                    long t0 = sw.ElapsedTicks; world.UpdateVisuals(World.FixedDt * 2f);
                    long t1 = sw.ElapsedTicks; cam.Render(); GL.Flush();
                    long t2 = sw.ElapsedTicks; vis += t1 - t0; ren += t2 - t1; frames++;
                }
            }
            Perf.On = false;
            string rep = $"{n} karts: " + Perf.Report() + $"; caution {100f * cautionSteps / Mathf.Max(1, Perf.Steps):F0}% of steps";
            rep += "\n   sections:" + Perf.Sections();
            double stepMs = (Perf.Pre + Perf.Ai + Perf.Kart + Perf.Phys + Perf.Post) * 1000.0 / System.Diagnostics.Stopwatch.Frequency / System.Math.Max(1, Perf.Steps);
            rep += $"\n   => sim alone at 120 Hz costs {stepMs * 120:F0} ms per real second ({(stepMs * 120 < 1000 ? "keeps up" : "CAN'T keep up")}); a 60 fps frame needs 2 steps = {stepMs * 2:F2} ms of the 16.7 ms budget";
            if (render && frames > 0)
            {
                double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency / frames;
                rep += $"\n   render (1920x1080, CPU side): visuals {vis * f:F2} ms + camera {ren * f:F2} ms per frame";
            }
            world.Destroy();
            if (rt != null) Object.DestroyImmediate(rt);
            return rep;
        }
    }
}
