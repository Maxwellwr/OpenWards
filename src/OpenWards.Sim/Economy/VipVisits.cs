// VIP visits. The visit's walk and the faxes started from CorsixTH CorsixTH/Lua/world.lua (spawnVIP),
// hospitals/player_hospital.lua (PlayerHospital:createVip) and dialogs/fullscreen/fax.lua (UIFax:choice for accept_vip
// and refuse_vip); math.t_random is app.lua's. When a VIP comes and how the visit is scored follow the community's
// description of the original game (the logic specification), not CorsixTH.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Calendar;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Economy;

/// <summary>Where a hospital's VIP visit stands (the original game's states 0 to 4).</summary>
public enum VipState : byte
{
    /// <summary>No visit: the days since the last VIP count up.</summary>
    None = 0,

    /// <summary>A fax asks whether the VIP may come.</summary>
    Invited = 1,

    /// <summary>The VIP accepted and is on the way.</summary>
    Travelling = 2,

    /// <summary>The VIP is in the hospital.</summary>
    Visiting = 3,

    /// <summary>The visit is over; the next comes when the timer runs out.</summary>
    Over = 4,
}

/// <summary>
/// A hospital's VIPs. A VIP asks to visit strictly by the timer: <see cref="LevelRules.MayorLaunch"/> days after the
/// last one asked, once the hospital has a reception desk. Refusing costs nothing; accepting (or leaving the fax
/// unanswered) brings the VIP <see cref="TravelTicks"/> ticks later. On leaving they score the whole hospital, the lower
/// the better, and the score decides the grant and the reputation.
/// </summary>
/// <remarks>
/// CorsixTH draws each visit's date from a triangle 30 days around <c>MayorLaunch</c>, needs a room and a staffed desk,
/// lets a VIP refused three times come anyway one time in two, and rates the visit from 1 to 15 by what the VIP saw on
/// the way (litter, deaths, cures, queues, the rooms' furniture). Reputation does not change how often VIPs come.
/// </remarks>
public sealed class VipVisits
{
    /// <summary>
    /// How many VIP names there are, numbered 0 to 10 in the order of the game's VIP list: 0 the Minister for Health,
    /// 1 the Mayor of Greater Trumpton, and so on. The number is kept in saves and faxes, so the order stays.
    /// </summary>
    public const int NameCount = 11;

    /// <summary>The <see cref="InvitedName"/> when there is no VIP.</summary>
    public const int NoName = -1;

    /// <summary>The fax asking accepts the VIP after this many days (CorsixTH's <c>Date.hoursPerDay() * 20</c>).</summary>
    public const int InvitationDays = 20;

    /// <summary>Ticks from accepting to the VIP's arrival (about 30 days).</summary>
    public const int TravelTicks = 1500;

    /// <summary>The worst score that still raises reputation.</summary>
    public const int PleasedScore = 3;

    /// <summary>A good VIP report brings this many percent of the visits in reputation points (about +20).</summary>
    public const int GoodReputationPercent = 4;

    /// <summary>A middling VIP report brings this many percent of the visits (about +10).</summary>
    public const int MiddlingReputationPercent = 2;

    /// <summary>A bad VIP report costs this many percent of the visits (about −20).</summary>
    public const int BadReputationPercent = -4;

    /// <summary>What every VIP visit brings in free build mode (CorsixTH's <c>Vip:onDestroy</c>).</summary>
    public const int FreeBuildReputationReward = 20;

    /// <summary>The grant by score, from −5 or less up to 1; none from 2 on.</summary>
    private static readonly int[] Grants = [4000, 2000, 1500, 1200, 800, 400, 200];

    /// <summary>The lowest score with a grant of its own; anything lower counts as this.</summary>
    private const int BestScore = -5;

    /// <summary>The visit's state.</summary>
    public VipState State { get; internal set; }

    /// <summary>Days since a VIP last asked to visit (or since the game began).</summary>
    public int DaysSinceLast { get; internal set; }

    /// <summary>
    /// The name (0 to <see cref="NameCount"/> − 1) of the VIP asking, on the way or visiting, or <see cref="NoName"/> when
    /// there is none.
    /// </summary>
    public int InvitedName { get; internal set; } = NoName;

    /// <summary>The tick at which the fax asking accepts the VIP by itself.</summary>
    public long InvitationExpires { get; internal set; }

    /// <summary>The tick at which an accepted VIP arrives.</summary>
    public long ArrivalTick { get; internal set; }

    /// <summary>VIPs who came.</summary>
    public int Count { get; internal set; }

    /// <summary>VIPs who made their report this year.</summary>
    public int ThisYear { get; internal set; }

