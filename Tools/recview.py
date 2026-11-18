"""Turn an M2Record folder into things to look at.
  python recview.py <recdir> sheet <t0> <t1> [cols]   -> contact sheet of captioned frames between t0..t1 s
  python recview.py <recdir> video [fps]              -> race.mp4 with captions (ffmpeg)
  python recview.py <recdir> crop <t> <x0> <y0> <x1> <y1> -> one frame cropped (pixels), 2x
"""
import csv, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

FFMPEG = r"C:\Users\frank\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg.Essentials_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe"

def font(sz):
    for f in ("arial.ttf", "segoeui.ttf"):
        try: return ImageFont.truetype(f, sz)
        except OSError: pass
    return ImageFont.load_default()

def rows(d):
    return list(csv.DictReader(open(os.path.join(d, "frames.csv"), encoding="utf-8")))

def captioned(d, r, scale=1.0):
    im = Image.open(os.path.join(d, f"f{int(r['frame']):05d}.jpg")).convert("RGB")
    if scale != 1.0: im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
    dr = ImageDraw.Draw(im); f = font(max(14, int(im.height * 0.03)))
    top = f"{float(r['t_s']):6.1f} s  {r['phase']}  lap {r['leader_laps']}  cautions {r['cautions']}" + ("  ONE TO GO" if r['one_to_go'] == "1" else "")
    ev = r["latest_event"][:110]
    for i, line in enumerate((top, ev)):
        y = 6 + i * int(f.size * 1.3)
        dr.rectangle([4, y - 2, 12 + dr.textlength(line, font=f), y + f.size + 4], fill=(0, 0, 0))
        dr.text((8, y), line, fill=(255, 255, 255), font=f)
    return im

def sheet(d, t0, t1, cols=3):
    rs = [r for r in rows(d) if t0 <= float(r["t_s"]) <= t1]
    ims = [captioned(d, r, 0.5) for r in rs]
    if not ims: print("no frames"); return
    w, h = ims[0].size; n = len(ims); rr = (n + cols - 1) // cols
    out = Image.new("RGB", (w * cols, h * rr), "black")
    for i, im in enumerate(ims): out.paste(im, ((i % cols) * w, (i // cols) * h))
    p = os.path.join(d, f"sheet_{t0:g}-{t1:g}.jpg"); out.save(p, quality=85); print(p)

def video(d, fps=8):
    cd = os.path.join(d, "cap"); os.makedirs(cd, exist_ok=True)
    for r in rows(d): captioned(d, r).save(os.path.join(cd, f"c{int(r['frame']):05d}.jpg"), quality=88)
    p = os.path.join(d, "race.mp4")
    subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-framerate", str(fps), "-i", os.path.join(cd, "c%05d.jpg"),
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "23", p], check=True)
    print(p)

def crop(d, t, box):
    r = min(rows(d), key=lambda r: abs(float(r["t_s"]) - t))
    im = Image.open(os.path.join(d, f"f{int(r['frame']):05d}.jpg")).crop(box)
    im = im.resize((im.width * 2, im.height * 2), Image.LANCZOS)
    p = os.path.join(d, f"crop_{float(r['t_s']):g}.jpg"); im.save(p, quality=90); print(p)

if __name__ == "__main__":
    d, cmd = sys.argv[1], sys.argv[2]
    if cmd == "sheet": sheet(d, float(sys.argv[3]), float(sys.argv[4]), int(sys.argv[5]) if len(sys.argv) > 5 else 3)
    elif cmd == "video": video(d, int(sys.argv[3]) if len(sys.argv) > 3 else 8)
    elif cmd == "crop": crop(d, float(sys.argv[3]), tuple(int(x) for x in sys.argv[4:8]))
