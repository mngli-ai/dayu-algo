using UnityEngine;

// 主角「鲸鱼娘女仆」素材。形象本身是第三方作品（CC BY-NC-SA 4.0），
// 这里只按 tools/cut_hero.py 从原始动画/立绘里抽帧裁切，不重绘。
// 署名链见 ArtInbox/SOURCES.md 与游戏内 BuildFooter。
public static class HeroAssets
{
    static PropSpriteSheet idle;
    static PropSpriteSheet happy;
    static Sprite face;
    static Sprite full;

    public static PropSpriteSheet Idle
    {
        get
        {
            if (idle == null) idle = Sheet("hero_idle_48x48_8f", 48, 48, 8);
            return idle;
        }
    }

    public static PropSpriteSheet Happy
    {
        get
        {
            if (happy == null) happy = Sheet("hero_happy_48x48_8f", 48, 48, 8);
            return happy;
        }
    }

    /// <summary>头像（对话、结算弹层用）。</summary>
    public static Sprite Face
    {
        get
        {
            if (face == null) face = Single("hero_face_96x96");
            return face;
        }
    }

    /// <summary>全身立绘（主菜单用）。</summary>
    public static Sprite Full
    {
        get
        {
            if (full == null) full = Single("hero_full_260x460");
            return full;
        }
    }

    static PropSpriteSheet Sheet(string res, int fw, int fh, int cols)
    {
        var tex = Resources.Load<Texture2D>("Hero/" + res);
        return tex == null ? null : PropSpriteSheet.FromTexture(tex, fw, fh, cols);
    }

    static Sprite Single(string res)
    {
        var tex = Resources.Load<Texture2D>("Hero/" + res);
        if (tex == null) return null;
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
    }
}
