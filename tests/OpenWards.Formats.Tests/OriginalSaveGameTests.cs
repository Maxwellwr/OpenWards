using System.Buffers.Binary;
using System.Text;

namespace OpenWards.Formats.Tests;

public class OriginalSaveGameTests
{
    [Fact]
    public void ReadsHeaderDateTilesEntitiesAndHospitals()
    {
        var data = new byte[OriginalSaveGame.Length];
        data[0] = 3;
        data[2] = 4;
        Encoding.ASCII.GetBytes("FULL").CopyTo(data, 3);
        data[OriginalSaveGame.DateOffset] = 17;
        data[OriginalSaveGame.DateOffset + 1] = 2;
        data[OriginalSaveGame.YearOffset] = 3;

        var tile = OriginalSaveGame.TileOffset + (((5 * OriginalSaveGame.MapSize) + 7) * 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(tile), 9);
        data[tile + 2] = 0x10;
        data[tile + 3] = 0x20;
        data[tile + 4] = 0x21;
        data[tile + 6] = 0x31;
        data[tile + 7] = 2;

        var entity = OriginalSaveGame.EntityOffset + (9 * OriginalSaveGame.EntityLength);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 4), 0x0780);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 6), 0x0540);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 8), 0x019A);
        data[entity + 12] = 6;
        data[entity + 14] = 11;
        data[entity + 17] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 100), 400);

        var hospital = OriginalSaveGame.HospitalOffset + OriginalSaveGame.HospitalLength;
        Encoding.ASCII.GetBytes("MAXWELL").CopyTo(data, hospital);
        data[hospital + 42] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 53), -17328);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 61), 35000);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 443), 87310);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 489), 131);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3228), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3232), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3236), 1);

        var save = OriginalSaveGame.Parse(data);

        Assert.Equal(4, save.Level);
        Assert.Equal("FULL", save.Difficulty);
        Assert.Equal(17, save.Day);
        Assert.Equal(2, save.Month);
        Assert.Equal(3, save.Year);
        Assert.Equal(new OriginalSaveTile(9, 0x10, 0x20, 0x21, 0, 0x31, 2), save[7, 5]);

        var parsed = Assert.Single(save.Entities);
        Assert.Equal(9, parsed.Slot);
        Assert.Equal((7, 5), (parsed.TileX, parsed.TileY));
        Assert.Equal((0x0780, 0x0540), (parsed.X, parsed.Y));
        Assert.Equal(0x019A, parsed.Frame);
        Assert.Equal(6, parsed.Kind);
        Assert.Equal(11, parsed.Type);
        Assert.Equal(2, parsed.Direction);
        Assert.Equal(400, parsed.PlantWater);

        Assert.Equal(OriginalSaveGame.MaxPlayers, save.Hospitals.Count);
        Assert.Equal(0, save.Hospitals[0].Kind);
        var second = save.Hospitals[1];
        Assert.Equal("MAXWELL", second.Name);
        Assert.Equal(1, second.Kind);
        Assert.Equal(-17328, second.Money);
        Assert.Equal(35000, second.Loan);
        Assert.Equal(87310, second.Value);
        Assert.Equal(131, second.PeopleCured);
        Assert.Equal(20, second.Staff);
        Assert.Equal(10, second.Doctors);
        Assert.Equal(1, second.Receptionists);
    }

    [Theory]
    [InlineData(2, "header +2 level")]
    [InlineData(13 + (((5 * 128) + 7) * 8) + 2, "tile (7,5) byte 2 floor")]
    [InlineData(131085 + (9 * 175) + 14, "entity 9 +14 type")]
    [InlineData(0x60194 + (17 * 35) + 2, "room 17 +2 height")]
    [InlineData(0x6661C + 2, "0x6661E plot owners")]
    [InlineData(399030, "0x616B6 day")]
    [InlineData(399031, "0x616B7 month")]
    [InlineData(399968 + 4697 + 53, "hospital 1 +53 money")]
    [InlineData(0x61000, "0x61000")]
    public void DescribesOffsets(int offset, string expected)
    {
        Assert.Equal(expected, OriginalSaveGame.DescribeOffset(offset));
    }

    [Fact]
    public void ReadsRooms()
    {
        var data = new byte[OriginalSaveGame.Length];
        var room = OriginalSaveGame.RoomOffset + (17 * OriginalSaveGame.RoomLength);
        data[room] = 7;
        data[room + 1] = 4;
        data[room + 2] = 5;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(room + 4), (81 * 128) + 82);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(room + 6), (81 * 128) + 83);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(room + 8), (80 * 128) + 83);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(room + 16), 124);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(room + 18), 157);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(room + 20), 83);
        data[room + 30] = 6;
        data[room + 31] = 2;
        data[room + 32] = 1;

        var parsed = Assert.Single(OriginalSaveGame.Parse(data).Rooms);

        Assert.Equal(17, parsed.Index);
        Assert.Equal(7, parsed.Type);
        Assert.Equal((4, 5), (parsed.Width, parsed.Height));
        Assert.Equal((83, 80), parsed.Origin);
        Assert.Equal((83, 81), parsed.Door);
        Assert.Equal((82, 81), parsed.Outside);
        Assert.Equal([124, 157], parsed.Staff);
        Assert.Equal(83, parsed.VisitorCount);
        Assert.Equal((6, 2, 1), (parsed.MaxQueueSize, parsed.QueueSize, parsed.Expected));
    }

    [Fact]
    public void ReadsPlotOwners()
    {
        var data = new byte[OriginalSaveGame.Length];
        data[OriginalSaveGame.PlotOffset] = 16;
        data[OriginalSaveGame.PlotOffset + 1] = 1;

        var save = OriginalSaveGame.Parse(data);

        Assert.Equal(16, save.PlotOwner(0));
        Assert.Equal(1, save.PlotOwner(1));
        Assert.Equal(0, save.PlotOwner(2));
    }

    [Fact]
    public void ReadsPolicyResearchReputationAndDiseases()
    {
        var data = new byte[OriginalSaveGame.Length];
        var hospital = OriginalSaveGame.HospitalOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 535), 1000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 537), 79);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 539), 70);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 541), 600);
        data[hospital + 543] = 1;
        short[] distribution = [-1, -1, 20, 0, 20];
        for (var i = 0; i < distribution.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(hospital + 433 + (2 * i)), distribution[i]);
        }

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 3198), 844);
        data[hospital + 3220] = 9;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 45), 74131);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 455), 193);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 477), 500);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 479), 812);
        data[hospital + 484] = 29;
        data[hospital + 485] = 3;
        data[hospital + 486] = 3;
        data[hospital + 488] = 75;
        var gp = hospital + 544 + 38;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(gp + 12), 168);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(gp + 16), 10000);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(gp + 20), 9180);
        data[gp + 27] = 100;
        data[gp + 29] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(gp), 611);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(gp + 4), 44);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(gp + 8), 36);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 221), 3450);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 225 + (39 * 4)), 21345);
        data[gp + 28] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(gp + 30), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(gp + 32), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(gp + 36), 1);

        var parsed = OriginalSaveGame.Parse(data).Hospitals[0];

        Assert.Equal((1000, 79, 70, 600, true), (parsed.StopProcedure, parsed.GuessCurePercent, parsed.SendHomePercent, parsed.StaffRestLevel, parsed.StaffLeaveRooms));
        Assert.Equal([-1, -1, 20, 0, 20], parsed.ResearchDistribution);
        Assert.Equal(844, parsed.Reputation);
        Assert.Equal(9, parsed.HeatingLevel);
        Assert.Equal((74131, 193), (parsed.PlayerSalary, parsed.VisitorsAtYearStart));
        Assert.Equal((500, 29, 3, 3, 75), (parsed.EmergencyBonus, parsed.EmergencyDisease, parsed.EmergencyCured, parsed.EmergencyPatients, parsed.EmergencyPassPercent));
        var disease = parsed.Disease(1);
        Assert.Equal((168, 10000, 9180, true), (disease.Charge, disease.ChargePercent, disease.MoneyEarned, disease.CanTreatNow));
        Assert.Equal((3, 2, 1), (disease.Recoveries, disease.Fatalities, disease.TurnedAway));
        Assert.Equal(100, disease.DrugEffectiveness);
        Assert.True(disease.Discovered);
        Assert.Equal((611, 44, 36), (disease.Reputation, disease.ReputationPoints, disease.ReputationVisits));
        Assert.Equal(3450, parsed.ResearchPoints);
        Assert.Equal(21345, parsed.ExpertiseResearch(39));
        Assert.False(parsed.Disease(2).CanTreatNow);
    }

    [Fact]
    public void ReadsObjectTypesDrugCostReputationAndInsurance()
    {
        var data = new byte[OriginalSaveGame.Length];
        var hospital = OriginalSaveGame.HospitalOffset;
        data[hospital + 42] = 1;
        var xRay = hospital + OriginalSaveHospital.ObjectTypeOffset + (27 * OriginalSaveHospital.ObjectTypeLength);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(xRay), 3340);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(xRay + 8), 18);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(xRay + 10), 1);
        data[xRay + 12] = 1;
        data[xRay + 13] = 18;
        var disease = hospital + OriginalSaveHospital.DiseaseOffset + (5 * OriginalSaveHospital.DiseaseLength);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(disease + 24), 95);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 3202), 239);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 3206), 257);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3214), 102);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 69 + 8), 1332);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 69 + 96 + 40), 589);

        var parsed = OriginalSaveGame.Parse(data).Hospitals[0];

        var type = parsed.ObjectType(27);
        Assert.Equal((3340, 18, 1, true, 9), (type.Cost, type.StartStrength, type.Bought, type.Available, type.Improvements));
        Assert.False(parsed.ObjectType(13).Available);
        Assert.Equal(95, parsed.Disease(5).DrugCost);
        Assert.Equal((239, 257, 102), (parsed.ReputationPoints, parsed.ReputationVisits, parsed.PriceLevel));
        Assert.Equal(1332, parsed.InsuranceOwed(0, 2));
        Assert.Equal(589, parsed.InsuranceOwed(2, 10));
    }

    [Fact]
    public void ReadsChartHistory()
    {
        var data = new byte[OriginalSaveGame.Length];
        var balance = OriginalSaveGame.ChartOffset + (3 * 3 * 12 * 4);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(balance), -5189);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(balance + (11 * 4)), 293);

        var chart = OriginalSaveGame.Parse(data).Chart(OriginalSaveChart.Balance);

        Assert.Equal(12, chart.Count);
        Assert.Equal(-5189, chart[0]);
        Assert.Equal(293, chart[11]);
    }

    [Fact]
    public void ReadsBankStatement()
    {
        var data = new byte[OriginalSaveGame.Length];
        var line = OriginalSaveGame.StatementOffset + 8;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(line), -2280);
        data[line + 4] = 4;
        data[line + 5] = 2;
        data[line + 6] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(line + 8), 923);
        data[line + 12] = 7;
        data[line + 13] = 28;
        data[line + 14] = 8;
        data[line + 15] = 2;

        var statement = OriginalSaveGame.Parse(data).Statement;

        Assert.Equal([new OriginalSaveStatementLine(-2280, 4, 2, 0, 0), new OriginalSaveStatementLine(923, 7, 28, 8, 2)], statement);
    }

    [Fact]
    public void ReadsStaffFields()
    {
        var data = new byte[OriginalSaveGame.Length];
        var entity = OriginalSaveGame.EntityOffset + (109 * OriginalSaveGame.EntityLength);
        data[entity + 12] = 4;
        data[entity + 14] = 1;
        data[entity + 34] = 2;
        data[entity + 141] = 7;
        data[entity + 152] = 18;
        data[entity + 153] = 23;
        data[entity + 124] = 16 + 32;
        data[entity + 125] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 126), 565);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 108), 161);
        data[entity + 132] = (byte)'T';
        data[entity + 133] = 16;
        data[entity + 134] = 15;
        data[entity + 138] = 23;
        data[entity + 139] = 13;
        data[entity + 81] = 94;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 120), 324);

        var staff = Assert.Single(OriginalSaveGame.Parse(data).Entities);

        Assert.Equal(16 + 32 + 256, staff.StaffFlags);
        Assert.Equal(565, staff.Skill);
        Assert.Equal(161, staff.Salary);
        Assert.Equal('T', staff.Initial);
        Assert.Equal((16, 15), (staff.NameStart, staff.NameEnd));
        Assert.Equal(((byte)23, (byte)13), staff.Description);
        Assert.Equal(94, staff.Morale);
        Assert.Equal(((byte)0, (byte)18, (byte)23), staff.TrainingProgress);
        Assert.Equal(7, staff.AttentionToDetail);
        Assert.Equal(13, staff.Layers.Count);
        Assert.Equal(4, staff.Layers[5]);
        Assert.Equal(324, staff.Fatigue);
    }

    [Fact]
    public void ReadsPatientFields()
    {
        var data = new byte[OriginalSaveGame.Length];
        var entity = OriginalSaveGame.EntityOffset + (189 * OriginalSaveGame.EntityLength);
        data[entity + 12] = 2;
        byte[] history = [13, 13, 13, 13, 13, 13, 26, 21, 16, 11];
        history.CopyTo(data, entity + 158);
        data[entity + 81] = 68;
        data[entity + 134] = 42;
        data[entity + 83] = 31;
        data[entity + 168] = 16;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 146), 1922);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 130), 605);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entity + 112), 78828);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 144), 35);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(entity + 136), 15);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(entity + 138), 225);
        data[entity + 173] = 4;
        data[entity + 174] = 3;

        var patient = Assert.Single(OriginalSaveGame.Parse(data).Entities);

        Assert.Equal(history, patient.HealthHistory.ToArray());
        Assert.Equal(68, patient.Happiness);
        Assert.Equal(42, patient.Thirst);
        Assert.Equal(31, patient.Warmth);
        Assert.Equal(16, patient.Disease);
        Assert.Equal(1922, patient.DiagnosisProgress);
        Assert.Equal((605, 78828, 35), (patient.Health, patient.Wallet, patient.DiagnosisDifficulty));
        Assert.Equal((15, 225), (patient.Bowel, patient.Nausea));
        Assert.Equal(((byte)4, (byte)3), (patient.InsurerBit, patient.ContagiousStrength));
    }

    [Fact]
    public void ReadsStaffDiscontentRaisesAndHiringTick()
    {
        var data = new byte[OriginalSaveGame.Length];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(OriginalSaveGame.TickOffset), 76780);
        var entity = OriginalSaveGame.EntityOffset + (40 * OriginalSaveGame.EntityLength);
        data[entity + 12] = 4;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 124), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 128), 53);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 130), 11222);
        data[entity + 143] = 4;
        data[entity + 158] = 20;
        data[entity + 159] = 30;
        data[entity + 160] = 50;

        var save = OriginalSaveGame.Parse(data);
        var handyman = Assert.Single(save.Entities);

        Assert.Equal(76780, save.Tick);
        Assert.Equal((53, 11222, (byte)4), (handyman.Discontent, handyman.HiredTick16, handyman.RaiseRequests));
        Assert.Equal(((byte)20, (byte)30, (byte)50), handyman.HandymanShares);
    }

    [Fact]
    public void ReadsTheYearsVipsRatsAndTheLastVip()
    {
        var data = new byte[OriginalSaveGame.Length];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(OriginalSaveGame.AwardCounterOffset + 2), -2);
        data[OriginalSaveGame.AwardCounterOffset + 6] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(OriginalSaveGame.AwardCounterOffset + 21), 7);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(OriginalSaveGame.AwardCounterOffset + 25), 12);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(OriginalSaveGame.HospitalOffset + 499), 51237);

        var save = OriginalSaveGame.Parse(data);

        Assert.Equal((1, -2), save.VipsThisYear);
        Assert.Equal((7, 12), save.RatsThisYear);
        Assert.Equal(51237, save.Hospitals[0].VipArrivalTick);
    }

    [Fact]
    public void ReadsRatholesAndLitteredTiles()
    {
        var data = new byte[OriginalSaveGame.Length];
        var hospital = OriginalSaveGame.HospitalOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3242), 117);
        data[hospital + 3246] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3247), (42 * 128) + 46);
        data[hospital + 3249] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3250), (36 * 128) + 27);
        data[hospital + 3252] = 3;

        var read = OriginalSaveGame.Parse(data).Hospitals[0];

        Assert.Equal((117, 2), (read.LitteredTiles, read.RatholeCount));
        Assert.Equal((46, 42, 2), read.Rathole(0));
        Assert.Equal((27, 36, 3), read.Rathole(1));
    }

    [Fact]
    public void ReceptionistsAreNeverTired()
    {
        var install = RealGameData.Require();
        var folder = install.FindDirectory("SAVE");
        var files = folder is null
            ? []
            : Directory.EnumerateFiles(folder, "*.H*").Where(file => new FileInfo(file).Length == OriginalSaveGame.Length).ToList();
        if (files.Count == 0)
        {
            Assert.Skip("No original saves in the SAVE folder.");
        }

        // Byte 120 is tiredness, not energy: receptionists, who never tire, hold 0 there.
        var receptionists = files.SelectMany(file => OriginalSaveGame.Parse(File.ReadAllBytes(file)).Entities)
            .Where(e => e.Kind == 4 && e.Type == 3 && e.TileX != 0)
            .ToList();
        Assert.All(receptionists, receptionist => Assert.Equal(0, receptionist.Fatigue));
    }

    [Fact]
    public void BenchFramesAreBenchIdleAnimations()
    {
        var install = RealGameData.Require();
        var folder = install.FindDirectory("SAVE");
        var files = folder is null
            ? []
            : Directory.EnumerateFiles(folder, "*.H*").Where(file => new FileInfo(file).Length == OriginalSaveGame.Length).ToList();
        if (files.Count == 0)
        {
            Assert.Skip("No original saves in the SAVE folder.");
        }

        // CorsixTH bench.lua: idle animations 112 (north) and 114 (east).
        var animations = AnimationSet.Load(install);
        int[] benchFrames = [animations.FirstFrame(112), animations.FirstFrame(114)];
        var benches = files.SelectMany(file => OriginalSaveGame.Parse(File.ReadAllBytes(file)).Entities)
            .Where(e => e.Kind == 6 && e.Type == 4)
            .ToList();

        Assert.NotEmpty(benches);
        Assert.All(benches, bench => Assert.Contains(bench.Frame, benchFrames));

        // FULL00.SAM: #objects[4] (bench) StartStrength 10; furniture never wears.
        Assert.All(benches, bench => Assert.Equal(10, bench.Strength));
    }

    [Fact]
    public void ReadsTheComputerPlayersScenario()
    {
        var data = new byte[OriginalSaveGame.Length];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(OriginalSaveGame.TickOffset), 76780);
        var hospital = OriginalSaveGame.HospitalOffset + (2 * OriginalSaveGame.HospitalLength);
        data[hospital + 42] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 4600), 76877);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 4604), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 4606), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 4608), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 4612 + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 4612 + 8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 4612 + 10), 32);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 4660), 4200);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 4664), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 4672), 13);
        data[hospital + 4677] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 4678 + 2), 1500);
        data[hospital + 4694] = 1;
        var lists = OriginalSaveGame.ObjectListOffset + (2 * OriginalSaveGame.HospitalListLength);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(lists + (2 * 4)), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(lists + OriginalSaveGame.HospitalRoomListOffset + (2 * 7)), 1);

        var save = OriginalSaveGame.Parse(data);
        var opponent = save.Hospitals[2];

        Assert.Equal(76780, save.Tick);
        Assert.Equal(76877, opponent.CompetitorNextTick);
        Assert.Equal((0, 0, 0), opponent.CompetitorQueue[0]);
        Assert.Equal((1, 1, 32), opponent.CompetitorQueue[1]);
        Assert.Equal((3, 4, 1, false), opponent.CompetitorPending);
        Assert.Equal(4200, opponent.CompetitorWages);
        Assert.Equal(3, opponent.CompetitorRooms);
        Assert.Equal(13, opponent.CompetitorScenarioPosition);
        Assert.Equal(1, opponent.CompetitorNameNumber);
        Assert.Equal(1500, opponent.CompetitorSkillSum(1));
        Assert.True(opponent.IsOpen);
        Assert.False(save.Hospitals[1].IsOpen);
        Assert.Equal(6, save.HospitalObjects(2, 4));
        Assert.Equal(0, save.HospitalObjects(1, 4));
        Assert.Equal(1, save.HospitalRooms(2, 7));
    }

    [Fact]
    public void RealComputerPlayersAreInTheirScenario()
    {
        var install = RealGameData.Require();
        var folder = install.FindDirectory("SAVE");
        var files = folder is null
            ? []
            : Directory.EnumerateFiles(folder, "*.H*").Where(file => new FileInfo(file).Length == OriginalSaveGame.Length).ToList();
        if (files.Count == 0)
        {
            Assert.Skip("No original saves in the SAVE folder.");
        }

        // Positions 1 to 9 are the start (the number of the next record) and 12 to 15 the cycle; a computer player that
        // has opened is past the start's opening step (position 6), and the rooms it counts are those of its list less the type 4 count.
        var opponents = files.Select(file => OriginalSaveGame.Parse(File.ReadAllBytes(file)))
            .SelectMany(save => save.Hospitals.Select((h, player) => (save, h, player)).Where(p => p.h.Kind == 2))
            .ToList();
        Assert.NotEmpty(opponents);
        Assert.All(opponents, p =>
        {
            Assert.True(p.h.CompetitorScenarioPosition is >= 0 and <= 9 or >= 12 and <= 15, $"{p.h.Name}: {p.h.CompetitorScenarioPosition}");
            Assert.True(!p.h.IsOpen || p.h.CompetitorScenarioPosition > 6, $"{p.h.Name} open at {p.h.CompetitorScenarioPosition}");
            Assert.InRange(p.h.CompetitorPending.Kind, 0, 4);
            Assert.All(p.h.CompetitorQueue, task => Assert.InRange(task.Type, 0, 2));
            var rooms = Enumerable.Range(0, OriginalSaveGame.HospitalRoomTypes).Where(t => t != 4).Sum(t => p.save.HospitalRooms(p.player, t));
            Assert.Equal(p.h.CompetitorRooms, rooms);
        });
    }

    [Fact]
    public void RejectsWrongSize()
    {
        Assert.Throws<InvalidDataException>(() => OriginalSaveGame.Parse(new byte[OriginalSaveGame.Length - 1]));
    }

    [Fact]
    public void RealSavesAreConsistent()
    {
        var install = RealGameData.Require();
        var folder = install.FindDirectory("SAVE");
        var files = folder is null
            ? []
            : Directory.EnumerateFiles(folder, "*.H*").Where(file => new FileInfo(file).Length == OriginalSaveGame.Length).ToList();
        if (files.Count == 0)
        {
            Assert.Skip("No original saves in the SAVE folder.");
        }

        foreach (var file in files)
        {
            var save = OriginalSaveGame.Parse(File.ReadAllBytes(file));

            Assert.InRange(save.Level, 1, 12);
            Assert.Contains(save.Difficulty, new[] { "EASY", "FULL", "HARD" });
            Assert.InRange(save.Day, 1, 31);
            Assert.InRange(save.Month, 0, 11);
            Assert.InRange(save.Year, 0, 20);

            var local = save.Hospitals[0];
            Assert.Equal(1, local.Kind);
            Assert.NotEmpty(local.Name);
            Assert.Equal(local.Staff, local.Nurses + local.Doctors + local.Handymen + local.Receptionists);

            var bySlotAll = save.Entities.ToDictionary(e => e.Slot);

            // The chart history is recorded month by month, so it never runs ahead of today's totals.
            Assert.True(save.Chart(OriginalSaveChart.Cures).Max() <= local.PeopleCured);

            // Following an object type's list from its first object visits objects of that type.
            var listed = 0;
            var wrongType = 0;
            for (var type = 1; type < OriginalSaveGame.ObjectTypes; type++)
            {
                var seen = new HashSet<int>();
                for (var objectSlot = save.FirstObjectOfType(type); objectSlot != 0 && seen.Add(objectSlot) && bySlotAll.TryGetValue(objectSlot, out var item); objectSlot = item.NextOfType)
                {
                    listed++;
                    wrongType += item.Type == type ? 0 : 1;
                }
            }

            // A fresh level carries a few stale entries over, so only judge saves with real furniture.
            Assert.True(listed < 20 || wrongType * 10 <= listed, $"{wrongType} of {listed} listed objects have another type");

            // Patients carry a disease index of the casebook (it may not be discovered yet; 0 is seen on a few visitors).
            Assert.All(save.Entities.Where(e => e.Kind == 2), p => Assert.InRange(p.Disease, 0, OriginalSaveHospital.DiseaseCount - 1));

            // People standing inside a room point at it; people in corridors point nowhere.
            var people = save.Entities.Where(e => e.Kind is 2 or 4 && e.TileX != 0).ToList();
            var placed = people.Count(person =>
            {
                var room = save.Rooms.FirstOrDefault(r => r.Width > 1
                    && person.TileX >= r.Origin.X && person.TileX < r.Origin.X + r.Width
                    && person.TileY >= r.Origin.Y && person.TileY < r.Origin.Y + r.Height);
                return person.RoomIndexPlusOne == (room is null ? 0 : room.Index + 1);
            });
            Assert.True(placed * 10 >= people.Count * 9, $"{placed} of {people.Count} people point at the room they stand in");

            // Staff listed by a room are staff standing in that room or close by.
            foreach (var room in save.Rooms.Where(r => r.Width > 1))
            {
                foreach (var staffSlot in room.Staff)
                {
                    Assert.True(bySlotAll.TryGetValue(staffSlot, out var member) && member.Kind == 4, $"room {room.Index} lists slot {staffSlot}, not staff");
                }
            }

            // Rooms lie on the map with their door on their edge and the outside tile next to it.
            foreach (var room in save.Rooms.Where(r => r.Width > 1))
            {
                Assert.InRange(room.Door.X, room.Origin.X, room.Origin.X + room.Width - 1);
                Assert.InRange(room.Door.Y, room.Origin.Y, room.Origin.Y + room.Height - 1);
                Assert.Equal(1, Math.Abs(room.Door.X - room.Outside.X) + Math.Abs(room.Door.Y - room.Outside.Y));
            }

            // Every staff member on the payroll has an entity (someone fired and still walking out has one too),
            // and tiles point at entities standing on them.
            Assert.True(save.Entities.Count(e => e.Kind == 4 && e.TileX != 0) >= local.Staff);
            var bySlot = save.Entities.ToDictionary(e => e.Slot);
            for (var y = 0; y < OriginalSaveGame.MapSize; y++)
            {
                for (var x = 0; x < OriginalSaveGame.MapSize; x++)
                {
                    if (save[x, y].Entity is var slot and not 0)
                    {
                        for (var entity = bySlot[slot]; ; entity = bySlot[entity.NextOnTile])
                        {
                            Assert.Equal((x, y), (entity.TileX, entity.TileY));
                            if (entity.NextOnTile == 0)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
