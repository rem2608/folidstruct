using System.Text.Json.Nodes;
using FolidStruct.Core;
using Xunit;

namespace FolidStruct.Core.Tests;

// The tests share Storage.ConfigDir: one collection, so they never run at the same time.
[Collection("Storage")]
public abstract class TempDirTest : IDisposable
{
    protected static readonly DateOnly Today = new(2026, 11, 7);
    protected readonly string Root = Directory.CreateTempSubdirectory("folidstruct-").FullName;
    protected string Work => Path.Combine(Root, "work");

    protected TempDirTest()
    {
        I18n.SetLanguage("en");
        Storage.ConfigDir = Path.Combine(Root, "config");
        Directory.CreateDirectory(Work);
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
        GC.SuppressFinalize(this);
    }

    protected static NameFormat Format(string separator, params Block[] blocks) =>
        new() { Separator = separator, Blocks = [.. blocks] };

    protected static Block Text(string value) => new(BlockKind.Text) { Value = value };

    protected static Block Field(string label, string value = "") =>
        new(BlockKind.Field) { Label = label, Value = value };

    protected static Block Choice(string label, string[] options, string value = "") =>
        new(BlockKind.Choice) { Label = label, Options = [.. options], Value = value };

    protected static Block Date(bool enabled = true) => new(BlockKind.Date, enabled);
    protected static Block Counter(bool enabled = true) => new(BlockKind.Counter, enabled);
    protected static Block NameBlock() => new(BlockKind.Name);
}

[Collection("Storage")]
public class NamingTests : TempDirTest
{
    [Fact]
    public void DefaultFormat()
    {
        var fmt = new NameFormat();
        Assert.Equal("2026-11-07 Logo", Naming.BuildProjectName(fmt, "Logo", Today));
        Assert.Equal("2026-11-07 22# Logo", Naming.BuildProjectName(fmt, "Logo", Today, 22));
        Assert.Equal("22# Logo", Naming.BuildProjectName(fmt, "Logo", null, 22));
        Assert.Equal("Logo", Naming.BuildProjectName(fmt, "  Logo ", null, null));
    }

    [Fact]
    public void CustomBlocksOrderAndSeparator()
    {
        var fmt = Format("_", Field("Client"), Date(), NameBlock(), Text("ITA"),
                         Choice("Kind", ["Print", "Web"]), Counter());
        var name = Naming.BuildProjectName(fmt, "Nuovo marchio", Today, 5,
                                           new Dictionary<int, string> { [0] = "Rossi", [4] = "Web" });
        Assert.Equal("Rossi_2026-11-07_Nuovo marchio_ITA_Web_5#", name);
    }

    [Fact]
    public void EmptyBlocksAreSkipped()
    {
        var fmt = Format(" - ", Field("Client"), Text(""), NameBlock(), Choice("Kind", ["A"]));
        Assert.Equal("Logo", Naming.BuildProjectName(
            fmt, "Logo", values: new Dictionary<int, string> { [0] = "", [3] = "" }));
    }

    [Fact]
    public void DefaultsUsedWhenNoValueGiven()
    {
        var fmt = Format(" ", Field("Client", "Rossi"), NameBlock());
        Assert.Equal("Rossi Logo", Naming.BuildProjectName(fmt, "Logo"));
        Assert.Equal("Logo", Naming.BuildProjectName(fmt, "Logo", values: new Dictionary<int, string> { [0] = "" }));
    }

    [Fact]
    public void WhitespaceIsCollapsed()
    {
        var fmt = Format(" ", Field("Client"), NameBlock());
        Assert.Equal("Ros si srl Logo", Naming.BuildProjectName(
            fmt, "Logo", values: new Dictionary<int, string> { [0] = "Ros\nsi  srl" }));
    }

    [Fact]
    public void ExampleName()
    {
        var fmt = Format(" ", Date(false), Counter(), Field("Cliente"), NameBlock());
        Assert.Equal("3# ‹Cliente› Project", Naming.ExampleName(fmt, counter: 3));
    }

    [Theory]
    [InlineData("Logo Rossi")]
    [InlineData("2026-11-07 22# Logo")]
    [InlineData("Console")]
    [InlineData("CONTO.txt")]
    [InlineData("v1.2")]
    public void ValidNames(string name) => Assert.Null(Naming.ValidateName(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:")]
    [InlineData("what?")]
    [InlineData("a*b")]
    [InlineData("\"quoted\"")]
    [InlineData("<tag>")]
    [InlineData("a|b")]
    [InlineData("tab\there")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("Logo.")]
    [InlineData("Logo ")]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("com1")]
    [InlineData("LPT9.txt")]
    [InlineData("aux .doc")]
    public void InvalidNames(string name) => Assert.NotNull(Naming.ValidateName(name));

    [Fact]
    public void TooLong()
    {
        Assert.Null(Naming.ValidateName(new string('a', 255)));
        Assert.NotNull(Naming.ValidateName(new string('a', 256)));
    }

    [Fact]
    public void Translated()
    {
        I18n.SetLanguage("it");
        Assert.Equal("Il nome non può essere vuoto", Naming.ValidateName(""));
        Assert.Equal("“CON” è un nome riservato di Windows", Naming.ValidateName("con"));
    }
}

[Collection("Storage")]
public class FormatTests
{
    [Fact]
    public void Roundtrip()
    {
        var fmt = new NameFormat
        {
            Separator = "_",
            Blocks =
            [
                new Block(BlockKind.Name),
                new Block(BlockKind.Choice) { Label = "Kind", Options = ["Print", "Web"], Value = "Web" },
                new Block(BlockKind.Counter, true),
                new Block(BlockKind.Text) { Value = "ITA" },
                new Block(BlockKind.Date, false),
                new Block(BlockKind.Field) { Label = "Client" },
            ],
        };
        var json = fmt.ToJson().ToJsonString();
        Assert.Equal(json, NameFormat.FromJson(JsonNode.Parse(json)).ToJson().ToJsonString());
    }

