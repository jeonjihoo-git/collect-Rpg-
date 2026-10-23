using Lumina.Core.Combat;
using Lumina.Core.Data;
using UnityEngine;

namespace Lumina.Runtime.Battle
{
    /// <summary>3/4 탑뷰 추적 카메라 + 트라우마 방식 흔들림 + 궁극기 줌.</summary>
    public sealed class CameraRig
    {
        public readonly Camera cam;
        private Vector3 _vel;
        private Vector3 _focus;
        private float _trauma;
        private float _zoom;
        private float _seed;
        private static readonly Vector3 Offset = new Vector3(0f, 11.5f, -8.8f);

        public CameraRig(Camera c)
        {
            cam = c;
            cam.fieldOfView = 46f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.09f, 0.15f);
            _seed = Random.value * 100f;
        }

        public void Snap(Vector3 target)
        {
            _focus = target;
            cam.transform.position = target + Offset;
            cam.transform.rotation = Quaternion.LookRotation(-Offset.normalized + Vector3.up * 0.05f, Vector3.up);
        }

        public void Shake(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);

        public void Zoom(float amount) => _zoom = Mathf.Max(_zoom, amount);

        public void Update(Vector3 target, float dt)
        {
            _focus = Vector3.SmoothDamp(_focus, target, ref _vel, 0.12f, 60f, dt);
            _zoom = Mathf.MoveTowards(_zoom, 0f, dt * 0.8f);
            Vector3 off = Offset * (1f - 0.22f * _zoom);
            float t = Time.time * 28f;
            float s = _trauma * _trauma;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(_seed, t) - 0.5f, Mathf.PerlinNoise(_seed + 7f, t) - 0.5f, Mathf.PerlinNoise(_seed + 13f, t) - 0.5f) * (0.9f * s);
            cam.transform.position = _focus + off + shake;
            cam.transform.rotation = Quaternion.LookRotation(-off.normalized + Vector3.up * 0.05f, Vector3.up) * Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(_seed + 21f, t) - 0.5f) * 4f * s);
            _trauma = Mathf.MoveTowards(_trauma, 0f, dt * 1.8f);
        }
    }

    /// <summary>1개 검증 맵: 원형 전장 + 경계 기둥 + 조명.</summary>
    public static class ArenaBuilder
    {
        public static GameObject Build(Transform parent, float radius)
        {
            var root = new GameObject("Arena");
            root.transform.SetParent(parent, false);
            var floor = ProcMesh.Flat("Floor", root.transform, ProcMesh.Disc(72), Mats.Lit(new Color(0.2f, 0.24f, 0.3f), 0.15f), 0f);
            floor.transform.localScale = Vector3.one * (radius + 1.5f);
            floor.GetComponent<MeshRenderer>().receiveShadows = true;
            var inner = ProcMesh.Flat("Inner", root.transform, ProcMesh.Ring(0.97f, 96), Mats.Unlit(new Color(0.17f, 0.71f, 0.65f, 0.55f)), 0.01f);
            inner.transform.localScale = Vector3.one * radius;
            var mid = ProcMesh.Flat("Mid", root.transform, ProcMesh.Ring(0.985f, 96), Mats.Unlit(new Color(0.79f, 0.65f, 0.36f, 0.35f)), 0.01f);
            mid.transform.localScale = Vector3.one * radius * 0.55f;
            var stone = Mats.Lit(new Color(0.32f, 0.35f, 0.42f), 0.2f);
            var cap = Mats.Unlit(new Color(0.17f, 0.71f, 0.65f, 0.9f));
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius + 0.8f);
                float h = 1.6f + (i % 3) * 0.5f;
                ProcMesh.Prim(PrimitiveType.Cylinder, "Pillar" + i, root.transform, p + Vector3.up * h * 0.5f, new Vector3(0.7f, h * 0.5f, 0.7f), stone);
                ProcMesh.Prim(PrimitiveType.Sphere, "Lamp" + i, root.transform, p + Vector3.up * (h + 0.25f), Vector3.one * 0.35f, cap);
            }
            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.4f, 0.5f);
            return root;
        }
    }

    /// <summary>적 공격 예고 장판(빨강). 채워지는 진행도로 회피 타이밍을 읽게 한다.</summary>
    public sealed class TelegraphView
    {
        public readonly GameObject root;
        private readonly Transform _fill;
        private readonly EnemyAttackShape _shape;
        private readonly float _size;

        public TelegraphView(Transform parent, CombatEvent ev)
        {
            _shape = ev.enemyShape;
            root = new GameObject("Telegraph");
            root.transform.SetParent(parent, false);
            root.transform.position = V.W(ev.pos, 0.03f);
            root.transform.rotation = V.Facing(ev.dir);
            var baseMat = Mats.Unlit(new Color(1f, 0.25f, 0.18f, 0.22f));
            var fillMat = Mats.Unlit(new Color(1f, 0.3f, 0.2f, 0.42f));
            Mesh mesh;
            Vector3 scale;
            switch (_shape)
            {
                case EnemyAttackShape.Cone:
                    mesh = ProcMesh.Cone(ev.arcDeg);
                    scale = Vector3.one * ev.range;
                    break;
                case EnemyAttackShape.Line:
                case EnemyAttackShape.Projectile:
                    mesh = ProcMesh.Rect();
                    scale = new Vector3(ev.radius * 2f, 1f, ev.range);
                    break;
                default:
                    mesh = ProcMesh.Disc();
                    scale = Vector3.one * ev.radius;
                    break;
            }
            _size = 1f;
            var b = ProcMesh.Flat("Base", root.transform, mesh, baseMat, 0f);
            b.transform.localScale = scale;
            var f = ProcMesh.Flat("Fill", root.transform, mesh, fillMat, 0.005f);
            f.transform.localScale = Vector3.zero;
            _fill = f.transform;
            _fillTarget = scale;
            var edge = ProcMesh.Flat("Edge", root.transform, _shape == EnemyAttackShape.Cone ? ProcMesh.Cone(ev.arcDeg, 0.95f) : _shape == EnemyAttackShape.Line || _shape == EnemyAttackShape.Projectile ? ProcMesh.Rect() : ProcMesh.Ring(0.94f), Mats.Unlit(new Color(1f, 0.35f, 0.25f, 0.85f)), 0.008f);
            edge.transform.localScale = _shape == EnemyAttackShape.Line || _shape == EnemyAttackShape.Projectile ? new Vector3(scale.x, 1f, 0.06f) : scale;
        }

        private readonly Vector3 _fillTarget;

        public void SetProgress(float p)
        {
            p = Mathf.Clamp01(p) * _size;
            if (_shape == EnemyAttackShape.Line || _shape == EnemyAttackShape.Projectile) _fill.localScale = new Vector3(_fillTarget.x, 1f, _fillTarget.z * p);
            else _fill.localScale = _fillTarget * p;
        }
    }

    /// <summary>플레이어 투사체/장판/링 표시(무기별 모양).</summary>
    public static class EntityViews
    {
        public static GameObject Projectile(Transform parent, WeaponType w, Element el)
        {
            var go = new GameObject("Proj_" + w);
            go.transform.SetParent(parent, false);
            Color c = el != Element.None ? Theme.Element(el) : Color.white;
            switch (w)
            {
                case WeaponType.Bow:
                    ProcMesh.Prim(PrimitiveType.Cube, "Shaft", go.transform, Vector3.zero, new Vector3(0.04f, 0.04f, 0.8f), Mats.Lit(new Color(0.6f, 0.45f, 0.3f)));
                    ProcMesh.Prim(PrimitiveType.Cube, "Head", go.transform, new Vector3(0f, 0f, 0.42f), new Vector3(0.08f, 0.02f, 0.12f), Mats.Lit(new Color(0.8f, 0.82f, 0.86f), 0.8f));
                    AddTrail(go, new Color(1f, 1f, 0.9f), 0.06f, 0.12f);
                    break;
                case WeaponType.Crossbow:
                    ProcMesh.Prim(PrimitiveType.Cube, "Bolt", go.transform, Vector3.zero, new Vector3(0.07f, 0.07f, 0.55f), Mats.Lit(new Color(0.4f, 0.42f, 0.48f), 0.7f));
                    AddTrail(go, new Color(0.9f, 0.95f, 1f), 0.08f, 0.1f);
                    break;
                case WeaponType.Gun:
                    ProcMesh.Prim(PrimitiveType.Sphere, "Bullet", go.transform, Vector3.zero, Vector3.one * 0.09f, Mats.Unlit(new Color(1f, 0.9f, 0.5f)));
                    AddTrail(go, new Color(1f, 0.85f, 0.4f), 0.06f, 0.06f);
                    break;
                case WeaponType.Launcher:
                    ProcMesh.Prim(PrimitiveType.Capsule, "Rocket", go.transform, Vector3.zero, new Vector3(0.18f, 0.3f, 0.18f), Mats.Lit(new Color(0.35f, 0.4f, 0.35f)), Quaternion.Euler(90f, 0f, 0f));
                    AddTrail(go, new Color(1f, 0.6f, 0.2f), 0.25f, 0.25f);
                    break;
                default:
                    ProcMesh.Prim(PrimitiveType.Sphere, "Orb", go.transform, Vector3.zero, Vector3.one * 0.38f, Mats.Unlit(new Color(c.r, c.g, c.b, 0.95f)));
                    ProcMesh.Prim(PrimitiveType.Sphere, "Core", go.transform, Vector3.zero, Vector3.one * 0.2f, Mats.Unlit(Color.white));
                    AddTrail(go, c, 0.3f, 0.2f);
                    break;
            }
            return go;
        }

        public static GameObject EnemyProjectile(Transform parent)
        {
            var go = new GameObject("EnemyProj");
            go.transform.SetParent(parent, false);
            ProcMesh.Prim(PrimitiveType.Sphere, "Orb", go.transform, Vector3.zero, Vector3.one * 0.5f, Mats.Unlit(new Color(0.75f, 0.3f, 1f, 0.95f)));
            AddTrail(go, new Color(0.7f, 0.3f, 1f), 0.35f, 0.25f);
            return go;
        }

        public static GameObject AreaMarker(Transform parent, WeaponType w, Element el, float radius)
        {
            var go = new GameObject("Area_" + w);
            go.transform.SetParent(parent, false);
            Color c = el != Element.None ? Theme.Element(el) : new Color(0.9f, 0.95f, 1f);
            var ring = ProcMesh.Flat("Ring", go.transform, ProcMesh.Ring(0.9f), Mats.Unlit(new Color(c.r, c.g, c.b, 0.9f)), 0.04f);
            ring.transform.localScale = Vector3.one * radius;
            var fill = ProcMesh.Flat("Fill", go.transform, w == WeaponType.Scroll ? ProcMesh.Rect() : ProcMesh.Disc(), Mats.Unlit(new Color(c.r, c.g, c.b, 0.25f)), 0.035f);
            fill.transform.localScale = w == WeaponType.Scroll ? new Vector3(radius * 1.3f, 1f, radius * 1.3f) : Vector3.one * radius * 0.95f;
            if (w == WeaponType.Scroll) fill.transform.localPosition = new Vector3(0f, 0.035f, -radius * 0.65f);
            if (w == WeaponType.Grimoire)
            {
                var star = ProcMesh.Flat("Rune", go.transform, ProcMesh.Cone(360f, 0.82f), Mats.Unlit(new Color(c.r, c.g, c.b, 0.7f)), 0.045f);
                star.transform.localScale = Vector3.one * radius * 0.6f;
                var fx = star.AddComponent<FadeFx>();
                fx.life = 999f;
                fx.scaleFrom = fx.scaleTo = Vector3.one * radius * 0.6f;
                fx.color = new Color(c.r, c.g, c.b, 0.7f);
                fx.spinDeg = 200f;
            }
            return go;
        }

        public static GameObject RingWave(Transform parent, Element el)
        {
            var go = new GameObject("RingWave");
            go.transform.SetParent(parent, false);
            Color c = el != Element.None ? Theme.Element(el) : new Color(0.85f, 0.75f, 1f);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            const int n = 48;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            lr.widthMultiplier = 0.35f;
            lr.material = Mats.UnlitInstance(new Color(c.r, c.g, c.b, 0.85f));
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private static void AddTrail(GameObject go, Color c, float width, float time)
        {
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = time;
            tr.startWidth = width;
            tr.endWidth = 0f;
            tr.material = Mats.UnlitInstance(Color.white);
            tr.startColor = new Color(c.r, c.g, c.b, 0.9f);
            tr.endColor = new Color(c.r, c.g, c.b, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
