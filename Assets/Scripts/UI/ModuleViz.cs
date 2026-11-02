using System;
using System.Collections.Generic;
using Dayu.Core;
using UnityEngine;
using UnityEngine.UI;

// 模块卡片本身就是一个会动的结构：厂房、道岔、码头、传送带、木箱、货架、工作台、栈塔、拱门。
// 结构铺满整张卡，动画元件在结构里面动，标题写在结构的招牌上，身份写在结构的名牌上。
// 动画帧取自 Assets/Resources/Props 已登记的像素帧；结构几何用 16 色板画，不新画位图。
public sealed class ModuleViz : MonoBehaviour
{
    // 最大一档动画舞台（码头那帧本来就是 96x64）；每种结构实际占多高看 StageHFor
    public const float StageH = 64f;

    public string Kind;
    public GraphNode Node;
    // 名牌文字由 NodeCard 现算：参数一改就能原地刷新，不必重建整卡
    public Func<string> PlateSource;

    readonly List<Image> cyclers = new List<Image>();
    readonly List<PropSpriteSheet> sheets = new List<PropSpriteSheet>();
    readonly List<float> speeds = new List<float>();
    readonly List<RectTransform> scrollers = new List<RectTransform>();   // 传送带条纹 / 码头浪花：横向循环滚动
    readonly List<float> scrollStart = new List<float>();
    readonly List<float> scrollSpan = new List<float>();
    readonly List<float> scrollPhase = new List<float>();
    readonly List<float> scrollHomeY = new List<float>();
    RectTransform gearRt;
    Vector2 gearHome;
    RectTransform bobRt;
    Vector2 bobHome;
    Image lampImg;
    PixelText plateText;
    RectTransform plateBg;
    float plateMaxW;
    NodeCard card;
    float clock;

    // 名牌在卡片里的矩形（左上角为原点）：创建变量把隐形点击区盖在这块上，双击照样打开表达式小窗
    public Rect PlateArea;
    // 名牌这块位置同时是内嵌表达式的读数屏：有表达式字段的模块把字段摆到这里，不再另画一块名牌
    public bool PlateIsFieldSlot;
    // 计数循环第一行「循环变量：i」的位置：它不归表达式浮窗管，NodeCard 在这块上盖一个开参数窗的点击区
    public Rect LoopVarArea;
    // 计数循环的三行字（循环变量 / 计数起点 / 计数终点）：参数一改原地重写，不再画白色框
    PixelText loopVarLine, loopFromLine, loopToLine;

    // 每种节点对应一种结构；样例板还是纸面板，返回 null
    // 「只回一个值」的查询模块（常量、长度、是否空、数量）也没有 Exec 端口，
    // 不需要一整块 64px 的动画舞台：摘掉皮肤，卡片缩成装得下这一行字的最小形状（NodeCard.PickShape）。
    public static string KindFor(GraphNode n)
    {
        if (n == null) return null;
        switch (n.Type)
        {
            case "forloop":
            case "whileloop": return "factory";
            case "branch":    return "switch";
            case "output":    return "dock";
            case "input":     return "belt";
            case "vardecl":   return NodeCard.IsArrayDecl(n) ? "shelf" : "crate";
            case "varset":    return "crate_set";
            case "varget":    return "crate_get";
            case "binop":
            case "compare":
            case "logic":
            case "text_index": return "bench";
            case "structnew": return NodeCard.StructIsQueue(n) ? "quay" : "tower";
            case "stack_push":
            case "stack_pop":
            case "stack_top": return "tower";
            case "queue_push":
            case "queue_pop":
            case "queue_front": return "quay";
            case "entry":     return "gate";
        }
        return null;
    }

    public static float StageHFor(GraphNode n)
    {
        switch (KindFor(n))
        {
            // 拱门只有一根横梁一面旗，码头动画本身就是 96x64 的帧，其余结构按各自最小的可行几何给：
            // 木箱 / 货架 / 传送带只有一块名牌或一排格子，塔 / 道岔 / 厂房要放下摞位、钢轨和冲压机。
            case "gate": return 16f;
            case "belt":
            case "crate":
            case "shelf": return 28f;
            case "bench": return 34f;
            case "quay": return 40f;
            case "crate_set":
            case "crate_get":
            case "crate_seal": return 42f;
            case "switch":
            case "factory": return 48f;
            // 创建栈只是「立起一座空塔」，比压栈/出栈少一层动画，塔身收 12px 才塞得进关卡里 84 的列行距
            case "tower": return n != null && n.Type == "structnew" ? 36f : 48f;
            case "dock": return StageH;
        }
        return 0f;
    }

    // 名牌那块位置就是表达式的读数屏（木箱 / 工作台）：字段盖在名牌上，卡高不再为它单独留一行
    public static bool PlateHoldsField(string kind)
    {
        switch (kind)
        {
            case "crate":
            case "crate_set":
            case "crate_get":
            case "crate_seal":
            case "bench": return true;
        }
        return false;
    }

