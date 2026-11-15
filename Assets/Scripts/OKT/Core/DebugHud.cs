// Crude M1 debug HUD (spec §10 spirit, M1 scope): speed controls via physics
// steps, lap times, the kart's suspension/air telemetry, and the drive-over
// test. Race buttons (Start race / Spin / Flip) belong to M2/M3.
using System.Text;
using UnityEngine;

namespace OKT
{
    public class DebugHud : MonoBehaviour
    {
        public RaceBootstrap boot;
        bool showLine;
        string driveOverMsg = "";
        float doMaxAir, doMaxComp;
        bool doWatching;
        GUIStyle box;

        void OnGUI()
        {
            if (boot == null || boot.world == null) return;
            var w = boot.world;
            if (box == null) { box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true }; box.normal.textColor = Color.white; }

            GUILayout.BeginArea(new Rect(10, 10, 330, Screen.height - 20));
            GUILayout.Label("<b>OKT M1 — a kart laps a real surface</b>", box);

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(boot.clock.speed == 0, "Pause", "Button")) boot.clock.speed = 0;
            if (GUILayout.Toggle(boot.clock.speed == 1, "1×", "Button")) boot.clock.speed = 1;
            if (GUILayout.Toggle(boot.clock.speed == 2, "2×", "Button")) boot.clock.speed = 2;
            if (GUILayout.Button("Reset")) { boot.ResetWorld(); driveOverMsg = ""; doWatching = false; }
            GUILayout.EndHorizontal();

            bool nl = GUILayout.Toggle(showLine, " Show Franklin's groove (overlay)");
            if (nl != showLine) { showLine = nl; if (w.lineOverlay) w.lineOverlay.enabled = showLine; }

            bool poor = w.surfaceCfg.prepScale > 1.01f;
            bool np = GUILayout.Toggle(poor, " Poor track prep (roughness ×2 = ±1.6 in)");
            if (np != poor) { w.surfaceCfg.prepScale = np ? 2f : 1f; w.RebuildSurface(); }

            if (w.drivers.Count > 0)
            {
                // Corner-speed choice for playtesting: 0.27 = Franklin's fast-lap window (~10.77 s, little margin);
                // 0.30 = ~11.03 s, more margin when knocked off the groove.
                var dc = w.drivers[0].cfg;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Corner speed: {dc.cornerSlow:F2}", GUILayout.Width(130));
                if (GUILayout.Toggle(Mathf.Abs(dc.cornerSlow - 0.27f) < 0.001f, "Fast 0.27", "Button")) dc.cornerSlow = 0.27f;
                if (GUILayout.Toggle(Mathf.Abs(dc.cornerSlow - 0.30f) < 0.001f, "Safe 0.30", "Button")) dc.cornerSlow = 0.30f;
                GUILayout.EndHorizontal();
            }

            if (w.drivers.Count > 0)
            {
                var ai = w.drivers[0]; var k = w.karts[0];
                if (GUILayout.Button(ai.driveOverArmed || ai.driveOverActive ? "Drive-over: on its way…" : "Drive-over test: cross the roughest patch"))
                {
                    w.track.FindRoughestPoint(out ai.driveOverS, out ai.driveOverD, out float hb);
                    ai.driveOverArmed = true; doWatching = true; doMaxAir = 0; doMaxComp = 0;
                    k.maxAirTime = 0; k.maxCompression = 0;
                    ai.onDriveOverDone = a => { doWatching = false; driveOverMsg = $"Drive-over done: max air {doMaxAir:F2} s, max compression {doMaxComp / U.In:F2} in"; };
                    driveOverMsg = $"Patch: u={ai.driveOverS / w.track.N:F3}, {U.FtS(ai.driveOverD)} from center, rise {U.InS(hb, 2)}";
                }
                if (doWatching) { doMaxAir = Mathf.Max(doMaxAir, k.maxAirTime); doMaxComp = Mathf.Max(doMaxComp, k.maxCompression); }

                var sb = new StringBuilder();
                sb.AppendLine($"Sim time {w.simTime:F1} s   steps {w.steps}");
                sb.AppendLine($"Laps {ai.lapsCompleted}   last {Fmt(ai.lastLap)}   best {Fmt(ai.bestLap)}   (Clone band 10.5–11.5)");
                int from = Mathf.Max(0, ai.lapTimes.Count - 6);
                for (int i = from; i < ai.lapTimes.Count; i++) sb.Append($"L{i + 1} {ai.lapTimes[i]:F2}  ");
                sb.AppendLine();
                sb.AppendLine($"Speed {U.MphS(k.ForwardSpeed)}   target {U.MphS(ai.targetSpeed)}   engine {k.EngineRpm:F0} rpm   throttle {k.Throttle * 100f:F0}%");
                sb.AppendLine($"Steer {k.steerDeg:F1}°   lap pos u={ai.s / w.track.N:F3}   from center {U.FtS(ai.d)}");
                sb.AppendLine($"Wheels  LF {W(k, 0)}  RF {W(k, 1)}  LR {W(k, 2)}  RR {W(k, 3)}");
                sb.AppendLine($"Grounded {k.GroundedCount}/4   air {k.airTime:F2} s   max air {k.maxAirTime:F2} s");
                sb.AppendLine($"Closest body→barrier {U.InS(ai.minWallGap)}   wall breaches {ai.bandBreachSteps}   off-track steps {ai.offTrackSteps}");
                if (k.UpsideDown) sb.AppendLine("<color=red>UPSIDE DOWN</color>");
                if (driveOverMsg.Length > 0) sb.AppendLine(driveOverMsg);
                GUILayout.Label(sb.ToString(), box);
            }
            GUILayout.EndArea();
        }

        static string Fmt(float t) => float.IsInfinity(t) || t <= 0 ? "—" : t.ToString("F2");
        static string W(KartPhysics k, int i) => k.grounded[i] ? $"{k.compression[i] / U.In:F2}in{(k.sliding[i] ? " slide" : "")}{(k.dust[i] > 0.01f ? $" dust{k.dust[i] * 100f:F0}%" : "")}{(k.surfaceGripNow[i] < 0.99f ? $" grip{k.surfaceGripNow[i] * 100f:F0}%" : "")}" : "air";
    }
}
