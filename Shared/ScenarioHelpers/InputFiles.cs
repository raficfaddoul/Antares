// =============================================================
// InputFiles.cs — Finding a scenario's input files by name.
// =============================================================

namespace RiggingOptimizer.IO;

public static class InputFiles
{
    /// <summary>
    /// The file called <paramref name="fileName"/> in <paramref name="folder"/>, whatever the case of its
    /// name; null when there is none, or no such folder. Windows ignores case, but the Docker image runs on
    /// Linux, which does not: there an exact-name test ignored a scenario's own "mother_alc500.xlsx",
    /// uploaded with the browser's casing, and silently used the shared copy - with the shared capacities
    /// and masses. Two files whose names differ only in case are refused, since which one was meant
    /// cannot be known.
    /// </summary>
    public static string? Find(string? folder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;

        var matches = Directory.EnumerateFiles(folder)
            .Where(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count > 1)
        {
            throw new IOException(
                $"Le dossier « {folder} » contient plusieurs fichiers « {fileName} » dont les noms ne diffèrent "
                + $"que par les majuscules : {string.Join(", ", matches.Select(path => Path.GetFileName(path)))}. "
                + "Gardez-en un seul.");
        }

        return matches.Count == 1 ? matches[0] : null;
    }
}
