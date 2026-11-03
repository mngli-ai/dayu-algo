using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 判题结论（AC / WA / 通关）→ 视觉与听觉反馈的一整层。
// 为什么单开一个文件：判定链在 App.SubmitRoutine / PlayCompareAnimation 里，那里只想要一行调用；
// 几十个轨迹常量、像素量化档位和彩带公式再往 4800 行的 App.cs 里塞就没法读了。
//
// 铁律（和 UiKit / Backdrop 同口径）：
//   1. 粒子的一切都由下面这组 **静态纯函数** 算出来（SeedFor / SeedOf / PositionAt / AlphaAt / AliveCount / BurstCount），
//      纯函数里禁止 UnityEngine.Random —— 主 agent 的单测和 Evidence/r17/fx_selfcheck.py 要能逐位复现同一帧。
//   2. 协程只负责把 t 喂给纯函数、把返回值落到 Image 上，自己不做任何数学。
//   3. 截图模式（Instant）不许留中间态：置真的那一刻 StopAll()，粒子/抖动/弹入一起归位。
//   4. 零资产依赖：一颗粒子就是运行时 1x1 白 Sprite（GraphView.White）拉出来的一块 Image；
//      整层 CanvasGroup 不挡射线、每张 Image 的 raycastTarget=false，绝不抢卡片/端口的点击。
//   5. 颜色只从 Theme 的 16 色板取；旋转与透明度量化到台阶上，像素风不许连续渐变。

/// <summary>判题结论的种类。按 SeverityRank 从轻到重：单条样例 → 徽标 AC/WA → 整关通过。</summary>
public enum VerdictKind { Ac, Wa, LevelClear, SamplePass, SampleFail }

/// <summary>一颗粒子的初始参数（SeedOf 的产物，只读）。坐标＝容器局部坐标，+y 向上，原点在容器中心。</summary>
public struct VerdictParticle
{
    public Vector2 Velocity;   // px/s 起飞初速
    public float Drag;         // 1/s 空气阻尼：纸片先窜开再飘，没有它 t=0.15 时还挤成一坨
    public Vector2 Size;       // px 宽高（整像素）
    public float Gravity;      // px/s² 向下为正
    public float WobbleAmp;    // px 横向摆幅（彩带的蛇行）
    public float WobbleHz;     // 摆动频率
    public float RotDeg;       // 初始角（已量化到 RotationStepDeg）
    public float SpinDeg;      // 自转角速度 deg/s
    public float Delay;        // 起飞错峰 s
    public float Life;         // 自身飞行时长 s（不含 Delay）
    public Color32 Color;
    public int Shape;          // 见 ShapeFlake 等常量
}

public sealed class VerdictFx : MonoBehaviour
{
    // ---------------- 对外常量：契约里逐条引用，改数只改这里 ----------------

    public const string LayerName = "VerdictFx";

    // 池：同屏最多同时在飞的粒子数。通关是最大一档（48 雨 + 3×16 礼花 = 96），留 32 颗余量。
    public const int MaxParticles = 128;
    public const int MaxBursts = 6;                   // 同存爆发的条数（通关 4 条 + 连点余量）

    // BurstCount 查的就是这张表
    public const int AcCount = 40;
    public const int WaCount = 8;                     // 含十字的两条臂，见 SeedOf 的 ShapeCrossH/V
    public const int LevelClearCount = 96;
    public const int SamplePassCount = 3;
    public const int SampleFailCount = 2;

    // 通关的分解：BurstCount(LevelClear) == Rain + Anchors × PerAnchor
    public const int CelebrationRainCount = 48;
    public const int CelebrationFireworkAnchors = 3;
    public const int CelebrationFireworkCount = 16;

    // 形状
    public const int ShapeFlake = 0, ShapeStreamer = 1, ShapeSpark = 2, ShapeCrossH = 3, ShapeCrossV = 4;

    // 时长：Life 上限 + 错峰上限 = 整段时长（BurstSeconds）
    public const float AcLifeMin = .52f, AcLifeMax = .80f;
    public const float WaLife = .30f;
    public const float ClearLifeMin = .50f, ClearLifeMax = .72f;
    public const float SampleLife = .22f;
    public const float AcStagger = .16f;              // 错峰起飞：彩带要「喷出来」而不是「同时闪一下」
    public const float ClearStagger = .22f;
    public const float SampleStagger = .03f;

