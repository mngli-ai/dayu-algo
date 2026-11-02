using UnityEngine;
using UnityEngine.UI;

public sealed class PixelText : MaskableGraphic
{
    const int CellW = 12;
    public const int CellH = 14;   // 一行字块的高度（scale 1）；外部按 CellH*scale 反推垂直居中偏移

    string text = "";
    public string Text { get => text; set { text = value ?? ""; SetVerticesDirty(); } }
    int scale = 1;
    // 缩放会在 OnPopulateMesh 里重排字形，所以改完必须重画。
    public int Scale { get => scale; set { if (scale == value) return; scale = value; SetVerticesDirty(); } }
    public enum TextAlign { Left, Center, Right }
    public TextAlign Align { get; set; } = TextAlign.Left;
    public bool Wrap { get; set; }
    public int WrapWidth { get; set; }

    public static PixelText Create(Transform parent, string text, Color color, int scale = 1)
    {
        var go = new GameObject("PixelText", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<PixelText>();
        p.Text = text; p.color = color; p.Scale = scale;
        var rt = p.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        var m = p.Measure();
        rt.sizeDelta = new Vector2(Mathf.Max(1, m.x), Mathf.Max(1, m.y));
        return p;
    }

    protected override void Awake()
    {
        base.Awake();
        EnsureAtlas();
    }

    // 图集只认这一处加载入口：编辑器自测要在没有 Awake 的环境里量字宽，也得能自己把图集拉起来。
    public static void EnsureAtlas()
    {
        if (GlyphAtlas.Texture == null)
        {
            var tex = Resources.Load<Texture2D>("atlas");
            var j = Resources.Load<TextAsset>("atlas");
            if (tex && j) GlyphAtlas.Init(tex, j.text);
        }
    }

    // 中日韩与全角标点固定 12px 推进；ASCII 用字体的 adv，但至少 5px
    static float AdvanceOf(char c, GlyphAtlas.Glyph g)
    {
        if (c >= 0x2E80 || (c >= 0xFF00 && c <= 0xFF60) || c == 0x3000) return 12f;
        return Mathf.Max(5f, g.Adv);
    }

    /// 返回 (整块最宽行宽, 整块总高)
    public Vector2 Measure()
    {
        float x = 0, y = CellH * Scale, max = 0;
        foreach (char c in text)
        {
            if (c == '\n') { max = Mathf.Max(max, x); x = 0; y += CellH * Scale; continue; }
            if (GlyphAtlas.TryGet(c, out var g))
            {
                float ad = AdvanceOf(c, g) * Scale;
                if (Wrap && WrapWidth > 0 && x + ad > WrapWidth) { max = Mathf.Max(max, x); x = 0; y += CellH * Scale; }
                x += ad;
            }
        }
        return new Vector2(Mathf.Max(max, x), y);
    }

    /// 不换行时的纯文本尺寸（供按钮等在外层算宽度用）
    public static Vector2 MeasureString(string s, int scale = 1)
    {
        float x = 0, max = 0; int lines = 1;
        foreach (char c in s ?? "")
        {
            if (c == '\n') { max = Mathf.Max(max, x); x = 0; lines++; continue; }
            if (GlyphAtlas.TryGet(c, out var g)) x += AdvanceOf(c, g) * scale;
        }
        return new Vector2(Mathf.Max(max, x), lines * CellH * scale);
    }

    public override Texture mainTexture => GlyphAtlas.Texture;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var tex = GlyphAtlas.Texture;
        if (tex == null) return;
        float tw = tex.width, th = tex.height;
        var r = rectTransform.rect;

        // 1) 先量出整块尺寸：整行宽度用于水平对齐，总高用于垂直居中
        var size = Measure();
        float lineW = size.x, blockH = size.y;
        float rowH = CellH * Scale;

        // 2) 水平起点：rect 宽度异常（0 或负数）时退化为 0 偏移
        float ox = 0f;
        if (r.width > 1f && lineW > 0f)
        {
            if (Align == TextAlign.Center) ox = (r.width - lineW) * 0.5f;
            else if (Align == TextAlign.Right) ox = r.width - lineW;
            if (ox < 0f) ox = 0f;
        }

        // 3) 垂直起点：rect 比内容高时整体垂直居中，否则贴顶
        float oy = 0f;
        if (r.height > 1f && blockH > 0f && r.height > blockH) oy = (r.height - blockH) * 0.5f;

        float x0 = r.xMin + ox, y0 = r.yMax - oy;
        float x = x0, y = y0;
        foreach (char c in text)
        {
            if (c == '\n') { x = x0; y -= rowH; continue; }
            if (!GlyphAtlas.TryGet(c, out var g)) continue;
            // 字形只占格子左侧 ad 像素：几何宽度与 UV 宽度都用 ad，否则 ASCII 会被水平压扁
            float ad = AdvanceOf(c, g);
            float w = ad * Scale;
            if (Wrap && WrapWidth > 0 && x + w > x0 + WrapWidth && x > x0) { x = x0; y -= rowH; }
            float u0 = g.X * (float)CellW / tw, u1 = (g.X * (float)CellW + ad) / tw;
            float v0 = 1f - g.Y * (float)CellH / th, v1 = 1f - (g.Y * (float)CellH + CellH) / th;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), color, new Vector2(u0, v0));
            vh.AddVert(new Vector3(x + w, y), color, new Vector2(u1, v0));
            vh.AddVert(new Vector3(x + w, y - rowH), color, new Vector2(u1, v1));
            vh.AddVert(new Vector3(x, y - rowH), color, new Vector2(u0, v1));
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
            x += w;
        }
    }
}
