using Lumina.Core.Audio;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using UnityEngine;

namespace Lumina.Runtime.Battle
{
    /// <summary>
    /// 파티원 임시 모형 + 절차 애니메이션. 포즈는 코어 시뮬레이션의 행동 시간(actionTime)과 공격 정의(startup)에서
    /// 직접 계산되므로, 무기가 정면을 가로지르는 순간 = 명중 틱 = 타격음 재생 시점이 항상 일치한다.
    /// </summary>
    public sealed class AvatarView
    {
        public readonly GameObject root;
        public readonly MemberState member;
        public readonly WeaponRig rig;
        private readonly Transform _model;
        private readonly Transform _shoulderR;
        private readonly Transform _shoulderL;
        private readonly Vector3 _shoulderRBase;
        private readonly Vector3 _shoulderLBase;
        private readonly Renderer[] _bodyRenderers;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly Color _bodyColor;
        private float _flash;
        private float _tagPop = 1f;
        private float _runPhase;
        private float _yaw;
        private float _recoil;

        public AvatarView(Transform parent, MemberState m)
        {
            member = m;
            var el = Theme.Element(m.def.element);
            _bodyColor = Color.Lerp(el, new Color(0.16f, 0.2f, 0.3f), 0.55f);
            root = new GameObject("Avatar_" + m.def.id);
            root.transform.SetParent(parent, false);
            _model = new GameObject("Model").transform;
            _model.SetParent(root.transform, false);

            var body = ProcMesh.Prim(PrimitiveType.Capsule, "Body", _model, new Vector3(0f, 0.95f, 0f), new Vector3(0.7f, 0.62f, 0.55f), Mats.Lit(_bodyColor, 0.3f));
            var skirt = ProcMesh.Prim(PrimitiveType.Cylinder, "Coat", _model, new Vector3(0f, 0.55f, 0f), new Vector3(0.78f, 0.28f, 0.62f), Mats.Lit(Color.Lerp(_bodyColor, Color.black, 0.25f), 0.2f));
            var head = ProcMesh.Prim(PrimitiveType.Sphere, "Head", _model, new Vector3(0f, 1.78f, 0f), Vector3.one * 0.46f, Mats.Lit(new Color(0.96f, 0.84f, 0.74f), 0.3f));
            ProcMesh.Prim(PrimitiveType.Sphere, "Hair", _model, new Vector3(0f, 1.86f, -0.05f), new Vector3(0.5f, 0.42f, 0.5f), Mats.Lit(Color.Lerp(el, Color.white, 0.25f), 0.4f));
            ProcMesh.Prim(PrimitiveType.Cube, "Sash", _model, new Vector3(0f, 1.12f, 0f), new Vector3(0.74f, 0.1f, 0.6f), Mats.Lit(el, 0.5f));
            ProcMesh.Prim(PrimitiveType.Cube, "Visor", _model, new Vector3(0f, 1.8f, 0.2f), new Vector3(0.3f, 0.06f, 0.08f), Mats.Unlit(new Color(el.r, el.g, el.b, 1f)));
            _bodyRenderers = new Renderer[] { body.GetComponent<Renderer>(), skirt.GetComponent<Renderer>(), head.GetComponent<Renderer>() };

            var armMat = Mats.Lit(Color.Lerp(_bodyColor, Color.white, 0.1f), 0.3f);
            _shoulderRBase = new Vector3(0.36f, 1.38f, 0f);
            _shoulderLBase = new Vector3(-0.36f, 1.38f, 0f);
            _shoulderR = Arm(_model, "ShoulderR", _shoulderRBase, armMat, out var handR);
            _shoulderL = Arm(_model, "ShoulderL", _shoulderLBase, armMat, out var handL);
            rig = WeaponRig.Build(m.def.weapon, handR, handL, el);
        }

