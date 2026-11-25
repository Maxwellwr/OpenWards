// Spending, receiving, the transaction log and monthly wages ported from CorsixTH CorsixTH/Lua/hospital.lua
// (Hospital:spendMoney, Hospital:receiveMoney, Hospital:logTransaction, Hospital:onEndMonth); the casebook,
// treatment money from the same file (Hospital:receiveMoneyForTreatment) and updateNotCuredCounts. The reputation
// is not CorsixTH's: it follows the community's description of the original game (an average of the patients'
// outcomes, see ReputationScore); nor are prices, insurance companies and the monthly price level (the game logic
// spec's price and payment section). The hospital's value and the year's counts from the same file (Hospital:Hospital,
// spendMoney, receiveMoney, onEndYear) and the year end's trophies and awards from
// dialogs/fullscreen/annual_report.lua (UIAnnualReport:checkTrophiesAndAwards, updateAwards); the rooms discovered
// from the same file (Hospital:Hospital's room_discoveries, Hospital:isRoomDiscovered); heating from the same file
// (Hospital:onEndDay, onEndMonth, daysUntilNextDisaster, boilerBreakdown, _fixBoiler) and
// dialogs/fullscreen/town_map.lua (UITownMap:increaseHeat, decreaseHeat); the monthly statistics for the graphs from
// hospital.lua (Hospital:Hospital, onEndMonth, spendMoney, receiveMoney).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using OpenWards.Sim.Disasters;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.People;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Economy;

/// <summary>What a transaction was for, as in CorsixTH's <c>_S.transactions</c>.</summary>
public enum TransactionKind : byte
{
    BuyObject = 0,
    SellObject = 1,
    BuildRoom = 2,
    HireStaff = 3,
    Wages = 4,

    /// <summary>A patient paid for a diagnosis or a cure; the subject is the (pseudo) disease.</summary>
    Treatment = 5,

    /// <summary>An insurance company paid what it owed from two months ago; the subject is its number (1 to 3).</summary>
    Insurance = 6,

    /// <summary>
    /// The drug for a cure in the pharmacy was bought; the subject is the drug company's number (1 to 5), as CorsixTH
    /// names one of five companies at random.
    /// </summary>
    DrugCost = 7,

    /// <summary>Money for the year's trophies (CorsixTH's <c>eoy_trophy_bonus</c>).</summary>
    TrophyBonus = 8,

    /// <summary>The year's award money less its fines, which may be negative (CorsixTH's <c>eoy_bonus_penalty</c>).</summary>
    AwardBonus = 9,

    /// <summary>Money borrowed from the bank (<c>bank_loan</c>).</summary>
    Loan = 10,

    /// <summary>Money paid back to the bank (<c>loan_repayment</c>).</summary>
    LoanRepayment = 11,

    /// <summary>The month's interest on the loan (<c>loan_interest</c>).</summary>
    LoanInterest = 12,

    /// <summary>The month's interest on an overdrawn balance (<c>overdraft</c>).</summary>
    Overdraft = 13,

    /// <summary>A parcel of land was bought; the subject is its number (<c>buy_land</c>).</summary>
    BuyLand = 14,

    /// <summary>The month's heating bill (<c>heating</c>).</summary>
    Heating = 15,

    /// <summary>Money from the money cheat (<c>cheat</c>).</summary>
    Cheat = 16,

    /// <summary>A month's wage for a member of staff who was fired (<c>severance</c>); the subject is their class.</summary>
    Severance = 17,

    /// <summary>A pleased VIP's grant (CorsixTH's <c>vip_award</c>).</summary>
    VipAward = 18,

    /// <summary>The bonus for the patients cured in an emergency (CorsixTH's <c>emergency_bonus</c>).</summary>
    EmergencyBonus = 19,

    /// <summary>The fine for declaring an epidemic (CorsixTH's <c>epidemy_fine</c>); the subject is the disease.</summary>
    EpidemicFine = 20,

    /// <summary>The fine for an epidemic's cover-up that failed (<c>epidemy_coverup_fine</c>); the subject is the disease.</summary>
    EpidemicCoverUpFine = 21,

    /// <summary>The compensation for an epidemic's cover-up that worked (<c>compensation</c>); the subject is the disease.</summary>
    EpidemicCompensation = 22,

    /// <summary>A nurse's vaccination during an epidemic's cover-up (<c>vaccination</c>); the subject is the disease.</summary>
    Vaccination = 23,

    /// <summary>A can sold from a drinks machine (<c>drinks</c>, <c>Hospital:sellSodaToPatient</c>).</summary>
    Drinks = 24,

    /// <summary>The month's research costs (<c>research</c>).</summary>
    Research = 25,

    /// <summary>A bonus of a tenth of a wage paid from the staff management screen (<c>personal_bonus</c>).</summary>
    PersonalBonus = 26,

    /// <summary>A machine replaced from the machine window (<c>machine_replacement</c>); the subject is its object type.</summary>
    MachineReplacement = 27,

    /// <summary>Clearing away a room wrecked by an explosion (<c>transactions.remove_room</c>).</summary>
    RemoveRoom = 28,
}

/// <summary>
/// The hospital's figures at the end of a month, for the graphs (an entry of CorsixTH's <c>statistics</c>).
/// </summary>
/// <param name="MoneyIn">Money taken in during the month.</param>
/// <param name="MoneyOut">Money spent during the month.</param>
/// <param name="Wages">The month's wages.</param>
/// <param name="Balance">The balance at the month's end.</param>
/// <param name="Visitors">Patients so far.</param>
/// <param name="Cures">Patients cured so far.</param>
/// <param name="Deaths">Patients who died so far.</param>
/// <param name="Reputation">The reputation at the month's end.</param>
public readonly record struct MonthStatistics(long MoneyIn, long MoneyOut, int Wages, long Balance, int Visitors, int Cures, int Deaths, int Reputation);

/// <summary>A trophy, award or fine of the year end (CorsixTH's annual report).</summary>
public enum YearEndAwardKind : byte
{
    /// <summary>Trophy: the reputation stayed above <c>awards_trophies.Reputation</c> all year.</summary>
    ConsistentReputation,

    /// <summary>Trophy: nobody died or went home uncured.</summary>
    AllCuredTrophy,

    /// <summary>Trophy: nobody died.</summary>
    NoDeathsTrophy,

    /// <summary>Trophy: more cured than nine tenths of those not cured.</summary>
    ManyCuredTrophy,

    /// <summary>Award: a reputation above <c>ReputationAward</c>.</summary>
    HighReputation,

    /// <summary>Fine: a reputation below <c>ReputationPoor</c>.</summary>
    PoorReputation,

    /// <summary>Award (in reputation): a hospital worth more than <c>HospValueAward</c>.</summary>
    HighValue,

    /// <summary>Penalty (in reputation): a hospital worth less than <c>HospValuePoor</c>.</summary>
    LowValue,

    /// <summary>Award: more cures than <c>CuresAward</c>.</summary>
    ManyCures,

    /// <summary>Fine: fewer cures than <c>CuresPoor</c>.</summary>
    FewCures,

    /// <summary>Award: nobody died or went home uncured.</summary>
    AllCures,

    /// <summary>Award: fewer deaths than <c>DeathsAward</c>.</summary>
    FewDeaths,

    /// <summary>Fine: more deaths than <c>DeathsPoor</c>.</summary>
    ManyDeaths,

    /// <summary>Award: more than <c>CuresVDeathsAward</c> cures per death.</summary>
    CuresOverDeaths,

    /// <summary>Fine: at most <c>CuresVDeathsPoor</c> cures per death.</summary>
    DeathsOverCures,

    /// <summary>Trophy (in reputation): every VIP of the year left pleased.</summary>
    HappyVips,

    /// <summary>Trophy: more cans sold from the drinks machines than <c>CansofCoke</c>.</summary>
    DrinksSoldTrophy,

    /// <summary>Trophy (in reputation): the staff's mean happiness stayed at <c>TrophyStaffHappiness</c> % or more each month.</summary>
    HappyStaffTrophy,

    /// <summary>Trophy (in reputation): more rats shot than <c>RatKillsAbsolute</c>; the detail is how many.</summary>
    RatsKilledTrophy,

    /// <summary>Trophy: a good aim at rats (<c>RatKillsPercentage</c>); the detail is the hits in percent of the shots.</summary>
    RatAccuracyTrophy,

    /// <summary>Trophy (in reputation): <c>Plant</c> % or more of the plants were watered each month.</summary>
    HealthyPlantsTrophy,

    /// <summary>Award (in reputation): more than <c>PopulationPercentageAward</c> % of the town's patients last month.</summary>
    HighPopulationShare,

    /// <summary>Penalty (in reputation): fewer than <c>PopulationPercentagePoor</c> % of them.</summary>
    LowPopulationShare,

    /// <summary>Award (in reputation): patients happier than <c>PeepHappinessAward</c> % over the year.</summary>
    HappyPatients,

    /// <summary>Penalty (in reputation): patients less happy than <c>PeepHappinessPoor</c> %.</summary>
    UnhappyPatients,

    /// <summary>Award (in reputation): staff happier than <c>StaffHappinessAward</c> % over the year.</summary>
    HappyStaff,

    /// <summary>Penalty (in reputation): staff less happy than <c>StaffHappinessPoor</c> %.</summary>
    UnhappyStaff,

    /// <summary>Award (in reputation): less litter and rat holes per tile than <c>CleanlinessAward</c> %.</summary>
    Clean,

    /// <summary>Penalty (in reputation): more than <c>CleanlinessPoor</c> %.</summary>
    Dirty,

    /// <summary>Award (in reputation): patients waited less than <c>WaitingTimesAward</c> % of what they put up with.</summary>
    ShortWaits,

    /// <summary>Penalty (in reputation): more than <c>WaitingTimesPoor</c> %.</summary>
    LongWaits,

    /// <summary>Award (in reputation): more than <c>EmergencyAward</c> % of the emergencies' patients saved.</summary>
    EmergenciesSaved,

    /// <summary>Penalty (in reputation): fewer than <c>EmergencyPoor</c> %.</summary>
    EmergenciesLost,

    /// <summary>Award (in reputation): more research than <c>NewTechAward</c> points.</summary>
    GoodResearch,

    /// <summary>Penalty (in reputation): less than <c>NewTechPoor</c> points.</summary>
    PoorResearch,

    /// <summary>Award (in reputation): the machines worn less than <c>WellKeptTechAward</c> % on average.</summary>
    WellKeptMachines,

    /// <summary>Penalty (in reputation): worn more than <c>WellKeptTechPoor</c> %.</summary>
    NeglectedMachines,
}

/// <summary>
/// What one month showed of a hospital, for the year's trophies and awards; −1 where there was nothing to measure.
/// </summary>
/// <param name="Reputation">The reputation at the month's end.</param>
/// <param name="StaffHappiness">The staff's mean happiness, in percent.</param>
/// <param name="PatientHappiness">The patients' mean happiness, in percent.</param>
/// <param name="PlantsWatered">The plants that need no water, in percent of them all.</param>
/// <param name="Waiting">The patients' mean leave count, in percent of what they put up with (<c>LeaveMax</c>).</param>
public readonly record struct AwardMonth(int Reputation, int StaffHappiness, int PatientHappiness, int PlantsWatered, int Waiting);

/// <summary>A trophy, award or fine the hospital got at a year end, and what it brought.</summary>
/// <param name="Kind">Which.</param>
/// <param name="Money">Money it brought; negative for a fine.</param>
/// <param name="Reputation">Reputation it brought, in percent of the visits it weighs against; negative for a penalty.</param>
/// <param name="Detail">A number its text shows: the rats shot, or the hits in percent of the shots.</param>
public readonly record struct YearEndAward(YearEndAwardKind Kind, int Money, int Reputation, int Detail = 0);

/// <summary>
/// Why a patient went home uncured (CorsixTH's <c>Patient:goHome</c> reasons). Either way the patient's visit counts in
/// the reputation without points (<see cref="Hospital.CountPatient"/>).
/// </summary>
public enum ReputationChange : byte
{
    /// <summary>A patient was sent home or gave up waiting.</summary>
    Kicked = 2,

    /// <summary>A patient found the price too high.</summary>
    OverPriced = 3,
}

