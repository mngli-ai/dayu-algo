using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 关卡背景层（r17）：画布视口最底下现在是「4 段天空带 + 远景条 + 分层暗带 + 近景条 + 稀疏微粒」共 5 组元素。
// 三点玩家反馈的落法：
//  1) 背景退到「几乎感觉不到但确实有层次」：贴图剪影只用 Grape/Ink/WhaleDeep 沉色（见 gen_art.py r17 注释），
//     运行时再乘一层暗色 Tint 并把不透明度收进 0.25~0.40，比模块卡片（Salt/Paper/Honey/Mint/Whale）暗且色相拉开。
//  2) 纵向滚动必须有变化：条带剪影的纵向摆幅被自身高度与 FarOverlap/NearBleed 卡死（zoom=1 顶格仅 ±7/±16px，
//     再大就撕裂或露底），所以纵向「看得见在动」主要落在两处无界响应上：天空 4 段渐变随 worldPos.y 整体插值
//     （SkyYResponse=1，滚满一屏每段走完自己那半程；往上滚=浅水偏冷偏亮，往下滚=深海偏深偏紫），
//     以及一层 x/y 双向视差（横 .05 / 纵 .18）、Mathf.Repeat 无限环绕的固定种子微粒（3~6px，α .16~.34）。
//     条带仍吃纵视差（系数远 .05 / 近 .14，Sat 限幅，瓦片下方加 NearBleed 余量），只是量级小。
//  3) 质感：远景条与近景条交界处压一条极淡 Ink 分层暗带（α.08，只跟随近景纵向偏移），画面有纵深不是两条平贴剪影。
// 一切 Image 都 raycastTarget = false，绝不抢卡片 / 端口的鼠标命中；不用任何 uGUI 布局组件，平铺靠手动摆瓦片。
// 公开 API 与 r16 契约（Evidence/r16/backdrop-contract.md）完全兼容：Attach / SetChapterFor / SetCameraFor /
// SetChapter / SetCamera / Snap / Instant / TextureKeyFor 的签名与语义不变，新增的只是静态调参字段。
// 静态调参字段要在 Attach 之前（或配合 Snap()）改才会立刻生效。
public sealed class Backdrop : MonoBehaviour
{
    public static Backdrop Instance;
    public const string DefaultKey = "bg_dock";

    // —— r17 新增公开静态配置（默认值即验收值；主 agent 不需要动，审美微调可改这里再 Snap()）——
    public static float FarXParallax = .12f;      // 远景横视差系数（原 FarK，语义不变）
    public static float NearXParallax = .30f;     // 近景横视差系数（原 NearK）
    public static float FarYParallax = .05f;      // 远景纵视差系数（小）
    public static float NearYParallax = .14f;     // 近景纵视差系数（大）
    public static float ParticleParallaxX = .05f; // 微粒横系数（全场最小，0.05 档）
    // 微粒纵系数吃到 .18：条带剪影的纵向位移被自身高度与重叠量卡死（远景 ±7px、近景 ±16px 封顶），
    // 上下滚动要看得出「背景真的在动」，靠的就是这层能无限环绕、不露缝的浮游物 + 天空整体变色。
    public static float ParticleParallaxY = .18f;
    public static float FarLayerAlpha = .30f;     // 远景条最终不透明度（收在 .25~.40 档内）
    public static float NearLayerAlpha = .38f;    // 近景条最终不透明度
    public static Color FarTint = new Color(.38f, .42f, .54f, 1f);   // 远景压暗染色（深雾紫，降饱和）
    public static Color NearTint = new Color(.50f, .58f, .66f, 1f);  // 近景压暗染色（墨青）
    public static int ParticleCount = 48;         // 微粒数量（Attach 时一次生成；固定种子，截图可复现）

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

