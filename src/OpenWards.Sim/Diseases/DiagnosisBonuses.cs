namespace OpenWards.Sim.Diseases;

/// <summary>
/// Which diagnosis rooms help diagnose each disease, and what each adds to a diagnosis step there: the community
/// specification's table of disease × eight diagnosis rooms, with the values the maintainer chose on 23 November 2026.
/// </summary>
/// <remarks>
/// A room missing from a disease's list is never one of its diagnosis rooms. A negative bonus also costs the patient
/// health (see <see cref="People.PatientCare"/>). The GP's office adds nothing. Alien DNA has no entry: its patients
/// only come in emergencies, already diagnosed. This departs from CorsixTH, whose diseases list their rooms without
/// bonuses.
/// </remarks>
public static class DiagnosisBonuses
{
    private static readonly Dictionary<string, (string Room, int Bonus)[]> Table = new(StringComparer.Ordinal)
    {
        ["bloaty_head"] = [("general_diag", 200), ("cardiogram", 0), ("scanner", 400), ("x_ray", 0), ("ward", 200)],
        ["hairyitis"] = [("scanner", 200), ("x_ray", 0), ("ward", 400)],
        ["king_complex"] = [("scanner", 0), ("x_ray", 0), ("psych", 400), ("ward", 200)],
        ["invisibility"] = [("scanner", 0), ("blood_machine", 400), ("x_ray", -200)],
        ["serious_radiation"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", -200), ("ultrascan", 0), ("blood_machine", 400), ("x_ray", -400), ("psych", 0), ("ward", 0)],
        ["slack_tongue"] = [("cardiogram", 200), ("scanner", 400), ("x_ray", 0), ("psych", -200)],
        ["fractured_bones"] = [("cardiogram", -200), ("scanner", 200), ("x_ray", 400)],
        ["baldness"] = [("scanner", 0), ("blood_machine", 400), ("x_ray", 0), ("psych", -200), ("ward", 200)],
        ["discrete_itching"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 400), ("x_ray", 0), ("psych", 0), ("ward", 0)],
        ["jellyitis"] = [("general_diag", 400), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 0)],
        ["sleeping_illness"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 400), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 200)],
        ["pregnant"] = [("general_diag", 200), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 0)],
        ["transparency"] = [("scanner", 0), ("ward", 400)],
        ["uncommon_cold"] = [("general_diag", 400), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 0)],
        ["broken_wind"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 400), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 0)],
        ["spare_ribs"] = [("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 400), ("psych", 0), ("ward", 0)],
        ["kidney_beans"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 400), ("blood_machine", 0), ("x_ray", 200), ("psych", 0), ("ward", 0)],
        ["broken_heart"] = [("general_diag", 200), ("cardiogram", 400), ("scanner", 0), ("ultrascan", 200), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 0)],
        ["ruptured_nodules"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 400), ("ultrascan", 200), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 0)],
        ["tv_personalities"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 400), ("ward", 0)],
        ["infectious_laughter"] = [("cardiogram", 400), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 200), ("x_ray", 0), ("psych", 400), ("ward", 0)],
        ["corrugated_ankles"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 400), ("blood_machine", 0), ("x_ray", 200), ("psych", 0), ("ward", 0)],
        ["chronic_nosehair"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 400), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 200)],
        ["third_degree_sideburns"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 400), ("ward", 0)],
        ["fake_blood"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 400), ("x_ray", 0), ("psych", 0), ("ward", 0)],
        ["gastric_ejections"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 400)],
        ["the_squits"] = [("general_diag", 400), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 200)],
        ["iron_lungs"] = [("cardiogram", 0), ("blood_machine", 0), ("x_ray", 400)],
        ["sweaty_palms"] = [("general_diag", 0), ("cardiogram", -400), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 400), ("ward", 0)],
        ["heaped_piles"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 0), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 400)],
        ["gut_rot"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 400), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 0), ("psych", 0), ("ward", 0)],
        ["golf_stones"] = [("general_diag", 0), ("cardiogram", 0), ("scanner", 400), ("ultrascan", 0), ("blood_machine", 0), ("x_ray", 200), ("psych", 0), ("ward", 0)],
        ["unexpected_swelling"] = [("general_diag", 0), ("cardiogram", -400), ("scanner", 0), ("ultrascan", 400), ("blood_machine", 200), ("x_ray", 0), ("psych", -200), ("ward", 0)],
    };

    /// <summary>The disease's diagnosis rooms other than the GP's office, in the table's order; empty for none.</summary>
    public static IReadOnlyList<string> Rooms(string diseaseId) =>
        Table.TryGetValue(diseaseId, out var entries) ? [.. entries.Select(entry => entry.Room)] : [];

    /// <summary>What a diagnosis step in the room adds for the disease; 0 for the GP's office and rooms not in its list.</summary>
    public static int Bonus(string diseaseId, string roomType)
    {
        if (Table.TryGetValue(diseaseId, out var entries))
        {
            foreach (var (room, bonus) in entries)
            {
                if (string.Equals(room, roomType, StringComparison.Ordinal))
                {
                    return bonus;
                }
            }
        }

        return 0;
    }
}
