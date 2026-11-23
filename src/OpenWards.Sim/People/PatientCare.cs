// Ported from CorsixTH CorsixTH/Lua/entities/humanoids/patient.lua (Patient:setDisease, completeDiagnosticStep,
// modifyDiagnosisProgress, getTreatmentDiseaseId, treatDisease, isTreatmentEffective), CorsixTH/Lua/room.lua
// (Room:dealtWithPatient, getStaffServiceQuality), CorsixTH/Lua/rooms/gp.lua (GPRoom:dealtWithPatient,
// sendPatientToNextDiagnosisRoom), CorsixTH/Lua/hospital.lua (Hospital:receiveMoneyForTreatment, paySupplierForDrug,
// updateCuredCounts, humanoidDeath, the default policies), CorsixTH/Lua/entities/humanoids/staff.lua
// (Staff:getServiceQuality) and CorsixTH/Lua/world.lua (World:getNewPatientDisease). Who is insured, whether a
// patient agrees to a price and how they pay are not CorsixTH's: they follow the community's description of the
// original game (the game logic spec's price and payment section).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.
//
// Diagnosis steps, guessing and the outcome of a treatment follow the community specification of the original game
// (the "Diagnosis" and "Treatment" sections), not CorsixTH.

using OpenWards.Sim.Disasters;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Staff;

namespace OpenWards.Sim.People;

/// <summary>
/// Diseases, diagnosis and payment: which disease a new patient comes with, how far the GP gets in
/// diagnosing it, where the patient goes next, and what they pay.
/// </summary>
/// <remarks>
/// Diagnosis progress runs from 0 to 1000, and the disease is diagnosed at 1000. Each step at the GP's or in a
/// diagnosis room adds the doctor's score plus the room's bonus for the disease (<see cref="DiagnosisBonuses"/>) less
/// the patient's difficulty, when that is above 0. A treatment room always cures, a drug cures with the triangular
/// chance of its effectiveness; neither depends on the doctor or on how sure the diagnosis is, only a wrong guess
/// kills.
/// </remarks>
public static class PatientCare
{
    /// <summary>Diagnosis progress in thousandths that stands for a complete diagnosis.</summary>
    public const int FullDiagnosis = 1000;

    /// <summary>Reputation points of a complete diagnosis.</summary>
    public const int FullDiagnosisPoints = 1;

    /// <summary>Reputation points of a cure.</summary>
    public const int CurePoints = 1;

    /// <summary>Reputation points of a death.</summary>
    public const int DeathPoints = -1;

    /// <summary>A patient's full health, in thousandths.</summary>
    public const int FullHealth = PatientNeeds.MaxHealth;

    /// <summary>Health in thousandths that a diagnosis room with a negative bonus for the disease costs.</summary>
    public const int WrongRoomHealthLoss = 5;

    /// <summary>
    /// <c>World:getNewPatientDisease</c>: one of the level's diseases at random; if it may not come yet, one at
    /// random of those that may. Null when none may.
    /// </summary>
    public static DiseaseDefinition? NewPatientDisease(Simulation simulation, Hospital hospital)
    {
        var rules = simulation.Rules.Diseases;
        var available = rules.AvailableDiseases().ToList();
        if (available.Count == 0)
        {
            return null;
        }

        var month = simulation.Date.MonthOfGame;
        var disease = available[simulation.Random.Next(available.Count)];
        if (rules.IsUsableForNewPatient(disease, month, hospital.Visitors))
        {
            return disease;
        }

        var usable = available.Where(d => rules.IsUsableForNewPatient(d, month, hospital.Visitors)).ToList();
        return usable.Count == 0 ? null : usable[simulation.Random.Next(usable.Count)];
    }

    /// <summary>
    /// One patient in how many is insured on the campaign level: a third on levels 1 and 2, a tenth on level 3, a fifth
    /// on the others, as in the original game (the level files do not set it).
    /// </summary>
    public static int InsuredOneIn(int? levelNumber) => levelNumber switch
    {
        <= 2 => 3,
        3 => 10,
        _ => 5,
    };

