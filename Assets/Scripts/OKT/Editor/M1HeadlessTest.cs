// Headless M1 check, runnable in batch mode:
//   Unity -batchmode -nographics -projectPath <proj> -executeMethod OKT.EditorTools.M1HeadlessTest.Run -okt-out <file> [-okt-sweep]
// Builds the real World (same code as play mode), steps physics, and writes
// numbers for the M1 REPORT — in imperial units (Franklin, 2026-11-14).
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OKT.EditorTools
{
    public static class M1HeadlessTest
    {
        [MenuItem("OKT/Run M1 Headless Test")]
        public static void RunFromMenu() { Debug.Log(Execute()); }

        public static void Run()
        {
            string outPath = "m1-headless-results.txt";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-okt-out") outPath = args[i + 1];
            int code = 0;
            string report;
            try { report = Execute(); }
            catch (System.Exception e) { report = "EXCEPTION: " + e; code = 1; }
            File.WriteAllText(outPath, report);
            Debug.Log(report);
            EditorApplication.Exit(code);
        }

        static float YawErr(World w, KartAI ai, KartPhysics k)
        {
            var f = w.track.Frame(ai.s);
            return Mathf.Abs(Vector3.SignedAngle(new Vector3(f.tan.x, 0, f.tan.y), Vector3.ProjectOnPlane(k.transform.forward, Vector3.up), Vector3.up));
        }

        /// <summary>Lowest point of the frame box / bottom tubes above the surface (m).</summary>
        static float LowestClearance(World w, KartPhysics k)
        {
            var st = k.setup; float best = float.MaxValue;
            float fy = st.frameClearance, ty = st.TubeBottomCentre - st.TubeRadius;
            float hw = st.cockpitHalfWidthIn * U.In, f = st.cockpitFrontIn * U.In, r = st.cockpitRearIn * U.In;
            var pts = new[] {
                new Vector3(hw, fy, f), new Vector3(-hw, fy, f), new Vector3(hw, fy, -r), new Vector3(-hw, fy, -r),
                new Vector3(st.NerfX(true), ty, 0f), new Vector3(st.NerfX(false), ty, 0f),
                new Vector3(0f, ty, st.FrontBumperZ), new Vector3(0f, ty, st.RearBumperZ) };
            foreach (var lp in pts)
            {
                var p = k.transform.TransformPoint(lp);
                best = Mathf.Min(best, p.y - w.track.SurfaceHeight(p.x, p.z));
            }
            return best;
        }

        static string sb0 = "";
        static string Execute()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cl = System.Environment.GetCommandLineArgs();
            int hz = 120; for (int a = 0; a < cl.Length - 1; a++) if (cl[a] == "-okt-hz") hz = int.Parse(cl[a + 1]);
            World.FixedDt = 1f / hz;
            for (int a = 0; a < cl.Length - 1; a++) if (cl[a] == "-okt-cs") DriverConfig.DefaultCornerSlow = float.Parse(cl[a + 1], System.Globalization.CultureInfo.InvariantCulture);
            sb0 = $" cornerSlow {DriverConfig.DefaultCornerSlow:F2}";
            string json = File.ReadAllText(M1SceneSetup.JsonPath);
            var sb = new StringBuilder();
            sb.AppendLine($"[{hz} Hz physics]{sb0}") ; sb.AppendLine("OKT M1 headless test — Unity " + Application.unityVersion + " — track " + M1SceneSetup.JsonPath);

            // ---------- A. static geometry checks
            {
                var w = World.Build(json);
                sb.Append(w.log);
                var t = w.track; var cfg = t.cfg;
                var setup = new KartSetup();
                float minW = float.MaxValue, maxW = 0f;
                for (int i = 0; i < t.M; i++) { float wd = t.Frame((float)i / t.M * t.N).halfWidth * 2f; minW = Mathf.Min(minW, wd); maxW = Mathf.Max(maxW, wd); }
                sb.AppendLine($"Width: {U.FtS(minW)} (straights) to {U.FtS(maxW)} (turns). Four-wide: 4 x kart {U.FtS(4 * setup.FootWidth)} -> {(4 * setup.FootWidth <= minW ? "fits" : "DOES NOT FIT")}");
                sb.AppendLine($"Kart footprint: {U.InS(setup.FootWidth)} wide (nerf to nerf), {U.InS(setup.FootFront + setup.FootRear)} long (bumper to bumper); front bumper {U.InS(setup.FootFront - setup.FrontTireLead)} ahead of the front tires, rear bumper {U.InS(setup.FootRear - setup.RearTireTrail)} behind the rears; nerfs {U.InS(setup.FootRight - setup.TireOuter(true), 2)} (R) / {U.InS(setup.FootLeft - setup.TireOuter(false), 2)} (L) outside the tires; frame and tube bottoms {U.InS(setup.frameClearance)} / {U.InS(setup.TubeBottomCentre - setup.TubeRadius)} off the ground");
                var rnd = new System.Random(3); float maxErr = 0f; int nOk = 0;
                var surfCol = w.surfaceGo.GetComponent<MeshCollider>();
                for (int i = 0; i < 400; i++)
                {
                    float s = (float)rnd.NextDouble() * t.N; var f = t.Frame(s);
                    float d = ((float)rnd.NextDouble() * 2f - 1f) * (f.halfWidth - 0.05f);
                    var p = f.pos + f.outN * d;
                    if (surfCol.Raycast(new Ray(new Vector3(p.x, 10f, p.y), Vector3.down), out var hit, 20f))
                    { maxErr = Mathf.Max(maxErr, Mathf.Abs(hit.point.y - t.SurfaceHeight(p.x, p.y))); nOk++; }
                }
                sb.AppendLine($"Collider vs SurfaceHeight(x,z): {nOk}/400 rays hit, max gap {U.InS(maxErr, 2)} (mesh facets between vertices)");
                t.FindRoughestPoint(out float rs, out float rd, out float rh);
                sb.AppendLine($"Roughest rise (normal prep): u={rs / t.N:F3}, {U.FtS(rd)} from center, +{U.InS(rh, 2)}");
                float outerH = 0; for (int i = 0; i < t.N; i++) { var f = t.Frame(i); outerH = Mathf.Max(outerH, t.LayerBanking(f, f.halfWidth)); }
                sb.AppendLine($"Banking {cfg.bankingDeg}°: outside edge up to {U.InS(outerH)} above the inside edge");
                // Groove vs berm: right-side tire outer face to berm start, minimum around the lap
                float rrOuter = Mathf.Abs(setup.WheelMount(Corner.RR).x) + setup.TireWidth(3) * 0.5f;
                float minClr = float.MaxValue; float atU = 0;
                for (int i = 0; i < t.M; i++) { float s = (float)i / t.M * t.N; var f = t.Frame(s); float c = (f.halfWidth - cfg.bermWidth) - (f.LineOffset + rrOuter); if (c < minClr) { minClr = c; atU = s / t.N; } }
                sb.AppendLine($"Groove: right-side tires to berm, closest {U.InS(minClr, 2)} at u={atU:F3} (rule: right up to it, never on it)");
                foreach (var b in t.barriers) sb.AppendLine($"Barrier {b.type}: u {b.u0:F3}–{b.u1:F3} ({U.FtS((b.u1 - b.u0) * t.centerLength, 0)} along the centerline)");
                w.Destroy();
            }

            // ---------- Z. surface scan near turn 3 entry (diagnostic)
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-okt-trace") >= 0)
            {
                var w = World.Build(json); var t = w.track;
                var mc = w.surfaceGo.GetComponent<MeshCollider>();
                for (float u = 0.600f; u <= 0.6201f; u += 0.002f)
                {
                    var line = new StringBuilder($"  scan u={u:F3}: ");
                    var f = t.Frame(u * t.N);
                    for (float d = 2.9f; d <= 5.31f; d += 0.2f)
                    {
                        var p = f.pos + f.outN * d;
                        float hf = t.SurfaceHeightSD(u * t.N, d);
                        float hm = float.NaN;
                        if (mc.Raycast(new Ray(new Vector3(p.x, 20f, p.y), Vector3.down), out var hh, 40f)) hm = hh.point.y;
                        line.Append($"{d:F1}:{hf:F3}/{hm:F3} ");
                    }
                    sb.AppendLine(line.ToString());
                }
                w.Destroy();
            }

            // ---------- L. scales (level ground, like the book's pads)
            {
                var ks = new KartSetup();
                sb.AppendLine($"Scales target (book Ch 27): nose {ks.frontWeightFrac * 100f:F1}%, left {ks.leftWeightFrac * 100f:F1}%, cross {ks.crossweightFrac * 100f:F1}% (base cross before spindle warp {ks.BaseCrossweight * 100f:F1}%, warp {U.InS(ks.CrossWarpOffset(), 3)} per spindle)");
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.localScale = new Vector3(20f, 1f, 20f); floor.transform.position = new Vector3(0f, 99.5f, 0f);
                World.ApplyPhysicsSettings();
                var kp = KartFactory.Create("ScaleKart", 0, Color.red, ks, null);
                kp.transform.position = new Vector3(0f, 100.005f, 0f);
                Physics.SyncTransforms();
                int settle = Mathf.RoundToInt(3f / World.FixedDt), avgN = Mathf.RoundToInt(1f / World.FixedDt);
                var lsum = new float[4];
                for (int i = 0; i < settle; i++)
                {
                    kp.steerDeg = 0f; kp.accelCmd = -1f; kp.Step(World.FixedDt); Physics.Simulate(World.FixedDt);
                    if (i >= settle - avgN) for (int j = 0; j < 4; j++) lsum[j] += kp.load[j] / avgN;
                }
                float tot = lsum[0] + lsum[1] + lsum[2] + lsum[3];
                sb.AppendLine($"Scales (level, 1 s avg): LF {U.LbfS(lsum[0])} RF {U.LbfS(lsum[1])} LR {U.LbfS(lsum[2])} RR {U.LbfS(lsum[3])} total {U.LbfS(tot)} -> nose {(lsum[0] + lsum[1]) / tot * 100f:F1}%, left {(lsum[0] + lsum[2]) / tot * 100f:F1}%, cross {(lsum[1] + lsum[2]) / tot * 100f:F1}%");
                Object.DestroyImmediate(kp.gameObject); Object.DestroyImmediate(floor);
            }

            // ---------- B. static sag (kart parked: vMax 0)
            {
                var w = World.Build(json, null, null, new DriverConfig { vMaxMph = 0f });
                var k = w.karts[0];
                int settle = Mathf.RoundToInt(3f / World.FixedDt), avgN = Mathf.RoundToInt(1f / World.FixedDt);
                var lsum = new float[4]; var csum = new float[4];
                for (int i = 0; i < settle; i++)
                {
                    w.StepOnce();
                    if (i >= settle - avgN) for (int j = 0; j < 4; j++) { lsum[j] += k.load[j] / avgN; csum[j] += k.compression[j] / avgN; }
                }
                for (int j = 0; j < 4; j++) { k.load[j] = lsum[j]; k.compression[j] = csum[j]; } // report 1 s averages
                float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(k.transform.up, k.transform.forward), Vector3.up, k.transform.forward);
                sb.AppendLine($"Static (parked on the banking, last-second averages): compression LF {k.compression[0] / U.In:F2} RF {k.compression[1] / U.In:F2} LR {k.compression[2] / U.In:F2} RR {k.compression[3] / U.In:F2} in; loads LF {U.LbfS(k.load[0])} RF {U.LbfS(k.load[1])} LR {U.LbfS(k.load[2])} RR {U.LbfS(k.load[3])}; roll vs level {roll:F1}°; creep {U.MphS(k.Body.linearVelocity.magnitude, 2)}");
                w.Destroy();
            }

            // ---------- S. optional controller sweep
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-okt-sweep") >= 0)
            {
                foreach (float la in new[] { 0.6f, 0.45f, 0.35f, 0.25f })
                    foreach (float yd in new[] { 0f, 0.1f, 0.25f })
                    {
                        var w = World.Build(json, null, null, new DriverConfig { lookaheadTime = la, yawDamping = yd });
                        var k = w.karts[0]; var ai = w.drivers[0];
                        float maxYaw = 0, maxDev = 0;
                        for (int i = 0; i < (hz / 60) * 60 * 90; i++)
                        {
                            w.StepOnce();
                            if (w.simTime > 3f) { maxYaw = Mathf.Max(maxYaw, YawErr(w, ai, k)); maxDev = Mathf.Max(maxDev, Mathf.Abs(ai.d - w.track.Frame(ai.s).LineOffset)); }
                        }
                        float avg = 0; int nl = 0; for (int i = 1; i < ai.lapTimes.Count; i++) { avg += ai.lapTimes[i]; nl++; }
                        sb.AppendLine($"SWEEP la={la:F2} yd={yd:F2}: laps {ai.lapsCompleted} best {ai.bestLap:F2} mean {(nl > 0 ? avg / nl : 0):F2} maxYawErr {maxYaw:F0}° maxLineDev {U.InS(maxDev)} minBody->barrier {U.InS(ai.minWallGap)} breaches {ai.bandBreachSteps}");
                        w.Destroy();
                    }
            }

            // ---------- G. gearing x corner-speed sweep (anchor: Franklin's 10.5–10.8 s laps, 47–48 mph top)
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-okt-gear") >= 0)
            {
                foreach (int axle in new[] { 61, 63, 65 })
                    foreach (float slow in new[] { 0.30f, 0.27f, 0.25f, 0.22f, 0.19f })
                    {
                        var ks = new KartSetup { axleTeeth = axle };
                        var w = World.Build(json, null, ks, new DriverConfig { cornerSlow = slow, vMaxMph = 52f });
                        var k = w.karts[0]; var ai = w.drivers[0];
                        float top = 0, low = 99, maxYaw = 0; int slide = 0, n = 0;
                        for (int i = 0; i < (hz / 60) * 60 * 75; i++)
                        {
                            w.StepOnce();
                            if (w.simTime > 15f) { n++; top = Mathf.Max(top, k.ForwardSpeed); low = Mathf.Min(low, k.ForwardSpeed); maxYaw = Mathf.Max(maxYaw, YawErr(w, ai, k)); if (k.sliding[2] || k.sliding[3]) slide++; }
                        }
                        float avg = 0; int nl = 0; for (int i = 1; i < ai.lapTimes.Count; i++) { avg += ai.lapTimes[i]; nl++; }
                        sb.AppendLine($"GEAR {ks.clutchTeeth}/{axle} ({ks.gearRatio:F2}:1, {U.MphS(ks.SpeedAtTarget)} at {ks.targetRpm:F0} rpm) cornerSlow {slow:F2}: laps {ai.lapsCompleted} mean {(nl > 0 ? avg / nl : 0):F2} s; speed {U.MphS(low)}–{U.MphS(top)}; rear sliding {100f * slide / Mathf.Max(1, n):F0}%; max angle {maxYaw:F0}°; closest {U.InS(ai.minWallGap)}; breaches {ai.bandBreachSteps}; off-track {ai.offTrackSteps}");
                        w.Destroy();
                    }
            }

            // ---------- C. clean laps at normal and poor prep, + drive-over
            foreach (float prep in new[] { 1f, 2f })
            {
                var w = World.Build(json, new SurfaceConfig { prepScale = prep });
                var k = w.karts[0]; var ai = w.drivers[0];
                int steps = (hz / 60) * 60 * 120, counted = 0, slideSteps = 0, looseSteps = 0, upside = 0;
                float lapMaxRpm = 0, lapMinRpm = 99999; int lastLapSeen = 0; var drops = new StringBuilder(); float maxRoll = 0, minSpd = 99, maxSpd = 0, maxYaw = 0, minClear = 99f; int scrape = 0, clrSamples = 0, liftLR = 0, liftAny = 0; float maxCompLoaded = 0;
                for (int i = 0; i < steps; i++)
                {
                    w.StepOnce();
                    if (w.simTime > 15f)
                    {
                        counted++;
                        maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Vector3.SignedAngle(Vector3.ProjectOnPlane(k.transform.up, k.transform.forward), Vector3.up, k.transform.forward)));
                        if (k.sliding[2] || k.sliding[3]) slideSteps++;
                        if (!k.grounded[2]) liftLR++;
                        if (k.GroundedCount < 4) liftAny++;
                        maxCompLoaded = Mathf.Max(maxCompLoaded, Mathf.Max(k.compression[1], k.compression[3]));
                        if (k.surfaceGripNow[1] < 0.99f || k.surfaceGripNow[3] < 0.99f) looseSteps++;
                        minSpd = Mathf.Min(minSpd, k.ForwardSpeed); maxSpd = Mathf.Max(maxSpd, k.ForwardSpeed);
                        maxYaw = Mathf.Max(maxYaw, YawErr(w, ai, k));
                        lapMaxRpm = Mathf.Max(lapMaxRpm, k.EngineRpm); lapMinRpm = Mathf.Min(lapMinRpm, k.EngineRpm);
                    }
                    if (ai.lapsCompleted != lastLapSeen) { if (lastLapSeen >= 1 && lapMaxRpm > 0) drops.Append($"{lapMaxRpm:F0}/{lapMinRpm:F0} "); lastLapSeen = ai.lapsCompleted; lapMaxRpm = 0; lapMinRpm = 99999; }
                    if (k.UpsideDown) upside++;
                    if (w.simTime > 15f && (i % 4 == 0))
                    {
                        float c = LowestClearance(w, k);
                        minClear = Mathf.Min(minClear, c); clrSamples++; if (c < 0.05f * U.In) scrape++;
                    }
                }
                var laps = new StringBuilder(); foreach (var l in ai.lapTimes) laps.Append(l.ToString("F2")).Append(' ');
                float avg = 0; int nl = 0; for (int i = 1; i < ai.lapTimes.Count; i++) { avg += ai.lapTimes[i]; nl++; }
                string label = prep > 1f ? "poor prep" : "normal prep";
                sb.AppendLine($"Laps ({label}): {ai.lapsCompleted} in {w.simTime:F0} s — {laps}");
                sb.AppendLine($"   best {ai.bestLap:F2}  mean (excl. lap 1) {(nl > 0 ? avg / nl : 0):F2}  speed {U.MphS(minSpd)}–{U.MphS(maxSpd)}  max roll {maxRoll:F1}°  max slip angle vs track {maxYaw:F0}°");
                sb.AppendLine($"   engine max/min rpm per lap (MyChron DROP = max - min): {drops}");
                sb.AppendLine($"   lowest frame/tube clearance {U.InS(minClear, 2)}; scraping {100f * scrape / Mathf.Max(1, clrSamples):F1}% of the time; right-side tire squash max {U.InS(maxCompLoaded, 2)} (static {U.InS(k.setup.staticSag, 2)}); inside rear (LR) off the ground {100f * liftLR / Mathf.Max(1, counted):F1}%, any wheel {100f * liftAny / Mathf.Max(1, counted):F1}%");
                sb.AppendLine($"   rear sliding {100f * slideSteps / Mathf.Max(1, counted):F1}% of the time  right tires on loose dirt {100f * looseSteps / Mathf.Max(1, counted):F1}%  max air {k.maxAirTime:F2} s  max compression {U.InS(k.maxCompression, 2)}  closest body->barrier {U.InS(ai.minWallGap)}  wall breaches {ai.bandBreaches}  off-track steps {ai.offTrackSteps}  upside-down steps {upside}");

                w.track.FindRoughestPoint(out ai.driveOverS, out ai.driveOverD, out float hb);
                k.maxAirTime = 0; k.maxCompression = 0;
                bool done = false; ai.onDriveOverDone = _ => done = true; ai.driveOverArmed = true;
                int unloaded = 0; float closest = float.MaxValue, spdAt = 0;
                for (int i = 0; i < (hz / 60) * 60 * 40 && !done; i++)
                {
                    w.StepOnce();
                    if (k.GroundedCount < 4) unloaded++;
                    var pf = w.track.Frame(ai.driveOverS); var pp = pf.pos + pf.outN * ai.driveOverD;
                    float dd = Vector2.Distance(new Vector2(k.transform.position.x, k.transform.position.z), pp);
                    if (dd < closest) { closest = dd; spdAt = k.ForwardSpeed; }
                }
                sb.AppendLine($"   Drive-over ({label}): rise +{U.InS(hb, 2)} at u={ai.driveOverS / w.track.N:F3}; passed within {U.FtS(closest)} at {U.MphS(spdAt)}; done={done}; max air {k.maxAirTime:F2} s; steps with a wheel unloaded {unloaded}; max compression {U.InS(k.maxCompression, 2)}; upside-down {k.UpsideDown}; end u={ai.s / w.track.N:F3} d={ai.d:F2} v={U.MphS(k.ForwardSpeed)} gnd{(k.grounded[0] ? 1 : 0)}{(k.grounded[1] ? 1 : 0)}{(k.grounded[2] ? 1 : 0)}{(k.grounded[3] ? 1 : 0)} load{k.load[0]:F0}/{k.load[1]:F0}/{k.load[2]:F0}/{k.load[3]:F0} patch d={ai.driveOverD:F2}");
                w.Destroy();
            }

            // ---------- K. wall hit: on the back straight a driver error steers the kart toward the rail
            // until it points at the chosen angle, then the AI takes back over. Franklin: a shallow hit
            // skims along the wall rather than stopping dead; a ~45° hit is different.
            foreach (float fric in new[] { 0.10f })
                foreach (float angleDeg in new[] { 5f, 10f, 15f, 20f })
                {
                    var w = World.Build(json, new SurfaceConfig { barrierFriction = fric });
                    var k = w.karts[0]; var ai = w.drivers[0];
                    // start of the back straight from the corner table (v4's rounder ends moved it: a fixed u=0.42 was still in turn 2)
                    float uBack = 0.42f; for (float uu = 0.3f; uu < 0.6f; uu += 0.002f) if (w.track.Frame(uu * w.track.N).corner < 0.1f) { uBack = uu; break; }
                    int phase = 0; float tStart = 0, tHit = 0, vHalf = -1, vBefore = 0, vMin = 99, vAfter = 0, maxYaw = 0, angAtHit = 0; int contact = 0;
                    for (int i = 0; i < (hz / 60) * 60 * 40 && phase < 3; i++)
                    {
                        float u = ai.s / w.track.N;
                        var f = w.track.Frame(ai.s);
                        float headOut = Mathf.Asin(Mathf.Clamp(Vector3.Dot(k.transform.forward, new Vector3(f.outN.x, 0, f.outN.y)), -1f, 1f)) * Mathf.Rad2Deg;
                        if (phase == 0 && w.simTime > 12f && u > uBack && u < uBack + 0.02f)
                        { phase = 1; tStart = w.simTime; vBefore = k.ForwardSpeed; ai.steerOverride = true; ai.steerOverrideDeg = Mathf.Min(28f, angleDeg * 0.6f + 3f); }
                        if (phase == 1 && (headOut >= angleDeg || ai.wallGapNow < 3f * U.In))
                        { ai.steerOverrideDeg = 0f; } // hold straight at the wall until contact
                        if (phase == 1 && (ai.wallGapNow < 3f * U.In || w.simTime - tStart > 2f)) { phase = 2; tHit = w.simTime; angAtHit = headOut; ai.steerOverride = false; }
                        if (phase == 1 && w.simTime > 30f) break;
                        w.StepOnce();
                        if (phase >= 1 && angleDeg == 5f && (i % 2 == 0) && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-okt-trace") >= 0)
                        {
                            var ff = w.track.Frame(ai.s);
                            if (phase == 2 && w.simTime - tHit < 0.6f)
                            {
                                var box = k.GetComponent<BoxCollider>();
                                var hitsO = Physics.OverlapBox(k.transform.TransformPoint(box.center), box.size * 0.5f + Vector3.one * 0.03f, k.transform.rotation);
                                var names = new System.Collections.Generic.List<string>(); foreach (var c in hitsO) if (c != box) names.Add(c.name);
                                sb.AppendLine($"    touching: {string.Join(",", names)}  vel {k.Body.linearVelocity}  angvel {k.Body.angularVelocity}");
                            }
                            sb.AppendLine($"    trace ph{phase} t={w.simTime:F2} u={ai.s / w.track.N:F4} d={ai.d:F2} m hw={ff.halfWidth:F2} barrier={w.track.BarrierAt(ai.s / w.track.N)} gap={ai.wallGapNow:F2} v={k.ForwardSpeed:F1} head={headOut:F0} breaches={ai.bandBreachSteps} pos={k.transform.position} hAbove={(k.transform.position.y - w.track.SurfaceHeightSD(ai.s, ai.d)) / U.In:F1}in roll={Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(k.transform.up, k.transform.forward), k.transform.forward):F0} gnd{(k.grounded[0] ? 1 : 0)}{(k.grounded[1] ? 1 : 0)}{(k.grounded[2] ? 1 : 0)}{(k.grounded[3] ? 1 : 0)} load{k.load[0]:F0}/{k.load[1]:F0}/{k.load[2]:F0}/{k.load[3]:F0} vy={k.Body.linearVelocity.y:F2} contact={(k.GetComponent<KartContact>() != null ? k.GetComponent<KartContact>().lastTrackContact : "")}");
                        }
                        if (phase == 2)
                        {
                            if (ai.wallGapNow < 3f * U.In) contact++;
                            vMin = Mathf.Min(vMin, k.ForwardSpeed);
                            maxYaw = Mathf.Max(maxYaw, YawErr(w, ai, k));
                            vAfter = k.ForwardSpeed;
                            if (vHalf < 0 && w.simTime - tHit >= 0.5f) vHalf = k.ForwardSpeed;
                            if (w.simTime - tHit > 3f) phase = 3;
                        }
                    }
                    if (phase < 2) { sb.AppendLine($"Wall hit {angleDeg:F0}°, rail friction {fric:F2}: never reached the rail"); w.Destroy(); continue; }
                    sb.AppendLine($"Wall hit at {angAtHit:F0}° (asked {angleDeg:F0}°) at {U.MphS(vBefore)}, rail friction {fric:F2}: on the rail {contact * World.FixedDt:F2} s; 0.5 s later {U.MphS(vHalf)}; slowest {U.MphS(vMin)}; 3 s later {U.MphS(vAfter)}; max angle to track {maxYaw:F0}°; closest {U.InS(ai.minWallGap)}; wall breaches {ai.bandBreaches}; off-track {ai.offTrackSteps * World.FixedDt:F1} s; upside-down {k.UpsideDown}");
                    w.Destroy();
                }

            // ---------- R. berm ride: mid turn 1, lock the steering straight for 1 s so the kart runs off the
            // groove, across the turn and up into the berm and rail of turn 2. (Muse N3: the old version rode the
            // berm down the front straight and could leave the track at the turn-1 access-road opening first;
            // turns 1–2 are guardrail end to end, u 0.099–0.582, so there is no opening on this path.)
            {
                var w = World.Build(json);
                var k = w.karts[0]; var ai = w.drivers[0];
                int phase = 0; float t0 = 0, vIn = 0, peakDecel = 0, maxYaw = 0, vOut = 0, minClr = 99f; Vector3 vPrev = Vector3.zero;
                for (int i = 0; i < (hz / 60) * 60 * 40 && phase < 3; i++)
                {
                    float u = ai.s / w.track.N;
                    if (phase == 0 && w.simTime > 12f && u > 0.15f && u < 0.20f) { phase = 2; t0 = w.simTime; vIn = k.ForwardSpeed; ai.steerOverride = true; ai.steerOverrideDeg = 0f; }
                    if (phase == 2 && w.simTime - t0 > 1.0f) { ai.steerOverride = false; }
                    w.StepOnce();
                    var v = k.Body.linearVelocity;
                    if (phase == 2)
                    {
                        if (vPrev != Vector3.zero) peakDecel = Mathf.Max(peakDecel, -Vector3.Dot(v - vPrev, vPrev.normalized) / World.FixedDt / U.G);
                        maxYaw = Mathf.Max(maxYaw, YawErr(w, ai, k));
                        if (i % 3 == 0) minClr = Mathf.Min(minClr, LowestClearance(w, k));
                        if (w.simTime - t0 > 3f) { vOut = k.ForwardSpeed; phase = 3; }
                    }
                    vPrev = v;
                }
                sb.AppendLine($"Berm ride, turns 1-2 (steering locked straight 1 s from {U.MphS(vIn)} mid turn 1): peak decel {peakDecel:F1} g; max angle to track {maxYaw:F0}°; lowest frame/tube clearance {U.InS(minClr, 2)}; 3 s later {U.MphS(vOut)}; wall breaches {ai.bandBreaches}; off-track {ai.offTrackSteps * World.FixedDt:F1} s; upside-down {k.UpsideDown}");
                w.Destroy();
            }

            // ---------- D. berm dust (Franklin): the kart runs its right sides onto the berm along the back
            // straight once, then goes back to the groove. Compare turns 3-4 and the front straight
            // against a clean lap: speed at mid-turn, at the turn exit, and down the front straight.
            {
                float[] probes = { 0.75f, 0.90f, 0.97f };
                float[] clean = new float[3], dusty = new float[3]; float cleanLap = 0, dustyLap = 0, dustAtTurn = 0, dustMid = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    var w = World.Build(json);
                    var k = w.karts[0]; var ai = w.drivers[0];
                    int lapToDust = 2; int pi = 0; float[] dst = pass == 0 ? clean : dusty;
                    for (int i = 0; i < (hz / 60) * 60 * 60 && ai.lapsCompleted < lapToDust + 2; i++)
                    {
                        float u = ai.s / w.track.N;
                        bool onDustLap = ai.lapsCompleted == lapToDust;
                        ai.cfg.lateralOffsetFt = (pass == 1 && onDustLap && u > 0.42f && u < 0.56f) ? 4f : 0f;
                        w.StepOnce();
                        if (onDustLap)
                        {
                            if (pass == 1 && u > 0.60f && u < 0.61f) dustAtTurn = Mathf.Max(dustAtTurn, k.dust[3]);
                            if (pass == 1 && u > 0.745f && u < 0.755f) dustMid = Mathf.Max(dustMid, k.dust[3]);
                            if (pi < 3 && u >= probes[pi]) { dst[pi] = k.ForwardSpeed; pi++; }
                        }
                    }
                    if (ai.lapTimes.Count > lapToDust) { if (pass == 0) cleanLap = ai.lapTimes[lapToDust]; else dustyLap = ai.lapTimes[lapToDust]; }
                    w.Destroy();
                }
                sb.AppendLine($"Berm dust: right-rear dust entering turn 3 {dustAtTurn * 100f:F0}%, at mid turns 3-4 {dustMid * 100f:F0}%");
                sb.AppendLine($"   speed clean vs dusted — mid-turn {U.MphS(clean[0])} vs {U.MphS(dusty[0])}; turn exit {U.MphS(clean[1])} vs {U.MphS(dusty[1])}; front straight {U.MphS(clean[2])} vs {U.MphS(dusty[2])}; lap {cleanLap:F2} vs {dustyLap:F2} s");
            }
            return sb.ToString();
        }
    }
}
