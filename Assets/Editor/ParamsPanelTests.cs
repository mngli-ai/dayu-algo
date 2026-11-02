using System.Collections.Generic;
using Dayu.Core;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十二轮的三件事：失焦不等于提交、Tab 在填空位之间绕圈、
    // 「赋值」面板把 name + valueExpr 两个参数并成一条语句、提交时再拆回去。
    public static class ParamsPanelTests
    {
        public static void Run(ref int total, ref int passed)
        {
            BlurCommitCases(ref total, ref passed);
            TabSlotCases(ref total, ref passed);
            PickCases(ref total, ref passed);
            VarsetCases(ref total, ref passed);
            LoopPanelCases(ref total, ref passed);
            DropSideCases(ref total, ref passed);
            InspectorFoldCases(ref total, ref passed);
            DropLinkageCases(ref total, ref passed);
            ShapeCardCases(ref total, ref passed);
            PushExprCases(ref total, ref passed);
        }

        // 十三轮 #104：压入 / 入队的浮窗里直接写一个表达式，不再逼玩家连一根数据线过来。
        // VM 真跑一遍，压进去的必须是表达式算出来的值，不是字符串。
        static void PushExprCases(ref int total, ref int passed)
        {
            Check(ref total, ref passed, "压入的值端口对应 pushExpr", NodeTypes.ExprField("stack_push", "value"), "pushExpr");
            Check(ref total, ref passed, "入队的值端口对应 pushExpr", NodeTypes.ExprField("queue_push", "value"), "pushExpr");
            Check(ref total, ref passed, "弹出没有表达式字段（它只往外给值）", NodeTypes.ExprField("stack_pop", "value"), null);
            Check(ref total, ref passed, "压入卡面认这条表达式", NodeCard.ExprFieldKeys("stack_push") != null
                && System.Array.IndexOf(NodeCard.ExprFieldKeys("stack_push"), "pushExpr") >= 0, true);
            Check(ref total, ref passed, "NodeTypes 参数表里有压入表达式这一格",
                NodeTypes.Get("stack_push").Params.Exists(p => p.Id == "pushExpr"), true);
            Check(ref total, ref passed, "NodeTypes 参数表里有入队表达式这一格",
                NodeTypes.Get("queue_push").Params.Exists(p => p.Id == "pushExpr"), true);

            var g = new Graph { LevelId = "PUSHX" };
            g.Structs.Add(new StructDef { Id = "st", Kind = "stack", Name = "一摞数" });
            g.Nodes.AddRange(new[]
            {
                Nd("e", "entry"),
                Nd("new", "structnew", "structId", "st"),
                Nd("push", "stack_push", "structId", "st", "pushExpr", "3 + 4"),
                Nd("pop", "stack_pop", "structId", "st"),
                Nd("out", "output"),
            });
            g.Links.Add(new GraphLink { Kind = LinkKind.Exec, FromNode = "e", FromPort = "out", ToNode = "new", ToPort = "in" });
            g.Links.Add(new GraphLink { Kind = LinkKind.Exec, FromNode = "new", FromPort = "out", ToNode = "push", ToPort = "in" });
            g.Links.Add(new GraphLink { Kind = LinkKind.Exec, FromNode = "push", FromPort = "out", ToNode = "pop", ToPort = "in" });
            g.Links.Add(new GraphLink { Kind = LinkKind.Exec, FromNode = "pop", FromPort = "out", ToNode = "out", ToPort = "in" });
            g.Links.Add(new GraphLink { Kind = LinkKind.Data, FromNode = "pop", FromPort = "value", ToNode = "out", ToPort = "value" });

            var r = Compiler.Compile(g);
            Check(ref total, ref passed, "压入表达式图编译通过（缺数据线不再报错）",
                r.Ok, true);
            if (r.Ok)
            {
                var run = Vm.Run(r.Program, "");
                Check(ref total, ref passed, "压入表达式跑通", run.Error == null, true);
                Check(ref total, ref passed, "弹出的是算出来的 7", run.Output, "7");
                int pushed = run.Events.FindAll(x => x.Kind == "push").Count;
                Check(ref total, ref passed, "压入事件带出数值", pushed, 1);
            }

            // 容器名要能打进表达式（st.top 的 st），所以只认标识符：中文、空格、符号、数字开头全拒。
            Check(ref total, ref passed, "容器名收英文标识符", App.StructIdFor(" my_box2 "), "my_box2");
            Check(ref total, ref passed, "容器名不收中文", App.StructIdFor("一摞数"), "");
            Check(ref total, ref passed, "容器名不收空格与符号", App.StructIdFor("my box"), "");
            Check(ref total, ref passed, "容器名不能数字开头", App.StructIdFor("2a"), "");
            Check(ref total, ref passed, "容器名空着就是没填", App.StructIdFor("   "), "");
            var gg = new Graph { LevelId = "NAME" };
            gg.Structs.Add(new StructDef { Id = "st", Kind = "queue" });
            Check(ref total, ref passed, "按实例编号找到结构", App.StructDefOf(gg, "st") != null, true);
            Check(ref total, ref passed, "编号为空就找不到结构", App.StructDefOf(gg, null) == null, true);
            // 改名查重：容器、变量、循环变量在表达式里挤同一个名字空间
            gg.Structs.Add(new StructDef { Id = "box", Kind = "stack" });
            gg.Nodes.Add(Nd("d1", "vardecl", "name", "n", "vtype", "int"));
            gg.Nodes.Add(Nd("d2", "input", "name", "a[i]"));
            gg.Nodes.Add(Nd("lp", "forloop", "indexName", "i"));
            Check(ref total, ref passed, "容器名撞另一个容器", App.StructIdConflict(gg, gg.Structs[0], "box"), "已经有另一个容器叫「box」");
            Check(ref total, ref passed, "容器名撞变量", App.StructIdConflict(gg, gg.Structs[0], "n"), "变量「n」已经占了这个名字");
            Check(ref total, ref passed, "读入 a[i] 也算占了 a", App.StructIdConflict(gg, gg.Structs[0], "a"), "变量「a」已经占了这个名字");
            Check(ref total, ref passed, "容器名撞循环变量", App.StructIdConflict(gg, gg.Structs[0], "i"), "循环变量「i」已经占了这个名字");
            Check(ref total, ref passed, "没占过就能用", App.StructIdConflict(gg, gg.Structs[0], "stk"), "");
        }

        static GraphNode Nd(string uid, string type, params string[] kv)
        {
            var n = new GraphNode { Uid = uid, Type = type };
            for (int i = 0; i + 1 < kv.Length; i += 2) n.Params[kv[i]] = kv[i + 1];
            return n;
        }

        // 十三轮 #106：只回一个值的查询模块摘掉动画舞台，换成非矩形的小形状卡。
        // 形状卡能不能出现全看「那行字装不装得进形状」，所以这里把取舍规则钉死。
        static void ShapeCardCases(ref int total, ref int passed)
        {
            PixelText.EnsureAtlas();
            Check(ref total, ref passed, "常量走形状卡", NodeCard.IsShapeCard("const"), true);
            Check(ref total, ref passed, "长度走形状卡", NodeCard.IsShapeCard("len_of"), true);
            Check(ref total, ref passed, "栈·空了走形状卡", NodeCard.IsShapeCard("stack_empty"), true);
            Check(ref total, ref passed, "队列·个数走形状卡", NodeCard.IsShapeCard("queue_size"), true);
            Check(ref total, ref passed, "赋值不走形状卡（有执行端口）", NodeCard.IsShapeCard("varset"), false);
            Check(ref total, ref passed, "压栈不走形状卡（要看结构动）", NodeCard.IsShapeCard("stack_push"), false);
            Check(ref total, ref passed, "开始不走形状卡（门墩轮廓已经是外形）", NodeCard.IsShapeCard("entry"), false);
            Check(ref total, ref passed, "计数循环不走形状卡", NodeCard.IsShapeCard("forloop"), false);

            // 摘舞台：这六种模块不再有皮肤（也就不再有 64px 舞台），其余照旧
            foreach (var k in new[] { "const", "len_of", "stack_empty", "stack_size", "queue_empty", "queue_size" })
                Check(ref total, ref passed, "形状卡 " + k + " 无结构皮肤",
                    ModuleViz.KindFor(Seed(k, "x")), (string)null);
            Check(ref total, ref passed, "压栈仍有塔皮肤", ModuleViz.KindFor(Seed("stack_push", "st")), "tower");
            Check(ref total, ref passed, "读输入仍有带皮肤", ModuleViz.KindFor(Seed("input", "a")), "belt");

            // 高度：形状卡只留一行字 + 上下各 12，不再被身份行 / 舞台撑高
            Check(ref total, ref passed, "常量卡高",
                NodeCard.CardHeight(NodeTypes.Get("const"), true, true, Seed("const", "5")), PixelText.CellH + 24f);
            Check(ref total, ref passed, "输入卡高含舞台",
                NodeCard.CardHeight(NodeTypes.Get("input"), true, true, Seed("input", "a")) > PixelText.CellH + 24f, true);

            // 宽度：跟着那一行字走，不再给整块长方形的份额
            string label = NodeCard.ShapeCardLabel(Seed("const", "12345"));
            Check(ref total, ref passed, "常量卡宽 = 字宽加两侧留白",
                NodeCard.CardWidth(NodeTypes.Get("const"), Seed("const", "12345"), true),
                Mathf.Max(NodeCard.ShapeCardMinWidth, PixelText.MeasureString(label, 1).x + NodeCard.ShapeSidePad * 2f));
            // 端口画在卡边上、一半探进卡里；留白盖不住这一半，第一个字就压在端口上（截图验收发现的）
            Check(ref total, ref passed, "形状卡留白盖得住端口内半",
                NodeCard.ShapeSidePad > GraphView.PortSize * .5f, true);

            // 形状取舍：字短用理想形状，字长一点退到胶囊（中间仍有满宽的一段），再退圆角矩形，最后认输回矩形
            // 52x38 的卡上，14 高的内容带里圆形最宽只有 48.3、胶囊 49.3、圆角矩形整幅 52：
            // 所以 44 走圆形、49 退胶囊、51 退圆角矩形、80 谁也别想装下。
            Check(ref total, ref passed, "常量短字用圆形",
                NodeCard.PickShape("const", 0, 52f, 38f, 44f, 14f), CardShape.Circle);
            Check(ref total, ref passed, "常量长字退到胶囊",
                NodeCard.PickShape("const", 0, 52f, 38f, 49f, 14f), CardShape.Pill);
            Check(ref total, ref passed, "胶囊也装不下退到圆角矩形",
                NodeCard.PickShape("const", 0, 52f, 38f, 51f, 14f), CardShape.RoundRect);
            Check(ref total, ref passed, "常量塞不下就认输回矩形",
                NodeCard.PickShape("const", 0, 52f, 38f, 80f, 14f), CardShape.Rect);
            Check(ref total, ref passed, "非形状卡一律矩形",
                NodeCard.PickShape("varset", 0, 152f, 120f, 24f, 14f), CardShape.Rect);
            Check(ref total, ref passed, "栈·空了短卡用菱形",
                NodeCard.PickShape("stack_empty", 0, 96f, 38f, 24f, 14f), CardShape.Diamond);
            Check(ref total, ref passed, "队列·个数用胶囊",
                NodeCard.PickShape("queue_size", 0, 96f, 38f, 24f, 14f), CardShape.Pill);
        }

        // 形状卡那批用例只要「某个类型 + 一点种子参数」的临时节点，不认 uid。
        // 名字必须和下面那个 (uid, type, params) 的重载分开：两个都写成 Node 时，
        // 两参数的调用会绑到这个不加 params 的版本上，uid 被当成类型，用例全在测一个不存在的模块。
        static GraphNode Seed(string type, string seed)
        {
            var n = new GraphNode { Uid = "n_" + type + "_" + seed, Type = type };
            if (type == "const") { n.Params["vtype"] = "int"; n.Params["value"] = seed; }
            else if (type == "input" || type == "varset") n.Params["name"] = seed;
            else n.Params["structId"] = seed;
            return n;
        }

        // bug 现场：点第二个输入框，整扇参数窗消失。根因是抢焦点时先把上一格 blur，
        // 而 blur 也发了 OnCommitted，参数窗把 OnCommitted 接成了「提交并关窗」。
        static void BlurCommitCases(ref int total, ref int passed)
        {
            PixelText.EnsureAtlas();
            var root = new GameObject("ParamsPanelTestRoot", typeof(RectTransform));
            try
            {
                var a = PixelInputField.Create(root.transform, "x", 100, 20);
                var b = PixelInputField.Create(root.transform, "y", 100, 20);
                int aCommit = 0, aBlur = 0, bCommit = 0, bBlur = 0;
                a.OnCommitted = v => aCommit++;
                a.OnBlurred = v => aBlur++;
                b.OnCommitted = v => bCommit++;
                b.OnBlurred = v => bBlur++;

                a.Focus();
                b.Focus();                                        // = 鼠标点到第二个填空
                Check(ref total, ref passed, "点第二个填空 上一格只失焦一次", aBlur, 1);
                Check(ref total, ref passed, "点第二个填空 上一格不提交", aCommit, 0);
                Check(ref total, ref passed, "点第二个填空 焦点落在新一格", bBlur, 0);

                PixelInputField.FlushActive();                     // = 关窗前把在编辑的那格收掉
                Check(ref total, ref passed, "关窗冲刷 在编辑格只失焦", bBlur, 1);
                Check(ref total, ref passed, "关窗冲刷 不触发整窗提交", bCommit, 0);

                a.Blur();                                          // 已经失焦的框再 blur 不该重复报
                Check(ref total, ref passed, "重复失焦不再报第二次", aBlur, 1);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Tab / Shift+Tab 落在第几个填空位：只有一格时不该跳出窗口
        static void TabSlotCases(ref int total, ref int passed)
        {
            Check(ref total, ref passed, "Tab 单格时 slot 0 仍在 0", NodeParamsUI.NextTabSlot(1, 0, false), 0);
            Check(ref total, ref passed, "Tab 三格顺序走 slot 1 → 2", NodeParamsUI.NextTabSlot(3, 1, false), 2);
            Check(ref total, ref passed, "Tab 最后一格绕回第一格", NodeParamsUI.NextTabSlot(3, 2, false), 0);
            Check(ref total, ref passed, "Shift+Tab 第一格绕回最后一格", NodeParamsUI.NextTabSlot(3, 0, true), 2);
            Check(ref total, ref passed, "Shift+Tab 中间格往回", NodeParamsUI.NextTabSlot(4, 2, true), 1);
            Check(ref total, ref passed, "没有填空时 Tab 返回 -1", NodeParamsUI.NextTabSlot(0, 0, false), -1);
            // 同一次物理按键不能被下一个框再吃一遍，否则 Shift+Tab 原地弹回
            Check(ref total, ref passed, "首次按键处理 Tab", PixelInputField.TakesTab(120, -1), true);
            Check(ref total, ref passed, "同帧第二个框不再处理 Tab", PixelInputField.TakesTab(120, 120), false);
            Check(ref total, ref passed, "下一帧的按键照常处理", PixelInputField.TakesTab(121, 120), true);
        }

        // 候选变量按钮的换行（窗口要多长由它决定）与点选插入的文本规矩
        static void PickCases(ref int total, ref int passed)
        {
            var names = new[] { "a", "sum", "i", "n" };
            var widths = new[] { 20f, 34f, 20f, 20f };
            var cells = NodeParamsUI.LayoutPicks(names, widths, 80f, 5f);
            Check(ref total, ref passed, "候选变量按钮排两行", NodeParamsUI.PickRows(cells), 2);
            Check(ref total, ref passed, "第一行第二枚接在第一枚后面", (int)cells[1].X, 25);
            Check(ref total, ref passed, "放不下时换到第二行", cells[2].Row, 1);
            Check(ref total, ref passed, "第二行从最左开始", (int)cells[2].X, 0);
            Check(ref total, ref passed, "第二行第二枚跟着排", (int)cells[3].X, 25);
            Check(ref total, ref passed, "每个位置带回自己的名字", cells[3].Name, "n");
            Check(ref total, ref passed, "没有候选时不占行", NodeParamsUI.PickRows(NodeParamsUI.LayoutPicks(null, null, 80f, 5f)), 0);

            Check(ref total, ref passed, "点变量 空格补成赋值开头", NodeParamsUI.InsertAfterPick("", "sum"), "sum = ");
            Check(ref total, ref passed, "点变量 只有空格也补成赋值开头", NodeParamsUI.InsertAfterPick("   ", "a"), "a = ");
            Check(ref total, ref passed, "点变量 等号后面直接接上", NodeParamsUI.InsertAfterPick("s = ", "x"), "s = x");
            Check(ref total, ref passed, "点变量 下标写到一半直接接上", NodeParamsUI.InsertAfterPick("a[", "i"), "a[i");
            Check(ref total, ref passed, "点变量 已有操作数时留空格不粘字", NodeParamsUI.InsertAfterPick("s = 1", "x"), "s = 1 x");
            Check(ref total, ref passed, "点变量 空名字不改原文", NodeParamsUI.InsertAfterPick("s = 1", ""), "s = 1");
        }

        // name + valueExpr 两个参数并成一条语句显示、提交时再拆回去
        static void VarsetCases(ref int total, ref int passed)
        {
            // 旧图：名字和表达式分开存，面板要拼成一句给玩家看
            var legacy = Varset("v1", "b", "a * 2");
            Check(ref total, ref passed, "赋值面板 旧图两段拼成一句", App.VarsetDisplay(legacy), "b = a * 2");
            App.VarsetApply(legacy, App.VarsetDisplay(legacy));
            Check(ref total, ref passed, "赋值提交 名字从左边解析", Param(legacy, "name"), "b");
            Check(ref total, ref passed, "赋值提交 整句原样存下", Param(legacy, "valueExpr"), "b = a * 2");

            // 复合赋值不能被显示成脱糖后的 s = s + 5，否则玩家以为模块被改坏了
            var compound = Varset("v2", "s", "s += 5");
            Check(ref total, ref passed, "赋值面板 复合赋值原样显示", App.VarsetDisplay(compound), "s += 5");
            App.VarsetApply(compound, "s += 5");
            Check(ref total, ref passed, "赋值提交 复合赋值没被脱糖", Param(compound, "valueExpr"), "s += 5");

            // 下标写法：名字取数组本体，下标留在语句里
            var indexed = Varset("v3", "a", "a[i] = x");
            Check(ref total, ref passed, "赋值面板 下标语句原样显示", App.VarsetDisplay(indexed), "a[i] = x");
            App.VarsetApply(indexed, "a[i] = x");
            Check(ref total, ref passed, "赋值提交 下标语句的名字取数组本体", Param(indexed, "name"), "a");

            // 只写了个名字就点确定：名字要留着，等玩家下次补右边
            var partial = Varset("v4", "sum", "");
            Check(ref total, ref passed, "赋值面板 空值显示成待填的左边", App.VarsetDisplay(partial), "sum = ");
            App.VarsetApply(partial, "sum =");
            Check(ref total, ref passed, "赋值提交 半截语句保住名字", Param(partial, "name"), "sum");

            // 清空 = 这个模块还没填，两个参数一起空掉，别让编译器拿着旧名字报错
            var cleared = Varset("v5", "b", "b = 3");
            App.VarsetApply(cleared, "");
            Check(ref total, ref passed, "赋值提交 清空后名字为空", Param(cleared, "name"), "");
            Check(ref total, ref passed, "赋值提交 清空后语句为空", Param(cleared, "valueExpr"), "");

            // 只有表达式、解析不成赋值：原样存着，名字不动，报错的活交给编译器
            var junk = Varset("v6", "c", "c * 2");
            App.VarsetApply(junk, "c * 2");
            Check(ref total, ref passed, "赋值提交 解析不成语句时名字不动", Param(junk, "name"), "c");
            Check(ref total, ref passed, "赋值提交 解析不成语句时原样存", Param(junk, "valueExpr"), "c * 2");

            // 改名跟着语句走：面板里把左边改成 ok，模块的 name 必须一起换
            var rename = Varset("v7", "b", "b = 3");
            App.VarsetApply(rename, "ok = 3");
            Check(ref total, ref passed, "赋值提交 改左边等于改变量名", Param(rename, "name"), "ok");

            // 「赋值」只剩一条语句：参数表里不能再摆名字 / 类型这两格
            var t = NodeTypes.Get("varset");
            Check(ref total, ref passed, "赋值模块标题改成赋值", t != null ? t.Title : "", "赋值");
            Check(ref total, ref passed, "赋值模块参数去掉类型", HasParam(t, "vtype"), false);
            Check(ref total, ref passed, "赋值模块保留语句字段", HasParam(t, "valueExpr"), true);
            Check(ref total, ref passed, "赋值模块参数只剩两格", t != null && t.Params != null ? t.Params.Count : 0, 2);

            // 空名字 + 有内容：编译器要明说缺什么，不能把值塞进一个没名字的变量
            var bare = G("VARSET-BARE");
            bare.Nodes.AddRange(new[] { Node("e", "entry"), Node("w", "varset", "valueExpr", "5 + 3") });
            Exec(bare, "e", "out", "w");
            var cr = Compiler.Compile(bare);
            Check(ref total, ref passed, "赋值缺目标时报中文错",
                !cr.Ok && cr.Errors.Exists(x => x.Message.Contains("还没写出要存给哪个变量")), true);

            // 什么都没填的赋值：一句中文错就够，不要再补一条「缺 value 输入」
            var empty = G("VARSET-EMPTY");
            empty.Nodes.AddRange(new[] { Node("e", "entry"), Node("w", "varset") });
            Exec(empty, "e", "out", "w");
            var er = Compiler.Compile(empty);
            Check(ref total, ref passed, "空赋值只报一条中文错",
                er.Errors.Count == 1 && er.Errors[0].Message.Contains("还没写出要存给哪个变量"), true);

            // 名字 + 只写了表达式的旧写法照样能跑：VM 口径没被这次改动破坏
            var legacyRun = G("VARSET-LEGACY");
            legacyRun.Nodes.AddRange(new[]
            {
                Node("e", "entry"),
                Node("d", "vardecl", "name", "b", "vtype", "int", "initExpr", "1"),
                Varset("w", "b", "b * 4"),
                Node("o", "output", "outputExpr", "b"),
            });
            Exec(legacyRun, "e", "out", "d");
            Exec(legacyRun, "d", "out", "w");
            Exec(legacyRun, "w", "out", "o");
            var rr = Compiler.Compile(legacyRun);
            total++;
            if (rr.Ok && Vm.Run(rr.Program, "").Output == "4")
            {
                passed++;
                Debug.Log("✔ 参数窗 —— 旧写法（名字 + 表达式）跑完还是 4");
            }
            else Debug.Log("✘ 参数窗 —— 旧写法（名字 + 表达式）跑完还是 4 —— "
                + (rr.Ok ? Show(Vm.Run(rr.Program, "").Output) : rr.Errors[0].Message));
        }

        // 第十三轮：计数循环的顺序开关（正序 / 倒序）与「同一份起点终点只出现一次」
        static void LoopPanelCases(ref int total, ref int passed)
        {
            var asc = Node("l1", "forloop", "indexName", "i", "fromExpr", "3", "toExpr", "1");
            var desc = Node("l2", "forloop", "indexName", "i", "fromExpr", "3", "toExpr", "1", "dir", "desc");

            Check(ref total, ref passed, "顺序标签 asc 读作正序", VarScope.DirLabelCn("asc"), "正序");
            Check(ref total, ref passed, "顺序标签 desc 读作倒序", VarScope.DirLabelCn("desc"), "倒序");
            Check(ref total, ref passed, "顺序标签空值兜底正序", VarScope.DirLabelCn(""), "正序");
            // 旧图根本没写过 dir：默认必须是正序，不能让老关卡突然倒着跑
            Check(ref total, ref passed, "旧图没写 dir 时默认正序", VarScope.DirOf(asc), VarScope.DirAsc);
            Check(ref total, ref passed, "dir=desc 认倒序", VarScope.IsDesc(desc), true);
            Check(ref total, ref passed, "卡面第一行正序不带字样", NodeCard.ForloopLine(asc, 0), "循环变量：i");
            Check(ref total, ref passed, "卡面第一行倒序带字样", NodeCard.ForloopLine(desc, 0), "循环变量：i · 倒序");

            // bug 现场：卡面写了「计数起点：3」「计数终点：1」，右侧栏又复述成「表达式：3」「表达式：1」
            Check(ref total, ref passed, "循环起点终点不再进右侧栏复述", App.CardLabeledExpr(asc, "fromExpr"), true);
            Check(ref total, ref passed, "循环终点同样不复述", App.CardLabeledExpr(asc, "toExpr"), true);
            Check(ref total, ref passed, "循环变量名仍走参数表", App.CardLabeledExpr(asc, "indexName"), false);
            Check(ref total, ref passed, "数组初始值卡面已画不复述",
                App.CardLabeledExpr(Node("d1", "vardecl", "name", "a", "vtype", "int[]"), "initExpr"), true);
            Check(ref total, ref passed, "标量初始值走参数表照常显示",
                App.CardLabeledExpr(Node("d2", "vardecl", "name", "s", "vtype", "int"), "initExpr"), false);
            Check(ref total, ref passed, "空节点不报错", App.CardLabeledExpr(null, "fromExpr"), false);

            var t = NodeTypes.Get("forloop");
            Check(ref total, ref passed, "循环模块参数四格", t != null && t.Params != null ? t.Params.Count : 0, 4);
            Check(ref total, ref passed, "循环模块有顺序参数", HasParam(t, "dir"), true);
            Check(ref total, ref passed, "循环模块顺序默认正序",
                t != null && t.Params.Exists(p => p.Id == "dir" && p.Default == VarScope.DirAsc), true);
            // 开关只做在表达式小窗头部一处，参数窗里不能再摆第二个
            Check(ref total, ref passed, "循环参数窗藏起顺序开关", App.LoopHiddenFromPopup("dir", false), true);
            Check(ref total, ref passed, "循环参数窗藏起起点终点", App.LoopHiddenFromPopup("fromExpr", true), true);
            Check(ref total, ref passed, "循环参数窗仍留循环变量名", App.LoopHiddenFromPopup("indexName", false), false);
        }

        // 第十三轮：落点方位摆放（新模块拖到旧模块身上，按落点相对旧模块中心的方位决定挨哪一边）、
        // 相邻间隔（循环体这种大框架要离得更远）、题栏折叠拉手、端口热区、模块栏二级目录默认收起。
        static void DropSideCases(ref int total, ref int passed)
        {
            // 宿主左上角 (100,100)、200x100 → 中心 (200,150)
            Check(ref total, ref passed, "落点压着中心线算下方",
                GraphView.DropSideOf(200f, 150f, 100f, 100f, 200f, 100f), GraphView.DropSide.Below);
            Check(ref total, ref passed, "落点在右半身算右边",
                GraphView.DropSideOf(290f, 150f, 100f, 100f, 200f, 100f), GraphView.DropSide.Right);
            Check(ref total, ref passed, "落点在左半身算左边",
                GraphView.DropSideOf(110f, 150f, 100f, 100f, 200f, 100f), GraphView.DropSide.Left);
            Check(ref total, ref passed, "落点在下半身边算下方",
                GraphView.DropSideOf(200f, 190f, 100f, 100f, 200f, 100f), GraphView.DropSide.Below);
            Check(ref total, ref passed, "落点在上半身边算上方",
                GraphView.DropSideOf(200f, 110f, 100f, 100f, 200f, 100f), GraphView.DropSide.Above);
            Check(ref total, ref passed, "偏移更大的一侧说话算数",
                GraphView.DropSideOf(290f, 190f, 100f, 100f, 200f, 100f), GraphView.DropSide.Right);

            // 又窄又高的卡（40x200，中心 (120,200)）：只看纵坐标它永远判成上/下，
            // 玩家明明点在右边却被丢到下面。偏移各自按半边长归一之后再比，才认得出左右。
            Check(ref total, ref passed, "窄高卡落点偏右仍算右边",
                GraphView.DropSideOf(139f, 150f, 100f, 100f, 40f, 200f), GraphView.DropSide.Right);
            Check(ref total, ref passed, "窄高卡落点偏左仍算左边",
                GraphView.DropSideOf(101f, 260f, 100f, 100f, 40f, 200f), GraphView.DropSide.Left);

            // 方位 → 接在宿主后面还是插到宿主前面
            Check(ref total, ref passed, "下方=接在后面", GraphView.DropAfter(GraphView.DropSide.Below), true);
            Check(ref total, ref passed, "右方=接在后面", GraphView.DropAfter(GraphView.DropSide.Right), true);
            Check(ref total, ref passed, "上方=插到前面", GraphView.DropAfter(GraphView.DropSide.Above), false);
            Check(ref total, ref passed, "左方=插到前面", GraphView.DropAfter(GraphView.DropSide.Left), false);

            // 相邻间隔：普通上下 12、左右 24；只要有一方是循环头就拉开，纵向还要把外框多占的像素并进来
            var plain = Node("p1", "output");
            var for1 = Node("p2", "forloop");
            var while1 = Node("p3", "whileloop");
            Check(ref total, ref passed, "普通模块上下间隔 12",
                GraphView.DropGap(GraphView.DropSide.Below, plain, plain, 0f), 12f);
            Check(ref total, ref passed, "普通模块左右间隔 24",
                GraphView.DropGap(GraphView.DropSide.Right, plain, plain, 0f), 24f);
            Check(ref total, ref passed, "循环头下面拉开到 48 再加外框",
                GraphView.DropGap(GraphView.DropSide.Below, for1, plain, 60f), 108f);
            Check(ref total, ref passed, "外框负数不反扣间隔",
                GraphView.DropGap(GraphView.DropSide.Below, for1, plain, -50f), 48f);
            Check(ref total, ref passed, "新模块是循环头同样拉开",
                GraphView.DropGap(GraphView.DropSide.Below, plain, for1, 0f), 48f);
            Check(ref total, ref passed, "条件循环也算大框架",
                GraphView.DropGap(GraphView.DropSide.Right, while1, plain, 0f), 72f);
            Check(ref total, ref passed, "循环头横向 72",
                GraphView.DropGap(GraphView.DropSide.Right, for1, plain, 0f), 72f);

            // 摆放后的左上角：宿主 (100,100) 200x100，新模块 60x40
            Check(ref total, ref passed, "靠右落在宿主右缘再加间隔",
                GraphView.DropTopLeft(GraphView.DropSide.Right, 100f, 100f, 200f, 100f, 60f, 40f, 24f).x, 324f);
            Check(ref total, ref passed, "靠右纵向跟宿主居中",
                GraphView.DropTopLeft(GraphView.DropSide.Right, 100f, 100f, 200f, 100f, 60f, 40f, 24f).y, 130f);
            Check(ref total, ref passed, "正下落在宿主底缘再加间隔",
                GraphView.DropTopLeft(GraphView.DropSide.Below, 100f, 100f, 200f, 100f, 60f, 40f, 12f).y, 212f);
            Check(ref total, ref passed, "正下横向跟宿主居中",
                GraphView.DropTopLeft(GraphView.DropSide.Below, 100f, 100f, 200f, 100f, 60f, 40f, 12f).x, 170f);
            Check(ref total, ref passed, "上方落在宿主顶缘减自身高再减间隔",
                GraphView.DropTopLeft(GraphView.DropSide.Above, 100f, 100f, 200f, 100f, 60f, 40f, 12f).y, 48f);
            Check(ref total, ref passed, "上方贴着画布顶不越过 0",
                GraphView.DropTopLeft(GraphView.DropSide.Above, 100f, 20f, 200f, 100f, 60f, 40f, 12f).y, 0f);
            Check(ref total, ref passed, "靠左落在宿主左缘减自身宽再减间隔",
                GraphView.DropTopLeft(GraphView.DropSide.Left, 100f, 100f, 200f, 100f, 60f, 40f, 24f).x, 16f);
            Check(ref total, ref passed, "靠左贴着画布左缘不越过 0",
                GraphView.DropTopLeft(GraphView.DropSide.Left, 30f, 100f, 200f, 100f, 60f, 40f, 24f).x, 0f);

            // 连接点做大：方块本身 13（原 10），外面再套一层透明热区 28。
            // 横排时热区纵向只到行距，超出就会跟上下邻居抢点击、也会盖住卡片拖动。
            Check(ref total, ref passed, "端口方块 13", GraphView.PortSize, 13f);
            Check(ref total, ref passed, "端口热区 28", GraphView.PortHitW, 28f);
            Check(ref total, ref passed, "热区比方块大", GraphView.PortHitW > GraphView.PortSize, true);
            Check(ref total, ref passed, "横排热区纵向不越过行距",
                GraphView.PortHitAlongRow, GraphView.PortRowH);

            // #99 收尾：默认「适应整张图」时机位只有 0.53，卡片坐标里的 13px 方块在屏幕上只剩 6.9，
            // 玩家照样点不准。端口跟头顶名牌一样按 1/zoom 反补，但顺着端口排开的那一侧有天花板 ——
            // 隔壁端口在屏幕上只离你 间距×zoom 远，热区补过头就把邻居的点击抢走了。
            Check(ref total, ref passed, "反补系数 = 1/zoom", GraphView.PortInvScale(.5f), 2f);
            Check(ref total, ref passed, "拉到最近时方块缩回屏幕原样",
                GraphView.PortInvScale(1.8f), 1f / 1.8f);
            Check(ref total, ref passed, "缩到最远时反补封顶（不无限放大）",
                GraphView.PortInvScale(.1f), 1f / .42f);
            Check(ref total, ref passed, "机位为 1 时尺寸不变（不破坏已验收的手感）",
                GraphView.PortDotScale(true, 1f), 1f);
            // 0.53 这类机位下：方块补到屏幕上约 13 像素（原来 6.9），热区纵向补到恒定 24
            Check(ref total, ref passed, "远机位方块屏幕边长≥12",
                GraphView.PortSize * GraphView.PortDotScale(true, .53f) * .53f >= 12f, true);
            Check(ref total, ref passed, "上下端口热区纵向恒定 24",
                GraphView.PortHitSize(true, .5f).y * .5f, 24f);
            Check(ref total, ref passed, "左右端口热区横向恒定 28",
                GraphView.PortHitSize(false, .5f).x * .5f, 28f);
            // 天花板：补到最狠也不越过相邻端口的间距
            Check(ref total, ref passed, "上下端口热区横向不越列距",
                GraphView.PortHitSize(true, .1f).x, GraphView.PortColPitch);
            Check(ref total, ref passed, "左右端口热区纵向不越行距",
                GraphView.PortHitSize(false, .1f).y, GraphView.PortRowH);
            Check(ref total, ref passed, "上下端口方块最远也只留 4px 缝",
                GraphView.PortSize * GraphView.PortDotScale(true, .1f), GraphView.PortColPitch - 4f);
            Check(ref total, ref passed, "左右端口方块不越行距",
                GraphView.PortSize * GraphView.PortDotScale(false, .1f) <= GraphView.PortRowH, true);
            // 方块放大只许朝卡片外走：内侧面留在原位，否则会把卡片里的标题与端口标签压住
            // （第一版没做外挪，P4 截图复核时端口把「读入」两个字盖掉一半，这条就是钉住它的）
            Check(ref total, ref passed, "机位为 1 时不外挪", GraphView.PortDotShift(true, 1f), 0f);
            Check(ref total, ref passed, "远机位方块内侧面仍留在卡片内 6.5",
                GraphView.PortSize * GraphView.PortDotScale(true, .5f) * .5f
                - GraphView.PortDotShift(true, .5f), GraphView.PortSize * .5f);
            Check(ref total, ref passed, "远机位方块外沿不超过列距",
                GraphView.PortDotShift(true, .5f) + GraphView.PortSize * GraphView.PortDotScale(true, .5f) * .5f
                <= GraphView.PortColPitch, true);

            // 模块栏二级目录：条目多的（栈、队列各六项）默认收起，只有一两项的（数组）默认展开
            var collapsed = App.DefaultCollapsedSubs();
            Check(ref total, ref passed, "数组默认展开", collapsed.Contains(App.SubKey("datastruct", "数组")), false);
            Check(ref total, ref passed, "栈默认收起", collapsed.Contains(App.SubKey("datastruct", "栈")), true);
            Check(ref total, ref passed, "队列默认收起", collapsed.Contains(App.SubKey("datastruct", "队列")), true);
            Check(ref total, ref passed, "超过两行才自动收起", App.SubAutoCollapseThreshold, 2);
        }

        // 题栏左边缘的书签拉手：栏宽和拉手位置必须是同一条算式，差一像素就是肉眼可见的错位
        static void InspectorFoldCases(ref int total, ref int passed)
        {
            Check(ref total, ref passed, "题栏展开是整宽", App.InspectorWidthFor(false), App.InspectorW);
            Check(ref total, ref passed, "题栏收起宽为 0", App.InspectorWidthFor(true), 0f);
            Check(ref total, ref passed, "展开时拉手贴住题栏左缘",
                App.InspectorTabOffset(App.InspectorWidthFor(false)).x, -App.InspectorW);
            Check(ref total, ref passed, "收起时拉手贴住屏幕右沿",
                App.InspectorTabOffset(App.InspectorWidthFor(true)).x, 0f);
            Check(ref total, ref passed, "拉手竖直位置不随栏宽变",
                App.InspectorTabOffset(0f).y, App.InspectorTabY);
            Check(ref total, ref passed, "拉手比题栏窄得多", App.InspectorTabW < App.InspectorW * .2f, true);
            Check(ref total, ref passed, "拉手比题栏矮得多", App.InspectorTabH < 200f, true);
        }

        // 落点联动：接上线以后把前面那块定好的变量名抄给后面那块空白格子
        static void DropLinkageCases(ref int total, ref int passed)
        {
            var declA = Node("d1", "vardecl", "name", "a", "vtype", "int");
            var inp = Node("i1", "input", "vtype", "int");
            Check(ref total, ref passed, "创建变量→读入 抄写入变量", GraphView.ApplyDropLinkage(declA, inp), "输入 · 写入变量 a");
            Check(ref total, ref passed, "创建变量→读入 真的写进参数", Param(inp, "name"), "a");

            // 数组没有「读进来一个 a」这种写法，抄过去只会换来一句看不懂的编译错
            var declArr = Node("d2", "vardecl", "name", "b", "vtype", "int[]");
            var inpArr = Node("i2", "input", "vtype", "int");
            Check(ref total, ref passed, "数组声明→读入 不抄名字", GraphView.ApplyDropLinkage(declArr, inpArr), null);
            Check(ref total, ref passed, "数组声明→读入 格子保持空白", Param(inpArr, "name"), null);

            Check(ref total, ref passed, "创建变量→输出 抄成输出表达式",
                GraphView.ApplyDropLinkage(declA, Node("o1", "output")), "输出 · 输出表达式 a");
            var o2 = Node("o2", "output");
            GraphView.ApplyDropLinkage(declA, o2);
            Check(ref total, ref passed, "创建变量→输出 参数落对格子", Param(o2, "outputExpr"), "a");

            // 赋值显示成一条语句：左边先铺好，光标留给玩家补右边
            var set1 = Node("s1", "varset");
            Check(ref total, ref passed, "创建变量→赋值 抄名字并铺好左边",
                GraphView.ApplyDropLinkage(declA, set1), "赋值 · a = ");
            Check(ref total, ref passed, "赋值 抄到的语句", Param(set1, "valueExpr"), "a = ");
            Check(ref total, ref passed, "赋值 抄到的名字", Param(set1, "name"), "a");

            // 循环终点：标量抄变量名，数组抄长度
            var loop1 = Node("l1", "forloop");
            GraphView.ApplyDropLinkage(declA, loop1);
            Check(ref total, ref passed, "标量声明→循环 终点用变量名", Param(loop1, "toExpr"), "a");
            var loop2 = Node("l2", "forloop");
            GraphView.ApplyDropLinkage(declArr, loop2);
            Check(ref total, ref passed, "数组声明→循环 终点用个数", Param(loop2, "toExpr"), "b.size");

            // 反过来：把「输入」插到「创建变量」前面，声明的名字跟着读入走
            var decl2 = Node("d3", "vardecl", "vtype", "int");
            Check(ref total, ref passed, "读入→创建变量 抄名字",
                GraphView.ApplyDropLinkage(Node("i3", "input", "name", "sum"), decl2), "创建变量 · 名字 sum");

            // 玩家已经填过的格子一个字都不动
            var filled = Node("i4", "input", "name", "x");
            Check(ref total, ref passed, "已填过的格子不被覆盖", GraphView.ApplyDropLinkage(declA, filled), null);
            Check(ref total, ref passed, "已填过的格子保持原值", Param(filled, "name"), "x");

            // 名字带空格：抄过去之前先 trim，免得参数里存进一个带尾空格的变量名
            var declPad = Node("d4", "vardecl", "name", "  n  ", "vtype", "int");
            var inpPad = Node("i5", "input");
            GraphView.ApplyDropLinkage(declPad, inpPad);
            Check(ref total, ref passed, "抄过去的名字去掉首尾空格", Param(inpPad, "name"), "n");

            // 嵌套循环：外层循环变量抄成内层的起点
            var outer = Node("L1", "forloop", "indexName", "i");
            var inner = Node("L2", "forloop");
            Check(ref total, ref passed, "外层循环→内层循环 抄起点",
                GraphView.ApplyDropLinkage(outer, inner), "计数循环 · 起点 i");

            // 前面那块跟变量无关（输出）、或者两头根本没接上：什么都不抄
            Check(ref total, ref passed, "输出→读入 无名字可抄",
                GraphView.ApplyDropLinkage(Node("o3", "output", "outputExpr", "1"), Node("i6", "input")), null);
            Check(ref total, ref passed, "空名字模块什么都不抄",
                GraphView.ApplyDropLinkage(Node("d5", "vardecl", "name", ""), Node("i7", "input")), null);
            Check(ref total, ref passed, "宿主为空不报错", GraphView.ApplyDropLinkage(null, inp), null);
            Check(ref total, ref passed, "同一块模块自己抄自己不报错",
                GraphView.ApplyDropLinkage(declA, declA), null);
        }

        static GraphNode Varset(string uid, string name, string valueExpr)
        {
            return Node(uid, "varset", "name", name ?? "", "valueExpr", valueExpr ?? "");
        }

        static GraphNode Node(string uid, string type, params string[] ps)
        {
            var n = new GraphNode { Uid = uid, Type = type };
            for (int i = 0; i + 1 < ps.Length; i += 2) n.Params[ps[i]] = ps[i + 1];
            return n;
        }

        static Graph G(string id) { return new Graph { LevelId = id }; }

        static void Exec(Graph g, string from, string port, string to)
        {
            g.Links.Add(new GraphLink { FromNode = from, FromPort = port, ToNode = to, ToPort = "in", Kind = LinkKind.Exec });
        }

        static string Show(string text) { return string.IsNullOrEmpty(text) ? "（空）" : text.Replace("\r\n", "\n").Replace("\n", " | "); }

        static string Param(GraphNode n, string id)
        {
            string v;
            return n != null && n.Params != null && n.Params.TryGetValue(id, out v) ? v : null;
        }

        static bool HasParam(NodeType t, string id)
        {
            if (t == null || t.Params == null) return false;
            foreach (var p in t.Params) if (p != null && p.Id == id) return true;
            return false;
        }

        static void Check(ref int total, ref int passed, string label, int actual, int want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 参数窗 —— " + label + " = " + actual); }
            else Debug.Log("✘ 参数窗 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Check(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 参数窗 —— " + label + " = " + actual); }
            else Debug.Log("✘ 参数窗 —— " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }

        static void Check(ref int total, ref int passed, string label, bool actual, bool want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 参数窗 —— " + label); }
            else Debug.Log("✘ 参数窗 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Check(ref int total, ref int passed, string label, float actual, float want)
        {
            total++;
            if (Mathf.Abs(actual - want) < 0.01f) { passed++; Debug.Log("✔ 参数窗 —— " + label + " = " + actual); }
            else Debug.Log("✘ 参数窗 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Check(ref int total, ref int passed, string label,
                          GraphView.DropSide actual, GraphView.DropSide want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 参数窗 —— " + label + " = " + actual); }
            else Debug.Log("✘ 参数窗 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Check(ref int total, ref int passed, string label, CardShape actual, CardShape want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 参数窗 —— " + label + " = " + actual); }
            else Debug.Log("✘ 参数窗 —— " + label + " 期望 " + want + "，实际 " + actual);
        }
    }
}
