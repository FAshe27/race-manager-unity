// Inside-edge probe (Franklin's playtest 11-20: a kart pushed to the inside on a three-wide start "hit a cone", stopped
// dead and bounced up into traffic). The cones have no colliders, so what stops it must be the inside edge itself: the
// low ridge of loose dirt and the band beyond it. One kart, alone, drives down the straight and eases over toward a
// lane past the inside edge at a set speed; this logs how hard it slows and what it touches.
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod OKT.EditorTools.M2RidgeTest.Run -okt-out ridge.txt
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M2RidgeTest
    {
        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs(); string outPath = "ridge.txt";
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-okt-out") outPath = a[i + 1];
            var sb = new StringBuilder("Lane is a fraction of the half-width (-1 = the inside edge). Decel in g, worst 0.05 s.\n");
            int code = 0;
            try
            {
                foreach (float mph in new[] { 15f, 25f, 35f })
                    foreach (float lane in new[] { -1.05f, -1.15f, -1.3f, -1.5f })
                        sb.AppendLine(Try(mph, lane));
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); code = 1; }
            File.WriteAllText(outPath, sb.ToString()); Debug.Log(sb.ToString()); EditorApplication.Exit(code);
        }

        static string Try(float mph, float lane)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = World.BuildRace(File.ReadAllText(M1SceneSetup.JsonPath), 9001, new RaceConfig { fieldSize = 2 });
            var k = world.karts[0]; var ai = world.drivers[0];
            var other = world.karts[1]; other.transform.position += Vector3.up * 50f; other.Body.isKinematic = true;
            var dir = world.director; world.director = null;        // no race control: this kart just drives
            var kc = k.GetComponent<KartContact>(); if (kc != null) kc.world = world;
            ai.formation = true; ai.paceMode = true; ai.laneFrac = lane;
            ai.paceStraight = ai.paceCorner = mph * U.Mph;
            float vPrev = 0f, worst = 0f, minIn = float.MaxValue, vAtWorst = 0f; string what = "", whatWorst = "";
            float maxRoll = 0f, maxPitch = 0f; bool airborne = false;
            var touched = new System.Collections.Generic.HashSet<string>();
            int steps = Mathf.RoundToInt(10f / World.FixedDt);
            for (int i = 0; i < steps; i++)
            {
                ai.formation = true; ai.paceMode = true; ai.laneFrac = lane; ai.paceStraight = ai.paceCorner = mph * U.Mph;
                world.StepOnce();
                if (kc != null && !string.IsNullOrEmpty(kc.lastTrackContact))
                {
                    what = kc.lastTrackContact; var nm = what.Split(' ')[0]; touched.Add(nm);
                }
                if (i % 6 == 5) // every 0.05 s
                {
                    float v = k.Body.linearVelocity.magnitude;
                    float dec = (vPrev - v) / (6 * World.FixedDt) / 9.81f;
                    var f = world.track.Frame(ai.s); float fromIn = (ai.d + f.halfWidth) / U.Ft;
                    if (i > 240 && dec > worst) { worst = dec; vAtWorst = vPrev / U.Mph; whatWorst = what; }
                    minIn = Mathf.Min(minIn, fromIn);
                    vPrev = v;
                    var up = k.transform.up;
                    maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(up, k.transform.forward), k.transform.forward)));
                    maxPitch = Mathf.Max(maxPitch, Mathf.Abs(Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(up, k.transform.right), k.transform.right)));
                }
            }
            float vEnd = k.Body.linearVelocity.magnitude / U.Mph;
            string s = $"  {mph,2:F0} mph lane {lane:F2}: deepest {-minIn:F1} ft past the inside edge, worst decel {worst:F1} g at {vAtWorst:F0} mph ({whatWorst}), " +
                       $"end {vEnd:F0} mph, max roll {maxRoll:F0} pitch {maxPitch:F0} | touched: {string.Join(", ", touched)}";
            world.director = dir;
            world.Destroy();
            return s;
        }
    }
}
