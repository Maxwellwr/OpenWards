// Emergencies, started from CorsixTH CorsixTH/Lua/world.lua (World:onEndDay's emergency check, nextEmergency,
// computeNextEmergencyDates, wasEmergencySkipped), hospital.lua (createEmergency, resolveEmergency, checkEmergencyOver,
// getHeliportPosition, getHeliportSpawnPosition, reputation_changes), objects/helicopter.lua, dialogs/watch.lua
// (UIWatch for "emergency"), hospitals/player_hospital.lua (makeEmergencyStartFax's timeout) and
// dialogs/fullscreen/fax.lua (accept_emergency, refuse_emergency). When an emergency waits, the watch, the helicopter's
// timing and the end of random emergencies follow the community's description of the original game (the logic
// specification), not CorsixTH.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Calendar;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Disasters;

/// <summary>
/// One of the level's planned emergencies (<c>emergency_control[n]</c>): it comes on a random day from
/// <see cref="StartMonth"/> to <see cref="EndMonth"/> (months of the game), with <see cref="Min"/> to <see cref="Max"/>
/// patients of the disease numbered <see cref="Illness"/>, and pays <see cref="Bonus"/> for each one cured if at least
/// <see cref="PercWin"/> percent are.
/// </summary>
public readonly record struct PlannedEmergency(int Index, int StartMonth, int EndMonth, int Min, int Max, int Illness, int PercWin, int Bonus)
{
    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Index);
        writer.WriteInt32(StartMonth);
        writer.WriteInt32(EndMonth);
        writer.WriteInt32(Min);
        writer.WriteInt32(Max);
        writer.WriteInt32(Illness);
        writer.WriteInt32(PercWin);
        writer.WriteInt32(Bonus);
    }

    internal static PlannedEmergency Read(SimReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}

/// <summary>
/// A level's random emergencies as CorsixTH reads them (<c>emergency_control[0].Random</c> or <c>Mean</c> set): kept in
/// the rules but not used, since the original game has no random emergencies, only the level's schedule.
/// </summary>
/// <param name="Mean">Days on average between emergencies (<c>Mean</c>, by default 180).</param>
/// <param name="Variance">Their standard deviation in days (<c>Variance</c>, by default 30).</param>
public readonly record struct RandomEmergencies(int Mean, int Variance)
{
    /// <summary><c>World:scheduleRandomEmergency</c>'s mean when the level gives none.</summary>
    public const int DefaultMean = 180;

    /// <summary>Likewise for the variance.</summary>
    public const int DefaultVariance = 30;
}

/// <summary>An emergency offered by fax, or under way once accepted (CorsixTH's <c>hospital.emergency</c>).</summary>
public sealed class Emergency
{
    internal Emergency(string diseaseId, int victims, int bonus, int percentage)
    {
        DiseaseId = diseaseId;
        Victims = victims;
        Bonus = bonus;
        Percentage = percentage;
    }

    /// <summary>The patients' disease.</summary>
    public string DiseaseId { get; }

    /// <summary>How many patients come.</summary>
    public int Victims { get; }

    /// <summary>What each cured patient brings, if enough are cured.</summary>
    public int Bonus { get; }

    /// <summary>The percentage of patients that must be cured for the bonus.</summary>
    public int Percentage { get; }

    /// <summary>Patients cured.</summary>
    public int Cured { get; internal set; }

    /// <summary>Patients who died or went home uncured.</summary>
    public int Killed { get; internal set; }

    /// <summary>Whether the fax was accepted and the emergency is under way.</summary>
    public bool IsAccepted { get; internal set; }

    /// <summary>The tick at which the fax refuses the emergency by itself.</summary>
    public long OfferExpires { get; internal set; }

    /// <summary>The watch's segments left (CorsixTH's <c>open_timer</c>); at −1 time is up.</summary>
    public int WatchTimer { get; internal set; }

    /// <summary>Hours left of the watch's current segment (<c>tick_timer</c>).</summary>
    public int WatchTickTimer { get; internal set; }

    /// <summary>The patients who came, in order.</summary>
    public List<int> PatientIds { get; } = [];