    /// <summary>The second half of <c>Patient:setDisease</c>: whether the patient is insured (<see cref="InsuredOneIn"/>), and by which of the hospital's three companies.</summary>
    internal static int ChooseInsuranceCompany(Simulation simulation)
    {
        var random = simulation.Random;
        return random.Next(InsuredOneIn(simulation.Rules.LevelNumber)) == 0 ? random.NextInclusive(1, Hospital.InsuranceCompanies) : 0;
    }

    /// <summary>The money a new patient brings: 5,000 + rng % 95,000 (not in CorsixTH).</summary>
    internal static int NewPatientMoney(Simulation simulation) =>
        PatientBehaviour.MinWallet + simulation.MoneyRandom.Next(PatientBehaviour.WalletRange);

    /// <summary>
    /// Whether the patient agrees to pay for the next diagnosis or treatment, which depends only on the price, their
    /// money and their insurance: with P the price and E the price they expect (<see cref="Hospital.TreatmentPrice"/>,
    /// <see cref="Hospital.ExpectedPrice"/>), an insured patient expects E + ⌊E × their company's bonus / 100⌋, one
    /// paying themselves with less money than P expects 0, and they agree if P ≤ E + rng % (⌊P / 2⌋ + 1). If they
    /// agree, they will pay P. Unlike CorsixTH's chance of e^(−4 × overprice), a patient may refuse a raised price
    /// less often (10% over: about 82% agree) and one short of money refuses even the standard price.
    /// </summary>
    internal static bool AgreesToPay(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var rules = simulation.Rules.Diseases;
        var price = hospital.TreatmentPrice(disease, rules);
        var agrees = Agrees(simulation.Random, price, hospital.ExpectedPrice(disease, rules), patient.Wallet, InsurerOf(hospital, patient));
        if (agrees)
        {
            patient.PayAmount = price;
        }

        return agrees;
    }

    /// <summary>The rule of <see cref="AgreesToPay"/> for a price, an expected price, the patient's money and their company (1 to 8), or 0.</summary>
    public static bool Agrees(SimRandom random, int price, int expected, int wallet, int insurer)
    {
        long expects = expected;
        if (insurer > 0)
        {
            // The original game takes the bonus of the next of the hospital's companies here, and none for the third;
            // OpenWards takes the patient's own company's.
            expects += expects * Hospital.InsurerPriceBonus(insurer) / 100;
        }
        else if (wallet < price)
        {
            expects = 0;
        }

        // Within what they expect they always agree, so no number needs drawing.
        return price <= expects || price <= expects + random.Next((price / 2) + 1);
    }

    /// <summary>The patient's insurance company (1 to 8), or 0 when they pay themselves.</summary>
    private static int InsurerOf(Hospital hospital, PatientBehaviour patient) =>
        patient.InsuranceCompany is >= 1 and <= Hospital.InsuranceCompanies ? hospital.Insurers[patient.InsuranceCompany - 1] : 0;

    /// <summary>
    /// <c>Room:dealtWithPatient</c> (<c>GPRoom:dealtWithPatient</c> in the GP's office): what the patient pays and does
    /// next. After a diagnosis room an undiagnosed patient gets further in the diagnosis, pays and goes back to the GP;
    /// a diagnosed one goes on to the next room of the treatment, or after the last is treated.
    /// </summary>
    internal static void DealtWith(Simulation simulation, Room room, Humanoid humanoid, PatientBehaviour patient)
    {
        GainProcedureSkill(simulation, room);
        if (string.Equals(room.RoomType, PatientBehaviour.FirstRoom, StringComparison.Ordinal))
        {
            GpDealtWith(simulation, room, humanoid, patient);
            return;
        }

        if (patient.Disease is not { } disease)
        {
            patient.SetNextGoHome();
            return;
        }

        if (!patient.IsDiagnosed)
        {
            CompleteDiagnosticStep(simulation, room, humanoid, patient, disease);
            ReceiveMoneyForTreatment(simulation, humanoid, patient, room.RoomType);
            if (DiseaseCatalog.Find(DiseaseCatalog.GpDiagnosis) is { } consultation && !AgreesToPay(simulation, humanoid, patient, consultation))
            {
                NotCured(simulation, humanoid, patient, ReputationChange.OverPriced);
                patient.SetNextGoHome();
            }
            else
            {
                patient.SetNextGp();
            }

            return;
        }

        patient.CureRoomsVisited++;
        if (patient.CureRoomsVisited < disease.TreatmentRooms.Count)
        {
            patient.SetNextTreatment();
        }
        else
        {
            TreatDisease(simulation, room, humanoid, patient, disease);
        }
    }

