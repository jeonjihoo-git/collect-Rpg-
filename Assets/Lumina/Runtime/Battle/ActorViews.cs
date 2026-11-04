using Lumina.Core.Audio;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using Lumina.Core.Presentation;
using UnityEngine;
using MPose = Lumina.Core.Presentation.Pose;

namespace Lumina.Runtime.Battle
{
    /// <summary>
    /// 파티원 임시 모형 + 절차 애니메이션. 포즈는 Core 의 MotionLibrary(테스트로 검증된 키프레임)에서 계산되며,
    /// 무기 끝이 가장 빠르게 정면을 지나는 순간 = 명중 틱 = 타격음 재생 시점이다.
    /// 계층: root(위치·방향) → model(몸 전체) → 다리 / torso(상체 비틀기) → 어깨 → 손 → 무기
    /// </summary>
    public sealed class AvatarView
    {
        public readonly GameObject root;
        public readonly MemberState member;
        public readonly WeaponRig rig;
        private readonly Transform _model;
        private readonly Transform _torso;
        private readonly Transform _shoulderR;
        private readonly Transform _shoulderL;
        private readonly Transform _legR;
        private readonly Transform _legL;
        private readonly Renderer[] _bodyRenderers;
        private readonly Color[] _bodyColors;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private float _flash;
        private float _tagPop = 1f;
        private float _runPhase;
        private float _yaw;
        private MPose _shown;
        private MPose _blendFrom;
        private float _blend;
        private int _lastActionId = -1;
        private ActionKind _lastKind = ActionKind.None;
        private const float BlendTime = 0.06f;

        public AvatarView(Transform parent, MemberState m)
        {
            member = m;
            var el = Theme.Element(m.def.element);
            var bodyColor = Color.Lerp(el, new Color(0.16f, 0.2f, 0.3f), 0.55f);
            var dark = Color.Lerp(bodyColor, Color.black, 0.25f);
            var skin = new Color(0.96f, 0.84f, 0.74f);
            root = new GameObject("Avatar_" + m.def.id);
            root.transform.SetParent(parent, false);
            _model = new GameObject("Model").transform;
            _model.SetParent(root.transform, false);

            // 하체(모델 직속): 코트 + 다리
            var coat = ProcMesh.Prim(PrimitiveType.Cylinder, "Coat", _model, new Vector3(0f, 0.62f, 0f), new Vector3(0.72f, 0.16f, 0.58f), Mats.Lit(dark, 0.2f));
            _legR = Leg(_model, "LegR", ToU(MotionLibrary.HipR), Mats.Lit(Color.Lerp(dark, Color.black, 0.3f), 0.2f));
            _legL = Leg(_model, "LegL", ToU(MotionLibrary.HipL), Mats.Lit(Color.Lerp(dark, Color.black, 0.3f), 0.2f));

            // 상체(허리 피벗): 몸통·머리·어깨
            _torso = new GameObject("Torso").transform;
            _torso.SetParent(_model, false);
            _torso.localPosition = ToU(MotionLibrary.TorsoPivot);
            var body = ProcMesh.Prim(PrimitiveType.Capsule, "Body", _torso, new Vector3(0f, 0.18f, 0f), new Vector3(0.66f, 0.42f, 0.5f), Mats.Lit(bodyColor, 0.3f));
            var head = ProcMesh.Prim(PrimitiveType.Sphere, "Head", _torso, new Vector3(0f, 0.88f, 0f), Vector3.one * 0.46f, Mats.Lit(skin, 0.3f));
            ProcMesh.Prim(PrimitiveType.Sphere, "Hair", _torso, new Vector3(0f, 0.96f, -0.05f), new Vector3(0.5f, 0.42f, 0.5f), Mats.Lit(Color.Lerp(el, Color.white, 0.25f), 0.4f));
            ProcMesh.Prim(PrimitiveType.Cube, "Sash", _torso, new Vector3(0f, 0.22f, 0f), new Vector3(0.7f, 0.1f, 0.56f), Mats.Lit(el, 0.5f));
            ProcMesh.Prim(PrimitiveType.Cube, "Visor", _torso, new Vector3(0f, 0.9f, 0.2f), new Vector3(0.3f, 0.06f, 0.08f), Mats.Unlit(new Color(el.r, el.g, el.b, 1f)));
            _bodyRenderers = new Renderer[] { body.GetComponent<Renderer>(), coat.GetComponent<Renderer>(), head.GetComponent<Renderer>() };
            _bodyColors = new[] { bodyColor, dark, skin };

            var armMat = Mats.Lit(Color.Lerp(bodyColor, Color.white, 0.1f), 0.3f);
            _shoulderR = Arm(_torso, "ShoulderR", ToU(MotionLibrary.ShoulderR), armMat, out var handR);
            _shoulderL = Arm(_torso, "ShoulderL", ToU(MotionLibrary.ShoulderL), armMat, out var handL);
            rig = WeaponRig.Build(m.def.weapon, handR, handL, el);
            _shown = MotionLibrary.Rest(m.def.weapon);
        }

