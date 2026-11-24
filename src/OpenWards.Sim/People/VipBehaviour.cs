// A VIP's visit. The walk started from CorsixTH CorsixTH/Lua/entities/humanoids/vip.lua (Vip:tickDay, getNextRoom,
// goHome), humanoid_actions/vip_go_to_next_room.lua, humanoid_actions/seek_reception.lua and
// objects/reception_desk.lua (handleVIP). The score follows the community's description of the original game (the
// logic specification), not CorsixTH's Vip:setVIPRating.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Serialization;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.People;

/// <summary>Where a VIP is in their visit.</summary>
public enum VipPhase : byte
{
    SeekReception,
    WalkingToReception,
    InReceptionQueue,

    /// <summary>Standing about until the day is out (CorsixTH's <c>waiting</c>), after reception or when a room went.</summary>
    Waiting,

    WalkingToRoom,

    /// <summary>Standing at a room's door, looking in.</summary>
    LookingAtRoom,

    GoingHome,
}

/// <summary>
/// A VIP visiting a hospital: they report at reception, look in at up to <see cref="RoomsToVisit"/> rooms from their
/// doors, and on leaving score the whole hospital as it stands then (<see cref="ScoreHospital"/>), the lower the better.
/// </summary>
/// <remarks>
/// The arrival announcement is the client's (HospitalAnnouncements). CorsixTH looks in at the first room and then each
/// with a chance, and rates by what the VIP saw on the way; here deaths, queues and cures during the visit do not count.
/// </remarks>
public sealed class VipBehaviour : HumanoidBehaviour, IQueueMember
{
    /// <summary>How many rooms a VIP looks in at.</summary>
    public const int RoomsToVisit = 14;

    /// <summary>Hours a VIP looks in at a room: CorsixTH's idle of 50, doubled by their slow animation.</summary>
    internal const int LookingHours = 100;

    /// <summary>Litter on this many tiles or more counts as a dirty hospital.</summary>
    internal const int DirtyLitterTiles = 11;

    /// <summary>With fewer staff than this the hospital scores badly.</summary>
    internal const int FewStaff = 3;

    /// <summary>The patients' warmth counts only with more patients than this.</summary>
    internal const int FewPatients = 10;

    /// <summary>The patients' average warmth (0 to 100) the VIP finds comfortable.</summary>
    internal const int ColdestComfortable = 20;

    /// <summary>The warmest average the VIP finds comfortable.</summary>
    internal const int WarmestComfortable = 80;

    public VipBehaviour(int name)
    {
        Name = name;
    }

    public override BehaviourKind Kind => BehaviourKind.Vip;

    /// <summary>Which of the VIP names (0 to <see cref="VipVisits.NameCount"/> − 1, in the game's order).</summary>
    public int Name { get; }

    public VipPhase Phase { get; private set; }

    /// <summary>The score once they go home, the lower the better; 0 before.</summary>
    public int Score { get; internal set; }

    /// <summary>Rooms looked in at so far.</summary>
    public int VisitedRooms { get; private set; }

    /// <summary>The room the VIP is going to or looking at, or the last one; 0 before the first.</summary>
    public int RoomId { get; private set; }

    /// <summary>The desk whose queue they are in or heading for, or 0.</summary>
    public int DeskId { get; private set; }

    /// <summary>Days left standing about (CorsixTH's <c>waiting</c>), or 0.</summary>
    private int Waiting { get; set; }

    /// <summary>Hours left looking in at a room.</summary>
    private int LookingLeft { get; set; }

    private TilePosition QueueTile { get; set; }

    private TilePosition QueueFace { get; set; }

    private TilePosition HomeTile { get; set; }

    /// <inheritdoc/>
    public bool IsStandingInQueue(Humanoid humanoid) => !humanoid.IsMoving && humanoid.Tile == QueueTile;

