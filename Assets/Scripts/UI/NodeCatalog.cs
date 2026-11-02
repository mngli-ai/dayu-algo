// 左侧模块栏的可拖列表。Key 是条目自身的 id，NodeType 才是落进画布的节点类型，
// Preset 是创建时预置的参数（key,value 成对），让同一个节点类型能有两个入口（创建变量 / 创建数组）。
// 运算（binop）、读取变量（varget）、常量、比较 / 逻辑 / 取字符 / 长度、栈与队列的查询（栈顶、队头、空了、个数）
// 全都由内嵌表达式取代，不再出现在列表里；NodeType 定义仍保留，旧关卡里的老节点照样能加载。
// Cat 是一级目录，Sub 是二级目录（目前只有「容器」需要再分数组 / 栈）。
// 「创建结构」(structnew) 以「创建栈」的名字回到列表，只服务栈这一章：关卡的 StackStart 里给它备好实例 st。
public struct NodeCatalogEntry
{
    public string Key, Title, NodeType, Cat, Sub, ColorKey;
    public string[] Preset;
    public NodeCatalogEntry(string key, string title, string nodeType, string cat, string colorKey, params string[] preset)
    { Key = key; Title = title; NodeType = nodeType; Cat = cat; Sub = null; ColorKey = colorKey; Preset = preset; }
}

public static class NodeCatalog
{
    public static readonly NodeCatalogEntry[] All = {
        // 输入输出置顶：每一关第一步都是把数据读进来
        new("input", "输入", "input", "io", "cyan"),
        new("output", "输出", "output", "io", "cyan"),

        new("entry", "开始", "entry", "flow", "whale"),
        new("branch", "如果", "branch", "flow", "whale"),
        new("forloop", "计数循环", "forloop", "flow", "whale"),
        new("whileloop", "条件循环", "whileloop", "flow", "whale"),

        new("vardecl", "创建变量", "vardecl", "var", "pink"),
        new("varset", "赋值", "varset", "var", "pink"),

        // 数组与栈都属于「容器」这一层，二级目录按玩家叫得出名字的结构分。
        // 数组元素写入不单列模块：在「赋值」里直接写 a[i] = x
        new("array_new", "创建数组", "vardecl", "datastruct", "pink", "vtype", "int[]", "name", "a") { Sub = "数组" },
        // 「创建结构」改名叫「创建栈」回到列表：栈的压入/弹出要先有实例才能建档，
        // structId 固定预置 st，和关卡表里 StackStart 写的实例号一一对齐。
        new("stack_new", "创建栈", "structnew", "datastruct", "purple", "structId", "st") { Sub = "栈" },
        // 名字只用中文：二级目录已经点明是栈，再挂 push/pop 英文原名会把放大的字号撑出栏宽
        new("stack_push", "压入", "stack_push", "datastruct", "purple") { Sub = "栈" },
        new("stack_pop", "弹出", "stack_pop", "datastruct", "purple") { Sub = "栈" },
        // 队列跟栈共用一个「创建结构」节点类型：卡面按关卡实例的 Kind 写成创建队列，
        // structId 同样预置 st，跟关卡表 QueueStart 写的实例号对齐。
        new("queue_new", "创建队列", "structnew", "datastruct", "purple", "structId", "st") { Sub = "队列" },
        new("queue_push", "入队", "queue_push", "datastruct", "purple") { Sub = "队列" },
        new("queue_pop", "出队", "queue_pop", "datastruct", "purple") { Sub = "队列" },

        new("sample_input", "样例输入板", "sample_input", "sample", "honey"),
        new("sample_compare", "样例对比板", "sample_compare", "sample", "mint")
    };
}
