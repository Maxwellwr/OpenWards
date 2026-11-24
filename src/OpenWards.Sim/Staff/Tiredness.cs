// How staff tire, after the community's logic specification; going to the staff room and walking speed ported from
// CorsixTH CorsixTH/Lua/entities/humanoids/staff.lua (checkIfNeedRest, isVeryTired, isCrackUpTired, updateSpeed).
// The thresholds' defaults are the original game's.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Staff;

/// <summary>
/// The level's tiredness thresholds in thousandths of fatigue: tired enough to want the staff room (the hospital's
/// <c>goto_staffroom</c> policy, <c>gbv.Tired</c>), very tired (<c>gbv.VeryTired</c>), so tired they could crack up
/// (<c>gbv.CrackUpTired</c>), and below which a doctor counts as rested in a diagnosis step (<c>gbv.NotTired</c>).
/// </summary>
/// <remarks>
/// The difficulty files set them (easy, medium, hard): NotTired 400 / 300 / 200, Tired 700 / 600 / 600, VeryTired
/// 800 / 700 / 700, CrackUpTired 900 / 800 / 800. <see cref="Default"/> is the medium one, for rules made without level
/// files; <see cref="Unset"/> is what the original game uses for a threshold its files leave out, as the maintainer
/// describes it.
/// </remarks>
public sealed record TirednessRules(int Tired, int VeryTired, int CrackUpTired, int NotTired = TirednessRules.UnsetNotTired)
{
    /// <summary>How fast staff tire at work and rest in the staff room; by default the medium difficulty's.</summary>
    public RestRules Rest { get; init; } = RestRules.Default;

    /// <summary>The original game's <c>gbv.NotTired</c> when the level files set none.</summary>
    public const int UnsetNotTired = 400;

    /// <summary>The medium difficulty's thresholds (<c>FULL00.SAM</c>): Tired 600, VeryTired 700, CrackUpTired 800, NotTired 300.</summary>
    public static readonly TirednessRules Default = new(600, 700, 800, 300);

    /// <summary>The original game's thresholds when the level files set none: Tired 800, VeryTired 1200, CrackUpTired 3000, NotTired 400.</summary>
    public static readonly TirednessRules Unset = new(800, 1200, 3000, UnsetNotTired) { Rest = RestRules.Unset };

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Tired);
        writer.WriteInt32(VeryTired);
        writer.WriteInt32(CrackUpTired);
        writer.WriteInt32(NotTired);
        Rest.Write(writer);
    }

    internal static TirednessRules Read(SimReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.Version is 0 or >= 107 ? reader.ReadInt32() : Default.NotTired)
        {
            Rest = reader.Version is 0 or >= 113 ? RestRules.Read(reader) : RestRules.Default,
        };
}

/// <summary>
/// How fast staff tire and rest, in thousandths of fatigue: work adds <paramref name="WorkLight"/> every
/// <see cref="People.PatientNeeds.ModifyFreq"/> ticks (<c>gbv.WorkLight</c>); in the staff room a cycle of standing takes
/// <paramref name="RestStanding"/> off (<c>gbv.RestStanding</c>), one on the sofa <paramref name="RestSofa"/>
/// (<c>gbv.RestSofa</c>), a game of the video game <paramref name="RestGame"/> (<c>gbv.RestGame</c>) and one of pool
/// <paramref name="RestSnooker"/> (<c>gbv.RestSnooker</c>), less while more tired than half of
/// <paramref name="RecoveryFactor"/> (<c>gbv.RecoveryFactor</c>), though never less than
/// <paramref name="RecoveryMinimum"/> (<c>gbv.RecoveryMinimum</c>).
/// </summary>
/// <remarks>
/// The difficulty files set them (easy, medium, hard): WorkLight 1, RestStanding 4 / 3 / 3, RestSofa 10 / 8 / 8, RestGame
/// 80 / 60 / 60, RestSnooker 60 / 40 / 40, RecoveryFactor 500 / 450 / 400, RecoveryMinimum 4 / 3 / 3.
/// </remarks>
public sealed record RestRules(int WorkLight, int RestStanding, int RestSofa, int RestGame, int RestSnooker, int RecoveryFactor, int RecoveryMinimum)
{
    /// <summary>The medium difficulty's (<c>FULL00.SAM</c>).</summary>
    public static readonly RestRules Default = new(1, 3, 8, 60, 40, 450, 3);

    /// <summary>When the level files set none: the original game's built-in WorkLight of 2, otherwise the medium ones.</summary>
    public static readonly RestRules Unset = Default with { WorkLight = 2 };

