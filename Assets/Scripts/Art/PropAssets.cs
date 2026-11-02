using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 运行时加载 Assets/Art/Props 下的像素素材并切成帧。
// 编辑器构建时把资源拷进 Resources，运行期直接 Resources.Load 取到。
public static class PropAssets
{
    static bool loaded;
    static readonly Dictionary<string, PropSpriteSheet> sheets = new Dictionary<string, PropSpriteSheet>();

    static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Load("box_int", "box_int_16x16", 16, 16, 1);
        Load("box_real", "box_real_16x16", 16, 16, 1);
        Load("box_bool", "box_bool_16x16", 16, 16, 1);
        Load("box_char", "box_char_16x16", 16, 16, 1);
        Load("box_string", "box_string_16x16", 16, 16, 1);
        Load("box_merge", "box_merge_16x16_4f", 16, 16, 4);
        Load("switch", "switch_32x16_3f", 32, 16, 3);
        Load("factory_press", "factory_press_48x32_6f", 48, 32, 6);
        Load("factory_roof", "factory_roof_16x16", 16, 16, 1);
        Load("factory_wall", "factory_wall_16x16", 16, 16, 1);
        Load("factory_floor", "factory_floor_16x16", 16, 16, 1);
        Load("factory_gear", "factory_gear_16x16", 16, 16, 1);
        Load("factory_chimney", "factory_chimney_16x16", 16, 16, 1);
        Load("dock", "dock_48x32_2f", 48, 32, 2);
        Load("array_loco", "array_loco_48x32", 48, 32, 1);
        Load("array_car", "array_car_32x32", 32, 32, 1);
        Load("array_coupler", "array_coupler_16x8", 16, 8, 1);
        Load("array_rail", "array_rail_16x8", 16, 8, 1);
    }

    static void Load(string key, string resourceName, int fw, int fh, int columns)
    {
        var tex = Resources.Load<Texture2D>("Props/" + resourceName);
        if (tex == null) return;
        sheets[key] = PropSpriteSheet.FromTexture(tex, fw, fh, columns);
    }

    public static PropSpriteSheet Get(string key)
    {
        EnsureLoaded();
        PropSpriteSheet s;
        return sheets.TryGetValue(key, out s) ? s : null;
    }

    public static Sprite Box(string type)
    {
        var sheet = Get("box_" + type);
        return sheet != null && sheet.FrameCount > 0 ? sheet[0] : null;
    }
}
