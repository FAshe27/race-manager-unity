// Every kart value in one place, each tagged with its source, AUTHORED IN
// IMPERIAL (Franklin, 2026-11-14). The SI properties below are what the
// physics reads; nothing else in the code should hold a kart number.
// Franklin = his ruling (2026-11-14). Spec = UNITY-SPIKE.md. [TEST VALUE] = tune it.
// Setup values (weight split, left %) will be player-adjustable later (Franklin).
using System;
using UnityEngine;

namespace OKT
{
    public enum Corner { LF = 0, RF = 1, LR = 2, RR = 3 }

    [Serializable]
    public class KartSetup
    {
        // ---- weight (spec §6.1: Clone Medium, kart + driver, 159 kg)
        public float weightLb = 350.5f;
        // Book (From Beginner to Winner, Ch 27 worked Clone Medium session), adopted by Franklin 2026-11-14:
        // 45.7% nose, 59.4% left, 64.9% crossweight. Player-adjustable later.
        public float frontWeightFrac = 0.457f;
        public float leftWeightFrac = 0.594f;
        public float crossweightFrac = 0.65f;     // (RF + LR) / total, set by spindle height (Ch 9/27)
        public float comHeightIn = 7.1f;          // spec ≈0.18 m [TEST VALUE] (Franklin: "decent enough")

        // ---- geometry (spec §6.2; Franklin: "sounds decent enough")
        public float wheelbaseIn = 41.3f;         // 1.05 m
        public float trackFrontIn = 32.0f;        // Franklin's official width (42 in overall): front scaled with the rear (was 39.4) [TEST VALUE split]
        public float trackRearIn = 34.0f;         // Franklin (11-16, measured with the body on): 42 in overall -> 41.5 in over the rear tires (panels 1/4 in outside) - (6 + 9)/2 in of tire = 34 in centre to centre (was 42 = WKA's 50 in cap)
        // ---- chassis shape (Franklin, 2026-11-14, with photos; body not mounted).
        // Collider = frame/cockpit box + ROUND TUBES for the bumpers and nerf bars (a round tube
        // rides up a dirt slope; the old single box's square corner dug into the berm).
        public float frameClearanceIn = 0.5f;          // Franklin: frame rails ~1/2 in off the ground
        public float tubeDiameterIn = 1.0f;            // bumper / nerf tubing [TEST VALUE]
        public float tubeBottomIn = 0.5f;              // Franklin: bumpers and nerfs ~1/2 in off the ground
        public float cockpitHalfWidthIn = 12f;      // frame rails ~23.5 in apart (road-course frame photo, 11-16); was 15.75, wider than the 34 in rear track allows      // frame rails + seat + engine, between the wheels [TEST VALUE, from photos]
        public float cockpitFrontIn = 23.6f;           // ahead of centre [TEST VALUE]
        public float cockpitRearIn = 23.6f;            // behind centre [TEST VALUE]
        public float cockpitTopIn = 15.4f;             // seat back / engine height [TEST VALUE]
        public float frontBumperAheadIn = 10f;         // inside the nose; WKA 401.6 length cap (74 in) forced it in from Franklin's ~14 [TEST VALUE]
        public float frontBumperHalfWidthIn = 13f;     // [TEST VALUE, from photos]
        public float frontBumperTopIn = 7.75f;         // top of the upper hoop: WKA 409.1 minimum 7.75 in
        public float nerfOutsideTireIn = 0.125f;       // Franklin: 1/8 in outside the tires' outer edge
        public float nerfTopIn = 8f;                   // top tube height [TEST VALUE, from photos]
        public float rearBumperBehindIn = 4f;          // Franklin's body mockup (11-16): close behind the rear tires (was 8)          // WKA 401.6 length cap (74 in) with the nose: was Franklin's ~12 [TEST VALUE, split of the 74 in]
        public float rearBumperHalfWidthIn = 14.5f;    // Franklin's mockup: ~29 in across, narrower than the tires (was 19.7)    // [TEST VALUE, from photos]
        // ---- body (Franklin, Pit Board 2026-11-16; WKA Dirt rulebook 2025 §404). Fiberglass; "generally they slide
        // pretty freely against each other". In side-by-side contact the panels are what touch.
        public bool bodyOn = true;
        public float panelOutsideTireIn = 0.25f;     // Franklin: outside edge of the tires tucked in ~1/4 in
        public float panelBottomIn = 0.5f;           // Franklin: ~1/2 in off the ground
        public float panelTopIn = 14f;               // WKA 404.7: no higher than 14 in (Franklin guessed 15; measuring) [TEST VALUE]
        public float panelThickIn = 0.75f;           // collider thickness (the shell is thin; thicker keeps the solver honest) [TEST VALUE]
        public float noseAheadOfTiresIn = 17f;       // Franklin's mockup: tip a bit further out; keeps 73 in overall with the 4 in rear bumper (was 13)       // WKA 401.6: overall length 74 in max -> 73.1 in with the rear bumper 8 in back (Franklin's ~2 ft overshot the cap) [TEST VALUE, split of the 74 in]
        public float noseBottomIn = 2f;              // [TEST VALUE]
        public float noseTopIn = 10f;                // WKA 404.4: nose 8-17 in tall [TEST VALUE]
        // Rounded nose (Franklin, 11-16: "the actual exterior contact points are exceptionally important"). From his
        // body photos the nose piece is a wide U in plan: big round front corners blending back into the sides. [TEST VALUEs]
        public float noseCornerRadiusIn = 21f;       // Franklin's body mockup (11-16): one round arch across the whole front ->
                                                     // clamps to the half width (x) and the nose depth (z): an elliptical front
        public float noseBottomRoundIn = 0f;         // lower front edge: square. A 2 in roll made a ramp -- the nose climbed the
                                                     // 3 in berm and the kart swung to 88 deg in the M1 berm ride (11-16)
        public float noseTopRoundIn = 3f;            // and the upper front edge
        public float bodyFriction = 0.15f;           // fiberglass on fiberglass, slides freely [TEST VALUE]
        public float rearBumperTopIn = 7.5f;           // top of the bumper: WKA 409.3.1 max 7.5 in; lowest bar no lower than the bottom of the rear axle (409.3.2)
        public float rearAxleDiaIn = 1.0f;             // WKA: 1 in min (40 mm max) [TEST VALUE]

