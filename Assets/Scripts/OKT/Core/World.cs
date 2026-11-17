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
            w.LogSetup();
            return w;
        }

        void LogSetup()
        {
            var w = this;
            w.Log($"Physics: gravity {Physics.gravity}, fixedDt {Time.fixedDeltaTime:F5}, solver pos {Physics.defaultSolverIterations} / vel {Physics.defaultSolverVelocityIterations}, mode {Physics.simulationMode}");
            w.Log($"Track '{w.track.name}': centerline {U.FtS(w.track.centerLength)} (file says {U.FtS(w.track.lapLengthM)}), racing line {U.FtS(w.track.lineLength)}");
        }

        public RaceDirector director;
        public List<DriverProfile> drivers12;

        /// <summary>M2: a full field on the grid (double-file, pace order — no invert), director in charge.</summary>
        public static World BuildRace(string json, int seed, RaceConfig race = null, SurfaceConfig cfg = null)
        {
            ApplyPhysicsSettings();
            var w = new World();
            w.trackJson = json;
            w.surfaceCfg = cfg ?? new SurfaceConfig();
            w.track = new TrackModel(json, w.surfaceCfg);
            w.root = new GameObject("OKT World (race)");
            w.trackGo = TrackBuilder.Build(w.track, w.root.transform);
            w.surfaceGo = w.trackGo.transform.Find("Surface").gameObject;
            w.lineOverlay = TrackBuilder.BuildLineOverlay(w.track, w.trackGo.transform);
            w.lineOverlay.enabled = false;
            race = race ?? new RaceConfig();
            w.director = new RaceDirector(w, race, seed);
            float baseLap = 11.03f; // clean-air Safe lap at M1 (s)
            w.drivers12 = FieldGen.Make(race.fieldSize, seed, baseLap);
            float L = w.track.centerLength;
            float a0 = race.gridLeaderU * L;
            var lane = race.stagingStart ? w.track.Feature("grid lane") : null;
            if (race.stagingStart && lane == null) w.Log("Staging start asked for, but the track has no \"grid lane\" feature: starting from the track grid.");
            if (lane != null) { w.SpawnStaging(lane, race); w.LogSetup(); return w; }
            for (int i = 0; i < w.drivers12.Count; i++)
            {
                var dp = w.drivers12[i];
                int row = i / 2;
                float arc = a0 - row * race.rowGapFt * U.Ft;
                float s = ArcToS(w.track, arc);
                var f = w.track.Frame(s);
                float dLane = (i % 2 == 0 ? -1f : 1f) * race.laneFrac * f.halfWidth;
                var dc = new DriverConfig { pace = dp.paceFactor, lateralOffsetFt = dp.lateralOffsetIn / 12f };
                var kp = w.SpawnKartAt(dp.number, dp.color, new KartSetup { axleTeeth = dp.axleTeeth }, dc, s, dLane);
                var ai = w.drivers[w.drivers.Count - 1];
                ai.raceDist = arc - L; // grid sits before S/F: crossing it the first time = distance 0
                w.director.Add(ai, kp, dp, i);
            }
            w.LogSetup();
            w.Log($"Field: {w.drivers12.Count} karts, seed {seed}; grid (pace order): " + string.Join(", ", w.drivers12.ConvertAll(d => $"#{d.number} {d.tier} q+{d.qualiOffsetSec:F2} race+{d.paceOffsetSec:F2} 12/{d.axleTeeth}{(d.tireNote.Length > 0 ? " " + d.tireNote : "")}{(d.qualiNote.Length > 0 ? " (" + d.qualiNote + ")" : "")}")));
            return w;
        }

        /// <summary>Staging-grid start: two columns on the paved grid outside turn 4, pole nearest the lane, facing the
        /// track. Each kart gets a path down its column, into the lane, through the gate and onto the dirt, where it
        /// turns down the track. Positions come from the track JSON's "grid lane" feature (u of the gate).</summary>
        void SpawnStaging(TrackFeature lane, RaceConfig race)
        {
            float uG = lane.points[0].x, L = track.centerLength;
            var fg = track.Frame(Mathf.Repeat(uG, 1f) * track.N);
            var outw = new Vector3(fg.outN.x, 0f, fg.outN.y); var tan = new Vector3(fg.tan.x, 0f, fg.tan.y);
            Vector3 Ray(float outFt, float sideFt) { var p = track.OffTrackPoint(uG, outFt) + tan * (sideFt * U.Ft); p.y = Mathf.Max(track.InfieldHeight, p.y); return p; }
            float gateArc = track.ArcAt(Mathf.Repeat(uG, 1f) * track.N);
            for (int i = 0; i < drivers12.Count; i++)
            {
                var dp = drivers12[i];
                int row = i / 2; float side = (i % 2 == 0 ? 1f : -1f) * race.stagingColumnFt; // pole column on the track-forward side
                float outFt = race.stagingFirstRowFt + row * race.stagingRowFt;
                var pos = Ray(outFt, side); pos.y += 0.03f;
                var dc = new DriverConfig { pace = dp.paceFactor, lateralOffsetFt = dp.lateralOffsetIn / 12f };
                var kp = SpawnKartPose(dp.number, dp.color, new KartSetup { axleTeeth = dp.axleTeeth }, dc, pos, Quaternion.LookRotation(-outw, Vector3.up));
                var ai = drivers[drivers.Count - 1];
                var path = new List<Vector3>();
                for (float o = outFt - 3f; o > 52f; o -= 3f) path.Add(Ray(o, side));          // straight out of my slot
                for (float o = 52f; o >= 0f; o -= 4f) path.Add(Ray(o, side * Mathf.Clamp01((o - 30f) / 22f))); // into the lane's centre
                // through the gate and onto the dirt, turning down the track to the outside of the groove
                foreach (var (du, frac) in new[] { (0.000f, 0.80f), (0.008f, 0.62f), (0.016f, 0.48f), (0.026f, 0.40f), (0.038f, 0.36f) })
                {
                    var f = track.Frame(Mathf.Repeat(uG + du, 1f) * track.N); var p = f.pos + f.outN * (frac * f.halfWidth);
                    path.Add(new Vector3(p.x, 0f, p.y));
                }
                ai.stagePath = path; ai.stageIdx = 0;
                // one lap to form up from the gate, then the pace lap (Franklin: about a lap before one-to-go)
                ai.raceDist = gateArc - 2f * L;
                director.Add(ai, kp, dp, i);
            }
            Log($"Field: {drivers12.Count} karts on the staging grid (gate u={uG:F3}); grid (pace order): " + string.Join(", ", drivers12.ConvertAll(d => $"#{d.number} {d.tier} q+{d.qualiOffsetSec:F2} race+{d.paceOffsetSec:F2} 12/{d.axleTeeth}{(d.tireNote.Length > 0 ? " " + d.tireNote : "")}{(d.qualiNote.Length > 0 ? " (" + d.qualiNote + ")" : "")}")));
        }

        static float ArcToS(TrackModel t, float arc)
        {
            arc = Mathf.Repeat(arc, t.centerLength);
            float lo = 0f, hi = t.N;
            for (int i = 0; i < 40; i++) { float mid = (lo + hi) * 0.5f; if (t.ArcAt(mid) < arc) lo = mid; else hi = mid; }
            return (lo + hi) * 0.5f;
        }

        public KartPhysics SpawnKart(int number, KartSetup setup, DriverConfig driver)
        {
            // M1: one kart, standing start on the racing line mid back-straight (s = N/2).
            float s0 = track.N * 0.5f;
            return SpawnKartAt(number, new Color(0.85f, 0.12f, 0.10f), setup, driver, s0, track.Frame(s0).LineOffset);
        }

        public KartPhysics SpawnKartAt(int number, Color color, KartSetup setup, DriverConfig driver, float s, float d)
        {
            var f = track.Frame(s);
            var p = f.pos + f.outN * d;
            float h = track.SurfaceHeightSD(s, d);
            var fwd = new Vector3(f.tan.x, 0f, f.tan.y);
            var outward = new Vector3(f.outN.x, 0f, f.outN.y);
            var n = (Vector3.up - outward * Mathf.Tan(surfaceCfg.bankingDeg * Mathf.Deg2Rad)).normalized;
            return SpawnKartPose(number, color, setup, driver, new Vector3(p.x, h + 0.03f, p.y), Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, n), n));
        }

        /// <summary>A kart at a world position and rotation (on the track, or off it on the staging grid).</summary>
        public KartPhysics SpawnKartPose(int number, Color color, KartSetup setup, DriverConfig driver, Vector3 pos, Quaternion rot)
        {
            var kp = KartFactory.Create("Kart " + number, number, color, setup, root.transform);
            kp.transform.SetPositionAndRotation(pos, rot);
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
                if (hit.collider.name == "Ground")
                {
                    track.Locate(new Vector2(hit.point.x, hit.point.z), ref hints[i], out float gs, out float gd);
                    return new GroundSample { grip = track.PavedAt(gs, gd) ? surfaceCfg.gripPaved : surfaceCfg.gripGrass };
                }
                return new GroundSample { grip = 1f }; // another kart's body or a barrier top
            };
            kp.surface = surfaceCfg;
            IgnoreTireBodiesVsGround(kp);
            kp.gameObject.AddComponent<KartContact>().world = this;
            var ai = kp.gameObject.AddComponent<KartAI>();
            ai.Init(kp, track, driver);
            karts.Add(kp); drivers.Add(ai);
            if (karts.Count == 1) Log($"Kart {number}: {U.LbS(setup.massKg)}, rb.maxAngularVelocity = {kp.Body.maxAngularVelocity} rad/s (asked {setup.maxAngularVelocity}), CoM {U.InS(kp.Body.centerOfMass.y)} high / {U.InS(-kp.Body.centerOfMass.x)} left / {U.InS(-kp.Body.centerOfMass.z)} rearward, spring {setup.SpringRate / U.LbfPerIn:F0} lbf/in, damper {setup.DamperRate / U.LbfPerIn:F1} lbf·s/in");
            return kp;
        }

        public void StepOnce()
        {
            float dt = FixedDt;
            director?.PreStep(dt);
            for (int i = 0; i < karts.Count; i++)
            {
                if (karts[i] == null) continue;
                drivers[i].Step(dt, simTime);
                karts[i].Step(dt);
            }
            Physics.Simulate(dt);
            simTime += dt;
            steps++;
            director?.PostStep(dt);
        }

        public void UpdateVisuals(float dt)
        {
            foreach (var k in karts) if (k != null) k.UpdateVisuals(dt);
        }

        /// <summary>Tire bodies never touch the dirt or infield — the wheel rays own the ground.</summary>
        void IgnoreTireBodiesVsGround(KartPhysics kp)
        {
            var tb = kp.transform.Find("TireBodies"); if (tb == null) return;
            // Tire bodies are for kart-on-kart only: the wheel rays own the ground, and the nerfs/bumpers own the
            // rails (a box-edge tire body catching the rail spun 4-5 deg wall touches round — M1 had them skim).
            var trackCols = trackGo.GetComponentsInChildren<Collider>();
            foreach (var c in tb.GetComponentsInChildren<Collider>())
            {
                if (surfaceGo != null) Physics.IgnoreCollision(c, surfaceGo.GetComponent<Collider>());
                foreach (var t in trackCols) Physics.IgnoreCollision(c, t);
            }
        }

        /// <summary>Rebuild the surface mesh/collider from the current SurfaceConfig (e.g. prep toggle).</summary>
        public void RebuildSurface()
        {
            if (surfaceGo != null) Object.DestroyImmediate(surfaceGo);
            surfaceGo = TrackBuilder.BuildSurface(track, trackGo.transform);
            foreach (var k in karts) if (k != null) IgnoreTireBodiesVsGround(k);
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
