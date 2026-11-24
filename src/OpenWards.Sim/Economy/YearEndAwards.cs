using OpenWards.Sim.Entities;
using OpenWards.Sim.People;

namespace OpenWards.Sim.Economy;

/// <summary>
/// The year's 9 trophies and 13 awards or penalties, as the logic specification describes them as meant (the level
/// files' <c>awards_trophies</c> settings, <see cref="AwardRules"/>). Trophies are judged when more than 5 patients came
/// in the year, awards when more than 3 × the level's number did. Awards that compare with the other hospitals give
/// twice the bonus to the best of them and three times to one ahead of all the others together, and twice the penalty
/// to the worst. All is judged before any of the year's counts start afresh.
/// </summary>
/// <remarks>
/// Departs from CorsixTH, which gives 6 trophies and 7 awards with its own conditions and a random part, and from the
/// original game, which by mistakes of order, index and scale gave fewer than half of them. Not kept from CorsixTH:
/// the trophy and award for curing everybody (<c>TrophyAllCuredBonus</c>, <c>AllCuresBonus</c>).
/// </remarks>
public static class YearEndAwards
{
    /// <summary>What a reputation trophy brings instead when the reputation is at its limit and does not move.</summary>
    public const int TrophyMoneyAtLimit = 5000;

    /// <summary>What a reputation award (or penalty, negative) brings instead in that case.</summary>
    public const int AwardMoneyAtLimit = 2500;

    /// <summary>Trophies are judged when more patients than this came in the year.</summary>
    private const int TrophyVisitors = 5;

    /// <summary>Awards are judged when more patients than this times the level's number came.</summary>
    private const int AwardVisitorsPerLevel = 3;

    /// <summary>The emergencies a year needs for their award.</summary>
    private const int MinEmergencies = 2;

    /// <summary>Whether a prize is a trophy rather than an award or penalty.</summary>
    public static bool IsTrophy(YearEndAwardKind kind) => kind is YearEndAwardKind.ConsistentReputation or YearEndAwardKind.AllCuredTrophy
        or YearEndAwardKind.NoDeathsTrophy or YearEndAwardKind.ManyCuredTrophy or YearEndAwardKind.HappyVips or YearEndAwardKind.DrinksSoldTrophy
        or YearEndAwardKind.HappyStaffTrophy or YearEndAwardKind.RatsKilledTrophy or YearEndAwardKind.RatAccuracyTrophy or YearEndAwardKind.HealthyPlantsTrophy;

    /// <summary>
    /// The end of a month, after every hospital's bills: each hospital's share of the town's new patients this month,
    /// the computer opponents' too, and what the month showed for the awards.
    /// </summary>
    internal static void OnEndMonth(Simulation simulation)
    {
        var visitors = new int[simulation.Hospitals.Count];
        long total = 0;
        foreach (var hospital in simulation.Hospitals)
        {
            var months = hospital.Statistics;
            visitors[hospital.Player] = months.Count >= 2 ? Math.Max(0, months[^1].Visitors - months[^2].Visitors) : 0;
            total += visitors[hospital.Player];
        }

        foreach (var competitor in simulation.Competitors)
        {
            total += competitor.VisitorsThisMonth;
        }

        foreach (var hospital in simulation.Hospitals)
        {
            hospital.PopulationPercent = total == 0 ? 0 : (int)(visitors[hospital.Player] * 100L / total);
            hospital.RecordAwardMonth(MeasureMonth(simulation, hospital));
        }

        foreach (var competitor in simulation.Competitors)
        {
            competitor.PopulationPercent = total == 0 ? 0 : (int)(competitor.VisitorsThisMonth * 100L / total);
        }
    }

