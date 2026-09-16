using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("MotherGridPalonnier")]
public partial class MotherGridPalonnier
{
    [Key]
    [Column("MotherGridPalonnierID")]
    public Guid MotherGridPalonnierId { get; set; }

    [Column("EventMPScenarioID")]
    public Guid EventMpscenarioId { get; set; }

    [Column("ElementTypeID")]
    public Guid ElementTypeId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    public int? Numero { get; set; }

    public bool Famille { get; set; }

    [StringLength(50)]
    public string? Type { get; set; }

    public double? X1 { get; set; }

    public double? Y1 { get; set; }

    public double? X2 { get; set; }

    public double? Y2 { get; set; }

    /// <summary>
    /// For a palonnier placed by the optimiser: the mothergrid its first end (X1, Y1) hangs from. It
    /// also hangs from every mothergrid it crosses between its ends, as <see cref="Accrochages"/>
    /// records. Null on mothergrids, and on palonniers drawn or imported by hand. Deliberately not a
    /// foreign key: see Program.EnsureDatabaseSchema.
    /// </summary>
    [Column("MotherAID")]
    public Guid? MotherAId { get; set; }

    /// <summary>The mothergrid the second end (X2, Y2) hangs from. See <see cref="MotherAId"/>.</summary>
    [Column("MotherBID")]
    public Guid? MotherBId { get; set; }

    /// <summary>
    /// Which mothergrids this palonnier hangs from, where somebody decided: JSON read and written by
    /// Helpers.PalonnierHooks, one record per mothergrid the optimiser decided on or that someone
    /// hooked or unhooked by hand. A palonnier hangs from every mothergrid it crosses unless a record
    /// says otherwise, so null means nothing was decided. Null on mothergrids.
    /// </summary>
    [Column("Accrochages")]
    public string? Accrochages { get; set; }

    /// <summary>
    /// The pieces the truss is assembled from, as JSON whole centimetres - "[560,280]" - read and written
    /// by Helpers.TrussComposition. Every piece is of the element's own model (its ElementType). Null on
    /// elements placed before pieces were recorded: their length is then read from the name.
    /// </summary>
    [Column("Composition")]
    [StringLength(400)]
    public string? Composition { get; set; }

    [InverseProperty("MotherGridPalonnier")]
    public virtual ICollection<ChargeMp> ChargeMps { get; set; } = new List<ChargeMp>();

    [ForeignKey("ElementTypeId")]
    [InverseProperty("MotherGridPalonniers")]
    public virtual ElementType ElementType { get; set; } = null!;

    [ForeignKey("EventMpscenarioId")]
    [InverseProperty("MotherGridPalonniers")]
    public virtual EventMpscenario EventMpscenario { get; set; } = null!;
}
