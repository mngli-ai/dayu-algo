"""真·鼠标键盘输入跑 Build/dayu.exe，用来验收只有真实 EventSystem 命中才会触发的交互。

用法：
  python tools/real_input.py --scenario tools/scenarios/marquee.json --tag marquee

坐标一律用客户区归一化比例 (0..1)，避免写死像素。
每一步都会往 stdout 打日志，结束时把 Player.log 里带 [交互] 的行原样贴出来，
再按 scenario 里的 shot 名单抓窗口位图。
"""
import argparse
import ctypes
import json
import os
import re
import subprocess
import sys
import time
from ctypes import wintypes

from PIL import Image

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
# 不设 DPI aware 时 GetClientRect/SetCursorPos 会被系统虚拟化，鼠标点会飘移到别的窗口上
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    user32.SetProcessDPIAware()

user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.PostMessageW.restype = wintypes.BOOL
user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
user32.GetWindowDC.argtypes = [wintypes.HWND]
user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
# hwndInsertAfter 传 -1/-2 这两个特殊句柄，argtypes 不设成 HWND 会被零扩展成 0x00000000FFFFFFFF，系统当成无效句柄
user32.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND,
                                ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT]
user32.SetWindowPos.restype = wintypes.BOOL

MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
MOUSEEVENTF_RIGHTDOWN = 0x0008
MOUSEEVENTF_RIGHTUP = 0x0010
MOUSEEVENTF_MIDDLEDOWN = 0x0020
MOUSEEVENTF_MIDDLEUP = 0x0040
MOUSEEVENTF_WHEEL = 0x0800

VK = {
    "ctrl": 0x11, "shift": 0x10, "alt": 0x12,
    "a": 0x41, "b": 0x42, "c": 0x43, "d": 0x44, "e": 0x45, "f": 0x46, "g": 0x47,
    "h": 0x48, "i": 0x49, "j": 0x4A, "k": 0x4B, "l": 0x4C, "m": 0x4D, "n": 0x4E,
    "o": 0x4F, "p": 0x50, "q": 0x51, "r": 0x52, "s": 0x53, "t": 0x54, "u": 0x55,
    "v": 0x56, "w": 0x57, "x": 0x58, "y": 0x59, "z": 0x5A,
    "0": 0x30, "1": 0x31, "2": 0x32, "3": 0x33, "4": 0x34, "5": 0x35,
    "6": 0x36, "7": 0x37, "8": 0x38, "9": 0x39,
    "del": 0x2E, "enter": 0x0D, "esc": 0x1B, "tab": 0x09, "space": 0x20, "back": 0x08,
    "=": 0xBB, "-": 0xBD, "[": 0xDB, "]": 0xDD, ",": 0xBC, ".": 0xBE, "/": 0xBF, ";": 0xBA,
    "left": 0x25, "up": 0x26, "right": 0x27, "down": 0x28,
    # F9：游戏里「把当前所有卡片坐标+尺寸倒进日志」的手动闸门，拖动/撤销前后各按一次做对比
    "f9": 0x78,
}

# 键盘上带 E0 前缀的那批键，注入时必须打扩展键标志
EXTENDED_VK = {0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E}

PLAYER_LOG = os.path.expandvars(r"%APPDATA%\..\LocalLow\DefaultCompany\dayu-algo\Player.log")


class Window:
    def __init__(self, hwnd):
        self.hwnd = hwnd

    def client_rect(self):
        r = wintypes.RECT()
        user32.GetClientRect(self.hwnd, ctypes.byref(r))
        p = wintypes.POINT(r.left, r.top)
        user32.ClientToScreen(self.hwnd, ctypes.byref(p))
        q = wintypes.POINT(r.right, r.bottom)
        user32.ClientToScreen(self.hwnd, ctypes.byref(q))
        return p.x, p.y, q.x - p.x, q.y - p.y

    def to_screen(self, nx, ny):
        x, y, w, h = self.client_rect()
        return int(x + nx * w), int(y + ny * h)


