using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace AliantWebS.Models;

public partial class AliantWebContext : DbContext
{
    public AliantWebContext()
    {
    }

    public AliantWebContext(DbContextOptions<AliantWebContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AspNetRole> AspNetRoles { get; set; }

    public virtual DbSet<AspNetRoleClaim> AspNetRoleClaims { get; set; }

    public virtual DbSet<AspNetUser> AspNetUsers { get; set; }

    public virtual DbSet<AspNetUserClaim> AspNetUserClaims { get; set; }

    public virtual DbSet<AspNetUserLogin> AspNetUserLogins { get; set; }

    public virtual DbSet<AspNetUserToken> AspNetUserTokens { get; set; }

    public virtual DbSet<AttributeElement> AttributeElements { get; set; }

    public virtual DbSet<AttributeEquipement> AttributeEquipements { get; set; }

    public virtual DbSet<AttributeStructure> AttributeStructures { get; set; }

    public virtual DbSet<AttributeType> AttributeTypes { get; set; }

    public virtual DbSet<AttributeTypeElementType> AttributeTypeElementTypes { get; set; }

    public virtual DbSet<Axe> Axes { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<ChannelDataType> ChannelDataTypes { get; set; }

    public virtual DbSet<ChannelDataTypeElementType> ChannelDataTypeElementTypes { get; set; }

    public virtual DbSet<ChargeMp> ChargeMps { get; set; }

    public virtual DbSet<ChargesScenique> ChargesSceniques { get; set; }

    public virtual DbSet<Charpente> Charpentes { get; set; }

    public virtual DbSet<Element> Elements { get; set; }

    public virtual DbSet<ElementType> ElementTypes { get; set; }

    public virtual DbSet<Equipement> Equipements { get; set; }

    public virtual DbSet<EquipementType> EquipementTypes { get; set; }

    public virtual DbSet<EquipementTypeAttributeType> EquipementTypeAttributeTypes { get; set; }

    public virtual DbSet<Event> Events { get; set; }

    public virtual DbSet<EventLoadCase> EventLoadCases { get; set; }

    public virtual DbSet<EventMpscenario> EventMpscenarios { get; set; }

    public virtual DbSet<LoadCase> LoadCases { get; set; }

    public virtual DbSet<MotherGridPalonnier> MotherGridPalonniers { get; set; }

    public virtual DbSet<Site> Sites { get; set; }

    public virtual DbSet<StaticLoad> StaticLoads { get; set; }

    public virtual DbSet<StaticLoadElement> StaticLoadElements { get; set; }

    public virtual DbSet<StaticPointLoad> StaticPointLoads { get; set; }

    public virtual DbSet<StaticPointLoadElement> StaticPointLoadElements { get; set; }

    public virtual DbSet<Structure> Structures { get; set; }

    public virtual DbSet<StructureImage> StructureImages { get; set; }

    public virtual DbSet<StructureType> StructureTypes { get; set; }

    public virtual DbSet<StructureTypeAttributeType> StructureTypeAttributeTypes { get; set; }

    public virtual DbSet<TagMappingTable> TagMappingTables { get; set; }

    public virtual DbSet<Tcharpente> Tcharpentes { get; set; }

    public virtual DbSet<Token> Tokens { get; set; }

    public virtual DbSet<TokenAspNetRole> TokenAspNetRoles { get; set; }

//    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
//#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
//        => optionsBuilder.UseSqlServer("Data Source=RaficLaptop; Initial Catalog=AliantWeb; Encrypt=False; Integrated Security = True; ConnectRetryCount=0", x => x.UseNetTopologySuite());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AspNetRole>(entity =>
        {
            entity.HasIndex(e => e.NormalizedName, "RoleNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedName] IS NOT NULL)");
        });

        modelBuilder.Entity<AspNetUser>(entity =>
        {
            entity.HasIndex(e => e.NormalizedUserName, "UserNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedUserName] IS NOT NULL)");

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "AspNetUserRole",
                    r => r.HasOne<AspNetRole>().WithMany().HasForeignKey("RoleId"),
                    l => l.HasOne<AspNetUser>().WithMany().HasForeignKey("UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.ToTable("AspNetUserRoles");
                        j.HasIndex(new[] { "RoleId" }, "IX_AspNetUserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<AttributeElement>(entity =>
        {
            // Created by Program.EnsureDatabaseSchema once the duplicate rows are cleaned up: one row per attribute per element.
            entity.HasIndex(e => new { e.ElementId, e.AttributeTypeId }, "UX_AttributeElement_ElementID_AttributeTypeID").IsUnique();

            entity.Property(e => e.AttributeElementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.AttributeType).WithMany(p => p.AttributeElements)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttributeElement_AttributeType");

            entity.HasOne(d => d.Element).WithMany(p => p.AttributeElements).HasConstraintName("FK_AttributeElement_Element");
        });

        modelBuilder.Entity<AttributeEquipement>(entity =>
        {
            entity.Property(e => e.AttributeEquipementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.AttributeType).WithMany(p => p.AttributeEquipements)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttributeEquipement_AttributeType");

            entity.HasOne(d => d.Equipement).WithMany(p => p.AttributeEquipements).HasConstraintName("FK_AttributeEquipement_Equipement");
        });

        modelBuilder.Entity<AttributeStructure>(entity =>
        {
            entity.HasKey(e => e.AttributeStructureId).HasName("PK_AttributeStructure_1");

            entity.Property(e => e.AttributeStructureId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.AttributeType).WithMany(p => p.AttributeStructures).HasConstraintName("FK_AttributeStructure_AttributeType");

            entity.HasOne(d => d.Structure).WithMany(p => p.AttributeStructures).HasConstraintName("FK_AttributeStructure_Structure");
        });

        modelBuilder.Entity<AttributeType>(entity =>
        {
            entity.HasKey(e => e.AttributeTypeId).HasName("PK_AttributeDefectType");

            entity.Property(e => e.AttributeTypeId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<AttributeTypeElementType>(entity =>
        {
            entity.HasKey(e => e.AttributeTypeElementTypeId).HasName("PK_AttributeTypeElementType_1");

            // Created by Program.EnsureDatabaseSchema once the duplicate links are cleaned up: one link per attribute per element type.
            entity.HasIndex(e => new { e.ElementTypeId, e.AttributeTypeId }, "UX_AttributeTypeElementType_ElementTypeID_AttributeTypeID").IsUnique();

            entity.Property(e => e.AttributeTypeElementTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.AttributeType).WithMany(p => p.AttributeTypeElementTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttributeTypeElementType_AttributeType");

            entity.HasOne(d => d.ElementType).WithMany(p => p.AttributeTypeElementTypes).HasConstraintName("FK_AttributeTypeElementType_ElementType");
        });

        modelBuilder.Entity<Axe>(entity =>
        {
            entity.Property(e => e.AxeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Event).WithMany(p => p.Axes).HasConstraintName("FK_Axe_Event");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK_Category_1");

            entity.Property(e => e.CategoryId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<ChannelDataType>(entity =>
        {
            entity.HasKey(e => e.ChannelDataTypeId).HasName("PK_ChannelDataType_1");

            entity.Property(e => e.ChannelDataTypeId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<ChannelDataTypeElementType>(entity =>
        {
            entity.HasKey(e => e.ChannelDataTypeElementTypeId).HasName("PK_ChannelDataTypeElementType_1");

            entity.Property(e => e.ChannelDataTypeElementTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.ChannelDataType).WithMany(p => p.ChannelDataTypeElementTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChannelDataTypeElementType_ChannelDataType");

            entity.HasOne(d => d.ElementType).WithMany(p => p.ChannelDataTypeElementTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChannelDataTypeElementType_ElementType");
        });

        modelBuilder.Entity<ChargeMp>(entity =>
        {
            entity.Property(e => e.ChargeMpid).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.MotherGridPalonnier).WithMany(p => p.ChargeMps).HasConstraintName("FK_ChargeMP_MotherGridPalonnier");
        });

        modelBuilder.Entity<ChargesScenique>(entity =>
        {
            entity.Property(e => e.ChargesSceniqueId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.EventMpscenario).WithMany(p => p.ChargesSceniques)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ChargesScenique_EventMPScenario");
        });

        modelBuilder.Entity<Charpente>(entity =>
        {
            entity.Property(e => e.CharpenteId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.EventMpscenario).WithMany(p => p.Charpentes).HasConstraintName("FK_Charpente_EventMPScenario");
        });

        modelBuilder.Entity<Element>(entity =>
        {
            entity.Property(e => e.ElementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.ElementType).WithMany(p => p.Elements).HasConstraintName("FK_Element_ElementType");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent).HasConstraintName("FK_Element_Element1");

            entity.HasOne(d => d.Structure).WithMany(p => p.Elements).HasConstraintName("FK_Element_Structure");
        });

        modelBuilder.Entity<ElementType>(entity =>
        {
            entity.HasKey(e => e.ElementTypeId).HasName("PK_ElementType_1");

            entity.Property(e => e.ElementTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Category).WithMany(p => p.ElementTypes).HasConstraintName("FK_ElementType_Category");
        });

        modelBuilder.Entity<Equipement>(entity =>
        {
            entity.Property(e => e.EquipementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Element).WithMany(p => p.Equipements)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Equipement_Element");

            entity.HasOne(d => d.EquipementType).WithMany(p => p.Equipements)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Equipement_EquipementType");

            entity.HasOne(d => d.Event).WithMany(p => p.Equipements)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Equipement_Event");
        });

        modelBuilder.Entity<EquipementType>(entity =>
        {
            entity.Property(e => e.EquipementTypeId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<EquipementTypeAttributeType>(entity =>
        {
            entity.Property(e => e.EquipementTypeAttributeTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.AttributeType).WithMany(p => p.EquipementTypeAttributeTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_EquipementTypeAttributeType_AttributeType");

            entity.HasOne(d => d.EquipementType).WithMany(p => p.EquipementTypeAttributeTypes).HasConstraintName("FK_EquipementTypeAttributeType_EquipementType");
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.Property(e => e.EventId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Structure).WithMany(p => p.Events)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Event_Structure");
        });

        modelBuilder.Entity<EventLoadCase>(entity =>
        {
            entity.Property(e => e.EventLoadCaseId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Event).WithMany(p => p.EventLoadCases).HasConstraintName("FK_EventLoadCase_Event");

            entity.HasOne(d => d.LoadCase).WithMany(p => p.EventLoadCases).HasConstraintName("FK_EventLoadCase_LoadCase");
        });

        modelBuilder.Entity<EventMpscenario>(entity =>
        {
            entity.Property(e => e.EventMpscenarioId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Event).WithMany(p => p.EventMpscenarios).HasConstraintName("FK_EventMPScenario_Event");
        });

        modelBuilder.Entity<LoadCase>(entity =>
        {
            entity.Property(e => e.LoadCaseId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<MotherGridPalonnier>(entity =>
        {
            entity.Property(e => e.MotherGridPalonnierId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.ElementType).WithMany(p => p.MotherGridPalonniers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MotherGridPalonnier_ElementType");

            entity.HasOne(d => d.EventMpscenario).WithMany(p => p.MotherGridPalonniers).HasConstraintName("FK_MotherGridPalonnier_EventMPScenario");
        });

        modelBuilder.Entity<Site>(entity =>
        {
            entity.Property(e => e.SiteId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<StaticLoad>(entity =>
        {
            entity.Property(e => e.StaticLoadId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.LoadCase).WithMany(p => p.StaticLoads).HasConstraintName("FK_StaticLoad_LoadCase");
        });

        modelBuilder.Entity<StaticLoadElement>(entity =>
        {
            entity.Property(e => e.StaticLoadElementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Element).WithMany(p => p.StaticLoadElements).HasConstraintName("FK_StaticLoadElement_Element");

            entity.HasOne(d => d.StaticLoad).WithMany(p => p.StaticLoadElements).HasConstraintName("FK_StaticLoadElement_StaticLoad");
        });

        modelBuilder.Entity<StaticPointLoad>(entity =>
        {
            entity.Property(e => e.StaticPointLoadId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.LoadCase).WithMany(p => p.StaticPointLoads).HasConstraintName("FK_StaticPointLoad_LoadCase");
        });

        modelBuilder.Entity<StaticPointLoadElement>(entity =>
        {
            entity.Property(e => e.StaticPointLoadElementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Element).WithMany(p => p.StaticPointLoadElements).HasConstraintName("FK_StaticPointLoadElement_Element");

            entity.HasOne(d => d.StaticPointLoad).WithMany(p => p.StaticPointLoadElements).HasConstraintName("FK_StaticPointLoadElement_StaticPointLoad");
        });

        modelBuilder.Entity<Structure>(entity =>
        {
            entity.HasKey(e => e.StructureId).HasName("PK_Structure_1");

            entity.Property(e => e.StructureId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Site).WithMany(p => p.Structures).HasConstraintName("FK_Structure_Site");

            entity.HasOne(d => d.StructureType).WithMany(p => p.Structures).HasConstraintName("FK_Structure_StructureType");
        });

        modelBuilder.Entity<StructureImage>(entity =>
        {
            entity.Property(e => e.StructureImageId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Structure).WithMany(p => p.StructureImages).HasConstraintName("FK_StructureImage_Structure");
        });

        modelBuilder.Entity<StructureType>(entity =>
        {
            entity.HasKey(e => e.StructureTypeId).HasName("PK_StructureType_1");

            entity.Property(e => e.StructureTypeId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<StructureTypeAttributeType>(entity =>
        {
            entity.HasKey(e => e.StructureTypeAttributeTypeId).HasName("PK_StructureTypeAttributeType_1");

            entity.Property(e => e.StructureTypeAttributeTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.AttributeType).WithMany(p => p.StructureTypeAttributeTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StructureTypeAttributeType_AttributeType");

            entity.HasOne(d => d.StructureType).WithMany(p => p.StructureTypeAttributeTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StructureTypeAttributeType_StructureType");
        });

        modelBuilder.Entity<TagMappingTable>(entity =>
        {
            entity.Property(e => e.TagMappingTableId).ValueGeneratedNever();
        });

        modelBuilder.Entity<Tcharpente>(entity =>
        {
            entity.HasKey(e => e.TcharpenteId).HasName("PK_TCharpente_1");

            entity.Property(e => e.TcharpenteId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<Token>(entity =>
        {
            entity.Property(e => e.TokenId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<TokenAspNetRole>(entity =>
        {
            entity.Property(e => e.TokenAspNetRoles).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.AspNetRoles).WithMany(p => p.TokenAspNetRoles).HasConstraintName("FK_TokenAspNetRoles_AspNetRoles");

            entity.HasOne(d => d.Token).WithMany(p => p.TokenAspNetRoles).HasConstraintName("FK_TokenAspNetRoles_Token");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
