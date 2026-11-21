// Raycast-suspension kart (spec §6.2). NO WheelCollider. Four rays from the
// frame's wheel mounts; each grounded wheel applies a spring/damper force along
// the frame's up axis, a lateral grip force capped at mu * load (beyond the cap
// the tire slides — that's the dirt), and (rear only, live axle) drive/brake.
// When a ray finds no ground the wheel applies nothing: air, flips and launches
// fall out of the model. All forces go through the Rigidbody — never set pose.
using UnityEngine;

namespace OKT
{
    [RequireComponent(typeof(Rigidbody))]
    public class KartPhysics : MonoBehaviour
    {
        public KartSetup setup = new KartSetup();

        // Inputs (from the AI), set before each Step.
        [HideInInspector] public float steerDeg;
        /// <summary>Where the front wheels actually point (deg, + = right): the driver's steer plus the caster's pull
        /// toward where the fronts are travelling (Franklin 11-20: "the fronts always want to point forwards -- the slide
        /// can sometimes catch itself as the kart rotates under the driver"). Instant: no reaction time in it.</summary>
        public float SteerApplied { get => stepped ? steerApplied : steerDeg; private set => steerApplied = value; }
        float steerApplied; bool stepped;
        [HideInInspector] public float accelCmd; // m/s², + drive / - brake (one-pedal command: staging, cautions, tests)
        // Two pedals, 0..1 each, used instead of accelCmd when throttleCmd >= 0 (the driver sets them every step):
        // the gas pulls with what the engine makes at this speed, the brake holds the rear axle back, and both can be on
        // at once (Franklin 11-19: throttle full and the brake dragged, to slow a touch and stay in the powerband).
        [System.NonSerialized] public float throttleCmd = -1f, brakeCmd;

        // Telemetry (read-only)
        public readonly bool[] grounded = new bool[4];
        public readonly float[] compression = new float[4];
        public readonly float[] load = new float[4];
        public readonly bool[] sliding = new bool[4];
        public float airTime;        // current time with all four wheels off the ground
        public float maxAirTime;
        public float maxCompression;
        public readonly float[] surfaceGripNow = { 1f, 1f, 1f, 1f };
        /// <summary>Each tire's slip angle (deg, unsigned): the angle between where it points and where it's rolling.</summary>
        public readonly float[] slipDeg = new float[4];
        /// <summary>Signed slip angle (rad) of each axle's tires, averaged: + = the tire is sliding to its right.</summary>
        public float FrontSlipRad { get; private set; }
        public float RearSlipRad { get; private set; }
        readonly float[] slipSigned = new float[4];
        readonly float[] axleV = new float[4];
        /// <summary>Solid-axle scrub force on each rear tire last step (N, + = forward).</summary>
        public readonly float[] AxleScrub = new float[4];   // each tire's side force last step (N, signed)
        /// <summary>Berm dust on each tire, 0 = clean .. 1 = fully coated (Franklin: costs bite until it cleans off).</summary>
        public readonly float[] dust = new float[4];
        readonly float[] rollExtra = new float[4];   // extra rolling resistance from the ground each tire stood on last step (loose ridge)
        /// <summary>What the ground under a wheel is (set by World from the track model).</summary>
        public System.Func<RaycastHit, int, GroundSample> surfaceGrip;
        public SurfaceConfig surface; // dust behaviour
        public float ForwardSpeed { get; private set; }
        public float EngineRpm { get; private set; }
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public int GroundedCount { get; private set; }

        public Rigidbody Body { get; private set; }
        public Transform[] wheelVisuals = new Transform[4]; // optional, steer/spin pivots
        readonly RaycastHit[] hits = new RaycastHit[4];
        readonly float[] prevX = new float[4];
        readonly float[] spinAngle = new float[4];
        int rayMask;
        public const int KartLayer = 8, BarrierLayer = 9;
        public int RayMask => rayMask;