/// <summary>One entry of a hospital's transaction log.</summary>
/// <param name="Hour">When it happened, as the hour of the game's calendar (<see cref="Simulation.Hours"/>).</param>
/// <param name="Kind">What it was for.</param>
/// <param name="Subject">Id of the room or object type it was for, the staff class of a hire, or empty.</param>
/// <param name="Amount">Money received (positive) or spent (negative).</param>
/// <param name="Balance">The balance afterwards.</param>
public readonly record struct Transaction(long Hour, TransactionKind Kind, string Subject, int Amount, long Balance);

/// <summary>
/// The hospital's casebook entry for a disease or a diagnosis (CorsixTH's <c>disease_casebook</c>): the
/// price the player set, whether the disease is known, and what it earned.
/// </summary>
public sealed class DiseaseCase
{
    /// <summary>The standard price in percent; the player may set 50 to 200.</summary>
    public const int StandardPrice = 100;

    /// <summary>The lowest price the player may set, in percent.</summary>
    public const int MinPrice = 50;

    /// <summary>The highest price the player may set, in percent.</summary>
    public const int MaxPrice = 200;

    /// <summary>Cure effectiveness of a disease cured without a drug.</summary>
    public const int FullEffectiveness = 100;

    internal DiseaseCase(string diseaseId, bool discovered)
    {
        DiseaseId = diseaseId;
        Discovered = discovered;
    }

    /// <summary>How likely a cure works, in percent (CorsixTH's <c>cure_effectiveness</c>): the drug's rating, otherwise 100.</summary>
    public int CureEffectiveness { get; internal set; } = FullEffectiveness;

    /// <summary>
    /// Whether the pharmacy has a drug for it: from the start for a disease the level marks <c>Known</c>, otherwise once
    /// research has found it. Always true for a disease not cured with a drug.
    /// </summary>
    public bool DrugAvailable { get; internal set; } = true;

    /// <summary>What the drug of each cure costs, or 0 for a disease not cured with a drug (CorsixTH's <c>drug_cost</c>).</summary>
    public int DrugCost { get; internal set; }

    /// <summary>Patients cured of it so far.</summary>
    public int Recoveries { get; internal set; }

    /// <summary>Patients who died of it so far.</summary>
    public int Fatalities { get; internal set; }

    public string DiseaseId { get; }

    /// <summary>The price as a percentage of the standard price (CorsixTH's <c>price</c>, a multiplier).</summary>
    public int PricePercent { get; internal set; } = StandardPrice;

    /// <summary>Whether the hospital knows the disease, so that it can be guessed and shows in the casebook.</summary>
    public bool Discovered { get; internal set; }

    /// <summary>Money received for it so far, insurance payments included.</summary>
    public long MoneyEarned { get; internal set; }

    /// <summary>
    /// The hospital's reputation for this disease, from 0 to 1000: like the hospital's (<see cref="Hospital.Reputation"/>)
    /// the average outcome of its patients, on counts of its own that start at 500; null for a diagnosis, which goes by
    /// the hospital's reputation. Setting it keeps the visits and sets the score to match.
    /// </summary>
    public int? Reputation
    {
        get => ReputationVisits == 0 ? null : Hospital.ReputationOf(ReputationScore, ReputationVisits);
        internal set
        {
            if (value is { } reputation)
            {
                ReputationVisits = ReputationVisits == 0 ? Hospital.StartReputationVisits : ReputationVisits;
                ReputationScore = (long)reputation * ReputationVisits;
            }
            else
            {
                ReputationVisits = 0;
                ReputationScore = 0;
            }
        }
    }

    /// <summary>The disease's reputation score, as <see cref="Hospital.ReputationScore"/>; 0 for a diagnosis.</summary>
    public long ReputationScore { get; internal set; }

    /// <summary>Patients with the disease counted in its reputation, as <see cref="Hospital.ReputationVisits"/>; 0 for a diagnosis.</summary>
    public int ReputationVisits { get; internal set; }

    /// <summary>Patients with it who were sent home or gave up (CorsixTH's <c>turned_away</c>).</summary>
    public int TurnedAway { get; internal set; }
}

/// <summary>A price's judgement by the adviser (<c>Hospital:advisePriceLevelImpact</c>'s under, over and fair).</summary>
public enum PriceJudgment : byte
{
    Under,
    Over,
    Fair,
}

/// <summary>A player's hospital: its money, its casebook and what insurance companies owe it.</summary>
public sealed class Hospital
{
    /// <summary>How many transactions the log keeps; CorsixTH keeps about 20.</summary>
    public const int LogLength = 20;

    /// <summary>Insurance companies a hospital deals with.</summary>
    public const int InsuranceCompanies = 3;

    /// <summary>
    /// The insurance companies there are to choose from, as the original game has them: the first eight names of
    /// <c>insurance_companies</c> (string S[20][2] to [9]), each with its own delay and price bonus.
    /// </summary>
    public const int InsuranceCompanyNames = 8;

    /// <summary>The companies CorsixTH chose from, which saves before format 106 may name.</summary>
    private const int OldInsuranceCompanyNames = 11;

    /// <summary>Months each insurance company (1 to 8, index 0 unused) takes to pay what it owes.</summary>
    private static readonly int[] InsurerPaymentDelays = [0, 3, 6, 8, 2, 9, 10, 4, 3];

    /// <summary>
    /// How much more each insurance company (1 to 8, index 0 unused) lets its patients pay, in percent of the price
    /// they expect.
    /// </summary>
    private static readonly int[] InsurerPriceBonuses = [0, 15, 10, 5, 8, 9, 40, 12, 3];

    private readonly int[] _insurers = [1, 2, 3];

    /// <summary>
    /// The companies the hospital deals with, each a number from 1 to <see cref="InsuranceCompanyNames"/> in the list of
    /// <c>insurance_companies</c> (string S[20][n + 1]); the first three unless <see cref="ChooseInsurers"/> picked others.
    /// </summary>
    public IReadOnlyList<int> Insurers => _insurers;

    /// <summary>Three different companies of the eight, each as likely as the others, all of which the hospital accepts.</summary>
    public void ChooseInsurers(SimRandom random)
    {
        var companies = Enumerable.Range(1, InsuranceCompanyNames).ToList();
        for (var i = 0; i < InsuranceCompanies; i++)
        {
            var index = random.Next(companies.Count);
            _insurers[i] = companies[index];
            companies.RemoveAt(index);
        }
    }

    /// <summary>
    /// Replaces a company past the eighth, which a save before format 106 may name, with the first of the eight the
    /// hospital does not deal with yet.
    /// </summary>
    private void KeepInsurersKnown()
    {
        for (var i = 0; i < InsuranceCompanies; i++)
        {
            if (_insurers[i] > InsuranceCompanyNames)
            {
                _insurers[i] = Enumerable.Range(1, InsuranceCompanyNames).First(company => !_insurers.Contains(company));
            }
        }
    }

    /// <summary>Months an insurance company takes to pay (1 to 8), as <see cref="Insurers"/> numbers them.</summary>
    public static int InsurerPaymentDelay(int insurer) => InsurerPaymentDelays[insurer];

    /// <summary>How much more, in percent of the expected price, a patient of the insurance company (1 to 8) agrees to pay.</summary>
    public static int InsurerPriceBonus(int insurer) => InsurerPriceBonuses[insurer];

    /// <summary>
    /// The months ahead for which insurance companies owe: index 0 is paid at the end of this month, index 1 at the end
    /// of the next, and so on. The original keeps a slot for each month of the year, so no delay reaches 12 months.
    /// </summary>
    private const int InsuranceMonths = 12;

    /// <summary>The months CorsixTH's companies (and saves before format 106) kept: paid at the end of the third.</summary>
    private const int OldInsuranceMonths = 3;

    /// <summary>The lowest monthly price level at which the adviser says prices are too low (unless it is 0).</summary>
    public const int LowPriceLevel = 60;

    /// <summary>The highest monthly price level at which the adviser does not yet say prices are too high.</summary>
    public const int HighPriceLevel = 150;

    /// <summary>The highest price level there is.</summary>
    public const int MaxPriceLevel = 300;

    /// <summary>Yearly inflation of prices (CorsixTH's <c>inflation_rate</c>).</summary>
    public const decimal InflationRate = 0.045m;

    private const int MaxCases = 256;

    /// <summary>The player's salary at the start of a level (<c>Hospital:Hospital</c>).</summary>
    public const long StartPlayerSalary = 10_000;

    /// <summary>The highest salary there is.</summary>
    public const long MaxPlayerSalary = 200_000_000;

    /// <summary>Above this salary the year's raise always takes the table's column above the ceiling.</summary>
    public const long RaiseCeilingSalary = 1_000_000;

    /// <summary>The level scores (at most) of the year's raise table's rows, the last row for anything higher.</summary>
    private static readonly int[] RaiseScores = [10, 60, 70, 75, 80, 90, 95, 100, 105, 110, 120, 130, 150, 175];

    /// <summary>The year's raise in percent for each row, while the salary is below the ceiling.</summary>
    private static readonly int[] RaisePercents = [0, 5, 6, 7, 8, 9, 10, 50, 55, 56, 58, 60, 62, 64, 65];

    /// <summary>The year's raise in percent for each row once the salary is above the ceiling.</summary>
    private static readonly int[] CeilingRaisePercents = [0, 0, 0, 0, 0, 2, 3, 4, 5, 5, 5, 6, 7, 7, 7];

    /// <summary>The longest name read back from a save.</summary>
    private const int MaxNameLength = 64;

    /// <summary>The most rat holes read back from a save.</summary>
    private const int MaxRatholes = 1 << 16;

    /// <summary>Months of statistics a save may hold: over 5,000 years.</summary>
    private const int MaxStatisticsMonths = 1 << 16;

    /// <summary>
    /// Hours per segment of the opening watch: CorsixTH's <c>UIWatch</c> runs 100 days over 13 segments
    /// (<c>TICK_DAYS</c>, <c>TIMER_SEGMENTS</c>), rounded down.
    /// </summary>
    public const int OpeningTickRate = 100 * 50 / 13;

    /// <summary>Segments of the opening watch (CorsixTH's <c>open_timer</c> starts at 12).</summary>
    public const int OpeningSegments = 12;

    /// <summary>The reputation a disease starts with in the casebook, and above which prices rise with it.</summary>
    public const int AverageReputation = 500;

    /// <summary>The bank's yearly interest in hundredths of a percent (the level files' <c>InterestRate</c>, base_config.lua's 100).</summary>
    public const int DefaultInterestRate = 100;

    /// <summary>What overdrafts cost on top of the interest, likewise (<c>OverdraftDiff</c>, base_config.lua's 200).</summary>
    /// <remarks>Read from the level files but not used: an overdraft costs twice the interest rate.</remarks>
    public const int DefaultOverdraftDifferential = 200;

    /// <summary>The most a hospital may borrow, whatever it is worth.</summary>
    public const int LoanCeiling = 100_000;

    /// <summary>What a hospital may borrow on top of a third of its value.</summary>
    public const int LoanAllowance = 10_000;

    /// <summary>The steps in which money is borrowed and paid back.</summary>
    public const int LoanStep = 5000;

    /// <summary>
    /// What a hospital with nothing built and the average reputation is worth besides its land: 10,000 for reputation
    /// 500 and 10,000 on top (<see cref="HospitalValue"/>).
    /// </summary>
    public const int StartValueBesidesLand = (AverageReputation / HospitalValue.ReputationStep * HospitalValue.ValuePerReputationStep) + HospitalValue.BaseValue;

    private const int MaxYearEndAwards = 64;

    /// <summary>The most months of a year's award records read back from a save.</summary>
    private const int MaxAwardMonths = 64;

    public const int MinReputation = 0;

    public const int MaxReputation = 1000;

    /// <summary>The visits a reputation starts with: at 500 a level starts with 20 points for 20 visits.</summary>
    public const int StartReputationVisits = 20;

    /// <summary>What a point of a patient's outcome adds to <see cref="ReputationScore"/>: a visit with one point stands for 500.</summary>
    public const int ReputationPerPoint = 500;

    /// <summary>Days without a heating disaster, give or take 10, by difficulty (<c>disaster_free_days</c>).</summary>
    private static readonly int[] DisasterFreeDays = [300, 200, 150];

