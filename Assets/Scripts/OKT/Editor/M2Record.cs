// Race RECORDER: runs one race headless (no window, but with graphics) and saves a picture from the
// whole-track camera every few sim-seconds, so Claude can SEE races between Franklin's playtests.
//   Unity -batchmode -projectPath <proj> -executeMethod OKT.EditorTools.M2Record.Run
//         -okt-seed 9001 -okt-out <dir> [-okt-every 0.5] [-okt-size 2560x1440] [-okt-from 0] [-okt-to 420]
//         [-okt-staging 1] [-okt-focus u:outFt:radiusFt]  (close-up: aim at a point off the track, e.g. the grid gate)
// (Do NOT pass -nographics: the camera needs the GPU.) The sim steps exactly as in the soak -- the recorder
// only looks; it never changes timing, physics or Time.timeScale.
// Writes <dir>/f00000.jpg ..., frames.csv (time, phase, laps, latest event per frame), karts.csv (every kart's
// position in feet and speed in mph per frame) and events.txt.
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M2Record
    {
        const float MToFt = 3.28084f, MsToMph = 2.23694f;

        public static void Run()
        {
            var a = System.Environment.GetCommandLineArgs();
            int seed = 9001, w = 2560, h = 1440; float every = 0.5f, from = 0f, to = Soak.MaxRaceSeconds;
            string outDir = "record"; string focus = null;
            var ci = CultureInfo.InvariantCulture;
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == "-okt-seed") seed = int.Parse(a[i + 1]);
                if (a[i] == "-okt-out") outDir = a[i + 1];
                if (a[i] == "-okt-every") every = float.Parse(a[i + 1], ci);
                if (a[i] == "-okt-from") from = float.Parse(a[i + 1], ci);
                if (a[i] == "-okt-to") to = float.Parse(a[i + 1], ci);
                if (a[i] == "-okt-size") { var p = a[i + 1].Split('x'); w = int.Parse(p[0]); h = int.Parse(p[1]); }
                if (a[i] == "-okt-focus") focus = a[i + 1];
                if (a[i] == "-okt-staging") RaceConfig.DefaultStagingStart = a[i + 1] == "1";
            }
            int code = 0;
            try { Record(seed, outDir, every, from, to, w, h, focus); }
            catch (System.Exception e) { Debug.LogError("[OKT] RECORD EXCEPTION: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        static void Record(int seed, string outDir, float every, float from, float to, int w, int h, string focus)
        {
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string json = File.ReadAllText(M1SceneSetup.JsonPath);
            var world = World.BuildRace(json, seed, new RaceConfig());
            var d = world.director;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.2f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var camW = WholeTrackCamera.Ensure(world.track);
            var cam = camW.GetComponent<Camera>();
            cam.targetTexture = rt;
            camW.Fit();   // refit to the picture's aspect
            if (focus != null)
            {
                // close-up for checking placement (a diagnostic view, not the game camera): same viewing direction as the
                // whole-track camera, aimed at a point off the track, pulled back to fit radiusFt round it
                var fp = focus.Split(':'); var ci2 = CultureInfo.InvariantCulture;
                var at = world.track.OffTrackPoint(float.Parse(fp[0], ci2), float.Parse(fp[1], ci2));
                float r = float.Parse(fp[2], ci2) * 0.3048f;
                camW.enabled = false;
                var rot = Quaternion.Euler(camW.pitchDeg, camW.yawDeg, 0f);
                cam.transform.SetPositionAndRotation(at - rot * Vector3.forward * (r / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)), rot);
            }
            camW.enabled = false;   // no refit mid-recording
            cam.Render();           // warm-up: the first render after setup came out wrong (unlit, unfitted)
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);

            var frames = new StringBuilder("frame,t_s,phase,leader_laps,cautions,one_to_go,latest_event\n");
            var karts = new StringBuilder("frame,t_s,number,x_ft,z_ft,mph,mad,weaving,recovering,to_tail\n");
            d.Start();
            int cap = Mathf.RoundToInt(Mathf.Min(to, Soak.MaxRaceSeconds) / World.FixedDt);
            int stepsPer = Mathf.Max(1, Mathf.RoundToInt(every / World.FixedDt));
            int frame = 0, evSeen = 0; string lastEv = "";
            for (int s = 0; s <= cap && d.phase != RacePhase.Done; s++)
            {
                if (s > 0) world.StepOnce();
                if (s % stepsPer != 0 || d.raceTime < from) continue;
                world.UpdateVisuals(every);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, $"f{frame:D5}.jpg"), tex.EncodeToJPG(88));

                while (evSeen < d.events.Count) lastEv = d.events[evSeen++];
                var order = d.RunningOrder();
                int lead = order.Count > 0 ? order[0].lapsDone : 0;
                frames.AppendLine(string.Join(",", frame, d.raceTime.ToString("F2", CultureInfo.InvariantCulture), d.phase, lead,
                    d.cautions, d.oneToGo ? 1 : 0, "\"" + lastEv.Replace("\"", "'") + "\""));
                foreach (var e in d.field)
                {
                    if (e.kart == null) continue;
                    var p = e.kart.transform.position; float v = e.kart.Body != null ? e.kart.Body.linearVelocity.magnitude : 0f;
                    karts.AppendLine(string.Join(",", frame, d.raceTime.ToString("F2", CultureInfo.InvariantCulture), e.driver.number,
                        (p.x * MToFt).ToString("F1", CultureInfo.InvariantCulture), (p.z * MToFt).ToString("F1", CultureInfo.InvariantCulture),
                        (v * MsToMph).ToString("F1", CultureInfo.InvariantCulture), e.mad ? 1 : 0, e.weaving ? 1 : 0, e.recovering ? 1 : 0, e.toTail ? 1 : 0));
                }
                frame++;
            }
            File.WriteAllText(Path.Combine(outDir, "frames.csv"), frames.ToString());
            File.WriteAllText(Path.Combine(outDir, "karts.csv"), karts.ToString());
            File.WriteAllText(Path.Combine(outDir, "events.txt"), string.Join("\n", d.events));
            Debug.Log($"[OKT] RECORD: seed {seed}, {frame} frames to {outDir}, phase {d.phase} at {d.raceTime:F1} s");
            world.Destroy();
            Object.DestroyImmediate(rt);
        }
    }
}
