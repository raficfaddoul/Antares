// =============================================================
// EmbeddedAssets.cs — The fixed images the report always uses.
//
// The company mark and the venue figures do not vary between studies, so they ship inside the
// assembly rather than as files that a deployment could forget to copy. OpenXml needs a path to feed
// an image part from, so each one is unpacked once into a temp folder and reused.
// =============================================================

using System.Reflection;

namespace AliantWebS.Reporting;

public static class EmbeddedAssets
{
    private const string Prefix = "Shared.ScenarioHelpers.Report.Assets.";

    private static readonly Lazy<string?> _rigUpLogo = new(() => Extract("rigup-logo.png"));
    private static readonly Lazy<string?> _rigUpLogoSmall = new(() => Extract("rigup-logo-small.png"));
    private static readonly Lazy<string?> _cidecoLogo = new(() => Extract("cideco-logo.png"));
    private static readonly Lazy<string?> _venueLogo = new(() => Extract("antares-logo.jpg"));
    private static readonly Lazy<string?> _venuePhoto = new(() => Extract("venue-photo.jpg"));
    private static readonly Lazy<string?> _venuePoints = new(() => Extract("venue-points.jpg"));

    /// <summary>
    /// The issuing company's mark for the cover. Two marks ship: the Rig'Up roundel the existing
    /// studies carry, and the CIDECO wordmark. Selected by name so switching issuer is configuration
    /// rather than a code change.
    /// </summary>
    public static string? LogoPath(string? key = null) => Resolve(key).Cover;

    /// <summary>
    /// The mark for the page header. The Rig'Up roundel has a separate, tighter version for this;
    /// the CIDECO wordmark uses the same file at both sizes.
    /// </summary>
    public static string? HeaderLogoPath(string? key = null) => Resolve(key).Header;

    private static (string? Cover, string? Header) Resolve(string? key) =>
        string.Equals(key, "cideco", StringComparison.OrdinalIgnoreCase)
            ? (_cidecoLogo.Value, _cidecoLogo.Value)
            : (_rigUpLogo.Value, _rigUpLogoSmall.Value);

    /// <summary>The venue's own mark, shown on the cover beside the company's.</summary>
    public static string? VenueLogoPath => _venueLogo.Value;

    /// <summary>Interior photograph of the hall, Figure 1.</summary>
    public static string? VenuePhotoPath => _venuePhoto.Value;

    /// <summary>Beam and attachment-point nomenclature diagram, Figure 2.</summary>
    public static string? VenuePointsPath => _venuePoints.Value;

    private static string? Extract(string fileName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(Prefix + fileName);
            if (stream is null) return null;

            // Keyed by assembly version so an upgraded image is not shadowed by a stale extraction.
            var version = assembly.GetName().Version?.ToString() ?? "0";
            var directory = Path.Combine(Path.GetTempPath(), "AliantWebS.ReportAssets", version);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, fileName);
            if (File.Exists(path) && new FileInfo(path).Length == stream.Length) return path;

            using var file = File.Create(path);
            stream.CopyTo(file);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A figure that cannot be unpacked is skipped rather than failing the whole document.
            Console.Error.WriteLine($"Rapport : image « {fileName} » indisponible ({ex.Message}).");
            return null;
        }
    }
}