    // 轨迹包络：|PositionAt| ≤ DriftCapPx 这条断言靠它们撑着（渲染端还有 CullMarginPx 兜底）。
    // 位移带空气阻尼 x = v0/k·(1-e^-kt)：初速可以给得很猛（0.15s 就摊成扇形），
    // 极限位移却只有 v0/k，纸片甩不出视口 —— 这才是纸屑的手感。
    public const float AcSpeedMin = 300f, AcSpeedMax = 640f;
    public const float ClearSpeedMin = 380f, ClearSpeedMax = 780f;
    public const float WaSpeedMin = 150f, WaSpeedMax = 260f;       // 火星要甩得出徽标（半宽 ~29px），否则同色白闪
    public const float SampleSpeedMin = 120f, SampleSpeedMax = 220f;
    public const float DragConfetti = 4.5f;           // 1/s：纸片/彩带的阻尼
    public const float DragClear = 5f;                // 礼花阻尼小一档，球才撑得开
    public const float DragSpark = 8f;                // 火星几乎不飘，一下就停
    public const float GravityConfetti = 420f;        // 彩带必须往下坠，不坠就不像纸屑
    public const float GravityClear = 120f;           // 礼花坠得慢，才留在上半区看得清
    public const float AcSpreadDeg = 55f;             // 只朝上喷：糊住对比板读数的特效是负反馈
    public const float FullSpreadDeg = 180f;          // 全向（礼花 / 火星）
    public const float WobbleAmpMax = 12f;
    public const float DriftCapPx = 200f;             // 纯函数口径：离锚点最远 200px
    public const float DriftClampRatio = .95f;        // 实际截停在 190px，给整像素取整留 10px
    public const float CullMarginPx = 200f;           // 渲染口径：飘出容器 200px 以外就不画

    // 像素量化
    public const int AlphaQuant = 4;                  // 透明度只走 0/.25/.5/.75/1
    public const int RotationStepDeg = 15;            // 连续角会把 1x1 白片在 Point 过滤下拉成糊边斜线
    public const float FadeInRatio = .12f;
    public const float FadeOutRatio = .38f;
    public const float BirthSpanRatio = .30f;         // AliveCount 的起飞段占整段时长的比例

    // 结构件（不飘，只闪/抖/弹）
    public const float TintDur = .12f;                // AC 的暖色提亮：短到「闪一下就没了」
    public const float TintPeakAlpha = .16f;
    public const float CrossDur = .20f;
    public const float CrossPeakAlpha = .9f;
    public const float CrossArmLen = 30f;             // 徽标只有 18px 高，臂长过 18 才拍得出「盖掉它」的十字
    public const float CrossArmThick = 3f;
    public const float CrossGrowFrom = .55f;          // 十字由半长张到 1.25 倍，闪的东西要往外拍才有力道
    public const float CrossGrowTo = 1.25f;
    public const float ShakeDur = .14f;
    public const int ShakeFrames = 4;                 // 再多就是「晃」而不是「抖一下」
    public const float ShakeAmpPx = 3f;               // 3px 顶格：徽标不许被抖出卡片
    public const float PopDur = .16f;
    public const float PopScaleMin = .6f;
    public const int PopSteps = 4;                    // 0.6→0.7→0.8→0.9→1，不做连续缩放

    // 尺寸
    public const int FlakeMinPx = 3, FlakeMaxPx = 6;
    public const int StreamerThickPx = 2, StreamerMinPx = 7, StreamerMaxPx = 14;
    public const int SparkPx = 2;
    public const float WobbleHzMin = 2.5f, WobbleHzSpan = 4.5f;
    public const float SpinMinDeg = 90f, SpinSpanDeg = 240f;

    // 落点
    public const float CelebrationTopRatio = .45f;    // 通关礼花的锚点只在容器上 45% 一带
    public const float SpawnPadPx = 24f;              // 离容器边至少 24px，免得第一帧就被切掉
    public const float AnchorJitterPx = 40f;          // 礼花横向均分后再抖这么多

    // 本轮新增的两条 cue（既有 17 条的名字/语义/去抖一个都没动）
    public const string AcCue = "confetti";
    public const string WaCue = "wa";

    /// <summary>true＝截图模式：任何补间都不许留中间态。置真的那一刻立刻清空整层。</summary>
    public static bool Instant
    {
        get { return instant; }
        set
        {
            instant = value;
            // 粒子没有「末帧」可言（末态就是全灭＋徽标归位），所以直接 StopAll 最干净
            if (value) StopAll();
        }
    }
    static bool instant;

    /// <summary>BurstAt 顺手播新 cue，主 agent 接一行就同时有画面和声音；想自己管声音就置 false。</summary>
    public static bool AutoSound = true;

    public static VerdictFx Instance;

    // ---------------- 纯函数族（自测只测这些；协程里没有一处数学） ----------------

    /// <summary>32 位混淆（splitmix 收尾那套魔数）。整族纯函数唯一的「随机源」，不碰 UnityEngine.Random。</summary>
    public static int Mix32(int x)
    {
        unchecked
        {
            uint v = (uint)x;
            v ^= v >> 16; v *= 0x7FEB352Du; v ^= v >> 15; v *= 0x846CA68Bu; v ^= v >> 16;
            return (int)v;
        }
    }

    /// <summary>哈希的高 24 位换成 [0,1)。只用 24 位是因为 float 尾数正好 24 位，免得断言测到舍入运气。</summary>
    public static float Unit01(int h)
    {
        unchecked { return (float)(((uint)h >> 8) & 0xFFFFFFu) / 16777216f; }
    }

    /// <summary>同上，映射到 [-1,1)。</summary>
    public static float SignedUnit(int h) { return Unit01(h) * 2f - 1f; }

