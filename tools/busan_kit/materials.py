"""Material settings for BusanGwangalliBlockout.cs, and the texture paths they name (Unity asset paths)."""
from .geo import ROOT

TEX = 'Assets/BusanEnvironment/Blockout/tex/'          # the textures made by textures.make_textures, as Unity sees them
TEX_DIR = ROOT / 'unity/InputProbe' / TEX               # the same folder on disk

PH = 'Assets/BusanEnvironment/GeneratedSlice/tex/ph/'
LOOK = 'Assets/_Parkside/Lookdev/Textures/'
KOR = 'Assets/ParkingLotEnvironment/KoreanAssets/'
ASPH, ASPH_N = LOOK + 'asphalt_03/asphalt_03_diff_2k.jpg', LOOK + 'asphalt_03/asphalt_03_nor_gl_2k.jpg'
PAVE, PAVE_N = PH + 'concrete_pavers_diff_2k.jpg', PH + 'concrete_pavers_nor_gl_2k.jpg'
CONC, CONC_N = PH + 'rough_concrete_diff_2k.jpg', PH + 'rough_concrete_nor_gl_2k.jpg'
SLAB, SLAB_N = LOOK + 'concrete_floor/concrete_floor_diff_2k.jpg', LOOK + 'concrete_floor/concrete_floor_nor_gl_2k.jpg'
GRASS, GRASS_N = KOR + 'grass_ground_diff_2k.jpg', KOR + 'grass_ground_nor_gl_2k.jpg'
WOOD, WOOD_N = LOOK + 'brown_planks_03/brown_planks_03_diff_1k.jpg', LOOK + 'brown_planks_03/brown_planks_03_nor_gl_1k.jpg'
SEA_N = 'Assets/BusanEnvironment/GeneratedSlice/tex/sea_normal.png'


def ground(tex='', nor='', tint=(1, 1, 1), tile=2.0, smooth=0.1, bump=1.0, vary=0.3, stain=0.2, along=True, shadows=False):
    return dict(kind='ground', tex=tex, nor=nor, tint=list(tint), tile=tile, smooth=smooth, metal=0.0, bump=bump, vary=vary, stain=stain,
                along=along, shadows=shadows, flipg=False, alpha=False, grime=0.0)


def surface(tex='', nor='', tint=(1, 1, 1), smooth=0.1, metal=0.0, bump=1.0, alpha=False, shadows=True, grime=0.0, emit=0.0):
    """grime: rain streaks and blotches from the shader, in world space (clean flat colour was what made the walls look fake)."""
    return dict(kind='surface', tex=tex, nor=nor, tint=list(tint), tile=1.0, smooth=smooth, metal=metal, bump=bump, vary=0.0, stain=0.0,
                along=False, shadows=shadows, flipg=True, alpha=alpha, grime=grime, emit=emit)


