using AliantWebS.Reporting;
using Xunit;

namespace Shared.Tests.Report;

/// <summary>
/// Where the study looks for the production's own plan drawing, and — the point of these rules — where
/// it does NOT look.
///
/// The plan is the show's document: the designer's drawing of what the production intends to fly, which
/// the study prints as its figure 4 and never reads. Every other input of a scenario falls back to the
/// shared « _common/data » folder, because the others describe the VENUE: one inventory of trusses serves
/// every scenario of every event. This one describes one production, and a copy left in the shared folder
/// would become the plan of every study that had not uploaded its own — silently, since a plan that is
/// found suppresses both the figure the study would have drawn from the coordinates and the reserve that
/// explains it.
/// </summary>
public class ProductionPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), nameof(ProductionPlanTests), Guid.NewGuid().ToString("N"));

    private readonly string _scenarioData;
    private readonly string _commonData;

    public ProductionPlanTests()
    {
        // The real shape of the tree: <root>/_common/data beside <root>/<event>/<scenario>/data.
        _scenarioData = Path.Combine(_root, "Evenement", "Scenario", "data");
        _commonData = Path.Combine(_root, "_common", "data");

        Directory.CreateDirectory(_scenarioData);
        Directory.CreateDirectory(_commonData);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private void Write(string folder, string name) =>
        File.WriteAllBytes(Path.Combine(folder, name), new byte[] { 1, 2, 3 });

    [Theory]
    [InlineData("PlanProduction.png")]
    [InlineData("PlanProduction.jpg")]
    [InlineData("PlanProduction.jpeg")]
    [InlineData("plan.png")]
    [InlineData("plan.jpg")]
    [InlineData("plan.jpeg")]
    public void The_scenario_s_own_plan_is_found_under_any_accepted_name(string name)
    {
        Write(_scenarioData, name);

        var found = ReportGenerator.FindUploadedPlan(_scenarioData);

        Assert.True(found is not null, $"« {name} » déposé dans le scénario n'a pas été trouvé");
        Assert.Equal(name, Path.GetFileName(found!));
    }

    [Fact]
    public void A_name_is_matched_whatever_its_case()
    {
        // The image ships in the Docker image, where the file system tells PLANPRODUCTION.PNG from
        // PlanProduction.png and a user does not.
        Write(_scenarioData, "PLANPRODUCTION.PNG");

        Assert.True(ReportGenerator.FindUploadedPlan(_scenarioData) is not null,
            "un plan déposé en majuscules n'a pas été trouvé");
    }

    [Fact]
    public void A_plan_left_in_the_shared_folder_is_never_adopted()
    {
        // THE RULE. « _common/data » is one folder for the whole installation, so this file is not
        // "another scenario's plan" - it is every scenario's plan, for every event, for as long as it
        // sits there. The study says instead that no plan was supplied, and plots the charges itself.
        Write(_commonData, "PlanProduction.png");

        Assert.True(ReportGenerator.FindUploadedPlan(_scenarioData) is null,
            "le plan du dossier partagé « _common/data » a été repris comme plan de la production");
    }

    [Fact]
    public void The_generic_name_in_the_shared_folder_is_never_adopted_either()
    {
        // « plan.png » is the name a stray image is likeliest to carry, and the shared folder is the one
        // users are told to use for the inventory workbooks.
        Write(_commonData, "plan.png");

        Assert.True(ReportGenerator.FindUploadedPlan(_scenarioData) is null,
            "« plan.png » du dossier partagé a été repris comme plan de la production");
    }

    [Fact]
    public void The_scenario_s_plan_wins_over_anything_shared()
    {
        Write(_commonData, "PlanProduction.png");
        Write(_scenarioData, "PlanProduction.png");

        var found = ReportGenerator.FindUploadedPlan(_scenarioData);

        Assert.True(found is not null, "le plan du scénario n'a pas été trouvé");
        Assert.StartsWith(_scenarioData, found!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_scenario_with_no_plan_at_all_reports_none()
    {
        // Not a failure: the study draws the charges from their coordinates and says so in a reserve.
        Assert.True(ReportGenerator.FindUploadedPlan(_scenarioData) is null,
            "un plan a été trouvé alors qu'aucun n'a été déposé");
    }
}
