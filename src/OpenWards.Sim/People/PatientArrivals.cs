// Ported from CorsixTH CorsixTH/Lua/world.lua (calculateSpawnTiles, spawnPatient, _findClosestSpawnToDesk,
// onEndMonth, _prepareSpawnDates, _prepareSpawnHours, and chooseHospitalForPatient with the reputation-based choice it
// has commented out), CorsixTH/Lua/hospital.lua (Hospital:getDiseaseReputationPriceFactor) and
// CorsixTH/Lua/app.lua (math.p_random).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Calendar;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.People;

/// <summary>
/// When patients arrive, as CorsixTH decides it: each month the spawn rate (patients per month) changes
/// by the level's <c>popn</c> setting and the arrivals of the month are spread over its days with
/// Poisson-distributed gaps; each day the arrivals of the next day get random hours. A patient only comes
/// to a hospital with a staffed reception desk, and enters the map at one of its spawn points.
/// </summary>
/// <remarks>
/// As in CorsixTH, patients come once the first hospital has opened (the countdown at the start of a level). They are
/// shared between the players' hospitals and the computer opponents' (<see cref="Simulation.Competitors"/>), which only
/// count theirs, as the original game shares them (<see cref="ChooseHospital"/>): first in turn, then by reputation and
/// price. Unlike CorsixTH, whose other hospitals are computer players, a player's hospital that has not opened yet gets
/// none: a patient sent to it is lost as if it had no desk.
/// </remarks>
public sealed class PatientArrivals
{
    /// <summary>Days of a month are numbered 1 to 31.</summary>
    private const int MaxDays = 32;

    /// <summary>Roujin's challenge's extra patients a month (<c>roujin_bonus</c>).</summary>
    private const int RoujinBonus = 40;

    private readonly int[] _spawnDates = new int[MaxDays];
    private readonly int[] _spawnHours = new int[GameDate.HoursPerDay];
    private List<TilePosition>? _spawnPoints;

    private PatientArrivals()
    {
    }

    /// <summary>Patients per month.</summary>
    public int SpawnRate { get; private set; }

    /// <summary>How much <see cref="SpawnRate"/> changes at the end of each month.</summary>
    public int MonthlyIncrease { get; private set; }

    /// <summary>
    /// Where patients enter and leave the map: up to 8 tiles on each edge that can be reached from the
    /// hospital. Derived from the map, which only changes inside the hospital; not saved.
    /// </summary>
    public IReadOnlyList<TilePosition> SpawnPoints => _spawnPoints ?? [];

    /// <summary>Patients arriving on each day of the month (index 1 to 31).</summary>
    public IReadOnlyList<int> SpawnDates => _spawnDates;

    /// <summary>Patients arriving at each hour of the coming day.</summary>
    public IReadOnlyList<int> SpawnHours => _spawnHours;

    /// <summary>Sets up arrivals for a new game, as <c>World:World</c>.</summary>
    internal static PatientArrivals Start(Simulation simulation)
    {
        var population = simulation.Rules.Population;
        var arrivals = new PatientArrivals();
        arrivals.SpawnRate = population.Count > 0 ? population[0].Change : 0;
        arrivals.MonthlyIncrease = arrivals.SpawnRate;
        arrivals.PrepareSpawnDates(simulation, firstMonth: true);
        return arrivals;
    }

    /// <summary>The spawn points of the map, worked out the first time they are needed.</summary>
    internal IReadOnlyList<TilePosition> SpawnPointsOf(LevelMap map) => _spawnPoints ??= CalculateSpawnTiles(map);

    /// <summary><c>World:onEndDay</c>: the next day's patients get their hours.</summary>
    internal void OnEndDay(Simulation simulation) => PrepareSpawnHours(simulation);

