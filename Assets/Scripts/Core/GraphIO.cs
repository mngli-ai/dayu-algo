using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dayu.Core
{
    // 关卡图的存档底座：手写 JSON + 深拷贝 + 结构等价比较。
    // 刻意不引用 UnityEngine（连 Debug.Log 都不用），Editor 自测才能把它当纯函数跑；
    // 也不引入 LitJSON/Newtonsoft，字段顺序与数字写法由我们自己钉死，
    // 于是「序列化 → 读回 → 再序列化」必然逐字节一致，存档还能人工 diff。
    public static class GraphIO
    {
        public const int FormatVersion = 1;      // 存档里的 "v"；只增不减，读到更新的版本一律拒收而不是猜
        const double Eps = 1e-4;                 // 撤销栈判「这一笔到底改没改」的坐标容差，鼠标抖动远小于它

        // ---------------- 序列化 ----------------

        // null 也吐一棵合法空图：写侧永远产出能读回来的 JSON，读侧不用为 null 留分支。
        public static string Serialize(Graph g)
        {
            var s = new StringBuilder(256);
            s.Append("{\"v\":").Append(FormatVersion)
             .Append(",\"levelId\":\"").Append(Esc(g == null ? null : g.LevelId)).Append('"')
             .Append(",\"exprMode\":").Append(g != null && g.ExprMode ? "true" : "false")
             .Append(",\"structs\":[");
            if (g != null && g.Structs != null)
                for (int i = 0; i < g.Structs.Count; i++)
                {
                    var d = g.Structs[i]; if (d == null) continue;
                    if (s[s.Length - 1] != '[') s.Append(',');
                    s.Append("{\"id\":\"").Append(Esc(d.Id)).Append("\",\"kind\":\"").Append(Esc(d.Kind))
                     .Append("\",\"elem\":\"").Append(Esc(d.Elem)).Append("\",\"name\":\"").Append(Esc(d.Name)).Append("\"}");
                }
            s.Append("],\"nodes\":[");
            if (g != null && g.Nodes != null)
                for (int i = 0; i < g.Nodes.Count; i++)
                {
                    var n = g.Nodes[i]; if (n == null) continue;
                    if (s[s.Length - 1] != '[') s.Append(',');
                    s.Append("{\"uid\":\"").Append(Esc(n.Uid)).Append("\",\"type\":\"").Append(Esc(n.Type))
                     .Append("\",\"x\":").Append(Num(n.X)).Append(",\"y\":").Append(Num(n.Y)).Append(",\"params\":").Append(Params(n.Params)).Append('}');
                }
            s.Append("],\"links\":[");
            if (g != null && g.Links != null)
                for (int i = 0; i < g.Links.Count; i++)
                {
                    var l = g.Links[i]; if (l == null) continue;
                    if (s[s.Length - 1] != '[') s.Append(',');
                    s.Append("{\"from\":\"").Append(Esc(l.FromNode)).Append("\",\"fromPort\":\"").Append(Esc(l.FromPort))
                     .Append("\",\"to\":\"").Append(Esc(l.ToNode)).Append("\",\"toPort\":\"").Append(Esc(l.ToPort))
                     .Append("\",\"kind\":\"").Append(l.Kind == LinkKind.Data ? "data" : "exec").Append("\"}");
                }
            s.Append("]}");
            return s.ToString();
        }

        // 参数键按字典序输出：玩家侧 Params 是 Dictionary，插入顺序会随编辑历史变；
        // 不排序的话「同一张图两次落盘」文本不同，撤销栈与存档 diff 都会多出假改动。
        static string Params(Dictionary<string, string> p)
        {
            if (p == null || p.Count == 0) return "{}";
            var keys = new List<string>(p.Keys); keys.Sort(StringComparer.Ordinal);
            var s = new StringBuilder("{");
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) s.Append(',');
                string v; s.Append('"').Append(Esc(keys[i])).Append("\":\"").Append(Esc(p.TryGetValue(keys[i], out v) ? v : null)).Append('"');
            }
            return s.Append('}').ToString();
        }

        // 参数字典的值是玩家手打的表达式，引号/反斜杠/换行都会出现，转义漏一个整份存档就废了。
        static string Esc(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            var s = new StringBuilder(v.Length + 8);
            foreach (char c in v)
            {
                if (c == '"') s.Append("\\\"");
                else if (c == '\\') s.Append("\\\\");
                else if (c == '\n') s.Append("\\n");
                else if (c == '\r') s.Append("\\r");
                else if (c == '\t') s.Append("\\t");
                else if (c == '\b') s.Append("\\b");
                else if (c == '\f') s.Append("\\f");
                else if (c < ' ') { s.Append("\\u"); s.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                else s.Append(c);
            }
            return s.ToString();
        }

        // 中文系统下 float.ToString() 会吐 "20,5"，写进 JSON 直接是坏档 → 一律 InvariantCulture。
        // 默认写法就是最短可往返写法（20 / -12.5 / 0.1），万一某个运行时给出不往返的写法再退到 G7~G9 兜底，
        // 于是「读回来再写出去」必然逐字节一致，存档里的坐标也还看得懂。
        static string Num(float v)
        {
            // JSON 没有 NaN/Infinity 字面量，非有限坐标按 0 落盘（画布上也不该出现这种值）
            if (float.IsNaN(v) || float.IsInfinity(v)) return "0";
            float back;
            string t = v.ToString(CultureInfo.InvariantCulture);
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out back) && back == v) return t;
            for (int p = 7; p <= 9; p++)
            {
                t = v.ToString("G" + p, CultureInfo.InvariantCulture);
                if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out back) && back == v) return t;
            }
            return t;
        }

        // ---------------- 反序列化 ----------------

        // 约定：任何输入都不抛异常。解析失败/版本不认识 → error 有值、返回 null；
        // 未知字段跳过（向前兼容）；缺字段用默认值（params 缺 → 空字典，exprMode 缺 → false，
        // structs/links 缺 → 空表，nodes 缺 → 视为坏档，一张没有节点的存档已经不像存档）。
        public static Graph Deserialize(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) { error = "存档内容为空"; return null; }
            Graph g = null;
            try { g = Read(text, out error); }
            // 兜底 catch：读存档是玩家进度唯一的入口，任何意外都必须表现为「坏档」而不是崩游戏
            catch (Exception ex) { g = null; error = "存档解析异常，已按坏档处理：" + ex.Message; }
            if (g == null && error == null) error = "无法识别的存档";
            return g;
        }

        public static Graph Deserialize(string text) { string e; return Deserialize(text, out e); }

        static Graph Read(string text, out string error)
        {
            error = null;
            var j = new J();
            if (!j.Parse(text)) { error = "存档不是合法 JSON：" + j.Error; return null; }
            var root = j.Root as Dictionary<string, object>;
            if (root == null) { error = "存档顶层不是对象"; return null; }
            double v = Dbl(Pick(root, "v"), 1);
            if (v > FormatVersion) { error = "存档版本 v" + (int)v + " 比本程序认得的 v" + FormatVersion + " 新，读不了"; return null; }

            var g = new Graph { LevelId = Str(Pick(root, "levelId"), ""), ExprMode = Bl(Pick(root, "exprMode"), false) };

            var structs = Pick(root, "structs");
            if (structs != null)
            {
                var arr = structs as List<object>;
                if (arr == null) { error = "structs 不是数组"; return null; }
                for (int i = 0; i < arr.Count; i++)
                {
                    var o = arr[i] as Dictionary<string, object>;
                    if (o == null) { error = "structs[" + i + "] 不是对象"; return null; }
                    g.Structs.Add(new StructDef { Id = Str(Pick(o, "id"), ""), Kind = Str(Pick(o, "kind"), ""), Elem = Str(Pick(o, "elem"), ""), Name = Str(Pick(o, "name"), "") });
                }
            }

            var nodes = Pick(root, "nodes");
            if (nodes == null) { error = "存档缺少 nodes"; return null; }
            var na = nodes as List<object>;
            if (na == null) { error = "nodes 不是数组"; return null; }
            for (int i = 0; i < na.Count; i++)
            {
                var o = na[i] as Dictionary<string, object>;
                if (o == null) { error = "nodes[" + i + "] 不是对象"; return null; }
                var uid = Str(Pick(o, "uid"), "");
                if (uid.Length == 0) { error = "nodes[" + i + "] 缺 uid"; return null; }
                var n = new GraphNode { Uid = uid, Type = Str(Pick(o, "type"), ""), X = Flt(Pick(o, "x"), 0f), Y = Flt(Pick(o, "y"), 0f) };
                var p = Pick(o, "params");
                if (p != null)
                {
                    var pd = p as Dictionary<string, object>;
                    if (pd == null) { error = "nodes[" + i + "].params 不是对象"; return null; }
                    foreach (var kv in pd)
                    {
                        // 玩家参数只可能是标量：数字/布尔按字面转字符串留下，嵌套对象数组这种看不懂的就整键丢掉
                        string val = Scalar(kv.Value);
                        if (val != null) n.Params[kv.Key] = val;
                    }
                }
                g.Nodes.Add(n);
            }

            var links = Pick(root, "links");
            if (links != null)
            {
                var la = links as List<object>;
                if (la == null) { error = "links 不是数组"; return null; }
                for (int i = 0; i < la.Count; i++)
                {
                    var o = la[i] as Dictionary<string, object>;
                    if (o == null) { error = "links[" + i + "] 不是对象"; return null; }
                    g.Links.Add(new GraphLink
                    {
                        FromNode = Str(Pick(o, "from"), ""), FromPort = Str(Pick(o, "fromPort"), ""),
                        ToNode = Str(Pick(o, "to"), ""), ToPort = Str(Pick(o, "toPort"), ""),
                        // kind 缺省或认不出时按执行线（= 枚举默认值，和「缺字段用默认值」一条约定）：
                        // 执行线接错会当场编译报错看得见，悄悄把执行线当数据线才是真的查不出来
                        Kind = Str(Pick(o, "kind"), "").Trim().ToLowerInvariant() == "data" ? LinkKind.Data : LinkKind.Exec
                    });
                }
            }
            return g;
        }

        static object Pick(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
        static string Str(object o, string def) { return o is string ? (string)o : def; }
        static bool Bl(object o, bool def) { if (o is bool) return (bool)o; if (o is J.Num) return ((J.Num)o).V != 0; if (o is string) { var t = ((string)o).Trim().ToLowerInvariant(); return t == "true" || t == "1"; } return def; }
        static double Dbl(object o, double def) { if (o is J.Num) return ((J.Num)o).V; if (o is string) { double p; return double.TryParse(((string)o).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out p) ? p : def; } return def; }
        static float Flt(object o, float def) { if (o is J.Num) return (float)((J.Num)o).V; string t = o as string; float f; if (t != null && float.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return f; return def; }
        static string Scalar(object o) { if (o == null) return ""; if (o is string) return (string)o; if (o is bool) return (bool)o ? "true" : "false"; var n = o as J.Num; if (n != null) return n.Raw; return null; }

        // ---------------- 深拷贝 ----------------

        // GraphView.LoadGraph 是 graph = g 的引用赋值（Graph.cs 里不拷贝），
        // 所以任何「拿关卡表里的图当起点」的调用都必须先 Clone，否则玩家编辑直接改写 Levels.All。
        public static Graph Clone(Graph src)
        {
            if (src == null) return null;
            var g = new Graph { LevelId = src.LevelId, ExprMode = src.ExprMode };
            if (src.Structs != null)
                foreach (var d in src.Structs)
                    if (d != null) g.Structs.Add(new StructDef { Id = d.Id, Kind = d.Kind, Elem = d.Elem, Name = d.Name });
            if (src.Nodes != null)
                foreach (var n in src.Nodes)
                {
                    if (n == null) continue;
                    var c = new GraphNode { Uid = n.Uid, Type = n.Type, X = n.X, Y = n.Y };
                    if (n.Params != null) foreach (var kv in n.Params) c.Params[kv.Key] = kv.Value;
                    g.Nodes.Add(c);
                }
            if (src.Links != null)
                foreach (var l in src.Links)
                    if (l != null) g.Links.Add(new GraphLink { FromNode = l.FromNode, FromPort = l.FromPort, ToNode = l.ToNode, ToPort = l.ToPort, Kind = l.Kind });
            return g;
        }

        // ---------------- 结构等价 ----------------

        public static bool Same(Graph a, Graph b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (Ord(a.LevelId) != Ord(b.LevelId) || a.ExprMode != b.ExprMode) return false;
            if (!SameStructs(a.Structs, b.Structs)) return false;
            if (!SameNodes(a.Nodes, b.Nodes)) return false;
            return SameLinks(a.Links, b.Links);
        }

        static bool SameStructs(List<StructDef> x, List<StructDef> y)
        {
            var a = Key(x == null ? null : x.ConvertAll(d => d == null ? "" : Ord(d.Id) + "\u0001" + Ord(d.Kind) + "\u0001" + Ord(d.Elem) + "\u0001" + Ord(d.Name)), "结构");
            var b = Key(y == null ? null : y.ConvertAll(d => d == null ? "" : Ord(d.Id) + "\u0001" + Ord(d.Kind) + "\u0001" + Ord(d.Elem) + "\u0001" + Ord(d.Name)), "结构");
            return a == b;
        }

        // 先按 Uid 排序再逐对精确比：既做到「列表顺序无关」，又不会像把坐标量化成网格字符串
        // 那样在 1e-4 边界上把真的只差 1e-4 的两个值判成不同。
        static bool SameNodes(List<GraphNode> x, List<GraphNode> y)
        {
            var a = SortedNodes(x); var b = SortedNodes(y);
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                var m = a[i]; var n = b[i];
                if (Ord(m.Uid) != Ord(n.Uid) || Ord(m.Type) != Ord(n.Type)) return false;
                if (!NumEq(m.X, n.X) || !NumEq(m.Y, n.Y)) return false;
                if (!SameParams(m.Params, n.Params)) return false;
            }
            return true;
        }

        static List<GraphNode> SortedNodes(List<GraphNode> src)
        {
            var list = new List<GraphNode>();
            if (src != null) foreach (var n in src) if (n != null) list.Add(n);
            // 排序键只用 Uid：同一 Uid 的两张卡（画布上不该出现）落到相邻位置，字段比较照样把它们区分开
            list.Sort((p, q) => { int c = string.CompareOrdinal(Ord(p.Uid), Ord(q.Uid)); return c != 0 ? c : string.CompareOrdinal(Ord(p.Type), Ord(q.Type)); });
            return list;
        }

        static bool SameParams(Dictionary<string, string> x, Dictionary<string, string> y)
        {
            int cx = x == null ? 0 : x.Count, cy = y == null ? 0 : y.Count;
            if (cx != cy) return false;
            if (cx == 0) return true;
            foreach (var kv in x)
            {
                string v;
                if (!y.TryGetValue(kv.Key, out v)) return false;
                if (Ord(v) != Ord(kv.Value)) return false;
            }
            return true;
        }

        static bool SameLinks(List<GraphLink> x, List<GraphLink> y)
        {
            var a = Key(x == null ? null : x.ConvertAll(l => l == null ? "" : Ord(l.FromNode) + "\u0001" + Ord(l.FromPort) + "\u0001" + Ord(l.ToNode) + "\u0001" + Ord(l.ToPort) + "\u0001" + (l.Kind == LinkKind.Data ? "data" : "exec")), "连线");
            var b = Key(y == null ? null : y.ConvertAll(l => l == null ? "" : Ord(l.FromNode) + "\u0001" + Ord(l.FromPort) + "\u0001" + Ord(l.ToNode) + "\u0001" + Ord(l.ToPort) + "\u0001" + (l.Kind == LinkKind.Data ? "data" : "exec")), "连线");
            return a == b;
        }

        static string Ord(string v) { return v ?? ""; }
        // 两边都 NaN 算「没变」：撤销栈宁可少一条历史，也不要为同一个坏坐标来回压栈
        static bool NumEq(float x, float y)
        {
            if (x == y) return true;
            if (float.IsNaN(x) && float.IsNaN(y)) return true;
            if (float.IsNaN(x) || float.IsNaN(y)) return false;
            return Math.Abs((double)x - y) <= Eps;
        }

        static string Key(List<string> items, string tag)
        {
            if (items == null || items.Count == 0) return tag + "|0";
            items.Sort(StringComparer.Ordinal);
            var s = new StringBuilder(tag).Append('|').Append(items.Count);
            foreach (var i in items) s.Append('\u0001').Append(i);
            return s.ToString();
        }

        // ---------------- 极简 JSON 解析器（只服务存档格式，不求快） ----------------

        sealed class J
        {
            public sealed class Num { public string Raw; public double V; public Num(string raw, double v) { Raw = raw; V = v; } }

            string s; int i, depth;
            public object Root; public string Error;

            public bool Parse(string text)
            {
                s = text; i = 0; depth = 0; Root = null; Error = null;
                object v;
                Ws();
                if (!Val(out v)) return false;
                Ws();
                if (i != s.Length) return Fail("文档结尾有多余内容");
                Root = v;
                return true;
            }

            void Ws() { while (i < s.Length) { char c = s[i]; if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++; else break; } }
            bool Peek(char c) { if (i < s.Length && s[i] == c) { i++; return true; } return false; }
            bool Fail(string m) { Error = m + "（第 " + i + " 个字符处）"; return false; }

            bool Val(out object v)
            {
                v = null;
                if (depth > 128) { Error = "嵌套层数太深"; return false; }
                if (i >= s.Length) return Fail("内容提前结束");
                char c = s[i]; depth++;
                bool ok;
                if (c == '{') ok = Obj(out v);
                else if (c == '[') ok = Arr(out v);
                else if (c == '"') { string t; ok = Str(out t); if (ok) v = t; }
                else if (c == 't' || c == 'f') ok = Bool(out v);
                else if (c == 'n') ok = Null(out v);
                else ok = Number(out v);
                depth--;
                return ok;
            }

            bool Obj(out object v)
            {
                var d = new Dictionary<string, object>(); v = d; i++;
                Ws();
                if (Peek('}')) return true;
                while (true)
                {
                    Ws();
                    if (i >= s.Length || s[i] != '"') return Fail("对象的键必须是带引号的字符串");
                    string k; if (!Str(out k)) return false;
                    Ws();
                    if (!Peek(':')) return Fail("键后面缺冒号");
                    object val; if (!Val(out val)) return false;
                    d[k] = val;
                    Ws();
                    if (Peek(',')) continue;
                    if (Peek('}')) return true;
                    return Fail("成员之间缺逗号或结尾缺花括号");
                }
            }

            bool Arr(out object v)
            {
                var a = new List<object>(); v = a; i++;
                Ws();
                if (Peek(']')) return true;
                while (true)
                {
                    object e; if (!Val(out e)) return false;
                    a.Add(e);
                    Ws();
                    if (Peek(',')) continue;
                    if (Peek(']')) return true;
                    return Fail("元素之间缺逗号或结尾缺方括号");
                }
            }

            bool Str(out string v)
            {
                v = null; i++; var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) return Fail("字符串没有闭合");
                    char c = s[i++];
                    if (c == '"') { v = sb.ToString(); return true; }
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) return Fail("转义符后面没字符了");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) return Fail("\\u 后面缺四位十六进制");
                            string hex = s.Substring(i, 4); i += 4;
                            int code;
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) return Fail("\\u 后面不是合法十六进制：" + hex);
                            sb.Append((char)code);
                            break;
                        default: return Fail("不认识的转义符 \\" + e);
                    }
                }
            }

            bool Number(out object v)
            {
                v = null; int start = i;
                while (i < s.Length) { char c = s[i]; if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++; else break; }
                string raw = s.Substring(start, i - start);
                if (raw.Length == 0) return Fail("这里应该是一个值");
                double d;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return Fail("数字写法不合法：" + raw);
                v = new Num(raw, d);
                return true;
            }

            bool Bool(out object v)
            {
                v = null;
                if (i + 4 <= s.Length && s.Substring(i, 4) == "true") { i += 4; v = true; return true; }
                if (i + 5 <= s.Length && s.Substring(i, 5) == "false") { i += 5; v = false; return true; }
                return Fail("不是合法的字面量");
            }

            bool Null(out object v)
            {
                v = null;
                if (i + 4 <= s.Length && s.Substring(i, 4) == "null") { i += 4; return true; }
                return Fail("不是合法的字面量");
            }
        }
    }
}
