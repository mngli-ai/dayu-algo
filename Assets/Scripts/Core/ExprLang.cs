using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 纯 C# 键盘输入表达式语言：词法 → 递归下降语法 → 类型检查 → 求值 → C++17 生成。
// 不依赖 UnityEngine，可被 Core / Editor / 测试直接复用。
namespace Dayu.Core
{
    public sealed class ExprToken
    {
        public string Kind;     // int real string char ident bool punct end
        public string Text;     // 运算符、标识符名、字面量原文
        public int Start;
        public int End;
        public long IntValue;
        public double RealValue;
        public bool BoolValue;
        public string StrValue;
        public char CharValue;

        public override string ToString() { return Kind + ":" + Text; }
    }

    public sealed class ExprAst
    {
        public string Kind;                 // int real bool string char var unary binary index member call array
        public int Start;
        public int End;
        public string Text;                 // 运算符 / 变量名 / 成员名 / 函数名
        public long IntValue;
        public double RealValue;
        public bool BoolValue;
        public string StrValue;
        public char CharValue;
        public List<ExprAst> Children = new List<ExprAst>();

        public bool IsLiteral
        {
            get
            {
                return Kind == "int" || Kind == "real" || Kind == "bool" || Kind == "string" || Kind == "char";
            }
        }

        public override string ToString() { return Kind + (string.IsNullOrEmpty(Text) ? "" : "(" + Text + ")"); }
    }

    public sealed class ExprParseResult
    {
        public bool Ok;
        public ExprAst Root;
        public string Error;
        public int Position = -1;
    }

    public sealed class ExprCheckResult
    {
        public bool Ok;
        public string Type = "unknown";
        public string Error;
        public int Position = -1;
    }

    public sealed class ExprEvalResult
    {
        public bool Ok;
        public object Value;
        public string Error;
        public int Position = -1;

        public static ExprEvalResult Fail(string message, int position)
        {
            return new ExprEvalResult { Ok = false, Error = message, Position = position };
        }

        public static ExprEvalResult Good(object value)
        {
            return new ExprEvalResult { Ok = true, Value = value };
        }
    }

    public delegate object ExprLookup(string name);

    // 容器实例交给表达式求值时的视图：类型名（stack<int> / queue<int>）+ 当前内容，
    // 成员访问按这颗查词表，栈顶取末尾、队头取首位。
    public sealed class ExprContainer
    {
        public string Type;
        public List<object> Items;

        public ExprContainer(string type, List<object> items) { Type = type; Items = items; }

        public bool IsQueue { get { return Members.IsQueue(Type); } }
    }

    public static class ExprLang
    {
        // ==================== 词法 ====================

        sealed class Lexer
        {
            readonly string _s;
            int _i;

            public Lexer(string source) { _s = source ?? ""; }

            public List<ExprToken> Scan()
            {
                var list = new List<ExprToken>();
                while (true)
                {
                    SkipSpace();
                    if (_i >= _s.Length)
                    {
                        list.Add(new ExprToken { Kind = "end", Text = "", Start = _s.Length, End = _s.Length });
                        return list;
                    }
                    char c = _s[_i];
                    if (char.IsDigit(c)) { list.Add(ReadNumber()); continue; }
                    if (c == '"') { list.Add(ReadString()); continue; }
                    if (c == '\'') { list.Add(ReadChar()); continue; }
                    if (IsIdentStart(c)) { list.Add(ReadIdent()); continue; }
                    list.Add(ReadPunct());
                }
            }

            void SkipSpace()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            static bool IsIdentStart(char c) { return c == '_' || char.IsLetter(c); }
            static bool IsIdentPart(char c) { return c == '_' || char.IsLetterOrDigit(c); }

            ExprToken ReadNumber()
            {
                int start = _i;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                bool real = false;
                if (_i + 1 < _s.Length && _s[_i] == '.' && char.IsDigit(_s[_i + 1]))
                {
                    real = true;
                    _i++;
                    while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                }
                string text = _s.Substring(start, _i - start);
                if (real)
                {
                    double d;
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        throw new ExprSyntaxException(start, "无法解析小数「" + text + "」。");
                    return new ExprToken { Kind = "real", Text = text, Start = start, End = _i, RealValue = d };
                }
                long v;
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    throw new ExprSyntaxException(start, "整数「" + text + "」超出范围。");
                return new ExprToken { Kind = "int", Text = text, Start = start, End = _i, IntValue = v };
            }

            ExprToken ReadString()
            {
                int start = _i;
                _i++; // 跳过开引号
                var sb = new StringBuilder();
                while (true)
                {
                    if (_i >= _s.Length) throw new ExprSyntaxException(start, "字符串缺少结束的双引号。");
                    char c = _s[_i];
                    if (c == '"') { _i++; break; }
                    if (c == '\\') { sb.Append(ReadEscape()); continue; }
                    sb.Append(c);
                    _i++;
                }
                return new ExprToken { Kind = "string", Text = _s.Substring(start, _i - start), Start = start, End = _i, StrValue = sb.ToString() };
            }

            ExprToken ReadChar()
            {
                int start = _i;
                _i++; // 跳过开引号
                if (_i >= _s.Length) throw new ExprSyntaxException(start, "字符缺少结束的单引号。");
                char value;
                if (_s[_i] == '\\') value = ReadEscape();
                else { value = _s[_i]; _i++; }
                if (_i >= _s.Length || _s[_i] != '\'')
                    throw new ExprSyntaxException(start, "字符字面量只能包含一个字符，并且需要单引号结束。");
                _i++;
                return new ExprToken { Kind = "char", Text = _s.Substring(start, _i - start), Start = start, End = _i, CharValue = value };
            }

            char ReadEscape()
            {
                int pos = _i;
                _i++; // 跳过反斜杠
                if (_i >= _s.Length) throw new ExprSyntaxException(pos, "转义字符不完整。");
                char e = _s[_i++];
                switch (e)
                {
                    case 'n': return '\n';
                    case 't': return '\t';
                    case 'r': return '\r';
                    case '0': return '\0';
                    case '\\': return '\\';
                    case '"': return '"';
                    case '\'': return '\'';
                    default: throw new ExprSyntaxException(pos, "不支持的转义字符「\\" + e + "」。");
                }
            }

