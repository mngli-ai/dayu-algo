using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Dayu.Core;

// 存档接线侧的纯逻辑：双击落点、看答案的开放条件、关卡进度元信息、进关该铺哪张图。
// 刻意不引用 UnityEngine（连 Debug.Log 都不用），Editor 自测能把它当纯函数直接跑；
// 目录由调用方（App / SaveSystem.Root）传进来，这里不碰 Application.persistentDataPath。
public static class SaveFlow
{
    // ---------------- 双击该聚焦哪个字段 ----------------

    // 「创建变量」双击必须永远先落在名字上（玩家第十五轮点名）：卡面那条 initExpr 也算这一卡的字段，
    // 但它是第二个要填的空，谁都不能把焦点抢走。其余类型一律给「这张卡第一个要填的空」。
    public static string PreferredFocusKey(string nodeType)
    {
        switch (nodeType ?? "")
        {
            case "vardecl": return "name";           // 标量与数组同一个答案：数组卡根本没有 initExpr 这一格
            case "varset": return "valueExpr";       // 「赋值」只有一条语句
            case "varget": return "name";
            case "input": return "name";             // vtype 是类型按钮排，第一个能敲字的是写入变量
            case "output": return "outputExpr";
            case "branch": return "condExpr";
            case "whileloop": return "condExpr";
            case "forloop": return "fromExpr";       // 卡面第一个表达式；参数窗里没有这条，回落见 PickPopupFocusKey
            case "structnew": return StructNameKey;  // 容器身份名，改它等于改表达式里的那个 st
            case "stack_push":
            case "queue_push": return "pushExpr";
            case "stack_pop":
            case "queue_pop":
            case "stack_top":
            case "stack_empty":
            case "stack_size":
            case "queue_front":
            case "queue_empty":
            case "queue_size": return "structId";    // 这类卡唯一的可调项就是实例（按钮排）
            case "const": return "value";
            case "sample_input": return "group";
            case "entry":
            case "sample_compare": return "";        // 没有要填的空
            default: return "";                      // 交给调用方按「第一个文本框」回落
        }
    }

    // 「栈名 / 队列名」那一格的键：与 App.StructNameKey 同一个字面串，纯逻辑侧不能去引用 App。
    public const string StructNameKey = "structName";

    // 参数浮窗只能把光标落进文本框，所以要给的 keys 必须已经是「浮窗里的文本框、从上到下」。
    // 首选键不在这张表的（数组 vardecl 被剔掉的 initExpr、forloop 的 fromExpr）就落第一个文本框，
    // 绝不返回一个浮窗里认不出来的键——那样光标会谁都不落，玩家看到的又是「双击没反应」。
    public static string FirstPopupFocusKey(string nodeType, IList<string> textKeys)
    {
        if (textKeys == null || textKeys.Count == 0) return "";
        string want = PreferredFocusKey(nodeType);
        if (!string.IsNullOrEmpty(want))
            for (int i = 0; i < textKeys.Count; i++)
                if (textKeys[i] == want) return want;
        return textKeys[0];
    }

    // 调用方点名的键（卡片上那条表达式区的 FieldKey）在浮窗里就照它落，不在就回落到该类型的第一个该填的空
    public static string PickPopupFocusKey(string nodeType, string requested, IList<string> textKeys)
    {
        if (textKeys == null || textKeys.Count == 0) return "";
        if (!string.IsNullOrEmpty(requested))
            for (int i = 0; i < textKeys.Count; i++)
                if (textKeys[i] == requested) return requested;
        return FirstPopupFocusKey(nodeType, textKeys);
    }

    // ---------------- 查看标准答案 ----------------

    // 调试期按钮常开：答案仍然只在玩家主动点之后才铺到画布上，进关不显示。
    // 发版把这一行改成 false，就自动退回到「连错 AnswerUnlockFailures 次才开放」那条口径。
    public const bool DebugAnswerAlwaysOpen = true;
    public const int AnswerUnlockFailures = 3;

    public static bool AnswerUnlocked(int failedAttempts, bool debugMode)
    {
        if (debugMode) return true;
        return failedAttempts >= AnswerUnlockFailures;
    }

    // 预览态里挂起自动存档；-solution 批处理截图也永远不落盘（否则一晚上跑批就把玩家 13 关的档刷成答案了）
    public static bool ShouldAutoSave(bool preview, bool batchSolution) { return !preview && !batchSolution; }

    // 预览用的必须是克隆：直接把 level.Solution 交给画布，画布就地编辑会改脏关卡静态答案。
    public static Graph PreviewGraph(Graph solution) { return GraphIO.Clone(solution); }

