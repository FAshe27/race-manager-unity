# race-manager-unity — OKT Raceway race-core spike

Unity 3D (PhysX) race core for the OKT Raceway dirt-kart manager game.
Spec: `UNITY-SPIKE.md`. Working channel: `COMMUNICATIONS.md`. Agent rules: `CLAUDE.md`.

- **Unity version:** 6000.6.9f1 (URP, 3D template)
- **Track data:** `Assets/Data/test-track-one-v3.json` (built by `Tools/build_track_v3.py` from Franklin's v2 `test-track-one.json` + his 2026-11-14 rulings; metres inside, imperial in everything you tune)

## Run M1
1. Open the project in Unity 6000.6.9f1.
2. `OKT > Open M1 Scene` (or open `Assets/Scenes/M1_KartLapsSurface.unity`), press **Play**.
3. HUD (top-left): Pause / 1× / 2× (extra physics steps, never `Time.timeScale`), Reset,
   groove overlay, poor-prep toggle, drive-over test, lap times and wheel telemetry.

## Headless check
`OKT > Run M1 Headless Test` in the editor, or batch:

```
Unity.exe -batchmode -nographics -projectPath <this folder> -executeMethod OKT.EditorTools.M1HeadlessTest.Run -okt-out m1-results.txt
```
Add `-okt-sweep` for the steering-controller sweep.

## Code map (`Assets/Scripts/OKT`)
- `Track/TrackModel.cs` — spline, frames, `SurfaceHeight(x, z)` (banking + groove + roughness + berm layers)
- `Track/TrackBuilder.cs` — surface mesh + MeshCollider, wall / tire-stack strip colliders, markers
- `Kart/KartSetup.cs` — every kart value with its source (Franklin / spec / [TEST VALUE])
- `Kart/KartPhysics.cs` — raycast suspension, grip, rear drive/brake (no WheelCollider)
- `Kart/KartAI.cs` — pure pursuit on the groove, corner-profile speed, laps
- `Core/World.cs` — physics settings, world build, `SimClock` (step-based game speed)
- `Core/RaceBootstrap.cs`, `Core/DebugHud.cs` — scene entry, whole-track camera, HUD
