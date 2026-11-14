"""Builds one district of the Busan landmark map with busan_kit.

    python tools/build_district.py <gwangalli|ring> [--out <dir>] [--legacy-json <out.json>] [--tex-dir <dir>]

--out: where district.json and district.bin go (busan_kit.output.district; read by BusanDistrictBuilder.cs, bridge command
`district <name> [dir]`). Default C:\\c\\busan-out\\<name>: outside %LOCALAPPDATA%, because when this runs inside a packaged (MSIX)
app Windows redirects what it writes there to a folder the Unity editor does not see. Written unless only --legacy-json is given.
--legacy-json: the single JSON that BusanGwangalliBlockout.cs reads (the format of tools/build-gwangalli-blockout.py).
--tex-dir: where the generated textures are written. The outputs name them by their Unity path (busan_kit.materials.TEX), so for
Unity this has to be that folder, which is the default.
Run with the venv that has shapely, mapbox_earcut, matplotlib and PIL."""
import argparse
import importlib
import inspect
import json
from pathlib import Path

from shapely.geometry import LineString, MultiLineString, mapping

from busan_kit import frontage, mesh, output, street
from busan_kit.geo import DATA
from busan_kit.materials import TEX_DIR

DISTRICTS = ('gwangalli', 'ring', 'bridge', 'haeundae', 'seomyeon', 'infill', 'trees')
OUT_ROOT = Path(r'C:\c\busan-out')          # the same folder as BusanDistrictBuilder.DefaultDir


def default_out(name):
    return OUT_ROOT / name


def driven():
    """The street-level parts of both routes' lane lines, as lines in (x, z)."""
    net = json.loads((DATA / 'LandmarkNetwork.json').read_text(encoding='utf-8'))
    parts = []
    for route in net['routes']:
        run = []
        for p in route['path'] + route['path'][:1]:
            if p[1] > 4.0 + 1e-3:
                if len(run) > 1:
                    parts.append(LineString(run))
                run = []
            else:
                run.append((p[0], p[2]))
        if len(run) > 1:
            parts.append(LineString(run))
    return MultiLineString(parts)


def signals():
    """Every district's zebra crossings in one file the scene builder reads (LoopTraffic stops its cars before them on red)."""
    zs = []
    for name in DISTRICTS:
        f = default_out(name) / 'footprints.json'
        if f.exists():
            zs += json.loads(f.read_text(encoding='utf-8')).get('zebras', [])
    cols = list(zip(*zs)) if zs else [[], [], [], [], []]
    out = {k: [float(v) for v in c] for k, c in zip(('x', 'z', 'ux', 'uz', 'half'), cols)}
    (DATA / 'LandmarkSignals.json').write_text(json.dumps(out), encoding='utf-8')


def main():
    ap = argparse.ArgumentParser(description='Build one district of the Busan landmark map.')
    ap.add_argument('name', choices=DISTRICTS)
    ap.add_argument('--out', type=Path, help='write district.json and district.bin here (default: %s\\<name>)' % OUT_ROOT)
    ap.add_argument('--legacy-json', type=Path, help='write the single-file JSON for BusanGwangalliBlockout.cs here')
    ap.add_argument('--tex-dir', type=Path, default=TEX_DIR, help='where the generated textures go (default: %(default)s)')
    args = ap.parse_args()
    district = importlib.import_module('districts.' + args.name)
    if args.legacy_json:
        kit, scene = district.build(args.tex_dir)
        output.legacy_json(args.legacy_json, kit, scene)
        output.report(args.legacy_json, kit)
    if args.out or not args.legacy_json:
        # the map version of a district that also has a stand-alone (legacy) form: clipped to its own ground, driven the map's way
        kw = {'legacy': False} if 'legacy' in inspect.signature(district.build).parameters else {}
        frontage.DRIVEN, frontage.PLACED[:], mesh.POSTS[:], street.ZEBRAS[:] = driven(), [], [], []
        kit, scene = district.build(args.tex_dir, **kw)
        out = args.out or default_out(args.name)
        st = output.district(out, args.name, kit, scene)
        # what this district stands on, for districts/infill.py (ring.py also leaves its open ground as kit.land)
        (out / 'footprints.json').write_text(json.dumps({'placed': [mapping(p) for p in frontage.PLACED if not p.is_empty],
                                                       'land': mapping(kit.land) if getattr(kit, 'land', None) is not None else None,
                                                       'treeline': mapping(kit.treeline) if getattr(kit, 'treeline', None) is not None else None,
                                                       'forest': getattr(kit, 'forest', None),
                                                       'posts': [[round(x, 2), round(z, 2)] for x, z in mesh.POSTS],
                                                       'zebras': [list(zb) for zb in street.ZEBRAS]}), encoding='utf-8')
        signals()
        size = (out / 'district.bin').stat().st_size + (out / 'district.json').stat().st_size
        print('district %s -> %s: cells %d, triangles %d, vertices %d (source %d), %s, colliders %s, MB %.1f'
              % (args.name, out, st['cells'], st['triangles'], st['vertices'], st['sourceVertices'],
                 ', '.join('%s %d cells %d tris' % (k, L['cells'], L['triangles']) for k, L in sorted(st['layers'].items())),
                 ', '.join('%s %d tris' % kv for kv in st['colliders'].items()) or 'none', size / 1048576))


if __name__ == '__main__':
    main()
