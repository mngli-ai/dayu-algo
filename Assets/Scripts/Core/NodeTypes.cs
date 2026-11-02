using System;
using System.Collections.Generic;

namespace Dayu.Core
{
    public sealed class PortDef
    {
        public string Id;
        public string Type;
        public string Label;
        public bool Optional;
        public PortDef(string id, string type = null, string label = "", bool optional = false) { Id = id; Type = type; Label = label; Optional = optional; }
    }

    public sealed class ParamDef
    {
        public string Id, Kind, Label, Default;
        public object Options;
        public ParamDef(string id, string kind, string label, object options = null, string @default = null) { Id = id; Kind = kind; Label = label; Options = options; Default = @default; }
    }

    public sealed class ExecDef
    {
        public string Id, Label;
        public ExecDef(string id, string label = "") { Id = id; Label = label; }
    }

    public sealed class NodeType
    {
        public string Key, Title, Cat, ColorKey, ExecIn;
        public List<ExecDef> ExecOut = new List<ExecDef>();
        public List<PortDef> In = new List<PortDef>();
        public List<PortDef> Out = new List<PortDef>();
        public List<ParamDef> Params = new List<ParamDef>();
        public int W = 168, TitleH = 22;
        public int PinRows { get { return Math.Max(In.Count, Out.Count); } }
        public int Height { get { return Math.Max(34, TitleH + 8 + PinRows * 18); } }
        public NodeType(string key, string title, string cat, string color, string execIn) { Key = key; Title = title; Cat = cat; ColorKey = color; ExecIn = execIn; }
    }

    public static class NodeTypes
    {
        public static readonly Dictionary<string, NodeType> All = Build();
        public static NodeType Get(string type) { NodeType value; return type != null && All.TryGetValue(type, out value) ? value : null; }

        static readonly Dictionary<string, string> TypeCn = new Dictionary<string, string>
        {
            { "int", "整数" }, { "ll", "长整数" }, { "long", "长整数" },
            { "double", "小数" }, { "bool", "布尔" }, { "char", "字符" }, { "string", "字符串" },
        };

        /// <summary>给玩家看的类型名：中文在外、代码里的英文类型名进括号（int[] → 整数数组 (int[])）。</summary>
        public static string TypeNameCn(string vtype)
        {
            string t = string.IsNullOrEmpty(vtype) ? "int" : vtype;
            bool array = t.EndsWith("[]", StringComparison.Ordinal);
            string elem = array ? t.Substring(0, t.Length - 2) : t;
            string cn;
            if (!TypeCn.TryGetValue(elem, out cn)) cn = elem;
            return (array ? cn + "数组" : cn) + " (" + t + ")";
        }

        /// <summary>数据输入端口对应的表达式字段名；返回 null 表示该端口只认数据线。</summary>
        public static string ExprField(string nodeType, string port)
        {
            switch (nodeType)
            {
                case "vardecl": return port == "value" ? "initExpr" : null;
                case "varset": return port == "value" ? "valueExpr" : null;
                // 压入 / 入队：第十三轮玩家要的「浮窗里直接写一个表达式」，写了就不再接数据线
                case "stack_push":
                case "queue_push": return port == "value" ? "pushExpr" : null;
                case "output": return port == "value" ? "outputExpr" : null;
                case "branch": return port == "cond" ? "condExpr" : null;
                case "forloop": return port == "from" ? "fromExpr" : port == "to" ? "toExpr" : null;
                case "whileloop": return port == "cond" ? "condExpr" : null;
                // 旧表达式工作台写出的整体公式：expr 同时满足 a / b 两个端口
                case "binop": return (port == "a" || port == "b") ? "expr" : null;
            }
            return null;
        }

