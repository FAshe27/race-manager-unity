// AI driver, M1 scope (spec §7 line following + §6.4 speed): pure pursuit on
// Franklin's groove, lookahead ∝ speed, target speed from the corner profile
// read AT the lookahead point. Outputs steering angle + accel command only;
// the kart's motion is the physics model's business.
// Also tracks progress (s, d), laps, and band breaches for the report/soak.
using System.Collections.Generic;
using UnityEngine;

namespace OKT
{
    [System.Serializable]
    public class DriverConfig
    {
        // Authored in imperial (Franklin); SI getters below.
        // Speed plan: vTarget = vMax (1 - cornerSlow x corner). The engine model sets top speed
        // (12/63, 34" rights: 6,900 rpm target ≈ 41.2 mph; the kart runs to ~44.5 mph as the power
        // falls off past target, "gear bound"), so vMax sits above that and only shapes the corners.
        // Default 52 mph / 0.30 -> 36.4 mph target at full corner, laps ≈ 11.03 s (Safe).
        // 0.27 -> laps ≈ 10.77 s (Franklin's fast window) but spins on any upset; 0.25 spins on clean laps.
        public float vMaxMph = 52f;            // [TEST VALUE] (spec start: 48.1 mph / 21.5 m/s)
        // Franklin (2026-11-14 playtest): "Safe" 0.30 feels realistic -> default.
        public static float DefaultCornerSlow = 0.26f;  // closer to the edge of grip (Franklin 11-16; was 0.30); soak 11-17: 0.26 laps 10.95 s, his 10.7-11.1 (headless test may override)
        public static bool UseTireFeel = false;          // loose-stuff speed also reads the rear tires' current grip (dust) [soak knob]
        public float cornerSlow = DefaultCornerSlow; // [TEST VALUE] (spec start: 0.30)
        public float pace = 1.0f;              // per-kart pace factor (M2 field spread)
        public float lookaheadTime = 0.25f;    // steering lookahead, s [TEST VALUE] — spec ≈0.6 s overshoots onto the walls (M1 sweep)
        public float speedLookaheadTime = 0.6f; // corner-profile read point, spec §7 ≈0.6 s [TEST VALUE]
        public float yawDamping = 0.1f;        // rad of steer per rad/s of unwanted yaw rate [TEST VALUE]
        public float minLookaheadFt = 8.2f;    // controller floor at low speed [TEST VALUE]
        public float speedGain = 2.0f;         // accel cmd per (m/s) of speed error, 1/s [TEST VALUE]
        public static float DefaultFeatherStartDeg = 6f, DefaultFeatherRangeDeg = 10f;
        public float featherStartDeg = DefaultFeatherStartDeg;     // body slip angle where the driver starts easing the throttle [TEST VALUE]
        public float featherRangeDeg = DefaultFeatherRangeDeg;    // ...and is fully off by start + this [TEST VALUE]
        public float lateralOffsetFt = 0f;     // personal offset from the groove (spec §7), + = toward the outside

        public float vMax => vMaxMph * U.Mph;
        public float minLookahead => minLookaheadFt * U.Ft;
        public float lateralOffset => lateralOffsetFt * U.Ft;
    }

    public class KartAI : MonoBehaviour
    {
        public DriverConfig cfg = new DriverConfig();
        public KartPhysics kart;
        public TrackModel track;

        // Telemetry
        public float targetSpeed;
        public float s, d;                 // track coordinates of the kart centre
        public int lapsCompleted;
        public float lastLap, bestLap = float.PositiveInfinity;
        public readonly List<float> lapTimes = new List<float>();
        public int bandBreachSteps;        // steps with the kart centre past the outside edge WHERE A BARRIER STANDS (= through the wall)
        public int offTrackSteps;
        public int bandBreaches;           // number of separate through-the-wall events
        bool wasOutside, breachedNow;          // steps with the centre off the dirt legally: infield grass or an opening
        public float maxAbsDOverHw;        // max |d| / halfWidth seen
        public float minWallGap = float.PositiveInfinity; // closest body corner to a barrier face, m
        public float wallGapNow = float.PositiveInfinity; // this step (∞ at openings)

        // Drive-over test (spec §11 M1): steer through the roughest patch once.
        public bool driveOverArmed;
        public bool driveOverActive;
        public float driveOverS, driveOverD;
        public float driveOverArcWindow = 14f; // metres before/after the patch to blend over [TEST VALUE]
        public System.Action<KartAI> onDriveOverDone;