    private readonly List<Transaction> _transactions = [];
    private readonly SortedDictionary<string, DiseaseCase> _casebook = new(StringComparer.Ordinal);

    /// <summary>What each of the hospital's insurance companies owes, by the month it pays (<see cref="InsuranceMonths"/>).</summary>
    private readonly long[,] _insuranceOwed = new long[InsuranceCompanies, InsuranceMonths];

    private readonly List<YearEndAward> _lastYearAwards = [];
    private readonly List<AwardMonth> _awardMonths = [];
    private readonly SortedSet<string> _discoveredRooms = new(StringComparer.Ordinal);
    private readonly List<MonthStatistics> _statistics = [];

    internal Hospital(int player, long balance, int reputation = AverageReputation, long value = 0, int interestRate = DefaultInterestRate)
    {
        InterestRate = interestRate;
        Player = player;
        Balance = balance;
        ReputationVisits = StartReputationVisits;
        ReputationScore = (long)Math.Clamp(reputation, MinReputation, MaxReputation) * StartReputationVisits;
        LowestReputationThisYear = Reputation;
        Value = value;
        _statistics.Add(new MonthStatistics(0, 0, 0, Balance, 0, 0, 0, Reputation));
    }

    /// <summary>
    /// The figures of each month for the graphs, the first as the game began (CorsixTH's <c>statistics</c>): entry
    /// <c>i</c> holds them at the end of month <c>i</c> of the game.
    /// </summary>
    public IReadOnlyList<MonthStatistics> Statistics => _statistics;

    /// <summary>Money taken in this month so far (CorsixTH's <c>money_in</c>).</summary>
    public long MoneyInThisMonth { get; private set; }

    /// <summary>Money spent this month so far (CorsixTH's <c>money_out</c>).</summary>
    public long MoneyOutThisMonth { get; private set; }

    /// <summary>What the hospital is worth (<see cref="HospitalValue"/>), as last worked out.</summary>
    public long Value { get; internal set; }

    /// <summary>What the hospital owes the bank (CorsixTH's <c>loan</c>).</summary>
    public long Loan { get; private set; }

    /// <summary>The player's network mission (<see cref="Economy.NetMission"/>).</summary>
    public NetMission NetMission { get; private set; } = new();

    /// <summary>The bank's yearly interest on the loan, in hundredths of a percent.</summary>
    public int InterestRate { get; private set; }

    /// <summary>
    /// What the hospital may owe the bank: a third of its value, rounded down, and 10,000 more, at most 100,000.
    /// </summary>
    /// <remarks>
    /// Nothing in free build mode. Departs from CorsixTH, whose limit is 33 % of the value rounded down to 5,000, and
    /// 10,000 more, without a ceiling.
    /// </remarks>
    public long MaxLoan => FreeBuild ? 0 : Math.Clamp((Value / 3) + LoanAllowance, 0, LoanCeiling);

    /// <summary>
    /// Whether the level is played without money (<see cref="LevelRules.FreeBuild"/>): no money is spent or
    /// taken in. Not saved: it comes from the level's rules.
    /// </summary>
    internal bool FreeBuild { get; set; }

    /// <summary>
    /// The tile of the hospital's land that last got litter where there was none, which new rat holes start from; null
    /// before any.
    /// </summary>
    public TilePosition? LastLitterTile { get; internal set; }

    /// <summary>Shots at rats this year.</summary>
    public int RatShotsThisYear { get; internal set; }

    /// <summary>Rats shot dead this year.</summary>
    public int RatsKilledThisYear { get; internal set; }

    /// <summary>Patients who came this year (CorsixTH's <c>num_visitors_ty</c>).</summary>
    public int VisitorsThisYear { get; internal set; }

    /// <summary>Patients cured this year (CorsixTH's <c>num_cured_ty</c>).</summary>
    public int CuredThisYear { get; internal set; }

    /// <summary>Patients who died this year (CorsixTH's <c>num_deaths_this_year</c>).</summary>
    public int DeathsThisYear { get; internal set; }

    /// <summary>Patients who went home uncured this year (CorsixTH's <c>not_cured_ty</c>).</summary>
    public int NotCuredThisYear { get; internal set; }

    /// <summary>
    /// The lowest reputation since the year began; while it stays above the level's <c>awards_trophies.Reputation</c>
    /// the hospital is on its way to the consistent reputation trophy (CorsixTH's <c>has_impressive_reputation</c>).
    /// </summary>
    public int LowestReputationThisYear { get; private set; }

    /// <summary>
    /// Cured patients in percent of all who were cured, died or went home uncured, rounded (CorsixTH's
    /// <c>percentage_cured</c> from <c>Hospital:updatePercentages</c>); 0 until the first of them.
    /// </summary>
    public int PercentageCured
    {
        get
        {
            var all = Cured + NotCured + Deaths;
            return all == 0 ? 0 : RoundPercent(Cured, all);
        }
    }

    /// <summary>
    /// Deaths in percent of the cured and dead, rounded (CorsixTH's <c>percentage_killed</c>); 0 until the first
    /// patient leaves. CorsixTH computes 0 / 0 when patients have only gone home uncured, which is null here.
    /// </summary>
    public int? PercentageKilled => Cured + Deaths > 0 ? RoundPercent(Deaths, Cured + Deaths) : NotCured > 0 ? null : 0;

    /// <summary>Whether the hospital has won or lost the level.</summary>
    public EndOutcome Outcome { get; private set; }

    /// <summary>The goal the hospital lost to, if it lost.</summary>
    public EndGoal? LostTo { get; private set; }

    /// <summary>The trophies, awards and fines of the last year end, in the order of CorsixTH's annual report.</summary>
    public IReadOnlyList<YearEndAward> LastYearAwards => _lastYearAwards;

    /// <summary>What each month of this year showed, for the trophies and awards (<see cref="YearEndAwards"/>).</summary>
    public IReadOnlyList<AwardMonth> AwardMonths => _awardMonths;

    /// <summary>The hospital's share of the town's new patients last month, in percent.</summary>
    public int PopulationPercent { get; internal set; }

    /// <summary>Emergencies that ended this year.</summary>
    public int EmergenciesThisYear { get; internal set; }

    /// <summary>Patients the emergencies that ended this year brought.</summary>
    public int EmergencyVictimsThisYear { get; internal set; }

    /// <summary>Of those, the ones cured in time.</summary>
    public int EmergencySavedThisYear { get; internal set; }

    /// <summary>Points of research this year's researchers' actions brought.</summary>
    public int ResearchPointsThisYear { get; internal set; }

    /// <summary>The player (0-based) the hospital belongs to.</summary>
    public int Player { get; }

    /// <summary>Money in the bank; may go below zero.</summary>
    public long Balance { get; private set; }

    /// <summary>The latest transactions, newest first.</summary>
    public IReadOnlyList<Transaction> Transactions => _transactions;

    /// <summary>Patients who came to the hospital so far (CorsixTH's <c>num_visitors</c>).</summary>
    public int Visitors { get; internal set; }

    /// <summary>Patients cured so far (CorsixTH's <c>num_cured</c>).</summary>
    public int Cured { get; internal set; }

    /// <summary>Cans sold from the drinks machines this year (<c>sodas_sold</c>).</summary>
    public int SodasSold { get; internal set; }

    /// <summary>
    /// Autopsies that came out and cost reputation so far, so that the adviser can say so
    /// (<c>_A.research.autopsy_discovered_rep_loss</c>).
    /// </summary>
    public int AutopsiesDiscovered { get; internal set; }

    /// <summary>
    /// How many times the month's price level has had the adviser judge the prices, and the last judgement with the
    /// casebook entry furthest that way, for the interface to say.
    /// </summary>
    public int PriceAdvices { get; private set; }

    /// <inheritdoc cref="PriceAdvices"/>
    public PriceJudgment LastPriceJudgment { get; private set; }

    /// <summary>The price level of the last month's end (<see cref="UpdatePriceLevel"/>): 100 at the expected prices.</summary>
    public int PriceLevel { get; private set; } = DiseaseCase.StandardPrice;

    /// <inheritdoc cref="PriceAdvices"/>
    public string LastPriceAdviceDisease { get; private set; } = string.Empty;

    /// <summary>
    /// Patients who went home because the price was too high, and the last one's casebook entry (Patient:goHome's
    /// <c>warnPatientNotPaying</c>), for the interface to say once a month.
    /// </summary>
    public int NotPaying { get; private set; }

    /// <inheritdoc cref="NotPaying"/>
    public string LastNotPayingDisease { get; private set; } = string.Empty;

    /// <summary>How long the fax telling of a discovered disease waits before it goes (<c>adviseDiscoverDisease</c>'s 25*24).</summary>
    public const int DiseaseReportDays = 25;

    /// <summary>
    /// The faxes telling of diseases newly discovered, oldest first, each until it is closed or expires
    /// (<c>PlayerHospital:adviseDiscoverDisease</c>).
    /// </summary>
    public List<DiseaseReport> DiseaseReports { get; } = [];

    /// <summary>adviseDiscoverDisease: a fax tells of the disease, for 25 days.</summary>
    internal void AdviseDiscoverDisease(Simulation simulation, string diseaseId) =>
        DiseaseReports.Add(new DiseaseReport(diseaseId, simulation.Tick + (DiseaseReportDays * Calendar.GameDate.HoursPerDay)));

    /// <summary>Forgets the faxes about diseases and the epidemic's result once they have expired.</summary>
    internal void TickReports(Simulation simulation)
    {
        DiseaseReports.RemoveAll(report => simulation.Tick >= report.Expires);
        if (Epidemics.Report is { } report && simulation.Tick >= report.Expires)
        {
            Epidemics.Report = null;
        }
    }

    /// <summary>
    /// Machines that have exploded and wrecked their room this year, for the year's judgement of the hospital's care of its
    /// machines. The original game never clears its flag, so the reprimand comes every year after one explosion; here it
    /// is cleared at the end of the year, as intended.
    /// </summary>
    public int Explosions { get; internal set; }

    /// <summary>Whether a patient has dropped litter in the hospital yet (CorsixTH's <c>hospital_littered</c>).</summary>
    public bool Littered { get; internal set; }

    /// <summary>
    /// Whether a handyman's priorities have been changed, after which the adviser no longer explains them
    /// (CorsixTH's <c>handyman_popup</c>).
    /// </summary>
    public bool HandymanPrioritiesChanged { get; internal set; }

    /// <summary>Whether a patient has peed on the floor yet, for the adviser (CorsixTH's <c>did_it_on_floor</c>).</summary>
    public bool DidItOnFloor { get; internal set; }

    /// <summary>A finished room taken back to its blueprint and not built again yet, or null (<see cref="Rooms.RoomRebuild"/>).</summary>
    public Rooms.RoomRebuild? Rebuild { get; internal set; }

    /// <summary>Patients who died so far (CorsixTH's <c>num_deaths</c>).</summary>
    public int Deaths { get; internal set; }

    /// <summary>Patients who went home without a cure so far: sent home, gave up or found it too dear (CorsixTH's <c>not_cured</c>).</summary>
    public int NotCured { get; internal set; }

    /// <summary>
    /// The hospital's reputation, from 0 to 1000: the average outcome of its patients, <c>⌊points × 500 / visits⌋</c>
    /// held to 0–1000 (<see cref="ReputationScore"/> over <see cref="ReputationVisits"/>). Nothing wears off with time; an
    /// event only gets diluted as visits grow. Levels start at their <c>StartRep</c>, usually 500.
    /// </summary>
    public int Reputation => ReputationOf(ReputationScore, ReputationVisits);

    /// <summary>
    /// The points of the patients' outcomes times <see cref="ReputationPerPoint"/>: a level at 500 starts with 20 points
    /// for 20 visits, 10,000. It is not held to anything: what is beyond 0–1000 stays, only <see cref="Reputation"/> is held.
    /// </summary>
    public long ReputationScore { get; private set; }

    /// <summary>Patients counted in the reputation, each once, on top of the <see cref="StartReputationVisits"/> it starts with.</summary>
    public int ReputationVisits { get; private set; }

    /// <summary>Year ends so far, each of which raised prices by the inflation rate.</summary>
    public int InflationYears { get; private set; }

