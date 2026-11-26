// Patients' mood, needs and leaving, after the community's logic specification for OpenWards ("Настроение и уход
// пациента"). Not ported from CorsixTH, whose patients feel thirst and the toilet in fractions once a day and leave by
// their health and a 60-day wait; see the pull request that brought it in for where it departs from CorsixTH.

using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Pathfinding;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.People;

/// <summary>
/// Every <see cref="ModifyFreq"/> ticks each patient in a hospital, waiting, in a room or walking, has their warmth, mood,
/// thirst, bowels, nausea, litter and leave counts worked out, and loses a point of health. A patient leaves when the one
/// leave count goes above the level's <see cref="PatientNeedsRules.LeaveMax"/>, not because of any one need: it grows by
/// one each time and by one more for each of being unhappy, needing the toilet and being thirsty, and is reset by every
/// procedure and by the toilet.
/// </summary>
/// <remarks>
/// The mood is kept in <see cref="Humanoid.Happiness"/> (a point is 0.01) and the warmth in <see cref="Humanoid.Warmth"/>
/// (likewise, 0.2 cold and 0.8 hot). Staff's warmth is worked out the same way, and their discontent at the same time
/// (<see cref="Staff.StaffEmployment.Modify"/>).
/// </remarks>
public static class PatientNeeds
{
    /// <summary>Ticks between two updates of the patients' needs (the specification's <c>ModifyFreq</c>).</summary>
    public const int ModifyFreq = 16;

    /// <summary>The most health a patient has.</summary>
    public const int MaxHealth = 1000;

    /// <summary>The most mood, drinks and warmth: they run from 0 to this.</summary>
    public const int MaxPoints = 100;

    /// <summary>Below this the patient is thirsty.</summary>
    public const int Thirsty = 30;

    /// <summary>Below this warmth the patient is cold.</summary>
    public const int Cold = 20;

    /// <summary>Above this warmth the patient is hot.</summary>
    public const int Hot = 80;

    /// <summary>Above this warmth a patient drinks more, and a can cools them down to it.</summary>
    public const int Warm = 60;

    /// <summary>Health below which the patient's state shows in their moods.</summary>
    public const int ShowsHealth = 101;

    /// <summary>What a failed walk to where the patient was going adds to their leave count.</summary>
    public const int FailedWalk = 15;

    /// <summary>Health lost by being sick or doing it on the floor.</summary>
    private const int SpotActionHealth = 10;

    /// <summary>Nausea left after being sick.</summary>
    private const int NauseaAfterVomit = 5;

    /// <summary>How far a radiator warms the corridor, along each axis.</summary>
    private const int RadiatorReach = 8;

    /// <summary>How far a patient sees plants, along each axis.</summary>
    private const int PlantSight = 5;

    /// <summary>How far a thirsty patient looks for a drinks machine.</summary>
    private const int MachineDistance = 8;

    private const string RadiatorType = "radiator";
    private const string PlantType = "plant";
    private const string BenchType = "bench";
    private const string DrinksMachineType = "drinks_machine";

    /// <summary>A patient's mood, 0 to 100.</summary>
    public static int Mood(Humanoid humanoid) => (int)(humanoid.Happiness * MaxPoints);

    /// <summary>A patient's warmth, 0 to 100.</summary>
    public static int Warmth(Humanoid humanoid) => (int)(humanoid.Warmth * MaxPoints);

    /// <summary>Changes a patient's mood by whole points, kept from 0 to 100.</summary>
    internal static void ChangeMood(Humanoid humanoid, int points) =>
        humanoid.Happiness = Math.Clamp(Mood(humanoid) + points, 0, MaxPoints) / (decimal)MaxPoints;

    private static void SetWarmth(Humanoid humanoid, int points) =>
        humanoid.Warmth = Math.Clamp(points, 0, MaxPoints) / (decimal)MaxPoints;

    /// <summary>Whether this tick updates the patients' needs.</summary>
    internal static bool IsDue(long hours) => hours % ModifyFreq == 0;

    /// <summary>The update of every patient's needs, once every <see cref="ModifyFreq"/> ticks.</summary>
    internal static void Modify(Simulation simulation)
    {
        var world = World.Of(simulation);
        foreach (var humanoid in simulation.Entities.OfType<Humanoid>().ToList())
        {
            if (humanoid.Behaviour is PatientBehaviour patient && Takes(humanoid, patient))
            {
                Modify(simulation, world, humanoid, patient);
            }
            else if (humanoid is { Profile.Class: not StaffClass.Receptionist, IsFired: false, IsDespawning: false, IsLeavingForPoacher: false }
                && humanoid.Employer >= 0 && humanoid.Employer < simulation.Hospitals.Count)
            {
                // Receptionists are left out: they never tire, warmth does not touch them, nor does their discontent change.
                Staff.Tiredness.Modify(simulation, humanoid);
                ModifyStaff(simulation, world, humanoid);
                Staff.StaffEmployment.Modify(simulation, humanoid);
            }
        }
    }

