using System.Text;
using AliantWebS.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AliantWebS.Data;

/// <summary>
/// The three directories that belong to one scenario.
/// </summary>
/// <param name="BaseDir">The scenario folder itself.</param>
/// <param name="DataDir">Inputs the user uploads (charges, abaques, production plan).</param>
/// <param name="OutputDir">Everything the generators write (plan, Excel, Word report).</param>
public readonly record struct ScenarioPaths(string BaseDir, string DataDir, string OutputDir);

/// <summary>
/// Single owner of every path under the ScenariosData tree.
///
/// Folders are named <c>{SanitizedName}__{first 8 hex of the entity GUID}</c>. The visible half keeps
/// the tree readable for whoever copies spreadsheets in by hand; the GUID half is what lookups key on.
/// That split is what makes renames safe: resolution finds the folder by id and then corrects its
/// visible name, so renaming an event in the UI moves its files instead of silently orphaning them.
/// Two events sharing a name no longer share a folder either, and a blank name can no longer collapse
/// a path segment to nothing.
///
/// Folders created by the old name-only scheme are adopted on first access, so an existing tree
/// migrates itself without a separate script.
///
/// Scoped, matching <see cref="JournalService"/>: it holds no per-request state, but the pages that
/// use it are scoped and it keeps the DI graph uniform.
/// </summary>
public sealed class ScenarioStorageService
{
    /// <summary>Shared inputs (abaques) that every scenario falls back to.</summary>
    public const string CommonFolderName = "_common";

    /// <summary>Where deleted folders and files go instead of being destroyed.</summary>
    public const string TrashFolderName = "_trash";

    public const string DataFolderName = "data";
    public const string OutputFolderName = "output";

    /// <summary>Configuration key holding the root. Set it next to BackupPath in compose.</summary>
    public const string RootConfigurationKey = "ScenariosDataPath";

    /// <summary>Used when the key is absent: the container path the image has always used.</summary>
    public const string DefaultRoot = "/app/ScenariosData";

    private const string IdSeparator = "__";
    private const int ShortIdLength = 8;
    private const int MaxSegmentLength = 60;
    private const int MaxFileNameLength = 120;
    private const string FallbackSegmentName = "sans-nom";

    private static readonly char[] WhitespaceChars = { ' ', '\t', '\n', '\r', '\f', '\v' };
    private static readonly char[] IllegalTrailingChars = { '.', ' ' };

    // Windows rejects trailing dots/spaces and a wider character set than Linux does. We apply the
    // union everywhere so one database row yields one folder name whether the app runs in the
    // container or under Visual Studio.
    private static readonly char[] ForbiddenChars =
        { '"', '<', '>', '|', ':', '*', '?', '\\', '/', '\0' };

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly ILogger<ScenarioStorageService> _logger;

    public ScenarioStorageService(IConfiguration configuration, ILogger<ScenarioStorageService> logger)
    {
        _logger = logger;
        Root = ResolveRoot(configuration?[RootConfigurationKey]);
    }

    /// <summary>Absolute, fully-qualified root of the ScenariosData tree.</summary>
    public string Root { get; }

    /// <summary>Shared abaque folder consulted when a scenario has no copy of an input file.</summary>
    public string CommonDataDir => Path.Combine(Root, CommonFolderName, DataFolderName);

    /// <summary>Quarantine folder for deleted scenarios, events and files.</summary>
    public string TrashDir => Path.Combine(Root, TrashFolderName);

    // ── Root ──────────────────────────────────────────────────────────────────