        // ---- tires. Sizes (Franklin): LF 10.5x4.50-6, LR 11x6.00-6, RF/RR 12x9.00-6 — nominal labels.
        // ROLLING size = measured circumference at race pressure (book Ch 25/28, 6/5 psi): rights 34",
        // LR 32-1/4" (1-3/4" rear stagger — Sanders: 1-1/2"..2" for an 11–13 tooth Clone driver),
        // LF 32-1/2" (1-1/2" front stagger, the book's norm). Adopted by Franklin 2026-11-14.
        public string[] tireSize = { "10.5x4.50-6", "12x9.00-6", "11x6.00-6", "12x9.00-6" }; // LF, RF, LR, RR
        public float[] tireCircumferenceIn = { 32.5f, 34f, 32.25f, 34f };
        public float[] tireWidthIn = { 4.50f, 9.00f, 6.00f, 9.00f };

        // ---- "suspension" = tire sidewall + chassis flex. Franklin (2026-11-14, after the chassis
        // test): corner squat is "barely at all" — very slight sidewall give; the frame drags only
        // very rarely; inside-rear lift is rare (a bad-setup symptom).
        public float travelIn = 1.0f;             // [TEST VALUE]
        public float staticSagIn = 0.15f;         // at rest; ~0.25–0.3 in on the loaded side mid-corner [TEST VALUE]
        public float dampingRatio = 0.4f;         // [TEST VALUE]
        public float bumpStopScale = 10f;         // spring multiplier past full travel [TEST VALUE]

        // ---- grip (Franklin: 1.3 g peak on the groove; slides past it)
        public float gripG = 1.3f;
        // Solver gain, not a tire fact: share of a wheel's sideways slip cancelled per physics step.
        public float lateralCancelGain = 0.5f;
        // Past the peak a sliding tire loses grip (Franklin, 11-16: a kart rotated far enough can't be pulled back by the
        // front tires). Full grip up to slideFadeStartDeg of slip, easing to slideGripRatio by slideFadeEndDeg. [TEST VALUE]
        public static float DefaultSlideGripRatio = 0.72f;  // soak 11-17: 0.85 changed nothing, 0.75 from 8 deg spun karts on exit
        public float slideGripRatio = DefaultSlideGripRatio;
        public static float DefaultSlideFadeStartDeg = 12f, DefaultSlideFadeEndDeg = 30f; // past normal exit slides (soak 11-17: 8 deg spun karts on exit)
        public float slideFadeStartDeg = DefaultSlideFadeStartDeg, slideFadeEndDeg = DefaultSlideFadeEndDeg;