    /// <summary>
    /// What a rest worth <paramref name="value"/> takes off <paramref name="fatigue"/> (both in thousandths): all of it,
    /// or, while more tired than half of <see cref="RecoveryFactor"/>, value × RecoveryFactor / fatigue, at least
    /// <see cref="RecoveryMinimum"/>.
    /// </summary>
    public int Recovery(int value, int fatigue) =>
        fatigue * 2 > RecoveryFactor ? Math.Max(RecoveryMinimum, value * RecoveryFactor / fatigue) : value;

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(WorkLight);
        writer.WriteInt32(RestStanding);
        writer.WriteInt32(RestSofa);
        writer.WriteInt32(RestGame);
        writer.WriteInt32(RestSnooker);
        writer.WriteInt32(RecoveryFactor);
        writer.WriteInt32(RecoveryMinimum);
    }

    internal static RestRules Read(SimReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}

/// <summary>
/// Staff tire while they work, after the community's logic specification: every <see cref="PatientNeeds.ModifyFreq"/>
/// ticks a nurse, doctor or handyman who is neither in a staff room nor standing idle (with nowhere to work) gains
/// <see cref="RestRules.WorkLight"/> thousandths of fatigue, whatever they are doing. In the same update fatigue above
/// <see cref="TirednessRules.CrackUpTired"/> costs a point of mood (and may make a doctor crack up), fatigue below
/// <see cref="TirednessRules.NotTired"/> brings one; in between it does not matter. Receptionists never tire. Staff more
/// tired than their hospital's staff room policy go and rest (<see cref="RestingBehaviour"/>) until fully rested.
/// </summary>
internal static class Tiredness
{
    /// <summary>The staff room's room type.</summary>
    internal const string StaffRoomType = "staff_room";

    /// <summary>One time in this many, a doctor tired past <see cref="TirednessRules.CrackUpTired"/> cracks up.</summary>
    internal const int CrackUpChance = 1000;

    private const string TrainingRoomType = "training";

    /// <summary>
    /// A nurse's, doctor's or handyman's fatigue in the update every <see cref="PatientNeeds.ModifyFreq"/> ticks: working
    /// tires; past the thresholds it moves the mood; resting and no longer very tired, the tired look goes and a doctor
    /// who cracked up comes round.
    /// </summary>
    internal static void Modify(Simulation simulation, Humanoid humanoid)
    {
        var rules = simulation.Rules.Tiredness;
        var inStaffRoom = RoomOf(simulation, humanoid) is { RoomType: StaffRoomType };

        // The no rest cheat keeps the hospital's staff fresh.
        if (!inStaffRoom && !IsIdle(humanoid) && !HasNoRestCheat(simulation, humanoid))
        {
            humanoid.Fatigue = Math.Min(humanoid.Fatigue + (rules.Rest.WorkLight / 1000m), 1m);
        }

        var fatigue = humanoid.Fatigue * 1000;
        if (fatigue > rules.CrackUpTired)
        {
            Feelings.ChangeHappiness(simulation, humanoid, -1m / PatientNeeds.MaxPoints);
            if (simulation.Random.Next(CrackUpChance) == 0)
            {
                CrackUp(humanoid);
            }
        }
        else if (fatigue < rules.NotTired)
        {
            Feelings.ChangeHappiness(simulation, humanoid, 1m / PatientNeeds.MaxPoints);
        }

        if (inStaffRoom && !IsVeryTired(simulation, humanoid.Fatigue))
        {
            humanoid.IsTiredMoodShown = false;
            ComeRound(humanoid);
        }
    }

    /// <summary>The frames of the resting animation between two rests standing or on the sofa.</summary>
    internal const int RestFrames = 8;

    /// <summary>
    /// The ticks between two rests standing or on the sofa: eight frames of the one-frame resting animation, each lasting
    /// <see cref="FrameTicks"/>.
    /// </summary>
    internal static int RestPeriod(Simulation simulation, Humanoid humanoid) => RestFrames * FrameTicks(simulation, humanoid);

    /// <summary>
    /// How long a frame of a member of staff's animation lasts (the maintainer's description of the original): two ticks
    /// up to <see cref="StaffRules.DoctorThreshold"/> skill; one tick below <see cref="StaffRules.ConsultantThreshold"/>
    /// unless very tired, and from it unless tired enough to crack up; always two in the training room.
    /// </summary>
    internal static int FrameTicks(Simulation simulation, Humanoid humanoid)
    {
        var staff = simulation.Rules.Staff;
        var tiredness = simulation.Rules.Tiredness;
        var skill = humanoid.Profile?.Skill ?? 0;
        var fatigue = humanoid.Fatigue * 1000;
        if (skill <= staff.DoctorThreshold || RoomOf(simulation, humanoid) is { RoomType: TrainingRoomType })
        {
            return 2;
        }

        return fatigue > (skill < staff.ConsultantThreshold ? tiredness.VeryTired : tiredness.CrackUpTired) ? 2 : 1;
    }

