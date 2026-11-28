# Track Designer — plan (draft 2026-11-28)

Franklin, 11-27: "After we get physics down, I'd like to build a software that I can use to design and build a track."
This is a plan only. Nothing is built yet, and nothing here changes the sim. Questions are q48–q50 on the Pit Board.

## What it is

The sim already builds the whole track from one file (`Assets/Data/test-track-one-v5.json`):
- 256 samples around the lap: centreline, width, groove, corner table, banking;
- walls, guardrail and fence by lap fraction, plus the gaps in them (access road, track exit, grid gate);
- features: grid, roads, gates, fences, scale house.

Today that file comes out of a Python script (`Tools/build_track_v5.py`) that only I can drive.
The designer replaces the script with a screen you drive: draw the track, see the numbers, save, race it.

## How it works for you

1. **Shape.** Start from a template (oval, D-oval, tri-oval, flat-sided "paperclip") and drag the handles:
   straight lengths, corner radii, track width in each section, banking. Or trace over a satellite photo or a
   MyChron GPS line, as v5 was built.
2. **Walls.** Click along the outside edge to set guardrail / fence / wall / nothing, and where the gaps are.
   Turn-4-style gaps (rail ending short of a fence) are flagged automatically.
3. **Infield and pits.** Place the grid, roads, gates and buildings.
4. **Check panel**, always visible, in feet and mph:
   - lap length (centreline and groove);
   - tightest radius, and where it is;
   - estimated corner speeds and perfect lap at the sim's grip (1.20 g);
   - widths, banking;
   - warnings: gaps in the wall, a groove too close to a wall, an opening with no way back.
5. **Race it.** Save, then either open it in Unity or have me run a test race overnight
   (lap times, wall hits, incidents), the same soak runs used for physics.

## Build order

| Phase | What | Result |
|---|---|---|
| 1 | Unity races any track file (track picker + `-okt-track <name>`); today v5 is hard-coded in RaceBootstrap | New tracks can be raced without code changes |
| 2 | The Python generator becomes a library the designer calls; v5 must come out byte-identical | Nothing about Atoka changes |
| 3 | Designer v1: templates, handles, widths, banking, walls/gaps, check panel, save | You can make and save an oval |
| 4 | "Race it": overnight soak report per track | Lap times and incidents for your design |
| 5 | Trace mode: photo / GPS / MyChron import | Real tracks from data |
| 6 | Infield features; dirt bank under the outside rail; later, surface layers (cushion, ridge, ruts) | Full track dressing |

Rough size: phases 1–3 are a few sessions of work; 4 reuses the soak tools; 5–6 depend on what you want first.

## Where it runs (needs your call, q48)

- **Recommended: a page in the browser** (PC, and phone for viewing). It doesn't freeze or recompile Unity,
  works while you're away from the PC, and saves the same track file.
- Alternative: a window inside Unity. You'd get a live 3D preview, but only with Unity open, and every change to the
  tool recompiles Unity (the freeze you already know).

## Prior art

UNITY-SPIKE.md §4.1 says test-track-one.json was "Franklin's hand-set Track Editor package" from the browser game.
If that editor still exists, it may be the right starting point (q49).
