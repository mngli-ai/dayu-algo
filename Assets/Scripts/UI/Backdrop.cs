using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 关卡背景层（r18 重写）：r17 的程序化剪影条被玩家点名「都是毛刺，丑丑的，不如上一个版本」，
// 这轮换成搜集来的真像素美术资产——CraftPix「Underwater World」五层水下视差套装（OGA-BY 3.0，
// 台账见 ArtInbox/SOURCES.md；tools/gen_backdrop.py 原样拷字节到 Resources/Backdrop/，点采样导入）：
//   uw_water 全幅不透明水体渐变（顶锚铺满视口，充当「天空」）
//   uw_fish  鱼群（中层，自带漂移，镜像瓦片交替让鱼朝向一左一右）
//   uw_rocks 礁石剪影（远景，底锚）
//   uw_sand  沙底（中近景，底锚）
//   uw_coral 珊瑚前景（近景，底锚）
// 横视差系数远→近：水体 .04 / 鱼 .08（+漂移）/ 礁石 .12 / 沙 .20 / 珊瑚 .30，五层拉开纵深。
// r17 保留下来的三条通道：
//  1) 天空渐变带：4 段 skyDeep↔skyLight 插值带现在压在礁石之上、沙底之下，随 worldPos.y 走 SkyT
//     （滚下去=深海偏深偏紫，滚上去=浅水偏冷偏亮）——仍是纵向滚动「看得见在动」的无界主通道。
//  2) 纵视差：Sat 限幅、振幅参考高 AmpRef=120（顶格 远档±7.2px / 近档±15.6px），近档摆幅 > 远档
//     （YOffsetsFor 契约不变），底锚层各留 34px 下出血（FarOverlap/NearBleed），极端滚动不露底、不撕裂。
//  3) 固定种子微粒改成上升气泡（ParticleRise），x/y 双向视差 + Mathf.Repeat 无限环绕，截图布局可复现。
// 素材横向不无缝：瓦片交替 localScale.x=-1 镜像（MirrorTileX/MirrorFlip 纯函数，自测钉住占槽连续与奇偶交替），
// A 瓦右缘永远接自己镜像的右缘，接缝连续不依赖素材作者做无缝；镜像周期 2 瓦，相位按 2*tw 环绕防跳变。
// 一切 Image 都 raycastTarget = false，绝不抢卡片 / 端口的鼠标命中；不用任何 uGUI 布局组件，平铺靠手动摆瓦片。
// 公开 API 与 r16/r17 契约完全兼容：Attach / SetChapterFor / SetCameraFor / SetChapter / SetCamera /
// Snap / Instant / TextureKeyFor 的签名与语义不变。TextureKeyFor 仍返回 bg_dock/bg_factory/bg_shelf/bg_deep
// 四键，但四键现在共用同一套水下五层，靠 TintFor 的章节染色区分（码头=原色、数组=偏绿、厂房=偏紫、深海=偏深蓝）。
// 静态调参字段要在 Attach 之前（或配合 Snap()）改才会立刻生效。
public sealed class Backdrop : MonoBehaviour
{
    public static Backdrop Instance;
    public const string DefaultKey = "bg_dock";

    // —— 视差与动态（默认值即验收值；审美微调改这里再 Snap()）——
    public static float FarXParallax = .12f;      // 礁石（远景）横视差系数
    public static float NearXParallax = .30f;     // 珊瑚（近景）横视差系数
    public static float FarYParallax = .05f;      // 纵视差输入系数（远档）
    public static float NearYParallax = .14f;     // 纵视差输入系数（近档）
    public static float WaterXParallax = .04f;    // 水体横视差（全场最远，动得最少）
    public static float FishXParallax = .08f;     // 鱼群横视差
    public static float SandXParallax = .20f;     // 沙底横视差（礁石与珊瑚之间）
    public static float FishDriftSpeed = 8f;      // 鱼群自主漂移（视图像素/秒；0=关。画布不动时背景也有生命）
    public static float ParticleRise = 9f;        // 气泡上升速度（视图像素/秒）
    public static float ParticleParallaxX = .05f; // 气泡横系数
    public static float ParticleParallaxY = .18f; // 气泡纵系数（无界环绕，纵向变化的第二条通道）
    public static int ParticleCount = 48;         // 气泡数量（Attach 时一次生成；固定种子，截图可复现）

