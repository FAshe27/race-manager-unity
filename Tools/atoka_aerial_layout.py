"""Atoka off-track layout from the aerial (2026-11-17 night: Franklin's sketch, his blue/red markup in
Track Photos/atoka-openings-aerial-markup.webp, and his rulings -- Lane A east, Lane B west, white strip between).
Aerial pixels -> (u, feet beyond the outside edge), solved against the game's own Catmull-Rom track frame
(TrackModel.Frame), then a check drawing of everything over the aerial.
   python Tools/atoka_aerial_layout.py "../Track Photos/atoka-aerial-2025.webp" Assets/Data/test-track-one-v4.json features.json check.png
The features it writes replace everything in the track JSON's "features" except the turn 1 access road."""
import json, math, sys
from PIL import Image, ImageDraw
AERIAL, J, OUT, PNG = sys.argv[1:5]
d = json.load(open(J)); c = d['centerlineM']; hw = d['halfWidthM']; N = len(c)
ax0, ax1, ay0, ay1 = 165, 842, 318, 768
FT = 0.3048
_area = sum(c[i][0] * c[(i + 1) % N][1] - c[(i + 1) % N][0] * c[i][1] for i in range(N)); OUTS = 1.0 if _area >= 0 else -1.0
def CR(p0, p1, p2, p3, t):
    t2 = t * t; t3 = t2 * t
    return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
def CRd(p0, p1, p2, p3, t):
    t2 = t * t
    return 0.5 * ((-p0 + p2) + 2 * (2 * p0 - 5 * p1 + 4 * p2 - p3) * t + 3 * (-p0 + 3 * p1 - 3 * p2 + p3) * t2)
def frame(u):
    """exactly TrackModel.Frame: Catmull-Rom position, tangent and half-width"""
    s = (u % 1.0) * N; i1 = int(math.floor(s)); t = s - i1
    i0, i2, i3 = (i1 - 1) % N, (i1 + 1) % N, (i1 + 2) % N; i1 %= N
    x = CR(c[i0][0], c[i1][0], c[i2][0], c[i3][0], t); y = CR(c[i0][1], c[i1][1], c[i2][1], c[i3][1], t)
    dx = CRd(c[i0][0], c[i1][0], c[i2][0], c[i3][0], t); dy = CRd(c[i0][1], c[i1][1], c[i2][1], c[i3][1], t)
    L = math.hypot(dx, dy); tx, ty = dx / L, dy / L
    h = CR(hw[i0], hw[i1], hw[i2], hw[i3], t)
    return x, y, tx, ty, ty * OUTS, -tx * OUTS, h
def W(u, outFt):
    x, y, tx, ty, nx, ny, h = frame(u); dd = h + outFt * FT
    return x + nx * dd, y + ny * dd
xs, ys = zip(*[W(i / N, 0) for i in range(N)])
mnx, mxx, mny, mxy = min(xs), max(xs), min(ys), max(ys)
def P(x, y): return (ax0 + (x - mnx) / (mxx - mnx) * (ax1 - ax0), ay1 - (y - mny) / (mxy - mny) * (ay1 - ay0))
def Pinv(px, py): return (mnx + (px - ax0) / (ax1 - ax0) * (mxx - mnx), mny + (ay1 - py) / (ay1 - ay0) * (mxy - mny))
FR = [frame(k / 20000) for k in range(20000)]
def UO(px, py):
    """aerial pixel -> (u, feet beyond the outside edge): the foot of the outward normal through the point, found as a
    root of the along-track offset (interpolated between samples) -- the nearest-sample pick jumped about far out"""
    X, Y = Pinv(px, py); prev = None; best = None
    for k in range(len(FR) + 1):
        x, y, tx, ty, nx, ny, h = FR[k % len(FR)]
        ex, ey = x + nx * h, y + ny * h
        along = (X - ex) * tx + (Y - ey) * ty; out = (X - ex) * nx + (Y - ey) * ny
        if prev is not None and (prev[1] > 0) != (along > 0) and out > -6 and prev[2] > -6:
            f = prev[1] / (prev[1] - along); u = (prev[0] + f / len(FR)) % 1.0; o = prev[2] + (out - prev[2]) * f
            if best is None or abs(o) < abs(best[1]): best = (u, o)
        prev = (k / len(FR), along, out)
    return round(best[0], 6), round(max(best[1] / FT, 1.0), 2)
def Q(u, o): return P(*W(u, o))
def line_px(pts, step=6):
    """dense pixel polyline (straight in the WORLD between the given pixels)"""
    out = []
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        n = max(1, int(math.hypot(x1 - x0, y1 - y0) / step))
        out += [(x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n) for k in range(n)]
    out.append(pts[-1]); return out
