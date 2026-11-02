using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dayu.Core
{
    /// <summary>
    /// 表达式语言里的数学函数与常量（对着 C++ 的 &lt;cmath&gt; 一套实装）。
    /// 一张表喂三处：类型检查、游戏内虚拟机求值、C++ 代码生成，
    /// 这样「大肥鱼里跑出来的数」和「g++ 对拍跑出来的数」才是同一套口径。
    /// 返回类型跟着 C++ 走：整数进、整数出的（abs / floor / ceil / round / trunc / min / max）标 Follow，
    /// 其余一律 double（C++ 的 pow、fmod、hypot 就算参数是整数也回小数）。
    /// </summary>
    public static class MathFns
    {
        public enum Ret { Double, Follow }

        public sealed class Def
        {
            public string Name;
            public int Arity;
            public Ret Ret;
            public Func<object[], object> Run;
        }

        static readonly Dictionary<string, Def> Table = Build();

        static Dictionary<string, Def> Build()
        {
            var d = new Dictionary<string, Def>(StringComparer.Ordinal);
            Add(d, F1("abs", x => Math.Abs(x), x => Math.Abs(x)));
            Add(d, G1("fabs", x => Math.Abs(x)));
            Add(d, G1("sqrt", x => Math.Sqrt(x)));
            Add(d, G1("cbrt", x => Math.Pow(Math.Abs(x), 1.0 / 3.0) * Math.Sign(x)));
            Add(d, G1("exp", x => Math.Exp(x)));
            Add(d, G1("exp2", x => Math.Pow(2, x)));
            Add(d, G1("expm1", x => Math.Exp(x) - 1.0));
            Add(d, G1("log", x => Math.Log(x)));
            Add(d, G1("log2", x => Math.Log(x, 2)));
            Add(d, G1("log10", x => Math.Log10(x)));
            Add(d, G1("log1p", x => Math.Log(1.0 + x)));
            Add(d, G1("sin", x => Math.Sin(x)));
            Add(d, G1("cos", x => Math.Cos(x)));
            Add(d, G1("tan", x => Math.Tan(x)));
            Add(d, G1("asin", x => Math.Asin(x)));
            Add(d, G1("acos", x => Math.Acos(x)));
            Add(d, G1("atan", x => Math.Atan(x)));
            Add(d, G1("sinh", x => Math.Sinh(x)));
            Add(d, G1("cosh", x => Math.Cosh(x)));
            Add(d, G1("tanh", x => Math.Tanh(x)));
            Add(d, G1("asinh", x => Math.Log(x + Math.Sqrt(x * x + 1))));
            Add(d, G1("acosh", x => Math.Log(x + Math.Sqrt(x * x - 1))));
            Add(d, G1("atanh", x => 0.5 * Math.Log((1 + x) / (1 - x))));
            Add(d, F1("floor", x => Math.Floor(x), x => x));
            Add(d, F1("ceil", x => Math.Ceiling(x), x => x));
            Add(d, F1("trunc", x => Math.Truncate(x), x => x));
            // C++ 的 std::round 是「零点半分」，.NET 默认也是 AwayFromZero，口径一致
            Add(d, F1("round", x => Math.Round(x, MidpointRounding.AwayFromZero), x => x));
            Add(d, G2("pow", (a, b) => Math.Pow(a, b)));
            Add(d, G2("atan2", (a, b) => Math.Atan2(a, b)));
            Add(d, G2("fmod", (a, b) => a - Math.Truncate(a / b) * b));
            Add(d, G2("hypot", (a, b) => Math.Sqrt(a * a + b * b)));
            Add(d, F2("min", (a, b) => Math.Min(a, b), (a, b) => Math.Min(a, b)));
            Add(d, F2("max", (a, b) => Math.Max(a, b), (a, b) => Math.Max(a, b)));
            return d;
        }

        static void Add(Dictionary<string, Def> d, Def def) { d[def.Name] = def; }

        static Def G1(string name, Func<double, double> f)
        {
            return new Def { Name = name, Arity = 1, Ret = Ret.Double, Run = a => f(AsDouble(a[0])) };
        }

        static Def G2(string name, Func<double, double, double> f)
        {
            return new Def { Name = name, Arity = 2, Ret = Ret.Double, Run = a => f(AsDouble(a[0]), AsDouble(a[1])) };
        }

        // 三目写法在这里是个坑：两个分支一个 long 一个 double，C# 会先把 long 提升成 double，
        // 整数口径的函数（abs / floor / min…）于是永远返回小数。必须分开 return。
        static Def F1(string name, Func<double, double> onReal, Func<long, long> onInt)
        {
            return new Def
            {
                Name = name,
                Arity = 1,
                Ret = Ret.Follow,
                Run = a =>
                {
                    if (a.Length == 1 && a[0] is long) return onInt((long)a[0]);
                    return onReal(AsDouble(a[0]));
                }
            };
        }

        static Def F2(string name, Func<double, double, double> onReal, Func<long, long, long> onInt)
        {
            return new Def
            {
                Name = name,
                Arity = 2,
                Ret = Ret.Follow,
                Run = a =>
                {
                    if (a.Length == 2 && a[0] is long && a[1] is long) return onInt((long)a[0], (long)a[1]);
                    return onReal(AsDouble(a[0]), AsDouble(a[1]));
                }
            };
        }

        static double AsDouble(object v)
        {
            if (v is double) return (double)v;
            if (v is long) return (long)v;
            if (v is int) return (int)v;
            if (v is char) return (char)v;
            if (v is bool) return (bool)v ? 1 : 0;
            return double.NaN;
        }

        public static Def Find(string name)
        {
            Def d;
            return name != null && Table.TryGetValue(name, out d) ? d : null;
        }

        public static bool IsMathFn(string name) { return Find(name) != null; }

        // 元素栏「数学函数」那一组芯片：abs / sqrt 这些高频的排在前面，玩家不用翻
        public static readonly string[] PaletteNames =
        {
            "abs", "sqrt", "pow", "min", "max", "floor", "ceil", "round", "fmod",
            "hypot", "log", "log2", "log10", "exp", "sin", "cos", "tan", "atan2",
            "cbrt", "fabs", "exp2", "expm1", "log1p", "asin", "acos", "atan",
            "sinh", "cosh", "tanh", "asinh", "acosh", "atanh",
        };

        // 常量芯片：写死 pi / e 不如点一下，避免玩家记不住小数
        public static readonly string[] PaletteConsts = { "pi", "e" };

        // ==================== 常量 ====================

        static readonly Dictionary<string, double> Consts = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            { "pi", Math.PI },
            { "e", Math.E },
        };

        public static bool IsConstant(string name) { return name != null && Consts.ContainsKey(name); }

        public static double ConstantValue(string name)
        {
            double v;
            return name != null && Consts.TryGetValue(name, out v) ? v : double.NaN;
        }

        // ==================== 求值 ====================

        /// <summary>虚拟机里跑一次：参数已经求成 long / double，回 long（整数口径）或 double。算不了回 null。</summary>
        public static object Run(string name, IList<object> args)
        {
            var d = Find(name);
            if (d == null || args == null || args.Count != d.Arity) return null;
            var a = new object[d.Arity];
            for (int i = 0; i < d.Arity; i++)
            {
                a[i] = args[i];
                if (!(a[i] is long || a[i] is double)) return null;
            }
            try { return d.Run(a); }
            catch { return null; }
        }

        /// <summary>类型口径：跟 C++ 一致，整数进整数出的标 int，其余 double。</summary>
        public static string ReturnType(string name, IList<string> argTypes)
        {
            var d = Find(name);
            if (d == null || argTypes == null) return "unknown";
            if (d.Ret == Ret.Double) return "double";
            foreach (var t in argTypes)
                if (t == "double") return "double";
            return "int";
        }

        // ==================== C++ 生成 ====================

        /// <summary>
        /// 写出 C++ 调用。args 与 types 一一对应（types 用表达式语言的类型名）。
        /// 一律回 double 的那几个（sqrt / pow / fmod / hypot / atan2 / 三角…）先把整数参数抬成 double：
        /// C++ 对这些函数还留着整数重载，pow(long long, long long) 在 C++17 会退化成整数结果，
        /// 跟我们的类型口径（double）不是一个数，对拍必翻车。
        /// min / max 是模板函数，两个参数必须同型：显式实例化，别让 (int, long long) 这种混合把推导卡死。
        /// 整数口径的那族还要整体套一层 static_cast&lt;long long&gt;：C++ 的 floor / ceil / round / trunc
        /// 就算参数是整数也回小数，跟我们的 int 口径差一个小数点，打印和下标运算都会对错。
        /// </summary>
        public static string CppCall(string name, string[] args, string[] types)
        {
            var d = Find(name);
            if (d == null || args == null || args.Length == 0) return "0";
            bool anyDouble = false;
            if (types != null)
                for (int i = 0; i < types.Length && i < args.Length; i++)
                    if (types[i] == "double") anyDouble = true;
            var sb = new System.Text.StringBuilder();
            if (d.Ret == Ret.Follow && !anyDouble) sb.Append("static_cast<long long>(");
            sb.Append("std::").Append(name);
            if (d.Ret == Ret.Follow && d.Arity == 2) sb.Append(anyDouble ? "<double>" : "<long long>");
            sb.Append('(');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(NeedsDoubleCast(d, anyDouble, ArgType(types, i)) ? "static_cast<double>(" + args[i] + ")" : args[i]);
            }
            sb.Append(')');
            if (d.Ret == Ret.Follow && !anyDouble) sb.Append(')');
            return sb.ToString();
        }

        static string ArgType(string[] types, int i)
        {
            return types != null && i < types.Length ? types[i] : "unknown";
        }

        // 结果注定是 double 的函数，整数参数一律抬成 double；参数里本来就有小数时不用管（隐式提升即可）
        static bool NeedsDoubleCast(Def d, bool anyDouble, string argType)
        {
            if (d.Ret != Ret.Double || anyDouble) return false;
            return argType == "int" || argType == "bool" || argType == "char";
        }

        /// <summary>常量写进 C++ 源码：用双精度字面量，不依赖 M_PI 这种非标准宏。</summary>
        public static string CppConstant(string name)
        {
            double v = ConstantValue(name);
            if (double.IsNaN(v)) return "0.0";
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        // 报错文案里的函数清单：太长会撑爆浮窗提示条，所以只列代表性的，完整清单在浮窗左侧元素栏
        public static string UnknownFnMessage(string name)
        {
            return "未知函数「" + (name ?? "") + "」。可用的有 abs、sqrt、pow、min、max、floor、ceil、"
                 + "round、fmod、log、exp、sin、cos、tan 等，完整清单见浮窗左侧「数学函数」；数组长度用 len、size。";
        }
    }
}
