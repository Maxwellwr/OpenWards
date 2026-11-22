// Ported from CorsixTH CorsixTH/Lua/hospitals/ai_hospital.lua (AIHospital) and the parts of CorsixTH/Lua/world.lua
// (World:World, World:spawnPatient) and CorsixTH/Lua/hospital.lua (Hospital:countNewPatient) that concern it.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Economy;

/// <summary>
/// A computer opponent a level names (<c>computer[n].Playing</c> and <c>.Name</c> in the level files).
/// </summary>
/// <param name="Number">CorsixTH's competitor number: the level files' index plus one, which picks the default name in
/// <c>competitor_names</c>.</param>
/// <param name="Name">The name the level gives it, which in CorsixTH's base_config.lua every index has.</param>
public readonly record struct PlannedCompetitor(int Number, string Name);

/// <summary>
/// A computer opponent's hospital (CorsixTH's <c>AIHospital</c>). As in CorsixTH it is not on the map, builds nothing
/// and has no staff, and the patients sent to it are counted as its visitors without coming (<c>World:spawnPatient</c>
/// for a hospital without a staffed desk). Unlike CorsixTH it has a reputation, which the patients go by as in the
/// original game (<see cref="PatientAllocationRules"/>).
/// </summary>
/// <remarks>
/// How the original's computer players' reputations develop is not described anywhere, and their level settings are only
/// whether they play and their names. So the reputation is a model, fitted to the computer hospitals of saves of the
/// original (levels 2 and 4 on medium): all start at the level's <c>StartRep</c> like the player, and climb together by
/// about 200 in the first two and a half months (693, 701 and 704 in mid-March on level 4); from then on each settles at a
/// standing of its own, from the start's reputation to 275 above it (516 to 763 in September and December on level 4,
/// 629 to 726 in July on level 2), and stays near it. Each month the reputation moves half of the way towards where it
/// tends (250 above the start in the first three months, then the standing), a little up or down at random. The standing is 50 lower on easy and 50 higher on hard, where the saves do not
/// tell. They charge the standard price for everything, so their reputation for each disease is their reputation.
/// </remarks>
/// </remarks>
public sealed class CompetitorHospital
{
    /// <summary>The longest name read back from a save.</summary>
    private const int MaxNameBytes = 64;

    /// <summary>The lowest standing above the start's reputation.</summary>
    public const int LowestStanding = 0;

    /// <summary>The highest standing above the start's reputation.</summary>
    public const int HighestStanding = 275;

    /// <summary>Where the reputations tend in the first months, above the start's.</summary>
    public const int EarlyStanding = 250;

    /// <summary>The months of the game in which the reputations tend to <see cref="EarlyStanding"/>.</summary>
    public const int EarlyMonths = 3;

    /// <summary>How much lower (easy) or higher (hard) the standing is than on medium.</summary>
    public const int DifficultyStanding = 50;

    /// <summary>The reputation moves this part of the way towards the standing each month.</summary>
    public const int MonthlyApproach = 2;

    /// <summary>The most the reputation moves up or down at random each month.</summary>
    public const int MonthlyWobble = 10;

    /// <param name="number">CorsixTH's competitor number (the level files' index plus one).</param>
    /// <param name="name">Its name.</param>
    /// <param name="reputation">Its reputation at the start.</param>
    /// <param name="standing">The reputation it tends to.</param>
    public CompetitorHospital(int number, string name, int reputation = Hospital.AverageReputation, int standing = Hospital.AverageReputation)
    {
        Number = number;
        Name = name;
        Reputation = Math.Clamp(reputation, Hospital.MinReputation, Hospital.MaxReputation);
        Standing = Math.Clamp(standing, Hospital.MinReputation, Hospital.MaxReputation);
    }

    /// <summary>CorsixTH's competitor number (the level files' index plus one).</summary>
    public int Number { get; }

    /// <summary>Its name, as the level gives it.</summary>
    public string Name { get; }

    /// <summary>Its reputation, from 0 to 1000; also its reputation for every disease.</summary>
    public int Reputation { get; internal set; }

    /// <summary>The reputation it tends to after the first months, drawn at the start.</summary>
    public int Standing { get; private set; }