            ExprToken ReadIdent()
            {
                int start = _i;
                while (_i < _s.Length && IsIdentPart(_s[_i])) _i++;
                string text = _s.Substring(start, _i - start);
                if (text == "true" || text == "false")
                    return new ExprToken { Kind = "bool", Text = text, Start = start, End = _i, BoolValue = text == "true" };
                return new ExprToken { Kind = "ident", Text = text, Start = start, End = _i };
            }

            ExprToken ReadPunct()
            {
                int start = _i;
                char c = _s[_i];
                if (_i + 1 < _s.Length)
                {
                    string two = _s.Substring(_i, 2);
                    if (two == "==" || two == "!=" || two == "<=" || two == ">=" || two == "&&" || two == "||"
                        || two == "<<" || two == ">>")
                    {
                        _i += 2;
                        return new ExprToken { Kind = "punct", Text = two, Start = start, End = _i };
                    }
                }
                switch (c)
                {
                    case '+':
                    case '-':
                    case '*':
                    case '/':
                    case '%':
                    case '(':
                    case ')':
                    case '[':
                    case ']':
                    case ',':
                    case '.':
                    case '!':
                    case '<':
                    case '>':
                    case '&':
                    case '|':
                    case '^':
                    case '~':
                        _i++;
                        return new ExprToken { Kind = "punct", Text = c.ToString(), Start = start, End = _i };
                }
                throw new ExprSyntaxException(start, "无法识别的字符「" + c + "」。");
            }
        }

        sealed class ExprSyntaxException : Exception
        {
            public readonly int Position;
            public ExprSyntaxException(int position, string message) : base(message) { Position = position; }
        }

        // ==================== 语法 ====================

        sealed class Parser
        {
            readonly List<ExprToken> _t;
            int _i;

            public Parser(List<ExprToken> tokens) { _t = tokens; }

            ExprToken Cur { get { return _t[_i]; } }

            bool IsPunct(string text)
            {
                return Cur.Kind == "punct" && Cur.Text == text;
            }

            ExprToken Accept(string text)
            {
                if (!IsPunct(text)) return null;
                var tok = Cur;
                _i++;
                return tok;
            }

            ExprToken Expect(string text, string message)
            {
                if (!IsPunct(text)) throw new ExprSyntaxException(Cur.Start, message);
                var tok = Cur;
                _i++;
                return tok;
            }

            public ExprAst ParseAll()
            {
                var root = ParseOr();
                if (Cur.Kind != "end")
                    throw new ExprSyntaxException(Cur.Start, "表达式末尾有多余内容「" + Cur.Text + "」。");
                return root;
            }

            ExprAst ParseOr()
            {
                var left = ParseAnd();
                while (IsPunct("||"))
                {
                    _i++;
                    var right = ParseAnd();
                    left = Binary("||", left, right);
                }
                return left;
            }

            ExprAst ParseAnd()
            {
                var left = ParseEquality();
                while (IsPunct("&&"))
                {
                    _i++;
                    var right = ParseEquality();
                    left = Binary("&&", left, right);
                }
                return left;
            }

            ExprAst ParseEquality()
            {
                var left = ParseRelational();
                while (IsPunct("==") || IsPunct("!="))
                {
                    string op = Cur.Text;
                    _i++;
                    var right = ParseRelational();
                    left = Binary(op, left, right);
                }
                return left;
            }

            ExprAst ParseRelational()
            {
                var left = ParseBitOr();
                while (IsPunct("<") || IsPunct("<=") || IsPunct(">") || IsPunct(">="))
                {
                    string op = Cur.Text;
                    _i++;
                    var right = ParseBitOr();
                    left = Binary(op, left, right);
                }
                return left;
            }

            // 位运算按用户口径排在比较之上、算术之下（和 Python 一致，与 C++ 不同）：
            // a & b == c 读作 (a & b) == c，1 + 2 << 3 读作 (1 + 2) << 3。
            // 生成 C++ 时每个二元节点都带全括号，所以对拍结果不受 C++ 原生优先级影响。
            ExprAst ParseBitOr()
            {
                var left = ParseBitXor();
                while (IsPunct("|"))
                {
                    _i++;
                    var right = ParseBitXor();
                    left = Binary("|", left, right);
                }
                return left;
            }

            ExprAst ParseBitXor()
            {
                var left = ParseBitAnd();
                while (IsPunct("^"))
                {
                    _i++;
                    var right = ParseBitAnd();
                    left = Binary("^", left, right);
                }
                return left;
            }

            ExprAst ParseBitAnd()
            {
                var left = ParseShift();
                while (IsPunct("&"))
                {
                    _i++;
                    var right = ParseShift();
                    left = Binary("&", left, right);
                }
                return left;
            }

            ExprAst ParseShift()
            {
                var left = ParseAdditive();
                while (IsPunct("<<") || IsPunct(">>"))
                {
                    string op = Cur.Text;
                    _i++;
                    var right = ParseAdditive();
                    left = Binary(op, left, right);
                }
                return left;
            }

            ExprAst ParseAdditive()
            {
                var left = ParseMultiplicative();
                while (IsPunct("+") || IsPunct("-"))
                {
                    string op = Cur.Text;
                    _i++;
                    var right = ParseMultiplicative();
                    left = Binary(op, left, right);
                }
                return left;
            }

            ExprAst ParseMultiplicative()
            {
                var left = ParseUnary();
                while (IsPunct("*") || IsPunct("/") || IsPunct("%"))
                {
                    string op = Cur.Text;
                    _i++;
                    var right = ParseUnary();
                    left = Binary(op, left, right);
                }
                return left;
            }

            ExprAst ParseUnary()
            {
                if (IsPunct("-") || IsPunct("!") || IsPunct("~"))
                {
                    var op = Cur;
                    _i++;
                    var operand = ParseUnary();
                    return new ExprAst
                    {
                        Kind = "unary",
                        Text = op.Text,
                        Start = op.Start,
                        End = operand.End,
                        Children = new List<ExprAst> { operand }
                    };
                }
                return ParsePostfix();
            }

