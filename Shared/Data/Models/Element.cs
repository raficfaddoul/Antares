using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("Element", Schema = "adbo")]
public partial class Element
{
    [Key]
    [Column("ElementID")]
    public Guid ElementId { get; set; }

    [Column("ParentID")]
    public Guid? ParentId { get; set; }

    [Column("BimID")]
    public long? BimId { get; set; }

    [Column("ElementTypeID")]
    public Guid? ElementTypeId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    [Column("StructureID")]
    public Guid StructureId { get; set; }

    public int? Structural { get; set; }

    public string? Description { get; set; }

    [InverseProperty("Element")]
    public virtual ICollection<AttributeElement> AttributeElements { get; set; } = new List<AttributeElement>();

    [ForeignKey("ElementTypeId")]
    [InverseProperty("Elements")]
    public virtual ElementType? ElementType { get; set; }

    [InverseProperty("Element")]
    public virtual ICollection<Equipement> Equipements { get; set; } = new List<Equipement>();

    [InverseProperty("Parent")]
    public virtual ICollection<Element> InverseParent { get; set; } = new List<Element>();

    [ForeignKey("ParentId")]
    [InverseProperty("InverseParent")]
    public virtual Element? Parent { get; set; }

    [InverseProperty("Element")]
    public virtual ICollection<StaticLoadElement> StaticLoadElements { get; set; } = new List<StaticLoadElement>();

    [InverseProperty("Element")]
    public virtual ICollection<StaticPointLoadElement> StaticPointLoadElements { get; set; } = new List<StaticPointLoadElement>();

    [ForeignKey("StructureId")]
    [InverseProperty("Elements")]
    public virtual Structure Structure { get; set; } = null!;
}