        // ---- Race director commands (M2), set every step before Step(). Defaults = free running (M1).
        [System.NonSerialized] public bool formation;        // pace lap: hold a grid lane instead of the groove
        [System.NonSerialized] public float laneFrac;        // lane centre as a fraction of half-width (+ = outside)
        // Formation on the groove (Franklin's playtest 11-16: pacing follows the groove, nobody drives the loose stuff):
        // 0 = single file on the groove, -1/+1 = inside/outside of a pair straddling it, NoGrooveLane = use laneFrac.
        public const int NoGrooveLane = 99;
        [System.NonSerialized] public int grooveLane = NoGrooveLane;
        [System.NonSerialized] public float grooveLaneHalf = 0.635f; // half the pair's centre-to-centre spacing (m)
        // How fully the driver allows for the loose stuff off the groove (0 = drives it at groove speed and slides up,
        // 1 = backs off to what it holds). From consistency: green drivers overdrive it (Franklin's clip). [TEST VALUE]
        [System.NonSerialized] public float looseAwareness = 0.8f;
        [System.NonSerialized] float formD = float.NaN;               // smoothed formation lane target (m)
        public const float FormLaneRateBase = 0.4f, FormLaneRatePerMps = 0.3f; // lateral m/s: 0.4 + 0.3 x speed [TEST VALUE]
        // Pacing stays off the wall (Franklin, v4 playtest: the outside pace lane rode the backstretch wall where the
        // groove runs high mid-straight). Daylight between the nerf bar and the rail, in inches. [TEST VALUE]
        public const float PaceWallGapIn = 30f;
        public float PaceHigh(in TrackFrame f) => Mathf.Min(HighLimit(f), f.halfWidth - kart.setup.FootRight - PaceWallGapIn * U.In);
        [System.NonSerialized] public bool paceMode;         // drive the pace-speed profile, not racing speed
        [System.NonSerialized] public float paceStraight = 25f * U.Mph, paceCorner = 20f * U.Mph;
        [System.NonSerialized] public float speedCap = float.PositiveInfinity; // traffic / check-up cap (m/s)
        [System.NonSerialized] public float trafficGoal;     // lateral offset from the groove the traffic logic wants (m)
        [System.NonSerialized] public float trafficRate = 1.0f; // m/s the traffic offset may change (eased, no steps)
        float trafficOffset;
        public float TrafficOffset => trafficOffset;
        /// <summary>Start the eased traffic offset from where the kart actually is (no lateral step at the green).</summary>
        public void SnapTrafficOffsetToCurrent() { trafficOffset = d - LineOffsetHere - cfg.lateralOffset; trafficGoal = trafficOffset; }
        /// <summary>Unwrapped distance along the centerline (m). S/F crossings are multiples of the lap length.</summary>
        [System.NonSerialized] public float raceDist;
        float prevArc = float.NaN;
        /// <summary>The speed this driver would run with no traffic (racing profile x pace), at the lookahead.</summary>
        public float freeSpeed;

        // Test hook: force a steering angle (simulates a driver error that points the kart at the wall).
        [System.NonSerialized] public bool steerOverride;
        [System.NonSerialized] public float steerOverrideDeg;

        int lineHint = -1, locHint = -1;
        float lapStartTime = -1f;
        float prevS = -1f;


        public void Init(KartPhysics k, TrackModel t, DriverConfig c)
        {
            kart = k; track = t; cfg = c;
        }