            ExprAst ParsePostfix()
            {
                var node = ParsePrimary();
                while (true)
                {
                    if (IsPunct("["))
                    {
                        var open = Cur;
                        _i++;
                        var index = ParseOr();
                        Expect("]", "数组下标缺少「]」。");
                        int end = PreviousEnd();
                        node = new ExprAst
                        {
                            Kind = "index",
                            Start = node.Start,
                            End = end,
                            Children = new List<ExprAst> { node, index }
                        };
                        continue;
                    }
                    if (IsPunct("."))
                    {
                        _i++;
                        if (Cur.Kind != "ident")
                            throw new ExprSyntaxException(Cur.Start, "「.」后面要跟成员名：打完「.」会弹出补全，从里面挑一个就行。");
                        var name = Cur;
                        _i++;
                        node = new ExprAst
                        {
                            Kind = "member",
                            Text = name.Text,
                            Start = node.Start,
                            End = name.End,
                            Children = new List<ExprAst> { node }
                        };
                        continue;
                    }
                    if (IsPunct("("))
                    {
                        if (node.Kind != "var")
                            throw new ExprSyntaxException(Cur.Start, "只有函数名可以加括号调用，例如 len(x)、size(x)。");
                        _i++;
                        var args = new List<ExprAst>();
                        if (!IsPunct(")"))
                        {
                            args.Add(ParseOr());
                            while (IsPunct(","))
                            {
                                _i++;
                                args.Add(ParseOr());
                            }
                        }
                        var close = Expect(")", "函数调用缺少「)」。");
                        node = new ExprAst
                        {
                            Kind = "call",
                            Text = node.Text,
                            Start = node.Start,
                            End = close.End,
                            Children = args
                        };
                        continue;
                    }
                    break;
                }
                return node;
            }

            int PreviousEnd() { return _i > 0 ? _t[_i - 1].End : 0; }

            ExprAst ParsePrimary()
            {
                var tok = Cur;
                switch (tok.Kind)
                {
                    case "int":
                        _i++;
                        return new ExprAst { Kind = "int", Start = tok.Start, End = tok.End, IntValue = tok.IntValue, Text = tok.Text };
                    case "real":
                        _i++;
                        return new ExprAst { Kind = "real", Start = tok.Start, End = tok.End, RealValue = tok.RealValue, Text = tok.Text };
                    case "bool":
                        _i++;
                        return new ExprAst { Kind = "bool", Start = tok.Start, End = tok.End, BoolValue = tok.BoolValue, Text = tok.Text };
                    case "string":
                        _i++;
                        return new ExprAst { Kind = "string", Start = tok.Start, End = tok.End, StrValue = tok.StrValue, Text = tok.Text };
                    case "char":
                        _i++;
                        return new ExprAst { Kind = "char", Start = tok.Start, End = tok.End, CharValue = tok.CharValue, Text = tok.Text };
                    case "ident":
                        _i++;
                        return new ExprAst { Kind = "var", Start = tok.Start, End = tok.End, Text = tok.Text };
                    case "punct":
                        if (tok.Text == "(")
                        {
                            _i++;
                            var inner = ParseOr();
                            var close = Expect(")", "括号没有闭合，缺少「)」。");
                            inner.Start = tok.Start;
                            inner.End = close.End;
                            return inner;
                        }
                        if (tok.Text == "[")
                        {
                            _i++;
                            var items = new List<ExprAst>();
                            if (!IsPunct("]"))
                            {
                                items.Add(ParseOr());
                                while (IsPunct(","))
                                {
                                    _i++;
                                    if (IsPunct("]")) break;
                                    items.Add(ParseOr());
                                }
                            }
                            var close = Expect("]", "数组字面量缺少「]」。");
                            return new ExprAst { Kind = "array", Start = tok.Start, End = close.End, Children = items };
                        }
                        if (tok.Text == ")") throw new ExprSyntaxException(tok.Start, "多余的「)」。");
                        if (tok.Text == "]") throw new ExprSyntaxException(tok.Start, "多余的「]」。");
                        throw new ExprSyntaxException(tok.Start, "这里需要一个值，但遇到了「" + tok.Text + "」。");
                    case "end":
                        throw new ExprSyntaxException(tok.Start, "表达式意外结束，这里还需要一个值。");
                }
                throw new ExprSyntaxException(tok.Start, "无法解析的记号「" + tok.Text + "」。");
            }

            static ExprAst Binary(string op, ExprAst a, ExprAst b)
            {
                return new ExprAst
                {
                    Kind = "binary",
                    Text = op,
                    Start = a.Start,
                    End = b.End,
                    Children = new List<ExprAst> { a, b }
                };
            }
        }

        // ==================== 入口：解析 ====================

        public static ExprParseResult Parse(string source)
        {
            var result = new ExprParseResult();
            if (string.IsNullOrWhiteSpace(source))
            {
                result.Error = "表达式为空，请输入内容。";
                result.Position = 0;
                return result;
            }
            try
            {
                var tokens = new Lexer(source).Scan();
                result.Root = new Parser(tokens).ParseAll();
                result.Ok = true;
                return result;
            }
            catch (ExprSyntaxException ex)
            {
                result.Error = "第 " + (ex.Position + 1) + " 个字符：" + ex.Message;
                result.Position = ex.Position;
                return result;
            }
        }

        public static ExprAst ParseOrThrow(string source)
        {
            var r = Parse(source);
            if (!r.Ok) throw new ArgumentException(r.Error);
            return r.Root;
        }

        // ==================== 类型 ====================

        public static bool IsArrayType(string type)
        {
            return !string.IsNullOrEmpty(type) && type.EndsWith("[]", StringComparison.Ordinal);
        }

        public static string ElementType(string type)
        {
            return IsArrayType(type) ? type.Substring(0, type.Length - 2) : type;
        }

        public static bool IsNumeric(string type) { return type == "int" || type == "double"; }

        // 位运算符：都是整数运算，小数一律拒绝（避免玩家以为 1.5 << 1 能跑）
        public static bool IsBitop(string op)
        {
            return op == "&" || op == "|" || op == "^" || op == "<<" || op == ">>";
        }

        static string NumericResult(string a, string b)
        {
            return (a == "double" || b == "double") ? "double" : "int";
        }

        // ==================== 类型检查 ====================

