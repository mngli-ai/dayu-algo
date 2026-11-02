using System;
using System.Collections.Generic;
using Dayu.Core;
using UnityEngine;

namespace Dayu.EditorTools
{
    // ExprLang 纯 C# 核心测试：词法/语法/类型检查/求值/C++ 生成。
    // 独立于 Cli.cs，由主流程后续接线调用 ExprLangTests.Run(ref total, ref passed)。
    public static class ExprLangTests
    {
        // 供 Unity 批处理 -executeMethod 直接调用：Dayu.EditorTools.ExprLangTests.RunAll
        public static void RunAll()
        {
            int total = 0, passed = 0;
            Run(ref total, ref passed);
            Debug.Log("ExprLang 共 " + total + " 项，通过 " + passed + " 项");
            if (total != passed) UnityEditor.EditorApplication.Exit(1);
        }

        public static void Run(ref int total, ref int passed)
        {
            ParseCases(ref total, ref passed);
            CheckCases(ref total, ref passed);
            EvalCases(ref total, ref passed);
            BitCases(ref total, ref passed);
            CppCases(ref total, ref passed);
        }

        // ---------------- 解析 ----------------

        static void ParseCases(ref int total, ref int passed)
        {
            var ok = ExprLang.Parse("(a + b) * 2");
            Case(ref total, ref passed, "解析 (a + b) * 2",
                ok.Ok && ok.Root != null && ok.Root.Kind == "binary" && ok.Root.Text == "*" && ok.Root.Children.Count == 2,
                ok.Ok ? "根节点 " + ok.Root : ok.Error);

            var err = ExprLang.Parse("1 +");
            Case(ref total, ref passed, "解析错误带位置与中文消息",
                !err.Ok && err.Position >= 0 && !string.IsNullOrEmpty(err.Error) && err.Error.Contains("第"),
                err.Ok ? "意外解析成功" : err.Error);

            var bad = ExprLang.Parse("1 @ 2");
            Case(ref total, ref passed, "非法字符报错",
                !bad.Ok && bad.Error.Contains("无法识别"),
                bad.Ok ? "意外解析成功" : bad.Error);

            var arr = ExprLang.Parse("[1, 2, 3][1]");
            Case(ref total, ref passed, "解析数组字面量下标",
                arr.Ok && arr.Root.Kind == "index" && arr.Root.Children[0].Kind == "array" && arr.Root.Children[0].Children.Count == 3,
                arr.Ok ? "根节点 " + arr.Root : arr.Error);

            var pos = ExprLang.Parse("len(arr) > 0");
            Case(ref total, ref passed, "AST 带起止位置",
                pos.Ok && pos.Root.Start == 0 && pos.Root.End == "len(arr) > 0".Length,
                pos.Ok ? "[" + pos.Root.Start + "," + pos.Root.End + ")" : pos.Error);
        }

        // ---------------- 类型检查 ----------------

        static Dictionary<string, string> Types()
        {
            return new Dictionary<string, string>
            {
                { "a", "int" }, { "b", "int" }, { "x", "double" }, { "i", "int" },
                { "arr", "int[]" }, { "f", "bool" }, { "s", "string" }, { "c", "char" },
                { "names", "string[]" }, { "vals", "double[]" }
            };
        }