    /// <summary>
    /// Whether the hospital has opened, so that patients come (CorsixTH's <c>Hospital.opened</c>): when the opening
    /// watch has run down, or when the player opened it early.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>The room types the hospital may build, in id order: those discovered at the start or since by research.</summary>
    public IReadOnlyCollection<string> DiscoveredRooms => _discoveredRooms;

    /// <summary>Whether the hospital may build rooms of this type (CorsixTH's <c>Hospital:isRoomDiscovered</c>).</summary>
    public bool IsRoomDiscovered(string roomId) => _discoveredRooms.Contains(roomId);

    /// <summary>Lets the hospital build rooms of this type: at the start, or when research discovers it.</summary>
    internal void DiscoverRoom(string roomId) => _discoveredRooms.Add(roomId);

    /// <summary>Whether the player has used a cheat (CorsixTH's <c>cheated</c>).</summary>
    public bool Cheated { get; internal set; }

    /// <summary>The fax code cheats switched on for the hospital (CorsixTH's <c>hosp_cheats.active_cheats</c>).</summary>
    public ToggleCheats ActiveCheats { get; internal set; }

    /// <summary>
    /// The player's own salary (CorsixTH's <c>player_salary</c>), the "Salary" of the year's charts and the score of the
    /// Hall of Fame; it starts at 10,000 and rises at the end of each year (<see cref="RaiseSalary"/>).
    /// </summary>
    public long PlayerSalary { get; internal set; } = StartPlayerSalary;

    /// <summary>
    /// The name the hospital goes by when the game itself knows it (a save of the original brings its player's name),
    /// else empty, and the player's name from the settings is shown.
    /// </summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>The salary offered for the next level on winning (<c>salary_offer</c>), 0 until then and after a refusal.</summary>
    public long SalaryOffer { get; private set; }

    /// <summary>The player's salary when the level started, which the year's raise measures its ceiling from.</summary>
    public long StartSalary { get; private set; } = StartPlayerSalary;

    /// <summary>How many letters offering the next level have come; each new one is another letter to show.</summary>
    public int SalaryOffers { get; private set; }

    /// <summary>Whether the player turned the next level down to play on.</summary>
    public bool IsSalaryOfferDeclined { get; private set; }

    /// <summary>Whether the next quarter's check for another offer is skipped, the first after a refusal.</summary>
    public bool SkipsNextSalaryOffer { get; private set; }

    /// <summary>Patients sent to the hospital so far, whether they came or not (CorsixTH's <c>spawn_attempts</c>).</summary>
    public long SpawnAttempts { get; internal set; }

    /// <summary>The radiators' heat at the start, in tenths (CorsixTH's <c>radiator_heat</c> of 0.5).</summary>
    public const int DefaultRadiatorHeat = 5;

    /// <summary>The highest heat the player may set, in tenths.</summary>
    public const int MaxRadiatorHeat = 10;

    /// <summary>The lowest heat the player may set, in tenths.</summary>
    public const int MinRadiatorHeat = 1;

    /// <summary>How hot the radiators are, in tenths (CorsixTH's <c>heating.radiator_heat</c>): 0 or 10 while the boiler is broken.</summary>
    public int RadiatorHeat { get; private set; } = DefaultRadiatorHeat;

    /// <summary>The hospital policy window's settings.</summary>
    public HospitalPolicies Policies { get; internal set; } = new();

    /// <summary>The answer to the player's last try at poaching another hospital's member of staff, if any.</summary>
    public PoachAnswer? LastPoach { get; internal set; }

    /// <summary>VIPs asking to visit, on their way and gone (<see cref="VipVisits"/>).</summary>
    public VipVisits Vips { get; internal set; } = new();

    /// <summary>The next emergency, the one offered or under way, and its helicopter (<see cref="Disasters.Emergencies"/>).</summary>
    public HospitalEmergencies Emergencies { get; internal set; } = new();

    /// <summary>The epidemic the player knows of, the hidden ones, and the last one's outcome (<see cref="Disasters.Epidemics"/>).</summary>
    public HospitalEpidemics Epidemics { get; internal set; } = new();

    private readonly List<Rathole> _ratholes = [];

    /// <summary>The rat holes in the hospital's corridor walls, in the order they appeared (<see cref="Disasters.Rats"/>).</summary>
    public IReadOnlyList<Rathole> Ratholes => _ratholes;

    /// <summary><c>Hospital:addRathole</c>: a hole, unless that wall of the tile has one.</summary>
    internal void AddRathole(Rathole hole)
    {
        if (!_ratholes.Any(other => other.Tile == hole.Tile && other.Wall == hole.Wall))
        {
            _ratholes.Add(hole);
        }
    }

    /// <summary><c>Hospital:removeRatholeXY</c>: the holes of a tile close.</summary>
    internal void RemoveRatholesAt(TilePosition tile) => _ratholes.RemoveAll(hole => hole.Tile == tile);

    /// <summary>Closes the holes that match.</summary>
    internal void RemoveRatholes(Predicate<Rathole> match) => _ratholes.RemoveAll(match);

    /// <summary>Whether the boiler is broken, so that the radiators are stuck at full or no heat (CorsixTH's <c>heating_broke</c>).</summary>
    public bool IsHeatingBroken { get; private set; }

    /// <summary>The heat the player had set before the boiler broke (CorsixTH's <c>saved_radiator_heat</c>).</summary>
    private int SavedRadiatorHeat { get; set; } = DefaultRadiatorHeat;

    /// <summary>Repair work left on the broken boiler (CorsixTH's <c>boiler_repair_count</c>).</summary>
    public int BoilerRepairCount { get; private set; }

    /// <summary>Days until the next chance of a disaster, such as the boiler breaking (CorsixTH's <c>disasterless_days</c>).</summary>
    public int DisasterlessDays { get; private set; }

    /// <summary>The heating run up this month and not yet paid (CorsixTH's <c>acc_heating</c>).</summary>
    public decimal AccruedHeating { get; private set; }

    /// <summary>The opening watch's segments still to go, from 12 down to −1 (CorsixTH's <c>open_timer</c>).</summary>
    public int OpenTimer { get; private set; } = OpeningSegments;

    /// <summary>Hours left in the current segment of the opening watch (CorsixTH's <c>tick_timer</c>).</summary>
    private int OpenTickTimer { get; set; } = OpeningTickRate;

    /// <summary>
    /// Whether the opening watch waits for the tutorial: CorsixTH's <c>World:onTick</c> starts no "initial_opening"
    /// watch while <c>ui.start_tutorial</c> is set, and the tutorial's last chapter starts one afresh.
    /// </summary>
    public bool IsOpeningWatchHeld { get; private set; }

    /// <summary>Hours until the hospital opens by itself; 0 once open.</summary>
    public int HoursUntilOpen => IsOpen ? 0 : OpenTimer < 0 ? 1 : OpenTickTimer + 1 + (OpenTimer * (OpeningTickRate + 1)) + 1;

    /// <summary>The hospital's research (CorsixTH's <c>research</c>).</summary>
    public Research.ResearchDepartment Research { get; internal set; } = new();

    /// <summary>Casebook entries made so far, by disease id.</summary>
    public IReadOnlyDictionary<string, DiseaseCase> Casebook => _casebook;

    /// <summary>What insurance company <paramref name="company"/> (1 to 3) owes in all.</summary>
    public long InsuranceOwed(int company)
    {
        long owed = 0;
        for (var month = 0; month < InsuranceMonths; month++)
        {
            owed += _insuranceOwed[company - 1, month];
        }

        return owed;
    }

    /// <summary>
    /// The casebook entry for a disease, made on first use: discovered if the level makes it known; a disease cured in
    /// the pharmacy gets the level's drug rating and cost (<c>gbv.StartRating</c>, <c>gbv.StartCost</c>).
    /// </summary>
    public DiseaseCase CaseOf(DiseaseDefinition disease, DiseaseRules rules)
    {
        if (!_casebook.TryGetValue(disease.Id, out var entry))
        {
            entry = NewCase(disease, rules);
            _casebook.Add(disease.Id, entry);
        }

        return entry;
    }

    /// <summary>
    /// The casebook entry for a disease as <see cref="CaseOf"/> gives it, without making one: for windows, which must not
    /// change the game. An entry not made yet is a fresh one, not kept.
    /// </summary>
    public DiseaseCase PeekCase(DiseaseDefinition disease, DiseaseRules rules) =>
        _casebook.TryGetValue(disease.Id, out var entry) ? entry : NewCase(disease, rules);

    private static DiseaseCase NewCase(DiseaseDefinition disease, DiseaseRules rules)
    {
        var entry = new DiseaseCase(disease.Id, rules.IsKnown(disease.ExpertiseId)) { Reputation = disease.IsPseudo ? null : AverageReputation };
        if (disease.TreatmentRooms is ["pharmacy", ..])
        {
            entry.CureEffectiveness = rules.DrugStartRating;
            entry.DrugCost = rules.DrugStartCost;
            entry.DrugAvailable = rules.IsKnown(disease.ExpertiseId);
        }

        return entry;
    }

    /// <summary>
    /// Sets the price of a disease or diagnosis the hospital knows, held to <see cref="DiseaseCase.MinPrice"/> to
    /// <see cref="DiseaseCase.MaxPrice"/> percent; one it does not know is not in the casebook to set.
    /// </summary>
    /// <returns>Whether the price was set.</returns>
    public bool SetTreatmentPrice(DiseaseDefinition disease, DiseaseRules rules, int percent)
    {
        var entry = CaseOf(disease, rules);
        if (!entry.Discovered)
        {
            return false;
        }

        entry.PricePercent = Math.Clamp(percent, DiseaseCase.MinPrice, DiseaseCase.MaxPrice);
        return true;
    }

    /// <summary>
    /// The price a patient expects to pay: the level's <c>StartPrice</c>, and with a reputation for the disease (the
    /// hospital's for a diagnosis) above 500 ⌊StartPrice × reputation / 500⌋. Unlike CorsixTH's, prices do not rise with
    /// inflation.
    /// </summary>
    public int ExpectedPrice(DiseaseDefinition disease, DiseaseRules rules)
    {
        // Peeking, so that working out a price (also for a window) does not add to the casebook.
        var entry = PeekCase(disease, rules);
        var start = (long)rules.CurePrice(disease);
        var reputation = entry.Reputation ?? Reputation;
        return (int)Math.Max(start, start * reputation / AverageReputation);
    }

    /// <summary>The price the hospital charges: ⌊<see cref="ExpectedPrice"/> × the player's percentage / 100⌋.</summary>
    public int TreatmentPrice(DiseaseDefinition disease, DiseaseRules rules)
    {
        var entry = PeekCase(disease, rules);
        return (int)((long)ExpectedPrice(disease, rules) * entry.PricePercent / DiseaseCase.StandardPrice);
    }

    /// <summary>
    /// The object the player has picked up off the floor and not yet put down (CorsixTH's object made invisible while
    /// <c>picked_up</c>), or null; nobody uses it meanwhile.
    /// </summary>
    public Objects.CarriedObject? Carried { get; internal set; }

    /// <summary>
    /// Takes over the money of an imported save: what building the hospital up again cost is forgotten, and the
    /// balance and loan are the save's.
    /// </summary>
    internal void ImportFinances(long balance, long loan)
    {
        Balance = balance;
        Loan = Math.Max(0, loan);
        _transactions.Clear();
        MoneyInThisMonth = 0;
        MoneyOutThisMonth = 0;
    }

    /// <summary>
    /// Takes over an imported save's records: totals, reputation and value, the graphs' monthly figures (oldest first)
    /// and the bank statement (oldest first; the latest <see cref="LogLength"/> are kept).
    /// </summary>
    internal void ImportRecords(int visitors, int cured, int deaths, int reputation, long value, IReadOnlyList<MonthStatistics> months, IReadOnlyList<Transaction> statement)
    {
        Visitors = visitors;
        Cured = cured;
        Deaths = deaths;

        // The save keeps only the reputation; its visitors weigh it against what is to come.
        ReputationVisits = StartReputationVisits + Math.Max(0, visitors);
        ReputationScore = (long)Math.Clamp(reputation, MinReputation, MaxReputation) * ReputationVisits;
        foreach (var entry in _casebook.Values)
        {
            if (entry.ReputationVisits != 0)
            {
                // Its own reputation is not known; the hospital's stands for it.
                entry.ReputationVisits = StartReputationVisits + Math.Max(0, entry.Recoveries + entry.Fatalities + entry.TurnedAway);
                entry.ReputationScore = (long)Reputation * entry.ReputationVisits;
            }
        }

        Value = value;
        if (months.Count > 0)
        {
            _statistics.Clear();
            _statistics.AddRange(months);
        }

        foreach (var transaction in statement)
        {
            Log(transaction);
        }
    }

