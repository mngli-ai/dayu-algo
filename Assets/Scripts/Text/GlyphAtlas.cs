using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public sealed class GlyphAtlas
{
    public struct Glyph { public int X, Y, Adv; }
    static readonly Dictionary<char, Glyph> glyphs = new();
    public static Texture2D Texture { get; private set; }
    public static void Init(Texture2D tex, string json)
    {
        Texture = tex;
        glyphs.Clear();
        if (string.IsNullOrEmpty(json)) return;
        const string pat = "\\\"(?<ch>(?:\\\\.|[^\\\"])*)\\\"\\s*:\\s*\\{\\s*\\\"x\\\"\\s*:\\s*(?<x>-?\\d+)\\s*,\\s*\\\"y\\\"\\s*:\\s*(?<y>-?\\d+)\\s*,\\s*\\\"adv\\\"\\s*:\\s*(?<adv>-?\\d+)";
        foreach (Match m in Regex.Matches(json, pat))
        {
            string key = Regex.Unescape(m.Groups["ch"].Value);
            if (key.Length == 0) continue;
            glyphs[key[0]] = new Glyph { X = int.Parse(m.Groups["x"].Value), Y = int.Parse(m.Groups["y"].Value), Adv = int.Parse(m.Groups["adv"].Value) };
        }
    }
    public static bool TryGet(char c, out Glyph g) => glyphs.TryGetValue(c, out g);
    public static GlyphAtlas Load(Texture2D tex, string json) { Init(tex, json); return new GlyphAtlas(); }
    public bool TryGetInstance(char c, out Glyph g) => TryGet(c, out g);
}