        private static Vector3 ToU(V3 v) => new Vector3(v.x, v.y, v.z);

        private static Transform Arm(Transform parent, string name, Vector3 pos, Material mat, out Transform hand)
        {
            var sh = new GameObject(name).transform;
            sh.SetParent(parent, false);
            sh.localPosition = pos;
            ProcMesh.Prim(PrimitiveType.Cube, "Arm", sh, new Vector3(0f, 0f, MotionLibrary.ArmLength * 0.5f), new Vector3(0.15f, 0.15f, MotionLibrary.ArmLength - 0.02f), mat);
            hand = new GameObject("Hand").transform;
            hand.SetParent(sh, false);
            hand.localPosition = new Vector3(0f, 0f, MotionLibrary.ArmLength);
            ProcMesh.Prim(PrimitiveType.Sphere, "Fist", hand, Vector3.zero, Vector3.one * 0.16f, mat);
            return sh;
        }

        private static Transform Leg(Transform parent, string name, Vector3 hip, Material mat)
        {
            var leg = new GameObject(name).transform;
            leg.SetParent(parent, false);
            leg.localPosition = hip;
            ProcMesh.Prim(PrimitiveType.Cube, "Shin", leg, new Vector3(0f, -MotionLibrary.LegLength * 0.5f, 0f), new Vector3(0.17f, MotionLibrary.LegLength, 0.2f), mat);
            ProcMesh.Prim(PrimitiveType.Cube, "Boot", leg, new Vector3(0f, -MotionLibrary.LegLength + 0.03f, 0.06f), new Vector3(0.19f, 0.08f, 0.3f), mat);
            return leg;
        }

        public void SetVisible(bool v)
        {
            if (root.activeSelf != v) root.SetActive(v);
            if (v) _tagPop = 0.55f;
            WeaponRig.SetTrails(rig, false);
        }

        public void Flash() => _flash = 1f;

        /// <summary>sim 상태로 포즈 갱신. alpha = 틱 사이 보간 비율, dt = 0 이면 정지(히트스톱/일시정지).</summary>
        public void Pose(BattleSim sim, float alpha, float dt)
        {
            var body = sim.Body;
            var w = member.def.weapon;
            Vector3 pos = Vector3.Lerp(V.W(body.prevPos), V.W(body.pos), alpha);
            root.transform.position = pos;
            float targetYaw = Mathf.Atan2(body.facing.x, body.facing.y) * Mathf.Rad2Deg;
            bool attacking = body.action == ActionKind.Normal || body.action == ActionKind.Skill || body.action == ActionKind.Ultimate || body.action == ActionKind.Approach;
            _yaw = attacking ? targetYaw : Mathf.MoveTowardsAngle(_yaw, targetYaw, 1080f * dt);
            root.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            float charge = 0f, sinceRelease = -1f;
            bool casting = false;
            MPose target;
            switch (body.action)
            {
                case ActionKind.Normal:
                case ActionKind.Skill:
                case ActionKind.Ultimate:
                    if (body.current != null)
                    {
                        target = MotionLibrary.Attack(w, body.action, body.current.step, body.current.def, body.actionTime, out charge, out sinceRelease);
                        casting = true;
                    }
                    else target = MotionLibrary.Rest(w);
                    break;
                case ActionKind.Approach:
                    _runPhase += dt * 17f;
                    target = MotionLibrary.Dash(w, _runPhase);
                    break;
                case ActionKind.Dodge:
                {
                    float u = Mathf.Clamp01(body.actionTime / Mathf.Max(0.01f, body.actionDuration));
                    Vector3 local = root.transform.InverseTransformDirection(new Vector3(body.dodgeDir.x, 0f, body.dodgeDir.y));
                    target = MotionLibrary.Dodge(w, u, local.x, local.z);
                    break;
                }
                case ActionKind.HitStun:
                    target = MotionLibrary.HitStun(w, Mathf.Clamp01(body.actionTime / Mathf.Max(0.01f, body.actionDuration)));
                    break;
                default:
                {
                    bool moving = (body.pos - body.prevPos).SqrLength > 1e-6f;
                    if (moving)
                    {
                        _runPhase += dt * 11f;
                        target = MotionLibrary.Run(w, _runPhase);
                    }
                    else target = MotionLibrary.Rest(w);
                    break;
                }
            }

            // 행동이 바뀌는 순간(시작·캔슬·교대) 짧게 블렌딩해 튀지 않게 한다(공격 중 포즈 자체는 그대로 재생).
            int actionId = body.current != null ? body.current.id : -1;
            if (actionId != _lastActionId || body.action != _lastKind)
            {
                _blendFrom = _shown;
                _blend = BlendTime;
                _lastActionId = actionId;
                _lastKind = body.action;
            }
            if (_blend > 0f)
            {
                _blend = Mathf.Max(0f, _blend - dt);
                target = MPose.Lerp(_blendFrom, target, 1f - _blend / BlendTime);
            }
            _shown = target;
            Apply(target);

            _tagPop = Mathf.MoveTowards(_tagPop, 1f, dt * 4f);
            _model.localScale = Vector3.one * _tagPop;
            WeaponRig.SetTrails(rig, target.trail > 0.5f);
            rig.Animate(charge, sinceRelease, casting, dt);

            _flash = Mathf.MoveTowards(_flash, 0f, dt * 8f);
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                var r = _bodyRenderers[i];
                r.GetPropertyBlock(_block);
                Mats.SetBlockColor(_block, Color.Lerp(_bodyColors[i], new Color(1f, 0.35f, 0.3f), _flash));
                r.SetPropertyBlock(_block);
            }
        }

