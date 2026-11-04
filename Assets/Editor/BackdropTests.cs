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
    // 第十一至十七轮的纵视差/天空响应契约原样保留。第十八轮把程序化剪影条换成搜集的五层真像素水下素材
    //（Resources/Backdrop/uw_*，台账见 ArtInbox/SOURCES.md），新增三组契约：
    // ChapterTint——四键共用素材后章节差异全靠 TintFor 乘色，分量不许越界、四键不许同色；
    // MirrorTiling——素材横向不无缝，瓦片奇偶镜像平铺，占槽必须连续、镜像必须交替、周期必须是两瓦；
    // CoverScale——水体层顶锚铺满视口的最小放大倍数，两维都盖得住且不许白放大。
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
            ChapterTint(ref total, ref passed);
            MirrorTiling(ref total, ref passed);
            CoverScale(ref total, ref passed);
            HintPlateReadable(ref total, ref passed);
        }

        // ---------------- 提示行有底板，字不压在花纹上（第十八轮⑤） ----------------
        //
        // 背景层一路铺到画布底边，换成真像素珊瑚后，提示行里「双击进表达式」「拖空白框选」被亮粉花纹
        // 啃掉一半（取证 Evidence/r18/shots/crop_r18p3_hint_strip.png，同一条带亮度实测 p5=83 / p95=253，
        // 跨度 170 级）。修法是字底下垫一条半透明鲸青底板。这里钉住：底板确实比文字框大一圈、
        // 文字上下都不出底板、屏宽不够时底板被钳在屏内。两条负对照不可省——
        // 「底板和文字框一样大」（= 没垫板的老写法）与「alpha 为 0」都必须判不过，否则前几条是自证空话。
        static void HintPlateReadable(ref int total, ref int passed)
        {
            var sz = PixelText.MeasureString(App.CanvasHintText, 2);
            float plateW = App.HintPlateW(sz.x, 1440f), plateH = App.HintPlateH(sz.y);
            ExpectInt(ref total, ref passed, "提示文字量得出宽度", sz.x > 100f ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "底板横向比文字宽一档（" + sz.x + " → " + plateW + "）",
                App.HintPlateFits(sz.x, sz.y, plateW, plateH) ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "底板左右各留 ≥" + App.HintPadMinX + "px",
                plateW - sz.x >= App.HintPadMinX ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "文字顶不出底板（底板顶 " +
                (App.HintPlateBottom + plateH) + " ≥ 文字顶 " + (App.HintTextBottom + sz.y) + "）",
                App.HintPlateBottom + plateH >= App.HintTextBottom + sz.y ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "底板底边不高于文字底边",
                App.HintPlateBottom <= App.HintTextBottom ? 1 : 0, 1);
            // 窄屏钳位：底板不许出屏，且仍是个正数
            float narrow = App.HintPlateW(sz.x, 620f);
            ExpectInt(ref total, ref passed, "屏宽 620 时底板被钳在屏内（" + narrow + "）",
                narrow > 0f && narrow <= 620f - 2f * App.HintEdge ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "屏宽为零时退化成一（不报错、不出负数）",
                App.HintPlateW(sz.x, 0f) == 0f && App.HintPlateH(0f) == 0f ? 1 : 0, 1);
            // 负对照①：底板与文字框同尺寸（没垫板的老写法）必须判压字
            ExpectInt(ref total, ref passed, "底板与文字同尺寸判为压字（负对照）",
                App.HintPlateFits(sz.x, sz.y, sz.x, sz.y) ? 1 : 0, 0);
            // 负对照②：只加宽不加高，纵向照样压字
            ExpectInt(ref total, ref passed, "只加宽不加高判为压字（负对照）",
                App.HintPlateFits(sz.x, sz.y, plateW, sz.y) ? 1 : 0, 0);
            // 底板要真把花纹压住：不透明度低于下限就等于没垫
            ExpectInt(ref total, ref passed, "底板压得住花纹（alpha " + App.HintPlateAlpha + " ≥ " +
                App.HintPlateAlphaMin + "）", App.HintPlateSuppresses(App.HintPlateAlpha) ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "alpha 为零判为没垫板（负对照）",
                App.HintPlateSuppresses(0f) ? 1 : 0, 0);
            // 沙色底板压在亮粉珊瑚上，和鲸青深字仍要拉开一档亮度；背景那档直接取纯白（比实测珊瑚更亮）
            Color salt = Theme.A(Theme.Salt, 1f), teal = Theme.A(Theme.WhaleDeep, 1f), bgWorst = Color.white;
            ExpectInt(ref total, ref passed, "沙色底板与深字亮度差够（背景取最亮那档）",
                App.HintPlateContrastOk(salt, App.HintPlateAlpha, teal, bgWorst) ? 1 : 0, 1);
            // 负对照③：底板刷成和字同一支鲸青（第一版就是这么写的），字直接糊在底上
            ExpectInt(ref total, ref passed, "底板与字同色判为糊字（负对照）",
                App.HintPlateContrastOk(teal, App.HintPlateAlpha, teal, bgWorst) ? 1 : 0, 0);
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

        // ---------------- r18：四键共用一套水下素材，章节气氛靠 TintFor 乘色区分 ----------------
        // 乘色是把双刃剑：分量越界会把素材乘成负色（黑块）或过曝（白块），四键同色等于章节差异消失。

        static void ChapterTint(ref int total, ref int passed)
        {
            string[] keys = { "bg_dock", "bg_factory", "bg_shelf", "bg_deep" };
            int outOfRange = 0;
            foreach (var k in keys)
            {
                Color c = Backdrop.TintFor(k);
                if (c.r <= 0f || c.g <= 0f || c.b <= 0f || c.r > 1f || c.g > 1f || c.b > 1f || c.a != 1f)
                    outOfRange++;
            }
            ExpectInt(ref total, ref passed, "四键乘色分量都落在 (0,1] 且不透明", outOfRange, 0);

            int same = 0;
            for (int i = 0; i < keys.Length; i++)
                for (int j = i + 1; j < keys.Length; j++)
                {
                    Color a = Backdrop.TintFor(keys[i]), b = Backdrop.TintFor(keys[j]);
                    if (Mathf.Abs(a.r - b.r) < 1e-4f && Mathf.Abs(a.g - b.g) < 1e-4f && Mathf.Abs(a.b - b.b) < 1e-4f)
                        same++;
                }
            ExpectInt(ref total, ref passed, "四键乘色互不相同（章节气氛不塌成一张图）", same, 0);

            ExpectInt(ref total, ref passed, "码头键回原色（白）",
                      Backdrop.TintFor("bg_dock") == Color.white ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "未知键回落原色（白）",
                      Backdrop.TintFor("bg_nonsense") == Color.white ? 1 : 0, 1);
            ExpectInt(ref total, ref passed, "null 键回落原色（白）",
                      Backdrop.TintFor(null) == Color.white ? 1 : 0, 1);
        }

        // ---------------- r18：非无缝素材靠镜像瓦片平铺，占槽必须连续、奇偶必须交替 ----------------
        // 瓦片 pivot 在左上：正放占 [p, p+tw]，镜像（localScale.x=-1 绕 pivot 翻）占 [p-tw, p]。
        // MirrorTileX 给奇数瓦多让一个 tw，两条才盖住同一个槽；错一格的表现为整列素材叠印或露缝。

        static void MirrorTiling(ref int total, ref int passed)
        {
            const float x0 = -37f, tw = 100f;
            int gap = 0, parity = 0;
            for (int j = 0; j < 12; j++)
            {
                float left = Backdrop.MirrorTileX(x0, tw, j) - (Backdrop.MirrorFlip(j) ? tw : 0f);
                // 第 j 瓦必须正好盖住第 j 个槽 [x0+j*tw, x0+(j+1)*tw]
                if (Mathf.Abs(left - (x0 + j * tw)) > 1e-3f) gap++;
                // 镜像必须奇偶交替：连续两瓦同向的话，非无缝素材的接缝就露出来了
                if (Backdrop.MirrorFlip(j) == Backdrop.MirrorFlip(j + 1)) parity++;
            }
            ExpectInt(ref total, ref passed, "每块瓦片正好盖住自己的槽（无叠印无露缝）", gap, 0);
            ExpectInt(ref total, ref passed, "镜像按奇偶交替", parity, 0);
            ExpectInt(ref total, ref passed, "第 0 瓦不镜像", Backdrop.MirrorFlip(0) ? 1 : 0, 0);
            // 镜像周期 = 2 瓦：相位环绕取 2*tw，跨周期时图案必须接得上
            ExpectInt(ref total, ref passed, "平铺以两瓦为周期",
                      (Mathf.Abs(Backdrop.MirrorTileX(x0 + 2f * tw, tw, 0) -
                                (Backdrop.MirrorTileX(x0, tw, 0) + 2f * tw)) < 1e-3f) ? 1 : 0, 1);
        }

        // ---------------- r18：水体层顶锚时必须盖满视口，CoverScaleFor 是唯一的放大倍数来源 ----------------

        static void CoverScale(ref int total, ref int passed)
        {
            int short_ = 0, over = 0;
            float[][] cases = {
                new[] { 934f, 752f, 576f, 324f },   // 实机画布宿主 x 素材原尺寸
                new[] { 934f, 766f, 576f, 324f },   // 加水体覆盖余量
                new[] { 1440f, 900f, 576f, 324f },  // 整窗
                new[] { 300f, 200f, 576f, 324f },   // 视口比素材还小
            };
            foreach (var c in cases)
            {
                float s = Backdrop.CoverScaleFor(c[0], c[1], c[2], c[3]);
                if (s * c[2] < c[0] - 1e-3f || s * c[3] < c[1] - 1e-3f) short_++;
                // 「最小倍数」：任一维缩小一点就盖不住
                float s2 = s * .999f;
                if (s2 * c[2] >= c[0] && s2 * c[3] >= c[1]) over++;
            }
            ExpectInt(ref total, ref passed, "放大后两维都盖得住视口", short_, 0);
            ExpectInt(ref total, ref passed, "倍数是最小的（不白放大）", over, 0);
            ExpectInt(ref total, ref passed, "正方形素材盖正方形视口倍数为一",
                      Mathf.Abs(Backdrop.CoverScaleFor(100f, 100f, 100f, 100f) - 1f) < 1e-4f ? 1 : 0, 1);
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
