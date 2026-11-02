using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 虚线悬浮块外壳：标题条按住拖位置，右下角拉手调大小，内容放进 Body 自己排。
// 数组看板、栈看板共用这一套外壳；射线只开在标题条和拉手上，其余地方点得穿。
public sealed class FloatPanel : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
{
    public const float HeadH = 20f;
    const float Dash = 6f, DashGap = 4f, Thick = 2f, Grip = 14f, GripHit = 26f, Edge = 6f;

    public RectTransform Root;
    public RectTransform Body;
    public Func<Vector2, Vector2> ScreenToLocal;   // 屏幕点 -> 父容器局部坐标，由画布注入
    public Action OnResized;
    public bool MovedByUser;
    public bool ResizedByUser;
    public Vector2 MinSize = new Vector2(120f, 76f);
    public Vector2 MaxSize = new Vector2(640f, 440f);

    RectTransform headRt, gripRt;
    public RectTransform HeadRect { get { return headRt; } }
    public RectTransform GripRect { get { return gripRt; } }
    PixelText title;
    Color frameColor;
    readonly List<Image> dashes = new List<Image>();
    Vector2 dragStartLocal, dragStartPos, dragStartSize;
    bool resizing, dragLogged;

    public static FloatPanel Create(Transform parent, string name, Color frame, Color head)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<FloatPanel>();
        p.Root = (RectTransform)go.transform;
        p.Root.anchorMin = p.Root.anchorMax = new Vector2(0f, 1f);
        p.Root.pivot = new Vector2(0f, 1f);
        p.frameColor = frame;

        var bg = GraphView.Tile(go.transform, "Bg", Theme.A(Theme.Paper, .93f), false);
        GraphView.Stretch(bg.rectTransform);

        p.headRt = GraphView.Tile(go.transform, "Head", head, true).rectTransform;
        p.headRt.anchorMin = new Vector2(0f, 1f);
        p.headRt.anchorMax = new Vector2(1f, 1f);
        p.headRt.pivot = new Vector2(0f, 1f);
        p.headRt.offsetMin = new Vector2(0f, -HeadH);
        p.headRt.offsetMax = Vector2.zero;

        p.title = PixelText.Create(p.headRt, "", Theme.White, 1);
        p.title.raycastTarget = false;
        var trt = p.title.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
        trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = new Vector2(6f, -3f);

        // 右下角拉手：两道长短横杠，一眼知道能拉；射线区比图案大一圈，小尺寸下也好点
        p.gripRt = GraphView.Tile(go.transform, "Grip", new Color(0f, 0f, 0f, 0f), true).rectTransform;
        p.gripRt.anchorMin = p.gripRt.anchorMax = new Vector2(1f, 0f);
        p.gripRt.pivot = new Vector2(1f, 0f);
        p.gripRt.anchoredPosition = Vector2.zero;
        p.gripRt.sizeDelta = new Vector2(GripHit, GripHit);
        p.gripRt.gameObject.AddComponent<GripTag>();
        GripBar(p.gripRt, "G1", frame, Grip - 4f, 4f);
        GripBar(p.gripRt, "G2", frame, Grip - 9f, 0f);

        p.Body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
        p.Body.SetParent(go.transform, false);
        GraphView.Stretch(p.Body);
        p.Body.offsetMin = new Vector2(Edge, Edge);
        p.Body.offsetMax = new Vector2(-Edge, -(HeadH + Edge));
        p.gripRt.SetAsLastSibling();

        p.Root.sizeDelta = p.MinSize;
        return p;
    }

    sealed class GripTag : MonoBehaviour { }

    static void GripBar(RectTransform parent, string name, Color c, float len, float yFromBottom)
    {
        var rt = GraphView.Tile(parent, name, c, false).rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-2f, yFromBottom + 2f);
        rt.sizeDelta = new Vector2(len, Thick);
    }

    public void SetTitle(string s) { if (title != null) title.Text = s ?? ""; }

    public Vector2 Size { get { return Root.sizeDelta; } }

    public void SetSize(Vector2 s)
    {
        Root.sizeDelta = new Vector2(
            Mathf.Clamp(s.x, MinSize.x, MaxSize.x),
            Mathf.Clamp(s.y, MinSize.y, MaxSize.y));
    }

    public void SetPosition(Vector2 topLeft) { Root.anchoredPosition = new Vector2(topLeft.x, -topLeft.y); }

    public Vector2 Position { get { return new Vector2(Root.anchoredPosition.x, -Root.anchoredPosition.y); } }

    void OnRectTransformDimensionsChange()
    {
        if (headRt == null) return;
        RebuildFrame();
        if (OnResized != null) OnResized();
    }

    // 虚线边框：横边竖边各铺一排短划，池化复用，拉大拉小不重建对象
    void RebuildFrame()
    {
        float w = Root.sizeDelta.x, h = Root.sizeDelta.y;
        var want = new List<Vector4>();
        for (float x = 0f; x < w; x += Dash + DashGap)
        {
            want.Add(new Vector4(x, 0f, Mathf.Min(Dash, w - x), Thick));
            want.Add(new Vector4(x, h - Thick, Mathf.Min(Dash, w - x), Thick));
        }
        for (float y = Dash + DashGap; y < h - Thick; y += Dash + DashGap)
        {
            want.Add(new Vector4(0f, y, Thick, Mathf.Min(Dash, h - Thick - y)));
            want.Add(new Vector4(w - Thick, y, Thick, Mathf.Min(Dash, h - Thick - y)));
        }
        for (int i = 0; i < want.Count; i++)
        {
            Image d;
            if (i < dashes.Count) d = dashes[i];
            else { d = GraphView.Tile(Root, "Dash", frameColor, false); dashes.Add(d); }
            var rt = d.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(want[i].x, -want[i].y);
            rt.sizeDelta = new Vector2(want[i].z, want[i].w);
            if (!d.enabled) d.enabled = true;
        }
        for (int i = want.Count; i < dashes.Count; i++) dashes[i].enabled = false;
        // 边框和拉手盖在内容上面
        Root.SetAsLastSibling();
    }

    public void OnPointerDown(PointerEventData e)
    {
        // 吃掉按下，否则冒泡到画布会被当成空白处框选
        if (e.button == PointerEventData.InputButton.Left) e.Use();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        var hit = e.pointerCurrentRaycast.gameObject != null ? e.pointerCurrentRaycast.gameObject : e.pointerEnter;
        resizing = hit != null && hit.GetComponentInParent<GripTag>() != null;
        Debug.Log("[交互] 看板抓住 " + gameObject.name + " 部位=" + (resizing ? "拉手" : "标题条"));
        if (ScreenToLocal == null) return;
        dragStartLocal = ScreenToLocal(e.position);
        dragStartPos = Root.anchoredPosition;
        dragStartSize = Root.sizeDelta;
        dragLogged = false;
        Root.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || ScreenToLocal == null) return;
        var d = ScreenToLocal(e.position) - dragStartLocal;
        if (!dragLogged)
        {
            dragLogged = true;
            Debug.Log("[交互] 看板拖动 " + gameObject.name + " 位移=(" + d.x.ToString("0.#") + "," + d.y.ToString("0.#") + ")");
        }
        // 先立旗再改尺寸：SetSize 会当场触发 OnResized，看板在那里按「玩家拉过就没有自动收高」来定，
        // 顺序反过来会把玩家刚拉高的那一截立刻收回内容高度。
        if (resizing) { ResizedByUser = true; SetSize(dragStartSize + new Vector2(d.x, -d.y)); }
        else { MovedByUser = true; Root.anchoredPosition = dragStartPos + d; }
    }
}
