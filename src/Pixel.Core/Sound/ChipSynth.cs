namespace Pixel.Core.Sound;

public enum SoundKind { Meow, Purr, Poof }

/// <summary>
/// Tiny chiptune synthesiser for Pixel's sounds — an 8-bit-style meow, a soft purr and a poof — rendered to
/// 16-bit mono WAV in memory. Original sounds, no audio files to ship.
/// </summary>
public static class ChipSynth
{
    private const int Rate = 22050;

    public static byte[] Render(SoundKind kind, double volume)
    {
        var samples = kind switch
        {
            SoundKind.Meow => Meow(),
            SoundKind.Purr => Purr(),
            _ => Poof(),
        };
        var gain = Math.Clamp(volume, 0, 1) * 0.8;
        return ToWav(samples, gain);
    }

    /// <summary>"mi-AOW": a rising-then-falling glide on a triangle/square mix with a little vibrato.</summary>
    private static float[] Meow()
    {
        var n = (int)(Rate * 0.45);
        var s = new float[n];
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var pitch = t < 0.3 ? Lerp(520, 820, t / 0.3) : Lerp(820, 430, (t - 0.3) / 0.7);
            pitch *= 1 + 0.02 * Math.Sin(2 * Math.PI * 6 * i / Rate);
            phase += pitch / Rate;
            var frac = phase - Math.Floor(phase);
            var triangle = 4 * Math.Abs(frac - 0.5) - 1;
            var square = frac < 0.5 ? 1 : -1;
            var env = Math.Min(1, t / 0.07) * Math.Pow(1 - t, 0.6) * (t < 0.1 ? 0.6 : 1);
            s[i] = (float)((0.75 * triangle + 0.25 * square) * env);
        }
        return s;
    }

    /// <summary>A low rumble pulsing ~25 times a second, swelling over two breaths.</summary>
    private static float[] Purr()
    {
        var n = (int)(Rate * 1.4);
        var s = new float[n];
        var rng = new Random(7);
        double lp = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Rate;
            lp += (rng.NextDouble() * 2 - 1 - lp) * 0.08; // soft low-passed noise
            var tone = Math.Sin(2 * Math.PI * 55 * t);
            var pulse = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 25 * t);
            var breath = Math.Sin(Math.PI * (t % 0.7) / 0.7);
            s[i] = (float)((0.6 * tone + 0.8 * lp) * pulse * breath * 0.7);
        }
        return s;
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
