from __future__ import annotations
"""第十七轮 · AI 虚拟形象像素立绘生成器（程序自绘原创，无任何第三方素材依赖）。

为什么要自绘：各家 LLM 的"娘化形象"全部是网友二创（谱系见 Evidence/r16/llm-persona-research.md
与 ArtInbox/SOURCES.md），要么带 CC BY-NC-SA 的 NC 限制、要么授权不明，且没有任何一家发布过
可商用的官方像素素材（排查记录见 Evidence/r17/persona-contract.md §1）。所以这里走
「自己另起一套像素吉祥物」的路线：只借用公开的性格标签（吃货/卷王/大小姐/国风/毒舌/圆润治愈），
造型、配色、构图全部由本脚本逐像素搭建，不临摹任何画师的成图。

风格口径：与 tools/gen_art.py 完全一致 —— 同一套 16 色板（Theme.cs 的 PALETTE）、
有限色、1px Ink(#38515B) 描边、无抗锯齿、整数坐标、RGBA 只允许 alpha 0 或 255。

跑法：
    C:/ProgramData/anaconda3/python tools/gen_personas.py
产物：
    Assets/Art/Personas/<key>_<action>.png       64x80 立绘帧（idle / wave / talk，大肥鱼多一帧 eat）
    Assets/Art/Personas/<key>_face.png           32x32 头像（题栏、对话框用）
    Assets/Art/Personas/personas_meta.json       帧清单（源目录，Unity 不加载）
    Assets/Resources/Personas/                   同名逐字节副本（Resources.Load 读的那份）
    Evidence/r17/personas_stage.png              主菜单舞台合成预览 1600x1000
    Evidence/r17/personas_sheet.png              全部帧总表

确定性：没有随机源（舞台图用固定种子），同机重跑逐字节一致。

【第十八轮起停用】六家 AI 的立绘已换成网上搜集的第三方同人件，转换脚本是
tools/gen_personas_from_photo.py；本脚本默认拒绝运行，详见 main() 的闸门注释。
"""
import json
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw

try:  # Windows 控制台默认 GBK，中文说明打印不出来
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:  # pragma: no cover
    pass

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Assets/Art/Personas"
RES = ROOT / "Assets/Resources/Personas"
EV = ROOT / "Evidence/r17"

FW, FH = 64, 80          # 立绘帧尺寸：所有角色严格一致，主菜单可以并排摆
VW, VH = 32, 32          # 头像尺寸

# —— 与 gen_art.py / Theme.cs 同一套 16 色板，一个色号都不许多用 ——
PALETTE = ['#FFFDF7', '#F2EAD8', '#FFFFFF', '#EAF7F8', '#B9E8DD', '#76C9D5', '#4A9EAD',
           '#2E6B77', '#38515B', '#F6B45C', '#D98E3F', '#B99AD9', '#8E6FB8', '#F4D878',
           '#F29B8F', '#8BD6A8']
RGB = [tuple(bytes.fromhex(x[1:])) + (255,) for x in PALETTE]
CREAM, SAND, WHITE, FOAM, MINTL, WHL, WHALE, WHD, INK, HONEY, HONEYPD, LAV, GRAPE, \
    YEL, CORAL, MINT = range(16)

# 出场关卡（Levels.cs 的 Story 文本口径），只是给主 agent 接线用的注释性清单
LEVELS_BY_KEY = {
    "dayu": "全部 13 关的主角：P0 P1 L1 P2 P3 P4 L5 L2 L4 S1 S2 S3 L3",
    "doubao": "P1（递纸条）/ L2（塞票数）/ S3（队尾挤进来的忙人）",
    "dragon": "L1（拍单据的实习生）/ P3（吵 9.11）/ S1（句句你说得太对了）",
    "claude": "P2（约法三章）/ L4（从夯到拉锐评）/ S2（即刻封号）",
    "gemini": "P3（自我否定循环）/ L3（乱塞管道接头）",
    "grok": "P4（全是我一个人干）",
    "qianwen": "L2（撇清格子不是她排的）/ S3（排在队尾的国风娘）",
}