    /// <summary>关卡 + 第几条样例 + 结论 → 稳定种子。同一关重跑两次彩带落在同一处，截图才敢逐像素比。</summary>
    public static int SeedFor(string levelId, int sampleIndex, bool accepted)
    {
        unchecked
        {
            uint h = 2166136261u;                     // FNV-1a：让 levelId 每个字符都参与，相邻关卡不至于长同一形
            if (!string.IsNullOrEmpty(levelId))
            {
                for (int i = 0; i < levelId.Length; i++)
                {
                    h ^= (uint)levelId[i];
                    h *= 16777619u;
                }
            }
            h ^= (uint)sampleIndex; h *= 16777619u;
            h ^= accepted ? 0x9E3779B9u : 0x85EBCA6Bu;
            h = (uint)Mix32((int)h);
            return h == 0u ? 0x5DEECE6D : (int)h;     // 全 0 种子会让头几颗挤在同一角度
        }
    }

    /// <summary>结论 → 严重度序（0 最轻）。单测按这个序断言 BurstCount 单调不减。</summary>
    public static int SeverityRank(VerdictKind kind)
    {
        switch (kind)
        {
            case VerdictKind.SampleFail: return 0;
            case VerdictKind.SamplePass: return 1;
            case VerdictKind.Wa: return 2;
            case VerdictKind.Ac: return 3;
            default: return 4;                         // LevelClear
        }
    }

    /// <summary>这一档喷几颗。</summary>
    public static int BurstCount(VerdictKind kind)
    {
        switch (kind)
        {
            case VerdictKind.Ac: return AcCount;
            case VerdictKind.Wa: return WaCount;
            case VerdictKind.LevelClear: return LevelClearCount;
            case VerdictKind.SamplePass: return SamplePassCount;
            default: return SampleFailCount;
        }
    }

    /// <summary>这一档活多久（寿命上限＋错峰上限）。协程按它收尾，粒子不会被半路冻住。</summary>
    public static float BurstSeconds(VerdictKind kind)
    {
        switch (kind)
        {
            case VerdictKind.Ac: return AcLifeMax + AcStagger;
            case VerdictKind.Wa: return WaLife;
            case VerdictKind.LevelClear: return ClearLifeMax + ClearStagger;
            default: return SampleLife + SampleStagger;
        }
    }

    /// <summary>结论 → 本轮新增的 cue。既有 pass/fail/ac/clear 仍由 App 播，这里返回 null 免得双响。</summary>
    public static string CueFor(VerdictKind kind)
    {
        if (kind == VerdictKind.Ac) return AcCue;
        if (kind == VerdictKind.Wa) return WaCue;
        return null;
    }

    struct KindParam
    {
        public float LifeMin, LifeMax, SpeedMin, SpeedMax, Gravity, Drag, SpreadDeg, Stagger, Wobble;
        public int FlakePercent;
        public bool Radial;
    }

    static KindParam Param(VerdictKind kind)
    {
        var k = new KindParam();
        switch (kind)
        {
            case VerdictKind.Ac:
                k.LifeMin = AcLifeMin; k.LifeMax = AcLifeMax;
                k.SpeedMin = AcSpeedMin; k.SpeedMax = AcSpeedMax;
                k.Gravity = GravityConfetti; k.Drag = DragConfetti; k.SpreadDeg = AcSpreadDeg;
                k.Stagger = AcStagger; k.Wobble = WobbleAmpMax; k.FlakePercent = 55;
                break;
            case VerdictKind.Wa:
                k.LifeMin = WaLife; k.LifeMax = WaLife;
                k.SpeedMin = WaSpeedMin; k.SpeedMax = WaSpeedMax;
                k.Drag = DragSpark;
                k.Stagger = 0f; k.FlakePercent = 100; k.Radial = true;
                break;
            case VerdictKind.LevelClear:
                k.LifeMin = ClearLifeMin; k.LifeMax = ClearLifeMax;
                k.SpeedMin = ClearSpeedMin; k.SpeedMax = ClearSpeedMax;
                k.Gravity = GravityClear; k.Drag = DragClear; k.SpreadDeg = FullSpreadDeg;
                k.Stagger = ClearStagger; k.Wobble = WobbleAmpMax; k.FlakePercent = 60; k.Radial = true;
                break;
            case VerdictKind.SamplePass:
                k.LifeMin = SampleLife; k.LifeMax = SampleLife;
                k.SpeedMin = SampleSpeedMin; k.SpeedMax = SampleSpeedMax;
                k.Drag = DragSpark;
                k.Stagger = SampleStagger; k.FlakePercent = 100; k.Radial = true;
                break;
            default:                                   // SampleFail
                k.LifeMin = SampleLife; k.LifeMax = SampleLife;
                k.SpeedMin = SampleSpeedMin; k.SpeedMax = SampleSpeedMax;
                k.Drag = DragSpark;
                k.Stagger = SampleStagger; k.FlakePercent = 100; k.Radial = true;
                break;
        }
        return k;
    }

    /// <summary>第 index 颗粒子的初始参数（Ac 档），等价于 SeedOf(index, seed, VerdictKind.Ac)。</summary>
    public static VerdictParticle SeedOf(int index, int seed) { return SeedOf(index, seed, VerdictKind.Ac); }

