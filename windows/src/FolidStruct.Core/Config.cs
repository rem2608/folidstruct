using System.Text.Json.Nodes;

namespace FolidStruct.Core;

public sealed class Template
{
    public string Name { get; set; }
    public List<string> Folders { get; set; }
    /// <summary>When false the template follows the global name format.</summary>
    public bool CustomFormat { get; set; }
    public NameFormat Format { get; set; } = new();
    /// <summary>Stable identity, so the counter survives renaming.</summary>
    public string Id { get; set; } = NewId();

    public Template(string name, IEnumerable<string>? folders = null)
    {
        Name = name;
        Folders = folders?.ToList() ?? [];
    }

    public static string NewId() => Guid.NewGuid().ToString("N");
}

/// <summary>The content of config.json, the same file format as on Linux.</summary>
public sealed class Config
{
    public const int Version = 2;

    /// <summary>The example template, in English; translated through I18n.</summary>
    public static readonly (string Name, string[] Folders) Example =
        ("Graphics", ["Fonts", "Images", "Drafts", "Print"]);

    public NameFormat Format { get; set; } = new();
    public string Language { get; set; } = I18n.Auto;
    public List<Template> Templates { get; set; } = [];

    public NameFormat FormatFor(Template template) => template.CustomFormat ? template.Format : Format;

    public Template? FindTemplate(string? name) => Templates.FirstOrDefault(t => t.Name == name);

    public static Template ExampleTemplate(string language) =>
        new(I18n.Translate(Example.Name, language), Example.Folders.Select(f => I18n.Translate(f, language)));

    public static Config Default() => new() { Templates = [ExampleTemplate(I18n.CurrentLanguage)] };

    /// <summary>A new template, starting from a copy of the global name format.</summary>
    public Template NewTemplate(string name) => new(name) { Format = Format.Clone() };

    /// <summary>
    /// Translate the example template to <paramref name="language"/> if it is still exactly
    /// as created, in any language. Templates edited by the user are kept.
    /// </summary>
    public bool TranslateExample(string language)
    {
        var target = ExampleTemplate(language);
        if (FindTemplate(target.Name) is not null)
            return false;
        foreach (var template in Templates)
        {
            foreach (var other in I18n.Translations.Keys.Prepend("en"))
            {
                var example = ExampleTemplate(other);
                if (template.Name == example.Name && template.Folders.SequenceEqual(example.Folders))
                {
                    template.Name = target.Name;
                    template.Folders = target.Folders;
                    return true;
                }
            }
        }
        return false;
    }

    public JsonObject ToJson() => new()
    {
        ["version"] = Version,
        ["language"] = Language,
        ["format"] = Format.ToJson(),
        ["templates"] = new JsonArray([.. Templates.Select(t => (JsonNode)new JsonObject
        {
            ["id"] = t.Id,
            ["name"] = t.Name,
            ["folders"] = new JsonArray([.. t.Folders.Select(f => (JsonNode?)JsonValue.Create(f))]),
            ["custom_format"] = t.CustomFormat,
            ["format"] = t.Format.ToJson(),
        })]),
    };

    /// <summary>Read a configuration, converting version 1 on the fly.</summary>
    public static Config FromJson(JsonObject data)
    {
        static NameFormat ReadFormat(JsonObject item)
        {
            if (item.ContainsKey("format"))
                return NameFormat.FromJson(item["format"]);
            // Version 1: only the date and counter switches.
            return new NameFormat
            {
                Blocks = NameFormat.DefaultBlocks(Json.Bool(item["use_date"]) ?? true,
                                                  Json.Bool(item["use_counter"]) ?? false),
            };
        }

        var templates = new List<Template>();
        if (data["templates"] is JsonArray items)
        {
            foreach (var node in items)
            {
                if (node is not JsonObject item || Json.String(item["name"]) is not { } name)
                    continue;
                var id = Json.String(item["id"]);
                templates.Add(new Template(name, Json.Strings(item["folders"]))
                {
                    CustomFormat = Json.Bool(item["custom_format"])
                                   ?? Json.Bool(item["custom_options"]) ?? false,
                    Format = ReadFormat(item),
                    Id = string.IsNullOrEmpty(id) ? Template.NewId() : id,
                });
            }
        }
        return new Config
        {
            Format = ReadFormat(data),
            Language = Json.String(data["language"]) ?? I18n.Auto,
            Templates = templates,
        };
    }

    /// <summary>True for a version 1 file, or one with templates missing their id.</summary>
    internal static bool NeedsUpgrade(JsonObject data) =>
        (Json.Integer(data["version"]) ?? 1) < Version
        || (data["templates"] is JsonArray items
            && items.Any(t => t is JsonObject o && string.IsNullOrEmpty(Json.String(o["id"]))));
}

public static class AppInfo
{
    public const string Id = "io.github.rem2608.FolidStruct";
    /// <summary>Same as folidstruct/core.py; kept in step by scripts/set-version.sh.</summary>
    public const string Version = "0.2.0";
}
