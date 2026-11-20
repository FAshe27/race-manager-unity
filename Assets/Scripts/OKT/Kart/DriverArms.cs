// The driver's hands stay on the steering wheel (Franklin 11-20: "Do the hands and arms animate and turn with the
// steering wheel?"). SteeringLinkage turns the model's steering column (SteeringShaft, about the line to its ColumnTop
// marker) to whatever angle the linkage needs for the front wheels, and hands that angle here; each arm reaches for
// its grip with a two-bone solve: shoulder fixed, hand carried round with the wheel, elbow bending the way it bends
// at rest. Visual only: no colliders, no say in the physics (the driver's capsules stay where they are).
using UnityEngine;

namespace OKT
{
    public class DriverArms : MonoBehaviour
    {
        class Arm
        {
            public Transform upper, fore, hand;
            public Vector3 sh, el, wr, pole;            // rest, kart space
            public Quaternion upperR, foreR, handR;      // rest rotations, kart space
            public float l1, l2;
        }

        KartPhysics kp;
        Transform kart, shaft;
        Vector3 axis;                                    // column axis, kart space
        Quaternion shaftR;
        Arm[] arms;
        float shown = float.NaN;

        public static DriverArms Attach(GameObject driverModel, KartPhysics kp, Transform shaft, Transform columnTop)
        {
            if (kp == null || shaft == null || columnTop == null) return null;
            var a = driverModel.AddComponent<DriverArms>();
            a.kp = kp; a.kart = kp.transform; a.shaft = shaft;
            a.axis = (a.L(columnTop.position) - a.L(shaft.position)).normalized;
            a.shaftR = a.LR(shaft.rotation);
            var list = new System.Collections.Generic.List<Arm>();
            foreach (var s in new[] { "L", "R" })
            {
                var arm = new Arm
                {
                    upper = Find(driverModel.transform, "Upper" + s),
                    fore = Find(driverModel.transform, "Fore" + s),
                    hand = Find(driverModel.transform, "Hand" + s),
                };
                if (arm.upper == null || arm.fore == null || arm.hand == null) continue;
                arm.sh = a.L(arm.upper.position); arm.el = a.L(arm.fore.position); arm.wr = a.L(arm.hand.position);
                arm.upperR = a.LR(arm.upper.rotation); arm.foreR = a.LR(arm.fore.rotation); arm.handR = a.LR(arm.hand.rotation);
                arm.l1 = (arm.el - arm.sh).magnitude; arm.l2 = (arm.wr - arm.el).magnitude;
                var d = (arm.wr - arm.sh).normalized;
                arm.pole = Vector3.ProjectOnPlane(arm.el - arm.sh, d).normalized;   // the way the elbow points at rest
                list.Add(arm);
            }
            a.arms = list.ToArray();
            return a;
        }

        Vector3 L(Vector3 world) => kart.InverseTransformPoint(world);
        Quaternion LR(Quaternion world) => Quaternion.Inverse(kart.rotation) * world;

        /// <summary>Hands on a steering wheel turned this far (degrees about the column, + = right; SteeringLinkage.ShaftDeg).
        /// The column itself is turned by SteeringLinkage.</summary>
        public void Pose(float wheelDeg)
        {
            if (kart == null || wheelDeg == shown) return;
            shown = wheelDeg;
            // Unity turns clockwise about an axis seen from its tip: the driver sits at the column's top end,
            // so + (right) is the wheel turning clockwise in his hands.
            var q = Quaternion.AngleAxis(wheelDeg, axis);
            var piv = L(shaft.position);
            foreach (var a in arms)
            {
                var wr = piv + q * (a.wr - piv);
                var el = TwoBone(a.sh, wr, a.l1, a.l2, a.pole);
                a.upper.rotation = kart.rotation * Quaternion.FromToRotation(a.el - a.sh, el - a.sh) * a.upperR;
                a.fore.rotation = kart.rotation * Quaternion.FromToRotation(a.wr - a.el, wr - el) * a.foreR;
                a.hand.rotation = kart.rotation * q * a.handR;
            }
        }

        static Vector3 TwoBone(Vector3 a, Vector3 target, float l1, float l2, Vector3 pole)
        {
            var d = target - a;
            float dist = Mathf.Min(d.magnitude, l1 + l2 - 1e-5f);
            var dn = d.normalized;
            float x = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(l1 * l1 - x * x, 0f));
            var p = Vector3.ProjectOnPlane(pole, dn).normalized;
            return a + dn * x + p * h;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }
    }
}
