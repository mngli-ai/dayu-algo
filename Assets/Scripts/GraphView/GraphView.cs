using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Dayu.Core;

// 节点画布：卡片底板用 Image + 运行时 1x1 白 Sprite（PanelGraphic 依赖 rect 时序，batch 下不渲染）。
// 卡片锚点为左上角、pivot 为左上角，anchoredPosition = (node.X, -node.Y)，于是 X/Y 即左上角坐标。
public sealed class GraphView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
{
    public const float ContentW = 1004f, ContentH = 684f;
    // 第一行端口中心离卡片顶边 26px（标题 22 + 4 留白），行距 18；
    // 卡片底部再留 6px，其上放一行 14px 的参数摘要，标题、端口标签、摘要三者互不压盖。
    // 端口方块从 10px 提到 13px：拉线时要点住的就是它，10px 在这套字号下实在太小。
    // 行距 18 不动 —— 卡片高度、连线走向都按它算，改一处要连着改一片。
    public const float PortSize = 13f, PortRowH = 18f;
    // 透明热区：横着排（端口在左右两边）时纵向只有 18px 行距，热区撑到 28 会跟上下邻居抢点击，
    // 所以纵向收到行距；竖着排（端口在上下两边）列距 30，横向可以吃满 28。
    public const float PortHitW = 28f, PortHitAlongRow = 18f;
    // 端口沿卡片上下边缘横排时，热区在「垂直于卡片边」那一侧的厚度（左右边缘那侧用 PortHitW）。
    public const float PortHitAcrossCol = 24f;
    // 上下边缘时端口顺着横轴排开的列距（左右边缘时是 PortRowH 行距）。
    public const float PortColPitch = 30f;
    // 镜头反向补偿（#99 收尾）：默认适应缩放只有 0.53，13px 的方块在屏幕上剩 6.9，照样难点到。
    // 头顶名牌（#84）已经证明「按 1/zoom 补」这条路可行，端口照做，但补的时候有一道天花板：
    // 相邻端口在屏幕上只离你 间距×zoom 远，热区补过头就会把邻居的点击整个抢走，
    // 所以顺着排开的那一侧最多长到间距本身，垂直于卡片边的那一侧（外面是空的）随便补。
    public static float PortInvScale(float zoom) { return 1f / Mathf.Clamp(zoom, .42f, 1.8f); }
    // 可见方块的补偿系数：均匀放大，但不能大到贴上隔壁端口。
    public static float PortDotScale(bool vertical, float zoom)
    {
        float pitch = vertical ? PortColPitch : PortRowH;
        return Mathf.Min(PortInvScale(zoom), (pitch - 4f) / PortSize);
    }
    public static Vector2 PortHitSize(bool vertical, float zoom)
    {
        float inv = PortInvScale(zoom);
        return vertical
            ? new Vector2(Mathf.Min(PortHitW * inv, PortColPitch), PortHitAcrossCol * inv)
            : new Vector2(PortHitW * inv, Mathf.Min(PortHitAlongRow * inv, PortRowH));
    }
    // 热区朝卡片外挪的距离也按镜头补，否则远机位下「往外探」这一格优势会被一起缩掉。
    public static float PortHitOutward(float zoom) { return 6f * PortInvScale(zoom); }
    // 方块放大只朝卡片外走：内侧面必须留在原来的位置，不然会把卡片里的标题和端口标签压住
    // （第一版没做这一步，截图复核时端口把「读入」两个字盖掉了一半）。
    public static float PortDotShift(bool vertical, float zoom)
    {
        return (PortSize * PortDotScale(vertical, zoom) - PortSize) * .5f;
    }
    public const float TitleH = 22f, HeadGap = 6f, FootGap = 6f, PortRowTop = 26f;
    // 卡片中间的内容区：标题下先居中写一行「这个模块关于哪个变量」，再是写好的表达式（28px 高的字段框）。
    // 表达式字段为空时不写「点此输入表达式」，只有一个居中的加号。
    public const float IdentityH = 16f, ExprFieldGap = 30f;
    public const float TextH = 14f;
    // 样例板自己带数字块，摘要照旧留在左下角；其它模块的功能说明改到标题下面居中。
    public static bool BottomSummary(string type) { return type == "sample_input" || type == "sample_compare"; }
    // 两块样例板钉死在画板上：判题链靠它们取当前样例、对比程序输出，玩家删掉这一关就没法判了。
    // 所以右键删、Delete、框选批量删、复制粘贴都跳过它们；关卡加载时还会自动补齐缺的那块。
    public static bool IsPinned(string type) { return BottomSummary(type); }
    // 标准答案预览里整张画布是只读的：框选答案 Ctrl+C、退出预览再 Ctrl+V，等于把答案原样抄进自己的图，
    // 判题照样判过——这条路必须堵死。删和改在预览里本来就不落存档（退出时换回玩家那份），不用拦。
    public const string PreviewReadOnlyHint = "标准答案预览是只读的：不能复制、不能粘贴，退出预览再动自己的图";
    public static bool PreviewBlocksClipboard(bool answerPreview) { return answerPreview; }
    static bool HasIdentityLine(string type) { return type != "entry" && !string.IsNullOrEmpty(type) && !BottomSummary(type); }
    // 表达式字段的第一行起点：有身份行时排在它下面，没有就紧贴标题。
    public static float ExprTop(string type) { return HasIdentityLine(type) ? TitleH + IdentityH + 2f : PortRowTop; }
    // 内容起点按卡片长相分两种：有结构皮肤的卡不画标题带和身份行，招牌只占 TitleH；
    // 纸面板卡（样例板、查询小形状卡）还要留出身份行。
    public static float CardTop(string type, string skinKind) { return skinKind != null ? TitleH : ExprTop(type); }

    // 端口朝向：默认「上下」——输入沿上边缘、输出沿下边缘，执行流跟 C++ 源码一样自上而下；
    // 「左右」时输入在左边缘、输出在右边缘，连线走横向贝塞尔。
    public bool VerticalPorts { get; private set; } = true;
    public Action<bool> PortLayoutChanged;

    // 顶栏「布局」按钮：翻转端口朝向并按新朝向重建所有卡片（节点坐标不动）
    public void TogglePortLayout()
    {
        VerticalPorts = !VerticalPorts;
        LoadGraph(graph, false);      // 只是重铺卡片，历史栈不能被这一键清空
        if (PortLayoutChanged != null) PortLayoutChanged(VerticalPorts);
    }

    static Sprite _white;

    // 运行时 1x1 白点 Sprite，Point 过滤，所有 Image 共用。
    public static Sprite White
    {
        get
        {
            if (_white != null) return _white;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _white.name = "GraphViewWhite";
            return _white;
        }
    }

    RectTransform host, content, lineLayer, loopLayer, nodeLayer, vizLayer;
    LineGraphic lines;
    readonly Dictionary<string, NodeCard> cards = new();
    Graph graph = new Graph();
    Vector2 panStart, contentStart, selectStart;
    bool panning, selecting;
    GameObject selectionBox;
    RectTransform contextMenu;
    string contextNodeUid;
    float zoom = 1f;
    bool loaded;
    // 撤销/恢复：画布只负责记账，快照栈与并笔规则都交给 GraphHistory
    readonly GraphHistory history = new GraphHistory();
    // 上一笔改的目标（哪个节点的哪一格），同一目标连改算一笔
    string lastEditTarget;

    public Graph CurrentGraph { get { return graph; } }
    // 当前画布缩放：头顶名牌之类的「屏幕尺寸要恒定」的元素按它反向补偿
    public float Zoom { get { return zoom; } }
    // 世界坐标接口供 Overlay 中的 token 每帧重新定位，拖动、平移、缩放后仍能跟随。
    public RectTransform ContentTransform { get { return content; } }
    public event Action<GraphNode> NodeSelected;
    public event Action<GraphNode> NodeDeleteRequested;
    public event Action<GraphNode> NodeEditRequested;
    // 双击卡片上「不是表达式」的那一行（计数循环的循环变量行）：要的是参数窗，不是表达式浮窗
    public event Action<GraphNode> NodeParamsRequested;
    public event Action<GraphNode> NodeBreakpointToggled;
    public event Action<GraphNode, GraphNode> LinkCreated;
    public event Action LinkHintRequested;
    // 卡片被原地重建后通知外部补状态（断点小圆点存在 App 的集合里，重建会丢）
    public event Action<string> CardRebuilt;
    // 图有任何变化（增删模块、连线、改参数、挪位置）都通知外部，让状态栏跟着重编译，
    // 不再挂着上一条已经过时的报错
    public event Action GraphChanged;

    void RaiseGraphChanged() { PushHistory(null); if (GraphChanged != null) GraphChanged(); }

    // 外部改参路径（App 的参数窗、单格开关）也走这里：既补一条历史，也通知重编译与自动存档。
    // target 传「节点:参数名」，连着改同一格只留一条历史，撤销不必一下退回去半截名字。
    public void NotifyGraphChanged(string target = null)
    {
        PushHistory(target);
        if (GraphChanged != null) GraphChanged();
    }

    // 这笔编辑并进上一笔的条件：批还开着，而且要么正拖着、要么改的是同一格。
    // 抽成纯函数，Editor 自测直接喂真值表，不用真点鼠标。
    public static bool ShouldMergeIntoBatch(bool inBatch, bool dragging, bool sameTarget)
    {
        return inBatch && (dragging || sameTarget);
    }

    void PushHistory(string target)
    {
        if (graph == null) return;
        bool same = !string.IsNullOrEmpty(target) && target == lastEditTarget;
        // 打字式改参没人开批，这里替它开一个；换目标、撤销、换图时统一收掉
        if (same && !history.InBatch && !nodeDragging) history.BeginBatch();
        if (ShouldMergeIntoBatch(history.InBatch, nodeDragging, same)) { history.Commit(graph); return; }
        history.EndBatch();
        history.Commit(graph);
        lastEditTarget = target;
    }

    public bool CanUndo { get { return history.CanUndo; } }
    public bool CanRedo { get { return history.CanRedo; } }

    // 撤销/恢复：把历史里的快照还原成活图重铺一遍，再通知外部（自动存档、重编译）。
    // 回放绝不能 Reset 历史，否则一撤销就把栈清空，恢复按钮当场失效。
    public void DoUndo()
    {
        history.EndBatch();               // 手上那笔还没收完（拖到一半按 Ctrl+Z）先落地，免得半截改动凭空消失
        var g = history.Undo();
        if (g == null) return;
        LoadGraph(g, false);
        RaiseGraphChanged();              // 还原后的图与栈顶等价，Commit 自己会丢弃，不会多塞一条
    }

    public void DoRedo()
    {
        history.EndBatch();
        var g = history.Redo();
        if (g == null) return;
        LoadGraph(g, false);
        RaiseGraphChanged();
    }

    // 进关、读档、看答案：历史整体作废，以这张新图为栈底
    public void ResetHistory()
    {
        history.Reset(graph);
        lastEditTarget = null;
    }

    // 正在拉线的输出端口，松手时吸附到最近的可用输入端口
    Port pendingLink;
    const float LinkSnapRadius = 46f;
    // 脚本化拉线演示用的指针数据（-linkdrag），非空表示演示正在进行
    PointerEventData demoPed;
    Port demoPressPort;

    public Vector2 PortLocal(string uid, string portId, bool output)
    {
        var p = FindPort(uid, portId, output);
        if (p == null || content == null) return Vector2.zero;
        return content.InverseTransformPoint(p.transform.position);
    }