    /// <summary>
    /// A member of staff's warmth, worked out as a patient's: it moves a point towards what the radiators give, and
    /// above <see cref="Hot"/> or below <see cref="Cold"/> it costs a point of mood.
    /// </summary>
    private static void ModifyStaff(Simulation simulation, World world, Humanoid humanoid)
    {
        var roomId = simulation.Map[humanoid.Tile.X, humanoid.Tile.Y].RoomId;
        var warmth = Warmth(humanoid);
        SetWarmth(humanoid, warmth + Math.Sign(WarmthTarget(simulation, world, humanoid, roomId) - warmth));
        warmth = Warmth(humanoid);
        if (warmth is > Hot or < Cold)
        {
            Feelings.ChangeHappiness(simulation, humanoid, -1m / MaxPoints);
        }
    }

    /// <summary>Whether the patient's needs go on: in a hospital, not on the way home, out or dead.</summary>
    private static bool Takes(Humanoid humanoid, PatientBehaviour patient) =>
        humanoid.HospitalPlayer >= 0 && !humanoid.IsDespawning
        && patient.Phase is not (PatientPhase.GoingHome or PatientPhase.Dying);

    private static void Modify(Simulation simulation, World world, Humanoid humanoid, PatientBehaviour patient)
    {
        var rules = simulation.Rules.Needs;
        var random = simulation.Random;
        var map = simulation.Map;
        var roomId = map[humanoid.Tile.X, humanoid.Tile.Y].RoomId;

        // 1. Warmth moves a point towards what the radiators give; warm patients drink more.
        var warmth = Warmth(humanoid);
        var target = WarmthTarget(simulation, world, humanoid, roomId);
        SetWarmth(humanoid, warmth + Math.Sign(target - warmth));
        warmth = Warmth(humanoid);
        if (warmth > Warm)
        {
            patient.DrinkStock = Math.Max(0, patient.DrinkStock - 2);
        }

        // 2. Plants: more healthy ones than withered cheer up and settle the stomach, fewer do the opposite.
        var plants = world.PlantBalance(humanoid.Tile);
        if (plants != 0)
        {
            ChangeMood(humanoid, Math.Sign(plants));
            patient.Nausea = Math.Max(0, patient.Nausea - Math.Sign(plants));
        }

        // 3. Litter on the patient's tile and the four next to it, outside rooms.
        if (roomId == 0)
        {
            SeeLitter(simulation, world, humanoid, patient);
        }

        // 4. A contagious patient may pass the disease on.
        Disasters.Epidemics.Spread(simulation, humanoid, patient);

        var leaveBefore = patient.LeaveCount;

        // 5. Time passes: the wait grows, the drinks run down, and one time in three the bowels fill.
        patient.LeaveCount++;
        patient.DrinkStock = Math.Max(0, patient.DrinkStock - 1);
        if (random.Next(3) == 0)
        {
            patient.Bowel++;
        }

        // 6. Warmth and mood.
        if (warmth > Hot)
        {
            ChangeMood(humanoid, -1);
            patient.Nausea++;
        }
        else if (warmth < Cold)
        {
            ChangeMood(humanoid, -1);
        }
        else
        {
            ChangeMood(humanoid, 1);
        }

        // 7. Each grievance makes the wait feel longer.
        if (Mood(humanoid) < rules.Unhappy)
        {
            patient.LeaveCount++;
        }

        if (patient.Bowel > rules.BowelFull)
        {
            patient.LeaveCount++;
        }

        if (patient.DrinkStock < Thirsty)
        {
            patient.LeaveCount++;
        }

        // 8. The disease takes its toll, but not on emergency patients.
        if (patient.EmergencyIndex == 0 && !patient.IsCured)
        {
            patient.Health = Math.Max(0, patient.Health - 1);
        }

        // Standing in a queue when a bench is free.
        if (patient.Phase is PatientPhase.InReceptionQueue or PatientPhase.InDoorQueue && patient.BenchId == 0
            && world.HasFreeBench(humanoid.HospitalPlayer))
        {
            ChangeMood(humanoid, -1);
        }

        patient.IsThirsty = patient.DrinkStock < Thirsty;
        patient.IsNeedingToilet = patient.Bowel > rules.BowelFull;
        patient.IsSad = Mood(humanoid) < rules.Unhappy;
        if (patient.Health < 1 && !patient.IsCured)
        {
            patient.SetToDie();
            return;
        }

        if (roomId == 0 && !patient.IsSidetracked)
        {
            InCorridor(simulation, world, humanoid, patient, rules);
        }

        if (patient.IsWaitingForTurn)
        {
            WhileWaiting(simulation, world, humanoid, patient, rules);
            patient.ShowImpatience(humanoid, leaveBefore, rules.LeaveMax);
        }
    }