    /// <summary>
    /// Standing idle, with nowhere to work: a doctor or nurse without a room (and a nurse not off to vaccinate), a handyman
    /// without a job.
    /// </summary>
    internal static bool IsIdle(Humanoid humanoid) => humanoid.Behaviour switch
    {
        DoctorBehaviour { Phase: DoctorPhase.Idle } => true,
        NurseBehaviour { Phase: NursePhase.Idle, VaccinatePatientId: 0 } => true,
        HandymanBehaviour { Phase: HandymanPhase.Meandering } => true,
        _ => false,
    };

    /// <summary>A doctor cracks up: they look crazy (<c>Doctor:setCrazy</c>). Only doctors have the look.</summary>
    private static void CrackUp(Humanoid humanoid)
    {
        if (!humanoid.IsCrazy && humanoid.Profile is { Class: StaffClass.Doctor } profile)
        {
            humanoid.SetLayer(Humanoid.StaffLayer, (byte)(profile.Layer5 + 4));
            humanoid.IsCrazy = true;
        }
    }

    /// <summary>A doctor who cracked up is themselves again.</summary>
    private static void ComeRound(Humanoid humanoid)
    {
        if (humanoid.IsCrazy && humanoid.Layers[Humanoid.StaffLayer] >= 5)
        {
            humanoid.SetLayer(Humanoid.StaffLayer, (byte)(humanoid.Layers[Humanoid.StaffLayer] - 4));
            humanoid.IsCrazy = false;
        }
    }

    /// <summary>
    /// <c>Staff:updateSpeed</c>, at the end of every tick: juniors walk slowly and consultants fast; very tired staff
    /// one step slower and those tired enough to crack up two (receptionists never); a doctor in the training room
    /// slowly; with the no-rest cheat everyone fast.
    /// </summary>
    internal static void UpdateSpeed(Simulation simulation, Humanoid humanoid)
    {
        var profile = humanoid.Profile!;
        var level = profile.IsJunior ? 1 : profile.IsConsultant ? 3 : 2;
        if (RoomOf(simulation, humanoid) is { RoomType: TrainingRoomType } && profile.Class == StaffClass.Doctor)
        {
            level = 1;
        }
        else if (HasNoRestCheat(simulation, humanoid))
        {
            level = 3;
        }
        else if (profile.Class != StaffClass.Receptionist)
        {
            if (IsCrackUpTired(simulation, humanoid.Fatigue))
            {
                level -= 2;
            }
            else if (IsVeryTired(simulation, humanoid.Fatigue))
            {
                level--;
            }
        }

        humanoid.IsFast = level >= 3;
        humanoid.IsSlow = level <= 1;
    }

    private static bool HasNoRestCheat(Simulation simulation, Humanoid humanoid) =>
        humanoid.Employer >= 0
        && humanoid.Employer < simulation.Hospitals.Count
        && (simulation.Hospitals[humanoid.Employer].ActiveCheats & ToggleCheats.NoRest) != 0;

    /// <summary><c>Staff:isVeryTired</c>.</summary>
    internal static bool IsVeryTired(Simulation simulation, decimal fatigue) => fatigue * 1000 >= simulation.Rules.Tiredness.VeryTired;

    /// <summary><c>Staff:isCrackUpTired</c>.</summary>
    internal static bool IsCrackUpTired(Simulation simulation, decimal fatigue) => fatigue * 1000 >= simulation.Rules.Tiredness.CrackUpTired;

