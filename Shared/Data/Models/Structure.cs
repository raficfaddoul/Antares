using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace AliantWebS.Models;

[Table("Structure", Schema = "adbo")]
public partial class Structure
{
    [Key]
    [Column("StructureID")]
    public Guid StructureId { get; set; }

    [Column("StructureTypeID")]
    public Guid? StructureTypeId { get; set; }

    [Column("SiteID")]
    public Guid? SiteId { get; set; }

    [StringLength(250)]
    public string? Name { get; set; }

    public string? ShortDescription { get; set; }

    public string? Description { get; set; }

    public Geometry? Geolocation { get; set; }

    public double? Length { get; set; }

    public DateOnly? YearBuilt { get; set; }

    public double? DesignLoad { get; set; }

    [InverseProperty("Structure")]
    public virtual ICollection<AttributeStructure> AttributeStructures { get; set; } = new List<AttributeStructure>();

    [InverseProperty("Structure")]
    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    [InverseProperty("Structure")]
    public virtual ICollection<Element> Elements { get; set; } = new List<Element>();

    [InverseProperty("Structure")]
    public virtual ICollection<Event> Events { get; set; } = new List<Event>();

    [InverseProperty("Structure")]
    public virtual ICollection<FlagStructure> FlagStructures { get; set; } = new List<FlagStructure>();

    [ForeignKey("SiteId")]
    [InverseProperty("Structures")]
    public virtual Site? Site { get; set; }

    [InverseProperty("Structure")]
    public virtual ICollection<StructureImage> StructureImages { get; set; } = new List<StructureImage>();

    [InverseProperty("Structure")]
    public virtual ICollection<StructureInspectionResult> StructureInspectionResults { get; set; } = new List<StructureInspectionResult>();

    [ForeignKey("StructureTypeId")]
    [InverseProperty("Structures")]
    public virtual StructureType? StructureType { get; set; }
}
