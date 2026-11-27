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
            Physics.reuseCollisionCallbacks = true;   // one Collision object reused per callback: no per-contact garbage
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
            w.drivers12 = FieldGen.Make(race.fieldSize, seed, baseLap, race.roster);
            float L = w.track.centerLength;
            float a0 = race.gridLeaderU * L;
            var lane = race.stagingStart && w.track.Feature("grid lane A") != null ? w.track.Feature("grid lane") : null;
            if (race.stagingStart && lane == null) w.Log("Staging start asked for, but the track has no \"grid lane\" feature: starting from the track grid.");
            if (lane != null) { w.SpawnStaging(lane, race); w.LogSetup(); return w; }
            for (int i = 0; i < w.drivers12.Count; i++)
            {
                var dp = w.drivers12[i];
                int lanes = race.singleFileStart ? 1 : 2;   // single-file start: one line, nose to tail
                int row = i / lanes;
                float arc = a0 - row * race.rowGapFt * U.Ft;
                float s = ArcToS(w.track, arc);
                var f = w.track.Frame(s);
                float dLane = lanes == 1 ? race.cautionLaneFrac * f.halfWidth : (i % 2 == 0 ? -1f : 1f) * race.laneFrac * f.halfWidth;
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

        /// <summary>Staging-grid start (Franklin 11-17, laid out from the aerial): the grid is the paved strip running north
        /// from the turn 4 exit, two lanes with a white strip down the middle -- Lane B west, Lane A east, each about three
        /// karts wide. The class lines up two-wide in Lane A facing south (toward the track), pole in the west column; a
        /// field too long for Lane A fills Lane B too. Out single file in pace order: down the column, west along the link
        /// to the gate at the end of the turn 4 wall, onto the dirt. Positions come from the track JSON's "grid lane"
        /// (road: gate .. grid) and "grid lane A" / "grid lane B" (centre lines, exit end first) features.</summary>
        void SpawnStaging(TrackFeature lane, RaceConfig race)
        {
            var laneA = track.Feature("grid lane A"); var laneB = track.Feature("grid lane B");
            float uG = lane.points[0].x, L = track.centerLength;
            Vector3 FP(Vector2 uo) { var p = track.OffTrackPoint(uo.x, uo.y); p.y = Mathf.Max(track.InfieldHeight, p.y); return p; }
            Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }
            float gateArc = track.ArcAt(Mathf.Repeat(uG, 1f) * track.N);
            var road = new List<Vector3>(); foreach (var q in lane.points) road.Add(FP(q)); // gate .. grid end
            var gatePos = road[0];
            var aS = FP(laneA.points[0]); var aN = FP(laneA.points[1]);
            var axis = Flat(aN - aS);                                             // into the grid (north), away from the exit
            var bS = laneB != null ? FP(laneB.points[0]) : aS + Vector3.Cross(Vector3.up, axis) * 4f;
            var west = Flat(Vector3.ProjectOnPlane(bS - aS, axis));               // across, toward Lane B
            float laneLenFt = Vector3.Distance(Flat(aN - aS) * 0f + aN, aS) / U.Ft;
            int capRows = Mathf.Max(1, Mathf.FloorToInt((laneLenFt - race.stagingFirstRowFt) / race.stagingRowFt) + 1);
            int n = drivers12.Count, inA = n <= 2 * capRows || laneB == null ? n : (n + 1) / 2;
            for (int i = 0; i < n; i++)
            {
                var dp = drivers12[i];
                bool inLaneA = i < inA; int k = inLaneA ? i : i - inA;
                var S0 = inLaneA ? aS : bS;
                int row = k / 2; float col = (k % 2 == 0 ? 1f : -1f) * race.stagingColumnFt * U.Ft; // pole column to the west
                var pos = S0 + axis * ((race.stagingFirstRowFt + row * race.stagingRowFt) * U.Ft) + west * col; pos.y += 0.03f;
                var dc = new DriverConfig { pace = dp.paceFactor, lateralOffsetFt = dp.lateralOffsetIn / 12f };
                var kp = SpawnKartPose(dp.number, dp.color, new KartSetup { axleTeeth = dp.axleTeeth }, dc, pos, Quaternion.LookRotation(-axis, Vector3.up));
                var ai = drivers[drivers.Count - 1];
                var path = new List<Vector3>();
                void Seg(Vector3 from, Vector3 to, float stepFt) { float d = Vector3.Distance(from, to); int m = Mathf.Max(1, Mathf.CeilToInt(d / (stepFt * U.Ft))); for (int j = 1; j <= m; j++) path.Add(Vector3.Lerp(from, to, (float)j / m)); }
                // down my column to just inside the grid's exit end (was 2 ft past it: the east column ran out onto the post
                // at the end of the turn 4 triangle fence and drove through the fence -- Franklin's playtest 11-18)
                var colEnd = S0 + west * col + axis * (1.5f * U.Ft);
                Seg(pos, colEnd, 3f);
                // along the link to the gate (the road run backwards), from the first road point nearer the gate than I am
                ai.stageSlowIdx = path.Count;
                var at = colEnd; float myGate = Vector3.Distance(colEnd, gatePos);
                for (int j = road.Count - 1; j >= 0; j--)
                    if (Vector3.Distance(road[j], gatePos) < myGate - 1f) { Seg(at, road[j], 3f); at = road[j]; }
                ai.stageGatePos = gatePos; ai.stageGateIdx = path.Count - 1; ai.stageGateArc = gateArc;
                // onto the dirt: across the edge and easing down the banking toward the formation lanes, staying in the top
                // half while the banking changes under it (no suspension: coming on from the low side the left front slams)
                {
                    var f0 = track.Frame(Mathf.Repeat(uG, 1f) * track.N);
                    float inEnd = 0.58f * f0.halfWidth;
                    void At(float alongFt, float inM)
                    {
                        var f = track.Frame(Mathf.Repeat(uG + alongFt * U.Ft / L, 1f) * track.N);
                        var p = f.pos + f.outN * (f.halfWidth - inM); var to = new Vector3(p.x, 0f, p.y);
                        Seg(at, to, 3f); at = to;
                    }
                    At(4f, 2.5f * U.Ft); At(10f, 4.5f * U.Ft); At(18f, 6.5f * U.Ft); At(30f, Mathf.Lerp(6.5f * U.Ft, inEnd, 0.6f)); At(45f, inEnd);
                }
                ai.stagePath = path; ai.stageIdx = 0;
                // one lap to form up from the gate, then the pace lap (Franklin: about a lap before one-to-go)
                ai.raceDist = gateArc - 2f * L;
                director.Add(ai, kp, dp, i);
            }
            Log($"Field: {n} karts on the staging grid, {inA} in Lane A{(n > inA ? $", {n - inA} in Lane B" : "")} (gate u={uG:F3}); grid (pace order): " + string.Join(", ", drivers12.ConvertAll(d => $"#{d.number} {d.tier} q+{d.qualiOffsetSec:F2} race+{d.paceOffsetSec:F2} 12/{d.axleTeeth}{(d.tireNote.Length > 0 ? " " + d.tireNote : "")}{(d.qualiNote.Length > 0 ? " (" + d.qualiNote + ")" : "")}")));
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
            if (Perf.On) { StepTimed(dt); return; }
            director?.PreStep(dt);
            CastWheelRays();
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

        /// <summary>
        /// Every kart's wheel rays in one batch, spread over the CPU cores by the job system, instead of 12 casts per kart
        /// one after another (perf pass 2026-11-17: the biggest piece of the kart code at 40 karts). Nearest hit per ray,
        /// with karts and the outside rail masked out by layer -- the same rule the per-hit filter applied.
        /// </summary>
        void CastWheelRays()
        {
            int n = 0; foreach (var k in karts) if (k != null) n++;
            if (n == 0) return;
            int total = n * KartPhysics.RaysPerKart;
            if (!rayCmds.IsCreated || rayCmds.Length < total)
            {
                if (rayCmds.IsCreated) { rayCmds.Dispose(); rayHits.Dispose(); }
                rayCmds = new Unity.Collections.NativeArray<RaycastCommand>(total, Unity.Collections.Allocator.Persistent);
                rayHits = new Unity.Collections.NativeArray<RaycastHit>(total, Unity.Collections.Allocator.Persistent);
            }
            int at = 0;
            foreach (var k in karts)
            {
                if (k == null) continue;
                k.FillRays(rayCmds, at, new QueryParameters(k.RayMask, false, QueryTriggerInteraction.Ignore, false));
                at += KartPhysics.RaysPerKart;
            }
            var cmds = rayCmds.GetSubArray(0, total); var res = rayHits.GetSubArray(0, total);
            RaycastCommand.ScheduleBatch(cmds, res, 16, 1).Complete();
            at = 0;
            foreach (var k in karts)
            {
                if (k == null) continue;
                for (int j = 0; j < KartPhysics.RaysPerKart; j++) k.preHits[j] = res[at + j];
                k.stepTick = steps; k.preTick = steps;
                at += KartPhysics.RaysPerKart;
            }
        }
        Unity.Collections.NativeArray<RaycastCommand> rayCmds;
        Unity.Collections.NativeArray<RaycastHit> rayHits;

        /// <summary>The same step, timed by part (Perf.On; the M2Perf harness). Identical order and calls.</summary>
        void StepTimed(float dt)
        {
            var sw = Perf.Sw; long t0 = sw.ElapsedTicks;
            director?.PreStep(dt);
            long t1 = sw.ElapsedTicks;
            CastWheelRays();
            Perf.S[15] += sw.ElapsedTicks - t1; t1 = sw.ElapsedTicks;
            for (int i = 0; i < karts.Count; i++)
            {
                if (karts[i] == null) continue;
                long a = sw.ElapsedTicks;
                drivers[i].Step(dt, simTime);
                long b = sw.ElapsedTicks;
                karts[i].Step(dt);
                Perf.Ai += b - a; Perf.Kart += sw.ElapsedTicks - b;
            }
            long t2 = sw.ElapsedTicks;
            Physics.Simulate(dt);
            long t3 = sw.ElapsedTicks;
            simTime += dt;
            steps++;
            director?.PostStep(dt);
            long t4 = sw.ElapsedTicks;
            Perf.Pre += t1 - t0; Perf.Phys += t3 - t2; Perf.Post += t4 - t3; Perf.Steps++;
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
            // Body panels (nose, side pods) never touch the ground either: they're thin plastic hung on the nerf bars about
            // 1/2 in off the dirt (Franklin q01), so a leaning kart's panel flexes and skids along it. As rigid hulls they
            // caught the ground sliding sideways and the engine pushed them out sideways-and-up -- a 38 mph slide off the
            // turn 3 exit threw a kart 26 ft up and back over the rail (M2RailEndTest, 2026-11-27). The frame, nerf bars
            // and bumpers still scrape the dirt; the panels still hit karts and the rails.
            foreach (var c in kp.GetComponentsInChildren<Collider>())
            {
                if (!c.name.EndsWith("_Hitbox") || c.name.StartsWith("Seat") || c.name.StartsWith("Engine")) continue;
                foreach (var t in trackCols) if (t.gameObject.layer != KartPhysics.BarrierLayer) Physics.IgnoreCollision(c, t);
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
            if (rayCmds.IsCreated) { rayCmds.Dispose(); rayHits.Dispose(); }
            if (root != null) Object.DestroyImmediate(root);
        }

        public void Log(string msg)
        {
            log.AppendLine(msg);
            Debug.Log("[OKT] " + msg);
        }
    }

    /// <summary>Step-time accounting for the performance harness (off in normal play).</summary>
    public static class Perf
    {
        public static bool On;
        public static readonly System.Diagnostics.Stopwatch Sw = System.Diagnostics.Stopwatch.StartNew();
        public static long Pre, Ai, Kart, Phys, Post, Steps;
        public static void Reset() { Pre = Ai = Kart = Phys = Post = Steps = 0; System.Array.Clear(S, 0, S.Length); }
        // finer sections (temporary instrumentation for the perf pass): Perf.T() at a section start, Perf.Add(i, ref t) at its end
        public static readonly long[] S = new long[16];
        public static readonly string[] SName = { "ai.locate+band", "ai.wallgap", "ai.laps+arc", "ai.pursuit-target", "ai.steer", "ai.speed-lookahead", "ai.curvature", "ai.ground", "ai.rest",
                                                   "kart.rays", "kart.grip+tires", "kart.rest", "dir.awareness", "dir.traffic(all, incl awareness)", "dir.caution-drive", "world.ray-batch" };
        public static long T() => On ? Sw.ElapsedTicks : 0;
        public static void Add(int i, ref long t) { if (!On) return; long n = Sw.ElapsedTicks; S[i] += n - t; t = n; }
        public static string Sections()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < S.Length; i++) if (S[i] > 0) sb.Append($"   {SName[i]} {Ms(S[i]):F3}");
            return sb.ToString();
        }
        static double Ms(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency / System.Math.Max(1, Steps);
        public static string Report() =>
            $"per step: total {Ms(Pre + Ai + Kart + Phys + Post):F3} ms = director-pre {Ms(Pre):F3} + drivers {Ms(Ai):F3} + kart-physics-code {Ms(Kart):F3} + Physics.Simulate {Ms(Phys):F3} + director-post {Ms(Post):F3} ({Steps} steps)";
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