    /// <summary>What the month that ends shows of the hospital.</summary>
    internal static AwardMonth MeasureMonth(Simulation simulation, Hospital hospital)
    {
        var map = simulation.Map;
        var leaveMax = simulation.Rules.Needs.LeaveMax;
        long staffHappiness = 0, patientHappiness = 0, waiting = 0;
        int staff = 0, patients = 0, plants = 0, watered = 0;
        foreach (var entity in simulation.Entities)
        {
            switch (entity)
            {
                case Humanoid { Profile: not null, IsFired: false } member when member.Employer == hospital.Player:
                    staffHappiness += (int)(member.Happiness * 100);
                    staff++;
                    break;
                case Humanoid { Behaviour: PatientBehaviour patient, IsDespawning: false } humanoid when humanoid.HospitalPlayer == hospital.Player:
                    patientHappiness += (int)(humanoid.Happiness * 100);
                    waiting += leaveMax > 0 ? Math.Max(0, patient.LeaveCount) * 100L / leaveMax : 0;
                    patients++;
                    break;
                case RoomObject { Plant: { } plant, IsDestroyed: false } placed
                    when map.Contains(placed.Tile.X, placed.Tile.Y) && map.GetParcelOwner(map[placed.Tile.X, placed.Tile.Y].Parcel) == hospital.Player + 1:
                    plants++;
                    if (!plant.NeedsWatering)
                    {
                        watered++;
                    }

                    break;
            }
        }

        return new AwardMonth(
            hospital.Reputation,
            staff > 0 ? (int)(staffHappiness / staff) : -1,
            patients > 0 ? (int)(patientHappiness / patients) : -1,
            plants > 0 ? watered * 100 / plants : -1,
            patients > 0 && leaveMax > 0 ? (int)(waiting / patients) : -1);
    }

    /// <summary>The year end: every hospital's trophies and awards, judged before any is paid.</summary>
    internal static void Judge(Simulation simulation)
    {
        var awards = simulation.Hospitals.Select(hospital => Judge(simulation, hospital)).ToList();
        foreach (var hospital in simulation.Hospitals)
        {
            hospital.SetYearAwards(awards[hospital.Player]);
        }
    }

    /// <summary>The hospital's trophies and awards for the year that ends.</summary>
    internal static List<YearEndAward> Judge(Simulation simulation, Hospital hospital)
    {
        var rules = simulation.Rules.Awards;
        var level = Math.Max(1, simulation.Rules.LevelNumber ?? 1);
        var prizes = new List<YearEndAward>();
        if (hospital.VisitorsThisYear > TrophyVisitors)
        {
            Trophies(hospital, rules, prizes);
        }

        if (hospital.VisitorsThisYear > AwardVisitorsPerLevel * level)
        {
            Awards(simulation, hospital, rules, level, prizes);
        }

        return prizes;
    }

    private static void Trophies(Hospital hospital, AwardRules rules, List<YearEndAward> prizes)
    {
        var months = hospital.AwardMonths;

        // Cures: over nine in ten of the year's patients who came to an end were cured.
        var ended = hospital.CuredThisYear + hospital.DeathsThisYear + hospital.NotCuredThisYear;
        if (ended > 0 && hospital.CuredThisYear * 10L > ended * 9L)
        {
            prizes.Add(new(YearEndAwardKind.ManyCuredTrophy, rules[AwardRules.TrophyCuresBonus], 0));
        }

        // Reputation: at the end of each month of the year at least Reputation.
        if (months.Count > 0 && months.All(month => month.Reputation >= rules[AwardRules.Reputation]))
        {
            prizes.Add(new(YearEndAwardKind.ConsistentReputation, rules[AwardRules.TrophyReputationBonus], 0));
        }

        // Staff happiness: each month's mean at least TrophyStaffHappiness %.
        if (months.Count > 0 && months.All(month => month.StaffHappiness >= rules[AwardRules.TrophyStaffHappiness]))
        {
            prizes.Add(new(YearEndAwardKind.HappyStaffTrophy, 0, rules[AwardRules.TrophyStaffHappinessBonus]));
        }

        // The mayor: two VIPs or more, scoring below TrophyMayor on average (a low score is a good one).
        if (hospital.Vips.ThisYear >= 2 && hospital.Vips.ScoreSumThisYear < (long)rules[AwardRules.TrophyMayor] * hospital.Vips.ThisYear)
        {
            prizes.Add(new(YearEndAwardKind.HappyVips, 0, rules[AwardRules.TrophyMayorBonus]));
        }

        // Nobody died.
        if (hospital.DeathsThisYear == 0)
        {
            prizes.Add(new(YearEndAwardKind.NoDeathsTrophy, rules[AwardRules.TrophyDeathBonus], 0));
        }

        // Rats: more shot than RatKillsAbsolute; a good aim with at least half as many.
        var killed = hospital.RatsKilledThisYear;
        if (killed > rules[AwardRules.RatKillsAbsolute])
        {
            prizes.Add(new(YearEndAwardKind.RatsKilledTrophy, 0, rules[AwardRules.RatKillsAbsoluteBonus], killed));
        }

        var shots = hospital.RatShotsThisYear;
        if (shots > 0 && killed > rules[AwardRules.RatKillsAbsolute] / 2 && killed * 100L > (long)rules[AwardRules.RatKillsPercentage] * shots)
        {
            prizes.Add(new(YearEndAwardKind.RatAccuracyTrophy, rules[AwardRules.RatKillsPercentageBonus], 0, (int)(killed * 100L / shots)));
        }

        // Plants: each month at least Plant % of them watered.
        if (months.Count > 0 && months.All(month => month.PlantsWatered >= rules[AwardRules.Plant]))
        {
            prizes.Add(new(YearEndAwardKind.HealthyPlantsTrophy, 0, rules[AwardRules.PlantBonus]));
        }

        // Drinks sold.
        if (hospital.SodasSold > rules[AwardRules.CansofCoke])
        {
            prizes.Add(new(YearEndAwardKind.DrinksSoldTrophy, rules[AwardRules.CansofCokeBonus], 0));
        }
    }