    /// <summary>VIPs whose report raised reputation this year (a score up to <see cref="PleasedScore"/>).</summary>
    public int PleasedThisYear { get; internal set; }

    /// <summary>The VIPs' scores this year added up, for the year's mayor trophy.</summary>
    public int ScoreSumThisYear { get; internal set; }

    /// <summary>Whether a VIP is asking to visit.</summary>
    public bool IsInvitationPending => State == VipState.Invited;

    /// <summary>
    /// The last visit's report (CorsixTH's <c>PlayerHospital:makeVipEndFax</c>), kept for the fax until it expires 20
    /// days after the VIP left, or null.
    /// </summary>
    public VipReport? Report { get; internal set; }

    /// <summary>The grant a score brings: $4,000 for −5 or less, down to $200 for 1, nothing from 2 on.</summary>
    public static int Grant(int score) => score > 1 ? 0 : Grants[Math.Max(score, BestScore) - BestScore];

    /// <summary>The reputation a score brings in percent of the visits: +4 up to −2, +2 up to 3, −4 from 4 on.</summary>
    public static int ReputationPercent(int score) =>
        score <= -2 ? GoodReputationPercent : score <= PleasedScore ? MiddlingReputationPercent : BadReputationPercent;

    /// <summary>
    /// <c>math.t_random(a, c, b)</c>: a number from <paramref name="a"/> to <paramref name="b"/>, most likely near
    /// <paramref name="c"/>.
    /// </summary>
    internal static decimal TriangularRandom(SimRandom random, decimal a, decimal c, decimal b)
    {
        var range = b - a;
        var peak = (c - a) / range;
        var u = random.Next(1_000_000) / 1_000_000m;
        return u <= peak
            ? a + (DecimalMath.Sqrt(peak * u) * range)
            : a + ((1 - DecimalMath.Sqrt((1 - peak) * (1 - u))) * range);
    }

    /// <summary>
    /// The end of a day: the days since the last VIP count up, and once they reach <see cref="LevelRules.MayorLaunch"/>
    /// with no visit under way and a reception desk in the hospital, a VIP asks to come. A visit whose VIP is gone from
    /// the map some other way is over.
    /// </summary>
    internal static void OnEndDay(Simulation simulation, Hospital hospital)
    {
        var vips = hospital.Vips;
        vips.DaysSinceLast++;
        if (vips.State == VipState.Visiting && !IsVipOnMap(simulation, hospital.Player))
        {
            vips.State = VipState.Over;
            vips.InvitedName = NoName;
        }

        var shared = Disasters.Emergencies.IsShared(simulation);
        if (shared && hospital.Player != 0)
        {
            return;
        }

        if (vips.State is not (VipState.None or VipState.Over) || vips.DaysSinceLast < Math.Max(simulation.Rules.MayorLaunch, 1))
        {
            return;
        }

        if (!shared)
        {
            if (HasDesk(simulation, hospital.Player))
            {
                Invite(simulation, hospital);
            }

            return;
        }

        // The network game: the VIP asks every hospital that could receive them at once, by the first hospital's timer;
        // the first to accept receives the visit (NETPLAY.TXT, "Emergencies & VIP Visits").
        int? name = null;
        foreach (var each in simulation.Hospitals)
        {
            if (each.Vips.State is (VipState.None or VipState.Over) && HasDesk(simulation, each.Player))
            {
                if (name is null)
                {
                    Invite(simulation, each);
                    name = each.Vips.InvitedName;
                }
                else
                {
                    each.Vips.State = VipState.Invited;
                    each.Vips.InvitedName = name.Value;
                    each.Vips.InvitationExpires = vips.InvitationExpires;
                }
            }
        }

        if (name is not null)
        {
            vips.DaysSinceLast = 0;
        }
    }

    /// <summary>A fax from one of the VIPs asks to visit, and the timer starts again.</summary>
    internal static void Invite(Simulation simulation, Hospital hospital)
    {
        var vips = hospital.Vips;
        vips.State = VipState.Invited;
        vips.DaysSinceLast = 0;
        vips.InvitedName = simulation.Random.Next(NameCount);
        vips.InvitationExpires = simulation.Tick + (InvitationDays * GameDate.HoursPerDay);
    }

