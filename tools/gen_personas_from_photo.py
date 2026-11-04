# 第十八轮 #145：把网上搜集的角色立绘转成游戏用的像素帧（不画、不 AI 生成，只做抠图与降采样）。
# 源件在 ArtInbox/personas/：zipzippipe_<key>.jpg 是 pixiv 高清立绘，zipzippipe_<key>_chibi.png 是
# 同一角色、同一设计的另一姿势（bilibili 展示视频截帧）。idle 用前者，wave/talk 用后者。
# 裁切按「前景包围盒高度」的固定比例：头区 0.30、半身 0.62，且半身框宽 = 高 × 64/80。
# 这样同一角色的两种姿势里，头在 64×80 帧中占的像素高度一致，主菜单每 3 秒换姿势不会忽大忽小。
# 默认只写预览到 Evidence/r18/personas_preview/out_*.png；加 --commit 才覆盖 Assets 两处目录（同字节）。
import os
import sys
import numpy as np
from PIL import Image, ImageFile
from scipy import ndimage

ImageFile.LOAD_TRUNCATED_IMAGES = True

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "ArtInbox", "personas")
PREVIEW = os.path.join(ROOT, "Evidence", "r18", "personas_preview")
ART = os.path.join(ROOT, "Assets", "Art", "Personas")
RES = os.path.join(ROOT, "Assets", "Resources", "Personas")

FRAME_W, FRAME_H = 64, 80
FACE = 32
HEAD = 0.30      # 头区（含头发）占前景高的比例
BUST = 0.62      # 半身框占前景高的比例

# src = 主姿势（idle）；alt = 同角色另一姿势（wave/talk），没有就三帧共用 src
CHARS = {
    "dragon":  dict(src="zipzippipe_dragon.jpg",  alt="zipzippipe_gpt_chibi.png"),
    "claude":  dict(src="zipzippipe_claude.jpg",  alt="zipzippipe_claude_chibi.png"),
    "gemini":  dict(src="zipzippipe_gemini.jpg",  alt="zipzippipe_gemini_chibi.png"),
    "qianwen": dict(src="zipzippipe_qianwen.jpg", alt="zipzippipe_qwen_chibi.png"),
    "grok":    dict(src="zipzippipe_grok.jpg",    alt="zipzippipe_grok_chibi.png"),
    "doubao":  dict(src="zipzippipe_doubao.jpg",  alt=None),
}


def load(fname):
    p = os.path.join(SRC, fname)
    im = Image.open(p).convert("RGB")
    im.load()
    a = np.asarray(im)
    # 传输截断时 PIL 把未解码区填成纯 (128,128,128) 行；裁掉，免得灰块进包围盒
    uniform_gray = np.all(a == 128, axis=(1, 2))
    if uniform_gray.any():
        a = a[:int(np.argmax(uniform_gray))]
    return Image.fromarray(a, "RGB")


def cutout(im):
    """白底→透明：从四条边泛洪近白区当背景，人物内部的白色（白裙/白袜）保留。numpy+scipy 向量化。"""
    rgb = np.asarray(im, dtype=np.int16)
    mx = rgb.max(axis=2)
    mn = rgb.min(axis=2)
    nearwhite = (mn > 235) & ((mx - mn) < 24)
    lab, n = ndimage.label(nearwhite)
    border = set(lab[0, :]) | set(lab[-1, :]) | set(lab[:, 0]) | set(lab[:, -1])
    border.discard(0)
    bg = np.isin(lab, list(border))
    fg = ~bg
    # 只留最大连通域定包围盒：飘着的星星/小配件不算头身范围，但仍在裁切框内时照常画出
    lab2, n2 = ndimage.label(fg)
    if n2 > 1:
        sizes = ndimage.sum(fg, lab2, index=np.arange(1, n2 + 1))
        keep = int(np.argmax(sizes)) + 1
        body = (lab2 == keep)
    else:
        body = fg
    alpha = np.where(fg, 255, 0).astype(np.uint8)
    rgba = np.dstack([np.asarray(im, dtype=np.uint8), alpha])
    return Image.fromarray(rgba, "RGBA"), body


