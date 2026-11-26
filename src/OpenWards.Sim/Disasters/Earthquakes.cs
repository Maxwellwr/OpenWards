// Earthquakes. The schedule, the time to the main quake and the damage follow the community's logic specification of
// the original game; the shaking is ported from CorsixTH CorsixTH/Lua/earthquake.lua (Earthquake:tick, onEndDay,
// createEarthquake), hospitals/player_hospital.lua (PlayerHospital:tickEarthquake) and the earthquake cheats of
// cheats.lua (cheatEarthquake, cheatToggleEarthquake).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Calendar;
using OpenWards.Sim.Serialization;

namespace OpenWards.Sim.Disasters;

/// <summary>
/// One of the level's planned earthquakes (<c>quake_control[n]</c>): it starts on a random day of a random month of
/// the game from <see cref="StartMonth"/> to <see cref="EndMonth"/> and wears every machine by <see cref="Severity"/>.
/// </summary>
/// <param name="StartMonth">The first month of the game it may start in, from 1.</param>
/// <param name="EndMonth">The last month of the game it may start in.</param>
/// <param name="Severity">Its size; 0 ends the level's earthquakes.</param>
public readonly record struct PlannedEarthquake(int StartMonth, int EndMonth, int Severity);

/// <summary>What an hour of an earthquake brings.</summary>
public enum EarthquakeRound : byte
{
    /// <summary>No round of the main quake.</summary>
    None,

    /// <summary>A round of the main quake: patients out of rooms may stumble.</summary>
    Shaking,

    /// <summary>The last round of the main quake, which wears every machine by the quake's size.</summary>
    Last,
}

/// <summary>How hard the screen shakes during an earthquake (CorsixTH's <c>beginShakeScreen</c> intensities).</summary>
public enum EarthquakeShake : byte
{
    /// <summary>No shaking.</summary>
    None = 0,

    /// <summary>0.2: the start of the warning quake, which the same hour turns into <see cref="Small"/>.</summary>
    Warning = 1,

    /// <summary>0.5: the first and last two rounds of damage, and the warning quake.</summary>
    Small = 2,

    /// <summary>1: the rounds of damage in between.</summary>
    Large = 3,
}

/// <summary>
/// The world's earthquakes: the next one is planned from the first of the level's <see cref="PlannedEarthquake"/>
/// entries not used yet whose months have not all passed, on a day drawn evenly from what is left of them. On its day a
/// short, harmless warning quake comes, then 5 to 9 days later the main quake, which shakes for a round every 16 hours,
/// as many rounds as its size. Patients out of rooms may stumble in each round; at the end of the last round every
/// machine wears once by the quake's size, which the simulation does when <see cref="Tick"/> says so.
/// </summary>
/// <remarks>
/// CorsixTH wears the machines by one use in each round, plans the level's entries strictly in turn (so an entry whose
/// months have passed stops the earthquakes) and always waits 600 hours for the main quake.
/// </remarks>
public sealed class Earthquakes
{
    /// <summary>Hours between rounds of damage (CorsixTH's <c>damage_time</c>).</summary>
    public const int DamageTime = 16;

    /// <summary>The fewest days from the warning quake to the main one.</summary>
    public const int MinWarningDays = 5;

    /// <summary>The most days from the warning quake to the main one.</summary>
    public const int MaxWarningDays = 9;

    /// <summary>How long the warning quake lasts, in hours (<c>warning_length</c>).</summary>
    public const int WarningLength = 25;

    /// <summary>The largest size the cheat makes when no earthquake is planned (CorsixTH: "above 6 seems disastrous").</summary>
    private const int MaxCheatSize = 6;

    /// <summary>Whether an earthquake is under way: from its day until its last round of damage.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Whether the cheat has turned earthquakes off.</summary>
    public bool IsDisabled { get; private set; }

    /// <summary>Whether the next earthquake has been planned and its warning has not come yet.</summary>
    public bool IsNextPlanned { get; private set; }

    /// <summary>The month of the game the next or current earthquake starts in, from 1.</summary>
    public int StartMonth { get; private set; }

    /// <summary>The day of that month.</summary>
    public int StartDay { get; private set; }

    /// <summary>The earthquake's size: how many rounds of damage it does.</summary>
    public int Size { get; private set; }

    /// <summary>The rounds of damage still to come.</summary>
    public int RemainingDamage { get; private set; }

    /// <summary>Hours to the next round of damage.</summary>
    public int DamageTimer { get; private set; }

    /// <summary>Hours to the main quake.</summary>
    public int WarningTimer { get; private set; }

    /// <summary>Hours from the warning quake to the main one, drawn when the earthquake is planned.</summary>
    public int WarningHours { get; private set; } = MaxWarningDays * GameDate.HoursPerDay;

    /// <summary>How many of the level's planned earthquakes have been used.</summary>
    public int PlannedSoFar { get; private set; }

