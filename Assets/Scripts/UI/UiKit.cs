using System;
using UnityEngine;
using UnityEngine.UI;

// 按钮/hover 的统一常量 + 唯一的补间出口 + 画布暗角。
// 补间只从 Animate 这一个口子出去，截图模式（-shot）才能一处止血、不留半截动画。
// 面板不在这儿：App / GraphView 的底板一律是 Image + 运行时 1x1 白 Sprite（PanelGraphic 依赖 rect 时序，batch 下不渲染），
// 所以这里不提供 FancyPanel / SectionBar 一类的 PanelGraphic 便捷构造，免得多一套没人调用的「假入口」。
public static class UiKit
{
    // 由主 agent 在 App.cs 里与 GraphCanvas.InstantView = HasArg("-shot") 同一处赋值
    public static bool Instant;

    public const float Lift = 2f;          // hover 浮起量：只挪 Bg / 文字这两个子节点，根矩形一个字节不动
    public const float Press = 1f;         // 按下回落量
    public const float RaiseDur = .06f;    // 浮起时长
    public const float PressDur = .04f;    // 按下时长：比浮起快，手感才跟得上手指
    public const int ButtonStroke = 1;     // 按钮描边带宽
    public const int ButtonDrop = 2;       // 按钮底部投影下移 px

    /// 统一补间入口：Instant 或 dur<=0 直接落终态、不起协程。返回协程句柄（没起协程就是 null）。
    public static Coroutine Animate(MonoBehaviour host, float dur, Action<float> step, Action done = null)
    {
        if (host == null || step == null)
        {
            if (step != null) step(1f);
            if (done != null) done();
            return null;
        }
        if (Instant || dur <= 0f) { step(1f); if (done != null) done(); return null; }
        return host.StartCoroutine(Tween.Run(host, dur, step, done));
    }

    /// 画布暗角：铺满 parent 的一层顶点色渐变，中间不压、四角最深。
    /// 挂在 CanvasHost 里、点阵之后、卡片之前（后建的兄弟节点画在上面），这样只压背景不会把卡片和文字蒙灰。
    /// raycastTarget=false，不吃点击也不吃滚轮。
    public static RectTransform Vignette(RectTransform parent, float maxAlpha = .22f)
    {
        if (parent == null) return null;
        var go = new GameObject("Vignette", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var g = go.AddComponent<VignetteGraphic>();
        g.MaxAlpha = maxAlpha;
        g.raycastTarget = false;
        return rt;
    }
}

// 暗角用顶点色渐变，不用贴图：一张渐变图要么引美术资源、要么运行时生成 Texture2D，
// 而这块覆盖本来就只有四角有内容，网格 12x12 已经足够顺。
public sealed class VignetteGraphic : MaskableGraphic
{
    public float MaxAlpha = .22f;              // 角上的最暗程度，中心恒为 0
    public Color32 Tint = Theme.Ink;           // 只从 16 色板取色，深浅靠透明度

    const int Div = 12;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = rectTransform.rect;
        if (r.width < 1f || r.height < 1f) return;
        float cx = r.xMin + r.width * .5f, cy = r.yMin + r.height * .5f;
        float rx = r.width * .5f, ry = r.height * .5f;
        int cols = Div + 1;
        for (int iy = 0; iy <= Div; iy++)
        {
            float ty = iy / (float)Div;
            float y = r.yMin + r.height * ty;
            for (int ix = 0; ix <= Div; ix++)
            {
                float tx = ix / (float)Div;
                float x = r.xMin + r.width * tx;
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                float d2 = dx * dx + dy * dy;                     // 归一化平方距离：中心 0、边中点 1、角上 2
                float k = Mathf.Clamp01((d2 - .36f) / 1.3f);      // 中间 6 成半径以内保持全干净
                float a = MaxAlpha * Mathf.SmoothStep(0f, 1f, k);
                vh.AddVert(new Vector3(x, y, 0f), Theme.A(Tint, a), Vector2.zero);
            }
        }
        for (int iy = 0; iy < Div; iy++)
            for (int ix = 0; ix < Div; ix++)
            {
                int v = iy * cols + ix;
                vh.AddTriangle(v, v + cols, v + 1);
                vh.AddTriangle(v + 1, v + cols, v + cols + 1);
            }
    }
}
