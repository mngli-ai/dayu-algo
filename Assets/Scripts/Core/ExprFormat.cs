using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dayu.Core
{
    // 运算块内部表达式：变量 / 常量 / 运算符 / 数组索引 / 长度 / 数组字面量
    public sealed class ExprNode
    {
        public string Kind;      // var const op index len array
        public string Name;      // 变量名、运算符、数组名
        public string Value;     // 常量
        public List<ExprNode> Children = new List<ExprNode>();

        public static ExprNode Var(string name) { return new ExprNode { Kind = "var", Name = name }; }
        public static ExprNode Const(string value) { return new ExprNode { Kind = "const", Value = value }; }
        public static ExprNode Op(string op, ExprNode a, ExprNode b) { return new ExprNode { Kind = "op", Name = op, Children = new List<ExprNode> { a, b } }; }
        public static ExprNode Index(string array, ExprNode index) { return new ExprNode { Kind = "index", Name = array, Children = new List<ExprNode> { index } }; }
        public static ExprNode Len(ExprNode arg) { return new ExprNode { Kind = "len", Children = new List<ExprNode> { arg } }; }
        public static ExprNode Array(params ExprNode[] items) { return new ExprNode { Kind = "array", Children = new List<ExprNode>(items) }; }
    }

    public sealed class ExprEquation
    {
        public string Name;
        public ExprNode Expr;
    }

    public sealed class ExprWorkspace
    {
        public List<ExprEquation> Equations = new List<ExprEquation>();
        public ExprNode Output;
    }

    public static class ExprFormat
    {
        public static bool IsArrayType(string t) { return !string.IsNullOrEmpty(t) && t.EndsWith("[]", StringComparison.Ordinal); }
        public static string ElemType(string t) { return IsArrayType(t) ? t.Substring(0, t.Length - 2) : t; }

        // ---------------- 序列化 ----------------

        public static string Serialize(ExprWorkspace w)
        {
            var parts = new List<string>();
            int n = w == null || w.Equations == null ? 0 : w.Equations.Count;
            parts.Add("E" + n);
            if (w != null && w.Equations != null)
                foreach (var eq in w.Equations) { parts.Add(eq.Name ?? "t"); parts.Add(Ser(eq.Expr)); }
            parts.Add("O");
            parts.Add(Ser(w != null ? w.Output : null));
            return string.Join(" ", parts);
        }

        static string Ser(ExprNode n)
        {
            if (n == null) return "n";
            switch (n.Kind)
            {
                case "var": return "v" + (n.Name ?? "");
                case "const": return "c" + Convert.ToBase64String(Encoding.UTF8.GetBytes(n.Value ?? ""));
                case "op": return "o" + (n.Name ?? "+") + " " + Ser(n.Children.Count > 0 ? n.Children[0] : null) + " " + Ser(n.Children.Count > 1 ? n.Children[1] : null);
                case "index": return "i" + (n.Name ?? "") + " " + Ser(n.Children.Count > 0 ? n.Children[0] : null);
                case "len": return "l " + Ser(n.Children.Count > 0 ? n.Children[0] : null);
                case "size": return "s " + Ser(n.Children.Count > 0 ? n.Children[0] : null);
                case "front": return "f " + Ser(n.Children.Count > 0 ? n.Children[0] : null);
                case "back": return "b " + Ser(n.Children.Count > 0 ? n.Children[0] : null);
                case "array":
                {
                    var sb = new StringBuilder("a" + n.Children.Count);
                    foreach (var c in n.Children) sb.Append(" ").Append(Ser(c));
                    return sb.ToString();
                }
            }
            return "n";
        }

        public static ExprWorkspace Parse(string s)
        {
            var w = new ExprWorkspace();
            if (string.IsNullOrWhiteSpace(s)) return w;
            var tk = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            try
            {
                if (i >= tk.Length || tk[i][0] != 'E') return w;
                int n = int.Parse(tk[i].Substring(1), CultureInfo.InvariantCulture); i++;
                for (int k = 0; k < n && i < tk.Length; k++)
                {
                    string name = tk[i++];
                    var expr = ParseNode(tk, ref i);
                    w.Equations.Add(new ExprEquation { Name = name, Expr = expr });
                }
                if (i < tk.Length && tk[i] == "O") { i++; w.Output = ParseNode(tk, ref i); }
            }
            catch { }
            return w;
        }

        static ExprNode ParseNode(string[] tk, ref int i)
        {
            if (i >= tk.Length) return null;
            string t = tk[i++];
            if (t == "n") return null;
            char k = t[0];
            if (k == 'v') return ExprNode.Var(t.Substring(1));
            if (k == 'c')
            {
                var v = t.Length > 1 ? Encoding.UTF8.GetString(Convert.FromBase64String(t.Substring(1))) : "";
                return ExprNode.Const(v);
            }
            if (k == 'o')
            {
                var a = ParseNode(tk, ref i);
                var b = ParseNode(tk, ref i);
                return ExprNode.Op(t.Length > 1 ? t.Substring(1) : "+", a, b);
            }
            if (k == 'i')
            {
                var idx = ParseNode(tk, ref i);
                return ExprNode.Index(t.Substring(1), idx);
            }
            if (k == 'l') return ExprNode.Len(ParseNode(tk, ref i));
            if (k == 's' || k == 'f' || k == 'b')
            {
                var child = ParseNode(tk, ref i);
                return new ExprNode { Kind = k == 's' ? "size" : k == 'f' ? "front" : "back", Children = new List<ExprNode> { child } };
            }
            if (k == 'a')
            {
                int count = int.Parse(t.Substring(1), CultureInfo.InvariantCulture);
                var arr = new List<ExprNode>();
                for (int j = 0; j < count; j++) arr.Add(ParseNode(tk, ref i));
                return new ExprNode { Kind = "array", Children = arr };
            }
            return null;
        }

        // ---------------- 显示 ----------------

        public static string Formula(ExprNode n)
        {
            if (n == null) return "?";
            switch (n.Kind)
            {
                case "var": return n.Name ?? "?";
                case "const": return ConstText(n.Value);
                case "op": return "(" + Formula(Child(n, 0)) + " " + (n.Name ?? "+") + " " + Formula(Child(n, 1)) + ")";
                case "index": return (n.Name ?? "?") + "[" + Formula(Child(n, 0)) + "]";
                case "len": return "len(" + Formula(Child(n, 0)) + ")";
                case "size": return "size(" + Formula(Child(n, 0)) + ")";
                case "front": return "front(" + Formula(Child(n, 0)) + ")";
                case "back": return "back(" + Formula(Child(n, 0)) + ")";
                case "array":
                {
                    var parts = new List<string>();
                    foreach (var c in n.Children) parts.Add(Formula(c));
                    return "[" + string.Join(", ", parts) + "]";
                }
            }
            return "?";
        }

        public static string FormulaClean(ExprNode n)
        {
            var s = Formula(n);
            if (s.Length > 1 && s[0] == '(' && s[s.Length - 1] == ')' && BalancedOuter(s)) s = s.Substring(1, s.Length - 2);
            return s;
        }

        static bool BalancedOuter(string s)
        {
            int d = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') d++;
                else if (s[i] == ')') { d--; if (d == 0 && i != s.Length - 1) return false; }
            }
            return d == 0;
        }

        static string ConstText(string v)
        {
            v = v ?? "";
            if (v == "true" || v == "false") return v;
            long l; double d;
            if (long.TryParse(v, out l) || double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return v;
            return "\"" + v + "\"";
        }

        static ExprNode Child(ExprNode n, int i) { return n != null && n.Children != null && i < n.Children.Count ? n.Children[i] : null; }

        // ---------------- 类型 ----------------

        public static Dictionary<string, string> BuildTypes(ExprWorkspace w, Graph g)
        {
            var types = new Dictionary<string, string>();
            if (g != null)
                foreach (var n in g.Nodes)
                    if (n != null && n.Type == "vardecl")
                    {
                        var name = GraphUtil.Get(n, "name");
                        if (!string.IsNullOrEmpty(name)) types[name] = GraphUtil.Get(n, "vtype") ?? "int";
                    }
            if (w != null)
                foreach (var eq in w.Equations)
                {
                    var t = TypeOf(eq.Expr, g, types);
                    if (!string.IsNullOrEmpty(eq.Name)) types[eq.Name] = t;
                }
            return types;
        }

        public static string TypeOf(ExprNode n, Graph g, Dictionary<string, string> types)
        {
            if (n == null) return "?";
            switch (n.Kind)
            {
                case "var":
                {
                    string t;
                    return n.Name != null && types.TryGetValue(n.Name, out t) ? t : (g != null ? GraphUtil.VarType(g, n.Name) : null) ?? "?";
                }
                case "const": return ConstType(n.Value);
                case "op":
                {
                    var a = TypeOf(Child(n, 0), g, types);
                    var b = TypeOf(Child(n, 1), g, types);
                    if (ExprLang.IsBitop(n.Name)) return "int";
                    if (a == "double" || b == "double") return "double";
                    if (a == "string" || b == "string") return "string";
                    return "int";
                }
                case "index":
                {
                    string t;
                    var bt = n.Name != null && types.TryGetValue(n.Name, out t) ? t : (g != null ? GraphUtil.VarType(g, n.Name) : null);
                    return IsArrayType(bt) ? ElemType(bt) : "?";
                }
                case "len":
                case "size": return "int";
                case "front":
                case "back":
                {
                    var at = TypeOf(Child(n, 0), g, types);
                    return IsArrayType(at) ? ElemType(at) : "?";
                }
                case "array":
                {
                    var e = "int";
                    foreach (var c in n.Children) { var t = TypeOf(c, g, types); if (t == "double") e = "double"; }
                    return e + "[]";
                }
            }
            return "?";
        }

        static string ConstType(string v)
        {
            v = v ?? "";
            if (v == "true" || v == "false") return "bool";
            long l;
            if (long.TryParse(v, out l)) return "int";
            double d;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return "double";
            return "string";
        }

        // ---------------- 校验 ----------------

        public static List<string> Validate(ExprWorkspace w, Graph g)
        {
            var errors = new List<string>();
            if (w == null) { errors.Add("表达式为空"); return errors; }
            var types = new Dictionary<string, string>();
            if (g != null)
                foreach (var n in g.Nodes)
                    if (n != null && n.Type == "vardecl")
                    {
                        var name = GraphUtil.Get(n, "name");
                        if (!string.IsNullOrEmpty(name)) types[name] = GraphUtil.Get(n, "vtype") ?? "int";
                    }

            var used = new HashSet<string>();
            foreach (var eq in w.Equations)
            {
                if (string.IsNullOrEmpty(eq.Name)) { errors.Add("中间变量缺名字"); continue; }
                if (!IsIdent(eq.Name)) errors.Add("变量名「" + eq.Name + "」不合法");
                if (used.Contains(eq.Name)) errors.Add("中间变量「" + eq.Name + "」重名");
                used.Add(eq.Name);
                if (eq.Expr == null) { errors.Add("中间变量「" + eq.Name + "」还没有表达式"); continue; }
                ValidateNode(eq.Expr, g, types, errors);
                types[eq.Name] = TypeOf(eq.Expr, g, types);
            }

            if (w.Output == null) errors.Add("输出还没有连接表达式");
            else
            {
                ValidateNode(w.Output, g, types, errors);
                var t = TypeOf(w.Output, g, types);
                if (IsArrayType(t)) errors.Add("输出不能是整个数组，请用 a[i] 或 len(a)");
            }
            return errors;
        }

        static void ValidateNode(ExprNode n, Graph g, Dictionary<string, string> types, List<string> errors)
        {
            if (n == null) return;
            switch (n.Kind)
            {
                case "var":
                    if (string.IsNullOrEmpty(n.Name) || !types.ContainsKey(n.Name))
                        errors.Add("变量「" + (n.Name ?? "?") + "」还没有创建");
                    break;
                case "const": break;
                case "op":
                {
                    if (Child(n, 0) == null || Child(n, 1) == null) { errors.Add("运算符「" + n.Name + "」还缺操作数"); break; }
                    ValidateNode(Child(n, 0), g, types, errors);
                    ValidateNode(Child(n, 1), g, types, errors);
                    var a = TypeOf(Child(n, 0), g, types);
                    var b = TypeOf(Child(n, 1), g, types);
                    if (!IsNumber(a) || !IsNumber(b)) errors.Add("运算符「" + n.Name + "」两边需要数字，当前是 " + a + " 和 " + b);
                    else if (ExprLang.IsBitop(n.Name) && (a == "double" || b == "double"))
                        errors.Add("位运算「" + n.Name + "」两边必须是整数 (int)，当前是 " + a + " 和 " + b);
                    break;
                }
                case "index":
                {
                    if (Child(n, 0) == null) { errors.Add("数组「" + n.Name + "」缺下标"); break; }
                    string baseType;
                    var bt = n.Name != null && types.TryGetValue(n.Name, out baseType) ? baseType : null;
                    if (!IsArrayType(bt)) errors.Add("「" + (n.Name ?? "?") + "」不是数组，不能用 [ ]");
                    ValidateNode(Child(n, 0), g, types, errors);
                    var it = TypeOf(Child(n, 0), g, types);
                    if (!IsNumber(it)) errors.Add("数组下标必须是整数");
                    break;
                }
                case "len":
                case "size":
                case "front":
                case "back":
                    if (Child(n, 0) == null) errors.Add((n.Kind) + " 还缺参数");
                    else
                    {
                        ValidateNode(Child(n, 0), g, types, errors);
                        if (n.Kind != "len" && !IsArrayType(TypeOf(Child(n, 0), g, types)))
                            errors.Add(n.Kind + " 需要一个数组（如 t1 或 a[ ]）");
                    }
                    break;
                case "array":
                    foreach (var c in n.Children) ValidateNode(c, g, types, errors);
                    break;
            }
        }

        static bool IsNumber(string t) { return t == "int" || t == "double" || t == "ll"; }
        static bool IsIdent(string x)
        {
            if (string.IsNullOrEmpty(x)) return false;
            if (!char.IsLetter(x[0]) && x[0] != '_') return false;
            for (int i = 1; i < x.Length; i++) if (!char.IsLetterOrDigit(x[i]) && x[i] != '_') return false;
            return true;
        }

        // ---------------- 求值 ----------------

        sealed class EV
        {
            public bool IsArray;
            public Val Scalar;
            public List<EV> Items;
            public static EV S(Val v) { return new EV { Scalar = v }; }
            public static EV A(List<EV> items) { return new EV { IsArray = true, Items = items }; }
        }

        public static Val Eval(ExprWorkspace w, Func<string, Val> lookup)
        {
            if (w == null) return Val.Int(0);
            var env = new Dictionary<string, EV>();
            foreach (var eq in w.Equations)
            {
                var v = EvalNode(eq.Expr, env, lookup);
                if (!string.IsNullOrEmpty(eq.Name)) env[eq.Name] = v;
            }
            var o = EvalNode(w.Output, env, lookup);
            return o != null && !o.IsArray ? o.Scalar : Val.Int(0);
        }

        static EV EvalNode(ExprNode n, Dictionary<string, EV> env, Func<string, Val> lookup)
        {
            if (n == null) return EV.S(Val.Int(0));
            switch (n.Kind)
            {
                case "var":
                {
                    EV v;
                    if (n.Name != null && env.TryGetValue(n.Name, out v)) return v;
                    return EV.S(lookup != null ? lookup(n.Name) : Val.Int(0));
                }
                case "const": return EV.S(ParseConst(n.Value));
                case "op":
                {
                    var a = EvalNode(Child(n, 0), env, lookup);
                    var b = EvalNode(Child(n, 1), env, lookup);
                    if (a.IsArray || b.IsArray) return EV.S(Val.Int(0));
                    return EV.S(Apply(n.Name, a.Scalar, b.Scalar));
                }
                case "index":
                {
                    EV basev;
                    if (n.Name == null || !env.TryGetValue(n.Name, out basev) || !basev.IsArray) return EV.S(Val.Int(0));
                    var idx = EvalNode(Child(n, 0), env, lookup);
                    long i = idx.IsArray ? 0 : idx.Scalar.AsLong();
                    return i >= 0 && i < basev.Items.Count ? basev.Items[(int)i] : EV.S(Val.Int(0));
                }
                case "len":
                {
                    var a = EvalNode(Child(n, 0), env, lookup);
                    if (a.IsArray) return EV.S(Val.Int(a.Items.Count));
                    return EV.S(Val.Int(a.Scalar.AsString().Length));
                }
                case "size":
                {
                    var a = EvalNode(Child(n, 0), env, lookup);
                    return EV.S(Val.Int(a.IsArray ? a.Items.Count : 0));
                }
                case "front":
                {
                    var a = EvalNode(Child(n, 0), env, lookup);
                    return a.IsArray && a.Items.Count > 0 ? a.Items[0] : EV.S(Val.Int(0));
                }
                case "back":
                {
                    var a = EvalNode(Child(n, 0), env, lookup);
                    return a.IsArray && a.Items.Count > 0 ? a.Items[a.Items.Count - 1] : EV.S(Val.Int(0));
                }
                case "array":
                {
                    var list = new List<EV>();
                    foreach (var c in n.Children) list.Add(EvalNode(c, env, lookup));
                    return EV.A(list);
                }
            }
            return EV.S(Val.Int(0));
        }

        static Val ParseConst(string value)
        {
            value = value ?? "";
            if (value == "true") return Val.Bool(true);
            if (value == "false") return Val.Bool(false);
            long l;
            if (long.TryParse(value, out l)) return Val.Int(l);
            double d;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return Val.Double(d);
            return Val.Str(value);
        }

        static Val Apply(string op, Val a, Val b)
        {
            if (a.Type == ValType.Str || b.Type == ValType.Str)
                return op == "+" ? Val.Str(a.AsString() + b.AsString()) : Val.Int(0);
            if (a.Type == ValType.Double || b.Type == ValType.Double)
            {
                double x = a.AsDouble(), y = b.AsDouble();
                switch (op)
                {
                    case "-": return Val.Double(x - y);
                    case "*": return Val.Double(x * y);
                    case "/": return Val.Double(y == 0 ? 0 : x / y);
                    case "%": return Val.Double(y == 0 ? 0 : x % y);
                    default: return Val.Double(x + y);
                }
            }
            long i = a.AsLong(), j = b.AsLong();
            switch (op)
            {
                case "-": return Val.Int(i - j);
                case "*": return Val.Int(i * j);
                case "/": return Val.Int(j == 0 ? 0 : i / j);
                case "%": return Val.Int(j == 0 ? 0 : i % j);
                case "&": return Val.Int(i & j);
                case "|": return Val.Int(i | j);
                case "^": return Val.Int(i ^ j);
                // 移位量按 0~62 收口，和 ExprLang 的报错口径一致，避免预览里绕圈
                case "<<": return Val.Int(j >= 0 && j <= 62 ? i << (int)j : i);
                case ">>": return Val.Int(j >= 0 && j <= 62 ? i >> (int)j : i);
                default: return Val.Int(i + j);
            }
        }

        // ---------------- C++ ----------------

        public static string Cpp(ExprWorkspace w)
        {
            if (w == null || w.Output == null) return "0";
            var sb = new StringBuilder("[&](){ ");
            foreach (var eq in w.Equations)
                if (eq.Expr != null && !string.IsNullOrEmpty(eq.Name))
                    sb.Append("auto ").Append(Safe(eq.Name)).Append(" = ").Append(CppNode(eq.Expr)).Append("; ");
            sb.Append("return ").Append(CppNode(w.Output)).Append("; }()");
            return sb.ToString();
        }

        static string CppNode(ExprNode n)
        {
            if (n == null) return "0";
            switch (n.Kind)
            {
                case "var": return Safe(n.Name);
                case "const": return CppConst(n.Value);
                case "op": return "(" + CppNode(Child(n, 0)) + " " + (n.Name ?? "+") + " " + CppNode(Child(n, 1)) + ")";
                case "index": return "(" + Safe(n.Name) + "[(size_t)(" + CppNode(Child(n, 0)) + ")])";
                case "len": return "(long long)(" + CppNode(Child(n, 0)) + ").size()";
                case "size": return "(long long)(" + CppNode(Child(n, 0)) + ").size()";
                case "front": return "(" + CppNode(Child(n, 0)) + ").front()";
                case "back": return "(" + CppNode(Child(n, 0)) + ").back()";
                case "array":
                {
                    var parts = new List<string>();
                    foreach (var c in n.Children) parts.Add(CppNode(c));
                    return "std::vector<long long>{" + string.Join(", ", parts) + "}";
                }
            }
            return "0";
        }

        static string CppConst(string v)
        {
            v = v ?? "";
            if (v == "true" || v == "false") return v;
            long l; double d;
            if (long.TryParse(v, out l) || double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return v;
            return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        static string Safe(string x)
        {
            if (string.IsNullOrEmpty(x)) return "_";
            var sb = new StringBuilder();
            foreach (char c in x) sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            if (sb.Length == 0 || (!char.IsLetter(sb[0]) && sb[0] != '_')) sb.Insert(0, '_');
            return sb.ToString();
        }
    }
}
