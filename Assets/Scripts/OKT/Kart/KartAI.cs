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
            if (outside)
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

            // --- pure pursuit
            lineHint = track.NearestLineIndex(p2, lineHint);
            float v = Mathf.Max(0f, kart.ForwardSpeed);
            float ld = Mathf.Max(cfg.minLookahead, v * cfg.lookaheadTime);
            int ti = track.LineAdvance(lineHint, ld);
            float ts = track.LineS(ti);
            var tf = track.Frame(ts);
            // Never aim through a wall (spec §7): keep the target where the whole kart fits on the dirt.
            float off = cfg.lateralOffset + DriveOverOffset(ti);
            float dT = Mathf.Clamp(tf.LineOffset + off, -tf.halfWidth + kart.setup.FootLeft + 2f * U.In, tf.halfWidth - kart.setup.FootRight - 2f * U.In);
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
            if (cfg.speedLookaheadTime != cfg.lookaheadTime)
            {
                int si = track.LineAdvance(lineHint, Mathf.Max(cfg.minLookahead, v * cfg.speedLookaheadTime));
                tf = track.Frame(track.LineS(si));
            }

            // --- speed: corner profile read at the lookahead point
            targetSpeed = cfg.vMax * cfg.pace * (1f - cfg.cornerSlow * tf.corner);
            kart.accelCmd = (targetSpeed - kart.ForwardSpeed) * cfg.speedGain;
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
                new Vector3(st.frontBumperHalfWidthIn * U.In, 0.1f, st.FootFront), new Vector3(-st.frontBumperHalfWidthIn * U.In, 0.1f, st.FootFront),
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
