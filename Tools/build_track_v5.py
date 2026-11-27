"""Build Assets/Data/test-track-one-v5.json: Atoka sized and grooved from the MyChron GPS line.

Why a v5 (Franklin, 2026-11-26): his MyChron data (the #72, 10.70-10.82 s laps) shows a racing line of
~688 ft, 242 x 190 ft, 75-95 ft radius through the ends and 150-300 ft down the sides, where the kart is
still turning at 0.5-0.8 g and accelerating to 47-49 mph. v4 (sized to a 660 ft centreline) put a 620 ft
line in a smaller footprint, so the sim could not build speed down the sides. Franklin: "Yes, build v5"
(lets the centreline go past the "1/8 mile" 660 ft).

Construction (v4's, re-sized to the GPS):
  - groove = the GPS line itself (Assets/Data/atoka-gps-line.json, Tools/atoka_gps_line.py)
  - OUTSIDE wall = stadium (straight rails, semicircle ends) as in v3/v4, fitted around the GPS line with
    v4's clearances: line 4.5 ft off the outside wall mid-straight (v4: +9 ft of 27), 3.7 ft off the
    inside edge at the apex (v4: -10.8 ft of 29) [TEST VALUEs carried from v2/v4]
  - width 27 ft straights / 29 ft turns (Franklin), eased over 30 deg; extra turn width off the infield
  - cornerTable by the Track Editor's method (as v4)
  - everything placed by lap fraction (barriers, openings, go line, features: staging grid, roads, fences)
    carried from v4 by matching sections (piecewise-linear in u); feature "feet out" kept as they are
  - sample 0 = start/finish where the MyChron's lap beacon fired (mid front stretch, as before)

Run: python Tools/build_track_v5.py   (from the project root; stdlib only)
"""
import json, math, os

FT = 0.3048
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
V4 = os.path.join(ROOT, "Assets", "Data", "test-track-one-v4.json")
GPS = os.path.join(ROOT, "Assets", "Data", "atoka-gps-line.json")
DST = os.path.join(ROOT, "Assets", "Data", "test-track-one-v5.json")

W_STRAIGHT = 27 * FT      # Franklin
W_TURN = 29 * FT          # Franklin
EASE_DEG = 30.0           # [TEST VALUE] (v4)
CLEAR_WALL_FT = 4.5       # line to outside wall, mid-straight [TEST VALUE] (v4)
CLEAR_APEX_FT = 3.7       # line to inside edge at the apex [TEST VALUE] (v4)
# Franklin 11-27 (first v5 playtest): "where the kart runs in the middle of the corner is about 18-24 in too high" --
# the GPS line (+-1.6 ft fix accuracy) is lowered through mid-corner, weighted by corner^2, never closer to the
# inside edge than the kart's left side (~19 in from its centre) + ~1 in: left tires right up to the inside ridge.
APEX_DROP_FT = 1.5
LINE_MIN_IN_FT = 1.85     # 1.7 put karts on the inside ridge (turn 3-4 self spins, soak 11-27)
N = 256

v4 = json.load(open(V4))
G = json.load(open(GPS)); L = [tuple(p) for p in G["pointsM"]]; ML = len(L)

half_z = max(abs(p[1]) for p in L); half_x = max(abs(p[0]) for p in L)
R = half_z + CLEAR_WALL_FT * FT                                   # outside radius = half width of the footprint
A = half_x - (R - W_TURN) - CLEAR_APEX_FT * FT                    # half straight
SF_X = L[0][0]                                                    # start/finish x on the front straight


def smooth(t):
    t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


