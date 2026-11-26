// Firing staff, ported from CorsixTH CorsixTH/Lua/entities/humanoids/staff.lua (Staff:fire, despawn). Promotion, pay,
// discontent and requests for money follow the community's logic specification for OpenWards instead of CorsixTH's
// Staff:tick, requestRaise and StaffProfile:getRiseAmount.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;

namespace OpenWards.Sim.Staff;

/// <summary>Why a member of staff asks for money, which decides what agreeing gives them.</summary>
public enum RaiseReason : byte
{
    /// <summary>No request.</summary>
    None = 0,

    /// <summary>Paid below the expected wage: asks for the shortfall and a tenth of the expected wage more, as a rise.</summary>
    Underpaid = 1,

    /// <summary>Very tired, asking for a rise.</summary>
    TiredRise = 2,

    /// <summary>Very tired, asking for a one-off payment.</summary>
    TiredPayment = 3,

    /// <summary>Unhappy for some other reason, asking for a one-off payment.</summary>
    Payment = 4,

    /// <summary>The sixth request or later: nothing will keep them, agreeing is as good as refusing.</summary>
    DefiniteQuit = 5,

    /// <summary>Another hospital (the network game's poaching) offers double the wage.</summary>
    Poached = 6,
}

/// <summary>
/// Staff discontent and their requests for money, as the community's specification describes the original game. Every
/// <see cref="PatientNeeds.ModifyFreq"/> ticks a member of staff's discontent grows by 1 while their mood is below
/// <c>gbv.Unhappy</c> and falls by 1 while it is above <c>gbv.Happy</c>; once it is above <c>gbv.ResignMax</c> and they
/// have worked six months or so, they ask for money, and wait 30 days for an answer. Promotion and the mood that pay
/// brings are worked out at the end of every month. Fired staff get a month's wage and walk off the map; those who quit
/// get nothing.
/// </summary>
internal static class StaffEmployment
{
    /// <summary>Ticks in the specification's fixed month of 30 days.</summary>
    internal const int Month = 1500;

    /// <summary>Ticks a request for money waits for an answer: 30 days.</summary>
    internal const int QuittingDelay = Month;

    /// <summary>From the sixth request on, nothing will keep the member of staff.</summary>
    internal const int DefiniteQuitRequest = 6;

    /// <summary>One hour of a member of staff (<c>Staff:tick</c>, the checks after <c>Entity.tick</c>).</summary>
    internal static void Tick(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.IsLeavingForPoacher)
        {
            if (!IsBusyWithPatient(simulation, humanoid))
            {
                JoinPoacher(simulation, humanoid);
            }

            return;
        }

