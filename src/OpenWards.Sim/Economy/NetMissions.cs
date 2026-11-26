// The network game's missions ("Mini Missions" in the original's NETPLAY.TXT), after the maintainer's specification of the
// game's logic ("Сетевые задания"): the level files' net_criteria, given out and judged at the quarter's end, a failure
// only reported. CorsixTH has no network game, so none of this comes from it.

using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Economy;

/// <summary>What a network mission asks for (the level files' <c>net_criteria[n].Criteria</c>).</summary>
public enum NetCriterion : byte
{
    /// <summary>An empty row.</summary>
    None = 0,

    /// <summary>Handle an emergency successfully.</summary>
    Emergency = 1,

    /// <summary>Have a VIP's visit end without a bad report.</summary>
    Vip = 2,

    /// <summary>Buy a plot of land.</summary>
    BuyPlot = 3,

    /// <summary>Cure the row's value of patients from when the mission is given.</summary>
    Cures = 4,

    /// <summary>Have a balance less the loan of at least the row's value.</summary>
    Money = 5,

    /// <summary>Have a hospital worth at least the row's value.</summary>
    Value = 6,

    /// <summary>Have a reputation of at least the row's value.</summary>
    Reputation = 7,

    /// <summary>Not have a reputation below every other player's.</summary>
    NotLowestReputation = 8,

    /// <summary>Not have a balance less the loan below every other player's.</summary>
    NotLowestMoney = 9,
}

/// <summary>
/// A row of the level's missions (<c>net_criteria[Index].Criteria.Value.Month.TimeToDo</c>): what it asks, its target
/// (for <see cref="NetCriterion.Cures"/> to <see cref="NetCriterion.Reputation"/>), the absolute month it is given out
/// from at the earliest and the months it gives.
/// </summary>
public readonly record struct NetMissionRule(int Index, NetCriterion Criterion, int Value, int Month, int TimeToDo)
{
    /// <summary>The highest row the level files have (<c>net_criteria[0…19]</c>).</summary>
    public const int MaxIndex = 19;

    /// <summary>Whether the row's <see cref="Value"/> counts: only for the missions with a target.</summary>
    public bool HasValue => Criterion is >= NetCriterion.Cures and <= NetCriterion.Reputation;

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Index);
        writer.WriteByte((byte)Criterion);
        writer.WriteInt32(Value);
        writer.WriteInt32(Month);
        writer.WriteInt32(TimeToDo);
    }

    internal static NetMissionRule Read(SimReader reader)
    {
        var index = reader.ReadInt32();
        var criterion = reader.ReadByte();
        return new NetMissionRule(
            index is >= 0 and <= MaxIndex ? index : throw new InvalidDataException($"Network mission row {index} is out of range."),
            criterion is >= 1 and <= (byte)NetCriterion.NotLowestMoney ? (NetCriterion)criterion : throw new InvalidDataException($"Unknown network mission {criterion}."),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32());
    }
}

/// <summary>How a mission was judged: its row, and for each player whether they did it (null for one who had none).</summary>
public sealed record NetMissionResult(int Index, NetCriterion Criterion, int Month, IReadOnlyList<bool?> Passed)
{
    /// <summary>Whether every player with the mission failed it.</summary>
    public bool AllFailed => Passed.All(passed => passed != true);

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Index);
        writer.WriteByte((byte)Criterion);
        writer.WriteInt32(Month);
        writer.WriteInt32(Passed.Count);
        foreach (var passed in Passed)
        {
            writer.WriteByte(passed switch { null => 0, false => 1, true => 2 });
        }
    }

    internal static NetMissionResult Read(SimReader reader, int players)
    {
        var index = reader.ReadInt32();
        var criterion = (NetCriterion)reader.ReadByte();
        var month = reader.ReadInt32();
        var passed = new bool?[reader.ReadCount(players)];
        for (var i = 0; i < passed.Length; i++)
        {
            passed[i] = reader.ReadByte() switch
            {
                0 => null,
                1 => false,
                2 => true,
                var other => throw new InvalidDataException($"Unknown mission result {other}."),
            };
        }

        return new NetMissionResult(index, criterion, month, passed);
    }
}

/// <summary>
/// A player's network mission: the row being played (or none), its deadline, whether its event has happened (for
/// <see cref="NetCriterion.Emergency"/>, <see cref="NetCriterion.Vip"/> and <see cref="NetCriterion.BuyPlot"/>), the
/// patients cured when it was given, and which rows have been given.
/// </summary>
public sealed class NetMission
{
    /// <summary>Missions are given and judged at the end of every third absolute month.</summary>
    public const int QuarterMonths = 3;

    /// <summary>The row being played, or null.</summary>
    public NetMissionRule? Current { get; private set; }

    /// <summary>The absolute month from whose quarter's end on the mission is judged.</summary>
    public int Deadline { get; private set; }

    /// <summary>Whether the mission's event happened after it was given (missions 1 to 3).</summary>
    public bool Done { get; private set; }

    /// <summary>The hospital's cured patients when the mission was given (mission 4).</summary>
    public int CuredAtStart { get; private set; }

