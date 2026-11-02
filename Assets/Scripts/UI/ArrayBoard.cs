using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// 数组列车：车头拉着一串敞篷车厢，一节车厢一格元素，值写在车厢的货面上、下标写在车厢下面的站牌上。
// 整列停在可拖可缩的悬浮看板里；写到哪一节哪一节抬起来亮一下，车厢按内宽自动换行接龙。
public sealed class ArrayBoard
{
    public const float LocoW = 48f, CarW = 32f, CarH = 32f, CouplerW = 8f;
    public const float CarPitch = CarW + CouplerW;
    const float TagH = 14f, RowGap = 8f;
    const float RowH = CarH + TagH + RowGap;
    const int MaxCells = 60;

    public readonly FloatPanel Panel;
    readonly RectTransform body;
    readonly List<Car> cars = new List<Car>();
    readonly List<Image> rails = new List<Image>();
    readonly List<Image> couplers = new List<Image>();
    Image loco;
    PixelText moreTag;
    string lastSig, cachedName;
    List<object> cachedItems;
    int cachedHi = -1;
    bool reflow;

    sealed class Car
    {
        public RectTransform Rt;
        public Image Art, Tag, Glow;
        public PixelText Val, Idx;
    }

    public ArrayBoard(Transform parent, string name, System.Func<Vector2, Vector2> screenToLocal)
    {
        Panel = FloatPanel.Create(parent, "ArrayBoard_" + name,
            Theme.A(Theme.WhaleDeep, .85f), Theme.A(Theme.WhaleDeep, .92f));
        Panel.ScreenToLocal = screenToLocal;
        Panel.Root.sizeDelta = new Vector2(264f, 84f);
        Panel.OnResized = OnPanelResized;
        body = Panel.Body;
    }

    // 拉宽拉窄之后车厢要立刻换行重排，不等下一步数据
    void OnPanelResized()
    {
        lastSig = null;
        if (reflow || cachedItems == null) return;
        reflow = true;
        try { Render(cachedName, cachedItems, cachedHi); }
        finally { reflow = false; }
    }