        static void CheckCases(ref int total, ref int passed)
        {
            var t = Types();

            var ok = ExprLang.Check("(a + b) * 2", t);
            Case(ref total, ref passed, "类型检查 (a + b) * 2 → int",
                ok.Ok && ok.Type == "int", ok.Ok ? ok.Type : ok.Error);

            var dbl = ExprLang.Check("a + x", t);
            Case(ref total, ref passed, "类型检查 int + double → double",
                dbl.Ok && dbl.Type == "double", dbl.Ok ? dbl.Type : dbl.Error);

            var cmp = ExprLang.Check("len(arr) > 0", t);
            Case(ref total, ref passed, "类型检查 len(arr) > 0 → bool",
                cmp.Ok && cmp.Type == "bool", cmp.Ok ? cmp.Type : cmp.Error);

            var undef = ExprLang.Check("a + missing", t);
            Case(ref total, ref passed, "未定义变量报错",
                !undef.Ok && undef.Error.Contains("未定义的变量"), undef.Ok ? "意外通过" : undef.Error);

            var idx = ExprLang.Check("arr[x]", t);
            Case(ref total, ref passed, "数组下标非整数报错",
                !idx.Ok && idx.Error.Contains("下标"), idx.Ok ? "意外通过" : idx.Error);

            var notArr = ExprLang.Check("a[0]", t);
            Case(ref total, ref passed, "非数组取下标报错",
                !notArr.Ok && notArr.Error.Contains("不是数组"), notArr.Ok ? "意外通过" : notArr.Error);

            var badOp = ExprLang.Check("a + s", t);
            Case(ref total, ref passed, "运算符类型不匹配报错",
                !badOp.Ok && badOp.Error.Contains("不匹配"), badOp.Ok ? "意外通过" : badOp.Error);

            var outArr = ExprLang.Check("[1, 2, 3]", t);
            Case(ref total, ref passed, "输出数组报错",
                !outArr.Ok && outArr.Error.Contains("不能是数组"), outArr.Ok ? "意外通过" : outArr.Error);

            var mix = ExprLang.Check("[1, \"x\"]", t);
            Case(ref total, ref passed, "数组元素类型不一致报错",
                !mix.Ok && mix.Error.Contains("不一致"), mix.Ok ? "意外通过" : mix.Error);

            var member = ExprLang.Check("arr.length", t);
            Case(ref total, ref passed, "类型检查 arr.length → int",
                member.Ok && member.Type == "int", member.Ok ? member.Type : member.Error);

            var empty = ExprLang.Check("arr.empty", t);
            Case(ref total, ref passed, "类型检查 arr.empty → bool",
                empty.Ok && empty.Type == "bool", empty.Ok ? empty.Type : empty.Error);

            var logic = ExprLang.Check("f && !arr.empty", t);
            Case(ref total, ref passed, "类型检查逻辑运算 → bool",
                logic.Ok && logic.Type == "bool", logic.Ok ? logic.Type : logic.Error);

            var lit = ExprLang.Check("names[0]", t);
            Case(ref total, ref passed, "字符串数组取下标 → string",
                lit.Ok && lit.Type == "string", lit.Ok ? lit.Type : lit.Error);

            var conv = ExprLang.Check("[1, 2.5][0]", t);
            Case(ref total, ref passed, "混合数字数组 → double[]",
                conv.Ok && conv.Type == "double", conv.Ok ? conv.Type : conv.Error);

            var charCmp = ExprLang.Check("c == 'a'", t);
            Case(ref total, ref passed, "类型检查 char 比较 → bool",
                charCmp.Ok && charCmp.Type == "bool", charCmp.Ok ? charCmp.Type : charCmp.Error);
        }

        // ---------------- 求值 ----------------

