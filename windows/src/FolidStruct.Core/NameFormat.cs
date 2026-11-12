using System.Text.Json.Nodes;

namespace FolidStruct.Core;

/// <summary>The kinds of blocks a project folder name is made of.</summary>
public static class BlockKind
{
    // Built-in blocks: always present exactly once, can be moved but not removed.
    public const string Date = "date";
    public const string Counter = "counter";
    public const string Name = "name";
    // Custom blocks, added by the user.
    public const string Text = "text";
    public const string Field = "field";
    public const string Choice = "choice";

    public static readonly string[] Builtin = [Date, Counter, Name];
    public static readonly string[] Custom = [Text, Field, Choice];

    public static bool IsBuiltin(string kind) => Builtin.Contains(kind);
    public static bool IsCustom(string kind) => Custom.Contains(kind);
}

public sealed class Block
{
    public string Kind { get; set; }
    /// <summary>Date and counter: on by default when creating a project.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Field and choice: shown next to the input.</summary>
    public string Label { get; set; } = "";
    /// <summary>Text: the text; field and choice: the default value.</summary>
    public string Value { get; set; } = "";
    /// <summary>Choice: the values to pick from.</summary>
    public List<string> Options { get; set; } = [];

    public Block(string kind, bool enabled = true)
    {
        Kind = kind;
        Enabled = enabled;
    }

    public Block Clone() => new(Kind, Enabled) { Label = Label, Value = Value, Options = [.. Options] };

    public JsonObject ToJson()
    {
        var data = new JsonObject { ["kind"] = Kind };
        if (Kind is BlockKind.Date or BlockKind.Counter)
            data["enabled"] = Enabled;
        if (Kind is BlockKind.Field or BlockKind.Choice)
            data["label"] = Label;
        if (Kind is BlockKind.Text or BlockKind.Field or BlockKind.Choice)
            data["value"] = Value;
        if (Kind == BlockKind.Choice)
            data["options"] = new JsonArray([.. Options.Select(o => (JsonNode?)JsonValue.Create(o))]);
        return data;
    }

    public static Block? FromJson(JsonNode? node)
    {
        if (node is not JsonObject data || Json.String(data["kind"]) is not { } kind
            || !(BlockKind.IsBuiltin(kind) || BlockKind.IsCustom(kind)))
            return null;
        return new Block(kind, Json.Bool(data["enabled"]) ?? kind != BlockKind.Counter)
        {
            Label = Json.String(data["label"]) ?? "",
            Value = Json.String(data["value"]) ?? "",
            Options = Json.Strings(data["options"]),
        };
    }
}

/// <summary>How the project folder name is built: blocks joined by a separator.</summary>
public sealed class NameFormat
{
    public static readonly string[] Separators = [" ", "_", "-", " - "];

    public string Separator { get; set; } = " ";
    public List<Block> Blocks { get; set; } = DefaultBlocks();

    public static List<Block> DefaultBlocks(bool useDate = true, bool useCounter = false) =>
        [new(BlockKind.Date, useDate), new(BlockKind.Counter, useCounter), new(BlockKind.Name)];

    /// <summary>The built-in block of this kind (always present).</summary>
    public Block Block(string kind) => Blocks.First(b => b.Kind == kind);

    public NameFormat Clone() => new() { Separator = Separator, Blocks = [.. Blocks.Select(b => b.Clone())] };

    public JsonObject ToJson() => new()
    {
        ["separator"] = Separator,
        ["blocks"] = new JsonArray([.. Blocks.Select(b => (JsonNode)b.ToJson())]),
    };

    public static NameFormat FromJson(JsonNode? node)
    {
        if (node is not JsonObject data)
            return new NameFormat();
        var blocks = new List<Block>();
        if (data["blocks"] is JsonArray items)
        {
            foreach (var item in items)
            {
                // Keep only the first copy of each built-in block.
                if (Core.Block.FromJson(item) is { } block
                    && !(BlockKind.IsBuiltin(block.Kind) && blocks.Any(b => b.Kind == block.Kind)))
                    blocks.Add(block);
            }
        }
        // Restore any missing built-in block, in its default position.
        if (!blocks.Any(b => b.Kind == BlockKind.Date))
            blocks.Insert(0, new Block(BlockKind.Date));
        if (!blocks.Any(b => b.Kind == BlockKind.Counter))
        {
            var dateIndex = blocks.FindIndex(b => b.Kind == BlockKind.Date);
            blocks.Insert(dateIndex + 1, new Block(BlockKind.Counter, enabled: false));
        }
        if (!blocks.Any(b => b.Kind == BlockKind.Name))
            blocks.Add(new Block(BlockKind.Name));

        var separator = Json.String(data["separator"]);
        return new NameFormat
        {
            Separator = separator is not null && Separators.Contains(separator) ? separator : " ",
            Blocks = blocks,
        };
    }
}

/// <summary>Small helpers to read untrusted JSON without exceptions.</summary>
internal static class Json
{
    public static string? String(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static bool? Bool(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    public static long? Integer(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        if (v.TryGetValue<long>(out var l))
            return l;
        if (v.TryGetValue<double>(out var d) && double.IsFinite(d))
            return (long)Math.Clamp(d, long.MinValue, long.MaxValue);
        if (v.TryGetValue<string>(out var s) && long.TryParse(s.Trim(), out l))
            return l;
        return null;
    }

    public static List<string> Strings(JsonNode? node) =>
        node is JsonArray array ? [.. array.Select(String).OfType<string>()] : [];
}