# --------------------------------------------------------------------------- 像素画布
class G:
    """整数格画布：-1 = 透明，其余是色板下标。"""

    def __init__(self, w=FW, h=FH):
        self.w, self.h = w, h
        self.a = [[-1] * w for _ in range(h)]

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.a[y][x] = c

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.a[y][x]
        return -1

    def rect(self, x, y, w, h, c):
        for j in range(int(h)):
            for i in range(int(w)):
                self.set(x + i, y + j, c)

    def frame(self, x, y, w, h, c):
        self.rect(x, y, w, 1, c)
        self.rect(x, y + h - 1, w, 1, c)
        self.rect(x, y, 1, h, c)
        self.rect(x + w - 1, y, 1, h, c)

    def ell(self, cx, cy, rx, ry, c):
        rx, ry = float(rx), float(ry)
        for y in range(int(math.floor(cy - ry)), int(math.ceil(cy + ry)) + 1):
            for x in range(int(math.floor(cx - rx)), int(math.ceil(cx + rx)) + 1):
                dx, dy = (x - cx) / max(rx, .5), (y - cy) / max(ry, .5)
                if dx * dx + dy * dy <= 1.0:
                    self.set(x, y, c)

    def ring(self, cx, cy, rx, ry, c):
        """只画轮廓的椭圆（气泡、星光用）"""
        for y in range(int(cy - ry), int(cy + ry) + 1):
            for x in range(int(cx - rx), int(cx + rx) + 1):
                d = ((x - cx) / max(rx, .5)) ** 2 + ((y - cy) / max(ry, .5)) ** 2
                if 0.55 < d <= 1.0:
                    self.set(x, y, c)

    def poly(self, pts, c):
        """偶奇扫描线多边形填充"""
        ys = [p[1] for p in pts]
        n = len(pts)
        for y in range(int(math.floor(min(ys))), int(math.ceil(max(ys))) + 1):
            yy = y + 0.5
            xc = []
            for i in range(n):
                x1, y1 = pts[i]
                x2, y2 = pts[(i + 1) % n]
                if (y1 <= yy < y2) or (y2 <= yy < y1):
                    t = (yy - y1) / (y2 - y1)
                    xc.append(x1 + t * (x2 - x1))
            xc.sort()
            for k in range(0, len(xc) - 1, 2):
                for x in range(int(round(xc[k])), int(round(xc[k + 1])) + 1):
                    self.set(x, y, c)

    def seg(self, x0, y0, x1, y1, c, t=3):
        """粗细为 t 的线段（四肢、飘带）"""
        steps = max(abs(x1 - x0), abs(y1 - y0), 1)
        r = t // 2
        for s in range(steps + 1):
            x = int(round(x0 + (x1 - x0) * s / steps))
            y = int(round(y0 + (y1 - y0) * s / steps))
            self.rect(x - r, y - r, t, t, c)

    def star4(self, cx, cy, r, c):
        """四角星（Gemini 的官方徽标形状，也是通用"闪光"记号）"""
        self.poly([(cx, cy - r), (cx + r // 3, cy - r // 3), (cx + r, cy),
                   (cx + r // 3, cy + r // 3), (cx, cy + r),
                   (cx - r // 3, cy + r // 3), (cx - r, cy),
                   (cx - r // 3, cy - r // 3)], c)

    def outline(self, c=INK):
        """透明格里挑出紧贴实色的那圈，涂成描边色 —— 1px、只走四邻，不糊细节"""
        out = [row[:] for row in self.a]
        for y in range(self.h):
            for x in range(self.w):
                if self.a[y][x] >= 0:
                    continue
                for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                    if 0 <= nx < self.w and 0 <= ny < self.h and self.a[ny][nx] >= 0:
                        out[y][x] = c
                        break
        self.a = out

    def image(self):
        im = Image.new("RGBA", (self.w, self.h), (0, 0, 0, 0))
        px = im.load()
        for y, row in enumerate(self.a):
            for x, c in enumerate(row):
                if c >= 0:
                    px[x, y] = RGB[c]
        return im


# --------------------------------------------------------------------------- 通用零件
SKIN = SAND        # 暖沙肤色：白发白裙的角色也要把脸拉出来
EYEY = 27          # 眼睛行
MOUTHY = 33
SEP = WHD          # 同色块之间的 1px 分隔线（描边只走外轮廓，内部分层得自己画）


def shadow(g, rx=14, c=WHALE):
    g.ell(32, 74, rx, 2, c)


def strands(g, x0, x1, y0, y1, c, step=5):
    """长发上的竖向发丝线：一片色块立刻变成一缕一缕"""
    for x in range(x0, x1 + 1, step):
        g.rect(x, y0, 1, y1 - y0, c)


def front_locks(g, c, hi, dy=0, y0=16, y1=54):
    """垂在裙前的两缕长直发：长发角色靠这两条才读得出"长" """
    for x0 in (18, 41):
        g.rect(x0, y0 + dy, 5, y1 - y0, c)
        g.poly([(x0, y1 + dy), (x0 + 5, y1 + dy), (x0 + 2, y1 + 5 + dy)], c)
        g.rect(x0 + 1, y0 + 3 + dy, 1, y1 - y0 - 4, hi)


def eye(g, x, y, col=INK, hi=True):
    g.rect(x, y, 2, 3, col)
    if hi:
        g.set(x, y, WHITE)


def eyes(g, act, colL=INK, colR=INK, dx=0):
    """按动作摆双眼：wave 眨一只，eat 眯成弧，talk 整体朝伸手那边偏一格"""
    y = EYEY
    off = -1 if act == "talk" else 0
    lx, rx = 26 + dx + off, 36 + dx + off
    if act == "eat":
        for x in (lx, rx):
            g.rect(x, y + 1, 3, 1, INK)
            g.set(x, y, INK)
            g.set(x + 2, y, INK)
        return
    if act == "wave":
        eye(g, rx, y, colR)
        g.rect(lx, y + 1, 3, 1, INK)          # 挤起来的左眼
        return
    eye(g, lx, y, colL)
    eye(g, rx, y, colR)


def brows(g, kind="flat", dx=0):
    y = 23
    if kind == "flat":
        g.rect(25 + dx, y, 4, 1, INK)
        g.rect(35 + dx, y, 4, 1, INK)
    elif kind == "stern":                          # 大小姐：眉头压低、外侧翘起
        g.rect(25 + dx, y + 1, 4, 1, INK)
        g.set(25 + dx, y, INK)
        g.rect(35 + dx, y + 1, 4, 1, INK)
        g.set(38 + dx, y, INK)
    elif kind == "sly":                            # grok：一边高一边低
        g.rect(25 + dx, y + 1, 4, 1, INK)
        g.rect(35 + dx, y, 4, 1, INK)
        g.set(38 + dx, y + 1, INK)
    elif kind == "soft":                           # 豆包 / 千问：短短一横、离眼睛远一点
        g.rect(26 + dx, y - 1, 3, 1, WHD)
        g.rect(36 + dx, y - 1, 3, 1, WHD)


def mouth(g, act, kind="dot", dx=0):
    y = MOUTHY
    x = 32 + dx
    if act == "talk":
        g.rect(x - 2, y, 4, 3, INK)               # 说话：张嘴，露一颗牙
        g.rect(x - 1, y, 2, 1, WHITE)
        return
    if act == "eat":
        g.rect(x - 1, y + 1, 3, 2, INK)
        g.rect(x - 1, y + 1, 1, 1, HONEYPD)
        return
    if kind == "dot":
        g.rect(x - 1, y + 1, 2, 1, INK)
    elif kind == "smile":
        g.rect(x - 2, y, 1, 1, INK)
        g.rect(x - 1, y + 1, 2, 1, INK)
        g.rect(x + 2, y, 1, 1, INK)
    elif kind == "wave":
        g.rect(x - 2, y, 5, 1, INK)
        g.rect(x - 1, y + 1, 3, 1, INK)
    elif kind == "flat":
        g.rect(x - 2, y + 1, 5, 1, INK)
    elif kind == "smirk":
        g.rect(x - 2, y + 1, 4, 1, INK)
        g.rect(x + 2, y, 1, 1, INK)


def cheeks(g, dx=0, y=EYEY + 5, w=2):
    g.rect(22 + dx, y, w, 1, CORAL)
    g.rect(40 - w + 1 + dx, y, w, 1, CORAL)


def hand(g, x, y, c=SKIN):
    g.rect(x - 1, y, 3, 3, c)


def arm(g, sx, sy, hx, hy, sleeve, hand_c=SKIN, t=4):
    """肩到手：先用分隔色铺粗一格，再压袖子色 —— 手臂才不会和身子糊成一片"""
    lx, ly = sx + (hx - sx) * 0.55, sy + (hy - sy) * 0.55
    g.seg(sx, sy, int(lx), int(ly), SEP, t=t + 1)
    g.seg(int(lx), int(ly), hx, hy, SEP, t=max(3, t - 1) + 1)
    g.seg(sx, sy, int(lx), int(ly), sleeve, t=t)
    g.seg(int(lx), int(ly), hx, hy, sleeve, t=max(3, t - 1))
    hand(g, hx, hy, hand_c)


def paper(g, x, y, w, h, edge=HONEY):
    """一张空白纸片（不写任何字，文字由主 agent 的位图字体负责）"""
    g.rect(x, y, w, h, WHITE)
    g.frame(x, y, w, h, INK)
    g.rect(x + 1, y + h - 2, w - 3, 1, SAND)
    g.rect(x + w - 2, y + 1, 1, 2, edge)


def bowl(g, cx, cy, chopsticks=False):
    """一只白米饭碗：大肥鱼的标志物"""
    g.ell(cx, cy, 7, 4, WHITE)
    g.rect(cx - 7, cy - 1, 15, 2, SAND)           # 饭
    g.rect(cx - 6, cy - 3, 13, 2, WHITE)          # 饭尖
    g.ell(cx, cy + 2, 5, 2, HONEYPD)              # 碗底
    if chopsticks:
        g.seg(cx + 2, cy - 8, cx + 8, cy - 2, HONEY, t=1)
        g.seg(cx + 3, cy - 9, cx + 9, cy - 3, HONEYPD, t=1)


def sparkles(g, pts, c=WHL):
    for (x, y, r) in pts:
        g.star4(x, y, r, c)


# --------------------------------------------------------------------------- 角色规格
SPECS = {
    # key: (中文名, 性格/视觉要点, 发色, 眼色 L/R)
    "dayu": dict(cn="大肥鱼", hair=WHD, hairHi=WHALE, eyeL=INK, eyeR=INK,
                 note="圆滚滚深蓝短发+大白围裙+饭碗+大鲸尾；不是女仆装、没有头鳍与蝴蝶结"),
    "dragon": dict(cn="龙娘", hair=WHITE, hairHi=WHITE, eyeL=INK, eyeR=INK,
                   note="白发白裙金络边，一对蜜黄龙角、两片淡青龙翼、白龙尾"),
    "claude": dict(cn="claude娘", hair=HONEY, hairHi=YEL, eyeL=INK, eyeR=INK,
                   note="琥珀橙长直+齐刘海+侧头花，墨黑三层长裙，站姿笔直、双手抱胸"),
    "gemini": dict(cn="gemini娘", hair=GRAPE, hairHi=LAV, eyeL=WHL, eyeR=CORAL,
                   note="葡萄紫长发，两侧各一条淡紫渐变带，异色瞳，别一枚四角星"),
    "qianwen": dict(cn="千问娘", hair=INK, hairHi=WHD, eyeL=INK, eyeR=INK,
                    note="墨发高髻插蜜黄发簪，薄荷绿交领广袖襦裙+深蓝腰封，国风"),
    "grok": dict(cn="grok娘", hair=YEL, hairHi=HONEY, eyeL=INK, eyeR=INK,
                 note="金黄双马尾+珊瑚色发绳，黑色短裙长筒袜，挑眼歪嘴的毒舌相"),
    "doubao": dict(cn="豆包", hair=WHL, hairHi=MINTL, eyeL=INK, eyeR=INK,
                   note="最矮最圆：淡蓝妹妹头+沙色豆豆连体衣，头顶一撮新芽，天然呆"),
}

ACTIONS = {
    "dayu": ["idle", "wave", "talk", "eat"],
    "dragon": ["idle", "wave", "talk"],
    "claude": ["idle", "wave", "talk"],
    "gemini": ["idle", "wave", "talk"],
    "qianwen": ["idle", "wave", "talk"],
    "grok": ["idle", "wave", "talk"],
    "doubao": ["idle", "wave", "talk"],
}

BOB = {"idle": 0, "wave": -1, "talk": -1, "eat": 0}


# --------------------------------------------------------------------------- 逐角色
def draw_dayu(g, act, dy):
    shadow(g, rx=15)
    # 大鲸尾：从胯后甩到右上，末端一片尾鳍（通用元素，不做任何成图复刻）
    g.seg(40, 62, 52, 50, WHD, t=8)
    g.seg(52, 50, 55, 42, WHD, t=6)
    g.poly([(50, 44), (62, 32), (60, 46), (54, 48)], WHALE)
    g.poly([(52, 42), (59, 35), (58, 43)], WHL)
    # 腿脚：深蓝裤袜 + 黑鞋
    g.rect(27, 61, 4, 10, WHD)
    g.rect(34, 61, 4, 10, WHD)
    g.rect(26, 69, 6, 4, INK)
    g.rect(33, 69, 6, 4, INK)
    # 圆滚滚的身子 + 大白围裙（做饭系，不是女仆头饰那一套）
    g.ell(32, 52 + dy, 13, 11, WHD)
    g.rect(24, 43 + dy, 16, 19, WHITE)
    g.ell(32, 61 + dy, 7, 2, WHITE)
    g.rect(26, 41 + dy, 3, 4, HONEY)              # 围裙肩带
    g.rect(35, 41 + dy, 3, 4, HONEY)
    g.rect(24, 43 + dy, 16, 1, SEP)
    g.rect(28, 54 + dy, 8, 6, WHD)                # 围裙口袋
    g.poly([(29, 56 + dy), (34, 56 + dy), (32, 59 + dy)], MINTL)   # 口袋上一只小鲸剪影
    g.rect(33, 55 + dy, 1, 2, MINTL)
    # 手臂 + 饭碗
    if act == "talk":
        arm(g, 21, 45 + dy, 9, 41 + dy, WHALE, t=4)
        arm(g, 43, 45 + dy, 45, 55 + dy, WHALE, t=4)
        bowl(g, 6, 36 + dy, chopsticks=True)      # 把饭递给对面那位
    elif act == "eat":
        arm(g, 21, 46 + dy, 25, 41 + dy, WHALE, t=4)
        arm(g, 43, 46 + dy, 39, 41 + dy, WHALE, t=4)
        bowl(g, 32, 39 + dy, chopsticks=True)
    elif act == "wave":
        arm(g, 43, 45 + dy, 49, 20 + dy, WHALE, t=4)
        arm(g, 21, 45 + dy, 17, 52 + dy, WHALE, t=4)
        bowl(g, 16, 58 + dy, chopsticks=True)
    else:
        arm(g, 21, 45 + dy, 18, 55 + dy, WHALE, t=4)
        arm(g, 43, 45 + dy, 46, 51 + dy, WHALE, t=4)
        bowl(g, 47, 48 + dy, chopsticks=True)
    # 头
    g.ell(32, 26 + dy, 12, 11, SKIN)
    g.rect(29, 36 + dy, 6, 3, WHD)
    # 深蓝短发：蓬、发梢小翘，不碰"渐变长发+头鳍+蝴蝶结"那套签名组合
    g.poly([(20, 26 + dy), (19, 14 + dy), (26, 9 + dy), (38, 9 + dy), (45, 14 + dy),
            (44, 26 + dy)], WHD)
    g.poly([(23, 13 + dy), (26, 7 + dy), (29, 13 + dy)], WHD)      # 两撮小翘
    g.poly([(35, 13 + dy), (38, 6 + dy), (41, 13 + dy)], WHD)
    g.rect(23, 15 + dy, 10, 2, WHALE)             # 高光带
    g.rect(21, 20 + dy, 6, 4, WHALE)
    g.rect(19, 22 + dy, 4, 10, WHD)               # 鬓发
    g.rect(41, 22 + dy, 4, 10, WHD)
    eyes(g, act)
    brows(g, "soft")
    mouth(g, act, "smile")
    cheeks(g)
    sparkles(g, [(54, 22 + dy, 3), (10, 26 + dy, 2)])


def draw_dragon(g, act, dy):
    shadow(g)
    # 龙翼（收在肩后，两片小帆）
    for sx in (1, -1):
        cx = 32 + sx * 12
        g.poly([(cx, 42 + dy), (cx + sx * 10, 32 + dy), (cx + sx * 12, 44 + dy),
                (cx + sx * 3, 50 + dy)], WHL)
        g.poly([(cx + sx, 42 + dy), (cx + sx * 7, 36 + dy), (cx + sx * 8, 44 + dy)], MINTL)
    # 白龙尾（甩到右侧，尾尖一点金）
    g.seg(41, 62, 52, 54, WHITE, t=5)
    g.seg(52, 54, 55, 46, WHITE, t=4)
    g.ell(55, 44, 3, 3, HONEY)
    # 长发：上宽下窄，一缕一缕
    g.poly([(19, 15 + dy), (45, 15 + dy), (44, 46 + dy), (40, 58 + dy), (24, 58 + dy),
            (20, 46 + dy)], WHITE)
    strands(g, 24, 40, 20 + dy, 54 + dy, SAND, step=5)
    g.poly([(24, 56 + dy), (28, 62 + dy), (30, 56 + dy)], WHITE)
    g.poly([(35, 56 + dy), (38, 63 + dy), (40, 56 + dy)], WHITE)
    g.rect(23, 57 + dy, 18, 1, SEP)
    # 裙：奶油白布 + 蜜金络边（比头发的白暖一档）
    g.poly([(23, 43 + dy), (41, 43 + dy), (46, 70 + dy), (18, 70 + dy)], CREAM)
    g.rect(18, 68 + dy, 29, 2, HONEY)
    g.rect(24, 52 + dy, 16, 3, HONEY)             # 腰带
    g.rect(27, 43 + dy, 10, 7, WHITE)             # 前襟
    g.rect(30, 43 + dy, 4, 9, SAND)
    g.rect(23, 43 + dy, 18, 1, SEP)
    front_locks(g, WHITE, SAND, dy)               # 垂在裙前的两缕白发
    # 手臂
    if act == "talk":
        arm(g, 21, 45 + dy, 9, 42 + dy, CREAM, t=4)
        arm(g, 43, 45 + dy, 45, 55 + dy, CREAM, t=4)
        paper(g, 2, 34 + dy, 9, 7, edge=HONEY)    # 两张单据之一
    elif act == "wave":
        arm(g, 43, 44 + dy, 49, 20 + dy, CREAM, t=4)
        arm(g, 21, 45 + dy, 17, 52 + dy, CREAM, t=4)
        g.rect(13, 54 + dy, 8, 10, HONEYPD)       # 打工日志（连摸鱼都记进去了）
        g.rect(14, 56 + dy, 6, 1, WHITE)
        g.rect(14, 59 + dy, 6, 1, WHITE)
        g.rect(14, 62 + dy, 4, 1, WHITE)
    else:
        arm(g, 21, 45 + dy, 17, 52 + dy, CREAM, t=4)
        arm(g, 43, 45 + dy, 46, 52 + dy, CREAM, t=4)
        g.rect(13, 54 + dy, 8, 10, HONEYPD)
        g.rect(14, 56 + dy, 6, 1, WHITE)
        g.rect(14, 59 + dy, 6, 1, WHITE)
        paper(g, 24, 34 + dy, 9, 7, edge=HONEY)   # 另一张单据拍在秤台上
    # 头 + 龙角
    g.ell(32, 26 + dy, 12, 11, SKIN)
    g.rect(29, 36 + dy, 6, 3, WHD)
    for sx in (1, -1):
        bx = 32 + sx * 9
        g.poly([(bx - 3, 16 + dy), (bx + sx * 2, 3 + dy), (bx + sx * 6, 14 + dy)], HONEY)
        g.poly([(bx - 1, 15 + dy), (bx + sx * 2, 6 + dy), (bx + sx * 4, 14 + dy)], YEL)
    g.poly([(20, 26 + dy), (20, 12 + dy), (32, 9 + dy), (44, 12 + dy), (44, 26 + dy)], WHITE)
    g.poly([(26, 18 + dy), (29, 25 + dy), (32, 18 + dy)], WHITE)   # 刘海中缝
    g.poly([(33, 18 + dy), (36, 25 + dy), (39, 18 + dy)], WHITE)
    g.rect(31, 18 + dy, 2, 5, SAND)
    g.rect(20, 19 + dy, 4, 14, WHITE)             # 两侧垂发
    g.rect(40, 19 + dy, 4, 14, WHITE)
    g.rect(23, 12 + dy, 12, 2, MINTL)             # 头顶一道光
    eyes(g, act)
    brows(g, "flat")
    mouth(g, act, "wave" if act == "wave" else "smile")
    cheeks(g)
    sparkles(g, [(52, 18 + dy, 3), (11, 20 + dy, 2)])


def draw_claude(g, act, dy):
    shadow(g)
    # 橙发长直：上齐下尖，一缕一缕压出竖向光泽
    g.poly([(19, 13 + dy), (45, 13 + dy), (44, 48 + dy), (41, 60 + dy), (23, 60 + dy),
            (20, 48 + dy)], HONEY)
    strands(g, 23, 41, 16 + dy, 58 + dy, HONEYPD, step=5)
    g.rect(19, 13 + dy, 26, 2, YEL)
    # 裙：墨黑三层，裙边压一道奶白 —— "立规矩"的气质靠笔直的站姿与硬边
    g.poly([(24, 44 + dy), (40, 44 + dy), (42, 56 + dy), (22, 56 + dy)], INK)
    g.poly([(22, 56 + dy), (42, 56 + dy), (45, 64 + dy), (19, 64 + dy)], INK)
    g.poly([(19, 64 + dy), (45, 64 + dy), (47, 71 + dy), (17, 71 + dy)], INK)
    for y in (55, 63, 70):
        g.rect(17, y + dy, 31, 1, CREAM)
    g.rect(24, 40 + dy, 16, 6, INK)
    g.rect(27, 40 + dy, 10, 4, CREAM)             # 领口一圈白
    front_locks(g, HONEY, HONEYPD, dy)            # 垂在裙前的两缕橙发
    # 手臂
    if act == "talk":
        arm(g, 21, 44 + dy, 8, 41 + dy, INK, t=4)
        arm(g, 43, 44 + dy, 45, 52 + dy, INK, t=4)
        paper(g, 1, 33 + dy, 9, 7, edge=HONEYPD)
        g.rect(2, 34 + dy, 2, 2, HONEYPD)
    elif act == "wave":
        arm(g, 21, 44 + dy, 11, 43 + dy, INK, t=4)   # 摊开一只手：「约法三章」
        arm(g, 43, 44 + dy, 44, 52 + dy, INK, t=4)
        g.rect(7, 39 + dy, 6, 5, SKIN)
        g.rect(7, 38 + dy, 1, 2, INK)
        g.rect(9, 38 + dy, 1, 2, INK)
        g.rect(11, 38 + dy, 1, 2, INK)
    else:                                        # idle：双手抱胸
        g.rect(21, 46 + dy, 22, 4, INK)
        g.rect(21, 46 + dy, 22, 1, HONEY)
        hand(g, 21, 47 + dy)
        hand(g, 43, 47 + dy)
        g.rect(22, 50 + dy, 20, 3, INK)
        g.rect(22, 50 + dy, 20, 1, HONEYPD)
    # 头
    g.ell(32, 26 + dy, 12, 11, SKIN)
    g.rect(29, 36 + dy, 6, 3, WHD)
    g.rect(20, 14 + dy, 24, 9, HONEY)             # 齐刘海，一条水平硬边
    g.rect(20, 22 + dy, 24, 1, HONEYPD)
    g.rect(19, 15 + dy, 4, 22, HONEY)             # 两侧垂发
    g.rect(41, 15 + dy, 4, 22, HONEY)
    # 侧头花
    g.rect(41, 11 + dy, 5, 1, CORAL)
    g.rect(41, 15 + dy, 5, 1, CORAL)
    g.rect(43, 12 + dy, 1, 3, CORAL)
    g.set(42, 12 + dy, CORAL)
    g.set(45, 12 + dy, CORAL)
    g.set(42, 14 + dy, CORAL)
    g.set(45, 14 + dy, CORAL)
    g.rect(43, 13 + dy, 1, 1, WHITE)
    eyes(g, act)
    brows(g, "stern")
    mouth(g, act, "flat")
    sparkles(g, [(50, 24 + dy, 2)])


def draw_gemini(g, act, dy):
    shadow(g)
    # 长发：中间葡萄紫、两侧各一条淡紫带 —— 用两块色带表达双色渐变意象
    g.poly([(19, 14 + dy), (45, 14 + dy), (44, 48 + dy), (41, 58 + dy), (23, 58 + dy),
            (20, 48 + dy)], GRAPE)
    g.rect(19, 14 + dy, 6, 40, LAV)
    g.rect(39, 14 + dy, 6, 40, MINTL)
    strands(g, 26, 38, 18 + dy, 54 + dy, WHD, step=6)
    # 裙
    g.poly([(23, 44 + dy), (41, 44 + dy), (45, 68 + dy), (19, 68 + dy)], LAV)
    g.poly([(23, 44 + dy), (41, 44 + dy), (42, 52 + dy), (22, 52 + dy)], GRAPE)
    g.rect(19, 66 + dy, 27, 2, WHL)
    g.rect(27, 44 + dy, 10, 9, MINTL)
    g.rect(30, 44 + dy, 4, 12, WHL)
    g.rect(23, 44 + dy, 18, 1, SEP)
    # 裙前两缕：左淡紫、右更淡一档，渐变带一路垂到底
    for x0, c in ((18, LAV), (41, MINTL)):
        g.rect(x0, 16 + dy, 5, 38, c)
        g.poly([(x0, 54 + dy), (x0 + 5, 54 + dy), (x0 + 2, 59 + dy)], c)
        g.rect(x0 + 1, 20 + dy, 1, 32, GRAPE)
    # 手臂
    if act == "talk":
        arm(g, 21, 45 + dy, 9, 42 + dy, LAV, t=4)
        arm(g, 43, 45 + dy, 45, 55 + dy, LAV, t=4)
        g.star4(4, 36 + dy, 6, YEL)              # 递出去的四角星
        g.star4(12, 30 + dy, 3, WHITE)
    elif act == "wave":
        arm(g, 43, 45 + dy, 49, 20 + dy, LAV, t=4)
        arm(g, 21, 45 + dy, 18, 55 + dy, LAV, t=4)
        g.star4(50, 12 + dy, 5, YEL)
    else:
        arm(g, 21, 45 + dy, 18, 55 + dy, LAV, t=4)
        arm(g, 43, 45 + dy, 46, 55 + dy, LAV, t=4)
        g.star4(52, 32 + dy, 4, YEL)
    # 头
    g.ell(32, 26 + dy, 12, 11, SKIN)
    g.rect(29, 36 + dy, 6, 3, WHD)
    g.poly([(20, 25 + dy), (20, 12 + dy), (32, 9 + dy), (44, 12 + dy), (44, 25 + dy)], GRAPE)
    g.rect(20, 12 + dy, 8, 8, LAV)
    g.rect(36, 12 + dy, 8, 8, MINTL)
    g.poly([(28, 20 + dy), (32, 27 + dy), (36, 20 + dy)], GRAPE)   # 中分
    g.rect(18, 16 + dy, 4, 20, LAV)
    g.rect(42, 16 + dy, 4, 20, MINTL)
    g.star4(42, 12 + dy, 5, YEL)                  # 发上的四角星
    # 异色瞳
    eye(g, 26, EYEY + dy, WHL)
    eye(g, 36, EYEY + dy, CORAL)
    if act == "wave":
        g.rect(26, EYEY + 1 + dy, 3, 1, INK)
    if act == "talk":
        eye(g, 25, EYEY + dy, WHL)
        eye(g, 35, EYEY + dy, CORAL)
    brows(g, "soft")
    mouth(g, act, "smile")
    cheeks(g)


def draw_qianwen(g, act, dy):
    shadow(g)
    # 襦裙：薄荷绿交领 + 深蓝腰封 + 蜜黄络边，广袖
    g.poly([(23, 42 + dy), (41, 42 + dy), (46, 70 + dy), (18, 70 + dy)], MINT)
    g.poly([(26, 42 + dy), (32, 51 + dy), (38, 42 + dy), (36, 42 + dy), (32, 48 + dy),
            (28, 42 + dy)], CREAM)                # 交领的 Y 字白领
    g.rect(24, 52 + dy, 16, 5, WHD)               # 腰封
    g.rect(24, 52 + dy, 16, 1, HONEY)
    g.rect(30, 57 + dy, 4, 8, HONEY)              # 垂下的系带
    g.rect(18, 68 + dy, 29, 2, MINTL)
    g.rect(27, 70 + dy, 10, 2, INK)
    # 手臂（广袖：袖口比手大得多）
    def sleeve(sx, hx, hy):
        g.poly([(sx - 6, 43 + dy), (sx + 6, 43 + dy), (hx + 5, hy + 7), (hx - 5, hy + 7)], SEP)
        g.poly([(sx - 5, 44 + dy), (sx + 5, 44 + dy), (hx + 4, hy + 6), (hx - 4, hy + 6)], MINT)
        g.rect(hx - 3, hy + 3, 7, 3, MINTL)
        hand(g, hx, hy + 5)
    if act == "talk":
        sleeve(21, 9, 44)
        sleeve(43, 44, 54)
        g.poly([(1, 40 + dy), (10, 36 + dy), (10, 46 + dy)], WHITE)   # 折扇
        g.seg(10, 46, 2, 41, INK, t=1)
        g.seg(10, 43, 2, 39, INK, t=1)
    elif act == "wave":
        sleeve(21, 18, 54)
        sleeve(43, 48, 22)
    else:
        sleeve(21, 20, 54)
        sleeve(43, 44, 54)
        g.rect(26, 50 + dy, 12, 4, MINTL)         # 两手拢在身前
    # 头
    g.ell(32, 26 + dy, 12, 11, SKIN)
    g.rect(29, 36 + dy, 6, 3, WHD)
    g.poly([(20, 25 + dy), (20, 12 + dy), (32, 9 + dy), (44, 12 + dy), (44, 25 + dy)], INK)
    g.ell(32, 9 + dy, 6, 5, INK)                  # 高髻
    g.ell(32, 8 + dy, 4, 3, WHD)
    g.seg(26, 9 + dy, 40, 6 + dy, HONEY, t=2)     # 发簪
    g.rect(40, 5 + dy, 3, 3, CORAL)
    g.rect(19, 16 + dy, 4, 22, INK)               # 两侧垂发
    g.rect(41, 16 + dy, 4, 22, INK)
    g.rect(23, 15 + dy, 18, 5, INK)
    eyes(g, act)
    brows(g, "soft")
    mouth(g, act, "smile")
    cheeks(g)
    sparkles(g, [(50, 40 + dy, 2)])


def draw_grok(g, act, dy):
    shadow(g, rx=12)
    # 金黄双马尾：扎在头两侧，甩到腰下收成尖
    for sx in (1, -1):
        bx = 32 + sx * 14
        g.rect(bx - 4, 18 + dy, 8, 6, YEL)        # 发绳处的一坨
        g.rect(bx - 3, 24 + dy, 6, 22, HONEY)
        g.poly([(bx - 5, 44 + dy), (bx + 5, 44 + dy), (bx + sx * 4, 58 + dy),
                (bx + sx * 1, 64 + dy), (bx - sx * 2, 54 + dy)], YEL)
        g.poly([(bx - 2, 40 + dy), (bx + 2, 40 + dy), (bx + sx * 2, 56 + dy)], HONEYPD)
        g.rect(bx - 4, 20 + dy, 8, 3, CORAL)      # 珊瑚色发绳
    # 黑短裙 + 长筒袜（袜口压一道奶白，才和裙子分得开）
    g.poly([(24, 44 + dy), (40, 44 + dy), (44, 58 + dy), (20, 58 + dy)], INK)
    g.rect(20, 56 + dy, 25, 2, CORAL)
    g.rect(25, 40 + dy, 14, 6, INK)
    g.rect(27, 40 + dy, 10, 2, CREAM)             # 哥特式小白领
    g.rect(31, 42 + dy, 2, 4, CORAL)
    g.rect(27, 59 + dy, 4, 11, INK)
    g.rect(33, 59 + dy, 4, 11, INK)
    g.rect(27, 59 + dy, 4, 2, CREAM)              # 袜口
    g.rect(33, 59 + dy, 4, 2, CREAM)
    g.rect(26, 69 + dy, 6, 4, INK)                # 小皮鞋
    g.rect(32, 69 + dy, 6, 4, INK)
    g.rect(26, 68 + dy, 6, 1, CORAL)
    g.rect(32, 68 + dy, 6, 1, CORAL)
    # 手臂
    if act == "talk":
        arm(g, 21, 44 + dy, 9, 42 + dy, INK, t=4)
        arm(g, 43, 44 + dy, 44, 52 + dy, INK, t=4)
        g.rect(2, 36 + dy, 8, 6, INK)             # 一张黑卡
        g.frame(2, 36 + dy, 8, 6, CORAL)
        g.rect(4, 38 + dy, 1, 1, WHITE)
        g.rect(7, 38 + dy, 1, 1, WHITE)
    elif act == "wave":
        arm(g, 43, 44 + dy, 47, 20 + dy, INK, t=4)
        arm(g, 21, 44 + dy, 14, 48 + dy, INK, t=4)   # 一摊手：「全是我一个人干」
    else:
        arm(g, 21, 44 + dy, 18, 52 + dy, INK, t=4)
        arm(g, 43, 44 + dy, 46, 52 + dy, INK, t=4)
        g.rect(43, 51 + dy, 7, 6, HONEYPD)        # 一台小终端，活全在她一个人手上
        g.rect(44, 52 + dy, 5, 3, WHL)
    # 头
    g.ell(32, 26 + dy, 11, 10, SKIN)
    g.rect(29, 35 + dy, 6, 3, WHD)
    g.poly([(21, 25 + dy), (21, 12 + dy), (32, 9 + dy), (43, 12 + dy), (43, 25 + dy)], YEL)
    g.rect(22, 13 + dy, 20, 5, YEL)
    g.poly([(24, 18 + dy), (28, 25 + dy), (30, 18 + dy)], HONEY)   # 锯齿刘海
    g.poly([(31, 18 + dy), (35, 25 + dy), (37, 18 + dy)], HONEY)
    g.rect(24, 12 + dy, 10, 2, HONEYPD)
    eyes(g, act)
    brows(g, "sly")
    mouth(g, act, "smirk")
    g.rect(25, EYEY + 4 + dy, 3, 1, CORAL)        # 眼下两道淡影
    g.rect(36, EYEY + 4 + dy, 3, 1, CORAL)
    sparkles(g, [(12, 16 + dy, 2), (52, 16 + dy, 2)])


def draw_doubao(g, act, dy):
    shadow(g, rx=15)
    # 最矮最圆：沙色豆豆连体衣 + 奶白肚皮，头直接坐在身上（没有脖子），比谁都矮一档
    g.ell(32, 52 + dy, 14, 11, SAND)
    g.ell(32, 54 + dy, 9, 7, CREAM)
    g.rect(28, 57 + dy, 8, 4, WHL)                # 肚皮上一只小口袋
    g.ell(32, 59 + dy, 3, 3, MINTL)
    g.rect(26, 62 + dy, 5, 8, SAND)               # 小短腿
    g.rect(33, 62 + dy, 5, 8, SAND)
    g.rect(25, 69 + dy, 6, 4, INK)
    g.rect(33, 69 + dy, 6, 4, INK)
    g.rect(24, 40 + dy, 16, 1, SEP)               # 下巴下一道影，头和身子才分得开
    # 头顶一撮新芽（"如破土的新芽"）
    g.rect(31, 6 + dy, 2, 5, MINT)
    g.ell(35, 7 + dy, 3, 2, MINT)
    g.ell(29, 9 + dy, 3, 2, MINTL)
    # 手臂短
    if act == "talk":
        arm(g, 20, 48 + dy, 9, 44 + dy, WHL, t=4)
        arm(g, 44, 48 + dy, 45, 56 + dy, WHL, t=4)
        paper(g, 2, 36 + dy, 11, 8, edge=WHL)     # 一张只写着一个数的纸条
    elif act == "wave":
        arm(g, 44, 46 + dy, 50, 24 + dy, WHL, t=4)
        arm(g, 20, 48 + dy, 16, 56 + dy, WHL, t=4)
    else:
        arm(g, 20, 48 + dy, 16, 56 + dy, WHL, t=4)
        arm(g, 44, 48 + dy, 48, 56 + dy, WHL, t=4)
    # 大头
    g.ell(32, 27 + dy, 13.6, 12.6, SKIN)
    g.poly([(19, 27 + dy), (19, 14 + dy), (32, 11 + dy), (45, 14 + dy), (45, 27 + dy)], WHL)
    g.rect(19, 20 + dy, 26, 6, WHL)
    g.rect(19, 25 + dy, 26, 2, MINTL)             # 妹妹头的一条齐边
    g.rect(18, 22 + dy, 4, 10, WHL)
    g.rect(42, 22 + dy, 4, 10, WHL)
    g.rect(23, 15 + dy, 10, 2, MINTL)
    eyes(g, act)
    brows(g, "soft")
    mouth(g, act, "dot")
    cheeks(g, w=3, y=EYEY + 4)


STYLE_FN = {
    "dayu": draw_dayu, "dragon": draw_dragon, "claude": draw_claude,
    "gemini": draw_gemini, "qianwen": draw_qianwen, "grok": draw_grok,
    "doubao": draw_doubao,
}


def draw_frame(key, act):
    g = G()
    STYLE_FN[key](g, act, BOB.get(act, 0))
    g.outline(INK)
    return g


# --------------------------------------------------------------------------- 32x32 头像
def draw_face(key):
    g = G(VW, VH)
    s = SPECS[key]
    hair, hi = s["hair"], s["hairHi"]
    colL, colR = s["eyeL"], s["eyeR"]

    def face_base():
        g.ell(16, 17, 11, 10, SKIN)
        g.rect(14, 26, 5, 3, SKIN)

    def eyes32():
        g.rect(10, 17, 3, 4, colL)
        g.rect(19, 17, 3, 4, colR)
        g.set(10, 17, WHITE)
        g.set(19, 17, WHITE)
        g.rect(11, 23, 3, 1, INK)
        g.rect(18, 23, 3, 1, INK)
        g.rect(12, 24, 1, 1, INK)
        g.rect(17, 24, 1, 1, INK)
        g.rect(6, 21, 3, 2, CORAL)
        g.rect(23, 21, 3, 2, CORAL)

    if key == "dayu":
        g.poly([(24, 26), (30, 20), (28, 14), (23, 18)], WHD)
        g.poly([(28, 16), (31, 10), (26, 14)], WHALE)
        face_base()
        g.poly([(5, 16), (5, 6), (16, 3), (27, 6), (27, 16)], WHD)
        g.poly([(7, 8), (11, 2), (14, 8)], WHALE)
        g.poly([(17, 8), (21, 2), (24, 8)], WHALE)
        g.rect(7, 7, 10, 2, WHALE)
        g.rect(4, 12, 4, 14, WHD)
        g.rect(24, 12, 4, 14, WHD)
    elif key == "dragon":
        g.poly([(23, 22), (30, 16), (26, 26)], WHITE)
        face_base()
        for sx, bx in ((1, 24), (-1, 8)):
            g.poly([(bx, 8), (bx + sx, 1), (bx + sx * 4, 7)], HONEY)
            g.poly([(bx + sx, 7), (bx + sx * 2, 2), (bx + sx * 3, 7)], YEL)
        g.poly([(4, 15), (4, 5), (16, 2), (28, 5), (28, 15)], WHITE)
        g.rect(4, 5, 24, 6, WHITE)
        g.poly([(14, 10), (16, 15), (18, 10)], WHITE)
        g.rect(3, 10, 4, 16, WHITE)
        g.rect(25, 10, 4, 16, WHITE)
        g.rect(7, 5, 9, 2, MINTL)
    elif key == "claude":
        face_base()
        g.rect(4, 3, 24, 24, HONEY)
        g.rect(4, 3, 24, 1, YEL)
        g.rect(6, 12, 20, 15, HONEY)
        g.rect(5, 9, 22, 6, HONEY)
        g.rect(5, 14, 22, 1, HONEYPD)
        g.ell(16, 18, 9, 8, SKIN)
        for x in (6, 11, 20, 25):
            g.rect(x, 12, 2, 16, HONEY)
        g.rect(4, 12, 4, 14, HONEY)
        g.rect(24, 12, 4, 14, HONEY)
        g.rect(24, 3, 5, 1, CORAL)
        g.rect(24, 7, 5, 1, CORAL)
        g.set(25, 4, CORAL)
        g.set(28, 4, CORAL)
        g.set(25, 6, CORAL)
        g.set(28, 6, CORAL)
        g.rect(26, 4, 2, 3, CORAL)
        g.set(26, 5, WHITE)
        eyes32()
        return finish(g)
    elif key == "gemini":
        face_base()
        g.poly([(4, 15), (4, 5), (16, 2), (28, 5), (28, 15)], GRAPE)
        g.rect(4, 4, 7, 22, LAV)
        g.rect(21, 4, 7, 22, MINTL)
        g.rect(9, 8, 14, 6, GRAPE)
        g.poly([(13, 10), (16, 16), (19, 10)], GRAPE)
        g.star4(23, 6, 5, YEL)
    elif key == "qianwen":
        face_base()
        g.poly([(4, 15), (4, 5), (16, 2), (28, 5), (28, 15)], INK)
        g.ell(16, 3, 6, 4, INK)
        g.ell(16, 2, 4, 2, WHD)
        g.seg(8, 4, 25, 1, HONEY, t=2)
        g.rect(25, 1, 3, 3, CORAL)
        g.rect(4, 9, 4, 18, INK)
        g.rect(24, 9, 4, 18, INK)
        g.rect(7, 8, 18, 5, INK)
    elif key == "grok":
        for sx, bx in ((1, 26), (-1, 6)):
            g.poly([(bx - 3, 10), (bx + 3, 10), (bx + sx * 4, 26), (bx - sx, 24)], YEL)
            g.rect(bx - 3, 8, 7, 3, CORAL)
        face_base()
        g.poly([(5, 15), (5, 5), (16, 2), (27, 5), (27, 15)], YEL)
        g.poly([(9, 9), (12, 15), (14, 9)], HONEY)
        g.poly([(16, 9), (19, 15), (21, 9)], HONEY)
        g.rect(8, 4, 10, 2, HONEYPD)
    else:  # doubao
        g.rect(11, 1, 2, 5, MINT)
        g.ell(15, 2, 3, 2, MINT)
        g.ell(9, 3, 3, 2, MINTL)
        face_base()
        g.poly([(4, 16), (4, 5), (16, 2), (28, 5), (28, 16)], WHL)
        g.rect(4, 5, 24, 8, WHL)
        g.rect(4, 12, 24, 2, MINTL)
        g.rect(3, 11, 4, 14, WHL)
        g.rect(25, 11, 4, 14, WHL)
    if key != "claude":
        eyes32()
    return finish(g)


def finish(g):
    g.outline(INK)
    return g


# --------------------------------------------------------------------------- 落盘
def write_all():
    ART.mkdir(parents=True, exist_ok=True)
    RES.mkdir(parents=True, exist_ok=True)
    manifest = {}
    for key in SPECS:
        acts = ACTIONS[key]
        manifest[key] = {"cn": SPECS[key]["cn"], "note": SPECS[key]["note"],
                         "actions": acts, "frame": [FW, FH], "face": [VW, VH],
                         "levels": LEVELS_BY_KEY[key]}
        for act in acts:
            im = draw_frame(key, act).image()
            for d in (ART, RES):
                im.save(d / f"{key}_{act}.png", optimize=True)
        draw_face(key).image().save(ART / f"{key}_face.png", optimize=True)
        draw_face(key).image().save(RES / f"{key}_face.png", optimize=True)
    (ART / "personas_meta.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    return manifest


# --------------------------------------------------------------------------- 预览图
def scale(im, k):
    return im.resize((im.width * k, im.height * k), Image.NEAREST)


def blit(base, im, x, y, k=3):
    base.alpha_composite(scale(im, k), (x, y))


def backdrop(w, h, seed=20261031):
    """照 App.cs 的 MenuScenery 口径复刻一张海底底：八道渐变带 + 两道浪 + 上浮气泡"""
    from PIL import ImageDraw as ID
    im = Image.new("RGBA", (w, h))
    d = ID.Draw(im)
    bands = 8
    for i in range(bands):
        k = i / (bands - 1)
        c1, c2 = RGB[WHALE], RGB[WHD]
        col = tuple(int(round(c1[j] + (c2[j] - c1[j]) * k)) for j in range(3)) + (255,)
        d.rectangle((0, h * i // bands, w, h * (i + 1) // bands), fill=col)
    rnd = random.Random(seed)
    for _ in range(70):
        s = rnd.randint(3, 9)
        x, y = rnd.randint(0, w), rnd.randint(0, h)
        a = rnd.randint(24, 60)
        d.rectangle((x, y, x + s, y + s), fill=RGB[FOAM][:3] + (a,))
    # 两道浪
    for (yy, amp, th) in ((h - 60, 10, 24), (h - 34, 7, 16)):
        for x in range(0, w, 8):
            d.rectangle((x, int(yy + math.sin(x / 120.0) * amp), x + 8,
                         int(yy + amp + th)), fill=RGB[WHL][:3] + (170,))
    return im


def stage(manifest):
    """主菜单舞台合成预览：模拟一张 1600x1000 的底，把站位、前后景和尺寸档位一次摆出来。

    排版意图（给主 agent 接线用）：
      前排 scale 4（256x320）—— 两组互动：龙娘朝大肥鱼递单据 / claude娘 朝 gemini娘 递规矩纸条
      后排 scale 3（192x240）—— 站在上一层台板上：千问娘、豆包、grok娘（grok 朝豆包递黑卡）
      底栏 scale 2 —— 头像 32x32 与立绘 64x80 的原尺寸档位，题栏/对话框照这档挑
    """
    W_, H_ = 1600, 1000
    base = backdrop(W_, H_)
    d = ImageDraw.Draw(base)
    DARK = RGB[INK][:3] + (255,)
    LIGHT = RGB[FOAM][:3] + (255,)

    # 主菜单面板：比现在的 760x486 大一档（这里示意 1400x700），标题/按钮/进度条留给 App.cs 写字
    px, py, pw, ph = 100, 64, 1400, 700
    d.rectangle((px, py, px + pw, py + ph), fill=RGB[INK][:3] + (90,))
    d.rectangle((px + 4, py + 4, px + pw - 4, py + ph - 4), fill=RGB[WHITE][:3] + (246,))
    d.rectangle((px + 4, py + 4, px + pw - 4, py + 30), fill=RGB[HONEY][:3] + (235,))
    d.text((px + 16, py + 10), "menu title / buttons / progress  <-  text drawn by App.cs bitmap font",
           fill=DARK)
    # 两层台板：后排站在上层，前排站在下层，脚线错开才不会互相糊
    shelf_y = py + 372
    floor_y = py + ph - 26
    d.rectangle((px + 12, shelf_y, px + pw - 12, shelf_y + 5), fill=RGB[SAND][:3] + (255,))
    d.rectangle((px + 12, floor_y, px + pw - 12, floor_y + 6), fill=RGB[MINTL][:3] + (255,))

    def put(key, act, x, top, k, label, dark=True):
        im = draw_frame(key, act).image()
        blit(base, im, x, top, k)
        d.text((x + 2, top + im.height * k + 3), label, fill=DARK if dark else LIGHT)
        return x + im.width * k

    # —— 后排（scale 3，192x240）：脚踩上层台板
    back_top = shelf_y - 240
    put("qianwen", "idle", px + 150, back_top, 3, "qianwen idle")
    put("doubao", "wave", px + 560, back_top, 3, "doubao wave")
    put("grok", "talk", px + 820, back_top, 3, "grok talk -> doubao")
    # —— 前排（scale 4，256x320）：脚踩下层地板
    front_top = floor_y - 320
    put("dayu", "idle", px + 60, front_top, 4, "dayu idle")
    put("dragon", "talk", px + 330, front_top, 4, "dragon talk -> dayu")
    put("gemini", "wave", px + 700, front_top, 4, "gemini wave")
    put("claude", "talk", px + 970, front_top, 4, "claude talk -> gemini")

    # —— 底栏：原尺寸档位（头像 scale 2 / 立绘 scale 2），主菜单之外题栏与对话框用
    keys = ["dayu", "dragon", "claude", "gemini", "qianwen", "grok", "doubao"]
    y0 = 800
    d.text((40, y0 - 18), "faces 32x32 @x2", fill=LIGHT)
    for i, key in enumerate(keys):
        x = 40 + i * 76
        blit(base, draw_face(key).image(), x, y0, 2)
        d.text((x, y0 + 70), key, fill=LIGHT)
    d.text((600, y0 - 18), "frames 64x80 @x2", fill=LIGHT)
    for i, key in enumerate(keys):
        x = 600 + i * 118
        blit(base, draw_frame(key, "idle").image(), x, y0 - 10, 2)
        d.text((x, y0 + 156), key, fill=LIGHT)
    x = 600 + 7 * 118
    blit(base, draw_frame("dayu", "eat").image(), x, y0 - 10, 2)
    d.text((x, y0 + 156), "dayu eat", fill=LIGHT)
    base.save(EV / "personas_stage.png", optimize=True)


def sheet(manifest):
    cols = 5
    rows = len(SPECS)
    k = 2
    cw, ch = FW * k + 12, FH * k + 34
    vw = VW * k
    w = cols * cw + 24
    h = rows * ch + 70
    base = Image.new("RGBA", (w, h), RGB[FOAM][:3] + (255,))
    d = ImageDraw.Draw(base)
    # 透明底用棋盘格，方便肉眼确认没有脏边
    for y in range(h):
        for x in range(w):
            if (x // 16 + y // 16) % 2:
                d.point((x, y), fill=RGB[WHITE][:3] + (255,))
    d.text((12, 8), "personas sheet  x2  |  col0 = face 32x32, cols 1..4 = 64x80 frames (idle/wave/talk[/eat])",
           fill=RGB[INK][:3] + (255,))
    top = 30
    for r, key in enumerate(SPECS):
        acts = ACTIONS[key]
        y = top + r * ch
        d.text((12, y - 12), f"{key}  {SPECS[key]['cn']}", fill=RGB[WHD][:3] + (255,))
        face = draw_face(key).image()
        blit(base, face, 12, y + 14, k)
        d.text((12, y + 14 + VH * k + 2), "face", fill=RGB[INK][:3] + (255,))
        for c, act in enumerate(acts):
            im = draw_frame(key, act).image()
            x = 12 + (c + 1) * cw
            blit(base, im, x, y + 14, k)
            d.text((x, y + 14 + FH * k + 2), act, fill=RGB[INK][:3] + (255,))
    base.save(EV / "personas_sheet.png", optimize=True)


def main():
    # 第十八轮玩家明令「以后不要再自己画角色，必须用网上搜集的现成素材」。
    # 六家 AI 的立绘已换成 ZipZipPipe 的第三方同人件（登记见 ArtInbox/SOURCES.md §十），
    # 这套自绘件只剩历史参考价值。留着不加闸门的话，误跑一次就会把游戏里的立绘刷回旧样式，
    # 所以要跑必须显式带 --legacy。
    if "--legacy" not in sys.argv:
        print("[gen_personas] 已停用：角色立绘改用 tools/gen_personas_from_photo.py（第三方素材转换）。"
              "确实要重跑这批自绘件，加 --legacy。")
        return 1
    EV.mkdir(parents=True, exist_ok=True)
    man = write_all()
    n = sum(len(v["actions"]) + 1 for v in man.values())
    print(f"[gen_personas] {len(man)} 个角色 / {n} 张 PNG 已写入 源={ART}  Resources={RES}")
    stage(man)
    sheet(man)
    print(f"[gen_personas] 预览：{EV / 'personas_stage.png'}  {EV / 'personas_sheet.png'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
