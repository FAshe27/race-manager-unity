// Builds the kart in code: Rigidbody + a compound collider shaped like Franklin's
// chassis photos (2026-11-14): a frame/cockpit box between the wheels, and ROUND
// TUBES for the nerf bars and the front/rear bumpers (bottom tubes 1/2 in off the
// ground). Every collider is drawn by its own primitive, so what you see IS the
// collider (spec §9). Wheels are visual only (the raycast model is the tire).
// The kart number is a separate layer, never baked into the body.
using UnityEngine;

namespace OKT
{
    public static class KartFactory
    {
        public static KartPhysics Create(string name, int number, Color bodyColor, KartSetup setup, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Rigidbody>();
            var bodyMat = TrackBuilder.BodyMaterial();
            var tubeMat = Materials.Solid(new Color(0.12f, 0.12f, 0.14f), 0.4f);

            // --- frame / seat / engine block, between the wheels
            float hw = setup.cockpitHalfWidthIn * U.In, front = setup.cockpitFrontIn * U.In, rear = setup.cockpitRearIn * U.In;
            float y0 = setup.frameClearance, y1 = setup.cockpitTopIn * U.In;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(2f * hw, y1 - y0, front + rear);
            box.center = new Vector3(0f, (y0 + y1) * 0.5f, (front - rear) * 0.5f);
            box.sharedMaterial = bodyMat;
            var cock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cock.GetComponent<Collider>());
            cock.name = "FrameVisual"; cock.layer = 2;
            cock.transform.SetParent(go.transform, false);
            cock.transform.localPosition = box.center; cock.transform.localScale = box.size;
            cock.GetComponent<MeshRenderer>().sharedMaterial = Materials.Solid(bodyColor, 0.4f);

            // --- tubes (collider + visual in one primitive each)
            float r = setup.TubeRadius, yb = setup.TubeBottomCentre;
            var tubes = new GameObject("Tubes").transform; tubes.SetParent(go.transform, false);
            // nerf bars: run between the front and rear wheels, just outside the tires
            float nz0 = -setup.wheelbase * 0.5f, nz1 = setup.wheelbase * 0.5f;
            foreach (bool right in new[] { true, false })
            {
                float x = setup.NerfX(right);
                Tube(tubes, (right ? "NerfR" : "NerfL") + "Low", new Vector3(x, yb, nz0), new Vector3(x, yb, nz1), r, tubeMat, bodyMat);
                Tube(tubes, (right ? "NerfR" : "NerfL") + "Top", new Vector3(x, setup.nerfTopIn * U.In, nz0 * 0.8f), new Vector3(x, setup.nerfTopIn * U.In, nz1 * 0.8f), r, tubeMat, bodyMat);
            }
            // front bumper
            float fz = setup.FrontBumperZ, fw = setup.frontBumperHalfWidthIn * U.In;
            Tube(tubes, "FrontBumperLow", new Vector3(-fw, yb, fz), new Vector3(fw, yb, fz), r, tubeMat, bodyMat);
            Tube(tubes, "FrontBumperTop", new Vector3(-fw * 0.8f, setup.frontBumperTopIn * U.In, fz - 0.05f), new Vector3(fw * 0.8f, setup.frontBumperTopIn * U.In, fz - 0.05f), r, tubeMat, bodyMat);
            // rear bumper
            float rz = setup.RearBumperZ, rw = setup.rearBumperHalfWidthIn * U.In;
            Tube(tubes, "RearBumperLow", new Vector3(-rw, yb, rz), new Vector3(rw, yb, rz), r, tubeMat, bodyMat);
            Tube(tubes, "RearBumperTop", new Vector3(-rw, setup.rearBumperTopIn * U.In, rz), new Vector3(rw, setup.rearBumperTopIn * U.In, rz), r, tubeMat, bodyMat);

            // --- tire bodies: solid blocks so karts shove off each other's tires instead of overlapping
            // (a ray-only tire could sit inside a rival's nerf bar and "stand" on it — climbs and flips).
            // They touch karts and rails, not the dirt: the wheel rays own the ground (World ignores
            // tire-vs-track collisions). Bottom 1 in off the ground at rest, top = top of tire.
            var tireBodyMat = new PhysicsMaterial("TireBody") { dynamicFriction = setup.tireContactFriction, staticFriction = setup.tireContactFriction,
                bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
            var tireBodies = new GameObject("TireBodies").transform; tireBodies.SetParent(go.transform, false);
            for (int i = 0; i < 4; i++)
            {
                var m = setup.WheelMount((Corner)i);
                float wr = setup.WheelRadius(i), tw = setup.TireWidth(i);
                float bottom = 1f * U.In, top = 2f * wr;
                var tb = new GameObject("TireBody_" + (Corner)i);
                tb.transform.SetParent(tireBodies, false);
                tb.transform.localPosition = new Vector3(m.x, (bottom + top) * 0.5f, m.z);
                var bc = tb.AddComponent<BoxCollider>();
                bc.size = new Vector3(tw, top - bottom, 2f * wr * 0.9f);
                bc.sharedMaterial = tireBodyMat;
            }

            var kp = go.AddComponent<KartPhysics>();
            kp.Init(setup);

            // --- wheels (visual only): pivot (steer) -> spin -> cylinder
            var tireMat = Materials.Solid(new Color(0.08f, 0.08f, 0.08f), 0.15f);
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject(((Corner)i).ToString()).transform;
                pivot.SetParent(go.transform, false);
                pivot.localPosition = setup.WheelMount((Corner)i);
                var spin = new GameObject("Spin").transform;
                spin.SetParent(pivot, false);
                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(cyl.GetComponent<Collider>());
                cyl.layer = 2;
                cyl.transform.SetParent(spin, false);
                float wr = setup.WheelRadius(i), w = setup.TireWidth(i);
                cyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                cyl.transform.localScale = new Vector3(2f * wr, w * 0.5f, 2f * wr);
                cyl.GetComponent<MeshRenderer>().sharedMaterial = tireMat;
                kp.wheelVisuals[i] = pivot;
            }

            // --- number: separate layer on top of the cockpit (spec §6 / CLAUDE.md)
            var num = new GameObject("Number");
            num.transform.SetParent(go.transform, false);
            num.layer = 2;
            num.transform.localPosition = new Vector3(0f, y1 + 0.01f, -rear * 0.5f);
            num.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tm = num.AddComponent<TextMesh>();
            tm.text = number.ToString();
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.06f;
            tm.fontSize = 64;
            tm.color = Color.white;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { tm.font = font; num.GetComponent<MeshRenderer>().sharedMaterial = font.material; }

            return kp;
        }

        /// <summary>A round tube from a to b (kart local): capsule primitive = collider and visual.</summary>
        static void Tube(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material mat, PhysicsMaterial pmat)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            c.name = name;
            c.transform.SetParent(parent, false);
            c.transform.localPosition = (a + b) * 0.5f;
            c.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            float len = (b - a).magnitude + 2f * radius; // capsule length includes the round ends
            c.transform.localScale = new Vector3(2f * radius, len * 0.5f, 2f * radius);
            c.GetComponent<MeshRenderer>().sharedMaterial = mat;
            c.GetComponent<CapsuleCollider>().sharedMaterial = pmat;
        }
    }
}