    // 项目铁律：补间在截图验收里不许留半透明中间帧。Instant 为真时 SetChapter/SetCamera 全部同步落终态，
    // 赋值 Instant 本身也会立刻掐掉正在进行的淡入。
    public bool Instant
    {
        get { return instant; }
        set
        {
            instant = value;
            if (value) { fading = false; alpha = 1f; ApplyColors(); }
        }
    }
    bool instant;

    const float BaseScale = 3f;             // 576x324 素材 x3 = 1728x972，像素颗粒与卡片图标同档
    const float WaterZoomMix = .25f, FishZoomMix = .25f, RockZoomMix = .25f, SandZoomMix = .40f, CoralZoomMix = .55f;
    const float AmpRef = 120f;              // 纵视差振幅参考高：沿用 r17 顶格 远±7.2 / 近±15.6 的手感
    // 公开只为自测断言「纵振幅必须落在两者之内」；改数值要同时看 BackdropTests.VerticalParallax。
    public const float FarOverlap = 34f;    // 底锚层下出血（远档）：> 顶格正偏移 7.2，礁石上移时视口底不露空
    public const float NearBleed = 34f;     // 底锚层下出血（近档）：> 顶格正偏移 15.6，珊瑚/沙上移不露底
    // 纵向振幅 = AmpRef x 该系数（Sat 限幅后）。近档必须大于远档：远处那座礁石不能比眼前这丛珊瑚晃得多，
    // 否则上下滚动读起来是「整层平移」，纵向视差就白做了（横视差靠 .04~.30 五档已经是这个顺序）。
    public static float FarYAmpK = .06f;
    public static float NearYAmpK = .13f;
    const float FadeDur = .35f;
    const int MaxTiles = 12;                // 每层瓦片池上限（镜像周期 2 瓦，最坏 x0=-2tw 也只需 ceil((vw+2tw)/tw) 条）
    const int ParticleSeed = 1717;          // 固定种子：气泡布局每次运行一致，验收截图可复现
    const float WaterCoverMargin = 14f;     // 水体覆盖余量：视口高 + 顶格上移量，保证底边永远够到视口底

    // 五层的静态定义。YMix = 纵偏移在 远档(AmpRef*FarUnit) 与 近档(AmpRef*NearUnit) 之间的插值因子；
    // Bleed = 底锚下出血；TopAnchor 只有水体为真（波浪贴视口顶，其余层的内容都压在图底部）。
    class Def
    {
        public readonly string Res;
        public readonly float ZoomMix, YMix, Bleed, Alpha;
        public readonly bool TopAnchor;
        public Def(string res, float zoomMix, float yMix, float bleed, float alpha, bool topAnchor)
        { Res = res; ZoomMix = zoomMix; YMix = yMix; Bleed = bleed; Alpha = alpha; TopAnchor = topAnchor; }
    }

    static readonly Def[] Defs =
    {
        new Def("uw_water", WaterZoomMix, .50f, 0f,         1f,   true),
        new Def("uw_fish",  FishZoomMix,  .35f, 0f,         .95f, false), // 鱼层底缘全透明，不需要出血
        new Def("uw_rocks", RockZoomMix,  0f,   FarOverlap, .85f, false),
        new Def("uw_sand",  SandZoomMix,  .50f, NearBleed,  .95f, false),
        new Def("uw_coral", CoralZoomMix, 1f,   NearBleed,  1f,   false),
    };

    class Layer
    {
        public readonly Def Def;
        public Sprite Sprite;
        public readonly List<Image> Tiles = new List<Image>();
        public Layer(Def d) { Def = d; }
    }

