using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("StructureType", Schema = "adbo")]
public partial class StructureType
{
    [Key]
    [Column("StructureTypeID")]
    public Guid StructureTypeId { get; set; }

    [StringLength(450)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [InverseProperty("StructureType")]
    public virtual ICollection<StructureTypeAttributeType> StructureTypeAttributeTypes { get; set; } = new List<StructureTypeAttributeType>();

    [InverseProperty("StructureType")]
    public virtual ICollection<Structure> Structures { get; set; } = new List<Structure>();
}
