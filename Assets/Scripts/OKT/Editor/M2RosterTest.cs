// Checks the hand-set field: names, numbers, colors and grid order come from the roster.
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod OKT.EditorTools.M2RosterTest.Run -okt-out roster.txt
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M2RosterTest
    {
        static FieldRoster Make()
        {
            var r = ScriptableObject.CreateInstance<FieldRoster>(); r.useRoster = true; r.hideFlags = HideFlags.DontUnloadUnusedAsset;
            string[] names = { "Ashe", "Germain", "DeMarco", "Smith", "Jones", "Brown", "Davis", "Miller" };
            for (int i = 0; i < names.Length; i++)
                r.drivers.Add(new RosterEntry { name = names[i], number = 10 + i, color = Color.HSVToRGB(i / 8f, 0.8f, 0.9f),
                                                tier = i == 0 ? RosterTier.Fast : RosterTier.Auto, startPosition = i == 5 ? 1 : (i == 0 ? 8 : 0) });
            return r;
        }

        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs(); string outPath = "roster.txt";
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-okt-out") outPath = a[i + 1];
            var sb = new StringBuilder(); int code = 0;
            try
            {
                var r = Make();
                foreach (var g in new[] { RosterGrid.Qualifying, RosterGrid.AsListed })
                {
                    r.gridOrder = g;
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    if (r == null) { sb.AppendLine("  (roster was unloaded by the new scene: rebuilt)"); r = Make(); r.gridOrder = g; }
                    sb.AppendLine($"  roster null={(r == null)} refnull={ReferenceEquals(r, null)} active={(r != null && r.Active)} count={r.drivers.Count}");
                    var rc = new RaceConfig { roster = r }; sb.AppendLine($"  rc.roster null={(rc.roster == null)}");
                    sb.AppendLine("  direct Make: " + string.Join(", ", FieldGen.Make(12, 4242, 11.03f, r).ConvertAll(d => d.name)));
                    var w = World.BuildRace(File.ReadAllText(M1SceneSetup.JsonPath), 4242, new RaceConfig { roster = r });
                    sb.AppendLine($"{g}: {w.drivers12.Count} karts, grid: " + string.Join(", ", w.drivers12.ConvertAll(d => $"#{d.number} {d.name} ({d.tier})")));
                    var k0 = w.karts[0]; sb.AppendLine($"  kart 0 object '{k0.name}'");
                    w.Destroy();
                }
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); code = 1; }
            File.WriteAllText(outPath, sb.ToString()); Debug.Log(sb.ToString()); EditorApplication.Exit(code);
        }
    }
}
