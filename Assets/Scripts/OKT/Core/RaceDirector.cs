// M2 race director (spec §8): phases pace -> green -> checkered -> done, positions by
// unwrapped centerline distance, lap timing, and the per-step traffic commands each AI
// driver receives (spec §7: occupancy, check-up, committed passes; clean racing per
// Franklin's M2 ruling — contact and incidents come in M3).
//
// Rules carried over from the browser sim (js/sim.js), all Franklin's:
//  - the PACE LAP holds the double-file grid formation; nobody accelerates to racing speed
//    before the green; the green flies when the leader crosses S/F after the pace lap
//  - START (Franklin, 11-16): everyone goes at the green and a kart CAN out-accelerate the kart ahead -- that
//    is the accordion push. Nobody pulls out to pass before the start/finish line (inside passes there are a
//    penalty; outside ones are so rare they aren't modelled). After the line it's a scramble into turn 1.
//  - passing is a LATCHED move (side chosen once), abandoned after ~3 s, with a cool-down;
//    the offset arcs in/out at a capped rate (no slithering)
//  - OCCUPANCY: a kart may not steer into a slot another kart holds; running alongside needs
//    a full kart width of room
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OKT
{
    public enum RacePhase { Grid, Pace, Green, Caution, Checkered, Done }

    [System.Serializable]
    public class RaceConfig
    {
        public int fieldSize = 12;                 // spec §1
        public int featureLaps = 15;               // spec §8 (Franklin: 15 or 20)
        // Fun / what-if switches (Franklin 2026-11-17). Read when the race is built: hit reset after changing them.
        public bool cautionsOn = true;             // off: no yellow ever; spun karts sort themselves out under green
        public float powerMult = 1f;               // engine power x this (hp). Top speed is still set by the gearing: the
                                                   // engine stops pulling past ~7,600 rpm, so more power mostly means harder pull
        public float gripMult = 1f;                // tire grip (and brakes, which are tire-limited) x this; drivers corner
                                                   // faster with it (corner speed goes with the square root of grip)
        public int paceLaps = 1;                   // Franklin (M2): one pace lap from the grid
        // One steady speed all the way round (Franklin's playtest 11-17: 25 straight / 20 corner made the line spread on
        // the straights and bunch in the corners -- an accordion). Franklin: ~20-25 mph. [TEST VALUE]
        public float paceStraightMph = 22f;
        public float paceCornerMph = 22f;
        // Staging-grid start (Franklin 2026-11-16: karts line up on the paved grid outside turn 4, pull out through the
        // red gate single file, form up double file over about a lap, then the pace lap). OFF by default for now.
        public static bool DefaultStagingStart = false; // soak knob -okt-staging 1
        // Initial start single file instead of double file (Franklin 11-18: "a toggle ... I can experiment with both").
        // Single file: one line on the groove, nose to tail, from the grid (or out of the staging grid) to the green.
        public static bool DefaultSingleFileStart = false; // soak knob -okt-singlefile 1
        public bool singleFileStart = DefaultSingleFileStart;
        public bool stagingStart = DefaultStagingStart;
        public float stagingMph = 8f;              // rolling out of the grid and down the lane [TEST VALUE]
        public float stagingLeaderMph = 10f;       // the pole kart's speed until the whole field is on the track (was 12: he lapped round to the gate before the grid was empty) [TEST VALUE]
        public float stagingFollowFt = 9f;         // centre to centre behind the kart ahead in the line: nose to tail at a walk (was 13) [TEST VALUE]
        public float stagingYieldFt = 50f;         // at the gate, wait for a kart on the track within this far of it [TEST VALUE]
        public float stagingFirstRowFt = 5f;        // first row, feet up the lane from its exit (south) end [TEST VALUE]
        public float stagingRowFt = 8f;            // row to row on the grid: nose to tail with a couple of feet (10 rows = a 20-kart class in Lane A) [TEST VALUE]
        public float stagingColumnFt = 2.6f;       // each column's offset from its lane's centre line (lanes ~10 ft wide, ~3 karts; two-wide leaves ~1.7 ft between) [TEST VALUE]
        // Franklin 11-17: past ~10 mph the karts ran through the gate, turned back up and into the wall (one parked
        // nose-first on the wall and jammed the whole grid). They slow to this through the gate and onto the dirt
        // whatever the lane speed (6 mph wasn't enough to carry a kart over the crest at the track's edge). [TEST VALUE]
        public float stagingGateMph = 8f;
        // Flips (Franklin 11-17): a rollover is judged only once the kart has come to rest on its side or its back --
        // it used to be called "flipped" the instant it passed 90 deg and the crew froze it there, mid-roll ("stopping
        // the rotation at 90 degrees and holding it there"). Then: "I'd wager 80% of flips have the driver done for";
        // the rest get it back on its wheels, look it over and rejoin from the tail (his brother finished 2nd that way).
        public float flipSettleSec = 1f;           // still for this long on its side / back = flipped [TEST VALUE]
        public float flipRejoinChance = 0.2f;      // Franklin
        public float gridLeaderU = 0.95f;          // front row: front straight, just past the grid entrance (top of T4) [TEST VALUE]
        public float rowGapFt = 6.3f;              // centre to centre: nose ~2.5 in off the kart ahead (Franklin: no daylight between karts in a line); kart 73.1 in (6.1 ft) nose to rear bumper [TEST VALUE]
        public float laneFrac = 0.16f;             // double-file lanes at ±0.16 half-width ≈ 53 in apart: the two lines nearly touching (Franklin) [TEST VALUE]
        public float packedStartSeconds = 4f;      // pass COUNTING only (soak metric continuity); lines are held to the S/F line now
        public float startLanesSec = 6f;           // off a green the double file's lane limits ride with the groove this long [TEST VALUE]
        public float launchSec = 3f;               // accordion: for this long after a green, close right up on the kart ahead [TEST VALUE]
        public float launchLeadMph = 0.9f;         // mph over the kart ahead while closing up -- the push that runs up the line [TEST VALUE]
        // Franklin 11-17: "the trailing karts are anticipating the green" -- nose on the bumper ahead, in the throttle
        // BEFORE the karts in front, pushing them. From this long before the leader goes, the rows behind lean on the
        // bumper ahead; at the green a kart with its nose on a bumper stays flat out and shoves. [TEST VALUEs]
        public float anticipateSec = 0.5f;
        public float anticipateLeanMph = 1f;
        // The tower won't start a field that isn't lined up (Franklin's playtest 11-17: the tail of a staging start was
        // still half a lap back at the green): another pace lap, at most this many. A kart well back of its spot runs
        // up to pace + formCatchUpMph to get there. [TEST VALUEs]
        public float formedFt = 40f;
        public int maxHoldLaps = 2;
        public float formCatchUpMph = 15f;
        public float startRollMinMph = 15f, startRollMaxMph = 25f; // Franklin q24: the leader rolls the start at 15-25 mph, his choice
        // Traffic geometry [TEST VALUEs]
        // A passed driver "changes where he's going to go, not necessarily where he IS" (Franklin 11-18): a kart beside me
        // reshapes my line ahead over a moment instead of snapping my aim; we may rub (centres down to this x side room
        // apart) but I never drive across him. [TEST VALUE]
        public float rubFrac = 0.8f;
        public float alongsideFt = 7.1f;           // longitudinal window that counts as "alongside" (≈ kart length 6.1 ft + 1 ft)
        // Driver awareness (Franklin, 11-16): "you've only got about a 90 degree field of view from your vantage point
        // thanks to the helmet... unless you have a reason to believe there's a kart there -- there isn't, in your mind."
        public float viewHalfAngleDeg = 45f;         // forward cone from the helmet [Franklin]
        public float headBackIn = 6f;                // driver's head sits ~6 in behind the kart's centre [TEST VALUE]
        public float viewRangeFt = 60f;
        public float awareMemorySec = 1.5f;          // a kart seen/felt this recently is still "there" in your mind [TEST VALUE]
        public bool glances = false;                 // Franklin q30: "Typically, people just look forward" -- no glancing
        public float glanceEverySec = 3f;            // (if glances are on) on a straight a driver may glance about this often... [TEST VALUE]
        public float glanceHalfAngleDeg = 120f;      // ...and a glance takes in the sides and a bit behind [TEST VALUE]
        // Defending (Franklin: "karts getting passed never try to defend"): knowing a kart is on him (felt, glanced), the
        // leader covers the inside at entry; once the nose is in he comes down a touch to make the inside kart back out.
        // Scales with aggression. Never into a kart that's level or ahead; always leaves a kart-width above the edge.
        public bool concedeOnlyInLoose = true;       // Franklin q29
        public float defendCoverIn = 14f;           // moves down this far below the groove at entry (aggression 1) [TEST VALUE]
        public float defendSqueezeIn = 3f;          // leans this far into the side-by-side spacing (aggression 1) [TEST VALUE]
        public float sideRoomIn = 38f;              // side by side, centre to centre: 4 in inside the 42 in overall width, so karts beside each other lean and rub (Franklin 11-18: they were giving each other a wide berth -- median 18 in of daylight at 42) [TEST VALUE]
        public float followGapFt = 1.0f;           // bumper-to-bumper gap a kart settles at when it checks up (Franklin's video: nose to tail, ~a kart length) [TEST VALUE]
        public float lookAheadTime = 0.5f;         // spec §7: slot projected ~0.5 s ahead
        public float passStartGapFt = 11f;         // centre to centre: nose within ~4 ft of his bumper (Franklin: passes tried from way too far back) [TEST VALUE]
        public float passHoldGapFt = 16f;          // ...and stays latched inside this gap (browser 0.055 lap)
        public float passAbandonSec = 4f;          // browser sim 3; Franklin's playtest 11-16: drivers too polite [TEST VALUE]
        public float passCooldownSec = 2.5f;       // browser sim
        // The exit-run pass (Franklin, 2026-11-15): a faster kart times a run off the corner, pulls to the inside of the
        // leader on the straight and owns the inside line into the next corner; the leader lifts and files in behind.
        public float runRoomFt = 4f;               // extra gap the follower leaves mid-corner so he can carry speed off it [TEST VALUE]
        public float runAbandonSec = 5f;
        public float bumpDraftAggression = 0.45f;  // drivers above this bumper-draft on the straights even when not faster [TEST VALUE]
        public float bumpLeanMph = 0.67f;          // mph extra while leaning on his bumper [TEST VALUE]           // straight + entry take longer than a dive [TEST VALUE]
        public float fasterBy = 1.002f;            // needs a pace edge (like-for-like free speed) to start a pass; was 1.005 (too polite) [TEST VALUE]
        // Conceding the inside (Franklin, 23:55): established inside me at corner entry -> drag the brakes, file in behind.
        public float concedeEntryCorner = 0.25f;    // corner value at my speed-lookahead that counts as "entry" [TEST VALUE]
        public float concedeNoseFt = 3.0f;          // his nose may be up to this far behind mine and still be "established" (overlapped); was 5 (too polite) [TEST VALUE]
        public float concedeSpeedDropMph = 0.67f;   // mph below his speed while I drop in behind; was 1.3 [TEST VALUE]
        public float concedeHoldSec = 0.8f;         // how long the concession lasts once made; was 1.2 [TEST VALUE]
        public float pushRate = 3f;                 // push likelihood per second at aggression 1 (5 tried 11-16: 4 contact spins a race); late-race urgency raises it [TEST VALUE]
        // Franklin (11-16): "some aggression and awareness that it's only a 15 lap race... Gotta get moving!" -- but no
        // overdriving a corner to clean someone out, so urgency never touches corner speed. On the last lap: push
        // likelihood x(1+this), a pass held 1+this/2 times longer, conceding half as hard. Ramps in linearly. [TEST VALUE]
        public float lateRaceUrgency = 1.0f;
        public int startBattleLapsMin = 1, startBattleLapsMax = 2; // the start is a scramble for a lap or two (Franklin), by aggression [TEST VALUE]
        public float hazardYawDeg = 30f;            // off-line angle that makes a kart a hazard [TEST VALUE]
        public float hazardYawRate = 1.8f;          // rad/s of rotation that makes a kart a hazard [TEST VALUE]
        public float hazardLookSeconds = 2.0f;
        public float crawlMph = 4f;
        // Cautions (Franklin, 2026-11-15) [TEST VALUEs]
        public float cautionCatchUpMph = 5f;        // karts behind run up to pace + this to catch the field (Franklin: "around that")
        public float cautionGapFt = 1f;             // (superseded by the 2-4 ft per-driver gap below; kept for the field stop)
        // Franklin 11-17: general pacing while the tower checks is 2-4 ft bumper to nose -- an obvious line, nobody
        // pushing. Each driver keeps his own gap in that range. [TEST VALUE]
        public float cautionGapMinFt = 2f, cautionGapMaxFt = 4f;
        // One to go: everyone pushes the kart ahead from about halfway between the centre of turns 1-2 and the middle
        // of the backstretch, into the restart (Franklin 11-17).
        public float pushUpU = 0.375f;
        // Tire-warming weave (Franklin 11-17): some drivers, now and then, under caution and pacing; corners too, as
        // long as it doesn't open a gap. Never once the push-up starts. [TEST VALUEs]
        public float weaverShare = 0.33f, weaveAmpFt = 1f, weavePeriodSec = 1.6f, weaveOnSec = 4f, weaveOffMinSec = 8f, weaveOffMaxSec = 20f;
        public float paceCatchUpMph = 5f;           // pace lap: a kart that's dropped back may run up to pace + this to close up (as under caution) [TEST VALUE]
        public float peekLeadSec = 0.8f;            // an inside peek starts only this close (in time) to corner entry, not down the straight (Franklin's playtest) [TEST VALUE]
        public float frustrationMemorySec = 20f;    // failed peeks at the same kart within this long add up [TEST VALUE]
        public int frustratedPeeks = 2;             // after this many failed peeks, push him instead (Franklin: "two, three times") [TEST VALUE]
        public float frustratedPushRate = 4f;       // push likelihood multiplier once frustrated [TEST VALUE]
        public float restartBunchGapFt = 0.25f;     // one to go, after the push-up point: nose on his bumper, pushing him (Franklin 11-17)
        public float cautionLaneFrac = 0.15f;       // single file, a touch above centre
        // a kart dropping back (sent to the tail, or out of place) stays HIGH and lets the line filter by underneath; some
        // slow to a crawl to wait them out (Franklin 11-18: "The kart going to the back should remain high - and sometimes
        // they'll even slow to a crawl"). It used to run low -- right where the line runs on the straights -- and a tail
        // kart that stopped in the groove jammed the whole field behind it.
        public float dropBackLaneFrac = 0.7f;
        public float dropCrawlChance = 0.35f;       // [TEST VALUE]
        public float passDropperLaneFrac = -0.25f;  // the line goes by a dropping kart this low [TEST VALUE]
        // under caution a kart brakes for the kart ahead in time: never faster than what slows to his speed at this
        // decel before the gap closes (Franklin 11-18: lining up, they ran into each other "as if their brakes don't work")
        public float cautionBrakeG = 0.35f;         // [TEST VALUE]
        public float minCautionSec = 15f;
        // Franklin q23: "the leader will go a little bit slow at first because he knows karts are trying to get lined up".
        // While the caution line has gaps, the leader runs this slow so the field can close up. [TEST VALUE]
        public float cautionLeaderSlowMph = 15f;
        // Field stop (Franklin, Pit Board q18): no radios, so when the caution line can't sort itself the field is stopped
        // at the first corner worker. He has the order from the tower and points at each kart in turn; pointed at = go
        // and pace, that's your spot. A two-kart swap is pointed out at pace instead (the drop-back above).
        public int fieldStopAfterChecks = 2;        // leader laps with the line still out of order before the stop [TEST VALUE]
        public int fieldStopMixedKarts = 3;         // or this many karts out of place at once: stop now [TEST VALUE]
        public float fieldStopWatchdogSec = 120f;   // or a caution this long that isn't waiting on a crew [TEST VALUE]
        public float fieldStopGapFt = 3f;           // bumper gap when stopped [TEST VALUE]
        public float fieldStopReleaseSec = 1.5f;    // the worker points at the next kart this long after the last one went [TEST VALUE]
        public float fieldStopPassMph = 6f;         // a released kart easing past the karts still stopped [TEST VALUE]
        public float fieldStopLeadMph = 12f;
        public float fieldStopGatherSec = 45f;      // the worker stops waiting for stragglers after this [TEST VALUE]
        public float fieldStopNextWaitSec = 5f;     // and points at the next kart even if the last one isn't clear yet [TEST VALUE]        // the first kart away runs slowly until the whole field is released [TEST VALUE]
        public float packBehindSec = 1.5f;          // a spin with a kart this close behind (or 15 m ahead) is "in a pack"
        public bool cautionForLoneSpin = false;     // some tracks throw it for any spin (Franklin): track setting                 // speed a blocked kart creeps through a jam at [TEST VALUE]      // how far ahead (in time) drivers watch for trouble [TEST VALUE]
        public float outsidePassMargin = 0.6f;
        public float insidePassMargin = 0.3f;       // m of spare room above the inside edge to dive [TEST VALUE]
        // The push-up pass (Franklin 22:10 / 23:20): nose on his bumper at corner entry, push him in slightly hot,
        // he drifts up, take the bottom. [TEST VALUEs]
        // A big push (Franklin, 11-16): karts live at the edge of grip, so a hard shove at entry can rotate a kart past what
        // the front tires can pull back. Aggressive, frustrated or late-race drivers sometimes lean much harder. [TEST VALUE]
        public static float DefaultBigPushChance = 0.7f;   // soak 11-17: 0.35 -> ~0.5 cautions/race; 0.7 (+ slide 0.72) -> ~1.0
        public float bigPushChance = DefaultBigPushChance; // per push, at aggression 1 (x urgency, x2 when frustrated)
        public float bigPushDelta = 2.0f;           // m/s over him (~4.5 mph) instead of pushSpeedDelta
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
        // Franklin 11-17: under caution, a kart spun in a corner with its nose in the inside berm -- the driver hops out,
        // picks up the nose, turns it the right way by hand and lines up with the field. No worker, and no trip to the
        // tail for that alone. [TEST VALUEs]
        public float bermSelfRotateAfterSec = 5f, driverHopOutSec = 3f;
        // Franklin 11-17: a driver sent to the tail (not damaged) is still fast -- and a mad or frustrated one pushes
        // the kart harder, which tends to help him or hurt him. The chance he's mad = his aggression. Mad: aggression
        // up by madAggression, and he drives the corners harder (cornerSlow cut by madCornerCut). [TEST VALUEs]
        public float madAggression = 0.3f, madCornerCut = 0.012f;
        public float recoverMaxSeconds = 12f;       // can't get going in this long -> out (pulls in / stopped)
        public float crewWalkMph = 3f;               // crew pushes a wreck to the infield at walking pace (§8.4)
        public float crewTurnDegPerSec = 45f;        // crew straightening a stuck kart by hand [TEST VALUE]
        // Infield track workers (Franklin, 2026-11-15): two of them, one covering turns 1-2 and one turns 3-4, standing
        // "a good ten feet or so away from the racing surface" (they also wave the yellow with the flagman at S/F).
        public float workerBackFt = 10f;
        public float crewGiveUpSec = 75f;               // a crew job this long and the kart is retired [TEST VALUE]
        public float driverSelfHelpSec = 15f;            // waiting on a busy worker this long, the driver hops out himself [TEST VALUE]
        public float[] workerPostU = { 0.25f, 0.75f };   // mid turns 1-2 and 3-4 on this track [TEST VALUE: read off the groove apexes]
        // Stub incident detection (M2): spins / flips / stops are logged, not judged (M3).
        public float spinAngleDeg = 75f;
        // Under caution a kart pointed more than this off the track crawls until it's straight (a spun kart handed back
        // at 40-60 degrees floored it to catch the line, slid down the banking and off the inside edge, soak 2026-11-17)
        public float cautionStraightenDeg = 30f;    // [TEST VALUE]
        public float stoppedSeconds = 3f;
        public float dnfStoppedSeconds = 15f;
        // Tires after a caution (Franklin: "cautions will cause the tire to cool, and they may not fire off properly
        // again"). [TEST VALUE] all four.
        public static float QualiFeatureSpreadSec = 0.12f;
        public static float LightPrepChance = 0.25f, LightPrepFalloffMin = 0.03f, LightPrepFalloffMax = 0.05f; // tires that go off [TEST VALUE] // qualifying pace = feature pace +- this (was 0.04) [TEST VALUE]
        public const float TireCautionCool = 0.6f;     // share of the green-flag cold penalty a restart brings back
        public float tireNoRefireChance = 0.3f;
        public float tireNoRefireMin = 0.005f, tireNoRefireMax = 0.015f; // lap-time fraction, ~0.05-0.16 s
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
        // ---- the kart's night (Franklin, 2026-11-15: where a faster kart gains is "both, and neither").
        // Gearing: shorter (more axle teeth) drives off the corner harder but goes gear bound and floats the valves at
        // the end of the straight; taller is the reverse. Falls out of the engine model (power curve, KartSetup).
        public int axleTeeth = 63;
        // Tires (Franklin, Pit Board 2026-11-15), as LAP-TIME fractions over the kart's peak lap (so they carry over
        // when the sim's laps come down to his 10.6s): "two or three laps for tires to come in" -- lap 2 ~11.0, lap 3
        // ~10.8, then 10.6-10.7 for the next 5-10 laps depending on prep. Properly prepped tires can last the whole
        // race; too little prep burns off and the kart "skates" (once tires fall off they may lose half a second or
        // more); too much prep produces too much grip and locks the kart down. Cautions cool the tires and they
        // may not fire off properly again.
        public float tireColdFrac;         // lap-time penalty on green lap 1 (Franklin: lap 2 ~3%, lap 1 slower still)
        public int tireWarmLaps = 3;       // laps to come in (2-3)
        public int tireHoldLaps = 99;      // laps at peak after coming in, before a light-prepped set goes off
        public float tireFalloffFrac;      // lap-time penalty once it has gone off (Franklin: ~0.5 s = 4.7%)
        public int tireFalloffLaps = 4;    // laps to go from peak to fully gone
        public float tireLockFrac;         // over-prepped: locked down, a touch slow at peak
        public string tireNote = "";       // "" proper, "light prep", "over-prepped"
        /// <summary>Lap-time penalty (fraction of a lap) from the tires: green lap index, laps since the last green
        /// (start or restart), whether this run began at a restart (tires partly cooled), and any failure to refire.</summary>
        public float TireLapFrac(int lap, int sinceGreen, bool restart, float refireMiss)
        {
            float cold = restart ? tireColdFrac * RaceConfig.TireCautionCool : tireColdFrac;
            float warm = sinceGreen < tireWarmLaps ? cold * (1f - (float)sinceGreen / tireWarmLaps) : 0f;
            int past = lap - tireWarmLaps - tireHoldLaps;
            float off = past > 0 ? tireFalloffFrac * Mathf.Min(1f, (float)past / tireFalloffLaps) : 0f;
            return warm + off + tireLockFrac + refireMiss;
        }
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
                // Qualifying is two laps on fresh tires; the feature is 15 laps of tires coming in and going off, traffic and
                // setup luck (Franklin q26/q27: pole wins ~20%; 12th-to-3rd and pole-to-8th both happen). [TEST VALUE spread]
                float q = off + (float)(rng.NextDouble() * 2 - 1) * RaceConfig.QualiFeatureSpreadSec; string note = "";
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
            // The kart's night: gearing and tires, drawn from their own stream so the driver draws above don't move.
            // [TEST VALUE] spreads, all pending Franklin. Book Ch 24: 12/63 is Franklin's gear, +-1 axle tooth = +-150 rpm.
            var kr = new System.Random(seed * 31 + 5);
            float K(float lo, float hi) => lo + (float)kr.NextDouble() * (hi - lo);
            foreach (var d in list)
            {
                double g = kr.NextDouble();
                d.axleTeeth = g < 0.10 ? 61 : g < 0.30 ? 62 : g < 0.70 ? 63 : g < 0.90 ? 64 : 65;
                // Tires (Franklin's lap times, see DriverProfile). Mix of prep outcomes [TEST VALUE odds].
                d.tireColdFrac = K(0.040f, 0.050f);                       // green lap 1: ~0.45-0.55 s off peak
                d.tireWarmLaps = kr.NextDouble() < 0.5 ? 2 : 3;
                double t = kr.NextDouble();
                if (t < RaceConfig.LightPrepChance)
                {   // too little prep: burns off, skates, loses ~0.3-0.5 s
                    d.tireHoldLaps = Mathf.RoundToInt(K(4f, 8f)); d.tireFalloffFrac = K(RaceConfig.LightPrepFalloffMin, RaceConfig.LightPrepFalloffMax);
                    d.tireFalloffLaps = Mathf.RoundToInt(K(3f, 5f)); d.tireNote = "light prep";
                }
                else if (t < RaceConfig.LightPrepChance + 0.20)
                {   // too much prep: locked down, a tenth or so slow all race
                    d.tireLockFrac = K(0.005f, 0.015f); d.tireNote = "over-prepped";
                }
                // else: properly prepped, lasts the whole race
            }
            list.Sort((a, b) => a.qualiOffsetSec.CompareTo(b.qualiOffsetSec)); // grid = qualifying order, no invert
            return list;
        }
    }

    public class RaceDirector
    {
        public readonly RaceConfig cfg;
        public RacePhase phase = RacePhase.Grid;
        public float phaseTime, raceTime, greenTime = -1f, firstGreenTime = -1f; // greenTime = latest green (restarts move it)
        public float greenMaxGapLaps = -1f, greenSpreadLaps = -1f; // packed start (spec §12.3), measured at the first green
        // after the first green: karts with a kart alongside, and leader-to-last spread (ft), at fixed times (soak metric)
        public static readonly float[] StartSampleSec = { 2f, 4f, 6f, 9f, 12f, 16f, 22f };
        public readonly List<int> startAlongside = new List<int>(); public readonly List<float> startSpreadFt = new List<float>();
        public int cautions;            // cautions thrown this race (spec 8.4, pulled forward from M3 at Franklin's request)
        // ---- caution state
        readonly List<Entry> cautionBase = new List<Entry>(), cautionTail = new List<Entry>();
        public readonly List<Entry> cautionQueue = new List<Entry>();
        public bool oneToGo; float cautionStart, goDist; int cautionLeaderLap = -1;
        readonly Queue<(float t, List<Entry> order)> orderSnaps = new Queue<(float t, List<Entry> order)>();
        float snapT;
        public readonly List<string> events = new List<string>();
        public StringBuilder trace; public float traceUntil = 7f; // debug: per-kart state for the first seconds of green
        readonly World w;
        float L;                        // lap length on the centerline (m)
        public float LapLengthM => L;
        readonly Entry[] workerBusy = new Entry[2]; // the kart each infield worker is helping (null = free)

        public class Entry
        {
            public KartAI ai; public KartPhysics kart; public DriverProfile driver; public int gridPos; public float baseGripG;
            public int idx; public KartContact kc;
            public int unsetTick, lineTick; public bool unsetV; public float lineV;   // per-pass memos (RaceDirector.Unsettled / LineAt)       // place in field (never reordered) and the contact log: cached, not looked up per step
            public float paceGapFt = 3f;                 // this driver's bumper gap when pacing under caution (2-4 ft)
            public bool weaver; public float weaveClock;  // tire-warming weave: does he, and time to the next change
            public bool weaving;
            public bool mad;                            // sent to the tail and mad about it: races angrier the rest of the night
            public int greenStartLap; public bool restarted; public float refireMiss; // tires: laps since the last green; cooled by a caution
            public bool finished; public int finishPos; public float finishTime;
            public bool outOfRace; public string outReason;
            public int lapsDone;                  // green laps completed
            public float lastCross = -1f;         // time of last S/F crossing during green
            public bool preLine;                  // green, but hasn't reached the start/finish line yet: hold your line
            public float overT;                   // time at rest on its side or back (flip settling)
            public bool flipRejoin;               // flipped, but the crew put it back on its wheels: rejoins at the tail
            public bool joinedFromGrid;           // staging: its race distance was set on reaching the track
            public float nextLine;                // next S/F distance to cross (0 = not yet set)
            public readonly List<float> laps = new List<float>();
            public readonly List<bool> lapClean = new List<bool>();
            public bool cleanThisLap = true;
            // passing state
            public int attackTarget = -1; public float attackT, attackCool; public int attackSide; public bool attackRun;
            // incident stub
            public bool inIncident; public int spins, flips; public float stoppedT;
            public bool blocked;
            public string cdbg = "";
            public float crewHelpT;               // when the workers took the job
            public readonly Dictionary<int, float> knowT = new Dictionary<int, float>(); public float glanceT; // awareness
            public int defendedAgainst = -1; public float defendedT = -99f; // I last defended against this kart (he gets annoyed)
            public bool fsHeld, fsReleased; public float fsStopAt, fsHeldAt; // field stop: stopped and waiting / pointed at and gone
            public bool dropCrawl;
            public bool toTail, spotBack, dropping; // caution: sent to the tail / keeps his spot (the kart that wrecked him goes back)   // a kart right in front of me: stopped here means waiting in a jam, not wrecked
            public float bobbleT;
            public float concedeT; public int concedeTo = -1;
            public int breachesSeen;
            public float pushT, pushCool; public int pushTarget = -1; public bool pushBig;   // the bumper-push pass (Franklin)
            public int peeks, peekTarget = -1; public float peekT;       // failed inside peeks at one kart (frustration -> push)
            public float pushedT;                                       // I'm being pushed: run in a touch hot, drift up
            public readonly Queue<string> hist = new Queue<string>(); public float histT;
            public bool recovering; public float recoverT;     // spun kart driving itself out (spec §8.3)
            public bool cleared; public bool beingCleared;      // wreck moved off by the crew (spec §8.4 step 3)
            public bool crewHelp, crewHelped, assistStarted; public float assistWait; public float crewS; // crewS: where the crew has it (own track position: the AI's s re-projects from the body each step) // stuck kart separated + pull-started by track workers
            public int worker = -1; public float workerArrives; public bool offLogged;                        // which infield worker, and when they get to it
        }
        public readonly List<Entry> field = new List<Entry>();
        int nextFinish = 1;
        System.Random rng;
        float Gauss() { double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble(); return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2)); }
        /// <summary>Tire lap-time penalty for this kart right now.</summary>
        float TireFrac(Entry e) => e.driver.TireLapFrac(e.lapsDone, e.lapsDone - e.greenStartLap, e.restarted, e.refireMiss);
        /// <summary>New lap: this lap's pace = the driver's pace x a consistency wobble.</summary>
        void RollLapPace(Entry e)
        {
            float sigmaSec = cfg.lapSigmaSecAtZero * (100f - e.driver.consistency) / 100f;
            float baseLap = 11.03f;
            e.ai.cfg.pace = e.driver.paceFactor * baseLap / (baseLap + Gauss() * sigmaSec);
            // Tires: the physics grip is set from the tire state, and the driver feels it — corner speed goes with
            // the square root of grip, so his pace in the turns does too (straights are engine-limited regardless).
            // Pace follows lap time (the same convention as feature pace); grip = pace^2 (corner speed ~ sqrt(grip)).
            float tp = 1f / (1f + TireFrac(e));
            e.kart.setup.gripG = e.baseGripG * tp * tp;
            e.ai.cfg.pace *= tp * Mathf.Sqrt(cfg.gripMult);
        }

        /// <summary>Is a quicker than b where it counts? A pace edge, or (off a corner) a shorter gear with pace close.</summary>
        bool Quicker(Entry a, Entry b, bool offCorner)
            => NightPace(a) > NightPace(b) * cfg.fasterBy
               || (offCorner && a.driver.axleTeeth > b.driver.axleTeeth && NightPace(a) > NightPace(b) * 0.995f);
        /// <summary>The kart's pace right now: the driver's feature pace with his tires' state (no lap-to-lap wobble).</summary>
        float NightPace(Entry e) => e.driver.paceFactor / (1f + TireFrac(e));

        public RaceDirector(World world, RaceConfig config, int seed = 0) { w = world; cfg = config ?? new RaceConfig(); L = w.track.centerLength; rng = new System.Random(seed * 7919 + 17); }

        public void Add(KartAI ai, KartPhysics kart, DriverProfile d, int gridPos)
        {
            // per-driver habits from a hash of his number (not the race RNG: adding these didn't reshuffle every race)
            float h1 = Hash01(d.number * 7919 + 11), h2 = Hash01(d.number * 104729 + 3), h3 = Hash01(d.number * 1299709 + 5);
            field.Add(new Entry
            {
                ai = ai, kart = kart, driver = d, gridPos = gridPos, baseGripG = ScaleKart(kart), idx = field.Count, kc = kart.GetComponent<KartContact>(),
                paceGapFt = Mathf.Lerp(cfg.cautionGapMinFt, cfg.cautionGapMaxFt, h1),
                weaver = h2 < cfg.weaverShare, weaveClock = Mathf.Lerp(cfg.weaveOffMinSec, cfg.weaveOffMaxSec, h3),
            });
        }
        /// <summary>Apply the power / grip multipliers to a new kart's setup once; returns its base grip.</summary>
        float ScaleKart(KartPhysics kart)
        {
            kart.setup.crankHp *= cfg.powerMult;
            kart.setup.gripG *= cfg.gripMult;
            kart.setup.brakeCapG *= cfg.gripMult;
            return kart.setup.gripG;
        }
        static int Ix(Entry x) => x == null ? -1 : x.idx;   // was field.IndexOf: a linear search inside the per-pair loops
        float Aggr(Entry e) => Mathf.Min(1f, e.driver.aggression + (e.mad ? cfg.madAggression : 0f));
        static float Hash01(int x) { unchecked { uint h = (uint)x; h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f; } }

        /// <summary>Tire-warming weave: a weaver weaves for a few seconds now and then while pacing.</summary>
        void UpdateWeave(Entry e, float dt, bool allowed)
        {
            if (!e.weaver || !allowed) { e.weaving = false; e.ai.weaveAmp = 0f; return; }
            e.weaveClock -= dt;
            if (e.weaveClock <= 0f)
            {
                e.weaving = !e.weaving;
                e.weaveClock = e.weaving ? cfg.weaveOnSec * (0.75f + 0.5f * (float)rngWeave.NextDouble())
                                         : Mathf.Lerp(cfg.weaveOffMinSec, cfg.weaveOffMaxSec, (float)rngWeave.NextDouble());
            }
            e.ai.weaveAmp = e.weaving ? cfg.weaveAmpFt * U.Ft : 0f; e.ai.weavePeriod = cfg.weavePeriodSec;
        }
        System.Random rngWeave = new System.Random(4242);

        void Event(string s) { events.Add($"[{raceTime:F1}s] {s}"); }
        public string Tag(Entry e) => $"#{e.driver.number} {e.driver.name}";

        /// <summary>Distance (m) of the S/F line that ends the pace lap and starts green lap 1.</summary>
        public float GreenLine => (cfg.paceLaps + towerLaps) * L;
        int towerLaps; // extra pace laps the tower called (staging start: someone still coming out of the grid)
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

        bool staged, stagingAnnounced, anticipating; int formHolds;
        /// <summary>Lanes in the initial start formation: 2 (double file) or 1 (single file).</summary>
        int Lanes => cfg.singleFileStart ? 1 : 2;

        /// <summary>The kart furthest back from its spot in the start line (the kart ahead of it in its lane).</summary>
        Entry Straggler(out float back)
        {
            Entry worst = null; back = 0f;
            foreach (var e in field)
            {
                if (e.outOfRace || e.gridPos < Lanes) continue;
                var p = field.Find(x => x.gridPos == e.gridPos - Lanes);
                if (p == null || p.outOfRace) continue;
                float b = p.ai.raceDist - e.ai.raceDist - cfg.rowGapFt * U.Ft;
                if (b > back) { back = b; worst = e; }
            }
            return back > cfg.formedFt * U.Ft ? worst : null;
        }

        /// <summary>Franklin 11-17: a button to throw a caution by hand (works with cautions off too).</summary>
        public bool CanCaution => phase == RacePhase.Green;
        public void ManualCaution() { if (phase == RacePhase.Green) ThrowCaution("thrown by hand from the tower"); }
        bool AnyStaging() { foreach (var o in field) if (!o.outOfRace && o.ai.stagePath != null) return true; return false; }

        /// <summary>Staging-grid start: out of the grid single file in pace order, each kart following the one ahead of it
        /// (the kart beside it on the grid waits its turn). The AI drives the lane path itself; this sets its speed.</summary>
        void StagingDrive(Entry e)
        {
            var ai = e.ai; ai.formation = false; ai.paceMode = true; ai.trafficGoal = 0f;
            float cap = cfg.stagingMph * U.Mph;
            var prev = field.Find(x => x.gridPos == e.gridPos - 1);
            if (prev != null && !prev.outOfRace)
            {
                float dist = Vector3.Distance(prev.kart.transform.position, e.kart.transform.position);
                cap = Mathf.Min(cap, Mathf.Max(0f, prev.kart.ForwardSpeed + 1.0f * (dist - cfg.stagingFollowFt * U.Ft)));
                if (prev.ai.stagePath != null && prev.ai.stageIdx == 0 && dist < cfg.stagingFollowFt * U.Ft * 1.5f) cap = 0f; // not rolling yet
                // my turn only once he's past me on the way to the gate (Lane B sits nearer the exit than the back of Lane A)
                if (prev.ai.stagePath != null && ToGate(prev.ai) > ToGate(ai) - cfg.stagingFollowFt * U.Ft) cap = 0f;
            }
            float gate = cfg.stagingGateMph * U.Mph;
            // about to pull onto the dirt: wait for a kart coming by on the track (they lap round while the grid empties)
            float tg = ToGate(ai);
            if (tg > 0f && tg < cfg.stagingYieldFt * U.Ft * 0.25f)
                foreach (var o in field)
                {
                    if (o.outOfRace || o.ai.stagePath != null || o.kart.ForwardSpeed < 1f) continue;
                    float toGateArc = Mathf.Repeat(ai.stageGateArc - w.track.ArcAt(o.ai.s), L);
                    if (toGateArc < cfg.stagingYieldFt * U.Ft) { cap = 0f; break; }
                }
            if (ai.stageIdx >= ai.stageSlowIdx) cap = Mathf.Min(cap, gate);
            else cap = Mathf.Min(cap, Mathf.Sqrt(gate * gate + 2f * 2.5f * Vector3.Distance(e.kart.transform.position, ai.stageGatePos))); // ~0.25 g down to it
            ai.speedCap = cap;
        }

        /// <summary>How far a staging kart still has to drive along its own path to the gate (negative once through it).</summary>
        static float ToGate(KartAI ai)
        {
            var p = ai.stagePath; if (p == null) return float.NegativeInfinity;
            int g = Mathf.Min(ai.stageGateIdx, p.Count - 1), i = Mathf.Min(ai.stageIdx, p.Count - 1);
            var here = ai.transform.position; here.y = 0f; var q = p[i]; q.y = 0f;
            float d = Vector3.Distance(here, q);
            if (i > g) return -Mathf.Max(0.01f, PathLen(p, g, i) - d); // past the gate: minus how far past it
            for (int j = i; j < g; j++) d += Vector3.Distance(p[j], p[j + 1]);
            return d;
        }

        static float PathLen(List<Vector3> p, int a, int b) { float d = 0f; for (int j = a; j < b; j++) d += Vector3.Distance(p[j], p[j + 1]); return d; }

        float startRollMph = 20f;
        public void Start()
        {
            phase = RacePhase.Pace; phaseTime = 0;
            startRollMph = cfg.startRollMinMph + (float)rng.NextDouble() * (cfg.startRollMaxMph - cfg.startRollMinMph);
            staged = AnyStaging();
            if (staged) Event($"Field pulls out of the staging grid single file and forms up {(Lanes == 1 ? "single" : "double")} file, then the pace lap. Leader will roll the start at {startRollMph:F0} mph.");
            else Event($"Field rolls off the grid — pace lap. Leader will roll the start at {startRollMph:F0} mph.");
        }

        /// <summary>Called every physics step before the AIs drive.</summary>
        public void PreStep(float dt)
        {
            memoTick++;
            raceTime += dt; phaseTime += dt;
            float paceS = cfg.paceStraightMph * U.Mph, paceC = cfg.paceCornerMph * U.Mph;
            float kartLen = cfg.alongsideFt * U.Ft, room = cfg.sideRoomIn * U.In;
            bool green = phase == RacePhase.Green;
            bool launch = green && raceTime - greenTime < cfg.launchSec;
            anticipating = false; bool stagingNow = staged && AnyStaging();
            if (phase == RacePhase.Pace)
            {
                var pole = field.Find(x => x.gridPos == 0);
                if (pole != null && !AnyStaging()) anticipating = GoLine - pole.ai.raceDist < Mathf.Max(1f, pole.kart.ForwardSpeed) * cfg.anticipateSec;
            }

            // lane leaders for the pace formation
            foreach (var e in field)
            {
                var ai = e.ai;
                ai.paceStraight = paceS; ai.paceCorner = paceC;
                ai.speedCap = float.PositiveInfinity; ai.attackBoost = 0f; ai.pushedBoost = 0f;
                ai.latMin = float.NegativeInfinity; ai.latMax = float.PositiveInfinity; ai.grooveLane = KartAI.NoGrooveLane;
                ai.latHardMin = float.NegativeInfinity; ai.latHardMax = float.PositiveInfinity; // (stale floors from the last green lap jammed the caution line)
                ai.rejoinOnGroove = false; ai.weaveAmp = 0f;
                ai.limitsRideGroove = green && raceTime - greenTime < cfg.startLanesSec; // the weave is caution-only: CautionDrive turns it back on
                if (e.outOfRace) { ai.paceMode = true; ai.speedCap = 0f; continue; }

                if (phase == RacePhase.Grid) { ai.paceMode = true; ai.speedCap = 0f; continue; }
                if (ai.stagePath != null) { StagingDrive(e); continue; }
                // Onto the track from the staging grid: count its race distance from the kart ahead of it in the line, not
                // from where it started. The leader circles slowly while the grid empties -- most of a lap for a big field --
                // so a kart counted from the gate read a lap down while right behind the field: the formation and the
                // caution line sorted it a lap back and every caution jammed (staging soak 2026-11-18: 11 of 12 timed out).
                if (staged && !e.joinedFromGrid)
                {
                    e.joinedFromGrid = true;
                    var pv = field.Find(x => x.gridPos == e.gridPos - 1);
                    if (pv != null && pv.joinedFromGrid && !pv.outOfRace)
                        ai.raceDist = pv.ai.raceDist - Mathf.Repeat(w.track.ArcAt(pv.ai.s) - w.track.ArcAt(ai.s), L);
                }
                if (staged && !stagingAnnounced && !AnyStaging()) { stagingAnnounced = true; Event($"STAGING: the whole field is out of the grid and on the track. Forming up {(Lanes == 1 ? "single" : "double")} file."); }

                if (phase == RacePhase.Pace)
                {
                    // Double-file formation, pace speed, hold the row spacing to the kart ahead in the same lane;
                    // the outside row holds alongside the inside row. Nobody accelerates to racing speed.
                    ai.formation = true; ai.paceMode = true; ai.trafficGoal = 0f;
                    if (Lanes == 1) { ai.laneFrac = cfg.cautionLaneFrac; ai.grooveLane = 0; } // single file on the groove, like a caution line
                    else
                    {
                        ai.laneFrac = (e.gridPos % 2 == 0 ? -1f : 1f) * cfg.laneFrac;
                        ai.grooveLane = e.gridPos % 2 == 0 ? -1 : 1; ai.grooveLaneHalf = room * 0.5f;
                    }
                    Entry ahead = null; float gap = float.MaxValue;
                    foreach (var o in field)
                    {
                        if (o == e || o.outOfRace || o.gridPos % Lanes != e.gridPos % Lanes || o.ai.stagePath != null) continue;
                        float g = o.ai.raceDist - ai.raceDist; if (g > 0f && g < gap) { gap = g; ahead = o; }
                    }
                    // the leader rolls the start at his chosen speed over the last third of the pace lap (Franklin q24)
                    if (e.gridPos == 0 && stagingNow)
                    {
                        // ...and slow enough that he doesn't come back round to the gate before the grid is empty (a big
                        // field's line is most of a lap long): time the rest need to get out vs the distance he has left
                        int left = 0; float gArc = 0f; foreach (var o in field) if (!o.outOfRace && o.ai.stagePath != null) { left++; gArc = o.ai.stageGateArc; }
                        float roomToGate = Mathf.Repeat(gArc - w.track.ArcAt(ai.s), L) - cfg.stagingYieldFt * U.Ft;
                        float need = left * cfg.stagingFollowFt * U.Ft / (cfg.stagingGateMph * U.Mph) + 3f;
                        float vMax = Mathf.Clamp(roomToGate / need, 1.5f * U.Mph, cfg.stagingLeaderMph * U.Mph);
                        ai.paceStraight = Mathf.Min(paceS, vMax); ai.paceCorner = Mathf.Min(paceC, vMax);
                    }
                    if (e.gridPos == 0 && ai.raceDist > GoLine - 0.35f * L)
                    { ai.paceStraight = Mathf.Min(paceS, startRollMph * U.Mph); ai.paceCorner = Mathf.Min(paceC, startRollMph * U.Mph); }
                    if (ahead != null)
                    {
                        ai.speedCap = Mathf.Max(0f, ahead.kart.ForwardSpeed + 1.0f * (gap - cfg.rowGapFt * U.Ft));
                        // Franklin's playtest (11-16): gaps opened in the formation and never closed -- the cap allowed
                        // closing but the pace target (25/20 mph) didn't. A kart that's dropped back runs up to pace + 5.
                        float lag = Mathf.Clamp(ai.speedCap - paceS, 0f, (gap > 3f * cfg.rowGapFt * U.Ft ? cfg.formCatchUpMph : cfg.paceCatchUpMph) * U.Mph);
                        ai.paceStraight = paceS + lag; ai.paceCorner = paceC + lag;
                    }
                    // Outside row: hold alongside the inside kart of the same row (Franklin's screenshot: the whole
                    // outside row ran a full row back). The outside lane is longer through the turns, so at plain
                    // pace speed it lost ground every corner and could never make it up — let it run faster than
                    // pace to stay level, never slower than its row partner.
                    if (Lanes == 2 && e.gridPos % 2 == 1)
                    {
                        var partner = field.Find(x => x.gridPos == e.gridPos - 1);
                        if (partner != null && !partner.outOfRace)
                        {
                            float err = partner.ai.raceDist - ai.raceDist; // + = I'm behind him
                            float want = Mathf.Max(0f, partner.kart.ForwardSpeed + 1.0f * err);
                            ai.speedCap = ahead != null ? Mathf.Min(ai.speedCap, Mathf.Max(want, ahead.kart.ForwardSpeed)) : want;
                            float catchUp = Mathf.Max(0f, want - ai.kart.ForwardSpeed) + Mathf.Max(0f, err);
                            ai.paceStraight = Mathf.Max(ai.paceStraight, paceS + catchUp); ai.paceCorner = Mathf.Max(ai.paceCorner, paceC + catchUp);
                        }
                    }
                    // a straggler closes up at no more than pace + formCatchUpMph (the outside-row catch-up had no ceiling: a
                    // kart far back ran 44 mph round the pace lap, met the field slowing for the start and rolled it)
                    { float top = paceS + cfg.formCatchUpMph * U.Mph; ai.paceStraight = Mathf.Min(ai.paceStraight, top); ai.paceCorner = Mathf.Min(ai.paceCorner, top); }
                    // the grid is still emptying: the line circulates slowly behind the leader (catching up at 37 mph, karts came
                    // round to the gate while others were still pulling out and hit them)
                    if (stagingNow) { float sc = (cfg.stagingLeaderMph + cfg.paceCatchUpMph) * U.Mph; ai.paceStraight = Mathf.Min(ai.paceStraight, sc); ai.paceCorner = Mathf.Min(ai.paceCorner, sc); }
                    // anticipating the green: nose onto the bumper ahead and lean on it
                    if (anticipating && ahead != null && e.gridPos >= Lanes)
                    {
                        var stA = ai.kart.setup; float bl = stA.FootFront + stA.FootRear;
                        ai.speedCap = Mathf.Max(0f, ahead.kart.ForwardSpeed + 1.0f * (gap - bl) + cfg.anticipateLeanMph * U.Mph);
                        float up = paceS + 3f * U.Mph; ai.paceStraight = Mathf.Max(ai.paceStraight, up); ai.paceCorner = Mathf.Max(ai.paceCorner, up);
                    }
                    continue;
                }

                if (phase == RacePhase.Caution && !e.finished) { long pt1 = Perf.T(); CautionDrive(e, paceS, paceC); Perf.Add(14, ref pt1); continue; }

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
                ai.looseAwareness = Mathf.Lerp(0.6f, 0.95f, Mathf.InverseLerp(35f, 95f, e.driver.consistency));
                long pt0 = Perf.T(); Traffic(e, dt, kartLen, room, green && e.preLine, launch); Perf.Add(13, ref pt0);
            }
        }

        /// <summary>A kart that is out, spinning, flipped or crawling: an obstacle to route around, not a car to race.</summary>
        /// <summary>Spec §7: a kart that's sideways, airborne or flipping ahead is a hazard to route around — flag it early.</summary>
        bool Unsettled(Entry o)
        {
            // Pure function of o's physical state, which doesn't change until the next physics step: worked out once
            // per director pass instead of for every pair that asks (perf pass 2026-11-17).
            if (o.unsetTick == memoTick) return o.unsetV;
            o.unsetTick = memoTick; o.unsetV = UnsettledCalc(o); return o.unsetV;
        }
        bool UnsettledCalc(Entry o)
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
        /// <summary>The groove's offset where o is now (memoised per director pass like Unsettled).</summary>
        float LineAt(Entry o)
        {
            if (o.lineTick != memoTick) { o.lineTick = memoTick; o.lineV = w.track.Frame(o.ai.s).LineOffset; }
            return o.lineV;
        }
        int memoTick = 1;   // bumped at the start of PreStep and PostStep: memos never outlive a physics step
        bool Hazard(Entry o) => (o.outOfRace && !o.cleared) || o.inIncident || o.recovering || o.crewHelp || Unsettled(o);

        /// <summary>Who this driver knows is there: karts in the ~90 deg view from the helmet, karts felt (contact), karts
        /// taken in by a glance on a straight -- each remembered for a moment (Franklin, 11-16).</summary>
        void UpdateAwareness(Entry e, in TrackFrame fr, float dt)
        {
            var tr = e.kart.transform;
            var head = tr.TransformPoint(0f, 0f, -cfg.headBackIn * U.In);
            var headL = tr.InverseTransformPoint(head);
            float range = cfg.viewRangeFt * U.Ft;
            bool glance = false;
            e.glanceT -= dt;
            if (e.glanceT <= 0f)
            {
                e.glanceT = cfg.glanceEverySec * (0.6f + 0.8f * (float)rng.NextDouble());
                glance = cfg.glances && fr.corner < 0.15f && rng.NextDouble() < e.driver.consistency / 100f; // off: Franklin q30, drivers just look forward
            }
            var kc = e.kc;
            for (int i = 0; i < field.Count; i++)
            {
                var o = field[i]; if (o == e || o.cleared) continue;
                var c = o.kart.transform.position;
                if ((c - head).sqrMagnitude > range * range) continue;
                // his centre, nose and tail (no per-pair array: that was garbage every step for every pair in range)
                bool seen = Sees(tr, headL, c, glance);
                if (!seen) { var otr = o.kart.transform; seen = Sees(tr, headL, otr.TransformPoint(0f, 0f, o.kart.setup.FootFront), glance) || Sees(tr, headL, otr.TransformPoint(0f, 0f, -o.kart.setup.FootRear), glance); }
                bool felt = kc != null && kc.lastOther == o.kart && w.simTime - kc.lastContactTime < 0.3f;
                // alongside with his nose at or past my helmet: I know he's there, looking or not (Franklin 11-18, photo of
                // the white kart: "when your nose is at the yellow line, it's a safe assumption that the white kart sees you")
                if (!seen && !felt)
                {
                    var otr = o.kart.transform;
                    var noseL = tr.InverseTransformPoint(otr.TransformPoint(0f, 0f, o.kart.setup.FootFront));
                    var tailL = tr.InverseTransformPoint(otr.TransformPoint(0f, 0f, -o.kart.setup.FootRear));
                    seen = noseL.z >= headL.z && tailL.z <= e.kart.setup.FootFront && Mathf.Abs(noseL.x) < 2f * cfg.sideRoomIn * U.In;
                }
                if (seen || felt) e.knowT[i] = raceTime;
            }
        }

        bool Sees(Transform tr, Vector3 headL, Vector3 pt, bool glance)
        {
            var lp = tr.InverseTransformPoint(pt) - headL;
            float ang = Mathf.Abs(Mathf.Atan2(lp.x, lp.z) * Mathf.Rad2Deg);
            return ang < cfg.viewHalfAngleDeg || (glance && ang < cfg.glanceHalfAngleDeg);
        }
        bool Knows(Entry e, Entry o) => e.knowT.TryGetValue(Ix(o), out var t) && raceTime - t < cfg.awareMemorySec;

        void Traffic(Entry e, float dt, float kartLen, float room, bool packed, bool launch)
        {
            var ai = e.ai;
            float myD = ai.d, line = ai.LineOffsetHere, personal = ai.cfg.lateralOffset;
            float goalAbs = line + personal;                 // where this driver wants to be (absolute d)
            float v = Mathf.Max(0.5f, ai.kart.ForwardSpeed);
            float look = Mathf.Max(kartLen + 2f, v * cfg.lookAheadTime + kartLen + 0.3f * v);
            e.attackCool = Mathf.Max(0f, e.attackCool - dt);
            var fr = w.track.Frame(ai.s);
            float bodyLen = ai.kart.setup.FootFront + ai.kart.setup.FootRear;
            // the first lap off the initial double-file start: the outside row races for it, not files in (Franklin)
            // Franklin's playtest #2: "a scramble, not a coherent zipper merge... drivers are trying to keep every inch of
            // track position they can hold, and then some more"; it takes a lap or two to filter out.
            int battleLaps = e.driver.aggression > 0.55f ? cfg.startBattleLapsMax : cfg.startBattleLapsMin;
            bool startBattle = phase == RacePhase.Green && e.lapsDone < battleLaps && greenTime == firstGreenTime && !e.finished;
            bool frustrated = e.peeks >= cfg.frustratedPeeks && raceTime - e.peekT < cfg.frustrationMemorySec;
            ai.allowBerm = false;
            float hiLim = ai.HighLimit(fr);
            float loLim = ai.LowLimit(fr);
            long ptAw = Perf.T(); UpdateAwareness(e, fr, dt); Perf.Add(12, ref ptAw);

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
                float lineThere = LineAt(o) + personal;
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
                // Overlapped and rubbing alongside (centres ~50 in apart) is side by side, not "in front of me": counting
                // it as ahead made the trailing kart check up hard while the other conceded -- both rode the brakes
                // (Franklin's playtest 11-16). Overlapped needs real lateral overlap to count.
                float latNeed = g < bodyLen ? room * 0.8f : room;
                if (Mathf.Abs(o.ai.d - myD) < latNeed && g < gapA) { gapA = g; ahead = o; }
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
            if (e.attackTarget >= 0 && (haz == null || Ix(haz) == e.attackTarget))
            {
                var tg = field[e.attackTarget];
                float gt = Gap(tg, e);
                e.attackT += dt;
                if (tg.outOfRace || tg.finished || Hazard(tg)) { e.attackTarget = -1; }
                else if (gt < -kartLen * 0.6f) { if (e.attackRun) dbgRunDone++; e.attackTarget = -1; e.attackCool = 0.9f; dbgCompleted++; e.peeks = 0; }     // cleared him: pass done
                else if (gt > cfg.passHoldGapFt * U.Ft + kartLen || (e.attackT > AbandonSec(e) && gt > kartLen * 0.3f))
                { FailedPeek(e); e.attackTarget = -1; e.attackCool = cfg.passCooldownSec; }                                         // didn't come off: fall back in line
                else if (e.attackSide > 0 && GrooveClimbing(ai)) { e.attackTarget = -1; e.attackCool = cfg.passCooldownSec; }
                else if (PeekHold(e, ai, fr, tg, room))
                {
                    // Down the straight: stay in his tracks on his bumper (bump draft) and pull out at entry, not before
                    // (Franklin's playtest 11-16: "karts peek to the inside down the straights very early").
                    attacking = true; e.attackT = Mathf.Max(0f, e.attackT - dt);
                    goalAbs = Mathf.Clamp(tg.ai.d, loLim, hiLim);
                    if (ahead == null && gt > 0f) { ahead = tg; gapA = gt; }
                }
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
                bool faster = ai.freeSpeed > ahead.ai.freeSpeed * cfg.fasterBy || heldUp || startBattle;
                bool latched = e.attackTarget == Ix(ahead);
                bool want = latched ? (gapA < startGap + 3f && (faster || e.attackT < 1.2f))
                                    : (gapA < startGap && faster);
                // exit run: off the corner, faster on pace, carrying more speed than him, within a run's distance
                // (pace compared like for like: free speed is read at each kart's own look-ahead point, and off a corner
                // the follower's point is still further back in the turn, so he always looked slower than the leader)
                bool run = !latched && ExitRun(ai) && Quicker(e, ahead, true)
                           && gapA < SettleGap(e, v, kartLen, true) + cfg.runRoomFt * U.Ft + 1f
                           && v > ahead.kart.ForwardSpeed + 0.2f;
                if (run) { want = true; dbgRuns++; }
                // Peeked at him two or three times and it didn't stick: lean on him instead (Franklin's playtest 11-16)
                if (want && !run && frustrated && e.peekTarget == Ix(ahead) && e.pushCool <= 0f) want = false;
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
                        if (side != 0) { e.attackTarget = Ix(ahead); e.attackT = 0f; e.attackSide = side; e.attackRun = run; if (run) dbgRunStarted++; }

                    }
                    if (e.attackTarget == Ix(ahead) && e.attackSide > 0 && GrooveClimbing(ai))
                    { e.attackTarget = -1; e.attackCool = cfg.passCooldownSec; } // call off an outside move as the exit opens to the wall
                    if (e.attackTarget == Ix(ahead))
                    {
                        attacking = true;
                        if (PeekHold(e, ai, fr, ahead, room)) goalAbs = Mathf.Clamp(ahead.ai.d, loLim, hiLim); // wait for the entry
                        else
                        {
                            e.attackT += dt;
                            goalAbs = ahead.ai.d + e.attackSide * room;
                            if (e.attackT > AbandonSec(e)) { FailedPeek(e); e.attackCool = cfg.passCooldownSec; e.attackTarget = -1; attacking = false; goalAbs = line + personal; }
                        }
                    }
                }
            }
            if (!attacking && e.attackTarget >= 0 && (ahead == null || e.attackTarget != Ix(ahead)))
            { e.attackTarget = -1; e.attackT = 0f; e.attackCool = Mathf.Max(e.attackCool, 0.9f); }

            // ---- start battle: alongside someone, hold my side of the pair -- don't lift and tuck in behind him
            if (startBattle && !attacking && !(ahead != null && Hazard(ahead)))
            {
                Entry beside = null; float bg = float.MaxValue;
                foreach (var o in field)
                {
                    if (o == e || o.outOfRace || Hazard(o)) continue;
                    float g = Gap(o, e);
                    if (Mathf.Abs(g) < bodyLen && Mathf.Abs(o.ai.d - myD) > room * 0.5f && Mathf.Abs(g) < bg) { bg = Mathf.Abs(g); beside = o; } // truly alongside, not nose to tail
                }
                if (beside != null)
                {
                    float half = room * 0.5f, lo = loLim + half, hi = hiLim - half;
                    float pc = lo < hi ? Mathf.Clamp(line, lo, hi) : line;
                    goalAbs = pc + (myD >= beside.ai.d ? half : -half);
                    dbgBattle++;
                }
            }

            // ---- defending (Franklin's playtest #3): no mirrors, but you feel a kart on your bumper or catch him in a glance.
            // Cover the inside at entry; once his nose is in, come down a touch to make him back out.
            int squeezeOn = -1; float squeezeD = 0f;
            if (!packed && !e.finished && !attacking && e.pushTarget < 0 && fr.corner < 0.6f && Aggr(e) > 0.2f)
            {
                int me = Ix(e);
                foreach (var o in field)
                {
                    if (o == e || o.outOfRace || Hazard(o) || o.attackTarget != me || o.attackSide >= 0) continue;
                    if (!Knows(e, o)) continue;                       // can't defend against a kart you don't know is there
                    float g = Gap(o, e); // + = he's ahead of me
                    if (g < -kartLen * 1.6f || g >= 0f) continue;   // too far back, or level/ahead: never chop him
                    float a = Aggr(e);
                    if (g < -bodyLen * 0.9f && EntryAhead(ai))
                    {   // on my bumper, not yet overlapped: cover the inside, leaving him a kart-width above the edge
                        goalAbs = Mathf.Max(loLim + room + 0.3f, Mathf.Min(goalAbs, line - cfg.defendCoverIn * U.In * a));
                        dbgCover++;
                    }
                    else if (o.ai.d < myD && a > 0.5f && o.ai.d > loLim + 0.3f)
                    {   // his nose is in on my inside: lean down a few inches into him
                        squeezeOn = Ix(o); squeezeD = o.ai.d + room - cfg.defendSqueezeIn * U.In * (a - 0.5f) * 2f;
                        goalAbs = Mathf.Min(goalAbs, squeezeD);
                        dbgSqueeze++;
                    }
                    e.defendedAgainst = Ix(o); e.defendedT = raceTime;
                    break;
                }
            }

            // ---- occupancy: never steer into a slot a kart alongside holds; keep my side of it.
            // Applied twice: to the traffic goal, and as hard lane limits on the steering target
            // (the target sits down the road where the groove may already have moved — e.g. diving
            // to the apex — so a limit at my current position alone let karts steer into each other).
            float latMin = float.NegativeInfinity, latMax = float.PositiveInfinity;
            float hardMin = float.NegativeInfinity, hardMax = float.PositiveInfinity;
            foreach (var o in field)
            {
                if (o == e || o.cleared) continue;
                float g = Gap(o, e);
                if (Mathf.Abs(g) > kartLen) continue;
                if (!Hazard(o) && !Knows(e, o)) continue; // not in my view and not felt: he isn't there, in my mind (Franklin)
                float od = o.ai.d;
                if (myD >= od) latMin = Mathf.Max(latMin, od + room); else latMax = Mathf.Min(latMax, od - room);
                if (myD >= od) hardMin = Mathf.Max(hardMin, od + room * cfg.rubFrac); else hardMax = Mathf.Min(hardMax, od - room * cfg.rubFrac);
                // Hold my line; never climb (or dive) to make room for him (Franklin, 11-16: "They'll rub up against each
                // other ten times before they move apart on purpose once"). Just don't steer further into him.
                if (Mathf.Abs(goalAbs - od) < room)
                    goalAbs = myD >= od ? Mathf.Max(goalAbs, Mathf.Min(myD, od + room)) : Mathf.Min(goalAbs, Mathf.Max(myD, od - room));
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
                if (squeezer != null) { e.concedeT = cfg.concedeHoldSec; e.concedeTo = Ix(squeezer); }
                if (e.attackTarget >= 0) { e.attackTarget = -1; e.attackCool = cfg.passCooldownSec; goalAbs = Mathf.Min(goalAbs, hiLim); }
            }
            if (squeezeOn >= 0) latMin = Mathf.Min(latMin, squeezeD);
            // If I'm already inside a limit (side by side and tight), don't yank me across: hold where I am.
            if (myD < latMin) latMin = myD;
            if (myD > latMax) latMax = myD;
            ai.latMin = latMin; ai.latMax = latMax;
            ai.latHardMin = Mathf.Min(hardMin, myD); ai.latHardMax = Mathf.Max(hardMax, myD);
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
                if (o == e || o.pushTarget != Ix(e)) continue;
                float g = Gap(e, o); // how far I am ahead of the pusher
                var sp = e.kart.setup; // only once his nose is actually on my bumper (within ~6 in)
                if (g > 0f && g < (sp.FootFront + sp.FootRear) + 0.15f) e.pushedT = cfg.pushedSeconds;
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
            if (!packed && !e.finished && !startBattle)
            {
                float entryCorner = w.track.Frame(w.track.LineS(w.track.LineAdvance(w.track.NearestLineIndex(new Vector2(ai.transform.position.x, ai.transform.position.z), -1), v * ai.cfg.speedLookaheadTime))).corner;
                foreach (var o in field)
                {
                    if (o == e || o.outOfRace || Hazard(o)) continue;
                    float g = Gap(o, e);
                    // Franklin q29: far enough alongside that you'd have to concede -> hold it if the groove is wide enough
                    // for both of you; if you're out of it (in the loose), lift slightly and file in behind.
                    bool alongsideInside = o.ai.d < myD - 0.5f && g > -cfg.concedeNoseFt * U.Ft && g < kartLen && Knows(e, o)
                                           && (!cfg.concedeOnlyInLoose || w.track.GrooveGrip(fr, myD) < 0.97f);
                    if (alongsideInside && entryCorner > cfg.concedeEntryCorner && e.attackTarget != Ix(o))
                    { e.concedeT = cfg.concedeHoldSec; e.concedeTo = Ix(o); break; }
                }
                if (e.concedeT > 0f && e.concedeTo >= 0)
                {
                    var o = field[e.concedeTo];
                    if (!o.outOfRace && Gap(o, e) > -kartLen)
                        ai.speedCap = Mathf.Min(ai.speedCap, Mathf.Max(0f, o.kart.ForwardSpeed - cfg.concedeSpeedDropMph * U.Mph / Urgency(e)));
                    else e.concedeT = 0f;
                }
            }

            // ---- the push: nose on his bumper, a touch faster than him; when he drifts up and the bottom opens, dive
            e.pushCool = Mathf.Max(0f, e.pushCool - dt);
            if (!packed && !e.finished && ahead != null && !Hazard(ahead) && e.pushTarget < 0 && e.attackTarget < 0 && e.pushCool <= 0f
                && (NightPace(e) > NightPace(ahead) * 1.001f || (frustrated && e.peekTarget == Ix(ahead)))
                && gapA < SettleGap(e, v, kartLen, false) + 3f && EntryAhead(ai)
                && rng.NextDouble() < (frustrated ? Mathf.Max(0.3f, Aggr(e)) * cfg.frustratedPushRate : Aggr(e)) * dt * cfg.pushRate * Urgency(e))
            { e.pushTarget = Ix(ahead); e.pushT = 0f; dbgPushes++;
              e.pushBig = rng.NextDouble() < cfg.bigPushChance * Aggr(e) * Urgency(e) * (frustrated ? 2f : 1f); if (e.pushBig) dbgBigPushes++;
              if (traceAtk < 0 && diveTraces < 0) { traceAtk = Ix(e); traceTgt = e.pushTarget; traceT0 = raceTime; traceAcc = 0f; diveTraces++; events.Add($"DIVE TRACE {diveTraces} (push): #{e.driver.number} on #{ahead.driver.number}"); } }
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
                        if (traceAtk < 0 && diveTraces < 0) { traceAtk = Ix(e); traceTgt = e.attackTarget; traceT0 = raceTime; traceAcc = 0f; diveTraces++; events.Add($"DIVE TRACE {diveTraces}: #{e.driver.number} under #{field[traceTgt].driver.number}"); }
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
                        { var stp = ai.kart.setup; float bl = (stp.FootFront + stp.FootRear);
                          ai.speedCap = Mathf.Min(ai.speedCap, pt.kart.ForwardSpeed + Mathf.Max(e.pushBig ? cfg.bigPushDelta : cfg.pushSpeedDelta, 0.8f * (gp - bl))); }
                    }
                }
            }

            // attacking drivers drive closer to the limit (not when dodging a wreck)
            if ((pushing || (attacking && e.attackTarget >= 0)) && haz == null) ai.attackBoost = cfg.attackBoostMax * Aggr(e);
            if (e.mad && phase == RacePhase.Green) ai.attackBoost = Mathf.Max(ai.attackBoost, cfg.madCornerCut); // driving it in harder
            // ---- check-up: hold station behind a kart in my slot until the lane is clear (time headway too)
            if (ahead != null && !(pushing && Ix(ahead) == e.pushTarget))
            {
                float va = ahead.kart.ForwardSpeed;
                // Packed start: hold the grid spacing (nose to tail). Otherwise: follow gap + time headway —
                // shortened to striking distance when I'm faster on pace and want by.
                bool wantBy = Quicker(e, ahead, true) && !Hazard(ahead);
                float settle = SettleGap(e, v, kartLen, wantBy);
                // (was: hold runRoomFt of extra room mid-corner to "time the run". With every Clone at the same top speed
                // that parked a faster kart ~12 ft back for the whole race — a train car, never a threat. Soak 2026-11-15:
                // P2 sat 0.28-0.33 s behind P1 for 15 laps. A faster kart closes to the bumper in the corner instead.)
                // Bumper drafting (Franklin, 2026-11-15): "literally up against the bumper of other karts, pushing them
                // down the straight", then lift slightly on entry so as not to spin him into the corner. On the
                // straight a racer (faster, or aggressive enough) closes to bumper contact and leans on him; at
                // entry the normal settle gap comes back — that's the lift.
                var st = ai.kart.setup;
                float bumperLen = (st.FootFront + st.FootRear); // centre to centre at bumper contact
                bool straightNow = fr.corner < 0.15f && !EntryAhead(ai);
                bool bumpDraft = !packed && !e.finished && straightNow && !Hazard(ahead)
                                 && (wantBy || Aggr(e) > cfg.bumpDraftAggression);
                if (bumpDraft) { settle = bumperLen + 0.02f; dbgBumpDraft++; }
                // the accordion (Franklin): off the green everyone's on the gas and closes right up on the kart ahead
                bool accordion = launch && !Hazard(ahead);
                // nose on his bumper at the green: flat out, shoving him (Franklin: the trailing karts are in the throttle
                // before the ones in front) -- no check-up at all, the contact does the rest
                // (measured kart to kart: the gap along the centre line reads ~1.3 ft long in the lanes -- only row 2 ever counted)
                bool shove = accordion && Vector3.Distance(ahead.kart.transform.position, ai.kart.transform.position) < bumperLen + 0.3f;
                if (accordion) settle = Mathf.Min(settle, bumperLen + 0.3f);
                float cap = va + cfg.followGain * (gapA - settle) + (bumpDraft ? cfg.bumpLeanMph * U.Mph : 0f) + (accordion ? cfg.launchLeadMph * U.Mph : 0f);
                // Never demand far below the kart ahead's speed unless contact is imminent — a check-up eases
                // back to his pace; it doesn't collapse (a hard cap cascaded the whole field to a stop).
                // "Imminent" = within a few inches of his bumper (was 1.6 ft of air, which kept everyone off each other).
                float touch = bumperLen + (bumpDraft ? -0.05f : 0.1f);
                cap = gapA > touch ? Mathf.Max(cap, va * 0.85f) : Mathf.Min(cap, va * 0.7f);
                if (!shove) ai.speedCap = Mathf.Min(ai.speedCap, Mathf.Max(0f, cap));
            }
            // (the old start-pack chain cap -- nobody out-accelerates the kart ahead for 4 s -- is gone: Franklin, 11-16,
            // "they can in real life! That's where that accordion push comes from." It strung the field out.)
        }

        float AbandonSec(Entry e) => (e.attackRun ? cfg.runAbandonSec : cfg.passAbandonSec) * (1f + 0.5f * (Urgency(e) - 1f));

        /// <summary>1 at the start of the feature, 1 + lateRaceUrgency on the last lap.</summary>
        float Urgency(Entry e) => 1f + cfg.lateRaceUrgency * Mathf.Clamp01(e.lapsDone / (float)Mathf.Max(1, cfg.featureLaps - 1));

        /// <summary>An inside move that didn't stick: count it against that kart (frustration builds toward the push).</summary>
        void FailedPeek(Entry e)
        {
            if (e.attackSide >= 0 || e.attackTarget < 0) return;
            if (e.peekTarget == e.attackTarget && raceTime - e.peekT < cfg.frustrationMemorySec) e.peeks++; else e.peeks = 1;
            // he blocked me (covered or leaned on me): that annoys -- some drivers give a blocker a push on the next entry
            var blk = field[e.attackTarget];
            if (blk.defendedAgainst == Ix(e) && raceTime - blk.defendedT < 3f) { e.peeks++; dbgAnnoyed++; }
            e.peekTarget = e.attackTarget; e.peekT = raceTime; dbgPeeksFailed++;
        }

        /// <summary>Latched on an inside move but still down the straight and still in his tracks: hold on his bumper
        /// until the corner is close, then pull out. Once pulled out, keep going.</summary>
        bool PeekHold(Entry e, KartAI ai, TrackFrame fr, Entry tg, float room)
        {
            if (e.attackSide >= 0 || Mathf.Abs(ai.d - tg.ai.d) > room * 0.5f) return false;
            if (fr.corner > 0.15f) return false;
            float v = Mathf.Max(5f, ai.kart.ForwardSpeed);
            float c1 = w.track.Frame(w.track.WrapS(ai.s + v * cfg.peekLeadSec / w.track.centerLength * w.track.N)).corner;
            return c1 <= cfg.pushEntryCorner;
        }

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
            memoTick++;
            var lead = Leader();
            if (phase == RacePhase.Green)
            {
                snapT += dt;
                if (snapT >= 0.5f)
                {
                    snapT = 0f; var ro = RacingOrder(); orderSnaps.Enqueue((raceTime, ro)); while (orderSnaps.Count > 8) orderSnaps.Dequeue();
                    // lead changes (scorecard): a new leader clear of the old one by a metre
                    if (ro.Count > 0)
                    {
                        if (lastLeader == null || lastLeader.outOfRace) lastLeader = ro[0];
                        else if (ro[0] != lastLeader && Gap(ro[0], lastLeader) > 1f) { leadChanges++; lastLeader = ro[0]; }
                    }
                }
                // a lone spinner still sitting there when traffic arrives -> caution after all
                foreach (var e in field)
                    if (cfg.cautionsOn && e.recovering && !e.outOfRace && PackNear(e)) { ThrowCaution($"{Tag(e)} still spun with traffic arriving"); SendToTail(e, "spun"); break; }
            }
            else if (phase == RacePhase.Caution) CautionStep(dt);
            // the tower won't start it with karts still coming out of the staging grid: another lap
            if (phase == RacePhase.Pace && lead != null && staged && lead.ai.raceDist > GoLine - 0.4f * L && AnyStaging())
            {
                towerLaps++;
                Event($"STAGING: tower holds the start — still karts coming out of the grid. Another pace lap ({towerLaps} extra).");
            }
            else if (phase == RacePhase.Pace && lead != null && lead.ai.raceDist > GoLine - 0.2f * L && formHolds < cfg.maxHoldLaps
                     && Straggler(out float back) is Entry lagger)
            {
                towerLaps++; formHolds++;
                Event($"TOWER: field isn't lined up -- {Tag(lagger)} is {back / U.Ft:F0} ft off his spot. Another pace lap ({towerLaps} extra).");
            }
            if (phase == RacePhase.Pace && lead != null && lead.ai.raceDist >= GoLine)
            {
                phase = RacePhase.Green; phaseTime = 0; greenTime = raceTime; firstGreenTime = raceTime;
                if (Lanes == 2)
                {
                    var sb = new System.Text.StringBuilder("GRID AT GREEN — outside kart vs its inside partner (ft, + = behind):");
                    foreach (var o in field) if (o.gridPos % 2 == 1) { var p = field.Find(x => x.gridPos == o.gridPos - 1); if (p != null) sb.Append($" row{o.gridPos / 2 + 1} {(p.ai.raceDist - o.ai.raceDist) / U.Ft:+0.0;-0.0}"); }
                    Event(sb.ToString());
                }
                {   // spec §12.3 packed start: consecutive gaps at the green, in laps (browser benchmark ≲ 0.02)
                    var ds = new List<float>(); foreach (var e in field) if (!e.outOfRace) ds.Add(e.ai.raceDist);
                    ds.Sort(); ds.Reverse();
                    greenMaxGapLaps = 0f; for (int i = 1; i < ds.Count; i++) greenMaxGapLaps = Mathf.Max(greenMaxGapLaps, (ds[i - 1] - ds[i]) / L);
                    greenSpreadLaps = ds.Count > 1 ? (ds[0] - ds[ds.Count - 1]) / L : 0f;
                }
                Event($"GREEN FLAG — {Tag(lead)} leads them to the line.");
                foreach (var e in field) { e.lastCross = -1f; e.preLine = true; e.ai.SnapTrafficOffsetToCurrent(); }
            }

            if (firstGreenTime >= 0f && startAlongside.Count < StartSampleSec.Length && raceTime - firstGreenTime >= StartSampleSec[startAlongside.Count])
            {
                int n = 0; float hi = float.MinValue, lo = float.MaxValue;
                foreach (var e in field)
                {
                    if (e.outOfRace) continue;
                    hi = Mathf.Max(hi, e.ai.raceDist); lo = Mathf.Min(lo, e.ai.raceDist);
                    float bl = e.kart.setup.FootFront + e.kart.setup.FootRear;
                    foreach (var o in field)
                        if (o != e && !o.outOfRace && Mathf.Abs(Gap(o, e)) < bl && Mathf.Abs(o.ai.d - e.ai.d) > cfg.sideRoomIn * U.In * 0.5f) { n++; break; }
                }
                startAlongside.Add(n); startSpreadFt.Add((hi - lo) / U.Ft);
            }
            foreach (var e in field)
            {
                var ai = e.ai;
                if (e.outOfRace && !e.finished) CrewClear(e, dt);
                if (e.crewHelp) { CrewAssist(e, dt); continue; }
                if (ai.offTrackSteps > 0 && !e.offLogged && !e.outOfRace)
                {
                    e.offLogged = true; var fo = w.track.Frame(ai.s);
                    Event($"OFF TRACK: {Tag(e)} u={ai.s / w.track.N:F3} d={ai.d:F2} m (edge {fo.halfWidth:F2}) v={e.kart.ForwardSpeed / U.Mph:F0} mph recovering={e.recovering} toTail={e.toTail} phase={phase} crewHelped={e.crewHelped}");
                    if (phase == RacePhase.Caution && !e.recovering) foreach (var h in e.hist) events.Add("      before: " + h);
                }
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
                if (phase == RacePhase.Green || phase == RacePhase.Checkered || phase == RacePhase.Caution)
                {
                    if (e.nextLine <= 0f) e.nextLine = GreenLine;
                    while (ai.raceDist >= e.nextLine)
                    {
                        if (phase == RacePhase.Caution) { e.nextLine += L; continue; } // caution laps don't count (Franklin)
                        if (e.lastCross >= 0f) { e.laps.Add(raceTime - e.lastCross); e.lapClean.Add(e.cleanThisLap); e.lapsDone++; }
                        e.lastCross = raceTime; e.cleanThisLap = true; e.nextLine += L; e.preLine = false;
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
                if ((phase == RacePhase.Green || phase == RacePhase.Caution) && !e.recovering && !e.inIncident && !e.crewHelp)
                {
                    e.histT += dt;
                    if (e.histT >= 0.1f)
                    {
                        e.histT = 0f;
                        var fh = w.track.Frame(ai.s); var k = e.kart;
                        e.hist.Enqueue($"u{ai.s / w.track.N:F2} v{k.ForwardSpeed / U.Mph:F0} tgt{ai.targetSpeed / U.Mph:F0} acc{k.accelCmd:F1} st{k.steerDeg:F0} dOff{ai.d - fh.LineOffset:F1} tOff{ai.TrafficOffset:F1} lat[{(float.IsInfinity(ai.latMin) ? "-" : ai.latMin.ToString("F1"))},{(float.IsInfinity(ai.latMax) ? "-" : ai.latMax.ToString("F1"))}] {(e.attackTarget >= 0 ? "ATK " : "")}{(e.concedeT > 0 ? "CONC " : "")}{(e.bobbleT > 0 ? "BOB " : "")}slide{(k.sliding[2] ? 1 : 0)}{(k.sliding[3] ? 1 : 0)} dust{k.dust[3]:F1} grip{k.surfaceGripNow[3]:F2} " +
                            $"roll{Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(k.transform.up, k.transform.forward), k.transform.forward):F0} rollRate{Vector3.Dot(k.Body.angularVelocity, k.transform.forward):F1} yawRate{Vector3.Dot(k.Body.angularVelocity, Vector3.up):F1} gnd{(k.grounded[0] ? 1 : 0)}{(k.grounded[1] ? 1 : 0)}{(k.grounded[2] ? 1 : 0)}{(k.grounded[3] ? 1 : 0)} load{k.load[0]:F0}/{k.load[1]:F0}/{k.load[2]:F0}/{k.load[3]:F0} slipAng{Vector3.SignedAngle(k.transform.forward, Vector3.ProjectOnPlane(k.Body.linearVelocity, k.transform.up), k.transform.up):F0} " +
                            $"inEdge{(ai.d + fh.halfWidth - k.setup.FootLeft) / U.In:F0}in aim{(ai.lastAimD - ai.lastAimLow) / U.In:F0}in d{ai.d:F2} tc[{(e.kc != null ? e.kc.lastTrackContact : "")}]");
                        while (e.hist.Count > 12) e.hist.Dequeue();
                    }
                }

                // ---- incident stub (logged, not judged — M3)
                var f = w.track.Frame(ai.s);
                float yaw = Vector3.Angle(new Vector3(f.tan.x, 0, f.tan.y), Vector3.ProjectOnPlane(e.kart.transform.forward, Vector3.up));
                bool spun = yaw > cfg.spinAngleDeg && ai.stagePath == null; // a kart driving out of the staging grid faces the gate, not the track
                // over = on its side or its back; flipped only once it has come to rest there (mid-roll it may yet go on
                // over, or fall back onto its wheels -- the physics decides, nobody grabs it)
                var kb = e.kart.Body;
                bool over = e.kart.transform.up.y < 0.5f;
                bool still = kb.isKinematic || (kb.linearVelocity.magnitude < 1f && kb.angularVelocity.magnitude < 1f);
                e.overT = !over ? 0f : still ? e.overT + dt : 0f;
                bool flipped = over && (e.overT >= cfg.flipSettleSec || kb.isKinematic);
                if (over && !flipped) { spun = false; e.stoppedT = 0f; } // still tumbling: not a spin, not stopped
                bool stopped = (phase == RacePhase.Green || phase == RacePhase.Caution) && e.kart.ForwardSpeed < 2f * U.Mph && !e.finished && !e.fsHeld && fsStage == 0;
                e.stoppedT = stopped ? e.stoppedT + dt : 0f;
                bool stuck = (e.stoppedT > cfg.stoppedSeconds && !e.blocked && phase == RacePhase.Green)
                             // under caution a stop is normally just the queue; but a kart that's supposed to be rolling
                             // (its cap allows it) and hasn't moved for 5 s is stuck (e.g. nosed into the wall after a hit)
                             || (phase == RacePhase.Caution && e.stoppedT > 5f && ai.speedCap > 3f * U.Mph); // stopped behind a kart = waiting in a jam; under caution, stopped in the queue is not a crash
                bool incident = spun || flipped || stuck;
                bool wasRecovering = e.recovering;
                bool newEpisode = incident && !e.inIncident && !e.recovering;
                // Spin recovery: a spun or stuck kart (not flipped) drives itself out; it's done when it's
                // pointed the right way and rolling. Can't get going in recoverMaxSeconds -> out.
                bool startRecover = !flipped && (spun || stuck) && (phase == RacePhase.Green || phase == RacePhase.Caution) && !e.finished && !e.recovering;
                if (startRecover) { e.recovering = true; e.recoverT = 0f; }
                if (e.recovering)
                {
                    e.recoverT += dt;
                    if (!spun && e.kart.ForwardSpeed > 3f * U.Mph) { e.recovering = false; Event($"RECOVERED: {Tag(e)} rejoins."); }
                    else if (e.recoverT > (InBermCorner(e) ? cfg.bermSelfRotateAfterSec : cfg.recoverMaxSeconds))
                    {
                        e.recovering = false;
                        // Franklin (Pit Board 2026-11-15): "Infield track workers typically help get any karts separated.
                        // Drivers will often hop out of their own kart to move when stuck. The engines have attached
                        // pull-starts too." A kart that's upright but can't get going gets separated and pull-started
                        // once and rejoins at the tail; a second time (or upside down) it's out.
                        if (!e.crewHelped && !flipped && InBermCorner(e))
                        {
                            e.crewHelp = true; e.crewHelped = true; e.assistStarted = false; e.assistWait = 0f; e.crewHelpT = raceTime;
                            e.worker = 2; e.workerArrives = raceTime + cfg.driverHopOutSec;
                            Event($"CREW: {Tag(e)}'s driver hops out, picks up the nose and turns it round himself.");
                        }
                        else if (!e.crewHelped && !flipped)
                        {
                            e.crewHelp = true; e.crewHelped = true; e.assistStarted = false; e.assistWait = 0f; e.worker = -1; e.crewHelpT = raceTime;
                            Event($"CREW: track workers separate {Tag(e)} and pull-start it.");
                            if (phase == RacePhase.Caution && !e.toTail) SendToTail(e, "crew had to get it going");
                        }
                        else { e.outOfRace = true; e.outReason = "couldn't restart"; Event($"OUT: {Tag(e)} (couldn't get going)."); }
                    }
                }
                if (newEpisode || (flipped && !e.inIncident))
                {
                    if (flipped) e.flips++; else e.spins++;
                    var kc = e.kc;
                    bool contact = kc != null && w.simTime - kc.lastContactTime < 1.5f;
                    if (contact) { if (flipped) contactFlips++; else contactSpins++; } else { if (flipped) selfFlips++; else selfSpins++; }
                    string who = contact && kc.lastOther != null ? $" after contact with #{field.Find(x => x.kart == kc.lastOther)?.driver.number} ({kc.lastImpulse:F0} N·s, {w.simTime - kc.lastContactTime:F1} s before)" : " (no kart contact — self)";
                    Event($"{(flipped ? "FLIP" : spun ? "SPIN" : "STOPPED")}: {Tag(e)} at lap {e.lapsDone + 1}, u={ai.s / w.track.N:F2}, {e.kart.ForwardSpeed / U.Mph:F0} mph{who}{(wasRecovering ? " [while recovering]" : "")}.");
                    if (flipped && !e.finished && !e.crewHelped && Hash01(e.driver.number * 9176 + e.flips * 131 + Mathf.RoundToInt(raceTime * 10f)) < cfg.flipRejoinChance)
                    {
                        // the lucky 20%: back on its wheels, looked over, pull-started, to the tail
                        e.flipRejoin = true; e.recovering = false;
                        e.crewHelp = true; e.crewHelped = true; e.assistStarted = false; e.assistWait = 0f; e.worker = -1; e.crewHelpT = raceTime;
                        Event($"CREW: {Tag(e)} is flipped back over and looked over -- nothing bent. Rejoins at the tail.");
                    }
                    if ((!contact || flipped) && (!wasRecovering || flipped)) foreach (var h in e.hist) events.Add("      before: " + h);
                    Entry other = contact && kc.lastOther != null ? field.Find(x => x.kart == kc.lastOther) : null;
                    JudgeIncident(e, other, flipped, stuck, spun);
                }
                e.inIncident = incident;
                if (!e.finished && !e.recovering && !e.outOfRace && ((flipped && !e.flipRejoin) || (e.stoppedT > cfg.dnfStoppedSeconds && !e.blocked && phase == RacePhase.Green)))
                {
                    e.outOfRace = true; e.outReason = flipped ? "flipped -- bent chassis" : "stopped";
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

        /// <summary>Stop (false) or restore (true) this kart's collisions with the other karts still racing.
        /// Pairs with karts the crew has taken off are left ignored.</summary>
        void SetKartCollisions(Entry e, bool on)
        {
            foreach (var c in e.kart.GetComponentsInChildren<Collider>())
                foreach (var o in field)
                    if (o != e && o.kart != null && !(on && (o.cleared || o.beingCleared || o.crewHelp)))
                        foreach (var oc in o.kart.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, oc, !on);
        }

        /// <summary>
        /// Track workers get a stuck kart going (Franklin): walk it clear of the tangle to the inside edge at walking
        /// pace, straighten it up the track, pull-start it, and let it go once nobody is right beside it. Same
        /// sanctioned scripted move as crew clearing (spec §8.4): walking pace, kinematic, not touching other karts
        /// while it's moved. It then drives itself to the tail like any spun kart.
        /// </summary>
        void CrewAssist(Entry e, float dt)
        {
            var rb = e.kart.Body; var ai = e.ai;
            // backstop: a kart the workers can't get going in this long is done for the night -- push it to the infield
            // rather than hold the caution on it (soak 2026-11-16: a 6-minute caution, race 17).
            if (raceTime - e.crewHelpT > cfg.crewGiveUpSec)
            {
                e.crewHelp = false; e.outOfRace = true; e.outReason = "couldn't restart";
                if (e.worker >= 0 && e.worker < 2 && workerBusy[e.worker] == e) workerBusy[e.worker] = null;
                Event($"OUT: {Tag(e)} (workers couldn't get it going in {cfg.crewGiveUpSec:F0} s).");
                return;
            }
            if (!e.assistStarted)
            {
                // the worker for this end of the track takes the job when free, then walks out from the infield
                if (e.worker < 0)
                {
                    float u = ai.s / w.track.N;
                    int wk = Mathf.Abs(Mathf.DeltaAngle(u * 360f, cfg.workerPostU[0] * 360f)) <= Mathf.Abs(Mathf.DeltaAngle(u * 360f, cfg.workerPostU[1] * 360f)) ? 0 : 1;
                    if (workerBusy[wk] != null && workerBusy[wk] != e)
                    {
                        // Franklin: "Drivers will often hop out of their own kart to move when stuck." Kept waiting on a
                        // busy worker, the driver does it himself (same walking-pace move, no walk-out). A six-kart pile
                        // fed one at a time to one worker ran the caution past the time cap (soak 2026-11-16).
                        e.assistWait += dt;
                        if (e.assistWait < cfg.driverSelfHelpSec) return;
                        e.worker = 2; e.workerArrives = raceTime;
                        Event($"CREW: {Tag(e)}'s driver hops out and frees it himself (the worker is busy).");
                    }
                    else
                    {
                        workerBusy[wk] = e; e.worker = wk;
                        var fp = w.track.Frame(cfg.workerPostU[wk] * w.track.N);
                        var post = fp.pos - fp.outN * (fp.halfWidth + cfg.workerBackFt * U.Ft);
                        var fk = w.track.Frame(ai.s); var kp = fk.pos + fk.outN * ai.d;
                        e.workerArrives = raceTime + Vector2.Distance(post, kp) / (cfg.crewWalkMph * U.Mph);
                        Event($"CREW: the turns {(wk == 0 ? "1-2" : "3-4")} worker walks out to {Tag(e)} ({Vector2.Distance(post, kp) / U.Ft:F0} ft).");
                    }
                }
                if (raceTime < e.workerArrives) { e.assistWait += dt; return; }
                e.assistStarted = true; rb.isKinematic = true; SetKartCollisions(e, false); e.crewS = ai.s;
            }
            // Work from the crew's own track position. Using ai.s, the walk up the inside edge never got anywhere: the AI
            // re-read s from the body (not moved yet) every step, so two stuck karts 5 ft apart waited on each other until
            // one was retired (seed 9001 staging: a 140 s caution, found with the race recorder 2026-11-17).
            ai.s = e.crewS;
            var f = w.track.Frame(e.crewS);
            float targetD = ai.LowLimit(f) + 0.3f;
            float nd = Mathf.MoveTowards(ai.d, targetD, cfg.crewWalkMph * U.Mph * dt);
            // Set it down the way a kart sits on this patch of the (banked) dirt: level with the surface, at its static
            // ride height. Standing it level to the WORLD buried the low side in the bank, and every kart helped this
            // way flipped as it was let go (soak 2026-11-16).
            float g1 = w.track.SurfaceHeightSD(e.crewS, nd);
            float slope = (w.track.SurfaceHeightSD(e.crewS, nd + 0.5f) - w.track.SurfaceHeightSD(e.crewS, nd - 0.5f)) / 1.0f;
            var outN3 = new Vector3(f.outN.x, 0f, f.outN.y);
            var nrm = (Vector3.up - outN3 * slope).normalized;
            var fwd = Vector3.ProjectOnPlane(new Vector3(f.tan.x, 0f, f.tan.y), nrm).normalized;
            var sit = Quaternion.LookRotation(fwd, nrm);
            var p = f.pos + f.outN * nd;
            var ground = new Vector3(p.x, g1, p.y);
            rb.MovePosition(ground + nrm * 0.03f); // same seat as a kart placed on the grid (World.SpawnKartAt)
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, sit, cfg.crewTurnDegPerSec * dt));
            ai.d = nd;
            if (Mathf.Abs(nd - targetD) > 0.01f || Quaternion.Angle(rb.rotation, sit) > 1f) return;
            // pull-started: let it go once no kart is rolling by close alongside (the field queues up the track), and never
            // while it overlaps anything. A parked kart nearby doesn't hold it forever (soak: a 400 s caution waiting on one).
            e.assistWait += dt;
            foreach (var o in field)
            {
                if (o == e || o.cleared || o.beingCleared || o.outOfRace) continue;
                float dist = (o.kart.Body.position - rb.position).magnitude;
                bool rolling = o.kart.ForwardSpeed > 2f * U.Mph;
                if (dist < 1.6f && !rolling)
                {
                    float go = Gap(o, e);
                    if (go > 0.05f || (Mathf.Abs(go) <= 0.05f && Ix(o) < Ix(e))) return; // he's ahead: he goes first
                    // parked right beside another stuck kart (a pile): walk it on up the inside edge until it's clear,
                    // rather than wait on a kart that is itself waiting (the deadlock behind a 300 s caution)
                    float s2 = w.track.WrapS(e.crewS + cfg.crewWalkMph * U.Mph * dt / w.track.centerLength * w.track.N);
                    var f2 = w.track.Frame(s2);
                    float g2 = w.track.SurfaceHeightSD(s2, nd);
                    float sl2 = (w.track.SurfaceHeightSD(s2, nd + 0.5f) - w.track.SurfaceHeightSD(s2, nd - 0.5f)) / 1.0f;
                    var n2 = (Vector3.up - new Vector3(f2.outN.x, 0f, f2.outN.y) * sl2).normalized;
                    var p2 = f2.pos + f2.outN * nd;
                    rb.MovePosition(new Vector3(p2.x, g2, p2.y) + n2 * 0.03f);
                    rb.MoveRotation(Quaternion.LookRotation(Vector3.ProjectOnPlane(new Vector3(f2.tan.x, 0f, f2.tan.y), n2).normalized, n2));
                    ai.s = s2; e.crewS = s2;
                    return;
                }
                if (dist < 1.6f || (dist < 2.5f && (rolling || e.assistWait < 8f))) return;
            }
            rb.isKinematic = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            SetKartCollisions(e, true);
            e.crewHelp = false; e.flipRejoin = false; e.overT = 0f; e.stoppedT = 0f; e.inIncident = false; ai.SnapTrafficOffsetToCurrent();
            // drives off like any spun kart: recovery mode (pull away, then rejoin at the tail). As a plain stopped kart it
            // sat at the inside edge with a zero cap while the queue treated it as a hazard: a 300 s caution (soak 2026-11-16).
            e.recovering = true; e.recoverT = 0f;
            if (e.worker >= 0 && e.worker < 2 && workerBusy[e.worker] == e) workerBusy[e.worker] = null;
            Event($"CREW: {Tag(e)} pull-started and rolling{(e.assistWait > 0.5f ? $" ({e.assistWait:F0} s waiting on the worker / traffic)" : "")}.");
        }

        // ======================= CAUTIONS (spec 8.4; Franklin's rules logged 2026-11-15) =======================

        /// <summary>An incident happened to e (other = the kart it last touched, if any). Decide caution / who goes back.</summary>
        void JudgeIncident(Entry e, Entry other, bool flipped, bool stuck, bool spun)
        {
            if (e.finished) return;
            // causer: the kart behind in a rear-end contact (spec 8.3): "P4 drove in way too deep and wrecked P3"
            bool rearEnded = other != null && !other.outOfRace && Gap(e, other) > 0.3f;
            if (phase == RacePhase.Green)
            {
                var fr = w.track.Frame(e.ai.s);
                bool onTrack = e.ai.d > -fr.halfWidth;
                bool inPack = other != null || PackNear(e);
                string why = flipped ? "flip" : (stuck && onTrack) ? "kart stopped on the track" : inPack ? "spin in traffic" : cfg.cautionForLoneSpin ? "spin" : null;
                if (why == null) { Event($"GREEN STAYS OUT: {Tag(e)} spun alone with nobody close."); return; }
                if (!cfg.cautionsOn) { Event($"NO CAUTION (cautions off): {why}: {Tag(e)}"); return; }
                ThrowCaution($"{why}: {Tag(e)}");
            }
            if (phase != RacePhase.Caution) return;
            if (rearEnded && !other.toTail)
            {
                e.spotBack = true;
                SendToTail(other, $"ran into the back of {Tag(e)}; {Tag(e)} gets the spot back");
            }
            else if (!e.spotBack) SendToTail(e, flipped ? "flipped" : stuck ? "stopped in the crash" : "spun");
        }

        bool PackNear(Entry e)
        {
            foreach (var o in field)
            {
                if (o == e || o.outOfRace || o.finished) continue;
                float g = Gap(e, o); // + = o is behind me
                if (g > 0f && g < cfg.packBehindSec * Mathf.Max(10f, o.kart.ForwardSpeed)) return true;
                if (g < 0f && g > -15f) return true;
            }
            return false;
        }

        void ThrowCaution(string reason)
        {
            if (phase != RacePhase.Green) return;
            phase = RacePhase.Caution; phaseTime = 0; cautions++;
            cautionStart = raceTime; oneToGo = false; cautionDumped = false; cautionLeaderLap = -1; goDist = float.MaxValue;
            fsStage = 0; fsOrder.Clear(); orderFails = 0;
            // order from ~2 s before the incident (the spinner has already lost spots by now)
            List<Entry> snap = null;
            foreach (var sn in orderSnaps) if (raceTime - sn.t >= 2f) snap = sn.order;
            if (snap == null) snap = RacingOrder();
            cautionBase.Clear(); cautionBase.AddRange(snap);
            cautionTail.Clear();
            foreach (var e in field) { e.toTail = false; e.spotBack = false; e.attackTarget = -1; e.pushTarget = -1; e.concedeT = 0f; }
            Event($"CAUTION #{cautions}: {reason}. Field slows to pace and lines up single file.");
            RebuildQueue();
        }

        void SendToTail(Entry e, string why)
        {
            if (e.toTail || e.outOfRace) return;
            e.toTail = true; cautionTail.Add(e);
            Event($"TO THE TAIL: {Tag(e)} ({why}). Drives back there.");
            if (!e.mad && Hash01(e.driver.number * 7727 + cautions * 131) < e.driver.aggression)
            { e.mad = true; Event($"MAD: {Tag(e)} is hot about it -- he'll be driving it in harder from the back."); }
            RebuildQueue();
        }

        List<Entry> RacingOrder()
        {
            var l = field.FindAll(x => !x.outOfRace && !x.finished);
            l.Sort((a, b) => b.ai.raceDist.CompareTo(a.ai.raceDist));
            return l;
        }

        void RebuildQueue()
        {
            cautionQueue.Clear();
            foreach (var e in cautionBase) if (!e.outOfRace && !e.finished && !e.toTail) cautionQueue.Add(e);
            foreach (var e in field) if (!e.outOfRace && !e.finished && !cautionBase.Contains(e) && !e.toTail) cautionQueue.Add(e);
            foreach (var e in cautionTail) if (!e.outOfRace && !e.finished) cautionQueue.Add(e);
        }

        /// <summary>Distance from e forward to the kart ahead of it in the queue (laps folded: a kart a lap down lines up physically).</summary>
        float QueueGap(Entry pred, Entry e)
        {
            return Mathf.Repeat(pred.ai.raceDist - e.ai.raceDist + 0.25f * L, L) - 0.25f * L;
        }

        /// <summary>Under caution: single file behind the leader at pace speed; catch up at up to pace + 5 mph; a kart
        /// that's ahead of its spot runs low and slow and lets them by. Positions change only by driving (spec 8.4).</summary>
        void CautionDrive(Entry e, float paceS, float paceC)
        {
            var ai = e.ai; ai.recovering = e.recovering;
            if (e.recovering) { ai.formation = false; ai.paceMode = false; ai.rejoinOnGroove = true; return; }
            ai.formation = true; ai.paceMode = true; ai.trafficGoal = 0f;
            if (fsStage != 0 && fsOrder.Contains(e)) { FieldStopDrive(e, paceS, paceC); return; }
            // (at one to go too, until the push-up: Franklin 11-18, the back half wasn't caught up when the green came)
            if (cautionGapsOpen && cautionQueue.Count > 0 && cautionQueue[0] == e && !PushingUp(e))
            {
                paceS = Mathf.Min(paceS, cfg.cautionLeaderSlowMph * U.Mph); paceC = Mathf.Min(paceC, cfg.cautionLeaderSlowMph * U.Mph);
                e.ai.paceStraight = Mathf.Min(e.ai.paceStraight, paceS); e.ai.paceCorner = Mathf.Min(e.ai.paceCorner, paceC);
            }
            float lane = cfg.cautionLaneFrac;
            Entry pred = null;
            var st = ai.kart.setup; float bl = (st.FootFront + st.FootRear);
            if (e.dropping)
            {
                // Out of place (a kart that belongs ahead of me is physically behind me): drop low at a fixed slow speed and
                // let them by. Fixed, not tied to anyone's speed — tying speeds through the queue order deadlocked.
                lane = cfg.dropBackLaneFrac;
                ai.speedCap = e.dropCrawl ? cfg.crawlMph * 1.5f * U.Mph : paceC * 0.6f;
            }
            else
            {
                // Follow the kart physically ahead in the line (not my queue predecessor, wherever he is): no speed ever
                // depends on a kart behind me, so nothing can wait on itself.
                // Only karts that belong AHEAD of me in the queue (else the leader chased the tail a lap ahead, the tail
                // chased the next kart, and the whole loop ran pace + 5 with the gaps never closing).
                float best = 0.95f * L; int myIdx = cautionQueue.IndexOf(e);
                for (int qi = 0; qi < myIdx; qi++)
                {
                    var o = cautionQueue[qi];
                    if (o.dropping || o.recovering || Hazard(o)) continue;
                    float g = Mathf.Repeat(o.ai.raceDist - e.ai.raceDist, L); // forward, all the way round
                    if (g > 0.05f && g < best) { best = g; pred = o; }
                }
            }
            if (pred != null)
            {
                float gap = Mathf.Repeat(pred.ai.raceDist - e.ai.raceDist, L);
                float want = bl + (PushingUp(e) ? cfg.restartBunchGapFt : e.paceGapFt) * U.Ft;
                {
                    float cap = pred.kart.ForwardSpeed + cfg.followGain * (gap - want);
                    float catchUp = Mathf.Clamp(cap - paceS, 0f, cfg.cautionCatchUpMph * U.Mph);
                    ai.paceStraight = paceS + catchUp; ai.paceCorner = paceC + catchUp;
                    ai.speedCap = Mathf.Max(0f, Mathf.Min(cap, BrakeCap(pred.kart.ForwardSpeed, gap - want)));
                }
            }
            // never run into whatever is physically right in front of me in my lane, whatever its place in the order
            // (soak 2026-11-16, race 3: karts headed to the tail got rear-ended at pace under the yellow, 250-420 N.s)
            {
                float myD = ai.d;
                foreach (var o in field)
                {
                    if (o == e || o.cleared || o.beingCleared) continue;
                    float g = Gap(o, e);
                    if (g <= 0f || g > 10f || Mathf.Abs(o.ai.d - myD) > 1.2f) continue;
                    float c = Mathf.Max(0f, Mathf.Min(o.kart.ForwardSpeed + cfg.followGain * (g - bl - 0.15f), BrakeCap(o.kart.ForwardSpeed, g - bl - 0.15f)));
                    // a wreck or a recovering kart right in my path: the go-round below steers me off his line, but until
                    // I'm clear I don't close on him faster than a walking-pace nudge (Franklin's playtest 11-17: a kart
                    // ran straight into a slow recovering kart under caution)
                    if (Hazard(o)) c = Mathf.Max(c, Mathf.Min(cfg.crawlMph * U.Mph, o.kart.ForwardSpeed + cfg.crawlMph * U.Mph * 0.5f));
                    if (c < ai.speedCap) ai.speedCap = c;
                }
            }
            // a kart dropping back ahead of me or beside me: the line files by underneath him
            if (!e.dropping)
                foreach (var o in field)
                {
                    if (o == e || !o.dropping || o.cleared || Hazard(o)) continue;
                    float g = Gap(o, e);
                    if (g > -bl && g < 25f) { lane = cfg.passDropperLaneFrac; break; }
                }
            // a wreck or recovering kart ahead in my lane: go round it, slowly
            float myLaneD = lane == cfg.cautionLaneFrac ? w.track.Frame(ai.s).LineOffset : lane * w.track.Frame(ai.s).halfWidth; // single file runs the groove
            foreach (var o in field)
            {
                if (o == e || o.cleared || !Hazard(o)) continue;
                float g = Gap(o, e);
                if (g > 0f && g < 20f && Mathf.Abs(o.ai.d - myLaneD) < 1.6f)
                {
                    lane = o.ai.d > 0f ? -0.6f : 0.6f;
                    ai.speedCap = Mathf.Min(ai.speedCap, 10f * U.Mph);
                }
            }
            {   // still crossed up after a spin: crawl until it's pointed down the track
                var fy = w.track.Frame(ai.s);
                float yawOff = Vector3.Angle(new Vector3(fy.tan.x, 0, fy.tan.y), Vector3.ProjectOnPlane(e.kart.transform.forward, Vector3.up));
                if (yawOff > cfg.cautionStraightenDeg) ai.speedCap = Mathf.Min(ai.speedCap, ai.recoverySpeed);
            }
            ai.laneFrac = lane;
            ai.grooveLane = lane == cfg.cautionLaneFrac ? 0 : KartAI.NoGrooveLane; // single file on the groove (Franklin's playtest 11-16)
            UpdateWeave(e, Time.fixedDeltaTime, lane == cfg.cautionLaneFrac && !PushingUp(e) && !e.dropping && !e.toTail);
            e.cdbg = $"v{e.kart.ForwardSpeed / U.Mph:F0} cap{(float.IsInfinity(ai.speedCap) ? -1 : ai.speedCap / U.Mph):F0} lane{lane:F2} d{ai.d:F1} pred#{(pred != null ? pred.driver.number : 0)} u{ai.s / w.track.N:F2}";
        }

        /// <summary>The fastest I can be going and still slow to vAhead at cautionBrakeG within `room` metres (less the
        /// speed controller's lag, ~1.5 m/s at that decel).</summary>
        float BrakeCap(float vAhead, float room)
            => Mathf.Sqrt(vAhead * vAhead + 2f * cfg.cautionBrakeG * U.G * Mathf.Max(0f, room - 0.5f));

        /// <summary>Under caution, stopped in a corner with the nose down at the inside berm (the driver can sort it himself).</summary>
        bool InBermCorner(Entry e)
        {
            if (phase != RacePhase.Caution) return false;
            var f = w.track.Frame(e.ai.s);
            if (f.corner < 0.5f) return false;
            var nose = e.kart.transform.TransformPoint(0f, 0f, e.kart.setup.FootFront);
            w.track.Locate(new Vector2(nose.x, nose.z), ref bermHint, out _, out float nd);
            return nd < -f.halfWidth + 0.5f && e.kart.ForwardSpeed < 2f * U.Mph;
        }
        int bermHint = -1;

        /// <summary>One to go, and this kart is past the push-up point (u = pushUpU) on the lap the green comes.</summary>
        bool PushingUp(Entry e)
        {
            if (!oneToGo) return false;
            float goArc = w.track.ArcAt(w.track.goU * w.track.N), pushArc = w.track.ArcAt(cfg.pushUpU * w.track.N);
            return e.ai.raceDist >= goDist - (goArc - pushArc);
        }

        string notReady; bool notReadyOrder, notReadyCrew, cautionGapsOpen;
        bool QueueReady()
        {
            notReady = null; notReadyOrder = false; notReadyCrew = false;
            foreach (var e in field)
            {
                if (e.beingCleared && !e.cleared) { notReady = $"crew clearing {Tag(e)}"; notReadyCrew = true; return false; }
                if (e.recovering && !e.outOfRace) { notReady = $"{Tag(e)} still recovering"; notReadyCrew = true; return false; }
                if (e.crewHelp) { notReady = $"track workers getting {Tag(e)} going"; notReadyCrew = true; return false; }
            }
            for (int i = 1; i < cautionQueue.Count; i++)
            {
                var a = cautionQueue[i - 1]; var b = cautionQueue[i];
                float bl = (b.kart.setup.FootFront + b.kart.setup.FootRear);
                float g = QueueGap(a, b);
                if (g < 0.6f || g > bl + 20f * U.Ft) { notReadyOrder = g < 0.6f; notReady = $"{Tag(b)} {(g < 0.6f ? "not behind" : "too far behind")} {Tag(a)} ({g / U.Ft:F0} ft) [#{b.driver.number}: {b.cdbg}] [#{a.driver.number}: {a.cdbg}]"; return false; } // out of order or strung out
            }
            return true;
        }

        bool cautionDumped;
        void CautionStep(float dt)
        {
            // watchdog: a caution this long is stuck somewhere -- say where, once
            if (!cautionDumped && raceTime - cautionStart > 150f)
            {
                cautionDumped = true;
                var sb = new StringBuilder("CAUTION WATCHDOG (150 s): ");
                foreach (var e in field)
                    if (!e.outOfRace && !e.finished)
                        sb.Append($"#{e.driver.number} u{e.ai.s / w.track.N:F3} d{e.ai.d:F1} v{e.kart.ForwardSpeed / U.Mph:F0} cap{(float.IsInfinity(e.ai.speedCap) ? -1 : e.ai.speedCap / U.Mph):F0} rec{(e.recovering ? 1 : 0)} help{(e.crewHelp ? 1 : 0)} tail{(e.toTail ? 1 : 0)} drop{(e.dropping ? 1 : 0)} blk{(e.blocked ? 1 : 0)} [{e.cdbg}] | ");
                Event(sb.ToString());
            }
            RebuildQueue();
            if (cautionQueue.Count == 0) return;
            // is the line strung out? (the leader slows until it closes up)
            cautionGapsOpen = false;
            for (int i = 1; i < cautionQueue.Count; i++)
            {
                var qa = cautionQueue[i - 1]; var qb = cautionQueue[i];
                if (qa.recovering || qb.recovering || Hazard(qa) || Hazard(qb)) continue;
                if (QueueGap(qa, qb) > qb.kart.setup.FootFront + qb.kart.setup.FootRear + 20f * U.Ft) { cautionGapsOpen = true; break; }
            }
            if (fsStage != 0) { FieldStopStep(); return; }
            // who is out of place: a rolling kart that belongs ahead of me is physically behind (or beside) me
            for (int k = 0; k < cautionQueue.Count; k++)
            {
                var e = cautionQueue[k]; bool was = e.dropping; e.dropping = false;
                for (int j = 0; j < k; j++)
                {
                    var a = cautionQueue[j];
                    if (a.recovering || Hazard(a)) continue;
                    if (QueueGap(a, e) < 0.6f) { e.dropping = true; break; }
                }
                if (e.dropping && !was) e.dropCrawl = Hash01(e.driver.number * 6151 + Mathf.RoundToInt(cautionStart * 10f)) < cfg.dropCrawlChance;
            }
            var lead = cautionQueue[0];
            int lapIdx = Mathf.FloorToInt(lead.ai.raceDist / L);
            if (!oneToGo && lapIdx != cautionLeaderLap)
            {
                bool first = cautionLeaderLap < 0; cautionLeaderLap = lapIdx;
                bool ready = !first && raceTime - cautionStart > cfg.minCautionSec && QueueReady();
                if (!first && !ready && notReady != null) Event($"CAUTION continues: {notReady}.");
                if (!first && !ready && notReadyOrder)
                {
                    orderFails++;
                    int mixed = 0; foreach (var q in cautionQueue) if (q.dropping) mixed++;
                    if (orderFails >= cfg.fieldStopAfterChecks || mixed >= cfg.fieldStopMixedKarts)
                    { BeginFieldStop(orderFails >= cfg.fieldStopAfterChecks ? $"line still out of order after {orderFails} laps" : $"{mixed} karts out of place"); return; }
                }
                if (ready)
                {
                    oneToGo = true; goDist = lapIdx * L + w.track.ArcAt(w.track.goU * w.track.N);
                    Event($"ONE TO GO: queue verified ({cautionQueue.Count} karts, {Tag(lead)} leads). Bunch up for the restart.");
                }
            }
            if (!oneToGo && raceTime - cautionStart > cfg.fieldStopWatchdogSec && raceTime - fsLastEnd > cfg.fieldStopWatchdogSec && !QueueReady() && !notReadyCrew)
            { BeginFieldStop($"caution {raceTime - cautionStart:F0} s and the line won't sort itself"); return; }
            if (oneToGo && lead.ai.raceDist >= goDist)
            {
                phase = RacePhase.Green; phaseTime = 0; greenTime = raceTime; oneToGo = false;
                foreach (var e in field)
                {
                    if (e.outOfRace || e.finished) continue;
                    // Scoring: caution laps don't count, and the field straddled the line when the yellow flew (karts
                    // that hadn't crossed yet lost that lap). Lead-lap karts get the leader's lap count; a kart a full
                    // lap behind in distance stays a lap down.
                    int lapsDown = Mathf.FloorToInt((lead.ai.raceDist - e.ai.raceDist) / L + 0.25f);
                    e.lapsDone = Mathf.Max(0, lead.lapsDone - lapsDown);
                    e.nextLine = (Mathf.Floor(e.ai.raceDist / L) + 1f) * L;
                    e.lastCross = raceTime; e.cleanThisLap = false; e.toTail = false; e.spotBack = false; e.preLine = true;
                    e.ai.formation = false; e.ai.SnapTrafficOffsetToCurrent();
                    // Tires cooled under the yellow: they come in again, and some never fire off properly (Franklin).
                    e.greenStartLap = e.lapsDone; e.restarted = true;
                    if (rng.NextDouble() < cfg.tireNoRefireChance)
                        e.refireMiss += cfg.tireNoRefireMin + (float)rng.NextDouble() * (cfg.tireNoRefireMax - cfg.tireNoRefireMin);
                    RollLapPace(e);
                }
                var sb = new StringBuilder($"GREEN: restart after {raceTime - cautionStart:F0} s under caution. Order:");
                for (int i = 0; i < cautionQueue.Count; i++) { var q = cautionQueue[i]; sb.Append($" {i + 1}.#{q.driver.number}(L{q.lapsDone}, next line {(q.nextLine - q.ai.raceDist) / U.Ft:F0} ft)"); }
                Event(sb.ToString());
            }
        }


        // ---------------- field stop (Franklin, q18) ----------------
        public int leadChanges; Entry lastLeader;       // scorecard (Franklin q26)
        public int fsStage; public int fieldStops;   // 0 none, 1 stopping, 2 releasing
        public readonly List<Entry> fsOrder = new List<Entry>();
        int fsNext, orderFails; float fsT, fsLastEnd = -1000f;
        float FieldStopLine => w.track.ArcAt(cfg.workerPostU[0] * w.track.N); // the turns 1-2 worker's post

        void BeginFieldStop(string why)
        {
            fsStage = 1; fieldStops++; fsT = raceTime; fsNext = 0; oneToGo = false;
            fsOrder.Clear();
            foreach (var q in cautionQueue) if (!q.outOfRace && !q.finished && !q.recovering && !q.crewHelp) fsOrder.Add(q);
            // each kart's stopping spot: single file back from the worker, in the order they physically come to him
            var arriving = new List<(Entry e, float toLine)>();
            float line = FieldStopLine;
            foreach (var q in fsOrder)
            {
                float v = q.kart.ForwardSpeed, pos = Mathf.Repeat(q.ai.raceDist, L);
                float toLine = Mathf.Repeat(line - pos, L);
                if (toLine < v * v / (2f * 1.5f) + 3f) toLine += L; // can't stop in time: next time round
                arriving.Add((q, toLine));
                q.fsHeld = false; q.fsReleased = false; q.dropping = false;
            }
            arriving.Sort((a, b) => a.toLine.CompareTo(b.toLine));
            float pitch = 0f;
            for (int i = 0; i < arriving.Count; i++)
            {
                var q = arriving[i].e;
                q.fsStopAt = q.ai.raceDist + arriving[i].toLine - pitch;
                pitch += q.kart.setup.FootFront + q.kart.setup.FootRear + cfg.fieldStopGapFt * U.Ft;
            }
            Event($"FIELD STOP: {why}. The turns 1-2 worker stops the field to line it up.");
        }

        void FieldStopStep()
        {
            if (fsStage == 1)
            {
                bool all = true;
                foreach (var q in fsOrder) if (!q.outOfRace && !q.recovering && !q.crewHelp && !q.fsHeld) { all = false; break; }
                if (all || raceTime - fsT > cfg.fieldStopGatherSec)
                {
                    fsStage = 2; fsNext = 0; fsT = raceTime - cfg.fieldStopReleaseSec;
                    var sb = new StringBuilder("FIELD STOPPED: worker has the order —");
                    foreach (var q in fsOrder) sb.Append($" #{q.driver.number}");
                    Event(sb.ToString() + (all ? "" : " (not everyone made it to the line)"));
                }
                return;
            }
            // releasing: point at the next kart once the last one is clear (or has had its chance)
            while (fsNext < fsOrder.Count && (fsOrder[fsNext].outOfRace || fsOrder[fsNext].finished)) fsNext++;
            if (fsNext < fsOrder.Count && raceTime - fsT >= cfg.fieldStopReleaseSec)
            {
                var nx = fsOrder[fsNext];
                Entry prev = null; for (int i = fsNext - 1; i >= 0; i--) if (!fsOrder[i].outOfRace) { prev = fsOrder[i]; break; }
                bool clear = prev == null || Gap(prev, nx) > nx.kart.setup.FootFront + nx.kart.setup.FootRear + 1f || raceTime - fsT > cfg.fieldStopNextWaitSec;
                if (clear)
                {
                    nx.fsHeld = false; nx.fsReleased = true; fsNext++; fsT = raceTime;
                    Event($"WORKER POINTS: #{nx.driver.number} goes ({fsNext}/{fsOrder.Count}).");
                }
            }
            if (fsNext >= fsOrder.Count && raceTime - fsT > cfg.fieldStopReleaseSec)
            {
                fsStage = 0; fsLastEnd = raceTime; orderFails = 0;
                foreach (var q in fsOrder) { q.fsHeld = false; q.fsReleased = false; }
                fsOrder.Clear();
                cautionLeaderLap = cautionQueue.Count > 0 ? Mathf.FloorToInt(cautionQueue[0].ai.raceDist / L) : -1;
                Event("FIELD RELEASED: everyone's been pointed off. Pace in order.");
            }
        }

        /// <summary>Under a field stop: drive to my stopping spot behind whoever is ahead, wait to be pointed at,
        /// then ease past the karts still stopped and pace behind the kart I was released after. All by driving.</summary>
        void FieldStopDrive(Entry e, float paceS, float paceC)
        {
            var ai = e.ai; var st = ai.kart.setup; float bl = st.FootFront + st.FootRear;
            float v = ai.kart.ForwardSpeed;
            ai.grooveLane = 0; ai.laneFrac = cfg.cautionLaneFrac;
            // the nearest kart physically ahead in my lane (anyone: stopped karts are obstacles until they go)
            Entry ahead = null; float ga = 25f;
            foreach (var o in field)
            {
                if (o == e || o.cleared || o.outOfRace && o.beingCleared) continue;
                float g = Gap(o, e);
                if (g > 0f && g < ga && Mathf.Abs(o.ai.d - ai.d) < 1.3f) { ga = g; ahead = o; }
            }
            if (!e.fsReleased)
            {
                float rem = e.fsStopAt - ai.raceDist;
                float cap = Mathf.Sqrt(2f * 1.2f * Mathf.Max(0f, rem - 0.3f));
                if (ahead != null) cap = Mathf.Min(cap, Mathf.Max(0f, ahead.kart.ForwardSpeed + cfg.followGain * (ga - bl - cfg.fieldStopGapFt * U.Ft)));
                float catchUp = Mathf.Clamp(cap - paceS, 0f, cfg.cautionCatchUpMph * U.Mph);
                ai.paceStraight = paceS + catchUp; ai.paceCorner = paceC + catchUp;
                ai.speedCap = cap;
                if (fsStage == 2 || (v < 0.3f && (rem < 1.0f || (ahead != null && ahead.fsHeld && ga < bl + cfg.fieldStopGapFt * U.Ft + 1.5f))))
                { if (!e.fsHeld) e.fsHeldAt = raceTime; e.fsHeld = true; ai.speedCap = 0f; }
                e.cdbg = $"FS stop rem{rem:F1} v{v / U.Mph:F0} held{(e.fsHeld ? 1 : 0)}";
                return;
            }
            // released: a stopped kart in my way -> go round it slowly on whichever side has room
            Entry block = null; float gb = 15f;
            foreach (var o in field)
            {
                if (o == e || !o.fsHeld && !(o.outOfRace && !o.cleared)) continue;
                float g = Gap(o, e);
                if (g > -bl - 0.5f && g < gb && Mathf.Abs(o.ai.d - ai.d) < 2.6f) { gb = g; block = o; }
            }
            var f = w.track.Frame(ai.s);
            if (block != null)
            {
                float lo = ai.LowLimit(f), hi = ai.PaceHigh(f), side = 2.2f;
                float dIn = block.ai.d - side, dOut = block.ai.d + side;
                float want = dIn >= lo ? dIn : dOut <= hi ? dOut : (block.ai.d - lo > hi - block.ai.d ? lo : hi);
                ai.grooveLane = KartAI.NoGrooveLane; ai.laneFrac = Mathf.Clamp(want / f.halfWidth, -0.95f, 0.95f);
                ai.speedCap = cfg.fieldStopPassMph * U.Mph; ai.paceStraight = ai.paceCorner = ai.speedCap;
                e.cdbg = $"FS pass #{block.driver.number} d{ai.d:F1}->{want:F1}";
                return;
            }
            // clear: pace behind the kart released before me
            Entry pred = null; int idx = fsOrder.IndexOf(e);
            for (int i = idx - 1; i >= 0; i--) if (!fsOrder[i].outOfRace && fsOrder[i].fsReleased) { pred = fsOrder[i]; break; }
            if (pred == null)
            {   // first away: slow until everyone's been pointed off
                ai.paceStraight = ai.paceCorner = Mathf.Min(paceC, cfg.fieldStopLeadMph * U.Mph);
                ai.speedCap = ahead != null ? Mathf.Max(0f, ahead.kart.ForwardSpeed + cfg.followGain * (ga - bl - cfg.cautionGapFt * U.Ft)) : float.PositiveInfinity;
            }
            else
            {
                float gap = Gap(pred, e), want = bl + cfg.cautionGapFt * U.Ft;
                float cap = gap > 0f ? pred.kart.ForwardSpeed + cfg.followGain * (gap - want) : cfg.fieldStopPassMph * U.Mph * 0.5f;
                float catchUp = Mathf.Clamp(cap - paceS, 0f, cfg.cautionCatchUpMph * U.Mph);
                ai.paceStraight = paceS + catchUp; ai.paceCorner = paceC + catchUp;
                ai.speedCap = Mathf.Max(0f, cap);
                if (ahead != null && ahead != pred) ai.speedCap = Mathf.Min(ai.speedCap, Mathf.Max(0f, ahead.kart.ForwardSpeed + cfg.followGain * (ga - want)));
            }
            e.cdbg = $"FS go pred#{(pred != null ? pred.driver.number : 0)} v{v / U.Mph:F0}";
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
        public long dbgBigPushes, dbgCover, dbgSqueeze, dbgAnnoyed, dbgBattle, dbgPeeksFailed, dbgPushes, dbgPushPasses, dbgCompleted, dbgRuns, dbgRunStarted, dbgRunDone, dbgBumpDraft;
        public int diveTraces; int traceAtk = -1, traceTgt = -1; float traceT0, traceAcc;
        public long dbgAhead, dbgClose, dbgFaster, dbgHeld, dbgWant, dbgInsideOk, dbgOutsideOk, dbgStarted, dbgHaz;
        public int Contacts { get { int n = 0; foreach (var e in field) { var c = e.kc; if (c) n += c.contacts; } return n / 2; } }
        public int HardContacts { get { int n = 0; foreach (var e in field) { var c = e.kc; if (c) n += c.hardContacts; } return n / 2; } }

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