    public void Render(string name, List<object> items, int hiIndex)
    {
        var sig = Signature(name, items, hiIndex);
        if (sig == lastSig) return;
        lastSig = sig;
        cachedName = name; cachedItems = items; cachedHi = hiIndex;

        int n = items != null ? items.Count : 0;
        Panel.SetTitle("数组 " + name + " · " + n + " 格");

        int rows = RowsFor(body.rect.width, n);
        int shown = Mathf.Min(n, MaxCells);

        // 高度按行数贴合；玩家自己拉过高度之后就不再自动缩，只在排不下时加高
        float need = HeightFor(rows);
        float want = Panel.ResizedByUser ? Mathf.Max(need, Panel.Size.y) : need;
        want = Mathf.Clamp(want, Panel.MinSize.y, Panel.MaxSize.y);
        if (Mathf.Abs(Panel.Size.y - want) > .5f) Panel.Root.sizeDelta = new Vector2(Panel.Size.x, want);

        EnsureLoco();
        loco.gameObject.SetActive(true);
        loco.rectTransform.anchoredPosition = Vector2.zero;

        // 每行先铺一条铁轨，车厢压在上面
        for (int r = 0; r < rows; r++)
        {
            float y = r * RowH + CarH - 7f;
            for (float x = 0f; x < body.rect.width; x += 16f)
                Tile(rails, "array_rail", 16f, 8f).rectTransform.anchoredPosition = new Vector2(x, -y);
        }

        for (int i = 0; i < shown; i++)
        {
            int row; float x, y;
            SlotAt(body.rect.width, i, out row, out x, out y);
            if (i > 0)
            {
                float px, py; int prow;
                SlotAt(body.rect.width, i - 1, out prow, out px, out py);
                Tile(couplers, "array_coupler", 16f, 8f).rectTransform.anchoredPosition = new Vector2(px + CarW - 4f, -(y + 20f));
            }
            var c = Acquire(i);
            bool hi = i == hiIndex;
            c.Rt.anchoredPosition = new Vector2(x, -(y + (hi ? 4f : 0f)));
            c.Tag.color = hi ? new Color(1f, .93f, .55f) : Theme.A(Theme.Cream, .92f);
            c.Glow.color = hi ? Theme.A(Theme.Coral, .9f) : Theme.A(Theme.Coral, 0f);
            c.Val.Text = Dayu.Core.ArrayView.ElementText(items[i]);
            c.Val.color = hi ? Theme.Coral : Theme.Ink;
            c.Idx.Text = i.ToString();
            c.Idx.color = hi ? Theme.Coral : Theme.A(Theme.WhaleDeep, .8f);
        }
        for (int i = shown; i < cars.Count; i++) cars[i].Rt.gameObject.SetActive(false);
        HideFrom(couplers, Mathf.Max(0, shown - 1));
        HideFrom(rails, rows * Mathf.CeilToInt(body.rect.width / 16f));

        if (moreTag != null) moreTag.gameObject.SetActive(false);
        if (n > shown)
        {
            if (moreTag == null)
            {
                moreTag = PixelText.Create(body, "", Theme.Border, 1);
                moreTag.raycastTarget = false;
                var mrt = moreTag.rectTransform;
                mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 1f);
                mrt.pivot = new Vector2(0f, 1f);
            }
            int row; float x, y;
            SlotAt(body.rect.width, shown, out row, out x, out y);
            moreTag.rectTransform.anchoredPosition = new Vector2(x, -(y + 12f));
            moreTag.Text = "…" + (n - shown);
            moreTag.gameObject.SetActive(true);
        }
    }

    void HideFrom(List<Image> pool, int keep)
    {
        for (int i = keep; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        for (int i = 0; i < Mathf.Min(keep, pool.Count); i++) pool[i].gameObject.SetActive(true);
    }

    Image Tile(List<Image> pool, string prop, float w, float h)
    {
        for (int i = 0; i < pool.Count; i++)
            if (!pool[i].gameObject.activeSelf) { pool[i].gameObject.SetActive(true); return pool[i]; }
        var go = new GameObject(prop, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(body, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        var img = go.GetComponent<Image>();
        var sheet = PropAssets.Get(prop);
        if (sheet != null && sheet.FrameCount > 0) img.sprite = sheet[0];
        img.raycastTarget = false;
        pool.Add(img);
        return img;
    }

    void EnsureLoco()
    {
        if (loco != null) return;
        loco = Tile(new List<Image>(), "array_loco", LocoW, CarH);
    }

    // 每行能挂几节车厢：第一行要让开车头
    public static int SlotsPerRow(float innerW, bool firstRow)
    {
        float head = firstRow ? LocoW + CouplerW : 0f;
        int n = Mathf.FloorToInt((innerW - head + CouplerW) / CarPitch);
        return Mathf.Max(1, n);
    }

    public static int RowsFor(float innerW, int count)
    {
        int shown = Mathf.Clamp(count, 1, MaxCells);
        int first = SlotsPerRow(innerW, true);
        if (shown <= first) return 1;
        int rest = SlotsPerRow(innerW, false);
        return 1 + (shown - first + rest - 1) / rest;
    }

    // 第 i 节车厢停在哪：第一行让开车头，排满换行接龙
    public static void SlotAt(float innerW, int i, out int row, out float x, out float y)
    {
        int first = SlotsPerRow(innerW, true);
        if (i < first) { row = 0; x = LocoW + CouplerW + i * CarPitch; }
        else
        {
            int rest = SlotsPerRow(innerW, false);
            int k = i - first;
            row = 1 + k / rest;
            x = (k % rest) * CarPitch;
        }
        y = row * RowH;
    }

    public static float HeightFor(int rows)
    {
        return FloatPanel.HeadH + 12f + rows * RowH - RowGap + 6f;
    }

    Car Acquire(int i)
    {
        if (i < cars.Count) { cars[i].Rt.gameObject.SetActive(true); return cars[i]; }
        var go = new GameObject("Car", typeof(RectTransform));
        go.transform.SetParent(body, false);
        var c = new Car { Rt = (RectTransform)go.transform };
        c.Rt.anchorMin = c.Rt.anchorMax = new Vector2(0f, 1f);
        c.Rt.pivot = new Vector2(0f, 1f);
        c.Rt.sizeDelta = new Vector2(CarW, CarH + TagH);

        var sheet = PropAssets.Get("array_car");
        var artGo = new GameObject("Art", typeof(RectTransform), typeof(Image));
        artGo.transform.SetParent(c.Rt, false);
        c.Art = artGo.GetComponent<Image>();
        if (sheet != null && sheet.FrameCount > 0) c.Art.sprite = sheet[0];
        c.Art.raycastTarget = false;
        var art = (RectTransform)artGo.transform;
        art.anchorMin = art.anchorMax = new Vector2(0f, 1f);
        art.pivot = new Vector2(0f, 1f);
        art.anchoredPosition = Vector2.zero;
        art.sizeDelta = new Vector2(CarW, CarH);

        // 货面就是这格元素的值：字比货面宽时 PixelText 的居中会把文字对称地溢出到两边，不裁字
        c.Val = PixelText.Create(c.Rt, "", Theme.Ink, 1);
        c.Val.raycastTarget = false;
        c.Val.Align = PixelText.TextAlign.Center;
        var vrt = c.Val.rectTransform;
        vrt.anchorMin = vrt.anchorMax = new Vector2(0f, 1f);
        vrt.pivot = new Vector2(0f, 1f);
        vrt.anchoredPosition = new Vector2(4f, -9f);
        vrt.sizeDelta = new Vector2(CarW - 8f, 14f);

        var tagGo = new GameObject("Tag", typeof(RectTransform), typeof(Image));
        tagGo.transform.SetParent(c.Rt, false);
        c.Tag = tagGo.GetComponent<Image>();
        c.Tag.sprite = GraphView.White;
        c.Tag.color = Theme.A(Theme.Cream, .92f);
        c.Tag.raycastTarget = false;
        var trt = (RectTransform)tagGo.transform;
        trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
        trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = new Vector2(0f, -CarH);
        trt.sizeDelta = new Vector2(CarW, TagH);

        c.Idx = PixelText.Create(trt, "", Theme.A(Theme.WhaleDeep, .8f), 1);
        c.Idx.raycastTarget = false;
        c.Idx.Align = PixelText.TextAlign.Center;
        var irt = c.Idx.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(0f, 1f);
        irt.pivot = new Vector2(0f, 1f);
        irt.anchoredPosition = Vector2.zero;
        irt.sizeDelta = new Vector2(CarW, TagH);

        var glowGo = new GameObject("Glow", typeof(RectTransform), typeof(Image));
        glowGo.transform.SetParent(c.Rt, false);
        c.Glow = glowGo.GetComponent<Image>();
        c.Glow.sprite = GraphView.White;
        c.Glow.color = Theme.A(Theme.Coral, 0f);
        c.Glow.raycastTarget = false;
        var grt = (RectTransform)glowGo.transform;
        grt.anchorMin = grt.anchorMax = new Vector2(0f, 1f);
        grt.pivot = new Vector2(0f, 1f);
        grt.anchoredPosition = new Vector2(0f, -(CarH + TagH));
        grt.sizeDelta = new Vector2(CarW, 2f);

        cars.Add(c);
        return c;
    }

    static string Signature(string name, List<object> items, int hiIndex)
    {
        var sb = new StringBuilder();
        sb.Append(name).Append('=').Append(items != null ? items.Count : 0).Append(':');
        if (items != null)
            for (int j = 0; j < items.Count; j++) sb.Append(Dayu.Core.ArrayView.ElementText(items[j])).Append(',');
        sb.Append('#').Append(hiIndex);
        return sb.ToString();
    }
}