    internal void Write(SimWriter writer)
    {
        writer.WriteString(DiseaseId);
        writer.WriteInt32(Victims);
        writer.WriteInt32(Bonus);
        writer.WriteInt32(Percentage);
        writer.WriteInt32(Cured);
        writer.WriteInt32(Killed);
        writer.WriteBool(IsAccepted);
        writer.WriteInt64(OfferExpires);
        writer.WriteInt32(WatchTimer);
        writer.WriteInt32(WatchTickTimer);
        writer.WriteInt32(PatientIds.Count);
        foreach (var id in PatientIds)
        {
            writer.WriteInt32(id);
        }
    }

    internal static Emergency Read(SimReader reader)
    {
        var emergency = new Emergency(reader.ReadString(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32())
        {
            Cured = reader.ReadInt32(),
            Killed = reader.ReadInt32(),
            IsAccepted = reader.ReadBool(),
            OfferExpires = reader.ReadInt64(),
            WatchTimer = reader.ReadInt32(),
            WatchTickTimer = reader.ReadInt32(),
        };
        if (DiseaseCatalog.Find(emergency.DiseaseId) is null)
        {
            throw new InvalidDataException($"Unknown emergency disease '{emergency.DiseaseId}'.");
        }

        var count = reader.ReadCount(1 << 10);
        for (var i = 0; i < count; i++)
        {
            emergency.PatientIds.Add(reader.ReadInt32());
        }

        return emergency;
    }
}

/// <summary>
/// A hospital's emergencies: when the next of the level's planned ones comes, the emergency offered or under way, and
/// the helicopter bringing its patients.
/// </summary>
/// <remarks>
/// CorsixTH keeps the schedule in the world and has emergencies at the local player's hospital only; here each
/// hospital has its own.
/// </remarks>
public sealed class HospitalEmergencies
{
    /// <summary>Days the watch runs: 50 (100 half-days). CorsixTH's <c>TICK_DAYS_EMERGENCY</c> is 52.</summary>
    public const int WatchDays = 50;

    /// <summary>The watch's segments (<c>TIMER_SEGMENTS</c>).</summary>
    public const int WatchSegments = 13;

    /// <summary>The fax refuses the emergency by itself after this many days.</summary>
    public const int OfferDays = 16;

    /// <summary>Hours of one segment of the watch: 192.</summary>
    public const int WatchTickRate = WatchDays * GameDate.HoursPerDay / WatchSegments;

    /// <summary>The helicopter's phase when it is called: it sets off 5 days (250 hours) later. CorsixTH waits 120 hours.</summary>
    public const int HelicopterStart = -5 * GameDate.HoursPerDay;

    /// <summary>The helicopter's phase when it has landed and the adviser says the patients have arrived.</summary>
    public const int HelicopterLanded = 60;

    /// <summary>The number of the next planned emergency to look at (<c>next_emergency_no</c>).</summary>
    public int NextNumber { get; internal set; }

    /// <summary>The month of the game of the next emergency, or 0 for none (<c>next_emergency_month</c>).</summary>
    public int NextMonth { get; internal set; }

    /// <summary>The day of the month of the next emergency (<c>next_emergency_day</c>).</summary>
    public int NextDay { get; internal set; }

    /// <summary>The number of the planned emergency that comes next (<c>next_emergency</c>), or −1.</summary>
    public int NextIndex { get; internal set; } = -1;

    /// <summary>The emergency offered or under way, or null.</summary>
    public Emergency? Current { get; internal set; }

    /// <summary>
    /// The helicopter's phase: from −250 while on the way, 0 when it shows, landed from 60, gone after 147
    /// (CorsixTH's <c>Helicopter.phase</c>), or null when there is none.
    /// </summary>
    public int? HelicopterPhase { get; internal set; }

    /// <summary>Patients the helicopter has let out so far.</summary>
    public int HelicopterSpawned { get; internal set; }

    /// <summary>Patients the helicopter brings.</summary>
    public int HelicopterVictims { get; internal set; }

