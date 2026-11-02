using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Dayu.Core;

// 大肥鱼の算法大冒险 · 运行时装配
// 面板一律用 Image（1×1 白图着色），文字用 PixelText（位图字）。
// 不依赖 PanelGraphic / GridGraphic，避免自定义 Graphic 的绘制差异。
public sealed class App : MonoBehaviour
{
    public static App I;

    public RectTransform CanvasHost;   // 画布区（用于放节点）
    public RectTransform OverlayLayer; // 覆盖层（token、提示）
    public GraphView GraphCanvas;
    public RectTransform GuideLayer;   // 首次使用引导层
    public RectTransform TopBarRect, PaletteRect, InspectorRect, TraceRect, FooterRect;

    // 时间/尺寸常量：token 飞行 280ms、事件间隔 150ms、单次最多播放 200 个事件
    // 运行速度：慢速 / 正常 / 快速，由顶栏的速度按钮切换
    public enum RunSpeed { Slow, Normal, Fast }
    RunSpeed runSpeed = RunSpeed.Normal;
    float TokenFlySec { get { return 0.62f * SpeedFactor; } }
    float TokenGapSec { get { return 0.10f * SpeedFactor; } }
    float EventGapSec { get { return 0.15f * SpeedFactor; } }
    float SpeedFactor
    {
        get
        {
            if (runSpeed == RunSpeed.Slow) return 2.2f;
            if (runSpeed == RunSpeed.Fast) return 0.45f;
            return 1f;
        }
    }
    const int MaxPlayEvents = 200;
    // 追踪栏 2 号字行高 28：变量区从栏顶第 34px 起排，4 行就会压到 872 的署名栏，所以总行数收在 3
    const int MaxVarItems = 3;         // 变量区里变量本身最多 3 行
    const int MaxVarLines = 3;         // 变量区总行数（含输入/输出行）
    const int MaxParamLines = 8;       // 检查器里参数最多 8 行
    const float MsgX = 210f, MsgW = 880f, VarX = 1104f, VarW = 326f;
    public const float HeaderLevelW = 620f;   // 顶栏「章节 · 关卡名」可用的最大宽度；右边按钮排从 x=1030 起，留得住（自测按这个值逐关盯截断）
    public const float InspectorW = 330f;

    PixelText headerLevel;             // 顶栏第二段：章节 · 关卡名
    PixelText traceStep, traceMessage, traceVars;

    Level level;
    RunResult lastRun;
    TraceEvent currentEvent;
    string lastInput = "";
    readonly List<TraceEvent> events = new List<TraceEvent>();
    readonly List<GameObject> tokenObjects = new List<GameObject>();
    readonly HashSet<string> breakpointUids = new HashSet<string>();
    GraphNode selectedNode;
    // 提示默认折着：进一个关卡只看到一句题干，「看提示」点开才摊开目标和做法
    bool tipsOpen;
    int eventCursor, playTotal;
    Coroutine runRoutine;
    Coroutine stepRoutine;
    Coroutine submitRoutine;
    enum RunMode { Normal, Step, Breakpoint, Trace }
    RunMode runMode = RunMode.Normal;
    bool breakpointGuideShown;
    bool traceGuideShown;
    bool linkHintShown;
    bool compileDirty;
    string liveError;   // 最近一次即时编译的报错，空表示当前这张图能编译通过
    // 存档：本关的画布图自动落盘，进度元信息（最后修改时间 / 通关 / 提交次数）另存一份 meta.json
    readonly Dictionary<string, SaveFlow.Meta> progress = new Dictionary<string, SaveFlow.Meta>(StringComparer.Ordinal);
    bool batchSolution;          // -solution 批跑：只把答案铺上画布截图，绝不写玩家的档
    bool previewAnswer;          // 正在看标准答案：挂起自动存档、禁用提交
    Graph previewReturn;         // 进预览前玩家自己的那份图
    PixelButton btnUndo, btnRedo, btnSubmit;
    GameObject operatorModal;
    GraphNode operatorNode;
    string editVarA, editVarB, editOp;
    PixelText editFormula, editSelA, editSelB;
    readonly List<PixelButton> editABtns = new List<PixelButton>();
    readonly List<PixelButton> editBBtns = new List<PixelButton>();
    readonly List<PixelButton> editOpBtns = new List<PixelButton>();

    const int W = 1440, H = 900;
    const int DefaultWindowW = 1600, DefaultWindowH = 1000;
    const int WindowMargin = 80;

    static Sprite _white;