    class Particle
    {
        public Image Img;
        public float Ox, Oy; // 基准位置（视口宽高归一 0..1）
        public float Bri;    // 亮度抖动 0..1
        public int Size;     // 3~6 视图像素
    }

    RectTransform view;
    readonly List<Layer> layers = new List<Layer>();
    readonly List<Particle> particles = new List<Particle>();
    readonly Image[] sky = new Image[4];         // 4 段竖向渐变带，锚定静止，颜色随 worldPos.y 插值
    readonly Color[] skyBase = new Color[4];     // Layout 算出的当帧基色（含 alpha，未乘淡入）
    Color skyLight, skyDeep;                     // 渐变两端：浅水冷亮 / 深海暗紫
    Color particleBase;                          // 气泡基色（薄荷浅+白 Mix，Build 里算一次）
    string key;
    float zoom = 1f, alpha = 1f;
    bool fading, dirty;
    float fishDrift, bubbleT;                    // 自主动态的累计量（unscaled 时间积分，不受 Time.timeScale 影响）
    Vector2 lastSize, camPos;
    static Sprite[] spriteCache;                 // 五层共用一套贴图，全局只加载一次

    public static Backdrop Attach(RectTransform view, Transform under)
    {
        if (view == null) return null;
        var go = new GameObject("Backdrop", typeof(RectTransform));
        go.transform.SetParent(view, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        // UGUI 按兄弟顺序绘制：插到 under（通常是 Grid 点阵）前面，背景才压在点阵与所有卡片之下
        if (under != null && under.parent == view) rt.SetSiblingIndex(under.GetSiblingIndex());
        else rt.SetSiblingIndex(0);
        var b = go.AddComponent<Backdrop>();
        b.view = view;
        b.Build();
        Instance = b;
        b.SetChapter(null);
        return b;
    }

    public static string TextureKeyFor(string chapter)
    {
        string s = chapter == null ? "" : chapter;
        if (s.IndexOf("数组") >= 0) return "bg_shelf";
        if (s.IndexOf("分支") >= 0 || s.IndexOf("循环") >= 0) return "bg_factory";
        if (s.IndexOf("栈") >= 0 || s.IndexOf("队列") >= 0 || s.StartsWith("第一章")) return "bg_deep";
        if (s.StartsWith("序章")) return "bg_dock";
        return DefaultKey;
    }

    // 章节染色：四键共用一套水下素材，靠一层乘色区分章节气氛（公开纯函数，BackdropTests.ChapterTint 钉住）。
    // bg_dock 与一切未知键回原色（白），乘色分量只许落在 (0,1]，绝不把素材乘成负色或过曝。
    public static Color TintFor(string key)
    {
        switch (key)
        {
            case "bg_shelf": return new Color(.86f, 1f, .90f, 1f);    // 数组·货架：偏绿水
            case "bg_factory": return new Color(.95f, .86f, 1f, 1f);  // 分支/循环·厂房：偏紫水
            case "bg_deep": return new Color(.72f, .80f, 1f, 1f);     // 栈/队列·深海：偏深蓝
            default: return Color.white;                              // 码头与未知键：原色
        }
    }

    public static void SetChapterFor(string chapter)
    {
        if (Instance != null) Instance.SetChapter(chapter);
    }

    public static void SetCameraFor(Vector2 worldPos, float zoom)
    {
        if (Instance != null) Instance.SetCamera(worldPos, zoom);
    }

    public void SetChapter(string chapter)
    {
        string k = TextureKeyFor(chapter);
        if (k == key && layers.Count > 0 && layers[0].Sprite != null) return;
        key = k;
        if (!EnsureSprites()) return;
        for (int i = 0; i < layers.Count; i++) layers[i].Sprite = spriteCache[i];
        dirty = true;
        if (Instant) { fading = false; alpha = 1f; Layout(); return; }
        alpha = 0f; fading = true; ApplyColors();
    }

    // worldPos 传 GraphView 的 content.anchoredPosition（视图单位）。x 给五档横视差（.04/.08/.12/.20/.30），
    // y 给纵视差（经 Sat 限幅）与天空渐变插值；气泡层吃 x/y 双向 .05/.18 外加自主上升。
    public void SetCamera(Vector2 worldPos, float zoom)
    {
        camPos = worldPos;
        this.zoom = Mathf.Clamp(zoom, .42f, 1.8f);
        dirty = true;
        if (Instant) Layout();
    }

    public void Snap()
    {
        fading = false; alpha = 1f;
        dirty = true; Layout();
    }

    void Build()
    {
        // 天空两端色：全部由 16 色板 Mix 而来（板外零新色）。亮端=薄荷浅+泡沫淡青，暗端=墨+葡萄紫。
        skyLight = Theme.Mix(Theme.MintLight, Theme.Foam, .30f);
        skyDeep = Theme.Mix(Theme.Ink, Theme.Grape, .40f);
        particleBase = Theme.Mix(Theme.MintLight, Theme.White, .5f);

        // 兄弟序（先画的在下）：水体 → 鱼 → 礁石 → 天空渐变带 → 沙底 → 珊瑚 → 气泡。
        // 渐变带压在远三层之上、近两层之下：滚动变暗时远处先沉下去，前景珊瑚保持清楚，纵深读得出来。
        for (int i = 0; i < Defs.Length; i++)
        {
            var l = new Layer(Defs[i]);
            for (int t = 0; t < 4; t++) l.Tiles.Add(NewTile());
            if (i == 2) for (int s = 0; s < 4; s++)   // 礁石之后、沙底之前插入 4 段天空带
                {
                    float aMin = 1f - (s + 1) * .25f, aMax = 1f - s * .25f;
                    sky[s] = Band("Sky" + s, new Vector2(0f, aMin), new Vector2(1f, aMax), Color.white);
                }
            layers.Add(l);
        }

        var rnd = new System.Random(ParticleSeed); // 固定种子，不用 UnityEngine.Random，保证截图可复现
        for (int i = 0; i < ParticleCount; i++)
        {
            var img = NewTile();
            img.sprite = GraphView.White;
            particles.Add(new Particle
            {
                Img = img,
                Ox = (float)rnd.NextDouble(),
                Oy = (float)rnd.NextDouble(),
                Bri = (float)rnd.NextDouble(),
                Size = 3 + i % 4,
            });
        }
    }

    static bool EnsureSprites()
    {
        if (spriteCache != null) return true;
        var sp = new Sprite[Defs.Length];
        for (int i = 0; i < Defs.Length; i++)
        {
            var tex = Resources.Load<Texture2D>("Backdrop/" + Defs[i].Res);
            if (tex == null) { Debug.LogWarning("[Backdrop] 缺少素材 Backdrop/" + Defs[i].Res); return false; }
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0f, 1f), 100f);
            s.name = Defs[i].Res;
            sp[i] = s;
        }
        spriteCache = sp;
        return true;
    }

    Image Band(string name, Vector2 aMin, Vector2 aMax, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = GraphView.White;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    Image NewTile()
    {
        var go = new GameObject("BgTile", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0f, 1f);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        img.enabled = false;
        go.SetActive(false);
        return img;
    }

    // 饱和限幅：v→(-1,1)。纵视差幅度有界，滚多远剪影都不会跑出天顶/海沟。
    static float Sat(float v) { return v / (1f + Mathf.Abs(v)); }

    static float FarUnit(float camY) { return FarYAmpK * Sat(camY * FarYParallax); }
    static float NearUnit(float camY) { return NearYAmpK * Sat(camY * NearYParallax); }

    /// <summary>纵视差的两位移（x = 远档、y = 近档），单位是视图像素。
    /// 抽成公开纯函数只为让自测能断言「近景摆幅处处 ≥ 远景」——这条是纵视差读不读得出来的关键。</summary>
    public static Vector2 YOffsetsFor(float camY, float farH, float nearH)
    {
        return new Vector2(farH * FarUnit(camY), nearH * NearUnit(camY));
    }

    // 天空某一段的插值因子（0=深海端、1=浅水端），抽成公开纯函数给自测断言单调与覆盖范围。
    // SkyYResponse=1：滚满一屏（sY 到 ±1）时每一段都走完自己那半程——五层贴图的纵向位移有天花板，
    // 「上下滚背景有变化」主要就靠这一路整体变色加上会环绕的气泡。
    public static float SkyYResponse = 1f;

    public static float SkyT(int i, float sY)
    {
        return Mathf.Clamp01(1f - i * (1f / 3f) - sY * SkyYResponse);
    }

    // 渐变带的不透明度两端：深海端压得比浅水端重得多，滚下去整块画布肉眼可见地暗下去，
    // 同时把画布下沿（正是沙底/珊瑚所在、也最容易和模块卡片混色的一带）与卡片拉开。
    // 公开只为自测断言「深端必须重于浅端、且差够一档」。
    public static float SkyAlphaDeep = .30f;
    public static float SkyAlphaLight = .12f;

    /// <summary>镜像瓦片 j 的 anchoredPosition.x（瓦片 pivot 在左上、镜像时 localScale.x=-1 绕 pivot 翻）。
    /// 偶数瓦正放占槽 [x0+j*tw, x0+(j+1)*tw]；奇数瓦镜像后要放到槽右缘才盖住同一个槽。
    /// 公开纯函数，BackdropTests.MirrorTiling 钉住占槽连续与奇偶交替。</summary>
    public static float MirrorTileX(float x0, float tw, int j)
    {
        return x0 + (j + (j & 1)) * tw;
    }

    public static bool MirrorFlip(int j) { return (j & 1) == 1; }

    /// <summary>把 texW x texH 的贴图放大到恰好盖住 viewW x viewH 的最小倍数（取两维需求的较大者）。
    /// 公开纯函数，BackdropTests.CoverScale 钉住「盖得住且不多放大另一维」。</summary>
    public static float CoverScaleFor(float viewW, float viewH, float texW, float texH)
    {
        return Mathf.Max(viewW / texW, viewH / texH);
    }

    void Layout()
    {
        if (view == null) return;
        var r = view.rect;
        if (r.width < 1f || r.height < 1f) return;
        lastSize = r.size;
        dirty = false;

        // 纵视差：以 AmpRef 为参考高，远档/近档顶格 ±7.2 / ±15.6，各层按 YMix 取两档之间
        Vector2 yOff = YOffsetsFor(camPos.y, AmpRef, AmpRef);

        // 天空 4 段：纵向基渐变（上亮下深）+ 随 worldPos.y 整体插值。
        // GraphView 里 content.anchoredPosition.y<0 = 看画布上方（浅水）→ 偏冷偏亮；>0 = 看下方（深海）→ 偏深偏紫。
        float sY = Mathf.Clamp(camPos.y / (r.height * 1.2f), -1f, 1f);
        for (int i = 0; i < 4; i++)
        {
            float t = SkyT(i, sY);
            Color c = Color.Lerp(skyDeep, skyLight, t);
            skyBase[i] = new Color(c.r, c.g, c.b, Mathf.Lerp(SkyAlphaDeep, SkyAlphaLight, t));
        }

        float[] xk = { WaterXParallax, FishXParallax, FarXParallax, SandXParallax, NearXParallax };
        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            if (l.Sprite == null) continue;
            var d = l.Def;
            float s = BaseScale * Mathf.Lerp(1f, zoom, d.ZoomMix);
            float tw = l.Sprite.rect.width * s;
            float th = l.Sprite.rect.height * s;
            float phase = camPos.x * xk[i] + (i == 1 ? fishDrift : 0f);
            float yMix = Mathf.Lerp(yOff.x, yOff.y, d.YMix);
            if (d.TopAnchor)
            {
                // 水体顶锚：盖住视口 + 顶格上移余量；向下滚波浪上移出画，向上滚钳在视口顶（水面之上无从再滚）
                float cover = CoverScaleFor(r.width, r.height + WaterCoverMargin,
                                          l.Sprite.rect.width, l.Sprite.rect.height);
                if (s < cover) { s = cover; tw = l.Sprite.rect.width * s; th = l.Sprite.rect.height * s; }
                Place(l, phase, tw, th, r.height + Mathf.Max(0f, yMix));
            }
            else
            {
                float bottomY = yMix - d.Bleed;
                Place(l, phase, tw, th, bottomY + th);
            }
        }
        LayoutParticles(r.width, r.height);
        ApplyColors();
    }

    // 横向镜像平铺：相位按 2*tw（一个镜像周期）环绕，首瓦最多错开两个瓦片宽，铺到视口右缘为止。
    // 奇数瓦 localScale.x=-1：A 瓦右缘接 A 镜像的右缘，非无缝素材也不露接缝。
    void Place(Layer l, float phase, float tw, float th, float topY)
    {
        if (l.Sprite == null || tw < 1f) return;
        float x0 = Mathf.Repeat(phase, 2f * tw) - 2f * tw;
        int n = Mathf.Clamp(Mathf.CeilToInt((view.rect.width - x0) / tw), 1, MaxTiles);
        while (l.Tiles.Count < n) l.Tiles.Add(NewTile());
        for (int j = 0; j < l.Tiles.Count; j++)
        {
            var img = l.Tiles[j];
            if (img == null) continue;
            bool on = j < n;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
            if (!on) continue;
            img.sprite = l.Sprite;
            img.enabled = true;
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(tw, th);
            rt.localScale = new Vector3(MirrorFlip(j) ? -1f : 1f, 1f, 1f);
            rt.anchoredPosition = new Vector2(MirrorTileX(x0, tw, j), topY);
        }
    }

    void LayoutParticles(float w, float h)
    {
        for (int i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            if (p == null || p.Img == null) continue;
            var rt = p.Img.rectTransform;
            // 视口归一布局 + .05/.18 双向视差 + 匀速上升，Repeat 环绕：x/y 任意滚动都连续、不露缝、不聚集
            float px = Mathf.Repeat(p.Ox * w + camPos.x * ParticleParallaxX, w);
            float py = Mathf.Repeat(p.Oy * h + camPos.y * ParticleParallaxY + bubbleT * ParticleRise, h);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(.5f, .5f);
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(p.Size, p.Size);
            rt.anchoredPosition = new Vector2(px, py);
            if (!p.Img.gameObject.activeSelf) p.Img.gameObject.SetActive(true);
            p.Img.enabled = true;
        }
    }

    void ApplyColors()
    {
        for (int i = 0; i < 4; i++)
            if (sky[i] != null) sky[i].color = new Color(skyBase[i].r, skyBase[i].g, skyBase[i].b, skyBase[i].a * alpha);
        Color tint = TintFor(key);
        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            Color c = new Color(tint.r, tint.g, tint.b, l.Def.Alpha * alpha);
            for (int j = 0; j < l.Tiles.Count; j++)
            {
                var img = l.Tiles[j];
                if (img == null || !img.enabled) continue;
                img.color = c;
            }
        }
        for (int i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            if (p == null || p.Img == null || !p.Img.enabled) continue;
            p.Img.color = new Color(particleBase.r, particleBase.g, particleBase.b, (.16f + .18f * p.Bri) * alpha);
        }
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        if (FishDriftSpeed != 0f || ParticleRise != 0f)
        {
            fishDrift += dt * FishDriftSpeed;
            bubbleT += dt;
            dirty = true;  // 自主动态：画布不动时鱼也在游、气泡也在升
        }
        if (fading)
        {
            alpha = Mathf.Min(1f, alpha + dt / FadeDur);
            if (alpha >= 1f) fading = false;
        }
        if (dirty || (view != null && view.rect.size != lastSize)) Layout();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
