using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十六轮：PanelGraphic 必须认 Graphic.color（uGUI 的通用染色口）。
    //
    // 这条测试是真机截图里挖出来的坑：ScrollBar 的槽与 thumb 只改 Graphic.color（底色固定纯白，
    // 颜色与透明度全走 color，hover 只刷一次 color），PixelButton 的禁用灰化同理。而旧
    // OnPopulateMesh 直接用 Fill/Edge 发顶点、把 color 丢了，结果滚动条画成一条纯白、
    // 禁用按钮也不发灰——截图上量出来是 x=1590..1593 四列 (255,255,255)，和纸底 (255,253,247) 只差一点。
    //
    // 判据全走同一套 Color→Color32 转换（和实现同一条浮点路径），所以是逐字节相等，不留容差。
    public static class PanelTintTests
    {
        const string Tag = "面板染色";

        public static void Run(ref int total, ref int passed)
        {
            DefaultIsUntinted(ref total, ref passed);
            TintMultipliesEveryBand(ref total, ref passed);
            ScrollBarColors(ref total, ref passed);
            DisabledButtonGray(ref total, ref passed);
        }

        // 探针对象不进场景存档，用完立刻销毁
        static PanelGraphic Make()
        {
            var go = new GameObject("PanelTintProbe", typeof(RectTransform));
            go.hideFlags = HideFlags.DontSave;
            var g = go.AddComponent<PanelGraphic>();
            g.rectTransform.sizeDelta = new Vector2(40f, 20f);
            return g;
        }

        static void Burn(PanelGraphic g)
        {
            if (g == null) return;
            Object.DestroyImmediate(g.gameObject);
        }

        // 和实现同一条换算：顶点色 = 本色 × Graphic.color
        static Color32 Want(Color32 from, Color tint)
        {
            Color c = from;
            c *= tint;
            return c;
        }

        static void Expect(ref int total, ref int passed, string label, Color32 actual, Color32 want)
        {
            bool ok = actual.r == want.r && actual.g == want.g && actual.b == want.b && actual.a == want.a;
            Expect(ref total, ref passed, label, ok,
                "实际=" + actual.r + "," + actual.g + "," + actual.b + "," + actual.a +
                " 期望=" + want.r + "," + want.g + "," + want.b + "," + want.a);
        }

        static void Expect(ref int total, ref int passed, string label, bool actual, string detail)
        {
            total++;
            if (actual) { passed++; Debug.Log("✔ " + Tag + " —— " + label); }
            else Debug.Log("✘ " + Tag + " —— " + label + " · " + detail);
        }

        // ---------------- 默认（color 纯白）逐字节不变 ----------------
        //
        // 顶点序：投影(开时 8 个) → 填充(8 个) → 描边带(开 Stroke 时每条边 4 个，双层就是两串 32)
        // → 旧 Thickness 环(Thickness>0 时 8 个，默认就开着)。
        // 所以不开 Stroke 时填充在 0、Thickness 环在 8；开了 Stroke 外圈在 8、内圈在 40；开了投影整体再往后挪 8。

        static void DefaultIsUntinted(ref int total, ref int passed)
        {
            var g = Make();
            g.Fill = Theme.Paper;
            Expect(ref total, ref passed, "白底时填充顶点＝Fill 本色", g.VertexColorAtForTest(0), Theme.Paper);
            g.Stroke = 2; g.DoubleEdge = true; g.OuterEdge = Theme.Ink; g.Edge = Theme.InkSoft;
            Expect(ref total, ref passed, "白底时描边外圈＝OuterEdge", g.VertexColorAtForTest(8), Theme.Ink);
            Expect(ref total, ref passed, "白底时描边内圈＝Edge", g.VertexColorAtForTest(40), Theme.InkSoft);
            Burn(g);

            var h = Make();
            h.Fill = Theme.Paper;
            h.Stroke = 2; h.DoubleEdge = true; h.OuterEdge = Theme.Ink; h.Edge = Theme.InkSoft;
            h.DropShadow = 2; h.ShadowColor = Theme.A(Theme.Ink, .18f);
            Expect(ref total, ref passed, "白底时投影＝ShadowColor", h.VertexColorAtForTest(0), Theme.A(Theme.Ink, .18f));
            Expect(ref total, ref passed, "开投影后填充顶点仍＝Fill 本色", h.VertexColorAtForTest(8), Theme.Paper);
            Expect(ref total, ref passed, "开投影后描边外圈＝OuterEdge", h.VertexColorAtForTest(16), Theme.Ink);
            Expect(ref total, ref passed, "开投影后描边内圈＝Edge", h.VertexColorAtForTest(48), Theme.InkSoft);
            Burn(h);
        }

        // ---------------- 染色要盖住每一条带 ----------------

        static void TintMultipliesEveryBand(ref int total, ref int passed)
        {
            var g = Make();
            g.Fill = Theme.Paper;
            g.Stroke = 2; g.DoubleEdge = true; g.OuterEdge = Theme.Ink; g.Edge = Theme.InkSoft;
            g.DropShadow = 2; g.ShadowColor = Theme.A(Theme.Ink, .18f);
            var tint = new Color(.55f, .55f, .55f, .7f);
            g.color = tint;
            Expect(ref total, ref passed, "染色后投影被压暗", g.VertexColorAtForTest(0), Want(Theme.A(Theme.Ink, .18f), tint));
            Expect(ref total, ref passed, "染色后填充顶点被压暗", g.VertexColorAtForTest(8), Want(Theme.Paper, tint));
            Expect(ref total, ref passed, "染色后描边外圈被压暗", g.VertexColorAtForTest(16), Want(Theme.Ink, tint));
            Expect(ref total, ref passed, "染色后描边内圈被压暗", g.VertexColorAtForTest(48), Want(Theme.InkSoft, tint));
            Burn(g);
        }

        // ---------------- 滚动条：槽与 thumb 都是「白底 × color」 ----------------

        static void ScrollBarColors(ref int total, ref int passed)
        {
            var g = Make();
            g.Fill = Theme.White;                 // ScrollBar.MakeGraphic 固定底色
            var thumb = Theme.A(Theme.WhaleDeep, .42f);
            g.color = thumb;
            var got = g.VertexColorAtForTest(0);
            Expect(ref total, ref passed, "thumb＝白底×类别深青", got, Want(Theme.White, thumb));
            Expect(ref total, ref passed, "thumb 不再是纯白（本轮那个看不见的 bug）",
                got.r < 255 && got.g < 255 && got.b < 255, "实际=" + got.r + "," + got.g + "," + got.b + "," + got.a);
            var track = Theme.A(Theme.Ink, .10f);
            g.color = track;
            var t = g.VertexColorAtForTest(0);
            Expect(ref total, ref passed, "槽＝白底×Ink 一成", t, Want(Theme.White, track));
            Expect(ref total, ref passed, "槽比纸底暗一点（能读出轨道）",
                t.r < Theme.Paper.r && t.g < Theme.Paper.g && t.b < Theme.Paper.b,
                "实际=" + t.r + "," + t.g + "," + t.b);
            Burn(g);
        }

        // ---------------- 禁用按钮要真的发灰 ----------------

        static void DisabledButtonGray(ref int total, ref int passed)
        {
            var g = Make();
            g.Fill = Theme.Salt;
            var gray = new Color(.55f, .55f, .55f, .7f);
            g.color = gray;
            var got = g.VertexColorAtForTest(0);
            Expect(ref total, ref passed, "禁用底色＝Salt×灰", got, Want(Theme.Salt, gray));
            Expect(ref total, ref passed, "禁用后确实比常态暗", got.r < Theme.Salt.r && got.b < Theme.Salt.b,
                "实际=" + got.r + "," + got.g + "," + got.b + "," + got.a + " 常态=" + Theme.Salt.r + "," + Theme.Salt.g + "," + Theme.Salt.b);
            Burn(g);
        }
    }
}
