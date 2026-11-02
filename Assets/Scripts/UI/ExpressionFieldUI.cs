using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Dayu.Core;

// 表达式字段的可用元素：标量变量、数组变量（可带成员，如 length / size / empty）。
public sealed class ExprVariableInfo
{
    public string Name;
    public string Type;
    public bool IsArray;
    public readonly List<string> Members = new List<string>();

    public ExprVariableInfo() { Name = ""; Type = "int"; }

    public ExprVariableInfo(string name, string type, bool isArray = false, params string[] members)
    {
        Name = name ?? "";
        Type = string.IsNullOrEmpty(type) ? "int" : type;
        IsArray = isArray || Type.EndsWith("[]");
        if (members != null) Members.AddRange(members);
    }

    public string Label { get { return IsArray ? Name + "[]" : Name; } }
}

// 内嵌表达式字段：折叠态 26px 单行文本，点开后在字段上/下方弹出 520x300 小窗。
// 文本与光标全部走 PixelText 的字形推进量，不依赖 InputField / TMP。
public sealed class ExpressionFieldUI : MonoBehaviour, IPointerClickHandler
{
    public const int Height = 28;
    public const float DefaultWidth = 220f;
    // 折叠态只剩一条压在身份行上的隐形点击区，不再占一整条空白框
    const float CollapsedH = 16f;
    static readonly Color Invisible = new Color(0f, 0f, 0f, 0f);

    public string Context = "表达式";
    // 同一张卡上有好几个表达式字段时（计数循环的起点 / 终点），浮窗头部给一排页签直接切，
    // 玩家不用再回卡片上找另一条框。
    public NodeCard Card;
    public string FieldKey = "";
    public string TabLabel = "表达式";
    public string[] TabGroup;

    internal List<ExprVariableInfo> Variables { get { return vars; } }
    internal Func<string, string> Validator { get { return validate; } }
    internal void AttachPopover(ExpressionPopoverUI p) { popover = p; }

    string value = "";
    List<ExprVariableInfo> vars = new List<ExprVariableInfo>();
    Func<string, string> validate;
    Action<string> onCommit;

    PanelGraphic bg;
    RectTransform bgRect;
    PixelText label;
    RectTransform viewport;
    ExpressionPopoverUI popover;
    float innerW = DefaultWidth - 16f;
    float width = DefaultWidth;
    bool collapsed;
    // 名牌式折叠：文字由模块结构的名牌负责，值改了也不展开，点击区大小由外部指定
    bool pinned;

    public RectTransform Rect { get { return (RectTransform)transform; } }
    public string Value { get { return value; } }
    public bool IsOpen { get { return popover != null; } }
    public bool IsCollapsed { get { return collapsed; } }

    /// 有弹窗打开时，画布层应屏蔽 Delete / Backspace 这类全局快捷键。
    public static bool AnyOpen { get { return ExpressionPopoverUI.Current != null; } }

    public static ExpressionFieldUI Create(Transform parent, string initial, List<ExprVariableInfo> vars, Func<string, string> validate, Action<string> onCommit, float width = DefaultWidth)
    {
        var go = new GameObject("ExpressionField", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(width, Height);
        rt.anchoredPosition = Vector2.zero;

        var f = go.AddComponent<ExpressionFieldUI>();
        f.value = initial ?? "";
        f.vars = vars ?? new List<ExprVariableInfo>();
        f.validate = validate;
        f.onCommit = onCommit;
        f.width = width;

        // 底板：PanelGraphic 从局部 (0,0) 画到 (w,h)，所以必须挂在 pivot 左下的子节点上
        // （和 PixelButton 同一套路）。挂在字段自身（pivot 左上）会让框整体上移一个字段高，
        // 于是身份行落进框里、表达式反而画到框外。
        var bgGo = new GameObject("Bg", typeof(RectTransform));
        bgGo.transform.SetParent(go.transform, false);
        var brt = (RectTransform)bgGo.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0, 0);
        brt.pivot = new Vector2(0, 0);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(width, Height);
        f.bg = bgGo.AddComponent<PanelGraphic>();
        f.bgRect = brt;
        f.bg.Fill = Theme.Salt;
        f.bg.Edge = Theme.Border;
        f.bg.Thickness = 1;
        f.bg.raycastTarget = true;

        // 视口负责横向裁剪：文本超长时右侧直接切掉，避免盖住边框
        var vpGo = new GameObject("Viewport", typeof(RectTransform));
        vpGo.transform.SetParent(go.transform, false);
        f.viewport = (RectTransform)vpGo.transform;
        f.viewport.anchorMin = f.viewport.anchorMax = new Vector2(0f, 1f);
        f.viewport.pivot = new Vector2(0f, 1f);
        f.viewport.anchoredPosition = new Vector2(5f, 0f);
        f.viewport.sizeDelta = new Vector2(width - 10f, Height);
        vpGo.AddComponent<RectMask2D>();

        f.label = PixelText.Create(f.viewport, "", Theme.Ink, 1);
        var lrt = f.label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
        lrt.pivot = new Vector2(0f, 1f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(width - 10f, Height);
        f.label.Align = PixelText.TextAlign.Center;
        f.label.raycastTarget = false;

        f.innerW = width - 16f;
        f.Refresh();
        return f;
    }

    public void SetValue(string v)
    {
        value = v ?? "";
        if (collapsed && !pinned && !string.IsNullOrEmpty(value)) SetCollapsed(false);
        else if (!pinned) Refresh();
    }

    /// <summary>折叠态：底板与文字都不画，只留一条隐形点击区，点它照样打开小窗。</summary>
    public void SetCollapsed(bool value)
    {
        collapsed = value;
        float h = value ? CollapsedH : Height;
        Rect.sizeDelta = new Vector2(width, h);
        if (bgRect != null) bgRect.sizeDelta = new Vector2(width, h);
        if (viewport != null) viewport.sizeDelta = new Vector2(width - 10f, h);
        if (label != null)
        {
            label.rectTransform.sizeDelta = new Vector2(width - 10f, h);
            if (value) label.Text = "";
        }
        if (bg != null)
        {
            bg.Fill = value ? Invisible : Theme.Salt;
            bg.Edge = value ? Invisible : Theme.Border;
            bg.SetVerticesDirty();
        }
        if (!value) Refresh();
    }

    /// <summary>名牌式折叠：点击区落在调用方给的矩形上（模块皮肤的名牌位置），值改了也不自动展开。</summary>
    public void SetCollapsedArea(float x, float y, float w, float h)
    {
        pinned = true;
        SetCollapsed(true);
        Rect.sizeDelta = new Vector2(w, h);
        Rect.anchoredPosition = new Vector2(x, -y);
        if (bgRect != null) bgRect.sizeDelta = new Vector2(w, h);
        if (viewport != null) viewport.sizeDelta = new Vector2(w, h);
        if (label != null) label.rectTransform.sizeDelta = new Vector2(w, h);
    }

    // 单击留给「选中这个模块」，双击才进表达式浮窗；e.Use() 防止这次点击再冒泡到卡片，
    // 否则卡片的双击处理会同时把参数悬浮窗也弹出来。
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (e.clickCount < 2) return;
        if (popover != null) return;
        e.Use();
        // 创建变量 / 赋值只有一个面板：双击卡上这块表达式区也开参数窗，
        // 光标一律落到「这一类模块第一个该填的空」——创建变量就是名字，绝不是这里那条初始值
        if (Card != null && Card.Owner != null && Card.Node != null
            && (Card.Node.Type == "vardecl" || Card.Node.Type == "varset"))
        {
            Card.Owner.NotifyParamsFocused(Card, SaveFlow.PreferredFocusKey(Card.Node.Type));
            return;
        }
        OpenPopover();
    }