    /// <summary>
    /// Where the patient's warmth heads. In a corridor, each radiator within <see cref="RadiatorReach"/> tiles along both
    /// axes adds ⌊heat × 10 / (|dx| + |dy|)⌋; in a room, its radiators give ⌊radiators × heat × 250 / area⌋, which is the
    /// specification's ⌊radiators × 1250 / area⌋ at the default heat of 5. With the heating at 0, nothing.
    /// </summary>
    internal static int WarmthTarget(Simulation simulation, World world, Humanoid humanoid, int roomId)
    {
        var player = humanoid.HospitalPlayer;
        if (player >= simulation.Hospitals.Count)
        {
            return 0;
        }

        var heat = simulation.Hospitals[player].RadiatorHeat;
        if (heat <= 0)
        {
            return 0;
        }

        var target = 0;
        if (roomId != 0)
        {
            if (simulation.FindEntity(roomId) is Room room && room.Area.Width * room.Area.Height is var area and > 0)
            {
                var radiators = world.Radiators.Count(radiator => radiator.RoomId == roomId);
                target = radiators * heat * 250 / area;
            }
        }
        else
        {
            foreach (var radiator in world.Radiators)
            {
                var dx = Math.Abs(radiator.Tile.X - humanoid.Tile.X);
                var dy = Math.Abs(radiator.Tile.Y - humanoid.Tile.Y);
                if (radiator.RoomId == 0 && dx <= RadiatorReach && dy <= RadiatorReach)
                {
                    target += heat * 10 / Math.Max(1, dx + dy);
                }
            }
        }

        return Math.Min(target, MaxPoints);
    }