    const float BaseScale = 3f;
    const float FarZoomMix = .25f, NearZoomMix = .55f;
    // 公开只为自测断言「纵振幅必须落在两者之内」；改数值要同时看 BackdropTests.VerticalParallax。
    public const float FarOverlap = 34f;   // 远景条下沿扎进近景区的量：要 > 两纵视差振幅之差，上下滚才不裂开
    public const float NearBleed = 34f;    // 近景条向下多铺的量：纵向视差把条整体上移时视口底不露空
    // 纵向振幅 = 条高 x 该系数（Sat 限幅后）。近景必须大于远景：远处那座山不能比眼前这条甲板晃得多，
    // 否则上下滚动读起来是「两层一起平移」，纵向视差就白做了（横视差靠 .12/.30 已经是这个顺序）。
    // 顶格值：远景 ≤ 120x.06=7.2px、近景 ≤ 120x.13=15.6px，两者之差 8.4 < FarOverlap 34，近景上移量 < NearBleed 34。
    public static float FarYAmpK = .06f;
    public static float NearYAmpK = .13f;
    const float FadeDur = .35f;
    const int MaxTiles = 12;
    const int ParticleSeed = 1717;  // 固定种子：微粒布局每次运行一致，验收截图可复现
    const float DepthBandH = 26f;   // 分层暗带高度（视图像素）
    const float DepthBandAlpha = .08f;

    class Layer
    {
        public readonly List<Image> Tiles = new List<Image>();
        public Sprite Sprite;
        public float Phase; // 横视差位移（SetCamera 写入）
    }

    class Particle
    {
        public Image Img;
        public float Ox, Oy; // 基准位置（视口宽高归一 0..1）
        public float Bri;    // 亮度抖动 0..1
        public int Size;     // 3~6 视图像素
    }