        sealed class Checker
        {
            readonly IDictionary<string, string> _types;
            string _error;
            int _pos = -1;

            public Checker(IDictionary<string, string> types) { _types = types; }

            public string Error { get { return _error; } }
            public int Position { get { return _pos; } }

            string Fail(string message, int position)
            {
                if (_error == null) { _error = message; _pos = position; }
                return "unknown";
            }

            public string TypeOf(ExprAst n)
            {
                if (n == null) return Fail("表达式节点为空。", 0);
                switch (n.Kind)
                {
                    case "int": return "int";
                    case "real": return "double";
                    case "bool": return "bool";
                    case "string": return "string";
                    case "char": return "char";
                    case "var":
                        {
                            string t;
                            if (_types != null && _types.TryGetValue(n.Text, out t) && !string.IsNullOrEmpty(t)) return t;
                            // pi / e 这类数学常量：没被同名变量挡住才按常量算（玩家自己声明了 pi 就用他的）
                            if (MathFns.IsConstant(n.Text)) return "double";
                            return Fail("未定义的变量「" + n.Text + "」。", n.Start);
                        }
                    case "unary":
                        {
                            string t = TypeOf(n.Children[0]);
                            if (_error != null) return "unknown";
                            if (n.Text == "!") return t == "bool" ? "bool" : Fail("逻辑取反需要 bool，实际为 " + t + "。", n.Start);
                            if (n.Text == "~") return t == "int" ? "int" : Fail("按位取反「~」需要整数 (int)，实际为 " + t + "。", n.Start);
                            return IsNumeric(t) ? t : Fail("一元负号需要数值，实际为 " + t + "。", n.Start);
                        }
                    case "binary": return BinaryType(n);
                    case "index":
                        {
                            string baseType = TypeOf(n.Children[0]);
                            if (_error != null) return "unknown";
                            // 字符串也能按下标取一格，拿到的是 char：和 C++ 的 s[i] 同一个口径，
                            // 括号匹配这类题才不用为「逐个字符看」再画一根数据线。
                            bool text = baseType == "string";
                            if (!text && !IsArrayType(baseType)) return Fail("类型 " + baseType + " 不是数组，不能取下标。", n.Start);
                            string idxType = TypeOf(n.Children[1]);
                            if (_error != null) return "unknown";
                            if (idxType != "int") return Fail("数组下标的类型必须是 int，实际为 " + idxType + "。", n.Children[1].Start);
                            return text ? "char" : ElementType(baseType);
                        }
                    case "member":
                        {
                            string baseType = TypeOf(n.Children[0]);
                            if (_error != null) return "unknown";
                            // 成员认不认、返回什么类型，全按 Members 这份词表答，别在这里另列一遍名字
                            string ret = Members.ReturnType(baseType, n.Text);
                            if (!string.IsNullOrEmpty(ret)) return ret;
                            return Fail(Members.NotSupportedMessage(baseType, n.Text), n.Start);
                        }
                    case "call":
                        {
                            if (n.Text != "len" && n.Text != "size") return MathFnType(n);
                            if (n.Children.Count != 1)
                                return Fail("函数「" + n.Text + "」需要 1 个参数，实际为 " + n.Children.Count + " 个。", n.Start);
                            string t = TypeOf(n.Children[0]);
                            if (_error != null) return "unknown";
                            if (!IsArrayType(t) && !Members.IsContainer(t))
                                return Fail("函数「" + n.Text + "」的参数要数组或容器，实际为 " + t + "。", n.Children[0].Start);
                            return "int";
                        }
                    case "array": return ArrayType(n);
                }
                return Fail("未知的表达式节点「" + n.Kind + "」。", n.Start);
            }

            // 数学函数（abs / sqrt / pow / min / max …）：参数一律要数值，bool、字符串、数组都不收。
            // 返回类型跟着 C++ 的口径走，见 MathFns。
            string MathFnType(ExprAst n)
            {
                var d = MathFns.Find(n.Text);
                if (d == null) return Fail(MathFns.UnknownFnMessage(n.Text), n.Start);
                if (n.Children.Count != d.Arity)
                    return Fail("函数「" + n.Text + "」需要 " + d.Arity + " 个参数，实际为 " + n.Children.Count + " 个。", n.Start);
                var types = new List<string>();
                foreach (var c in n.Children)
                {
                    string t = TypeOf(c);
                    if (_error != null) return "unknown";
                    if (!IsNumeric(t)) return Fail("函数「" + n.Text + "」的参数要数值，实际为 " + t + "。", c.Start);
                    types.Add(t);
                }
                return MathFns.ReturnType(n.Text, types);
            }

            string ArrayType(ExprAst n)
            {
                if (n.Children.Count == 0) return Fail("空数组无法推断元素类型。", n.Start);
                string elem = null;
                foreach (var child in n.Children)
                {
                    string t = TypeOf(child);
                    if (_error != null) return "unknown";
                    if (IsArrayType(t)) return Fail("数组元素不能是数组，实际为 " + t + "。", child.Start);
                    if (elem == null) elem = t;
                    else if (elem != t)
                    {
                        if (IsNumeric(elem) && IsNumeric(t)) elem = "double";
                        else return Fail("数组元素类型不一致：" + elem + " 与 " + t + "。", child.Start);
                    }
                }
                return elem + "[]";
            }