def find_window(title_sub, timeout=40.0, pid=None):
    """按标题找窗口。桌面上可能同时开着好几个 dayu-algo（上一轮没关的旧实例），
    只按标题匹配会拍到旧进程的画面上——传 pid 就只认本脚本刚拉起的那个进程。"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        found = []

        @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
        def enum_proc(h, lparam):
            if not user32.IsWindowVisible(h):
                return True
            if pid is not None:
                wpid = wintypes.DWORD()
                user32.GetWindowThreadProcessId(h, ctypes.byref(wpid))
                if wpid.value != pid:
                    return True
            n = user32.GetWindowTextLengthW(h)
            buf = ctypes.create_unicode_buffer(n + 1)
            user32.GetWindowTextW(h, buf, n + 1)
            if title_sub.lower() in buf.value.lower():
                found.append((h, buf.value))
            return True

        user32.EnumWindows(enum_proc, 0)
        if found:
            if pid is not None:
                print(f"[win] pid={pid} 命中 {len(found)} 个同名窗口，取第一个 hwnd={found[0][0]}")
            return Window(found[0][0])
        time.sleep(0.5)
    return None


def minimize_others(title_sub, keep_pid):
    """桌面上还开着上一轮没关的同名窗口时，新窗口被压在下面，PrintWindow 拍到的是全黑。
    把别的同名窗口最小化（跑完再还原），不去关它们，玩家没存的进度一律保留。"""
    out = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
    def enum_proc(h, lparam):
        if not user32.IsWindowVisible(h) or user32.IsIconic(h):
            return True
        wpid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(h, ctypes.byref(wpid))
        if wpid.value == keep_pid:
            return True
        n = user32.GetWindowTextLengthW(h)
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(h, buf, n + 1)
        if title_sub.lower() in buf.value.lower():
            user32.ShowWindow(h, 6)   # SW_MINIMIZE
            out.append(h)
        return True

    user32.EnumWindows(enum_proc, 0)
    if out:
        print(f"[win] 先把 {len(out)} 个旧的窗口缩到任务栏（跑完还原）：{out}")
    return out


def restore_windows(handles):
    for h in handles:
        user32.ShowWindow(h, 9)      # SW_RESTORE
    if handles:
        print(f"[win] 已还原 {len(handles)} 个旧窗口")


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long), ("biHeight", ctypes.c_long),
                ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD),
                ("biCompression", wintypes.DWORD), ("biSizeImage", wintypes.DWORD),
                ("biXPelsPerMeter", ctypes.c_long), ("biYPelsPerMeter", ctypes.c_long),
                ("biClrUsed", wintypes.DWORD), ("biClrImportant", wintypes.DWORD)]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


def _dib_to_image(mem, bmp, w, h):
    bmi = BITMAPINFO()
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = w
    bmi.bmiHeader.biHeight = -h          # 负值 = 自上而下的行序
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    buf = ctypes.create_string_buffer(w * h * 4)
    got = gdi32.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(bmi), 0)
    return Image.frombytes("RGBX", (w, h), buf.raw, "raw", "BGRX").convert("RGB"), got


def _win_rect(win):
    r = wintypes.RECT()
    user32.GetWindowRect(win.hwnd, ctypes.byref(r))
    return r.left, r.top, r.right - r.left, r.bottom - r.top


def _nudge(win):
    """Unity 的 D3D 后端在窗口被移动过之前会一直打
    "IDXGISwapChain::GetFrameStatistics is broken ... until the game window is moved"，
    此时 PrintWindow 拍到的是全黑。原地挪 1 像素再挪回来即可解锁，位置对玩家没影响。"""
    r = wintypes.RECT()
    user32.GetWindowRect(win.hwnd, ctypes.byref(r))
    for dx in (1, 0):
        user32.SetWindowPos(win.hwnd, 0, r.left + dx, r.top + dx, 0, 0,
                           0x0001 | 0x0004 | 0x0020)   # NOSIZE | NOZORDER | NOACTIVATE
        time.sleep(0.25)


def _grab(win, w, h):
    hwnd_dc = user32.GetWindowDC(win.hwnd)
    mem = gdi32.CreateCompatibleDC(hwnd_dc)
    bmp = gdi32.CreateCompatibleBitmap(hwnd_dc, w, h)
    gdi32.SelectObject(mem, bmp)
    # PW_RENDERFULLCONTENT = 2：Unity 用 DX 呈现，普通 BitBlt 会抓到黑屏
    user32.PrintWindow(win.hwnd, mem, 2)
    img, got = _dib_to_image(mem, bmp, w, h)
    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mem)
    user32.ReleaseDC(win.hwnd, hwnd_dc)
    return img, got


def _bright(img):
    """画面里最亮的通道的最大值：全黑=0，用来判断这次抓的到底是不是空帧。"""
    return max(ch[1] for ch in img.getextrema())


def capture(win, path):
    cx, cy, w, h = win.client_rect()
    if w <= 0 or h <= 0:
        # 客户区 0x0 = 窗口已经不在了。最常见的原因是把 -shot 当成开关写在参数中间：
        # 播放器里 -shot 后面跟的那个字符串是截图文件名，它会顺手 Application.Quit()。
        raise SystemExit(f"[fail] 窗口 {win.hwnd} 客户区是 {w}x{h}，播放器多半已退出。"
                         f"检查场景 args：-shot 必须留在末尾且后面跟文件名，否则会被当成开关并触发退出")
    wx, wy, ww, wh = _win_rect(win)
    # PrintWindow 拍的是整扇窗口（含标题栏），塞进 1600x1000 的缓冲里等于把画面整体下移、
    # 底部被裁掉。按窗口尺寸开缓冲再裁出客户区，图上的像素才能和归一化坐标一一对上。
    off_x, off_y = cx - wx, cy - wy
    for attempt in range(4):
        img, got = _grab(win, ww, wh)
        img = img.crop((off_x, off_y, off_x + w, off_y + h))
        if _bright(img) >= 6:
            break
        # 黑帧 = 播放器失焦后挂起、没往 DWM 交新帧。抢回前台再试；
        # 绝不退化成「拍屏幕上这块区域」——那块区域此刻是浏览器，拍回来的是别人的窗口。
        focus(win)
        _nudge(win)
        time.sleep(0.8)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    print(f"[shot] {path} ({w}x{h}) PrintWindow GetDIBits={got} 亮度={_bright(img)}")


class Driver:
    """两套投递方式：
    cursor = 真的动系统光标（会和前台游戏抢鼠标，慎用）
    post   = 往窗口消息队列塞 WM_MOUSEMOVE / WM_*BUTTONDOWN（不动光标、不抢焦点）
    """
    def __init__(self, win, mode="post"):
        self.win = win
        self.mode = mode
        self.x = self.y = 0
        self.buttons = 0

    def _apply(self, nx, ny):
        cx, cy, w, h = self.win.client_rect()
        self.x, self.y = int(nx * w), int(ny * h)
        if self.mode == "cursor":
            sx, sy = self.win.to_screen(nx, ny)
            user32.SetCursorPos(sx, sy)

    def move(self, nx, ny):
        self._apply(nx, ny)
        if self.mode == "post":
            user32.PostMessageW(self.win.hwnd, 0x0200, self.buttons, self.lparam())

    def lparam(self):
        return (self.y << 16) | (self.x & 0xFFFF)

    def down(self, button="left"):
        self.buttons |= {"left": 1, "right": 2, "middle": 4}[button]
        if self.mode == "cursor":
            user32.mouse_event({"left": MOUSEEVENTF_LEFTDOWN, "right": MOUSEEVENTF_RIGHTDOWN,
                                "middle": MOUSEEVENTF_MIDDLEDOWN}[button], 0, 0, 0, 0)
        else:
            msg = {"left": 0x0201, "right": 0x0204, "middle": 0x0207}[button]
            user32.PostMessageW(self.win.hwnd, msg, self.buttons, self.lparam())

    def up(self, button="left"):
        self.buttons &= ~{"left": 1, "right": 2, "middle": 4}[button]
        if self.mode == "cursor":
            user32.mouse_event({"left": MOUSEEVENTF_LEFTUP, "right": MOUSEEVENTF_RIGHTUP,
                                "middle": MOUSEEVENTF_MIDDLEUP}[button], 0, 0, 0, 0)
        else:
            msg = {"left": 0x0202, "right": 0x0205, "middle": 0x0208}[button]
            user32.PostMessageW(self.win.hwnd, msg, self.buttons, self.lparam())

    def wheel(self, amount, notches=1):
        if self.mode == "cursor":
            user32.mouse_event(MOUSEEVENTF_WHEEL, 0, 0, ctypes.c_uint(amount * 120).value, 0)
        else:
            user32.PostMessageW(self.win.hwnd, 0x020A, ctypes.c_uint(amount * 120).value, self.lparam())

    def key(self, name, hold=0.06):
        vk = VK[name.lower()]
        if self.mode == "cursor":
            # 方向键 / Home / End / Ins / Del / PgUp / PgDn 是「扩展键」：keybd_event 不带
            # KEYEVENTF_EXTENDEDKEY 发出去，游戏里 Input.GetKeyDown 收不到（第一次拿方向键做验收时踩到）
            ext = 0x0001 if vk in EXTENDED_VK else 0
            user32.keybd_event(vk, 0, ext, 0)
            time.sleep(hold)
            user32.keybd_event(vk, 0, 2 | ext, 0)
        else:
            user32.PostMessageW(self.win.hwnd, 0x0100, vk, 0)   # WM_KEYDOWN
            time.sleep(hold)
            user32.PostMessageW(self.win.hwnd, 0x0101, vk, 0)   # WM_KEYUP
        time.sleep(0.02)

    def mod(self, name, down_flag):
        vk = VK[name.lower()]
        if self.mode == "cursor":
            user32.keybd_event(vk, 0, 0 if down_flag else 2, 0)
        else:
            user32.PostMessageW(self.win.hwnd, 0x0100 if down_flag else 0x0101, vk, 0)


def ensure_fore(win):
    if user32.GetForegroundWindow() == win.hwnd:
        return True
    print("[focus] 前台被别的窗口抢走，抢回来再继续")
    return focus(win)


def run_steps(drv, win, steps, shots_dir, tag):
    for i, st in enumerate(steps):
        kind = st.get("do")
        if drv.mode == "cursor" and kind in ("move", "drag", "click", "double", "clickrect", "wheel", "key", "shot"):
            # 播放器一失焦就挂起主循环：点击不落地、PrintWindow 拍到黑帧。
            # 只在确实丢了前台时才抢，否则每一步都会把用户的窗口挤下去。
            ensure_fore(win)
        if kind == "wait":
            time.sleep(float(st.get("sec", 1.0)))
        elif kind == "waitlog":
            # 游戏从进程启动到第一帧要几秒，每次冷启动还不一样；用固定 wait 去卡
            # 一段 0.22 秒的补间必然对不上。让程序自己往日志里报「这个状态已经稳了」，
            # 截图等到那一行出现才拍，时序就跟机器快慢无关了。
            # optional：探测类步骤（「这个点到底收不收得到鼠标事件」）要一次跑完好几个候选点，
            # 中间某次没等到是结果而不是故障，不能让整个场景在这里断掉。
            if not wait_log(st["marker"], float(st.get("timeout", 90.0))):
                if st.get("optional"):
                    print(f"[waitlog] 没等到（可选，继续）：{st['marker']}")
                else:
                    raise SystemExit(f"等不到日志标记：{st['marker']}（游戏可能崩了或参数没生效）")
        elif kind == "move":
            drv.move(*st["at"])
            time.sleep(float(st.get("sec", 0.05)))
        elif kind == "drag":
            if "from" in st:
                a = st["from"]
            else:
                # 起点从日志里最近一条 [rect] 量出来，再按 dx/dy 挪开：
                # 卡片正中是白色表达式框（只选中不拖动），要拖整张卡必须落在标题带上，
                # 而标题带在哪只有游戏自己知道。
                at = last_rect(st["label"])
                if at is None:
                    raise SystemExit(f"日志里找不到 [rect] {st['label']}，前面缺一次 F9 测量")
                a = [at[0] + float(st.get("dx", 0.0)), at[1] + float(st.get("dy", 0.0))]
                print(f"[drag] 起点取自 {st['label']} → ({a[0]:.4f}, {a[1]:.4f})")
            b = st["to"]
            drv.move(*a)
            time.sleep(0.08)
            drv.down(st.get("button", "left"))
            time.sleep(0.08)
            n = int(st.get("steps", 12))
            for k in range(1, n + 1):
                t = k / n
                drv.move(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
                time.sleep(float(st.get("slice", 0.03)))
                if st.get("shot_mid") and k == n // 2:
                    capture(win, os.path.join(shots_dir, f"{tag}-{i:02d}-{st['shot_mid']}.png"))
            # hold：拖到终点按住不放若干秒。验「拖到画布边缘镜头自动平移」必须在
            # 松手前给 StepEdgePan 留出帧数，一到位就松手等于没测。
            hold = float(st.get("hold", 0.0))
            if hold > 0.0:
                if st.get("shot_hold"):
                    time.sleep(hold * .5)
                    capture(win, os.path.join(shots_dir, f"{tag}-{i:02d}-{st['shot_hold']}.png"))
                time.sleep(hold * .5)
            drv.up(st.get("button", "left"))
            time.sleep(float(st.get("after", 0.25)))
        elif kind == "click":
            drv.move(*st["at"])
            time.sleep(0.05)
            drv.down(st.get("button", "left"))
            time.sleep(0.05)
            drv.up(st.get("button", "left"))
            time.sleep(float(st.get("after", 0.2)))
        elif kind == "double":
            drv.move(*st["at"])
            time.sleep(0.05)
            for _ in range(2):
                drv.down(st.get("button", "left"))
                time.sleep(0.04)
                drv.up(st.get("button", "left"))
                time.sleep(0.04)
            time.sleep(float(st.get("after", 0.3)))
        elif kind == "wheel":
            drv.move(*st.get("at", [0.5, 0.5]))
            time.sleep(0.05)
            if st.get("ctrl"):
                drv.mod("ctrl", True)
            for _ in range(int(st.get("notches", 3))):
                drv.wheel(int(st.get("amount", 1)))
                time.sleep(0.05)
            if st.get("ctrl"):
                drv.mod("ctrl", False)
            time.sleep(0.2)
        elif kind == "key":
            mods = st.get("with", [])
            for m in mods:
                drv.mod(m, True)
            drv.key(st["name"])
            for m in reversed(mods):
                drv.mod(m, False)
            time.sleep(float(st.get("after", 0.25)))
        elif kind == "clickrect":
            # 先让游戏 F9 把自己的矩形打进日志，再按量出来的点下鼠标：
            # 题栏滚动后按钮位置会变，写死坐标这一轮点得到、下一轮就点空。
            at = last_rect(st["label"])
            if at is None:
                raise SystemExit(f"日志里找不到 [rect] {st['label']}，前面缺一次 F9 测量")
            # dx/dy：卡片行量的是左上角，正好落在边角上（右键命中卡片、左键拖标题带都要往内挪几步）
            at = [at[0] + float(st.get("dx", 0.0)), at[1] + float(st.get("dy", 0.0))]
            print(f"[clickrect] {st['label']} → ({at[0]:.4f}, {at[1]:.4f})")
            drv.move(*at)
            time.sleep(0.05)
            btn = st.get("button", "left")
            for _ in range(2 if st.get("double") else 1):
                drv.down(btn)
                time.sleep(0.05)
                drv.up(btn)
                time.sleep(0.05)
            time.sleep(float(st.get("after", 0.3)))
        elif kind == "max":
            user32.ShowWindow(win.hwnd, 3)   # SW_MAXIMIZE：模拟玩家点标题栏的放大按钮
            time.sleep(float(st.get("sec", 1.5)))
        elif kind == "restore":
            user32.ShowWindow(win.hwnd, 9)   # SW_RESTORE
            time.sleep(float(st.get("sec", 1.0)))
        elif kind == "shot":
            capture(win, os.path.join(shots_dir, f"{tag}-{i:02d}-{st['name']}.png"))
        else:
            raise SystemExit(f"未知步骤：{json.dumps(st, ensure_ascii=False)}")


# Player.log 每次启动都被播放器重写，所以整份文件就是这一次运行的日志。
# 但同一个标记可能要等第二次、第三次出现（例如两次双击都要看聚焦日志），
# 所以记一个消费位点：每次等到标记就把位点推到文件末尾，下一次只读新写进来的部分。
LOG_LINE = 0


def reset_log_pos():
    global LOG_LINE
    LOG_LINE = 0


def wait_log(marker, timeout=90.0):
    """轮询玩家日志，等 marker 出现。命中返回 True，超时返回 False。
    位点按「行」推进而不是跳到文件末尾：一次拖拽会连着写 [drag] 起手 / 松手 / [add] 三行，
    按文件末尾推进会把命中行之后的新行整段跳过，后面等第二个标记必然超时（上一轮就是这么误判成「撤销没生效」）。"""
    global LOG_LINE
    deadline = time.time() + timeout
    while time.time() < deadline:
        lines = tail_player_log()
        for i in range(LOG_LINE, len(lines)):
            if marker in lines[i]:
                LOG_LINE = i + 1
                print(f"[waitlog] 等到：{marker}")
                return True
        time.sleep(0.2)
    return False


def tail_player_log(needle=None, since=0):
    try:
        with open(PLAYER_LOG, "r", encoding="utf-8", errors="replace") as f:
            f.seek(max(0, since))
            lines = f.read().splitlines()
    except OSError as exc:
        print(f"[log] 读不到 Player.log：{exc}")
        return []
    out = [ln for ln in lines if (needle is None or needle in ln)]
    return out


def last_rect(label):
    """取日志里最近一行「[rect] <label> … 归一化(左上)=(x, y)」，返回归一化落点。
    题栏里的按钮位置随滚动变（答案按钮在内容最底下），写死坐标必然点空；
    而画布卡片的行是「[rect] <类型> <uid> 归一化(左上)=…」，中间夹了 uid，
    所以匹配只比「[rect] <label> 」这个前缀。
    让游戏自己 F9 把当前矩形打进日志，脚本读回来再点，量一次点一次。"""
    lines = tail_player_log(f"[rect] {label} ")
    if not lines:
        return None
    m = re.search(r"\((-?[\d.]+),\s*(-?[\d.]+)\)", lines[-1])
    if not m:
        return None
    return float(m.group(1)), float(m.group(2))


HWND_TOPMOST, HWND_NOTOPMOST = -1, -2
SWP_FLAGS = 0x0001 | 0x0002 | 0x0040   # NOSIZE | NOMOVE | SHOWWINDOW


def set_topmost(win, on):
    """微信这类常驻聊天窗是 WS_EX_TOPMOST，BringWindowToTop 打不过它，
    cursor 模式下光标挪过去了点击却落在别人身上。验收期间把播放器自己也置成 topmost，
    跑完再摘掉 —— 只改这一个窗口的层级，不动用户其他窗口。"""
    user32.SetWindowPos(win.hwnd, HWND_TOPMOST if on else HWND_NOTOPMOST, 0, 0, 0, 0, SWP_FLAGS)


def focus(win):
    """后台进程抢前台要靠先按一次 Alt，否则 SetForegroundWindow 会被系统挡掉。"""
    hwnd = win.hwnd
    user32.ShowWindow(hwnd, 9)   # SW_RESTORE
    user32.BringWindowToTop(hwnd)
    user32.keybd_event(0x12, 0, 0, 0)   # VK_MENU down
    user32.keybd_event(0x12, 0, 2, 0)   # VK_MENU up
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.35)
    if user32.GetForegroundWindow() != hwnd:
        # 微信这类常驻聊天窗会立刻抢回去，SetForegroundWindow 挡不住；
        # SwitchToThisWindow 是给「Alt+Tab 那类切换器」用的，不受前台锁限制，能压过去。
        user32.SwitchToThisWindow(hwnd, 1)
        time.sleep(0.35)
        user32.SetForegroundWindow(hwnd)
        time.sleep(0.2)
    fg = user32.GetForegroundWindow()
    n = user32.GetWindowTextLengthW(fg)
    buf = ctypes.create_unicode_buffer(n + 1)
    user32.GetWindowTextW(fg, buf, n + 1)
    p = wintypes.POINT()
    user32.GetCursorPos(ctypes.byref(p))
    print(f"[focus] foreground={buf.value!r} self={hwnd == fg} cursor=({p.x},{p.y})")
    return hwnd == fg


def verify_cursor(drv, win, nx, ny):
    drv.move(nx, ny)
    time.sleep(0.08)
    p = wintypes.POINT()
    user32.GetCursorPos(ctypes.byref(p))
    tx, ty = win.to_screen(nx, ny)
    print(f"[cursor] 目标=({tx},{ty}) 实际=({p.x},{p.y})")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scenario", required=True)
    ap.add_argument("--tag", default="real")
    ap.add_argument("--exe", default="Build/dayu.exe")
    ap.add_argument("--title", default="dayu-algo")
    ap.add_argument("--shots", default="Temp/shots")
    ap.add_argument("--filter", default="[交互]")
    ap.add_argument("--mode", default="post", choices=["post", "cursor"],
                    help="post=只往窗口塞消息（不动光标）；cursor=真动系统光标，会和前台程序抢鼠标")
    ap.add_argument("--keep", action="store_true", help="跑完不关窗口")
    args = ap.parse_args()

    scen = json.load(open(args.scenario, encoding="utf-8"))
    extra = scen.get("args", [])
    cmd = [os.path.abspath(args.exe)] + extra
    log_offset = os.path.getsize(PLAYER_LOG) if os.path.exists(PLAYER_LOG) else 0

    proc = subprocess.Popen(cmd)
    reset_log_pos()      # 新进程会重写 Player.log，位点必须回到 0
    win = None
    minimized = []
    try:
        win = find_window(args.title, pid=proc.pid)
        if win is None:
            print("[fail] 没找到窗口")
            return 2
        minimized = minimize_others(args.title, proc.pid)
        if args.mode == "cursor":
            set_topmost(win, True)
            # 微信之类的程序会抢前台，一次没抢到就多试几次，否则鼠标事件全发给别的窗口
            for _ in range(8):
                if focus(win):
                    break
                time.sleep(0.4)
            if user32.GetForegroundWindow() != win.hwnd:
                # 抢不到前台就绝不能继续：cursor 模式下的点击会原样落进别人（比如微信）的窗口里
                raise SystemExit("[fail] 抢不到前台，已中止 —— 一个鼠标事件都没发出去")
        time.sleep(0.6)
        print(f"[win] client={win.client_rect()} mode={args.mode}")
        drv = Driver(win, args.mode)
        if args.mode == "cursor":
            verify_cursor(drv, win, 0.5, 0.5)
        run_steps(drv, win, scen["steps"], args.shots, args.tag)
        time.sleep(0.4)
        capture(win, os.path.join(args.shots, f"{args.tag}-99-final.png"))
    finally:
        if win is not None:
            set_topmost(win, False)
        if not args.keep and win is not None:
            user32.PostMessageW(win.hwnd, 0x0010, 0, 0)  # WM_CLOSE
            try:
                proc.wait(timeout=25)
            except subprocess.TimeoutExpired:
                proc.kill()
        restore_windows(minimized)
        time.sleep(0.5)

    print("---- Player.log ----")
    lines = tail_player_log(args.filter)
    for ln in lines:
        print(ln)
    if not lines:
        print(f"(没有包含 {args.filter} 的日志行)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
