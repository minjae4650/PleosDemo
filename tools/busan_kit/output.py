"""Writing a district out."""
import json
from pathlib import Path

import numpy as np

from .materials import LAYER

CELL = 128.0          # the side of the square cells the meshes are split into, metres
# per layer: the far layer (hills, window boxes, the sea) spreads thin over kilometres, and 128 m cells made thousands of renderers
# of a few triangles each (3,545 in Gwangalli); 512 m cells are still culled on their own
CELLS = {'near': 256.0, 'far': 512.0}
# a material's settings as BusanDistrictBuilder.cs (and BusanGwangalliBlockout.cs) reads them, with the value for a missing one
SPEC = (('kind', 'surface'), ('tex', ''), ('nor', ''), ('tint', [1.0, 1.0, 1.0]), ('tile', 1.0), ('smooth', 0.0), ('metal', 0.0), ('bump', 0.0),
        ('vary', 0.0), ('stain', 0.0), ('along', False), ('shadows', True), ('flipg', False), ('alpha', False), ('grime', 0.0), ('emit', 0.0))


def legacy_json(path, kit, scene):
    """The single JSON that BusanGwangalliBlockout.cs reads: material settings and geometry per non-empty mesh, cars, views.
    `scene` holds note, heading, sunYaw, sunPitch, probe, cars and views; the key order is the file's."""
    out = {'note': scene['note'], 'heading': scene['heading'], 'sunYaw': scene['sunYaw'], 'sunPitch': scene['sunPitch'], 'probe': scene['probe'],
           'meshes': [dict(kit.mats[k], name=k, v=m.v, uv=m.uv, c=m.c, t=m.t) for k, m in kit.M.items() if m.t],
           'cars': scene['cars'], 'views': scene['views']}
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(out, separators=(',', ':')), encoding='utf-8')


def cells(mesh, size=CELL):
    """The triangles of `mesh` split into `size` m square cells by their centroid (x, z), cells in (ix, iz) order. Yields (ix, iz),
    the mesh's vertex indices the cell uses (in their original order) and the cell's triangles (in their original order) indexing
    into those. Vertices stay shared inside a cell (round posts are smooth because their rings share vertices); a vertex used in two
    cells is written once for each."""
    t = np.asarray(mesh.t, dtype=np.int64).reshape(-1, 3)
    v = np.asarray(mesh.v, dtype=np.float64).reshape(-1, 3)
    ix = np.floor((v[t[:, 0], 0] + v[t[:, 1], 0] + v[t[:, 2], 0]) / 3.0 / size).astype(np.int64)
    iz = np.floor((v[t[:, 0], 2] + v[t[:, 1], 2] + v[t[:, 2], 2]) / 3.0 / size).astype(np.int64)
    key = (ix + (1 << 20)) * (1 << 21) + (iz + (1 << 20))
    order = np.argsort(key, kind='stable')                 # stable: the triangles of a cell keep their order
    key = key[order]
    starts = np.flatnonzero(np.r_[True, key[1:] != key[:-1]])
    ends = np.r_[starts[1:], len(key)]
    for a, b in zip(starts, ends):
        tri = t[order[a:b]]
        used, local = np.unique(tri.ravel(), return_inverse=True)
        yield (int(ix[order[a]]), int(iz[order[a]])), used, local.reshape(-1, 3)