    // 数组货架的槽数：跟着「长度」参数走（1..8 格），画格子和算卡宽共用这一处，两边不会各算一套
    public static int ShelfCells(GraphNode n)
    {
        int cells = 3, parsed;
        string size = n != null ? GraphUtil.Get(n, "size") : null;
        if (!string.IsNullOrEmpty(size) && int.TryParse(size, out parsed) && parsed > 0) cells = Mathf.Clamp(parsed, 1, 8);
        return cells;
    }

    // 一格先按 18px 排，超过这个宽度就整体退到 14px 小格
    public static float ShelfCellSize(int cells) { return cells * 18f + (cells - 1) * 6f > 156f ? 14f : 18f; }
    public static float ShelfBandWidth(int cells)
    {
        float cw = ShelfCellSize(cells);
        return cells * cw + (cells - 1) * (cw >= 18f ? 6f : 4f);
    }

    // ---------------- 卡片轮廓 ----------------
    // 十一轮玩家原话：「全部都是长方形就显得很死板」。宽窄已经按内容分了九档，外轮廓还得各不相同。
    // 只做「往外凸」的加法，不挖凹口：卡片底板是一张纯色 Image，向内挖要拿画布背景色去遮，
    // 而画布是渐变 + 网格，遮出来的口子会露馅。凸出来的小块用和边框同色的像素台阶画，
    // 像素画的斜边本来就是台阶状的，看着反而像刻意的。
    // 坐标：x 从卡片左缘向右、y 从卡片上缘向下，负值就是凸到卡片外面；单位像素。
    // 命中区、端口、排版全都不动（这些块 raycastTarget = false），所以改形状不会碰交互。
    public static List<Rect> SilhouetteBlocks(string kind, float w, float h)
    {
        var L = new List<Rect>();
        if (kind == null || w <= 0f || h <= 0f) return L;
        switch (kind)
        {
            case "gate":              // 开始：上下各两块门墩，避开正中的执行端口
                L.Add(new Rect(w * .25f - 11f, -5f, 22f, 5f));
                L.Add(new Rect(w * .75f - 11f, -5f, 22f, 5f));
                L.Add(new Rect(w * .25f - 11f, h, 22f, 5f));
                L.Add(new Rect(w * .75f - 11f, h, 22f, 5f));
                break;
            case "factory":           // 循环：厂房屋面的四道宽垛口（偶数，中间留空给端口）
                for (int i = 0; i < 4; i++)
                    L.Add(new Rect(8f + i * (w - 24f - 16f) / 3f, -6f, 16f, 6f));
                break;
            case "tower":             // 栈：竖井顶上的六道细齿，比厂房的垛口又窄又高
                for (int i = 0; i < 6; i++)
                    L.Add(new Rect(6f + i * (w - 20f - 8f) / 5f, -8f, 8f, 8f));
                break;
            case "quay":              // 队列：左右各一枚车钩，和列车看板接得上
                L.Add(new Rect(-5f, h * .5f - 4f, 5f, 8f));
                L.Add(new Rect(w, h * .5f - 4f, 5f, 8f));
                break;
            case "crate":             // 创建变量：左上角一枚标签耳
                L.Add(new Rect(10f, -6f, 30f, 6f));
                break;
            case "crate_set":         // 赋值：四角包铁
                L.Add(new Rect(0f, -4f, 6f, 4f));
                L.Add(new Rect(w - 6f, -4f, 6f, 4f));
                L.Add(new Rect(0f, h, 6f, 4f));
                L.Add(new Rect(w - 6f, h, 6f, 4f));
                break;
            case "crate_get":         // 取回变量：左缘一支向左的像素箭头
                AddArrow(L, -1f, w, h);
                break;
            case "crate_seal":        // 常量：底缘一枚封蜡（偏左，正中有取值端口）
                L.Add(new Rect(w * .25f - 9f, h, 18f, 5f));
                break;
            case "shelf":             // 数组：两条底腿，货架的脚
                L.Add(new Rect(10f, h, 10f, 6f));
                L.Add(new Rect(w - 20f, h, 10f, 6f));
                break;
            case "bench":             // 运算台：台面右端夹一枚虎钳
                L.Add(new Rect(w - 26f, -5f, 14f, 5f));
                break;
            case "belt":              // 输入：上缘四级往右升的台阶，传送带的斜台（偶数级，中间让开端口）
            {
                float step = (w - 24f) / 4f;
                for (int i = 0; i < 4; i++)
                    L.Add(new Rect(12f + i * step, -3f - i * 1.5f, step, 3f + i * 1.5f));
                break;
            }
            case "switch":            // 分支：右缘一支向右的像素箭头（好几条出线）
                AddArrow(L, 1f, w, h);
                break;
            case "dock":              // 输出：上缘左右两块护舷，中间空出来的就是泊位
            {
                float bw = (w - 16f) * .3f;
                L.Add(new Rect(8f, -5f, bw, 5f));
                L.Add(new Rect(w - 8f - bw, -5f, bw, 5f));
                break;
            }
        }
        return L;
    }

    // 三级台阶拼成的像素箭头：贴着卡片的一侧边缘，越往外越短。dir = -1 朝左、1 朝右。
    static void AddArrow(List<Rect> L, float dir, float w, float h)
    {
        for (int i = 0; i < 3; i++)
        {
            float bh = 16f - i * 6f;
            float x = dir < 0f ? -4f * (i + 1) : w + 4f * i;
            L.Add(new Rect(x, h * .5f - bh * .5f, 4f, bh));
        }
    }

