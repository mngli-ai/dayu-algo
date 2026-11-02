using System.Collections.Generic;
using UnityEngine;

// 把等宽 sprite sheet 在运行时切成帧，供 UI 逐帧动画使用。
// 不依赖编辑器切片，导入设置只要求 Sprite / Multiple 或 Default 均可。
public sealed class PropSpriteSheet
{
    readonly List<Sprite> frames = new List<Sprite>();
    public int FrameCount { get { return frames.Count; } }
    public float Fps = 10f;

    public Sprite this[int index]
    {
        get
        {
            if (frames.Count == 0) return null;
            return frames[Mathf.Clamp(index, 0, frames.Count - 1)];
        }
    }

    /// <summary>frameWidth / frameHeight 是单帧像素尺寸，columns 是每行帧数。</summary>
    public static PropSpriteSheet FromTexture(Texture2D tex, int frameWidth, int frameHeight, int columns, int padding = 1)
    {
        var sheet = new PropSpriteSheet();
        if (tex == null || frameWidth <= 0 || frameHeight <= 0) return sheet;
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        int cols = Mathf.Max(1, columns);
        int count = tex.width / frameWidth;
        for (int i = 0; i < count; i++)
        {
            int cx = i % cols, cy = i / cols;
            int x = cx * frameWidth, y = cy * frameHeight;
            if (y + frameHeight > tex.height) break;
            var sp = Sprite.Create(tex, new Rect(x, y, frameWidth, frameHeight), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(padding, padding, padding, padding));
            sp.name = tex.name + "_" + i;
            sheet.frames.Add(sp);
        }
        return sheet;
    }

    public void Clear()
    {
        foreach (var f in frames) if (f != null) Object.Destroy(f);
        frames.Clear();
    }
}