    /// <summary>
    /// The treatment's last room is done (the community specification's "Treatment"; CorsixTH's
    /// <c>Patient:treatDisease</c>). The patient pays. A treatment room always cures; a drug cures when
    /// <c>rnd(0, 99)</c> is below its effectiveness, and is paid for either way. A failed drug costs
    /// <c>1000 − 10 × effectiveness</c> health and sends the patient back to the GP; for a special disease
    /// (<see cref="DiseaseDefinition.IsSpecial"/>), an emergency patient or after a wrong guess it costs 1000, as any
    /// treatment after a wrong guess does. Health changes count once the patient is out of the room; a patient left below 1 dies.
    /// </summary>
    /// <remarks>
    /// CorsixTH cures with a chance from the cure's effectiveness, the diagnosis progress and the staff's quality, for
    /// every treatment, and a failed treatment always kills.
    /// </remarks>
    internal static void TreatDisease(Simulation simulation, Room room, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var entry = hospital.CaseOf(disease, simulation.Rules.Diseases);
        ReceiveMoneyForTreatment(simulation, humanoid, patient, room.RoomType);
        var isDrug = string.Equals(room.RoomType, RoomVisits.PharmacyRoom, StringComparison.Ordinal);
        bool cured;
        var healthLoss = FullHealth;
        if (isDrug)
        {
            cured = !patient.IsWrongGuess && simulation.Random.Triangular(0, 99) < entry.CureEffectiveness;

            // A special disease's drug, an emergency's or one after a wrong guess kills when it fails.
            if (!patient.IsWrongGuess && !disease.IsSpecial && patient.EmergencyIndex == 0)
            {
                healthLoss = FullHealth - (10 * entry.CureEffectiveness);
            }

            hospital.PayForDrug(simulation.Hours, entry, simulation.Random.NextInclusive(1, 5));
        }
        else
        {
            cured = !patient.IsWrongGuess;
        }

        if (cured)
        {
            // Patient:cure and goHome("cured") with Hospital:updateCuredCounts.
            CountForReputation(simulation, humanoid, patient, CurePoints);
            hospital.Cured++;
            hospital.CuredThisYear++;
            entry.Recoveries++;
            patient.Cure();
            patient.AddToTreatmentHistory(new TreatmentHistoryEntry(TreatmentHistoryKind.Cured, string.Empty));
            patient.SetNextGoHome();
        }
        else
        {
            patient.PendingHealthChange -= healthLoss;
            if (patient.Health + patient.PendingHealthChange < 1)
            {
                // Patient:die with Hospital:humanoidDeath.
                RecordDeath(simulation, humanoid, patient);
                patient.SetNextDie();
            }
            else
            {
                // The drug did not work: back to the GP, who sends the patient on to the treatment again.
                patient.CureRoomsVisited = 0;
                patient.SetNextGp();
                return;
            }
        }

        if (patient.EmergencyIndex > 0)
        {
            Emergencies.PatientDone(simulation, hospital, patient.IsCured);
        }
    }