    /// <summary>The emergency's disease, for the helicopter's patients.</summary>
    public string HelicopterDisease { get; internal set; } = string.Empty;

    /// <summary>
    /// The last emergency's result (<c>PlayerHospital:makeEmergencyEndFax</c>), kept for the fax until it expires 25
    /// days after the emergency ended, or null.
    /// </summary>
    public EmergencyReport? Report { get; internal set; }

    /// <summary>Days the result's fax stays (<c>queueMessage</c>'s timeout).</summary>
    public const int ReportDays = 25;

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(NextNumber);
        writer.WriteInt32(NextMonth);
        writer.WriteInt32(NextDay);
        writer.WriteInt32(NextIndex);
        writer.WriteBool(Current is not null);
        Current?.Write(writer);
        writer.WriteBool(HelicopterPhase is not null);
        writer.WriteInt32(HelicopterPhase ?? 0);
        writer.WriteInt32(HelicopterSpawned);
        writer.WriteInt32(HelicopterVictims);
        writer.WriteString(HelicopterDisease);
        writer.WriteBool(Report is not null);
        if (Report is { } report)
        {
            writer.WriteInt32(report.Rescued);
            writer.WriteInt32(report.Total);
            writer.WriteInt32(report.MaxBonus);
            writer.WriteInt32(report.Earned);
            writer.WriteInt64(report.Expires);
        }
    }

    internal static HospitalEmergencies Read(SimReader reader)
    {
        var emergencies = new HospitalEmergencies
        {
            NextNumber = reader.ReadInt32(),
            NextMonth = reader.ReadInt32(),
            NextDay = reader.ReadInt32(),
            NextIndex = reader.ReadInt32(),
            Current = reader.ReadBool() ? Emergency.Read(reader) : null,
        };
        var hasHelicopter = reader.ReadBool();
        var phase = reader.ReadInt32();
        emergencies.HelicopterPhase = hasHelicopter ? phase : null;
        emergencies.HelicopterSpawned = reader.ReadInt32();
        emergencies.HelicopterVictims = reader.ReadInt32();
        emergencies.HelicopterDisease = reader.ReadString();
        if ((reader.Version is 0 or >= 80) && reader.ReadBool())
        {
            emergencies.Report = new EmergencyReport(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64());
        }

        return emergencies;
    }
}

/// <summary>
/// Emergencies by the level's schedule only: from the planned day, once nothing else is on, the hospital is offered one
/// by fax; accepted, a helicopter sets off 5 days later, lands at the heliport and lets out a patient every 25 hours,
/// already diagnosed and on their way to treatment, and a watch runs for 50 days. When time is up, or every patient is cured, dead or gone, the patients not yet cured are set to
/// die, and the hospital gains reputation and a bonus for each cured patient if enough were cured, or loses
/// reputation.
/// </summary>
public static class Emergencies
{
    /// <summary>An emergency handled well brings this many percent of the visits in reputation points (about +10).</summary>
    public const int SuccessReputationPercent = 2;

    /// <summary>An emergency handled badly costs this many percent of the visits for each patient not cured (about −20 each).</summary>
    public const int FailureReputationPercent = -4;

    /// <summary>The cheat's emergency's bonus.</summary>
    private const int RandomBonus = 1000;

    /// <summary>A random emergency's percentage to cure.</summary>
    private const int RandomPercentage = 75;

    /// <summary>Plans the hospital's first emergency (<c>World:World</c>).</summary>
    internal static void Start(Simulation simulation, Hospital hospital) => NextEmergency(simulation, hospital);

    /// <summary>
    /// <c>World:nextEmergency</c>: the next of the level's planned emergencies that can still come, stepping over one
    /// missing number; none when they run out.
    /// </summary>
    internal static void NextEmergency(Simulation simulation, Hospital hospital)
    {
        var state = hospital.Emergencies;
        var planned = simulation.Rules.Emergencies;
        while (true)
        {
            var number = state.NextNumber;
            if (Find(planned, number) is null && Find(planned, number + 1) is not null)
            {
                number++;
                state.NextNumber = number;
            }

            if (Find(planned, number) is not { } emergency)
            {
                state.NextMonth = 0;
                state.NextIndex = -1;
                return;
            }

            state.NextIndex = number;
            state.NextNumber++;
            if (ComputeNextDates(simulation, state, emergency))
            {
                return;
            }
        }
    }

