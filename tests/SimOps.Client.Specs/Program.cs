using SimOps.Game.Core;
using SimOps.Unity;

var config = GameConfig.CreateBaseline();
var score = ScoreRule.CreateBaseline();
var sim = new GameSimulation(config, score);
var context = new RunContext(config.GameVersion, config.Checksum, score.Version, score.Checksum, 42);
var observation = sim.Reset(context);
var passed = 0;
void Check(bool test, string name) { if (!test) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
Check(Enum.GetValues<GameActionType>().All(x => ArenaText.ActionName(x) != "행동"), "UI-TEXT-001 all actions translated");
Check(config.Encounters.All(x => ArenaText.EnemyName(x.Id) != "상대"), "UI-TEXT-002 all encounters translated");
Check(config.Rewards.All(x => ArenaText.RewardName(x) != "강화 보상" && ArenaText.RewardDescription(x).Contains(x.Value.ToString())), "UI-TEXT-003 all reward effects include actual values");
Check(ArenaText.ActionEffect(GameActionType.Technique, config, observation, sim.ActionLog).Contains("행동력 2"), "UI-TEXT-004 technique cost is two, not one");
var beforeHash = sim.GetStateHash();
foreach (var type in Enum.GetValues<GameActionType>()) { ArenaText.ActionEffect(type, config, observation, sim.ActionLog); ArenaText.DisabledReason(type, observation); }
ArenaText.Intent(config, observation);
Check(sim.GetStateHash() == beforeHash, "UI-TEXT-005 presentation never mutates simulation");
observation = sim.Apply(new GameAction(0, GameActionType.UseItem)).Observation;
Check(ArenaText.DisabledReason(GameActionType.UseItem, observation) == "이번 턴에 이미 사용", "UI-TEXT-006 item disabled explanation");
observation = sim.Apply(new GameAction(1, GameActionType.Technique)).Observation;
Check(observation.Player.TechniqueCooldown > 0 && ArenaText.DisabledReason(GameActionType.Technique, observation).Contains("재사용"), "UI-TEXT-007 cooldown explanation");
var actions = new[] { new GameAction(0, GameActionType.ChooseReward, "offense-precise-strike"), new GameAction(1, GameActionType.ChooseReward, "offense-precise-strike") };
Check(ArenaText.Bonus(config, actions, RewardEffectType.StrikeBonus) == 4, "UI-TEXT-008 stacked rewards included in displayed power");
for (ulong seed = 0; seed < 100; seed++)
{
    observation = sim.Reset(new RunContext(config.GameVersion, config.Checksum, score.Version, score.Checksum, seed));
    var encounter = config.Encounters[observation.Stage - 1]; var enemy = observation.Enemy;
    if (enemy.Intent == EnemyIntentType.HeavyAttack)
    { Check(ArenaText.Intent(config, observation).Contains(((enemy.AttackPower + enemy.AttackBonus) * encounter.HeavyAttackPercent / 100).ToString()), "UI-TEXT-009 heavy intent uses integer damage rule"); break; }
    if (seed == 99) throw new Exception("Heavy intent fixture not found");
}
Console.WriteLine($"Client presentation specs: {passed} passed");
