using System.Globalization;
using OpenWards.Sim.Economy;

namespace OpenWards.Client.Ui;

/// <summary>
/// The texts of the network game's missions, from the original's strings as the maintainer's specification of the game's
/// logic lists them ("Итог"): section 66 for the missions, section 67 for how they were judged.
/// </summary>
internal static class NetMissionTexts
{
    // The original strings' 0-based sections; OriginalTexts.String counts from 1.
    private const int MissionsSection = 66;
    private const int ResultsSection = 67;

    /// <summary>
    /// The mission's text: its first string, and for missions 1 to 7 the second, which says what failing it means. The
    /// target and the months left go into missions 4 to 7, only the months into the others.
    /// </summary>
    public static IReadOnlyList<string> Mission(IWindowHost host, NetMissionRule mission, int monthsLeft)
    {
        var first = mission.Criterion switch
        {
            NetCriterion.NotLowestMoney => 25,
            NetCriterion.NotLowestReputation => 23,
            var code => ((int)code * 3) - 1,
        };
        var text = Original(host, MissionsSection, first) is { } found
            ? mission.HasValue ? Fill(found, mission.Value, monthsLeft) : Fill(found, monthsLeft)
            : UiText.Format("openwards.netmission.fallback", "Mission %1%: %2% months", (int)mission.Criterion, monthsLeft);
        var paragraphs = new List<string> { text };
        if (mission.Criterion <= NetCriterion.Reputation && Original(host, MissionsSection, first + 1) is { } warning)
        {
            paragraphs.Add(warning);
        }

        return paragraphs;
    }

    /// <summary>How the mission went, for the player watching: everyone failed, they did it, or they failed; then who failed.</summary>
    public static IReadOnlyList<string> Result(IWindowHost host, NetMissionResult result, int player, Func<int, string> hospitalName)
    {
        var paragraphs = new List<string>();
        if (result.AllFailed)
        {
            paragraphs.Add(Original(host, ResultsSection, 0) ?? UiText.Get("openwards.netmission.all_failed", "Nobody completed the mission. The game goes on."));
            return paragraphs;
        }

        var passed = player >= 0 && player < result.Passed.Count ? result.Passed[player] : null;
        if (passed == true)
        {
            paragraphs.Add(Original(host, ResultsSection, 4) ?? UiText.Get("openwards.netmission.passed", "You have completed the mission."));
        }
        else if (passed == false)
        {
            paragraphs.Add(Original(host, ResultsSection, 2) ?? UiText.Get("openwards.netmission.failed", "You have failed the mission."));
        }

        var failed = Enumerable.Range(0, result.Passed.Count).Where(i => result.Passed[i] == false).Select(hospitalName).ToList();
        if (failed.Count > 0)
        {
            paragraphs.Add(UiText.Format("openwards.netmission.who_failed", "Failed the mission: %1%", string.Join(", ", failed)));
        }

        return paragraphs;
    }

    /// <summary>A string of the original's 0-based section and index.</summary>
    private static string? Original(IWindowHost host, int section, int index) => host.Texts?.String(section + 1, index + 1);

    /// <summary>
    /// Puts the numbers into the string's slots: a numbered slot <c>%n$d</c> takes the n-th number, as the German text of
    /// mission 7 has them (<see cref="Formats.StringTable.FixGerman"/>), and plain <c>%d</c> take them in turn.
    /// </summary>
    internal static string Fill(string text, params int[] numbers)
    {
        if (text.Contains("$d", StringComparison.Ordinal))
        {
            for (var i = 0; i < numbers.Length; i++)
            {
                text = text.Replace($"%{i + 1}$d", numbers[i].ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            }

            return text;
        }

        foreach (var number in numbers)
        {
            var at = text.IndexOf("%d", StringComparison.Ordinal);
            if (at < 0)
            {
                break;
            }

            text = string.Concat(text.AsSpan(0, at), number.ToString(CultureInfo.InvariantCulture), text.AsSpan(at + 2));
        }

        return text;
    }
}