    // 把关卡里所有节点缩放到可视区域内（L2/L3 的坐标跨度比画布大）
    public void FitToContent()
    {
        if (cards == null || cards.Count == 0) return;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in cards.Values)
        {
            if (c == null || c.Node == null) continue;
            minX = Mathf.Min(minX, c.Node.X);
            minY = Mathf.Min(minY, c.Node.Y);
            maxX = Mathf.Max(maxX, c.Node.X + c.Rect.sizeDelta.x);
            maxY = Mathf.Max(maxY, c.Node.Y + c.Rect.sizeDelta.y);
        }
        if (minX == float.MaxValue) return;
        float w = Mathf.Max(1f, maxX - minX), h = Mathf.Max(1f, maxY - minY);
        const float margin = 36f;
        float fit = Mathf.Min((ContentW - margin * 2f) / w, (ContentH - margin * 2f) / h);
        float z = Mathf.Clamp(Mathf.Min(1f, fit), .42f, 1f);
        float cx = (minX + maxX) * .5f, cy = (minY + maxY) * .5f;
        var local = new Vector2(-ContentW * .5f + cx, ContentH * .5f - cy) * z;
        SetViewTarget(z, -local);
    }

    // 把某个模块挪到画布正中：验收截图和「跳到出错模块」都用得上
    public bool CenterOnNode(string uid, float zoomWant)
    {
        NodeCard c;
        if (string.IsNullOrEmpty(uid) || !cards.TryGetValue(uid, out c) || c == null || c.Node == null) return false;
        float z = Mathf.Clamp(zoomWant, .42f, 1.8f);
        float cx = c.Node.X + c.Rect.sizeDelta.x * .5f, cy = c.Node.Y + c.Rect.sizeDelta.y * .5f;
        SetViewTarget(z, new Vector2(ContentW * .5f - cx, cy - ContentH * .5f) * z);
        return true;
    }

    // 镜头补间：缩放、平移、适应、居中一律只改目标值，Update 每帧朝目标追，
    // 滚轮连按、按钮连点都不会一跳一跳。InstantView 给自测截图用，保证拿到最终机位。
    float wantZoom = 1f;
    Vector2 wantPos;
    bool viewFollow;
    public bool InstantView;

    void SetViewTarget(float z, Vector2 pos)
    {
        if (content == null) return;
        if (InstantView) { ApplyView(z, pos); viewFollow = false; return; }
        wantZoom = z; wantPos = pos; viewFollow = true;
    }

    void ApplyView(float z, Vector2 pos)
    {
        bool zoomChanged = Mathf.Abs(z - zoom) > .0005f;
        zoom = z;
        content.localScale = Vector3.one * z;
        content.anchoredPosition = pos;
        if (zoomChanged) ApplyPortZoom(z);
        RefreshLines();
    }

    void ApplyPortZoom(float z)
    {
        if (cards == null) return;
        foreach (var kv in cards)
        {
            var c = kv.Value;
            if (c == null || c.Ports == null) continue;
            foreach (var p in c.Ports) if (p != null) p.ApplyZoom(z);
        }
    }

    void StepViewFollow()
    {
        if (!viewFollow || content == null) return;
        float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 16f);
        float z = Mathf.Lerp(zoom, wantZoom, k);
        var pos = Vector2.Lerp(content.anchoredPosition, wantPos, k);
        if (Mathf.Abs(z - wantZoom) < .002f && (pos - wantPos).sqrMagnitude < .5f)
        {
            ApplyView(wantZoom, wantPos);
            viewFollow = false;
            return;
        }
        ApplyView(z, pos);
    }

    // 手拖平移时目标值要跟手，否则松手后镜头会往旧目标回弹
    void SyncViewTarget()
    {
        wantZoom = zoom;
        wantPos = content != null ? content.anchoredPosition : wantPos;
    }

    public void NotifyDelete(NodeCard c)
    {
        if (c == null) return;
        if (NodeDeleteRequested != null) NodeDeleteRequested(c.Node);
        else DeleteNode(c.Node != null ? c.Node.Uid : null);
    }

    public void NotifyEdit(NodeCard c)
    {
        if (c != null && c.Node != null && NodeEditRequested != null) NodeEditRequested(c.Node);
    }

    public void NotifyParams(NodeCard c)
    {
        if (c == null || c.Node == null || NodeParamsRequested == null) return;
        Debug.Log("[交互] 双击「循环变量」那一行 → 开参数窗");
        NodeParamsRequested(c.Node);
    }

    // 卡片上的表达式区被双击、但这类模块只有一个统一面板时走这里：
    // 开同一个参数窗，并把光标直接落进被双击的那个字段
    public event Action<GraphNode, string> NodeParamsFocusRequested;

    public void NotifyParamsFocused(NodeCard c, string focusKey)
    {
        if (c == null || c.Node == null) return;
        Debug.Log("[交互] 双击「" + (c.Node.Type) + "」的表达式区 → 开统一参数窗并聚焦 " + focusKey);
        if (NodeParamsFocusRequested != null) NodeParamsFocusRequested(c.Node, focusKey);
        else NotifyParams(c);
    }

    public void DeleteNode(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return;
        var pinned = GraphUtil.FindNode(graph, uid);
        if (pinned != null && IsPinned(pinned.Type)) return;   // 常驻板删不掉，要不要提示由调用方决定
        NodeCard c;
        if (cards.TryGetValue(uid, out c) && c != null) Destroy(c.gameObject);
        cards.Remove(uid);
        graph.Nodes.RemoveAll(n => n != null && n.Uid == uid);
        graph.Links.RemoveAll(l => l.FromNode == uid || l.ToNode == uid);
        RefreshLines();
        RefreshScopeFields();
        RaiseGraphChanged();
    }

    public static GraphView Create(RectTransform parent)
    {
        var go = new GameObject("GraphView", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>(); rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.offsetMin=rt.offsetMax=Vector2.zero;
        var v = go.AddComponent<GraphView>(); v.Initialize(rt); return v;
    }

    void Initialize(RectTransform rt)
    {
        host = rt;
        var hit = host.gameObject.GetComponent<Image>();
        if (hit == null) hit = host.gameObject.AddComponent<Image>();
        hit.sprite = White;
        hit.type = Image.Type.Simple;
        hit.color = new Color(1f, 1f, 1f, 0f);
        hit.raycastTarget = true;

        content = Child("Content", host);
        content.anchorMin = content.anchorMax = new Vector2(.5f, .5f);
        content.pivot = new Vector2(.5f, .5f);
        content.sizeDelta = new Vector2(ContentW, ContentH);
        content.anchoredPosition = Vector2.zero;

        lineLayer = Child("Links", content); Stretch(lineLayer);
        lines = lineLayer.gameObject.AddComponent<LineGraphic>();
        lines.Owner = this; lines.raycastTarget = false;
        loopLayer = Child("LoopBodies", content); Stretch(loopLayer);
        nodeLayer = Child("Nodes", content); Stretch(nodeLayer);
        // 数组 / 栈的悬浮看板画在卡片之上；外壳只让标题条和拉手接射线，其余点得穿
        vizLayer = Child("Viz", content); Stretch(vizLayer);

        // LoadGraph 调用过之后就不再自动铺 demo 图
        if (!loaded) { BuildDemo(); ResetHistory(); }
        StartCoroutine(RefreshNextFrame());
    }

    IEnumerator RefreshNextFrame()
    {
        yield return null;          // 等一帧，让 RectTransform 的 world 矩阵就绪再算端点
        yield return null;
        RefreshLines();
    }

    // 初始 demo：整体居中，四周留白，不贴边。
    // 数据线已经彻底退出画布，demo 只铺一条执行链，不再摆常量和运算卡。
    void BuildDemo()
    {
        graph.LevelId = "chapter1";
        AddNode("n0", "entry", 40, 120);
        AddNode("n1", "input", 250, 90);
        AddNode("n4", "output", 700, 120);
        graph.Links.Add(new GraphLink { FromNode = "n0", FromPort = "out", ToNode = "n1", ToPort = "in", Kind = LinkKind.Exec });
        graph.Links.Add(new GraphLink { FromNode = "n1", FromPort = "out", ToNode = "n4", ToPort = "in", Kind = LinkKind.Exec });
        RefreshLines();
    }

    void AddNode(string uid, string type, float x, float y)
    {
        var n = new GraphNode { Uid = uid, Type = type, X = x, Y = y }; graph.Nodes.Add(n);
        var c = NodeCard.Create(nodeLayer, this, n); cards[uid] = c;
    }

    // ---------------- 对外接口（App / Inspector 用） ----------------

    // 用外部图整体替换当前画布：清掉旧卡片，按 g.Nodes 重建，再刷新连线。
    // resetHistory 只有撤销/恢复的回回放 false：换图是换图，历史栈不能被一起清空。
    public void LoadGraph(Graph g, bool resetHistory = true)
    {
        ClearGraph();
        loaded = true;
        if (g == null) return;
        graph = g;
        NodeCard.LevelStructs = g.Structs ?? new List<StructDef>();
        foreach (var n in graph.Nodes)
        {
            if (n == null || string.IsNullOrEmpty(n.Uid)) continue;
            cards[n.Uid] = NodeCard.Create(nodeLayer, this, n);
        }
        RespaceChains();
        RefreshLines();
        if (resetHistory) ResetHistory();
    }

    // 卡片带不带模块动图舞台高度差很多，手摆的关卡坐标可能因此叠在一起：
    // 沿执行链自上而下把 Y 推开，保证上下至少留 24px；X 不动，横向关系不变。
    public void RespaceChains()
    {
        if (graph == null || cards == null || cards.Count == 0) return;
        // 循环体末尾会连回循环头形成回边，算高度时必须跳过，否则循环头会被自己的循环体推到下面去
        var backEdges = new HashSet<GraphLink>();
        foreach (var n in graph.Nodes)
        {
            if (n == null || (n.Type != "forloop" && n.Type != "whileloop")) continue;
            var body = new HashSet<string>(LoopBodyNodes(n));
            foreach (var l in graph.Links)
                if (l != null && l.Kind == LinkKind.Exec && l.ToNode == n.Uid && body.Contains(l.FromNode)) backEdges.Add(l);
        }
        var placed = new Dictionary<string, float>();
        foreach (var uid in new List<string>(cards.Keys)) Place(uid, new HashSet<string>());
        foreach (var kv in placed)
        {
            NodeCard c;
            var n = graph.Nodes.Find(x => x != null && x.Uid == kv.Key);
            if (n == null || !cards.TryGetValue(kv.Key, out c) || c == null) continue;
            if (Mathf.Abs(n.Y - kv.Value) < .01f) continue;
            n.Y = kv.Value;
            c.Rect.anchoredPosition = new Vector2(n.X, -n.Y);
        }

        float Place(string uid, HashSet<string> stack)
        {
            float y;
            if (placed.TryGetValue(uid, out y)) return y;
            NodeCard c;
            if (!cards.TryGetValue(uid, out c) || c == null || c.Node == null) return 0f;
            if (!stack.Add(uid)) return c.Node.Y;                 // 有环就保持原坐标，不跟自己递归
            float want = c.Node.Y;
            foreach (var l in graph.Links)
            {
                if (l == null || l.Kind != LinkKind.Exec || l.ToNode != uid || backEdges.Contains(l)) continue;
                NodeCard p;
                if (!cards.TryGetValue(l.FromNode, out p) || p == null || p.Node == null) continue;
                want = Mathf.Max(want, Place(l.FromNode, stack) + p.Rect.sizeDelta.y + 24f);
            }
            stack.Remove(uid);
            placed[uid] = want;
            return want;
        }
    }

    public void ClearGraph()
    {
        // 卡片要全销毁，先把手上的拉线撤掉，否则橡皮筋还指着已销毁的端口
        pendingLink = null;
        if (lines != null) lines.ClearGhost();
        foreach (var c in cards.Values) if (c != null) Destroy(c.gameObject);
        cards.Clear();
        foreach (var kv in loopFrames) if (kv.Value != null) Destroy(kv.Value);
        loopFrames.Clear();
        loopDrags.Clear();
        DestroyArrayViz();
        DestroyBoards();
        if (nodeLayer != null)
            for (int i = nodeLayer.childCount - 1; i >= 0; i--) Destroy(nodeLayer.GetChild(i).gameObject);
        if (vizLayer != null)
            for (int i = vizLayer.childCount - 1; i >= 0; i--) Destroy(vizLayer.GetChild(i).gameObject);
        graph = new Graph();
        NodeCard.LevelStructs = graph.Structs;
        HideSelection();
        RefreshLines();
    }

    public NodeCard FindCard(string uid)
    {
        NodeCard c;
        return uid != null && cards.TryGetValue(uid, out c) ? c : null;
    }

    // 截图验收用：把每张卡片中心的屏幕坐标按「左上原点、0..1 归一化」打出来，
    // tools/real_input.py 的场景文件照着这个坐标下真实鼠标，不用再靠肉眼量像素。
    public string CardRectLog()
    {
        var sb = new StringBuilder();
        foreach (var kv in cards)
        {
            var c = kv.Value;
            if (c == null || c.Node == null || c.Rect == null) continue;
            var sp = RectTransformUtility.WorldToScreenPoint(null, c.Rect.TransformPoint(c.Rect.rect.center));
            // 卡片尺寸同时给「卡片坐标」和「屏幕像素」两栏：#115 按内容伸缩是不是真落地，
            // 光看自测里的逻辑高度不够，要能在这一次运行的日志里量出屏幕上占了多少像素。
            var sc = c.Rect.lossyScale;
            float pw = c.Rect.rect.width * Mathf.Abs(sc.x), ph = c.Rect.rect.height * Mathf.Abs(sc.y);
            sb.Append("[rect] ").Append(c.Node.Type).Append(" ").Append(kv.Key).Append(" 归一化(左上)=(")
              .Append((sp.x / Screen.width).ToString("F4")).Append(", ")
              .Append((1f - sp.y / Screen.height).ToString("F4")).Append(")")
              .Append(" 卡面=").Append(c.Rect.rect.width.ToString("F0")).Append("x").Append(c.Rect.rect.height.ToString("F0"))
              .Append(" 屏幕=").Append(pw.ToString("F0")).Append("x").Append(ph.ToString("F0")).Append("\n");
        }
        return sb.ToString();
    }

    // 端口（#99 做大的那个「头头」）坐标 + 实际屏幕尺寸：
    // 热区在卡片坐标里是固定的，镜头一缩放屏幕上的手感就变了，所以两栏都打出来量。
    public string PortRectLog()
    {
        var sb = new StringBuilder();
        foreach (var kv in cards)
        {
            var c = kv.Value;
            if (c == null || c.Node == null) continue;
            foreach (var q in c.Ports)
            {
                if (q == null || q.Rect == null) continue;
                var s = q.Rect.lossyScale;
                float dw = q.Rect.rect.width * Mathf.Abs(s.x), dh = q.Rect.rect.height * Mathf.Abs(s.y);
                float hw = q.HitRect != null ? q.HitRect.rect.width * Mathf.Abs(q.HitRect.lossyScale.x) : 0f;
                float hh = q.HitRect != null ? q.HitRect.rect.height * Mathf.Abs(q.HitRect.lossyScale.y) : 0f;
                var sp = RectTransformUtility.WorldToScreenPoint(null, q.Rect.TransformPoint(q.Rect.rect.center));
                sb.Append("[port] ").Append(c.Node.Type).Append(" ").Append(kv.Key).Append(' ')
                  .Append(q.PortId).Append(q.Output ? " 出" : " 入").Append(' ').Append(q.Kind)
                  .Append(" 屏幕=").Append(dw.ToString("F1")).Append('x').Append(dh.ToString("F1"))
                  .Append(" 热区=").Append(hw.ToString("F1")).Append('x').Append(hh.ToString("F1"))
                  .Append(" 归一化(左上)=(")
                  .Append((sp.x / Screen.width).ToString("F4")).Append(", ")
                  .Append((1f - sp.y / Screen.height).ToString("F4")).Append(")\n");
            }
        }
        return sb.ToString();
    }

    public Port FindPort(string uid, string portId, bool output)
    {
        var card = FindCard(uid);
        if (card == null || portId == null) return null;
        foreach (var port in card.Ports)
            if (port.PortId == portId && port.Output == output) return port;
        return null;
    }

    public bool TryGetNodeWorldCenter(string uid, out Vector3 worldPosition)
    {
        var card = FindCard(uid);
        if (card != null && card.Rect != null)
        {
            var center = card.Rect.rect.center;
            worldPosition = card.Rect.TransformPoint(new Vector3(center.x, center.y, 0f));
            return true;
        }
        worldPosition = Vector3.zero;
        return false;
    }

    public bool TryGetPortWorldCenter(string uid, string portId, bool output, out Vector3 worldPosition)
    {
        var port = FindPort(uid, portId, output);
        if (port != null && port.Rect != null)
        {
            worldPosition = port.Rect.TransformPoint(Vector3.zero);
            return true;
        }
        worldPosition = Vector3.zero;
        return false;
    }

    // 取一条连线的绘制路径（与 LineGraphic 画线用的完全同一条三次贝塞尔），
    // 让数字沿玩家看到的引线走，而不是直线穿过画布。
    public List<Vector2> LinkPath(GraphLink link, int samples = 18)
    {
        var pts = new List<Vector2>();
        if (link == null || content == null) return pts;
        var from = FindPort(link.FromNode, link.FromPort, true);
        var to = FindPort(link.ToNode, link.ToPort, false);
        if (from == null || to == null) return pts;
        float half = PortSize * .5f;
        Vector2 a = content.InverseTransformPoint(from.transform.position);
        a = new Vector2(a.x + half, a.y);
        Vector2 b = content.InverseTransformPoint(to.transform.position);
        b = new Vector2(b.x - half, b.y);
        return CurvePath(a, b, samples);
    }

    // 两点之间的三次贝塞尔采样（与 LineGraphic 画线同一套算式），坐标系是 content 局部
    public List<Vector2> CurvePath(Vector2 a, Vector2 b, int samples = 18)
    {
        var pts = new List<Vector2>();
        if (content == null) return pts;
        float dx = Mathf.Abs(b.x - a.x) * .45f;
        var p0 = a + new Vector2(dx, 0f);
        var p1 = b - new Vector2(dx, 0f);
        for (int i = 0; i <= samples; i++)
        {
            float k = i / (float)samples;
            float u = 1f - k;
            pts.Add(u * u * u * a + 3f * u * u * k * p0 + 3f * u * k * k * p1 + k * k * k * b);
        }
        return pts;
    }

    // 卡片中心在 content 坐标系里的位置；单步动画在两个模块没有正式引线时用它兜底
    public Vector2 CardCenterLocal(string uid)
    {
        var c = FindCard(uid);
        if (c == null || content == null) return Vector2.zero;
        return content.InverseTransformPoint(c.Rect.TransformPoint(c.Rect.rect.center));
    }

    public void HighlightNode(string uid, bool on)
    {
        var c = FindCard(uid);
        if (c != null) c.SetHighlight(on);
    }

    public void ClearHighlight()
    {
        foreach (var c in cards.Values) if (c != null) c.SetHighlight(false);
    }

    public void NotifySelected(NodeCard c) { if (c != null && NodeSelected != null) NodeSelected(c.Node); }

    // 断点圆点：切换状态并刷新所有卡片上的圆点颜色
    public void ToggleBreakpoint(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return;
        var node = graph.Nodes.Find(n => n != null && n.Uid == uid);
        if (node == null) return;
        if (NodeBreakpointToggled != null) NodeBreakpointToggled(node);
        else SetBreakpointDot(uid, false);
    }

    public void SetBreakpointDot(string uid, bool on)
    {
        var c = FindCard(uid);
        if (c != null && c.breakDot != null)
            c.breakDot.color = on ? Theme.Coral : Theme.Border;
    }

    // 变量增删或改名后，刷新所有卡片上的表达式字段，让左侧元素栏跟着更新
    public void RefreshExprVariables()
    {
        foreach (var c in cards.Values)
            if (c != null) c.RebuildExprFields();
    }

    // 结构实例改名后，所有引用它的卡片（创建栈 / 压入 / 弹出 / 查看…）的「栈 · 名字」都要跟着换。
    // 名字只是显示用（代码里认的是 Id），所以只刷文案，不动卡的尺寸。
    public void RefreshStructLabels()
    {
        foreach (var c in cards.Values)
        {
            if (c == null || c.Node == null) continue;
            string type = c.Node.Type ?? "";
            if (type == "structnew" || type.StartsWith("stack_") || type.StartsWith("queue_"))
                c.RefreshSummary();
        }
    }

    // 连线 / 增删模块都会改变每个模块「此刻看得见的变量」，重算各卡片元素栏。
    // 表达式小窗正开着时不重建——那会把玩家正在输入的字段连根拔掉。
    public void RefreshScopeFields()
    {
        if (ExpressionFieldUI.AnyOpen) return;
        RefreshExprVariables();
    }

    public void OnPointerDown(PointerEventData e)
    {
        bool onBlank = IsBlankPress(e);
        Debug.Log("[交互] 画布按下 键=" + e.button + " 命中=" + (e.pointerPress != null ? e.pointerPress.name : "无") + " 空白=" + onBlank);
        // 拉线进行中点空白处 = 取消这次拉线（不顺手开一个框选）
        if (e.button == PointerEventData.InputButton.Left && pendingLink != null && onBlank) { CancelLink(); return; }
        // 平移交给中键（空白处右键也可以）：左键在空白处按下专门用来框选模块
        if (e.button == PointerEventData.InputButton.Middle || (e.button == PointerEventData.InputButton.Right && onBlank))
        { panning = true; panStart = e.position; contentStart = content.anchoredPosition; }
        if (e.button == PointerEventData.InputButton.Left && onBlank)
        {
            selecting = true; selectStart = Local(e.position); ShowSelection(selectStart, selectStart);
            foreach (var c in cards.Values) if (c != null && c.Selected) { c.Selected = false; c.SetSelected(false); }
        }
    }
    // 「空白处」= 按到的东西不属于任何模块卡片，也不属于端口。
    // 只比 e.pointerPress == gameObject 不够：真实射线命中的是画布背景那一层，
    // 上一轮的脚本化演示把 pointerPress 直接塞成了画布自身，等于绕过了这条判断，所以看着是好的。
    bool IsBlankPress(PointerEventData e)
    {
        var go = e.pointerPress;
        if (go == null || go == gameObject) return true;
        return go.GetComponentInParent<NodeCard>() == null && go.GetComponentInParent<Port>() == null
            && go.GetComponentInParent<FloatPanel>() == null;
    }

    public void OnPointerUp(PointerEventData e)
    {
        panning=false;
        if(selecting){selecting=false; HideSelection(); Debug.Log("[交互] 框选结束 选中 " + SelectedCount() + " 块");}
        // 端口上松手时 Port 已经处理过；这里兜住「在端口之外的空白处松手」的落点吸附
        if (pendingLink != null) TrySnapLink(e.position);
    }
    public void OnDrag(PointerEventData e){ if(panning){content.anchoredPosition=contentStart+(e.position-panStart);SyncViewTarget();RefreshLines();return;} if(selecting){var p=Local(e.position); ShowSelection(selectStart,p); SelectInRect(selectStart,p);} }
    public void OnScroll(PointerEventData e)
    {
        // Ctrl + 滚轮：向上放大、向下缩小；普通滚轮：平移画布。
        // 只改目标值，Update 里朝目标补间，滚轮连按也是顺滑的一档一档。
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            // 下限与 FitToContent 一致：整张图纵向排开时，缩到这块才装得下
            wantZoom = Mathf.Clamp(wantZoom + e.scrollDelta.y * .08f, .42f, 1.8f);
        }
        else
        {
            // 滚轮向上 = 看更上面的内容，所以画布内容要跟着往下走：位移取负。
            // 之前这里是正的，向上滚反而露出下面的部分，方向是反的。
            wantPos -= new Vector2(e.scrollDelta.x, e.scrollDelta.y) * 28f;
        }
        viewFollow = true;
    }
    Vector2 Local(Vector2 screen){RectTransformUtility.ScreenPointToLocalPointInRectangle(content,screen,null,out var p); return p;}
    public Vector2 PointToLocal(Vector2 screen){ return Local(screen); }

    // 一次拖动的组：按下时选中的卡片 + 被拖的循环节点自己的循环体
    readonly List<NodeCard> dragCards = new List<NodeCard>();
    readonly List<Vector2> dragFrom = new List<Vector2>();
    Vector2 dragStart;
    bool nodeDragging;

    // 拖到画布边缘时镜头自动跟着走：框选和拖模块都靠它把视野外的部分拽进来，
    // 否则鼠标顶在边上就再也够不着画面外的模块。速度按「越贴边越快」线性给。
    // 上限 460 屏像素 / 秒：实测过 900 时贴边按住一秒多就把整张图推出视野，玩家找不到模块。
    const float EdgeMargin = 56f;
    const float EdgeSpeed = 460f;

    void StepEdgePan()
    {
        if (!(selecting || nodeDragging) || content == null) return;
        var view = transform as RectTransform;
        if (view == null) return;
        Vector2 sp = Input.mousePosition;
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(view, sp, null, out lp)) return;
        var r = view.rect;
        float ax = EdgeAxis(lp.x - r.xMin, r.xMax - lp.x);
        float ay = EdgeAxis(lp.y - r.yMin, r.yMax - lp.y);
        if (ax == 0f && ay == 0f) return;
        // 要看的方向和 content 的位移方向相反
        content.anchoredPosition -= new Vector2(ax, ay) * (EdgeSpeed * Time.unscaledDeltaTime);
        SyncViewTarget();
        RefreshLines();
        if (selecting) { var p = Local(sp); ShowSelection(selectStart, p); SelectInRect(selectStart, p); }
        else ApplyNodeDrag(sp);
    }

    // 距某一侧边界的余量换成 -1..1 的方向系数；不在边界带里返回 0。拖出画布外按满速算。
    public static float EdgeAxis(float near, float far)
    {
        if (near < 0f) return -1f;
        if (far < 0f) return 1f;
        if (near < EdgeMargin) return -(1f - near / EdgeMargin);
        if (far < EdgeMargin) return 1f - far / EdgeMargin;
        return 0f;
    }

    public void BeginNodeDrag(NodeCard c, PointerEventData e)
    {
        dragCards.Clear(); dragFrom.Clear();
        nodeDragging = true;
        // 一次按住可能连着几十帧挪位：开批，松手时只留一条历史
        history.BeginBatch();
        dragStart = Local(e.position);
        CollectDrag(c);
        foreach (var kv in cards) if (kv.Value != null && kv.Value.Selected) CollectDrag(kv.Value);
        // 循环节点被拖着走时循环体跟着一起走，否则框住的范围会越拉越大
        var n = c != null ? c.Node : null;
        if (n != null && (n.Type == "forloop" || n.Type == "whileloop"))
            foreach (var uid in LoopBodyNodes(n)) CollectDragHidden(FindCard(uid));
    }

    void CollectDrag(NodeCard k)
    {
        if (k == null || !k.gameObject.activeSelf || dragCards.Contains(k)) return;
        dragCards.Add(k);
        dragFrom.Add(k.Rect.anchoredPosition);
    }

    // 折叠起来的循环体卡片是 SetActive(false) 的，但它照样得跟着循环头走，
    // 否则一展开就散在原地，循环框会突然变大。
    void CollectDragHidden(NodeCard k)
    {
        if (k == null || dragCards.Contains(k)) return;
        dragCards.Add(k);
        dragFrom.Add(k.Rect.anchoredPosition);
    }

    public void DragNode(NodeCard c, PointerEventData e) { ApplyNodeDrag(e.position); }

    // 位置按屏幕点算，边缘自动平移时也能直接喂鼠标坐标
    void ApplyNodeDrag(Vector2 screenPos)
    {
        var d = Local(screenPos) - dragStart;
        for (int i = 0; i < dragCards.Count; i++)
        {
            var k = dragCards[i];
            if (k == null || k.Node == null) continue;
            var p = dragFrom[i] + d;
            k.Rect.anchoredPosition = p;
            var n = graph.Nodes.Find(x => x.Uid == k.Node.Uid);
            if (n != null) { n.X = p.x; n.Y = -p.y; }
        }
        RefreshLines();
        RefreshLoopBodies();
        // 拖动途中每帧只往批里存最新一份（批内的 Commit 不落栈），松手统一收
        if (nodeDragging && history.InBatch) history.Commit(graph);
    }
    public void EndNodeDrag(NodeCard c)
    {
        nodeDragging = false; c.Rect.localRotation = Quaternion.identity;
        // 松手才算一笔挪位结束：收批 + 落一条历史（没真挪动会被 GraphHistory 丢掉）
        PushHistory(null);
    }

    // 拖循环体外框的标题条：只带循环头和它自己的循环体，不牵扯画布上别的已选中卡片
    public void BeginLoopFrameDrag(string uid, PointerEventData e)
    {
        dragCards.Clear(); dragFrom.Clear();
        nodeDragging = true;
        history.BeginBatch();
        dragStart = Local(e.position);
        var head = FindCard(uid);
        if (head == null || head.Node == null) return;
        CollectDrag(head);
        foreach (var u in LoopBodyNodes(head.Node)) CollectDragHidden(FindCard(u));
        Debug.Log("[交互] 拖动循环体外框 " + uid + "，一起走 " + dragCards.Count + " 块");
    }

    public void EndLoopFrameDrag() { nodeDragging = false; RaiseGraphChanged(); }   // 改图通知顺带把拖动的批收成一条历史

    public int SelectedCount()
    {
        int k = 0;
        foreach (var c in cards.Values) if (c != null && c.gameObject.activeSelf && c.Selected) k++;
        return k;
    }

    public NodeCard FirstSelectedCard()
    {
        foreach (var c in cards.Values) if (c != null && c.gameObject.activeSelf && c.Selected) return c;
        return null;
    }

    // 当前所有卡片在 content 局部坐标下的包围盒；脚本化框选（-marquee）按它取两个角
    public bool ContentBounds(out Rect r)
    {
        r = new Rect(); bool any = false;
        foreach (var c in cards.Values)
        {
            if (c == null || !c.gameObject.activeSelf) continue;
            var o = c.Rect.localPosition;
            var cr = new Rect(o.x, o.y - c.Rect.sizeDelta.y, c.Rect.sizeDelta.x, c.Rect.sizeDelta.y);
            r = any ? Rect.MinMaxRect(Mathf.Min(r.xMin, cr.xMin), Mathf.Min(r.yMin, cr.yMin),
                                      Mathf.Max(r.xMax, cr.xMax), Mathf.Max(r.yMax, cr.yMax)) : cr;
            any = true;
        }
        return any;
    }

    // Local() 的反向：把 content 局部坐标换回屏幕点，让脚本化的指针事件走真实坐标链路
    public Vector2 ScreenFromContent(Vector2 local)
    {
        return RectTransformUtility.WorldToScreenPoint(null, content.TransformPoint(new Vector3(local.x, local.y, 0f)));
    }
    public void AddPaletteNode(string type, Vector2 screenPosition, string[] preset = null)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPosition, null, out var p);
        var uid = "n" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var node = new GraphNode { Uid = uid, Type = type, X = p.x + ContentW * .5f, Y = ContentH * .5f - p.y };
        // 同一个节点类型可以有多个入口（创建变量 / 创建数组），差别就在预置参数上
        if (preset != null)
            for (int i = 0; i + 1 < preset.Length; i += 2) node.Params[preset[i]] = preset[i + 1];
        graph.Nodes.Add(node);
        var card = NodeCard.Create(nodeLayer, this, node);
        cards[uid] = card;
        ClearDropPreview();
        // 落在已有模块身上就自动把两个执行头接上，省得每加一块都要再拉一次线
        AutoLinkDrop(card, node);
        // 卡片和浮窗在格子空着时都按 NodeTypes 的默认值画（创建变量画的是「v =」），可图里存的仍是空串：
        // 拖一块创建变量下来、一个字没碰就吃一句「变量名不是合法标识符」。所见即所存，把看到的默认值写进去。
        // 放在联动抄参数之后，抄来的名字（前面那块模块已定好的变量名）优先于默认值。
        if (FillParamDefaults(node)) { card.SyncExprFields(); card.RefreshSummary(); }
        RefreshLines();
        RefreshScopeFields();
        RaiseGraphChanged();
        SoundSystem.Play("place");
        Debug.Log("[add] " + type + " " + uid + " 节点数 " + graph.Nodes.Count + " 可撤销=" + history.CanUndo);
    }

    // 把没填的参数格子补上类型表里的默认值；补过返回 true，调用方要重画这张卡
    bool FillParamDefaults(GraphNode node)
    {
        var spec = NodeTypes.Get(node.Type);
        if (spec == null) return false;
        bool any = false;
        for (int i = 0; i < spec.Params.Count; i++)
        {
            var pd = spec.Params[i];
            if (string.IsNullOrEmpty(pd.Default)) continue;
            string cur;
            if (node.Params.TryGetValue(pd.Id, out cur) && !string.IsNullOrEmpty(cur)) continue;
            node.Params[pd.Id] = pd.Default;
            any = true;
        }
        return any;
    }

    // ---------------- 落点自动连线 / 相邻摆放 ----------------

    // 新模块落在宿主哪一侧：由落点相对宿主中心的方位决定，不再只有「上面 / 下面」两种。
    public enum DropSide { Below, Above, Left, Right }

    // 落点在宿主的哪个方位。横向偏移按半宽归一、纵向按半高归一再比大小：
    // 不归一的话，一张又窄又高的卡片永远判成上/下，玩家明明点在它右边却掉到下面。
    // 正好压在中心线上时算下方（与旧的「下半身 = 接在后面」一致）。
    public static DropSide DropSideOf(float cx, float cy, float hostX, float hostY, float hostW, float hostH)
    {
        float dx = (cx - (hostX + hostW * .5f)) / Mathf.Max(1f, hostW * .5f);
        float dy = (cy - (hostY + hostH * .5f)) / Mathf.Max(1f, hostH * .5f);
        if (Mathf.Abs(dx) > Mathf.Abs(dy)) return dx > 0f ? DropSide.Right : DropSide.Left;
        return dy >= 0f ? DropSide.Below : DropSide.Above;
    }

    // 落在下方 / 右方 = 接在宿主后面；落在上方 / 左方 = 插到宿主前面。
    public static bool DropAfter(DropSide side) { return side == DropSide.Below || side == DropSide.Right; }

    static bool IsLoopHead(GraphNode n) { return n != null && (n.Type == "forloop" || n.Type == "whileloop"); }

    // 相邻间隔。循环头下面挂着整个循环体外框，贴着放会被外框压住，所以只要有一方是循环头
    // 就把间隔拉开；横向并排时外框也会往两边溢出，所以左右方向给得比上下更多。
    public static float DropGap(DropSide side, GraphNode host, GraphNode incoming, float frameBelow)
    {
        bool loop = IsLoopHead(host) || IsLoopHead(incoming);
        if (side == DropSide.Left || side == DropSide.Right) return loop ? 72f : 24f;
        return loop ? 48f + Mathf.Max(0f, frameBelow) : 12f;
    }

    // 摆放后的左上角（节点坐标：X 向右、Y 向下）。
    // gap 由 DropGap 定，循环体外框多占的像素已经并进 gap，这里不再叠加。
    public static Vector2 DropTopLeft(DropSide side, float hostX, float hostY, float hostW, float hostH,
                                      float myW, float myH, float gap)
    {
        switch (side)
        {
            case DropSide.Above:
                return new Vector2(hostX + (hostW - myW) * .5f, Mathf.Max(0f, hostY - myH - gap));
            case DropSide.Left:
                return new Vector2(Mathf.Max(0f, hostX - myW - gap), hostY + (hostH - myH) * .5f);
            case DropSide.Right:
                return new Vector2(hostX + hostW + gap, hostY + (hostH - myH) * .5f);
            default:
                return new Vector2(hostX + (hostW - myW) * .5f, hostY + hostH + gap);
        }
    }

    GameObject dropGhostRoot;
    readonly List<Image> dropGhostDashes = new List<Image>();

    // 拖拽途中（鼠标还没松）在画布上铺一个虚线框，框住的就是松手后新模块会落的位置。
    // 尺寸跟真卡片用同一套 CardWidth / CardHeight 估，落点跟 SnapNextTo 用同一条算式，
    // 所以虚线框和真落点不会差出半个身位。
    public void ShowDropGhost(Vector2 screenPosition, string type, string[] preset)
    {
        var host = CardAtScreen(screenPosition);
        var t = NodeTypes.Get(type);
        if (host == null || t == null) { ClearDropGhost(); return; }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPosition, null, out var p);
        var probe = new GraphNode { Uid = "ghost", Type = type };
        if (preset != null)
            for (int i = 0; i + 1 < preset.Length; i += 2) probe.Params[preset[i]] = preset[i + 1];
        bool exprMode = CurrentGraph != null && CurrentGraph.ExprMode;
        float myW = NodeCard.CardWidth(t, probe, exprMode);
        float myH = NodeCard.CardHeight(t, exprMode, VerticalPorts, probe);
        float hx = host.Node.X, hy = host.Node.Y, hw = host.Rect.sizeDelta.x, hh = host.Rect.sizeDelta.y;
        var side = DropSideOf(p.x + ContentW * .5f, ContentH * .5f - p.y, hx, hy, hw, hh);
        float gap = DropGap(side, host.Node, probe, FrameBelow(host));
        var tl = DropTopLeft(side, hx, hy, hw, hh, myW, myH, gap);
        DrawDropGhost(tl, myW, myH);
    }

    // 宿主的循环体外框比卡片本体多往下占的像素（没有外框返回 0）
    float FrameBelow(NodeCard host)
    {
        if (host == null || host.Node == null) return 0f;
        GameObject go;
        if (!loopFrames.TryGetValue(host.Node.Uid, out go) || go == null) return 0f;
        var frt = (RectTransform)go.transform;
        // 外框与卡片同在 content 层、同一套「左上角 + 向下为正」的坐标，底边 = 顶边 y + 高
        float frameBottom = -frt.anchoredPosition.y + frt.sizeDelta.y;
        float cardBottom = host.Node.Y + host.Rect.sizeDelta.y;
        return Mathf.Max(0f, frameBottom - cardBottom);
    }

    void DrawDropGhost(Vector2 topLeft, float w, float h)
    {
        if (dropGhostRoot == null)
        {
            dropGhostRoot = new GameObject("DropGhost", typeof(RectTransform));
            dropGhostRoot.transform.SetParent(content, false);
        }
        var rt = (RectTransform)dropGhostRoot.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
        dropGhostRoot.transform.SetAsLastSibling();
        const float Dash = 7f, DashGap = 5f, Thick = 2f;
        int n = 0;
        for (float x = 0f; x < w; x += Dash + DashGap)
            n = LayDash(n, x, 0f, Mathf.Min(Dash, w - x), Thick);
        for (float x = 0f; x < w; x += Dash + DashGap)
            n = LayDash(n, x, h - Thick, Mathf.Min(Dash, w - x), Thick);
        for (float y = Dash + DashGap; y < h - Thick; y += Dash + DashGap)
        {
            n = LayDash(n, 0f, y, Thick, Mathf.Min(Dash, h - Thick - y));
            n = LayDash(n, w - Thick, y, Thick, Mathf.Min(Dash, h - Thick - y));
        }
        for (int i = n; i < dropGhostDashes.Count; i++) dropGhostDashes[i].enabled = false;
    }

    // 池化复用短划线：拖动每帧都在重铺，频繁建 Image 会卡
    int LayDash(int i, float x, float y, float w, float h)
    {
        Image d;
        if (i < dropGhostDashes.Count) d = dropGhostDashes[i];
        else { d = Tile(dropGhostRoot.transform, "Dash", Theme.A(Theme.Coral, .85f), false); dropGhostDashes.Add(d); }
        var r = d.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
        if (!d.enabled) d.enabled = true;
        return i + 1;
    }

    public void ClearDropGhost()
    {
        if (dropGhostRoot != null) { Destroy(dropGhostRoot); dropGhostRoot = null; }
        dropGhostDashes.Clear();
    }

    NodeCard dropPreview;

    // 从模块栏拖模块过来时，把落点下面那张卡片亮起来；带上新模块类型时再铺一个虚线落点框。
    public void PreviewDropTarget(Vector2 screenPosition, string type = null, string[] preset = null)
    {
        var hit = CardAtScreen(screenPosition);
        if (hit != dropPreview)
        {
            if (dropPreview != null) dropPreview.SetHighlight(false);
            dropPreview = hit;
            if (dropPreview != null) dropPreview.SetHighlight(true);
        }
        if (hit != null && !string.IsNullOrEmpty(type)) ShowDropGhost(screenPosition, type, preset);
        else ClearDropGhost();
    }

    public void ClearDropPreview()
    {
        if (dropPreview != null) { dropPreview.SetHighlight(false); dropPreview = null; }
        ClearDropGhost();
    }

    NodeCard CardAtScreen(Vector2 screenPosition)
    {
        foreach (var kv in cards)
        {
            var c = kv.Value;
            if (c == null || c.Node == null || !c.gameObject.activeSelf) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(c.Rect, screenPosition, null)) return c;
        }
        return null;
    }

    // 新卡片中心落在哪张卡片里（节点坐标：X 向右、Y 向下，都是左上角）
    NodeCard CardAtNodePoint(float cx, float cy, NodeCard except)
    {
        foreach (var kv in cards)
        {
            var c = kv.Value;
            if (c == null || c == except || c.Node == null || !c.gameObject.activeSelf) continue;
            if (cx < c.Node.X || cx > c.Node.X + c.Rect.sizeDelta.x) continue;
            if (cy < c.Node.Y || cy > c.Node.Y + c.Rect.sizeDelta.y) continue;
            return c;
        }
        return null;
    }

    bool HasExecIn(string uid) { return graph.Links.Find(l => l.Kind == LinkKind.Exec && l.ToNode == uid) != null; }
    bool HasExecOut(string uid, string port) { return graph.Links.Find(l => l.Kind == LinkKind.Exec && l.FromNode == uid && l.FromPort == port) != null; }

    // 第一个还空着的执行出口；全被占了返回 null（自动连线绝不顶掉玩家已经接好的线）
    string FreeExecOut(GraphNode n)
    {
        var t = NodeTypes.Get(n == null ? null : n.Type);
        if (t == null) return null;
        foreach (var e in t.ExecOut) if (!HasExecOut(n.Uid, e.Id)) return e.Id;
        return null;
    }

    // 落在宿主下半身 / 右半身 = 接在它后面，落在上半身 / 左半身 = 插到它前面。
    // 方位由 DropSideOf 按落点相对宿主中心的偏移定，不再只看纵坐标；
    // 接不接得上由 TryDropLink 判，摆位永远照方位挪 —— 接不上线也不让两块叠在一起，
    // 这样虚线落点框（ShowDropGhost）承诺的位置和真落点永远是同一个位置。
    // 命中用的是「松手那一点」也就是新卡片的左上角，和 PreviewDropTarget 的高亮同一套判定；
    // 用新卡片中心的话，往对方下半身一丢，中心已经滑出对方范围，高亮说会接、真松手却什么都不接。
    void AutoLinkDrop(NodeCard dropped, GraphNode node)
    {
        var t = NodeTypes.Get(node.Type);
        if (t == null || dropped == null) return;
        var host = CardAtNodePoint(node.X, node.Y, dropped);
        if (host == null || host.Node == null) return;
        var ht = NodeTypes.Get(host.Node.Type);
        if (ht == null) return;

        var side = DropSideOf(node.X, node.Y, host.Node.X, host.Node.Y,
                               host.Rect.sizeDelta.x, host.Rect.sizeDelta.y);
        string fromUid, fromPort, toUid, toPort;
        bool linked = TryDropLink(host, dropped, side, t, ht, out fromUid, out fromPort, out toUid, out toPort);
        PlaceAdjacent(host, dropped, side, linked ? fromPort : null);
        if (!linked)
        {
            Debug.Log("[交互] 落在宿主" + SideName(side) + "：两头空位不够，只挪开不接线");
            return;
        }
        graph.Links.Add(new GraphLink { FromNode = fromUid, FromPort = fromPort, ToNode = toUid, ToPort = toPort, Kind = LinkKind.Exec });
        ApplyDropLinkageAround(host, dropped, node, toUid);
        Debug.Log("[交互] 落点自动连线 " + fromUid + "." + fromPort + " → " + toUid + "." + toPort + "（摆在宿主" + SideName(side) + "）");
        if (LinkCreated != null)
            LinkCreated(GraphUtil.FindNode(graph, fromUid), GraphUtil.FindNode(graph, toUid));
    }

    // 线接上了才抄参数：执行流里在前的那块是「已经定好名字」的一方，在后的那块若是空白格子就顺势填上
    void ApplyDropLinkageAround(NodeCard host, NodeCard dropped, GraphNode node, string consumerUid)
    {
        NodeCard producerCard, consumerCard;
        GraphNode producer, consumer;
        if (consumerUid == node.Uid) { producerCard = host; consumerCard = dropped; producer = host.Node; consumer = node; }
        else { producerCard = dropped; consumerCard = host; producer = node; consumer = host.Node; }
        string what = ApplyDropLinkage(producer, consumer);
        if (what == null) return;
        if (consumerCard != null) { consumerCard.SyncExprFields(); consumerCard.RefreshSummary(); }
        Debug.Log("[交互] 落点联动：" + what + "（抄自 " + producer.Uid + "）");
    }

    // 落点联动：两块接上以后，把「已经在前面定好的那个变量名」顺势抄给后面那块，
    // 省掉「刚创建 a，又得双击输入把名字再敲一遍 a」这种重复劳动。
    // 只抄还没填过的格子，玩家自己写过的一个字都不动；抄不抄得对，由这张表说了算：
    //   创建变量 a（标量） → 输入：写入变量 = a            —— 玩家点名的那条
    //   创建变量 a（数组） → 计数循环：终点 = a.size        —— 遍历数组的起手式
    //   创建变量 a        → 输出：输出表达式 = a
    //   创建变量 a        → 赋值：a = （左边先铺好，光标留给右边）
    //   输入 a            → 创建变量：名字 = a              —— 反过来把读入拖到声明前面
    // 返回一句中文说明（抄了什么），没得抄返回 null。纯函数：不碰 Unity 对象，好单测。
    public static string ApplyDropLinkage(GraphNode producer, GraphNode consumer)
    {
        if (producer == null || consumer == null || producer == consumer) return null;
        var ct = NodeTypes.Get(consumer.Type);
        if (ct == null) return null;

        string subject = LinkageSubject(producer);
        if (subject != null)
        {
            bool array = DeclaredType(producer).EndsWith("[]");
            if (consumer.Type == "input")
            {
                // 数组不能直接「输入」到一个名字上，抄过去只会让编译器报一句看不懂的错
                if (IsEmpty(consumer, "name") && !array)
                {
                    consumer.Params["name"] = subject;
                    return "输入 · 写入变量 " + subject;
                }
            }
            else if (consumer.Type == "vardecl" && IsEmpty(consumer, "name"))
            {
                consumer.Params["name"] = subject;
                return "创建变量 · 名字 " + subject;
            }
            else if (consumer.Type == "output" && IsEmpty(consumer, "outputExpr"))
            {
                consumer.Params["outputExpr"] = subject;
                return "输出 · 输出表达式 " + subject;
            }
            else if (consumer.Type == "varset" && IsEmpty(consumer, "valueExpr"))
            {
                consumer.Params["name"] = subject;
                consumer.Params["valueExpr"] = subject + " = ";
                return "赋值 · " + subject + " = ";
            }
            else if (consumer.Type == "forloop" && IsEmpty(consumer, "toExpr"))
            {
                // 数组的长度跟着元素个数走，终点写 a.size 比抄一个 n 更贴「遍历一遍」
                consumer.Params["toExpr"] = array ? subject + ".size" : subject;
                return "计数循环 · 终点 " + consumer.Params["toExpr"];
            }
        }
        // 循环头后面那块多半就要用循环变量：把索引名抄给需要「下标」的赋值语句没有意义，
        // 但抄给一个空白的计数循环起点，玩家少打一次「i」。
        if (producer.Type == "forloop" && ct.Key == "forloop" && IsEmpty(consumer, "fromExpr"))
        {
            string idx = GraphUtil.Get(producer, "indexName");
            if (!string.IsNullOrEmpty(idx))
            {
                consumer.Params["fromExpr"] = idx;
                return "计数循环 · 起点 " + idx;
            }
        }
        return null;
    }

    // 「这个模块是关于哪个变量的」：创建变量 / 赋值 / 输入都直接写名字，
    // 输出和循环没有名字可抄（返回 null）
    static string LinkageSubject(GraphNode n)
    {
        if (n == null) return null;
        if (n.Type != "vardecl" && n.Type != "varset" && n.Type != "input") return null;
        var v = GraphUtil.Get(n, "name");
        return string.IsNullOrEmpty(v) ? null : v;
    }

    static bool IsEmpty(GraphNode n, string key)
    {
        string v;
        return !n.Params.TryGetValue(key, out v) || string.IsNullOrEmpty(v);
    }

    // 声明的类型没填就是默认整数；返回空串而不是 null，省得每处都判一遍
    static string DeclaredType(GraphNode n)
    {
        var v = GraphUtil.Get(n, "vtype");
        return v ?? "";
    }

    // 这一脚落下去接不接得上线：两头的空位都得是空的才接，绝不顶掉玩家已经接好的线
    bool TryDropLink(NodeCard host, NodeCard dropped, DropSide side, NodeType t, NodeType ht,
                     out string fromUid, out string fromPort, out string toUid, out string toPort)
    {
        fromUid = toUid = fromPort = toPort = null;
        var node = dropped.Node;
        if (DropAfter(side))
        {
            if (string.IsNullOrEmpty(t.ExecIn) || HasExecIn(node.Uid)) return false;
            fromPort = FreeExecOut(host.Node);
            if (fromPort == null) return false;
            fromUid = host.Node.Uid; toUid = node.Uid; toPort = t.ExecIn;
        }
        else
        {
            if (string.IsNullOrEmpty(ht.ExecIn) || t.ExecOut.Count == 0 || HasExecIn(host.Node.Uid)) return false;
            fromPort = t.ExecOut[0].Id;
            if (HasExecOut(node.Uid, fromPort)) return false;
            fromUid = node.Uid; toUid = host.Node.Uid; toPort = ht.ExecIn;
        }
        return true;
    }

    static string SideName(DropSide side)
    {
        switch (side)
        {
            case DropSide.Above: return "上面";
            case DropSide.Left: return "左边";
            case DropSide.Right: return "右边";
            default: return "下面";
        }
    }

    void PlaceAdjacent(NodeCard host, NodeCard dropped, DropSide side, string linkedPort)
    {
        // 接进循环体就交给循环体自己摆位：它要排在循环头正下方、水平居中
        if (linkedPort == "body") { PlaceUnderLoop(host.Node.Uid, dropped.Node.Uid); return; }
        if (host.Node == null || dropped == null || dropped.Node == null) return;
        float myW = dropped.Rect.sizeDelta.x, myH = dropped.Rect.sizeDelta.y;
        float hw = host.Rect.sizeDelta.x, hh = host.Rect.sizeDelta.y;
        float gap = DropGap(side, host.Node, dropped.Node, FrameBelow(host));
        var tl = DropTopLeft(side, host.Node.X, host.Node.Y, hw, hh, myW, myH, gap);
        dropped.Node.X = tl.x;
        dropped.Node.Y = tl.y;
        dropped.Rect.anchoredPosition = new Vector2(tl.x, -tl.y);
    }
    // 拖动前先定好这一组：普通点击 = 单选这张（已在这组里的保持整组一起动）；Ctrl+点击 = 加进/移出当前组。
    public void SelectForDrag(NodeCard c, bool additive)
    {
        if (additive) { c.Selected = !c.Selected; c.SetSelected(c.Selected); return; }
        if (c.Selected) return;
        foreach (var other in cards.Values)
            if (other != null && other != c && other.Selected) { other.Selected = false; other.SetSelected(false); }
        c.Selected = true; c.SetSelected(true);
    }

    // ---------------- 复制 / 粘贴 ----------------

    readonly List<GraphNode> clipboard = new List<GraphNode>();
    readonly List<GraphLink> clipboardLinks = new List<GraphLink>();
    public bool HasClipboard { get { return clipboard.Count > 0; } }
    public int ClipboardCount { get { return clipboard.Count; } }

    static GraphNode CloneNode(GraphNode n)
    {
        var c = new GraphNode { Uid = n.Uid, Type = n.Type, X = n.X, Y = n.Y };
        foreach (var kv in n.Params) c.Params[kv.Key] = kv.Value;
        return c;
    }

    static GraphLink CloneLink(GraphLink l)
    {
        return new GraphLink { FromNode = l.FromNode, FromPort = l.FromPort, ToNode = l.ToNode, ToPort = l.ToPort, Kind = l.Kind };
    }

    // 预览里按了 Ctrl+C / Ctrl+V：一声「不行」的音 + 一条说清为什么的 Toast，不静默吞掉按键。
    // 返回值必须和「什么都没选中」区分开，否则调用方会再补一条自己的 Toast 把这句顶掉。
    public const int ClipboardBlocked = -1;
    void RejectPreviewClipboard()
    {
        SoundSystem.Play("error");
        if (App.I != null) App.I.Toast(PreviewReadOnlyHint);
    }

    // 把当前选中的模块（连同它们之间的连线）抄进剪贴板
    public int CopySelected()
    {
        if (PreviewBlocksClipboard(App.I != null && App.I.AnswerPreview)) { RejectPreviewClipboard(); return ClipboardBlocked; }
        clipboard.Clear(); clipboardLinks.Clear();
        var picked = new List<string>();
        foreach (var kv in cards) if (kv.Value != null && kv.Value.Selected && kv.Value.gameObject.activeSelf) picked.Add(kv.Key);
        if (picked.Count == 0) return 0;
        foreach (var uid in picked)
        {
            var n = GraphUtil.FindNode(graph, uid);
            if (n == null) continue;
            if (IsPinned(n.Type)) { if (App.I != null) App.I.Toast(PinnedHint); continue; }   // 抄走再贴就多出第二块样板，判题会认错板
            clipboard.Add(CloneNode(n));
        }
        foreach (var l in graph.Links)
            if (picked.Contains(l.FromNode) && picked.Contains(l.ToNode)) clipboardLinks.Add(CloneLink(l));
        return clipboard.Count;
    }

    // 贴到选中模块的右下方；没有选中就贴到画布中心。贴出来的新块保持选中，方便连着拖。
    public int PasteClipboard()
    {
        if (PreviewBlocksClipboard(App.I != null && App.I.AnswerPreview)) { RejectPreviewClipboard(); return ClipboardBlocked; }
        if (clipboard.Count == 0) return 0;
        var map = new Dictionary<string, string>();
        foreach (var n in clipboard) map[n.Uid] = "n" + Guid.NewGuid().ToString("N").Substring(0, 8);

        float dx = 36f, dy = 36f;
        var anchor = FindCard(FirstSelectedUid());
        if (anchor != null) { dx = anchor.Rect.localPosition.x + 36f; dy = anchor.Rect.localPosition.y - 36f; }

        foreach (var kv in cards.Values) if (kv != null && kv.Selected) { kv.Selected = false; kv.SetSelected(false); }

        foreach (var src in clipboard)
        {
            var n = CloneNode(src);
            n.Uid = map[src.Uid];
            n.X = (anchor != null ? dx - anchor.Rect.localPosition.x : 0f) + src.X;
            n.Y = (anchor != null ? -(dy - anchor.Rect.localPosition.y) : 36f) + src.Y;
            graph.Nodes.Add(n);
            var c = NodeCard.Create(nodeLayer, this, n);
            c.Selected = true; c.SetSelected(true);
            cards[n.Uid] = c;
        }
        foreach (var src in clipboardLinks)
        {
            var l = CloneLink(src);
            l.FromNode = map[l.FromNode]; l.ToNode = map[l.ToNode];
            graph.Links.Add(l);
        }
        RefreshLines();
        RefreshScopeFields();
        RaiseGraphChanged();
        return clipboard.Count;
    }

    string FirstSelectedUid()
    {
        foreach (var kv in cards) if (kv.Value != null && kv.Value.Selected) return kv.Key;
        return null;
    }

    // ---------------- 连线悬停删除 ----------------

    GraphLink hotLink;
    GameObject linkDotGo;

    // 鼠标停在某根线上 → 线中点冒出一个 ×，点它就删掉这根线。
    // 线本身是网格顶点，不是图元，没法直接接收射线，所以按贝塞尔采样距离来找最近的。
    void UpdateLinkHover()
    {
        if (lines == null || pendingLink != null || selecting || panning || ExpressionFieldUI.AnyOpen)
        { SetHotLink(null); return; }
        var hit = lines.HitLink(Local(Input.mousePosition), LinkHitTolerance);
        SetHotLink(hit);
    }

    float LinkHitTolerance { get { return 10f / Mathf.Max(.01f, zoom); } }

    void SetHotLink(GraphLink l)
    {
        bool same = l == hotLink || (l != null && hotLink != null && SameLink(l, hotLink));
        if (same) { if (l != null) PlaceLinkDot(l); return; }
        hotLink = l;
        if (lines != null) lines.SetVerticesDirty();
        if (linkDotGo != null) linkDotGo.SetActive(l != null);
        if (l != null) PlaceLinkDot(l);
    }

    static bool SameLink(GraphLink a, GraphLink b)
    {
        return a.FromNode == b.FromNode && a.FromPort == b.FromPort && a.ToNode == b.ToNode && a.ToPort == b.ToPort && a.Kind == b.Kind;
    }

    void PlaceLinkDot(GraphLink l)
    {
        if (linkDotGo == null) BuildLinkDot();
        Vector2 mid;
        if (!lines.LinkMid(l, out mid)) { linkDotGo.SetActive(false); return; }
        var rt = linkDotGo.GetComponent<RectTransform>();
        rt.localScale = Vector3.one / Mathf.Max(.01f, zoom);     // 缩放画布时小圆点保持同样大小
        rt.localPosition = mid;
        linkDotGo.SetActive(true);
    }

    void BuildLinkDot()
    {
        linkDotGo = new GameObject("LinkDelete", typeof(RectTransform), typeof(Image));
        linkDotGo.transform.SetParent(content, false);
        var img = linkDotGo.GetComponent<Image>();
        img.sprite = White; img.type = Image.Type.Simple; img.color = Theme.Coral; img.raycastTarget = true;
        linkDotGo.GetComponent<RectTransform>().sizeDelta = new Vector2(14f, 14f);
        var lbl = PixelText.Create(linkDotGo.transform, "×", Theme.White, 1);
        lbl.raycastTarget = false;
        var lrt = lbl.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(.5f, .5f);
        lrt.pivot = new Vector2(.5f, .5f);
        lrt.anchoredPosition = new Vector2(0f, 1f);
        lrt.sizeDelta = new Vector2(14f, 14f);
        lbl.Align = PixelText.TextAlign.Center;
        linkDotGo.AddComponent<LinkDotClick>().Owner = this;
        linkDotGo.SetActive(false);
    }

    // 悬停中的那根线（供演示与外部删除）
    public GraphLink HoveredLink { get { return hotLink; } }

    public bool DeleteLink(GraphLink l)
    {
        if (l == null || graph.Links.RemoveAll(x => SameLink(x, l)) == 0) return false;
        hotLink = null;
        if (linkDotGo != null) linkDotGo.SetActive(false);
        RefreshLines();
        RefreshScopeFields();
        RaiseGraphChanged();
        SoundSystem.Play("unlink");   // 只有真删掉一条线才响，点空了不回执
        return true;
    }

    public void DeleteHoveredLink()
    {
        var l = hotLink;
        if (l == null) return;
        if (DeleteLink(l)) Debug.Log("[交互] 删除连线 " + l.FromNode + "." + l.FromPort + " → " + l.ToNode + "." + l.ToPort);
    }

    // 原地重建一张卡片：卡片高度会随参数变化（创建变量的初始值条有无），只换这一张，不动整张图。
    public void RebuildCard(GraphNode n)
    {
        if (n == null) return;
        NodeCard old;
        if (!cards.TryGetValue(n.Uid, out old) || old == null) return;
        bool wasSelected = old.Selected;
        Destroy(old.gameObject);
        var c = NodeCard.Create(nodeLayer, this, n);
        cards[n.Uid] = c;
        c.Selected = wasSelected;
        c.SetSelected(wasSelected);
        // 断点小圆点这类存在外部集合里的状态，重建会抹掉，请外部补回来
        if (CardRebuilt != null) CardRebuilt(n.Uid);
        RefreshLines();
        RefreshLoopBodies();
    }
    public void StartLink(Port p)
    {
        if (!p.Output)
        {
            // 正在拉线时按下输入端口：当场接上（先点输出、再点输入也能连线）
            if (pendingLink != null && pendingLink != p && pendingLink.NodeUid != p.NodeUid) FinishLink(p);
            return;
        }
        pendingLink = p;
        foreach (var c in cards.Values)
            foreach (var q in c.Ports)
                q.SetPending(q == p);
        UpdateGhost();
        if (LinkHintRequested != null) LinkHintRequested();
    }

    // Unity 把 pointer-up 永远发给「按下的那个」对象，所以端口上松手拿到的还是起点端口，
    // 绝不能再拿它当终点（那样 pendingLink 被提前清空，线永远接不上）。
    // 这里按落点吸附找终点；吸附不到就保留待定端口，让玩家接着点输入端口。
    public void HandlePortPointerUp(Port p, Vector2 screenPoint)
    {
        if (pendingLink == null || p != pendingLink) return;
        if (TrySnapLink(screenPoint)) return;
        UpdateGhost();
    }

    // 松手时找兼容的输入端口接上。分两档，玩家不必把线头精确戳到那个小方块上：
    // ① 落在某张卡片身上 —— 这张卡里离落点最近的输入端口直接接上，不受距离限制；
    // ② 落在空白处 —— 沿用原来的按距离吸附（46px 内），够不着就保留待定端口，让玩家接着点。
    bool TrySnapLink(Vector2 screenPoint)
    {
        if (pendingLink == null) return false;
        Port onCard = null, onCardAny = null;
        float onCardDist = float.MaxValue, onCardAnyDist = float.MaxValue;
        Port nearby = null;
        float nearbyDist = LinkSnapRadius;
        foreach (var c in cards.Values)
        {
            if (c == null) continue;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(c.Rect, screenPoint, null);
            foreach (var q in c.Ports)
            {
                if (q.NodeUid == pendingLink.NodeUid) continue;         // 画布上只剩执行端口，不再按 Kind 挑兼容口
                float d = Vector2.Distance(q.Rect.position, screenPoint);
                if (over)
                {
                    if (!q.Output) { if (d < onCardDist) { onCardDist = d; onCard = q; } }
                    else if (d < onCardAnyDist) { onCardAnyDist = d; onCardAny = q; }
                }
                else if (!q.Output && d < nearbyDist) { nearbyDist = d; nearby = q; }
            }
        }
        // 落在卡片上但那张卡没有能吃的输入口（比如「开始」）：不硬接，也顺手把待定线撤掉，
        // 免得橡皮筋留在屏幕上，玩家以为还在连线状态。
        if (onCard == null)
        {
            if (onCardAny != null) { CancelLink(); Debug.Log("[交互] 落在这块模块上没有可接入的入口，取消连线"); return true; }
            if (nearby == null) return false;
        }
        var target = onCard ?? nearby;
        FinishLink(target);
        return true;
    }

    public void CancelLink()
    {
        pendingLink = null;
        foreach (var c in cards.Values)
            foreach (var q in c.Ports)
                q.SetPending(false);
        UpdateGhost();
    }
    public void FinishLink(Port p)
    {
        Port source = null;
        foreach (var c in cards.Values)
            foreach (var q in c.Ports)
                if (q.Pending) source = q;
        pendingLink = null;

        if (source != null && source != p && source.Output && !p.Output && source.NodeUid != p.NodeUid)
        {
            graph.Links.RemoveAll(l => l.FromNode == source.NodeUid && l.FromPort == source.PortId && l.ToNode == p.NodeUid && l.ToPort == p.PortId);
            // 一个出口只走一条线：新线顶掉旧线，不存在「先走哪根」的悬念。
            // 数据线已经退出画布（值全走内嵌表达式），这里不再按 Kind 分叉。
            graph.Links.RemoveAll(l => l.Kind == LinkKind.Exec && l.FromNode == source.NodeUid && l.FromPort == source.PortId);
            graph.Links.Add(new GraphLink { FromNode = source.NodeUid, FromPort = source.PortId, ToNode = p.NodeUid, ToPort = p.PortId, Kind = LinkKind.Exec });
            // 接进循环体的那一刻，把它直接摆到循环头正下方 —— 循环条件写在循环体上面，不用手拖
            if (source.PortId == "body") PlaceUnderLoop(source.NodeUid, p.NodeUid);
            if (LinkCreated != null)
            {
                var fromNode = GraphUtil.FindNode(graph, source.NodeUid);
                var toNode = GraphUtil.FindNode(graph, p.NodeUid);
                if (fromNode != null && toNode != null) LinkCreated(fromNode, toNode);
            }
        }

        foreach (var c in cards.Values)
            foreach (var q in c.Ports)
                q.SetPending(false);
        // 线接完（或落点无效而作废）都算这次拉线结束：橡皮筋必须跟着撤掉，
        // 否则那条红线会一直挂在画布上（UpdateGhost 见 pendingLink 为空即 ClearGhost）。
        UpdateGhost();
        RefreshLines();
        RefreshScopeFields();
        RaiseGraphChanged();
    }

    // 拉线中的橡皮筋：起点是待定端口，终点跟手
    void UpdateGhost()
    {
        if (lines == null) return;
        if (pendingLink == null) lines.ClearGhost();
        else lines.SetGhost(pendingLink, Local(Input.mousePosition));
    }

    // ---------------- 脚本化拉线演示（-linkdrag） ----------------
    // 指针事件全部经 ExecuteEvents 派发给端口本体，和真实鼠标走同一套处理器，
    // 端口没接住按下/拖动/松手或终点判断写错时，这个入口会直接暴露出来。

    public bool DemoPressPort(string uid, string portId, bool output)
    {
        var es = EventSystem.current;
        var p = FindPort(uid, portId, output);
        if (es == null || p == null) return false;
        demoPed = new PointerEventData(es) { pointerId = 97, position = ScreenPointOfPort(p) };
        demoPressPort = p;
        ExecuteEvents.Execute(p.gameObject, demoPed, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(p.gameObject, demoPed, ExecuteEvents.dragHandler);
        return true;
    }

    public bool DemoDragPortTo(string uid, string portId, bool output)
    {
        if (demoPed == null || demoPressPort == null) return false;
        var target = FindPort(uid, portId, output);
        if (target == null) return false;
        demoPed.position = ScreenPointOfPort(target);
        ExecuteEvents.Execute(demoPressPort.gameObject, demoPed, ExecuteEvents.dragHandler);
        if (pendingLink != null && lines != null) lines.SetGhost(pendingLink, Local(demoPed.position));
        return true;
    }

    public void DemoReleasePort()
    {
        if (demoPed == null || demoPressPort == null) return;
        // pointer-up 永远发给按下的那个端口，这里如实复现，才能验出「终点判断拿错对象」这类 bug
        ExecuteEvents.Execute(demoPressPort.gameObject, demoPed, ExecuteEvents.pointerUpHandler);
        demoPed = null;
        demoPressPort = null;
        if (pendingLink == null && lines != null) lines.ClearGhost();
    }

    static Vector2 ScreenPointOfPort(Port p)
    {
        return RectTransformUtility.WorldToScreenPoint(null, p.Rect.TransformPoint(p.Rect.rect.center));
    }
    public void DeleteSelected()
    {
        bool any = false, blocked = false;
        for (int i = graph.Nodes.Count - 1; i >= 0; i--)
        {
            var n = graph.Nodes[i];
            NodeCard c;
            if (n == null || !cards.TryGetValue(n.Uid, out c) || c == null || !c.Selected) continue;
            if (IsPinned(n.Type)) { blocked = true; continue; }   // 框选里混进样例板：其余照删，那两块留下
            var id = n.Uid;
            Destroy(c.gameObject);
            cards.Remove(id);
            graph.Nodes.RemoveAt(i);
            graph.Links.RemoveAll(l => l.FromNode == id || l.ToNode == id);
            any = true;
        }
        RefreshLines();
        RefreshScopeFields();
        RaiseGraphChanged();
        if (blocked && App.I != null) App.I.Toast(PinnedHint);
        if (any) SoundSystem.Play("del");   // 批量删只给一声，这条 cue 的 0.07s 去抖会把连点删几块压成一下
    }
    public const string PinnedHint = "「样例输入板」「样例对比板」常驻画板，不能删除";
    void Update()
    {
        StepViewFollow();
        TickBoards(Time.unscaledDeltaTime);   // 看板的压入/弹出动画跟键盘无关，放在所有提前 return 之前
        StepEdgePan();                        // 边缘自动平移同理：框选/拖模块时鼠标不松手也要一直跟
        // 拉线中的橡皮筋跟手（也覆盖「先点输出、松手后移动鼠标」这一段）；脚本演示时由演示自己给落点
        if (pendingLink != null && demoPed == null) UpdateGhost();
        UpdateLinkHover();
        if (Input.GetKeyDown(KeyCode.Escape)) { CancelLink(); return; }
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        // 小窗开着时 Ctrl+C / Ctrl+V 归输入框，不动模块
        var focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        bool editingText = ExpressionFieldUI.AnyOpen || NodeParamsUI.IsOpen || ExprEditorUI.IsOpen
            || (focused != null && focused.GetComponent<PixelInputField>() != null);
        if (editingText) return;
        // 预览里被拦时提示已经由 RejectPreviewClipboard 发过，这里再补一句会把它顶掉，所以先判返回值
        if (ctrl && Input.GetKeyDown(KeyCode.C)) { int k = CopySelected(); if (k != ClipboardBlocked && App.I != null) App.I.Toast(k > 0 ? "已复制 " + k + " 个模块" : "先选中模块再复制"); return; }
        if (ctrl && Input.GetKeyDown(KeyCode.V)) { int k = PasteClipboard(); if (k != ClipboardBlocked && App.I != null) App.I.Toast(k > 0 ? "已粘贴 " + k + " 个模块" : "剪贴板是空的"); return; }
        // 撤销/恢复：Ctrl+Z 退一步，Ctrl+Y 与 Ctrl+Shift+Z 进一步；上面 editingText 已经拦住输入框，打字时的 Z 不会被抢
        if (ctrl && (Input.GetKeyDown(KeyCode.Y) || (Input.GetKeyDown(KeyCode.Z)
            && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))))
        { if (CanRedo) { DoRedo(); if (App.I != null) App.I.Toast("已恢复一步"); } else if (App.I != null) App.I.Toast("没有可恢复的改动"); return; }
        if (ctrl && Input.GetKeyDown(KeyCode.Z))
        { if (CanUndo) { DoUndo(); if (App.I != null) App.I.Toast("已撤销一步"); } else if (App.I != null) App.I.Toast("没有可撤销的改动"); return; }
        if (!Input.GetKeyDown(KeyCode.Delete)) return;
        // 表达式小窗打开时，Delete / Backspace 属于输入框，不删节点
        if (ExpressionFieldUI.AnyOpen) return;
        // 正在编辑变量名时，Delete 属于文本框，不删节点
        if (focused != null && focused.GetComponent<PixelInputField>() != null) return;
        if (SelectedCount() == 0 && hotLink != null) { DeleteHoveredLink(); return; }
        DeleteSelected();
    }
    public void RefreshLines(){if(lines!=null)lines.SetLinks(graph,cards); RefreshLoopBodies(); LayoutArrayViz();}

    // ---------------- 数组可视化：虚线悬浮看板，位置大小玩家自己定 ----------------

    sealed class ArrayVizSlot
    {
        public ArrayBoard Board;
    }

    readonly Dictionary<string, ArrayVizSlot> arrayViz = new Dictionary<string, ArrayVizSlot>(StringComparer.Ordinal);
    List<KeyValuePair<string, List<object>>> vizArrays;
    string vizHiName;
    int vizHiIndex = -1;

    /// <summary>把运行时的数组数据画成虚线悬浮看板；arrays 传 null 就全部收起。</summary>
    public void ShowArrays(IEnumerable<KeyValuePair<string, List<object>>> arrays, string hiName, int hiIndex)
    {
        vizArrays = arrays == null ? null : new List<KeyValuePair<string, List<object>>>(arrays);
        vizHiName = hiName; vizHiIndex = hiIndex;
        if (vizArrays == null) { DestroyArrayViz(); return; }

        // 数据里已经没有的数组，看板跟着撤掉
        var stale = new List<string>();
        foreach (var kv in arrayViz)
            if (!vizArrays.Exists(a => a.Key == kv.Key)) stale.Add(kv.Key);
        foreach (var k in stale)
        {
            if (arrayViz[k].Board != null && arrayViz[k].Board.Panel != null) Destroy(arrayViz[k].Board.Panel.gameObject);
            arrayViz.Remove(k);
        }

        foreach (var a in vizArrays)
        {
            ArrayVizSlot slot;
            if (!arrayViz.TryGetValue(a.Key, out slot) || slot.Board == null)
            {
                slot = new ArrayVizSlot { Board = new ArrayBoard(vizLayer, a.Key, PointToLocal) };
                arrayViz[a.Key] = slot;
            }
            slot.Board.Render(a.Key, a.Value, a.Key == vizHiName ? vizHiIndex : -1);
        }
        LayoutArrayViz();
    }

    void DestroyArrayViz()
    {
        foreach (var kv in arrayViz)
            if (kv.Value != null && kv.Value.Board != null && kv.Value.Board.Panel != null) Destroy(kv.Value.Board.Panel.gameObject);
        arrayViz.Clear();
        vizArrays = null;
    }

    // 没被玩家拖过的看板先停在声明它的卡片右边；拖过之后就归玩家管，不再跟着卡片跑
    void LayoutArrayViz()
    {
        if (arrayViz.Count == 0) return;
        foreach (var kv in arrayViz)
        {
            var slot = kv.Value;
            if (slot == null || slot.Board == null || slot.Board.Panel == null) continue;
            var panel = slot.Board.Panel;
            if (panel.MovedByUser) { panel.Root.gameObject.SetActive(true); continue; }
            var card = FindDeclCard(kv.Key);
            if (card == null || card.Rect == null) { panel.Root.gameObject.SetActive(false); continue; }
            panel.Root.gameObject.SetActive(true);
            float cx = card.Rect.anchoredPosition.x, cy = -card.Rect.anchoredPosition.y;
            float w = panel.Size.x, h = panel.Size.y;
            float x = cx + card.Rect.sizeDelta.x + 24f, y = cy;
            // 右边放不下就改放卡片下面，看板不许跑出画布
            if (x + w > ContentW - 8f) { x = cx; y = cy + card.Rect.sizeDelta.y + 20f; }
            panel.SetPosition(new Vector2(Mathf.Max(8f, x), Mathf.Max(8f, y)));
        }
    }

    // 这个数组是哪个模块声明的：同名且类型是数组的「创建变量」卡片
    NodeCard FindDeclCard(string name)
    {
        if (string.IsNullOrEmpty(name) || graph == null || graph.Nodes == null) return null;
        foreach (var n in graph.Nodes)
        {
            if (n == null || n.Type != "vardecl") continue;
            if (GraphUtil.Get(n, "name") != name || !ExprLang.IsArrayType(GraphUtil.Get(n, "vtype"))) continue;
            NodeCard c;
            if (cards.TryGetValue(n.Uid, out c) && c != null) return c;
        }
        return null;
    }

    // ---------------- 结构可视化：虚线悬浮看板（栈一摞盘子 / 队列一队车厢） ----------------

    readonly Dictionary<string, StackBoard> stackViz = new Dictionary<string, StackBoard>(StringComparer.Ordinal);
    readonly Dictionary<string, QueueBoard> queueViz = new Dictionary<string, QueueBoard>(StringComparer.Ordinal);

    /// <summary>把运行时压进栈 / 排进队列的东西画成看板；structs 传 null 就全部收起。</summary>
    public void ShowStacks(IEnumerable<Dayu.Core.StructSnap> structs, string action)
    {
        List<Dayu.Core.StructSnap> stacks = null, queues = null;
        if (structs != null)
            foreach (var s in structs)
            {
                if (s == null) continue;
                if (s.Kind == "queue") { if (queues == null) queues = new List<Dayu.Core.StructSnap>(); queues.Add(s); }
                else if (s.Kind == "stack") { if (stacks == null) stacks = new List<Dayu.Core.StructSnap>(); stacks.Add(s); }
            }
        Sweep(stackViz, stacks);
        Sweep(queueViz, queues);
        if ((stacks == null || stacks.Count == 0) && (queues == null || queues.Count == 0)) { DestroyBoards(); return; }

        if (stacks != null)
            foreach (var s in stacks)
            {
                StackBoard board;
                if (!stackViz.TryGetValue(s.Id, out board) || board == null)
                {
                    board = new StackBoard(vizLayer, s.Id, string.IsNullOrEmpty(s.Name) ? s.Id : s.Name, PointToLocal);
                    stackViz[s.Id] = board;
                }
                board.Render(s.Items, InstantView ? null : action);
            }
        if (queues != null)
            foreach (var s in queues)
            {
                QueueBoard board;
                if (!queueViz.TryGetValue(s.Id, out board) || board == null)
                {
                    board = new QueueBoard(vizLayer, s.Id, string.IsNullOrEmpty(s.Name) ? s.Id : s.Name, PointToLocal);
                    queueViz[s.Id] = board;
                }
                board.Render(s.Items, InstantView ? null : action);
            }
        LayoutBoards();
    }

    // 场上不再存在的结构实例，看板跟着收掉
    void Sweep<T>(Dictionary<string, T> dict, List<Dayu.Core.StructSnap> live)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        if (live != null) foreach (var s in live) keep.Add(s.Id);
        var stale = new List<string>();
        foreach (var k in dict) if (!keep.Contains(k.Key)) stale.Add(k.Key);
        foreach (var k in stale)
        {
            var panel = PanelOf(dict[k]);
            if (panel != null) Destroy(panel.gameObject);
            dict.Remove(k);
        }
    }

    static FloatPanel PanelOf(object board)
    {
        var s = board as StackBoard;
        if (s != null) return s.Panel;
        var q = board as QueueBoard;
        return q != null ? q.Panel : null;
    }

    void DestroyBoards()
    {
        foreach (var kv in stackViz) { var p = PanelOf(kv.Value); if (p != null) Destroy(p.gameObject); }
        foreach (var kv in queueViz) { var p = PanelOf(kv.Value); if (p != null) Destroy(p.gameObject); }
        stackViz.Clear();
        queueViz.Clear();
    }

    // 没被玩家拖过的看板先停在「创建栈 / 创建队列」卡片旁边，拖过之后归玩家摆
    void LayoutBoards()
    {
        foreach (var kv in stackViz) LayoutBoard(PanelOf(kv.Value), kv.Key);
        foreach (var kv in queueViz) LayoutBoard(PanelOf(kv.Value), kv.Key);
    }

    void LayoutBoard(FloatPanel panel, string structId)
    {
        if (panel == null) return;
        if (panel.MovedByUser) { panel.Root.gameObject.SetActive(true); return; }
        var card = FindStructCard(structId);
        if (card == null || card.Rect == null) { panel.Root.gameObject.SetActive(false); return; }
        panel.Root.gameObject.SetActive(true);
        float cx = card.Rect.anchoredPosition.x, cy = -card.Rect.anchoredPosition.y;
        float w = panel.Size.x, h = panel.Size.y;
        float x = cx + card.Rect.sizeDelta.x + 24f, y = cy - h + card.Rect.sizeDelta.y;
        if (x + w > ContentW - 8f) { x = cx; y = cy + card.Rect.sizeDelta.y + 20f; }
        panel.SetPosition(new Vector2(Mathf.Max(8f, x), Mathf.Max(8f, y)));
    }

    // 栈数据属于哪个模块：structId 对得上的「创建结构」卡片
    NodeCard FindStructCard(string structId)
    {
        if (string.IsNullOrEmpty(structId) || graph == null || graph.Nodes == null) return null;
        foreach (var n in graph.Nodes)
        {
            if (n == null || n.Type != "structnew") continue;
            if (GraphUtil.Get(n, "structId") != structId) continue;
            NodeCard c;
            if (cards.TryGetValue(n.Uid, out c) && c != null) return c;
        }
        return null;
    }

    // 看板的进出动画靠这里推进；截图模式（InstantView）不生成动画，只留终态
    void TickBoards(float dt)
    {
        foreach (var kv in stackViz)
            if (kv.Value != null) kv.Value.Tick(dt);
    }

    // ---------------- 看板拖拽自检（-boarddrag） ----------------

    // 命中判定走真实 GraphicRaycaster，处理器走真实的事件冒泡：
    // 上一轮脚本化指针事件踩过假阳性的坑（把 pointerPress 直接塞成画布），这里不手写 pointerPress。
    // 位移方向按「拖右下角拉手 = 往左下 = 变窄变高」来给，两个轴向都能量出变化才算调大小真的通了。
    public string BoardDragReport()
    {
        var sb = new StringBuilder("看板拖拽自检");
        int done = 0;
        foreach (var kv in arrayViz)
        {
            var b = kv.Value != null ? kv.Value.Board : null;
            if (b == null || b.Panel == null) continue;
            sb.Append(" 数组[").Append(DragZone(b.Panel, b.Panel.HeadRect, new Vector2(-120f, 90f), "标题条"))
              .Append("；").Append(DragZone(b.Panel, b.Panel.GripRect, new Vector2(-100f, -90f), "拉手")).Append(']');
            done++;
            break;
        }
        foreach (var kv in stackViz)
        {
            var b = kv.Value;
            if (b == null || b.Panel == null) continue;
            sb.Append(" 栈[").Append(DragZone(b.Panel, b.Panel.HeadRect, new Vector2(-90f, 60f), "标题条"))
              .Append("；").Append(DragZone(b.Panel, b.Panel.GripRect, new Vector2(-60f, -70f), "拉手")).Append(']');
            done++;
            break;
        }
        foreach (var kv in queueViz)
        {
            var b = kv.Value;
            if (b == null || b.Panel == null) continue;
            sb.Append(" 队列[").Append(DragZone(b.Panel, b.Panel.HeadRect, new Vector2(-70f, 110f), "标题条"))
              .Append("；").Append(DragZone(b.Panel, b.Panel.GripRect, new Vector2(-40f, -60f), "拉手")).Append(']');
            done++;
            break;
        }
        return done == 0 ? sb + "：场上没有看板" : sb.ToString();
    }

    string DragZone(FloatPanel panel, RectTransform zone, Vector2 screenDelta, string label)
    {
        if (zone == null || EventSystem.current == null) return label + "：区域或事件系统缺失";
        var caster = zone.GetComponentInParent<GraphicRaycaster>();
        if (caster == null) return label + "：找不到射线投射器";
        var ped = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(null, zone.TransformPoint(zone.rect.center)),
            button = PointerEventData.InputButton.Left
        };
        var results = new List<RaycastResult>();
        caster.Raycast(ped, results);
        if (results.Count == 0) return label + "：射线没命中任何东西";
        if (results[0].gameObject != zone.gameObject) return label + "：被 " + results[0].gameObject.name + " 挡住";
        ped.pointerCurrentRaycast = results[0];
        var handler = ExecuteEvents.GetEventHandler<IDragHandler>(results[0].gameObject);
        if (handler == null) return label + "：没找到拖拽处理器";
        var p0 = ped.position;
        Vector2 posBefore = panel.Position, sizeBefore = panel.Size;
        ExecuteEvents.Execute(handler, ped, ExecuteEvents.beginDragHandler);
        ped.position = p0 + screenDelta;
        ExecuteEvents.Execute(handler, ped, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(handler, ped, ExecuteEvents.endDragHandler);
        return label + " 命中✓ " + (label == "拉手"
            ? "尺寸 " + (int)sizeBefore.x + "x" + (int)sizeBefore.y + " → " + (int)panel.Size.x + "x" + (int)panel.Size.y
            : "位置 " + (int)posBefore.x + "," + (int)posBefore.y + " → " + (int)panel.Position.x + "," + (int)panel.Position.y);
    }

    // ---------------- 循环体小画布 ----------------

    // 找出某个循环节点的循环体：沿着 body 出口往下走，直到离开这个循环
    List<string> LoopBodyNodes(GraphNode loop)
    {
        var body = new List<string>();
        if (loop == null) return body;
        var link = graph.Links.Find(x => x.Kind == LinkKind.Exec && x.FromNode == loop.Uid && x.FromPort == "body");
        if (link == null) return body;
        var seen = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(link.ToNode);
        while (stack.Count > 0)
        {
            string uid = stack.Pop();
            if (!seen.Add(uid)) continue;
            if (uid == loop.Uid) continue;
            body.Add(uid);
            foreach (var l in graph.Links)
                if (l.Kind == LinkKind.Exec && l.FromNode == uid && l.FromPort != "body" && l.FromPort != "done")
                    stack.Push(l.ToNode);
        }
        return body;
    }

    readonly Dictionary<string, GameObject> loopFrames = new Dictionary<string, GameObject>();
    readonly Dictionary<string, LoopFrameDrag> loopDrags = new Dictionary<string, LoopFrameDrag>();

    // 折叠起来的循环体：只属于画布显示，不进 Graph 数据，编译与答案判定都不认识它
    readonly HashSet<string> foldedLoops = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<string, string> thumbSig = new Dictionary<string, string>();
    const float ThumbTileW = 24f, ThumbTileH = 10f, ThumbGap = 4f;

    public bool IsLoopFolded(string uid) { return uid != null && foldedLoops.Contains(uid); }

    // 循环卡片右上角的 +/-：折叠时藏起循环体卡片，在循环头下面画一块缩略图
    public void ToggleLoopFold(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return;
        if (foldedLoops.Contains(uid)) foldedLoops.Remove(uid); else foldedLoops.Add(uid);
        thumbSig.Remove(uid);
        var c = FindCard(uid);
        if (c != null) c.UpdateFoldLabel();
        RefreshLines();
    }

    // 把刚接进循环体的模块摆到循环头（或已有的最后一个循环体模块）正下方，水平居中对齐循环头
    void PlaceUnderLoop(string loopUid, string bodyUid)
    {
        var head = FindCard(loopUid);
        var target = FindCard(bodyUid);
        if (head == null || target == null || head.Node == null || target.Node == null) return;
        float bottom = head.Node.Y + head.Rect.sizeDelta.y;
        foreach (var uid in LoopBodyNodes(head.Node))
        {
            if (uid == bodyUid) continue;
            var c = FindCard(uid);
            if (c == null || c.Node == null) continue;
            bottom = Mathf.Max(bottom, c.Node.Y + c.Rect.sizeDelta.y);
        }
        float hw = head.Rect.sizeDelta.x, tw = target.Rect.sizeDelta.x;
        target.Node.X = head.Node.X + (hw - tw) * 0.5f;
        target.Node.Y = bottom + 12f;
        target.Rect.anchoredPosition = new Vector2(target.Node.X, -target.Node.Y);
    }

    // 循环体画成一块框：框住循环头 + 里面的模块，于是「循环条件」永远写在循环体上面。
    // 折叠后框只剩循环头，下面一小块按类型画出内部结构的缩略图。
    void RefreshLoopBodies()
    {
        if (loopLayer == null) return;
        if (graph == null) return;

        var alive = new HashSet<string>();
        foreach (var loop in graph.Nodes)
        {
            if (loop == null || (loop.Type != "forloop" && loop.Type != "whileloop")) continue;
            var head = FindCard(loop.Uid);
            if (head == null || head.Node == null) continue;
            var bodyUids = LoopBodyNodes(loop);
            bool folded = IsLoopFolded(loop.Uid);

            // 折叠 = 把循环体卡片本身收起来（线也跟着不画，见 LineGraphic）
            foreach (var uid in bodyUids)
            {
                var bc = FindCard(uid);
                if (bc != null && bc.gameObject.activeSelf == folded) bc.gameObject.SetActive(!folded);
            }

            // headH 是循环头上方的标题条：既写「循环体 · N 个模块」，也放循环头的「循环开始」标签，
            // 同时它本身就是拖动整个循环体的把手。
            const float pad = 14f, headH = 22f;
            float minX = head.Node.X, minY = head.Node.Y;
            float maxX = head.Node.X + head.Rect.sizeDelta.x, maxY = head.Node.Y + head.Rect.sizeDelta.y;
            if (!folded)
                foreach (var uid in bodyUids)
                {
                    var c = FindCard(uid);
                    if (c == null || c.Node == null) continue;
                    minX = Mathf.Min(minX, c.Node.X);
                    minY = Mathf.Min(minY, c.Node.Y);
                    maxX = Mathf.Max(maxX, c.Node.X + c.Rect.sizeDelta.x);
                    maxY = Mathf.Max(maxY, c.Node.Y + c.Rect.sizeDelta.y);
                }

            float x = minX - pad, y = minY - pad - headH;
            float w = (maxX - minX) + pad * 2f, h = (maxY - minY) + pad * 2f + headH;

            int perRow = 1, rows = 0;
            float thumbH = 0f;
            if (folded && bodyUids.Count > 0)
            {
                perRow = Mathf.Max(1, Mathf.FloorToInt((w - pad * 2f + ThumbGap) / (ThumbTileW + ThumbGap)));
                rows = Mathf.CeilToInt(bodyUids.Count / (float)perRow);
                thumbH = 4f + rows * (ThumbTileH + ThumbGap) + 2f;
                h += thumbH;
            }

            alive.Add(loop.Uid);
            GameObject frame;
            if (!loopFrames.TryGetValue(loop.Uid, out frame) || frame == null)
            {
                frame = new GameObject("LoopBody_" + loop.Uid, typeof(RectTransform), typeof(Image));
                frame.transform.SetParent(loopLayer, false);
                var fimg = frame.GetComponent<Image>();
                fimg.sprite = White; fimg.type = Image.Type.Simple;
                fimg.color = new Color(0.30f, 0.55f, 0.68f, .12f);
                fimg.raycastTarget = false;
                Edge((RectTransform)frame.transform, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(.5f, 1f), new Vector2(0f, 1f));
                Edge((RectTransform)frame.transform, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 1f));
                Edge((RectTransform)frame.transform, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, .5f), new Vector2(1f, 0f));
                Edge((RectTransform)frame.transform, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, .5f), new Vector2(1f, 0f));
                // 标题条：整个循环体的拖动把手。只有这一条吃鼠标事件，框内空白仍然归画布（平移 / 框选），
                // 卡片在更靠后的兄弟层里，优先级也比它高，所以拖模块、拉线都不受影响。
                var bar = new GameObject("DragBar", typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(frame.transform, false);
                var brt = (RectTransform)bar.transform;
                brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
                brt.pivot = new Vector2(0f, 1f);
                brt.anchoredPosition = Vector2.zero;
                var bimg = bar.GetComponent<Image>();
                bimg.sprite = White; bimg.type = Image.Type.Simple;
                bimg.color = new Color(Theme.Whale.r / 255f, Theme.Whale.g / 255f, Theme.Whale.b / 255f, .18f);
                bimg.raycastTarget = true;
                var grab = bar.AddComponent<LoopFrameDrag>();
                grab.Owner = this; grab.LoopUid = loop.Uid;
                loopDrags[loop.Uid] = grab;
                var cap = PixelText.Create(frame.transform, "", Theme.WhaleDeep, 1);
                var crt2 = cap.rectTransform;
                crt2.anchorMin = crt2.anchorMax = new Vector2(0f, 1f);
                crt2.pivot = new Vector2(0f, 1f);
                crt2.anchoredPosition = new Vector2(6f, -3f);
                cap.raycastTarget = false;
                loopFrames[loop.Uid] = frame;
                thumbSig.Remove(loop.Uid);
            }
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 1f);
            frt.anchoredPosition = new Vector2(x, -y);
            frt.sizeDelta = new Vector2(w, h);
            frame.transform.SetAsFirstSibling();
            LoopFrameDrag grab2;
            if (loopDrags.TryGetValue(loop.Uid, out grab2) && grab2 != null)
            {
                grab2.LoopUid = loop.Uid;
                grab2.Rect.sizeDelta = new Vector2(w, pad + headH);
            }
            var titleText = frame.transform.Find("PixelText");
            if (titleText != null)
            {
                var pt = titleText.GetComponent<PixelText>();
                if (pt != null)
                {
                    // 「拖这条整体移动」这句提示只在框够宽时才写，免得溢出到框外
                    string capText = "循环体 · " + bodyUids.Count + " 个模块" + (folded ? "（已折叠）" : "");
                    string hint = folded ? "" : "（拖这条整体移动）";
                    pt.Text = PixelText.MeasureString(capText + hint, 1).x <= w - 12f ? capText + hint : capText;
                }
            }
            if (folded) BuildThumbs(frt, loop, bodyUids, pad, perRow, rows);
        }

        // 清理已经不在的循环框
        var stale = new List<string>();
        foreach (var kv in loopFrames)
            if (!alive.Contains(kv.Key)) { if (kv.Value != null) Destroy(kv.Value); stale.Add(kv.Key); }
        foreach (var k in stale) { loopFrames.Remove(k); loopDrags.Remove(k); }
    }

    // 折叠后的缩略图：每个内部模块一块 24x10 的小色块，颜色按模块类型，位置在循环头正下方
    void BuildThumbs(RectTransform frame, GraphNode loop, List<string> bodyUids, float pad, int perRow, int rows)
    {
        string sig = bodyUids.Count + "/" + perRow + "/" + rows + "/" + string.Join(",", bodyUids);
        string memo;
        var layerTf = frame.Find("Thumbs");
        if (layerTf != null && thumbSig.TryGetValue(loop.Uid, out memo) && memo == sig) return;
        if (layerTf != null) Destroy(layerTf.gameObject);

        float thumbH = 4f + rows * (ThumbTileH + ThumbGap) + 2f;
        var go = new GameObject("Thumbs", typeof(RectTransform));
        go.transform.SetParent(frame, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(0f, pad);
        rt.sizeDelta = new Vector2(0f, thumbH);

        for (int i = 0; i < bodyUids.Count; i++)
        {
            var c = FindCard(bodyUids[i]);
            string type = c != null && c.Node != null ? c.Node.Type : "";
            var tile = Tile(go.transform, "Thumb" + i, ThumbColor(type), false);
            var trt = tile.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(pad + (i % perRow) * (ThumbTileW + ThumbGap),
                                               -(4f + (i / perRow) * (ThumbTileH + ThumbGap)));
            trt.sizeDelta = new Vector2(ThumbTileW, ThumbTileH);
            tile.color = ThumbColor(type);
        }
        thumbSig[loop.Uid] = sig;
    }

    // 缩略图色块：一类模块一个颜色，全部取自 16 色板，不新增颜色
    static Color ThumbColor(string type)
    {
        switch (type)
        {
            case "vardecl":
            case "varset":      return Theme.Mint;
            case "input":       return Theme.Honey;
            case "output":      return Theme.Grape;
            case "binop":
            case "compare":
            case "logic":       return Theme.Whale;
            case "branch":      return Theme.Coral;
            case "sample_input":
            case "sample_compare": return Theme.Yellow;
            default:            return Theme.Border;
        }
    }

    void Edge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size)
    {
        var go = new GameObject("Edge" + name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = White; img.type = Image.Type.Simple;
        img.color = Theme.WhaleDeep;
        img.raycastTarget = false;
    }

    void SelectInRect(Vector2 a,Vector2 b)
    {
        var r=Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        foreach(var c in cards.Values)
        {
            if (c == null || !c.gameObject.activeSelf) continue;   // 折叠掉的循环体不参与框选
            var origin=c.Rect.localPosition;                 // 左上角（pivot 在左上）
            var rect=new Rect(origin.x,origin.y-c.Rect.sizeDelta.y,c.Rect.sizeDelta.x,c.Rect.sizeDelta.y);
            bool s=r.Overlaps(rect); c.Selected=s; c.SetSelected(s);
        }
    }
    void ShowSelection(Vector2 a,Vector2 b)
    {
        if(selectionBox==null)
        {
            selectionBox=new GameObject("Selection",typeof(RectTransform),typeof(Image));
            selectionBox.transform.SetParent(content,false);
            var i=selectionBox.GetComponent<Image>();
            i.sprite=White;
            i.color=Theme.A(Theme.Whale,.14f);
            i.raycastTarget=false;
            var ol = selectionBox.AddComponent<Outline>();
            ol.effectColor = Theme.Whale;
            ol.effectDistance = new Vector2(1.5f, -1.5f);
        }
        var rt=selectionBox.GetComponent<RectTransform>();
        rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);
        rt.pivot=new Vector2(0,0);
        rt.anchoredPosition=new Vector2(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y));
        rt.sizeDelta=new Vector2(Mathf.Abs(a.x-b.x),Mathf.Abs(a.y-b.y));
    }
    void HideSelection(){if(selectionBox!=null)Destroy(selectionBox);selectionBox=null;}
    static RectTransform Child(string n,Transform p){var go=new GameObject(n,typeof(RectTransform));go.transform.SetParent(p,false);return go.GetComponent<RectTransform>();}
    public static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}

    // 单色 Image 小工具（合成白 Sprite，默认不接收射线）
    public static Image Tile(Transform parent, string name, Color color, bool raycast)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = White; img.type = Image.Type.Simple; img.color = color; img.raycastTarget = raycast;
        return img;
    }
}

