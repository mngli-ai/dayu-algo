using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// 栈看板：虚线悬浮块里从下往上摞的一只只盘子，最上面那只是栈顶。
// 压入 → 新盘子从上方落进栈顶闪一下；弹出 → 顶上那只往上飘走淡出。
// 大小位置玩家自己拖，宽度跟最长的值走，高度跟盘子数走。
public sealed class StackBoard
{
    const float CellW = 40f, CellH = 22f, Gap = 4f, TagW = 16f;
    const int MaxCells = 40;

    public readonly FloatPanel Panel;
    readonly RectTransform body;
    readonly string id, dispName;
    readonly List<Cell> cells = new List<Cell>();
    readonly List<Fx> fx = new List<Fx>();
    int lastCount = -1;
    string lastSig;
    List<object> cachedItems;
    float bob;
    float lastInnerH;

    sealed class Cell
    {
        public RectTransform Rt;
        public Image Bg, Shade;
        public PixelText Val, Tag;
    }

    sealed class Fx
    {
        public RectTransform Rt;
        public Image Img;
        public Vector2 From, To;
        public float T, Dur;
        public bool Leaving;
        public Color Base;
        public byte A0;
    }

    public StackBoard(Transform parent, string structId, string name, System.Func<Vector2, Vector2> screenToLocal)
    {
        id = structId;
        dispName = string.IsNullOrEmpty(name) ? structId : name;
        Panel = FloatPanel.Create(parent, "StackBoard_" + structId,
            Theme.A(Theme.GrapeDeep, .85f), Theme.A(Theme.GrapeDeep, .92f));
        Panel.ScreenToLocal = screenToLocal;
        Panel.Root.sizeDelta = new Vector2(150f, 96f);
        Panel.OnResized = OnPanelResized;
        body = Panel.Body;
        Panel.SetTitle("栈 " + dispName);    }

    void OnPanelResized() { lastSig = null; Render(cachedItems, null); }

    // action 来自这一帧的事件类型：push / pop，其余一律按「数据变了但没动作」处理
    public void Render(List<object> items, string action)
    {
        var prev = cachedItems;
        cachedItems = items;
        var sig = Signature(items);
        if (sig == lastSig) return;
        lastSig = sig;

        int n = items != null ? items.Count : 0;
        Panel.SetTitle("栈 " + dispName + " · " + n + " 个");

        // 弹出时那只盘子原来待在「上一次的筐」里，高度变了要按旧内框算落点，否则一帧内跳位
        float oldInnerH = lastInnerH;

        int shown = Mathf.Min(n, MaxCells);
        float needH = HeightFor(shown > 0 ? shown : 1);
        float wantH = Panel.ResizedByUser ? Mathf.Max(needH, Panel.Size.y) : needH;
        wantH = Mathf.Clamp(wantH, Panel.MinSize.y, Panel.MaxSize.y);
        if (Mathf.Abs(Panel.Size.y - wantH) > .5f)
            Panel.Root.sizeDelta = new Vector2(Panel.Size.x, wantH);

        // 面板尺寸是自己算的，直接按它推内框高度：body 的上下留白固定为 HeadH+6 / 6，
        // 用 body.rect.height 会在同一帧里读到还没重排的旧值
        float innerH = Panel.Size.y - FloatPanel.HeadH - 12f;
        lastInnerH = innerH;
        // 0 号在底部：越靠栈顶的盘子越往上，视觉上就是一摞碗
        for (int i = 0; i < shown; i++)
        {
            var c = Acquire(i);
            bool top = i == shown - 1;
            float y = SlotY(innerH, i);
            c.Rt.anchoredPosition = new Vector2(0f, y);
            c.Bg.color = top ? new Color(1f, .93f, .55f) : Theme.A(Theme.Lavender, .75f);
            c.Shade.color = top ? Theme.A(Theme.Grape, .85f) : Theme.A(Theme.WhaleDeep, .3f);
            c.Val.Text = Dayu.Core.ArrayView.ElementText(items[i]);
            c.Val.color = Theme.Ink;
            c.Tag.Text = top ? "顶" : "";
            c.Tag.color = Theme.GrapeDeep;
        }
        for (int i = shown; i < cells.Count; i++) cells[i].Rt.gameObject.SetActive(false);

        // 进出动画只在玩家拖动看板之外补一层反馈：压入落顶、弹出飘走
        if (lastCount >= 0 && n != lastCount)
        {
            if (n > lastCount && shown > 0) SpawnIn(cells[shown - 1]);
            else SpawnOut(prev, lastCount, oldInnerH > 0f ? oldInnerH : innerH);
        }
        lastCount = n;
    }

