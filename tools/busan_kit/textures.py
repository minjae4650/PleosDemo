"""Textures made here: sign atlases (generic trade words, no business names), sandwich boards, wall panels, an AC unit, a gutter
grate, road signs, sand and a far facade tile. Fonts: Noto Sans KR (OFL)."""
import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


def font(size):
    return ImageFont.truetype('C:/Windows/Fonts/NotoSansKR-Bold.ttf', size)


H_WORDS = ['카페', '편의점', '횟집', '조개구이', '호텔', '약국', '부동산', '치킨', '노래방', '미용실', '분식', '돼지국밥', '베이커리', '펍', '서핑숍', '사진관',
           '꽃집', '안경', '마트', '해물탕', '밀면', '커피', '피자', '게스트하우스', '당구장', '네일', '세탁', '식당', '포차', '와인바', '아이스크림', '기념품']
V_WORDS = ['카페', '노래방', '호텔', '약국', '횟집', '치킨', '미용실', '당구장']
# the small second line of a sign: what the trade sells, never a name
SUB = {'카페': 'COFFEE · DESSERT', '편의점': '24H', '횟집': '활어회 · 물회', '조개구이': '구이 · 찜', '호텔': 'HOTEL', '약국': 'PHARMACY', '부동산': '매매 · 전세 · 월세',
       '치킨': 'CHICKEN & BEER', '노래방': '2F', '미용실': 'HAIR', '분식': '김밥 · 떡볶이', '돼지국밥': '수육 · 순대', '베이커리': 'BAKERY', '펍': 'PUB',
       '서핑숍': 'SURF', '사진관': 'PHOTO', '꽃집': 'FLOWER', '안경': '렌즈 · 선글라스', '마트': 'MART', '해물탕': '아구찜 · 탕', '밀면': '냉면 · 만두',
       '커피': 'TAKE OUT', '피자': 'PIZZA', '게스트하우스': 'GUESTHOUSE', '당구장': 'BILLIARDS', '네일': 'NAIL · 속눈썹', '세탁': '수선 · 드라이',
       '식당': '백반 · 찌개', '포차': '안주 일체', '와인바': 'WINE', '아이스크림': 'ICE CREAM', '기념품': 'GIFT SHOP'}
SIGN_COLOURS = [((24, 60, 120), (255, 255, 255)), ((250, 250, 245), (30, 30, 30)), ((190, 30, 35), (255, 255, 255)), ((30, 30, 32), (240, 200, 60)),
                ((240, 185, 30), (40, 30, 20)), ((20, 110, 80), (255, 255, 255)), ((235, 235, 230), (190, 40, 40)), ((70, 45, 30), (245, 235, 215)),
                ((250, 250, 250), (20, 90, 160)), ((225, 120, 30), (255, 255, 255)), ((55, 60, 70), (255, 255, 255)), ((130, 30, 90), (255, 240, 240))]


