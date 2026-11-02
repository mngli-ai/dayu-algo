using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Dayu.Core;

// 表达式工作台：左侧元素库可无限拖出副本，中间用插槽式表达式树拼公式。
public sealed class ExprEditorUI : MonoBehaviour
{
    const float PanelW = 1180f, PanelH = 726f;
    const float PaletteW = 206f, CenterW = 706f, RightW = 260f;
    const float BodyY = 52f, BodyH = 624f;

    static ExprEditorUI active;
    public static bool IsOpen { get { return active != null; } }

    App app;
    GraphNode target;
    ExprWorkspace ws;
    RectTransform root, panel, paletteRoot, centerRoot, rightRoot, equationsRoot;
    CanvasGroup group;
    PixelText statusText, previewText;
    PixelInputField constField;
    PixelButton saveBtn;
    readonly List<SlotRef> slots = new List<SlotRef>();
    // 手填的位置：常量框 + 每条等式的名字框，Tab / Shift+Tab 在它们之间跳
    readonly List<PixelInputField> tabFields = new List<PixelInputField>();
    SlotRef hoverSlot;
    GameObject ghost;
    bool rebuildQueued;
    bool closing;

    sealed class SlotRef
    {
        public RectTransform Rect;
        public Image Img;
        public Action<ExprNode> Set;
        public float Area;
        public bool Empty;
        public Color Base;
    }

    public static void Open(App owner, GraphNode node)
    {
        if (owner == null || node == null || owner.GuideLayer == null) return;
        if (active != null) Destroy(active.gameObject);
        var go = new GameObject("ExprEditor", typeof(RectTransform));
        go.transform.SetParent(owner.GuideLayer, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var e = go.AddComponent<ExprEditorUI>();
        e.app = owner; e.target = node;
        e.Build();
        active = e;
    }

    void Build()
    {
        var old = GraphUtil.Get(target, "expr");
        ws = ExprFormat.Parse(old);
        if (ws.Output == null)
        {
            var a = GraphUtil.Get(target, "varA");
            var b = GraphUtil.Get(target, "varB");
            if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b))
                ws.Output = ExprNode.Op(GraphUtil.Get(target, "op") ?? "+", ExprNode.Var(a), ExprNode.Var(b));
        }
        if (HasArg("-exprdemo") || HasArg("-exprmany"))
        {
            ws.Equations.Clear();
            ws.Equations.Add(new ExprEquation
            {
                Name = "t1",
                Expr = ExprNode.Array(ExprNode.Const("10"), ExprNode.Const("20"), ExprNode.Const("30"))
            });
            ws.Output = ExprNode.Op("+", ExprNode.Index("t1", ExprNode.Var("b")), ExprNode.Var("a"));
        }
        if (HasArg("-exprmany"))
        {
            for (int i = 2; i <= 10; i++)
                ws.Equations.Add(new ExprEquation { Name = "t" + i, Expr = ExprNode.Const((i * 10).ToString()) });
        }

        root = (RectTransform)transform;
        var dim = root.gameObject.AddComponent<Image>();
        dim.sprite = GraphView.White; dim.type = Image.Type.Simple;
        dim.color = new Color(0.06f, 0.08f, 0.09f, .55f);
        dim.raycastTarget = true;
        root.gameObject.AddComponent<EditorDimClick>().Owner = this;
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        panel = Rect("Panel", root, 0, 0, PanelW, PanelH);
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
        panel.pivot = new Vector2(.5f, .5f);
        panel.anchoredPosition = Vector2.zero;
        var bg = panel.gameObject.AddComponent<Image>();
        bg.sprite = GraphView.White; bg.type = Image.Type.Simple; bg.color = Theme.Paper;
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = Theme.Border; outline.effectDistance = new Vector2(2, -2);
        panel.localScale = Vector3.one * .96f;

        var title = Text(panel, "表达式工作台", 18, 6, Theme.Ink, 2);
        title.raycastTarget = false;
        Text(panel, "拖元素到插槽，公式自动排版", 18, 36, Theme.Border, 1).raycastTarget = false;

        var save = PixelButton.Create(panel, "应用到运算块", Save, 118, 26, true);
        save.Rect.anchoredPosition = new Vector2(1044, -11);
        saveBtn = save;
        var cancel = PixelButton.Create(panel, "取消", Cancel, 62, 26);
        cancel.Rect.anchoredPosition = new Vector2(974, -11);