        // ---- engine & drive. Replaces spec §6.2's flat 4.5 m/s² cap so acceleration fades with
        // speed (momentum matters). Karts brake on the rear axle only.
        // Clone: 17 hp at the crank (Franklin: 16–17 tonight, 17–18 on 2026-11-13 — the overlap).
        // Target rpm 6,900 (book Ch 24/32, the engine builder's number); strongest just under it
        // (Franklin: "pulls more strongly" near the top); past it the engine is "gear bound" (Ch 24)
        // and stops making power. Curve shape [TEST VALUE].
        public float crankHp = 17f;
        public float drivetrainEfficiency = 0.90f;   // clutch + chain [TEST VALUE]
        public float targetRpm = 6900f;
        public float clutchEngageRpm = 2400f;        // centrifugal clutch lock-up [TEST VALUE]
        public float[] powerCurveRpm = { 2000f, 3000f, 4000f, 5000f, 6000f, 6550f, 6900f, 7150f, 7400f, 7600f };
        public float[] powerCurveFrac = { 0.40f, 0.55f, 0.68f, 0.80f, 0.92f, 1.00f, 1.00f, 0.80f, 0.45f, 0.0f };
        // Gearing (Franklin): 12-tooth clutch driver, 63-tooth axle (book Ch 24: ratio = axle / clutch).
        public int clutchTeeth = 12;
        public int axleTeeth = 63;
        public float gearRatio => (float)axleTeeth / clutchTeeth;
        public float rollingResistance = 0.030f;     // dirt, fraction of weight [TEST VALUE]
        public float dragAreaSqFt = 6.5f;            // Cd x frontal area, kart + driver [TEST VALUE]
        public float brakeCapG = 0.82f;              // spec 8 m/s² [TEST VALUE]
        public float maxSteerDeg = 28f;

        // ---- physics body (spec §3 [TEST VALUE])
        public float maxAngularVelocity = 25f;    // rad/s
        public int solverIterations = 4;          // position
        public int solverVelocityIterations = 8;  // velocity
        public float maxTireForceLbf = 900f;        // ceiling on one tire's spring force (~10x its static load) [TEST VALUE]
        public float maxTireForceN => maxTireForceLbf * 4.4482216f;
        public float tireContactFriction = 0.35f;    // tire body vs kart/rail rubbing friction [TEST VALUE]
        public float maxDepenetrationVelocity = 1.5f; // m/s, solver push-apart cap (Unity default 10) [TEST VALUE]

        // ================= SI (engine) values =================
        public float massKg => weightLb * U.Lb;
        public float comHeight => comHeightIn * U.In;
        public float wheelbase => wheelbaseIn * U.In;
        public float trackFront => trackFrontIn * U.In;
        public float trackRear => trackRearIn * U.In;
        public float TubeRadius => tubeDiameterIn * 0.5f * U.In;
        public float TubeBottomCentre => tubeBottomIn * U.In + TubeRadius;
        public float frameClearance => frameClearanceIn * U.In;

        /// <summary>Outer edge of the tires on one side (m from centre).</summary>
        public float TireOuter(bool right) => frozen ? (right ? fTireOutR : fTireOutL) : TireOuterCalc(right);
        float TireOuterCalc(bool right) => right
            ? Mathf.Max(Mathf.Abs(WheelMount(Corner.RF).x) + TireWidth(1) * 0.5f, Mathf.Abs(WheelMount(Corner.RR).x) + TireWidth(3) * 0.5f)
            : Mathf.Max(Mathf.Abs(WheelMount(Corner.LF).x) + TireWidth(0) * 0.5f, Mathf.Abs(WheelMount(Corner.LR).x) + TireWidth(2) * 0.5f);
        /// <summary>Nerf bar tube centre x (m, + = right).</summary>
        public float NerfX(bool right) => (TireOuter(right) + nerfOutsideTireIn * U.In - TubeRadius) * (right ? 1f : -1f);
        public float FrontTireLead => wheelbase * 0.5f + Mathf.Max(WheelRadius(0), WheelRadius(1));
        public float RearTireTrail => wheelbase * 0.5f + Mathf.Max(WheelRadius(2), WheelRadius(3));
        public float FrontBumperZ => FrontTireLead + frontBumperAheadIn * U.In - TubeRadius;
        public float RearBumperZ => -(RearTireTrail + rearBumperBehindIn * U.In - TubeRadius);
        /// <summary>Rear bumper low bar centre height: its bottom level with the bottom of the rear axle (WKA 409.3.2).</summary>
        public float RearBumperLowCentre => RearRadius - rearAxleDiaIn * 0.5f * U.In + TubeRadius;