def district(out_dir, name, kit, scene, layers=LAYER, sizes=None):
    """district.json and district.bin in `out_dir`, for BusanDistrictBuilder.cs. Returns the stats.

    district.bin, little-endian, one record per (material, cell) one after the other: positions float32 x3 per vertex, then for
    surface materials uv float32 x2 and colour float32 x3 per vertex, then the triangles as uint32 x3; after the cells one record per
    collider: positions float32 x3 per vertex, then the triangles as uint32 x3.
    district.json: name, bin (the file name), heading (radians, as in the legacy JSON), sun {yaw, pitch} (degrees), materials (key,
    layer and the settings in SPEC; only materials with triangles, in kit order), cells (mat, layer, cell [ix, iz] in units of the
    layer's cell size `sizes`, nv vertices, nt triangles, and the byte offsets of v, uv, c and t in the bin, -1 for none), cars,
    views, probes (name, pos, and when the district gives them box [cx, cy, cz, sx, sy, sz] in world axes and importance), stats,
    and when scene['colliders'] (name -> Mesh) is given, colliders (name, nv, nt and the byte offsets of v and t), in its order."""
    sizes = dict(CELLS, **(sizes or {}))
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    materials, records, chunks = [], [], []
    offset = 0
    stats = {'cellSize': sizes, 'cells': 0, 'triangles': 0, 'vertices': 0, 'sourceVertices': 0, 'binBytes': 0, 'layers': {}, 'materials': {},
             'colliders': {}}

    def put(arr):
        nonlocal offset
        b = np.ascontiguousarray(arr).tobytes()
        chunks.append(b)
        at = offset
        offset += len(b)
        return at

    for key, mesh in kit.M.items():
        if not mesh.t:
            continue
        spec = kit.mats[key]
        layer = layers.get(key, 'near')
        materials.append(dict({'key': key, 'layer': layer}, **{f: spec.get(f, d) for f, d in SPEC}))
        v = np.asarray(mesh.v, dtype=np.float64).reshape(-1, 3)
        uv = np.asarray(mesh.uv, dtype=np.float64).reshape(-1, 2) if mesh.has_uv else None
        c = np.asarray(mesh.c, dtype=np.float64).reshape(-1, 3) if mesh.has_uv else None
        mat_tri = 0
        for (ix, iz), used, tri in cells(mesh, sizes.get(layer, CELL)):
            rec = {'mat': key, 'layer': layer, 'cell': [ix, iz], 'nv': int(len(used)), 'nt': int(len(tri))}
            rec['v'] = put(v[used].astype('<f4'))
            rec['uv'] = put(uv[used].astype('<f4')) if uv is not None else -1
            rec['c'] = put(c[used].astype('<f4')) if c is not None else -1
            rec['t'] = put(tri.astype('<u4'))
            records.append(rec)
            L = stats['layers'].setdefault(layer, {'cells': 0, 'triangles': 0, 'vertices': 0})
            L['cells'] += 1
            L['triangles'] += rec['nt']
            L['vertices'] += rec['nv']
            mat_tri += rec['nt']
        stats['materials'][key] = mat_tri
        stats['sourceVertices'] += len(v)
    stats['cells'] = len(records)
    stats['triangles'] = sum(L['triangles'] for L in stats['layers'].values())
    stats['vertices'] = sum(L['vertices'] for L in stats['layers'].values())

    # the surfaces the car drives on (no renderer; BusanDistrictBuilder puts them on the road layer)
    colliders = []
    for cname, mesh in (scene.get('colliders') or {}).items():
        if not mesh.t:
            continue
        v = np.asarray(mesh.v, dtype=np.float64).reshape(-1, 3)
        t = np.asarray(mesh.t, dtype=np.int64).reshape(-1, 3)
        colliders.append({'name': cname, 'nv': int(len(v)), 'nt': int(len(t)), 'v': put(v.astype('<f4')), 't': put(t.astype('<u4'))})
        stats['colliders'][cname] = int(len(t))
    stats['binBytes'] = offset

    probes = scene.get('probes') or [{'name': 'probe', 'pos': scene['probe']}]
    out = {'name': name, 'bin': 'district.bin', 'heading': scene['heading'], 'sun': {'yaw': scene['sunYaw'], 'pitch': scene['sunPitch']},
           'note': scene.get('note', ''), 'materials': materials, 'cells': records, 'cars': scene['cars'], 'views': scene['views'],
           'probes': probes, 'stats': stats}
    if scene.get('colliders'):
        out['colliders'] = colliders
    (out_dir / 'district.bin').write_bytes(b''.join(chunks))
    (out_dir / 'district.json').write_text(json.dumps(out, separators=(',', ':'), ensure_ascii=False), encoding='utf-8')
    return stats


def report(path, kit):
    tri = {k: len(m.t) // 3 for k, m in kit.M.items() if m.t}
    print('triangles', sum(tri.values()), dict(sorted(tri.items(), key=lambda kv: -kv[1])[:8]), 'MB %.1f' % (Path(path).stat().st_size / 1048576))
