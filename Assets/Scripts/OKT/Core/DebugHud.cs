// Debug HUD (spec §10, crude by design): phase banner, lap counter, running order, caution
// count; Start race / Reset; speed Pause / 1x / 2x / 4x via extra physics steps (never
// Time.timeScale); SOAK runner; plus the M1 toggles (groove overlay, poor prep, Fast/Safe).
// Spin a kart / Flip a kart arrive with M3.
using System.Text;
using UnityEngine;

namespace OKT
{
    public class DebugHud : MonoBehaviour
    {
        public RaceBootstrap boot;
        bool showLine;
        GUIStyle box, banner;

        void OnGUI()
        {
            if (boot == null || boot.world == null) return;
            var w = boot.world; var d = w.director;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = true };
                box.normal.textColor = Color.white;
                banner = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold, richText = true };
                banner.normal.textColor = Color.white;
            }

            if (d != null) DrawBanner(d);

            GUILayout.BeginArea(new Rect(10, 10, 360, Screen.height - 20));
            GUILayout.Label("<b>OKT M2 — a race</b>   field seed " + boot.seed, box);

            if (boot.soakRunning)
            {
                GUILayout.Label(boot.soakStatus, box);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = d != null && d.phase == RacePhase.Grid;
            if (GUILayout.Button("Start race")) boot.StartRace();
            GUI.enabled = true;
            if (GUILayout.Button("Reset")) boot.ResetWorld(false);
            if (GUILayout.Button("New field")) boot.ResetWorld(true);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(boot.clock.speed == 0, "Pause", "Button")) boot.clock.speed = 0;
            if (GUILayout.Toggle(boot.clock.speed == 1, "1×", "Button")) boot.clock.speed = 1;
            if (GUILayout.Toggle(boot.clock.speed == 2, "2×", "Button")) boot.clock.speed = 2;
            if (GUILayout.Toggle(boot.clock.speed == 4, "4×", "Button")) boot.clock.speed = 4;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Corner speed {DriverConfig.DefaultCornerSlow:F2}", GUILayout.Width(120));
            if (GUILayout.Toggle(Mathf.Abs(DriverConfig.DefaultCornerSlow - 0.30f) < 0.001f, "Safe 0.30", "Button")) SetCorner(w, 0.30f);
            if (GUILayout.Toggle(Mathf.Abs(DriverConfig.DefaultCornerSlow - 0.27f) < 0.001f, "Fast 0.27", "Button")) SetCorner(w, 0.27f);
            GUILayout.EndHorizontal();

            bool nl = GUILayout.Toggle(showLine, " Show Franklin's groove (overlay)");
            if (nl != showLine) { showLine = nl; if (w.lineOverlay) w.lineOverlay.enabled = showLine; }
            bool poor = w.surfaceCfg.prepScale > 1.01f;
            bool np = GUILayout.Toggle(poor, " Poor track prep (roughness ×2)");
            if (np != poor) { w.surfaceCfg.prepScale = np ? 2f : 1f; w.RebuildSurface(); }

            if (GUILayout.Button("Soak: run 10 races unattended → soak-results.txt")) boot.StartSoak(10);
            if (!string.IsNullOrEmpty(boot.soakStatus)) GUILayout.Label(boot.soakStatus, box);

            if (d != null) GUILayout.Label(Order(d), box);
            GUILayout.EndArea();
        }

        static void SetCorner(World w, float v)
        {
            DriverConfig.DefaultCornerSlow = v;
            foreach (var a in w.drivers) a.cfg.cornerSlow = v;
        }

        void DrawBanner(RaceDirector d)
        {
            var lead = d.Leader();
            int lap = lead != null ? Mathf.Clamp(lead.lapsDone + 1, 1, d.cfg.featureLaps) : 0;
            string text = d.phase switch
            {
                RacePhase.Grid => "ON THE GRID — press Start race",
                RacePhase.Pace => "<color=#ffd84d>PACE LAP</color> — double file, no passing",
                RacePhase.Green => $"<color=#5fe35f>GREEN</color>   Lap {lap} / {d.cfg.featureLaps}",
                RacePhase.Caution => $"<color=#ffd84d>CAUTION</color>   {(d.oneToGo ? "ONE TO GO" : "single file")}   Lap {lap} / {d.cfg.featureLaps}",
                RacePhase.Checkered => "<color=#ffffff>CHECKERED</color>",
                _ => "RACE COMPLETE",
            };
            text += $"     cautions {d.cautions}";
            GUI.Label(new Rect(Screen.width / 2 - 230, 8, 460, 34), text, banner);
        }

        static string Order(RaceDirector d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<b>Pos  #    Driver      Laps  Gap     Last   Best</b>");
            var order = d.RunningOrder();
            var lead = order.Count > 0 ? order[0] : null;
            for (int i = 0; i < order.Count; i++)
            {
                var e = order[i];
                string gap = i == 0 ? "—" : e.finished || e.outOfRace ? "" : $"{(lead.ai.raceDist - e.ai.raceDist) / Mathf.Max(5f, e.kart.ForwardSpeed):F2}s";
                if (e.outOfRace) gap = "<color=#ff6060>OUT " + e.outReason + "</color>";
                else if (e.finished) gap = "<color=#bbbbbb>fin</color>";
                string last = e.laps.Count > 0 ? e.laps[e.laps.Count - 1].ToString("F2") : "—";
                float b = d.BestLap(e); string best = float.IsInfinity(b) ? "—" : b.ToString("F2");
                string tier = e.driver.tier == "fast" ? "★" : e.driver.tier == "new" ? "·" : " ";
                sb.AppendLine($"{i + 1,2}.  #{e.driver.number,-3}{tier} {e.driver.name,-10} {e.lapsDone,3}   {gap,-7} {last,6} {best,6}");
            }
            sb.AppendLine($"on-track passes {d.passes}   incidents {d.Incidents} (cautions judged per Franklin's rules)");
            return sb.ToString();
        }
    }
}