def build(steps):
    """Dense loop: (outer point, outward normal, width, section), starting at the start/finish, CCW."""
    L_str = 2 * A; L_arc = math.pi * R; per = 2 * L_str + 2 * L_arc
    s0 = -SF_X if -A <= -SF_X <= A else 0.0                       # top straight runs from x=+A toward -A
    out = []
    for k in range(steps):
        s = (s0 + k / steps * per) % per
        if s < A:
            p = (-s, R); n = (0, 1); w = W_STRAIGHT; sec = "front"
        elif s < A + L_arc:
            th = (s - A) / R
            p = (-A - R * math.sin(th), R * math.cos(th)); n = (-math.sin(th), math.cos(th))
            deg = math.degrees(th); w = W_STRAIGHT + (W_TURN - W_STRAIGHT) * smooth(min(deg, 180 - deg) / EASE_DEG); sec = "t12"
        elif s < A + L_arc + L_str:
            p = (-A + (s - A - L_arc), -R); n = (0, -1); w = W_STRAIGHT; sec = "back"
        elif s < A + 2 * L_arc + L_str:
            th = (s - A - L_arc - L_str) / R
            p = (A + R * math.sin(th), -R * math.cos(th)); n = (math.sin(th), -math.cos(th))
            deg = math.degrees(th); w = W_STRAIGHT + (W_TURN - W_STRAIGHT) * smooth(min(deg, 180 - deg) / EASE_DEG); sec = "t34"
        else:
            p = (A - (s - A - 2 * L_arc - L_str), R); n = (0, 1); w = W_STRAIGHT; sec = "front"
        out.append((p, n, w, sec))
    return out


dense = build(16384)
Cd = [(p[0] - n[0] * w / 2, p[1] - n[1] * w / 2) for p, n, w, _ in dense]
arc = [0.0]
for i in range(len(Cd)): arc.append(arc[-1] + math.dist(Cd[i], Cd[(i + 1) % len(Cd)]))
total = arc[-1]
C, HW, SEC, ON, J = [], [], [], [], 0
for i in range(N):
    target = i / N * total
    while arc[J + 1] < target: J += 1
    C.append(Cd[J]); HW.append(dense[J][2] / 2); SEC.append(dense[J][3]); ON.append(dense[J][1])


def boundaries(sec):
    b = {}
    for i in range(N):
        if sec[i - 1] != sec[i]: b[(sec[i - 1], sec[i])] = i / N
    return b
B = boundaries(SEC)
u5 = [B[("front", "t12")], B[("t12", "back")], B[("back", "t34")], B[("t34", "front")]]
s4 = v4["sections"]; u4 = [s4["turn1Entry"], s4["turn2Exit"], s4["turn3Entry"], s4["turn4Exit"]]


def map_u(u, src=u4, dst=u5):
    if u >= 1.0: return map_u(u - 1.0, src, dst) + 1.0
    s = src + [src[0] + 1]; d = dst + [dst[0] + 1]
    uu = u if u >= s[0] else u + 1
    for k in range(4):
        if s[k] <= uu <= s[k + 1]:
            t = (uu - s[k]) / (s[k + 1] - s[k]); return (d[k] + t * (d[k + 1] - d[k])) % 1.0
    return u


# groove: where each sample's outward normal crosses the GPS line (inside the GPS loop = toward the infield)
def inside_line(x, z):
    c = False
    for k in range(ML):
        (x1, z1), (x2, z2) = L[k], L[(k + 1) % ML]
        if (z1 > z) != (z2 > z) and x < x1 + (z - z1) * (x2 - x1) / (z2 - z1): c = not c
    return c
frac, clear_in, clear_out = [], [], []
for i in range(N):
    px, pz = C[i]; nx, nz = ON[i]; hw = HW[i]; lo, hi = -hw, hw
    assert inside_line(px + nx * lo, pz + nz * lo) and not inside_line(px + nx * hi, pz + nz * hi), f"line leaves the track at sample {i}"
    for _ in range(50):
        mid = (lo + hi) / 2
        if inside_line(px + nx * mid, pz + nz * mid): lo = mid
        else: hi = mid
    off = (lo + hi) / 2; frac.append(round(off / (2 * hw) + 0.5, 4))
    clear_in.append((off + hw) / FT); clear_out.append((hw - off) / FT)

# cornerTable: Track Editor method (as v4)
def circ_smooth(v, w):
    n = len(v); h = w // 2
    return [sum(v[(i + j) % n] for j in range(-h, h + 1)) / w for i in range(n)]
M2 = 512; C512 = []; J = 0
for i in range(M2):
    target = i / M2 * total
    while arc[J + 1] < target: J += 1
    C512.append(Cd[J])
sm = list(zip(circ_smooth([p[0] for p in C512], 17), circ_smooth([p[1] for p in C512], 17)))
kap = []
for i in range(M2):
    pA, pB, pC = sm[(i - 6) % M2], sm[i], sm[(i + 6) % M2]
    h1 = math.atan2(pB[1] - pA[1], pB[0] - pA[0]); h2 = math.atan2(pC[1] - pB[1], pC[0] - pB[0])
    dh = (h2 - h1 + math.pi) % (2 * math.pi) - math.pi
    kap.append(abs(dh) / ((math.dist(pA, pB) + math.dist(pB, pC)) / 2))
