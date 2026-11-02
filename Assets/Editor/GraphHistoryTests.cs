using System;
using System.Collections.Generic;
using Dayu.Core;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 撤销/恢复栈自测：只喂 Graph 对象，判据全部落在「深度、回到哪一步、历史不被玩家改动污染」上。
    // 用单节点图 Nodes[0].Y 当状态编号，回到哪一步一眼可验，不留假阳性。
    // 注意：断言里凡是会移动撤销指针的调用都单独取值，不在同一个布尔表达式里顺手 Undo，
    // 否则「撤销后另起一笔会作废待恢复尾巴」这条真实行为会被误当成 bug。
    public static class GraphHistoryTests
    {
        const string Tag = "撤销";

        public static void Run(ref int n, ref int ok)
        {
            Basic(ref n, ref ok);
            EquivalentDropped(ref n, ref ok);
            Batch(ref n, ref ok);
            Cap(ref n, ref ok);
            Copies(ref n, ref ok);
        }

        static GraphNode Nd(string uid, string type, float x, float y, params string[] ps)
        {
            var nd = new GraphNode { Uid = uid, Type = type, X = x, Y = y };
            for (int i = 0; i + 1 < ps.Length; i += 2) nd.Params[ps[i]] = ps[i + 1];
            return nd;
        }

        // 一张小图：entry 的 Y 就是状态编号，vardecl 的名字跟着编号，方便验参数也进历史
        static Graph G(int step)
        {
            var g = new Graph { LevelId = "HIS", ExprMode = true };
            g.Nodes.Add(Nd("e", "entry", 20, step));
            g.Nodes.Add(Nd("v", "vardecl", 250, step + 0.5f, "name", "a" + step, "vtype", "int"));
            g.Links.Add(new GraphLink { FromNode = "e", FromPort = "out", ToNode = "v", ToPort = "in", Kind = LinkKind.Exec });
            return g;
        }

        static float Step(Graph g) { return g == null ? -9999f : g.Nodes[0].Y; }
        static bool At(Graph g, int step) { return g != null && Math.Abs(g.Nodes[0].Y - step) < 1e-5; }

        static void Basic(ref int n, ref int ok)
        {
            var h = new GraphHistory();
            h.Reset(G(0));
            Case(ref n, ref ok, "Reset 后无可撤销无可恢复", !h.CanUndo && !h.CanRedo && h.UndoDepth == 0 && h.RedoDepth == 0, "深度 " + h.Depth);
            Case(ref n, ref ok, "Reset 栈底留了一份快照", h.Depth == 1, "栈里 " + h.Depth + " 条");
            Case(ref n, ref ok, "Reset 后撤销与恢复都返回 null", h.Undo() == null && h.Redo() == null, "两头都空");

            h.Commit(G(1)); h.Commit(G(2)); h.Commit(G(3));
            Case(ref n, ref ok, "提交三次后可撤销三层", h.CanUndo && h.UndoDepth == 3 && !h.CanRedo, "UndoDepth=" + h.UndoDepth);

            var u1 = h.Undo(); var u2 = h.Undo(); var u3 = h.Undo();
            Case(ref n, ref ok, "连着撤销三次逐个回到前一态", At(u1, 2) && At(u2, 1) && At(u3, 0), "回到 " + Step(u1) + "/" + Step(u2) + "/" + Step(u3));
            Case(ref n, ref ok, "一路撤到底后不能再撤", !h.CanUndo && h.Undo() == null, "到头了");

            var r1 = h.Redo(); var r2 = h.Redo(); var r3 = h.Redo();
            Case(ref n, ref ok, "恢复三次逐个回到后一态", At(r1, 1) && At(r2, 2) && At(r3, 3), "到 " + Step(r1) + "/" + Step(r2) + "/" + Step(r3));
            Case(ref n, ref ok, "恢复到底后不能再恢复", !h.CanRedo && h.Redo() == null, "到头了");

            // 撤销到新改动之后，旧的待恢复尾巴必须作废
            h.Undo();                       // 现在在 2
            h.Commit(G(9));                 // 从 2 另起一笔
            Case(ref n, ref ok, "撤销后另起一笔则待恢复尾巴作废", !h.CanRedo && h.RedoDepth == 0 && h.Depth == 4 && h.UndoDepth == 3, "RedoDepth=" + h.RedoDepth + "，栈里 " + h.Depth + " 条（原第 3 笔被砍掉）");
            Case(ref n, ref ok, "作废之后恢复不出东西", h.Redo() == null, "恢复到头了");
            var after = h.Undo();
            Case(ref n, ref ok, "另起一笔的前一层是 2", At(after, 2), "回到 " + Step(after));

            h.Reset(G(7));
            Case(ref n, ref ok, "Reset 把历史整个清空", h.Depth == 1 && !h.CanUndo && !h.CanRedo && !h.InBatch, "栈里 " + h.Depth + " 条");
        }

        static void EquivalentDropped(ref int n, ref int ok)
        {
            var h = new GraphHistory();
            var live = G(0);
            h.Reset(live);
            h.Commit(live);                                   // 原对象直接提交
            var clone = GraphIO.Clone(live);
            h.Commit(clone);                                   // 深拷贝但内容等价
            clone.Nodes.Reverse(); clone.Links.Reverse();
            h.Commit(clone);                                   // 只是列表顺序变了
            var tiny = GraphIO.Clone(live); tiny.Nodes[1].Y += 1e-6f;
            h.Commit(tiny);                                    // 坐标抖了 1e-6
            Case(ref n, ref ok, "四笔等价提交一条历史都不增", h.Depth == 1 && !h.CanUndo, "栈里 " + h.Depth + " 条");

            var moved = GraphIO.Clone(live); moved.Nodes[0].Y = 64f;
            h.Commit(moved);
            Case(ref n, ref ok, "真改了坐标才增一条", h.Depth == 2 && h.UndoDepth == 1, "栈里 " + h.Depth + " 条");

            var renamed = GraphIO.Clone(moved); renamed.Nodes[1].Params["name"] = "b";
            h.Commit(renamed);
            Case(ref n, ref ok, "改参数也算一笔", h.Depth == 3, "栈里 " + h.Depth + " 条");

            var linkOff = GraphIO.Clone(renamed); linkOff.Links.Clear();
            h.Commit(linkOff);
            Case(ref n, ref ok, "删连线也算一笔", h.Depth == 4, "栈里 " + h.Depth + " 条");

            var modeOff = GraphIO.Clone(linkOff); modeOff.ExprMode = false;
            h.Commit(modeOff);
            Case(ref n, ref ok, "改 exprMode 也算一笔", h.Depth == 5, "栈里 " + h.Depth + " 条");

            string err;
            var fromText = GraphIO.Deserialize(GraphIO.Serialize(modeOff), out err);
            h.Commit(fromText);
            Case(ref n, ref ok, "从存档文本读回来的同一状态再提交不增历史", h.Depth == 5 && err == null, "栈里 " + h.Depth + " 条");

            var addNode = GraphIO.Clone(fromText); addNode.Nodes.Add(Nd("x", "output", 1, 1));
            h.Commit(addNode);
            Case(ref n, ref ok, "加模块也算一笔", h.Depth == 6, "栈里 " + h.Depth + " 条");
        }

        static void Batch(ref int n, ref int ok)
        {
            var h = new GraphHistory();
            h.Reset(G(0));
            h.BeginBatch();
            Case(ref n, ref ok, "批内标记生效", h.InBatch, "InBatch=true");
            for (int i = 1; i <= 10; i++) { var g = GraphIO.Clone(h.Current()); g.Nodes[0].Y = 20f + i; h.Commit(g); }
            Case(ref n, ref ok, "批内十次提交不入栈", h.Depth == 1 && !h.CanUndo, "栈里 " + h.Depth + " 条");
            h.EndBatch();
            Case(ref n, ref ok, "整批只留一条历史", h.Depth == 2 && h.UndoDepth == 1 && !h.InBatch, "栈里 " + h.Depth + " 条");

            var beforeBatch = h.Undo();
            Case(ref n, ref ok, "撤销一次回到批前", At(beforeBatch, 0) && !h.CanUndo, "回到 " + Step(beforeBatch));
            var afterBatch = h.Redo();
            Case(ref n, ref ok, "恢复一次回到批末", At(afterBatch, 30), "到 " + Step(afterBatch));

            h.BeginBatch();
            var same = GraphIO.Clone(h.Current());            // 拖了一下又放回原位
            for (int i = 0; i < 3; i++) h.Commit(same);
            h.EndBatch();
            Case(ref n, ref ok, "批内内容没变则不入栈", h.Depth == 2 && h.UndoDepth == 1, "栈里 " + h.Depth + " 条");

            // 嵌套：只有最外层 EndBatch 真正入栈，且只留批内最后一次内容
            h.BeginBatch();
            h.BeginBatch();
            var nested = GraphIO.Clone(h.Current()); nested.Nodes[0].Y = 88f;
            h.Commit(nested);
            h.EndBatch();
            Case(ref n, ref ok, "内层 EndBatch 不入栈也不结束批", h.Depth == 2 && h.InBatch, "还在批里");
            var nested2 = GraphIO.Clone(h.Current()); nested2.Nodes[0].Y = 99f;
            h.Commit(nested2);
            h.EndBatch();
            Case(ref n, ref ok, "外层 EndBatch 才入栈且只留最后态", h.Depth == 3 && h.UndoDepth == 2 && !h.InBatch, "栈里 " + h.Depth + " 条");
            var n1 = h.Undo(); var n2 = h.Undo();
            Case(ref n, ref ok, "嵌套批合并成一条：一次撤销回到批前", At(n1, 30) && At(n2, 0) && !h.CanUndo, "批末回 " + Step(n1) + "、批前回 " + Step(n2));

            h.Reset(G(5));
            h.EndBatch();   // 没有配对 BeginBatch 的野调用
            Case(ref n, ref ok, "多余的 EndBatch 不把计数搞负", !h.InBatch && h.Depth == 1, "栈里 " + h.Depth + " 条");
            h.Commit(G(6));
            var got = h.Undo();
            Case(ref n, ref ok, "野 EndBatch 之后普通提交照常工作", h.Depth == 2 && At(got, 5), "回到 " + Step(got));
        }

        static void Cap(ref int n, ref int ok)
        {
            var h = new GraphHistory();
            h.Reset(G(0));
            for (int i = 1; i <= 65; i++) h.Commit(G(i));
            Case(ref n, ref ok, "历史条数封顶 60", h.Depth == GraphHistory.MaxDepth, "栈里 " + h.Depth + " 条");
            Case(ref n, ref ok, "可撤销深度是 59", h.UndoDepth == GraphHistory.MaxDepth - 1, "UndoDepth=" + h.UndoDepth);

            // 最老的几笔（0~5）被挤掉，但仍能一路撤到留下的栈底（第 6 笔）
            int steps = 0; bool neverNull = true;
            while (h.CanUndo) { if (h.Undo() == null) { neverNull = false; break; } steps++; }
            Case(ref n, ref ok, "能一路撤销到栈底不中断", neverNull && steps == GraphHistory.MaxDepth - 1, "撤了 " + steps + " 步");
            Case(ref n, ref ok, "栈底是第 6 笔（最老五笔被挤掉）", At(h.Current(), 6), "栈底 Y=" + Step(h.Current()));
            Case(ref n, ref ok, "栈底之后不能再撤但留着能恢复", !h.CanUndo && h.RedoDepth == GraphHistory.MaxDepth - 1, "RedoDepth=" + h.RedoDepth);

            Graph top = null; int redos = 0;
            while (h.CanRedo) { top = h.Redo(); redos++; }
            Case(ref n, ref ok, "一路恢复到顶回到第 65 笔", redos == GraphHistory.MaxDepth - 1 && At(top, 65) && h.Redo() == null, "恢复了 " + redos + " 步，顶是 " + Step(top));

            // 挤掉最老那笔之后，栈底那份还得跟着下移一格（永远留一份可撤到底的态）
            h.Commit(G(66));
            Case(ref n, ref ok, "封顶后再提交不涨深度也不留待恢复", h.Depth == GraphHistory.MaxDepth && h.RedoDepth == 0 && h.UndoDepth == GraphHistory.MaxDepth - 1, "栈里 " + h.Depth + " 条");
            steps = 0;
            while (h.CanUndo) { h.Undo(); steps++; }
            Case(ref n, ref ok, "最老的第 6 笔被挤掉后新栈底是第 7 笔", At(h.Current(), 7) && steps == GraphHistory.MaxDepth - 1, "栈底 Y=" + Step(h.Current()) + "，撤了 " + steps + " 步");
        }

        static void Copies(ref int n, ref int ok)
        {
            var h = new GraphHistory();
            h.Reset(G(0));
            h.Commit(G(1));
            string text0 = GraphIO.Serialize(G(0));

            var back = h.Undo();
            // 玩家拿到返回的图就开始改：历史里那份必须一个字都不动
            back.LevelId = "污染"; back.ExprMode = false;
            back.Nodes[0].Y = 7777f; back.Nodes[1].Params["name"] = "污染了";
            back.Nodes.Add(Nd("x", "output", 1, 1)); back.Links.Clear();

            Case(ref n, ref ok, "撤销返回的是独立副本（改它不动历史）", At(h.Current(), 0) && GraphIO.Serialize(h.Current()) == text0, "栈里仍是干净的 0");
            var r1 = h.Redo();
            Case(ref n, ref ok, "再恢复拿到的还是没被污染的第 1 笔", At(r1, 1) && GraphIO.Same(r1, G(1)) && GraphIO.Serialize(r1) == GraphIO.Serialize(G(1)), "回到干净的 1");
            var again = h.Undo();
            Case(ref n, ref ok, "每次撤销都返回不同实例", !ReferenceEquals(again, back) && !ReferenceEquals(again.Nodes, back.Nodes), "新对象");
            Case(ref n, ref ok, "被改过的那个副本不影响后续撤销的文本", GraphIO.Serialize(again) == text0, "文本可复现");

            // Reset 传进来的图同样不能被历史牵着走（这就是看答案预置图要先 Clone 的原因）
            var preset = G(3);
            h.Reset(preset);
            h.Commit(G(4));
            var u = h.Undo();
            u.Nodes[1].Params["name"] = "改了副本";
            Case(ref n, ref ok, "Reset 传入的原图不受历史操作影响", preset.LevelId == "HIS" && preset.ExprMode && preset.Nodes[1].Params["name"] == "a3" && GraphIO.Serialize(preset) == GraphIO.Serialize(G(3)), "预置图完好");
            Case(ref n, ref ok, "改过的副本不污染栈底那份文本", GraphIO.Serialize(h.Current()) == GraphIO.Serialize(G(3)), "栈底仍是预置图");
        }

        static void Case(ref int n, ref int ok, string name, bool pass, string detail)
        {
            n++;
            if (pass) { ok++; Debug.Log("✔ " + Tag + " —— " + name); }
            else Debug.Log("✘ " + Tag + " —— " + name + " —— " + (detail ?? ""));
        }
    }
}
