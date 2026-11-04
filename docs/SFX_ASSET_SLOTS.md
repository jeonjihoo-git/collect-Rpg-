# 실제 음원 교체 슬롯 (자동 생성: `dotnet run --project tools/Lumina.Sim -- slots`)

`Assets/Lumina/Resources/LuminaSfx/` 아래에 **아래 경로 + 확장자(.wav/.ogg)** 로 파일을 넣으면, 그 소리가 절차 합성음 대신 재생됩니다. 코드 수정 불필요.
- 등급(Tier)별 파일이 없으면 `종류` 공통 파일(예: `Longsword/Impact.wav`)을 찾고, 그것도 없으면 합성음을 씁니다.
- 명중음(Impact)은 **피해가 들어간 그 틱**에 재생됩니다. 파일 앞부분에 무음이 있으면 타격감이 늦어지므로 **앞 무음 0ms** 로 잘라 주세요.
- 휘두름(Swing)은 **명중 시각보다 선행 시간만큼 먼저** 재생됩니다. 소리의 가장 큰 지점이 파일 시작에서 선행 시간 위치에 오도록 만들어 주세요.
- 같은 소리 반복감 방지를 위해 재생 시 피치가 ±3% 무작위로 흔들립니다.

| 무기 | 슬롯 경로(공통 → 등급별) | 재생 시점 | 길이 가이드 | 휘두름 선행 |
|---|---|---|---|---|
| 장검 | `Longsword/Swing` → `Longsword/Swing_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중 − 선행 | 0.1~0.45초 | 90ms |
| 장검 | `Longsword/Impact` → `Longsword/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 장검 | `Longsword/Tail` → `Longsword/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 단검 | `Dagger/Swing` → `Dagger/Swing_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중 − 선행 | 0.1~0.45초 | 50ms |
| 단검 | `Dagger/Impact` → `Dagger/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 단검 | `Dagger/Tail` → `Dagger/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 대검 | `Greatsword/Swing` → `Greatsword/Swing_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중 − 선행 | 0.1~0.45초 | 200ms |
| 대검 | `Greatsword/Impact` → `Greatsword/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 대검 | `Greatsword/Tail` → `Greatsword/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 쌍검 | `DualBlades/Swing` → `DualBlades/Swing_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중 − 선행 | 0.1~0.45초 | 60ms |
| 쌍검 | `DualBlades/Impact` → `DualBlades/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 쌍검 | `DualBlades/Tail` → `DualBlades/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 창 | `Spear/Swing` → `Spear/Swing_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중 − 선행 | 0.1~0.45초 | 80ms |
| 창 | `Spear/Impact` → `Spear/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 창 | `Spear/Tail` → `Spear/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 건틀릿 | `Gauntlet/Swing` → `Gauntlet/Swing_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중 − 선행 | 0.1~0.45초 | 50ms |
| 건틀릿 | `Gauntlet/Impact` → `Gauntlet/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 건틀릿 | `Gauntlet/Tail` → `Gauntlet/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 낫 | `Scythe/Swing` → `Scythe/Swing_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중 − 선행 | 0.1~0.45초 | 140ms |
| 낫 | `Scythe/Impact` → `Scythe/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 낫 | `Scythe/Tail` → `Scythe/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 도끼 | `Axe/Swing` → `Axe/Swing_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중 − 선행 | 0.1~0.45초 | 160ms |
| 도끼 | `Axe/Impact` → `Axe/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 도끼 | `Axe/Tail` → `Axe/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 활 | `Bow/Cast` → `Bow/Cast_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 공격 시작(당김·장전·충전) | 선딜 길이(0.06~0.85초) | - |
| 활 | `Bow/Projectile` → `Bow/Projectile_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 발사·발동 | 0.1~0.5초 | - |
| 활 | `Bow/Impact` → `Bow/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 활 | `Bow/Tail` → `Bow/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 석궁 | `Crossbow/Cast` → `Crossbow/Cast_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 공격 시작(당김·장전·충전) | 선딜 길이(0.06~0.85초) | - |
| 석궁 | `Crossbow/Projectile` → `Crossbow/Projectile_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 발사·발동 | 0.1~0.5초 | - |
| 석궁 | `Crossbow/Impact` → `Crossbow/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 석궁 | `Crossbow/Tail` → `Crossbow/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 총 | `Gun/Cast` → `Gun/Cast_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 공격 시작(당김·장전·충전) | 선딜 길이(0.06~0.85초) | - |
| 총 | `Gun/Projectile` → `Gun/Projectile_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 발사·발동 | 0.1~0.5초 | - |
| 총 | `Gun/Impact` → `Gun/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 총 | `Gun/Tail` → `Gun/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 런처 | `Launcher/Cast` → `Launcher/Cast_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 공격 시작(당김·장전·충전) | 선딜 길이(0.06~0.85초) | - |
| 런처 | `Launcher/Projectile` → `Launcher/Projectile_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 발사·발동 | 0.1~0.5초 | - |
| 런처 | `Launcher/Impact` → `Launcher/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.8~1.5초 | - |
| 런처 | `Launcher/Tail` → `Launcher/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 지팡이 | `Staff/Cast` → `Staff/Cast_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 공격 시작(당김·장전·충전) | 선딜 길이(0.06~0.85초) | - |
| 지팡이 | `Staff/Projectile` → `Staff/Projectile_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 발사·발동 | 0.1~0.5초 | - |
| 지팡이 | `Staff/Impact` → `Staff/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 지팡이 | `Staff/Tail` → `Staff/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 마법책 | `Grimoire/Cast` → `Grimoire/Cast_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 공격 시작(당김·장전·충전) | 선딜 길이(0.06~0.85초) | - |
| 마법책 | `Grimoire/Projectile` → `Grimoire/Projectile_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 발사·발동 | 0.1~0.5초 | - |
| 마법책 | `Grimoire/Impact` → `Grimoire/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 마법책 | `Grimoire/Tail` → `Grimoire/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 스크롤 | `Scroll/Cast` → `Scroll/Cast_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 공격 시작(당김·장전·충전) | 선딜 길이(0.06~0.85초) | - |
| 스크롤 | `Scroll/Projectile` → `Scroll/Projectile_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 발사·발동 | 0.1~0.5초 | - |
| 스크롤 | `Scroll/Impact` → `Scroll/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 스크롤 | `Scroll/Tail` → `Scroll/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |
| 마법링 | `MagicRing/Cast` → `MagicRing/Cast_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 공격 시작(당김·장전·충전) | 선딜 길이(0.06~0.85초) | - |
| 마법링 | `MagicRing/Projectile` → `MagicRing/Projectile_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 발사·발동 | 0.1~0.5초 | - |
| 마법링 | `MagicRing/Impact` → `MagicRing/Impact_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 명중(피해 적용 틱) | 0.1~0.5초 | - |
| 마법링 | `MagicRing/Tail` → `MagicRing/Tail_Light` · `_Medium` · `_Heavy` · `_Finisher` · `_Skill` · `_Ultimate` | 강타 명중 잔향 | 0.3~1.5초 | - |

| 대상 재질(피격 반응) | 슬롯 경로 |
|---|---|
| Flesh | `Reaction/Flesh` → `Reaction/Flesh_<Tier>` |
| Armor | `Reaction/Armor` → `Reaction/Armor_<Tier>` |
| Phantom | `Reaction/Phantom` → `Reaction/Phantom_<Tier>` |

| 시스템 | 슬롯 경로 |
|---|---|
| Dodge | `System/Dodge` |
| PerfectDodge | `System/PerfectDodge` |
| Tag | `System/Tag` |
| Break | `System/Break` |
| PlayerHurt | `System/PlayerHurt` |
| EnemyTelegraph | `System/EnemyTelegraph` |
| EnemySwing | `System/EnemySwing` |
| EnemyProjectile | `System/EnemyProjectile` |
| EnemyDeath | `System/EnemyDeath` |
| UltReady | `System/UltReady` |
| Heal | `System/Heal` |
| Buff | `System/Buff` |
| Shield | `System/Shield` |
| Reaction | `System/Reaction` |
| Victory | `System/Victory` |
| Defeat | `System/Defeat` |
| UiClick | `System/UiClick` |
