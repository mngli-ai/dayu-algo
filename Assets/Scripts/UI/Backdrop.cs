using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 关卡背景层：画布视口最底下的 2~3 层像素剪影（天空带 + 远景条 + 近景条），按章节换贴图、随镜头视差。
// 一切 Image 都 raycastTarget = false，绝不抢卡片 / 端口的鼠标命中；不用任何 uGUI 布局组件，平铺靠手动摆两条以上贴图。
// 接法见 Evidence/r16/backdrop-contract.md：App 里 Attach 一次，换关 SetChapterFor，Update 里 SetCameraFor，
// 截图模式（GraphCanvas.InstantView）要把 Instant 置真或调 Snap()，任何淡入直接落终态。
public sealed class Backdrop : MonoBehaviour
{
    public static Backdrop Instance;
    public const string DefaultKey = "bg_dock";

    // 项目铁律：补间在截图验收里不许留半透明中间帧。Instant 为真时 SetChapter/SetCamera 全部同步落终态，
    // 赋值 Instant 本身也会立刻掐掉正在进行的淡入。
    public bool Instant
    {
        get { return instant; }
        set
        {
            instant = value;
            if (value) { fading = false; alpha = 1f; ApplyAlpha(); }
        }
    }
    bool instant;

    const float BaseScale = 3f;
    const float FarK = 0.12f, NearK = 0.30f;
    const float FarZoomMix = 0.25f, NearZoomMix = 0.55f;
    const float FarOverlap = 18f;
    const float FadeDur = 0.35f;
    const int MaxTiles = 12;

    class Layer
    {
        public readonly List<Image> Tiles = new List<Image>();
        public Sprite Sprite;
        public float Phase;
        public float ZoomMix;
    }

    RectTransform view;
    readonly Layer far = new Layer { ZoomMix = FarZoomMix };
    readonly Layer near = new Layer { ZoomMix = NearZoomMix };
    Image skyTop, skyBottom;
    Color skyTopBase, skyBottomBase;
    string key;
    float zoom = 1f, alpha = 1f;
    bool fading, dirty;
    Vector2 lastSize;
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
        if (Instant) { fading = false; alpha = 1f; Layout(); ApplyAlpha(); return; }
        alpha = 0f; fading = true; ApplyAlpha();
    }

    // worldPos 传 GraphView 的 content.anchoredPosition（视图单位），远景 0.12、近景 0.30 同向慢随，天空带不动
    public void SetCamera(Vector2 worldPos, float zoom)
    {
        far.Phase = worldPos.x * FarK;
        near.Phase = worldPos.x * NearK;
        this.zoom = Mathf.Clamp(zoom, .42f, 1.8f);
        dirty = true;
        if (Instant) Layout();
    }

    public void Snap()
    {
        fading = false; alpha = 1f; ApplyAlpha();
        dirty = true; Layout();
    }

    void Build()
    {
        skyTopBase = Theme.A(Theme.MintLight, .16f);
        skyBottomBase = Theme.A(Theme.Paper, .10f);
        skyTop = Band("SkyFar", new Vector2(0f, .45f), Vector2.one, skyTopBase);
        skyBottom = Band("SkyNear", Vector2.zero, new Vector2(1f, .45f), skyBottomBase);
        for (int i = 0; i < 4; i++) { far.Tiles.Add(NewTile(transform)); near.Tiles.Add(NewTile(transform)); }
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

        Place(near, nw, nh, nh);
        Place(far, fw, fh, nh + fh - FarOverlap);
        ApplyAlpha();
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

    void ApplyAlpha()
    {
        if (skyTop != null) skyTop.color = new Color(skyTopBase.r, skyTopBase.g, skyTopBase.b, skyTopBase.a * alpha);
        if (skyBottom != null) skyBottom.color = new Color(skyBottomBase.r, skyBottomBase.g, skyBottomBase.b, skyBottomBase.a * alpha);
        for (int i = 0; i < far.Tiles.Count; i++) if (far.Tiles[i] != null && far.Tiles[i].enabled) far.Tiles[i].color = new Color(1f, 1f, 1f, alpha);
        for (int i = 0; i < near.Tiles.Count; i++) if (near.Tiles[i] != null && near.Tiles[i].enabled) near.Tiles[i].color = new Color(1f, 1f, 1f, alpha);
    }

    void LateUpdate()
    {
        if (fading)
        {
            alpha = Mathf.Min(1f, alpha + Time.unscaledDeltaTime / FadeDur);
            if (alpha >= 1f) fading = false;
            ApplyAlpha();
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