    /// <summary>
    /// <c>GPRoom:dealtWithPatient</c>: the GP is done. The patient pays for the consultation and gets further in
    /// the diagnosis; once diagnosed they go on to treatment, otherwise to another diagnosis room. What they do
    /// after leaving the room is decided here, while they are still in it.
    /// </summary>
    internal static void GpDealtWith(Simulation simulation, Room room, Humanoid humanoid, PatientBehaviour patient)
    {
        if (patient.Disease is not { } disease || patient.IsCured)
        {
            // CorsixTH lets such a patient meander about; they only come in debugging, so here they go home.
            patient.SetNextGoHome();
            return;
        }

        if (patient.IsDiagnosed)
        {
            // Back after a drug that did not work: on to the treatment again, if they agree to pay for it.
            if (AgreesToPay(simulation, humanoid, patient, disease))
            {
                patient.SetNextTreatment();
            }
            else
            {
                NotCured(simulation, humanoid, patient, ReputationChange.OverPriced);
                patient.SetNextGoHome();
            }

            return;
        }

        ReceiveMoneyForTreatment(simulation, humanoid, patient, room.RoomType);
        CompleteDiagnosticStep(simulation, room, humanoid, patient, disease);
        if (patient.DiagnosisProgress >= FullDiagnosis)
        {
            SetDiagnosed(simulation, humanoid, patient, disease);
            if (AgreesToPay(simulation, humanoid, patient, disease))
            {
                patient.SetNextTreatment();
            }
            else
            {
                NotCured(simulation, humanoid, patient, ReputationChange.OverPriced);
                patient.SetNextGoHome();
            }

            return;
        }

        // GPRoom:sendPatientToNextDiagnosisRoom.
        var rooms = patient.AvailableDiagnosisRooms;
        if (rooms.Count == 0)
        {
            // No diagnosis room left: once out, the hospital's policy decides (PatientBehaviour.NoDiagnosisRoomFound).
            patient.SetNextDiagnosis(-1);
            return;
        }

        var index = simulation.Random.Next(rooms.Count);
        var next = DiseaseCatalog.Find(DiseaseCatalog.DiagnosisPrefix + rooms[index]);
        if (next is null || AgreesToPay(simulation, humanoid, patient, next))
        {
            patient.SetNextDiagnosis(index);
        }
        else
        {
            NotCured(simulation, humanoid, patient, ReputationChange.OverPriced);
            patient.SetNextGoHome();
        }
    }

    /// <summary>
    /// <c>Hospital:updateNotCuredCounts</c> from <c>Patient:goHome</c>: the patient goes home without a cure, sent home or
    /// because of the price, which changes the reputation for their disease.
    /// </summary>
    /// <summary><c>Hospital:humanoidDeath</c>: the death counts, the disease's fatalities and the reputation.</summary>
    internal static void RecordDeath(Simulation simulation, Humanoid humanoid, PatientBehaviour patient)
    {
        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        if (patient.Disease is { } disease)
        {
            hospital.CaseOf(disease, simulation.Rules.Diseases).Fatalities++;
        }

        hospital.Deaths++;
        hospital.DeathsThisYear++;
        CountForReputation(simulation, humanoid, patient, DeathPoints);
    }

    /// <summary>
    /// Counts a patient's outcome in the reputation (<see cref="Hospital.CountPatient"/>): their visit, if it has not
    /// counted yet, and the outcome's points. Unlike CorsixTH's fixed amounts (+1 a cure, −4 a death, −3 sent home, −2 the
    /// price) the reputation is the average of these, as the community describes the original game.
    /// </summary>
    internal static void CountForReputation(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, int points)
    {
        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var entry = patient.Disease is { } disease ? hospital.CaseOf(disease, simulation.Rules.Diseases) : null;
        var visit = !patient.IsCountedInReputation;
        patient.IsCountedInReputation = true;
        hospital.CountPatient(entry, visit, points);
    }

