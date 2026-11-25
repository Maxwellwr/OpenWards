// Research as the community's logic specification describes the original game: points for each action in the research
// department, divided once a day by the five sliders, the cheapest item of each area researched, improvements of drugs
// and machines, autopsies. Parts kept from CorsixTH CorsixTH/Lua/research_department.lua and hospital.lua: which objects
// a level researches, which rooms a discovery makes buildable, automatic discovery by WhenAvail, getObjectBuildCost,
// canConcentrateResearch and the research window's adjustResearch.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Research;

/// <summary>What a research area works on.</summary>
public enum ResearchTargetKind : byte
{
    /// <summary>Nothing to work on now; the area's slider is off.</summary>
    None = 0,

    /// <summary>An object to discover or improve; <see cref="ResearchTarget.Id"/> is the object's id.</summary>
    Object = 2,

    /// <summary>A drug to improve; <see cref="ResearchTarget.Id"/> is the disease's id.</summary>
    Drug = 3,
}

/// <summary>What a research area works on.</summary>
public readonly record struct ResearchTarget(ResearchTargetKind Kind, string Id)
{
    public static ResearchTarget None => new(ResearchTargetKind.None, string.Empty);

    public static ResearchTarget Object(string id) => new(ResearchTargetKind.Object, id);

    public static ResearchTarget Drug(string id) => new(ResearchTargetKind.Drug, id);

    public bool IsNone => Kind == ResearchTargetKind.None;
}

/// <summary>Research on an object type: its discovery, its improvements and what new ones are like.</summary>
public sealed class ObjectResearch
{
    /// <summary>Points gathered towards the next discovery or improvement.</summary>
    public int Points { get; internal set; }

    /// <summary>How many uses a new one takes before it is worn out, raised by improvements.</summary>
    public int StartStrength { get; internal set; }

    /// <summary>What a new one costs now, lowered by improvements.</summary>
    public int Cost { get; internal set; }

    public bool Discovered { get; internal set; }

    /// <summary>Improvements so far, at most <see cref="ResearchDepartment.MaxImprovements"/>.</summary>
    public int Improvements { get; internal set; }
}

/// <summary>One area of the research policy: its slider and what it works on.</summary>
public sealed class ResearchArea
{
    /// <summary>The area's slider, in percent of the pool it takes each day.</summary>
    public int Share { get; internal set; }

    /// <summary>The area's cheapest item, chosen again each day; none when there is nothing to work on.</summary>
    public ResearchTarget Current { get; internal set; } = ResearchTarget.None;
}

/// <summary>
/// A hospital's research. Each action of a researcher in the research department adds points to a pool. Once a day the
/// areas, in the research window's order, each take their slider's percent of what is left in the pool, and put it into
/// the cheapest item they can work on; an item whose points reach its cost is discovered or improved. A cure or diagnosis
/// area with nothing to work on gives its points back and its slider goes off; a drugs or improvements area gives them
/// back and keeps its slider; the specialisation area without a disease loses its share. Research costs no money.
/// </summary>
/// <remarks>
/// Where it departs from CorsixTH: CorsixTH gives each researcher points every day and for each use of a desk, computer
/// or analyser, divides them by <c>ResearchPointsDivisor</c>, puts them into each area's current item at once with a
/// random 0.75–1.25 factor, charges $3 a day per researcher, picks the least effective drug and the weakest machine
/// rather than the cheapest item, improves a drug's cost by a tenth down to <c>MinDrugCost</c> and alternates a machine's
/// strength and cost improvements; and it concentrates research on a disease by itself.
/// </remarks>
public sealed class ResearchDepartment
{
    /// <summary>The most improvements a machine type gets.</summary>
    public const int MaxImprovements = 20;

    /// <summary>The points of one action at a desk.</summary>
    public const int DeskPoints = 50;

    /// <summary>The points of one action at a computer or an analyser.</summary>
    public const int MachinePoints = 100;

    /// <summary>The lowest a drug's cost gets by research, in dollars.</summary>
    public const int MinDrugCost = 10;

    /// <summary>A new area's slider, in percent.</summary>
    private const int StartShare = 20;

    private const int MaxEntries = 1024;

    private static readonly ResearchCategory[] Order =
        [ResearchCategory.Cure, ResearchCategory.Diagnosis, ResearchCategory.Drugs, ResearchCategory.Improvements, ResearchCategory.Specialisation];

