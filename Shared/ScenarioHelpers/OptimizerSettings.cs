// =============================================================
// OptimizerSettings.cs — Les réglages que l'implantation lit, et rien d'autre.
//
// The optimiser is domain code: it knows nothing of a database, and it is given its options rather than
// fetching them. This is the one place that turns the stored settings into those options, so the rules for
// reading a bad value live together - and every one of them is the same rule: a value nobody can read
// never silently changes what the optimiser does. It falls back to the default and says so.
// =============================================================

using AliantWebS.Data;
using RiggingOptimizer.Core;
using RiggingOptimizer.Models;

namespace RiggingOptimizer;

public static class OptimizerSettings
{
    /// <summary>
    /// The options for a run, read from the AppSetting table: the orthogonal restriction, the ranking that
    /// decides which of two layouts is kept, and how hard the optimiser looks. Anything it could not read
    /// is reported in <paramref name="notes"/>, in French, for the console and the user.
    /// </summary>
    public static async Task<OptimizerOptions> ReadAsync(List<string>? notes = null)
    {
        // "Orientations orthogonales uniquement": one value for everyone, on the Settings page.
        bool orthogonalOnly = await AppSettings.GetBoolAsync(AppSettings.OptimizerOrthogonalOnly, false);

        // The ranking: which of two possible layouts the optimiser keeps. A developer may reorder it on the
        // Settings page; an unreadable order is refused rather than guessed at, because a half-understood
        // ranking would quietly place differently without anyone being told.
        var stored = await AppSettings.GetStringAsync(AppSettings.OptimizerObjectiveOrder, null);
        var ranking = Objective.Parse(stored, out var problem);
        if (problem is not null)
        {
            Console.Error.WriteLine("⚠️ " + problem);
            notes?.Add(problem);
        }

        // How the layout is looked for. Unknown value: the fast one, which is what every run did before the
        // setting existed.
        var levelText = await AppSettings.GetStringAsync(AppSettings.OptimizerSearchLevel, null);
        var search = SearchLevel.Bounds;
        if (!string.IsNullOrWhiteSpace(levelText)
            && !Enum.TryParse(levelText.Trim(), ignoreCase: true, out search))
        {
            search = SearchLevel.Bounds;
            var note = $"Le niveau de recherche « {levelText.Trim()} » n'est pas reconnu : "
                     + "la recherche rapide a été utilisée.";
            Console.Error.WriteLine("⚠️ " + note);
            notes?.Add(note);
        }

        return new OptimizerOptions
        {
            OrthogonalOnly = orthogonalOnly,
            Ranking = ranking,
            Search = search,
        };
    }

    /// <summary>What the options say, in one French line, for the console and the study.</summary>
    public static string Describe(OptimizerOptions options) =>
        (options.OrthogonalOnly
            ? "Orientations orthogonales uniquement : aucun palonnier incliné."
            : "Palonniers inclinés autorisés.")
        + " Recherche "
        + (options.Search == SearchLevel.Exact ? "exacte (avec preuve)." : "rapide (avec borne).")
        + " " + Objective.DescribeRanking(options.RankingInForce) + ".";
}