def fit_into(rgba_crop, tw, th):
    """等比缩到 tw x th 内（contain），居中贴到透明画布。"""
    cw, ch = rgba_crop.size
    s = min(tw / cw, th / ch)
    nw, nh = max(1, int(round(cw * s))), max(1, int(round(ch * s)))
    small = rgba_crop.resize((nw, nh), Image.LANCZOS)
    canvas = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    canvas.paste(small, ((tw - nw) // 2, (th - nh) // 2), small)
    return hard_alpha(canvas)


def hard_alpha(rgba):
    a = np.asarray(rgba).copy()
    a[:, :, 3] = np.where(a[:, :, 3] > 128, 255, 0).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def build(fname):
    rgba, body = cutout(load(fname))
    ys, xs = np.nonzero(body)
    if len(ys) == 0:
        raise SystemExit("%s: 抠不出前景（全白？）" % fname)
    x0, x1 = int(xs.min()), int(xs.max())
    y0, y1 = int(ys.min()), int(ys.max())
    fh = y1 - y0 + 1
    # 头的水平中心取头顶那条带的质心，比整个包围盒中线准：侧身/伸手的姿势不会把脸裁歪
    band = body[y0:y0 + max(1, int(fh * 0.12)), :]
    hx = int(round(np.nonzero(band)[1].mean())) + x0
    s = max(8, int(fh * HEAD))
    face_box = (max(0, hx - s // 2), y0, min(rgba.width, hx + s // 2), y0 + s)
    bh = int(fh * BUST)
    bw = int(round(bh * FRAME_W / float(FRAME_H)))
    # 半身框水平位置：以头为中心，但若有前景（长柄武器、张开的龙翼、大裙摆）落在框外，
    # 就在「不把头挤出框」的范围内往那边挪，取装下最多前景像素的那个位置。
    lo = max(0, min(hx + (s + 1) // 2 - bw, rgba.width - bw))
    hi = max(lo, min(hx - s // 2, rgba.width - bw))
    start = min(max(hx - bw // 2, lo), hi)
    cands = {start, min(max(x0, lo), hi), min(max(x1 + 1 - bw, lo), hi)}
    strip = body[y0:y0 + bh, :]
    best = max(cands, key=lambda c: int(strip[:, c:c + bw].sum()) * 100000 - abs(c - start))
    bust_box = (best, y0, min(rgba.width, best + bw), y0 + bh)
    return fit_into(rgba.crop(face_box), FACE, FACE), fit_into(rgba.crop(bust_box), FRAME_W, FRAME_H)


def frames_for(key):
    cfg = CHARS[key]
    face, idle = build(cfg["src"])
    alt = cfg.get("alt")
    wave = build(alt)[1] if alt and os.path.exists(os.path.join(SRC, alt)) else idle
    return face, idle, wave


def main():
    commit = "--commit" in sys.argv
    keys = [a for a in sys.argv[1:] if not a.startswith("--")] or list(CHARS)
    os.makedirs(PREVIEW, exist_ok=True)
    for key in keys:
        face, idle, wave = frames_for(key)
        if commit:
            for d in (ART, RES):
                os.makedirs(d, exist_ok=True)
                face.save(os.path.join(d, key + "_face.png"))
                idle.save(os.path.join(d, key + "_idle.png"))
                for a in ("wave", "talk"):
                    wave.save(os.path.join(d, key + "_" + a + ".png"))
            print("commit", key, "两姿势" if wave is not idle else "单姿势")
        else:
            face.save(os.path.join(PREVIEW, "out_%s_face.png" % key))
            idle.save(os.path.join(PREVIEW, "out_%s_idle.png" % key))
            wave.save(os.path.join(PREVIEW, "out_%s_wave.png" % key))
            print("preview", key, "face", face.size, "idle", idle.size, "wave", wave.size)


if __name__ == "__main__":
    main()