        /// <summary>When true (dodging a wreck) the kart may use the berm; otherwise its right-side tires stay off it.</summary>
        [System.NonSerialized] public bool allowBerm;
        /// <summary>Gap kept between the right-side tires and the berm when aiming (in). 1 in = right up to it.</summary>
        [System.NonSerialized] public float bermBufferIn = 4f; // [TEST VALUE] (M2: 1 in let karts in traffic dust up and hit the rail at the T2 exit)
        /// <summary>Spin recovery (spec §8.3): turn the kart back to the racing direction and rejoin slowly.</summary>
        [System.NonSerialized] public bool recovering;
        /// <summary>On an inside pass: brake earlier/deeper (rear-only brakes + a tighter line spin the kart otherwise).</summary>
        [System.NonSerialized] public bool diving;
        [System.NonSerialized] public float divingSpeedFactor = 1.0f; // [TEST VALUE]
        /// <summary>Director: driving harder than safe pace while pushing or passing — cornerSlow is reduced by this
        /// (0.30 Safe -> 0.27 Fast at most). Closer to the grip limit, so a spin is more likely: honest physics.</summary>
        [System.NonSerialized] public float attackBoost;
        /// <summary>Director: I'm being pushed — I enter the corner this much (m/s) faster than I want to (Franklin).</summary>
        [System.NonSerialized] public float pushedBoost;
        [System.NonSerialized] public float recoverySpeed = 8f * U.Mph;
        /// <summary>Lane limits from karts alongside (absolute lateral m), set by the director each step.</summary>
        /// <summary>Staging-grid start: world points from the kart's grid slot, down the grid lane and through the gate onto
        /// the track. While set, the kart drives the path (it's off the track legitimately); cleared once it's on the dirt.</summary>
        [System.NonSerialized] public float weaveAmp, weavePeriod = 1.6f; float weavePhase, weaveNow; // tire-warming weave (m, s)
        [System.NonSerialized] public bool rejoinOnGroove; // recovering under caution: rejoin on the groove, not low
        [System.NonSerialized] public List<Vector3> stagePath;
        [System.NonSerialized] public int stageSlowIdx = int.MaxValue;   // from this path point on: gate speed (director)
        [System.NonSerialized] public Vector3 stageGatePos;
        [System.NonSerialized] public int stageGateIdx = int.MaxValue;     // the path point at the gate
        [System.NonSerialized] public float stageGateArc;                  // the gate's distance round the lap (m)
        [System.NonSerialized] public int stageIdx;
        [System.NonSerialized] public bool limitsRideGroove;
        // Catching a slide (Franklin 11-18: "Them saving the kart in a slide is instantaneous and there's a human delay
        // in real life. While the better drivers detect it quicker, some drivers don't" -- "super slight, I just want it
        // to not be superhuman drivers"). The driver feels the kart rotate under him this long late; where he's going
        // (his line, the karts around him) he still sees at once. Set by the director from consistency.
        [System.NonSerialized] public float reactSec = 0.1f;
        // Point of no return (Franklin 11-18: "when a kart starts to spin and they spin about 15-20 degrees to the left in
        // a corner, there's no saving that without getting lucky by another kart hitting your nose"; 11-19, after seeing the
        // angles drawn: "passing roughly 35 degrees is the point of no return"): rotated this far
        // to the inside of the corner, it's gone. He still cranks the wheel right and the throttle comes off as the slide grows
        // (Franklin: "crank the wheel to the right and modulate through gas and brake to hopefully save the slide") --
        // whatever saves it now is the physics and luck, not him; once it's really spinning he locks the brakes ("If they
        // spin, they'll usually lock the brakes down"). Director sets it.
        [System.NonSerialized] public float noReturnDeg = 35f;
        public static bool SlideReact = true, NoReturn = true;  // soak knobs
        public const float NoReturnCornerMin = 0.3f;   // "in a corner": the corner profile past this [TEST VALUE]
        public const float SpinLockDeg = 60f;          // rotated this far it's a spin: brakes locked [TEST VALUE]
        public const float NoReturnLuckDeg = 8f;       // knocked back under this (another kart's nose): he has it again [TEST VALUE]
        const int SlideHistN = 64;
        readonly float[] betaHist = new float[SlideHistN], yawHist = new float[SlideHistN]; int slideHead, slideCount;
        [System.NonSerialized] public bool gone; // past the point of no return
        public static long DbgGone, DbgLucky;
        public static float WallLookSec = 0.4f;   // sliding past my aim into the wall within this long: drag the brake [TEST VALUE]
        public static float WallDragFrac = 0.05f; // ...at most this much under my speed (about 1/8 g at 30 mph) [TEST VALUE]
        [System.NonSerialized] public float latHardMin = float.NegativeInfinity, latHardMax = float.PositiveInfinity, planPush;
        public static float PlanEaseMps = 1.5f;   // m/s the occupancy push on my aim comes in at [TEST VALUE]     // set by the director for the first seconds after a green
        [System.NonSerialized] public float lastAimD, lastAimLow; // forensics: the steering target and its low limit
        [System.NonSerialized] public float latMin = float.NegativeInfinity, latMax = float.PositiveInfinity;

