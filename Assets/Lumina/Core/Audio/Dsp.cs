using System;
using Lumina.Core.Util;

namespace Lumina.Core.Audio
{
    /// <summary>
    /// 절차적 효과음용 최소 DSP. 모든 레이어는 버퍼에 '더하기'로 합성한다(모노, 44.1kHz).
    /// 정식 음원(녹음/디자인된 SFX)으로 교체하기 전까지의 플레이스홀더 생성기.
    /// </summary>
    public static class Dsp
    {
        public const int SampleRate = 44100;
        private const float TwoPi = MathF.PI * 2f;

        public static int N(float seconds) => Math.Max(1, (int)(seconds * SampleRate));

        public struct Biquad
        {
            private float _b0, _b1, _b2, _a1, _a2, _z1, _z2;

            public static Biquad LowPass(float f, float q)
            {
                var b = new Biquad();
                b.SetLowPass(f, q);
                return b;
            }

            public static Biquad HighPass(float f, float q)
            {
                var b = new Biquad();
                b.SetHighPass(f, q);
                return b;
            }

            public static Biquad BandPass(float f, float q)
            {
                var b = new Biquad();
                b.SetBandPass(f, q);
                return b;
            }

            private static void Common(float f, float q, out float w0, out float alpha, out float cos)
            {
                f = Math.Clamp(f, 10f, SampleRate * 0.45f);
                w0 = TwoPi * f / SampleRate;
                cos = MathF.Cos(w0);
                alpha = MathF.Sin(w0) / (2f * MathF.Max(0.05f, q));
            }

            public void SetLowPass(float f, float q)
            {
                Common(f, q, out _, out float alpha, out float cos);
                float a0 = 1f + alpha;
                _b0 = (1f - cos) / 2f / a0;
                _b1 = (1f - cos) / a0;
                _b2 = _b0;
                _a1 = -2f * cos / a0;
                _a2 = (1f - alpha) / a0;
            }

            public void SetHighPass(float f, float q)
            {
                Common(f, q, out _, out float alpha, out float cos);
                float a0 = 1f + alpha;
                _b0 = (1f + cos) / 2f / a0;
                _b1 = -(1f + cos) / a0;
                _b2 = _b0;
                _a1 = -2f * cos / a0;
                _a2 = (1f - alpha) / a0;
            }

            public void SetBandPass(float f, float q)
            {
                Common(f, q, out _, out float alpha, out float cos);
                float a0 = 1f + alpha;
                _b0 = alpha / a0;
                _b1 = 0f;
                _b2 = -alpha / a0;
                _a1 = -2f * cos / a0;
                _a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = _b0 * x + _z1;
                _z1 = _b1 * x - _a1 * y + _z2;
                _z2 = _b2 * x - _a2 * y;
                return y;
            }
        }

        public static float AttackDecay(float t, float attack, float tau)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / MathF.Max(attack, 1e-5f);
            return MathF.Exp(-(t - attack) / MathF.Max(tau, 1e-5f));
        }

        private static int Start(float startSec) => Math.Max(0, (int)(startSec * SampleRate));

        private static float[] Grow(float[] buf, int needed)
        {
            if (needed <= buf.Length) return buf;
            var nb = new float[needed];
            Array.Copy(buf, nb, buf.Length);
            return nb;
        }

