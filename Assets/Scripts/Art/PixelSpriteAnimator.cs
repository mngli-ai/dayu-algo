using UnityEngine;
using UnityEngine.UI;

// UI 内逐帧动画：把等宽 sprite sheet 的帧数组交给 Image 播放。
// 像素图本身不缩放、不旋转，位移由外部容器完成。
public sealed class PixelSpriteAnimator : MonoBehaviour
{
    public Image Target;
    public Sprite[] Frames;
    public float Fps = 10f;
    public bool Loop = true;
    public bool PlayOnEnable = true;

    float clock;
    int index;
    bool playing;

    void OnEnable()
    {
        if (PlayOnEnable) Play();
    }

    void Update()
    {
        if (!playing || Frames == null || Frames.Length == 0 || Target == null) return;
        clock += Time.deltaTime;
        float step = 1f / Mathf.Max(1f, Fps);
        while (clock >= step)
        {
            clock -= step;
            index++;
            if (index >= Frames.Length)
            {
                if (Loop) index = 0;
                else { index = Frames.Length - 1; playing = false; break; }
            }
            Target.sprite = Frames[index];
        }
    }

    public void Play()
    {
        if (Frames == null || Frames.Length == 0) return;
        index = Mathf.Clamp(index, 0, Frames.Length - 1);
        if (Target != null) Target.sprite = Frames[index];
        clock = 0f;
        playing = true;
    }

    public void Play(Sprite[] frames, float fps = 10f, bool loop = true)
    {
        Frames = frames;
        Fps = fps;
        Loop = loop;
        index = 0;
        Play();
    }

    public void Stop() { playing = false; }
    public bool IsPlaying { get { return playing; } }
}
