// Rollover probe: a kart on the front straight tipped over its outside tires at several roll rates -- does it go up
// and over like a tossed toy car, or stop on its side? Run with and without the driver's body.
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod OKT.EditorTools.M2RollTest.Run -okt-out roll.txt
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M2RollTest
    {
        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs(); string outPath = "roll.txt";
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-okt-out") outPath = a[i + 1];
            var sb = new StringBuilder("Roll rates are about the outside tires' contact line (the kart pivots on them, CoM rising).\n");
            int code = 0;
            try
            {
                foreach (bool drv in new[] { false, true })
                {
                    KartSetup.DefaultDriverBody = drv;
                    sb.AppendLine(drv ? "WITH the driver's body:" : "WITHOUT the driver (as before):");
                    foreach (float w in new[] { 4f, 5f, 6f, 8f, 10f, 12f })
                        sb.AppendLine(Try(w));
                }
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); code = 1; }
            File.WriteAllText(outPath, sb.ToString()); Debug.Log(sb.ToString()); EditorApplication.Exit(code);
        }

        /// <summary>The kart body's centre of mass and inertia with and without the driver (they must match: he's shape only).
        ///   -executeMethod OKT.EditorTools.M2RollTest.Info -okt-out rbinfo.txt</summary>
        public static void Info()
        {
            var a = System.Environment.GetCommandLineArgs(); string outPath = "rbinfo.txt";
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-okt-out") outPath = a[i + 1];
            var sb = new StringBuilder();
            foreach (bool drv in new[] { false, true })
            {
                KartSetup.DefaultDriverBody = drv;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var world = World.BuildRace(File.ReadAllText(M1SceneSetup.JsonPath), 9001, new RaceConfig { fieldSize = 2 });
                var rb = world.karts[0].Body;
                sb.AppendLine($"driver {drv} at spawn: CoM {rb.centerOfMass * 39.37f} in, inertia {rb.inertiaTensor} rot {rb.inertiaTensorRotation.eulerAngles} autoCoM {rb.automaticCenterOfMass} autoI {rb.automaticInertiaTensor}");
                for (int i = 0; i < 60; i++) world.StepOnce();
                sb.AppendLine($"driver {drv} after 0.5 s: CoM {rb.centerOfMass * 39.37f} in, inertia {rb.inertiaTensor} rot {rb.inertiaTensorRotation.eulerAngles} autoCoM {rb.automaticCenterOfMass} autoI {rb.automaticInertiaTensor}");
                world.Destroy();
            }
            File.WriteAllText(outPath, sb.ToString()); EditorApplication.Exit(0);
        }

        static float Roll(Transform t) => Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(t.up, t.forward), t.forward);

        static string Try(float w)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = World.BuildRace(File.ReadAllText(M1SceneSetup.JsonPath), 9001, new RaceConfig { fieldSize = 2 });
            var k = world.karts[0]; var rb = k.Body; var st = k.setup;
            var other = world.karts[1]; other.transform.position += Vector3.up * 50f; other.Body.isKinematic = true;
            for (int i = 0; i < 120; i++) world.StepOnce();   // settle on the springs
            var omega = k.transform.forward * w;
            var com = rb.worldCenterOfMass;
            Vector3 best = Vector3.zero;
            foreach (float sgn in new[] { -1f, 1f })
            {
                var piv = k.transform.TransformPoint(new Vector3(sgn * (st.trackRear * 0.5f + st.TireWidth(2) * 0.5f), 0f, 0f));
                var v = Vector3.Cross(omega, com - piv);
                if (v.y > 0f) best = v;
            }
            rb.angularVelocity = omega; rb.linearVelocity = best;
            var sb = new StringBuilder($"  {w,4:F1} rad/s:");
            float maxAbs = 0f, total = 0f, prev = Roll(k.transform);
            for (int i = 0; i < 4 * 120; i++)
            {
                world.StepOnce();
                float r = Roll(k.transform); total += Mathf.DeltaAngle(prev, r); prev = r;
                maxAbs = Mathf.Max(maxAbs, Mathf.Abs(total));
                if (i % 30 == 29) sb.Append($" {r:F0}{(rb.isKinematic ? "K" : "")}/{rb.angularVelocity.magnitude:F1}");
            }
            float end = Mathf.Abs(Roll(k.transform));
            string rest = end < 30f ? "back on its wheels" : end < 135f ? "ON ITS SIDE" : "on its back";
            sb.Append($" | turned up to {maxAbs:F0} deg, came to rest {rest} ({Roll(k.transform):F0} deg)");
            int ev = 0; foreach (var e in world.director.events) if (ev++ < 6) sb.Append("\n      " + e);
            world.Destroy();
            return sb.ToString();
        }
    }
}
