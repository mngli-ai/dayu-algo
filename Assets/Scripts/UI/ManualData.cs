// ============================================================================
// ManualData.cs —— 《大肥鱼の算法大冒险》「说明书」的内容与数据层。
//
// 这个文件只装数据与纯函数，不引用 UnityEngine，也不碰任何界面，
// 渲染与接线（可滚动弹窗 + 菜单入口）由 App.cs 侧负责。
//
// 术语口径（玩家逐条定过，全文不许漂移）：
//   条件分支叫「如果」；读取 / 输出叫「输入」「输出」；数据结构叫「容器」；
//   「设置变量」叫「赋值」；栈 / 队列成员统一用陈述式：空了、栈顶元素、队头元素、个数；
//   容器实例默认名只能用英文字母 / 数字 / 下划线（统一 st），绝不写中文实例名。
//
// 全文用中文，英文只允许出现在代码符号里（变量名、表达式、快捷键）。
//
// 长度口径（位图字体按像素量宽，题栏 / 弹窗宽度有限，超了会溢出）：
//   Summary（功能一句话）≤ 34 字；
//   Steps（操作步骤，2~4 条）每条 ≤ 30 字；
//   Pitfall（最常见的一个坑）≤ 40 字；
//   Example（可照抄的写法）≤ 24 字。
// 「字」按 Unicode 码点数计，标点各算一字，ASCII 字母数字同样各算一字（从严）。
// ============================================================================

using System;
using System.Collections.Generic;

// 说明书的一条。Steps 是操作步骤数组，Example 是能直接照抄的写法，
// 没有对应表达式的流程 / 操作类条目一律写「无表达式」。
public struct ManualEntry
{
    public string Title;      // 标题，与左侧栏卡名或操作名对齐
    public string Group;      // 手风琴分组，取值见 Groups()
    public string Summary;    // 一句话功能
    public string[] Steps;    // 2~4 条操作步骤
    public string Pitfall;    // 最常见的一个坑
    public string Example;    // 一个可直接照抄的写法；没有就写「无表达式」
}

public static class ManualData
{
    // 构造一条，参数顺序按「读起来顺」排：标题、分组、功能、坑、示例，最后是可变的操作步骤。
    static ManualEntry E(string title, string group, string summary, string pitfall, string example, params string[] steps)
    {
        return new ManualEntry { Title = title, Group = group, Summary = summary, Steps = steps, Pitfall = pitfall, Example = example };
    }

    // 「无表达式」这个占位在 C++ 与校验里都用同一个字面串，单测靠它识别示例是否可空。
    public const string NoExample = "无表达式";

    // ---- 分组名（手风琴顺序）。样例这一类本轮从左侧栏去掉，两块样例板改挂在「运行与判定」。----
    static readonly string GroupIO = "输入输出";
    static readonly string GroupFlow = "流程控制";
    static readonly string GroupVar = "变量";
    static readonly string GroupStruct = "容器";
    static readonly string GroupCanvas = "画布与操作";
    static readonly string GroupExpr = "表达式";
    static readonly string GroupRun = "运行与判定";

