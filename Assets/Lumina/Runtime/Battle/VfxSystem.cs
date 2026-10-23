using Lumina.Core.Data;
using UnityEngine;

namespace Lumina.Runtime.Battle
{
    /// <summary>수명 동안 크기·알파를 보간하고 스스로 파괴되는 단발 이펙트.</summary>
    public sealed class FadeFx : MonoBehaviour
    {
        public float life = 0.2f;
        public Vector3 scaleFrom = Vector3.one;
        public Vector3 scaleTo = Vector3.one;
        public Color color = Color.white;
        public Material mat;
        public LineRenderer line;
        public bool billboard;
        public float spinDeg;
        private float _t;

        private void Update()
        {
            _t += Time.deltaTime;
            float u = Mathf.Clamp01(_t / life);
            float e = 1f - (1f - u) * (1f - u);
            transform.localScale = Vector3.LerpUnclamped(scaleFrom, scaleTo, e);
            float a = color.a * (1f - u) * (1f - u);
            var c = new Color(color.r, color.g, color.b, a);
            if (mat != null) mat.color = c;
            if (line != null)
            {
                line.startColor = c;
                line.endColor = c;
            }
            if (billboard && Camera.main != null) transform.rotation = Camera.main.transform.rotation;
            if (spinDeg != 0f) transform.Rotate(0f, spinDeg * Time.deltaTime, 0f, Space.World);
            if (u >= 1f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }

    /// <summary>
    /// 타격·반응·브레이크·회피 시각 효과. 무기 계열별로 다른 형태(베기 궤적 / 둔기 충격파 / 탄착 불꽃 / 폭발 / 마법 파열)를 사용한다.
    /// </summary>
    public sealed class VfxSystem
    {
        private readonly Transform _root;
        private readonly ParticleSystem _sparks;
        private readonly ParticleSystem _dust;
        private readonly ParticleSystem _motes;
        private Mesh _quad;

        public VfxSystem(Transform root)
        {
            _root = root;
            _sparks = MakePs("Sparks", true, 1.6f);
            _dust = MakePs("Dust", false, -0.05f);
            _motes = MakePs("Motes", false, -0.3f);
        }

        private ParticleSystem MakePs(string name, bool stretch, float gravity)
        {
            var go = new GameObject("PS_" + name);
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.maxParticles = 600;
            main.startLifetime = 0.4f;
            main.startSpeed = 0f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, stretch ? 0.4f : 1.6f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = Mats.ParticleMaterial(Color.white);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.04f;
                r.lengthScale = 1.5f;
            }
            ps.Play();
            return ps;
        }

        private static void Emit(ParticleSystem ps, Vector3 pos, Vector3 baseDir, float spread, float speedMin, float speedMax, Color c, float size, float life, int count)
        {
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector3 d = (baseDir.sqrMagnitude > 0.001f ? baseDir.normalized : Vector3.up) + Random.insideUnitSphere * spread;
                p.position = pos + Random.insideUnitSphere * 0.1f;
                p.velocity = d.normalized * Random.Range(speedMin, speedMax);
                p.startColor = c;
                p.startSize = size * Random.Range(0.6f, 1.3f);
                p.startLifetime = life * Random.Range(0.7f, 1.2f);
                ps.Emit(p, 1);
            }
        }