    // 结构内部的底色：大面积仍走冷 / 暖两层，类别色只薄薄罩一层
    public static Color BaseColor(string kind)
    {
        switch (kind)
        {
            case "factory":    return Theme.A(Theme.WhaleLight, .18f);
            case "switch":     return Theme.A(Theme.Salt, .55f);
            case "dock":       return Theme.A(Theme.Whale, .12f);
            case "belt":       return Theme.A(Theme.Mint, .14f);
            case "crate":
            case "crate_set":
            case "crate_get":
            case "crate_seal": return Theme.A(Theme.Honey, .16f);
            case "shelf":      return Theme.A(Theme.Mint, .16f);
            case "bench":      return Theme.A(Theme.Yellow, .18f);
            case "tower":      return Theme.A(Theme.Lavender, .18f);
            case "quay":       return Theme.A(Theme.Mint, .16f);
            case "gate":       return Theme.A(Theme.Whale, .10f);
        }
        return Theme.Paper;
    }

    // 创建变量 / 创建数组名牌上写的那一行：x = 0 / a[5]，一眼看懂这块存着什么
    public static string PlateText(GraphNode n)
    {
        if (n == null) return "";
        string name = GraphUtil.Get(n, "name");
        if (string.IsNullOrEmpty(name)) name = "v";
        if (NodeCard.IsArrayDecl(n))
        {
            string size = GraphUtil.Get(n, "size");
            return name + "[" + (string.IsNullOrEmpty(size) ? "" : size) + "]";
        }
        string init = GraphUtil.Get(n, "initExpr");
        return string.IsNullOrEmpty(init) ? name : name + " = " + init;
    }

    // zoneTop / zoneH：表达式字段与端口之间留给结构与动画元件的中段
    public static ModuleViz BuildSkin(Transform cardRoot, GraphNode node, float w, float h,
                                      float zoneTop, float zoneH, Func<string> plateSource)
    {
        string kind = KindFor(node);
        if (kind == null) return null;
        var go = new GameObject("Skin", typeof(RectTransform));
        go.transform.SetParent(cardRoot, false);
        var host = (RectTransform)go.transform;
        host.anchorMin = host.anchorMax = new Vector2(0f, 1f);
        host.pivot = new Vector2(0f, 1f);
        host.anchoredPosition = Vector2.zero;
        host.sizeDelta = new Vector2(w, h);
        var v = go.AddComponent<ModuleViz>();
        v.Kind = kind;
        v.Node = node;
        v.PlateSource = plateSource;
        v.card = cardRoot.GetComponent<NodeCard>();
        v.BuildScene(host, w, h, zoneTop, zoneH);
        return v;
    }

    string Plate()
    {
        string s = PlateSource != null ? PlateSource() : null;
        return s ?? "";
    }

    void BuildScene(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        switch (Kind)
        {
            case "factory":    BuildFactory(host, w, h, zoneTop, zoneH); break;
            case "switch":     BuildSwitch(host, w, h, zoneTop, zoneH); break;
            case "dock":       BuildDock(host, w, h, zoneTop, zoneH); break;
            case "belt":       BuildBelt(host, w, h, zoneTop, zoneH); break;
            case "crate":      BuildCrate(host, w, h, zoneTop, zoneH, 0); break;
            case "crate_set":  BuildCrate(host, w, h, zoneTop, zoneH, 1); break;
            case "crate_get":  BuildCrate(host, w, h, zoneTop, zoneH, 2); break;
            case "crate_seal": BuildCrate(host, w, h, zoneTop, zoneH, 3); break;
            case "shelf":      BuildShelf(host, w, h, zoneTop, zoneH); break;
            case "bench":      BuildBench(host, w, h, zoneTop, zoneH); break;
            case "tower":      BuildTower(host, w, h, zoneTop, zoneH); break;
            case "quay":       BuildQuay(host, w, h, zoneTop, zoneH); break;
            default:           BuildGate(host, w, h, zoneTop, zoneH); break;
        }
        CornerDress(host, w, h);
    }

    // 轮廓收边：底板还是矩形，但不同结构在四角做不同的收边，整屏卡片不再是一个模子的长方形。
    // 木箱钉角铁、栈塔压石脚加塔尖、拱门加拱肩、道岔立信号柱，其余留直角。
    void CornerDress(RectTransform host, float w, float h)
    {
        switch (Kind)
        {
            case "crate":
            case "crate_set":
            case "crate_get":
            case "crate_seal":
                CornerBrackets(host, w, h, 9f, Theme.A(Theme.WhaleDeep, .55f));
                break;
            case "tower":
                R(host, 0f, h - 5f, 6f, 5f, Theme.A(Theme.WhaleDeep, .7f));
                R(host, w - 6f, h - 5f, 6f, 5f, Theme.A(Theme.WhaleDeep, .7f));
                R(host, w * .5f - 3f, -4f, 6f, 4f, Theme.A(Theme.Lavender, .8f));
                break;
            case "gate":
                R(host, 0f, 0f, 8f, 8f, Theme.A(Theme.Whale, .35f));
                R(host, w - 8f, 0f, 8f, 8f, Theme.A(Theme.Whale, .35f));
                break;
            case "switch":
                R(host, 2f, h - 8f, 4f, 8f, Theme.A(Theme.Coral, .8f));
                R(host, w - 6f, h - 8f, 4f, 8f, Theme.A(Theme.Mint, .8f));
                break;
        }
    }