            string BinaryType(ExprAst n)
            {
                string op = n.Text;
                if (op == "&&" || op == "||")
                {
                    string a = TypeOf(n.Children[0]);
                    string b = TypeOf(n.Children[1]);
                    if (_error != null) return "unknown";
                    if (a != "bool" || b != "bool") return Fail("逻辑运算「" + op + "」需要两侧都是 bool，实际为 " + a + " 与 " + b + "。", n.Start);
                    return "bool";
                }
                if (op == "==" || op == "!=" || op == "<" || op == "<=" || op == ">" || op == ">=")
                {
                    string a = TypeOf(n.Children[0]);
                    string b = TypeOf(n.Children[1]);
                    if (_error != null) return "unknown";
                    bool ok = (IsNumeric(a) && IsNumeric(b))
                              || (a == b && (a == "string" || a == "char" || a == "bool"));
                    if (!ok) return Fail("比较运算「" + op + "」两侧类型不匹配：" + a + " 与 " + b + "。", n.Start);
                    return "bool";
                }
                if (IsBitop(op))
                {
                    string a = TypeOf(n.Children[0]);
                    string b = TypeOf(n.Children[1]);
                    if (_error != null) return "unknown";
                    if (a != "int" || b != "int")
                        return Fail("位运算「" + op + "」两侧都必须是整数 (int)，实际为 " + a + " 与 " + b + "。", n.Start);
                    return "int";
                }
                if (op == "+")
                {
                    string a0 = TypeOf(n.Children[0]);
                    string b0 = TypeOf(n.Children[1]);
                    if (_error != null) return "unknown";
                    if (a0 == "string" && b0 == "string") return "string";
                    if (IsNumeric(a0) && IsNumeric(b0)) return NumericResult(a0, b0);
                    return Fail("运算符「+」两侧类型不匹配：" + a0 + " 与 " + b0 + "。", n.Start);
                }
                {
                    string a = TypeOf(n.Children[0]);
                    string b = TypeOf(n.Children[1]);
                    if (_error != null) return "unknown";
                    if (!IsNumeric(a) || !IsNumeric(b))
                        return Fail("运算符「" + op + "」需要数值，实际为 " + a + " 与 " + b + "。", n.Start);
                    return NumericResult(a, b);
                }
            }
        }

        public static ExprCheckResult Check(ExprAst node, IDictionary<string, string> variableTypes)
        {
            var result = new ExprCheckResult();
            if (node == null)
            {
                result.Error = "没有可检查的表达式。";
                return result;
            }
            var checker = new Checker(variableTypes);
            string type = checker.TypeOf(node);
            if (checker.Error != null)
            {
                result.Error = checker.Error;
                result.Position = checker.Position;
                result.Type = "unknown";
                return result;
            }
            if (IsArrayType(type))
            {
                result.Error = "输出结果不能是数组，实际为 " + type + "。";
                result.Position = node.Start;
                result.Type = type;
                return result;
            }
            // 整个容器当结果同理：cout 不出来，C++ 那边也编不过，得先按成员取一个值
            if (Members.IsContainer(type))
            {
                result.Error = "输出结果不能是整个容器，实际为 " + type + "，请用 size / empty / top / front 取一个值。";
                result.Position = node.Start;
                result.Type = type;
                return result;
            }
            result.Ok = true;
            result.Type = type;
            return result;
        }

        public static ExprCheckResult Check(string source, IDictionary<string, string> variableTypes)
        {
            var parsed = Parse(source);
            if (!parsed.Ok)
            {
                return new ExprCheckResult { Error = parsed.Error, Position = parsed.Position };
            }
            return Check(parsed.Root, variableTypes);
        }

        // ==================== 求值 ====================

        sealed class Evaluator
        {
            readonly ExprLookup _lookup;
            readonly IDictionary<string, ExprContainer> _containers;
            string _error;
            int _pos = -1;

            public Evaluator(ExprLookup lookup, IDictionary<string, ExprContainer> containers)
            {
                _lookup = lookup;
                _containers = containers;
            }

            public string Error { get { return _error; } }
            public int Position { get { return _pos; } }

            object Fail(string message, int position)
            {
                if (_error == null) { _error = message; _pos = position; }
                return null;
            }

            public object ValueOf(ExprAst n)
            {
                if (_error != null) return null;
                if (n == null) return Fail("表达式节点为空。", 0);
                switch (n.Kind)
                {
                    case "int": return n.IntValue;
                    case "real": return n.RealValue;
                    case "bool": return n.BoolValue;
                    case "string": return n.StrValue ?? "";
                    case "char": return n.CharValue;
                    case "var":
                        {
                            object v = _lookup == null ? null : _lookup(n.Text);
                            if (v == null)
                            {
                                // 没声明同名变量时，pi / e 按数学常量取值
                                if (MathFns.IsConstant(n.Text)) return MathFns.ConstantValue(n.Text);
                                return Fail("变量「" + n.Text + "」没有值。", n.Start);
                            }
                            return Normalize(v);
                        }
                    case "array":
                        {
                            var list = new List<object>();
                            foreach (var child in n.Children)
                            {
                                var item = ValueOf(child);
                                if (_error != null) return null;
                                list.Add(item);
                            }
                            return list;
                        }
                    case "index":
                        {
                            var target = ValueOf(n.Children[0]);
                            if (_error != null) return null;
                            var idxValue = ValueOf(n.Children[1]);
                            if (_error != null) return null;
                            long idx;
                            if (!TryLong(idxValue, out idx)) return Fail("数组下标必须是整数。", n.Children[1].Start);
                            // 字符串也按 C++ 的 s[i] 给一格字符：括号匹配这类题不用为「逐个字符看」再画数据线
                            var text = target as string;
                            if (text != null)
                            {
                                if (idx < 0 || idx >= text.Length)
                                    return Fail("数组下标越界：" + idx + "（长度 " + text.Length + "）。", n.Children[1].Start);
                                return text[(int)idx];
                            }
                            var arr = AsList(target);
                            if (arr == null) return Fail("这里不是数组，不能取下标。", n.Start);
                            if (idx < 0 || idx >= arr.Count)
                                return Fail("数组下标越界：" + idx + "（长度 " + arr.Count + "）。", n.Children[1].Start);
                            return Normalize(arr[(int)idx]);
                        }
                    case "member":
                        {
                            var cont = ContainerOf(n.Children[0]);
                            if (cont != null) return ContainerValue(cont, n);
                            // 字符串的 size / empty 与 std::string 同一个口径，类型检查那头已经放行
                            var text = ValueOf(n.Children[0]) as string;
                            if (text != null)
                            {
                                if (Members.IsCountToken(n.Text)) return (long)text.Length;
                                if (EmptyToken(n.Text)) return text.Length == 0;
                                return Fail(Members.NotSupportedMessage("string", n.Text), n.Start);
                            }
                            var arr = AsList(ValueOf(n.Children[0]));
                            if (_error != null) return null;
                            if (arr == null) return Fail("这里不是数组也不是容器，没有成员「" + n.Text + "」。", n.Start);
                            if (Members.IsCountToken(n.Text)) return (long)arr.Count;
                            if (EmptyToken(n.Text)) return arr.Count == 0;
                            return Fail("数组没有成员「" + n.Text + "」，只有 " + MemberNames(AnyArray) + "。", n.Start);
                        }
                    case "call":
                        {
                            if (n.Text != "len" && n.Text != "size") return MathFnValue(n);
                            if (n.Children.Count != 1)
                                return Fail("函数「" + n.Text + "」需要 1 个参数。", n.Start);
                            var cont = ContainerOf(n.Children[0]);
                            if (cont != null) return (long)(cont.Items == null ? 0 : cont.Items.Count);
                            var arr = AsList(ValueOf(n.Children[0]));
                            if (_error != null) return null;
                            if (arr == null) return Fail("函数「" + n.Text + "」的参数要数组或容器。", n.Children[0].Start);
                            return (long)arr.Count;
                        }
                    case "unary": return UnaryValue(n);
                    case "binary": return BinaryValue(n);
                }
                return Fail("未知的表达式节点「" + n.Kind + "」。", n.Start);
            }

