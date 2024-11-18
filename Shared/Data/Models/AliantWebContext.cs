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

    public virtual DbSet<AttributeDefect> AttributeDefects { get; set; }

    public virtual DbSet<AttributeElement> AttributeElements { get; set; }

    public virtual DbSet<AttributeEquipement> AttributeEquipements { get; set; }

    public virtual DbSet<AttributeStructure> AttributeStructures { get; set; }

    public virtual DbSet<AttributeType> AttributeTypes { get; set; }

    public virtual DbSet<AttributeTypeDefectType> AttributeTypeDefectTypes { get; set; }

    public virtual DbSet<AttributeTypeElementType> AttributeTypeElementTypes { get; set; }

    public virtual DbSet<Axe> Axes { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<ChannelDataType> ChannelDataTypes { get; set; }

    public virtual DbSet<ChannelDataTypeElementType> ChannelDataTypeElementTypes { get; set; }

    public virtual DbSet<ChargeMp> ChargeMps { get; set; }

    public virtual DbSet<ChargesScenique> ChargesSceniques { get; set; }

    public virtual DbSet<Charpente> Charpentes { get; set; }

    public virtual DbSet<Defect> Defects { get; set; }

    public virtual DbSet<DefectType> DefectTypes { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DynamicLoad> DynamicLoads { get; set; }

    public virtual DbSet<DynamicLoadElement> DynamicLoadElements { get; set; }

    public virtual DbSet<DynamicPointLoad> DynamicPointLoads { get; set; }

    public virtual DbSet<DynamicPointLoadElement> DynamicPointLoadElements { get; set; }

    public virtual DbSet<Element> Elements { get; set; }

    public virtual DbSet<ElementType> ElementTypes { get; set; }

    public virtual DbSet<ElementTypeDefectType> ElementTypeDefectTypes { get; set; }

    public virtual DbSet<Equipement> Equipements { get; set; }

    public virtual DbSet<EquipementType> EquipementTypes { get; set; }

    public virtual DbSet<EquipementTypeAttributeType> EquipementTypeAttributeTypes { get; set; }

    public virtual DbSet<Event> Events { get; set; }

    public virtual DbSet<EventLoadCase> EventLoadCases { get; set; }

    public virtual DbSet<EventMpscenario> EventMpscenarios { get; set; }

    public virtual DbSet<FlagElement> FlagElements { get; set; }

    public virtual DbSet<FlagStructure> FlagStructures { get; set; }

    public virtual DbSet<FlagType> FlagTypes { get; set; }

    public virtual DbSet<FlagTypeDefectType> FlagTypeDefectTypes { get; set; }

    public virtual DbSet<FlagTypeElementType> FlagTypeElementTypes { get; set; }

    public virtual DbSet<FlagTypeInspectionResultType> FlagTypeInspectionResultTypes { get; set; }

    public virtual DbSet<Inspection> Inspections { get; set; }

    public virtual DbSet<InspectionResult> InspectionResults { get; set; }

    public virtual DbSet<InspectionResultType> InspectionResultTypes { get; set; }

    public virtual DbSet<InspectionResultTypeElementType> InspectionResultTypeElementTypes { get; set; }

    public virtual DbSet<LoadCase> LoadCases { get; set; }

    public virtual DbSet<MobileLoad> MobileLoads { get; set; }

    public virtual DbSet<MobileLoadElement> MobileLoadElements { get; set; }

    public virtual DbSet<MotherGridPalonnier> MotherGridPalonniers { get; set; }

    public virtual DbSet<Site> Sites { get; set; }

    public virtual DbSet<StaticLoad> StaticLoads { get; set; }

    public virtual DbSet<StaticLoadElement> StaticLoadElements { get; set; }

    public virtual DbSet<StaticPointLoad> StaticPointLoads { get; set; }

    public virtual DbSet<StaticPointLoadElement> StaticPointLoadElements { get; set; }

    public virtual DbSet<Structure> Structures { get; set; }

    public virtual DbSet<StructureImage> StructureImages { get; set; }

    public virtual DbSet<StructureInspectionResult> StructureInspectionResults { get; set; }

    public virtual DbSet<StructureType> StructureTypes { get; set; }

    public virtual DbSet<StructureTypeAttributeType> StructureTypeAttributeTypes { get; set; }

    public virtual DbSet<StructureTypeFlagType> StructureTypeFlagTypes { get; set; }

    public virtual DbSet<StructureTypeInspectionResultType> StructureTypeInspectionResultTypes { get; set; }

    public virtual DbSet<Tcharpente> Tcharpentes { get; set; }

    public virtual DbSet<Token> Tokens { get; set; }

    public virtual DbSet<TokenAspNetRole> TokenAspNetRoles { get; set; }

//    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
//#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see http://go.microsoft.com/fwlink/?LinkId=723263.
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
                    "AspNetUserRoles",
                    r => r.HasOne<AspNetRole>().WithMany().HasForeignKey("RoleId"),
                    l => l.HasOne<AspNetUser>().WithMany().HasForeignKey("UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.HasIndex(new[] { "RoleId" }, "IX_AspNetUserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<AttributeDefect>(entity =>
        {
            entity.HasKey(e => e.AttributeDefectId).HasName("PK_AttributeDefect_1");

            entity.Property(e => e.AttributeDefectId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.AttributeType).WithMany(p => p.AttributeDefects)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttributeDefect_AttributeType");

            entity.HasOne(d => d.Defect).WithMany(p => p.AttributeDefects).HasConstraintName("FK_AttributeDefect_Defect");
        });

        modelBuilder.Entity<AttributeElement>(entity =>
        {
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

        modelBuilder.Entity<AttributeTypeDefectType>(entity =>
        {
            entity.HasKey(e => e.AttributeTypeDefectTypeId).HasName("PK_AttributeTypeDefectType_1");

            entity.Property(e => e.AttributeTypeDefectTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.AttributeType).WithMany(p => p.AttributeTypeDefectTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttributeTypeDefectType_AttributeType");

            entity.HasOne(d => d.DefectType).WithMany(p => p.AttributeTypeDefectTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AttributeTypeDefectType_DefectType");
        });

        modelBuilder.Entity<AttributeTypeElementType>(entity =>
        {
            entity.HasKey(e => e.AttributeTypeElementTypeId).HasName("PK_AttributeTypeElementType_1");

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

            entity.HasOne(d => d.Event).WithMany(p => p.ChargesSceniques).HasConstraintName("FK_ChargesScenique_Event");
        });

        modelBuilder.Entity<Charpente>(entity =>
        {
            entity.Property(e => e.CharpenteId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Event).WithMany(p => p.Charpentes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Charpente_Event");
        });

        modelBuilder.Entity<Defect>(entity =>
        {
            entity.HasKey(e => e.DefectId).HasName("PK_Defect_1");

            entity.Property(e => e.DefectId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.DefectType).WithMany(p => p.Defects)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Defect_DefectType");

            entity.HasOne(d => d.Element).WithMany(p => p.Defects).HasConstraintName("FK_Defect_Element");
        });

        modelBuilder.Entity<DefectType>(entity =>
        {
            entity.HasKey(e => e.DefectTypeId).HasName("PK_DefectType_1");

            entity.Property(e => e.DefectTypeId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.DocumentId).HasName("PK_Document_1");

            entity.Property(e => e.DocumentId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Structure).WithMany(p => p.Documents).HasConstraintName("FK_Document_Structure");
        });

        modelBuilder.Entity<DynamicLoad>(entity =>
        {
            entity.Property(e => e.DynamicLoadId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.LoadCase).WithMany(p => p.DynamicLoads).HasConstraintName("FK_DynamicLoad_LoadCase");
        });

        modelBuilder.Entity<DynamicLoadElement>(entity =>
        {
            entity.Property(e => e.DynamicLoadElementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.DynamicLoad).WithMany(p => p.DynamicLoadElements).HasConstraintName("FK_DynamicLoadElement_DynamicLoad");

            entity.HasOne(d => d.Element).WithMany(p => p.DynamicLoadElements).HasConstraintName("FK_DynamicLoadElement_Element");
        });

        modelBuilder.Entity<DynamicPointLoad>(entity =>
        {
            entity.Property(e => e.DynamicPointLoadId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.LoadCase).WithMany(p => p.DynamicPointLoads).HasConstraintName("FK_DynamicPointLoad_LoadCase");
        });

        modelBuilder.Entity<DynamicPointLoadElement>(entity =>
        {
            entity.Property(e => e.DynamicPointLoadElementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.DynamicPointLoad).WithMany(p => p.DynamicPointLoadElements).HasConstraintName("FK_DynamicPointLoadElement_DynamicPointLoad");

            entity.HasOne(d => d.Element).WithMany(p => p.DynamicPointLoadElements).HasConstraintName("FK_DynamicPointLoadElement_Element");
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

        modelBuilder.Entity<ElementTypeDefectType>(entity =>
        {
            entity.HasKey(e => e.ElementTypeDefectTypeId).HasName("PK_ElementTypeDefectType_1");

            entity.Property(e => e.ElementTypeDefectTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.DefectType).WithMany(p => p.ElementTypeDefectTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ElementTypeDefectType_DefectType");

            entity.HasOne(d => d.ElementType).WithMany(p => p.ElementTypeDefectTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ElementTypeDefectType_ElementType");
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

        modelBuilder.Entity<FlagElement>(entity =>
        {
            entity.HasKey(e => e.FlagElementId).HasName("PK_FlagElement_1");

            entity.Property(e => e.FlagElementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Element).WithMany(p => p.FlagElements).HasConstraintName("FK_FlagElement_Element");

            entity.HasOne(d => d.FlagType).WithMany(p => p.FlagElements)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FlagElement_FlagType");
        });

        modelBuilder.Entity<FlagStructure>(entity =>
        {
            entity.HasKey(e => e.FlagStructureId).HasName("PK_FlagStructure_1");

            entity.Property(e => e.FlagStructureId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.FlagType).WithMany(p => p.FlagStructures)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FlagStructure_FlagType");

            entity.HasOne(d => d.Structure).WithMany(p => p.FlagStructures).HasConstraintName("FK_FlagStructure_Structure");
        });

        modelBuilder.Entity<FlagType>(entity =>
        {
            entity.Property(e => e.FlagTypeId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<FlagTypeDefectType>(entity =>
        {
            entity.HasKey(e => e.FlagTypeDefectTypeId).HasName("PK_FlagTypeDefectType_1");

            entity.Property(e => e.FlagTypeDefectTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.DefectType).WithMany(p => p.FlagTypeDefectTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FlagTypeDefectType_DefectType");

            entity.HasOne(d => d.FlagType).WithMany(p => p.FlagTypeDefectTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FlagTypeDefectType_FlagType");
        });

        modelBuilder.Entity<FlagTypeElementType>(entity =>
        {
            entity.HasKey(e => e.FlagTypeElementTypeId).HasName("PK_FlagTypeElementType_1");

            entity.Property(e => e.FlagTypeElementTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.ElementType).WithMany(p => p.FlagTypeElementTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FlagTypeElementType_ElementType");

            entity.HasOne(d => d.FlagType).WithMany(p => p.FlagTypeElementTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FlagTypeElementType_FlagType");
        });

        modelBuilder.Entity<FlagTypeInspectionResultType>(entity =>
        {
            entity.HasKey(e => e.FlagTypeInspectionResultTypeId).HasName("PK_FlagTypeInspectionResultType_1");

            entity.Property(e => e.FlagTypeInspectionResultTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.FlagType).WithMany(p => p.FlagTypeInspectionResultTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FlagTypeInspectionResultType_FlagType");

            entity.HasOne(d => d.InspectionResultType).WithMany(p => p.FlagTypeInspectionResultTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FlagTypeInspectionResultType_InspectionResultType");
        });

        modelBuilder.Entity<Inspection>(entity =>
        {
            entity.HasKey(e => e.InspectionId).HasName("PK_Inspection_1");

            entity.Property(e => e.InspectionId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<InspectionResult>(entity =>
        {
            entity.HasKey(e => e.InspectionResultId).HasName("PK_InspectionResult_1");

            entity.Property(e => e.InspectionResultId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Element).WithMany(p => p.InspectionResults).HasConstraintName("FK_InspectionResult_Element");

            entity.HasOne(d => d.Inspection).WithMany(p => p.InspectionResults).HasConstraintName("FK_InspectionResult_Inspection1");

            entity.HasOne(d => d.InspectionResultType).WithMany(p => p.InspectionResults)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InspectionResult_InspectionResultType");
        });

        modelBuilder.Entity<InspectionResultType>(entity =>
        {
            entity.HasKey(e => e.InspectionResultTypeId).HasName("PK_InspectionResultType_1");

            entity.Property(e => e.InspectionResultTypeId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<InspectionResultTypeElementType>(entity =>
        {
            entity.HasKey(e => e.InspectionResultTypeElementTypeId).HasName("PK_InspectionResultTypeElementType_1");

            entity.Property(e => e.InspectionResultTypeElementTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.ElementType).WithMany(p => p.InspectionResultTypeElementTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InspectionResultTypeElementType_ElementType");

            entity.HasOne(d => d.InspectionResultType).WithMany(p => p.InspectionResultTypeElementTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InspectionResultTypeElementType_InspectionResultType");
        });

        modelBuilder.Entity<LoadCase>(entity =>
        {
            entity.Property(e => e.LoadCaseId).HasDefaultValueSql("(newsequentialid())");
        });

        modelBuilder.Entity<MobileLoad>(entity =>
        {
            entity.Property(e => e.MobileLoadId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.LoadCase).WithMany(p => p.MobileLoads).HasConstraintName("FK_MobileLoad_LoadCase");
        });

        modelBuilder.Entity<MobileLoadElement>(entity =>
        {
            entity.Property(e => e.MobileLoadElementId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.Element).WithMany(p => p.MobileLoadElements).HasConstraintName("FK_MobileLoadElement_Element");

            entity.HasOne(d => d.MobileLoad).WithMany(p => p.MobileLoadElements).HasConstraintName("FK_MobileLoadElement_MobileLoad");
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

        modelBuilder.Entity<StructureInspectionResult>(entity =>
        {
            entity.HasKey(e => e.StructureInspectionResultId).HasName("PK_StructureInspectionResult_1");

            entity.Property(e => e.StructureInspectionResultId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.InspectionResultType).WithMany(p => p.StructureInspectionResults)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StructureInspectionResult_InspectionResultType");

            entity.HasOne(d => d.Structure).WithMany(p => p.StructureInspectionResults).HasConstraintName("FK_StructureInspectionResult_Structure");
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

        modelBuilder.Entity<StructureTypeFlagType>(entity =>
        {
            entity.HasKey(e => e.StructureTypeFlagTypeId).HasName("PK_StructureTypeFlagType_1");

            entity.Property(e => e.StructureTypeFlagTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.FlagType).WithMany(p => p.StructureTypeFlagTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StructureTypeFlagType_FlagType");

            entity.HasOne(d => d.StructureType).WithMany(p => p.StructureTypeFlagTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StructureTypeFlagType_StructureType");
        });

        modelBuilder.Entity<StructureTypeInspectionResultType>(entity =>
        {
            entity.HasKey(e => e.StructureTypeInspectionResultTypeId).HasName("PK_StructureTypeInspectionResultType_1");

            entity.Property(e => e.StructureTypeInspectionResultTypeId).HasDefaultValueSql("(newsequentialid())");

            entity.HasOne(d => d.InspectionResultType).WithMany(p => p.StructureTypeInspectionResultTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StructureTypeInspectionResultType_InspectionResultType");

            entity.HasOne(d => d.StructureType).WithMany(p => p.StructureTypeInspectionResultTypes)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StructureTypeInspectionResultType_StructureType");
        });

        modelBuilder.Entity<Tcharpente>(entity =>
        {
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
