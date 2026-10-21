using DocumentFormat.OpenXml.Bibliography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using AliantWebS.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace AliantWebS
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            EnsureDatabaseSchema(host.Services);

            // Restore any alarm that was active before this restart, and re-assert the siren.
            AliantWebS.Data.ReportsService
                .InitializeAsync(host.Services.GetRequiredService<IServiceScopeFactory>())
                .GetAwaiter().GetResult();

            host.Run();
        }

        // Idempotent schema patches applied at startup: add columns if missing, so we don't rely on
        // EF migrations. Safe to run on every start (each patch is guarded by IF NOT EXISTS).
        private static void EnsureDatabaseSchema(IServiceProvider services)
        {
            try
            {
                var factory = services.GetRequiredService<IDbContextFactory<AliantWebContext>>();
                using var db = factory.CreateDbContext();

                // SortOrder on ChannelDataTypeElementType: lets each element type order its channels.
                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'SortOrder'
      AND Object_ID = Object_ID(N'adbo.ChannelDataTypeElementType'))
    ALTER TABLE adbo.ChannelDataTypeElementType ADD SortOrder INT NULL;");

                // Active alarms + mute switches, so a container restart cannot silently drop a
                // live alarm (the state used to be held only in memory).
                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AlarmState')
    CREATE TABLE AlarmState (
        AlarmKey       NVARCHAR(400) NOT NULL PRIMARY KEY,
        SensorName     NVARCHAR(400) NULL,
        ChannelName    NVARCHAR(400) NULL,
        CurrentValue   FLOAT         NOT NULL,
        ThresholdValue FLOAT         NOT NULL,
        TriggeredTime  DATETIME2     NOT NULL
    );");

                // Alarm level (1/2/3) reached by each latched alarm. Rows written before levels
                // existed fired at 100 % of the threshold, which is level 3.
                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'Level' AND Object_ID = Object_ID(N'AlarmState'))
    ALTER TABLE AlarmState ADD [Level] INT NOT NULL CONSTRAINT DF_AlarmState_Level DEFAULT 3;");

                // Structure the sensor belongs to, so a restored alarm can still name it in e-mails.
                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'StructureName' AND Object_ID = Object_ID(N'AlarmState'))
    ALTER TABLE AlarmState ADD StructureName NVARCHAR(400) NULL;");

                // Operational settings, editable without a rebuild. Every consumer falls back to its
                // compiled-in default, so an empty table changes nothing.
                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AppSetting')
    CREATE TABLE AppSetting (
        [Key]       NVARCHAR(200) NOT NULL PRIMARY KEY,
        [Value]     NVARCHAR(MAX) NULL,
        Description NVARCHAR(400) NULL,
        UpdatedAt   DATETIME2     NOT NULL CONSTRAINT DF_AppSetting_UpdatedAt DEFAULT SYSDATETIME()
    );");

                // Audit journal of deliberate user actions. Append-only in practice: only an
                // administrator can purge it, and the purge journals itself.
                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Journal')
    CREATE TABLE Journal (
        JournalId   BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Timestamp   DATETIME2     NOT NULL CONSTRAINT DF_Journal_Timestamp DEFAULT SYSDATETIME(),
        UserName    NVARCHAR(256) NULL,
        UserEmail   NVARCHAR(256) NULL,
        Category    NVARCHAR(50)  NULL,
        Action      NVARCHAR(100) NULL,
        Target      NVARCHAR(400) NULL,
        Details     NVARCHAR(MAX) NULL,
        StructureId UNIQUEIDENTIFIER NULL,
        EventId     UNIQUEIDENTIFIER NULL,
        Severity    TINYINT       NOT NULL CONSTRAINT DF_Journal_Severity DEFAULT 0
    );");

                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Journal_Timestamp')
    CREATE INDEX IX_Journal_Timestamp ON Journal (Timestamp DESC);");

                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Journal_Category_Timestamp')
    CREATE INDEX IX_Journal_Category_Timestamp ON Journal (Category, Timestamp DESC);");

                // The mothergrids at a palonnier's two ends, as the optimiser chose them. Deliberately
                // no foreign key: the optimiser deletes and re-inserts a scenario's elements as one
                // batch, SQL Server refuses cascading self-references, and the load calculation only
                // uses an id that names a mothergrid the palonnier still meets.
                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'MotherAID' AND Object_ID = Object_ID(N'dbo.MotherGridPalonnier'))
    ALTER TABLE dbo.MotherGridPalonnier ADD MotherAID UNIQUEIDENTIFIER NULL;");

                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'MotherBID' AND Object_ID = Object_ID(N'dbo.MotherGridPalonnier'))
    ALTER TABLE dbo.MotherGridPalonnier ADD MotherBID UNIQUEIDENTIFIER NULL;");

                // Which mothergrids a palonnier hangs from, where somebody decided - the optimiser, or a
                // person hooking or unhooking a crossing by hand - as JSON (Helpers.PalonnierHooks). A
                // palonnier hangs from every mothergrid it crosses unless a record says otherwise. On the
                // palonnier's own row rather than in a table: it travels with the row, and a record that
                // names a mothergrid the palonnier no longer meets is simply ignored.
                db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'Accrochages' AND Object_ID = Object_ID(N'dbo.MotherGridPalonnier'))
    ALTER TABLE dbo.MotherGridPalonnier ADD Accrochages NVARCHAR(MAX) NULL;");

                // The pieces a truss is assembled from, as JSON whole centimetres (Helpers.TrussComposition).
                // In its own try: once EF maps the property every element query selects the column, so its
                // absence must be visible in the log rather than hidden behind an earlier patch's failure.
                try
                {
                    db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'Composition' AND Object_ID = Object_ID(N'dbo.MotherGridPalonnier'))
    ALTER TABLE dbo.MotherGridPalonnier ADD Composition NVARCHAR(400) NULL;");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureDatabaseSchema] Composition column failed: {ex.Message}");
                }

                // Coordinates now follow the studies' axes on every page and in every file (Helpers.StudyAxes),
                // and Charges.xlsx was always written in them - but the scenic loads were stored exactly as
                // typed, and the calculations read them in the grid's axes. Once, move the stored loads to the
                // grid's axes, keeping the values as they were in ChargesSceniqueAxesBackup so the change can
                // be undone. The layouts computed from the old reading must be generated again.
                try
                {
                    db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (SELECT 1 FROM AppSetting WHERE [Key] = N'coords.axes')
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.ChargesSceniqueAxesBackup', N'U') IS NULL
        CREATE TABLE dbo.ChargesSceniqueAxesBackup (
            ChargesSceniqueID UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            X                 FLOAT            NULL,
            Y                 FLOAT            NULL,
            BackedUpAt        DATETIME2        NOT NULL CONSTRAINT DF_ChargesSceniqueAxesBackup_At DEFAULT SYSDATETIME()
        );

    INSERT INTO dbo.ChargesSceniqueAxesBackup (ChargesSceniqueID, X, Y)
        SELECT c.ChargesSceniqueID, c.X, c.Y
        FROM dbo.ChargesScenique c
        WHERE NOT EXISTS (SELECT 1 FROM dbo.ChargesSceniqueAxesBackup b WHERE b.ChargesSceniqueID = c.ChargesSceniqueID);

    UPDATE dbo.ChargesScenique SET X = -Y, Y = -X;

    INSERT INTO AppSetting ([Key], [Value], Description)
        VALUES (N'coords.axes', N'studies',
                N'Charges scéniques converties aux axes de la grille ; valeurs d''origine dans ChargesSceniqueAxesBackup');

    COMMIT TRANSACTION;
END");
                    AliantWebS.Data.AppSettings.Invalidate();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureDatabaseSchema] scenic load axes conversion failed: {ex.Message}");
                }

                // Production held 619 attributes stored twice on the same element, and 4 attributes linked twice to the
                // same element type. The links came first: adding an element creates one attribute row per link, so
                // Elem3d elements got two Ix, Iy and Iz rows, a value was typed into one of each pair, and pasting
                // elements with their attributes copied both rows to some 200 elements. Nothing in the database stopped
                // it, so unique indexes on both pairs now do, whatever the code path. Until both exist: keep one row per
                // pair (the one holding a value, then a date) and one link per pair, copying everything removed into
                // adbo.AttributeElementDedupeBackup and adbo.AttributeTypeElementTypeDedupeBackup. A pair whose rows
                // hold different values is left alone - only a person can tell which one is right - and the attribute
                // index waits until it is resolved, so this runs, and says so, at every start until then.
                try
                {
                    var ran = new SqlParameter("@Ran", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                    var rowsRemoved = new SqlParameter("@RowsRemoved", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var linksRemoved = new SqlParameter("@LinksRemoved", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var conflictGroups = new SqlParameter("@ConflictGroups", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var attributeIndex = new SqlParameter("@AttributeIndex", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                    var linkIndex = new SqlParameter("@LinkIndex", SqlDbType.Bit) { Direction = ParameterDirection.Output };

                    // The scans and the index build grow with the table: a tenth of a second on today's few thousand
                    // rows, but past EF's default 30 s on a few million, and a timeout rolls the whole patch back at
                    // every start. The host has not started yet, so the table locks keep no user waiting.
                    db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
                    db.Database.ExecuteSqlRaw(@"
SET @Ran = 0;
SET @RowsRemoved = 0;
SET @LinksRemoved = 0;
SET @ConflictGroups = 0;
SET @AttributeIndex = CASE WHEN EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_AttributeElement_ElementID_AttributeTypeID'
      AND object_id = OBJECT_ID(N'adbo.AttributeElement')) THEN 1 ELSE 0 END;
SET @LinkIndex = CASE WHEN EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_AttributeTypeElementType_ElementTypeID_AttributeTypeID'
      AND object_id = OBJECT_ID(N'adbo.AttributeTypeElementType')) THEN 1 ELSE 0 END;

IF @AttributeIndex = 0 OR @LinkIndex = 0
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    SET @Ran = 1;

    IF OBJECT_ID(N'adbo.AttributeElementDedupeBackup', N'U') IS NULL
        CREATE TABLE adbo.AttributeElementDedupeBackup (
            AttributeElementID     UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            ElementID              UNIQUEIDENTIFIER NOT NULL,
            AttributeTypeID        UNIQUEIDENTIFIER NOT NULL,
            [Date]                 DATE             NULL,
            [Value]                NVARCHAR(50)     NULL,
            KeptAttributeElementID UNIQUEIDENTIFIER NULL,
            BackedUpAt             DATETIME2        NOT NULL CONSTRAINT DF_AttributeElementDedupeBackup_At DEFAULT SYSDATETIME(),
            [Reason]               NVARCHAR(30)     NOT NULL CONSTRAINT DF_AttributeElementDedupeBackup_Reason DEFAULT N'duplicate'
        );

    IF OBJECT_ID(N'adbo.AttributeTypeElementTypeDedupeBackup', N'U') IS NULL
        CREATE TABLE adbo.AttributeTypeElementTypeDedupeBackup (
            AttributeTypeElementTypeID     UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            ElementTypeID                  UNIQUEIDENTIFIER NOT NULL,
            AttributeTypeID                UNIQUEIDENTIFIER NOT NULL,
            KeptAttributeTypeElementTypeID UNIQUEIDENTIFIER NOT NULL,
            BackedUpAt                     DATETIME2        NOT NULL CONSTRAINT DF_AttributeTypeElementTypeDedupeBackup_At DEFAULT SYSDATETIME()
        );

    -- Attribute rows. Only pairs whose non-blank values are the same byte for byte are merged: a difference in
    -- case or in surrounding spaces is a conflict, since a padded material name matches no case in the
    -- calculation code. The values are compared as varbinary because nvarchar comparisons ignore trailing
    -- spaces, whatever the collation. The table locks, held until COMMIT, keep new duplicates out between the
    -- clean-up and the index.
    DECLARE @RemovedRows TABLE (
        AttributeElementID     UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        KeptAttributeElementID UNIQUEIDENTIFIER NOT NULL);

    WITH Pairs AS (
        SELECT ElementID, AttributeTypeID
        FROM adbo.AttributeElement WITH (TABLOCKX, HOLDLOCK)
        GROUP BY ElementID, AttributeTypeID
        HAVING COUNT(*) > 1
           AND COUNT(DISTINCT CASE WHEN NULLIF(LTRIM(RTRIM([Value])), N'') IS NOT NULL THEN CONVERT(VARBINARY(100), [Value]) END) <= 1
    ), Candidates AS (
        SELECT a.AttributeElementID, a.ElementID, a.AttributeTypeID,
               CASE WHEN NULLIF(LTRIM(RTRIM(a.[Value])), N'') IS NULL THEN 1 ELSE 0 END AS Blank,
               CASE WHEN a.[Date] IS NULL THEN 1 ELSE 0 END AS Undated
        FROM adbo.AttributeElement a
        JOIN Pairs p ON p.ElementID = a.ElementID AND p.AttributeTypeID = a.AttributeTypeID
    ), Ranked AS (
        SELECT AttributeElementID,
               FIRST_VALUE(AttributeElementID) OVER (PARTITION BY ElementID, AttributeTypeID
                                                     ORDER BY Blank, Undated, AttributeElementID) AS KeptAttributeElementID,
               ROW_NUMBER() OVER (PARTITION BY ElementID, AttributeTypeID
                                  ORDER BY Blank, Undated, AttributeElementID) AS Position
        FROM Candidates
    )
    INSERT INTO @RemovedRows (AttributeElementID, KeptAttributeElementID)
        SELECT AttributeElementID, KeptAttributeElementID FROM Ranked WHERE Position > 1;

    -- Reason is left to its default, N'duplicate', rather than named: a table made before the column existed only
    -- gains it in the unlinked-attributes step below, and SQL Server checks a batch's column names before running
    -- any of it, so naming it would stop this batch on such a table even when the clean-up has nothing to do.
    INSERT INTO adbo.AttributeElementDedupeBackup (AttributeElementID, ElementID, AttributeTypeID, [Date], [Value], KeptAttributeElementID)
        SELECT a.AttributeElementID, a.ElementID, a.AttributeTypeID, a.[Date], a.[Value], r.KeptAttributeElementID
        FROM adbo.AttributeElement a
        JOIN @RemovedRows r ON r.AttributeElementID = a.AttributeElementID;

    DELETE a
        FROM adbo.AttributeElement a
        JOIN @RemovedRows r ON r.AttributeElementID = a.AttributeElementID;
    SET @RowsRemoved = @@ROWCOUNT;

    SELECT @ConflictGroups = COUNT(*)
        FROM (SELECT ElementID
              FROM adbo.AttributeElement
              GROUP BY ElementID, AttributeTypeID
              HAVING COUNT(DISTINCT CASE WHEN NULLIF(LTRIM(RTRIM([Value])), N'') IS NOT NULL THEN CONVERT(VARBINARY(100), [Value]) END) > 1) AS Conflicts;

    -- Links carry nothing but their two ids: keep the lowest of each pair.
    DECLARE @RemovedLinks TABLE (
        AttributeTypeElementTypeID     UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        KeptAttributeTypeElementTypeID UNIQUEIDENTIFIER NOT NULL);

    WITH Ranked AS (
        SELECT AttributeTypeElementTypeID,
               FIRST_VALUE(AttributeTypeElementTypeID) OVER (PARTITION BY ElementTypeID, AttributeTypeID
                                                             ORDER BY AttributeTypeElementTypeID) AS KeptAttributeTypeElementTypeID,
               ROW_NUMBER() OVER (PARTITION BY ElementTypeID, AttributeTypeID
                                  ORDER BY AttributeTypeElementTypeID) AS Position
        FROM adbo.AttributeTypeElementType WITH (TABLOCKX, HOLDLOCK)
    )
    INSERT INTO @RemovedLinks (AttributeTypeElementTypeID, KeptAttributeTypeElementTypeID)
        SELECT AttributeTypeElementTypeID, KeptAttributeTypeElementTypeID FROM Ranked WHERE Position > 1;

    INSERT INTO adbo.AttributeTypeElementTypeDedupeBackup (AttributeTypeElementTypeID, ElementTypeID, AttributeTypeID, KeptAttributeTypeElementTypeID)
        SELECT l.AttributeTypeElementTypeID, l.ElementTypeID, l.AttributeTypeID, r.KeptAttributeTypeElementTypeID
        FROM adbo.AttributeTypeElementType l
        JOIN @RemovedLinks r ON r.AttributeTypeElementTypeID = l.AttributeTypeElementTypeID;

    DELETE l
        FROM adbo.AttributeTypeElementType l
        JOIN @RemovedLinks r ON r.AttributeTypeElementTypeID = l.AttributeTypeElementTypeID;
    SET @LinksRemoved = @@ROWCOUNT;

    IF @AttributeIndex = 0 AND NOT EXISTS (
        SELECT 1 FROM adbo.AttributeElement
        GROUP BY ElementID, AttributeTypeID
        HAVING COUNT(*) > 1)
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UX_AttributeElement_ElementID_AttributeTypeID
            ON adbo.AttributeElement (ElementID, AttributeTypeID);
        SET @AttributeIndex = 1;
    END

    IF @LinkIndex = 0 AND NOT EXISTS (
        SELECT 1 FROM adbo.AttributeTypeElementType
        GROUP BY ElementTypeID, AttributeTypeID
        HAVING COUNT(*) > 1)
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UX_AttributeTypeElementType_ElementTypeID_AttributeTypeID
            ON adbo.AttributeTypeElementType (ElementTypeID, AttributeTypeID);
        SET @LinkIndex = 1;
    END

    COMMIT TRANSACTION;
END", ran, rowsRemoved, linksRemoved, conflictGroups, attributeIndex, linkIndex);

                    if ((bool)ran.Value)
                    {
                        int removed = (int)rowsRemoved.Value, links = (int)linksRemoved.Value, conflicts = (int)conflictGroups.Value;
                        if (removed > 0 || links > 0)
                            Console.WriteLine($"[EnsureDatabaseSchema] duplicate attributes: rows removed: {removed}, links removed: {links}; copies in adbo.AttributeElementDedupeBackup and adbo.AttributeTypeElementTypeDedupeBackup.");
                        if (conflicts > 0)
                            Console.WriteLine($"[EnsureDatabaseSchema] duplicate attributes: element/attribute pairs whose rows hold different values, left untouched: {conflicts}; UX_AttributeElement_ElementID_AttributeTypeID skipped until one row of each pair is kept.");
                        Console.WriteLine("[EnsureDatabaseSchema] duplicate attributes: unique index UX_AttributeElement_ElementID_AttributeTypeID "
                            + ((bool)attributeIndex.Value ? "present" : "missing")
                            + ", UX_AttributeTypeElementType_ElementTypeID_AttributeTypeID "
                            + ((bool)linkIndex.Value ? "present" : "missing") + ".");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureDatabaseSchema] duplicate attributes clean-up failed: {ex.Message}");
                }
                finally
                {
                    db.Database.SetCommandTimeout(null);
                }

                // EditElement lists every attribute row an element holds, including rows of attribute types its element
                // type is not linked to: production's Beam3d beams kept empty E, G, Ix, Iy, Iz, Poisson, Rho and S rows
                // after Beam3d was linked to its six current attributes instead. Once per database, delete those empty
                // rows - no value and no date, on an element that has a type - copying them into
                // adbo.AttributeElementDedupeBackup with Reason 'unlinked-empty'. A row that holds something stays, and
                // EditElement shows it greyed and read-only. Positions come first: the monitoring plan wrote X and Y rows
                // for sensors of types never linked to X and Y, and those were all of production's valued unlinked rows,
                // so X and Y are linked to every element type whose elements hold one, which keeps positions editable.
                try
                {
                    var ran = new SqlParameter("@Ran", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                    var xTypes = new SqlParameter("@XTypes", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var yTypes = new SqlParameter("@YTypes", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var linksAdded = new SqlParameter("@LinksAdded", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var rowsRemoved = new SqlParameter("@RowsRemoved", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var rowsKept = new SqlParameter("@RowsKept", SqlDbType.Int) { Direction = ParameterDirection.Output };

                    // Full scans of AttributeElement under table locks, as in the duplicate clean-up above.
                    db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

                    // The first version of the duplicate clean-up made its backup table without Reason and with a kept row
                    // required; the rows such a table holds are all duplicates, which the new column's default says. The
                    // alter needs a batch of its own, outside the transaction below: SQL Server checks the column names
                    // of a whole batch before running any of it, so the step below, which names Reason when it copies a
                    // row away, cannot be the batch that adds the column. A column added and left behind by a rolled
                    // back step costs nothing, and the step runs again at the next start.
                    db.Database.ExecuteSqlRaw(@"
IF NOT EXISTS (SELECT 1 FROM AppSetting WHERE [Key] = N'attributes.unlinked-cleanup')
   AND OBJECT_ID(N'adbo.AttributeElementDedupeBackup', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'adbo.AttributeElementDedupeBackup', N'Reason') IS NULL
        ALTER TABLE adbo.AttributeElementDedupeBackup
            ADD [Reason] NVARCHAR(30) NOT NULL CONSTRAINT DF_AttributeElementDedupeBackup_Reason DEFAULT N'duplicate';

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE Name = N'KeptAttributeElementID'
          AND Object_ID = Object_ID(N'adbo.AttributeElementDedupeBackup')
          AND is_nullable = 0)
        ALTER TABLE adbo.AttributeElementDedupeBackup ALTER COLUMN KeptAttributeElementID UNIQUEIDENTIFIER NULL;
END");

                    db.Database.ExecuteSqlRaw(@"
SET @Ran = 0;
SET @XTypes = 0;
SET @YTypes = 0;
SET @LinksAdded = 0;
SET @RowsRemoved = 0;
SET @RowsKept = 0;

IF NOT EXISTS (SELECT 1 FROM AppSetting WHERE [Key] = N'attributes.unlinked-cleanup')
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    SET @Ran = 1;

    IF OBJECT_ID(N'adbo.AttributeElementDedupeBackup', N'U') IS NULL
        CREATE TABLE adbo.AttributeElementDedupeBackup (
            AttributeElementID     UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            ElementID              UNIQUEIDENTIFIER NOT NULL,
            AttributeTypeID        UNIQUEIDENTIFIER NOT NULL,
            [Date]                 DATE             NULL,
            [Value]                NVARCHAR(50)     NULL,
            KeptAttributeElementID UNIQUEIDENTIFIER NULL,
            BackedUpAt             DATETIME2        NOT NULL CONSTRAINT DF_AttributeElementDedupeBackup_At DEFAULT SYSDATETIME(),
            [Reason]               NVARCHAR(30)     NOT NULL CONSTRAINT DF_AttributeElementDedupeBackup_Reason DEFAULT N'duplicate'
        );

    -- X and Y are found by name, as the monitoring page finds them. A name several attribute types carry links nothing:
    -- the page would pick any one of them, and only a person can say which is meant.
    SET @XTypes = (SELECT COUNT(*) FROM adbo.AttributeType WHERE Name = N'X');
    SET @YTypes = (SELECT COUNT(*) FROM adbo.AttributeType WHERE Name = N'Y');

    INSERT INTO adbo.AttributeTypeElementType (ElementTypeID, AttributeTypeID)
        SELECT DISTINCT e.ElementTypeID, a.AttributeTypeID
        FROM adbo.AttributeElement a
        JOIN adbo.AttributeType t ON t.AttributeTypeID = a.AttributeTypeID
        JOIN adbo.Element e ON e.ElementID = a.ElementID
        WHERE ((t.Name = N'X' AND @XTypes = 1) OR (t.Name = N'Y' AND @YTypes = 1))
          AND e.ElementTypeID IS NOT NULL
          AND (NULLIF(LTRIM(RTRIM(a.[Value])), N'') IS NOT NULL OR a.[Date] IS NOT NULL)
          AND NOT EXISTS (
              SELECT 1 FROM adbo.AttributeTypeElementType l
              WHERE l.ElementTypeID = e.ElementTypeID AND l.AttributeTypeID = a.AttributeTypeID);
    SET @LinksAdded = @@ROWCOUNT;

    -- A row is empty when its value is blank, as the duplicate clean-up judges it, and it has no date. The table
    -- locks, held until COMMIT, keep a chosen row from gaining a value, or its attribute type a link, before it is
    -- deleted.
    DECLARE @RemovedRows TABLE (AttributeElementID UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);

    INSERT INTO @RemovedRows (AttributeElementID)
        SELECT a.AttributeElementID
        FROM adbo.AttributeElement a WITH (TABLOCKX, HOLDLOCK)
        JOIN adbo.Element e ON e.ElementID = a.ElementID
        WHERE e.ElementTypeID IS NOT NULL
          AND NULLIF(LTRIM(RTRIM(a.[Value])), N'') IS NULL
          AND a.[Date] IS NULL
          AND NOT EXISTS (
              SELECT 1 FROM adbo.AttributeTypeElementType l WITH (TABLOCKX, HOLDLOCK)
              WHERE l.ElementTypeID = e.ElementTypeID AND l.AttributeTypeID = a.AttributeTypeID);

    INSERT INTO adbo.AttributeElementDedupeBackup (AttributeElementID, ElementID, AttributeTypeID, [Date], [Value], KeptAttributeElementID, [Reason])
        SELECT a.AttributeElementID, a.ElementID, a.AttributeTypeID, a.[Date], a.[Value], NULL, N'unlinked-empty'
        FROM adbo.AttributeElement a
        JOIN @RemovedRows r ON r.AttributeElementID = a.AttributeElementID;

    DELETE a
        FROM adbo.AttributeElement a
        JOIN @RemovedRows r ON r.AttributeElementID = a.AttributeElementID;
    SET @RowsRemoved = @@ROWCOUNT;

    -- Every unlinked row left holds a value or a date.
    SELECT @RowsKept = COUNT(*)
        FROM adbo.AttributeElement a
        JOIN adbo.Element e ON e.ElementID = a.ElementID
        WHERE e.ElementTypeID IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM adbo.AttributeTypeElementType l
              WHERE l.ElementTypeID = e.ElementTypeID AND l.AttributeTypeID = a.AttributeTypeID);

    -- Only done when both names were unambiguous. A name several attribute types carry left that axis's
    -- positions unlinked, and unlinked is read-only in EditElement, so the step has to come back once the
    -- name is a person's to fix. Coming back costs nothing: the delete finds no empty unlinked row left,
    -- and the link insert skips what is already linked.
    IF @XTypes = 1 AND @YTypes = 1
        INSERT INTO AppSetting ([Key], [Value], Description)
            VALUES (N'attributes.unlinked-cleanup', N'done',
                    N'Attributs vides d''un type non lié au type de l''élément supprimés (copies dans adbo.AttributeElementDedupeBackup, Reason unlinked-empty) ; X et Y liés aux types des éléments positionnés');

    COMMIT TRANSACTION;
END", ran, xTypes, yTypes, linksAdded, rowsRemoved, rowsKept);

                    if ((bool)ran.Value)
                    {
                        AliantWebS.Data.AppSettings.Invalidate();
                        foreach (var (name, types) in new[] { ("X", (int)xTypes.Value), ("Y", (int)yTypes.Value) })
                        {
                            if (types != 1)
                                Console.WriteLine($"[EnsureDatabaseSchema] unlinked attributes: {types} attribute types named '{name}', so no {name} link added; rename or remove the extra one and the next start links them.");
                        }
                        Console.WriteLine($"[EnsureDatabaseSchema] unlinked attributes: X/Y links added: {linksAdded.Value}, empty rows removed: {rowsRemoved.Value}; copies in adbo.AttributeElementDedupeBackup (Reason 'unlinked-empty').");
                        if ((int)rowsKept.Value > 0)
                            Console.WriteLine($"[EnsureDatabaseSchema] unlinked attributes: rows holding a value or a date for an attribute type their element's type is not linked to, kept and shown read-only: {rowsKept.Value}.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureDatabaseSchema] unlinked attributes clean-up failed: {ex.Message}");
                }
                finally
                {
                    db.Database.SetCommandTimeout(null);
                }

                // Every reader of an alarm threshold - the dashboard gauges and charge curve, the plan's colours, alarm
                // detection, the Charpente Element Mapping page - reads it per channel, "weight:1500;temperature:60". The
                // shackles held a bare "1500" (59 of production's 81) or nothing (the other 22), so none had a threshold:
                // the gauges sat at 0 %, the charge curve was empty and no shackle could raise an alarm. Once per database,
                // give every shackle's threshold attribute a weight threshold: a blank value becomes "weight:1500", the
                // figure decided for every shackle on 2026-09-16, a bare number keeps its number, and a shackle with no
                // row gets one. Anything else is left alone. Previous values go to adbo.AttributeElementDedupeBackup with
                // Reason 'threshold-format', and each shackle gets a system journal row. Once only, because an emptied
                // threshold is later somebody's decision. The threshold attribute is the one linked to the shackle's
                // element type under the name seuil_alarme or one of its aliases, as the mapping page picks it; a type
                // linked to several stops the whole step, since only a person can say which one is meant.
                try
                {
                    var ran = new SqlParameter("@Ran", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                    var ambiguousTypes = new SqlParameter("@AmbiguousTypes", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var unlinkedTypes = new SqlParameter("@UnlinkedTypes", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var rowsRewritten = new SqlParameter("@RowsRewritten", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var rowsAdded = new SqlParameter("@RowsAdded", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    var rowsLeft = new SqlParameter("@RowsLeft", SqlDbType.Int) { Direction = ParameterDirection.Output };

                    // Full scans of AttributeElement under table locks, as in the clean-ups above.
                    db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
                    db.Database.ExecuteSqlRaw(@"
SET @Ran = 0;
SET @AmbiguousTypes = 0;
SET @UnlinkedTypes = 0;
SET @RowsRewritten = 0;
SET @RowsAdded = 0;
SET @RowsLeft = 0;

IF NOT EXISTS (SELECT 1 FROM AppSetting WHERE [Key] = N'thresholds.manille-weight')
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    SET @Ran = 1;

    -- The names TagMappingResolver accepts for the tag: the tag itself and every alias.
    DECLARE @ThresholdNames TABLE (Name NVARCHAR(400) NOT NULL);
    INSERT INTO @ThresholdNames (Name) VALUES (N'seuil_alarme');
    IF OBJECT_ID(N'dbo.TagMappingTable', N'U') IS NOT NULL
        INSERT INTO @ThresholdNames (Name)
            SELECT LTRIM(RTRIM(UserTag))
            FROM dbo.TagMappingTable
            WHERE LTRIM(RTRIM(CodeTag)) = N'seuil_alarme'
              AND NULLIF(LTRIM(RTRIM(UserTag)), N'') IS NOT NULL;

    -- Shackle types, found as SensorChargeRatioHelper finds them, with their linked threshold attribute types.
    DECLARE @Links TABLE (ElementTypeID UNIQUEIDENTIFIER NOT NULL, AttributeTypeID UNIQUEIDENTIFIER NULL);
    INSERT INTO @Links (ElementTypeID, AttributeTypeID)
        SELECT DISTINCT et.ElementTypeID, t.AttributeTypeID
        FROM adbo.ElementType et
        JOIN adbo.Category c ON c.CategoryID = et.CategoryID
        LEFT JOIN adbo.AttributeTypeElementType l ON l.ElementTypeID = et.ElementTypeID
        LEFT JOIN adbo.AttributeType t ON t.AttributeTypeID = l.AttributeTypeID
                                      AND LTRIM(RTRIM(t.Name)) IN (SELECT Name FROM @ThresholdNames)
        WHERE LOWER(c.Name) = N'sensing device'
          AND et.Name LIKE N'%Manille%';

    DECLARE @Types TABLE (ElementTypeID UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, AttributeTypeID UNIQUEIDENTIFIER NULL, Candidates INT NOT NULL);
    INSERT INTO @Types (ElementTypeID, AttributeTypeID, Candidates)
        SELECT k.ElementTypeID,
               CASE WHEN k.Candidates = 1 THEN (SELECT TOP 1 x.AttributeTypeID FROM @Links x
                                                WHERE x.ElementTypeID = k.ElementTypeID AND x.AttributeTypeID IS NOT NULL) END,
               k.Candidates
        FROM (SELECT ElementTypeID, SUM(CASE WHEN AttributeTypeID IS NULL THEN 0 ELSE 1 END) AS Candidates
              FROM @Links GROUP BY ElementTypeID) AS k;

    SELECT @AmbiguousTypes = COUNT(*) FROM @Types WHERE Candidates > 1;
    SELECT @UnlinkedTypes = COUNT(*) FROM @Types WHERE Candidates = 0;

    IF @AmbiguousTypes = 0
    BEGIN
        IF OBJECT_ID(N'adbo.AttributeElementDedupeBackup', N'U') IS NULL
            CREATE TABLE adbo.AttributeElementDedupeBackup (
                AttributeElementID     UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                ElementID              UNIQUEIDENTIFIER NOT NULL,
                AttributeTypeID        UNIQUEIDENTIFIER NOT NULL,
                [Date]                 DATE             NULL,
                [Value]                NVARCHAR(50)     NULL,
                KeptAttributeElementID UNIQUEIDENTIFIER NULL,
                BackedUpAt             DATETIME2        NOT NULL CONSTRAINT DF_AttributeElementDedupeBackup_At DEFAULT SYSDATETIME(),
                [Reason]               NVARCHAR(30)     NOT NULL CONSTRAINT DF_AttributeElementDedupeBackup_Reason DEFAULT N'duplicate'
            );

        -- A row with no id is one to add. A bare number is at most 50 characters, the backup's width.
        DECLARE @Changes TABLE (
            AttributeElementID UNIQUEIDENTIFIER NULL,
            ElementID          UNIQUEIDENTIFIER NOT NULL,
            AttributeTypeID    UNIQUEIDENTIFIER NOT NULL,
            OldValue           NVARCHAR(100)    NULL,
            NewValue           NVARCHAR(100)    NOT NULL);

        INSERT INTO @Changes (AttributeElementID, ElementID, AttributeTypeID, OldValue, NewValue)
            SELECT a.AttributeElementID, a.ElementID, a.AttributeTypeID, a.[Value],
                   N'weight:' + COALESCE(NULLIF(LTRIM(RTRIM(a.[Value])), N''), N'1500')
            FROM adbo.AttributeElement a WITH (TABLOCKX, HOLDLOCK)
            JOIN adbo.Element e ON e.ElementID = a.ElementID
            JOIN @Types ty ON ty.ElementTypeID = e.ElementTypeID AND ty.AttributeTypeID = a.AttributeTypeID
            WHERE NULLIF(LTRIM(RTRIM(a.[Value])), N'') IS NULL
               OR (TRY_CONVERT(FLOAT, LTRIM(RTRIM(a.[Value]))) IS NOT NULL AND LEN(LTRIM(RTRIM(a.[Value]))) <= 50);

        INSERT INTO @Changes (AttributeElementID, ElementID, AttributeTypeID, OldValue, NewValue)
            SELECT NULL, e.ElementID, ty.AttributeTypeID, NULL, N'weight:1500'
            FROM adbo.Element e
            JOIN @Types ty ON ty.ElementTypeID = e.ElementTypeID
            WHERE ty.AttributeTypeID IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM adbo.AttributeElement a
                  WHERE a.ElementID = e.ElementID AND a.AttributeTypeID = ty.AttributeTypeID);

        -- The row stays, holding the new value, so it is its own kept row.
        INSERT INTO adbo.AttributeElementDedupeBackup (AttributeElementID, ElementID, AttributeTypeID, [Date], [Value], KeptAttributeElementID, [Reason])
            SELECT a.AttributeElementID, a.ElementID, a.AttributeTypeID, a.[Date], LEFT(a.[Value], 50), a.AttributeElementID, N'threshold-format'
            FROM adbo.AttributeElement a
            JOIN @Changes ch ON ch.AttributeElementID = a.AttributeElementID
            WHERE NOT EXISTS (
                SELECT 1 FROM adbo.AttributeElementDedupeBackup b
                WHERE b.AttributeElementID = a.AttributeElementID);

        UPDATE a
            SET a.[Value] = ch.NewValue
            FROM adbo.AttributeElement a
            JOIN @Changes ch ON ch.AttributeElementID = a.AttributeElementID;
        SET @RowsRewritten = @@ROWCOUNT;

        INSERT INTO adbo.AttributeElement (ElementID, AttributeTypeID, [Value])
            SELECT ElementID, AttributeTypeID, NewValue FROM @Changes WHERE AttributeElementID IS NULL;
        SET @RowsAdded = @@ROWCOUNT;

        -- Written as CharpenteMapping journals a threshold save, with no user: the journal shows it as Système.
        -- The timestamp is given, as the app gives it: a Journal table made by EF has no default for it.
        INSERT INTO Journal ([Timestamp], Category, [Action], Target, Details, StructureId, Severity)
            SELECT SYSDATETIME(), N'Alarme', N'threshold.migrate', LEFT(e.Name, 400),
                   COALESCE(NULLIF(LTRIM(RTRIM(ch.OldValue)), N''), N'(vide)') + N' → ' + ch.NewValue,
                   e.StructureID, 1
            FROM @Changes ch
            JOIN adbo.Element e ON e.ElementID = ch.ElementID;

        -- What is left naming no channel at all is for a person; the mapping page flags it.
        SELECT @RowsLeft = COUNT(*)
            FROM adbo.AttributeElement a
            JOIN adbo.Element e ON e.ElementID = a.ElementID
            JOIN @Types ty ON ty.ElementTypeID = e.ElementTypeID AND ty.AttributeTypeID = a.AttributeTypeID
            WHERE CHARINDEX(N':', a.[Value]) = 0;

        INSERT INTO AppSetting ([Key], [Value], Description)
            VALUES (N'thresholds.manille-weight', N'done',
                    N'Seuils d''alarme des manilles au format weight: (vide → weight:1500, nombre seul → weight:nombre) ; anciennes valeurs dans adbo.AttributeElementDedupeBackup, Reason threshold-format');
    END

    COMMIT TRANSACTION;
END", ran, ambiguousTypes, unlinkedTypes, rowsRewritten, rowsAdded, rowsLeft);

                    if ((bool)ran.Value)
                    {
                        if ((int)ambiguousTypes.Value > 0)
                        {
                            Console.WriteLine($"[EnsureDatabaseSchema] shackle thresholds: {ambiguousTypes.Value} Manille type(s) linked to several alarm threshold attributes, so nothing rewritten; keep one link per type and the next start does it.");
                        }
                        else
                        {
                            AliantWebS.Data.AppSettings.Invalidate();
                            Console.WriteLine($"[EnsureDatabaseSchema] shackle thresholds: rows rewritten as weight:<value>: {rowsRewritten.Value}, rows added as weight:1500: {rowsAdded.Value}; previous values in adbo.AttributeElementDedupeBackup (Reason 'threshold-format').");
                            if ((int)rowsLeft.Value > 0)
                                Console.WriteLine($"[EnsureDatabaseSchema] shackle thresholds: values naming no channel, left for a person to fix: {rowsLeft.Value}.");
                        }
                        if ((int)unlinkedTypes.Value > 0)
                            Console.WriteLine($"[EnsureDatabaseSchema] shackle thresholds: {unlinkedTypes.Value} Manille type(s) with no alarm threshold attribute linked, left without thresholds.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureDatabaseSchema] shackle thresholds rewrite failed: {ex.Message}");
                }
                finally
                {
                    db.Database.SetCommandTimeout(null);
                }

                // One Charpente row per scenario: UX_Charpente_EventMPScenarioID (decided by Rafic, 2026-09-27). Two tabs
                // saving a scenario's first Charpente at the same moment could insert two rows, and only one was ever read
                // again - the other tab's calculation was lost without a word. Created only while no scenario has two rows:
                // a scenario that has is reported and left for a person to choose from, and the index waits for it.
                try
                {
                    var state = new SqlParameter("@State", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    db.Database.ExecuteSqlRaw(@"
SET @State = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_Charpente_EventMPScenarioID' AND object_id = OBJECT_ID(N'dbo.Charpente'))
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.Charpente GROUP BY EventMPScenarioID HAVING COUNT(*) > 1)
        SET @State = 2;
    ELSE
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UX_Charpente_EventMPScenarioID ON dbo.Charpente (EventMPScenarioID);
        SET @State = 1;
    END
END", state);

                    if ((int)state.Value == 1)
                        Console.WriteLine("[EnsureDatabaseSchema] Charpente: unique index UX_Charpente_EventMPScenarioID created.");
                    else if ((int)state.Value == 2)
                        Console.WriteLine("[EnsureDatabaseSchema] Charpente: some scenarios have several Charpente rows; "
                            + "UX_Charpente_EventMPScenarioID not created until one row of each is kept.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureDatabaseSchema] Charpente unique index failed: {ex.Message}");
                }

                Console.WriteLine("[EnsureDatabaseSchema] schema check complete.");
            }
            catch (Exception ex)
            {
                // Don't block startup on a schema patch; log and continue.
                Console.WriteLine($"[EnsureDatabaseSchema] failed: {ex.Message}");
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                 
                    webBuilder.UseStartup<Startup>();

                    // Force HTTP-only inside the container; HTTPS is terminated by nginx
                    //webBuilder.ConfigureKestrel(options =>
                    //{
                    //    options.ListenAnyIP(80); // no HTTPS listener
                    //});

                });
    }
    /*
Scaffold-DbContext "Data Source=CSTFI-DGC04; Initial Catalog=AliantWeb; Integrated Security = True; ConnectRetryCount=0" Microsoft.EntityFrameworkCore.SqlServer -OutputDir Models -Context AliantWebContext -DataAnnotations -Force
*/
    /*Scaffold-DbContext "Data Source=RaficLaptop; Initial Catalog=AliantWeb; Encrypt=False; Integrated Security = True; ConnectRetryCount=0" Microsoft.EntityFrameworkCore.SqlServer -OutputDir Models -Context AliantWebContext -DataAnnotations -Force*/
    //Guid.Parse("9245fe4a-d402-451c-b9ed-9c1a04247482") = -1
    //Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7") =-2

    // Credentials, tokens and connection strings once noted here were moved out of the repository (2026-09-30).
}

//# Stay in: C:\Users\Rafic\Desktop\Antares\AntaresNetwork\AliantWebS
//docker build -t raficfaddoul/antares:latest -f AliantWebS/Dockerfile .

//# Then push
//docker push raficfaddoul/antares:latest

//# Go to the parent directory (AntaresNetwork - where data/ folder exists)
//cd C:\Users\Rafic\Desktop\Antares\AntaresNetwork

//# Build using the Dockerfile in AliantWebS subfolder, with context at current directory
//docker build -t raficfaddoul/antares-db:latest - f AliantWebS/Dockerfile.dbfiles.

//# Push to Docker Hub
//docker push raficfaddoul/antares-db:latest


//https://192.168.2.10:9443/
//http://192.168.2.10:8080/