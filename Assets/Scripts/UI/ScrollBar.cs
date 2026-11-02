using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 4px 细滚动条。只依赖 PaletteScroll 公开出来的 Offset / MaxScroll / ScrollTo / Content，
// 位置一律交给 ScrollTo 钳位，这里只钳指针、不钳第二遍偏移。
public sealed class ScrollBar : MonoBehaviour, IPointerDownHandler, IDragHandler, IScrollHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    public const float Width = 4f;
    public const string NodeName = "ScrollBar";

    const float MinThumb = 18f;
    const float StepPx = 28f;        // 与 PaletteScroll.OnScroll 的每格 28px 对齐，滚轮和拖条手感一致

    App.PaletteScroll target;
    RectTransform viewport;          // PaletteScroll 就挂在视口上，视口高即轨道高，缩放窗口也跟着变
    RectTransform parentRt;
    RectTransform trackRt, thumbRt;
    Graphic trackG, thumbG;
    float fallbackH = 1f;
    float thumbH = MinThumb;
    bool hovered, shown;
    Color thumbIdle, thumbHot, trackTint;

    /// 在 parent 的右上角内侧挂一条滚动槽（anchor/pivot 都是 (1,1)，rightInset 距右边、topInset 距顶边，都是像素）。
    /// parent 要用没被 RectMask2D 裁住的那一层（视口本身带遮罩，thumb 会被裁掉）。
    /// trackHeight 是视口高（Awake 阶段 rect 还没刷出来时的兜底值），之后每帧按视口实际高度跟随。
    /// 同名旧条会被替换，所以题栏折叠反复重建也不会叠出第二条。
    public static ScrollBar Attach(RectTransform parent, App.PaletteScroll target, float trackHeight,
        float rightInset = 6f, float topInset = 0f, bool blockClicks = false)
    {
        if (parent == null || target == null) return null;
        var prev = parent.Find(NodeName);
        if (prev != null)
        {
            // Destroy 延迟到帧末，这一帧里旧条还活着：先禁掉组件再隐藏，否则同屏两条会互相盖
            var ob = prev.GetComponent<ScrollBar>();
            if (ob != null) ob.enabled = false;
            prev.gameObject.SetActive(false);
            Destroy(prev.gameObject);
        }
        var go = new GameObject(NodeName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var bar = go.AddComponent<ScrollBar>();
        bar.Bind(parent, target, trackHeight, rightInset, topInset, blockClicks);
        return bar;
    }

    void Bind(RectTransform parent, App.PaletteScroll sc, float trackHeight, float rightInset, float topInset, bool blockClicks)
    {
        target = sc;
        parentRt = parent;
        fallbackH = Mathf.Max(1f, trackHeight);
        thumbIdle = Theme.A(Theme.WhaleDeep, .42f);
        thumbHot = Theme.A(Theme.WhaleDeep, .86f);
        trackTint = Theme.A(Theme.Ink, .10f);

        var rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-rightInset, -topInset);
        rt.sizeDelta = new Vector2(Width, fallbackH);

        var t = new GameObject("Track", typeof(RectTransform));
        t.transform.SetParent(transform, false);
        trackRt = (RectTransform)t.transform;
        trackRt.anchorMin = trackRt.anchorMax = new Vector2(0, 0);
        trackRt.pivot = new Vector2(0, 0);         // pivot 左下：PanelGraphic 的形状从 (0,0) 画到 (w,h) 才和 rect 对齐
        trackRt.anchoredPosition = Vector2.zero;
        trackRt.sizeDelta = new Vector2(Width, fallbackH);
        trackG = MakeGraphic(t, 0, false);
        trackG.color = trackTint;

        var h = new GameObject("Thumb", typeof(RectTransform));
        h.transform.SetParent(transform, false);
        thumbRt = (RectTransform)h.transform;
        thumbRt.anchorMin = thumbRt.anchorMax = new Vector2(0, 0);
        thumbRt.pivot = new Vector2(0, 0);
        thumbRt.anchoredPosition = Vector2.zero;
        thumbRt.sizeDelta = new Vector2(Width, MinThumb);
        thumbG = MakeGraphic(h, 1, true);
        thumbG.color = thumbIdle;

        if (blockClicks)
        {
            // 4px 条上本来只有一格一格的空白，默认让点击穿到后面的列表；需要挡住时才铺一层全透明底
            var blocker = MakeGraphic(gameObject, 0, true);
            blocker.color = new Color(1f, 1f, 1f, 0f);
        }

        viewport = target.GetComponent<RectTransform>();
        shown = true;
        SetShown(false);
    }

    Graphic MakeGraphic(GameObject go, int notch, bool ray)
    {
        var g = go.AddComponent<PanelGraphic>();
        g.Fill = Theme.White;      // 底色固定纯白，颜色与透明度全部走 Graphic.color，hover 只改一次 color
        g.Notch = notch;
        g.Thickness = 0;           // 旧那圈描边顶点面积恒为 0，细条上直接省掉
        g.raycastTarget = ray;     // 槽底永远不吃射线，滚轮要继续落到视口上；只有 thumb 吃
        return g;
    }

    void SetShown(bool v)
    {
        if (shown == v) return;
        shown = v;
        if (trackG != null) trackG.gameObject.SetActive(v);
        if (thumbG != null) thumbG.gameObject.SetActive(v);
    }

    float ViewHeight()
    {
        if (viewport != null && viewport.rect.height > 1f) return viewport.rect.height;
        return fallbackH;
    }

    static bool Off(float a, float b) { return Mathf.Abs(a - b) < .25f; }

    void LateUpdate()
    {
        if (target == null) { SetShown(false); return; }
        float max = target.MaxScroll;
        float vh = ViewHeight();
        float pw = parentRt != null ? parentRt.rect.width : 9999f;
        // 没什么可滚、或者父面板已经被折叠到 0 宽（题栏收起），整条收起
        if (max <= .001f || pw < Width + 1f) { SetShown(false); return; }
        SetShown(true);

        float hi = Mathf.Max(MinThumb, vh);
        float th = Mathf.Clamp(vh * vh / (vh + max), MinThumb, hi);
        thumbH = th;
        float span = Mathf.Max(0f, vh - th);
        float bottom = span * (1f - Mathf.Clamp01(target.Offset / max));   // offset=0 → thumb 贴顶

        if (!Off(rtHeight(trackRt), vh)) trackRt.sizeDelta = new Vector2(Width, vh);
        if (!Off(rtHeight(thumbRt), th)) thumbRt.sizeDelta = new Vector2(Width, th);
        if (!Off(thumbRt.anchoredPosition.y, bottom)) thumbRt.anchoredPosition = new Vector2(0f, bottom);
    }

    static float rtHeight(RectTransform rt) { return rt == null ? 0f : rt.rect.height; }

    void MoveTo(PointerEventData e)
    {
        if (target == null || trackRt == null) return;
        float max = target.MaxScroll;
        if (max <= .001f) return;
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(trackRt, e.position, e.pressEventCamera, out lp)) return;
        float span = trackRt.rect.height - thumbH;
        if (span <= 1f) return;
        // thumb 中心跟指针：从轨道底往上量，再换算成「还剩多少可滚」的比例
        float bottom = Mathf.Clamp(lp.y - thumbH * .5f, 0f, span);
        target.ScrollTo(max * (1f - bottom / span));
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        MoveTo(e);
    }

    public void OnDrag(PointerEventData e) { MoveTo(e); }

    public void OnScroll(PointerEventData e)
    {
        if (target == null || target.MaxScroll <= .001f) return;
        target.ScrollTo(target.Offset - e.scrollDelta.y * StepPx);
        e.Use();
    }

    public void OnPointerEnter(PointerEventData e) { hovered = true; ApplyTint(); }

    public void OnPointerExit(PointerEventData e) { hovered = false; ApplyTint(); }

    void ApplyTint() { if (thumbG != null) thumbG.color = hovered ? thumbHot : thumbIdle; }
}
