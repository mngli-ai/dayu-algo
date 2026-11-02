using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十六轮：右侧题栏的滚动位置。玩家报的是「点『看提示』、点画布上的模块，题栏都会瞬间弹回最顶部」。
    // 根因是整栏重排时先把滚动偏移钳回 0。这里把「重排后该停在哪儿」写成纯函数的数字断言：
    // 同关重排必须原位不动，只有换关回顶，新内容变矮时钳到底而不是回顶。
    public static class InspectorScrollTests
    {
        const string Tag = "题栏滚动";

        public static void Run(ref int total, ref int passed)
        {
            KeepOnRebuild(ref total, ref passed);
            ResetOnlyOnLevelChange(ref total, ref passed);
            ClampToNewExtent(ref total, ref passed);
            DegenerateInputs(ref total, ref passed);
        }

        // ---------------- 同关重排保住原位 ----------------

        static void KeepOnRebuild(ref int total, ref int passed)
        {
            // 玩家滚到 500 处读题面，点「看提示」触发整栏重排，新内容更长，位置该原样留着
            Expect(ref total, ref passed, "点提示重排后仍在 500", App.PaletteScroll.OffsetAfter(false, 500f, 900f), 500f);
            // 选中画布上的模块，题栏多了参数区，同样不该弹回顶部
            Expect(ref total, ref passed, "点模块重排后仍在 500", App.PaletteScroll.OffsetAfter(false, 500f, 1400f), 500f);
            // 已经在中间，重排前后 extent 一样，数值必须一模一样
            Expect(ref total, ref passed, "extent 不变则偏移不变", App.PaletteScroll.OffsetAfter(false, 328f, 1398f), 328f);
        }

        // ---------------- 只有换关回顶 ----------------

        static void ResetOnlyOnLevelChange(ref int total, ref int passed)
        {
            Expect(ref total, ref passed, "换关一律回顶", App.PaletteScroll.OffsetAfter(true, 500f, 900f), 0f);
            // 换关时哪怕新关短得没有可滚量，也必须是 0 而不是被钳成别的数
            Expect(ref total, ref passed, "换关且无滚动余量仍是顶", App.PaletteScroll.OffsetAfter(true, 500f, 0f), 0f);
        }

        // ---------------- 新内容比原来矮：钳到底，不是回顶 ----------------

        static void ClampToNewExtent(ref int total, ref int passed)
        {
            // 收起提示后内容短了一截，原来 900 的位置已经没有内容，应该停在新的最底部而不是弹回 0
            Expect(ref total, ref passed, "内容变矮钳到新底部", App.PaletteScroll.OffsetAfter(false, 900f, 620f), 620f);
            Expect(ref total, ref passed, "恰好停在底部", App.PaletteScroll.OffsetAfter(false, 620f, 620f), 620f);
            // 顶部本来就没有可滚内容（题栏收起 / 内容不足一屏），任何位置都归 0
            Expect(ref total, ref passed, "无可滚量归顶", App.PaletteScroll.OffsetAfter(false, 480f, 0f), 0f);
        }

        // ---------------- 脏输入：负数、无余量 ----------------

        static void DegenerateInputs(ref int total, ref int passed)
        {
            Expect(ref total, ref passed, "负偏移当顶", App.PaletteScroll.OffsetAfter(false, -120f, 800f), 0f);
            Expect(ref total, ref passed, "负可滚量当无", App.PaletteScroll.OffsetAfter(false, 50f, -30f), 0f);
            Expect(ref total, ref passed, "顶到顶保持顶", App.PaletteScroll.OffsetAfter(false, 0f, 800f), 0f);
        }

        static void Expect(ref int total, ref int passed, string label, float actual, float want)
        {
            total++;
            if (UnityEngine.Mathf.Abs(actual - want) < 0.01f)
            {
                passed++;
                Debug.Log("✔ " + Tag + " —— " + label + " = " + actual.ToString("F2"));
            }
            else Debug.Log("✘ " + Tag + " —— " + label + " 期望 " + want.ToString("F2") + "，实际 " + actual.ToString("F2"));
        }
    }
}
