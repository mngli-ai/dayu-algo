using System;
using System.Collections.Generic;
using UnityEngine;

// 各家 AI 拟人的像素立绘加载器（第十七轮）。
//
// 形象来源：全部由 tools/gen_personas.py 程序自绘原创，逐像素搭建，不依赖、不临摹任何第三方素材；
// 许可与登记口径见 ArtInbox/SOURCES.md 末尾「程序自绘形象」条目。
// 资源位置：Assets/Resources/Personas/<key>_<action>.png（源件同尺寸同字节放在 Assets/Art/Personas/）。
// 尺寸口径：立绘帧 64x80、头像 32x32，所有角色严格一致，可以直接并排摆舞台。
//
// 失败行为（重要）：任何加载失败都只 Debug.LogWarning 一次并返回 null，绝不抛异常、绝不 Debug.Assert。
// 主 agent 那边应当把 null 当作「这一格不画立绘」处理，用现成的 Panel/Label 兜底。
public static class Personas
{
    const string Dir = "Personas/";
    const float PPU = 100f;                       // 与 HeroAssets 一致：100 像素 = 1 单位

    // key → 动作清单（不含 face；face 由 Face(key) 单独取）
    static readonly Dictionary<string, string[]> ActionMap = new Dictionary<string, string[]>
    {
        { "dayu", new[] { "idle", "wave", "talk", "eat" } },
        { "dragon", new[] { "idle", "wave", "talk" } },
        { "claude", new[] { "idle", "wave", "talk" } },
        { "gemini", new[] { "idle", "wave", "talk" } },
        { "qianwen", new[] { "idle", "wave", "talk" } },
        { "grok", new[] { "idle", "wave", "talk" } },
        { "doubao", new[] { "idle", "wave", "talk" } },
    };

    static readonly string[] KeyOrder = { "dayu", "dragon", "claude", "gemini", "qianwen", "grok", "doubao" };

    // 中文形象名/社区别名 → key。按别名长度从长到短匹配，避免「大肥鱼的劳工」这类整句里抢词。
    static readonly List<KeyValuePair<string, string>> Aliases = BuildAliases();

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    static readonly HashSet<string> missing = new HashSet<string>();

    static List<KeyValuePair<string, string>> BuildAliases()
    {
        var raw = new Dictionary<string, string[]>
        {
            { "dayu", new[] { "大肥鱼", "蓝色大肥鱼", "鲸鱼娘", "deepseek娘", "吃白饭的大肥鱼", "本鲸", "本娘" } },
            { "dragon", new[] { "龙娘", "白龙娘", "gpt娘", "chatgpt娘" } },
            { "claude", new[] { "claude娘", "大小姐", "小克", "claude" } },
            { "gemini", new[] { "gemini娘", "gemini" } },
            { "qianwen", new[] { "千问娘", "通义千问娘", "文昭", "小酒窝", "千问" } },
            { "grok", new[] { "grok娘", "老司机", "grok" } },
            { "doubao", new[] { "豆包", "豆沙", "豆脚" } },
        };
        var list = new List<KeyValuePair<string, string>>();
        foreach (var kv in raw)
            foreach (var a in kv.Value)
                list.Add(new KeyValuePair<string, string>(Norm(a), kv.Key));
        list.Sort((x, y) => y.Key.Length.CompareTo(x.Key.Length));   // 长别名优先
        return list;
    }

    /// <summary>全部角色 key，按主菜单舞台的站位顺序。</summary>
    public static IReadOnlyList<string> Keys { get { return KeyOrder; } }

    /// <summary>这个 key 有没有登记（不代表资源一定在，资源在不在由 Face/Frame 的 null 说了算）。</summary>
    public static bool Has(string key)
    {
        var k = Norm(key);
        return k.Length > 0 && ActionMap.ContainsKey(k);
    }

    /// <summary>头像（32x32，题栏与对话框用）。找不到返回 null。</summary>
    public static Sprite Face(string key) { return Frame(key, "face"); }

    /// <summary>立绘帧（64x80）。action 用 idle / wave / talk / eat / face，找不到返回 null。</summary>
    public static Sprite Frame(string key, string action)
    {
        var k = Norm(key);
        var a = Norm(action);
        if (k.Length == 0 || a.Length == 0) return null;
        string path = Dir + k + "_" + a;
        if (missing.Contains(path)) return null;
        Sprite s;
        if (cache.TryGetValue(path, out s)) return s;

        try
        {
            // 首选 Sprite 资源；本工程的 ImportSettings 把 Resources 下的贴图按 Default 导入，
            // 所以 Resources.Load<Sprite> 会拿到 null，这时退回 Texture2D 自己造 Sprite（同 HeroAssets 的做法）。
            s = Resources.Load<Sprite>(path);
            if (s == null)
            {
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                {
                    tex.filterMode = FilterMode.Point;      // 像素图：点采样，不许糊
                    tex.wrapMode = TextureWrapMode.Clamp;
                    s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                      new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Personas] 加载 " + path + " 出错：" + e.Message);
            s = null;
        }

        if (s == null)
        {
            missing.Add(path);
            Debug.LogWarning("[Personas] 缺资源 " + path + "（Personas 目录下没有这张图，界面会跳过这一格立绘）");
            return null;
        }
        cache[path] = s;
        return s;
    }

    /// <summary>这个角色的动作清单（不含 face）。未知 key 返回空数组，不返回 null。</summary>
    public static string[] ActionsFor(string key)
    {
        var k = Norm(key);
        string[] acts;
        if (ActionMap.TryGetValue(k, out acts))
        {
            var copy = new string[acts.Length];
            Array.Copy(acts, copy, acts.Length);
            return copy;
        }
        return new string[0];
    }

    /// <summary>
    /// 中文形象名 → key。给主 agent 按 Story 文本挑出场角色用。
    /// 先按整名精确匹配，再退化成「文本里包含哪个别名」的最长命中；认不出来返回 null。
    /// </summary>
    public static string KeyForDisplayName(string cnName)
    {
        var t = Norm(cnName);
        if (t.Length == 0) return null;
        if (ActionMap.ContainsKey(t)) return t;                 // 直接给 key 也认
        foreach (var kv in Aliases)
            if (t == kv.Key) return kv.Value;
        foreach (var kv in Aliases)
            if (t.IndexOf(kv.Key, StringComparison.Ordinal) >= 0) return kv.Value;
        return null;
    }

    /// <summary>一段文本里出现了哪几个角色（按 Keys 的顺序去重）。关卡想摆一排出场人物时用这个。</summary>
    public static IReadOnlyList<string> KeysMentioned(string text)
    {
        var hit = new List<string>();
        var t = Norm(text);
        if (t.Length == 0) return hit;
        foreach (var key in KeyOrder)
        {
            foreach (var kv in Aliases)
            {
                if (kv.Value != key) continue;
                if (t.IndexOf(kv.Key, StringComparison.Ordinal) >= 0) { hit.Add(key); break; }
            }
        }
        return hit;
    }

    /// <summary>丢掉已缓存的 Sprite（换美术后想让 Resources 重新加载时用）。</summary>
    public static void Forget()
    {
        cache.Clear();
        missing.Clear();
    }

    static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