public sealed class NodeCard : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerClickHandler
{
    public RectTransform Rect; public GraphNode Node; public GraphView Owner; public bool Selected;
    public readonly List<Port> Ports = new();
    readonly List<Image> borders = new();
    readonly Dictionary<string, ExpressionFieldUI> exprFields = new();
    PixelText summaryLabel;
    ModuleViz skin;
    RectTransform loopVarClick;
    public Image breakDot;
    Image selectTint;
    public PixelText foldLabel;
    float portTop = GraphView.PortRowTop;
    float exprTop = GraphView.PortRowTop;
    float lastClickTime = -1f;
    // 上下布局时出口端口的横向格距：标签比默认格距宽就撑开，否则两个标签会叠在一起
    float outColPitch = PortColPitch;

    // ---------------- 内嵌表达式字段 ----------------

    // 每种节点在卡片上放几个表达式，以及写回 Params 的 key（顺序 = 卡片里从上到下）
    static readonly Dictionary<string, string[]> ExprFieldMap = new Dictionary<string, string[]>
    {
        { "vardecl",   new[] { "initExpr" } },
        { "varset",    new[] { "valueExpr" } },
        { "output",    new[] { "outputExpr" } },
        { "branch",    new[] { "condExpr" } },
        { "forloop",   new[] { "fromExpr", "toExpr" } },
        { "whileloop", new[] { "condExpr" } },
        // 压入 / 入队：卡面上直接写一个表达式，不再逼玩家连一根数据线过来
        { "stack_push", new[] { "pushExpr" } },
        { "queue_push", new[] { "pushExpr" } },
    };