    private readonly SortedDictionary<string, ObjectResearch> _objects = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _drugs = new(StringComparer.Ordinal);
    private readonly ResearchArea[] _areas = [new(), new(), new(), new(), new()];

    /// <summary>A department with nothing to research.</summary>
    internal ResearchDepartment()
    {
    }

    /// <summary>Points the researchers produced that no area has taken yet.</summary>
    public int Pool { get; private set; }

    /// <summary>
    /// How many times the day's research switched the diagnosis slider off with nothing left to discover, for the
    /// adviser. The original game's message for the cure area is lost (its category number 0 reads as an empty adviser
    /// queue), so only diagnosis counts. Not saved: only the interface reads it, comparing it from one look to the next.
    /// </summary>
    public int DiagnosisSwitchedOff { get; private set; }

    /// <summary>The sum of the areas' sliders, in percent.</summary>
    public int Total => _areas.Sum(area => area.Share);

    /// <summary>The disease the player chose to concentrate research on, or empty.</summary>
    public string Concentrated { get; private set; } = string.Empty;

    /// <summary>The research on the objects the level has, by id.</summary>
    public IReadOnlyDictionary<string, ObjectResearch> Objects => _objects;

    /// <summary>Points gathered towards each drug's next improvement, by disease id.</summary>
    public IReadOnlyDictionary<string, int> Drugs => _drugs;

    public ResearchArea Area(ResearchCategory category) => _areas[(int)category];

    /// <summary>
    /// The level's researchable objects (CorsixTH's <c>initResearch</c>) and the drugs of its diseases; every slider starts
    /// at 20 %.
    /// </summary>
    internal static ResearchDepartment Create(Simulation simulation, Hospital hospital)
    {
        var rules = simulation.Rules;
        var research = new ResearchDepartment();
        foreach (var obj in ResearchObjects.All)
        {
            if (rules.Research.Object(obj.ObjectType) is not { ForLevel: true } entry || obj.Category is null)
            {
                continue;
            }

            research._objects[obj.Id] = new ObjectResearch
            {
                StartStrength = entry.StartStrength,
                Cost = rules.FreeBuild ? 0 : entry.StartCost,
                Discovered = entry.StartAvail,
            };
        }

        foreach (var disease in rules.Diseases.AvailableDiseases())
        {
            if (IsDrugDisease(disease))
            {
                research._drugs[disease.Id] = 0;
            }
        }

        foreach (var area in research._areas)
        {
            area.Share = StartShare;
        }

        research.RefreshTargets(simulation, hospital);
        return research;
    }

    /// <summary>CorsixTH's <c>disease.drug</c>: diseases cured in the pharmacy.</summary>
    internal static bool IsDrugDisease(DiseaseDefinition disease) => disease.TreatmentRooms is ["pharmacy", ..];

    /// <summary>
    /// The points of one action of a researcher: 50 at a desk or 100 at a computer or analyser, plus their skill, halved
    /// when they are tired and quartered when very tired.
    /// </summary>
    internal static int ActionPoints(Simulation simulation, int workPoints, int skill, decimal fatigue)
    {
        var tiredness = simulation.Rules.Tiredness;
        var level = (int)decimal.Floor(fatigue * 1000);
        var divisor = level > tiredness.VeryTired ? 4 : level > tiredness.Tired ? 2 : 1;
        return workPoints + (Math.Max(0, skill) / divisor);
    }

    /// <summary>Puts points into the pool; the areas take them at the end of the day.</summary>
    public void AddPoints(int points)
    {
        Pool = Math.Max(0, Pool + points);
    }

