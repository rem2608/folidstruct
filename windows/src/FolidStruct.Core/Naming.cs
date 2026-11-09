using static FolidStruct.Core.I18n;

namespace FolidStruct.Core;

public static class Naming
{
    public const string CounterSuffix = "#";
    /// <summary>NTFS limit for one name, in UTF-16 code units.</summary>
    public const int MaxNameLength = 255;

    /// <summary>Characters Windows does not allow in file and folder names.</summary>
    public const string InvalidChars = "\\/:*?\"<>|";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// Build the project folder name, e.g. <c>2026-11-07 22# Rossi Logo</c>.
    /// <paramref name="date"/> and <paramref name="counter"/> are left out when null.
    /// <paramref name="values"/> maps the index of a field or choice block to what was
    /// entered; blocks missing from it use their default value. Empty blocks are skipped.
    /// </summary>
    public static string BuildProjectName(NameFormat format, string name, DateOnly? date = null,
                                          int? counter = null, IReadOnlyDictionary<int, string>? values = null)
    {
        var parts = new List<string>();
        for (var index = 0; index < format.Blocks.Count; index++)
        {
            var block = format.Blocks[index];
            var part = block.Kind switch
            {
                BlockKind.Date => date?.ToString("yyyy-MM-dd") ?? "",
                BlockKind.Counter => counter is { } n ? $"{n}{CounterSuffix}" : "",
                BlockKind.Name => name,
                BlockKind.Text => block.Value,
                _ => values is not null && values.TryGetValue(index, out var v) ? v : block.Value,
            };
            part = CollapseSpaces(part); // no line breaks or double spaces
            if (part.Length > 0)
                parts.Add(part);
        }
        return string.Join(format.Separator, parts);
    }

    public static string CollapseSpaces(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A sample name for previews; fields without a default show their label.</summary>
    public static string ExampleName(NameFormat format, DateOnly? today = null, int counter = 1)
    {
        var values = new Dictionary<int, string>();
        for (var i = 0; i < format.Blocks.Count; i++)
        {
            var b = format.Blocks[i];
            if (b.Kind is BlockKind.Field or BlockKind.Choice)
                values[i] = b.Value.Length > 0 ? b.Value : $"‹{b.Label}›";
        }
        return BuildProjectName(
            format, T("Project"),
            date: format.Block(BlockKind.Date).Enabled ? today ?? DateOnly.FromDateTime(DateTime.Now) : null,
            counter: format.Block(BlockKind.Counter).Enabled ? counter : null,
            values: values);
    }

    public static bool HasInvalidChars(string text) =>
        text.Any(c => c < ' ' || InvalidChars.Contains(c));

    /// <summary>An error message if <paramref name="text"/> has characters Windows does not allow.</summary>
    public static string? CheckChars(string text) =>
        HasInvalidChars(text)
            ? T("The name cannot contain these characters: {chars}",
                ("chars", string.Join(' ', InvalidChars.ToCharArray())))
            : null;

    /// <summary>An error message if <paramref name="name"/> is not a valid folder name, else null.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return T("The name cannot be empty");
        if (CheckChars(name) is { } charsError)
            return charsError;
        if (name.Trim() is "." or "..")
            return T("“.” and “..” are not valid names");
        if (name.EndsWith('.') || name.EndsWith(' '))
            return T("The name cannot end with a dot or a space");
        // "CON" and also "CON.txt" or "con " are reserved.
        var stem = name.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem))
            return T("“{name}” is reserved by Windows", ("name", stem.ToUpperInvariant()));
        if (name.Length > MaxNameLength)
            return T("The name is too long");
        return null;
    }
}