    // 退出预览还原玩家那份：同样是克隆，内存里存的那一份不能被下一次编辑接着改。
    public static Graph PlayerGraphAfterPreview(Graph stashed, Graph starter)
    {
        var src = stashed != null ? stashed : starter;
        return GraphIO.Clone(src);
    }

    // ---------------- 进关该铺哪张图 ----------------

    public enum LoadSource { Starter, Saved, StarterAfterBadSave, Solution }

    public sealed class InitialGraph
    {
        public Graph Graph;             // 永远是新对象：交给画布不会别名到关卡静态数据
        public LoadSource Source;
        public string Error;            // 读档失败的原始错误，UI 决定怎么显示
    }

    // saved 是 SaveSystem.Load 现读出来的新对象，可以直接用；其余三条都得克隆。
    public static InitialGraph PickInitialGraph(Graph starter, Graph solution, Graph saved, string saveError, bool useSolution)
    {
        if (useSolution && solution != null)
            return new InitialGraph { Graph = GraphIO.Clone(solution), Source = LoadSource.Solution };
        if (saved != null)
            return new InitialGraph { Graph = saved, Source = LoadSource.Saved };
        if (!string.IsNullOrEmpty(saveError))
            return new InitialGraph { Graph = GraphIO.Clone(starter), Source = LoadSource.StarterAfterBadSave, Error = saveError };
        return new InitialGraph { Graph = GraphIO.Clone(starter), Source = LoadSource.Starter };
    }

    // ---------------- 关卡进度元信息 ----------------

    public sealed class Meta
    {
        public string LevelId = "";
        public long UpdatedTicks;       // 最后一次改图（UTC ticks），0 = 还没记过
        public bool Cleared;            // PlayerPrefs 通关标记的镜像，只写不读回，见 ClearedAuthority
        public int Submissions;         // 提交过几条
        public int Failures;            // 其中没过几条；过了就归零，看答案的阈值看的就是它
    }

    public const string MetaDirName = "progress";
    public const string MetaFileName = "meta.json";

    // 进度元信息单独占一个子目录：SaveSystem.SavedLevelIds 会把 Root 下所有 *.json 的名字当关卡 id 列出来，
    // 直接放 meta.json 会被当成一个叫 meta 的存档。
    public static string MetaPath(string root) { return string.IsNullOrEmpty(root) ? null : Path.Combine(root, MetaDirName, MetaFileName); }

    // 通关判定以 PlayerPrefs 为准：解锁链（App.IsUnlocked）读的就是 PlayerPrefs，meta 这边只是展示用的副本。
    // 两边不可能互相覆盖，因为读档与解锁都不看 meta.Cleared。
    public static bool ClearedAuthority(bool prefsCleared, Meta m) { return prefsCleared; }

    public static Meta TouchEdited(IDictionary<string, Meta> map, string levelId, long ticks)
    {
        var m = Of(map, levelId);
        if (m == null) return null;
        m.UpdatedTicks = ticks;
        return m;
    }

    // 一次提交的记账：提交数总是 +1，没过就把 Failures 叠上去，过了归零并把通关镜像写成 true。
    public static Meta RecordSubmit(IDictionary<string, Meta> map, string levelId, long ticks, bool accepted)
    {
        var m = Of(map, levelId);
        if (m == null) return null;
        m.Submissions++;
        m.Failures = accepted ? 0 : m.Failures + 1;
        m.UpdatedTicks = ticks;
        if (accepted) m.Cleared = true;
        return m;
    }

    public static Meta SyncCleared(IDictionary<string, Meta> map, string levelId, bool prefsCleared)
    {
        var m = Of(map, levelId);
        if (m == null) return null;
        m.Cleared = prefsCleared;
        return m;
    }

    // 关卡 id 是文件名的一部分，与 SaveSystem 的白名单同口径，不合规就不进这份表。
    static Meta Of(IDictionary<string, Meta> map, string levelId)
    {
        if (map == null || !ValidId(levelId)) return null;
        Meta m;
        if (!map.TryGetValue(levelId, out m))
        {
            m = new Meta { LevelId = levelId };
            map[levelId] = m;
        }
        return m;
    }

    public static bool ValidId(string levelId)
    {
        if (string.IsNullOrEmpty(levelId) || levelId.Length > 32) return false;
        for (int i = 0; i < levelId.Length; i++)
        {
            char c = levelId[i];
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
        }
        return true;
    }

    // ---------------- 读写 ----------------

    public static Dictionary<string, Meta> ReadMeta(string root)
    {
        string err;
        return ReadMeta(root, out err);
    }