    void CornerBrackets(RectTransform host, float w, float h, float len, Color c)
    {
        R(host, 0f, 0f, len, 2f, c); R(host, 0f, 0f, 2f, len, c);
        R(host, w - len, 0f, len, 2f, c); R(host, w - 2f, 0f, 2f, len, c);
        R(host, 0f, h - 2f, len, 2f, c); R(host, 0f, h - len, 2f, len, c);
        R(host, w - len, h - 2f, len, 2f, c); R(host, w - 2f, h - len, 2f, len, c);
    }

    // ---------------- 结构 ----------------

    // 循环厂房：屋顶探出卡外半格，烟囱冒的汽轻轻起伏；墙里冲压机循环冲压、齿轮上下咬合，
    // 循环变量写在车间地坪上
    void BuildFactory(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        R(host, -3f, -6f, w + 6f, 14f, Theme.WhaleDeep);
        R(host, -3f, -6f, w + 6f, 3f, Theme.Whale);
        R(host, w - 26f, -14f, 10f, 10f, Theme.WhaleDeep);
        AddBob(R(host, w - 24f, -20f, 6f, 6f, Theme.A(Theme.Foam, .9f)).rectTransform);
        R(host, 0f, 8f, 10f, h - 20f, Theme.A(Theme.WhaleLight, .55f));
        R(host, w - 10f, 8f, 10f, h - 20f, Theme.A(Theme.WhaleLight, .55f));
        R(host, 14f, 12f, 12f, 10f, Theme.Foam);
        R(host, w - 26f, 12f, 12f, 10f, Theme.Foam);
        // 地坪抬到端口带上面：循环体 / 循环结束两个出口标签落在浅色底上才看得清
        R(host, 0f, h - 44f, w, 12f, Theme.A(Theme.WhaleDeep, .85f));
        R(host, 0f, h - 44f, w, 2f, Theme.Whale);
        Sign(host, NodeCard.TitleFor(Node), 8f, -4f, Theme.White, 1);

        float mid = zoneTop + Mathf.Max(0f, zoneH - 32f) * .5f;
        var gear = PropAssets.Get("factory_gear");
        if (gear != null && gear.FrameCount > 0)
        {
            gearRt = Sheet(host, gear[0], w * .5f - 44f, mid - 4f, 16f, 16f);
            gearHome = gearRt.anchoredPosition;
        }
        AddCycler(host, "factory_press", 48f, 32f, (w - 48f) * .5f, mid - 8f, 6f);
        if (Node != null && Node.Type == "forloop") { BuildLoopLines(host, w); return; }
        plateText = Sign(host, Plate(), 8f, h - 43f, Theme.White, 1);
        plateMaxW = w - 16f;
        PlateArea = new Rect(6f, h - 45f, plateMaxW, 14f);
    }

    // 计数循环卡面三行字：白色表达式框撤掉之后，起点 / 终点的读数写在这里，
    // 每行下面压一条隐形点击区（NodeCard 摆的），双击进小窗改。
    void BuildLoopLines(RectTransform host, float w)
    {
        float top = NodeCard.LoopLineTop("forloop", 0);
        loopVarLine = Sign(host, LoopLine(0, w), 16f, top, Theme.Ink, 1);
        loopFromLine = Sign(host, LoopLine(1, w), 16f, top + NodeCard.LoopLinePitch, Theme.Ink, 1);
        loopToLine = Sign(host, LoopLine(2, w), 16f, top + NodeCard.LoopLinePitch * 2f, Theme.Ink, 1);
        LoopVarArea = new Rect(12f, top - 3f, w - 24f, NodeCard.LoopLinePitch);
    }

    string LoopLine(int index, float cardW)
    {
        return ExpressionFieldUI.Ellipsize(NodeCard.ForloopLine(Node, index), cardW - 30f);
    }

    // 条件道岔：枕木 + 两条钢轨横穿整卡，道岔三帧来回扳，真 / 假两块小牌立在岔口两侧
    void BuildSwitch(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        R(host, 4f, 2f, w - 8f, 16f, Theme.Paper);
        Edge1(host, 4f, 2f, w - 8f, 16f, Theme.A(Theme.WhaleDeep, .5f));
        Sign(host, NodeCard.TitleFor(Node), 10f, 5f, Theme.Ink, 1);

        float ry = zoneTop + zoneH * .5f;
        R(host, 0f, ry - 7f, w, 14f, Theme.Salt);
        R(host, 0f, ry - 2f, w, 2f, Theme.WhaleDeep);
        R(host, 0f, ry + 3f, w, 2f, Theme.WhaleDeep);
        AddCycler(host, "switch", 96f, 48f, (w - 96f) * .5f, ry - 24f, 1.2f);

        R(host, 6f, zoneTop + 2f, 20f, 14f, Theme.A(Theme.Mint, .85f));
        Sign(host, "真", 11f, zoneTop + 4f, Theme.WhaleDeep, 1);
        R(host, w - 26f, zoneTop + 2f, 20f, 14f, Theme.A(Theme.Coral, .85f));
        Sign(host, "假", w - 21f, zoneTop + 4f, Theme.WhaleDeep, 1);
    }