    /// <summary>The fax runs out and takes its default choice, inviting the VIP; an invited VIP arrives on time.</summary>
    internal static void TickInvitation(Simulation simulation, Hospital hospital)
    {
        var vips = hospital.Vips;
        if (vips.IsInvitationPending && simulation.Tick >= vips.InvitationExpires)
        {
            Answer(simulation, hospital.Player, accept: true);
        }
        else if (vips.State == VipState.Travelling && simulation.Tick >= vips.ArrivalTick)
        {
            vips.Count++;
            vips.State = Spawn(simulation, hospital, vips.InvitedName) is null ? VipState.Over : VipState.Visiting;
        }
    }

    /// <summary>
    /// The fax's answer: an invited VIP sets off and arrives <see cref="TravelTicks"/> ticks later; a refused one does
    /// not come, with no other consequence.
    /// </summary>
    internal static void Answer(Simulation simulation, int player, bool accept)
    {
        if (player < 0 || player >= simulation.Hospitals.Count || !simulation.Hospitals[player].Vips.IsInvitationPending)
        {
            return;
        }

        var vips = simulation.Hospitals[player].Vips;
        if (Disasters.Emergencies.IsShared(simulation) && accept)
        {
            // First come, first served: the first to accept receives the VIP and the others' invitations go.
            foreach (var other in simulation.Hospitals)
            {
                if (other.Player != player && other.Vips.IsInvitationPending && other.Vips.InvitedName == vips.InvitedName
                    && other.Vips.InvitationExpires == vips.InvitationExpires)
                {
                    other.Vips.State = VipState.None;
                    other.Vips.InvitedName = NoName;
                }
            }
        }

        if (accept)
        {
            vips.State = VipState.Travelling;
            vips.ArrivalTick = simulation.Tick + TravelTicks;
        }
        else
        {
            vips.State = VipState.None;
            vips.InvitedName = NoName;
        }
    }

    /// <summary>
    /// CorsixTH's <c>World:spawnVIP</c>: the VIP comes in at a random spawn point and heads for reception. Without a
    /// spawn point the visit is off.
    /// </summary>
    internal static Humanoid? Spawn(Simulation simulation, Hospital hospital, int name)
    {
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        if (points.Count == 0)
        {
            return null;
        }

        var spawn = points[simulation.Random.Next(points.Count)];
        var vip = simulation.AddEntity(id => Humanoid.CreateVip(id, spawn, hospital.Player));
        vip.Behaviour = new VipBehaviour(name);
        return vip;
    }

    /// <summary>
    /// Off the hospital grounds, the VIP's report: the grant for the score and the reputation in percent of the visits;
    /// in free build mode every visit brings 20 reputation and nothing else. The visit is over.
    /// </summary>
    internal static void OnVipLeft(Simulation simulation, Hospital hospital, VipBehaviour vip)
    {
        var vips = hospital.Vips;
        var grant = 0;
        var percent = 0;
        if (simulation.Rules.FreeBuild)
        {
            hospital.ShiftReputation(FreeBuildReputationReward);
        }
        else
        {
            grant = Grant(vip.Score);
            percent = ReputationPercent(vip.Score);
            if (grant > 0)
            {
                hospital.Receive(simulation.Hours, grant, TransactionKind.VipAward, string.Empty);
            }

            hospital.ChangeReputationPercent(percent);
            vips.ScoreSumThisYear += vip.Score;
            if (vip.Score <= PleasedScore)
            {
                vips.PleasedThisYear++;
            }
        }

        vips.ThisYear++;
        vips.Report = new VipReport(vip.Name, vip.Score, grant, percent, simulation.Tick + (InvitationDays * GameDate.HoursPerDay));
        if (vips.State == VipState.Visiting)
        {
            vips.State = VipState.Over;
            vips.InvitedName = NoName;
        }
    }

    /// <summary>Forgets the visit's report once its fax has expired.</summary>
    internal static void TickReport(Simulation simulation, Hospital hospital)
    {
        if (hospital.Vips.Report is { } report && simulation.Tick >= report.Expires)
        {
            hospital.Vips.Report = null;
        }
    }

