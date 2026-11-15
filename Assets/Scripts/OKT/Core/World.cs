// The spike world: physics settings, track, karts, and the sim clock.
// Plain C# so the same code runs in play mode (RaceBootstrap) and headless
// (Editor/M1HeadlessTest) — the headless test exercises the real thing.
//
// Game speed is extra physics steps per frame (spec §10), never Time.timeScale.
using System.Collections.Generic;
using UnityEngine;

namespace OKT
{
    public class World
    {
        // 120 Hz (spec §3 said 1/60; spec §13: shrink the timestep before reaching for hacks).
        // Needed for the stiff tire springs and round-tube-on-dirt contact. Headless test may override.
        public static float FixedDt = 1f / 120f;

        public TrackModel track;
        public SurfaceConfig surfaceCfg;
        public GameObject root, trackGo, surfaceGo;
        public LineRenderer lineOverlay;
        public readonly List<KartPhysics> karts = new List<KartPhysics>();
        public readonly List<KartAI> drivers = new List<KartAI>();
        public float simTime;
        public long steps;
        public string trackJson;
        public System.Text.StringBuilder log = new System.Text.StringBuilder();

        public static void ApplyPhysicsSettings()
        {
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Time.fixedDeltaTime = FixedDt;
            Physics.defaultSolverIterations = 4;          // position (spec §3 [TEST VALUE])
            Physics.defaultSolverVelocityIterations = 8;  // velocity (spec §3 [TEST VALUE])
            Physics.simulationMode = SimulationMode.Script; // SimClock steps physics
        }

        public static World Build(string json, SurfaceConfig cfg = null, KartSetup setup = null, DriverConfig driver = null)
        {
            ApplyPhysicsSettings();
            var w = new World();
            w.trackJson = json;
            w.surfaceCfg = cfg ?? new SurfaceConfig();
            w.track = new TrackModel(json, w.surfaceCfg);
            w.root = new GameObject("OKT World");
            w.trackGo = TrackBuilder.Build(w.track, w.root.transform);
            w.surfaceGo = w.trackGo.transform.Find("Surface").gameObject;
            w.lineOverlay = TrackBuilder.BuildLineOverlay(w.track, w.trackGo.transform);
            w.lineOverlay.enabled = false;
            w.SpawnKart(1, setup ?? new KartSetup(), driver ?? new DriverConfig());
            w.Log($"Physics: gravity {Physics.gravity}, fixedDt {Time.fixedDeltaTime:F5}, solver pos {Physics.defaultSolverIterations} / vel {Physics.defaultSolverVelocityIterations}, mode {Physics.simulationMode}");
            w.Log($"Track '{w.track.name}': centerline {U.FtS(w.track.centerLength)} (file says {U.FtS(w.track.lapLengthM)}), racing line {U.FtS(w.track.lineLength)}");
            return w;
        }

        public KartPhysics SpawnKart(int number, KartSetup setup, DriverConfig driver)
        {
            // M1: one kart, standing start on the racing line mid back-straight (s = N/2).
            float s = track.N * 0.5f;
            var f = track.Frame(s);
            var p = f.pos + f.outN * f.LineOffset;
            float h = track.SurfaceHeightSD(s, f.LineOffset);
            var fwd = new Vector3(f.tan.x, 0f, f.tan.y);
            var outward = new Vector3(f.outN.x, 0f, f.outN.y);
            var n = (Vector3.up - outward * Mathf.Tan(surfaceCfg.bankingDeg * Mathf.Deg2Rad)).normalized;

            var kp = KartFactory.Create("Kart " + number, number, new Color(0.85f, 0.12f, 0.10f), setup, root.transform);
            kp.transform.SetPositionAndRotation(new Vector3(p.x, h + 0.03f, p.y), Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, n), n));
            Physics.SyncTransforms();
            // Per-wheel ground grip from the ONE track model: groove / loose berm / grass.
            var hints = new[] { -1, -1, -1, -1 };
            kp.surfaceGrip = (hit, i) =>
            {
                if (surfaceGo != null && hit.collider.gameObject == surfaceGo)
                {
                    track.Locate(new Vector2(hit.point.x, hit.point.z), ref hints[i], out float hs, out float hd);
                    return track.GroundAtSD(hs, hd);
                }
                if (hit.collider.name == "Ground") return new GroundSample { grip = surfaceCfg.gripGrass };
                return new GroundSample { grip = 1f }; // another kart's body or a barrier top
            };
            kp.surface = surfaceCfg;
            var ai = kp.gameObject.AddComponent<KartAI>();
            ai.Init(kp, track, driver);
            karts.Add(kp); drivers.Add(ai);
            Log($"Kart {number}: {U.LbS(setup.massKg)}, rb.maxAngularVelocity = {kp.Body.maxAngularVelocity} rad/s (asked {setup.maxAngularVelocity}), CoM {U.InS(kp.Body.centerOfMass.y)} high / {U.InS(-kp.Body.centerOfMass.x)} left / {U.InS(-kp.Body.centerOfMass.z)} rearward, spring {setup.SpringRate / U.LbfPerIn:F0} lbf/in, damper {setup.DamperRate / U.LbfPerIn:F1} lbf·s/in");
            return kp;
        }

        public void StepOnce()
        {
            float dt = FixedDt;
            for (int i = 0; i < karts.Count; i++)
            {
                if (karts[i] == null) continue;
                drivers[i].Step(dt, simTime);
                karts[i].Step(dt);
            }
            Physics.Simulate(dt);
            simTime += dt;
            steps++;
        }

        public void UpdateVisuals(float dt)
        {
            foreach (var k in karts) if (k != null) k.UpdateVisuals(dt);
        }

        /// <summary>Rebuild the surface mesh/collider from the current SurfaceConfig (e.g. prep toggle).</summary>
        public void RebuildSurface()
        {
            if (surfaceGo != null) Object.DestroyImmediate(surfaceGo);
            surfaceGo = TrackBuilder.BuildSurface(track, trackGo.transform);
            Physics.SyncTransforms();
        }

        public void Destroy()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        public void Log(string msg)
        {
            log.AppendLine(msg);
            Debug.Log("[OKT] " + msg);
        }
    }

    /// <summary>Accumulates real time x speed and runs whole physics steps (spec §10).</summary>
    public class SimClock
    {
        public int speed = 1;          // 0 = paused, 1, 2
        public int maxStepsPerFrame = 16;
        float acc;

        public int Tick(World w, float realDt)
        {
            if (speed <= 0) { acc = 0f; return 0; }
            acc += realDt * speed;
            int n = 0;
            while (acc >= World.FixedDt && n < maxStepsPerFrame * speed)
            {
                w.StepOnce();
                acc -= World.FixedDt;
                n++;
            }
            if (n >= maxStepsPerFrame * speed) acc = 0f; // don't spiral if the frame rate collapses
            return n;
        }
    }
}