        // Batched wheel rays (World.StepOnce): 3 per wheel -- centre, then the two tread edges -- cast for every kart at
        // once at the start of the step (the bodies don't move until Physics.Simulate, so it's the same rays from the same
        // place). preTick says which step they belong to; anything else (a lone kart in a test) casts them itself.
        public const int RaysPerKart = 12;
        [System.NonSerialized] public RaycastHit[] preHits = new RaycastHit[RaysPerKart];
        [System.NonSerialized] public long preTick = -1;
        [System.NonSerialized] public long stepTick;
        public void FillRays(Unity.Collections.NativeArray<RaycastCommand> cmds, int at, QueryParameters qp)
        {
            var up = transform.up; var right = transform.right;
            for (int i = 0; i < 4; i++)
            {
                var mount = transform.TransformPoint(setup.WheelMount((Corner)i));
                float len = setup.travel + setup.WheelRadius(i);
                var side = right * (setup.TireWidth(i) * 0.45f);
                cmds[at + i * 3] = new RaycastCommand(mount, -up, qp, len);
                cmds[at + i * 3 + 1] = new RaycastCommand(mount - side, -up, qp, len);
                cmds[at + i * 3 + 2] = new RaycastCommand(mount + side, -up, qp, len);
            }
        }

        public void Init(KartSetup s)
        {
            setup = s;
            Body = GetComponent<Rigidbody>();
            Body.mass = s.massKg;
            Body.centerOfMass = s.CenterOfMass();
            Body.linearDamping = 0f;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.interpolation = RigidbodyInterpolation.None; // stepped by SimClock (Script mode)
            Body.maxAngularVelocity = s.maxAngularVelocity;
            Body.solverIterations = s.solverIterations;
            Body.solverVelocityIterations = s.solverVelocityIterations;
            // Cap how fast the solver may push overlapping bodies apart (Unity default 10 m/s). In multi-kart
            // pile-ups the default fired karts metres into the air and over the fence (M2 soak forensics).
            Body.maxDepenetrationVelocity = s.maxDepenetrationVelocity;
            rayMask = ~((1 << 2) | (1 << KartLayer) | (1 << BarrierLayer)); // not Ignore Raycast, not karts, not the outside rail
            preTick = -1;
        }

        [System.NonSerialized] public Collider[] driverColliders;   // the driver's body, switched on at the first step

        [System.NonSerialized] public bool lockBrakes; public static int DbgLocked;
        public void Step(float dt)
        {
            var rb = Body;
            if (driverColliders != null)
            {
                // the inertia the engine worked out from the kart alone, pinned, then the driver's shape switched on
                var it = rb.inertiaTensor; var itr = rb.inertiaTensorRotation;
                rb.inertiaTensor = it; rb.inertiaTensorRotation = itr;
                foreach (var dcol in driverColliders) if (dcol != null) dcol.enabled = true;
                driverColliders = null;
            }
            var up = transform.up;
            var s = setup;
            float k = s.SpringRate, c = s.DamperRate;
            float g = -Physics.gravity.y;
            ForwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

            // caster self-centring: the fronts pull toward their own direction of travel against the driver's hands, a
            // share of last step's front slip (+ slip = sliding right -> the wheels swing right). [TEST VALUE gain]
            SteerApplied = steerDeg; stepped = true;
            if (KartSetup.SlipCurve && s.selfAlignGain > 0f)
                SteerApplied = Mathf.Clamp(steerDeg + s.selfAlignGain * FrontSlipRad * Mathf.Rad2Deg, -s.maxSteerDeg, s.maxSteerDeg);
            float rearLoad = 0f; long pt = Perf.T();
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                var mount = transform.TransformPoint(s.WheelMount((Corner)i));
                float r = s.WheelRadius(i);
                float rayLen = s.travel + r;
                wheelNow = i;
                if (TreadRay(mount, -up, rayLen, s.TireWidth(i), out hits[i], out float x))
                {
                    float xdot = (x - prevX[i]) / dt;
                    prevX[i] = x;
                    float f = k * x + c * xdot;
                    if (x > s.travel) f += k * s.bumpStopScale * (x - s.travel);
                    f = Mathf.Clamp(f, 0f, s.maxTireForceN); // no deep compression may become a launch
                    grounded[i] = true;
                    compression[i] = x;
                    load[i] = f * Mathf.Max(0f, Vector3.Dot(up, hits[i].normal));
                    rb.AddForceAtPosition(up * f, hits[i].point);
                    // Newton's third law: a tire standing on another kart pushes that kart down/away just as hard.
                    // (Missing before: a wheel climbing a rival's nerf bar got a free spring push and launched.)
                    if (hits[i].rigidbody != null && hits[i].rigidbody != rb) hits[i].rigidbody.AddForceAtPosition(-up * f, hits[i].point);
                    if (i >= 2) rearLoad += load[i];
                    if (x > maxCompression) maxCompression = x;
                    n++;
                }
                else
                {
                    grounded[i] = false; compression[i] = 0f; load[i] = 0f; prevX[i] = 0f;
                }
            }
            Perf.Add(9, ref pt);
            GroundedCount = n;
            if (n == 0) { airTime += dt; if (airTime > maxAirTime) maxAirTime = airTime; }
            else airTime = 0f;

