using System.Collections.Generic;
using Dayu.Core;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 三关在表达式模式下的验收：编译成功 + 样例输出正确。
    public static class LevelExprTests
    {
        public static void Run(ref int total, ref int passed)
        {
            // 起始图现在是空的（不给预置答案），能跑通样例的只有 Solution —— 验收口径跟着改。
            // 每一关都走一遍「编译标准答案 + 逐样例跑 VM」：往关卡表里加新关不用改这里就会被盯住。
            foreach (var level in Levels.All)
            {
                var cr = Compiler.Compile(level.Solution);
                total++;
                if (!cr.Ok)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var e in cr.Errors) sb.Append("｜").Append(e.NodeUid).Append(' ').Append(e.Message);
                    Debug.Log("✘ " + level.Id + " 标准答案编译 —— " + sb.ToString());
                    continue;
                }
                passed++;
                Debug.Log("✔ " + level.Id + " 标准答案编译 —— 通过");

                foreach (var test in level.Tests)
                {
                    total++;
                    var run = Vm.Run(cr.Program, test.Input);
                    if (run.Ok && run.Output == test.Output)
                    {
                        passed++;
                        Debug.Log("✔ " + level.Id + " " + test.Input.Replace("\n", " ") + " —— " + run.Output.Replace("\n", " "));
                    }
                    else Debug.Log("✘ " + level.Id + " " + test.Input.Replace("\n", " ") + " —— 期望「" + test.Output.Replace("\n", " ") + "」，实际「"
                        + (run.Error == null ? (run.Output ?? "").Replace("\n", " ") : run.Error.Message) + "」");
                }
            }

            ShapeCases(ref total, ref passed);
            HeaderCases(ref total, ref passed);
            CarryBadgeCases(ref total, ref passed);
            SilhouetteCases(ref total, ref passed);
        }

        // 鲸鱼娘头顶那块搬运名牌：框要比字大、字块矩形要跟着文字撑开，
        // 否则 PixelText 的居中会退化成左对齐，整串数字从名牌中心往右溢出（第十轮截图抓到过）。
        static void CarryBadgeCases(ref int total, ref int passed)
        {
            PixelText.EnsureAtlas();
            foreach (var s in new[] { "3", "42", "s = 0", "a[0] = 7", "sum = 123456" })
            {
                Vector2 badge, label;
                App.HeroBadgeLayout(s, 4, out badge, out label);
                var m = PixelText.MeasureString(s, 4);
                Check(ref total, ref passed, "搬运名牌「" + s + "」字块矩形等于字宽", label, m);
                Check(ref total, ref passed, "搬运名牌「" + s + "」框住整串字",
                    badge.x >= m.x + 8f && badge.y >= m.y + 4f, true);
                Debug.Log("  · 名牌 " + s + " 字 " + Mathf.CeilToInt(m.x) + "x" + Mathf.CeilToInt(m.y)
                          + " 框 " + Mathf.CeilToInt(badge.x) + "x" + Mathf.CeilToInt(badge.y));
            }
            // 空串不该撑出一块空白牌
            Vector2 b0, l0;
            App.HeroBadgeLayout("", 4, out b0, out l0);
            Check(ref total, ref passed, "搬运名牌空值不撑框", b0.x <= 12f && b0.y <= 8f + PixelText.CellH * 4, true);

            // 反翻系数：立绘镜像时名牌要跟着取负，净缩放永远是正的，否则数字照镜子（第十一轮截图抓到「s = 0」显示成「0 = 2」）
            Check(ref total, ref passed, "搬运名牌 立绘朝右不翻时净缩放为正",
                (1f * App.HeroBadgeLocalX(1f, 1f)).ToString("0.###"), "1");
            Check(ref total, ref passed, "搬运名牌 立绘镜像时局部取负抵消",
                ((-1f) * App.HeroBadgeLocalX(-1f, 1f)).ToString("0.###"), "1");
            // 恒定屏幕大小：净缩放 = zoom × 局部系数，必须钉成 1，镜头拉远数字才不会被一起缩没
            foreach (var z in new[] { 0.42f, 0.6f, 1f, 1.35f, 1.8f })
                Check(ref total, ref passed, "搬运名牌 机位 " + z.ToString("0.##") + " 下屏幕大小恒定",
                    (z * App.HeroBadgeLocalX(1f, z)).ToString("0.###"), "1");
            Check(ref total, ref passed, "搬运名牌 机位 " + (1f / App.HeroBadgeLocalX(1f, 20f)).ToString("0.##") + " 已截到上限",
                App.HeroBadgeLocalX(1f, 20f).ToString("0.###"), "0.25");
        }

        // 顶栏「章节 · 关卡名」是 2 号字单行，超过 App.HeaderLevelW 会被 Fit 截成「..」。
        // 第九轮 S2 就在截图里被截断过，这里逐关把宽度钉死，改文案或加关卡立刻报。
        static void HeaderCases(ref int total, ref int passed)
        {
            PixelText.EnsureAtlas();
            // 图集没加载时 MeasureString 全返回 0，宽度断言会变成假绿，所以先确认图集真的起来了
            Check(ref total, ref passed, "字体图集已加载",
                GlyphAtlas.Texture != null && PixelText.MeasureString("栈", 2).x > 0f, true);

            var missing = new System.Text.StringBuilder();
            foreach (var l in Levels.All)
            {
                string s = l.Chapter + " · " + l.Title;
                foreach (var c in s)
                    if (c != ' ' && !GlyphAtlas.TryGet(c, out _)) missing.Append(c);
                float w = PixelText.MeasureString(s, 2).x;
                Check(ref total, ref passed, l.Id + " 顶栏标题不截断（" + s + "）",
                    w <= App.HeaderLevelW, true);
                Debug.Log("  · 顶栏 " + l.Id + " 实测 " + Mathf.CeilToInt(w) + "/" + App.HeaderLevelW + "px");
            }
            Check(ref total, ref passed, "顶栏标题字符全在图集里", missing.ToString(), "");
        }

        // 关卡表的形状检查：文案、任务、样例数量、序章空画布、关卡选择弹窗高度，都是纯数据断言。
        static void ShapeCases(ref int total, ref int passed)
        {
            var ids = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var l in Levels.All)
            {
                Check(ref total, ref passed, l.Id + " 编号不重复", ids.Add(l.Id), true);
                Check(ref total, ref passed, l.Id + " Graph.LevelId 对齐", l.Solution != null && l.Solution.LevelId == l.Id, true);
                Check(ref total, ref passed, l.Id + " 有简介", !string.IsNullOrEmpty(l.Brief), true);
                Check(ref total, ref passed, l.Id + " 有输入说明", !string.IsNullOrEmpty(l.InputHint), true);
                Check(ref total, ref passed, l.Id + " 有任务清单", l.Goals != null && l.Goals.Count > 0, true);
                // 序章 P0 只让写一个算式，输入为空，重复三条样例没有意义
                // 题干只说做什么：一句话、不点模块名，做法一律收进「看提示」折叠块
                Check(ref total, ref passed, l.Id + " 题干不剧透做法", !l.Brief.Contains("「") && CountChar(l.Brief, '。') <= 1, true);
                // 每一关进来都是空画布：只有「开始」和两块样例板，绝不预置标准答案
                Check(ref total, ref passed, l.Id + " 起始图是空的", l.Starter != null && l.Starter.Nodes.Count <= 3, true);
                // 起始图默认走表达式模式；栈那几关例外：压入/弹出要挂在执行链上、值要沿数据线流，
                // 表达式模式不画数据端口，所以带结构实例的关卡一律开连线模式。
                Check(ref total, ref passed, l.Id + " 起始图模式对", l.Starter != null
                    && (l.Starter.ExprMode || (l.Starter.Structs != null && l.Starter.Structs.Count > 0)), true);
                Check(ref total, ref passed, l.Id + " 起始图能编译", Compiler.Compile(l.Starter).Ok, true);
                // 标 Free（一进来就能挑）的关必须都在序章里，序章的关都必须标 Free
                Check(ref total, ref passed, l.Id + " 序章与解锁标记一致", l.Free == l.Chapter.StartsWith("序章"), true);
                // 序章只让写一个算式，输入为空，重复三条样例没有意义
                Check(ref total, ref passed, l.Id + " 样例条数够", l.Tests.Count >= (l.Free ? 1 : 3), true);
            }
            // 关卡选择弹窗一屏放得下，不加滚动也不能被裁
            int dialogH = 130 + Levels.All.Count * 44;
            Check(ref total, ref passed, "关卡选择弹窗高度不超屏（" + dialogH + "px）", dialogH <= 820, true);
            Check(ref total, ref passed, "关卡总数", Levels.All.Count, 13);
            int free = 0;
            foreach (var l in Levels.All) if (l.Free) free++;
            Check(ref total, ref passed, "序章关卡数", free, 9);
            // 顺序：栈之前都算序章，第一章从「栈与队列」开手
            int firstChapter = Levels.All.FindIndex(l => !l.Free);
            Check(ref total, ref passed, "第一章从第 " + (firstChapter + 1) + " 关开始", firstChapter, 9);
            Check(ref total, ref passed, "第一章章节名", firstChapter >= 0 && Levels.All[firstChapter].Chapter == "第一章 · 栈与队列", true);
            // 第一章由易到难：栈的倒序输出 → 栈顶元素 → 队列先进先出 → 括号匹配（空了 + 如果）
            var chapter = Levels.All.FindAll(l => l.Chapter == "第一章 · 栈与队列");
            Check(ref total, ref passed, "第一章关卡数", chapter.Count, 4);
            Check(ref total, ref passed, "第一章顺序",
                chapter.Count == 4 ? chapter[0].Id + "/" + chapter[1].Id + "/" + chapter[2].Id + "/" + chapter[3].Id : "缺关",
                "S1/S2/S3/L3");
            // 章节名叫栈与队列，两类结构都得真有关卡撑着
            Check(ref total, ref passed, "第一章有栈关", chapter.Exists(l => l.Structs != null && l.Structs.Exists(s => s.Kind == "stack")), true);
            Check(ref total, ref passed, "第一章有队列关", chapter.Exists(l => l.Structs != null && l.Structs.Exists(s => s.Kind == "queue")), true);
            foreach (var l in chapter)
            {
                string kind = l.Structs != null && l.Structs.Count > 0 ? l.Structs[0].Kind : "";
                Check(ref total, ref passed, l.Id + " 关卡表声明了结构实例", kind == "stack" || kind == "queue", true);
                Check(ref total, ref passed, l.Id + " 起始图带同一个结构实例", l.Starter != null && l.Starter.Structs.Exists(s => s.Id == "st" && s.Kind == kind), true);
                Check(ref total, ref passed, l.Id + " 标准答案建了结构", l.Solution != null && l.Solution.Nodes.Exists(n => n.Type == "structnew"), true);
                // 答案里只能出现跟实例种类匹配的操作模块：栈关不许混进队列模块，队列关也不许混进栈模块
                string wrong = kind == "queue" ? "stack_" : "queue_";
                Check(ref total, ref passed, l.Id + " 标准答案用对结构种类",
                    kind.Length == 0 || l.Solution.Nodes.Find(n => n.Type.StartsWith(wrong, System.StringComparison.Ordinal)) == null, true);
            }
        }

        // 卡片轮廓：十一轮玩家要「不要清一色长方形」。几何全在 ModuleViz.SilhouetteBlocks 这个纯函数里，
        // 所以这里能不看屏幕就把三件事钉死：每种结构都有轮廓块、十三种的轮廓互不相同、块只许贴在卡外面。
        private static readonly string[] SilhouetteKinds =
        {
            "gate", "factory", "tower", "quay", "crate", "crate_set", "crate_get",
            "crate_seal", "shelf", "bench", "belt", "switch", "dock"
        };

        private static void SilhouetteCases(ref int total, ref int passed)
        {
            var seen = new HashSet<string>();
            // 最窄 112 / 最宽 300 都要成立：CardWidth 的 Clamp 区间就是这两个数
            foreach (var w in new[] { 112f, 168f, 300f })
            {
                foreach (var kind in SilhouetteKinds)
                {
                    var blocks = ModuleViz.SilhouetteBlocks(kind, w, 120f);
                    Check(ref total, ref passed, "轮廓 " + kind + " 宽" + (int)w + " 有凸块", blocks.Count > 0, true);

                    int bad = 0;
                    foreach (var b in blocks)
                    {
                        if (b.width <= 0f || b.height <= 0f) { bad++; continue; }
                        // 块必须整个落在四条边带之一：压进卡面会盖住招牌 / 表达式
                        bool top = b.yMax <= 0.01f, bottom = b.y >= 119.99f, left = b.xMax <= 0.01f, right = b.x >= w - 0.01f;
                        if (!top && !bottom && !left && !right) { bad++; continue; }
                        // 凸出的深度：上下 ≤8、左右 ≤12，卡片之间的间距是 24px（RespaceChains / 关卡排版），留一半余量
                        float depth = top ? -b.y : bottom ? b.yMax - 120f : left ? -b.x : b.xMax - w;
                        if (depth > (top || bottom ? 8.01f : 12.01f)) { bad++; continue; }
                        // 沿边方向不许伸到隔壁卡片的位置上：上下按横轴查、左右按纵轴查
                        if (top || bottom) { if (b.x < -0.01f || b.xMax > w + 0.01f) { bad++; continue; } }
                        else if (b.y < -0.01f || b.yMax > 120.01f) bad++;
                    }
                    Check(ref total, ref passed, "轮廓 " + kind + " 宽" + (int)w + " 块全在卡外且凸出不越 24px 间距", bad, 0);

                    if (w == 168f)
                    {
                        var sb = new System.Text.StringBuilder(kind);
                        foreach (var b in blocks)
                            sb.Append('|').Append(b.x.ToString("0.#")).Append(',').Append(b.y.ToString("0.#"))
                              .Append(',').Append(b.width.ToString("0.#")).Append(',').Append(b.height.ToString("0.#"));
                        seen.Add(sb.ToString());
                    }
                }
            }
            Check(ref total, ref passed, "轮廓 十三种结构互不重复", seen.Count, SilhouetteKinds.Length);
            Check(ref total, ref passed, "轮廓 未知种类不画块", ModuleViz.SilhouetteBlocks(null, 168f, 120f).Count, 0);
        }

        static int CountChar(string s, char c)
        {
            int n = 0;
            foreach (var ch in s) if (ch == c) n++;
            return n;
        }

        static void Check(ref int total, ref int passed, string label, bool ok, bool want)
        {
            total++;
            if (ok == want) { passed++; Debug.Log("✔ 关卡表 —— " + label); }
            else Debug.Log("✘ 关卡表 —— " + label + " 期望 " + want + "，实际 " + ok);
        }

        static void Check(ref int total, ref int passed, string label, int actual, int want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 关卡表 —— " + label + " = " + actual); }
            else Debug.Log("✘ 关卡表 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Check(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 关卡表 —— " + label + " = " + actual); }
            else Debug.Log("✘ 关卡表 —— " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }

        static void Check(ref int total, ref int passed, string label, Vector2 actual, Vector2 want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 关卡表 —— " + label + " = " + actual); }
            else Debug.Log("✘ 关卡表 —— " + label + " 期望 " + want + "，实际 " + actual);
        }
    }
}
