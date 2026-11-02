"""数组列车道具：车头、车厢、车钩、铁轨。

沿用 tools/gen_art.py 的 16 色色板与像素画法，产物同时落到
Assets/Art/Props（原稿）和 Assets/Resources/Props（运行时加载）。
用法：python tools/gen_array_props.py
"""
from __future__ import annotations
import shutil
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'Assets/Art/Props'
RES = ROOT / 'Assets/Resources/Props'

PALETTE = ['#FFFDF7', '#F2EAD8', '#FFFFFF', '#EAF7F8', '#B9E8DD', '#76C9D5',
           '#4A9EAD', '#2E6B77', '#38515B', '#F6B45C', '#D98E3F', '#B99AD9',
           '#8E6FB8', '#F4D878', '#F29B8F', '#8BD6A8']
P = [tuple(bytes.fromhex(x[1:])) + (255,) for x in PALETTE]

CREAM, SAND, WHITE, FOAM, MINT, WHALE, WHALE_MID, DEEP, INK = 0, 1, 2, 3, 4, 5, 6, 7, 8
HONEY, AMBER, GRAPE_L, GRAPE, YELLOW, CORAL, MINT_D = 9, 10, 11, 12, 13, 14, 15


def canvas(w, h):
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)


def wheel(d, cx, cy, r, hub=CREAM):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=P[DEEP], outline=P[INK])
    d.point((cx, cy), fill=P[hub])


def loco():
    """48x32 小火车头：鲸鱼蓝锅炉 + 蜜糖橙驾驶室 + 烟囱冒汽，货面上写数组名。"""
    im, d = canvas(48, 32)
    # 底盘
    d.rectangle((2, 22, 45, 25), fill=P[INK])
    d.rectangle((3, 22, 44, 24), fill=P[DEEP])
    # 锅炉
    d.rectangle((6, 13, 28, 22), fill=P[WHALE], outline=P[INK])
    d.rectangle((7, 14, 27, 15), fill=P[WHALE_MID])
    # 烟囱与蒸汽
    d.rectangle((9, 7, 14, 13), fill=P[DEEP], outline=P[INK])
    d.rectangle((8, 5, 15, 7), fill=P[INK])
    d.ellipse((15, 2, 19, 6), fill=P[FOAM])
    d.ellipse((19, 0, 22, 3), fill=P[CREAM])
    # 驾驶室 + 顶棚
    d.rectangle((29, 8, 44, 22), fill=P[HONEY], outline=P[INK])
    d.rectangle((31, 10, 36, 15), fill=P[FOAM], outline=P[INK])
    d.rectangle((27, 5, 46, 8), fill=P[AMBER], outline=P[INK])
    # 名牌：写数组名的浅色条
    d.rectangle((8, 17, 26, 21), fill=P[CREAM], outline=P[INK])
    # 前照灯与排障器
    d.rectangle((3, 16, 6, 20), fill=P[YELLOW], outline=P[INK])
    d.polygon([(2, 25), (6, 25), (4, 29)], fill=P[AMBER], outline=P[INK])
    wheel(d, 12, 26, 3)
    wheel(d, 22, 26, 3)
    wheel(d, 37, 26, 4)
    return im


def car():
    """32x32 敞篷货车厢：木框 + 浅色货面（写值），底部两个轮子压在铁轨上。"""
    im, d = canvas(32, 32)
    # 车体木框
    d.rectangle((2, 7, 29, 24), fill=P[HONEY], outline=P[INK])
    d.rectangle((3, 21, 28, 23), fill=P[AMBER])
    for x in (5, 26):
        d.rectangle((x, 8, x + 1, 23), fill=P[AMBER])
    # 货面：值写在这块浅色区里
    d.rectangle((6, 9, 25, 20), fill=P[CREAM], outline=P[INK])
    d.rectangle((7, 10, 24, 10), fill=P[SAND])
    # 两端车钩座
    d.rectangle((0, 17, 2, 20), fill=P[DEEP], outline=P[INK])
    d.rectangle((29, 17, 31, 20), fill=P[DEEP], outline=P[INK])
    wheel(d, 9, 26, 3)
    wheel(d, 22, 26, 3)
    return im


def coupler():
    """16x8 车钩连杆：两节车厢之间的连接件。"""
    im, d = canvas(16, 8)
    d.rectangle((0, 3, 15, 4), fill=P[DEEP])
    d.rectangle((0, 3, 15, 3), fill=P[WHALE_MID])
    d.ellipse((5, 1, 10, 6), outline=P[INK])
    return im


def rail():
    """16x8 铁轨段：一根钢轨 + 两根枕木，横向平铺成一整条轨。"""
    im, d = canvas(16, 8)
    for x in (1, 9):
        d.rectangle((x, 3, x + 4, 6), fill=P[SAND])
        d.rectangle((x, 3, x + 4, 3), fill=P[AMBER])
    d.rectangle((0, 1, 15, 2), fill=P[DEEP])
    d.rectangle((0, 1, 15, 1), fill=P[WHALE_MID])
    return im


def main():
    ART.mkdir(parents=True, exist_ok=True)
    RES.mkdir(parents=True, exist_ok=True)
    for name, im in (('array_loco_48x32.png', loco()),
                     ('array_car_32x32.png', car()),
                     ('array_coupler_16x8.png', coupler()),
                     ('array_rail_16x8.png', rail())):
        im.save(ART / name)
        shutil.copyfile(ART / name, RES / name)
        print('已生成', name, im.size)


if __name__ == '__main__':
    main()
