using RiggingOptimizer;
using RiggingOptimizer.Core;
using RiggingOptimizer.IO;
using RiggingOptimizer.Models;
using Xunit;

namespace Shared.Tests.Engine;

/// <summary>
/// The inventory as the optimiser uses it: the pieces it may take, and what they are allowed to carry.
///
/// A mother or a palonnier is several pieces of ONE model joined end to end - 5,6 + 2,8 = 8,4 m - and the
/// pieces come from the Nombre column of MotherGrill.xlsx and Palonnier.xlsx. Two rules govern the choice:
/// as few pieces as possible, then as little extra length as possible, then the lightest; and never a
/// piece the venue does not own. A layout that uses a piece twice is a layout the crew cannot build, and
/// nothing further downstream would notice.
///
/// What those pieces may then carry is the owner's rule (2026-09-11): the loads between two neighbouring
/// hanging points may add up to the charge ponctuelle au milieu of the model, read at the length
/// immediately above the span. Past the model's longest row the value is computed at equal moment, and
/// past twice that row there is none at all. LoadTableTests checks that rule as the study applies it;
/// these check the table itself, and the boundaries the study never reaches.
/// </summary>
[Collection(EngineCollection.Name)]
public class StockAndAssemblyTests
{
    private readonly VenuePlan _venue;

    public StockAndAssemblyTests(VenuePlan venue) => _venue = venue;

    // ── Which pieces are taken ───────────────────────────────────

    [Fact]
    public void A_span_no_single_piece_covers_is_reached_with_the_least_extra_length()
    {
        // 3,7 m from 2,8 m and 1,4 m pieces: 2,8 + 1,4 = 4,2 m, two pieces. Three 1,4 m pieces reach the
        // same 4,2 m, so the tie is broken on the count - one joint fewer to make and to trust.
        var long_ = Row("P", 2.8, 2, 2000, 31);
        var short_ = Row("P", 1.4, 3, 3000, 16);

        Assert.Equal(new[] { 2.8, 1.4 }, LengthsOf(Pick(3.7, (long_, 2), (short_, 3))));
    }

    [Fact]
    public void The_same_span_is_reached_with_as_few_pieces_as_the_stock_still_allows()
    {
        var long_ = Row("P", 2.8, 2, 2000, 31);
        var short_ = Row("P", 1.4, 3, 3000, 16);

        // Two 2,8 m pieces are exactly 5,6 m.
        Assert.Equal(new[] { 2.8, 2.8 }, LengthsOf(Pick(5.6, (long_, 2), (short_, 3))));

        // With only one 2,8 m left, the same 5,6 m is still reached - with one more piece.
        Assert.Equal(new[] { 2.8, 1.4, 1.4 }, LengthsOf(Pick(5.6, (long_, 1), (short_, 3))));
    }

    [Fact]
    public void A_piece_the_stock_does_not_hold_is_never_used()
    {
        var five = Row("P", 5.6, 1, 1500, 60);
        var one = Row("P", 1.4, 3, 3000, 16);

        // 8,4 m: one 5,6 m and two 1,4 m.
        Assert.Equal(new[] { 5.6, 1.4, 1.4 }, LengthsOf(Pick(8.4, (five, 1), (one, 2))));

        // With only one 1,4 m left the length cannot be reached at all - and the answer is nothing, never
        // a shorter truss that would then be drawn as if it reached.
        Assert.True(Pick(8.4, (five, 1), (one, 1)) is null,
            "8,4 m ne peuvent pas être atteints avec une pièce de 5,6 m et une de 1,4 m");
    }

    [Fact]
    public void Between_two_pieces_of_one_length_the_lighter_is_taken()
    {
        // Mass is what the attachment points carry on top of the production: at equal length and equal
        // count, the lighter truss is the better one.
        var light = Row("Q", 3.0, 1, 2000, 20);
        var heavy = Row("Q", 3.0, 1, 2000, 40);

        Assert.Same(light, Pick(2.5, (heavy, 1), (light, 1))!.Single());
    }

