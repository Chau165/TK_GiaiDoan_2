/*
   TDD lifecycle test for invalidated daily inventory snapshots.

   RED on the pre-refactor database:
   - InventoryBalance_Snapshot_Daily has no lifecycle columns.
   - InventorySnapshot_RebuildQueue does not exist.
   - The old trigger deletes snapshots instead of invalidating and queueing them.

   Run:
     sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventorySnapshotLifecycle.IntegrationTests.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Tag NVARCHAR(40) = REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N'');
DECLARE @Login NVARCHAR(100) = CONCAT(N'__snapshot_lifecycle_', @Tag);
DECLARE @WarehouseId BIGINT;
DECLARE @OtherWarehouseId BIGINT;
DECLARE @ProductId BIGINT;
DECLARE @OtherProductId BIGINT;
DECLARE @SupplierId BIGINT;
DECLARE @ReceiptId BIGINT;
DECLARE @IssueId BIGINT;
DECLARE @RollbackReceiptId BIGINT;
DECLARE @Opening DECIMAL(18,3);
DECLARE @Closing DECIMAL(18,3);

BEGIN TRY
    SELECT TOP (1) @ProductId = Auto_ID
    FROM dbo.tbl_DM_San_Pham
    ORDER BY Auto_ID;

    SELECT TOP (1) @OtherProductId = Auto_ID
    FROM dbo.tbl_DM_San_Pham
    WHERE Auto_ID <> @ProductId
    ORDER BY Auto_ID;

    SELECT TOP (1) @SupplierId = Auto_ID
    FROM dbo.tbl_DM_NCC
    ORDER BY Auto_ID;

    IF @ProductId IS NULL OR @OtherProductId IS NULL OR @SupplierId IS NULL
        THROW 52300, N'Lifecycle test requires at least two products and one supplier.', 1;

    BEGIN TRANSACTION;

    INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu)
    VALUES (CONCAT(N'__snapshot_lifecycle_wh_a_', @Tag), N'Lifecycle integration test');
    SET @WarehouseId = SCOPE_IDENTITY();

    INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu)
    VALUES (CONCAT(N'__snapshot_lifecycle_wh_b_', @Tag), N'Lifecycle integration test');
    SET @OtherWarehouseId = SCOPE_IDENTITY();

    INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID)
    VALUES (@Login, @WarehouseId), (@Login, @OtherWarehouseId);

    INSERT dbo.InventoryBalance_Snapshot_Daily
    (
        Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity,
        IsValid, InvalidatedAt, InvalidReason, Version
    )
    VALUES
        ('2026-01-19', @WarehouseId, @ProductId, 100, 1, NULL, NULL, 1),
        ('2026-01-20', @WarehouseId, @ProductId, 999, 1, NULL, NULL, 1),
        ('2026-01-21', @WarehouseId, @ProductId, 999, 1, NULL, NULL, 1),
        ('2026-01-21', @OtherWarehouseId, @OtherProductId, 777, 1, NULL, NULL, 1);

    INSERT dbo.tbl_XNK_Nhap_Kho
    (
        So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu
    )
    VALUES (CONCAT(N'__snapshot_lifecycle_receipt_', @Tag), @WarehouseId, @SupplierId, '2026-01-20', 0, N'Lifecycle integration test');
    SET @ReceiptId = SCOPE_IDENTITY();

    INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data
    (Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
    VALUES (@ReceiptId, @ProductId, 5, 1);

    EXEC dbo.sp_XNK_Document_Post
        @Is_Receipt = 1,
        @Document_ID = @ReceiptId,
        @Ma_Dang_Nhap = @Login;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventoryBalance_Snapshot_Daily
        WHERE Kho_ID = @WarehouseId
          AND San_Pham_ID = @ProductId
          AND Snapshot_Date IN ('2026-01-20', '2026-01-21')
          AND IsValid = 0
          AND InvalidReason = N'BACK_DATE_POST'
          AND InvalidatedAt IS NOT NULL
    )
        THROW 52301, N'Back-date post did not invalidate the affected snapshots.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.InventoryBalance_Snapshot_Daily
        WHERE Kho_ID = @OtherWarehouseId
          AND San_Pham_ID = @OtherProductId
          AND Snapshot_Date = '2026-01-21'
          AND IsValid = 0
    )
        THROW 52302, N'Back-date post invalidated an unrelated warehouse/product scope.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventorySnapshot_RebuildQueue
        WHERE Kho_ID = @WarehouseId
          AND San_Pham_ID = @ProductId
          AND From_Date = '2026-01-20'
          AND Status = N'WAITING'
    )
        THROW 52303, N'Back-date post did not enqueue a rebuild request.', 1;

    SELECT @Opening = SL_Dau_Ky, @Closing = SL_Cuoi_Ky
    FROM dbo.fn_Inventory_Report_Snapshot('2026-01-22', '2026-01-22', @Login, 0)
    WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;

    IF @Opening <> 105 OR @Closing <> 105
        THROW 52304, N'Report read an invalid snapshot instead of falling back to the ledger.', 1;

    EXEC dbo.sp_Inventory_Snapshot_Process_RebuildQueue @Batch_Size = 10;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.InventoryBalance_Snapshot_Daily
        WHERE Kho_ID = @WarehouseId
          AND San_Pham_ID = @ProductId
          AND Snapshot_Date IN ('2026-01-20', '2026-01-21')
          AND (IsValid <> 1 OR ClosingQuantity <> 105 OR Version <= 1)
    )
        THROW 52305, N'Rebuild did not restore valid snapshot values and increment Version.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventorySnapshot_RebuildQueue
        WHERE Kho_ID = @WarehouseId
          AND San_Pham_ID = @ProductId
          AND From_Date = '2026-01-20'
          AND Status = N'COMPLETED'
          AND CompletedAt IS NOT NULL
    )
        THROW 52306, N'Rebuild worker did not complete the queued request.', 1;

    INSERT dbo.tbl_XNK_Nhap_Kho
    (
        So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu
    )
    VALUES (CONCAT(N'__snapshot_lifecycle_rollback_', @Tag), @WarehouseId, @SupplierId, '2026-01-20', 0, N'Lifecycle rollback test');
    SET @RollbackReceiptId = SCOPE_IDENTITY();

    INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data
    (Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
    VALUES (@RollbackReceiptId, @ProductId, 7, 1);

    SAVE TRANSACTION BeforeRollbackPost;

    EXEC dbo.sp_XNK_Document_Post
        @Is_Receipt = 1,
        @Document_ID = @RollbackReceiptId,
        @Ma_Dang_Nhap = @Login;

    ROLLBACK TRANSACTION BeforeRollbackPost;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.InventoryBalance_Snapshot_Daily
        WHERE Kho_ID = @WarehouseId
          AND San_Pham_ID = @ProductId
          AND Snapshot_Date >= '2026-01-20'
          AND IsValid = 0
    )
        THROW 52307, N'Rolling back the post did not restore snapshot validity.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.InventorySnapshot_RebuildQueue
        WHERE Kho_ID = @WarehouseId
          AND San_Pham_ID = @ProductId
          AND From_Date = '2026-01-20'
          AND Status IN (N'WAITING', N'PROCESSING')
    )
        THROW 52308, N'Rolling back the post did not roll back the rebuild queue write.', 1;

    ROLLBACK TRANSACTION;
    SELECT N'PASS: snapshot lifecycle invalidation, scoped queueing, rebuild, report fallback and rollback.' AS Result;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