    /// <summary>
    /// The day's research: each area in turn takes its slider's percent of what is left in the pool for its cheapest item.
    /// With nothing to work on, the cure and diagnosis areas leave their points in the pool and their sliders go off; the
    /// drugs and improvements areas leave their points in the pool and keep their sliders; and with no disease to
    /// concentrate on, the specialisation area still takes its share, which is lost. Every share taken and not given
    /// back counts towards the year's award for research, the lost one too.
    /// </summary>
    internal void OnEndDay(Simulation simulation, Hospital hospital)
    {
        foreach (var category in Order)
        {
            var area = Area(category);
            area.Current = FindTarget(simulation, hospital, category);
            var taken = (int)((long)area.Share * Pool / 100);
            if (area.Current.IsNone)
            {
                switch (category)
                {
                    case ResearchCategory.Cure or ResearchCategory.Diagnosis:
                        if (category == ResearchCategory.Diagnosis && area.Share > 0)
                        {
                            DiagnosisSwitchedOff++;
                        }

                        area.Share = 0;
                        break;
                    case ResearchCategory.Specialisation:
                        Concentrated = string.Empty;
                        Pool -= taken;
                        hospital.ResearchDistributedThisYear += taken;
                        break;
                }

                continue;
            }

            if (taken > 0)
            {
                Pool -= taken;
                hospital.ResearchDistributedThisYear += taken;
                Progress(simulation, hospital, area.Current, taken);
            }
        }

        RefreshTargets(simulation, hospital);
    }

    /// <summary>The cheat that discovers every object the cure and diagnosis areas could research.</summary>
    internal void DiscoverAll(Simulation simulation, Hospital hospital)
    {
        foreach (var obj in ResearchObjects.All)
        {
            if (obj.Category is ResearchCategory.Cure or ResearchCategory.Diagnosis && _objects.TryGetValue(obj.Id, out var research) && !research.Discovered)
            {
                DiscoverObject(simulation, hospital, obj);
            }
        }
    }

    /// <summary>
    /// Whether an object can be bought for a room (UIEditRoom:purchaseItems): the level has it (`AvailableForLevel`) and,
    /// if it is researched, it has been discovered.
    /// </summary>
    public bool CanBuy(LevelRules rules, string objectId) =>
        rules.Availability(objectId).ForLevel && (!_objects.TryGetValue(objectId, out var research) || research.Discovered);

    /// <summary><c>Hospital:getObjectBuildCost</c>: what an object costs now; research may have lowered a machine's cost.</summary>
    public int ObjectCost(LevelRules rules, string objectId) =>
        rules.FreeBuild ? 0 : _objects.TryGetValue(objectId, out var research) ? research.Cost : rules.ObjectCost(objectId);

    /// <summary>
    /// Takes over an imported save's research: for each object type of the level what is discovered, its improvements,
    /// the strength and cost of new ones; the points put into each expertise, for the drugs and for the objects that
    /// research under an expertise (<see cref="ResearchObject.Fallback"/>); and the points not yet shared out. An object
    /// that stands in the hospital stays discovered.
    /// </summary>
    internal void Import(
        Simulation simulation,
        Hospital hospital,
        IReadOnlyList<Import.ImportedObjectResearch> objects,
        IReadOnlyList<(int Expertise, int Points)> expertise,
        int? pool)
    {
        var rules = simulation.Rules;
        var points = expertise.Where(e => e.Points > 0).GroupBy(e => e.Expertise).ToDictionary(g => g.Key, g => g.Last().Points);
        foreach (var obj in ResearchObjects.All)
        {
            if (!_objects.TryGetValue(obj.Id, out var research))
            {
                continue;
            }

            if (objects.LastOrDefault(o => o.ObjectType == obj.ObjectType) is { } imported)
            {
                if (obj.IsMachine)
                {
                    research.Improvements = Math.Clamp(imported.Improvements, 0, MaxImprovements);
                    research.StartStrength = Math.Max(1, imported.StartStrength);
                    research.Cost = rules.FreeBuild ? 0 : Math.Max(0, imported.Cost);
                }

                // Only the importing player builds on a freshly imported level.
                var placed = simulation.Entities.OfType<Entities.RoomObject>().Any(o => string.Equals(o.ObjectType, obj.Id, StringComparison.Ordinal));
                research.Discovered = imported.Discovered || placed;
            }

            research.Points = obj.Fallback > 0 && points.TryGetValue(obj.Fallback, out var objectPoints) ? objectPoints : 0;
        }

        foreach (var diseaseId in _drugs.Keys.ToList())
        {
            _drugs[diseaseId] = DiseaseCatalog.Find(diseaseId) is { } disease && points.TryGetValue(disease.ExpertiseId, out var drugPoints) ? drugPoints : 0;
        }

        if (pool is { } left)
        {
            Pool = Math.Max(0, left);
        }

        foreach (var room in simulation.Content.Rooms)
        {
            if (!hospital.IsRoomDiscovered(room.Id) && rules.IsRoomAvailable(room)
                && !LevelRules.Needed(room).Any(needed => _objects.TryGetValue(needed.ObjectId, out var research) && !research.Discovered))
            {
                hospital.DiscoverRoom(room.Id);
            }
        }

        RefreshTargets(simulation, hospital);
    }

