using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System;
using System.IO;
using System.Text;
using Dayu.Core;
using Proc = System.Diagnostics.Process;
using ProcStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Dayu.EditorTools
{
    public static class Cli
    {
        public static void BuildScene()
        {
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("App");
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(s, "Assets/Scenes/Main.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
        }

        public static void RunSelfTests()
        {
            if (!File.Exists("Assets/Scenes/Main.unity")) BuildScene();
            int n = 0, ok = 0;
            foreach (var l in Levels.All)
            {
                n++;
                var c = Compiler.Compile(l.Solution);
                if (c.Ok)
                {
                    ok++;
                    Debug.Log("✔ " + l.Id + " —— Compile");
                    foreach (var t in l.Tests)
                    {
                        n++;
                        var r = Vm.Run(c.Program, t.Input);
                        if (r.Ok && r.Output == t.Output)
                        {
                            ok++;
                            Debug.Log("✔ " + l.Id + " " + t.Input.Replace("\n", " ") + " —— " + r.Output);
                        }
                        else Debug.Log("✘ " + l.Id + " —— " + (r.Error == null ? "输出不一致" : r.Error.Message));
                    }
                }
                else Debug.Log("✘ " + l.Id + " —— " + c.Errors[0].Message);
            }

            RunExprSelfTest(ref n, ref ok);
            RunWhileSelfTest(ref n, ref ok);
            RunCppLevelSelfTest(ref n, ref ok, "L1");
            RunCppLevelSelfTest(ref n, ref ok, "L2");
            // 其余每一关（含序章五关）也过一遍 g++：VM 与原生 C++ 的逐样例输出必须一致
            // L3 输出的是 bool，靠 CppGen 的 cout << boolalpha 和 VM 的 true/false 对齐，这里一并盯住
            foreach (var lvl in Levels.All)
                if (lvl.Id != "L1" && lvl.Id != "L2") RunCppLevelSelfTest(ref n, ref ok, lvl.Id);
            ExprLangTests.Run(ref n, ref ok);
            ControlFlowTests.Run(ref n, ref ok);
            ExprNodesTests.Run(ref n, ref ok);
            ArrayTests.Run(ref n, ref ok);
            StructMemberTests.Run(ref n, ref ok);
            MathFnTests.Run(ref n, ref ok);
            LevelExprTests.Run(ref n, ref ok);
            LevelContentTests.Run(ref n, ref ok);
            SampleLayoutTests.Run(ref n, ref ok);
            ParamsPanelTests.Run(ref n, ref ok);
            CompletionPopupTests.Run(ref n, ref ok);
            CardShapeTests.Run(ref n, ref ok);
            SaveSystemTests.Run(ref n, ref ok);
            GraphHistoryTests.Run(ref n, ref ok);
            SaveFlowTests.Run(ref n, ref ok);
            CardFitTests.Run(ref n, ref ok);
            InspectorScrollTests.Run(ref n, ref ok);
            BackdropTests.Run(ref n, ref ok);
            SoundCueTests.Run(ref n, ref ok);
            PanelTintTests.Run(ref n, ref ok);
            ClosureCaptureTests.Run(ref n, ref ok);
            SamplePinTests.Run(ref n, ref ok);
            ManualCoverageTests.Run(ref n, ref ok);
            PersonaAssetTests.Run(ref n, ref ok);
            LicenseLedgerTests.Run(ref n, ref ok);
            VerdictFxTests.Run(ref n, ref ok);
            MenuLayoutTests.Run(ref n, ref ok);
            Debug.Log("共 " + n + " 项，通过 " + ok + " 项");
            if (n != ok) EditorApplication.Exit(1);
        }

        private static void RunWhileSelfTest(ref int total, ref int passed)
        {
            var g = new Graph { LevelId = "WHILE" };
            var e = Node("e", "entry", 0, 0); g.Nodes.Add(e);
            var decl = Node("decl", "vardecl", 0, 0, "name", "i", "vtype", "int"); g.Nodes.Add(decl);
            var zero = Node("zero", "const", 0, 0, "vtype", "int", "value", "0"); g.Nodes.Add(zero);
            var three = Node("three", "const", 0, 0, "vtype", "int", "value", "3"); g.Nodes.Add(three);
            var one = Node("one", "const", 0, 0, "vtype", "int", "value", "1"); g.Nodes.Add(one);
            var set0 = Node("set0", "varset", 0, 0, "name", "i"); g.Nodes.Add(set0);
            var loop = Node("loop", "whileloop", 0, 0); g.Nodes.Add(loop);
            var get = Node("get", "varget", 0, 0, "name", "i"); g.Nodes.Add(get);
            var cmp = Node("cmp", "compare", 0, 0, "op", "<"); g.Nodes.Add(cmp);
            var add = Node("add", "binop", 0, 0, "op", "+", "varA", "i", "varB", ""); g.Nodes.Add(add);
            var seti = Node("seti", "varset", 0, 0, "name", "i"); g.Nodes.Add(seti);
            var outn = Node("out", "output", 0, 0); g.Nodes.Add(outn);

            Exec(g, "e", "out", "decl");
            Exec(g, "decl", "out", "set0");
            Exec(g, "set0", "out", "loop");
            Exec(g, "loop", "body", "seti");
            Exec(g, "loop", "done", "out");
            Data(g, "zero", "value", "set0", "value");
            Data(g, "get", "value", "cmp", "a");
            Data(g, "three", "value", "cmp", "b");
            Data(g, "cmp", "r", "loop", "cond");
            Data(g, "get", "value", "add", "a");
            Data(g, "one", "value", "add", "b");
            Data(g, "add", "r", "seti", "value");
            Data(g, "get", "value", "out", "value");

            total++;
            var cr = Compiler.Compile(g);
            if (!cr.Ok) { Debug.Log("✘ While —— " + cr.Errors[0].Message); return; }
            var run = Vm.Run(cr.Program, "");
            if (run.Ok && run.Output == "3") { passed++; Debug.Log("✔ While —— 3"); }
            else Debug.Log("✘ While —— " + (run.Error == null ? run.Output : run.Error.Message));

            total++;
            try
            {
                string root = Path.GetFullPath(Path.Combine("Temp", "while-e2e"));
                Directory.CreateDirectory(root);
                string cpp = Path.Combine(root, "While.cpp");
                string exe = Path.Combine(root, "While.exe");
                File.WriteAllText(cpp, CppGen.Generate(cr.Program, "WHILE"), Encoding.UTF8);
                string o, er;
                if (!RunProcess("C:/Users/24585/Downloads/w64devkit/bin/g++.exe", "-std=c++17 -O0 -o " + Quote(exe) + " " + Quote(cpp), null, out o, out er))
                    Debug.Log("✘ While C++ —— g++ " + er);
                else if (RunProcess(exe, "", "", out o, out er) && Normalize(o) == "3") { passed++; Debug.Log("✔ While C++ —— 3"); }
                else Debug.Log("✘ While C++ —— " + Normalize(o));
            }
            catch (Exception ex) { Debug.Log("✘ While C++ —— " + ex.Message); }
        }

        private static GraphNode Node(string uid, string type, float x, float y, params string[] ps)
        {
            var n = new GraphNode { Uid = uid, Type = type, X = x, Y = y };
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

        private static void RunExprSelfTest(ref int total, ref int passed)
        {
            var ws = new ExprWorkspace();
            ws.Equations.Add(new ExprEquation { Name = "t1", Expr = ExprNode.Array(ExprNode.Const("10"), ExprNode.Const("20"), ExprNode.Const("30")) });
            var call = new ExprNode { Kind = "front", Children = new System.Collections.Generic.List<ExprNode> { ExprNode.Var("t1") } };
            var call2 = new ExprNode { Kind = "back", Children = new System.Collections.Generic.List<ExprNode> { ExprNode.Var("t1") } };
            var call3 = new ExprNode { Kind = "size", Children = new System.Collections.Generic.List<ExprNode> { ExprNode.Var("t1") } };
            var e1 = ExprNode.Op("+", ExprNode.Index("t1", ExprNode.Var("b")), ExprNode.Var("a"));
            var e2 = ExprNode.Op("+", e1, call);
            var e3 = ExprNode.Op("+", e2, call2);
            ws.Output = ExprNode.Op("+", e3, call3);

            total++;
            var direct = ExprFormat.Eval(ws, name => name == "a" ? Val.Int(3) : name == "b" ? Val.Int(1) : Val.Int(0));
            if (direct.AsLong() == 66) { passed++; Debug.Log("✔ Expr 数组索引/STL —— 66"); }
            else Debug.Log("✘ Expr 数组索引/STL —— " + direct.AsLong());

            total++;
            var g = new Graph { LevelId = "EXPR" };
            var e0 = Node("e", "entry", 0, 0); g.Nodes.Add(e0);
            var declA = Node("declA", "vardecl", 0, 0, "name", "a", "vtype", "int"); g.Nodes.Add(declA);
            var declB = Node("declB", "vardecl", 0, 0, "name", "b", "vtype", "int"); g.Nodes.Add(declB);
            var ia = Node("ia", "input", 0, 0, "vtype", "int", "name", "a"); g.Nodes.Add(ia);
            var ib = Node("ib", "input", 0, 0, "vtype", "int", "name", "b"); g.Nodes.Add(ib);
            var sum = Node("sum", "binop", 0, 0, "op", "+", "expr", ExprFormat.Serialize(ws)); g.Nodes.Add(sum);
            var outn = Node("out", "output", 0, 0); g.Nodes.Add(outn);
            Exec(g, "e", "out", "declA");
            Exec(g, "declA", "out", "declB");
            Exec(g, "declB", "out", "ia");
            Exec(g, "ia", "out", "ib");
            Exec(g, "ib", "out", "out");
            Data(g, "sum", "r", "out", "value");
            var cr = Compiler.Compile(g);
            if (!cr.Ok) Debug.Log("✘ Expr VM —— " + cr.Errors[0].Message);
            else
            {
                var run = Vm.Run(cr.Program, "3 1");
                if (run.Ok && run.Output == "66") { passed++; Debug.Log("✔ Expr VM —— 66"); }
                else Debug.Log("✘ Expr VM —— " + (run.Error == null ? run.Output : run.Error.Message));
            }

            total++;
            try
            {
                string root = Path.GetFullPath(Path.Combine("Temp", "expr-e2e"));
                Directory.CreateDirectory(root);
                string cpp = Path.Combine(root, "Expr.cpp");
                string exe = Path.Combine(root, "Expr.exe");
                File.WriteAllText(cpp, "#include <bits/stdc++.h>\nusing namespace std;\nint main(){ long long a=3,b=1; cout << (" + ExprFormat.Cpp(ws) + ") << '\\n'; }\n", Encoding.UTF8);
                string o, e;
                string gpp = "C:/Users/24585/Downloads/w64devkit/bin/g++.exe";
                if (!RunProcess(gpp, "-std=c++17 -O0 -o " + Quote(exe) + " " + Quote(cpp), null, out o, out e))
                    Debug.Log("✘ Expr C++ —— g++ " + e);
                else if (RunProcess(exe, "", "", out o, out e) && Normalize(o) == "66")
                {
                    passed++; Debug.Log("✔ Expr C++ —— 66");
                }
                else Debug.Log("✘ Expr C++ —— " + Normalize(o));
            }
            catch (Exception ex) { Debug.Log("✘ Expr C++ —— " + ex.Message); }
        }

        private static Graph Clone(Graph src)
        {
            var g = new Graph { LevelId = src.LevelId };
            if (src.Structs != null) foreach (var s in src.Structs) g.Structs.Add(new StructDef { Id = s.Id, Kind = s.Kind, Elem = s.Elem, Name = s.Name });
            foreach (var n in src.Nodes)
            {
                var c = new GraphNode { Uid = n.Uid, Type = n.Type, X = n.X, Y = n.Y };
                foreach (var kv in n.Params) c.Params[kv.Key] = kv.Value;
                g.Nodes.Add(c);
            }
            foreach (var l in src.Links)
                g.Links.Add(new GraphLink { FromNode = l.FromNode, FromPort = l.FromPort, ToNode = l.ToNode, ToPort = l.ToPort, Kind = l.Kind });
            return g;
        }

        // 关卡 Solution 生成的 C++ 用 g++ 真编译真运行，和 VM 逐条样例比对。
        private static void RunCppLevelSelfTest(ref int total, ref int passed, string id)
        {
            total++;
            var level = Levels.All.Find(x => x.Id == id);
            if (level == null)
            {
                Debug.Log("✘ CppGen " + id + " —— 找不到 " + id + " 关卡");
                return;
            }

            string root = Path.GetFullPath(Path.Combine("Temp", "cpp-e2e", id));
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, id + ".cpp");
            string exe = Path.Combine(root, id + ".exe");
            string compiler = "C:/Users/24585/Downloads/w64devkit/bin/g++.exe";
            try
            {
                var compiled = Compiler.Compile(level.Solution);
                if (!compiled.Ok) { Debug.Log("✘ CppGen " + id + " —— Core 编译失败：" + compiled.Errors[0].Message); return; }
                File.WriteAllText(source, CppGen.Generate(compiled.Program, level.Id), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.Log("✘ CppGen " + id + " —— 生成 C++ 失败：" + ex.Message);
                return;
            }

            string compileOutput;
            string compileError;
            if (!RunProcess(compiler, "-std=c++17 -O0 -o " + Quote(exe) + " " + Quote(source), null, out compileOutput, out compileError))
            {
                Debug.Log("✘ CppGen " + id + " —— g++ 编译失败：" + compileError);
                return;
            }

            passed++;
            Debug.Log("✔ CppGen " + id + " —— g++ compile");
            var compiledProgram = Compiler.Compile(level.Solution);
            foreach (var test in level.Tests)
            {
                total++;
                var vm = Vm.Run(compiledProgram.Program, test.Input);
                string nativeOutput;
                string nativeError;
                bool ran = RunProcess(exe, "", test.Input, out nativeOutput, out nativeError);
                if (ran && vm.Ok && Normalize(nativeOutput) == Normalize(vm.Output))
                {
                    passed++;
                    Debug.Log("✔ CppGen " + id + " " + test.Input.Replace("\n", " ") + " —— " + Normalize(nativeOutput));
                }
                else
                {
                    string reason = !ran ? nativeError : (!vm.Ok ? vm.Error.Message : "输出不一致：C++=" + Normalize(nativeOutput) + "，VM=" + Normalize(vm.Output));
                    Debug.Log("✘ CppGen " + id + " " + test.Input.Replace("\n", " ") + " —— " + reason);
                }
            }
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
            catch (Exception ex) { error = "Unity Editor 无法启动外部进程：" + ex.Message; return false; }
        }

        private static string Quote(string path) { return "\"" + path.Replace("\"", "\\\"") + "\""; }
        private static string Normalize(string text) { return (text ?? "").Replace("\r\n", "\n").Trim(); }
    }
}
