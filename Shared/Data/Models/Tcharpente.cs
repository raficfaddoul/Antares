using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

[Table("TCharpente")]
public partial class Tcharpente
{
    [Key]
    [Column("TCharpenteID")]
    public Guid TcharpenteId { get; set; }

    public int? Number { get; set; }

    public double? A1 { get; set; }

    [Column("A1Map")]
    public Guid? A1map { get; set; }

    public double? A2 { get; set; }

    [Column("A2Map")]
    public Guid? A2map { get; set; }

    public double? A3 { get; set; }

    [Column("A3Map")]
    public Guid? A3map { get; set; }

    public double? A4 { get; set; }

    [Column("A4Map")]
    public Guid? A4map { get; set; }

    public double? A5 { get; set; }

    [Column("A5Map")]
    public Guid? A5map { get; set; }

    public double? A6 { get; set; }

    [Column("A6Map")]
    public Guid? A6map { get; set; }

    public double? A7 { get; set; }

    [Column("A7Map")]
    public Guid? A7map { get; set; }

    public double? A8 { get; set; }

    [Column("A8Map")]
    public Guid? A8map { get; set; }

    public double? A9 { get; set; }

    [Column("A9Map")]
    public Guid? A9map { get; set; }

    public double? A10 { get; set; }

    [Column("A10Map")]
    public Guid? A10map { get; set; }

    public double? B1 { get; set; }

    [Column("B1Map")]
    public Guid? B1map { get; set; }

    public double? B2 { get; set; }

    [Column("B2Map")]
    public Guid? B2map { get; set; }

    public double? B3 { get; set; }

    [Column("B3Map")]
    public Guid? B3map { get; set; }

    public double B4 { get; set; }

    [Column("B4Map")]
    public Guid? B4map { get; set; }

    public double? B5 { get; set; }

    [Column("B5Map")]
    public Guid? B5map { get; set; }

    public double? B6 { get; set; }

    [Column("B6Map")]
    public Guid? B6map { get; set; }

    public double? B7 { get; set; }

    [Column("B7Map")]
    public Guid? B7map { get; set; }

    public double? B8 { get; set; }

    [Column("B8Map")]
    public Guid? B8map { get; set; }

    public double? B9 { get; set; }

    [Column("B9Map")]
    public Guid? B9map { get; set; }

    public double? B10 { get; set; }

    [Column("B10Map")]
    public Guid? B10map { get; set; }

    public double? C1 { get; set; }

    [Column("C1Map")]
    public Guid? C1map { get; set; }

    public double? C2 { get; set; }

    [Column("C2Map")]
    public Guid? C2map { get; set; }

    public double? C3 { get; set; }

    [Column("C3Map")]
    public Guid? C3map { get; set; }

    public double? C4 { get; set; }

    [Column("C4Map")]
    public Guid? C4map { get; set; }

    public double? C5 { get; set; }

    [Column("C5Map")]
    public Guid? C5map { get; set; }

    public double? C6 { get; set; }

    [Column("C6Map")]
    public Guid? C6map { get; set; }

    public double? C7 { get; set; }

    [Column("C7Map")]
    public Guid? C7map { get; set; }

    public double? C8 { get; set; }

    [Column("C8Map")]
    public Guid? C8map { get; set; }

    public double? C9 { get; set; }

    [Column("C9Map")]
    public Guid? C9map { get; set; }

    public double? C10 { get; set; }

    [Column("C10Map")]
    public Guid? C10map { get; set; }

    public double? E1 { get; set; }

    [Column("E1Map")]
    public Guid? E1map { get; set; }

    public double? E2 { get; set; }

    [Column("E2Map")]
    public Guid? E2map { get; set; }

    public double? E3 { get; set; }

    [Column("E3Map")]
    public Guid? E3map { get; set; }

    public double? E4 { get; set; }

    [Column("E4Map")]
    public Guid? E4map { get; set; }

    public double? E5 { get; set; }

    [Column("E5Map")]
    public Guid? E5map { get; set; }

    public double? E6 { get; set; }

    [Column("E6Map")]
    public Guid? E6map { get; set; }

    public double? E7 { get; set; }

    [Column("E7Map")]
    public Guid? E7map { get; set; }

    public double? E8 { get; set; }

    [Column("E8Map")]
    public Guid? E8map { get; set; }

    public double? E9 { get; set; }

    [Column("E9Map")]
    public Guid? E9map { get; set; }

    public double? E10 { get; set; }

    [Column("E10Map")]
    public Guid? E10map { get; set; }

    public double? F1 { get; set; }

    [Column("F1Map")]
    public Guid? F1map { get; set; }

    public double? F2 { get; set; }

    [Column("F2Map")]
    public Guid? F2map { get; set; }

    public double? F3 { get; set; }

    [Column("F3Map")]
    public Guid? F3map { get; set; }

    public double? F4 { get; set; }

    [Column("F4Map")]
    public Guid? F4map { get; set; }

    public double? F5 { get; set; }

    [Column("F5Map")]
    public Guid? F5map { get; set; }

    public double? F6 { get; set; }

    [Column("F6Map")]
    public Guid? F6map { get; set; }

    public double? F7 { get; set; }

    [Column("F7Map")]
    public Guid? F7map { get; set; }

    public double? F8 { get; set; }

    [Column("F8Map")]
    public Guid? F8map { get; set; }

    public double? F9 { get; set; }

