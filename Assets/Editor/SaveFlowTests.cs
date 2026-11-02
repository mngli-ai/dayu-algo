using System;
using System.Collections.Generic;
using System.IO;
using Dayu.Core;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 存档接线侧的纯逻辑自测：双击落点、看答案的开放条件、进度元信息往返、进关该铺哪张图。
    // 全部走 SaveFlow 的纯函数 + SaveSystem 的临时目录，不碰 Unity 播放态。
    public static class SaveFlowTests
    {
        const string Tag = "存档接线";
        static string root;

        public static void Run(ref int n, ref int ok)
        {
            FocusKey(ref n, ref ok);
            PopupFocus(ref n, ref ok);
            AnswerGate(ref n, ref ok);
            AutoSaveSwitch(ref n, ref ok);
            MetaAccounting(ref n, ref ok);
            MetaRoundTrip(ref n, ref ok);
            MetaDisk(ref n, ref ok);
            InitialGraph(ref n, ref ok);
            SaveLoadTiming(ref n, ref ok);
            Cleanup();
        }

        // ---------------- 双击落点 ----------------

        static void FocusKey(ref int n, ref int ok)
        {
            var want = new Dictionary<string, string>
            {
                { "vardecl", "name" }, { "varset", "valueExpr" }, { "varget", "name" },
                { "input", "name" }, { "output", "outputExpr" },
                { "branch", "condExpr" }, { "whileloop", "condExpr" }, { "forloop", "fromExpr" },
                { "structnew", SaveFlow.StructNameKey },
                { "stack_push", "pushExpr" }, { "queue_push", "pushExpr" },
                { "stack_pop", "structId" }, { "stack_top", "structId" }, { "stack_empty", "structId" }, { "stack_size", "structId" },
                { "queue_pop", "structId" }, { "queue_front", "structId" }, { "queue_empty", "structId" }, { "queue_size", "structId" },
                { "const", "value" }, { "sample_input", "group" },
                { "entry", "" }, { "sample_compare", "" },
            };
            foreach (var kv in want)
                Case(ref n, ref ok, "双击 " + kv.Key + " 聚焦 " + (string.IsNullOrEmpty(kv.Value) ? "（没有要填的空）" : kv.Value),
                     SaveFlow.PreferredFocusKey(kv.Key) == kv.Value, "实得 " + (SaveFlow.PreferredFocusKey(kv.Key) ?? "null"));

            // 玩家最在意的那一条：创建变量绝不能落到初始值那一格
            Case(ref n, ref ok, "创建变量绝不落到初始值格", SaveFlow.PreferredFocusKey("vardecl") != "initExpr",
                 "落到了 " + SaveFlow.PreferredFocusKey("vardecl"));
            Case(ref n, ref ok, "没听过的类型不猜落点", SaveFlow.PreferredFocusKey("made_up_node") == "",
                 "实得 " + SaveFlow.PreferredFocusKey("made_up_node"));
            Case(ref n, ref ok, "类型为空也不炸", SaveFlow.PreferredFocusKey(null) == "", "实得 " + (SaveFlow.PreferredFocusKey(null) ?? "null"));

            // 关卡里真在用的每个类型：给的键必须是这个类型自己声明过的参数，不然浮窗里认不出来
            var used = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var l in Levels.All)
            {
                Collect(l.Starter, used);
                Collect(l.Solution, used);
            }
            int bad = 0; string detail = "";
            foreach (var t in used)
            {
                string key = SaveFlow.PreferredFocusKey(t);
                if (string.IsNullOrEmpty(key)) continue;
                var nt = NodeTypes.Get(t);
                bool found = false;
                if (nt != null && nt.Params != null)
                    foreach (var p in nt.Params)
                        if (p != null && p.Id == key) found = true;
                if (key == SaveFlow.StructNameKey) found = true;   // 容器名是 App 往参数表后面补的一格，类型表里没有
                if (!found) { bad++; detail += t + "→" + key + " "; }
            }
            Case(ref n, ref ok, "关卡在用类型的落点都是真参数（共 " + used.Count + " 类）", bad == 0 && used.Count >= 12,
                 "越界的：" + detail + "类型数 " + used.Count);
        }

        static void Collect(Graph g, SortedSet<string> into)
        {
            if (g == null || g.Nodes == null) return;
            foreach (var nd in g.Nodes)
                if (nd != null && !string.IsNullOrEmpty(nd.Type)) into.Add(nd.Type);
        }

        static void PopupFocus(ref int n, ref int ok)
        {
            // 浮窗里的文本框从上到下（与 App.BuildParamSpecs 同口径：enum 那一排按钮不算文本框）
            var scalar = new List<string> { "name", "initExpr" };
            var array = new List<string> { "name", "size" };                       // 数组卡把 initExpr 顶掉了
            var loop = new List<string> { "indexName" };                            // 起点/终点留在卡面上
            Case(ref n, ref ok, "标量创建变量落名字", SaveFlow.PickPopupFocusKey("vardecl", "name", scalar) == "name", "");
            Case(ref n, ref ok, "数组创建变量也落名字", SaveFlow.FirstPopupFocusKey("vardecl", array) == "name", "");
            Case(ref n, ref ok, "空初始值的创建变量仍落名字", SaveFlow.FirstPopupFocusKey("vardecl", scalar) == "name", "");
            Case(ref n, ref ok, "改过一次的创建变量还是落名字", SaveFlow.FirstPopupFocusKey("vardecl", scalar) == "name", "");
            Case(ref n, ref ok, "从初始值格双击也回落到名字", SaveFlow.PickPopupFocusKey("vardecl", "initExpr", array) == "name",
                 "实得 " + SaveFlow.PickPopupFocusKey("vardecl", "initExpr", array));
            Case(ref n, ref ok, "赋值点名语句格就照它", SaveFlow.PickPopupFocusKey("varset", "valueExpr", new List<string> { "valueExpr" }) == "valueExpr", "");
            Case(ref n, ref ok, "循环窗里没有 fromExpr 就落循环变量名", SaveFlow.PickPopupFocusKey("forloop", "fromExpr", loop) == "indexName", "");
            Case(ref n, ref ok, "没有文本框就不给落点", SaveFlow.PickPopupFocusKey("vardecl", "name", new List<string>()) == "", "");
            Case(ref n, ref ok, "文本框表为 null 也不给落点", SaveFlow.PickPopupFocusKey("vardecl", "name", null) == "", "");

            // 永远不返回一个浮窗里认不出来的键
            var probe = new[] { scalar, array, loop, new List<string> { "condExpr" }, new List<string> { "structId", SaveFlow.StructNameKey } };
            string types = "vardecl,varset,input,output,branch,whileloop,forloop,structnew,stack_push,const,sample_input,entry";
            int off = 0;
            foreach (var t in types.Split(','))
                foreach (var keys in probe)
                {
                    string got = SaveFlow.PickPopupFocusKey(t, "ghostKey", keys);
                    if (got.Length > 0 && keys.IndexOf(got) < 0) off++;
                }
            Case(ref n, ref ok, "落点永远在浮窗的文本框里", off == 0, "越界 " + off + " 次");
        }

        // ---------------- 看答案的开放条件 ----------------

        static void AnswerGate(ref int n, ref int ok)
        {
            Case(ref n, ref ok, "调试期恒开（错 0 次也能看）", SaveFlow.AnswerUnlocked(0, true), "");
            Case(ref n, ref ok, "调试期连负数也开", SaveFlow.AnswerUnlocked(-5, true), "");
            Case(ref n, ref ok, "正式期错 2 次还不给看", !SaveFlow.AnswerUnlocked(SaveFlow.AnswerUnlockFailures - 1, false),
                 "阈值 " + SaveFlow.AnswerUnlockFailures + " 却放行了");
            Case(ref n, ref ok, "正式期错满 " + SaveFlow.AnswerUnlockFailures + " 次就放行", SaveFlow.AnswerUnlocked(SaveFlow.AnswerUnlockFailures, false), "");
            Case(ref n, ref ok, "正式期错过头继续放行", SaveFlow.AnswerUnlocked(SaveFlow.AnswerUnlockFailures + 7, false), "");
            Case(ref n, ref ok, "正式期一次没错不给看", !SaveFlow.AnswerUnlocked(0, false), "");
            Case(ref n, ref ok, "调试开关确实是常量 true", SaveFlow.DebugAnswerAlwaysOpen, "改成 false 才能发版");
            Case(ref n, ref ok, "阈值不是随手能撞到的数", SaveFlow.AnswerUnlockFailures >= 2 && SaveFlow.AnswerUnlockFailures <= 10,
                 "阈值 " + SaveFlow.AnswerUnlockFailures);
        }

        static void AutoSaveSwitch(ref int n, ref int ok)
        {
            Case(ref n, ref ok, "正常玩法自动存档", SaveFlow.ShouldAutoSave(false, false), "");
            Case(ref n, ref ok, "看答案时挂起存档", !SaveFlow.ShouldAutoSave(true, false), "");
            Case(ref n, ref ok, "-solution 批跑不落盘", !SaveFlow.ShouldAutoSave(false, true), "");
            Case(ref n, ref ok, "批跑里的预览也不落盘", !SaveFlow.ShouldAutoSave(true, true), "");
            var st = new Graph { LevelId = "T" };
            st.Nodes.Add(Nd("e", "entry", 0, 0));
            var p = SaveFlow.PreviewGraph(st);
            Case(ref n, ref ok, "预览交出去的是克隆", !ReferenceEquals(p, st) && GraphIO.Same(p, st), "");
            p.Nodes.Add(Nd("x", "output", 10, 10, "outputExpr", "9"));
            Case(ref n, ref ok, "改预览不改脏答案原件", st.Nodes.Count == 1, "原件节点数 " + st.Nodes.Count);
            var back1 = SaveFlow.PlayerGraphAfterPreview(p, st);
            Case(ref n, ref ok, "退出预览还回玩家那份", back1.Nodes.Count == 2 && !ReferenceEquals(back1, p), "");
            var back2 = SaveFlow.PlayerGraphAfterPreview(null, st);
            Case(ref n, ref ok, "没存过玩家那份就退回 Starter 克隆", back2.Nodes.Count == 1 && !ReferenceEquals(back2, st), "");
        }

        // ---------------- 进度元信息 ----------------

        static void MetaAccounting(ref int n, ref int ok)
        {
            var map = new Dictionary<string, SaveFlow.Meta>(StringComparer.Ordinal);
            long t0 = new DateTime(2026, 6, 1, 3, 4, 5, DateTimeKind.Utc).Ticks;
            var m = SaveFlow.TouchEdited(map, "L3", t0);
            Case(ref n, ref ok, "改一笔就记下时间", m != null && m.UpdatedTicks == t0, "");
            for (int i = 0; i < 3; i++) SaveFlow.RecordSubmit(map, "L3", t0 + i, false);
            Case(ref n, ref ok, "连错三次记三笔", map["L3"].Failures == 3 && map["L3"].Submissions == 3,
                 "Failures=" + map["L3"].Failures + " Submissions=" + map["L3"].Submissions);
            SaveFlow.RecordSubmit(map, "L3", t0 + 9, true);
            Case(ref n, ref ok, "过了就连错次数归零", map["L3"].Failures == 0 && map["L3"].Submissions == 4, "");
            Case(ref n, ref ok, "过了一笔就镜像成通关", map["L3"].Cleared, "");
            SaveFlow.SyncCleared(map, "L3", false);
            Case(ref n, ref ok, "对齐标记按 PlayerPrefs 说话", !map["L3"].Cleared, "");
            Case(ref n, ref ok, "通关判定以 PlayerPrefs 为准", !SaveFlow.ClearedAuthority(false, map["L3"]) && SaveFlow.ClearedAuthority(true, null), "");
            Case(ref n, ref ok, "非法 id 不进表", SaveFlow.TouchEdited(map, "../etc/passwd", t0) == null && SaveFlow.TouchEdited(map, "", t0) == null
                 && SaveFlow.TouchEdited(map, new string('a', 33), t0) == null, "");
        }

        static void MetaRoundTrip(ref int n, ref int ok)
        {
            var map = new Dictionary<string, SaveFlow.Meta>(StringComparer.Ordinal);
            long t1 = new DateTime(2026, 6, 12, 14, 3, 0, DateTimeKind.Utc).Ticks;
            map["L9"] = new SaveFlow.Meta { LevelId = "L9", UpdatedTicks = t1, Cleared = true, Submissions = 5, Failures = 0 };
            map["P0"] = new SaveFlow.Meta { LevelId = "P0", UpdatedTicks = 0, Cleared = false, Submissions = -2, Failures = -1 };
            string text = SaveFlow.SerializeMeta(map);
            Case(ref n, ref ok, "落盘文本按 id 升序（Ordinal：L 在 P 前面）", text.IndexOf("\"L9\"", StringComparison.Ordinal) < text.IndexOf("\"P0\"", StringComparison.Ordinal), text);
            Case(ref n, ref ok, "负数计数写出去就是 0", text.Contains("\"submissions\":0") && !text.Contains("-"), text);
            Case(ref n, ref ok, "同一份进度两次序列化逐字相同", text == SaveFlow.SerializeMeta(map), "");
            var back = SaveFlow.ParseMeta(text);
            Case(ref n, ref ok, "读回来条数一致", back.Count == 2, "实得 " + back.Count);
            var p0 = back["P0"];
            Case(ref n, ref ok, "读回来负数已被夹成 0", p0.Submissions == 0 && p0.Failures == 0, "sub=" + p0.Submissions + " fail=" + p0.Failures);
            var l9 = back["L9"];
            Case(ref n, ref ok, "通关标记与时间往返一致", l9.Cleared && l9.UpdatedTicks == t1, "");
            Case(ref n, ref ok, "时间读数格式对", SaveFlow.TimeText(t1).StartsWith("2026-") && SaveFlow.TimeShort(t1).Contains("-"),
                 SaveFlow.TimeText(t1) + " / " + SaveFlow.TimeShort(t1));
            Case(ref n, ref ok, "没记过时间就不显示时间", SaveFlow.TimeText(0) == "" && SaveFlow.TimeShort(0) == "", "");
            Case(ref n, ref ok, "空表读回来是空表", SaveFlow.ParseMeta("").Count == 0 && SaveFlow.ParseMeta("{\"v\":1,\"levels\":[]}").Count == 0, "");
            Case(ref n, ref ok, "整文件是垃圾也不抛", SafeParse("完全不是 json，{{{{") , "");
            Case(ref n, ref ok, "半截文件丢掉残条但不炸", SafeParse("{\"v\":1,\"levels\":[{\"id\":\"L1\",\"updated\":12,"), "");
            Case(ref n, ref ok, "越界 id 的条目被丢掉", !SaveFlow.ParseMeta("[{\"id\":\"..\",\"updated\":1}]").ContainsKey(".."), "");
            Case(ref n, ref ok, "元信息躲在 progress 子目录里", SaveFlow.MetaPath("R").Replace('\\', '/').EndsWith("R/progress/meta.json")
                 && Path.GetFileName(Path.GetDirectoryName(SaveFlow.MetaPath("R"))) == SaveFlow.MetaDirName, SaveFlow.MetaPath("R"));
            Case(ref n, ref ok, "没有根目录就不给路径", SaveFlow.MetaPath(null) == null && SaveFlow.MetaPath("") == null, "");
        }

        static bool SafeParse(string text)
        {
            try { return SaveFlow.ParseMeta(text).Count == 0; }
            catch (Exception) { return false; }
        }

        static void MetaDisk(ref int n, ref int ok)
        {
            root = Path.GetFullPath(Path.Combine("Temp", "dayu-saveflow-meta"));
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception) { }
            var map = new Dictionary<string, SaveFlow.Meta>(StringComparer.Ordinal);
            SaveFlow.RecordSubmit(map, "L5", new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc).Ticks, false);
            string err;
            Case(ref n, ref ok, "进度文件写得出去", SaveFlow.WriteMeta(root, map, out err), err ?? "");
            Case(ref n, ref ok, "写出来的就是那一个文件", File.Exists(SaveFlow.MetaPath(root)) && !File.Exists(Path.Combine(root, "meta.json")), "");
        }

        static void InitialGraph(ref int n, ref int ok)
        {
            var starter = new Graph { LevelId = "T", ExprMode = true };
            starter.Nodes.Add(Nd("e", "entry", 0, 0));
            starter.Nodes.Add(Nd("d", "vardecl", 0, 80, "name", "a", "vtype", "int", "initExpr", "1"));
            var solution = GraphIO.Clone(starter);
            solution.Nodes.Add(Nd("o", "output", 0, 160, "outputExpr", "a"));

            var a = SaveFlow.PickInitialGraph(starter, solution, null, null, false);
            Case(ref n, ref ok, "没存档就铺 Starter", a.Source == SaveFlow.LoadSource.Starter && a.Graph.Nodes.Count == 2, "");
            Case(ref n, ref ok, "铺出去的 Starter 不是关卡那份", !ReferenceEquals(a.Graph, starter), "");
            a.Graph.Nodes.Find(x => x.Uid == "d").Params["initExpr"] = "999";
            Case(ref n, ref ok, "改脏铺出去的图不动关卡静态数据", GraphUtil.Get(starter.Nodes.Find(x => x.Uid == "d"), "initExpr") == "1",
                 "关卡里现在是 " + GraphUtil.Get(starter.Nodes.Find(x => x.Uid == "d"), "initExpr"));
            Case(ref n, ref ok, "表达式模式标记跟着克隆走", a.Graph.ExprMode, "");

            var saved = GraphIO.Clone(starter);
            saved.Nodes.Add(Nd("o", "output", 0, 200, "outputExpr", "7"));
            var b = SaveFlow.PickInitialGraph(starter, solution, saved, null, false);
            Case(ref n, ref ok, "有存档就铺存档那份", b.Source == SaveFlow.LoadSource.Saved && ReferenceEquals(b.Graph, saved) && b.Graph.Nodes.Count == 3, "");

            var c = SaveFlow.PickInitialGraph(starter, solution, null, "存档已损坏（少个右括号）", false);
            Case(ref n, ref ok, "读档失败回落 Starter 并带上错误", c.Source == SaveFlow.LoadSource.StarterAfterBadSave && !string.IsNullOrEmpty(c.Error)
                 && c.Graph.Nodes.Count == 2, "");

            var d = SaveFlow.PickInitialGraph(starter, solution, saved, null, true);
            Case(ref n, ref ok, "-solution 批跑只看答案不看存档", d.Source == SaveFlow.LoadSource.Solution && !ReferenceEquals(d.Graph, solution)
                 && d.Graph.Nodes.Count == 3, "");
            Case(ref n, ref ok, "没有答案的关卡不铺空图", SaveFlow.PickInitialGraph(starter, null, null, null, true).Source == SaveFlow.LoadSource.Starter, "");
        }

        // 「存档→编辑→再读档拿回自己那张图」：只能借真实文件走一遍，Unity 生命周期那半截（GraphChanged→Tick）不在这里覆盖
        static void SaveLoadTiming(ref int n, ref int ok)
        {
            root = Path.GetFullPath(Path.Combine("Temp", "dayu-saveflow-timing"));
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception) { }
            SaveSystem.OverrideRoot = root;
            string err;

            var starter = new Graph { LevelId = "L1", ExprMode = true };
            starter.Nodes.Add(Nd("e", "entry", 0, 0));
            var mine = GraphIO.Clone(starter);
            mine.Nodes.Add(Nd("d", "vardecl", 0, 80, "name", "a", "vtype", "int", "initExpr", "2"));
            Case(ref n, ref ok, "没存过时 HasSave 为假", !SaveSystem.HasSave("L1"), "");
            var first = SaveFlow.PickInitialGraph(starter, null, null, null, false);
            Case(ref n, ref ok, "第一次进关铺 Starter", first.Source == SaveFlow.LoadSource.Starter, "");

            SaveSystem.Save("L1", mine, out err);
            var readBack = SaveSystem.Load("L1", out err);
            var second = SaveFlow.PickInitialGraph(starter, null, readBack, err, false);
            Case(ref n, ref ok, "编辑过再进关拿回自己那张图", second.Source == SaveFlow.LoadSource.Saved && second.Graph.Nodes.Count == 2, err);
            Case(ref n, ref ok, "拿回来的图内容就是编辑过的", GraphUtil.Get(second.Graph.Nodes.Find(x => x.Uid == "d"), "initExpr") == "2", "");
            Case(ref n, ref ok, "关卡静态 Starter 一个字节没动", starter.Nodes.Count == 1, "现在 " + starter.Nodes.Count + " 个节点");

            // 玩家在自己图上再改一笔，读档回来的对象改脏也不能污染下一次读档
            second.Graph.Nodes.Add(Nd("o", "output", 0, 160, "outputExpr", "a"));
            SaveSystem.Save("L1", second.Graph, out err);
            var third = SaveFlow.PickInitialGraph(starter, null, SaveSystem.Load("L1", out err), err, false);
            Case(ref n, ref ok, "第三趟读档是三块的图", third.Graph.Nodes.Count == 3, "实得 " + third.Graph.Nodes.Count);
            Case(ref n, ref ok, "批跑 -solution 不受存档影响", SaveFlow.PickInitialGraph(starter, null, third.Graph, null, false).Source == SaveFlow.LoadSource.Saved, "");

            var map = new Dictionary<string, SaveFlow.Meta>(StringComparer.Ordinal);
            SaveFlow.RecordSubmit(map, "L1", DateTime.UtcNow.Ticks, false);
            SaveFlow.WriteMeta(root, map, out err);
            var disk = SaveFlow.ReadMeta(root, out err);
            Case(ref n, ref ok, "进度落盘后读回连错 1 次", disk.Count == 1 && disk["L1"].Failures == 1, err ?? "条数 " + disk.Count);
            Case(ref n, ref ok, "进度文件没被当成一个关卡存档", !Contains(SaveSystem.SavedLevelIds(), SaveFlow.MetaDirName),
                 string.Join(",", ListIds()));
            SaveFlow.DeleteMeta(root);
            Case(ref n, ref ok, "删掉进度文件就是没进度", SaveSystem.HasSave("L1") && SaveFlow.ReadMeta(root, out err).Count == 0, "");
        }

        static bool Contains(IEnumerable<string> ids, string want)
        {
            foreach (var s in ids) if (s == want) return true;
            return false;
        }

        static List<string> ListIds()
        {
            var r = new List<string>();
            foreach (var s in SaveSystem.SavedLevelIds()) r.Add(s);
            return r;
        }

        static GraphNode Nd(string uid, string type, float x, float y, params string[] ps)
        {
            var nd = new GraphNode { Uid = uid, Type = type, X = x, Y = y };
            for (int i = 0; i + 1 < ps.Length; i += 2) nd.Params[ps[i]] = ps[i + 1];
            return nd;
        }

        static void Cleanup()
        {
            SaveSystem.CancelPending();
            SaveSystem.OnError = null;
            SaveSystem.OverrideRoot = null;
            try { if (root != null && Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception ex) { Debug.Log("✘ " + Tag + " —— 临时目录没删干净：" + ex.Message); }
        }

        static void Case(ref int n, ref int ok, string name, bool pass, string detail)
        {
            n++;
            if (pass) { ok++; Debug.Log("✔ " + Tag + " —— " + name); }
            else Debug.Log("✘ " + Tag + " —— " + name + " —— " + (detail ?? ""));
        }
    }
}
