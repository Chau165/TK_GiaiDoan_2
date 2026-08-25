/*
   Isolated lifecycle benchmark. Run only against TKS_Thuc_Tap_V11_Perf_<rows>.
   It compares the old delete/fallback shape with the new scoped
   invalidate/queue/rebuild shape, then captures the rebuilt report path.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET STATISTICS IO ON;
SET STATISTICS TIME ON;

DECLARE @SnapshotDate DATE = CONVERT(DATE, '$(SnapshotDate)');
DECLARE @FromDate DATE = CONVERT(DATE, '$(FromDate)');
DECLARE @ToDate DATE = CONVERT(DATE, '$(ToDate)');
DECLARE @Kho_ID BIGINT;
DECLARE @San_Pham_ID BIGINT;

SELECT TOP (1)
       @Kho_ID = Kho_ID,
       @San_Pham_ID = San_Pham_ID
FROM dbo.InventoryBalance_Snapshot_Daily
WHERE Snapshot_Date = @SnapshotDate
  AND IsValid = 1
ORDER BY Kho_ID, San_Pham_ID;

IF @Kho_ID IS NULL OR @San_Pham_ID IS NULL
    THROW 52400, N'Lifecycle benchmark requires a valid baseline snapshot.', 1;

PRINT N'BEFORE_DELETE_FALLBACK_REPORT';
SELECT N'TempdbBeforeDeleteFallback' AS Metric,
       SUM(internal_object_reserved_page_count) * 8 AS InternalObjectKB,
       SUM(user_object_reserved_page_count) * 8 AS UserObjectKB
FROM tempdb.sys.dm_db_file_space_usage;
BEGIN TRANSACTION;
DELETE FROM dbo.InventoryBalance_Snapshot_Daily
WHERE Snapshot_Date = @SnapshotDate
  AND Kho_ID = @Kho_ID
  AND San_Pham_ID = @San_Pham_ID;
COMMIT TRANSACTION;

EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay = @FromDate,
    @Den_Ngay = @ToDate,
    @Page_Number = 1,
    @Page_Size = 10,
    @Ma_Dang_Nhap = N'PERF_USER';
SELECT N'TempdbAfterDeleteFallback' AS Metric,
       SUM(internal_object_reserved_page_count) * 8 AS InternalObjectKB,
       SUM(user_object_reserved_page_count) * 8 AS UserObjectKB
FROM tempdb.sys.dm_db_file_space_usage;

/* Restore exactly the isolated baseline row for the after-path. */
;WITH Movement AS
(
    SELECT h.Kho_ID, d.San_Pham_ID, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Quantity
    FROM dbo.tbl_XNK_Nhap_Kho h
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
    WHERE h.Is_Posted = 1
      AND h.Ngay_Nhap_Kho <= @SnapshotDate
      AND h.Kho_ID = @Kho_ID
      AND d.San_Pham_ID = @San_Pham_ID
    UNION ALL
    SELECT h.Kho_ID, d.San_Pham_ID, CAST(-d.SL_Xuat AS DECIMAL(18,3))
    FROM dbo.tbl_XNK_Xuat_Kho h
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
    WHERE h.Is_Posted = 1
      AND h.Ngay_Xuat_Kho <= @SnapshotDate
      AND h.Kho_ID = @Kho_ID
      AND d.San_Pham_ID = @San_Pham_ID
)
INSERT dbo.InventoryBalance_Snapshot_Daily
(
    Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity,
    IsValid, InvalidatedAt, InvalidReason, [Version]
)
SELECT @SnapshotDate, @Kho_ID, @San_Pham_ID, COALESCE(SUM(Quantity), 0), 1, NULL, NULL, 1
FROM Movement;

PRINT N'AFTER_SCOPED_INVALIDATE_QUEUE';
EXEC dbo.sp_Inventory_Snapshot_Invalidate_From
    @From_Date = @SnapshotDate,
    @Kho_ID = @Kho_ID,
    @San_Pham_ID = @San_Pham_ID,
    @InvalidReason = N'BACK_DATE_POST';

EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay = @FromDate,
    @Den_Ngay = @ToDate,
    @Page_Number = 1,
    @Page_Size = 10,
    @Ma_Dang_Nhap = N'PERF_USER';
SELECT N'TempdbAfterScopedInvalidate' AS Metric,
       SUM(internal_object_reserved_page_count) * 8 AS InternalObjectKB,
       SUM(user_object_reserved_page_count) * 8 AS UserObjectKB
FROM tempdb.sys.dm_db_file_space_usage;

PRINT N'AFTER_REBUILD_QUEUE_REPORT';
EXEC dbo.sp_Inventory_Snapshot_Process_RebuildQueue @Batch_Size = 100;

EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay = @FromDate,
    @Den_Ngay = @ToDate,
    @Page_Number = 1,
    @Page_Size = 10,
    @Ma_Dang_Nhap = N'PERF_USER';
SELECT N'TempdbAfterRebuild' AS Metric,
       SUM(internal_object_reserved_page_count) * 8 AS InternalObjectKB,
       SUM(user_object_reserved_page_count) * 8 AS UserObjectKB
FROM tempdb.sys.dm_db_file_space_usage;

SELECT N'LifecycleState' AS Metric, q.Status, q.From_Date, s.IsValid, s.[Version]
FROM dbo.InventorySnapshot_RebuildQueue q
JOIN dbo.InventoryBalance_Snapshot_Daily s
  ON s.Kho_ID = q.Kho_ID
 AND s.San_Pham_ID = q.San_Pham_ID
 AND s.Snapshot_Date = q.From_Date
WHERE q.Kho_ID = @Kho_ID
  AND q.San_Pham_ID = @San_Pham_ID
ORDER BY q.ID DESC;