    /// <summary>
    /// 第 index 颗粒子的初始参数。所有字段只由 (index, seed, kind) 决定：
    /// 一路一路互不相同的哈希分别喂给角度/速度/寿命/错峰/摆幅/形状/颜色，改任何一档都不会连带改到别的档。
    /// </summary>
    public static VerdictParticle SeedOf(int index, int seed, VerdictKind kind)
    {
        var p = new VerdictParticle();
        KindParam k = Param(kind);
        int b0 = unchecked(seed ^ (index * unchecked((int)0x9E3779B1)));
        int h0 = Mix32(unchecked(b0 + 0x1234567));
        int h1 = Mix32(unchecked(h0 + unchecked((int)0x9E3779B9)));
        int h2 = Mix32(unchecked(h1 + unchecked((int)0x85EBCA6B)));
        int h3 = Mix32(unchecked(h2 + unchecked((int)0xC2B2AE35)));
        int h4 = Mix32(unchecked(h3 + 0x27D4EB2F));
        int h5 = Mix32(unchecked(h4 + unchecked((int)0x165667B1)));

        // WA 的头两颗固定当十字的两条臂（速度 0、不飘）：徽标上「啪」拍出一个红十字，比撒一把粒子更像判错
        bool crossArm = kind == VerdictKind.Wa && index < 2;

        // 全向档直接拿 ±180° 当角度（礼花要炸成一个圆）；Ac 的 ±55° 是「以正上为轴的扇形」
        float ang = k.Radial ? SignedUnit(h0) * Mathf.PI
                             : Mathf.PI * .5f + SignedUnit(h0) * k.SpreadDeg * Mathf.Deg2Rad;
        float spd = Mathf.Lerp(k.SpeedMin, k.SpeedMax, Unit01(h1));
        p.Velocity = crossArm ? Vector2.zero : new Vector2(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd);
        p.Gravity = crossArm ? 0f : k.Gravity;
        p.Drag = k.Drag;
        p.Life = crossArm ? CrossDur : Mathf.Lerp(k.LifeMin, k.LifeMax, Unit01(h2));
        p.Delay = k.Stagger > 0f ? Unit01(h3) * k.Stagger : 0f;
        p.WobbleAmp = crossArm ? 0f : Unit01(h4) * k.Wobble;
        p.WobbleHz = WobbleHzMin + Unit01(h5) * WobbleHzSpan;

        if (crossArm) p.Shape = index == 0 ? ShapeCrossH : ShapeCrossV;
        else if (kind == VerdictKind.SamplePass || kind == VerdictKind.SampleFail || kind == VerdictKind.Wa)
            p.Shape = ShapeSpark;                     // 火星：轻反馈只给两三点，不满屏
        else p.Shape = Unit01(h5) * 100f < k.FlakePercent ? ShapeFlake : ShapeStreamer;

        p.Size = SizeOf(p.Shape, h2, h4);
        p.RotDeg = QuantizeRotation(Unit01(h3) * 360f);
        p.SpinDeg = crossArm ? 0f : SignedUnit(h1) * (SpinMinDeg + Unit01(h0) * SpinSpanDeg);
        Color32[] pal = PaletteFor(kind, index);
        p.Color = crossArm ? CrossColor : pal[Mathf.Min(pal.Length - 1, (int)(Unit01(h5) * pal.Length))];
        return p;
    }

    static Vector2 SizeOf(int shape, int ha, int hb)
    {
        switch (shape)
        {
            case ShapeStreamer:
                // 长条只 2~3px 宽，再宽就成飘带色块，读不出「纸片」
                return new Vector2(StreamerThickPx + Mathf.Round(Unit01(ha) * 1f),
                                   StreamerMinPx + Mathf.Round(Unit01(hb) * (StreamerMaxPx - StreamerMinPx)));
            case ShapeSpark:
            case ShapeCrossH:
            case ShapeCrossV:
                return new Vector2(SparkPx, SparkPx);     // 十字臂长由 CrossArmAt 逐帧给，这里只是初值
            default:
                int s = FlakeMinPx + (int)Mathf.Round(Unit01(ha) * (FlakeMaxPx - FlakeMinPx));
                return new Vector2(s, s);
        }
    }

    /// <summary>暖色族（Honey/Yellow/Coral/HoneyDeep）。不放 White：画布底是 Foam、卡片是 Paper，
    /// 纯白小方片贴上去等于没画，特效白做。</summary>
    public static readonly Color32[] PaletteWarm = { Theme.Honey, Theme.Yellow, Theme.Coral, Theme.HoneyDeep };
    /// <summary>冷色族（WhaleLight/Mint/Lavender/Grape）。与暖族交替出现才读得出「双色礼花」；
    /// 末位用 Grape 而不是 MintLight，同样是给浅底留对比。</summary>
    public static readonly Color32[] PaletteCool = { Theme.WhaleLight, Theme.Mint, Theme.Lavender, Theme.Grape };
    /// <summary>判错的火星也用 Ink：WA/单条失败的徽标底就是 Coral，Coral 火星落在 Coral 徽标上
    /// 等于没画（预览图实测）。红色由徽标自己提供，特效只负责「拍下去的暗痕」。</summary>
    public static readonly Color32[] PaletteWrong = { Theme.Ink };
    /// <summary>WA 的十字用描边色 Ink：徽标底本身就是 Coral，红叉拍在红底上等于没画（预览图实测）。</summary>
    public static readonly Color32 CrossColor = Theme.Ink;
    /// <summary>单条通过给 HoneyDeep：徽标底是 Mint，Mint 火星同样同色看不见；暖橙既醒目又不刺眼。</summary>
    public static readonly Color32[] PaletteRight = { Theme.HoneyDeep };