# The order of this dict is the order of the meshes in the output.
MAT = {
    # the ground: world-mapped, laid along the road direction
    'asphalt': ground(ASPH, ASPH_N, (0.62, 0.62, 0.64), 4.0, 0.16, 1.0, 0.45, 0.35),
    'asphalt_alley': ground(ASPH, ASPH_N, (0.72, 0.71, 0.70), 3.2, 0.12, 1.0, 0.55, 0.5),
    'gutter': ground('', SLAB_N, (0.47, 0.47, 0.45), 3.0, 0.12, 0.7, 0.45, 0.8),      # the concrete gutter: grey (tinted asphalt read as a strip of dirt)
    'sidewalk': ground(PAVE, PAVE_N, (0.92, 0.91, 0.89), 1.8, 0.10, 1.0, 0.25, 0.25),
    'promenade': ground(PAVE, PAVE_N, (0.86, 0.60, 0.50), 1.8, 0.10, 1.0, 0.4, 0.3),      # weathered red-brown block, not orange
    'granite': ground(PAVE, PAVE_N, (1.02, 1.02, 1.0), 2.6, 0.16, 0.8, 0.2, 0.15),
    'tactile': ground(PAVE, PAVE_N, (1.25, 0.95, 0.20), 0.6, 0.08, 1.4, 0.1, 0.1),
    'yard': ground(ASPH, ASPH_N, (0.95, 0.95, 0.93), 5.0, 0.08, 0.7, 0.5, 0.5),
    'sand': ground(TEX + 'sand.png', TEX + 'sand_nor.png', (1.0, 0.97, 0.92), 3.0, 0.04, 1.0, 0.25, 0.0, along=False),
    'grass': ground(GRASS, GRASS_N, (0.85, 0.95, 0.75), 2.5, 0.05, 1.0, 0.3, 0.0),
    'hill': ground(GRASS, GRASS_N, (0.42, 0.50, 0.38), 70.0, 0.02, 0.0, 0.6, 0.0, along=False),
    'paint_white': ground('', ASPH_N, (0.80, 0.80, 0.78), 4.0, 0.22, 0.6, 0.2, 0.5),
    'paint_yellow': ground('', ASPH_N, (0.82, 0.62, 0.10), 4.0, 0.22, 0.6, 0.2, 0.5),
    'paint_red': ground('', ASPH_N, (0.40, 0.14, 0.11), 4.0, 0.18, 0.9, 0.35, 0.5),
    'sea': ground('', SEA_N, (0.035, 0.14, 0.19), 26.0, 0.90, 0.55, 0.0, 0.0, along=False),
    # things with their own UVs (metres), tinted per vertex
    'stone': surface('', SLAB_N, (0.74, 0.74, 0.72), 0.16, bump=0.6, grime=0.5),          # grey granite; the scanned concretes are tan or green
    # three wall finishes; the colour comes per building, from the vertices
    'wall': surface('', SLAB_N, (1.0, 1.0, 1.0), 0.10, bump=0.3, grime=0.8),                                   # painted render
    'wall_tile': surface(TEX + 'panel.png', TEX + 'panel_nor.png', (1.0, 1.0, 1.0), 0.28, bump=1.0, grime=0.6),   # stone or tile panels
    'wall_brick': surface(KOR + 'red_brick_diff_2k.jpg', SLAB_N, (1.25, 1.2, 1.15), 0.08, bump=0.5, grime=0.5),
    'glass': surface('', '', (0.50, 0.55, 0.60), 0.96, 0.88),      # tinted, mirror-like: daylight glass shows the sky and the street
    'frame': surface('', '', (1, 1, 1), 0.55, 0.6),
    'signs': surface(TEX + 'signs.png', '', (1, 1, 1), 0.30, grime=0.2),
    'signs_v': surface(TEX + 'signs_v.png', '', (1, 1, 1), 0.30, grime=0.2),
    'boards': surface(TEX + 'boards.png', '', (1, 1, 1), 0.20, grime=0.2),
    'ac': surface(TEX + 'ac.png', '', (1, 1, 1), 0.30, grime=0.5),
    'grate': surface(TEX + 'grate.png', '', (1, 1, 1), 0.40, 0.5, shadows=False),
    'roadsigns': surface(TEX + 'roadsigns.png', '', (1, 1, 1), 0.45),
    'fabric': surface('', '', (1, 1, 1), 0.04, grime=0.3),
    'wood': surface(WOOD, WOOD_N, (1.0, 0.95, 0.9), 0.25),
    'metal': surface('', '', (0.24, 0.26, 0.28), 0.50, 0.4),        # dark grey painted steel (near-black metal read as a silhouette)
    'painted': surface('', '', (1, 1, 1), 0.40, 0.2, grime=0.3),          # painted steel: poles, pipes, cabinets
    'iron': surface('', CONC_N, (0.20, 0.20, 0.21), 0.35, 0.6),
    'windows': surface(TEX + 'windows.png', '', (1, 1, 1), 0.85, 0.1, alpha=True, shadows=False, grime=0.3),
    'roof': surface(CONC, CONC_N, (0.80, 0.80, 0.78), 0.06, shadows=False),
    'promenade2': ground(PAVE, PAVE_N, (0.80, 0.80, 0.79), 1.8, 0.10, 1.0, 0.3, 0.25),
    'asphalt_patch': ground(ASPH, ASPH_N, (0.545, 0.545, 0.565), 3.1, 0.20, 1.0, 0.4, 0.3),   # newer, darker asphalt: trench repairs
    'bridge': surface('', '', (0.80, 0.81, 0.82), 0.2, shadows=False),
    'landmark': surface('', '', (1, 1, 1), 0.55, 0.0, grime=0.05, emit=0.4),      # the lettering: glossy paint, lit from inside a little
    # the lamps of the traffic signals, one material each: SignalLights.cs switches them on and off in play (all signals keep
    # one phase); as built they show a green signal and a red man
    'sig_red': surface('', '', (0.28, 0.05, 0.04), 0.3, shadows=False),
    'sig_yellow': surface('', '', (0.30, 0.21, 0.04), 0.3, shadows=False),
    'sig_green': surface('', '', (0.10, 1.0, 0.55), 0.3, emit=1.0, shadows=False),
    'walk_red': surface('', '', (1.0, 0.10, 0.07), 0.3, emit=1.0, shadows=False),
    'walk_green': surface('', '', (0.03, 0.20, 0.10), 0.3, shadows=False),
}

# Which mesh file of a district a material's cells go to (output.district): 'far' for what is only seen from a distance, 'near'
# for the rest. Kept out of MAT so the legacy JSON stays as it was. The far land (terrain.far_land) draws with 'yard' and
# 'stone', which the street uses up close as well, so those two stay near.
FAR = ('windows', 'hill', 'sea', 'bridge')
LAYER = {k: 'far' if k in FAR else 'near' for k in MAT}
