using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class PixelButton : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    const int PadX = 16, DefH = 26;

    PanelGraphic bg;
    PixelText label;
    Action action;
    bool fireOnDown;   // 按下即触发：悬浮窗里的按钮用，避免轻微拖动或焦点切换把 click 吞掉
    bool downFired;    // 已在按下时触发过，随后那次 click 要跳过，否则回调跑两遍

    bool primary;
    bool affordance = true;
    string accentKey;

    Color32 baseFill, hoverFill, baseEdge, pressEdge, lastFill, lastEdge;
    bool stateInit;

    Coroutine liftCo;
    float lift;        // 当前浮起像素数：新动画一律从这里起步，连点连移不会叠加
    bool hovered, pressed;

    public RectTransform Rect => (RectTransform)transform;
    public float Width => Rect.sizeDelta.x;
    public float Height => Rect.sizeDelta.y;

    /// w <= 0 时按文字测宽 + 16 自动定宽；高度默认 26
    /// scale 只给悬浮窗这类短表单用 2；顶栏那种密集按钮保持 1，否则 11 个按钮撑不出 1440
    /// affordance=false 关掉描边/投影与 hover/按下位移，画在点阵背景上的小图标按钮用
    /// hoverColorKey 给状态类按钮换成类别色（hover 直接落到 Theme.Accent 上）
    public static PixelButton Create(Transform parent, string text, Action click, int w = 0, int h = DefH, bool primary = false, int scale = 1, bool clickOnDown = false, bool affordance = true, string hoverColorKey = null)
    {
        var go = new GameObject("PixelButton", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);

        var m = PixelText.MeasureString(text ?? "", scale);
        if (w <= 0) w = Mathf.CeilToInt(m.x) + PadX;
        if (h <= 0) h = DefH;
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = Vector2.zero;

        var b = go.AddComponent<PixelButton>();
        b.action = click;
        b.fireOnDown = clickOnDown;
        b.affordance = affordance;
        b.accentKey = hoverColorKey;

        // 底板：独立子节点，pivot 放在左下角，PanelGraphic 从 (0,0) 画到 (w,h) 正好铺满
        var bgGo = new GameObject("Bg", typeof(RectTransform));
        bgGo.transform.SetParent(go.transform, false);
        var brt = (RectTransform)bgGo.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0, 0);
        brt.pivot = new Vector2(0, 0);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(w, h);
        b.bg = bgGo.AddComponent<PanelGraphic>();
        b.bg.raycastTarget = true;
        if (affordance)
        {
            // 位移与投影全部落在 Bg / 文字这两个子节点上：App 是手工排布的，动根矩形会把整片布局推歪
            b.bg.Stroke = UiKit.ButtonStroke;
            b.bg.DropShadow = h >= 22 ? UiKit.ButtonDrop : 0;   // 20px 以下的小按钮投影会糊成一团
            if (h >= 30) b.bg.Notch = 2;
        }
        else
        {
            b.bg.Stroke = 0;
            b.bg.Thickness = 0;
            b.bg.DropShadow = 0;
        }

        // 文字：整块铺满按钮 (0,0,w,h)，水平居中，垂直由 PixelText 自动居中
        b.label = PixelText.Create(go.transform, text, Theme.Ink, scale);
        var lrt = b.label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0, 0);
        lrt.pivot = new Vector2(0, 0);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(w, h);
        b.label.Align = PixelText.TextAlign.Center;
        b.label.raycastTarget = false;
        b.ResolveColors();
        b.ApplyState();
        return b;
    }

    // 常态按下用 Theme 里注明过的 SaltPress；类别色按钮的底色是动态混合出来的，就照同一档比 HoverFill 沉 12% 现算
    Color32 PressFill(Color32 hover)
    {
        return Same(hover, Theme.White) ? Theme.SaltPress : Theme.Mix(hover, Theme.Ink, .12f);
    }

    static bool Same(Color32 a, Color32 b) { return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a; }

    void ResolveColors()
    {
        if (string.IsNullOrEmpty(accentKey))
        {
            baseFill = primary ? Theme.Honey : Theme.Salt;
            hoverFill = primary ? Theme.HoneyLift : Theme.White;
            baseEdge = primary ? Theme.InkSoft : Theme.Border;
            pressEdge = primary ? Theme.Border : Theme.InkPress;
        }
        else
        {
            Color32 a = Theme.Accent(accentKey);
            baseFill = primary ? a : Theme.Mix(a, Theme.White, .66f);
            hoverFill = a;
            baseEdge = primary ? Theme.InkSoft : Theme.Border;
            pressEdge = Theme.Border;
        }
        stateInit = false;   // 颜色表换过一套，下一次 ApplyState 一定要刷网格
    }

    void ApplyState()
    {
        if (bg == null) return;
        bool hot = enabled && (hovered || pressed);
        Color32 fill = !affordance ? baseFill
                     : hot ? (pressed ? PressFill(hoverFill) : hoverFill)
                     : baseFill;
        Color32 edge = pressed ? pressEdge : baseEdge;
        bg.Fill = fill;
        bg.Edge = edge;
        // Color32 在 Unity 6 里没有 ==/!= 运算符，逐字节比
        if (!stateInit || !Same(lastFill, fill) || !Same(lastEdge, edge))
        {
            stateInit = true;
            lastFill = fill;
            lastEdge = edge;
            bg.SetVerticesDirty();
        }
    }

    void Refresh()
    {
        float target = 0f;
        if (enabled && affordance && hovered)
            target = pressed ? Mathf.Max(0f, UiKit.Lift - UiKit.Press) : UiKit.Lift;
        ApplyState();
        AnimateLift(target, pressed ? UiKit.PressDur : UiKit.RaiseDur);
    }

    void AnimateLift(float to, float dur)
    {
        if (Mathf.Abs(lift - to) < .01f) return;
        if (liftCo != null) { StopCoroutine(liftCo); liftCo = null; }
        float from = lift;
        liftCo = UiKit.Animate(this, dur, k => SetLift(Mathf.LerpUnclamped(from, to, k)));
        if (liftCo == null) SetLift(to);   // UiKit.Instant / dur<=0 时 Animate 已经落过终态，这里只兜底
    }

    void SetLift(float v)
    {
        lift = v;
        var p = new Vector2(0f, v);
        if (bg != null) bg.rectTransform.anchoredPosition = p;
        if (label != null) label.rectTransform.anchoredPosition = p;
    }

    public void SetEnabled(bool v)
    {
        enabled = v;
        bg.color = v ? Color.white : new Color(.55f, .55f, .55f, .7f);
        if (!v) { hovered = false; pressed = false; }   // 禁用态要把 hover 视觉清零，否则灰底上挂着浮起+高亮
        Refresh();
    }

    public void SetLabel(string s) { label.Text = s; }

    public void SetPrimary(bool on)
    {
        if (bg == null) return;
        primary = on;
        ResolveColors();
        ApplyState();   // 只刷颜色：浮起量由 hover/press 决定，这里不该顺手挪位子
    }

    public void SetAction(Action a) { action = a; }

    public void OnPointerEnter(PointerEventData e)
    {
        if (!enabled) return;
        hovered = true;
        Refresh();
    }

    public void OnPointerExit(PointerEventData e)
    {
        hovered = false;
        pressed = false;
        Refresh();
    }

    public void OnPointerDown(PointerEventData e)
    {
        pressed = true;
        Refresh();
        if (!fireOnDown || !enabled) return;
        if (e.button != PointerEventData.InputButton.Left) return;
        downFired = true;
        action?.Invoke();
    }

    public void OnPointerUp(PointerEventData e)
    {
        pressed = false;
        Refresh();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (downFired) { downFired = false; return; }
        if (enabled) action?.Invoke();
    }
}