    /// <summary>A staff room was built: nobody waits for one any longer (<c>Staff:notifyNewRoom</c>).</summary>
    internal static void OnRoomActivated(Simulation simulation, Room room)
    {
        if (room.RoomType != StaffRoomType)
        {
            return;
        }

        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Profile: not null } humanoid)
            {
                humanoid.WaitingForStaffRoom = false;
            }
        }
    }

    /// <summary>The employer's <c>goto_staffroom</c> policy, which starts at the level's <c>gbv.Tired</c>.</summary>
    private static decimal GoToStaffRoom(Simulation simulation, Humanoid humanoid) =>
        humanoid.Employer >= 0 && humanoid.Employer < simulation.Hospitals.Count
            ? simulation.Hospitals[humanoid.Employer].Policies.GoToStaffRoom / 1000m
            : simulation.Rules.Tiredness.Tired / 1000m;

    /// <summary>
    /// <c>Staff:checkIfNeedRest</c>, every tick: very tired staff look it; staff more tired than the staff room policy
    /// go and rest once they have finished what they are doing.
    /// </summary>
    internal static void CheckIfNeedRest(Simulation simulation, Humanoid humanoid)
    {
        // Only when very tired does the icon show.
        if (IsVeryTired(simulation, humanoid.Fatigue))
        {
            humanoid.IsTiredMoodShown = true;
        }

        WantStaffRoom(simulation, humanoid);
    }

    /// <summary>
    /// The rest of <c>checkIfNeedRest</c>: a member of staff more tired than the policy and outside a staff room goes to
    /// one if they may, or waits for one to be built.
    /// </summary>
    internal static void WantStaffRoom(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.Fatigue <= GoToStaffRoom(simulation, humanoid) || RoomOf(simulation, humanoid) is { RoomType: StaffRoomType }
            || humanoid.WaitingForStaffRoom)
        {
            return;
        }

        if (!HasStaffRoom(simulation, humanoid.Employer))
        {
            humanoid.WaitingForStaffRoom = true;
            return;
        }

        if (humanoid.Behaviour is RestingBehaviour || !MayLeaveWork(simulation, humanoid) || NearestStaffRoom(simulation, humanoid) is not { } staffRoom)
        {
            // staffroom_needed: they go once the patient has left (or the handyman has finished the job).
            return;
        }

        // Staff:goToStaffRoom: out of the room (Room:createLeaveAction) and to the nearest staff room.
        var parcel = humanoid.Behaviour is HandymanBehaviour handyman ? handyman.Parcel : 0;

        // last_room: a researcher or a doctor in training goes back there once rested, if it still has room for them.
        var returnRoom = humanoid.Behaviour is DoctorBehaviour doctor && simulation.FindEntity(doctor.RoomId) is Room { RoomType: RoomVisits.ResearchRoom or Training.RoomType } working
            ? working.Id
            : 0;
        // A surgeon changes back into a doctor at the theatre's screen first (operating_theatre.lua's must-happen actions).
        if (humanoid.Behaviour is DoctorBehaviour { IsChangingBack: true }
            || (humanoid.Type == HumanoidType.Surgeon && humanoid.Behaviour is DoctorBehaviour surgeon && surgeon.ChangeBackBeforeRest(simulation, humanoid)))
        {
            return;
        }

        StaffEmployment.Detach(simulation, humanoid);
        if (humanoid.Type == HumanoidType.Surgeon)
        {
            humanoid.Type = HumanoidType.Doctor;
        }

        humanoid.Behaviour = new RestingBehaviour(staffRoom.Id, parcel, returnRoom);
    }

    /// <summary>
    /// Whether the member of staff may leave what they are doing for the staff room: nurses may leave a ward with
    /// patients in its beds; others wait until the room's patient has gone (<c>may_leave</c>); a handyman finishes his
    /// job, a nurse her vaccination; receptionists never go.
    /// </summary>
    private static bool MayLeaveWork(Simulation simulation, Humanoid humanoid)
    {
        var roomId = humanoid.Behaviour switch
        {
            DoctorBehaviour doctor => doctor.RoomId,
            NurseBehaviour { VaccinatePatientId: 0 } nurse => nurse.RoomId,
            HandymanBehaviour { Phase: HandymanPhase.Meandering } => 0,
            _ => -1,
        };
        if (roomId < 0)
        {
            return false;
        }

        return simulation.FindEntity(roomId) is not Room room || room.RoomType == RoomVisits.WardRoom || room.PatientId == 0;
    }

    /// <summary><c>World:findRoomNear(self, "staff_room")</c>: the nearest working staff room of their hospital.</summary>
    private static Room? NearestStaffRoom(Simulation simulation, Humanoid humanoid)
    {
        Room? best = null;
        var bestDistance = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is Room { IsActive: true, RoomType: StaffRoomType } room && RoomVisits.Owner(simulation, room) == humanoid.Employer
                && simulation.FindEntity(room.DoorId) is RoomDoor door
                && simulation.Pathfinder.FindDistance(simulation.Map, humanoid.StepTarget, door.InsideTile(room.Area)) is { } distance
                && (best is null || distance < bestDistance))
            {
                best = room;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary><c>Humanoid:getRoom</c>: the room, built or not, whose floor the person stands on.</summary>
    private static Room? RoomOf(Simulation simulation, Humanoid humanoid)
    {
        var roomId = simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId;
        return roomId != 0 ? simulation.FindEntity(roomId) as Room : null;
    }

    /// <summary>
    /// Whether the hospital has a working staff room (<c>World:findRoomNear(self, "staff_room")</c>, which looks for any
    /// active staff room that is not being edited).
    /// </summary>
    private static bool HasStaffRoom(Simulation simulation, int player)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Room { IsActive: true, RoomType: StaffRoomType } room && RoomVisits.Owner(simulation, room) == player)
            {
                return true;
            }
        }

        return false;
    }
}
