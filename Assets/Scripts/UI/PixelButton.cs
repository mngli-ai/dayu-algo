using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class PixelButton : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
{
    const int PadX = 16, DefH = 26;

    PanelGraphic bg;
    PixelText label;
    Action action;
    bool fireOnDown;   // 按下即触发：悬浮窗里的按钮用，避免轻微拖动或焦点切换把 click 吞掉
    bool downFired;    // 已在按下时触发过，随后那次 click 要跳过，否则回调跑两遍

    public RectTransform Rect => (RectTransform)transform;
    public float Width => Rect.sizeDelta.x;
    public float Height => Rect.sizeDelta.y;

    /// w <= 0 时按文字测宽 + 16 自动定宽；高度默认 26
    /// scale 只给悬浮窗这类短表单用 2；顶栏那种密集按钮保持 1，否则 11 个按钮撑不出 1440
    public static PixelButton Create(Transform parent, string text, Action click, int w = 0, int h = DefH, bool primary = false, int scale = 1, bool clickOnDown = false)
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

        // 底板：独立子节点，pivot 放在左下角，PanelGraphic 从 (0,0) 画到 (w,h) 正好铺满
        var bgGo = new GameObject("Bg", typeof(RectTransform));
        bgGo.transform.SetParent(go.transform, false);
        var brt = (RectTransform)bgGo.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0, 0);
        brt.pivot = new Vector2(0, 0);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(w, h);
        b.bg = bgGo.AddComponent<PanelGraphic>();
        b.bg.Fill = primary ? Theme.Honey : Theme.Salt;
        b.bg.raycastTarget = true;

        // 文字：整块铺满按钮 (0,0,w,h)，水平居中，垂直由 PixelText 自动居中
        b.label = PixelText.Create(go.transform, text, Theme.Ink, scale);
        var lrt = b.label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0, 0);
        lrt.pivot = new Vector2(0, 0);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(w, h);
        b.label.Align = PixelText.TextAlign.Center;
        b.label.raycastTarget = false;
        return b;
    }

    public void SetEnabled(bool v)
    {
        enabled = v;
        bg.color = v ? Color.white : new Color(.55f, .55f, .55f, .7f);
    }

    public void SetLabel(string s) { label.Text = s; }

    public void SetPrimary(bool on)
    {
        if (bg == null) return;
        bg.Fill = on ? Theme.Honey : Theme.Salt;
        bg.SetVerticesDirty();
    }

    public void SetAction(Action a) { action = a; }

    public void OnPointerDown(PointerEventData e)
    {
        if (!fireOnDown || !enabled) return;
        if (e.button != PointerEventData.InputButton.Left) return;
        downFired = true;
        action?.Invoke();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (downFired) { downFired = false; return; }
        if (enabled) action?.Invoke();
    }
}