            // 容器实例只在「直接写它的名字」时认得（st.empty / len(st)）：链式取元素那种写不出来，
            // 类型检查那一头也不会放行，所以这里不再递归求值。
            ExprContainer ContainerOf(ExprAst n)
            {
                if (_containers == null || n == null) return null;
                ExprContainer c;
                return n.Kind == "var" && _containers.TryGetValue(n.Text, out c) ? c : null;
            }

            // 容器成员：个数 / 空了看格子数，取元素时栈看最后压进去的那一格、队列看最前面那一格。
            // 空容器要像模块那样说一句人话，不能让玩家看见异常。
            object ContainerValue(ExprContainer c, ExprAst n)
            {
                if (Members.Find(c.Type, n.Text) == null)
                    return Fail(Members.NotSupportedMessage(c.Type, n.Text), n.Start);
                var items = c.Items;
                int count = items == null ? 0 : items.Count;
                if (Members.IsCountToken(n.Text)) return (long)count;
                if (EmptyToken(n.Text)) return count == 0;
                if (count == 0) return Fail(c.IsQueue ? "队列为空，无法查看队头。" : "栈为空，无法查看顶端。", n.Start);
                return Normalize(c.IsQueue ? items[0] : items[count - 1]);
            }

            static bool EmptyToken(string token)
            {
                return Array.IndexOf(Members.EmptyTokens, token) >= 0;
            }

            // 求值时拿不到数组声明的元素类型，报错只列数组通用的那两颗成员，按 int[] 查词表即可。
            const string AnyArray = "int[]";

            static string MemberNames(string type)
            {
                var names = new List<string>();
                foreach (var m in Members.Of(type)) names.Add(m.Token + "（" + m.Gloss + "）");
                return string.Join("、", names.ToArray());
            }

            // 数学函数在游戏内虚拟机里跑一遍：整数参数保持整数口径，混进小数才转 double
            object MathFnValue(ExprAst n)
            {
                if (MathFns.Find(n.Text) == null) return Fail(MathFns.UnknownFnMessage(n.Text), n.Start);
                var args = new List<object>();
                foreach (var c in n.Children)
                {
                    var v = ValueOf(c);
                    if (_error != null) return null;
                    v = Normalize(v);
                    // 这里不收 bool / char / 字符串：类型检查已经拦过一道，直接跑（单步、断点续跑）时兜住，
                    // 免得静默算出一个 nan 让玩家对不上答案
                    if (!(v is long || v is double)) return Fail("函数「" + n.Text + "」的参数要数值。", c.Start);
                    args.Add(v);
                }
                var r = MathFns.Run(n.Text, args);
                if (r == null) return Fail("函数「" + n.Text + "」的参数要数值。", n.Start);
                return r;
            }

            object UnaryValue(ExprAst n)
            {
                var v = ValueOf(n.Children[0]);
                if (_error != null) return null;
                if (n.Text == "!")
                {
                    bool b;
                    if (!TryBool(v, out b)) return Fail("逻辑取反需要 bool 值。", n.Start);
                    return !b;
                }
                if (n.Text == "~")
                {
                    if (!(v is long)) return Fail("按位取反「~」只支持整数。", n.Start);
                    return ~(long)v;
                }
                if (v is double) return -(double)v;
                if (v is long) return -(long)v;
                return Fail("一元负号需要数值。", n.Start);
            }

            object BinaryValue(ExprAst n)
            {
                string op = n.Text;
                if (op == "&&" || op == "||")
                {
                    bool a;
                    if (!TryBool(ValueOf(n.Children[0]), out a)) return Fail("逻辑运算「" + op + "」需要 bool 值。", n.Start);
                    if (op == "&&" && !a) return false;
                    if (op == "||" && a) return true;
                    bool b;
                    if (!TryBool(ValueOf(n.Children[1]), out b)) return Fail("逻辑运算「" + op + "」需要 bool 值。", n.Start);
                    return op == "&&" ? (a && b) : (a || b);
                }

                var left = ValueOf(n.Children[0]);
                if (_error != null) return null;
                var right = ValueOf(n.Children[1]);
                if (_error != null) return null;

                if (op == "==" || op == "!=")
                {
                    bool eq;
                    if (left is string || right is string) eq = AsText(left) == AsText(right);
                    else if (left is bool || right is bool) eq = left is bool && right is bool && (bool)left == (bool)right;
                    else eq = AsNumber(left) == AsNumber(right);
                    return op == "==" ? eq : !eq;
                }
                if (op == "<" || op == "<=" || op == ">" || op == ">=")
                {
                    int cmp;
                    if (left is string && right is string) cmp = string.CompareOrdinal((string)left, (string)right);
                    else if (left is char && right is char) cmp = ((char)left).CompareTo((char)right);
                    else cmp = AsNumber(left).CompareTo(AsNumber(right));
                    if (op == "<") return cmp < 0;
                    if (op == "<=") return cmp <= 0;
                    if (op == ">") return cmp > 0;
                    return cmp >= 0;
                }
                if (op == "+" && left is string && right is string) return (string)left + (string)right;