    [Column("F9Map")]
    public Guid? F9map { get; set; }

    public double? F10 { get; set; }

    [Column("F10Map")]
    public Guid? F10map { get; set; }

    public double? G1 { get; set; }

    [Column("G1Map")]
    public Guid? G1map { get; set; }

    public double? G2 { get; set; }

    [Column("G2Map")]
    public Guid? G2map { get; set; }

    public double? G3 { get; set; }

    [Column("G3Map")]
    public Guid? G3map { get; set; }

    public double? G4 { get; set; }

    [Column("G4Map")]
    public Guid? G4map { get; set; }

    public double? G5 { get; set; }

    [Column("G5Map")]
    public Guid? G5map { get; set; }

    public double? G6 { get; set; }

    [Column("G6Map")]
    public Guid? G6map { get; set; }

    public double? G7 { get; set; }

    [Column("G7Map")]
    public Guid? G7map { get; set; }

    public double? G8 { get; set; }

    [Column("G8Map")]
    public Guid? G8map { get; set; }

    public double? G9 { get; set; }

    [Column("G9Map")]
    public Guid? G9map { get; set; }

    public double? G10 { get; set; }

    [Column("G10Map")]
    public Guid? G10map { get; set; }

    public double? H1 { get; set; }

    [Column("H1Map")]
    public Guid? H1map { get; set; }

    public double? H2 { get; set; }

    [Column("H2Map")]
    public Guid? H2map { get; set; }

    public double? H3 { get; set; }

    [Column("H3Map")]
    public Guid? H3map { get; set; }

    public double? H4 { get; set; }

    [Column("H4Map")]
    public Guid? H4map { get; set; }

    public double? H5 { get; set; }

    [Column("H5Map")]
    public Guid? H5map { get; set; }

    public double? H6 { get; set; }

    [Column("H6Map")]
    public Guid? H6map { get; set; }

    public double? H7 { get; set; }

    [Column("H7Map")]
    public Guid? H7map { get; set; }

    public double? H8 { get; set; }

    [Column("H8Map")]
    public Guid? H8map { get; set; }

    public double? H9 { get; set; }

    [Column("H9Map")]
    public Guid? H9map { get; set; }

    public double? H10 { get; set; }

    [Column("H10Map")]
    public Guid? H10map { get; set; }

    public double? I1 { get; set; }

    [Column("I1Map")]
    public Guid? I1map { get; set; }

    public double? I2 { get; set; }

    [Column("I2Map")]
    public Guid? I2map { get; set; }

    public double? I3 { get; set; }

    [Column("I3Map")]
    public Guid? I3map { get; set; }

    public double? I4 { get; set; }

    [Column("I4Map")]
    public Guid? I4map { get; set; }

    public double? I5 { get; set; }

    [Column("I5Map")]
    public Guid? I5map { get; set; }

    public double? I6 { get; set; }

    [Column("I6Map")]
    public Guid? I6map { get; set; }

    public double? I7 { get; set; }

    [Column("I7Map")]
    public Guid? I7map { get; set; }

    public double? I8 { get; set; }

    [Column("I8Map")]
    public Guid? I8map { get; set; }

    public double? I9 { get; set; }

    [Column("I9Map")]
    public Guid? I9map { get; set; }

    public double? I10 { get; set; }

    [Column("I10Map")]
    public Guid? I10map { get; set; }

    public double? J1 { get; set; }

    [Column("J1Map")]
    public Guid? J1map { get; set; }

    public double? J2 { get; set; }

    [Column("J2Map")]
    public Guid? J2map { get; set; }

    public double? J3 { get; set; }

    [Column("J3Map")]
    public Guid? J3map { get; set; }

    public double? J4 { get; set; }

    [Column("J4Map")]
    public Guid? J4map { get; set; }

    public double? J5 { get; set; }

    [Column("J5Map")]
    public Guid? J5map { get; set; }

    public double? J6 { get; set; }

    [Column("J6Map")]
    public Guid? J6map { get; set; }

    public double? J7 { get; set; }

    [Column("J7Map")]
    public Guid? J7map { get; set; }

    public double? J8 { get; set; }

    [Column("J8Map")]
    public Guid? J8map { get; set; }

    public double? J9 { get; set; }

    [Column("J9Map")]
    public Guid? J9map { get; set; }

    public double? J10 { get; set; }

    [Column("J10Map")]
    public Guid? J10map { get; set; }

    public double? D1 { get; set; }

    [Column("D1Map")]
    public Guid? D1map { get; set; }

    public double? D2 { get; set; }

    [Column("D2Map")]
    public Guid? D2map { get; set; }

    public double? D3 { get; set; }

    [Column("D3Map")]
    public Guid? D3map { get; set; }

    public double? D4 { get; set; }

    [Column("D4Map")]
    public Guid? D4map { get; set; }

    public double? D5 { get; set; }

    [Column("D5Map")]
    public Guid? D5map { get; set; }

    public double? D6 { get; set; }

    [Column("D6Map")]
    public Guid? D6map { get; set; }

    public double? D7 { get; set; }

    [Column("D7Map")]
    public Guid? D7map { get; set; }

    public double? D8 { get; set; }

    [Column("D8Map")]
    public Guid? D8map { get; set; }

    public double? D9 { get; set; }

    [Column("D9Map")]
    public Guid? D9map { get; set; }

    public double? D10 { get; set; }

    [Column("D10Map")]
    public Guid? D10map { get; set; }
}
