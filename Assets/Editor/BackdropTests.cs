using UnityEngine;
using Dayu.Core;

namespace Dayu.EditorTools
{
    // 第十六轮：关卡背景层。章节串是「序章 · 数组」这种带小节的写法，映射必须先看小节再看章前缀，
    // 否则序章七关全压在同一张图上。这里把 Backdrop.TextureKeyFor 当纯函数断言，并把「每关都能拿到一张已知背景」钉住。
    public static class BackdropTests
    {
        const string Tag = "关卡背景";
        static readonly string[] Known = { "bg_dock", "bg_factory", "bg_shelf", "bg_deep" };

        public static void Run(ref int total, ref int passed)
        {
            ChapterMapping(ref total, ref passed);
            Fallback(ref total, ref passed);
            EveryLevelHasABackdrop(ref total, ref passed);
        }

        // ---------------- 小节关键词优先于章前缀 ----------------

        static void ChapterMapping(ref int total, ref int passed)
        {
            ExpectStr(ref total, ref passed, "序章·数组走货架", Backdrop.TextureKeyFor("序章 · 数组"), "bg_shelf");
            ExpectStr(ref total, ref passed, "条件分支走厂房", Backdrop.TextureKeyFor("序章 · 条件分支"), "bg_factory");
            ExpectStr(ref total, ref passed, "循环走厂房", Backdrop.TextureKeyFor("序章 · 循环"), "bg_factory");
            ExpectStr(ref total, ref passed, "栈与队列走深海", Backdrop.TextureKeyFor("第一章 · 栈与队列"), "bg_deep");
            ExpectStr(ref total, ref passed, "序章开头不挡住数组", Backdrop.TextureKeyFor("序章 · 数组"), "bg_shelf");
            ExpectStr(ref total, ref passed, "其余序章走码头", Backdrop.TextureKeyFor("序章 · 从零开始"), "bg_dock");
        }

        // ---------------- 空串、null、没见过的章节都回落，绝不抛 ----------------

        static void Fallback(ref int total, ref int passed)
        {
            ExpectStr(ref total, ref passed, "null 回落码头", Backdrop.TextureKeyFor(null), "bg_dock");
            ExpectStr(ref total, ref passed, "空串回落码头", Backdrop.TextureKeyFor(""), "bg_dock");
            ExpectStr(ref total, ref passed, "未知章节回落码头", Backdrop.TextureKeyFor("第二章 · 图论"), "bg_dock");
        }

        // ---------------- 每一关都命中已知贴图，且不同章节真的换图 ----------------

        static void EveryLevelHasABackdrop(ref int total, ref int passed)
        {
            var used = new System.Collections.Generic.HashSet<string>();
            bool allKnown = true;
            foreach (var l in Levels.All)
            {
                string k = Backdrop.TextureKeyFor(l == null ? null : l.Chapter);
                if (System.Array.IndexOf(Known, k) < 0) allKnown = false;
                used.Add(k);
            }
            total++;
            if (allKnown) { passed++; Debug.Log("✔ " + Tag + " —— 全部 " + Levels.All.Count + " 关都映射到已知背景"); }
            else Debug.Log("✘ " + Tag + " —— 有关卡映射到不存在的背景名");

            ExpectInt(ref total, ref passed, "至少切出 3 种背景", used.Count >= 3 ? 1 : 0, 1);
        }

        static void ExpectStr(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want)
            {
                passed++;
                Debug.Log("✔ " + Tag + " —— " + label + " = " + actual);
            }
            else Debug.Log("✘ " + Tag + " —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void ExpectInt(ref int total, ref int passed, string label, int actual, int want)
        {
            total++;
            if (actual == want)
            {
                passed++;
                Debug.Log("✔ " + Tag + " —— " + label);
            }
            else Debug.Log("✘ " + Tag + " —— " + label + " 期望 " + want + "，实际 " + actual);
        }
    }
}