                if (IsBitop(op))
                {
                    // 位运算只走整数：小数在 C++ 里根本没有对应写法，提前拦下比生成错误代码好
                    if (!(left is long) || !(right is long))
                        return Fail("位运算「" + op + "」两侧都必须是整数。", n.Start);
                    long a = (long)left, b = (long)right;
                    if ((op == "<<" || op == ">>") && (b < 0 || b > 62))
                        return Fail("移位只能移 0 到 62 位，实际为 " + b + "。", n.Start);
                    switch (op)
                    {
                        case "&": return a & b;
                        case "|": return a | b;
                        case "^": return a ^ b;
                        case "<<": return a << (int)b;
                        default: return a >> (int)b;
                    }
                }

                bool numeric = (left is long || left is double) && (right is long || right is double);
                if (!numeric) return Fail("运算符「" + op + "」需要数值。", n.Start);

                bool useDouble = left is double || right is double;
                if (useDouble)
                {
                    double a = AsNumber(left), b = AsNumber(right);
                    switch (op)
                    {
                        case "+": return a + b;
                        case "-": return a - b;
                        case "*": return a * b;
                        case "/":
                            if (b == 0) return Fail("除数不能为 0。", n.Start);
                            return a / b;
                        case "%":
                            if (b == 0) return Fail("取余的除数不能为 0。", n.Start);
                            return a % b;
                    }
                }
                else
                {
                    long a = (long)left, b = (long)right;
                    switch (op)
                    {
                        case "+": return a + b;
                        case "-": return a - b;
                        case "*": return a * b;
                        case "/":
                            if (b == 0) return Fail("除数不能为 0。", n.Start);
                            return a / b;
                        case "%":
                            if (b == 0) return Fail("取余的除数不能为 0。", n.Start);
                            return a % b;
                    }
                }
                return Fail("未知运算符「" + op + "」。", n.Start);
            }
        }

        public static ExprEvalResult Eval(ExprAst node, ExprLookup lookup)
        {
            return Eval(node, lookup, null);
        }

        static ExprEvalResult Eval(ExprAst node, ExprLookup lookup, IDictionary<string, ExprContainer> containers)
        {
            if (node == null) return ExprEvalResult.Fail("没有可求值的表达式。", 0);
            var ev = new Evaluator(lookup, containers);
            var value = ev.ValueOf(node);
            if (ev.Error != null) return ExprEvalResult.Fail(ev.Error, ev.Position);
            return ExprEvalResult.Good(value);
        }

        public static ExprEvalResult Eval(ExprAst node, IDictionary<string, object> variables)
        {
            return Eval(node, name =>
            {
                object v;
                if (variables != null && variables.TryGetValue(name, out v)) return v;
                return null;
            });
        }

        public static ExprEvalResult Eval(ExprAst node, IDictionary<string, object> scalars, IDictionary<string, List<object>> arrays)
        {
            return Eval(node, scalars, arrays, null);
        }

        // containers：栈 / 队列实例的当前内容，成员访问（st.size / st.top）靠它算；没有容器时传 null。
        public static ExprEvalResult Eval(ExprAst node, IDictionary<string, object> scalars, IDictionary<string, List<object>> arrays,
                                          IDictionary<string, ExprContainer> containers)
        {
            return Eval(node, name =>
            {
                List<object> list;
                if (arrays != null && arrays.TryGetValue(name, out list)) return list;
                object v;
                if (scalars != null && scalars.TryGetValue(name, out v)) return v;
                return null;
            }, containers);
        }

        public static ExprEvalResult Eval(string source, IDictionary<string, object> variables)
        {
            var parsed = Parse(source);
            if (!parsed.Ok) return ExprEvalResult.Fail(parsed.Error, parsed.Position);
            return Eval(parsed.Root, variables);
        }

        static object Normalize(object v)
        {
            if (v == null) return null;
            if (v is int) return (long)(int)v;
            if (v is short) return (long)(short)v;
            if (v is byte) return (long)(byte)v;
            if (v is uint) return (long)(uint)v;
            if (v is float) return (double)(float)v;
            if (v is decimal) return (double)(decimal)v;
            if (v is List<object>) return v;
            if (v is string || v is bool || v is char || v is double || v is long) return v;
            if (v is IEnumerable && !(v is string))
            {
                var list = new List<object>();
                foreach (var item in (IEnumerable)v) list.Add(Normalize(item));
                return list;
            }
            return v;
        }

        static List<object> AsList(object v)
        {
            if (v == null) return null;
            var list = v as List<object>;
            if (list != null) return list;
            if (v is string) return null;
            var il = v as IList;
            if (il != null)
            {
                var copy = new List<object>(il.Count);
                for (int i = 0; i < il.Count; i++) copy.Add(Normalize(il[i]));
                return copy;
            }
            return null;
        }

        static bool TryLong(object v, out long result)
        {
            result = 0;
            if (v is long) { result = (long)v; return true; }
            if (v is double)
            {
                double d = (double)v;
                if (Math.Abs(d - Math.Round(d)) > 1e-9) return false;
                result = (long)Math.Round(d);
                return true;
            }
            return false;
        }

        static bool TryBool(object v, out bool result)
        {
            result = false;
            if (v is bool) { result = (bool)v; return true; }
            return false;
        }

