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
            GUI.enabled = d != null && d.CanCaution;
            if (GUILayout.Button("Caution")) d.ManualCaution();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            // debug viewer cameras: the whole-track view is the game's; on-board / chase follow the chosen kart
            GUILayout.BeginHorizontal();
            GUILayout.Label("Camera (debug):", GUILayout.Width(110));
            int v = GUILayout.Toolbar((int)WholeTrackCamera.View, new[] { "Whole track", "On-board", "Chase" });
            WholeTrackCamera.View = (WholeTrackCamera.DebugView)v;
            var karts = boot.world.karts;
            if (karts.Count > 0)
            {
                int ki = Mathf.Max(0, karts.IndexOf(WholeTrackCamera.ViewKart));
                if (GUILayout.Button("<", GUILayout.Width(24))) ki = (ki + karts.Count - 1) % karts.Count;
                var e = d?.field.Find(x => x.kart == karts[ki]);
                GUILayout.Label(e != null ? $"#{e.driver.number} {e.driver.name}" : karts[ki].name, GUILayout.Width(120));
                if (GUILayout.Button(">", GUILayout.Width(24))) ki = (ki + 1) % karts.Count;
                WholeTrackCamera.ViewKart = karts[ki];
                // DEBUG test-drive the kart the camera is on (pad: LS steer, RT gas, LT brake; keys: arrows / WASD)
                bool driving = HumanDriver.Driven != null && HumanDriver.Driven.kart == karts[ki];
                bool want = GUILayout.Toggle(driving, "Drive it (debug)");
                if (want != driving) HumanDriver.Driven = want ? boot.world.drivers[ki] : null;
                else if (!want && HumanDriver.Driven != null && HumanDriver.Driven.kart != karts[ki]) HumanDriver.Driven = null;
            }
            GUILayout.EndHorizontal();
            if (HumanDriver.Driven != null)
                GUILayout.Label($"Driving #{(d?.field.Find(x => x.ai == HumanDriver.Driven)?.driver.number ?? 0)} with {(HumanDriver.LastDevice == "" ? "pad or keys" : HumanDriver.LastDevice)}: " +
                                "left stick / arrows steer, right trigger / Up gas, left trigger / Down brake. The AI drives it under yellow, pacing and staging.");

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(boot.clock.speed == 0, "Pause", "Button")) boot.clock.speed = 0;
            if (GUILayout.Toggle(boot.clock.speed == 1, "1×", "Button")) boot.clock.speed = 1;
            if (GUILayout.Toggle(boot.clock.speed == 2, "2×", "Button")) boot.clock.speed = 2;
            if (GUILayout.Toggle(boot.clock.speed == 4, "4×", "Button")) boot.clock.speed = 4;
            GUILayout.EndHorizontal();

            // initial start format (Franklin 11-18): takes effect on Reset / New field (the grid is laid out at the reset)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Start (Reset to apply)", GUILayout.Width(150));
            if (GUILayout.Toggle(!boot.raceCfg.singleFileStart, "Double file", "Button")) boot.raceCfg.singleFileStart = false;
            if (GUILayout.Toggle(boot.raceCfg.singleFileStart, "Single file", "Button")) boot.raceCfg.singleFileStart = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Corner speed {DriverConfig.DefaultCornerSlow:F2}", GUILayout.Width(120));
            if (GUILayout.Toggle(Mathf.Abs(DriverConfig.DefaultCornerSlow - 0.30f) < 0.001f, "Safe 0.30", "Button")) SetCorner(w, 0.30f);
            if (GUILayout.Toggle(Mathf.Abs(DriverConfig.DefaultCornerSlow - 0.24f) < 0.001f, "Race 0.24", "Button")) SetCorner(w, 0.24f); if (GUILayout.Toggle(Mathf.Abs(DriverConfig.DefaultCornerSlow - 0.20f) < 0.001f, "Edge 0.20", "Button")) SetCorner(w, 0.20f);
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
            DrawFlag(d, lead);
        }

        enum Flag { None, Green, Yellow, White, Checkered }

        /// <summary>The flagstand (Franklin, 11-16): green live, yellow caution, white one lap to go, checkered finish.</summary>
        static Flag CurrentFlag(RaceDirector d, RaceDirector.Entry lead)
        {
            switch (d.phase)
            {
                case RacePhase.Caution: return Flag.Yellow;
                case RacePhase.Checkered: case RacePhase.Done: return Flag.Checkered;
                case RacePhase.Green:
                    return lead != null && lead.lapsDone >= d.cfg.featureLaps - 1 ? Flag.White : Flag.Green;
                default: return Flag.None;
            }
        }

        void DrawFlag(RaceDirector d, RaceDirector.Entry lead)
        {
            var flag = CurrentFlag(d, lead);
            if (flag == Flag.None) return;
            const float fw = 96f, fh = 64f, pole = 4f;
            float x = Screen.width - fw - 24f, y = 12f;
            var tex = Texture2D.whiteTexture; var old = GUI.color;
            GUI.color = new Color(0.25f, 0.25f, 0.25f); GUI.DrawTexture(new Rect(x - pole, y, pole, fh + 40f), tex); // the stick
            var r = new Rect(x, y, fw, fh);
            GUI.color = Color.black; GUI.DrawTexture(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), tex); // outline
            if (flag == Flag.Checkered)
            {
                const int cols = 6, rows = 4; float cw = fw / cols, ch = fh / rows;
                for (int i = 0; i < cols; i++)
                    for (int j = 0; j < rows; j++)
                    {
                        GUI.color = (i + j) % 2 == 0 ? Color.white : Color.black;
                        GUI.DrawTexture(new Rect(x + i * cw, y + j * ch, cw, ch), tex);
                    }
            }
            else
            {
                GUI.color = flag == Flag.Green ? new Color(0.13f, 0.75f, 0.2f)
                          : flag == Flag.Yellow ? new Color(1f, 0.85f, 0.1f)
                          : Color.white;
                GUI.DrawTexture(r, tex);
            }
            GUI.color = old;
            string label = flag == Flag.Green ? "GREEN" : flag == Flag.Yellow ? (d.fsStage != 0 ? "FIELD STOPPED — LINING UP" : d.oneToGo ? "YELLOW — 1 TO GO" : "YELLOW")
                         : flag == Flag.White ? "WHITE — LAST LAP" : "CHECKERED";
            GUI.Label(new Rect(x - 40f, y + fh + 6f, fw + 60f, 24f), label, banner);
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
                else if (e.finished) gap = order[0].finished && e.finishLaps < order[0].finishLaps
                    ? $"<color=#bbbbbb>-{order[0].finishLaps - e.finishLaps}L</color>" : "<color=#bbbbbb>fin</color>";   // lapped: laps down
                string last = e.laps.Count > 0 ? e.laps[e.laps.Count - 1].ToString("F2") : "—";
                float b = d.BestLap(e); string best = float.IsInfinity(b) ? "—" : b.ToString("F2");
                string tier = e.driver.tier == "fast" ? "★" : e.driver.tier == "new" ? "·" : " ";
                sb.AppendLine($"{i + 1,2}.  #{e.driver.number,-3}{tier} {e.driver.name,-10} {(e.finished ? e.finishLaps : e.lapsDone),3}   {gap,-7} {last,6} {best,6}");
            }
            sb.AppendLine($"on-track passes {d.passes}   incidents {d.Incidents} (cautions judged per Franklin's rules)");
            return sb.ToString();
        }
    }
}
