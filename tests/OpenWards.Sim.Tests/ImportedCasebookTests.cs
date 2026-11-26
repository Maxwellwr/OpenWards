using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.Tests;

/// <summary>What an original save's casebook brings into the game.</summary>
public class ImportedCasebookTests
{
    [Fact]
    public void OnlyDrugsTakeTheOriginalsEffectiveness()
    {
        var simulation = NewSimulation();

        // The original keeps 0 for diseases not cured by drugs, such as fake blood at the psychiatrist's.
        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Casebook =
            [
                new ImportedCase("fake_blood", true, 0, 3, 0, 0, 0),
                new ImportedCase("king_complex", true, 0, 1, 0, 0, 0),
                new ImportedCase("broken_wind", true, 40, 2, 0, 0, 0),
            ],
        });

        var casebook = simulation.Hospitals[0].Casebook;
        Assert.Equal(DiseaseCase.FullEffectiveness, casebook["fake_blood"].CureEffectiveness);
        Assert.Equal(DiseaseCase.FullEffectiveness, casebook["king_complex"].CureEffectiveness);
        Assert.Equal(40, casebook["broken_wind"].CureEffectiveness);
        Assert.Equal(3, casebook["fake_blood"].Recoveries);
    }

    [Fact]
    public void LoadingAGameImportedBeforeTheFixMendsTheEffectiveness()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.CaseOf(DiseaseCatalog.Find("fake_blood")!, simulation.Rules.Diseases).CureEffectiveness = 0;
        hospital.CaseOf(DiseaseCatalog.Find("broken_wind")!, simulation.Rules.Diseases).CureEffectiveness = 40;

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var loaded = Simulation.Load(stream).Hospitals[0].Casebook;

        Assert.Equal(DiseaseCase.FullEffectiveness, loaded["fake_blood"].CureEffectiveness);
        Assert.Equal(40, loaded["broken_wind"].CureEffectiveness);
    }

    [Fact]
    public void TakesTheSavesReputationRecordInsuranceDebtsAndPriceLevel()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 17,
            Visitors = 295,
            Reputation = 464,
            ReputationRecord = (239, 257),
            Casebook = [new ImportedCase("broken_wind", true, 40, 2, 0, 0, 0, ReputationPoints: 44, ReputationVisits: 36)],
            InsuranceOwed = [(1, 0, 1332), (3, 9, 589)],
            PriceLevel = 102,
        });

        var hospital = simulation.Hospitals[0];
        Assert.Equal((239L * Hospital.ReputationPerPoint, 257), (hospital.ReputationScore, hospital.ReputationVisits));
        Assert.Equal(464, hospital.Reputation);
        var wind = hospital.Casebook["broken_wind"];
        Assert.Equal((44L * Hospital.ReputationPerPoint, 36), (wind.ReputationScore, wind.ReputationVisits));
        Assert.Equal((1332, 0, 589), (hospital.InsuranceOwed(1), hospital.InsuranceOwed(2), hospital.InsuranceOwed(3)));
        Assert.Equal(102, hospital.PriceLevel);
    }

    [Fact]
    public void PatientsKeepTheirMoneyInsurerAndNeeds()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Patients =
            [
                new ImportedPatient("broken_wind", new TilePosition(2, 2), 0.472m, 0.9m, 0.6m, 0.5m, 1522, Wallet: 38710, InsuranceCompany: 2, DiagnosisDifficulty: 83, Bowel: 42, Nausea: 38, ContagiousStrength: 4),
            ],
        });

        var patient = simulation.Entities.OfType<Entities.Humanoid>().Select(h => h.Behaviour).OfType<People.PatientBehaviour>().Single();
        Assert.Equal(472, patient.Health);
        Assert.Equal((38710, 2, 83), (patient.Wallet, patient.InsuranceCompany, patient.DiagnosisDifficulty));
        Assert.Equal((42, 38, 4), (patient.Bowel, patient.Nausea, patient.ContagiousStrength));
        Assert.Equal(1000, patient.DiagnosisProgress);
    }

    [Fact]
    public void StaffKeepTheirDiscontentRaiseRequestsServiceAndShares()
    {
        var simulation = NewSimulation();
        var handyman = StaffProfile.CreateRandom(StaffClass.Handyman, 3, simulation.Rules.Staff, simulation.Random);

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Staff = [new ImportedStaff(handyman, new TilePosition(2, 2), 0.5m, 0.2m, Discontent: 53, RaiseRequests: 4, HiredHoursAgo: 1000, HandymanShares: (20, 30, 50))],
        });

        var staff = simulation.Entities.OfType<Entities.Humanoid>().Single(h => h.Profile is not null);
        Assert.Equal((53, 4), (staff.Discontent, staff.RaiseRequestCount));
        Assert.Equal(simulation.Hours - 1000, staff.HiredAt);
        var behaviour = Assert.IsType<People.HandymanBehaviour>(staff.Behaviour);
        Assert.Equal((200, 300, 500), (behaviour.CleaningPriority, behaviour.WateringPriority, behaviour.RepairingPriority));
    }

    [Fact]
    public void TakesTheYearsVipsAndRatsAndTheDaysSinceTheLastVip()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            VipsThisYear = (1, -2),
            DaysSinceVip = 82,
            RatsThisYear = (7, 12),
        });

        var hospital = simulation.Hospitals[0];
        Assert.Equal((1, -2, 1), (hospital.Vips.ThisYear, hospital.Vips.ScoreSumThisYear, hospital.Vips.PleasedThisYear));
        Assert.Equal(82, hospital.Vips.DaysSinceLast);
        Assert.Equal((7, 12), (hospital.RatsKilledThisYear, hospital.RatShotsThisYear));
    }

    [Fact]
    public void LitterAndRatholesComeBack()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Litter = [(new TilePosition(1, 1), Entities.LitterType.Banana), (new TilePosition(2, 4), Entities.LitterType.Puke)],
            Ratholes = [(new TilePosition(3, 3), Entities.Direction.West)],
        });

        var litter = simulation.Entities.OfType<Entities.Litter>().OrderBy(l => l.Tile.X).Select(l => (l.Tile, l.Type));
        Assert.Equal([(new TilePosition(1, 1), Entities.LitterType.Banana), (new TilePosition(2, 4), Entities.LitterType.Puke)], litter);
        var hole = Assert.Single(simulation.Hospitals[0].Ratholes);
        Assert.Equal((new TilePosition(3, 3), Entities.Direction.West), (hole.Tile, hole.Wall));
    }

    [Fact]
    public void AVipOnTheWayArrivesAsMuchLaterAsInTheSave()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame { Month = 3, Day = 1, Vip = new ImportedVip(Economy.VipState.Travelling, 3, 1387, 0, null) });

        var vips = simulation.Hospitals[0].Vips;
        Assert.Equal((Economy.VipState.Travelling, 3, simulation.Tick + 1387), (vips.State, vips.InvitedName, vips.ArrivalTick));
    }

    [Fact]
    public void AVipInTheHospitalCarriesOnWithTheRoomsLeft()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame { Month = 3, Day = 1, Vip = new ImportedVip(Economy.VipState.Visiting, 10, -4080, 4, new TilePosition(2, 2)) });

        var vip = simulation.Entities.OfType<Entities.Humanoid>().Select(h => h.Behaviour).OfType<People.VipBehaviour>().Single();
        Assert.Equal((10, People.VipBehaviour.RoomsToVisit - 4, People.VipPhase.Waiting), (vip.Name, vip.VisitedRooms, vip.Phase));
        Assert.Equal(Economy.VipState.Visiting, simulation.Hospitals[0].Vips.State);
    }

    [Fact]
    public void PatientsPickUpTheirRouteAndTheYearsResearchCounts()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            ResearchDistributedThisYear = 25701,
            Patients =
            [
                new ImportedPatient("broken_wind", new TilePosition(1, 1), 0.5m, 0.5m, 0.5m, 0.5m, 1000, Diagnosed: true),
                new ImportedPatient("broken_wind", new TilePosition(4, 4), 0.5m, 0.5m, 0.5m, 0.5m, 400, SeenByGp: true),
                new ImportedPatient("broken_wind", new TilePosition(1, 4), 0.5m, 0.5m, 0.5m, 0.5m, 0),
            ],
        });

        var patients = simulation.Entities.OfType<Entities.Humanoid>().Where(h => h.Behaviour is People.PatientBehaviour).OrderBy(h => h.Id)
            .Select(h => (People.PatientBehaviour)h.Behaviour!).ToList();
        Assert.Equal((true, true, People.RoomSeek.Treatment), (patients[0].IsDiagnosed, patients[0].HasPassedReception, patients[0].Seeking));
        Assert.Equal((false, true, People.RoomSeek.Gp), (patients[1].IsDiagnosed, patients[1].HasPassedReception, patients[1].Seeking));
        Assert.False(patients[2].HasPassedReception);
        Assert.Equal(25701, simulation.Hospitals[0].ResearchDistributedThisYear);
    }

    [Fact]
    public void AnEmergencyUnderWayKeepsItsPatientsAndWatch()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Emergency = new ImportedEmergency("broken_wind", 3, 1, 500, 75, 1000),
            Patients = [new ImportedPatient("broken_wind", new TilePosition(1, 1), 0.5m, 0.5m, 0.5m, 0.5m, 1000, IsEmergency: true)],
        });

        var emergency = simulation.Hospitals[0].Emergencies.Current;
        Assert.NotNull(emergency);
        Assert.Equal((3, 1, 500, 75, true), (emergency.Victims, emergency.Cured, emergency.Bonus, emergency.Percentage, emergency.IsAccepted));
        Assert.Single(emergency.PatientIds);
        Assert.Equal((1000 / Disasters.HospitalEmergencies.WatchTickRate, 1000 % Disasters.HospitalEmergencies.WatchTickRate), (emergency.WatchTimer, emergency.WatchTickTimer));
    }

    [Fact]
    public void ThePlannedEarthquakeComesOnTheSavesDay()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame { Month = 3, Day = 1, NextEarthquake = (2, 10 * Calendar.GameDate.HoursPerDay, 4) });

        var quake = simulation.Earthquakes;
        var expected = Calendar.GameDate.FromHours(simulation.Hours + (10 * Calendar.GameDate.HoursPerDay));
        Assert.True(quake.IsNextPlanned);
        Assert.Equal((expected.MonthOfGame, expected.Day, 4), (quake.StartMonth, quake.StartDay, quake.Size));
    }

    [Fact]
    public void ARaiseRequestStillUnansweredWaitsOn()
    {
        var simulation = NewSimulation();
        var doctor = StaffProfile.CreateRandom(StaffClass.Doctor, 3, simulation.Rules.Staff, simulation.Random);

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Staff = [new ImportedStaff(doctor, new TilePosition(2, 2), 0.1m, 0.2m, Discontent: 152, RaiseRequests: 1, HiredHoursAgo: 50000, RaiseHoursLeft: 1481)],
        });

        // The request counted in the save is the one waiting: it is made again, and the 30 days run on from where they were.
        var staff = simulation.Entities.OfType<Entities.Humanoid>().Single(h => h.Profile is not null);
        Assert.True(staff.HasRaiseRequest);
        Assert.Equal((1, 1481), (staff.RaiseRequestCount, staff.QuittingIn));
    }

    [Fact]
    public void AnAcceptedEmergencyWaitsForItsHelicopter()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Emergency = new ImportedEmergency("broken_wind", 4, 0, 500, 75, 2400, ImportedEmergencyStage.WaitingForHelicopter, HelicopterDaysLeft: 3),
        });

        var state = simulation.Hospitals[0].Emergencies;
        Assert.NotNull(state.Current);
        Assert.True(state.Current.IsAccepted);
        Assert.Equal((-3 * Calendar.GameDate.HoursPerDay, 4, 0), (state.HelicopterPhase, state.HelicopterVictims, state.HelicopterSpawned));
    }

    [Fact]
    public void AHelicopterLettingOutPatientsLetsOutTheRest()
    {
        var simulation = NewSimulation();

        // Five patients: one cured, one in the hospital, two still in the helicopter, so one died or went home.
        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Emergency = new ImportedEmergency("broken_wind", 5, 1, 500, 75, 2000, ImportedEmergencyStage.UnderWay, ToLetOut: 2, InHospital: 1),
            Patients = [new ImportedPatient("broken_wind", new TilePosition(1, 1), 0.5m, 0.5m, 0.5m, 0.5m, 1000, IsEmergency: true)],
        });

        var state = simulation.Hospitals[0].Emergencies;
        Assert.NotNull(state.Current);
        Assert.Equal((1, 1), (state.Current.Cured, state.Current.Killed));
        Assert.Equal((Disasters.HospitalEmergencies.HelicopterLanded + 1, 5, 3), (state.HelicopterPhase, state.HelicopterVictims, state.HelicopterSpawned));
    }

    [Fact]
    public void AnEmergencyWhoseTimeIsUpEndsAtOnce()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Emergency = new ImportedEmergency("broken_wind", 4, 3, 500, 75, 0, ImportedEmergencyStage.TimeUp, InHospital: 0),
        });

        simulation.Step();

        var state = simulation.Hospitals[0].Emergencies;
        Assert.Null(state.Current);
        Assert.Equal((3, 4, 1500), (state.Report?.Rescued, state.Report?.Total, state.Report?.Earned));
    }

    [Fact]
    public void AnEmergencyOverKeepsOnlyItsResult()
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame
        {
            Month = 3,
            Day = 1,
            Emergency = new ImportedEmergency("broken_wind", 9, 7, 1000, 75, 0, ImportedEmergencyStage.Result),
        });

        var state = simulation.Hospitals[0].Emergencies;
        Assert.Null(state.Current);
        Assert.Equal((7, 9, 7000), (state.Report?.Rescued, state.Report?.Total, state.Report?.Earned));
    }

    [Theory]
    [InlineData(1, 300, 300, 5)]
    [InlineData(2, 100, 100, 5)]
    [InlineData(3, 40, 0, 3)]
    [InlineData(4, 50, 0, 0)]
    public void AnEarthquakeUnderWayGoesOn(int phase, long hoursToStep, int warningTimer, int remainingDamage)
    {
        var simulation = NewSimulation();

        GameImport.Apply(simulation, new ImportedGame { Month = 7, Day = 10, NextEarthquake = (1, null, 5), EarthquakeUnderWay = (phase, hoursToStep, 5) });

        var quake = simulation.Earthquakes;
        Assert.True(quake.IsActive);
        Assert.Equal((5, warningTimer, remainingDamage), (quake.Size, quake.WarningTimer, quake.RemainingDamage));
    }

    private static Simulation NewSimulation()
    {
        var map = new LevelMap(6, 6, 1);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                map[x, y].Floor = 1;
                map[x, y].Set(TileFlags.Passable, true);
            }
        }

        map[3, 3].Set(TileFlags.Hospital, true);
        map.UpdatePathfinding();
        return new Simulation(map, seed: 2);
    }
}