    public void OpenPopover()
    {
        if (popover != null) return;
        // 挂在 Canvas 根下并置为最后一个兄弟，保证弹窗画在所有节点卡片与面板之上
        var canvas = GetComponentInParent<Canvas>();
        Transform layer = canvas != null ? canvas.rootCanvas.transform : transform.root;
        popover = ExpressionPopoverUI.Open((RectTransform)layer, this, value, vars, validate);
        popover.transform.SetAsLastSibling();
        if (!collapsed) { bg.Fill = Theme.Cream; bg.SetVerticesDirty(); }
    }

    internal void OnPopoverResult(string v, bool commit)
    {
        popover = null;
        if (!collapsed) { bg.Fill = Theme.Salt; bg.SetVerticesDirty(); }
        if (!commit) return;
        value = v ?? "";
        Refresh();
        if (onCommit != null) onCommit(value);
    }

    internal string ContextLabel { get { return string.IsNullOrEmpty(Context) ? "表达式" : Context; } }

    void Refresh()
    {
        if (label == null) return;
        if (string.IsNullOrEmpty(value))
        {
            // 空表达式什么都不写：一个居中的加号会被读成表达式的一部分（「a : int +」）。
            // 空框本身既是提示也是点击区。
            label.Scale = 1;
            label.Text = "";
            label.color = Theme.Border;
            return;
        }
        label.color = Theme.Ink;
        // 写得下就用双倍字号（和标题同高），放不下退回单倍字号并省略尾部。
        if (PixelText.MeasureString(value, 2).x <= innerW) { label.Scale = 2; label.Text = value; }
        else { label.Scale = 1; label.Text = Ellipsize(value, innerW); }
    }

    // 单行省略：超宽时从尾部截断并补 “…”
    internal static string Ellipsize(string s, float maxWidth)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (PixelText.MeasureString(s, 1).x <= maxWidth) return s;
        const string Dot = "…";
        float dotW = PixelText.MeasureString(Dot, 1).x;
        for (int i = s.Length - 1; i > 0; i--)
        {
            string head = s.Substring(0, i);
            if (PixelText.MeasureString(head, 1).x + dotW <= maxWidth) return head + Dot;
        }
        return Dot;
    }
}

// 520x300 的表达式小窗：顶部上下文、左侧元素栏、中间单行输入 + 自绘光标、下方类型/错误提示。
public sealed class ExpressionPopoverUI : MonoBehaviour
{
    const float W = 520f, H = 300f;
    const float PaletteW = 152f;
    const float InputX = 164f, InputY = 38f, InputW = 344f, InputH = 30f;
    const int CaretTop = 7, CaretH = 16;

    // 头部页签 / 顺序开关的尺寸：页签是这扇窗里最常点的两块，比原来的 88x22 明显放宽放高
    const float TabX0 = 60f, TabW = 104f, TabH = 28f, TabY = 1f, TabGap = 4f;
    const float DirLabelX = 282f, DirLabelW = 30f, DirChipW = 54f;

    // 补全浮窗一行一颗成员：左边英文 token、右边中文释义，宽度按量出来的字宽给，不硬编码
    const float CompPadX = 6f, CompRowH = 20f, CompTokenGap = 14f;
    const float CompTop = 4f, CompFootGap = 3f, CompFootH = 14f, CompBottom = 4f;

    public static ExpressionPopoverUI Current { get; private set; }

    static Sprite white;

    ExpressionFieldUI owner;
    List<ExprVariableInfo> vars;
    Func<string, string> validate;

    string draft = "";
    int caret, anchor;
    float scrollX, blink;
    string errorText = "";

    CanvasGroup group;
    RectTransform overlay, root, viewport, caretRect, highlightRect, blocker;
    PixelText inputText, hintText, headerTitle, helpText;
    readonly List<PixelButton> headerTabs = new List<PixelButton>();
    readonly List<PixelButton> dirChips = new List<PixelButton>();
    readonly List<string> dirVals = new List<string>();
    string[] tabKeys;
    Image caretImage;
    Camera uiCamera;
    bool closing;

    // 元素栏滚动
    RectTransform paletteView, paletteContent, paletteThumb;
    float paletteScroll, paletteMax;
    float flowX, flowY;

    // 芯片拖拽
    ExprChip dragChip;
    RectTransform dragGhost, dropBarRect;
    Image dragGhostImage, dropBarImage;
    PixelText dragGhostLabel;
    Outline inputOutline;

    // 成员补全浮窗：打完一个 `.` 就贴着光标列出这个变量能取的方法
    RectTransform completionPanel;
    Image completionSelection;
    List<MemberDef> completionItems;
    string completionVar = "";
    int completionSel;

