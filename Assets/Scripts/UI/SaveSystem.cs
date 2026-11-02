using System;
using System.Collections.Generic;
using System.IO;
using Dayu.Core;
using UnityEngine;

// 关卡进度存档：一关一个 json，落在 persistentDataPath/saves 下。
// 与 PlayerPrefs 的通关标记是两套东西（那边只存 0/1，容不下整张图），这里只管图。
// 写盘一律「临时文件 + File.Replace」原子换入：断电或 Unity 被杀都不能把老档写成半截。
public static class SaveSystem
{
    // 自测把存档根目录指到 Temp，跑完整目录删掉；正式跑游戏时永远是 null
    public static string OverrideRoot;
    // 自动保存失败不能弹窗、不能打断玩法，只能把这个字符串交给 UI 决定要不要提示
    public static Action<string> OnError;

    public static string Root { get { return string.IsNullOrEmpty(OverrideRoot) ? Path.Combine(Application.persistentDataPath, "saves") : OverrideRoot; } }

    const int MaxIdLen = 32;   // 关卡 id 都是 P0/S2/L3 这种短 ASCII，超出来的一定是别人塞进来的

    public static string FileFor(string levelId)
    {
        string err;
        return Valid(levelId, out err) ? Path.Combine(Root, levelId + ".json") : null;
    }

    public static bool Save(string levelId, Graph g, out string error)
    {
        error = null;
        if (!Valid(levelId, out error)) return false;
        if (g == null) { error = "要写的图是空的，已经取消写入：" + levelId; return false; }   // 空图落盘等于把玩家进度抹掉
        try
        {
            EnsureRoot();
            WriteAtomic(Path.Combine(Root, levelId + ".json"), GraphIO.Serialize(g));
            return true;
        }
        catch (Exception ex)
        {
            error = "写入失败（" + levelId + "）：" + ex.Message;
            return false;
        }
    }

    // 没存档是正常态：返回 null 且 error 为 null；文件坏掉才给 error，并把坏文件改名留证据
    public static Graph Load(string levelId, out string error)
    {
        error = null;
        if (!Valid(levelId, out error)) return null;
        string file = Path.Combine(Root, levelId + ".json");
        if (!File.Exists(file)) return null;
        string text;
        try { text = File.ReadAllText(file, System.Text.Encoding.UTF8); }
        catch (Exception ex) { error = "读不出来（" + levelId + "）：" + ex.Message; return null; }
        string err;
        var g = GraphIO.Deserialize(text, out err);
        if (g != null) return g;
        string bad = file + ".bad-" + DateTime.UtcNow.Ticks;
        try
        {
            // 绝不动静删玩家的档：改名留一份，之后人工还能捞
            if (File.Exists(file)) File.Move(file, bad);
        }
        catch (Exception) { bad = null; }
        error = "存档已损坏（" + err + "）" + (bad == null ? "，且坏文件备份失败" : "，原文件已备份为 " + Path.GetFileName(bad));
        return null;
    }

    public static bool HasSave(string levelId)
    {
        string err;
        return Valid(levelId, out err) && File.Exists(Path.Combine(Root, levelId + ".json"));
    }