        static void EvalCases(ref int total, ref int passed)
        {
            var vars = new Dictionary<string, object> { { "a", 1 }, { "b", 2 }, { "i", 1 }, { "f", false }, { "s", "hi" }, { "c", 'x' } };
            var arrays = new Dictionary<string, List<object>>
            {
                { "arr", new List<object> { 10L, 20L, 30L } },
                { "none", new List<object>() }
            };
            var empty = new Dictionary<string, object>();

            var r1 = ExprLang.Eval(ExprLang.Parse("(a + b) * 2").Root, vars);
            Case(ref total, ref passed, "求值 (a + b) * 2 = 6",
                r1.Ok && Convert.ToInt64(r1.Value) == 6L, r1.Ok ? r1.Value.ToString() : r1.Error);

            var r2 = ExprLang.Eval(ExprLang.Parse("arr[i] + 1").Root, vars, arrays);
            Case(ref total, ref passed, "求值 arr[i] + 1 = 21",
                r2.Ok && Convert.ToInt64(r2.Value) == 21L, r2.Ok ? r2.Value.ToString() : r2.Error);

            var arrA = new Dictionary<string, List<object>> { { "a", new List<object> { 4L, 5L, 6L } } };
            var onlyI = new Dictionary<string, object> { { "i", 2 } };
            var r2b = ExprLang.Eval(ExprLang.Parse("a[i] + 1").Root, onlyI, arrA);
            Case(ref total, ref passed, "求值 a[i] + 1 = 7（数组 a）",
                r2b.Ok && Convert.ToInt64(r2b.Value) == 7L, r2b.Ok ? r2b.Value.ToString() : r2b.Error);

            var r3 = ExprLang.Eval(ExprLang.Parse("len(arr) > 0").Root, vars, arrays);
            Case(ref total, ref passed, "求值 len(arr) > 0 = true",
                r3.Ok && r3.Value is bool && (bool)r3.Value, r3.Ok ? r3.Value.ToString() : r3.Error);

            var r4 = ExprLang.Eval(ExprLang.Parse("none.empty").Root, vars, arrays);
            Case(ref total, ref passed, "求值 空数组 .empty = true",
                r4.Ok && r4.Value is bool && (bool)r4.Value, r4.Ok ? r4.Value.ToString() : r4.Error);

            var r5 = ExprLang.Eval(ExprLang.Parse("[1,2,3][1]").Root, empty);
            Case(ref total, ref passed, "求值 [1,2,3][1] = 2",
                r5.Ok && Convert.ToInt64(r5.Value) == 2L, r5.Ok ? r5.Value.ToString() : r5.Error);

            var r6 = ExprLang.Eval(ExprLang.Parse("!f && true").Root, vars);
            Case(ref total, ref passed, "求值 !false && true = true",
                r6.Ok && r6.Value is bool && (bool)r6.Value, r6.Ok ? r6.Value.ToString() : r6.Error);

            var r7 = ExprLang.Eval(ExprLang.Parse("10 % 3").Root, empty);
            Case(ref total, ref passed, "求值 10 % 3 = 1",
                r7.Ok && Convert.ToInt64(r7.Value) == 1L, r7.Ok ? r7.Value.ToString() : r7.Error);

            var r8 = ExprLang.Eval(ExprLang.Parse("s == \"hi\"").Root, vars);
            Case(ref total, ref passed, "求值字符串比较 = true",
                r8.Ok && r8.Value is bool && (bool)r8.Value, r8.Ok ? r8.Value.ToString() : r8.Error);

            var r9 = ExprLang.Eval(ExprLang.Parse("c == 'x'").Root, vars);
            Case(ref total, ref passed, "求值字符比较 = true",
                r9.Ok && r9.Value is bool && (bool)r9.Value, r9.Ok ? r9.Value.ToString() : r9.Error);

            var r10 = ExprLang.Eval(ExprLang.Parse("size(arr)").Root, vars, arrays);
            Case(ref total, ref passed, "求值 size(arr) = 3",
                r10.Ok && Convert.ToInt64(r10.Value) == 3L, r10.Ok ? r10.Value.ToString() : r10.Error);

            var r11 = ExprLang.Eval(ExprLang.Parse("arr[3]").Root, vars, arrays);
            Case(ref total, ref passed, "数组越界返回错误而非崩溃",
                !r11.Ok && r11.Error.Contains("越界"), r11.Ok ? "意外成功：" + r11.Value : r11.Error);

            var r12 = ExprLang.Eval(ExprLang.Parse("a / 0").Root, vars);
            Case(ref total, ref passed, "除零返回错误",
                !r12.Ok && r12.Error.Contains("除数"), r12.Ok ? "意外成功：" + r12.Value : r12.Error);

            var r13 = ExprLang.Eval(ExprLang.Parse("3.5 * 2").Root, empty);
            Case(ref total, ref passed, "求值小数 3.5 * 2 = 7",
                r13.Ok && r13.Value is double && Math.Abs((double)r13.Value - 7.0) < 1e-9,
                r13.Ok ? r13.Value.ToString() : r13.Error);

            var r14 = ExprLang.Eval(ExprLang.Parse("[1,2,3]").Root, empty);
            var list = r14.Ok ? r14.Value as List<object> : null;
            Case(ref total, ref passed, "求值数组字面量 → List<object>",
                list != null && list.Count == 3 && Convert.ToInt64(list[2]) == 3L,
                r14.Ok ? (list == null ? "不是 List" : "长度 " + list.Count) : r14.Error);
        }

        // ---------------- C++ 生成 ----------------

        static void CppCases(ref int total, ref int passed)
        {
            var d = Types();

            string c1 = ExprLang.Cpp(ExprLang.Parse("(a + b) * 2").Root, d);
            Case(ref total, ref passed, "C++ 生成 (a + b) * 2",
                c1.Contains("(a + b)") && c1.Contains("2LL"), c1);

            string c2 = ExprLang.Cpp(ExprLang.Parse("[1,2,3][1]").Root, d);
            Case(ref total, ref passed, "C++ 生成数组字面量 vector<long long>",
                c2.Contains("std::vector<long long>") && c2.Contains("[1LL]"), c2);

            string c3 = ExprLang.Cpp(ExprLang.Parse("[1,2.5]").Root, d);
            Case(ref total, ref passed, "C++ 生成 double 数组 vector<double>",
                c3.Contains("std::vector<double>") && c3.Contains("2.5"), c3);

            string c4 = ExprLang.Cpp(ExprLang.Parse("arr.length > 0 && arr.empty").Root, d);
            Case(ref total, ref passed, "C++ 生成 length/empty",
                c4.Contains(".size()") && c4.Contains(".empty()") && c4.Contains("&&"), c4);

            string c5 = ExprLang.Cpp(ExprLang.Parse("arr[i] >= len(arr)").Root, d);
            Case(ref total, ref passed, "C++ 生成下标与 len",
                c5.Contains("(arr)[i]") && c5.Contains(".size()") && c5.Contains(">="), c5);

            string c6 = ExprLang.Cpp(ExprLang.Parse("!f || a != b").Root, d);
            Case(ref total, ref passed, "C++ 生成逻辑与比较",
                c6.Contains("(!f)") && c6.Contains("||") && c6.Contains("!="), c6);
        }

