# -*- coding: utf-8 -*-
"""第十八轮 #146：把 ArtInbox/bg/oga_underwater-world-pixel-bg 的 5 层真像素水下背景
搬进 Assets/Resources/Backdrop/（运行时 Backdrop.cs 从这里读）。

素材是 CraftPix「Underwater World」像素背景包（OGA-BY 3.0，见 ArtInbox/SOURCES.md），
本身就是 4~15 色的干净像素图，不做量化/重绘——只做原样拷贝 + 校验，
保证「工程里的字节 == ArtInbox 里的字节」，出问题可以逐字节回溯。

用法: C:/ProgramData/anaconda3/python tools/gen_backdrop.py
"""
import hashlib
import shutil
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "ArtInbox" / "bg" / "oga_underwater-world-pixel-bg"
DST = ROOT / "Assets" / "Resources" / "Backdrop"

# 源文件 -> 运行时名字。层内容（2026-11-04 实测 alpha 包围盒）：
#   1.png 全幅不透明水体渐变        -> uw_water （顶锚，铺满视口）
#   2.png 鱼群，悬浮在中带 y126..235 -> uw_fish  （中层视差 + 自主漂移）
#   3.png 礁石剪影，y130..324       -> uw_rocks （远景，底锚）
#   4.png 沙底，y243..324           -> uw_sand  （中近景，底锚）
#   5.png 珊瑚前景，y169..324       -> uw_coral （近景，底锚）
LAYERS = [
    ("1.png", "uw_water.png"),
    ("2.png", "uw_fish.png"),
    ("3.png", "uw_rocks.png"),
    ("4.png", "uw_sand.png"),
    ("5.png", "uw_coral.png"),
]


def md5(p: Path) -> str:
    return hashlib.md5(p.read_bytes()).hexdigest()


def main() -> None:
    DST.mkdir(parents=True, exist_ok=True)
    bad = 0
    for src_name, dst_name in LAYERS:
        s = SRC / src_name
        d = DST / dst_name
        if not s.exists():
            print(f"[MISS] 源缺失 {s}")
            bad += 1
            continue
        shutil.copyfile(s, d)
        im = Image.open(d)
        colors = len(im.convert("RGBA").getcolors(maxcolors=1 << 20) or [])
        same = md5(s) == md5(d)
        print(f"[COPY] {src_name} -> Resources/Backdrop/{dst_name} "
              f"{im.size[0]}x{im.size[1]} {im.mode} 色数={colors} 字节一致={same}")
        if im.size != (576, 324) or not same:
            bad += 1
    if bad:
        raise SystemExit(f"[FAIL] {bad} 项校验未通过")
    print(f"[OK] 5 层全部落位 {DST}")


if __name__ == "__main__":
    main()
