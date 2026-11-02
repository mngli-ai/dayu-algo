using System.Collections.Generic;

namespace Dayu.Core
{
    // 成员访问的唯一词表：表达式校验、自动补全浮窗、C++ 出码三条链都从这里取，
    // 免得模块栏的卡名、浮窗里的列表和真能跑通的成员各写一套。
    // 成员名一律用 C++ 的原名（size / empty / top / front）：这游戏只在电脑端玩，
    // 打英文比打中文省事，玩家以后回洛谷看别人的题解也不用重新认一套名字。
    // 中文只做浮窗里的释义，玩家一次中文都不用打。
    public sealed class MemberDef
    {
        public readonly string Token;   // 玩家按 Tab / 回车插进表达式的那几个字母
        public readonly string Gloss;   // 卡面上叫什么的中文释义
        public readonly string Returns; // 表达式语言里的类型：int / bool / 或元素的类型（要按容器补）

        public MemberDef(string token, string gloss, string returns)
        { Token = token; Gloss = gloss; Returns = returns; }
    }

    public static class Members
    {
        // 类型表里容器实例的写法：数组沿用 "int[]"，栈 / 队列各带一个元素类型。
        public const string StackPrefix = "stack<";
        public const string QueuePrefix = "queue<";

        public static string StackType(string elem) { return StackPrefix + Elem(elem) + ">"; }
        public static string QueueType(string elem) { return QueuePrefix + Elem(elem) + ">"; }

        // 结构实例建档（Graph.Structs 那一颗）→ 类型表里的类型名，四个登记处共用一份口径。
        // 元素类型要先折算成表达式语言认的名字：图里写的 ll / long 在表达式里都按 int 算，
        // 不然 st.top 查出来的类型对不上号。
        public static string TypeOf(string kind, string elem)
        {
            return kind == "queue" ? QueueType(ExprElem(elem)) : StackType(ExprElem(elem));
        }

        static string ExprElem(string e) { return string.IsNullOrEmpty(e) || e == "ll" || e == "long" ? "int" : e; }

        public static bool IsStack(string t) { return StartsWith(t, StackPrefix); }
        public static bool IsQueue(string t) { return StartsWith(t, QueuePrefix); }
        public static bool IsContainer(string t) { return IsStack(t) || IsQueue(t); }

        // stack<int> / queue<int> → int；数组交给 ExprLang.ElementType；标量给空串
        public static string ElementOf(string t)
        {
            if (IsContainer(t)) return t.Substring(6, t.Length - 7);
            return ExprLang.IsArrayType(t) ? ExprLang.ElementType(t) : "";
        }

        static bool StartsWith(string t, string prefix)
        { return !string.IsNullOrEmpty(t) && t.StartsWith(prefix) && t.EndsWith(">"); }

        static string Elem(string t) { return string.IsNullOrEmpty(t) ? "int" : t; }

        // 「个数」「空了」在数组 / 栈 / 队列里都叫同一个名字，只有取元素那颗按结构分开
        //（栈看到的是顶、队列看到的是头，本来就是两个位置）。
        static readonly MemberDef Size = new MemberDef("size", "个数", "int");
        static readonly MemberDef Empty = new MemberDef("empty", "空了", "bool");
        static readonly MemberDef Top = new MemberDef("top", "栈顶元素", "");
        static readonly MemberDef Front = new MemberDef("front", "队头元素", "");

        // 老关卡的图和自测里写过 a.length，照旧认，但浮窗不推荐：推荐玩家统一用 size。
        public static readonly string LegacyAliases = "length";

        public static List<MemberDef> Of(string type)
        {
            var list = new List<MemberDef>();
            // 字符串也照 C++ 的口径发 size / empty（std::string 两个都有）：关卡里要按 s.size 数括号，
            // 为玩家另发明一套「字符串没有成员」的规矩只会逼回去画数据线。
            if (ExprLang.IsArrayType(type) || IsContainer(type) || type == "string")
            {
                list.Add(Size);
                list.Add(Empty);
            }
            if (IsStack(type)) list.Add(WithElement(Top, type));
            if (IsQueue(type)) list.Add(WithElement(Front, type));
            return list;
        }

        // 补上「有没有这颗成员」：浮窗按 token 查，类型检查按 token 查，两处同一个答案。
        public static MemberDef Find(string type, string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            // length 只在数组和字符串上等价于 size：栈顶、队头都没有 length 这种叫法
            if (token == LegacyAliases && (ExprLang.IsArrayType(type) || type == "string")) token = Size.Token;
            foreach (var m in Of(type))
                if (m.Token == token) return m;
            return null;
        }

        // 这颗成员在这类容器里返回什么类型：个数是 int，空了是 bool，取元素是元素类型。
        public static string ReturnType(string type, string token)
        {
            var m = Find(type, token);
            if (m == null) return null;
            return string.IsNullOrEmpty(m.Returns) ? ElementOf(type) : m.Returns;
        }

        static MemberDef WithElement(MemberDef m, string type)
        {
            return new MemberDef(m.Token, m.Gloss, ElementOf(type));
        }

        // 出码用的小写名字表：浮窗、类型检查、C++ 生成都从这里拿 token，别再各写一份字符串。
        public static readonly string[] CountTokens = { "size", "length" };
        public static readonly string[] EmptyTokens = { "empty" };

        public static bool IsCountToken(string token)
        { foreach (var t in CountTokens) if (t == token) return true; return false; }

        // C++ 那边栈是 vector，top 取的是最后一格，所以出码要写成 back()；队列是 deque，front 原名就能用。
        public static string CppCall(string type, string token)
        {
            if (IsCountToken(token)) return "size()";
            if (token == "empty") return "empty()";
            if (token == "top") return "back()";
            if (token == "front") return "front()";
            return null;
        }

        public static string NotSupportedMessage(string type, string token)
        {
            var list = Of(type);
            if (list.Count == 0) return "类型 " + type + " 不是数组或容器，没有成员「" + token + "」。";
            var names = new List<string>();
            foreach (var m in list) names.Add(m.Token + "（" + m.Gloss + "）");
            return "「" + type + "」没有成员「" + token + "」，只有 " + string.Join("、", names.ToArray()) + "。";
        }
    }
}
