using UnityEngine;
using Dayu.Core;

namespace Dayu.EditorTools
{
    // 第十六轮：关卡背景层。章节串是「序章 · 数组」这种带小节的写法，映射必须先看小节再看章前缀，
    // 否则序章七关全压在同一张图上。这里把 Backdrop.TextureKeyFor 当纯函数断言，并把「每关都能拿到一张已知背景」钉住。
    // 第十七轮补纵视差：背景横滚有变化、竖滚一动不动，根因是纵向振幅系数写成了远近倒挂
    // （远景 .10 > 近景 .08，两层上下摆得一样多 = 整张背景平移，读不出深度）。
    // VerticalParallax 把「近景摆幅处处 ≥ 远景」和「顶格位移不越 FarOverlap/NearBleed 容限」钉住，改系数必须先过它们。
    // 同一轮又实测：条带剪影被那个容限卡死，上下只摆 ±7/±16px，玩家照样读不出变化。SkyResponse 因此钉住真正扛纵向变化的
    // 那一路——滚满一屏时四段天空各自走满浅水↔深海全程、上亮下深不翻序、往下滚只往深海端单调走。
    public static class BackdropTests
    {
        const string Tag = "关卡背景";
        static readonly string[] Known = { "bg_dock", "bg_factory", "bg_shelf", "bg_deep" };

