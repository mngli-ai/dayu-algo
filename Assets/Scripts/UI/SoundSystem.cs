using System;
using System.Collections.Generic;
using UnityEngine;

// 教学反馈音系统：clip 全部由 tools/gen_sfx.py 程序化合成，落在 Assets/Resources/Audio/<cue>.wav。
// 定位是「操作回执」而不是 BGM，所以每条 cue 都在 0.6s 以内、峰值留了 20% 余量，
// 并且每个 cue 自带最小间隔：单步播放会在一帧里塞进大量 VM 事件，不去抖就是一团噪音。
//
// 硬约束：Play() 在未初始化 / cue 不认识 / 静音 / 缺文件时一律静默返回，绝不抛异常，
// 因为接线点撒在十几个玩法回调里，任何一次异常都会把玩家那次操作打断。
public sealed class SoundSystem : MonoBehaviour
{
    public static SoundSystem I { get; private set; }

    // PlayerPrefs 键名沿用 App.cs 的 dayu. 前缀口径
    public const string MuteKey = "dayu.mute";
    public const string VolumeKey = "dayu.volume";
    public const float DefaultVolume = 0.8f;

    // Resources 下的目录前缀：Resources.Load<AudioClip>(ResourceFolder + cue)
    public const string ResourceFolder = "Audio/";
    public const string DiskFolder = "Resources/Audio";     // 相对 Application.dataPath，给 Editor 自测拼绝对路径用

    public const float DefaultMinInterval = 0.06f;
    public const float MaxCueSeconds = 0.6f;    // 与 tools/gen_sfx.py 的 MAX_DUR 同口径
    public const float PeakCeiling = 0.8f;      // 与 gen_sfx.py 的 CEILING 同口径
    public const int MaxVoices = 6;             // 同时最多 6 路，超了就抢最老的那一路

    // confetti/wa 是第十七轮加的「判题结论」音：只跟彩带/红十字的画面走，和 ac/fail 各管一层。
    // 名单是追加在尾部的，既有 17 条的名字与顺序一个都没动（自测在断言它们）。
    static readonly string[] CueNames =
    {
        "place", "link", "unlink", "del", "undo", "redo", "click", "toggle",
        "run", "step", "pass", "ac", "fail", "error", "levelin", "clear", "save",
        "confetti", "wa"
    };

    // 每个 cue 的最小间隔（秒），与 gen_sfx.py 里 CUES[name]["gap"] 一一对应：
    // 搬运/落子这类高频点收得紧，通关/进关这类结论音收得松，免得连点两下把庆祝音叠成糊。
    static readonly Dictionary<string, float> MinIntervals = new Dictionary<string, float>(StringComparer.Ordinal)
    {
        { "click", 0.05f }, { "place", 0.06f }, { "link", 0.06f }, { "unlink", 0.06f },
        { "del", 0.07f }, { "undo", 0.07f }, { "redo", 0.07f }, { "toggle", 0.06f },
        { "run", 0.25f }, { "step", 0.07f }, { "pass", 0.08f }, { "ac", 0.50f },
        { "fail", 0.30f }, { "error", 0.30f }, { "levelin", 0.40f }, { "clear", 0.80f },
        { "save", 0.45f },
        // 判题结论层新增：confetti 跟在 ac 之后半秒内会被去抖吞掉也没关系，彩带音是「补色」不是主音
        { "confetti", 0.35f }, { "wa", 0.30f },
    };