    /// <inheritdoc/>
    public void TakeQueuePlace(Simulation simulation, Humanoid humanoid, TilePosition tile, TilePosition face)
    {
        QueueFace = face;
        if (QueueTile == tile && (humanoid.Tile == tile || humanoid.Destination == tile))
        {
            return;
        }

        QueueTile = tile;
        if (humanoid.Tile != tile || humanoid.IsMoving)
        {
            humanoid.WalkTo(simulation, tile);
        }
    }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        switch (Phase)
        {
            case VipPhase.SeekReception:
            case VipPhase.WalkingToReception:
                SeekReception(simulation, humanoid);
                break;
            case VipPhase.InReceptionQueue:
                if (humanoid.Tile == QueueTile)
                {
                    humanoid.FaceTowards(QueueFace);
                }

                break;
            case VipPhase.WalkingToRoom:
                if (simulation.FindEntity(RoomId) is not Room { DoorId: var doorId } room || simulation.FindEntity(doorId) is not RoomDoor door)
                {
                    StandAbout();
                    break;
                }

                humanoid.FaceTowards(door.InsideTile(room.Area));
                Phase = VipPhase.LookingAtRoom;
                LookingLeft = LookingHours;
                break;
            case VipPhase.LookingAtRoom:
                if (simulation.FindEntity(RoomId) is null)
                {
                    StandAbout();
                    break;
                }

                if (--LookingLeft <= 0)
                {
                    VisitedRooms++;
                    GetNextRoom(simulation, humanoid);
                }

                break;
            case VipPhase.GoingHome:
                if (humanoid.Tile == HomeTile || !humanoid.WalkTo(simulation, HomeTile))
                {
                    humanoid.IsDespawning = true;
                    if (humanoid.HospitalPlayer >= 0 && humanoid.HospitalPlayer < simulation.Hospitals.Count)
                    {
                        VipVisits.OnVipLeft(simulation, simulation.Hospitals[humanoid.HospitalPlayer], this);
                    }
                }

                break;
        }
    }

    /// <summary><c>Vip:tickDay</c>: a day of standing about is over; on to the next room, or home if there is none.</summary>
    internal void OnEndDay(Simulation simulation, Humanoid humanoid)
    {
        if (Waiting > 0 && --Waiting == 0)
        {
            GetNextRoom(simulation, humanoid);
        }
    }

    /// <summary><c>handleVIP</c>: reception has seen the VIP, who stands there for the rest of the day.</summary>
    internal void OnSeenAtReception()
    {
        Phase = VipPhase.Waiting;
        DeskId = 0;
        Waiting = 1;
    }

    /// <summary>
    /// <c>action_seek_reception_start</c>: to the best staffed desk, joining its queue once close enough. Without one
    /// the VIP waits where they are and looks again (CorsixTH has them meander meanwhile).
    /// </summary>
    private void SeekReception(Simulation simulation, Humanoid humanoid)
    {
        Phase = ReceptionDesks.Approach(simulation, humanoid, out var deskId) switch
        {
            DeskApproach.Joined => VipPhase.InReceptionQueue,
            DeskApproach.Walking => VipPhase.WalkingToReception,
            _ => VipPhase.SeekReception,
        };
        if (deskId != 0)
        {
            DeskId = deskId;
        }
    }

    /// <summary>The room went while the VIP was on the way or looking (the queue's <c>expect</c> callback).</summary>
    private void StandAbout()
    {
        Phase = VipPhase.Waiting;
        Waiting = 1;
    }

    /// <summary>The next built room in order, or home once there is none or <see cref="RoomsToVisit"/> were seen.</summary>
    private void GetNextRoom(Simulation simulation, Humanoid humanoid)
    {
        Room? next = null;
        if (VisitedRooms < RoomsToVisit)
        {
            foreach (var room in VipVisits.RoomsOf(simulation, humanoid.HospitalPlayer))
            {
                if (room.Id > RoomId && room.IsActive)
                {
                    next = room;
                    break;
                }
            }
        }

        GoToNextRoom(simulation, humanoid, next);
    }

    /// <summary><c>VipGoToNextRoomAction</c>: to the room's door, or home when there are no rooms left.</summary>
    private void GoToNextRoom(Simulation simulation, Humanoid humanoid, Room? room)
    {
        if (room is null || simulation.FindEntity(room.DoorId) is not RoomDoor door)
        {
            GoHome(simulation, humanoid);
            return;
        }

        RoomId = room.Id;
        var inside = door.InsideTile(room.Area);
        var outside = inside == door.Tile ? door.OtherSide : door.Tile;
        Phase = VipPhase.WalkingToRoom;
        if (humanoid.Tile != outside && !humanoid.WalkTo(simulation, outside))
        {
            // CorsixTH's walk would fail and the VIP stand about; here they stand about at once.
            StandAbout();
        }
    }

    /// <summary><c>Vip:goHome</c>: the VIP makes up their mind and walks off the map.</summary>
    private void GoHome(Simulation simulation, Humanoid humanoid)
    {
        if (Phase == VipPhase.GoingHome)
        {
            return;
        }

        if (simulation.FindEntity(DeskId) is RoomObject { Desk: { } desk })
        {
            desk.Queue.Remove(simulation, humanoid.Id);
        }

        DeskId = 0;
        Score = ScoreHospital(simulation, humanoid.HospitalPlayer) + simulation.Random.NextInclusive(-3, 2);
        Phase = VipPhase.GoingHome;
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        HomeTile = points.Count > 0 ? points[simulation.Random.Next(points.Count)] : humanoid.Tile;
        if (!humanoid.WalkTo(simulation, HomeTile))
        {
            HomeTile = humanoid.Tile;
        }
    }

    /// <summary>
    /// The VIP's score of the whole hospital, without the random −3 to +2 added to it; the lower the better: litter on
    /// <see cref="DirtyLitterTiles"/> tiles or more +1 (else −1); fewer than <see cref="FewStaff"/> staff +4, otherwise
    /// anyone more tired than <c>VeryTired</c> +2, an average tiredness above <c>Tired</c> +1, else −1; with more than
    /// <see cref="FewPatients"/> patients, their average warmth outside 20 to 80 +1 (else −1); no doctors +4, otherwise
    /// more than half of them juniors +2 or consultants −2; no rooms +4, one or two +1.
    /// </summary>
    internal static int ScoreHospital(Simulation simulation, int player)
    {
        if (player < 0 || player >= simulation.Hospitals.Count)
        {
            return 0;
        }

        var score = LitterTiles(simulation, player) >= DirtyLitterTiles ? 1 : -1;

        var staff = new List<Humanoid>();
        var patients = new List<Humanoid>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Profile: not null, IsFired: false } member && member.Employer == player)
            {
                staff.Add(member);
            }
            else if (entity is Humanoid { Behaviour: PatientBehaviour, IsDespawning: false } patient && patient.HospitalPlayer == player)
            {
                patients.Add(patient);
            }
        }

        var tiredness = simulation.Rules.Tiredness;
        if (staff.Count < FewStaff)
        {
            score += 4;
        }
        else if (staff.Any(member => member.Fatigue * 1000 > tiredness.VeryTired))
        {
            score += 2;
        }
        else if (staff.Average(member => member.Fatigue) * 1000 > tiredness.Tired)
        {
            score += 1;
        }
        else
        {
            score -= 1;
        }

        if (patients.Count > FewPatients)
        {
            var warmth = patients.Average(patient => (decimal)PatientNeeds.Warmth(patient));
            score += warmth is < ColdestComfortable or > WarmestComfortable ? 1 : -1;
        }

        var doctors = staff.Count(member => member.Profile!.Class == StaffClass.Doctor);
        if (doctors == 0)
        {
            score += 4;
        }
        else if (staff.Count(member => member.Profile is { Class: StaffClass.Doctor, IsJunior: true }) * 2 > doctors)
        {
            score += 2;
        }
        else if (staff.Count(member => member.Profile is { Class: StaffClass.Doctor, IsConsultant: true }) * 2 > doctors)
        {
            score -= 2;
        }

        var rooms = VipVisits.RoomsOf(simulation, player).Count(room => room.IsActive);
        score += rooms == 0 ? 4 : rooms <= 2 ? 1 : 0;
        return score;
    }

    /// <summary>The tiles of the hospital with litter on them.</summary>
    internal static int LitterTiles(Simulation simulation, int player)
    {
        var tiles = new HashSet<TilePosition>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Litter { IsRemoved: false } litter && litter.Player == player)
            {
                tiles.Add(litter.Tile);
            }
        }

        return tiles.Count;
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteInt32(Name);
        writer.WriteByte((byte)Phase);
        writer.WriteInt32(Score);
        writer.WriteInt32(VisitedRooms);
        writer.WriteInt32(RoomId);
        writer.WriteInt32(DeskId);
        writer.WriteInt32(Waiting);
        writer.WriteInt32(LookingLeft);
        writer.WriteTile(QueueTile);
        writer.WriteTile(QueueFace);
        writer.WriteTile(HomeTile);
    }

    internal static VipBehaviour ReadState(SimReader reader)
    {
        var name = VipVisits.NameFromSave(reader, reader.ReadInt32());
        var phase = reader.ReadByte();
        if (phase > (byte)VipPhase.GoingHome)
        {
            throw new InvalidDataException($"Unknown VIP phase {phase}.");
        }

        var vip = new VipBehaviour(name) { Phase = (VipPhase)phase };
        if (reader.Version is 0 or >= VipVisits.SaveVersion)
        {
            vip.Score = reader.ReadInt32();
            vip.VisitedRooms = reader.ReadInt32();
            vip.RoomId = reader.ReadInt32();
            vip.DeskId = reader.ReadInt32();
            vip.Waiting = reader.ReadInt32();
            vip.LookingLeft = reader.ReadInt32();
            vip.QueueTile = reader.ReadTile();
            vip.QueueFace = reader.ReadTile();
            vip.HomeTile = reader.ReadTile();
            return vip;
        }

        // Before the score: the rating from 1 to 15 becomes the score its remark stands for; what CorsixTH's rating
        // counted along the way is dropped.
        vip.Score = VipReport.ScoreOfRemark(reader.ReadInt32());
        _ = reader.ReadInt32(); // CashReward
        _ = reader.ReadInt32(); // ReputationReward
        _ = reader.ReadInt32(); // EnterDeaths
        _ = reader.ReadInt32(); // EnterVisitors
        _ = reader.ReadInt32(); // EnterCures
        _ = reader.ReadInt32(); // EnterPatients
        _ = reader.ReadInt32(); // RoomVisitChance
        vip.VisitedRooms = reader.ReadInt32();
        _ = reader.ReadInt32(); // RoomEvaluation
        vip.RoomId = reader.ReadInt32();
        vip.DeskId = reader.ReadInt32();
        vip.Waiting = reader.ReadInt32();
        vip.LookingLeft = reader.ReadInt32();
        vip.QueueTile = reader.ReadTile();
        vip.QueueFace = reader.ReadTile();
        vip.HomeTile = reader.ReadTile();
        _ = reader.ReadInt32(); // RubbishFound
        _ = reader.ReadInt32(); // VomitFound
        var found = reader.ReadCount(1 << 16);
        for (var i = 0; i < found; i++)
        {
            _ = reader.ReadInt32();
        }

        return vip;
    }
}
