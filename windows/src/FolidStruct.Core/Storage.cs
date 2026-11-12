using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using static FolidStruct.Core.I18n;

namespace FolidStruct.Core;

/// <summary>An error with a message that can be shown to the user.</summary>
public sealed class AppException(string message) : Exception(message);

/// <summary>
/// Configuration files and folder creation.
///
/// Files, inside <see cref="ConfigDir"/>:
/// <list type="bullet">
/// <item>config.json – options and templates, edited by the manager window;</item>
/// <item>counters.json – the next progressive number of each template.</item>
/// </list>
/// The counters live in their own file so that a manager window left open with an
/// old copy of the configuration can never roll the numbering back.
/// </summary>
public static class Storage
{
    /// <summary>
    /// Where the files are. The packaged app sets it to its LocalState folder, which
    /// the File Explorer menu reads too; FOLIDSTRUCT_CONFIG_DIR overrides it (tests).
    /// </summary>
    public static string ConfigDir { get; set; } =
        Environment.GetEnvironmentVariable("FOLIDSTRUCT_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                           "FolidStruct");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");
    public static string CountersPath => Path.Combine(ConfigDir, "counters.json");
    /// <summary>The single shared counter of version 1 of the configuration.</summary>
    private static string LegacyCounterPath => Path.Combine(ConfigDir, "counter");

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep accents readable
    };

    // --- Files ---------------------------------------------------------------

    /// <summary>
    /// Serialize writes between concurrent FolidStruct processes.
    /// Not reentrant: code holding the lock must not call a method that takes it.
    /// </summary>
    private static FileStream Lock()
    {
        Directory.CreateDirectory(ConfigDir);
        var path = Path.Combine(ConfigDir, ".lock");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50); // held by another process
            }
        }
    }

    private static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
        {
            writer.Write(text);
            writer.Flush();
            stream.Flush(flushToDisk: true); // never leave an empty file after a power loss
        }
        // The File Explorer menu may be reading the file at this very moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static void WriteJson(string path, JsonNode data) =>
        WriteAtomic(path, data.ToJsonString(WriteOptions) + "\n");

    // --- Configuration -------------------------------------------------------

    private static JsonObject? ReadConfigData()
    {
        string text;
        try
        {
            text = File.ReadAllText(ConfigPath);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException e)
        {
            throw Damaged(e.Message);
        }
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException e)
        {
            throw Damaged(e.Message);
        }
        return node as JsonObject ?? throw Damaged("not an object");
    }

    private static AppException Damaged(string error) =>
        new(T("The configuration file is damaged: {error}", ("error", error)));

    /// <summary>
    /// Load the configuration, or the defaults if none has been saved yet.
    /// An older configuration is converted and saved once, so that template ids
    /// (and with them the counters) stay the same from then on.
    /// </summary>
    public static Config LoadConfig()
    {
        var data = ReadConfigData();
        if (data is null)
            return Config.Default();
        var config = Config.FromJson(data);
        if (!Config.NeedsUpgrade(data))
            return config;
        try
        {
            using var _ = Lock();
            data = ReadConfigData(); // another process may have upgraded it
            if (data is not null)
            {
                config = Config.FromJson(data);
                if (Config.NeedsUpgrade(data))
                {
                    UpgradeCounters(config);
                    SaveConfig(config);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Read-only configuration: use it as it is.
        }
        return config;
    }

    /// <summary>Version 1 had one counter for all templates: each template starts from it.</summary>
    private static void UpgradeCounters(Config config)
    {
        if (!File.Exists(LegacyCounterPath)
            || !int.TryParse(File.ReadAllText(LegacyCounterPath).Trim(), out var start))
            return;
        start = Math.Max(1, start);
        var counters = ReadCounters();
        foreach (var template in config.Templates)
            counters.TryAdd(template.Id, start);
        WriteJson(CountersPath, counters);
        File.Delete(LegacyCounterPath);
    }

    public static void SaveConfig(Config config) => WriteJson(ConfigPath, config.ToJson());

    /// <summary>Write the default configuration if none exists yet.</summary>
    public static void EnsureConfig()
    {
        if (!File.Exists(ConfigPath))
            SaveConfig(Config.Default());
    }

    // --- Counters ------------------------------------------------------------

    private static JsonObject ReadCounters()
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(CountersPath)) as JsonObject ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return [];
        }
    }

    private static int CounterFrom(JsonObject counters, Template template) =>
        (int)Math.Clamp(Json.Integer(counters[template.Id]) ?? 1, 1, int.MaxValue);

    /// <summary>The next progressive number of <paramref name="template"/>.</summary>
    public static int ReadCounter(Template template) => CounterFrom(ReadCounters(), template);

    public static void WriteCounter(Template template, int value)
    {
        try
        {
            using var _ = Lock();
            var counters = ReadCounters();
            counters[template.Id] = Math.Max(1, value);
            WriteJson(CountersPath, counters);
        }
        catch (UnauthorizedAccessException)
        {
            throw new AppException(T("Permission denied"));
        }
        catch (IOException e)
        {
            throw new AppException(e.Message);
        }
    }

    /// <summary>Drop the counter of a deleted template.</summary>
    public static void ForgetCounter(Template template)
    {
        try
        {
            using var _ = Lock();
            var counters = ReadCounters();
            if (counters.Remove(template.Id))
                WriteJson(CountersPath, counters);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover number is harmless.
        }
    }

    // --- Creation ------------------------------------------------------------

    /// <summary>
    /// Create the project folder and its subfolders inside <paramref name="parent"/>.
    /// <paramref name="format"/> is the name format to use and <paramref name="values"/>
    /// the content of its field and choice blocks (see Naming.BuildProjectName). The
    /// template's progressive number is consumed only if the creation succeeds.
    /// Returns the path of the new project folder.
    /// </summary>
    public static string CreateProject(string parent, Template template, string name, NameFormat format,
                                       bool useDate, bool useCounter,
                                       IReadOnlyDictionary<int, string>? values = null, DateOnly? today = null)
    {
        if (Naming.ValidateName(name) is { } error)
            throw new AppException(error);

        // Skip invalid names and duplicates (e.g. "Fonts" and "fonts ").
        var folders = template.Folders
            .Select(f => f.Trim())
            .Where(f => Naming.ValidateName(f) is null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!Directory.Exists(parent))
            throw new AppException(T("The folder could not be found"));

        try
        {
            using var _ = Lock();
            var counters = ReadCounters();
            int? counter = useCounter ? CounterFrom(counters, template) : null;
            DateOnly? date = useDate ? today ?? DateOnly.FromDateTime(DateTime.Now) : null;
            var folderName = Naming.BuildProjectName(format, name, date, counter, values);
            if (Naming.ValidateName(folderName) is { } nameError)
                throw new AppException(nameError);

            var target = Path.Combine(parent, folderName);
            if (Directory.Exists(target) || File.Exists(target))
                throw new AppException(T("A folder named “{name}” already exists here", ("name", folderName)));
            Directory.CreateDirectory(target);

            try
            {
                foreach (var folder in folders)
                    Directory.CreateDirectory(Path.Combine(target, folder));
            }
            catch
            {
                // Undo: everything inside is ours and still empty.
                foreach (var folder in folders)
                {
                    try { Directory.Delete(Path.Combine(target, folder)); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
                Directory.Delete(target);
                throw;
            }

            if (counter is { } used)
            {
                counters[template.Id] = used + 1;
                WriteJson(CountersPath, counters);
            }
            return target;
        }
        catch (UnauthorizedAccessException)
        {
            throw new AppException(T("Permission denied"));
        }
        catch (IOException e)
        {
            throw new AppException(e.Message);
        }
    }
}