        public static void Run(ref int total, ref int passed)
        {
            ChapterMapping(ref total, ref passed);
            Fallback(ref total, ref passed);
            EveryLevelHasABackdrop(ref total, ref passed);
            VerticalParallax(ref total, ref passed);
            SkyResponse(ref total, ref passed);
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

        // ---------------- 纵视差：近景摆幅 ≥ 远景，且顶格位移不撕开两条之间与视口底 ----------------

        static void VerticalParallax(ref int total, ref int passed)
        {
            ExpectInt(ref total, ref passed, "纵振幅系数近景大于远景",
                      Backdrop.NearYAmpK > Backdrop.FarYAmpK ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "纵视差系数近景大于远景",
                      Backdrop.NearYParallax > Backdrop.FarYParallax ? 1 : 0, 1);

            // 条高取 200px：实机能出现的最高的近景条是「半张 160x80 贴图的下半 40px × BaseScale3 × Lerp(1,1.8,.55)」≈173，
            // 用比它更大的整数顶格算，容限断言只会比实机更严。
            const float H = 200f;
            int reversed = 0, tear = 0, bleed = 0, nonFinite = 0, nonMonotone = 0;
            float prevFar = -1f, prevNear = -1f;
            for (int step = 0; step <= 80; step++)
            {
                float camY = step * 100f;              // 扫到 ±8000 视图像素，远超画布实际跨度
                Vector2 a = Backdrop.YOffsetsFor(camY, H, H);
                Vector2 b = Backdrop.YOffsetsFor(-camY, H, H);
                if (float.IsNaN(a.x) || float.IsNaN(a.y) || float.IsInfinity(a.x) || float.IsInfinity(a.y)) nonFinite++;
                if (Mathf.Abs(a.y) < Mathf.Abs(a.x)) reversed++;
                // 两条之间的裂口：远景条顶边必须仍然盖过近景条顶边，容限由 FarOverlap 兜住
                if (Mathf.Abs(a.y - a.x) > Backdrop.FarOverlap) tear++;
                // 视口底露空：近景条上移的量不许超过它向下多铺的 NearBleed
                if (Mathf.Abs(a.y) > Backdrop.NearBleed) bleed++;
                // 上下滚时摆幅只能单调变大直到饱和，中途回落会让滚动读起来发抖
                if (step > 0 && (Mathf.Abs(a.x) < prevFar - 1e-4f || Mathf.Abs(a.y) < prevNear - 1e-4f)) nonMonotone++;
                prevFar = Mathf.Abs(a.x); prevNear = Mathf.Abs(a.y);
                // 限幅函数是奇函数：往上滚和往下滚的位移必须等大反向，否则中线歪了
                if (Mathf.Abs(a.x + b.x) > 1e-3f || Mathf.Abs(a.y + b.y) > 1e-3f) nonFinite++;
            }
            ExpectInt(ref total, ref passed, "滚到极端也不出 NaN/无穷且上下对称", nonFinite, 0);
            ExpectInt(ref total, ref passed, "近景纵向摆幅处处不小于远景", reversed, 0);
            ExpectInt(ref total, ref passed, "两位移之差不撕开远景与近景", tear, 0);
            ExpectInt(ref total, ref passed, "近景上移不越过下沿余量", bleed, 0);
            ExpectInt(ref total, ref passed, "摆幅随滚动距离单调增至饱和", nonMonotone, 0);

            Vector2 mid = Backdrop.YOffsetsFor(0f, H, H);
            ExpectInt(ref total, ref passed, "镜头在原点时两层不偏移",
                      (mid.x == 0f && mid.y == 0f) ? 1 : 0, 1);
        }

        // ---------------- 天空渐变：纵向滚动的「无界」通道，摆幅不受条带几何限制 ----------------
        // 条带剪影上下最多摆 ±7/±16px（再大就撕裂、露底），玩家「竖着滚背景没变化」实际靠的是这一路：
        // 滚满一屏（sY 从 -1 到 1）时四段天空各自走满浅水↔深海全程。断言走满量，不断言系数写死没改。

        static void SkyResponse(ref int total, ref int passed)
        {
            int outOfRange = 0, inverted = 0, rising = 0;
            for (int step = -40; step <= 40; step++)
            {
                float sY = step * .05f;
                float prevBand = 2f;
                for (int i = 0; i < 4; i++)
                {
                    float t = Backdrop.SkyT(i, sY);
                    if (t < -1e-4f || t > 1f + 1e-4f) outOfRange++;
                    // 上亮下深的次序不能翻：靠顶的第 i 段永远不比下面那段更暗
                    if (t > prevBand + 1e-4f) inverted++;
                    prevBand = t;
                }
            }
            for (int i = 0; i < 4; i++)
            {
                float prev = Backdrop.SkyT(i, -1f);
                for (int step = -39; step <= 40; step++)
                {
                    float t = Backdrop.SkyT(i, step * .05f);
                    // 往下滚（sY 增大）= 往深海，因子只许降不许升，中途回升读起来是变色抖了一下
                    if (t > prev + 1e-4f) rising++;
                    prev = t;
                }
            }
            ExpectInt(ref total, ref passed, "天空因子恒被夹在 0..1", outOfRange, 0);
            ExpectInt(ref total, ref passed, "四段上亮下深永不翻序", inverted, 0);
            ExpectInt(ref total, ref passed, "往下滚天空只往深海端单调变化", rising, 0);

            for (int i = 0; i < 4; i++)
            {
                float tv = 0f, prev = Backdrop.SkyT(i, -1f);
                for (int step = -39; step <= 40; step++)
                {
                    float t = Backdrop.SkyT(i, step * .05f);
                    tv += Mathf.Abs(t - prev);
                    prev = t;
                }
                // 每段都该从浅水端一路走到深海端（全程 = 1）；被 Clamp 顶住的部分不计入，走满量仍要接近 1
                ExpectInt(ref total, ref passed, "第 " + i + " 段滚满一屏走满浅水到深海", tv >= .9f ? 1 : 0, 1);
            }

            int live = 0;
            for (int i = 0; i < 4; i++)
                if (Mathf.Abs(Backdrop.SkyT(i, 0f) - Backdrop.SkyT(i, .5f)) > 1e-3f) live++;
            ExpectInt(ref total, ref passed, "只滚半屏也至少 3 段天空在变", live >= 3 ? 1 : 0, 1);

            ExpectInt(ref total, ref passed, "原点天空与顶格天空不重合",
                      (Mathf.Abs(Backdrop.SkyT(1, 0f) - Backdrop.SkyT(1, 1f)) > .5f) ? 1 : 0, 1);

            // 变色要看得见，两端的不透明度就得拉开一档；但深端仍须留在「背景不抢卡片」的档内（≤.40，与条带同规）。
            ExpectInt(ref total, ref passed, "天空深端比浅端压得够暗（差 ≥ .15）",
                      (Backdrop.SkyAlphaDeep > Backdrop.SkyAlphaLight &&
                       Backdrop.SkyAlphaDeep - Backdrop.SkyAlphaLight >= .15f) ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "天空深端不越过背景不透明度上限 .40",
                      (Backdrop.SkyAlphaDeep <= .40f && Backdrop.SkyAlphaLight >= .05f) ? 1 : 0, 1);
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
