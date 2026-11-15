# OKT Raceway — Unity Race-Core Spike

**Status:** Spec v2 (2026-11-14), revised by Muse at Franklin's ruling —
**3D physics from the start** (see §14 for what changed and why). Supersedes
v1's 2D plan; if you read v1, re-read §§1, 3–6, 9, 11–14.
**Builder:** Claude Code, in a Unity project on Franklin's PC.
**Auditor:** Muse — each milestone's code gets read against this spec before Franklin playtests.
**Arbiter:** Franklin. His eyes and his kart knowledge outrank this document; where the spec and Franklin disagree, Franklin wins and the spec gets updated.

## 1. What this spike is

The browser version of this game (repo `FAshe27/race-manager`, live at
fashe27.github.io/race-manager) simulates a kart as two numbers — distance
around the lap and a lateral offset. That model races well, but every physical
edge case (walls, overlap, spins) has to be patched by hand, and Franklin keeps
catching the patches. This spike rebuilds **only the race core** in Unity with
real 3D physics (PhysX), so that walls, contact, spins, flips, and the track
surface itself are structural instead of simulated.

**The vertical dimension is real racing, not decoration** (Franklin,
2026-11-14, with photo evidence from his series): karts get airborne, karts
flip — usually by climbing another kart's tire or digging a wheel into a rut —
and dirt surfaces break up, forming rises, dips, and bumps that change how a
kart behaves crossing them. A flat-plane sim cannot produce any of that
honestly, which is why this spike is 3D from the start.

**The one question the spike answers:** does a physics-based race *feel* like
dirt kart racing — the line, the walls, the packs, the spins, the surface?
If yes, the management game (garage, economy, seasons) gets built around this
core later. If no, the browser game remains the game and nothing is lost.

**In scope:** one track (Test Track One, Franklin's own hand-set data, as a
banked 3D mesh with a modelled dirt surface), a 12-kart field of AI drivers
(the player is an owner — he never drives, in any version of this game), the
full race flow (pace lap → green → cautions → restarts → checkered), physical
contact, spins, flips, and the full recovery sequence in §8, a whole-track
camera, and a minimal debug HUD.

**Out of scope (do not build):** garage, economy, menus beyond a debug
launcher, saving, multiple tracks, player creation. Management comes later,
after the core is judged.

## 2. Working agreement

- Claude Code implements, milestone by milestone (§11). Don't run ahead of
  the current milestone.
- Muse audits each milestone's code against this spec; Franklin playtests
  and rules on feel. Disagreements about *racing* go to Franklin, always.
- Franklin is the domain source for kart values. Any number in this spec that
  isn't measured from his data is stamped **[TEST VALUE]** — tune it, don't
  treat it as truth, and flag it when it matters.
- Determinism is **not** a requirement. Unity physics varies run to run; the
  browser sim's replay-exactness was a testing luxury and is deliberately
  given up. Judging is by feel and by the statistical bands in §12 over
  multiple races.

## 3. Project setup

- New Unity project, **3D** template, current LTS. Record the exact Unity
  version in the project README.
- New private repo `race-manager-unity` with the standard Unity `.gitignore`
  (Library/, Temp/, Builds/ never committed).
- Physics (PhysX) settings: gravity (0, −9.81, 0); Fixed Timestep 1/60;
  default solver iterations raised (velocity 8 / position 4 is a sane start
  **[TEST VALUE]**); kart rigidbodies use **Continuous Dynamic** collision
  detection, and their `maxAngularVelocity` is raised (≈ 25 rad/s
  **[TEST VALUE]**) — the PhysX default clamps exactly the flip rotations
  this game needs.
- Copy `unity-handoff/test-track-one.json` from the browser repo into
  `Assets/Data/`. All track geometry derives from that file — never
  hand-place track pieces.
- Copy `UNITY-SPIKE.md`, `COMMUNICATIONS.md`, and `CLAUDE.md` from the
  browser repo into the new repo root. From then on, the Unity repo's copies
  are canonical. `COMMUNICATIONS.md` is the working channel between Claude
  Code and Muse — read its protocol before the first line of code.

## 4. Track data and the surface model

### 4.1 The file (`test-track-one.json`)
Franklin's hand-set Track Editor package, converted from image pixels to
meters at the track's racing calibration (1/8-mile lap, 201.17 m — the
browser game's Test Track One races on Atoka Raceway Park's numbers).

- `samples`: 256 points per loop, in order of travel. Sample 0 = start/finish.
- **Coordinates: world X = json x, world Z = json y, Y is up.**
- `centerlineM`: [x, y] points (→ world X/Z), meters, origin at track centroid.
- `halfWidthM`: centerline-to-edge distance per sample (3.0–4.4 m — the
  surface runs 6.0–8.8 m wide).
