"""Add Atoka's off-track scenery to the track JSON as "features" (Franklin's markup over the Google Earth aerial,
2026-11-16; photos IMG_1998 grid lane, IMG_4521 T4 exit). Points are (u around the lap, feet beyond the outside edge),
so they follow the track. All sizes are [TEST VALUE]s measured off the aerial (~0.4 ft per pixel at the track's scale).

Laid out counterclockwise from start/finish (u = 0, middle of the front straight, top of the aerial):
- turn 1 access road (tractor and emergency vehicles only), through the opening at the top of turn 1;
- the exit road: from the gap at the end of the backstretch (before turn 3) around the outside of turns 3-4 to the
  scale house, beside the grid;
- the grid lane: a paved chute between chain-link fences from the red swing gate at the turn 4 exit up to the staging
  grid (paved, fenced), where karts line up before their race.

Visual only for now: the karts don't use any of it yet (the openings in the wall are already real gaps).
Run: python Tools/add_atoka_features.py Assets/Data/test-track-one-v4.json
"""
import json, sys

# Wall layout from Franklin's markup over the Unity view (2026-11-16), mapped onto v4's shape. (v4 had kept v3's u
# values, which put the grid gate mid-straight once the straights got shorter.)
FENCE_U0, FENCE_U1 = 0.0625, 0.0977   # chain link at the turn 1 end of the front straight
T1_U0, T1_U1 = 0.0977, 0.1719         # no guardrail at the top of turn 1 (the access road comes in here)
EXIT_U0, EXIT_U1 = 0.5508, 0.6094     # the gap at the end of the backstretch onto the exit road
GATE_U0, GATE_U1 = 0.8672, 0.9102     # the grid gate at the turn 4 exit, just past the restart line

def mid(a, b): return round((a + b) / 2, 4)

features = [
    {"type": "dirtroad", "name": "turn 1 access road (tractor, emergency vehicles)", "widthFt": 12,
     "points": [[mid(T1_U0, T1_U1), 0], [mid(T1_U0, T1_U1), 30], [0.125, 70], [0.12, 110]]},
    {"type": "road", "name": "exit road (to the scale house)", "widthFt": 10,
     "points": [[mid(EXIT_U0, EXIT_U1), 0], [0.615, 14], [0.66, 14], [0.74, 14], [0.80, 14], [0.835, 16],
                [0.855, 32], [0.865, 52]]},
    {"type": "building", "name": "scale house", "widthFt": 12, "depthFt": 10, "heightFt": 9,
     "points": [[0.842, 30]]},
    {"type": "road", "name": "grid lane", "widthFt": 20,
     "points": [[mid(GATE_U0, GATE_U1), 0], [mid(GATE_U0, GATE_U1), 55]]},
    {"type": "gate", "name": "grid gate (red swing gate)", "heightFt": 3.5,
     "points": [[GATE_U0, 1], [GATE_U1, 1]]},
    {"type": "fence", "name": "grid lane fence (left)", "heightFt": 5,
     "points": [[mid(GATE_U0, GATE_U1) - 0.0152, 1], [mid(GATE_U0, GATE_U1) - 0.0130, 55]]},  # lane edges: 10 ft each side
    {"type": "fence", "name": "grid lane fence (right)", "heightFt": 5,
     "points": [[mid(GATE_U0, GATE_U1) + 0.0152, 1], [mid(GATE_U0, GATE_U1) + 0.0130, 55]]},
    {"type": "pad", "name": "staging grid", "points": [[0.872, 55], [0.935, 55], [0.945, 125], [0.862, 125]]},
    {"type": "fence", "name": "staging grid fence", "heightFt": 5,
     "points": [[mid(GATE_U0, GATE_U1) + 0.0130, 55], [0.935, 55], [0.945, 125], [0.862, 125], [0.866, 72]]},
]

path = sys.argv[1]
d = json.load(open(path))
d["features"] = features
d["barriers"] = [
    {"type": "fence", "u0": FENCE_U0, "u1": FENCE_U1},
    {"type": "guardrail", "u0": T1_U1, "u1": EXIT_U0},
    {"type": "guardrail", "u0": EXIT_U1, "u1": GATE_U0},
    {"type": "guardrail", "u0": GATE_U1, "u1": 1 + FENCE_U0},
]
d["openings"] = [
    {"name": "access road (top of turn 1, no guardrail)", "u0": T1_U0, "u1": T1_U1},
    {"name": "track exit (end of the backstretch, onto the exit road)", "u0": EXIT_U0, "u1": EXIT_U1},
    {"name": "grid gate (turn 4 exit)", "u0": GATE_U0, "u1": GATE_U1},
]
json.dump(d, open(path, "w"), indent=1)
print(f"{path}: {len(features)} features")