    /// <summary>How hard the screen shakes now.</summary>
    public EarthquakeShake Shake { get; private set; }

    /// <summary>Starts the level's earthquakes: plans the first one (CorsixTH's <c>Earthquake:Earthquake</c>).</summary>
    internal static Earthquakes Start(IReadOnlyList<PlannedEarthquake> planned, SimRandom random)
    {
        var earthquakes = new Earthquakes();
        earthquakes.PlanNext(planned, random, monthOfGame: 1, day: 0);
        return earthquakes;
    }

    /// <summary>
    /// One hour of an earthquake (<c>Earthquake:tick</c>). Returns a round of the main quake: its last round does the
    /// damage.
    /// </summary>
    internal EarthquakeRound Tick(IReadOnlyList<PlannedEarthquake> planned, SimRandom random, GameDate date)
    {
        if (!IsActive || IsDisabled)
        {
            return EarthquakeRound.None;
        }

        if (RemainingDamage == 0)
        {
            IsActive = false;
            Shake = EarthquakeShake.None;
            PlanNext(planned, random, date.MonthOfGame, date.Day);
            return EarthquakeRound.None;
        }

        if (WarningTimer == WarningHours)
        {
            IsNextPlanned = false;
            Shake = EarthquakeShake.Warning;
        }

        // The end of the warning quake.
        if (WarningTimer >= WarningHours - WarningLength && WarningTimer - 1 < WarningHours - WarningLength)
        {
            Shake = EarthquakeShake.None;
        }

        if (WarningTimer > 0)
        {
            WarningTimer--;

            // Nothing more happens between the warning quake and the main one. CorsixTH's "main_start" announcement
            // comes after this test and so never plays: the hour the timer reaches 0 returns here too.
            if (WarningTimer < WarningHours - WarningLength)
            {
                return EarthquakeRound.None;
            }
        }

        // Earthquakes start and end small: gently while at most two rounds have been done or are left.
        Shake = RemainingDamage <= 2 || Size - RemainingDamage <= 2 ? EarthquakeShake.Small : EarthquakeShake.Large;

        if (WarningTimer > 0)
        {
            return EarthquakeRound.None;
        }

        DamageTimer--;
        if (DamageTimer > 0)
        {
            return EarthquakeRound.None;
        }

        RemainingDamage--;
        DamageTimer += DamageTime;
        return RemainingDamage == 0 ? EarthquakeRound.Last : EarthquakeRound.Shaking;
    }

    /// <summary>Starts the planned earthquake on its day (<c>Earthquake:onEndDay</c>).</summary>
    internal void OnEndDay(GameDate date)
    {
        if (date.MonthOfGame == StartMonth && date.Day == StartDay && !IsDisabled)
        {
            IsActive = true;
        }
    }

    /// <summary>
    /// The cheat's earthquake (<c>Earthquake:createEarthquake</c>): the planned one comes today, or a new one of size
    /// 1 to 6 if none is planned. False if one is under way or earthquakes are off.
    /// </summary>
    internal bool Create(GameDate date, SimRandom random)
    {
        if (IsActive || IsDisabled)
        {
            return false;
        }

        StartDay = date.Day;
        StartMonth = date.MonthOfGame;
        if (!IsNextPlanned)
        {
            Size = random.NextInclusive(1, MaxCheatSize);
            RemainingDamage = Size;
            DamageTimer = DamageTime;
            WarningHours = random.NextInclusive(MinWarningDays, MaxWarningDays) * GameDate.HoursPerDay;
            WarningTimer = WarningHours;
        }

        return true;
    }

    /// <summary>
    /// Turns earthquakes off or back on (<c>cheatToggleEarthquake</c>). Unlike CorsixTH, turning them off mid-quake
    /// stops the screen shaking, which would otherwise go on until they are turned on again.
    /// </summary>
    internal void ToggleDisabled()
    {
        IsDisabled = !IsDisabled;
        if (IsDisabled)
        {
            Shake = EarthquakeShake.None;
        }
    }

    /// <summary>
    /// An imported save's planned earthquake: it comes on <paramref name="next"/> with strength <paramref name="size"/>,
    /// and the level's first <paramref name="used"/> planned ones are spent. Without one the level's plan stays: every
    /// save seen marks all sixteen flags used, so they say nothing on their own.
    /// </summary>
    internal void Import(int plannedCount, int used, GameDate next, int size)
    {
        if (IsActive || size <= 0)
        {
            return;
        }

        PlannedSoFar = Math.Clamp(used, 0, plannedCount);
        (StartMonth, StartDay) = (next.MonthOfGame, next.Day);
        Size = size;
        RemainingDamage = Size;
        DamageTimer = DamageTime;
        WarningTimer = WarningHours;
        IsNextPlanned = true;
    }