    static Color32[] PaletteFor(VerdictKind kind, int index)
    {
        switch (kind)
        {
            case VerdictKind.LevelClear: return (index & 1) == 0 ? PaletteWarm : PaletteCool;
            case VerdictKind.Wa: return PaletteWrong;
            case VerdictKind.SampleFail: return PaletteWrong;
            case VerdictKind.SamplePass: return PaletteRight;
            default: return (index & 1) == 0 ? PaletteCool : PaletteWarm;
        }
    }

    static float QuantizeRotation(float deg)
    {
        return Mathf.Round(deg / RotationStepDeg) * RotationStepDeg;
    }

    /// <summary>
    /// t 时刻相对锚点的位移。t≤0 恒为原点（断言口径）；漂移截停在 DriftCapPx×DriftClampRatio 内。
    /// 带空气阻尼的解析解：x = v0/k·(1-e^-kt)，重力项 = g/k·(t-(1-e^-kt)/k)。
    /// k→0 时两者都退回匀变速（v0·t、½g·t²），所以这里对 k=0 做了退化分支。
    /// </summary>
    public static Vector2 PositionAt(VerdictParticle p, float t)
    {
        if (t <= 0f) return Vector2.zero;
        float x, y;
        if (p.Drag <= 0f)
        {
            x = p.Velocity.x * t;
            y = p.Velocity.y * t - .5f * p.Gravity * t * t;
        }
        else
        {
            float f = 1f - Mathf.Exp(-p.Drag * t);           // 阻尼位移因子，t=0 时为 0 ⇒ 起点就是锚点
            x = p.Velocity.x / p.Drag * f;
            y = p.Velocity.y / p.Drag * f - p.Gravity / p.Drag * (t - f / p.Drag);
        }
        x += p.WobbleAmp * Mathf.Sin(2f * Mathf.PI * p.WobbleHz * t);     // 摆动项在 t=0 也是 0
        var v = new Vector2(x, y);
        float cap = DriftCapPx * DriftClampRatio;
        float m = v.magnitude;
        if (m > cap) v *= cap / m;
        // 整像素：半个像素的位移在 Point 过滤下会抖出毛边
        return new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));
    }

    /// <summary>t 时刻的角度（量化到 RotationStepDeg）。</summary>
    public static float RotationAt(VerdictParticle p, float t)
    {
        if (t <= 0f) return p.RotDeg;
        return QuantizeRotation(p.RotDeg + p.SpinDeg * t);
    }

    /// <summary>
    /// 透明度。t≤0 与 t≥dur 恒为 0（演完必须归零，截图才不会有残留），中段量化到 1/AlphaQuant 档。
    /// </summary>
    public static float AlphaAt(float t, float dur)
    {
        if (dur <= 0f || t <= 0f || t >= dur) return 0f;
        float u = t / dur;
        float a = 1f;
        if (u < FadeInRatio) a = u / FadeInRatio;
        else if (u > 1f - FadeOutRatio) a = (1f - u) / FadeOutRatio;
        return Mathf.Clamp01(Mathf.Round(a * AlphaQuant) / AlphaQuant);
    }

    /// <summary>
    /// t 时刻「已经起飞」的颗数上限（满池口径）：前 BirthSpanRatio 段线性拉满，t≥life 一律算 0 颗（整段演完）。
    /// 渲染端用它当节流：只画前 min(BurstCount, AliveCount) 颗，其余 Image 关掉，省批次也让「喷出来」有先后。
    /// </summary>
    public static int AliveCount(float t, float life)
    {
        if (life <= 0f || t < 0f || t >= life) return 0;
        float span = life * BirthSpanRatio;
        if (t >= span) return MaxParticles;
        return Mathf.CeilToInt(MaxParticles * (t / span));
    }

    /// <summary>
    /// 暖色提亮层：起来快落得也快的三角包络，峰值 TintPeakAlpha。t≤0 / t≥dur 恒 0。
    /// 这里不量化到 AlphaQuant 档——提亮是整片覆盖而不是像素片，跳档会看出一闪一闪的台阶。
    /// </summary>
    public static float TintAlphaAt(float t, float dur)
    {
        if (dur <= 0f || t <= 0f || t >= dur) return 0f;
        float u = t / dur;
        const float attack = .3f;
        return (u < attack ? u / attack : (1f - u) / (1f - attack)) * TintPeakAlpha;
    }

    /// <summary>WA 十字的淡出：起手最亮、一路衰减（「闪」和「渐显」是两回事）。</summary>
    public static float FlashAlphaAt(float t, float dur)
    {
        if (dur <= 0f || t <= 0f || t >= dur) return 0f;
        return Mathf.Round((1f - t / dur) * CrossPeakAlpha * AlphaQuant) / AlphaQuant;
    }

    /// <summary>十字臂长：随时间由 CrossGrowFrom 张到 CrossGrowTo，闪的东西要往外拍才有力道。</summary>
    public static float CrossArmAt(float t, float dur)
    {
        if (dur <= 0f) return CrossArmLen * CrossGrowFrom;
        float u = Mathf.Clamp01(t <= 0f ? 0f : t / dur);
        return Mathf.Round(CrossArmLen * Mathf.Lerp(CrossGrowFrom, CrossGrowTo, u));
    }

    /// <summary>
    /// 徽标抖动偏移：ShakeFrames 个台阶、幅度 ≤ShakeAmpPx，第一帧归零（否则分不清是抖还是挪），
    /// t≥ShakeDur 恒 0。只叠在 anchoredPosition 上，不动 sizeDelta、不挂布局组，所以影响不到布局。
    /// </summary>
    public static Vector2 ShakeOffsetAt(float t, int seed)
    {
        if (t <= 0f || t >= ShakeDur) return Vector2.zero;
        int f = Mathf.Clamp(Mathf.FloorToInt(t / ShakeDur * ShakeFrames), 0, ShakeFrames - 1);
        if (f == 0) return Vector2.zero;
        float k = 1f - f / (float)ShakeFrames;                    // 后一帧比前一帧轻
        int hx = Mix32(unchecked(seed + f * unchecked((int)0x9E3779B1)));
        int hy = Mix32(unchecked(hx ^ 0x5BF03635));
        return new Vector2(Mathf.Round(SignedUnit(hx) * ShakeAmpPx * k),
                           Mathf.Round(SignedUnit(hy) * ShakeAmpPx * k));
    }

    /// <summary>徽标弹入：PopScaleMin→1 分 PopSteps 个像素台阶；t≥dur 必为 1，保证终态一字不差。</summary>
    public static float PopScaleAt(float t, float dur)
    {
        if (dur <= 0f || t >= dur) return 1f;
        if (t <= 0f) return PopScaleMin;
        int s = Mathf.Clamp(Mathf.FloorToInt(t / dur * PopSteps), 0, PopSteps - 1);
        return Mathf.Lerp(PopScaleMin, 1f, s / (float)PopSteps);
    }

    /// <summary>通关礼花的第 i 个锚点（容器局部坐标）：横向均分 ±AnchorJitterPx，纵向只落在上 CelebrationTopRatio 区。</summary>
    public static Vector2 CelebrationAnchorAt(int i, int count, int seed, Vector2 size)
    {
        float w = Mathf.Max(1f, size.x), h = Mathf.Max(1f, size.y);
        float u = count <= 1 ? .5f : (i + .5f) / count;
        float x = -w * .5f + u * w + SignedUnit(Mix32(unchecked(seed + i * 7919))) * AnchorJitterPx;
        float top = h * .5f - SpawnPadPx;                         // 顶边留回收余量，礼花不贴出屏
        float bottom = h * (.5f - CelebrationTopRatio);           // 上 45% 区的下沿：压住卡片的礼花不叫庆祝
        float y = top - Unit01(Mix32(unchecked(seed ^ (i * 104729 + 17)))) * (top - bottom);
        x = Mathf.Clamp(x, -w * .5f + SpawnPadPx, w * .5f - SpawnPadPx);
        y = Mathf.Max(y, -h * .5f + SpawnPadPx);                 // 别让抖动把礼花甩到下半天
        return new Vector2(Mathf.Round(x), Mathf.Round(y));
    }

    // ---------------- 对外一行 API ----------------

    /// <summary>铺满 parent 建特效层（幂等，重复调用复用现成的）。接在 App 的 OverlayLayer 上，天然绘于所有卡片之上。</summary>
    public static VerdictFx Attach(RectTransform parent)
    {
        if (parent == null) return null;
        var old = parent.Find(LayerName);
        if (old != null)
        {
            var reused = old.GetComponent<VerdictFx>();
            if (reused != null) { Instance = reused; return reused; }
        }
        var go = new GameObject(LayerName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        // 纯函数按「容器中心＝局部原点」算落点，所以 pivot 必须是 0.5
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(.5f, .5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var cg = go.AddComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false;            // 整层不吃鼠标也不吃滚轮
        var fx = go.AddComponent<VerdictFx>();
        fx.container = rt;
        Instance = fx;
        return fx;
    }

    /// <summary>这一层建好了没（App 换关时可以先判一下再调 Celebration）。</summary>
    public static bool Attached { get { return Instance != null; } }

    /// <summary>在 anchor 的位置来一次反馈；anchor 为 null 就落在容器中心。种子走默认值，重跑会重演同一套。</summary>
    public static void BurstAt(RectTransform anchor, VerdictKind kind)
    {
        BurstAt(anchor, kind, SeedFor(null, SeverityRank(kind), true));
    }

    /// <summary>同上，指定种子：把 SeedFor(level.Id, 第几条样例, ok) 传进来，同一关的截图才可逐像素对拍。</summary>
    public static void BurstAt(RectTransform anchor, VerdictKind kind, int seed)
    {
        var fx = Instance;
        if (fx == null) { WarnOnce(); return; }
        if (instant) return;                  // 截图模式一帧都不播：不留中间态
        if (fx.Begin(anchor, kind, fx.LocalOf(anchor), BurstCount(kind), seed) != null) fx.PlayCue(kind);
    }

    /// <summary>通关：一条彩带雨 + 三朵双色礼花，锚点都在容器上半区。levelId 只用来决定种子。</summary>
    public static void Celebration(string levelId)
    {
        var fx = Instance;
        if (fx == null) { WarnOnce(); return; }
        if (instant) return;
        int seed = SeedFor(levelId, -1, true);
        Vector2 size = fx.container != null ? fx.container.rect.size : new Vector2(934f, 684f);

        // 彩带雨贴顶边往下落，礼花在上半区炸开：都收在上半，才不会压住底下的对比板读数
        fx.Begin(null, VerdictKind.LevelClear, new Vector2(0f, size.y * .5f - SpawnPadPx),
                 CelebrationRainCount, unchecked(seed + 11));
        for (int i = 0; i < CelebrationFireworkAnchors; i++)
            fx.Begin(null, VerdictKind.LevelClear,
                     CelebrationAnchorAt(i, CelebrationFireworkAnchors, seed, size),
                     CelebrationFireworkCount, unchecked(seed + (i + 1) * 31337));
        fx.PlayCue(VerdictKind.LevelClear);   // 现在返回 null：ac/clear 仍归 App 播，别双响
    }

    /// <summary>掐掉所有在飞的反馈并还原徽标的位移/缩放。换关、重来、退菜单、截图模式都该调。</summary>
    public static void StopAll()
    {
        var fx = Instance;
        if (fx != null) fx.StopEverything();
    }

    // ---------------- 渲染：只落值，不做数学 ----------------

    sealed class Burst
    {
        public VerdictKind Kind;
        public int Seed;
        public Vector2 Origin;
        public VerdictParticle[] Ps;
        public int[] Slots;
        public float Elapsed;
        public float End;
        public RectTransform Anchor;
        public Vector2 AnchorPos0;
        public Vector3 AnchorScale0;
        public bool AnchorOwned;
        public bool Dead;
    }

    RectTransform container;
    readonly List<Burst> bursts = new List<Burst>();
    readonly List<Image> pool = new List<Image>();
    readonly List<int> free = new List<int>();
    Image tint;
    Coroutine routine;
    static bool warned;

    static void WarnOnce()
    {
        if (warned) return;
        warned = true;
        Debug.Log("[fx] 没先调 VerdictFx.Attach(OverlayLayer)，本局不放判题特效（发声不受影响）");
    }

    // 锚点世界中心 → 容器局部坐标；容器 pivot=0.5，所以结果直接就是「中心为原点」
    Vector2 LocalOf(RectTransform anchor)
    {
        if (container == null || anchor == null) return Vector2.zero;
        Vector2 world = anchor.TransformPoint(anchor.rect.center);
        return container.InverseTransformPoint(world);
    }

    Burst Begin(RectTransform anchor, VerdictKind kind, Vector2 origin, int count, int seed)
    {
        if (container == null || count <= 0) return null;
        if (bursts.Count >= MaxBursts) Finish(bursts[0], 0);
        var b = new Burst
        {
            Kind = kind,
            Seed = seed,
            Origin = origin,
            End = BurstSeconds(kind),
            Ps = new VerdictParticle[count],
            Slots = new int[count],
        };
        for (int i = 0; i < count; i++)
        {
            b.Ps[i] = SeedOf(i, seed, kind);
            b.Slots[i] = AcquireSlot();
        }
        b.Anchor = anchor;
        if (anchor != null)
        {
            b.AnchorPos0 = anchor.anchoredPosition;
            b.AnchorScale0 = anchor.localScale;
            b.AnchorOwned = true;
        }
        if (NeedsTint(kind)) EnsureTint();
        bursts.Add(b);
        EnsureRoutine();
        // 每条特效落一行日志：真实鼠标验收要等这行确认「运行时真的喷了」，
        // 而不是只在大脑里推断「App 调了 BurstAt，所以应该喷了」。
        Debug.Log("[fx] " + kind + " 喷 " + count + " 颗 种子=" + seed + " 锚=" + (anchor == null ? "屏幕中心" : anchor.name));
        return b;
    }

    static bool NeedsTint(VerdictKind kind) { return kind == VerdictKind.Ac || kind == VerdictKind.LevelClear; }

    int AcquireSlot()
    {
        if (free.Count > 0)
        {
            int s = free[free.Count - 1];
            free.RemoveAt(free.Count - 1);
            return s;
        }
        if (pool.Count >= MaxParticles) return -1;      // 池满就丢这颗粒子：宁可稀一点，不许无限膨胀
        var go = new GameObject("Fx" + pool.Count, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(container, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
        rt.pivot = new Vector2(.5f, .5f);
        var img = go.GetComponent<Image>();
        img.sprite = GraphView.White;                   // 运行时 1x1 白片，零资产
        img.type = Image.Type.Simple;
        img.raycastTarget = false;                      // 一个都不许吃射线
        img.enabled = false;
        pool.Add(img);
        return pool.Count - 1;
    }

    void ReleaseSlot(int s)
    {
        if (s < 0 || s >= pool.Count) return;
        if (pool[s] != null) pool[s].enabled = false;
        free.Add(s);
    }

    void EnsureTint()
    {
        if (tint != null || container == null) return;
        var go = new GameObject("FxTint", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(container, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = GraphView.White;
        img.color = Theme.A(Theme.Honey, 0f);           // 只把画面顶亮一档，不许糊住字
        img.raycastTarget = false;
        img.enabled = false;
        tint = img;
    }

    void EnsureRoutine()
    {
        if (routine != null) return;
        routine = StartCoroutine(TickAll());
    }

    IEnumerator TickAll()
    {
        while (bursts.Count > 0)
        {
            // 用非缩放时钟：慢放/暂停时判定反馈该按真实毫秒走完，不然彩带会被 timeScale 拖成长条
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < bursts.Count; i++)
            {
                var b = bursts[i];
                b.Elapsed += dt;
                if (b.Elapsed >= b.End) { b.Dead = true; continue; }
                Draw(b);
            }
            for (int i = bursts.Count - 1; i >= 0; i--) if (bursts[i].Dead) Finish(bursts[i], i);
            ApplyTint();
            yield return null;
        }
        if (tint != null) tint.enabled = false;
        routine = null;
    }

    void Draw(Burst b)
    {
        float t = b.Elapsed;
        int cap = AliveCount(t, b.End);                 // 起飞节流：后段才起飞的先别画
        bool wa = b.Kind == VerdictKind.Wa;
        bool pop = b.Kind == VerdictKind.SamplePass || b.Kind == VerdictKind.SampleFail;

        for (int i = 0; i < b.Ps.Length; i++)
        {
            int s = b.Slots[i];
            if (s < 0 || s >= pool.Count) continue;
            var img = pool[s];
            if (img == null) continue;
            var p = b.Ps[i];
            bool structural = p.Shape == ShapeCrossH || p.Shape == ShapeCrossV;
            float lt = t - p.Delay;
            // 十字/弹入这类「结构件」不受节流，否则闪会缺一条胳膊
            float a = structural ? FlashAlphaAt(lt, CrossDur) : (i < cap ? AlphaAt(lt, p.Life) : 0f);
            if (a <= 0f) { img.enabled = false; continue; }

            Vector2 local = b.Origin + PositionAt(p, lt);
            if (Outside(local)) { img.enabled = false; continue; }

            var rt = (RectTransform)img.transform;
            rt.anchoredPosition = local;
            img.color = Theme.A(p.Color, a);
            if (p.Shape == ShapeCrossH)
            {
                rt.sizeDelta = new Vector2(CrossArmAt(lt, CrossDur), CrossArmThick);
                rt.localEulerAngles = Vector3.zero;
            }
            else if (p.Shape == ShapeCrossV)
            {
                rt.sizeDelta = new Vector2(CrossArmThick, CrossArmAt(lt, CrossDur));
                rt.localEulerAngles = Vector3.zero;
            }
            else
            {
                rt.sizeDelta = p.Size;
                rt.localEulerAngles = new Vector3(0f, 0f, RotationAt(p, lt));
            }
            img.enabled = true;
        }

        if (b.AnchorOwned && b.Anchor != null)
        {
            if (wa) b.Anchor.anchoredPosition = b.AnchorPos0 + ShakeOffsetAt(t, b.Seed);
            else if (pop) b.Anchor.localScale = b.AnchorScale0 * PopScaleAt(t, PopDur);
        }
    }

    // 飘出容器 200px 以外就不画：省 Image 批次，也让「不许跑出视口」这条断言在渲染端同样成立
    bool Outside(Vector2 local)
    {
        if (container == null) return false;
        var r = container.rect;
        return local.x < r.xMin - CullMarginPx || local.x > r.xMax + CullMarginPx ||
               local.y < r.yMin - CullMarginPx || local.y > r.yMax + CullMarginPx;
    }

    void ApplyTint()
    {
        if (tint == null) return;
        float a = 0f;
        for (int i = 0; i < bursts.Count; i++)
        {
            var b = bursts[i];
            if (!NeedsTint(b.Kind)) continue;
            float v = TintAlphaAt(b.Elapsed, TintDur);
            if (v > a) a = v;
        }
        if (a <= 0f) { tint.enabled = false; return; }
        tint.color = Theme.A(Theme.Honey, a);
        tint.enabled = true;
    }

    void Finish(Burst b, int index)
    {
        if (b == null) return;
        for (int i = 0; i < b.Slots.Length; i++) ReleaseSlot(b.Slots[i]);
        if (b.AnchorOwned && b.Anchor != null)
        {
            b.Anchor.anchoredPosition = b.AnchorPos0;   // 抖动的原值必须一字不差还回去
            b.Anchor.localScale = b.AnchorScale0;
        }
        if (index >= 0 && index < bursts.Count) bursts.RemoveAt(index);
    }

    void PlayCue(VerdictKind kind)
    {
        if (!AutoSound) return;
        string cue = CueFor(kind);
        if (!string.IsNullOrEmpty(cue)) SoundSystem.Play(cue);
    }

    void StopEverything()
    {
        for (int i = bursts.Count - 1; i >= 0; i--) Finish(bursts[i], i);
        if (routine != null) { StopCoroutine(routine); routine = null; }
        if (tint != null) tint.enabled = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