    RectTransform view;
    readonly Layer far = new Layer();
    readonly Layer near = new Layer();
    readonly List<Particle> particles = new List<Particle>();
    readonly Image[] sky = new Image[4];         // 4 段竖向天空带，锚定静止，颜色随 worldPos.y 插值
    readonly Color[] skyBase = new Color[4];     // Layout 算出的当帧基色（含 alpha，未乘淡入）
    Image depth;                                 // 远/近条之间的分层暗带
    Color skyLight, skyDeep;                     // 天空插值两端：浅水冷亮 / 深海暗紫
    Color particleBase;                          // 微粒基色（薄荷浅+白 Mix，Build 里算一次）
    string key;
    float zoom = 1f, alpha = 1f;
    bool fading, dirty;
    Vector2 lastSize, camPos;
    static readonly Dictionary<string, Sprite[]> SpriteCache = new Dictionary<string, Sprite[]>();

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
        if (k == key && far.Sprite != null) return;
        var sp = LoadSprites(k);
        if (sp == null) return;
        key = k; far.Sprite = sp[0]; near.Sprite = sp[1];
        dirty = true;
        if (Instant) { fading = false; alpha = 1f; Layout(); return; }
        alpha = 0f; fading = true; ApplyColors();
    }

    // worldPos 传 GraphView 的 content.anchoredPosition（视图单位）。x 给横视差（远 .12 / 近 .30），
    // y 给纵视差（远 .05 / 近 .14，经 Sat 限幅）与天空渐变插值；微粒层吃 x/y 双向 .05。
    public void SetCamera(Vector2 worldPos, float zoom)
    {
        far.Phase = worldPos.x * FarXParallax;
        near.Phase = worldPos.x * NearXParallax;
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
        for (int i = 0; i < 4; i++)
        {
            float aMin = 1f - (i + 1) * .25f, aMax = 1f - i * .25f;
            sky[i] = Band("Sky" + i, new Vector2(0f, aMin), new Vector2(1f, aMax), Color.white);
        }
        // 分层暗带：宽度拉伸铺满，高度固定，纵向位置在 Layout 里跟着近景条顶边走。
        // 兄弟序必须 = 天空(0..3) → 远瓦片 → 暗带 → 近瓦片 → 微粒：UGUI 先画的在下，
        // 暗带要盖住远景条、又被近景条盖住，才在两条之间形成「交界压暗」的纵深。
        for (int i = 0; i < 4; i++) far.Tiles.Add(NewTile(transform));
        depth = Band("Depth", Vector2.zero, Vector2.zero, Color.white);
        var dRT = depth.rectTransform;
        dRT.anchorMin = new Vector2(0f, 0f);
        dRT.anchorMax = new Vector2(1f, 0f);
        dRT.pivot = new Vector2(.5f, 0f);
        dRT.sizeDelta = new Vector2(0f, DepthBandH);
        for (int i = 0; i < 4; i++) near.Tiles.Add(NewTile(transform));
        var rnd = new System.Random(ParticleSeed); // 固定种子，不用 UnityEngine.Random，保证截图可复现
        for (int i = 0; i < ParticleCount; i++)
        {
            var img = NewTile(transform);
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

    Image NewTile(Transform parent)
    {
        var go = new GameObject("BgTile", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
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

    /// <summary>纵视差的两位移（x = 远景、y = 近景），单位是视图像素。
    /// 抽成公开纯函数只为让自测能断言「近景摆幅处处 ≥ 远景」——这条是纵视差读不读得出来的关键。</summary>
    public static Vector2 YOffsetsFor(float camY, float farH, float nearH)
    {
        return new Vector2(farH * FarYAmpK * Sat(camY * FarYParallax),
                           nearH * NearYAmpK * Sat(camY * NearYParallax));
    }

    // 天空某一段的插值因子（0=深海端、1=浅水端），抽成公开纯函数给自测断言单调与覆盖范围。
    // SkyYResponse=1：滚满一屏（sY 到 ±1）时每一段都走完自己那半程——条带剪影的纵向位移有天花板，
    // 「上下滚背景有变化」主要就靠这一路整体变色加上会环绕的微粒。
    public static float SkyYResponse = 1f;

    public static float SkyT(int i, float sY)
    {
        return Mathf.Clamp01(1f - i * (1f / 3f) - sY * SkyYResponse);
    }

    // 天空段的不透明度两端：深海端压得比浅水端重得多，滚下去整块画布肉眼可见地暗下去，
    // 同时把画布下沿（正是两条剪影所在、也最容易和模块卡片混色的一带）与卡片拉开。
    // 公开只为自测断言「深端必须重于浅端、且差够一档」。
    public static float SkyAlphaDeep = .30f;
    public static float SkyAlphaLight = .12f;

    void Layout()
    {
        if (view == null) return;
        var r = view.rect;
        if (r.width < 1f || r.height < 1f) return;
        lastSize = r.size;
        dirty = false;

        float sn = BaseScale * Mathf.Lerp(1f, zoom, NearZoomMix);
        float sf = BaseScale * Mathf.Lerp(1f, zoom, FarZoomMix);
        float nw = near.Sprite != null ? near.Sprite.rect.width * sn : 0f;
        float nh = near.Sprite != null ? near.Sprite.rect.height * sn : 0f;
        float fw = far.Sprite != null ? far.Sprite.rect.width * sf : 0f;
        float fh = far.Sprite != null ? far.Sprite.rect.height * sf : 0f;

        // —— 纵视差：远景摆幅小、近景摆幅大，都经 Sat 限幅 ——
        Vector2 yOff = YOffsetsFor(camPos.y, fh, nh);
        float farYOff = yOff.x, nearYOff = yOff.y;
        float nearTop = nh + nearYOff;
        // 安全余量：120px 条高顶格时 |farYOff|≤7.2、|nearYOff|≤15.6，
        // 两者之差 8.4 < FarOverlap 34，两条之间永不裂口；NearBleed=34 > 15.6，近景条上移时视口底部不露空。

        // —— 天空 4 段：纵向基渐变（上亮下深）+ 随 worldPos.y 整体插值。
        //    GraphView 里 content.anchoredPosition.y<0 = 看画布上方（浅水）→ 偏冷偏亮；>0 = 看下方（深海）→ 偏深偏紫。
        float sY = Mathf.Clamp(camPos.y / (r.height * 1.2f), -1f, 1f);
        for (int i = 0; i < 4; i++)
        {
            float t = SkyT(i, sY);
            Color c = Color.Lerp(skyDeep, skyLight, t);
            skyBase[i] = new Color(c.r, c.g, c.b, Mathf.Lerp(SkyAlphaDeep, SkyAlphaLight, t));
        }

        // 分层暗带压在近景条顶沿（跟随近景纵偏移），盖在远景条之上、近景条之下（兄弟序已保证）
        depth.rectTransform.anchoredPosition = new Vector2(0f, nearTop - 4f);

        Place(near, nw, nh + NearBleed, nearTop);
        Place(far, fw, fh, nh + fh - FarOverlap + farYOff);
        LayoutParticles(r.width, r.height);
        ApplyColors();
    }

    void Place(Layer l, float tw, float th, float topY)
    {
        if (l.Sprite == null || tw < 1f) return;
        int n = Mathf.Clamp(Mathf.CeilToInt(view.rect.width / tw) + 1, 1, MaxTiles);
        while (l.Tiles.Count < n) l.Tiles.Add(NewTile(transform));
        // 首条向左错开不足一个瓦片宽，铺满视口后右端再补一条：平移时左右都不露空
        float x0 = Mathf.Repeat(l.Phase, tw) - tw;
        for (int i = 0; i < l.Tiles.Count; i++)
        {
            var img = l.Tiles[i];
            if (img == null) continue;
            bool on = i < n;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
            if (!on) continue;
            img.sprite = l.Sprite;
            img.enabled = true;
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(tw, th);
            rt.anchoredPosition = new Vector2(x0 + i * tw, topY);
        }
    }

    void LayoutParticles(float w, float h)
    {
        for (int i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            if (p == null || p.Img == null) continue;
            var rt = p.Img.rectTransform;
            // 视口归一布局 + .05 双向视差，Repeat 环绕：x/y 任意滚动都连续、不露缝、不聚集
            float px = Mathf.Repeat(p.Ox * w + camPos.x * ParticleParallaxX, w);
            float py = Mathf.Repeat(p.Oy * h + camPos.y * ParticleParallaxY, h);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(.5f, .5f);
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
        if (depth != null)
            depth.color = new Color(Theme.Ink.r / 255f, Theme.Ink.g / 255f, Theme.Ink.b / 255f, DepthBandAlpha * alpha);
        TintTiles(far, FarTint, FarLayerAlpha);
        TintTiles(near, NearTint, NearLayerAlpha);
        for (int i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            if (p == null || p.Img == null || !p.Img.enabled) continue;
            p.Img.color = new Color(particleBase.r, particleBase.g, particleBase.b, (.16f + .18f * p.Bri) * alpha);
        }
    }

    void TintTiles(Layer l, Color tint, float layerAlpha)
    {
        for (int i = 0; i < l.Tiles.Count; i++)
        {
            var img = l.Tiles[i];
            if (img == null || !img.enabled) continue;
            img.color = new Color(tint.r, tint.g, tint.b, layerAlpha * alpha);
        }
    }

    void LateUpdate()
    {
        if (fading)
        {
            alpha = Mathf.Min(1f, alpha + Time.unscaledDeltaTime / FadeDur);
            if (alpha >= 1f) fading = false;
            ApplyColors();
        }
        if (dirty || (view != null && view.rect.size != lastSize)) Layout();
    }

    static Sprite[] LoadSprites(string k)
    {
        Sprite[] sp;
        if (SpriteCache.TryGetValue(k, out sp)) return sp;
        var tex = Resources.Load<Texture2D>("Props/" + k);
        if (tex == null) { Debug.LogWarning("[Backdrop] 缺少素材 Props/" + k); return null; }
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        int half = tex.height / 2;
        // PIL 图的上半截是远景；Unity 贴图 v=0 在底，所以远景取 Rect 的上半
        var f = Sprite.Create(tex, new Rect(0, half, tex.width, tex.height - half), new Vector2(0f, 1f), 100f);
        var n = Sprite.Create(tex, new Rect(0, 0, tex.width, half), new Vector2(0f, 1f), 100f);
        f.name = k + "_far"; n.name = k + "_near";
        sp = new Sprite[] { f, n };
        SpriteCache[k] = sp;
        return sp;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