    /// <summary>
    /// Step 3: for each of the patient's tile and the four next to it, puke or pee costs 2 mood and adds 2 nausea and 2 to
    /// the litter count; other litter 1 of each.
    /// </summary>
    private static void SeeLitter(Simulation simulation, World world, Humanoid humanoid, PatientBehaviour patient)
    {
        ReadOnlySpan<(int X, int Y)> around = [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)];
        foreach (var (x, y) in around)
        {
            var tile = new TilePosition(humanoid.Tile.X + x, humanoid.Tile.Y + y);
            if (!simulation.Map.Contains(tile.X, tile.Y) || simulation.Map[tile.X, tile.Y].RoomId != 0)
            {
                continue;
            }

            var points = world.LitterAt(tile);
            if (points > 0)
            {
                ChangeMood(humanoid, -points);
                patient.Nausea += points;
                patient.LitterCount += points;
            }
        }
    }

    /// <summary>
    /// What a patient in a corridor may do: be sick (nausea at the level's limit, one time in five), do it on the floor
    /// (bowels at the level's limit, one time in five) and drop litter (litter count over the level's limit, or now and
    /// then anyway, one time in ten).
    /// </summary>
    private static void InCorridor(Simulation simulation, World world, Humanoid humanoid, PatientBehaviour patient, PatientNeedsRules rules)
    {
        var random = simulation.Random;
        var animations = simulation.Content.FindPatientAnimations(humanoid.Type);
        if (patient.Nausea >= rules.VomitLimit && random.Next(5) == 0 && animations?.Vomit is not null
            && patient.Vomit(simulation, humanoid))
        {
            patient.Health = Math.Max(0, patient.Health - SpotActionHealth);
            patient.Nausea = NauseaAfterVomit;
            return;
        }

        if (patient.Bowel >= rules.BowelOverflows && random.Next(5) == 0 && animations?.Pee is not null
            && patient.Pee(simulation, humanoid))
        {
            patient.Health = Math.Max(0, patient.Health - SpotActionHealth);
            patient.Bowel = 0;
            return;
        }

        var wantsTo = patient.LitterCount > rules.LitterDrop || (rules.LitterRandom > 0 && random.Next(rules.LitterRandom) == 0);
        if (wantsTo && random.Next(10) == 0 && Littering.Drop(simulation, humanoid))
        {
            patient.LitterCount = 0;
        }
    }

    /// <summary>
    /// A waiting patient's checks, in order: gone over the leave limit, they go home; otherwise thirsty with a drinks
    /// machine near, they go and buy a can; otherwise needing the toilet with toilets in the hospital, they go.
    /// </summary>
    private static void WhileWaiting(Simulation simulation, World world, Humanoid humanoid, PatientBehaviour patient, PatientNeedsRules rules)
    {
        if (patient.LeaveCount > rules.LeaveMax)
        {
            patient.LeaveFedUpWaiting(simulation, humanoid);
            return;
        }

        // Busy with a can or something on the spot; a patient on a bench gets up for a can (GoForDrink).
        if (patient.DrinksMachineId != 0 || patient.SpotAction != PatientSpotAction.None)
        {
            return;
        }

        if (patient.DrinkStock < Thirsty && world.HasDrinksMachine && FindMachineNear(simulation, world, humanoid) is { } machine)
        {
            if (!patient.GoForDrink(simulation, humanoid, machine))
            {
                patient.LeaveCount += FailedWalk;
            }

            return;
        }

        if (patient.Bowel > rules.BowelFull && patient.GoingToToilet == ToiletVisit.No && patient.BenchId == 0)
        {
            if (!RoomVisits.HasRoom(simulation, humanoid.HospitalPlayer, RoomVisits.ToiletsRoom))
            {
                patient.GoingToToilet = ToiletVisit.NoToilets;
            }
            else if (!patient.GoToToilets(simulation, humanoid))
            {
                patient.LeaveCount += FailedWalk;
            }
        }
    }

    /// <summary>The nearest drinks machine by the object visitor, within <see cref="MachineDistance"/> steps.</summary>
    private static RoomObject? FindMachineNear(Simulation simulation, World world, Humanoid humanoid)
    {
        RoomObject? found = null;
        ObjectVisitor.Visit(simulation.Map, humanoid.Tile, MachineDistance, tile => world.DrinksMachines.TryGetValue(tile, out found));
        return found;
    }

    /// <summary>What the update needs to know of the world, gathered once for all patients.</summary>
    internal sealed class World
    {
        private readonly Dictionary<TilePosition, int> _litter = [];
        private readonly List<(TilePosition Tile, bool Healthy)> _plants = [];
        private readonly HashSet<int> _freeBenches = [];

        private World()
        {
        }

        /// <summary>Every radiator with the room it stands in, or 0.</summary>
        public List<(TilePosition Tile, int RoomId)> Radiators { get; } = [];

        /// <summary>The drinks machines by their tile.</summary>
        public Dictionary<TilePosition, RoomObject> DrinksMachines { get; } = [];

        public bool HasDrinksMachine => DrinksMachines.Count > 0;

        public static World Of(Simulation simulation)
        {
            var world = new World();
            var map = simulation.Map;
            var usedBenches = new HashSet<int>();
            var benches = new List<RoomObject>();
            foreach (var entity in simulation.Entities)
            {
                switch (entity)
                {
                    case RoomObject { IsDestroyed: false } placed:
                        switch (placed.ObjectType)
                        {
                            case RadiatorType:
                                world.Radiators.Add((placed.Tile, map.Contains(placed.Tile.X, placed.Tile.Y) ? map[placed.Tile.X, placed.Tile.Y].RoomId : 0));
                                break;
                            case PlantType:
                                // Only corridor plants count, healthy while they have water left (the original's rule).
                                if (placed.RoomId == 0)
                                {
                                    world._plants.Add((placed.Tile, placed.Plant is not { IsWithered: true }));
                                }
                                break;
                            case BenchType when placed.RoomId == 0:
                                benches.Add(placed);
                                break;
                            case DrinksMachineType:
                                world.DrinksMachines.TryAdd(placed.Tile, placed);
                                break;
                        }

                        break;
                    case Litter { IsRemoved: false } litter:
                        var points = litter.IsVomitInducing ? 2 : 1;
                        world._litter[litter.Tile] = Math.Max(world._litter.GetValueOrDefault(litter.Tile), points);
                        break;
                    case Humanoid { Behaviour: PatientBehaviour { BenchId: not 0 } sitter }:
                        usedBenches.Add(sitter.BenchId);
                        break;
                }
            }

            foreach (var bench in benches)
            {
                if (!usedBenches.Contains(bench.Id) && map.Contains(bench.Tile.X, bench.Tile.Y)
                    && map.GetParcelOwner(map[bench.Tile.X, bench.Tile.Y].Parcel) is var owner and > 0)
                {
                    world._freeBenches.Add(owner - 1);
                }
            }

            return world;
        }

        /// <summary>What litter on the tile costs: 2 for puke or pee, 1 for other litter, 0 for none.</summary>
        public int LitterAt(TilePosition tile) => _litter.GetValueOrDefault(tile);

        /// <summary>Whether the hospital has a corridor bench nobody sits on or is going to.</summary>
        public bool HasFreeBench(int player) => _freeBenches.Contains(player);

        /// <summary>Healthy plants within <see cref="PlantSight"/> tiles along both axes, less withered ones.</summary>
        public int PlantBalance(TilePosition tile)
        {
            var balance = 0;
            foreach (var (at, healthy) in _plants)
            {
                if (Math.Abs(at.X - tile.X) <= PlantSight && Math.Abs(at.Y - tile.Y) <= PlantSight)
                {
                    balance += healthy ? 1 : -1;
                }
            }

            return balance;
        }
    }
}