    static Sprite White
    {
        get
        {
            if (_white != null) return _white;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0, 0), 1f);
            return _white;
        }
    }

    void Awake()
    {
        I = this;

        ApplyDisplayMode();

        LoadAtlas();
        EnsureEventSystem();

        var canvas = CreateCanvas();
        var root = Child("Root", canvas.transform);
        Stretch(root);
        uiRoot = root;

        // 画布先建、四周边栏后建：UGUI 按兄弟顺序绘制，后建的盖在前面。
        // 平移画布时卡片滑到左栏下面，就该被左栏盖住，而不是糊在栏上面。
        // 六块面板各自贴住屏幕边（响应式锚点）：窗口比例变了只拉伸画布，
        // 右边不露黑边、底栏不被裁掉；1440x900 下与旧固定坐标完全重合。
        CanvasHost    = Panel(root, "Canvas",    176,  44,  934,  684, Theme.Bg);
        TopBarRect    = Panel(root, "TopBar",    0,    0,   1440, 44,  Theme.Paper);
        PaletteRect   = Panel(root, "Palette",   0,    44,  176,  684, Theme.Paper);
        InspectorRect = Panel(root, "Inspector", 1110, 44,  330,  684, Theme.Paper);
        TraceRect     = Panel(root, "Trace",     0,    728, 1440, 144, Theme.Paper);
        FooterRect    = Panel(root, "Footer",    0,    872, 1440, 28,  Theme.Paper);
        Edge(TopBarRect,    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -44f),   Vector2.zero);
        Edge(PaletteRect,   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 172f),   new Vector2(176f, -44f));
        Edge(InspectorRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-330f, 172f), new Vector2(0f, -44f));
        Edge(TraceRect,     new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 28f),    new Vector2(0f, 172f));
        Edge(FooterRect,    new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero,            new Vector2(0f, 28f));
        Edge(CanvasHost,    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(176f, 172f), new Vector2(-330f, -44f));
        SetBlocking(TopBarRect); SetBlocking(PaletteRect); SetBlocking(InspectorRect);
        SetBlocking(TraceRect); SetBlocking(FooterRect);

        // 画布点阵：24px 一个淡点、120px 一个亮点，给平移一个参照物，也免得整片底色发空
        var grid = Child("Grid", CanvasHost);
        Stretch(grid);
        var gridG = grid.gameObject.AddComponent<GridGraphic>();
        gridG.raycastTarget = false;

        // 周边栏与画布之间压一条 2px 沙色分隔，面板和画布才分得开
        Divider(root, "DivTop",    0,    42,  1440, 2);
        Divider(root, "DivLeft",   174,  44,  2,   684);
        Divider(root, "DivRight",  1110, 44,  2,   684);
        Divider(root, "DivBottom", 0,    726, 1440, 2);
        Edge(FindRect(root, "DivTop"),    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -44f),   new Vector2(0f, -42f));
        Edge(FindRect(root, "DivLeft"),   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(174f, 172f), new Vector2(176f, -44f));
        Edge(FindRect(root, "DivRight"),  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-330f, 172f), new Vector2(-328f, -44f));
        Edge(FindRect(root, "DivBottom"), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 170f),   new Vector2(0f, 172f));

        // 题栏左边缘中间的「书签」拉手：凸出栏外 26px，按一下收起题栏、再按一下展开。
        // 收起时整栏往右滑出屏幕、画布跟着长满，只剩这个拉手贴住屏幕右沿。
        BuildInspectorTab(root);

        OverlayLayer = Child("Overlay", CanvasHost);
        Stretch(OverlayLayer);
        GraphCanvas = GraphView.Create(CanvasHost);
        // 截图验收要的是「已经落定」的画面：相机补间和卡片淡入在 -shot 下直接跳到终态
        GraphCanvas.InstantView = HasArg("-shot");
        // 覆盖层放到画布之后绘制，token / 提示才不会被节点卡片压住（它本身不接收射线）
        OverlayLayer.SetAsLastSibling();

        // 画布底部操作提示：贴画布下沿居中，窗口拉高拉矮都跟着底边走，
        // 再用固定 y 会悬在半空压住卡片（1280x1024 那种偏方比例下实测会）
        var hint = Label(CanvasHost, "单击选中 · 双击进表达式 · 拖空白框选 · Delete 删除 · Ctrl+滚轮缩放", 10, 648, Theme.WhaleDeep, 2);
        hint.gameObject.name = "CanvasHint";
        hint.raycastTarget = false;
        var hintRt = hint.rectTransform;
        hintRt.anchorMin = hintRt.anchorMax = new Vector2(.5f, 0f);
        hintRt.pivot = new Vector2(.5f, 0f);
        hintRt.anchoredPosition = new Vector2(0f, 16f);   // 16 = 旧固定坐标在 1440x900 下离底的距离

        GuideLayer = Child("GuideLayer", root);
        Stretch(GuideLayer);
        var guideImage = GuideLayer.gameObject.AddComponent<Image>();
        guideImage.color = new Color(0, 0, 0, 0);
        guideImage.raycastTarget = false;

        BuildTopBar();
        BuildPalette();
        BuildInspector();
        BuildTrace();
        BuildFooter();
        BuildMenuLayer();

        GraphCanvas.NodeSelected += OnNodeSelected;
        GraphCanvas.NodeDeleteRequested += OnNodeDeleteRequested;
        GraphCanvas.NodeEditRequested += OnNodeEditRequested;
        GraphCanvas.NodeParamsRequested += n => OpenNodeParams(n);
        GraphCanvas.NodeParamsFocusRequested += (n, k) => OpenNodeParams(n, k);
        GraphCanvas.NodeBreakpointToggled += OnBreakpointToggled;
        GraphCanvas.LinkCreated += OnLinkCreated;
        GraphCanvas.LinkHintRequested += OnLinkHintRequested;
        // 卡片因参数改动被原地重建，断点小圆点会丢，按集合补回来
        GraphCanvas.CardRebuilt += uid =>
        {
            if (GraphCanvas != null && breakpointUids.Contains(uid)) GraphCanvas.SetBreakpointDot(uid, true);
        };
        // 图一改就标脏，下一帧重编译：状态栏不会再挂着上一条已经过时的报错
        GraphCanvas.GraphChanged += MarkCompileDirty;
        // 同一个挂点上再挂自动存档：Request 只克隆一份快照，不回叫画布，不会二次触发 GraphChanged
        GraphCanvas.GraphChanged += QueueAutoSave;

        batchSolution = HasArg("-solution");
        // 存档失败既进日志也进状态栏：玩家不看 Editor 日志，静默丢档最没人认账
        SaveSystem.OnError = msg =>
        {
            Debug.Log("[save] " + msg);
            if (!string.IsNullOrEmpty(msg)) SetMessage("存档没写进去：" + msg);
        };
        string metaErr;
        foreach (var kv in SaveFlow.ReadMeta(SaveSystem.Root, out metaErr)) progress[kv.Key] = kv.Value;
        // 通关标记以 PlayerPrefs 为准，meta 里那份只是给关卡列表看的副本，进来先对齐一次
        foreach (var l in Levels.All) SaveFlow.SyncCleared(progress, l.Id, IsCleared(l.Id));
        if (!string.IsNullOrEmpty(metaErr)) Debug.Log("[save] " + metaErr);
        // 关窗口、退出播放模式都要把去抖里没落盘的改动写完
        Application.quitting += () => { SaveSystem.Flush(); SaveProgressMeta(); };

        string startLevel = ArgValue("-level");
        LoadLevel(string.IsNullOrEmpty(startLevel) ? "L1" : startLevel);
        string selectUid = ArgValue("-select");
        if (!string.IsNullOrEmpty(selectUid) && GraphCanvas != null)
        {
            var card = GraphCanvas.FindCard(selectUid);
            if (card != null) GraphCanvas.NotifySelected(card);
        }
        if (HasArg("-submit")) { StartCoroutine(SubmitDemo()); }
        if (HasArg("-opedit") && GraphCanvas != null)
        {
            var op = GraphCanvas.CurrentGraph.Nodes.Find(n => n != null && n.Type == "binop");
            if (op != null) OpenOperatorEditor(op);
        }
        if (HasArg("-exprnodes")) ApplyExprNodesDemo();
        // -paramsdemo：弹出第一个有参数的模块（优先 vardecl）的悬浮窗，验收悬浮窗字号与排版
        if (HasArg("-paramsdemo")) ParamsDemo();
        // -copydemo：选中两个模块 → Ctrl+C/Ctrl+V 那条路，验收复制粘贴带不带连线
        if (HasArg("-copydemo")) CopyPasteDemo();
        // -palettefold：折叠掉一个一级目录，验收多级目录的开合
        if (HasArg("-palettefold")) PaletteFoldDemo();
        StartCoroutine(Repaint());

        if (HasArg("-demo")) StartCoroutine(DemoRoutine());
        if (HasArg("-shot")) StartCoroutine(ShotRoutine());
        if (HasArg("-exprfield") || HasArg("-chipdrag")) BuildExprFieldDemo(root, HasArg("-chipdrag"));
        if (HasArg("-exprcard")) StartCoroutine(ExprCardDemo());
        // -mathdemo：计数循环卡面的三行文字 + 浮窗顶部页签 + 元素栏「数学函数」芯片组，一次截全
        if (HasArg("-mathdemo")) StartCoroutine(MathDemo());
        // -cardrect：把每张卡片中心的归一化屏幕坐标打进日志，真实鼠标双击照着它落点
        if (HasArg("-cardrect")) StartCoroutine(CardRectDemo());
        // 第十三轮验收：本机鼠标被别的程序占住时，这四条用真 handler + PrintWindow 出证
        if (HasArg("-insecollapse")) StartCoroutine(InspectorCollapseDemo());
        if (HasArg("-insewheel")) StartCoroutine(InspectorWheelDemo());
        if (HasArg("-dropghost")) StartCoroutine(DropGhostDemo());
        if (HasArg("-pushexpr")) StartCoroutine(PushExprDemo());
        if (HasArg("-complete")) StartCoroutine(CompletionDemo());
        if (HasArg("-rename")) StartCoroutine(RenameCheckDemo());
        if (HasArg("-linkdrag")) StartCoroutine(LinkDragDemo());
        if (HasArg("-stepdemo")) StartCoroutine(StepDemo());
        if (HasArg("-tracedemo")) StartCoroutine(TraceDemo());
        // -arrayviz：跳到 L2 数组已填了几格的那一步，验收数组是虚线悬浮看板
        if (HasArg("-arrayviz")) StartCoroutine(ArrayVizDemo());
        // -stackviz：跳到带栈的那一关，把事件流停在「刚压进一个元素」的那一步，镜头对准「创建结构」卡片
        if (HasArg("-stackviz")) StartCoroutine(StackVizDemo());
        // -queueviz：跳到队列关，停在「刚排进一节车厢」那一步，验收队列看板
        if (HasArg("-queueviz")) StartCoroutine(QueueVizDemo());
        // -boarddrag：接着 -arrayviz，用真实射线 + 真实事件冒泡把看板拖走、拉长，验收玩家可调位置大小
        if (HasArg("-boarddrag")) StartCoroutine(BoardDragDemo());
        // -tipsopen：进关后自己点开「看提示」，截图验收折叠块的展开态
        if (HasArg("-tipsopen")) StartCoroutine(TipsOpenDemo());
        // -foldloop N：把第 N 个循环节点折叠成缩略图小块（默认第 1 个），验收折叠 / 缩略图
        if (HasArg("-foldloop")) StartCoroutine(FoldLoopDemo());
        // -layout：切一次端口朝向（上下 → 左右），验收「布局」按钮
        if (HasArg("-layout")) TogglePortLayout();
        // -marquee：脚本化一次空白处左键框选 + 整组挪动，验收多选拖动
        if (HasArg("-marquee")) StartCoroutine(MarqueeDemo());
        // -dropdemo：把一块新「输出」丢到第一张卡的下半身，验收「落进已有模块范围就自动接线」
        if (HasArg("-dropdemo")) StartCoroutine(DropDemo());
        // -skingrid：每种结构各摆一块，验收整卡即结构的皮肤
        if (HasArg("-skingrid")) StartCoroutine(SkinGridDemo());
        if (HasArg("-levelselect")) ShowLevelSelect();
        else if (HasArg("-help")) ShowHelp();
        else if (HasArg("-menu")) ShowMainMenu();
        // 不带 -level 就是从游戏入口进来：先落在主菜单，别直接砸进关卡
        else if (string.IsNullOrEmpty(startLevel) && !HasArg("-shot")) ShowMainMenu();
    }

    // -marquee：脚本化一次「空白处按住左键拖动」的框选，再把选中的一组整体挪动，
    // 用日志证明「框选命中数」和「整组平移」两件事，配 -level L1 -shot 截图验收。
    // -dropdemo：从模块栏「丢」一块新输出到最后一张卡的下半身，验收落点自动连线（#57）
    IEnumerator DropDemo()
    {
        yield return new WaitForSeconds(.8f);
        if (GraphCanvas == null) yield break;
        var g = GraphCanvas.CurrentGraph;
        if (g == null || g.Nodes == null || g.Nodes.Count == 0) yield break;
        GraphNode host = null;
        foreach (var n in g.Nodes)
        {
            if (n == null || GraphCanvas.FindCard(n.Uid) == null) continue;
            var t = NodeTypes.Get(n.Type);
            if (t == null || t.ExecOut.Count == 0) continue;   // 样例板一类没有执行出口的卡接不了线
            bool busy = false;
            foreach (var e in t.ExecOut)
                foreach (var l in g.Links)
                    if (l != null && l.Kind == LinkKind.Exec && l.FromNode == n.Uid && l.FromPort == e.Id) busy = true;
            if (busy) continue;                                // 出口全被占了，接不上新线
            host = n;
        }
        if (host == null) yield break;
        var card = GraphCanvas.FindCard(host.Uid);
        if (card == null) yield break;
        int before = g.Links.Count;
        // card.Rect.localPosition 就是 content 局部坐标，ScreenFromContent 要的正是这个空间
        var drop = card.Rect.localPosition + new Vector3(card.Rect.sizeDelta.x * .5f, -card.Rect.sizeDelta.y * .75f, 0f);
        GraphCanvas.AddPaletteNode("output", GraphCanvas.ScreenFromContent(drop));
        yield return null;
        GraphCanvas.FitToContent();   // 新卡接在链尾，机位收一下让两张卡和那条新线都进画面
        Debug.Log("[demo] -dropdemo 丢到 " + host.Uid + "(" + host.Type + ") 下半身，连线 " + before + " → " + g.Links.Count);
    }

    // -skingrid：把每种结构各摆一块在画布上，验收「整卡即结构」的皮肤、招牌与名牌
    IEnumerator SkinGridDemo()
    {
        yield return new WaitForSeconds(.6f);
        if (GraphCanvas == null) yield break;
        string[][] specs =
        {
            new[] { "entry" },
            new[] { "input", "name", "n" },
            new[] { "vardecl", "name", "x", "vtype", "int", "initExpr", "0" },
            new[] { "vardecl", "name", "a", "vtype", "int[]", "size", "5" },
            new[] { "varset", "name", "x", "valueExpr", "x + 1" },
            new[] { "varget", "name", "x" },
            new[] { "const", "vtype", "int", "value", "7" },
            new[] { "binop", "op", "+" },
            new[] { "compare", "op", "<" },
            new[] { "branch", "condExpr", "x > 0" },
            new[] { "forloop", "fromExpr", "1", "toExpr", "n" },
            new[] { "whileloop", "condExpr", "x > 0" },
            new[] { "output", "outputExpr", "x" },
            // 栈 / 队列这两族的结构种类只有 stack_* / queue_* 才走得到，补上才凑齐十三种轮廓
            new[] { "stack_push", "pushExpr", "x" },
            new[] { "queue_push", "pushExpr", "x" },
            // 形状卡（#106）：只回一个值的小查询模块，字一短就不再是长方形
            new[] { "len_of", "expr", "s" },
            new[] { "stack_empty", "structId", "st" },
            new[] { "stack_size", "structId", "st" },
            new[] { "queue_empty", "structId", "st" },
            new[] { "queue_size", "structId", "st" },
        };
        for (int i = 0; i < specs.Length; i++)
        {
            string type = specs[i][0];
            var preset = new string[specs[i].Length - 1];
            System.Array.Copy(specs[i], 1, preset, 0, preset.Length);
            // 间距按「最高的那块（计数循环带循环体框）」留，190 会让上一行的循环体框压到下一行的招牌上
            float x = 60f + (i % 5) * 232f;
            float y = 60f + (i / 5) * 232f;
            GraphCanvas.AddPaletteNode(type, GraphCanvas.ScreenFromContent(new Vector2(x, y)), preset);
        }
        yield return null;
        // 整幅适应：按内容包围盒居中，13 块结构一屏看全且不会互相压框
        // （之前居中到某一块，第一行被顶出画面，验收截图看不全）
        GraphCanvas.FitToContent();
        Debug.Log("[demo] -skingrid 摆了 " + specs.Length + " 种结构");
    }

    IEnumerator MarqueeDemo()
    {
        yield return new WaitForSeconds(.6f);
        if (GraphCanvas == null || GraphCanvas.CurrentGraph == null) yield break;
        if (!GraphCanvas.ContentBounds(out var box)) { Debug.Log("[marquee] 画布上没有模块"); yield break; }

        var g = GraphCanvas.CurrentGraph;
        var beforePos = new Dictionary<string, Vector2>();
        foreach (var n in g.Nodes) if (n != null) beforePos[n.Uid] = new Vector2(n.X, n.Y);
        int before = GraphCanvas.SelectedCount();

        // 两个角都取在包围盒外侧，保证每一块都被矩形盖住
        var from = GraphCanvas.ScreenFromContent(new Vector2(box.xMin - 40f, box.yMax + 40f));
        var to = GraphCanvas.ScreenFromContent(new Vector2(box.xMax + 40f, box.yMin - 40f));

        // 走真实射线：让 GraphicRaycaster 决定命中谁，再把事件投递给对应的处理者。
        // 上一版直接写死 pointerPress = 画布，等于替画布把「点在空白处」的条件凑齐了，是假阳性。
        var ped = new PointerEventData(EventSystem.current) { position = from };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, hits);
        if (hits.Count == 0) { Debug.Log("[marquee] 起点没有命中任何图元，框选无从谈起"); yield break; }
        var chain = new System.Text.StringBuilder();
        for (int i = 0; i < hits.Count && i < 4; i++) chain.Append(hits[i].gameObject.name).Append(i + 1 < hits.Count && i + 1 < 4 ? " < " : "");
        var target = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject);
        Debug.Log("[marquee] 起点命中链 " + chain + " → 处理者 " + (target != null ? target.name : "无"));
        ped.pointerEnter = ped.pointerPress = target;
        ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerDownHandler);
        for (int i = 1; i <= 6; i++)
        {
            ped.position = Vector2.Lerp(from, to, i / 6f);
            ExecuteEvents.Execute(target, ped, ExecuteEvents.dragHandler);
            yield return new WaitForSeconds(.05f);
        }
        ExecuteEvents.Execute(target, ped, ExecuteEvents.pointerUpHandler);
        int after = GraphCanvas.SelectedCount();
        Debug.Log("[marquee] 框选前选中 " + before + " 块，框选后 " + after + " 块，包围盒 " + box);

        // 整组挪动：从选中的某张卡片起手，往右下拖 (120, 60)，所有选中卡片应一起平移
        var card = GraphCanvas.FirstSelectedCard();
        if (card == null || card.Node == null) { Debug.Log("[marquee] 没有选中卡片，跳过整组拖动"); yield break; }
        var grab = GraphCanvas.ScreenFromContent(card.Rect.localPosition + new Vector3(60f, -10f, 0f));
        ped.position = grab;
        GraphCanvas.BeginNodeDrag(card, ped);
        ped.position = GraphCanvas.ScreenFromContent(card.Rect.localPosition + new Vector3(180f, -70f, 0f));
        GraphCanvas.DragNode(card, ped);

        int moved = 0; string sample = "";
        foreach (var n in g.Nodes)
        {
            if (n == null || !beforePos.ContainsKey(n.Uid)) continue;
            var p0 = beforePos[n.Uid];
            if (Mathf.Abs(p0.x - n.X) < .5f && Mathf.Abs(p0.y - n.Y) < .5f) continue;
            moved++;
            if (sample.Length == 0) sample = n.Uid + " (" + p0.x + "," + p0.y + ") → (" + n.X + "," + n.Y + ")";
        }
        Debug.Log("[marquee] 整组拖动：位移 " + moved + " / " + after + " 块，示例 " + sample);
        yield return new WaitForSeconds(.2f);
    }

    // -foldloop [N]：把第 N 个循环节点的循环体折叠起来（默认第 1 个），验收折叠按钮 + 缩略图小块
    IEnumerator FoldLoopDemo()
    {
        yield return new WaitForSeconds(.6f);
        if (GraphCanvas == null) yield break;
        int n = 1;
        int.TryParse(ArgValue("-foldloop"), out n);
        if (n < 1) n = 1;
        var g = GraphCanvas.CurrentGraph;
        if (g == null) yield break;
        GraphNode loop = null;
        int seen = 0;
        foreach (var nd in g.Nodes)
        {
            if (nd == null || (nd.Type != "forloop" && nd.Type != "whileloop")) continue;
            if (++seen == n) { loop = nd; break; }
        }
        if (loop == null) { Debug.Log("[fold] 当前关卡没有第 " + n + " 个循环节点，配 -level L2 使用"); yield break; }
        GraphCanvas.ToggleLoopFold(loop.Uid);
        Debug.Log("[fold] 折叠 " + loop.Uid + " → " + GraphCanvas.IsLoopFolded(loop.Uid));
        yield return new WaitForSeconds(.4f);
    }

    // -linkdrag：把一条已存在的执行连线删掉，再用脚本化指针事件重新连上，验收「拖端口连线」整条链路
    IEnumerator LinkDragDemo()
    {
        yield return new WaitForSeconds(.6f);
        if (GraphCanvas == null) yield break;
        var g = GraphCanvas.CurrentGraph;
        if (g == null || g.Links == null) yield break;

        GraphLink target = null;
        foreach (var l in g.Links)
        {
            if (l.Kind != LinkKind.Exec) continue;
            if (GraphCanvas.FindPort(l.FromNode, l.FromPort, true) == null) continue;
            if (GraphCanvas.FindPort(l.ToNode, l.ToPort, false) == null) continue;
            target = l;
            break;
        }
        if (target == null) { Debug.Log("[linkdrag] 当前关卡没有可断开的执行连线，配 -level L1 使用"); yield break; }

        int before = g.Links.Count;
        g.Links.Remove(target);
        GraphCanvas.RefreshLines();
        Debug.Log("[linkdrag] 已断开 " + target.FromNode + "." + target.FromPort + " → " + target.ToNode + "." + target.ToPort + "，连线 " + before + " → " + g.Links.Count);
        yield return new WaitForSeconds(.5f);

        if (!GraphCanvas.DemoPressPort(target.FromNode, target.FromPort, true)) { Debug.Log("[linkdrag] 起点端口按不下去"); yield break; }
        Debug.Log("[linkdrag] 已按下输出端口，橡皮筋应从端口跟出");
        yield return new WaitForSeconds(1f);

        GraphCanvas.DemoDragPortTo(target.ToNode, target.ToPort, false);
        Debug.Log("[linkdrag] 已拖到输入端口上方，落点端口应高亮");
        yield return new WaitForSeconds(1f);

        GraphCanvas.DemoReleasePort();
        yield return new WaitForSeconds(.4f);
        bool restored = g.Links.Exists(l => l.FromNode == target.FromNode && l.FromPort == target.FromPort
            && l.ToNode == target.ToNode && l.ToPort == target.ToPort);
        Debug.Log("[linkdrag] 松手后连线数 " + g.Links.Count + "，重连" + (restored ? "成功" : "失败"));
    }

    // -exprfield：内嵌表达式组件演示（独立于主流程，只用于验收组件外观）
    // -chipdrag：在同一个演示小窗里脚本化拖元素芯片，验收跟手幽灵、落点竖线和落点插入
    void BuildExprFieldDemo(RectTransform root, bool chipDrag)
    {
        var holder = Child("ExprFieldDemo", root);
        Anchor(holder, 460, 280, 520, 170);
        var bg = holder.gameObject.AddComponent<Image>();
        bg.sprite = White; bg.type = Image.Type.Simple; bg.color = Theme.Paper;
        var edge = holder.gameObject.AddComponent<Outline>();
        edge.effectColor = Theme.Border; edge.effectDistance = new Vector2(1, -1);

        Label(holder, "表达式字段演示", 16, 14, Theme.Ink, 1).raycastTarget = false;
        Label(holder, chipDrag ? "小窗内按住元素芯片拖到落点" : "点字段打开小窗 · 左侧元素点击或拖拽插入",
            16, 32, Theme.Border, 1).raycastTarget = false;

        var vars = new List<ExprVariableInfo>
        {
            new ExprVariableInfo("a", "int"),
            new ExprVariableInfo("b", "int"),
            new ExprVariableInfo("sum", "int"),
            new ExprVariableInfo("flag", "bool"),
            new ExprVariableInfo("arr", "int[]", false, "length", "empty")
        };
        if (chipDrag)
        {
            // 拖拽验收顺带把元素栏撑出滚动：分组换行和滚动条都要在图里看得见
            string[] more = { "cnt", "idx", "total", "maxv", "minv", "tmp1", "tmp2", "head", "tail", "ans" };
            foreach (string m in more) vars.Add(new ExprVariableInfo(m, "int"));
            vars.Add(new ExprVariableInfo("text", "string[]", false, "length", "empty"));
        }
        var field = ExpressionFieldUI.Create(holder, chipDrag ? "" : "arr.length + a", vars, s => "", s => Debug.Log("[exprfield] commit " + s), 320);
        field.Rect.anchoredPosition = new Vector2(16, -62);
        field.Context = "运算块 · 左侧表达式";

        Label(holder, "Enter 提交 · Esc 取消 · 自绘光标", 16, 96, Theme.Border, 1).raycastTarget = false;

        if (chipDrag) StartCoroutine(ChipDragDemo(field));
        else StartCoroutine(OpenExprFieldDemo(field));
    }

    // 按下即松手算点击，拖出阈值算拖拽：先点 sum，再把 arr[] 拖到末尾、把 a 拖进它的方括号里
    IEnumerator ChipDragDemo(ExpressionFieldUI field)
    {
        yield return new WaitForSeconds(.4f);
        if (field == null) yield break;
        field.OpenPopover();
        var pop = ExpressionPopoverUI.Current;
        if (pop == null) { Debug.Log("[chipdrag] 小窗没打开"); yield break; }

        yield return new WaitForSeconds(.5f);
        pop.DemoScrollPalette(-8f);
        Debug.Log("[chipdrag] 元素栏滚到底，验收分组换行与滚动条");
        yield return new WaitForSeconds(1f);
        pop.DemoScrollPalette(8f);             // 滚回顶部，让 arr[] 芯片重新可见

        if (!pop.DemoClickChip("sum")) { Debug.Log("[chipdrag] 找不到芯片 sum"); yield break; }
        Debug.Log("[chipdrag] 按下后只挪 2px 松手，点击插入 → draft=" + pop.DemoDraft);
        yield return new WaitForSeconds(.8f);

        if (!pop.DemoPressChip("arr[]")) { Debug.Log("[chipdrag] 找不到芯片 arr[]"); yield break; }
        pop.DemoDragChipTo(pop.DemoDraft.Length);
        Debug.Log("[chipdrag] 真实拖动 arr[] 中，幽灵跟手、落点竖线在表达式末尾");
        yield return new WaitForSeconds(1.4f);
        pop.DemoReleaseChip(pop.DemoDraft.Length);
        Debug.Log("[chipdrag] 松手后 draft=" + pop.DemoDraft + "  光标应停在方括号内");
        yield return new WaitForSeconds(.6f);

        int slot = pop.DemoDraft.Length - 1;
        if (!pop.DemoPressChip("a")) { Debug.Log("[chipdrag] 找不到芯片 a"); yield break; }
        pop.DemoDragChipTo(slot);
        Debug.Log("[chipdrag] 真实拖动 a 中，落点在方括号内");
        yield return new WaitForSeconds(1.4f);
        pop.DemoReleaseChip(slot);
        Debug.Log("[chipdrag] 松手后 draft=" + pop.DemoDraft);
    }

    // -cardrect：卡片之外还要三处落点，真实鼠标照着坐标走
    IEnumerator CardRectDemo()
    {
        yield return new WaitForSeconds(.8f);
        if (GraphCanvas == null) { Debug.Log("[rect] 还没有画布"); yield break; }
        Debug.Log(GraphCanvas.CardRectLog().TrimEnd());
        // 卡片之外还有三处要点：题栏拉手（#95 折叠）、题栏视口（#96 滚轮）、模块栏条目（#103 拖新模块）
        yield return new WaitForEndOfFrame();
        Debug.Log(GraphCanvas.PortRectLog().TrimEnd());
        RectLog("题栏拉手", InspectorTabRect);
        RectLog("题栏视口", inspectorView);
        if (paletteContent != null)
            for (int i = 0; i < PaletteRowDemoKeys.GetLength(0); i++)
                RectLog("模块栏 " + PaletteRowDemoKeys[i, 1],
                        FindRect(paletteContent, "Row_" + PaletteRowDemoKeys[i, 0]));
    }

    // 模块栏里挑几条验收要拖的条目：名字写死在这里，改 -level 也不用改场景文件。
    // 只挑一级目录里默认展开的：「容器」下的方法要点了才建条目，坐标日志会拿不到。
    static readonly string[,] PaletteRowDemoKeys =
    {
        { "input", "输入" },
        { "output", "输出" },
        { "vardecl", "创建变量" },
        { "varset", "赋值" },
        { "forloop", "计数循环" },
    };

    // F9 顺带做一次真·射线命中：坐标对得上却收不到事件时，光看 [rect] 分不清
    // 「点歪了」还是「上面盖了一层没人接射线的东西」。RaycastAll 按从最上层往下排，
    // 谁挡在第一个一眼看见。
    void HitProbe(string label, Vector2 screen)
    {
        if (EventSystem.current == null) { Debug.Log("[hit] " + label + " 还没有 EventSystem"); return; }
        var ped = new PointerEventData(EventSystem.current) { position = screen };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, hits);
        var sb = new System.Text.StringBuilder();
        sb.Append("[hit] ").Append(label).Append(" 屏幕=(").Append(screen.x.ToString("F0")).Append(",").Append(screen.y.ToString("F0"))
          .Append(") 命中 ").Append(hits.Count).Append(" 层：");
        for (int i = 0; i < hits.Count && i < 6; i++)
        {
            if (i > 0) sb.Append(" / ");
            sb.Append(hits[i].gameObject.name);
            if (!hits[i].isValid) sb.Append("(无效)");
        }
        Debug.Log(sb.ToString());
    }

    void HitRect(string label, RectTransform rt)
    {
        if (rt == null) { Debug.Log("[hit] " + label + " 还没建起来"); return; }
        var sp = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        HitProbe(label, sp);
    }

    static void RectLog(string label, RectTransform rt)
    {
        if (rt == null) { Debug.Log("[rect] " + label + " 还没建起来"); return; }
        var sp = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        Debug.Log("[rect] " + label + " 归一化(左上)=(" +
                  (sp.x / Screen.width).ToString("F4") + ", " +
                  (1f - sp.y / Screen.height).ToString("F4") + ") 尺寸=" +
                  rt.rect.width.ToString("F0") + "x" + rt.rect.height.ToString("F0"));
    }

    // -insecollapse：题栏拉手按两下（展开 → 收起 → 展开）。
    // 走的是 InspectorTabClick 这个真 handler，只是把操作系统那一下点击换成直接派发；
    // 本机鼠标被别的程序占住时（SetCursorPos 全部失败）仍能出这一轮的折叠截图。
    // 每一态都打一行「已稳定」并把题栏实际宽度一起打出来，截图端等这一行再拍 ——
    // 游戏冷启动要几秒，拿固定等待去卡 0.22 秒的补间必然对不上。
    IEnumerator InspectorCollapseDemo()
    {
        yield return new WaitForSeconds(1.2f);
        var tab = InspectorTabRect != null ? InspectorTabRect.GetComponent<InspectorTabClick>() : null;
        if (tab == null) { Debug.Log("[insecollapse] 还没有题栏拉手"); yield break; }
        Debug.Log("[insecollapse] 第 1 击：收起（栏宽 " + InspectorWidthNow + " → 0）");
        tab.OnPointerClick(new PointerEventData(EventSystem.current)
                           { button = PointerEventData.InputButton.Left });
        yield return new WaitForSeconds(.7f);
        Debug.Log("[insecollapse] 收起态已稳定 题栏实宽=" + (InspectorRect != null ? InspectorRect.rect.width.ToString("F0") : "?"));
        yield return new WaitForSeconds(9f);
        Debug.Log("[insecollapse] 第 2 击：展开（栏宽回到 " + InspectorWidthFor(false) + "）");
        tab.OnPointerClick(new PointerEventData(EventSystem.current)
                           { button = PointerEventData.InputButton.Left });
        yield return new WaitForSeconds(.7f);
        Debug.Log("[insecollapse] 展开态已稳定 题栏实宽=" + (InspectorRect != null ? InspectorRect.rect.width.ToString("F0") : "?"));
    }

    // -insewheel：题栏滚轮往下滚三格，滚的就是 PaletteScroll.OnScroll 那条真路径。
    // 滚动前后各打一行「已稳定」，截图端等到行再拍，不用猜游戏冷启动花了几秒。
    IEnumerator InspectorWheelDemo()
    {
        yield return new WaitForSeconds(1.2f);
        if (inspectorScroll == null || inspectorContent == null) { Debug.Log("[insewheel] 题栏还没建起来"); yield break; }
        Debug.Log("[insewheel] 滚动前已稳定 内容 y=" + inspectorContent.anchoredPosition.y.ToString("F0"));
        yield return new WaitForSeconds(1.5f);
        for (int i = 0; i < 3; i++)
        {
            inspectorScroll.OnScroll(new PointerEventData(EventSystem.current)
                                     { scrollDelta = new Vector2(0f, -1f) });
            Debug.Log("[insewheel] 第 " + (i + 1) + " 格后题栏内容 y=" +
                      inspectorContent.anchoredPosition.y.ToString("F0"));
            yield return new WaitForSeconds(.55f);
        }
        Debug.Log("[insewheel] 滚动完成 内容 y=" + inspectorContent.anchoredPosition.y.ToString("F0"));
        // 给「滚动完成」那张截图留出时间，再一把滚到底：判定行「样板 x 条 · 抽查 y 条」在题面最下面
        yield return new WaitForSeconds(2.5f);
        for (int i = 0; i < 40; i++)
            inspectorScroll.OnScroll(new PointerEventData(EventSystem.current)
                                     { scrollDelta = new Vector2(0f, -1f) });
        yield return new WaitForSeconds(.6f);
        Debug.Log("[insewheel] 已滚到底 内容 y=" + inspectorContent.anchoredPosition.y.ToString("F0"));
    }

    // -dropghost：把「新模块拖到旧模块身上、还没松手」那一帧钉住 ——
    // 先铺虚线落点（PreviewDropTarget），留时间截图，再按同一个屏幕点真正落子，看相邻方位与间隔。
    IEnumerator DropGhostDemo()
    {
        yield return new WaitForSeconds(.9f);
        if (GraphCanvas == null) { Debug.Log("[dropghost] 还没有画布"); yield break; }
        var g = GraphCanvas.CurrentGraph;
        if (g == null || g.Nodes == null) yield break;
        // 宿主优先选循环：#103 要看的就是「大框架落子时间隔更大」，循环体框最能说明问题；
        // 没有循环就退而取最大的那张卡，位置每次一致，截图才可核对。
        GraphNode host = null;
        float bestArea = -1f;
        foreach (var n in g.Nodes)
        {
            if (n == null || n.Type.StartsWith("sample", StringComparison.Ordinal)) continue;
            var c = GraphCanvas.FindCard(n.Uid);
            if (c == null) continue;
            bool isLoop = n.Type == "forloop" || n.Type == "whileloop";
            bool hostIsLoop = host != null && (host.Type == "forloop" || host.Type == "whileloop");
            if (host != null && hostIsLoop && !isLoop) continue;
            if (host != null && !hostIsLoop && !isLoop) continue;   // 非循环时只比大小
            float area = c.Rect.rect.width * c.Rect.rect.height;
            if (host == null || isLoop != hostIsLoop || area > bestArea) { host = n; bestArea = area; }
        }
        if (host == null) { Debug.Log("[dropghost] 画布上没有可当宿主的模块"); yield break; }
        var rt = GraphCanvas.FindCard(host.Uid).Rect;
        // 落点故意压在宿主中心偏右下：方位该判成「右下」，新卡挨在宿主右下方并空开一格间隔
        var world = rt.TransformPoint(new Vector3(rt.rect.center.x + rt.rect.width * .25f,
                                                   rt.rect.center.y - rt.rect.height * .25f, 0f));
        var sp = RectTransformUtility.WorldToScreenPoint(null, world);
        GraphCanvas.PreviewDropTarget(sp, "const", new[] { "vtype", "int", "value", "7" });
        Debug.Log("[dropghost] 虚线落点已铺在 " + host.Type + " " + host.Uid + " 身上（截图看虚线框）");
        yield return new WaitForSeconds(9f);
        GraphCanvas.AddPaletteNode("const", sp, new[] { "vtype", "int", "value", "7" });
        GraphCanvas.ClearDropPreview();
        yield return new WaitForSeconds(1.5f);
        Debug.Log("[dropghost] 落子完成，卡片坐标：\n" + GraphCanvas.CardRectLog().TrimEnd());
    }

    // -pushexpr：停在「刚压进一个元素」那一步，再打开压入卡的表达式浮窗，一次截齐 #104 的栈名与压入表达式
    IEnumerator PushExprDemo()
    {
        yield return StartCoroutine(StackVizDemo());
        yield return new WaitForSeconds(.6f);
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        var push = g != null ? g.Nodes.Find(n => n != null && n.Type == "stack_push") : null;
        if (push == null) { Debug.Log("[pushexpr] 这一关没有压入模块"); yield break; }
        GraphCanvas.CenterOnNode(push.Uid, 1f);
        yield return new WaitForSeconds(.5f);
        var card = GraphCanvas.FindCard(push.Uid);
        if (card == null || !card.OpenExprField("pushExpr"))
        {
            Debug.Log("[pushexpr] 打不开压入表达式的浮窗");
            yield break;
        }
        // 浮窗有淡入补间，等它落定再报「已打开」，截图才拍到完整的窗
        yield return new WaitForSeconds(.6f);
        Debug.Log("[pushexpr] 浮窗已打开，卡面容器名读作「" + NodeCard.StructLabel(GraphUtil.Get(push, "structId")) + "」");
    }

    // -complete：打开压入卡的表达式浮窗，替玩家敲到「容器名.」，把自动补全浮窗拍下来
    IEnumerator CompletionDemo()
    {
        yield return new WaitForSeconds(.6f);
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        var push = g != null ? g.Nodes.Find(n => n != null && n.Type == "stack_push") : null;
        if (push == null) { Debug.Log("[complete] 这一关没有压入模块，配 -level S1/S2 的 -solution 用"); yield break; }
        GraphCanvas.CenterOnNode(push.Uid, 1f);
        yield return new WaitForSeconds(.5f);
        var card = GraphCanvas.FindCard(push.Uid);
        if (card == null || !card.OpenExprField("pushExpr")) { Debug.Log("[complete] 打不开压入表达式的浮窗"); yield break; }
        yield return new WaitForSeconds(.6f);
        var pop = ExpressionPopoverUI.Current;
        if (pop == null) { Debug.Log("[complete] 表达式浮窗没起来"); yield break; }
        var id = GraphUtil.Get(push, "structId");
        string half = (id ?? "") + ".";
        pop.DemoSetDraft(half, half.Length);
        yield return new WaitForSeconds(.4f);
        var cands = ExpressionPopoverUI.Candidates(half, half.Length, NodeCard.BuildVariables(g, push.Uid));
        string names = "";
        foreach (var m in cands) names += (names.Length == 0 ? "" : "、") + m.Token + "（" + m.Gloss + "）";
        Debug.Log("[complete] 「" + half + "」候选 " + cands.Count + " 颗：" + names);
    }

    // -rename：容器实例名这条链在真图上走一遍。中文名、数字开头、撞变量名三条都该被拒，
    // 改成合法英文名后全图 structId 跟着换、卡面与看板按新名重建——判定拍不进截图，用日志出证。
    IEnumerator RenameCheckDemo()
    {
        yield return new WaitForSeconds(.8f);
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        if (g == null) { Debug.Log("[rename] 当前没有图"); yield break; }
        var node = g.Nodes.Find(n => n != null && n.Type == "structnew");
        if (node == null) { Debug.Log("[rename] 这一关没有「创建栈」模块，配 -level S1 -solution 用"); yield break; }
        string before = GraphUtil.Get(node, "structId");
        bool cn = StructRename(node, "一摞数");
        bool digit = StructRename(node, "2bad");
        bool taken = StructRename(node, "n");
        Debug.Log("[rename] 三条该被拒的：" + cn + "/" + digit + "/" + taken + "（全 False 才对）");
        if (!StructRename(node, "stk")) { Debug.Log("[rename] 合法英文名没改成功"); yield break; }
        RefreshStructCards();
        string refs = "";
        foreach (var n in g.Nodes)
            if (n != null && n.Params != null && n.Params.ContainsKey("structId"))
                refs += (refs.Length == 0 ? "" : "、") + n.Type + "=" + n.Params["structId"];
        Debug.Log("[rename] " + before + " → stk 卡面「" + NodeCard.StructLabel("stk") + "」 引用：" + refs);
    }

    IEnumerator MathDemo()
    {
        yield return new WaitForSeconds(.6f);
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        if (g == null) { Debug.Log("[demo] -mathdemo 当前没有图"); yield break; }
        var loop = g.Nodes.Find(n => n != null && n.Type == "forloop");
        if (loop == null) { Debug.Log("[demo] -mathdemo 这一关没有计数循环，配一个含循环节点的 -level 使用"); yield break; }
        for (int i = 0; i < 3; i++) Debug.Log("[demo] 卡面第 " + (i + 1) + " 行：" + NodeCard.ForloopLine(loop, i));
        var card = GraphCanvas.FindCard(loop.Uid);
        if (card == null || !card.OpenExprField("fromExpr")) { Debug.Log("[demo] -mathdemo 打不开计数起点字段"); yield break; }
        Debug.Log("[demo] -mathdemo 浮窗已打开：顶部两个页签，左边元素栏带「数学函数」和「常量」");
    }

    // -exprcard：在真实关卡画布的节点卡片上走一遍「清空字段 → 打开小窗 → 拖元素芯片 → 提交回写」
    // 先走「赋值」的语句字段（a[i] = x），再走循环节点的起点字段
    IEnumerator ExprCardDemo()
    {
        yield return new WaitForSeconds(.5f);
        if (GraphCanvas == null) yield break;
        var g = GraphCanvas.CurrentGraph;
        if (g == null || g.Nodes == null) yield break;

        var wset = g.Nodes.Find(n => n != null && n.Type == "varset" && !string.IsNullOrEmpty(GraphUtil.Get(n, "valueExpr")));
        if (wset != null) yield return CardChipDrag(wset, "valueExpr", "a");
        var loop = g.Nodes.Find(n => n != null && n.Type == "forloop");
        if (loop != null) yield return CardChipDrag(loop, "fromExpr", "n");
        if (wset == null && loop == null) Debug.Log("[exprcard] 当前关卡没有赋值卡片或循环节点，配 -level L2 使用");
    }

    IEnumerator CardChipDrag(GraphNode node, string key, string chipLabel)
    {
        node.Params[key] = "";
        var card = GraphCanvas.FindCard(node.Uid);
        if (card == null) { Debug.Log("[exprcard] 找不到节点卡片 " + node.Uid); yield break; }
        card.SyncExprFields();
        if (!card.OpenExprField(key)) { Debug.Log("[exprcard] " + node.Type + " 卡片上没有 " + key + " 字段"); yield break; }
        var pop = ExpressionPopoverUI.Current;
        if (pop == null) yield break;

        yield return new WaitForSeconds(.8f);
        if (!pop.DemoPressChip(chipLabel)) { Debug.Log("[exprcard] 元素栏里没有变量 " + chipLabel); yield break; }
        pop.DemoDragChipTo(0);
        Debug.Log("[exprcard] 已在 " + node.Type + " 卡片的小窗里拖起 " + chipLabel);
        yield return new WaitForSeconds(1f);
        pop.DemoReleaseChip(0);
        Debug.Log("[exprcard] 落下后 draft=" + pop.DemoDraft);
        yield return new WaitForSeconds(.6f);
        pop.DemoSubmit();
        Debug.Log("[exprcard] 提交后卡片 " + key + " = " + GraphUtil.Get(node, key));
    }

    IEnumerator OpenExprFieldDemo(ExpressionFieldUI field)
    {
        yield return new WaitForSeconds(.35f);
        if (field != null) field.OpenPopover();
    }

    // -submit：延迟一秒触发提交，验证提交按钮真的有反应
    IEnumerator SubmitDemo()
    {
        yield return new WaitForSeconds(1f);
        SubmitLevel();
        yield return new WaitForSeconds(.6f);
    }

    // -tracedemo：直接进跟踪模式，验收「不再先把整片跑一遍」
    IEnumerator TraceDemo()
    {
        yield return new WaitForSeconds(1f);
        RunTrace();
    }

    // -arrayviz：不播搬运动画，直接把 L2 的事件流跳到「数组已经填了好几格」那一步，
    // 再把镜头对准「创建数组」卡片 —— 用来验收数组看板就停在这张卡片旁边而不是屏幕底部。
    IEnumerator ArrayVizDemo()
    {
        yield return new WaitForSeconds(.6f);
        LoadLevel("L2");
        yield return null;
        if (!PrepareSession()) { Debug.Log("[arrayviz] 会话准备失败"); yield break; }
        int pick = -1;
        for (int i = 0; i < events.Count; i++)
            if (events[i] != null && events[i].Arrays != null && events[i].Arrays.Count > 0) pick = i;
        if (pick < 0) { Debug.Log("[arrayviz] 这一关没有数组事件"); yield break; }
        eventCursor = pick + 1;
        var ev = events[pick];
        PlayEvent(ev);

        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        var decl = g != null ? g.Nodes.Find(n => n != null && n.Type == "vardecl" && ExprLang.IsArrayType(GraphUtil.Get(n, "vtype"))) : null;
        if (decl != null && GraphCanvas != null) GraphCanvas.CenterOnNode(decl.Uid, 1f);

        int cells = 0;
        var names = new System.Text.StringBuilder();
        foreach (var kv in ev.Arrays) { names.Append(kv.Key).Append(' '); cells += kv.Value != null ? kv.Value.Count : 0; }
        Debug.Log("[arrayviz] 停在第 " + eventCursor + " / " + playTotal + " 步（" + ev.Text + "），数组 "
            + names + "共 " + cells + " 格，镜头对准 " + (decl != null ? decl.Uid : "（未找到创建数组卡片）"));
    }

    // -stackviz：把带栈的那一关的事件流跳到「刚压进去一只盘子」那一步，镜头对准「创建结构」卡片，
    // 验收栈看板停在那张卡旁边、栈顶亮着并带落下动画。
    IEnumerator StackVizDemo()
    {
        yield return new WaitForSeconds(.6f);
        if (level == null || !HasStructKind(level, "stack")) LoadLevel(LevelIdWithStructKind("stack"));
        yield return null;
        if (!PrepareSession()) { Debug.Log("[stackviz] 会话准备失败"); yield break; }
        int pick = -1;
        for (int i = 0; i < events.Count; i++)
            if (events[i] != null && events[i].Kind == "push" && events[i].Structs != null && events[i].Structs.Count > 0) pick = i;
        if (pick < 0)
            for (int i = 0; i < events.Count; i++)
                if (events[i] != null && events[i].Structs != null && events[i].Structs.Count > 0) pick = i;
        if (pick < 0) { Debug.Log("[stackviz] 这一关没有栈事件"); yield break; }
        eventCursor = pick + 1;
        var ev = events[pick];
        PlayEvent(ev);

        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        var st = g != null ? g.Nodes.Find(n => n != null && n.Type == "structnew") : null;
        if (st != null && GraphCanvas != null) GraphCanvas.CenterOnNode(st.Uid, 1f);

        int plates = 0;
        foreach (var s in ev.Structs) if (s != null && s.Items != null) plates += s.Items.Count;
        Debug.Log("[stackviz] 停在第 " + eventCursor + " / " + playTotal + " 步（" + ev.Text + "），栈里共 " + plates +
            " 个元素，镜头对准 " + (st != null ? st.Uid : "（未找到创建结构卡片）"));
    }

    // -queueviz：把队列关的事件流停在「刚排进一节车厢」那一步，镜头对准「创建队列」卡片，
    // 验收队列看板横着排在卡片旁边、队头绿队尾黄并带滑入动画。
    IEnumerator QueueVizDemo()
    {
        yield return new WaitForSeconds(.6f);
        if (level == null || !HasStructKind(level, "queue")) LoadLevel(LevelIdWithStructKind("queue"));
        yield return null;
        if (!PrepareSession()) { Debug.Log("[queueviz] 会话准备失败"); yield break; }
        int pick = -1;
        for (int i = 0; i < events.Count; i++)
            if (events[i] != null && events[i].Kind == "push" && HasStructKind(events[i], "queue")) pick = i;
        if (pick < 0)
            for (int i = 0; i < events.Count; i++)
                if (events[i] != null && HasStructKind(events[i], "queue")) pick = i;
        if (pick < 0) { Debug.Log("[queueviz] 这一关没有队列事件"); yield break; }
        eventCursor = pick + 1;
        var ev = events[pick];
        PlayEvent(ev);

        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        var st = g != null ? g.Nodes.Find(n => n != null && n.Type == "structnew") : null;
        if (st != null && GraphCanvas != null) GraphCanvas.CenterOnNode(st.Uid, 1f);

        int cars = 0;
        foreach (var s in ev.Structs) if (s != null && s.Kind == "queue" && s.Items != null) cars += s.Items.Count;
        Debug.Log("[queueviz] 停在第 " + eventCursor + " / " + playTotal + " 步（" + ev.Text + "），队列里共 " + cars +
            " 节车厢，镜头对准 " + (st != null ? st.Uid : "（未找到创建队列卡片）"));
    }

    // 关卡 / 事件里的结构实例是否是指定的那一种（stack / queue）。
    static bool HasStructKind(Level lv, string kind)
    {
        if (lv == null || lv.Structs == null) return false;
        foreach (var s in lv.Structs) if (s != null && s.Kind == kind) return true;
        return false;
    }

    static bool HasStructKind(Dayu.Core.TraceEvent ev, string kind)
    {
        if (ev == null || ev.Structs == null) return false;
        foreach (var s in ev.Structs) if (s != null && s.Kind == kind) return true;
        return false;
    }

    static string LevelIdWithStructKind(string kind)
    {
        foreach (var l in Levels.All) if (HasStructKind(l, kind)) return l.Id;
        return kind == "queue" ? "S3" : "L3";
    }

    // -boarddrag：等 -arrayviz 把看板铺好，再让画布自己拿真实射线去命中「标题条」和右下角「拉手」，
    // 走一遍 beginDrag→drag→endDrag，把位置和尺寸的前后变化打进日志。
    // 外部鼠标脚本在这台共享桌面上老是抢不到前台，所以自检放进玩家内部，不依赖真实光标。
    IEnumerator BoardDragDemo()
    {
        yield return new WaitForSeconds(2.5f);
        if (GraphCanvas == null) { Debug.Log("[boarddrag] 画布还没建好"); yield break; }
        Debug.Log("[boarddrag] " + GraphCanvas.BoardDragReport());
    }

    // -tipsopen：等关卡装载完，模拟玩家点了一下「看提示」，把折叠块摊开
    IEnumerator TipsOpenDemo()
    {
        yield return new WaitForSeconds(1.2f);
        tipsOpen = true;
        RebuildInspector(selectedNode);
        Debug.Log("[tipsopen] 已点开提示，关卡 " + (level != null ? level.Id : "?") + " 提示 " + (level != null && level.Tips != null ? level.Tips.Count : 0) + " 条");
    }

    // -stepdemo：连点三次单步，验收「点单步不会先把整片跑一遍」且每一步都带搬运动画
    IEnumerator StepDemo()
    {
        yield return new WaitForSeconds(.8f);
        for (int i = 0; i < 3; i++)
        {
            StepOnce();
            Debug.Log("[stepdemo] 第 " + (i + 1) + " 次单步，cursor=" + eventCursor + "/" + playTotal);
            yield return new WaitForSeconds(1.2f);
        }
    }

    // -exprnodes：给当前关卡的输出节点预置一条示例表达式，只为截图验收内嵌表达式，不写任何存档
    void ApplyExprNodesDemo()
    {
        if (GraphCanvas == null) return;
        var g = GraphCanvas.CurrentGraph;
        if (g == null) return;
        var outNode = g.Nodes.Find(n => n != null && n.Type == "output");
        if (outNode == null) return;
        outNode.Params["outputExpr"] = "a + b";
        var card = GraphCanvas.FindCard(outNode.Uid);
        if (card != null)
        {
            card.SyncExprFields();
            card.RefreshSummary();
        }
        Debug.Log("[exprnodes] output 节点表达式 = " + outNode.Params["outputExpr"]);
    }

    // ---------------- 面板 ----------------

    RectTransform Panel(RectTransform parent, string name, float x, float y, float w, float h, Color color)
    {
        var rt = Child(name, parent);
        Anchor(rt, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = White;
        img.type = Image.Type.Simple;
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    // 面板之间的一条实色细线：只用 16 色板里的沙色，不再新造灰
    void Divider(RectTransform parent, string name, float x, float y, float w, float h)
    {
        var rt = Panel(parent, name, x, y, w, h, Theme.Salt);
        rt.SetAsLastSibling();
    }

    RectTransform FindRect(RectTransform parent, string name)
    {
        var tr = parent.Find(name);
        return tr == null ? null : (RectTransform)tr;
    }

    // 响应式锚点：offsetMin/offsetMax 是相对锚点框的四边距离，与 pivot 无关，
    // 用来让面板贴住屏幕边而不是钉死在 1440x900 的坐标上
    static void Edge(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = oMin; rt.offsetMax = oMax;
    }

    // 周边栏要挡住鼠标：平移后的卡片虽然被栏盖住了，点击却还会穿过去选中看不见的卡片
    void SetBlocking(RectTransform rt)
    {
        var img = rt != null ? rt.GetComponent<Image>() : null;
        if (img != null) img.raycastTarget = true;
    }

    void BuildTopBar()
    {
        Label(TopBarRect, "大肥鱼の算法大冒险", 12, 8, Theme.Ink, 2);
        headerLevel = Label(TopBarRect, "第一章 · 入门 / 两数之和", 300, 8, Theme.Ink, 2);
        SetLines(headerLevel, Fit(headerLevel.Text, HeaderLevelW, 2), 0f);

        string[] btns = { "全屏", "菜单", "提交", "运行", "单步", "撤销", "恢复", "重来", "断点", "跟踪", "布局", "适应", "速度" };
        const float gap = 8f, rightPad = 12f, btnH = 26f;
        // 按钮组整体贴右：窗口变宽时整组跟着右边走，不会悬在顶栏中间
        var holder = Child("TopButtons", TopBarRect);
        holder.anchorMin = holder.anchorMax = new Vector2(1f, 1f);
        holder.pivot = new Vector2(1f, 1f);
        holder.anchoredPosition = new Vector2(-rightPad, 0f);
        var created = new List<PixelButton>();
        float total = 0f;
        for (int i = 0; i < btns.Length; i++)
        {
            string name = btns[i];
            System.Action click = null;
            if (name == "运行") click = RunInput;
            else if (name == "单步") click = StepOnce;
            else if (name == "撤销") click = DoUndoClick;
            else if (name == "恢复") click = DoRedoClick;
            else if (name == "重来") click = RestartLevel;
            else if (name == "适应") click = ResetView;
            else if (name == "断点") click = RunBreakpoint;
            else if (name == "跟踪") click = RunTrace;
            else if (name == "全屏") click = ToggleFullscreen;
            else if (name == "菜单") click = ShowMainMenu;
            else if (name == "提交") click = SubmitLevel;
            else if (name == "速度") click = CycleRunSpeed;
            else if (name == "布局") click = TogglePortLayout;
            var b = PixelButton.Create(holder, name, click, 0, (int)btnH, name == "提交");
            if (name == "撤销") btnUndo = b;
            else if (name == "恢复") btnRedo = b;
            else if (name == "提交") btnSubmit = b;
            created.Add(b);
            total += b.Rect.sizeDelta.x + (i > 0 ? gap : 0f);
        }
        holder.sizeDelta = new Vector2(total, 44f);
        float y = Mathf.Round((44f - btnH) * 0.5f);          // 垂直居中
        float bx = 0f;
        foreach (var b in created)
        {
            var rt = b.Rect;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(bx, -y);
            bx += rt.sizeDelta.x + gap;
        }
        Debug.Log("[btn] 顶栏按钮组总宽 " + total + "（贴右 " + rightPad + "）");
        RefreshTopBarButtons();
    }

    // 撤销 / 恢复按画布历史的深浅置灰，提交在看答案时禁用
    void RefreshTopBarButtons()
    {
        if (GraphCanvas != null)
        {
            if (btnUndo != null) btnUndo.SetEnabled(GraphCanvas.CanUndo);
            if (btnRedo != null) btnRedo.SetEnabled(GraphCanvas.CanRedo);
        }
        if (btnSubmit != null) btnSubmit.SetEnabled(!previewAnswer);
    }

    // 撤销会把整张图换成历史里的新对象，App 攥着的那份引用必须一起放手，否则检查器还在讲一个已经没了的模块
    void DoUndoClick()
    {
        if (GraphCanvas == null || !GraphCanvas.CanUndo) return;
        GraphCanvas.DoUndo();
        AfterHistoryChange("已撤销一步");
    }

    void DoRedoClick()
    {
        if (GraphCanvas == null || !GraphCanvas.CanRedo) return;
        GraphCanvas.DoRedo();
        AfterHistoryChange("已恢复一步");
    }

    void AfterHistoryChange(string toast)
    {
        selectedNode = null;
        ClearBadges();
        MarkCompileDirty();
        RebuildInspector(null);
        RefreshTopBarButtons();
        Toast(toast);
    }

    void CycleRunSpeed()
    {
        runSpeed = runSpeed == RunSpeed.Normal ? RunSpeed.Fast : runSpeed == RunSpeed.Fast ? RunSpeed.Slow : RunSpeed.Normal;
        string label = runSpeed == RunSpeed.Slow ? "慢速" : runSpeed == RunSpeed.Fast ? "快速" : "正常";
        Toast("运行速度：" + label);
        Debug.Log("[speed] " + label);
    }

    // 顶栏「布局」：模块的输入输出端口在「上下」与「左右」之间来回切，连线方向跟着换
    void TogglePortLayout()
    {
        if (GraphCanvas == null) { Toast("还没有画布"); return; }
        GraphCanvas.TogglePortLayout();
        foreach (var uid in breakpointUids) GraphCanvas.SetBreakpointDot(uid, true);   // 重建卡片会把断点小圆点抹掉
        Toast(GraphCanvas.VerticalPorts ? "端口朝向：上下（自上而下执行）" : "端口朝向：左右");
        Debug.Log("[layout] 端口朝向 " + (GraphCanvas.VerticalPorts ? "上下" : "左右"));
    }

    // 提交：先编译，然后把关卡的每一条样例依次搬到对比板上核对，全对才记通关并跳下一关
    void SubmitLevel()
    {
        if (level == null) { Toast("还没有加载关卡"); return; }
        if (previewAnswer) { Toast("标准答案预览里不能提交，先点「退出预览」"); return; }
        if (submitRoutine != null) { Debug.Log("[submit] 上一条提交还在播，先等它跑完"); return; }
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        if (g == null) { Toast("画布是空的"); return; }
        var cr = Compiler.Compile(g);
        if (!cr.Ok)
        {
            var e0 = cr.Errors.Count > 0 ? cr.Errors[0] : null;
            SetMessage("提交失败：" + (e0 != null ? e0.Message : "编译错误"));
            if (e0 != null && !string.IsNullOrEmpty(e0.NodeUid)) GraphCanvas.HighlightNode(e0.NodeUid, true);
            Toast("提交失败：编译没通过");
            return;
        }
        if (level.Tests == null || level.Tests.Count == 0) { Toast("这关没有测试样例"); return; }

        StopRun();
        GraphCanvas.ClearHighlight();
        submitRoutine = StartCoroutine(SubmitRoutine(cr.Program));
    }

    // 判定分两层（洛谷口径）：可见的「样板」逐条放给玩家看、跑动画；
    // 隐藏「抽查」只报第几条没过，输入输出一律不公开，否则隐藏数据就等于白送。
    IEnumerator SubmitRoutine(Program prog)
    {
        // 答案预览铺的是关卡自带的解，提交它必然全对，等于白送通关，所以预览态禁止提交
        if (previewAnswer) { Toast("标准答案预览里不能提交，先点「退出预览」"); yield break; }
        var g = GraphCanvas.CurrentGraph;
        var tests = level.Tests;
        int total = tests.Count, pass = 0, fail = 0;
        int wantV = LevelBrief.Visible(level).Count, wantH = LevelBrief.HiddenCount(level);
        int vi = 0, hi = 0, vpass = 0, hpass = 0;
        var bad = new List<string>();
        for (int i = 0; i < total; i++)
        {
            var t = tests[i];
            bool open = t.Visible;                       // 公开 = 样板，不公开 = 抽查
            if (open) { vi++; SetSampleBoardIndex(i); SetMessage("提交中：样板 " + vi + " / " + wantV + "「" + OneLine(t.Input) + "」"); }
            else { hi++; SetMessage("提交中：抽查 " + hi + " / " + wantH + "（隐藏数据，不公开内容）"); }
            yield return new WaitForSeconds(TokenGapSec);

            var run = Vm.Run(prog, t.Input);
            bool ok = run != null && run.Ok && run.Output == t.Output;
            if (ok) { pass++; if (open) vpass++; else hpass++; }
            else
            {
                fail++;
                if (bad.Count < 2)
                    bad.Add(open
                        ? OneLine(t.Input) + " → 期望 " + t.Output + "，实际 " +
                          (run != null && run.Error != null ? run.Error.Message : (run == null ? "无结果" : run.Output))
                        // 抽查只报「第几条」，具体是什么数据要玩家自己算出来
                        : "抽查第 " + hi + " 条" + (run != null && run.Error != null ? "运行出错" : "输出对不上"));
            }

            if (open)
            {
                yield return PlayCompareAnimation(g, run);      // 逐位飞入 + AC/WA 徽标
                if (!ok && run != null && run.Error != null)
                    SetMessage("样板 " + (vi) + " 运行出错：" + run.Error.Message);
                SetMessage((ok ? "通过 " : "未过 ") + "样板 " + vi + "/" + wantV + "：" +
                           (ok ? "合格" : "对不上（当前 " + pass + " 对 " + fail + " 错）"));
            }
            else
                SetMessage((ok ? "通过 " : "未过 ") + "抽查 " + hi + "/" + wantH + "：" +
                           (ok ? "合格" : "对不上（当前 " + pass + " 对 " + fail + " 错）"));
            yield return new WaitForSeconds(Mathf.Max(.16f, TokenGapSec));
        }
        submitRoutine = null;

        // 提交这一笔要记进进度元信息（提交次数 + 连错次数），通关就当场落盘，玩家随时退都不会丢
        SaveFlow.RecordSubmit(progress, level.Id, DateTime.UtcNow.Ticks, fail == 0);
        SaveSystem.Flush();
        SaveProgressMeta();

        if (fail == 0)
        {
            Toast("合格：样板 " + vpass + " 条 + 抽查 " + hpass + " 条全对");
            Debug.Log("[submit] " + level.Id + " pass=" + pass + " fail=0 样板=" + vpass + " 抽查=" + hpass);
            ShowLevelClear(vpass, hpass);
        }
        else
        {
            SetMessage("还没合格：" + pass + " / " + total + " 通过（样板 " + vpass + "/" + wantV +
                       "、抽查 " + hpass + "/" + wantH + "）· " + string.Join("；", bad.ToArray()));
            Toast("还有 " + fail + " 条没过（样板 + 抽查共 " + total + " 条）· 连错 " + Failures(level.Id) + " 次");
            Debug.Log("[submit] " + level.Id + " pass=" + pass + " fail=" + fail);
        }
    }

    // 把样例板切到第 index 条：对比板期望、卡片摘要都跟着走，逐样例动画才有正确的期望值
    void SetSampleBoardIndex(int index)
    {
        if (GraphCanvas == null || GraphCanvas.CurrentGraph == null || level == null || level.Tests.Count == 0) return;
        var board = GraphCanvas.CurrentGraph.Nodes.Find(n => n != null && n.Type == "sample_input");
        if (board == null) return;
        board.Params["group"] = Mathf.Clamp(index, 0, level.Tests.Count - 1).ToString();
        ApplySamplePreview();
        RefreshSampleControls();
        lastInput = FirstTestInput();
    }

    // 通关横幅：只把横幅立起来，停在原地等玩家自己点，不做任何定时跳转。
    // 之前这里是「2.5 秒没人点就自动进下一关」，玩家还在看对比板就被送走了，
    // 想留在本关改改代码的机会都没有；横幅会一直挡住画面，等点为止。
    void ShowLevelClear(int visiblePassed, int hiddenPassed)
    {
        SetCleared(level.Id);
        var next = NextLevel(level.Id);
        string line = "样板 " + visiblePassed + " 条 + 抽查 " + hiddenPassed + " 条全部通过";
        string body = next == null
            ? line + "\n" + Levels.All.Count + " 关全部打通，没有更多关卡了。"
            : line + "\n下一关：" + next.Chapter + " · " + next.Title;
        MenuDialog(600, 208, next == null ? "关卡全部通过！" : "关卡通过！", body,
            next == null ? new object[] { "进入下一关", (Action)(() => ShowLevelSelect()),
                           "留在本关", (Action)(() => HideMenu()) }
                         : new object[] { "进入下一关", (Action)(() => { HideMenu(); LoadLevel(next.Id); }),
                                          "留在本关", (Action)(() => HideMenu()) });
        SetMessage("合格：" + line + "，请从弹窗选择留在本关或进入下一关");
    }

    RectTransform paletteViewport;
    RectTransform inspectorView, inspectorContent;
    PaletteScroll inspectorScroll;
    RectTransform paletteContent;
    PaletteScroll paletteScroll;
    Coroutine paletteFadeCo;
    // 收起的一级目录（按 Cat 记），跨关卡保留玩家的偏好
    readonly HashSet<string> paletteCollapsed = new HashSet<string>();
    // 收起的二级目录（按 Cat/Sub 记）。数据结构一栏以前把所有方法平铺出来，玩家看到的是
    // 二十几个不认识的名词；现在每个二级目录默认收起，先只看到「数组」「栈」「队列」这几个头，
    // 点一下才展开它下面的操作。
    readonly HashSet<string> paletteCollapsedSubs = new HashSet<string>();
    bool subDefaultsSeeded;

    public static string SubKey(string cat, string sub) { return cat + "/" + sub; }

    // 首次建栏时按目录里的条目数定初始开合：只有一两项的结构（数组目前只有「创建数组」）
    // 展开更直观，方法多的（栈 / 队列各六项）默认收起。玩家点过之后一律以玩家的选择为准。
    void SeedSubDefaults()
    {
        if (subDefaultsSeeded) return;
        subDefaultsSeeded = true;
        foreach (var k in DefaultCollapsedSubs()) paletteCollapsedSubs.Add(k);
    }

    // 纯函数：从模块目录算出「默认该收起」的二级目录，方便自测直接对照目录条目数
    public static HashSet<string> DefaultCollapsedSubs()
    {
        var counts = new Dictionary<string, int>();
        foreach (var n in NodeCatalog.All)
        {
            if (string.IsNullOrEmpty(n.Sub)) continue;
            string k = SubKey(n.Cat, n.Sub);
            counts[k] = counts.TryGetValue(k, out int c) ? c + 1 : 1;
        }
        var collapsed = new HashSet<string>();
        foreach (var kv in counts)
            if (kv.Value > SubAutoCollapseThreshold) collapsed.Add(kv.Key);
        return collapsed;
    }

    public const int SubAutoCollapseThreshold = 2;

    // ---------------- 主菜单 / 关卡选择 / 玩法说明 ----------------

    RectTransform uiRoot;
    GameObject menuBackdrop;
    RectTransform menuDialog;
    readonly List<RectTransform> menuDeco = new List<RectTransform>();
    readonly List<MenuBubble> menuBubbles = new List<MenuBubble>();
    RectTransform menuWaveA, menuWaveB;
    Image menuHero;
    float menuHeroX, menuHeroY;
    Coroutine menuAnim;

    void BuildMenuLayer()
    {
        if (uiRoot == null) return;
        menuBackdrop = Panel(uiRoot, "MenuBackdrop", 0, 0, W, H, Theme.WhaleDeep).gameObject;
        // 底衬贴满整块画布：窗口放大 / 换比例时四周都由它盖住，不留黑边
        Edge((RectTransform)menuBackdrop.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var img = menuBackdrop.GetComponent<Image>();
        img.raycastTarget = true;          // 挡住后面的画布与按钮
        menuBackdrop.SetActive(false);
    }

    // 菜单背景里的浮动气泡：位置、速度、相位都由固定种子决定，截图每次都能对上
    sealed class MenuBubble
    {
        public RectTransform Rt;
        public float Xf, Size, Speed, Phase, Sway;   // Xf = 横向位置占画布宽的比例，换窗口比例也不挤在左半边
    }

    // 主菜单那一层「海底」：竖向渐变带 + 上浮气泡 + 底部两道浪。
    // 只在主菜单开的时候铺，关掉菜单连同协程一起收掉，不留到关卡里挡视线。
    void MenuScenery()
    {
        if (menuBackdrop == null) return;
        for (int i = menuDeco.Count - 1; i >= 0; i--)
            if (menuDeco[i] != null) Destroy(menuDeco[i].gameObject);
        menuDeco.Clear();
        menuBubbles.Clear();

        var parent = (RectTransform)menuBackdrop.transform;
        var rnd = new System.Random(20261031);

        // 自上而下的八道渐变带：越往下越深，给整块底衬出层次
        int bands = 8;
        for (int i = 0; i < bands; i++)
        {
            float k = (float)i / (bands - 1);
            var c = Color.Lerp(Theme.A(Theme.Whale, 1f), Theme.A(Theme.WhaleDeep, 1f), k);
            var band = Panel(parent, "MenuBand" + i, 0, 0, W, 1f, c);
            Edge(band, new Vector2(0f, 1f - (i + 1) / (float)bands), new Vector2(1f, 1f - i / (float)bands), Vector2.zero, Vector2.zero);
            band.SetAsFirstSibling();
            menuDeco.Add(band);
        }

        // 底部两道浪：一条实色、一条浅一档，靠协程左右摇
        menuWaveA = Panel(parent, "MenuWaveA", -W, H - 46f, W * 3f, 22f, Theme.A(Theme.WhaleDeep, .92f));
        menuWaveB = Panel(parent, "MenuWaveB", -W, H - 26f, W * 3f, 14f, Theme.A(Theme.Whale, .55f));
        Edge(menuWaveA, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(-W, 46f), new Vector2(-W + W * 3f, 68f));
        Edge(menuWaveB, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(-W, 26f), new Vector2(-W + W * 3f, 40f));
        menuDeco.Add(menuWaveA); menuDeco.Add(menuWaveB);

        // 上浮气泡：3~7px 的小方块，越大的浮得越快，看着才有前后景
        for (int i = 0; i < 18; i++)
        {
            int size = rnd.Next(3, 8);
            var b = Panel(parent, "MenuBubble" + i, 0f, 0f, size, size,
                Theme.A(Theme.Foam, (float)rnd.Next(22, 46) / 100f));
            Edge(b, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -H), new Vector2(size, -H - size));
            menuDeco.Add(b);
            menuBubbles.Add(new MenuBubble
            {
                Rt = b,
                Xf = (float)rnd.NextDouble(),
                Size = size,
                Speed = 22f + size * 9f + rnd.Next(0, 26),
                Phase = (float)(rnd.NextDouble() * 6.283),
                Sway = 6f + rnd.Next(0, 14),
            });
        }

        if (menuAnim != null) StopCoroutine(menuAnim);
        menuAnim = StartCoroutine(MenuAnimRoutine());
    }

    IEnumerator MenuAnimRoutine()
    {
        var wait = new WaitForSeconds(.05f);      // 每 50ms 挪一格，像素画本来就是一格一格的
        float t = 0f;
        while (menuBackdrop != null && menuBackdrop.activeSelf)
        {
            t += .05f;
            // 每格都按画布的实际尺寸算：窗口中途放大缩小，气泡和浪也跟着铺满
            float vw = uiRoot != null ? uiRoot.rect.width : W;
            float vh = uiRoot != null ? uiRoot.rect.height : H;
            for (int i = 0; i < menuBubbles.Count; i++)
            {
                var b = menuBubbles[i];
                if (b.Rt == null) continue;
                float y = vh + b.Size - Mathf.Repeat(t * b.Speed, vh + 80f);
                float x = b.Xf * vw + Mathf.Sin(t * .7f + b.Phase) * b.Sway;
                b.Rt.anchoredPosition = new Vector2(x, -y);
            }
            if (menuWaveA != null) menuWaveA.anchoredPosition = new Vector2(-W + Mathf.Sin(t * .5f) * 26f, 46f);
            if (menuWaveB != null) menuWaveB.anchoredPosition = new Vector2(-W * 2f + Mathf.Sin(t * .35f + 1.6f) * 40f, 26f);
            if (menuHero != null)
                menuHero.rectTransform.anchoredPosition = new Vector2(menuHeroX, -menuHeroY + Mathf.Sin(t * 1.3f) * 3f);
            yield return wait;
        }
        menuAnim = null;
    }

    void ClearMenuScenery()
    {
        if (menuAnim != null) { StopCoroutine(menuAnim); menuAnim = null; }
        for (int i = menuDeco.Count - 1; i >= 0; i--)
            if (menuDeco[i] != null) Destroy(menuDeco[i].gameObject);
        menuDeco.Clear();
        menuBubbles.Clear();
        menuWaveA = menuWaveB = null;
        menuHero = null;
    }

    // 居中弹窗外壳：深青底 + 白卡 + 外描边。MenuOpen 在这之上再叠标题条，主菜单直接用它自己排版
    RectTransform MenuShell(float w, float h)
    {
        if (menuBackdrop == null) BuildMenuLayer();
        if (menuDialog != null) Destroy(menuDialog.gameObject);
        menuDialog = Panel((RectTransform)menuBackdrop.transform, "MenuDialog",
            0f, 0f, w, h, Theme.White);
        // 弹窗按画布实际尺寸居中：窗口放大后仍居中，不会偏到左上角露出黑边。
        // pivot 必须先改再 Edge —— Unity 改 pivot 时会保住 anchoredPosition，后改会把矩形整体挪走
        menuDialog.pivot = new Vector2(.5f, .5f);
        Edge(menuDialog, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(-w * .5f, -h * .5f + 40f), new Vector2(w * .5f, h * .5f + 40f));
        var frame = menuDialog.gameObject.AddComponent<Outline>();
        frame.effectColor = Theme.WhaleDeep;
        frame.effectDistance = new Vector2(3, -3);
        // 弹窗从 0.94 倍弹到位并淡入，和浮窗、卡片那套补间口径一致
        var cg = menuDialog.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        StartCoroutine(Tween.Run(this, .18f, k =>
        {
            if (menuDialog == null) return;
            cg.alpha = k;
            menuDialog.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, k);
        }));
        return menuDialog;
    }

    // 打开一层居中弹窗：返回内容面板，按钮与文字由调用方摆
    RectTransform MenuOpen(float w, float h, string title)
    {
        var dialog = MenuShell(w, h);
        var head = Panel(dialog, "MenuHead", 0, 0, w, 50f, Theme.Salt);
        head.GetComponent<Image>().raycastTarget = false;
        var rule = Panel(dialog, "MenuHeadRule", 0, 50f, w, 2f, Theme.Whale);
        rule.GetComponent<Image>().raycastTarget = false;
        var dot = Panel(dialog, "MenuTitleDot", 20, 18, 12, 12, Theme.Honey);
        dot.GetComponent<Image>().raycastTarget = false;
        var t = Label(dialog, title, 42, 14, Theme.Ink, 2);
        t.rectTransform.sizeDelta = new Vector2(w - 62f, 30f);
        var close = PixelButton.Create(dialog, "关闭", HideMenu, 0, 24);
        close.Rect.anchoredPosition = new Vector2(w - close.Rect.sizeDelta.x - 12f, -12f);
        return dialog;
    }

    void HideMenu()
    {
        ClearMenuScenery();
        if (menuBackdrop != null) menuBackdrop.SetActive(false);
        if (menuDialog != null) { Destroy(menuDialog.gameObject); menuDialog = null; }
    }

    // 一段正文 + 一行按钮的通用弹窗（通关横幅用它）
    RectTransform MenuDialog(float w, float h, string title, string body, object[] buttons)
    {
        var d = MenuOpen(w, h, title);
        // 左边一枚主角头像，正文往右让出头像宽度
        var av = Panel(d, "HeroFace", 20, 60, 64, 64, Color.white);
        var avImg = av.GetComponent<Image>();
        avImg.raycastTarget = false;
        avImg.sprite = HeroAssets.Face;
        avImg.preserveAspect = true;
        var t = Label(d, "", 96, 62, Theme.Ink, 2);
        t.Wrap = true;
        t.WrapWidth = (int)(w - 120);
        t.Text = body ?? "";
        if (buttons != null && buttons.Length > 1) MenuButtons(d, h - 54, buttons);
        if (menuBackdrop != null) menuBackdrop.SetActive(true);
        return d;
    }

    // 居中一行标签，返回宽度方便调用方排版
    PixelText MenuLine(RectTransform parent, float y, string text, Color color, int scale)
    {
        float w = parent.rect.width;
        float tw = PixelText.MeasureString(text, scale).x;
        var t = Label(parent, text, Mathf.Max(0f, (w - tw) * .5f), y, color, scale);
        t.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, tw), scale == 2 ? 30f : 18f);
        return t;
    }

    // 一行居中按钮（可多枚并排），按文字实际宽度居中
    void MenuButtons(RectTransform parent, float y, params object[] textActionPairs)
    {
        var list = new List<PixelButton>();
        for (int i = 0; i + 1 < textActionPairs.Length; i += 2)
        {
            var b = PixelButton.Create(parent, (string)textActionPairs[i], (Action)textActionPairs[i + 1], 0, 28, i == 0);
            list.Add(b);
        }
        float total = 0f;
        foreach (var b in list) total += b.Rect.sizeDelta.x;
        total += (list.Count - 1) * 10f;
        float x = Mathf.Max(12f, (parent.rect.width - total) * .5f);
        for (int i = 0; i < list.Count; i++)
        {
            list[i].Rect.anchoredPosition = new Vector2(x, -y);
            x += list[i].Rect.sizeDelta.x + 10f;
        }
    }

    void ShowMainMenu()
    {
        if (uiRoot == null) return;
        // 回主菜单就是「暂时退出」：先把这一关没落盘的改动写完，玩家下次进来才看得到自己的进度
        SaveSystem.Flush();
        SaveProgressMeta();
        MenuScenery();                       // 先把海底铺好，弹窗才是最后一层
        float w = 760f, h = 486f;
        var d = MenuShell(w, h);
        MenuRivets(d, w, h);

        // 招牌：4 号字大标题 + 一层鲸青投影。主菜单第一眼就该是个名字，不是一行小字
        const string title = "大肥鱼の算法大冒险";
        var tm = PixelText.MeasureString(title, 4);
        float tx = Mathf.Round((w - tm.x) * .5f);
        var shadow = Label(d, title, tx + 3f, 18f, Theme.A(Theme.Whale, .7f), 4);
        shadow.raycastTarget = false;
        var big = Label(d, title, tx, 14f, Theme.Ink, 4);
        big.raycastTarget = false;
        MenuLine(d, 78, "把算法画成一张会跑通的图", Theme.WhaleDeep, 2);
        var rule = Panel(d, "MenuTitleRule", 180, 110, w - 360, 2f, Theme.Honey);
        rule.GetComponent<Image>().raycastTarget = false;
        for (int i = 0; i < 3; i++)
        {
            var p = Panel(d, "MenuTitleDot" + i, w * .5f - 18f + i * 18f, 118, 6, 6, Theme.HoneyDeep);
            p.GetComponent<Image>().raycastTarget = false;
        }

        // 左半边：主角立绘（第三方女仆鲸鱼娘），白框 + 薄荷底 + 呼吸起伏
        var artFrame = Panel(d, "HeroFrame", 22, 132, 210, 300, Theme.MintLight);
        artFrame.GetComponent<Image>().raycastTarget = false;
        var art = Panel(d, "HeroArt", 26, 136, 202, 292, Theme.White);
        var artImg = art.GetComponent<Image>();
        artImg.raycastTarget = false;
        artImg.sprite = HeroAssets.Full;
        artImg.preserveAspect = true;
        menuHero = artImg; menuHeroX = 26f; menuHeroY = 136f;
        var caption = MenuLine(d, 442, "深海值班中", Theme.Whale, 2);
        caption.raycastTarget = false;

        // 右半边：进度 → 按钮 → 上手两句话 → 落款
        var col = Panel(d, "MenuCol", 250, 132, 488, 300, new Color(0, 0, 0, 0));
        col.GetComponent<Image>().raycastTarget = false;
        int cleared = 0;
        foreach (var l in Levels.All) if (IsCleared(l.Id)) cleared++;
        ChapterBar(col, 0, "序章", CountWhere(true), CountWhere(true, true));
        ChapterBar(col, 34, "第一章", CountWhere(false), CountWhere(false, true));

        MenuButtons(col, 78, "开始游戏", (Action)StartFromMenu, "选择关卡", (Action)ShowLevelSelect);
        MenuButtons(col, 120, "玩法说明", (Action)ShowHelp, "重置进度", (Action)ResetProgress);

        var h1 = MenuLine(col, 168, "连线：按住输出端口拖到输入端口上松手", Theme.Ink, 2);
        h1.raycastTarget = false;
        var h2 = MenuLine(col, 192, "调参：单击选中模块，双击直接改表达式", Theme.Ink, 2);
        h2.raycastTarget = false;
        var credit = MenuLine(col, 236, "主角形象：溟月（第三方授权素材）", Theme.Whale, 2);
        credit.raycastTarget = false;
        var total = MenuLine(col, 268, "共 " + Levels.All.Count + " 关 · 已通过 " + cleared + " 关", Theme.Whale, 2);
        total.raycastTarget = false;
        var recent = MenuLine(col, 296, RecentProgressLine(), Theme.WhaleDeep, 1);
        recent.raycastTarget = false;

        var close = PixelButton.Create(d, "关闭", HideMenu, 0, 24);
        close.Rect.anchoredPosition = new Vector2(w - close.Rect.sizeDelta.x - 12f, -12f);
        if (menuBackdrop != null) menuBackdrop.SetActive(true);
        Debug.Log("[menu] 主菜单打开，进度 " + cleared + "/" + Levels.All.Count);
    }

    // 主菜单那一行小字读数：存了几关 + 最近一次动笔的是哪一关
    string RecentProgressLine()
    {
        int count = 0;
        foreach (var ignored in SaveSystem.SavedLevelIds()) count++;
        if (count == 0) return "还没有存档，进关卡改一个模块就会自动存上";
        string best = null; long ticks = 0;
        foreach (var kv in progress)
            if (kv.Value != null && kv.Value.UpdatedTicks > ticks) { ticks = kv.Value.UpdatedTicks; best = kv.Key; }
        return string.IsNullOrEmpty(best) ? "已自动存档 " + count + " 关"
                                          : "已自动存档 " + count + " 关 · 最近 " + best + " " + SaveFlow.TimeShort(ticks);
    }

    // 白卡四角钉四枚蜜黄铆钉，弹窗边缘就不只是一条描边
    void MenuRivets(RectTransform d, float w, float h)
    {
        var pts = new[] { new Vector2(6, 6), new Vector2(w - 14, 6), new Vector2(6, h - 14), new Vector2(w - 14, h - 14) };
        for (int i = 0; i < pts.Length; i++)
        {
            var r = Panel(d, "MenuRivet" + i, pts[i].x, pts[i].y, 8, 8, Theme.Honey);
            r.GetComponent<Image>().raycastTarget = false;
        }
    }

    // 一行章节进度：章节名 + 细进度条 + 「x / y 关」。序章和第一章各占一行，比一根大长条看得清
    void ChapterBar(RectTransform col, float y, string name, int total, int done)
    {
        var t = Label(col, name, 0, y, Theme.Ink, 2);
        t.raycastTarget = false;
        var back = Panel(col, "ChapterBack_" + name, 108, y + 6, 258, 10f, Theme.Salt);
        back.GetComponent<Image>().raycastTarget = false;
        var edge = back.gameObject.AddComponent<Outline>();
        edge.effectColor = Theme.Whale;
        edge.effectDistance = new Vector2(1, -1);
        float k = total > 0 ? Mathf.Clamp01((float)done / total) : 0f;
        if (k > 0f)
        {
            var fill = Panel(col, "ChapterFill_" + name, 109, y + 7, Mathf.Max(2f, Mathf.Round(256f * k)), 8f,
                done == total ? Theme.Mint : Theme.Honey);
            fill.GetComponent<Image>().raycastTarget = false;
        }
        var num = Label(col, done + " / " + total + " 关", 378, y, Theme.WhaleDeep, 2);
        num.raycastTarget = false;
    }

    // prologue=true 数序章（章节名以「序章」开头），cleared=true 只数已通过的
    static int CountWhere(bool prologue, bool cleared = false)
    {
        int n = 0;
        foreach (var l in Levels.All)
        {
            if (l.Chapter.StartsWith("序章") != prologue) continue;
            if (cleared && !IsCleared(l.Id)) continue;
            n++;
        }
        return n;
    }

    void StartFromMenu()
    {
        var lv = ContinueLevel();
        HideMenu();
        if (lv == null) { Toast("还没有可玩的关卡"); return; }
        LoadLevel(lv.Id);
        Toast("进入 " + lv.Id + "：" + lv.Title);
    }

    Level ContinueLevel()
    {
        for (int i = 0; i < Levels.All.Count; i++)
            if (IsUnlocked(i) && !IsCleared(Levels.All[i].Id)) return Levels.All[i];
        return Levels.All.Count > 0 ? Levels.All[Levels.All.Count - 1] : null;
    }

    void ShowLevelSelect()
    {
        int n = Levels.All.Count;
        var d = MenuOpen(860, 140 + n * 44, "选择关卡");
        MenuLine(d, 60, "已通过的关卡解锁下一关", Theme.WhaleDeep, 2);
        float y = 100f;
        for (int i = 0; i < n; i++)
        {
            var l = Levels.All[i];
            bool unlocked = IsUnlocked(i), done = IsCleared(l.Id);
            // 行底色按状态分三档：已通关薄荷、可进入纯白、未解锁沙色
            var row = Panel(d, "LevelRow_" + l.Id, 24, y, d.rect.width - 48, 36,
                done ? Theme.MintLight : unlocked ? Theme.White : Theme.Salt);
            row.GetComponent<Image>().raycastTarget = true;
            // 左缘 4px 状态色条：不读文字也能一眼扫出进度走到哪一关
            var stripe = Panel(row, "Stripe", 0, 0, 4f, 36f, done ? Theme.Mint : unlocked ? Theme.Whale : Theme.Border);
            stripe.GetComponent<Image>().raycastTarget = false;
            var num = Panel(row, "Num", 12, 9, 18, 18, unlocked ? Theme.WhaleDeep : Theme.Whale);
            num.GetComponent<Image>().raycastTarget = false;
            Label(num, Fit((i + 1).ToString(), 12f, 1), 4, 2, Theme.White, 1).raycastTarget = false;
            Label(row, Fit(l.Chapter + " · " + l.Title, 410f, 2), 40, 0, unlocked ? Theme.Ink : Theme.Whale, 2)
                .rectTransform.sizeDelta = new Vector2(410, 36);
            string status = done ? "已通关" : unlocked ? "可进入" : "未解锁";
            // 状态字要跟行底色拉开对比：薄荷底上再写薄荷字就糊了
            var st = Label(row, status, 462, 0, done ? Theme.WhaleDeep : unlocked ? Theme.HoneyDeep : Theme.Whale, 2);
            st.raycastTarget = false;
            st.rectTransform.sizeDelta = new Vector2(96, 36);
            // 进度元信息（最后动笔时间 + 提交次数）：只看得到自己存过没存过，不暴露任何答案内容
            SaveFlow.Meta meta;
            if (progress.TryGetValue(l.Id, out meta) && meta != null && meta.UpdatedTicks > 0 && SaveSystem.HasSave(l.Id))
            {
                var info = Label(row, SaveFlow.TimeShort(meta.UpdatedTicks) + " · 提交 " + meta.Submissions + " 次",
                                 566, 0, Theme.WhaleDeep, 1);
                info.raycastTarget = false;
                info.rectTransform.sizeDelta = new Vector2(164, 36);
            }
            if (unlocked)
            {
                string id = l.Id;
                var go = PixelButton.Create(row, "进入", () => { HideMenu(); LoadLevel(id); }, 60, 26);
                go.Rect.anchoredPosition = new Vector2(row.rect.width - 74, -5);
            }
            y += 44;
        }
        MenuButtons(d, y + 10, "返回", (Action)ShowMainMenu);
        if (menuBackdrop != null) menuBackdrop.SetActive(true);
        Debug.Log("[menu] 关卡选择打开，共 " + n + " 关");
    }

    void ShowHelp()
    {
        var d = MenuOpen(900, 476, "玩法说明");
        var body = Label(d, "", 24, 60, Theme.Ink, 2);
        body.Text = "连线：在模块右侧的出口上按下鼠标，拖到下一个模块左侧的入口松手；离得远可以先点出口、再点入口。\n" +
                    "删线：把鼠标移到线上，线中间会出现小圆点，点一下就断开这根线。\n" +
                    "选中与框选：单击模块选中它；在空白处按住左键拖出一个框，框里的模块一起选中，可以整组拖动。\n" +
                    "批量操作：Ctrl+C 复制、Ctrl+V 粘贴、Delete 删除选中，多选之后一次做完。\n" +
                    "双击模块：条件、循环、输出、运算这些块直接进表达式编辑，浮窗顶部页签能切换同一个模块的两个表达式；点浮窗外面任意一点就是作废、还原原值。\n" +
                    "写表达式：左边「可用元素」点一下或拖进来，数学函数 abs、sqrt、pow、min、max、floor、ceil、round、sin、log 和常量 pi、e 都支持；要改名字、类型、循环变量名，点右侧「编辑参数」。\n" +
                    "数据：运算、条件、循环这些格子直接写变量名或表达式，不用连线。\n" +
                    "运行：「运行」看整片数字流动；「单步」一下一下走；「断点」在模块旁的圆点上设置。\n" +
                    "提交：把本关所有样例逐条搬进对比板，全对才记为通过并解锁下一关。";
        body.Wrap = true; body.WrapWidth = (int)(d.rect.width - 48);
        MenuButtons(d, 424, "返回菜单", (Action)ShowMainMenu, "选择关卡", (Action)ShowLevelSelect);
        if (menuBackdrop != null) menuBackdrop.SetActive(true);
        Debug.Log("[menu] 玩法说明正文高 " + body.Measure().y);
    }

    void ResetProgress()
    {
        foreach (var l in Levels.All) PlayerPrefs.DeleteKey(ClearKey(l.Id));
        PlayerPrefs.Save();
        // 「重置进度」连存档一起清：留着上一轮的图，玩家会以为进度根本没清干净
        SaveSystem.CancelPending();
        foreach (var l in Levels.All) SaveSystem.Delete(l.Id);
        progress.Clear();
        string metaErr;
        SaveFlow.WriteMeta(SaveSystem.Root, progress, out metaErr);
        Toast("进度与存档已重置");
        ShowMainMenu();
    }

    static string ClearKey(string id) { return "dayu.clear." + id; }
    static bool IsCleared(string id) { return !string.IsNullOrEmpty(id) && PlayerPrefs.GetInt(ClearKey(id), 0) == 1; }
    // 序章关卡标了 Free，新玩家一进来就能直接挑；正文章节按「过一关解锁下一关」推进，已通关的关随时可以回去重玩
    static bool IsUnlocked(int index)
    {
        if (index <= 0 || Levels.All[index].Free) return true;
        return IsCleared(Levels.All[index].Id) || IsCleared(Levels.All[index - 1].Id);
    }
    static void SetCleared(string id) { PlayerPrefs.SetInt(ClearKey(id), 1); PlayerPrefs.Save(); }
    static Level NextLevel(string id)
    {
        int i = Levels.All.FindIndex(x => x.Id == id);
        return i >= 0 && i + 1 < Levels.All.Count ? Levels.All[i + 1] : null;
    }

    void BuildPalette()
    {
        Label(PaletteRect, "模块", 10, 6, Theme.WhaleDeep, 2);

        // 左栏放不下全部条目，所以改成「视口 + 内容 + 滚轮」，不再靠 break 把最后的条目丢掉
        float viewW = PaletteRect.rect.width;
        float viewH = Mathf.Max(120f, PaletteRect.rect.height - 40f);
        paletteViewport = Panel(PaletteRect, "PaletteView", 0, 36f, viewW, viewH, new Color(0, 0, 0, 0));
        paletteViewport.GetComponent<Image>().raycastTarget = true;
        paletteViewport.gameObject.AddComponent<RectMask2D>();
        paletteScroll = paletteViewport.gameObject.AddComponent<PaletteScroll>();
        paletteContent = Child("PaletteContent", paletteViewport);
        paletteContent.anchorMin = paletteContent.anchorMax = new Vector2(0, 1);
        paletteContent.pivot = new Vector2(0, 1);
        paletteContent.sizeDelta = new Vector2(viewW, viewH);
        paletteScroll.Content = paletteContent;
        RebuildPalette();
    }

    // 多级目录：Cat 是一级（点标题收起/展开），Sub 是二级（数组 / 栈这种玩家叫得出名字的结构）。
    // 目录顺序跟着 NodeCatalog.All 里各 Cat 首次出现的顺序走，所以「输入输出」置顶是在数据里排的。
    void RebuildPalette()
    {
        const float rowH = 28f, groupH = 26f, subH = 22f;
        if (paletteContent == null || paletteScroll == null) { BuildPalette(); return; }
        for (int i = paletteContent.childCount - 1; i >= 0; i--)
        {
            var go = paletteContent.GetChild(i).gameObject;
            go.SetActive(false);                             // 立即隐藏，避免 Destroy 延迟一帧导致重影
            Destroy(go);
        }

        float viewW = paletteViewport.rect.width;
        float viewH = paletteViewport.rect.height;
        SeedSubDefaults();
        float y = 0f;
        string lastCat = null, lastSub = null;
        bool subHidden = false;
        foreach (var n in NodeCatalog.All)
        {
            if (n.Cat != lastCat)
            {
                lastCat = n.Cat;
                lastSub = null;
                subHidden = false;
                y += AddPaletteGroup(n.Cat, y, viewW, groupH);
            }
            if (paletteCollapsed.Contains(n.Cat)) continue;

            if (n.Sub != lastSub)
            {
                lastSub = n.Sub;
                subHidden = !string.IsNullOrEmpty(n.Sub) && paletteCollapsedSubs.Contains(SubKey(n.Cat, n.Sub));
                if (!string.IsNullOrEmpty(n.Sub)) y += AddPaletteSub(n.Cat, n.Sub, y, viewW, subH, !subHidden);
            }
            // 二级目录收起时它下面的条目一个都不画：玩家要的是「先只看到栈 / 队列这几个头，
            // 点进去才看到压入弹出」，条目全部罗列等于没分目录
            if (subHidden) continue;
            y += AddPaletteRow(n, y, viewW, rowH, string.IsNullOrEmpty(n.Sub) ? 6f : 24f);
        }
        paletteContent.sizeDelta = new Vector2(viewW, Mathf.Max(viewH, y));
        paletteScroll.SetExtent(y - viewH);
        // 目录开合 / 重建后整栏淡入：直接换内容会闪一下
        var cg = paletteContent.GetComponent<CanvasGroup>();
        if (cg == null) cg = paletteContent.gameObject.AddComponent<CanvasGroup>();
        if (paletteFadeCo != null) StopCoroutine(paletteFadeCo);
        paletteFadeCo = StartCoroutine(Tween.Fade(this, cg, .25f, 1f, .18f));
        Debug.Log("[palette] " + NodeCatalog.All.Length + " 项，内容高 " + y + "，视口高 " + viewH + "，可滚 " + Mathf.Max(0f, y - viewH));
    }

    // 一级目录：整行可点，点一下收起、再点展开
    float AddPaletteGroup(string cat, float y, float viewW, float h)
    {
        bool collapsed = paletteCollapsed.Contains(cat);
        var head = Panel(paletteContent, "Grp_" + cat, 0, y, viewW, h, Theme.A(Theme.Whale, .16f));
        var img = head.GetComponent<Image>();
        img.raycastTarget = true;
        head.gameObject.AddComponent<PaletteGroupClick>().Cat = cat;
        var t = Label(head, (collapsed ? "[+]" : "[-]") + CatName(cat), 8, 2, Theme.WhaleDeep, 2);
        t.raycastTarget = false;
        return h + 2f;
    }

    // 二级目录标题：和一级一样整行可点，点一下收起 / 展开它下面的操作。
    // 底色比一级淡一档，两级目录才分得开层次。
    float AddPaletteSub(string cat, string sub, float y, float viewW, float h, bool expanded)
    {
        var head = Panel(paletteContent, "Sub_" + cat + "_" + sub, 0, y, viewW, h, Theme.A(Theme.Grape, .08f));
        head.GetComponent<Image>().raycastTarget = true;
        head.gameObject.AddComponent<PaletteSubClick>().Key = SubKey(cat, sub);
        var t = Label(head, (expanded ? "[-]" : "[+]") + sub, 14, 0, Theme.GrapeDeep, 2);
        t.raycastTarget = false;
        return h;
    }

    // 可拖条目：图标靠左，名字用 2 号字，缩进按二级目录深浅走
    float AddPaletteRow(NodeCatalogEntry n, float y, float viewW, float h, float indent)
    {
        var row = Panel(paletteContent, "Row_" + n.Key, 0, y, viewW, h, new Color(0, 0, 0, 0));
        var hit = row.GetComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        hit.raycastTarget = true;
        var drag = row.gameObject.AddComponent<PaletteDrag>();
        drag.NodeType = n.NodeType;
        drag.Preset = n.Preset;
        // 左缘一条 3px 类别色条：模块栏跟画布卡片标题带同一套配色，
        // 玩家才能把「栏里的名字」和「画布上那块」对上号
        var bar = Panel(row, "Accent", 0, 0, 3f, h, Theme.Accent(n.ColorKey));
        bar.GetComponent<Image>().raycastTarget = false;
        float iconSize = 20f;
        var ico = Panel(row, "Icon", indent, (h - iconSize) * 0.5f, iconSize, iconSize, Color.white);
        var iimg = ico.GetComponent<Image>();
        iimg.raycastTarget = false;
        // 同一个节点类型能有两个入口（创建变量 / 创建数组），图标按条目 Key 找，找不到再退回节点类型
        var icoSprite = ModuleIcons.Get(n.Key);
        if (icoSprite == null) icoSprite = ModuleIcons.Get(n.NodeType);
        iimg.sprite = icoSprite;
        iimg.preserveAspect = true;
        float lx = indent + iconSize + 6f;
        var label = Label(row, n.Title, lx, 0, Theme.Ink, 2);
        label.raycastTarget = false;
        // 高度撑满条目，PixelText 会把单行文字垂直居中
        label.rectTransform.sizeDelta = new Vector2(Mathf.Max(40f, viewW - lx - 4f), h);
        return h;
    }

    // 收起 / 展开一级目录：整栏重建，条目不多，比重排兄弟节点省事
    public void TogglePaletteGroup(string cat)
    {
        if (string.IsNullOrEmpty(cat)) return;
        if (!paletteCollapsed.Add(cat)) paletteCollapsed.Remove(cat);
        Debug.Log("[交互] 模块栏目录 " + cat + (paletteCollapsed.Contains(cat) ? " 收起" : " 展开"));
        RebuildPalette();
    }

    // 收起 / 展开二级目录（数据结构下面的数组 / 栈 / 队列）
    public void TogglePaletteSub(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!paletteCollapsedSubs.Add(key)) paletteCollapsedSubs.Remove(key);
        Debug.Log("[交互] 模块栏二级目录 " + key + (paletteCollapsedSubs.Contains(key) ? " 收起" : " 展开"));
        RebuildPalette();
    }

    static string CatName(string cat)
    {
        switch (cat)
        {
            case "flow": return "流程";
            case "var": return "变量";
            case "datastruct": return "容器";
            case "math": return "运算";
            case "io": return "输入输出";
            case "string": return "文本";
            case "sample": return "样例测试";
            default: return cat;
        }
    }

    // 题栏左边缘的书签拉手：按一下收起 / 展开整栏
    sealed class InspectorTabClick : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            e.Use();
            App.I.ToggleInspectorCollapsed();
        }
    }

    // 二级目录标题（数组 / 栈 / 队列）：点一下收起 / 展开它下面的操作模块
    sealed class PaletteSubClick : MonoBehaviour, IPointerClickHandler
    {
        public string Key;
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            e.Use();
            App.I.TogglePaletteSub(Key);
        }
    }

    // ---------------- 题栏折叠（书签拉手） ----------------

    public const float InspectorTabW = 26f, InspectorTabH = 54f;
    // 题栏竖向范围是「屏幕顶往下 44」到「屏幕底往上 172」（见 Edge 的两个 offset），
    // 这两条边到屏幕中心的偏移平均一下就是 64 —— 拉手的竖直位置，跟窗口多高没关系。
    public const float InspectorTabY = 64f;

    RectTransform InspectorTabRect;
    PixelText inspectorTabArrow;
    bool inspectorCollapsed;
    Coroutine inspectorSlideCo;

    public bool InspectorCollapsed { get { return inspectorCollapsed; } }

    // 当前题栏实际宽度：收起 = 0。题面折行、视口尺寸都按这个走，不再用常量 330。
    public float InspectorWidthNow { get { return InspectorWidthFor(inspectorCollapsed); } }

    // 开合 → 栏宽，和 栏宽 → 拉手锚点偏移，两条都收成纯函数：
    // 拉手永远贴住题栏左缘（收起时题栏宽 0，它就正好贴住屏幕右沿），这两条一旦不一致就是肉眼可见的错位。
    public static float InspectorWidthFor(bool collapsed) { return collapsed ? 0f : InspectorW; }

    public static Vector2 InspectorTabOffset(float w) { return new Vector2(-w, InspectorTabY); }

    void BuildInspectorTab(RectTransform root)
    {
        InspectorTabRect = Panel(root, "InspectorTab", 0, 0, InspectorTabW, InspectorTabH, Theme.A(Theme.Whale, .78f));
        var img = InspectorTabRect.GetComponent<Image>();
        img.raycastTarget = true;
        InspectorTabRect.gameObject.AddComponent<InspectorTabClick>();
        // 外侧那条 2px 深色竖边：拉手和题栏底色接近，压一道边才看得出栏停在哪里
        var edge = Panel(InspectorTabRect, "Edge", 0f, 0f, 2f, InspectorTabH, Theme.WhaleDeep);
        edge.GetComponent<Image>().raycastTarget = false;
        edge.anchorMin = edge.anchorMax = new Vector2(0f, .5f);
        edge.pivot = new Vector2(0f, .5f);
        edge.anchoredPosition = Vector2.zero;
        edge.sizeDelta = new Vector2(2f, InspectorTabH);
        inspectorTabArrow = PixelText.Create(InspectorTabRect, "▶", Theme.Cream);
        inspectorTabArrow.raycastTarget = false;
        var art = inspectorTabArrow.rectTransform;
        art.anchorMin = art.anchorMax = new Vector2(.5f, .5f);
        art.pivot = new Vector2(.5f, .5f);
        art.anchoredPosition = new Vector2(1f, 0f);
        art.sizeDelta = new Vector2(InspectorTabW, InspectorTabH);
        ApplyInspectorFrame(InspectorW);
    }

    // 按「当前栏宽 w」把题栏、画布、右分隔线、拉手四处一起摆好。
    // 只动锚点偏移，不动子节点：窗口比例变了仍旧只拉伸画布，跟原有响应式布局一致。
    void ApplyInspectorFrame(float w)
    {
        if (InspectorRect != null)
        {
            InspectorRect.offsetMin = new Vector2(-w, 172f);
            InspectorRect.offsetMax = new Vector2(0f, -44f);
        }
        if (CanvasHost != null) CanvasHost.offsetMax = new Vector2(-w, -44f);
        var div = uiRoot != null ? FindRect(uiRoot, "DivRight") : null;
        if (div != null)
        {
            div.offsetMin = new Vector2(-w - 2f, 172f);
            div.offsetMax = new Vector2(-w, -44f);
            div.gameObject.SetActive(w > 0f);
        }
        if (InspectorTabRect != null)
        {
            InspectorTabRect.anchorMin = InspectorTabRect.anchorMax = new Vector2(1f, .5f);
            InspectorTabRect.pivot = new Vector2(1f, .5f);
            InspectorTabRect.sizeDelta = new Vector2(InspectorTabW, InspectorTabH);
            InspectorTabRect.anchoredPosition = InspectorTabOffset(w);
        }
        if (inspectorTabArrow != null) inspectorTabArrow.Text = w > InspectorW * .5f ? "▶" : "◀";
        // 视口 / 内容跟着栏宽走，RectMask2D 才裁得到位；内容本身等滑动结束再按新宽度重排
        float h = InspectorRect != null && InspectorRect.rect.height > 1f ? InspectorRect.rect.height : 684f;
        if (inspectorView != null) inspectorView.sizeDelta = new Vector2(w, h);
        if (inspectorContent != null) inspectorContent.sizeDelta = new Vector2(Mathf.Max(1f, w), h);
    }

    // 按一下收起 / 展开：栏宽补间 0.22 秒，滑动过程中只挪框、不动内容，
    // 落地后再按新宽度重排一次题面，折行才跟得上。截图模式（-shot）直接跳到终态。
    public void ToggleInspectorCollapsed()
    {
        inspectorCollapsed = !inspectorCollapsed;
        float from = inspectorCollapsed ? InspectorW : 0f;
        float to = inspectorCollapsed ? 0f : InspectorW;
        if (inspectorSlideCo != null) StopCoroutine(inspectorSlideCo);
        Debug.Log("[交互] 题栏" + (inspectorCollapsed ? "收起" : "展开"));
        if (GraphCanvas != null && GraphCanvas.InstantView)
        {
            ApplyInspectorFrame(to);
            RebuildInspector(selectedNode);
            return;
        }
        inspectorSlideCo = StartCoroutine(Tween.Run(this, .22f,
            k => ApplyInspectorFrame(Mathf.LerpUnclamped(from, to, k)),
            () => { inspectorSlideCo = null; ApplyInspectorFrame(to); RebuildInspector(selectedNode); }));
    }

    // 换关 / 折叠状态变了以后都要走这里：把手柄和视口按当前宽度摆好再填内容
    void BuildInspector()
    {
        if (InspectorRect == null) { RebuildInspector(null); return; }
        // 栏宽按折叠状态取，不读 InspectorRect.rect： Awake 里这一句跑在布局刷新之前，rect 可能还是 0
        float w = InspectorWidthNow, h = InspectorRect.rect.height > 1f ? InspectorRect.rect.height : 684f;
        inspectorView = Panel(InspectorRect, "InspectorView", 0, 0, w, h, new Color(0, 0, 0, 0));
        inspectorView.GetComponent<Image>().raycastTarget = true;
        inspectorView.gameObject.AddComponent<RectMask2D>();
        inspectorScroll = inspectorView.gameObject.AddComponent<PaletteScroll>();
        inspectorContent = Child("InspectorContent", inspectorView);
        inspectorContent.anchorMin = inspectorContent.anchorMax = new Vector2(0, 1);
        inspectorContent.pivot = new Vector2(0, 1);
        inspectorContent.sizeDelta = new Vector2(w, h);
        inspectorScroll.Content = inspectorContent;
        RebuildInspector(null);
    }

    // ---------------- 关卡 ----------------

    public void LoadLevel(string id) { LoadLevel(id, true); }

    // restoreSave=false 只给「重来」用：先把这一关的档删掉，再按关卡自带的 Starter 铺一遍
    public void LoadLevel(string id, bool restoreSave)
    {
        var lv = Levels.All.Find(x => x.Id == id);
        if (lv == null) lv = Levels.All.Count > 0 ? Levels.All[0] : null;
        // 换关前把上一关没落盘的改动强制写完，再丢掉待写快照，绝不会把上一关的图写进新关的档
        SaveSystem.Flush();
        SaveProgressMeta();
        SaveSystem.CancelPending();
        if (previewAnswer) { previewAnswer = false; previewReturn = null; }   // 换关一律退出答案预览
        level = lv;
        if (level == null) { Debug.LogError("[level] 找不到关卡 " + id); return; }

        StopRun();
        HideMenu();          // 任何一次换关都要把菜单收起来，否则画布被盖住
        tipsOpen = false;    // 换关重新折回一句题干
        events.Clear(); eventCursor = 0; playTotal = 0;
        lastRun = null; currentEvent = null;
        lastInput = FirstTestInput();                     // 未运行时变量区先给一个输入预览
        ClearTokens();
        HideHero();

        string notice = null;
        if (GraphCanvas != null)
        {
            // 关卡静态数据一旦被画布就地改脏，重进关卡就变成上一轮的残图，所以永远交一份克隆出去
            var starter = GraphIO.Clone(level.Starter);
            Graph saved = null; string loadErr = null;
            if (restoreSave && !batchSolution && SaveSystem.HasSave(level.Id)) saved = SaveSystem.Load(level.Id, out loadErr);
            // -solution 只在批处理截图时用：把标准答案铺到画布上跑一遍，好让数组/栈/队列看板有内容可验收。
            // 玩家正常启动不带这个参数，进关卡永远只有自己的进度，看不到答案。
            var pick = SaveFlow.PickInitialGraph(starter, level.Solution, saved, loadErr,
                batchSolution && level.Solution != null);
            EnsureSampleBoards(pick.Graph);
            GraphCanvas.ClearHighlight();
            GraphCanvas.LoadGraph(pick.Graph);
            GraphCanvas.ResetHistory();      // 进关/读档都从这张图重新起算历史，读回来的那份不该能撤销掉
            GraphCanvas.FitToContent();
            if (pick.Source == SaveFlow.LoadSource.Saved)
            {
                notice = "已恢复上次进度（" + SaveFlow.TimeText(Updated(level.Id)) + "）· 点「重来」回到初始图";
                Debug.Log("[save] " + level.Id + " 读档恢复，节点 " + pick.Graph.Nodes.Count);
            }
            else if (pick.Source == SaveFlow.LoadSource.StarterAfterBadSave)
            {
                notice = "存档读不出来，已回到本关初始图：" + pick.Error;
                Debug.Log("[save] " + level.Id + " 读档失败，回落 Starter：" + pick.Error);
            }
            SaveFlow.SyncCleared(progress, level.Id, IsCleared(level.Id));
        }

        if (headerLevel != null) SetLines(headerLevel, Fit(level.Chapter + " · " + level.Title, HeaderLevelW, 2), 0f);

        ApplySamplePreview();
        RefreshSampleControls();
        RebuildInspector(null);
        SetStep(0, 0);
        SetVars(null);
        SetMessage(notice ?? "点「运行」看数字流动 · 双击模块改表达式 · 右键删除");
        RefreshTopBarButtons();

        int nodes = GraphCanvas != null && GraphCanvas.CurrentGraph != null ? GraphCanvas.CurrentGraph.Nodes.Count : 0;
        Debug.Log("[level] " + level.Id + " " + level.Chapter + " · " + level.Title + "，节点 " + nodes + "，首个测试输入「" + FirstTestInput() + "」");
    }

    long Updated(string id)
    {
        SaveFlow.Meta m;
        return id != null && progress.TryGetValue(id, out m) && m != null ? m.UpdatedTicks : 0L;
    }

    int CurrentSampleIndex()
    {
        if (GraphCanvas != null && GraphCanvas.CurrentGraph != null)
        {
            var board = GraphCanvas.CurrentGraph.Nodes.Find(n => n != null && n.Type == "sample_input");
            if (board != null)
            {
                int index;
                if (int.TryParse(GraphUtil.Get(board, "group"), out index)) return Mathf.Max(0, index);
            }
        }
        return 0;
    }

    // 样板（公开样例）才允许在样例板上翻；抽查数据不进任何可见的板子，
    // 否则「隐藏测试」就变成挂在右侧栏的公开答案。
    LevelTest CurrentSample()
    {
        var vis = level != null ? LevelBrief.Visible(level) : null;
        if (vis == null || vis.Count == 0) return null;
        return vis[Mathf.Clamp(CurrentSampleIndex(), 0, vis.Count - 1)];
    }

    string FirstTestInput()
    {
        var t = CurrentSample();
        if (t == null && level != null && level.Tests != null) t = level.Tests.Find(x => x.Visible);
        return t != null ? (t.Input ?? "") : "";
    }

    // 把当前样例写进样例板/答案板的参数，卡片底部摘要直接显示输入与期望
    void ApplySamplePreview()
    {
        if (GraphCanvas == null || GraphCanvas.CurrentGraph == null || level == null) return;
        var sample = CurrentSample();
        int wantV = level != null ? LevelBrief.Visible(level).Count : 0;
        int index = Mathf.Clamp(CurrentSampleIndex(), 0, Mathf.Max(0, wantV - 1));
        foreach (var n in GraphCanvas.CurrentGraph.Nodes)
        {
            if (n == null) continue;
            if (n.Type == "sample_input")
            {
                n.Params["group"] = index.ToString();
                n.Params["preview"] = sample != null ? "样板 " + (index + 1) + "/" + wantV + "  " + OneLine(sample.Input) : "没有样例";
            }
            else if (n.Type == "sample_compare")
            {
                n.Params["preview"] = sample != null ? OneLine(sample.Output) : "";
            }
            var card = GraphCanvas.FindCard(n.Uid);
            if (card != null) card.RefreshSummary();
        }
        RefreshSampleSequences();
    }

    // 样例板里直接排出具体数字块：输入序列；答案板排出期望序列和实际序列
    void RefreshSampleSequences()
    {
        sampleChips.Clear();
        expectedChips.Clear();
        actualChips.Clear();
        if (GraphCanvas == null || GraphCanvas.CurrentGraph == null) return;
        var sample = CurrentSample();
        if (sample == null) return;

        foreach (var n in GraphCanvas.CurrentGraph.Nodes)
        {
            if (n == null) continue;
            var card = GraphCanvas.FindCard(n.Uid);
            if (card == null) continue;
            bool isSample = n.Type == "sample_input";
            bool isCompare = n.Type == "sample_compare";
            if (!isSample && !isCompare) continue;

            for (int i = card.transform.childCount - 1; i >= 0; i--)
            {
                var ch = card.transform.GetChild(i);
                if (ch.name.StartsWith("SampleSeq")) Destroy(ch.gameObject);
            }

            if (isSample)
            {
                var head = Label((RectTransform)card.transform, "输入", 8f, 38f, Theme.Ink, 1);
                head.gameObject.name = "SampleSeqHead";
                head.raycastTarget = false;
                float x = 8f;
                foreach (var tok in Tokenize(sample.Input))
                {
                    var chip = CreateSeqChip(card.transform, tok, x, 54f, Theme.Honey, "SampleSeqIn");
                    sampleChips.Add(chip);
                    x += chip.GetComponent<RectTransform>().sizeDelta.x + 4f;
                }
            }
            else
            {
                var headE = Label((RectTransform)card.transform, "期望", 8f, 38f, Theme.Ink, 1);
                headE.gameObject.name = "SampleSeqHeadE";
                headE.raycastTarget = false;
                float xe = 8f;
                foreach (var tok in Tokenize(sample.Output))
                {
                    var chip = CreateSeqChip(card.transform, tok, xe, 54f, Theme.Mint, "SampleSeqExpected");
                    expectedChips.Add(chip);
                    xe += chip.GetComponent<RectTransform>().sizeDelta.x + 4f;
                }

                var headA = Label((RectTransform)card.transform, "实际", 8f, 76f, Theme.Ink, 1);
                headA.gameObject.name = "SampleSeqHeadA";
                headA.raycastTarget = false;
                float xa = 8f;
                foreach (var tok in Tokenize(sample.Output))
                {
                    var chip = CreateSeqChip(card.transform, "?", xa, 92f, Theme.Salt, "SampleSeqActual");
                    actualChips.Add(chip);
                    xa += chip.GetComponent<RectTransform>().sizeDelta.x + 4f;
                }
            }
        }
    }

    GameObject CreateSeqChip(Transform parent, string text, float x, float y, Color color, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        float w = Mathf.Max(22f, PixelText.MeasureString(text, 1).x + 12f);
        rt.sizeDelta = new Vector2(w, 20f);
        rt.anchoredPosition = new Vector2(x, -y);

        var img = go.GetComponent<Image>();
        img.sprite = GraphView.White;
        img.type = Image.Type.Simple;
        img.color = color;
        img.raycastTarget = false;

        var label = PixelText.Create(go.transform, text, Theme.Ink, 1);
        var lrt = label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0f);
        lrt.pivot = new Vector2(0f, 0f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = rt.sizeDelta;
        label.Align = PixelText.TextAlign.Center;
        label.raycastTarget = false;
        return go;
    }

    static void SetChip(GameObject chip, string text, Color color)
    {
        if (chip == null) return;
        var img = chip.GetComponent<Image>();
        if (img != null) img.color = color;
        var label = chip.GetComponentInChildren<PixelText>(true);
        if (label != null) label.Text = text;
    }

    // 老关卡（L2/L3）没有样例板，加载时补上，保证每关都能拖、能连、能判 AC
    void EnsureSampleBoards(Graph g)
    {
        if (g == null) return;
        var sample = g.Nodes.Find(n => n != null && n.Type == "sample_input");
        if (sample == null)
        {
            float maxY = 0f, minX = float.MaxValue;
            foreach (var n in g.Nodes)
            {
                if (n == null) continue;
                maxY = Mathf.Max(maxY, n.Y);
                minX = Mathf.Min(minX, n.X);
            }
            if (minX == float.MaxValue) minX = 40f;
            sample = new GraphNode { Uid = "sample", Type = "sample_input", X = minX, Y = maxY + 90f };
            sample.Params["group"] = "0";
            g.Nodes.Add(sample);
        }

        // 输入模块不再靠样例板的数据线喂值：表达式模式下它自己按 name 写进变量，
        // 没填名字的输入由编译器直接报错，这里替它编一个名字反而会撞掉玩家已有的变量。

        var answer = g.Nodes.Find(n => n != null && n.Type == "sample_compare");
        if (answer == null)
        {
            float maxX = 0f, minY = float.MaxValue;
            foreach (var n in g.Nodes)
            {
                if (n == null) continue;
                maxX = Mathf.Max(maxX, n.X);
                minY = Mathf.Min(minY, n.Y);
            }
            if (minY == float.MaxValue) minY = 200f;
            answer = new GraphNode { Uid = "answer", Type = "sample_compare", X = maxX + 200f, Y = minY + 120f };
            g.Nodes.Add(answer);
        }
        // 对比板原先靠一条黄色数据线接住输出的值；现在直接把它自己标成表达式模式，
        // 编译器就改从运行输出取值（Compiler.IsSampleExprMode 认的就是这一格）
        if (string.IsNullOrEmpty(GraphUtil.Get(answer, "exprMode"))) answer.Params["exprMode"] = "1";
    }

    void RefreshSampleControls()
    {
        if (GraphCanvas == null || GraphCanvas.CurrentGraph == null) return;
        foreach (var n in GraphCanvas.CurrentGraph.Nodes)
        {
            if (n == null || n.Type != "sample_input") continue;
            var card = GraphCanvas.FindCard(n.Uid);
            if (card == null) continue;
            for (int i = card.transform.childCount - 1; i >= 0; i--)
            {
                var ch = card.transform.GetChild(i);
                if (ch.name.StartsWith("SampleCtl")) Destroy(ch.gameObject);
            }

            float h = card.Rect.sizeDelta.y, w = card.Rect.sizeDelta.x;
            // 摆在卡片右侧的空白里：卡片下沿那条横带是下一块板的摘要行（sample_* 的摘要在框上方），
            // 压在卡片下面会和它叠字。
            float ry = Mathf.Max(20f, h - 24f);

            var prev = PixelButton.Create(card.transform, "<", () => ShiftSample(-1), 24, 20);
            prev.gameObject.name = "SampleCtlPrev";
            prev.Rect.anchoredPosition = new Vector2(w + 8f, -ry);

            var next = PixelButton.Create(card.transform, ">", () => ShiftSample(1), 24, 20);
            next.gameObject.name = "SampleCtlNext";
            next.Rect.anchoredPosition = new Vector2(w + 36f, -ry);

            var info = Label((RectTransform)card.transform, "切换样例组", w + 66f, ry, Theme.WhaleDeep, 2);
            info.gameObject.name = "SampleCtlInfo";
            info.rectTransform.sizeDelta = new Vector2(120f, 22f);
            info.Align = PixelText.TextAlign.Left;
        }
    }

    void ShiftSample(int delta)
    {
        int wantV = LevelBrief.Visible(level).Count;
        if (GraphCanvas == null || GraphCanvas.CurrentGraph == null || level == null || wantV == 0) return;
        var board = GraphCanvas.CurrentGraph.Nodes.Find(n => n != null && n.Type == "sample_input");
        if (board == null) return;
        int index = Mathf.Clamp(CurrentSampleIndex() + delta, 0, wantV - 1);
        board.Params["group"] = index.ToString();
        ClearTokens();
        ClearBadges();
        ApplySamplePreview();
        RefreshSampleControls();
        lastInput = FirstTestInput();
        SetStep(0, 0);
        SetVars(null);
        SetMessage("样板 " + (index + 1) + "/" + wantV + "：" + OneLine(lastInput));
        Debug.Log("[sample] 切到第 " + (index + 1) + " 组");
    }

    void OnNodeSelected(GraphNode node)
    {
        selectedNode = node;
        RebuildInspector(node);
    }

    // 点模块旁边的小圆点：切换断点
    void OnBreakpointToggled(GraphNode node)
    {
        if (node == null) return;
        if (!breakpointUids.Add(node.Uid)) breakpointUids.Remove(node.Uid);
        bool on = breakpointUids.Contains(node.Uid);
        GraphCanvas.SetBreakpointDot(node.Uid, on);
        SetMessage(on ? "已设置断点：" + node.Type + "，运行时会在此停下" : "已取消断点：" + node.Type);
        RebuildInspector(node);
        Debug.Log("[breakpoint] " + node.Uid + " -> " + (on ? "on" : "off"));
    }

    // 连线成功：回报一句，玩家才知道刚才那一下真的接上了
    void OnLinkCreated(GraphNode from, GraphNode to)
    {
        var ft = NodeTypes.Get(from.Type);
        var tt = NodeTypes.Get(to.Type);
        string msg = "已连接：" + (ft != null ? ft.Title : from.Type) + " → " + (tt != null ? tt.Title : to.Type);
        SetMessage(msg);
        Toast(msg);
        Debug.Log("[link] " + from.Uid + " → " + to.Uid);
    }

    // 第一次拉线时教一次怎么连
    void OnLinkHintRequested()
    {
        if (linkHintShown) return;
        linkHintShown = true;
        Toast("连线：按住输出端口拖到输入端口松手（也可先点输出、再点输入）");
    }

    void OnNodeDeleteRequested(GraphNode node)
    {
        if (node == null || GraphCanvas == null) return;
        GraphCanvas.DeleteNode(node.Uid);
        ClearBadges();
        RebuildInspector(null);
        Toast("已删除模块：" + node.Type);
    }

    // ---------------- 即时编译（编辑后马上重算，状态栏不挂旧报错） ----------------

    void MarkCompileDirty() { compileDirty = true; }

    // ---------------- 存档与标准答案预览 ----------------

    // 图一改就记一次待写快照，什么时候真落盘由 SaveSystem 的去抖决定；看答案和批跑截图不落玩家档
    void QueueAutoSave()
    {
        RefreshTopBarButtons();
        if (level == null || GraphCanvas == null) return;
        if (!SaveFlow.ShouldAutoSave(previewAnswer, batchSolution)) return;
        SaveFlow.TouchEdited(progress, level.Id, DateTime.UtcNow.Ticks);
        SaveSystem.Request(level.Id, () => GraphCanvas != null ? GraphCanvas.CurrentGraph : null);
    }

    // 进度元信息只在真的落过盘之后才写文件，免得每次编辑都多写一个小文件
    bool SaveProgressMeta()
    {
        string err;
        if (SaveFlow.WriteMeta(SaveSystem.Root, progress, out err)) return true;
        Debug.Log("[save] " + err);
        return false;
    }

    // 把答案铺到画布上之前先收好玩家自己那份，退出预览原样还回去；两份都是克隆，谁也不别名到关卡静态数据
    void ToggleAnswerPreview()
    {
        if (level == null || GraphCanvas == null) { Toast("还没有加载关卡"); return; }
        if (!previewAnswer)
        {
            if (level.Solution == null) { Toast("这一关还没有标准答案"); return; }
            if (!SaveFlow.AnswerUnlocked(Failures(level.Id), SaveFlow.DebugAnswerAlwaysOpen))
            {
                Toast("再错 " + Mathf.Max(0, SaveFlow.AnswerUnlockFailures - Failures(level.Id)) + " 次就能看答案");
                return;
            }
            var mine = GraphCanvas.CurrentGraph;
            previewReturn = mine != null ? GraphIO.Clone(mine) : null;
            SaveSystem.Flush();                       // 玩家这一份先落盘，再铺答案
            SaveProgressMeta();
            SaveSystem.CancelPending();
            var g = SaveFlow.PreviewGraph(level.Solution);
            EnsureSampleBoards(g);
            GraphCanvas.LoadGraph(g);
            GraphCanvas.ResetHistory();
            previewAnswer = true;
            selectedNode = null;
            StopRun();
            ClearTokens();
            ClearBadges();
            GraphCanvas.ClearHighlight();
            GraphCanvas.FitToContent();
            ApplySamplePreview();
            RefreshSampleControls();
            RebuildInspector(null);
            RefreshTopBarButtons();
            Toast("已在画布上铺出标准答案（预览中，不会自动存档）");
            SetMessage("标准答案预览：这一份改了也不会存，点「退出预览」回到自己的图");
            Debug.Log("[answer] " + level.Id + " 预览标准答案，节点 " + (g.Nodes != null ? g.Nodes.Count : 0));
            return;
        }
        ExitAnswerPreview();
    }

    void ExitAnswerPreview()
    {
        if (!previewAnswer || GraphCanvas == null) return;
        var g = SaveFlow.PlayerGraphAfterPreview(previewReturn, level != null ? level.Starter : null);
        previewReturn = null;
        previewAnswer = false;
        EnsureSampleBoards(g);
        GraphCanvas.LoadGraph(g);
        GraphCanvas.ResetHistory();
        selectedNode = null;
        ClearTokens();
        ClearBadges();
        GraphCanvas.ClearHighlight();
        GraphCanvas.FitToContent();
        ApplySamplePreview();
        RefreshSampleControls();
        RebuildInspector(null);
        RefreshTopBarButtons();
        // 回到自己那份之后不再挂起：下一次 GraphChanged 会照常把改动写进玩家这一关的档
        Toast("已退出标准答案预览");
    }

    int Failures(string id)
    {
        SaveFlow.Meta m;
        return id != null && progress.TryGetValue(id, out m) && m != null ? m.Failures : 0;
    }

    // 空画布 / 只拖了一个模块时会报「必须有唯一的开始节点」，这对刚上手的人只是噪音，
    // 运行和提交那条路再认真报。这里挑第一条「真的值得改」的错。
    static CompileError FirstRealError(CompileResult cr)
    {
        if (cr == null || cr.Errors == null) return null;
        foreach (var e in cr.Errors)
            if (e != null && e.Message != null && e.Message.IndexOf("「开始」节点") < 0) return e;
        return null;
    }

    void LiveCompileCheck()
    {
        if (runRoutine != null || submitRoutine != null) return;
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        if (g == null || level == null) return;
        var cr = Compiler.Compile(g);
        var e0 = cr.Ok ? null : FirstRealError(cr);
        if (e0 == null)
        {
            GraphCanvas.ClearHighlight();
            ClearBadges();
            liveError = null;
            SetMessage("这张图能跑通，点「运行」看大肥鱼怎么搬数据");
            return;
        }
        liveError = e0.Message;
        SetMessage("还差一步：" + liveError);
        if (!string.IsNullOrEmpty(e0.NodeUid)) GraphCanvas.HighlightNode(e0.NodeUid, true);
        Debug.Log("[live] 编译失败 " + e0.NodeUid + " " + e0.Message);
    }

    // ---------------- 运算参数弹窗 ----------------

    public void OpenOperatorEditor(GraphNode node)
    {
        if (node == null || node.Type != "binop" || GraphCanvas == null) return;
        ExprEditorUI.Open(this, node);
    }

    // 双击模块：运算块进表达式工作台；条件分支 / 两种循环 / 输出 / 计数循环直接进第一个表达式的浮窗
    // （不用再移鼠标去点卡片上那条框）；创建变量开统一参数窗落在名字上，赋值只有一条语句，直接落在语句上。
    void OnNodeEditRequested(GraphNode node)
    {
        if (node == null || GraphCanvas == null) return;
        if (node.Type == "binop") { OpenOperatorEditor(node); return; }
        if (node.Type == "vardecl" || node.Type == "varset")
        {
            // 创建变量永远先落在名字上（数组卡也一样），赋值只有那一条语句
            string focus = SaveFlow.PreferredFocusKey(node.Type);
            Debug.Log("[交互] 双击 " + node.Type + " 卡片 → 开参数窗并聚焦 " + focus);
            OpenNodeParams(node, focus);
            return;
        }
        if (NodeCard.DblClickOpensExpr(node.Type))
        {
            var card = GraphCanvas.FindCard(node.Uid);
            if (card != null && card.TryOpenFirstExprField())
            {
                Debug.Log("[交互] 双击 " + node.Type + " 卡片 → 直接进第一个表达式浮窗");
                return;
            }
        }
        Debug.Log("[交互] 双击 " + node.Type + " 卡片 → 开参数窗并聚焦第一个字段");
        OpenNodeParams(node);
    }

    // -paramsdemo 用的取样：优先 vardecl（既有文本框又有类型按钮排），都没有就挑第一个能弹开窗的模块
    void ParamsDemo()
    {
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        if (g == null) { Debug.Log("[demo] -paramsdemo 当前没有图"); return; }
        var target = g.Nodes.Find(n => n != null && n.Type == (HasArg("-structname") ? "structnew" : "vardecl"));
        if (target == null) target = g.Nodes.Find(n => n != null && BuildParamSpecs(n).Count > 0);
        if (target == null) { Debug.Log("[demo] -paramsdemo 没找到可开参数窗的模块"); return; }
        OpenNodeParams(target);
        Debug.Log("[demo] -paramsdemo 弹出 " + target.Type + "(" + target.Uid + ") 的参数悬浮窗");
    }

    // -copydemo 用的取样：挑两个相连的模块走一遍「复制 → 粘贴」，日志里给出前后模块数
    void CopyPasteDemo()
    {
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        if (g == null) { Debug.Log("[demo] -copydemo 当前没有图"); return; }
        var pick = g.Nodes.FindAll(n => n != null && (n.Type == "vardecl" || n.Type == "input"));
        if (pick.Count < 2) { Debug.Log("[demo] -copydemo 图里可复制的模块不足 2 个"); return; }
        int before = g.Nodes.Count, beforeLinks = g.Links.Count;
        for (int i = 0; i < 2; i++)
        {
            var c = GraphCanvas.FindCard(pick[i].Uid);
            if (c != null) { c.Selected = true; c.SetSelected(true); }
        }
        int k = GraphCanvas.CopySelected();
        int p = GraphCanvas.PasteClipboard();
        Debug.Log("[demo] -copydemo 复制 " + k + " 个 → 粘贴 " + p + " 个，模块 " + before + "→" + g.Nodes.Count
            + "，连线 " + beforeLinks + "→" + g.Links.Count);
        Toast("已粘贴 " + p + " 个模块");
    }

    // -palettefold 用的取样：把第一个一级目录折起来，看 [+] 与子项收起
    void PaletteFoldDemo()
    {
        string cat = null;
        foreach (var n in NodeCatalog.All)
            if (!string.IsNullOrEmpty(n.Cat)) { cat = n.Cat; break; }
        if (cat == null) return;
        TogglePaletteGroup(cat);
        Debug.Log("[demo] -palettefold 折叠目录 " + cat);
    }

    void OpenNodeParams(GraphNode node, string focusKey = null)    {
        var specs = BuildParamSpecs(node);
        if (specs.Count == 0) { Toast("这个模块没有需要填的参数"); return; }
        var card = GraphCanvas.FindCard(node.Uid);
        // 落点由 SaveFlow 那个纯函数定：点名的格子在这张表里就落它，不在（数组卡被剔掉的初始值）
        // 就落这一类模块第一个该填的空，绝不落成一个浮窗里根本不存在的键
        var textKeys = new List<string>();
        foreach (var s in specs) if (!s.IsChoice) textKeys.Add(s.Key);
        focusKey = SaveFlow.PickPopupFocusKey(node.Type, focusKey, textKeys);
        // 验收 #117：不管从卡片、折叠区还是卡上表达式区进来，浮窗最终落在哪一格都由这一行自证
        Debug.Log("[focus] " + node.Type + " 浮窗落点=" + (focusKey ?? "-"));
        NodeParamsUI.Open(uiRoot, card != null ? card.Rect : null,
            NodeCard.TitleFor(node) + " · 参数", specs,
            values =>
            {
                var exprKeys = NodeCard.ExprFieldKeys(node.Type);
                bool wasArray = NodeCard.IsArrayDecl(node);
                foreach (var kv in values)
                {
                    // 赋值只有一格：整句话要拆成「名字 + 语句」两个参数，不能再走表达式提交那条
                    if (node.Type == "varset" && kv.Key == "valueExpr") { VarsetApply(node, kv.Value); continue; }
                    // 栈名 / 队列名是容器的身份：改 Id 并回写全图，改不动就原样留着旧名
                    if (kv.Key == StructNameKey)
                    {
                        if (StructRename(node, kv.Value)) RefreshStructCards();
                        continue;
                    }
                    // 表达式字段必须走卡片那条提交路径：它管着「初始值从空到有」时的卡片高度重建
                    if (exprKeys != null && System.Array.IndexOf(exprKeys, kv.Key) >= 0)
                    {
                        var ec = GraphCanvas.FindCard(node.Uid);
                        if (ec != null) { ec.CommitExpr(kv.Key, kv.Value); continue; }
                    }
                    node.Params[kv.Key] = kv.Value;
                }
                var c = GraphCanvas.FindCard(node.Uid);
                // 类型在标量和数组之间换了边，或者创建变量改了名字 / 长度、赋值改了语句：
                // 卡面文字长短变了，整块要重画，原地重建这张卡
                if (c != null && (wasArray != NodeCard.IsArrayDecl(node) || node.Type == "vardecl" || node.Type == "varset"))
                    GraphCanvas.RebuildCard(node);
                else if (c != null) { c.SyncExprFields(); c.RefreshSummary(); }
                // 改名 / 循环变量名会改变其它模块能看见的变量，候选列表要跟着重算
                GraphCanvas.RefreshExprVariables();
                if (selectedNode == node) RebuildInspector(node);
                MarkCompileDirty();
            }, focusKey);
    }

    // 卡面上自己就带着中文标签显示出来的表达式字段。
    // 这些字段按 3.50 的约定不做进参数浮窗（上面 BuildParamSpecs 会跳过），于是右侧栏
    // 「没被参数表列过就复述一遍」的兜底会把它们当成陌生字段再写一次「表达式：x」，
    // 玩家看到的就是同一份起点 / 终点出现两遍。这里补一份认账。
    public static bool CardLabeledExpr(GraphNode node, string key)
    {
        if (node == null || string.IsNullOrEmpty(key)) return false;
        if (node.Type == "forloop") return key == "fromExpr" || key == "toExpr";
        // 数组的初始值直接画成卡面上的字面量，浮窗里又被「长度」顶掉，同样不再复述
        if (node.Type == "vardecl" && key == "initExpr") return NodeCard.IsArrayDecl(node);
        return false;
    }

    // 界面上「点一下就改一个参数」的入口：计数循环的「顺序」开关走这里。
    // 参数窗那条整表提交对这种单格开关太重（会重建整块卡），这里只写一格 + 刷新卡面读数。
    public void SetNodeParam(GraphNode node, string key, string value)
    {
        if (node == null || string.IsNullOrEmpty(key)) return;
        node.Params[key] = value ?? "";
        var c = GraphCanvas == null ? null : GraphCanvas.FindCard(node.Uid);
        if (c != null) { c.SyncExprFields(); c.RefreshSummary(); }
        if (selectedNode == node) RebuildInspector(node);
        MarkCompileDirty();
    }

    // 计数循环里「另有编辑面」的参数：表达式字段在卡面上（双击那两行改），顺序开关在小窗头部。
    // 参数窗只留循环变量名这一格。
    public static bool LoopHiddenFromPopup(string id, bool isExpr)
    {
        return isExpr || id == "dir";
    }

    // 参数表由节点类型自己声明：text 给输入框，enum 给一排互斥按钮。
    // 表达式字段（*Expr）也一并列进来——玩家双击模块就能一次把「名字 / 类型 / 初始值表达式」全填完，
    // 不用再回到卡片上找那条几乎看不出来的表达式区。旧运算工作台（binop）的 expr / formula / varA / varB 仍走它自己的弹窗。
    List<NodeParamsUI.Spec> BuildParamSpecs(GraphNode node)
    {
        var list = new List<NodeParamsUI.Spec>();
        var t = NodeTypes.Get(node.Type);
        if (t == null) return list;
        // 「赋值」就一条语句一个空：名字从语句左边现推，类型跟着创建变量的声明走，
        // 再摆「名字 / 类型 / 值表达式」三格就成了创建变量的第二个编辑面
        if (node.Type == "varset")
        {
            list.Add(new NodeParamsUI.Spec
            {
                Key = "valueExpr",
                Label = "赋值语句",
                Value = VarsetDisplay(node),
                IsExpr = true,
                Hint = "s = x / s += x / a[i] = x；点下面的名字把它填进来",
                Picks = UsableVarNames(node),
            });
            return list;
        }
        var exprKeys = NodeCard.ExprFieldKeys(node.Type);
        foreach (var p in t.Params)
        {
            if (p.Id == "expr" || p.Id == "formula" || p.Id == "varA" || p.Id == "varB") continue;
            // 数组的「长度」和标量的「初始值」互斥：不是数组就别把长度摆出来，是数组就别把初始值摆出来，
            // 免得玩家两个都填、再被编译器劝回来
            if (node.Type == "vardecl")
            {
                bool arr = NodeCard.IsArrayDecl(node);
                if (p.Id == "size" && !arr) continue;
                if (p.Id == "initExpr" && arr) continue;
            }
            bool isExpr = p.Id.EndsWith("Expr", StringComparison.Ordinal);
            // 计数循环的参数窗只留「循环变量」一格：起点 / 终点写在卡面三行里、双击那两行进小窗改，
            // 顺序做在表达式小窗的头部（ExpressionFieldUI.BuildDirSwitch）。
            // 参数窗再摆一遍就是同一份内容的第二个编辑面，玩家会以为模块画重了。
            if (node.Type == "forloop" && LoopHiddenFromPopup(p.Id, isExpr)) continue;
            // 卡片上没有这条表达式字段的（比如旧图里的遗留参数）不进弹窗，避免填了不生效
            if (isExpr && (exprKeys == null || System.Array.IndexOf(exprKeys, p.Id) < 0)) continue;
            var s = new NodeParamsUI.Spec
            {
                Key = p.Id,
                Label = p.Label,
                Value = GraphUtil.Get(node, p.Id) ?? (p.Default ?? ""),
                IsExpr = isExpr,
                Hint = isExpr ? NodeCard.ExprFieldContext(node.Type, p.Id) : null,
            };
            if (p.Id == "size") s.Hint = "按默认值铺满 N 格，与初始值二选一";
            // 输入的目标可以写 a，也可以写 a[i]：循环里一格一格读进数组靠的就是它
            if (node.Type == "input" && p.Id == "name") s.Hint = "写 a 存进变量；写 a[i] 存进数组第 i 格";
            var opts = p.Options as string[];
            if (p.Id == "structId") opts = StructIds();
            if (p.Kind == "enum" && opts != null && opts.Length > 0)
            {
                s.Choices = opts;
                if (p.Id == "vtype") s.ChoiceText = NodeTypes.TypeNameCn;
                else if (p.Id == "op") s.ChoiceText = OpLabelCn;
            }
            list.Add(s);
        }
        // 创建栈 / 创建队列：这一格改的是容器的「身份」而不是称呼——表达式里 st.top 的那个 st 就是它，
        // 所以只收英文标识符。名字只有一份：卡面、看板、表达式、出的 C++ 代码都读同一个 Id。
        if (node.Type == "structnew")
        {
            var def = FindStructDef(node);
            list.Add(new NodeParamsUI.Spec
            {
                Key = StructNameKey,
                Label = (def != null && def.Kind == "queue" ? "队列名" : "栈名"),
                Value = def != null ? (def.Id ?? "") : "",
                Hint = "表达式里就写这个名字，如 st.top 的 st",
            });
        }
        return list;
    }

    // 「栈名 / 队列名」这一格的键名：不落到 node.Params，而是改结构实例的 Id —— 表达式里的那个名字
    public const string StructNameKey = "structName";

    StructDef FindStructDef(GraphNode node)
    {
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        return StructDefOf(g, GraphUtil.Get(node, "structId"));
    }

    public static StructDef StructDefOf(Graph g, string id)
    {
        if (g == null || g.Structs == null || string.IsNullOrEmpty(id)) return null;
        return g.Structs.Find(s => s != null && s.Id == id);
    }

    // 实例名要能直接打进表达式，所以只认标识符：字母或下划线开头，后面接字母 / 数字 / 下划线。
    // 中文与符号判非法（返回空串）让调用方拒绝，而不是悄悄删掉——「一摞数」被删成空串，
    // 玩家会以为名字没存上。
    public static string StructIdFor(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "";
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '_' && !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z')) return "";
        }
        return s[0] >= '0' && s[0] <= '9' ? "" : s;
    }

    // 改名前的一整套查重：返回空串表示可以用，否则返回「为什么不能用」的一句话。
    // 容器、变量、循环变量在表达式里是同一个名字空间，所以三处都要问一遍。
    public static string StructIdConflict(Graph g, StructDef self, string id)
    {
        if (g == null || string.IsNullOrEmpty(id)) return "";
        foreach (var s in g.Structs)
            if (s != null && s != self && s.Id == id) return "已经有另一个容器叫「" + id + "」";
        foreach (var n in g.Nodes)
        {
            if (n == null) continue;
            if (n.Type == "vardecl" || n.Type == "input")
            {
                if (BaseName(GraphUtil.Get(n, "name")) == id) return "变量「" + id + "」已经占了这个名字";
            }
            else if ((n.Type == "forloop" || n.Type == "whileloop") && VarScope.IndexName(n) == id)
                return "循环变量「" + id + "」已经占了这个名字";
        }
        return "";
    }

    // 改实例名 = 改结构实例的 Id，再把全图引用它的 structId 参数一起换掉：只改 Id 不改参数，
    // 压入 / 弹出卡还指着旧名，编译立刻报「还没有这个结构实例」。
    public bool StructRename(GraphNode node, string raw)
    {
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        if (g == null || g.Structs == null) return false;
        var def = StructDefOf(g, GraphUtil.Get(node, "structId"));
        if (def == null) return false;
        var id = StructIdFor(raw);
        if (id.Length == 0) { Toast("这个名字在表达式里打不出来：只能用英文字母、数字和下划线，且不能以数字开头"); return false; }
        if (id == def.Id) return true;
        var why = StructIdConflict(g, def, id);
        if (why.Length > 0) { Toast(why); return false; }
        var old = def.Id;
        def.Id = id;
        // 称呼不留第二份：卡片与看板在 Name 为空时回落到 Id，留着旧称呼会出现两个名字
        def.Name = null;
        foreach (var n in g.Nodes)
            if (n != null && GraphUtil.Get(n, "structId") == old) n.Params["structId"] = id;
        Debug.Log("[实例名] " + old + " → " + id);
        return true;
    }

    // 改名后卡面那行「栈 · 名字」长短变了：只刷文字会把长名塞进原来的小菱形，
    // 所以引用这个容器的卡整块重建（RebuildCard 会按新宽度重挑形状）。
    // 看板按实例编号认人，编号换了旧看板就成了孤儿，先全部收起，下一次执行事件自会按新名重建。
    void RefreshStructCards()
    {
        GraphCanvas.RefreshStructLabels();
        GraphCanvas.ShowStacks(null, null);
        var g = GraphCanvas.CurrentGraph;
        if (g == null || g.Nodes == null) return;
        foreach (var n in g.Nodes)
        {
            if (n == null) continue;
            var t = n.Type ?? "";
            if (t == "structnew" || t.StartsWith("stack_") || t.StartsWith("queue_"))
                GraphCanvas.RebuildCard(n);
        }
    }

    // a[i] 这种写入目标只取头里的名字，否则 "a[0]" 认不出它写的是变量 a
    static string BaseName(string target)
    {
        var s = (target ?? "").Trim();
        int i = s.IndexOf('[');
        return i < 0 ? s : s.Substring(0, i).Trim();
    }

    // 「赋值」面板 / 卡片显示的同一条语句：已经是一句就原样给（"s += 5" 不能显示成脱糖后的
    // "s = s + 5"，玩家会以为模块画重了）；旧图那种「名字 + 表达式」两段分开存的拼成一句。
    public static string VarsetDisplay(GraphNode node)
    {
        if (node == null) return "";
        var src = (GraphUtil.Get(node, "valueExpr") ?? "").Trim();
        if (Assign.TryParse(src) != null) return src;
        var name = (GraphUtil.Get(node, "name") ?? "").Trim();
        if (name.Length == 0) return src;
        return src.Length == 0 ? name + " = " : name + " = " + src;
    }

    // 一句话拆回模块的两个参数：能解析成赋值就按左边记名字，解析不了（写到一半）
    // 就原样存着、名字不动——报错的活交给编译器，面板这边不替玩家猜。
    public static void VarsetApply(GraphNode node, string raw)
    {
        if (node == null) return;
        var s = (raw ?? "").Trim();
        if (s.Length == 0) { node.Params["name"] = ""; node.Params["valueExpr"] = ""; return; }
        var asg = Assign.TryParse(s);
        node.Params["valueExpr"] = s;
        node.Params["name"] = asg != null ? asg.Name : (GraphUtil.Get(node, "name") ?? "");
    }

    // 参数窗里「目前能用的变量」：按这个模块执行到时可见的变量算，循环体里能看见循环变量，
    // 排在创建变量之前的赋值就只有前面那几个
    string[] UsableVarNames(GraphNode node)
    {
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        if (g == null || node == null) return null;
        var list = new List<string>();
        foreach (var v in NodeCard.BuildVariables(g, node.Uid))
            if (v != null && !string.IsNullOrEmpty(v.Name) && !list.Contains(v.Name)) list.Add(v.Name);
        if (list.Count == 0) return null;
        list.Sort(StringComparer.Ordinal);
        return list.ToArray();
    }

    string[] StructIds()
    {
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        if (g == null || g.Structs == null || g.Structs.Count == 0) return null;
        var ids = new List<string>();
        foreach (var s in g.Structs) if (s != null && !ids.Contains(s.Id)) ids.Add(s.Id);
        return ids.ToArray();
    }

    static string OpLabelCn(string op)
    {
        switch (op)
        {
            case "and": return "并且 (and)";
            case "or": return "或者 (or)";
            case "not": return "取反 (not)";
        }
        return op;
    }

    public void OnExpressionApplied(GraphNode node)
    {
        if (node == null) return;
        var card = GraphCanvas != null ? GraphCanvas.FindCard(node.Uid) : null;
        if (card != null) card.RefreshSummary();
        if (selectedNode == node) RebuildInspector(node);
        ClearBadges();
        Toast("表达式已保存：" + (GraphUtil.Get(node, "formula") ?? ""));
    }

    // 运算工作台预览用：取最近一次运行里同名变量的值
    public Val PreviewVar(string name)
    {
        if (lastRun != null && lastRun.Events != null)
        {
            for (int i = lastRun.Events.Count - 1; i >= 0; i--)
            {
                var ev = lastRun.Events[i];
                if (ev == null || ev.Vars == null || !ev.Vars.ContainsKey(name)) continue;
                object o = ev.Vars[name];
                if (o is bool) return Val.Bool((bool)o);
                if (o is long) return Val.Int((long)o);
                if (o is int) return Val.Int((int)o);
                if (o is double) return Val.Double((double)o);
                if (o is char) return Val.Char((char)o);
                if (o is string) return Val.Str((string)o);
            }
        }
        return Val.Int(0);
    }

    IEnumerator ShowOperatorEditor()
    {
        var parent = GuideLayer != null ? GuideLayer : (RectTransform)transform;
        var root = Child("OperatorModal", parent);
        Stretch(root);
        operatorModal = root.gameObject;

        var dim = root.gameObject.AddComponent<Image>();
        dim.sprite = White;
        dim.type = Image.Type.Simple;
        dim.color = new Color(0f, 0f, 0f, .42f);
        dim.raycastTarget = true;

        var cg = root.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        var panel = Child("Panel", root);
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
        panel.pivot = new Vector2(.5f, .5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(520f, 360f);
        panel.localScale = Vector3.one * .94f;
        var pimg = panel.gameObject.AddComponent<Image>();
        pimg.sprite = White;
        pimg.type = Image.Type.Simple;
        pimg.color = Theme.Paper;
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = Theme.Border;
        outline.effectDistance = new Vector2(2f, -2f);

        Label(panel, "编辑运算", 18f, 12f, Theme.Ink, 2);
        var close = PixelButton.Create(panel, "取消", CloseOperatorEditor, 64, 24);
        close.Rect.anchoredPosition = new Vector2(438f, -10f);
        Label(panel, "选择已有变量，保存后卡片上直接显示公式", 18f, 36f, Theme.Border, 1);

        var vars = new List<string>();
        var types = new Dictionary<string, string>();
        foreach (var n in GraphCanvas.CurrentGraph.Nodes)
        {
            if (n == null || n.Type != "vardecl") continue;
            var name = GraphUtil.Get(n, "name");
            if (string.IsNullOrEmpty(name) || vars.Contains(name)) continue;
            vars.Add(name);
            types[name] = GraphUtil.Get(n, "vtype") ?? "int";
        }

        Label(panel, "可用变量（" + vars.Count + "）", 18f, 58f, Theme.Border, 1);
        int rows = Mathf.Min(5, vars.Count);
        for (int i = 0; i < rows; i++)
        {
            string name = vars[i];
            int index = i;
            float ry = 80f + i * 26f;
            Label(panel, name + " : " + types[name], 20f, ry, Theme.Ink, 1);
            var a = PixelButton.Create(panel, "设为 A", () => { editVarA = name; SelectVarButton(index, true); }, 58, 22, editVarA == name);
            a.Rect.anchoredPosition = new Vector2(338f, -ry);
            editABtns.Add(a);
            var b = PixelButton.Create(panel, "设为 B", () => { editVarB = name; SelectVarButton(index, false); }, 58, 22, editVarB == name);
            b.Rect.anchoredPosition = new Vector2(404f, -ry);
            editBBtns.Add(b);
        }
        if (vars.Count == 0)
            Label(panel, "还没有变量，请先在画布上创建变量模块", 20f, 82f, Theme.Coral, 1);

        float y = 86f + Mathf.Max(1, rows) * 26f;
        editSelA = Label(panel, "", 18f, y, Theme.Ink, 1);
        editSelB = Label(panel, "", 266f, y, Theme.Ink, 1);
        y += 26f;

        Label(panel, "运算符", 18f, y, Theme.Border, 1);
        string[] ops = { "+", "-", "*", "/", "%" };
        for (int i = 0; i < ops.Length; i++)
        {
            string op = ops[i];
            int index = i;
            var ob = PixelButton.Create(panel, op, () => { editOp = op; SelectOpButton(index); }, 34, 22, editOp == op);
            ob.Rect.anchoredPosition = new Vector2(84f + i * 42f, -y);
            editOpBtns.Add(ob);
        }
        y += 32f;

        editFormula = Label(panel, "", 18f, y, Theme.Ink, 1);
        editFormula.rectTransform.sizeDelta = new Vector2(480f, 18f);

        var save = PixelButton.Create(panel, "保存并关闭", ApplyOperatorEditor, 100, 26, true);
        save.Rect.anchoredPosition = new Vector2(18f, -314f);

        RefreshOperatorPreview();

        float t = 0f;
        while (t < .18f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / .18f);
            cg.alpha = k;
            panel.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }
        cg.alpha = 1f;
        panel.localScale = Vector3.one;
    }

    void SelectVarButton(int index, bool forA)
    {
        var list = forA ? editABtns : editBBtns;
        for (int i = 0; i < list.Count; i++) if (list[i] != null) list[i].SetPrimary(i == index);
        RefreshOperatorPreview();
    }

    void SelectOpButton(int index)
    {
        for (int i = 0; i < editOpBtns.Count; i++) if (editOpBtns[i] != null) editOpBtns[i].SetPrimary(i == index);
        RefreshOperatorPreview();
    }

    void RefreshOperatorPreview()
    {
        string a = string.IsNullOrEmpty(editVarA) ? "未选择" : editVarA;
        string b = string.IsNullOrEmpty(editVarB) ? "未选择" : editVarB;
        if (editSelA != null) editSelA.Text = "A = " + a;
        if (editSelB != null) editSelB.Text = "B = " + b;
        if (editFormula != null) editFormula.Text = "公式： " + a + " " + editOp + " " + b;
    }

    void ApplyOperatorEditor()
    {
        if (operatorNode == null) { CloseOperatorEditor(); return; }
        operatorNode.Params["varA"] = editVarA ?? "";
        operatorNode.Params["varB"] = editVarB ?? "";
        operatorNode.Params["op"] = string.IsNullOrEmpty(editOp) ? "+" : editOp;
        var card = GraphCanvas.FindCard(operatorNode.Uid);
        if (card != null) card.RefreshSummary();
        RebuildInspector(operatorNode);
        Toast("运算公式已保存：" + (editVarA ?? "") + " " + editOp + " " + (editVarB ?? ""));
        CloseOperatorEditor();
    }

    void CloseOperatorEditor()
    {
        if (operatorModal == null) return;
        var root = operatorModal;
        operatorModal = null;
        StartCoroutine(HideOperatorEditor(root));
    }

    IEnumerator HideOperatorEditor(GameObject root)
    {
        var cg = root.GetComponent<CanvasGroup>();
        var panel = root.transform.Find("Panel") as RectTransform;
        float t = 0f;
        while (t < .14f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / .14f);
            if (cg != null) cg.alpha = 1f - k;
            if (panel != null) panel.localScale = Vector3.one * Mathf.Lerp(1f, .95f, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }
        Destroy(root);
    }

    // 检查器：题干 / 输入格式 / 样例 / 任务 / 提示 + 选中节点的只读参数
    // 正文明码统一提到 2 号、颜色统一压深（原来的 Border 在纸色底上对比只有 2:1，读起来很吃力）
    void RebuildInspector(GraphNode sel)
    {
        var host = inspectorContent != null ? inspectorContent : InspectorRect;
        if (host == null) return;
        for (int i = host.childCount - 1; i >= 0; i--)
        {
            var go = host.GetChild(i).gameObject;
            go.SetActive(false);                             // 立即隐藏，避免 Destroy 延迟一帧导致重影
            Destroy(go);
        }
        if (inspectorScroll != null) inspectorScroll.SetExtent(0f);

        float y = 10f;
        // 收起时栏宽为 0，内容已经在屏幕外：折行仍按满宽算，不然 0 宽会把整段题面折成一屏单字
        float wrap = (inspectorCollapsed ? InspectorW : InspectorWidthNow) - 20f;
        if (level != null)
        {
            // 题面按真正算法题的版式排：背景故事 → 题干与数据范围 → 一组输入配一组输出的样例 → 提示（默认收起）。
            // 每段前挂一个小标题，长题面靠右栏滚轮看，不再把所有内容挤成一句 Brief。
            y = PutInspectorLine(level.Chapter, y, Theme.WhaleDeep, 1, 0) + 2f;
            y = PutInspectorLine(level.Title, y, Theme.Ink, 3, 0) + 6f;
            // 入口贴在标题下面：题面连着故事、数据范围、几组样例，全展开得两千来像素高，
            // 原先按钮排在末尾，玩家要一路滚到底才点得到——一个「看答案」的按钮不该藏这么深。
            // 答案入口是玩家主动点的按钮，不是进关就摊开的图：按钮常开 ≠ 答案常显
            if (previewAnswer)
            {
                var back = PixelButton.Create(host, "退出预览", ExitAnswerPreview, 0, 30, true, 2);
                back.Rect.anchoredPosition = new Vector2(10f, -y);
                answerBtnRect = back.Rect;
                y += 40f;
                y = PutInspectorLine("画布上是本关的标准答案，改它不会存进你的进度", y, Theme.Coral, 2, wrap) + 2f;
            }
            else if (level.Solution != null && SaveFlow.AnswerUnlocked(Failures(level.Id), SaveFlow.DebugAnswerAlwaysOpen))
            {
                var see = PixelButton.Create(host, "查看标准答案", ToggleAnswerPreview, 0, 30, false, 2);
                see.Rect.anchoredPosition = new Vector2(10f, -y);
                answerBtnRect = see.Rect;
                y += 40f;
            }
            if (!string.IsNullOrEmpty(level.Story))
            {
                y = PutInspectorLine("背景故事", y, Theme.GrapeDeep, 2, 0) + 2f;
                y = PutInspectorLine(level.Story, y, Theme.Ink, 2, wrap) + 6f;
            }
            y = PutInspectorLine("题干", y, Theme.HoneyDeep, 2, 0) + 2f;
            y = PutInspectorLine(!string.IsNullOrEmpty(level.Problem) ? level.Problem : level.Brief, y, Theme.Ink, 2, wrap) + 2f;
            if (!string.IsNullOrEmpty(level.DataRange))
                y = PutInspectorLine("数据范围", y, Theme.HoneyDeep, 2, 0) + 2f;
            y = PutInspectorLine(!string.IsNullOrEmpty(level.DataRange) ? level.DataRange : "输入：" + level.InputHint,
                                 y, Theme.WhaleDeep, 2, wrap) + 6f;

            // 标题与正文之间空一整行（PutInspectorLine 遇空串不占高，间距只能靠 y 偏移）；
            // 一组输出到下一组输入再翻一倍，一眼分清哪段输入配哪段输出。
            float sampleLine = PixelText.CellH * 2f;
            var secs = LevelBrief.SampleSections(level);
            for (int i = 0; i < secs.Count; i++)
            {
                var sec = secs[i];
                if (string.IsNullOrEmpty(sec.Body)) continue;       // P0 这类没有输入的关不挂空标题
                y = PutInspectorLine(sec.Heading, y, Theme.GrapeDeep, 2, 0) + sampleLine;
                y = PutInspectorLine(sec.Body, y, Theme.Ink, 2, wrap)
                    + (i == secs.Count - 1 ? 4f : (sec.GroupEnd ? sampleLine * 2f : sampleLine));
            }
            // 判定口径：样板公开、抽查不公开。序章第一关整段讲清，其它关只剩一行读数。
            y = PutInspectorLine(LevelBrief.JudgeLine(level), y, Theme.WhaleDeep, 2, wrap) + 2f;
            if (level.TeachJudge)
                y = PutInspectorLine("公开给你的只是几条好不好看的样板。点「提交」还会拿一批看不见的抽查数据跑一遍，"
                                     + "全部对得上才算合格——跟真正的题库一样。", y, Theme.Coral, 2, wrap) + 4f;
            y += 2f;
            // 进关只摊开题面：目标和做法都折在「看提示」后面，玩家自己点开
            bool hasHelp = (level.Tips != null && level.Tips.Count > 0) || (level.Goals != null && level.Goals.Count > 0);
            if (hasHelp)
            {
                var tipsBtn = PixelButton.Create(host, tipsOpen ? "收起提示" : "看提示",
                    () => { tipsOpen = !tipsOpen; RebuildInspector(selectedNode); }, 132, 30, false, 2);
                tipsBtn.Rect.anchoredPosition = new Vector2(10f, -y);
                y += 40f;
                if (tipsOpen)
                {
                    if (level.Goals != null && level.Goals.Count > 0)
                    {
                        y = PutInspectorLine("目标", y, Theme.HoneyDeep, 2, 0) + 2f;
                        foreach (var goal in level.Goals)
                            y = PutInspectorLine("· " + goal, y, Theme.Ink, 2, wrap);
                        y += 4f;
                    }
                    if (level.Tips != null && level.Tips.Count > 0)
                    {
                        y = PutInspectorLine("提示", y, Theme.GrapeDeep, 2, 0) + 2f;
                        foreach (var tip in level.Tips)
                            y = PutInspectorLine("· " + tip, y, Theme.WhaleDeep, 2, wrap);
                        y += 6f;
                    }
                }
            }
            y = PutInspectorLine("选中模块", y, Theme.WhaleDeep, 2, 0) + 2f;
        }

        if (sel == null)
        {
            y = PutInspectorLine("点画布上的节点看它的参数；双击节点直接改表达式，名字和类型用下面的「编辑参数」", y, Theme.WhaleDeep, 2, wrap);
            FinishInspector(y);
            return;
        }

        var nt = NodeTypes.Get(sel.Type);
        y = PutInspectorLine(nt != null ? nt.Title : sel.Type, y, Theme.Ink, 2, 0) + 2f;
        y = PutInspectorLine("类型：" + sel.Type, y, Theme.WhaleDeep, 2, 0) + 2f;
        if (breakpointUids.Contains(sel.Uid))
            y = PutInspectorLine("● 已设置断点", y, Theme.HoneyDeep, 2, 0) + 2f;

        // 右侧栏这颗按钮固定开参数窗：双击模块现在会先奔第一个表达式浮窗，
        // 想改名字 / 类型 / 循环变量名，从这里进。
        var edit = PixelButton.Create(host, "编辑参数", () => OpenNodeParams(sel), 140, 30, true, 2);
        edit.Rect.anchoredPosition = new Vector2(10f, -y);
        y += 40f;
        y = PutInspectorLine("双击画布上的这个模块：直接进它的第一个表达式", y, Theme.WhaleDeep, 2, wrap) + 6f;

        if (sel.Type == "sample_input")
        {
            y = PutInspectorLine("样例组", y, Theme.WhaleDeep, 2, wrap) + 2f;
            var prev = PixelButton.Create(host, "上一组", () => ShiftSample(-1), 92, 30, false, 2);
            prev.Rect.anchoredPosition = new Vector2(10f, -y);
            var next = PixelButton.Create(host, "下一组", () => ShiftSample(1), 92, 30, false, 2);
            next.Rect.anchoredPosition = new Vector2(110f, -y);
            y += 38f;
        }

        int shown = 0;
        var specs = BuildParamSpecs(sel);
        var listed = new System.Collections.Generic.HashSet<string>();
        foreach (var s in specs) listed.Add(s.Key);
        foreach (var s in specs)
        {
            string v = s.Value;
            string text = string.IsNullOrEmpty(v) ? "（还没填）" : v;
            if (!string.IsNullOrEmpty(v) && s.Choices != null && s.ChoiceText != null) text = s.ChoiceText(v);
            if (shown >= MaxParamLines) { PutInspectorLine("…", y, Theme.WhaleDeep, 2, 0); break; }
            y = PutInspectorLine(s.Label + "：" + text, y, Theme.Ink, 2, wrap) + 1f;
            shown++;
        }
        // 表达式写在卡片正中间，这里只把它复述一遍，方便核对；
        // 参数表已经带标签列过的（计数循环的起点 / 终点）不再复述，否则同一份值出现两遍
        foreach (var kv in sel.Params)
        {
            bool isExpr = kv.Key.EndsWith("Expr", StringComparison.Ordinal) || kv.Key == "expr" || kv.Key == "formula";
            if (!isExpr || string.IsNullOrEmpty(kv.Value)) continue;
            if (listed.Contains(kv.Key) || CardLabeledExpr(sel, kv.Key)) continue;
            if (shown >= MaxParamLines) { PutInspectorLine("…", y, Theme.WhaleDeep, 2, 0); break; }
            y = PutInspectorLine("表达式：" + OneLine(kv.Value), y, Theme.HoneyDeep, 2, wrap) + 1f;
            shown++;
        }
        if (shown == 0) PutInspectorLine("这个模块没有参数", y, Theme.WhaleDeep, 2, wrap);
        FinishInspector(y);
        Debug.Log("[inspector] 选中 " + sel.Uid + " " + sel.Type + " 参数 " + shown + " 行");
    }

    // 题栏内容的真实下沿：子节点都按「锚点在左上、y 负值向下」摆放，取最下面那条的底边
    float InspectorContentBottom()
    {
        float b = 0f;
        if (inspectorContent == null) return b;
        for (int i = 0; i < inspectorContent.childCount; i++)
        {
            var rt = inspectorContent.GetChild(i) as RectTransform;
            if (rt == null) continue;
            float bottom = -rt.anchoredPosition.y + rt.rect.height;
            if (bottom > b) b = bottom;
        }
        return b + 12f;
    }

    // 内容高度超出视口就交给滚轮，不要把手尾的行裁掉
    void FinishInspector(float y)
    {
        if (inspectorContent == null || inspectorScroll == null) return;
        float h = InspectorRect != null && InspectorRect.rect.height > 1f ? InspectorRect.rect.height : 684f;
        // 累加出来的 y 只是「以为排到了多高」：带折行的长题面量出来的高和真正占的位置对不上，
        // 末尾几行会掉到视口下面，滚到底也只露个头，鼠标按下被底栏抢走。按子节点真实下沿再兜一次。
        float need = Mathf.Max(y + 12f, InspectorContentBottom());
        inspectorContent.sizeDelta = new Vector2(inspectorContent.sizeDelta.x, Mathf.Max(h, need));
        inspectorScroll.SetExtent(need - h);
    }

    float PutInspectorLine(string s, float y, Color color, int scale, float wrapW)
    {
        if (string.IsNullOrEmpty(s)) return y;
        var t = Label(inspectorContent != null ? inspectorContent : InspectorRect, s, 10, y, color, scale);
        if (wrapW > 0) { t.Wrap = true; t.WrapWidth = Mathf.RoundToInt(wrapW); }
        var m = t.Measure();
        t.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, m.x), Mathf.Max(1f, m.y));
        return y + m.y + 4f;
    }

    void BuildFooter()
    {
        // 署名整行居中：锚点拉满底栏，窗口多宽都居中，不会偏在左边 1440 里
        var t = Label(FooterRect, "主角形象为第三方作品：原型溟月（B站@上善无形）· 女仆版@ZipZipPipe · 动画@Small-tailqwq · CC BY-NC-SA 4.0 非商业", 0, 4, Theme.WhaleDeep, 2);
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        t.Align = PixelText.TextAlign.Center;
        t.raycastTarget = false;
    }

    // ---------------- 追踪栏 ----------------

    void BuildTrace()
    {
        // 追踪栏总高 144：左侧步数固定宽度，中间消息，右侧变量区（2 号字，最多 4 行 28px）
        traceStep = Label(TraceRect, "第 0 / 0 步", 10, 6, Theme.Ink, 2);
        traceStep.Align = PixelText.TextAlign.Left;
        SetLines(traceStep, "第 0 / 0 步", 190f);

        traceMessage = Label(TraceRect, "点运行，看大肥鱼怎么搬数据", MsgX, 6, Theme.WhaleDeep, 2);
        traceMessage.Align = PixelText.TextAlign.Left;
        SetLines(traceMessage, "点运行，看大肥鱼怎么搬数据", MsgW);

        var head = Label(TraceRect, "变量 / 数据", VarX, 6, Theme.WhaleDeep, 2);
        head.Align = PixelText.TextAlign.Left;
        SetLines(head, "变量 / 数据", VarW);

        traceVars = Label(TraceRect, "", VarX, 34, Theme.Ink, 2);
        traceVars.Align = PixelText.TextAlign.Left;
        SetLines(traceVars, "", VarW);

        Panel(TraceRect, "Slot", 10, 34, 190, 6, Theme.Salt);
        Debug.Log("[trace] step=" + traceStep.rectTransform.rect + " msg=" + traceMessage.rectTransform.rect);
    }

    void SetStep(int i, int total)
    {
        SetLines(traceStep, Fit("第 " + i + " / " + total + " 步", 190f, 2), 190f);
    }

    void SetMessage(string s)
    {
        SetLines(traceMessage, Fit(OneLine(s), MsgW - 10f, 2), MsgW);
    }

    // 变量区：固定第一行是本轮输入，中间最多 6 行变量，末尾是当前输出（合计最多 8 行）
    void SetVars(TraceEvent ev)
    {
        currentEvent = ev;
        RefreshVars(null);
    }

    void RefreshVars(string outputOverride)
    {
        if (traceVars == null) return;
        string output = outputOverride;
        if (string.IsNullOrEmpty(output) && currentEvent != null) output = currentEvent.Output;
        // 输出行是玩家最关心的那一条，行数不够时先给它留位置，从变量行开始裁
        int room = MaxVarLines - (string.IsNullOrEmpty(output) ? 0 : 1);
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(lastInput)) lines.Add(Fit("输入 = " + OneLine(lastInput), VarW, 2));

        if (currentEvent != null && currentEvent.Vars != null)
        {
            int n = 0;
            foreach (var kv in currentEvent.Vars)
            {
                if (n >= MaxVarItems || lines.Count >= room) break;
                lines.Add(Fit(kv.Key + " = " + ValueText(kv.Value), VarW, 2));
                n++;
            }
        }

        if (!string.IsNullOrEmpty(output) && lines.Count < MaxVarLines)
            lines.Add(Fit("输出 = " + OneLine(output), VarW, 2));

        if (lines.Count == 0) lines.Add("（暂无数据）");
        while (lines.Count > MaxVarLines) lines.RemoveAt(lines.Count - 1);
        SetLines(traceVars, string.Join("\n", lines.ToArray()), VarW);
        RefreshBoards();
    }

    // 数组变量画成虚线悬浮看板：默认停在声明它的卡片旁边，玩家拖过之后就归玩家摆；
    // 本轮按下标写了哪一格就把哪一格抬起来亮一下
    void RefreshBoards()
    {
        if (GraphCanvas == null) return;
        string hiName = null;
        int hiIndex = -1;
        if (currentEvent != null && currentEvent.Kind == "varset"
            && Dayu.Core.ArrayView.TryParseIndexedWrite(currentEvent.Text, out hiName, out hiIndex) == false)
        {
            hiName = null;
            hiIndex = -1;
        }
        GraphCanvas.ShowArrays(currentEvent == null ? null : currentEvent.Arrays, hiName, hiIndex);
        // 栈：压入弹出的那一步把动作带给看板，盘子才落得下去、飘得起来
        GraphCanvas.ShowStacks(currentEvent == null ? null : currentEvent.Structs,
            currentEvent != null && (currentEvent.Kind == "push" || currentEvent.Kind == "pop") ? currentEvent.Kind : null);
    }

    static string ValueText(object v)
    {
        if (v == null) return "null";
        if (v is bool) return (bool)v ? "true" : "false";
        if (v is double) return ((double)v).ToString("0.######");
        return v.ToString();
    }

    // ---------------- 运行链路 ----------------

    public void RunInput()
    {
        StartRun(breakpointUids.Count > 0 ? RunMode.Breakpoint : RunMode.Normal);
    }

    public void RunBreakpoint()
    {
        // 选中了节点就切换它的断点，没选中就按断点模式直接跑
        if (selectedNode != null && !string.IsNullOrEmpty(selectedNode.Uid))
        {
            OnBreakpointToggled(selectedNode);
            ShowGuide("断点");
            return;
        }
        StartRun(RunMode.Breakpoint);
    }

    public void RunTrace()
    {
        ShowGuide("跟踪");
        StartRun(RunMode.Trace);
    }

    void StartRun(RunMode mode)
    {
        StopRun();
        runMode = mode;
        if (level == null) LoadLevel("L1");
        Debug.Log("[run] 启动会话 mode=" + mode);
        runRoutine = StartCoroutine(RunRoutine());
    }

    void StopRun()
    {
        if (runRoutine != null) { StopCoroutine(runRoutine); runRoutine = null; }
        if (stepRoutine != null) { StopCoroutine(stepRoutine); stepRoutine = null; }
    }

    // 只做「清场 + 编译 + 跑完 VM 拿到全部事件」，不放动画；四种运行模式共用同一份事件流
    bool PrepareSession()
    {
        ClearTokens();
        ClearBadges();
        if (GraphCanvas != null) GraphCanvas.ClearHighlight();
        events.Clear(); eventCursor = 0; playTotal = 0;
        lastRun = null; currentEvent = null;
        SetStep(0, 0);
        SetVars(null);

        if (level == null) return false;

        var graph = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        var cr = Compiler.Compile(graph);

        // 编译失败 ⇒ 只报错并高亮出错节点，不进入播放
        if (!cr.Ok)
        {
            var e0 = cr.Errors.Count > 0 ? cr.Errors[0] : null;
            string msg = e0 != null ? e0.Message : "未知错误";
            SetMessage("编译错误：" + msg);
            SetStep(0, 0);
            if (e0 != null && !string.IsNullOrEmpty(e0.NodeUid)) GraphCanvas.HighlightNode(e0.NodeUid, true);
            Debug.Log("[run] 编译失败：" + (e0 != null ? e0.NodeUid + " " + msg : msg));
            return false;
        }

        lastInput = FirstTestInput();
        var result = Vm.Run(cr.Program, lastInput);
        lastRun = result;
        events.AddRange(result.Events);
        playTotal = Mathf.Min(events.Count, MaxPlayEvents);
        eventCursor = 0;
        Debug.Log("[run] " + level.Id + " 输入「" + lastInput + "」事件 " + events.Count + " 条，输出「" + (result.Output ?? "") + "」");
        return true;
    }

    IEnumerator RunRoutine()
    {
        if (!PrepareSession()) { runRoutine = null; yield break; }

        var graph = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;

        if (runMode == RunMode.Step)
        {
            // 单步：会话先备好，但不自动播，等玩家一下一下点
            SetMessage("单步就绪：共 " + playTotal + " 步，点「单步」前进");
            runRoutine = null;
            yield break;
        }

        // 画布上已经没有数据线可流了，四种播放共用同一套带搬运动画的逐事件回放：
        // 循环体和循环的每一轮都算一步，不再「先把整片播一遍、再从头干跑一遍」
        while (eventCursor < playTotal)
        {
            int i = eventCursor;
            yield return PlayEventAnimated(events[i], i);
            eventCursor++;
            if (runMode == RunMode.Breakpoint && evHasBreakpoint(events[i]))
            {
                SetMessage("命中断点：第 " + eventCursor + " 步，点「单步」继续");
                Debug.Log("[run] 命中断点 cursor=" + eventCursor);
                runRoutine = null;
                yield break;
            }
            if (runMode == RunMode.Trace)
                SetMessage("跟踪中：第 " + eventCursor + " / " + playTotal + " 步");
            yield return new WaitForSeconds(EventGapSec);
        }

        if (runMode == RunMode.Normal && graph != null)
            yield return PlayCompareAnimation(graph, lastRun);

        ShowRunEnd(lastRun);
        runRoutine = null;
    }

    // 单步 / 跟踪的动画：把这一步的数值沿「上一步模块 → 本步模块」的执行引线搬过去，落地后留在卡片上
    IEnumerator PlayEventAnimated(TraceEvent ev, int index)
    {
        PlayEvent(ev);
        if (ev == null || GraphCanvas == null || string.IsNullOrEmpty(ev.Uid)) yield break;
        var g = GraphCanvas.CurrentGraph;
        if (g == null || GraphCanvas.FindCard(ev.Uid) == null) yield break;

        string text = StepValueText(index);
        if (string.IsNullOrEmpty(text)) yield break;

        List<Vector2> path = null;
        string fromUid = PrevStepNodeUid(index, ev.Uid);
        if (!string.IsNullOrEmpty(fromUid) && GraphCanvas.FindCard(fromUid) != null)
        {
            var link = g.Links.Find(l => l.Kind == LinkKind.Exec && l.FromNode == fromUid && l.ToNode == ev.Uid);
            path = link != null
                ? GraphCanvas.LinkPath(link)
                : GraphCanvas.CurvePath(GraphCanvas.CardCenterLocal(fromUid), GraphCanvas.CardCenterLocal(ev.Uid));
        }
        else
        {
            // 第一步：从样例输入板出发
            var sample = g.Nodes.Find(n => n != null && n.Type == "sample_input");
            if (sample != null && GraphCanvas.FindCard(sample.Uid) != null)
                path = GraphCanvas.CurvePath(GraphCanvas.CardCenterLocal(sample.Uid), GraphCanvas.CardCenterLocal(ev.Uid));
        }

        if (path == null || path.Count < 2)
        {
            SetNodeBadge(ev.Uid, text, Theme.Honey);      // 没有可走的引线：数值直接留在卡片上
            yield break;
        }

        // 鲸鱼娘从她当前站的位置沿执行线走过去，数字顶在头上；走完就留在这一块旁边
        yield return WalkHero(path, TokenFlySec, text);
        SetNodeBadge(ev.Uid, text, Theme.Honey);
    }

    // 这一步该搬什么数字：优先本步的输出值，其次与上一步相比刚发生变化的变量
    string StepValueText(int index)
    {
        var ev = events[index];
        if (ev == null) return "";
        if (!string.IsNullOrEmpty(ev.Output)) return ev.Output;
        var prev = index > 0 ? events[index - 1] : null;
        if (ev.Vars == null) return "";
        foreach (var kv in ev.Vars)
        {
            object old;
            if (prev != null && prev.Vars != null && prev.Vars.TryGetValue(kv.Key, out old) && Equals(old, kv.Value)) continue;
            return kv.Key + "=" + ValueText(kv.Value);
        }
        return "";
    }

    // 往前找最近一个「换了模块」的事件，作为这一步动画的起点
    string PrevStepNodeUid(int index, string currentUid)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            var ev = events[i];
            if (ev == null || string.IsNullOrEmpty(ev.Uid) || ev.Uid == currentUid) continue;
            return ev.Uid;
        }
        return null;
    }

    bool evHasBreakpoint(TraceEvent ev)
    {
        return ev != null && !string.IsNullOrEmpty(ev.Uid) && breakpointUids.Contains(ev.Uid);
    }

    void PlayEvent(TraceEvent ev)
    {
        if (ev == null) return;
        GraphCanvas.ClearHighlight();
        if (!string.IsNullOrEmpty(ev.Uid)) GraphCanvas.HighlightNode(ev.Uid, true);
        SetStep(ev.I + 1, events.Count);
        SetMessage(ev.Text);
        SetVars(ev);
    }

    void ShowRunEnd(RunResult result)
    {
        if (result == null) return;
        if (!result.Ok && result.Error != null)
        {
            SetMessage("运行出错：" + result.Error.Message);
            if (!string.IsNullOrEmpty(result.Error.NodeUid))
            {
                GraphCanvas.ClearHighlight();
                GraphCanvas.HighlightNode(result.Error.NodeUid, true);
            }
            return;
        }
        var sample = CurrentSample();
        var g = GraphCanvas != null ? GraphCanvas.CurrentGraph : null;
        var cmpNode = g != null ? g.Nodes.Find(n => n != null && n.Type == "sample_compare") : null;
        bool compared = sample != null && cmpNode != null && GraphUtil.LinkInto(g, cmpNode.Uid, "value") != null;
        bool accepted = compared && string.Equals((result.Output ?? "").Trim(), (sample.Output ?? "").Trim(), System.StringComparison.Ordinal);
        string msg = compared
            ? ((accepted ? "AC" : "WA") + "：输出 " + OneLine(result.Output) + "，期望 " + OneLine(sample.Output))
            : "运行完成，输出：" + OneLine(result.Output);
        if (events.Count > MaxPlayEvents)
            msg += "（共 " + events.Count + " 条事件，只播放了前 " + MaxPlayEvents + " 条）";
        SetMessage(msg);
        RefreshVars(result.Output);
        Debug.Log("[run] 完成，输出「" + (result.Output ?? "") + "」");
    }

    // f：单步 —— 先备好会话（编译 + 跑完 VM），但不自动播；每点一下只带动画前进一步
    public void StepOnce()
    {
        if (stepRoutine != null) { Debug.Log("[run] 上一步还在播，先等它落地"); return; }
        StopRun();
        runMode = RunMode.Step;
        if (events.Count == 0 || lastRun == null)
        {
            if (level == null) LoadLevel("L1");
            if (!PrepareSession()) return;
            SetMessage("单步就绪：共 " + playTotal + " 步，点「单步」前进");
            Debug.Log("[run] 单步创建共享会话并编译当前图");
            if (playTotal == 0) return;
        }
        if (eventCursor >= playTotal)
        {
            SetMessage("已经是最后一步了，点「重来」再跑一次");
            return;
        }
        stepRoutine = StartCoroutine(StepOneRoutine());
    }

    IEnumerator StepOneRoutine()
    {
        int i = eventCursor;
        yield return PlayEventAnimated(events[i], i);
        eventCursor++;
        stepRoutine = null;
        Debug.Log("[run] 单步推进 cursor=" + eventCursor + "/" + playTotal);
        if (eventCursor >= playTotal) ShowRunEnd(lastRun);
        else SetMessage("单步暂停：第 " + eventCursor + " / " + playTotal + " 步（点单步继续）");
    }

    // g：重来 —— 清干净后重新载入当前关卡
    public void RestartLevel()
    {
        string id = level != null ? level.Id : "L1";
        // 自动存档上线后「重来」必须先把这一关的档删掉，否则读档会把玩家自己的图原样读回来，按钮就成了摆设
        bool had = SaveSystem.HasSave(id);
        SaveSystem.CancelPending();
        SaveSystem.Delete(id);
        progress.Remove(id);      // 删档就顺手删这一关的进度条目，别的关卡的进度一个字都不动
        SaveProgressMeta();
        LoadLevel(id, false);
        Toast(had ? "已清空本关存档，回到初始图" : "已回到本关初始图");
    }

    // h：适应 —— GraphView 的 content / zoom 是私有成员，拿不到；
    // 这里只通过公开的 transform 找 "Content" 子节点复位平移与缩放，再让画布重算连线。
    // （缩放字段仍是原值，下一次滚轮会覆盖，属于可接受的无副作用实现。）
    public void ResetView()
    {
        if (GraphCanvas == null) { Toast("画布还没准备好"); return; }
        GraphCanvas.FitToContent();
        Toast("画布已适应窗口");
    }

    // ---------------- token ----------------

    static string[] Tokenize(string s)
    {
        var t = (s ?? "").Trim();
        return t.Length == 0 ? new string[0] : t.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
    }

    // ---------------- 数据流动画 ----------------

    sealed class FlowToken
    {
        public GameObject Go;
        public RectTransform Rt;
        public GraphLink Link;
        public bool Custom;
        public bool HasStart;
        public Vector2 StartLocal;
        public Vector2 EndLocal;
        public GameObject SourceChip;
        public GameObject Carrier;      // 抱箱子的鲸鱼娘
        public Image CarrierImg;
        public Vector2 LastPos;
        public bool HasLastPos;
        public float CarryClock;
    }

    readonly List<GameObject> flowTokens = new List<GameObject>();
    readonly Dictionary<string, GameObject> badges = new Dictionary<string, GameObject>();
    readonly List<GameObject> sampleChips = new List<GameObject>();
    readonly List<GameObject> expectedChips = new List<GameObject>();
    readonly List<GameObject> actualChips = new List<GameObject>();

    Vector2 ChipLocal(GameObject chip)
    {
        if (chip == null || GraphCanvas == null || GraphCanvas.ContentTransform == null) return Vector2.zero;
        return GraphCanvas.ContentTransform.InverseTransformPoint(chip.transform.position);
    }

    // 结果从「产出它的那个模块」走到对比板：画布上不再连数据线，取图里最后一块输出模块当出发点
    IEnumerator WalkToCompareBoard(Graph g, GraphNode cmp, string text)
    {
        if (g == null || cmp == null || GraphCanvas == null || string.IsNullOrEmpty(text)) yield break;

        GraphNode src = null;
        foreach (var n in g.Nodes)
            if (n != null && n.Type == "output" && GraphCanvas.FindCard(n.Uid) != null) src = n;
        if (src == null || src.Uid == cmp.Uid) yield break;

        var path = GraphCanvas.CurvePath(GraphCanvas.CardCenterLocal(src.Uid), GraphCanvas.CardCenterLocal(cmp.Uid));
        Debug.Log("[compare] 鲸鱼娘从 " + src.Uid + "(" + src.Type + ") 走到 " + cmp.Uid + "，路径 " + path.Count + " 点");
        yield return WalkHero(path, Mathf.Max(.4f, TokenFlySec), text);
        SetMessage("大肥鱼把结果送到了对比板");
    }

    // 结果与期望逐位飞入对比板：一致的两块变绿，不一致变红
    IEnumerator PlayCompareAnimation(Graph graph, RunResult result)
    {
        var cmp = graph != null ? graph.Nodes.Find(n => n != null && n.Type == "sample_compare") : null;
        var sample = CurrentSample();
        if (cmp == null || sample == null) yield break;

        var expected = Tokenize(sample.Output);
        var actual = Tokenize(result != null ? result.Output : "");
        int max = Mathf.Max(expected.Length, actual.Length);
        if (max == 0) yield break;

        var card = GraphCanvas.FindCard(cmp.Uid);
        if (card == null) yield break;
        EnsureCompareSlots(card, max);
        // 逐条样例换着跑时位数会变，多出来的旧色块要收掉
        for (int k = max; k < expectedChips.Count; k++) expectedChips[k].SetActive(false);
        for (int k = max; k < actualChips.Count; k++) actualChips[k].SetActive(false);
        for (int k = 0; k < max; k++)
        {
            if (k < expectedChips.Count) expectedChips[k].SetActive(true);
            if (k < actualChips.Count) actualChips[k].SetActive(true);
        }
        // 表达式模式下对比板没有 value 端口，直接从卡片中心出发
        Vector2 start = GraphCanvas.PortLocal(cmp.Uid, "value", false);
        if (start == Vector2.zero)
            start = GraphCanvas.ContentTransform.InverseTransformPoint(card.Rect.TransformPoint(card.Rect.rect.center));

        // 鲸鱼娘先抱着结果从输出模块走到对比板，再逐位往槽里搬：直接出现在板上看不出是谁送来的
        yield return WalkToCompareBoard(graph, cmp, OneLine(result != null ? result.Output : ""));

        SetMessage("样例对比：逐位核对结果");
        for (int i = 0; i < max; i++)
        {
            string exp = i < expected.Length ? expected[i] : "-";
            string act = i < actual.Length ? actual[i] : "-";
            if (i < actualChips.Count)
            {
                SetChip(actualChips[i], "?", Theme.Salt);
                SetChip(expectedChips.Count > i ? expectedChips[i] : null, exp, Theme.Salt);
            }

            Vector2 target = i < actualChips.Count ? ChipLocal(actualChips[i]) : ChipLocal(card.gameObject);
            var token = CreateFlowToken(act, false);
            if (token != null)
            {
                token.Custom = true;
                token.StartLocal = start;
                token.EndLocal = target;
                token.Rt.anchoredPosition = start;
                float elapsed = 0f, dur = Mathf.Max(.28f, TokenFlySec * .7f);
                while (elapsed < dur)
                {
                    elapsed += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
                    PositionFlowToken(token, k);
                    yield return null;
                }
                Destroy(token.Go);
                flowTokens.Remove(token.Go);
            }

            bool eq = i < expected.Length && i < actual.Length && string.Equals(expected[i], actual[i], StringComparison.Ordinal);
            Color c = eq ? Theme.Mint : Theme.Coral;
            if (i < expectedChips.Count) SetChip(expectedChips[i], exp, c);
            if (i < actualChips.Count) SetChip(actualChips[i], act, c);
            SetMessage("对比第 " + (i + 1) + " 位：" + exp + (eq ? " = " : " 不等于 ") + act);
            yield return new WaitForSeconds(.18f);
        }

        bool accepted = result != null && result.Ok &&
            string.Equals((result.Output ?? "").Trim(), (sample.Output ?? "").Trim(), StringComparison.Ordinal);
        SetNodeBadge(cmp.Uid, accepted ? "AC" : "WA", accepted ? Theme.Mint : Theme.Coral);
        SetMessage(accepted ? "逐位对比完成：AC" : "逐位对比完成：WA");
    }

    void EnsureCompareSlots(NodeCard card, int count)
    {
        while (expectedChips.Count < count)
        {
            float x = 8f;
            if (expectedChips.Count > 0)
            {
                var last = expectedChips[expectedChips.Count - 1].GetComponent<RectTransform>();
                x = last.anchoredPosition.x + last.sizeDelta.x + 4f;
            }
            expectedChips.Add(CreateSeqChip(card.transform, "-", x, 54f, Theme.Salt, "SampleSeqExpected"));
        }
        while (actualChips.Count < count)
        {
            float x = 8f;
            if (actualChips.Count > 0)
            {
                var last = actualChips[actualChips.Count - 1].GetComponent<RectTransform>();
                x = last.anchoredPosition.x + last.sizeDelta.x + 4f;
            }
            actualChips.Add(CreateSeqChip(card.transform, "?", x, 92f, Theme.Salt, "SampleSeqActual"));
        }
    }

    static Vector2 SamplePath(List<Vector2> path, float k)
    {
        if (path == null || path.Count == 0) return Vector2.zero;
        if (path.Count == 1) return path[0];
        float f = Mathf.Clamp01(k) * (path.Count - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, path.Count - 2);
        float t = f - i;
        return Vector2.Lerp(path[i], path[i + 1], t);
    }


    // 数据流动画里的箱子：按类型取对应精灵，取不到时退回原来的色块
    static string BoxTypeFor(string text)
    {
        if (string.IsNullOrEmpty(text)) return "int";
        if (text == "true" || text == "false") return "bool";
        if (text.Length == 1 && char.IsLetter(text[0])) return "char";
        if (text.IndexOf(' ') >= 0 || text.IndexOf('\'') >= 0) return "string";
        double d;
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
            return d == Math.Floor(d) && !text.Contains(".") ? "int" : "real";
        return "string";
    }

    // withWhale=false 时令牌只是一个数字箱：数据流一波可能同时飞六块，
    // 每块都骑一只鲸鱼娘就满屏都是她了；角色全场只留 WalkHero 那一只
    FlowToken CreateFlowToken(string text, bool withWhale = true)
    {
        if (GraphCanvas == null || GraphCanvas.ContentTransform == null) return null;
        var go = new GameObject("FlowToken", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(GraphCanvas.ContentTransform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
        rt.pivot = new Vector2(.5f, .5f);

        string type = BoxTypeFor(text);
        var box = PropAssets.Box(type);
        var img = go.GetComponent<Image>();
        var whaleSheet = HeroAssets.Idle;
        bool hasWhale = withWhale && whaleSheet != null && whaleSheet.FrameCount > 0;

        if (hasWhale)
        {
            // 主角本人就是数据载体，数字写在头顶
            img.sprite = null;
            img.enabled = false;
            rt.sizeDelta = new Vector2(46f, 62f);
        }
        else if (box != null)
        {
            img.sprite = box;
            img.type = Image.Type.Simple;
            img.color = Color.white;
            rt.sizeDelta = new Vector2(18f, 18f);
        }
        else
        {
            var m = PixelText.MeasureString(text, 1);
            rt.sizeDelta = new Vector2(Mathf.Max(26f, m.x + 14f), 20f);
            img.sprite = GraphView.White;
            img.type = Image.Type.Simple;
            img.color = Theme.Honey;
        }
        img.raycastTarget = false;

        // 数字写在小标签上，叠在箱子上方
        var label = PixelText.Create(go.transform, text, Theme.Ink, 1);
        var lrt = label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(.5f, 1f);
        lrt.pivot = new Vector2(.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, hasWhale ? -1f : -1f);
        lrt.sizeDelta = new Vector2(Mathf.Max(20f, PixelText.MeasureString(text, 1).x + 4f), 14f);
        label.Align = PixelText.TextAlign.Center;
        label.raycastTarget = false;

        var token = new FlowToken { Go = go, Rt = rt };

        // 搬运角色：抱着箱子的鲸鱼娘，10fps 循环，按移动方向水平翻转
        if (hasWhale)
        {
            var carrier = new GameObject("Carrier", typeof(RectTransform), typeof(Image));
            carrier.transform.SetParent(go.transform, false);
            var crt = (RectTransform)carrier.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(.5f, 0f);
            crt.pivot = new Vector2(.5f, 0f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(44f, 44f);
            var cimg = carrier.GetComponent<Image>();
            cimg.sprite = whaleSheet[0];
            cimg.type = Image.Type.Simple;
            cimg.color = Color.white;
            cimg.raycastTarget = false;
            token.Carrier = carrier;
            token.CarrierImg = cimg;
            if (label != null) label.transform.SetAsLastSibling();
        }

        flowTokens.Add(go);
        go.SetActive(true);
        return token;
    }

    void PositionFlowToken(FlowToken t, float k)
    {
        if (t == null || t.Rt == null) return;
        Vector2 pos;
        if (t.Custom)
        {
            pos = Vector2.Lerp(t.StartLocal, t.EndLocal, k);
            t.Rt.anchoredPosition = pos;
            UpdateCarrier(t, pos);
            return;
        }
        if (t.Link == null) return;

        var a = GraphCanvas.PortLocal(t.Link.FromNode, t.Link.FromPort, true);
        var b = GraphCanvas.PortLocal(t.Link.ToNode, t.Link.ToPort, false);

        // 从样例板里的数字块先“抬起”到端口，再沿数据线走
        if (t.HasStart)
        {
            const float lift = .22f;
            if (k < lift)
            {
                t.Rt.anchoredPosition = Vector2.Lerp(t.StartLocal, a, k / lift);
                return;
            }
            k = (k - lift) / (1f - lift);
        }

        float dx = Mathf.Abs(b.x - a.x) * .45f;
        var p0 = a + new Vector2(dx, 0f);
        var p1 = b - new Vector2(dx, 0f);
        float u = 1f - k;
        var p = u * u * u * a + 3f * u * u * k * p0 + 3f * u * k * k * p1 + k * k * k * b;
        t.Rt.anchoredPosition = p;
        UpdateCarrier(t, p);
    }

    // 搬运角色：10fps 循环，方向朝移动方向
    void UpdateCarrier(FlowToken t, Vector2 pos)
    {
        if (t == null || t.CarrierImg == null) return;
        if (t.HasLastPos)
        {
            float dx = pos.x - t.LastPos.x;
            if (Mathf.Abs(dx) > 0.5f)
            {
                var crt = (RectTransform)t.Carrier.transform;
                crt.localScale = new Vector3(dx >= 0f ? -1f : 1f, 1f, 1f);
            }
        }
        t.LastPos = pos;
        t.HasLastPos = true;

        t.CarryClock += Time.deltaTime;
        var sheet = HeroAssets.Idle;
        if (sheet == null || sheet.FrameCount <= 1) return;
        float step = 1f / 10f;
        int frame = Mathf.FloorToInt(t.CarryClock / step) % sheet.FrameCount;
        if (t.CarrierImg.sprite != sheet[frame]) t.CarrierImg.sprite = sheet[frame];
    }

    // ---------------- 全场唯一的一只鲸鱼娘 ----------------
    // 之前每一跳都新建一个带角色的令牌、跳完就销毁，看上去就是「进块瞬间消失、再从另一头钻出来」。
    // 现在她常驻画布：走完一段就站在终点等下一段，下一段从脚下接着走。
    RectTransform heroRt; Image heroImg; PixelText heroLabel; RectTransform heroBadge;
    Vector2 heroPos; bool heroHasPos; float heroClock;

    void EnsureHero()
    {
        if (heroRt != null || GraphCanvas == null || GraphCanvas.ContentTransform == null) return;
        var sheet = HeroAssets.Idle;
        if (sheet == null || sheet.FrameCount == 0) return;
        var go = new GameObject("HeroWalker", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(GraphCanvas.ContentTransform, false);
        heroRt = (RectTransform)go.transform;
        heroRt.anchorMin = heroRt.anchorMax = new Vector2(.5f, .5f);
        heroRt.pivot = new Vector2(.5f, 1f);
        heroRt.sizeDelta = new Vector2(44f, 44f);
        heroImg = go.GetComponent<Image>();
        heroImg.sprite = sheet[0];
        heroImg.type = Image.Type.Simple;
        heroImg.raycastTarget = false;
        // 搬的数字放在头顶一块描边名牌里：纯黑小字直接压在深色立绘和卡片背景上看不清（玩家原话）
        var badge = new GameObject("HeroBadge", typeof(RectTransform), typeof(Image));
        badge.transform.SetParent(go.transform, false);
        var bimg = badge.GetComponent<Image>();
        bimg.color = Theme.Paper;
        bimg.raycastTarget = false;
        var border = badge.gameObject.AddComponent<Outline>();
        border.effectColor = Theme.A(Theme.Ink, .9f);
        border.effectDistance = new Vector2(1.5f, -1.5f);
        heroBadge = badge.GetComponent<RectTransform>();
        heroBadge.anchorMin = heroBadge.anchorMax = new Vector2(.5f, 1f);
        heroBadge.pivot = new Vector2(.5f, 1f);
        heroLabel = PixelText.Create(badge.transform, "", Theme.Ink, HeroCarryScale);
        var lrt = heroLabel.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(.5f, .5f);
        lrt.pivot = new Vector2(.5f, .5f);
        lrt.anchoredPosition = Vector2.zero;
        heroLabel.Align = PixelText.TextAlign.Center;
        heroLabel.raycastTarget = false;
        go.SetActive(false);
    }

    // 搬运数字的字号：画布缩到 0.5 上下时，2 号字在屏幕上只剩 6px 高，玩家说看不清（原话「太小了」）
    const int HeroCarryScale = 4;

    // 名牌与字块该多大：字块矩形必须跟着文字一起撑开，否则 PixelText 的居中对齐
    // 拿不到正确宽度（rect 宽 <=1 时直接退化成左对齐），整串字会从名牌中心往右溢出。
    public static void HeroBadgeLayout(string carry, int scale, out Vector2 badge, out Vector2 label)
    {
        var m = PixelText.MeasureString(carry ?? "", scale);
        label = new Vector2(Mathf.Max(1f, m.x), Mathf.Max(1f, m.y));
        badge = new Vector2(m.x + 12f, m.y + 8f);
    }

    // 名牌跟着数字伸缩；整块抬到头顶上方，避免压住脸
    void SetHeroCarry(string carry)
    {
        if (heroLabel == null) return;
        bool show = !string.IsNullOrEmpty(carry);
        heroLabel.Text = show ? carry : "";
        if (heroBadge == null) return;
        heroBadge.gameObject.SetActive(show);
        if (!show) return;
        Vector2 bs, ls;
        HeroBadgeLayout(carry, HeroCarryScale, out bs, out ls);
        heroLabel.Scale = HeroCarryScale;
        heroLabel.rectTransform.sizeDelta = ls;
        heroBadge.sizeDelta = bs;
        heroBadgeGap = bs.y + 2f;
        ApplyHeroBadge();
    }

    // 名牌的屏幕大小恒定：画布放大就把它缩小，画布缩小就把它放大，任何机位下数字都看得清。
    // 抬头的间距同样按缩放反向补偿，否则放大后名牌会飘到立绘老远的外面。
    float heroBadgeInv = 1f;
    float heroBadgeGap;
    float heroFlip = 1f;

    void StepHeroBadgeScale()
    {
        if (heroBadge == null || GraphCanvas == null) return;
        float inv = 1f / Mathf.Clamp(GraphCanvas.Zoom, .1f, 4f);
        if (Mathf.Abs(inv - heroBadgeInv) < .001f) return;
        heroBadgeInv = inv;
        ApplyHeroBadge();
    }

    // 名牌的反翻系数：名牌是立绘的子节点，净缩放 = 立绘的 x 缩放 × 名牌局部 x 缩放。
    // 立绘左右走时会镜像（x 缩放取 ±1），不顶回来数字就照镜子 —— 第十一轮截图里「s = 0」渲染成「0 = 2」。
    // 按父节点当前的 x 缩放取，别按行走方向记的 heroFlip 取：开局立绘还没翻过，heroFlip 已经是 ±1，两者对不上。
    public static float HeroBadgeLocalX(float carrierScaleX, float zoom)
    {
        float inv = 1f / Mathf.Clamp(zoom, .1f, 4f);
        return carrierScaleX < 0f ? -inv : inv;
    }

    void ApplyHeroBadge()
    {
        if (heroBadge == null) return;
        float cx = heroRt != null ? heroRt.localScale.x : 1f;
        heroBadge.localScale = new Vector3(HeroBadgeLocalX(cx, GraphCanvas != null ? GraphCanvas.Zoom : 1f),
                                           heroBadgeInv, 1f);
        heroBadge.anchoredPosition = new Vector2(0f, heroBadgeGap * heroBadgeInv);
    }

    void HideHero()
    {
        heroHasPos = false;
        if (heroRt != null) heroRt.gameObject.SetActive(false);
    }

    void StepHero(Vector2 pos)
    {
        if (heroRt == null) return;
        if (heroHasPos)
        {
            float dx = pos.x - heroPos.x;
            if (Mathf.Abs(dx) > .5f)
            {
                heroFlip = dx >= 0f ? -1f : 1f;
                heroRt.localScale = new Vector3(-heroFlip, 1f, 1f);
                // 名牌是立绘的子节点，跟着父节点一起翻会把数字照镜子，反向顶回来
                ApplyHeroBadge();
            }
        }
        heroClock += Time.deltaTime;
        var sheet = HeroAssets.Idle;
        if (sheet == null || sheet.FrameCount <= 1) return;
        int f = Mathf.FloorToInt(heroClock / .1f) % sheet.FrameCount;
        if (heroImg != null && heroImg.sprite != sheet[f]) heroImg.sprite = sheet[f];
    }

    // 沿路径走过去并留在终点；carry 非空时把数字顶在头上一起搬，送到后放下
    IEnumerator WalkHero(List<Vector2> path, float dur, string carry)
    {
        EnsureHero();
        if (heroRt == null || path == null || path.Count < 2) yield break;
        heroRt.gameObject.SetActive(true);
        if (carry != null) SetHeroCarry(carry);
        var full = new List<Vector2>();
        if (heroHasPos && (heroPos - path[0]).sqrMagnitude > 4f) full.Add(heroPos);
        full.AddRange(path);
        float elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            var p = SamplePath(full, Mathf.Clamp01(elapsed / dur));
            heroRt.anchoredPosition = p;
            StepHero(p);
            heroPos = p; heroHasPos = true;
            yield return null;
        }
        var end = full[full.Count - 1];
        heroRt.anchoredPosition = end;
        StepHero(end);
        heroPos = end; heroHasPos = true;
        if (heroLabel != null) SetHeroCarry(null);
    }

    void SetNodeBadge(string uid, string text, Color color)
    {
        if (GraphCanvas == null || string.IsNullOrEmpty(text)) return;
        var card = GraphCanvas.FindCard(uid);
        if (card == null) return;
        GameObject old;
        if (badges.TryGetValue(uid, out old) && old != null) Destroy(old);

        var go = new GameObject("ValueBadge", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(card.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-5f, -2f);
        float w = Mathf.Min(72f, PixelText.MeasureString(text, 1).x + 12f);
        rt.sizeDelta = new Vector2(w, 18f);

        var img = go.GetComponent<Image>();
        img.sprite = GraphView.White;
        img.type = Image.Type.Simple;
        img.color = color;
        img.raycastTarget = false;

        var label = PixelText.Create(go.transform, Fit(text, w - 8f), Theme.Ink, 1);
        var lrt = label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0f);
        lrt.pivot = new Vector2(0f, 0f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = rt.sizeDelta;
        label.Align = PixelText.TextAlign.Center;
        label.raycastTarget = false;

        badges[uid] = go;
    }

    void ClearBadges()
    {
        foreach (var go in badges.Values) if (go != null) Destroy(go);
        badges.Clear();
    }

    GameObject CreateToken(string text, int index)
    {
        if (OverlayLayer == null) return null;
        var go = new GameObject("Token_" + index, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(OverlayLayer, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);

        var m = PixelText.MeasureString(text, 1);
        float w = Mathf.Max(30f, m.x + 18f), h = 26f;
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(10f, -(10f + index * 30f));   // 画布左上角，纵向排开

        var img = go.GetComponent<Image>();
        img.sprite = GraphView.White;
        img.type = Image.Type.Simple;
        img.color = Theme.Honey;
        img.raycastTarget = false;

        var label = PixelText.Create(go.transform, text, Theme.Ink, 1);
        var lrt = label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0f);
        lrt.pivot = new Vector2(0f, 0f);
        lrt.anchoredPosition = Vector2.zero;
        lrt.sizeDelta = new Vector2(w, h);
        label.Align = PixelText.TextAlign.Center;
        label.raycastTarget = false;

        tokenObjects.Add(go);
        return go;
    }

    // 卡片世界中心 → 覆盖层局部坐标；没有这张卡片就退回画布中心
    Vector2 TokenTarget(string uid, Vector2 size)
    {
        Vector3 world;
        var card = !string.IsNullOrEmpty(uid) && GraphCanvas != null ? GraphCanvas.FindCard(uid) : null;
        if (card != null && card.Rect != null)
        {
            var c = card.Rect.rect.center;
            world = card.Rect.TransformPoint(new Vector3(c.x, c.y, 0f));
        }
        else if (CanvasHost != null)
        {
            world = CanvasHost.TransformPoint(CanvasHost.rect.center);
        }
        else world = Vector3.zero;

        Vector2 local = OverlayLayer.InverseTransformPoint(world);
        var r = OverlayLayer.rect;
        // token 的 anchor/pivot 都在左上角，锚点参考点是 (rect.xMin, rect.yMax)
        return new Vector2(local.x - r.xMin - size.x * 0.5f, local.y - r.yMax + size.y * 0.5f);
    }

    void ClearTokens()
    {
        for (int i = tokenObjects.Count - 1; i >= 0; i--)
        {
            var go = tokenObjects[i];
            if (go == null) continue;
            go.SetActive(false);
            Destroy(go);
        }
        tokenObjects.Clear();
        for (int i = flowTokens.Count - 1; i >= 0; i--)
        {
            var go = flowTokens[i];
            if (go == null) continue;
            go.SetActive(false);
            Destroy(go);
        }
        flowTokens.Clear();
        foreach (var chip in sampleChips) if (chip != null) chip.SetActive(true);
    }

    // ---------------- 工具 ----------------

    static RectTransform Child(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Anchor(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static PixelText Label(RectTransform parent, string text, float x, float y, Color color, int scale)
    {
        var t = PixelText.Create(parent, text, color, scale);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        return t;
    }

    // 改文字的同时把矩形高度对齐到文字块高（PixelText 在矩形比内容高时会垂直居中）
    void SetLines(PixelText t, string text, float width)
    {
        if (t == null) return;
        t.Wrap = false;
        t.Text = text ?? "";
        var m = t.Measure();
        t.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, width > 0f ? width : m.x), Mathf.Max(1f, m.y));
    }

    static string OneLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    static string Fit(string s, float maxW, int scale = 1)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (PixelText.MeasureString(s, scale).x <= maxW) return s;
        var cur = s;
        while (cur.Length > 1)
        {
            cur = cur.Substring(0, cur.Length - 1);
            if (PixelText.MeasureString(cur + "..", scale).x <= maxW) return cur + "..";
        }
        return "..";
    }

    Canvas CreateCanvas()
    {
        var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var cv = go.GetComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(W, H);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = 0.5f;
        return cv;
    }

    void LoadAtlas()
    {
        var tex = Resources.Load<Texture2D>("atlas");
        var js = Resources.Load<TextAsset>("atlas");
        if (tex == null || js == null) { Debug.LogError("[App] atlas 缺失"); return; }
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        GlyphAtlas.Init(tex, js.text);
    }

    static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    // 一级目录标题：点一下收起 / 展开整组条目
    sealed class PaletteGroupClick : MonoBehaviour, IPointerClickHandler
    {
        public string Cat;
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            e.Use();
            App.I.TogglePaletteGroup(Cat);
        }
    }

    sealed class PaletteDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public string NodeType;
        public string[] Preset;
        GameObject ghost;
        public void OnBeginDrag(PointerEventData e)
        {
            // 验收 #120：模块栏拖到画布这一条链路以前只在「成功」时才有痕迹，
            // 拖不动时日志里一片空白，分不清是鼠标事件没进 Unity 还是 handler 没触发。
            Debug.Log("[drag] 起手 " + NodeType + " 屏幕=(" + e.position.x.ToString("F0") + "," + e.position.y.ToString("F0") + ")");
            ghost = new GameObject("PaletteDragGhost", typeof(RectTransform), typeof(Image));
            ghost.transform.SetParent(App.I.OverlayLayer, false);
            var rt = ghost.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(120, 28);
            var img = ghost.GetComponent<Image>();
            img.color = Theme.A(Theme.Mint, 0.75f);
            img.raycastTarget = false;
            OnDrag(e);
        }
        public void OnDrag(PointerEventData e)
        {
            if (ghost == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(App.I.OverlayLayer, e.position, null, out var p);
            ghost.GetComponent<RectTransform>().anchoredPosition = p;
            // 拖到画布上时把落点下面那张卡片亮起来，并铺一个虚线框标出松手后新模块会落在哪
            var canvasRect = App.I.CanvasHost;
            if (RectTransformUtility.RectangleContainsScreenPoint(canvasRect, e.position, null))
                App.I.GraphCanvas.PreviewDropTarget(e.position, NodeType, Preset);
            else App.I.GraphCanvas.ClearDropPreview();
        }
        public void OnEndDrag(PointerEventData e)
        {
            if (ghost != null) Destroy(ghost);
            var canvasRect = App.I.CanvasHost;
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(canvasRect, e.position, null);
            Debug.Log("[drag] 松手 " + NodeType + " 落点屏幕=(" + e.position.x.ToString("F0") + "," + e.position.y.ToString("F0") +
                      ") 画面=" + Screen.width + "x" + Screen.height + " 画布内=" + inside);
            App.I.GraphCanvas.ClearDropPreview();
            if (inside) App.I.GraphCanvas.AddPaletteNode(NodeType, e.position, Preset);
        }
    }

    // 模块栏滚动：条目比视口高时，滚轮上下挪内容
    sealed class PaletteScroll : MonoBehaviour, IScrollHandler
    {
        public RectTransform Content;
        float maxScroll;
        float offset;
        public void SetExtent(float max)
        {
            maxScroll = Mathf.Max(0f, max);
            offset = Mathf.Clamp(offset, 0f, maxScroll);
            Apply();
        }
        public void OnScroll(PointerEventData e)
        {
            if (Content == null || maxScroll <= 0f) return;
            offset = Mathf.Clamp(offset - e.scrollDelta.y * 28f, 0f, maxScroll);
            Apply();
            e.Use();
        }
        void Apply()
        {
            // 内容锚点在左上角，正向 y 把内容往上推，等于往下看后面的条目
            if (Content != null) Content.anchoredPosition = new Vector2(0f, offset);
        }
    }

    IEnumerator Repaint()
    {
        yield return null;
        foreach (var g in UnityEngine.Object.FindObjectsByType<MaskableGraphic>(FindObjectsSortMode.None))
            g.SetAllDirty();
    }

    // ---------------- 对外 ----------------

    // 提示条只留一条：连着报两条时后一条顶掉前一条。两条都钉在 (12,12)，
    // 叠在一起就是一团糊字（一次拒三个容器名就撞上了）。
    PixelText toastLabel;
    Coroutine toastCo;

    public void Toast(string msg)
    {
        Debug.Log("[toast] " + msg);
        bool onMenu = menuBackdrop != null && menuBackdrop.activeSelf;
        var layer = onMenu ? (RectTransform)menuBackdrop.transform : OverlayLayer;
        if (layer == null) return;
        if (toastLabel != null && toastLabel.transform.parent != layer)
        {
            Destroy(toastLabel.gameObject);
            toastLabel = null;
        }
        if (toastLabel == null)
        {
            toastLabel = Label(layer, msg, 12, 12, onMenu ? Theme.White : Theme.Ink, 2);
            toastLabel.gameObject.AddComponent<CanvasGroup>();
        }
        toastLabel.Text = msg;
        toastLabel.color = onMenu ? Theme.White : Theme.Ink;
        var g = toastLabel.GetComponent<CanvasGroup>();
        if (toastCo != null) StopCoroutine(toastCo);
        // 提示条淡入、停一会儿、淡出再销毁；直接 Destroy 会「啪」地一下消失
        toastCo = StartCoroutine(ToastLife(toastLabel.gameObject, g));
    }

    IEnumerator ToastLife(GameObject go, CanvasGroup g)
    {
        yield return Tween.Fade(this, g, 0f, 1f, .15f);
        yield return new WaitForSeconds(1.5f);
        yield return Tween.Fade(this, g, 1f, 0f, .3f);
        Destroy(go);
    }

    public void SetHeader(string title, string chapter, string[] goals) { }

    void ShowGuide(string mode)
    {
        if (GuideLayer == null) return;
        if (mode == "断点")
        {
            if (breakpointGuideShown) return;
            breakpointGuideShown = true;
        }
        else
        {
            if (traceGuideShown) return;
            traceGuideShown = true;
        }
        var card = Panel(GuideLayer, "FirstUseGuide", 430, 86, 580, 152, Theme.Cream);
        var border = card.gameObject.AddComponent<Outline>();
        border.effectColor = Theme.Border;
        border.effectDistance = new Vector2(2, -2);
        // 24x24 像素风女仆头像：使用色块构成，保持无需额外资源。
        var avatar = Panel(card, "MaidAvatar", 16, 18, 24, 24, Theme.Grape);
        Panel(avatar, "Hair", 2, 2, 20, 7, Theme.GrapeDeep);
        Panel(avatar, "Face", 4, 9, 16, 11, Theme.Cream);
        Panel(avatar, "EyeL", 7, 14, 2, 2, Theme.Ink);
        Panel(avatar, "EyeR", 15, 14, 2, 2, Theme.Ink);
        Panel(avatar, "Ribbon", 8, 1, 8, 3, Theme.White);
        string text = mode == "断点"
            ? "点节点旁的圆点设断点，\n运行到那里会停下，再用单步继续。"
            : "运行后底部逐事件显示变量与输出，\n用单步 / 继续推进，随时看数据变化。";
        var title = Label(card, mode + " · 首次使用", 54, 12, Theme.Ink, 2);
        var body = Label(card, text, 54, 44, Theme.Ink, 2);
        body.Wrap = true; body.WrapWidth = 520;
        var close = PixelButton.Create(card, "知道了", () => Destroy(card.gameObject), 72, 24);
        close.transform.localPosition = new Vector3(480, -120, 0);
    }

    // ---------------- 截图 / 演示 ----------------

    static bool HasArg(string a)
    {
        return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), a) >= 0;
    }

    static string ArgValue(string key)
    {
        var argv = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < argv.Length; i++)
            if (argv[i] == key) return argv[i + 1];
        return null;
    }

    void ApplyDisplayMode()
    {
        // 截图必须稳定使用 1440x900 窗口，避免桌面缩放或全屏导致验收失控。
        if (HasArg("-shot"))
        {
            // -shotres 1920x1080：换比例截图，验收窗口放大 / 非 16:10 比例下六块面板仍铺满
            int rw = W, rh = H;
            string res = ArgValue("-shotres");
            if (!string.IsNullOrEmpty(res))
            {
                int ix = res.IndexOf('x');
                int pw, ph;
                if (ix > 0 && int.TryParse(res.Substring(0, ix), out pw) && int.TryParse(res.Substring(ix + 1), out ph)
                    && pw >= 640 && ph >= 480) { rw = pw; rh = ph; }
            }
            Screen.SetResolution(rw, rh, FullScreenMode.Windowed);
            return;
        }

        if (HasArg("-fullscreen"))
        {
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            return;
        }

        if (HasArg("-windowed"))
        {
            SetWindowedResolution();
            return;
        }

        // 默认窗口尽量放大，但保留桌面边距，避免启动时被任务栏或屏幕边缘裁切。
        SetWindowedResolution();
    }

    void SetWindowedResolution()
    {
        int width = Mathf.Min(DefaultWindowW, Mathf.Max(640, Screen.currentResolution.width - WindowMargin));
        int height = Mathf.Min(DefaultWindowH, Mathf.Max(480, Screen.currentResolution.height - WindowMargin));
        Screen.SetResolution(width, height, FullScreenMode.Windowed);
    }

    public void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
        Toast(Screen.fullScreen ? "已切换全屏（F11 或再点全屏切回）" : "已切回窗口模式（F11 切换全屏）");
    }

    // F9 手动把当前所有卡片坐标+尺寸打进日志。-cardrect 只在启动那一刻打一次，
    // 拖动/撤销前后的位置要对比，就得随时能再倒一次；带序号是因为验收脚本按整行匹配等标记。
    int rectDumpSeq;
    // 「查看标准答案」在可滚动的题栏里，落点随滚动变，只能现场量给验收脚本
    RectTransform answerBtnRect;

    void Update()
    {
        if (compileDirty) { compileDirty = false; LiveCompileCheck(); }
        StepHeroBadgeScale();
        // 自动存档的去抖时钟：pending 由有变无就是刚落了一次盘，进度元信息跟着写一次
        bool wasPending = SaveSystem.HasPending;
        SaveSystem.Tick(Time.realtimeSinceStartupAsDouble);
        if (wasPending && !SaveSystem.HasPending) SaveProgressMeta();
        if (Input.GetKeyDown(KeyCode.F9) && GraphCanvas != null)
        {
            rectDumpSeq++;
            Debug.Log("[rectdump] #" + rectDumpSeq);
            Debug.Log(GraphCanvas.CardRectLog().TrimEnd());
            RectLog("答案按钮", answerBtnRect);
            // 按钮在可滚的题栏里，滚到底也点不动时得知道是谁盖在上面：
            // 视口被 RectMask2D 裁掉的那一截、还是底栏压过来的一条。
            HitRect("答案按钮", answerBtnRect);
            HitRect("题栏视口", inspectorView);
            RectLog("顶栏撤销", btnUndo != null ? btnUndo.Rect : null);
            RectLog("顶栏恢复", btnRedo != null ? btnRedo.Rect : null);
            RectLog("画布宿主", CanvasHost);
            HitRect("模块栏 创建变量", FindRect(paletteContent, "Row_vardecl"));
            // 这一格的 y 是「从屏幕底边往上」（和 Input.mousePosition 同系），
            // 换成验收脚本那套「从顶往下」就是 1 - 0.38 = 0.62
            HitProbe("画布空白", new Vector2(Screen.width * 0.30f, Screen.height * 0.38f));
        }
        if (Input.GetKeyDown(KeyCode.F11))
            Screen.fullScreen = !Screen.fullScreen;
    }

    // -demo：启动 1.2s 后自动跑一遍，配合 -shot 截播放中的画面
    IEnumerator DemoRoutine()
    {
        yield return new WaitForSeconds(1.2f);
        Debug.Log("[demo] 自动点一次「运行」");
        RunInput();
    }

    IEnumerator ShotRoutine()
    {
        // 播放器失焦会挂起主循环，截图协程就永远走不到；批处理截图期间允许后台跑
        Application.runInBackground = true;
        float wait = HasArg("-demo") ? 8f : 2f;   // 演示模式留足播放时间
        var argv = System.Environment.GetCommandLineArgs();
        for (int ai = 0; ai + 1 < argv.Length; ai++)
            if (argv[ai] == "-shotat") { float v; if (float.TryParse(argv[ai + 1], out v)) wait = v; }
        yield return new WaitForSeconds(wait);
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-shot") continue;
            Debug.Log("[shot] " + args[i + 1] + " step=" + (traceStep != null ? traceStep.Text : "-") + " msg=" + (traceMessage != null ? traceMessage.Text : "-"));
            ScreenCapture.CaptureScreenshot(args[i + 1]);
            yield return new WaitForSeconds(0.8f);
            Application.Quit();
        }
    }
}
