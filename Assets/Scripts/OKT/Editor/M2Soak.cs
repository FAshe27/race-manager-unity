// Batch SOAK for M2:
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod OKT.EditorTools.M2Soak.Run -okt-races 10 -okt-out soak-results.txt
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M2Soak
    {
        [MenuItem("OKT/Run M2 Soak (10 races, headless)")]
        public static void RunFromMenu() { Debug.Log(Execute(10, "soak-results.txt", 1001)); }

        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs();
            int n = 10, seed0 = 1001; string outPath = "soak-results.txt";
            for (int i = 0; i < a.Length; i++)
            {
                if (i < a.Length - 1 && a[i] == "-okt-races") n = int.Parse(a[i + 1]);
                if (a[i] == "-okt-out") outPath = a[i + 1];
                if (a[i] == "-okt-seed") seed0 = int.Parse(a[i + 1]);
                if (a[i] == "-okt-trace") Soak.TraceFirstGreen = true;
                if (a[i] == "-okt-slide") KartSetup.DefaultSlideGripRatio = float.Parse(a[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                if (a[i] == "-okt-fade") { var fp = a[i + 1].Split(':'); KartSetup.DefaultSlideFadeStartDeg = float.Parse(fp[0], System.Globalization.CultureInfo.InvariantCulture); KartSetup.DefaultSlideFadeEndDeg = float.Parse(fp[1], System.Globalization.CultureInfo.InvariantCulture); }
                if (a[i] == "-okt-feather") { var fp = a[i + 1].Split(':'); DriverConfig.DefaultFeatherStartDeg = float.Parse(fp[0], System.Globalization.CultureInfo.InvariantCulture); DriverConfig.DefaultFeatherRangeDeg = float.Parse(fp[1], System.Globalization.CultureInfo.InvariantCulture); }
                if (a[i] == "-okt-tirefeel") DriverConfig.UseTireFeel = a[i + 1] == "1";
                if (a[i] == "-okt-lightprep") { var lp = a[i + 1].Split(':'); RaceConfig.LightPrepChance = float.Parse(lp[0], System.Globalization.CultureInfo.InvariantCulture); RaceConfig.LightPrepFalloffMin = float.Parse(lp[1], System.Globalization.CultureInfo.InvariantCulture); RaceConfig.LightPrepFalloffMax = float.Parse(lp[2], System.Globalization.CultureInfo.InvariantCulture); }
                if (a[i] == "-okt-bigpush") RaceConfig.DefaultBigPushChance = float.Parse(a[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                if (a[i] == "-okt-staging") RaceConfig.DefaultStagingStart = a[i + 1] == "1";
                if (a[i] == "-okt-cs") DriverConfig.DefaultCornerSlow = float.Parse(a[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            }
            int code = 0; string rep;
            try { rep = Execute(n, outPath, seed0); } catch (System.Exception e) { rep = "EXCEPTION: " + e; code = 1; File.WriteAllText(outPath, rep); }
            Debug.Log(rep);
            EditorApplication.Exit(code);
        }

        static string Execute(int n, string outPath, int seed0)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string json = File.ReadAllText(M1SceneSetup.JsonPath);
            var rc = new RaceConfig();
            var sb = new StringBuilder(Soak.Header(n, rc));
            var st = new SoakStats();
            var t0 = System.DateTime.Now;
            for (int i = 0; i < n; i++)
            {
                var w = Soak.RunRace(json, seed0 + i, rc);
                if (i == 0) sb.Append(w.log);
                Soak.Record(w, seed0 + i, sb, st);
                w.Destroy();
                File.WriteAllText(outPath, sb + "\n(in progress)\n");
            }
            sb.AppendLine();
            sb.AppendLine(Soak.Summary(st));
            sb.AppendLine($"Wall-clock {(System.DateTime.Now - t0).TotalSeconds:F0} s.");
            File.WriteAllText(outPath, sb.ToString());
            return sb.ToString();
        }
    }
}
