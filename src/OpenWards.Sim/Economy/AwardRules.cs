// The level's trophy and award settings, with the defaults of CorsixTH CorsixTH/Lua/base_config.lua (awards_trophies).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Economy;

/// <summary>
/// What a hospital must do in a year to win a trophy or an award, or to be fined, and what it gets: the level files'
/// <c>awards_trophies</c> settings by name, each falling back on CorsixTH's base_config.lua (and <c>TrophyMayor</c>,
/// commented out there, on its 25). <see cref="YearEndAwards"/> uses them.
/// </summary>
public sealed class AwardRules
{
    // Trophies.
    public const string CansofCoke = "CansofCoke";
    public const string CansofCokeBonus = "CansofCokeBonus";
    public const string Reputation = "Reputation";
    public const string TrophyReputationBonus = "TrophyReputationBonus";
    public const string TrophyAllCuredBonus = "TrophyAllCuredBonus";
    public const string TrophyDeathBonus = "TrophyDeathBonus";
    public const string TrophyCuresBonus = "TrophyCuresBonus";
    public const string TrophyMayorBonus = "TrophyMayorBonus";
    public const string TrophyMayor = "TrophyMayor";
    public const string TrophyStaffHappiness = "TrophyStaffHappiness";
    public const string TrophyStaffHappinessBonus = "TrophyStaffHappinessBonus";
    public const string RatKillsAbsolute = "RatKillsAbsolute";
    public const string RatKillsAbsoluteBonus = "RatKillsAbsoluteBonus";
    public const string RatKillsPercentage = "RatKillsPercentage";
    public const string RatKillsPercentageBonus = "RatKillsPercentageBonus";
    public const string Plant = "Plant";
    public const string PlantBonus = "PlantBonus";

    // Awards and penalties.
    public const string CuresAward = "CuresAward";
    public const string CuresPoor = "CuresPoor";
    public const string DeathsAward = "DeathsAward";
    public const string DeathsPoor = "DeathsPoor";
    public const string CuresVDeathsAward = "CuresVDeathsAward";
    public const string CuresVDeathsPoor = "CuresVDeathsPoor";
    public const string ReputationAward = "ReputationAward";
    public const string ReputationPoor = "ReputationPoor";
    public const string HospValueAward = "HospValueAward";
    public const string HospValuePoor = "HospValuePoor";
    public const string CuresBonus = "CuresBonus";
    public const string CuresPenalty = "CuresPenalty";
    public const string AllCuresBonus = "AllCuresBonus";
    public const string DeathsBonus = "DeathsBonus";
    public const string DeathsPenalty = "DeathsPenalty";
    public const string CuresVDeathsBonus = "CuresVDeathsBonus";
    public const string CuresVDeathsPenalty = "CuresVDeathsPenalty";
    public const string AwardReputationBonus = "AwardReputationBonus";
    public const string AwardReputationPenalty = "AwardReputationPenalty";
    public const string HospValueBonus = "HospValueBonus";
    public const string HospValuePenalty = "HospValuePenalty";
    public const string PopulationPercentageAward = "PopulationPercentageAward";
    public const string PopulationPercentagePoor = "PopulationPercentagePoor";
    public const string PopulationPercentageBonus = "PopulationPercentageBonus";
    public const string PopulationPercentagePenalty = "PopulationPercentagePenalty";
    public const string CleanlinessAward = "CleanlinessAward";
    public const string CleanlinessPoor = "CleanlinessPoor";
    public const string CleanlinessBonus = "CleanlinessBonus";
    public const string CleanlinessPenalty = "CleanlinessPenalty";
    public const string EmergencyAward = "EmergencyAward";
    public const string EmergencyPoor = "EmergencyPoor";
    public const string EmergencyBonus = "EmergencyBonus";
    public const string EmergencyPenalty = "EmergencyPenalty";
    public const string StaffHappinessAward = "StaffHappinessAward";
    public const string StaffHappinessPoor = "StaffHappinessPoor";
    public const string AwardStaffHappinessBonus = "AwardStaffHappinessBonus";
    public const string AwardStaffHappinessPenalty = "AwardStaffHappinessPenalty";
    public const string PeepHappinessAward = "PeepHappinessAward";
    public const string PeepHappinessPoor = "PeepHappinessPoor";
    public const string PeepHappinessBonus = "PeepHappinessBonus";
    public const string PeepHappinessPenalty = "PeepHappinessPenalty";
    public const string WaitingTimesAward = "WaitingTimesAward";
    public const string WaitingTimesPoor = "WaitingTimesPoor";
    public const string WaitingTimesBonus = "WaitingTimesBonus";
    public const string WaitingTimesPenalty = "WaitingTimesPenalty";
    public const string WellKeptTechAward = "WellKeptTechAward";
    public const string WellKeptTechPoor = "WellKeptTechPoor";
    public const string WellKeptTechBonus = "WellKeptTechBonus";
    public const string WellKeptTechPenalty = "WellKeptTechPenalty";
    public const string NewTechAward = "NewTechAward";
    public const string NewTechPoor = "NewTechPoor";
    public const string ResearchBonus = "ResearchBonus";
    public const string ResearchPenalty = "ResearchPenalty";