        private void Apply(MPose p)
        {
            _model.localPosition = ToU(p.modelPos);
            _model.localRotation = Quaternion.Euler(ToU(p.modelRot));
            _torso.localRotation = Quaternion.Euler(ToU(p.torso));
            _shoulderR.localPosition = ToU(MotionLibrary.ShoulderR + p.reachR);
            _shoulderL.localPosition = ToU(MotionLibrary.ShoulderL + p.reachL);
            _shoulderR.localRotation = Quaternion.Euler(ToU(p.armR));
            _shoulderL.localRotation = Quaternion.Euler(ToU(p.armL));
            _legR.localRotation = Quaternion.Euler(p.legR, 0f, 0f);
            _legL.localRotation = Quaternion.Euler(p.legL, 0f, 0f);
        }
    }


    /// <summary>적 임시 모형 + 예고/공격/피격/브레이크/사망 연출.</summary>
    public sealed class EnemyView
    {
        public readonly GameObject root;
        public readonly int id;
        public readonly EnemyDef def;
        private readonly Transform _model;
        private readonly Transform _armR;
        private readonly Transform _armL;
        private readonly Transform _core;
        private readonly Transform _dizzy;
        private readonly Renderer[] _renderers;
        private readonly Color _baseColor;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private float _flash;
        private Vector3 _flinch;
        private float _recoil;
        private float _spawn;
        private float _death = -1f;
        public bool Finished { get; private set; }

        public EnemyView(Transform parent, EnemyState e)
        {
            id = e.id;
            def = e.def;
            root = new GameObject("Enemy_" + def.id + "_" + id);
            root.transform.SetParent(parent, false);
            _model = new GameObject("Model").transform;
            _model.SetParent(root.transform, false);
            float s = def.radius / 0.6f;
            _baseColor = def.isBoss ? new Color(0.22f, 0.14f, 0.3f) : def.material == EnemyMaterial.Phantom ? new Color(0.3f, 0.22f, 0.5f) : new Color(0.28f, 0.17f, 0.36f);
            var bodyMat = Mats.Lit(_baseColor, 0.4f);
            var eyeMat = Mats.Unlit(def.isBoss ? new Color(1f, 0.55f, 0.2f) : new Color(1f, 0.25f, 0.35f));
            GameObject body;
            if (def.material == EnemyMaterial.Phantom)
            {
                body = ProcMesh.Prim(PrimitiveType.Sphere, "Body", _model, new Vector3(0f, 1.3f * s, 0f), Vector3.one * 0.9f * s, bodyMat);
                var ring = ProcMesh.Flat("Halo", _model, ProcMesh.Ring(0.85f), Mats.Unlit(new Color(0.75f, 0.45f, 1f, 0.8f)), 1.3f * s);
                ring.transform.localScale = Vector3.one * 0.85f * s;
                ring.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
                _core = ProcMesh.Prim(PrimitiveType.Sphere, "Core", _model, new Vector3(0f, 1.3f * s, 0.55f * s), Vector3.one * 0.25f * s, eyeMat).transform;
            }
            else
            {
                body = ProcMesh.Prim(PrimitiveType.Capsule, "Body", _model, new Vector3(0f, 1.0f * s, 0f), new Vector3(0.9f, 0.75f, 0.75f) * s, bodyMat);
                ProcMesh.Prim(PrimitiveType.Sphere, "EyeL", _model, new Vector3(-0.16f * s, 1.45f * s, 0.32f * s), Vector3.one * 0.12f * s, eyeMat);
                ProcMesh.Prim(PrimitiveType.Sphere, "EyeR", _model, new Vector3(0.16f * s, 1.45f * s, 0.32f * s), Vector3.one * 0.12f * s, eyeMat);
                if (def.isBoss)
                {
                    var horn = Mats.Lit(new Color(0.85f, 0.75f, 0.6f), 0.5f);
                    ProcMesh.Prim(PrimitiveType.Cube, "HornL", _model, new Vector3(-0.35f * s, 1.95f * s, 0f), new Vector3(0.12f, 0.5f, 0.12f) * s, horn, Quaternion.Euler(0f, 0f, 25f));
                    ProcMesh.Prim(PrimitiveType.Cube, "HornR", _model, new Vector3(0.35f * s, 1.95f * s, 0f), new Vector3(0.12f, 0.5f, 0.12f) * s, horn, Quaternion.Euler(0f, 0f, -25f));
                    ProcMesh.Prim(PrimitiveType.Cube, "Crack", _model, new Vector3(0f, 1.05f * s, 0.38f * s), new Vector3(0.5f, 0.06f, 0.05f) * s, eyeMat);
                }
                _armR = EnemyArm(_model, new Vector3(0.55f * s, 1.25f * s, 0f), s, bodyMat);
                _armL = EnemyArm(_model, new Vector3(-0.55f * s, 1.25f * s, 0f), s, bodyMat);
            }
            _renderers = new[] { body.GetComponent<Renderer>() };
            _dizzy = new GameObject("Dizzy").transform;
            _dizzy.SetParent(_model, false);
            _dizzy.localPosition = new Vector3(0f, (def.material == EnemyMaterial.Phantom ? 2.1f : 2.0f) * s, 0f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                ProcMesh.Prim(PrimitiveType.Sphere, "Star" + i, _dizzy, new Vector3(Mathf.Cos(a) * 0.4f * s, 0f, Mathf.Sin(a) * 0.4f * s), Vector3.one * 0.14f * s, Mats.Unlit(new Color(1f, 0.9f, 0.4f)));
            }
            _dizzy.gameObject.SetActive(false);
            root.transform.position = V.W(e.pos);
        }

        private static Transform EnemyArm(Transform parent, Vector3 pos, float s, Material m)
        {
            var sh = new GameObject("Arm").transform;
            sh.SetParent(parent, false);
            sh.localPosition = pos;
            ProcMesh.Prim(PrimitiveType.Cube, "Limb", sh, new Vector3(0f, 0f, 0.35f * s), new Vector3(0.2f, 0.2f, 0.7f) * s, m);
            ProcMesh.Prim(PrimitiveType.Cube, "Claw", sh, new Vector3(0f, 0f, 0.8f * s), new Vector3(0.28f, 0.06f, 0.3f) * s, Mats.Lit(new Color(0.85f, 0.85f, 0.9f), 0.8f));
            sh.localRotation = Quaternion.Euler(60f, 0f, 0f);
            return sh;
        }

        public void Hit(Vector3 dir, SfxTier tier)
        {
            _flash = 1f;
            float k = tier == SfxTier.Ultimate ? 0.55f : tier == SfxTier.Finisher || tier == SfxTier.Heavy || tier == SfxTier.Skill ? 0.4f : 0.22f;
            if (def.isBoss) k *= 0.4f;
            _flinch = dir.normalized * k;
            _recoil = Mathf.Max(_recoil, def.isBoss ? 0.4f : 1f);
        }

        public void Die() => _death = 0f;

        public void Pose(EnemyState e, float alpha, float dt)
        {
            if (_death >= 0f)
            {
                _death += dt;
                float k = Mathf.Clamp01(_death / 0.6f);
                _model.localScale = Vector3.one * (1f - k * 0.9f);
                _model.localPosition = new Vector3(0f, -k * 0.8f, 0f);
                _model.localRotation = Quaternion.Euler(-60f * k, 0f, 0f);
                if (k >= 1f) Finished = true;
                return;
            }
            Vector3 pos = Vector3.Lerp(V.W(e.prevPos), V.W(e.pos), alpha);
            _flinch = Vector3.MoveTowards(_flinch, Vector3.zero, dt * 2.5f);
            root.transform.position = pos + _flinch;
            root.transform.rotation = Quaternion.Slerp(root.transform.rotation, V.Facing(e.ai == EnemyAi.Telegraph ? e.lockedDir : e.facing), 1f - Mathf.Exp(-dt * 14f));

            _spawn = Mathf.MoveTowards(_spawn, 1f, dt * 2f);
            float squash = 1f + _flash * 0.22f;
            Vector3 scale = new Vector3(squash, 1f / squash, squash) * Mathf.SmoothStep(0.2f, 1f, _spawn);
            Vector3 euler = Vector3.zero;
            Vector3 mpos = Vector3.zero;
            float armPitch = 60f;
            Color glow = _baseColor;

            if (e.Staggered)
            {
                euler = new Vector3(-18f, 0f, Mathf.Sin(Time.time * 5f) * 8f);
                _dizzy.gameObject.SetActive(true);
                _dizzy.Rotate(0f, 260f * dt, 0f);
                armPitch = 80f;
            }
            else
            {
                _dizzy.gameObject.SetActive(false);
                if (e.ai == EnemyAi.Telegraph && e.attack != null)
                {
                    float p = 1f - e.aiTimer / Mathf.Max(0.01f, e.aiDuration);
                    glow = Color.Lerp(_baseColor, new Color(1f, 0.25f, 0.15f), p * 0.8f);
                    switch (e.attack.shape)
                    {
                        case EnemyAttackShape.Circle:
                        case EnemyAttackShape.SelfRing:
                            armPitch = Mathf.Lerp(60f, -150f, p);
                            mpos.y = e.attack.shape == EnemyAttackShape.SelfRing ? -0.2f * p : 0f;
                            euler.x = -12f * p;
                            break;
                        case EnemyAttackShape.Line:
                            euler.x = 15f * p;
                            armPitch = Mathf.Lerp(60f, 0f, p);
                            mpos.z = -0.3f * p;
                            break;
                        default:
                            armPitch = Mathf.Lerp(60f, -60f, p);
                            euler.y = -25f * p;
                            break;
                    }
                    if (_core != null) _core.localScale = Vector3.one * Mathf.Lerp(0.25f, 0.6f, p) * (def.radius / 0.6f);
                }
                else if (e.ai == EnemyAi.Recover)
                {
                    float rp = 1f - e.aiTimer / Mathf.Max(0.01f, e.aiDuration);
                    armPitch = Mathf.Lerp(110f, 60f, rp);
                    euler.y = e.attack != null && e.attack.shape == EnemyAttackShape.Cone ? Mathf.Lerp(35f, 0f, rp) : 0f;
                    if (_core != null) _core.localScale = Vector3.one * 0.25f * (def.radius / 0.6f);
                }
                if (def.material == EnemyMaterial.Phantom) mpos.y += Mathf.Sin(Time.time * 2.4f + id) * 0.12f;
            }
            if (_armR != null)
            {
                _armR.localRotation = Quaternion.Euler(armPitch, 10f, 0f);
                _armL.localRotation = Quaternion.Euler(armPitch, -10f, 0f);
            }
            // 피격 순간 뒤로 젖힘(즉시) + 시뮬레이션 경직 중 자세 유지
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 7f);
            euler.x -= 14f * _recoil;
            if (e.flinchTimer > 0f) euler.x -= 16f * Mathf.Clamp01(e.flinchTimer / 0.25f);
            _model.localScale = scale;
            _model.localRotation = Quaternion.Euler(euler);
            _model.localPosition = mpos;
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 6f);
            foreach (var r in _renderers)
            {
                r.GetPropertyBlock(_block);
                Mats.SetBlockColor(_block, Color.Lerp(glow, Color.white, _flash));
                r.SetPropertyBlock(_block);
            }
        }
    }
}
