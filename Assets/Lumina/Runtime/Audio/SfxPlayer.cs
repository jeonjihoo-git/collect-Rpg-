using System.Collections.Generic;
using Lumina.Core.Audio;
using Lumina.Core.Data;
using UnityEngine;

namespace Lumina.Runtime.Audio
{
    /// <summary>
    /// SfxRequest → AudioClip(절차 합성 캐시) → AudioSource 풀 재생.
    /// 같은 키는 2개 변형을 번갈아 사용하고 ±3% 피치 흔들림으로 기계적 반복감을 줄인다.
    /// 정식 음원이 들어오면 Render 대신 에셋 매핑을 반환하도록 이 클래스만 교체하면 된다.
    /// </summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        private const int Voices = 24;
        private const int Variants = 2;

        public float masterVolume = 0.9f;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, int> _rr = new Dictionary<string, int>();
        private AudioSource[] _voices;
        private float[] _startedAt;
        private int _next;

        private void Awake()
        {
            _voices = new AudioSource[Voices];
            _startedAt = new float[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.dopplerLevel = 0f;
                _voices[i] = src;
            }
        }

        public void Play(SfxRequest r)
        {
            string key = r.Key;
            _rr.TryGetValue(key, out int v);
            _rr[key] = (v + 1) % Variants;
            var clip = Clip(r, v);
            if (clip == null) return;
            var src = PickVoice();
            src.clip = clip;
            src.volume = Mathf.Clamp01((r.volume <= 0f ? 1f : r.volume) * masterVolume);
            src.pitch = r.isSystem ? 1f : 1f + Random.Range(-0.03f, 0.03f);
            src.panStereo = r.pan;
            src.Play();
        }

        public void PlaySystem(SystemSfx s, float volume = 1f)
        {
            Play(new SfxRequest { isSystem = true, system = s, volume = volume });
        }

        public void PlayAll(List<SfxRequest> reqs)
        {
            foreach (var r in reqs) Play(r);
        }

        private AudioSource PickVoice()
        {
            for (int i = 0; i < Voices; i++)
            {
                int idx = (_next + i) % Voices;
                if (!_voices[idx].isPlaying)
                {
                    _next = (idx + 1) % Voices;
                    _startedAt[idx] = Time.unscaledTime;
                    return _voices[idx];
                }
            }
            // 모두 재생 중이면 가장 오래된 음 교체
            int oldest = 0;
            for (int i = 1; i < Voices; i++)
            {
                if (_startedAt[i] < _startedAt[oldest]) oldest = i;
            }
            _startedAt[oldest] = Time.unscaledTime;
            _voices[oldest].Stop();
            return _voices[oldest];
        }

        private AudioClip Clip(SfxRequest r, int variant)
        {
            string key = r.Key + "#" + variant;
            if (_clips.TryGetValue(key, out var c)) return c;
            float[] data = r.Render(variant);
            if (data == null || data.Length == 0) return null;
            c = AudioClip.Create(key, data.Length, 1, Dsp.SampleRate, false);
            c.SetData(data, 0);
            _clips[key] = c;
            return c;
        }

        /// <summary>전투 시작 전 파티 무기 효과음 미리 합성(전투 중 끊김 방지).</summary>
        public void Prewarm(IEnumerable<CharacterDef> party, GameDatabase db)
        {
            foreach (var c in party)
            {
                var w = db.GetWeapon(c.weapon);
                var cat = WeaponInfo.Category(c.weapon);
                var tiers = new HashSet<SfxTier> { SfxTier.Skill, SfxTier.Ultimate };
                foreach (var s in w.combo) tiers.Add(s.sfxTier);
                foreach (var t in tiers)
                {
                    Element el = t == SfxTier.Skill || t == SfxTier.Ultimate || cat == WeaponCategory.Magic ? c.element : Element.None;
                    if (cat == WeaponCategory.Melee) Clip(new SfxRequest { weapon = c.weapon, kind = SfxKind.Swing, tier = t }, 0);
                    else Clip(new SfxRequest { weapon = c.weapon, kind = SfxKind.Projectile, tier = t, element = el }, 0);
                    Clip(new SfxRequest { weapon = c.weapon, kind = SfxKind.Impact, tier = t, element = el }, 0);
                    if (SfxRouter.HasTail(t)) Clip(new SfxRequest { weapon = c.weapon, kind = SfxKind.Tail, tier = t }, 0);
                }
            }
        }

        public int CachedClipCount => _clips.Count;
    }
}