    // 输出码头：顶板 + 木纹铺满，底下一条会起浪的海，码头两帧起伏
    void BuildDock(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        R(host, 0f, 0f, w, 12f, Theme.A(Theme.HoneyDeep, .9f));
        R(host, 0f, 4f, w, 1f, Theme.Honey);
        for (float y = 16f; y < h - 50f; y += 8f) R(host, 0f, y, w, 1f, Theme.A(Theme.HoneyDeep, .25f));
        // 海面抬到端口带上面：出口「执行」标签落在木板上才看得清
        R(host, 0f, h - 48f, w, 18f, Theme.Whale);
        for (int i = 0; i < 3; i++)
            AddScroll(R(host, i * (w / 3f) + 6f, h - 42f, 10f, 2f, Theme.A(Theme.Foam, .9f)).rectTransform,
                      0f, w - 10f, i, (w - 10f) / 3f);
        AddCycler(host, "dock", 96f, 64f, (w - 96f) * .5f, zoneTop + Mathf.Max(0f, zoneH - 64f) * .5f, 1.5f);
        Sign(host, NodeCard.TitleFor(Node), 8f, 1f, Theme.White, 1);
    }

    // 输入传送带：两侧立柱 + 一条循环滚动的带子，接料盘上写着读进哪个变量。
    // 带子贴在舞台下沿、接料盘坐在上沿，28px 的舞台就是这么排的。
    void BuildBelt(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        R(host, 0f, 0f, 8f, h, Theme.A(Theme.WhaleDeep, .7f));
        R(host, w - 8f, 0f, 8f, h, Theme.A(Theme.WhaleDeep, .7f));
        Sign(host, NodeCard.TitleFor(Node), 14f, 5f, Theme.Ink, 1);

        float by = zoneTop + zoneH - 12f;
        R(host, 8f, by, w - 16f, 12f, Theme.A(Theme.Ink, .8f));
        float span = w - 16f;
        for (int i = 0; i < 5; i++)
            AddScroll(R(host, 8f + i * (span / 5f), by + 2f, 4f, 8f, Theme.Honey).rectTransform,
                      8f, span - 4f, i, (span - 4f) / 5f);

        string plate = Plate();
        float tw = Mathf.Min(w - 20f, Mathf.Max(64f, PixelText.MeasureString(plate, 1).x + 16f));
        float tx = (w - tw) * .5f, ty = zoneTop;
        plateBg = R(host, tx, ty, tw, 16f, Theme.Paper).rectTransform;
        Edge1(host, tx, ty, tw, 16f, Theme.A(Theme.WhaleDeep, .5f));
        plateText = Sign(host, plate, tx + 6f, ty + 1f, Theme.Ink, 1);
        plateMaxW = tw - 12f;
        PlateArea = new Rect(tx, ty, tw, 16f);
    }

    // 变量木箱：竖板 + 四角包铁，中间一块名牌写 x = 0；设置 / 读取 / 常量各有小标记
    void BuildCrate(RectTransform host, float w, float h, float zoneTop, float zoneH, int variant)
    {
        for (float x = 6f; x < w - 6f; x += 12f) R(host, x, 6f, 1f, h - 12f, Theme.A(Theme.HoneyDeep, .22f));
        R(host, 0f, 0f, w, 6f, Theme.A(Theme.HoneyDeep, .75f));
        R(host, 0f, h - 6f, w, 6f, Theme.A(Theme.HoneyDeep, .75f));
        Corner(host, 0f, 0f, 1f, 1f); Corner(host, w, 0f, -1f, 1f);
        Corner(host, 0f, h, 1f, -1f); Corner(host, w, h, -1f, -1f);
        Sign(host, NodeCard.TitleFor(Node), 8f, 8f, Theme.Ink, 1);

        string plate = Plate();
        float ph = 26f;
        float pw = string.IsNullOrEmpty(plate) ? w - 40f : Mathf.Min(w - 16f, PixelText.MeasureString(plate, 2).x + 16f);
        // 没有小标记的箱子（创建变量）把名牌摆在舞台正中；带标记的（赋值 / 取回 / 常量）让名牌坐底，
        // 上面留 12px 给标记，标记才不会被招牌那一行吃掉
        float px = (w - pw) * .5f;
        float py = variant == 0 ? zoneTop + Mathf.Max(0f, zoneH - ph) * .5f : zoneTop + zoneH - ph - 2f;
        PlateArea = new Rect(px, py, pw, ph);
        PlateIsFieldSlot = true;
        if (!string.IsNullOrEmpty(plate))
        {
            plateBg = R(host, px, py, pw, ph, Theme.Paper).rectTransform;
            Edge1(host, px, py, pw, ph, Theme.A(Theme.Ink, .8f));
            // 2 号字一行是 CellH*2=28px，比名牌（26px）还高 2px：按字块高反推偏移才能上下留白相等。
            // 原来写死 py + 6 会把整段字压到名牌下沿外面（玩家截图量出来墨迹中心比框中心低 7px）。
            plateText = Sign(host, plate, px + 8f, py + (ph - PixelText.CellH * 2f) * .5f, Theme.Ink, 2);
            plateMaxW = pw - 16f;
        }

        if (variant == 1) { R(host, w * .5f - 2f, py - 10f, 4f, 7f, Theme.HoneyDeep); R(host, w * .5f - 5f, py - 5f, 10f, 3f, Theme.HoneyDeep); }
        if (variant == 2) { R(host, w * .5f - 2f, py - 6f, 4f, 7f, Theme.Whale); R(host, w * .5f - 5f, py - 8f, 10f, 3f, Theme.Whale); }
        if (variant == 3) R(host, px + pw - 10f, py - 4f, 8f, 8f, Theme.Coral);
    }