    [Fact]
    public void A_piece_whose_mass_is_unknown_is_treated_as_the_heavier()
    {
        // An unknown mass is not a light one. Preferring it would quietly put the truss nobody weighed in
        // the rig, and every attachment point it feeds would then read lower than the truth.
        var known = Row("Q", 3.0, 1, 2000, 20);
        var unknown = Row("Q", 3.0, 1, 2000, null);

        Assert.Same(known, Pick(2.5, (unknown, 1), (known, 1))!.Single());
    }

    [Fact]
    public void No_truss_is_assembled_from_more_than_sixteen_pieces()
    {
        // A truss of forty joints exists on paper only. Past Assembler.MaxPieces the answer is nothing, so
        // the charge comes back as uncovered instead of being hung from a fiction.
        Assert.True(Pick(50.0, (Row("P", 1.4, 40, 3000, 16), 40)) is null,
            $"50 m ne s'assemblent pas de pièces de 1,4 m : il en faudrait plus que {Assembler.MaxPieces}");
    }

    // ── The stock itself ─────────────────────────────────────────

    [Fact]
    public void A_row_with_Nombre_zero_gives_a_value_and_is_never_a_piece()
    {
        // Such a row is in the workbook for its charge ponctuelle au milieu alone - the venue owns none of
        // that length. Taken as a piece it would put a truss in the rig that does not exist.
        var stock = new PieceStock(new[]
        {
            Row("P", 2.8, 2, 2000, 31), Row("P", 1.4, 3, 3000, 16), Row("Z", 3.0, 0, 2000, 30),
        });

        Assert.True(stock.Rows.Count == 2,
            "lignes retenues comme pièces : " + string.Join(", ", stock.Rows.Select(r => $"{r.Type} {r.Length} m")));
        Assert.DoesNotContain(stock.Rows, r => r.Type == "Z");
    }

    [Fact]
    public void Taking_pieces_takes_them_out_of_stock_and_giving_them_back_puts_them_in()
    {
        // The optimiser tries a layout, undoes it and tries another: what it gives back has to be there
        // again, to the piece, or the search would slowly starve itself.
        var twoEighty = Row("P", 2.8, 2, 2000, 31);
        var stock = new PieceStock(new[] { twoEighty, Row("P", 1.4, 3, 3000, 16) });

        var assembly = stock.Cheapest("P", 5.6)!;
        stock.Take(assembly);

        Assert.Equal(0, stock.Left(twoEighty));
        Assert.Equal(2, stock.Used(twoEighty));

        stock.Give(assembly);

        Assert.Equal(2, stock.Left(twoEighty));
    }

    [Fact]
    public void What_the_stock_does_not_hold_cannot_be_taken()
    {
        // It refuses rather than lend a piece twice: a stock allowed to go negative would place a layout
        // the crew only discovers on the day, two trusses short.
        var stock = new PieceStock(new[] { Row("P", 2.8, 2, 2000, 31), Row("P", 1.4, 3, 3000, 16) });
        var assembly = stock.Cheapest("P", 5.6)!;
        stock.Take(assembly);

        Assert.Throws<InvalidOperationException>(() => stock.Take(assembly));
    }

    [Fact]
    public void An_assembly_records_the_pieces_it_is_made_of()
    {
        // Stored on the element's row as whole centimetres, longest first, and printed as a person reads
        // it. Without that record the study cannot weigh the truss or say where it is joined.
        var assembly = new Assembly("P", new[] { Row("P", 1.4, 3, 3000, 16), Row("P", 2.8, 2, 2000, 31) });

        Assert.Equal("[280,140]", assembly.Composition);
        Assert.Equal("2,8 + 1,4 m", assembly.Describe());
        Assert.Equal(4.2, assembly.LengthM, 9);
        Assert.Equal(47.0, assembly.WeightKg!.Value, 9);
    }

    [Fact]
    public void An_assembly_holding_one_piece_of_unknown_mass_has_no_known_mass()
    {
        // Not a lighter truss: a truss nobody weighed. The study has to name it rather than add zero.
        var assembly = new Assembly("P", new[] { Row("P", 2.8, 2, 2000, 31), Row("P", 1.4, 3, 3000, null) });

        Assert.Null(assembly.WeightKg);
    }

    // ── What the pieces may carry ────────────────────────────────