    public static readonly ManualEntry[] All = {
        // ---------------- 输入输出 ----------------
        E("输入", GroupIO,
          "从关卡数据读一个值存进变量",
          "写入变量要先有同名的创建变量，否则报还没声明",
          "a",
          "拖到执行链上，接在开始后面",
          "双击选类型并填写入变量名",
          "一个输入只装一个数，要读多个就摆多个"),

        E("输出", GroupIO,
          "把表达式算出的数亮到屏幕上",
          "输出表达式留空等于没打印，这行会少一个数",
          "sum",
          "接在执行链末端",
          "双击在输出表达式里写变量或算式",
          "运行或提交时它打印一行"),

        // ---------------- 流程控制 ----------------
        E("开始", GroupFlow,
          "整张图唯一的入口，执行从这里往下走",
          "多放一块开始会报必须存在唯一的开始节点",
          NoExample,
          "每关自带一块，必须有且只有一个",
          "把它底部的出口连到第一个模块",
          "删掉它整张图就编译不过"),

        E("如果", GroupFlow,
          "按条件把执行分成真、假两条路",
          "条件表达式要算出真或假，写成数字会报类型不对",
          "a >= 60",
          "双击条件表达式写一个判断",
          "真出口接满足时要做的事",
          "假出口接不满足时要做的事"),

        E("计数循环", GroupFlow,
          "从起点到终点按次数重复一段模块",
          "循环体没接模块会空转，跟踪只见第几轮",
          "i",
          "填起始、结束表达式和循环变量名",
          "循环体出口接要重复的模块",
          "循环结束出口接收尾要做的模块"),

        E("条件循环", GroupFlow,
          "条件为真就一直重复，直到变假",
          "条件永远为真会死循环，体里要把计数往退出推",
          "n > 0",
          "双击条件表达式写一个判断",
          "循环体接重复的模块，体里推进退出条件",
          "循环结束出口接收尾的模块"),

        // ---------------- 变量 ----------------
        E("创建变量", GroupVar,
          "声明一个有名字的格子用来存值",
          "在它上面的模块里读它，会报这个变量还没创建",
          "a",
          "双击填名字、选类型，可给初值表达式",
          "名字只用字母数字下划线",
          "接在执行链上，之后的模块才看得见它"),

        E("赋值", GroupVar,
          "把一个新值写进已经建好的变量",
          "赋值给没创建过的变量会报错，并列出已有名字",
          "a[i] = x",
          "双击在赋值语句里写名字和表达式",
          "也支持 s += x 这种复合写法",
          "数组元素写成 a[i] = x，下标来自循环变量"),

        // ---------------- 容器 ----------------
        E("创建数组", GroupStruct,
          "开一排按号编好的格子，用来存一串数",
          "数组不能整块连到端口，要写 a[i] 取一个元素",
          "a",
          "从容器里拖进来，类型选数组",
          "要排在读长度 n 之后，才知道铺几格",
          "用 a[i] 取一格，用 a.size 数几格"),

        E("创建栈", GroupStruct,
          "建一个只能从最上面拿取的栈实例",
          "没先建档就压入弹出，会报还没有这个结构实例",
          "st",
          "拖到最前面，实例名默认 st",
          "压入弹出之前要先有这块建档",
          "一关通常只放一个栈，别建第二个"),

        E("压入", GroupStruct,
          "把一个元素放到栈的最上面",
          "压入表达式留空又没连线，会报缺一个输入",
          "x",
          "接在执行链上，实例选 st",
          "双击写压入表达式，或连一条数据线进来",
          "值可以来自输入变量或直接写算式"),

        E("弹出", GroupStruct,
          "拿走栈最上面那一个元素",
          "空栈弹出拿不到东西，操作前先看 st.empty",
          "st.top",
          "接在执行链上，实例选 st",
          "弹出是拿走，只想看一眼用栈顶元素 st.top",
          "常和输出连用：先输出 st.top 再弹出"),

        E("创建队列", GroupStruct,
          "建一个按先后排队进出的队列实例",
          "拿队列的模块去操作栈，会报这个实例用不了",
          "st",
          "拖到最前面，实例名默认 st",
          "入队出队之前先建档",
          "一关通常只放一个实例"),

        E("入队", GroupStruct,
          "把一个元素排到队伍最末尾",
          "入队表达式留空又没连线，会报缺一个输入",
          "x",
          "接在执行链上，实例选 st",
          "双击写入队表达式，或连一条数据线进来",
          "排在队尾，出队时最后才走"),

        E("出队", GroupStruct,
          "放走队伍最前面那一个元素",
          "空队出队拿不到东西，操作前先看 st.empty",
          "st.front",
          "接在执行链上，实例选 st",
          "出队是放走队头，只想看一眼用队头元素 st.front",
          "常和输出连用：先输出 st.front 再出队"),

        // ---------------- 样例板（左侧栏去掉样例分类，两块板永远留在画布上） ----------------
        E("样例输入板", GroupRun,
          "展示当前这条样例读进来的数据",
          "它只负责显示，不接线也不影响判定结果",
          NoExample,
          "常驻画板，删也删不掉",
          "选中它切换看第几组样例",
          "运行或提交时它显示当前输入"),

        E("样例对比板", GroupRun,
          "拿运行输出的结果和期望答案比对",
          "没有输出值就没得比，先把执行链接满",
          NoExample,
          "常驻画板，删也删不掉",
          "接住输出这一路跑出来的结果",
          "对上亮 AC，对不上亮 WA"),

        // ---------------- 画布与操作 ----------------
        E("连线与端口操作", GroupCanvas,
          "把执行线从一个出口拉到下一个入口",
          "同一个出口只走一条执行线，新线会顶掉旧线",
          NoExample,
          "按住模块右侧出口，拖到下个模块入口松手",
          "离得远可以先点出口、再点入口",
          "拉线途中点空白或按 Esc 就取消这根线"),

        E("选中与删除模块", GroupCanvas,
          "单击选中、双击改参、右键删除",
          "删错了按 Ctrl+Z 撤销回来，先别点重来",
          NoExample,
          "单击模块选中它，按住左键拖动能挪位置",
          "双击进表达式或参数浮窗改这一块的填空",
          "右键模块直接把它删掉"),

        E("框选与批量搬移", GroupCanvas,
          "拖一个框一次选中多个模块一起动",
          "折叠起来的循环体不参与框选",
          NoExample,
          "空白处按住左键拖出一个框，框里都选中",
          "按住 Ctrl 再点卡片可以加选或减选",
          "选中后整组拖动，Ctrl+C 复制、Ctrl+V 粘贴"),

        E("滚轮缩放与边缘跟随", GroupCanvas,
          "滚轮平移画布，Ctrl 加滚轮缩放",
          "找不到模块时先点适应，把整张图收回来",
          NoExample,
          "普通滚轮上下平移整张图",
          "Ctrl 加滚轮放大或缩小",
          "拖到画布边缘时镜头会自动跟着走"),

        E("撤销与恢复", GroupCanvas,
          "退回或重做画布上的一步改动",
          "打字时 Z 归输入框，撤销只在画布上生效",
          NoExample,
          "Ctrl+Z 撤销一步，Ctrl+Y 恢复一步",
          "Ctrl+Shift+Z 也能恢复下一步",
          "顶栏撤销、恢复按钮按历史深浅自动置灰"),

        E("自动存档与读档", GroupCanvas,
          "改图就自动存，进关自动读回你的图",
          "预览标准答案时挂起存档，改它不会存进进度",
          NoExample,
          "改一个模块就会自动落盘这一关",
          "下次进关原样读回你自己画的那张图",
          "点重来会删掉这一关的存档、重铺初始图"),

        // ---------------- 表达式 ----------------
        E("表达式速查", GroupExpr,
          "一格里能写的运算符、成员和函数",
          "位运算只认整数，写成小数会被拒绝",
          "s[i] == '('",
          "加减乘除取余 + - * / %",
          "比较 == != < <= > >=，逻辑 && || !",
          "数组取格 a[i]，容器读成员 st.size",
          "数学函数 abs sqrt pow min max 都支持"),

        E("自动补全", GroupExpr,
          "打完点号列出可用成员，再按 Tab 补全",
          "光标要停在变量名那一段才有候选",
          "st.top",
          "变量名后输入一个点号，弹出成员列表",
          "上下键选择，Tab 或回车补全高亮那颗",
          "点某一行也算采纳这个成员",
          "Esc 只收起列表，不动你写了一半的草稿"),

        E("容器成员访问", GroupExpr,
          "用点号读容器或数组的状态",
          "老图写 a.length 也认，但推荐统一用 size",
          "st.size",
          "个数写 st.size 或 a.size",
          "空了写 st.empty，算出真或假",
          "栈顶元素写 st.top，队头元素写 st.front",
          "成员名用英文，中文只是浮窗里的释义"),

        E("位运算与复合赋值", GroupExpr,
          "支持位运算和 += -= 这类简写",
          "位运算结果仍是整数，别当成小数来用",
          "s += 1",
          "位运算 & | ^ << >> 和取反 ~",
          "复合赋值 += -= *= /= %= &= 都认",
          "带下标也能写 a[i] <<= 1"),

        // ---------------- 运行与判定 ----------------
        E("运行与单步", GroupRun,
          "看整片数字流动，或一步一步走",
          "数字不动多半是执行链没接满，先检查连线",
          NoExample,
          "运行：让整片数字一次流完",
          "单步：点一下走一步，带搬运动画",
          "跟踪：不先跑完，逐条看事件顺序"),

        E("断点", GroupRun,
          "让运行停在某个模块前面",
          "一个断点都没设时，点断按钮会直接跑到底",
          NoExample,
          "点卡片左侧的小圆点设一个断点",
          "再运行就会停在标了断点的模块前",
          "点顶栏断点按钮跳到下一个断点"),

        E("提交与判定", GroupRun,
          "跑完本关所有样例，全对才算通过",
          "有抽查没过就显示还有几条没过，别只看样板",
          NoExample,
          "点提交，把本关样例逐条搬进对比板核对",
          "样板是公开的，抽查是隐藏数据不公开内容",
          "每条对上亮 AC、对不上亮 WA",
          "样板加抽查全对才记通关并解锁下一关"),

        E("查看标准答案", GroupRun,
          "在画布上铺出这关的解，让你照着看",
          "预览里不能提交、不能复制粘贴，先点退出预览",
          NoExample,
          "在右侧题栏点查看标准答案",
          "预览只铺图给你看，改它不会存进进度",
          "点退出预览原样还回你自己那张图"),
    };

