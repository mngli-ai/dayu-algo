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
    /// 基础控制流自测：if / if-else / else-if 链 / for / while。
    /// 每个图先由 Core 编译，再分别用 VM 与 C++ 原生程序跑同一份输入，两者都必须等于期望值。
    /// </summary>
    public static class ControlFlowTests
    {
        public static void Run(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "if", "if 单分支", IfGraph(),
                "5", "start\nyes",
                "-3", "start");

            Verify(ref total, ref passed, "ifelse", "if-else", IfElseGraph(),
                "5", "pos",
                "-2", "neg",
                "0", "neg");

            Verify(ref total, ref passed, "elseif", "else-if 连续链", ElseIfGraph(),
                "-5", "A",
                "0", "B",
                "7", "C");

            Verify(ref total, ref passed, "for", "for 索引", ForIndexGraph(),
                "3", "0\n1\n2\n3",
                "0", "0",
                "-1", "");

            Verify(ref total, ref passed, "forsum", "for 累加", ForSumGraph(),
                "4", "10",
                "0", "0",
                "-1", "0");

            Verify(ref total, ref passed, "forbound", "for 上界只求值一次", ForBoundGraph(),
                "4", "-1");

            Verify(ref total, ref passed, "fornested", "for 嵌套 __index 作用域", NestedForGraph(),
                "", "0\n1");

            Verify(ref total, ref passed, "while", "while 计数", WhileCountGraph(),
                "", "0\n1\n2");

            Verify(ref total, ref passed, "whileinput", "while 条件逐轮重求值", WhileInputGraph(),
                "5", "5",
                "0", "0");

            VerifyWhileLimit(ref total, ref passed);
            VerifyForLimit(ref total, ref passed);
            VerifyLoopGuard(ref total, ref passed);
            Verify(ref total, ref passed, "bitwise", "位运算 & | ^ << >> ~", BitwiseGraph(),
                "3", "0\n2\n8",
                "10", "10\n4\n4");
            VerifyDuplicateExecOut(ref total, ref passed);
        }

        // 一个出口挂两根执行线（只会出现在旧关卡或手改的图里）：编译器必须点名，不能让玩家猜「先走哪根」
        private static void VerifyDuplicateExecOut(ref int total, ref int passed)
        {
            var g = G("DUP-EXEC");
            var e = Node("e", "entry");
            var a = Node("a", "vardecl", "name", "a", "vtype", "int");
            var b = Node("b", "vardecl", "name", "b", "vtype", "int");
            g.Nodes.AddRange(new[] { e, a, b });
            Exec(g, "e", "out", "a");
            Exec(g, "e", "out", "b");
            total++;
            var cr = Compiler.Compile(g);
            bool flagged = !cr.Ok && cr.Errors.Exists(x => x.NodeUid == "e" && x.Message.Contains("只走一条执行线"));
            if (flagged) { passed++; Debug.Log("✔ 同一出口两根执行线被拦下 —— " + cr.Errors.Find(x => x.NodeUid == "e").Message); }
            else Debug.Log("✘ 同一出口两根执行线被拦下 —— " + (cr.Ok ? "意外编译通过，玩家无从判断先走哪根" : cr.Errors[0].NodeUid + "：" + cr.Errors[0].Message));
        }

        // 三行输出把 & | ^ << >> ~ 全覆盖：VM 与 g++ 生成的原生程序必须给同一份答案
        private static Graph BitwiseGraph()
        {
            var g = G("BITWISE");
            g.ExprMode = true;
            var e = Node("e", "entry");
            var declA = Node("declA", "vardecl", "name", "a", "vtype", "int");
            var in0 = Node("in0", "input", "vtype", "int", "name", "a");
            var o1 = Node("o1", "output", "outputExpr", "(a & 12) | (a >> 2)");
            var o2 = Node("o2", "output", "outputExpr", "(a ^ 5) + ~a");
            var o3 = Node("o3", "output", "outputExpr", "1 << (a % 8)");
            g.Nodes.AddRange(new[] { e, declA, in0, o1, o2, o3 });
            Exec(g, "e", "out", "declA");
            Exec(g, "declA", "out", "in0");
            Exec(g, "in0", "out", "o1");
            Exec(g, "o1", "out", "o2");
            Exec(g, "o2", "out", "o3");
            return g;
        }

        /// <summary>仅供单独验证控制流用例（不影响 Cli.RunSelfTests 的接线）。</summary>
        public static void RunAlone()
        {
            int total = 0, passed = 0;
            Run(ref total, ref passed);
            Debug.Log("控制流：共 " + total + " 项，通过 " + passed + " 项");
            if (total != passed) EditorApplication.Exit(1);
        }

        // ---------------- 用例图 ----------------

        // if（无 else）：假分支不连任何出口，执行到此结束。
        private static Graph IfGraph()
        {
            var g = G("IF");
            var e = Node("e", "entry");
            var in0 = Node("in0", "input", "vtype", "int");
            var cStart = Node("cStart", "const", "vtype", "string", "value", "start");
            var cYes = Node("cYes", "const", "vtype", "string", "value", "yes");
            var zero = Node("zero", "const", "vtype", "int", "value", "0");
            var cmp = Node("cmp", "compare", "op", ">");
            var start = Node("start", "output");
            var yes = Node("yes", "output");
            var br = Node("br", "branch");
            g.Nodes.AddRange(new[] { e, in0, cStart, cYes, zero, cmp, start, yes, br });
            Exec(g, "e", "out", "in0");
            Exec(g, "in0", "out", "start");
            Exec(g, "start", "out", "br");
            Exec(g, "br", "true", "yes");
            Data(g, "cStart", "value", "start", "value");
            Data(g, "cYes", "value", "yes", "value");
            Data(g, "in0", "value", "cmp", "a");
            Data(g, "zero", "value", "cmp", "b");
            Data(g, "cmp", "r", "br", "cond");
            return g;
        }

        private static Graph IfElseGraph()
        {
            var g = G("IFELSE");
            var e = Node("e", "entry");
            var in0 = Node("in0", "input", "vtype", "int");
            var zero = Node("zero", "const", "vtype", "int", "value", "0");
            var cP = Node("cP", "const", "vtype", "string", "value", "pos");
            var cN = Node("cN", "const", "vtype", "string", "value", "neg");
            var cmp = Node("cmp", "compare", "op", ">");
            var br = Node("br", "branch");
            var outP = Node("outP", "output");
            var outN = Node("outN", "output");
            g.Nodes.AddRange(new[] { e, in0, zero, cP, cN, cmp, br, outP, outN });
            Exec(g, "e", "out", "in0");
            Exec(g, "in0", "out", "br");
            Exec(g, "br", "true", "outP");
            Exec(g, "br", "false", "outN");
            Data(g, "in0", "value", "cmp", "a");
            Data(g, "zero", "value", "cmp", "b");
            Data(g, "cmp", "r", "br", "cond");
            Data(g, "cP", "value", "outP", "value");
            Data(g, "cN", "value", "outN", "value");
            return g;
        }

        // else-if 链：branch.false 接到下一个 branch 的「in」。
        private static Graph ElseIfGraph()
        {
            var g = G("ELSEIF");
            var e = Node("e", "entry");
            var in0 = Node("in0", "input", "vtype", "int");
            var z1 = Node("z1", "const", "vtype", "int", "value", "0");
            var z2 = Node("z2", "const", "vtype", "int", "value", "0");
            var c1 = Node("c1", "compare", "op", "<");
            var c2 = Node("c2", "compare", "op", "==");
            var b1 = Node("b1", "branch");
            var b2 = Node("b2", "branch");
            var cA = Node("cA", "const", "vtype", "string", "value", "A");
            var cB = Node("cB", "const", "vtype", "string", "value", "B");
            var cC = Node("cC", "const", "vtype", "string", "value", "C");
            var oA = Node("oA", "output");
            var oB = Node("oB", "output");
            var oC = Node("oC", "output");
            g.Nodes.AddRange(new[] { e, in0, z1, z2, c1, c2, b1, b2, cA, cB, cC, oA, oB, oC });
            Exec(g, "e", "out", "in0");
            Exec(g, "in0", "out", "b1");
            Exec(g, "b1", "true", "oA");
            Exec(g, "b1", "false", "b2");
            Exec(g, "b2", "true", "oB");
            Exec(g, "b2", "false", "oC");
            Data(g, "in0", "value", "c1", "a");
            Data(g, "z1", "value", "c1", "b");
            Data(g, "c1", "r", "b1", "cond");
            Data(g, "in0", "value", "c2", "a");
            Data(g, "z2", "value", "c2", "b");
            Data(g, "c2", "r", "b2", "cond");
            Data(g, "cA", "value", "oA", "value");
            Data(g, "cB", "value", "oB", "value");
            Data(g, "cC", "value", "oC", "value");
            return g;
        }

        // for：循环体直接输出 __index，验证 0..to 闭区间与索引取值。
        private static Graph ForIndexGraph()
        {
            var g = G("FORINDEX");
            var e = Node("e", "entry");
            var inN = Node("inN", "input", "vtype", "int");
            var zero = Node("zero", "const", "vtype", "int", "value", "0");
            var loop = Node("loop", "forloop");
            var outn = Node("out", "output");
            g.Nodes.AddRange(new[] { e, inN, zero, loop, outn });
            Exec(g, "e", "out", "inN");
            Exec(g, "inN", "out", "loop");
            Exec(g, "loop", "body", "out");
            Data(g, "zero", "value", "loop", "from");
            Data(g, "inN", "value", "loop", "to");
            Data(g, "loop", "index", "out", "value");
            return g;
        }

        // for：sum += __index，验证索引参与运算。
        private static Graph ForSumGraph()
        {
            var g = G("FORSUM");
            var e = Node("e", "entry");
            var inN = Node("inN", "input", "vtype", "int");
            var declSum = Node("declSum", "vardecl", "name", "sum", "vtype", "int");
            var set0 = Node("set0", "varset", "name", "sum");
            var zero = Node("zero", "const", "vtype", "int", "value", "0");
            var loop = Node("loop", "forloop");
            var getSum = Node("getSum", "varget", "name", "sum");
            var add = Node("add", "binop", "op", "+");
            var setSum = Node("setSum", "varset", "name", "sum");
            var getOut = Node("getOut", "varget", "name", "sum");
            var outn = Node("out", "output");
            g.Nodes.AddRange(new[] { e, inN, declSum, set0, zero, loop, getSum, add, setSum, getOut, outn });
            Exec(g, "e", "out", "inN");
            Exec(g, "inN", "out", "declSum");
            Exec(g, "declSum", "out", "set0");
            Exec(g, "set0", "out", "loop");
            Exec(g, "loop", "body", "setSum");
            Exec(g, "loop", "done", "out");
            Data(g, "zero", "value", "set0", "value");
            Data(g, "zero", "value", "loop", "from");
            Data(g, "inN", "value", "loop", "to");
            Data(g, "getSum", "value", "add", "a");
            Data(g, "loop", "index", "add", "b");
            Data(g, "add", "r", "setSum", "value");
            Data(g, "getOut", "value", "out", "value");
            return g;
        }

        // for：上界在进入循环时求值一次，循环体改 n 不应改变轮数（VM 与 C++ 必须一致）。
        private static Graph ForBoundGraph()
        {
            var g = G("FORBOUND");
            var e = Node("e", "entry");
            var inN = Node("inN", "input", "vtype", "int");
            var declN = Node("declN", "vardecl", "name", "n", "vtype", "int");
            var setInit = Node("setInit", "varset", "name", "n");
            var zero = Node("zero", "const", "vtype", "int", "value", "0");
            var loop = Node("loop", "forloop");
            var getFrom = Node("getFrom", "varget", "name", "n");
            var one = Node("one", "const", "vtype", "int", "value", "1");
            var minus = Node("minus", "binop", "op", "-");
            var setN = Node("setN", "varset", "name", "n");
            var getOut = Node("getOut", "varget", "name", "n");
            var outn = Node("out", "output");
            g.Nodes.AddRange(new[] { e, inN, declN, setInit, zero, loop, getFrom, one, minus, setN, getOut, outn });
            Exec(g, "e", "out", "inN");
            Exec(g, "inN", "out", "declN");
            Exec(g, "declN", "out", "setInit");
            Exec(g, "setInit", "out", "loop");
            Exec(g, "loop", "body", "setN");
            Exec(g, "loop", "done", "out");
            Data(g, "inN", "value", "setInit", "value");
            Data(g, "zero", "value", "loop", "from");
            Data(g, "getFrom", "value", "loop", "to");
            Data(g, "getFrom", "value", "minus", "a");
            Data(g, "one", "value", "minus", "b");
            Data(g, "minus", "r", "setN", "value");
            Data(g, "getOut", "value", "out", "value");
            return g;
        }

        // 嵌套 for：内层循环结束后读到的 __index 必须是外层索引（C++ 里靠作用域遮蔽，VM 里靠保存/恢复）。
        private static Graph NestedForGraph()
        {
            var g = G("FORNESTED");
            var e = Node("e", "entry");
            var z1 = Node("z1", "const", "vtype", "int", "value", "0");
            var z2 = Node("z2", "const", "vtype", "int", "value", "0");
            var one1 = Node("one1", "const", "vtype", "int", "value", "1");
            var one2 = Node("one2", "const", "vtype", "int", "value", "1");
            var outer = Node("outer", "forloop");
            var inner = Node("inner", "forloop");
            var outn = Node("out", "output");
            // 内层循环体接一个赋值：第十五轮起「循环体空着」在编译期就报错，这条用例要能照常建图。
            var declT = Node("declT", "vardecl", "name", "t", "vtype", "int", "initExpr", "0");
            var bump = Node("bump", "varset", "name", "t", "valueExpr", "t + 1");
            g.Nodes.AddRange(new[] { e, z1, z2, one1, one2, declT, outer, inner, bump, outn });
            Exec(g, "e", "out", "declT");
            Exec(g, "declT", "out", "outer");
            Exec(g, "outer", "body", "inner");
            Exec(g, "inner", "body", "bump");
            Exec(g, "inner", "done", "out");
            Data(g, "z1", "value", "outer", "from");
            Data(g, "one1", "value", "outer", "to");
            Data(g, "z2", "value", "inner", "from");
            Data(g, "one2", "value", "inner", "to");
            Data(g, "outer", "index", "out", "value");
            return g;
        }

        // while：i < 3 时每轮输出 i 并自增，验证条件逐轮重求值。
        private static Graph WhileCountGraph()
        {
            var g = G("WHILECOUNT");
            var e = Node("e", "entry");
            var declI = Node("declI", "vardecl", "name", "i", "vtype", "int");
            var set0 = Node("set0", "varset", "name", "i");
            var zero = Node("zero", "const", "vtype", "int", "value", "0");
            var three = Node("three", "const", "vtype", "int", "value", "3");
            var one = Node("one", "const", "vtype", "int", "value", "1");
            var getCmp = Node("getCmp", "varget", "name", "i");
            var cmp = Node("cmp", "compare", "op", "<");
            var loop = Node("loop", "whileloop");
            var getOut = Node("getOut", "varget", "name", "i");
            var outn = Node("out", "output");
            var getAdd = Node("getAdd", "varget", "name", "i");
            var add = Node("add", "binop", "op", "+");
            var setI = Node("setI", "varset", "name", "i");
            g.Nodes.AddRange(new[] { e, declI, set0, zero, three, one, getCmp, cmp, loop, getOut, outn, getAdd, add, setI });
            Exec(g, "e", "out", "declI");
            Exec(g, "declI", "out", "set0");
            Exec(g, "set0", "out", "loop");
            Exec(g, "loop", "body", "out");
            Exec(g, "out", "out", "setI");
            Data(g, "zero", "value", "set0", "value");
            Data(g, "getCmp", "value", "cmp", "a");
            Data(g, "three", "value", "cmp", "b");
            Data(g, "cmp", "r", "loop", "cond");
            Data(g, "getOut", "value", "out", "value");
            Data(g, "getAdd", "value", "add", "a");
            Data(g, "one", "value", "add", "b");
            Data(g, "add", "r", "setI", "value");
            return g;
        }

        // while：条件依赖循环体里被改写的变量（x > 0），若条件被缓存就会死循环或算错。
        private static Graph WhileInputGraph()
        {
            var g = G("WHILEINPUT");
            var e = Node("e", "entry");
            var inX = Node("inX", "input", "vtype", "int");
            var declX = Node("declX", "vardecl", "name", "x", "vtype", "int");
            var setX = Node("setX", "varset", "name", "x");
            var declC = Node("declC", "vardecl", "name", "cnt", "vtype", "int");
            var setC = Node("setC", "varset", "name", "cnt");
            var zero = Node("zero", "const", "vtype", "int", "value", "0");
            var one = Node("one", "const", "vtype", "int", "value", "1");
            var getCond = Node("getCond", "varget", "name", "x");
            var cmp = Node("cmp", "compare", "op", ">");
            var loop = Node("loop", "whileloop");
            var getSub = Node("getSub", "varget", "name", "x");
            var sub = Node("sub", "binop", "op", "-");
            var bodyX = Node("bodyX", "varset", "name", "x");
            var getInc = Node("getInc", "varget", "name", "cnt");
            var inc = Node("inc", "binop", "op", "+");
            var bodyC = Node("bodyC", "varset", "name", "cnt");
            var getOut = Node("getOut", "varget", "name", "cnt");
            var outn = Node("out", "output");
            g.Nodes.AddRange(new[] { e, inX, declX, setX, declC, setC, zero, one, getCond, cmp, loop, getSub, sub, bodyX, getInc, inc, bodyC, getOut, outn });
            Exec(g, "e", "out", "inX");
            Exec(g, "inX", "out", "declX");
            Exec(g, "declX", "out", "setX");
            Exec(g, "setX", "out", "declC");
            Exec(g, "declC", "out", "setC");
            Exec(g, "setC", "out", "loop");
            Exec(g, "loop", "body", "bodyX");
            Exec(g, "bodyX", "out", "bodyC");
            Exec(g, "loop", "done", "out");
            Data(g, "inX", "value", "setX", "value");
            Data(g, "zero", "value", "setC", "value");
            Data(g, "getCond", "value", "cmp", "a");
            Data(g, "zero", "value", "cmp", "b");
            Data(g, "cmp", "r", "loop", "cond");
            Data(g, "getSub", "value", "sub", "a");
            Data(g, "one", "value", "sub", "b");
            Data(g, "sub", "r", "bodyX", "value");
            Data(g, "getInc", "value", "inc", "a");
            Data(g, "one", "value", "inc", "b");
            Data(g, "inc", "r", "bodyC", "value");
            Data(g, "getOut", "value", "out", "value");
            return g;
        }

        // ---------------- 步数上限 ----------------

        // while(true) 且循环体没接线：步数不会再增长，必须靠迭代上限兜底，否则 VM 会挂死。
        private static void VerifyWhileLimit(ref int total, ref int passed)
        {
            var g = G("WHILELIMIT");
            var e = Node("e", "entry");
            var loop = Node("loop", "whileloop");
            var tru = Node("tru", "const", "vtype", "bool", "value", "true");
            // 循环体现在必须在编译期就接上模块（空着会被 Compiler 点名），所以体里放一个输出，
            // 照样能验「无限循环必须撞步数上限」这条判据本身。
            var body = Node("body", "output", "outputExpr", "1");
            g.Nodes.AddRange(new[] { e, loop, tru, body });
            Exec(g, "e", "out", "loop");
            Exec(g, "loop", "body", "body");
            Data(g, "tru", "value", "loop", "cond");

            total++;
            var cr = Compiler.Compile(g);
            if (!cr.Ok) { Debug.Log("✘ while 步数上限 —— Core 编译失败：" + cr.Errors[0].Message); return; }
            var run = Vm.Run(cr.Program, "", 500);
            if (!run.Ok && run.Error != null && run.Error.Message.Contains("步数超限"))
            {
                passed++;
                Debug.Log("✔ while 步数上限 —— 每轮只输出的死循环被判为步数超限");
            }
            else Debug.Log("✘ while 步数上限 —— " + (run.Ok ? "没有触发上限，输出「" + Show(run.Output) + "」" : run.Error.Message));
        }

        // for 0..1000000：同样必须有迭代上限（循环体接一个输出，避开「体空着编译不过」）。
        private static void VerifyForLimit(ref int total, ref int passed)
        {
            var g = G("FORLIMIT");
            var e = Node("e", "entry");
            var loop = Node("loop", "forloop");
            var zero = Node("zero", "const", "vtype", "int", "value", "0");
            var big = Node("big", "const", "vtype", "int", "value", "1000000");
            var body = Node("body", "output", "outputExpr", "1");
            g.Nodes.AddRange(new[] { e, loop, zero, big, body });
            Exec(g, "e", "out", "loop");
            Exec(g, "loop", "body", "body");
            Data(g, "zero", "value", "loop", "from");
            Data(g, "big", "value", "loop", "to");

            total++;
            var cr = Compiler.Compile(g);
            if (!cr.Ok) { Debug.Log("✘ for 步数上限 —— Core 编译失败：" + cr.Errors[0].Message); return; }
            var run = Vm.Run(cr.Program, "");
            if (!run.Ok && run.Error != null && run.Error.Message.Contains("步数超限"))
            {
                passed++;
                Debug.Log("✔ for 步数上限 —— 百万轮循环被判为步数超限");
            }
            else Debug.Log("✘ for 步数上限 —— " + (run.Ok ? "没有触发上限，输出「" + Show(run.Output) + "」" : run.Error.Message));
        }

        // 第十五轮的现场坑：起点跨过终点时，for 的守卫当场不成立，循环 0 轮静默跳过，
        // 玩家只看见「直接到下一步」，误以为程序坏了（S2 那次两个循环各 0 轮、全程 8 条事件）。
        // 现在三件事必须成立：0 轮要发一条看得见的事件、事件要把起点/终点/方向写全、循环体空着要在编译期拦住。
        private static void VerifyLoopGuard(ref int total, ref int passed)
        {
            CheckEmptyRounds(ref total, ref passed, "顺序且起点大于终点", EmptyRoundsGraph("LOOP-EMPTY-ASC", "3", "1", null), "起点 3", "终点 1", "顺序");
            CheckEmptyRounds(ref total, ref passed, "倒序且起点小于终点", EmptyRoundsGraph("LOOP-EMPTY-DESC", "1", "3", "desc"), "起点 1", "终点 3", "倒序");

            // 终点连了一张排在循环体里、还没执行过的输入卡：数据线取到空值折算成 0，
            // 这种「起点 1、终点 0」以前同样静默跳过，现在事件里必须看得见终点是 0。
            var stale = G("LOOP-STALE-TO");
            stale.ExprMode = true;
            var loop = Node("loop", "forloop", "fromExpr", "1", "indexName", "i");
            var inT = Node("inT", "input", "vtype", "int");
            var tail = Node("tail", "output", "outputExpr", "ok");
            var declOk = Node("declOk", "vardecl", "name", "ok", "vtype", "int", "initExpr", "7");
            stale.Nodes.AddRange(new[] { Node("e", "entry"), loop, inT, tail, declOk });
            Exec(stale, "e", "out", "loop");
            Exec(stale, "loop", "body", "inT");
            Exec(stale, "loop", "done", "declOk");
            Exec(stale, "declOk", "out", "tail");
            Data(stale, "inT", "value", "loop", "to");
            total++;
            var staleC = Compiler.Compile(stale);
            if (!staleC.Ok) { Debug.Log("✘ 循环 0 轮看得见（终点取到未执行的输入）—— Core 编译失败：" + staleC.Errors[0].Message); }
            else
            {
                passed++;
                Debug.Log("✔ 循环 0 轮看得见（终点取到未执行的输入）—— Core 编译通过");
                total++;
                var staleRun = Vm.Run(staleC.Program, "4");
                TraceEvent staleHit = null;
                foreach (var ev in staleRun.Events) if (ev.Kind == "loopEmpty") { staleHit = ev; break; }
                if (!staleRun.Ok) Debug.Log("✘ 循环 0 轮看得见（终点取到未执行的输入）—— 运行报错：" + staleRun.Error.Message);
                else if (staleRun.Output != "7") Debug.Log("✘ 循环 0 轮看得见（终点取到未执行的输入）—— 循环后面的模块没照常执行，输出「" + Show(staleRun.Output) + "」，期望「7」");
                else if (staleHit == null) Debug.Log("✘ 循环 0 轮看得见（终点取到未执行的输入）—— 没有 0 轮事件，玩家依旧只看见「直接到下一步」");
                else if (!staleHit.Text.Contains("终点 0")) Debug.Log("✘ 循环 0 轮看得见（终点取到未执行的输入）—— 事件没写明终点是 0：「" + staleHit.Text + "」");
                else { passed++; Debug.Log("✔ 循环 0 轮看得见（终点取到未执行的输入）—— 一圈没转、后面照常走：" + staleHit.Text); }
            }

            // 循环体出口一根线都没接：编译期就得点名是哪个出口，不能等运行时把轮次事件发满却什么都不做。
            var bare = G("LOOP-NO-BODY");
            bare.ExprMode = true;
            bare.Nodes.AddRange(new[] { Node("e", "entry"), Node("loop", "forloop", "fromExpr", "0", "toExpr", "2", "indexName", "i") });
            Exec(bare, "e", "out", "loop");
            total++;
            var bareC = Compiler.Compile(bare);
            var bareHit = bareC.Errors.Find(x => x.NodeUid == "loop" && x.Message.Contains("循环体"));
            if (bareHit != null) { passed++; Debug.Log("✔ 循环体空着编译期拦住 —— " + bareHit.Message); }
            else Debug.Log("✘ 循环体空着编译期拦住 —— " + (bareC.Ok ? "意外编译通过，玩家只会看见循环空转" : bareC.Errors[0].NodeUid + "：" + bareC.Errors[0].Message));

            // 条件循环同一条规矩
            var bareW = G("WHILE-NO-BODY");
            bareW.ExprMode = true;
            bareW.Nodes.AddRange(new[] { Node("e", "entry"), Node("loop", "whileloop", "condExpr", "1 > 0") });
            Exec(bareW, "e", "out", "loop");
            total++;
            var bareWC = Compiler.Compile(bareW);
            var bareWHit = bareWC.Errors.Find(x => x.NodeUid == "loop" && x.Message.Contains("循环体"));
            if (bareWHit != null) { passed++; Debug.Log("✔ 条件循环体空着编译期拦住 —— " + bareWHit.Message); }
            else Debug.Log("✘ 条件循环体空着编译期拦住 —— " + (bareWC.Ok ? "意外编译通过" : bareWC.Errors[0].NodeUid + "：" + bareWC.Errors[0].Message));
        }

        // 起点、终点、方向三个参数搭一个「注定 0 轮」的循环，体里放一个输出，0 轮时它一次都不该执行。
        private static Graph EmptyRoundsGraph(string id, string from, string to, string dir)
        {
            var g = G(id);
            g.ExprMode = true;
            var loop = dir == null
                ? Node("loop", "forloop", "fromExpr", from, "toExpr", to, "indexName", "i")
                : Node("loop", "forloop", "fromExpr", from, "toExpr", to, "indexName", "i", "dir", dir);
            g.Nodes.AddRange(new[] { Node("e", "entry"), loop, Node("body", "output", "outputExpr", "1") });
            Exec(g, "e", "out", "loop");
            Exec(g, "loop", "body", "body");
            return g;
        }

        private static void CheckEmptyRounds(ref int total, ref int passed, string label, Graph g, string wantFrom, string wantTo, string wantDir)
        {
            total++;
            var cr = Compiler.Compile(g);
            if (!cr.Ok) { Debug.Log("✘ 循环 0 轮看得见（" + label + "）—— Core 编译失败：" + cr.Errors[0].Message); return; }
            passed++;
            Debug.Log("✔ 循环 0 轮看得见（" + label + "）—— Core 编译通过");

            total++;
            var run = Vm.Run(cr.Program, "");
            if (!run.Ok) { Debug.Log("✘ 循环 0 轮看得见（" + label + "）—— 运行报错：" + run.Error.Message); return; }
            // 循环体里那块输出一旦执行过就说明轮数根本不是 0，这条判据要能抓出假阴性
            if (!string.IsNullOrEmpty(run.Output)) { Debug.Log("✘ 循环 0 轮看得见（" + label + "）—— 循环体执行了，输出「" + Show(run.Output) + "」"); return; }
            TraceEvent hit = null;
            foreach (var ev in run.Events) if (ev.Kind == "loopEmpty") { hit = ev; break; }
            if (hit == null) { Debug.Log("✘ 循环 0 轮看得见（" + label + "）—— 没有 0 轮事件，玩家只看见「直接到下一步」"); return; }
            if (!hit.Text.Contains(wantFrom) || !hit.Text.Contains(wantTo) || !hit.Text.Contains(wantDir))
            { Debug.Log("✘ 循环 0 轮看得见（" + label + "）—— 事件缺「" + wantFrom + "/" + wantTo + "/" + wantDir + "」：「" + hit.Text + "」"); return; }
            passed++;
            Debug.Log("✔ 循环 0 轮看得见（" + label + "）—— " + hit.Text);
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
                string root = Path.GetFullPath(Path.Combine("Temp", "controlflow", key));
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
                if (run.Ok && got == expect) { passed++; Debug.Log("✔ " + label + " [VM] 输入「" + Show(input) + "」—— " + Show(got)); }
                else Debug.Log("✘ " + label + " [VM] 输入「" + Show(input) + "」—— " + (run.Ok ? "得到「" + Show(got) + "」，期望「" + Show(expect) + "」" : run.Error.Message));

                total++;
                if (exe == null) { Debug.Log("✘ " + label + " [C++] 输入「" + Show(input) + "」—— " + cppError); continue; }
                string no, ne;
                if (!RunProcess(exe, "", input, out no, out ne)) { Debug.Log("✘ " + label + " [C++] 输入「" + Show(input) + "」—— " + First(ne, no)); continue; }
                string native = Normalize(no);
                if (native == expect) { passed++; Debug.Log("✔ " + label + " [C++] 输入「" + Show(input) + "」—— " + Show(native)); }
                else Debug.Log("✘ " + label + " [C++] 输入「" + Show(input) + "」—— 得到「" + Show(native) + "」，期望「" + Show(expect) + "」，VM 为「" + Show(got) + "」");
            }
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
