using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// 队列看板：虚线悬浮块里从左往右排的一队小车厢，最左是队头、最右是队尾。
// 入队 → 新车厢从右边滑进队尾闪一下；出队 → 队头那只往左飘走淡出。
// 位置和大小玩家自己拖，长度跟车厢数走，宽度不够时装不下的车厢只画前 MaxCells 节。
public sealed class QueueBoard
{
    const float CellW = 34f, CellH = 26f, Gap = 4f, TagH = 10f;
    const int MaxCells = 14;

    public readonly FloatPanel Panel;
    readonly RectTransform body;
    readonly string dispName;
    readonly List<Cell> cells = new List<Cell>();
    readonly List<Fx> fx = new List<Fx>();
    int lastCount = -1;
    string lastSig;
    List<object> cachedItems;

    sealed class Cell
    {
        public RectTransform Rt;
        public Image Bg, Shade;
        public PixelText Val, Tag;
        public float Phase;
    }

    sealed class Fx
    {
        public RectTransform Rt;
        public Image Img;
        public Vector2 From, To;
        public float T, Dur;
        public bool Leaving;
        public Color Base;
    }

    public QueueBoard(Transform parent, string structId, string name, System.Func<Vector2, Vector2> screenToLocal)
    {
        dispName = string.IsNullOrEmpty(name) ? structId : name;
        Panel = FloatPanel.Create(parent, "QueueBoard_" + structId,
            Theme.A(Theme.WhaleDeep, .8f), Theme.A(Theme.WhaleDeep, .9f));
        Panel.ScreenToLocal = screenToLocal;
        Panel.Root.sizeDelta = new Vector2(WidthFor(3), 96f);
        Panel.OnResized = OnPanelResized;
        body = Panel.Body;
        Panel.SetTitle("队列 " + dispName);
    }

    void OnPanelResized() { lastSig = null; Render(cachedItems, null); }

    // action 是这一帧的事件类型：push（入队）/ pop（出队），其余按「数据变了但没动作」处理
    public void Render(List<object> items, string action)
    {
        var prev = cachedItems;
        cachedItems = items;
        var sig = Signature(items);
        if (sig == lastSig) return;
        lastSig = sig;

        int n = items != null ? items.Count : 0;
        Panel.SetTitle("队列 " + dispName + " · " + n + " 个");

        int shown = VisibleCount(n);
        float wantW = Panel.ResizedByUser
            ? Mathf.Max(WidthFor(shown > 0 ? shown : 1), Panel.Size.x)
            : WidthFor(shown > 0 ? shown : 1);
        wantW = Mathf.Clamp(wantW, Panel.MinSize.x, Panel.MaxSize.x);
        if (Mathf.Abs(Panel.Size.x - wantW) > .5f)
            Panel.Root.sizeDelta = new Vector2(wantW, Panel.Size.y);

        // 车厢从内框左边贴起，间距固定；面板被拉宽时车厢仍靠左排，不跟着拉伸变形

        for (int i = 0; i < shown; i++)
        {
            var c = Acquire(i);
            bool head = i == 0, tail = i == shown - 1;
            c.Rt.anchoredPosition = new Vector2(SlotX(i), 0f);
            c.Bg.color = head ? new Color(.62f, .92f, .78f) : tail ? new Color(1f, .93f, .55f) : Theme.A(Theme.MintLight, .8f);
            c.Shade.color = head || tail ? Theme.A(Theme.WhaleDeep, .55f) : Theme.A(Theme.WhaleDeep, .28f);
            c.Val.Text = Dayu.Core.ArrayView.ElementText(items[i]);
            c.Val.color = Theme.Ink;
            c.Tag.Text = head ? "头" : tail ? "尾" : "";
            c.Tag.color = head ? Theme.Mint : Theme.HoneyDeep;
        }
        for (int i = shown; i < cells.Count; i++) cells[i].Rt.gameObject.SetActive(false);

        if (lastCount >= 0 && n != lastCount)
        {
            if (n > lastCount && shown > 0) SpawnIn(cells[shown - 1]);
            else SpawnOut(prev, lastCount);
        }
        lastCount = n;
    }

    void SpawnIn(Cell c)
    {
        var to = c.Rt.anchoredPosition;
        fx.Add(new Fx { Rt = c.Rt, From = to + new Vector2(30f, 0f), To = to, Dur = .22f, Img = c.Bg, Base = c.Bg.color });
        c.Bg.color = Theme.A(Theme.Coral, .9f);
    }