        // ---------------- 位运算 ----------------

        // 优先级口径：~ > 移位 > & > ^ > |，整组低于算术、高于比较（与 Python 一致，和 C++ 不同）。
        // 生成 C++ 时每个二元节点都带全括号，所以这套优先级和对拍结果不会互相打架。
        static void BitCases(ref int total, ref int passed)
        {
            var vars = new Dictionary<string, object> { { "a", 6 }, { "b", 3 }, { "x", 1.5 } };
            var empty = new Dictionary<string, object>();

            EvalLong(ref total, ref passed, vars, "6 & 3", "", 2L);
            EvalLong(ref total, ref passed, vars, "6 | 3", "", 7L);
            EvalLong(ref total, ref passed, vars, "6 ^ 3", "", 5L);
            EvalLong(ref total, ref passed, vars, "a << b", "", 48L);
            EvalLong(ref total, ref passed, vars, "a >> b", "", 0L);
            EvalLong(ref total, ref passed, vars, "~a", "", -7L);
            EvalLong(ref total, ref passed, empty, "1 | 2 & 3", "（& 高于 |）", 3L);
            EvalLong(ref total, ref passed, empty, "1 + 2 << 3", "（算术高于移位）", 24L);
            EvalLong(ref total, ref passed, empty, "8 >> 1 + 1", "（算术高于移位）", 2L);

            var cmp = ExprLang.Eval(ExprLang.Parse("1 & 2 == 2").Root, empty);
            Case(ref total, ref passed, "1 & 2 == 2 读作 (1 & 2) == 2 = false",
                cmp.Ok && cmp.Value is bool && !(bool)cmp.Value, cmp.Ok ? "" + cmp.Value : cmp.Error);

            var shift = ExprLang.Eval(ExprLang.Parse("1 << 63").Root, empty);
            Case(ref total, ref passed, "移位超过 62 位被拦下",
                !shift.Ok && shift.Error.Contains("62"), shift.Ok ? "意外成功：" + shift.Value : shift.Error);

            var realBit = ExprLang.Check("x & 1", Types());
            Case(ref total, ref passed, "小数做位运算被类型检查拦下",
                !realBit.Ok && realBit.Error.Contains("整数"), realBit.Ok ? "意外通过检查" : realBit.Error);

            var boolNot = ExprLang.Check("~(a > b)", Types());
            Case(ref total, ref passed, "对 bool 取反 ~ 被拦下（该用 !）",
                !boolNot.Ok && boolNot.Error.Contains("整数"), boolNot.Ok ? "意外通过检查" : boolNot.Error);

            string cpp = ExprLang.Cpp(ExprLang.Parse("a << 3 | b & 1").Root, Types());
            Case(ref total, ref passed, "C++ 生成位运算带全括号",
                cpp.Contains("<<") && cpp.Contains("|") && cpp.Contains("&") && cpp.Contains("(a << 3LL)"), cpp);

            string cppNot = ExprLang.Cpp(ExprLang.Parse("~a").Root, Types());
            Case(ref total, ref passed, "C++ 生成 ~", cppNot.Contains("(~a)"), cppNot);
        }

        static void EvalLong(ref int total, ref int passed, Dictionary<string, object> vars, string source, string note, long expect)
        {
            var r = ExprLang.Eval(ExprLang.Parse(source).Root, vars);
            Case(ref total, ref passed, "求值 " + source + " = " + expect + note,
                r.Ok && r.Value is long && (long)r.Value == expect, r.Ok ? "" + r.Value : r.Error);
        }

        // ---------------- 用例计数 ----------------

        static void Case(ref int total, ref int passed, string name, bool ok, string detail)
        {
            total++;
            if (ok)
            {
                passed++;
                Debug.Log("✔ ExprLang " + name + " —— " + detail);
            }
            else
            {
                Debug.Log("✘ ExprLang " + name + " —— " + detail);
            }
        }
    }
}