    /// <summary>Pays money out, except in free build mode (<c>spendMoney</c>).</summary>
    internal void Spend(long tick, int amount, TransactionKind kind, string subject)
    {
        if (FreeBuild)
        {
            return;
        }

        Balance -= amount;
        MoneyOutThisMonth += amount;
        Log(new Transaction(tick, kind, subject, -amount, Balance));
    }

    /// <summary>Takes money in, except in free build mode (<c>receiveMoney</c>).</summary>
    internal void Receive(long tick, int amount, TransactionKind kind, string subject)
    {
        if (FreeBuild)
        {
            return;
        }

        Balance += amount;
        MoneyInThisMonth += amount;
        Log(new Transaction(tick, kind, subject, amount, Balance));
    }

    /// <summary>
    /// Pays the month's wages of all staff in one transaction, as CorsixTH's
    /// <c>Hospital:onEndMonth</c> does; nothing is logged when they come to nothing.
    /// </summary>
    internal void PayWages(long tick, int wages)
    {
        if (wages != 0)
        {
            Spend(tick, wages, TransactionKind.Wages, string.Empty);
        }
    }

    /// <summary>
    /// <c>Hospital:receiveMoneyForTreatment</c>: the patient pays, or their insurance company owes it until the end of
    /// the month its delay brings (<see cref="InsurerPaymentDelay"/>).
    /// </summary>
    /// <param name="tick">When.</param>
    /// <param name="entry">The casebook entry of what was paid for.</param>
    /// <param name="amount">What the patient agreed to pay.</param>
    /// <param name="insuranceCompany">The patient's insurance company (1 to 3), or 0 when they pay themselves.</param>
    internal void ReceiveForTreatment(long tick, DiseaseCase entry, int amount, int insuranceCompany)
    {
        if (insuranceCompany > 0)
        {
            _insuranceOwed[insuranceCompany - 1, InsurerPaymentDelay(_insurers[insuranceCompany - 1])] += amount;
        }
        else
        {
            Receive(tick, amount, TransactionKind.Treatment, entry.DiseaseId);
        }

        entry.MoneyEarned += amount;
    }

    /// <summary>
    /// The insurance part of the end of a month: each company pays in full what it owes for this month, and what it
    /// owes for later months comes a month nearer.
    /// </summary>
    internal void PayInsurance(long tick)
    {
        for (var company = 0; company < InsuranceCompanies; company++)
        {
            var payout = _insuranceOwed[company, 0];
            if (payout > 0)
            {
                Receive(tick, checked((int)payout), TransactionKind.Insurance, (company + 1).ToString(CultureInfo.InvariantCulture));
            }

            for (var month = 0; month < InsuranceMonths - 1; month++)
            {
                _insuranceOwed[company, month] = _insuranceOwed[company, month + 1];
            }

            _insuranceOwed[company, InsuranceMonths - 1] = 0;
        }
    }

