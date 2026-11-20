// The kart's LOOK: Franklin's Blender kart (Kart Parts/exports/OKT_Kart.fbx -> Resources/KartModel/OKT_Kart) laid over
// the physics kart as a skin. Spec §9 says what you see is the collider; that stays true underneath: every collider is
// built exactly as before, only the primitives that draw the body, nose, front bumper, frame box and wheels are hidden
// under the model. Show = false (or no model in Resources) gives the old collider view back. The model has no
// colliders and no say in the physics. Its wheels are hung on the physics wheel pivots, so they steer, spin and ride
// where the tire rays are.
using UnityEngine;

namespace OKT
{
    public static class KartModel
    {
        public static bool Show = true;
        const string ResourcePath = "KartModel/OKT_Kart";
        static readonly string[] WheelNames = { "Wheel_LF", "Wheel_RF", "Wheel_LR", "Wheel_RR" }; // Corner order
        // physics primitives the model replaces (their renderers only; colliders stay)
        static readonly string[] Covered = { "FrameVisual", "Body", "FrontBumperLow", "FrontBumperTop" };

        static GameObject prefab; static bool loaded;

        public static bool Available
        {
            get { if (!loaded) { loaded = true; prefab = Resources.Load<GameObject>(ResourcePath); } return prefab != null; }
        }

        public static void Attach(GameObject kart, KartPhysics kp, KartSetup setup, Color bodyColor)
        {
            if (!Show || !Available) return;
            var root = kart.transform;
            var model = Object.Instantiate(prefab, root, false);
            model.name = "Model";
            // model origin: front axle centre on the ground, nose +Z -> the physics kart's front axle
            // (the importer's own root rotation is the FBX axis conversion: keep it)
            model.transform.localPosition = prefab.transform.localPosition + new Vector3(0f, 0f, setup.wheelbase * 0.5f);
            model.transform.localRotation = prefab.transform.localRotation;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Kill(c);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;

            // body paint in the kart's colour
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.StartsWith("Body_Paint"))
                    {
                        var m = new Material(mats[i]) { name = "Body_Paint #" + kart.name };
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", bodyColor);
                        if (m.HasProperty("_Color")) m.SetColor("_Color", bodyColor);
                        mats[i] = m; changed = true;
                    }
                if (changed) r.sharedMaterials = mats;
            }

            // wheels onto the physics pivots: keep each wheel's orientation relative to the kart, centred on the pivot
            for (int i = 0; i < 4; i++)
            {
                var pivot = kp.wheelVisuals[i]; if (pivot == null || pivot.childCount == 0) continue;
                var w = Find(model.transform, WheelNames[i]); if (w == null) continue;
                var spin = pivot.GetChild(0);
                foreach (var r in spin.GetComponentsInChildren<Renderer>(true)) r.enabled = false; // the cylinder
                var rel = Quaternion.Inverse(model.transform.rotation) * w.rotation;
                w.SetParent(spin, false);
                w.localPosition = Vector3.zero; w.localRotation = rel;
            }

            // hide the primitives the model covers
            foreach (var name in Covered)
            {
                var t = Find(root, name); if (t == null || t.IsChildOf(model.transform)) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }

        static void Kill(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
    }
}