    // 数组货架：上下两道横梁，中间一格一格槽位，槽数跟着长度参数走，右上角挂 a[5] 名牌
    void BuildShelf(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        R(host, 0f, 0f, w, 6f, Theme.A(Theme.WhaleDeep, .75f));
        R(host, 0f, h - 6f, w, 6f, Theme.A(Theme.WhaleDeep, .75f));
        Sign(host, NodeCard.TitleFor(Node), 8f, 8f, Theme.Ink, 1);

        // 槽数与格子大小都由长度参数决定（卡宽按同一套算法量过，格子一定铺得下）
        int n = ShelfCells(Node);
        float cw = ShelfCellSize(n), gap = cw >= 18f ? 6f : 4f;
        float total = ShelfBandWidth(n);
        float x0 = (w - total) * .5f;
        float cy = zoneTop + Mathf.Max(0f, zoneH - cw) * .5f;
        for (int i = 0; i < n; i++)
        {
            float x = x0 + i * (cw + gap);
            R(host, x, cy, cw, cw, Theme.A(Theme.Foam, .9f));
            Edge1(host, x, cy, cw, cw, Theme.A(Theme.WhaleDeep, .7f));
            R(host, x + cw * .5f - 1f, cy + cw + 2f, 2f, 2f, Theme.Whale);
        }

        string plate = Plate();
        float pw = PixelText.MeasureString(plate, 1).x + 10f;
        plateBg = R(host, w - pw - 6f, 7f, pw, 14f, Theme.Paper).rectTransform;
        Edge1(host, w - pw - 6f, 7f, pw, 14f, Theme.A(Theme.WhaleDeep, .6f));
        plateText = Sign(host, plate, w - pw - 1f, 10f, Theme.Ink, 1);
        plateMaxW = pw - 10f;
        PlateArea = new Rect(w - pw - 6f, 7f, pw, 14f);
    }

    // 运算工作台：台面 + 两条桌腿，台上一只小机器，齿轮在转、指示灯交替闪，读数屏写着这一步算什么
    void BuildBench(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        Sign(host, NodeCard.TitleFor(Node), 8f, 5f, Theme.Ink, 1);
        float bw = 92f, bh = Mathf.Max(28f, zoneH - 10f);
        float bx = (w - bw) * .5f, by = zoneTop + 2f;
        R(host, bx, by, bw, bh, Theme.Paper);
        Edge1(host, bx, by, bw, bh, Theme.A(Theme.Ink, .7f));
        var gear = PropAssets.Get("factory_gear");
        if (gear != null && gear.FrameCount > 0)
        {
            gearRt = Sheet(host, gear[0], bx + 5f, by + bh * .5f - 8f, 16f, 16f);
            gearHome = gearRt.anchoredPosition;
        }
        lampImg = R(host, bx + bw - 12f, by + bh * .5f - 3f, 7f, 7f, Theme.Honey);
        string plate = Plate();
        if (!string.IsNullOrEmpty(plate))
        {
            plateText = Sign(host, plate, bx + 26f, by + bh * .5f - 5f, Theme.Ink, 1);
            plateMaxW = bw - 46f;
        }
        PlateArea = new Rect(bx, by, bw, bh);
        PlateIsFieldSlot = true;

        float ty = by + bh;
        R(host, 0f, ty, w, 4f, Theme.A(Theme.HoneyDeep, .8f));
        R(host, bx + 8f, ty + 4f, 6f, Mathf.Max(2f, h - 8f - ty - 4f), Theme.A(Theme.WhaleDeep, .8f));
        R(host, bx + bw - 14f, ty + 4f, 6f, Mathf.Max(2f, h - 8f - ty - 4f), Theme.A(Theme.WhaleDeep, .8f));
    }