    /// <summary><c>Hospital:paySupplierForDrug</c>: the drug of a cure is paid to one of five drug companies.</summary>
    internal void PayForDrug(long tick, DiseaseCase entry, int company)
    {
        if (entry.DrugCost != 0)
        {
            Spend(tick, entry.DrugCost, TransactionKind.DrugCost, company.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>A reputation from its score and visits: <c>⌊score / visits⌋</c> held to 0–1000.</summary>
    internal static int ReputationOf(long score, int visits) =>
        visits <= 0 ? AverageReputation : (int)Math.Clamp(score / visits, MinReputation, MaxReputation);

    /// <summary>
    /// A patient's outcome in the reputation, the hospital's and their disease's: the patient's visit (counted once, at the
    /// first of a complete diagnosis, a cure, death, refusing to pay or going home) and its points (+1 for a complete
    /// diagnosis, +1 for a cure, −1 for a death).
    /// </summary>
    internal void CountPatient(DiseaseCase? entry, bool visit, int points)
    {
        var visits = visit ? 1 : 0;
        ReputationVisits += visits;
        ReputationScore += (long)points * ReputationPerPoint;
        if (entry is { ReputationVisits: > 0 })
        {
            entry.ReputationVisits += visits;
            entry.ReputationScore += (long)points * ReputationPerPoint;
        }

        NoteReputation();
    }

    /// <summary>
    /// A one-off event of ±<paramref name="percent"/> % of the visits in points (<c>⌊visits × k / 100⌋</c>, cut toward
    /// zero), which moves the reputation by about 5 × k; with fewer than 100 / k visits it does nothing. Gives the change
    /// in reputation.
    /// </summary>
    internal int ChangeReputationPercent(int percent)
    {
        var before = Reputation;
        ReputationScore += PercentOfVisits(ReputationVisits, percent) * ReputationPerPoint;
        NoteReputation();
        return Reputation - before;
    }

    /// <summary>As <see cref="ChangeReputationPercent"/>, for the reputation for a disease alone.</summary>
    internal static void ChangeReputationPercent(DiseaseCase entry, int percent)
    {
        entry.ReputationScore += PercentOfVisits(entry.ReputationVisits, percent) * ReputationPerPoint;
    }

    /// <summary>
    /// Moves the reputation by <paramref name="amount"/> as it stands, whatever the visits: for the year end's awards and
    /// the free build mode's visits, whose amounts are in reputation, and the cheat. Gives the change in reputation.
    /// </summary>
    internal int ShiftReputation(int amount)
    {
        var before = Reputation;
        ReputationScore += (long)amount * ReputationVisits;
        NoteReputation();
        return Reputation - before;
    }

    /// <summary>Sets the reputation, keeping the visits it weighs against (the cheat, tests).</summary>
    internal void SetReputation(int reputation)
    {
        ReputationScore = (long)Math.Clamp(reputation, MinReputation, MaxReputation) * ReputationVisits;
        NoteReputation();
    }

    private static long PercentOfVisits(int visits, int percent) => (long)visits * percent / 100;

    private void NoteReputation() => LowestReputationThisYear = Math.Min(LowestReputationThisYear, Reputation);

    /// <summary>
    /// <c>Hospital:updateNotCuredCounts</c>: a patient went home without a cure, sent home (<see cref="ReputationChange.Kicked"/>)
    /// or because of the price (<see cref="ReputationChange.OverPriced"/>). Unlike CorsixTH, as in the original game, the
    /// price itself does not change the reputation.
    /// </summary>
    internal void UpdateNotCuredCounts(ReputationChange reason, DiseaseCase entry)
    {
        NotCured++;
        NotCuredThisYear++;
        if (reason == ReputationChange.Kicked)
        {
            entry.TurnedAway++;
        }

        if (reason == ReputationChange.OverPriced)
        {
            NotPaying++;
            LastNotPayingDisease = entry.DiseaseId;
        }
    }

    /// <summary>
    /// The month's price level: Σ prices × 100 / Σ expected prices (<see cref="TreatmentPrice"/>, <see cref="ExpectedPrice"/>)
    /// over what the hospital can treat or diagnose now, 100 when it can do nothing, held to 0 to 300. Above 150 the
    /// adviser says prices are too high, below 60 (but not at 0) too low, naming the casebook entry furthest that way.
    /// </summary>
    internal void UpdatePriceLevel(DiseaseRules rules)
    {
        long prices = 0;
        long expected = 0;
        DiseaseCase? dearest = null;
        DiseaseCase? cheapest = null;
        foreach (var disease in DiseaseCatalog.All)
        {
            if (!IsAvailableProcedure(disease, rules))
            {
                continue;
            }

            var entry = PeekCase(disease, rules);
            prices += TreatmentPrice(disease, rules);
            expected += ExpectedPrice(disease, rules);
            if (dearest is null || entry.PricePercent > dearest.PricePercent)
            {
                dearest = entry;
            }

            if (cheapest is null || entry.PricePercent < cheapest.PricePercent)
            {
                cheapest = entry;
            }
        }

        PriceLevel = expected > 0 ? (int)Math.Clamp(prices * 100 / expected, 0, MaxPriceLevel) : DiseaseCase.StandardPrice;
        if (PriceLevel > HighPriceLevel && dearest is not null)
        {
            AdvisePrice(PriceJudgment.Over, dearest);
        }
        else if (PriceLevel is > 0 and < LowPriceLevel && cheapest is not null)
        {
            AdvisePrice(PriceJudgment.Under, cheapest);
        }
    }

    /// <summary>
    /// Whether the hospital can do it now: a diagnosis whose room it has, or a disease of the level it knows whose
    /// treatment rooms it has all.
    /// </summary>
    private bool IsAvailableProcedure(DiseaseDefinition disease, DiseaseRules rules)
    {
        if (disease.IsPseudo)
        {
            return disease.Id.StartsWith(DiseaseCatalog.DiagnosisPrefix, StringComparison.Ordinal)
                && IsRoomDiscovered(disease.Id[DiseaseCatalog.DiagnosisPrefix.Length..]);
        }

        return rules.Frequency(disease) != 0 && PeekCase(disease, rules) is { Discovered: true, DrugAvailable: true } && disease.TreatmentRooms.All(IsRoomDiscovered);
    }

    internal void AdvisePrice(PriceJudgment judgment, DiseaseCase entry)
    {
        PriceAdvices++;
        LastPriceJudgment = judgment;
        LastPriceAdviceDisease = entry.DiseaseId;
    }

    /// <summary>Borrows 5,000, or as much as allowed, if the loan may grow by 5,000 (<c>UIBankManager:increaseLoan</c>).</summary>
    internal void Borrow(long tick, bool asMuchAsAllowed)
    {
        var max = MaxLoan;
        if (Loan + LoanStep > max)
        {
            return;
        }

        var amount = asMuchAsAllowed ? max - Loan : LoanStep;
        Loan += amount;
        Receive(tick, checked((int)amount), TransactionKind.Loan, string.Empty);
    }

    /// <summary>
    /// Pays back 5,000, or with <paramref name="asMuchAsPossible"/> and more than 5,000 in the bank as much of the loan
    /// as the balance allows in steps of 5,000, if there is a loan and the money (<c>UIBankManager:decreaseLoan</c>).
    /// </summary>
    internal void Repay(long tick, bool asMuchAsPossible)
    {
        long amount = LoanStep;
        if (asMuchAsPossible && Balance > LoanStep)
        {
            amount = Math.Min(Loan, Balance / LoanStep * LoanStep);
        }

        if (Loan > 0 && Balance >= amount)
        {
            Loan -= amount;
            Spend(tick, checked((int)amount), TransactionKind.LoanRepayment, string.Empty);
        }
    }

    /// <summary>
    /// The end of <c>Hospital:onEndMonth</c>, after the bills and insurance: the month's figures go into
    /// <see cref="Statistics"/> and the month's money in and out start afresh.
    /// </summary>
    internal void RecordMonth(int wages)
    {
        _statistics.Add(new MonthStatistics(MoneyInThisMonth, MoneyOutThisMonth, wages, Balance, Visitors, Cured, Deaths, Reputation));
        MoneyInThisMonth = 0;
        MoneyOutThisMonth = 0;
    }

    /// <summary>
    /// <c>UITownMap:increaseHeat</c> and <c>decreaseHeat</c>: sets the radiators' heat, held to
    /// <see cref="MinRadiatorHeat"/>–<see cref="MaxRadiatorHeat"/> tenths, unless the boiler is broken.
    /// </summary>
    /// <returns>Whether the heat was set.</returns>
    public bool SetRadiatorHeat(int tenths)
    {
        if (IsHeatingBroken)
        {
            return false;
        }

        RadiatorHeat = Math.Clamp(tenths, MinRadiatorHeat, MaxRadiatorHeat);
        return true;
    }

    /// <summary>
    /// <c>Hospital:daysUntilNextDisaster</c>: 300, 200 or 150 days by difficulty, give or take 10.
    /// </summary>
    internal void ScheduleNextDisaster(SimRandom random, Difficulty difficulty) =>
        DisasterlessDays = DisasterFreeDays[(int)difficulty - 1] + random.NextInclusive(1, 21) - 11;

    /// <summary>
    /// The heating part of <c>Hospital:onEndDay</c>: work on the boiler, perhaps a disaster, and the day's share of
    /// the month's heating bill, which is the heat × 10 × the radiators × 7.5 over the month.
    /// </summary>
    /// <param name="random">The simulation's random numbers.</param>
    /// <param name="radiators">The radiators in the hospital.</param>
    /// <param name="handymen">The handymen the hospital employs.</param>
    /// <param name="daysInMonth">Days in the month that the day ends.</param>
    /// <param name="difficulty">The level's difficulty, for the days until the next disaster.</param>
    internal void OnEndDayHeating(SimRandom random, int radiators, int handymen, int daysInMonth, Difficulty difficulty = Difficulty.Medium)
    {
        FixBoiler(radiators, handymen);
        DisasterlessDays--;
        if (DisasterlessDays <= 0)
        {
            ScheduleNextDisaster(random, difficulty);

            // 1 is no disaster; the vomit wave (4) is not there in CorsixTH either.
            switch (random.NextInclusive(1, 3))
            {
                case 2:
                    BreakBoiler(random, MaxRadiatorHeat, radiators, handymen);
                    break;
                case 3:
                    BreakBoiler(random, 0, radiators, handymen);
                    break;
            }
        }

        AccruedHeating += RadiatorHeat * radiators * 7.5m / daysInMonth;
    }

    /// <summary><c>Hospital:onEndMonth</c> after the wages: the month's heating is paid, rounded, unless it rounds to nothing.</summary>
    internal void PayHeating(long tick)
    {
        var heating = RoundHalfUp(AccruedHeating);
        if (heating > 0)
        {
            Spend(tick, heating, TransactionKind.Heating, string.Empty);
            AccruedHeating = 0;
        }
    }

    /// <summary>
    /// <c>Hospital:boilerBreakdown</c>: an open hospital with radiators and too few handymen to look after them (one per
    /// 8 radiators) has its boiler break, stuck at <paramref name="brokenHeat"/>, with 10 to 30 repairs to do.
    /// </summary>
    private void BreakBoiler(SimRandom random, int brokenHeat, int radiators, int handymen)
    {
        if (!IsOpen || IsHeatingBroken || radiators == 0 || radiators <= 8 * handymen)
        {
            return;
        }

        SavedRadiatorHeat = RadiatorHeat;
        RadiatorHeat = brokenHeat;
        BoilerRepairCount = random.NextInclusive(10, 30);
        IsHeatingBroken = true;
    }

    /// <summary><c>Hospital:_fixBoiler</c>: a day's repairs, the more the more handymen there are for the radiators.</summary>
    private void FixBoiler(int radiators, int handymen)
    {
        if (!IsHeatingBroken)
        {
            return;
        }

        BoilerRepairCount -= radiators < 5 * handymen ? 3 : radiators < 8 * handymen ? 2 : 1;
        if (BoilerRepairCount <= 0)
        {
            RadiatorHeat = SavedRadiatorHeat;
            IsHeatingBroken = false;
        }
    }

    /// <summary>
    /// The end of the month, after the wages, insurance and heating: a month's interest on the loan,
    /// ⌊rate × loan / 120,000⌋ (the rate being yearly, in hundredths of a percent), and then, if the balance is below
    /// zero, twice that rate on the overdraft. Nothing is logged for an amount that rounds down to nothing.
    /// </summary>
    /// <remarks>
    /// Departs from CorsixTH, which runs the interest up day by day, rounds it, and charges the overdraft the rate plus
    /// the level's <c>OverdraftDiff</c>.
    /// </remarks>
    internal void PayInterest(long tick)
    {
        var interest = InterestRate * Loan / 120_000;
        if (interest > 0)
        {
            Spend(tick, checked((int)interest), TransactionKind.LoanInterest, string.Empty);
        }

        if (Balance < 0)
        {
            var overdraft = InterestRate * -Balance * 2 / 120_000;
            if (overdraft > 0)
            {
                Spend(tick, checked((int)overdraft), TransactionKind.Overdraft, string.Empty);
            }
        }
    }

    private static int RoundHalfUp(decimal value) => (int)decimal.Floor(value + 0.5m);

    /// <summary>
    /// Records that the hospital won or lost; the first outcome stands. A win comes with the offer of a salary for the
    /// next level (<c>World:winGame</c>): the player's salary plus a bonus of 4 to 9 %, none for a hospital that cheated.
    /// </summary>
    internal void End(EndOutcome outcome, EndGoal? lostTo, SimRandom random)
    {
        if (Outcome == EndOutcome.None && outcome != EndOutcome.None)
        {
            Outcome = outcome;
            LostTo = lostTo;
            if (outcome == EndOutcome.Won)
            {
                OfferSalary(EndConditions.FullScore, random);
            }
        }
    }

    /// <summary>
    /// The letter offering the next level, as the community's specification describes it: the salary times
    /// (score / 2 + rnd(55, 60)) %, at most <see cref="MaxPlayerSalary"/>; at a score of 100 that is 5 to 9 % more,
    /// mostly 7. A hospital that cheated gets no more than it has.
    /// </summary>
    internal void OfferSalary(int score, SimRandom random)
    {
        var percent = (score / 2) + random.Triangular(55, 60);
        SalaryOffer = Cheated ? PlayerSalary : Math.Min(MaxPlayerSalary, PlayerSalary * percent / 100);
        SalaryOffers++;
    }

    /// <summary>
    /// The player turns the next level down and plays on: the offer goes, and comes again at the end of the third, sixth
    /// or ninth month while the score stays at 100, the first of those checks skipped.
    /// </summary>
    internal void DeclineSalaryOffer()
    {
        if (Outcome != EndOutcome.Won || SalaryOffer <= 0)
        {
            return;
        }

        SalaryOffer = 0;
        IsSalaryOfferDeclined = true;
        SkipsNextSalaryOffer = true;
    }

    /// <summary>The end of March, June or September after a refusal: another offer if the score is still 100.</summary>
    internal void CheckSalaryOfferAgain(int score, SimRandom random)
    {
        if (!IsSalaryOfferDeclined || SalaryOffer > 0)
        {
            return;
        }

        if (SkipsNextSalaryOffer)
        {
            SkipsNextSalaryOffer = false;
            return;
        }

        if (score >= EndConditions.FullScore)
        {
            OfferSalary(score, random);
        }
    }

    /// <summary>
    /// The salary the player brings to a level from the one before, set before the level's first tick: it is also the
    /// start the year's raise measures its ceiling from.
    /// </summary>
    public void BringSalary(long salary)
    {
        PlayerSalary = Math.Clamp(salary, 0, MaxPlayerSalary);
        StartSalary = PlayerSalary;
    }

    /// <summary>
    /// The year end's raise, as in the original rather than CorsixTH (whose <c>Hospital:onEndMonth</c> raises the salary
    /// each month by up to <c>gbv.ScoreMaxInc</c>): the salary stays the same all year and rises on 1 January by a whole
    /// percentage, rounded down (saves of the original: 69,935 from September to December, 74,131 from 1 January). The
    /// percentage comes from the community specification's table by the level score: the first row whose score is at
    /// least it, from the ordinary column while the salary is at most <paramref name="scoreMaxIncrease"/> % of
    /// <paramref name="startSalary"/> and at most <see cref="RaiseCeilingSalary"/>, otherwise from the column above the
    /// ceiling; never above <see cref="MaxPlayerSalary"/> and never down.
    /// </summary>
    internal static long RaisedSalary(long salary, int score, long startSalary, int scoreMaxIncrease)
    {
        var row = 0;
        while (row < RaiseScores.Length && score > RaiseScores[row])
        {
            row++;
        }

        var belowCeiling = salary <= startSalary * scoreMaxIncrease / 100 && salary <= RaiseCeilingSalary;
        var percent = belowCeiling ? RaisePercents[row] : CeilingRaisePercents[row];
        return Math.Max(salary, Math.Min(MaxPlayerSalary, RaisedSalary(salary, percent)));
    }

    /// <summary>A salary raised by a whole percentage, rounded down.</summary>
    internal static long RaisedSalary(long salary, int percent) => salary * (100 + percent) / 100;

    /// <summary>The year end's raise of the player's salary by the level score (<see cref="RaisedSalary(long, int, long, int)"/>).</summary>
    internal void RaiseSalary(int score, int scoreMaxIncrease) => PlayerSalary = RaisedSalary(PlayerSalary, score, StartSalary, scoreMaxIncrease);

    /// <summary>The year's this-year counts of an imported game, which the year's awards go by.</summary>
    /// <summary>Takes over an imported save's reputation record: its score and the visits it is weighed by.</summary>
    internal void ImportReputation(long score, int visits)
    {
        ReputationVisits = Math.Max(1, visits);
        ReputationScore = Math.Clamp(score, (long)MinReputation * ReputationVisits, (long)MaxReputation * ReputationVisits);
    }

    /// <summary>Takes over what the insurance companies owe, by company (1 to 3) and months from now.</summary>
    internal void ImportInsuranceOwed(IReadOnlyList<(int Company, int MonthsAhead, long Amount)> owed)
    {
        if (owed.Count == 0)
        {
            return;
        }

        Array.Clear(_insuranceOwed);
        foreach (var (company, months, amount) in owed)
        {
            if (company is >= 1 and <= InsuranceCompanies && months is >= 0 and < InsuranceMonths && amount > 0)
            {
                _insuranceOwed[company - 1, months] += amount;
            }
        }
    }

    /// <summary>Takes over an imported save's price level of the last month's end.</summary>
    internal void ImportPriceLevel(int level) => PriceLevel = Math.Clamp(level, 0, MaxPriceLevel);

    internal void ImportYear(int visitors, int cured, int deaths, int notCured, int lowestReputation)
    {
        VisitorsThisYear = Math.Max(0, visitors);
        CuredThisYear = Math.Max(0, cured);
        DeathsThisYear = Math.Max(0, deaths);
        NotCuredThisYear = Math.Max(0, notCured);
        LowestReputationThisYear = Math.Clamp(lowestReputation, MinReputation, Reputation);
    }

    /// <summary>Lua's <c>math.round</c>, <c>floor(x + 0.5)</c>, of a part in percent.</summary>
    private static int RoundPercent(int part, int all) => (int)decimal.Floor((part * 100m / all) + 0.5m);

    /// <summary>Opens the hospital (CorsixTH's <c>Hospital:open</c>), when the watch runs down or the player clicks it.</summary>
    internal void Open() => IsOpen = true;

    /// <summary>
    /// Holds the opening watch for the tutorial, or lets it go: the watch then starts afresh, as the tutorial's last
    /// chapter opens a new <c>UIWatch</c> (game_ui.lua's tutorial_phases).
    /// </summary>
    internal void HoldOpeningWatch(bool hold)
    {
        if (IsOpen || hold == IsOpeningWatchHeld)
        {
            return;
        }

        IsOpeningWatchHeld = hold;
        if (!hold)
        {
            OpenTimer = OpeningSegments;
            OpenTickTimer = OpeningTickRate;
        }
    }

    /// <summary>
    /// One hour of the opening watch, as CorsixTH's <c>UIWatch:onWorldTick</c> for "initial_opening": each segment
    /// takes <see cref="OpeningTickRate"/> + 1 hours, and an hour after the last one the hospital opens.
    /// </summary>
    internal void TickOpeningWatch()
    {
        if (IsOpen || IsOpeningWatchHeld)
        {
            return;
        }

        if (OpenTickTimer == 0 && OpenTimer >= 0)
        {
            OpenTickTimer = OpeningTickRate;
            OpenTimer--;
        }
        else if (OpenTimer == -1)
        {
            Open();
        }
        else
        {
            OpenTickTimer--;
        }
    }

    /// <summary>
    /// The year end, after <see cref="YearEndAwards.Judge"/>: the trophies and awards are paid, money at once and
    /// reputation as a percentage of the visits (if the reputation is at its limit and does not move, $5,000 for a
    /// trophy or ±$2,500 for an award instead), and the year's counts start afresh.
    /// </summary>
    internal void OnEndYear(long tick)
    {
        var trophyMoney = 0;
        var awardMoney = 0;
        for (var i = 0; i < _lastYearAwards.Count; i++)
        {
            var award = _lastYearAwards[i];
            var trophy = YearEndAwards.IsTrophy(award.Kind);
            if (award.Reputation != 0 && !FreeBuild)
            {
                var atLimit = award.Reputation > 0 ? Reputation >= MaxReputation : Reputation <= MinReputation;
                if (atLimit)
                {
                    award = award with { Money = award.Money + (trophy ? YearEndAwards.TrophyMoneyAtLimit : Math.Sign(award.Reputation) * YearEndAwards.AwardMoneyAtLimit), Reputation = 0 };
                    _lastYearAwards[i] = award;
                }
                else
                {
                    ChangeReputationPercent(award.Reputation);
                }
            }

            if (trophy)
            {
                trophyMoney += award.Money;
            }
            else
            {
                awardMoney += award.Money;
            }
        }

        if (trophyMoney != 0)
        {
            Receive(tick, trophyMoney, TransactionKind.TrophyBonus, string.Empty);
        }

        if (awardMoney != 0)
        {
            Receive(tick, awardMoney, TransactionKind.AwardBonus, string.Empty);
        }

        Explosions = 0;
        RatShotsThisYear = 0;
        RatsKilledThisYear = 0;
        SodasSold = 0;
        VisitorsThisYear = 0;
        Vips.ThisYear = 0;
        Vips.PleasedThisYear = 0;
        Vips.ScoreSumThisYear = 0;
        CuredThisYear = 0;
        NotCuredThisYear = 0;
        DeathsThisYear = 0;
        EmergenciesThisYear = 0;
        EmergencyVictimsThisYear = 0;
        EmergencySavedThisYear = 0;
        ResearchPointsThisYear = 0;
        _awardMonths.Clear();
        LowestReputationThisYear = Reputation;
        InflationYears++;
    }

    /// <summary>The trophies and awards of the year that ends, before they are paid.</summary>
    internal void SetYearAwards(IEnumerable<YearEndAward> awards)
    {
        _lastYearAwards.Clear();
        _lastYearAwards.AddRange(awards);
    }

    /// <summary>Notes what the month that ends showed, for the year's trophies and awards.</summary>
    internal void RecordAwardMonth(AwardMonth month) => _awardMonths.Add(month);

    internal void Write(SimWriter writer)
    {
        writer.WriteInt64(Balance);
        writer.WriteInt32(_transactions.Count);
        foreach (var transaction in _transactions)
        {
            writer.WriteInt64(transaction.Hour);
            writer.WriteByte((byte)transaction.Kind);
            writer.WriteString(transaction.Subject);
            writer.WriteInt32(transaction.Amount);
            writer.WriteInt64(transaction.Balance);
        }

        writer.WriteInt32(Visitors);
        writer.WriteInt32(Cured);
        writer.WriteInt32(SodasSold);
        writer.WriteInt32(Deaths);
        writer.WriteInt32(NotCured);
        writer.WriteInt32(Reputation);
        writer.WriteInt32(InflationYears);
        writer.WriteInt64(Value);
        writer.WriteInt32(VisitorsThisYear);
        writer.WriteInt32(CuredThisYear);
        writer.WriteInt32(DeathsThisYear);
        writer.WriteInt32(NotCuredThisYear);
        writer.WriteInt32(LowestReputationThisYear);
        writer.WriteInt32(_lastYearAwards.Count);
        foreach (var award in _lastYearAwards)
        {
            writer.WriteByte((byte)award.Kind);
            writer.WriteInt32(award.Money);
            writer.WriteInt32(award.Reputation);
            writer.WriteInt32(award.Detail);
        }

        writer.WriteInt64(Loan);
        writer.WriteInt32(InterestRate);
        writer.WriteByte((byte)Outcome);
        writer.WriteBool(LostTo is not null);
        if (LostTo is { } lostTo)
        {
            writer.WriteByte((byte)lostTo.Criterion);
            writer.WriteInt32(lostTo.Group);
            writer.WriteBool(lostTo.AtLeast);
            writer.WriteInt32(lostTo.Value);
            writer.WriteInt32(lostTo.Bound);
            writer.WriteInt32(lostTo.Start);
        }

        writer.WriteInt32(_discoveredRooms.Count);
        foreach (var room in _discoveredRooms)
        {
            writer.WriteString(room);
        }

        writer.WriteInt64(MoneyInThisMonth);
        writer.WriteInt64(MoneyOutThisMonth);
        writer.WriteInt32(_statistics.Count);
        foreach (var month in _statistics)
        {
            writer.WriteInt64(month.MoneyIn);
            writer.WriteInt64(month.MoneyOut);
            writer.WriteInt32(month.Wages);
            writer.WriteInt64(month.Balance);
            writer.WriteInt32(month.Visitors);
            writer.WriteInt32(month.Cures);
            writer.WriteInt32(month.Deaths);
            writer.WriteInt32(month.Reputation);
        }

        writer.WriteBool(Cheated);
        writer.WriteInt64(PlayerSalary);
        writer.WriteInt64(SalaryOffer);
        writer.WriteInt64(StartSalary);
        writer.WriteInt32(SalaryOffers);
        writer.WriteBool(IsSalaryOfferDeclined);
        writer.WriteBool(SkipsNextSalaryOffer);
        writer.WriteByte((byte)ActiveCheats);
        writer.WriteInt64(SpawnAttempts);
        writer.WriteInt32(RadiatorHeat);
        writer.WriteBool(IsHeatingBroken);
        writer.WriteInt32(SavedRadiatorHeat);
        writer.WriteInt32(BoilerRepairCount);
        writer.WriteInt32(DisasterlessDays);
        writer.WriteDecimal(AccruedHeating);
        writer.WriteBool(IsOpen);
        writer.WriteInt32(OpenTimer);
        writer.WriteInt32(OpenTickTimer);
        Policies.Write(writer);
        Vips.Write(writer);
        Emergencies.Write(writer);
        Epidemics.Write(writer);
        writer.WriteInt32(_ratholes.Count);
        foreach (var hole in _ratholes)
        {
            Rat.WriteRathole(writer, hole);
        }
        for (var company = 0; company < InsuranceCompanies; company++)
        {
            for (var month = 0; month < InsuranceMonths; month++)
            {
                writer.WriteInt64(_insuranceOwed[company, month]);
            }
        }

        writer.WriteInt32(_casebook.Count);
        foreach (var entry in _casebook.Values)
        {
            writer.WriteString(entry.DiseaseId);
            writer.WriteInt32(entry.PricePercent);
            writer.WriteBool(entry.Discovered);
            writer.WriteInt64(entry.MoneyEarned);
            writer.WriteInt32(entry.CureEffectiveness);
            writer.WriteInt32(entry.DrugCost);
            writer.WriteInt32(entry.Recoveries);
            writer.WriteInt32(entry.Fatalities);
            writer.WriteBool(entry.Reputation is not null);
            writer.WriteInt32(entry.Reputation ?? 0);
            writer.WriteInt32(entry.TurnedAway);
            writer.WriteBool(entry.DrugAvailable);
        }

        Research.Write(writer);
        writer.WriteInt32(AutopsiesDiscovered);
        writer.WriteBool(Rebuild is not null);
        Rebuild?.Write(writer);
        writer.WriteBool(Littered);
        writer.WriteBool(HandymanPrioritiesChanged);
        writer.WriteBool(DidItOnFloor);
        writer.WriteBool(IsOpeningWatchHeld);
        writer.WriteInt32(Explosions);
        writer.WriteInt32(PriceAdvices);
        writer.WriteByte((byte)LastPriceJudgment);
        writer.WriteString(LastPriceAdviceDisease);
        writer.WriteInt32(NotPaying);
        writer.WriteString(LastNotPayingDisease);
        writer.WriteInt32(DiseaseReports.Count);
        foreach (var report in DiseaseReports)
        {
            writer.WriteString(report.DiseaseId);
            writer.WriteInt64(report.Expires);
        }

        writer.WriteBool(Epidemics.Report is not null);
        if (Epidemics.Report is { } result)
        {
            writer.WriteString(result.DiseaseId);
            writer.WriteByte((byte)result.Outcome);
            writer.WriteInt32(result.Amount);
            writer.WriteInt64(result.Expires);
        }

        foreach (var insurer in _insurers)
        {
            writer.WriteByte((byte)insurer);
        }

        writer.WriteBool(LastPoach is not null);
        if (LastPoach is { } poach)
        {
            writer.WriteInt32(poach.StaffId);
            writer.WriteBool(poach.Listened);
            writer.WriteInt64(poach.Tick);
        }

        writer.WriteBool(Carried is not null);
        if (Carried is { } carried)
        {
            writer.WriteString(carried.ObjectType);
            writer.WriteTile(carried.Tile);
            writer.WriteByte((byte)carried.Orientation);
            writer.WriteBool(carried.Plant is not null);
            carried.Plant?.Write(writer);
        }

        writer.WriteString(Name);
        writer.WriteInt64(ReputationScore);
        writer.WriteInt32(ReputationVisits);
        foreach (var entry in _casebook.Values)
        {
            writer.WriteInt64(entry.ReputationScore);
            writer.WriteInt32(entry.ReputationVisits);
        }

        writer.WriteInt32(PriceLevel);
        writer.WriteBool(LastLitterTile is not null);
        if (LastLitterTile is { } lastLitter)
        {
            writer.WriteTile(lastLitter);
        }

        writer.WriteInt32(RatShotsThisYear);
        writer.WriteInt32(RatsKilledThisYear);
        writer.WriteInt32(PopulationPercent);
        writer.WriteInt32(EmergenciesThisYear);
        writer.WriteInt32(EmergencyVictimsThisYear);
        writer.WriteInt32(EmergencySavedThisYear);
        writer.WriteInt32(ResearchPointsThisYear);
        writer.WriteInt32(_awardMonths.Count);
        foreach (var month in _awardMonths)
        {
            writer.WriteInt32(month.Reputation);
            writer.WriteInt32(month.StaffHappiness);
            writer.WriteInt32(month.PatientHappiness);
            writer.WriteInt32(month.PlantsWatered);
            writer.WriteInt32(month.Waiting);
        }

        NetMission.Write(writer);
    }

    internal static Hospital Read(SimReader reader, int player)
    {
        var hospital = new Hospital(player, reader.ReadInt64());
        var count = reader.ReadCount(LogLength);
        for (var i = 0; i < count; i++)
        {
            var tick = reader.ReadInt64();
            var kind = reader.ReadByte();
            if (kind > (byte)TransactionKind.PersonalBonus)
            {
                throw new InvalidDataException($"Unknown transaction kind {kind}.");
            }

            hospital._transactions.Add(new Transaction(tick, (TransactionKind)kind, reader.ReadString(), reader.ReadInt32(), reader.ReadInt64()));
        }

        hospital.Visitors = reader.ReadInt32();
        hospital.Cured = reader.ReadInt32();
        hospital.SodasSold = reader.ReadInt32();
        hospital.Deaths = reader.ReadInt32();
        hospital.NotCured = reader.ReadInt32();
        var reputation = reader.ReadInt32();
        hospital.InflationYears = reader.ReadInt32();
        hospital.Value = reader.ReadInt64();
        hospital.VisitorsThisYear = reader.ReadInt32();
        hospital.CuredThisYear = reader.ReadInt32();
        hospital.DeathsThisYear = reader.ReadInt32();
        hospital.NotCuredThisYear = reader.ReadInt32();
        hospital.LowestReputationThisYear = reader.ReadInt32();
        var awards = reader.ReadCount(MaxYearEndAwards);
        for (var i = 0; i < awards; i++)
        {
            var kind = reader.ReadByte();
            if (kind > (byte)YearEndAwardKind.NeglectedMachines)
            {
                throw new InvalidDataException($"Unknown year end award {kind}.");
            }

            // Version 113 keeps a detail, and the reputation as a percentage (before, it was in points).
            hospital._lastYearAwards.Add(new((YearEndAwardKind)kind, reader.ReadInt32(), reader.ReadInt32(), reader.Version is 0 or >= 113 ? reader.ReadInt32() : 0));
        }

        hospital.Loan = reader.ReadInt64();
        hospital.InterestRate = reader.ReadInt32();
        if (reader.Version is not 0 and < 109)
        {
            // The overdraft differential and the interest run up this month, which version 109 no longer has.
            reader.ReadInt32();
            reader.ReadDecimal();
            reader.ReadDecimal();
        }
        var outcome = reader.ReadByte();
        if (outcome > (byte)EndOutcome.Lost)
        {
            throw new InvalidDataException($"Unknown outcome {outcome}.");
        }

        hospital.Outcome = (EndOutcome)outcome;
        if (reader.ReadBool())
        {
            hospital.LostTo = new EndGoal((EndCriterion)reader.ReadByte(), reader.ReadInt32(), reader.ReadBool(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }

        var rooms = reader.ReadCount(MaxCases);
        for (var i = 0; i < rooms; i++)
        {
            hospital._discoveredRooms.Add(reader.ReadString());
        }

        hospital.MoneyInThisMonth = reader.ReadInt64();
        hospital.MoneyOutThisMonth = reader.ReadInt64();
        hospital._statistics.Clear();
        var months = reader.ReadCount(MaxStatisticsMonths);
        for (var i = 0; i < months; i++)
        {
            hospital._statistics.Add(new MonthStatistics(
                reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
        }

        hospital.Cheated = reader.ReadBool();
        hospital.PlayerSalary = reader.ReadInt64();
        hospital.SalaryOffer = reader.ReadInt64();
        if (reader.Version is 0 or >= 110)
        {
            hospital.StartSalary = reader.ReadInt64();
            hospital.SalaryOffers = reader.ReadInt32();
            hospital.IsSalaryOfferDeclined = reader.ReadBool();
            hospital.SkipsNextSalaryOffer = reader.ReadBool();
        }
        else
        {
            hospital.SalaryOffers = hospital.SalaryOffer > 0 ? 1 : 0;
        }

        var cheats = reader.ReadByte();
        hospital.ActiveCheats = cheats <= (byte)ToggleCheats.All ? (ToggleCheats)cheats : throw new InvalidDataException($"Unknown toggle cheats {cheats}.");
        hospital.SpawnAttempts = reader.ReadInt64();
        hospital.RadiatorHeat = reader.ReadInt32();
        hospital.IsHeatingBroken = reader.ReadBool();
        hospital.SavedRadiatorHeat = reader.ReadInt32();
        hospital.BoilerRepairCount = reader.ReadInt32();
        hospital.DisasterlessDays = reader.ReadInt32();
        hospital.AccruedHeating = reader.ReadDecimal();
        hospital.IsOpen = reader.ReadBool();
        hospital.OpenTimer = reader.ReadInt32();
        hospital.OpenTickTimer = reader.ReadInt32();
        hospital.Policies = HospitalPolicies.Read(reader);
        hospital.Vips = VipVisits.Read(reader);
        hospital.Emergencies = HospitalEmergencies.Read(reader);
        hospital.Epidemics = HospitalEpidemics.Read(reader);
        var holes = reader.ReadCount(MaxRatholes);
        for (var i = 0; i < holes; i++)
        {
            hospital._ratholes.Add(Rat.ReadRathole(reader));
        }
        for (var company = 0; company < InsuranceCompanies; company++)
        {
            if (reader.Version is 0 or >= 106)
            {
                for (var month = 0; month < InsuranceMonths; month++)
                {
                    hospital._insuranceOwed[company, month] = reader.ReadInt64();
                }
            }
            else
            {
                // CorsixTH's months: this month's, last month's and the month before's, which is paid at this month's end.
                for (var month = 0; month < OldInsuranceMonths; month++)
                {
                    hospital._insuranceOwed[company, OldInsuranceMonths - 1 - month] = reader.ReadInt64();
                }
            }
        }

        var cases = reader.ReadCount(MaxCases);
        for (var i = 0; i < cases; i++)
        {
            var id = reader.ReadString();
            var entry = new DiseaseCase(id, discovered: false) { PricePercent = reader.ReadInt32() };
            entry.Discovered = reader.ReadBool();
            entry.MoneyEarned = reader.ReadInt64();
            entry.CureEffectiveness = reader.ReadInt32();
            entry.DrugCost = reader.ReadInt32();
            entry.Recoveries = reader.ReadInt32();
            entry.Fatalities = reader.ReadInt32();
            var hasReputation = reader.ReadBool();
            var diseaseReputation = reader.ReadInt32();
            entry.Reputation = hasReputation ? diseaseReputation : null;
            entry.TurnedAway = reader.ReadInt32();

            // Version 112 researches drugs; older games keep every drug they had.
            entry.DrugAvailable = reader.Version is not 0 and < 112 || reader.ReadBool();
            if (DiseaseCatalog.Find(id) is { } disease && !OpenWards.Sim.Research.ResearchDepartment.IsDrugDisease(disease))
            {
                // Games imported before the fix took the original's 0 for these, and their patients all died.
                entry.CureEffectiveness = DiseaseCase.FullEffectiveness;
            }

            if (!hospital._casebook.TryAdd(id, entry))
            {
                throw new InvalidDataException($"Casebook entry '{id}' is given twice.");
            }
        }

        hospital.Research = Sim.Research.ResearchDepartment.Read(reader);
        hospital.AutopsiesDiscovered = reader.Version is 0 or >= 75 ? reader.ReadInt32() : 0;
        hospital.Rebuild = reader.Version is 0 or >= 76 && reader.ReadBool() ? Rooms.RoomRebuild.Read(reader) : null;
        hospital.Littered = reader.Version is 0 or >= 77 ? reader.ReadBool() : false;
        hospital.HandymanPrioritiesChanged = reader.Version is 0 or >= 78 ? reader.ReadBool() : false;
        hospital.DidItOnFloor = reader.Version is 0 or >= 79 ? reader.ReadBool() : false;
        hospital.IsOpeningWatchHeld = reader.Version is 0 or >= 85 && reader.ReadBool();
        hospital.Explosions = reader.Version is 0 or >= 86 ? reader.ReadInt32() : 0;
        if (reader.Version is 0 or >= 90)
        {
            hospital.PriceAdvices = reader.ReadInt32();
            var judgment = reader.ReadByte();
            hospital.LastPriceJudgment = judgment <= (byte)PriceJudgment.Fair ? (PriceJudgment)judgment : throw new InvalidDataException($"Unknown price judgment {judgment}.");
            hospital.LastPriceAdviceDisease = reader.ReadString();
            hospital.NotPaying = reader.ReadInt32();
            hospital.LastNotPayingDisease = reader.ReadString();
        }

        if (reader.Version is 0 or >= 93)
        {
            var reports = reader.ReadCount(Diseases.DiseaseCatalog.All.Count);
            for (var i = 0; i < reports; i++)
            {
                hospital.DiseaseReports.Add(new DiseaseReport(reader.ReadString(), reader.ReadInt64()));
            }

            if (reader.ReadBool())
            {
                var diseaseId = reader.ReadString();
                var ended = reader.ReadByte();
                hospital.Epidemics.Report = new Disasters.EpidemicReport(
                    diseaseId,
                    ended <= (byte)Disasters.EpidemicOutcome.FinedWithReputation ? (Disasters.EpidemicOutcome)ended : throw new InvalidDataException($"Unknown epidemic outcome {ended}."),
                    reader.ReadInt32(),
                    reader.ReadInt64());
            }
        }

        if (reader.Version is 0 or >= 95)
        {
            for (var i = 0; i < InsuranceCompanies; i++)
            {
                var insurer = reader.ReadByte();
                hospital._insurers[i] = insurer is >= 1 and <= OldInsuranceCompanyNames ? insurer : throw new InvalidDataException($"Unknown insurance company {insurer}.");
            }

            hospital.KeepInsurersKnown();
        }

        if ((reader.Version is 0 or >= 97) && reader.ReadBool())
        {
            hospital.LastPoach = new PoachAnswer(reader.ReadInt32(), reader.ReadBool(), reader.ReadInt64());
        }

        if ((reader.Version is 0 or >= 98) && reader.ReadBool())
        {
            var type = reader.ReadString();
            var tile = reader.ReadTile();
            var facing = reader.ReadByte();
            hospital.Carried = new Objects.CarriedObject(
                type,
                tile,
                facing <= (byte)Direction.West ? (Direction)facing : throw new InvalidDataException($"Unknown direction {facing}."),
                reader.ReadBool() ? Objects.PlantState.Read(reader) : null);
        }

        // Version 104 keeps the name the hospital goes by.
        if (reader.Version is 0 or >= 104)
        {
            hospital.Name = reader.ReadString(MaxNameLength);
        }

        // Version 105 keeps the reputation's score and visits; before, only the reputation was kept, which is weighed
        // against the patients who had come to an end so far.
        if (reader.Version is 0 or >= 105)
        {
            hospital.ReputationScore = reader.ReadInt64();
            hospital.ReputationVisits = ReadReputationVisits(reader);
            foreach (var entry in hospital._casebook.Values)
            {
                entry.ReputationScore = reader.ReadInt64();
                entry.ReputationVisits = ReadReputationVisits(reader);
            }
        }
        else
        {
            hospital.ReputationVisits = StartReputationVisits + hospital.Cured + hospital.Deaths + hospital.NotCured;
            hospital.ReputationScore = (long)Math.Clamp(reputation, MinReputation, MaxReputation) * hospital.ReputationVisits;
            foreach (var entry in hospital._casebook.Values)
            {
                if (entry.Reputation is { } diseaseReputation)
                {
                    entry.ReputationVisits = StartReputationVisits + entry.Recoveries + entry.Fatalities + entry.TurnedAway;
                    entry.ReputationScore = (long)diseaseReputation * entry.ReputationVisits;
                }
            }
        }

        // Version 106 keeps the month's price level.
        if (reader.Version is 0 or >= 106)
        {
            hospital.PriceLevel = Math.Clamp(reader.ReadInt32(), 0, MaxPriceLevel);
        }

        // Version 109 keeps the last littered tile and the year's shots at rats.
        if (reader.Version is 0 or >= 109)
        {
            hospital.LastLitterTile = reader.ReadBool() ? reader.ReadTile() : null;
            hospital.RatShotsThisYear = Math.Max(0, reader.ReadInt32());
            hospital.RatsKilledThisYear = Math.Max(0, reader.ReadInt32());
        }

        // Version 113 keeps what the year's trophies and awards look at.
        if (reader.Version is 0 or >= 113)
        {
            hospital.PopulationPercent = reader.ReadInt32();
            hospital.EmergenciesThisYear = reader.ReadInt32();
            hospital.EmergencyVictimsThisYear = reader.ReadInt32();
            hospital.EmergencySavedThisYear = reader.ReadInt32();
            hospital.ResearchPointsThisYear = reader.ReadInt32();
            var awardMonths = reader.ReadCount(MaxAwardMonths);
            for (var i = 0; i < awardMonths; i++)
            {
                hospital._awardMonths.Add(new AwardMonth(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
            }
        }

        // Version 118 keeps the network mission.
        if (reader.Version is 0 or >= 118)
        {
            hospital.NetMission = NetMission.Read(reader);
        }

        return hospital;
    }

    private static int ReadReputationVisits(SimReader reader)
    {
        var visits = reader.ReadInt32();
        return visits >= 0 ? visits : throw new InvalidDataException($"Reputation visits {visits} are negative.");
    }

    private void Log(Transaction transaction)
    {
        _transactions.Insert(0, transaction);
        if (_transactions.Count > LogLength)
        {
            _transactions.RemoveAt(_transactions.Count - 1);
        }
    }
}

/// <summary>A fax telling of a disease newly discovered, until <see cref="Expires"/>.</summary>
public readonly record struct DiseaseReport(string DiseaseId, long Expires);

/// <summary>A member of staff's answer to another hospital's offer: whether they listened, and when.</summary>
public sealed record PoachAnswer(int StaffId, bool Listened, long Tick);
