using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十八轮收尾：素材授权台账的「文本 ↔ 仓库现状」交叉核对。
    // 为什么要盯：这一轮逐条核对 README / SOURCES / 资产清单时，抓出两类真缺陷——
    //   1) 台账把署名落点写成 `App.cs BuildFooter()`，而工程里根本没有这个函数（实际是 CreditLines），四处引用全是指向空气；
    //   2) `Assets/Art/Font/fusion-pixel-12px.ttf` 是第三方 OFL 字体本体、随初始提交进了公开仓库，
    //      但台账、README 与许可证原文三样都没有——OFL 要求再分发字体本体时必须带许可证文本。
    // 两类都不是运行时能发现的，只能把「文档引用的符号与路径必须真实存在」钉成断言。
    public static class LicenseLedgerTests
    {
        const string Tag = "授权台账";

        // 只在文档里出现、允许指向不存在文件 / 不存在符号的措辞（历史条目 / 已删除 / 已停用 / 说明「那个函数不存在」的订正记录）
        static readonly string[] ExemptWords = { "删除", "停用", "不再", "已作废", "历史", "未入", "摘掉", "不存在", "误写", "登记时" };

        public static void Run(ref int total, ref int passed)
        {
            string root = System.IO.Directory.GetParent(Application.dataPath).FullName;

            var docs = new List<string>();
            foreach (var rel in new[] { "ArtInbox/SOURCES.md", "docs/art-assets-manifest.md", "README.md" })
            {
                string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                Expect(ref total, ref passed, Tag + "·台账在库：" + rel, File.Exists(p) ? 1 : 0, 1);
                if (File.Exists(p)) docs.Add(Load(p));
            }
            if (docs.Count == 0) return;

            string appSrc = Load(Path.Combine(root, "Assets/Scripts/UI/App.cs"));

            // ① 字体本体必须有随附的许可证原文
            var fonts = Directory.GetFiles(Path.Combine(root, "Assets/Art/Font"), "*.ttf");
            Expect(ref total, ref passed, Tag + "·字体本体数量（本轮只有一种）", fonts.Length, 1);
            Expect(ref total, ref passed, Tag + "·字体自证 OFL（name 表里 UTF-16BE 的许可证串）",
                   FontDeclaresOfl(fonts) ? 1 : 0, 1);
            string lic = Path.Combine(root, "docs/licenses/fusion-pixel-12px-OFL.txt");
            Expect(ref total, ref passed, Tag + "·OFL 原文已入库", File.Exists(lic) ? 1 : 0, 1);
            if (File.Exists(lic))
            {
                string t = Load(lic).ToUpperInvariant();
                Expect(ref total, ref passed, Tag + "·原文含 SIL OPEN FONT LICENSE 标题",
                       t.Contains("SIL OPEN FONT LICENSE") ? 1 : 0, 1);
                Expect(ref total, ref passed, Tag + "·原文含 PERMISSION NOTICE & 版权行",
                       (t.Contains("COPYRIGHT") && t.Contains("RESERVED FONT NAME")) ? 1 : 0, 1);
            }

            // ② 台账点名的 App.cs 符号必须真在 App.cs 里（BuildFooter 那类指向空气的引用）
            var refs = NewFunctionRefs(string.Join("\n", docs.ToArray()));
            int bogus = 0;
            foreach (var name in refs)
                if (!HasSymbol(appSrc, name)) { bogus++; Debug.Log("· " + Tag + "·台账引用了 App.cs 里不存在的符号：" + name); }
            Expect(ref total, ref passed, Tag + "·指向空气的 App.cs 符号引用", bogus, 0);
            // 尺子自身的活路证明：不能靠「文档里恰好有引用」来防空跑，因为台账修干净后那个数就该是 0
            Expect(ref total, ref passed, Tag + "·正对照：`App.cs` 的 `CreditLines()` 这种写法抓得到且真在 App.cs 里",
                   NewFunctionRefs("`App.cs` 的 `CreditLines()`").Count == 1 && HasSymbol(appSrc, "CreditLines") ? 1 : 0, 1);

            // ③ 台账写的仓库路径必须存在
            var paths = NewRepoPaths(string.Join("\n", docs.ToArray()), docs);
            int missing = 0;
            foreach (var rel in paths)
            {
                string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(p) && !Directory.Exists(p)) { missing++; Debug.Log("· " + Tag + "·台账引用了不存在的文件：" + rel); }
            }
            Expect(ref total, ref passed, Tag + "·台账里指向空气的文件路径", missing, 0);
            Expect(ref total, ref passed, Tag + "·至少核到 3 个文件路径（防空匹配自证）", paths.Count >= 3 ? 1 : 0, 1);

            // ④ 负对照：把已知不存在的符号与路径喂进同一把尺，必须各判 1 条红
            Expect(ref total, ref passed, Tag + "·负对照：不存在的符号名判红",
                   NewFunctionRefs("`App.cs BuildFooter()` 与 `App.cs MenuScenery()`").Count == 2 ? 1 : 0, 1);
            Expect(ref total, ref passed, Tag + "·负对照：旧函数名 BuildFooter 在 App.cs 里确实查不到",
                   HasSymbol(appSrc, "BuildFooter") ? 0 : 1, 1);
            Expect(ref total, ref passed, Tag + "·负对照：说明「那个函数不存在」的订正记录不判引用",
                   NewFunctionRefs("登记时把落点写成 App.cs 里的 `BuildFooter()`，那个函数不存在，按实现改正").Count, 0);
            Expect(ref total, ref passed, Tag + "·负对照：不带括号的字段式引用也抓得到（CreditLines 是数组不是方法）",
                   NewFunctionRefs("`App.cs CreditLines` 第五行").Count, 1);
            Expect(ref total, ref passed, Tag + "·负对照：`App.cs` 与 `GraphView.cs` 并列句不把后者算成 App.cs 的符号",
                   NewFunctionRefs("`App.cs` 与 `GraphView.cs` 同屏").Count, 0);
            Expect(ref total, ref passed, Tag + "·负对照：带通配符的路径不拿去判存在",
                   NewRepoPaths("`Assets/Resources/Personas/<key>_<action>.png`", new List<string>()).Count, 0);
            Expect(ref total, ref passed, Tag + "·负对照：写了「已删除」的路径不判存在",
                   NewRepoPaths("`Assets/Art/Personas/personas_meta.json` 已在第十八轮删除", new List<string> { "`Assets/Art/Personas/personas_meta.json` 已在第十八轮删除" }).Count, 0);

            Debug.Log("· " + Tag + "：台账 " + docs.Count + " 份，App.cs 符号引用 " + refs.Count + " 个，文件路径 " + paths.Count + " 个");
        }

        static string Load(string path)
        {
            return File.ReadAllText(path, System.Text.Encoding.UTF8);
        }

        // 字体本体的 name 串按 UTF-16BE 存，'S' 后面跟一个 0x00，所以按双字节找
        static bool FontDeclaresOfl(string[] fonts)
        {
            if (fonts.Length == 0) return false;
            byte[] raw = File.ReadAllBytes(fonts[0]);
            byte[] needle = Utf16Be("Open Font License");
            for (int i = 0; i + needle.Length <= raw.Length; i++)
            {
                bool hit = true;
                for (int j = 0; j < needle.Length; j++) if (raw[i + j] != needle[j]) { hit = false; break; }
                if (hit) return true;
            }
            return false;
        }

        static byte[] Utf16Be(string s)
        {
            var list = new List<byte>();
            foreach (char c in s) { list.Add(0); list.Add((byte)c); }
            return list.ToArray();
        }

        // 台账点名的 App.cs 符号：`App.cs Foo()` / `App.cs:Foo()` / `App.cs` 的 `Foo()` / `App.cs Foo` 都收。
        // 不带括号那式必须收：CreditLines 实为 static readonly string[]，只认 名字( 会把这类合法引用全放过，检查沦为绿色空跑。
        // App.cs 与符号之间只允许反引号、空格、冒号和一个「的」，所以「`App.cs` 与 `GraphView.cs`」不会把后者误算进来。
        static List<string> NewFunctionRefs(string text)
        {
            var names = new List<string>();
            foreach (var line in text.Split('\n'))
            {
                bool exempt = false;
                foreach (var w in ExemptWords) if (line.Contains(w)) { exempt = true; break; }
                if (exempt) continue;
                foreach (Match m in Regex.Matches(line, @"App\.cs[` :]*的?[` :]*([A-Za-z_][A-Za-z0-9_]*)"))
                {
                    string n = m.Groups[1].Value;
                    if (!names.Contains(n)) names.Add(n);
                }
            }
            return names;
        }

        // 整词查 App.cs：方法、字段、属性都算存在，只要这个名字真在源文件里
        static bool HasSymbol(string appSrc, string name)
        {
            return Regex.IsMatch(appSrc, @"\b" + Regex.Escape(name) + @"\b");
        }

        // 只收反引号里的仓库内路径，且跳过通配符、跳过同一行声明了「已删除 / 停用」等字样的历史条目
        static List<string> NewRepoPaths(string joined, List<string> docs)
        {
            var found = new List<string>();
            foreach (var doc in docs)
            {
                foreach (var line in doc.Split('\n'))
                {
                    bool exempt = false;
                    foreach (var w in ExemptWords) if (line.Contains(w)) { exempt = true; break; }
                    if (exempt) continue;
                    foreach (Match m in Regex.Matches(line, @"`(Assets|docs|tools)/[A-Za-z0-9_./-]+(?![A-Za-z0-9_./-])"))
                    {
                        string rel = m.Value.Trim('`');
                        if (rel.IndexOf('<') >= 0 || rel.IndexOf('*') >= 0 || rel.EndsWith("/") || rel.EndsWith(".")) continue;
                        if (!found.Contains(rel)) found.Add(rel);
                    }
                }
            }
            return found;
        }

        static void Expect(ref int total, ref int passed, string name, int actual, int want)
        {
            total++;
            bool ok = actual == want;
            if (ok) passed++;
            Debug.Log((ok ? "✔ " : "✘ ") + name + (ok ? "" : "：实得 " + actual + "，期望 " + want));
        }
    }
}
