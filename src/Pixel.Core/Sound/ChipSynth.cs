using Pixel.Core.Behaviour;

namespace Pixel.Core.Sound;

public enum SoundKind { Meow, Poof }

/// <summary>Which sound goes with what the cat is doing (null: stay quiet).</summary>
public static class CatSounds
{
    public static SoundKind? For(Activity activity) => activity switch
    {
        Activity.Purr => SoundKind.Meow, // petting: a happy meow
        Activity.Alert or Activity.AgentNeedsYou => SoundKind.Meow,
        Activity.PoofOut or Activity.PoofIn => SoundKind.Poof,
        _ => null,
    };
}

/// <summary>
/// Tiny synthesiser for Pixel's sounds — a meow and a poof — rendered to 16-bit mono WAV in memory.
/// Original sounds, no audio files to ship.
/// </summary>
public static class ChipSynth
{
    private const int Rate = 22050;

    public static byte[] Render(SoundKind kind, double volume)
    {
        var samples = kind switch
        {
            SoundKind.Meow => Meow(),
            _ => Poof(),
        };
        var gain = Math.Clamp(volume, 0, 1) * 0.8;
        return ToWav(samples, gain);
    }

    /// <summary>Mouth shape over the meow (fraction of its length → first two mouth resonances, Hz): m → ee → ah → ooo.</summary>
    private static readonly (double T, double F1, double F2)[] MeowMouth =
        [(0, 300, 1100), (0.12, 450, 2100), (0.35, 850, 1500), (0.65, 650, 1050), (1, 400, 750)];

    /// <summary>
    /// "mee-ow-www": a buzzy voice (a band-limited sawtooth) shaped by two mouth resonances that glide like a cat
    /// opening and closing its mouth — m → ee → ah → ooo — with the pitch rising then falling, a little vibrato
    /// that grows into a long soft tail.
    /// </summary>
    private static float[] Meow()
    {
        var n = (int)(Rate * 0.9);
        var s = new float[n];
        var low = new Resonator(110);
        var high = new Resonator(170);
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var sec = (double)i / Rate;
            var pitch = t < 0.25 ? Lerp(560, 780, t / 0.25)
                : t < 0.7 ? Lerp(780, 560, (t - 0.25) / 0.45)
                : Lerp(560, 470, (t - 0.7) / 0.3);
            pitch *= 1 + (0.008 + 0.02 * t) * Math.Sin(2 * Math.PI * 5.5 * sec);
            phase += pitch / Rate;
            phase -= Math.Floor(phase);

            double voice = 0;
            for (var k = 1; k <= 16 && k * pitch < Rate / 2.0; k++) voice += Math.Sin(2 * Math.PI * k * phase) / k;

            var (f1, f2) = MouthAt(t);
            var v = low.Next(voice, f1) + 0.7 * high.Next(voice, f2);
            var lips = t < 0.1 ? Lerp(0.3, 1, t / 0.1) : 1; // the "m": lips still closed, so quieter
            var env = Math.Min(1, sec / 0.03) * lips * (t > 0.45 ? Math.Pow(1 - (t - 0.45) / 0.55, 1.3) : 1);
            s[i] = (float)(v * env);
        }
        Normalize(s, 0.9);
        return s;
    }

    private static (double F1, double F2) MouthAt(double t)
    {
        for (var k = 1; k < MeowMouth.Length; k++)
        {
            var (t1, a1, b1) = MeowMouth[k];
            if (t > t1) continue;
            var (t0, a0, b0) = MeowMouth[k - 1];
            var u = (t - t0) / (t1 - t0);
            return (Lerp(a0, a1, u), Lerp(b0, b1, u));
        }
        return (MeowMouth[^1].F1, MeowMouth[^1].F2);
    }

    /// <summary>
    /// Two-pole resonant filter: one "formant" of the mouth, retunable every sample. Scaled to the same peak gain
    /// at any frequency, so the high "ee" rings as strongly as the low "ow" (unscaled, low notes boom and it sounds muffled).
    /// </summary>
    private sealed class Resonator(double bandwidth)
    {
        private double _y1, _y2;

        public double Next(double x, double frequency)
        {
            var r = Math.Exp(-Math.PI * bandwidth / Rate);
            var theta = 2 * Math.PI * frequency / Rate;
            var y = (1 - r) * 2 * Math.Sin(theta) * x + 2 * r * Math.Cos(theta) * _y1 - r * r * _y2;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }

    private static void Normalize(float[] s, double peak)
    {
        var max = s.Max(Math.Abs);
        if (max <= 0) return;
        var k = (float)(peak / max);
        for (var i = 0; i < s.Length; i++) s[i] *= k;
    }

    /// <summary>A short "pff": decaying noise with a falling tone underneath.</summary>
    private static float[] Poof()
    {
        var n = (int)(Rate * 0.25);
        var s = new float[n];
        var rng = new Random(3);
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            phase += Lerp(300, 90, t) / Rate;
            var env = Math.Exp(-5 * t) * Math.Min(1, t / 0.02);
            s[i] = (float)(((rng.NextDouble() * 2 - 1) * 0.7 + 0.3 * Math.Sin(2 * Math.PI * phase)) * env);
        }
        return s;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    private static byte[] ToWav(float[] samples, double gain)
    {
        var data = samples.Length * 2;
        var wav = new byte[44 + data];
        void Ascii(int at, string s) { for (var i = 0; i < 4; i++) wav[at + i] = (byte)s[i]; }
        Ascii(0, "RIFF");
        BitConverter.TryWriteBytes(wav.AsSpan(4), 36 + data);
        Ascii(8, "WAVE");
        Ascii(12, "fmt ");
        BitConverter.TryWriteBytes(wav.AsSpan(16), 16);           // PCM chunk size
        BitConverter.TryWriteBytes(wav.AsSpan(20), (short)1);     // PCM
        BitConverter.TryWriteBytes(wav.AsSpan(22), (short)1);     // mono
        BitConverter.TryWriteBytes(wav.AsSpan(24), Rate);
        BitConverter.TryWriteBytes(wav.AsSpan(28), Rate * 2);     // byte rate
        BitConverter.TryWriteBytes(wav.AsSpan(32), (short)2);     // block align
        BitConverter.TryWriteBytes(wav.AsSpan(34), (short)16);    // bits per sample
        Ascii(36, "data");
        BitConverter.TryWriteBytes(wav.AsSpan(40), data);
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (short)Math.Clamp(Math.Round(samples[i] * gain * short.MaxValue), short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(wav.AsSpan(44 + i * 2), v);
        }
        return wav;
    }
}
