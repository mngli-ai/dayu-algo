using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十七轮⑪：主菜单左右两栏的横向分区。
    // 玩家截图上「深海值班中」五个字正好糊在「共 13 关 · 已通过 1 关」那一行上——题注当时走 MenuLine，
    // 那是按「整块弹窗」居中的，落点横穿进右栏；而右栏的进度行在竖向上又和题注带重叠，两者一撞就成一片糊。
    // 这里把「题注只许待在立绘那一栏里」写成数字断言：两栏横向不相交、题注带的左右缘都不越立绘栏，
    // 顺带把弹窗右缘、立绘框底边这些几何约束一起钉住，改任何一个坐标常量都会立刻报红。
    public static class MenuLayoutTests
    {
        const string Tag = "菜单排版";

        public static void Run(ref int total, ref int passed)
        {
            ColumnsDoNotOverlap(ref total, ref passed);
            CaptionStaysInArtColumn(ref total, ref passed);
            DegenerateTextWidths(ref total, ref passed);
            CaptionInsideDialog(ref total, ref passed);
            CastStripFits(ref total, ref passed);
            SceneryActivatesBeforeAnimRoutine(ref total, ref passed);
        }

        // ---------------- 两栏横向不相交 ----------------

        static void ColumnsDoNotOverlap(ref int total, ref int passed)
        {
            // 立绘栏右缘必须留出走道，不能一路铺到右栏左缘（贴着也不行，像素会互相压）
            Check(ref total, ref passed, "立绘栏与右栏之间有空隙",
                App.MenuColX - (App.MenuArtX + App.MenuArtW) >= 1f, true);
            // 右栏整体不出弹窗：MenuCol 的右缘要在 dialog 之内
            Check(ref total, ref passed, "右栏不出弹窗右缘",
                App.MenuColX + App.MenuColW <= App.MenuDialogW, true);
            // 立绘栏从弹窗左边起，不悬空
            Expect(ref total, ref passed, "立绘栏贴弹窗左缘", App.MenuArtX, 24f);
        }

        // ---------------- 题注带被关在立绘栏里 ----------------

        static void CaptionStaysInArtColumn(ref int total, ref int passed)
        {
            PixelText.EnsureAtlas();
            // 图集没加载时 MeasureString 全返回 0，宽度断言会变成假绿，所以先确认图集真的起来了
            Check(ref total, ref passed, "字体图集已加载",
                GlyphAtlas.Texture != null && PixelText.MeasureString("深海值班中", 2).x > 0f, true);

            float w = PixelText.MeasureString(App.MenuCaptionText, 2).x;
            float left = App.MenuCaptionLeft(w);
            Check(ref total, ref passed, "题注不越过立绘栏左缘（" + App.MenuCaptionText + " " + Mathf.CeilToInt(w) + "px）",
                left >= App.MenuArtX, true);
            Check(ref total, ref passed, "题注不越过立绘栏右缘",
                left + w <= App.MenuArtX + App.MenuArtW, true);
            // 这条才是本轮那个 bug 的直接拦截：题注带的右缘连右栏的左缘都碰不到，
            // 于是右栏里任何一行（进度、落款、上手两句）都不可能再和它叠字
            Check(ref total, ref passed, "题注与右栏横向零交叠", left + w <= App.MenuColX, true);
            Debug.Log("  · 题注带实测 x=" + Mathf.RoundToInt(left) + ".." + Mathf.CeilToInt(left + w)
                + "，立绘栏 " + App.MenuArtX + ".." + (App.MenuArtX + App.MenuArtW)
                + "，右栏起 " + App.MenuColX);

            // 竖向：题注顶边要在立绘框底边之下，否则会把框的下沿啃掉一截
            Check(ref total, ref passed, "题注在立绘框下方", App.MenuCaptionY >= App.MenuArtBottom, true);
        }

        // ---------------- 脏宽度：超长文案只能贴左缘，不许往右栏探 ----------------

        static void DegenerateTextWidths(ref int total, ref int passed)
        {
            // 文案一改长到超出立绘栏时，只许贴左缘放下（ Mathf.Max 兜住），不许算出负 x 往弹窗外跑，
            // 更不许像旧写法那样一路探进右栏
            Expect(ref total, ref passed, "超长题注贴立绘栏左缘",
                App.MenuCaptionLeft(App.MenuArtW + 400f), App.MenuArtX);
            // 恰好铺满立绘栏时也是左缘起步
            Expect(ref total, ref passed, "铺满宽度从左缘起", App.MenuCaptionLeft(App.MenuArtW), App.MenuArtX);
            // 零宽（空串 / 图集缺字）落在立绘栏正中，仍在栏内
            float zero = App.MenuCaptionLeft(0f);
            Check(ref total, ref passed, "零宽仍落在立绘栏内",
                zero >= App.MenuArtX && zero <= App.MenuArtX + App.MenuArtW, true);
            // 负宽当零宽处理：不能算出跑到左缘左边去
            Check(ref total, ref passed, "负宽不出左缘", App.MenuCaptionLeft(-60f) >= App.MenuArtX, true);
        }

        static void CaptionInsideDialog(ref int total, ref int passed)
        {
            float h = PixelText.CellH * 2;   // 题注是 2 号字，一行字块高 28
            Check(ref total, ref passed, "题注带不出弹窗底缘", App.MenuCaptionY + h <= App.MenuDialogH, true);
            // 落款两行挪进主菜单后占用 548/566，题注带（478..506）不能压到它们
            Check(ref total, ref passed, "题注不压到落款行", App.MenuCaptionY + h <= 548f, true);
        }

        // ---------------- 出场名单：名字各归各格 ----------------

        static void CastStripFits(ref int total, ref int passed)
        {
            PixelText.EnsureAtlas();
            var keys = Personas.Keys;
            // 七格排下来不能超出右栏宽，超了最右那只的名字会被裁到栏外
            Check(ref total, ref passed, "出场名单整条不出右栏（" + keys.Count + " 格 × " + App.CastSlotW + "px）",
                App.CastSlotW * keys.Count <= App.MenuColW, true);

            var missing = new System.Text.StringBuilder();
            foreach (var k in keys)
            {
                string nm = App.CastLabelFor(k);
                Check(ref total, ref passed, k + " 有中文名", nm != null && nm.Length > 0, true);
                if (nm == null) continue;
                foreach (var c in nm)
                    if (c != ' ' && !GlyphAtlas.TryGet(c, out _)) missing.Append(k + ":" + c + " ");
                float w = PixelText.MeasureString(nm, 1).x;
                // 名字在格内居中，左右各留 2px 才算不粘到邻格
                Check(ref total, ref passed, "名字塞进自己那一格（" + nm + "）",
                    w <= App.CastSlotW - 4f, true);
            }
            Check(ref total, ref passed, "出场名单字符全在图集里", missing.ToString(), "");
        }

        // ---------------- 底衬点亮必须在起协程之前（第十八轮④） ----------------
        //
        // MenuAnimRoutine 的循环条件是 menuBackdrop.activeSelf，而 Unity 的 StartCoroutine 会当场同步
        // 执行到第一个 yield 为止——也就是循环条件在启动那一刻就被判一次。底衬还关着就起协程，第一次判断
        // 即为假，协程当场结束，主菜单的气泡 / 浪 / 立绘呼吸 / 每 3 秒换姿势全都不会动一帧。
        // 这个缺陷从第十七轮舞台落地起就存在，r17 的截图验收没看出来（截图本来就不动），第十八轮换素材时
        // 拿两张不同时刻的姿势图对比才撞破。它不是「数字算错」，是「两句代码的先后」，所以这里直接读
        // App.cs 源文本按大括号配对抠出方法体，断言两句的顺序；配三条负对照（反序 / 缺一句 / 找不到方法），
        // 否则「顺序对」这三个字就是自证空话。

        const string Activate = "menuBackdrop.SetActive(true)";
        const string StartAnim = "StartCoroutine(MenuAnimRoutine())";

        static void SceneryActivatesBeforeAnimRoutine(ref int total, ref int passed)
        {
            string src = System.IO.File.ReadAllText(
                System.IO.Path.Combine(Application.dataPath, "Scripts", "UI", "App.cs"));
            Check(ref total, ref passed, "App.cs 源文件读到了（" + src.Length + " 字符）",
                src.Length > 100000, true);

            string scenery = BodyOf(src, "void MenuScenery()");
            Check(ref total, ref passed, "MenuScenery 方法体抠到了", scenery != null, true);
            Check(ref total, ref passed, "MenuScenery 里点亮底衬排在起协程之前",
                OrderName(OrderOf(scenery, Activate, StartAnim)), "顺序对");

            // 负对照①：同一对句子写反顺序，判「顺序反了」
            Check(ref total, ref passed, "反序判为不合规（负对照）",
                OrderName(OrderOf("{ " + StartAnim + "; " + Activate + "; }", Activate, StartAnim)), "顺序反了");
            // 负对照②：体里根本没有点亮那一句，判「找不到句子」——当年 ShowMainMenu 就是这个形态，
            // 协程在关着的底衬上起，屏幕上看不出任何异常
            Check(ref total, ref passed, "缺点亮那一句判为找不到（负对照）",
                OrderName(OrderOf("{ " + StartAnim + "; }", Activate, StartAnim)), "找不到句子");
            // 负对照③：签名压根不存在，BodyOf 返回 null，同样判「找不到句子」，不会误报成合规
            Check(ref total, ref passed, "方法名写错时不误报合规（负对照）",
                OrderName(OrderOf(BodyOf(src, "void NoSuchMethod()"), Activate, StartAnim)), "找不到句子");

            // 先后只留一处：ShowMainMenu 不再自己点亮底衬，避免两个地方各写一遍、改一处漏一处
            string menu = BodyOf(src, "void ShowMainMenu()");
            Check(ref total, ref passed, "ShowMainMenu 里不再重复点亮底衬",
                OrderName(OrderOf(menu, Activate, StartAnim)), "找不到句子");
        }

        static string OrderName(int v)
        {
            return v < 0 ? "找不到句子" : v == 0 ? "顺序反了" : "顺序对";
        }

        // 按大括号配对从源文本里抠出一个方法体（含最外层那对括号）；签名找不到或括号配不上就返回 null。
        static string BodyOf(string src, string signature)
        {
            if (src == null) return null;
            int at = src.IndexOf(signature, System.StringComparison.Ordinal);
            if (at < 0) return null;
            int open = src.IndexOf('{', at);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, i - open + 1);
                }
            }
            return null;
        }

        // 两句代码的先后：-1 = 体为 null 或至少一句不在体里，0 = 反了，1 = 前一句确实在后一句之前。
        static int OrderOf(string body, string before, string after)
        {
            if (body == null) return -1;
            int a = body.IndexOf(before, System.StringComparison.Ordinal);
            int b = body.IndexOf(after, System.StringComparison.Ordinal);
            if (a < 0 || b < 0) return -1;
            return a < b ? 1 : 0;
        }

        static void Expect(ref int total, ref int passed, string label, float actual, float want)
        {
            total++;
            if (Mathf.Abs(actual - want) < 0.01f)
            {
                passed++;
                Debug.Log("✔ " + Tag + " —— " + label + " = " + actual.ToString("F2"));
            }
            else Debug.Log("✘ " + Tag + " —— " + label + " 期望 " + want.ToString("F2") + "，实际 " + actual.ToString("F2"));
        }

        static void Check(ref int total, ref int passed, string label, bool actual, bool want)
        {
            total++;
            if (actual == want)
            {
                passed++;
                Debug.Log("✔ " + Tag + " —— " + label);
            }
            else Debug.Log("✘ " + Tag + " —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Check(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want)
            {
                passed++;
                Debug.Log("✔ " + Tag + " —— " + label);
            }
            else Debug.Log("✘ " + Tag + " —— " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }
    }
}