    // 栈塔：底座 + 两侧立柱 + 一摞槽位，最上面一格轻轻起伏，等下一件入栈
    void BuildTower(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        Sign(host, NodeCard.TitleFor(Node), 8f, 5f, Theme.Ink, 1);
        float baseY = zoneTop + zoneH - 10f;
        R(host, w * .5f - 36f, baseY, 72f, 8f, Theme.A(Theme.Grape, .85f));
        R(host, w * .5f - 34f, zoneTop, 3f, Mathf.Max(2f, baseY - zoneTop), Theme.A(Theme.Grape, .7f));
        R(host, w * .5f + 31f, zoneTop, 3f, Mathf.Max(2f, baseY - zoneTop), Theme.A(Theme.Grape, .7f));
        for (int i = 0; i < 3; i++)
        {
            float y = baseY - 12f - i * 12f;
            R(host, w * .5f - 28f, y, 56f, 9f, Theme.A(Theme.Lavender, .30f + .18f * i));
            Edge1(host, w * .5f - 28f, y, 56f, 9f, Theme.A(Theme.Grape, .6f));
        }
        var top = R(host, w * .5f - 28f, zoneTop + 2f, 56f, 9f, Theme.A(Theme.Lavender, .8f));
        Edge1(host, w * .5f - 28f, zoneTop + 2f, 56f, 9f, Theme.Grape);
        AddBob(top.rectTransform);

        string plate = Plate();
        if (!string.IsNullOrEmpty(plate))
        {
            float pw = PixelText.MeasureString(plate, 1).x + 10f;
            plateBg = R(host, w - pw - 6f, 7f, pw, 14f, Theme.Paper).rectTransform;
            Edge1(host, w - pw - 6f, 7f, pw, 14f, Theme.A(Theme.Grape, .7f));
            plateText = Sign(host, plate, w - pw - 1f, 10f, Theme.Ink, 1);
            plateMaxW = pw - 10f;
            PlateArea = new Rect(w - pw - 6f, 7f, pw, 14f);
        }
    }

    // 队列码头：一条通道，右边进、左边出。三节车厢排在通道上，最左那节是队头（亮薄荷、轻轻起伏，
    // 等下一件出队），最右那节是队尾（蜜黄，刚进来的位置）。通道底色走冷色，跟栈塔的紫分开。
    void BuildQuay(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        Sign(host, NodeCard.TitleFor(Node), 8f, 5f, Theme.Ink, 1);
        float laneY = zoneTop + zoneH - 16f;
        R(host, 6f, laneY, w - 12f, 10f, Theme.A(Theme.Ink, .72f));
        float span = w - 16f;
        for (int i = 0; i < 4; i++)
            AddScroll(R(host, 8f + i * (span / 4f), laneY + 2f, 4f, 6f, Theme.A(Theme.Foam, .85f)).rectTransform,
                      8f, span - 4f, i, span / 4f);

        float cw = 16f, ch = 16f, gap = 4f, cy = zoneTop + 6f;
        for (int i = 0; i < 3; i++)
        {
            float x = 10f + i * (cw + gap);
            Color c = i == 0 ? Theme.A(Theme.Mint, .9f) : i == 2 ? Theme.A(Theme.Honey, .85f) : Theme.A(Theme.MintLight, .8f);
            var car = R(host, x, cy, cw, ch, c);
            Edge1(host, x, cy, cw, ch, Theme.A(Theme.WhaleDeep, .5f));
            if (i == 0) AddBob(car.rectTransform);
        }

        // 两头各钉一枚箭头：左边出、右边进，看形状就知道队伍往哪走
        R(host, 2f, cy + 7f, 7f, 2f, Theme.Coral);
        R(host, 2f, cy + 5f, 2f, 2f, Theme.Coral);
        R(host, 2f, cy + 9f, 2f, 2f, Theme.Coral);
        R(host, w - 9f, cy + 7f, 7f, 2f, Theme.Coral);
        R(host, w - 4f, cy + 5f, 2f, 2f, Theme.Coral);
        R(host, w - 4f, cy + 9f, 2f, 2f, Theme.Coral);

        string plate = Plate();
        if (!string.IsNullOrEmpty(plate))
        {
            float pw = PixelText.MeasureString(plate, 1).x + 10f;
            plateBg = R(host, w - pw - 6f, 7f, pw, 14f, Theme.Paper).rectTransform;
            Edge1(host, w - pw - 6f, 7f, pw, 14f, Theme.A(Theme.Whale, .7f));
            plateText = Sign(host, plate, w - pw - 1f, 10f, Theme.Ink, 1);
            plateMaxW = pw - 10f;
            PlateArea = new Rect(w - pw - 6f, 7f, pw, 14f);
        }
    }

    // 开始拱门：两根门柱 + 横梁，梁下挂一面轻轻摆动的旗
    void BuildGate(RectTransform host, float w, float h, float zoneTop, float zoneH)
    {
        R(host, 6f, 12f, 8f, h - 12f, Theme.A(Theme.WhaleDeep, .85f));
        R(host, w - 14f, 12f, 8f, h - 12f, Theme.A(Theme.WhaleDeep, .85f));
        R(host, 0f, 0f, w, 12f, Theme.Whale);
        R(host, 0f, 0f, w, 3f, Theme.WhaleLight);
        AddBob(R(host, w * .5f - 6f, 16f, 12f, 8f, Theme.Coral).rectTransform);
        Sign(host, NodeCard.TitleFor(Node), 10f, 1f, Theme.White, 1);
        R(host, 0f, h - 6f, w, 6f, Theme.A(Theme.WhaleDeep, .5f));
    }

    // ---------------- 小工具 ----------------

    Image R(RectTransform host, float x, float y, float w, float h, Color color)
    {
        var img = GraphView.Tile(host, "SkinPart", color, false);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return img;
    }