        /// <summary>
        /// Highest lateral position this driver aims for. Franklin: karts get right up to the berm but not on it
        /// (loose dirt dusts the tires) — so personal offsets and traffic moves never put the right sides on it.
        /// The wall-footprint limit applies when dodging a wreck.
        /// </summary>
        public float HighLimit(in TrackFrame f)
        {
            float wall = f.halfWidth - kart.setup.FootRight - 2f * U.In;
            if (allowBerm) return wall;
            float rightTire = kart.setup.TireOuter(true);
            return Mathf.Min(wall, f.halfWidth - track.cfg.bermWidth - rightTire - bermBufferIn * U.In);
        }

        /// <summary>
        /// Lowest the kart's centre may aim (m): its whole footprint on the racing surface, with more room in the
        /// corners — pure pursuit cuts the chord to its target and a kart sliding a few degrees drifts in further, so a
        /// 2 in margin put the left side over the inside edge at the T3/T4 apex (M2 soak, 2026-11-15).
        /// </summary>
        public float LowLimit(in TrackFrame f)
            => -f.halfWidth + kart.setup.FootLeft + Mathf.Lerp(2f, insideCornerMarginIn, f.corner) * U.In;
        [System.NonSerialized] public float insideCornerMarginIn = 6f; // [TEST VALUE]

        /// <summary>Lateral offset (m) of Franklin's groove at this kart's current position.</summary>
        public float LineOffsetHere => track.Frame(s).LineOffset;

