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
            setup.FreezeFootprint();
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
            float fTop = setup.frontBumperTopIn * U.In - r; // the rule measures the TOP of the hoop
            Tube(tubes, "FrontBumperTop", new Vector3(-fw * 0.8f, fTop, fz - 0.05f), new Vector3(fw * 0.8f, fTop, fz - 0.05f), r, tubeMat, bodyMat);
            // rear bumper
            float rz = setup.RearBumperZ, rw = setup.rearBumperHalfWidthIn * U.In;
            // WKA 409.3: between the bottom of the rear axle and 7.5 in (top of the bumper)
            float rLow = setup.RearBumperLowCentre, rTop = setup.rearBumperTopIn * U.In - r;
            Tube(tubes, "RearBumperLow", new Vector3(-rw, rLow, rz), new Vector3(rw, rLow, rz), r, tubeMat, bodyMat);
            Tube(tubes, "RearBumperTop", new Vector3(-rw, rTop, rz), new Vector3(rw, rTop, rz), r, tubeMat, bodyMat);

            // --- body (Franklin 2026-11-16, WKA §404): fiberglass side panels from the nose back to the rear tires, outer face 1/4 in
            // outside the tires, 1/2 in off the ground, with a ROUND bottom edge (a square corner caught the inside lip
            // the way the old box chassis caught the berm); a full-width nose out ahead of the front tires with a round
            // leading edge. Slippery: panels slide along each other. Ends at the rear tires (the rear bumper stays bare).
            // Franklin's shop photos (11-16): the body is ONE shell that wraps OUTSIDE the front tires (they only show
            // through wheel-well cutouts), so the panels run forward past the front tires to the nose -- front tires
            // never touch another kart; side contact is shell on shell.
            if (setup.bodyOn)
            {
                var panelMat = new PhysicsMaterial("BodyPanel") { dynamicFriction = setup.bodyFriction, staticFriction = setup.bodyFriction,
                    bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
                var shell = Materials.Solid(bodyColor, 0.5f);
                var body = new GameObject("Body").transform; body.SetParent(go.transform, false);
                float th = setup.panelThickIn * U.In, pr = th * 0.5f;
                float pz0 = -setup.wheelbase * 0.5f + setup.WheelRadius(2), pz1 = setup.FrontTireLead; // to the nose
                float py0 = setup.panelBottomIn * U.In, py1 = setup.panelTopIn * U.In;
                foreach (bool right in new[] { true, false })
                {
                    float xo = setup.PanelOuter(right) * (right ? 1f : -1f), xc = xo - (right ? pr : -pr);
                    Slab(body, right ? "PanelR" : "PanelL", new Vector3(xc, (py0 + pr + py1) * 0.5f, (pz0 + pz1) * 0.5f),
                        new Vector3(th, py1 - (py0 + pr), pz1 - pz0), shell, panelMat);
                    Tube(body, (right ? "PanelR" : "PanelL") + "Edge", new Vector3(xc, py0 + pr, pz0), new Vector3(xc, py0 + pr, pz1), pr, shell, panelMat);
                }
                float noseTip = setup.NoseFrontZ;   // trailing edge of the front tires -> nose tip
                float nx = setup.PanelOuter(true), nxl = setup.PanelOuter(false);
                float ny0 = setup.noseBottomIn * U.In, ny1 = setup.noseTopIn * U.In, nr = 1.0f * U.In;
                // nose block ahead of the front tires (the side panels cover the front wheels; the nose starts where they end)
                float nzA = setup.FrontTireLead;
                // one convex, rounded shell: round front corners in plan, rolled lower/upper front edges (a square nose
                // corner caught on bumpers and panels; a round one glances off, like the real fiberglass)
                var nose = new GameObject("Nose"); nose.transform.SetParent(body, false);
                var nm = NoseMesh(-nxl, nx, nzA, noseTip, ny0, ny1, setup.noseCornerRadiusIn * U.In,
                                  setup.noseBottomRoundIn * U.In, setup.noseTopRoundIn * U.In);
                nose.AddComponent<MeshFilter>().sharedMesh = nm;
                nose.AddComponent<MeshRenderer>().sharedMaterial = shell;
                var nmc = nose.AddComponent<MeshCollider>(); nmc.sharedMesh = nm; nmc.convex = true; nmc.sharedMaterial = panelMat;
            }

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
                float wr = setup.WheelRadius(i), tw = setup.TireOuterWidth(i);
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

            // --- the driver: torso + helmet (shape only -- see KartSetup.driverBody). The kart's rotational inertia is
            // kept as it was without him, so handling doesn't change; he only adds what can touch the ground or a rival.
            if (setup.driverBody)
            {
                var rb = kp.Body;
                var drv = new GameObject("Driver").transform; drv.SetParent(go.transform, false);
                float dx = -setup.driverOffsetLeftIn * U.In, tr = setup.driverTorsoRadiusIn * U.In;
                var hip = new Vector3(dx, setup.driverHipYIn * U.In, setup.driverHipZIn * U.In);
                var sh = new Vector3(dx - setup.driverLeanLeftIn * U.In, setup.driverShoulderYIn * U.In, (setup.driverHipZIn - setup.driverLeanBackIn) * U.In);
                // torso: one capsule, hips to shoulders (Franklin 11-20: one body, not two side by side); the arms make
                // up the shoulders' width
                var suitMat = Materials.Solid(new Color(0.1f, 0.1f, 0.12f), 0.3f);
                float shw = setup.driverShoulderHalfWidthIn * U.In;
                {
                    var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule); torso.name = "Torso";
                    torso.transform.SetParent(drv, false);
                    float tlen = (sh - hip).magnitude + 2f * tr;
                    torso.transform.localPosition = (hip + sh) * 0.5f;
                    torso.transform.localRotation = Quaternion.FromToRotation(Vector3.up, sh - hip);
                    torso.transform.localScale = new Vector3(2f * tr, tlen * 0.5f, 2f * tr); // capsule primitive: 1 wide, 2 tall
                    torso.GetComponent<Collider>().sharedMaterial = bodyMat;
                    torso.GetComponent<MeshRenderer>().sharedMaterial = suitMat;
                }
                foreach (float side in new[] { -1f, 1f })
                {
                    // arm: shoulder -> elbow (out wide and low) -> hand on the wheel grip
                    float ar = setup.driverArmRadiusIn * U.In;
                    var shoulder = sh + new Vector3(side * (shw + tr * 0.6f), 0f, 0f);
                    var hand = new Vector3(-setup.driverKneeLineLeftIn * U.In + side * setup.driverHandHalfSpanIn * U.In, setup.driverHandYIn * U.In, setup.driverHandZIn * U.In);
                    var elbow = new Vector3(shoulder.x + side * setup.driverElbowOutIn * U.In, setup.driverElbowYIn * U.In, (shoulder.z + hand.z) * 0.5f);
                    foreach (var (a, b, nm) in new[] { (shoulder, elbow, "UpperArm"), (elbow, hand, "Forearm") })
                    {
                        var arm = GameObject.CreatePrimitive(PrimitiveType.Capsule); arm.name = nm + (side < 0f ? "L" : "R");
                        arm.transform.SetParent(drv, false);
                        arm.transform.localPosition = (a + b) * 0.5f;
                        arm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                        arm.transform.localScale = new Vector3(2f * ar, ((b - a).magnitude + 2f * ar) * 0.5f, 2f * ar);
                        arm.GetComponent<Collider>().sharedMaterial = bodyMat;
                        arm.GetComponent<MeshRenderer>().sharedMaterial = suitMat;
                    }
                }
                var helm = GameObject.CreatePrimitive(PrimitiveType.Sphere); helm.name = "Helmet";
                helm.transform.SetParent(drv, false);
                helm.transform.localPosition = new Vector3(sh.x, setup.helmetYIn * U.In, sh.z + setup.helmetAheadIn * U.In);
                helm.transform.localScale = Vector3.one * (2f * setup.helmetRadiusIn * U.In);
                helm.GetComponent<Collider>().sharedMaterial = bodyMat;
                helm.GetComponent<MeshRenderer>().sharedMaterial = Materials.Solid(bodyColor, 0.6f);
                // legs: thigh up to the knee (above the body top), shin down to the pedals
                var legMat = Materials.Solid(new Color(0.16f, 0.2f, 0.3f), 0.2f);
                float lr = setup.driverLegRadiusIn * U.In;
                foreach (float side in new[] { -1f, 1f })
                {
                    float lx = dx + side * setup.driverLegSpreadIn * U.In;                                   // at the seat
                    float kx = -setup.driverKneeLineLeftIn * U.In + side * setup.driverLegSpreadIn * U.In;    // either side of the column
                    var hipL = new Vector3(lx, setup.driverHipYIn * U.In, setup.driverHipZIn * U.In + tr * 0.5f);
                    var knee = new Vector3(kx, setup.driverKneeYIn * U.In, setup.driverKneeZIn * U.In);
                    var foot = new Vector3(kx, setup.driverFootYIn * U.In, setup.driverFootZIn * U.In);
                    foreach (var (a, b, nm) in new[] { (hipL, knee, "Thigh"), (knee, foot, "Shin") })
                    {
                        var leg = GameObject.CreatePrimitive(PrimitiveType.Capsule); leg.name = nm + (side < 0f ? "L" : "R");
                        leg.transform.SetParent(drv, false);
                        leg.transform.localPosition = (a + b) * 0.5f;
                        leg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                        leg.transform.localScale = new Vector3(2f * lr, ((b - a).magnitude + 2f * lr) * 0.5f, 2f * lr);
                        leg.GetComponent<Collider>().sharedMaterial = bodyMat;
                        leg.GetComponent<MeshRenderer>().sharedMaterial = legMat;
                    }
                }
                // He's built switched off: KartPhysics turns him on at its first step, once the engine has worked out the
                // kart's inertia from the kart's own parts, and pins that (see KartPhysics.Step). Reading the inertia here,
                // as the parts were still being added, got a half-built tensor -- yaw 21 instead of 53 kg m2, axes twisted
                // 38 deg -- and the karts spun and rolled far too easily (soak 2026-11-18: 3 flips in 8 races).
                kp.driverColliders = drv.GetComponentsInChildren<Collider>();
                foreach (var dc in kp.driverColliders) dc.enabled = false;
            }

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

            // The look: Franklin's Blender kart over these colliders (KartModel; off = the collider view).
            KartModel.Attach(go, kp, setup, bodyColor);

            // Every collider of the kart on the kart layer, so wheel rays (batched, nearest hit only) skip karts by mask --
            // the same rule GroundRay applied hit by hit (wheels stand on the track only).
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                if (col.gameObject.layer != 2) col.gameObject.layer = KartPhysics.KartLayer;
            return kp;
        }

        /// <summary>A box (kart local centre and size): cube primitive = collider and visual.</summary>
        static void Slab(Transform parent, string name, Vector3 centre, Vector3 size, Material mat, PhysicsMaterial pmat)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = name;
            c.transform.SetParent(parent, false);
            c.transform.localPosition = centre;
            c.transform.localScale = size;
            c.GetComponent<MeshRenderer>().sharedMaterial = mat;
            c.GetComponent<BoxCollider>().sharedMaterial = pmat;
        }

        /// <summary>
        /// The nose as a lofted convex solid (kart local). Plan outline: straight back edge at zBack, straight sides,
        /// round front corners of radius R, flat-ish front at zTip. Each height ring is the outline pulled in by the
        /// front-edge rounding (bottom rB, top rT), so the lower and upper front edges roll over.
        /// </summary>
        static Mesh NoseMesh(float xL, float xR, float zBack, float zTip, float y0, float y1, float R, float rB, float rT)
        {
            float Rx = Mathf.Min(R, (xR - xL) * 0.5f - 0.001f), Rz = Mathf.Min(R, zTip - zBack - 0.01f); // elliptical corners
            rB = Mathf.Max(rB, 0.0005f); rT = Mathf.Max(rT, 0.0005f); // a zero roll is a square edge (rings collapse to one)
            const int cornerSeg = 8, ringsB = 4, ringsT = 4;
            var heights = new System.Collections.Generic.List<float>(); var insets = new System.Collections.Generic.List<float>();
            for (int i = 0; i <= ringsB; i++)
            {   // bottom roll: quarter circle from (inset rB at y0) to (inset 0 at y0 + rB)
                float a = Mathf.PI * 0.5f * i / ringsB;
                heights.Add(y0 + rB - rB * Mathf.Cos(a)); insets.Add(rB - rB * Mathf.Sin(a));
            }
            for (int i = 0; i <= ringsT; i++)
            {
                float a = Mathf.PI * 0.5f * i / ringsT;
                float y = y1 - rT + rT * Mathf.Sin(a), ins = rT - rT * Mathf.Cos(a);
                if (y <= heights[heights.Count - 1] + 1e-4f) continue;
                heights.Add(y); insets.Add(ins);
            }
            var verts = new System.Collections.Generic.List<Vector3>(); int ringN = -1;
            foreach (int k in System.Linq.Enumerable.Range(0, heights.Count))
            {
                float ins = insets[k], y = heights[k];
                float l = xL + ins, r = xR - ins, tip = zTip - ins, rx = Mathf.Max(0.005f, Rx - ins), rz = Mathf.Max(0.005f, Rz - ins);
                var ring = new System.Collections.Generic.List<Vector3>();
                ring.Add(new Vector3(r, y, zBack));                                   // back right
                for (int j = 0; j <= cornerSeg; j++)                                  // front right corner
                {
                    float a = Mathf.PI * 0.5f * j / cornerSeg;
                    ring.Add(new Vector3(r - rx + rx * Mathf.Cos(a), y, tip - rz + rz * Mathf.Sin(a)));
                }
                for (int j = 0; j <= cornerSeg; j++)                                  // front left corner
                {
                    float a = Mathf.PI * 0.5f + Mathf.PI * 0.5f * j / cornerSeg;
                    ring.Add(new Vector3(l + rx + rx * Mathf.Cos(a), y, tip - rz + rz * Mathf.Sin(a)));
                }
                ring.Add(new Vector3(l, y, zBack));                                   // back left
                ringN = ring.Count; verts.AddRange(ring);
            }
            var tris = new System.Collections.Generic.List<int>(); int rings = heights.Count;
            for (int k = 0; k < rings - 1; k++)
                for (int j = 0; j < ringN; j++)
                {
                    int a0 = k * ringN + j, a1 = k * ringN + (j + 1) % ringN, b0 = a0 + ringN, b1 = a1 + ringN;
                    tris.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                }
            // caps (each ring is convex): fan from the first vertex
            for (int j = 1; j < ringN - 1; j++) tris.AddRange(new[] { 0, j + 1, j });
            int top = (rings - 1) * ringN;
            for (int j = 1; j < ringN - 1; j++) tris.AddRange(new[] { top, top + j, top + j + 1 });
            var m = new Mesh { name = "Nose" };
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
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