    /// <summary>
    /// <c>ResearchDepartment:checkAutomaticDiscovery</c>, at the end of each month: equipment the level brings in this
    /// month of the game (<c>objects[n].WhenAvail</c>) is discovered by itself.
    /// </summary>
    internal void CheckAutomaticDiscovery(Simulation simulation, Hospital hospital, int monthOfGame)
    {
        foreach (var obj in ResearchObjects.All)
        {
            if (obj.IsMachine && _objects.TryGetValue(obj.Id, out var research) && !research.Discovered
                && simulation.Rules.Research.Object(obj.ObjectType) is { WhenAvail: not 0 and var month } && monthOfGame >= month)
            {
                DiscoverObject(simulation, hospital, obj);
            }
        }
    }

    /// <summary>
    /// What an item costs in its present state: an object to discover its <c>RschReqd</c> (or its fallback expertise's);
    /// an improvement ⌊<c>RschReqd</c> × (<c>RschImproveCostPercent</c> + <c>RschImproveIncrementPercent</c> × n) / 100⌋,
    /// where n is the machine's improvements so far, or for a drug its effectiveness / 25. Null when there is nothing left
    /// to do with it.
    /// </summary>
    public int? Required(Simulation simulation, Hospital hospital, ResearchTarget target)
    {
        var rules = simulation.Rules;
        switch (target.Kind)
        {
            case ResearchTargetKind.Object when ResearchObjects.Find(target.Id) is { } obj && _objects.TryGetValue(obj.Id, out var research):
                if (BaseRequired(rules, obj) is not { } points)
                {
                    return null;
                }

                if (!research.Discovered)
                {
                    return points;
                }

                return obj.IsMachine && research.Improvements < MaxImprovements ? ImprovementCost(rules, points, research.Improvements) : null;
            case ResearchTargetKind.Drug when DiseaseCatalog.Find(target.Id) is { } disease && _drugs.ContainsKey(disease.Id):
                var effectiveness = hospital.PeekCase(disease, rules.Diseases).CureEffectiveness;
                return effectiveness < DiseaseCase.FullEffectiveness
                    ? ImprovementCost(rules, rules.Research.ExpertiseRequired(disease.ExpertiseId), effectiveness / 25)
                    : null;
            default:
                return null;
        }
    }

    private static int ImprovementCost(LevelRules rules, int required, int steps) =>
        (int)((long)required * (rules.Research.ImproveCostPercent + (rules.Research.ImproveIncrementPercent * steps)) / 100);

    /// <summary>An object's <c>RschReqd</c>, or its fallback expertise's; null for none.</summary>
    private static int? BaseRequired(LevelRules rules, ResearchObject obj) =>
        rules.Research.Object(obj.ObjectType)?.ResearchRequired ?? (obj.Fallback > 0 ? rules.Research.ExpertiseRequired(obj.Fallback) : null);

    /// <summary>
    /// An autopsy: the item of the patient's disease (as <see cref="DiseaseTarget"/> finds it) gets
    /// ⌊<c>RschReqd</c> × <c>AutopsyRschPercent</c> / 100⌋ points at once.
    /// </summary>
    internal void AddPointsForAutopsy(Simulation simulation, Hospital hospital, DiseaseDefinition disease)
    {
        var target = DiseaseTarget(simulation, hospital, disease);
        int? required = target.Kind switch
        {
            ResearchTargetKind.Object => BaseRequired(simulation.Rules, ResearchObjects.Find(target.Id)!),
            ResearchTargetKind.Drug => simulation.Rules.Research.ExpertiseRequired(disease.ExpertiseId),
            _ => null,
        };
        if (required is { } points)
        {
            Progress(simulation, hospital, target, (int)((long)points * simulation.Rules.Research.AutopsyPercent / 100));
            RefreshTargets(simulation, hospital);
        }
    }

