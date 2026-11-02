using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dayu.Core
{
    public static class CppGen
    {
        public static string Generate(Program p, string levelId)
        {
            if (p == null || p.Graph == null) throw new ArgumentNullException("p");
            var g = p.Graph;
            var c = new Context(p);
            var b = new StringBuilder();
            b.AppendLine("#include <bits/stdc++.h>");
            b.AppendLine("using namespace std;");
            b.AppendLine();
            b.AppendLine("int main() {");
            b.AppendLine("  // generated level " + (levelId ?? g.LevelId ?? "") );
            // 布尔打印对齐虚拟机口径：C++ 默认打 1/0，游戏里打 true/false
            b.AppendLine("  cout << boolalpha;");
            foreach (var s in g.Structs) b.AppendLine("  " + (s.Kind == "queue" ? "deque<" : "vector<") + CppType(s.Elem) + "> " + Safe(s.Id) + ";");
            foreach (var kv in p.VarTypes) b.AppendLine("  " + CppType(kv.Value) + " " + Safe(kv.Key) + "{};");
            foreach (var n in g.Nodes)
                if (n.Type == "input" && InputNeedsTemp(n)) b.AppendLine("  " + CppType(GraphUtil.Get(n, "vtype")) + " " + c.InputName(n) + "{};");
            foreach (var n in g.Nodes)
                if (n.Type == "stack_pop" || n.Type == "queue_pop") b.AppendLine("  " + CppType(c.NodeType(n)) + " " + c.Temp(n) + "{};");
            b.AppendLine();
            var entry = GraphUtil.FindNode(g, p.EntryUid);
            if (entry != null) c.EmitChain(entry, "out", b, 1, new HashSet<string>());
            b.AppendLine("  return 0;");
            b.AppendLine("}");
            return b.ToString();
        }

        // 输入模块要不要临时变量：值只走数据线（没填目标），或者目标是 a[i]（先读进临时量再按下标写数组）。
        static bool InputNeedsTemp(GraphNode n)
        {
            var raw = GraphUtil.Get(n, "name");
            if (string.IsNullOrEmpty(raw)) return true;
            string nm, idx;
            return Assign.TryParseTarget(raw, out nm, out idx) && idx != null;
        }

        private sealed class Context
        {
            private readonly Program p;
            private readonly Graph g;
            private readonly Dictionary<string, string> exprTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            public Context(Program program)
            {
                p = program; g = program.Graph;
                foreach (var kv in p.VarTypes) exprTypes[kv.Key] = ExprTypeOf(kv.Value);
                // 容器实例进类型表（stack<int> / queue<int>）：成员访问的出码文本按 Members 查，不认识就只能写死
                foreach (var s in g.Structs) if (s != null && !string.IsNullOrEmpty(s.Id)) exprTypes[s.Id] = Members.TypeOf(s.Kind, s.Elem);
                // 循环变量在 C++ 里由 for 语句声明，这里补进类型表，除法取整之类的判断才认得它
                foreach (var n in g.Nodes) if (n != null && n.Type == "forloop") exprTypes[VarScope.IndexName(n)] = "int";
            }
            public string InputName(GraphNode n) { return "__input_" + Safe(n.Uid); }
            public string Temp(GraphNode n) { return "__value_" + Safe(n.Uid); }
            public string NodeType(GraphNode n) { return GraphUtil.OutType(n, "value", g) ?? "int"; }

            /// <summary>把节点上的表达式字段编译成 C++ 表达式；字段为空返回 null。</summary>
            private string ExprField(GraphNode n, string key)
            {
                return ExprOf(n, GraphUtil.Get(n, key));
            }

            /// <summary>把一段表达式文本编译成 C++；空文本返回 null。赋值语句拆出来的右值走这里。</summary>
            private string ExprOf(GraphNode n, string src)
            {
                if (string.IsNullOrEmpty(src)) return null;
                var parsed = ExprLang.Parse(src);
                if (!parsed.Ok) return "0";
                return ExprLang.Cpp(parsed.Root, exprTypes);
            }

            public void EmitChain(GraphNode from, string port, StringBuilder b, int ind, HashSet<string> path)
            {
                var l = GraphUtil.ExecOutLink(g, from.Uid, port);
                if (l == null) return;
                var n = GraphUtil.FindNode(g, l.ToNode);
                if (n == null || !path.Add(n.Uid + ":" + port)) return;
                Emit(n, b, ind, path);
                path.Remove(n.Uid + ":" + port);
            }
            private void Emit(GraphNode n, StringBuilder b, int ind, HashSet<string> path)
            {
                var pad = new string(' ', ind * 2);
                switch (n.Type)
                {
                    case "entry": EmitChain(n, "out", b, ind, path); break;
                    case "vardecl":
                        {
                            // 长度：resize 出 N 个默认值，和 VM 里逐格 Add(DefaultVal) 等价；负数按 0 处理
                            var size = ExprField(n, "size");
                            if (size != null && ExprLang.IsArrayType(GraphUtil.Get(n, "vtype")))
                                b.AppendLine(pad + "{ long long __n = " + size + "; if (__n > 0) " + Safe(GraphUtil.Get(n, "name")) + ".resize((size_t)__n); }");
                            var init = ExprField(n, "initExpr");
                            if (init != null) b.AppendLine(pad + Safe(GraphUtil.Get(n, "name")) + " = " + init + ";");
                            EmitChain(n, "out", b, ind, path); break;
                        }
                    case "input":
                        {
                            var target = GraphUtil.Get(n, "name");
                            string inName = null, inIdx = null;
                            bool indexed = !string.IsNullOrEmpty(target)
                                && Assign.TryParseTarget(target, out inName, out inIdx) && inIdx != null;
                            if (!indexed)
                            {
                                b.AppendLine(pad + "cin >> " + (string.IsNullOrEmpty(target) ? InputName(n) : Safe(target)) + ";");
                                EmitChain(n, "out", b, ind, path); break;
                            }
                            // 读入 a[i]：先读进临时变量，再按下标补长写入，和 VM 里「不够长就补默认值」一致
                            var arr = Safe(inName);
                            b.AppendLine(pad + "cin >> " + InputName(n) + ";");
                            b.AppendLine(pad + "{ long long __ii = (long long)(" + (ExprOf(n, inIdx) ?? "0") + ");");
                            b.AppendLine(pad + "  if (__ii < 0 || __ii > 1000000) { cerr << \"数组下标越界：\" << __ii << endl; return 1; }");
                            b.AppendLine(pad + "  if (__ii >= (long long)" + arr + ".size()) " + arr + ".resize((size_t)__ii + 1);");
                            b.AppendLine(pad + "  " + arr + "[(size_t)__ii] = " + InputName(n) + "; }");
                            EmitChain(n, "out", b, ind, path); break;
                        }
                    case "output": b.AppendLine(pad + "cout << " + (ExprField(n, "outputExpr") ?? Expr(n, "value")) + " << '\\n';"); EmitChain(n, "out", b, ind, path); break;
                    case "varset":
                        {
                            // a[i] = x 与 sum = x 都写在这一个模块里；下标越界先 resize，和 VM 一致。
                            var src = GraphUtil.Get(n, "valueExpr");
                            var asg = Assign.TryParse(src);
                            var target = asg != null ? asg.Name : GraphUtil.Get(n, "name");
                            if (asg != null && asg.IsIndexed)
                            {
                                var arrName = Safe(asg.Name);
                                b.AppendLine(pad + "{ long long __idx = " + (ExprOf(n, asg.IndexExpr) ?? "0") + ";");
                                b.AppendLine(pad + "  if (__idx >= (long long)" + arrName + ".size()) " + arrName + ".resize((size_t)__idx + 1);");
                                b.AppendLine(pad + "  " + arrName + "[(size_t)__idx] = " + (ExprOf(n, asg.Rhs) ?? Expr(n, "value")) + "; }");
                            }
                            else b.AppendLine(pad + Safe(target) + " = " + (ExprOf(n, asg != null ? asg.Rhs : src) ?? Expr(n, "value")) + ";");
                            EmitChain(n, "out", b, ind, path); break;
                        }
                    case "branch":
                        b.AppendLine(pad + "if (" + (ExprField(n, "condExpr") ?? Expr(n, "cond")) + ") {"); EmitChain(n, "true", b, ind + 1, new HashSet<string>(path));
                        b.AppendLine(pad + "} else {"); EmitChain(n, "false", b, ind + 1, new HashSet<string>(path)); b.AppendLine(pad + "}"); break;
                    case "forloop":
                        // 循环变量用节点上选的名字；__index 留给老图的数据线，内层循环靠作用域自然遮蔽。
                        {
                            var iv = VarScope.IndexName(n);
                            // 倒序：从起点往下走，比较方向和步长一起翻；起点 / 终点谁大都由 dir 决定，不替玩家交换
                            bool desc = VarScope.IsDesc(n);
                            b.AppendLine(pad + "for (long long " + Safe(iv) + " = " + (ExprField(n, "fromExpr") ?? Expr(n, "from")) + ", __bound_" + Safe(n.Uid) + " = " + (ExprField(n, "toExpr") ?? Expr(n, "to")) + "; " + Safe(iv) + (desc ? " >= " : " <= ") + "__bound_" + Safe(n.Uid) + "; " + (desc ? "--" : "++") + Safe(iv) + ") {");
                            b.AppendLine(pad + "  long long __index = " + Safe(iv) + ";");
                            EmitChain(n, "body", b, ind + 1, new HashSet<string>(path)); b.AppendLine(pad + "}"); EmitChain(n, "done", b, ind, path); break;
                        }
                    case "whileloop":
                        b.AppendLine(pad + "while (" + (ExprField(n, "condExpr") ?? Expr(n, "cond")) + ") {");
                        EmitChain(n, "body", b, ind + 1, new HashSet<string>(path)); b.AppendLine(pad + "}"); EmitChain(n, "done", b, ind, path); break;
                    case "structnew": b.AppendLine(pad + Safe(GraphUtil.Get(n, "structId")) + ".clear();"); EmitChain(n, "out", b, ind, path); break;
                    case "stack_push": b.AppendLine(pad + Safe(GraphUtil.Get(n, "structId")) + ".push_back(" + (ExprField(n, "pushExpr") ?? Expr(n, "value")) + ");"); EmitChain(n, "out", b, ind, path); break;
                    case "stack_pop":
                        b.AppendLine(pad + Temp(n) + " = " + Safe(GraphUtil.Get(n, "structId")) + ".back();");
                        b.AppendLine(pad + Safe(GraphUtil.Get(n, "structId")) + ".pop_back();"); EmitChain(n, "out", b, ind, path); break;
                    case "queue_push": b.AppendLine(pad + Safe(GraphUtil.Get(n, "structId")) + ".push_back(" + (ExprField(n, "pushExpr") ?? Expr(n, "value")) + ");"); EmitChain(n, "out", b, ind, path); break;
                    case "queue_pop":
                        b.AppendLine(pad + Temp(n) + " = " + Safe(GraphUtil.Get(n, "structId")) + ".front();");
                        b.AppendLine(pad + Safe(GraphUtil.Get(n, "structId")) + ".pop_front();"); EmitChain(n, "out", b, ind, path); break;
                    default: EmitChain(n, "out", b, ind, path); break;
                }
            }
            private string Expr(GraphNode n, string port)
            {
                var l = GraphUtil.LinkInto(g, n.Uid, port);
                if (l == null)
                {
                    if (n.Type == "binop" && (port == "a" || port == "b"))
                    {
                        string vn = GraphUtil.Get(n, port == "a" ? "varA" : "varB");
                        if (!string.IsNullOrEmpty(vn)) return Safe(vn);
                    }
                    return "0";
                }
                var s = GraphUtil.FindNode(g, l.FromNode);
                if (s == null) return "0";
                switch (s.Type)
                {
                    case "const": return Literal(GraphUtil.Get(s, "value"), GraphUtil.Get(s, "vtype"));
                    case "input":
                        {
                            var target = GraphUtil.Get(s, "name");
                            if (string.IsNullOrEmpty(target)) return InputName(s);
                            string vn = null, vi = null;
                            if (Assign.TryParseTarget(target, out vn, out vi) && vi != null)
                                return Safe(vn) + "[(size_t)(" + (ExprOf(s, vi) ?? "0") + ")]";
                            return Safe(target);
                        }
                    case "varget": case "varset": case "vardecl": return Safe(GraphUtil.Get(s, "name"));
                    case "forloop": return "__index";
                    case "binop":
                    {
                        var ex = GraphUtil.Get(s, "expr");
                        if (!string.IsNullOrEmpty(ex)) return ExprFormat.Cpp(ExprFormat.Parse(ex));
                        return "(" + Expr(s, "a") + " " + (GraphUtil.Get(s, "op") ?? "+") + " " + Expr(s, "b") + ")";
                    }
                    case "compare": return "(" + Expr(s, "a") + " " + (GraphUtil.Get(s, "op") ?? "==") + " " + Expr(s, "b") + ")";
                    case "logic": { var op = GraphUtil.Get(s, "op"); return op == "not" ? "(!" + Expr(s, "a") + ")" : "(" + Expr(s, "a") + (op == "or" ? " || " : " && ") + Expr(s, "b") + ")"; }
                    case "stack_top": return Safe(GraphUtil.Get(s, "structId")) + ".back()";
                    case "stack_pop": return Temp(s);
                    case "stack_empty": return Safe(GraphUtil.Get(s, "structId")) + ".empty()";
                    case "stack_size": return "(int)" + Safe(GraphUtil.Get(s, "structId")) + ".size()";
                    case "queue_front": return Safe(GraphUtil.Get(s, "structId")) + ".front()";
                    case "queue_pop": return Temp(s);
                    case "queue_empty": return Safe(GraphUtil.Get(s, "structId")) + ".empty()";
                    case "queue_size": return "(int)" + Safe(GraphUtil.Get(s, "structId")) + ".size()";
                    case "text_index": return Expr(s, "s") + "[(size_t)" + Expr(s, "i") + "]";
                    case "len_of": return "(int)" + Expr(s, "v") + ".size()";
                    default: return "0";
                }
            }
        }
        private static string CppType(string t)
        {
            if (string.IsNullOrEmpty(t)) return "int";
            // 数组类型：int[] / double[] / string[] / char[]，元素类型决定 vector 的模板参数。
            if (t.EndsWith("[]", StringComparison.Ordinal)) return "std::vector<" + CppElemType(t.Substring(0, t.Length - 2)) + ">";
            switch (t) { case "ll": return "long long"; case "double": return "double"; case "bool": return "bool"; case "char": return "char"; case "string": return "string"; default: return "int"; }
        }
        private static string CppElemType(string t)
        {
            switch (t) { case "int": case "ll": return "long long"; case "double": return "double"; case "bool": return "bool"; case "char": return "char"; case "string": return "std::string"; default: return "int"; }
        }
        // 表达式运行时认识的类型：ll 当作 int，数组保留 [] 后缀。
        private static string ExprTypeOf(string t)
        {
            if (string.IsNullOrEmpty(t)) return "int";
            if (t.EndsWith("[]", StringComparison.Ordinal)) return ExprTypeOf(t.Substring(0, t.Length - 2)) + "[]";
            return t == "ll" ? "int" : t;
        }
        private static string Literal(string value, string type)
        {
            value = value ?? "0";
            if (type == "string") return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            if (type == "char") return "'" + (value.Length == 0 ? " " : value.Substring(0, 1).Replace("'", "\\'")) + "'";
            if (type == "bool") return value == "true" || value == "1" ? "true" : "false";
            return value;
        }
        private static string Safe(string x) { if (string.IsNullOrEmpty(x)) return "_"; var b = new StringBuilder(); foreach (var ch in x) b.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_'); return b.ToString(); }
    }
}