        static double AsNumber(object v)
        {
            if (v is long) return (long)v;
            if (v is double) return (double)v;
            if (v is bool) return (bool)v ? 1 : 0;
            if (v is char) return (char)v;
            double d;
            if (v is string && double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }

        static string AsText(object v)
        {
            if (v == null) return "";
            if (v is string) return (string)v;
            if (v is char) return ((char)v).ToString();
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is double) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        // ==================== C++17 生成 ====================

        public static string Cpp(ExprAst node)
        {
            return Cpp(node, null);
        }

        public static string Cpp(ExprAst node, IDictionary<string, string> variableTypes)
        {
            if (node == null) return "0";
            switch (node.Kind)
            {
                case "int": return node.IntValue.ToString(CultureInfo.InvariantCulture) + "LL";
                case "real": return RealLiteral(node.RealValue);
                case "bool": return node.BoolValue ? "true" : "false";
                case "string": return "\"" + EscapeCpp(node.StrValue ?? "") + "\"";
                case "char": return "'" + EscapeCppChar(node.CharValue) + "'";
                case "var": return CppVarName(node.Text, variableTypes);
                case "unary": return "(" + node.Text + Cpp(node.Children[0], variableTypes) + ")";
                case "binary":
                    {
                        string a = Cpp(node.Children[0], variableTypes);
                        string b = Cpp(node.Children[1], variableTypes);
                        if (node.Text == "%") return "(" + a + " % " + b + ")";
                        if (node.Text == "/" && CppType(node, variableTypes) != "double")
                            return "((" + a + ") / (" + b + "))";
                        return "(" + a + " " + node.Text + " " + b + ")";
                    }
                case "index":
                    return "(" + Cpp(node.Children[0], variableTypes) + ")[" + Cpp(node.Children[1], variableTypes) + "]";
                case "member":
                    {
                        string mt = CppType(node.Children[0], variableTypes);
                        // 名字表里查不到的成员不能默默写成别的：出码只在类型检查放行之后跑，这里兜底给 false
                        if (Members.Find(mt, node.Text) == null) return "false";
                        string text = "(" + Cpp(node.Children[0], variableTypes) + ")." + Members.CppCall(mt, node.Text);
                        // size() 给的是无符号数，套一层 long long 才和虚拟机里的整数对得上
                        return Members.IsCountToken(node.Text) ? "(long long)" + text : text;
                    }
                case "call":
                    {
                        if (node.Text == "len" || node.Text == "size")
                            return "((long long)(" + Cpp(node.Children[0], variableTypes) + ").size())";
                        int count = node.Children.Count;
                        var args = new string[count];
                        var types = new string[count];
                        for (int i = 0; i < count; i++)
                        {
                            args[i] = Cpp(node.Children[i], variableTypes);
                            types[i] = CppType(node.Children[i], variableTypes);
                        }
                        return MathFns.CppCall(node.Text, args, types);
                    }
                case "array":
                    {
                        string elem = CppElementType(node, variableTypes);
                        var sb = new StringBuilder();
                        sb.Append("std::vector<").Append(elem).Append(">{");
                        for (int i = 0; i < node.Children.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(CppArrayItem(node.Children[i], elem, variableTypes));
                        }
                        sb.Append("}");
                        return sb.ToString();
                    }
            }
            return "0";
        }

        // 表达式里写到的名字：玩家自己声明过同名变量就照他的写，否则 pi / e 展成双精度字面量
        //（C++ 的 M_PI 不是标准宏，别指望它存在）。
        static string CppVarName(string name, IDictionary<string, string> variableTypes)
        {
            if (string.IsNullOrEmpty(name)) return "0";
            string t;
            if (variableTypes != null && variableTypes.TryGetValue(name, out t)) return name;
            return MathFns.IsConstant(name) ? MathFns.CppConstant(name) : name;
        }

        static string RealLiteral(double value)
        {
            string text = value.ToString("R", CultureInfo.InvariantCulture);
            if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0) text += ".0";
            return text;
        }

        static string CppArrayItem(ExprAst child, string elem, IDictionary<string, string> variableTypes)
        {
            string text = Cpp(child, variableTypes);
            if (child.Kind == "int" && (elem == "double" || elem == "long double")) return text.Replace("LL", ".0");
            return text;
        }

        static string CppElementType(ExprAst node, IDictionary<string, string> variableTypes)
        {
            string type = CppType(node, variableTypes);
            string elem = ElementType(type);
            switch (elem)
            {
                case "int": return "long long";
                case "double": return "double";
                case "bool": return "bool";
                case "string": return "std::string";
                case "char": return "char";
            }
            return "long long";
        }

        static string CppType(ExprAst node, IDictionary<string, string> variableTypes)
        {
            if (node == null) return "unknown";
            switch (node.Kind)
            {
                case "int": return "int";
                case "real": return "double";
                case "bool": return "bool";
                case "string": return "string";
                case "char": return "char";
                case "var":
                    {
                        string t;
                        if (variableTypes != null && variableTypes.TryGetValue(node.Text, out t)) return t;
                        return MathFns.IsConstant(node.Text) ? "double" : "unknown";
                    }
                case "array":
                    {
                        string elem = "unknown";
                        foreach (var child in node.Children)
                        {
                            string t = CppType(child, variableTypes);
                            if (elem == "unknown") elem = t;
                            else if (elem != t)
                            {
                                if (IsNumeric(elem) && IsNumeric(t)) elem = "double";
                                else return "unknown";
                            }
                        }
                        return elem == "unknown" ? "unknown[]" : elem + "[]";
                    }
                case "index":
                    return ElementType(CppType(node.Children[0], variableTypes));
                case "member":
                    {
                        string rt = Members.ReturnType(CppType(node.Children[0], variableTypes), node.Text);
                        return string.IsNullOrEmpty(rt) ? "int" : rt;
                    }
                case "call":
                    {
                        if (node.Text == "len" || node.Text == "size") return "int";
                        var ts = new List<string>();
                        foreach (var c in node.Children) ts.Add(CppType(c, variableTypes));
                        return MathFns.ReturnType(node.Text, ts);
                    }
                case "unary":
                    return node.Text == "!" ? "bool" : CppType(node.Children[0], variableTypes);
                case "binary":
                    {
                        if (node.Text == "&&" || node.Text == "||") return "bool";
                        if (IsBitop(node.Text)) return "int";
                        if (node.Text == "==" || node.Text == "!=" || node.Text == "<" || node.Text == "<=" ||
                            node.Text == ">" || node.Text == ">=") return "bool";
                        string a = CppType(node.Children[0], variableTypes);
                        string b = CppType(node.Children[1], variableTypes);
                        if (node.Text == "+" && a == "string" && b == "string") return "string";
                        if (IsNumeric(a) && IsNumeric(b)) return NumericResult(a, b);
                        return "unknown";
                    }
            }
            return "unknown";
        }

        static string EscapeCpp(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        static string EscapeCppChar(char c)
        {
            switch (c)
            {
                case '\\': return "\\\\";
                case '\'': return "\\'";
                case '\n': return "\\n";
                case '\r': return "\\r";
                case '\t': return "\\t";
                case '\0': return "\\0";
            }
            return c.ToString();
        }
    }
}
