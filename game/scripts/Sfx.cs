using Godot;

namespace InstinctGame;

/// <summary>
/// 소리. 임시 소리는 모두 코드로 합성한다(종, 북, 나무 두드림, 뜯는 현). game/assets/sound/{이름}.ogg 나 .wav 가 있으면 그것을 쓴다.
/// 이름 목록은 ASSETS.md. 버스는 Music과 Effects 둘이고 크기는 설정(Prefs)을 따른다.
/// </summary>
public partial class Sfx : Node
{
    const int Rate = 22050;
    static Sfx? _me;
    readonly Dictionary<string, AudioStream> _streams = new();
    readonly Dictionary<string, double> _last = new();
    readonly List<AudioStreamPlayer> _pool = new();
    AudioStreamPlayer _music = null!;
    double _now;

    public static readonly string[] Names = { "click", "tick", "build", "coin", "bell", "alarm", "battle", "season", "hunt", "feast", "gift", "raid", "fall", "save", "music" };

    public override void _Ready()
    {
        _me = this;
        foreach (var bus in new[] { "Music", "Effects" })
        {
            if (AudioServer.GetBusIndex(bus) >= 0) continue;
            AudioServer.AddBus();
            int i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, bus);
            AudioServer.SetBusSend(i, "Master");
        }
        for (int k = 0; k < 10; k++)
        {
            var p = new AudioStreamPlayer { Bus = "Effects" };
            AddChild(p);
            _pool.Add(p);
        }
        _music = new AudioStreamPlayer { Bus = "Music", Stream = Get("music") };
        AddChild(_music);
        ApplyVolumes();
        if (DisplayServer.GetName() != "headless") _music.Play();
    }

    public override void _Process(double delta) => _now += delta;

    public static void ApplyVolumes()
    {
        static void Set(string bus, float v)
        {
            int i = AudioServer.GetBusIndex(bus);
            if (i < 0) return;
            AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Math.Max(0.0001f, v)));
            AudioServer.SetBusMute(i, v < 0.01f);
        }
        Set("Master", Prefs.Master);
        Set("Music", Prefs.Music * 0.7f);
        Set("Effects", Prefs.Effects);
    }

    /// <summary>소리 하나를 낸다. 같은 소리가 너무 잦으면 거른다.</summary>
    public static void Play(string name, float volume = 1f)
    {
        if (_me == null) return;
        if (_me._last.TryGetValue(name, out var t) && _me._now - t < 0.07) return;
        _me._last[name] = _me._now;
        var p = _me._pool.FirstOrDefault(x => !x.Playing) ?? _me._pool[0];
        p.Stream = _me.Get(name);
        p.VolumeDb = Mathf.LinearToDb(volume);
        p.Play();
    }

    AudioStream Get(string name)
    {
        if (_streams.TryGetValue(name, out var s)) return s;
        s = FromFile(name) ?? Make(name);
        _streams[name] = s;
        return s;
    }

    static AudioStream? FromFile(string name)
    {
        foreach (var ext in new[] { "ogg", "wav" })
        {
            string res = $"res://assets/sound/{name}.{ext}";
            if (!Godot.FileAccess.FileExists(res)) continue;
            string path = ProjectSettings.GlobalizePath(res);
            try
            {
                AudioStream? st = ext == "ogg" ? AudioStreamOggVorbis.LoadFromFile(path) : AudioStreamWav.LoadFromFile(path);
                if (st is AudioStreamOggVorbis ov && name == "music") ov.Loop = true;
                if (st != null) return st;
            }
            catch (Exception e) { GD.PushWarning($"소리 {path}: {e.Message}"); }
        }
        return null;
    }

    // ───────── 합성 ─────────

    static AudioStreamWav Wav(float[] buf, bool loop = false)
    {
        var data = new byte[buf.Length * 2];
        for (int i = 0; i < buf.Length; i++)
        {
            short v = (short)(Math.Clamp(buf[i], -1f, 1f) * 32000);
            data[i * 2] = (byte)(v & 0xff);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }
        var w = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
        if (loop)
        {
            w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            w.LoopBegin = 0;
            w.LoopEnd = buf.Length;
        }
        return w;
    }

    static float Rnd(ref uint s)
    {
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return (s & 0xFFFFFF) / (float)0x1000000 * 2 - 1;
    }

    /// <summary>울리는 소리: 부분음(배율, 크기, 감쇠)을 더한다.</summary>
    static void Tone(float[] b, float at, float f, (float ratio, float amp, float decay)[] parts, float attack = 0.003f, float gain = 1f)
    {
        int start = (int)(at * Rate);
        for (int i = start; i < b.Length; i++)
        {
            float t = (i - start) / (float)Rate;
            float env = Math.Min(1, t / attack);
            float v = 0;
            foreach (var (ratio, amp, decay) in parts) v += amp * MathF.Exp(-t / decay) * MathF.Sin(Mathf.Tau * f * ratio * t);
            b[i] += v * env * gain;
        }
    }

    /// <summary>북: 높이가 떨어지는 낮은 사인과 잡음.</summary>
    static void Drum(float[] b, float at, float f, float gain, ref uint seed)
    {
        int start = (int)(at * Rate);
        float ph = 0;
        for (int i = start; i < Math.Min(b.Length, start + Rate); i++)
        {
            float t = (i - start) / (float)Rate;
            ph += Mathf.Tau * f * (1 + 1.6f * MathF.Exp(-t / 0.03f)) / Rate;
            b[i] += gain * (MathF.Sin(ph) * MathF.Exp(-t / 0.22f) + 0.25f * Rnd(ref seed) * MathF.Exp(-t / 0.02f));
        }
    }

    /// <summary>나무를 두드리는 소리.</summary>
    static void Knock(float[] b, float at, float gain, ref uint seed)
    {
        int start = (int)(at * Rate);
        float lp = 0;
        for (int i = start; i < Math.Min(b.Length, start + Rate / 4); i++)
        {
            float t = (i - start) / (float)Rate;
            lp += (Rnd(ref seed) - lp) * 0.25f;
            b[i] += gain * (lp * 2.2f * MathF.Exp(-t / 0.018f) + 0.6f * MathF.Sin(Mathf.Tau * 190 * t) * MathF.Exp(-t / 0.05f));
        }
    }

    /// <summary>뜯는 현(카플러스-스트롱).</summary>
    static void Pluck(float[] b, float at, float f, float gain, ref uint seed, float decay = 0.996f)
    {
        int n = Math.Max(2, (int)(Rate / f));
        var ring = new float[n];
        for (int k = 0; k < n; k++) ring[k] = Rnd(ref seed);
        int start = (int)(at * Rate), len = Math.Min(b.Length - start, Rate * 4);
        float prev = 0;
        for (int i = 0; i < len; i++)
        {
            int k = i % n;
            float v = ring[k];
            ring[k] = (v + prev) * 0.5f * decay;
            prev = v;
            float env = Math.Min(1, i / 40f);
            b[start + i] += gain * v * env;
        }
    }

    static float[] Buf(float seconds) => new float[(int)(seconds * Rate)];

    static AudioStream Make(string name)
    {
        uint seed = 0x9E3779B9u ^ (uint)name.GetHashCode();
        float[] b;
        switch (name)
        {
            case "click":
                b = Buf(0.06f);
                Tone(b, 0, 1650, new[] { (1f, 0.35f, 0.012f), (2.4f, 0.12f, 0.006f) });
                break;
            case "tick":
                b = Buf(0.08f);
                Tone(b, 0, 1100, new[] { (1f, 0.32f, 0.02f), (3f, 0.08f, 0.008f) });
                break;
            case "build":
                b = Buf(0.7f);
                Knock(b, 0, 0.55f, ref seed);
                Knock(b, 0.17f, 0.45f, ref seed);
                Knock(b, 0.34f, 0.5f, ref seed);
                break;
            case "coin": case "gift":
                b = Buf(0.9f);
                Tone(b, 0, 1568, new[] { (1f, 0.25f, 0.25f), (2.76f, 0.08f, 0.1f) });
                Tone(b, 0.09f, 2093, new[] { (1f, 0.25f, 0.35f), (2.76f, 0.08f, 0.12f) });
                break;
            case "save":
                b = Buf(0.9f);
                Tone(b, 0, 784, new[] { (1f, 0.22f, 0.25f), (2f, 0.06f, 0.15f) });
                Tone(b, 0.11f, 1175, new[] { (1f, 0.22f, 0.4f), (2f, 0.06f, 0.2f) });
                break;
            case "bell":
                b = Buf(2.6f);
                Tone(b, 0, 523, new[] { (1f, 0.32f, 0.9f), (2.0f, 0.16f, 0.5f), (2.76f, 0.12f, 0.35f), (5.4f, 0.05f, 0.15f), (0.5f, 0.1f, 1.2f) });
                break;
            case "season":
                b = Buf(2.2f);
                Tone(b, 0, 880, new[] { (1f, 0.1f, 0.7f), (2.76f, 0.03f, 0.2f) });
                Tone(b, 0.22f, 1319, new[] { (1f, 0.08f, 0.9f), (2.76f, 0.02f, 0.2f) });
                break;
            case "alarm":
                b = Buf(2.6f);
                for (int i = 0; i < b.Length; i++)
                {
                    float t = i / (float)Rate;
                    float env = Math.Min(1, t / 0.35f) * MathF.Exp(-Math.Max(0, t - 1.2f) / 0.4f);
                    float v = 0;
                    for (int h = 1; h <= 6; h++) v += MathF.Sin(Mathf.Tau * 110 * h * t * (1 + 0.003f * MathF.Sin(t * 30))) / (h * 1.3f);
                    b[i] += 0.22f * v * env;
                }
                Drum(b, 0, 60, 0.6f, ref seed);
                Drum(b, 0.5f, 60, 0.5f, ref seed);
                break;
            case "battle": case "raid":
                b = Buf(1.4f);
                Drum(b, 0, 70, 0.6f, ref seed);
                Drum(b, 0.16f, 66, 0.45f, ref seed);
                Drum(b, 0.32f, 62, 0.55f, ref seed);
                for (int k = 0; k < 4; k++) Tone(b, 0.12f + k * 0.11f, 2400 + 300 * k, new[] { (1f, 0.07f, 0.06f), (1.43f, 0.06f, 0.05f) });
                break;
            case "hunt":
                b = Buf(1.2f);
                Drum(b, 0, 85, 0.5f, ref seed);
                Drum(b, 0.22f, 85, 0.4f, ref seed);
                Drum(b, 0.33f, 95, 0.4f, ref seed);
                Drum(b, 0.55f, 80, 0.55f, ref seed);
                break;
            case "feast":
                b = Buf(2.2f);
                float[] notes = { 440, 523, 587, 659, 784 };
                for (int k = 0; k < 6; k++) Pluck(b, k * 0.12f, notes[(k * 2) % 5], 0.3f, ref seed);
                Drum(b, 0, 90, 0.3f, ref seed);
                Drum(b, 0.36f, 90, 0.3f, ref seed);
                break;
            case "fall":
                b = Buf(4f);
                Tone(b, 0, 98, new[] { (1f, 0.35f, 1.6f), (2.1f, 0.18f, 0.9f), (3.0f, 0.1f, 0.6f), (4.2f, 0.06f, 0.4f) }, 0.02f);
                break;
            case "music":
                return Wav(Music(ref seed), true);
            default:
                b = Buf(0.05f);
                break;
        }
        // 끝을 부드럽게 줄인다.
        int fade = Math.Min(b.Length, Rate / 50);
        for (int i = 0; i < fade; i++) b[b.Length - 1 - i] *= i / (float)fade;
        return Wav(b);
    }

    /// <summary>배경: 낮게 깔리는 소리 위에 다섯음 음계의 현을 드문드문 뜯는다. 끝과 처음이 이어진다.</summary>
    static float[] Music(ref uint seed)
    {
        const float len = 48f;
        var b = Buf(len);
        int n = b.Length;
        // 깔리는 소리: A2와 E3, 천천히 숨 쉬듯.
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float breath = 0.6f + 0.4f * MathF.Sin(Mathf.Tau * t / len * 3);
            b[i] += 0.05f * breath * (MathF.Sin(Mathf.Tau * 110 * t) + 0.6f * MathF.Sin(Mathf.Tau * 164.81f * t) + 0.25f * MathF.Sin(Mathf.Tau * 220 * t));
        }
        float[] scale = { 220, 261.63f, 293.66f, 329.63f, 392, 440, 523.25f, 587.33f };
        float at = 0.8f;
        int idx = 3;
        while (at < len - 4)
        {
            idx = Math.Clamp(idx + (int)MathF.Round(Rnd(ref seed) * 2.2f), 0, scale.Length - 1);
            Pluck(b, at, scale[idx], 0.16f, ref seed, 0.997f);
            if (Rnd(ref seed) > 0.55f) Pluck(b, at + 0.01f, scale[Math.Max(0, idx - 3)] / 2, 0.1f, ref seed, 0.997f);
            at += Rnd(ref seed) > 0.3f ? 1.2f + 0.6f * MathF.Abs(Rnd(ref seed)) : 3.2f;
        }
        // 넓은 방의 울림: 고리 모양으로 두 번 돌려 끝이 처음으로 넘어가게 한다.
        foreach (int d in new[] { (int)(0.37f * Rate), (int)(0.53f * Rate) })
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                    b[i] += b[(i - d + n) % n] * 0.28f;
        float peak = b.Max(MathF.Abs);
        if (peak > 0.6f) for (int i = 0; i < n; i++) b[i] *= 0.6f / peak;
        return b;
    }
}
