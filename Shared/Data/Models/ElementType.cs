using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("ElementType", Schema = "adbo")]
[Index("ElementTypeId", Name = "IX_ElementType")]
public partial class ElementType
{
    [Key]
    [Column("ElementTypeID")]
    public Guid ElementTypeId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    [Column("CategoryID")]
    public Guid? CategoryId { get; set; }

    public string? Description { get; set; }

    [Column(TypeName = "image")]
    public byte[]? Icon { get; set; }

    [InverseProperty("ElementType")]
    public virtual ICollection<AttributeTypeElementType> AttributeTypeElementTypes { get; set; } = new List<AttributeTypeElementType>();

    [ForeignKey("CategoryId")]
    [InverseProperty("ElementTypes")]
    public virtual Category? Category { get; set; }

    [InverseProperty("ElementType")]
    public virtual ICollection<ChannelDataTypeElementType> ChannelDataTypeElementTypes { get; set; } = new List<ChannelDataTypeElementType>();

    [InverseProperty("ElementType")]
    public virtual ICollection<Element> Elements { get; set; } = new List<Element>();

    [InverseProperty("ElementType")]
    public virtual ICollection<MotherGridPalonnier> MotherGridPalonniers { get; set; } = new List<MotherGridPalonnier>();
}
