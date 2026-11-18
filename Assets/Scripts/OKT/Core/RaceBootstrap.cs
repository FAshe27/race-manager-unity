// Scene entry point: builds the M2 race (12 karts on the grid, race director) from the
// track JSON, sets up the whole-track camera and light, drives the SimClock (game speed =
// extra physics steps, never Time.timeScale), runs SOAK batches, and draws the debug HUD.
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

        public const string CurrentTrackName = "test-track-one-v4";

        public World world;
        public SimClock clock = new SimClock();
        WholeTrackCamera cam;
        public int seed;
        public RaceConfig raceCfg = new RaceConfig();

        // SOAK (Muse's M2 request): run N races unattended at high step rate, write soak-results.txt.
        [System.NonSerialized] public bool soakRunning;
        [System.NonSerialized] public int soakTotal, soakDone;
        [System.NonSerialized] public string soakStatus = "";
        System.Text.StringBuilder soakSb; SoakStats soakStats;
        const int SoakStepsPerFrame = 3000;

        void Start()
        {
            surface = new SurfaceConfig(); kartSetup = new KartSetup(); driver = new DriverConfig();
            // Race Cfg shows in the Inspector for live tuning during Play, but every Play starts from the code's values:
            // anything the scene saved is thrown away here (same stale-copy trap as the kart values, 2026-11-14).
            raceCfg = new RaceConfig();
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
            seed = Random.Range(1, 1000000);
            world = World.BuildRace(trackJson.text, seed, raceCfg, surface);
            cam = WholeTrackCamera.Ensure(world.track);
            EnsureLight();
            var hud = gameObject.AddComponent<DebugHud>();
            hud.boot = this;
        }

        void Update()
        {
            if (world == null) return;
            if (soakRunning) { SoakTick(); return; }
            clock.Tick(world, Time.deltaTime);
            world.UpdateVisuals(Time.deltaTime * Mathf.Max(1, clock.speed));
        }

        /// <summary>Rebuild the grid. newField = a fresh field (new seed); otherwise the same drivers again.</summary>
        public void ResetWorld(bool newField = false)
        {
            int sp = clock.speed;
            world.Destroy();
            if (newField) seed = Random.Range(1, 1000000);
            surface = new SurfaceConfig();
            world = World.BuildRace(trackJson.text, seed, raceCfg, surface);
            clock = new SimClock { speed = sp };
        }

        public void StartRace() { if (world?.director != null && world.director.phase == RacePhase.Grid) world.director.Start(); }

        public void StartSoak(int n)
        {
            soakRunning = true; soakTotal = n; soakDone = 0;
            soakSb = new System.Text.StringBuilder(Soak.Header(n, raceCfg)); soakStats = new SoakStats();
            BeginSoakRace();
        }

        void BeginSoakRace()
        {
            world.Destroy();
            seed = 1001 + soakDone;
            world = World.BuildRace(trackJson.text, seed, raceCfg, new SurfaceConfig());
            world.director.Start();
        }

        void SoakTick()
        {
            var d = world.director;
            int cap = Mathf.RoundToInt(Soak.MaxRaceSeconds / World.FixedDt);
            for (int i = 0; i < SoakStepsPerFrame && d.phase != RacePhase.Done && world.steps < cap; i++) world.StepOnce();
            world.UpdateVisuals(0f);
            soakStatus = $"SOAK race {soakDone + 1}/{soakTotal} — {d.phase}, sim {d.raceTime:F0} s";
            if (d.phase == RacePhase.Done || world.steps >= cap)
            {
                Soak.Record(world, seed, soakSb, soakStats);
                soakDone++;
                if (soakDone >= soakTotal)
                {
                    soakSb.AppendLine().AppendLine(Soak.Summary(soakStats));
                    string path = System.IO.Path.Combine(Application.dataPath, "..", "soak-results.txt");
                    System.IO.File.WriteAllText(path, soakSb.ToString());
                    soakRunning = false;
                    soakStatus = "SOAK done — wrote soak-results.txt. " + Soak.Summary(soakStats);
                    Debug.Log("[OKT] " + soakStatus);
                    ResetWorld(true);
                }
                else BeginSoakRace();
            }
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