    private static void Awards(Simulation simulation, Hospital hospital, AwardRules rules, int level, List<YearEndAward> prizes)
    {
        var others = simulation.Hospitals.Where(other => other.Player != hospital.Player).ToList();
        var competitors = simulation.Competitors;

        // Reputation, against the others; the bonus needs 2 × the level's number of cures in the year.
        var reputations = others.Select(other => (long)other.Reputation).Concat(competitors.Select(other => (long)other.Reputation)).ToList();
        Compared(prizes, hospital.Reputation, reputations, higherIsBetter: true,
            hospital.Reputation > rules[AwardRules.ReputationAward] && hospital.CuredThisYear >= 2 * level, hospital.Reputation < rules[AwardRules.ReputationPoor],
            YearEndAwardKind.HighReputation, YearEndAwardKind.PoorReputation, rules[AwardRules.AwardReputationBonus], rules[AwardRules.AwardReputationPenalty], inReputation: false);

        // The year's cures, against the others.
        var cures = others.Select(other => (long)other.CuredThisYear).Concat(competitors.Select(other => (long)other.CuredThisYear)).ToList();
        Compared(prizes, hospital.CuredThisYear, cures, higherIsBetter: true,
            hospital.CuredThisYear > rules[AwardRules.CuresAward], hospital.CuredThisYear < rules[AwardRules.CuresPoor],
            YearEndAwardKind.ManyCures, YearEndAwardKind.FewCures, rules[AwardRules.CuresBonus], rules[AwardRules.CuresPenalty], inReputation: false);

        // The year's deaths, against the others: fewer is better.
        var deaths = others.Select(other => (long)other.DeathsThisYear).Concat(competitors.Select(other => (long)other.DeathsThisYear)).ToList();
        Compared(prizes, hospital.DeathsThisYear, deaths, higherIsBetter: false,
            hospital.DeathsThisYear < rules[AwardRules.DeathsAward], hospital.DeathsThisYear > rules[AwardRules.DeathsPoor],
            YearEndAwardKind.FewDeaths, YearEndAwardKind.ManyDeaths, rules[AwardRules.DeathsBonus], rules[AwardRules.DeathsPenalty], inReputation: false);

        // The share of the town's patients last month, against the others.
        var shares = others.Select(other => (long)other.PopulationPercent).Concat(competitors.Select(other => (long)other.PopulationPercent)).ToList();
        Compared(prizes, hospital.PopulationPercent, shares, higherIsBetter: true,
            hospital.PopulationPercent > rules[AwardRules.PopulationPercentageAward], hospital.PopulationPercent < rules[AwardRules.PopulationPercentagePoor],
            YearEndAwardKind.HighPopulationShare, YearEndAwardKind.LowPopulationShare, rules[AwardRules.PopulationPercentageBonus], rules[AwardRules.PopulationPercentagePenalty], inReputation: true);

        // Cures against deaths since the level began; with no deaths, any cure is enough for the bonus.
        if (hospital.Cured > (long)rules[AwardRules.CuresVDeathsAward] * hospital.Deaths)
        {
            prizes.Add(new(YearEndAwardKind.CuresOverDeaths, rules[AwardRules.CuresVDeathsBonus], 0));
        }
        else if (hospital.Cured < (long)rules[AwardRules.CuresVDeathsPoor] * hospital.Deaths)
        {
            prizes.Add(new(YearEndAwardKind.DeathsOverCures, rules[AwardRules.CuresVDeathsPenalty], 0));
        }

        // The patients' and the staff's happiness over the year.
        var months = hospital.AwardMonths;
        Plain(prizes, Mean(months.Select(month => month.PatientHappiness)), higherIsBetter: true, rules[AwardRules.PeepHappinessAward], rules[AwardRules.PeepHappinessPoor],
            YearEndAwardKind.HappyPatients, YearEndAwardKind.UnhappyPatients, rules[AwardRules.PeepHappinessBonus], rules[AwardRules.PeepHappinessPenalty]);

        // The hospital's value, against the others.
        var values = others.Select(other => other.Value).Concat(competitors.Select(other => other.Value)).ToList();
        Compared(prizes, hospital.Value, values, higherIsBetter: true,
            hospital.Value > rules[AwardRules.HospValueAward], hospital.Value < rules[AwardRules.HospValuePoor],
            YearEndAwardKind.HighValue, YearEndAwardKind.LowValue, rules[AwardRules.HospValueBonus], rules[AwardRules.HospValuePenalty], inReputation: true);

        Plain(prizes, Mean(months.Select(month => month.StaffHappiness)), higherIsBetter: true, rules[AwardRules.StaffHappinessAward], rules[AwardRules.StaffHappinessPoor],
            YearEndAwardKind.HappyStaff, YearEndAwardKind.UnhappyStaff, rules[AwardRules.AwardStaffHappinessBonus], rules[AwardRules.AwardStaffHappinessPenalty]);

        // Cleanliness: littered tiles and twice the rat holes, in percent of the hospital's land.
        var area = LandTiles(simulation, hospital);
        if (area > 0)
        {
            var dirt = (Disasters.Rats.LitteredTiles(simulation, hospital.Player) + (2L * hospital.Ratholes.Count)) * 100 / area;
            Plain(prizes, (int)dirt, higherIsBetter: false, rules[AwardRules.CleanlinessAward], rules[AwardRules.CleanlinessPoor],
                YearEndAwardKind.Clean, YearEndAwardKind.Dirty, rules[AwardRules.CleanlinessBonus], rules[AwardRules.CleanlinessPenalty]);
        }

        // Waiting: the patients' mean leave count in percent of what they put up with.
        Plain(prizes, Mean(months.Select(month => month.Waiting)), higherIsBetter: false, rules[AwardRules.WaitingTimesAward], rules[AwardRules.WaitingTimesPoor],
            YearEndAwardKind.ShortWaits, YearEndAwardKind.LongWaits, rules[AwardRules.WaitingTimesBonus], rules[AwardRules.WaitingTimesPenalty]);

        // Emergencies: the patients saved, in percent, with two emergencies or more in the year.
        if (hospital.EmergenciesThisYear >= MinEmergencies && hospital.EmergencyVictimsThisYear > 0)
        {
            Plain(prizes, (int)(hospital.EmergencySavedThisYear * 100L / hospital.EmergencyVictimsThisYear), higherIsBetter: true, rules[AwardRules.EmergencyAward], rules[AwardRules.EmergencyPoor],
                YearEndAwardKind.EmergenciesSaved, YearEndAwardKind.EmergenciesLost, rules[AwardRules.EmergencyBonus], rules[AwardRules.EmergencyPenalty]);
        }

        // Research: the year's points of research.
        Plain(prizes, hospital.ResearchPointsThisYear, higherIsBetter: true, rules[AwardRules.NewTechAward], rules[AwardRules.NewTechPoor],
            YearEndAwardKind.GoodResearch, YearEndAwardKind.PoorResearch, rules[AwardRules.ResearchBonus], rules[AwardRules.ResearchPenalty]);

        // The machines' mean wear.
        if (MeanWear(simulation, hospital) is { } wear)
        {
            Plain(prizes, wear, higherIsBetter: false, rules[AwardRules.WellKeptTechAward], rules[AwardRules.WellKeptTechPoor],
                YearEndAwardKind.WellKeptMachines, YearEndAwardKind.NeglectedMachines, rules[AwardRules.WellKeptTechBonus], rules[AwardRules.WellKeptTechPenalty]);
        }
    }