        // Plan-view footprint (outer surfaces) — walls, AI clearance, four-wide checks.
        /// <summary>Outer face of the side panel (m from centre).</summary>
        public float PanelOuter(bool right) => TireOuter(right) + panelOutsideTireIn * U.In;
        /// <summary>Front face of the nose (m ahead of centre).</summary>
        public float NoseFrontZ => FrontTireLead + noseAheadOfTiresIn * U.In;
        public float FootRight => frozen ? fRight : bodyOn ? Mathf.Max(NerfX(true) + TubeRadius, PanelOuter(true)) : NerfX(true) + TubeRadius;
        public float FootLeft => frozen ? fLeft : bodyOn ? Mathf.Max(-NerfX(false) + TubeRadius, PanelOuter(false)) : -NerfX(false) + TubeRadius;
        public float FootFront => frozen ? fFront : bodyOn ? Mathf.Max(FrontBumperZ + TubeRadius, NoseFrontZ) : FrontBumperZ + TubeRadius;
        public float FootRear => frozen ? fRear : -RearBumperZ + TubeRadius;

        // The plan-view footprint is worked out once, when the kart's body is built from these dimensions (KartFactory):
        // the colliders are made from the same numbers, so they can't change afterwards. Each Foot* was a chain of
        // wheel-mount / tire / nerf / panel maths, asked hundreds of times a step by the drivers and the director
        // (perf pass 2026-11-17: about a third of the driver time at 40 karts).
        [System.NonSerialized] bool frozen; float fRight, fLeft, fFront, fRear, fTireOutR, fTireOutL;
        public void FreezeFootprint()
        {
            frozen = false;
            fTireOutR = TireOuterCalc(true); fTireOutL = TireOuterCalc(false);
            fRight = FootRight; fLeft = FootLeft; fFront = FootFront; fRear = FootRear;
            frozen = true;
        }
        public float FootWidth => FootLeft + FootRight;
        public float WheelRadius(int i) => tireCircumferenceIn[i] / (2f * Mathf.PI) * U.In;
        public float TireWidth(int i) => tireWidthIn[i] * U.In;
        public float travel => travelIn * U.In;
        public float staticSag => staticSagIn * U.In;
        public float gripMu => gripG;
        public float brakeCap => brakeCapG * U.G;
        public float dragArea => dragAreaSqFt * U.Ft * U.Ft;  // m²

        /// <summary>Drive radius of the live rear axle: mean of the two rear tires.</summary>
        public float RearRadius => (WheelRadius(2) + WheelRadius(3)) * 0.5f;

        /// <summary>Engine rpm at forward speed v (m/s); the clutch slips below lock-up.</summary>
        public float EngineRpmAt(float v)
        {
            float wheelRpm = Mathf.Max(0f, v) / RearRadius * 60f / (2f * Mathf.PI);
            return Mathf.Max(clutchEngageRpm, wheelRpm * gearRatio);
        }

        /// <summary>Fraction of peak power at rpm (zero past the end of the curve).</summary>
        public float PowerFrac(float rpm) => rpm >= powerCurveRpm[powerCurveRpm.Length - 1] ? 0f : Interp(rpm);

        float Interp(float rpm)
        {
            var x = powerCurveRpm; var y = powerCurveFrac;
            if (rpm <= x[0]) return y[0] * rpm / x[0];
            for (int i = 0; i < x.Length - 1; i++)
                if (rpm <= x[i + 1]) return Mathf.Lerp(y[i], y[i + 1], (rpm - x[i]) / (x[i + 1] - x[i]));
            return y[y.Length - 1];
        }

        /// <summary>Most drive force (N) the engine can put to the rear tires at forward speed v.</summary>
        public float EngineForceAt(float v)
        {
            float rpm = EngineRpmAt(v);
            float watts = crankHp * 745.7f * drivetrainEfficiency * PowerFrac(rpm);
            float omega = rpm * 2f * Mathf.PI / 60f;
            float torque = watts / Mathf.Max(1f, omega);
            return torque * gearRatio / RearRadius;
        }

        /// <summary>Speed (m/s) at the target rpm with this gearing.</summary>
        public float SpeedAtTarget => targetRpm / gearRatio * 2f * Mathf.PI / 60f * RearRadius;

        public float CornerMass => massKg / 4f;
        public float SpringRate => massKg * 9.81f / 4f / staticSag;          // N/m
        public float DamperRate => 2f * dampingRatio * Mathf.Sqrt(SpringRate * CornerMass);

