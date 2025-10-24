using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("Category", Schema = "adbo")]
public partial class Category
{
    [Key]
    [Column("CategoryID")]
    public Guid CategoryId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    public string? Description { get; set; }

    public bool? Structural { get; set; }

    [InverseProperty("Category")]
    public virtual ICollection<ElementType> ElementTypes { get; set; } = new List<ElementType>();
}