    /// <summary>
    /// <c>World:wasEmergencySkipped</c>: when a cheat moved the calendar on from <paramref name="previous"/> past the
    /// start of the planned emergency's day, the next one is planned instead (<c>setEndMonth</c>, <c>setEndYear</c>).
    /// </summary>
    internal static void OnCalendarSkipped(Simulation simulation, Hospital hospital, long previous, long now)
    {
        var state = hospital.Emergencies;
        if (state.NextMonth <= 0)
        {
            return;
        }

        // Date(1, next_emergency_month, next_emergency_day): the first hour of that day.
        var planned = new GameDate(1 + ((state.NextMonth - 1) / 12), 1 + ((state.NextMonth - 1) % 12), state.NextDay, 0).ToHours();
        if (planned < now && previous < planned)
        {
            NextEmergency(simulation, hospital);
        }
    }

    /// <summary>
    /// The end of a day: from the planned day on, the emergency waits while the hospital is not open yet, another fax is
    /// waiting, an epidemic is being covered up, another emergency is under way or its result's fax is still open. Then,
    /// if the player knows the disease and has a heliport and a staffed desk, the fax offers it; otherwise the level's
    /// entry is used up all the same and the next one planned.
    /// </summary>
    internal static void OnEndDay(Simulation simulation, Hospital hospital)
    {
        // The network game: every emergency comes to all the hospitals at once, planned by the first hospital's
        // schedule (the original's NETPLAY.TXT, "Emergencies & VIP Visits").
        if (IsShared(simulation) && hospital.Player != 0)
        {
            return;
        }

        var state = hospital.Emergencies;
        var date = simulation.Date;
        if (state.NextMonth <= 0 || (date.MonthOfGame, date.Day).CompareTo((state.NextMonth, state.NextDay)) < 0)
        {
            return;
        }

        if (IsShared(simulation) ? simulation.Hospitals.All(IsBusy) : IsBusy(hospital))
        {
            return;
        }

        var available = simulation.Rules.Diseases.AvailableDiseases().ToList();
        Emergency? planned = null;
        if (Find(simulation.Rules.Emergencies, state.NextIndex) is { } next
            && available.FirstOrDefault(disease => disease.ExpertiseId == next.Illness) is { } disease)
        {
            planned = new Emergency(disease.Id, simulation.Random.NextInclusive(next.Min, Math.Max(next.Min, next.Max)), next.Bonus, next.PercWin);
        }

        if (planned is null)
        {
            NextEmergency(simulation, hospital);
            return;
        }

        if (IsShared(simulation))
        {
            // Offered to every hospital that can take it, the same emergency; the first to accept receives it.
            var offered = false;
            foreach (var each in simulation.Hospitals)
            {
                if (!IsBusy(each) && Offer(simulation, each, Copy(planned)))
                {
                    offered = true;
                }
            }

            if (!offered)
            {
                NextEmergency(simulation, hospital);
            }

            return;
        }

        if (!Offer(simulation, hospital, planned))
        {
            NextEmergency(simulation, hospital);
        }
    }

    /// <summary>
    /// Whether an emergency due now has to wait: the hospital not open yet or a vacant place of a network game, a fax waiting (a VIP's, an epidemic's or
    /// an emergency's), an epidemic being covered up, an emergency under way, or the last result's fax still open.
    /// </summary>
    internal static bool IsBusy(Hospital hospital) =>
        !hospital.IsOpen || hospital.IsVacant || hospital.Emergencies.Current is not null || hospital.Emergencies.Report is not null
        || hospital.Vips.IsInvitationPending || hospital.Epidemics.IsFaxPending || hospital.Epidemics.Current is { IsCoverUpActive: true };

    /// <summary>Whether emergencies and VIP visits go to all the hospitals at once: a game of several hospitals.</summary>
    internal static bool IsShared(Simulation simulation) => simulation.Hospitals.Count > 1;