    // 出队那只已经不在新列表里，从上一帧快照取值，借一节游离车厢往左飘走淡出
    void SpawnOut(List<object> prev, int prevCount)
    {
        if (prev == null || prevCount <= 0 || prev.Count == 0) return;
        var go = new GameObject("Dequeue", typeof(RectTransform));
        go.transform.SetParent(body, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(CellW, CellH + TagH);
        rt.anchoredPosition = new Vector2(SlotX(0), 0f);
        var bg = GraphView.Tile(rt, "Bg", Theme.A(Theme.Coral, .9f), false);
        TopLeft(bg.rectTransform, Vector2.zero, new Vector2(CellW, CellH));
        var val = PixelText.Create(rt, Dayu.Core.ArrayView.ElementText(prev[0]), Theme.Ink, 1);
        val.raycastTarget = false;
        val.Align = PixelText.TextAlign.Center;
        TopLeft(val.rectTransform, Vector2.zero, new Vector2(CellW, CellH));
        var col = bg.color;
        fx.Add(new Fx { Rt = rt, Img = bg, From = rt.anchoredPosition, To = rt.anchoredPosition + new Vector2(-34f, 0f),
            Dur = .3f, Leaving = true, Base = col });
    }

    // 每帧由画布叫一次：推进进出动画，顺带给队尾那节一点呼吸（等下一件入队）
    public void Tick(float dt)
    {
        int shown = VisibleCells();
        if (shown > 0)
        {
            var tail = cells[shown - 1];
            if (tail != null && tail.Rt != null && !IsTweening(tail.Rt))
            {
                tail.Phase += dt * 2.2f;
                float k = .5f + .5f * Mathf.Sin(tail.Phase);
                tail.Rt.localScale = Vector3.one * (1f + .03f * k);
            }
        }
        for (int i = fx.Count - 1; i >= 0; i--)
        {
            var f = fx[i];
            f.T += dt;
            float k = Mathf.Clamp01(f.T / f.Dur);
            float e = 1f - (1f - k) * (1f - k);
            f.Rt.anchoredPosition = Vector2.Lerp(f.From, f.To, e);
            if (f.Img != null)
            {
                if (f.Leaving) { var c = f.Base; c.a = f.Base.a * (1f - k); f.Img.color = c; }
                else f.Img.color = Color.Lerp(f.Base, new Color(1f, .93f, .55f), k);
            }
            if (k >= 1f)
            {
                if (f.Leaving && f.Rt != null) UnityEngine.Object.Destroy(f.Rt.gameObject);
                fx.RemoveAt(i);
            }
        }
    }

    int VisibleCells()
    {
        int n = 0;
        for (int i = 0; i < cells.Count; i++) if (cells[i].Rt != null && cells[i].Rt.gameObject.activeSelf) n++;
        return n;
    }

    bool IsTweening(RectTransform rt)
    {
        for (int i = 0; i < fx.Count; i++) if (fx[i].Rt == rt) return true;
        return false;
    }

    // 第 index 节车厢在内框里的水平位置：0 号贴左（队头在左，往右排队尾）
    public static float SlotX(int index)
    {
        return 2f + index * (CellW + Gap);
    }

    public static int VisibleCount(int count)
    {
        return Mathf.Min(Mathf.Max(count, 0), MaxCells);
    }

    // 车厢数决定看板该多宽：每节 34 + 间距 4，再加内框左右留白，
    // 最窄不能低于 FloatPanel 的 MinSize.x=120，否则标题条会被裁掉（纯函数，自测直接盯这个数）
    public static float WidthFor(int count)
    {
        return Mathf.Max(120f, 14f + Mathf.Max(1, count) * (CellW + Gap));
    }

    Cell Acquire(int i)
    {
        if (i < cells.Count) { cells[i].Rt.gameObject.SetActive(true); return cells[i]; }
        var go = new GameObject("Car", typeof(RectTransform));
        go.transform.SetParent(body, false);
        var c = new Cell { Rt = (RectTransform)go.transform };
        c.Rt.anchorMin = c.Rt.anchorMax = new Vector2(0f, 1f);
        c.Rt.pivot = new Vector2(0f, 1f);
        c.Rt.sizeDelta = new Vector2(CellW, CellH + TagH);
        c.Shade = GraphView.Tile(c.Rt, "Shade", Theme.A(Theme.WhaleDeep, .28f), false);
        TopLeft(c.Shade.rectTransform, new Vector2(2f, -2f), new Vector2(CellW, CellH));
        c.Bg = GraphView.Tile(c.Rt, "Bg", Theme.A(Theme.MintLight, .8f), false);
        TopLeft(c.Bg.rectTransform, Vector2.zero, new Vector2(CellW, CellH));
        c.Val = PixelText.Create(c.Rt, "", Theme.Ink, 1);
        c.Val.raycastTarget = false;
        c.Val.Align = PixelText.TextAlign.Center;
        TopLeft(c.Val.rectTransform, Vector2.zero, new Vector2(CellW, CellH));
        c.Tag = PixelText.Create(c.Rt, "", Theme.Mint, 1);
        c.Tag.raycastTarget = false;
        c.Tag.Align = PixelText.TextAlign.Center;
        TopLeft(c.Tag.rectTransform, new Vector2(0f, CellH), new Vector2(CellW, TagH));
        cells.Add(c);
        return c;
    }

    // Tile 出来的 RectTransform 锚点不是左上，每节车厢的子件都要显式钉好
    static void TopLeft(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static string Signature(List<object> items)
    {
        var sb = new StringBuilder();
        sb.Append(items != null ? items.Count : 0).Append(':');
        if (items != null)
            for (int j = 0; j < items.Count; j++) sb.Append(Dayu.Core.ArrayView.ElementText(items[j])).Append(',');
        return sb.ToString();
    }
}