    // VM 事件名 → cue：单步/自动播放逐事件时一行接进去。
    // 名单之外的（含 loopEmpty / end）一律不发声音，只在状态栏里写字。
    static readonly Dictionary<string, string> VmEventCues = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "loopIter", "step" }, { "push", "step" }, { "pop", "step" }, { "create", "step" },
        { "input", "step" }, { "output", "step" }, { "varset", "step" }, { "step", "step" },
        { "branch", "toggle" }, { "error", "error" },
    };

    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
    readonly Dictionary<string, float> lastAt = new Dictionary<string, float>(StringComparer.Ordinal);
    AudioSource[] voices;
    int cursor;
    bool complained;

    static bool prefsLoaded;
    static bool muted;
    static float volume = DefaultVolume;

    public static IReadOnlyList<string> Cues { get { return CueNames; } }

    /// <summary>App 启动时调一次；已在场景里就只是取回现成的那个实例。</summary>
    public static SoundSystem Ensure(Transform parent = null)
    {
        if (I != null) return I;
        try
        {
            var go = new GameObject("SoundSystem");
            if (parent != null) go.transform.SetParent(parent, false);
            else UnityEngine.Object.DontDestroyOnLoad(go);
            LoadPrefs();
            go.AddComponent<SoundSystem>();
            EnsureListener();
            return I;
        }
        catch (Exception ex)
        {
            Debug.Log("[sfx] 初始化失败，本局全程静音：" + ex.Message);
            return null;
        }
    }

    void Awake()
    {
        I = this;
        LoadPrefs();
        BuildVoices();
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    public static void Play(string cue)
    {
        var s = I;
        if (s == null || string.IsNullOrEmpty(cue)) return;
        if (IsMuted) return;
        if (!IsKnownCue(cue)) return;      // 名单写错就当无事发生，不去 Resources 里空跑一趟
        try
        {
            s.PlayOnce(cue);
        }
        catch (Exception ex)
        {
            // 只报一次：缺文件、Resources 未就绪这类问题不该在每个操作里刷一行日志
            if (!s.complained)
            {
                s.complained = true;
                Debug.Log("[sfx] 发声失败，后续同类问题不再重复：" + ex.Message);
            }
        }
    }

    void PlayOnce(string cue)
    {
        float now = Time.realtimeSinceStartup;   // 用不受 timeScale 影响的帧钟：慢放时去抖窗口才是玩家体感的那个毫秒
        float last;
        if (!lastAt.TryGetValue(cue, out last)) last = -1f;
        if (!ShouldPlay(now, last, MinIntervalFor(cue))) return;
        var clip = ClipFor(cue);
        if (clip == null) return;
        lastAt[cue] = now;
        var v = NextVoice();
        if (v == null) return;
        v.clip = clip;
        v.volume = Volume;
        v.Stop();           // 复用的那一路可能还压着上一句的尾巴，先停干净再播
        v.Play();
        // 每发声一次落一行：真实鼠标验收要拿这行确认「AC 那一声真的排进了音频设备」，
        // 而不是只看 App 调没调 Play。被去抖挡掉的不落行，所以这行出现就等于玩家听见了。
        Debug.Log("[sfx] 发声 " + cue);
    }

    /// <summary>去抖判定，纯函数，自测直接按真值表打。</summary>
    public static bool ShouldPlay(float now, float last, float minInterval)
    {
        if (minInterval <= 0f) return true;      // 0 与负数＝不限流
        if (last < 0f) return true;              // 从没响过
        float d = now - last;
        if (d < 0f) return true;                 // 帧钟倒退时宁可重复也不能永久不出声
        return d >= minInterval;                 // 恰好等于阈值算到点
    }

    public static bool IsKnownCue(string cue)
    {
        if (string.IsNullOrEmpty(cue)) return false;
        for (int i = 0; i < CueNames.Length; i++) if (CueNames[i] == cue) return true;
        return false;
    }

    public static float MinIntervalFor(string cue)
    {
        float v;
        return cue != null && MinIntervals.TryGetValue(cue, out v) ? v : DefaultMinInterval;
    }

    /// <summary>VM 事件名换成 cue 名；不该发声的事件返回 null（Play(null) 自己也是静默的）。</summary>
    public static string CueForVmEventKind(string kind)
    {
        string cue;
        return kind != null && VmEventCues.TryGetValue(kind, out cue) ? cue : null;
    }

    // ---------------- 顶栏静音开关 ----------------

    public static bool IsMuted
    {
        get { LoadPrefs(); return muted; }
    }

    /// <summary>存进 PlayerPrefs 的音量本身，静音时它保持不变，取消静音就回来了。</summary>
    public static float Volume
    {
        get { LoadPrefs(); return volume; }
    }

    /// <summary>顶栏按钮用：切换静音并返回切换后的状态（true = 现在静音）。</summary>
    public static bool ToggleMute()
    {
        LoadPrefs();
        muted = !muted;
        PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
        PlayerPrefs.Save();
        if (I != null) I.ApplyGainToVoices();
        // 取消静音这一下要给个反馈，不然玩家不知道自己按没按对；这里绕过静音判定直接确认
        if (!muted) Play("click");
        return muted;
    }

    public static void SetVolume(float value)
    {
        LoadPrefs();
        volume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(VolumeKey, volume);
        PlayerPrefs.Save();
        if (I != null) I.ApplyGainToVoices();
    }

    static void LoadPrefs()
    {
        if (prefsLoaded) return;
        prefsLoaded = true;
        try
        {
            muted = PlayerPrefs.GetInt(MuteKey, 0) != 0;
            volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, DefaultVolume));
        }
        catch (Exception ex) { Debug.Log("[sfx] 读音量设置失败，用默认值：" + ex.Message); }
    }

    // ---------------- 内部 ----------------

    void BuildVoices()
    {
        voices = new AudioSource[MaxVoices];
        for (int i = 0; i < MaxVoices; i++)
        {
            var v = gameObject.AddComponent<AudioSource>();
            v.playOnAwake = false;
            v.loop = false;
            v.spatialBlend = 0f;        // 教学界面是 2D 的，别按 3D 衰减
            v.rolloffMode = AudioRolloffMode.Linear;
            v.dopplerLevel = 0f;
            v.volume = Volume;
            v.Stop();
            voices[i] = v;
        }
    }

    AudioSource NextVoice()
    {
        if (voices == null) BuildVoices();
        int start = cursor;
        // 先找一路真闲着的；全都在响就抢最老那一路，保证最新一次操作一定听得见
        for (int i = 0; i < MaxVoices; i++)
        {
            int k = (start + i) % MaxVoices;
            if (voices[k] != null && !voices[k].isPlaying) { cursor = (k + 1) % MaxVoices; return voices[k]; }
        }
        var v = voices[start];
        cursor = (start + 1) % MaxVoices;
        return v;
    }

    void ApplyGainToVoices()
    {
        if (voices == null) return;
        for (int i = 0; i < voices.Length; i++)
            if (voices[i] != null) voices[i].volume = IsMuted ? 0f : Volume;
    }

    AudioClip ClipFor(string cue)
    {
        AudioClip clip;
        if (clips.TryGetValue(cue, out clip)) return clip;   // 加载失败也缓存 null，不重复砸 Resources
        try
        {
            clip = Resources.Load<AudioClip>(ResourceFolder + cue);
        }
        catch (Exception)
        {
            clip = null;
        }
        clips[cue] = clip;
        return clip;
    }

    static void EnsureListener()
    {
        // 工程是代码装配场景的，没人摆 AudioListener，缺了就永远听不见：这里补一个
        if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null)
            new GameObject("AudioListener").AddComponent<AudioListener>();
    }

    /// <summary>换关 / 重来 / 退菜单时掐掉还在播的长音（通关琶音之类）。</summary>
    public static void StopAll()
    {
        var s = I;
        if (s == null || s.voices == null) return;
        for (int i = 0; i < s.voices.Length; i++)
            if (s.voices[i] != null) s.voices[i].Stop();
    }
}
