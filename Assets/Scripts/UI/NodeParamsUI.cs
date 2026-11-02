using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Dayu.Core;

// 模块参数悬浮窗：双击画布上的模块就地弹出，不再跑到右侧栏去改。
// 点窗外任意一点 / Esc = 取消并还原原值；点「确定」或在输入框里按回车 = 写回。
public sealed class NodeParamsUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    static NodeParamsUI current;

    const float FieldW = 250f;
    const float RowH = 30f;
    const float LabelH = 26f;
    const float Gap = 10f;
    const float Pad = 12f;
    const float MinChoiceW = 118f;
    const int FontScale = 2;
    const float TitleH = 32f;
    const float HintRowH = 18f;
    // 变量候选的小按钮（「赋值」面板用来点选目前能用的变量）
    const float PickH = 22f;
    const float PickPitch = 26f;
    const float PickGapX = 5f;
    const float PickPadX = 12f;

    static float HintH(Spec s) { return string.IsNullOrEmpty(s.Hint) ? 0f : HintRowH; }

    // 一条可编辑参数：要么是一个文本框，要么是一排互斥的类型按钮
    public class Spec
    {
        public string Key;
        public string Label;
        public string Value;
        public string[] Choices;
        public Func<string, string> ChoiceText;
        public float ChoiceWidth = MinChoiceW;
        // 表达式字段：标签下面多写一行「这里该填什么」，玩家不用猜
        public bool IsExpr;
        public string Hint;
        // 点一下就能填进这个空的候选词（变量名），排在输入框下面
        public string[] Picks;

        public bool IsChoice { get { return Choices != null && Choices.Length > 0; } }
        public int Rows { get { return IsChoice ? (Choices.Length + 1) / 2 : 1; } }

        public string Display(string val)
        {
            return ChoiceText != null ? ChoiceText(val) : val;
        }
    }

    // 一枚候选按钮落在窗口里的哪一行哪一列（纯函数排好的，摆的时候不再算）
    public struct PickCell
    {
        public string Name;
        public float X;
        public float Y;
        public float W;
        public int Row;
        public PickCell(string name, float x, float y, float w, int row)
        {
            Name = name; X = x; Y = y; W = w; Row = row;
        }
    }

    // 候选按钮按窗口内宽换行：这一行放不下就另起一行。行数决定窗口要多长。
    public static List<PickCell> LayoutPicks(string[] names, float[] widths, float bodyW, float gapX)
    {
        var cells = new List<PickCell>();
        if (names == null || widths == null) return cells;
        float x = 0f;
        int row = 0;
        for (int i = 0; i < names.Length; i++)
        {
            float w = i < widths.Length ? widths[i] : 0f;
            if (x > 0f && x + w > bodyW) { x = 0f; row++; }
            cells.Add(new PickCell(names[i], x, row * PickPitch, w, row));
            x += w + gapX;
        }
        return cells;
    }

    public static int PickRows(List<PickCell> cells)
    {
        int rows = 0;
        foreach (var c in cells) rows = Mathf.Max(rows, c.Row + 1);
        return rows;
    }

    // 点变量名往空里填的规矩：整句还没开头就补成「变量 = 」，光标等着写右边；
    // 已经写了等号或运算符就直接接上，否则中间留一个空格，不会把两个名字粘成一个
    public static string InsertAfterPick(string current, string token)
    {
        var cur = current ?? "";
        var tok = token ?? "";
        if (tok.Length == 0) return cur;
        if (cur.Trim().Length == 0) return tok + " = ";
        char last = cur[cur.Length - 1];
        bool open = last == '=' || last == '+' || last == '-' || last == '*' || last == '/'
            || last == '%' || last == '&' || last == '|' || last == '^' || last == '<'
            || last == '>' || last == ',' || last == '(' || last == '[' || last == ' ';
        return open ? cur + tok : cur + " " + tok;
    }

    readonly Dictionary<string, string> draft = new Dictionary<string, string>();
    readonly Dictionary<string, RectTransform> choiceRows = new Dictionary<string, RectTransform>();
    readonly Dictionary<string, PixelInputField> fields = new Dictionary<string, PixelInputField>();
    // Tab 跳的是「填空位」，顺序 = 参数从上到下的顺序，所以按创建次序另存一份
    readonly List<PixelInputField> tabOrder = new List<PixelInputField>();
    readonly Dictionary<string, List<PickCell>> pickCells = new Dictionary<string, List<PickCell>>();
    readonly List<Spec> specs = new List<Spec>();
    Action<Dictionary<string, string>> onCommit;
    RectTransform panel;
    CanvasGroup cg;
    GameObject blocker;
    bool closing;

    // anchor：被编辑的卡片；layer：窗口挂载的父节点（画布根），保证盖在所有卡片之上。
    // focusKey：开窗后立刻把光标落进这个字段的输入框（双击模块要直接进编辑态）。
    public static NodeParamsUI Open(RectTransform layer, RectTransform anchor, string title,
                                    List<Spec> specs, Action<Dictionary<string, string>> onCommit,
                                    string focusKey = null)
    {
        if (layer == null || specs == null || specs.Count == 0) return null;
        if (current != null) current.Close();

        var go = new GameObject("NodeParams", typeof(RectTransform));
        go.transform.SetParent(layer, false);
        GraphView.Stretch(go.GetComponent<RectTransform>());
        var w = go.AddComponent<NodeParamsUI>();
        w.onCommit = onCommit;
        w.Build(layer, anchor, title, specs, focusKey);
        current = w;
        IsOpen = true;
        return w;
    }

    void Build(RectTransform layer, RectTransform anchor, string title, List<Spec> list, string focusKey)
    {
        specs.AddRange(list);
        cg = gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        // 挡板：盖住整块画布，点它就等于「点了窗外」
        var bgGo = new GameObject("Blocker", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(transform, false);
        blocker = bgGo;
        GraphView.Stretch(bgGo.GetComponent<RectTransform>());
        var bimg = bgGo.GetComponent<Image>();
        bimg.sprite = GraphView.White;
        bimg.type = Image.Type.Simple;
        bimg.color = new Color(1f, 1f, 1f, 0f);
        bimg.raycastTarget = true;
        bgGo.AddComponent<OutsideClick>().Owner = this;

        float bodyW = 0f;
        foreach (var s in specs)
        {
            if (!string.IsNullOrEmpty(s.Hint)) bodyW = Mathf.Max(bodyW, PixelText.MeasureString(s.Hint, 1).x + 8f);
            if (!s.IsChoice) { bodyW = Mathf.Max(bodyW, FieldW); continue; }
            // 按钮宽度按最长的中文名现量：字号放大后「并且 (and)」这类会撑破固定宽
            float wide = MinChoiceW;
            foreach (var c in s.Choices)
                wide = Mathf.Max(wide, PixelText.MeasureString(s.Display(c) ?? "", FontScale).x + 18f);
            s.ChoiceWidth = wide;
            bodyW = Mathf.Max(bodyW, wide * 2f + 6f);
        }
        float winW = bodyW + Pad * 2f;
        foreach (var s in specs)
        {
            if (s.Picks == null || s.Picks.Length == 0) continue;
            var w = new float[s.Picks.Length];
            for (int i = 0; i < s.Picks.Length; i++)
                w[i] = Mathf.CeilToInt(PixelText.MeasureString(s.Picks[i] ?? "", 1).x + PickPadX);
            pickCells[s.Key] = LayoutPicks(s.Picks, w, bodyW, PickGapX);
        }
        float winH = Pad + TitleH;
        foreach (var s in specs)
            winH += LabelH + 2f + HintH(s) + s.Rows * (RowH + 4f) + PickHeight(s.Key) + Gap;
        winH += 34f + Pad;

        var pGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        pGo.transform.SetParent(transform, false);
        panel = pGo.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(winW, winH);
        var pimg = pGo.GetComponent<Image>();
        pimg.sprite = GraphView.White;
        pimg.type = Image.Type.Simple;
        pimg.color = Theme.Paper;
        pimg.raycastTarget = true;
        var outline = pGo.AddComponent<Outline>();
        outline.effectColor = Theme.Whale;
        outline.effectDistance = new Vector2(2f, -2f);

        var t = PixelText.Create(panel.transform, title, Theme.Ink, FontScale);
        PlaceTop(t.rectTransform, Pad, -Pad);

        // 摆在被编辑模块的右边；放不下就翻到左边，再夹回画布内
        var lr = layer.rect;
        float ax = lr.xMin, ay = lr.yMax, aw = 0f;
        if (anchor != null)
        {
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            var tl = layer.InverseTransformPoint(corners[0]);
            var tr = layer.InverseTransformPoint(corners[1]);
            ax = tl.x; ay = tl.y;
            aw = tr.x - tl.x;
        }

        float x = ax + aw + 10f;
        if (x + winW > lr.xMax - 6f) x = ax - winW - 10f;
        x = Mathf.Clamp(x, lr.xMin + 6f, Mathf.Max(lr.xMin + 6f, lr.xMax - winW - 6f));
        float top = Mathf.Clamp(ay, lr.yMin + winH + 6f, lr.yMax - 6f);
        panel.anchoredPosition = new Vector2(x - lr.xMin, top - lr.yMax);

        float cy = -Pad - TitleH;
        foreach (var s in specs)
        {
            draft[s.Key] = s.Value ?? "";
            var lb = PixelText.Create(panel.transform, s.Label, Theme.Ink, FontScale);
            PlaceTop(lb.rectTransform, Pad, cy);
            cy -= LabelH + 2f;

            if (!string.IsNullOrEmpty(s.Hint))
            {
                var hint = PixelText.Create(panel.transform, s.Hint, Theme.Whale, 1);
                PlaceTop(hint.rectTransform, Pad + 2f, cy);
                cy -= HintRowH;
            }

            if (s.IsChoice)
            {
                var rowGo = new GameObject("Choices", typeof(RectTransform));
                rowGo.transform.SetParent(panel.transform, false);
                var row = (RectTransform)rowGo.transform;
                row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
                row.pivot = new Vector2(0f, 1f);
                row.anchoredPosition = new Vector2(Pad, cy);
                row.sizeDelta = new Vector2(bodyW, s.Rows * (RowH + 4f));
                choiceRows[s.Key] = row;
                BuildChoices(s, row);
                cy -= s.Rows * (RowH + 4f);
            }
            else
            {
                var f = PixelInputField.Create(panel.transform, s.Value ?? "", (int)FieldW, (int)RowH, FontScale);
                // 表达式（尤其「赋值」整句）比变量名长得多，沿用 24 字会把玩家写的句子砍掉
                if (s.IsExpr) f.MaxLength = 96;
                var frt = f.GetComponent<RectTransform>();
                frt.anchorMin = frt.anchorMax = new Vector2(0f, 1f);
                frt.pivot = new Vector2(0f, 1f);
                frt.anchoredPosition = new Vector2(Pad, cy);
                string key = s.Key;
                fields[key] = f;
                // 敲一个字就同步进 draft：否则「点确定」拿到的是打开窗口时的旧值，
                // 只有按回车（OnCommitted）才生效——玩家看到的就是「确定按钮没用」。
                f.OnChanged = v => draft[key] = (v ?? "").Trim();
                f.OnCommitted = v => { draft[key] = (v ?? "").Trim(); Commit(); };
                // 点到下一个框只是把这一个空挪走：只存草稿，窗口必须还在
                f.OnBlurred = v => draft[key] = (v ?? "").Trim();
                int slot = tabOrder.Count;
                tabOrder.Add(f);
                f.OnTab = back => FocusTabSlot(NextTabSlot(tabOrder.Count, slot, back));
                cy -= RowH + 4f;
                cy = BuildPicks(s, f, cy);
            }
            cy -= Gap;
        }

        var ok = PixelButton.Create(panel.transform, "确定", Commit, 80, (int)RowH, true, FontScale, true);
        Place(ok.Rect, Pad, cy);
        var cancel = PixelButton.Create(panel.transform, "取消", CancelOutsider, 80, (int)RowH, false, FontScale, true);
        Place(cancel.Rect, Pad + 88f, cy);
        if (tabOrder.Count > 1)
        {
            var tip = PixelText.Create(panel.transform, "Tab 换填空位", Theme.Whale, 1);
            Place(tip.rectTransform, Pad + 178f, cy + 6f);
        }

        gameObject.AddComponent<CloseOnEsc>().Owner = this;
        PixelInputField target;
        if (!string.IsNullOrEmpty(focusKey) && fields.TryGetValue(focusKey, out target) && target != null) target.Focus();
        StartCoroutine(OpenAnim());
        Debug.Log("[交互] 打开参数窗 " + title + " 字段 " + specs.Count + " 可Tab " + tabOrder.Count);
    }

    void FocusTabSlot(int slot)
    {
        if (slot < 0 || slot >= tabOrder.Count) return;
        var f = tabOrder[slot];
        if (f == null) return;
        Debug.Log("[交互] Tab 切到第 " + (slot + 1) + "/" + tabOrder.Count + " 个填空位");
        f.Focus();
    }

    // 纯函数：Tab 到第几个填空位，首尾相接（往下按到头回到第一个，往上按到底到最后一个）
    public static int NextTabSlot(int count, int current, bool backward)
    {
        if (count <= 0) return -1;
        int step = backward ? -1 : 1;
        return ((current + step) % count + count) % count;
    }

    float PickHeight(string key)
    {
        List<PickCell> cells;
        return pickCells.TryGetValue(key, out cells) ? PickRows(cells) * PickPitch + 4f : 0f;
    }

    // 输入框下面一排变量名：点一下填进上面那个空，不用记名字怎么拼
    float BuildPicks(Spec s, PixelInputField field, float y)
    {
        List<PickCell> cells;
        if (!pickCells.TryGetValue(s.Key, out cells) || cells.Count == 0) return y;
        foreach (var c in cells)
        {
            string token = c.Name;
            var b = PixelButton.Create(panel.transform, token, () =>
            {
                if (field == null) return;
                field.SetValue(InsertAfterPick(field.Value, token));
                draft[s.Key] = (field.Value ?? "").Trim();
                field.Focus();
            }, Mathf.CeilToInt(c.W), (int)PickH, false, 1, true);
            Place(b.Rect, Pad + c.X, y - c.Y);
        }
        return y - PickHeight(s.Key);
    }

    void BuildChoices(Spec s, RectTransform row)
    {
        for (int i = 0; i < s.Choices.Length; i++)
        {
            string val = s.Choices[i];
            int col = i % 2, r = i / 2;
            var b = PixelButton.Create(row, s.Display(val),
                () => { draft[s.Key] = val; RebuildChoiceRow(s); },
                (int)s.ChoiceWidth, (int)RowH, val == draft[s.Key], FontScale, true);
            Place(b.Rect, col * (s.ChoiceWidth + 6f), -r * (RowH + 4f));
        }
    }

    // 选中态变了要换按钮外观，只重建这一排
    void RebuildChoiceRow(Spec s)
    {
        RectTransform row;
        if (!choiceRows.TryGetValue(s.Key, out row) || row == null) return;
        for (int i = row.childCount - 1; i >= 0; i--) Destroy(row.GetChild(i).gameObject);
        StartCoroutine(RebuildNextFrame(s, row));
    }

    IEnumerator RebuildNextFrame(Spec s, RectTransform row)
    {
        yield return null;
        BuildChoices(s, row);
    }

    static void Place(RectTransform rt, float x, float y)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
    }

    static void PlaceTop(RectTransform rt, float x, float y) { Place(rt, x, y); }

    IEnumerator OpenAnim()
    {
        float t = 0f;
        panel.localScale = new Vector3(.94f, .94f, 1f);
        while (t < .12f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / .12f);
            cg.alpha = k;
            panel.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }
        cg.alpha = 1f;
        panel.localScale = Vector3.one;
    }

    void Commit()
    {
        if (closing) return;
        // 兜底再同步一次输入框的当前文本：OnChanged 已经逐字同步，这里防的是
        // 「焦点还在框里、直接点了确定」这类没走过 OnChanged 的路径。
        foreach (var kv in fields)
            if (kv.Value != null) draft[kv.Key] = (kv.Value.Value ?? "").Trim();
        var snapshot = new Dictionary<string, string>(draft);
        Close();
        if (onCommit != null) onCommit(snapshot);
    }

    public void CancelOutsider()
    {
        if (closing) return;
        Debug.Log("[交互] 点了参数窗外，取消并还原");
        Close();
    }

    void Close()
    {
        if (closing) return;
        closing = true;
        IsOpen = false;
        if (current == this) current = null;
        PixelInputField.FlushActive();
        if (blocker != null) Destroy(blocker);
        StartCoroutine(CloseAnim());
    }

    IEnumerator CloseAnim()
    {
        float t = 0f;
        while (t < .1f && cg != null)
        {
            t += Time.deltaTime;
            cg.alpha = 1f - Mathf.Clamp01(t / .1f);
            yield return null;
        }
        Destroy(gameObject);
    }
}

// 挡板上的一次按下 = 点了窗外，取消并还原
public sealed class OutsideClick : MonoBehaviour, IPointerDownHandler
{
    public NodeParamsUI Owner;
    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        e.Use();
        if (Owner != null) Owner.CancelOutsider();
    }
}

// 悬浮窗按 Esc 取消
public sealed class CloseOnEsc : MonoBehaviour
{
    public NodeParamsUI Owner;
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && Owner != null) Owner.CancelOutsider();
    }
}
