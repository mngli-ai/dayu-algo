using System.Collections.Generic;
using Dayu.Core;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Dayu.EditorTools
{
    /// <summary>
    /// 成员自动补全自测：第十四轮需求「打完一个 `.` 就弹浮窗」的判据全在
    /// ExpressionPopoverUI.Candidates 这个纯函数里（给 draft + caret + 作用域变量表，
    /// 回该列哪几颗成员），所以这里不搭 Unity 场景也能把键位之外的对错钉死。
    /// 键位（上下键选、Tab / 回车采纳、Esc 只关列表）由玩家截图那轮核对。
    /// </summary>
    public static class CompletionPopupTests
    {
        // 供 Unity 批处理 -executeMethod 直接调用：Dayu.EditorTools.CompletionPopupTests.RunAll
        public static void RunAll()
        {
            int total = 0, passed = 0;
            Run(ref total, ref passed);
            Debug.Log("补全 共 " + total + " 项，通过 " + passed + " 项");
            if (total != passed) EditorApplication.Exit(1);
        }

        public static void Run(ref int total, ref int passed)
        {
            DotJustTypedCases(ref total, ref passed);
            PrefixFilterCases(ref total, ref passed);
            NoPopupCases(ref total, ref passed);
            ContainerTypeCases(ref total, ref passed);
            ScopeCases(ref total, ref passed);
        }

        // 数组与容器刚打完点号：整颗词表都列出来，顺序照 Members.Of（size、empty、取元素那颗）
        static void DotJustTypedCases(ref int total, ref int passed)
        {
            var arr = Vars(Var("a", "int[]"));
            Check(ref total, ref passed, "数组刚打点号列两颗", arr, "a.", 2, "size", "empty");

            var st = Vars(Var("st", "stack<int>"));
            Check(ref total, ref passed, "栈刚打点号带出栈顶那颗", st, "st.", 3, "size", "empty", "top");

            var q = Vars(Var("q", "queue<int>"));
            Check(ref total, ref passed, "队列刚打点号带出队头那颗", q, "q.", 2, "size", "empty", "front");
        }

        // 打完点号继续敲字母 → 按前缀筛；玩家只会打英文，中文释义一次都不用碰
        static void PrefixFilterCases(ref int total, ref int passed)
        {
            var arr = Vars(Var("a", "int[]"));
            Check(ref total, ref passed, "前缀 s 只剩 size", arr, "a.s", 3, "size");
            Check(ref total, ref passed, "前缀 e 只剩 empty", arr, "a.e", 3, "empty");
            Check(ref total, ref passed, "前缀 si 仍然只剩 size", arr, "a.si", 4, "size");
            Check(ref total, ref passed, "前缀没人匹配就收掉", arr, "a.z", 3);
            // 光标退回成员中间（左边那个点还在）：按光标到点号之间那段重新筛
            Check(ref total, ref passed, "光标停在成员中间", arr, "a.size", 3, "size");

            var st = Vars(Var("st", "stack<int>"));
            Check(ref total, ref passed, "栈前缀 t 只剩 top", st, "st.t", 4, "top");
            Check(ref total, ref passed, "栈前缀 si 只剩 size", st, "st.si", 5, "size");
        }

        // 什么都不该弹的情况：未知标识符、标量变量、光标还在点号之前、老写法 length、补完之后再打点
        static void NoPopupCases(ref int total, ref int passed)
        {
            var arr = Vars(Var("a", "int[]"));
            var mixed = Vars(Var("a", "int[]"), Var("x", "int"));

            Check(ref total, ref passed, "点号前是不认识的标识符", arr, "q.", 2);
            Check(ref total, ref passed, "点号前是标量变量（没有成员）", mixed, "x.", 2);
            Check(ref total, ref passed, "标量变量带前缀也不弹", mixed, "x.s", 3);
            Check(ref total, ref passed, "光标在点号之前", arr, "a.size", 1);
            Check(ref total, ref passed, "光标在整句最前面", arr, "a.size", 0);
            // 补完了还挂着浮窗，只会压住光标后面的字；老写法 length 认成一整颗成员，同样不再筛
            Check(ref total, ref passed, "整颗成员已经打完", arr, "a.size", 6);
            Check(ref total, ref passed, "老写法 length 打完同样不弹", arr, "a.length", 8);
            // a.size 再敲一个点，点号左边那段叫 size，不是变量
            Check(ref total, ref passed, "补完之后再敲点号", arr, "a.size.", 7);
            Check(ref total, ref passed, "点号前是数字不是变量", arr, "3.", 2);
            Check(ref total, ref passed, "两个点号之间什么都不补", arr, "a..", 3);
            Check(ref total, ref passed, "草稿为空", arr, "", 0);
            Check(ref total, ref passed, "光标越界", arr, "a.", 9);
            Check(ref total, ref passed, "变量表为空", new List<ExprVariableInfo>(), "a.", 2);
        }

        // 词表唯一来源是 Members：类型串的写法（int[] / stack<int> / queue<int>）必须和它对上
        static void ContainerTypeCases(ref int total, ref int passed)
        {
            Check(ref total, ref passed, "栈的类型串写法", Members.StackType("int"), "stack<int>");
            Check(ref total, ref passed, "队列的类型串写法", Members.QueueType("int"), "queue<int>");

            var st = Vars(Var("st", Members.StackType("int")));
            Check(ref total, ref passed, "栈成员三颗", st, "st.", 3, "size", "empty", "top");

            var q = Vars(Var("q", Members.QueueType("int")));
            Check(ref total, ref passed, "队列成员三颗", q, "q.", 2, "size", "empty", "front");

            var arr = Vars(Var("a", "int[]"));
            Check(ref total, ref passed, "数组成员两颗（不推荐 length）", arr, "a.", 2, "size", "empty");

            // 释义是浮窗右边那颗小字：token 给英文，中文只负责解释，玩家一次中文都不用打
            var top = Members.Find("stack<int>", "top");
            Check(ref total, ref passed, "top 的中文解释", top != null ? top.Gloss : null, "栈顶元素");
            var size = Members.Find("int[]", "size");
            Check(ref total, ref passed, "size 的中文解释", size != null ? size.Gloss : null, "个数");
        }

        // 同一个表达式里有好几个变量：列表只认光标所在那一段，别的变量的成员一颗都不混进来
        static void ScopeCases(ref int total, ref int passed)
        {
            var scope = Vars(Var("a", "int[]"), Var("st", "stack<int>"), Var("i", "int"));

            Check(ref total, ref passed, "前半段补数组", scope, "a. + st.top", 2, "size", "empty");
            Check(ref total, ref passed, "后半段补栈", scope, "a.size + st.", 12, "size", "empty", "top");
            Check(ref total, ref passed, "后半段按前缀筛栈", scope, "a.size + st.t", 13, "top");
            Check(ref total, ref passed, "下标里的点号补下标那个变量", scope, "a[st.]", 5, "size", "empty", "top");
            // 名字前缀重合：ba 不是 a，不能把 a 的成员塞进去
            var tricky = Vars(Var("a", "int[]"), Var("ba", "stack<int>"));
            Check(ref total, ref passed, "长名字变量补自己的成员", tricky, "ba.", 3, "size", "empty", "top");
            Check(ref total, ref passed, "短名字变量补自己的成员", tricky, "ba + a.", 7, "size", "empty");
        }

        // ---------------- 造数据 ----------------

        static ExprVariableInfo Var(string name, string type)
        {
            // Members 是浮窗唯一的行数据来源，成员列表这颗只是给左侧芯片栏用，这里照旧填上
            var members = new List<string>();
            foreach (var m in Members.Of(type)) members.Add(m.Token);
            return new ExprVariableInfo(name, type, ExprLang.IsArrayType(type), members.ToArray());
        }

        static List<ExprVariableInfo> Vars(params ExprVariableInfo[] vs)
        {
            var list = new List<ExprVariableInfo>();
            foreach (var v in vs) list.Add(v);
            return list;
        }

        // 断言一次 Candidates 的结果：期望的 token 按顺序写死，实际值原样打进日志方便 grep
        static void Check(ref int total, ref int passed, string label, List<ExprVariableInfo> vars,
                          string draft, int caret, params string[] want)
        {
            total++;
            var got = ExpressionPopoverUI.Candidates(draft, caret, vars);
            var names = new List<string>();
            foreach (var m in got) names.Add(m.Token);
            string actual = string.Join("/", names.ToArray());
            string expect = string.Join("/", want);
            if (actual == expect)
            {
                passed++;
                Debug.Log("✔ 补全 —— " + label + "：「" + Show(draft, caret) + "」= " + (names.Count == 0 ? "（不弹）" : actual));
            }
            else Debug.Log("✘ 补全 —— " + label + "：「" + Show(draft, caret) + "」期望 " + expect + "，实际 " + actual);
        }

        // 竖线标出光标位置，报错时一眼看得出测的是哪个 caret
        static string Show(string draft, int caret)
        {
            int at = caret < 0 ? 0 : (caret > draft.Length ? draft.Length : caret);
            return draft.Substring(0, at) + "│" + draft.Substring(at);
        }

        static void Check(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 补全 —— " + label + " = " + actual); }
            else Debug.Log("✘ 补全 —— " + label + " 期望「" + want + "」，实际「" + actual + "」");
        }
    }
}
