# -*- coding: utf-8 -*-
"""R13 关卡样例核算：把 Levels.cs 每关 Solution 图的语义用 Python 重写一遍，
把「可见 + 隐藏」全部样例输入喂进去，打印实际输出，与文件里将填的期望值逐条对账。
跑法："C:/ProgramData/anaconda3/python.exe" tools/verify_levels.py
口径对齐 Vm.cs：整段输入按空白切成 token 依次读；每次「输出」打印一行，行与行用 \\n 连接；
布尔按 true/false 打印；整数按十进制原样打印。"""
import sys
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

def toks(s):
    return (s or "").split()

def iz(x):  # token -> 整数显示串（对齐 C# long.Parse + InvariantCulture）
    return str(int(x))

# ---- 每关一个函数，返回输出行列表 ----
def P0(t):
    return ["13"]  # 输出节点固定算式 6 + 7

def P1(t):
    return [iz(t[0])]

def L1(t):
    return [str(int(t[0]) + int(t[1]))]

def P2(t):
    a = int(t[0]); b = a * 2
    return [str(b + 1)]

def P3(t):
    return ["1" if int(t[0]) >= 60 else "0"]

def P4(t):
    n = int(t[0]); s = 0
    for i in range(1, n + 1):          # 计数循环：起始 1、结束 n（闭区间）
        s += int(t[i])
    return [str(s)]

def L5(t):
    n = int(t[0]); s = 0
    while n > 0:                        # 条件循环：每轮 s+=n、n-=1
        s += n; n -= 1
    return [str(s)]

def L2(t):
    n = int(t[0]); a = [int(x) for x in t[1:1 + n]]
    return [str(sum(a))]                # 两个循环：先填 a[i]=x，再按 a.length 累加

def L4(t):
    n = int(t[0]); a = [int(x) for x in t[1:1 + n]]
    m = a[0]                            # 擂主初值取 a[0]
    for i in range(1, n):
        if a[i] > m: m = a[i]
    return [str(m)]

def S1(t):
    n = int(t[0]); nums = t[1:1 + n]
    return list(reversed(nums))         # 压 n 个再弹 n 个：后进先出 = 倒序

def S2(t):
    n = int(t[0]); nums = [int(x) for x in t[1:1 + n]]; k = int(t[1 + n])
    for _ in range(k):
        nums.pop()                      # 弹走顶上 k 只
    return [str(nums[-1])]              # 「栈顶元素」只看不拿

def S3(t):
    n = int(t[0]); nums = t[1:1 + n]
    return list(nums)                   # 入队 n 个再出队 n 个：先进先出 = 原序

def L3(t):
    s = t[0]; ok = True; depth = 0
    for c in s:
        if c == "(":
            depth += 1
        elif c == ")":
            if depth == 0: ok = False
            else: depth -= 1
    if depth != 0: ok = False           # 扫完筒里还有剩
    return ["true" if ok else "false"]

RUN = dict(P0=P0, P1=P1, L1=L1, P2=P2, P3=P3, P4=P4, L5=L5, L2=L2, L4=L4, S1=S1, S2=S2, S3=S3, L3=L3)