    public static string[] ExprFieldKeys(string type)
    {
        string[] keys;
        return type != null && ExprFieldMap.TryGetValue(type, out keys) ? keys : null;
    }

    public static int ExprFieldCount(string type)
    {
        var keys = ExprFieldKeys(type);
        return keys == null ? 0 : keys.Length;
    }

    public static string ExprFieldContext(string type, string key)
    {
        switch ((type ?? "") + "." + (key ?? ""))
        {
            case "vardecl.initExpr":   return "初始值：标量写 0，数组写 [1,2,3]；填了「长度」就不要再写初始值";
            case "varset.valueExpr":   return "赋值语句：s = x / s += x / a[i] = x";
            case "output.outputExpr":  return "输出表达式";
            case "branch.condExpr":    return "分支条件";
            case "whileloop.condExpr": return "循环条件";
            case "forloop.fromExpr":   return "循环起点";
            case "forloop.toExpr":     return "循环终点";
        }
        return "表达式";
    }

    // 浮窗页签上的短名：上下文说明太长（「循环起点」这种四个字才放得进头部按钮）
    public static string ExprFieldTab(string type, string key)
    {
        switch ((type ?? "") + "." + (key ?? ""))
        {
            case "forloop.fromExpr": return "计数起点";
            case "forloop.toExpr":   return "计数终点";
            case "vardecl.initExpr": return "初始值";
            case "varset.valueExpr": return "赋值";
            case "output.outputExpr": return "输出";
            case "branch.condExpr":  return "条件";
            case "whileloop.condExpr": return "条件";
        }
        return "表达式";
    }

