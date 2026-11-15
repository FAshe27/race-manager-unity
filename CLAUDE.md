# OKT Raceway — Unity race-core spike

A dirt-kart racing sim core in Unity 3D physics (PhysX): a field of AI karts
racing a real track — banked, rough dirt surface — with physical walls, a
defended groove, physical spins, and real flips. This is the race engine of
a team-owner manager game (the management shell comes later — do not
build it).

## Read first, every session

1. `UNITY-SPIKE.md` — the spec. It governs scope, physics, AI, and the race
   rules. Build milestone by milestone (M1 → M4), in order.
2. `COMMUNICATIONS.md` — the channel with Muse (auditor). Check it before
   starting work; post a REPORT there when a milestone is ready; don't start
   the next milestone until the AUDIT and Franklin's playtest verdict are in.

## Hard rules

- **Franklin is the domain source.** He is a real Clone-kart racer. Never
  invent a kart/racing value — use the spec's numbers, stamp guesses
  **[TEST VALUE]**, and raise domain questions in COMMUNICATIONS.md as
  DECISION NEEDED.
- **No teleporting karts, ever.** Positions change only by driving —
  including the causer and the spinner going to the back under caution
  (spec §8.4).
- **Physics must be honest.** Air, flips, and berm launches fall out of the
  raycast-suspension model — never scripted exceptions. No stock
  `WheelCollider`. If a behaviour seems to need a scripted hack, raise it
  in COMMUNICATIONS.md instead of building it.
- **The wall has no give.** A kart crossing a wall under any circumstances
  is a stop-and-report bug (COMMUNICATIONS.md), not something to patch
  around quietly.
- **Whole-track camera only.** No following or zooming to a kart.
- **Game speed via physics substeps, never `Time.timeScale`.**
- **Kart numbers are a separate layer**, never baked into the sprite.
- Determinism is not required; don't chase it.

## Key data

- `Assets/Data/test-track-one.json` — Franklin's hand-set track in meters
  (centerline, half-widths, his racing line, corner table, go line).
  Colliders are built from this file, never from the backdrop art's pixels.
- Reference implementation of the rules (browser version, repo
  `FAshe27/race-manager`): `js/sim.js` — caution rule, incident outcomes,
  attitude, packed starts. Design docs there: `DESIGN.md`,
  `KARTING-REFERENCE.md`.