    static Sprite White
    {
        get
        {
            if (white != null) return white;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0, 0), 1f);
            return white;
        }
    }

    public static ExpressionPopoverUI Open(RectTransform layer, ExpressionFieldUI field, string value, List<ExprVariableInfo> vars, Func<string, string> validate)
    {
        if (layer == null) return null;
        if (Current != null) Destroy(Current.gameObject);

        EnsureAtlas();

        var go = new GameObject("ExpressionPopover", typeof(RectTransform));
        go.transform.SetParent(layer, false);
        var p = go.AddComponent<ExpressionPopoverUI>();
        p.owner = field;
        p.vars = vars ?? new List<ExprVariableInfo>();
        p.validate = validate;
        p.draft = value ?? "";
        p.caret = p.anchor = p.draft.Length;
        p.Build(layer);
        Current = p;
        return p;
    }

    static void EnsureAtlas()
    {
        if (GlyphAtlas.Texture != null) return;
        var tex = Resources.Load<Texture2D>("atlas");
        var js = Resources.Load<TextAsset>("atlas");
        if (tex != null && js != null) GlyphAtlas.Init(tex, js.text);
    }

    void Build(RectTransform layer)
    {
        overlay = (RectTransform)transform;
        // overlay 铺满整屏，负责遮罩与淡入淡出
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        overlay.pivot = new Vector2(0f, 1f);

        // 透明挡板：挡住下面的节点 / 按钮，同时把「点窗外」翻译成取消并还原原值
        var blockGo = new GameObject("Blocker", typeof(RectTransform), typeof(Image));
        blockGo.transform.SetParent(overlay, false);
        blocker = (RectTransform)blockGo.transform;
        blocker.anchorMin = Vector2.zero;
        blocker.anchorMax = Vector2.one;
        blocker.offsetMin = blocker.offsetMax = Vector2.zero;
        var blockImg = blockGo.GetComponent<Image>();
        blockImg.sprite = White;
        blockImg.type = Image.Type.Simple;
        blockImg.color = new Color(0, 0, 0, 0);
        blockImg.raycastTarget = true;
        blockGo.AddComponent<PopoverOutsideClick>().Owner = this;

        group = overlay.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var canvas = layer.GetComponent<Canvas>();
        uiCamera = canvas != null ? canvas.worldCamera : null;

        // panel 是真正可见的弹窗，内部所有元素都相对它左上角排布
        root = Rect("Panel", overlay, 0, 0, W, H);
        var bg = root.gameObject.AddComponent<Image>();
        bg.sprite = White;
        bg.type = Image.Type.Simple;
        bg.color = Theme.Paper;
        bg.raycastTarget = true;
        var edge = root.gameObject.AddComponent<Outline>();
        edge.effectColor = Theme.Border;
        edge.effectDistance = new Vector2(2, -2);

        Place(layer);
        BuildHeader();
        BuildPalette();
        BuildEditor();
        RefreshEditor();
        RefreshHint();
        StartCoroutine(OpenAnim());
    }

    // ---------------- 布局 ----------------

    void Place(RectTransform layer)
    {
        var cam = uiCamera;
        var fr = owner != null ? owner.Rect : null;

        float px = 0f, py = 0f;
        bool placed = false;
        if (fr != null)
        {
            Vector3 world = fr.TransformPoint(fr.rect.center);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, cam, out local))
            {
                float lw = layer.rect.width, lh = layer.rect.height;
                // local 以父层轴心为原点、y 向上；换算成距父层左上角的像素
                float fieldLeft = (local.x - fr.rect.width * .5f) + lw * .5f;
                float fieldTop = (lh * .5f - local.y) - fr.rect.height * .5f;
                float gap = 6f;
                float below = fieldTop + fr.rect.height + gap;
                float above = fieldTop - gap - H;
                py = (below + H <= lh - 8f) ? below : above;
                px = fieldLeft;
                placed = true;
            }
        }
        if (!placed) { px = (layer.rect.width - W) * .5f; py = 90f; }

        px = Mathf.Clamp(px, 8f, Mathf.Max(8f, layer.rect.width - W - 8f));
        py = Mathf.Clamp(py, 8f, Mathf.Max(8f, layer.rect.height - H - 8f));
        root.anchoredPosition = new Vector2(Mathf.Round(px), Mathf.Round(-py));
    }

    void BuildHeader()
    {
        bool tabbed = HasTabs;
        string ctx = owner != null ? owner.ContextLabel : "表达式";
        // 有页签时，正在改哪一格由亮着的那块页签说，标题再写一遍就是同一件事出现两次；
        // 只剩「表达式」三个字，右边腾出的位置给计数循环的顺序开关。
        var title = Text(root, tabbed ? "表达式" : "表达式 · " + ctx, 14, 9, Theme.Ink, 1);
        title.raycastTarget = false;
        headerTitle = title;

        if (!tabbed)
        {
            var tip = Text(root, "Esc 还原原值", W - 110, 9, Theme.Border, 1);
            tip.raycastTarget = false;
        }

        var line = Rect("Sep", root, 0, 30, W, 1);
        var img = line.gameObject.AddComponent<Image>();
        img.sprite = White; img.type = Image.Type.Simple; img.color = Theme.Border;
        img.raycastTarget = false;

        BuildTabs();
        BuildDirSwitch();
    }

    bool HasTabs { get { return owner != null && owner.TabGroup != null && owner.TabGroup.Length >= 2; } }

    // 一张卡上有好几个表达式字段时（计数循环的起点 / 终点），头部给一排页签直接换字段。
    // 页签按 owner.TabGroup 建，只有一个字段（或拿不到卡）时不建，头部跟以前一样。
    // 页签本身是这扇窗里最常点的两块，做到 104x28（原来 88x22），字距和点击热区一起放宽。
    void BuildTabs()
    {
        tabKeys = HasTabs ? owner.TabGroup : null;
        if (tabKeys == null) return;
        float x = TabX0;
        for (int i = 0; i < tabKeys.Length; i++)
        {
            string key = tabKeys[i];
            var tab = PixelButton.Create(root, TabText(key), () => SwitchField(key), (int)TabW, (int)TabH, IsTabActive(key), 1, true);
            tab.Rect.anchoredPosition = new Vector2(x, -TabY);
            headerTabs.Add(tab);
            x += TabW + TabGap;
        }
    }

    // 计数循环的顺序开关：改的是节点参数，不是这一栏的表达式草稿，所以点下去当场写回、
    // 卡面第一行立刻跟着变；参数窗里不再摆第二个同样的开关（见 App.BuildParamSpecs）。
    void BuildDirSwitch()
    {
        var node = owner != null && owner.Card != null ? owner.Card.Node : null;
        if (node == null || node.Type != "forloop") return;
        float x = DirLabelX;
        var lab = Text(root, "顺序", x, 9, Theme.WhaleDeep, 1);
        lab.raycastTarget = false;
        x += DirLabelW;
        dirChips.Clear();
        dirVals.Clear();
        AddDirChip(ref x, VarScope.DirAsc, node);
        AddDirChip(ref x, VarScope.DirDesc, node);
    }

    void AddDirChip(ref float x, string dir, GraphNode node)
    {
        string d = dir;
        var chip = PixelButton.Create(root, VarScope.DirLabelCn(d), () => ApplyDir(d), (int)DirChipW, (int)TabH, VarScope.DirOf(node) == d, 1, true);
        chip.Rect.anchoredPosition = new Vector2(x, -TabY);
        dirChips.Add(chip);
        dirVals.Add(d);
        x += DirChipW + TabGap;
    }

    void ApplyDir(string dir)
    {
        var node = owner != null && owner.Card != null ? owner.Card.Node : null;
        if (node == null || node.Type != "forloop" || VarScope.DirOf(node) == dir) return;
        if (App.I != null) App.I.SetNodeParam(node, "dir", dir);
        for (int i = 0; i < dirChips.Count; i++) dirChips[i].SetPrimary(dirVals[i] == dir);
        Touch();
        Debug.Log("[交互] 计数循环改为「" + VarScope.DirLabelCn(dir) + "」");
    }

    bool IsTabActive(string key)
    {
        return owner != null && owner.FieldKey == key;
    }

    // Tab 在这里换的是「正在编辑哪一格填空」：多字段模块（条件循环的条件、计数循环的步长）
    // 不用先关窗口回卡片上再点一次
    void TabToField(bool backward)
    {
        if (tabKeys == null || tabKeys.Length < 2 || owner == null) return;
        int at = System.Array.IndexOf(tabKeys, owner.FieldKey);
        if (at < 0) { SwitchField(tabKeys[0]); return; }
        SwitchField(tabKeys[NodeParamsUI.NextTabSlot(tabKeys.Length, at, backward)]);
    }

    string TabText(string key)
    {
        var card = owner != null ? owner.Card : null;
        string type = card != null && card.Node != null ? card.Node.Type : "";
        return NodeCard.ExprFieldTab(type, key);
    }

    // 切字段：先校验当前草稿，合法就原地写回旧字段（卡片上的读数立刻跟着变），再把浮窗接到新字段。
    // 有错时不切、也不清掉草稿——半截写完的表达式不该被静默丢掉。
    void SwitchField(string key)
    {
        if (closing || owner == null || IsTabActive(key)) return;
        string err = validate != null ? validate(draft) : null;
        if (!string.IsNullOrEmpty(err))
        {
            errorText = err;
            RefreshHint();
            return;
        }
        var card = owner.Card;
        var next = card != null ? card.GetExprField(key) : null;
        if (next == null) return;
        owner.OnPopoverResult(draft, true);
        owner = next;
        owner.AttachPopover(this);
        vars = next.Variables;
        validate = next.Validator;
        draft = next.Value;
        caret = anchor = draft.Length;
        errorText = "";
        if (headerTitle != null) headerTitle.Text = HasTabs ? "表达式" : "表达式 · " + owner.ContextLabel;
        for (int i = 0; i < headerTabs.Count; i++)
            headerTabs[i].SetPrimary(IsTabActive(tabKeys[i]));
        Touch();
    }

    void BuildPalette()
    {
        var strip = Rect("Palette", root, 0, 31, PaletteW, H - 31);
        var img = strip.gameObject.AddComponent<Image>();
        img.sprite = White; img.type = Image.Type.Simple; img.color = Theme.Salt; img.raycastTarget = true;

        Text(strip, "可用元素", 10, 8, Theme.Ink, 1).raycastTarget = false;
        Text(strip, "点击或拖到输入框", 10, 26, Theme.Border, 1).raycastTarget = false;

        const float headerH = 44f;
        paletteView = Rect("Scroll", strip, 0, headerH, PaletteW - 6f, H - 31f - headerH - 4f);
        paletteView.gameObject.AddComponent<RectMask2D>();
        paletteContent = Rect("Content", paletteView, 2, 0, PaletteW - 6f, 1f);
        strip.gameObject.AddComponent<ExprPaletteScroll>().Host = this;

        var scalars = new List<ExprVariableInfo>();
        var arrays = new List<ExprVariableInfo>();
        var structs = new List<ExprVariableInfo>();
        foreach (var v in vars)
        {
            if (v == null || string.IsNullOrEmpty(v.Name)) continue;
            if (v.IsArray) arrays.Add(v);
            else if (v.Members.Count > 0) structs.Add(v);
            else scalars.Add(v);
        }

        flowX = 6f; flowY = 2f;
        AddGroup("变量", scalars, false, "当前作用域没有变量");
        AddGroup("数组", arrays, true);
        AddGroup("数据结构", structs, true);
        AddFnGroup("数学函数", Dayu.Core.MathFns.PaletteNames, Theme.WhaleLight);
        AddFnGroup("常量", Dayu.Core.MathFns.PaletteConsts, Theme.Yellow);

        float contentH = Mathf.Max(paletteView.rect.height, flowY + 24f);
        paletteContent.sizeDelta = new Vector2(PaletteW - 6f, contentH);
        paletteMax = Mathf.Max(0f, contentH - paletteView.rect.height);
        if (paletteMax > 0f)
        {
            paletteThumb = Rect("Thumb", strip, PaletteW - 5f, headerH, 3f, 1f);
            var thumbImg = paletteThumb.gameObject.AddComponent<Image>();
            thumbImg.sprite = White; thumbImg.type = Image.Type.Simple; thumbImg.color = Theme.Border;
            thumbImg.raycastTarget = false;
        }
        ApplyPaletteScroll();

        var edgeImg = Rect("PaletteEdge", root, PaletteW, 31, 1, H - 31).gameObject.AddComponent<Image>();
        edgeImg.sprite = White; edgeImg.type = Image.Type.Simple; edgeImg.color = Theme.Border;
        edgeImg.raycastTarget = false;
    }

    // 一组元素：标题 + 每个变量一颗名字芯片，再跟上它的成员芯片
    void AddGroup(string title, List<ExprVariableInfo> list, bool withMembers, string emptyHint = null)
    {
        if (list.Count == 0)
        {
            if (string.IsNullOrEmpty(emptyHint)) return;
            flowX = 6f;
            if (flowY > 2f) flowY += 6f;
            Text(paletteContent, title, 6f, flowY, Theme.GrapeDeep, 1).raycastTarget = false;
            flowY += 16f;
            Text(paletteContent, emptyHint, 6f, flowY, Theme.Border, 1).raycastTarget = false;
            flowX = 6f;
            flowY += 24f;
            return;
        }
        if (flowY > 2f) flowY += 6f;
        flowX = 6f;
        Text(paletteContent, title, 6f, flowY, Theme.GrapeDeep, 1).raycastTarget = false;
        flowY += 16f;
        foreach (var v in list)
        {
            if (v.IsArray)
                AddFlowChip(v.Name + "[]", v.Name + "[]", v.Name.Length + 1, Theme.Mint, Theme.Ink);
            else
                AddFlowChip(v.Name, v.Name, v.Name.Length, Theme.Honey, Theme.Ink);

            if (!withMembers) continue;
            foreach (var m in v.Members)
            {
                if (string.IsNullOrEmpty(m)) continue;
                string full = v.Name + "." + m;
                AddFlowChip(full, full, full.Length, Theme.Cream, Theme.GrapeDeep);
            }
        }
        flowX = 6f;
        flowY += 24f;
    }

    // 数学函数 / 常量：不随作用域变，每个浮窗都有，点一下就把「名字()」插进去、光标落在括号里
    void AddFnGroup(string title, string[] names, Color fill)
    {
        if (names == null || names.Length == 0) return;
        if (flowY > 2f) flowY += 6f;
        flowX = 6f;
        Text(paletteContent, title, 6f, flowY, Theme.GrapeDeep, 1).raycastTarget = false;
        flowY += 16f;
        foreach (var n in names)
        {
            bool fn = Dayu.Core.MathFns.IsMathFn(n);
            AddFlowChip(n, fn ? n + "()" : n, fn ? n.Length + 1 : n.Length, fill, Theme.Ink);
        }
        flowX = 6f;
        flowY += 24f;
    }

    void AddFlowChip(string label, string snippet, int caretWithin, Color fill, Color fg)
    {
        float w = PixelText.MeasureString(label, 1).x + 12f;
        float innerRight = paletteView.rect.width - 10f;
        if (flowX + w > innerRight && flowX > 6f) { flowX = 6f; flowY += 24f; }

        var rt = Rect("Chip_" + label, paletteContent, flowX, flowY, w, 22f);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = White; img.type = Image.Type.Simple; img.color = fill; img.raycastTarget = true;

        var t = Text(rt, label, 0, 4, fg, 1);
        var trt = t.rectTransform;
        trt.sizeDelta = new Vector2(w, 22f);
        t.Align = PixelText.TextAlign.Center;
        t.raycastTarget = false;

        var chip = rt.gameObject.AddComponent<ExprChip>();
        chip.Label = label;
        chip.Snippet = snippet;
        chip.CaretWithin = caretWithin;
        chip.Fill = fill;
        chip.Foreground = fg;
        chip.Host = this;

        flowX += w + 4f;
    }

    internal void ScrollPalette(float delta)
    {
        if (paletteMax <= 0f) return;
        paletteScroll = Mathf.Clamp(paletteScroll - delta * 24f, 0f, paletteMax);
        ApplyPaletteScroll();
    }

    void ApplyPaletteScroll()
    {
        if (paletteContent != null)
            paletteContent.anchoredPosition = new Vector2(2f, -paletteScroll);
        if (paletteThumb != null && paletteView != null)
        {
            float viewH = paletteView.rect.height;
            float thumbH = Mathf.Max(18f, viewH * viewH / (viewH + paletteMax));
            float travel = viewH - thumbH;
            float top = 44f + (paletteMax > 0f ? travel * (paletteScroll / paletteMax) : 0f);
            paletteThumb.anchoredPosition = new Vector2(PaletteW - 5f, -top);
            paletteThumb.sizeDelta = new Vector2(3f, thumbH);
        }
    }

    void BuildEditor()
    {
        var box = Rect("Input", root, InputX, InputY, InputW, InputH);
        var img = box.gameObject.AddComponent<Image>();
        img.sprite = White; img.type = Image.Type.Simple; img.color = Theme.White; img.raycastTarget = true;
        var ol = box.gameObject.AddComponent<Outline>();
        ol.effectColor = Theme.Border;
        ol.effectDistance = new Vector2(1, -1);
        inputOutline = ol;

        viewport = Rect("Viewport", box, 6f, 0f, InputW - 12f, InputH);
        viewport.gameObject.AddComponent<RectMask2D>();

        // 选中高亮垫在文字下面，光标在最上层
        highlightRect = Rect("Selection", viewport, 0f, CaretTop, 0f, CaretH);
        var hl = highlightRect.gameObject.AddComponent<Image>();
        hl.sprite = White; hl.type = Image.Type.Simple;
        hl.color = new Color(Theme.Whale.r / 255f, Theme.Whale.g / 255f, Theme.Whale.b / 255f, .28f);
        hl.raycastTarget = false;

        inputText = PixelText.Create(viewport, "", Theme.Ink, 1);
        var trt = inputText.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
        trt.pivot = new Vector2(0f, 1f);
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = new Vector2(2000f, InputH);
        inputText.Align = PixelText.TextAlign.Left;
        inputText.raycastTarget = false;

        caretRect = Rect("Caret", viewport, 0f, CaretTop, 2f, CaretH);
        caretImage = caretRect.gameObject.AddComponent<Image>();
        caretImage.sprite = White; caretImage.type = Image.Type.Simple; caretImage.color = Theme.Ink;
        caretImage.raycastTarget = false;

        // 拖拽落点竖线：比光标粗一档，深海蓝，只在按住芯片拖动时出现
        dropBarRect = Rect("DropBar", viewport, 0f, CaretTop - 2f, 3f, CaretH + 4f);
        dropBarImage = dropBarRect.gameObject.AddComponent<Image>();
        dropBarImage.sprite = White; dropBarImage.type = Image.Type.Simple; dropBarImage.color = Theme.Whale;
        dropBarImage.raycastTarget = false;
        dropBarRect.gameObject.SetActive(false);

        hintText = Text(root, "", InputX, InputY + InputH + 8f, Theme.Whale, 1);
        hintText.raycastTarget = false;
        hintText.rectTransform.sizeDelta = new Vector2(InputW, 14f);

        helpText = Text(root, "Enter 或点「确定」提交 · Esc / 点窗外 / 点「取消」还原原值\n左右键移动光标，Home / End 跳到首尾\nBackspace / Delete 删除，Ctrl+A 全选\nCtrl+C 复制，Ctrl+V 粘贴\n变量名后打 . 出成员列表 · 上下键选择 · Tab 补全", InputX, InputY + InputH + 32f, Theme.Border, 1);
        helpText.Wrap = true;
        helpText.WrapWidth = (int)InputW;
        helpText.rectTransform.sizeDelta = new Vector2(InputW, 84f);
        helpText.raycastTarget = false;

        // 之前这个浮窗只有 Enter 能提交，玩家找不到「点哪里才算写完」。补上两个按钮，
        // 按下即触发（clickOnDown）：轻微拖动或焦点切换都不会把 click 吞掉。
        // 帮助文案有 5 行、首行还会折行，占满 InputY+InputH+32 往下 84 像素（到 184）。
        // 按钮中心原先落在 184，正好把最后一行「变量名后打 .」拦腰压住，往下挪到文案块之下。
        const float btnY = InputY + InputH + 142f;
        var ok = PixelButton.Create(root, "确定", Submit, 110, 32, true, 2, true);
        ok.Rect.anchoredPosition = new Vector2(InputX, -btnY);
        var no = PixelButton.Create(root, "取消", () => Close(false), 110, 32, false, 2, true);
        no.Rect.anchoredPosition = new Vector2(InputX + 120f, -btnY);
    }

    // ---------------- 编辑 ----------------

    int SelStart { get { return Mathf.Min(caret, anchor); } }
    int SelEnd { get { return Mathf.Max(caret, anchor); } }
    bool HasSelection { get { return caret != anchor; } }

    void DeleteSelection()
    {
        if (!HasSelection) return;
        int a = SelStart, b = SelEnd;
        draft = draft.Remove(a, b - a);
        caret = anchor = a;
    }

    void InsertSnippet(string s, int caretWithin)
    {
        if (string.IsNullOrEmpty(s)) return;
        DeleteSelection();
        int at = Mathf.Clamp(caret, 0, draft.Length);
        draft = draft.Insert(at, s);
        caret = anchor = Mathf.Clamp(at + caretWithin, 0, draft.Length);
        errorText = "";
        RefreshEditor();
        RefreshHint();
    }

    // ---------------- 元素芯片拖拽 ----------------

    internal void InsertChip(ExprChip chip)
    {
        if (chip == null) return;
        InsertSnippet(chip.Snippet, chip.CaretWithin);
    }

    internal void BeginChipDrag(ExprChip chip, Vector2 screenPos)
    {
        if (chip == null || overlay == null) return;
        dragChip = chip;
        EnsureGhost(chip);
        MoveGhost(screenPos);
        SetDropTarget(-1);
        if (hintText != null)
        {
            hintText.color = Theme.Whale;
            hintText.Text = "拖到输入框内选落点";
        }
    }

    internal void MoveChipDrag(Vector2 screenPos)
    {
        if (dragChip == null) return;
        MoveGhost(screenPos);
        int index;
        SetDropTarget(TryMapDropIndex(screenPos, out index) ? index : -1);
    }

    internal void EndChipDrag(Vector2 screenPos)
    {
        if (dragChip == null) return;
        ExprChip chip = dragChip;
        dragChip = null;
        int index;
        bool hit = TryMapDropIndex(screenPos, out index);
        DestroyGhost();
        SetDropTarget(-1);
        if (!hit) { Touch(); return; }
        caret = anchor = index;
        InsertSnippet(chip.Snippet, chip.CaretWithin);
    }

    void SetDropTarget(int index)
    {
        if (dropBarRect == null) return;
        if (index < 0)
        {
            dropBarRect.gameObject.SetActive(false);
            if (inputOutline != null) inputOutline.effectColor = Theme.Border;
            return;
        }
        float x = Width(draft.Substring(0, Mathf.Clamp(index, 0, draft.Length))) - scrollX;
        dropBarRect.gameObject.SetActive(true);
        dropBarRect.anchoredPosition = new Vector2(x, -(CaretTop - 2f));
        if (inputOutline != null) inputOutline.effectColor = Theme.Whale;
    }

    // 落点判定给 14px 余量，贴着输入框边缘松手也算命中
    bool TryMapDropIndex(Vector2 screenPos, out int index)
    {
        index = -1;
        if (viewport == null) return false;
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPos, uiCamera, out lp)) return false;
        UnityEngine.Rect rr = viewport.rect;
        const float slack = 14f;
        if (lp.x < rr.xMin - slack || lp.x > rr.xMax + slack) return false;
        if (lp.y < rr.yMin - slack || lp.y > rr.yMax + slack) return false;
        index = CaretIndexAt(Mathf.Clamp(lp.x - rr.xMin + scrollX, 0f, Width(draft)));
        return true;
    }

    void EnsureGhost(ExprChip chip)
    {
        if (dragGhost == null)
        {
            var go = new GameObject("ChipGhost", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(overlay, false);
            dragGhost = (RectTransform)go.transform;
            dragGhost.anchorMin = dragGhost.anchorMax = new Vector2(0f, 1f);
            dragGhost.pivot = new Vector2(0f, 1f);
            dragGhostImage = go.GetComponent<Image>();
            dragGhostImage.sprite = White;
            dragGhostImage.type = Image.Type.Simple;
            dragGhostImage.raycastTarget = false;
            var ol = go.AddComponent<Outline>();
            ol.effectDistance = new Vector2(2, -2);
            ol.effectColor = chip.Foreground;

            dragGhostLabel = PixelText.Create(dragGhost, "", chip.Foreground, 1);
            var t = dragGhostLabel.rectTransform;
            t.anchorMin = t.anchorMax = new Vector2(0f, 1f);
            t.pivot = new Vector2(0f, 1f);
            t.anchoredPosition = new Vector2(6f, -4f);
            t.sizeDelta = new Vector2(300f, 22f);
            dragGhostLabel.Align = PixelText.TextAlign.Left;
            dragGhostLabel.raycastTarget = false;
        }
        float w = PixelText.MeasureString(chip.Label, 1).x + 12f;
        dragGhost.sizeDelta = new Vector2(w, 22f);
        dragGhostImage.color = chip.Fill;
        dragGhostLabel.color = chip.Foreground;
        dragGhostLabel.Text = chip.Label;
        dragGhost.gameObject.SetActive(true);
        dragGhost.SetAsLastSibling();
    }

    void MoveGhost(Vector2 screenPos)
    {
        if (dragGhost == null) return;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, screenPos, uiCamera, out local)) return;
        dragGhost.anchoredPosition = new Vector2(local.x + 10f, local.y - 14f);
    }

    void DestroyGhost()
    {
        if (dragGhost != null) Destroy(dragGhost.gameObject);
        dragGhost = null;
        dragGhostImage = null;
        dragGhostLabel = null;
    }

    // ---------------- -chipdrag 脚本演示 ----------------
    // 事件一律经 EventSystem 派发给芯片，和真实鼠标走同一条路：
    // 芯片没接住按下/拖动/松手，这里就复现不出来。

    PointerEventData demoPed;
    ExprChip demoChip;

    internal bool DemoPressChip(string label)
    {
        var es = EventSystem.current;
        ExprChip chip = FindChip(label);
        if (es == null || chip == null) return false;
        demoPed = new PointerEventData(es) { pointerId = 98, position = ScreenPointOf((RectTransform)chip.transform) };
        demoChip = chip;
        ExecuteEvents.Execute(chip.gameObject, demoPed, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(chip.gameObject, demoPed, ExecuteEvents.dragHandler);
        return true;
    }

    internal void DemoDragChipTo(int index)
    {
        if (demoPed == null || demoChip == null) return;
        demoPed.position = ScreenPointOfDrop(index);
        ExecuteEvents.Execute(demoChip.gameObject, demoPed, ExecuteEvents.dragHandler);
    }

    internal void DemoReleaseChip(int index)
    {
        if (demoPed == null || demoChip == null) return;
        demoPed.position = ScreenPointOfDrop(index);
        ExecuteEvents.Execute(demoChip.gameObject, demoPed, ExecuteEvents.pointerUpHandler);
        demoPed = null;
        demoChip = null;
    }

    // 按下后只挪 2px 就松手，落在拖拽阈值内 → 走点击插入分支
    internal bool DemoClickChip(string label)
    {
        if (!DemoPressChip(label)) return false;
        Vector2 start = demoPed.position;
        demoPed.position = start + new Vector2(2f, 0f);
        ExecuteEvents.Execute(demoChip.gameObject, demoPed, ExecuteEvents.pointerUpHandler);
        demoPed = null;
        demoChip = null;
        return true;
    }

    internal void DemoScrollPalette(float wheel)
    {
        ScrollPalette(wheel);
    }

    internal void DemoSubmit()
    {
        Submit();
    }

    internal string DemoDraft { get { return draft; } }

    ExprChip FindChip(string label)
    {
        if (paletteContent == null) return null;
        foreach (Transform child in paletteContent)
        {
            var c = child.GetComponent<ExprChip>();
            if (c != null && c.Label == label) return c;
        }
        return null;
    }

    static Vector2 ScreenPointOf(RectTransform rt)
    {
        return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
    }

    Vector2 ScreenPointOfDrop(int index)
    {
        if (viewport == null) return Vector2.zero;
        float x = Width(draft.Substring(0, Mathf.Clamp(index, 0, draft.Length))) - scrollX;
        Vector3 world = viewport.TransformPoint(new Vector2(x, viewport.rect.center.y));
        return RectTransformUtility.WorldToScreenPoint(uiCamera, world);
    }

    void Touch()
    {
        blink = 0f;
        errorText = "";
        RefreshEditor();
        RefreshHint();
    }

    void Submit()
    {
        if (owner == null) { Close(true); return; }
        string err = validate != null ? validate(draft) : null;
        if (!string.IsNullOrEmpty(err))
        {
            errorText = err;
            RefreshHint();
            return;
        }
        Close(true);
    }

    void Close(bool commit)
    {
        if (closing) return;
        closing = true;
        if (Current == this) Current = null;
        if (blocker != null) Destroy(blocker.gameObject);
        if (owner != null) owner.OnPopoverResult(draft, commit);
        StartCoroutine(CloseAnim());
    }

    // 点浮窗外任意一点：不写回，等于还原原值（和 Esc 同一条路）
    public void CancelFromOutside()
    {
        Debug.Log("[交互] 表达式浮窗被点窗外，还原原值");
        Close(false);
    }

    float Width(string s)
    {
        return string.IsNullOrEmpty(s) ? 0f : PixelText.MeasureString(s, 1).x;
    }

    void RefreshEditor()
    {
        if (inputText == null) return;
        float caretX = Width(draft.Substring(0, Mathf.Clamp(caret, 0, draft.Length)));
        float maxX = viewport.rect.width - 3f;
        if (maxX < 1f) maxX = 1f;
        if (caretX - scrollX > maxX) scrollX = caretX - maxX;
        if (caretX - scrollX < 0f) scrollX = caretX;
        if (scrollX < 0f) scrollX = 0f;

        inputText.Text = draft;
        inputText.rectTransform.anchoredPosition = new Vector2(-scrollX, 0f);
        caretRect.anchoredPosition = new Vector2(caretX - scrollX, -CaretTop);

        if (HasSelection)
        {
            float a = Width(draft.Substring(0, SelStart));
            float b = Width(draft.Substring(0, SelEnd));
            highlightRect.gameObject.SetActive(true);
            highlightRect.anchoredPosition = new Vector2(a - scrollX, -CaretTop);
            highlightRect.sizeDelta = new Vector2(Mathf.Max(2f, b - a), CaretH);
        }
        else highlightRect.gameObject.SetActive(false);

        blink = 0f;
        // 文本或光标每动一次都重新问一遍候选：这一句是补全浮窗唯一的存活判据，
        // 免得打字、删除、点选、换字段哪条路忘了收，列表就挂在屏幕上不走
        SyncCompletion();
    }

    string GuessType(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "—";
        if (s.IndexOf("==") >= 0 || s.IndexOf("!=") >= 0 || s.IndexOf("<=") >= 0 || s.IndexOf(">=") >= 0) return "bool";
        // 位运算结果仍是整数，先于下面的 < > 判断，免得把 a << 3 猜成 bool
        if (s.IndexOf("<<") >= 0 || s.IndexOf(">>") >= 0 || s.IndexOf("&") >= 0 || s.IndexOf("|") >= 0
            || s.IndexOf("^") >= 0 || s.IndexOf("~") >= 0) return "int";
        if (s.IndexOf("<") >= 0 || s.IndexOf(">") >= 0) return "bool";
        // 成员访问按词表答：st.top 返回的是元素类型，不是一句 int 能盖住的
        int dot = s.LastIndexOf('.');
        if (dot > 0 && vars != null)
        {
            var head = s.Substring(0, dot);
            foreach (var v in vars)
                if (v != null && v.Name == head)
                {
                    var rt = Members.ReturnType(v.Type, s.Substring(dot + 1));
                    if (!string.IsNullOrEmpty(rt)) return rt;
                }
        }
        if (s.EndsWith(".length") || s.EndsWith(".size")) return "int";
        if (s.EndsWith(".empty")) return "bool";
        if (vars != null)
            foreach (var v in vars)
                if (v != null && v.Name == s) return v.Type;
        return "int";
    }

    void RefreshHint()
    {
        if (hintText == null) return;
        string err = errorText;
        if (string.IsNullOrEmpty(err) && validate != null && draft.Length > 0) err = validate(draft);
        if (!string.IsNullOrEmpty(err))
        {
            hintText.color = Theme.Coral;
            hintText.Text = ExpressionFieldUI.Ellipsize("✗ " + err, InputW);
        }
        else if (draft.Length == 0)
        {
            hintText.color = Theme.Border;
            hintText.Text = "空表达式 · 用左侧元素或直接输入";
        }
        else
        {
            hintText.color = Theme.Whale;
            hintText.Text = "✓ 类型：" + GuessType(draft);
        }
    }

    int CaretIndexAt(float localX)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i <= draft.Length; i++)
        {
            float w = Width(draft.Substring(0, i));
            float d = Mathf.Abs(w - localX);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    // ---------------- 成员补全浮窗 ----------------

    bool CompletionOpen { get { return completionPanel != null && completionItems != null && completionItems.Count > 0; } }

    // 候选空了就收，变了就重建，没变只挪高亮：同一个表达式里可能有好几个变量，
    // 这里只认光标所在那一段「变量名.前缀」，别的变量的成员一颗都不列
    void SyncCompletion()
    {
        var items = Candidates(draft, caret, vars);
        if (items.Count == 0) { if (completionItems != null) DestroyCompletion(); return; }
        if (SameCandidates(items))
        {
            completionSel = Mathf.Clamp(completionSel, 0, items.Count - 1);
            PlaceCompletion();
            HighlightCompletion();
            return;
        }
        BuildCompletion(items, VarNameAt(draft, caret));
    }

    bool SameCandidates(List<MemberDef> items)
    {
        if (completionItems == null || completionItems.Count != items.Count) return false;
        for (int i = 0; i < items.Count; i++)
        {
            var a = completionItems[i];
            var b = items[i];
            if (a == null || b == null || a.Token != b.Token) return false;
        }
        return true;
    }

    void BuildCompletion(List<MemberDef> items, string varName)
    {
        DestroyCompletion();
        completionItems = items;
        completionVar = varName;
        completionSel = 0;

        float tokenW = 0f, glossW = 0f;
        foreach (var m in items)
        {
            if (m == null) continue;
            tokenW = Mathf.Max(tokenW, Width(m.Token));
            glossW = Mathf.Max(glossW, Width(m.Gloss));
        }
        const string hint = "上下键选择 · Tab 补全 · Esc 关闭";
        float w = Mathf.Max(CompPadX * 2f + tokenW + CompTokenGap + glossW, CompPadX * 2f + Width(hint));
        float h = CompTop + items.Count * CompRowH + CompFootGap + CompFootH + CompBottom;

        // 挂在面板（root）里面而不是挡板上：挡板的「点窗外 = 取消」会把整扇窗带走
        var go = new GameObject("Completion", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root, false);
        completionPanel = (RectTransform)go.transform;
        completionPanel.anchorMin = completionPanel.anchorMax = new Vector2(0f, 1f);
        completionPanel.pivot = new Vector2(0f, 1f);
        completionPanel.sizeDelta = new Vector2(w, h);
        var bg = go.GetComponent<Image>();
        bg.sprite = White; bg.type = Image.Type.Simple; bg.color = Theme.White; bg.raycastTarget = true;
        var edge = go.AddComponent<Outline>();
        edge.effectColor = Theme.Border;
        edge.effectDistance = new Vector2(1, -1);

        var fade = go.AddComponent<CanvasGroup>();
        // 补间纪律同 GraphView：截图模式（InstantView）直接落终态，不留半透明中间帧
        if (InstantMode()) fade.alpha = 1f;
        else StartCoroutine(Tween.Fade(this, fade, 0f, 1f, .08f));

        completionSelection = Rect("Selected", completionPanel, 1f, CompTop, w - 2f, CompRowH)
            .gameObject.AddComponent<Image>();
        completionSelection.sprite = White;
        completionSelection.type = Image.Type.Simple;
        completionSelection.color = Theme.A(Theme.WhaleLight, .55f);
        completionSelection.raycastTarget = false;

        float glossX = CompPadX + tokenW + CompTokenGap;
        for (int i = 0; i < items.Count; i++)
        {
            var m = items[i];
            if (m == null) continue;
            var row = Rect("Row_" + m.Token, completionPanel, 0f, CompTop + i * CompRowH, w, CompRowH);
            // 一行一颗也能点：热区是透明图，画在文字底下不动视觉
            var hit = row.gameObject.AddComponent<Image>();
            hit.sprite = White; hit.type = Image.Type.Simple; hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;
            Text(row, m.Token, CompPadX, 3f, Theme.Ink, 1).raycastTarget = false;
            Text(row, m.Gloss, glossX, 3f, Theme.WhaleDeep, 1).raycastTarget = false;
            var click = row.gameObject.AddComponent<CompletionRowClick>();
            click.Host = this;
            click.Index = i;
        }

        Text(completionPanel, hint, CompPadX, CompTop + items.Count * CompRowH + CompFootGap, Theme.Border, 1)
            .raycastTarget = false;

        PlaceCompletion();
        HighlightCompletion();
        completionPanel.SetAsLastSibling();
        // 列表就摆在输入行下面，正压在「✗ 报错」那一行和按键说明上，字被拦腰截断。
        // 浮窗开着时这两行先退场，关掉（Esc / 补完 / 候选空了）再还回来。
        if (hintText != null) hintText.gameObject.SetActive(false);
        if (helpText != null) helpText.gameObject.SetActive(false);
    }

    // 浮窗落在输入行下面，横坐标跟着光标走：压住编辑行就等于看不见自己刚打的东西
    void PlaceCompletion()
    {
        if (completionPanel == null || root == null) return;
        float maxW = root.rect.width - 12f;
        float w = Mathf.Min(completionPanel.sizeDelta.x, maxW);
        completionPanel.sizeDelta = new Vector2(w, completionPanel.sizeDelta.y);
        float head = Width(draft.Substring(0, Mathf.Clamp(caret, 0, draft.Length)));
        float x = Mathf.Clamp(InputX + 6f + head - scrollX, 6f, Mathf.Max(6f, root.rect.width - w - 6f));
        float y = InputY + InputH + 3f;
        completionPanel.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
    }

    void HighlightCompletion()
    {
        if (completionSelection == null || completionItems == null || completionItems.Count == 0) return;
        completionSel = Mathf.Clamp(completionSel, 0, completionItems.Count - 1);
        completionSelection.rectTransform.anchoredPosition = new Vector2(1f, -(CompTop + completionSel * CompRowH));
    }

    void DestroyCompletion()
    {
        completionItems = null;
        completionVar = "";
        completionSel = 0;
        if (completionPanel != null) Destroy(completionPanel.gameObject);
        completionPanel = null;
        completionSelection = null;
        if (hintText != null) hintText.gameObject.SetActive(true);
        if (helpText != null) helpText.gameObject.SetActive(true);
    }

    void MoveCompletion(int step)
    {
        int n = completionItems.Count;
        completionSel = (completionSel + step + n) % n;   // 首尾相接，玩家不用先按到列表头
        HighlightCompletion();
        Debug.Log("[补全] 高亮第 " + (completionSel + 1) + "/" + n + " 颗：" + completionItems[completionSel].Token);
    }

    bool AcceptCompletion()
    {
        if (!CompletionOpen) return false;
        CompleteAt(completionSel);
        return true;
    }

    internal void CompleteAt(int index)
    {
        // 淡出那 0.12 秒里浮窗还看得见，点它不该再往已经交回的草稿里插字
        if (closing || !CompletionOpen || index < 0 || index >= completionItems.Count) return;
        var m = completionItems[index];
        if (m == null) { DestroyCompletion(); return; }
        // 玩家手打的那半截前缀要连着换掉：光标停在 a.si| 时补的是 size，
        // 只在光标后面插就会写成 a.sisize
        string accName, accPrefix;
        int typed = TryMemberAccess(draft, caret, out accName, out accPrefix) ? accPrefix.Length : 0;
        string varName = typed > 0 ? accName : completionVar;
        int total = completionItems.Count;
        DestroyCompletion();
        // 把前缀划成选区，InsertSnippet 头一步就是删选区
        if (typed > 0) anchor = Mathf.Max(0, caret - typed);
        InsertSnippet(m.Token, m.Token.Length);
        Debug.Log("[补全] " + varName + "." + m.Token + "（" + m.Gloss + "，候选 " + total + " 颗）");
    }

    // 截图/自测模式下所有补间直接落终态：半透明中间帧会让截图核不出字
    static bool InstantMode()
    {
        var app = App.I;
        return app != null && app.GraphCanvas != null && app.GraphCanvas.InstantView;
    }

    // ---------------- 成员补全判定（纯函数，Editor 自测直接调） ----------------

    // 截图验收用的替手：把草稿直接摆成「st.」这种半截样子，省掉真键盘逐字敲
    internal void DemoSetDraft(string text, int at)
    {
        draft = text ?? "";
        caret = Mathf.Clamp(at, 0, draft.Length);
        anchor = caret;
        errorText = "";
        RefreshEditor();
        RefreshHint();
    }

    /// <summary>
    /// 光标落在「变量名.前缀」里时，给出这个变量能补的成员；其余情况一律空表：
    /// 点号前面是未知标识符、标量变量（Members.Of 给不出成员）、光标在点号之前、
    /// 前缀已经是一整颗成员 —— 都没什么可补的。
    /// </summary>
    public static List<MemberDef> Candidates(string draft, int caret, List<ExprVariableInfo> vars)
    {
        var list = new List<MemberDef>();
        string name, prefix;
        if (!TryMemberAccess(draft, caret, out name, out prefix)) return list;
        var v = FindVariable(vars, name);
        if (v == null) return list;
        // 补完了还挂着，只会压住光标后面的字
        if (Members.Find(v.Type, prefix) != null) return list;
        foreach (var m in Members.Of(v.Type))
            if (m != null && (prefix.Length == 0 || m.Token.StartsWith(prefix, StringComparison.Ordinal))) list.Add(m);
        return list;
    }

    // 光标左边最近的那个 `.` 决定在补谁：点号到光标之间只能是还没打完的成员前缀，
    // 点号左边必须是一整个标识符（a.size 再打一个点，那段就叫 size，不是变量）
    static bool TryMemberAccess(string draft, int caret, out string name, out string prefix)
    {
        name = null;
        prefix = null;
        if (draft == null || caret <= 0 || caret > draft.Length) return false;
        int dot = -1;
        for (int i = caret - 1; i >= 0; i--)
        {
            if (draft[i] == '.') { dot = i; break; }
            if (!IsIdentPart(draft[i])) return false;
        }
        if (dot < 0) return false;
        prefix = draft.Substring(dot + 1, caret - dot - 1);
        int start = dot;
        while (start > 0 && IsIdentPart(draft[start - 1])) start--;
        if (start == dot) return false;
        name = draft.Substring(start, dot - start);
        return true;
    }

    static string VarNameAt(string draft, int caret)
    {
        string name, prefix;
        return TryMemberAccess(draft, caret, out name, out prefix) ? name : "";
    }

    // 标识符规则要跟 ExprLang 的词法一致，否则浮窗列出来的东西编译器不认
    static bool IsIdentPart(char c) { return c == '_' || char.IsLetterOrDigit(c); }

    static ExprVariableInfo FindVariable(List<ExprVariableInfo> vars, string name)
    {
        if (vars == null || string.IsNullOrEmpty(name)) return null;
        foreach (var v in vars)
            if (v != null && v.Name == name) return v;
        return null;
    }

    // ---------------- 输入 ----------------

    void Update()
    {
        if (closing) return;

        blink += Time.unscaledDeltaTime;
        if (caretImage != null)
        {
            Color c = Theme.Ink;
            c.a = 0.15f + 0.85f * ((blink % 1f) < .55f ? 1f : 0f);
            caretImage.color = c;
        }

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 列表开着时 Esc 只退列表：草稿还在玩家手里，不该顺手把整扇窗还原成原值
            if (CompletionOpen) { DestroyCompletion(); return; }
            Close(false);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            // 回车先吞掉一次「采纳候选」，否则半截表达式会被当成写完提交掉
            if (AcceptCompletion()) return;
            Submit();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // 列表可见时 Tab 归补全，不再跳到下一个填空
            if (AcceptCompletion()) return;
            TabToField(shift);
            return;
        }

        if (ctrl)
        {
            if (Input.GetKeyDown(KeyCode.A))
            {
                anchor = 0; caret = draft.Length;
                blink = 0f;
                RefreshEditor();
                return;
            }
            if (Input.GetKeyDown(KeyCode.C))
            {
                GUIUtility.systemCopyBuffer = HasSelection ? draft.Substring(SelStart, SelEnd - SelStart) : draft;
                return;
            }
            if (Input.GetKeyDown(KeyCode.V))
            {
                var clip = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(clip))
                {
                    clip = clip.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
                    InsertSnippet(clip, clip.Length);
                }
                return;
            }
        }

        // 列表可见时上下键归补全，左右键照旧动光标：光标挪出成员那一段由 SyncCompletion 收尾
        bool stepDown = Input.GetKeyDown(KeyCode.DownArrow);
        bool stepUp = Input.GetKeyDown(KeyCode.UpArrow);
        if (CompletionOpen && (stepDown || stepUp))
        {
            MoveCompletion(stepDown ? 1 : -1);
            return;
        }

        bool changed = false;
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            if (HasSelection && !shift) { caret = anchor = SelStart; }
            else caret = Mathf.Max(0, caret - 1);
            if (!shift) anchor = caret;
            changed = true;
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (HasSelection && !shift) { caret = anchor = SelEnd; }
            else caret = Mathf.Min(draft.Length, caret + 1);
            if (!shift) anchor = caret;
            changed = true;
        }
        if (Input.GetKeyDown(KeyCode.Home)) { caret = anchor = 0; changed = true; }
        if (Input.GetKeyDown(KeyCode.End)) { caret = anchor = draft.Length; changed = true; }

        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            if (HasSelection) DeleteSelection();
            else if (caret > 0) { draft = draft.Remove(caret - 1, 1); caret = anchor = caret - 1; }
            changed = true;
        }
        if (Input.GetKeyDown(KeyCode.Delete))
        {
            if (HasSelection) DeleteSelection();
            else if (caret < draft.Length) draft = draft.Remove(caret, 1);
            changed = true;
        }

        foreach (char c in Input.inputString)
        {
            if (c < ' ') continue;
            DeleteSelection();
            int at = Mathf.Clamp(caret, 0, draft.Length);
            draft = draft.Insert(at, c.ToString());
            caret = anchor = at + 1;
            changed = true;
        }

        if (changed && !string.IsNullOrEmpty(errorText)) errorText = "";
        if (changed) Touch();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (viewport == null) return;
        var cam = e.pressEventCamera;
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, e.position, cam, out lp)) return;
        if (!viewport.rect.Contains(lp)) return;
        float x = lp.x - viewport.rect.xMin + scrollX;
        caret = anchor = CaretIndexAt(x);
        blink = 0f;
        RefreshEditor();
    }

    // ---------------- 动画 ----------------

    System.Collections.IEnumerator OpenAnim()
    {
        float t = 0f;
        while (t < .16f)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / .16f);
            if (group != null) group.alpha = k;
            root.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }
        if (group != null) group.alpha = 1f;
        root.localScale = Vector3.one;
    }

    System.Collections.IEnumerator CloseAnim()
    {
        float t = 0f;
        while (t < .12f)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / .12f);
            if (group != null) group.alpha = 1f - k;
            root.localScale = Vector3.one * Mathf.Lerp(1f, .95f, k);
            yield return null;
        }
        Destroy(gameObject);
    }

    // ---------------- UI 工具 ----------------

    static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    static PixelText Text(Transform parent, string s, float x, float y, Color color, int scale = 1)
    {
        var t = PixelText.Create(parent, s, color, scale);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        return t;
    }
}

