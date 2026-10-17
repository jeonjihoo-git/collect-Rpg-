using Lumina.Core.Combat;

namespace Lumina.Sim
{
    /// <summary>
    /// 헤드리스 검증용 '수동 입력' 봇. 자동전투 AI(AutoBattleController) 위에 스킬 순환 태그를 더해
    /// 사람이 하듯 파티를 교대하며 속성 반응을 만든다. 게임 내 자동전투 기능이 아니다(자동 태그는 미확정 정책).
    /// </summary>
    public sealed class TestBot
    {
        private readonly AutoBattleController _ai = new AutoBattleController();

        public PlayerInput Decide(BattleSim sim)
        {
            var input = _ai.Decide(sim);
            if (input.dodgePressed || input.ultimatePressed || input.skillPressed) return input;
            if (sim.TagCd <= 0f && sim.Active.skillCd > 0f && sim.Body.action == ActionKind.None)
            {
                for (int i = 1; i < sim.Party.Length; i++)
                {
                    int j = (sim.ActiveIndex + i) % sim.Party.Length;
                    if (sim.Party[j].Alive && sim.Party[j].skillCd <= 0f)
                    {
                        input = PlayerInput.None;
                        input.tagTo = j;
                        return input;
                    }
                }
            }
            return input;
        }
    }
}