        public void Step(float dt, float simTime)
        {
            var tr = transform; long pt = Perf.T();
            var p2 = new Vector2(tr.position.x, tr.position.z);

            // --- progress, laps, band
            track.Locate(p2, ref locHint, out s, out d);
            var fr = track.Frame(s);
            float ratio = Mathf.Abs(d) / fr.halfWidth;
            if (ratio > maxAbsDOverHw) maxAbsDOverHw = ratio;
            bool barrierHere = track.BarrierAt(s / track.N) != BarrierType.None;
            // Breach = the kart centre CROSSES the outside edge where a barrier stands (through the wall).
            // Leaving through an opening or onto the infield is off-track, not a breach — and a kart
            // already off-track (e.g. out an opening, behind the next rail) is not re-counted.
            bool outside = ratio > 1f;
            if (kart.Body.isKinematic || stagePath != null) { wasOutside = outside; breachedNow = false; } // moved by the crew / driving out of the staging grid: not racing
            if (outside && !kart.Body.isKinematic && stagePath == null)
            {
                if (!wasOutside) { if (d > 0f && barrierHere) { bandBreaches++; breachedNow = true; } else breachedNow = false; }
                if (breachedNow) bandBreachSteps++; else offTrackSteps++;
            }
            else breachedNow = false;
            wasOutside = outside;
            Perf.Add(0, ref pt);
            wallGapNow = barrierHere ? WallGap(fr) : float.PositiveInfinity;
            Perf.Add(1, ref pt);
            if (wallGapNow < minWallGap) minWallGap = wallGapNow;
            if (prevS >= 0f && prevS > track.N * 0.75f && s < track.N * 0.25f)
            {
                if (lapStartTime >= 0f)
                {
                    lastLap = simTime - lapStartTime;
                    lapTimes.Add(lastLap);
                    if (lastLap < bestLap) bestLap = lastLap;
                    lapsCompleted++;
                }
                lapStartTime = simTime;
            }
            prevS = s;
            float arcNow = track.ArcAt(s);
            if (float.IsNaN(prevArc)) { prevArc = arcNow; }
            else
            {
                float da = arcNow - prevArc;
                if (da > track.centerLength * 0.5f) da -= track.centerLength;
                else if (da < -track.centerLength * 0.5f) da += track.centerLength;
                raceDist += da; prevArc = arcNow;
            }

            Perf.Add(2, ref pt);
            if (stagePath != null) { StageStep(fr); return; }

            // --- pure pursuit
            lineHint = track.NearestLineIndex(p2, lineHint);
            float v = Mathf.Max(0f, kart.ForwardSpeed);
            float ld = Mathf.Max(cfg.minLookahead, v * cfg.lookaheadTime);
            int ti = track.LineAdvance(lineHint, ld);
            float ts = track.LineS(ti);
            var tf = track.Frame(ts);
            Perf.Add(3, ref pt);
            // Never aim through a wall (spec §7): keep the target where the whole kart fits on the dirt.
            trafficOffset += Mathf.Clamp(trafficGoal - trafficOffset, -trafficRate * dt, trafficRate * dt);
            // personal offset fades out through the corner: everyone runs the optimal line there (Franklin: the line is
            // at grip's edge — entering low on purpose costs too much momentum unless you're passing)
            float off = cfg.lateralOffset * (1f - tf.corner) + DriveOverOffset(ti) + trafficOffset;
            float dWant = formation ? laneFrac * tf.halfWidth : tf.LineOffset + off;
            if (formation && grooveLane != NoGrooveLane)
            {
                if (grooveLane == 0) dWant = Mathf.Min(tf.LineOffset, PaceHigh(tf));
                else
                {   // the pair straddles the groove, slid down where the groove runs near the wall (both karts on the dirt)
                    float lo = LowLimit(tf) + grooveLaneHalf, hi = PaceHigh(tf) - grooveLaneHalf;
                    float pc = lo < hi ? Mathf.Clamp(tf.LineOffset, lo, hi) : tf.LineOffset;
                    dWant = pc + grooveLane * grooveLaneHalf;
                }
            }
            if (formation && !recovering)
            {
                if (float.IsNaN(formD)) formD = d;
                formD = Mathf.MoveTowards(formD, dWant, (FormLaneRateBase + FormLaneRatePerMps * v) * dt);
                // the weave eases in and out (never a step) and rides on the lane, so it can't walk the kart off its spot
                weaveNow = Mathf.MoveTowards(weaveNow, weaveAmp, 0.5f * dt);
                weavePhase += dt * 2f * Mathf.PI / Mathf.Max(0.5f, weavePeriod);
                dWant = formD + weaveNow * Mathf.Sin(weavePhase);
            }
            else formD = float.NaN;
            // rejoin low, off the groove, out of the way -- under caution there's no traffic at speed to stay clear of,
            // and aiming low there walked crossed-up karts off the inside edge (soak 2026-11-17)
            if (recovering) dWant = rejoinOnGroove ? tf.LineOffset : Mathf.Min(d, tf.LineOffset - 1.0f);
            float dT = Mathf.Clamp(dWant, LowLimit(tf), HighLimit(tf));
            // Occupancy as hard lane limits on the STEERING TARGET (karts alongside own their slots).
            // Off a green, while the pack is still double file, carry the limits along with the groove to the aim point --
            // the karts beside me are following it too. (Franklin 11-18: off the green the field went four and five wide.
            // The groove climbs ~16 ft from the turn 4 exit to the line; limits held where the karts were let only the
            // front row climb with it, and every row behind climbed a step later than the row ahead. Only off the green:
            // in traffic it also let a kart turn down onto one running under it at the apex -- soak 11-18, cautions x3.)
            float gShift = limitsRideGroove ? tf.LineOffset - LineOffsetHere : 0f;
            float dPre = dT;
            if (latMin <= latMax) dT = Mathf.Clamp(dT, latMin + gShift, latMax + gShift);
            else dT = d; // squeezed from both sides: hold my line
            if (gShift != 0f) dT = Mathf.Clamp(dT, LowLimit(tf), HighLimit(tf)); // a shifted limit never aims me off the dirt
            // A kart beside me changes where I'm GOING, not where I am (Franklin 11-18): the limit's push on my aim comes in
            // over a moment (the plan ahead reshapes) instead of in one step (a yank of the wheel, toward the wall or into
            // him), and eases back out as he clears (letting go at once was itself a yank: incidents 0.3 -> 1.4/race). The hard floor -- rubbing, never across him -- holds throughout.
            {
                float push = dT - dPre;
                // (not off a green: there the limits ride the groove and must move with it at once -- eased, the double
                // file froze row by row again and fanned out four wide, Franklin's playtest 11-18 evening)
                if (!limitsRideGroove) push = Mathf.MoveTowards(planPush, push, PlanEaseMps * dt);
                planPush = push; dT = dPre + push;
                dT = Mathf.Clamp(dT, Mathf.Min(latHardMin, latHardMax) + gShift, Mathf.Max(latHardMin, latHardMax) + gShift);
                dT = Mathf.Clamp(dT, LowLimit(tf), HighLimit(tf));
            }
            lastAimD = dT; lastAimLow = LowLimit(tf);
            var target = tf.pos + tf.outN * dT;

            var rear = tr.TransformPoint(0f, 0f, -kart.setup.wheelbase * 0.5f);
            var toT = new Vector3(target.x - rear.x, 0f, target.y - rear.z);
            var local = tr.InverseTransformDirection(toT);
            float alpha = Mathf.Atan2(local.x, local.z);
            // --- the slide as the driver feels it: body slip (nose vs. where the kart is travelling, + = nose right)
            float yawRate = Vector3.Dot(kart.Body.angularVelocity, tr.up);
            float slipB = 0f;
            {
                var vb = kart.Body.linearVelocity; vb.y = 0f; var fb = tr.forward; fb.y = 0f;
                if (vb.magnitude > 3f) slipB = Vector3.SignedAngle(vb, fb, Vector3.up) * Mathf.Deg2Rad;
            }
            slideHead = (slideHead + 1) % SlideHistN; betaHist[slideHead] = slipB; yawHist[slideHead] = yawRate;
            if (slideCount < SlideHistN) slideCount++;
            bool racing = !formation && !paceMode && !recovering;
            if (SlideReact && racing && dt > 0f)
            {
                // he still thinks the nose is where it was reactSec ago: a kart stepping out is caught that much late
                int k = Mathf.Min(Mathf.RoundToInt(reactSec / dt), slideCount - 1, SlideHistN - 1);
                int j = (slideHead - k + SlideHistN) % SlideHistN;
                alpha += slipB - betaHist[j];
                yawRate = yawHist[j];
            }
            float dist = Mathf.Max(0.5f, new Vector2(local.x, local.z).magnitude);
            float kappa = 2f * Mathf.Sin(alpha) / dist;               // pure-pursuit path curvature
            float steerRad = Mathf.Atan(kart.setup.wheelbase * kappa);
            // Yaw-rate damping: steer against yaw rate the pursuit arc doesn't ask for.
            steerRad += cfg.yawDamping * (v * kappa - yawRate);
            kart.steerDeg = Mathf.Clamp(steerRad * Mathf.Rad2Deg, -kart.setup.maxSteerDeg, kart.setup.maxSteerDeg);
            // past the point of no return: nose ~35 deg to the inside (left) in a corner -- no saving it
            float insideDeg = -slipB * Mathf.Rad2Deg;
            if (!NoReturn || !racing || v < 3f * U.Mph) gone = false;
            else if (!gone && insideDeg > noReturnDeg && fr.corner > NoReturnCornerMin) { gone = true; DbgGone++; }
            else if (gone && Mathf.Abs(insideDeg) < NoReturnLuckDeg) { gone = false; DbgLucky++; } // knocked straight again: lucky
            if (gone) kart.steerDeg = kart.setup.maxSteerDeg; // cranked full right
            if (steerOverride) kart.steerDeg = steerOverrideDeg; // debug/test only: a driver error

            Perf.Add(4, ref pt);
            // Speed is read further ahead (spec: ≈0.6 s) so braking anticipates the corner.
            var tfSteer = tf; int si0 = ti;
            if (cfg.speedLookaheadTime != cfg.lookaheadTime)
            {
                int si = track.LineAdvance(lineHint, Mathf.Max(cfg.minLookahead, v * cfg.speedLookaheadTime));
                tf = track.Frame(track.LineS(si)); si0 = si;
            }

            Perf.Add(5, ref pt);
            // --- speed: corner profile read at the lookahead point
            freeSpeed = cfg.vMax * cfg.pace * (1f - Mathf.Max(0f, cfg.cornerSlow - attackBoost) * tf.corner);
            // Off-line corner speed: the profile is tuned for Franklin's groove. A tighter line (an inside dive,
            // a kart pushed low) can't carry groove speed — scale by sqrt(path radius / groove radius), the grip
            // limit. Wider lines keep groove speed (the outside costs distance, not grip, here).
            if (!formation)
            {
                float kap = track.CurvatureAt(track.LineS(si0));
                if (kap > 1f / 150f)
                {
                    float rC = 1f / kap;
                    float offFromLine = dT - tfSteer.LineOffset;
                    float rLine = rC + tf.LineOffset, rPath = rC + tf.LineOffset + offFromLine;
                    if (rPath < rLine && rPath > 1f) freeSpeed *= Mathf.Sqrt(rPath / rLine);
                }
            }
            Perf.Add(6, ref pt);
            // the loose stuff: a corner off the groove holds sqrt(grip) of the speed (here and where I'm aiming)
            if (!formation && tf.corner > 0.05f)
            {
                float gHere = track.GroundAtSD(s, d).grip, gThere = track.GroundAtSD(track.LineS(si0), dT).grip;
                float g = Mathf.Min(gHere, gThere);
                // ...and what my own tires tell me: dust from the loose / berm takes the bite away too (a kart up in the
                // loose with dusted rights spun at 30 mph off turn 2, soak 2026-11-17)
                if (DriverConfig.UseTireFeel) g = Mathf.Min(g, Mathf.Min(kart.surfaceGripNow[2], kart.surfaceGripNow[3]));
                g = Mathf.Clamp(g, 0.3f, 1f);
                freeSpeed *= Mathf.Lerp(1f, Mathf.Lerp(1f, Mathf.Sqrt(g), Mathf.Clamp01(tf.corner * 2f)), looseAwareness);
            }
            Perf.Add(7, ref pt);
            targetSpeed = paceMode ? Mathf.Lerp(paceStraight, paceCorner, tf.corner) : freeSpeed;
            if (diving && !paceMode) targetSpeed *= divingSpeedFactor;
            if (!paceMode) targetSpeed += pushedBoost;
            if (recovering) targetSpeed = recoverySpeed;
            targetSpeed = Mathf.Min(targetSpeed, speedCap);
            // Pushing wide toward the wall (an exit, a slide): drag the brake a bit to tighten up rather than bounce off
            // it (Franklin 11-18: "much more often drag the brake a bit to avoid hitting the wall ... That breaks stuff").
            if (!paceMode && !recovering && !formation)
            {
                var fw = track.Frame(s); var vw = kart.Body.linearVelocity;
                // only when the slide is carrying me past where I'm aiming and on into the wall -- the groove itself runs
                // up by the wall on the exits, and dragging the brake for that cost ~0.45 s a lap (soak 11-18)
                float vOut = vw.x * fw.outN.x + vw.z * fw.outN.y;
                float dPred = d + vOut * WallLookSec, over = dPred - HighLimit(fw);
                if (vOut > 0.3f && over > 0f && dPred > lastAimD + 0.3f)
                {
                    float urg = Mathf.Clamp01(over / (vOut * WallLookSec));
                    targetSpeed = Mathf.Min(targetSpeed, kart.ForwardSpeed * (1f - WallDragFrac * urg));
                }
            }
            kart.accelCmd = (targetSpeed - kart.ForwardSpeed) * cfg.speedGain;
            // Feather the throttle when the rear steps out: a driver eases off a power slide instead of holding it
            // flat. Held flat, the rear caught grip all at once with the wheel turned against the slide and
            // high-sided the kart (turn-2-exit self spins and a self flip, soak 2026-11-15).
            if (kart.accelCmd > 0f && !paceMode)
            {
                var vel = kart.Body.linearVelocity; vel.y = 0f;
                if (vel.magnitude > 3f)
                {
                    var fwd = tr.forward; fwd.y = 0f;
                    float beta = Vector3.Angle(fwd, vel);
                    if (beta > cfg.featherStartDeg)
                        kart.accelCmd *= Mathf.Clamp01(1f - (beta - cfg.featherStartDeg) / cfg.featherRangeDeg);
                }
            }
            if (gone && Mathf.Abs(slipB) * Mathf.Rad2Deg > SpinLockDeg) kart.accelCmd = -100f; // spinning: brakes locked (capped at what they hold)
            Perf.Add(8, ref pt);
        }

