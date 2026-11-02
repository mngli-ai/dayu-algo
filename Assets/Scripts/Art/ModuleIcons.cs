using System.Collections.Generic;
using UnityEngine;

// 运行时加载 Assets/Resources/ModuleIcons 下的 16x16 模块图标。
// 每张图对应一个节点类型，由 tools/gen_art.py icons 生成。
public static class ModuleIcons
{
    const string Dir = "ModuleIcons/mod_";
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    static readonly HashSet<string> missing = new HashSet<string>();

    public static Sprite Get(string nodeTypeKey)
    {
        if (string.IsNullOrEmpty(nodeTypeKey)) return null;
        Sprite sp;
        if (cache.TryGetValue(nodeTypeKey, out sp)) return sp;
        if (missing.Contains(nodeTypeKey)) return null;
        var tex = Resources.Load<Texture2D>(Dir + nodeTypeKey + "_16x16");
        if (tex == null) { missing.Add(nodeTypeKey); return null; }
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0f, 1f), 100f, 0, SpriteMeshType.FullRect);
        sp.name = "mod_" + nodeTypeKey;
        cache[nodeTypeKey] = sp;
        return sp;
    }
}
