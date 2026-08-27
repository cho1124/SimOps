using System;
using System.Collections.Generic;
using System.Linq;
using SimOps.Game.Core;

namespace SimOps.Unity
{
    // Presentation only. ValidActionTypes and the simulation remain authoritative.
    public static class ArenaText
    {
        public static string ActionName(GameActionType type) => type switch
        {
            GameActionType.Strike => "공격", GameActionType.Guard => "방어",
            GameActionType.Technique => "기술", GameActionType.UseItem => "회복",
            GameActionType.EndTurn => "턴 종료", GameActionType.ChooseReward => "보상 선택", _ => "행동"
        };
        public static string EnemyName(string id) => id switch
        {
            "striker-1" => "정찰병", "guardian-2" => "수호병", "charger-3" => "돌격병",
            "striker-4" => "추격병", "guardian-5" => "철벽 수호병", "boss-6" => "아레나 지배자", _ => "상대"
        };
        public static string RewardName(RewardDefinition reward) => reward.Id switch
        {
            "offense-sharpened-edge" => "날카로운 칼날", "offense-precise-strike" => "정밀 타격",
            "offense-overcharge" => "과충전", "defense-braced-stance" => "견고한 자세",
            "defense-reinforced-frame" => "강화 골격", "defense-opening-guard" => "선제 방어",
            "sustain-vitality" => "생명력", "sustain-field-medicine" => "야전 의약품",
            "sustain-extra-charge" => "추가 회복약", "tactics-quick-cycle" => "빠른 순환",
            "tactics-battle-rhythm" => "전투의 리듬", "tactics-fortified-technique" => "기술 단련", _ => "강화 보상"
        };
        public static string Category(RewardCategory category) => category switch
        {
            RewardCategory.Offense => "공격 강화", RewardCategory.Defense => "방어 강화",
            RewardCategory.Sustain => "생존 강화", _ => "전술 강화"
        };
        public static string RewardDescription(RewardDefinition reward) => reward.EffectType switch
        {
            RewardEffectType.Attack => $"기본 공격력 +{reward.Value}",
            RewardEffectType.StrikeBonus => $"일반 공격 피해 +{reward.Value}",
            RewardEffectType.TechniqueBonus => $"기술 피해 +{reward.Value}",
            RewardEffectType.GuardBonus => $"방어 행동의 방어도 +{reward.Value}",
            RewardEffectType.MaxHealth => $"최대 체력과 현재 체력 +{reward.Value}",
            RewardEffectType.StartTurnBlock => $"매 턴 시작 시 방어도 +{reward.Value}",
            RewardEffectType.ItemHealBonus => $"회복약 회복량 +{reward.Value}",
            RewardEffectType.ItemCharges => $"회복약 {reward.Value}개 추가",
            RewardEffectType.TechniqueCooldownReduction => $"기술 재사용 대기 -{reward.Value}턴",
            RewardEffectType.ActionPoints => $"매 턴 행동력 +{reward.Value}", _ => "효과 확인 필요"
        };
        public static int Bonus(GameConfig config, IReadOnlyList<GameAction> actions, RewardEffectType effect) =>
            actions.Where(a => a.ActionType == GameActionType.ChooseReward)
                .Sum(a => config.Rewards.Where(r => r.Id == a.RewardId && r.EffectType == effect).Sum(r => r.Value));

        public static string ActionEffect(GameActionType type, GameConfig config, GameObservation observation, IReadOnlyList<GameAction> actions)
        {
            switch (type)
            {
                case GameActionType.Strike: return $"피해 {observation.Player.Attack + config.StrikeBonus + Bonus(config, actions, RewardEffectType.StrikeBonus)} · 행동력 1";
                case GameActionType.Guard: return $"방어도 +{config.GuardAmount + Bonus(config, actions, RewardEffectType.GuardBonus)} · 행동력 1";
                case GameActionType.Technique: return $"피해 {config.TechniqueDamage + Bonus(config, actions, RewardEffectType.TechniqueBonus)} · 행동력 2";
                case GameActionType.UseItem: return $"최대 {config.ItemHealAmount + Bonus(config, actions, RewardEffectType.ItemHealBonus)} 회복 · 행동력 0";
                default: return "남은 행동력을 포기하고 다음 턴";
            }
        }
        public static string DisabledReason(GameActionType type, GameObservation observation)
        {
            if (observation.ValidActionTypes.Contains(type)) return "";
            if (observation.Phase != RunPhase.PlayerTurn) return "전투 중에만 사용";
            if (type == GameActionType.Technique && observation.Player.TechniqueCooldown > 0) return $"재사용까지 {observation.Player.TechniqueCooldown}턴";
            if (type == GameActionType.UseItem) return observation.Player.ItemCharges <= 0 ? "회복약 없음" : "이번 턴에 이미 사용";
            return "행동력 부족";
        }
        public static string Intent(GameConfig config, GameObservation observation)
        {
            var enemy = observation.Enemy;
            if (enemy == null || observation.Phase != RunPhase.PlayerTurn) return "다음 전투를 준비하세요";
            var encounter = config.Encounters[observation.Stage - 1];
            var power = enemy.AttackPower + enemy.AttackBonus;
            return enemy.Intent switch
            {
                EnemyIntentType.Attack => $"공격 예고  ·  피해 {power}",
                EnemyIntentType.HeavyAttack => $"강공격 예고  ·  피해 {power * encounter.HeavyAttackPercent / 100}",
                EnemyIntentType.Guard => $"방어 예고  ·  방어도 +{encounter.GuardAmount}",
                _ => $"강화 예고  ·  공격력 +{encounter.EmpowerAmount}"
            };
        }
        public static string Outcome(RunOutcome outcome) => outcome == RunOutcome.Victory ? "아레나 정복" : "도전 종료";
    }
}
