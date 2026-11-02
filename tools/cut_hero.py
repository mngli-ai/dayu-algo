"""从 ArtInbox 里的第三方鲸鱼娘女仆素材切出游戏用的精灵。

素材本身是别人画的（CC BY-NC-SA 4.0，署名见 ArtInbox/SOURCES.md）；
这里只做抽帧、裁切、缩放到整数格，不重绘任何一笔。
"""
from __future__ import annotations
import subprocess, tempfile, shutil
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
INBOX = ROOT / "ArtInbox"
OUT = ROOT / "Assets/Resources/Hero"
TMP = Path(tempfile.gettempdir()) / "dayu_hero_frames"


def extract(webm: Path, prefix: str) -> list[Path]:
    shutil.rmtree(TMP, ignore_errors=True)
    TMP.mkdir(parents=True)
    # VP9 的 alpha 是旁路流，必须显式指定解码器与 yuva420p 才不会被压成黑底
    subprocess.run(
        ["ffmpeg", "-y", "-v", "error", "-c:v", "libvpx-vp9", "-i", str(webm),
         "-pix_fmt", "yuva420p", "-vsync", "0", str(TMP / f"{prefix}_%04d.png")],
        check=True)
    return sorted(TMP.glob(f"{prefix}_*.png"))


def union_bbox(frames: list[Path], min_alpha: int = 24) -> tuple[int, int, int, int]:
    u = None
    for f in frames:
        a = np.array(Image.open(f).convert("RGBA"))[:, :, 3]
        ys, xs = np.nonzero(a >= min_alpha)
        if len(xs) == 0:
            continue
        b = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
        u = b if u is None else (min(u[0], b[0]), min(u[1], b[1]), max(u[2], b[2]), max(u[3], b[3]))
    if u is None:
        raise RuntimeError("整段素材都找不到不透明像素")
    return u


def square_around(bbox: tuple[int, int, int, int], side: int, canvas: tuple[int, int]) -> tuple[int, int, int, int]:
    cx = (bbox[0] + bbox[2]) / 2.0
    cy = (bbox[1] + bbox[3]) / 2.0
    x0 = int(round(cx - side / 2.0))
    y0 = int(round(cy - side / 2.0))
    x0 = max(0, min(x0, canvas[0] - side))
    y0 = max(0, min(y0, canvas[1] - side))
    return (x0, y0, x0 + side, y0 + side)


def make_strip(webm: Path, prefix: str, frames: int, cell: int, out_name: str) -> Path:
    src = extract(webm, prefix)
    bbox = union_bbox(src)
    side = max(bbox[2] - bbox[0], bbox[3] - bbox[1])
    w, h = Image.open(src[0]).size
    crop = square_around(bbox, side, (w, h))
    picks = [src[round(i * (len(src) - 1) / (frames - 1))] for i in range(frames)]
    strip = Image.new("RGBA", (cell * frames, cell), (0, 0, 0, 0))
    for i, f in enumerate(picks):
        im = Image.open(f).convert("RGBA").crop(crop).resize((cell, cell), Image.LANCZOS)
        strip.alpha_composite(im, (i * cell, 0))
    OUT.mkdir(parents=True, exist_ok=True)
    dest = OUT / out_name
    strip.save(dest)
    print(f"{out_name}: {len(src)} 帧里取 {frames} 帧，裁切 {crop}，单元 {cell}x{cell} -> {dest}")
    return dest


def make_single(src_path: Path, cell_w: int, cell_h: int, out_name: str, pad_ratio: float = 0.02) -> Path:
    im = Image.open(src_path).convert("RGBA")
    a = np.array(im)[:, :, 3]
    ys, xs = np.nonzero(a >= 24)
    b = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
    bbox_w, bbox_h = b[2] - b[0], b[3] - b[1]
    s = min(cell_w / bbox_w, cell_h / bbox_h)
    nw, nh = int(round(bbox_w * s)), int(round(bbox_h * s))
    art = im.crop(b).resize((nw, nh), Image.LANCZOS)
    out = Image.new("RGBA", (cell_w, cell_h), (0, 0, 0, 0))
    out.alpha_composite(art, ((cell_w - nw) // 2, (cell_h - nh) - int(round(cell_h * pad_ratio))))
    OUT.mkdir(parents=True, exist_ok=True)
    dest = OUT / out_name
    out.save(dest)
    print(f"{out_name}: 源 {im.size} 内容 {bbox_w}x{bbox_h} -> {cell_w}x{cell_h} 落 {dest}")
    return dest


def main():
    make_strip(INBOX / "dshpet_whale-maid_待机呼吸休闲.webm", "idle", 8, 48, "hero_idle_48x48_8f.png")
    make_strip(INBOX / "dshpet_whale-maid_女仆屈膝礼仪.webm", "bow", 8, 48, "hero_happy_48x48_8f.png")
    bust = INBOX / "fayer_whale-maid_composite_neutral.png"
    if bust.exists():
        im = Image.open(bust).convert("RGBA")
        face = im.crop((int(im.width * .20), int(im.height * .18), int(im.width * .82), int(im.height * .61)))
        face_path = TMP / "hero_face_src.png"
        TMP.mkdir(parents=True, exist_ok=True)
        face.save(face_path)
        make_single(face_path, 96, 96, "hero_face_96x96.png")
    full = INBOX / "stdw_maid-atelier_maid_left_v5.webp"
    if full.exists():
        make_single(full, 260, 460, "hero_full_260x460.png")


if __name__ == "__main__":
    main()
