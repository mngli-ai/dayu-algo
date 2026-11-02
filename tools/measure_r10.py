"""从截图里量两块东西：① 木箱名牌的墨迹中心 vs 名牌框中心（表达式垂直居中）；
② 把计数循环卡裁出来放大，肉眼核对三行字。"""
import sys
from PIL import Image

img = Image.open(sys.argv[1]).convert("RGB")
W, H = img.size
px = img.load()


def crop(x0, y0, x1, y1, scale, out):
    c = img.crop((x0, y0, x1, y1))
    c = c.resize((c.width * scale, c.height * scale), Image.NEAREST)
    c.save(out)
    print("crop ->", out, c.size)


def measure(x0, y0, x1, y1, label):
    """名牌底色是 Paper（近白的暖色），墨迹是 Ink（深蓝黑）。按颜色分类求两者的中心。"""
    bx = [10**9, 10**9, -1, -1]   # 背景框 xmin,ymin,xmax,ymax
    ix = [10**9, 10**9, -1, -1]
    for y in range(y0, y1):
        for x in range(x0, x1):
            r, g, b = px[x, y]
            paper = r > 225 and g > 220 and b > 200 and abs(r - b) < 45
            ink = r < 90 and g < 90 and b < 120
            if paper:
                bx[0] = min(bx[0], x); bx[1] = min(bx[1], y); bx[2] = max(bx[2], x); bx[3] = max(bx[3], y)
            if ink:
                ix[0] = min(ix[0], x); ix[1] = min(ix[1], y); ix[2] = max(ix[2], x); ix[3] = max(ix[3], y)
    if bx[2] < 0 or ix[2] < 0:
        print(label, "找不到背景框或墨迹", bx, ix)
        return
    bcx, bcy = (bx[0] + bx[2]) / 2.0, (bx[1] + bx[3]) / 2.0
    icx, icy = (ix[0] + ix[2]) / 2.0, (ix[1] + ix[3]) / 2.0
    print("%s 框=(%d,%d,%d,%d) 中心(%.1f,%.1f) 字=(%d,%d,%d,%d) 中心(%.1f,%.1f) 偏移 dx=%.1f dy=%.1f 框高=%d 字高=%d"
          % (label, bx[0], bx[1], bx[2], bx[3], bcx, bcy, ix[0], ix[1], ix[2], ix[3], icx, icy,
             icx - bcx, icy - bcy, bx[3] - bx[1] + 1, ix[3] - ix[1] + 1))


if len(sys.argv) > 2 and sys.argv[2] == "loop":
    crop(600, 640, 740, 780, 4, "Evidence/r10/loop-card-4x.png")
elif len(sys.argv) > 2 and sys.argv[2] == "crate":
    measure(600, 380, 720, 450, "创建变量木箱")
    crop(560, 360, 760, 470, 4, "Evidence/r10/crate-4x.png")