        var palette = Rect("Palette", panel, 0, BodyY, PaletteW, BodyH);
        var pimg = palette.gameObject.AddComponent<Image>();
        pimg.sprite = GraphView.White; pimg.type = Image.Type.Simple; pimg.color = Theme.Salt;
        paletteRoot = Rect("Content", palette, 0, 0, PaletteW, BodyH);

        var center = Rect("Center", panel, PaletteW + 4, BodyY, CenterW, BodyH);
        var cimg = center.gameObject.AddComponent<Image>();
        cimg.sprite = GraphView.White; cimg.type = Image.Type.Simple; cimg.color = Theme.Foam;
        centerRoot = center;

        var scroll = Rect("Scroll", center, 0, 84, CenterW, BodyH - 84);
        var simg = scroll.gameObject.AddComponent<Image>();
        simg.sprite = GraphView.White; simg.type = Image.Type.Simple;
        simg.color = new Color(1, 1, 1, 0); simg.raycastTarget = true;
        scroll.gameObject.AddComponent<RectMask2D>();
        equationsRoot = Rect("Equations", scroll, 0, 0, CenterW, BodyH - 84);
        var sr = scroll.gameObject.AddComponent<ScrollRect>();
        sr.viewport = scroll;
        sr.content = equationsRoot;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 28f;

        var right = Rect("Right", panel, PaletteW + CenterW + 8, BodyY, RightW, BodyH);
        var rimg = right.gameObject.AddComponent<Image>();
        rimg.sprite = GraphView.White; rimg.type = Image.Type.Simple; rimg.color = Theme.Salt;
        rightRoot = right;

        Text(rightRoot, "检查", 12, 12, Theme.Border, 1);
        statusText = Text(rightRoot, "", 12, 32, Theme.Coral, 1);
        statusText.rectTransform.sizeDelta = new Vector2(RightW - 24, 44);
        Text(rightRoot, "实时预览", 12, 90, Theme.Border, 1);
        previewText = Text(rightRoot, "", 12, 110, Theme.Ink, 1);
        previewText.rectTransform.sizeDelta = new Vector2(RightW - 24, 40);
        Text(rightRoot, "说明", 12, 168, Theme.Border, 1);
        var help = Text(rightRoot, "变量和运算符可从左侧重复拖出。\n等式名可改，回车确认。\nTab 在常量框和等式名之间跳。\n等式多时用滚轮上下滚动。\n数组用 [ ] 创建，再用 t1[i] 取值。", 12, 188, Theme.Border, 1);
        help.Wrap = true; help.WrapWidth = (int)(RightW - 24);
        help.rectTransform.sizeDelta = new Vector2(RightW - 24, 90);

        var addEq = PixelButton.Create(rightRoot, "+ 新建中间变量", AddEquation, 150, 26, false);
        addEq.Rect.anchoredPosition = new Vector2(12, -292);