    /// <summary>Adds points to an item; once they reach its cost it is discovered or improved and its points start again from 0.</summary>
    private void Progress(Simulation simulation, Hospital hospital, ResearchTarget target, int points)
    {
        if (Required(simulation, hospital, target) is not { } required || required <= 0)
        {
            return;
        }

        switch (target.Kind)
        {
            case ResearchTargetKind.Object:
                var research = _objects[target.Id];
                research.Points += points;
                if (research.Points >= required)
                {
                    research.Points = 0;
                    var obj = ResearchObjects.Find(target.Id)!;
                    if (!research.Discovered)
                    {
                        DiscoverObject(simulation, hospital, obj);
                    }
                    else
                    {
                        ImproveMachine(simulation.Rules, obj, research);
                    }
                }

                break;
            case ResearchTargetKind.Drug:
                _drugs[target.Id] += points;
                if (_drugs[target.Id] >= required)
                {
                    _drugs[target.Id] = 0;
                    ImproveDrug(simulation, hospital, target.Id);
                }

                break;
        }
    }

    /// <summary>
    /// A drug's improvement, one of two ways at even odds. Cheaper: its cost × 95 / 100, not below $10, and one time in
    /// seven its effectiveness up by <c>DrugImproveRate</c> too; nothing if it costs $10 already. More effective: its
    /// effectiveness up by <c>DrugImproveRate</c>, to at most 100, and one time in seven its cost down by $1, not below
    /// $10; nothing if it is fully effective already.
    /// </summary>
    /// <remarks>
    /// The original game sets the cost straight to $10 in the second way when it stays above $10 after the dollar comes
    /// off, by a wrong comparison; here the dollar comes off, as intended.
    /// </remarks>
    private static void ImproveDrug(Simulation simulation, Hospital hospital, string diseaseId)
    {
        var rate = simulation.Rules.Research.DrugImproveRate;
        var entry = hospital.CaseOf(DiseaseCatalog.Find(diseaseId)!, simulation.Rules.Diseases);
        if (simulation.Random.NextInclusive(1, 2) == 1)
        {
            if (entry.DrugCost > MinDrugCost)
            {
                entry.DrugCost = Math.Max(MinDrugCost, entry.DrugCost * 95 / 100);
                if (simulation.Random.NextInclusive(1, 7) == 1)
                {
                    entry.CureEffectiveness = Math.Min(DiseaseCase.FullEffectiveness, entry.CureEffectiveness + rate);
                }
            }
        }
        else if (entry.CureEffectiveness < DiseaseCase.FullEffectiveness)
        {
            entry.CureEffectiveness = Math.Min(DiseaseCase.FullEffectiveness, entry.CureEffectiveness + rate);
            if (simulation.Random.NextInclusive(1, 7) == 1)
            {
                entry.DrugCost = Math.Max(MinDrugCost, entry.DrugCost - 1);
            }
        }
    }

    /// <summary>
    /// A machine type's improvement, in a round of three: the first and every third after it does nothing; the second
    /// raises the strength of new ones by <c>ResearchIncrement</c> while below <c>MaxObjectStrength</c>; the third takes
    /// the cost of new ones to 7/8, rounded down to $10, not below a third of the level's cost.
    /// </summary>
    private static void ImproveMachine(LevelRules rules, ResearchObject machine, ObjectResearch research)
    {
        switch (research.Improvements % 3)
        {
            case 1 when research.StartStrength < rules.Research.MaxObjectStrength:
                research.StartStrength += rules.Research.ResearchIncrement;
                break;
            case 2:
                var floor = (rules.FreeBuild ? 0 : rules.Research.Object(machine.ObjectType)?.StartCost ?? research.Cost) / 3;
                research.Cost = Math.Max(floor, research.Cost * 7 / 8 / 10 * 10);
                break;
        }

        research.Improvements++;
    }

    /// <summary>
    /// <c>ResearchDepartment:discoverObject</c>: the object is discovered; rooms whose researchable objects are all
    /// discovered can now be built.
    /// </summary>
    private void DiscoverObject(Simulation simulation, Hospital hospital, ResearchObject obj)
    {
        _objects[obj.Id].Discovered = true;
        foreach (var room in simulation.Content.Rooms)
        {
            if (hospital.IsRoomDiscovered(room.Id) || !simulation.Rules.IsRoomAvailable(room))
            {
                continue;
            }

            if (LevelRules.Needed(room).Any(needed => _objects.TryGetValue(needed.ObjectId, out var research) && !research.Discovered))
            {
                continue;
            }

            hospital.DiscoverRoom(room.Id);
        }

        RefreshTargets(simulation, hospital);
    }