    private static Emergency Copy(Emergency emergency) => new(emergency.DiseaseId, emergency.Victims, emergency.Bonus, emergency.Percentage);

    /// <summary>The other hospitals' copies of the same offer, not yet answered.</summary>
    private static IEnumerable<Hospital> OthersOffered(Simulation simulation, int player, Emergency emergency) =>
        simulation.Hospitals.Where(other => other.Player != player
            && other.Emergencies.Current is { IsAccepted: false } offer && offer.DiseaseId == emergency.DiseaseId && offer.OfferExpires == emergency.OfferExpires);

    /// <summary>
    /// The fax offers the emergency, if the hospital has a heliport and a staffed desk and knows the disease. A refused
    /// or unanswered offer frees its place: the original game keeps it, so that after four offers no more emergencies
    /// come on the level.
    /// </summary>
    private static bool Offer(Simulation simulation, Hospital hospital, Emergency emergency)
    {
        if (HeliportSpawnTile(simulation, hospital.Player) is null || !ReceptionDesks.OfHospital(simulation, hospital.Player, staffedOnly: true).Any()
            || !hospital.CaseOf(DiseaseCatalog.Find(emergency.DiseaseId)!, simulation.Rules.Diseases).Discovered)
        {
            return false;
        }

        emergency.OfferExpires = simulation.Tick + (HospitalEmergencies.OfferDays * GameDate.HoursPerDay);
        hospital.Emergencies.Current = emergency;
        return true;
    }

    /// <summary>
    /// <c>Cheats:cheatEmergency</c>: an emergency of a random known disease, 2 to its <c>EmergencyNumber</c> patients, is
    /// offered now, if the hospital has a heliport and a staffed desk. Not while one is under way. The level's own
    /// emergencies come only by its schedule.
    /// </summary>
    internal static bool CreateRandom(Simulation simulation, Hospital hospital)
    {
        var available = simulation.Rules.Diseases.AvailableDiseases().ToList();
        if (hospital.Emergencies.Current is { IsAccepted: true } || available.Count == 0)
        {
            return false;
        }

        var random = available[simulation.Random.Next(available.Count)];
        var number = simulation.Random.NextInclusive(2, Math.Max(random.EmergencyNumber, 2));
        return Offer(simulation, hospital, new Emergency(random.Id, number, RandomBonus, RandomPercentage));
    }

    /// <summary>
    /// <c>UIFax:choice</c>: accepted, the helicopter comes and the watch starts; refused, the next emergency is planned.
    /// </summary>
    internal static void Answer(Simulation simulation, int player, bool accept)
    {
        if (player < 0 || player >= simulation.Hospitals.Count || simulation.Hospitals[player].Emergencies.Current is not { IsAccepted: false } emergency)
        {
            return;
        }

        var hospital = simulation.Hospitals[player];
        var state = hospital.Emergencies;
        if (IsShared(simulation))
        {
            // First come, first served: accepted, the others' offers go; refused, only this hospital's goes. The first
            // hospital's schedule plans the next once nobody's offer is left.
            var others = OthersOffered(simulation, player, emergency).ToList();
            if (accept)
            {
                foreach (var other in others)
                {
                    other.Emergencies.Current = null;
                }
            }
            else
            {
                state.Current = null;
            }

            if (accept || others.Count == 0)
            {
                NextEmergency(simulation, simulation.Hospitals[0]);
            }

            if (!accept)
            {
                return;
            }
        }
        else if (!accept)
        {
            state.Current = null;
            NextEmergency(simulation, hospital);
            return;
        }

        emergency.IsAccepted = true;
        emergency.WatchTimer = HospitalEmergencies.WatchSegments - 1;
        emergency.WatchTickTimer = HospitalEmergencies.WatchTickRate;
        state.HelicopterPhase = HospitalEmergencies.HelicopterStart;
        state.HelicopterSpawned = 0;
        state.HelicopterVictims = emergency.Victims;
        state.HelicopterDisease = emergency.DiseaseId;
    }

