// Track model: Franklin's Test Track One JSON -> closed Catmull-Rom spline,
// per-point frames, and the ONE SurfaceHeight function (spec §4.2) that the
// mesh, the AI and the soak checker all read. Pure C#, no scene objects.
//
// Conventions (spec §4.1): world X = json x, world Z = json y, Y up.
// Lateral offset d is measured from the centerline along the OUTWARD normal
// (+d = toward the outside wall). lineFrac: 0 = inside edge, 1 = outside edge.
//
// NOTE (M1 REPORT deviation): the file's racingLineM points are mirrored across
// the centerline relative to lineFrac (they equal centerline - outward*offset to
// within 0.7 mm). We build the line from lineFrac, which matches the spec text
// ("~0.79 across on the straights, ~0.20 at the apexes") and Franklin's editor.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OKT
{
    /// <summary>
    /// Surface + barrier values. Authored in IMPERIAL (Franklin's units, 2026-11-14);
    /// the SI properties below are what the engine reads.
    /// </summary>
    [Serializable]
    public class SurfaceConfig
    {
        // Layer 1: banking — from the JSON (bankingDeg), constant around the lap (spec §4.2).
        public float bankingDeg = 8f;

        // Layer 2: groove dig. Depth ≈1.2 in (spec ≈3 cm) [TEST VALUE]; width 6.5 ft
        // [TEST VALUE] — middle of Franklin's 5.2–8.2 ft (1.6–2.5 m) range.
        public float grooveDepthIn = 1.2f;
        public float grooveWidthFt = 6.5f;

        // Layer 3: roughness. Franklin: "generally it's smooth; rises and dips are
        // symptoms of poor track prep." Default ±0.8 in peak (spec low end) with
        // 20 ft swells + 10 ft waves [TEST VALUE]. prepScale 2 = "poor prep" toggle
        // (spec high end, ±1.6 in). Franklin: real air only from extreme cases —
        // a divot building up over a night (that's M4 surface wear, not this layer).
        public float roughAmpIn = 0.8f;
        public float roughWaveAFt = 20f;
        public float roughWaveBFt = 10f;
        public float roughWeightA = 0.65f;
        public float prepScale = 1f;
        public int roughSeed = 1337; // [TEST VALUE] any seed
        public float roughFloor = 0.35f;      // fraction present on a clean straight
        public float grooveSmoothing = 0.6f;  // the groove is worn down and smooth

        // Berm: Franklin — 6 in tall x 32 in wide against the outside barrier, loose
        // dirt. "Karts get right up to the berm, but not on it": right sides on it dust
        // up and lose grip into the corner.
        public float bermHeightIn = 6f;
        public float bermWidthIn = 32f;

        // Grass (infield inside the cones, outside run-off): 50% of the groove's grip (Franklin).
        public float gripGrass = 0.50f;

        // Berm dust (Franklin, after the M1 playtest): running on the loose berm does NOT
        // scrub speed by itself. It coats the tire; a dusted tire has 70% bite until it
        // cleans off, which happens by about halfway through the next corner — and the
        // lost bite costs momentum down the following straight.
        public float gripDusted = 0.70f;       // Franklin
        public float dustPickupTime = 0.15f;   // s on the berm to fully coat a tire [TEST VALUE]
        public float dustCleanTime = 0.9f;     // s of full cornering load to clean it off (≈ half a corner) [TEST VALUE]
        public float dustCleanStraight = 0.1f; // fraction cleaned per second rolling straight [TEST VALUE]

        // The groove keeps the right-side tires off the berm: line offset is capped
        // at (edge - berm - this). = RR tire outer face from kart centre + ~0.5 in.
        public float grooveBermClearanceIn = 26.6f;

        // Barriers (layout from the JSON 'barriers' list — Franklin's overhead).
        public float guardrailHeightIn = 20f;  // spec ≈0.5 m [TEST VALUE]
        public float fenceHeightFt = 6f;       // chain-link + low wall [TEST VALUE]
        public float barrierThicknessIn = 12f; // build value [TEST VALUE]
        public float barrierFriction = 0.10f;  // steel rail vs bodywork: shallow hits skim [TEST VALUE]

        // Outside run-off behind the barrier/openings: flat, then a grass bank down to infield level.
        public float outerFlatFt = 3f;
        public float outerBankFt = 20f;

        // Mesh resolution [TEST VALUE]: rows per JSON sample; columns across.
        public int rowsPerSample = 4;
        public int surfaceCols = 16;
        public int bermCols = 4;

        // ---- SI (engine) values
        public float grooveDepth => grooveDepthIn * U.In;
        public float grooveWidth => grooveWidthFt * U.Ft;
        public float roughAmp => roughAmpIn * U.In;
        public float roughWaveA => roughWaveAFt * U.Ft;
        public float roughWaveB => roughWaveBFt * U.Ft;
        public float bermHeight => bermHeightIn * U.In;
        public float bermWidth => bermWidthIn * U.In;
        public float grooveBermClearance => grooveBermClearanceIn * U.In;
        public float guardrailHeight => guardrailHeightIn * U.In;
        public float fenceHeight => fenceHeightFt * U.Ft;
        public float barrierThickness => barrierThicknessIn * U.In;
        public float outerFlat => outerFlatFt * U.Ft;
        public float outerBank => outerBankFt * U.Ft;
    }

    public enum BarrierType { None = 0, Guardrail = 1, Fence = 2 }

    /// <summary>Ground under a tire: grip multiplier of the surface itself, and whether it's loose (dusts the tire).</summary>
    public struct GroundSample { public float grip; public bool loose; }

    public struct TrackFrame
    {
        public Vector2 pos;      // centerline point (world XZ)
        public Vector2 tan;      // unit tangent, direction of travel
        public Vector2 outN;     // unit outward normal
        public float halfWidth;
        public float lineFrac;
        public float corner;
        public float LineOffset => (lineFrac - 0.5f) * 2f * halfWidth;
    }

    public sealed class TrackModel
    {
        public readonly string name;
        public readonly int N;
        public readonly float lapLengthM;
        public readonly float goU;
        public readonly SurfaceConfig cfg;

        readonly Vector2[] c;
        readonly float[] hw, frac, corner;
        readonly float outSign;

        // Dense tables (N * rowsPerSample) for locating and arc length.
        public readonly int M;
        readonly Vector2[] dPos;
        readonly float[] dArc;      // centerline arc length at dense index
        public readonly float centerLength;

        // Racing line (from lineFrac), dense, with arc length.
        readonly Vector2[] lPos;
        readonly float[] lArc;
        public readonly float lineLength;

        float tanBank;

        public struct BarrierSpan { public BarrierType type; public float u0, u1; }
        public readonly List<BarrierSpan> barriers = new List<BarrierSpan>();

        /// <summary>What stands at the outside edge at lap fraction u (None = an opening).</summary>
        public BarrierType BarrierAt(float u)
        {
            u = Mathf.Repeat(u, 1f);
            foreach (var b in barriers)
                if ((u >= b.u0 && u < b.u1) || (u + 1f >= b.u0 && u + 1f < b.u1)) return b.type;
            return BarrierType.None;
        }

        public TrackModel(string json, SurfaceConfig config)
        {
            cfg = config ?? new SurfaceConfig();
            var root = (Dictionary<string, object>)MiniJson.Parse(json);
            name = root.TryGetValue("name", out var nm) ? (string)nm : "track";
            N = Convert.ToInt32(root["samples"]);
            lapLengthM = Convert.ToSingle(root["lapLengthM"]);
            goU = Convert.ToSingle(root["goU"]);
            if (root.TryGetValue("bankingDeg", out var bd)) cfg.bankingDeg = Convert.ToSingle(bd);

            c = ReadPts(root["centerlineM"], N);
            hw = ReadFloats(root["halfWidthM"], N);
            frac = ReadFloats(root["lineFrac"], N);
            corner = ReadFloats(root["cornerTable"], N);

            // Barrier layout (v3+). Without it (v2 file): one continuous guardrail.
            if (root.TryGetValue("barriers", out var bo) && bo is List<object> bl)
            {
                foreach (var e in bl)
                {
                    var d = (Dictionary<string, object>)e;
                    string ty = (string)d["type"];
                    barriers.Add(new BarrierSpan
                    {
                        type = ty == "fence" ? BarrierType.Fence : BarrierType.Guardrail,
                        u0 = Convert.ToSingle(d["u0"]),
                        u1 = Convert.ToSingle(d["u1"]),
                    });
                }
            }
            else barriers.Add(new BarrierSpan { type = BarrierType.Guardrail, u0 = 0f, u1 = 1f });

            // Travel direction: CCW (positive area in XZ) => outward normal is right of travel.
            float area = 0f;
            for (int i = 0; i < N; i++) { var a = c[i]; var b = c[(i + 1) % N]; area += a.x * b.y - b.x * a.y; }
            outSign = area >= 0 ? 1f : -1f;

            RecomputeBanking();

            M = N * Mathf.Max(1, cfg.rowsPerSample);
            dPos = new Vector2[M];
            dArc = new float[M + 1];
            lPos = new Vector2[M];
            lArc = new float[M + 1];
            for (int k = 0; k < M; k++)
            {
                var f = Frame((float)k / M * N);
                dPos[k] = f.pos;
                lPos[k] = f.pos + f.outN * f.LineOffset;
            }
            for (int k = 0; k < M; k++)
            {
                dArc[k + 1] = dArc[k] + Vector2.Distance(dPos[k], dPos[(k + 1) % M]);
                lArc[k + 1] = lArc[k] + Vector2.Distance(lPos[k], lPos[(k + 1) % M]);
            }
            centerLength = dArc[M];
            lineLength = lArc[M];
        }

        public void RecomputeBanking() { tanBank = Mathf.Tan(cfg.bankingDeg * Mathf.Deg2Rad); }

        static Vector2[] ReadPts(object o, int n)
        {
            var l = (List<object>)o; var r = new Vector2[n];
            for (int i = 0; i < n; i++) { var p = (List<object>)l[i]; r[i] = new Vector2(Convert.ToSingle(p[0]), Convert.ToSingle(p[1])); }
            return r;
        }
        static float[] ReadFloats(object o, int n)
        {
            var l = (List<object>)o; var r = new float[n];
            for (int i = 0; i < n; i++) r[i] = Convert.ToSingle(l[i]);
            return r;
        }

        // ---------------------------------------------------------------- spline
        int W(int i) => ((i % N) + N) % N;
        public float WrapS(float s) { s %= N; return s < 0 ? s + N : s; }

        static float CR(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
        static float CRd(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t;
            return 0.5f * ((-p0 + p2) + 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t + 3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t2);
        }

        /// <summary>Frame at continuous sample parameter s in [0, N).</summary>
        public TrackFrame Frame(float s)
        {
            s = WrapS(s);
            int i1 = (int)Mathf.Floor(s); float t = s - i1;
            int i0 = W(i1 - 1), i2 = W(i1 + 1), i3 = W(i1 + 2); i1 = W(i1);
            var f = new TrackFrame();
            f.pos = new Vector2(CR(c[i0].x, c[i1].x, c[i2].x, c[i3].x, t), CR(c[i0].y, c[i1].y, c[i2].y, c[i3].y, t));
            var d = new Vector2(CRd(c[i0].x, c[i1].x, c[i2].x, c[i3].x, t), CRd(c[i0].y, c[i1].y, c[i2].y, c[i3].y, t));
            f.tan = d.normalized;
            f.outN = new Vector2(f.tan.y, -f.tan.x) * outSign;
            f.halfWidth = CR(hw[i0], hw[i1], hw[i2], hw[i3], t);
            f.lineFrac = Mathf.Clamp01(CR(frac[i0], frac[i1], frac[i2], frac[i3], t));
            // Franklin: the groove gets the right-side tires right up to the berm, never on it.
            float dMax = f.halfWidth - cfg.bermWidth - cfg.grooveBermClearance;
            float fMax = 0.5f + dMax / (2f * f.halfWidth);
            if (f.lineFrac > fMax) f.lineFrac = fMax;
            f.corner = Mathf.Clamp01(CR(corner[i0], corner[i1], corner[i2], corner[i3], t));
            return f;
        }

        /// <summary>Centerline arc length (m) at s.</summary>
        public float ArcAt(float s)
        {
            float k = WrapS(s) / N * M; int k0 = (int)k; float t = k - k0;
            return Mathf.Lerp(dArc[k0], dArc[k0 + 1], t);
        }

        /// <summary>World XZ -> (s, d). hint: dense index to start from (-1 = full search).</summary>
        public void Locate(Vector2 p, ref int hint, out float s, out float d)
        {
            int best = 0; float bestD2 = float.MaxValue;
            if (hint < 0 || hint >= M)
            {
                for (int k = 0; k < M; k++) { float q = (dPos[k] - p).sqrMagnitude; if (q < bestD2) { bestD2 = q; best = k; } }
            }
            else
            {
                const int win = 48;
                for (int j = -win; j <= win; j++)
                {
                    int k = ((hint + j) % M + M) % M;
                    float q = (dPos[k] - p).sqrMagnitude; if (q < bestD2) { bestD2 = q; best = k; }
                }
                // Lost (e.g. a respawn): fall back to a full search.
                if (bestD2 > 100f) { hint = -1; Locate(p, ref hint, out s, out d); return; }
            }
            hint = best;
            // Refine on the neighbouring segments.
            int kp = (best - 1 + M) % M, kn = (best + 1) % M;
            float sk = ProjectSeg(p, kp, best, out float e1);
            float sn = ProjectSeg(p, best, kn, out float e2);
            float kf = e1 < e2 ? (kp + sk) : (best + sn);
            s = WrapS(kf / M * N);
            var f = Frame(s);
            d = Vector2.Dot(p - f.pos, f.outN);
        }

        float ProjectSeg(Vector2 p, int a, int b, out float err)
        {
            Vector2 A = dPos[a], B = dPos[b], ab = B - A;
            float t = Mathf.Clamp01(Vector2.Dot(p - A, ab) / Mathf.Max(1e-9f, ab.sqrMagnitude));
            err = (A + ab * t - p).sqrMagnitude;
            return t;
        }

        // ---------------------------------------------------------------- racing line
        public int LineCount => M;
        public Vector2 LinePoint(int k) => lPos[((k % M) + M) % M];
        public float LineArc(int k) => lArc[((k % M) + M) % M];
        /// <summary>Sample parameter s of dense line index k (line index k sits at the same s as dense centerline k).</summary>
        public float LineS(int k) => (float)(((k % M) + M) % M) / M * N;

        /// <summary>Nearest racing-line dense index to p (windowed around hint).</summary>
        public int NearestLineIndex(Vector2 p, int hint)
        {
            int best = 0; float bestD2 = float.MaxValue;
            if (hint < 0) { for (int k = 0; k < M; k++) { float q = (lPos[k] - p).sqrMagnitude; if (q < bestD2) { bestD2 = q; best = k; } } return best; }
            for (int j = -48; j <= 48; j++)
            {
                int k = ((hint + j) % M + M) % M;
                float q = (lPos[k] - p).sqrMagnitude; if (q < bestD2) { bestD2 = q; best = k; }
            }
            if (bestD2 > 100f) return NearestLineIndex(p, -1);
            return best;
        }

        /// <summary>Walk the racing line from index k forward by dist metres; returns s and the point.</summary>
        public int LineAdvance(int k, float dist)
        {
            float acc = 0f; int j = ((k % M) + M) % M;
            for (int g = 0; g < M && acc < dist; g++)
            {
                int n = (j + 1) % M;
                acc += Vector2.Distance(lPos[j], lPos[n]);
                j = n;
            }
            return j;
        }

        // ---------------------------------------------------------------- surface
        /// <summary>THE surface height function (spec §4.2). World XZ in, height out.</summary>
        public float SurfaceHeight(float x, float z)
        {
            int hint = -1;
            Locate(new Vector2(x, z), ref hint, out float s, out float d);
            return SurfaceHeightSD(s, d);
        }

        /// <summary>Surface height from track coordinates (s, d). Sum of separate layers.</summary>
        public float SurfaceHeightSD(float s, float d)
        {
            var f = Frame(s);
            if (d > f.halfWidth + cfg.outerFlat)
            {
                // Outside run-off: flat behind the barrier, then a grass bank down to infield level.
                float edge = SurfaceHeightSD(s, f.halfWidth);
                float t = Mathf.Clamp01((d - f.halfWidth - cfg.outerFlat) / cfg.outerBank);
                return edge * (1f - t * t * (3f - 2f * t));
            }
            return LayerBanking(f, d) + LayerGroove(f, d) + LayerRoughness(s, f, d) + LayerBerm(f, d);
        }

        /// <summary>
        /// What the ground at (s, d) does to a tire (Franklin): grass has 50% grip;
        /// the berm is loose dirt — full grip underfoot, but it dusts the tire (see KartPhysics).
        /// </summary>
        public GroundSample GroundAtSD(float s, float d)
        {
            var f = Frame(s);
            if (d < -f.halfWidth || d > f.halfWidth + 0.01f) return new GroundSample { grip = cfg.gripGrass, loose = false };
            return new GroundSample { grip = 1f, loose = d > f.halfWidth - cfg.bermWidth };
        }

        public float LayerBanking(in TrackFrame f, float d)
        {
            float dc = Mathf.Clamp(d, -f.halfWidth, f.halfWidth);
            return (dc + f.halfWidth) * tanBank; // 0 at the inside edge (ground level), high side out
        }

        public float LayerGroove(in TrackFrame f, float d)
        {
            float half = cfg.grooveWidth * 0.5f;
            float dl = Mathf.Abs(d - f.LineOffset);
            if (dl >= half) return 0f;
            return -cfg.grooveDepth * 0.5f * (1f + Mathf.Cos(Mathf.PI * dl / half));
        }

        public float GrooveMask(in TrackFrame f, float d)
        {
            float half = cfg.grooveWidth * 0.5f;
            float dl = Mathf.Abs(d - f.LineOffset);
            return dl >= half ? 0f : 0.5f * (1f + Mathf.Cos(Mathf.PI * dl / half));
        }

        /// <summary>Roughness amplitude envelope at (s, d), metres.</summary>
        public float RoughAmplitude(float s, in TrackFrame f, float d)
        {
            float cn = f.corner;
            float transition = 4f * cn * (1f - cn); // peaks at corner entry/exit (corner ≈ 0.5)
            float off = Mathf.Clamp01((Mathf.Abs(d - f.LineOffset) - cfg.grooveWidth * 0.25f) / 1.5f);
            float env = Mathf.Clamp01(cfg.roughFloor + (1f - cfg.roughFloor) * Mathf.Max(transition, off));
            env *= 1f - cfg.grooveSmoothing * GrooveMask(f, d);
            // Fade to zero at the inside edge so the dirt meets the infield grass flush.
            env *= Mathf.Clamp01((d + f.halfWidth) / (2f * U.Ft));
            return cfg.roughAmp * cfg.prepScale * env;
        }

        public float LayerRoughness(float s, in TrackFrame f, float d)
        {
            float dc = Mathf.Clamp(d, -f.halfWidth, f.halfWidth);
            float a = ArcAt(s);
            float n = cfg.roughWeightA * PeriodicNoise(a, dc, cfg.roughWaveA, cfg.roughSeed)
                    + (1f - cfg.roughWeightA) * PeriodicNoise(a, dc, cfg.roughWaveB, cfg.roughSeed + 101);
            return RoughAmplitude(s, f, dc) * n;
        }

        public float LayerBerm(in TrackFrame f, float d)
        {
            float start = f.halfWidth - cfg.bermWidth;
            if (d <= start) return 0f;
            float t = Mathf.Clamp01((d - start) / cfg.bermWidth);
            return cfg.bermHeight * t * t * (3f - 2f * t);
        }

        // Value noise, periodic along the lap so start/finish has no seam. Range ≈ [-1, 1].
        float PeriodicNoise(float arc, float lat, float wave, int seed)
        {
            int period = Mathf.Max(1, Mathf.RoundToInt(centerLength / wave));
            float x = arc / centerLength * period;
            float y = lat / wave + 50f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = Fade(x - x0), ty = Fade(y - y0);
            float v00 = Hash(Mod(x0, period), y0, seed), v10 = Hash(Mod(x0 + 1, period), y0, seed);
            float v01 = Hash(Mod(x0, period), y0 + 1, seed), v11 = Hash(Mod(x0 + 1, period), y0 + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(v00, v10, tx), Mathf.Lerp(v01, v11, tx), ty);
        }
        static int Mod(int a, int m) => ((a % m) + m) % m;
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144269504);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
            }
        }

        // ---------------------------------------------------------------- helpers
        public Vector3 ToWorld(float s, float d)
        {
            var f = Frame(s);
            var p = f.pos + f.outN * d;
            return new Vector3(p.x, SurfaceHeightSD(s, d), p.y);
        }

        /// <summary>Find the biggest rise of the roughness layer inside the drivable band.</summary>
        public void FindRoughestPoint(out float sBest, out float dBest, out float hBest)
        {
            sBest = 0; dBest = 0; hBest = float.MinValue;
            for (int k = 0; k < M; k++)
            {
                float s = (float)k / M * N; var f = Frame(s);
                float lim = f.halfWidth - cfg.bermWidth - 0.7f;
                for (float d = -f.halfWidth + 0.7f; d <= lim; d += 0.1f)
                {
                    float h = LayerRoughness(s, f, d);
                    if (h > hBest) { hBest = h; sBest = s; dBest = d; }
                }
            }
        }
    }
}