        private static Transform Arm(Transform parent, string name, Vector3 pos, Material mat, out Transform hand)
        {
            var sh = new GameObject(name).transform;
            sh.SetParent(parent, false);
            sh.localPosition = pos;
            ProcMesh.Prim(PrimitiveType.Cube, "Arm", sh, new Vector3(0f, 0f, 0.28f), new Vector3(0.15f, 0.15f, 0.56f), mat);
            hand = new GameObject("Hand").transform;
            hand.SetParent(sh, false);
            hand.localPosition = new Vector3(0f, 0f, 0.58f);
            ProcMesh.Prim(PrimitiveType.Sphere, "Fist", hand, Vector3.zero, Vector3.one * 0.16f, mat);
            return sh;
        }

        public void SetVisible(bool v)
        {
            if (root.activeSelf != v) root.SetActive(v);
            if (v) _tagPop = 0.55f;
            WeaponRig.SetTrails(rig, false);
        }

        public void Flash() => _flash = 1f;

        /// <summary>sim 상태로 포즈 갱신. alpha = 틱 사이 보간 비율.</summary>
        public void Pose(BattleSim sim, float alpha, float dt)
        {
            var body = sim.Body;
            Vector3 pos = Vector3.Lerp(V.W(body.prevPos), V.W(body.pos), alpha);
            root.transform.position = pos;
            float targetYaw = Mathf.Atan2(body.facing.x, body.facing.y) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, targetYaw, 1080f * dt);
            if (body.action == ActionKind.Normal || body.action == ActionKind.Skill || body.action == ActionKind.Ultimate) _yaw = targetYaw;
            root.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            // 기본(대기/이동) 포즈
            Vector3 rEuler = new Vector3(55f, 12f, 0f), lEuler = new Vector3(55f, -12f, 0f);
            Vector3 rOff = Vector3.zero, lOff = Vector3.zero;
            Vector3 modelPos = Vector3.zero;
            Vector3 modelEuler = Vector3.zero;
            bool trail = false;
            float charge = 0f, sinceRelease = -1f;
            bool casting = false;

            bool moving = (body.pos - body.prevPos).SqrLength > 1e-6f && body.action == ActionKind.None;
            if (moving)
            {
                _runPhase += dt * 11f;
                modelPos.y = Mathf.Abs(Mathf.Sin(_runPhase)) * 0.08f;
                modelEuler.x = 8f;
                rEuler.x += Mathf.Sin(_runPhase) * 25f;
                lEuler.x -= Mathf.Sin(_runPhase) * 25f;
            }
            HoldPose(ref rEuler, ref lEuler, ref rOff, ref lOff);

            switch (body.action)
            {
                case ActionKind.Dodge:
                {
                    float u = Mathf.Clamp01(body.actionTime / Mathf.Max(0.01f, body.actionDuration));
                    Vector3 local = root.transform.InverseTransformDirection(new Vector3(body.dodgeDir.x, 0f, body.dodgeDir.y));
                    modelEuler.x = local.z * 25f * Mathf.Sin(u * Mathf.PI);
                    modelEuler.z = -local.x * 25f * Mathf.Sin(u * Mathf.PI);
                    modelPos.y = -0.15f * Mathf.Sin(u * Mathf.PI);
                    break;
                }
                case ActionKind.HitStun:
                    modelEuler.x = -14f;
                    modelPos.x = Mathf.Sin(Time.time * 70f) * 0.04f;
                    break;
                case ActionKind.Normal:
                case ActionKind.Skill:
                case ActionKind.Ultimate:
                    if (body.current != null)
                        AttackPose(body.current, body.actionTime, ref rEuler, ref lEuler, ref rOff, ref lOff, ref modelPos, ref modelEuler, ref trail, out charge, out sinceRelease, out casting);
                    break;
            }

            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 6f);
            _shoulderR.localRotation = Quaternion.Euler(rEuler);
            _shoulderL.localRotation = Quaternion.Euler(lEuler);
            _shoulderR.localPosition = _shoulderRBase + rOff;
            _shoulderL.localPosition = _shoulderLBase + lOff;
            _model.localPosition = modelPos;
            _model.localRotation = Quaternion.Euler(modelEuler);
            _tagPop = Mathf.MoveTowards(_tagPop, 1f, dt * 4f);
            _model.localScale = Vector3.one * _tagPop;
            WeaponRig.SetTrails(rig, trail);
            rig.Animate(charge, sinceRelease, casting, dt);

