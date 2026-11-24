// Training doctors after the community's logic specification (lessons). Leaving the training room on promotion is
// ported from CorsixTH CorsixTH/Lua/entities/humanoids/staff/doctor.lua (updateSkill).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Entities;
using OpenWards.Sim.People;

namespace OpenWards.Sim.Staff;

/// <summary>
/// What doctors learn in a training room. Only the consultant teaches, one lesson at the end of each cycle of their
/// animation at the projector, the skeleton or the bookcase; each lesson may teach every student on a lecture chair.
/// </summary>
internal static class Training
{
    /// <summary>The training room's type (rooms/training.lua).</summary>
    internal const string RoomType = "training";

    /// <summary>The projector, whose lessons are worth <see cref="TrainingRules.ProjectorValue"/>.</summary>
    internal const string Projector = "projector";

    /// <summary>The skeleton, whose lessons are worth <see cref="TrainingRules.SkeletonValue"/>.</summary>
    internal const string Skeleton = "skeleton";

    /// <summary>The bookcase, whose lessons are worth <see cref="TrainingRules.BookcaseValue"/>.</summary>
    internal const string Bookcase = "bookcase";

    /// <summary>The chance in ten, after each cycle, of a lesson with the books, else of one with the skeleton.</summary>
    internal const int AidChanceInTen = 1;

    /// <summary>What a lesson with <paramref name="objectType"/> is worth towards an ability.</summary>
    internal static int Value(TrainingRules rules, string objectType) => objectType switch
    {
        Skeleton => rules.SkeletonValue,
        Bookcase => rules.BookcaseValue,
        _ => rules.ProjectorValue,
    };

    /// <summary>
    /// A lesson by <paramref name="teacher"/> with an aid worth <paramref name="value"/>. The students are the doctors who
    /// are not consultants, studying on the room's lecture chairs: with n of them, k = n / 4 + 1. One threshold serves the
    /// whole lesson, a random number below the teacher's skill. The students are taken in turn with m = 1, and m grows by
    /// one for each student lacking at least one of the teacher's abilities, before their own share. A student below the
    /// threshold gains TrainingRate / (k × m) skill and, towards each ability the teacher has and they lack,
    /// value / (k × m) points, at least 1 of each.
    /// </summary>
    internal static void Lesson(Simulation simulation, Room room, Humanoid teacher, int value)
    {
        if (teacher.Profile is not { Skill: > 0 } master)
        {
            return;
        }

        var students = Students(simulation, room).ToList();
        if (students.Count == 0)
        {
            return;
        }

        var rules = simulation.Rules.Staff.Training;
        var k = (students.Count / 4) + 1;
        var threshold = simulation.Random.Next(master.Skill);
        var m = 1;
        foreach (var student in students)
        {
            var taught = Taught(student, master).ToList();
            if (taught.Count > 0)
            {
                m++;
            }

            if (student.Skill >= threshold)
            {
                continue;
            }

            var divisor = k * m;
            student.TrainSkill(Math.Max(1, rules.Rate / divisor));
            foreach (var specialism in taught)
            {
                student.TrainSpecialism(specialism, Math.Max(1, value / divisor), rules.Threshold(specialism));
            }
        }
    }

    /// <summary>The doctors of the hospital, not consultants, studying on the room's lecture chairs, in the room's order.</summary>
    private static IEnumerable<StaffProfile> Students(Simulation simulation, Room room)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: DoctorBehaviour { Phase: DoctorPhase.Studying } doctor, Profile: { IsConsultant: false } profile, IsDespawning: false } humanoid
                && doctor.RoomId == room.Id && humanoid.Employer >= 0)
            {
                yield return profile;
            }
        }
    }

    /// <summary>The abilities the teacher has and the student lacks.</summary>
    private static IEnumerable<Specialism> Taught(StaffProfile student, StaffProfile teacher)
    {
        foreach (var specialism in new[] { Specialism.Surgeon, Specialism.Psychiatrist, Specialism.Researcher })
        {
            if (teacher.Has(specialism) && !student.Has(specialism))
            {
                yield return specialism;
            }
        }
    }

    /// <summary>A doctor just made a consultant no longer learns: a student leaves the training room (<c>updateSkill</c>).</summary>
    internal static void OnPromotedToConsultant(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.Behaviour is DoctorBehaviour doctor && simulation.FindEntity(doctor.RoomId) is Room { RoomType: RoomType } room && room.StaffId != humanoid.Id)
        {
            StaffEmployment.Detach(simulation, humanoid);
            humanoid.Behaviour = StaffHiring.NewBehaviour(humanoid.Profile!.Class);
            RoomVisits.SettleIdleStaff(simulation, humanoid);
        }
    }
}