- `racingLineM`: the groove, as points — **Franklin set this line himself.**
  It is the optimum. AI karts aim for it (§7); it runs high on the straights
  (~0.79 across) and clips the apexes (~0.20 across).
- `lineFrac`: the same line as a 0..1 fraction across the surface
  (0 = inside edge, 1 = outside edge).
- `cornerTable`: 0..1 per sample (0 = straight, 1 = full corner) — drives the
  speed profile and the surface roughness (§4.2).
- `goU`: 0.8301 — the restart ("go") line position in lap fraction.
- `bankingDeg`: 8 — the surface's cross-slope, high side out (Atoka's).

### 4.2 The surface is a heightfield, built in layers
The racing surface's height at any point = the sum of three layers. This
layering is the data model for dirt: keep the layers separate in code,
because the third one *changes*.

1. **Banking.** The cross-slope: height rises linearly across the surface by
   `bankingDeg`. (On the straights of a real oval the banking mostly holds;
   treat it as constant around the lap for the spike.)
2. **The groove dig.** Where the field runs, the surface is worn slightly
   *down* and smooth: a shallow channel (≈ 3 cm deep **[TEST VALUE]**)
   centred on `lineFrac`, following it around the lap.
3. **Roughness.** Dirt breaks up: rises, dips, and bumps, concentrated where
   the surface works hardest — corner entries and exits, and off the groove
   (the loose stuff up high). Procedural, seeded, small amplitude
   (≈ ±2–4 cm **[TEST VALUE]**, scaled by `cornerTable` and by distance
   from the groove). A kart crossing roughness at speed should feel it:
   suspension working, grip varying, and a big enough rise at speed puts
   air under the kart.
4. **(Runtime, M4) Wear.** As laps accumulate, the groove digs deeper and
   roughness migrates — the track evolves through the night the way
   Franklin's grip curves do. Runtime state layered on top; the file on
   disk stays the fresh-track source of truth.

Heights are sampled from these layers by a single `SurfaceHeight(x, z)`
function (analytic from the spline + layers) — the mesh is built from it,
the AI reads ride height from it, and the soak checker uses it. One source
of truth, never three copies.

Track backdrop: for the spike, a simple dirt material on the mesh is fine;
Franklin's painted art can drape the mesh later. **Colliders and heights
come from the data, never from art pixels.**

## 5. Building the track

- Centerline: closed Catmull-Rom spline through `centerlineM` (world XZ).
- Cross-sections: at each sample, a row of vertices across the surface
  (say 9 across **[TEST VALUE]**), positioned by the edge offsets and
  heighted by `SurfaceHeight`. Stitch into a ribbon mesh → `MeshCollider`
  (static, non-convex). Extend the mesh a skirt beyond both edges (infield
  apron and outer run-off at ground level) so a kart that leaves the band
  is still on ground, not falling through the world.