    // ---------------- 操作总表 ----------------
    // 逐条按代码里的真实绑定核对：GraphView.OnPointerDown/Up/Drag/Scroll、
    // GraphView.Update、NodeCard.OnPointerDown/Click、ExpressionPopover.Update、App.Update。
    public static readonly (string Key, string What)[] Controls = {
        ("左键单击模块", "选中它，按住再拖动能整体挪位置"),
        ("左键双击模块", "打开表达式或参数浮窗改这一块的填空"),
        ("左键在空白拖动", "拖出一个框，框里的模块一起选中"),
        ("左键端口到端口", "从一个出口拖到下个入口，接一根执行线"),
        ("右键模块", "直接删掉这个模块"),
        ("右键或中键在空白", "按住拖动平移整张画布"),
        ("滚轮", "上下平移画布；拖到边缘时镜头自动跟随"),
        ("Ctrl 加滚轮", "放大或缩小画布"),
        ("Enter", "在表达式浮窗里提交这一句；补全列表开着时先采纳候选"),
        ("Esc", "取消正在拉的线；浮窗开着时还原原值；补全列表开着时只收列表"),
        ("Tab", "浮窗里跳到下一个填空；补全列表开着时补全高亮那颗"),
        ("Shift 加 Tab", "浮窗里跳到上一个填空"),
        ("Delete", "删除选中的模块；没选中模块时删掉鼠标悬着的那根线"),
        ("Ctrl+C 与 Ctrl+V", "复制并粘贴选中的模块（看答案时不给）"),
        ("Ctrl+Z", "撤销画布上的一步改动"),
        ("Ctrl+Y 或 Ctrl+Shift+Z", "恢复一步被撤销的改动"),
        ("顶栏 运行", "让整片数字一次流动跑完"),
        ("顶栏 单步", "点一下往前走一步"),
        ("顶栏 断点", "运行到下一个设了断点的模块前停下"),
        ("顶栏 跟踪", "不先整片跑完，逐条查看事件顺序"),
        ("顶栏 重来", "删掉本关存档，重铺关卡自带的初始图"),
        ("顶栏 撤销 恢复", "按画布历史的深浅自动置灰"),
        ("顶栏 提交", "跑所有样例逐条比对，全对才记通关"),
        ("顶栏 适应", "按内容包围盒把整张图居中铺开"),
        ("顶栏 布局", "在上下和左右两种端口朝向间切换"),
        ("顶栏 速度", "循环切换运行播放的快慢"),
        ("顶栏 音效", "静音或恢复音效"),
        ("顶栏 全屏", "切换全屏窗口"),
        ("顶栏 菜单", "回到主菜单"),
        ("F9", "调试用：把当前卡片位置打进日志，玩家不需要"),
    };

