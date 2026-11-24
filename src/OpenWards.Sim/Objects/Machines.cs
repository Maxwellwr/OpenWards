// Machines wearing out, breaking down and being repaired. The wear, the explosion chance and the loss of strength on
// repair follow the community's logic specification of the original game; the rest is ported from CorsixTH
// CorsixTH/Lua/entities/machine.lua (Machine:Machine, machineUsed, explodeMachine, replaceMachine), cheats.lua
// (cheatRepairAllMachines, invulnerable_machines) and room.lua (Room:crashRoom).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;
using OpenWards.Sim.Research;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Objects;

/// <summary>
/// A machine's wear: its strength s, the uses that wear it out completely (from the hospital's research of it), and its
/// wear w, the uses since the last repair. It smokes from 75 % wear, the adviser warns from 85 %, and from 100 % each use
/// may make it explode, surely by 150 %.
/// </summary>
public sealed class MachineState
{
    internal MachineState(int strength) => Strength = strength;

    /// <summary>Uses it stands between repairs.</summary>
    public int Strength { get; internal set; }

    /// <summary>Uses since it was built or last repaired.</summary>
    public int TimesUsed { get; internal set; }

    /// <summary>Uses since it was built (<c>total_usage</c>).</summary>
    public int TotalUsage { get; internal set; }

    /// <summary>Uses left before it is worn out; at 0 or below it may explode.</summary>
    public int RemainingUses => Strength - TimesUsed;

    /// <summary>The wear in percent of the strength.</summary>
    public int WearPercent => Strength > 0 ? (int)(100L * TimesUsed / Strength) : 100;

    /// <summary>Worn past a third of its strength (w > ⌊s / 3⌋): a handyman is asked to repair it.</summary>
    public bool NeedsRepair => TimesUsed > Strength / 3;

    /// <summary>Smoking: worn 75 % or more.</summary>
    public bool IsBreaking => 4L * TimesUsed >= 3L * Strength;

    /// <summary>Worn 85 % or more: the adviser warns.</summary>
    public bool IsNearlyWornOut => 100L * TimesUsed >= 85L * Strength;

    /// <summary>
    /// The player called a handyman from the machine window (<c>setRepairingStatus</c>, the room's <c>manual_repair</c> and
    /// <c>needs_repair</c>): the room lets no patient in until the machine is repaired.
    /// </summary>
    public bool IsRepairCalled { get; internal set; }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Strength);
        writer.WriteInt32(TimesUsed);
        writer.WriteInt32(TotalUsage);
        writer.WriteBool(IsRepairCalled);
    }

    internal static MachineState Read(SimReader reader) =>
        new(reader.ReadInt32())
        {
            TimesUsed = reader.ReadInt32(),
            TotalUsage = reader.ReadInt32(),
            IsRepairCalled = reader.Version is 0 or >= 73 && reader.ReadBool(),
        };
}

/// <summary>What happens to machines as they are used.</summary>
internal static class Machines
{
    /// <summary>
    /// An explosion costs this many percent of the visits in reputation points (about −50), as the community describes the
    /// original game; CorsixTH takes 50 off the reputation.
    /// </summary>
    internal const int ExplosionReputationPercent = -10;

    /// <summary>A machine that is placed starts as strong as the hospital's research has made it (<c>start_strength</c>).</summary>
    internal static void OnPlaced(Simulation simulation, RoomObject placed, int player)
    {
        if (ResearchObjects.Find(placed.ObjectType) is not { IsMachine: true } research
            || simulation.Content.FindObject(placed.ObjectType)?.Machine is null)
        {
            return;
        }

        var strength = player >= 0 && player < simulation.Hospitals.Count
            && simulation.Hospitals[player].Research.Objects.TryGetValue(placed.ObjectType, out var progress) && progress.StartStrength > 0
                ? progress.StartStrength
                : research.DefaultStrength;
        placed.Machine = new MachineState(strength);
        Rooms.RoomRebuild.OnMachinePlaced(simulation, placed, player);
    }

    /// <summary>
    /// After someone other than a handyman has used it: one use more, up to twice its strength; worn out it may explode,
    /// which crashes the room.
    /// </summary>
    internal static void Used(Simulation simulation, RoomObject placed, Humanoid user)
    {
        if (placed.Machine is not { } machine || user.Profile?.Class == Staff.StaffClass.Handyman
            || simulation.FindEntity(placed.RoomId) is not Room { IsCrashed: false } room)
        {
            return;
        }

        machine.TotalUsage++;

        // The invulnerable machines cheat: only the total usage counts, and nothing wears or explodes.
        if (!IsInvulnerable(simulation, room))
        {
            machine.TimesUsed = Math.Min(machine.TimesUsed + 1, 2 * machine.Strength);
            CheckExplosion(simulation, room, machine);
        }
    }

