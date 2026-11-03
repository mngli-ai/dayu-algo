using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 第十七轮：各家 AI 拟人像素形象的「文件 ↔ 加载器」交叉核对。
    // 为什么要盯：App 里的舞台和题栏都是「Personas.Frame 返回 null 就跳过这一格」，
    // 少一张图不会报错，只会在主菜单上默默缺一个人——所以必须在自测里把缺图本身判红。
    // 两处目录（Art 源件 / Resources 运行时）必须同名同字节，只改一边就会打出「看着一样其实不同」的包。
    public static class PersonaAssetTests
    {
        const string Tag = "拟人形象资源";

        static readonly string[] MustHave = { "face", "idle", "talk" };   // 舞台、题栏、对话框都要用到的三帧

        public static void Run(ref int total, ref int passed)
        {
            var artDir = Path.Combine(Application.dataPath, "Art", "Personas");
            var resDir = Path.Combine(Application.dataPath, "Resources", "Personas");

            Expect(ref total, ref passed, "Art 目录存在", Directory.Exists(artDir) ? 1 : 0, 1);
            Expect(ref total, ref passed, "Resources 目录存在", Directory.Exists(resDir) ? 1 : 0, 1);
            if (!Directory.Exists(artDir) || !Directory.Exists(resDir)) return;

            var art = List(artDir);
            var res = List(resDir);
            Expect(ref total, ref passed, "Resources 帧数（>=29）", res.Count >= 29 ? 1 : 0, 1);
            Expect(ref total, ref passed, "两处目录帧数相等", art.Count == res.Count ? 1 : 0, 1);
            Debug.Log("· Art " + art.Count + " 张 / Resources " + res.Count + " 张");

            // 命名必须 <key>_<action>.png，且 key 在加载器登记过
            int badName = 0, unknownKey = 0;
            foreach (var f in res.Keys)
            {
                string stem = Path.GetFileNameWithoutExtension(f);
                int k = stem.LastIndexOf('_');
                if (k <= 0 || k == stem.Length - 1) { badName++; Debug.Log("· 文件名不像 <key>_<action>：" + f); continue; }
                string key = stem.Substring(0, k);
                if (!Personas.Has(key)) { unknownKey++; Debug.Log("· " + f + " 的 key「" + key + "」没在 Personas 登记"); }
            }
            Expect(ref total, ref passed, "命名不合规的 PNG", badName, 0);
            Expect(ref total, ref passed, "未登记 key 的 PNG", unknownKey, 0);

            // 每个登记过的 key 都得有 face/idle/talk；大肥鱼还要有 eat（主菜单舞台用它）
            int missing = 0;
            foreach (var key in Personas.Keys)
            {
                foreach (var act in MustHave)
                    if (!res.ContainsKey(key + "_" + act + ".png")) { missing++; Debug.Log("· 缺 " + key + "_" + act); }
                if (key == "dayu" && !res.ContainsKey("dayu_eat.png")) { missing++; Debug.Log("· 缺 dayu_eat"); }
            }
            Expect(ref total, ref passed, "缺失的必需帧", missing, 0);

            // 两处同名必须逐字节一致（sha 相同）
            int differ = 0, absent = 0;
            foreach (var kv in res)
            {
                string other;
                if (!art.TryGetValue(kv.Key, out other)) { absent++; Debug.Log("· Art 侧没有 " + kv.Key); continue; }
                if (!SameBytes(other, Path.Combine(resDir, kv.Key))) differ++;
            }
            foreach (var kv in art) if (!res.ContainsKey(kv.Key)) { absent++; Debug.Log("· Resources 侧没有 " + kv.Key); }
            Expect(ref total, ref passed, "只在单边存在的帧", absent, 0);
            Expect(ref total, ref passed, "两处字节不一致的帧", differ, 0);

            // 加载器的中文别名表：题栏靠它从 Story 文本里认人
            ExpectStr(ref total, ref passed, "KeyForDisplayName(龙娘)", Personas.KeyForDisplayName("龙娘"), "dragon");
            ExpectStr(ref total, ref passed, "KeyForDisplayName(大肥鱼)", Personas.KeyForDisplayName("大肥鱼"), "dayu");
            ExpectStr(ref total, ref passed, "KeyForDisplayName(豆包)", Personas.KeyForDisplayName("豆包"), "doubao");
            ExpectStr(ref total, ref passed, "KeyForDisplayName(千问娘)", Personas.KeyForDisplayName("千问娘"), "qianwen");
            Expect(ref total, ref passed, "KeyForDisplayName(不认识的) 返回 null",
                Personas.KeyForDisplayName("完全不存在的名号") == null ? 1 : 0, 1);
            var many = Personas.KeysMentioned("大肥鱼找豆包和龙娘借钱");
            Expect(ref total, ref passed, "KeysMentioned 命中 3 人", many.Count, 3);
            Expect(ref total, ref passed, "KeysMentioned 空文本 0 人", Personas.KeysMentioned("").Count, 0);
            Expect(ref total, ref passed, "ActionsFor(未知) 返回空数组不返回 null",
                Personas.ActionsFor("nobody") != null && Personas.ActionsFor("nobody").Length == 0 ? 1 : 0, 1);
        }

        static Dictionary<string, string> List(string dir)
        {
            var d = new Dictionary<string, string>();
            foreach (var f in Directory.GetFiles(dir, "*.png"))
                d[Path.GetFileName(f)] = f;
            return d;
        }

        static bool SameBytes(string a, string b)
        {
            var ia = new FileInfo(a);
            var ib = new FileInfo(b);
            if (ia.Length != ib.Length) return false;
            using (var fa = ia.OpenRead())
            using (var fb = ib.OpenRead())
            {
                var ba = new byte[8192];
                var bb = new byte[8192];
                while (true)
                {
                    int na = ReadFull(fa, ba), nb = ReadFull(fb, bb);
                    if (na != nb) return false;
                    if (na == 0) return true;
                    for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                }
            }
        }

        static int ReadFull(Stream s, byte[] buf)
        {
            int off = 0;
            while (off < buf.Length)
            {
                int n = s.Read(buf, off, buf.Length - off);
                if (n <= 0) break;
                off += n;
            }
            return off;
        }

        static void ExpectStr(ref int total, ref int passed, string label, string actual, string want)
        {
            Expect(ref total, ref passed, label + " = " + (actual ?? "null"), actual == want ? 1 : 0, 1);
        }

        static void Expect(ref int total, ref int passed, string label, int actual, int want)
        {
            total++;
            if (actual == want)
            {
                passed++;
                Debug.Log("✔ " + Tag + " —— " + label);
            }
            else Debug.Log("✘ " + Tag + " —— " + label + " 期望 " + want + "，实际 " + actual);
        }
    }
}