    /// <summary>
    /// Sets the patient's diagnosis progress; reaching a complete diagnosis (<see cref="FullDiagnosis"/>) for the first
    /// time adds <see cref="FullDiagnosisPoints"/> to the reputation.
    /// </summary>
    internal static void SetDiagnosisProgress(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, int progress)
    {
        var before = patient.DiagnosisProgress;
        patient.DiagnosisProgress = progress;
        if (before < FullDiagnosis && progress >= FullDiagnosis)
        {
            CountForReputation(simulation, humanoid, patient, FullDiagnosisPoints);
        }
    }

    /// <summary>The policies of the patient's hospital.</summary>
    internal static HospitalPolicies Policies(Simulation simulation, Humanoid humanoid) => simulation.Hospitals[humanoid.HospitalPlayer].Policies;

    internal static void NotCured(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, ReputationChange reason)
    {
        if (patient.Disease is not { } disease)
        {
            return;
        }

        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        hospital.UpdateNotCuredCounts(reason, hospital.CaseOf(disease, simulation.Rules.Diseases));
        CountForReputation(simulation, humanoid, patient, 0);

        // Though not killed, this lets the emergency's watch end early.
        if (patient.EmergencyIndex > 0)
        {
            Emergencies.PatientDone(simulation, hospital, cured: false);
        }
    }

    /// <summary>Marks the patient diagnosed; the hospital now knows the disease (<c>Patient:setDiagnosed</c>, <c>discoverDisease</c>).</summary>
    internal static void SetDiagnosed(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        patient.IsDiagnosed = true;
        patient.AddToTreatmentHistory(new TreatmentHistoryEntry(TreatmentHistoryKind.Diagnosis, disease.Id));
        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var entry = hospital.CaseOf(disease, simulation.Rules.Diseases);
        if (!entry.Discovered)
        {
            entry.Discovered = true;
            hospital.AdviseDiscoverDisease(simulation, disease.Id);
            hospital.Research.OnDiseaseDiscovered(simulation, hospital, disease);
        }
    }

    /// <summary>Whether the hospital knows the patient's disease, so that it may be guessed.</summary>
    internal static bool IsDiscovered(Simulation simulation, Humanoid humanoid, DiseaseDefinition disease)
    {
        return simulation.Hospitals[humanoid.HospitalPlayer].CaseOf(disease, simulation.Rules.Diseases).Discovered;
    }

    /// <summary>
    /// <c>Hospital:receiveMoneyForTreatment</c>: the patient pays what they agreed to for the disease, or before the
    /// diagnosis for the room they are in, or else its current price. An insured patient's company owes it; one paying
    /// themselves pays from their money, and if it falls short, as in the original game, the procedure goes unpaid and
    /// the patient carries on.
    /// </summary>
    internal static void ReceiveMoneyForTreatment(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, string roomType)
    {
        // Nothing at all in free build mode, not even the patient's view of the price.
        if (simulation.Rules.FreeBuild)
        {
            return;
        }

        var id = patient.IsDiagnosed ? patient.DiseaseId : DiseaseCatalog.DiagnosisPrefix + roomType;
        if (DiseaseCatalog.Find(id) is not { } paidFor)
        {
            return;
        }

        var hospital = simulation.Hospitals[humanoid.HospitalPlayer];
        var rules = simulation.Rules.Diseases;
        var amount = patient.PayAmount > 0 ? patient.PayAmount : hospital.TreatmentPrice(paidFor, rules);
        var entry = hospital.CaseOf(paidFor, rules);
        patient.PayAmount = 0;
        if (patient.InsuranceCompany == 0)
        {
            if (patient.Wallet < amount)
            {
                return;
            }

            patient.Wallet -= amount;
        }

        hospital.ReceiveForTreatment(simulation.Hours, entry, amount, patient.InsuranceCompany);
        simulation.ShowFloatingDollarSign(humanoid, amount, patient.InsuranceCompany != 0);
    }

