"""程序化合成教学反馈音，写到 Assets/Resources/Audio/*.wav。

零外部素材：全部由 numpy 用方波/三角波/白噪声 + A-D 包络拼出来，
跟 tools/gen_art.py 画像素图是同一套口径（生成物入库、源件不入库）。

用法：
    python tools/gen_sfx.py           # 生成全部 cue
    python tools/gen_sfx.py --check   # 只校验不落盘（时长预算/峰值/DC/磁盘与名单一致）
"""
from __future__ import annotations
import argparse, math, sys, wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Resources" / "Audio"

SR = 44100
BITS = 16
MAX_DUR = 0.6          # 单条 cue 的时长预算：这些音一帧能放好几条，长了就是噪音不是反馈
CEILING = 0.8          # 峰值上限，留 20% 余量给 Unity 侧的多路叠加
TOTAL_BUDGET = 620_000  # 全部 cue 加起来的体积上限（会进游戏包）
HARMONICS = 20         # 带限方波的谐波个数上限：再多就糊到高频刺耳了
FADE_IN = 64           # 起始 64 个样本线性抬起，杜绝开头那一下爆音
FADE_OUT = 140

# (起振秒, 释音秒)：释音按时长等比缩放，所以同一个包络名在任何 dur 下都是同一性格
ENVELOPES = {
    "click": (0.0008, 0.55),
    "stacc": (0.0018, 0.62),
    "pluck": (0.0035, 0.78),
    "soft": (0.014, 0.9),
    "swell": (0.034, 0.85),
    "hold": (0.006, 1.0),
}

NOTE = {"C4": 261.63, "D4": 293.66, "E4": 329.63, "F4": 349.23, "G4": 392.00, "A4": 440.00,
        "B4": 493.88, "C5": 523.25, "D5": 587.33, "E5": 659.25, "G5": 783.99, "A5": 880.00,
        "C6": 1046.50, "E6": 1318.51, "G6": 1567.98}


def nsamp(dur: float) -> int:
    return max(1, int(round(dur * SR)))


def envelope(n: int, name: str) -> np.ndarray:
    att, rel = ENVELOPES[name]
    e = np.zeros(n)
    na = max(1, min(n - 1, int(round(att * SR))))
    e[:na] = np.linspace(0.0, 1.0, na)
    m = n - na
    if m > 0:
        t = np.arange(1, m + 1) / m
        e[na:] = np.exp(-5.0 * rel * t)
    return e


def _freq_ramp(f0: float, f1: float, n: int) -> np.ndarray:
    if n == 1:
        return np.array([f0])
    t = np.arange(n) / (n - 1)
    return f0 * (f1 / f0) ** t          # 等比滑音：按半音感均匀，比线性滑更不突兀


def _phases(freq) -> np.ndarray:
    # 相位用累积频率积分，滑音途中才不会跳相位（直接算 sin(2pi f t) 会在转折处炸出咔声）
    return 2 * np.pi * np.cumsum(np.asarray(freq, dtype=float)) / SR


def square(freq: float, dur: float, env: str = "pluck", duty: float = 0.5, glide: float | None = None) -> np.ndarray:
    """带限方波：duty 走脉宽谐波系数 sin(pi*k*d)/(pi*k)，只叠到奈奎斯特以下。"""
    n = nsamp(dur)
    f = np.full(n, float(freq)) if glide is None else _freq_ramp(freq, glide, n)
    ph = _phases(f)
    flow = min(freq, glide if glide is not None else freq)
    kmax = max(1, min(HARMONICS, int(SR * 0.45 / max(20.0, flow))))
    x = np.zeros(n)
    for k in range(1, kmax + 1):
        c = math.sin(math.pi * k * duty) / (math.pi * k)
        if abs(c) < 1e-9:
            continue
        x += c * np.sin(k * ph)
    p = float(np.max(np.abs(x)))
    if p > 0:
        x = x / p
    return x * envelope(n, env)