        /// <summary>Drive the staging path: pure pursuit on its points at the director's speed cap. Done once past the
        /// last point and the whole kart is on the racing surface; then the formation takes over from where it is.</summary>
        void StageStep(in TrackFrame fr)
        {
            var tr = transform;
            var rear = tr.TransformPoint(0f, 0f, -kart.setup.wheelbase * 0.5f);
            float look = cfg.minLookahead * 0.6f;
            while (stageIdx < stagePath.Count - 1)
            {
                var q = stagePath[stageIdx]; q.y = rear.y;
                if (Vector3.Distance(q, rear) < look) stageIdx++; else break;
            }
            var target = stagePath[stageIdx];
            var local = tr.InverseTransformDirection(new Vector3(target.x - rear.x, 0f, target.z - rear.z));
            float alpha = Mathf.Atan2(local.x, local.z);
            float dist = Mathf.Max(0.5f, new Vector2(local.x, local.z).magnitude);
            float kappa = 2f * Mathf.Sin(alpha) / dist;
            kart.steerDeg = Mathf.Clamp(Mathf.Atan(kart.setup.wheelbase * kappa) * Mathf.Rad2Deg, -kart.setup.maxSteerDeg, kart.setup.maxSteerDeg);
            targetSpeed = speedCap; // the director's staging speed (was also held under 12 mph here, which hid the setting)
            kart.accelCmd = (targetSpeed - kart.ForwardSpeed) * cfg.speedGain;
            bool onTrack = Mathf.Abs(d) < fr.halfWidth - kart.setup.FootRight - 0.3f;
            if (stageIdx >= stagePath.Count - 1 && onTrack) { stagePath = null; formD = float.NaN; SnapTrafficOffsetToCurrent(); }
        }

