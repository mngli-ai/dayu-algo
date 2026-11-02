using System.Collections.Generic;
using Dayu.Core;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 关卡内容自测（第十三轮）：题面四段（背景故事/题干/数据范围/提示）齐不齐、
    // 「样板 + 抽查」的样例排布对不对、题面用词有没有混进代码语法。
    // 隐藏样例的期望值本身由 LevelExprTests 拿 Vm 跑标准答案图来当裁判，这里只管形状。
    public static class LevelContentTests
    {
        // 题面正文（Story/Problem/DataRange）里禁止出现的代码语法片段：像素风全中文界面，题面不掺 C++。
        // 注意是「拉丁片段/符号」级别的黑名单，中文的「栈」「数组」暂时还允许出现，别误报。
        static readonly string[] BannedTokens = { "int", "for", "while", "[]", "{}", ";" };

        public static void Run(ref int total, ref int passed)
        {
            foreach (var l in Levels.All)
            {
                // 一、题面四段齐不齐：新排式全靠这三段正文，缺一段右栏就开天窗。
                Check(ref total, ref passed, l.Id + " 有背景故事", !string.IsNullOrEmpty(l.Story), true);
                Check(ref total, ref passed, l.Id + " 有题干", !string.IsNullOrEmpty(l.Problem), true);
                Check(ref total, ref passed, l.Id + " 有数据范围", !string.IsNullOrEmpty(l.DataRange), true);

                // 二、样例排布：可见（样板）至少 1 条、全部排在隐藏（抽查）前面；抽查至少 1 条。
                var vis = LevelBrief.Visible(l);
                var hid = LevelBrief.Hidden(l);
                Check(ref total, ref passed, l.Id + " 样板至少 1 条", vis.Count >= 1, true);
                Check(ref total, ref passed, l.Id + " 有抽查样例", LevelBrief.HasHidden(l) && LevelBrief.HiddenCount(l) > 0, true);
                int firstHidden = l.Tests.Count, lastVisible = -1;
                for (int i = 0; i < l.Tests.Count; i++)
                {
                    if (l.Tests[i].Visible) lastVisible = i;
                    else if (i < firstHidden) firstHidden = i;
                }
                // UI 拿 Find(x=>x.Visible) 和索引切换样例，可见段必须整个压在隐藏段前面。
                Check(ref total, ref passed, l.Id + " 样板全部排在抽查前面（可见末位 " + lastVisible + " < 隐藏首位 " + firstHidden + "）",
                    vis.Count == 0 || lastVisible < firstHidden, true);
                // 保序筛的下标关系：LevelBrief 两段拼回去就是原表，顺序不许打乱。
                var stitched = new List<LevelTest>(vis); stitched.AddRange(hid);
                bool sameOrder = stitched.Count == l.Tests.Count;
                for (int i = 0; i < stitched.Count && sameOrder; i++) sameOrder = ReferenceEquals(stitched[i], l.Tests[i]);
                Check(ref total, ref passed, l.Id + " 可见加隐藏等于原表且保序", sameOrder, true);
                Check(ref total, ref passed, l.Id + " 抽查条数达标（" + l.Id + " 需 " + (l.Id == "P0" ? 1 : 3) + "）",
                    LevelBrief.HiddenCount(l) >= (l.Id == "P0" ? 1 : 3), true);

                // 三、用词：题面正文不许出现代码语法片段（黑名单见 BannedTokens）。
                foreach (var field in new[] { l.Story, l.Problem, l.DataRange })
                {
                    string hit = null;
                    foreach (var b in BannedTokens)
                        if (field != null && field.IndexOf(b, System.StringComparison.OrdinalIgnoreCase) >= 0) { hit = b; break; }
                    Check(ref total, ref passed, l.Id + " 题面不含代码语法「" + (hit ?? "") + "」", hit == null, true);
                }

                // 四、提示栏每条一句、不超过 34 字（右栏折行宽约 310px，超长会把滚动条撑爆）。
                if (l.Tips != null)
                    foreach (var tip in l.Tips)
                        Check(ref total, ref passed, l.Id + " 提示不超 34 字（" + tip + "，" + tip.Length + " 字）", tip.Length <= 34, true);
            }

            // 五、判定机制讲解只在序章第一关亮一次。
            int firstPrologue = Levels.All.FindIndex(l => l.Free);
            Check(ref total, ref passed, "序章第一关已找到", firstPrologue >= 0, true);
            if (firstPrologue >= 0)
                Check(ref total, ref passed, "序章第一关「" + Levels.All[firstPrologue].Id + "」讲了样板与抽查",
                    Levels.All[firstPrologue].TeachJudge, true);
            foreach (var l in Levels.All)
                if (Levels.All.IndexOf(l) != firstPrologue)
                    Check(ref total, ref passed, l.Id + " 不重复讲判定机制", l.TeachJudge, false);

            // 六、判定一行话：手写期望串对照，条数口径必须和样例表一致。
            Check(ref total, ref passed, "P0 判定行", LevelBrief.JudgeLine(Find("P0")), "样板 1 条 · 抽查 1 条");
            Check(ref total, ref passed, "S1 判定行", LevelBrief.JudgeLine(Find("S1")), "样板 3 条 · 抽查 4 条");
            Check(ref total, ref passed, "L2 判定行", LevelBrief.JudgeLine(Find("L2")), "样板 3 条 · 抽查 5 条");
            Check(ref total, ref passed, "L3 判定行", LevelBrief.JudgeLine(Find("L3")), "样板 6 条 · 抽查 5 条");
            foreach (var l in Levels.All)
                Check(ref total, ref passed, l.Id + " 判定行条数与样例表一致",
                    LevelBrief.JudgeLine(l), "样板 " + LevelBrief.Visible(l).Count + " 条 · 抽查 " + LevelBrief.Hidden(l).Count + " 条");

            // 七、样例版式：一条输入配一条输出交替成节；标题带序号（单条不带），
            // 正文是样例原文（自带换行照留），不许再有「第 k 组：」这种前缀。
            Check(ref total, ref passed, "P1 样例成节（输入配输出交替）", Sections(Find("P1")),
                "输入样例 1|5\n输出样例 1|5\n输入样例 2|-3\n输出样例 2|-3\n输入样例 3|0\n输出样例 3|0");
            Check(ref total, ref passed, "P0 单条样例标题不带序号（无输入则正文空）", Sections(Find("P0")),
                "输入样例|\n输出样例|13");
            Check(ref total, ref passed, "S1 样例正文保留自带换行", Sections(Find("S1")),
                "输入样例 1|3\n1 2 3\n输出样例 1|3\n2\n1\n输入样例 2|1\n7\n输出样例 2|7\n输入样例 3|4\n9 -2 5 0\n输出样例 3|0\n5\n-2\n9");
        }

        static Level Find(string id) { return Levels.All.Find(l => l.Id == id); }

        // 分节结果压成可对照的一串：每节「标题|正文」，节之间换行（正文自己的换行原样留在里面）。
        static string Sections(Level l)
        {
            var parts = new List<string>();
            foreach (var s in LevelBrief.SampleSections(l)) parts.Add(s.Heading + "|" + s.Body);
            return string.Join("\n", parts.ToArray());
        }

        static void Check(ref int total, ref int passed, string label, bool ok, bool want)
        {
            total++;
            if (ok == want) { passed++; Debug.Log("✔ 关卡内容 —— " + label); }
            else Debug.Log("✘ 关卡内容 —— " + label + " 期望 " + want + "，实际 " + ok);
        }

        static void Check(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 关卡内容 —— " + label + " = 「" + actual.Replace("\n", "\\n") + "」"); }
            else Debug.Log("✘ 关卡内容 —— " + label + " 期望「" + want.Replace("\n", "\\n") + "」，实际「" + (actual ?? "").Replace("\n", "\\n") + "」");
        }
    }
}