def tri(freq: float, dur: float, env: str = "pluck", glide: float | None = None) -> np.ndarray:
    n = nsamp(dur)
    f = np.full(n, float(freq)) if glide is None else _freq_ramp(freq, glide, n)
    x = (2.0 / math.pi) * np.arcsin(np.sin(_phases(f)))
    return x * envelope(n, env)


def sine(freq: float, dur: float, env: str = "pluck", glide: float | None = None) -> np.ndarray:
    n = nsamp(dur)
    f = np.full(n, float(freq)) if glide is None else _freq_ramp(freq, glide, n)
    x = np.sin(_phases(f))
    return x * envelope(n, env)


def noise(dur: float, env: str = "click", lp: float = 0.0, gain: float = 1.0, seed: int = 7) -> np.ndarray:
    """白噪声；lp>0 时做一次滑动平均，把「嘶」压成「擦」，用于删除/拆线这类摩擦感。

    种子固定：同样的参数永远出同样的字节，--check 与 Editor 自测才有可比性。
    """
    n = nsamp(dur)
    x = np.random.default_rng(seed).uniform(-1.0, 1.0, n)
    k = int(round(lp * 60))
    if k > 1:
        x = np.convolve(x, np.ones(k) / k, mode="same")
        p = float(np.max(np.abs(x)))
        if p > 0:
            x = x / p
    return x * gain * envelope(n, env)


def mix(parts: list[tuple[np.ndarray, float]]) -> np.ndarray:
    end = max(int(round(t * SR)) + len(x) for x, t in parts)
    buf = np.zeros(end)
    for x, t in parts:
        i = int(round(t * SR))
        buf[i:i + len(x)] += x
    return buf


def arp(notes, kind: str = "square", env: str = "stacc", dur: float = 0.07,
        gap: float = 0.0, duty: float = 0.5, gain: float = 1.0) -> np.ndarray:
    """琶音：notes 收频率（Hz）或 (频率, 时长)；kind 选音色，gap 是音与音之间的空隙。"""
    voice = {"square": lambda f, d: square(f, d, env, duty),
             "tri": lambda f, d: tri(f, d, env),
             "sine": lambda f, d: sine(f, d, env)}[kind]
    parts, t = [], 0.0
    for item in notes:
        f, d = item if isinstance(item, tuple) else (item, dur)
        parts.append((voice(f, d) * gain, t))
        t += d + gap
    return mix(parts)


def _trim(x: np.ndarray, keep: float = 0.012) -> np.ndarray:
    hit = np.nonzero(np.abs(x) > 1e-4)[0]
    if len(hit) == 0:
        return np.zeros(nsamp(0.02))
    return x[:min(len(x), int(hit[-1]) + 1 + int(keep * SR))]


