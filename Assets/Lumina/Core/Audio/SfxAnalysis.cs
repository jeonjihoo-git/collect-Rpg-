using System;
using System.IO;

namespace Lumina.Core.Audio
{
    /// <summary>16비트 PCM WAV 저장(청음용 미리듣기 파일 생성).</summary>
    public static class WavWriter
    {
        public static void Write(string path, float[] samples, int sampleRate = Dsp.SampleRate)
        {
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                int dataBytes = samples.Length * 2;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(sampleRate);
                w.Write(sampleRate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(dataBytes);
                foreach (float s in samples)
                {
                    float c = Math.Clamp(s, -1f, 1f);
                    w.Write((short)(c * 32767f));
                }
            }
        }
    }

    /// <summary>
    /// 효과음 특징량(자동 회귀검사용). 사람 청음 평가를 대체하지 않으며,
    /// "모든 무기에 같은 소리를 재사용"하는 퇴행을 기계적으로 잡아내는 최소 장치다.
    /// </summary>
    public struct SfxFeatures
    {
        public float durationSec;
        public float attackSec;
        public float centroidHz;
        public float lowRatio;
        public float midRatio;
        public float highRatio;
        public float rms;

        public float[] Vector => new[]
        {
            MathF.Log(MathF.Max(durationSec, 0.005f)),
            MathF.Log(MathF.Max(centroidHz, 20f)),
            lowRatio * 3f,
            midRatio * 3f,
            highRatio * 3f,
            MathF.Log(MathF.Max(attackSec, 0.0005f)) * 0.5f
        };

        public static float Distance(SfxFeatures a, SfxFeatures b)
        {
            var va = a.Vector;
            var vb = b.Vector;
            float s = 0f;
            for (int i = 0; i < va.Length; i++) s += (va[i] - vb[i]) * (va[i] - vb[i]);
            return MathF.Sqrt(s);
        }

        public static SfxFeatures Compute(float[] x, int sampleRate = Dsp.SampleRate)
        {
            var f = new SfxFeatures();
            if (x == null || x.Length == 0) return f;
            float peak = 0f;
            int peakIdx = 0;
            double sumSq = 0;
            for (int i = 0; i < x.Length; i++)
            {
                float a = MathF.Abs(x[i]);
                sumSq += x[i] * x[i];
                if (a > peak)
                {
                    peak = a;
                    peakIdx = i;
                }
            }
            f.rms = (float)Math.Sqrt(sumSq / x.Length);
            f.attackSec = peakIdx / (float)sampleRate;
            float thr = peak * 0.01f;
            int last = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (MathF.Abs(x[i]) > thr) last = i;
            }
            f.durationSec = last / (float)sampleRate;

            const int frame = 1024;
            var mag = new double[frame / 2];
            int frames = 0;
            for (int start = 0; start + frame <= Math.Max(x.Length, frame); start += frame / 2)
            {
                var re = new double[frame];
                var im = new double[frame];
                for (int i = 0; i < frame; i++)
                {
                    int idx = start + i;
                    double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (frame - 1));
                    re[i] = idx < x.Length ? x[idx] * w : 0.0;
                }
                Fft(re, im);
                for (int k = 0; k < frame / 2; k++) mag[k] += Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                frames++;
                if (start + frame >= x.Length) break;
            }
            double total = 0, weighted = 0, low = 0, mid = 0, high = 0;
            for (int k = 1; k < frame / 2; k++)
            {
                double hz = k * (double)sampleRate / frame;
                double m = mag[k];
                total += m;
                weighted += m * hz;
                if (hz < 250) low += m;
                else if (hz < 2000) mid += m;
                else high += m;
            }
            if (total > 0)
            {
                f.centroidHz = (float)(weighted / total);
                f.lowRatio = (float)(low / total);
                f.midRatio = (float)(mid / total);
                f.highRatio = (float)(high / total);
            }
            return f;
        }

        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci;
                        double ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }

        public override string ToString() =>
            $"dur {durationSec * 1000:0}ms, atk {attackSec * 1000:0.0}ms, centroid {centroidHz:0}Hz, L/M/H {lowRatio:0.00}/{midRatio:0.00}/{highRatio:0.00}";
    }
}