    // ---------------- 纯分页 / 检索 API ----------------

    // 一共几页。entriesPerPage 非正数时当作「全部塞进一页」。
    public static int PageCount(int entriesPerPage)
    {
        if (entriesPerPage <= 0) return All.Length > 0 ? 1 : 0;
        return (All.Length + entriesPerPage - 1) / entriesPerPage;
    }

    // 取第 pageIndex 页（从 0 起）。pageIndex 越界一律夹到合法页，绝不抛异常、绝不返回越界切片。
    public static ManualEntry[] Page(int pageIndex, int entriesPerPage)
    {
        if (entriesPerPage <= 0) entriesPerPage = All.Length;
        if (entriesPerPage <= 0) return new ManualEntry[0];
        int pages = PageCount(entriesPerPage);
        if (pages <= 0) return new ManualEntry[0];
        if (pageIndex < 0) pageIndex = 0;
        if (pageIndex > pages - 1) pageIndex = pages - 1;
        int start = pageIndex * entriesPerPage;
        int len = All.Length - start;
        if (len > entriesPerPage) len = entriesPerPage;
        if (len < 0) len = 0;
        var arr = new ManualEntry[len];
        for (int i = 0; i < len; i++) arr[i] = All[start + i];
        return arr;
    }

    // 按 Title / Group / Summary 做子串匹配（区分大小写、按序号比较）。空串或未给返回全部。
    public static ManualEntry[] Search(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return (ManualEntry[])All.Clone();
        var list = new List<ManualEntry>();
        foreach (var e in All)
            if (Has(e.Title, keyword) || Has(e.Group, keyword) || Has(e.Summary, keyword)) list.Add(e);
        return list.ToArray();
    }

