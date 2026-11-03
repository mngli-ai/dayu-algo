using System.Collections.Generic;
using Dayu.Core;      // Graph / GraphNode / Levels / Level
using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十七轮：两块样例板改成常驻画板。玩家的诉求是「样例测试的两个模块不要提供删除功能，
    // 左侧栏也不需要有这个栏目了」。这里把「钉住哪些类型、栏里不该再出现、每一关都得自带这两块」写成纯函数断言。
    public static class SamplePinTests
    {
        const string Tag = "样例板常驻";

        public static void Run(ref int total, ref int passed)
        {
            PinnedTypes(ref total, ref passed);
            PaletteHasNoSample(ref total, ref passed);
            EveryLevelCarriesBothBoards(ref total, ref passed);
        }

        // ---------------- 钉住的就是这两块，其余一律照旧可删 ----------------

        static void PinnedTypes(ref int total, ref int passed)
        {
            Expect(ref total, ref passed, "样例输入板钉住", GraphView.IsPinned("sample_input") ? 1 : 0, 1);
            Expect(ref total, ref passed, "样例对比板钉住", GraphView.IsPinned("sample_compare") ? 1 : 0, 1);
            Expect(ref total, ref passed, "开始模块照旧可删", GraphView.IsPinned("entry") ? 1 : 0, 0);
            Expect(ref total, ref passed, "空类型不误钉", GraphView.IsPinned(null) ? 1 : 0, 0);
            // 左侧栏现在能拖出来的每一种模块都必须可删，否则玩家会以为画板坏了
            foreach (var e in NodeCatalog.All)
                Expect(ref total, ref passed, "栏里「" + e.Title + "」可删", GraphView.IsPinned(e.NodeType) ? 1 : 0, 0);
        }

        // ---------------- 左侧栏不再有「样例」这一栏 ----------------

        static void PaletteHasNoSample(ref int total, ref int passed)
        {
            int sampleEntries = 0, cats = 0;
            var seen = new HashSet<string>();
            foreach (var e in NodeCatalog.All)
            {
                if (e.Cat == "sample") sampleEntries++;
                if (GraphView.IsPinned(e.NodeType)) sampleEntries++;
                if (!string.IsNullOrEmpty(e.Cat) && !seen.Contains(e.Cat)) { seen.Add(e.Cat); cats++; }
            }
            Expect(ref total, ref passed, "模块栏里样例条目数", sampleEntries, 0);
            Expect(ref total, ref passed, "一级目录里没有 sample", seen.Contains("sample") ? 1 : 0, 0);
            // 一级目录本来 5 个（io/flow/var/datastruct/sample），摘掉「样例」后剩 4 个
            Expect(ref total, ref passed, "剩余目录仍是 4 组", cats, 4);
        }

        // ---------------- 每一关自带这两块板（常驻的前提是关卡给得出来） ----------------

        static void EveryLevelCarriesBothBoards(ref int total, ref int passed)
        {
            int levels = 0, badIn = 0, badCmp = 0;
            foreach (var l in Levels.All)
            {
                levels++;
                if (l.Starter == null) continue;
                int a = Count(l.Starter, "sample_input"), b = Count(l.Starter, "sample_compare");
                // 序章第一关之类由 App 加载时补齐的关卡按「不许多也不缺」盯死：两块都恰好 1
                if (a != 1) { badIn++; Debug.Log("· " + l.Id + " 的样例输入板 " + a + " 块"); }
                if (b != 1) { badCmp++; Debug.Log("· " + l.Id + " 的样例对比板 " + b + " 块"); }
            }
            Expect(ref total, ref passed, "关卡总数大于 10", levels > 10 ? 1 : 0, 1);
            Expect(ref total, ref passed, "缺/多样例输入板的关卡数", badIn, 0);
            Expect(ref total, ref passed, "缺/多样例对比板的关卡数", badCmp, 0);
        }

        static int Count(Graph g, string type)
        {
            int k = 0;
            foreach (var n in g.Nodes) if (n != null && n.Type == type) k++;
            return k;
        }

        static void Expect(ref int total, ref int passed, string label, int actual, int want)
        {
            total++;
            if (actual == want)
            {
                passed++;
                Debug.Log("✔ " + Tag + " —— " + label + " = " + actual);
            }
            else Debug.Log("✘ " + Tag + " —— " + label + " 期望 " + want + "，实际 " + actual);
        }
    }
}
