// Ported from CorsixTH CorsixTH/Lua/humanoid_actions/seek_staffroom.lua, use_staffroom.lua and
// CorsixTH/Lua/rooms/staff_room.lua (StaffRoom:onHumanoidEnter); what resting takes off and what is used next after the
// community's logic specification.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Serialization;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.People;

/// <summary>Where a resting member of staff is.</summary>
public enum RestingPhase : byte
{
    /// <summary>On the way to the staff room (<c>SeekStaffRoomAction</c>).</summary>
    WalkingToRoom,

    /// <summary>Walking to the sofa, pool table or video game picked.</summary>
    WalkingToObject,

    /// <summary>Using it: less tired every few ticks on the sofa, after every game of pool or the video game.</summary>
    UsingObject,

    /// <summary>No free sofa: standing about, less tired every few ticks, looking again after each rest.</summary>
    Meandering,

    /// <summary>Rested: out of the staff room, back to work.</summary>
    Leaving,
}

/// <summary>
/// Where a member of staff goes once rested. The original keeps no room for them: what they did is all it remembers
/// (the game expert's answer of 26.11.2026, from the developers' notes).
/// </summary>
public enum RestReturn : byte
{
    /// <summary>Back to work like anyone free: the nearest room calling for their kind, or wandering about.</summary>
    None,

    /// <summary>A researcher: to the research department with the most desks left free.</summary>
    Research,

    /// <summary>A doctor from the training room: a consultant to one without a lecturer, a student to the most chairs.</summary>
    Training,

    /// <summary>A nurse: to one of the hospital's wards at random.</summary>
    Ward,
}

/// <summary>
/// A member of staff tired past the hospital's <c>goto_staffroom</c> policy, who has left their work for the nearest staff
/// room. Every rest there takes fatigue off (<see cref="RestRules.Recovery"/>) and brings a point of mood: every
/// <see cref="Tiredness.RestPeriod"/> ticks standing (<see cref="RestRules.RestStanding"/>) or on the sofa
/// (<see cref="RestRules.RestSofa"/>), a whole game of the
/// video game (<see cref="RestRules.RestGame"/>, doctors and nurses) or of pool (<see cref="RestRules.RestSnooker"/>,
/// doctors and handymen). After every rest they choose again: one time in ten pool, one time in ten the video game, if
/// there is a free one, else a sofa, else standing. Fully rested, their discontent halves and they go back to work: free
/// for any room, except that a researcher, a doctor from the training room and a nurse look for that kind of room first
/// (<see cref="RestReturn"/>).
/// </summary>
public sealed class RestingBehaviour : HumanoidBehaviour
{
    private const string Sofa = "sofa";
    private const string PoolTable = "pool_table";
    private const string VideoGame = "video_game";

    internal RestingBehaviour(int roomId, int parcel, RestReturn returnTo = RestReturn.None)
    {
        RoomId = roomId;
        Parcel = parcel;
        ReturnTo = returnTo;
    }

    public override BehaviourKind Kind => BehaviourKind.Resting;

    public RestingPhase Phase { get; private set; }

    /// <summary>The staff room.</summary>
    public int RoomId { get; private set; }

    /// <summary>The object being walked to or used, reserved for this member of staff (<c>reserved_for</c>), or 0.</summary>
    public int TargetId { get; private set; }

    /// <summary>A handyman's parcel, given back afterwards.</summary>
    public int Parcel { get; }

    /// <summary>What kind of room they look for first once rested.</summary>
    public RestReturn ReturnTo { get; private set; }

    /// <summary>
    /// A save from before format 120 kept the research department or training room itself (CorsixTH's
    /// <c>last_room</c>); its kind is taken from it once they are rested, if it is still there.
    /// </summary>
    private int _legacyReturnRoomId;

    /// <summary>The use of the object, while it lasts.</summary>
    public ObjectUse? Use { get; private set; }

    private Meandering Meander { get; init; } = new();

    /// <summary>Rounds of pool or the video game left in the game (<c>obj_use_time</c>).</summary>
    private int _useTime;

