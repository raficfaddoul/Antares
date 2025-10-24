using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("ChannelDataType", Schema = "adbo")]
public partial class ChannelDataType
{
    [Key]
    [Column("ChannelDataTypeID")]
    public Guid ChannelDataTypeId { get; set; }

    [StringLength(50)]
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    [StringLength(50)]
    public string? Units { get; set; }

    [StringLength(50)]
    public string? Format { get; set; }

    [StringLength(1000)]
    public string? FileAddr { get; set; }

    [Column(TypeName = "image")]
    public byte[]? Icon { get; set; }

    [InverseProperty("ChannelDataType")]
    public virtual ICollection<ChannelDataTypeElementType> ChannelDataTypeElementTypes { get; set; } = new List<ChannelDataTypeElementType>();
}
