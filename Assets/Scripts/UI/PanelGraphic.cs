using UnityEngine;
using UnityEngine.UI;

// 全项目通用的一格面板图：切角八边形 + 可选描边/高光/投影。
//
// 旧代码里 Thickness 那一环的内外两组顶点重合在同一个八边形上，三角形面积恒为 0，
// 也就是说「1px 描边」其实一直没显形。为了不改动 App / GraphView 等调用点的画面，
// 那圈顶点照旧保留（Thickness 仍然生效、仍然不显形）；
// 真正看得见的描边走新字段 Stroke，默认 0 = 关，所以默认画面逐像素不变。
public sealed class PanelGraphic : MaskableGraphic
{
    public Color Fill = Theme.Paper, Edge = Theme.Border, Highlight = Theme.White;
    public int Notch = 3, Thickness = 1;

    // —— ③b 新增的质感开关，默认值全部等于「关」——
    public int Stroke = 0;                 // >0：轮廓内侧画 Stroke px 的真实描边带
    public bool DoubleEdge = false;        // 描边带最外 1px 换成 OuterEdge，与内层拉开成双层
    public Color32 OuterEdge = Theme.Ink;  // 双层描边的外圈（默认 Ink；内圈给 InkSoft 才读得出两层）
    public bool Bevel = false;             // 描边带顶边用 Highlight 提亮、下沿用 Shade 压暗
    public Color32 Shade = Theme.Salt;     // 下沿内暗边色（UiKit 传 Theme.ShadeSoft）
    public int DropShadow = 0;             // >0：底部投影，形状整体下移这么多像素，根节点尺寸不动
    public Color ShadowColor = Theme.A(Theme.Ink, .18f);   // 投影色＝Ink 叠透明度，不引新色

    const float Sqrt2 = 1.41421356237f;
    // 网格重建只在脏了的时候跑，但仍然复用同一个数组，避免每次 Clear/SetDirty 都产生垃圾
    readonly Vector2[] po = new Vector2[8], pi = new Vector2[8], pb = new Vector2[8], ps = new Vector2[8];

    // 切角八边形按「四边等距内缩 d」的算法取点：d=0 时与旧代码那 8 个点完全重合。
    // 斜切角的内缩量和直边不一样（45° 边的法向偏移要除以 sin45），所以轴向端点是 n + d*(√2-1)。
    void Octo(Vector2[] o, float w, float h, float n, float d)
    {
        float k = n + d * (Sqrt2 - 1f);
        o[0] = new Vector2(k, d);
        o[1] = new Vector2(w - k, d);
        o[2] = new Vector2(w - d, k);
        o[3] = new Vector2(w - d, h - k);
        o[4] = new Vector2(w - k, h - d);
        o[5] = new Vector2(k, h - d);
        o[6] = new Vector2(d, h - k);
        o[7] = new Vector2(d, k);
    }

    // 顶点序 0→1 底边、2→3 右边、4→5 顶边、6→7 左边，1/3/5/7 是四个斜切角
    static bool TopEdge(int e) { return e == 3 || e == 4 || e == 5; }
    static bool BottomEdge(int e) { return e == 0 || e == 1 || e == 7; }

    void AddBand(VertexHelper vh, Vector2[] a, Vector2[] b, Color32 baseColor, bool bevel)
    {
        for (int i = 0; i < 8; i++)
        {
            int j = (i + 1) % 8;
            Color c = baseColor;
            if (bevel) c = TopEdge(i) ? Highlight : (BottomEdge(i) ? (Color)Shade : (Color)Edge);
            int v = vh.currentVertCount;
            // 每条边单独发 4 个顶点，相邻两边才有硬转折（渐变会糊成一片脏边）
            vh.AddVert(a[i], c, Vector2.zero);
            vh.AddVert(a[j], c, Vector2.zero);
            vh.AddVert(b[j], c, Vector2.zero);
            vh.AddVert(b[i], c, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v, v + 2, v + 3);
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        BuildShape(vh);
        ApplyTint(vh);
    }

    // Graphic.color 是 uGUI 的通用染色入口：ScrollBar 的槽/thumb 和 PixelButton 的禁用灰化都只改它，
    // 不改 Fill/Edge。以前顶点色直接用 Fill/Edge、把 color 丢了，所以那两处全是白底、灰化也看不出。
    // 默认 color 就是纯白，逐顶点乘一遍等于没乘，默认画面仍然逐像素不变（比较一次就跳过整趟循环）。
    void ApplyTint(VertexHelper vh)
    {
        Color t = color;
        if (t.r == 1f && t.g == 1f && t.b == 1f && t.a == 1f) return;
        UIVertex v = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            Color c = v.color;
            c *= t;
            v.color = c;
            vh.SetUIVertex(v, i);
        }
    }

    // 自测钩子：按当前字段 + color 现生成一次网格，取第 idx 个顶点的颜色。
    // 顶点序固定：投影(开时 8 个) → 填充 8 个 → 描边带(开 Stroke 时每条边 4 个，双层两串) → 旧 Thickness 环(8 个)。
    // 不开投影时填充的第一个顶点就是 idx=0；开投影后填充挪到 8，描边带再往后。
    public Color32 VertexColorAtForTest(int idx)
    {
        var vh = new VertexHelper();
        OnPopulateMesh(vh);
        if (idx < 0 || idx >= vh.currentVertCount) return new Color32(0, 0, 0, 0);
        UIVertex v = default;
        vh.PopulateUIVertex(ref v, idx);
        return v.color;
    }

    void BuildShape(VertexHelper vh)
    {
        var r = rectTransform.rect;
        float w = r.width, h = r.height, n = Notch;
        Octo(po, w, h, n, 0f);

        // 投影先画（同一网格更早的三角形后绘制时才会被本体压住），形状只往下挪，不往外扩
        if (DropShadow > 0)
        {
            int s0 = vh.currentVertCount;
            for (int i = 0; i < 8; i++) { ps[i] = po[i]; ps[i].y -= DropShadow; }
            for (int i = 0; i < 8; i++) vh.AddVert(ps[i], ShadowColor, Vector2.zero);
            for (int i = 1; i < 7; i++) vh.AddTriangle(s0, s0 + i, s0 + i + 1);
        }

        int f0 = vh.currentVertCount;
        for (int i = 0; i < 8; i++) vh.AddVert(po[i], Fill, Vector2.zero);
        for (int i = 1; i < 7; i++) vh.AddTriangle(f0, f0 + i, f0 + i + 1);

        if (Stroke > 0)
        {
            Octo(pi, w, h, n, Stroke);                    // 描边带的最内边界
            if (DoubleEdge && Stroke >= 2)
            {
                Octo(pb, w, h, n, 1f);                   // 外圈只占 1px，剩下的都归内层
                AddBand(vh, po, pb, OuterEdge, false);   // 外圈纯色：高光压在它里面才像「光打在边沿」
                AddBand(vh, pb, pi, Edge, Bevel);
            }
            else AddBand(vh, po, pi, Edge, Bevel);
        }

        if (Thickness > 0)
        {
            int b = vh.currentVertCount;
            for (int i = 0; i < 8; i++) vh.AddVert(po[i], Edge, Vector2.zero);
            for (int i = 0; i < 8; i++)
            {
                int j = (i + 1) % 8;
                vh.AddTriangle(b + i, b + j, f0 + i);
                vh.AddTriangle(b + j, f0 + j, f0 + i);
            }
        }
    }
}
