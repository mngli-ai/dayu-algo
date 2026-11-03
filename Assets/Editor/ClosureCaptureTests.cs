using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十七轮：主菜单四个按钮点下去全无声，Player.log 里一片 IndexOutOfRangeException，
    // 栈顶是 App.MenuButtons 的闭包。根因：把「读 pairs[i + 1]」写进了按钮回调里，而 C# 的 for
    // 计数器整个循环只有一份，点击那一刻它早就越界了。这类错只有真点一下才暴露，所以这里放一把静态扫子，
    // 并且拿「修复前的原样代码」当负对照 —— 扫不出那段就等于扫子在装样子。
    public static class ClosureCaptureTests
    {
        const string Tag = "闭包捕获";

        public static void Run(ref int total, ref int passed)
        {
            DetectsTheHistoricalBug(ref total, ref passed);
            AcceptsTheFixedShape(ref total, ref passed);
            BodyKinds(ref total, ref passed);
            RepositoryIsClean(ref total, ref passed);
        }

        static void DetectsTheHistoricalBug(ref int total, ref int passed)
        {
            var hits = ClosureCapture.Scan(ClosureCapture.SampleBuggy);
            Check(ref total, ref passed, "修复前那段必须报 1 处", hits.Count, 1);
            string line = hits.Count == 1 ? ClosureCapture.Line(ClosureCapture.SampleBuggy, hits[0]) : "";
            Check(ref total, ref passed, "报的正是 Create(...) 那一句", line.Contains("Create(") ? 1 : 0, 1);
            Check(ref total, ref passed, "同一份代码两次扫描结果一致",
                ClosureCapture.Scan(ClosureCapture.SampleBuggy).Count, hits.Count);
        }

        static void AcceptsTheFixedShape(ref int total, ref int passed)
        {
            Check(ref total, ref passed, "修复后不再报点", ClosureCapture.Scan(ClosureCapture.SampleFixed).Count, 0);
            // 箭头之后同层实参里的计数器（primary 那种）不属于 lambda 体，误伤就是噪音
            Check(ref total, ref passed, "同层实参不误伤",
                ClosureCapture.Scan("for (int i = 0; i < 2; i++) { Make(label[i], () => Go(), i == 0); }").Count, 0);
        }

        static void BodyKinds(ref int total, ref int passed)
        {
            Check(ref total, ref passed, "表达式体 lambda 也算命中",
                ClosureCapture.Scan("for (int i = 0; i < n; i++) { Make(() => cells[i]); }").Count, 1);
            Check(ref total, ref passed, "嵌套循环内外层各自报一行",
                ClosureCapture.Scan("for (int a = 0; a < 3; a++) {\n Make(() => a);\n for (int b = 0; b < 3; b++) {\n Make(() => b);\n }\n}").Count, 2);
            Check(ref total, ref passed, "lambda 里给计数器赋值不算读",
                ClosureCapture.Scan("for (int i = 0; i < 3; i++) { Make(() => { i = 9; }); }").Count, 0);
            Check(ref total, ref passed, "foreach 不在射程内（每轮新建一份）",
                ClosureCapture.Scan("foreach (var l in list) { Make(() => l.id); }").Count, 0);
            Check(ref total, ref passed, "空文本不炸", ClosureCapture.Scan("").Count, 0);
            Check(ref total, ref passed, "null 不炸", ClosureCapture.Scan(null).Count, 0);
        }

        static void RepositoryIsClean(ref int total, ref int passed)
        {
            int files = 0, hits = 0, scanned = 0;
            var sample = new List<string>();
            foreach (var dir in new[] { "Scripts", "Editor" })
            {
                int f;
                int h = ClosureCapture.ScanTree(Path.Combine(Application.dataPath, dir), out f, sample, "ClosureCapture");
                if (h < 0) { Check(ref total, ref passed, "源码目录存在：" + dir, 0, 1); continue; }
                scanned++;
                files += f;
                hits += h;
            }
            // 跳过是为了不让检测试抓自己的负对照样例；但「不跳确实会抓到东西」必须同时成立，
            // 否则「跳过」就可能变成把整个目录一起放过去的后门。
            int f2;
            int unskipped = ClosureCapture.ScanTree(Path.Combine(Application.dataPath, "Editor"), out f2, null);
            Check(ref total, ref passed, "不跳过本检测试时至少抓到 1 处自己的 fixture（证明跳过不是万能放行）", unskipped >= 1 ? 1 : 0, 1);
            Check(ref total, ref passed, "两个源码目录都真在（防路径拼错）", scanned, 2);
            // 一个文件都没扫到也判红：否则「全绿」可能只是没扫
            Check(ref total, ref passed, "至少扫到 60 个源文件，实际 " + files, files >= 60 ? 1 : 0, 1);
            Check(ref total, ref passed, "全工程闭包捕获命中 " + hits + " 处：" + string.Join("、", sample.ToArray()), hits, 0);
            Debug.Log("  · " + Tag + " —— 共扫 " + files + " 个 .cs");
        }

        static void Check(ref int total, ref int passed, string label, int actual, int want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ " + Tag + " —— " + label); }
            else Debug.Log("✘ " + Tag + " —— " + label + " 期望 " + want + "，实际 " + actual);
        }
    }
}