    /// <summary>
    /// One hour after the entities: the helicopter's (<c>Helicopter:tick</c>), then the fax's timeout (refusing) or the
    /// watch's (<c>UIWatch:onWorldTick</c>).
    /// </summary>
    internal static void Tick(Simulation simulation, Hospital hospital)
    {
        var state = hospital.Emergencies;
        if (state.Report is { } report && simulation.Tick >= report.Expires)
        {
            state.Report = null;
        }

        TickHelicopter(simulation, hospital, state);
        if (state.Current is not { } emergency)
        {
            return;
        }

        if (!emergency.IsAccepted)
        {
            if (simulation.Tick >= emergency.OfferExpires)
            {
                Answer(simulation, hospital.Player, accept: false);
            }

            return;
        }

        if (emergency.WatchTickTimer == 0 && emergency.WatchTimer >= 0)
        {
            emergency.WatchTickTimer = HospitalEmergencies.WatchTickRate;
            emergency.WatchTimer--;
        }
        else if (emergency.WatchTimer == -1)
        {
            Resolve(simulation, hospital);
        }
        else
        {
            emergency.WatchTickTimer--;
        }
    }

    /// <summary>
    /// An emergency patient was cured, or died or went home uncured; once all are accounted for the watch ends early
    /// (<c>updateCuredCounts</c>, <c>humanoidDeath</c>, <c>updateNotCuredCounts</c>, <c>checkEmergencyOver</c>).
    /// </summary>
    internal static void PatientDone(Simulation simulation, Hospital hospital, bool cured)
    {
        if (hospital.Emergencies.Current is not { IsAccepted: true } emergency)
        {
            return;
        }

        if (cured)
        {
            emergency.Cured++;
        }
        else
        {
            emergency.Killed++;
        }

        if (emergency.Killed + emergency.Cured >= emergency.Victims)
        {
            Resolve(simulation, hospital);
        }
    }

