using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Dayu.EditorTools
{
    // 音效层自测：反馈音全部由 tools/gen_sfx.py 程序化合成，这里只盯三件会真出事的事——
    // 名单/磁盘/生成器三方对不上（接线点了个不存在的 cue）、单条太长或太响（进包体积 + 削波爆音）、
    // 去抖判定写歪（单步播放一帧几十个事件，不去抖就是一团噪音）。
    // 全是确定性纯函数断言：直接读磁盘字节，不碰 Unity 的音频设备，也不依赖当前工作目录。
    public static class SoundCueTests
    {
        const long TotalBudgetBytes = 700_000L;          // 与 gen_sfx.py 的 TOTAL_BUDGET 同一档
        const double DcTolerance = 0.01;                 // 均值绝对值上限，超过就是能听出来的偏置
        const double PeakTolerance = 0.005;              // 16bit 量化误差的容身之处
        const int ExpectedCueCount = 17;

        public static void Run(ref int total, ref int passed)
        {
            string dir = Path.Combine(Application.dataPath, SoundSystem.DiskFolder);
            var files = LoadCues(dir);
            ListCases(ref total, ref passed, dir, files);
            QualityCases(ref total, ref passed, dir, files);
            DebounceCases(ref total, ref passed);
            MappingCases(ref total, ref passed);
        }

        sealed class Wav
        {
            public string Name;
            public int Channels, Rate, Bits;
            public double[] Samples;
            public string Error;
            public double Seconds { get { return Rate > 0 ? (double)Samples.Length / Rate : 99.0; } }
            public double Peak
            {
                get
                {
                    double p = 0;
                    foreach (var s in Samples) p = Math.Max(p, Math.Abs(s));
                    return p;
                }
            }
            public double Dc
            {
                get
                {
                    if (Samples.Length == 0) return 1;
                    double sum = 0;
                    foreach (var s in Samples) sum += s;
                    return Math.Abs(sum / Samples.Length);
                }
            }
            public string Num(double v, string format) { return v.ToString(format, CultureInfo.InvariantCulture); }
        }

        static Dictionary<string, Wav> LoadCues(string dir)
        {
            var map = new Dictionary<string, Wav>(StringComparer.Ordinal);
            if (!Directory.Exists(dir)) return map;
            foreach (var cue in SoundSystem.Cues)
            {
                string path = Path.Combine(dir, cue + ".wav");
                if (File.Exists(path)) map[cue] = Parse(path, cue);
            }
            return map;
        }

        // ---------------- 名单 / 磁盘 / 生成器三方对齐 ----------------
        private static void ListCases(ref int total, ref int passed, string dir, Dictionary<string, Wav> files)
        {
            Expect(ref total, ref passed, "cue 名单是 " + ExpectedCueCount + " 条", SoundSystem.Cues.Count, ExpectedCueCount);

            if (!Directory.Exists(dir))
            {
                Expect(ref total, ref passed, "音频目录存在", false, dir + "（先跑 python tools/gen_sfx.py）");
                return;
            }

            var missing = MissingCues(files, SoundSystem.Cues);
            Expect(ref total, ref passed, "每条 cue 都有对应 wav", missing.Count == 0, "缺文件：" + Join(missing));

            var extra = new List<string>();
            foreach (var f in Directory.GetFiles(dir, "*.wav"))
            {
                string stem = Path.GetFileNameWithoutExtension(f);
                if (!SoundSystem.IsKnownCue(stem)) extra.Add(stem);
            }
            Expect(ref total, ref passed, "磁盘上没有名单外的音频", extra.Count == 0, "多余：" + Join(extra));

            // 上面的检查本身也得能被证伪：名单里塞一个不存在的 cue，必须当场报出来
            var names = new List<string>();
            foreach (var cue in SoundSystem.Cues) names.Add(cue);
            names.Add("__no_such_cue__");
            var probe = MissingCues(files, names);
            Expect(ref total, ref passed, "名单漏一条能被查出来",
                probe.Count == 1 && probe[0] == "__no_such_cue__", "查出来的是：" + Join(probe));

            GeneratorAgreement(ref total, ref passed);
        }

        static List<string> MissingCues(Dictionary<string, Wav> files, IEnumerable<string> names)
        {
            var miss = new List<string>();
            foreach (var cue in names)
            {
                Wav w;
                if (!files.TryGetValue(cue, out w) || w == null) miss.Add(cue);
            }
            return miss;
        }

        // 直接把 gen_sfx.py 里 CUES 的键与 gap 抠出来对一遍：
        // 真正会漂移的是「加了一条音、改了去抖，忘了改 C# 这边」
        static void GeneratorAgreement(ref int total, ref int passed)
        {
            string py = Path.Combine(Path.GetDirectoryName(Application.dataPath), "tools", "gen_sfx.py");
            if (!File.Exists(py))
            {
                total++;
                passed++;
                Debug.Log("✔ 音效 —— 生成器脚本不在库里，跳过名单交叉核对");
                return;
            }
            var found = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(File.ReadAllText(py, Encoding.UTF8),
                       "\"([a-z_]+)\"\\s*:\\s*dict\\(build=\\w+,\\s*level=[0-9.]+,\\s*gap=([0-9.]+)"))
            {
                float gap;
                if (float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out gap))
                    found[m.Groups[1].Value] = gap;
            }

            var diff = new List<string>();
            foreach (var kv in found)
            {
                if (!SoundSystem.IsKnownCue(kv.Key)) diff.Add("只在 py：" + kv.Key);
                else if (Math.Abs(SoundSystem.MinIntervalFor(kv.Key) - kv.Value) > 1e-5f)
                    diff.Add(kv.Key + " 去抖 py=" + kv.Value + " C#=" + SoundSystem.MinIntervalFor(kv.Key));
            }
            foreach (var n in SoundSystem.Cues) if (!found.ContainsKey(n)) diff.Add("只在 C#：" + n);
            Expect(ref total, ref passed, "名单与去抖表和 gen_sfx.py 一致", found.Count > 0 && diff.Count == 0, Join(diff));
        }

        // ---------------- 单条音频的硬指标 ----------------
        private static void QualityCases(ref int total, ref int passed, string dir, Dictionary<string, Wav> files)
        {
            var unreadable = new List<string>();
            var badFormat = new List<string>();
            var tooLong = new List<string>();
            var tooLoud = new List<string>();
            var silent = new List<string>();
            var dc = new List<string>();
            var clicky = new List<string>();

            foreach (var cue in SoundSystem.Cues)
            {
                Wav w;
                if (!files.TryGetValue(cue, out w)) continue;
                if (w.Error != null) { unreadable.Add(cue + "（" + w.Error + "）"); continue; }
                if (w.Rate != 44100 || w.Bits != 16 || w.Channels != 1)
                    badFormat.Add(cue + "（" + w.Rate + "Hz/" + w.Bits + "bit/" + w.Channels + "声道）");
                if (w.Seconds > SoundSystem.MaxCueSeconds)
                    tooLong.Add(cue + "（" + w.Num(w.Seconds, "F3") + "s）");
                if (w.Peak > (double)SoundSystem.PeakCeiling + PeakTolerance)
                    tooLoud.Add(cue + "（峰值 " + w.Num(w.Peak, "F3") + "）");
                if (w.Peak < 0.12)
                    silent.Add(cue + "（峰值只有 " + w.Num(w.Peak, "F3") + "）");
                if (w.Dc > DcTolerance)
                    dc.Add(cue + "（均值 " + w.Num(w.Dc, "F4") + "）");
                // 首尾必须还在爬坡/落到 0：开头直接给一个满幅样本就是耳朵能听见的爆音
                double head = w.Samples.Length > 0 ? Math.Abs(w.Samples[0]) : 1.0;
                double tail = w.Samples.Length > 0 ? Math.Abs(w.Samples[w.Samples.Length - 1]) : 1.0;
                if (head > 0.02 || tail > 0.05)
                    clicky.Add(cue + "（头 " + w.Num(head, "F3") + " 尾 " + w.Num(tail, "F3") + "）");
            }

            Expect(ref total, ref passed, "每个 wav 都解析得了", unreadable.Count == 0, Join(unreadable));
            Expect(ref total, ref passed, "每条都是 44100Hz/16bit/单声道", badFormat.Count == 0, Join(badFormat));
            Expect(ref total, ref passed, "每条时长不超 " + SoundSystem.MaxCueSeconds + "s", tooLong.Count == 0, Join(tooLong));
            Expect(ref total, ref passed, "每条峰值不超 " + SoundSystem.PeakCeiling + "（不削波）", tooLoud.Count == 0, Join(tooLoud));
            Expect(ref total, ref passed, "每条都听得见", silent.Count == 0, Join(silent));
            Expect(ref total, ref passed, "每条无 DC 偏移", dc.Count == 0, Join(dc));
            Expect(ref total, ref passed, "每条首尾淡入淡出（不爆音）", clicky.Count == 0, Join(clicky));

            long bytes = 0;
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.wav")) bytes += new FileInfo(f).Length;
            Expect(ref total, ref passed, "音频总体积在预算内",
                bytes > 0 && bytes <= TotalBudgetBytes,
                bytes + " 字节 / 预算 " + TotalBudgetBytes);
        }

        // ---------------- 去抖真值表 ----------------
        // 取样点全用二进制精确的分数（0.25/0.125/0.78125），免得断言测到的是浮点运气而不是逻辑
        private static void DebounceCases(ref int total, ref int passed)
        {
            const float g = 0.25f, now = 1f;
            Expect(ref total, ref passed, "从没响过要发声", SoundSystem.ShouldPlay(now, -1f, g), true);
            Expect(ref total, ref passed, "间隔外要发声", SoundSystem.ShouldPlay(now, 0.5f, g), true);
            Expect(ref total, ref passed, "间隔内不发声", SoundSystem.ShouldPlay(now, 0.875f, g), false);
            Expect(ref total, ref passed, "恰好等于阈值算到点", SoundSystem.ShouldPlay(now, 0.75f, g), true);
            Expect(ref total, ref passed, "差一点点不算到点", SoundSystem.ShouldPlay(now, 0.78125f, g), false);
            Expect(ref total, ref passed, "同一时刻重复调用被吞", SoundSystem.ShouldPlay(now, now, g), false);
            Expect(ref total, ref passed, "0 间隔＝不限流", SoundSystem.ShouldPlay(now, now, 0f), true);
            Expect(ref total, ref passed, "负间隔＝不限流", SoundSystem.ShouldPlay(now, now, -0.25f), true);
            Expect(ref total, ref passed, "帧钟倒退时放行（不能永久哑）", SoundSystem.ShouldPlay(now, 2f, g), true);

            float sum = 0f;
            foreach (var cue in SoundSystem.Cues) sum += SoundSystem.MinIntervalFor(cue);
            bool allPositive = true;
            foreach (var cue in SoundSystem.Cues)
                if (SoundSystem.MinIntervalFor(cue) <= 0f || SoundSystem.MinIntervalFor(cue) > 2f) allPositive = false;
            Expect(ref total, ref passed, "每条 cue 的去抖间隔在 0～2s 之间", allPositive,
                "合计 " + sum.ToString("F2", CultureInfo.InvariantCulture) + "s");
            Expect(ref total, ref passed, "名单外的 cue 用默认间隔", SoundSystem.MinIntervalFor("whatever"),
                SoundSystem.DefaultMinInterval);
        }

        // ---------------- 接线用的常量与映射 ----------------
        private static void MappingCases(ref int total, ref int passed)
        {
            Expect(ref total, ref passed, "静音键名跟 dayu. 前缀一致", SoundSystem.MuteKey, "dayu.mute");
            Expect(ref total, ref passed, "音量键名跟 dayu. 前缀一致", SoundSystem.VolumeKey, "dayu.volume");
            Expect(ref total, ref passed, "Resources 目录口径", SoundSystem.ResourceFolder, "Audio/");
            Expect(ref total, ref passed, "同时最多 6 路", SoundSystem.MaxVoices, 6);
            Expect(ref total, ref passed, "空串不是合法 cue", SoundSystem.IsKnownCue(""), false);
            Expect(ref total, ref passed, "null 不是合法 cue", SoundSystem.IsKnownCue(null), false);
            Expect(ref total, ref passed, "loopIter 走搬运音", SoundSystem.CueForVmEventKind("loopIter"), "step");
            Expect(ref total, ref passed, "vm 内部 error 走报错音", SoundSystem.CueForVmEventKind("error"), "error");
            Expect(ref total, ref passed, "loopEmpty 不发声音", SoundSystem.CueForVmEventKind("loopEmpty") ?? "-", "-");
            Expect(ref total, ref passed, "不认得的事件名返回 null", SoundSystem.CueForVmEventKind("nope") == null, true);
        }

        // ---------------- 工具 ----------------

        static string Join(List<string> items)
        {
            return items.Count == 0 ? "无" : string.Join("、", items.ToArray());
        }

        // RIFF 是逐 chunk 串的，不能按固定偏移读：LIST / fact 这些块会插在 fmt 和 data 之间
        static Wav Parse(string path, string name)
        {
            var w = new Wav { Name = name, Samples = new double[0], Rate = 0, Bits = 0, Channels = 0 };
            byte[] b;
            try { b = File.ReadAllBytes(path); }
            catch (Exception ex) { w.Error = "读不出来 " + ex.Message; return w; }
            if (b.Length < 12 || Ascii(b, 0) != "RIFF" || Ascii(b, 8) != "WAVE")
            {
                w.Error = "不是 RIFF/WAVE";
                return w;
            }
            int p = 12;
            bool hasFmt = false, hasData = false;
            int dataOff = 0, dataLen = 0;
            while (p + 8 <= b.Length)
            {
                string id = Ascii(b, p);
                int size = LE(b, p + 4);
                int body = p + 8;
                if (size < 0 || body + size > b.Length) size = b.Length - body;
                if (id == "fmt " && size >= 16)
                {
                    hasFmt = true;
                    w.Channels = LE16(b, body + 2);
                    w.Rate = LE(b, body + 4);
                    w.Bits = LE16(b, body + 14);
                }
                else if (id == "data")
                {
                    hasData = true;
                    dataOff = body;
                    dataLen = size;
                }
                p = body + size + (size & 1);      // chunk 按偶数字节对齐
            }
            if (!hasFmt || !hasData) { w.Error = "缺 fmt 或 data 块"; return w; }
            int stride = Math.Max(1, w.Bits / 8);
            int count = dataLen / stride;
            var s = new double[count];
            for (int i = 0; i < count; i++)
            {
                int off = dataOff + i * stride;
                if (off + 2 > b.Length) { Array.Resize(ref s, i); break; }
                s[i] = (short)LE16(b, off) / 32768.0;
            }
            w.Samples = s;
            return w;
        }

        static string Ascii(byte[] b, int off)
        {
            if (off + 4 > b.Length) return "?";
            return new string(new[] { (char)b[off], (char)b[off + 1], (char)b[off + 2], (char)b[off + 3] });
        }

        static int LE16(byte[] b, int off)
        {
            if (off + 2 > b.Length) return 0;
            return b[off] | (b[off + 1] << 8);
        }

        static int LE(byte[] b, int off)
        {
            if (off + 4 > b.Length) return 0;
            return b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24);
        }

        static void Expect(ref int total, ref int passed, string label, bool actual, string detail)
        {
            total++;
            if (actual) { passed++; Debug.Log("✔ 音效 —— " + label); }
            else Debug.Log("✘ 音效 —— " + label + "：" + detail);
        }

        static void Expect(ref int total, ref int passed, string label, bool actual, bool want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 音效 —— " + label); }
            else Debug.Log("✘ 音效 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Expect(ref int total, ref int passed, string label, string actual, string want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 音效 —— " + label + " = " + actual); }
            else Debug.Log("✘ 音效 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Expect(ref int total, ref int passed, string label, float actual, float want)
        {
            total++;
            if (Mathf.Abs(actual - want) < 1e-6f) { passed++; Debug.Log("✔ 音效 —— " + label + " = " + actual); }
            else Debug.Log("✘ 音效 —— " + label + " 期望 " + want + "，实际 " + actual);
        }

        static void Expect(ref int total, ref int passed, string label, int actual, int want)
        {
            total++;
            if (actual == want) { passed++; Debug.Log("✔ 音效 —— " + label + " = " + actual); }
            else Debug.Log("✘ 音效 —— " + label + " 期望 " + want + "，实际 " + actual);
        }
    }
}