// 元素栏小芯片：点一下插到光标处；按住拖出阈值后变成拖拽，松手点落在输入框里就插到那个落点。
// 拖拽事件必须由芯片自己接住，否则事件冒泡到面板会被当成画布点击。
// 浮窗挡板上的按下 = 点了窗外：取消并还原原值
public sealed class PopoverOutsideClick : MonoBehaviour, IPointerDownHandler
{
    public ExpressionPopoverUI Owner;
    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        e.Use();
        if (Owner != null) Owner.CancelFromOutside();
    }
}

// 补全浮窗的一行：点它等于按 Tab 采纳这颗成员
public sealed class CompletionRowClick : MonoBehaviour, IPointerClickHandler
{
    public ExpressionPopoverUI Host;
    public int Index;

    public void OnPointerClick(PointerEventData e)
    {
        e.Use();
        if (Host != null) Host.CompleteAt(Index);
    }
}

public sealed class ExprChip : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public const float DragThreshold = 5f;
    public string Label;
    public string Snippet;
    public int CaretWithin;
    public Color Fill;
    public Color Foreground;
    public ExpressionPopoverUI Host;

    bool pressed;
    bool dragging;
    Vector2 downPos;

    public void OnPointerDown(PointerEventData e)
    {
        if (Host == null) return;
        pressed = true;
        dragging = false;
        downPos = e.position;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!pressed || Host == null) return;
        if (!dragging)
        {
            if (Vector2.Distance(e.position, downPos) < DragThreshold) return;
            dragging = true;
            Host.BeginChipDrag(this, e.position);
        }
        else Host.MoveChipDrag(e.position);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (!pressed || Host == null) return;
        pressed = false;
        if (dragging) { dragging = false; Host.EndChipDrag(e.position); }
        else Host.InsertChip(this);
    }
}

// 元素栏滚轮转发：滚动事件沿父链冒泡到这里
public sealed class ExprPaletteScroll : MonoBehaviour, IScrollHandler
{
    public ExpressionPopoverUI Host;

    public void OnScroll(PointerEventData e)
    {
        if (Host != null && e.scrollDelta.y != 0f) Host.ScrollPalette(e.scrollDelta.y);
    }
}