    // 计数循环卡面的三行文字（原来那两条白色表达式框不再画）：
    // 行高 21px，第一行给参数窗（改循环变量名），后两行各盖一条隐形字段点击区（改起点 / 终点）。
    public const float LoopLinePitch = 21f;
    // 计数循环一定是厂房皮肤，三行字从招牌下面起排，最后一行正好落在厂房中段上面
    public static float LoopLineTop(string type, int index) { return GraphView.CardTop(type, "factory") + 4f + index * LoopLinePitch; }

    public static string ForloopLine(GraphNode n, int index)
    {
        switch (index)
        {
            // 倒序写在第一行末尾：卡面三行的行数和行高都不动，名牌刷新（RefreshPlate）顺带把它带上
            case 0: return "循环变量：" + VarScope.IndexName(n) + (VarScope.IsDesc(n) ? " · 倒序" : "");
            case 1: return "计数起点：" + ForloopBound(n, "fromExpr");
            default: return "计数终点：" + ForloopBound(n, "toExpr");
        }
    }

    static string ForloopBound(GraphNode n, string key)
    {
        string v = GraphUtil.Get(n, key);
        return string.IsNullOrEmpty(v) ? "未填" : v;
    }

    // 双击模块直接进第一个表达式浮窗：只对这些「表达式就是这块要填的东西」的模块生效。
    // 创建变量 / 赋值还要挑名字和类型，双击仍旧开参数窗；它们要改进表达式，
    // 双击木箱名牌 / 读数屏那条就行。计数循环按玩家要求双击进起点，终点用浮窗页签切。
    public static bool DblClickOpensExpr(string type)
    {
        switch (type)
        {
            case "branch":
            case "whileloop":
            case "output":
            case "forloop": return true;
        }
        return false;
    }

