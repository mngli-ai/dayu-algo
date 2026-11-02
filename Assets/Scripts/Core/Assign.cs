using System;
using System.Text;

namespace Dayu.Core
{
    /// <summary>顶层赋值语句：v = 表达式 / a[i] = 表达式 / v += 表达式。右边仍交给 ExprLang 解析。</summary>
    public sealed class AssignStatement
    {
        public string Name;
        public string IndexExpr;
        public string Rhs;

        /// <summary>玩家写的复合赋值运算符（+= -= …）；普通 = 时为空串。Rhs 已经脱糖成「左边 op 右边」。</summary>
        public string Op = "";

        public bool IsIndexed { get { return !string.IsNullOrEmpty(IndexExpr); } }

        public string LeftText { get { return IsIndexed ? Name + "[" + IndexExpr + "]" : Name; } }

        public override string ToString() { return LeftText + (string.IsNullOrEmpty(Op) ? " = " : " " + Op + "= ") + Rhs; }
    }

    public static class Assign
    {
        /// <summary>源码是赋值语句就返回结果，否则返回 null。括号、方括号里的等号不算。</summary>
        public static AssignStatement TryParse(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            string text = source.Trim();
            int eq = FindTopLevelEquals(text);
            if (eq < 0) return null;
            // 复合赋值（s += t、a[i] <<= 1）在这里脱糖成 s = s + t：
            // Vm 与 CppGen 只认 Rhs，不用各自再实现一遍运算。下标表达式在这门语言里没有副作用，
            // 所以 a[i] = a[i] + x 与 C++ 的 a[i] += x 求值次数不同也不会产生分歧。
            string op = CompoundOp(text, eq);
            string left = text.Substring(0, eq - op.Length).Trim();
            string right = text.Substring(eq + 1).Trim();
            if (left.Length == 0 || right.Length == 0) return null;

            var st = new AssignStatement { Op = op };
            int bracket = left.IndexOf('[');
            if (bracket < 0)
            {
                if (!IsIdentifier(left)) return null;
                st.Name = left;
            }
            else
            {
                if (!left.EndsWith("]", StringComparison.Ordinal)) return null;
                string name = left.Substring(0, bracket).Trim();
                string index = left.Substring(bracket + 1, left.Length - bracket - 2).Trim();
                if (!IsIdentifier(name) || index.Length == 0 || index.IndexOf('[') >= 0) return null;
                st.Name = name;
                st.IndexExpr = index;
            }
            st.Rhs = op.Length == 0 ? right : st.LeftText + " " + op + " " + right;
            return st;
        }

        /// <summary>拼回一条赋值语句，供旧图迁移和卡片显示用。</summary>
        public static string Compose(string name, string indexExpr, string rhs)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(rhs)) return rhs ?? "";
            var sb = new StringBuilder();
            sb.Append(name);
            if (!string.IsNullOrEmpty(indexExpr)) sb.Append('[').Append(indexExpr).Append(']');
            sb.Append(" = ").Append(rhs);
            return sb.ToString();
        }

        /// <summary>只解析赋值左边：「a」或「a[i]」。输入模块要把值直接写进数组元素时用它。</summary>
        public static bool TryParseTarget(string s, out string name, out string indexExpr)
        {
            name = null; indexExpr = null;
            if (string.IsNullOrEmpty(s)) return false;
            var t = s.Trim();
            if (t.Length == 0) return false;
            int open = t.IndexOf('[');
            if (open < 0)
            {
                if (!IsIdentifier(t)) return false;
                name = t;
                return true;
            }
            if (t[t.Length - 1] != ']') return false;
            if (!IsIdentifier(t.Substring(0, open))) return false;
            var idx = t.Substring(open + 1, t.Length - open - 2).Trim();
            if (idx.Length == 0) return false;
            name = t.Substring(0, open);
            indexExpr = idx;
            return true;
        }

        // = 左边紧贴着的复合赋值运算符；普通 = 返回空串
        static string CompoundOp(string s, int eq)
        {
            if (eq <= 0) return "";
            char p = s[eq - 1];
            if (p == '<' || p == '>') return eq >= 2 && s[eq - 2] == p ? new string(p, 2) : "";
            switch (p)
            {
                case '+': case '-': case '*': case '/': case '%':
                case '&': case '|': case '^':
                    return p.ToString();
            }
            return "";
        }

        /// <summary>
        /// 顶层赋值号的位置：找到返回下标，一个都没有返回 -1，出现两条（多半是写错了）返回 -2。
        /// 编译器用它区分「还没写等号」和「写了但整句没解析成功」，别说反了误导玩家。
        /// </summary>
        public static int TopLevelEqualsAt(string source)
        {
            return FindTopLevelEquals(source);
        }

        static int FindTopLevelEquals(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            s = s.Trim();
            // 深度为 0 的单个 =；==、!=、<=、>= 以及括号内的都跳过。出现两条顶层赋值返回 -2。
            // <<= >>= 是复合赋值，要认；<= >= 是比较，不认。
            int depth = 0, found = -1;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(' || c == '[') { depth++; continue; }
                if (c == ')' || c == ']') { depth--; continue; }
                if (depth != 0 || c != '=') continue;
                char prev = i > 0 ? s[i - 1] : '\0';
                char prev2 = i > 1 ? s[i - 2] : '\0';
                char next = i + 1 < s.Length ? s[i + 1] : '\0';
                if (prev == '=' || prev == '!') continue;
                if ((prev == '<' || prev == '>') && prev2 != prev) continue;
                if (next == '=') { i++; continue; }
                if (found >= 0) return -2;
                found = i;
            }
            return found;
        }

        static bool IsIdentifier(string x)
        {
            if (string.IsNullOrEmpty(x)) return false;
            char first = x[0];
            if (!char.IsLetter(first) && first != '_' && first < '\u3400') return false;
            for (int i = 1; i < x.Length; i++)
            {
                char c = x[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c < '\u3400') return false;
            }
            return true;
        }
    }
}