            // Drive / brake on the rear (live axle), split by rear load. Drive is limited by the
            // engine at this speed (power curve, clutch, gearing, rev limiter) — the pull fades
            // with speed, so momentum matters. Brakes: rear axle only.
            float engineForce = s.EngineForceAt(ForwardSpeed);
            EngineRpm = s.EngineRpmAt(ForwardSpeed);
            float driveTotal;
            if (throttleCmd >= 0f)
            {
                Throttle = Mathf.Clamp01(throttleCmd); Brake = Mathf.Clamp01(brakeCmd);
                driveTotal = Throttle * engineForce - Brake * s.massKg * s.brakeCap;
            }
            else
            {
                driveTotal = accelCmd >= 0f
                    ? Mathf.Min(s.massKg * accelCmd, engineForce)
                    : -s.massKg * Mathf.Min(-accelCmd, s.brakeCap);
                Throttle = accelCmd >= 0f && engineForce > 1f ? Mathf.Clamp01(driveTotal / engineForce) : 0f;
                Brake = accelCmd < 0f ? Mathf.Clamp01(-accelCmd / s.brakeCap) : 0f;
            }

            // Resistances at the centre of mass: rolling (when on the ground) and air drag (always).
            var v = rb.linearVelocity; float speed = v.magnitude;
            if (speed > 0.05f)
            {
                float totalLoad = 0f, extra = 0f; for (int i = 0; i < 4; i++) { totalLoad += load[i]; if (grounded[i]) extra += rollExtra[i] * load[i]; }
                float fRoll = s.rollingResistance * totalLoad + extra;
                float fAero = 0.5f * 1.2f * s.dragArea * speed * speed;
                rb.AddForce(-v / speed * (fRoll + fAero));
            }

            // Solid rear axle (no differential): both rears turn at the same speed, so in a corner the outside rear (which
            // has further to go) is dragged back and the inside one pushed on. That pair of forces resists the kart
            // rotating -- the steadying a real kart has and an open-diff model doesn't (Franklin 11-20, chasing why karts on
            // slip-angle tires spun in traffic). Each rear's share: scrub stiffness x its load x how much faster or slower its
            // patch moves than the load-weighted mean, as a slip ratio. The forces cancel: no net drag, only the yaw moment.
            float axleMean = 0f, axleN = 0f;
            if (KartSetup.SolidAxle)
                for (int i = 2; i < 4; i++)
                {
                    if (!grounded[i]) continue;
                    var fw = Vector3.ProjectOnPlane(transform.forward, hits[i].normal).normalized;
                    var vpi = rb.GetPointVelocity(hits[i].point);
                    if (hits[i].rigidbody != null && hits[i].rigidbody != rb) vpi -= hits[i].rigidbody.GetPointVelocity(hits[i].point);
                    axleV[i] = Vector3.Dot(vpi, fw); axleMean += load[i] * axleV[i]; axleN += load[i];
                }
            if (axleN > 1e-3f) axleMean /= axleN;
            float axleRef = Mathf.Max(3f, Mathf.Abs(ForwardSpeed));

