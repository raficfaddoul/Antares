using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("TokenAspNetRoles", Schema = "adbo")]
public partial class TokenAspNetRole
{
    [Key]
    public Guid TokenAspNetRoles { get; set; }

    [Column("AspNetRolesID")]
    [StringLength(450)]
    public string AspNetRolesId { get; set; } = null!;

    [Column("TokenID")]
    public Guid TokenId { get; set; }

    [ForeignKey("AspNetRolesId")]
    [InverseProperty("TokenAspNetRoles")]
    public virtual AspNetRole AspNetRoles { get; set; } = null!;

    [ForeignKey("TokenId")]
    [InverseProperty("TokenAspNetRoles")]
    public virtual Token Token { get; set; } = null!;
}
