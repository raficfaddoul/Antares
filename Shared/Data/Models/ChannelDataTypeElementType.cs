using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("ChannelDataTypeElementType", Schema = "adbo")]
public partial class ChannelDataTypeElementType
{
    [Key]
    [Column("ChannelDataTypeElementTypeID")]
    public Guid ChannelDataTypeElementTypeId { get; set; }

    [Column("ChannelDataTypeID")]
    public Guid ChannelDataTypeId { get; set; }

    [Column("ElementTypeID")]
    public Guid ElementTypeId { get; set; }

    [ForeignKey("ChannelDataTypeId")]
    [InverseProperty("ChannelDataTypeElementTypes")]
    public virtual ChannelDataType ChannelDataType { get; set; } = null!;

    [ForeignKey("ElementTypeId")]
    [InverseProperty("ChannelDataTypeElementTypes")]
    public virtual ElementType ElementType { get; set; } = null!;
}