    public static void Delete(string levelId)
    {
        string err;
        if (!Valid(levelId, out err)) return;
        string file = Path.Combine(Root, levelId + ".json");
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) { Report("删档失败（" + levelId + "）：" + ex.Message); }
    }

    // 主菜单「继续上次进度」用：只认真正的存档文件名，.tmp / .bad-xxx 这些一律不列出来
    public static IEnumerable<string> SavedLevelIds()
    {
        var ids = new List<string>();
        try
        {
            if (!Directory.Exists(Root)) return ids;
            foreach (var f in Directory.GetFiles(Root, "*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(f);
                string err;
                if (Valid(id, out err)) ids.Add(id);
            }
        }
        catch (Exception ex) { Report("列存档目录失败：" + ex.Message); }
        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    // ---------------- 自动保存（去抖） ----------------

    // Request 收的是「怎么拿到当前图」的委托而不是图本身：调用方给的永远是正在被编辑的活图，
    // 所以这里立刻 Clone 一份快照存着，等 Tick 到点再序列化，边改边写不会写出半态图。
    static Func<Graph> pendingSnap;
    static string pendingId;
    static double clock = double.NaN, dirtyAt;

    public static bool HasPending { get { return pendingSnap != null; } }

    public static void Request(string levelId, Func<Graph> snapshot)
    {
        string err;
        if (!Valid(levelId, out err) || snapshot == null) { if (err != null) Report(err); return; }
        Graph snap;
        try { snap = GraphIO.Clone(snapshot()); }
        catch (Exception ex) { Report("抓快照失败（" + levelId + "）：" + ex.Message); return; }
        if (snap == null) return;
        // 同一时刻多次 Request 只是把待写快照覆盖掉，落盘次数由 Tick 决定
        pendingSnap = () => snap;
        pendingId = levelId;
        // 没跑过 Tick 时时钟是 NaN，起点只能按 0 记；UI 每帧都调 Tick，实际不会出现这种退化
        if (double.IsNaN(dirtyAt) || double.IsNaN(clock)) dirtyAt = double.IsNaN(clock) ? 0 : clock;
    }

    // 由 UI 的 Update 每帧调用，now 用 Time.realtimeSinceStartup 这类单调递增的帧钟即可。
    // 拖动一帧改一次，0.8 秒合并成一次写盘；到点且有待写快照才写。
    public static void Tick(double nowSeconds, double intervalSeconds = 0.8)
    {
        clock = nowSeconds;
        if (pendingSnap == null) { dirtyAt = double.NaN; return; }
        if (nowSeconds - dirtyAt < intervalSeconds) return;
        Flush();
    }

    // 退出关卡/回主菜单/程序退出前调用，把没落盘的改动强制写下去
    public static void Flush()
    {
        if (pendingSnap == null) return;
        var snap = pendingSnap;
        string id = pendingId;
        pendingSnap = null; pendingId = null; dirtyAt = double.NaN;
        Graph g;
        try { g = snap(); }
        catch (Exception ex) { Report("抓快照失败（" + id + "）：" + ex.Message); return; }
        string err;
        if (!Save(id, g, out err)) Report(err);
    }

    // 换关/重开关卡时丢掉待写快照，免得把上一关的图写进新关的档
    public static void CancelPending() { pendingSnap = null; pendingId = null; dirtyAt = double.NaN; }

    // ---------------- 内部 ----------------

    static void EnsureRoot()
    {
        if (!Directory.Exists(Root)) Directory.CreateDirectory(Root);
    }

    static void WriteAtomic(string file, string text)
    {
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, text, System.Text.Encoding.UTF8);
        try
        {
            if (File.Exists(file)) File.Replace(tmp, file, null);
            else File.Move(tmp, file);
            return;
        }
        catch (Exception)
        {
            // File.Replace 在网络盘/FAT/部分杀软拦截下会抛：退化成「旧档先留 .bak 再覆盖」，
            // 半截写的风险由 .tmp 承担，绝不出现老档没了、新档也没写完的状态
            if (File.Exists(file)) File.Copy(file, file + ".bak", true);
            File.Copy(tmp, file, true);
        }
        TryDelete(tmp);
    }

    static void TryDelete(string p)
    {
        try { if (File.Exists(p)) File.Delete(p); }
        catch (Exception) { }
    }

    // 关卡 id 是 ASCII 短串，但存档路径由它拼出来，必须挡路径注入；不合规一律拒绝，不落到磁盘
    static bool Valid(string levelId, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(levelId)) { error = "关卡 id 为空"; return false; }
        if (levelId.Length > MaxIdLen) { error = "关卡 id 太长：" + levelId; return false; }
        for (int i = 0; i < levelId.Length; i++)
        {
            char c = levelId[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok) { error = "关卡 id 里有不认识的字符（只能字母/数字/-/_）：" + levelId; return false; }
        }
        // 白名单已经排除了 / \ . : 等，这里再显式挡一道 ".." 让人一眼看出防的是什么
        if (levelId.IndexOf("..", StringComparison.Ordinal) >= 0) { error = "关卡 id 想越出存档目录：" + levelId; return false; }
        return true;
    }

    static void Report(string msg)
    {
        var cb = OnError;
        if (cb != null) { try { cb(msg); } catch (Exception) { } }   // UI 的回调再炸也不能把存档系统带崩
    }
}
