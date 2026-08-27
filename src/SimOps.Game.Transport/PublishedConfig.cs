using System;
using System.Linq;
using SimOps.Game.Core;

namespace SimOps.Game.Transport;

// Fields intentionally support both Unity JsonUtility and System.Text.Json IncludeFields.
// Schema 1 supports only the attack changes in the registered difficulty experiment contract.
[Serializable]
public sealed class PublishedConfig
{
    public int schemaVersion = 1;
    public string gameVersion = "";
    public string configVersion = "";
    public string checksum = "";
    public int[] attackPowers = Array.Empty<int>();

    public GameConfig ToConfig()
    {
        var baseline = GameConfig.CreateBaseline();
        if (schemaVersion != 1 || gameVersion != baseline.GameVersion || attackPowers == null || attackPowers.Length != 6 ||
            attackPowers.Where((value, i) => value < baseline.Encounters[i].AttackPower || value > baseline.Encounters[i].AttackPower * 3).Any() ||
            attackPowers[0] != baseline.Encounters[0].AttackPower)
            throw new ArgumentException("Unsupported published config schema or attack bounds.");
        var encounters = baseline.Encounters.Select((e, i) => new EncounterDefinition(e.Id, e.Stage, e.MaxHealth,
            attackPowers[i], e.GuardAmount, e.EmpowerAmount, e.HeavyAttackPercent, e.AttackWeight,
            e.HeavyAttackWeight, e.GuardWeight, e.EmpowerWeight, e.ParTurns)).ToArray();
        var config = new GameConfig(gameVersion, configVersion, baseline.InitialMaxHealth, baseline.InitialAttack,
            baseline.BaseActionPoints, baseline.StrikeBonus, baseline.GuardAmount, baseline.TechniqueDamage,
            baseline.TechniqueCooldownTurns, baseline.InitialItemCharges, baseline.ItemHealAmount,
            baseline.MaximumTurnsPerEncounter, encounters, baseline.Rewards);
        if (config.Checksum != checksum) throw new ArgumentException("Published config checksum mismatch.");
        return config;
    }
    public static PublishedConfig From(GameConfig config)
    {
        var snapshot = new PublishedConfig { gameVersion = config.GameVersion, configVersion = config.ConfigVersion,
            checksum = config.Checksum, attackPowers = config.Encounters.Select(e => e.AttackPower).ToArray() };
        snapshot.ToConfig(); // Fail closed if a future experiment changes fields not represented by this schema.
        return snapshot;
    }
}