    static bool Has(string s, string k) { return s != null && k != null && s.IndexOf(k, StringComparison.Ordinal) >= 0; }

    // 手风琴分组顺序，按 NodeCatalog 的分类口径给（样例这一类本轮去掉，两块板并入运行与判定）。
    public static string[] Groups()
    {
        return new[] { GroupIO, GroupFlow, GroupVar, GroupStruct, GroupCanvas, GroupExpr, GroupRun };
    }

    // 估算一条目在界面上占几行：纯算术，无 UnityEngine 依赖。
    // wrapWidthPx 是一栏的可排宽度（像素），charPx 是一个全角汉字的像素宽；
    // 半角（字母数字与 ASCII 标点）按 charPx 的一半估，空字段算 0 行，标题固定占 1 行。
    public static int MeasureEntryLines(ManualEntry e, int wrapWidthPx, int charPx)
    {
        if (wrapWidthPx <= 0 || charPx <= 0) return 1;
        int lines = 1;                       // 标题行
        lines += WrapLines(e.Summary, wrapWidthPx, charPx);
        if (e.Steps != null)
            foreach (var s in e.Steps) lines += WrapLines(s, wrapWidthPx, charPx);
        lines += WrapLines(e.Pitfall, wrapWidthPx, charPx);
        lines += WrapLines(e.Example, wrapWidthPx, charPx);
        return lines;
    }

    // 一段文字按宽度折成几行：宽度 = 全角按 charPx、半角按半宽累加；空串 0 行，至少 1 行。
    static int WrapLines(string text, int wrapWidthPx, int charPx)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int half = charPx / 2; if (half < 1) half = 1;
        int width = 0;
        foreach (char c in text) width += IsWide(c) ? charPx : half;
        int lines = (width + wrapWidthPx - 1) / wrapWidthPx;
        return lines < 1 ? 1 : lines;
    }

    // 全角判定：汉字、假名、日韩兼容区，以及 CJK 标点（「」、。（）…）都按整宽算。
    static bool IsWide(char c)
    {
        return c >= '\u3000' && c <= '\u9fff';
    }
}
