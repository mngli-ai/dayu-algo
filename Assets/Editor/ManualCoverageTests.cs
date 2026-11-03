using System.Collections.Generic;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十七轮：说明书内容层（ManualData）与左栏/关卡的交叉覆盖。
    // 口径来自 Evidence/r17/manual-contract.md：条目要盖住每一个可拖模块、四条长度上限、分页越界必须夹紧。
    // 这套断言在引擎里再跑一遍，是为了让「改了卡名忘了改说明书」这种漂移在自测阶段就红，而不是等玩家翻手册发现缺页。
    public static class ManualCoverageTests
    {
        const string Tag = "说明书覆盖";

        // 长度上限（按 Unicode 码点计，标点和 ASCII 都算一字，从严）
        const int MaxSummary = 34, MaxStep = 30, MaxPitfall = 40, MaxExample = 24;

        public static void Run(ref int total, ref int passed)
        {
            CatalogCovered(ref total, ref passed);
            LengthCaps(ref total, ref passed);
            GroupMembership(ref total, ref passed);
            PageClamp(ref total, ref passed);
            SearchBehaviour(ref total, ref passed);
            ControlsTable(ref total, ref passed);
        }

        // ---------------- 左栏每一种可拖模块都得有自己的说明书条目 ----------------

        static void CatalogCovered(ref int total, ref int passed)
        {
            Expect(ref total, ref passed, "说明书条目总数（>=30）", ManualData.All.Length >= 30 ? 1 : 0, 1);
            int missing = 0;
            foreach (var e in NodeCatalog.All)
            {
                bool hit = false;
                foreach (var m in ManualData.All)
                    if (m.Title == e.Title) { hit = true; break; }
                if (!hit) { missing++; Debug.Log("· 左栏「" + e.Title + "」没有说明书条目"); }
            }
            Expect(ref total, ref passed, "左栏缺说明书条目的模块数", missing, 0);
            // Title 不许重复，否则 Search(Title) 会命中两条，跳转位置就不确定
            var seen = new HashSet<string>();
            int dup = 0;
            foreach (var m in ManualData.All) if (!seen.Add(m.Title)) dup++;
            Expect(ref total, ref passed, "重复的条目 Title 数", dup, 0);
        }

        // ---------------- 四条长度口径 + 步骤条数 ----------------

        static void LengthCaps(ref int total, ref int passed)
        {
            int badSummary = 0, badStep = 0, badPitfall = 0, badExample = 0, badSteps = 0, emptyExample = 0;
            foreach (var m in ManualData.All)
            {
                if (CodePoints(m.Summary) > MaxSummary) { badSummary++; Debug.Log("· " + m.Title + " 功能句 " + CodePoints(m.Summary) + " 字"); }
                if (CodePoints(m.Pitfall) > MaxPitfall) badPitfall++;
                if (CodePoints(m.Example) > MaxExample) badExample++;
                if (string.IsNullOrEmpty(m.Example)) emptyExample++;
                if (m.Steps == null || m.Steps.Length < 1 || m.Steps.Length > 4) badSteps++;
                else foreach (var s in m.Steps) if (CodePoints(s) > MaxStep) badStep++;
            }
            Expect(ref total, ref passed, "超长的功能句", badSummary, 0);
            Expect(ref total, ref passed, "超长的步骤", badStep, 0);
            Expect(ref total, ref passed, "超长的坑提示", badPitfall, 0);
            Expect(ref total, ref passed, "超长的写法示例", badExample, 0);
            Expect(ref total, ref passed, "步骤条数越界（要 1~4 条）", badSteps, 0);
            Expect(ref total, ref passed, "Example 空串（无写法要标「" + ManualData.NoExample + "」）", emptyExample, 0);
        }

        // ---------------- 每条都归在 Groups() 声明过的组里 ----------------

        static void GroupMembership(ref int total, ref int passed)
        {
            var gs = ManualData.Groups();
            Expect(ref total, ref passed, "分组数（>=6）", gs.Length >= 6 ? 1 : 0, 1);
            int bad = 0;
            foreach (var m in ManualData.All)
            {
                bool hit = false;
                foreach (var g in gs) if (g == m.Group) { hit = true; break; }
                if (!hit) { bad++; Debug.Log("· " + m.Title + " 的分组「" + m.Group + "」不在 Groups() 里"); }
            }
            Expect(ref total, ref passed, "分组名不在 Groups() 里的条目", bad, 0);
            // 每个组都得有内容，手风琴点开不能是空的
            int empty = 0;
            foreach (var g in gs)
            {
                bool any = false;
                foreach (var m in ManualData.All) if (m.Group == g) { any = true; break; }
                if (!any) { empty++; Debug.Log("· 空分组「" + g + "」"); }
            }
            Expect(ref total, ref passed, "空的分组", empty, 0);
        }

        // ---------------- 分页：越界一律夹紧，逐页拼回必须等于整表 ----------------

        static void PageClamp(ref int total, ref int passed)
        {
            int bad = 0, overflow = 0;
            var pers = new[] { 1, 4, 5, 7, ManualData.All.Length, ManualData.All.Length + 3, 0, -2 };
            var idxs = new[] { -10, -1, 0, 1, 2, 5, 99999 };
            foreach (int per in pers)
            {
                int pc = ManualData.PageCount(per);
                if (pc < 0) { bad++; continue; }
                int eff = per <= 0 ? ManualData.All.Length : per;
                int wantPages = (ManualData.All.Length + eff - 1) / eff;
                if (pc != wantPages) { bad++; Debug.Log("· PageCount(" + per + ") = " + pc + "，应为 " + wantPages); }
                var joined = new List<string>();
                for (int i = 0; i < pc; i++)
                {
                    var page = ManualData.Page(i, per);
                    if (page == null) { overflow++; continue; }
                    int wantLen = (i == pc - 1) ? ManualData.All.Length - i * eff : eff;
                    if (page.Length != wantLen) { overflow++; Debug.Log("· Page(" + i + "," + per + ") 长度 " + page.Length + "，应为 " + wantLen); }
                    foreach (var m in page) joined.Add(m.Title);
                }
                // 末页以外的越界索引必须夹到合法页，不能抛也不能给空
                foreach (int i in idxs)
                {
                    var p = ManualData.Page(i, per);
                    if (p == null || p.Length == 0) { overflow++; Debug.Log("· Page(" + i + "," + per + ") 返回空"); continue; }
                    if (i < 0 && p[0].Title != ManualData.All[0].Title) overflow++;
                    if (i >= pc && p[p.Length - 1].Title != ManualData.All[ManualData.All.Length - 1].Title) overflow++;
                }
                if (per > 0 && joined.Count != ManualData.All.Length)
                {
                    bad++;
                    Debug.Log("· per=" + per + " 逐页拼回 " + joined.Count + " 条，应为 " + ManualData.All.Length);
                }
                else if (per > 0)
                    for (int i = 0; i < joined.Count; i++)
                        if (joined[i] != ManualData.All[i].Title) { bad++; Debug.Log("· per=" + per + " 第 " + i + " 条对不上"); break; }
            }
            Expect(ref total, ref passed, "PageCount 与页数不一致", bad, 0);
            Expect(ref total, ref passed, "分页越界没夹紧 / 长度不对", overflow, 0);
        }

        // ---------------- 检索：空串给全部，关键词只给含词 ----------------

        static void SearchBehaviour(ref int total, ref int passed)
        {
            Expect(ref total, ref passed, "Search(\"\") 返回全部", ManualData.Search("").Length, ManualData.All.Length);
            Expect(ref total, ref passed, "Search(null) 返回全部", ManualData.Search(null).Length, ManualData.All.Length);
            int bad = 0;
            foreach (var m in ManualData.All)
            {
                var hits = ManualData.Search(m.Title);
                if (hits.Length < 1) { bad++; Debug.Log("· 用自己的 Title 搜不到「" + m.Title + "」"); continue; }
                bool exact = false;
                foreach (var h in hits) if (h.Title == m.Title) exact = true;
                if (!exact) { bad++; Debug.Log("· 搜「" + m.Title + "」没命中本条"); }
            }
            Expect(ref total, ref passed, "按自己 Title 搜不到的条目", bad, 0);
            Expect(ref total, ref passed, "乱码关键词命中数", ManualData.Search("zzz_不存在_zzz").Length, 0);
        }

        // ---------------- 操作总表：每行都得有「按键 + 做什么」 ----------------

        static void ControlsTable(ref int total, ref int passed)
        {
            Expect(ref total, ref passed, "操作总表行数（>=25）", ManualData.Controls.Length >= 25 ? 1 : 0, 1);
            int badKey = 0, badWhat = 0;
            var keys = new HashSet<string>();
            foreach (var c in ManualData.Controls)
            {
                if (string.IsNullOrEmpty(c.Key)) badKey++;
                else if (!keys.Add(c.Key)) { badKey++; Debug.Log("· 操作总表 Key 重复：「" + c.Key + "」"); }
                if (string.IsNullOrEmpty(c.What)) badWhat++;
            }
            Expect(ref total, ref passed, "操作总表空 Key / 重复 Key", badKey, 0);
            Expect(ref total, ref passed, "操作总表空说明", badWhat, 0);
        }

        static int CodePoints(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                n++;
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            }
            return n;
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
