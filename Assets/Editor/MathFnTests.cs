using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dayu.Core;
using UnityEditor;
using Debug = UnityEngine.Debug;
using Proc = System.Diagnostics.Process;
using ProcStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Dayu.EditorTools
{
    /// <summary>
    /// cmath 函数自测。三段口径必须一致：
    /// 类型检查（决定生成的变量类型）、游戏内虚拟机求值、C++ 代码生成，
    /// 最后再用一张把函数跑全的图做 VM 与 g++ 原生程序的逐行对拍。
    /// </summary>
    public static class MathFnTests
    {
        public static void Run(ref int total, ref int passed)
        {
            CheckCases(ref total, ref passed);
            EvalCases(ref total, ref passed);
            CppCases(ref total, ref passed);
            DisplayCases(ref total, ref passed);
            ParityCases(ref total, ref passed);
        }

        /// <summary>仅供单独验证本组用例（不影响 Cli.RunSelfTests 的接线）。</summary>
        public static void RunAlone()
        {
            int total = 0, passed = 0;
            Run(ref total, ref passed);
            Debug.Log("数学函数：共 " + total + " 项，通过 " + passed + " 项");
            if (total != passed) EditorApplication.Exit(1);
        }

        static Dictionary<string, string> Types()
        {
            return new Dictionary<string, string>
            {
                { "a", "int" }, { "b", "int" }, { "x", "double" },
                { "arr", "int[]" }, { "s", "string" }, { "f", "bool" }, { "c", "char" }
            };
        }

        // ---------------- 类型口径 ----------------

        static void CheckCases(ref int total, ref int passed)
        {
            var t = Types();

            // 整数进整数出的那一族（Follow）：参数是 int 就回 int，掺了小数才回 double
            CheckType(ref total, ref passed, t, "abs(a)", "int");
            CheckType(ref total, ref passed, t, "floor(a)", "int");
            CheckType(ref total, ref passed, t, "ceil(a)", "int");
            CheckType(ref total, ref passed, t, "trunc(a)", "int");
            CheckType(ref total, ref passed, t, "round(a)", "int");
            CheckType(ref total, ref passed, t, "min(a, b)", "int");
            CheckType(ref total, ref passed, t, "max(a, 3)", "int");
            CheckType(ref total, ref passed, t, "abs(x)", "double");
            CheckType(ref total, ref passed, t, "floor(x)", "double");
            CheckType(ref total, ref passed, t, "min(a, x)", "double");
            CheckType(ref total, ref passed, t, "max(x, a)", "double");
            CheckType(ref total, ref passed, t, "abs(abs(a))", "int");
            CheckType(ref total, ref passed, t, "min(max(a, b), 10)", "int");

            // 一律回 double 的那一族，整数参数也一样
            CheckType(ref total, ref passed, t, "sqrt(a)", "double");
            CheckType(ref total, ref passed, t, "pow(a, 2)", "double");
            CheckType(ref total, ref passed, t, "fmod(a, 2)", "double");
            CheckType(ref total, ref passed, t, "hypot(a, 12)", "double");
            CheckType(ref total, ref passed, t, "log(a)", "double");
            CheckType(ref total, ref passed, t, "sin(a) + cos(a)", "double");
            CheckType(ref total, ref passed, t, "atan2(a, b)", "double");

            // 常量：没被同名变量挡住时按 double 算
            CheckType(ref total, ref passed, t, "pi", "double");
            CheckType(ref total, ref passed, t, "abs(0 - pi)", "double");
            var shadow = Types();
            shadow["pi"] = "int";
            CheckType(ref total, ref passed, shadow, "pi", "int");

            // 报错口径：参数个数、参数类型、未知函数
            CheckFail(ref total, ref passed, t, "max(1, 2, 3)", "需要 2 个参数");
            CheckFail(ref total, ref passed, t, "sqrt()", "需要 1 个参数");
            CheckFail(ref total, ref passed, t, "abs(s)", "要数值");
            CheckFail(ref total, ref passed, t, "abs(f)", "要数值");
            CheckFail(ref total, ref passed, t, "abs(arr)", "要数值");
            CheckFail(ref total, ref passed, t, "foo(a)", "未知函数");
            CheckFail(ref total, ref passed, t, "log(a, b)", "需要 1 个参数");
        }

        static void CheckType(ref int total, ref int passed, Dictionary<string, string> types, string src, string expect)
        {
            var r = ExprLang.Check(src, types);
            Case(ref total, ref passed, "类型 " + src + " → " + expect,
                r.Ok && r.Type == expect, r.Ok ? r.Type : r.Error);
        }

        static void CheckFail(ref int total, ref int passed, Dictionary<string, string> types, string src, string keyword)
        {
            var r = ExprLang.Check(src, types);
            Case(ref total, ref passed, "类型 " + src + " 被拦下（" + keyword + "）",
                !r.Ok && r.Error != null && r.Error.Contains(keyword), r.Ok ? "意外通过：" + r.Type : r.Error);
        }

        // ---------------- 虚拟机求值 ----------------

        static void EvalCases(ref int total, ref int passed)
        {
            var vars = new Dictionary<string, object> { { "a", 5L }, { "b", 12L }, { "x", 2.5 } };

            EvalLong(ref total, ref passed, vars, "abs(0 - a)", 5L);
            EvalLong(ref total, ref passed, vars, "min(a, b)", 5L);
            EvalLong(ref total, ref passed, vars, "max(a, b)", 12L);
            EvalLong(ref total, ref passed, vars, "floor(a / 2)", 2L);   // 整数除先行：5/2=2
            EvalLong(ref total, ref passed, vars, "ceil(a / 2)", 2L);
            EvalDouble(ref total, ref passed, vars, "ceil(a / 2.0)", 3.0);
            EvalLong(ref total, ref passed, vars, "trunc(0 - a / 2)", -2L);
            EvalLong(ref total, ref passed, vars, "round(a)", 5L);
            EvalLong(ref total, ref passed, vars, "abs(abs(0 - a))", 5L);

            EvalDouble(ref total, ref passed, vars, "sqrt(a)", Math.Sqrt(5));
            EvalDouble(ref total, ref passed, vars, "pow(a, 2)", 25.0);
            EvalDouble(ref total, ref passed, vars, "fmod(a, 2)", 1.0);
            EvalDouble(ref total, ref passed, vars, "hypot(a, b)", 13.0);
            EvalDouble(ref total, ref passed, vars, "exp(0)", 1.0);
            EvalDouble(ref total, ref passed, vars, "log(exp(2))", 2.0);
            EvalDouble(ref total, ref passed, vars, "atan2(a, a)", Math.PI / 4);
            EvalDouble(ref total, ref passed, vars, "floor(x)", 2.0);
            EvalDouble(ref total, ref passed, vars, "round(2.5)", 3.0);   // C++ 的 round 也是零点半分
            EvalDouble(ref total, ref passed, vars, "round(-2.5)", -3.0);
            EvalDouble(ref total, ref passed, vars, "min(a, x)", 2.5);
            EvalDouble(ref total, ref passed, vars, "pi", Math.PI);
            EvalDouble(ref total, ref passed, vars, "e", Math.E);
            EvalDouble(ref total, ref passed, vars, "cbrt(27)", 3.0);
            EvalDouble(ref total, ref passed, vars, "fabs(0 - x)", 2.5);

            // 非数值参数：单步/断点续跑绕过类型检查时也要报错，不能静默算出一个 nan
            var text = new Dictionary<string, object> { { "s", "hi" } };
            var bad = ExprLang.Eval(ExprLang.Parse("abs(s)").Root, text);
            Case(ref total, ref passed, "求值 abs(字符串) 报错而不是算出 nan",
                !bad.Ok && bad.Error != null && bad.Error.Contains("要数值"), bad.Ok ? "意外成功：" + bad.Value : bad.Error);
        }

        static void EvalLong(ref int total, ref int passed, Dictionary<string, object> vars, string src, long expect)
        {
            var r = ExprLang.Eval(ExprLang.Parse(src).Root, vars);
            Case(ref total, ref passed, "求值 " + src + " = " + expect,
                r.Ok && r.Value is long && (long)r.Value == expect, r.Ok ? "" + r.Value : r.Error);
        }

        static void EvalDouble(ref int total, ref int passed, Dictionary<string, object> vars, string src, double expect)
        {
            var r = ExprLang.Eval(ExprLang.Parse(src).Root, vars);
            bool ok = r.Ok && r.Value is double && Math.Abs((double)r.Value - expect) < 1e-9;
            Case(ref total, ref passed, "求值 " + src + " = " + expect, ok, r.Ok ? "" + r.Value : r.Error);
        }

        // ---------------- C++ 生成 ----------------

        static void CppCases(ref int total, ref int passed)
        {
            var t = Types();

            CppHas(ref total, ref passed, t, "abs(0 - a)", "std::abs");
            // min / max 是模板函数，两个参数必须同型；字面量在 C++ 侧是 long long，变量是 int，
            // 不显式实例化会直接编译失败（deduced conflicting types）
            CppHas(ref total, ref passed, t, "min(a, b)", "std::min<long long>(a, b)");
            CppHas(ref total, ref passed, t, "max(x, a)", "std::max<double>");
            // C++ 的 floor / ceil / round / trunc 就算吃整数也吐小数，套一层整数转换才对得上我们的类型口径
            CppHas(ref total, ref passed, t, "floor(a)", "static_cast<long long>(std::floor(a))");
            CppHas(ref total, ref passed, t, "abs(a)", "static_cast<long long>(std::abs(a))");
            // 整数参数抬成 double，躲开 C++17 的整数重载（pow(long long, long long) 会退化成整数结果）
            CppHas(ref total, ref passed, t, "sqrt(a)", "std::sqrt(static_cast<double>(a))");
            CppHas(ref total, ref passed, t, "pow(a, 2)", "std::pow(static_cast<double>(a)");
            CppHas(ref total, ref passed, t, "hypot(a, b)", "std::hypot(static_cast<double>(a), static_cast<double>(b))");
            CppHas(ref total, ref passed, t, "sin(x)", "std::sin(x)");
            CppHas(ref total, ref passed, t, "pi", "3.141592653589793");
            CppHas(ref total, ref passed, t, "e", "2.718281828459045");
            // 玩家自己声明了同名变量，常量就让位给变量
            var shadow = Types();
            shadow["pi"] = "int";
            CppHas(ref total, ref passed, shadow, "pi", "pi");
            CppHas(ref total, ref passed, t, "len(arr) + abs(a)", ".size()");
            CppHas(ref total, ref passed, t, "abs(0 - a) / 2", "(2LL)");
        }

        static void CppHas(ref int total, ref int passed, Dictionary<string, string> types, string src, string expect)
        {
            string cpp = ExprLang.Cpp(ExprLang.Parse(src).Root, types);
            Case(ref total, ref passed, "C++ 生成 " + src + " 含「" + expect + "」",
                cpp.Contains(expect), cpp);
        }

        // ---------------- 小数打印口径 ----------------

        // C++ 的 cout 默认 6 位有效数字，VM 的输出要跟它对齐，否则对拍会因为格式差异判错
        static void DisplayCases(ref int total, ref int passed)
        {
            Display(ref total, ref passed, Math.Sqrt(2), "1.41421");
            Display(ref total, ref passed, 25.0, "25");
            Display(ref total, ref passed, 1.0 / 3.0, "0.333333");
            Display(ref total, ref passed, -0.95892427466313845, "-0.958924");
            Display(ref total, ref passed, Math.PI, "3.14159");
            Display(ref total, ref passed, 1e20, "1e+20");
            Display(ref total, ref passed, double.NaN, "nan");
            Display(ref total, ref passed, double.PositiveInfinity, "inf");
            Display(ref total, ref passed, double.NegativeInfinity, "-inf");
        }

        static void Display(ref int total, ref int passed, double value, string expect)
        {
            string got = Val.Double(value).ToDisplayString();
            Case(ref total, ref passed, "小数打印 " + value + " → " + expect, got == expect, got);
        }

        // ---------------- 与 g++ 对拍 ----------------

        // 一张图把两族函数都跑全：VM 与 g++ 原生程序逐行输出必须一模一样
        private static Graph MathGraph()
        {
            var g = G("MATHFN");
            g.ExprMode = true;
            var e = Node("e", "entry");
            var declA = Node("declA", "vardecl", "name", "a", "vtype", "int");
            var declZ = Node("declZ", "vardecl", "name", "z", "vtype", "double", "initExpr", "sqrt(a)");
            var in0 = Node("in0", "input", "vtype", "int", "name", "a");
            var o1 = Node("o1", "output", "outputExpr", "abs(0 - a) + min(a, 3) + max(a, 3)");
            var o2 = Node("o2", "output", "outputExpr", "sqrt(a)");
            var o3 = Node("o3", "output", "outputExpr", "pow(a, 2)");
            var o4 = Node("o4", "output", "outputExpr", "fmod(a, 2)");
            var o5 = Node("o5", "output", "outputExpr", "hypot(a, 12)");
            var o6 = Node("o6", "output", "outputExpr", "floor(a / 2)");
            var o7 = Node("o7", "output", "outputExpr", "ceil(a / 2.0)");
            var o8 = Node("o8", "output", "outputExpr", "round(a / 4.0)");
            var o9 = Node("o9", "output", "outputExpr", "log(a)");
            var o10 = Node("o10", "output", "outputExpr", "atan2(a, a)");
            var o11 = Node("o11", "output", "outputExpr", "sin(a)");
            var o12 = Node("o12", "output", "outputExpr", "pi");
            var o13 = Node("o13", "output", "outputExpr", "z + 1");
            var o14 = Node("o14", "output", "outputExpr", "trunc(0 - a / 2.0)");
            var o15 = Node("o15", "output", "outputExpr", "min(a, 2.5)");
            g.Nodes.AddRange(new[] { e, declA, in0, declZ, o1, o2, o3, o4, o5, o6, o7, o8, o9, o10, o11, o12, o13, o14, o15 });
            Exec(g, "e", "out", "declA");
            Exec(g, "declA", "out", "in0");
            Exec(g, "in0", "out", "declZ");
            Exec(g, "declZ", "out", "o1");
            Exec(g, "o1", "out", "o2");
            Exec(g, "o2", "out", "o3");
            Exec(g, "o3", "out", "o4");
            Exec(g, "o4", "out", "o5");
            Exec(g, "o5", "out", "o6");
            Exec(g, "o6", "out", "o7");
            Exec(g, "o7", "out", "o8");
            Exec(g, "o8", "out", "o9");
            Exec(g, "o9", "out", "o10");
            Exec(g, "o10", "out", "o11");
            Exec(g, "o11", "out", "o12");
            Exec(g, "o12", "out", "o13");
            Exec(g, "o13", "out", "o14");
            Exec(g, "o14", "out", "o15");
            return g;
        }

        // 输入 5 时的期望输出（手算，小数按 6 位有效数字）
        private const string Expect5 =
            "13\n2.23607\n25\n1\n13\n2\n3\n1\n1.60944\n0.785398\n-0.958924\n3.14159\n3.23607\n-2\n2.5";

        private static void ParityCases(ref int total, ref int passed)
        {
            var g = MathGraph();
            total++;
            var cr = Compiler.Compile(g);
            if (!cr.Ok)
            {
                Debug.Log("✘ 数学函数图 —— Core 编译失败：" + cr.Errors[0].Message);
                return;
            }
            passed++;
            Debug.Log("✔ 数学函数图 —— Core 编译通过");

            string exe = null;
            string cppError = null;
            string source = null;
            try
            {
                string root = Path.GetFullPath(Path.Combine("Temp", "mathfn"));
                Directory.CreateDirectory(root);
                string cpp = Path.Combine(root, "MATHFN.cpp");
                source = CppGen.Generate(cr.Program, "MATHFN");
                Case(ref total, ref passed, "生成的 C++ 打开 boolalpha（布尔要打成 true/false，跟 VM 一致）",
                    source != null && source.Contains("cout << boolalpha"), Show(source ?? ""));
                File.WriteAllText(cpp, source, Encoding.UTF8);
                string path = Path.Combine(root, "MATHFN.exe");
                string o, er;
                if (RunProcess(Gpp(), "-std=c++17 -O0 -o " + Quote(path) + " " + Quote(cpp), null, out o, out er)) exe = path;
                else cppError = "g++ " + First(er, o);
            }
            catch (Exception ex)
            {
                cppError = "生成或编译 C++ 失败：" + ex.Message;
            }

            foreach (var input in new[] { "5", "9" })
            {
                total++;
                var run = Vm.Run(cr.Program, input);
                string vm = run.Ok ? Normalize(run.Output) : null;
                if (!run.Ok) Debug.Log("✘ 数学函数 [VM] 输入「" + input + "」—— " + run.Error.Message);
                else if (vm != Expect5 && input == "5") Debug.Log("✘ 数学函数 [VM] 输入「" + input + "」—— 得到「" + Show(vm) + "」，期望「" + Show(Expect5) + "」");
                else { passed++; Debug.Log("✔ 数学函数 [VM] 输入「" + input + "」—— " + Show(vm)); }

                total++;
                if (exe == null) { Debug.Log("✘ 数学函数 [C++] 输入「" + input + "」—— " + cppError + "｜源码：" + Show(source ?? "")); continue; }
                string no, ne;
                if (!RunProcess(exe, "", input, out no, out ne)) { Debug.Log("✘ 数学函数 [C++] 输入「" + input + "」—— " + First(ne, no)); continue; }
                string native = Normalize(no);
                bool same = native == vm;
                if (input == "5") same = same && native == Expect5;
                if (same) { passed++; Debug.Log("✔ 数学函数 [C++] 输入「" + input + "」—— 与 VM 逐行相同"); }
                else Debug.Log("✘ 数学函数 [C++] 输入「" + input + "」—— 原生「" + Show(native) + "」VM「" + Show(vm) + "」");
            }
        }

        // ---------------- 通用 ----------------

        private static void Case(ref int total, ref int passed, string name, bool ok, string detail)
        {
            total++;
            if (ok) { passed++; Debug.Log("✔ 数学函数 " + name + " —— " + detail); }
            else Debug.Log("✘ 数学函数 " + name + " —— " + detail);
        }

        private static Graph G(string id) { return new Graph { LevelId = id }; }

        private static GraphNode Node(string uid, string type, params string[] ps)
        {
            var n = new GraphNode { Uid = uid, Type = type };
            for (int i = 0; i + 1 < ps.Length; i += 2) n.Params[ps[i]] = ps[i + 1];
            return n;
        }

        private static void Exec(Graph g, string from, string port, string to)
        {
            g.Links.Add(new GraphLink { FromNode = from, FromPort = port, ToNode = to, ToPort = "in", Kind = LinkKind.Exec });
        }

        private static string Gpp()
        {
            const string local = "C:/Users/24585/Downloads/w64devkit/bin/g++.exe";
            return File.Exists(local) ? local : "g++";
        }

        private static bool RunProcess(string fileName, string args, string input, out string output, out string error)
        {
            output = "";
            error = "";
            try
            {
                var si = new ProcStartInfo { FileName = fileName, Arguments = args, UseShellExecute = false, RedirectStandardInput = input != null, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var p = new Proc { StartInfo = si })
                {
                    if (!p.Start()) { error = "无法启动外部进程"; return false; }
                    if (input != null) { p.StandardInput.Write(input); p.StandardInput.Close(); }
                    output = p.StandardOutput.ReadToEnd();
                    error = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } error = "外部进程超时"; return false; }
                    if (p.ExitCode != 0) { if (string.IsNullOrEmpty(error)) error = "退出码 " + p.ExitCode; return false; }
                    return true;
                }
            }
            catch (Exception ex) { error = "无法启动外部进程：" + ex.Message; return false; }
        }

        private static string Show(string text) { return string.IsNullOrEmpty(text) ? "（空）" : text.Replace("\r\n", "\n").Replace("\n", " | "); }

        private static string First(string a, string b) { return string.IsNullOrEmpty(a) ? (string.IsNullOrEmpty(b) ? "未知错误" : b) : a; }

        private static string Quote(string path) { return "\"" + path.Replace("\"", "\\\"") + "\""; }

        private static string Normalize(string text) { return (text ?? "").Replace("\r\n", "\n").Trim(); }
    }
}