    /// <summary>The level's starting reputation, from which the reputations climb in the first months.</summary>
    public int StartReputation { get; private set; } = Hospital.AverageReputation;

    /// <summary>Patients sent to it so far, whether they came or not (CorsixTH's <c>spawn_attempts</c>).</summary>
    public long SpawnAttempts { get; internal set; }

    /// <summary>Patients so far (<c>num_visitors</c>).</summary>
    public int Visitors { get; internal set; }

    /// <summary>Patients this year (<c>num_visitors_ty</c>), reset at the end of each year.</summary>
    public int VisitorsThisYear { get; internal set; }

    /// <summary><c>Hospital:countNewPatient</c>: a patient sent to it, counted as a visitor.</summary>
    internal void CountNewPatient()
    {
        SpawnAttempts++;
        Visitors++;
        VisitorsThisYear++;
    }

    /// <summary>A new opponent with the level's starting reputation and a standing drawn for it.</summary>
    internal static CompetitorHospital Create(PlannedCompetitor planned, LevelRules rules, SimRandom random)
    {
        var standing = rules.StartReputation + random.NextInclusive(LowestStanding, HighestStanding) + rules.Difficulty switch
        {
            Difficulty.Easy => -DifficultyStanding,
            Difficulty.Hard => DifficultyStanding,
            _ => 0,
        };
        return new CompetitorHospital(planned.Number, planned.Name, rules.StartReputation, standing) { StartReputation = rules.StartReputation };
    }

    /// <summary>
    /// At the end of each month of the game (from 1) the reputation moves towards where it tends: in the first months to
    /// <see cref="EarlyStanding"/> above the start, later to its standing; and a little at random.
    /// </summary>
    internal void OnEndMonth(int monthOfGame, SimRandom random)
    {
        var target = monthOfGame <= EarlyMonths ? Math.Min(StartReputation + EarlyStanding, Hospital.MaxReputation) : Standing;
        var change = ((target - Reputation) / MonthlyApproach) + random.NextInclusive(-MonthlyWobble, MonthlyWobble);
        Reputation = Math.Clamp(Reputation + change, Hospital.MinReputation, Hospital.MaxReputation);
    }

    /// <summary>
    /// An opponent of an imported save of the original: its reputation and patients, the reputation also its standing
    /// from now on.
    /// </summary>
    internal void Import(int reputation, int visitors)
    {
        Reputation = Math.Clamp(reputation, Hospital.MinReputation, Hospital.MaxReputation);
        Standing = Reputation;
        Visitors = Math.Max(0, visitors);
        VisitorsThisYear = Visitors;
        SpawnAttempts = Visitors;
    }

    /// <summary>The part of <c>Hospital:onEndYear</c> that concerns it.</summary>
    internal void OnEndYear() => VisitorsThisYear = 0;

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Number);
        writer.WriteString(Name);
        writer.WriteInt64(SpawnAttempts);
        writer.WriteInt32(Visitors);
        writer.WriteInt32(VisitorsThisYear);
        writer.WriteInt32(Reputation);
        writer.WriteInt32(Standing);
        writer.WriteInt32(StartReputation);
    }

    /// <param name="reader">The save.</param>
    /// <param name="startReputation">The level's starting reputation, for a game saved before opponents had one.</param>
    internal static CompetitorHospital Read(SimReader reader, int startReputation)
    {
        var number = reader.ReadInt32();
        var name = reader.ReadString(MaxNameBytes);
        var spawnAttempts = reader.ReadInt64();
        var visitors = reader.ReadInt32();
        var visitorsThisYear = reader.ReadInt32();

        // Version 103 gives the opponents a reputation.
        var withReputation = reader.Version is 0 or >= 103;
        var reputation = withReputation ? reader.ReadInt32() : startReputation;
        var standing = withReputation ? reader.ReadInt32() : startReputation;
        var start = withReputation ? reader.ReadInt32() : startReputation;
        return new CompetitorHospital(number, name, reputation, standing)
        {
            StartReputation = start,
            SpawnAttempts = spawnAttempts,
            Visitors = visitors,
            VisitorsThisYear = visitorsThisYear,
        };
    }
}