    /// <summary>
    /// A diagnosis step at the GP's or in a diagnosis room (the community specification's "Diagnosis"): progress grows by
    /// <c>doctor's score + room's bonus − difficulty</c> when that is above 0, up to <see cref="FullDiagnosis"/>. The
    /// patient's difficulty is rolled at the first step as <c>rnd(1, MaxDiagDiff)</c>. A room with a negative bonus for
    /// the disease costs the patient 5 health.
    /// </summary>
    /// <remarks>
    /// CorsixTH adds a base that shrinks with the disease's difficulty and a bonus from the doctor's skill and attention
    /// to detail, and lets the stop procedure policy take progress above 1000; here that policy no longer matters.
    /// </remarks>
    private static void CompleteDiagnosticStep(Simulation simulation, Room room, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        if (patient.DiagnosisDifficulty < 0)
        {
            var maxDifficulty = simulation.Rules.Diseases.MaxDiagDiff(disease.ExpertiseId);
            patient.DiagnosisDifficulty = maxDifficulty >= 1 ? simulation.Random.Triangular(1, maxDifficulty) : 0;
        }

        var bonus = string.Equals(room.RoomType, PatientBehaviour.FirstRoom, StringComparison.Ordinal) ? 0 : DiagnosisBonuses.Bonus(disease.Id, room.RoomType);
        var staff = room.StaffPresent ? simulation.FindEntity(room.StaffId) as Humanoid : null;
        var gain = DoctorScore(simulation, staff) + bonus - patient.DiagnosisDifficulty;
        if (gain > 0)
        {
            SetDiagnosisProgress(simulation, humanoid, patient, Math.Min(patient.DiagnosisProgress + gain, FullDiagnosis));
        }

        if (bonus < 0)
        {
            patient.PendingHealthChange -= WrongRoomHealthLoss;
        }
    }

    /// <summary>
    /// The doctor's score in a diagnosis step: skill (0 to 1000) <c>+ 250 − rnd(0, 500)</c>, +200 for a consultant, −100
    /// for a junior, +100 when not tired (fatigue below the level's <c>NotTired</c>), −100 when tired (at or above
    /// <c>Tired</c>). Without a doctor only the random part counts.
    /// </summary>
    /// <remarks>The specification adds a correction from the room that it leaves open; it is 0 here.</remarks>
    internal static int DoctorScore(Simulation simulation, Humanoid? staff)
    {
        var score = 250 - simulation.Random.Triangular(0, 500);
        if (staff is not { Profile: { } profile })
        {
            return score;
        }

        score += profile.Skill;
        if (profile.IsConsultant)
        {
            score += 200;
        }
        else if (profile.IsJunior)
        {
            score -= 100;
        }

        var tiredness = simulation.Rules.Tiredness;
        var fatigue = (int)decimal.Floor(staff.Fatigue * 1000);
        if (fatigue >= tiredness.Tired)
        {
            score -= 100;
        }
        else if (fatigue < tiredness.NotTired)
        {
            score += 100;
        }

        return score;
    }

    /// <summary>
    /// A doctor who finishes any procedure gains 1 skill, within 30 to 1000 (the community specification's experience
    /// rule; CorsixTH lets doctors learn by the hour of work instead).
    /// </summary>
    private static void GainProcedureSkill(Simulation simulation, Room room)
    {
        if (simulation.FindEntity(room.StaffId) is Humanoid { Profile: { Class: StaffClass.Doctor } profile })
        {
            profile.GainProcedureSkill(simulation.Rules.Staff);
        }
    }

    /// <summary>
    /// A guessed diagnosis (the hospital's guess cure policy or the player's guess): the patient counts as diagnosed, but
    /// the guess is wrong when <c>rng % 100 &gt; min(p, 99)</c> with <c>p = progress × 100 / 1000</c>, which makes any
    /// treatment kill them.
    /// </summary>
    internal static void GuessDiagnosis(Simulation simulation, Humanoid humanoid, PatientBehaviour patient, DiseaseDefinition disease)
    {
        var percent = patient.DiagnosisProgress * 100 / FullDiagnosis;
        patient.IsWrongGuess = simulation.Random.Next(100) > Math.Min(percent, 99);
        SetDiagnosed(simulation, humanoid, patient, disease);
    }
}
