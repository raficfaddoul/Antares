using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("Token", Schema = "adbo")]
public partial class Token
{
    [Key]
    [Column("TokenID")]
    public Guid TokenId { get; set; }

    [Column("AspNetRolesID")]
    [StringLength(450)]
    public string? AspNetRolesId { get; set; }

    [StringLength(450)]
    public string? Value { get; set; }

    public DateTime? Timestamp { get; set; }

    [InverseProperty("Token")]
    public virtual ICollection<TokenAspNetRole> TokenAspNetRoles { get; set; } = new List<TokenAspNetRole>();
}
