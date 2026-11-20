// The front end steers in the model as it does on Franklin's kart (11-20 late: "The spindles and tie rods definitely
// should move"). The steering shaft turns in its floor heim; the pitman tabs near its bottom swing both tie rods,
// which turn the spindles about their kingpins. Visual only: the physics still turns both front wheels by
// KartPhysics.steerDeg, and this finds the steering-wheel angle whose linkage turns the spindles that far on average.
//
// The linkage is solved from the model's own markers (Kart Parts/steering.py, front_end.py), so it follows the
// real geometry: rigid tie rods, each spindle at the angle that keeps its tie rod's length. A table of spindle
// angles against shaft angle is built once per kart; each frame reads it.
using UnityEngine;

namespace OKT
{
    public class SteeringLinkage : MonoBehaviour
    {
        const float MaxShaftDeg = 150f, StepDeg = 0.5f;
        static readonly string[] Corners = { "LF", "RF" };

        KartPhysics kp;
        Transform kart, shaft;
        Vector3 shaftPivot, shaftAxis;        // kart space
        Quaternion shaftR;
        DriverArms arms;

        class Side
        {
            public Transform spindle, rod;
            public Vector3 kingpin, kpAxis, hole0, end0;   // kart space, at rest
            public Quaternion spindleR, rodR;
            public float len;
        }
        Side[] sides;

        // table: shaft angle -> spindle angles (+ = right, as steerDeg)
        float[] tShaft, tLF, tRF, tMean;
        int tLo, tHi;                         // usable range (the linkage hasn't folded over)
        float shown = float.NaN;
        public float ShaftDeg { get; private set; }

        public static SteeringLinkage Attach(Transform root, KartPhysics kp)
        {
            var shaft = Find(root, "SteeringShaft"); var top = Find(root, "ColumnTop");
            if (kp == null || shaft == null || top == null) return null;
            var l = root.gameObject.AddComponent<SteeringLinkage>();
            l.kp = kp; l.kart = kp.transform; l.shaft = shaft;
            l.shaftPivot = l.L(shaft.position);
            l.shaftAxis = (l.L(top.position) - l.shaftPivot).normalized;
            l.shaftR = l.LR(shaft.rotation);
            var list = new System.Collections.Generic.List<Side>();
            foreach (var c in Corners)
            {
                var sp = Find(root, "Spindle_" + c); var kt = Find(root, "KingpinTop_" + c);
                var hole = Find(root, "TieRodPivot_" + c); var end = Find(root, "TieRodEnd_" + c);
                var rod = Find(root, "TieRod_" + c);
                if (sp == null || kt == null || hole == null || end == null || rod == null) return l.Disable();
                var s = new Side { spindle = sp, rod = rod, kingpin = l.L(sp.position), hole0 = l.L(hole.position), end0 = l.L(end.position),
                                   spindleR = l.LR(sp.rotation), rodR = l.LR(rod.rotation) };
                s.kpAxis = (l.L(kt.position) - s.kingpin).normalized;
                s.len = (s.end0 - s.hole0).magnitude;
                list.Add(s);
            }
            l.sides = list.ToArray();
            l.BuildTable();
            return l;
        }

        SteeringLinkage Disable() { enabled = false; return null; }

        Vector3 L(Vector3 world) => kart.InverseTransformPoint(world);
        Quaternion LR(Quaternion world) => Quaternion.Inverse(kart.rotation) * world;

        Vector3 Hole(Side s, float shaftDeg) => shaftPivot + Quaternion.AngleAxis(shaftDeg, shaftAxis) * (s.hole0 - shaftPivot);
        Vector3 End(Side s, float spDeg) => s.kingpin + Quaternion.AngleAxis(spDeg, s.kpAxis) * (s.end0 - s.kingpin);

