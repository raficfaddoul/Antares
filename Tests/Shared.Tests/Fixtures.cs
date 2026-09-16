using System.Reflection;

namespace Shared.Tests;

/// <summary>
/// Where the test workbooks are. They are copied beside the test assembly at build time, so a test reads
/// the venue's real inventory and the real plan without knowing anything about the machine it runs on.
/// </summary>
public static class Fixtures
{
    /// <summary>The folder holding Charges.xlsx, MotherGrill.xlsx and Palonnier.xlsx.</summary>
    public static string Dir { get; } = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Fixtures");

    public static string Charges => Path.Combine(Dir, "Charges.xlsx");

    /// <summary>
    /// The examples the application publishes, checked here so a wrong one cannot ship. Copied beside the
    /// test assembly by the project file: walking up the source tree would break the moment the build
    /// writes somewhere else, which is exactly what happens when the tests run out of an artifacts folder.
    /// </summary>
    public static string ExamplesDir { get; } = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Examples");
}
