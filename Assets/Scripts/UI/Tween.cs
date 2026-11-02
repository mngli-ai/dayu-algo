using System;
using System.Collections;
using UnityEngine;

// 项目里没有补间库，之前各处自己写 Lerp + SmoothStep 协程，写法还互不相同。
// 新的交互动画一律走这里：Run 只负责把 0→1 推进去，调用方写「k 时长什么样」。
public static class Tween
{
    public static IEnumerator Run(MonoBehaviour host, float dur, Action<float> step, Action done = null)
    {
        if (host == null || step == null) yield break;
        if (dur <= 0f) { step(1f); if (done != null) done(); yield break; }
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            step(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur)));
            yield return null;
        }
        step(1f);
        if (done != null) done();
    }

    // 淡入淡出：CanvasGroup 只改透明度，不动层级，也不影响射线命中
    public static IEnumerator Fade(MonoBehaviour host, CanvasGroup g, float from, float to, float dur)
    {
        if (g == null) yield break;
        g.alpha = from;
        yield return Run(host, dur, k => g.alpha = Mathf.Lerp(from, to, k));
    }
}