    /// <summary>
    /// An award or penalty that compares with the other hospitals: the bonus doubled for the best of them all and tripled
    /// for one ahead of all the others together (where more is better), the penalty doubled for the worst.
    /// </summary>
    private static void Compared(
        List<YearEndAward> prizes,
        long value,
        IReadOnlyList<long> others,
        bool higherIsBetter,
        bool bonus,
        bool penalty,
        YearEndAwardKind bonusKind,
        YearEndAwardKind penaltyKind,
        int bonusAmount,
        int penaltyAmount,
        bool inReputation)
    {
        int factor;
        YearEndAwardKind kind;
        int amount;
        if (bonus)
        {
            var best = others.All(other => higherIsBetter ? value > other : value < other);
            factor = others.Count == 0 || !best ? 1 : higherIsBetter && value > others.Sum() ? 3 : 2;
            (kind, amount) = (bonusKind, bonusAmount);
        }
        else if (penalty)
        {
            var worst = others.Count > 0 && others.All(other => higherIsBetter ? value < other : value > other);
            factor = worst ? 2 : 1;
            (kind, amount) = (penaltyKind, penaltyAmount);
        }
        else
        {
            return;
        }

        prizes.Add(inReputation ? new(kind, 0, amount * factor) : new(kind, amount * factor, 0));
    }