        private Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "FxQuad" };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                return _quad;
            }
        }

        private FadeFx Spawn(string name, Mesh mesh, Vector3 pos, Quaternion rot, Color c, float life, Vector3 from, Vector3 to, bool textured)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var m = Mats.UnlitInstance(c);
            if (textured) m.mainTexture = Mats.SoftDot;
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var fx = go.AddComponent<FadeFx>();
            fx.life = life;
            fx.scaleFrom = from;
            fx.scaleTo = to;
            fx.color = c;
            fx.mat = m;
            go.transform.localScale = from;
            return fx;
        }

        public void Flash(Vector3 pos, Color c, float size, float life = 0.12f)
        {
            var fx = Spawn("Flash", Quad, pos, Quaternion.identity, c, life, Vector3.one * size * 0.4f, Vector3.one * size, true);
            fx.billboard = true;
        }

        public void GroundRing(Vector3 center, float radius, Color c, float life = 0.35f, float width = 0.12f)
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(_root, false);
            go.transform.position = new Vector3(center.x, 0.05f, center.z);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            const int n = 40;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            lr.widthMultiplier = width;
            var m = Mats.UnlitInstance(c);
            lr.material = m;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var fx = go.AddComponent<FadeFx>();
            fx.life = life;
            fx.scaleFrom = Vector3.one * radius * 0.2f;
            fx.scaleTo = Vector3.one * radius;
            fx.color = c;
            fx.mat = m;
            fx.line = lr;
        }

        /// <summary>근접 베기 궤적(초승달). 휘두름 방향에 맞춰 기울인다.</summary>
        public void Slash(Vector3 pos, Vector3 forward, SwingStyle swing, Color c, float size)
        {
            float roll = swing == SwingStyle.Overhead || swing == SwingStyle.Slam ? 80f : swing == SwingStyle.Uppercut ? -80f : swing == SwingStyle.HorizontalRL ? 180f : 0f;
            var rot = Quaternion.LookRotation(forward.sqrMagnitude > 0.001f ? forward : Vector3.forward) * Quaternion.Euler(0f, 0f, roll);
            Spawn("Slash", ProcMesh.Crescent(150f), pos + Vector3.up * 1.0f - forward * 0.6f, rot, c, 0.16f, Vector3.one * size * 0.8f, Vector3.one * size * 1.15f, false);
        }

        /// <summary>무기 계열별 명중 이펙트. 피해 이벤트와 같은 프레임에 호출된다.</summary>
        public void Impact(Vector3 pos, Vector3 dir, WeaponType w, SfxTier tier, SwingStyle swing, Element el, bool crit)
        {
            float k = tier == SfxTier.Ultimate ? 1.8f : tier == SfxTier.Skill || tier == SfxTier.Finisher ? 1.35f : tier == SfxTier.Heavy ? 1.2f : 1f;
            Vector3 hit = pos + Vector3.up * 1.0f;
            Color elc = Theme.Element(el);
            Color main = el != Element.None ? elc : Color.white;
            if (crit) main = Color.Lerp(main, Theme.Gold, 0.6f);
            Flash(hit, new Color(main.r, main.g, main.b, 0.95f), (crit ? 1.6f : 1.1f) * k);

            switch (w)
            {
                case WeaponType.Longsword:
                case WeaponType.Dagger:
                case WeaponType.DualBlades:
                case WeaponType.Scythe:
                    Slash(pos, dir, swing, new Color(1f, 1f, 1f, 0.9f), (w == WeaponType.Scythe ? 2.6f : w == WeaponType.Dagger ? 1.2f : 1.8f) * k);
                    Emit(_sparks, hit, dir, 0.7f, 4f, 9f, new Color(1f, 0.95f, 0.75f), 0.08f, 0.25f, (int)(8 * k));
                    break;
                case WeaponType.Spear:
                    Emit(_sparks, hit, dir, 0.25f, 7f, 13f, new Color(1f, 0.95f, 0.8f), 0.09f, 0.22f, (int)(10 * k));
                    break;
                case WeaponType.Greatsword:
                case WeaponType.Axe:
                    Slash(pos, dir, swing, new Color(1f, 0.92f, 0.8f, 0.9f), 2.4f * k);
                    GroundRing(pos, 1.6f * k, new Color(0.95f, 0.85f, 0.7f, 0.8f), 0.3f, 0.18f);
                    Emit(_dust, pos + Vector3.up * 0.2f, Vector3.up, 1.2f, 1f, 3f, new Color(0.6f, 0.52f, 0.42f, 0.8f), 0.6f, 0.6f, (int)(10 * k));
                    Emit(_sparks, hit, dir, 0.6f, 3f, 7f, new Color(1f, 0.85f, 0.6f), 0.1f, 0.3f, (int)(6 * k));
                    break;
                case WeaponType.Gauntlet:
                    GroundRing(hit - Vector3.up * 0.95f, 1.0f * k, new Color(1f, 0.9f, 0.7f, 0.7f), 0.2f, 0.1f);
                    Flash(hit + dir * 0.2f, new Color(1f, 0.85f, 0.6f, 0.8f), 0.9f * k, 0.08f);
                    Emit(_dust, hit, dir, 0.5f, 2f, 4f, new Color(0.9f, 0.85f, 0.75f, 0.6f), 0.3f, 0.3f, (int)(6 * k));
                    break;
                case WeaponType.Bow:
                case WeaponType.Crossbow:
                    Emit(_sparks, hit, -dir, 0.5f, 2f, 5f, new Color(0.9f, 0.8f, 0.6f), 0.06f, 0.2f, (int)(6 * k));
                    Emit(_dust, hit, -dir, 0.6f, 0.5f, 1.5f, new Color(0.7f, 0.6f, 0.5f, 0.6f), 0.25f, 0.35f, 4);
                    break;
                case WeaponType.Gun:
                    Emit(_sparks, hit, -dir, 0.8f, 3f, 8f, new Color(1f, 0.9f, 0.5f), 0.05f, 0.15f, (int)(7 * k));
                    break;
                case WeaponType.Launcher:
                    Explosion(pos, 2.2f * k, new Color(1f, 0.6f, 0.25f));
                    break;
                default:
                    // 마법: 속성색 입자 파열 + 고리
                    Emit(_motes, hit, Vector3.up, 1.3f, 1.5f, 4.5f, new Color(main.r, main.g, main.b, 0.9f), 0.25f, 0.5f, (int)(12 * k));
                    GroundRing(pos, 1.3f * k, new Color(main.r, main.g, main.b, 0.8f), 0.3f, 0.1f);
                    break;
            }
            if (tier == SfxTier.Ultimate) GroundRing(pos, 3.5f, new Color(main.r, main.g, main.b, 0.9f), 0.45f, 0.25f);
        }

        public void Explosion(Vector3 pos, float radius, Color c)
        {
            Spawn("Boom", ProcMesh.Disc(), new Vector3(pos.x, 0.06f, pos.z), Quaternion.identity, new Color(c.r, c.g, c.b, 0.55f), 0.35f, Vector3.one * radius * 0.3f, Vector3.one * radius, false);
            Flash(pos + Vector3.up * 0.8f, new Color(1f, 0.85f, 0.5f, 1f), radius * 1.4f, 0.18f);
            GroundRing(pos, radius * 1.3f, new Color(1f, 0.8f, 0.5f, 0.9f), 0.4f, 0.2f);
            Emit(_sparks, pos + Vector3.up * 0.5f, Vector3.up, 1.4f, 4f, 11f, new Color(1f, 0.7f, 0.3f), 0.12f, 0.5f, 22);
            Emit(_dust, pos + Vector3.up * 0.3f, Vector3.up, 1.5f, 1f, 3f, new Color(0.35f, 0.32f, 0.3f, 0.8f), 0.9f, 0.9f, 12);
        }

        public void Burst(Vector3 pos, Color c, int count, float size = 0.25f)
        {
            Emit(_motes, pos + Vector3.up, Vector3.up, 1.5f, 1.5f, 5f, c, size, 0.6f, count);
        }

        public void Reaction(Vector3 pos, Element a, Element b)
        {
            Burst(pos, Theme.Element(a), 18, 0.3f);
            Burst(pos, Theme.Element(b), 18, 0.3f);
            GroundRing(pos, 2.4f, Theme.WithAlpha(Color.Lerp(Theme.Element(a), Theme.Element(b), 0.5f), 0.9f), 0.45f, 0.2f);
            Flash(pos + Vector3.up, new Color(1f, 1f, 1f, 1f), 2.6f, 0.2f);
        }

        public void BreakShatter(Vector3 pos)
        {
            Emit(_sparks, pos + Vector3.up * 1.2f, Vector3.up, 1.6f, 4f, 10f, new Color(0.75f, 0.95f, 1f), 0.14f, 0.6f, 30);
            GroundRing(pos, 3f, new Color(0.7f, 0.95f, 1f, 0.9f), 0.5f, 0.25f);
            Flash(pos + Vector3.up * 1.2f, new Color(0.8f, 1f, 1f, 1f), 3f, 0.2f);
        }

        public void Afterimage(Vector3 pos, Quaternion rot, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            go.transform.position = pos + Vector3.up * 0.95f;
            go.transform.rotation = rot;
            var m = Mats.UnlitInstance(c);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var fx = go.AddComponent<FadeFx>();
            fx.life = 0.3f;
            fx.scaleFrom = new Vector3(0.7f, 0.62f, 0.55f);
            fx.scaleTo = new Vector3(0.8f, 0.66f, 0.6f);
            fx.color = c;
            fx.mat = m;
        }

        public void Heal(Vector3 pos)
        {
            Burst(pos, new Color(0.5f, 1f, 0.7f, 0.9f), 20, 0.25f);
            GroundRing(pos, 2f, new Color(0.5f, 1f, 0.7f, 0.9f), 0.5f, 0.12f);
        }
    }
}