    [Fact]
    public void A_span_is_read_at_the_length_immediately_above_it()
    {
        // Reading the longer row is the conservative way round: the same model, longer, is allowed less.
        var table = Table();

        Assert.True(table.At("T", 2.5) is { Kg: 2345, Source: AllowedSource.Table, RowLengthM: 2.8 },
            "2,5 m doivent lire la ligne de 2,8 m : " + table.At("T", 2.5));
        Assert.True(table.At("T", 2.9) is { Kg: 2000, Source: AllowedSource.Table, RowLengthM: 3.0 },
            "2,9 m doivent lire la ligne de 3 m : " + table.At("T", 2.9));
    }

    [Fact]
    public void Two_rows_at_one_length_are_read_at_the_lower_value()
    {
        // Two lines for one piece: neither reading is obviously the one that was meant, so the safe one
        // stands. The workbook is flagged elsewhere; here, nothing is ever allowed more than the lower row.
        Assert.Equal(2000.0, Table().At("U", 5.6).Kg!.Value, 9);
    }

    [Fact]
    public void Past_the_longest_row_the_value_is_computed_at_equal_moment_and_allows_less()
    {
        // A centred point load P bends a truss by P·L/4 and its own weight p per metre by p·L²/8, so the
        // value at a longer span is the P of equal moment. Announced as computed, with the row it came
        // from, or a reader cannot tell it from one the manufacturer published.
        double perMetre = 35.0 / 3.0;
        double moment = 2000 * 3.0 / 4 + perMetre * 3.0 * 3.0 / 8;
        double expected = 4 * (moment - perMetre * 3.5 * 3.5 / 8) / 3.5;

        var computed = Table().At("T", 3.5);

        Assert.Equal(AllowedSource.Computed, computed.Source);
        Assert.Equal(expected, computed.Kg!.Value, 9);
        Assert.Equal(3.0, computed.RowLengthM!.Value, 9);
        Assert.Equal(perMetre, computed.KgPerM!.Value, 9);
        Assert.True(computed.Kg < 2000,
            $"3,5 m doivent admettre moins que les 2 000 kg de la ligne de 3 m, {computed.Kg} kg lus");
    }

    [Fact]
    public void Exactly_twice_the_longest_row_is_still_computed_and_past_it_there_is_no_value()
    {
        // At long spans deflection governs and the formula would allow more than the truss carries, so
        // there is a hard stop. It is a boundary, so both sides of it are pinned: this model's longest row
        // is 3 m, and 6 m is the last span that has a value at all.
        var table = Table();

        Assert.Equal(AllowedSource.Computed, table.At("T", 6.0).Source);
        Assert.True(table.At("T", 6.2) is { Kg: null, Source: AllowedSource.None },
            "au-delà de deux fois la plus grande longueur, aucune valeur : " + table.At("T", 6.2));
    }

    [Fact]
    public void Without_the_longest_row_s_own_weight_nothing_is_computed()
    {
        // The formula needs the truss's own weight. Taken as zero it would allow the most of all, so
        // nothing is computed and the study says which column to fill in.
        var none = Table().At("V", 3.5);

        Assert.Null(none.Kg);
        Assert.True(none.Reason!.Contains("poids propre"),
            "la raison doit envoyer le lecteur vers le poids propre : " + none.Reason);
    }

    [Fact]
    public void A_model_the_file_does_not_list_has_no_value_at_all()
    {
        // Never another model's rows: a palonnier's value lent to a mother line would be read over a bay
        // several times as long.
        Assert.Equal(AllowedSource.None, Table().At("W", 3.0).Source);
    }

    [Fact]
    public void The_venue_s_S500_Carree_Acier_across_its_narrowest_bay_is_computed_from_its_7_m_row()
    {
        // The real case: the narrowest bay of the hall is E-D at 7,144 m, and the venue's mother model
        // stops at 7 m (2 000 kg, 96 kg) - a row nobody holds, Nombre 0, which supplies a value all the
        // same. No row covers the bay, so the value is computed - about 1 958 kg - and every study that
        // quotes a mother line over that bay quotes this number.
        var inventory = InventoryFiles.Load(Fixtures.Dir, null);
        double narrowestBay = Geometry.ColXList.Zip(Geometry.ColXList.Skip(1), (a, b) => b.X - a.X).Min();

        var allowed = inventory.MotherTable.At("S500 Carrée Acier", narrowestBay);

        Assert.Equal(7.144, narrowestBay, 3);
        Assert.Equal(AllowedSource.Computed, allowed.Source);
        Assert.Equal(7.0, allowed.RowLengthM!.Value, 3);
        Assert.Equal(1957.7, allowed.Kg!.Value, 1);
    }

