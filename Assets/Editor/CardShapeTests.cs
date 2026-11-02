using System.Collections.Generic;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 十三轮的卡片外形：扫描线横条切出来的八种形状。
    // 全是纯几何断言，不建场景对象、不碰 Unity 场景，Bars / FitsContent / ForType 三个纯函数说了算。
    public static class CardShapeTests
    {
        const float Tol = 0.01f;

        public static void Run(ref int total, ref int passed)
        {
            RectCases(ref total, ref passed);
            BoundsCases(ref total, ref passed);
            CircleCases(ref total, ref passed);
            RoundRectCases(ref total, ref passed);
            DiamondCases(ref total, ref passed);
            TriangleCases(ref total, ref passed);
            TrapezoidCases(ref total, ref passed);
            HexagonCases(ref total, ref passed);
            PillCases(ref total, ref passed);
            FitsCases(ref total, ref passed);
            ForTypeCases(ref total, ref passed);
            StableCases(ref total, ref passed);
        }

        // ---------------- 取样 ----------------
        static float[] Widths(List<Vector4> bars)
        {
            var r = new float[bars.Count];
            for (int i = 0; i < r.Length; i++) r[i] = bars[i].z;
            return r;
        }

        // 行区间互不重叠、且每行正好从上一行的行尾接着往下走
        static bool RowsStacked(List<Vector4> bars)
        {
            for (int i = 0; i < bars.Count; i++)
            {
                if (bars[i].y < 0f || bars[i].w <= 0f) return false;
                if (i > 0 && bars[i].y + 0.001f < bars[i - 1].y + bars[i - 1].w) return false;
            }
            return true;
        }

        // 上下镜像对称：第 i 行和第 n-1-i 行一样宽
        static bool Symmetric(float[] w)
        {
            for (int i = 0; i < w.Length / 2; i++)
                if (Mathf.Abs(w[i] - w[w.Length - 1 - i]) > Tol) return false;
            return true;
        }

        // 先不减后不增（圆角矩形、六边形腰上是一段平的，所以不能要求严格）
        static bool UpThenDown(float[] w)
        {
            int top = 0;
            for (int i = 1; i < w.Length; i++) if (w[i] > w[top] + Tol) top = i;
            for (int i = 1; i <= top; i++) if (w[i] < w[i - 1] - Tol) return false;
            for (int i = top + 1; i < w.Length; i++) if (w[i] > w[i - 1] + Tol) return false;
            return true;
        }

        static bool StrictlyDown(float[] w)
        {
            for (int i = 1; i < w.Length; i++) if (w[i] >= w[i - 1] - Tol) return false;
            return true;
        }

        // 每一条都夹在卡片框内，而且宽度都算数（> 0）
        static bool Inside(List<Vector4> bars, float cw, float ch)
        {
            foreach (var b in bars)
                if (b.x < -Tol || b.y < -Tol || b.z <= 0f || b.w <= 0f ||
                    b.x + b.z > cw + Tol || b.y + b.w > ch + Tol) return false;
            return true;
        }

        // ---------------- 矩形 ----------------
        static void RectCases(ref int total, ref int passed)
        {
            var b = CardShapes.Bars(CardShape.Rect, 100f, 40f);
            Check(ref total, ref passed, "矩形行数 = 高除以台阶", b.Count, Mathf.CeilToInt(40f / CardShapes.Pitch));
            Check(ref total, ref passed, "矩形第一条铺满整宽", b[0].z, 100f);
            Check(ref total, ref passed, "矩形第一条贴左缘", b[0].x, 0f);
            Check(ref total, ref passed, "矩形第一条贴顶上", b[0].y, 0f);
            bool allFull = true;
            foreach (var r in b) if (Mathf.Abs(r.z - 100f) > Tol || Mathf.Abs(r.x) > Tol) allFull = false;
            Check(ref total, ref passed, "矩形每一行都是整宽", allFull, true);
            Check(ref total, ref passed, "矩形行高等于台阶", b[3].w, CardShapes.Pitch);
            Check(ref total, ref passed, "矩形最后一行收到卡片底", b[b.Count - 1].y + b[b.Count - 1].w, 40f);
            Check(ref total, ref passed, "矩形各行首尾相接不重叠", RowsStacked(b), true);

            // 高不是台阶整数倍时最后一行只铺到卡底，不溢出
            var odd = CardShapes.Bars(CardShape.Rect, 100f, 35f);
            Check(ref total, ref passed, "矩形高度不整除时多出的半行也数进来", odd.Count, 18);
            Check(ref total, ref passed, "矩形最后一行收成剩余高度", odd[odd.Count - 1].w, 1f);
            Check(ref total, ref passed, "矩形最后一行不越过卡片底", odd[odd.Count - 1].y + odd[odd.Count - 1].w, 35f);

            Check(ref total, ref passed, "宽为 0 不出条", CardShapes.Bars(CardShape.Rect, 0f, 40f).Count, 0);
            Check(ref total, ref passed, "高为 0 不出条", CardShapes.Bars(CardShape.Rect, 100f, 0f).Count, 0);
            Check(ref total, ref passed, "负尺寸不出条", CardShapes.Bars(CardShape.Rect, -10f, -10f).Count, 0);
        }

        // ---------------- 所有形状的边界 ----------------
        static void BoundsCases(ref int total, ref int passed)
        {
            var shapes = new[] { CardShape.Rect, CardShape.RoundRect, CardShape.Hexagon, CardShape.Diamond,
                                 CardShape.Circle, CardShape.TriangleDown, CardShape.Trapezoid, CardShape.Pill };
            var sizes = new Vector2[] { new Vector2(152f, 34f), new Vector2(88f, 42f), new Vector2(30f, 30f),
                                        new Vector2(26f, 26f), new Vector2(110f, 33f), new Vector2(196f, 116f) };
            foreach (var s in shapes)
            {
                bool ok = true, stacked = true, notTooMany = true;
                foreach (var z in sizes)
                {
                    var bars = CardShapes.Bars(s, z.x, z.y);
                    if (!Inside(bars, z.x, z.y)) ok = false;
                    if (!RowsStacked(bars)) stacked = false;
                    if (bars.Count > Mathf.CeilToInt(z.y / CardShapes.Pitch)) notTooMany = false;
                }
                Check(ref total, ref passed, s + " 每一条都落在卡片框内且宽度大于 0", ok, true);
                Check(ref total, ref passed, s + " 各行按台阶递进不重叠", stacked, true);
                Check(ref total, ref passed, s + " 行数不超过 ceil(高/台阶)", notTooMany, true);
            }
        }

        // ---------------- 圆 / 圆角矩形 ----------------
        static void CircleCases(ref int total, ref int passed)
        {
            var b = CardShapes.Bars(CardShape.Circle, 60f, 60f);
            var w = Widths(b);
            Check(ref total, ref passed, "圆行数 = 高除以台阶", b.Count, 30);
            Check(ref total, ref passed, "圆上下对称", Symmetric(w), true);
            Check(ref total, ref passed, "圆行宽先增后减", UpThenDown(w), true);
            Check(ref total, ref passed, "圆第一行比中间行窄", w[0] < w[w.Length / 2] - 1f, true);
            Check(ref total, ref passed, "圆最后一行比中间行窄", w[w.Length - 1] < w[w.Length / 2] - 1f, true);
            int top = 0;
            for (int i = 1; i < w.Length; i++) if (w[i] > w[top]) top = i;
            Check(ref total, ref passed, "圆最宽行落在中线附近", Mathf.Abs(top - w.Length / 2) <= 1, true);
            Check(ref total, ref passed, "圆最宽行不超过卡片宽", w[top] <= 60f + Tol, true);
            Check(ref total, ref passed, "圆的左右边缘居中对称", Mathf.Abs(b[10].x - (60f - b[10].z) * 0.5f) <= Tol, true);
        }

        static void RoundRectCases(ref int total, ref int passed)
        {
            var w = Widths(CardShapes.Bars(CardShape.RoundRect, 150f, 40f));
            int plateaus = 0;
            foreach (var v in w) if (Mathf.Abs(v - 150f) <= Tol) plateaus++;
            Check(ref total, ref passed, "圆角矩形腰上有一段满宽直边", plateaus >= 1, true);
            Check(ref total, ref passed, "圆角矩形第一行被圆角收窄", w[0] < 150f - 1f, true);
            Check(ref total, ref passed, "圆角矩形首尾等宽（四角一样大）", Mathf.Abs(w[0] - w[w.Length - 1]) <= Tol, true);
            Check(ref total, ref passed, "圆角矩形上下对称", Symmetric(w), true);
            Check(ref total, ref passed, "圆角矩形行宽先增后减", UpThenDown(w), true);
            Check(ref total, ref passed, "圆角矩形收得比矩形少（只吃四个角）", w[0] > 150f * 0.8f, true);
        }

        // ---------------- 菱形 ----------------
        static void DiamondCases(ref int total, ref int passed)
        {
            var b = CardShapes.Bars(CardShape.Diamond, 88f, 42f);
            var w = Widths(b);
            int top = 0, widest = 0;
            for (int i = 1; i < w.Length; i++) if (w[i] > w[top] + Tol) top = i;
            for (int i = 0; i < w.Length; i++) if (w[i] > w[top] - Tol) widest++;
            Check(ref total, ref passed, "菱形只有一行最宽", widest, 1);
            Check(ref total, ref passed, "菱形最宽那行在卡片中线上", top, b.Count / 2);
            Check(ref total, ref passed, "菱形最宽行等于卡片宽", w[top], 88f);
            Check(ref total, ref passed, "菱形上半逐行变宽", StrictlyDown(ReversedTopHalf(w, top)), true);
            Check(ref total, ref passed, "菱形下半逐行变窄", StrictlyDown(SecondHalf(w, top)), true);
            Check(ref total, ref passed, "菱形上下对称", Symmetric(w), true);
            Check(ref total, ref passed, "菱形尖角收到一个台阶而不是 0 宽", w[0] > 0f, true);
            Check(ref total, ref passed, "菱形贴左缘居中", Mathf.Abs(b[top].x) <= Tol, true);
        }

        // 把上半段翻过来，好复用「严格递减」这一个断言
        static float[] ReversedTopHalf(float[] w, int top)
        {
            int n = top;
            var r = new float[n];
            for (int i = 0; i < n; i++) r[i] = w[top - 1 - i];
            return r;
        }

        static float[] SecondHalf(float[] w, int from)
        {
            var r = new float[w.Length - from - 1];
            for (int i = 0; i < r.Length; i++) r[i] = w[from + 1 + i];
            return r;
        }

        // ---------------- 倒三角 ----------------
        static void TriangleCases(ref int total, ref int passed)
        {
            var w = Widths(CardShapes.Bars(CardShape.TriangleDown, 124f, 34f));
            int top = 0, last = w.Length - 1;
            for (int i = 1; i < w.Length; i++) if (w[i] > w[top]) top = i;
            Check(ref total, ref passed, "倒三角第一行最宽", top, 0);
            Check(ref total, ref passed, "倒三角最后一行最窄", NarrowestIndex(w), last);
            Check(ref total, ref passed, "倒三角逐行收窄", StrictlyDown(w), true);
            Check(ref total, ref passed, "倒三角最后一行仍有宽度", w[last] > 0f, true);
            Check(ref total, ref passed, "倒三角第一行接近满宽", w[0] > 124f * 0.9f, true);
        }

        static int NarrowestIndex(float[] w)
        {
            int m = 0;
            for (int i = 1; i < w.Length; i++) if (w[i] < w[m]) m = i;
            return m;
        }

        // ---------------- 梯形 ----------------
        static void TrapezoidCases(ref int total, ref int passed)
        {
            var w = Widths(CardShapes.Bars(CardShape.Trapezoid, 122f, 34f));
            int last = w.Length - 1;
            Check(ref total, ref passed, "梯形第一行比末行宽", w[0] > w[last] + 1f, true);
            Check(ref total, ref passed, "梯形末行不为 0（梯形不是三角形）", w[last] >= 122f * 0.45f, true);
            Check(ref total, ref passed, "梯形逐行收窄", StrictlyDown(w), true);
            Check(ref total, ref passed, "梯形第一行接近满宽", w[0] > 122f * 0.9f, true);
        }

        // ---------------- 六边形 ----------------
        static void HexagonCases(ref int total, ref int passed)
        {
            var w = Widths(CardShapes.Bars(CardShape.Hexagon, 88f, 40f));
            int waist = 0;
            foreach (var v in w) if (Mathf.Abs(v - 88f) <= Tol) waist++;
            int up = 0, down = 0;
            for (int i = 0; i < w.Length; i++) if (w[i] < 88f - Tol) { if (i < w.Length / 2) up++; else down++; }
            Check(ref total, ref passed, "六边形中间一段等宽（平顶的腰）", waist >= 1, true);
            Check(ref total, ref passed, "六边形的腰就是卡片满宽", Mathf.Abs(Max(w) - 88f) <= Tol, true);
            Check(ref total, ref passed, "六边形上面收的阶数不少于两阶", up >= 2, true);
            Check(ref total, ref passed, "六边形上下收的阶数一样", up, down);
            Check(ref total, ref passed, "六边形顶边比腰窄", w[0] < 88f - 1f, true);
            Check(ref total, ref passed, "六边形顶边留了半块宽（不是尖角）", w[0] > 88f * 0.4f, true);
            Check(ref total, ref passed, "六边形上下对称", Symmetric(w), true);
            Check(ref total, ref passed, "六边形行宽先增后减", UpThenDown(w), true);
        }

        static float Max(float[] w)
        {
            float m = float.MinValue;
            foreach (var v in w) m = Mathf.Max(m, v);
            return m;
        }

        // ---------------- 胶囊 ----------------
        static void PillCases(ref int total, ref int passed)
        {
            var w = Widths(CardShapes.Bars(CardShape.Pill, 100f, 42f));
            Check(ref total, ref passed, "胶囊正中间一行满宽", w[w.Length / 2], 100f);
            Check(ref total, ref passed, "胶囊两端收成半圆", w[0] < 100f - 1f, true);
            Check(ref total, ref passed, "胶囊上下对称", Symmetric(w), true);
            Check(ref total, ref passed, "胶囊行宽先增后减", UpThenDown(w), true);
            Check(ref total, ref passed, "胶囊第一行还是条实心的", w[0] > 0f, true);
            var thin = CardShapes.Bars(CardShape.Pill, 74f, 26f);
            Check(ref total, ref passed, "小胶囊两端也收宽", Widths(thin)[0] < thin[thin.Count / 2].z, true);
        }

        // ---------------- 内容能不能装下 ----------------
        static void FitsCases(ref int total, ref int passed)
        {
            Check(ref total, ref passed, "矩形永远容得下自身减一点点",
                  CardShapes.FitsContent(CardShape.Rect, 100f, 40f, 99f, 39f), true);
            Check(ref total, ref passed, "矩形容得下一行字",
                  CardShapes.FitsContent(CardShape.Rect, 152f, 34f, 120f, 14f), true);
            Check(ref total, ref passed, "同样尺寸下菱形容不下矩形能容下的宽内容",
                  CardShapes.FitsContent(CardShape.Diamond, 100f, 40f, 90f, 30f), false);
            Check(ref total, ref passed, "同样尺寸下矩形容得下那块内容",
                  CardShapes.FitsContent(CardShape.Rect, 100f, 40f, 90f, 30f), true);
            Check(ref total, ref passed, "30x30 的圆容不下 60 宽",
                  CardShapes.FitsContent(CardShape.Circle, 30f, 30f, 60f, 14f), false);
            Check(ref total, ref passed, "30x30 的圆容得下一个短值",
                  CardShapes.FitsContent(CardShape.Circle, 30f, 30f, 20f, 14f), true);
            Check(ref total, ref passed, "26x26 的圆刚好容得下一个字块",
                  CardShapes.FitsContent(CardShape.Circle, 26f, 26f, 14f, 14f), true);
            Check(ref total, ref passed, "60x60 的圆容得下 30x10",
                  CardShapes.FitsContent(CardShape.Circle, 60f, 60f, 30f, 10f), true);
            Check(ref total, ref passed, "60x60 的圆容不下 34x50 的高内容",
                  CardShapes.FitsContent(CardShape.Circle, 60f, 60f, 34f, 50f), false);
            Check(ref total, ref passed, "74x26 的小胶囊容得下一句话",
                  CardShapes.FitsContent(CardShape.Pill, 74f, 26f, 48f, 14f), true);
            Check(ref total, ref passed, "88x30 的六边形容得下一个名字",
                  CardShapes.FitsContent(CardShape.Hexagon, 88f, 30f, 48f, 14f), true);
            Check(ref total, ref passed, "122x34 的梯形容得下压入的值",
                  CardShapes.FitsContent(CardShape.Trapezoid, 122f, 34f, 60f, 14f), true);
            Check(ref total, ref passed, "112x30 的倒三角容得下「开始」两个字",
                  CardShapes.FitsContent(CardShape.TriangleDown, 112f, 30f, 24f, 14f), true);
            Check(ref total, ref passed, "112x30 的倒三角容不下六个字",
                  CardShapes.FitsContent(CardShape.TriangleDown, 112f, 30f, 72f, 14f), false);
            Check(ref total, ref passed, "菱形尺寸翻倍才装得下一行字",
                  CardShapes.FitsContent(CardShape.Diamond, 168f, 56f, 60f, 14f), true);
            Check(ref total, ref passed, "168x56 的菱形仍然容不下长表达式",
                  CardShapes.FitsContent(CardShape.Diamond, 168f, 56f, 130f, 14f), false);
            Check(ref total, ref passed, "内容宽高为 0 永远算容得下",
                  CardShapes.FitsContent(CardShape.Diamond, 20f, 20f, 0f, 0f), true);
            Check(ref total, ref passed, "比卡片还大的内容一律容不下",
                  CardShapes.FitsContent(CardShape.Rect, 20f, 20f, 21f, 10f), false);
        }

        // ---------------- 类型 → 形状 ----------------
        static void ForTypeCases(ref int total, ref int passed)
        {
            Check(ref total, ref passed, "开始不是矩形", CardShapes.ForType("entry", 0) != CardShape.Rect, true);
            Check(ref total, ref passed, "开始 = 倒三角", CardShapes.ForType("entry", 0), CardShape.TriangleDown);
            Check(ref total, ref passed, "输入 = 六边形", CardShapes.ForType("input", 1), CardShape.Hexagon);
            Check(ref total, ref passed, "输出 = 胶囊", CardShapes.ForType("output", 1), CardShape.Pill);
            Check(ref total, ref passed, "如果 = 菱形", CardShapes.ForType("branch", 1), CardShape.Diamond);
            Check(ref total, ref passed, "分支两格表达式就退回圆角矩形",
                  CardShapes.ForType("branch", 2), CardShape.RoundRect);
            Check(ref total, ref passed, "比较 = 菱形", CardShapes.ForType("compare", 0), CardShape.Diamond);
            Check(ref total, ref passed, "逻辑 = 菱形", CardShapes.ForType("logic", 0), CardShape.Diamond);
            Check(ref total, ref passed, "创建变量两格时能装字",
                  CanHoldText(CardShapes.ForType("vardecl", 2)), true);
            Check(ref total, ref passed, "创建变量一格时是矩形", CardShapes.ForType("vardecl", 1), CardShape.Rect);
            Check(ref total, ref passed, "赋值两格表达式时回到圆角矩形", CardShapes.ForType("varset", 2), CardShape.RoundRect);
            Check(ref total, ref passed, "读取变量 = 矩形", CardShapes.ForType("varget", 1), CardShape.Rect);
            Check(ref total, ref passed, "常量是小形状", IsSmall(CardShapes.ForType("const", 1)), true);
            Check(ref total, ref passed, "常量 = 小圆", CardShapes.ForType("const", 1), CardShape.Circle);
            Check(ref total, ref passed, "长字符串常量回到能装字的形状",
                  CanHoldText(CardShapes.ForType("const", 2)), true);
            Check(ref total, ref passed, "计数循环不是菱形", CardShapes.ForType("forloop", 2) != CardShape.Diamond, true);
            Check(ref total, ref passed, "计数循环不是圆", CardShapes.ForType("forloop", 2) != CardShape.Circle, true);
            Check(ref total, ref passed, "条件循环不是菱形", CardShapes.ForType("whileloop", 1) != CardShape.Diamond, true);
            Check(ref total, ref passed, "条件循环不是圆", CardShapes.ForType("whileloop", 1) != CardShape.Circle, true);
            Check(ref total, ref passed, "循环 = 圆角矩形（被外框包住）", CardShapes.ForType("whileloop", 1), CardShape.RoundRect);
            Check(ref total, ref passed, "创建栈 = 圆角矩形", CardShapes.ForType("structnew", 0), CardShape.RoundRect);
            Check(ref total, ref passed, "压入 = 梯形", CardShapes.ForType("stack_push", 1), CardShape.Trapezoid);
            Check(ref total, ref passed, "入队 = 梯形", CardShapes.ForType("queue_push", 1), CardShape.Trapezoid);
            Check(ref total, ref passed, "弹出 = 六边形", CardShapes.ForType("stack_pop", 0), CardShape.Hexagon);
            Check(ref total, ref passed, "出队 = 六边形", CardShapes.ForType("queue_pop", 0), CardShape.Hexagon);
            Check(ref total, ref passed, "栈顶元素 = 六边形", CardShapes.ForType("stack_top", 0), CardShape.Hexagon);
            Check(ref total, ref passed, "队头元素 = 六边形", CardShapes.ForType("queue_front", 0), CardShape.Hexagon);
            Check(ref total, ref passed, "栈·空了 = 小菱形", CardShapes.ForType("stack_empty", 0), CardShape.Diamond);
            Check(ref total, ref passed, "队列·空了 = 小菱形", CardShapes.ForType("queue_empty", 0), CardShape.Diamond);
            Check(ref total, ref passed, "栈·个数 = 小胶囊", CardShapes.ForType("stack_size", 0), CardShape.Pill);
            Check(ref total, ref passed, "队列长度 = 小胶囊", CardShapes.ForType("queue_size", 0), CardShape.Pill);
            Check(ref total, ref passed, "长度 = 小胶囊", CardShapes.ForType("len_of", 0), CardShape.Pill);
            Check(ref total, ref passed, "运算 = 矩形", CardShapes.ForType("binop", 1), CardShape.Rect);
            Check(ref total, ref passed, "取字符 = 矩形", CardShapes.ForType("text_index", 1), CardShape.Rect);
            Check(ref total, ref passed, "样例输入板 = 矩形", CardShapes.ForType("sample_input", 0), CardShape.Rect);
            Check(ref total, ref passed, "样例对比板 = 矩形", CardShapes.ForType("sample_compare", 0), CardShape.Rect);
            Check(ref total, ref passed, "没登记的类型一律用矩形", CardShapes.ForType("whatever", 0), CardShape.Rect);
            Check(ref total, ref passed, "空类型 key 也不崩", CardShapes.ForType(null, 0), CardShape.Rect);

            // 建议表里的小尺寸要真的装得下一块内容，否则就不该缩
            Check(ref total, ref passed, "常量 30x30 圆装得下一个短值",
                  CardShapes.FitsContent(CardShapes.ForType("const", 0), 30f, 30f, 20f, 14f), true);
            Check(ref total, ref passed, "空了 96x40 菱形装得下一句「是/否」",
                  CardShapes.FitsContent(CardShapes.ForType("stack_empty", 0), 96f, 40f, 36f, 14f), true);
            Check(ref total, ref passed, "个数 74x26 胶囊装得下三个字",
                  CardShapes.FitsContent(CardShapes.ForType("stack_size", 0), 74f, 26f, 36f, 14f), true);
            Check(ref total, ref passed, "开始 112x30 倒三角装得下招牌",
                  CardShapes.FitsContent(CardShapes.ForType("entry", 0), 112f, 30f, 24f, 14f), true);
            Check(ref total, ref passed, "压入 122x34 梯形装得下漏斗标签",
                  CardShapes.FitsContent(CardShapes.ForType("stack_push", 1), 122f, 34f, 60f, 14f), true);
        }

        // 能装字的形状：矩形和圆角矩形（腰上是整宽直边）
        static bool CanHoldText(CardShape s)
        { return s == CardShape.Rect || s == CardShape.RoundRect; }

        // 小形状 = 会切掉面积的那种，只给信息量少的模块用
        static bool IsSmall(CardShape s)
        {
            return s == CardShape.Circle || s == CardShape.Diamond || s == CardShape.Pill ||
                   s == CardShape.TriangleDown || s == CardShape.Trapezoid;
        }

        // ---------------- 幂等：同样的入参必须给同样的条，截图才稳定 ----------------
        static void StableCases(ref int total, ref int passed)
        {
            var shapes = new[] { CardShape.Rect, CardShape.RoundRect, CardShape.Hexagon, CardShape.Diamond,
                                 CardShape.Circle, CardShape.TriangleDown, CardShape.Trapezoid, CardShape.Pill };
            bool same = true;
            foreach (var s in shapes)
            {
                var a = CardShapes.Bars(s, 152f, 34f);
                var b = CardShapes.Bars(s, 152f, 34f);
                if (a.Count != b.Count) { same = false; continue; }
                for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) same = false;
            }
            Check(ref total, ref passed, "每个形状两次调用结果完全一致", same, true);

            // 台阶高度决定行数：Pitch 之外的尺寸不该多出整行
            var q = CardShapes.Bars(CardShape.Circle, 60f, 60f);
            Check(ref total, ref passed, "圆的行数就是 60/2", q.Count, 30);
            var r = CardShapes.Bars(CardShape.Diamond, 88f, 42f);
            Check(ref total, ref passed, "菱形的行数就是 42/2", r.Count, 21);
        }

        // ---------------- 断言出口 ----------------
        static void Check(ref int total, ref int passed, string label, bool actual, bool want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 卡片形状 —— " + label); }
            else Debug.Log("✘ 卡片形状 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Check(ref int total, ref int passed, string label, int actual, int want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 卡片形状 —— " + label + " = " + actual); }
            else Debug.Log("✘ 卡片形状 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Check(ref int total, ref int passed, string label, float actual, float want)
        {
            total++;
            if (Mathf.Abs(actual - want) < Tol) { passed++; Debug.Log("✔ 卡片形状 —— " + label + " = " + actual); }
            else Debug.Log("✘ 卡片形状 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Check(ref int total, ref int passed, string label, CardShape actual, CardShape want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 卡片形状 —— " + label + " = " + actual); }
            else Debug.Log("✘ 卡片形状 —— " + label + " 期望 " + want + "，实际 " + actual);
        }
    }
}