    /// <summary>
    /// Runs before the entities update on each tick, after the end of a day: the patients of the coming
    /// hour arrive (CorsixTH's <c>spawn_hours</c> in <c>World:onTick</c>).
    /// </summary>
    internal void SpawnPatients(Simulation simulation)
    {
        // As in CorsixTH, patients come only once the first hospital has opened.
        if (simulation.Hospitals.Count == 0 || !simulation.Hospitals[0].IsOpen)
        {
            return;
        }

        var count = _spawnHours[GameDate.FromHours(simulation.Hours + 1).Hour];
        for (var i = 0; i < count; i++)
        {
            if (simulation.Rules.Allocation.InTurnIn(simulation.Date.MonthOfGame))
            {
                SpawnPatient(simulation, ChooseHospital(simulation));
            }
            else
            {
                // As World:spawnPatient, the disease first, by the local player's hospital, then the hospital for it.
                var disease = PatientCare.NewPatientDisease(simulation, simulation.Hospitals[0]);
                SpawnPatient(simulation, ChooseHospital(simulation, disease), disease);
            }
        }
    }

    /// <summary>
    /// <c>World:chooseHospitalForPatient</c> as the original game shares patients, which CorsixTH has commented out for want of
    /// opponents' reputations: in the first months of the level (<see cref="PatientAllocationRules.Delay"/>) the hospitals take
    /// turns, by the patients each was sent so far, whether they came or not. Then one patient in
    /// <see cref="PatientAllocationRules.RandomChance"/> goes to a hospital at random, and the others to a hospital drawn
    /// by how much it appeals (<see cref="Appeal"/>).
    /// </summary>
    /// <param name="simulation">The game.</param>
    /// <param name="disease">The new patient's disease, if any; for the hospitals' reputations for it and their prices.</param>
    /// <returns>A player's hospital, or past the players' a computer opponent's, counting from the first opponent.</returns>
    internal static int ChooseHospital(Simulation simulation, DiseaseDefinition? disease = null)
    {
        var hospitals = simulation.Hospitals.Count + simulation.Competitors.Count;
        var allocation = simulation.Rules.Allocation;
        if (allocation.InTurnIn(simulation.Date.MonthOfGame))
        {
            long attempts = 0;
            foreach (var hospital in simulation.Hospitals)
            {
                attempts += hospital.SpawnAttempts;
            }

            foreach (var competitor in simulation.Competitors)
            {
                attempts += competitor.SpawnAttempts;
            }

            return (int)(attempts % hospitals);
        }

        if (allocation.RandomChance > 0 && simulation.Random.Next(allocation.RandomChance) == 0)
        {
            return simulation.Random.Next(hospitals);
        }

        var appeals = new long[hospitals];
        long total = 0;
        for (var i = 0; i < hospitals; i++)
        {
            appeals[i] = Appeal(simulation, i, disease);
            total += appeals[i];
        }

        if (total <= 0)
        {
            return simulation.Random.Next(hospitals);
        }

        // A number from 0 to the sum of the appeals, against each hospital's share of it in turn.
        var threshold = NextLong(simulation.Random, total);
        for (var i = 0; i < hospitals; i++)
        {
            threshold -= appeals[i];
            if (threshold < 0)
            {
                return i;
            }
        }

        return hospitals - 1;
    }