    public static Dictionary<string, Meta> ReadMeta(string root, out string error)
    {
        error = null;
        string file = MetaPath(root);
        if (file == null) { error = "没有存档目录，读不了进度"; return New(); }
        if (!File.Exists(file)) return New();
        string text;
        try { text = File.ReadAllText(file, Encoding.UTF8); }
        catch (Exception ex) { error = "进度文件读不出来：" + ex.Message; return New(); }
        var map = ParseMeta(text);
        // 一份读不出任何条目的进度文件按「没有进度」处理而不是报错拦着玩家：它丢了只少几个统计数字
        if (map.Count == 0 && text.Trim().Length > 0) error = "进度文件认不出任何条目，已按空进度处理";
        return map;
    }

    public static bool WriteMeta(string root, IDictionary<string, Meta> map, out string error)
    {
        error = null;
        string file = MetaPath(root);
        if (file == null) { error = "没有存档目录，写不了进度"; return false; }
        try
        {
            string dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            // 和 SaveSystem 一样先写 .tmp 再换入：半截的进度文件读回来只会是空进度
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, SerializeMeta(map), Encoding.UTF8);
            if (File.Exists(file)) File.Replace(tmp, file, null);
            else File.Move(tmp, file);
            return true;
        }
        catch (Exception ex)
        {
            error = "进度写入失败：" + ex.Message;
            return false;
        }
    }

    public static bool DeleteMeta(string root)
    {
        try
        {
            string file = MetaPath(root);
            if (file != null && File.Exists(file)) File.Delete(file);
            return true;
        }
        catch (Exception) { return false; }
    }

    // 键按字典序输出，和 GraphIO 一个道理：同一份进度两次落盘必须逐字节一样，不然 diff 全是假改动。
    public static string SerializeMeta(IDictionary<string, Meta> map)
    {
        var s = new StringBuilder(128);
        s.Append("{\"v\":1,\"levels\":[");
        var ids = new List<string>();
        if (map != null) foreach (var k in map.Keys) if (!string.IsNullOrEmpty(k)) ids.Add(k);
        ids.Sort(StringComparer.Ordinal);
        bool first = true;
        for (int i = 0; i < ids.Count; i++)
        {
            Meta m;
            if (!map.TryGetValue(ids[i], out m) || m == null) continue;
            if (!first) s.Append(',');
            first = false;
            s.Append("{\"id\":\"").Append(ids[i]).Append("\",\"updated\":").Append(m.UpdatedTicks)
             .Append(",\"cleared\":").Append(m.Cleared ? "true" : "false")
             .Append(",\"submissions\":").Append(m.Submissions < 0 ? 0 : m.Submissions)
             .Append(",\"failures\":").Append(m.Failures < 0 ? 0 : m.Failures).Append('}');
        }
        return s.Append("]}").ToString();
    }

    // 只认自己写出来的那种形状；任何意外（半截、被手改、整个文件是垃圾）都退回空表，绝不抛。
    public static Dictionary<string, Meta> ParseMeta(string text)
    {
        var map = New();
        if (string.IsNullOrEmpty(text)) return map;
        try
        {
            foreach (Match b in Regex.Matches(text, "\\{[^{}]*\\}"))
            {
                string v = b.Value;
                string id = Field(v, "id");
                if (!ValidId(id)) continue;
                var m = new Meta
                {
                    LevelId = id,
                    UpdatedTicks = (long)Number(v, "updated"),
                    Cleared = Flag(v, "cleared"),
                    Submissions = (int)Number(v, "submissions"),
                    Failures = (int)Number(v, "failures"),
                };
                map[id] = m;
            }
        }
        catch (Exception) { return New(); }
        return map;
    }

    static Dictionary<string, Meta> New() { return new Dictionary<string, Meta>(StringComparer.Ordinal); }

    static string Field(string block, string key)
    {
        var m = Regex.Match(block, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    static double Number(string block, string key)
    {
        var m = Regex.Match(block, "\"" + key + "\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)");
        double d;
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0d;
    }

    static bool Flag(string block, string key)
    {
        return Regex.IsMatch(block, "\"" + key + "\"\\s*:\\s*(?:true|1)");
    }

    // ---------------- 给状态栏用的一行读数 ----------------

    public static string TimeText(long ticks)
    {
        if (ticks <= 0) return "";
        try
        {
            var t = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            return t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        catch (Exception) { return ""; }
    }

    // 关卡列表那一栏只有 160px，年份是白占的
    public static string TimeShort(long ticks)
    {
        if (ticks <= 0) return "";
        try
        {
            var t = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            return t.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        catch (Exception) { return ""; }
    }
}