        /// <summary>대역 통과 노이즈 휘두름. 중심 주파수 f0→f1 지수 스윕, peakFrac 지점에서 최대.</summary>
        public static float[] Whoosh(float[] buf, float start, float dur, float peakFrac, float f0, float f1, float q, float gain, DeterministicRng rng, float fMid = 0f)
        {
            int s0 = Start(start);
            int n = N(dur);
            buf = Grow(buf, s0 + n);
            var bp = Biquad.BandPass(f0, q);
            var lp = Biquad.LowPass(MathF.Max(f0, f1) * 1.5f, 0.7f);
            float tp = Math.Clamp(peakFrac, 0.05f, 0.95f) * dur;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                if ((i & 15) == 0)
                {
                    float u = t / dur;
                    float f;
                    if (fMid > 0f) f = u < 0.5f ? f0 * MathF.Pow(fMid / f0, u * 2f) : fMid * MathF.Pow(f1 / fMid, (u - 0.5f) * 2f);
                    else f = f0 * MathF.Pow(f1 / f0, u);
                    bp.SetBandPass(f, q);
                }
                float env = t < tp ? MathF.Pow(t / tp, 2f) : MathF.Pow(MathF.Max(0f, 1f - (t - tp) / (dur - tp)), 1.6f);
                float flutter = 1f + 0.15f * MathF.Sin(TwoPi * 23f * t + rng.NextFloat() * 0.1f);
                float x = lp.Process(bp.Process(rng.NextSigned())) * 3.2f;
                buf[s0 + i] += x * env * flutter * gain;
            }
            return buf;
        }

        /// <summary>몸체 충격(피치가 떨어지는 사인).</summary>
        public static float[] Thump(float[] buf, float start, float f0, float f1, float sweepTau, float decayTau, float gain)
        {
            int s0 = Start(start);
            int n = N(decayTau * 7f + 0.004f);
            buf = Grow(buf, s0 + n);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float f = f1 + (f0 - f1) * MathF.Exp(-t / MathF.Max(sweepTau, 1e-4f));
                phase += TwoPi * f / SampleRate;
                float env = AttackDecay(t, 0.0015f, decayTau);
                buf[s0 + i] += MathF.Sin(phase) * env * gain;
            }
            return buf;
        }

        /// <summary>필터된 노이즈 타격(칼날 절삭, 슬랩, 크랙 등 과도음).</summary>
        public static float[] NoiseHit(float[] buf, float start, float attack, float tau, float hp, float lp, float gain, DeterministicRng rng)
        {
            int s0 = Start(start);
            int n = N(attack + tau * 7f);
            buf = Grow(buf, s0 + n);
            var h = Biquad.HighPass(hp, 0.7f);
            var l = Biquad.LowPass(lp, 0.7f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float x = l.Process(h.Process(rng.NextSigned()));
                buf[s0 + i] += x * AttackDecay(t, attack, tau) * gain * 1.6f;
            }
            return buf;
        }

        /// <summary>짧은 비조화 금속 성분(감쇠가 매우 빠름 — 징/종처럼 길게 울리지 않음).</summary>
        public static float[] Metal(float[] buf, float start, float baseF, float[] ratios, float[] taus, float gain)
        {
            int s0 = Start(start);
            float maxTau = 0f;
            foreach (float t in taus) maxTau = MathF.Max(maxTau, t);
            int n = N(maxTau * 6f);
            buf = Grow(buf, s0 + n);
            for (int k = 0; k < ratios.Length; k++)
            {
                float f = baseF * ratios[k];
                float tau = taus[Math.Min(k, taus.Length - 1)];
                float amp = gain / (1f + k * 0.6f);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate;
                    buf[s0 + i] += MathF.Sin(TwoPi * f * t) * AttackDecay(t, 0.0008f, tau) * amp;
                }
            }
            return buf;
        }

        /// <summary>Karplus–Strong 현 튕김(활/석궁 시위).</summary>
        public static float[] Pluck(float[] buf, float start, float freq, float dur, float damping, float gain, DeterministicRng rng)
        {
            int s0 = Start(start);
            int n = N(dur);
            buf = Grow(buf, s0 + n);
            int len = Math.Max(2, (int)(SampleRate / freq));
            var line = new float[len];
            var lp = Biquad.LowPass(freq * 12f, 0.7f);
            for (int i = 0; i < len; i++) line[i] = lp.Process(rng.NextSigned());
            int idx = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                int nxt = (idx + 1) % len;
                float y = line[idx];
                line[idx] = damping * 0.5f * (line[idx] + line[nxt]);
                idx = nxt;
                buf[s0 + i] += y * gain * MathF.Exp(-t / (dur * 0.4f));
            }
            return buf;
        }

        /// <summary>기계식 틱(장전/격발 부품).</summary>
        public static float[] Click(float[] buf, float start, float freq, float tau, float gain)
        {
            return Thump(buf, start, freq, freq, 1f, tau, gain);
        }

        /// <summary>무작위 임펄스 지글거림(불꽃, 파편, 종이).</summary>
        public static float[] Crackle(float[] buf, float start, float dur, float density, float tau, float hp, float gain, DeterministicRng rng)
        {
            int s0 = Start(start);
            int n = N(dur + 0.01f);
            buf = Grow(buf, s0 + n);
            var h = Biquad.HighPass(hp, 0.7f);
            float p = density / SampleRate;
            float burst = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                if (rng.NextFloat() < p) burst = 0.6f + 0.4f * rng.NextFloat();
                burst *= 0.93f;
                float x = h.Process(rng.NextSigned() * burst);
                buf[s0 + i] += x * MathF.Exp(-t / MathF.Max(tau, 1e-4f)) * gain * 2f;
            }
            return buf;
        }

        public enum Wave
        {
            Sine,
            Saw,
            Square,
            Triangle
        }

        /// <summary>스윕 톤(마법 충전, 경고음, UI). 톱니/사각은 저역통과로 거칠기 조절.</summary>
        public static float[] Tone(float[] buf, float start, float dur, float f0, float f1, Wave wave, float vibHz, float vibDepth,
            float attack, float release, float gain, float lowpass = 0f, float amHz = 0f, float amDepth = 0f)
        {
            int s0 = Start(start);
            int n = N(dur);
            buf = Grow(buf, s0 + n);
            var lp = Biquad.LowPass(lowpass > 0f ? lowpass : 18000f, 0.7f);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float u = t / dur;
                float f = f0 * MathF.Pow(f1 / f0, u);
                f *= 1f + vibDepth * MathF.Sin(TwoPi * vibHz * t);
                phase += f / SampleRate;
                phase -= MathF.Floor(phase);
                float x;
                switch (wave)
                {
                    case Wave.Saw: x = 2f * phase - 1f; break;
                    case Wave.Square: x = phase < 0.5f ? 1f : -1f; break;
                    case Wave.Triangle: x = 1f - 4f * MathF.Abs(phase - 0.5f); break;
                    default: x = MathF.Sin(TwoPi * phase); break;
                }
                x = lp.Process(x);
                float env = t < attack ? t / MathF.Max(attack, 1e-4f) : 1f;
                float rel = dur - t;
                if (rel < release) env *= MathF.Max(0f, rel / MathF.Max(release, 1e-4f));
                if (amHz > 0f) env *= 1f - amDepth * (0.5f + 0.5f * MathF.Sin(TwoPi * amHz * t));
                buf[s0 + i] += x * env * gain;
            }
            return buf;
        }

        /// <summary>저역 노이즈 굉음(폭발 잔향, 지면 진동).</summary>
        public static float[] Rumble(float[] buf, float start, float dur, float cutoff, float attack, float tau, float gain, DeterministicRng rng)
        {
            int s0 = Start(start);
            int n = N(dur);
            buf = Grow(buf, s0 + n);
            var l1 = Biquad.LowPass(cutoff, 0.7f);
            var l2 = Biquad.LowPass(cutoff, 0.7f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float x = l2.Process(l1.Process(rng.NextSigned()));
                buf[s0 + i] += x * AttackDecay(t, attack, tau) * gain * 4f;
            }
            return buf;
        }

        /// <summary>전기 지직임(번개).</summary>
        public static float[] Zap(float[] buf, float start, float dur, float baseF, float gain, DeterministicRng rng)
        {
            int s0 = Start(start);
            int n = N(dur);
            buf = Grow(buf, s0 + n);
            var hp = Biquad.HighPass(600f, 0.7f);
            float phase = 0f;
            float f = baseF;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                if (i % 220 == 0) f = baseF * (0.6f + 0.9f * rng.NextFloat());
                phase += f / SampleRate;
                phase -= MathF.Floor(phase);
                float saw = 2f * phase - 1f;
                float x = hp.Process(saw * 0.7f + rng.NextSigned() * 0.5f);
                float gate = rng.NextFloat() < 0.85f ? 1f : 0.2f;
                buf[s0 + i] += x * gate * AttackDecay(t, 0.002f, dur * 0.35f) * gain;
            }
            return buf;
        }

        /// <summary>물 튀김: 대역 노이즈 + 짧은 상승 처프(거품).</summary>
        public static float[] Splash(float[] buf, float start, float dur, float gain, DeterministicRng rng)
        {
            buf = NoiseHit(buf, start, 0.002f, dur * 0.25f, 700f, 3500f, gain * 0.8f, rng);
            for (int k = 0; k < 5; k++)
            {
                float s = start + rng.Range(0.005f, dur * 0.6f);
                float f = rng.Range(450f, 900f);
                buf = Tone(buf, s, 0.025f, f, f * 2.4f, Wave.Sine, 0f, 0f, 0.002f, 0.01f, gain * 0.35f);
            }
            return buf;
        }

        /// <summary>종이/잎 바스락: 짧은 노이즈 그레인 연속.</summary>
        public static float[] Rustle(float[] buf, float start, float dur, float center, float grainsPerSec, float gain, DeterministicRng rng)
        {
            int count = Math.Max(1, (int)(dur * grainsPerSec));
            for (int k = 0; k < count; k++)
            {
                float s = start + dur * k / count + rng.Range(0f, 0.01f);
                buf = NoiseHit(buf, s, 0.001f, rng.Range(0.002f, 0.007f), center * 0.6f, center * 1.6f, gain * rng.Range(0.5f, 1f), rng);
            }
            return buf;
        }

        /// <summary>마무리: DC 제거, 소프트 클립, 목표 피크 정규화, 끝 페이드, 꼬리 무음 제거.</summary>
        public static float[] Finish(float[] buf, float peak)
        {
            var dc = Biquad.HighPass(28f, 0.7f);
            float max = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float x = dc.Process(buf[i]);
                x = MathF.Tanh(x * 1.1f);
                buf[i] = x;
                max = MathF.Max(max, MathF.Abs(x));
            }
            if (max < 1e-6f) return buf;
            float g = peak / max;
            int last = 0;
            float thresh = peak * 0.0012f;
            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] *= g;
                if (MathF.Abs(buf[i]) > thresh) last = i;
            }
            int len = Math.Min(buf.Length, last + N(0.012f));
            var outBuf = new float[Math.Max(len, 1)];
            Array.Copy(buf, outBuf, outBuf.Length);
            int fade = Math.Min(outBuf.Length, N(0.01f));
            for (int i = 0; i < fade; i++) outBuf[outBuf.Length - 1 - i] *= i / (float)fade;
            return outBuf;
        }
    }
}