- **Walls:** the outer boundary is a physical barrier following the outer
  edge loop — an extruded low wall (tire-wall height ≈ 0.5 m
  **[TEST VALUE]**) with a box-strip collider per segment. At its base, the
  heightfield carries a **berm**: a gentle dirt ramp piled against the wall
  (Franklin's photos show karts riding up exactly this). A kart can climb
  the berm, scrub speed, and come back down; the wall proper stops it.
  The inner boundary is the infield edge — model it as a low tire-stack
  strip collider the same way, so "off the inside" is also physical.
- "The wall has no give" (Franklin): a kart may scrape, ride the berm, and
  lose speed; it must never pass through the barrier, and pack pressure
  must never pop one through. If the solver ever does it, that's a
  spike-ending bug — report it in COMMUNICATIONS.md, don't paper over it.

## 6. The kart (3D physics model)

One prefab: a `Rigidbody` (mass per class weight, §6.1), a compound
collider (a low box for the chassis/body ≈ 1.80 m long × 1.35 m wide ×
0.35 m tall, plus the wheels in the suspension model below), and a
placeholder wedge mesh — the procedural 3D kart from Franklin's pipeline
is the eventual body; a clean box-wedge is fine for the spike. Numbers are
a separate layer, never baked into the mesh or texture.

### 6.1 Mass and centre of mass
Total kart+driver by class: Light 147 kg / Medium 159 / Heavy 170 /
Super Heavy 181. The spike fields Clone Medium: **159 kg**.
Centre of mass: low (≈ 0.18 m **[TEST VALUE]**) and a touch rearward —
karts are tail-heavy (driver + engine behind the seat). Get this wrong and
you get toy flips: a kart must **not** roll from cornering grip alone at
racing speed. Flips in this game come from *contact and surface* — a wheel
climbing another kart's tire, a wheel digging into a rut or the berm at an
angle — the way they do in Franklin's photos.

### 6.2 Vehicle model — raycast suspension (do NOT use WheelCollider)
Unity's stock `WheelCollider` is built for street cars and fights every
kart behaviour we need. Use the proven arcade pattern instead:
- Four **wheel raycasts** from the chassis corners (wheelbase ≈ 1.05 m,
  track ≈ 1.1 m rear / 1.0 m front **[TEST VALUE]** — real kart geometry,
  Franklin to confirm). Each grounded wheel applies a **suspension spring
  force** (stiffness/damping **[TEST VALUE]**) along its ray.
- **Drive:** the rear wheels apply longitudinal force from the engine,
  toward the AI's target speed (§7); accel cap ≈ 4.5 m/s², braking ≈ 8
  **[TEST VALUE]**.
- **Grip:** each grounded wheel applies a lateral force opposing its
  sideways slide, capped at the tire's grip limit; beyond the cap the
  wheel slides (that's the dirt). Front wheels' grip acts at the steered
  angle (steering ≈ ±28° **[TEST VALUE]**). Loose/tight attitude (§6.3)
  biases the rear cap down / the front wash up.
- **Airborne is free:** when the rays find no ground, no forces apply —
  the kart is ballistic, gravity and angular momentum rule, and landing is
  just the rays finding ground again. No special-casing: air, flips, and
  berm launches must *fall out* of this model. If a behaviour needs a
  scripted exception, that's a smell — raise it in COMMUNICATIONS.md.
- A kart is **flipped** when its up vector points down (dot(up, worldUp)
  < 0) and it has come to rest, or the moment it's clearly inverted in the
  air with no recovery possible. A flipped kart is out (§8.3).

### 6.3 Attitude (loose / tight) — carries over from the browser game
Per-kart attitude −1 (loose) .. +1 (tight), from setup:
- **Loose:** lower rear grip cap (tail steps out), more yaw under power.
- **Tight:** front grip scrubs speed in corners; the kart washes up the
  track (its effective line sits higher in the corners).
Feel seasoning — a neutral field is fine for M1–M2; wire it in by M4.

### 6.4 Speed calibration
Clone Medium at Test Track One: nominal lap **10.68 s** (browser
calibration; Franklin's real Clone band at Atoka is 10.5–11.5 s). Average
≈ 18.8 m/s. Starting target-speed curve: `vTarget = vMax × (1 − 0.30 ×
corner)`, vMax ≈ 21.5 m/s **[TEST VALUE]** — tune in M4 until clean-air
laps land in the band. Note the 3D surface changes cornering: banking
helps, roughness hurts; expect to re-derive the curve, not copy it.

## 7. AI drivers

Every kart is AI-driven, including the player's (agency in this game lives
in the pits, not on the track). The decision layer is unchanged from the
browser game's lessons; only its outputs (steering angle, throttle/brake)
now feed a physical kart.

- **Line following:** pure-pursuit — steer at a point on the racing-line
  spline ahead of the kart, lookahead ∝ speed (≈ 0.6 s of travel
  **[TEST VALUE]**). Target speed from the corner profile (§6.4) read at
  the lookahead point. The line spline rides the surface (heights from
  `SurfaceHeight`).
- **Personal variation:** per-kart lateral offset from the groove
  (−1.0..+1.0 m **[TEST VALUE]**) and a pace factor (field spread: 20%
  fast / 70% mid / 10% new — Franklin's numbers).
- **Occupancy — the rule Franklin cares most about:** the groove is the
  optimum and it is *defended*. A kart may not steer into a lateral slot
  another kart occupies: check the slot (footprint projected ~0.5 s ahead)
  — free → proceed; occupied → **check up** (match speed, hold slot) or
  take the other side only with a full kart width of clearance. Passing is
  a committed, latched move — no weaving. In 3D this matters double: a
  careless move isn't a nudge, it's a wheel-climb and a flip (§8.3).
- **Avoidance:** short feelers ahead-left/center/right for karts and walls;
  never plan a path through either. Also read the kart ahead's *state* —
  a kart that's sideways, airborne, or flipping ahead of you is a hazard
  to route around, not a slot to fill.
- **Aggression** (per-kart stat): biases gap acceptance and how hard it
  leans in a side-by-side. It never overrides occupancy.

## 8. Race director

Mirror the browser game's structure (it's Franklin's racing, codified):

- **Phases:** `pace → green → (caution → togo → green)* → checkered → done`.
- **Feature length:** 15 laps. Caution laps don't count.
- **Positions:** by spline progress (lap fraction u + laps completed),
  not raw distance — karts a lap down sort correctly.

### 8.1 The start
Double-file grid on the back straight, rows a nose gap apart — the field
rolls off basically pushing each other. Pace lap at pace speed (≈ 0.62 ×
racing speed **[TEST VALUE]**). **Nobody accelerates to racing speed
before the green** — the leader holds the field at pace speed to the flag
(Franklin, 2026-11-14). Green flies when the leader crosses start/finish.

### 8.2 Restarts
Single-file queue behind the leader at pace speed. One-to-go, then green
at start/finish under the same no-early-acceleration rule. A restart
happens **only when the queue is actually formed** (§8.4). The go line
(`goU`) is the jumped-start reference.

### 8.3 Incidents — spins, flips, and damage
Contact is physics; consequences are judged from the contact and the
kart's state:
- **Bent but rolling** (light/medium contact): damage factor, kart loses
  pace, no yellow.
- **Spin** (rear-quarter strike, or a loose kart over the limit): the kart
  rotates and slides — in 3D this is the rigidbody's own business; the
  director just notes it. Engine cut while it's rotating out of control.
- **Flip** (wheel-climb, rut-dig trip, berm launch gone wrong): the kart
  leaves the ground and rolls. **A flipped kart is always a caution and
  always out** — it does not recover and drive again. (Franklin's photos:
  a flip ends that kart's race, full stop.)
- **Causer:** the kart behind in a rear-end contact; in side contact, the
  kart moving into the other's slot. The director logs its reasoning per
  incident (Franklin will audit these).
- **THE CAUTION RULE (Franklin):** a spin stays green **only** if it's a
  self-spin, at the back of the field, *and* the spinner gets going again
  before traffic arrives. Anything spun in traffic is a caution. A flip is
  a caution regardless of where it happens.
- **Recovery:** once a spun (not flipped) kart settles, its AI drives it
  out — steer back to racing direction, rejoin at reduced speed. A kart
  that can't continue pulls to the inside edge and stops (out).

### 8.4 The caution sequence (Franklin's spec — the heart of the spike)
When a caution is thrown:
1. The field slows to pace speed and forms the single-file queue behind
   the leader, in current running order.
2. The spun kart recovers (§8.3) and **drives** to the back of the queue.
   The causer is assigned the tail as well and **drives** back there.
   Nobody's position ever changes except by driving there.
3. **Wrecked and flipped karts are cleared by the crew:** during the
   caution they are moved off the racing surface to the infield at walking
   pace (they're being carried/pushed by officials — animate the move,
   don't vanish them). The queue forms without them.
4. The director verifies the queue: correct order, gaps tight (≈ a kart
   length), everyone rolling. Only then: one-to-go, then green (§8.2).

### 8.5 Mechanical DNFs
Rare, probability per lap per kart (spike: flat low rate **[TEST VALUE]**).
Kart slows, pulls to the inside edge, stops. Franklin's calibration:
karts are durable; wrecks rarely end a night — mechanicals are the usual
cause. (Flips are the exception that proves it: rare, but final.)

## 9. Camera & presentation

- **Whole-track camera. Always.** The default judging view is an elevated
  3D view of the entire circuit (no following, no zooming to a kart —
  Franklin's explicit ruling). Fence-style trackside cameras are a later
  presentation polish, not spike scope — but nothing in the spike may
  assume a top-down camera, because the fence view is coming.
- Kart mesh/collider agreement matters as much as ever: if the visual body
  is bigger than the collider, walls will look wrong with correct physics
  underneath (the browser game's most persistent illusion). Match them.
- Dust, dirt spray, and surface shading that shows the groove and the
  roughness: later polish. The surface must at least *read* — a subtle
  darker groove strip in the material is worth it from M2 on, because
  Franklin judges the line by eye.

## 10. Debug HUD & controls (spike-only, keep it crude)

- Phase banner, lap counter, running order, caution count.
- Buttons: **Start race**, **Spin a kart** (debug: spin a random mid-pack
  kart — exercises §8.4 on demand), **Flip a kart** (debug: launch a
  mid-pack kart into a flip — exercises the flip path and crew clearing),
  **Reset**.
- Speed: pause / 1× / 2×. Implement speed by running extra physics steps
  per frame — **not** `Time.timeScale` (it changes effective timestep
  behaviour and can destabilise contacts at exactly the moments we care
  about).

## 11. Milestones

- **M1 — A kart laps a real surface.** Project + track mesh from the JSON
  (banking, groove dig, roughness, berm + walls), one AI kart on the
  raycast vehicle model lapping the groove at Clone pace, whole-track
  camera. Includes a drive-over test: the kart crosses the roughest patch
  at speed and we all watch what the suspension and the air do. Franklin
  judges: does its motion look like a dirt kart — the slide, the line, the
  corner speed, the way it takes a bump?
- **M2 — A race.** 12 karts, double-file start, director phases through
  the checkered, positions/laps/HUD, packed start. Include the SOAK
  auto-run logger requested in COMMUNICATIONS.md — every REPORT from here
  on carries 10-race soak numbers. (Incidents can be stubs.)
- **M3 — Contact, spins, flips, and the caution sequence.** Impulse-judged
  incidents, physical spins, wheel-climb flips, crew clearing, causer-to-
  the-tail by driving, verified-queue restarts. The §10 debug buttons earn
  their keep here.
- **M4 — Tuning and the living surface.** Lap times in band, caution rate
  in the 0–2 band over 10+ races, attitude wired in, wall-integrity soak
  (§12), and the first cut of surface wear (§4.2 layer 4): the groove
  deepens and roughness migrates as laps accumulate.

## 12. Acceptance checks (Franklin's feel tests)

1. **Wall integrity:** in 10 full races, no kart ever passes through the
   outer barrier — not in packs, not in spins, not mid-flip. Riding the
   berm and coming back is fine; through is never. Zero exceptions.
2. **The groove is defended:** a faster kart behind a kart on the line
   cannot pass through it — it goes around with clearance or checks up.
3. **Packed start:** the field reaches turn one nose-to-tail (browser
   benchmark: consecutive gaps ≲ 0.02 laps at the green).
4. **The spin sequence:** force a spin mid-pack → yellow; the spinner
   recovers and drives to the tail; the causer joins it there; the
   restart waits for the queue. A self-spin at the back that recovers
   quickly stays green.
5. **The flip sequence:** force a flip → yellow immediately; the kart is
   out; the crew clears it during the caution; the race restarts clean.
   In soak, flips are **rare** (order of one per several races, always
   traceable to a major contact or a dug wheel — never from clean
   cornering).
6. **The surface is honest:** crossing the rough patch at speed visibly
   works the suspension and can put air under the kart on the biggest
   rise; the groove is visibly the fast way around; no kart ever sinks
   into or clips through the mesh.
7. **Race shape:** races finish; cautions average inside 0–2 per 15-lap
   feature over 10 races; clean-air Clone laps land in 10.5–11.5 s.
8. **Four-wide sanity:** four karts fit across the narrowest surface
   (they will never race that way — if they do, something's wrong).

## 13. Known traps

- `Time.timeScale` for game speed (see §10).
- Stock `WheelCollider` (see §6.2) — street-car assumptions, endless
  fighting. Raycast suspension only.
- Centre of mass too high → karts that flip from clean cornering (toy
  physics; §12 check 5 catches it, Franklin's eyes catch it faster).
- PhysX `maxAngularVelocity` default (7 rad/s) silently clamping flips —
  raised in §3; verify it took.
- Mesh/collider mismatch (§9) — recreates "kart over the edge" with
  correct physics underneath.
- Setting a racing kart's rotation directly — it fights the solver and
  looks wrong. Forces and torques only (crew-clearing moves in §8.4 are
  the one sanctioned exception, at walking pace, under caution).
- Solver pop-through under pack pressure (§5) — raise iterations / shrink
  the fixed timestep before reaching for hacks.
- AI steering at the kart's *current* position instead of the lookahead —
  guarantees corner-cutting and wall kisses at the apexes.

## 14. Why 3D from the start (the v1 → v2 change)

Spec v1 planned a 2D-physics spike with 3D presentation layered later, on
the reasoning that "the racing is planar." Franklin corrected that with
facts and photographs from his own series (2026-11-14): karts climb each
other's tires and flip, karts ride up the dirt berm piled at the wall,
surfaces break up into rises and dips that launch karts at speed. The
vertical dimension is part of the racing this game exists to simulate, and
faking it later would mean rebuilding the vehicle layer — the exact
throwaway work the Unity move was meant to avoid. So: 3D physics (PhysX),
a heightfield surface, and a 3D presentation from the start. What carries
over unchanged from v1: the track data, the race director, the AI decision
layer, and every rule Franklin has set — those were always engine-agnostic,
which is why this revision is a re-cut of the vehicle and track sections,
not a rethink of the game.
