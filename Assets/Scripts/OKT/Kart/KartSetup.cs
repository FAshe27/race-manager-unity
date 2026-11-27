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
        // Franklin's kart measured 2026-11-21 (Pit Board + garage notes). Wheelbase: front axle centre to rear axle centre.
        public float wheelbaseIn = 39.75f;        // was 41.3
        // Each wheel centre from the kart centre (in, + = right). Franklin 11-21: kingpins 24 in apart, each lined up with its
        // tire's inside sidewall; fronts 42-1/4 in and rears 42 in outside to outside (exact); the chassis is asymmetric --
        // RR ~1 in right of the RF, LF ~1/2 in left of the LR (his estimates, split to fit the exact widths: 5/8 and 7/8).
        // Tire widths are the stretched outer widths below (LF 7, RF 10.5, LR 8.75, RR 10.5).
        public float[] wheelXIn = { -15.875f, 17.625f, -14.125f, 18.25f };   // LF, RF, LR, RR
        public float frontLeadIn = 0.375f;        // Franklin 11-21: the LF sits 3/8 in behind the RF
        public float trackFrontIn => wheelXIn[1] - wheelXIn[0];   // centre to centre (33.5 in)
        // Franklin (11-16, measured with the body on): 42 in overall -> 41.5 in over the rear tires. Taking the tires as
        // their nominal 6 and 9 in gave 34 in centre to centre; on his 8.25 and 10 in rims they stretch to ~8.75 and 10.5 in
        // (the Blender wheels, tireOuterWidthIn), so 41.5 - (8.75 + 10.5)/2 = 31.9 in, the model's rear track. At 34 the
        // left-rear stuck 1.7 in out past the body (Franklin 11-20, q39: tires tucked 1/8-1/4 in inside the body).
        public float trackRearIn => wheelXIn[3] - wheelXIn[2];    // 32.375 in
        // ---- chassis shape (Franklin, 2026-11-14, with photos; body not mounted).
        // Collider = frame/cockpit box + ROUND TUBES for the bumpers and nerf bars (a round tube
        // rides up a dirt slope; the old single box's square corner dug into the berm).
        public float frameClearanceIn = 1.875f;        // Franklin 11-21 measured: lowest part of the chassis (the seat's underside) 1-7/8 in
        public float tubeDiameterIn = 1.0f;            // bumper / nerf tubing [TEST VALUE]
        public float tubeBottomIn = 2.0f;              // Franklin 11-21 measured: rear bumper's lowest bar 2 in (was 1/2)
        public float cockpitHalfWidthIn = 12f;      // frame rails ~23.5 in apart (road-course frame photo, 11-16); was 15.75, wider than the 34 in rear track allows      // frame rails + seat + engine, between the wheels [TEST VALUE, from photos]
        public float cockpitFrontIn = 23.6f;           // ahead of centre [TEST VALUE]
        public float cockpitRearIn = 23.6f;            // behind centre [TEST VALUE]
        public float cockpitTopIn = 15.4f;             // seat back / engine height [TEST VALUE]
        // ---- the driver (2026-11-17 night). Without him the kart was a 42 x 15 in brick: on its side it sat on a flat
        // 15 in face and every rollover stopped dead at 90 deg (Franklin: "think of a Hot Wheels car getting tossed on
        // its side -- inertia would carry it up and over"). Seated in a Clone, the shoulders and helmet stand ~40 in
        // up, so on its side the kart lies on a long slanted face from the tires to the helmet and can roll on over
        // the helmet onto its back. Shape only: the weight, balance and handling are the kart's as before. [TEST VALUEs]
        public static bool DefaultDriverBody = true;
        public bool driverBody = DefaultDriverBody;
        // Franklin 11-19 (race photos): the driver was "super tall and sitting upright" -- he lies well back: seat low,
        // shoulders back, knees up by the wheel, feet out at the pedals near the nose. His garage and overhead photos
        // (11-19): seat well left of centre (engine on the right), helmet ~10-18 in ahead of the rear axle. Sized for the
        // driver in those photos, 6 ft 1 in: torso ~19 in hip to shoulder, thigh ~18 in, shin ~20 in.
        public float driverHipZIn = 0f;               // hips, ahead (+) / behind (-) the kart centre (rear axle -20.65)
        public float driverHipYIn = 7.5f;
        public float driverShoulderYIn = 21f;           // Franklin 11-19 side photo: helmet top ~34 in, shoulders ~21 in, knees ~17 in
        public float driverLeanBackIn = 9f;            // shoulders this far behind the hips (~45 deg recline)
        public float helmetAheadIn = 3f;               // helmet centre this far ahead of the shoulders (head up, looking forward)
        public float driverTorsoRadiusIn = 5f;           // a 10 in deep torso (13 in looked "very very tall")
        public float helmetRadiusIn = 5.5f;
        public float helmetYIn = 28f;                  // centre; top of helmet ~33.5 in (was 40.5)
        public float driverKneeYIn = 17f, driverKneeZIn = 12f;  // knees up above the 14 in body top, by the wheel
        public float driverFootYIn = 6f, driverFootZIn = 30f;   // feet on the pedals, ~9 in ahead of the front axle
        public float driverLegRadiusIn = 2.5f, driverLegSpreadIn = 4f; // each leg this far either side of its line
        // shoulders and arms (his overhead photo: the driver fills the cockpit, elbows out, hands on the wheel grips)
        public float driverShoulderHalfWidthIn = 5.5f;  // two 10 in torso halves this far either side -> ~21 in across (+ arms ~23, shop overhead photo)
        public float driverHipHalfWidthIn = 1.5f;       // ...and only this far apart at the seat (13 in across the hips)
        public float driverArmRadiusIn = 2f;
        public float driverHandHalfSpanIn = 6f, driverHandYIn = 20f, driverHandZIn = 3f; // grips: steering wheel ~12 in across, ~3 in ahead of centre
        public float driverElbowOutIn = 2.5f, driverElbowYIn = 16f;
        public float driverKneeLineLeftIn = 1.4f;      // knees and feet straddle the steering column (Franklin: the shaft went through a leg)
        public float driverLeanLeftIn = 2f;            // shoulders and helmet lean this much further left than the seat (helmet ~9 in left, shop overhead photo)
        public float driverOffsetLeftIn = 1.5f; // Franklin 11-22: 3 in further right, beside the engine on its rails. was 7:         // seat well left of centre on an offset dirt chassis (engine on the right) [TEST VALUE]
        public float frontBumperAheadIn = 12f;  // Franklin 11-21 measured: 12 in ahead of the front tires. was 10:        // inside the nose; WKA 401.6 length cap (74 in) forced it in from Franklin's ~14 [TEST VALUE]
        public float frontBumperHalfWidthIn = 13f;     // [TEST VALUE, from photos]
        public float frontBumperTopIn = 8f;            // Franklin 11-21 measured: top hoop 8 in (WKA 409.1 minimum 7.75 in)
        public float nerfOutsideTireIn = 0.125f;       // Franklin: 1/8 in outside the tires' outer edge
        public float nerfTopIn = 8f;                   // top tube height [TEST VALUE, from photos]
        public float rearBumperBehindIn = 6f;  // Franklin 11-21 measured: 6 in behind the rear tires. was 4:         // Franklin's body mockup (11-16): close behind the rear tires (was 8)          // WKA 401.6 length cap (74 in) with the nose: was Franklin's ~12 [TEST VALUE, split of the 74 in]
        public float rearBumperHalfWidthIn = 14.5f;    // Franklin's mockup: ~29 in across, narrower than the tires (was 19.7)    // [TEST VALUE, from photos]
        // ---- body (Franklin, Pit Board 2026-11-16; WKA Dirt rulebook 2025 §404). Fiberglass; "generally they slide
        // pretty freely against each other". In side-by-side contact the panels are what touch.
        public bool bodyOn = true;
        public float panelOutsideTireIn = 0.375f;    // Franklin 11-21: 43 in overall over 42-1/4 in of front tires
        public float panelBottomIn = 2f;             // Franklin 11-21 measured: 2 in off the ground
        public float panelTopIn = 12f;  // Franklin 11-21 measured 12 in. was 14:              // WKA 404.7: no higher than 14 in (Franklin guessed 15; measuring) [TEST VALUE]
        public float panelThickIn = 0.75f;           // collider thickness (the shell is thin; thicker keeps the solver honest) [TEST VALUE]
        public float noseAheadOfTiresIn = 15f;  // Franklin 11-21 measured 15 in. was 17:      // Franklin's mockup: tip a bit further out; keeps 73 in overall with the 4 in rear bumper (was 13)       // WKA 401.6: overall length 74 in max -> 73.1 in with the rear bumper 8 in back (Franklin's ~2 ft overshot the cap) [TEST VALUE, split of the 74 in]
        public float noseBottomIn = 2f;              // [TEST VALUE]
        public float noseTopIn = 15.25f;             // Franklin 11-21 measured: top of the nose at its front edge 15-1/4 in (WKA 404.4: 8-17)
        // Rounded nose (Franklin, 11-16: "the actual exterior contact points are exceptionally important"). From his
        // body photos the nose piece is a wide U in plan: big round front corners blending back into the sides. [TEST VALUEs]
        public float noseCornerRadiusIn = 21f;       // Franklin's body mockup (11-16): one round arch across the whole front ->
                                                     // clamps to the half width (x) and the nose depth (z): an elliptical front
        public float noseBottomRoundIn = 0f;         // lower front edge: square. A 2 in roll made a ramp -- the nose climbed the
                                                     // 3 in berm and the kart swung to 88 deg in the M1 berm ride (11-16)
        public float noseTopRoundIn = 3f;            // and the upper front edge
        public float bodyFriction = 0.15f;           // fiberglass on fiberglass, slides freely [TEST VALUE]
        public float rearBumperTopIn = 8f;             // Franklin 11-21 measured 8 in (WKA 409.3.1 says 7.5 max -- asked); lowest bar no lower than the bottom of the rear axle (409.3.2)
        public float rearAxleDiaIn = 1.0f;             // WKA: 1 in min (40 mm max) [TEST VALUE]

        // ---- tires. Sizes (Franklin): LF 10.5x4.50-6, LR 11x6.00-6, RF/RR 12x9.00-6 — nominal labels.
        // ROLLING size = measured circumference at race pressure (book Ch 25/28, 6/5 psi): rights 34",
        // LR 32-1/4" (1-3/4" rear stagger — Sanders: 1-1/2"..2" for an 11–13 tooth Clone driver),
        // LF 32-1/2" (1-1/2" front stagger, the book's norm). Adopted by Franklin 2026-11-14.
        public string[] tireSize = { "10.5x4.50-6", "12x9.00-6", "11x6.00-6", "12x9.00-6" }; // LF, RF, LR, RR
        public float[] tireCircumferenceIn = { 32.5f, 34f, 32.25f, 34f };
        public float[] tireWidthIn = { 4.50f, 9.00f, 6.00f, 9.00f };       // tread (the contact rays)
        // sidewall to sidewall, stretched on Franklin's rims (LF 6.5, LR 8.25, rights 10 in): the Blender wheels' width.
        // The tire's collider and the kart's outer edge use this.
        public float[] tireOuterWidthIn = { 7.0f, 10.5f, 8.75f, 10.5f };

        // ---- "suspension" = tire sidewall + chassis flex. Franklin (2026-11-14, after the chassis
        // test): corner squat is "barely at all" — very slight sidewall give; the frame drags only
        // very rarely; inside-rear lift is rare (a bad-setup symptom).
        public float travelIn = 1.0f;             // [TEST VALUE]
        public float staticSagIn = 0.15f;         // at rest; ~0.25–0.3 in on the loaded side mid-corner [TEST VALUE]
        public float dampingRatio = 0.4f;         // [TEST VALUE]
        public float bumpStopScale = 10f;         // spring multiplier past full travel [TEST VALUE]

        // ---- grip (Franklin: 1.3 g peak on the groove; slides past it)
        public float gripG = 1.08f;   // Franklin 11-26: a perfect lap should be ~10.5 s; the limit-lap test (M2LimitLap) gives 10.52 s here (1.3 g: 9.70 s, corners nearly flat)
        // Solver gain, not a tire fact: share of a wheel's sideways slip cancelled per physics step.
        public float lateralCancelGain = 0.5f;
        // Past the peak a sliding tire loses grip (Franklin, 11-16: a kart rotated far enough can't be pulled back by the
        // front tires). Full grip up to slideFadeStartDeg of slip, easing to slideGripRatio by slideFadeEndDeg. [TEST VALUE]
        public static float DefaultSlideGripRatio = 0.72f;  // soak 11-17: 0.85 changed nothing, 0.75 from 8 deg spun karts on exit
        public float slideGripRatio = DefaultSlideGripRatio;
        public static float DefaultSlideFadeStartDeg = 12f, DefaultSlideFadeEndDeg = 30f; // past normal exit slides (soak 11-17: 8 deg spun karts on exit)
        public float slideFadeStartDeg = DefaultSlideFadeStartDeg, slideFadeEndDeg = DefaultSlideFadeEndDeg;
        // Slip angle (Franklin 11-20: "we don't have any karts sliding at all"). A real tire only pushes sideways while it
        // runs at an angle to where it's rolling: the side force builds with the slip angle up to a peak, then holds
        // (and past slideFadeStartDeg fades, above). So a kart at the limit runs with its tires slipping, the body angled
        // to where it's going: a little for a dialed kart, ~10-15 deg for a loose one. Off = the old stiff tire, which
        // held any side load up to its cap with no slip at all. [soak knob]
        public static bool SlipCurve = true;    // with the solid axle it races clean (soak 11-20: 0.2-1.0 incidents/race over 12)
        public static float DefaultSlipPeakDeg = 8f;
        public float slipPeakDeg = DefaultSlipPeakDeg;   // tire slip angle at peak side grip, dirt [TEST VALUE]
        public float slipPastPeakDrop = 0.04f;           // side grip lost per peak-width of slip past the peak (to 10% at most) [TEST VALUE]
        // The kart's balance (Franklin 11-20: few karts are dialed in; more are loose than tight). Each axle's grip x this:
        // a loose kart has less at the rear, a tight one (push) less at the front. Set per kart by the director.
        public float frontGripScale = 1f, rearGripScale = 1f;
        public static float DialedRearGrip = 1.0f;   // rear grip of a dialed kart against its front (set up a touch free) [TEST VALUE]
        // Each front wheel's own angle from the steering linkage (Kart Parts/steering.py front_angles: 3 in pitman tabs,
        // ~5 in spindle arms, tie rods in the outer hole; kingpins 24 in apart, Franklin 11-21). The inside wheel turns further, more so near lock (Ackermann).
        // Mean front angle (deg, + = right) -> LF, RF. Off = both fronts at the mean (parallel steer, the old model).
        public static bool LinkageSteer = true;
        static readonly float[] AckMean = { -32.04f, -28.54f, -23.55f, -18.06f, -12.22f, -6.16f, -3.08f, 0f, 3.08f, 6.15f, 12.17f, 17.93f, 23.29f, 26.24f };
        static readonly float[] AckLF   = { -34.22f, -29.99f, -24.33f, -18.40f, -12.30f, -6.14f, -3.06f, 0f, 3.03f, 6.01f, 11.77f, 17.16f, 22.00f, 24.55f };
        static readonly float[] AckRF   = { -29.86f, -27.08f, -22.76f, -17.72f, -12.14f, -6.18f, -3.11f, 0f, 3.14f, 6.29f, 12.56f, 18.70f, 24.58f, 27.93f };
        /// <summary>Front wheel i's steer angle (deg) for a mean front angle; rears 0.</summary>
        public static float WheelSteer(int i, float mean)
        {
            if (i >= 2) return 0f;
            if (!LinkageSteer) return mean;
            var t = i == 0 ? AckLF : AckRF; int n = AckMean.Length;
            int k = 0; while (k < n - 2 && mean > AckMean[k + 1]) k++;
            float u = (mean - AckMean[k]) / (AckMean[k + 1] - AckMean[k]);   // extrapolates past the ends on the last span
            return Mathf.LerpUnclamped(t[k], t[k + 1], u);
        }
        public static bool SolidAxle = true;    // soak knob: off = drive split by load like an open diff (the old model)
        public float axleScrubStiffness = 10f;  // rear tire force per unit load per unit slip ratio, dirt [TEST VALUE]
        public float selfAlignGain = 0.4f;   // caster: share of the fronts' slip angle the wheels swing back by against his hands [TEST VALUE]
        public static bool BalanceOn = true, BrakeLeavesSide = true;   // soak knob: off = every kart neutral

        // ---- engine & drive. Replaces spec §6.2's flat 4.5 m/s² cap so acceleration fades with
        // speed (momentum matters). Karts brake on the rear axle only.
        // Clone: 17 hp at the crank (Franklin: 16–17 tonight, 17–18 on 2026-11-13 — the overlap).
        // Gear bound at 7,050-7,100 on today's engines (Franklin 11-26; the book's Ch 24/32 6,900 is older);
        // strongest just under it (Franklin: "pulls more strongly" near the top); past it the engine is
        // "gear bound" (Ch 24) and stops making power. Curve shape [TEST VALUE].
        public float crankHp = 17f;
        public float drivetrainEfficiency = 0.90f;   // clutch + chain [TEST VALUE]
        public float targetRpm = 7075f;
        public float clutchEngageRpm = 2400f;        // centrifugal clutch lock-up [TEST VALUE]
        public float[] powerCurveRpm = { 2000f, 3000f, 4000f, 5000f, 6000f, 6725f, 7075f, 7325f, 7575f, 7775f };
        public float[] powerCurveFrac = { 0.40f, 0.55f, 0.68f, 0.80f, 0.92f, 1.00f, 1.00f, 0.80f, 0.45f, 0.0f };
        // Gearing: 12-tooth clutch driver, 55-tooth axle (book Ch 24: ratio = axle / clutch). Franklin 11-26: 12/55 is
        // what the #72 ran for the MyChron data (6,763 rpm at 48 mph -> 4.58:1 on 34" rights; was 12/63).
        public int clutchTeeth = 12;
        public int axleTeeth = 55;
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
            ? Mathf.Max(Mathf.Abs(WheelMount(Corner.RF).x) + TireOuterWidth(1) * 0.5f, Mathf.Abs(WheelMount(Corner.RR).x) + TireOuterWidth(3) * 0.5f)
            : Mathf.Max(Mathf.Abs(WheelMount(Corner.LF).x) + TireOuterWidth(0) * 0.5f, Mathf.Abs(WheelMount(Corner.LR).x) + TireOuterWidth(2) * 0.5f);
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
        /// <summary>The plan-view footprint from the kart's actual colliders (the body from Franklin's model, KartModel).</summary>
        public void SetFootprint(float front, float rear, float left, float right)
        {
            if (!frozen) FreezeFootprint();
            fFront = front; fRear = rear; fLeft = left; fRight = right;
        }
        // Rotational inertia (kg m2, principal axes) pinned when the body hitbox comes from the model: the collider shapes
        // decide how karts touch, not how they turn. These are what the primitive kart worked out (soak 11-19); known to be
        // on the high side for a real kart (yaw 52.6 vs ~20-30 estimated). [TEST VALUE]
        public Vector3 inertiaPin = new Vector3(43.13f, 52.64f, 12.74f);
        public Vector3 inertiaPinEuler = new Vector3(0.95f, 6.00f, 0.43f);
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
        public float TireOuterWidth(int i) => tireOuterWidthIn[i] * U.In;
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

        /// <summary>Wheel centre fore/aft (m, + = forward): the axles a wheelbase apart, the LF frontLeadIn behind the RF.</summary>
        public float AxleZ(Corner c) => c == Corner.LF ? (wheelbase - frontLeadIn * U.In) * 0.5f
                                      : c == Corner.RF ? (wheelbase + frontLeadIn * U.In) * 0.5f : -wheelbase * 0.5f;

        public Vector3 WheelMount(Corner c)
        {
            bool front = c == Corner.LF || c == Corner.RF;
            bool left = c == Corner.LF || c == Corner.LR;
            // Each spindle sits at its own tire's height so all four tires carry load at rest (a real
            // chassis flexes to keep them planted; a rigid flat frame on 10.5/11/12 in tires with stiff
            // sidewalls would sit on three wheels).
            // Crossweight (book Ch 9/27): lowering a spindle loads that corner AND its diagonal.
            // RF/LR mounts drop by the warp offset, LF/RR rise by it.
            float warp = CrossWarpOffset();
            float diag = (c == Corner.RF || c == Corner.LR) ? -warp : warp;
            return new Vector3(wheelXIn[(int)c] * U.In, WheelRadius((int)c) + travel - staticSag + diag, AxleZ(c));
        }

        /// <summary>Corner (x, z) positions, LF RF LR RR, for the static load solve.</summary>
        Vector2 CornerXZ(Corner c)
        {
            return new Vector2(wheelXIn[(int)c] * U.In, AxleZ(c));
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
            // left weight fraction on scales: the CoM sits that share of the way from the right wheels' line to the left's
            float xl = (wheelXIn[0] + wheelXIn[2]) * 0.5f * U.In, xr = (wheelXIn[1] + wheelXIn[3]) * 0.5f * U.In;
            float x = xr + (xl - xr) * leftWeightFrac;
            return new Vector3(x, comHeight, z);
        }
    }
}
