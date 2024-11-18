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
    public virtual ICollection<AttributeTypeElementType> AttributeTypeElementTypes { get; } = new List<AttributeTypeElementType>();

    [ForeignKey("CategoryId")]
    [InverseProperty("ElementTypes")]
    public virtual Category? Category { get; set; }

    [InverseProperty("ElementType")]
    public virtual ICollection<ChannelDataTypeElementType> ChannelDataTypeElementTypes { get; } = new List<ChannelDataTypeElementType>();

    [InverseProperty("ElementType")]
    public virtual ICollection<ElementTypeDefectType> ElementTypeDefectTypes { get; } = new List<ElementTypeDefectType>();

    [InverseProperty("ElementType")]
    public virtual ICollection<Element> Elements { get; } = new List<Element>();

    [InverseProperty("ElementType")]
    public virtual ICollection<FlagTypeElementType> FlagTypeElementTypes { get; } = new List<FlagTypeElementType>();

    [InverseProperty("ElementType")]
    public virtual ICollection<InspectionResultTypeElementType> InspectionResultTypeElementTypes { get; } = new List<InspectionResultTypeElementType>();

    [InverseProperty("ElementType")]
    public virtual ICollection<MotherGridPalonnier> MotherGridPalonniers { get; } = new List<MotherGridPalonnier>();
}
