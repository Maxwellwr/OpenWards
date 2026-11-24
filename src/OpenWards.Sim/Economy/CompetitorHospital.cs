// Ported from CorsixTH CorsixTH/Lua/hospitals/ai_hospital.lua (AIHospital) and the parts of CorsixTH/Lua/world.lua
// (World:World, World:spawnPatient) and CorsixTH/Lua/hospital.lua (Hospital:countNewPatient) that concern it.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Import;
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
/// <para>
/// The year's charts (the annual report) rank them with the players by money, salary, cures, deaths, visitors and value,
/// which the same saves hold for them too; how those develop is a model as well. Of the patients sent to an opponent a
/// share is cured and a share dies at once, by its reputation: 54 % cured and 3 % dead at 532, 74 % and none at 763 on
/// level 4, 75 % to 79 % on level 2. Its money starts at the level's and each month earns $400 per patient cured less
/// $4,000 of running costs (from $50,000 to $72,000 to $126,000 by mid-March and $24,000 to $51,000 by December on
/// level 4). Its value is $100 per tile of the plot the players start on (all the opponents have one that size) plus $10
/// per point of its standing above the start: $51,660 to $55,660 on level 4 with 520 tiles. Its owner's salary starts
/// at $10,000 like the player's and like it stays the same all year and rises by a few percent at its end: $11,130 and
/// $11,236 in the third year of level 4 are $10,000 raised by 5 % and 6 %, and by 6 % twice. At the end of the year
/// the value rises by $2,030, as the original's charts of the year show.
/// </para>
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

    /// <summary>Patients cured in a thousand, beside <see cref="CurePerReputation"/> per point of reputation.</summary>
    public const int CureBase = 100;

    /// <summary>Patients cured in a thousand per point of reputation, over <see cref="CureDivisor"/>.</summary>
    public const int CurePerReputation = 4;

    /// <summary>The divisor of <see cref="CurePerReputation"/>.</summary>
    public const int CureDivisor = 5;

    /// <summary>At this reputation and above no patient dies.</summary>
    public const int DeathlessReputation = 750;

    /// <summary>Patients dying in a thousand: the reputation below <see cref="DeathlessReputation"/> over this.</summary>
    public const int DeathDivisor = 6;

    /// <summary>What a patient cured earns.</summary>
    public const int CureIncome = 400;

    /// <summary>The running costs of a month.</summary>
    public const int MonthlyCosts = 4000;

    /// <summary>The value of a tile of its plot.</summary>
    public const int ValuePerTile = 100;

    /// <summary>The value of a point of standing above the start's reputation.</summary>
    public const int ValuePerStanding = 10;

    /// <summary>The value moves this part of the way towards where it tends each month.</summary>
    public const int MonthlyValueApproach = 12;

    /// <summary>How much its value rises at the end of a year.</summary>
    public const int YearEndValueRise = 2030;

    /// <summary>The owner's salary at the start, as the player's.</summary>
    public const long StartSalary = Hospital.StartPlayerSalary;

    /// <summary>The share of a patient cured or dead carried over to the next, in thousandths.</summary>
    private int _cureCredit;

    private int _deathCredit;

    /// <param name="number">CorsixTH's competitor number (the level files' index plus one).</param>
    /// <param name="name">Its name.</param>
    /// <param name="reputation">Its reputation at the start.</param>
    /// <param name="standing">The reputation it tends to.</param>
    /// <param name="balance">Its money at the start.</param>
    /// <param name="tiles">The tiles of its plot, as big as the one the players start on.</param>
    public CompetitorHospital(int number, string name, int reputation = Hospital.AverageReputation, int standing = Hospital.AverageReputation, long balance = 0, int tiles = 0)
    {
        Number = number;
        Name = name;
        Reputation = Math.Clamp(reputation, Hospital.MinReputation, Hospital.MaxReputation);
        Standing = Math.Clamp(standing, Hospital.MinReputation, Hospital.MaxReputation);
        Balance = balance;
        Tiles = Math.Max(0, tiles);
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

    /// <summary>Its money; it never borrows.</summary>
    public long Balance { get; private set; }

    /// <summary>Its value, 0 until the end of the first month as in the original's saves.</summary>
    public long Value { get; private set; }

    /// <summary>Patients cured so far.</summary>
    public int Cured { get; private set; }

    /// <summary>Patients who died so far.</summary>
    public int Deaths { get; private set; }

    /// <summary>Patients cured this month, for its money.</summary>
    public int CuredThisMonth { get; private set; }

    /// <summary>Its owner's salary, the "Salary" of the year's charts.</summary>
    public long PlayerSalary { get; private set; } = StartSalary;

    /// <summary>The tiles of its plot.</summary>
    public int Tiles { get; private set; }

    /// <summary>Patients cured in a thousand at its reputation.</summary>
    public int CurePermille => Math.Clamp(CureBase + (Reputation * CurePerReputation / CureDivisor), 0, 1000);

    /// <summary>Patients dying in a thousand at its reputation.</summary>
    public int DeathPermille => Math.Clamp((DeathlessReputation - Reputation) / DeathDivisor, 0, 1000 - CurePermille);

    /// <summary>The value it tends to.</summary>
    public long TargetValue => ((long)Tiles * ValuePerTile) + ((long)Math.Max(0, Standing - StartReputation) * ValuePerStanding);

    /// <summary>
    /// <c>Hospital:countNewPatient</c>: a patient sent to it, counted as a visitor; and cured, dead or sent home at once,
    /// so that over the patients the shares are those of its reputation (no random draw, so the game's run of random
    /// numbers stays as it was).
    /// </summary>
    internal void CountNewPatient()
    {
        SpawnAttempts++;
        Visitors++;
        VisitorsThisYear++;
        _cureCredit += CurePermille;
        _deathCredit += DeathPermille;
        if (_cureCredit >= 1000)
        {
            _cureCredit -= 1000;
            Cured++;
            CuredThisMonth++;
        }
        else if (_deathCredit >= 1000)
        {
            _deathCredit -= 1000;
            Deaths++;
        }
    }

    /// <summary>A new opponent with the level's starting reputation and a standing drawn for it.</summary>
    /// <param name="planned">The level's opponent.</param>
    /// <param name="rules">The level's settings.</param>
    /// <param name="random">The game's random numbers.</param>
    /// <param name="tiles">The tiles of the plot the players start on.</param>
    internal static CompetitorHospital Create(PlannedCompetitor planned, LevelRules rules, SimRandom random, int tiles)
    {
        var standing = rules.StartReputation + random.NextInclusive(LowestStanding, HighestStanding) + rules.Difficulty switch
        {
            Difficulty.Easy => -DifficultyStanding,
            Difficulty.Hard => DifficultyStanding,
            _ => 0,
        };
        return new CompetitorHospital(planned.Number, planned.Name, rules.StartReputation, standing, rules.StartCash, tiles) { StartReputation = rules.StartReputation };
    }

    /// <summary>
    /// At the end of each month of the game (from 1) the reputation moves towards where it tends: in the first months to
    /// <see cref="EarlyStanding"/> above the start, later to its standing; and a little at random. It is paid for the
    /// month's cures and pays its running costs, and its value is set (or moves towards where it tends).
    /// </summary>
    /// <param name="monthOfGame">The month that ends, from 1.</param>
    /// <param name="random">The game's random numbers.</param>
    internal void OnEndMonth(int monthOfGame, SimRandom random)
    {
        var target = monthOfGame <= EarlyMonths ? Math.Min(StartReputation + EarlyStanding, Hospital.MaxReputation) : Standing;
        var change = ((target - Reputation) / MonthlyApproach) + random.NextInclusive(-MonthlyWobble, MonthlyWobble);
        Reputation = Math.Clamp(Reputation + change, Hospital.MinReputation, Hospital.MaxReputation);

        Balance += ((long)CuredThisMonth * CureIncome) - MonthlyCosts;
        Value = Value == 0 ? TargetValue : Value + ((TargetValue - Value) / MonthlyValueApproach);
        CuredThisMonth = 0;
    }

    /// <summary>
    /// An opponent of an imported save of the original: its reputation and patients, the reputation also its standing
    /// from now on, and what the year's charts show of it.
    /// </summary>
    internal void Import(ImportedCompetitor state)
    {
        Reputation = Math.Clamp(state.Reputation, Hospital.MinReputation, Hospital.MaxReputation);
        Standing = Reputation;
        Visitors = Math.Max(0, state.Visitors);
        VisitorsThisYear = state.VisitorsThisYear is { } thisYear ? Math.Clamp(thisYear, 0, Visitors) : Visitors;
        SpawnAttempts = Visitors;
        Balance = state.Money ?? Balance;
        Value = Math.Max(0, state.Value ?? Value);
        Cured = Math.Max(0, state.Cured ?? Cured);
        Deaths = Math.Max(0, state.Deaths ?? Deaths);
        PlayerSalary = state.Salary is > 0 and var salary ? salary : PlayerSalary;
        if (state.Tiles is > 0 and var tiles)
        {
            Tiles = tiles;
        }
    }

    /// <summary>
    /// The year end: its owner's salary rises as the player's does (<see cref="Hospital.RaisedSalary(long, SimRandom)"/>:
    /// 12,247, 11,910 and 11,797 on 1 January from 11,236, 11,236 and 11,130 on level 4) and its value by $2,030 (53,690
    /// from 51,660; the others 55,690 from 54,660 and 58,690 from 55,660, a thousand less and more), and the year's
    /// patients start afresh.
    /// </summary>
    internal void OnEndYear(int score, int scoreMaxIncrease)
    {
        VisitorsThisYear = 0;
        PlayerSalary = Hospital.RaisedSalary(PlayerSalary, score, StartSalary, scoreMaxIncrease);
        if (Value > 0)
        {
            Value += YearEndValueRise;
        }
    }

    /// <summary>
    /// What a win criterion measures for the computer player, for its level score: reputation, balance, the cured and
    /// killed shares of its visitors, cures and value; nothing for the rest.
    /// </summary>
    internal decimal? Measure(EndCriterion criterion) => criterion switch
    {
        EndCriterion.Reputation => Reputation,
        EndCriterion.Balance => Balance,
        EndCriterion.PercentageCured => Visitors > 0 ? Cured * 100m / Visitors : 0,
        EndCriterion.NumCured => Cured,
        EndCriterion.PercentageKilled => Visitors > 0 ? Deaths * 100m / Visitors : 0,
        EndCriterion.Value => Value,
        _ => null,
    };

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
        writer.WriteInt64(Balance);
        writer.WriteInt64(Value);
        writer.WriteInt32(Cured);
        writer.WriteInt32(Deaths);
        writer.WriteInt32(CuredThisMonth);
        writer.WriteInt64(PlayerSalary);
        writer.WriteInt32(Tiles);
        writer.WriteInt32(_cureCredit);
        writer.WriteInt32(_deathCredit);
    }

    /// <param name="reader">The save.</param>
    /// <param name="startReputation">The level's starting reputation, for a game saved before opponents had one.</param>
    /// <param name="startCash">The level's starting money, for a game saved before opponents had any.</param>
    /// <param name="tiles">The tiles of the plot the players start on, for a game saved before opponents had a plot.</param>
    internal static CompetitorHospital Read(SimReader reader, int startReputation, long startCash, int tiles)
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
        var competitor = new CompetitorHospital(number, name, reputation, standing, startCash, tiles)
        {
            StartReputation = start,
            SpawnAttempts = spawnAttempts,
            Visitors = visitors,
            VisitorsThisYear = visitorsThisYear,
        };

        // Version 104 gives them what the year's charts show.
        if (reader.Version is 0 or >= 104)
        {
            competitor.Balance = reader.ReadInt64();
            competitor.Value = reader.ReadInt64();
            competitor.Cured = reader.ReadInt32();
            competitor.Deaths = reader.ReadInt32();
            competitor.CuredThisMonth = reader.ReadInt32();
            competitor.PlayerSalary = reader.ReadInt64();
            competitor.Tiles = Math.Max(0, reader.ReadInt32());
            competitor._cureCredit = Math.Clamp(reader.ReadInt32(), 0, 999);
            competitor._deathCredit = Math.Clamp(reader.ReadInt32(), 0, 999);
        }

        return competitor;
    }
}