        /// <summary>Front corner of the footprint: on the rounded nose, the point of its corner arc at 45 deg.</summary>
        static Vector3 NoseCorner(KartSetup st, bool right)
        {
            float x = right ? st.FootRight : st.FootLeft;
            if (!st.bodyOn) return new Vector3(right ? x : -x, 0.1f, st.FootFront);
            float R = st.noseCornerRadiusIn * U.In;
            float Rx = Mathf.Min(R, x), Rz = Mathf.Min(R, st.noseAheadOfTiresIn * U.In); // elliptical nose corner
            return new Vector3((x - Rx * (1f - 0.7071f)) * (right ? 1f : -1f), 0.1f, st.FootFront - Rz * (1f - 0.7071f));
        }

        Vector3[] wallCorners;
        float WallGap(in TrackFrame fr)
        {
            // Outermost body corner's lateral distance to the outside edge (wall face).
            // Outermost point of the plan-view footprint (nerf bars / bumper ends) to the outside edge.
            var st = kart.setup;
            float best = float.PositiveInfinity;
            // the nine footprint corners in kart space, built once (they were rebuilt -- a new array, two NoseCorner
            // calls -- every step for every kart near a wall); one world matrix instead of nine engine calls
            if (wallCorners == null)
                wallCorners = new[] {
                    new Vector3(st.FootRight, 0.1f, st.wheelbase * 0.5f), new Vector3(st.FootRight, 0.1f, -st.wheelbase * 0.5f),
                    new Vector3(-st.FootLeft, 0.1f, st.wheelbase * 0.5f), new Vector3(-st.FootLeft, 0.1f, -st.wheelbase * 0.5f),
                    NoseCorner(st, true), NoseCorner(st, false), new Vector3(0f, 0.1f, st.FootFront),
                    new Vector3(st.rearBumperHalfWidthIn * U.In, 0.1f, -st.FootRear), new Vector3(-st.rearBumperHalfWidthIn * U.In, 0.1f, -st.FootRear) };
            var m = transform.localToWorldMatrix;
            foreach (var corner in wallCorners)
            {
                var w = m.MultiplyPoint3x4(corner);
                float dd = Vector2.Dot(new Vector2(w.x, w.z) - fr.pos, fr.outN);
                best = Mathf.Min(best, fr.halfWidth - dd);
            }
            return best;
        }

