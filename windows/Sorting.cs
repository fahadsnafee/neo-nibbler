using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AstromechDesk;

sealed record Rule(string Extension, string Folder);
sealed record Settings(Rule[] Rules, bool Sound, [property: JsonPropertyName("PdfTooltips")] bool FileTooltips = true, bool AutoMove = false, bool WatchDesktop = false, bool ChaseFiles = true, bool ChaseMouse = true, int MouseChaseSeconds = 5);
sealed record Candidate(string Source, string Folder, long Size, DateTime Modified)
{
    public string Name => Path.GetFileName(Source);
}
sealed record MoveResult(string Name, string Outcome, bool Moved);

static class Sorting
{
    private const FileAttributes Excluded = FileAttributes.System | FileAttributes.Hidden |
        FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Offline;

    private static readonly string LocalData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string DataFolder = Path.Combine(LocalData, "NeoNibbler");
    private static string SettingsPath => Path.Combine(DataFolder, "settings.json");

    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static Rule CreateRule(string extension, string folder)
    {
        extension = "." + extension.Trim().TrimStart('.').ToLowerInvariant();
        if (!Regex.IsMatch(extension, @"^\.[a-z0-9_+-]{1,24}$"))
            throw new ArgumentException("Enter one extension, such as .pdf or .jpg.");
        if (!Directory.Exists(folder))
            throw new ArgumentException("Select an existing destination folder.");
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (string.Equals(folder, Path.TrimEndingDirectorySeparator(Desktop), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select a folder other than the Desktop itself.");
        return new Rule(extension, folder);
    }

    public static Settings Load()
    {
        if (File.Exists(SettingsPath)) return ReadSettings(SettingsPath);
        string previous = Path.Combine(LocalData, "AstromechDesk", "settings.json");
        if (!File.Exists(previous)) return new Settings([], true);

        Settings settings = ReadSettings(previous);
        Save(settings);
        return settings;
    }

    private static Settings ReadSettings(string path) =>
        JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))
        ?? throw new InvalidDataException("The settings file is empty.");

    public static void Save(Settings settings)
    {
        Directory.CreateDirectory(DataFolder);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, SettingsPath, overwrite: true);
    }

    public static Candidate[] ScanFiles(IEnumerable<string> paths, IEnumerable<Rule> rules)
    {
        var destinations = rules.ToDictionary(rule => rule.Extension, rule => rule.Folder,
            StringComparer.OrdinalIgnoreCase);
        var matches = new List<Candidate>();
        foreach (string path in paths)
        {
            if (!destinations.TryGetValue(Path.GetExtension(path), out string? folder)) continue;
            var file = new FileInfo(path);
            if (!file.Exists || (file.Attributes & Excluded) != 0) continue;
            try { matches.Add(new Candidate(file.FullName, folder, file.Length, file.LastWriteTimeUtc)); }
            catch (FileNotFoundException) { }
        }

        return matches.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static MoveResult Move(Candidate candidate)
    {
        try
        {
            var source = new FileInfo(candidate.Source);
            if (!source.Exists || (source.Attributes & Excluded) != 0 ||
                source.Length != candidate.Size || source.LastWriteTimeUtc != candidate.Modified)
                return new(candidate.Name, "Changed since scan. Scan again.", false);
            if (!Directory.Exists(candidate.Folder))
                return new(candidate.Name, "Destination folder is unavailable.", false);

            string stem = Path.GetFileNameWithoutExtension(candidate.Name);
            string extension = Path.GetExtension(candidate.Name);
            for (int suffix = 0; suffix < 10000; suffix++)
            {
                string name = suffix == 0 ? candidate.Name : $"{stem} ({suffix}){extension}";
                string target = Path.Combine(candidate.Folder, name);
                if (File.Exists(target) || Directory.Exists(target)) continue;
                try
                {
                    File.Move(candidate.Source, target, overwrite: false);
                    return new(candidate.Name, target, true);
                }
                catch (IOException) when (File.Exists(target) && File.Exists(candidate.Source)) { }
            }
            return new(candidate.Name, "Could not find an available filename.", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(candidate.Name, ex.Message, false);
        }
    }
}