    /// <summary>The rows given so far, a bit for each <see cref="NetMissionRule.Index"/>.</summary>
    public int GivenRows { get; private set; }

    /// <summary>
    /// The months left until the deadline, for the mission's text, given the months that have passed (April of the first
    /// year is 3, as the deadline counts from the quarter's end): the row's months to do when the mission has just been given.
    /// </summary>
    public int MonthsLeft(int monthsPassed) => Math.Max(0, Deadline - monthsPassed);

    /// <summary>Notes the event a mission 1 to 3 waits for, if it is the mission being played.</summary>
    internal void Happened(NetCriterion criterion)
    {
        if (Current is { } current && current.Criterion == criterion)
        {
            Done = true;
        }
    }

    /// <summary>
    /// The quarter's end for every player (absolute months 3, 6, 9 …): a mission whose deadline has come is judged, the
    /// players' results are compared and reported, and whoever has no mission is given the first row not given yet that
    /// may come by now. A mission done early waits for its deadline; failing one only brings the message.
    /// </summary>
    internal static void OnEndMonth(Simulation simulation, int month)
    {
        // Only the hospitals a person holds have missions; a computer opponent holding a place counts as a rival.
        var hospitals = simulation.HeldHospitals.ToList();
        if (month % QuarterMonths != 0 || hospitals.Count + StandIns(simulation).Count() < 2 || simulation.Rules.NetMissions.Count == 0)
        {
            return;
        }

        // Every result is reckoned before any mission changes, since the last two compare the players.
        var passed = new bool?[simulation.Hospitals.Count];
        NetMissionRule? judged = null;
        foreach (var hospital in hospitals)
        {
            if (hospital.NetMission.Current is { } current && month >= hospital.NetMission.Deadline)
            {
                passed[hospital.Player] = Passed(simulation, hospital, current);
                judged ??= current;
            }
        }

        foreach (var hospital in hospitals)
        {
            var mission = hospital.NetMission;
            if (passed[hospital.Player] is not null)
            {
                mission.Current = null;
            }

            if (mission.Current is null)
            {
                mission.Give(simulation.Rules.NetMissions, month, hospital.Cured);
            }
        }

        if (judged is { } row)
        {
            simulation.LastNetMission = new NetMissionResult(row.Index, row.Criterion, month, passed);
        }
    }

    /// <summary>Whether the player has done their mission now.</summary>
    internal static bool Passed(Simulation simulation, Hospital hospital, NetMissionRule mission)
    {
        var others = simulation.HeldHospitals.Where(other => other.Player != hospital.Player).ToList();
        var standIns = StandIns(simulation).ToList();
        return mission.Criterion switch
        {
            NetCriterion.Emergency or NetCriterion.Vip or NetCriterion.BuyPlot => hospital.NetMission.Done,
            NetCriterion.Cures => hospital.Cured - hospital.NetMission.CuredAtStart >= mission.Value,
            NetCriterion.Money => hospital.Balance - hospital.Loan >= mission.Value,
            NetCriterion.Value => hospital.Value >= mission.Value,
            NetCriterion.Reputation => hospital.Reputation >= mission.Value,

            // Last only below every other player: level with anyone is enough.
            NetCriterion.NotLowestReputation => others.Count + standIns.Count == 0
                || others.Any(other => hospital.Reputation >= other.Reputation) || standIns.Any(other => hospital.Reputation >= other.Reputation),
            NetCriterion.NotLowestMoney => others.Count + standIns.Count == 0
                || others.Any(other => hospital.Balance - hospital.Loan >= other.Balance - other.Loan) || standIns.Any(other => hospital.Balance - hospital.Loan >= other.Balance),
            _ => true,
        };
    }

    /// <summary>The computer opponents holding places no person holds (<see cref="Simulation.SetSeat"/>).</summary>
    private static IEnumerable<CompetitorHospital> StandIns(Simulation simulation) => simulation.Competitors.Where(competitor => competitor.Seat >= 0);

    /// <summary>Gives the first row not given yet whose month has come, if any.</summary>
    private void Give(IReadOnlyList<NetMissionRule> rows, int month, int cured)
    {
        foreach (var row in rows)
        {
            if ((GivenRows & (1 << row.Index)) == 0 && row.Month <= month)
            {
                GivenRows |= 1 << row.Index;
                Current = row;
                Deadline = month + row.TimeToDo;
                Done = false;
                CuredAtStart = cured;
                return;
            }
        }
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteBool(Current is not null);
        Current?.Write(writer);
        writer.WriteInt32(Deadline);
        writer.WriteBool(Done);
        writer.WriteInt32(CuredAtStart);
        writer.WriteInt32(GivenRows);
    }

    internal static NetMission Read(SimReader reader)
    {
        var mission = new NetMission();
        mission.Current = reader.ReadBool() ? NetMissionRule.Read(reader) : null;
        mission.Deadline = reader.ReadInt32();
        mission.Done = reader.ReadBool();
        mission.CuredAtStart = reader.ReadInt32();
        mission.GivenRows = reader.ReadInt32();
        return mission;
    }
}
