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
        [HideInInspector] public float accelCmd; // m/s², + drive / - brake

        // Telemetry (read-only)
        public readonly bool[] grounded = new bool[4];
        public readonly float[] compression = new float[4];
        public readonly float[] load = new float[4];
        public readonly bool[] sliding = new bool[4];
        public float airTime;        // current time with all four wheels off the ground
        public float maxAirTime;
        public float maxCompression;
        public readonly float[] surfaceGripNow = { 1f, 1f, 1f, 1f };
        /// <summary>Berm dust on each tire, 0 = clean .. 1 = fully coated (Franklin: costs bite until it cleans off).</summary>
        public readonly float[] dust = new float[4];
        /// <summary>What the ground under a wheel is (set by World from the track model).</summary>
        public System.Func<RaycastHit, int, GroundSample> surfaceGrip;
        public SurfaceConfig surface; // dust behaviour
        public float ForwardSpeed { get; private set; }
        public float EngineRpm { get; private set; }
        public float Throttle { get; private set; }
        public int GroundedCount { get; private set; }

        public Rigidbody Body { get; private set; }
        public Transform[] wheelVisuals = new Transform[4]; // optional, steer/spin pivots
        readonly RaycastHit[] hits = new RaycastHit[4];
        readonly float[] prevX = new float[4];
        readonly float[] spinAngle = new float[4];
        int rayMask;

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
            rayMask = ~(1 << 2); // everything except Ignore Raycast
        }

        public void Step(float dt)
        {
            var rb = Body;
            var up = transform.up;
            var s = setup;
            float k = s.SpringRate, c = s.DamperRate;
            float g = -Physics.gravity.y;
            ForwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

            float rearLoad = 0f;
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                var mount = transform.TransformPoint(s.WheelMount((Corner)i));
                float r = s.WheelRadius(i);
                float rayLen = s.travel + r;
                if (GroundRay(mount, -up, rayLen, out hits[i]))
                {
                    float x = rayLen - hits[i].distance;
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
            GroundedCount = n;
            if (n == 0) { airTime += dt; if (airTime > maxAirTime) maxAirTime = airTime; }
            else airTime = 0f;

            // Drive / brake on the rear (live axle), split by rear load. Drive is limited by the
            // engine at this speed (power curve, clutch, gearing, rev limiter) — the pull fades
            // with speed, so momentum matters. Brakes: rear axle only.
            float engineForce = s.EngineForceAt(ForwardSpeed);
            EngineRpm = s.EngineRpmAt(ForwardSpeed);
            float driveTotal = accelCmd >= 0f
                ? Mathf.Min(s.massKg * accelCmd, engineForce)
                : -s.massKg * Mathf.Min(-accelCmd, s.brakeCap);
            Throttle = accelCmd >= 0f && engineForce > 1f ? Mathf.Clamp01(driveTotal / engineForce) : 0f;

            // Resistances at the centre of mass: rolling (when on the ground) and air drag (always).
            var v = rb.linearVelocity; float speed = v.magnitude;
            if (speed > 0.05f)
            {
                float totalLoad = 0f; for (int i = 0; i < 4; i++) totalLoad += load[i];
                float fRoll = s.rollingResistance * totalLoad;
                float fAero = 0.5f * 1.2f * s.dragArea * speed * speed;
                rb.AddForce(-v / speed * (fRoll + fAero));
            }

            for (int i = 0; i < 4; i++)
            {
                sliding[i] = false;
                if (!grounded[i]) continue;
                var hn = hits[i].normal;
                bool front = i < 2;
                var fwd = front ? Quaternion.AngleAxis(steerDeg, up) * transform.forward : transform.forward;
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
                float cap = s.gripMu * surfaceGripNow[i] * N;

                // Lateral: cancel a fraction of the slip of this wheel's share of mass.
                float share = N / g;
                float fLat = -vLat * share * s.lateralCancelGain / dt;

                float fLong = 0f;
                if (!front && rearLoad > 1e-3f)
                {
                    fLong = driveTotal * (N / rearLoad);
                    if (fLong < 0f) // braking never pushes the kart backwards
                    {
                        float stop = Mathf.Max(0f, vLong) * share * s.lateralCancelGain / dt;
                        fLong = -Mathf.Min(-fLong, stop);
                    }
                }

                // Friction circle.
                float mag = Mathf.Sqrt(fLat * fLat + fLong * fLong);
                if (mag > cap && mag > 1e-6f)
                {
                    float sc = cap / mag; fLat *= sc; fLong *= sc;
                    sliding[i] = true;
                }
                rb.AddForceAtPosition(side * fLat + fwd * fLong, hits[i].point);
                if (hits[i].rigidbody != null && hits[i].rigidbody != rb) hits[i].rigidbody.AddForceAtPosition(-(side * fLat + fwd * fLong), hits[i].point);

                // Dust: picked up fast on the loose berm; cleaned off by working the tire
                // (cornering load), plus a little just rolling.
                if (surface != null)
                {
                    if (ground.loose) dust[i] = Mathf.Min(1f, dust[i] + dt / Mathf.Max(1e-3f, surface.dustPickupTime));
                    else
                    {
                        float work = cap > 1e-3f ? Mathf.Clamp01(Mathf.Abs(fLat) / cap) : 0f;
                        dust[i] = Mathf.Max(0f, dust[i] - dt * (work / Mathf.Max(1e-3f, surface.dustCleanTime) + surface.dustCleanStraight));
                    }
                }
            }
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
                float steer = i < 2 ? steerDeg : 0f;
                w.localRotation = Quaternion.Euler(0f, steer, 0f);
                if (w.childCount > 0) w.GetChild(0).localRotation = Quaternion.Euler(spinAngle[i], 0f, 0f);
            }
        }

        readonly RaycastHit[] rayBuf = new RaycastHit[8];

        /// <summary>Nearest hit along the wheel ray that isn't part of this kart (its own tubes/frame).
        /// Other karts DO count — a wheel landing on another kart's tire or nerf bar is the M3 climb.</summary>
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
                if (h.rigidbody != null && h.rigidbody.GetComponent<KartPhysics>() != null) continue;
                if (h.distance < bd) { bd = h.distance; best = h; found = true; }
            }
            return found;
        }

        public bool UpsideDown => Vector3.Dot(transform.up, Vector3.up) < 0f;
    }
}
