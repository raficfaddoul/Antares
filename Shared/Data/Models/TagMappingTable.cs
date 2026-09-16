using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("TagMappingTable")]
public partial class TagMappingTable
{
    [Key]
    [Column("TagMappingTableID")]
    public Guid TagMappingTableId { get; set; }

    [StringLength(50)]
    public string CodeTag { get; set; } = null!;

    [StringLength(50)]
    public string UserTag { get; set; } = null!;

    public string? Description { get; set; }
}
