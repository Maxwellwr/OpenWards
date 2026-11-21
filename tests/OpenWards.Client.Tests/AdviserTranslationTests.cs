using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using OpenWards.Client.Ui;
using OpenWards.Content;
using OpenWards.Content.Languages;
using OpenWards.Formats;

namespace OpenWards.Client.Tests;

/// <summary>
/// Everything the adviser says comes out translated: with the original's strings all English stand-ins, Russian (the
/// most complete of CorsixTH's translations) has its own text for every message, so none falls back to English.
/// </summary>
public partial class AdviserTranslationTests
{
    private const string EnglishMarker = "ORIGINAL";

    private static readonly Lazy<CorsixLanguages> Languages =
        new(() => CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages")));

    private static readonly Lazy<OriginalTexts> Russian =
        new(() => new OriginalTexts(EnglishStrings, Languages.Value.Load("Russian", _ => EnglishStrings)));

    private static readonly Lazy<LanguageStrings> English = new(() => Languages.Value.Load("English", _ => EnglishStrings));

    /// <summary>Every original string is "ORIGINAL section.index" (CorsixTH's 1-based numbers), 64 sections of 220.</summary>
    private static readonly StringTable EnglishStrings = Original();

    /// <summary>Every advice message the checks give, by its field's name.</summary>
    public static TheoryData<string> AdviceNames()
    {
        var names = new TheoryData<string>();
        foreach (var (name, _) in AllAdvice())
        {
            names.Add(name);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(AdviceNames))]
    public void EveryAdviceIsTranslated(string name)
    {
        var advice = AllAdvice().Single(entry => entry.Name == name).Advice;

        var text = advice.Text(Russian.Value, "x", "y");

        Assert.False(text.Contains(EnglishMarker, StringComparison.Ordinal), $"{name} falls back to the original's English: {text}");
        Assert.True(IsRussian(text), $"{name} is not in Russian: {text}");
    }

    [Fact]
    public void EveryTutorialStepIsTranslated()
    {
        var untranslated = new List<string>();
        for (var chapter = 1; chapter <= 4; chapter++)
        {
            for (var phase = 0; phase <= 20; phase++)
            {
                if (Tutorial.Entry(chapter, phase) is { } entry
                    && (Russian.Value.String(11, entry.Index) is not { } text || !IsRussian(text) || text.Contains(EnglishMarker, StringComparison.Ordinal)))
                {
                    untranslated.Add($"{chapter}.{phase} (S[11][{entry.Index}])");
                }
            }
        }

        Assert.True(untranslated.Count == 0, "Untranslated tutorial steps: " + string.Join(", ", untranslated));
    }

    /// <summary>
    /// The adviser's strings the client asks for by name (<c>"adviser.warnings.cannot_afford"</c>) or by the original's
    /// section and index (<c>String(11, 73)</c>): each must be a name English has (a wrong one always shows the English
    /// stand-in) and Russian must translate it.
    /// </summary>
    [Fact]
    public void EveryAdviserStringTheClientNamesIsTranslated()
    {
        var problems = new List<string>();
        foreach (var name in SourceMatches(AdviserName()).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal))
        {
            if (name.EndsWith('.') || name.EndsWith('_'))
            {
                // The start of a name the code completes (adviser.warnings.no_desk_ + number).
                continue;
            }

            if (English.Value.Get(name) is null)
            {
                problems.Add(name + " (no such string)");
            }
            else if (Russian.Value.Named(name) is not { } text || !IsRussian(text) || text.Contains(EnglishMarker, StringComparison.Ordinal))
            {
                problems.Add(name + " (not translated)");
            }
        }

        foreach (var match in SourceMatches(AdviserSectionString()))
        {
            var (section, index) = (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            if (Russian.Value.String(section, index) is not { } text || !IsRussian(text) || text.Contains(EnglishMarker, StringComparison.Ordinal))
            {
                problems.Add($"S[{section}][{index}] (not translated)");
            }
        }

        Assert.True(problems.Count == 0, "Adviser strings: " + string.Join(", ", problems.Distinct()));
    }

    [Fact]
    public void TheMarkerShowsAnUntranslatedString() =>
        Assert.Contains(EnglishMarker, new OriginalTexts(EnglishStrings).String(11, 2), StringComparison.Ordinal);

    private static List<(string Name, Advice Advice)> AllAdvice()
    {
        var all = new List<(string, Advice)>();
        foreach (var field in typeof(AdviserChecks).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            switch (field.GetValue(null))
            {
                case Advice advice:
                    all.Add((field.Name, advice));
                    break;
                case IEnumerable<Advice> list:
                    all.AddRange(list.Select((advice, i) => ($"{field.Name}[{i}]", advice)));
                    break;
            }
        }

        Assert.NotEmpty(all);
        return all;
    }

    /// <summary>Mostly Cyrillic letters: a few Latin ones (a name, "VIP") are fine.</summary>
    private static bool IsRussian(string text) =>
        text.Count(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё') > text.Count(char.IsAsciiLetter);

    private static List<Match> SourceMatches(Regex pattern)
    {
        // The build output may lie outside the checkout (--artifacts-path), so this file's own path is tried too.
        var directory = RepositoryRoot(AppContext.BaseDirectory) ?? RepositoryRoot(ThisFile());
        Assert.NotNull(directory);
        var matches = new List<Match>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory.FullName, "src", "OpenWards.Client"), "*.cs", SearchOption.AllDirectories))
        {
            matches.AddRange(pattern.Matches(File.ReadAllText(file)));
        }

        Assert.NotEmpty(matches);
        return matches;
    }

    private static DirectoryInfo? RepositoryRoot(string path)
    {
        var directory = new DirectoryInfo(path);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenWards.slnx")))
        {
            directory = directory.Parent;
        }

        return directory;
    }

    private static string ThisFile([CallerFilePath] string path = "") => Path.GetDirectoryName(path) ?? path;

    private static StringTable Original()
    {
        var data = new List<byte> { 64, 0 };
        for (var section = 0; section < 64; section++)
        {
            data.Add(220);
            data.Add(0);
        }

        for (var section = 1; section <= 64; section++)
        {
            for (var index = 1; index <= 220; index++)
            {
                data.AddRange($"{EnglishMarker} {section}.{index}".Select(c => (byte)c));
                data.Add(0);
            }
        }

        return StringTable.Parse(data.ToArray());
    }

    [GeneratedRegex("\"(adviser\\.[a-z0-9_.]+)\"")]
    private static partial Regex AdviserName();

    /// <summary>The original's sections of the adviser's messages (original_strings.lua's adviser and tutorial).</summary>
    [GeneratedRegex(@"\.String\((11|21|22|28|62), (\d+)\)")]
    private static partial Regex AdviserSectionString();
}