    /// <summary>The object picked to go to next once the current use ends, or 0 (<c>next_target_obj</c>).</summary>
    private int _nextTargetId;

    private int _meanders;

    /// <summary>Ticks since the last rest standing or on the sofa (<see cref="Tiredness.RestPeriod"/>).</summary>
    private int _restTicks;

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        if (simulation.FindEntity(RoomId) is not Room { IsActive: true } room || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            BackToWork(simulation, humanoid);
            return;
        }

        switch (Phase)
        {
            case RestingPhase.WalkingToRoom:
                if (!room.Area.Contains(humanoid.Tile))
                {
                    if (!humanoid.WalkTo(simulation, door.InsideTile(room.Area)))
                    {
                        BackToWork(simulation, humanoid);
                    }

                    return;
                }

                // StaffRoom:onHumanoidEnter: they count as a visitor and look for something to do.
                if (room.DoorQueue is { } queue)
                {
                    queue.VisitorCount++;
                }

                PickNext(simulation, humanoid, room);
                break;
            case RestingPhase.WalkingToObject:
                if (simulation.FindEntity(TargetId) is RoomObject target && humanoid.Tile == RoomVisits.UseTile(simulation, target))
                {
                    Phase = RestingPhase.UsingObject;
                    _useTime = UseTime(simulation, target.ObjectType);
                    Use = ObjectUse.Start(simulation, humanoid, target, prolonged: true, alwaysProlonged: true);
                }
                else
                {
                    PickNext(simulation, humanoid, room);
                }

                break;
            case RestingPhase.UsingObject:
                UseStep(simulation, humanoid, room, door);
                break;
            case RestingPhase.Meandering:
                Meander.Update(simulation, humanoid, loop: () => false);
                if (++_restTicks >= Tiredness.RestPeriod(humanoid))
                {
                    _restTicks = 0;
                    if (Rest(simulation, humanoid, simulation.Rules.Tiredness.Rest.RestStanding))
                    {
                        Leave(simulation, humanoid, room, door);
                    }
                    else
                    {
                        PickNext(simulation, humanoid, room);
                    }
                }

                break;
            case RestingPhase.Leaving:
                if (room.Area.Contains(humanoid.Tile))
                {
                    if (!humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door)))
                    {
                        BackToWork(simulation, humanoid);
                    }