def make_textures(tex_dir):
    tex_dir = Path(tex_dir)
    tex_dir.mkdir(parents=True, exist_ok=True)
    rng = random.Random(4)
    # shop signs: 4 x 8 cells of 512 x 160. Generic trade words only, no business names.
    img = Image.new('RGB', (2048, 1280))
    d = ImageDraw.Draw(img)
    for i, word in enumerate(H_WORDS):
        x, y = (i % 4) * 512, (i // 4) * 160
        bg, fg = SIGN_COLOURS[(i * 5 + 1) % len(SIGN_COLOURS)]
        d.rectangle((x, y, x + 511, y + 159), fill=bg)
        d.rectangle((x + 5, y + 5, x + 506, y + 154), outline=tuple(int(c * 0.78) for c in bg), width=4)
        sub = SUB.get(word, '')
        layout = i % 3              # 0: centred with a line below, 1: a colour block on the left, 2: plain centred
        left = x + (150 if layout == 1 else 0)
        room = 512 - (150 if layout == 1 else 0)
        size = 84 if sub else 100
        while size > 30 and d.textlength(word, font=font(size)) > room - 70:
            size -= 6
        w = d.textlength(word, font=font(size))
        cy = y + (64 if sub else 80)
        d.text((left + room / 2 - w / 2, cy - size * 0.72), word, fill=fg, font=font(size))
        if layout == 1:
            d.rectangle((x + 14, y + 14, x + 136, y + 145), fill=fg)
            ch = word[0]
            cw = d.textlength(ch, font=font(88))
            d.text((x + 75 - cw / 2, y + 80 - 88 * 0.72), ch, fill=bg, font=font(88))
        if sub:
            small = ImageFont.truetype('C:/Windows/Fonts/NotoSansKR-Regular.ttf', 30)
            sw = d.textlength(sub, font=small)
            if layout == 0:
                d.line((left + room / 2 - 150, y + 112, left + room / 2 + 150, y + 112), fill=fg, width=2)
            d.text((left + room / 2 - sw / 2, y + 114), sub, fill=fg, font=small)
    img.save(tex_dir / 'signs.png')

    # sandwich boards on the sidewalk: 4 x 2 cells of 128 x 192
    img = Image.new('RGB', (512, 384))
    d = ImageDraw.Draw(img)
    for i, (text, bg, fg) in enumerate([('OPEN', (30, 32, 34), (240, 235, 220)), ('오늘의\n메뉴', (30, 60, 45), (245, 240, 225)), ('COFFEE', (240, 236, 226), (60, 40, 30)),
                                        ('런치\n세트', (190, 40, 40), (255, 255, 255)), ('SALE', (245, 205, 40), (30, 30, 30)), ('포장\n가능', (245, 245, 240), (20, 80, 150)),
                                        ('생맥주', (40, 40, 44), (245, 200, 70)), ('아침\n식사', (235, 230, 215), (40, 40, 40))]):
        x, y = (i % 4) * 128, (i // 4) * 192
        d.rectangle((x, y, x + 127, y + 191), fill=bg)
        d.rectangle((x + 5, y + 5, x + 122, y + 186), outline=fg, width=2)
        rows = text.split('\n')
        size = 44 if max(len(r) for r in rows) <= 3 else 30
        for k, r in enumerate(rows):
            w = d.textlength(r, font=font(size))
            d.text((x + 64 - w / 2, y + 96 - len(rows) * size * 0.62 + k * size * 1.2 - size * 0.1), r, fill=fg, font=font(size))
    img.save(tex_dir / 'boards.png')

    # wall panels 0.75 x 0.375 m in running bond (the UVs are 3 m per tile), with grooved joints
    n = 512
    alb = np.full((n, n), 244.0)
    hgt = np.ones((n, n))
    pr = np.random.default_rng(8)
    for r in range(8):
        off = 64 if r % 2 else 0
        for c in range(4):
            x0 = (c * 128 + off) % n
            xs = np.arange(x0, x0 + 128) % n
            alb[r * 64:(r + 1) * 64, xs] += pr.uniform(-7, 5)
        alb[r * 64:r * 64 + 1, :] = 214
        hgt[r * 64:r * 64 + 3, :] = 0
        for c in range(4):
            x0 = (c * 128 + off) % n
            alb[r * 64:(r + 1) * 64, x0:x0 + 1] = 214
            hgt[r * 64:(r + 1) * 64, np.arange(x0, x0 + 3) % n] = 0
    Image.fromarray(np.stack([alb] * 3, axis=2).clip(0, 255).astype(np.uint8)).save(tex_dir / 'panel.png')
    hb = (hgt + np.roll(hgt, 1, 0) + np.roll(hgt, -1, 0) + np.roll(hgt, 1, 1) + np.roll(hgt, -1, 1)) / 5.0
    gx, gy = (np.roll(hb, -1, 1) - np.roll(hb, 1, 1)) * 1.2, (np.roll(hb, -1, 0) - np.roll(hb, 1, 0)) * 1.2
    gz = 1.0 / np.sqrt(gx * gx + gy * gy + 1.0)
    Image.fromarray((np.stack([-gx * gz * 0.5 + 0.5, gy * gz * 0.5 + 0.5, gz * 0.5 + 0.5], axis=2) * 255).astype(np.uint8)).save(tex_dir / 'panel_nor.png')

    # an air conditioner's outdoor unit, seen from the front
    img = Image.new('RGB', (256, 192), (222, 222, 216))
    d = ImageDraw.Draw(img)
    d.rectangle((2, 2, 253, 189), outline=(170, 170, 165), width=3)
    d.ellipse((18, 22, 166, 170), fill=(60, 62, 64), outline=(150, 150, 146), width=4)
    for r in range(10, 74, 9):
        d.ellipse((92 - r, 96 - r, 92 + r, 96 + r), outline=(118, 120, 122), width=2)
    for k in range(8):
        a = k * math.pi / 4
        d.line((92, 96, 92 + 72 * math.cos(a), 96 + 72 * math.sin(a)), fill=(118, 120, 122), width=2)
    d.ellipse((80, 84, 104, 108), fill=(150, 150, 146))
    for yy in range(30, 165, 12):
        d.line((184, yy, 242, yy), fill=(175, 175, 170), width=3)
    img.save(tex_dir / 'ac.png')

    # a gutter grate
    img = Image.new('RGB', (128, 192), (58, 58, 60))
    d = ImageDraw.Draw(img)
    d.rectangle((1, 1, 126, 190), outline=(92, 92, 94), width=5)
    for yy in range(16, 180, 20):
        d.rectangle((16, yy, 111, yy + 9), fill=(14, 14, 15))
    img.save(tex_dir / 'grate.png')

    # road signs, four discs: keep right, 30, no parking, straight on
    img = Image.new('RGB', (512, 128), (120, 120, 120))
    d = ImageDraw.Draw(img)
    blue, red = (20, 80, 170), (205, 30, 35)
    d.ellipse((2, 2, 125, 125), fill=blue, outline=(240, 240, 240), width=4)
    d.polygon([(40, 36), (84, 80), (84, 54), (96, 54), (96, 100), (50, 100), (50, 88), (76, 88), (32, 44)], fill=(255, 255, 255))
    d.ellipse((130, 2, 253, 125), fill=(250, 250, 250), outline=red, width=14)
    w = d.textlength('30', font=font(62))
    d.text((192 - w / 2, 64 - 62 * 0.72), '30', fill=(20, 20, 20), font=font(62))
    d.ellipse((258, 2, 381, 125), fill=blue, outline=red, width=13)
    d.line((282, 26, 358, 102), fill=red, width=13)
    d.ellipse((386, 2, 509, 125), fill=blue, outline=(240, 240, 240), width=4)
    d.polygon([(448, 22), (478, 58), (458, 58), (458, 104), (438, 104), (438, 58), (418, 58)], fill=(255, 255, 255))
    img.save(tex_dir / 'roadsigns.png')
    img = Image.new('RGB', (1024, 512))
    d = ImageDraw.Draw(img)
    for i, word in enumerate(V_WORDS):
        x = i * 128
        bg, fg = SIGN_COLOURS[(i * 7 + 2) % len(SIGN_COLOURS)]
        d.rectangle((x, 0, x + 127, 511), fill=bg)
        d.rectangle((x + 5, 5, x + 122, 506), outline=tuple(int(c * 0.8) for c in bg), width=3)
        size = min(92, int(440 / len(word)))
        for k, ch in enumerate(word):
            w = d.textlength(ch, font=font(size))
            d.text((x + 64 - w / 2, 256 - len(word) * size * 0.62 + k * size * 1.24 - size * 0.2), ch, fill=fg, font=font(size))
    img.save(tex_dir / 'signs_v.png')

    def noise(n, beta, seed):
        f = np.fft.fft2(np.random.default_rng(seed).standard_normal((n, n)))
        k = np.sqrt(np.fft.fftfreq(n)[:, None] ** 2 + np.fft.fftfreq(n)[None, :] ** 2)
        k[0, 0] = 1.0
        o = np.real(np.fft.ifft2(f / k ** beta))
        return (o - o.min()) / (o.max() - o.min())
    n = 1024
    low, mid, fine = noise(n, 2.2, 1), noise(n, 1.2, 2), noise(n, 0.2, 3)
    shade = 0.80 + 0.22 * low + 0.10 * (mid - 0.5) + 0.16 * (fine - 0.5)
    rgb = np.stack([0.78 * shade, 0.69 * shade, 0.53 * shade], axis=2)
    Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).save(tex_dir / 'sand.png')
    h = 0.55 * mid + 0.35 * noise(n, 1.6, 5) + 0.10 * fine
    dx, dy = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 14.0, (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 14.0
    nz = 1.0 / np.sqrt(dx * dx + dy * dy + 1.0)
    nor = np.stack([(-dx * nz) * 0.5 + 0.5, (dy * nz) * 0.5 + 0.5, nz * 0.5 + 0.5], axis=2)
    Image.fromarray((np.clip(nor, 0, 1) * 255).astype(np.uint8)).save(tex_dir / 'sand_nor.png')

    # distant facades: 4 x 4 windows per tile, alpha = how glossy (the glass)
    img = Image.new('RGBA', (512, 512), (232, 230, 226, 30))
    d = ImageDraw.Draw(img)
    for r in range(4):
        d.rectangle((0, r * 128 + 118, 511, r * 128 + 127), fill=(205, 203, 198, 30))
        for c in range(4):
            v = rng.randint(-14, 22)
            d.rectangle((c * 128 + 16, r * 128 + 26, c * 128 + 111, r * 128 + 100), fill=(52 + v, 64 + v, 76 + v, 235))
            d.rectangle((c * 128 + 16, r * 128 + 26, c * 128 + 111, r * 128 + 100), outline=(150, 152, 155, 120), width=3)
            d.line((c * 128 + 63, r * 128 + 26, c * 128 + 63, r * 128 + 100), fill=(150, 152, 155, 120), width=3)
    img.save(tex_dir / 'windows.png')