    private static string ResolveRoot(string? configured)
    {
        var raw = string.IsNullOrWhiteSpace(configured) ? DefaultRoot : configured!.Trim();

        // A bare "/app/ScenariosData" is fully qualified on Linux but not on Windows, where it would
        // otherwise land on C:\app. Anything not fully qualified is treated as relative to the content
        // root, which is how the Images folder is already resolved in Startup.
        return Path.IsPathFullyQualified(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), raw.TrimStart('/', '\\')));
    }

    // ── Naming ────────────────────────────────────────────────────────────────

    /// <summary>The id half of a folder name.</summary>
    public static string ShortId(Guid id) => id.ToString("N").Substring(0, ShortIdLength);

    /// <summary>
    /// Turns an entity name into the readable half of a folder name. Never returns an empty string,
    /// so a nameless event can no longer collapse a path segment and silently retarget the tree.
    /// </summary>
    public static string SanitizeSegment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return FallbackSegmentName;

        var sb = new StringBuilder(raw!.Length);
        foreach (var ch in raw)
        {
            if (char.IsControl(ch)) continue;
            if (Array.IndexOf(ForbiddenChars, ch) >= 0) continue;
            sb.Append(ch);
        }

        var collapsed = string.Join(' ',
            sb.ToString().Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries));

        var trimmed = collapsed.Trim().TrimEnd(IllegalTrailingChars);
        if (trimmed.Length > MaxSegmentLength)
            trimmed = trimmed.Substring(0, MaxSegmentLength).Trim().TrimEnd(IllegalTrailingChars);

        // "." and ".." reduce to empty here, so traversal cannot survive sanitization.
        return trimmed.Length == 0 ? FallbackSegmentName : trimmed;
    }

    /// <summary>The folder name an entity should have right now.</summary>
    public static string FolderNameFor(Guid id, string? name) =>
        SanitizeSegment(name) + IdSeparator + ShortId(id);

    /// <summary>
    /// Cleans a browser-supplied upload name down to a bare file name. Returns null when nothing
    /// usable survives, which the caller must treat as a rejected file rather than a default.
    /// </summary>
    public static string? SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Backslash is not a separator on Linux, so strip both flavours before taking the leaf.
        var leaf = Path.GetFileName(raw!.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf)) return null;

        var sb = new StringBuilder(leaf.Length);
        foreach (var ch in leaf)
        {
            if (char.IsControl(ch)) continue;
            if (Array.IndexOf(ForbiddenChars, ch) >= 0) continue;
            sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim().TrimEnd(IllegalTrailingChars);
        if (cleaned.Length > MaxFileNameLength)
        {
            var ext = Path.GetExtension(cleaned);
            var stem = Path.GetFileNameWithoutExtension(cleaned);
            var room = Math.Max(1, MaxFileNameLength - ext.Length);
            cleaned = stem.Substring(0, Math.Min(stem.Length, room)).TrimEnd(IllegalTrailingChars) + ext;
        }

        if (cleaned.Length == 0 || cleaned == "." || cleaned == "..") return null;
        return cleaned;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> resolves inside <paramref name="root"/>. Every path that
    /// originates outside the server must pass through this before being opened.
    /// </summary>
    public static bool IsInside(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate)) return false;

        var normalizedRoot = Path.GetFullPath(root);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
            normalizedRoot += Path.DirectorySeparatorChar;

        return Path.GetFullPath(candidate).StartsWith(normalizedRoot, PathComparison);
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    /// <summary>Folder of one event, renaming or adopting an existing folder when needed.</summary>
    public string GetEventDir(Guid eventId, string? eventName, bool create = true) =>
        ResolveSegment(Root, eventId, eventName, create);

    /// <inheritdoc cref="GetEventDir(Guid, string?, bool)"/>
    public string GetEventDir(Event ev, bool create = true) =>
        GetEventDir(ev.EventId, ev.Name, create);

    /// <summary>Base, data and output folders of one scenario.</summary>
    public ScenarioPaths GetScenarioPaths(
        Guid eventId, string? eventName,
        Guid scenarioId, string? scenarioName,
        bool create = true)
    {
        var eventDir = ResolveSegment(Root, eventId, eventName, create);
        var baseDir = ResolveSegment(eventDir, scenarioId, scenarioName, create);

        var dataDir = Path.Combine(baseDir, DataFolderName);
        var outputDir = Path.Combine(baseDir, OutputFolderName);

        if (create)
        {
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(outputDir);
        }

        return new ScenarioPaths(baseDir, dataDir, outputDir);
    }

    /// <inheritdoc cref="GetScenarioPaths(Guid, string?, Guid, string?, bool)"/>
    public ScenarioPaths GetScenarioPaths(Event ev, EventMpscenario scenario, bool create = true) =>
        GetScenarioPaths(ev.EventId, ev.Name, scenario.EventMpscenarioId, scenario.Name, create);

    /// <summary>
    /// Locates a scenario folder from ids alone, without creating or renaming anything. Used by the
    /// download endpoint, which must not have the side effects of the resolving overloads.
    /// </summary>
    public ScenarioPaths? FindScenarioPaths(Guid eventId, Guid scenarioId)
    {
        var eventDir = FindTagged(Root, eventId);
        if (eventDir is null) return null;

        var baseDir = FindTagged(eventDir, scenarioId);
        if (baseDir is null) return null;

        return new ScenarioPaths(
            baseDir,
            Path.Combine(baseDir, DataFolderName),
            Path.Combine(baseDir, OutputFolderName));
    }

    /// <summary>
    /// Path of an input file for a scenario: its own copy if present, otherwise the shared copy under
    /// <see cref="CommonDataDir"/>. Returns the scenario path when neither exists, so the resulting
    /// error names the folder the user is actually looking at.
    /// </summary>
    public string ResolveInputFile(string scenarioDataDir, string fileName)
    {
        // Whatever the case of its name. On Linux - the Docker image - an exact-name test ignored a
        // scenario's own "mother_alc500.xlsx" and silently used the shared copy. See InputFiles.Find.
        return RiggingOptimizer.IO.InputFiles.Find(scenarioDataDir, fileName)
            ?? RiggingOptimizer.IO.InputFiles.Find(CommonDataDir, fileName)
            ?? Path.Combine(scenarioDataDir, fileName);
    }

    private string ResolveSegment(string parentDir, Guid id, string? name, bool create)
    {
        var desiredName = FolderNameFor(id, name);
        var desiredPath = Path.Combine(parentDir, desiredName);

        if (!Directory.Exists(parentDir))
        {
            if (!create) return desiredPath;
            Directory.CreateDirectory(desiredPath);
            return desiredPath;
        }

        // 1. A folder already tagged with this id owns the entity, whatever its visible name says.
        //    If the name drifted (the entity was renamed) we correct it here rather than stranding
        //    the files under the old name.
        var tagged = FindTagged(parentDir, id);
        if (tagged is not null)
        {
            if (string.Equals(Path.GetFileName(tagged), desiredName, StringComparison.Ordinal))
                return tagged;

            return TryMove(tagged, desiredPath, "renommage") ?? tagged;
        }

        // 2. Nothing tagged: adopt a folder left by the old name-only scheme, so existing trees keep
        //    their contents on first access instead of appearing empty.
        var legacyPath = Path.Combine(parentDir, SanitizeSegment(name));
        var legacyName = Path.GetFileName(legacyPath);
        if (Directory.Exists(legacyPath) && !IsReservedFolder(legacyName) && !HasIdSuffix(legacyName))
        {
            var adopted = TryMove(legacyPath, desiredPath, "migration");
            if (adopted is not null) return adopted;

            // Adoption failed, which one file open in Excel is enough to cause. Keep using the folder
            // that actually holds the data. Creating the tagged folder here instead would make the
            // failure permanent: FindTagged would match it on every later call, this branch would
            // never run again, and the real files would be stranded with no retry.
            return legacyPath;
        }

        if (create) Directory.CreateDirectory(desiredPath);
        return desiredPath;
    }

    private string? FindTagged(string parentDir, Guid id)
    {
        var suffix = IdSeparator + ShortId(id);
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parentDir))
            {
                if (Path.GetFileName(dir).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return dir;
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Raced with a delete; treated as "no folder yet".
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "ScenariosData: lecture de {Parent} refusée.", parentDir);
        }

        return null;
    }

    private string? TryMove(string from, string to, string reason)
    {
        if (string.Equals(from, to, PathComparison)) return from;

        try
        {
            if (Directory.Exists(to))
            {
                _logger.LogWarning(
                    "ScenariosData: {Reason} de {From} abandonné, {To} existe déjà.", reason, from, to);
                return null;
            }

            Directory.Move(from, to);
            _logger.LogInformation("ScenariosData: {Reason} {From} -> {To}.", reason, from, to);
            return to;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file open in Excel is enough to block this. Keeping the old folder is strictly better
            // than failing the page, so callers carry on with the path that actually holds the data.
            _logger.LogWarning(ex, "ScenariosData: {Reason} de {From} vers {To} impossible.", reason, from, to);
            return null;
        }
    }

    private static bool IsReservedFolder(string folderName) =>
        string.Equals(folderName, CommonFolderName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(folderName, TrashFolderName, StringComparison.OrdinalIgnoreCase);

    private static bool HasIdSuffix(string folderName)
    {
        if (folderName.Length < IdSeparator.Length + ShortIdLength) return false;

        var separatorStart = folderName.Length - ShortIdLength - IdSeparator.Length;
        if (string.CompareOrdinal(folderName, separatorStart, IdSeparator, 0, IdSeparator.Length) != 0)
            return false;

        for (var i = folderName.Length - ShortIdLength; i < folderName.Length; i++)
        {
            if (!Uri.IsHexDigit(folderName[i])) return false;
        }

        return true;
    }

    // ── Deletion ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves an event's folder to the trash. Nothing is destroyed, because the folder holds inputs the
    /// user uploaded by hand and a mis-click on a grid row should not be able to lose them.
    /// </summary>
    public string? TrashEventFolder(Guid eventId, string? eventName)
    {
        var dir = FindTagged(Root, eventId) ?? Path.Combine(Root, FolderNameFor(eventId, eventName));
        return Directory.Exists(dir) ? TrashDirectory(dir) : null;
    }

    /// <summary>Moves one scenario's folder to the trash, leaving its event's other scenarios alone.</summary>
    public string? TrashScenarioFolder(Guid eventId, Guid scenarioId)
    {
        var eventDir = FindTagged(Root, eventId);
        if (eventDir is null) return null;

        var dir = FindTagged(eventDir, scenarioId);
        return dir is not null && Directory.Exists(dir) ? TrashDirectory(dir) : null;
    }

    /// <summary>Moves a single file to the trash. Returns its new path, or null if it could not move.</summary>
    public string? TrashFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var target = ReserveTrashPath(Path.GetFileName(filePath));
        try
        {
            File.Move(filePath, target);
            _logger.LogInformation("ScenariosData: fichier mis à la corbeille {From} -> {To}.", filePath, target);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "ScenariosData: mise à la corbeille de {From} impossible.", filePath);
            return null;
        }
    }

    private string? TrashDirectory(string directory)
    {
        var target = ReserveTrashPath(Path.GetFileName(directory));
        return TryMove(directory, target, "mise à la corbeille");
    }

    private string ReserveTrashPath(string name)
    {
        Directory.CreateDirectory(TrashDir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(TrashDir, $"{stamp}-{name}");

        // Two deletes in the same second, or a restored-then-redeleted folder, must not collide.
        var attempt = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
        {
            candidate = Path.Combine(TrashDir, $"{stamp}-{attempt}-{name}");
            attempt++;
        }

        return candidate;
    }
}