    /// <summary>The rooms of a hospital, built or not, in the order they were made (CorsixTH's <c>world.rooms</c>).</summary>
    internal static IEnumerable<Room> RoomsOf(Simulation simulation, int player)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Room room && RoomVisits.Owner(simulation, room) == player)
            {
                yield return room;
            }
        }
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteByte((byte)State);
        writer.WriteInt32(DaysSinceLast);
        writer.WriteInt32(InvitedName);
        writer.WriteInt64(InvitationExpires);
        writer.WriteInt64(ArrivalTick);
        writer.WriteInt32(Count);
        writer.WriteInt32(ThisYear);
        writer.WriteInt32(PleasedThisYear);
        writer.WriteInt32(ScoreSumThisYear);
        writer.WriteBool(Report is not null);
        if (Report is { } report)
        {
            writer.WriteInt32(report.Name);
            writer.WriteInt32(report.Score);
            writer.WriteInt32(report.Grant);
            writer.WriteInt32(report.ReputationPercent);
            writer.WriteInt64(report.Expires);
        }
    }

    internal static VipVisits Read(SimReader reader)
    {
        var visits = reader.Version is 0 or >= SaveVersion ? ReadCurrent(reader) : ReadBeforeTimer(reader);
        return visits.InvitedName is >= NoName and < NameCount && visits.State <= VipState.Over
            && visits.Report is not { Name: < 0 or >= NameCount }
            ? visits
            : throw new InvalidDataException($"Unknown VIP name {visits.InvitedName} or state {visits.State}.");
    }

    /// <summary>The save version that brought the VIP timer and scores.</summary>
    internal const int SaveVersion = 110;

    /// <summary>The save version from which the names count from 0 in the game's order (before: 1 to 10, 11 the minister, 0 none).</summary>
    internal const int NamesFromZeroVersion = 116;

    /// <summary>A name from an older save: 0 was none, 1 to 10 are as now, 11 was the Minister for Health, now 0.</summary>
    internal static int NameFromSave(SimReader reader, int name) =>
        reader.Version is 0 or >= NamesFromZeroVersion ? name : name switch
        {
            0 => NoName,
            NameCount => 0,
            _ => name,
        };

    private static VipVisits ReadCurrent(SimReader reader)
    {
        var visits = new VipVisits
        {
            State = (VipState)reader.ReadByte(),
            DaysSinceLast = reader.ReadInt32(),
            InvitedName = NameFromSave(reader, reader.ReadInt32()),
            InvitationExpires = reader.ReadInt64(),
            ArrivalTick = reader.ReadInt64(),
            Count = reader.ReadInt32(),
            ThisYear = reader.ReadInt32(),
            PleasedThisYear = reader.ReadInt32(),
            ScoreSumThisYear = reader.ReadInt32(),
        };
        if (reader.ReadBool())
        {
            visits.Report = new VipReport(NameFromSave(reader, reader.ReadInt32()), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64());
        }

        return visits;
    }

    /// <summary>
    /// A save from before the timer: the next VIP's day and the refusals are dropped and the timer starts afresh; a
    /// report's rating from 1 to 15 becomes the score its remark stands for.
    /// </summary>
    private static VipVisits ReadBeforeTimer(SimReader reader)
    {
        _ = reader.ReadInt64();
        var name = reader.ReadInt32();
        var expires = reader.ReadInt64();
        _ = reader.ReadInt32();
        var visits = new VipVisits
        {
            State = name != 0 ? VipState.Invited : VipState.None,
            InvitedName = NameFromSave(reader, name),
            InvitationExpires = expires,
            Count = reader.ReadInt32(),
            ThisYear = reader.ReadInt32(),
            PleasedThisYear = reader.ReadInt32(),
        };
        if ((reader.Version >= 80) && reader.ReadBool())
        {
            var reportName = NameFromSave(reader, reader.ReadInt32());
            var score = VipReport.ScoreOfRemark(reader.ReadInt32());
            var grant = reader.ReadInt32();
            visits.Report = new VipReport(reportName, score, grant, ReputationPercent(score), reader.ReadInt64());
        }

        return visits;
    }

    private static bool HasDesk(Simulation simulation, int player) => ReceptionDesks.OfHospital(simulation, player, staffedOnly: false).Any();

    private static bool IsVipOnMap(Simulation simulation, int player)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: VipBehaviour, IsDespawning: false } vip && vip.HospitalPlayer == player)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A VIP's report on their visit: who, their score (the lower the better), the grant, the reputation in percent of the
/// visits, and when the fax expires.
/// </summary>
public readonly record struct VipReport(int Name, int Score, int Grant, int ReputationPercent, long Expires)
{
    /// <summary>How many remarks there are for a VIP's report (the original's strings 44, 196 to 210).</summary>
    public const int RemarkCount = 15;

    /// <summary>
    /// Which of the 15 remarks, from 1 (glowing) to 15 (scathing), fits the score: −5 or less is 1, 3 is 9, and the
    /// scores that cost reputation start at 11.
    /// </summary>
    public int Remark => Score <= VipVisits.PleasedScore ? Math.Clamp(Score + 6, 1, 9) : Math.Clamp(Score + 7, 11, RemarkCount);

    /// <summary>The score a remark from an older save stands for.</summary>
    internal static int ScoreOfRemark(int remark) => remark <= 9 ? remark - 6 : remark - 7;
}