    /// <summary>
    /// How much a hospital appeals to a new patient with the disease: its reputation times <c>gbv.AllocTotalRep</c> plus its
    /// reputation for the disease times <c>gbv.AllocIndRep</c>, times what its price is worth for that reputation
    /// (<c>Hospital:getDiseaseReputationPriceFactor</c>: the reputation for the disease / 500 / the price's share of the
    /// standard price, from 0.5 to 1.5). A computer opponent charges the standard price, and its reputation is also its
    /// reputation for every disease.
    /// </summary>
    /// <param name="simulation">The game.</param>
    /// <param name="index">A player's hospital, or past the players' a computer opponent's.</param>
    /// <param name="disease">The patient's disease, if any.</param>
    public static long Appeal(Simulation simulation, int index, DiseaseDefinition? disease)
    {
        int reputation;
        int diseaseReputation;
        var pricePercent = DiseaseCase.StandardPrice;
        if (index < simulation.Hospitals.Count)
        {
            var hospital = simulation.Hospitals[index];
            reputation = hospital.Reputation;
            diseaseReputation = reputation;
            if (disease is not null)
            {
                // Peeking, so that choosing a hospital does not add to the casebook.
                var entry = hospital.PeekCase(disease, simulation.Rules.Diseases);
                diseaseReputation = entry.Reputation ?? reputation;
                pricePercent = entry.PricePercent;
            }
        }
        else
        {
            reputation = simulation.Competitors[index - simulation.Hospitals.Count].Reputation;
            diseaseReputation = reputation;
        }

        var allocation = simulation.Rules.Allocation;
        var priceFactor = Math.Clamp(diseaseReputation * (decimal)DiseaseCase.StandardPrice / (Hospital.AverageReputation * pricePercent), 0.5m, 1.5m);
        return (long)(((long)allocation.TotalReputation * reputation + ((long)allocation.DiseaseReputation * diseaseReputation)) * priceFactor);
    }

    /// <summary>A number from 0 to below <paramref name="maxExclusive"/>, which may be beyond what an int holds.</summary>
    private static long NextLong(SimRandom random, long maxExclusive)
    {
        return maxExclusive <= int.MaxValue
            ? random.Next((int)maxExclusive)
            : (long)((ulong)(((long)random.Next(int.MaxValue) << 31) | (uint)random.Next(int.MaxValue)) % (ulong)maxExclusive);
    }

    /// <summary>
    /// <c>World:spawnPatient</c>: a patient for the hospital, if it has a staffed reception desk, at a random
    /// spawn point, or for the hospital's first patient the one with the shortest way to a desk.
    /// </summary>
    internal Humanoid? SpawnPatient(Simulation simulation, int player) => SpawnPatient(simulation, player, chosen: false, disease: null);

    /// <summary><see cref="SpawnPatient(Simulation, int)"/> for a patient whose disease was chosen first.</summary>
    internal Humanoid? SpawnPatient(Simulation simulation, int player, DiseaseDefinition? disease) => SpawnPatient(simulation, player, chosen: true, disease);

    private Humanoid? SpawnPatient(Simulation simulation, int player, bool chosen, DiseaseDefinition? disease)
    {
        SpawnPointsOf(simulation.Map);
        if (player >= simulation.Hospitals.Count + simulation.Competitors.Count || _spawnPoints!.Count == 0)
        {
            return null;
        }

        if (player >= simulation.Hospitals.Count)
        {
            // An AIHospital has no reception desk, so World:spawnPatient only counts the patient. The disease is still
            // chosen first, by the local player's hospital as World:getNewPatientDisease does.
            if (!chosen)
            {
                _ = PatientCare.NewPatientDisease(simulation, simulation.Hospitals[0]);
            }

            simulation.Competitors[player - simulation.Hospitals.Count].CountNewPatient();
            return null;
        }

        // The disease is chosen first, even when no patient comes after all.
        var hospital = simulation.Hospitals[player];
        if (!chosen)
        {
            disease = PatientCare.NewPatientDisease(simulation, hospital);
        }


        // Counted even when no patient comes, as CorsixTH's spawn_attempts.
        hospital.SpawnAttempts++;
        var desks = ReceptionDesks.OfHospital(simulation, player, staffedOnly: true).ToList();
        if (desks.Count == 0 || !hospital.IsOpen)
        {
            return null;
        }

        // The hospital's first patient takes the shortest way to a desk (Hospital:hadPatients).
        var spawn = hospital.Visitors == 0 && ClosestSpawnToDesk(simulation, desks) is { } closest
            ? closest
            : _spawnPoints[simulation.Random.Next(_spawnPoints.Count)];
        var look = disease?.Look ?? PatientLook.Standard;

        // Humanoid:Humanoid: patients are not fully well when they come, from 0.8 to 1.
        var health = simulation.Random.NextInclusive(80, 100) / 100m;
        var patient = simulation.AddEntity(id => Humanoid.CreatePatient(id, spawn, simulation.Random, look, player));
        var behaviour = new PatientBehaviour { Health = health };
        if (disease is not null)
        {
            behaviour.SetDisease(disease, PatientCare.ChooseInsuranceCompany(simulation.Random), simulation.Random);
        }

        patient.Behaviour = behaviour;
        hospital.Visitors++;
        hospital.VisitorsThisYear++;
        Disasters.Epidemics.DetermineIfContagious(simulation, hospital, patient, behaviour);
        return patient;
    }