        /// <summary>Spindle angle near `guess` that keeps the tie rod its length; NaN if there's none close by.</summary>
        float Solve(Side s, Vector3 hole, float guess)
        {
            float f(float a) => (End(s, a) - hole).magnitude - s.len;
            float lo = guess - 6f, hi = guess + 6f, flo = f(lo), fhi = f(hi);
            if ((flo > 0f) == (fhi > 0f)) return float.NaN;
            for (int i = 0; i < 30; i++)
            {
                float mid = 0.5f * (lo + hi), fm = f(mid);
                if ((fm > 0f) == (flo > 0f)) { lo = mid; flo = fm; } else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        void BuildTable()
        {
            int n = Mathf.RoundToInt(2f * MaxShaftDeg / StepDeg) + 1, mid = n / 2;
            tShaft = new float[n]; tLF = new float[n]; tRF = new float[n]; tMean = new float[n];
            for (int i = 0; i < n; i++) tShaft[i] = (i - mid) * StepDeg;
            tLo = tHi = mid;
            foreach (int dir in new[] { 1, -1 })
            {
                float gL = 0f, gR = 0f, lastMean = 0f;
                for (int i = mid; i >= 0 && i < n; i += dir)
                {
                    float a = Solve(sides[0], Hole(sides[0], tShaft[i]), gL), b = Solve(sides[1], Hole(sides[1], tShaft[i]), gR);
                    if (float.IsNaN(a) || float.IsNaN(b)) break;
                    float m = 0.5f * (a + b);
                    if (i != mid && (m - lastMean) * dir * Mathf.Sign(Slope()) <= 0f) break;   // folded over
                    tLF[i] = a; tRF[i] = b; tMean[i] = m; gL = a; gR = b; lastMean = m;
                    if (dir > 0) tHi = i; else tLo = i;
                }
            }
        }

        // which way the spindles turn for a + (right) shaft turn: + if they go the same way
        float slope = float.NaN;
        float Slope()
        {
            if (float.IsNaN(slope))
            {
                float a = Solve(sides[0], Hole(sides[0], 2f), 0f), b = Solve(sides[1], Hole(sides[1], 2f), 0f);
                slope = Mathf.Sign(0.5f * (a + b));
                if (slope == 0f) slope = 1f;
            }
            return slope;
        }

        void LateUpdate()
        {
            if (kp != null) Pose(kp.steerDeg);
        }

        /// <summary>Front wheels at this angle (degrees, + = right, as KartPhysics.steerDeg).</summary>
        public void Pose(float steerDeg)
        {
            if (sides == null || kart == null || steerDeg == shown) return;
            shown = steerDeg;
            // invert the table: the shaft angle whose spindles average steerDeg (clamped to what the linkage can do)
            int i0 = tLo, i1 = tHi;
            float mLo = tMean[i0], mHi = tMean[i1];
            float target = Mathf.Clamp(steerDeg, Mathf.Min(mLo, mHi), Mathf.Max(mLo, mHi));
            bool rising = mHi > mLo;
            int lo = i0, hi = i1;
            while (hi - lo > 1)
            {
                int m = (lo + hi) / 2;
                if ((tMean[m] < target) == rising) lo = m; else hi = m;
            }
            float t = Mathf.Approximately(tMean[hi], tMean[lo]) ? 0f : Mathf.Clamp01((target - tMean[lo]) / (tMean[hi] - tMean[lo]));
            float sh = Mathf.Lerp(tShaft[lo], tShaft[hi], t);
            float[] sp = { Mathf.Lerp(tLF[lo], tLF[hi], t), Mathf.Lerp(tRF[lo], tRF[hi], t) };
            ShaftDeg = sh;

            var kr = kart.rotation;
            shaft.rotation = kr * Quaternion.AngleAxis(sh, shaftAxis) * shaftR;
            for (int k = 0; k < sides.Length; k++)
            {
                var s = sides[k];
                s.spindle.rotation = kr * Quaternion.AngleAxis(sp[k], s.kpAxis) * s.spindleR;
                var hole = Hole(s, sh); var end = End(s, sp[k]);
                s.rod.position = kart.TransformPoint(hole);
                s.rod.rotation = kr * Quaternion.FromToRotation(s.end0 - s.hole0, end - hole) * s.rodR;
            }
            if (arms == null) arms = GetComponentInChildren<DriverArms>();
            if (arms != null) arms.Pose(sh);
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }
    }
}
