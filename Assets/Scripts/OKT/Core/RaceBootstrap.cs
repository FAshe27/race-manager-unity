// Scene entry point for M1: builds the world from the track JSON, sets up the
// whole-track camera and light, drives the SimClock, and draws the debug HUD.
using UnityEngine;

namespace OKT
{
    public class RaceBootstrap : MonoBehaviour
    {
        public TextAsset trackJson;
        // NOT serialized: every kart/track value lives in code (KartSetup.cs etc.), never as a stale
        // copy saved in the scene. (Found 2026-11-14: the scene had frozen its creation-time values,
        // so Franklin's playtests ran old springs/settings.) Built fresh each Play.
        [System.NonSerialized] public SurfaceConfig surface;
        [System.NonSerialized] public KartSetup kartSetup;
        [System.NonSerialized] public DriverConfig driver;

        public const string CurrentTrackName = "test-track-one-v3";

        public World world;
        public SimClock clock = new SimClock();
        WholeTrackCamera cam;

        void Start()
        {
            surface = new SurfaceConfig(); kartSetup = new KartSetup(); driver = new DriverConfig();
#if UNITY_EDITOR
            // Always race the current track file, even if this scene still references an older one.
            if (trackJson == null || trackJson.name != CurrentTrackName)
            {
                var cur = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/" + CurrentTrackName + ".json");
                if (cur != null)
                {
                    Debug.LogWarning("[OKT] Scene referenced '" + (trackJson ? trackJson.name : "nothing") + "'; racing " + CurrentTrackName + " instead.");
                    trackJson = cur;
                }
            }
#endif
            if (trackJson == null) { Debug.LogError("[OKT] RaceBootstrap: no track JSON (expected Assets/Data/" + CurrentTrackName + ".json)"); enabled = false; return; }
            Debug.Log("[OKT] Racing track file: " + trackJson.name);
            world = World.Build(trackJson.text, surface, kartSetup, driver);
            cam = WholeTrackCamera.Ensure(world.track);
            EnsureLight();
            var hud = gameObject.AddComponent<DebugHud>();
            hud.boot = this;
        }

        void Update()
        {
            if (world == null) return;
            clock.Tick(world, Time.deltaTime);
            world.UpdateVisuals(Time.deltaTime * clock.speed);
        }

        public void ResetWorld()
        {
            int sp = clock.speed;
            world.Destroy();
            world = World.Build(trackJson.text, surface, kartSetup, driver);
            clock = new SimClock { speed = sp };
        }

        static void EnsureLight()
        {
            if (Object.FindAnyObjectByType<Light>() != null) return;
            var go = new GameObject("Sun");
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.2f;
            l.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        }
    }

    /// <summary>
    /// Whole-track camera (spec §9): fixed, elevated 3D view of the entire circuit.
    /// Never follows or zooms to a kart. Fits the outer wall into the viewport and
    /// refits when the window aspect changes.
    /// </summary>
    public class WholeTrackCamera : MonoBehaviour
    {
        public TrackModel track;
        public float pitchDeg = 52f;   // [TEST VALUE] presentation
        public float yawDeg = 0f;      // looking from the -Z side
        public float margin = 0.04f;
        float lastAspect;
        Camera cam;

        public static WholeTrackCamera Ensure(TrackModel t)
        {
            var c = Camera.main;
            if (c == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                c = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            c.fieldOfView = 40f;
            c.nearClipPlane = 0.3f; c.farClipPlane = 600f;
            var w = c.GetComponent<WholeTrackCamera>();
            if (w == null) w = c.gameObject.AddComponent<WholeTrackCamera>();
            w.track = t; w.cam = c;
            w.Fit();
            return w;
        }

        void LateUpdate()
        {
            if (cam != null && Mathf.Abs(cam.aspect - lastAspect) > 1e-3f) Fit();
        }

        public void Fit()
        {
            if (cam == null) cam = GetComponent<Camera>();
            lastAspect = cam.aspect;
            var rot = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            var fwd = rot * Vector3.forward;
            // Points that must be in view: the outer wall tops.
            var pts = new Vector3[128];
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < pts.Length; i++)
            {
                float s = (float)i / pts.Length * track.N; var f = track.Frame(s);
                var p = f.pos + f.outN * (f.halfWidth + track.cfg.barrierThickness);
                pts[i] = new Vector3(p.x, track.SurfaceHeightSD(s, f.halfWidth) + track.cfg.fenceHeight, p.y);
                centre += pts[i];
            }
            centre /= pts.Length;
            float dist = 20f;
            for (int it = 0; it < 200; it++)
            {
                transform.SetPositionAndRotation(centre - fwd * dist, rot);
                bool ok = true;
                foreach (var p in pts)
                {
                    var v = cam.WorldToViewportPoint(p);
                    if (v.z <= 0 || v.x < margin || v.x > 1 - margin || v.y < margin || v.y > 1 - margin) { ok = false; break; }
                }
                if (ok) break;
                dist *= 1.03f;
            }
        }
    }
}
