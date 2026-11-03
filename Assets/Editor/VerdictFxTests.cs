using System.IO;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十七轮：AC/WA 的音效与特效。VerdictFx 的数学全在一族纯函数里（协程只落值不算数），
    // 所以这里逐条盯住「彩带不许飘出容器、演完必须归零、整像素、种子可重演」这些口径；
    // 再加一把源码扫描 —— 特效类写得再好，App 里没人调用就等于没做，上一轮正是停在这一步。
    public static class VerdictFxTests
    {
        const string Tag = "判题特效";

        static readonly VerdictKind[] AllKinds =
            { VerdictKind.Ac, VerdictKind.Wa, VerdictKind.LevelClear, VerdictKind.SamplePass, VerdictKind.SampleFail };

        public static void Run(ref int total, ref int passed)
        {
            Seeds(ref total, ref passed);
            SeverityLadder(ref total, ref passed);
            ParticlesStayInside(ref total, ref passed);
            FadeEndsClean(ref total, ref passed);
            AnchorMotion(ref total, ref passed);
            WaCross(ref total, ref passed);
            CelebrationAnchors(ref total, ref passed);
            WiredIntoApp(ref total, ref passed);
        }

        // ---------------- 种子：同关重跑必须逐粒一致，且不许出现全 0 ----------------

        static void Seeds(ref int total, ref int passed)
        {
            int a = VerdictFx.SeedFor("L2", 1, true);
            Expect(ref total, ref passed, "同参数两次种子相等", a == VerdictFx.SeedFor("L2", 1, true) ? 1 : 0, 1);
            Expect(ref total, ref passed, "种子非 0（全 0 会让头几颗挤在同一角度）", a != 0 ? 1 : 0, 1);
            Expect(ref total, ref passed, "换关卡种子就变", a != VerdictFx.SeedFor("L3", 1, true) ? 1 : 0, 1);
            Expect(ref total, ref passed, "换样例序号种子就变", a != VerdictFx.SeedFor("L2", 2, true) ? 1 : 0, 1);
            Expect(ref total, ref passed, "结论相反种子就变", a != VerdictFx.SeedFor("L2", 1, false) ? 1 : 0, 1);
            Expect(ref total, ref passed, "关卡名为空也不炸且非 0",
                VerdictFx.SeedFor(null, 0, true) != 0 ? 1 : 0, 1);
            int unitBad = 0;
            for (int i = 0; i < 4000; i++)
            {
                int h = VerdictFx.Mix32(unchecked(i * unchecked((int)0x9E3779B9)));
                float u = VerdictFx.Unit01(h), s = VerdictFx.SignedUnit(h);
                // 闭开区间：取到 1.0 会让「按百分比选形状」越界取到 pal.Length
                if (!(u >= 0f && u < 1f) || !(s >= -1f && s < 1f)) unitBad++;
            }
            Expect(ref total, ref passed, "Unit01/SignedUnit 落在 [0,1) 与 [-1,1)", unitBad, 0);
        }

        // ---------------- 严重度阶梯：越重的结论喷得越多 ----------------

        static void SeverityLadder(ref int total, ref int passed)
        {
            int bad = 0;
            for (int r = 0; r + 1 < AllKinds.Length; r++)
                if (VerdictFx.BurstCount(KindByRank(r)) > VerdictFx.BurstCount(KindByRank(r + 1))) bad++;
            Expect(ref total, ref passed, "颗数随严重度单调不减", bad, 0);
            Expect(ref total, ref passed, "AC 喷 40 颗", VerdictFx.BurstCount(VerdictKind.Ac), 40);
            Expect(ref total, ref passed, "单条样例只给 3 点火星以内",
                VerdictFx.BurstCount(VerdictKind.SamplePass) <= 3 ? 1 : 0, 1);
            Expect(ref total, ref passed, "通关礼花颗数最多",
                VerdictFx.BurstCount(VerdictKind.LevelClear) >= VerdictFx.BurstCount(VerdictKind.Ac) ? 1 : 0, 1);
            Expect(ref total, ref passed, "每一档时长都 > 0（协程按它收尾）",
                AllKinds.Length == CountPositiveLife() ? 1 : 0, 1);
            // 音效分层：只有本轮新增的两条 cue 由特效顺手播，既有 pass/fail/ac/clear 仍归 App，双响会变噪音
            Expect(ref total, ref passed, "AC 的 cue = confetti", VerdictFx.CueFor(VerdictKind.Ac) == "confetti" ? 1 : 0, 1);
            Expect(ref total, ref passed, "WA 的 cue = wa", VerdictFx.CueFor(VerdictKind.Wa) == "wa" ? 1 : 0, 1);
            int cueBad = 0;
            foreach (var k in AllKinds)
                if (k != VerdictKind.Ac && k != VerdictKind.Wa && !string.IsNullOrEmpty(VerdictFx.CueFor(k))) cueBad++;
            Expect(ref total, ref passed, "其余档位不发声的个数", cueBad, 0);
        }

        static int CountPositiveLife()
        {
            int n = 0;
            foreach (var k in AllKinds) if (VerdictFx.BurstSeconds(k) > 0f) n++;
            return n;
        }

        static VerdictKind KindByRank(int rank)
        {
            foreach (var k in AllKinds) if (VerdictFx.SeverityRank(k) == rank) return k;
            return VerdictKind.LevelClear;      //  rank 4
        }

        // ---------------- 粒子：整条生命周期都留在容器 200px 内，且逐粒落在整像素上 ----------------

        static void ParticlesStayInside(ref int total, ref int passed)
        {
            int outside = 0, fractional = 0, nonPositiveSize = 0, startOffAnchor = 0, overStagger = 0, overWindow = 0;
            foreach (var kind in AllKinds)
            {
                int seed = VerdictFx.SeedFor("L2", 0, true);
                int n = VerdictFx.BurstCount(kind);
                float stag = KindStagger(kind);
                for (int i = 0; i < n; i++)
                {
                    var p = VerdictFx.SeedOf(i, seed, kind);
                    if (p.Size.x < 1f || p.Size.y < 1f) nonPositiveSize++;
                    // 错峰只许在本档 Stagger 之内：WA 那档故意 Stagger=0，十字必须和火星同帧拍下去
                    if (p.Delay > stag + 1e-5f) overStagger++;
                    // 协程按 BurstSeconds 收尾，任何一颗的「起飞时刻 + 自身寿命」都不许越过它，否则会被半路冻住
                    if (p.Delay + p.Life > VerdictFx.BurstSeconds(kind) + 1e-4f) overWindow++;
                    // t≤0 必须正好在原点：第一帧就散开的话，「从徽标喷出来」这件事根本读不出来
                    var z = VerdictFx.PositionAt(p, 0f);
                    if (z.x != 0f || z.y != 0f) startOffAnchor++;
                    float end = p.Delay + p.Life;
                    for (int s = 0; s <= 24; s++)
                    {
                        float t = end * s / 24f;
                        var v = VerdictFx.PositionAt(p, t);
                        if (v.magnitude > VerdictFx.DriftCapPx) outside++;
                        if (v.x != Mathf.Round(v.x) || v.y != Mathf.Round(v.y)) fractional++;
                    }
                }
            }
            Expect(ref total, ref passed, "飘出 200px 容限的采样点", outside, 0);
            Expect(ref total, ref passed, "非整像素的采样点（Point 过滤下会糊出毛边）", fractional, 0);
            Expect(ref total, ref passed, "尺寸小于 1px 的粒子", nonPositiveSize, 0);
            Expect(ref total, ref passed, "t=0 不在锚点上的粒子", startOffAnchor, 0);
            Expect(ref total, ref passed, "错峰超出本档 Stagger 的粒子", overStagger, 0);
            Expect(ref total, ref passed, "演到一半被协程收尾冻住的粒子", overWindow, 0);
        }

        // Param(kind) 是私有的，这里按公开常量把每档的错峰上限摊开写死，改一档就会在这里撞红
        static float KindStagger(VerdictKind kind)
        {
            switch (kind)
            {
                case VerdictKind.Ac: return VerdictFx.AcStagger;
                case VerdictKind.LevelClear: return VerdictFx.ClearStagger;
                case VerdictKind.Wa: return 0f;
                default: return VerdictFx.SampleStagger;
            }
        }

        // ---------------- 淡入淡出：起手与收尾都必须归零，截图才不留半透明的纸片 ----------------

        static void FadeEndsClean(ref int total, ref int passed)
        {
            int nonzero = 0, unquantized = 0, overOne = 0;
            float[] durs = { VerdictFx.BurstSeconds(VerdictKind.Ac), VerdictFx.BurstSeconds(VerdictKind.Wa),
                             VerdictFx.AcLifeMin, VerdictFx.TintDur };
            foreach (float dur in durs)
            {
                for (int s = 0; s <= 40; s++)
                {
                    float t = dur * s / 40f;
                    float a = VerdictFx.AlphaAt(t, dur);
                    if (s == 0 && a != 0f) nonzero++;                       // t=0 必须全灭
                    if (a > 1f || a < 0f) overOne++;
                    float q = a * VerdictFx.AlphaQuant;
                    if (Mathf.Abs(q - Mathf.Round(q)) > 1e-4f) unquantized++;
                }
                if (VerdictFx.AlphaAt(dur, dur) != 0f) nonzero++;            // 演完必须归零
                if (VerdictFx.AlphaAt(dur + .3f, dur) != 0f) nonzero++;
            }
            Expect(ref total, ref passed, "起止不归零的采样点", nonzero, 0);
            Expect(ref total, ref passed, "透明度没量化到 1/4 档的采样点", unquantized, 0);
            Expect(ref total, ref passed, "透明度越界的采样点", overOne, 0);
            Expect(ref total, ref passed, "AliveCount 在 t≥life 归 0",
                VerdictFx.AliveCount(VerdictFx.BurstSeconds(VerdictKind.Ac), VerdictFx.BurstSeconds(VerdictKind.Ac)), 0);
            Expect(ref total, ref passed, "AliveCount 起飞中段严格递增",
                VerdictFx.AliveCount(.01f, 1f) < VerdictFx.AliveCount(.10f, 1f) ? 1 : 0, 1);
            Expect(ref total, ref passed, "AliveCount 起飞段末尾拉满",
                VerdictFx.AliveCount(1f * VerdictFx.BirthSpanRatio, 1f), VerdictFx.MaxParticles);
            int tintBad = 0;
            for (int s = 0; s <= 20; s++)
            {
                float t = VerdictFx.TintDur * s / 20f;
                float a = VerdictFx.TintAlphaAt(t, VerdictFx.TintDur);
                if (a < 0f || a > VerdictFx.TintPeakAlpha + 1e-4f) tintBad++;
                if ((s == 0 || s == 20) && a != 0f) tintBad++;
            }
            Expect(ref total, ref passed, "提亮层包络越界或首尾不归零的采样点", tintBad, 0);
        }

        // ---------------- 徽标本体：弹入必须精确回到 1，抖动必须精确回到原位 ----------------

        static void AnchorMotion(ref int total, ref int passed)
        {
            Expect(ref total, ref passed, "PopScaleAt(t≥dur) 精确回 1",
                VerdictFx.PopScaleAt(VerdictFx.PopDur, VerdictFx.PopDur) == 1f ? 1 : 0, 1);
            Expect(ref total, ref passed, "PopScaleAt(t≥dur) 晚一帧也还是 1",
                VerdictFx.PopScaleAt(VerdictFx.PopDur + .5f, VerdictFx.PopDur) == 1f ? 1 : 0, 1);
            Expect(ref total, ref passed, "PopScaleAt(t≤0) 从最小档起",
                VerdictFx.PopScaleAt(0f, VerdictFx.PopDur) == VerdictFx.PopScaleMin ? 1 : 0, 1);
            int shakeBad = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                if (VerdictFx.ShakeOffsetAt(0f, seed) != Vector2.zero) shakeBad++;
                if (VerdictFx.ShakeOffsetAt(VerdictFx.ShakeDur, seed) != Vector2.zero) shakeBad++;
                for (int s = 1; s < 8; s++)
                {
                    var v = VerdictFx.ShakeOffsetAt(VerdictFx.ShakeDur * s / 8f, seed);
                    // 3px 顶格：再大就是把徽标抖出卡片，读不出「AC」两个字
                    if (Mathf.Abs(v.x) > VerdictFx.ShakeAmpPx || Mathf.Abs(v.y) > VerdictFx.ShakeAmpPx) shakeBad++;
                    if (v.x != Mathf.Round(v.x) || v.y != Mathf.Round(v.y)) shakeBad++;
                }
            }
            Expect(ref total, ref passed, "抖动不越 3px / 不归零 / 非整像素的采样点", shakeBad, 0);
            int rotBad = 0;
            for (int i = 0; i < 200; i++)
            {
                var p = VerdictFx.SeedOf(i, i * 7919, VerdictKind.Ac);
                for (int s = 0; s <= 6; s++)
                {
                    float deg = Mathf.Abs(VerdictFx.RotationAt(p, p.Life * s / 6f));
                    float step = VerdictFx.RotationStepDeg;
                    if (Mathf.Abs(deg / step - Mathf.Round(deg / step)) > 1e-3f) rotBad++;
                }
            }
            Expect(ref total, ref passed, "角度没量化到 15° 一档的采样点", rotBad, 0);
        }

        // ---------------- WA 的头两颗固定当红十字的两条臂 ----------------

        static void WaCross(ref int total, ref int passed)
        {
            var h = VerdictFx.SeedOf(0, 12345, VerdictKind.Wa);
            var v = VerdictFx.SeedOf(1, 12345, VerdictKind.Wa);
            Expect(ref total, ref passed, "WA 第 0 颗 = 横臂", h.Shape, VerdictFx.ShapeCrossH);
            Expect(ref total, ref passed, "WA 第 1 颗 = 竖臂", v.Shape, VerdictFx.ShapeCrossV);
            Expect(ref total, ref passed, "十字臂不飘（速度 0）",
                h.Velocity == Vector2.zero && v.Velocity == Vector2.zero ? 1 : 0, 1);
            Expect(ref total, ref passed, "十字臂无重力", h.Gravity == 0f ? 1 : 0, 1);
            Expect(ref total, ref passed, "十字臂颜色用描边色 Ink（红叉拍红底等于没画）",
                h.Color.Equals(VerdictFx.CrossColor) ? 1 : 0, 1);
            var spark = VerdictFx.SeedOf(2, 12345, VerdictKind.Wa);
            Expect(ref total, ref passed, "WA 第 2 颗起是火星", spark.Shape, VerdictFx.ShapeSpark);
            float from = VerdictFx.CrossArmAt(0f, VerdictFx.CrossDur);
            float to = VerdictFx.CrossArmAt(VerdictFx.CrossDur * .99f, VerdictFx.CrossDur);
            Expect(ref total, ref passed, "十字由半长往外张", to > from ? 1 : 0, 1);
            Expect(ref total, ref passed, "臂长终态不超过 1.25 倍",
                to <= VerdictFx.CrossArmLen * VerdictFx.CrossGrowTo + 1f ? 1 : 0, 1);
            Expect(ref total, ref passed, "WA 火星落在徽标外沿（速度下限 > 臂长/寿命）",
                VerdictFx.WaSpeedMin * VerdictFx.WaLife > VerdictFx.CrossArmLen ? 1 : 0, 1);
        }

        // ---------------- 通关礼花的锚点只准待在容器上半区，且不许贴边被切 ----------------

        static void CelebrationAnchors(ref int total, ref int passed)
        {
            var size = new Vector2(1440f, 900f);
            int bad = 0;
            for (int seed = 1; seed < 40; seed++)
                for (int i = 0; i < VerdictFx.CelebrationFireworkAnchors; i++)
                {
                    var p = VerdictFx.CelebrationAnchorAt(i, VerdictFx.CelebrationFireworkAnchors, seed * 31, size);
                    float top = size.y * .5f;
                    float lowestAllowed = top - size.y * VerdictFx.CelebrationTopRatio;
                    if (p.y < lowestAllowed - 1f || p.y > top) bad++;
                    if (Mathf.Abs(p.x) > size.x * .5f - VerdictFx.SpawnPadPx) bad++;
                }
            Expect(ref total, ref passed, "礼花锚点跑出上半区或贴出容限的采样点", bad, 0);
            // count=1 走的是 u=.5 的退化分支：只有一朵礼花时它必须站中间，不能贴到左边当半边屏幕的空洞
            var solo = VerdictFx.CelebrationAnchorAt(0, 1, 4242, size);
            Expect(ref total, ref passed, "单朵礼花横向居中（|x| ≤ 抖动量）",
                Mathf.Abs(solo.x) <= VerdictFx.AnchorJitterPx ? 1 : 0, 1);
            Expect(ref total, ref passed, "礼花朵数与实现约定一致",
                solo.y > 0f && VerdictFx.CelebrationFireworkAnchors == 3 ? 1 : 0, 1);
        }

        // ---------------- 接线扫描：特效类再完整，App 不调用就等于没做 ----------------

        static void WiredIntoApp(ref int total, ref int passed)
        {
            string path = Path.Combine(Application.dataPath, "Scripts/UI/App.cs");
            Expect(ref total, ref passed, "App.cs 读得到", File.Exists(path) ? 1 : 0, 1);
            if (!File.Exists(path)) return;
            string src = File.ReadAllText(path);
            Expect(ref total, ref passed, "App 建了特效层（VerdictFx.Attach）", src.Contains("VerdictFx.Attach(") ? 1 : 0, 1);
            Expect(ref total, ref passed, "App 在结论处放特效（VerdictFx.BurstAt）", src.Contains("VerdictFx.BurstAt(") ? 1 : 0, 1);
            Expect(ref total, ref passed, "App 通关放礼花（VerdictFx.Celebration）", src.Contains("VerdictFx.Celebration(") ? 1 : 0, 1);
            Expect(ref total, ref passed, "App 换关收特效（VerdictFx.StopAll）", src.Contains("VerdictFx.StopAll(") ? 1 : 0, 1);
            Expect(ref total, ref passed, "截图模式跳过补间（VerdictFx.Instant）", src.Contains("VerdictFx.Instant") ? 1 : 0, 1);
            // 徽标必须把 RectTransform 交回去，否则特效只能往屏幕中心喷
            Expect(ref total, ref passed, "SetNodeBadge 返回徽标本体",
                src.Contains("RectTransform SetNodeBadge(") ? 1 : 0, 1);
            Expect(ref total, ref passed, "AC/WA 两种结论各放一档特效",
                src.Contains("VerdictKind.Ac") && src.Contains("VerdictKind.Wa") ? 1 : 0, 1);
            // 没 Attach 之前 BurstAt 只提示一次、不抛异常：整局照常能玩
            Expect(ref total, ref passed, "没建层时 BurstAt 不炸", Throws(() =>
            {
                VerdictFx.Instance = null;
                VerdictFx.BurstAt(null, VerdictKind.Ac);
                VerdictFx.Celebration("L2");
                VerdictFx.StopAll();
            }) ? 1 : 0, 0);
        }

        static bool Throws(System.Action act)
        {
            try { act(); } catch (System.Exception e) { Debug.Log("✘ " + Tag + " —— 抛异常：" + e.Message); return true; }
            return false;
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