        RebuildAll();
        StartCoroutine(OpenAnim());
        if (HasArg("-exprsave")) StartCoroutine(SaveNext());
    }

    IEnumerator SaveNext()
    {
        yield return new WaitForSeconds(.4f);
        Save();
    }

    IEnumerator OpenAnim()
    {
        float t = 0;
        while (t < .18f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / .18f);
            group.alpha = k;
            panel.localScale = Vector3.one * Mathf.Lerp(.96f, 1f, Mathf.SmoothStep(0, 1, k));
            yield return null;
        }
        group.alpha = 1f;
        panel.localScale = Vector3.one;
    }

    void RebuildAll()
    {
        if (closing) return;
        slots.Clear();
        tabFields.Clear();
        hoverSlot = null;
        BuildPalette();
        BuildCenter();
        UpdateCheck();
        UpdatePreview();
    }

    void QueueRebuild()
    {
        if (!rebuildQueued && !closing) StartCoroutine(DeferRebuild());
    }

    IEnumerator DeferRebuild()
    {
        rebuildQueued = true;
        yield return null;
        rebuildQueued = false;
        RebuildAll();
    }

    // 按创建次序登记：常量框在前、各等式名框在后，Tab 就照这个顺序绕圈
    void RegisterTabField(PixelInputField f)
    {
        if (f == null) return;
        int slot = tabFields.Count;
        tabFields.Add(f);
        f.OnTab = back =>
        {
            if (tabFields.Count == 0) return;
            int next = ((slot + (back ? -1 : 1)) % tabFields.Count + tabFields.Count) % tabFields.Count;
            if (tabFields[next] != null) tabFields[next].Focus();
        };
    }

    // ---------------- 元素库 ----------------

    void BuildPalette()
    {
        for (int i = paletteRoot.childCount - 1; i >= 0; i--) Destroy(paletteRoot.GetChild(i).gameObject);
        float y = 8;
        Text(paletteRoot, "元素库", 10, y, Theme.Ink, 2); y += 28;

        var types = ExprFormat.BuildTypes(ws, app.GraphCanvas != null ? app.GraphCanvas.CurrentGraph : null);
        Text(paletteRoot, "变量", 10, y, Theme.WhaleDeep, 2); y += 22;
        float x = 10;
        foreach (var n in app.GraphCanvas.CurrentGraph.Nodes)
        {
            if (n == null || n.Type != "vardecl") continue;
            var name = GraphUtil.Get(n, "name");
            if (string.IsNullOrEmpty(name)) continue;
            var type = GraphUtil.Get(n, "vtype") ?? "int";
            x = AddPaletteChip(x, y, name, Theme.Honey, () => ExprNode.Var(name));
            if (ExprFormat.IsArrayType(type))
                x = AddPaletteChip(x, y, name + "[]", Theme.Mint, () => ExprNode.Index(name, null));
            if (x > 120) { x = 10; y += 26; }
        }

        foreach (var eq in ws.Equations)
        {
            if (string.IsNullOrEmpty(eq.Name)) continue;
            string t;
            types.TryGetValue(eq.Name, out t);
            if (ExprFormat.IsArrayType(t))
                x = AddPaletteChip(x, y, eq.Name + "[]", Theme.Mint, () => ExprNode.Index(eq.Name, null));
            else
                x = AddPaletteChip(x, y, eq.Name, Theme.Honey, () => ExprNode.Var(eq.Name));
            if (x > 120) { x = 10; y += 26; }
        }
        y += 28;

        Text(paletteRoot, "常量", 10, y, Theme.WhaleDeep, 2); y += 22;
        constField = PixelInputField.Create(paletteRoot, "0", 104, 22);
        constField.GetComponent<RectTransform>().anchoredPosition = new Vector2(10, -y);
        RegisterTabField(constField);
        var dragConst = AddPaletteChip(128, y - 2, "拖常量", Theme.Cream, () => ExprNode.Const(string.IsNullOrEmpty(constField.Value) ? "0" : constField.Value));
        y += 30;

        // 算术一行、位运算一行：& | ^ << >> 都只吃整数，工作台里也能直接拖
        Text(paletteRoot, "运算符", 10, y, Theme.WhaleDeep, 2); y += 22;
        x = 10;
        foreach (var op in new[] { "+", "-", "*", "/", "%" })
        {
            string o = op;
            x = AddPaletteChip(x, y, o, Theme.Yellow, () => ExprNode.Op(o, null, null));
        }
        y += 26;
        x = 10;
        foreach (var op in new[] { "&", "|", "^", "<<", ">>" })
        {
            string o = op;
            x = AddPaletteChip(x, y, o, Theme.Yellow, () => ExprNode.Op(o, null, null));
        }
        y += 28;

        Text(paletteRoot, "数组 / STL", 10, y, Theme.WhaleDeep, 2); y += 22;
        AddPaletteChip(10, y, "[ ] 数组", Theme.Coral, () => ExprNode.Array(null));
        AddPaletteChip(72, y, "len", Theme.Grape, () => ExprNode.Len(null)); y += 26;
        AddPaletteChip(10, y, "size", Theme.Grape, () => new ExprNode { Kind = "size", Children = new List<ExprNode> { null } });
        AddPaletteChip(58, y, "front", Theme.Grape, () => new ExprNode { Kind = "front", Children = new List<ExprNode> { null } });
        AddPaletteChip(114, y, "back", Theme.Grape, () => new ExprNode { Kind = "back", Children = new List<ExprNode> { null } });
        y += 30;

        Text(paletteRoot, "拖出复制 · 点击放入空插槽", 10, y, Theme.WhaleDeep, 2);
    }

    float AddPaletteChip(float x, float y, string label, Color color, Func<ExprNode> factory)
    {
        float w = PixelText.MeasureString(label, 1).x + 14;
        var rt = Rect("Chip_" + label, paletteRoot, x, y, w, 22);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = GraphView.White; img.type = Image.Type.Simple; img.color = color; img.raycastTarget = true;
        var text = Text(rt, label, 0, 4, Theme.Ink, 1);
        text.rectTransform.sizeDelta = new Vector2(w, 22);
        text.Align = PixelText.TextAlign.Center; text.raycastTarget = false;
        var drag = rt.gameObject.AddComponent<ExprPaletteDrag>();
        drag.Editor = this; drag.Factory = factory;
        return x + w + 5;
    }

    // ---------------- 画布 ----------------

    void BuildCenter()
    {
        for (int i = equationsRoot.childCount - 1; i >= 0; i--) Destroy(equationsRoot.GetChild(i).gameObject);
        var oldOut = centerRoot.Find("OutputCard");
        if (oldOut != null) Destroy(oldOut.gameObject);

        var outCard = Rect("OutputCard", centerRoot, 10, 12, CenterW - 20, 64);
        var outImg = outCard.gameObject.AddComponent<Image>();
        outImg.sprite = GraphView.White; outImg.type = Image.Type.Simple; outImg.color = Theme.Cream;
        var outLine = outCard.gameObject.AddComponent<Outline>(); outLine.effectColor = Theme.Whale; outLine.effectDistance = new Vector2(1, -1);
        Text(outCard, "输出 =", 10, 22, Theme.Ink, 1);
        float ox = 76;
        ox += BuildExpr(outCard, ws.Output, ox, 20, v => { ws.Output = v; QueueRebuild(); }, true);

        float y = 2;
        for (int i = 0; i < ws.Equations.Count; i++)
        {
            var eq = ws.Equations[i];
            var card = Rect("Eq_" + eq.Name, equationsRoot, 10, y, CenterW - 20, 58);
            var img = card.gameObject.AddComponent<Image>();
            img.sprite = GraphView.White; img.type = Image.Type.Simple; img.color = Theme.Paper;
            var line = card.gameObject.AddComponent<Outline>(); line.effectColor = Theme.Border; line.effectDistance = new Vector2(1, -1);

            var eqLocal = eq;
            string oldName = eq.Name;
            var nameField = PixelInputField.Create(card, oldName, 64, 22);
            nameField.GetComponent<RectTransform>().anchoredPosition = new Vector2(8, -14);
            nameField.OnCommitted = v =>
            {
                if (!RenameEquation(eqLocal, oldName, v)) nameField.SetValue(oldName);
            };
            // 失焦不再等于提交：改名按回车生效，点别处只是把光标挪走
            RegisterTabField(nameField);
            Text(card, "=", 78, 18, Theme.Ink, 1);
            float ex = 90;
            ex += BuildExpr(card, eq.Expr, ex, 16, v => { eqLocal.Expr = v; QueueRebuild(); }, true);
            var del = PixelButton.Create(card, "删除", () => { ws.Equations.Remove(eqLocal); QueueRebuild(); }, 44, 22);
            del.Rect.anchoredPosition = new Vector2(CenterW - 86, -18);
            y += 66;
        }

        var add = PixelButton.Create(equationsRoot, "+ 添加等式", AddEquation, 92, 24);
        add.Rect.anchoredPosition = new Vector2(10, -(y + 2));

        float need = y + 44;
        equationsRoot.sizeDelta = new Vector2(CenterW, Mathf.Max(BodyH - 84, need));
    }

    bool RenameEquation(ExprEquation eq, string oldName, string raw)
    {
        var name = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(name) || !IsIdent(name)) { app.Toast("变量名只能用字母、数字、下划线"); return false; }
        if (name == oldName) return true;
        if (app.GraphCanvas.CurrentGraph.Nodes.Exists(n => n != null && n.Type == "vardecl" && GraphUtil.Get(n, "name") == name))
        { app.Toast("主画布已经有变量「" + name + "」"); return false; }
        foreach (var e in ws.Equations)
            if (e != eq && e.Name == name) { app.Toast("中间变量「" + name + "」已存在"); return false; }

        eq.Name = name;
        foreach (var e in ws.Equations) RenameRefs(e.Expr, oldName, name);
        RenameRefs(ws.Output, oldName, name);
        QueueRebuild();
        return true;
    }

    static void RenameRefs(ExprNode n, string oldName, string newName)
    {
        if (n == null) return;
        if ((n.Kind == "var" || n.Kind == "index") && n.Name == oldName) n.Name = newName;
        foreach (var c in n.Children) RenameRefs(c, oldName, newName);
    }

    static bool IsIdent(string x)
    {
        if (string.IsNullOrEmpty(x)) return false;
        if (!char.IsLetter(x[0]) && x[0] != '_') return false;
        for (int i = 1; i < x.Length; i++) if (!char.IsLetterOrDigit(x[i]) && x[i] != '_') return false;
        return true;
    }

    // 返回本表达式占用的宽度；空节点会注册一个可投放插槽
    float BuildExpr(RectTransform parent, ExprNode node, float x, float y, Action<ExprNode> setter, bool root = false)
    {
        if (node == null)
        {
            var slot = Rect("Slot", parent, x, y, 60, 24);
            var img = slot.gameObject.AddComponent<Image>();
            img.sprite = GraphView.White; img.type = Image.Type.Simple; img.color = Theme.Salt; img.raycastTarget = true;
            var outline = slot.gameObject.AddComponent<Outline>(); outline.effectColor = Theme.Border; outline.effectDistance = new Vector2(1, -1);
            var label = Text(slot, "拖入", 0, 5, Theme.Border, 1);
            label.rectTransform.sizeDelta = new Vector2(60, 24);
            label.Align = PixelText.TextAlign.Center; label.raycastTarget = false;
            slots.Add(new SlotRef { Rect = slot, Img = img, Set = setter, Area = 60 * 24, Empty = true, Base = Theme.Salt });
            return 64;
        }

        float start = x;
        switch (node.Kind)
        {
            case "var":
            {
                x += Chip(parent, node.Name ?? "?", x, y, Theme.Honey);
                break;
            }
            case "const":
            {
                x += Chip(parent, node.Value ?? "0", x, y, Theme.Cream);
                break;
            }
            case "op":
            {
                x += Symbol(parent, "(", x, y);
                x += BuildExpr(parent, Child(node, 0), x, y, v => { Ensure(node, 2); node.Children[0] = v; QueueRebuild(); });
                x += Symbol(parent, (node.Name ?? "+"), x, y);
                x += BuildExpr(parent, Child(node, 1), x, y, v => { Ensure(node, 2); node.Children[1] = v; QueueRebuild(); });
                x += Symbol(parent, ")", x, y);
                break;
            }
            case "index":
            {
                x += Symbol(parent, (node.Name ?? "?"), x, y);
                x += Symbol(parent, "[", x, y);
                x += BuildExpr(parent, Child(node, 0), x, y, v => { Ensure(node, 1); node.Children[0] = v; QueueRebuild(); });
                x += Symbol(parent, "]", x, y);
                break;
            }
            case "len":
            case "size":
            case "front":
            case "back":
            {
                string fname = node.Kind == "size" ? "size" : node.Kind == "front" ? "front" : node.Kind == "back" ? "back" : "len";
                x += Symbol(parent, fname + "(", x, y);
                x += BuildExpr(parent, Child(node, 0), x, y, v => { Ensure(node, 1); node.Children[0] = v; QueueRebuild(); });
                x += Symbol(parent, ")", x, y);
                break;
            }
            case "array":
            {
                x += Symbol(parent, "[", x, y);
                for (int i = 0; i < node.Children.Count; i++)
                {
                    int idx = i;
                    if (i > 0) x += Symbol(parent, ",", x, y);
                    x += BuildExpr(parent, node.Children[i], x, y, v => { node.Children[idx] = v; QueueRebuild(); });
                }
                x += Symbol(parent, "]", x, y);
                var plus = PixelButton.Create(parent, "加", () => { node.Children.Add(null); QueueRebuild(); }, 22, 20);
                plus.Rect.anchoredPosition = new Vector2(x + 2, -y);
                x += 26;
                break;
            }
        }

        float width = x - start;
        var replace = Rect("Replace", parent, start, y, width, 24);
        var rimg = replace.gameObject.AddComponent<Image>();
        rimg.sprite = GraphView.White; rimg.type = Image.Type.Simple;
        var clear = new Color(1, 1, 1, 0);
        rimg.color = clear; rimg.raycastTarget = false;
        slots.Add(new SlotRef { Rect = replace, Img = rimg, Set = setter, Area = width * 24, Base = clear });

        if (root)
        {
            var rm = PixelButton.Create(parent, "删", () => { setter(null); QueueRebuild(); }, 22, 20);
            rm.Rect.anchoredPosition = new Vector2(x + 4, -y);
            width += 28;
        }
        return width;
    }

    static ExprNode Child(ExprNode n, int i)
    {
        return n != null && n.Children != null && i < n.Children.Count ? n.Children[i] : null;
    }

    static void Ensure(ExprNode n, int count)
    {
        while (n.Children.Count < count) n.Children.Add(null);
    }

    float Chip(RectTransform parent, string label, float x, float y, Color color)
    {
        float w = PixelText.MeasureString(label, 1).x + 14;
        var rt = Rect("ExprChip", parent, x, y, w, 24);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = GraphView.White; img.type = Image.Type.Simple; img.color = color; img.raycastTarget = false;
        var t = Text(rt, label, 0, 5, Theme.Ink, 1);
        t.rectTransform.sizeDelta = new Vector2(w, 24);
        t.Align = PixelText.TextAlign.Center; t.raycastTarget = false;
        return w + 3;
    }

    float Symbol(RectTransform parent, string text, float x, float y)
    {
        var t = Text(parent, text, x, y + 5, Theme.Ink, 1);
        t.raycastTarget = false;
        return PixelText.MeasureString(text, 1).x + 2;
    }

    // ---------------- 拖拽 ----------------

    public void BeginDrag(ExprNode template, PointerEventData e)
    {
        EndDrag(e, null);
        float w = 72, h = 26;
        var go = new GameObject("ExprGhost", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(root, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
        rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = new Vector2(w, h);
        var img = go.GetComponent<Image>();
        img.sprite = GraphView.White; img.type = Image.Type.Simple; img.color = Theme.Honey; img.raycastTarget = false;
        var t = PixelText.Create(go.transform, Label(template), Theme.Ink, 1);
        var lrt = t.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0f);
        lrt.pivot = new Vector2(0f, 0f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(w, h);
        t.Align = PixelText.TextAlign.Center; t.raycastTarget = false;
        ghost = go;
        MoveGhost(e);
    }

    public void MoveGhost(PointerEventData e)
    {
        if (ghost == null) return;
        Vector2 p;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, e.position, null, out p);
        ghost.GetComponent<RectTransform>().anchoredPosition = p;
        HighlightAt(e.position);
    }

    SlotRef SlotAt(Vector2 screen)
    {
        SlotRef best = null;
        foreach (var s in slots)
            if (RectTransformUtility.RectangleContainsScreenPoint(s.Rect, screen, null))
                if (best == null || s.Area < best.Area) best = s;
        return best;
    }

    void HighlightAt(Vector2 screen)
    {
        SlotRef found = SlotAt(screen);
        if (hoverSlot == found) return;
        if (hoverSlot != null && hoverSlot.Img != null) hoverSlot.Img.color = hoverSlot.Base;
        hoverSlot = found;
        if (hoverSlot != null && hoverSlot.Img != null)
        {
            Color c = Theme.Honey;
            c.a = hoverSlot.Empty ? 1f : .22f;
            hoverSlot.Img.color = c;
        }
    }

    public void EndDrag(PointerEventData e, ExprNode template)
    {
        if (ghost != null) { Destroy(ghost); ghost = null; }
        if (template != null)
        {
            SlotRef found = SlotAt(e.position);
            if (found != null)
            {
                found.Set(template);
                if (found.Img != null) found.Img.color = found.Base;
                hoverSlot = null;
                QueueRebuild();
                return;
            }
        }
        if (hoverSlot != null && hoverSlot.Img != null) hoverSlot.Img.color = hoverSlot.Base;
        hoverSlot = null;
    }

    public void AddToFirstEmpty(ExprNode template)
    {
        if (template == null) return;
        SlotRef slot = null;
        foreach (var s in slots) if (s.Empty) { slot = s; break; }
        if (slot == null) { app.Toast("没有空插槽，先添加一个等式或清除旧表达式"); return; }
        slot.Set(template);
        QueueRebuild();
    }

    // ---------------- 状态 ----------------

    void UpdateCheck()
    {
        var g = app.GraphCanvas != null ? app.GraphCanvas.CurrentGraph : null;
        var errors = ExprFormat.Validate(ws, g);
        statusText.color = errors.Count == 0 ? Theme.Whale : Theme.Coral;
        if (errors.Count == 0) statusText.Text = "公式完整 · 类型检查通过";
        else
        {
            var s = "";
            for (int i = 0; i < Mathf.Min(3, errors.Count); i++) s += "· " + errors[i] + "\n";
            statusText.Text = s.TrimEnd();
        }
    }

    void UpdatePreview()
    {
        if (ws.Output == null) { previewText.Text = "输出 = ?"; return; }
        var g = app.GraphCanvas != null ? app.GraphCanvas.CurrentGraph : null;
        var types = ExprFormat.BuildTypes(ws, g);
        var t = ExprFormat.TypeOf(ws.Output, g, types);
        var v = ExprFormat.Eval(ws, app.PreviewVar);
        previewText.Text = "输出 = " + v.ToDisplayString() + "\n类型：" + t;
    }

    void AddEquation()
    {
        int n = ws.Equations.Count + 1;
        var names = new HashSet<string>();
        foreach (var e in ws.Equations) names.Add(e.Name);
        foreach (var node in app.GraphCanvas.CurrentGraph.Nodes)
            if (node != null && node.Type == "vardecl") names.Add(GraphUtil.Get(node, "name"));
        while (names.Contains("t" + n)) n++;
        ws.Equations.Add(new ExprEquation { Name = "t" + n, Expr = null });
        QueueRebuild();
    }

    void Save()
    {
        PixelInputField.FlushActive();
        var g = app.GraphCanvas != null ? app.GraphCanvas.CurrentGraph : null;
        var errors = ExprFormat.Validate(ws, g);
        if (errors.Count > 0) { app.Toast("还有问题：" + errors[0]); return; }
        target.Params["expr"] = ExprFormat.Serialize(ws);
        target.Params["formula"] = ExprFormat.FormulaClean(ws.Output);
        target.Params["varA"] = "";
        target.Params["varB"] = "";
        app.OnExpressionApplied(target);
        Close();
    }

    void Cancel() { Close(); }

    // 点工作台外任意一点 = 取消（不应用，原公式保持不变）
    public void CancelFromOutside()
    {
        Debug.Log("[交互] 表达式工作台点窗外，取消");
        Close();
    }

    // 遮罩是面板的父节点，面板里的按下会冒泡到它，所以要用「起始按下位置」判定是否真在窗外
    public bool PressedInsidePanel(Vector2 screenPos)
    {
        if (panel == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(panel, screenPos, null);
    }

    void Close()
    {
        if (closing) return;
        closing = true;
        active = null;
        StartCoroutine(CloseAnim());
    }

    IEnumerator CloseAnim()
    {
        float t = 0;
        while (t < .14f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / .14f);
            if (group != null) group.alpha = 1f - k;
            if (panel != null) panel.localScale = Vector3.one * Mathf.Lerp(1f, .96f, Mathf.SmoothStep(0, 1, k));
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

    static bool HasArg(string key)
    {
        return Array.IndexOf(Environment.GetCommandLineArgs(), key) >= 0;
    }

    static string Label(ExprNode n)
    {
        if (n == null) return "?";
        if (n.Kind == "op") return n.Name ?? "+";
        if (n.Kind == "array") return "[ ]";
        if (n.Kind == "len") return "len";
        if (n.Kind == "index") return (n.Name ?? "?") + "[]";
        return ExprFormat.Formula(n);
    }
}

public sealed class ExprPaletteDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    public ExprEditorUI Editor;
    public Func<ExprNode> Factory;
    bool moved;

    public void OnBeginDrag(PointerEventData e)
    {
        moved = true;
        var node = Factory != null ? Factory() : null;
        if (node != null) Editor.BeginDrag(node, e);
    }

    public void OnDrag(PointerEventData e)
    {
        Editor.MoveGhost(e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        var node = Factory != null ? Factory() : null;
        Editor.EndDrag(e, node);
        StartCoroutine(ResetMoved());
    }

    IEnumerator ResetMoved()
    {
        yield return null;
        moved = false;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (moved) return;
        var node = Factory != null ? Factory() : null;
        if (node != null) Editor.AddToFirstEmpty(node);
    }
}

// 遮罩上的一次按下：起点在面板之外才算「点了窗外」，取消并还原
public sealed class EditorDimClick : MonoBehaviour, IPointerDownHandler
{
    public ExprEditorUI Owner;
    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (Owner == null || Owner.PressedInsidePanel(e.pressPosition)) return;
        e.Use();
        Owner.CancelFromOutside();
    }
}