    // 变量列表和类型表按「这个节点执行到时已经可见的变量」实时算：
    // 走不到的孤立节点退回全量变量，老图不会因为位置没摆好就满屏报错。
    static Dictionary<string, string> ScopeTypes(Graph g, string uid)
    {
        var visible = VarScope.Visible(g, uid);
        if (visible != null)
        {
            var d = new Dictionary<string, string>(visible, StringComparer.Ordinal);
            var self = uid != null && g != null ? GraphUtil.FindNode(g, uid) : null;
            // 循环自己的边界表达式也能用循环变量，和编译器口径一致。
            if (self != null && self.Type == "forloop") d[VarScope.IndexName(self)] = "int";
            return d;
        }
        var all = new Dictionary<string, string>();
        if (g != null && g.Nodes != null)
            foreach (var n in g.Nodes)
            {
                if (n == null || n.Type != "vardecl") continue;
                string name = GraphUtil.Get(n, "name");
                if (string.IsNullOrEmpty(name) || all.ContainsKey(name)) continue;
                all[name] = GraphUtil.Get(n, "vtype");
            }
        if (g != null && g.Nodes != null)
            foreach (var n in g.Nodes)
                if (n != null && n.Type == "forloop") all[VarScope.IndexName(n)] = "int";
        // 孤立节点退回全量时也带上容器实例，口径和 Compiler.AllTypes 一致；可达节点由「创建栈」卡登记。
        if (g != null && g.Structs != null)
            foreach (var s in g.Structs)
                if (s != null && !string.IsNullOrEmpty(s.Id) && !all.ContainsKey(s.Id)) all[s.Id] = Members.TypeOf(s.Kind, s.Elem);
        return all;
    }

    public static List<ExprVariableInfo> BuildVariables(Graph g, string uid)
    {
        var list = new List<ExprVariableInfo>();
        foreach (var kv in ScopeTypes(g, uid))
        {
            string name = kv.Key;
            string t = ExprTypeOf(kv.Value);
            // Members 只给 token 字符串，中文释义由浮窗按 Type 自己查；容器不套 IsArray，免得显示成 st[]
            list.Add(new ExprVariableInfo(name, t, ExprLang.IsArrayType(t), MemberTokens(t)));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    // 这颗类型支持的成员：数组额外补上老关卡用惯的 length（它就是 size 的同义）。
    static string[] MemberTokens(string type)
    {
        var names = new List<string>();
        foreach (var m in Members.Of(type))
            if (!names.Contains(m.Token)) names.Add(m.Token);
        if (ExprLang.IsArrayType(type)) names.Add(Members.LegacyAliases);
        return names.ToArray();
    }

    public static Dictionary<string, string> BuildTypeTable(Graph g, string uid)
    {
        var table = new Dictionary<string, string>();
        foreach (var kv in ScopeTypes(g, uid)) table[kv.Key] = ExprTypeOf(kv.Value);
        return table;
    }

    // 关卡里写的是 C++ 侧类型名（ll = 64 位整数），表达式语言只认 int / double / bool / char / string
    public static string ExprTypeOf(string vtype)
    {
        if (string.IsNullOrEmpty(vtype)) return "int";
        if (vtype.EndsWith("[]", StringComparison.Ordinal)) return ExprTypeOf(vtype.Substring(0, vtype.Length - 2)) + "[]";
        if (vtype == "ll" || vtype == "long") return "int";
        return vtype;
    }

    // 卡片高度 = 内容起点（标题 + 居中的身份行）+ 表达式字段 * 32 + 端口带 + 底部留白
    // 表达式模式下数据端口不画出来，端口行只按执行端口计；
    // 上下布局时端口改占卡片上下边缘，各只留一行高，不再逐行堆叠。
    // 创建变量的初始值不再占一行：它写在木箱名牌「x = 0」里，双击名牌照样能改。
    // 计数循环的两个字段折叠成隐形点击区、卡面改画三行文字，但高度仍按两行留：三行字正好落进这一段。
    public static int VisibleExprCount(string type, GraphNode n)
    {
        if (type == "vardecl") return 0;
        return ExprFieldCount(type);
    }

    // 创建数组只是 vardecl 把类型选成了数组：节点类型不拆，卡面与参数窗按这里分开
    public static bool IsArrayDecl(GraphNode n)
    { return n != null && n.Type == "vardecl" && ExprLang.IsArrayType(GraphUtil.Get(n, "vtype")); }

    public static string TitleFor(GraphNode n)
    {
        var t = n != null ? NodeTypes.Get(n.Type) : null;
        if (t == null) return n != null ? n.Type : "";
        if (n.Type == "structnew") return StructIsQueue(n) ? "创建队列" : "创建栈";
        return IsArrayDecl(n) ? "创建数组" : t.Title;
    }

    // 关卡给出的结构实例清单：同一张「创建结构」卡按实例的 Kind 写成创建栈 / 创建队列，
    // 玩家看卡面就知道这一关摞的是栈还是排队，不用去猜模块名的区别。
    public static List<StructDef> LevelStructs = new List<StructDef>();

    public static bool StructIsQueue(GraphNode n)
    {
        if (n == null || LevelStructs == null) return false;
        string id = GraphUtil.Get(n, "structId");
        if (string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < LevelStructs.Count; i++)
            if (LevelStructs[i] != null && LevelStructs[i].Id == id) return LevelStructs[i].Kind == "queue";
        return false;
    }

    // 卡面上把实例写成「栈 · 一摞数」：光一个 st 玩家看不出这一摞叫什么，也看不出是栈还是队列
    public static string StructLabel(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        if (LevelStructs != null)
            for (int i = 0; i < LevelStructs.Count; i++)
            {
                var s = LevelStructs[i];
                if (s == null || s.Id != id) continue;
                string kind = s.Kind == "queue" ? "队列" : "栈";
                return string.IsNullOrEmpty(s.Name) ? kind + " · " + id : kind + " · " + s.Name;
            }
        return id;
    }

    // 卡面与模块栏都按这个键取色：创建数组虽然也是 vardecl，但配色要跟创建变量分开
    public static string ColorKeyFor(GraphNode n)
    {
        if (IsArrayDecl(n)) return "mint";
        var t = n != null ? NodeTypes.Get(n.Type) : null;
        return t != null && !string.IsNullOrEmpty(t.ColorKey) ? t.ColorKey : "whale";
    }

    // 创建变量卡片的身份行文本：整数 (int) a。抽成纯函数方便自测。
    public static string VardeclLine(GraphNode n)
    {
        string vt = GraphUtil.Get(n, "vtype");
        string line = NodeTypes.TypeNameCn(vt) + " " + GraphUtil.Get(n, "name");
        // 数组把「铺了几格」写在身份行上，否则玩家看不出长度参数到底生效没有
        string size = ExprLang.IsArrayType(vt) ? GraphUtil.Get(n, "size") : null;
        if (!string.IsNullOrEmpty(size)) line += " · 长度 " + size;
        return line;
    }

    // 卡片高度 = 内容起点 + 表达式字段行 + 结构舞台 + 端口带 + 底部留白（样例板的数字块另加 extra）。
    // 数据端口已经彻底退出画布：端口行只按执行端口计，exprMode 形参留给老调用点，两种模式算法一致。
    // 创建变量 / 赋值的表达式写在结构名牌上（ModuleViz.PlateHoldsField），不再单独占一行。
    public static float CardHeight(NodeType t, bool exprMode, bool vertical, GraphNode n = null)
    {
        // 查询类小模块卡面上只有一行值：高度按「字高 + 上下各留 12」定，不掺身份行那套排版，
        // 更不掺动画舞台（这些模块本来就没有执行端口，鲸鱼娘不会走进去）
        if (IsShapeCard(t.Key)) return Mathf.Max(34f, PixelText.CellH + 24f);
        int rows = Mathf.Max(1, Mathf.Max(t.ExecIn != null ? 1 : 0, t.ExecOut.Count));
        string kind = ModuleViz.KindFor(n);
        int exprCount = ModuleViz.PlateHoldsField(kind) ? 0 : VisibleExprCount(t.Key, n);
        // 样例板要额外容纳“输入序列 / 期望 + 实际”两行数字块
        float extra = 0f;
        if (t.Key == "sample_input") extra = 46f;
        else if (t.Key == "sample_compare") extra = 100f;
        float portBand = vertical ? GraphView.PortRowH : rows * GraphView.PortRowH;
        return Mathf.Max(34f, GraphView.CardTop(t.Key, kind) + exprCount * GraphView.ExprFieldGap
                           + ModuleViz.StageHFor(n) + portBand + GraphView.FootGap + extra);
    }

    // 每种结构的基础宽度：内容多的（循环厂房、道岔）宽一点，内容少的（入口拱门 112、木箱 120）窄一点，
    // 一屏卡片不再是一个模子刻出来的等宽长方形。木箱 / 传送带 / 货架的宽度直接量卡面上那一行字，
    // 表达式或循环行写长了再把卡撑宽，别用省略号截掉读题信息。
    public static float CardWidth(NodeType t, GraphNode n, bool exprMode)
    {
        float w;
        switch (ModuleViz.KindFor(n))
        {
            case "factory": w = 196f; break;
            case "switch": w = 184f; break;
            case "dock": w = 172f; break;
            case "bench": w = 168f; break;
            case "quay": w = 156f; break;
            case "tower": w = 148f; break;
            case "belt": w = BeltCardWidth(n); break;
            case "crate":
            case "crate_set":
            case "crate_get":
            case "crate_seal": w = CrateCardWidth(n); break;
            case "shelf": w = ShelfCardWidth(n); break;
            case "gate": w = 112f; break;
            default:
                // 查询类小模块：宽度跟着卡面上那一行字走，不给整块长方形的份额
                w = IsShapeCard(t != null ? t.Key : null) ? ShapeCardTextWidth(n) : (t != null ? t.W : 168f);
                break;
        }
        float minW = IsShapeCard(t != null ? t.Key : null) ? ShapeCardMinWidth : 112f;
        if (n == null) return Mathf.Clamp(w, minW, 300f);
        if (n.Type == "forloop")
            for (int i = 0; i < 3; i++)
                w = Mathf.Max(w, PixelText.MeasureString(ForloopLine(n, i), 1).x + 34f);
        if (exprMode)
        {
            var keys = ExprFieldKeys(n.Type);
            if (keys != null)
                foreach (var k in keys)
                {
                    string s = GraphUtil.Get(n, k);
                    if (string.IsNullOrEmpty(s)) continue;
                    w = Mathf.Max(w, PixelText.MeasureString(s, 1).x + 46f);
                }
        }
        return Mathf.Clamp(w, minW, 300f);
    }

    // 传送带（输入）：名牌上就是「读入 x」这一行字，卡宽按它量，两侧立柱各 8px 也算在里面
    static float BeltCardWidth(GraphNode n)
    {
        string name = n != null ? GraphUtil.Get(n, "name") : "";
        string plate = string.IsNullOrEmpty(name) ? "读入" : "读入 " + name;
        return Mathf.Max(132f, PixelText.MeasureString(plate, 1).x + 40f);
    }

    // 木箱（创建变量 / 赋值）：名牌是 2 号字，箱子跟着「x = 0」那一行伸缩
    static float CrateCardWidth(GraphNode n)
    {
        return Mathf.Max(120f, PixelText.MeasureString(ModuleViz.PlateText(n), 2).x + 32f);
    }

    // 货架（创建数组）：槽位数就是长度参数，格子铺不开先加宽卡片，别把 a[8] 挤成三格
    static float ShelfCardWidth(GraphNode n)
    {
        int cells = ModuleViz.ShelfCells(n);
        return Mathf.Clamp(ModuleViz.ShelfBandWidth(cells) + 24f, 120f, 180f);
    }

    // ---------------- 卡片外形（第十三轮 #106）----------------
    // 玩家原话：「一眼看过去还是全都是长方形」「有些板块根本不需要那么大一块长方形」。
    // 两件事一起解决：只回一个值的查询模块摘掉动画舞台（ModuleViz.KindFor 返回 null），
    // 卡面按形状切出来的底板不再是矩形；名字挪到卡片上方的小标签，卡里只留值本身，字才不会被尖角切掉。
    public const float ShapeCardMinWidth = 44f;

    // 哪些模块走「小形状」路线：全是没有执行端口、只往外吐一个值的查询模块。
    public static bool IsShapeCard(string typeKey)
    {
        switch (typeKey)
        {
            case "const":
            case "len_of":
            case "stack_empty":
            case "stack_size":
            case "queue_empty":
            case "queue_size": return true;
        }
        return false;
    }

    // 卡面里那一行字：常量写「类型 值」，结构查询写「栈 · 名字」。
    // 标题不在这里 —— 形状卡的标题画到卡片外面去了（TitleFor 仍然是唯一的文案来源）。
    public static string ShapeCardLabel(GraphNode n)
    {
        if (n == null) return "";
        switch (n.Type)
        {
            case "const": return NodeTypes.TypeNameCn(ShapeParam(n, "vtype")) + " " + ShapeParam(n, "value");
            case "len_of": return ShapeParam(n, "expr");
        }
        return StructLabel(GraphUtil.Get(n, "structId"));
    }

    // 参数没填时按 NodeTypes 的默认值算字宽，否则卡片会比实际显示的内容窄
    static string ShapeParam(GraphNode n, string key)
    {
        string v = GraphUtil.Get(n, key);
        if (!string.IsNullOrEmpty(v)) return v;
        var t = NodeTypes.Get(n == null ? null : n.Type);
        if (t != null)
        {
            var d = t.Params.Find(p => p.Id == key);
            if (d != null && !string.IsNullOrEmpty(d.Default)) return d.Default;
        }
        return "";
    }

    // 形状卡两侧各留 ShapeSidePad：端口画在卡边上、有一半探进卡里（13 宽 → 进来 6.5），
    // 只留 6px 的话第一个字直接压在端口方块上，鼠标想点端口也点在字上。
    public const float ShapeSidePad = 12f;

    // 形状卡宽度 = 字宽 + 两侧留白
    public static float ShapeCardTextWidth(GraphNode n)
    {
        string s = ShapeCardLabel(n);
        return PixelText.MeasureString(s, 1).x + ShapeSidePad * 2f;
    }

    // 形状跟着内容走：先要 CardShapes.ForType 的理想形状，装得下卡面正中的那行字才给；
    // 装不下就依次退到胶囊、圆角矩形（都留得出中间一块平地方），最后才认输回矩形。
    public static CardShape PickShape(string typeKey, int exprCount, float w, float h, float textW, float textH)
    {
        if (!IsShapeCard(typeKey)) return CardShape.Rect;
        var want = CardShapes.ForType(typeKey, exprCount);
        if (want == CardShape.Rect) return CardShape.Rect;
        if (CardShapes.FitsContent(want, w, h, textW, textH)) return want;
        if (CardShapes.FitsContent(CardShape.Pill, w, h, textW, textH)) return CardShape.Pill;
        if (CardShapes.FitsContent(CardShape.RoundRect, w, h, textW, textH)) return CardShape.RoundRect;
        return CardShape.Rect;
    }

    public static NodeCard Create(RectTransform parent, GraphView owner, GraphNode node)
    {
        var go = new GameObject("Node_" + node.Uid, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var c = go.AddComponent<NodeCard>();
        c.Owner = owner; c.Node = node;
        c.Rect = go.GetComponent<RectTransform>();

        var t = NodeTypes.Get(node.Type);
        if (t == null) t = NodeTypes.Get("const");
        // 创建变量 / 创建数组共用 vardecl 这一个节点类型，卡面按 vtype 是不是数组分开显示：
        // 从模块栏拖出来的两个入口，落到画布上得能一眼认出自己是哪个
        bool arrayDecl = IsArrayDecl(node);

        // 整卡即结构：有皮肤的模块不再画标题带 / 图标 / 标题 / 身份行，招牌与名牌全由结构自己承担
        string skinKind = ModuleViz.KindFor(node);
        bool skinned = skinKind != null;
        // 表达式模式下数据关系由内嵌表达式承担，只保留执行端口，画面上不再有数据线。
        bool exprMode = owner != null && owner.CurrentGraph != null && owner.CurrentGraph.ExprMode;
        bool vertical = owner == null || owner.VerticalPorts;
        float w = CardWidth(t, node, exprMode), h = CardHeight(t, exprMode, vertical, node);
        // 名牌本身就是读数屏的结构（木箱 / 工作台），字段盖在名牌上，不再单独占一行
        int exprCount = ModuleViz.PlateHoldsField(skinKind) ? 0 : VisibleExprCount(t.Key, node);
        c.exprTop = GraphView.CardTop(t.Key, skinKind);
        // 动图舞台插在表达式字段和端口之间；横排布局时端口行要整体让到舞台下面
        float stageH = ModuleViz.StageHFor(node);
        c.portTop = c.exprTop + exprCount * GraphView.ExprFieldGap + (vertical ? 0f : stageH);
        c.Rect.anchorMin = c.Rect.anchorMax = new Vector2(0f, 1f);
        c.Rect.pivot = new Vector2(0f, 1f);
        c.Rect.sizeDelta = new Vector2(w, h);
        c.Rect.anchoredPosition = new Vector2(node.X, -node.Y);

        // 1) 底板（先画一层 2px 投影，把卡片从同色系画布上拉开）
        var shadow = GraphView.Tile(go.transform, "Shadow", Theme.A(Theme.WhaleDeep, .22f), false);
        var srt = shadow.rectTransform;
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(2f, -2f); srt.offsetMax = new Vector2(2f, -2f);

        var body = GraphView.Tile(go.transform, "Body", skinned ? ModuleViz.BaseColor(skinKind) : Theme.Paper, true);
        GraphView.Stretch(body.rectTransform);

        // 1.5) 外形：只有查询类小模块走这条路 —— 卡面不再是那张 Stretch 的长方形白图（清一色长方形的根因），
        // 改由 CardShapes 按行切出的形状当底板。名字挪到卡片上方的小标签里，卡面正中只留一行值，
        // 尖角形状才切得到字；切不到字时 PickShape 自己退回矩形，宁可方一点也不能遮字。
        float shapeTextW = ShapeCardTextWidth(node) - ShapeSidePad * 2f;
        CardShape shape = PickShape(t.Key, exprCount, w, h, shapeTextW, PixelText.CellH);
        bool shaped = shape != CardShape.Rect;
        if (shaped)
        {
            body.color = new Color(1f, 1f, 1f, 0f);              // 底板让位给形状，射线还归它（拖卡点卡都走 Body）
            shadow.color = Theme.A(Theme.WhaleDeep, 0f);          // 矩形投影会把四个直角露出来
            CardShapes.Draw(c.Rect, "Shape", shape, w, h,
                Theme.AccentSoft(ColorKeyFor(node), .62f), Theme.Border);
        }

        // 标题带按模块类别上色：流程青、变量粉、输入输出浅青、运算黄、栈紫。
        // 一整屏卡片不再都是纸白，玩家扫一眼就知道这块管什么；带子只铺到 TitleH 为止，
        // 身份行正好接在下面，不会被压住。有结构皮肤的模块自己画招牌，不再铺这条带子。
        if (!skinned && !shaped)
        {
            var band = GraphView.Tile(go.transform, "TitleBand", Theme.AccentSoft(ColorKeyFor(node), .30f), false);
            var brt = band.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(1f, -1f);
            brt.sizeDelta = new Vector2(w - 2f, GraphView.TitleH - 1f);
        }

        // 选中底色：1px 边框在这个尺寸的画布上太不起眼，选中再加一层淡蓝底
        c.selectTint = GraphView.Tile(go.transform, "SelectTint", Theme.A(Theme.Whale, 0f), false);
        if (shaped)
        {
            // 形状卡的选中底色只铺中间那条平的地方，铺满整格会把形状外头也涂方
            var ert = c.selectTint.rectTransform;
            ert.anchorMin = ert.anchorMax = new Vector2(0f, 1f);
            ert.pivot = new Vector2(0f, 1f);
            ert.anchoredPosition = new Vector2((w - shapeTextW) * .5f, -(h - PixelText.CellH) * .5f);
            ert.sizeDelta = new Vector2(shapeTextW + 8f, PixelText.CellH + 6f);
        }
        else GraphView.Stretch(c.selectTint.rectTransform);

        // 2) 四边 1px 边框（形状卡自带一圈描边，再叠直角边框就把切出来的角又框回长方形了）
        if (!shaped)
        {
            c.AddEdge("Top",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(.5f, 1f), new Vector2(0f, 1f));
            c.AddEdge("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(.5f, 0f), new Vector2(0f, 1f));
            c.AddEdge("Left",   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, .5f), new Vector2(1f, 0f));
            c.AddEdge("Right",  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, .5f), new Vector2(1f, 0f));
        }

        // 2b) 轮廓台阶：按结构种类往框外面凸几块，十三种模块不再是同一块长方形（几何见 ModuleViz.SilhouetteBlocks）
        if (skinned)
            foreach (var b in ModuleViz.SilhouetteBlocks(skinKind, w, h))
            {
                var sil = GraphView.Tile(go.transform, "Silhouette", Theme.Border, false);
                var srt2 = sil.rectTransform;
                srt2.anchorMin = srt2.anchorMax = new Vector2(0f, 1f);
                srt2.pivot = new Vector2(0f, 1f);
                srt2.anchoredPosition = new Vector2(b.x, -b.y);
                srt2.sizeDelta = new Vector2(b.width, b.height);
            }

        // 3) 标题：左侧一枚 16x16 模块图标，文字往后让出图标宽度（有结构皮肤的模块把标题写在招牌上）
        if (!skinned && shaped)
        {
            // 形状卡里塞不下图标 + 标题，把名字挂到卡片上方的外面：卡越小，名字越要在外面
            var above = PixelText.Create(go.transform, TitleFor(node), Theme.Ink);
            above.raycastTarget = false;
            above.Align = PixelText.TextAlign.Center;
            var art = above.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(0f, 1f);
            art.pivot = new Vector2(.5f, 0f);
            // 上边缘那个端口有一半探出卡外（13 高 → 出来 6.5），标题再压在 y=3 就被端口方块咬住底
            art.anchoredPosition = new Vector2(w * .5f, 9f);
            art.sizeDelta = new Vector2(Mathf.Max(40f, w + 24f), PixelText.CellH);
        }
        else if (!skinned)
        {
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(go.transform, false);
            var irt = (RectTransform)icon.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 1f);
            irt.pivot = new Vector2(0f, 1f);
            irt.anchoredPosition = new Vector2(6f, -4f);
            irt.sizeDelta = new Vector2(16f, 16f);
            var iimg = icon.GetComponent<Image>();
            iimg.sprite = ModuleIcons.Get(arrayDecl ? "array_new" : t.Key);
            iimg.preserveAspect = true;
            iimg.raycastTarget = false;

            var title = PixelText.Create(go.transform, TitleFor(node), Theme.Ink);
            title.raycastTarget = false;
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(26f, -4f);
        }

        // 3.4) 断点小圆点：卡片左外侧 6px 处，不占卡片内部空间
        var dot = new GameObject("BreakDot", typeof(RectTransform), typeof(Image));
        dot.transform.SetParent(go.transform, false);
        var drt = (RectTransform)dot.transform;
        drt.anchorMin = drt.anchorMax = new Vector2(0f, 1f);
        drt.pivot = new Vector2(.5f, .5f);
        drt.anchoredPosition = new Vector2(-7f, -12f);
        drt.sizeDelta = new Vector2(12f, 12f);
        var dimg = dot.GetComponent<Image>();
        dimg.sprite = GraphView.White;
        dimg.type = Image.Type.Simple;
        dimg.color = Theme.Border;
        dimg.raycastTarget = true;
        var dclick = dot.AddComponent<BreakDotClick>();
        dclick.Card = c;
        c.breakDot = dimg;

        // 3.6) 折叠按钮：循环节点右上角一个 14x14 小块，点一下把循环体收成缩略图小块
        if (node.Type == "forloop" || node.Type == "whileloop")
        {
            var fold = new GameObject("FoldButton", typeof(RectTransform), typeof(Image));
            fold.transform.SetParent(go.transform, false);
            var frt = (RectTransform)fold.transform;
            frt.anchorMin = frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(1f, 1f);
            frt.anchoredPosition = new Vector2(-4f, -4f);
            frt.sizeDelta = new Vector2(16f, 16f);
            var fimg = fold.GetComponent<Image>();
            fimg.sprite = GraphView.White; fimg.type = Image.Type.Simple;
            fimg.color = Theme.Foam; fimg.raycastTarget = true;
            var flbl = PixelText.Create(fold.transform, "", Theme.WhaleDeep, 1);
            flbl.raycastTarget = false;
            var flrt = flbl.rectTransform;
            flrt.anchorMin = flrt.anchorMax = new Vector2(0f, 1f);
            flrt.pivot = new Vector2(0f, 1f);
            flrt.anchoredPosition = new Vector2(2f, -1f);
            c.foldLabel = flbl;
            c.UpdateFoldLabel();
            var fclick = fold.AddComponent<FoldClick>();
            fclick.Card = c;
        }

        // 3.5) 内嵌表达式字段：身份行下面依次排开，宽 = 卡片宽 - 16
        c.BuildExprFields(t, w);

        // 3.7) 结构皮肤：整张卡就是一个会动的结构，动画元件在结构里面动，身份写在结构的名牌上
        if (skinned)
        {
            c.skin = ModuleViz.BuildSkin(go.transform, node, w, h,
                c.exprTop + exprCount * GraphView.ExprFieldGap, stageH, c.PlateLabel);
            c.AlignPlateField();
            c.AlignLoopVarClick();
        }

        // 4) 端口：只剩执行端口。数据线已经彻底退出画布（值全部走内嵌表达式），
        // 上下布局时输入沿上边缘、输出沿下边缘；左右布局时沿左右边缘逐行排。
        int inTotal = t.ExecIn != null ? 1 : 0;
        int outTotal = t.ExecOut.Count;
        int inRow = 0, outRow = 0;
        // 「循环体」「循环结束」这类四字标签比 30px 格距宽，先量一遍把出口的格距撑开，免得叠字
        if (vertical)
            foreach (var e in t.ExecOut)
                if (!string.IsNullOrEmpty(e.Label))
                    c.outColPitch = Mathf.Max(c.outColPitch, PixelText.MeasureString(e.Label, 1).x + 8f);
        // 上下布局里执行入口在卡片上边缘，标签塞进卡片必然压在图标和标题上；
        // 循环头是唯一必须写清楚的入口（玩家分不清哪个口进循环、哪个口跳出循环），
        // 所以给它写「循环开始」，并画到卡片上边缘之外（AddPortElement 里判断）。
        bool loopHead = node.Type == "forloop" || node.Type == "whileloop";
        if (t.ExecIn != null) c.AddPortElement(t.ExecIn, vertical ? (loopHead ? "循环开始" : "") : "执行", false, inTotal, ref inRow);
        foreach (var e in t.ExecOut) c.AddPortElement(e.Id, string.IsNullOrEmpty(e.Label) ? "执行" : e.Label, true, outTotal, ref outRow);

        // 5) 模块身份行：标题下面居中一行 12px，写「这个模块关于哪个变量」；样例板仍留在左下角。
        // 有结构皮肤的模块把身份写在结构名牌上，不再重复画一行。
        if (!skinned)
        {
            var summary = c.SummaryText();
            if (!string.IsNullOrEmpty(summary))
            {
                c.summaryLabel = c.CreateSummaryLabel();
                c.summaryLabel.Text = Fit(summary, w - (IsShapeCard(t.Key) ? ShapeSidePad * 2f : 16f));
            }
        }

        // 新卡片淡入 .15s：从模块栏拖进来、粘贴、原地重建都不再「啪」地出现
        if (owner != null && !owner.InstantView) c.FadeIn(.15f);
        return c;
    }

    // 只改透明度，不动层级与射线：淡入期间卡片已经可以点
    public void FadeIn(float dur)
    {
        var g = gameObject.GetComponent<CanvasGroup>();
        if (g == null) g = gameObject.AddComponent<CanvasGroup>();
        StopAllCoroutines();
        StartCoroutine(FadeCo(g, dur));
    }

    System.Collections.IEnumerator FadeCo(CanvasGroup g, float dur)
    {
        g.alpha = 0f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            g.alpha = Mathf.Clamp01(t / dur);
            yield return null;
        }
        g.alpha = 1f;
        Destroy(g);
    }

