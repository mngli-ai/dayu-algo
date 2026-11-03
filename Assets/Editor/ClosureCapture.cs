using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Dayu.EditorTools
{
    /// <summary>
    /// 纯文本扫「lambda 读了本层 for 计数器」，零 UnityEngine 依赖，
    /// 这样 Evidence/r17/closure_run 那个 dotnet 小程序能直接引这份源跑断言，不用等 Unity 自测。
    /// C# 的 for 计数器整个循环只有一份，被闭包捕获的就是「循环结束后的那个值」；
    /// foreach 的迭代变量每轮新建一份，不在此列。
    /// </summary>
    public static class ClosureCapture
    {
        static readonly Regex ForHeader = new Regex(@"\bfor\s*\(\s*(?:int|long|short)\s+(\w+)\s*=", RegexOptions.Compiled);
        static readonly Dictionary<string, Regex> WordCache = new Dictionary<string, Regex>();

        // 第十七轮主菜单事故的原样形状，闭包检测试拿它当负对照：扫不出这段就等于没在扫
        public const string SampleBuggy = @"
void MenuButtons(object[] pairs)
{
    for (int i = 0; i + 1 < pairs.Length; i += 2)
    {
        var b = Create((string)pairs[i], () => { Play(); ((Action)pairs[i + 1]).Invoke(); }, 0, 28, i == 0);
        list.Add(b);
    }
}
";

        // 现写法：循环体内先抄成局部变量，闭包只碰局部变量
        public const string SampleFixed = @"
void MenuButtons(object[] pairs)
{
    for (int i = 0; i + 1 < pairs.Length; i += 2)
    {
        string label = (string)pairs[i];
        Action click = (Action)pairs[i + 1];
        bool primary = i == 0;
        var b = Create(label, () => { Play(); click?.Invoke(); }, 0, 28, primary);
    }
}
";

        /// <summary>返回 src 中所有「lambda 体读到 for 计数器」的行号，去重。</summary>
        public static List<int> Scan(string src)
        {
            var outLines = new List<int>();
            if (string.IsNullOrEmpty(src)) return outLines;
            foreach (Match m in ForHeader.Matches(src))
            {
                string name = m.Groups[1].Value;
                int bodyA, bodyB;
                if (!TryBody(src, m.Index, out bodyA, out bodyB)) continue;
                int pos = bodyA;
                while (bodyB - pos >= 2)
                {
                    int arrow = src.IndexOf("=>", pos, bodyB - pos);
                    if (arrow < 0) break;
                    pos = arrow + 2;
                    int lo, hi;
                    if (!TryLambdaBody(src, arrow, out lo, out hi)) continue;
                    if (ReadsWord(src, lo, hi, name)) Add(outLines, LineOf(src, lo));
                }
            }
            outLines.Sort();
            return outLines;
        }

        static void Add(List<int> lines, int v) { if (!lines.Contains(v)) lines.Add(v); }

        static Regex WordOf(string name)
        {
            Regex r;
            if (!WordCache.TryGetValue(name, out r))
            {
                r = new Regex(@"\b" + Regex.Escape(name) + @"\b");
                WordCache[name] = r;
            }
            return r;
        }

        /// <summary>lo..hi 之间是否有整词 name，且不是「name = ...」这种赋值左侧。</summary>
        static bool ReadsWord(string src, int lo, int hi, string name)
        {
            string txt = src.Substring(lo, hi - lo);
            foreach (Match w in WordOf(name).Matches(txt))
            {
                int k = w.Index + w.Length;
                while (k < txt.Length && (txt[k] == ' ' || txt[k] == '\t')) k++;
                if (k + 1 < txt.Length && txt[k] == '=' && txt[k + 1] != '=' && txt[k - 1] != '=' && txt[k - 1] != '!')
                    continue;   // 「name = ...」是赋值，不是读
                return true;
            }
            return false;
        }

        /// <summary>从 for 的位置起，先配对头部圆括号，再配对随后第一个花括号，返回体内部区间。</summary>
        static bool TryBody(string src, int forIndex, out int bodyA, out int bodyB)
        {
            bodyA = bodyB = -1;
            int p = src.IndexOf('(', forIndex);
            if (p < 0) return false;
            int d = 0, j = p;
            for (; j < src.Length; j++)
            {
                if (src[j] == '(') d++;
                else if (src[j] == ')') { d--; if (d == 0) break; }
            }
            if (d != 0) return false;
            int i = src.IndexOf('{', j + 1);
            if (i < 0) return false;
            d = 0;
            for (int k = i; k < src.Length; k++)
            {
                if (src[k] == '{') d++;
                else if (src[k] == '}') { d--; if (d == 0) { bodyA = i + 1; bodyB = k; return true; } }
            }
            return false;
        }

        /// <summary>界定 lambda 真正的体：块体取花括号配对，表达式体取到同层的 , ) ; 。
        /// 不这么切的话「() => f(), 0, 28, i == 0」里箭头之后同层实参的 i 会被当成 lambda 内引用，纯误报。</summary>
        static bool TryLambdaBody(string src, int arrow, out int lo, out int hi)
        {
            lo = hi = -1;
            int j = arrow + 2;
            while (j < src.Length && (src[j] == ' ' || src[j] == '\t' || src[j] == '\r' || src[j] == '\n')) j++;
            if (j >= src.Length) return false;
            if (src[j] == '{')
            {
                int d = 0;
                for (int k = j; k < src.Length; k++)
                {
                    if (src[k] == '{') d++;
                    else if (src[k] == '}') { d--; if (d == 0) { lo = j; hi = k + 1; return true; } }
                }
                return false;
            }
            int dp = 0, db = 0, dk = 0;
            for (int k = j; k < src.Length; k++)
            {
                char c = src[k];
                if (c == '(') dp++;
                else if (c == ')')
                {
                    if (dp == 0) { lo = j; hi = k; return true; }
                    dp--;
                }
                else if (c == '{') db++;
                else if (c == '}') db--;
                else if (c == '[') dk++;
                else if (c == ']') dk--;
                else if ((c == ',' || c == ';') && dp == 0 && db == 0 && dk == 0) { lo = j; hi = k; return true; }
            }
            lo = j; hi = src.Length;
            return true;
        }

        /// <summary>扫一棵目录树（Assets/Scripts、Assets/Editor 之类），返回命中总数；files 出去扫了几个文件，
        /// 让「路径拼错导致一个文件都没扫」也判红，而不是悄悄全绿。
        /// skipPrefixes：跳过文件名前缀。检测试自己那份文件里带的是故意的错误样例（负对照），
        /// 不跳过的话它会抓到自己的 fixture，全绿就变成永远拿不到。</summary>
        public static int ScanTree(string root, out int files, List<string> samples, params string[] skipPrefixes)
        {
            files = 0;
            int hits = 0;
            if (!System.IO.Directory.Exists(root)) return -1;
            foreach (var f in System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                if (skipPrefixes != null)
                {
                    string name = System.IO.Path.GetFileName(f);
                    bool skip = false;
                    foreach (var p in skipPrefixes) if (name.StartsWith(p)) { skip = true; break; }
                    if (skip) continue;
                }
                files++;
                foreach (int line in Scan(System.IO.File.ReadAllText(f)))
                {
                    hits++;
                    if (samples != null && samples.Count < 6) samples.Add(f + ":" + line);
                }
            }
            return hits;
        }

        static int LineOf(string src, int pos)
        {
            int n = 1;
            for (int i = 0; i < pos && i < src.Length; i++) if (src[i] == '\n') n++;
            return n;
        }

        /// <summary>取第 n 行（1 基），给断言「报的行确实是 lambda 那一句」用。</summary>
        public static string Line(string src, int n)
        {
            if (n < 1) return "";
            int start = 0;
            for (int k = 0; k < src.Length; k++)
            {
                if (src[k] != '\n') continue;
                if (n == 1) return src.Substring(start, k - start).Trim();
                n--;
                start = k + 1;
            }
            return src.Substring(start).Trim();
        }
    }
}
