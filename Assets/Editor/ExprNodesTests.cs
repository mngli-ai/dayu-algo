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
    /// 键盘表达式接入 Core 的自测：改写后的 L1、vardecl/varset/branch/forloop/whileloop 的表达式字段。
    /// 每张图先由 Core 编译，再分别用 VM 与 C++ 原生程序跑同一份输入，两者都必须等于期望值。
    /// </summary>
    public static class ExprNodesTests
    {
        // 供 Unity 批处理 -executeMethod 直接调用：Dayu.EditorTools.ExprNodesTests.RunAll
        public static void RunAll()
        {
            int total = 0, passed = 0;
            Run(ref total, ref passed);
            Debug.Log("表达式节点 共 " + total + " 项，通过 " + passed + " 项");
            if (total != passed) EditorApplication.Exit(1);
        }

        public static void Run(ref int total, ref int passed)
        {
            L1Cases(ref total, ref passed);
            VarDeclInitCases(ref total, ref passed);
            VarSetValueCases(ref total, ref passed);
            BranchCondCases(ref total, ref passed);
            ForLoopCases(ref total, ref passed);
            NestedForCases(ref total, ref passed);
            WhileCondCases(ref total, ref passed);
            CompileErrorCases(ref total, ref passed);
            TypeNameCases(ref total, ref passed);
            VardeclEmptyBarCases(ref total, ref passed);
            ArrayBoardCases(ref total, ref passed);
            StackBoardCases(ref total, ref passed);
            QueueBoardCases(ref total, ref passed);
            HandBuiltVarOrderCases(ref total, ref passed);
            VarErrorWordingCases(ref total, ref passed);
            EdgePanCases(ref total, ref passed);
        }

        // 拖到画布边缘时镜头自动平移：方向系数是纯函数，自测盯「离边多远给多快」和「画布正中不许动」。
        private static void EdgePanCases(ref int total, ref int passed)
        {
            CheckEdge(ref total, ref passed, "贴左半程", GraphView.EdgeAxis(28f, 500f).ToString("0.###"), "-0.5");
            CheckEdge(ref total, ref passed, "贴右半程", GraphView.EdgeAxis(500f, 28f).ToString("0.###"), "0.5");
            CheckEdge(ref total, ref passed, "画布正中不平移", GraphView.EdgeAxis(300f, 500f).ToString("0.###"), "0");
            CheckEdge(ref total, ref passed, "刚好在边界带上不触发", GraphView.EdgeAxis(56f, 56f).ToString("0.###"), "0");
            CheckEdge(ref total, ref passed, "越出左边界满速", GraphView.EdgeAxis(-10f, 500f).ToString("0.###"), "-1");
            CheckEdge(ref total, ref passed, "越出右边界满速", GraphView.EdgeAxis(500f, -10f).ToString("0.###"), "1");
        }

        // 「还没创建变量」这类报错必须自己把话说清楚：是名字打错了（列出图里真创建过的变量），
        // 还是创建模块排在后面（说明连线并列出此刻可用的变量），不能让玩家去猜。
        private static void VarErrorWordingCases(ref int total, ref int passed)
        {
            // ① 名字打错：图里只有 a，却去设置 z
            var typo = G("ERR-TYPO");
            typo.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dA", "vardecl", "name", "a", "vtype", "int"),
                Node("sZ", "varset", "name", "z", "valueExpr", "z = 1")
            });
            Exec(typo, "e", "out", "dA");
            Exec(typo, "dA", "out", "sZ");
            total++;
            var tr = Compiler.Compile(typo);
            var te = tr.Ok ? null : tr.Errors.Find(x => x.Message.Contains("变量「z」还没创建"));
            if (te != null && te.Message.Contains("已创建的变量：a"))
            {
                passed++;
                Debug.Log("✔ 变量名打错时列出已创建的变量 —— " + te.Message);
            }
            else Debug.Log("✘ 变量名打错时列出已创建的变量 —— " + (tr.Ok ? "意外编译通过" : tr.Errors[0].Message));

            // ② 创建模块在后面：b 已创建，a 的创建块还排在「设置 b」下面
            var late = G("ERR-LATE");
            late.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dB", "vardecl", "name", "b", "vtype", "int"),
                Node("sB", "varset", "name", "b", "valueExpr", "b = a"),
                Node("dA", "vardecl", "name", "a", "vtype", "int")
            });
            Exec(late, "e", "out", "dB");
            Exec(late, "dB", "out", "sB");
            Exec(late, "sB", "out", "dA");
            total++;
            var lr = Compiler.Compile(late);
            var le = lr.Ok ? null : lr.Errors.Find(x => x.NodeUid == "sB" && x.Message.Contains("在这里还没创建"));
            if (le != null && le.Message.Contains("此刻可用的变量：b"))
            {
                passed++;
                Debug.Log("✔ 创建块在后面时说明连线并列出可用变量 —— " + le.Message);
            }
            else Debug.Log("✘ 创建块在后面时说明连线并列出可用变量 —— " + (lr.Ok ? "意外编译通过" : lr.Errors[0].NodeUid + "：" + lr.Errors[0].Message));

            // ③ 一个变量都没创建时，不能给出空的「已创建的变量：」
            var none = G("ERR-NONE");
            none.Nodes.AddRange(new[] { Node("e", "entry"), Node("sQ", "varset", "name", "q", "valueExpr", "q = 1") });
            Exec(none, "e", "out", "sQ");
            total++;
            var nr = Compiler.Compile(none);
            var ne = nr.Ok ? null : nr.Errors.Find(x => x.Message.Contains("变量「q」还没创建"));
            if (ne != null && ne.Message.Contains("一个变量都没创建"))
            {
                passed++;
                Debug.Log("✔ 空图时直说一个变量都没创建 —— " + ne.Message);
            }
            else Debug.Log("✘ 空图时直说一个变量都没创建 —— " + (nr.Ok ? "意外编译通过" : nr.Errors[0].Message));
        }

        // 玩家手搭 P2「存一存」这张图（截图里报「变量「b」还没创建」的那张）：
        // 创建 a → 创建 b → 读入 a → 读入 b → 设置 b = 2*a → 输出 b+1。
        // 引擎侧必须认得 b；顺带钉住变量名里的空格处理：首尾空格自动收拾，中间夹空格才拦下。
        private static void HandBuiltVarOrderCases(ref int total, ref int passed)
        {
            var g = G("P2-HAND");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dA", "vardecl", "name", "a", "vtype", "int"),
                Node("dB", "vardecl", "name", "b", "vtype", "int"),
                Node("inA", "input", "name", "a", "vtype", "int"),
                Node("inB", "input", "name", "b", "vtype", "int"),
                Node("sB", "varset", "name", "b", "valueExpr", "b = 2 * a"),
                Node("o", "output", "outputExpr", "b + 1")
            });
            Exec(g, "e", "out", "dA");
            Exec(g, "dA", "out", "dB");
            Exec(g, "dB", "out", "inA");
            Exec(g, "inA", "out", "inB");
            Exec(g, "inB", "out", "sB");
            Exec(g, "sB", "out", "o");

            total++;
            var r = Compiler.Compile(g);
            if (r.Ok) { passed++; Debug.Log("✔ 手搭图变量顺序 —— 创建 b 在设置 b 之前，编译通过"); }
            else Debug.Log("✘ 手搭图变量顺序 —— " + string.Join(" / ", r.Errors.ConvertAll(x => x.NodeUid + ": " + x.Message).ToArray()));

            if (r.Ok)
            {
                // 这张图有两次「输入」，样例要喂两个数：a=3、b=5，随后 b 被改成 2*a=6，输出 b+1=7
                var run = Vm.Run(r.Program, "3 5");
                Expect(ref total, ref passed, "手搭图输入 3 5 的输出",
                    run.Ok ? Show(run.Output) : "运行失败：" + (run.Error != null ? run.Error.Message : "?"), "7");
            }
            else
            {
                total++; Debug.Log("✘ 手搭图变量顺序 —— 编译就没过，跳过运行比对");
            }

            // 变量名首尾空格是输入框里最容易带上的噪音：读取出口统一 trim，
            // 「创建 b␣」和「设置 b」必须认作同一个变量，不再谎报「还没创建」。
            var spaced = G("P2-SPACED");
            spaced.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dB", "vardecl", "name", "b ", "vtype", "int"),
                Node("sB", "varset", "name", " b", "valueExpr", "b = 2"),
                Node("o", "output", "outputExpr", "b")
            });
            Exec(spaced, "e", "out", "dB");
            Exec(spaced, "dB", "out", "sB");
            Exec(spaced, "sB", "out", "o");
            total++;
            var sr = Compiler.Compile(spaced);
            if (!sr.Ok)
                Debug.Log("✘ 变量名首尾空格自动收拾 —— " + string.Join(" / ", sr.Errors.ConvertAll(x => x.NodeUid + ": " + x.Message).ToArray()));
            else
            {
                var srun = Vm.Run(sr.Program, "");
                if (srun.Ok && Show(srun.Output) == "2")
                {
                    passed++;
                    Debug.Log("✔ 变量名首尾空格自动收拾 —— 创建 b␣ 与设置 b 认作同一变量，输出 2");
                }
                else Debug.Log("✘ 变量名首尾空格自动收拾 —— " + (srun.Ok ? "输出「" + Show(srun.Output) + "」" : "运行失败：" + (srun.Error != null ? srun.Error.Message : "?")));
            }

            // 名字中间夹空格是真正的非法标识符，必须点名拦下
            var inner = G("P2-INNER");
            inner.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dX", "vardecl", "name", "a b", "vtype", "int")
            });
            Exec(inner, "e", "out", "dX");
            total++;
            var ir = Compiler.Compile(inner);
            if (!ir.Ok && ir.Errors.Exists(x => x.NodeUid == "dX" && x.Message.Contains("变量名")))
            {
                passed++;
                Debug.Log("✔ 变量名中间夹空格被拦下 —— " + ir.Errors.Find(x => x.NodeUid == "dX").Message);
            }
            else Debug.Log("✘ 变量名中间夹空格被拦下 —— " + (ir.Ok ? "意外编译通过" : string.Join(" / ", ir.Errors.ConvertAll(x => x.NodeUid + ": " + x.Message).ToArray())));
        }

        // 类型显示名：中文在外、英文原名进括号，数组元素类型在中文里带「数组」二字。
        private static void TypeNameCases(ref int total, ref int passed)
        {
            Expect(ref total, ref passed, "int", NodeTypes.TypeNameCn("int"), "整数 (int)");
            Expect(ref total, ref passed, "ll", NodeTypes.TypeNameCn("ll"), "长整数 (ll)");
            Expect(ref total, ref passed, "double", NodeTypes.TypeNameCn("double"), "小数 (double)");
            Expect(ref total, ref passed, "bool", NodeTypes.TypeNameCn("bool"), "布尔 (bool)");
            Expect(ref total, ref passed, "char", NodeTypes.TypeNameCn("char"), "字符 (char)");
            Expect(ref total, ref passed, "string", NodeTypes.TypeNameCn("string"), "字符串 (string)");
            Expect(ref total, ref passed, "int[]", NodeTypes.TypeNameCn("int[]"), "整数数组 (int[])");
            Expect(ref total, ref passed, "char[]", NodeTypes.TypeNameCn("char[]"), "字符数组 (char[])");
            // 空类型按 int 处理，卡片上不会出现「( ）」这种半截标签
            Expect(ref total, ref passed, "空类型回退 int", NodeTypes.TypeNameCn(""), "整数 (int)");
        }

        // 创建变量的初始值不再占一条空白框：它写在木箱名牌「x = 0」上，卡片高度与写没写初始值无关。
        private static void VardeclEmptyBarCases(ref int total, ref int passed)
        {
            var t = NodeTypes.Get("vardecl");
            var empty = Node("v1", "vardecl", "name", "a", "vtype", "int");
            var withInit = Node("v2", "vardecl", "name", "a", "vtype", "int", "initExpr", "0");

            Expect(ref total, ref passed, "空初始值不占字段行", NodeCard.VisibleExprCount("vardecl", empty).ToString(), "0");
            Expect(ref total, ref passed, "写了初始值也不占字段行", NodeCard.VisibleExprCount("vardecl", withInit).ToString(), "0");
            float hEmpty = NodeCard.CardHeight(t, true, true, empty);
            float hInit = NodeCard.CardHeight(t, true, true, withInit);
            Expect(ref total, ref passed, "空/满卡片高度一致", (hInit - hEmpty).ToString(), "0");
            Expect(ref total, ref passed, "身份行用中文类型", NodeCard.VardeclLine(empty), "整数 (int) a");
            Expect(ref total, ref passed, "数组类型身份行", NodeCard.VardeclLine(Node("v3", "vardecl", "name", "a", "vtype", "int[]")), "整数数组 (int[]) a");
            // 名牌一行写完「x = 0」：不再上面一大行 0、下面一个 x
            Expect(ref total, ref passed, "标量名牌一行写完", ModuleViz.PlateText(withInit), "a = 0");
            Expect(ref total, ref passed, "没写初始值的名牌", ModuleViz.PlateText(empty), "a");
            Expect(ref total, ref passed, "数组名牌带长度", ModuleViz.PlateText(Node("v4", "vardecl", "name", "a", "vtype", "int[]", "size", "5")), "a[5]");
        }

        private static void Expect(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 类型名与空白条 —— " + label + " = " + actual); }
            else Debug.Log("✘ 类型名与空白条 —— " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }

        // 数组看板：下标高亮靠解析跟踪文本（写才算、读和比较都不算），排版靠纯函数（宽度→每行几格→高度）。
        private static void ArrayBoardCases(ref int total, ref int passed)
        {
            string name;
            int idx;

            Check(ref total, ref passed, "写入可解析", Dayu.Core.ArrayView.TryParseIndexedWrite("a[2] = 5", out name, out idx).ToString(), "True");
            Check(ref total, ref passed, "写入变量名", name, "a");
            Check(ref total, ref passed, "写入下标", idx.ToString(), "2");

            Check(ref total, ref passed, "多位下标", Dayu.Core.ArrayView.TryParseIndexedWrite("arr[10] = 7", out name, out idx).ToString(), "True");
            Check(ref total, ref passed, "多位下标值", idx.ToString(), "10");
            Check(ref total, ref passed, "下标是表达式时不高亮", Dayu.Core.ArrayView.TryParseIndexedWrite("a[i] = 5", out name, out idx).ToString(), "False");
            Check(ref total, ref passed, "只读不算写", Dayu.Core.ArrayView.TryParseIndexedWrite("a[2] == 5", out name, out idx).ToString(), "False");
            Check(ref total, ref passed, "取值不算写", Dayu.Core.ArrayView.TryParseIndexedWrite("sum = a[2]", out name, out idx).ToString(), "False");
            Check(ref total, ref passed, "空文本", Dayu.Core.ArrayView.TryParseIndexedWrite("", out name, out idx).ToString(), "False");
            Check(ref total, ref passed, "无下标", Dayu.Core.ArrayView.TryParseIndexedWrite("a = 5", out name, out idx).ToString(), "False");

            Check(ref total, ref passed, "元素文本 bool", Dayu.Core.ArrayView.ElementText(true), "true");
            Check(ref total, ref passed, "元素文本 double", Dayu.Core.ArrayView.ElementText(1.5d), "1.5");
            Check(ref total, ref passed, "元素文本 long", Dayu.Core.ArrayView.ElementText(42L), "42");
            Check(ref total, ref passed, "元素文本 null", Dayu.Core.ArrayView.ElementText(null), "null");

            // 排版：内宽 252（看板默认宽 264 扣两边 6）第一行让开车头挂 5 节，后面每行 6 节
            int cols, rows;
            cols = ArrayBoard.SlotsPerRow(252f, true);
            Check(ref total, ref passed, "首行让开车头", cols.ToString(), "5");
            cols = ArrayBoard.SlotsPerRow(252f, false);
            Check(ref total, ref passed, "后续行每行车厢", cols.ToString(), "6");
            rows = ArrayBoard.RowsFor(252f, 5);
            Check(ref total, ref passed, "默认宽 5 格行数", rows.ToString(), "1");
            Check(ref total, ref passed, "一行高的列车", ArrayBoard.HeightFor(rows).ToString("0.#"), "84");
            // 玩家把看板拉窄到 102（内宽 90）→ 首行 1 节、后面每行 2 节，5 格要三行，高度自己长
            rows = ArrayBoard.RowsFor(90f, 5);
            Check(ref total, ref passed, "拉窄行数", rows.ToString(), "3");
            Check(ref total, ref passed, "三行高的列车", ArrayBoard.HeightFor(rows).ToString("0.#"), "192");
            // 拉到极窄也不能 0 列；空数组留一节位置，看板不至于塌成一条
            Check(ref total, ref passed, "极窄至少一列", ArrayBoard.SlotsPerRow(20f, true).ToString(), "1");
            Check(ref total, ref passed, "空数组一行", ArrayBoard.RowsFor(20f, 0).ToString(), "1");
            // 超长数组只显示前 60 节，多出来的用「…n」标出来
            Check(ref total, ref passed, "100 格按 60 节排", ArrayBoard.RowsFor(252f, 100).ToString(), "11");
            // 第 0 节紧跟车头，第 5 节换到第二行行首
            int row; float x, y;
            ArrayBoard.SlotAt(252f, 0, out row, out x, out y);
            Check(ref total, ref passed, "第 0 节位置", row + "," + x.ToString("0.#") + "," + y.ToString("0.#"), "0,56,0");
            ArrayBoard.SlotAt(252f, 5, out row, out x, out y);
            Check(ref total, ref passed, "第 5 节换行", row + "," + x.ToString("0.#") + "," + y.ToString("0.#"), "1,0,54");
        }

        // 栈看板：一摞盘子从下往上摞，位置/高度都由纯函数算，自测盯「第几只在第几层」和「几只对应多高」。
        private static void StackBoardCases(ref int total, ref int passed)
        {
            CheckStack(ref total, ref passed, "一只盘子的高度", StackBoard.HeightFor(1).ToString("0.##"), "60");
            CheckStack(ref total, ref passed, "空栈不塌成零", StackBoard.HeightFor(0).ToString("0.##"), "60");
            CheckStack(ref total, ref passed, "三只盘子的高度", StackBoard.HeightFor(3).ToString("0.##"), "112");

            // 三只盘子内框 80：贴底的第 0 只在 -58（下沿正好压到框底），栈顶第 2 只停在 -6（离标题条留一点缝）
            CheckStack(ref total, ref passed, "栈底那只的位置", StackBoard.SlotY(80f, 0).ToString("0.##"), "-58");
            CheckStack(ref total, ref passed, "栈顶那只的位置", StackBoard.SlotY(80f, 2).ToString("0.##"), "-6");
            CheckStack(ref total, ref passed, "越靠栈顶越往上",
                (StackBoard.SlotY(80f, 0) < StackBoard.SlotY(80f, 1) && StackBoard.SlotY(80f, 1) < StackBoard.SlotY(80f, 2)).ToString(), "True");

            CheckStack(ref total, ref passed, "五只全画", StackBoard.VisibleCount(5).ToString(), "5");
            CheckStack(ref total, ref passed, "一百只截到上限", StackBoard.VisibleCount(100).ToString(), "40");
            CheckStack(ref total, ref passed, "负数当空栈", StackBoard.VisibleCount(-2).ToString(), "0");
        }

        private static void QueueBoardCases(ref int total, ref int passed)
        {
            // 车厢一节 34px + 4px 间距，再加内框留白；不足 120 按标题条最小宽度兜底：单节 120、三节 128
            CheckQueue(ref total, ref passed, "一节车厢的宽度", QueueBoard.WidthFor(1).ToString("0.##"), "120");
            CheckQueue(ref total, ref passed, "空队列不塌成零", QueueBoard.WidthFor(0).ToString("0.##"), "120");
            CheckQueue(ref total, ref passed, "三节车厢的宽度", QueueBoard.WidthFor(3).ToString("0.##"), "128");

            // 队头贴左，往右一节节排：第 0 只在 2，第 2 只在 78
            CheckQueue(ref total, ref passed, "队头那节的位置", QueueBoard.SlotX(0).ToString("0.##"), "2");
            CheckQueue(ref total, ref passed, "第三节的位置", QueueBoard.SlotX(2).ToString("0.##"), "78");
            CheckQueue(ref total, ref passed, "越靠队尾越往右",
                (QueueBoard.SlotX(0) < QueueBoard.SlotX(1) && QueueBoard.SlotX(1) < QueueBoard.SlotX(2)).ToString(), "True");

            CheckQueue(ref total, ref passed, "五节全画", QueueBoard.VisibleCount(5).ToString(), "5");
            CheckQueue(ref total, ref passed, "一百节截到上限", QueueBoard.VisibleCount(100).ToString(), "14");
            CheckQueue(ref total, ref passed, "负数当空队列", QueueBoard.VisibleCount(-2).ToString(), "0");

            // 结构种类不能混用：栈关里摆队列模块要编译报错，反过来也一样
            CheckQueue(ref total, ref passed, "栈实例用队列模块要报错", CompileStructMismatch("stack", "queue_push"), "hit");
            CheckQueue(ref total, ref passed, "队列实例用栈模块要报错", CompileStructMismatch("queue", "stack_pop"), "hit");
            CheckQueue(ref total, ref passed, "种类对上就不报", CompileStructMismatch("queue", "queue_pop"), "clean");
        }

        // 一张最小的图：entry → 创建结构 → 一个结构操作模块（value 端口由常量喂）。
        // 返回值说明「类型混用」这条报错到底有没有命中。
        private static string CompileStructMismatch(string kind, string nodeType)
        {
            var g = new Graph { LevelId = "T" };
            g.Structs.Add(new StructDef { Id = "st", Kind = kind, Elem = "int", Name = "测试结构" });
            g.Nodes.Add(new GraphNode { Uid = "e", Type = "entry" });
            g.Nodes.Add(new GraphNode { Uid = "new", Type = "structnew", Params = new System.Collections.Generic.Dictionary<string, string> { { "structId", "st" } } });
            g.Nodes.Add(new GraphNode { Uid = "op", Type = nodeType, Params = new System.Collections.Generic.Dictionary<string, string> { { "structId", "st" } } });
            g.Nodes.Add(new GraphNode { Uid = "c", Type = "const", Params = new System.Collections.Generic.Dictionary<string, string> { { "vtype", "int" }, { "value", "1" } } });
            g.Links.Add(new GraphLink { Kind = LinkKind.Exec, FromNode = "e", FromPort = "out", ToNode = "new", ToPort = "in" });
            g.Links.Add(new GraphLink { Kind = LinkKind.Exec, FromNode = "new", FromPort = "out", ToNode = "op", ToPort = "in" });
            g.Links.Add(new GraphLink { Kind = LinkKind.Data, FromNode = "c", FromPort = "value", ToNode = "op", ToPort = "value" });
            var r = Compiler.Compile(g);
            foreach (var err in r.Errors) if (err.Message.Contains("用不了")) return "hit";
            return r.Ok ? "clean" : "other";
        }

        private static void CheckQueue(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 队列看板 —— " + label + " = " + actual); }
            else Debug.Log("✘ 队列看板 —— " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }

        private static void CheckStack(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 栈看板 —— " + label + " = " + actual); }
            else Debug.Log("✘ 栈看板 —— " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }

        private static void Check(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 数组看板 —— " + label + " = " + actual); }
            else Debug.Log("✘ 数组看板 —— " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }

        private static void CheckEdge(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 边缘平移 —— " + label + " = " + actual); }
            else Debug.Log("✘ 边缘平移 —— " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }

        // ---------------- 用例 ----------------

        // 改写后的 L1：没有任何数据线，输入直接写入 a/b，输出靠 "a + b"。
        private static void L1Cases(ref int total, ref int passed)
        {
            var level = Levels.All.Find(x => x.Id == "L1");
            if (level == null)
            {
                total++;
                Debug.Log("✘ L1 表达式模式 —— 找不到 L1 关卡");
                return;
            }
            Verify(ref total, ref passed, "L1", "L1 表达式模式", level.Solution,
                "3 5", "8",
                "-2 7", "5",
                "0 0", "0");
        }

        private static void VarDeclInitCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "vardecl-init", "创建变量初始值表达式", VarDeclInitGraph(),
                "", "10");
        }

        private static void VarSetValueCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "varset-value", "赋值语句表达式", VarSetValueGraph(),
                "4", "13",
                "-2", "-5");
        }

        private static void BranchCondCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "branch-cond", "如果的条件表达式", BranchCondGraph(),
                "5", "pos",
                "-1", "neg",
                "0", "neg");
        }

        private static void ForLoopCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "for-expr", "计数循环 from/to 表达式引用 i", ForLoopGraph(),
                "5", "15",
                "1", "1",
                "0", "0");
            // 倒序：起点写大、终点写小，逐轮输出 i 必须是 3 2 1；同一张图正序时起点 > 终点，一轮都不跑。
            // 两条放一起才证明 dir 真被读进来了，而不是「恰好都能跑」。
            Verify(ref total, ref passed, "for-desc", "计数循环倒序从起点往下走", ForOrderGraph("desc"),
                "", "3\n2\n1");
            Verify(ref total, ref passed, "for-asc-empty", "计数循环正序起点大于终点时零轮", ForOrderGraph("asc"),
                "", "");
            // 旧图根本没写 dir：必须还是正序，不能因为加了开关就把老关卡跑反
            Verify(ref total, ref passed, "for-legacy-asc", "计数循环旧图默认正序", ForOrderGraph(null),
                "", "");
        }

        private static void NestedForCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "for-nested-expr", "嵌套循环 i 作用域恢复", NestedForGraph(),
                "", "1");
        }

        private static void WhileCondCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "while-expr", "条件循环每轮重求值", WhileCondGraph(),
                "", "3");
        }

        private static void CompileErrorCases(ref int total, ref int passed)
        {
            var bad = G("EXPR-SYNTAX");
            bad.Nodes.AddRange(new[] { Node("e", "entry"), Node("br", "branch", "condExpr", "1 +"), Node("o", "output", "outputExpr", "\"x\"") });
            Exec(bad, "e", "out", "br");
            Exec(bad, "br", "true", "o");
            total++;
            var cr = Compiler.Compile(bad);
            if (!cr.Ok && cr.Errors[0].Message.Contains("条件表达式"))
            {
                passed++;
                Debug.Log("✔ 表达式语法错误落到 CompileError —— " + cr.Errors[0].Message);
            }
            else Debug.Log("✘ 表达式语法错误落到 CompileError —— " + (cr.Ok ? "意外编译通过" : cr.Errors[0].Message));

            var wrong = G("EXPR-TYPE");
            wrong.Nodes.AddRange(new[] { Node("e", "entry"), Node("d", "vardecl", "name", "a", "vtype", "int"), Node("br", "branch", "condExpr", "a"), Node("o", "output", "outputExpr", "a") });
            Exec(wrong, "e", "out", "d");
            Exec(wrong, "d", "out", "br");
            Exec(wrong, "br", "true", "o");
            total++;
            var cr2 = Compiler.Compile(wrong);
            if (!cr2.Ok && cr2.Errors.Exists(x => x.Message.Contains("bool")))
            {
                passed++;
                Debug.Log("✔ 条件表达式类型不匹配 —— " + cr2.Errors[0].Message);
            }
            else Debug.Log("✘ 条件表达式类型不匹配 —— " + (cr2.Ok ? "意外编译通过" : cr2.Errors[0].Message));
        }

        // ---------------- 用例图 ----------------

        // 创建变量带初始值表达式 2 + 3，输出 a * 2。
        private static Graph VarDeclInitGraph()
        {
            var g = G("VARDECLINIT");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "a", "vtype", "int", "initExpr", "2 + 3"),
                Node("o", "output", "outputExpr", "a * 2")
            });
            Exec(g, "e", "out", "d");
            Exec(g, "d", "out", "o");
            return g;
        }

        // 读入 n，a = n * 3，输出 a + 1。
        private static Graph VarSetValueGraph()
        {
            var g = G("VARSETVALUE");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dN", "vardecl", "name", "n", "vtype", "int"),
                Node("inN", "input", "vtype", "int", "name", "n"),
                Node("d", "vardecl", "name", "a", "vtype", "int"),
                Node("s", "varset", "name", "a", "valueExpr", "n * 3"),
                Node("o", "output", "outputExpr", "a + 1")
            });
            Exec(g, "e", "out", "dN");
            Exec(g, "dN", "out", "inN");
            Exec(g, "inN", "out", "d");
            Exec(g, "d", "out", "s");
            Exec(g, "s", "out", "o");
            return g;
        }

        // 读入 a，a > 0 走真分支输出 pos，否则输出 neg（输出也用表达式）。
        private static Graph BranchCondGraph()
        {
            var g = G("BRANCHCOND");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dA", "vardecl", "name", "a", "vtype", "int"),
                Node("inA", "input", "vtype", "int", "name", "a"),
                Node("br", "branch", "condExpr", "a > 0"),
                Node("oPos", "output", "outputExpr", "\"pos\""),
                Node("oNeg", "output", "outputExpr", "\"neg\"")
            });
            Exec(g, "e", "out", "dA");
            Exec(g, "dA", "out", "inA");
            Exec(g, "inA", "out", "br");
            Exec(g, "br", "true", "oPos");
            Exec(g, "br", "false", "oNeg");
            return g;
        }

        // 读入 n，for i = 1..n：sum += i；输出 sum。from/to 与循环体都用表达式。
        private static Graph ForLoopGraph()
        {
            var g = G("FOREXPR");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("dN", "vardecl", "name", "n", "vtype", "int"),
                Node("inN", "input", "vtype", "int", "name", "n"),
                Node("declSum", "vardecl", "name", "sum", "vtype", "int", "initExpr", "0"),
                Node("loop", "forloop", "fromExpr", "1", "toExpr", "n"),
                Node("add", "varset", "name", "sum", "valueExpr", "sum + i"),
                Node("o", "output", "outputExpr", "sum")
            });
            Exec(g, "e", "out", "dN");
            Exec(g, "dN", "out", "inN");
            Exec(g, "inN", "out", "declSum");
            Exec(g, "declSum", "out", "loop");
            Exec(g, "loop", "body", "add");
            Exec(g, "loop", "done", "o");
            return g;
        }

        // 起点 3 → 终点 1，循环体逐轮输出 i：dir 决定这三次是「3 2 1」还是一次都不跑。
        // dir 传 null 用来模拟旧图——节点上压根没有这个参数。
        private static Graph ForOrderGraph(string dir)
        {
            var loop = Node("loop", "forloop", "fromExpr", "3", "toExpr", "1");
            if (dir != null) loop.Params["dir"] = dir;
            var g = G("FORORDER");
            g.Nodes.AddRange(new[] { Node("e", "entry"), loop, Node("o", "output", "outputExpr", "i") });
            Exec(g, "e", "out", "loop");
            Exec(g, "loop", "body", "o");
            return g;
        }

        // 外层 i=0..1，内层 0..0：内层结束后再 += 外层 i，最终 acc = 1；
        // 若内层把外层 i 覆盖掉，结果会变成 0（i 未被恢复）或 2。
        private static Graph NestedForGraph()
        {
            var g = G("FORNESTEDEXPR");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("declAcc", "vardecl", "name", "acc", "vtype", "int", "initExpr", "0"),
                Node("outer", "forloop", "fromExpr", "0", "toExpr", "1"),
                Node("inner", "forloop", "fromExpr", "0", "toExpr", "0"),
                Node("addInner", "varset", "name", "acc", "valueExpr", "acc + i"),
                Node("addOuter", "varset", "name", "acc", "valueExpr", "acc + i"),
                Node("o", "output", "outputExpr", "acc")
            });
            Exec(g, "e", "out", "declAcc");
            Exec(g, "declAcc", "out", "outer");
            Exec(g, "outer", "body", "inner");
            Exec(g, "inner", "body", "addInner");
            Exec(g, "inner", "done", "addOuter");
            Exec(g, "outer", "done", "o");
            return g;
        }

        // i = 0；while (i < 3) { i = i + 1 }；输出 i。
        private static Graph WhileCondGraph()
        {
            var g = G("WHILEEXPR");
            g.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("declI", "vardecl", "name", "i", "vtype", "int"),
                Node("set0", "varset", "name", "i", "valueExpr", "0"),
                Node("loop", "whileloop", "condExpr", "i < 3"),
                Node("inc", "varset", "name", "i", "valueExpr", "i + 1"),
                Node("o", "output", "outputExpr", "i")
            });
            Exec(g, "e", "out", "declI");
            Exec(g, "declI", "out", "set0");
            Exec(g, "set0", "out", "loop");
            Exec(g, "loop", "body", "inc");
            Exec(g, "loop", "done", "o");
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
                string root = Path.GetFullPath(Path.Combine("Temp", "exprnodes", key));
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