    [Fact]
    public void RepairsDamagedFormat()
    {
        var fmt = NameFormat.FromJson(JsonNode.Parse("""
            {"separator": "/", "blocks": [{"kind": "name"}, {"kind": "name"}, {"kind": "bogus"},
             "junk", {"kind": "text", "value": "x"}]}
            """));
        Assert.Equal(" ", fmt.Separator);
        Assert.Equal(["date", "counter", "name", "text"], fmt.Blocks.Select(b => b.Kind));
        Assert.False(fmt.Block(BlockKind.Counter).Enabled);
        Assert.True(fmt.Block(BlockKind.Date).Enabled);
    }

    [Fact]
    public void ReadsTheLinuxFormat()
    {
        // A config.json block list as written by the Python version.
        var fmt = NameFormat.FromJson(JsonNode.Parse("""
            {"separator": " - ", "blocks": [
              {"kind": "date", "enabled": true}, {"kind": "counter", "enabled": false},
              {"kind": "choice", "label": "Tipo", "value": "", "options": ["Stampa", "Web"]},
              {"kind": "name"}]}
            """));
        Assert.Equal(" - ", fmt.Separator);
        Assert.Equal(["Stampa", "Web"], fmt.Blocks[2].Options);
        Assert.Equal("Tipo", fmt.Blocks[2].Label);
    }
}

[Collection("Storage")]
public class CreationTests : TempDirTest
{
    private readonly Template template = new("Graphics", ["Fonts", "Images", "Print"]);

    [Fact]
    public void CreateWithDate()
    {
        var path = Storage.CreateProject(Work, template, "Logo", new NameFormat(), true, false, today: Today);
        Assert.Equal("2026-11-07 Logo", Path.GetFileName(path));
        Assert.Equal(["Fonts", "Images", "Print"],
                     Directory.GetDirectories(path).Select(Path.GetFileName).Order());
        Assert.Equal(1, Storage.ReadCounter(template)); // not consumed
    }

    [Fact]
    public void CreateWithCustomBlocks()
    {
        var fmt = Format("_", Date(), Counter(false), Field("Client"), NameBlock());
        var path = Storage.CreateProject(Work, template, "Logo", fmt, true, false,
                                         new Dictionary<int, string> { [2] = "Rossi" }, Today);
        Assert.Equal("2026-11-07_Rossi_Logo", Path.GetFileName(path));
    }

    [Fact]
    public void ValueWithInvalidCharsIsRejected()
    {
        var fmt = Format(" ", Field("Client"), NameBlock());
        Assert.Throws<AppException>(() => Storage.CreateProject(
            Work, template, "Logo", fmt, false, false, new Dictionary<int, string> { [0] = "a:b" }));
        Assert.Empty(Directory.GetFileSystemEntries(Work));
    }

    [Fact]
    public void CountersArePerTemplate()
    {
        var other = new Template("Video");
        Storage.WriteCounter(template, 22);
        var fmt = new NameFormat();
        var a = Storage.CreateProject(Work, template, "A", fmt, false, true);
        var b = Storage.CreateProject(Work, template, "B", fmt, false, true);
        var c = Storage.CreateProject(Work, other, "C", fmt, false, true);
        Assert.Equal(["22# A", "23# B", "1# C"], new[] { a, b, c }.Select(Path.GetFileName));
        Assert.Equal(24, Storage.ReadCounter(template));
        Assert.Equal(2, Storage.ReadCounter(other));
    }

    [Fact]
    public void ForgetCounter()
    {
        Storage.WriteCounter(template, 7);
        Storage.ForgetCounter(template);
        Assert.Equal(1, Storage.ReadCounter(template));
    }

    [Fact]
    public void ExistingFolderIsNotTouchedAndCounterKept()
    {
        Storage.WriteCounter(template, 5);
        Directory.CreateDirectory(Path.Combine(Work, "Logo"));
        File.WriteAllText(Path.Combine(Work, "Logo", "file.txt"), "keep");
        Assert.Throws<AppException>(() =>
            Storage.CreateProject(Work, template, "Logo", new NameFormat(), false, false));
        Assert.Equal(["file.txt"], Directory.GetFileSystemEntries(Path.Combine(Work, "Logo"))
                                            .Select(Path.GetFileName));
        Assert.Equal(5, Storage.ReadCounter(template));
    }

