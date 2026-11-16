// SOAK mode (Muse's 18:00 M2 request): run N races unattended at high step rate and write
// soak-results.txt with, per race: cautions, wall breaches (checked every physics step),
// clean-air best laps per kart, finish order, DNF count — plus the M2 incident stub counts.
// Used by the HUD's Soak button (play mode) and by OKT.EditorTools.M2Soak (batch mode).
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OKT
{
    public class SoakStats
    {
        public int races, finished, cautions, breaches, incidents, dnfs;
        public readonly List<float> cleanBest = new List<float>();
        public readonly List<float> winnerTimes = new List<float>();
        public float minClean = float.PositiveInfinity, maxClean;
        public int offTrackSteps;
        public int passes; public float moved;
        public int cSpin, sSpin, cFlip, sFlip, contacts, hard;
        public readonly List<int> winGrid = new List<int>(), winPace = new List<int>();
    }

    public static class Soak
    {
        public const float MaxRaceSeconds = 420f;
        public static bool TraceFirstGreen;

        /// <summary>Run one race to completion (or the time cap). Returns the director's world; caller destroys it.</summary>
        public static World RunRace(string json, int seed, RaceConfig rc = null)
        {
            var w = World.BuildRace(json, seed, rc);
            if (TraceFirstGreen) w.director.trace = new StringBuilder();
            w.director.Start();
            int cap = Mathf.RoundToInt(MaxRaceSeconds / World.FixedDt);
            for (int i = 0; i < cap && w.director.phase != RacePhase.Done; i++) w.StepOnce();
            return w;
        }

        /// <summary>Append one race's block to the report and fold it into the totals.</summary>
        public static void Record(World w, int seed, StringBuilder sb, SoakStats st)
        {
            var d = w.director;
            st.races++;
            bool done = d.phase == RacePhase.Done;
            if (done) st.finished++;
            st.cautions += d.cautions; st.breaches += d.WallBreaches; st.incidents += d.Incidents; st.dnfs += d.Dnfs;
            foreach (var e in d.field) st.offTrackSteps += e.ai.offTrackSteps;
            var order = d.RunningOrder();
            var win = order.Count > 0 ? order[0] : null;
            if (win != null && win.finished)
            {
                st.winnerTimes.Add(win.finishTime - d.firstGreenTime);
                st.winGrid.Add(win.gridPos + 1);
                // feature-pace rank of the winner (grid is qualifying order, which can differ)
                var byPace = new List<RaceDirector.Entry>(d.field); byPace.Sort((x, y) => x.driver.paceOffsetSec.CompareTo(y.driver.paceOffsetSec));
                st.winPace.Add(byPace.IndexOf(win) + 1);
            }

            sb.AppendLine($"Race {st.races} (seed {seed}): {(done ? "complete" : "TIME CAP (did not finish)")}, " +
                          $"sim {d.raceTime:F0} s, from green flag {(d.firstGreenTime >= 0 ? (d.raceTime - d.firstGreenTime) : 0):F0} s");
            sb.AppendLine($"  cautions {d.cautions} (Franklin's rules)   " +
                          $"wall breaches {d.WallBreaches}   incidents {d.Incidents} (spins/flips/stops)   DNF {d.Dnfs}   " +
                          $"on-track passes {d.passes}   mean positions moved vs grid {d.MeanPositionsMoved:F1}");
            sb.AppendLine($"  pass logic (kart-steps): ahead {d.dbgAhead} close {d.dbgClose} faster {d.dbgFaster} heldUp {d.dbgHeld} want {d.dbgWant} insideOk {d.dbgInsideOk} outsideOk {d.dbgOutsideOk} started {d.dbgStarted} hazardAhead {d.dbgHaz} pushes {d.dbgPushes} push->dive {d.dbgPushPasses} completed {d.dbgCompleted} | exit runs: seen {d.dbgRuns} started {d.dbgRunStarted} done {d.dbgRunDone} | bump-draft steps {d.dbgBumpDraft}");
            sb.AppendLine($"  packed start at the green: largest consecutive gap {d.greenMaxGapLaps:F4} laps ({d.greenMaxGapLaps * d.LapLengthM / U.Ft:F1} ft centre to centre), leader to last {d.greenSpreadLaps:F3} laps");
            sb.AppendLine($"  contacts {d.Contacts} (hard {d.HardContacts})   spins: contact {d.contactSpins} / self {d.selfSpins}   flips: contact {d.contactFlips} / self {d.selfFlips}");
            st.cSpin += d.contactSpins; st.sSpin += d.selfSpins; st.cFlip += d.contactFlips; st.sFlip += d.selfFlips; st.contacts += d.Contacts; st.hard += d.HardContacts;
            st.passes += d.passes; st.moved += d.MeanPositionsMoved;
            sb.Append("  finish: ");
            for (int i = 0; i < order.Count; i++)
            {
                var e = order[i];
                string st2 = e.finished ? $"{e.lapsDone}L" : e.outOfRace ? $"OUT-{e.outReason}" : $"{e.lapsDone}L running";
                sb.Append($"{i + 1}. #{e.driver.number}({e.driver.tier[0]},g{e.gridPos + 1},{e.driver.axleTeeth}{(e.driver.tireNote == "light prep" ? ",lp" : e.driver.tireNote == "over-prepped" ? ",op" : "")}) {st2}  ");
            }
            sb.AppendLine();
            // interval to the kart ahead (s) at each S/F crossing, top 6 finishers: does anybody close on the leader?
            float Cross(RaceDirector.Entry e, int k) { float t = e.lastCross; for (int j = e.laps.Count - 1; j >= k; j--) t -= e.laps[j]; return t; }
            for (int i = 1; i < Mathf.Min(6, order.Count); i++)
            {
                var e = order[i]; var a = order[i - 1];
                if (e.laps.Count < 2 || a.laps.Count < 2) continue;
                sb.Append($"  P{i + 1} #{e.driver.number} interval to P{i} by lap:");
                int n = Mathf.Min(e.laps.Count, a.laps.Count);
                for (int k = 0; k <= n; k += 2) sb.Append($" {Cross(e, k) - Cross(a, k):F2}");
                float me = 0, ma = 0; for (int k = 1; k < n; k++) { me += e.laps[k]; ma += a.laps[k]; }
                sb.AppendLine($"   (mean lap {me / (n - 1):F2} vs {ma / (n - 1):F2}; feature pace {e.driver.paceOffsetSec:+0.00} vs {a.driver.paceOffsetSec:+0.00})");
            }
            // field mean lap time by green lap number (tires coming in / going off; cautions excluded by lap timing)
            {
                sb.Append("  field mean lap by lap#:");
                for (int k = 0; k < 15; k++)
                {
                    float sum = 0; int cnt = 0;
                    foreach (var e in d.field) if (e.laps.Count > k && e.lapClean.Count > k && e.laps[k] < 14f) { sum += e.laps[k]; cnt++; }
                    if (cnt > 0) sb.Append($" {sum / cnt:F2}");
                }
                sb.AppendLine();
            }
            {
                var off = new System.Text.StringBuilder();
                foreach (var e in d.field) if (e.ai.offTrackSteps > 0) off.Append($" #{e.driver.number} {e.ai.offTrackSteps * World.FixedDt:F1}s");
                if (off.Length > 0) sb.AppendLine("  off-track time:" + off);
            }
            sb.Append("  clean-air best laps: ");
            foreach (var e in d.field)
            {
                float b = d.BestCleanLap(e);
                if (!float.IsInfinity(b))
                {
                    st.cleanBest.Add(b); st.minClean = Mathf.Min(st.minClean, b); st.maxClean = Mathf.Max(st.maxClean, b);
                    sb.Append($"#{e.driver.number} {b:F2}  ");
                }
                else sb.Append($"#{e.driver.number} —  ");
            }
            sb.AppendLine();
            if (d.trace != null) sb.Append(d.trace);
            foreach (var ev in d.events) if (ev.Contains("SPIN") || ev.Contains("FLIP") || ev.Contains("STOPPED") || ev.Contains("OUT:") || ev.Contains("CREW") || ev.Contains("WATCHDOG") || ev.Contains("OFF TRACK") || ev.Contains("WALL BREACH") || ev.Contains("CAUTION") || ev.Contains("TO THE TAIL") || ev.Contains("ONE TO GO") || ev.Contains("GREEN") || ev.Contains("GRID AT GREEN") || ev.Contains("before:") || ev.Contains("DIVE") || ev.Contains("dive ")) sb.AppendLine("  " + ev);
        }

        public static string Summary(SoakStats st)
        {
            float mean = 0; foreach (var x in st.cleanBest) mean += x; mean = st.cleanBest.Count > 0 ? mean / st.cleanBest.Count : 0;
            float win = 0; foreach (var x in st.winnerTimes) win += x; win = st.winnerTimes.Count > 0 ? win / st.winnerTimes.Count : 0;
            return $"SOAK SUMMARY: {st.races} races, {st.finished} completed. Cautions {st.cautions} total ({(st.races > 0 ? (float)st.cautions / st.races : 0):F1}/race). " +
                   $"Wall breaches {st.breaches}. Incidents {st.incidents} ({(st.races > 0 ? (float)st.incidents / st.races : 0):F1}/race). DNFs {st.dnfs}. " +
                   $"Clean-air best laps: {st.cleanBest.Count} karts, mean {mean:F2} s, range {(st.cleanBest.Count > 0 ? st.minClean : 0):F2}–{st.maxClean:F2} s (Clone band 10.5–11.5). " +
                   $"Mean winner race time {win:F1} s. Off-track steps {st.offTrackSteps}. " +
                   $"On-track passes {(st.races > 0 ? (float)st.passes / st.races : 0):F1}/race; mean positions moved vs grid {(st.races > 0 ? st.moved / st.races : 0):F1}. " +
                   $"Winners' starting spots: {string.Join(", ", st.winGrid)}; winners' feature-pace rank: {string.Join(", ", st.winPace)}. " +
                   $"Per race: contacts {(float)st.contacts / Mathf.Max(1, st.races):F1} (hard {(float)st.hard / Mathf.Max(1, st.races):F1}), spins contact {(float)st.cSpin / Mathf.Max(1, st.races):F1} / self {(float)st.sSpin / Mathf.Max(1, st.races):F1}, flips contact {(float)st.cFlip / Mathf.Max(1, st.races):F2} / self {(float)st.sFlip / Mathf.Max(1, st.races):F2}.";
        }

        public static string Header(int n, RaceConfig rc)
        {
            return $"OKT M2 soak — {n} races, {rc.fieldSize} karts, {rc.featureLaps}-lap features, {Mathf.RoundToInt(1f / World.FixedDt)} Hz physics, " +
                   $"corner factor {DriverConfig.DefaultCornerSlow:F2}, Unity {Application.unityVersion}\n";
        }
    }
}
