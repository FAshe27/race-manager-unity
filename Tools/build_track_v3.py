"""Build Assets/Data/test-track-one-v3.json from Franklin's v2 data + his 2026-11-14 rulings.

Why a v3: the v2 outer edge was traced on art whose outside edge runs off the
image top/bottom, so the outside wall bowed in down the straights; the hand-
traced inner edge had elliptical, pinched ends (30-50 ft radius, wobbling).
Franklin's rulings (2026-11-14, from the Atoka video + overhead photo):
  - straight walls; 27 ft wide on the straights, 29 ft in the turns
  - turns rounder AND wider ("both")
  - barrier layout from his annotated overhead (guardrail / fence / 3 openings)

Construction:
  - OUTSIDE wall = stadium (straight rails tangent into semicircle ends), so the
    rail runs dead straight down both straights (Franklin, after M1 playtest: with
    the inside edge as the stadium, the extra turn width pushed the outside wall
    out at every turn and the straights read as bowed in)
  - straight/radius proportion from the v2 inside edge's footprint (x / z extent);
    sized so the CENTERLINE stays exactly 660 ft (201.17 m, 1/8-mile calibration)
  - width 27 ft on straights, easing to 29 ft over the first/last 30 deg of each
    end; the extra turn width comes off the INSIDE (infield)
  - groove (lineFrac) and go line carried over from v2 by matching sections:
    each straight maps to its straight, each end to its end (piecewise-linear in u)
  - cornerTable recomputed from the new centerline with the Track Editor's method

Run:  python Tools/build_track_v3.py   (from the project root; stdlib only)
"""
import json, math, os

FT = 0.3048
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Assets", "Data", "test-track-one.json")
DST = os.path.join(ROOT, "Assets", "Data", "test-track-one-v3.json")

W_STRAIGHT = 27 * FT      # Franklin
W_TURN = 29 * FT          # Franklin
EASE_DEG = 30.0           # width eases over the first/last 30 deg of each end [TEST VALUE]
LAP_M = 201.17            # 660 ft centerline, spec calibration
N = 256

v2 = json.load(open(SRC))
C2 = v2["centerlineM"]; HW2 = v2["halfWidthM"]

# v2 inside-edge footprint aspect
def normals(P):
    n = len(P); out = []
    for i in range(n):
        a = P[(i - 1) % n]; b = P[(i + 1) % n]
        tx, tz = b[0] - a[0], b[1] - a[1]; l = math.hypot(tx, tz)
        out.append((tz / l, -tx / l))  # outward for CCW travel
    return out
On2 = normals(C2)
I2 = [(C2[i][0] - On2[i][0] * HW2[i], C2[i][1] - On2[i][1] * HW2[i]) for i in range(N)]
xext = (max(p[0] for p in I2) - min(p[0] for p in I2)) / 2
zext = (max(p[1] for p in I2) - min(p[1] for p in I2)) / 2
ASPECT = xext / zext


def smooth(t):
    t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


def build(R, steps=8192):
    """Dense loop for OUTSIDE radius R. Returns list of (outer_pt, outward_n, width, section)."""
    a = (ASPECT - 1) * (R - W_STRAIGHT)  # half straight length, proportioned like v2's inside edge
    L_str = 2 * a; L_arc = math.pi * R
    per = 2 * L_str + 2 * L_arc
    pts = []
    for k in range(steps):
        s = k / steps * per
        # start mid top straight (x=0, z=+R), travel toward -x (CCW seen from +Y)
        if s < a:                                  # top straight, first half
            p = (-s, R); n = (0, 1); w = W_STRAIGHT; sec = "front"
        elif s < a + L_arc:                        # left end (turns 1-2)
            th = (s - a) / R                       # 0..pi
            p = (-a - R * math.sin(th), R * math.cos(th)); n = (-math.sin(th), math.cos(th))
            deg = math.degrees(th); w = W_STRAIGHT + (W_TURN - W_STRAIGHT) * smooth(min(deg, 180 - deg) / EASE_DEG); sec = "t12"
        elif s < a + L_arc + L_str:                # bottom straight
            p = (-a + (s - a - L_arc), -R); n = (0, -1); w = W_STRAIGHT; sec = "back"
        elif s < a + 2 * L_arc + L_str:            # right end (turns 3-4)
            th = (s - a - L_arc - L_str) / R
            p = (a + R * math.sin(th), -R * math.cos(th)); n = (math.sin(th), -math.cos(th))
            deg = math.degrees(th); w = W_STRAIGHT + (W_TURN - W_STRAIGHT) * smooth(min(deg, 180 - deg) / EASE_DEG); sec = "t34"
        else:                                      # top straight, second half
            p = (a - (s - a - 2 * L_arc - L_str), R); n = (0, 1); w = W_STRAIGHT; sec = "front"
        pts.append((p, n, w, sec))
    return pts, a


