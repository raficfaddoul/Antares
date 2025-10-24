using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace AliantWebS.Models;

[Table("Site", Schema = "adbo")]
public partial class Site
{
    [Key]
    [Column("SiteID")]
    public Guid SiteId { get; set; }

    [StringLength(250)]
    public string? Name { get; set; }

    public string? ShortDescription { get; set; }

    public string? Description { get; set; }

    public Geometry? Geolocation { get; set; }

    public string? Address { get; set; }

    [InverseProperty("Site")]
    public virtual ICollection<Structure> Structures { get; set; } = new List<Structure>();
}