        /// <summary>节点是否用表达式代替了某个数据输入端口。</summary>
        public static bool HasExpr(GraphNode n, string port)
        {
            var field = ExprField(n == null ? null : n.Type, port);
            return field != null && !string.IsNullOrEmpty(GraphUtil.Get(n, field));
        }
        private static List<ExecDef> E(params string[] pairs) { var x = new List<ExecDef>(); for (int i = 0; i + 1 < pairs.Length; i += 2) x.Add(new ExecDef(pairs[i], pairs[i + 1])); return x; }
        private static List<PortDef> P(params string[] pairs) { var x = new List<PortDef>(); for (int i = 0; i + 1 < pairs.Length; i += 2) x.Add(new PortDef(pairs[i], pairs[i + 1])); return x; }
        private static List<ParamDef> T(params string[] a) { var x = new List<ParamDef>(); for (int i = 0; i + 2 < a.Length; i += 3) x.Add(new ParamDef(a[i], "text", a[i + 1], null, a[i + 2])); return x; }
        private static List<ParamDef> Enum(string id, string label, string def, params string[] options) { return new List<ParamDef> { new ParamDef(id, "enum", label, options, def) }; }
        private static Dictionary<string, NodeType> Build()
        {
            var d = new Dictionary<string, NodeType>();
            Add(d, new NodeType("entry", "开始", "flow", "whale", null), E("out", ""));
            // 表达式字段（*Expr）为空时，节点仍按旧的数据线方式工作，保证 L2/L3 老图不受影响。
            Add(d, new NodeType("branch", "如果", "flow", "whale", "in"), E("true", "真", "false", "假"), P("cond", "bool"), null, T("condExpr", "条件表达式", ""));
            // dir 只管一件事：从「计数起点」走到「计数终点」时是越数越大（asc）还是越数越小（desc）。
            // 起点 / 终点谁大谁小不作要求，倒序时由 dir 决定比较方向；这一格在表达式浮窗头部改，不进参数窗。
            Add(d, new NodeType("forloop", "计数循环", "flow", "whale", "in"), E("body", "循环体", "done", "循环结束"), P("from", "int", "to", "int"), P("index", "int"), T("indexName", "循环变量", "i"), T("fromExpr", "起始表达式", ""), T("toExpr", "结束表达式", ""), new ParamDef("dir", "enum", "顺序", new[] { "asc", "desc" }, "asc"));
            Add(d, new NodeType("whileloop", "条件循环", "flow", "whale", "in"), E("body", "循环体", "done", "循环结束"), P("cond", "bool"), null, T("condExpr", "条件表达式", ""));
            // vtype 除了标量还能写 int[] / double[] / string[] / char[]：数组变量用 vtype 前缀区分元素类型。
            // size 只对数组有意义（先铺 N 个默认值），标量变量这一栏在参数窗里不显示。
            Add(d, new NodeType("vardecl", "创建变量", "var", "pink", "in"), E("out", ""), null, P("value", "any"), T("name", "名字", "v"), new ParamDef("vtype", "enum", "类型", new[] { "int", "ll", "double", "bool", "char", "string", "int[]", "double[]", "string[]", "char[]" }, "int"), T("size", "长度", ""), T("initExpr", "初始值表达式", ""));
            // 「赋值」只填一条语句：s = x / s += x / a[i] = x，类型跟着「创建变量」的声明走，这里不再重复选。
            Add(d, new NodeType("varset", "赋值", "var", "pink", "in"), E("out", ""), P("value", "any"), null, T("name", "名字", "v"), T("valueExpr", "赋值语句", ""));
            // 数组元素写入不单列模块：在「赋值」里直接写 a[i] = x，下标来自循环变量。
            AddOutput(d, new NodeType("varget", "读取变量", "var", "pink", null), null, P("value", "any"), T("name", "名字", "v"));
            AddOutput(d, new NodeType("const", "常量", "literal", "yellow", null), null, P("value", "any"), new ParamDef("vtype", "enum", "类型", new[] { "int", "ll", "double", "bool", "char", "string" }, "int"), T("value", "值", "0"));
            AddIO(d, new NodeType("binop", "运算", "math", "yellow", null),
                new List<PortDef> { new PortDef("a", "any", "a", true), new PortDef("b", "any", "b", true) },
                P("r", "any"),
                new ParamDef("op", "enum", "运算", new[] { "+", "-", "*", "/", "%" }, "+"),
                T("varA", "变量 A", ""), T("varB", "变量 B", ""),
                T("formula", "公式", ""), T("expr", "表达式", ""));
            AddIO(d, new NodeType("compare", "比较", "math", "yellow", null), P("a", "any", "b", "any"), P("r", "bool"), new ParamDef("op", "enum", "比较", new[] { "<", "<=", ">", ">=", "==", "!=" }, "=="));
            AddIO(d, new NodeType("logic", "逻辑", "math", "yellow", null), new List<PortDef> { new PortDef("a", "bool"), new PortDef("b", "bool", "", true) }, P("r", "bool"), new ParamDef("op", "enum", "运算", new[] { "and", "or", "not" }, "and"));
            Add(d, new NodeType("input", "输入", "io", "cyan", "in"), E("out", ""), new List<PortDef> { new PortDef("sample", "string", "样例", true) }, P("value", "any"), new ParamDef("vtype", "enum", "类型", new[] { "int", "ll", "double", "bool", "char", "string" }, "int"), T("name", "写入变量", ""));
            Add(d, new NodeType("output", "输出", "io", "cyan", "in"), E("out", ""), P("value", "any"), P("value", "any"), T("outputExpr", "输出表达式", ""));
            // 样例板块：sample_input 作为当前测试样例的可视化来源，sample_compare 接收程序输出并在运行结束时对比预期。
            AddOutput(d, new NodeType("sample_input", "样例输入板", "sample", "honey", null), null, P("value", "string"), T("group", "样例组", "0"));
            Add(d, new NodeType("sample_compare", "样例对比板", "sample", "mint", null), null, new List<PortDef> { new PortDef("value", "any", "输出", true) }, null);
            Add(d, new NodeType("structnew", "创建结构", "struct", "purple", "in"), E("out", ""), null, null, new ParamDef("structId", "enum", "实例", "<来自关卡 structs>", "st"));
            Add(d, new NodeType("stack_push", "栈·压入", "struct", "purple", "in"), E("out", ""), P("value", "any"), null, new ParamDef("structId", "enum", "实例", "<structs>", "st"), T("pushExpr", "压入表达式", ""));
            Add(d, new NodeType("stack_pop", "栈·弹出", "struct", "purple", "in"), E("out", ""), null, P("value", "any"), new ParamDef("structId", "enum", "实例", "<structs>", "st"));
            Add(d, new NodeType("stack_top", "栈·栈顶", "structread", "lavender", null), null, null, P("value", "any"), new ParamDef("structId", "enum", "实例", "<structs>", "st"));
            // 栈和队列里「空了」「个数」是同一件事，卡名就必须一模一样；只有取元素那颗按结构分开。
            Add(d, new NodeType("stack_empty", "栈·空了", "structread", "lavender", null), null, null, P("value", "bool"), new ParamDef("structId", "enum", "实例", "<structs>", "st"));
            Add(d, new NodeType("stack_size", "栈·个数", "structread", "lavender", null), null, null, P("value", "int"), new ParamDef("structId", "enum", "实例", "<structs>", "st"));
            Add(d, new NodeType("queue_push", "队列·入队", "struct", "purple", "in"), E("out", ""), P("value", "any"), null, new ParamDef("structId", "enum", "实例", "<structs>", "st"), T("pushExpr", "入队表达式", ""));
            Add(d, new NodeType("queue_pop", "队列·出队", "struct", "purple", "in"), E("out", ""), null, P("value", "any"), new ParamDef("structId", "enum", "实例", "<structs>", "st"));
            Add(d, new NodeType("queue_front", "队列·队头", "structread", "lavender", null), null, null, P("value", "any"), new ParamDef("structId", "enum", "实例", "<structs>", "st"));
            Add(d, new NodeType("queue_empty", "队列·空了", "structread", "lavender", null), null, null, P("value", "bool"), new ParamDef("structId", "enum", "实例", "<structs>", "st"));
            Add(d, new NodeType("queue_size", "队列·个数", "structread", "lavender", null), null, null, P("value", "int"), new ParamDef("structId", "enum", "实例", "<structs>", "st"));
            AddIO(d, new NodeType("text_index", "取字符", "string", "yellow", null), P("s", "string", "i", "int"), P("value", "char"));
            AddIO(d, new NodeType("len_of", "长度", "string", "yellow", null), P("v", "any"), P("value", "int"));
            return d;
        }
        private static void AddOutput(Dictionary<string, NodeType> d, NodeType n, List<ExecDef> e, List<PortDef> output, params object[] rest) { n.ExecOut = e ?? n.ExecOut; n.Out = output ?? n.Out; foreach (var o in rest) { var p = o as ParamDef; if (p != null) n.Params.Add(p); else { var pms = o as List<ParamDef>; if (pms != null) n.Params.AddRange(pms); } } d[n.Key] = n; }        private static void AddIO(Dictionary<string, NodeType> d, NodeType n, List<PortDef> input, List<PortDef> output, params object[] rest) { n.In = input ?? n.In; n.Out = output ?? n.Out; foreach (var o in rest) { var p = o as ParamDef; if (p != null) n.Params.Add(p); else { var pms = o as List<ParamDef>; if (pms != null) n.Params.AddRange(pms); } } d[n.Key] = n; }
        private static void Add(Dictionary<string, NodeType> d, NodeType n, List<ExecDef> e = null, List<PortDef> input = null, List<PortDef> output = null, params object[] rest)
        {
            n.ExecOut = e ?? n.ExecOut; if (input != null) n.In = input; if (output != null) n.Out = output;
            foreach (var o in rest) { var p = o as ParamDef; if (p != null) n.Params.Add(p); else { var ps = o as List<PortDef>; if (ps != null) { if (n.In.Count == 0) n.In = ps; else n.Out = ps; continue; } var pms = o as List<ParamDef>; if (pms != null) n.Params.AddRange(pms); } }
            d[n.Key] = n;
        }
    }
}
