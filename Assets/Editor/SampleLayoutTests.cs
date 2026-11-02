using System.Collections.Generic;
using Dayu.Core;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十四轮：样例版式改成洛谷那种「一组输入配一组输出」。
    // LevelBrief.SampleSections 是纯函数，这里只验它的分节形状；像素间距由右栏渲染，截图核对。
    public static class SampleLayoutTests
    {
        public static void Run(ref int total, ref int passed)
        {
            SingleSampleCases(ref total, ref passed);
            AlternateCases(ref total, ref passed);
            RawBodyCases(ref total, ref passed);
            HiddenCases(ref total, ref passed);
            EmptyCases(ref total, ref passed);
            RealLevelCases(ref total, ref passed);
        }

        static Level Lvl(string id, params LevelTest[] tests)
        {
            return new Level { Id = id, Tests = new List<LevelTest>(tests) };
        }

        static LevelTest Hid(string i, string o)
        {
            return new LevelTest(i, o) { Visible = false };
        }

        // 一条样例时标题不带序号：带序号会让玩家以为一份数据里只截了一条。
        static void SingleSampleCases(ref int total, ref int passed)
        {
            var s = LevelBrief.SampleSections(Lvl("ONE", new LevelTest("5", "5")));
            Check(ref total, ref passed, "单条样例切两节", s.Count, 2);
            Check(ref total, ref passed, "单条样例输入标题无序号", s[0].Heading, "输入样例");
            Check(ref total, ref passed, "单条样例输出标题无序号", s[1].Heading, "输出样例");
            Check(ref total, ref passed, "单条样例正文是原文", s[0].Body + "/" + s[1].Body, "5/5");
        }

        // 多条样例严格「输入 1 / 输出 1 / 输入 2 / 输出 2 …」，组尾标记只落在输出节上。
        static void AlternateCases(ref int total, ref int passed)
        {
            var s = LevelBrief.SampleSections(Lvl("ALT",
                new LevelTest("3 5", "8"), new LevelTest("-2 7", "5"), new LevelTest("0 0", "0")));
            Check(ref total, ref passed, "三条样例切成六节", s.Count, 6);
            Check(ref total, ref passed, "标题严格交替带序号", Headings(s),
                "输入样例 1/输出样例 1/输入样例 2/输出样例 2/输入样例 3/输出样例 3");
            bool ends = true;
            for (int i = 0; i < s.Count; i++) ends &= s[i].GroupEnd == (i % 2 == 1);
            Check(ref total, ref passed, "组尾标记只在输出节上", ends, true);
        }

        // 正文不许再挂「第 k 组：」前缀；Input/Output 自带的换行原样留着，渲染层按 \n 分行。
        static void RawBodyCases(ref int total, ref int passed)
        {
            var s = LevelBrief.SampleSections(Lvl("RAW",
                new LevelTest("5\n1 2 3 4 5", "15"), new LevelTest("1\n7", "7")));
            Check(ref total, ref passed, "多行输入正文原样保留", s[0].Body, "5\n1 2 3 4 5");
            Check(ref total, ref passed, "多行输出正文原样保留", s[1].Body, "15");
            bool prefixed = s.Exists(x => x.Body.StartsWith("第 ") || x.Body.Contains("组："));
            Check(ref total, ref passed, "正文没有「第 k 组：」前缀", prefixed, false);
        }

        // 抽查（隐藏）样例一条都不能进题栏列表。
        static void HiddenCases(ref int total, ref int passed)
        {
            var s = LevelBrief.SampleSections(Lvl("HID",
                new LevelTest("3", "7"), Hid("987654321", "123456789"), Hid("偷放进去的抽查串", "抽查答案串")));
            Check(ref total, ref passed, "隐藏样例不参与分节", s.Count, 2);
            Check(ref total, ref passed, "隐藏样例内容不出现在标题或正文里", Joined(s).Contains("抽查"), false);
            Check(ref total, ref passed, "可见样例标题也不因隐藏样例存在而变", Headings(s), "输入样例/输出样例");
        }

        // 关卡为空 / 没填样例 / 样例字段为 null：只回空列表或空正文，不许抛。
        static void EmptyCases(ref int total, ref int passed)
        {
            Check(ref total, ref passed, "关卡没有样例返回空列表", Count(Lvl("NONE")), 0);
            var noTests = new Level { Id = "NULLTESTS", Tests = null };
            Check(ref total, ref passed, "样例表为 null 返回空列表", Count(noTests), 0);
            try
            {
                Check(ref total, ref passed, "关卡为 null 返回空列表不抛异常", Count(null), 0);
            }
            catch (System.Exception e) { Check(ref total, ref passed, "关卡为 null 不抛异常（" + e.Message + "）", false, true); }
            try
            {
                var s = LevelBrief.SampleSections(Lvl("NULLFIELD", new LevelTest(null, null)));
                Check(ref total, ref passed, "样例字段为 null 正文退化成空串",
                    s[0].Body == "" && s[1].Body == "", true);
            }
            catch (System.Exception e) { Check(ref total, ref passed, "样例字段为 null 不抛异常（" + e.Message + "）", false, true); }
        }

        // 拿真实关卡表复核：节数与可见条数挂钩、第 k 组正文逐字等于第 k 条可见样例，
        // 结构上隐藏样例就没有入口；再加一条「正文不撞抽查独有串」兜底。
        static void RealLevelCases(ref int total, ref int passed)
        {
            foreach (var l in Levels.All)
            {
                var vis = LevelBrief.Visible(l);
                var hid = LevelBrief.Hidden(l);
                var s = LevelBrief.SampleSections(l);
                Check(ref total, ref passed, l.Id + " 节数是可见条数的两倍", s.Count, vis.Count * 2);
                bool align = s.Count == vis.Count * 2;
                for (int k = 0; align && k < vis.Count; k++)
                {
                    string tail = vis.Count > 1 ? " " + (k + 1) : "";
                    align &= s[2 * k].Heading == "输入样例" + tail && s[2 * k].Body == (vis[k].Input ?? "");
                    align &= s[2 * k + 1].Heading == "输出样例" + tail && s[2 * k + 1].Body == (vis[k].Output ?? "");
                }
                Check(ref total, ref passed, l.Id + " 每组标题正文对得上自己的可见样例", align, true);
                Check(ref total, ref passed, l.Id + " 正文一律没有「第 k 组：」前缀",
                    s.Exists(x => x.Body.StartsWith("第 ")), false);
                foreach (var h in hid)
                {
                    bool shared = vis.Exists(t => t.Input == h.Input || t.Output == h.Input
                                                || t.Input == h.Output || t.Output == h.Output);
                    if (shared || string.IsNullOrEmpty(h.Input) || string.IsNullOrEmpty(h.Output)) continue;
                    Check(ref total, ref passed, l.Id + " 抽查样例独有的串不作为整节正文出现",
                        s.Exists(x => x.Body == h.Input || x.Body == h.Output), false);
                }
            }
        }

        static int Count(Level l) { return LevelBrief.SampleSections(l).Count; }
        static string Headings(List<BriefSection> s) { return string.Join("/", s.ConvertAll(x => x.Heading).ToArray()); }
        static string Joined(List<BriefSection> s) { return string.Join("\n", s.ConvertAll(x => x.Heading + "\n" + x.Body).ToArray()); }

        static void Check(ref int total, ref int passed, string label, bool ok, bool want)
        {
            total++;
            if (ok == want) { passed++; Debug.Log("✔ 样例版式 —— " + label); }
            else Debug.Log("✘ 样例版式 —— " + label + " 期望 " + want + "，实际 " + ok);
        }

        static void Check(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 样例版式 —— " + label + " = 「" + (actual ?? "").Replace("\n", "\\n") + "」"); }
            else Debug.Log("✘ 样例版式 —— " + label + " 期望「" + (want ?? "").Replace("\n", "\\n") + "」，实际「" + (actual ?? "").Replace("\n", "\\n") + "」");
        }

        static void Check(ref int total, ref int passed, string label, int actual, int want)
        {
            Check(ref total, ref passed, label, actual.ToString(), want.ToString());
        }
    }
}