    void SpawnIn(Cell c)
    {
        var to = c.Rt.anchoredPosition;
        fx.Add(new Fx { Rt = c.Rt, From = to + new Vector2(0f, 26f), To = to, Dur = .22f, Base = c.Bg.color, Img = c.Bg });
        c.Bg.color = Theme.A(Theme.Coral, .9f);
    }

    // 弹出的那只已经不在新列表里了，从上一帧的快照取值，借一块游离的盘子往上飘走淡出
    void SpawnOut(List<object> prev, int prevCount, float innerH)
    {
        if (prev == null || prevCount <= 0 || prevCount - 1 >= prev.Count) return;
        int idx = prevCount - 1;
        var go = new GameObject("Pop", typeof(RectTransform));
        go.transform.SetParent(body, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(CellW + TagW, CellH);
        rt.anchoredPosition = new Vector2(0f, SlotY(innerH, idx));
        var bg = GraphView.Tile(rt, "Bg", Theme.A(Theme.Coral, .9f), false);
        TopLeft(bg.rectTransform, Vector2.zero, new Vector2(CellW, CellH));
        var val = PixelText.Create(rt, Dayu.Core.ArrayView.ElementText(prev[idx]), Theme.Ink, 1);
        val.raycastTarget = false;
        val.Align = PixelText.TextAlign.Center;
        TopLeft(val.rectTransform, Vector2.zero, new Vector2(CellW, CellH));
        var col = bg.color;
        fx.Add(new Fx { Rt = rt, Img = bg, From = rt.anchoredPosition, To = rt.anchoredPosition + new Vector2(0f, 30f),
            Dur = .3f, Leaving = true, Base = col, A0 = (byte)(col.a * 255f) });
    }

    // 每帧由画布叫一次：推进进出动画，顺带给栈顶一点呼吸
    public void Tick(float dt)
    {
        if (cells.Count > 0)
        {
            bob += dt * 2.2f;
            float k = .5f + .5f * Mathf.Sin(bob);
            var top = cells[Mathf.Max(0, VisibleCells() - 1)];
            if (top != null && top.Rt != null && !IsTweening(top.Rt))
                top.Rt.localScale = Vector3.one * (1f + .03f * k);
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
                if (f.Leaving)
                {
                    var c = f.Base; c.a = c.a * (1f - k); f.Img.color = c;
                }
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

    // 第 index 只盘子在内框里的竖直位置：0 号贴底，越靠栈顶越往上（pivot 在左上的锚点系里越负越低）
    public static float SlotY(float innerH, int index)
    {
        return -(innerH - (index + 1) * (CellH + Gap) + Gap);
    }

    // 盘子太多只画最上面 MaxCells 只
    public static int VisibleCount(int count)
    {
        return Mathf.Min(Mathf.Max(count, 0), MaxCells);
    }

    // 盘子数决定看板该多高（纯函数，自测直接盯这个数）
    public static float HeightFor(int count)
    {
        return FloatPanel.HeadH + 12f + Mathf.Max(1, count) * (CellH + Gap) - Gap + 6f;
    }

    Cell Acquire(int i)
    {
        if (i < cells.Count) { cells[i].Rt.gameObject.SetActive(true); return cells[i]; }
        var go = new GameObject("Slot", typeof(RectTransform));
        go.transform.SetParent(body, false);
        var c = new Cell { Rt = (RectTransform)go.transform };
        c.Rt.anchorMin = c.Rt.anchorMax = new Vector2(0f, 1f);
        c.Rt.pivot = new Vector2(0f, 1f);
        c.Rt.sizeDelta = new Vector2(CellW + TagW, CellH);
        c.Shade = GraphView.Tile(c.Rt, "Shade", Theme.A(Theme.WhaleDeep, .3f), false);
        TopLeft(c.Shade.rectTransform, new Vector2(2f, -2f), new Vector2(CellW, CellH));
        c.Bg = GraphView.Tile(c.Rt, "Bg", Theme.A(Theme.Lavender, .75f), false);
        TopLeft(c.Bg.rectTransform, Vector2.zero, new Vector2(CellW, CellH));
        c.Val = PixelText.Create(c.Rt, "", Theme.Ink, 1);
        c.Val.raycastTarget = false;
        c.Val.Align = PixelText.TextAlign.Center;
        TopLeft(c.Val.rectTransform, Vector2.zero, new Vector2(CellW, CellH));
        c.Tag = PixelText.Create(c.Rt, "", Theme.GrapeDeep, 1);
        c.Tag.raycastTarget = false;
        TopLeft(c.Tag.rectTransform, new Vector2(CellW + 2f, 0f), new Vector2(TagW, CellH));
        cells.Add(c);
        return c;
    }

    // 新建的 RectTransform 锚点不是左上，盘子要从左上往下摞，每个子件都显式钉好
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