PXFT = (ax1 - ax0) / ((mxx - mnx) / FT)   # aerial pixels per foot
print(f"scale: {PXFT:.2f} px/ft")

# ---- read off the aerial (pixels of aerial.webp) -----------------------------------------------------------------
GRID = dict(xW=797, xE=848, yN=128, yS=334)          # the staging grid: Franklin's blue lines on atoka-openings-aerial-markup (x 797..848)
GATE = [(733, 343), (757, 368)]                       # opening at the end of the turn 4 wall (track edge)
LINK = [(745, 352), (790, 340), (830, 336)]           # grid's south end -> gate (karts drive it the other way)
EXIT = [(703, 746), (735, 745), (762, 737), (797, 718), (821, 681), (846, 640), (864, 600), (870, 560), (871, 520), (867, 470), (863, 420), (867, 370), (872, 336)]
SCALE = [(871, 279), (931, 279), (931, 320), (871, 320)]  # scale house footprint (blue roof)
FENCE_W = [(797, 128), (797, 326)]                    # chain link along the grid's west side (grass square)
FENCE_S = [(682, 324), (797, 326)]                    # grass square's south fence, along the front stretch
TRI_N = [(757, 369), (843, 333)]                      # fence on the north side of the grass triangle outside turn 4
TRI_E = [(843, 333), (856, 430)]

feats = []
def feat(**k): feats.append(k)
xm = (GRID['xW'] + GRID['xE']) / 2
feat(type='road', name='grid lane', widthFt=20, points=[UO(*p) for p in [((GATE[0][0] + GATE[1][0]) / 2, (GATE[0][1] + GATE[1][1]) / 2)] + LINK[1:] + [(xm + (GRID['xE'] - xm) / 2, GRID['yS'] - 2)]])
feat(type='gate', name='grid gate (red swing gate)', heightFt=3.5, points=[UO(*p) for p in GATE])
feat(type='pad', name='staging grid', points=[UO(*p) for p in line_px([(GRID['xW'], GRID['yS']), (GRID['xE'], GRID['yS']), (GRID['xE'], GRID['yN']), (GRID['xW'], GRID['yN']), (GRID['xW'], GRID['yS'])])][:-1])
feat(type='pad', name='grid link (to the gate)', points=[UO(*p) for p in line_px([(733, 340), (797, 327), (848, 331), (757, 369)])])
feat(type='paint', name='grid centre strip (Lane B west | Lane A east)', widthFt=0.33, points=[UO(*p) for p in line_px([(xm, GRID['yS']), (xm, GRID['yN'])], 10)])
feat(type='road', name='exit road (to the scale house)', widthFt=10, points=[UO(*p) for p in line_px(EXIT, 12)])
feat(type='building', name='scale house', heightFt=9, points=[UO(*p) for p in SCALE])
feat(type='fence', name='grid fence (west)', heightFt=5, points=[UO(*p) for p in line_px(FENCE_W, 10)])
feat(type='fence', name='front stretch fence (grass square)', heightFt=5, points=[UO(*p) for p in line_px(FENCE_S, 10)])
feat(type='fence', name='turn 4 triangle fence', heightFt=4, points=[UO(*p) for p in line_px(TRI_N + TRI_E[1:], 10)])
# the grid's two lanes, for the staging spawn: centre lines from the south (exit) end to the north end
lanes = {'A': (xm + (GRID['xE'] - xm) / 2), 'B': (GRID['xW'] + (xm - GRID['xW']) / 2)}
for nm, x in lanes.items():
    feat(type='lane', name=f'grid lane {nm}', widthFt=round((GRID['xE'] - xm) / PXFT, 1), points=[UO(x, GRID['yS'] - 4), UO(x, GRID['yN'] + 4)])
json.dump(feats, open(OUT, 'w'), indent=1)

im = Image.open(AERIAL).convert('RGB'); dr = ImageDraw.Draw(im)
col = {'road': (255, 140, 0), 'pad': (0, 160, 255), 'gate': (255, 0, 0), 'fence': (255, 255, 0), 'building': (255, 0, 255), 'paint': (255, 255, 255), 'lane': (0, 255, 0)}
for f in feats:
    p = [Q(u, o) for u, o in f['points']]
    if f['type'] in ('pad', 'building'): dr.polygon(p, outline=col[f['type']])
    else: dr.line(p, fill=col[f['type']], width=2 if f['type'] != 'lane' else 1)
im = im.crop((640, 80, 1000, 780)); im = im.resize((im.width * 2, im.height * 2), Image.LANCZOS); im.save(PNG)
for f in feats: print(f['type'], f['name'], f['points'][:2], '...' if len(f['points']) > 2 else '')
