import io, re

P = "Assets/Scripts/Core/Levels.cs"
src = io.open(P, encoding="utf-8").read().split("\n")

start = None
for i, ln in enumerate(src):
    if "public static readonly List<Level> All=" in ln:
        start = i
        break
assert start is not None

block = "\n".join(src[start:])
m = re.search(r'All=new List<Level>\{', block)
prefix_line = block[:m.end()]
body = block[m.end():]

# 逐个抠出 new Level{...}，用花括号配对而不是猜结尾字符
levels = {}
order_in_file = []
pos = 0
while True:
    k = body.find("new Level{", pos)
    if k < 0:
        break
    d = 0
    j = k + len("new Level")
    while j < len(body):
        c = body[j]
        if c == "{":
            d += 1
        elif c == "}":
            d -= 1
            if d == 0:
                break
        j += 1
    text = body[k:j + 1]
    lid = re.search(r'Id="([^"]+)"', text).group(1)
    levels[lid] = text
    order_in_file.append(lid)
    pos = j + 1

assert len(levels) == 10, order_in_file

ORDER = ["P0", "P1", "L1", "P2", "P3", "P4", "L5", "L2", "L4", "L3"]

META = {
    "P0": ("序章 · 从零开始", "把 6 和 7 加起来，把它们的和输出到屏幕上。"),
    "P1": ("序章 · 从零开始", "读进来一个整数，再把它原样输出出去。"),
    "L1": ("序章 · 变量与运算", "读入两个整数，输出它们的和。"),
    "P2": ("序章 · 变量与运算", "读入一个整数，输出它的 2 倍再加 1。"),
    "P3": ("序章 · 条件分支", "读入一个满分 100 的分数，及格输出 1，不及格输出 0。"),
    "P4": ("序章 · 循环", "第一行给个数 n，后面跟着 n 个整数，把它们全加起来输出。"),
    "L5": ("序章 · 循环", "读入一个正整数 n，把 n、n-1 一直加到 1，输出总和。"),
    "L2": ("序章 · 数组", "读入 n 个整数存进数组 a，再把它们全部加起来输出。"),
    "L4": ("序章 · 数组", "读入 n 个整数存进数组 a，输出其中最大的那个数。"),
    "L3": ("第一章 · 栈与队列", "输入一行只有括号的字符串，成对匹配输出 1，否则输出 0。"),
}

for lid in ORDER:
    chapter, brief = META[lid]
    t = levels[lid]
    t = re.sub(r'Brief="[^"]*"', 'Brief="' + brief + '"', t, count=1)
    t = re.sub(r'Chapter="[^"]*"', 'Chapter="' + chapter + '"', t, count=1)
    if lid != "L3" and "Free=true" not in t:
        t = t.replace('Id="%s",' % lid, 'Id="%s",Free=true,' % lid, 1)
    t = re.sub(r'Starter=[A-Za-z0-9_]+\([^)]*\)', 'Starter=Empty("%s")' % lid, t, count=1)
    levels[lid] = t

lines = [prefix_line + levels[ORDER[0]] + ",", ]
for lid in ORDER[1:]:
    # levels[lid] 自带 new Level{...} 的收尾花括号，列表只再多闭一个再加分号
    lines.append(levels[lid] + ("," if lid != ORDER[-1] else "};"))

# 原来的整块可能横跨多行：定位块尾（最后一个 }}; 所在行）
end = start
depth = 0
seen_open = False
for i in range(start, len(src)):
    for c in src[i]:
        if c == "{":
            depth += 1
            seen_open = True
        elif c == "}":
            depth -= 1
    if seen_open and depth == 0:
        end = i
        break

src = src[:start] + lines + src[end + 1:]
io.open(P, "w", encoding="utf-8", newline="\n").write("\n".join(src))
for lid in ORDER:
    t = levels[lid]
    print(lid, re.search(r'Chapter="[^"]*"', t).group(0), re.search(r'Starter=[^,]*', t).group(0),
          "Free" if "Free=true" in t else "锁定", re.search(r'Brief="[^"]*"', t).group(0))
