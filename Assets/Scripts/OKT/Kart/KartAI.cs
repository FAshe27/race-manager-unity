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
        public static float DefaultCornerSlow = 0.30f; // (headless test may override)
        public float cornerSlow = DefaultCornerSlow; // [TEST VALUE] (spec start: 0.30)
        public float pace = 1.0f;              // per-kart pace factor (M2 field spread)
        public float lookaheadTime = 0.25f;    // steering lookahead, s [TEST VALUE] — spec ≈0.6 s overshoots onto the walls (M1 sweep)
        public float speedLookaheadTime = 0.6f; // corner-profile read point, spec §7 ≈0.6 s [TEST VALUE]
        public float yawDamping = 0.1f;        // rad of steer per rad/s of unwanted yaw rate [TEST VALUE]
        public float minLookaheadFt = 8.2f;    // controller floor at low speed [TEST VALUE]
        public float speedGain = 2.0f;         // accel cmd per (m/s) of speed error, 1/s [TEST VALUE]
        public float featherStartDeg = 6f;     // body slip angle where the driver starts easing the throttle [TEST VALUE]
        public float featherRangeDeg = 10f;    // ...and is fully off by start + this [TEST VALUE]
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
            var tr = transform;
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
            if (kart.Body.isKinematic) { wasOutside = outside; breachedNow = false; } // being moved by the crew: not racing
            if (outside && !kart.Body.isKinematic)
            {
                if (!wasOutside) { if (d > 0f && barrierHere) { bandBreaches++; breachedNow = true; } else breachedNow = false; }
                if (breachedNow) bandBreachSteps++; else offTrackSteps++;
            }
            else breachedNow = false;
            wasOutside = outside;
            wallGapNow = barrierHere ? WallGap(fr) : float.PositiveInfinity;
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

            // --- pure pursuit
            lineHint = track.NearestLineIndex(p2, lineHint);
            float v = Mathf.Max(0f, kart.ForwardSpeed);
            float ld = Mathf.Max(cfg.minLookahead, v * cfg.lookaheadTime);
            int ti = track.LineAdvance(lineHint, ld);
            float ts = track.LineS(ti);
            var tf = track.Frame(ts);
            // Never aim through a wall (spec §7): keep the target where the whole kart fits on the dirt.
            trafficOffset += Mathf.Clamp(trafficGoal - trafficOffset, -trafficRate * dt, trafficRate * dt);
            // personal offset fades out through the corner: everyone runs the optimal line there (Franklin: the line is
            // at grip's edge — entering low on purpose costs too much momentum unless you're passing)
            float off = cfg.lateralOffset * (1f - tf.corner) + DriveOverOffset(ti) + trafficOffset;
            float dWant = formation ? laneFrac * tf.halfWidth : tf.LineOffset + off;
            if (formation && grooveLane != NoGrooveLane)
            {
                if (grooveLane == 0) dWant = tf.LineOffset;
                else
                {   // the pair straddles the groove, slid down where the groove runs near the wall (both karts on the dirt)
                    float lo = LowLimit(tf) + grooveLaneHalf, hi = HighLimit(tf) - grooveLaneHalf;
                    float pc = lo < hi ? Mathf.Clamp(tf.LineOffset, lo, hi) : tf.LineOffset;
                    dWant = pc + grooveLane * grooveLaneHalf;
                }
            }
            if (recovering) dWant = Mathf.Min(d, tf.LineOffset - 1.0f); // rejoin low, off the groove, out of the way
            float dT = Mathf.Clamp(dWant, LowLimit(tf), HighLimit(tf));
            // Occupancy as hard lane limits on the STEERING TARGET (karts alongside own their slots).
            if (latMin <= latMax) dT = Mathf.Clamp(dT, latMin, latMax);
            else dT = d; // squeezed from both sides: hold my line
            var target = tf.pos + tf.outN * dT;

            var rear = tr.TransformPoint(0f, 0f, -kart.setup.wheelbase * 0.5f);
            var toT = new Vector3(target.x - rear.x, 0f, target.y - rear.z);
            var local = tr.InverseTransformDirection(toT);
            float alpha = Mathf.Atan2(local.x, local.z);
            float dist = Mathf.Max(0.5f, new Vector2(local.x, local.z).magnitude);
            float kappa = 2f * Mathf.Sin(alpha) / dist;               // pure-pursuit path curvature
            float steerRad = Mathf.Atan(kart.setup.wheelbase * kappa);
            // Yaw-rate damping: steer against yaw rate the pursuit arc doesn't ask for.
            float yawRate = Vector3.Dot(kart.Body.angularVelocity, tr.up);
            steerRad += cfg.yawDamping * (v * kappa - yawRate);
            kart.steerDeg = Mathf.Clamp(steerRad * Mathf.Rad2Deg, -kart.setup.maxSteerDeg, kart.setup.maxSteerDeg);
            if (steerOverride) kart.steerDeg = steerOverrideDeg; // debug/test only: a driver error

            // Speed is read further ahead (spec: ≈0.6 s) so braking anticipates the corner.
            var tfSteer = tf; int si0 = ti;
            if (cfg.speedLookaheadTime != cfg.lookaheadTime)
            {
                int si = track.LineAdvance(lineHint, Mathf.Max(cfg.minLookahead, v * cfg.speedLookaheadTime));
                tf = track.Frame(track.LineS(si)); si0 = si;
            }

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
            targetSpeed = paceMode ? Mathf.Lerp(paceStraight, paceCorner, tf.corner) : freeSpeed;
            if (diving && !paceMode) targetSpeed *= divingSpeedFactor;
            if (!paceMode) targetSpeed += pushedBoost;
            if (recovering) targetSpeed = recoverySpeed;
            targetSpeed = Mathf.Min(targetSpeed, speedCap);
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
        }

        float WallGap(in TrackFrame fr)
        {
            // Outermost body corner's lateral distance to the outside edge (wall face).
            // Outermost point of the plan-view footprint (nerf bars / bumper ends) to the outside edge.
            var st = kart.setup;
            float best = float.PositiveInfinity;
            foreach (var corner in new[] {
                new Vector3(st.FootRight, 0.1f, st.wheelbase * 0.5f), new Vector3(st.FootRight, 0.1f, -st.wheelbase * 0.5f),
                new Vector3(-st.FootLeft, 0.1f, st.wheelbase * 0.5f), new Vector3(-st.FootLeft, 0.1f, -st.wheelbase * 0.5f),
                new Vector3(st.FootRight, 0.1f, st.FootFront), new Vector3(-st.FootLeft, 0.1f, st.FootFront),
                new Vector3(st.rearBumperHalfWidthIn * U.In, 0.1f, -st.FootRear), new Vector3(-st.rearBumperHalfWidthIn * U.In, 0.1f, -st.FootRear) })
            {
                var w = transform.TransformPoint(corner);
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