    /// <summary>CorsixTH's base_config.lua.</summary>
    public static readonly AwardRules Default = new(new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
        [CansofCoke] = 100,
        [CansofCokeBonus] = 1000,
        [Reputation] = 400,
        [TrophyReputationBonus] = 2000,
        [TrophyAllCuredBonus] = 20000,
        [TrophyDeathBonus] = 10000,
        [TrophyCuresBonus] = 6000,
        [TrophyMayorBonus] = 5,
        [CuresAward] = 50,
        [CuresPoor] = 10,
        [DeathsAward] = 10,
        [DeathsPoor] = 25,
        [CuresVDeathsAward] = 5,
        [CuresVDeathsPoor] = 1,
        [ReputationAward] = 500,
        [ReputationPoor] = 200,
        [HospValueAward] = 150000,
        [HospValuePoor] = 50000,
        [CuresBonus] = 2000,
        [CuresPenalty] = -3000,
        [AllCuresBonus] = 5000,
        [DeathsBonus] = 3000,
        [DeathsPenalty] = -5000,
        [CuresVDeathsBonus] = 4000,
        [CuresVDeathsPenalty] = -4000,
        [AwardReputationBonus] = 2000,
        [AwardReputationPenalty] = -3000,
        [HospValueBonus] = 3,
        [HospValuePenalty] = -6,
        [TrophyMayor] = 25,
        [TrophyStaffHappiness] = 85,
        [TrophyStaffHappinessBonus] = 5,
        [RatKillsAbsolute] = 25,
        [RatKillsAbsoluteBonus] = 5,
        [RatKillsPercentage] = 11,
        [RatKillsPercentageBonus] = 5000,
        [Plant] = 80,
        [PlantBonus] = 5,
        [PopulationPercentageAward] = 50,
        [PopulationPercentagePoor] = 15,
        [PopulationPercentageBonus] = 4,
        [PopulationPercentagePenalty] = -5,
        [CleanlinessAward] = 5,
        [CleanlinessPoor] = 40,
        [CleanlinessBonus] = 6,
        [CleanlinessPenalty] = -12,
        [EmergencyAward] = 90,
        [EmergencyPoor] = 50,
        [EmergencyBonus] = 7,
        [EmergencyPenalty] = -11,
        [StaffHappinessAward] = 75,
        [StaffHappinessPoor] = 25,
        [AwardStaffHappinessBonus] = 4,
        [AwardStaffHappinessPenalty] = -10,
        [PeepHappinessAward] = 75,
        [PeepHappinessPoor] = 25,
        [PeepHappinessBonus] = 4,
        [PeepHappinessPenalty] = -9,
        [WaitingTimesAward] = 25,
        [WaitingTimesPoor] = 75,
        [WaitingTimesBonus] = 2,
        [WaitingTimesPenalty] = -6,
        [WellKeptTechAward] = 20,
        [WellKeptTechPoor] = 70,
        [WellKeptTechBonus] = 7,
        [WellKeptTechPenalty] = -5,
        [NewTechAward] = 2000,
        [NewTechPoor] = 0,
        [ResearchBonus] = 5,
        [ResearchPenalty] = -6,
    });

    /// <summary>The settings saved before version 113, in the order they were written.</summary>
    private static readonly string[] OldNames = [.. new SortedSet<string>(StringComparer.Ordinal)
    {
        CansofCoke, CansofCokeBonus, Reputation, TrophyReputationBonus, TrophyAllCuredBonus, TrophyDeathBonus, TrophyCuresBonus,
        TrophyMayorBonus, CuresAward, CuresPoor, DeathsAward, DeathsPoor, CuresVDeathsAward, CuresVDeathsPoor, ReputationAward,
        ReputationPoor, HospValueAward, HospValuePoor, CuresBonus, CuresPenalty, AllCuresBonus, DeathsBonus, DeathsPenalty,
        CuresVDeathsBonus, CuresVDeathsPenalty, AwardReputationBonus, AwardReputationPenalty, HospValueBonus, HospValuePenalty,
    }];

    private readonly SortedDictionary<string, int> _values;

    private AwardRules(SortedDictionary<string, int> values)
    {
        _values = values;
    }

    /// <summary>The names of the settings, in ordinal order.</summary>
    public static IEnumerable<string> Names => Default._values.Keys;

    /// <summary>A setting by its name in the level files.</summary>
    public int this[string name] => _values[name];

    /// <summary>The defaults with the given settings changed; names not in <see cref="Names"/> are ignored.</summary>
    public static AwardRules With(IEnumerable<KeyValuePair<string, int>> values)
    {
        var merged = new SortedDictionary<string, int>(Default._values, StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (merged.ContainsKey(name))
            {
                merged[name] = value;
            }
        }

        return new AwardRules(merged);
    }

    internal void Write(SimWriter writer)
    {
        foreach (var value in _values.Values)
        {
            writer.WriteInt32(value);
        }
    }

    internal static AwardRules Read(SimReader reader)
    {
        // Version 113 has all the settings; older saves kept some, and the rest take their defaults.
        var values = new SortedDictionary<string, int>(Default._values, StringComparer.Ordinal);
        foreach (var name in reader.Version is 0 or >= 113 ? Names : OldNames)
        {
            values[name] = reader.ReadInt32();
        }

        return new AwardRules(values);
    }
}