            _flash = Mathf.MoveTowards(_flash, 0f, dt * 8f);
            foreach (var r in _bodyRenderers)
            {
                r.GetPropertyBlock(_block);
                Mats.SetBlockColor(_block, Color.Lerp(_bodyColor, new Color(1f, 0.35f, 0.3f), _flash));
                if (r.name == "Head") Mats.SetBlockColor(_block, Color.Lerp(new Color(0.96f, 0.84f, 0.74f), new Color(1f, 0.4f, 0.35f), _flash));
                r.SetPropertyBlock(_block);
            }
        }

        /// <summary>무기를 쥐는 기본 자세(원거리/마법은 앞으로 겨눔).</summary>
        private void HoldPose(ref Vector3 r, ref Vector3 l, ref Vector3 ro, ref Vector3 lo)
        {
            switch (member.def.weapon)
            {
                case WeaponType.Bow:
                    l = new Vector3(5f, 0f, 0f);
                    r = new Vector3(5f, -20f, 0f);
                    ro = new Vector3(0f, 0f, -0.05f);
                    break;
                case WeaponType.Crossbow:
                case WeaponType.Gun:
                    r = new Vector3(15f, -5f, 0f);
                    l = new Vector3(25f, 25f, 0f);
                    break;
                case WeaponType.Launcher:
                    r = new Vector3(0f, 0f, 0f);
                    ro = new Vector3(-0.05f, 0.12f, -0.25f);
                    l = new Vector3(20f, 25f, 0f);
                    break;
                case WeaponType.Staff:
                    r = new Vector3(-30f, 10f, 0f);
                    break;
                case WeaponType.Scroll:
                    r = new Vector3(20f, -10f, 0f);
                    l = new Vector3(30f, 20f, 0f);
                    break;
                case WeaponType.Grimoire:
                    l = new Vector3(20f, 15f, 0f);
                    break;
                case WeaponType.MagicRing:
                    r = new Vector3(15f, 0f, 0f);
                    break;
                case WeaponType.Spear:
                    r = new Vector3(20f, 5f, 0f);
                    break;
            }
        }

        private static float EaseInOut(float u) => u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) / 2f;
        private static float EaseOut(float u) => 1f - (1f - u) * (1f - u);

        /// <summary>
        /// 공격 포즈. 각 명중 시각(startup + i×interval)을 '스윙 중심'으로 두고 SwingLead×2 길이의 스트라이크 구간에서
        /// 무기가 가장 빠르게 정면을 지나가도록 한다.
        /// </summary>
        private void AttackPose(ActionInstance act, float t, ref Vector3 r, ref Vector3 l, ref Vector3 ro, ref Vector3 lo,
            ref Vector3 mPos, ref Vector3 mEuler, ref bool trail, out float charge, out float sinceRelease, out bool casting)
        {
            var def = act.def;
            float S = def.startup;
            int hits = def.SafeHits;
            float lead = SfxTiming.SwingLead(act.weapon);
            if (lead <= 0f) lead = 0.06f;
            float strike = Mathf.Min(lead * 2f, hits > 1 ? def.hitInterval * 0.95f : 99f);
            strike = Mathf.Max(strike, 0.05f);
            charge = Mathf.Clamp01(t / Mathf.Max(0.01f, S));
            sinceRelease = t - S;
            casting = true;

            // 현재 기준 명중 인덱스(가장 가까운 다음/직전 명중)
            int i = Mathf.Clamp(Mathf.RoundToInt((t - S) / Mathf.Max(0.0001f, def.hitInterval)), 0, hits - 1);
            if (hits == 1) i = 0;
            float hitT = S + i * def.hitInterval;
            float s0 = hitT - strike * 0.5f;
            float u = Mathf.Clamp01((t - s0) / strike);          // 0→1, 0.5 = 명중 순간
            bool beforeFirst = t < S - strike * 0.5f;
            float windup = beforeFirst ? EaseOut(Mathf.Clamp01(t / Mathf.Max(0.01f, S - strike * 0.5f))) : 1f;
            float after = t - (def.LastHitTime + strike * 0.5f);   // 마지막 타 이후
            float settle = after > 0f ? Mathf.Clamp01(after / Mathf.Max(0.05f, def.recovery * 0.8f)) : 0f;
            bool inStrike = !beforeFirst && after <= 0.02f;
            bool alt = (act.step + i) % 2 == 1;

            switch (def.swing)
            {
                case SwingStyle.HorizontalLR:
                case SwingStyle.HorizontalRL:
                {
                    float dir = def.swing == SwingStyle.HorizontalLR ? 1f : -1f;
                    if (alt && act.weapon == WeaponType.DualBlades) dir = -dir;
                    float from = -105f * dir, to = 95f * dir;
                    float yaw = beforeFirst ? Mathf.Lerp(12f, from, windup) : Mathf.Lerp(from, to, EaseInOut(u));
                    var arm = new Vector3(8f, yaw, 0f);
                    arm = Vector3.Lerp(arm, new Vector3(55f, 12f, 0f), settle);
                    if (act.weapon == WeaponType.DualBlades && alt) l = new Vector3(arm.x, -arm.y, 0f);
                    else r = arm;
                    mEuler.y = beforeFirst ? -15f * dir * windup : Mathf.Lerp(-15f * dir, 20f * dir, EaseInOut(u)) * (1f - settle);
                    mEuler.x = inStrike ? 8f : 0f;
                    trail = inStrike;
                    break;
                }
                case SwingStyle.Overhead:
                case SwingStyle.Slam:
                {
                    float pitch = beforeFirst ? Mathf.Lerp(55f, -150f, windup) : Mathf.Lerp(-150f, 70f, EaseInOut(u));
                    r = Vector3.Lerp(new Vector3(pitch, 0f, 0f), new Vector3(55f, 12f, 0f), settle);
                    l = act.weapon == WeaponType.Greatsword || act.weapon == WeaponType.Axe ? new Vector3(r.x, -8f, 0f) : l;
                    mEuler.x = beforeFirst ? -10f * windup : Mathf.Lerp(-10f, 18f, EaseInOut(u)) * (1f - settle);
                    if (def.swing == SwingStyle.Slam)
                    {
                        float jump = beforeFirst ? Mathf.Sin(windup * Mathf.PI * 0.85f) * 0.9f : Mathf.Max(0f, 0.9f * (1f - EaseInOut(u) * 2f));
                        mPos.y += jump;
                    }
                    trail = inStrike;
                    break;
                }
                case SwingStyle.Thrust:
                {
                    float ext = beforeFirst ? -0.25f * windup : Mathf.Lerp(-0.25f, 0.55f, EaseInOut(u));
                    ext *= 1f - settle;
                    var arm = new Vector3(5f, 0f, 0f);
                    if (act.weapon == WeaponType.Dagger && alt) l = arm;
                    else r = arm;
                    if (act.weapon == WeaponType.Dagger && alt) lo = new Vector3(0.1f, 0f, ext);
                    else ro = new Vector3(-0.1f, 0f, ext);
                    if (act.weapon == WeaponType.Spear) l = new Vector3(15f, 25f, 0f);
                    mEuler.x = inStrike ? 12f : 0f;
                    trail = inStrike;
                    break;
                }
                case SwingStyle.Punch:
                {
                    float ext = beforeFirst ? -0.2f * windup : Mathf.Lerp(-0.2f, 0.45f, EaseInOut(u));
                    ext *= 1f - settle;
                    if (alt)
                    {
                        l = new Vector3(5f, 5f, 0f);
                        lo = new Vector3(0.12f, 0f, ext);
                    }
                    else
                    {
                        r = new Vector3(5f, -5f, 0f);
                        ro = new Vector3(-0.12f, 0f, ext);
                    }
                    mEuler.y = (alt ? 18f : -18f) * (inStrike ? 1f : windup);
                    mEuler.x = inStrike ? 10f : 0f;
                    trail = inStrike;
                    break;
                }
                case SwingStyle.Uppercut:
                {
                    float pitch = beforeFirst ? Mathf.Lerp(55f, 85f, windup) : Mathf.Lerp(85f, -120f, EaseInOut(u));
                    r = Vector3.Lerp(new Vector3(pitch, -5f, 0f), new Vector3(55f, 12f, 0f), settle);
                    mPos.y += inStrike ? Mathf.Sin(u * Mathf.PI) * 0.25f : 0f;
                    mEuler.x = beforeFirst ? 12f * windup : -10f * (1f - settle);
                    trail = inStrike;
                    break;
                }
                case SwingStyle.Spin:
                {
                    // 전체 다단 구간 동안 회전(각 명중마다 정면 통과)
                    float total = def.LastHitTime + strike * 0.5f - (S - strike * 0.5f);
                    float su = Mathf.Clamp01((t - (S - strike * 0.5f)) / Mathf.Max(0.05f, total));
                    float turns = hits > 1 ? hits * 0.5f : 1f;
                    mEuler.y = beforeFirst ? -30f * windup : su * 360f * turns;
                    r = new Vector3(15f, 75f, 0f);
                    if (act.weapon == WeaponType.DualBlades) l = new Vector3(15f, -75f, 0f);
                    trail = inStrike;
                    break;
                }
                case SwingStyle.Draw:
                {
                    l = new Vector3(0f, 0f, 0f);
                    float pull = beforeFirst || t < S ? charge : 0f;
                    r = new Vector3(0f, -18f, 0f);
                    ro = new Vector3(-0.1f, 0f, -0.45f * pull);
                    if (t >= S && t < S + 0.12f) ro.z = 0.05f;
                    break;
                }
                case SwingStyle.Fire:
                {
                    if (t >= hitT - 0.001f && t < hitT + 0.02f) _recoil = act.weapon == WeaponType.Launcher ? 1f : act.weapon == WeaponType.Gun ? 0.5f : 0.7f;
                    float kick = _recoil;
                    r = new Vector3(5f - kick * 28f, -3f, 0f);
                    ro += new Vector3(0f, 0f, -kick * 0.18f);
                    mEuler.x = -kick * 6f;
                    break;
                }
                case SwingStyle.Cast:
                {
                    float raise = beforeFirst || t < S ? charge : 0f;
                    r = new Vector3(Mathf.Lerp(-30f, -70f, raise), 8f, 0f);
                    if (t >= S && t < S + 0.15f)
                    {
                        float k = (t - S) / 0.15f;
                        r = new Vector3(Mathf.Lerp(-70f, 10f, EaseOut(k)), 5f, 0f);
                        ro = new Vector3(0f, 0f, 0.25f * (1f - k));
                    }
                    if (act.weapon == WeaponType.Grimoire) l = new Vector3(Mathf.Lerp(20f, -10f, raise), 15f, 0f);
                    mEuler.x = raise * -6f;
                    break;
                }
                case SwingStyle.Unfurl:
                {
                    r = new Vector3(5f, -12f, 0f);
                    l = new Vector3(5f, 12f, 0f);
                    ro = new Vector3(-0.1f, 0.05f, 0.1f);
                    if (t >= S && t < S + 0.12f) mEuler.x = 10f;
                    break;
                }
                case SwingStyle.RingSpin:
                {
                    r = new Vector3(Mathf.Lerp(15f, -10f, charge), 0f, 0f);
                    if (t >= S && t < S + 0.12f) ro = new Vector3(0f, 0f, 0.25f);
                    break;
                }
            }
            if (act.kind == ActionKind.Ultimate && beforeFirst) mPos.y += Mathf.Sin(windup * Mathf.PI) * 0.25f;
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
            float k = tier == SfxTier.Ultimate ? 0.45f : tier == SfxTier.Finisher || tier == SfxTier.Heavy || tier == SfxTier.Skill ? 0.3f : 0.15f;
            if (def.isBoss) k *= 0.4f;
            _flinch = dir.normalized * k;
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
            float squash = 1f + _flash * 0.12f;
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
            _model.localScale = scale;
            _model.localRotation = Quaternion.Euler(euler);
            _model.localPosition = mpos;

            _flash = Mathf.MoveTowards(_flash, 0f, dt * 9f);
            foreach (var r in _renderers)
            {
                r.GetPropertyBlock(_block);
                Mats.SetBlockColor(_block, Color.Lerp(glow, Color.white, _flash));
                r.SetPropertyBlock(_block);
            }
        }
    }
}