        Tiredness.CheckIfNeedRest(simulation, humanoid);
        CheckIfWaitedTooLongForRaise(simulation, humanoid);
    }

    /// <summary>
    /// A member of staff's part of the update every <see cref="PatientNeeds.ModifyFreq"/> ticks: discontent follows the
    /// mood, and enough of it makes them ask for money.
    /// </summary>
    internal static void Modify(Simulation simulation, Humanoid humanoid)
    {
        var rules = simulation.Rules.Staff.Employment;
        var mood = PatientNeeds.Mood(humanoid);
        if (mood < rules.Unhappy)
        {
            humanoid.Discontent++;
        }
        else if (mood > rules.Happy)
        {
            humanoid.Discontent = Math.Max(0, humanoid.Discontent - 1);
        }

        // The length of service asked for is drawn again at every check: six to nine months, mostly about six.
        if (humanoid.Discontent > rules.ResignMax && !humanoid.HasRaiseRequest
            && !Cheats.SalaryRaiseDisabled(simulation, simulation.Hospitals[humanoid.Employer])
            && simulation.Hours - humanoid.HiredAt > (6 + simulation.Random.Next(4)) * (long)Month)
        {
            RequestRaise(simulation, humanoid);
        }
    }

    /// <summary>
    /// The end of a month for every member of staff: a junior doctor skilled enough and hired at least
    /// <c>gbv.PromoDoc</c> months ago becomes a doctor, a doctor skilled enough and hired at least <c>gbv.PromoCon</c>
    /// months ago a consultant; then pay below the expected wage (<see cref="StaffProfile.FairWage"/>) changes the mood by
    /// <c>gbv.SalaryTooLow</c>, otherwise by <c>gbv.SalaryTooHigh</c>.
    /// </summary>
    internal static void OnEndMonth(Simulation simulation)
    {
        var staffRules = simulation.Rules.Staff;
        var rules = staffRules.Employment;
        foreach (var entity in simulation.Entities)
        {
            if (entity is not Humanoid { Profile: { } profile, IsFired: false, IsDespawning: false } humanoid
                || humanoid.Employer < 0 || humanoid.Employer >= simulation.Hospitals.Count)
            {
                continue;
            }

            var served = simulation.Hours - humanoid.HiredAt;
            if (profile.Class == StaffClass.Doctor)
            {
                if (profile.IsJunior && profile.Skill > staffRules.DoctorThreshold && served >= rules.PromoDoc * (long)Month)
                {
                    profile.Promote();
                }
                else if (!profile.IsJunior && !profile.IsConsultant && profile.Skill > staffRules.ConsultantThreshold && served >= rules.PromoCon * (long)Month)
                {
                    profile.Promote();
                    Training.OnPromotedToConsultant(simulation, humanoid);
                }
            }

            var points = profile.Wage < profile.FairWage(staffRules) ? rules.SalaryTooLow : rules.SalaryTooHigh;
            ChangeMood(simulation, humanoid, points);
        }
    }

    /// <summary>The mood in points from 0 to 100, through <see cref="Feelings.ChangeHappiness"/>.</summary>
    private static void ChangeMood(Simulation simulation, Humanoid humanoid, int points) =>
        Feelings.ChangeHappiness(simulation, humanoid, points / (decimal)PatientNeeds.MaxPoints);

    /// <summary>
    /// The player's answer to a request for money. Agreeing pays it, as a rise or a one-off payment, and makes the
    /// member of staff fully happy with no discontent left. Refusing, or agreeing to someone nothing will keep, lets them
    /// stay with a chance of 1 in (number of requests + 1), with no discontent left; otherwise they quit without
    /// severance. A poached member of staff refused joins the poacher.
    /// </summary>
    internal static void AnswerRaise(Simulation simulation, Humanoid humanoid, bool grant)
    {
        if (!humanoid.HasRaiseRequest)
        {
            return;
        }

        if (grant && humanoid.RaiseReason != RaiseReason.DefiniteQuit)
        {
            Agree(simulation, humanoid);
        }
        else if (humanoid.PoachedBy >= 0)
        {
            // The poached member of staff's demand not met: they resign and join the poacher's hospital.
            ClearRequest(humanoid);
            humanoid.IsLeavingForPoacher = true;
        }
        else
        {
            Refuse(simulation, humanoid);
        }
    }

    /// <summary>Whether the request is for a one-off payment rather than a rise.</summary>
    internal static bool IsOneOffPayment(RaiseReason reason) => reason is RaiseReason.TiredPayment or RaiseReason.Payment;

    /// <summary>Agreeing: the rise or payment, a full mood and no discontent.</summary>
    private static void Agree(Simulation simulation, Humanoid humanoid)
    {
        var profile = humanoid.Profile!;
        if (IsOneOffPayment(humanoid.RaiseReason))
        {
            simulation.Hospitals[humanoid.Employer].Spend(simulation.Hours, humanoid.RaiseRequest, TransactionKind.PersonalBonus, profile.Class.ToString());
        }
        else
        {
            var maxSalary = simulation.Rules.MaxSalary;
            profile.Wage = Math.Min(profile.Wage + humanoid.RaiseRequest, Math.Max(maxSalary, profile.Wage));
        }

        ClearRequest(humanoid);
        humanoid.PoachedBy = -1;
        humanoid.Discontent = 0;
        humanoid.Happiness = 1m;
    }

    /// <summary>Refusing: they stay with a chance of 1 in (requests + 1), otherwise quit.</summary>
    private static void Refuse(Simulation simulation, Humanoid humanoid)
    {
        ClearRequest(humanoid);
        if (simulation.Random.Next(humanoid.RaiseRequestCount + 1) == 0)
        {
            humanoid.Discontent = 0;
            return;
        }

        Quit(simulation, humanoid);
    }

    private static void ClearRequest(Humanoid humanoid)
    {
        humanoid.RaiseRequest = 0;
        humanoid.RaiseReason = RaiseReason.None;
        humanoid.QuittingIn = -1;
    }

    /// <summary>
    /// The network game's poaching: another player may tempt a member of staff whose discontent has reached a quarter
    /// of <c>gbv.ResignMax</c>. Their employer is then asked for double the wage at once, with no length of service
    /// needed and whatever the number of requests; refusing sends them to the poacher.
    /// </summary>
    /// <returns>Whether the member of staff listened.</returns>
    internal static bool Poach(Simulation simulation, Humanoid humanoid, int player)
    {
        if (humanoid.Profile is not { } profile || humanoid.IsFired || humanoid.IsDespawning || humanoid.Employer < 0
            || humanoid.Employer == player || player < 0 || player >= simulation.Hospitals.Count
            || humanoid.HasRaiseRequest || humanoid.PoachedBy >= 0 || humanoid.IsLeavingForPoacher)
        {
            return false;
        }

        if (humanoid.Discontent < simulation.Rules.Staff.Employment.ResignMax / 4)
        {
            return false;
        }

        // Double the wage, which the original's dialog promises (the original adds twice the wage, tripling it).
        humanoid.PoachedBy = player;
        humanoid.RaiseReason = RaiseReason.Poached;
        humanoid.RaiseRequest = Math.Max(1, profile.Wage);
        humanoid.QuittingIn = QuittingDelay;
        return true;
    }

    /// <summary>
    /// The poached member of staff resigns and joins the poacher automatically: off their job, at their wage, employed
    /// by the poacher and put in the poacher's hospital, on the first free tile of the poacher's land outside rooms.
    /// </summary>
    internal static void JoinPoacher(Simulation simulation, Humanoid humanoid)
    {
        var poacher = humanoid.PoachedBy;
        if (poacher < 0 || poacher >= simulation.Hospitals.Count || humanoid.Profile is not { } profile)
        {
            humanoid.IsLeavingForPoacher = false;
            humanoid.PoachedBy = -1;
            return;
        }

        Detach(simulation, humanoid);
        humanoid.IsLeavingForPoacher = false;
        humanoid.PoachedBy = -1;
        ClearRequest(humanoid);
        humanoid.Discontent = 0;
        humanoid.HiredAt = simulation.Hours;
        humanoid.Employer = poacher;
        humanoid.HospitalPlayer = poacher;
        humanoid.Behaviour = StaffHiring.NewBehaviour(profile.Class);
        if (PoacherTile(simulation, poacher) is { } tile)
        {
            humanoid.PlaceAt(tile);
        }
    }

    /// <summary>The first passable tile of the player's land outside rooms, row by row, or null.</summary>
    private static Maps.TilePosition? PoacherTile(Simulation simulation, int player)
    {
        var map = simulation.Map;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var tile = map[x, y];
                if (tile.Parcel != 0 && map.GetParcelOwner(tile.Parcel) == player + 1 && tile.RoomId == 0 && tile.Has(Maps.TileFlags.Passable))
                {
                    return new Maps.TilePosition(x, y);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// <c>Staff:fire</c>: a month's wage as severance, and the member of staff leaves through a spawn point
    /// (<c>despawn</c>), no longer counting as staff.
    /// </summary>
    internal static void Fire(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.IsFired || humanoid.Profile is not { } profile || humanoid.Employer < 0 || humanoid.Employer >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[humanoid.Employer];
        hospital.Spend(simulation.Hours, profile.Wage, TransactionKind.Severance, profile.Class.ToString());

        // Unlike CorsixTH, firing does not cost reputation (the original game's, as the community describes it).
        Quit(simulation, humanoid);
    }

    /// <summary>The member of staff quits without severance and leaves as soon as they are free.</summary>
    internal static void Quit(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.IsFired)
        {
            return;
        }

        humanoid.IsFired = true;
        ClearRequest(humanoid);
        LeaveWhenFree(simulation, humanoid);
    }

    /// <summary>
    /// A fired member of staff leaves, unless they are in the middle of using an object together with a patient (the
    /// pharmacy's drink, the crash trolley's examination); then they finish that first.
    /// </summary>
    internal static void LeaveWhenFree(Simulation simulation, Humanoid humanoid)
    {
        if (!IsBusyWithPatient(simulation, humanoid))
        {
            Leave(simulation, humanoid);
        }
    }

    internal static bool IsBusyWithPatient(Simulation simulation, Humanoid humanoid) => humanoid.Behaviour switch
    {
        NurseBehaviour { Phase: NursePhase.Treating } => true,
        DoctorBehaviour doctor => simulation.FindEntity(doctor.RoomId) is Room room
            && simulation.FindEntity(room.PatientId) is Humanoid { Behaviour: PatientBehaviour { TrolleyUse: not null } },
        _ => false,
    };

    /// <summary>
    /// A request for money left unanswered for 30 days: under the grant wage increase policy (CorsixTH's, which the
    /// original lacks) it is agreed to, otherwise the member of staff quits, or a poached one joins the poacher.
    /// </summary>
    /// <returns>Whether the member of staff left.</returns>
    private static bool CheckIfWaitedTooLongForRaise(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.QuittingIn < 0)
        {
            return false;
        }

        humanoid.QuittingIn--;
        if (humanoid.QuittingIn >= 0)
        {
            return false;
        }

        if (simulation.Hospitals[humanoid.Employer].Policies.GrantWageIncrease && humanoid.RaiseReason != RaiseReason.DefiniteQuit)
        {
            Agree(simulation, humanoid);
            return false;
        }

        if (humanoid.PoachedBy >= 0)
        {
            ClearRequest(humanoid);
            humanoid.IsLeavingForPoacher = true;
            return true;
        }

        Quit(simulation, humanoid);
        return true;
    }

    /// <summary>
    /// An imported save's member of staff who has asked for money and waits for an answer: the request is made again
    /// here (an original save keeps neither its amount nor its kind, which the original works out afresh whenever its
    /// fax is opened), as their <paramref name="requests"/>th, and they wait <paramref name="hoursLeft"/> hours more.
    /// </summary>
    internal static void ImportRaiseRequest(Simulation simulation, Humanoid humanoid, int requests, long hoursLeft)
    {
        if (humanoid.Profile is null || humanoid.HasRaiseRequest)
        {
            return;
        }

        humanoid.RaiseRequestCount = Math.Clamp(requests, 1, DefiniteQuitRequest) - 1;
        RequestRaise(simulation, humanoid);
        humanoid.QuittingIn = (int)Math.Clamp(hoursLeft, 0, QuittingDelay);
    }

    /// <summary>
    /// A request for money, the first reason that holds deciding it: from the sixth request on nothing will keep them;
    /// paid below the expected wage, a rise of the shortfall plus a tenth of the expected wage; very tired, 10 % to 30 %
    /// of the wage (a triangular draw), as a rise one time in four and otherwise as a one-off payment; anything else, a
    /// one-off payment of 10 % to 30 %. A rise stops at the highest salary.
    /// </summary>
    private static void RequestRaise(Simulation simulation, Humanoid humanoid)
    {
        var profile = humanoid.Profile!;
        var random = simulation.Random;
        humanoid.RaiseRequestCount++;
        var expected = profile.FairWage(simulation.Rules.Staff);
        RaiseReason reason;
        int amount;
        if (humanoid.RaiseRequestCount >= DefiniteQuitRequest)
        {
            reason = RaiseReason.DefiniteQuit;
            amount = 0;
        }
        else if (profile.Wage < expected)
        {
            reason = RaiseReason.Underpaid;
            amount = expected - profile.Wage + (expected / 10);
        }
        else if (humanoid.Fatigue * 1000 > simulation.Rules.Tiredness.VeryTired)
        {
            amount = profile.Wage * random.Triangular(10, 30) / 100;
            reason = random.Next(4) == 0 ? RaiseReason.TiredRise : RaiseReason.TiredPayment;
        }
        else
        {
            amount = profile.Wage * random.Triangular(10, 30) / 100;
            reason = RaiseReason.Payment;
        }

        if (reason is RaiseReason.Underpaid or RaiseReason.TiredRise)
        {
            amount = Math.Min(amount, simulation.Rules.MaxSalary - profile.Wage);
        }

        humanoid.RaiseReason = reason;
        humanoid.RaiseRequest = reason == RaiseReason.DefiniteQuit ? 0 : Math.Max(1, amount);
        humanoid.QuittingIn = QuittingDelay;
    }

    /// <summary>
    /// <c>UIStaffManagement:payBonus</c>: if the balance is more than a tenth of the wage, that tenth is paid and the
    /// member of staff is half as happy again.
    /// </summary>
    /// <returns>Whether the bonus was paid.</returns>
    internal static bool PayBonus(Simulation simulation, Humanoid humanoid)
    {
        var profile = humanoid.Profile!;
        var hospital = simulation.Hospitals[humanoid.Employer];
        var bonus = profile.Wage / 10;
        if (hospital.Balance <= bonus)
        {
            return false;
        }

        Feelings.ChangeHappiness(simulation, humanoid, 0.5m);
        hospital.Spend(simulation.Hours, bonus, TransactionKind.PersonalBonus, profile.Class.ToString());
        return true;
    }

    /// <summary>
    /// <c>UIStaffManagement:increaseSalary</c>: a tenth more wage, up to the highest salary, and the member of staff is
    /// happy again; a pending request is settled by it, as in CorsixTH (a poached member of staff stays).
    /// </summary>
    internal static void IncreaseSalary(Simulation simulation, Humanoid humanoid)
    {
        var profile = humanoid.Profile!;
        var maxSalary = simulation.Rules.MaxSalary;
        if (profile.Wage < maxSalary)
        {
            profile.Wage = Math.Min(profile.Wage + (profile.Wage / 10), maxSalary);
        }

        ClearRequest(humanoid);
        humanoid.PoachedBy = -1;
        Feelings.ChangeHappiness(simulation, humanoid, 0.99m);
    }

    /// <summary>
    /// Leaves the job at once: gives up the room or desk (which then sends for someone else), steps off any object and
    /// walks to a random spawn point (<c>Humanoid:despawn</c>).
    /// </summary>
    private static void Leave(Simulation simulation, Humanoid humanoid)
    {
        Detach(simulation, humanoid);
        var points = simulation.Arrivals.SpawnPointsOf(simulation.Map);
        var home = points.Count > 0 ? points[simulation.Random.Next(points.Count)] : humanoid.Tile;
        humanoid.Behaviour = new LeavingBehaviour(home);
    }

    /// <summary>
    /// Takes a member of staff off their job: out of the room they work in or are going to, off the reception desk they
    /// staff or are going to; they stop walking and any action, and are seen again.
    /// </summary>
    internal static void Detach(Simulation simulation, Humanoid humanoid)
    {
        switch (humanoid.Behaviour)
        {
            case DoctorBehaviour doctor:
                if (simulation.FindEntity(doctor.RoomId) is Room examining
                    && simulation.FindEntity(examining.PatientId) is Humanoid { Behaviour: PatientBehaviour { TrolleyUse: { } } patient } examined)
                {
                    // Picked up while using the room's object with the patient: the use stops, the patient waits again.
                    patient.AbortTrolleyUse(simulation, examined, humanoid);
                }

                LeaveRoom(simulation, humanoid, doctor.RoomId, doctor.Use?.OldTile);
                break;
            case NurseBehaviour nurse:
                nurse.AbortTreatment(simulation, humanoid);
                LeaveRoom(simulation, humanoid, nurse.RoomId, null);
                break;
            case ReceptionistBehaviour receptionist when simulation.FindEntity(receptionist.DeskId) is RoomObject { Desk: { } state } desk:
                if (state.ReceptionistId == humanoid.Id)
                {
                    state.ReceptionistId = 0;
                }

                if (state.ReservedForId == humanoid.Id)
                {
                    state.ReservedForId = 0;
                }

                if (humanoid.Tile == desk.Tile)
                {
                    humanoid.PlaceAt(ReceptionDesks.SecondaryUseTile(simulation, desk));
                }

                break;
        }

        humanoid.ClearActionAnimation();
        humanoid.IsHidden = false;
        humanoid.StopWalking();
    }

    private static void LeaveRoom(Simulation simulation, Humanoid humanoid, int roomId, Maps.TilePosition? standAt)
    {
        if (simulation.FindEntity(roomId) is Room room && room.StaffId == humanoid.Id)
        {
            room.StaffId = 0;
            room.StaffPresent = false;
        }

        if (standAt is { } tile && tile != humanoid.Tile)
        {
            humanoid.PlaceAt(tile);
        }
    }
}