            for (int i = 0; i < 4; i++)
            {
                sliding[i] = false;
                if (!grounded[i]) continue;
                var hn = hits[i].normal;
                bool front = i < 2;
                var fwd = front ? Quaternion.AngleAxis(KartSetup.WheelSteer(i, SteerApplied), up) * transform.forward : transform.forward;
                fwd = Vector3.ProjectOnPlane(fwd, hn).normalized;
                var side = Vector3.Cross(hn, fwd);
                // Slip is relative to whatever the tire stands on (another kart moves; the dirt doesn't).
                var vp = rb.GetPointVelocity(hits[i].point);
                if (hits[i].rigidbody != null && hits[i].rigidbody != rb) vp -= hits[i].rigidbody.GetPointVelocity(hits[i].point);
                float vLat = Vector3.Dot(vp, side);
                float vLong = Vector3.Dot(vp, fwd);
                float N = load[i];
                // Ground under this tire (grass 50%) x the tire's own condition (berm dust, Franklin).
                var ground = surfaceGrip != null ? surfaceGrip(hits[i], i) : new GroundSample { grip = 1f };
                float dustedGrip = surface != null ? surface.gripDusted : 1f;
                surfaceGripNow[i] = ground.grip * Mathf.Lerp(1f, dustedGrip, dust[i]);
                float cap = s.gripMu * surfaceGripNow[i] * N * (front ? s.frontGripScale : s.rearGripScale);
                float aDeg = Mathf.Atan2(Mathf.Abs(vLat), Mathf.Max(1.5f, Mathf.Abs(vLong))) * Mathf.Rad2Deg;
                slipDeg[i] = aDeg; slipSigned[i] = Mathf.Atan2(vLat, Mathf.Max(1.5f, Mathf.Abs(vLong)));
                // a tire sliding at a big angle has less bite than at its peak (real tires; what makes a big rotation unsavable)
                if (s.slideGripRatio < 1f)
                {
                    float sd = Mathf.Atan2(Mathf.Abs(vLat), Mathf.Max(0.5f, Mathf.Abs(vLong))) * Mathf.Rad2Deg;
                    // with a slip-angle tire the fade starts past twice the peak: a loose kart hangs its rear out there
                    float f0 = KartSetup.SlipCurve ? Mathf.Max(s.slideFadeStartDeg, 2f * s.slipPeakDeg) : s.slideFadeStartDeg;
                    float f1 = f0 + (s.slideFadeEndDeg - s.slideFadeStartDeg);
                    float fade = Mathf.Clamp01((sd - f0) / (f1 - f0));
                    cap *= Mathf.Lerp(1f, s.slideGripRatio, fade * fade * (3f - 2f * fade));
                }

                // Lateral: cancel a fraction of the slip of this wheel's share of mass.
                float share = N / g;
                float fLat = -vLat * share * s.lateralCancelGain / dt;
                bool pastPeak = false;
                if (KartSetup.SlipCurve)
                {
                    // the tire's side force from its slip angle: rising to the peak, then holding (a slow fade past it).
                    // Never more than the old cancel force -- that would push the slip through zero inside one step.
                    float x = aDeg / Mathf.Max(0.5f, s.slipPeakDeg);
                    float shape = x < 1f ? Mathf.Sin(x * Mathf.PI * 0.5f) : Mathf.Max(0.9f, 1f - s.slipPastPeakDrop * (x - 1f));
                    float fc = cap * shape;
                    if (Mathf.Abs(fLat) > fc) fLat = -Mathf.Sign(vLat) * fc;
                    pastPeak = x > 1f;
                }

                float fLong = 0f; bool locked = false;
                if (!front && rearLoad > 1e-3f)
                {
                    fLong = driveTotal * (N / rearLoad);
                    float scrub = 0f;
                    if (KartSetup.SolidAxle && axleN > 1e-3f) scrub = s.axleScrubStiffness * N * (axleMean - axleV[i]) / axleRef;
                    if (fLong < 0f && !lockBrakes)
                    {
                        // eases off as the rear goes: brakes only as hard as the rear can take on top of holding the kart
                        // sideways (with the slip-angle tire the side force is a share of grip, so braking past what's left
                        // took the rear's side grip and spun karts checking up in traffic -- soak 11-20)
                        float room = KartSetup.SlipCurve && KartSetup.BrakeLeavesSide ? Mathf.Sqrt(Mathf.Max(0f, cap * cap - fLat * fLat)) : cap;
                        fLong = Mathf.Max(fLong, -room * 0.95f);
                    }
                    locked = lockBrakes && fLong < 0f && -fLong >= cap * 0.95f;
                    if (fLong < 0f) // braking never pushes the kart backwards
                    {
                        float stop = Mathf.Max(0f, vLong) * share * s.lateralCancelGain / dt;
                        fLong = -Mathf.Min(-fLong, stop);
                    }
                    fLong += scrub; AxleScrub[i] = scrub;
                }

                // Friction circle.
                float mag = Mathf.Sqrt(fLat * fLat + fLong * fLong);
                if (mag > cap && mag > 1e-6f)
                {
                    float sc = cap / mag; fLat *= sc; fLong *= sc;
                    sliding[i] = true;
                }
                if (pastPeak) sliding[i] = true;
                // A locked tire just skids: all its grip opposes the way it is sliding, none left to hold it sideways
                // (Franklin 11-18: locking the brakes on dirt slides and probably spins the kart). Drivers only lock
                // them on purpose (lockBrakes: spinning past the point of no return); otherwise they ease off at the limit.
                if (locked)
                {
                    float vs = Mathf.Sqrt(vLat * vLat + vLong * vLong);
                    if (vs > 0.3f) { fLat = -vLat / vs * cap; fLong = -vLong / vs * cap; sliding[i] = true; DbgLocked++; }
                }
                rb.AddForceAtPosition(side * fLat + fwd * fLong, hits[i].point);
                if (hits[i].rigidbody != null && hits[i].rigidbody != rb) hits[i].rigidbody.AddForceAtPosition(-(side * fLat + fwd * fLong), hits[i].point);

                // Dust: picked up fast on the loose berm; cleaned off by working the tire
                // (cornering load), plus a little just rolling.
                if (surface != null)
                {
                    rollExtra[i] = ground.rollExtra;
                    if (ground.loose) dust[i] = Mathf.Min(1f, dust[i] + dt / Mathf.Max(1e-3f, surface.dustPickupTime));
                    else
                    {
                        float work = cap > 1e-3f ? Mathf.Clamp01(Mathf.Abs(fLat) / cap) : 0f;
                        dust[i] = Mathf.Max(0f, dust[i] - dt * (work / Mathf.Max(1e-3f, surface.dustCleanTime) + surface.dustCleanStraight));
                    }
                }
            }
            {
                float fs = 0f, rs = 0f; int fn = 0, rn = 0;
                for (int i = 0; i < 4; i++) if (grounded[i]) { if (i < 2) { fs += slipSigned[i]; fn++; } else { rs += slipSigned[i]; rn++; } }
                FrontSlipRad = fn > 0 ? fs / fn : 0f; RearSlipRad = rn > 0 ? rs / rn : 0f;
            }
            Perf.Add(10, ref pt);
        }

