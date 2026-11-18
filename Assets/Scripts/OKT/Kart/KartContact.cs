// Kart-on-kart contact log: every collision between two karts, with its impulse, so the director
// can tell a contact spin from a self spin (and, in M3, judge the causer: spec §8.3).
// [ExecuteAlways] so callbacks also fire in the headless soak (edit-mode Physics.Simulate).
using UnityEngine;

namespace OKT
{
    [ExecuteAlways]
    public class KartContact : MonoBehaviour
    {
        public float lastContactTime = -99f;     // World.simTime of the last kart contact
        public float lastImpulse;                // N·s of that contact
        public KartPhysics lastOther;
        public int contacts, hardContacts;       // counts this race
        public float hardImpulse = 150f;         // N·s that counts as a hard hit [TEST VALUE]
        [System.NonSerialized] public World world;

        void OnCollisionEnter(Collision c) => Record(c);
        void OnCollisionStay(Collision c) => Record(c);

        // debug: last contact with a non-kart collider (rail, wall, surface) — which of my parts, which surface, normal
        // Kept as raw values and only formatted when read (the forensics, ~10 Hz): formatting it on every contact,
        // every step, for every kart was a measurable slice of Physics.Simulate at 40 karts (perf pass 2026-11-17).
        Collider tcThis, tcOther; Vector3 tcNormal; float tcImp, tcY;
        public string lastTrackContact => tcThis == null || tcOther == null ? ""
            : $"{tcThis.name}->{tcOther.name} n({tcNormal.x:F2},{tcNormal.y:F2},{tcNormal.z:F2}) imp{tcImp:F0} y{tcY:F2}";
        void Record(Collision c)
        {
            if (c.rigidbody == null)
            {
                if (c.contactCount > 0)
                {
                    var cp = c.GetContact(0);
                    tcThis = cp.thisCollider; tcOther = c.collider; tcNormal = cp.normal; tcImp = c.impulse.magnitude; tcY = cp.point.y;
                }
                return;
            }
            var other = c.rigidbody.GetComponent<KartPhysics>();
            if (other == null) return;
            float imp = c.impulse.magnitude;
            if (imp < 1f) return;
            float now = world != null ? world.simTime : 0f;
            if (now - lastContactTime > 0.5f) { contacts++; if (imp > hardImpulse) hardContacts++; }
            else if (imp > hardImpulse && lastImpulse <= hardImpulse) hardContacts++;
            lastContactTime = now; lastImpulse = Mathf.Max(imp, now - lastContactTime < 0.05f ? lastImpulse : 0f); lastOther = other;
        }
    }
}
