using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 像素风文本框：直接用 PixelText 渲染，键盘输入写回节点参数。
// 变量名/常量值这类短文本足够用，避免引入 TMP 或默认 Arial 破坏像素风。
public sealed class PixelInputField : MonoBehaviour, IPointerClickHandler
{
    public Action<string> OnChanged;
    // 只有回车 = 真的要提交（参数窗拿它当「确定」用）
    public Action<string> OnCommitted;
    // 点到别处 / 关掉窗口 = 只是失焦，宿主只能同步草稿，不能当成提交
    public Action<string> OnBlurred;
    // Tab / Shift+Tab：参数里有多处填空时用来跳位，参数是「是否往回跳」
    public Action<bool> OnTab;

    PixelText label;
    Image bg;
    bool focused;
    // 变量名这类短文本 24 个字符够了；表达式框由宿主调大，否则「sum = sum + a[i] * 12」这种会被静默截断
    public int MaxLength = 24;

    static PixelInputField active;
    static int sTabFrame = -1;

    // 纯函数：一次物理 Tab 按键只允许一个输入框处理。
    // 不拦的话，OnTab 刚把焦点切到下一个框，同一帧里那个框的 Update 又读到
    // GetKeyDown(Tab)==true，于是 Shift+Tab 立刻原地弹回，看着像反向跳位没反应。
    public static bool TakesTab(int frameCount, int lastTabFrame)
    {
        return frameCount != lastTabFrame;
    }

    public string Value { get; private set; } = "";

    public static PixelInputField Create(Transform parent, string value, int w, int h, int scale = 1)
    {
        var go = new GameObject("PixelInput", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);

        var f = go.AddComponent<PixelInputField>();
        f.Value = value ?? "";
        f.bg = go.GetComponent<Image>();
        f.bg.sprite = GraphView.White;
        f.bg.type = Image.Type.Simple;
        f.bg.color = Theme.Salt;
        f.bg.raycastTarget = true;
        var edge = go.AddComponent<Outline>();
        edge.effectColor = Theme.Border;
        edge.effectDistance = new Vector2(1, -1);

        f.label = PixelText.Create(go.transform, f.Display(), Theme.Ink, scale);
        f.label.raycastTarget = false;
        var lrt = f.label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
        lrt.pivot = new Vector2(0f, 1f);
        lrt.sizeDelta = new Vector2(w - 8f, h);
        lrt.anchoredPosition = new Vector2(4f, 0f);
        f.label.Align = PixelText.TextAlign.Left;
        return f;
    }

    string Display()
    {
        var s = Value;
        if (focused) s += "_";
        if (s.Length == 0) s = focused ? "_" : "点这里输入";
        return s;
    }

    void OnEnable()
    {
        if (bg != null) bg.color = Theme.Salt;
    }

    public void OnPointerClick(PointerEventData e)
    {
        Focus();
    }

    // 代码路径直接给焦点：双击模块开窗后要立刻落在某个输入框里，玩家不用再点一下
    public void Focus()
    {
        if (active != null && active != this) active.Blur();
        active = this;
        focused = true;
        bg.color = Theme.Cream;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
        Refresh();
    }

    public void Blur()
    {
        CommitAndBlur(false);
    }

    // commit=true 只在按回车时走；被点走 / 关窗都只报失焦。
    // 早先失焦也发 OnCommitted，参数窗把 OnCommitted 接成了「提交并关窗」，
    // 于是「点第二个输入框」等于「点确定」——整扇窗直接消失。
    void CommitAndBlur(bool commit)
    {
        if (!focused) return;
        focused = false;
        bg.color = Theme.Salt;
        Refresh();
        if (active == this) active = null;
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            EventSystem.current.SetSelectedGameObject(null);
        if (commit)
        {
            if (OnCommitted != null) OnCommitted(Value);
        }
        else if (OnBlurred != null) OnBlurred(Value);
    }

    void CancelFocus()
    {
        focused = false;
        bg.color = Theme.Salt;
        Refresh();
        if (active == this) active = null;
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void SetValue(string v)
    {
        Value = v ?? "";
        Refresh();
    }

    public static void FlushActive()
    {
        if (active != null) active.CommitAndBlur(false);
    }

    void Update()
    {
        if (!focused) return;

        bool changed = false;
        if (Input.GetKeyDown(KeyCode.Backspace) && Value.Length > 0)
        {
            Value = Value.Substring(0, Value.Length - 1);
            changed = true;
        }

        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
        {
            var clip = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip)) { Value = (Value + clip).Substring(0, Mathf.Min(MaxLength, (Value + clip).Length)); changed = true; }
        }

        foreach (char c in Input.inputString)
        {
            if (c == '\b' || c == '\n' || c == '\r' || c == '\t') continue;
            if (Value.Length >= MaxLength) break;
            Value += c;
            changed = true;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            CommitAndBlur(true);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (!TakesTab(Time.frameCount, sTabFrame)) return;
            sTabFrame = Time.frameCount;
            bool backward = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (OnTab != null)
            {
                OnTab(backward);
                Refresh();
            }
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelFocus();
            return;
        }

        if (changed)
        {
            Refresh();
            if (OnChanged != null) OnChanged(Value);
        }
    }

    void Refresh()
    {
        if (label != null) label.Text = Display();
    }
}