    private TilePosition? ClosestSpawnToDesk(Simulation simulation, List<RoomObject> desks)
    {
        TilePosition? best = null;
        var bestDistance = 0;
        foreach (var spawn in SpawnPoints)
        {
            foreach (var desk in desks)
            {
                if (simulation.Pathfinder.FindDistance(simulation.Map, desk.Tile, spawn) is { } distance && (best is null || distance < bestDistance))
                {
                    best = spawn;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    /// <summary><c>World:onEndMonth</c>: the spawn rate changes by the level's setting for the month that ends.</summary>
    internal void OnEndMonth(Simulation simulation)
    {
        var month = simulation.Date.MonthOfGame;
        foreach (var entry in simulation.Rules.Population)
        {
            if (entry.Month == month - 1)
            {
                MonthlyIncrease = entry.Change;
                break;
            }
        }

        SpawnRate += MonthlyIncrease;
        PrepareSpawnDates(simulation, firstMonth: false);
    }

    /// <summary>Cheats:roujinOff: the month's patients still to come are called off.</summary>
    internal void ClearSpawnDates() => Array.Clear(_spawnDates);

    /// <summary><c>World:_prepareSpawnDates</c>: spreads the month's patients over its days.</summary>
    private void PrepareSpawnDates(Simulation simulation, bool firstMonth)
    {
        Array.Clear(_spawnDates);
        var count = SpawnRate;

        // Roujin's challenge: the first (local) hospital's cheat brings 40 more patients.
        if (simulation.Hospitals.Count > 0 && (simulation.Hospitals[0].ActiveCheats & ToggleCheats.SpawnRate) != 0)
        {
            count += RoujinBonus;
        }
        if (count <= 0)
        {
            return;
        }

        var month = simulation.Date.Month;
        if (!firstMonth)
        {
            month = (month % 12) + 1;
        }

        var lastDay = GameDate.DaysIn(month);
        var interval = (decimal)lastDay / count;
        var forceArrival = true;
        var targetDay = 1;
        while (targetDay <= lastDay)
        {
            targetDay += PoissonRandom(simulation.Random, interval);
            if (targetDay <= lastDay)
            {
                _spawnDates[targetDay]++;
                forceArrival = false;
            }
        }

        // Give the poor hospital a patient this month anyway.
        if (forceArrival)
        {
            _spawnDates[simulation.Random.NextInclusive(1, lastDay)] = 1;
        }

        PrepareSpawnHours(simulation);
    }

    /// <summary><c>World:_prepareSpawnHours</c>: gives the next day's patients random hours.</summary>
    private void PrepareSpawnHours(Simulation simulation)
    {
        Array.Clear(_spawnHours);
        var date = simulation.Date;
        var nextDay = date.IsLastDayOfMonth ? 1 : date.Day + 1;
        for (var i = 0; i < _spawnDates[nextDay]; i++)
        {
            _spawnHours[simulation.Random.NextInclusive(1, GameDate.HoursPerDay - 1)]++;
        }
    }

    /// <summary>
    /// A Poisson-distributed number with the given mean, by Knuth's method as <c>math.p_random</c>:
    /// multiply uniform numbers in (0, 1] until the product drops below e^−mean. Fixed point decimals keep
    /// it deterministic.
    /// </summary>
    public static int PoissonRandom(SimRandom random, decimal mean)
    {
        var limit = 1m / Exp(mean);
        var k = 0;
        var p = 1m;
        do
        {
            k++;
            p *= (random.Next(int.MaxValue) + 1m) / int.MaxValue;
        }
        while (p > limit);

        return k - 1;
    }

    /// <summary>e^x for 0 ≤ x ≤ 60 by its Taylor series.</summary>
    internal static decimal Exp(decimal x)
    {
        var sum = 1m;
        var term = 1m;
        for (var n = 1; n < 200; n++)
        {
            term = term * x / n;
            if (term < 0.0000000000000000001m)
            {
                break;
            }

            sum += term;
        }

        return sum;
    }

    /// <summary>
    /// <c>World:calculateSpawnTiles</c>: on each edge, the tiles from which the hospital can be reached, of which
    /// at most 8 spread evenly along the edge.
    /// </summary>
    internal static List<TilePosition> CalculateSpawnTiles(LevelMap map)
    {
        var reachable = ReachableFromHospital(map);
        var w = map.Width;
        var h = map.Height;
        var points = new List<TilePosition>();
        var edges = new (int X, int Y, int Dx, int Dy)[] { (0, 0, 1, 0), (w - 1, 0, 0, 1), (w - 1, h - 1, -1, 0), (0, h - 1, 0, -1) };
        foreach (var (startX, startY, dx, dy) in edges)
        {
            var candidates = new List<TilePosition>();
            for (int x = startX, y = startY; map.Contains(x, y); x += dx, y += dy)
            {
                if (reachable[(y * w) + x])
                {
                    candidates.Add(new TilePosition(x, y));
                }
            }

            var count = Math.Min(8, candidates.Count);
            for (var i = 1; i <= count; i++)
            {
                // math.floor((i - 0.5) / num * #xs + 1), 1-based.
                points.Add(candidates[((2 * i) - 1) * candidates.Count / (2 * count)]);
            }
        }

        return points;
    }

    /// <summary>Tiles from which a walk leads into the hospital: a flood fill backwards from its tiles.</summary>
    private static bool[] ReachableFromHospital(LevelMap map)
    {
        var w = map.Width;
        var reachable = new bool[w * map.Height];
        var queue = new Queue<TilePosition>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (map[x, y].Has(TileFlags.Hospital | TileFlags.Passable))
                {
                    reachable[(y * w) + x] = true;
                    queue.Enqueue(new TilePosition(x, y));
                }
            }
        }

        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            foreach (var (dx, dy, back) in new[] { (0, -1, TileFlags.CanTravelSouth), (1, 0, TileFlags.CanTravelWest), (0, 1, TileFlags.CanTravelNorth), (-1, 0, TileFlags.CanTravelEast) })
            {
                var x = tile.X + dx;
                var y = tile.Y + dy;
                if (map.Contains(x, y) && !reachable[(y * w) + x] && map[x, y].Has(TileFlags.Passable | back))
                {
                    reachable[(y * w) + x] = true;
                    queue.Enqueue(new TilePosition(x, y));
                }
            }
        }

        return reachable;
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(SpawnRate);
        writer.WriteInt32(MonthlyIncrease);
        foreach (var count in _spawnDates)
        {
            writer.WriteInt32(count);
        }

        foreach (var count in _spawnHours)
        {
            writer.WriteInt32(count);
        }
    }

    internal static PatientArrivals Read(SimReader reader)
    {
        var arrivals = new PatientArrivals
        {
            SpawnRate = reader.ReadInt32(),
            MonthlyIncrease = reader.ReadInt32(),
        };
        for (var i = 0; i < arrivals._spawnDates.Length; i++)
        {
            arrivals._spawnDates[i] = reader.ReadInt32();
        }

        for (var i = 0; i < arrivals._spawnHours.Length; i++)
        {
            arrivals._spawnHours[i] = reader.ReadInt32();
        }

        return arrivals;
    }
}

/// <summary>One entry of a level's <c>popn</c> setting: from the end of <paramref name="Month"/> on, the spawn rate changes by <paramref name="Change"/> each month.</summary>
public readonly record struct PopulationChange(int Month, int Change);