# ---- 样例数据：(是否可见, 输入, 声称的期望输出)；期望输出就是 Levels.cs 里要填的值 ----
TESTS = {
"P0": [(True,  "",     "13"),
       (False, "",     "13")],
"P1": [(True,  "5",    "5"), (True,  "-3",   "-3"), (True,  "0",    "0"),
       (False, "1000000000", "1000000000"), (False, "-1000000000", "-1000000000"),
       (False, "1",    "1"), (False, "-1",   "-1")],
"L1": [(True,  "3 5",  "8"), (True,  "-2 7", "5"), (True,  "0 0",  "0"),
       (False, "1000000000 1000000000", "2000000000"),
       (False, "-1000000000 -1000000000", "-2000000000"),
       (False, "-5 5", "0"), (False, "0 -1", "-1")],
"P2": [(True,  "3",    "7"), (True,  "0",    "1"), (True,  "-2",   "-3"),
       (False, "1",    "3"), (False, "-1",   "-1"),
       (False, "500000000",  "1000000001"),
       (False, "-1000000000", "-1999999999")],
"P3": [(True,  "60",   "1"), (True,  "59",   "0"), (True,  "100",  "1"),
       (False, "0",    "0"), (False, "1",    "0"), (False, "61",   "1"), (False, "99", "1")],
"P4": [(True,  "3\n1 2 3", "6"), (True,  "1\n7", "7"), (True,  "5\n1 2 3 4 5", "15"),
       (False, "0", "0"), (False, "4\n-1 -2 -3 -4", "-10"),
       (False, "2\n1000000000 1000000000", "2000000000"),
       (False, "10\n1 2 3 4 5 6 7 8 9 10", "55")],
"L5": [(True,  "4",    "10"), (True,  "1",    "1"), (True,  "10",   "55"),
       (False, "0",    "0"), (False, "2",    "3"), (False, "5",    "15"), (False, "100", "5050")],
"L2": [(True,  "5\n1 2 3 4 5", "15"), (True,  "1\n42", "42"), (True,  "3\n-1 -2 -3", "-6"),
       (False, "0", "0"), (False, "2\n1000000000 -1000000000", "0"),
       (False, "1\n-1000000000", "-1000000000"), (False, "4\n0 0 0 0", "0"),
       (False, "8\n-5 -4 -3 -2 -1 3 4 5", "-3")],
"L4": [(True,  "3\n1 5 2", "5"), (True,  "4\n-7 -2 -9 -1", "-1"), (True,  "1\n42", "42"),
       (False, "3\n-1000000000 -1000000000 -1000000000", "-1000000000"),
       (False, "6\n1 2 3 4 5 6", "6"), (False, "6\n6 5 4 3 2 1", "6"),
       (False, "1\n-1000000000", "-1000000000")],
"S1": [(True,  "3\n1 2 3", "3\n2\n1"), (True,  "1\n7", "7"), (True,  "4\n9 -2 5 0", "0\n5\n-2\n9"),
       (False, "2\n1000000000 -1000000000", "-1000000000\n1000000000"),
       (False, "5\n1 1 2 2 3", "3\n2\n2\n1\n1"),
       (False, "6\n-1 -2 -3 -4 -5 -6", "-6\n-5\n-4\n-3\n-2\n-1"),
       (False, "4\n0 0 0 0", "0\n0\n0\n0")],
"S2": [(True,  "5\n1 2 3 4 5\n2", "3"), (True,  "3\n7 8 9\n2", "7"), (True,  "4\n1 3 5 7\n0", "7"),
       (False, "1\n42\n0", "42"), (False, "5\n1 2 3 4 5\n4", "1"),
       (False, "3\n-1000000000 0 1000000000\n1", "0"),
       (False, "6\n9 8 7 6 5 4\n3", "7")],
"S3": [(True,  "3\n1 2 3", "1\n2\n3"), (True,  "1\n7", "7"), (True,  "4\n9 -2 5 0", "9\n-2\n5\n0"),
       (False, "2\n1000000000 -1000000000", "1000000000\n-1000000000"),
       (False, "5\n1 1 2 2 3", "1\n1\n2\n2\n3"),
       (False, "6\n-1 -2 -3 -4 -5 -6", "-1\n-2\n-3\n-4\n-5\n-6"),
       (False, "4\n0 0 0 0", "0\n0\n0\n0")],
"L3": [(True,  "(())", "true"), (True,  "()()", "true"), (True,  "(()",  "false"),
       (True,  ")(",   "false"), (True,  "(",    "false"), (True,  ")(())", "false"),
       (False, "())(",  "false"), (False, "((()))", "true"), (False, "(())(", "false"),
       (False, "(((((((((())))))))))", "true"), (False, "()()()()", "true")],
}

ORDER = ["P0", "P1", "L1", "P2", "P3", "P4", "L5", "L2", "L4", "S1", "S2", "S3", "L3"]
bad = 0
print("关卡  可见/隐藏  样例输入            期望 -> 实际            判定")
print("-" * 78)
for lid in ORDER:
    rows = TESTS[lid]
    vis = sum(1 for v, _, _ in rows if v)
    hid = len(rows) - vis
    print("%-4s %d/%d" % (lid, vis, hid))
    for visible, inp, want in rows:
        got = "\n".join(RUN[lid](toks(inp)))
        tag = "可见" if visible else "隐藏"
        ok = "OK" if got == want else "**不符**"
        if got != want: bad += 1
        print("   [%s] %-28s 期望 %-26s 实际 %-26s %s" %
              (tag, inp.replace("\n", "\\n"), want.replace("\n", "\\n"), got.replace("\n", "\\n"), ok))
print("-" * 78)
print("共 %d 条样例，不符 %d 条" % (sum(len(TESTS[k]) for k in ORDER), bad))
sys.exit(1 if bad else 0)