        // Offset (m, along the outward normal) that bends the kart's path through the
        // roughest patch for one pass, blending in and out over driveOverArcWindow.
        float DriveOverOffset(int lineIdx)
        {
            if (!driveOverArmed && !driveOverActive) return 0f;
            float L = track.lineLength;
            int patchIdx = Mathf.RoundToInt(driveOverS / track.N * track.LineCount) % track.LineCount;
            float aPatch = track.LineArc(patchIdx);
            float aHere = track.LineArc(lineIdx);
            float delta = Mathf.Repeat(aHere - aPatch + L * 0.5f, L) - L * 0.5f; // signed metres to the patch
            float aKart = track.LineArc(lineHint);
            float kartDelta = Mathf.Repeat(aKart - aPatch + L * 0.5f, L) - L * 0.5f;

            if (driveOverArmed && kartDelta < -driveOverArcWindow * 2f && kartDelta > -driveOverArcWindow * 3f)
            { driveOverArmed = false; driveOverActive = true; }
            if (driveOverActive && kartDelta > driveOverArcWindow * 1.5f)
            { driveOverActive = false; onDriveOverDone?.Invoke(this); }
            if (!driveOverActive) return 0f;

            var pf = track.Frame(driveOverS);
            float want = driveOverD - pf.LineOffset;
            float w = Mathf.Abs(delta) >= driveOverArcWindow ? 0f : 0.5f * (1f + Mathf.Cos(Mathf.PI * delta / driveOverArcWindow));
            return want * w;
        }

        /// <summary>Lap progress for ordering: laps + fraction.</summary>
        public float Progress => lapsCompleted + s / track.N;
    }
}