    /// <summary>Sets what each area works on now, without touching the sliders.</summary>
    private void RefreshTargets(Simulation simulation, Hospital hospital)
    {
        foreach (var category in Order)
        {
            Area(category).Current = FindTarget(simulation, hospital, category);
        }
    }

    /// <summary>
    /// The area's item with the lowest cost above 0: an object of its kind to discover, a new drug for a known disease or
    /// else a drug not fully effective,
    /// a discovered machine with improvements left, or the item of the disease the player concentrates on.
    /// </summary>
    private ResearchTarget FindTarget(Simulation simulation, Hospital hospital, ResearchCategory category)
    {
        var rules = simulation.Rules;
        var best = ResearchTarget.None;
        var bestCost = int.MaxValue;
        void Consider(ResearchTarget target)
        {
            if (Required(simulation, hospital, target) is { } cost && cost > 0 && cost < bestCost)
            {
                best = target;
                bestCost = cost;
            }
        }

        switch (category)
        {
            case ResearchCategory.Cure or ResearchCategory.Diagnosis:
                foreach (var obj in ResearchObjects.All)
                {
                    if (obj.Category == category && _objects.TryGetValue(obj.Id, out var research) && !research.Discovered)
                    {
                        Consider(ResearchTarget.Object(obj.Id));
                    }
                }

                break;
            case ResearchCategory.Drugs:
                // Every drug is there from the start; research makes the drugs of known diseases better.
                foreach (var disease in rules.Diseases.AvailableDiseases())
                {
                    if (_drugs.ContainsKey(disease.Id) && hospital.PeekCase(disease, rules.Diseases) is { Discovered: true })
                    {
                        Consider(ResearchTarget.Drug(disease.Id));
                    }
                }

                break;
            case ResearchCategory.Improvements:
                foreach (var obj in ResearchObjects.All)
                {
                    if (obj.IsMachine && _objects.TryGetValue(obj.Id, out var research) && research.Discovered)
                    {
                        Consider(ResearchTarget.Object(obj.Id));
                    }
                }

                break;
            case ResearchCategory.Specialisation:
                if (Concentrated.Length > 0 && DiseaseCatalog.Find(Concentrated) is { } concentrated)
                {
                    Consider(DiseaseTarget(simulation, hospital, concentrated));
                }

                break;
        }

        return best;
    }

    /// <summary>
    /// The item research on a disease works on: its drug once its pharmacy can be built, or else the researchable object
    /// its room needs, to discover or to improve.
    /// </summary>
    private ResearchTarget DiseaseTarget(Simulation simulation, Hospital hospital, DiseaseDefinition disease)
    {
        if (IsDrugDisease(disease) && _drugs.ContainsKey(disease.Id) && hospital.IsRoomDiscovered("pharmacy"))
        {
            return ResearchTarget.Drug(disease.Id);
        }

        var roomType = ConcentrationRoomType(disease);
        if (roomType is null || simulation.Content.FindRoom(roomType) is not { } room)
        {
            return ResearchTarget.None;
        }

        string? found = null;
        foreach (var (objectId, _) in LevelRules.Needed(room))
        {
            if (_objects.ContainsKey(objectId))
            {
                found = objectId;
            }
        }

        return found is null ? ResearchTarget.None : ResearchTarget.Object(found);
    }

    /// <summary>
    /// <c>Hospital:canConcentrateResearch</c>: a disease whose room is not discovered yet, a drug not fully effective, or
    /// a disease whose machine has improvements left.
    /// </summary>
    public bool CanConcentrate(Simulation simulation, Hospital hospital, DiseaseDefinition disease)
    {
        var target = DiseaseTarget(simulation, hospital, disease);
        return !target.IsNone && Required(simulation, hospital, target) is > 0;
    }

    /// <summary>
    /// The casebook's concentrate button: research concentrates on the disease; again on the same disease stops it.
    /// </summary>
    public bool Concentrate(Simulation simulation, Hospital hospital, DiseaseDefinition disease)
    {
        if (string.Equals(Concentrated, disease.Id, StringComparison.Ordinal))
        {
            Concentrated = string.Empty;
        }
        else if (CanConcentrate(simulation, hospital, disease))
        {
            Concentrated = disease.Id;
        }
        else
        {
            return false;
        }

        RefreshTargets(simulation, hospital);
        return true;
    }