    /// <summary>
    /// An award or penalty in reputation for a measure above (or, where less is better, below) its award's threshold or
    /// past its penalty's; nothing without a measure.
    /// </summary>
    private static void Plain(List<YearEndAward> prizes, int? value, bool higherIsBetter, int award, int poor, YearEndAwardKind bonusKind, YearEndAwardKind penaltyKind, int bonus, int penalty)
    {
        if (value is not { } measure)
        {
            return;
        }

        if (higherIsBetter ? measure > award : measure < award)
        {
            prizes.Add(new(bonusKind, 0, bonus));
        }
        else if (higherIsBetter ? measure < poor : measure > poor)
        {
            prizes.Add(new(penaltyKind, 0, penalty));
        }
    }

    /// <summary>The mean of the months that measured something (−1 is nothing), or null if none did.</summary>
    private static int? Mean(IEnumerable<int> months)
    {
        long sum = 0;
        var count = 0;
        foreach (var month in months)
        {
            if (month >= 0)
            {
                sum += month;
                count++;
            }
        }

        return count == 0 ? null : (int)(sum / count);
    }

    /// <summary>The tiles of the hospital's land.</summary>
    private static long LandTiles(Simulation simulation, Hospital hospital)
    {
        var map = simulation.Map;
        long tiles = 0;
        for (var parcel = 1; parcel < map.ParcelCount; parcel++)
        {
            if (map.GetParcelOwner(parcel) == hospital.Player + 1)
            {
                tiles += map.ParcelTileCount(parcel);
            }
        }

        return tiles;
    }

    /// <summary>The mean wear of the hospital's machines in percent, or null if it has none.</summary>
    private static int? MeanWear(Simulation simulation, Hospital hospital)
    {
        var map = simulation.Map;
        long sum = 0;
        var count = 0;
        foreach (var entity in simulation.Entities)
        {
            if (entity is RoomObject { Machine: { } machine, IsDestroyed: false } placed
                && map.Contains(placed.Tile.X, placed.Tile.Y) && map.GetParcelOwner(map[placed.Tile.X, placed.Tile.Y].Parcel) == hospital.Player + 1)
            {
                sum += machine.WearPercent;
                count++;
            }
        }

        return count == 0 ? null : (int)(sum / count);
    }
}
