using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("Inspection", Schema = "adbo")]
public partial class Inspection
{
    [Key]
    [Column("InspectionID")]
    public Guid InspectionId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    [Column(TypeName = "date")]
    public DateTime? Date { get; set; }

    public string? Description { get; set; }

    [InverseProperty("Inspection")]
    public virtual ICollection<InspectionResult> InspectionResults { get; } = new List<InspectionResult>();
}
