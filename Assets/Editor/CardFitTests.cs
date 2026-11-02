using System.Collections.Generic;
using UnityEngine;
using Dayu.Core;

namespace Dayu.EditorTools
{
    // 第十五轮 #115：卡片必须按内容量伸缩，且关卡答案图在新尺寸下真的排得开。
    // 玩家原话「创建变量、输入模块显然不需要现在这么大」——这里把「小」写成可失败的数字断言，
    // 再把「排得开」写成对全部关卡 Starter/Solution 的矩形相交检查，防止只把卡改小却改出压叠。
    public static class CardFitTests
    {
        const float Gap = 2f;

        public static void Run(ref int total, ref int passed)
        {
            SmallCardCases(ref total, ref passed);
            ScaleWithContentCases(ref total, ref passed);
            LevelLayoutCases(ref total, ref passed);
        }

        static GraphNode N(string uid, string type, float x, float y, params string[] ps)
        {
            var n = new GraphNode { Uid = uid, Type = type, X = x, Y = y };
            for (int i = 0; i + 1 < ps.Length; i += 2) n.Params[ps[i]] = ps[i + 1];
            return n;
        }

        static float H(GraphNode n) { return NodeCard.CardHeight(NodeTypes.Get(n.Type), true, true, n); }
        static float W(GraphNode n) { return NodeCard.CardWidth(NodeTypes.Get(n.Type), n, true); }

        // ---------------- 单行小卡压到能塞进 84 行距 ----------------
        private static void SmallCardCases(ref int total, ref int passed)
        {
            // 关卡答案图同列行距只有 84（循环头到体 140），卡高超过这个数就会互相压叠
            const int RowPitch = 84;

            var vd = N("vd", "vardecl", 0, 0, "name", "n", "vtype", "int");
            Expect(ref total, ref passed, "创建变量卡高进得去行距", H(vd) <= RowPitch - Gap, true);
            Expect(ref total, ref passed, "创建变量卡比旧口径(128)小", H(vd) < 128f, true);

            var inp = N("in", "input", 0, 0, "vtype", "int", "name", "n");
            Expect(ref total, ref passed, "输入卡高进得去行距", H(inp) <= RowPitch - Gap, true);
            Expect(ref total, ref passed, "输入卡比旧口径(128)小", H(inp) < 128f, true);

            var tower = N("ps", "stack_push", 0, 0, "structId", "st", "pushExpr", "x");
            Expect(ref total, ref passed, "内容多的压栈卡仍高于单行小卡", H(tower) > H(vd), true);

            // 宽度同理：单行小卡不该占满整块长方形的份额
            Expect(ref total, ref passed, "创建变量卡宽收到字宽", W(vd) < 152f, true);
        }

        // ---------------- 伸缩方向：内容多 → 更大，不是写死 ----------------
        private static void ScaleWithContentCases(ref int total, ref int passed)
        {
            var shortLoop = N("l1", "forloop", 0, 0, "varName", "i", "fromExpr", "0", "toExpr", "n");
            var longLoop = N("l2", "forloop", 0, 0, "varName", "i", "fromExpr", "0", "toExpr", "s.size - firstPassIndex");
            Expect(ref total, ref passed, "循环表达式写更长时卡变宽", W(longLoop) > W(shortLoop), true);

            var emptyBelt = N("b1", "input", 0, 0, "vtype", "int", "name", "n");
            var longBelt = N("b2", "input", 0, 0, "vtype", "int", "name", "secondValue");
            Expect(ref total, ref passed, "输入名字写更长时卡变宽", W(longBelt) > W(emptyBelt), true);

            // 同一类型：写了初始值与没写，高度必须一致（初始值写在名牌上，不另起一行）
            var bare = N("v1", "vardecl", 0, 0, "name", "a", "vtype", "int");
            var init = N("v2", "vardecl", 0, 0, "name", "a", "vtype", "int", "initExpr", "0");
            Expect(ref total, ref passed, "初始值写在名牌上不撑高", (H(init) - H(bare)).ToString(), "0");

            // 留白必须盖得住探进卡里的半个端口，否则第一个字压在端口点上（历史截图验收出过）
            Expect(ref total, ref passed, "形状卡留白盖得住端口内半",
                NodeCard.ShapeSidePad > GraphView.PortSize * .5f, true);
        }

        // ---------------- 全部关卡的图按真实卡尺排一遍，压叠即失败 ----------------
        private static void LevelLayoutCases(ref int total, ref int passed)
        {
            foreach (var lv in Levels.All)
            {
                CheckGraph(ref total, ref passed, lv.Id + " 初始图", lv.Starter);
                CheckGraph(ref total, ref passed, lv.Id + " 标准答案", lv.Solution);
            }
        }

        // 一张图只算一项，但把图上所有压叠一次报全：只报第一对会盖住后面的，改一轮才暴一轮
        private static void CheckGraph(ref int total, ref int passed, string label, Graph g)
        {
            total++;
            if (g == null || g.Nodes == null || g.Nodes.Count == 0)
            {
                passed++;
                Debug.Log("✔ 卡片排版 —— " + label + " 无节点，跳过");
                return;
            }
            var rects = new List<RectF>(g.Nodes.Count);
            foreach (var n in g.Nodes)
            {
                var t = NodeTypes.Get(n.Type);
                if (t == null) { Debug.Log("✘ 卡片排版 —— " + label + "：「" + n.Uid + "」类型 " + n.Type + " 查不到定义"); return; }
                float w = NodeCard.CardWidth(t, n, g.ExprMode), h = NodeCard.CardHeight(t, g.ExprMode, true, n);
                rects.Add(new RectF(n.X, n.Y, w, h, n.Type + "/" + n.Uid));
            }
            int bad = 0;
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    if (Overlaps(rects[i], rects[j], Gap))
                    {
                        bad++;
                        Debug.Log("✘ 卡片排版 —— " + label + "：" + rects[i].Who + " 与 " + rects[j].Who + " 压叠（"
                             + rects[i].X + "," + rects[i].Y + " 尺 " + (int)rects[i].W + "x" + (int)rects[i].H
                             + " 对 " + rects[j].X + "," + rects[j].Y + " 尺 " + (int)rects[j].W + "x" + (int)rects[j].H + "）");
                    }
            if (bad == 0) { passed++; Debug.Log("✔ 卡片排版 —— " + label + " " + rects.Count + " 张卡互不压叠"); }
        }

        struct RectF
        {
            public readonly float X, Y, W, H; public readonly string Who;
            public RectF(float x, float y, float w, float h, string who) { X = x; Y = y; W = w; H = h; Who = who; }
        }

        // 卡片锚点在左上角（GraphView.cs:11），所以 X/Y 就是左上角，直接做轴对齐矩形相交
        static bool Overlaps(RectF a, RectF b, float gap)
        {
            return a.X + gap < b.X + b.W && b.X + gap < a.X + a.W
                && a.Y + gap < b.Y + b.H && b.Y + gap < a.Y + a.H;
        }

        static void Expect(ref int total, ref int passed, string label, bool actual, bool want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 卡片排版 —— " + label); }
            else Debug.Log("✘ 卡片排版 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Expect(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 卡片排版 —— " + label + " = " + actual); }
            else Debug.Log("✘ 卡片排版 —— " + label + " 期望 " + want + "，实际 " + actual);
        }
    }
}