    // 1px 描边框：四条细边，比 Outline 组件便宜也不糊像素
    void Edge1(RectTransform host, float x, float y, float w, float h, Color color)
    {
        R(host, x, y, w, 1f, color);
        R(host, x, y + h - 1f, w, 1f, color);
        R(host, x, y, 1f, h, color);
        R(host, x + w - 1f, y, 1f, h, color);
    }

    void Corner(RectTransform host, float cx, float cy, float sx, float sy)
    {
        float x = sx > 0 ? cx : cx - 10f;
        float y = sy > 0 ? cy : cy - 10f;
        R(host, x, y, 10f, 3f, Theme.A(Theme.Ink, .8f));
        R(host, x, y, 3f, 10f, Theme.A(Theme.Ink, .8f));
    }

    RectTransform Sheet(RectTransform host, Sprite sp, float x, float y, float w, float h)
    {
        var go = new GameObject("SkinSheet", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(host, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        var img = go.GetComponent<Image>();
        img.sprite = sp;
        img.type = Image.Type.Simple;
        img.raycastTarget = false;
        return rt;
    }

    PixelText Sign(RectTransform host, string text, float x, float y, Color color, int scale)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var t = PixelText.Create(host, text, color, scale);
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        return t;
    }

    void AddCycler(RectTransform host, string sheetKey, float w, float h, float x, float y, float fps)
    {
        var sheet = PropAssets.Get(sheetKey);
        if (sheet == null || sheet.FrameCount == 0) return;
        var rt = Sheet(host, sheet[0], x, y, w, h);
        cyclers.Add(rt.GetComponent<Image>());
        sheets.Add(sheet);
        speeds.Add(fps);
    }

    void AddScroll(RectTransform rt, float startX, float span, float index, float pitch)
    {
        scrollers.Add(rt);
        scrollStart.Add(startX);
        scrollSpan.Add(Mathf.Max(1f, span));
        scrollPhase.Add(index * pitch);
        scrollHomeY.Add(rt.anchoredPosition.y);
    }

    void AddBob(RectTransform rt)
    {
        if (bobRt == null) { bobRt = rt; bobHome = rt.anchoredPosition; }
    }

    // 名牌跟着参数走：改了名字 / 初始值 / 长度，文字重排一遍，木箱的名牌底板跟着伸缩
    public void RefreshPlate()
    {
        // 计数循环没有名牌，改的是那三行字
        if (loopFromLine != null)
        {
            float cw = ((RectTransform)loopFromLine.transform.parent).sizeDelta.x;
            loopVarLine.Text = LoopLine(0, cw);
            loopFromLine.Text = LoopLine(1, cw);
            loopToLine.Text = LoopLine(2, cw);
            return;
        }
        if (plateText == null) return;
        string plate = Plate();
        if (plateMaxW > 0f) plate = ExpressionFieldUI.Ellipsize(plate, plateMaxW);
        plateText.Text = plate;
        if (plateBg == null || Kind.IndexOf("crate", StringComparison.Ordinal) < 0) return;
        float pw = Mathf.Min(plateMaxW + 16f, PixelText.MeasureString(plate, 2).x + 16f);
        float px = ((RectTransform)plateBg.parent).sizeDelta.x * .5f - pw * .5f;
        plateBg.sizeDelta = new Vector2(pw, plateBg.sizeDelta.y);
        plateBg.anchoredPosition = new Vector2(px, plateBg.anchoredPosition.y);
        plateText.rectTransform.anchoredPosition = new Vector2(px + 8f, plateText.rectTransform.anchoredPosition.y);
        PlateArea = new Rect(px, PlateArea.y, pw, PlateArea.height);
    }

    void Update()
    {
        clock += Time.unscaledDeltaTime;
        // 正在执行的那一块转得快一点：一眼看得出程序走到哪了
        float boost = card != null && (card.Highlighted || card.Selected) ? 2f : 1f;
        for (int i = 0; i < cyclers.Count; i++)
        {
            var sheet = sheets[i];
            if (sheet == null || sheet.FrameCount <= 1) continue;
            int f = Mathf.FloorToInt(clock * speeds[i] * boost) % sheet.FrameCount;
            if (cyclers[i] != null && cyclers[i].sprite != sheet[f]) cyclers[i].sprite = sheet[f];
        }
        for (int i = 0; i < scrollers.Count; i++)
        {
            if (scrollers[i] == null) continue;
            // 等距铺满一条带子：每格各自往前走，越过右端回到左端，条纹间距始终不变
            float d = (clock * 24f * boost + scrollPhase[i]) % scrollSpan[i];
            scrollers[i].anchoredPosition = new Vector2(scrollStart[i] + d, scrollHomeY[i]);
        }
        if (gearRt != null) gearRt.anchoredPosition = gearHome + new Vector2(0f, Mathf.Sin(clock * 5f) * 1.5f);
        if (bobRt != null) bobRt.anchoredPosition = bobHome + new Vector2(0f, Mathf.Sin(clock * 3f) * 1.5f);
        if (lampImg != null) lampImg.color = Mathf.FloorToInt(clock * 2f) % 2 == 0 ? Theme.Honey : Theme.Coral;
    }
}
