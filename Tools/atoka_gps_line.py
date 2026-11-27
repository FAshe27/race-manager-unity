"""Atoka racing line from Franklin's MyChron 6 session (2026-11-26) -> Assets/Data/atoka-gps-line.json.

Source: OKT Racing/Telemetry/Telemetry Files for RaceStudio3/Elliott Ashe_72 Tricon_Atoka_a_0144.xrk
(the #72 Tricon, 12/55; Franklin's screenshots are from this file). The .xrk is AiM's tagged-record log:
  <hGPS\\0 + uint32 len + 4 bytes, payload: uint32 t_ms, uint32 tow, 2 x uint32, int32 ECEF X/Y/Z (cm),
  int32 posAcc, int32 ECEF vX/vY/vZ (cm/s), ... (25 Hz)
  <hLAP\\0 + uint32 len + 4 bytes, payload: uint16, uint16 lap#, uint32 lap ms, 8 bytes, uint32 end t_ms
Lap times decoded here match RaceStudio's (10.700 / 10.781 / 10.698 / 10.815 are records 6 / 10 / 11 / 17).

Output: the mean of the fast laps (< 11 s), each resampled by arc fraction from its own start (the lap
beacon = start/finish, mid front stretch), then rotated so the long axis is x, the front stretch is +z
(top) and travel is counter-clockwise seen from above -- the track JSON convention. Metres.

Run: python Tools/atoka_gps_line.py   (stdlib only)
"""
import json, math, os, struct

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
XRK = os.path.join(os.path.dirname(ROOT), "Telemetry", "Telemetry Files for RaceStudio3",
                   "Elliott Ashe_72 Tricon_Atoka_a_0144.xrk")
DST = os.path.join(ROOT, "Assets", "Data", "atoka-gps-line.json")
FT = 0.3048
M = 512            # output samples

d = open(XRK, "rb").read()
gps, laps, i = [], [], 0
while (i := d.find(b"<hGPS\x00", i)) >= 0:
    n = struct.unpack_from("<I", d, i + 6)[0]; p = i + 12
    t, _, _, _, X, Y, Z, acc, vx, vy, vz = struct.unpack_from("<IIIIiiiiiii", d, p)
    gps.append((t, X / 100, Y / 100, Z / 100, vx / 100, vy / 100, vz / 100)); i = p + n
i = 0
while (i := d.find(b"<hLAP\x00", i)) >= 0:
    p = i + 12; _, num, ms = struct.unpack_from("<HHI", d, p); tend = struct.unpack_from("<I", d, p + 16)[0]
    laps.append((num, ms, tend)); i = p + 20

# ECEF -> local east/north about the session's middle fix (WGS84)
a_ = 6378137.0; e2 = 6.69437999014e-3; b_ = a_ * math.sqrt(1 - e2); ep2 = (a_ * a_ - b_ * b_) / (b_ * b_)
X0, Y0, Z0 = gps[len(gps) // 2][1:4]
pp = math.hypot(X0, Y0); th = math.atan2(Z0 * a_, pp * b_)
lat0 = math.atan2(Z0 + ep2 * b_ * math.sin(th) ** 3, pp - e2 * a_ * math.cos(th) ** 3); lon0 = math.atan2(Y0, X0)
sl, cl, so, co = math.sin(lat0), math.cos(lat0), math.sin(lon0), math.cos(lon0)
def en(X, Y, Z):
    dx, dy, dz = X - X0, Y - Y0, Z - Z0
    return (-so * dx + co * dy, -sl * co * dx - sl * so * dy + cl * dz)

def resample(P, m):
    arc = [0.0]
    for k in range(1, len(P)): arc.append(arc[-1] + math.dist(P[k - 1], P[k]))
    out, j = [], 0
    for k in range(m):
        s = k / m * arc[-1]
        while arc[j + 1] < s: j += 1
        t = (s - arc[j]) / max(1e-9, arc[j + 1] - arc[j])
        out.append((P[j][0] + t * (P[j + 1][0] - P[j][0]), P[j][1] + t * (P[j + 1][1] - P[j][1])))
    return out, arc[-1]

fast = [(num, ms, te) for num, ms, te in laps if ms < 11000]
runs, lens = [], []
for num, ms, te in fast:
    P = [en(*g[1:4]) for g in gps if te - ms <= g[0] <= te]
    R, L = resample(P, M); runs.append(R); lens.append(L)
mean = [(sum(r[k][0] for r in runs) / len(runs), sum(r[k][1] for r in runs) / len(runs)) for k in range(M)]
spread = max(max(math.dist(r[k], mean[k]) for r in runs) for k in range(M))

# orient: centre on the bounding box of the principal axes, long axis -> x, start/finish (sample 0) -> +z
cx = sum(p[0] for p in mean) / M; cy = sum(p[1] for p in mean) / M
sxx = sum((p[0] - cx) ** 2 for p in mean); syy = sum((p[1] - cy) ** 2 for p in mean); sxy = sum((p[0] - cx) * (p[1] - cy) for p in mean)
ang = 0.5 * math.atan2(2 * sxy, sxx - syy)                       # long axis direction
ca, sa = math.cos(-ang), math.sin(-ang)
rot = [((p[0] - cx) * ca - (p[1] - cy) * sa, (p[0] - cx) * sa + (p[1] - cy) * ca) for p in mean]
if rot[0][1] < 0: rot = [(-x, -z) for x, z in rot]                 # turn 180 deg: front stretch on top
area = sum(rot[k][0] * rot[(k + 1) % M][1] - rot[(k + 1) % M][0] * rot[k][1] for k in range(M)) / 2
assert area > 0, "expected counter-clockwise travel"
xs = [p[0] for p in rot]; zs = [p[1] for p in rot]
mx, mz = (max(xs) + min(xs)) / 2, (max(zs) + min(zs)) / 2           # centre the bounding box
rot = [(x - mx, z - mz) for x, z in rot]
L = sum(math.dist(rot[k], rot[(k + 1) % M]) for k in range(M))

json.dump({
    "source": "MyChron 6, #72 Tricon (Elliott Ashe), Atoka 2026-09-26, " + os.path.basename(XRK)
              + "; mean of laps " + ", ".join(f"{ms / 1000:.3f}" for _, ms, _ in fast),
    "units": "metres; x = long axis, z = across (front stretch +z), counter-clockwise, sample 0 = start/finish",
    "lapLengthsM": [round(x, 2) for x in lens], "meanLineLengthM": round(L, 2), "maxLapSpreadM": round(spread, 2),
    "bearingOfXDeg": round((90 - math.degrees(ang)) % 360, 1),
    "pointsM": [[round(x, 3), round(z, 3)] for x, z in rot],
}, open(DST, "w"), indent=1)
print(f"{len(fast)} fast laps {[ms / 1000 for _, ms, _ in fast]}; line {L / FT:.0f} ft; extent "
      f"{(max(p[0] for p in rot) - min(p[0] for p in rot)) / FT:.0f} x {(max(p[1] for p in rot) - min(p[1] for p in rot)) / FT:.0f} ft; "
      f"laps within {spread / FT:.1f} ft of the mean; start/finish at x {rot[0][0] / FT:+.1f} ft, z {rot[0][1] / FT:+.1f} ft")
