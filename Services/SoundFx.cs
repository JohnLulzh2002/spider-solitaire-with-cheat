using System.IO;
using System.Media;

namespace SpiderSolitaireWithCheat.Services;

/// <summary>
/// 程序化合成的 16-bit WAV 音效（无外部资源文件），预加载后即时播放。
/// </summary>
public static class SoundFx
{
    private const int Sr = 44100;

    private static readonly Dictionary<string, SoundPlayer> Players = new();
    private static readonly Random Rng = new();

    public static bool Enabled { get; set; } = true;

    public static void PreloadAll()
    {
        Load("place", BuildPlace());
        Load("flip", BuildFlip());
        Load("deal", BuildDeal());
        Load("complete", BuildComplete());
        Load("undo", BuildUndo());
        Load("error", BuildError());
        Load("cheat", BuildCheat());
        Load("win", BuildWin());
    }

    public static void Play(string name)
    {
        if (!Enabled || !Players.TryGetValue(name, out var player))
        {
            return;
        }

        try
        {
            player.Play();
        }
        catch
        {
            // 播放失败不影响游戏
        }
    }

    private static void Load(string name, byte[] wav)
    {
        var player = new SoundPlayer(new MemoryStream(wav));
        try
        {
            player.Load();
        }
        catch
        {
            // 预载失败时播放阶段会静默忽略
        }

        Players[name] = player;
    }

    // ---------- 合成 ----------

    private static double[] Silence(double seconds) => new double[(int)(Sr * seconds)];

    /// <summary>向缓冲区叠加一个带指数衰减的正弦音。</summary>
    private static void AddTone(double[] buf, double startSec, double durSec, double freq, double vol, double decayPerSec)
    {
        int start = (int)(startSec * Sr);
        int n = (int)(durSec * Sr);
        for (int i = 0; i < n && start + i < buf.Length; i++)
        {
            double t = i / (double)Sr;
            buf[start + i] += Math.Sin(2 * Math.PI * freq * t) * vol * Math.Exp(-t * decayPerSec);
        }
    }

    /// <summary>向缓冲区叠加一段低通噪声（用于卡牌拍落/呼啸声）。</summary>
    private static void AddNoise(double[] buf, double startSec, double durSec, double vol, double decayPerSec, double lowpassAlpha)
    {
        int start = (int)(startSec * Sr);
        int n = (int)(durSec * Sr);
        double y = 0;
        for (int i = 0; i < n && start + i < buf.Length; i++)
        {
            double t = i / (double)Sr;
            double x = Rng.NextDouble() * 2 - 1;
            y += lowpassAlpha * (x - y);
            buf[start + i] += y * vol * Math.Exp(-t * decayPerSec);
        }
    }

    private static byte[] ToWav(double[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);   // PCM
        w.Write((short)1);   // 单声道
        w.Write(Sr);
        w.Write(Sr * 2);     // 字节率
        w.Write((short)2);   // 块对齐
        w.Write((short)16);  // 位深
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples)
        {
            w.Write((short)(Math.Clamp(s, -1.0, 1.0) * short.MaxValue));
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>落牌：低频闷响 + 短噪声拍击。</summary>
    private static byte[] BuildPlace()
    {
        var buf = Silence(0.09);
        AddNoise(buf, 0, 0.08, 0.55, 55, 0.18);
        AddTone(buf, 0, 0.06, 170, 0.35, 70);
        return ToWav(buf);
    }

    /// <summary>翻牌：更亮更短的噪声。</summary>
    private static byte[] BuildFlip()
    {
        var buf = Silence(0.07);
        AddNoise(buf, 0, 0.06, 0.4, 90, 0.45);
        return ToWav(buf);
    }

    /// <summary>发牌：先起后落的呼啸声。</summary>
    private static byte[] BuildDeal()
    {
        var buf = Silence(0.35);
        int n = buf.Length;
        double y = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Sr;
            double env = Math.Sin(Math.PI * t / 0.35);
            double alpha = 0.05 + 0.35 * env;
            double x = Rng.NextDouble() * 2 - 1;
            y += alpha * (x - y);
            buf[i] += y * env * 0.5;
        }

        AddTone(buf, 0.28, 0.07, 320, 0.18, 60);
        return ToWav(buf);
    }

    /// <summary>完成序列：上行双音钟声。</summary>
    private static byte[] BuildComplete()
    {
        var buf = Silence(0.65);
        AddTone(buf, 0.00, 0.5, 880.0, 0.35, 7);
        AddTone(buf, 0.13, 0.5, 1318.5, 0.35, 6);
        return ToWav(buf);
    }

    /// <summary>撤销：下行双音。</summary>
    private static byte[] BuildUndo()
    {
        var buf = Silence(0.16);
        AddTone(buf, 0.00, 0.14, 420, 0.3, 12);
        AddTone(buf, 0.05, 0.11, 300, 0.3, 14);
        return ToWav(buf);
    }

    /// <summary>错误：低频拍频嗡声。</summary>
    private static byte[] BuildError()
    {
        var buf = Silence(0.18);
        AddTone(buf, 0, 0.16, 140, 0.4, 10);
        AddTone(buf, 0, 0.16, 133, 0.3, 10);
        return ToWav(buf);
    }

    /// <summary>作弊：神秘上行双音。</summary>
    private static byte[] BuildCheat()
    {
        var buf = Silence(0.25);
        AddTone(buf, 0.00, 0.10, 620, 0.28, 16);
        AddTone(buf, 0.09, 0.14, 930, 0.28, 14);
        return ToWav(buf);
    }

    /// <summary>胜利：C 大调琶音小号角。</summary>
    private static byte[] BuildWin()
    {
        var buf = Silence(1.25);
        (double freq, double start)[] notes =
        {
            (523.25, 0.00),
            (659.25, 0.15),
            (783.99, 0.30),
            (1046.50, 0.45),
        };
        foreach (var (freq, start) in notes)
        {
            AddTone(buf, start, 0.7, freq, 0.3, 4.5);
            AddTone(buf, start, 0.7, freq * 2, 0.12, 6);
        }

        AddTone(buf, 0.45, 0.8, 261.63, 0.18, 3);
        return ToWav(buf);
    }
}