        /// <summary>Visual-only: place wheel pivots at the suspended wheel centre, steer and spin.</summary>
        public void UpdateVisuals(float dt)
        {
            for (int i = 0; i < 4; i++)
            {
                var w = wheelVisuals[i]; if (w == null) continue;
                var m = setup.WheelMount((Corner)i);
                float springLen = grounded[i] ? setup.travel - compression[i] : setup.travel;
                springLen = Mathf.Max(springLen, 0f);
                w.localPosition = new Vector3(m.x, m.y - springLen, m.z);
                spinAngle[i] = (spinAngle[i] + ForwardSpeed * dt / setup.WheelRadius(i) * Mathf.Rad2Deg) % 360f;
                float steer = KartSetup.WheelSteer(i, SteerApplied);
                w.localRotation = Quaternion.Euler(0f, steer, 0f);
                if (w.childCount > 0) w.GetChild(0).localRotation = Quaternion.Euler(spinAngle[i], 0f, 0f);
            }
        }

        readonly RaycastHit[] rayBuf = new RaycastHit[8];

        /// <summary>Nearest hit along the wheel ray that isn't part of this kart (its own tubes/frame).
        /// Other karts DO count — a wheel landing on another kart's tire or nerf bar is the M3 climb.</summary>
        /// <summary>
        /// The tire feels the dirt across its whole tread: rays at the centre and both tread edges, and the squash is
        /// the AVERAGE of the three (a tire envelops what it stands on). With one centre ray a tire's outer half hung
        /// over a ridge without feeling it, so the low nerf tube (1/2 in up, 1/8 in outside the tire) was the first
        /// thing to meet it — a rigid stop that rolled karts at the inside edge (M2 soak, 2026-11-15). A 3 in ridge
        /// under one edge now lifts the kart ~1 in and the tire climbs it. (Highest-point-wins was tried first: on the
        /// bank a 9 in right tire's edges sit ~0.7 in apart against 0.15 in of sag, and it slid a kart into the wall.)
        /// Point and normal come from the centre ray when it hits.
        /// </summary>
        bool TreadRay(Vector3 mount, Vector3 dir, float len, float width, out RaycastHit best, out float squash)
        {
            squash = 0f;
            bool pre = preTick == stepTick;
            bool found = pre ? PreHit(wheelNow * 3, out best) : GroundRay(mount, dir, len, out best);
            if (found) squash += len - best.distance;
            var side = transform.right * (width * 0.45f);
            for (int e = -1; e <= 1; e += 2)
                if (pre ? PreHit(wheelNow * 3 + (e < 0 ? 1 : 2), out var h) : GroundRay(mount + side * e, dir, len, out h))
                {
                    squash += len - h.distance;
                    if (!found) { best = h; found = true; }
                }
            squash /= 3f;
            return found;
        }

