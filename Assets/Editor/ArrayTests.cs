using System;
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
    /// 数组自测：数组变量声明（含「长度」参数按默认值铺满）、a[i] 取元素、a.length 求长度、
    /// 数组参与运算、数组写入 a[i] = x（含按下标自增长）、复合赋值 += -= *= …、
    /// 越界与负下标报错、编译期类型报错，以及 C++ 原生程序与 VM 的结果比对。
    /// </summary>
    public static class ArrayTests
    {
        // 供 Unity 批处理 -executeMethod 直接调用：Dayu.EditorTools.ArrayTests.RunAll
        public static void RunAll()
        {
            int total = 0, passed = 0;
            Run(ref total, ref passed);
            Debug.Log("数组 共 " + total + " 项，通过 " + passed + " 项");
            if (total != passed) EditorApplication.Exit(1);
        }

        public static void Run(ref int total, ref int passed)
        {
            DeclareCases(ref total, ref passed);
            IndexCases(ref total, ref passed);
            LengthCases(ref total, ref passed);
            ArithmeticCases(ref total, ref passed);
            ElementTypeCases(ref total, ref passed);
            AssignCases(ref total, ref passed);
            SetCases(ref total, ref passed);
            InputIndexedCases(ref total, ref passed);
            LoopCases(ref total, ref passed);
            BoundsErrorCases(ref total, ref passed);
            SetErrorCases(ref total, ref passed);
            SetBeforeCreateCase(ref total, ref passed);
            DeclareOrderCases(ref total, ref passed);
            CompileErrorCases(ref total, ref passed);
            AssignParseCases(ref total, ref passed);
            CompoundAssignCases(ref total, ref passed);
            SizedArrayCases(ref total, ref passed);
            LoopVarNameCases(ref total, ref passed);
        }

        // ---------------- 用例 ----------------

        // int[] a = [1, 2, 3]；取 a[1] 应为 2。
        private static void DeclareCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "ARRAY-DECL", "声明 int[] 数组", ArrayGraph("ARRAY-DECL", "int[]", "[1, 2, 3]", "a[1]"),
                "", "2");
        }

        // a[0] 应为 1。
        private static void IndexCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "ARRAY-INDEX", "a[0] 取元素", ArrayGraph("ARRAY-INDEX", "int[]", "[1, 2, 3]", "a[0]"),
                "", "1");
        }

        // a.length 应为 3。
        private static void LengthCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "ARRAY-LENGTH", "a.length 求长度", ArrayGraph("ARRAY-LENGTH", "int[]", "[1, 2, 3]", "a.length"),
                "", "3");
        }

        // a[0] + a[2] 应为 4。
        private static void ArithmeticCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "ARRAY-ADD", "a[0] + a[2]", ArrayGraph("ARRAY-ADD", "int[]", "[1, 2, 3]", "a[0] + a[2]"),
                "", "4");
        }

        // string[] 与 char[] 数组同样可以取元素。
        private static void ElementTypeCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "ARRAY-STRING", "string[] 数组取元素", ArrayGraph("ARRAY-STRING", "string[]", "[\"ab\", \"cd\"]", "names[0]"),
                "", "ab");
            Verify(ref total, ref passed, "ARRAY-CHAR", "char[] 数组取元素", ArrayGraph("ARRAY-CHAR", "char[]", "['x', 'y']", "cs[1]"),
                "", "y");
            Verify(ref total, ref passed, "ARRAY-DOUBLE", "double[] 数组求长度", ArrayGraph("ARRAY-DOUBLE", "double[]", "[1.5, 2.5]", "ds.length"),
                "", "2");
        }

        // 先建空数组，再用「赋值」整体赋值，最后取元素与长度。
        private static void AssignCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "ARRAY-ASSIGN", "赋值给数组", AssignGraph(),
                "", "9\n2");
        }

        // 数组写入：逐个下标赋值后读回，长度随写入自增长。
        private static void SetCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "ARRAY-SET", "数组写入 a[i] = x", SetGraph(),
                "", "9\n2");
            Verify(ref total, ref passed, "ARRAY-SET-GROW", "跳过下标写入自动补位", GrowGraph(),
                "", "3\n0\n5");
            Verify(ref total, ref passed, "ARRAY-SET-LOOP", "循环写入数组再按 a[i] 累加", FillLoopGraph(),
                "5\n1 2 3 4 5", "15", "3\n-1 -2 -3", "-6");
        }

        // 循环里用 a[i] 依次取出第 2、3 个元素。
        private static void LoopCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "ARRAY-LOOP", "循环里按 a[i] 取元素", LoopGraph(),
                "", "20\n30");
        }

        // 输入直接写进 a[i]：循环里一格一格读，不用再借一个临时变量中转。
        private static void InputIndexedCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "ARRAY-INPUT-IDX", "输入直接写 a[i]", InputIndexedGraph(),
                "3\n10 20 30", "3\n10\n60", "1\n7", "1\n7\n7");
            CheckTarget(ref total, ref passed, "a", "a", null);
            CheckTarget(ref total, ref passed, "a[i]", "a", "i");
            CheckTarget(ref total, ref passed, "a[i - 1]", "a", "i - 1");
            CheckTargetNot(ref total, ref passed, "a[]");
            CheckTargetNot(ref total, ref passed, "a + b");
            ExpectCompileError(ref total, ref passed, "输入给标量带下标", ScalarIndexedInputGraph(), "不是数组");
            ExpectCompileError(ref total, ref passed, "输入的元素类型选错", TypedInputGraph(), "读取类型要选");
            InputBeforeCreateCase(ref total, ref passed);
        }

        // 先读进 a[i]、后创建数组 a：变量表是全图预扫的，编译过得去，但运行时数组还没建，
        // 必须给中文报错而不是崩掉——和「赋值」那条同类用例保持一致口径。
        private static void InputBeforeCreateCase(ref int total, ref int passed)
        {
            total++;
            var cr = Compiler.Compile(LateInputGraph());
            if (!cr.Ok) { Debug.Log("✘ 数组未创建就读入 —— Core 编译失败：" + cr.Errors[0].Message); return; }
            var run = Vm.Run(cr.Program, "5");
            if (!run.Ok && run.Error != null && run.Error.Message.Contains("尚未创建"))
            {
                passed++;
                Debug.Log("✔ 数组未创建就读入 —— " + run.Error.Message);
            }
            else Debug.Log("✘ 数组未创建就读入 —— " + (run.Ok ? "意外成功：" + run.Output : run.Error.Message));
        }

        private static void CheckTarget(ref int total, ref int passed, string src, string name, string index)
        {
            total++;
            string n, i;
            if (Assign.TryParseTarget(src, out n, out i) && n == name && i == index)
            {
                passed++;
                Debug.Log("✔ 写入位置解析 —— " + src);
            }
            else Debug.Log("✘ 写入位置解析 —— " + src + " 实际得到 " + n + " / " + (i ?? "无下标"));
        }

        private static void CheckTargetNot(ref int total, ref int passed, string src)
        {
            total++;
            string n, i;
            if (!Assign.TryParseTarget(src, out n, out i))
            {
                passed++;
                Debug.Log("✔ 不当成写入位置 —— " + src);
            }
            else Debug.Log("✘ 不当成写入位置 —— " + src + " 被当成 " + n + (i != null ? "[" + i + "]" : ""));
        }

        // a[3] 越界：VM 应给出中文错误而不是崩溃。
        private static void BoundsErrorCases(ref int total, ref int passed)
        {
            total++;
            var g = ArrayGraph("ARRAY-BOUND", "int[]", "[1, 2, 3]", "a[3]");
            var cr = Compiler.Compile(g);
            if (!cr.Ok)
            {
                Debug.Log("✘ 数组下标越界 —— Core 编译失败：" + cr.Errors[0].Message);
                return;
            }
            var run = Vm.Run(cr.Program, "");
            string want = "数组下标越界：3（长度 3）。";
            if (!run.Ok && run.Error != null && run.Error.Message.Contains(want))
            {
                passed++;
                Debug.Log("✔ 数组下标越界 —— " + run.Error.Message);
            }
            else Debug.Log("✘ 数组下标越界 —— " + (run.Ok ? "意外成功：" + run.Output : run.Error.Message));
        }

        // 数组变量先用后创建：可见范围按执行顺序实时算，所以这种图在编译期就该拦下来，而不是等运行到才炸。
        private static void DeclareOrderCases(ref int total, ref int passed)
        {
            var g = G("ARRAY-ORDER");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("o", "output", "outputExpr", "a[0]"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "initExpr", "[1, 2, 3]")
            });
            Exec(g, "e", "out", "o");
            Exec(g, "o", "out", "d");
            ExpectCompileError(ref total, ref passed, "数组变量先用后创建", g, "还没创建");
        }

        // 赋值语句解析：只有顶层一个等号、左边写成 变量 或 变量[下标] 才算；
        // 比较运算符、嵌套下标、连续赋值都不该被误认成赋值。
        private static void AssignParseCases(ref int total, ref int passed)
        {
            CheckParse(ref total, ref passed, "a[i] = x", "a", "i", "x");
            CheckParse(ref total, ref passed, "a[0] = 1", "a", "0", "1");
            CheckParse(ref total, ref passed, "sum = sum + a[i]", "sum", null, "sum + a[i]");
            CheckParse(ref total, ref passed, "a[i + 1] = b[i] * 2", "a", "i + 1", "b[i] * 2");
            CheckNotParse(ref total, ref passed, "a[i] == x");
            CheckNotParse(ref total, ref passed, "a[i] != x");
            CheckNotParse(ref total, ref passed, "a[i] <= x");
            CheckNotParse(ref total, ref passed, "a[0][1] = x");
            CheckNotParse(ref total, ref passed, "a = b = c");
            CheckNotParse(ref total, ref passed, "(a) = 1");
            CheckNotParse(ref total, ref passed, "a[i]");
            // 顶层等号的位置决定报错措辞：一个都没有才是「还没写存给谁」，
            // 有等号却没解析成功（a = = x）得照抄玩家那句。
            CheckEqAt(ref total, ref passed, "a[i] = x", 5);
            CheckEqAt(ref total, ref passed, "s += 1", 3);
            CheckEqAt(ref total, ref passed, "a = b = c", -2);
            CheckEqAt(ref total, ref passed, "a = = [1,2]", -2);
            CheckEqAt(ref total, ref passed, "a == b", -1);
            CheckEqAt(ref total, ref passed, "a[i]", -1);
            CheckEqAt(ref total, ref passed, "", -1);
            VarsetHintCases(ref total, ref passed);
        }

        // 「赋值」面板只剩一个填空，报错必须说准缺的是哪一半，
        // 不能把写坏了的句子说成「还没写出要存给哪个变量」——名字明明在等号左边。
        private static void VarsetHintCases(ref int total, ref int passed)
        {
            var empty = G("VARSET-HINT-EMPTY");
            empty.Nodes.AddRange(new[] { Node("e", "entry"), Node("s", "varset") });
            Exec(empty, "e", "out", "s");
            ExpectCompileError(ref total, ref passed, "赋值整句没写", empty, "还没写出要存给哪个变量");

            var broken = G("VARSET-HINT-BROKEN");
            broken.Nodes.AddRange(new[] { Node("e", "entry"), Node("s", "varset", "valueExpr", "a = = [1,2]") });
            Exec(broken, "e", "out", "s");
            ExpectCompileError(ref total, ref passed, "赋值句子写坏", broken, "这句没写对：「a = = [1,2]」");
        }

        private static void CheckEqAt(ref int total, ref int passed, string src, int expect)
        {
            total++;
            int at = Assign.TopLevelEqualsAt(src);
            if (at == expect)
            {
                passed++;
                Debug.Log("✔ 顶层等号位置 —— 「" + src + "」= " + at);
            }
            else Debug.Log("✘ 顶层等号位置 —— 「" + src + "」期望 " + expect + " 实际 " + at);
        }

        private static void CheckParse(ref int total, ref int passed, string src, string name, string index, string rhs)
        {
            total++;
            var asg = Assign.TryParse(src);
            if (asg != null && asg.Name == name && asg.IndexExpr == index && asg.Rhs == rhs)
            {
                passed++;
                Debug.Log("✔ 赋值解析 —— " + src);
            }
            else Debug.Log("✘ 赋值解析 —— " + src + " 实际得到 " + (asg == null ? "不是赋值语句" : asg.ToString()));
        }

        private static void CheckNotParse(ref int total, ref int passed, string src)
        {
            total++;
            var asg = Assign.TryParse(src);
            if (asg == null)
            {
                passed++;
                Debug.Log("✔ 不当成赋值 —— " + src);
            }
            else Debug.Log("✘ 不当成赋值 —— " + src + " 被当成 " + asg);
        }

        // 复合赋值：+= -= *= /= %= 与位运算 &= |= ^= <<= >>=，全部脱糖成「左边 = 左边 op 右边」，
        // 所以 Vm 与 CppGen 不需要各自再实现一遍运算，两边天然一致。
        private static void CompoundAssignCases(ref int total, ref int passed)
        {
            CheckParse(ref total, ref passed, "s += 1", "s", null, "s + 1");
            CheckParse(ref total, ref passed, "s -= t * 2", "s", null, "s - t * 2");
            CheckParse(ref total, ref passed, "n *= n", "n", null, "n * n");
            CheckParse(ref total, ref passed, "n /= 3", "n", null, "n / 3");
            CheckParse(ref total, ref passed, "n %= 4", "n", null, "n % 4");
            CheckParse(ref total, ref passed, "m &= 3", "m", null, "m & 3");
            CheckParse(ref total, ref passed, "m |= 3", "m", null, "m | 3");
            CheckParse(ref total, ref passed, "m ^= 3", "m", null, "m ^ 3");
            CheckParse(ref total, ref passed, "m <<= 2", "m", null, "m << 2");
            CheckParse(ref total, ref passed, "m >>= 2", "m", null, "m >> 2");
            CheckParse(ref total, ref passed, "a[i] += x", "a", "i", "a[i] + x");
            CheckParse(ref total, ref passed, "a[i + 1] *= 2", "a", "i + 1", "a[i + 1] * 2");
            CheckParse(ref total, ref passed, "s+=1", "s", null, "s + 1");
            // 认了 <<= >>= 之后，比较运算符仍然不能被误当成赋值
            CheckNotParse(ref total, ref passed, "a <= b");
            CheckNotParse(ref total, ref passed, "a >= b");
            CheckNotParse(ref total, ref passed, "a[i] <= x");
            CheckNotParse(ref total, ref passed, "a <<= b <<= c");

            Verify(ref total, ref passed, "COMPOUND-SET", "复合赋值 s += -= *= /= %= <<= 与 a[i] +=",
                CompoundGraph(), "", "16\n12");
            Verify(ref total, ref passed, "COMPOUND-LOOP-SUM", "循环里 sum += a[k]",
                LoopVarGraph("sum += a[k]"), "", "6");
            Verify(ref total, ref passed, "COMPOUND-LOOP-SUB", "循环里 sum -= a[k]",
                LoopVarGraph("sum -= a[k]"), "", "-6");
            Verify(ref total, ref passed, "COMPOUND-LOOP-MUL", "循环里 sum *= a[k]（初值 1）",
                LoopVarGraph("sum *= a[k]", "1"), "", "6");
        }

        // s 从 10 出发连做五种复合赋值，再把 a[1] 就地加 10 倍 a[0]；VM 与 g++ 必须给出同一串输出。
        private static Graph CompoundGraph()
        {
            var g = G("COMPOUND-SET");
            g.ExprMode = true;
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "initExpr", "[1, 2, 3]"),
                Node("ds", "vardecl", "name", "s", "vtype", "int", "initExpr", "10"),
                Node("w1", "varset", "name", "s", "valueExpr", "s += 5"),
                Node("w2", "varset", "name", "s", "valueExpr", "s -= 3"),
                Node("w3", "varset", "name", "s", "valueExpr", "s *= 2"),
                Node("w4", "varset", "name", "s", "valueExpr", "s /= 6"),
                Node("w5", "varset", "name", "s", "valueExpr", "s %= 3"),
                Node("w6", "varset", "name", "s", "valueExpr", "s <<= 4"),
                Node("w7", "varset", "name", "a", "valueExpr", "a[1] += a[0] * 10"),
                Node("o1", "output", "outputExpr", "s"),
                Node("o2", "output", "outputExpr", "a[1]")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "ds");
            Exec(g, "ds", "out", "w1");
            Exec(g, "w1", "out", "w2");
            Exec(g, "w2", "out", "w3");
            Exec(g, "w3", "out", "w4");
            Exec(g, "w4", "out", "w5");
            Exec(g, "w5", "out", "w6");
            Exec(g, "w6", "out", "w7");
            Exec(g, "w7", "out", "o1");
            Exec(g, "o1", "out", "o2");
            return g;
        }

        // ---------------- 创建数组：长度参数 ----------------

        // 长度 N = 先按默认值铺满 N 格（等价 C++ 的 vector::resize），VM 与 g++ 必须给出同一串输出。
        private static void SizedArrayCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "SIZED-ARRAY", "创建数组填长度 4，铺满默认值后就地改元素",
                SizedArrayGraph(), "", "4\n7\n0");

            Verify(ref total, ref passed, "SIZED-ARRAY-EXPR", "创建数组的长度写表达式 n（先读入再创建）",
                SizedArrayExprGraph(), "3", "3\n4");

            var scalar = G("SIZED-ERR-SCALAR");
            scalar.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "s", "vtype", "int", "size", "4"),
                Node("o", "output", "outputExpr", "s")
            });
            Exec(scalar, "e", "out", "d");
            Exec(scalar, "d", "out", "o");
            ExpectCompileError(ref total, ref passed, "标量写长度", scalar, "不是数组，写不了长度");

            var both = G("SIZED-ERR-BOTH");
            both.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "size", "4", "initExpr", "[1, 2]"),
                Node("o", "output", "outputExpr", "a.length")
            });
            Exec(both, "e", "out", "d");
            Exec(both, "d", "out", "o");
            ExpectCompileError(ref total, ref passed, "长度与初始值同时填", both, "只能填一个");

            var frac = G("SIZED-ERR-FRAC");
            frac.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "size", "1.5"),
                Node("o", "output", "outputExpr", "a.length")
            });
            Exec(frac, "e", "out", "d");
            Exec(frac, "d", "out", "o");
            ExpectCompileError(ref total, ref passed, "长度写成小数", frac, "长度必须是整数");
        }

        private static Graph SizedArrayGraph()
        {
            var g = G("SIZED-ARRAY");
            g.ExprMode = true;
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "size", "4"),
                Node("w", "varset", "name", "a", "valueExpr", "a[1] = 7"),
                Node("o1", "output", "outputExpr", "a.length"),
                Node("o2", "output", "outputExpr", "a[1]"),
                Node("o3", "output", "outputExpr", "a[3]")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "w");
            Exec(g, "w", "out", "o1");
            Exec(g, "o1", "out", "o2");
            Exec(g, "o2", "out", "o3");
            return g;
        }

        // 长度可以是表达式：读进 n 之后再创建数组，铺 n 格，循环里填 k*k。
        private static Graph SizedArrayExprGraph()
        {
            var g = G("SIZED-ARRAY-EXPR");
            g.ExprMode = true;
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dn", "vardecl", "name", "n", "vtype", "int"),
                Node("in", "input", "vtype", "int", "name", "n"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "size", "n"),
                Node("loop", "forloop", "fromExpr", "0", "toExpr", "n - 1", "indexName", "k"),
                Node("w", "varset", "name", "a", "valueExpr", "a[k] = k * k"),
                Node("o1", "output", "outputExpr", "a.length"),
                Node("o2", "output", "outputExpr", "a[n - 1]")
            });
            Exec(g, "e", "out", "dn");
            Exec(g, "dn", "out", "in");
            Exec(g, "in", "out", "d");
            Exec(g, "d", "out", "loop");
            Exec(g, "loop", "body", "w");
            Exec(g, "loop", "done", "o1");
            Exec(g, "o1", "out", "o2");
            return g;
        }

        // 循环变量可以自己命名：k 在循环体里可见，改名后默认的 i 就不再凭空出现。
        private static void LoopVarNameCases(ref int total, ref int passed)
        {
            var g = LoopVarGraph("sum = sum + a[k]");
            Verify(ref total, ref passed, "LOOP-VAR-NAME", "循环变量命名成 k", g, "", "6");

            var stale = LoopVarGraph("sum = sum + a[i]");
            ExpectCompileError(ref total, ref passed, "循环改名后旧的 i 不再可见", stale, "未定义的变量「i」");
        }

        // for k = 0..2 把 a[k] 累加进 sum，输出 sum。
        private static Graph LoopVarGraph(string bodyExpr) { return LoopVarGraph(bodyExpr, "0"); }

        private static Graph LoopVarGraph(string bodyExpr, string sumInit)
        {
            var g = G("LOOP-VAR");
            g.ExprMode = true;
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "initExpr", "[1, 2, 3]"),
                Node("s", "vardecl", "name", "sum", "vtype", "int", "initExpr", sumInit),
                Node("loop", "forloop", "fromExpr", "0", "toExpr", "2", "indexName", "k"),
                Node("add", "varset", "name", "sum", "valueExpr", bodyExpr),
                Node("o", "output", "outputExpr", "sum")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "s");
            Exec(g, "s", "out", "loop");
            Exec(g, "loop", "body", "add");
            Exec(g, "loop", "done", "o");
            return g;
        }

        // 编译期就应该拦住的错误：非数组取下标、数组参与算术、数组变量没声明、类型不匹配。
        private static void CompileErrorCases(ref int total, ref int passed)
        {
            var notArray = G("ARRAY-ERR-INDEX");
            notArray.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int"),
                Node("o", "output", "outputExpr", "a[0]")
            });
            Exec(notArray, "e", "out", "d");
            Exec(notArray, "d", "out", "o");
            ExpectCompileError(ref total, ref passed, "非数组取下标", notArray, "不是数组");

            var arithmetic = G("ARRAY-ERR-OP");
            arithmetic.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "initExpr", "[1, 2, 3]"),
                Node("o", "output", "outputExpr", "a + 1")
            });
            Exec(arithmetic, "e", "out", "d");
            Exec(arithmetic, "d", "out", "o");
            ExpectCompileError(ref total, ref passed, "数组参与算术", arithmetic, "不匹配");

            var undeclared = G("ARRAY-ERR-UNDECL");
            undeclared.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("o", "output", "outputExpr", "a[0]")
            });
            Exec(undeclared, "e", "out", "o");
            ExpectCompileError(ref total, ref passed, "数组变量未声明", undeclared, "未定义的变量");

            var scalarInit = G("ARRAY-ERR-SCALAR");
            scalarInit.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "initExpr", "1 + 2"),
                Node("o", "output", "outputExpr", "a[0]")
            });
            Exec(scalarInit, "e", "out", "d");
            Exec(scalarInit, "d", "out", "o");
            ExpectCompileError(ref total, ref passed, "数组变量用标量初始化", scalarInit, "必须给出数组");

            var wrongElem = G("ARRAY-ERR-ELEM");
            wrongElem.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "initExpr", "[1.5]"),
                Node("o", "output", "outputExpr", "a[0]")
            });
            Exec(wrongElem, "e", "out", "d");
            Exec(wrongElem, "d", "out", "o");
            ExpectCompileError(ref total, ref passed, "数组元素类型不匹配", wrongElem, "元素类型");

            // 数组不能直接接到标量端口上，只能用 a[i] 取元素。
            var plug = G("ARRAY-ERR-PLUG");
            plug.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "initExpr", "[1, 2, 3]"),
                Node("get", "varget", "name", "a"),
                Node("o", "output")
            });
            Exec(plug, "e", "out", "d");
            Exec(plug, "d", "out", "o");
            Data(plug, "get", "value", "o", "value");
            ExpectCompileError(ref total, ref passed, "数组直接连到标量端口", plug, "不能直接连到");
        }

        // 下标赋值在编译期就该拦住的错误：写进标量变量、下标不是整数、数组变量被赋标量、元素类型不符、整段数组赋值、数组没声明；
        // 外加一条运行期检查：负下标要被拦下来。
        private static void SetErrorCases(ref int total, ref int passed)
        {
            var scalar = G("SET-ERR-SCALAR");
            scalar.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int"),
                Node("w", "varset", "name", "a", "valueExpr", "a[0] = 1")
            });
            Exec(scalar, "e", "out", "d");
            Exec(scalar, "d", "out", "w");
            ExpectCompileError(ref total, ref passed, "标量变量写不成下标赋值", scalar, "不是数组，写不成");

            var realIndex = G("SET-ERR-INDEX");
            realIndex.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]"),
                Node("w", "varset", "name", "a", "valueExpr", "a[1.5] = 1")
            });
            Exec(realIndex, "e", "out", "d");
            Exec(realIndex, "d", "out", "w");
            ExpectCompileError(ref total, ref passed, "数组下标不是整数", realIndex, "下标必须是整数");

            var scalarToArr = G("SET-ERR-NOINDEX");
            scalarToArr.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]"),
                Node("w", "varset", "name", "a", "valueExpr", "a = 1")
            });
            Exec(scalarToArr, "e", "out", "d");
            Exec(scalarToArr, "d", "out", "w");
            ExpectCompileError(ref total, ref passed, "数组变量赋值缺整段数组", scalarToArr, "赋值表达式必须给出数组");

            var wrongElem = G("SET-ERR-ELEM");
            wrongElem.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]"),
                Node("w", "varset", "name", "a", "valueExpr", "a[0] = \"ab\"")
            });
            Exec(wrongElem, "e", "out", "d");
            Exec(wrongElem, "d", "out", "w");
            ExpectCompileError(ref total, ref passed, "写入值与元素类型不符", wrongElem, "元素类型不匹配");

            var wholeArray = G("SET-ERR-ARRAY");
            wholeArray.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]"),
                Node("s", "vardecl", "name", "b", "vtype", "int[]", "initExpr", "[1, 2]"),
                Node("w", "varset", "name", "a", "valueExpr", "a[0] = b")
            });
            Exec(wholeArray, "e", "out", "d");
            Exec(wholeArray, "d", "out", "s");
            Exec(wholeArray, "s", "out", "w");
            ExpectCompileError(ref total, ref passed, "下标右边写整段数组", wholeArray, "要给出一个元素");

            var undeclared = G("SET-ERR-UNDECL");
            undeclared.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("w", "varset", "name", "a", "valueExpr", "a[0] = 1")
            });
            Exec(undeclared, "e", "out", "w");
            ExpectCompileError(ref total, ref passed, "写入未声明的数组", undeclared, "还没创建");

            total++;
            var neg = G("SET-ERR-NEG");
            neg.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]"),
                Node("w", "varset", "name", "a", "valueExpr", "a[0 - 1] = 1")
            });
            Exec(neg, "e", "out", "d");
            Exec(neg, "d", "out", "w");
            var cr = Compiler.Compile(neg);
            if (!cr.Ok)
            {
                Debug.Log("✘ 数组写入负下标 —— Core 编译失败：" + cr.Errors[0].Message);
                return;
            }
            var run = Vm.Run(cr.Program, "");
            if (!run.Ok && run.Error != null && run.Error.Message.Contains("下标不能是负数"))
            {
                passed++;
                Debug.Log("✔ 数组写入负下标 —— " + run.Error.Message);
            }
            else Debug.Log("✘ 数组写入负下标 —— " + (run.Ok ? "意外成功：" + run.Output : run.Error.Message));
        }

        // 数组还没创建就写入：VM 要提示先执行「创建变量」。
        private static void SetBeforeCreateCase(ref int total, ref int passed)
        {
            total++;
            var g = G("SET-ERR-LATE");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("w", "varset", "name", "a", "valueExpr", "a[0] = 1"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]")
            });
            Exec(g, "e", "out", "w");
            Exec(g, "w", "out", "d");
            var cr = Compiler.Compile(g);
            if (!cr.Ok)
            {
                Debug.Log("✘ 数组未创建就写入 —— Core 编译失败：" + cr.Errors[0].Message);
                return;
            }
            var run = Vm.Run(cr.Program, "");
            if (!run.Ok && run.Error != null && run.Error.Message.Contains("尚未创建"))
            {
                passed++;
                Debug.Log("✔ 数组未创建就写入 —— " + run.Error.Message);
            }
            else Debug.Log("✘ 数组未创建就写入 —— " + (run.Ok ? "意外成功：" + run.Output : run.Error.Message));
        }

        // ---------------- 用例图 ----------------

        // 声明一个数组变量并输出表达式结果；变量名按元素类型取名。
        private static Graph ArrayGraph(string id, string vtype, string init, string expr)
        {
            var g = G(id);
            string name = vtype == "string[]" ? "names" : vtype == "char[]" ? "cs" : vtype == "double[]" ? "ds" : "a";
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", name, "vtype", vtype, "initExpr", init),
                Node("o", "output", "outputExpr", expr)
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "o");
            return g;
        }

        // 先建空数组，再用「赋值」整体赋值；输出 a[0] + a[1] 与 a.length。
        private static Graph AssignGraph()
        {
            var g = G("ARRAY-ASSIGN");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]"),
                Node("s", "varset", "name", "a", "valueExpr", "[4, 5]"),
                Node("o1", "output", "outputExpr", "a[0] + a[1]"),
                Node("o2", "output", "outputExpr", "a.length")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "s");
            Exec(g, "s", "out", "o1");
            Exec(g, "o1", "out", "o2");
            return g;
        }

        // for i = 1..2：输出 a[i]，即第 2、3 个元素。
        private static Graph LoopGraph()
        {
            var g = G("ARRAY-LOOP");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]", "initExpr", "[10, 20, 30]"),
                Node("loop", "forloop", "fromExpr", "1", "toExpr", "2"),
                Node("o", "output", "outputExpr", "a[i]")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "loop");
            Exec(g, "loop", "body", "o");
            return g;
        }

        // 空数组 a，逐个下标写入 a[0]=7、a[1]=2；输出 a[0] + a[1] 与 a.length。
        private static Graph SetGraph()
        {
            var g = G("ARRAY-SET");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]"),
                Node("w0", "varset", "name", "a", "valueExpr", "a[0] = 7"),
                Node("w1", "varset", "name", "a", "valueExpr", "a[1] = a[0] - 5"),
                Node("o1", "output", "outputExpr", "a[0] + a[1]"),
                Node("o2", "output", "outputExpr", "a.length")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "w0");
            Exec(g, "w0", "out", "w1");
            Exec(g, "w1", "out", "o1");
            Exec(g, "o1", "out", "o2");
            return g;
        }

        // 直接写 a[2]，前面的空位由默认值补上；输出长度、a[0] 与 a[2]。
        private static Graph GrowGraph()
        {
            var g = G("ARRAY-SET-GROW");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]"),
                Node("w", "varset", "name", "a", "valueExpr", "a[2] = 5"),
                Node("o1", "output", "outputExpr", "a.length"),
                Node("o2", "output", "outputExpr", "a[0]"),
                Node("o3", "output", "outputExpr", "a[2]")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "w");
            Exec(g, "w", "out", "o1");
            Exec(g, "o1", "out", "o2");
            Exec(g, "o2", "out", "o3");
            return g;
        }

        // L2 的形状：读 n，循环把 n 个数写进 a[i]，再循环按 a[i] 累加求和。
        private static Graph FillLoopGraph()
        {
            var g = G("ARRAY-SET-LOOP");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dn", "vardecl", "name", "n", "vtype", "int"),
                Node("in", "input", "vtype", "int", "name", "n"),
                Node("da", "vardecl", "name", "a", "vtype", "int[]"),
                Node("dx", "vardecl", "name", "x", "vtype", "int", "initExpr", "0"),
                Node("ds", "vardecl", "name", "sum", "vtype", "int", "initExpr", "0"),
                Node("fill", "forloop", "fromExpr", "0", "toExpr", "n - 1"),
                Node("inx", "input", "vtype", "int", "name", "x"),
                Node("w", "varset", "name", "a", "valueExpr", "a[i] = x"),
                Node("acc", "forloop", "fromExpr", "0", "toExpr", "a.length - 1"),
                Node("add", "varset", "name", "sum", "valueExpr", "sum + a[i]"),
                Node("o", "output", "outputExpr", "sum")
            });
            Exec(g, "e", "out", "dn");
            Exec(g, "dn", "out", "in");
            Exec(g, "in", "out", "da");
            Exec(g, "da", "out", "dx");
            Exec(g, "dx", "out", "ds");
            Exec(g, "ds", "out", "fill");
            Exec(g, "fill", "body", "inx");
            Exec(g, "inx", "out", "w");
            Exec(g, "fill", "done", "acc");
            Exec(g, "acc", "body", "add");
            Exec(g, "acc", "done", "o");
            return g;
        }

        // 读 n，循环把 n 个数直接读进 a[i]，再循环累加；输出长度、首元素与和。
        private static Graph InputIndexedGraph()
        {
            var g = G("ARRAY-INPUT-IDX");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dn", "vardecl", "name", "n", "vtype", "int"),
                Node("in", "input", "vtype", "int", "name", "n"),
                Node("da", "vardecl", "name", "a", "vtype", "int[]"),
                Node("ds", "vardecl", "name", "sum", "vtype", "int", "initExpr", "0"),
                Node("fill", "forloop", "fromExpr", "0", "toExpr", "n - 1"),
                Node("w", "input", "vtype", "int", "name", "a[i]"),
                Node("acc", "forloop", "fromExpr", "0", "toExpr", "a.length - 1"),
                Node("add", "varset", "name", "sum", "valueExpr", "sum + a[i]"),
                Node("o1", "output", "outputExpr", "a.length"),
                Node("o2", "output", "outputExpr", "a[0]"),
                Node("o3", "output", "outputExpr", "sum")
            });
            Exec(g, "e", "out", "dn");
            Exec(g, "dn", "out", "in");
            Exec(g, "in", "out", "da");
            Exec(g, "da", "out", "ds");
            Exec(g, "ds", "out", "fill");
            Exec(g, "fill", "body", "w");
            Exec(g, "fill", "done", "acc");
            Exec(g, "acc", "body", "add");
            Exec(g, "acc", "done", "o1");
            Exec(g, "o1", "out", "o2");
            Exec(g, "o2", "out", "o3");
            return g;
        }

        // 目标是标量 x 却带了下标：编译期就该拦下来。
        private static Graph ScalarIndexedInputGraph()
        {
            var g = G("INPUT-IDX-SCALAR");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "x", "vtype", "int"),
                Node("loop", "forloop", "fromExpr", "0", "toExpr", "0"),
                Node("w", "input", "vtype", "int", "name", "x[i]")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "loop");
            Exec(g, "loop", "body", "w");
            return g;
        }

        // 数组装的是 int，读取类型却选了 double：元素类型对不上要报错。
        private static Graph TypedInputGraph()
        {
            var g = G("INPUT-IDX-TYPE");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]"),
                Node("loop", "forloop", "fromExpr", "0", "toExpr", "0"),
                Node("w", "input", "vtype", "double", "name", "a[i]")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "loop");
            Exec(g, "loop", "body", "w");
            return g;
        }

        // 先读进 a[i] 再创建数组 a：编译期变量表是全图预扫的，所以这条要到运行时才暴露。
        private static Graph LateInputGraph()
        {
            var g = G("INPUT-IDX-LATE");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("w", "input", "vtype", "int", "name", "a[0]"),
                Node("d", "vardecl", "name", "a", "vtype", "int[]")
            });
            Exec(g, "e", "out", "w");
            Exec(g, "w", "out", "d");
            return g;
        }

        // ---------------- 执行与比较 ----------------

        private static void Verify(ref int total, ref int passed, string key, string label, Graph g, params string[] io)
        {
            total++;
            var cr = Compiler.Compile(g);
            if (!cr.Ok)
            {
                Debug.Log("✘ " + label + " —— Core 编译失败：" + cr.Errors[0].Message);
                return;
            }
            passed++;
            Debug.Log("✔ " + label + " —— Core 编译通过");

            string exe = null;
            string cppError = null;
            try
            {
                string root = Path.GetFullPath(Path.Combine("Temp", "arraytests", key));
                Directory.CreateDirectory(root);
                string cpp = Path.Combine(root, key + ".cpp");
                string path = Path.Combine(root, key + ".exe");
                File.WriteAllText(cpp, CppGen.Generate(cr.Program, key), Encoding.UTF8);
                string o, er;
                if (RunProcess(Gpp(), "-std=c++17 -O0 -o " + Quote(path) + " " + Quote(cpp), null, out o, out er)) exe = path;
                else cppError = "g++ " + First(er, o);
            }
            catch (Exception ex)
            {
                cppError = "生成或编译 C++ 失败：" + ex.Message;
            }

            for (int i = 0; i + 1 < io.Length; i += 2)
            {
                string input = io[i];
                string expect = io[i + 1];

                total++;
                var run = Vm.Run(cr.Program, input);
                string got = run.Ok ? (run.Output ?? "") : null;
                if (run.Ok && got == expect) { passed++; Debug.Log("✔ " + label + " [VM] —— " + Show(got)); }
                else Debug.Log("✘ " + label + " [VM] —— " + (run.Ok ? "得到「" + Show(got) + "」，期望「" + Show(expect) + "」" : run.Error.Message));

                total++;
                if (exe == null) { Debug.Log("✘ " + label + " [C++] —— " + cppError); continue; }
                string no, ne;
                if (!RunProcess(exe, "", input, out no, out ne)) { Debug.Log("✘ " + label + " [C++] —— " + First(ne, no)); continue; }
                string native = Normalize(no);
                if (native == Normalize(expect)) { passed++; Debug.Log("✔ " + label + " [C++] —— " + Show(native)); }
                else Debug.Log("✘ " + label + " [C++] —— 得到「" + Show(native) + "」，期望「" + Show(expect) + "」，VM 为「" + Show(got) + "」");
            }
        }

        private static void ExpectCompileError(ref int total, ref int passed, string label, Graph g, string expect)
        {
            total++;
            var cr = Compiler.Compile(g);
            if (!cr.Ok && cr.Errors.Exists(x => x.Message.Contains(expect)))
            {
                passed++;
                Debug.Log("✔ " + label + " 编译报错 —— " + cr.Errors[0].Message);
            }
            else Debug.Log("✘ " + label + " 编译报错 —— " + (cr.Ok ? "意外编译通过" : "错误信息里没有「" + expect + "」：" + cr.Errors[0].Message));
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

        private static void Data(Graph g, string from, string port, string to, string toPort)
        {
            g.Links.Add(new GraphLink { FromNode = from, FromPort = port, ToNode = to, ToPort = toPort, Kind = LinkKind.Data });
        }

        private static string Gpp()
        {
            const string local = "C:/Users/24585/Downloads/w64devkit/bin/g++.exe";
            return File.Exists(local) ? local : "g++";
        }

        private static string Show(string text) { return string.IsNullOrEmpty(text) ? "（空）" : text.Replace("\r\n", "\n").Replace("\n", " | "); }

        private static string First(string a, string b) { return string.IsNullOrEmpty(a) ? (string.IsNullOrEmpty(b) ? "未知错误" : b) : a; }

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

        private static string Quote(string path) { return "\"" + path.Replace("\"", "\\\"") + "\""; }
        private static string Normalize(string text) { return (text ?? "").Replace("\r\n", "\n").Trim(); }
    }
}
