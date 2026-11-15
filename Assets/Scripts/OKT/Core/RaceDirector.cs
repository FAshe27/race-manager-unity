// M2 race director (spec §8): phases pace -> green -> checkered -> done, positions by
// unwrapped centerline distance, lap timing, and the per-step traffic commands each AI
// driver receives (spec §7: occupancy, check-up, committed passes; clean racing per
// Franklin's M2 ruling — contact and incidents come in M3).
//
// Rules carried over from the browser sim (js/sim.js), all Franklin's:
//  - the PACE LAP holds the double-file grid formation; nobody accelerates to racing speed
//    before the green; the green flies when the leader crosses S/F after the pace lap
//  - START PACK: for the first ~4 s of a green, a kart with company close ahead cannot
//    out-accelerate the kart in front of it (the field stays packed to turn one)
//  - passing is a LATCHED move (side chosen once), abandoned after ~3 s, with a cool-down;
//    the offset arcs in/out at a capped rate (no slithering)
//  - OCCUPANCY: a kart may not steer into a slot another kart holds; running alongside needs
//    a full kart width of room
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OKT
{
    public enum RacePhase { Grid, Pace, Green, Checkered, Done }

    [System.Serializable]
    public class RaceConfig
    {
        public int fieldSize = 12;                 // spec §1
        public int featureLaps = 15;               // spec §8 (Franklin: 15 or 20)
        public int paceLaps = 1;                   // Franklin (M2): one pace lap from the grid
        public float paceStraightMph = 25f;        // Franklin: ~20–25 mph [TEST VALUE]
        public float paceCornerMph = 20f;
        public float gridLeaderU = 0.95f;          // front row: front straight, just past the grid entrance (top of T4) [TEST VALUE]
        public float rowGapFt = 6.7f;              // centre to centre: bumpers ~1 in apart (Franklin: no daylight between karts in a line) [TEST VALUE]
        public float laneFrac = 0.16f;             // double-file lanes at ±0.16 half-width ≈ 53 in apart: the two lines nearly touching (Franklin) [TEST VALUE]
        public float packedStartSeconds = 4f;      // browser sim (Franklin) [TEST VALUE]
        // Traffic geometry [TEST VALUEs]
        public float alongsideFt = 7.5f;           // longitudinal window that counts as "alongside" (≈ kart length + margin)
        public float sideRoomIn = 50f;             // centre-to-centre side by side: nerfs are 51 in apart, so side by side means rubbing (Franklin: contact is commonplace) [TEST VALUE]
        public float followGapFt = 1.0f;           // bumper-to-bumper gap a kart settles at when it checks up (Franklin's video: nose to tail, ~a kart length) [TEST VALUE]
        public float lookAheadTime = 0.5f;         // spec §7: slot projected ~0.5 s ahead
        public float passStartGapFt = 11f;         // centre to centre: nose within ~4 ft of his bumper (Franklin: passes tried from way too far back) [TEST VALUE]
        public float passHoldGapFt = 16f;          // ...and stays latched inside this gap (browser 0.055 lap)
        public float passAbandonSec = 3f;          // browser sim
        public float passCooldownSec = 2.5f;       // browser sim
        // The exit-run pass (Franklin, 2026-11-15): a faster kart times a run off the corner, pulls to the inside of the
        // leader on the straight and owns the inside line into the next corner; the leader lifts and files in behind.
        public float runRoomFt = 4f;               // extra gap the follower leaves mid-corner so he can carry speed off it [TEST VALUE]
        public float runAbandonSec = 5f;
        public float bumpDraftAggression = 0.45f;  // drivers above this bumper-draft on the straights even when not faster [TEST VALUE]
        public float bumpLean = 0.3f;              // m/s extra while leaning on his bumper [TEST VALUE]           // straight + entry take longer than a dive [TEST VALUE]
        public float fasterBy = 1.005f;            // needs a pace edge (like-for-like free speed) to start a pass [TEST VALUE]
        // Conceding the inside (Franklin, 23:55): established inside me at corner entry -> drag the brakes, file in behind.
        public float concedeEntryCorner = 0.25f;    // corner value at my speed-lookahead that counts as "entry" [TEST VALUE]
        public float concedeNoseFt = 5.0f;          // his nose may be up to this far behind mine and still be "established" (overlapped) [TEST VALUE]
        public float concedeSpeedDrop = 0.6f;       // m/s below his speed while I drop in behind [TEST VALUE]
        public float concedeHoldSec = 1.2f;         // how long the concession lasts once made [TEST VALUE]
        public float hazardYawDeg = 30f;            // off-line angle that makes a kart a hazard [TEST VALUE]
        public float hazardYawRate = 1.8f;          // rad/s of rotation that makes a kart a hazard [TEST VALUE]
        public float hazardLookSeconds = 2.0f;
        public float crawlMph = 4f;                 // speed a blocked kart creeps through a jam at [TEST VALUE]      // how far ahead (in time) drivers watch for trouble [TEST VALUE]
        public float outsidePassMargin = 0.6f;
        public float insidePassMargin = 0.3f;       // m of spare room above the inside edge to dive [TEST VALUE]
        // The push-up pass (Franklin 22:10 / 23:20): nose on his bumper at corner entry, push him in slightly hot,
        // he drifts up, take the bottom. [TEST VALUEs]
        public float pushSpeedDelta = 0.7f;         // m/s (~1.5 mph) — Franklin: make him enter the corner 1–2 mph faster than he wants
        public float pushMaxSeconds = 3.0f;         // close up + lean; give it up if the bottom doesn't open
        public float pushCooldown = 3f;
        public float pushEntryCorner = 0.15f;       // only into a corner (corner value at the speed-lookahead point)
        public float pushedDriftM = 0.9f;           // how far the pushed kart drifts up the track [TEST VALUE]
        public float attackBoostMax = 0.0f;         // OFF (0.03 caused pileups + a physics blow-up, see log). cornerSlow cut at aggression 1 while pushing/passing (0.30 -> 0.27 = HUD "Fast") [TEST VALUE]
        public float pushedSeconds = 1.2f;          // how long the drift lasts after the push [TEST VALUE]      // m of spare room needed to try the outside [TEST VALUE]
        public float freeMergeRate = 2.5f;          // m/s lateral offset change with nobody alongside [TEST VALUE]
        public float trafficMergeRate = 1.0f;       // m/s when side by side / passing (browser sim's capped arc) [TEST VALUE]
        public float followHeadway = 0.03f;        // s of extra following gap at speed (was 0.3: ~20 ft back at speed, real karts run nose to tail) [TEST VALUE]
        public float followGain = 1.5f;            // check-up: m/s of speed per m of gap error [TEST VALUE]
        public float cleanAirSeconds = 1.0f;       // a lap is clean-air if the kart ahead was always > this far in time [TEST VALUE]
        // Consistency (browser sim stat; Franklin: winners aren't predetermined) [TEST VALUEs]
        public float lapSigmaSecAtZero = 0.25f;     // lap-to-lap pace sigma for consistency 0 (scales with 100 - consistency)
        public float bobblePerSecAtZero = 0.10f;    // chance per second of a bobble for consistency 0
        public float bobbleSeconds = 0.6f, bobbleSpeedFactor = 0.90f;
        // Recovery / clearing (spec §8.3, §8.4 — pulled forward from M3 so one spin doesn't end a race)
        public float recoverMaxSeconds = 12f;       // can't get going in this long -> out (pulls in / stopped)
        public float crewWalkMph = 3f;               // crew pushes a wreck to the infield at walking pace (§8.4)
        // Stub incident detection (M2): spins / flips / stops are logged, not judged (M3).
        public float spinAngleDeg = 75f;
        public float stoppedSeconds = 3f;
        public float dnfStoppedSeconds = 15f;
    }

    public class DriverProfile
    {
        public string name; public int number; public string tier;
        public float paceOffsetSec;   // FEATURE pace: seconds off the base lap (browser sim tiers)
        public float qualiOffsetSec;  // QUALIFYING pace — the grid comes from this (no invert); can differ (setup hit/miss)
        public string qualiNote = ""; // "missed setup in qualifying" / "feature setup off"
        public float paceFactor;      // speed scale
        public float aggression;      // 0..1 (stored for M3; never overrides occupancy in M2)
        public float consistency;     // 0..100 (browser sim tiers: fast 75–95, mid 55–85, new 35–60)
        public float lateralOffsetIn; // personal offset from the groove
        public Color color;
    }

    public static class FieldGen
    {
        static readonly string[] Names = { "Bridger", "Walsh", "McCord", "Hale", "Stephens", "Harlan", "Pruitt", "Dawes",
                                           "Kimbro", "Tate", "Rowland", "Coffey", "Ivey", "Mabry", "Nall", "Sutter" };
        static readonly Color[] Colors = {
            new Color(0.85f,0.12f,0.10f), new Color(0.10f,0.35f,0.85f), new Color(0.95f,0.75f,0.10f), new Color(0.10f,0.65f,0.25f),
            new Color(0.55f,0.20f,0.75f), new Color(0.95f,0.45f,0.10f), new Color(0.90f,0.90f,0.90f), new Color(0.10f,0.75f,0.80f),
            new Color(0.60f,0.40f,0.20f), new Color(0.95f,0.35f,0.65f), new Color(0.30f,0.30f,0.30f), new Color(0.55f,0.85f,0.20f) };

        /// <summary>12-kart field: 2 fast / 9 mid / 1 new (Franklin 20/70/10). Sorted fastest first = grid order (no invert).</summary>
        public static List<DriverProfile> Make(int n, int seed, float baseLapSec)
        {
            var rng = new System.Random(seed);
            float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            int nFast = Mathf.Max(1, Mathf.RoundToInt(n * 0.2f) - (n == 12 ? 1 : 0)); // 12 -> 2
            int nNew = Mathf.Max(1, Mathf.RoundToInt(n * 0.1f));
            var list = new List<DriverProfile>();
            var nums = new List<int>(); for (int i = 1; i < 100; i++) nums.Add(i);
            for (int i = 0; i < n; i++)
            {
                string tier = i < nFast ? "fast" : (i >= n - nNew ? "new" : "mid");
                float off = tier == "fast" ? R(0.00f, 0.09f) : tier == "mid" ? R(0.10f, 0.27f) : R(0.30f, 0.44f);
                int ni = rng.Next(nums.Count); int num = nums[ni]; nums.RemoveAt(ni);
                // Qualifying vs feature (Franklin, 00:50): most qualify where their speed belongs; some missed the
                // setup in qualifying and come through the pack; a few lose the feature setup. [TEST VALUE odds]
                float q = off + (float)(rng.NextDouble() * 2 - 1) * 0.04f; string note = "";
                double roll = rng.NextDouble();
                if (roll < 0.20) { q += R(0.15f, 0.35f); note = "missed setup in qualifying"; }
                else if (roll < 0.30) { off += R(0.10f, 0.25f); note = "feature setup off"; }
                list.Add(new DriverProfile
                {
                    name = Names[(i + seed) % Names.Length], number = num, tier = tier,
                    paceOffsetSec = off, paceFactor = baseLapSec / (baseLapSec + off), qualiOffsetSec = q, qualiNote = note,
                    aggression = R(0.15f, 0.95f), lateralOffsetIn = R(-10f, 10f),
                    consistency = tier == "fast" ? R(75f, 95f) : tier == "mid" ? R(55f, 85f) : R(35f, 60f),
                    color = Colors[i % Colors.Length],
                });
            }
            list.Sort((a, b) => a.qualiOffsetSec.CompareTo(b.qualiOffsetSec)); // grid = qualifying order, no invert
            return list;
        }
    }

    public class RaceDirector
    {
        public readonly RaceConfig cfg;
        public RacePhase phase = RacePhase.Grid;
        public float phaseTime, raceTime, greenTime = -1f;
        public int cautions;            // M2: no caution system yet (stub, always 0)
        public readonly List<string> events = new List<string>();
        public StringBuilder trace; public float traceUntil = 7f; // debug: per-kart state for the first seconds of green
        readonly World w;
        float L;                        // lap length on the centerline (m)

        public class Entry
        {
            public KartAI ai; public KartPhysics kart; public DriverProfile driver; public int gridPos;
            public bool finished; public int finishPos; public float finishTime;
            public bool outOfRace; public string outReason;
            public int lapsDone;                  // green laps completed
            public float lastCross = -1f;         // time of last S/F crossing during green
            public float nextLine;                // next S/F distance to cross (0 = not yet set)
            public readonly List<float> laps = new List<float>();
            public readonly List<bool> lapClean = new List<bool>();
            public bool cleanThisLap = true;
            // passing state
            public int attackTarget = -1; public float attackT, attackCool; public int attackSide; public bool attackRun;
            // incident stub
            public bool inIncident; public int spins, flips; public float stoppedT;
            public bool blocked;   // a kart right in front of me: stopped here means waiting in a jam, not wrecked
            public float bobbleT;
            public float concedeT; public int concedeTo = -1;
            public int breachesSeen;
            public float pushT, pushCool; public int pushTarget = -1;   // the bumper-push pass (Franklin)
            public float pushedT;                                       // I'm being pushed: run in a touch hot, drift up
            public readonly Queue<string> hist = new Queue<string>(); public float histT;
            public bool recovering; public float recoverT;     // spun kart driving itself out (spec §8.3)
            public bool cleared; public bool beingCleared;      // wreck moved off by the crew (spec §8.4 step 3)
        }
        public readonly List<Entry> field = new List<Entry>();
        int nextFinish = 1;
        System.Random rng;
        float Gauss() { double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble(); return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2)); }
        /// <summary>New lap: this lap's pace = the driver's pace x a consistency wobble.</summary>
        void RollLapPace(Entry e)
        {
            float sigmaSec = cfg.lapSigmaSecAtZero * (100f - e.driver.consistency) / 100f;
            float baseLap = 11.03f;
            e.ai.cfg.pace = e.driver.paceFactor * baseLap / (baseLap + Gauss() * sigmaSec);
        }

        public RaceDirector(World world, RaceConfig config, int seed = 0) { w = world; cfg = config ?? new RaceConfig(); L = w.track.centerLength; rng = new System.Random(seed * 7919 + 17); }

        public void Add(KartAI ai, KartPhysics kart, DriverProfile d, int gridPos) => field.Add(new Entry { ai = ai, kart = kart, driver = d, gridPos = gridPos });

        void Event(string s) { events.Add($"[{raceTime:F1}s] {s}"); }
        public string Tag(Entry e) => $"#{e.driver.number} {e.driver.name}";

        /// <summary>Distance (m) of the S/F line that ends the pace lap and starts green lap 1.</summary>
        public float GreenLine => cfg.paceLaps * L;
        /// <summary>The go line (the red line in turn 4, JSON goU): the green flies here, not at the S/F line (Franklin).
        /// Laps are still scored at the S/F line.</summary>
        public float GoLine => GreenLine - (L - w.track.ArcAt(w.track.goU * w.track.N));
        public float CheckeredLine => GreenLine + cfg.featureLaps * L;

        public List<Entry> RunningOrder()
        {
            var o = new List<Entry>(field);
            o.Sort((a, b) =>
            {
                if (a.finished != b.finished) return a.finished ? -1 : 1;
                if (a.finished) return a.finishPos.CompareTo(b.finishPos);
                if (a.outOfRace != b.outOfRace) return a.outOfRace ? 1 : -1;
                return b.ai.raceDist.CompareTo(a.ai.raceDist);
            });
            return o;
        }

        public Entry Leader()
        {
            Entry best = null;
            foreach (var e in field) if (!e.outOfRace && (best == null || e.ai.raceDist > best.ai.raceDist)) best = e;
            return best;
        }

        public void Start() { phase = RacePhase.Pace; phaseTime = 0; Event("Field rolls off the grid — pace lap."); }

        /// <summary>Called every physics step before the AIs drive.</summary>
        public void PreStep(float dt)
        {
            raceTime += dt; phaseTime += dt;
            float paceS = cfg.paceStraightMph * U.Mph, paceC = cfg.paceCornerMph * U.Mph;
            float kartLen = cfg.alongsideFt * U.Ft, room = cfg.sideRoomIn * U.In;
            bool green = phase == RacePhase.Green;
            bool packed = green && raceTime - greenTime < cfg.packedStartSeconds;

            // lane leaders for the pace formation
            foreach (var e in field)
            {
                var ai = e.ai;
                ai.paceStraight = paceS; ai.paceCorner = paceC;
                ai.speedCap = float.PositiveInfinity; ai.attackBoost = 0f; ai.pushedBoost = 0f;
                ai.latMin = float.NegativeInfinity; ai.latMax = float.PositiveInfinity;
                if (e.outOfRace) { ai.paceMode = true; ai.speedCap = 0f; continue; }

                if (phase == RacePhase.Grid) { ai.paceMode = true; ai.speedCap = 0f; continue; }

                if (phase == RacePhase.Pace)
                {
                    // Double-file formation, pace speed, hold the row spacing to the kart ahead in the same lane;
                    // the outside row holds alongside the inside row. Nobody accelerates to racing speed.
                    ai.formation = true; ai.paceMode = true; ai.trafficGoal = 0f;
                    ai.laneFrac = (e.gridPos % 2 == 0 ? -1f : 1f) * cfg.laneFrac;
                    Entry ahead = null; float gap = float.MaxValue;
                    foreach (var o in field)
                    {
                        if (o == e || o.outOfRace || o.gridPos % 2 != e.gridPos % 2) continue;
                        float g = o.ai.raceDist - ai.raceDist; if (g > 0f && g < gap) { gap = g; ahead = o; }
                    }
                    if (ahead != null) ai.speedCap = Mathf.Max(0f, ahead.kart.ForwardSpeed + 1.0f * (gap - cfg.rowGapFt * U.Ft));
                    // Outside row: hold alongside the inside kart of the same row (Franklin's screenshot: the whole
                    // outside row ran a full row back). The outside lane is longer through the turns, so at plain
                    // pace speed it lost ground every corner and could never make it up — let it run faster than
                    // pace to stay level, never slower than its row partner.
                    if (e.gridPos % 2 == 1)
                    {
                        var partner = field.Find(x => x.gridPos == e.gridPos - 1);
                        if (partner != null && !partner.outOfRace)
                        {
                            float err = partner.ai.raceDist - ai.raceDist; // + = I'm behind him
                            float want = Mathf.Max(0f, partner.kart.ForwardSpeed + 1.0f * err);
                            ai.speedCap = ahead != null ? Mathf.Min(ai.speedCap, Mathf.Max(want, ahead.kart.ForwardSpeed)) : want;
                            float catchUp = Mathf.Max(0f, want - ai.kart.ForwardSpeed) + Mathf.Max(0f, err);
                            ai.paceStraight = paceS + catchUp; ai.paceCorner = paceC + catchUp;
                        }
                    }
                    continue;
                }

                // Green / checkered: race the groove with traffic rules. Finished karts cool down at pace.
                ai.formation = false;
                ai.recovering = e.recovering;
                if (phase == RacePhase.Green && !e.finished)
                {
                    e.bobbleT = Mathf.Max(0f, e.bobbleT - dt);
                    float pB = cfg.bobblePerSecAtZero * (100f - e.driver.consistency) / 100f * dt;
                    if (e.bobbleT <= 0f && rng.NextDouble() < pB) e.bobbleT = cfg.bobbleSeconds;
                    if (e.bobbleT > 0f) ai.speedCap = Mathf.Min(ai.speedCap, ai.freeSpeed * cfg.bobbleSpeedFactor);
                }
                ai.paceMode = e.finished;
                Traffic(e, dt, kartLen, room, packed);
            }
        }

        /// <summary>A kart that is out, spinning, flipped or crawling: an obstacle to route around, not a car to race.</summary>
        /// <summary>Spec §7: a kart that's sideways, airborne or flipping ahead is a hazard to route around — flag it early.</summary>
        bool Unsettled(Entry o)
        {
            var k = o.kart; if (k == null || k.Body.isKinematic) return false;
            var f = w.track.Frame(o.ai.s);
            float yaw = Vector3.Angle(new Vector3(f.tan.x, 0, f.tan.y), Vector3.ProjectOnPlane(k.transform.forward, Vector3.up));
            float yawRate = Mathf.Abs(Vector3.Dot(k.Body.angularVelocity, Vector3.up));
            return yaw > cfg.hazardYawDeg || yawRate > cfg.hazardYawRate || k.GroundedCount == 0;
        }
        // A kart that's merely slow in a queue is NOT a hazard — the karts behind just follow it. Counting every slow
        // kart made a jam behind a wreck spread backwards: each stopped kart became a wreck to the ones behind, and
        // the whole field gridlocked (Franklin's video: karts thread through and it clears in seconds).
        bool Hazard(Entry o) => (o.outOfRace && !o.cleared) || o.inIncident || o.recovering || Unsettled(o);

        void Traffic(Entry e, float dt, float kartLen, float room, bool packed)
        {
            var ai = e.ai;
            float myD = ai.d, line = ai.LineOffsetHere, personal = ai.cfg.lateralOffset;
            float goalAbs = line + personal;                 // where this driver wants to be (absolute d)
            float v = Mathf.Max(0.5f, ai.kart.ForwardSpeed);
            float look = Mathf.Max(kartLen + 2f, v * cfg.lookAheadTime + kartLen + 0.3f * v);
            e.attackCool = Mathf.Max(0f, e.attackCool - dt);
            var fr = w.track.Frame(ai.s);
            ai.allowBerm = false;
            float hiLim = ai.HighLimit(fr);
            float loLim = -fr.halfWidth + ai.kart.setup.FootLeft + 2f * U.In;

            // trouble ahead: the nearest hazard within ~1.2 s, in or near my lane (wider window than traffic)
            Entry haz = null; float gapH = float.MaxValue;
            float hazLook = kartLen + v * cfg.hazardLookSeconds;
            foreach (var o in field)
            {
                if (o == e || o.cleared) continue;
                float g = Gap(o, e);
                if (g <= 0f || g > hazLook) continue;
                // in my lane now, OR where my line will be when I get there (the groove swings ~18 ft from
                // entry to apex — a wreck at the apex wasn't "in my lane" at entry and got hit, lap after lap)
                float lineThere = w.track.Frame(o.ai.s).LineOffset + personal;
                bool inPath = Mathf.Abs(o.ai.d - myD) < room * 1.5f || Mathf.Abs(o.ai.d - lineThere) < room * 1.5f;
                if (inPath && Hazard(o) && g < gapH) { gapH = g; haz = o; }
            }

            // nearest kart ahead whose slot overlaps mine (stopped / wrecked karts included: spec §7)
            Entry ahead = null; float gapA = float.MaxValue;
            foreach (var o in field)
            {
                if (o == e || o.cleared) continue;
                float g = Gap(o, e);
                if (g <= 0f || g > look) continue;
                if (Mathf.Abs(o.ai.d - myD) < room && g < gapA) { gapA = g; ahead = o; }
            }

            // clean-air bookkeeping: any running kart ahead closer than cleanAirSeconds?
            foreach (var o in field)
            {
                if (o == e || o.outOfRace) continue;
                float g = o.ai.raceDist - ai.raceDist;
                if (g > 0f && g < cfg.cleanAirSeconds * v) { e.cleanThisLap = false; break; }
            }

            bool attacking = false;
            if (haz != null && (ahead == null || gapH < gapA)) { ahead = haz; gapA = gapH; }
            e.blocked = ahead != null && gapA < kartLen * 1.8f;

            // ---- a latched pass tracks ITS target until done — diving inside leaves his lane, so "the kart ahead in
            // my lane" stopped being him and the move was cancelled the moment it started.
            if (e.attackTarget >= 0 && (haz == null || field.IndexOf(haz) == e.attackTarget))
            {
                var tg = field[e.attackTarget];
                float gt = Gap(tg, e);
                e.attackT += dt;
                if (tg.outOfRace || tg.finished || Hazard(tg)) { e.attackTarget = -1; }
                else if (gt < -kartLen * 0.6f) { if (e.attackRun) dbgRunDone++; e.attackTarget = -1; e.attackCool = 0.9f; dbgCompleted++; }     // cleared him: pass done
                else if (gt > cfg.passHoldGapFt * U.Ft + kartLen || (e.attackT > AbandonSec(e) && gt > kartLen * 0.3f))
                { e.attackTarget = -1; e.attackCool = cfg.passCooldownSec; }                                         // didn't come off: fall back in line
                else if (e.attackSide > 0 && GrooveClimbing(ai)) { e.attackTarget = -1; e.attackCool = cfg.passCooldownSec; }
                else
                {
                    attacking = true;
                    goalAbs = tg.ai.d + e.attackSide * room;
                    if (ahead == tg) { ahead = null; gapA = float.MaxValue; } // he's beside me, not in front of me
                }
            }
            if (ahead != null && Hazard(ahead))
            {
                dbgHaz++;
                // ---- hazard ahead: route around on whichever side has room; no room -> slow and stop short
                float od = ahead.ai.d;
                ai.allowBerm = true; hiLim = ai.HighLimit(fr); // dodging a wreck may use the berm
                bool upOk = od + room <= hiLim, downOk = od - room >= loLim;
                if (upOk || downOk)
                {
                    bool goUp = upOk && (!downOk || myD >= od);
                    goalAbs = goUp ? od + room : od - room;
                    attacking = true; // reuse the eased offset path
                }
                // brake for it too: never faster than what stops short of it (or 60% of free speed while dodging)
                float stopV = Mathf.Sqrt(Mathf.Max(0f, 2f * ai.kart.setup.brakeCap * 0.8f * Mathf.Max(0f, gapA - kartLen)));
                // No room either side: don't park — creep up at walking pace and nose through (contact at a walking
                // pace is a nudge, and it's how real karts get out of a jam).
                // (a kart that's itself recovering noses closer — two recovering karts braking for each other deadlocked)
                float crawl = gapA > kartLen * (e.recovering ? 0.6f : 0.9f) ? cfg.crawlMph * U.Mph : 0f;
                ai.speedCap = Mathf.Min(ai.speedCap, (upOk || downOk) ? Mathf.Max(stopV, ai.freeSpeed * 0.6f) : Mathf.Max(stopV, crawl));
                ai.trafficRate = cfg.freeMergeRate; // swerve briskly
            }
            else if (!attacking && !packed && !e.finished && ahead != null && e.attackCool <= 0f && e.attackTarget < 0)
            {
                dbgAhead++;
                // ---- passing (latched side, abandon, cooldown) — browser sim rules, like-for-like pace
                // Faster on pace, OR being held up behind him (browser sim: close behind and overlapped counts).
                // Start distance is relative to where a follower settles (the follow gap includes a time headway),
                // otherwise a faster kart sat in its following slot and never got close enough to try.
                float startGap = Mathf.Max(cfg.passStartGapFt * U.Ft, SettleGap(e, v, kartLen, true) + 0.3f);
                bool heldUp = gapA < startGap && ahead.kart.ForwardSpeed < ai.freeSpeed * 0.985f;
                bool faster = ai.freeSpeed > ahead.ai.freeSpeed * cfg.fasterBy || heldUp;
                bool latched = e.attackTarget == field.IndexOf(ahead);
                bool want = latched ? (gapA < startGap + 3f && (faster || e.attackT < 1.2f))
                                    : (gapA < startGap && faster);
                // exit run: off the corner, faster on pace, carrying more speed than him, within a run's distance
                // (pace compared like for like: free speed is read at each kart's own look-ahead point, and off a corner
                // the follower's point is still further back in the turn, so he always looked slower than the leader)
                bool run = !latched && ExitRun(ai) && ai.cfg.pace > ahead.ai.cfg.pace * cfg.fasterBy
                           && gapA < SettleGap(e, v, kartLen, true) + cfg.runRoomFt * U.Ft + 1f
                           && v > ahead.kart.ForwardSpeed + 0.2f;
                if (run) { want = true; dbgRuns++; }
                if (gapA < startGap) dbgClose++; if (faster) dbgFaster++; if (heldUp) dbgHeld++; if (want) dbgWant++;
                if (want)
                {
                    if (!latched)
                    {
                        // dive under a kart running high, go around one running low — if the room exists
                        // Inside is the normal pass; around the outside only with real room to spare (the groove
                        // climbs to the wall at the exits and an outside kart got squeezed onto the berm and rail).
                        float od = ahead.ai.d;
                        bool insideOk = od - room >= loLim + cfg.insidePassMargin;
                        bool outsideOk = od + room <= hiLim - cfg.outsidePassMargin && !GrooveClimbing(ai);
                        int side = insideOk ? -1 : (outsideOk && !run ? 1 : 0); // a run goes to the inside only
                        if (insideOk) dbgInsideOk++; if (outsideOk) dbgOutsideOk++; if (side != 0) dbgStarted++;
                        if (side != 0) { e.attackTarget = field.IndexOf(ahead); e.attackT = 0f; e.attackSide = side; e.attackRun = run; if (run) dbgRunStarted++; }

                    }
                    if (e.attackTarget == field.IndexOf(ahead) && e.attackSide > 0 && GrooveClimbing(ai))
                    { e.attackTarget = -1; e.attackCool = cfg.passCooldownSec; } // call off an outside move as the exit opens to the wall
                    if (e.attackTarget == field.IndexOf(ahead))
                    {
                        e.attackT += dt; attacking = true;
                        goalAbs = ahead.ai.d + e.attackSide * room;
                        if (e.attackT > AbandonSec(e)) { e.attackCool = cfg.passCooldownSec; e.attackTarget = -1; attacking = false; goalAbs = line + personal; }
                    }
                }
            }
            if (!attacking && e.attackTarget >= 0 && (ahead == null || e.attackTarget != field.IndexOf(ahead)))
            { e.attackTarget = -1; e.attackT = 0f; e.attackCool = Mathf.Max(e.attackCool, 0.9f); }

            // ---- occupancy: never steer into a slot a kart alongside holds; keep my side of it.
            // Applied twice: to the traffic goal, and as hard lane limits on the steering target
            // (the target sits down the road where the groove may already have moved — e.g. diving
            // to the apex — so a limit at my current position alone let karts steer into each other).
            float latMin = float.NegativeInfinity, latMax = float.PositiveInfinity;
            foreach (var o in field)
            {
                if (o == e || o.cleared) continue;
                float g = Gap(o, e);
                if (Mathf.Abs(g) > kartLen) continue;
                float od = o.ai.d;
                if (myD >= od) latMin = Mathf.Max(latMin, od + room); else latMax = Mathf.Min(latMax, od - room);
                if (Mathf.Abs(goalAbs - od) < room)
                    goalAbs = myD >= od ? Mathf.Max(goalAbs, od + room) : Mathf.Min(goalAbs, od - room);
            }
            // Squeezed against the berm/wall by a kart below me: don't get pushed onto it — lift and drop in behind.
            if (latMin > hiLim)
            {
                latMin = hiLim;
                Entry squeezer = null; float sg = float.MaxValue;
                foreach (var o in field)
                {
                    if (o == e || o.cleared) continue;
                    float g = Gap(o, e);
                    if (Mathf.Abs(g) <= kartLen && o.ai.d < myD && Mathf.Abs(g) < sg) { sg = Mathf.Abs(g); squeezer = o; }
                }
                if (squeezer != null) { e.concedeT = cfg.concedeHoldSec; e.concedeTo = field.IndexOf(squeezer); }
                if (e.attackTarget >= 0) { e.attackTarget = -1; e.attackCool = cfg.passCooldownSec; goalAbs = Mathf.Min(goalAbs, hiLim); }
            }
            // If I'm already inside a limit (side by side and tight), don't yank me across: hold where I am.
            if (myD < latMin) latMin = myD;
            if (myD > latMax) latMax = myD;
            ai.latMin = latMin; ai.latMax = latMax;
            // Merge rate: brisk when nobody is alongside and no move is on (the post-green lane-to-groove merge
            // finishes on the straight — a slow merge unwound mid-corner and swung the pole onto the berm at
            // the turn-2 exit, lap 1, in most races); gentle when side by side or passing.
            bool free = float.IsNegativeInfinity(latMin) && float.IsPositiveInfinity(latMax) && !attacking;
            ai.trafficRate = free ? cfg.freeMergeRate : cfg.trafficMergeRate;
            goalAbs = Mathf.Clamp(goalAbs, loLim, hiLim);
            // being pushed (Franklin): a kart's nose on my bumper at entry -> I run in slightly hot and drift up the track
            e.pushedT = Mathf.Max(0f, e.pushedT - dt);
            foreach (var o in field)
            {
                if (o == e || o.pushTarget != field.IndexOf(e)) continue;
                float g = Gap(e, o); // how far I am ahead of the pusher
                var sp = e.kart.setup; // only once his nose is actually on my bumper (within ~6 in)
                if (g > 0f && g < sp.FrontBumperZ - sp.RearBumperZ + 2f * sp.TubeRadius + 0.15f) e.pushedT = cfg.pushedSeconds;
            }
            // Franklin: "juuuust up against their bumper, where they don't even feel you there... make them enter the
            // corner 1–2 mph faster than they want to. They won't know they were pushed until they slide up just
            // enough for you to get underneath them mid-corner." He doesn't brake against it (carries the extra
            // speed in), and he slides up once he's into the corner.
            if (e.pushedT > 0f) ai.pushedBoost = cfg.pushSpeedDelta;
            if (e.pushedT > 0f && !attacking && fr.corner > 0.35f) goalAbs = Mathf.Min(goalAbs + cfg.pushedDriftM * (e.pushedT / cfg.pushedSeconds), hiLim);
            ai.trafficGoal = goalAbs - line - personal;
            ai.diving = attacking && e.attackTarget >= 0 && e.attackSide < 0;

            // ---- conceding the inside (Franklin): a kart established on my inside at corner entry ->
            // drag the brakes slightly and file in behind him. Karts nose-to-tail behind him get the
            // same treatment as they arrive (the freight train).
            e.concedeT = Mathf.Max(0f, e.concedeT - dt);
            if (!packed && !e.finished)
            {
                float entryCorner = w.track.Frame(w.track.LineS(w.track.LineAdvance(w.track.NearestLineIndex(new Vector2(ai.transform.position.x, ai.transform.position.z), -1), v * ai.cfg.speedLookaheadTime))).corner;
                foreach (var o in field)
                {
                    if (o == e || o.outOfRace || Hazard(o)) continue;
                    float g = Gap(o, e);
                    bool alongsideInside = o.ai.d < myD - 0.5f && g > -cfg.concedeNoseFt * U.Ft && g < kartLen;
                    if (alongsideInside && entryCorner > cfg.concedeEntryCorner && e.attackTarget != field.IndexOf(o))
                    { e.concedeT = cfg.concedeHoldSec; e.concedeTo = field.IndexOf(o); break; }
                }
                if (e.concedeT > 0f && e.concedeTo >= 0)
                {
                    var o = field[e.concedeTo];
                    if (!o.outOfRace && Gap(o, e) > -kartLen)
                        ai.speedCap = Mathf.Min(ai.speedCap, Mathf.Max(0f, o.kart.ForwardSpeed - cfg.concedeSpeedDrop));
                    else e.concedeT = 0f;
                }
            }

            // ---- the push: nose on his bumper, a touch faster than him; when he drifts up and the bottom opens, dive
            e.pushCool = Mathf.Max(0f, e.pushCool - dt);
            if (!packed && !e.finished && ahead != null && !Hazard(ahead) && e.pushTarget < 0 && e.attackTarget < 0 && e.pushCool <= 0f
                && e.driver.paceFactor > ahead.driver.paceFactor * 1.001f
                && gapA < SettleGap(e, v, kartLen, false) + 3f && EntryAhead(ai)
                && rng.NextDouble() < e.driver.aggression * dt * 3.0)
            { e.pushTarget = field.IndexOf(ahead); e.pushT = 0f; dbgPushes++;
              if (traceAtk < 0 && diveTraces < 0) { traceAtk = field.IndexOf(e); traceTgt = e.pushTarget; traceT0 = raceTime; traceAcc = 0f; diveTraces++; events.Add($"DIVE TRACE {diveTraces} (push): #{e.driver.number} on #{ahead.driver.number}"); } }
            bool pushing = false;
            if (e.pushTarget >= 0)
            {
                var pt = field[e.pushTarget];
                e.pushT += dt;
                float gp = Gap(pt, e);
                bool lost = pt.outOfRace || pt.finished || gp > SettleGap(e, v, kartLen, false) + 6f || gp < 0f || Hazard(pt);
                if (lost || e.pushT > cfg.pushMaxSeconds) { e.pushTarget = -1; e.pushCool = cfg.pushCooldown; }
                else
                {
                    pushing = true;
                    float od = pt.ai.d;
                    if (gp < kartLen + 0.8f && od - room >= loLim + cfg.insidePassMargin && e.attackTarget < 0) // nose on his bumper and he's drifted up: the bottom is open
                    {
                        e.attackTarget = e.pushTarget; e.attackT = 0f; e.attackSide = -1; e.pushTarget = -1; dbgPushPasses++;
                        if (traceAtk < 0 && diveTraces < 0) { traceAtk = field.IndexOf(e); traceTgt = e.attackTarget; traceT0 = raceTime; traceAcc = 0f; diveTraces++; events.Add($"DIVE TRACE {diveTraces}: #{e.driver.number} under #{field[traceTgt].driver.number}"); }
                    }
                    // close up briskly in HIS tracks (a wider lane loses ground in the corner and never closes),
                    // then lean on him: a touch faster once the noses meet
                    else
                    {
                        // ...but only line up behind him on the straight / at entry, gently: swinging across to his
                        // tracks mid-corner at 35 mph spun the pusher on its own and started pileups (soak, 2026-11-15)
                        if (fr.corner < 0.4f)
                        {
                            float follow = Mathf.Clamp(pt.ai.d, Mathf.Max(loLim, ai.latMin), Mathf.Min(hiLim, ai.latMax));
                            ai.trafficGoal = follow - line - personal; ai.trafficRate = cfg.trafficMergeRate;
                        }
                        { var stp = ai.kart.setup; float bl = stp.FrontBumperZ - stp.RearBumperZ + 2f * stp.TubeRadius;
                          ai.speedCap = Mathf.Min(ai.speedCap, pt.kart.ForwardSpeed + Mathf.Max(cfg.pushSpeedDelta, 0.8f * (gp - bl))); }
                    }
                }
            }

            // attacking drivers drive closer to the limit (not when dodging a wreck)
            if ((pushing || (attacking && e.attackTarget >= 0)) && haz == null) ai.attackBoost = cfg.attackBoostMax * e.driver.aggression;
            // ---- check-up: hold station behind a kart in my slot until the lane is clear (time headway too)
            if (ahead != null && !(pushing && field.IndexOf(ahead) == e.pushTarget))
            {
                float va = ahead.kart.ForwardSpeed;
                // Packed start: hold the grid spacing (nose to tail). Otherwise: follow gap + time headway —
                // shortened to striking distance when I'm faster on pace and want by.
                bool wantBy = ai.cfg.pace > ahead.ai.cfg.pace * cfg.fasterBy && !Hazard(ahead);
                float settle = packed ? cfg.rowGapFt * U.Ft : SettleGap(e, v, kartLen, wantBy);
                if (wantBy && !packed && fr.corner > 0.5f) settle += cfg.runRoomFt * U.Ft; // time the run: a little room mid-corner
                // Bumper drafting (Franklin, 2026-11-15): "literally up against the bumper of other karts, pushing them
                // down the straight", then lift slightly on entry so as not to spin him into the corner. On the
                // straight a racer (faster, or aggressive enough) closes to bumper contact and leans on him; at
                // entry the normal settle gap comes back — that's the lift.
                var st = ai.kart.setup;
                float bumperLen = st.FrontBumperZ - st.RearBumperZ + 2f * st.TubeRadius; // centre to centre at bumper contact
                bool straightNow = fr.corner < 0.15f && !EntryAhead(ai);
                bool bumpDraft = !packed && !e.finished && straightNow && !Hazard(ahead)
                                 && (wantBy || e.driver.aggression > cfg.bumpDraftAggression);
                if (bumpDraft) { settle = bumperLen + 0.02f; dbgBumpDraft++; }
                float cap = va + cfg.followGain * (gapA - settle) + (bumpDraft ? cfg.bumpLean : 0f);
                // Never demand far below the kart ahead's speed unless contact is imminent — a check-up eases
                // back to his pace; it doesn't collapse (a hard cap cascaded the whole field to a stop).
                // "Imminent" = within a few inches of his bumper (was 1.6 ft of air, which kept everyone off each other).
                float touch = bumperLen + (bumpDraft ? -0.05f : 0.1f);
                cap = gapA > touch ? Mathf.Max(cap, va * 0.85f) : Mathf.Min(cap, va * 0.7f);
                ai.speedCap = Mathf.Min(ai.speedCap, Mathf.Max(0f, cap));
            }
            // ---- start pack: nobody out-accelerates the kart in front off the green
            if (packed)
            {
                Entry front = null; float gf = float.MaxValue;
                foreach (var o in field)
                {
                    if (o == e || o.outOfRace) continue;
                    float g = o.ai.raceDist - ai.raceDist;
                    if (g > 0f && g < 6f && g < gf) { gf = g; front = o; }
                }
                if (front != null) ai.speedCap = Mathf.Min(ai.speedCap, front.kart.ForwardSpeed + 0.15f);
            }
        }

        float AbandonSec(Entry e) => e.attackRun ? cfg.runAbandonSec : cfg.passAbandonSec;

        /// <summary>Coming off a corner: it was a corner just behind me and it's opening up here.</summary>
        bool ExitRun(KartAI ai)
        {
            float cb = w.track.Frame(w.track.WrapS(ai.s - 8f / w.track.centerLength * w.track.N)).corner;
            float c0 = w.track.Frame(ai.s).corner;
            return cb > 0.5f && c0 < cb && c0 < 0.85f;
        }

        /// <summary>Is a corner coming up (entry), at the speed-lookahead point?</summary>
        bool EntryAhead(KartAI ai)
        {
            float v = Mathf.Max(5f, ai.kart.ForwardSpeed);
            float c0 = w.track.Frame(ai.s).corner;
            float c1 = w.track.Frame(w.track.WrapS(ai.s + v * 1.5f / w.track.centerLength * w.track.N)).corner;
            return c1 > cfg.pushEntryCorner && c0 < 0.6f && c1 > c0;
        }

        /// <summary>Is the groove climbing toward the wall ahead of this kart (a corner exit)?</summary>
        bool GrooveClimbing(KartAI ai)
        {
            float v = Mathf.Max(5f, ai.kart.ForwardSpeed);
            float sAhead = w.track.WrapS(ai.s + v * 1.0f / w.track.centerLength * w.track.N);
            return w.track.Frame(sAhead).LineOffset > w.track.Frame(ai.s).LineOffset + 0.4f;
        }

        float SettleGap(Entry e, float v, float kartLen, bool wantBy)
            => kartLen + cfg.followGapFt * U.Ft + (wantBy ? 0f : cfg.followHeadway * v);

        /// <summary>Signed track distance from e forward to o (m), nearest way round (handles lapped karts).</summary>
        float Gap(Entry o, Entry e)
        {
            float g = o.ai.raceDist - e.ai.raceDist;
            g = Mathf.Repeat(g + L * 0.5f, L) - L * 0.5f;
            return g;
        }

        /// <summary>Called every physics step after the physics moved: phases, laps, finishes, incident stubs.</summary>
        public void PostStep(float dt)
        {
            var lead = Leader();
            if (phase == RacePhase.Pace && lead != null && lead.ai.raceDist >= GoLine)
            {
                phase = RacePhase.Green; phaseTime = 0; greenTime = raceTime;
                {
                    var sb = new System.Text.StringBuilder("GRID AT GREEN — outside kart vs its inside partner (ft, + = behind):");
                    foreach (var o in field) if (o.gridPos % 2 == 1) { var p = field.Find(x => x.gridPos == o.gridPos - 1); if (p != null) sb.Append($" row{o.gridPos / 2 + 1} {(p.ai.raceDist - o.ai.raceDist) / U.Ft:+0.0;-0.0}"); }
                    Event(sb.ToString());
                }
                Event($"GREEN FLAG — {Tag(lead)} leads them to the line.");
                foreach (var e in field) { e.lastCross = -1f; e.ai.SnapTrafficOffsetToCurrent(); }
            }

            foreach (var e in field)
            {
                var ai = e.ai;
                if (e.outOfRace && !e.finished) CrewClear(e, dt);
                // ---- wall breach forensics (CLAUDE.md: stop-and-report) — every kart, out or not
                if (ai.bandBreaches > e.breachesSeen)
                {
                    e.breachesSeen = ai.bandBreaches;
                    var fb = w.track.Frame(ai.s);
                    float surf = w.track.SurfaceHeightSD(ai.s, fb.halfWidth);
                    var pos = e.kart.transform.position; var vel = e.kart.Body.linearVelocity;
                    Event($"WALL BREACH: {Tag(e)}{(e.outOfRace ? " (already out: " + e.outReason + ")" : "")} u={ai.s / w.track.N:F3} d={ai.d:F2} m (edge {fb.halfWidth:F2}) barrier={w.track.BarrierAt(ai.s / w.track.N)} " +
                          $"height above rail base {pos.y - surf:F2} m (rail {w.track.cfg.guardrailHeight:F2} m) wheels grounded {e.kart.GroundedCount} upside-down {e.kart.UpsideDown} " +
                          $"speed {vel.magnitude / U.Mph:F1} mph vertical {vel.y:F1} m/s incident {e.inIncident}");
                }
                if (e.outOfRace) continue;
                // S/F crossings during green: lap timing and counting (lines at GreenLine + n·L)
                if (phase == RacePhase.Green || phase == RacePhase.Checkered)
                {
                    if (e.nextLine <= 0f) e.nextLine = GreenLine;
                    while (ai.raceDist >= e.nextLine)
                    {
                        if (e.lastCross >= 0f) { e.laps.Add(raceTime - e.lastCross); e.lapClean.Add(e.cleanThisLap); e.lapsDone++; }
                        e.lastCross = raceTime; e.cleanThisLap = true; e.nextLine += L;
                        RollLapPace(e);
                        if (phase == RacePhase.Checkered && !e.finished) Finish(e);
                        else if (phase == RacePhase.Green && e.lapsDone >= cfg.featureLaps && !e.finished)
                        {
                            phase = RacePhase.Checkered; phaseTime = 0;
                            Event($"CHECKERED FLAG — {Tag(e)} wins.");
                            Finish(e);
                        }
                    }
                }

                // ---- spin forensics: what was this kart doing in the last ~1.5 s?
                if (phase == RacePhase.Green && !e.recovering && !e.inIncident)
                {
                    e.histT += dt;
                    if (e.histT >= 0.1f)
                    {
                        e.histT = 0f;
                        var fh = w.track.Frame(ai.s); var k = e.kart;
                        e.hist.Enqueue($"u{ai.s / w.track.N:F2} v{k.ForwardSpeed / U.Mph:F0} tgt{ai.targetSpeed / U.Mph:F0} acc{k.accelCmd:F1} st{k.steerDeg:F0} dOff{ai.d - fh.LineOffset:F1} tOff{ai.TrafficOffset:F1} lat[{(float.IsInfinity(ai.latMin) ? "-" : ai.latMin.ToString("F1"))},{(float.IsInfinity(ai.latMax) ? "-" : ai.latMax.ToString("F1"))}] {(e.attackTarget >= 0 ? "ATK " : "")}{(e.concedeT > 0 ? "CONC " : "")}{(e.bobbleT > 0 ? "BOB " : "")}slide{(k.sliding[2] ? 1 : 0)}{(k.sliding[3] ? 1 : 0)} dust{k.dust[3]:F1} grip{k.surfaceGripNow[3]:F2} " +
                            $"roll{Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(k.transform.up, k.transform.forward), k.transform.forward):F0} rollRate{Vector3.Dot(k.Body.angularVelocity, k.transform.forward):F1} yawRate{Vector3.Dot(k.Body.angularVelocity, Vector3.up):F1} gnd{(k.grounded[0] ? 1 : 0)}{(k.grounded[1] ? 1 : 0)}{(k.grounded[2] ? 1 : 0)}{(k.grounded[3] ? 1 : 0)} load{k.load[0]:F0}/{k.load[1]:F0}/{k.load[2]:F0}/{k.load[3]:F0} slipAng{Vector3.SignedAngle(k.transform.forward, Vector3.ProjectOnPlane(k.Body.linearVelocity, k.transform.up), k.transform.up):F0}");
                        while (e.hist.Count > 12) e.hist.Dequeue();
                    }
                }

                // ---- incident stub (logged, not judged — M3)
                var f = w.track.Frame(ai.s);
                float yaw = Vector3.Angle(new Vector3(f.tan.x, 0, f.tan.y), Vector3.ProjectOnPlane(e.kart.transform.forward, Vector3.up));
                bool spun = yaw > cfg.spinAngleDeg, flipped = e.kart.UpsideDown;
                bool stopped = phase == RacePhase.Green && e.kart.ForwardSpeed < 2f * U.Mph && !e.finished;
                e.stoppedT = stopped ? e.stoppedT + dt : 0f;
                bool stuck = e.stoppedT > cfg.stoppedSeconds && !e.blocked; // stopped behind a kart = waiting in a jam
                bool incident = spun || flipped || stuck;
                bool wasRecovering = e.recovering;
                bool newEpisode = incident && !e.inIncident && !e.recovering;
                // Spin recovery: a spun or stuck kart (not flipped) drives itself out; it's done when it's
                // pointed the right way and rolling. Can't get going in recoverMaxSeconds -> out.
                bool startRecover = !flipped && (spun || stuck) && phase == RacePhase.Green && !e.finished && !e.recovering;
                if (startRecover) { e.recovering = true; e.recoverT = 0f; }
                if (e.recovering)
                {
                    e.recoverT += dt;
                    if (!spun && e.kart.ForwardSpeed > 3f * U.Mph) { e.recovering = false; Event($"RECOVERED: {Tag(e)} rejoins."); }
                    else if (e.recoverT > cfg.recoverMaxSeconds) { e.recovering = false; e.outOfRace = true; e.outReason = "couldn't restart"; Event($"OUT: {Tag(e)} (couldn't get going)."); }
                }
                if (newEpisode || (flipped && !e.inIncident))
                {
                    if (flipped) e.flips++; else e.spins++;
                    var kc = e.kart.GetComponent<KartContact>();
                    bool contact = kc != null && w.simTime - kc.lastContactTime < 1.5f;
                    if (contact) { if (flipped) contactFlips++; else contactSpins++; } else { if (flipped) selfFlips++; else selfSpins++; }
                    string who = contact && kc.lastOther != null ? $" after contact with #{field.Find(x => x.kart == kc.lastOther)?.driver.number} ({kc.lastImpulse:F0} N·s, {w.simTime - kc.lastContactTime:F1} s before)" : " (no kart contact — self)";
                    Event($"{(flipped ? "FLIP" : spun ? "SPIN" : "STOPPED")}: {Tag(e)} at lap {e.lapsDone + 1}, u={ai.s / w.track.N:F2}, {e.kart.ForwardSpeed / U.Mph:F0} mph{who}{(wasRecovering ? " [while recovering]" : "")}.");
                    if ((!contact || flipped) && (!wasRecovering || flipped)) foreach (var h in e.hist) events.Add("      before: " + h);
                }
                e.inIncident = incident;
                if (!e.finished && !e.recovering && (flipped || (e.stoppedT > cfg.dnfStoppedSeconds && !e.blocked)))
                {
                    e.outOfRace = true; e.outReason = flipped ? "flipped" : "stopped";
                    Event($"OUT: {Tag(e)} ({e.outReason}).");
                }
            }

            CountPasses();
            if (traceAtk >= 0)
            {
                traceAcc += dt;
                if (traceAcc >= 0.1f)
                {
                    traceAcc = 0f;
                    var A = field[traceAtk]; var T = field[traceTgt];
                    events.Add($"   dive t+{raceTime - traceT0:F1}: gap(T ahead of A) {Gap(T, A):F2} m  A d{A.ai.d:F2} v{A.kart.ForwardSpeed / U.Mph:F1} tgt{A.ai.targetSpeed / U.Mph:F1} free{A.ai.freeSpeed / U.Mph:F1} cap{(float.IsInfinity(A.ai.speedCap) ? -1 : A.ai.speedCap / U.Mph):F1} atk{A.attackTarget} push{A.pushTarget} lat[{A.ai.latMin:F1},{A.ai.latMax:F1}] | T d{T.ai.d:F2} v{T.kart.ForwardSpeed / U.Mph:F1} cap{(float.IsInfinity(T.ai.speedCap) ? -1 : T.ai.speedCap / U.Mph):F1} conc{T.concedeT:F1} u{T.ai.s / w.track.N:F3} corner{w.track.Frame(T.ai.s).corner:F2}");
                    if (raceTime - traceT0 > 6f || (A.attackTarget < 0 && A.pushTarget < 0)) { events.Add($"   dive end: attackTarget {A.attackTarget}, completed so far {dbgCompleted}"); traceAtk = -1; }
                }
            }
            if (trace != null && greenTime >= 0 && raceTime - greenTime < traceUntil && Mathf.Repeat(raceTime - greenTime, 0.2f) < dt)
            {
                var p0 = field.Find(x => x.gridPos == 0); var k = p0.kart; var a0 = p0.ai;
                var f0 = w.track.Frame(a0.s);
                float yaw = Vector3.SignedAngle(new Vector3(f0.tan.x, 0, f0.tan.y), Vector3.ProjectOnPlane(k.transform.forward, Vector3.up), Vector3.up);
                trace.AppendLine($"t+{raceTime - greenTime:F2} POLE u{a0.s / w.track.N:F3} d{a0.d:F2} line{f0.LineOffset:F2} hw{f0.halfWidth:F2} off{a0.TrafficOffset:F2} goal{a0.trafficGoal:F2} lat[{a0.latMin:F1},{a0.latMax:F1}] v{k.ForwardSpeed / U.Mph:F1} tgt{a0.targetSpeed / U.Mph:F1} free{a0.freeSpeed / U.Mph:F1} cap{(float.IsInfinity(a0.speedCap) ? -1 : a0.speedCap / U.Mph):F0} steer{k.steerDeg:F1} yaw{yaw:F0} slide{k.sliding[2]}/{k.sliding[3]} grip{k.surfaceGripNow[0]:F2}/{k.surfaceGripNow[1]:F2}/{k.surfaceGripNow[2]:F2}/{k.surfaceGripNow[3]:F2} dust{k.dust[3]:F2} gnd{k.GroundedCount} att{(p0.attackTarget >= 0 ? "Y" : "n")} bob{p0.bobbleT:F1}");
            }
            if (phase == RacePhase.Checkered)
            {
                bool allDone = true;
                foreach (var e in field) if (!e.finished && !e.outOfRace) allDone = false;
                if (allDone || phaseTime > 30f) { phase = RacePhase.Done; Event("Race complete."); }
            }
        }

        /// <summary>
        /// Crew clearing (spec §8.4 step 3 — the one sanctioned scripted move, at walking pace): a wrecked or
        /// dead kart is moved off the racing surface to the infield. M2 has no caution yet (M3), so this
        /// runs under green; the kart becomes kinematic while the crew moves it.
        /// </summary>
        void CrewClear(Entry e, float dt)
        {
            if (e.cleared) return;
            var rb = e.kart.Body;
            // A wreck that ended up outside the barrier is already off the racing surface: leave it there
            // (dragging it back through the rail was the 91 s "breach" artifact).
            if (!e.beingCleared && e.ai.d > w.track.Frame(e.ai.s).halfWidth) { e.cleared = true; rb.isKinematic = true; Event($"CREW: {Tag(e)} is outside the barrier — left there."); return; }
            if (!e.beingCleared)
            {
                e.beingCleared = true; rb.isKinematic = true;
                // A kinematic body can't be pushed back: walked into a kart that's sitting on the infield, it launched
                // that kart over the turn-4 rail at a solver-made 362 mph (soak 2026-11-15, STOP-AND-REPORT). The crew
                // walks it around other karts in reality — here it simply stops touching other karts while it's moved.
                foreach (var c in e.kart.GetComponentsInChildren<Collider>())
                    foreach (var o in field)
                        if (o != e && o.kart != null)
                            foreach (var oc in o.kart.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, oc);
                Event($"CREW: clearing {Tag(e)} to the infield.");
            }
            var ai = e.ai; var f = w.track.Frame(ai.s);
            float targetD = -f.halfWidth - 3.0f;
            float step = cfg.crewWalkMph * U.Mph * dt;
            float nd = Mathf.Max(targetD, ai.d - step);
            var p = f.pos + f.outN * nd;
            var pos = rb.position;
            float ground = nd < -f.halfWidth ? 0f : w.track.SurfaceHeightSD(ai.s, nd);
            rb.MovePosition(new Vector3(p.x, Mathf.Max(ground + 0.2f, pos.y - 0.5f * dt), p.y));
            ai.d = nd;
            if (nd <= targetD + 0.01f) { e.cleared = true; }
        }

        void Finish(Entry e)
        {
            e.finished = true; e.finishPos = nextFinish++; e.finishTime = raceTime;
        }

        // ---- on-track passes: a running kart moving from behind to ahead of another running kart (green, after the pack)
        public int passes; public readonly Dictionary<long, bool> ahead0 = new Dictionary<long, bool>();
        void CountPasses()
        {
            if (phase != RacePhase.Green || raceTime - greenTime < cfg.packedStartSeconds) { ahead0.Clear(); return; }
            for (int i = 0; i < field.Count; i++)
                for (int j = i + 1; j < field.Count; j++)
                {
                    var a = field[i]; var b = field[j];
                    if (a.outOfRace || b.outOfRace || a.finished || b.finished) continue;
                    float g = a.ai.raceDist - b.ai.raceDist;
                    if (Mathf.Abs(g) > L * 0.5f) continue; // lapping traffic isn't a race for position here
                    long key = i * 64 + j; bool aAhead = g > 0f;
                    if (ahead0.TryGetValue(key, out bool was) && was != aAhead) passes++;
                    ahead0[key] = aAhead;
                }
        }
        public float MeanPositionsMoved
        {
            get
            {
                var o = RunningOrder(); float sum = 0;
                for (int i = 0; i < o.Count; i++) sum += Mathf.Abs(i - o[i].gridPos);
                return o.Count > 0 ? sum / o.Count : 0;
            }
        }

        public int contactSpins, selfSpins, contactFlips, selfFlips;
        public long dbgPushes, dbgPushPasses, dbgCompleted, dbgRuns, dbgRunStarted, dbgRunDone, dbgBumpDraft;
        public int diveTraces; int traceAtk = -1, traceTgt = -1; float traceT0, traceAcc;
        public long dbgAhead, dbgClose, dbgFaster, dbgHeld, dbgWant, dbgInsideOk, dbgOutsideOk, dbgStarted, dbgHaz;
        public int Contacts { get { int n = 0; foreach (var e in field) { var c = e.kart.GetComponent<KartContact>(); if (c) n += c.contacts; } return n / 2; } }
        public int HardContacts { get { int n = 0; foreach (var e in field) { var c = e.kart.GetComponent<KartContact>(); if (c) n += c.hardContacts; } return n / 2; } }

        public int WallBreaches { get { int n = 0; foreach (var e in field) n += e.ai.bandBreaches; return n; } }
        public int Incidents { get { int n = 0; foreach (var e in field) n += e.spins + e.flips; return n; } }
        public int Dnfs { get { int n = 0; foreach (var e in field) if (e.outOfRace && !e.finished) n++; return n; } }

        public float BestCleanLap(Entry e)
        {
            float b = float.PositiveInfinity;
            for (int i = 0; i < e.laps.Count; i++) if (e.lapClean[i] && e.laps[i] < b) b = e.laps[i];
            return b;
        }
        public float BestLap(Entry e) { float b = float.PositiveInfinity; foreach (var t in e.laps) b = Mathf.Min(b, t); return b; }
    }
}
