"""Looks over rendered views before a person (or a model) does: contact sheets, a comparison of two runs, and a scan for the
faults that have actually happened on this map (plan.md 6.2 and 6.6). Numbers only sort the pictures; the verdict is by eye.

  python tools/check_frames.py sheet <dir> <out_dir> [--cols 6 --rows 4 --width 320]
  python tools/check_frames.py diff <dir_a> <dir_b>            mean difference per view (0-255), largest first
  python tools/check_frames.py scan <dir>                      over-saturated, near-black and sky-coloured-below-the-road shares

`scan` also reads the special passes when they are there: <name>_hole.png (background painted magenta, fog off: any magenta
below the horizon line is a hole in the ground) and <name>_id.png (materials in flat colours)."""
import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

FONT = r'C:\Windows\Fonts\NotoSansKR-Bold.ttf'


def pictures(folder):
    return sorted(p for p in Path(folder).glob('*.png') if not p.stem.endswith(('_hole', '_id')) and not p.stem.startswith('_'))


def sheet(folder, out_dir, cols, rows, width):
    files = pictures(folder)
    if not files:
        print('no pictures in', folder)
        return 1
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    font = ImageFont.truetype(FONT, 13)
    first = Image.open(files[0])
    height = round(width * first.height / first.width)
    per = cols * rows
    for k in range(0, len(files), per):
        page = Image.new('RGB', (cols * width, rows * (height + 18)), (24, 24, 24))
        d = ImageDraw.Draw(page)
        for i, f in enumerate(files[k:k + per]):
            x, y = (i % cols) * width, (i // cols) * (height + 18)
            page.paste(Image.open(f).convert('RGB').resize((width, height), Image.LANCZOS), (x, y + 18))
            d.text((x + 3, y + 1), f.stem, font=font, fill=(235, 235, 235))
        name = out_dir / ('sheet_%02d.jpg' % (k // per + 1))
        page.save(name, quality=88)
        print(name, min(per, len(files) - k), 'views')
    return 0


def diff(dir_a, dir_b):
    rows = []
    for f in pictures(dir_a):
        g = Path(dir_b) / f.name
        if not g.exists():
            rows.append((999.0, 999.0, f.stem + ' (missing in the second folder)'))
            continue
        a, b = np.asarray(Image.open(f).convert('RGB'), dtype=np.float32), np.asarray(Image.open(g).convert('RGB'), dtype=np.float32)
        if a.shape != b.shape:
            rows.append((999.0, 999.0, f.stem + ' (sizes differ)'))
            continue
        d = np.abs(a - b).mean(axis=2)
        h, w = d.shape[0] // 16 * 16, d.shape[1] // 16 * 16
        blocks = d[:h, :w].reshape(h // 16, 16, w // 16, 16).mean(axis=(1, 3))
        rows.append((float(d.mean()), float(blocks.max()), f.stem))
    for mean, worst, name in sorted(rows, reverse=True):
        print('%7.2f mean  %7.2f worst 16x16 block  %s' % (mean, worst, name))
    return 0


def scan(folder):
    flagged = 0
    for f in pictures(folder):
        a = np.asarray(Image.open(f).convert('RGB'), dtype=np.float32) / 255.0
        mx, mn = a.max(axis=2), a.min(axis=2)
        sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
        loud = float(((sat > 0.85) & (mx > 0.6)).mean())                 # strong pure colour (pink shop bases, an electric-blue bonnet)
        black = float((mx < 0.03).mean())                                # glass, sea and poles with nothing to reflect
        notes = []
        if loud > 0.03:
            notes.append('strong colour %.1f%%' % (loud * 100))
        if black > 0.04:
            notes.append('near black %.1f%%' % (black * 100))
        hole = f.with_name(f.stem + '_hole.png')
        if hole.exists():
            h = np.asarray(Image.open(hole).convert('RGB'), dtype=np.float32) / 255.0
            magenta = (h[:, :, 0] > 0.9) & (h[:, :, 1] < 0.1) & (h[:, :, 2] > 0.9)
            rows = np.where(magenta.any(axis=1))[0]
            sky_rows = magenta.mean(axis=1) > 0.5                        # rows that are mostly background: the sky
            horizon = int(np.where(sky_rows)[0].max()) if sky_rows.any() else 0
            below = float(magenta[horizon + 8:].mean()) if horizon + 8 < magenta.shape[0] else 0.0
            if below > 0.0005:
                notes.append('background shows below the horizon %.2f%%' % (below * 100))
        if notes:
            flagged += 1
            print('%-28s %s' % (f.stem, '; '.join(notes)))
    print('%d of %d views flagged' % (flagged, len(pictures(folder))))
    return 0


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='cmd', required=True)
    s = sub.add_parser('sheet')
    s.add_argument('dir')
    s.add_argument('out_dir')
    s.add_argument('--cols', type=int, default=6)
    s.add_argument('--rows', type=int, default=4)
    s.add_argument('--width', type=int, default=320)
    d = sub.add_parser('diff')
    d.add_argument('dir_a')
    d.add_argument('dir_b')
    c = sub.add_parser('scan')
    c.add_argument('dir')
    a = ap.parse_args()
    if a.cmd == 'sheet':
        return sheet(a.dir, a.out_dir, a.cols, a.rows, a.width)
    if a.cmd == 'diff':
        return diff(a.dir_a, a.dir_b)
    return scan(a.dir)


if __name__ == '__main__':
    sys.exit(main())