        public float MeanRadius => (WheelRadius(0) + WheelRadius(1) + WheelRadius(2) + WheelRadius(3)) / 4f;
        /// <summary>Height of the (flat) frame's wheel mounts above ground at static ride, for the mean tire.</summary>
        public float MountHeight => MeanRadius + travel - staticSag;

        public Vector3 WheelMount(Corner c)
        {
            bool front = c == Corner.LF || c == Corner.RF;
            bool left = c == Corner.LF || c == Corner.LR;
            float halfTrack = (front ? trackFront : trackRear) * 0.5f;
            // Each spindle sits at its own tire's height so all four tires carry load at rest (a real
            // chassis flexes to keep them planted; a rigid flat frame on 10.5/11/12 in tires with stiff
            // sidewalls would sit on three wheels).
            // Crossweight (book Ch 9/27): lowering a spindle loads that corner AND its diagonal.
            // RF/LR mounts drop by the warp offset, LF/RR rise by it.
            float warp = CrossWarpOffset();
            float diag = (c == Corner.RF || c == Corner.LR) ? -warp : warp;
            return new Vector3(left ? -halfTrack : halfTrack, WheelRadius((int)c) + travel - staticSag + diag, front ? wheelbase * 0.5f : -wheelbase * 0.5f);
        }

        /// <summary>Corner (x, z) positions, LF RF LR RR, for the static load solve.</summary>
        Vector2 CornerXZ(Corner c)
        {
            bool front = c == Corner.LF || c == Corner.RF, left = c == Corner.LF || c == Corner.LR;
            float ht = (front ? trackFront : trackRear) * 0.5f;
            return new Vector2(left ? -ht : ht, front ? wheelbase * 0.5f : -wheelbase * 0.5f);
        }

        /// <summary>
        /// Static corner loads (N) on level scales for equal springs with NO crossweight warp:
        /// the planar solution N_i = a + b x_i + c z_i satisfying total weight and both moments.
        /// </summary>
        public float[] BaseCornerLoads()
        {
            var com = CenterOfMass(); float W = massKg * 9.81f;
            double[,] A = new double[3, 3]; double[] rhs = { W, W * com.x, W * com.z };
            for (int i = 0; i < 4; i++)
            {
                var p = CornerXZ((Corner)i); double[] r = { 1, p.x, p.y };
                for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) A[a, b] += r[a] * r[b];
            }
            var sol = Solve3(A, rhs);
            var n = new float[4];
            for (int i = 0; i < 4; i++) { var p = CornerXZ((Corner)i); n[i] = (float)(sol[0] + sol[1] * p.x + sol[2] * p.y); }
            return n;
        }

        /// <summary>Base crossweight (RF + LR) / total with no warp.</summary>
        public float BaseCrossweight { get { var n = BaseCornerLoads(); return (n[1] + n[2]) / (n[0] + n[1] + n[2] + n[3]); } }

        /// <summary>Spindle-height warp (m) that moves crossweight from its base value to crossweightFrac.</summary>
        public float CrossWarpOffset()
        {
            float W = massKg * 9.81f;
            float dN = (crossweightFrac - BaseCrossweight) * W * 0.5f; // per diagonal corner
            return dN / SpringRate;
        }

        static double[] Solve3(double[,] m, double[] b)
        {
            var a = new double[3, 4];
            for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) a[i, j] = m[i, j]; a[i, 3] = b[i]; }
            for (int c = 0; c < 3; c++)
            {
                int p = c; for (int r = c + 1; r < 3; r++) if (System.Math.Abs(a[r, c]) > System.Math.Abs(a[p, c])) p = r;
                for (int j = 0; j < 4; j++) { var t = a[c, j]; a[c, j] = a[p, j]; a[p, j] = t; }
                for (int r = 0; r < 3; r++) if (r != c) { var f = a[r, c] / a[c, c]; for (int j = 0; j < 4; j++) a[r, j] -= f * a[c, j]; }
            }
            return new[] { a[0, 3] / a[0, 0], a[1, 3] / a[1, 1], a[2, 3] / a[2, 2] };
        }

        /// <summary>Centre of mass in kart local space (origin = ground under the frame centre).</summary>
        public Vector3 CenterOfMass()
        {
            float z = -wheelbase * 0.5f + frontWeightFrac * wheelbase;           // along the wheelbase
            float meanHalf = (trackFront + trackRear) * 0.25f;
            float x = -(leftWeightFrac - 0.5f) * 2f * meanHalf;                  // left = -x
            return new Vector3(x, comHeight, z);
        }
    }
}
