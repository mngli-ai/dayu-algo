using UnityEditor;
using UnityEngine;

// 像素资产的导入设置：不缩放、不压缩、点采样、无 mipmap
public static class ImportSettings
{
    public static void Apply()
    {
        Fix("Assets/Resources/atlas.png", false);
        Fix("Assets/Art/Font/atlas.png", false);
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Sprites" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            Fix(path, false);
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Props" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            Fix(path, true);
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Props" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            Fix(path, false);
        }
        // r18 水下背景 5 层：第三方真像素图（CraftPix OGA-BY 3.0），点采样、不压缩、不缩放
        if (AssetDatabase.IsValidFolder("Assets/Resources/Backdrop"))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Backdrop" }))
            {
                Fix(AssetDatabase.GUIDToAssetPath(guid), false);
            }
        }
        foreach (var dir in new[] { "Assets/Art/ModuleIcons", "Assets/Resources/ModuleIcons" })
        {
            if (!AssetDatabase.IsValidFolder(dir)) continue;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                Fix(path, false);
            }
        }
        // 主角立绘是第三方高清插画，不是像素图：缩放显示时用双线性，点采样会出锯齿
        if (AssetDatabase.IsValidFolder("Assets/Resources/Hero"))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Hero" }))
            {
                Fix(AssetDatabase.GUIDToAssetPath(guid), false, FilterMode.Bilinear);
            }
        }
        AssetDatabase.Refresh();
    }

    static void Fix(string path, bool isSprite, FilterMode filter = FilterMode.Point)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;

        bool dirty = false;

        // 关键：非 2 的幂纹理不能被缩放
        if (imp.npotScale != TextureImporterNPOTScale.None) { imp.npotScale = TextureImporterNPOTScale.None; dirty = true; }
        if (imp.filterMode != filter) { imp.filterMode = filter; dirty = true; }
        if (imp.mipmapEnabled) { imp.mipmapEnabled = false; dirty = true; }
        if (imp.textureCompression != TextureImporterCompression.Uncompressed) { imp.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
        if (imp.maxTextureSize < 2048) { imp.maxTextureSize = 2048; dirty = true; }
        if (imp.isReadable == false) { imp.isReadable = true; dirty = true; }

        var want = isSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        if (imp.textureType != want) { imp.textureType = want; dirty = true; }

        if (dirty)
        {
            imp.SaveAndReimport();
            Debug.Log("[ImportSettings] fixed " + path);
        }
    }
}
