using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// 容器成员访问自测：栈 / 队列实例写 st.size / st.empty / st.top / q.front，数组补上 a.size（a.length 照旧）。
    /// 补全浮窗补出来的东西必须真能用，所以每条链都要单独盯住：类型检查给得出类型、
    /// 虚拟机算得出值（空容器只说一句人话，不许抛异常）、C++ 出码编得过且和 VM 给出同一个答案。
    /// </summary>
    public static class StructMemberTests
    {
        // 供 Unity 批处理 -executeMethod 直接调用：Dayu.EditorTools.StructMemberTests.RunAll
        public static void RunAll()
        {
            int total = 0, passed = 0;
            Run(ref total, ref passed);
            Debug.Log("容器成员 共 " + total + " 项，通过 " + passed + " 项");
            if (total != passed) EditorApplication.Exit(1);
        }

        public static void Run(ref int total, ref int passed)
        {
            CheckCases(ref total, ref passed);
            EvalCases(ref total, ref passed);
            CppCases(ref total, ref passed);
            RunCases(ref total, ref passed);
            PaletteCases(ref total, ref passed);
        }

        // ---------------- 类型检查 ----------------

        // 类型表里容器的写法：栈 = stack<元素>，队列 = queue<元素>，数组沿用 int[]。
        static Dictionary<string, string> Types()
        {
            return new Dictionary<string, string>
            {
                { "st", "stack<int>" }, { "q", "queue<int>" },
                { "cs", "stack<char>" }, { "dq", "queue<double>" },
                { "a", "int[]" }, { "n", "int" }
            };
        }

        private static void CheckCases(ref int total, ref int passed)
        {
            var t = Types();
            TypeGood(ref total, ref passed, "STKMEM-CHK-SIZE", t, "st.size", "int");
            TypeGood(ref total, ref passed, "STKMEM-CHK-EMPTY", t, "st.empty", "bool");
            TypeGood(ref total, ref passed, "STKMEM-CHK-TOP", t, "st.top", "int");
            TypeGood(ref total, ref passed, "STKMEM-CHK-QFRONT", t, "q.front", "int");
            TypeGood(ref total, ref passed, "STKMEM-CHK-QSIZE", t, "q.size", "int");
            TypeGood(ref total, ref passed, "STKMEM-CHK-QEMPTY", t, "q.empty", "bool");
            TypeGood(ref total, ref passed, "STKMEM-CHK-ASSIZE", t, "a.size", "int");
            // length 是数组上 size 的同义：老关卡的解法还写着 a.length，不能一改动就报未支持
            TypeGood(ref total, ref passed, "STKMEM-CHK-ASLENGTH", t, "a.length", "int");
            TypeGood(ref total, ref passed, "STKMEM-CHK-TOPCHAR", t, "cs.top", "char");
            TypeGood(ref total, ref passed, "STKMEM-CHK-FRONTDOUBLE", t, "dq.front", "double");
            TypeGood(ref total, ref passed, "STKMEM-CHK-COMPOUND", t, "st.size + q.size >= 0", "bool");
            TypeGood(ref total, ref passed, "STKMEM-CHK-TOPMATH", t, "st.top + 1 > q.front", "bool");
            // 函数式写法一并放开：容器和数组一样能数个数
            TypeGood(ref total, ref passed, "STKMEM-CHK-LEN-CALL", t, "len(st) > 0", "bool");

            // 种类不能串：队列没有栈顶，栈没有队头
            TypeBad(ref total, ref passed, "STKMEM-CHK-QUEUE-TOP", "q.top", "没有成员「top」");
            TypeBad(ref total, ref passed, "STKMEM-CHK-QUEUE-TOP-LIST", "q.top", "front（队头元素）");
            TypeBad(ref total, ref passed, "STKMEM-CHK-STACK-FRONT", "st.front", "top（栈顶元素）");
            // 词表里没有的名字，报错要把这颗类型真支持的成员列出来
            TypeBad(ref total, ref passed, "STKMEM-CHK-UNKNOWN", "st.pop", "只有 size（个数）、empty（空了）、top（栈顶元素）");
            TypeBad(ref total, ref passed, "STKMEM-CHK-NO-LENGTH", "st.length", "只有 size");
            TypeBad(ref total, ref passed, "STKMEM-CHK-SCALAR", "n.empty", "不是数组或容器");
            TypeBad(ref total, ref passed, "STKMEM-CHK-INDEX", "q[0]", "不是数组");
            // 整颗容器当结果：C++ 那边 cout 不出来，编译期就该拦住
            TypeBad(ref total, ref passed, "STKMEM-CHK-WHOLE", "st", "不能是整个容器");
        }

        private static void TypeGood(ref int total, ref int passed, string id, Dictionary<string, string> types, string src, string want)
        {
            var r = ExprLang.Check(src, types);
            Check(ref total, ref passed, id + " 类型检查「" + src + "」", r.Ok ? r.Type : "报错：" + r.Error, want);
        }

        private static void TypeBad(ref int total, ref int passed, string id, string src, string want)
        {
            var r = ExprLang.Check(src, Types());
            CheckHas(ref total, ref passed, id + " 类型检查拦下「" + src + "」", r.Ok ? "意外通过：" + r.Type : r.Error, want);
        }

        // ---------------- 虚拟机求值 ----------------

        private static Dictionary<string, object> Scalars()
        {
            return new Dictionary<string, object> { { "n", 5L } };
        }

        private static Dictionary<string, List<object>> Arrays()
        {
            return new Dictionary<string, List<object>> { { "a", new List<object> { 1L, 2L, 3L, 4L } } };
        }

        // 三格的栈 / 队列各一颗，再备一颗空的：取元素的报错口径就靠空的这一颗盯住
        private static Dictionary<string, ExprContainer> Containers()
        {
            var full = new List<object> { 10L, 20L, 30L };
            return new Dictionary<string, ExprContainer>
            {
                { "st", new ExprContainer("stack<int>", full) },
                { "q", new ExprContainer("queue<int>", new List<object> { 10L, 20L, 30L }) },
                { "es", new ExprContainer("stack<int>", new List<object>()) },
                { "eq", new ExprContainer("queue<int>", new List<object>()) }
            };
        }

        private static void EvalCases(ref int total, ref int passed)
        {
            EvalCase(ref total, ref passed, "STKMEM-EVAL-TOP", "st.top", "30");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-FRONT", "q.front", "10");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-SIZE", "st.size", "3");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-QSIZE", "q.size", "3");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-EMPTY", "es.empty", "true");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-NOTEMPTY", "st.empty", "false");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-MATH", "st.top - q.front", "20");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-COND", "!st.empty && st.size > 2", "true");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-LEN", "len(st)", "3");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-SIZE-CALL", "size(q)", "3");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-ARR-SIZE", "a.size", "4");
            EvalCase(ref total, ref passed, "STKMEM-EVAL-ARR-LENGTH", "a.length", "4");
            // 空容器取元素：说一句人话，不许抛异常把整局跑断
            EvalFail(ref total, ref passed, "STKMEM-EVAL-EMPTY-TOP", "es.top", "栈为空，无法查看顶端。");
            EvalFail(ref total, ref passed, "STKMEM-EVAL-EMPTY-FRONT", "eq.front", "队列为空，无法查看队头。");
            EvalFail(ref total, ref passed, "STKMEM-EVAL-STACK-FRONT", "st.front", "没有成员「front」");
            EvalFail(ref total, ref passed, "STKMEM-EVAL-NO-LENGTH", "st.length", "没有成员「length」");
            EvalFail(ref total, ref passed, "STKMEM-EVAL-SCALAR", "n.size", "不是数组也不是容器");
        }

        private static void EvalCase(ref int total, ref int passed, string id, string src, string want)
        {
            var r = ExprLang.Eval(ExprLang.Parse(src).Root, Scalars(), Arrays(), Containers());
            Check(ref total, ref passed, id + " 求值「" + src + "」", r.Ok ? Show(r.Value) : "报错：" + r.Error, want);
        }

        private static void EvalFail(ref int total, ref int passed, string id, string src, string want)
        {
            var r = ExprLang.Eval(ExprLang.Parse(src).Root, Scalars(), Arrays(), Containers());
            CheckHas(ref total, ref passed, id + " 求值「" + src + "」报错", r.Ok ? "意外算出 " + Show(r.Value) : r.Error, want);
        }

        private static string Show(object v)
        {
            if (v == null) return "（空）";
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is double) return ((double)v).ToString("0.####", CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        // ---------------- C++ 出码 ----------------

        private static void CppCases(ref int total, ref int passed)
        {
            var t = Types();
            CppCase(ref total, ref passed, "STKMEM-CPP-TOP", t, "st.top", "(st).back()");
            CppCase(ref total, ref passed, "STKMEM-CPP-FRONT", t, "q.front", "(q).front()");
            CppCase(ref total, ref passed, "STKMEM-CPP-SIZE", t, "st.size", "(st).size()");
            CppCase(ref total, ref passed, "STKMEM-CPP-QEMPTY", t, "q.empty", "(q).empty()");
            CppCase(ref total, ref passed, "STKMEM-CPP-ARR-SIZE", t, "a.size", "(a).size()");
            CppCase(ref total, ref passed, "STKMEM-CPP-ARR-LENGTH", t, "a.length", "(a).size()");
            CppCase(ref total, ref passed, "STKMEM-CPP-LEN", t, "len(st)", "(st).size()");
            // 个数是 size_t，不套 long long 会和 VM 的整数对不上；取元素那颗不能套，套了字符就变数字
            CppCase(ref total, ref passed, "STKMEM-CPP-SIZE-CAST", t, "st.size", "(long long)");
            CppHasNot(ref total, ref passed, "STKMEM-CPP-NO-CAST", t, "cs.top", "(long long)");

            total++;
            string bySize = ExprLang.Cpp(ExprLang.ParseOrThrow("a.size"), t);
            string byLength = ExprLang.Cpp(ExprLang.ParseOrThrow("a.length"), t);
            if (bySize == byLength) { passed++; Debug.Log("✔ STKMEM-CPP-ALIAS-SAME size 与 length 出码同一句 —— " + bySize); }
            else Debug.Log("✘ STKMEM-CPP-ALIAS-SAME size 与 length 出码不一致 —— " + bySize + " / " + byLength);
        }

        private static void CppCase(ref int total, ref int passed, string id, Dictionary<string, string> types, string src, string want)
        {
            string cpp = ExprLang.Cpp(ExprLang.Parse(src).Root, types);
            CheckHas(ref total, ref passed, id + " 出码「" + src + "」", cpp, want);
        }

        private static void CppHasNot(ref int total, ref int passed, string id, Dictionary<string, string> types, string src, string unwanted)
        {
            string cpp = ExprLang.Cpp(ExprLang.Parse(src).Root, types);
            total++;
            if (cpp.IndexOf(unwanted, StringComparison.Ordinal) < 0) { passed++; Debug.Log("✔ " + id + " 出码「" + src + "」不带 " + unwanted + " —— " + cpp); }
            else Debug.Log("✘ " + id + " 出码「" + src + "」不该带 " + unwanted + " —— " + cpp);
        }

        // ---------------- 整图跑通：VM 与 g++ 对拍 ----------------

        private static void RunCases(ref int total, ref int passed)
        {
            Verify(ref total, ref passed, "STKMEM-STACK-E2E", "栈成员压入与读出", StackMemberGraph(),
                "", "2\n2\nfalse\n2");
            Verify(ref total, ref passed, "STKMEM-QUEUE-E2E", "队列成员读出与出队后队头", QueueMemberGraph(),
                "", "3\n2\n4\nfalse");
            Verify(ref total, ref passed, "STKMEM-ARRAY-E2E", "a.size 与 a.length 同值", ArraySizeGraph(),
                "", "3\n3");

            // 空栈取栈顶：VM 要拦住并说人话，不能默默给个 0 让玩家对不上答案
            total++;
            var late = Compiler.Compile(TopOfEmptyGraph());
            if (!late.Ok) Debug.Log("✘ STKMEM-EMPTY-RUN —— Core 编译失败：" + late.Errors[0].Message);
            else
            {
                var run = Vm.Run(late.Program, "");
                if (!run.Ok && run.Error != null && run.Error.Message.Contains("栈为空，无法查看顶端。"))
                {
                    passed++;
                    Debug.Log("✔ STKMEM-EMPTY-RUN —— " + run.Error.Message);
                }
                else Debug.Log("✘ STKMEM-EMPTY-RUN —— " + (run.Ok ? "意外跑通：" + run.Output : run.Error == null ? "无错误信息" : run.Error.Message));
            }

            // 创建栈的卡还没连进来：容器实例此刻不可见，编译期就该说「在这里还没创建」
            ExpectCompileError(ref total, ref passed, "STKMEM-BEFORE-CREATE", "还没创建", CreateAfterUseGraph());
            ExpectCompileError(ref total, ref passed, "STKMEM-BAD-MEMBER", "没有成员「pop」", BadMemberGraph());
            ExpectCompileError(ref total, ref passed, "STKMEM-WRONG-CONTAINER", "front（队头元素）", TopOfQueueGraph());
        }

        // entry → 创建栈 → 压 7 → 压「st.size + 1」→ 依次输出栈顶、个数、空了、len(st)
        private static Graph StackMemberGraph()
        {
            var g = StructGraph("STKMEM-STACK-E2E", "stack", "st");
            g.Nodes.AddRange(new[]
            {
                Nd("push1", "stack_push", "structId", "st", "pushExpr", "7"),
                Nd("push2", "stack_push", "structId", "st", "pushExpr", "st.size + 1"),
                Nd("o1", "output", "outputExpr", "st.top"),
                Nd("o2", "output", "outputExpr", "st.size"),
                Nd("o3", "output", "outputExpr", "st.empty"),
                Nd("o4", "output", "outputExpr", "len(st)")
            });
            Link(g, "new", "push1");
            Link(g, "push1", "push2");
            Link(g, "push2", "o1");
            Link(g, "o1", "o2");
            Link(g, "o2", "o3");
            Link(g, "o3", "o4");
            return g;
        }

        // entry → 创建队列 → 入队 3、4 → 输出队头 / 个数 → 出队 → 再输出队头（还剩一个）/ 空了
        private static Graph QueueMemberGraph()
        {
            var g = StructGraph("STKMEM-QUEUE-E2E", "queue", "q");
            g.Nodes.AddRange(new[]
            {
                Nd("push1", "queue_push", "structId", "q", "pushExpr", "3"),
                Nd("push2", "queue_push", "structId", "q", "pushExpr", "4"),
                Nd("o1", "output", "outputExpr", "q.front"),
                Nd("o2", "output", "outputExpr", "q.size"),
                Nd("pop", "queue_pop", "structId", "q"),
                Nd("o3", "output", "outputExpr", "q.front"),
                Nd("o4", "output", "outputExpr", "q.empty")
            });
            Link(g, "new", "push1");
            Link(g, "push1", "push2");
            Link(g, "push2", "o1");
            Link(g, "o1", "o2");
            Link(g, "o2", "pop");
            Link(g, "pop", "o3");
            Link(g, "o3", "o4");
            return g;
        }

        // int[] a = [1, 2, 3]，输出 a.size 与 a.length：两者必须一模一样
        private static Graph ArraySizeGraph()
        {
            var g = new Graph { LevelId = "STKMEM-ARRAY-E2E", ExprMode = true };
            g.Nodes.AddRange(new[]
            {
                Nd("e", "entry"),
                Nd("d", "vardecl", "name", "a", "vtype", "int[]", "initExpr", "[1, 2, 3]"),
                Nd("o1", "output", "outputExpr", "a.size"),
                Nd("o2", "output", "outputExpr", "a.length")
            });
            Link(g, "e", "d");
            Link(g, "d", "o1");
            Link(g, "o1", "o2");
            return g;
        }

        // 创建了但一颗没压，直接取栈顶：编译得过（表达式在创建卡下面），要拦在运行时。
        private static Graph TopOfEmptyGraph()
        {
            var g = StructGraph("STKMEM-EMPTY-RUN", "stack", "st");
            g.Nodes.Add(Nd("o", "output", "outputExpr", "st.top"));
            Link(g, "new", "o");
            return g;
        }

        // 创建栈的卡排在输出后面：表达式写在前面时容器实例还没建档
        private static Graph CreateAfterUseGraph()
        {
            var g = new Graph { LevelId = "STKMEM-BEFORE-CREATE", ExprMode = true };
            g.Structs.Add(new StructDef { Id = "st", Kind = "stack", Elem = "int", Name = "栈" });
            g.Nodes.AddRange(new[]
            {
                Nd("e", "entry"),
                Nd("o", "output", "outputExpr", "st.empty"),
                Nd("new", "structnew", "structId", "st")
            });
            Link(g, "e", "o");
            Link(g, "o", "new");
            return g;
        }

        // 词表里没有的成员：编译期报错要把实际支持的几颗列出来
        private static Graph BadMemberGraph()
        {
            var g = StructGraph("STKMEM-BAD-MEMBER", "stack", "st");
            g.Nodes.Add(Nd("o", "output", "outputExpr", "st.pop"));
            Link(g, "new", "o");
            return g;
        }

        // 拿栈的写法去问队列（front 才对，top 是栈的）：报错要指出这颗队列真有的成员
        private static Graph TopOfQueueGraph()
        {
            var g = StructGraph("STKMEM-WRONG-CONTAINER", "queue", "q");
            g.Nodes.Add(Nd("o", "output", "outputExpr", "q.top"));
            Link(g, "new", "o");
            return g;
        }

        // 骨架：entry → 创建一颗实例。实例名由调用方给，顺带验的不只是默认那颗 st。
        private static Graph StructGraph(string levelId, string kind, string id)
        {
            var g = new Graph { LevelId = levelId, ExprMode = true };
            g.Structs.Add(new StructDef { Id = id, Kind = kind, Elem = "int", Name = kind == "queue" ? "一排队" : "一摞数" });
            g.Nodes.AddRange(new[] { Nd("e", "entry"), Nd("new", "structnew", "structId", id) });
            Link(g, "e", "new");
            return g;
        }

        private static GraphNode Nd(string uid, string type, params string[] ps)
        {
            var n = new GraphNode { Uid = uid, Type = type };
            for (int i = 0; i + 1 < ps.Length; i += 2) n.Params[ps[i]] = ps[i + 1];
            return n;
        }

        private static void Link(Graph g, string from, string to)
        {
            g.Links.Add(new GraphLink { FromNode = from, FromPort = "out", ToNode = to, ToPort = "in", Kind = LinkKind.Exec });
        }

        // ---------------- 变量表与补全词表 ----------------

        // 浮窗补出来的名字要能在类型表里查到：容器作为一条 ExprVariableInfo 出现，成员直接给 token。
        private static void PaletteCases(ref int total, ref int passed)
        {
            var g = StructGraph("STKMEM-UI", "stack", "st");
            g.Nodes.Add(Nd("o", "output", "outputExpr", "st.size"));
            Link(g, "new", "o");

            var vars = NodeCard.BuildVariables(g, "o");
            CheckHas(ref total, ref passed, "STKMEM-UI-HAS-STACK", Describe(vars, "st"), "st:stack<int>:[size,empty,top]");
            CheckHas(ref total, ref passed, "STKMEM-UI-NOT-ARRAY", Describe(vars, "st"), "array=False");

            var before = NodeCard.BuildVariables(g, "e");
            Check(ref total, ref passed, "STKMEM-UI-BEFORE-CREATE 创建卡前面看不见实例", Describe(before, "st"), "（没有 st）");

            var arr = ArraySizeGraph();
            CheckHas(ref total, ref passed, "STKMEM-UI-ARRAY-MEMBERS", Describe(NodeCard.BuildVariables(arr, "o1"), "a"), "a:int[]:[size,empty,length]");
        }

        // 一行讲清「这个名字 / 这颗类型 / 给了哪几颗成员 / 有没有被当成数组」，好和期望逐字比。
        private static string Describe(List<ExprVariableInfo> vars, string name)
        {
            foreach (var v in vars)
                if (v != null && v.Name == name)
                    return v.Name + ":" + v.Type + ":[" + string.Join(",", v.Members) + "] array=" + v.IsArray;
            return "（没有 " + name + "）";
        }

        // ---------------- 执行与比较 ----------------

        // 和 ArrayTests 一个口径：Core 编译 → 生成 C++ → g++ 真编译真跑 → 与 VM 逐条样例比对。
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
                string root = Path.GetFullPath(Path.Combine("Temp", "structmemtests", key));
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
                if (run.Ok && got == expect) { passed++; Debug.Log("✔ " + label + " [VM] —— " + Show2(got)); }
                else Debug.Log("✘ " + label + " [VM] —— " + (run.Ok ? "得到「" + Show2(got) + "」，期望「" + Show2(expect) + "」" : run.Error.Message));

                total++;
                if (exe == null) { Debug.Log("✘ " + label + " [C++] —— " + cppError); continue; }
                string no, ne;
                if (!RunProcess(exe, "", input, out no, out ne)) { Debug.Log("✘ " + label + " [C++] —— " + First(ne, no)); continue; }
                string native = Normalize(no);
                if (native == Normalize(expect)) { passed++; Debug.Log("✔ " + label + " [C++] —— " + Show2(native)); }
                else Debug.Log("✘ " + label + " [C++] —— 得到「" + Show2(native) + "」，期望「" + Show2(expect) + "」，VM 为「" + Show2(got) + "」");
            }
        }

        private static void ExpectCompileError(ref int total, ref int passed, string id, string expect, Graph g)
        {
            total++;
            var cr = Compiler.Compile(g);
            if (!cr.Ok && cr.Errors.Exists(x => x.Message.Contains(expect)))
            {
                passed++;
                Debug.Log("✔ " + id + " 编译报错 —— " + cr.Errors[0].Message);
            }
            else Debug.Log("✘ " + id + " 编译报错 —— " + (cr.Ok ? "意外编译通过" : "错误信息里没有「" + expect + "」：" + cr.Errors[0].Message));
        }

        private static void Check(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ " + label + " —— " + actual); }
            else Debug.Log("✘ " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }

        // 报错文案只看关键半句：整句里带了类型名和成员列表，逐字比会把自己绑死
        private static void CheckHas(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual != null && actual.IndexOf(want, StringComparison.Ordinal) >= 0) { passed++; Debug.Log("✔ " + label + " —— " + actual); }
            else Debug.Log("✘ " + label + " 里找不到「" + want + "」—— " + actual);
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

        private static string Quote(string path) { return "\"" + path.Replace("\"", "\\\"") + "\""; }
        private static string Normalize(string text) { return (text ?? "").Replace("\r\n", "\n").Trim(); }
        private static string Show2(string text) { return string.IsNullOrEmpty(text) ? "（空）" : text.Replace("\r\n", "\n").Replace("\n", " | "); }
        private static string First(string a, string b) { return string.IsNullOrEmpty(a) ? (string.IsNullOrEmpty(b) ? "未知错误" : b) : a; }
    }
}