    // ---------------- 内嵌表达式：构建 / 回写 / 校验 ----------------

    void BuildExprFields(NodeType t, float cardW)
    {
        var keys = ExprFieldKeys(t.Key);
        if (keys == null || keys.Length == 0) return;
        var vars = BuildVariables(Owner != null ? Owner.CurrentGraph : null, Node != null ? Node.Uid : null);
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            var field = ExpressionFieldUI.Create(transform, Param(key), vars, MakeValidator(key), v => CommitExpr(key, v), cardW - 16f);
            field.Context = ExprFieldContext(t.Key, key);
            field.TabLabel = ExprFieldTab(t.Key, key);
            field.TabGroup = keys.Length > 1 ? keys : null;
            field.Card = this;
            field.FieldKey = key;
            // 创建变量的初始值写在木箱名牌「x = 0」上：字段永远折叠成隐形点击区，
            // 位置由 AlignPlateField 盖到名牌上，双击名牌照样打开小窗。
            // 计数循环折叠成「计数起点 / 计数终点」两行文字的点击区：卡面不再画两条长白框。
            bool cratePlate = t.Key == "vardecl";
            bool loopLines = t.Key == "forloop";
            field.SetCollapsed(cratePlate || loopLines);
            if (loopLines)
                field.SetCollapsedArea(10f, LoopLineTop(t.Key, i + 1), cardW - 20f, LoopLinePitch - 1f);
            else
                field.Rect.anchoredPosition = cratePlate
                    ? new Vector2(8f, -GraphView.TitleH)
                    : new Vector2(8f, -(exprTop + i * GraphView.ExprFieldGap));
            exprFields[key] = field;
        }
    }

    // 结构名牌上的文字：创建变量写 x = 0；有内嵌表达式字段的模块，字段本身就是读数屏，不再重复挂名牌
    public string PlateLabel()
    {
        if (Node == null) return "";
        if (Node.Type == "vardecl") return ModuleViz.PlateText(Node);
        if (ExprFieldCount(Node.Type) > 0) return "";
        switch (Node.Type)
        {
            case "const":  return Param("value");
            case "varget": return Param("name");
        }
        return SummaryText() ?? "";
    }

    // 木箱 / 工作台中间那块就是表达式的读数屏：隐形字段盖在名牌上（双击打开小窗），
    // 可见字段整体下移到结构中段，卡片上不再出现「上面一大行表达式、下面一行名字」
    void AlignPlateField()
    {
        if (skin == null || !skin.PlateIsFieldSlot) return;
        var a = skin.PlateArea;
        if (a.width <= 1f || a.height <= 1f) return;
        var keys = ExprFieldKeys(Node.Type);
        if (keys == null) return;
        for (int i = 0; i < keys.Length; i++)
        {
            ExpressionFieldUI f;
            if (!exprFields.TryGetValue(keys[i], out f) || f == null) continue;
            if (f.IsCollapsed) f.SetCollapsedArea(a.x, a.y + i * GraphView.ExprFieldGap, a.width, a.height);
            // 字段框（28 高）比名牌（26 高）高 2px：按行高反推偏移，白框才不会压到名牌下沿外的端口标签
            else f.Rect.anchoredPosition = new Vector2(8f, -(a.y + i * GraphView.ExprFieldGap + (a.height - ExpressionFieldUI.Height) * .5f));
        }
    }

    // 按 Params 重新同步字段显示值（例如 -exprnodes 预置示例表达式后）
    public void SyncExprFields()
    {
        foreach (var kv in exprFields)
            if (kv.Value != null) kv.Value.SetValue(Param(kv.Key));
    }

    // 变量列表变化（改名 / 加删变量）后重建字段，让左侧元素栏跟着更新
    public void RebuildExprFields()
    {
        foreach (var kv in exprFields) if (kv.Value != null) Destroy(kv.Value.gameObject);
        exprFields.Clear();
        var t = NodeTypes.Get(Node.Type);
        if (t != null) BuildExprFields(t, Rect.sizeDelta.x);
    }

    // 代码方式打开某个表达式字段的小窗（-exprcard 拖拽验收入口）
    public bool OpenExprField(string key)
    {
        ExpressionFieldUI f;
        if (!exprFields.TryGetValue(key, out f) || f == null) return false;
        f.OpenPopover();
        return true;
    }

    public ExpressionFieldUI GetExprField(string key)
    {
        ExpressionFieldUI f;
        return exprFields.TryGetValue(key, out f) ? f : null;
    }

    // 双击模块时直接进第一个表达式字段（玩家不用再移鼠标去点那条框）
    public bool TryOpenFirstExprField()
    {
        var keys = Node != null ? ExprFieldKeys(Node.Type) : null;
        if (keys == null || keys.Length == 0) return false;
        return OpenExprField(keys[0]);
    }

    // 计数循环第一行「循环变量：i」的点击区：它不是表达式，双击要开参数窗改名字。
    // 皮肤先建、点击区后盖， invisible 图不影响渲染，只吃掉这次双击不再冒泡到卡片。
    void AlignLoopVarClick()
    {
        if (skin == null || Node == null || Node.Type != "forloop") return;
        var a = skin.LoopVarArea;
        if (a.width <= 1f || a.height <= 1f) return;
        if (loopVarClick == null)
        {
            var go = new GameObject("LoopVarClick", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = GraphView.White;
            img.type = Image.Type.Simple;
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;
            var click = go.AddComponent<LoopVarClick>();
            click.Card = this;
            loopVarClick = (RectTransform)go.transform;
        }
        loopVarClick.anchorMin = loopVarClick.anchorMax = new Vector2(0f, 1f);
        loopVarClick.pivot = new Vector2(0f, 1f);
        loopVarClick.anchoredPosition = new Vector2(a.x, -a.y);
        loopVarClick.sizeDelta = new Vector2(a.width, a.height);
    }

    // 表达式提交：空值也写空字符串，随后刷新底部摘要
    public void CommitExpr(string key, string v)
    {
        if (Node == null) return;
        if (Node.Params == null) Node.Params = new Dictionary<string, string>();
        Node.Params[key] = v ?? "";
        // 卡片的宽高都跟内容走：初始值从「没写」变成「写了」会多一条空白条（高变），
        // 表达式写长了要把卡撑宽（宽变），两种都只能原地把这张卡重建一次。
        if (Owner != null)
        {
            var t = NodeTypes.Get(Node.Type);
            var g = Owner.CurrentGraph;
            bool em = g != null && g.ExprMode;
            float wantH = CardHeight(t, em, Owner.VerticalPorts, Node);
            float wantW = CardWidth(t, Node, em);
            if (Mathf.Abs(wantH - Rect.sizeDelta.y) > .5f || Mathf.Abs(wantW - Rect.sizeDelta.x) > .5f)
            { Owner.RebuildCard(Node); return; }
        }
        RefreshSummary();
    }

    Func<string, string> MakeValidator(string key)
    {
        string type = Node != null ? Node.Type : "";
        return text => ValidateExpr(type, key, text);
    }

    // 语法 + 类型检查，返回第一条中文错误；合法返回空串
    public string ValidateExpr(string type, string key, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var g = Owner != null ? Owner.CurrentGraph : null;
        var scope = BuildTypeTable(g, Node != null ? Node.Uid : null);

        // 「赋值」里写的就是整条赋值语句：a[i] = x
        if (type == "varset" && key == "valueExpr")
        {
            var asg = Assign.TryParse(text);
            if (asg != null) return ValidateAssignment(g, asg, scope);
        }

        var parsed = ExprLang.Parse(text);
        if (!parsed.Ok) return parsed.Error;

        var check = ExprLang.Check(parsed.Root, scope);
        if (!check.Ok)
        {
            // ExprLang.Check 把“结果是数组”当作错误：变量初始化 / 赋值允许数组，其余沿用它的报错
            if (!ExprLang.IsArrayType(check.Type)) return WithScopeHint(check.Error, scope);
            if (key != "initExpr" && key != "valueExpr") return WithScopeHint(check.Error, scope);
        }
        string resultType = check.Type;

        switch (key)
        {
            case "condExpr":
                return resultType == "bool" ? "" : "条件表达式必须是 bool，实际为 " + resultType + "。";
            case "fromExpr":
            case "toExpr":
                return (resultType == "int" || resultType == "double")
                    ? "" : "循环边界必须是 int 或 double，实际为 " + resultType + "。";
            case "indexExpr":
                return resultType == "int" ? "" : "下标表达式必须是整数，实际为 " + resultType + "。";
            case "initExpr":
            {
                string want = ExprTypeOf(Param("vtype"));
                return CompatibleWith(resultType, want)
                    ? "" : "初始化表达式类型 " + resultType + " 与变量类型 " + want + " 不兼容。";
            }
            case "valueExpr":
            {
                string want = ExprTypeOf(GraphUtil.VarType(g, Param("name")));
                if (string.IsNullOrEmpty(want)) return "";
                return CompatibleWith(resultType, want)
                    ? "" : "赋值表达式类型 " + resultType + " 与变量 " + Param("name") + " 的类型 " + want + " 不兼容。";
            }
        }
        // outputExpr：ExprLang.Check 已经拒绝了数组结果
        return "";
    }

    // 卡片上写 a[i] = x 这类整条赋值语句时的即时校验，口径和编译器一致。
    string ValidateAssignment(Graph g, AssignStatement asg, Dictionary<string, string> scope)
    {
        string want = ExprTypeOf(VarTypeInScope(g, scope, asg.Name));
        if (string.IsNullOrEmpty(want)) return "变量「" + asg.Name + "」在这里还没创建：先把它上面的模块用执行线连进来，再在它下面写表达式。";
        var rhs = ExprLang.Check(asg.Rhs, scope);
        if (!asg.IsIndexed)
        {
            if (ExprLang.IsArrayType(rhs.Type))
                return ExprLang.IsArrayType(want)
                    ? (ExprLang.ElementType(want) == ExprLang.ElementType(rhs.Type) ? "" : "元素类型 " + ExprLang.ElementType(rhs.Type) + " 与数组 " + asg.Name + " 的元素类型 " + ExprLang.ElementType(want) + " 不兼容。")
                    : "变量 " + asg.Name + " 的类型是 " + want + "，不能赋值为数组。";
            if (!rhs.Ok) return WithScopeHint(rhs.Error, scope);
            return CompatibleWith(rhs.Type, want)
                ? "" : "赋值表达式的类型是 " + rhs.Type + "，与变量 " + asg.Name + " 的类型 " + want + " 不兼容。";
        }
        if (!ExprLang.IsArrayType(want)) return "变量 " + asg.Name + " 的类型是 " + want + "，不是数组，不能写下标赋值。";
        var idx = ExprLang.Check(asg.IndexExpr, scope);
        if (!idx.Ok) return WithScopeHint(idx.Error, scope);
        if (idx.Type != "int") return "下标表达式必须是整数，实际为 " + idx.Type + "。";
        if (ExprLang.IsArrayType(rhs.Type)) return "下标赋值右边要给出一个元素，不能是整个数组；整段赋值请写 " + asg.Name + " = …。";
        if (!rhs.Ok) return WithScopeHint(rhs.Error, scope);
        string elem = ExprTypeOf(ExprLang.ElementType(want));
        return CompatibleWith(rhs.Type, elem)
            ? "" : "写入值的类型是 " + rhs.Type + "，与数组 " + asg.Name + " 的元素类型 " + elem + " 不兼容。";
    }

    // 优先按「这个节点能看见的变量」查类型，退回全图声明，方便孤立节点也能编辑。
    static string VarTypeInScope(Graph g, Dictionary<string, string> scope, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string t;
        if (scope != null && scope.TryGetValue(name, out t)) return t;
        return g != null ? GraphUtil.VarType(g, name) : null;
    }

    // ExprLang 只说「未定义的变量」，卡片上补一句该怎么连。
    static string WithScopeHint(string message, Dictionary<string, string> scope)
    {
        if (string.IsNullOrEmpty(message) || scope == null) return message;
        if (message.IndexOf("未定义", StringComparison.Ordinal) < 0) return message;
        return message + "在这里还没创建的变量，要先用执行线把它上面的模块连进来。";
    }

    static bool CompatibleWith(string actual, string expected)
    {
        if (string.IsNullOrEmpty(expected)) return true;
        if (actual == expected) return true;
        if (ExprLang.IsArrayType(actual) || ExprLang.IsArrayType(expected)) return false;
        return (actual == "int" || actual == "double") && (expected == "int" || expected == "double");
    }

    // 模块身份行：标题下面居中一行 12px，写「这个模块关于哪个变量」；
    // 样例板自己带数字块，它的摘要仍留在卡片左下角。
    PixelText CreateSummaryLabel()
    {
        var text = PixelText.Create(transform, "", Theme.Ink);
        text.raycastTarget = false;
        var rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        float w = Mathf.Max(1f, Rect.sizeDelta.x - 16f);
        // 形状卡：值写在形状正中间（那里最宽，尖角形状切不到字），标题已经在卡片外面
        if (IsShapeCard(Node != null ? Node.Type : null))
        {
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(Rect.sizeDelta.x * .5f, -Rect.sizeDelta.y * .5f);
            rt.sizeDelta = new Vector2(Mathf.Max(1f, Rect.sizeDelta.x), PixelText.CellH);
            text.Align = PixelText.TextAlign.Center;
            return text;
        }
        if (GraphView.BottomSummary(Node != null ? Node.Type : null))
        {
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(8f, GraphView.FootGap - 2f);
            rt.sizeDelta = new Vector2(w, 14f);
            return text;
        }
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(8f, -GraphView.TitleH);
        rt.sizeDelta = new Vector2(w, GraphView.IdentityH);
        text.Align = PixelText.TextAlign.Center;
        return text;
    }

    // 参数改完后刷新卡片底部摘要（不重建卡片，避免打断拖动/选中）
    public void RefreshSummary()
    {
        if (Node == null) return;
        // 有结构皮肤的模块，身份写在结构名牌上，刷新名牌即可
        if (skin != null) { skin.RefreshPlate(); return; }
        var summary = SummaryText();
        if (string.IsNullOrEmpty(summary))
        {
            if (summaryLabel != null) summaryLabel.Text = "";
            return;
        }
        if (summaryLabel == null) summaryLabel = CreateSummaryLabel();
        // 形状卡的宽度本来就是「字宽 + 两侧各 ShapeSidePad」，按普通卡的 -16 去截会把最后一个字截掉
        summaryLabel.Text = Fit(summary, Rect.sizeDelta.x - (IsShapeCard(Node.Type) ? ShapeSidePad * 2f : 16f));
    }

    // 模块身份行：这个模块关于哪个变量 / 哪段数据。
    // 写好了内嵌表达式的模块，表达式已经在卡片中间居中显示，身份行不再重复它；
    // 还在用数据线的旧图没有表达式字段，才把值写进身份行。
    public string SummaryText()
    {
        string type = Node.Type ?? "";
        bool inline = ExprFieldCount(type) > 0;
        switch (type)
        {
            case "const":    return NodeTypes.TypeNameCn(Param("vtype")) + " " + Param("value");
            case "input":
            {
                var name = Param("name");
                return string.IsNullOrEmpty(name) ? "读入" : "读入 " + name;
            }
            case "sample_input":
            {
                var preview = Param("preview");
                return string.IsNullOrEmpty(preview) ? "样例组 " + Param("group") : preview;
            }
            case "sample_compare":
            {
                var preview = Param("preview");
                return string.IsNullOrEmpty(preview) ? "对比输出" : "期望 " + preview;
            }
            // 创建变量：中文类型名 + 英文原名进括号 + 变量名，没写初始值时这就是卡片的全部内容
            case "vardecl":  return VardeclLine(Node);
            case "varset":
            {
                string name = Param("name"), value = Param("valueExpr");
                var asg = Assign.TryParse(value);
                if (asg != null) name = asg.Name;
                if (inline || string.IsNullOrEmpty(value)) return name;
                // 整条赋值语句（a[i] = x、s += t）自己就带着变量名，不能再往前面拼一次「name =」
                return asg != null ? value.Trim() : name + " = " + value;
            }
            case "varget":   return Param("name");
            case "binop":
            {
                var formula = Param("formula");
                if (!string.IsNullOrEmpty(formula)) return formula;
                var a = Param("varA");
                var b = Param("varB");
                var op = Param("op");
                return (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)) ? a + " " + op + " " + b : op;
            }
            case "compare":
            case "logic":    return Param("op");
            case "output":
            {
                if (inline) return "输出";
                var e = Param("outputExpr");
                return string.IsNullOrEmpty(e) ? "输出" : "输出 " + e;
            }
            case "branch":
            {
                if (inline) return "如果";
                var e = Param("condExpr");
                return string.IsNullOrEmpty(e) ? "如果" : "如果 " + e;
            }
            case "whileloop":
            {
                if (inline) return "当";
                var e = Param("condExpr");
                return string.IsNullOrEmpty(e) ? "当" : "当 " + e;
            }
            case "forloop":
            {
                if (inline) return "循环变量 " + VarScope.IndexName(Node);
                // 卡面上是三行文字（见 ModuleViz.BuildLoopLines），这里留给没有皮肤的旧图，口径保持一致
                return ForloopLine(Node, 1) + "，" + ForloopLine(Node, 2);
            }
            case "structnew":return NodeCard.StructLabel(Param("structId"));
        }
        if (type.StartsWith("stack_") || type.StartsWith("queue_")) return NodeCard.StructLabel(Param("structId"));
        return null;
    }

    string Param(string key)
    {
        var v = GraphUtil.Get(Node, key);
        if (!string.IsNullOrEmpty(v)) return v;
        var t = NodeTypes.Get(Node.Type);
        if (t != null)
        {
            var d = t.Params.Find(p => p.Id == key);
            if (d != null && !string.IsNullOrEmpty(d.Default)) return d.Default;
        }
        return "";
    }

    // 摘要超出卡片宽度时截断并补 ".."，避免压到边框
    static string Fit(string s, float maxW)
    {
        if (maxW <= 0f || PixelText.MeasureString(s).x <= maxW) return s;
        var cur = s;
        while (cur.Length > 0)
        {
            cur = cur.Substring(0, cur.Length - 1);
            if (PixelText.MeasureString(cur + "..").x <= maxW) return cur + "..";
        }
        return "";
    }

    void AddEdge(string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size)
    {
        var img = GraphView.Tile(transform, "Edge" + name, Theme.Border, false);
        var rt = img.rectTransform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        borders.Add(img);
    }

    // 端口行距：左右布局按纵向 18px 一行；上下布局按横向 30px 一格，围绕卡片中线对称排开。
    const float PortColPitch = GraphView.PortColPitch;

    // 执行端口（画布上唯一的一种端口）：id / 标签 / 朝向，row 是同类端口的排号
    void AddPortElement(string id, string label, bool output, int total, ref int row)
    {
        float w = Rect.sizeDelta.x, h = Rect.sizeDelta.y;
        bool vertical = Owner == null || Owner.VerticalPorts;
        var port = Port.Create(transform, Owner, Node.Uid, id, LinkKind.Exec, output, label);

        var prt = port.Rect;
        prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(GraphView.PortSize, GraphView.PortSize);

        float half = GraphView.PortSize * 0.5f;
        Vector2 pos;
        if (vertical)
        {
            float pitch = output ? outColPitch : PortColPitch;
            float cx = w * 0.5f + (row - Mathf.Max(0, total - 1) * 0.5f) * pitch;
            cx = Mathf.Clamp(cx, half + 2f, w - half - 2f);
            pos = new Vector2(cx, output ? -h : 0f);
        }
        else
            pos = new Vector2(output ? w : 0f, -(portTop + row * GraphView.PortRowH));
        prt.anchoredPosition = pos;
        port.SetHitLayout(vertical, output);

        // 端口文字标签：留在卡片内部
        if (!string.IsNullOrEmpty(label))
        {
            var text = PixelText.Create(transform, label, Theme.WhaleDeep);
            text.raycastTarget = false;
            var lrt = text.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 1f);
            float tw = text.Measure().x;
            if (vertical && !output)
            {
                // 循环头的「循环开始」：卡片顶部已经被图标 + 标题占满，写进去必然压字，
                // 所以挪到卡片上边缘之外、端口右侧，正好落在循环体外框的标题条里。
                float lx = pos.x + half + 4f;
                if (lx + tw > w - 2f) lx = Mathf.Max(2f, pos.x - half - 4f - tw);
                lrt.anchoredPosition = new Vector2(lx, GraphView.TextH + 2f);
            }
            else if (vertical)
            {
                // 上边缘端口：标签贴在端口下方；下边缘端口：标签贴在端口上方，都离边 3px
                float lx = Mathf.Max(2f, Mathf.Min(pos.x - tw * 0.5f, w - 2f - tw));
                float top = output ? h - half - 3f - GraphView.TextH : half + 3f;
                lrt.anchoredPosition = new Vector2(lx, -top);
            }
            else
            {
                float pad = half + 4f;                          // 与端口方块留 4px 间隙
                float lx = output ? (w - pad - tw) : pad;
                lx = Mathf.Max(2f, Mathf.Min(lx, w - 2f - tw));
                lrt.anchoredPosition = new Vector2(lx, pos.y + 7f);
            }
        }

        Ports.Add(port);
        row++;
    }

    public bool Highlighted;

    // 折叠按钮上的符号：展开时是减号（点一下缩成小块），折叠时是加号（点一下还原）
    public void UpdateFoldLabel()
    {
        if (foldLabel == null) return;
        bool folded = Owner != null && Node != null && Owner.IsLoopFolded(Node.Uid);
        foldLabel.Text = folded ? "+" : "-";
    }

    public void SetSelected(bool s)
    {
        Selected = s;
        Retint();
    }

    // 外部高亮：边框改 Theme.Whale、整体放大 4%；关闭时同时还原比例与边框色
    public void SetHighlight(bool on)
    {
        Highlighted = on;
        Retint();
    }

    // 选中 / 高亮的强度补间：边框色、选中底色、放大比例都跟着这一个 k 走，
    // 点选不再是一帧变色，松手也不会「啪」地褪掉
    float tintK, tintWant;

    void Retint()
    {
        tintWant = Selected ? 1f : (Highlighted ? .7f : 0f);
        if (Owner != null && Owner.InstantView) { tintK = tintWant; ApplyTint(); }
    }

    void Update()
    {
        if (Mathf.Approximately(tintK, tintWant)) return;
        tintK = Mathf.MoveTowards(tintK, tintWant, Time.unscaledDeltaTime * 9f);
        ApplyTint();
    }

    void ApplyTint()
    {
        foreach (var b in borders) if (b != null) b.color = Color.Lerp(Theme.Border, Theme.Whale, tintK);
        if (selectTint != null) selectTint.color = Theme.A(Theme.Whale, .16f * tintK);
        transform.localScale = Vector3.one * (1f + .04f * tintK);
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right)
        {
            Owner.NotifyDelete(this);
            return;
        }
        if (e.button == PointerEventData.InputButton.Left)
        {
            // 这套 UGUI 的 PointerEventData 不带 modifiers，Ctrl 只能直接问键盘
            bool additive = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            Owner.SelectForDrag(this, additive);
            Owner.BeginNodeDrag(this, e); Owner.NotifySelected(this);
        }
    }

    // 断点小圆点：卡片左侧的独立可点区域，点它切换断点，不会拖动节点
    public void ToggleBreakpointDot()
    {
        if (Owner != null) Owner.ToggleBreakpoint(Node != null ? Node.Uid : null);
    }

    // 双击运算节点直接打开参数编辑窗
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (lastClickTime > 0f && Time.unscaledTime - lastClickTime < .32f)
        {
            lastClickTime = -1f;
            Owner.NotifyEdit(this);
            return;
        }
        lastClickTime = Time.unscaledTime;
    }
    public void OnDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left){Owner.DragNode(this,e);}}
    public void OnPointerUp(PointerEventData e){Owner.EndNodeDrag(this);}
}