    /// <summary>Whether the room's hospital has the invulnerable machines cheat on.</summary>
    private static bool IsInvulnerable(Simulation simulation, Room room) =>
        RoomVisits.Owner(simulation, room) is var owner && owner >= 0 && owner < simulation.Hospitals.Count
        && (simulation.Hospitals[owner].ActiveCheats & ToggleCheats.InvulnerableMachines) != 0;

    /// <summary>
    /// <c>Cheats:cheatRepairAllMachines</c>: every machine of the player's hospital (in rooms that have not crashed) is as
    /// good as new at once.
    /// </summary>
    /// <remarks>
    /// CorsixTH means to repair without reducing strength, but its <c>machineRepaired</c> turns the false it is given into
    /// true (<c>strength_reducible or true</c>); here the strength is kept, as intended.
    /// </remarks>
    internal static void RepairAll(Simulation simulation, int player)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject { Machine: { } machine, IsDestroyed: false } placed
                && simulation.FindEntity(placed.RoomId) is Room { IsCrashed: false } room && RoomVisits.Owner(simulation, room) == player)
            {
                machine.TimesUsed = 0;
                machine.IsRepairCalled = false;
            }
        }
    }

    /// <summary>
    /// The damage of an earthquake, once at the end of its main quake: every machine in every room that has not crashed
    /// wears by the quake's size, past the usual limit of twice its strength, and may smoke or explode as after a use. It
    /// returns how many machines it damaged.
    /// </summary>
    internal static int EarthquakeImpact(Simulation simulation, int size)
    {
        var damaged = 0;
        foreach (var placed in simulation.Entities.OfType<RoomObject>().Where(o => o.Machine is not null && !o.IsDestroyed).ToList())
        {
            if (simulation.FindEntity(placed.RoomId) is Room { IsCrashed: false } room && !IsInvulnerable(simulation, room) && size > 0)
            {
                placed.Machine!.TimesUsed += size;
                damaged++;
                CheckExplosion(simulation, room, placed.Machine);
            }
        }

        return damaged;
    }

    /// <summary>Worn out, the machine may explode and crash its room.</summary>
    private static void CheckExplosion(Simulation simulation, Room room, MachineState machine)
    {
        if (MustExplode(simulation.Random, machine))
        {
            Crash(simulation, room);
        }
    }

    /// <summary>
    /// From 100 % wear (w ≥ s) a machine explodes with a chance of 1 / (⌊1.5 × s⌋ − w + 1), surely once w reaches
    /// ⌊1.5 × s⌋: with a strength of 8, one in five on the 8th use, then one in four, three, two, and surely on the 12th.
    /// </summary>
    /// <remarks>
    /// CorsixTH lets it explode surely without a fire extinguisher in the room, and otherwise at 2 / s plus a fifth for
    /// each use past, less a twentieth for each extinguisher. The specification knows no extinguishers.
    /// </remarks>
    internal static bool MustExplode(SimRandom random, MachineState machine)
    {
        if (machine.TimesUsed < machine.Strength)
        {
            return false;
        }

        var odds = (3 * machine.Strength / 2) - machine.TimesUsed + 1;
        return odds <= 1 || random.Next(odds) == 0;
    }

    /// <summary>
    /// A handyman's repair: its wear goes back to 0, and with a chance of w / s (taken before) it loses a use of strength
    /// for good, while above 2; repaired at 100 % wear or more it always does.
    /// </summary>
    internal static void Repaired(Simulation simulation, RoomObject placed)
    {
        if (placed.Machine is not { } machine)
        {
            return;
        }

        if (machine.Strength > 2 && machine.TimesUsed > 0
            && (machine.TimesUsed >= machine.Strength || simulation.Random.Next(machine.Strength) < machine.TimesUsed))
        {
            machine.Strength--;
        }

        machine.TimesUsed = 0;
        machine.IsRepairCalled = false;
    }

    /// <summary>
    /// The machine window's call button (<c>UIMachine:callHandyman</c>): unless the machine is as good as new, its repair
    /// becomes an urgent task and its room lets no patient in until then.
    /// </summary>
    internal static void CallHandyman(RoomObject placed)
    {
        if (placed.Machine is { TimesUsed: not 0 } machine)
        {
            machine.IsRepairCalled = true;
        }
    }

    /// <summary>
    /// The machine window's replace button (<c>UIMachine:replaceMachine</c>, <c>Machine:replaceMachine</c>): the hospital pays
    /// what the machine costs now and gets a new one, as strong as new ones of its type are now. False without the money.
    /// </summary>
    internal static bool Replace(Simulation simulation, Hospital hospital, RoomObject placed)
    {
        if (placed.Machine is not { } machine)
        {
            return false;
        }

        var cost = hospital.Research.ObjectCost(simulation.Rules, placed.ObjectType);
        if (hospital.Balance < cost)
        {
            return false;
        }

        hospital.Spend(simulation.Hours, cost, TransactionKind.MachineReplacement, placed.ObjectType);
        machine.TotalUsage = 0;
        machine.TimesUsed = 0;
        machine.IsRepairCalled = false;
        machine.Strength = hospital.Research.Objects.TryGetValue(placed.ObjectType, out var progress) && progress.StartStrength > 0
            ? progress.StartStrength
            : ResearchObjects.Find(placed.ObjectType)?.DefaultStrength ?? machine.Strength;
        return true;
    }

    /// <summary>
    /// <c>explodeMachine</c> and <c>Room:crashRoom</c>: everyone in the room dies, its other objects go, soot covers its floor
    /// and walls, the hospital loses the room's value and 10 % of the visits in reputation, counts the explosion for the
    /// year's judgement of its machines, and the room stays a wreck until it is taken down.
    /// </summary>
    internal static void Crash(Simulation simulation, Room room)
    {
        var owner = RoomVisits.Owner(simulation, room);
        room.IsCrashed = true;
        room.IsActive = false;
        Disasters.Rats.RemoveRatholesAroundRoom(simulation, room);
        room.StaffId = 0;
        room.StaffPresent = false;
        room.PatientId = 0;

        // Patients queueing at the door look for another room.
        if (room.DoorQueue is { } queue)
        {
            foreach (var id in queue.Members.ToList())
            {
                if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour patient } waiting)
                {
                    patient.LeaveClosedRoom(simulation, waiting, room);
                }
            }
        }

        foreach (var humanoid in simulation.Entities.OfType<Humanoid>().ToList())
        {
            var inside = room.Area.Contains(humanoid.Tile) || (humanoid.IsCrossingDoor && room.Area.Contains(humanoid.StepTarget));
            if (!inside || humanoid.IsDespawning)
            {
                continue;
            }

            humanoid.ClearActionAnimation();
            if (humanoid.Behaviour is PatientBehaviour patient)
            {
                patient.Die(simulation, humanoid, animated: false);
            }
            else
            {
                humanoid.StopWalking();
                humanoid.IsDespawning = true;
            }
        }

        foreach (var placed in simulation.Entities.OfType<RoomObject>().Where(o => o.RoomId == room.Id && o.Machine is null))
        {
            placed.IsDestroyed = true;
        }

        ObjectPlacer.Reoccupy(simulation, room);

        for (var x = room.Area.X; x < room.Area.Right; x++)
        {
            for (var y = room.Area.Y; y < room.Area.Bottom; y++)
            {
                Litter.Drop(simulation, owner, LitterType.SootFloor, new(x, y), false);
            }
        }

        for (var x = room.Area.X; x < room.Area.Right; x++)
        {
            var tile = new Maps.TilePosition(x, room.Area.Y);
            Litter.Drop(simulation, owner, simulation.Content.IsWindowWall(simulation.Map[tile.X, tile.Y].NorthWall) ? LitterType.SootWindow : LitterType.SootWall, tile, true);
        }

        for (var y = room.Area.Y; y < room.Area.Bottom; y++)
        {
            var tile = new Maps.TilePosition(room.Area.X, y);
            Litter.Drop(simulation, owner, simulation.Content.IsWindowWall(simulation.Map[tile.X, tile.Y].WestWall) ? LitterType.SootWindow : LitterType.SootWall, tile, false);
        }

        if (owner >= 0 && owner < simulation.Hospitals.Count)
        {
            var hospital = simulation.Hospitals[owner];
            hospital.Explosions++;
            if (simulation.Content.FindRoom(room.RoomType) is { } definition)
            {
                hospital.ChangeValue(-RoomPurchases.BuildCost(simulation, hospital, definition));
            }

            hospital.ChangeReputationPercent(ExplosionReputationPercent);
        }
    }
}
