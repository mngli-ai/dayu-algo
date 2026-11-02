using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dayu.Core;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 关卡存档底座自测：GraphIO 的序列化/深拷贝/等价比较 + SaveSystem 的落盘行为。
    // 全是确定性判据（文本逐字节比、内存计数、临时目录），不碰 Unity 播放态、不依赖关卡表内容。
    public static class SaveSystemTests
    {
        const string Tag = "存档";
        static string root;

        public static void Run(ref int n, ref int ok)
        {
            RoundTrip(ref n, ref ok);
            CloneCases(ref n, ref ok);
            BadInput(ref n, ref ok);
            SameCases(ref n, ref ok);
            Disk(ref n, ref ok);
            Debounce(ref n, ref ok);
            BadIds(ref n, ref ok);
            BrokenAndError(ref n, ref ok);
            Cleanup();
        }

        // ---------------- 造图（手搭，不依赖 Levels.All） ----------------

        static GraphNode Nd(string uid, string type, float x, float y, params string[] ps)
        {
            var nd = new GraphNode { Uid = uid, Type = type, X = x, Y = y };
            for (int i = 0; i + 1 < ps.Length; i += 2) nd.Params[ps[i]] = ps[i + 1];
            return nd;
        }
        static GraphLink Ex(string f, string fp, string t) { return new GraphLink { FromNode = f, FromPort = fp, ToNode = t, ToPort = "in", Kind = LinkKind.Exec }; }
        static GraphLink Da(string f, string fp, string t, string tp) { return new GraphLink { FromNode = f, FromPort = fp, ToNode = t, ToPort = tp, Kind = LinkKind.Data }; }

        // 覆盖：中文/引号/反斜杠/换行/制表/空串参数、负数与小数坐标、structs、两种 kind 混合
        static Graph Sample()
        {
            var g = new Graph { LevelId = "S2", ExprMode = true };
            g.Structs.Add(new StructDef { Id = "st", Kind = "stack", Elem = "char" });
            g.Structs.Add(new StructDef { Id = "qu", Kind = "queue", Elem = "int[]", Name = "一列队伍" });
            g.Nodes.Add(Nd("e", "entry", 20, 20));
            g.Nodes.Add(Nd("decl", "vardecl", -12.5f, 82.25f, "name", "a", "vtype", "int[]", "empty", ""));
            g.Nodes.Add(Nd("w", "varset", 250, 806.5f,
                "name", "a", "valueExpr", "a[i] = x",
                "quote", "他说\"走完\"就好", "path", "C:\\saves\\S2.json",
                "multi", "第一行\n第二行\t带制表", "cn", "大肥鱼の算法", "num", "3.25"));
            g.Nodes.Add(Nd("out", "output", 20, 1218, "outputExpr", "sum"));
            g.Links.Add(Ex("e", "out", "decl"));
            g.Links.Add(Ex("decl", "out", "w"));
            g.Links.Add(Ex("w", "out", "out"));
            g.Links.Add(Da("out", "value", "w", "value"));
            return g;
        }

        static Graph Minimal()
        {
            var g = new Graph { LevelId = "P0" };
            g.Nodes.Add(Nd("e", "entry", 0, 0));
            return g;
        }

        // ---------------- 序列化往返 ----------------

        static void RoundTrip(ref int n, ref int ok)
        {
            var g = Sample();
            string err;
            string s1 = GraphIO.Serialize(g);
            var g2 = GraphIO.Deserialize(s1, out err);
            string s2 = GraphIO.Serialize(g2);
            var g3 = GraphIO.Deserialize(s2, out err);
            string s3 = GraphIO.Serialize(g3);

            Case(ref n, ref ok, "复杂图往返无 error", err == null, err);
            Case(ref n, ref ok, "复杂图三次序列化逐字节相同", s1 == s2 && s2 == s3, s1 == s2 ? "一致" : "S1≠S2\n" + s1 + "\n" + s2);
            Case(ref n, ref ok, "坐标用点不用逗号（中文系统坑）", s1.Contains("\"x\":-12.5") && s1.IndexOf("-12,5", StringComparison.Ordinal) < 0, s1);
            Case(ref n, ref ok, "kind 落成 exec/data 字面串", s1.Contains("\"kind\":\"exec\"") && s1.Contains("\"kind\":\"data\""), "见文本");
            Case(ref n, ref ok, "读回的图与原图 Same", GraphIO.Same(g, g2), "结构等价");
            Case(ref n, ref ok, "exprMode 与 levelId 保住", g2 != null && g2.ExprMode && g2.LevelId == "S2", g2 == null ? "读回了 null" : "true/S2");

            var w = g2 == null ? null : GraphUtil.FindNode(g2, "w");
            Case(ref n, ref ok, "引号/反斜杠参数原样读回", w != null && w.Params["quote"] == "他说\"走完\"就好" && w.Params["path"] == "C:\\saves\\S2.json", w == null ? "节点丢了" : w.Params["quote"]);
            Case(ref n, ref ok, "换行/制表参数原样读回", w != null && w.Params["multi"] == "第一行\n第二行\t带制表", w == null ? "节点丢了" : w.Params["multi"].Replace("\n", "\\n").Replace("\t", "\\t"));
            Case(ref n, ref ok, "中文与表达式参数原样读回", w != null && w.Params["cn"] == "大肥鱼の算法" && w.Params["valueExpr"] == "a[i] = x", w == null ? "节点丢了" : "见参数");
            var d = g2 == null ? null : GraphUtil.FindNode(g2, "decl");
            Case(ref n, ref ok, "空串参数不丢键", d != null && d.Params.ContainsKey("empty") && d.Params["empty"] == "", d == null ? "节点丢了" : "empty 在");
            Case(ref n, ref ok, "小数坐标原样读回", d != null && d.X == -12.5f && d.Y == 82.25f, d == null ? "节点丢了" : "X/Y");
            Case(ref n, ref ok, "structs 读回两条且字段齐", g2 != null && g2.Structs.Count == 2 && g2.Structs[1].Name == "一列队伍" && g2.Structs[0].Elem == "char", g2 == null ? "" : "条数 " + g2.Structs.Count);
            Case(ref n, ref ok, "links 读回四条两种 kind", g2 != null && g2.Links.Count == 4 && g2.Links[3].Kind == LinkKind.Data, g2 == null ? "" : "条数 " + g2.Links.Count);

            string m1 = GraphIO.Serialize(Minimal());
            var mErr = (string)null;
            string m2 = GraphIO.Serialize(GraphIO.Deserialize(m1, out mErr));
            Case(ref n, ref ok, "极简图（无 structs/links）无 error", mErr == null, mErr);
            Case(ref n, ref ok, "极简图两次序列化逐字节相同", m1 == m2, m1 == m2 ? m1 : m1 + "\n" + m2);
            Case(ref n, ref ok, "空表落成 [] 而不是省略", m1.Contains("\"structs\":[]") && m1.Contains("\"links\":[]") && m1.Contains("\"params\":{}"), m1);
            Case(ref n, ref ok, "exprMode 默认 false", m1.Contains("\"exprMode\":false"), m1);
            Case(ref n, ref ok, "Serialize(null) 也吐合法空图", GraphIO.Serialize(null).Contains("\"nodes\":[]"), "不返回 null");
        }

        // ---------------- 深拷贝 ----------------

        static void CloneCases(ref int n, ref int ok)
        {
            var g = Sample();
            string before = GraphIO.Serialize(g);
            var c = GraphIO.Clone(g);

            Case(ref n, ref ok, "Clone 与原图 Same", GraphIO.Same(g, c), "结构等价");
            Case(ref n, ref ok, "Clone 全是新实例", !ReferenceEquals(g, c) && !ReferenceEquals(g.Nodes, c.Nodes) && !ReferenceEquals(g.Nodes[0], c.Nodes[0]) && !ReferenceEquals(g.Nodes[0].Params, c.Nodes[0].Params) && !ReferenceEquals(g.Links[0], c.Links[0]), "列表/字典/节点都是新的");
            Case(ref n, ref ok, "Clone 保留 Uid", c.Nodes[1].Uid == "decl", c.Nodes[1].Uid);

            // 下面这组就是防 Graph.cs 里 graph = g 的别名坑：改 clone，原图必须一个字段都不动
            c.LevelId = "CHANGED"; c.ExprMode = false;
            c.Nodes[1].Y = 4321f; c.Nodes[1].X = -999f;
            c.Nodes[2].Params["valueExpr"] = "污染了";
            c.Nodes[2].Params["brandNew"] = "1";
            c.Nodes.Add(Nd("extra", "output", 1, 1));
            c.Links.Add(Ex("extra", "out", "e"));
            c.Structs[0].Elem = "int";

            Case(ref n, ref ok, "改 clone 的 Y 不动原图", g.Nodes[1].Y == 82.25f, "原图 Y=" + g.Nodes[1].Y);
            Case(ref n, ref ok, "改 clone 的 X 不动原图", g.Nodes[1].X == -12.5f, "原图 X=" + g.Nodes[1].X);
            Case(ref n, ref ok, "改 clone 的参数不动原图", g.Nodes[2].Params["valueExpr"] == "a[i] = x" && !g.Nodes[2].Params.ContainsKey("brandNew"), "原图参数没变");
            Case(ref n, ref ok, "clone 加节点不动原图", g.Nodes.Count == 4, "原图节点数 " + g.Nodes.Count);
            Case(ref n, ref ok, "clone 加连线不动原图", g.Links.Count == 4, "原图连线数 " + g.Links.Count);
            Case(ref n, ref ok, "改 clone 的结构体不动原图", g.Structs[0].Elem == "char", "原图 elem=" + g.Structs[0].Elem);
            Case(ref n, ref ok, "改 clone 的 levelId/模式不动原图", g.LevelId == "S2" && g.ExprMode, "原图头信息没变");
            Case(ref n, ref ok, "改 clone 后原图序列化文本一字不差", GraphIO.Serialize(g) == before, "文本一致");
            Case(ref n, ref ok, "Clone(null) 返回 null", GraphIO.Clone(null) == null, "null");
            Case(ref n, ref ok, "Clone 保住连线 kind", GraphIO.Clone(g).Links[3].Kind == LinkKind.Data, "data");
        }

        // ---------------- 坏输入不抛异常 ----------------

        static Graph R(string text, ref int n, ref int ok, string name, bool wantError, string wantInError)
        {
            string err;
            Graph g = null;
            try { g = GraphIO.Deserialize(text, out err); }
            catch (Exception ex) { Case(ref n, ref ok, name + " 不抛异常", false, "抛了 " + ex.GetType().Name); return null; }
            if (wantError) Case(ref n, ref ok, name, g == null && !string.IsNullOrEmpty(err) && (wantInError == null || err.Contains(wantInError)), "null + " + err);
            else Case(ref n, ref ok, name, g != null && err == null, g == null ? "意外报错 " + err : "读回来了");
            return g;
        }

        static void BadInput(ref int n, ref int ok)
        {
            R(null, ref n, ref ok, "null 输入报错不抛", true, null);
            R("", ref n, ref ok, "空串报错不抛", true, null);
            R("   \n\t ", ref n, ref ok, "纯空白报错不抛", true, null);
            R("这一看就不是 json", ref n, ref ok, "非 JSON 报错不抛", true, null);
            R("{\"v\":1,\"nodes\":[{\"uid\":\"e\"", ref n, ref ok, "半截 JSON 报错不抛", true, null);
            R("[1,2]", ref n, ref ok, "顶层是数组报错不抛", true, null);
            R("{\"v\":1,\"levelId\":\"A\"}", ref n, ref ok, "缺 nodes 报错并点名 nodes", true, "nodes");
            R("{\"v\":99,\"nodes\":[]}", ref n, ref ok, "版本比程序新拒收", true, "版本");
            R("{\"nodes\":[\"我不是对象\"]}", ref n, ref ok, "数组元素形状不对报错", true, null);
            R("{\"nodes\":[{\"type\":\"entry\"}]}", ref n, ref ok, "节点缺 uid 报错并点名 uid", true, "uid");
            R("{\"nodes\":[],\"structs\":3}", ref n, ref ok, "structs 不是数组报错", true, null);
            R("{\"nodes\":[],\"links\":3}", ref n, ref ok, "links 不是数组报错", true, null);
            R("{\"nodes\":[{\"uid\":\"e\",\"params\":[1,2]}]}", ref n, ref ok, "params 不是对象报错", true, null);

            var g = R("{\"v\":1,\"zzz\":{\"a\":[1,2]},\"levelId\":\"A\",\"exprMode\":true,\"nodes\":[{\"uid\":\"e\",\"type\":\"entry\",\"x\":1,\"y\":2,\"future\":\"跳过我\",\"params\":{\"name\":\"a\",\"p2\":2}}],\"links\":[],\"next\":null}", ref n, ref ok, "未知字段整份跳过不报错", false, null);
            Case(ref n, ref ok, "params 里数字照字面转成字符串", g != null && GraphUtil.FindNode(g, "e").Params["p2"] == "2", g == null ? "没读回来" : "p2=2");
            Case(ref n, ref ok, "levelId/exprMode 未知字段干扰后仍在", g != null && g.LevelId == "A" && g.ExprMode, g == null ? "没读回来" : "A/true");

            var p = R("{\"nodes\":[{\"uid\":\"e\",\"type\":\"entry\"}]}", ref n, ref ok, "params 缺省不报错", false, null);
            Case(ref n, ref ok, "params 缺省给非空空字典", p != null && p.Nodes[0].Params != null && p.Nodes[0].Params.Count == 0, p == null ? "没读回来" : "空字典");
            Case(ref n, ref ok, "x/y 缺省给 0", p != null && p.Nodes[0].X == 0f && p.Nodes[0].Y == 0f, p == null ? "没读回来" : "0");
            Case(ref n, ref ok, "exprMode 缺省给 false", p != null && !p.ExprMode, p == null ? "没读回来" : "false");
            Case(ref n, ref ok, "links/structs 缺省给空表", p != null && p.Links.Count == 0 && p.Structs.Count == 0, p == null ? "没读回来" : "空表");

            var q = R("{\"nodes\":[{\"uid\":\"e\",\"x\":\"3.5\",\"y\":\"乱写\"}]}", ref n, ref ok, "坐标写成字符串不报错", false, null);
            Case(ref n, ref ok, "坐标字符串能认就认、不认给 0", q != null && q.Nodes[0].X == 3.5f && q.Nodes[0].Y == 0f, q == null ? "没读回来" : "3.5 / 0");

            var m = R("{\"nodes\":[],\"exprMode\":1,\"levelId\":7}", ref n, ref ok, "布尔/字符串写成数字不报错", false, null);
            Case(ref n, ref ok, "exprMode 写成 1 当 true", m != null && m.ExprMode, m == null ? "没读回来" : "true");
            Case(ref n, ref ok, "levelId 写成数字给空串默认值", m != null && m.LevelId == "", m == null ? "没读回来" : "空串");

            var k = R("{\"nodes\":[{\"uid\":\"a\"},{\"uid\":\"b\"}],\"links\":[{\"from\":\"a\",\"to\":\"b\",\"kind\":\"不认识的串\"},{\"from\":\"b\",\"to\":\"a\"}]}", ref n, ref ok, "kind 写不认识的串不报错", false, null);
            Case(ref n, ref ok, "认不出的 kind 保留这条线并按执行线处理", k != null && k.Links.Count == 2 && k.Links[0].Kind == LinkKind.Exec && k.Links[1].Kind == LinkKind.Exec, k == null ? "没读回来" : "两条 exec");

            var nest = R("{\"nodes\":[{\"uid\":\"e\",\"params\":{\"obj\":{\"a\":1},\"arr\":[1],\"t\":true,\"nl\":null}}]}", ref n, ref ok, "params 里嵌套对象/数组不报错", false, null);
            Case(ref n, ref ok, "params 里对象/数组整键丢掉", nest != null && !nest.Nodes[0].Params.ContainsKey("obj") && !nest.Nodes[0].Params.ContainsKey("arr"), nest == null ? "没读回来" : "只剩标量");
            Case(ref n, ref ok, "params 里布尔/null 按字面收进", nest != null && nest.Nodes[0].Params["t"] == "true" && nest.Nodes[0].Params["nl"] == "", nest == null ? "没读回来" : "true/空串");

            var u = R("{\"nodes\":[{\"uid\":\"e\",\"params\":{\"k\":\"\\u4e2d\\u6587\"}}]}", ref n, ref ok, "\\u 转义不报错", false, null);
            Case(ref n, ref ok, "\\u 转义读回中文", u != null && u.Nodes[0].Params["k"] == "中文", u == null ? "没读回来" : "中文");
        }

        // ---------------- Same ----------------

        static void SameCases(ref int n, ref int ok)
        {
            Case(ref n, ref ok, "null 与 null 相同", GraphIO.Same(null, null), "都空");
            Case(ref n, ref ok, "null 与非空不同", !GraphIO.Same(null, Minimal()) && !GraphIO.Same(Minimal(), null), "一边空");

            var a = Sample(); var b = GraphIO.Clone(a);
            b.Nodes.Reverse(); b.Links.Reverse(); b.Structs.Reverse();
            Case(ref n, ref ok, "列表顺序打乱仍算相同", GraphIO.Same(a, b), "顺序无关");
            Case(ref n, ref ok, "自己和自己相同", GraphIO.Same(a, a), "同一对象");

            var c = GraphIO.Clone(a); c.Nodes.Add(Nd("extra", "output", 1, 1));
            Case(ref n, ref ok, "多一个节点算不同", !GraphIO.Same(a, c), "节点数不等");

            var d = GraphIO.Clone(a); d.Nodes[2].Params.Remove("num");
            Case(ref n, ref ok, "参数少一个键算不同", !GraphIO.Same(a, d), "键数不等");

            var e = GraphIO.Clone(a); e.Nodes[2].Params["num"] = "3.5";
            Case(ref n, ref ok, "参数值不同算不同", !GraphIO.Same(a, e), "值不等");

            var f = GraphIO.Clone(a); f.Nodes[1].Y += 1e-6f;
            Case(ref n, ref ok, "坐标差 1e-6 算相同", GraphIO.Same(a, f), "容差内");
            var h = GraphIO.Clone(a); h.Nodes[1].Y += 1e-3f;
            Case(ref n, ref ok, "坐标差 1e-3 算不同", !GraphIO.Same(a, h), "超容差");

            var i2 = GraphIO.Clone(a); i2.Links[3].Kind = LinkKind.Exec;
            Case(ref n, ref ok, "连线 kind 变了算不同", !GraphIO.Same(a, i2), "kind 不等");
            var j = GraphIO.Clone(a); j.ExprMode = false;
            Case(ref n, ref ok, "exprMode 变了算不同", !GraphIO.Same(a, j), "模式不等");
            var k2 = GraphIO.Clone(a); k2.Structs[1].Name = "换个名";
            Case(ref n, ref ok, "结构体名字变了算不同", !GraphIO.Same(a, k2), "结构不等");
            var l = GraphIO.Clone(a); l.Nodes[0].Params["新键"] = "1"; l.Nodes[1].Params.Remove("empty");
            Case(ref n, ref ok, "两个节点各改一处仍判不同", !GraphIO.Same(a, l), "参数不等");
            var o = GraphIO.Clone(a); o.Nodes[2].Params["cn"] = ""; o.Nodes[2].Params.Remove("quote");
            Case(ref n, ref ok, "空串值与缺键不等价", !GraphIO.Same(a, o), "键存在性也参与比较");
        }

        // ---------------- 落盘 ----------------

        static void Disk(ref int n, ref int ok)
        {
            root = Path.GetFullPath(Path.Combine("Temp", "dayu-save-tests"));
            SaveSystem.CancelPending();
            SaveSystem.OnError = null;
            SaveSystem.OverrideRoot = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception) { }
            SaveSystem.OverrideRoot = root;

            string err;
            Case(ref n, ref ok, "没存档时 Load 返回 null 且不算错误", SaveSystem.Load("NOPE", out err) == null && err == null, err ?? "正常空态");
            Case(ref n, ref ok, "没存档时 HasSave 为假", !SaveSystem.HasSave("NOPE"), "假");

            var g = Sample();
            bool saved = SaveSystem.Save("S2", g, out err);
            Case(ref n, ref ok, "Save 成功并顺带建好目录", saved && err == null && Directory.Exists(root), err);
            Case(ref n, ref ok, "Save 后 HasSave 为真", SaveSystem.HasSave("S2"), "真");
            var back = SaveSystem.Load("S2", out err);
            Case(ref n, ref ok, "Load 拿回 Same 的图", back != null && err == null && GraphIO.Same(g, back), err ?? "等价");
            Case(ref n, ref ok, "存档文件就在 Root 里", File.Exists(Path.Combine(root, "S2.json")), Path.Combine(root, "S2.json"));
            Case(ref n, ref ok, "FileFor 指到同一个路径", SaveSystem.FileFor("S2") == Path.Combine(root, "S2.json"), SaveSystem.FileFor("S2") ?? "null");

            var g2 = Sample(); g2.LevelId = "S2"; g2.ExprMode = false;
            saved = SaveSystem.Save("S2", g2, out err);
            var back2 = SaveSystem.Load("S2", out err);
            Case(ref n, ref ok, "二次 Save 覆盖成最新内容", saved && back2 != null && !back2.ExprMode && GraphIO.Same(g2, back2) && !GraphIO.Same(g, back2), err ?? "覆盖 ok");
            Case(ref n, ref ok, "覆盖后不留 .tmp", TmpCount() == 0, "残留 " + TmpCount() + " 个");
            Case(ref n, ref ok, "Save 空图被拒绝（等于抹进度）", !SaveSystem.Save("S2", null, out err) && !string.IsNullOrEmpty(err), err ?? "拒绝");

            string ids = Path.Combine(root, "ids");
            Directory.CreateDirectory(ids);
            SaveSystem.OverrideRoot = ids;
            SaveSystem.Save("Z1", Minimal(), out err); SaveSystem.Save("A2", Minimal(), out err); SaveSystem.Save("M_3", Minimal(), out err);
            var list = Ids();
            Case(ref n, ref ok, "SavedLevelIds 列全且按字典序", list.Count == 3 && list[0] == "A2" && list[1] == "M_3" && list[2] == "Z1", Join(list));
            SaveSystem.Delete("Z1");
            Case(ref n, ref ok, "Delete 之后 HasSave 为假", !SaveSystem.HasSave("Z1") && Ids().Count == 2, Join(Ids()));
            SaveSystem.Delete("不存在");
            Case(ref n, ref ok, "Delete 不存在的档不炸", true, "静默返回");
        }

        // ---------------- 去抖自动保存 ----------------

        static void Debounce(ref int n, ref int ok)
        {
            SaveSystem.OverrideRoot = root;
            SaveSystem.CancelPending();
            SaveSystem.OnError = null;
            string err;

            var live = Sample(); live.LevelId = "AUTO";
            SaveSystem.Tick(0);   // 先把时钟钉在 0
            for (int i = 0; i < 5; i++) { live.Nodes[1].Y = 100f + i; SaveSystem.Request("AUTO", () => live); }
            Case(ref n, ref ok, "连按 5 次 Request 只留一份待写", SaveSystem.HasPending, "有 pending");
            Case(ref n, ref ok, "没越界前一次都不落盘", !SaveSystem.HasSave("AUTO"), "还没写");

            SaveSystem.Tick(0.5);
            Case(ref n, ref ok, "间隔没到 0.8 秒不落盘", !SaveSystem.HasSave("AUTO") && SaveSystem.HasPending, "仍待写");

            SaveSystem.Tick(0.9);
            var back = SaveSystem.Load("AUTO", out err);
            Case(ref n, ref ok, "越界后恰好落盘一次（内容是最后一次改动）", SaveSystem.HasSave("AUTO") && back != null && Math.Abs(back.Nodes[1].Y - 104f) < 1e-5, err ?? "Y=104");
            Case(ref n, ref ok, "落盘后 pending 清零", !SaveSystem.HasPending, "干净");

            File.Delete(Path.Combine(root, "AUTO.json"));
            SaveSystem.Tick(20);
            Case(ref n, ref ok, "没有新改动就不重复写盘", !SaveSystem.HasSave("AUTO"), "没写");

            // Request 内部必须深拷贝：调用方给的是正在被编辑的活图
            var live2 = Sample(); live2.LevelId = "CLONE";
            SaveSystem.Tick(0);
            live2.Nodes[1].Y = 10f;
            SaveSystem.Request("CLONE", () => live2);
            live2.Nodes[1].Y = 999f;
            SaveSystem.Flush();
            var c2 = SaveSystem.Load("CLONE", out err);
            Case(ref n, ref ok, "Flush 强制落盘并存的是按下那一刻的快照", c2 != null && Math.Abs(c2.Nodes[1].Y - 10f) < 1e-5 && !SaveSystem.HasPending, c2 == null ? err : "Y=10");

            SaveSystem.Tick(0);
            live2.Nodes[1].Y = 55f;
            SaveSystem.Request("CLONE", () => live2);
            SaveSystem.CancelPending();
            var c3 = SaveSystem.Load("CLONE", out err);
            Case(ref n, ref ok, "CancelPending 丢掉待写不污染档", !SaveSystem.HasPending && c3 != null && Math.Abs(c3.Nodes[1].Y - 10f) < 1e-5, c3 == null ? err : "还是 10");
            Case(ref n, ref ok, "去抖写盘不留 .tmp", TmpCount() == 0, "残留 " + TmpCount() + " 个");

            // 换了关卡 id 之后的 Request 覆盖前一份待写，不会把上一关写进新关的档
            SaveSystem.Tick(0);
            SaveSystem.Request("AUTO", () => live);
            SaveSystem.Request("OTHER", () => live2);
            SaveSystem.Flush();
            Case(ref n, ref ok, "待写目标以最后一次 Request 为准", SaveSystem.HasSave("OTHER") && !SaveSystem.HasSave("AUTO"), Join(Ids()));
            SaveSystem.CancelPending();
            SaveSystem.Delete("OTHER");
        }

        // ---------------- 非法 id / 坏档 ----------------

        static void BadIds(ref int n, ref int ok)
        {
            SaveSystem.OverrideRoot = root;
            SaveSystem.CancelPending();
            string err = null;
            int before = FileCount(root);
            var evil = new[] { "../evil", "..\\..\\evil", "a:b", "a/b", "a\\b", "a b", "", null, "S2.json", "..", new string('x', 40) };
            bool allRejected = true, everyErr = true;
            foreach (var id in evil)
            {
                if (SaveSystem.Save(id, Minimal(), out err)) allRejected = false;
                if (string.IsNullOrEmpty(err)) everyErr = false;
            }
            Case(ref n, ref ok, "非法/超长/空 id 的 Save 全被拒绝且都给 error", allRejected && everyErr && !string.IsNullOrEmpty(err), err);
            Case(ref n, ref ok, "非法 id 没在 Root 内多写文件", FileCount(root) == before, "条目 " + before + " → " + FileCount(root));
            Case(ref n, ref ok, "非法 id 没越出 Root 落盘", !File.Exists(Path.Combine(Path.GetDirectoryName(root), "evil.json")) && !File.Exists(root + ".json"), "目录外面干干净净");
            Case(ref n, ref ok, "非法 id 的 HasSave/Load/FileFor 都安全", !SaveSystem.HasSave("../evil") && SaveSystem.Load("a:b", out err) == null && !string.IsNullOrEmpty(err) && SaveSystem.FileFor("../evil") == null, err ?? "拒绝");
            Case(ref n, ref ok, "合法 id 认得下划线和连字符", SaveSystem.Save("L_3-x", Minimal(), out err) && SaveSystem.HasSave("L_3-x"), err ?? "ok");
            SaveSystem.Delete("L_3-x");
        }

        static void BrokenAndError(ref int n, ref int ok)
        {
            SaveSystem.OverrideRoot = root;
            string err;
            string half = "{\"v\":1,\"levelId\":\"BAD\",\"nodes\":[{\"uid\":\"e\",\"type\":\"entry\",\"x\":2";
            string badFile = Path.Combine(root, "BAD.json");
            File.WriteAllText(badFile, half, Encoding.UTF8);
            var g = SaveSystem.Load("BAD", out err);
            var bads = Directory.Exists(root) ? Directory.GetFiles(root, "BAD.json.bad-*") : new string[0];
            Case(ref n, ref ok, "坏档 Load 返回 null 并给 error", g == null && !string.IsNullOrEmpty(err), err ?? "没报错");
            Case(ref n, ref ok, "坏档自动备份成 .bad- 且只有一份", bads.Length == 1 && !File.Exists(badFile), bads.Length + " 份备份");
            Case(ref n, ref ok, "备份内容就是那段半截 JSON（没删没改）", bads.Length == 1 && File.ReadAllText(bads[0], Encoding.UTF8) == half, "内容一致");
            var list = Ids();
            Case(ref n, ref ok, "坏档与备份文件不进 SavedLevelIds 名单", !list.Contains("BAD"), Join(list));

            // 写盘失败：把 Root 指到一个「父级是文件」的路径，建目录必抛
            string blocker = Path.Combine(root, "blocker");
            File.WriteAllText(blocker, "我是文件不是目录", Encoding.UTF8);
            SaveSystem.OverrideRoot = Path.Combine(blocker, "saves");
            SaveSystem.CancelPending();
            bool failed = !SaveSystem.Save("X1", Minimal(), out err);
            Case(ref n, ref ok, "落盘失败 Save 返回 false 带 error", failed && !string.IsNullOrEmpty(err), err ?? "居然写成功了");
            string cb = null;
            SaveSystem.OnError = m => cb = m;
            SaveSystem.Tick(0);
            SaveSystem.Request("X1", () => Minimal());
            SaveSystem.Tick(9);
            Case(ref n, ref ok, "自动保存失败只走 OnError 回调不抛", !string.IsNullOrEmpty(cb) && !SaveSystem.HasPending, cb ?? "回调没被调到");
            cb = null;
            SaveSystem.OnError = m => { throw new Exception("UI 回调自己炸了"); };
            SaveSystem.Tick(0);
            SaveSystem.Request("X1", () => Minimal());
            bool threw = false;
            try { SaveSystem.Tick(9); } catch (Exception) { threw = true; }
            Case(ref n, ref ok, "OnError 回调炸了也不影响存档系统", !threw, threw ? "把异常抛给玩法循环了" : "吞掉了");
            SaveSystem.OnError = null;
            SaveSystem.OverrideRoot = root;
        }

        // ---------------- 工具 ----------------

        static List<string> Ids() { var l = new List<string>(SaveSystem.SavedLevelIds()); return l; }
        static string Join(List<string> l) { return string.Join(",", l.ToArray()); }

        static int TmpCount()
        {
            return Directory.Exists(root) ? Directory.GetFiles(root, "*.tmp").Length : 0;
        }

        static int FileCount(string dir)
        {
            return Directory.Exists(dir) ? Directory.GetFileSystemEntries(dir).Length : 0;
        }

        static void Cleanup()
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception ex) { Debug.Log("✘ " + Tag + " —— 临时目录没删干净：" + ex.Message); }
            SaveSystem.CancelPending();
            SaveSystem.OnError = null;
            SaveSystem.OverrideRoot = null;
        }

        static void Case(ref int n, ref int ok, string name, bool pass, string detail)
        {
            n++;
            if (pass) { ok++; Debug.Log("✔ " + Tag + " —— " + name); }
            else Debug.Log("✘ " + Tag + " —— " + name + " —— " + (detail ?? ""));
        }
    }
}