def render(x: np.ndarray, level: float) -> np.ndarray:
    """收尾三件套：掐掉尾巴静音 → 去 DC → 淡入淡出 → 峰值标定到 level（level 恒 ≤ CEILING）。"""
    x = _trim(np.asarray(x, dtype=float))
    x = x - float(np.mean(x))
    n = len(x)
    fi = min(FADE_IN, max(1, n // 2))
    x[:fi] *= np.linspace(0.0, 1.0, fi)
    fo = min(FADE_OUT, max(1, n // 2))
    x[n - fo:] *= np.linspace(1.0, 0.0, fo)
    p = float(np.max(np.abs(x)))
    if p > 0:
        x = x * (level / p)
    return x


# ---------------- cue 定义 ----------------

def c_click() -> np.ndarray:
    return mix([(square(940, 0.026, "click"), 0.0), (noise(0.018, "click", 0.35, 0.35, 11), 0.0)])


def c_place() -> np.ndarray:
    # 落子 = 一声低频「咚」+ 上行两音确认，玩家不看画布也知道模块放下了
    return mix([
        (tri(190, 0.05, "stacc", glide=150), 0.0),
        (noise(0.03, "click", 0.5, 0.5, 13), 0.0),
        (arp([NOTE["C5"], NOTE["G5"]], "square", "stacc", 0.045, 0.004, gain=0.85), 0.035),
    ])


def c_link() -> np.ndarray:
    return mix([
        (square(430, 0.1, "pluck", glide=720), 0.0),
        (noise(0.024, "click", 0.2, 0.4, 17), 0.09),
    ])


def c_unlink() -> np.ndarray:
    return mix([
        (square(700, 0.09, "pluck", glide=330), 0.0),
        (noise(0.05, "stacc", 0.55, 0.45, 19), 0.03),
    ])


def c_del() -> np.ndarray:
    return mix([
        (arp([NOTE["G4"], NOTE["C4"]], "square", "stacc", 0.055, 0.006, duty=0.3, gain=0.8), 0.0),
        (noise(0.075, "stacc", 0.7, 0.55, 23), 0.03),
    ])


def c_undo() -> np.ndarray:
    return arp([(NOTE["D5"], 0.07), (NOTE["G4"], 0.095)], "tri", "pluck", gap=0.008)


def c_redo() -> np.ndarray:
    return arp([(NOTE["G4"], 0.07), (NOTE["D5"], 0.095)], "tri", "pluck", gap=0.008)


def c_toggle() -> np.ndarray:
    return mix([
        (tri(330, 0.06, "stacc", glide=520), 0.0),
        (noise(0.05, "click", 0.8, 0.3, 29), 0.02),
    ])


def c_run() -> np.ndarray:
    return mix([
        (arp([NOTE["C5"], NOTE["E5"], NOTE["G5"]], "square", "stacc", 0.055, 0.006, gain=0.9), 0.0),
        (sine(NOTE["C6"], 0.14, "pluck", glide=NOTE["C6"] * 1.02), 0.16),
    ])


def c_step() -> np.ndarray:
    # 每轮搬运都响一次，音量收得比谁都低，不然循环一快就是机枪
    return mix([
        (tri(680, 0.042, "click"), 0.0),
        (square(1020, 0.022, "click", duty=0.25, glide=1020), 0.0),
    ])


def c_pass() -> np.ndarray:
    return arp([NOTE["E5"], NOTE["A5"]], "square", "stacc", 0.05, 0.006, gain=0.9)


def c_ac() -> np.ndarray:
    return mix([
        (arp([NOTE["C5"], NOTE["E5"], NOTE["G5"], NOTE["C6"]], "square", "stacc", 0.062, 0.006, gain=0.9), 0.0),
        (noise(0.05, "click", 0.15, 0.25, 31), 0.2),
    ])


def c_fail() -> np.ndarray:
    # 下行 + 占空比歪一点，听起来「不对味」，但不用刺耳的 buzz：错一次是可以接受的
    return mix([
        (square(NOTE["E4"], 0.13, "pluck", duty=0.35, glide=NOTE["C4"]), 0.0),
        (square(NOTE["C4"] * 0.985, 0.16, "pluck", duty=0.35, glide=NOTE["G4"] * 0.98), 0.1),
        (noise(0.04, "click", 0.6, 0.25, 37), 0.0),
    ])


def c_error() -> np.ndarray:
    # 编译报错：低八度的两记 buzz，明显比 fail 更硬，闭着眼也分得清是语法没过
    return mix([
        (square(150, 0.075, "stacc", duty=0.22), 0.0),
        (square(138, 0.085, "stacc", duty=0.22), 0.1),
        (noise(0.05, "click", 0.3, 0.35, 41), 0.0),
        (noise(0.045, "click", 0.3, 0.3, 43), 0.1),
    ])


def c_levelin() -> np.ndarray:
    return arp([NOTE["G4"], NOTE["C5"], NOTE["E5"]], "tri", "soft", 0.075, 0.008, gain=0.9)


def c_clear() -> np.ndarray:
    # 通关：五音上行琶音，末尾叠一个三音和弦拖住，收在 0.5s 出头
    chord_start = 0.3
    return mix([
        (arp([(NOTE["C5"], 0.06), (NOTE["E5"], 0.06), (NOTE["G5"], 0.06), (NOTE["C6"], 0.06)],
             "square", "stacc", gap=0.006, gain=0.85), 0.0),
        (tri(NOTE["C5"], 0.19, "hold"), chord_start),
        (tri(NOTE["G5"], 0.19, "hold"), chord_start),
        (tri(NOTE["E6"], 0.19, "hold", glide=NOTE["E6"] * 1.005), chord_start),
        (noise(0.05, "click", 0.12, 0.2, 47), chord_start),
    ])


def c_save() -> np.ndarray:
    return arp([NOTE["E6"], NOTE["G6"]], "tri", "click", 0.03, 0.004, gain=0.7)


# gap = 该 cue 的最小间隔（秒），和 SoundSystem.cs 里的 MinInterval 表是同一份口径
CUES: dict[str, dict] = {
    "click":   dict(build=c_click, level=0.34, gap=0.05, label="通用按钮"),
    "place":   dict(build=c_place, level=0.50, gap=0.06, label="落子"),
    "link":    dict(build=c_link, level=0.50, gap=0.06, label="连线"),
    "unlink":  dict(build=c_unlink, level=0.46, gap=0.06, label="拆线"),
    "del":     dict(build=c_del, level=0.50, gap=0.07, label="删除模块"),
    "undo":    dict(build=c_undo, level=0.46, gap=0.07, label="撤销（下行两音）"),
    "redo":    dict(build=c_redo, level=0.46, gap=0.07, label="恢复（上行两音）"),
    "toggle":  dict(build=c_toggle, level=0.40, gap=0.06, label="折叠/展开"),
    "run":     dict(build=c_run, level=0.60, gap=0.25, label="开始运行"),
    "step":    dict(build=c_step, level=0.28, gap=0.07, label="每轮搬运"),
    "pass":    dict(build=c_pass, level=0.56, gap=0.08, label="单条样例通过"),
    "ac":      dict(build=c_ac, level=0.64, gap=0.50, label="全部通过"),
    "fail":    dict(build=c_fail, level=0.54, gap=0.30, label="答案不对"),
    "error":   dict(build=c_error, level=0.58, gap=0.30, label="编译/语法报错"),
    "levelin": dict(build=c_levelin, level=0.52, gap=0.40, label="进关"),
    "clear":   dict(build=c_clear, level=0.68, gap=0.80, label="通关"),
    "save":    dict(build=c_save, level=0.24, gap=0.45, label="自动保存落盘"),
}


def build(name: str) -> np.ndarray:
    cue = CUES[name]
    return render(cue["build"](), cue["level"])


def write_wav(path: Path, x: np.ndarray) -> None:
    pcm = np.clip(np.rint(x * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def verify(name: str, x: np.ndarray) -> list[str]:
    bad = []
    dur = len(x) / SR
    peak = float(np.max(np.abs(x))) if len(x) else 0.0
    dc = float(abs(np.mean(x))) if len(x) else 1.0
    if dur > MAX_DUR:
        bad.append(f"{name} 时长 {dur:.3f}s 超预算 {MAX_DUR}s")
    if peak > CEILING + 1e-6:
        bad.append(f"{name} 峰值 {peak:.3f} 超过上限 {CEILING}")
    if peak < 0.12:
        bad.append(f"{name} 峰值只有 {peak:.3f}，等于没声")
    if dc > 0.01:
        bad.append(f"{name} 有 DC 偏移 {dc:.4f}")
    if len(x) and abs(float(x[0])) > 0.02:
        bad.append(f"{name} 起始样本 {float(x[0]):.3f} 没淡入，会爆音")
    gap = CUES.get(name, {}).get("gap")
    if gap is not None and gap <= 0:
        bad.append(f"{name} 去抖间隔必须为正")
    return bad


def report(name: str, x: np.ndarray, size: int | None) -> None:
    cue = CUES[name]
    dur = len(x) / SR
    peak = float(np.max(np.abs(x)))
    tail = f"{size / 1024:6.1f}KB" if size is not None else "  (未落盘)"
    print(f"  {name:<8}{cue['label']:<14}{dur:5.3f}s  峰值 {peak:.2f}  去抖 {cue['gap']:.2f}s  {tail}")


def do_generate() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    problems: list[str] = []
    total = 0
    print(f"生成 {len(CUES)} 条 cue → {OUT.relative_to(ROOT)}")
    for name in CUES:
        x = build(name)
        problems += verify(name, x)
        path = OUT / f"{name}.wav"
        write_wav(path, x)
        total += path.stat().st_size
        report(name, x, path.stat().st_size)
    for stale in sorted(OUT.glob("*.wav")):
        if stale.stem not in CUES:
            problems.append(f"磁盘上有名单之外的音频：{stale.name}")
    if total > TOTAL_BUDGET:
        problems.append(f"总体积 {total} 字节超过预算 {TOTAL_BUDGET}")
    print(f"合计 {total / 1024:.1f}KB（预算 {TOTAL_BUDGET / 1024:.0f}KB）")
    return finish(problems)


def do_check() -> int:
    problems: list[str] = []
    total = 0
    print(f"校验 {len(CUES)} 条 cue ← {OUT.relative_to(ROOT)}")
    if not OUT.exists():
        problems.append(f"目录不存在：{OUT}")
        print("\n".join("  ✘ " + p for p in problems))
        return 1
    for name in CUES:
        x = build(name)
        problems += verify(name, x)
        path = OUT / f"{name}.wav"
        if not path.exists():
            problems.append(f"{name}.wav 不在磁盘上，先跑 python tools/gen_sfx.py")
            report(name, x, None)
            continue
        size = path.stat().st_size
        total += size
        disk = read_wav(path)
        problems += verify(name + "(磁盘)", disk)
        if abs(len(disk) - len(x)) > 64:
            problems.append(f"{name} 磁盘长度 {len(disk)} 和现算长度 {len(x)} 对不上，文件是旧的")
        report(name, disk, size)
    for stale in sorted(OUT.glob("*.wav")):
        if stale.stem not in CUES:
            problems.append(f"磁盘上有名单之外的音频：{stale.name}")
    if total > TOTAL_BUDGET:
        problems.append(f"总体积 {total} 字节超过预算 {TOTAL_BUDGET}")
    print(f"合计 {total / 1024:.1f}KB（预算 {TOTAL_BUDGET / 1024:.0f}KB）")
    return finish(problems)


def read_wav(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as w:
        if w.getnchannels() != 1 or w.getsampwidth() * 8 != BITS or w.getframerate() != SR:
            raise RuntimeError(f"{path.name} 不是 {SR}Hz/{BITS}bit/单声道")
        data = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2")
    return data.astype(float) / 32767.0


def finish(problems: list[str]) -> int:
    if problems:
        for p in problems:
            print("  ✘ " + p)
        print(f"音效校验失败：{len(problems)} 项")
        return 1
    print("音效校验通过")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="程序化合成 8-bit 教学反馈音")
    ap.add_argument("--check", action="store_true", help="只校验不落盘")
    ap.add_argument("--only", metavar="CUES", help="只生成逗号分隔的几条 cue")
    args = ap.parse_args(argv)
    if args.only:
        picked = [n.strip() for n in args.only.split(",") if n.strip()]
        unknown = [n for n in picked if n not in CUES]
        if unknown:
            print("  ✘ 不认识的 cue：" + " ".join(unknown) + "（名单：" + " ".join(CUES) + "）")
            return 1
        OUT.mkdir(parents=True, exist_ok=True)
        for name in picked:
            write_wav(OUT / f"{name}.wav", build(name))
            print(f"  {name}.wav 已生成")
        return 0
    return do_check() if args.check else do_generate()


if __name__ == "__main__":
    sys.exit(main())