kref = sorted(kap)[int(0.95 * (M2 - 1))]
c512 = circ_smooth([min(1.0, k / kref * 1.02) for k in kap], 9)
ct = [round(c512[i * 2], 3) for i in range(N)]

# mid-corner drop (Franklin 11-27)
for i in range(N):
    hw = HW[i]; off = (frac[i] - 0.5) * 2 * hw; w = ct[i] ** 2
    lo_off = -hw + LINE_MIN_IN_FT * FT
    new = max(min(off, lo_off), off - APEX_DROP_FT * FT * w) if w > 0 else off
    frac[i] = round(new / (2 * hw) + 0.5, 4); clear_in[i] = (new + hw) / FT
print(f"after the mid-corner drop: groove off the inside edge {min(clear_in):.1f}..{max(clear_in):.1f} ft")

# smooth the groove (11-27): GPS noise + the mid-corner clamp left kinks (line radius 73 -> 129 -> 73 ft through turn 3)
# that the radius speed plan read as tight spots. 9 samples (~25 ft), two passes, circular moving average of the offset.
SMOOTH_W = 9
offs = [(frac[i] - 0.5) * 2 * HW[i] for i in range(N)]
h = SMOOTH_W // 2
for _ in range(2):
    offs = [sum(offs[(i + j) % N] for j in range(-h, h + 1)) / SMOOTH_W for i in range(N)]
for i in range(N):
    hw = HW[i]; o = max(offs[i], -hw + LINE_MIN_IN_FT * FT)
    frac[i] = round(o / (2 * hw) + 0.5, 4); clear_in[i] = (o + hw) / FT
print(f"after smoothing: groove off the inside edge {min(clear_in):.1f}..{max(clear_in):.1f} ft")


# carried from v4 by lap fraction
barriers = [dict(b, u0=round(map_u(b["u0"]), 4), u1=round(map_u(b["u1"]), 4)) for b in v4["barriers"]]
openings = [dict(o, u0=round(map_u(o["u0"]), 4), u1=round(map_u(o["u1"]), 4)) for o in v4["openings"]]
features = []
for f in v4.get("features", []):
    f = dict(f); f["points"] = [[round(map_u(u), 6), ft] for u, ft in f["points"]]; features.append(f)

out = dict(v4)
out.update({
    "version": 5,
    "source": "v5: sized and grooved from Franklin's MyChron GPS (" + G["source"] + "); v4's construction "
              "(stadium outside wall, 27/29 ft, features by lap fraction) re-fitted around the GPS line by Tools/build_track_v5.py.",
    "lapLengthM": round(total, 2),
    "goU": round(map_u(v4["goU"]), 4),
    "cornerTable": ct,
    "centerlineM": [[round(p[0], 3), round(p[1], 3)] for p in C],
    "halfWidthM": [round(h, 3) for h in HW],
    "lineFrac": frac,
    "barriers": barriers, "openings": openings, "features": features,
    "sections": {"turn1Entry": round(u5[0], 4), "turn2Exit": round(u5[1], 4), "turn3Entry": round(u5[2], 4), "turn4Exit": round(u5[3], 4)},
    "notes": v4["notes"] + " 2026-11-26 v5: re-sized to the MyChron GPS line (groove = the measured line); everything "
             "placed by u carried from v4 section by section.",
})
json.dump(out, open(DST, "w"), indent=1)
print(f"outside radius {R / FT:.1f} ft, straights {2 * A / FT:.1f} ft, footprint {2 * (A + R) / FT:.0f} x {2 * R / FT:.0f} ft, "
      f"centerline {total / FT:.0f} ft, inside radius in turns {(R - W_TURN) / FT:.1f} ft")
print(f"groove off the inside edge {min(clear_in):.1f}..{max(clear_in):.1f} ft, off the outside wall {min(clear_out):.1f}..{max(clear_out):.1f} ft")
print("sections v4", [round(x, 4) for x in u4], "-> v5", [round(x, 4) for x in u5], f"goU {v4['goU']} -> {out['goU']}")
print("barriers", barriers); print("openings", [(o['name'][:20], o['u0'], o['u1']) for o in openings])