        int wheelNow;
        bool PreHit(int k, out RaycastHit h)
        {
            h = preHits[k];
            return h.collider != null && !NotGround(h);
        }

        bool GroundRay(Vector3 origin, Vector3 dir, float len, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, rayBuf, len, rayMask, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue; bool found = false;
            for (int j = 0; j < n; j++)
            {
                var h = rayBuf[j];
                if (h.rigidbody == Body) continue;
                // Wheel rays stand on the TRACK only. On another kart's tire/body the stiff tire spring + bump stop
                // saw "ground" inches high and produced tens of kN — launches at 58 mph (M2 soak forensics). Kart-on-kart
                // contact (shoving, a tire riding up a rival) belongs to the tire/tube colliders.
                // ...and never on a barrier: a tire overlapping the guardrail saw the rail top as "ground" and the stiff
                // spring lifted that side onto the rail — a 6-degree touch rolled the kart (M1 wall test, 2026-11-15).
                // The rail belongs to the nerf/bumper tubes.
                if (NotGround(h)) continue;
                if (h.distance < bd) { bd = h.distance; best = h; found = true; }
            }
            return found;
        }

        // Per-collider answer to "can a wheel stand on this?", worked out once. Asking every hit every step fetched the
        // parent's name from the engine (a new string each time) and a GetComponent: a slice of every kart's step at 40
        // karts (perf pass 2026-11-17). Same test as before.
        static readonly System.Collections.Generic.Dictionary<Collider, bool> notGround = new System.Collections.Generic.Dictionary<Collider, bool>();
        static bool NotGround(in RaycastHit h)
        {
            var col = h.collider;
            if (notGround.TryGetValue(col, out bool v)) return v;
            var rb = h.rigidbody; var hp = col.transform.parent;
            v = (rb != null && rb.GetComponent<KartPhysics>() != null) || (hp != null && hp.name == "OutsideBarrier");
            notGround[col] = v;
            return v;
        }

        public bool UpsideDown => Vector3.Dot(transform.up, Vector3.up) < 0f;
    }
}