    /// <summary>Once a disease is diagnosed for the first time its drug may be researched.</summary>
    internal void OnDiseaseDiscovered(Simulation simulation, Hospital hospital) => RefreshTargets(simulation, hospital);

    /// <summary>The room research on a disease is about: its last treatment room, or a diagnosis's own room.</summary>
    private static string? ConcentrationRoomType(DiseaseDefinition disease) =>
        disease.IsPseudo
            ? disease.Id.StartsWith("diag_", StringComparison.Ordinal) ? disease.Id[5..] : null
            : disease.TreatmentRooms.Count > 0 ? disease.TreatmentRooms[^1] : null;

    /// <summary>
    /// <c>UIResearch:adjustResearch</c>: an area's slider goes down to no less than 0, or up while the total is under
    /// 100 % and the area has something to work on.
    /// </summary>
    public void AdjustShare(ResearchCategory category, int change)
    {
        var area = Area(category);
        if (change < 0)
        {
            area.Share = Math.Max(0, area.Share + change);
        }
        else if (change > 0 && Total < 100 && !area.Current.IsNone)
        {
            area.Share += Math.Min(change, 100 - Total);
        }
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Pool);
        writer.WriteInt32(_objects.Count);
        foreach (var (id, research) in _objects)
        {
            writer.WriteString(id);
            writer.WriteInt32(research.Points);
            writer.WriteInt32(research.StartStrength);
            writer.WriteInt32(research.Cost);
            writer.WriteBool(research.Discovered);
            writer.WriteInt32(research.Improvements);
        }

        writer.WriteInt32(_drugs.Count);
        foreach (var (id, points) in _drugs)
        {
            writer.WriteString(id);
            writer.WriteInt32(points);
        }

        foreach (var area in _areas)
        {
            writer.WriteInt32(area.Share);
            writer.WriteByte((byte)area.Current.Kind);
            writer.WriteString(area.Current.Id);
        }

        writer.WriteString(Concentrated);
    }

    internal static ResearchDepartment Read(SimReader reader)
    {
        // Version 112 has the pool and the specification's improvements; older saves kept CorsixTH's points.
        var old = reader.Version is not 0 and < 112;
        var research = new ResearchDepartment();
        if (!old)
        {
            research.Pool = reader.ReadInt32();
        }

        var objects = reader.ReadCount(MaxEntries);
        for (var i = 0; i < objects; i++)
        {
            var id = reader.ReadString();
            research._objects[id] = old
                ? new ObjectResearch
                {
                    Points = (int)decimal.Floor(reader.ReadDecimal()),
                    StartStrength = reader.ReadInt32(),
                    Cost = reader.ReadInt32(),
                    Discovered = reader.ReadBool(),
                    Improvements = Math.Min(MaxImprovements, reader.ReadInt32() + reader.ReadInt32()),
                }
                : new ObjectResearch
                {
                    Points = reader.ReadInt32(),
                    StartStrength = reader.ReadInt32(),
                    Cost = reader.ReadInt32(),
                    Discovered = reader.ReadBool(),
                    Improvements = reader.ReadInt32(),
                };
        }

        var drugs = reader.ReadCount(MaxEntries);
        for (var i = 0; i < drugs; i++)
        {
            research._drugs[reader.ReadString()] = old ? (int)decimal.Floor(reader.ReadDecimal()) : reader.ReadInt32();
        }

        foreach (var area in research._areas)
        {
            area.Share = reader.ReadInt32();
            var kind = reader.ReadByte();
            var id = reader.ReadString();
            area.Current = kind switch
            {
                (byte)ResearchTargetKind.Object => ResearchTarget.Object(id),
                (byte)ResearchTargetKind.Drug => ResearchTarget.Drug(id),
                0 or 1 => ResearchTarget.None,
                _ => throw new InvalidDataException($"Unknown research target {kind}."),
            };
        }

        if (old)
        {
            // CorsixTH's drain points, policy total and month's research cost.
            reader.ReadDecimal();
            reader.ReadInt32();
            reader.ReadInt32();
        }

        research.Concentrated = reader.ReadString();
        return research;
    }
}