    /// <summary>
    /// An imported save's earthquake under way, of strength <paramref name="size"/>, with the level's first
    /// <paramref name="used"/> planned ones spent. In <paramref name="phase"/> 1 the warning quake starts and 2 waits
    /// for the main quake, which comes in <paramref name="hoursToStep"/> hours; in 3 the main quake shakes until
    /// <paramref name="hoursToStep"/> hours from now, its damage at the end; in 4 the damage is done and the next
    /// earthquake is planned at once.
    /// </summary>
    internal void ImportUnderWay(int plannedCount, int used, GameDate today, int phase, long hoursToStep, int size)
    {
        PlannedSoFar = Math.Clamp(used, 0, plannedCount);
        (StartMonth, StartDay) = (today.MonthOfGame, today.Day);
        Size = Math.Max(1, size);
        IsActive = true;
        IsNextPlanned = false;
        DamageTimer = DamageTime;
        var hours = (int)Math.Clamp(hoursToStep, 0, MaxWarningDays * GameDate.HoursPerDay);
        switch (phase)
        {
            case 1:
                // The warning quake shakes from the next hour.
                WarningHours = Math.Max(hours, WarningLength + 1);
                WarningTimer = WarningHours;
                RemainingDamage = Size;
                IsNextPlanned = true;
                break;
            case 2:
                WarningHours = MaxWarningDays * GameDate.HoursPerDay;
                WarningTimer = Math.Clamp(hours, 1, WarningHours - WarningLength - 1);
                RemainingDamage = Size;
                break;
            case 3:
                WarningTimer = 0;
                RemainingDamage = Math.Clamp((hours + DamageTime - 1) / DamageTime, 1, Size);
                break;
            default:
                WarningTimer = 0;
                RemainingDamage = 0;
                break;
        }
    }

    /// <summary>
    /// Plans the level's next earthquake, if it has one: the first entry not used yet that still has a day after
    /// <paramref name="day"/> of <paramref name="monthOfGame"/>, on a day drawn evenly from the days of its months left.
    /// Entries whose months have passed are used up. An entry whose months are the wrong way round starts in its
    /// <see cref="PlannedEarthquake.StartMonth"/>.
    /// </summary>
    private void PlanNext(IReadOnlyList<PlannedEarthquake> planned, SimRandom random, int monthOfGame, int day)
    {
        IsActive = false;
        while (PlannedSoFar < planned.Count && planned[PlannedSoFar].Severity != 0)
        {
            var control = planned[PlannedSoFar];
            PlannedSoFar++;
            var days = new List<(int Month, int Day)>();
            for (var month = Math.Max(control.StartMonth, monthOfGame); month <= Math.Max(control.StartMonth, control.EndMonth); month++)
            {
                for (var d = month == monthOfGame ? day + 1 : 1; d <= GameDate.DaysIn((((month - 1) % 12) + 12) % 12 + 1); d++)
                {
                    days.Add((month, d));
                }
            }

            if (days.Count == 0)
            {
                continue;
            }

            (StartMonth, StartDay) = days[random.Next(days.Count)];
            Size = control.Severity;
            RemainingDamage = Size;
            DamageTimer = DamageTime;
            WarningHours = random.NextInclusive(MinWarningDays, MaxWarningDays) * GameDate.HoursPerDay;
            WarningTimer = WarningHours;
            IsNextPlanned = true;
            return;
        }
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteBool(IsActive);
        writer.WriteBool(IsDisabled);
        writer.WriteBool(IsNextPlanned);
        writer.WriteInt32(StartMonth);
        writer.WriteInt32(StartDay);
        writer.WriteInt32(Size);
        writer.WriteInt32(RemainingDamage);
        writer.WriteInt32(DamageTimer);
        writer.WriteInt32(WarningTimer);
        writer.WriteInt32(WarningHours);
        writer.WriteInt32(PlannedSoFar);
        writer.WriteByte((byte)Shake);
    }

    internal static Earthquakes Read(SimReader reader)
    {
        var earthquakes = new Earthquakes
        {
            IsActive = reader.ReadBool(),
            IsDisabled = reader.ReadBool(),
            IsNextPlanned = reader.ReadBool(),
            StartMonth = reader.ReadInt32(),
            StartDay = reader.ReadInt32(),
            Size = reader.ReadInt32(),
            RemainingDamage = reader.ReadInt32(),
            DamageTimer = reader.ReadInt32(),
            WarningTimer = reader.ReadInt32(),
        };

        // Version 112 draws the days to the main quake; before, CorsixTH's 600 hours.
        earthquakes.WarningHours = reader.Version is 0 or >= 112 ? reader.ReadInt32() : 600;
        earthquakes.PlannedSoFar = reader.ReadInt32();
        var shake = reader.ReadByte();
        earthquakes.Shake = shake <= (byte)EarthquakeShake.Large ? (EarthquakeShake)shake : throw new InvalidDataException($"Unknown earthquake shake {shake}.");
        return earthquakes;
    }
}