    [Fact]
    public void DuplicateAndInvalidFoldersAreSkipped()
    {
        var t = new Template("T", ["Fonts", "Fonts ", "fonts", "a/b", "", "CON", "Print"]);
        var path = Storage.CreateProject(Work, t, "Logo", new NameFormat(), false, false);
        Assert.Equal(["Fonts", "Print"], Directory.GetDirectories(path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void MissingParentGivesAMessage()
    {
        var error = Assert.Throws<AppException>(() => Storage.CreateProject(
            Path.Combine(Work, "missing"), template, "Logo", new NameFormat(), false, true));
        Assert.Equal("The folder could not be found", error.Message);
        Assert.Equal(1, Storage.ReadCounter(template));
    }
}

[Collection("Storage")]
public class ConfigTests : TempDirTest
{
    [Fact]
    public void DefaultConfig()
    {
        var config = Storage.LoadConfig();
        Assert.Single(config.Templates);
        Assert.Equal("Graphics", config.Templates[0].Name);
        Assert.Equal(4, config.Templates[0].Folders.Count);
        Assert.Equal(["date", "counter", "name"], config.Format.Blocks.Select(b => b.Kind));
        Assert.True(config.Format.Block(BlockKind.Date).Enabled);
        Assert.False(config.Format.Block(BlockKind.Counter).Enabled);
    }

    [Fact]
    public void Roundtrip()
    {
        var config = Config.Default();
        config.Language = "it";
        config.Format.Separator = "_";
        config.Templates.Add(new Template("Vidéo", ["Rushes"]) { CustomFormat = true });
        Storage.SaveConfig(config);
        Assert.Equal(config.ToJson().ToJsonString(), Storage.LoadConfig().ToJson().ToJsonString());
        Assert.Contains("Vidéo", File.ReadAllText(Storage.ConfigPath)); // not escaped
    }

    [Fact]
    public void FormatFor()
    {
        var config = Config.Default();
        var template = config.Templates[0];
        Assert.Same(config.Format, config.FormatFor(template));
        template.CustomFormat = true;
        Assert.Same(template.Format, config.FormatFor(template));
    }

    [Fact]
    public void NewTemplateCopiesGlobalFormat()
    {
        var config = Config.Default();
        config.Format.Separator = "-";
        var template = config.NewTemplate("New");
        Assert.Equal(config.Format.ToJson().ToJsonString(), template.Format.ToJson().ToJsonString());
        Assert.NotSame(config.Format, template.Format);
    }

    [Fact]
    public void TranslateExample()
    {
        var config = Config.Default();
        Assert.True(config.TranslateExample("it"));
        Assert.Equal("Grafica", config.Templates[0].Name);
        Assert.Equal(["Font", "Immagini", "Bozze", "Stampa"], config.Templates[0].Folders);
        config.Templates[0].Folders.Add("Web"); // edited: kept as it is
        Assert.False(config.TranslateExample("en"));
    }

    [Fact]
    public void UpgradeFromVersion1()
    {
        Directory.CreateDirectory(Storage.ConfigDir);
        File.WriteAllText(Storage.ConfigPath, """
            {"language": "it", "use_date": true, "use_counter": false,
             "templates": [{"name": "Grafica", "folders": ["Font"]},
                           {"name": "Video", "folders": [], "custom_options": true,
                            "use_date": false, "use_counter": true}]}
            """);
        File.WriteAllText(Path.Combine(Storage.ConfigDir, "counter"), "23\n");

        var config = Storage.LoadConfig();
        var (graphics, video) = (config.Templates[0], config.Templates[1]);
        Assert.Equal("it", config.Language);
        Assert.False(graphics.CustomFormat);
        Assert.True(config.Format.Block(BlockKind.Date).Enabled);
        Assert.False(config.Format.Block(BlockKind.Counter).Enabled);
        Assert.True(video.CustomFormat);
        Assert.False(video.Format.Block(BlockKind.Date).Enabled);
        Assert.True(video.Format.Block(BlockKind.Counter).Enabled);

        Assert.Equal(23, Storage.ReadCounter(graphics));
        Assert.Equal(23, Storage.ReadCounter(video));
        Assert.False(File.Exists(Path.Combine(Storage.ConfigDir, "counter")));
        var saved = JsonNode.Parse(File.ReadAllText(Storage.ConfigPath))!;
        Assert.Equal(Config.Version, (int)saved["version"]!);
        Assert.Equal([graphics.Id, video.Id], Storage.LoadConfig().Templates.Select(t => t.Id));
    }

    [Fact]
    public void DamagedConfig()
    {
        Directory.CreateDirectory(Storage.ConfigDir);
        File.WriteAllText(Storage.ConfigPath, "{not json");
        Assert.Throws<AppException>(Storage.LoadConfig);
        File.WriteAllText(Storage.ConfigPath, "[]");
        Assert.Throws<AppException>(Storage.LoadConfig);
    }
}