                    return;
                }

                var returnTo = ReturnTo;
                if (_legacyReturnRoomId != 0)
                {
                    returnTo = simulation.FindEntity(_legacyReturnRoomId) switch
                    {
                        Room { RoomType: RoomVisits.ResearchRoom } => RestReturn.Research,
                        Room { RoomType: Training.RoomType } => RestReturn.Training,
                        _ => RestReturn.None,
                    };
                }

                BackToWork(simulation, humanoid);
                RoomVisits.ReturnAfterRest(simulation, humanoid, returnTo);

                break;
        }
    }

    /// <summary>
    /// <c>use_staffroom</c>'s loop callback: a cycle on the sofa is a rest, pool and the video game rest at the end of a
    /// game; rested, they leave; otherwise they choose again, and go on if it is the same object.
    /// </summary>
    private void UseStep(Simulation simulation, Humanoid humanoid, Room room, RoomDoor door)
    {
        var target = simulation.FindEntity(TargetId) as RoomObject;
        switch (Use?.Update(simulation, humanoid) ?? ObjectUseEvent.Done)
        {
            case ObjectUseEvent.Loop when _nextTargetId == 0 && Phase == RestingPhase.UsingObject && target?.ObjectType is PoolTable or VideoGame:
                if (--_useTime > 0)
                {
                    break;
                }

                var rules = simulation.Rules.Tiredness.Rest;
                AfterRest(simulation, humanoid, room, target, target.ObjectType == PoolTable ? rules.RestSnooker : rules.RestGame);
                break;
            case ObjectUseEvent.None or ObjectUseEvent.Loop when _nextTargetId == 0 && Phase == RestingPhase.UsingObject && Use is not null:
                // On the sofa: a rest every eight frames of the sitting animation.
                if (++_restTicks >= Tiredness.RestPeriod(humanoid))
                {
                    _restTicks = 0;
                    AfterRest(simulation, humanoid, room, target, simulation.Rules.Tiredness.Rest.RestSofa);
                }

                break;
            case ObjectUseEvent.Done:
                Use = null;
                if (Phase == RestingPhase.Leaving || humanoid.Fatigue == 0)
                {
                    Leave(simulation, humanoid, room, door);
                }
                else if (simulation.FindEntity(_nextTargetId) is RoomObject following)
                {
                    GoTo(simulation, humanoid, following);
                }
                else if (_nextTargetId < 0)
                {
                    Stand();
                }
                else
                {
                    PickNext(simulation, humanoid, room);
                }

                _nextTargetId = 0;
                break;
        }
    }

    /// <summary>
    /// After a rest on <paramref name="target"/>: rested, they get up to leave; otherwise they choose again, and go on if
    /// it is the same object.
    /// </summary>
    private void AfterRest(Simulation simulation, Humanoid humanoid, Room room, RoomObject? target, int value)
    {
        if (Rest(simulation, humanoid, value))
        {
            Phase = RestingPhase.Leaving;
            Use!.Interrupt();
            return;
        }

        var next = Decide(simulation, humanoid, room);
        if (next is not null && next.Id == target?.Id)
        {
            _useTime = UseTime(simulation, next.ObjectType);
        }
        else
        {
            // Another object, or standing about when no sofa is free.
            _nextTargetId = next?.Id ?? -1;
            Use!.Interrupt();
        }
    }

    /// <summary>
    /// A rest worth <paramref name="value"/>: fatigue down by <see cref="RestRules.Recovery"/>, mood up a point. Fully
    /// rested, the discontent halves (the specification's resign count).
    /// </summary>
    /// <returns>Whether they are now fully rested.</returns>
    private static bool Rest(Simulation simulation, Humanoid humanoid, int value)
    {
        var fatigue = (int)decimal.Floor(humanoid.Fatigue * 1000);
        humanoid.Fatigue = Math.Max(0, humanoid.Fatigue - (simulation.Rules.Tiredness.Rest.Recovery(value, fatigue) / 1000m));
        Feelings.ChangeHappiness(simulation, humanoid, 1m / PatientNeeds.MaxPoints);
        if (humanoid.Fatigue > 0)
        {
            return false;
        }

        humanoid.Discontent /= 2;
        return true;
    }

    private void Leave(Simulation simulation, Humanoid humanoid, Room room, RoomDoor door)
    {
        Use = null;
        TargetId = 0;
        Phase = RestingPhase.Leaving;
        humanoid.WalkTo(simulation, RoomVisits.OutsideTile(room, door));
    }

    private void PickNext(Simulation simulation, Humanoid humanoid, Room room)
    {
        Use = null;
        if (Decide(simulation, humanoid, room) is { } placed)
        {
            GoTo(simulation, humanoid, placed);
            return;
        }

        Stand();
    }

    /// <summary>No free sofa: standing about (<c>MeanderAction</c>).</summary>
    private void Stand()
    {
        Use = null;
        TargetId = 0;
        Phase = RestingPhase.Meandering;
        _meanders = 1;
        _restTicks = 0;
        Meander.Reset();
    }

    private void GoTo(Simulation simulation, Humanoid humanoid, RoomObject placed)
    {
        TargetId = placed.Id;
        _restTicks = 0;
        Phase = RestingPhase.WalkingToObject;
        humanoid.WalkTo(simulation, RoomVisits.UseTile(simulation, placed));
    }

    /// <summary>
    /// What to rest on next: one time in ten pool (doctors and handymen), one time in ten the video game (doctors and
    /// nurses), if a free one is in the room, else the nearest free sofa, else nothing (standing about).
    /// </summary>
    private RoomObject? Decide(Simulation simulation, Humanoid humanoid, Room room)
    {
        var chance = simulation.Random.Next(10);
        var staffClass = humanoid.Profile?.Class;
        var type = Sofa;
        if (chance == 0 && staffClass is StaffClass.Doctor or StaffClass.Handyman)
        {
            type = PoolTable;
        }
        else if (chance == 1 && staffClass is StaffClass.Doctor or StaffClass.Nurse)
        {
            type = VideoGame;
        }

        var taken = new HashSet<int>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: RestingBehaviour resting } && entity.Id != humanoid.Id)
            {
                taken.Add(resting.TargetId);
                taken.Add(resting._nextTargetId);
            }
        }

        taken.Remove(0);
        return Nearest(type) ?? (type != Sofa ? Nearest(Sofa) : null);

        // The object in use counts first, so that a rest on the sofa goes on there.
        RoomObject? Nearest(string wanted) => simulation.FindEntity(TargetId) is RoomObject { IsDestroyed: false } current && current.ObjectType == wanted
            ? current
            : simulation.Entities.OfType<RoomObject>()
            .Where(o => o.RoomId == room.Id && o.ObjectType == wanted && !o.IsDestroyed && !taken.Contains(o.Id))
            .OrderBy(o => Math.Abs(o.Tile.X - humanoid.Tile.X) + Math.Abs(o.Tile.Y - humanoid.Tile.Y))
            .ThenBy(o => o.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// <c>generate_use_time</c>: the rounds of a game, at the pool table 2 to 5, at the video game 2 to 15; on the sofa
    /// every cycle is a rest.
    /// </summary>
    private static int UseTime(Simulation simulation, string type) => type switch
    {
        PoolTable => simulation.Random.NextInclusive(2, 5),
        VideoGame => simulation.Random.NextInclusive(2, 15),
        _ => 1,
    };

    /// <summary>Rested (or the staff room gone): back to their usual work, free for any room.</summary>
    private void BackToWork(Simulation simulation, Humanoid humanoid)
    {
        // The pace is worked out again after a rest.
        Tiredness.UpdateSpeed(simulation, humanoid);
        humanoid.Behaviour = StaffHiring.NewBehaviour(humanoid.Profile!.Class);
        if (humanoid.Behaviour is HandymanBehaviour handyman)
        {
            handyman.Parcel = Parcel;
        }
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteInt32(RoomId);
        writer.WriteInt32(Parcel);
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(TargetId);
        ObjectUse.WriteOptional(writer, Use);
        Meander.Write(writer);
        writer.WriteInt32(_useTime);
        writer.WriteInt32(_nextTargetId);
        writer.WriteInt32(_meanders);
        writer.WriteByte((byte)ReturnTo);
        writer.WriteInt32(_restTicks);
    }

    internal static RestingBehaviour ReadState(SimReader reader)
    {
        var roomId = reader.ReadInt32();
        var parcel = reader.ReadInt32();
        var phase = (RestingPhase)reader.ReadByte();
        var target = reader.ReadInt32();
        var use = ObjectUse.ReadOptional(reader);
        var resting = new RestingBehaviour(roomId, parcel) { Phase = phase, Meander = Meandering.Read(reader) };
        resting.TargetId = target;
        resting.Use = use;
        resting._useTime = reader.ReadInt32();
        resting._nextTargetId = reader.ReadInt32();
        resting._meanders = reader.ReadInt32();
        // Version 120 keeps the kind of room looked for after the rest rather than the room left.
        if (reader.Version is 0 or >= 120)
        {
            var returnTo = reader.ReadByte();
            resting.ReturnTo = returnTo <= (byte)RestReturn.Ward ? (RestReturn)returnTo : throw new InvalidDataException($"Bad rest return {returnTo}.");
        }
        else if (reader.Version >= 74)
        {
            resting._legacyReturnRoomId = reader.ReadInt32();
        }

        resting._restTicks = reader.Version is 0 or >= 114 ? reader.ReadInt32() : 0;
        return resting;
    }
}