    /// <summary>
    /// <c>Hospital:resolveEmergency</c>: the patients still waiting are set to die; enough cured brings a bonus for each
    /// and reputation, too few cost reputation. Then the next emergency is planned.
    /// </summary>
    private static void Resolve(Simulation simulation, Hospital hospital)
    {
        var state = hospital.Emergencies;
        if (state.Current is not { } emergency)
        {
            return;
        }

        state.Current = null;
        foreach (var id in emergency.PatientIds)
        {
            if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour patient, IsDespawning: false })
            {
                patient.SetToDieUnlessCuredOrLeaving();
            }
        }

        // For the year's award for emergencies.
        hospital.EmergenciesThisYear++;
        hospital.EmergencyVictimsThisYear += emergency.Victims;
        hospital.EmergencySavedThisYear += emergency.Cured;

        var success = emergency.Cured * 100 >= emergency.Percentage * emergency.Victims;
        var earned = success ? emergency.Bonus * emergency.Cured : 0;
        state.Report = new EmergencyReport(emergency.Cured, emergency.Victims, emergency.Bonus * emergency.Victims, earned, simulation.Tick + (HospitalEmergencies.ReportDays * GameDate.HoursPerDay));
        if (success)
        {
            hospital.NetMission.Happened(NetCriterion.Emergency);
            hospital.ChangeReputationPercent(SuccessReputationPercent);
            if (earned != 0)
            {
                hospital.Receive(simulation.Hours, earned, TransactionKind.EmergencyBonus, string.Empty);
            }
        }
        else
        {
            // Unlike CorsixTH's fixed −20, the loss grows with the patients not cured.
            hospital.ChangeReputationPercent(FailureReputationPercent * (emergency.Victims - emergency.Cured));
        }

        NextEmergency(simulation, hospital);
    }

    /// <summary>
    /// <c>Helicopter:tick</c>: 120 hours on the way, 60 coming down, then a patient every 26 hours until all are out,
    /// and 60 hours going up again.
    /// </summary>
    private static void TickHelicopter(Simulation simulation, Hospital hospital, HospitalEmergencies state)
    {
        if (state.HelicopterPhase is not { } phase)
        {
            return;
        }

        if (phase == 60)
        {
            state.HelicopterSpawned = 0;
        }
        else if (phase == 85 && state.HelicopterSpawned < state.HelicopterVictims)
        {
            SpawnPatient(simulation, hospital, state);
            phase = 60;
        }
        else if (phase == 147)
        {
            state.HelicopterPhase = null;
            return;
        }

        state.HelicopterPhase = phase + 1;
    }

    /// <summary>
    /// <c>Helicopter:spawnPatient</c>: a patient with the emergency's disease, diagnosed, comes out by the heliport and
    /// heads for the last room of the treatment, unless they find its price too high.
    /// </summary>
    private static void SpawnPatient(Simulation simulation, Hospital hospital, HospitalEmergencies state)
    {
        state.HelicopterSpawned++;
        if (DiseaseCatalog.Find(state.HelicopterDisease) is not { } disease || HeliportSpawnTile(simulation, hospital.Player) is not { } tile)
        {
            return;
        }

        var health = simulation.Random.Triangular(200, PatientNeeds.MaxHealth);
        var humanoid = simulation.AddEntity(id => Humanoid.CreatePatient(id, tile, simulation.Random, disease.Look, hospital.Player));
        var patient = new PatientBehaviour { Health = health };
        patient.SetDisease(disease, PatientCare.ChooseInsuranceCompany(simulation), simulation.Rules.Needs);
        patient.Wallet = PatientCare.NewPatientMoney(simulation);
        humanoid.Behaviour = patient;
        hospital.Visitors++;
        hospital.VisitorsThisYear++;
        hospital.Emergencies.Current?.PatientIds.Add(humanoid.Id);
        patient.StartEmergency(simulation, humanoid, state.HelicopterSpawned);
    }

    /// <summary>
    /// <c>Hospital:getHeliportSpawnPosition</c>: the tile north of the level's heliport tile, if the heliport tile is
    /// passable (level 2 names a heliport tile but has no heliport).
    /// </summary>
    public static TilePosition? HeliportSpawnTile(Simulation simulation, int player)
    {
        var map = simulation.Map;
        if (player < 0 || player >= map.PlayerCount)
        {
            return null;
        }

        var heliport = map.GetHeliportTile(player);
        if (!map.Contains(heliport.X, heliport.Y) || heliport.Y < 1 || !map[heliport.X, heliport.Y].Has(TileFlags.Passable))
        {
            return null;
        }

        return heliport with { Y = heliport.Y - 1 };
    }

    /// <summary>
    /// <c>World:computeNextEmergencyDates</c>: a random month from the start (no earlier than now) to the end, and a
    /// random day of it, no earlier than today if it is this month. False if that is already past.
    /// </summary>
    private static bool ComputeNextDates(Simulation simulation, HospitalEmergencies state, PlannedEmergency emergency)
    {
        var date = simulation.Date;
        var start = Math.Max(emergency.StartMonth, date.MonthOfGame);
        if (emergency.EndMonth < start)
        {
            return false;
        }

        var month = simulation.Random.NextInclusive(start, emergency.EndMonth);
        state.NextMonth = month;
        var dayStart = start == emergency.EndMonth ? date.Day : 1;
        state.NextDay = simulation.Random.NextInclusive(dayStart, GameDate.DaysIn(1 + ((month - 1) % 12)));

        // CorsixTH compares dates with their hours: today counts only at the first hour.
        return month > date.MonthOfGame || (month == date.MonthOfGame && (state.NextDay > date.Day || (state.NextDay == date.Day && date.Hour == 0)));
    }

    private static PlannedEmergency? Find(IReadOnlyList<PlannedEmergency> planned, int index)
    {
        foreach (var emergency in planned)
        {
            if (emergency.Index == index)
            {
                return emergency;
            }
        }

        return null;
    }
}

/// <summary>An emergency's result: patients saved of how many, the bonus there was to earn, what was earned, and when the fax expires.</summary>
public readonly record struct EmergencyReport(int Rescued, int Total, int MaxBonus, int Earned, long Expires);

