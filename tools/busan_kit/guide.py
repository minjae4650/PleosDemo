"""Road guide boards (the green and blue signs over a road): one texture with a board per cell, and the board itself.

Only public place names go on them (광안대교, 광안리, 서면, 해운대 ...), never a business name."""
from PIL import Image, ImageDraw, ImageFont

from .materials import TEX, surface

FONT = r'C:\Windows\Fonts\NotoSansKR-Bold.ttf'
COLS, ROWS, CW, CH = 2, 8, 768, 256                      # a board is three times as wide as it is high
GREEN, BLUE, BROWN = (12, 108, 70), (22, 78, 150), (110, 70, 36)
# (key, colour, big line, small line)
BOARDS = (
    ('bridge_ahead', GREEN, '광안대교 · 서면 ↑', 'Gwangandaegyo · Seomyeon'),
    ('gwangalli_right', GREEN, '광안리 →', 'Gwangalli Beach'),
    ('bridge', GREEN, '광안대교', 'Gwangandaegyo Bridge'),
    ('seomyeon_ahead', GREEN, '서면 ↑', 'Seomyeon'),
    ('haeundae_ahead', GREEN, '해운대 ↑', 'Haeundae'),
    ('tunnel', GREEN, '터널', 'Tunnel'),
    ('seomyeon_station', BLUE, '서면역', 'Seomyeon Stn.'),
    ('gwangalli_beach', BROWN, '광안리해수욕장', 'Gwangalli Beach'),
    ('haeundae', BLUE, '해운대', 'Haeundae'),
    ('seomyeon', BLUE, '서면', 'Seomyeon'),
    ('seomyeon_right', GREEN, '서면 →', 'Seomyeon'),
    ('haeundae_right', GREEN, '해운대 →', 'Haeundae'),
    ('gwangalli_ahead', GREEN, '광안리 ↑', 'Gwangalli Beach'),
    ('bridge_left_lanes', GREEN, '↑ 광안대교', 'Gwangandaegyo'),
    ('gwangalli_right_lanes', GREEN, '광안리 ↗', 'Gwangalli Beach'),
    ('blank', GREEN, '', ''),
)
MATERIAL = {'guide': surface(TEX + 'guide.png', '', (1, 1, 1), 0.35, grime=0.1)}


def make_guide(tex_dir):
    sheet = Image.new('RGB', (COLS * CW, ROWS * CH), (40, 40, 40))
    d = ImageDraw.Draw(sheet)
    for i, (_, colour, big, small) in enumerate(BOARDS):
        x, y = (i % COLS) * CW, (i // COLS) * CH
        d.rounded_rectangle((x + 6, y + 6, x + CW - 6, y + CH - 6), 22, fill=colour, outline=(245, 245, 245), width=7)
        size = 120
        while size > 40:
            f = ImageFont.truetype(FONT, size)
            if d.textlength(big, font=f) <= CW - 70:
                break
            size -= 6
        f = ImageFont.truetype(FONT, size)
        d.text((x + CW / 2, y + CH * 0.42), big, font=f, fill=(250, 250, 250), anchor='mm')
        d.text((x + CW / 2, y + CH * 0.80), small, font=ImageFont.truetype(FONT, 38), fill=(240, 240, 240), anchor='mm')
    sheet.save(tex_dir / 'guide.png')


def board_uv(key):
    i = next(k for k, b in enumerate(BOARDS) if b[0] == key)
    c, r = i % COLS, i // COLS
    u0, u1, v0, v1 = c / COLS, (c + 1) / COLS, 1 - (r + 1) / ROWS, 1 - r / ROWS
    return ((u0, v0), (u1, v0), (u1, v1), (u0, v1))


def board(kit, key, centre, right, width=6.0, height=2.0, back=(0.42, 0.44, 0.46)):
    """A board facing the viewer for whom `right` (x, z) points right; `centre` is its middle (x, y, z). The back is plain."""
    cx, cy, cz = centre
    rx, rz = right
    nx, nz = rz, -rx                                         # toward the viewer (who looks along -n and has `right` on the right)
    hw, hh = width / 2, height / 2
    p = [(cx - rx * hw, cy - hh, cz - rz * hw), (cx + rx * hw, cy - hh, cz + rz * hw), (cx + rx * hw, cy + hh, cz + rz * hw), (cx - rx * hw, cy + hh, cz - rz * hw)]
    kit.M['guide'].quad(p[0], p[1], p[2], p[3], (nx, 0.0, nz), board_uv(key))
    q = [(x - nx * 0.06, y, z - nz * 0.06) for x, y, z in p]
    kit.M['painted'].quad(q[0], q[1], q[2], q[3], (-nx, 0.0, -nz), col=back)