def centerline_len(R):
    pts, _ = build(R, 4096)
    C = [(p[0] - n[0] * w / 2, p[1] - n[1] * w / 2) for p, n, w, _ in pts]
    return sum(math.dist(C[i], C[(i + 1) % len(C)]) for i in range(len(C)))


lo, hi = 12.0, 45.0
for _ in range(60):
    mid = (lo + hi) / 2
    if centerline_len(mid) > LAP_M: hi = mid
    else: lo = mid
R = (lo + hi) / 2
dense, A = build(R, 16384)
Cd = [(p[0] - n[0] * w / 2, p[1] - n[1] * w / 2) for p, n, w, _ in dense]
arc = [0.0]
for i in range(len(Cd)): arc.append(arc[-1] + math.dist(Cd[i], Cd[(i + 1) % len(Cd)]))
total = arc[-1]

# resample to N samples, equal centerline arc
C3, HW3, SEC, J = [], [], [], 0
for i in range(N):
    target = i / N * total
    while arc[J + 1] < target: J += 1
    C3.append(Cd[J]); HW3.append(dense[J][2] / 2); SEC.append(dense[J][3])

# section boundaries in u for v3 (exact) — where the ends begin/finish
def boundaries(sec):
    b = {}
    for i in range(N):
        a_, b_ = sec[i - 1], sec[i]
        if a_ != b_: b[(a_, b_)] = i / N
    return b
B3 = boundaries(SEC)
u3 = [B3[("front", "t12")], B3[("t12", "back")], B3[("back", "t34")], B3[("t34", "front")]]

# v2 section boundaries: where v2's cornerTable crosses 0.5
ct2 = v2["cornerTable"]
cross = [i / N for i in range(N) if (ct2[i - 1] < 0.5) != (ct2[i] < 0.5)]
cross.sort()
assert len(cross) == 4, cross
u2 = cross  # [t1 entry, t2 exit, t3 entry, t4 exit]


def map_u(u, src, dst):
    """Piecewise-linear map of lap fraction u between matching section boundaries."""
    s = src + [src[0] + 1]; d = dst + [dst[0] + 1]
    uu = u if u >= s[0] else u + 1
    for k in range(4):
        if s[k] <= uu <= s[k + 1]:
            t = (uu - s[k]) / (s[k + 1] - s[k]); return (d[k] + t * (d[k + 1] - d[k])) % 1.0
    return u


def interp_loop(vals, u):
    f = u * N; i0 = int(math.floor(f)) % N; t = f - math.floor(f)
    return vals[i0] * (1 - t) + vals[(i0 + 1) % N] * t


# groove: v3 sample i takes v2's lineFrac at the matching u
frac3 = [round(interp_loop(v2["lineFrac"], map_u(i / N, u3, u2)), 4) for i in range(N)]
goU3 = round(map_u(v2["goU"], u2, u3), 4)

# cornerTable: Track Editor method (smoothed heading change / arc, ref = 95th pct)
def circ_smooth(v, w):
    n = len(v); h = w // 2
    return [sum(v[(i + j) % n] for j in range(-h, h + 1)) / w for i in range(n)]