// 断点圆点的点击处理：吞掉事件，避免触发节点拖动
public sealed class BreakDotClick : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    public NodeCard Card;
    public void OnPointerDown(PointerEventData e) { e.Use(); }
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (Card != null) Card.ToggleBreakpointDot();
    }
}

// 连线中点上的删除点：悬停到线上才出现，点一下删掉这根线
public sealed class LinkDotClick : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    public GraphView Owner;
    public void OnPointerDown(PointerEventData e) { e.Use(); }
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        e.Use();
        if (Owner != null) Owner.DeleteHoveredLink();
    }
}

// 循环卡片右上角的折叠按钮：把循环体缩成一块看缩略图，再点一下还原
public sealed class FoldClick : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    public NodeCard Card;
    public void OnPointerDown(PointerEventData e) { e.Use(); }
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (Card != null && Card.Owner != null) Card.Owner.ToggleLoopFold(Card.Node != null ? Card.Node.Uid : null);
    }
}

// 计数循环卡上「循环变量：i」那一行：双击开参数窗改名字。
// 和表达式字段一个套路：单击照常留给卡片选中 / 拖动，只有双击这次被 e.Use() 吃掉，
// 否则卡片自己的双击逻辑会把起点 / 终点的表达式浮窗一起弹出来。
public sealed class LoopVarClick : MonoBehaviour, IPointerClickHandler
{
    public NodeCard Card;
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (e.clickCount < 2) return;
        e.Use();
        if (Card != null && Card.Owner != null) Card.Owner.NotifyParams(Card);
    }
}

// 循环体外框顶上的标题条：拖它 = 循环头和整个循环体一起走。
// 按下必须 e.Use() 吃掉，否则事件冒泡到 GraphView 会被当成「空白处框选」，先把已选中的卡片全清掉。
public sealed class LoopFrameDrag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public GraphView Owner;
    public string LoopUid;
    public RectTransform Rect => (RectTransform)transform;
    bool grabbed;

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        grabbed = true;
        e.Use();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (grabbed && Owner != null) Owner.BeginLoopFrameDrag(LoopUid, e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (grabbed && Owner != null) Owner.DragNode(null, e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!grabbed) return;
        grabbed = false;
        if (Owner != null) Owner.EndLoopFrameDrag();
    }
}

public sealed class Port : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public GraphView Owner; public string NodeUid,PortId,Label; public LinkKind Kind; public bool Output,Pending;
    public RectTransform Rect; public Image Img; public RectTransform HitRect;
    public static Port Create(Transform parent,GraphView owner,string n,string id,LinkKind kind,bool output,string label)
    {
        var go=new GameObject("Port_"+id,typeof(RectTransform),typeof(Image));
        go.transform.SetParent(parent,false);
        var p=go.AddComponent<Port>();
        p.Owner=owner;p.NodeUid=n;p.PortId=id;p.Kind=kind;p.Output=output;p.Label=label;
        p.Rect=go.GetComponent<RectTransform>();
        p.Rect.sizeDelta=new Vector2(GraphView.PortSize,GraphView.PortSize);
        var i=go.GetComponent<Image>();
        i.sprite=GraphView.White; i.type=Image.Type.Simple;
        i.color=Theme.Whale;     // 画布上只有执行端口一种颜色，蜜黄色的数据端口已经彻底取消
        i.raycastTarget=true;
        p.Img=i;

        // 透明热区：方块本身只有 13px，可点范围靠这一层撑到 26 以上，小方块才好按。
        // 尺寸在 AddPortElement 排好位置后按端口朝向再收一次（SetHitLayout）。
        var hit=new GameObject("Hit",typeof(RectTransform),typeof(Image));
        hit.transform.SetParent(go.transform,false);
        var hrt=(RectTransform)hit.transform;
        hrt.anchorMin=hrt.anchorMax=new Vector2(.5f,.5f);
        hrt.pivot=new Vector2(.5f,.5f);
        hrt.sizeDelta=new Vector2(GraphView.PortHitW,GraphView.PortHitW);
        var hi=hit.GetComponent<Image>();
        hi.sprite=GraphView.White; hi.type=Image.Type.Simple;
        hi.color=new Color(1f,1f,1f,0f); hi.raycastTarget=true;
        p.HitRect=hrt;
        return p;
    }
    // 热区按端口朝向定形。两个方向都让热区「顺着卡片边宽、朝卡片外偏」：
    // 竖排时端口沿横轴排开（列距 30），横向给 28；横排时端口沿纵轴排开（行距 18），
    // 纵向必须收到行距以内，否则会把上下相邻端口的点击抢走。
    // 垂直于卡片边那一侧整体朝外挪 6px：往外才是玩家真正点不准的地方，
    // 往卡片里只探几个像素，免得盖住卡片本体的拖动和双击编辑。
    // 上面这些尺寸都是「卡片坐标」里的，镜头一缩屏幕上就跟着缩，所以每次排形都按当前 zoom 反补
    // （#99）；朝向一变（上下↔左右）也要重新排一次，故把 vertical/output 记在端口上。
    bool verticalLayout;
    Vector2 basePos;      // 端口压在卡片边线上的原始位置，放大后的外挪量在它基础上加
    public void SetHitLayout(bool vertical, bool output)
    {
        verticalLayout = vertical;
        if (Rect != null) basePos = Rect.anchoredPosition;
        LayoutForZoom(Owner != null ? Owner.Zoom : 1f, output);
    }
    // 镜头缩放变了（含适应、居中、滚轮）：方块与热区按新的 zoom 重新反补一次
    public void ApplyZoom(float zoom) { if (HitRect != null) LayoutForZoom(zoom, Output); }
    void LayoutForZoom(float zoom, bool output)
    {
        if (Rect == null) return;
        float dot = GraphView.PortSize * GraphView.PortDotScale(verticalLayout, zoom);
        float shift = GraphView.PortDotShift(verticalLayout, zoom);
        Rect.sizeDelta = new Vector2(dot, dot);
        Rect.anchoredPosition = basePos + (verticalLayout
            ? new Vector2(0f, output ? -shift : shift)
            : new Vector2(output ? shift : -shift, 0f));
        if (HitRect == null) return;
        HitRect.sizeDelta = GraphView.PortHitSize(verticalLayout, zoom);
        float outwards = GraphView.PortHitOutward(zoom);
        HitRect.anchoredPosition = verticalLayout
            ? new Vector2(0f, output ? -outwards : outwards)
            : new Vector2(output ? outwards : -outwards, 0f);
    }
    // 拉线中的端口涂成珊瑚色，玩家才看得见自己按住的是哪一头
    public void SetPending(bool on)
    {
        Pending = on;
        if (Img != null) Img.color = on ? Theme.Coral : Theme.Whale;
    }
    public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)Owner.StartLink(this);}
    public void OnPointerUp(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)Owner.HandlePortPointerUp(this,e.position);}
    // 从端口按下后的拖拽属于「拉线」，绝不能冒泡到卡片把节点拖走
    public void OnDrag(PointerEventData e){}
}

public sealed class LineGraphic : MaskableGraphic
{
    public GraphView Owner; Graph graph; Dictionary<string,NodeCard> cards;
    Port ghostFrom; Vector2 ghostTo; bool ghostOn;
    public void SetLinks(Graph g,Dictionary<string,NodeCard> c){graph=g;cards=c;SetVerticesDirty();}
    public void SetGhost(Port from,Vector2 toLocal){ghostFrom=from;ghostTo=toLocal;ghostOn=from!=null;SetVerticesDirty();}
    public void ClearGhost(){if(!ghostOn)return;ghostOn=false;ghostFrom=null;SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (graph == null || cards == null) return;
        foreach (var l in graph.Links)
        {
            Vector2 a, b;
            if (!Endpoints(l, out a, out b)) continue;
            var col = Theme.Whale;
            if (Owner != null && Owner.HoveredLink == l) col = Theme.Coral;   // 悬停中的线换成珊瑚色，提示可删
            Draw(vh, a, b, col);
        }
        // 拉线中的橡皮筋：从待定端口连到鼠标
        if (ghostOn && ghostFrom != null) Draw(vh, PortPosition(ghostFrom), ghostTo, Theme.Coral);
    }

    // 连线两端点（content 局部坐标）。端口缺失、卡片被折叠、数据线（已退出画布）都不画，也不参与命中。
    bool Endpoints(GraphLink l, out Vector2 a, out Vector2 b)
    {
        a = b = Vector2.zero;
        if (l == null || graph == null || cards == null) return false;
        if (l.Kind != LinkKind.Exec) return false;
        NodeCard from, to;
        if (!cards.TryGetValue(l.FromNode, out from) || !cards.TryGetValue(l.ToNode, out to)) return false;
        if (from == null || !from.gameObject.activeSelf || to == null || !to.gameObject.activeSelf) return false;
        var source = FindPort(from, l.FromPort, true);
        var target = FindPort(to, l.ToPort, false);
        if (source == null || target == null) return false;
        a = PortPosition(source);
        b = PortPosition(target);
        return true;
    }

    // 贝塞尔控制点，与 Draw 用同一套，保证命中测试贴着看得见的线
    void Control(Vector2 a, Vector2 b, out Vector2 p0, out Vector2 p1)
    {
        if (Owner != null && Owner.VerticalPorts)
        {
            float dy = Mathf.Abs(b.y - a.y) * .45f;
            p0 = a + new Vector2(0f, -dy);
            p1 = b + new Vector2(0f, dy);
        }
        else
        {
            float dx = Mathf.Abs(b.x - a.x) * .45f;
            p0 = a + new Vector2(dx, 0f);
            p1 = b - new Vector2(dx, 0f);
        }
    }

    static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 p0, Vector2 p1, float t)
    {
        float u = 1f - t;
        return u * u * u * a + 3 * u * u * t * p0 + 3 * u * t * t * p1 + t * t * t * b;
    }

    // 命中测试：content 局部坐标下离折线最近且在 tol 内的连线，给悬停删除用
    public GraphLink HitLink(Vector2 pt, float tol)
    {
        if (graph == null) return null;
        GraphLink best = null;
        float bestD = tol;
        foreach (var l in graph.Links)
        {
            Vector2 a, b;
            if (!Endpoints(l, out a, out b)) continue;
            Vector2 p0, p1;
            Control(a, b, out p0, out p1);
            for (int i = 1; i <= 16; i++)
            {
                float d = Vector2.Distance(pt, Bezier(a, b, p0, p1, i / 16f));
                if (d < bestD) { bestD = d; best = l; }
            }
        }
        return best;
    }

    public bool LinkMid(GraphLink l, out Vector2 mid)
    {
        Vector2 a, b;
        mid = Vector2.zero;
        if (!Endpoints(l, out a, out b)) return false;
        Vector2 p0, p1;
        Control(a, b, out p0, out p1);
        mid = Bezier(a, b, p0, p1, .5f);
        return true;
    }

    static Port FindPort(NodeCard card, string id, bool output)
    {
        foreach (var port in card.Ports)
            if (port.PortId == id && port.Output == output) return port;
        return null;
    }

    // 端点取端口节点自身的世界位置，转换到本图层（= content）局部坐标。
    // 端口中心正好压在卡片边缘线上，这里再把端点收到端口方块的外侧面，
    // 于是连线永远停在卡片边线上，不会出现"线钻进卡片内部"的观感。
    Vector2 PortPosition(Port port)
    {
        var world = port.transform.TransformPoint(Vector3.zero);
        var p = ((RectTransform)transform).InverseTransformPoint(world);
        float half = GraphView.PortSize * 0.5f;
        if (Owner != null && Owner.VerticalPorts) p.y += port.Output ? -half : half;
        else p.x += port.Output ? half : -half;
        return p;
    }

    // 贝塞尔控制点顺着端口朝向：上下布局竖直出笔，左右布局水平出笔，线头永远垂直离开卡片边缘。
    void Draw(VertexHelper vh, Vector2 a, Vector2 b, Color col)
    {
        Vector2 p0, p1;
        Control(a, b, out p0, out p1);
        Vector2 prev = a;
        for (int i = 1; i <= 16; i++)
        {
            var p = Bezier(a, b, p0, p1, i / 16f);
            AddSeg(vh, prev, p, col, 2f);
            prev = p;
        }
    }
    void AddSeg(VertexHelper vh,Vector2 a,Vector2 b,Color c,float w){Vector2 d=(b-a).normalized;Vector2 n=new Vector2(-d.y,d.x)*w*.5f;int i=vh.currentVertCount;vh.AddVert(a-n,c,Vector2.zero);vh.AddVert(a+n,c,Vector2.zero);vh.AddVert(b+n,c,Vector2.zero);vh.AddVert(b-n,c,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i+2,i+3,i);}
}