    // ── With no piece in stock at all ────────────────────────────

    [Fact]
    public void With_no_piece_in_stock_nothing_is_placed_and_every_charge_is_named()
    {
        // The values are all still there - only the Nombre columns are empty. Nothing can be built, and
        // every load has to come back named, because the page lists them for the user.
        var result = Optimizer.Optimize(
            _venue.Charges, Emptied(_venue.Palonniers), Emptied(_venue.Mothers), verbose: false);

        Assert.Empty(result.Palonniers);
        Assert.Empty(result.Mothers);
        Assert.Equal(_venue.Charges.Count, result.Uncovered.Count);
    }

    [Fact]
    public void The_refusal_says_the_stock_is_empty_and_that_the_layout_was_left_alone()
    {
        // Saving REPLACES the scenario's elements, so a refusal has to say the existing layout is still
        // there - otherwise a user reading it assumes their pre-rig has just been wiped. And it must name
        // the empty stock rather than describe a truss that measures 0 m or admits 0 kg, which is what a
        // message built from the longest assembly would otherwise say.
        var pals = Emptied(_venue.Palonniers);
        var mothers = Emptied(_venue.Mothers);
        var result = Optimizer.Optimize(_venue.Charges, pals, mothers, verbose: false);

        string message = Pipeline.UncoveredMessage(result, pals, mothers);

        Assert.Contains("ni par un palonnier ni par un mother", message);
        Assert.Contains("n'a pas été modifiée", message);
        Assert.Contains($"{InventoryFiles.PalonniersFileName} n'a aucune pièce en stock", message);
        Assert.Contains($"{InventoryFiles.MothersFileName} n'a aucune pièce en stock", message);
        Assert.DoesNotContain("mesure 0 m", message);
        Assert.DoesNotContain("admet 0 kg", message);
    }

    // ── The rows these tests are written with ────────────────────

    /// <summary>One inventory line: a model, a length, a Nombre, a load value and a mass.</summary>
    private static BarSpec Row(string type, double lengthM, int stock, double capacityKg, double? weightKg) => new()
    {
        Type = type,
        Length = lengthM,
        StockInit = stock,
        Stock = stock,
        Capacity = capacityKg,
        WeightKg = weightKg,
    };

    private static List<BarSpec>? Pick(double lengthM, params (BarSpec Row, int Available)[] rows) =>
        Assembler.Cheapest(rows, lengthM);

    /// <summary>
    /// The pieces chosen, in metres, longest first. Compared as numbers and never as text: a decimal
    /// comma or point would make the test depend on the language of the machine it runs on.
    /// </summary>
    private static double[] LengthsOf(List<BarSpec>? pieces) =>
        pieces is null ? Array.Empty<double>() : pieces.Select(p => p.Length).OrderByDescending(l => l).ToArray();

    /// <summary>
    /// A load table holding every case the boundaries are made of: two lengths of one model, one length
    /// listed twice at two values, and a model whose longest row has no mass.
    /// </summary>
    private static LoadTable Table() => LoadTable.From(new[]
    {
        Row("T", 2.8, 5, 2345, 31),
        Row("T", 3.0, 5, 2000, 35),
        Row("U", 5.6, 3, 3429, 95.6),
        Row("U", 5.6, 0, 2000, 96),
        Row("V", 3.0, 2, 2000, null),
    });

    /// <summary>The same inventory with every Nombre set to zero: the values stay, the pieces go.</summary>
    private static List<BarSpec> Emptied(IEnumerable<BarSpec> rows) => rows.Select(b => new BarSpec
    {
        Type = b.Type,
        Length = b.Length,
        StockInit = 0,
        Stock = 0,
        Capacity = b.Capacity,
        WeightKg = b.WeightKg,
    }).ToList();
}