M2 = 512
C512 = []
J = 0
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
ct3 = [round(c512[i * 2], 3) for i in range(N)]

# ---- barriers (Franklin's annotated overhead, 2026-11-14). u ranges along the
# outside edge; anything not listed is an opening (flat run-off, no collider).
# Opening widths are estimated from the photo [TEST VALUE].
L_front_half = A                              # inside-edge half straight (m)
u_t1 = u3[0]; u_t2 = u3[1]; u_t3 = u3[2]; u_t4 = u3[3]
m_per_u = total
fence_start = 10 * FT / m_per_u                               # "just past S/F": 10 ft [TEST VALUE]
fence_end = 0.68 * u_t1                                       # fence runs ~2/3 of the way to turn 1 (photo)
open_t1 = (fence_end, fence_end + 16 * FT / m_per_u)          # access road at the top of turn 1: ~16 ft [TEST VALUE]
open_t3 = (u_t3 - 18 * FT / m_per_u, u_t3 + 14 * FT / m_per_u)  # track exit at the top of turn 3: ~32 ft [TEST VALUE]
open_t4 = (u_t4 - 6 * FT / m_per_u, u_t4 + 6 * FT / m_per_u)    # grid entrance at the top of turn 4: ~12 ft [TEST VALUE]
barriers = [
    {"type": "fence", "u0": round(fence_start, 4), "u1": round(fence_end, 4)},
    {"type": "guardrail", "u0": round(open_t1[1], 4), "u1": round(open_t3[0], 4)},
    {"type": "guardrail", "u0": round(open_t3[1], 4), "u1": round(open_t4[0], 4)},
    {"type": "guardrail", "u0": round(open_t4[1], 4), "u1": round(1.0 + fence_start, 4)},
]
openings = [
    {"name": "access road (top of turn 1)", "u0": round(open_t1[0], 4), "u1": round(open_t1[1], 4)},
    {"name": "track exit (top of turn 3)", "u0": round(open_t3[0], 4), "u1": round(open_t3[1], 4)},
    {"name": "grid entrance (top of turn 4)", "u0": round(open_t4[0], 4), "u1": round(open_t4[1], 4)},
]

out = {
    "name": "Test Track One",
    "version": 3,
    "source": "Derived from v2 (" + os.path.basename(SRC) + ") by Tools/build_track_v3.py per Franklin's 2026-11-14 rulings: "
              "straight walls, 27 ft straights / 29 ft turns, rounder+wider turns, barrier layout from his annotated overhead.",
    "lapLengthM": LAP_M,
    "samples": N,
    "goU": goU3,
    "bankingDeg": v2.get("bankingDeg", 8),
    "cornerTable": ct3,
    "centerlineM": [[round(p[0], 3), round(p[1], 3)] for p in C3],
    "halfWidthM": [round(h, 3) for h in HW3],
    "lineFrac": frac3,
    "barriers": barriers,
    "openings": openings,
    "sections": {"turn1Entry": round(u_t1, 4), "turn2Exit": round(u_t2, 4), "turn3Entry": round(u_t3, 4), "turn4Exit": round(u_t4, 4)},
    "notes": "Units are metres (engine units); Franklin-facing values are reported in feet. Sample 0 = start/finish, "
             "mid front straight; travel counter-clockwise seen from above; world X = json x, world Z = json y. "
             "lineFrac 0 = inside edge, 1 = outside edge. No racingLineM: v2's was mirrored; build the line from lineFrac.",
}
json.dump(out, open(DST, "w"), indent=1)
print("outside radius %.1f ft, straights %.1f ft, centerline %.1f ft, inside radius in turns %.1f ft"
      % (R / FT, 2 * A / FT, total / FT, (R - W_TURN) / FT))
print("sections u:", [round(x, 4) for x in u3], " v2:", [round(x, 4) for x in u2], " goU v2 %.4f -> v3 %.4f" % (v2["goU"], goU3))
print("barriers:", barriers); print("openings:", openings)
