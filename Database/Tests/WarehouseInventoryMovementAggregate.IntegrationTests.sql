/*
   TDD integration test for the daily movement aggregate lifecycle.

   RED before deployment:
   - dbo.Inventory_Movement_Daily and dbo.InventoryMovement_RebuildQueue do not exist.
   - posting does not enqueue a movement rebuild.
   - dbo.sp_Inventory_Movement_Process_RebuildQueue does not exist.

   Run after WarehouseModule.Schema.sql and WarehouseModule.Procedures.sql:
     sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventoryMovementAggregate.IntegrationTests.sql
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

DECLARE @Tag NVARCHAR(20) = LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 20);
DECLARE @Login NVARCHAR(100) = CONCAT(N'__movement_aggregate_', @Tag);
DECLARE @WarehouseId BIGINT;
DECLARE @ProductId BIGINT;
DECLARE @SupplierId BIGINT;
DECLARE @ReceiptId BIGINT;
DECLARE @IssueId BIGINT;
DECLARE @BackDateReceiptId BIGINT;
DECLARE @RollbackReceiptId BIGINT;
DECLARE @MemberId BIGINT;
DECLARE @ReportFrom DATE = '2099-02-01';
DECLARE @ReceiptDate DATE = '2099-02-10';
DECLARE @IssueDate DATE = '2099-02-12';
DECLARE @Received DECIMAL(18,3);
DECLARE @Issued DECIMAL(18,3);
DECLARE @Opening DECIMAL(18,3);
DECLARE @Closing DECIMAL(18,3);
DECLARE @VersionAfterFirstRebuild INT;

BEGIN TRY
    SELECT TOP (1) @ProductId = Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;
    SELECT TOP (1) @SupplierId = Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;

    IF @ProductId IS NULL OR @SupplierId IS NULL
        THROW 52400, N'Movement aggregate test requires one product and one supplier.', 1;

    BEGIN TRANSACTION;

    INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu)
    VALUES (CONCAT(N'__movement_aggregate_wh_', @Tag), N'Movement aggregate integration test');
    SET @WarehouseId = SCOPE_IDENTITY();

    SELECT @MemberId = ISNULL(MAX(Auto_ID), 0) + 1
    FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);
    INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted)
    VALUES (@MemberId, @Login, N'Movement aggregate integration test', 0);

    INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID)
    VALUES (@Login, @WarehouseId);

    INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity)
    VALUES (@WarehouseId, @ProductId, 0, 0);

    INSERT dbo.InventoryBalance_Snapshot_Daily
    (Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version])
    VALUES ('2099-01-31', @WarehouseId, @ProductId, 0, 1, 1);

    /* Post receipt: only a small invalidation/queue write occurs in the Post transaction. */
    INSERT dbo.tbl_XNK_Nhap_Kho
    (So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu)
    VALUES (CONCAT(N'__movement_receipt_', @Tag), @WarehouseId, @SupplierId, @ReceiptDate, 0, N'Movement aggregate integration test');
    SET @ReceiptId = SCOPE_IDENTITY();

    INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
    VALUES (@ReceiptId, @ProductId, 100, 1);

    EXEC dbo.sp_XNK_Document_Post @Is_Receipt = 1, @Document_ID = @ReceiptId, @Ma_Dang_Nhap = @Login;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.InventoryMovement_RebuildQueue
        WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
          AND From_Date = @ReceiptDate AND To_Date = @ReceiptDate AND Status = N'WAITING'
    )
        THROW 52401, N'Posted receipt did not enqueue its daily movement rebuild.', 1;

    EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size = 10;

    SELECT @Received = Total_Receipt, @Issued = Total_Issue, @VersionAfterFirstRebuild = [Version]
    FROM dbo.Inventory_Movement_Daily
    WHERE Movement_Date = @ReceiptDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;

    IF @Received <> 100 OR @Issued <> 0
        THROW 52402, N'Receipt rebuild did not materialize the expected daily movement.', 1;

    /* Post issue and verify the independent issue aggregate. */
    INSERT dbo.tbl_XNK_Xuat_Kho
    (So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu)
    VALUES (CONCAT(N'__movement_issue_', @Tag), @WarehouseId, @IssueDate, 0, N'Movement aggregate integration test');
    SET @IssueId = SCOPE_IDENTITY();

    INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat)
    VALUES (@IssueId, @ProductId, 25, 1);

    EXEC dbo.sp_XNK_Document_Post @Is_Receipt = 0, @Document_ID = @IssueId, @Ma_Dang_Nhap = @Login;
    EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size = 10;

    SELECT @Received = Total_Receipt, @Issued = Total_Issue
    FROM dbo.Inventory_Movement_Daily
    WHERE Movement_Date = @IssueDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND IsValid = 1;

    IF @Received <> 0 OR @Issued <> 25
        THROW 52403, N'Issue rebuild did not materialize the expected daily movement.', 1;

    /* A second back-dated receipt invalidates only its own daily row, then rebuilds it. */
    INSERT dbo.tbl_XNK_Nhap_Kho
    (So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu)
    VALUES (CONCAT(N'__movement_backdate_', @Tag), @WarehouseId, @SupplierId, @ReceiptDate, 0, N'Movement aggregate integration test');
    SET @BackDateReceiptId = SCOPE_IDENTITY();

    INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
    VALUES (@BackDateReceiptId, @ProductId, 5, 1);

    EXEC dbo.sp_XNK_Document_Post @Is_Receipt = 1, @Document_ID = @BackDateReceiptId, @Ma_Dang_Nhap = @Login;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.Inventory_Movement_Daily
        WHERE Movement_Date = @ReceiptDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
          AND IsValid = 0 AND InvalidatedAt IS NOT NULL
    )
        THROW 52404, N'Back-dated post did not invalidate the affected daily aggregate.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.InventoryMovement_RebuildQueue
        WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
          AND From_Date = @ReceiptDate AND To_Date = @ReceiptDate AND Status = N'WAITING'
    )
        THROW 52405, N'Back-dated post did not enqueue the affected daily aggregate.', 1;

    EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size = 10;

    SELECT @Received = Total_Receipt, @Issued = Total_Issue
    FROM dbo.Inventory_Movement_Daily
    WHERE Movement_Date = @ReceiptDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
      AND IsValid = 1 AND [Version] > @VersionAfterFirstRebuild;

    IF @Received <> 105 OR @Issued <> 0
        THROW 52406, N'Back-dated rebuild did not recompute the complete daily aggregate.', 1;

    /* The paged report must read aggregate rows and retain its two-result-set contract. */
    UPDATE dbo.InventoryMovement_AggregateState
    SET IsInitialized = 1, InitializedAt = SYSUTCDATETIME(), LastReconciledAt = SYSUTCDATETIME()
    WHERE State_ID = 1;

    DECLARE @PageDefinition NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_BC_Xuat_Nhap_Ton_Page'));
    IF CHARINDEX(N'Inventory_Movement_Daily', @PageDefinition) = 0
       OR CHARINDEX(N'tbl_XNK_Nhap_Kho_Raw_Data', @PageDefinition) > 0
       OR CHARINDEX(N'tbl_XNK_Xuat_Kho_Raw_Data', @PageDefinition) > 0
        THROW 52407, N'InventoryReportPaged still reads raw movement details instead of the daily aggregate.', 1;

    /* Execute the unchanged two-result-set paging contract. */
    EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page @Tu_Ngay = @ReportFrom, @Den_Ngay = '2099-02-28', @Page_Number = 1, @Page_Size = 10, @Ma_Dang_Nhap = @Login;

    /* The non-paged function and the paged procedure are both report contracts.
       The function is checked for the exact aggregate values here. */
    SELECT @Opening = SL_Dau_Ky, @Received = SL_Nhap, @Issued = SL_Xuat, @Closing = SL_Cuoi_Ky
    FROM dbo.fn_Inventory_Report_Snapshot(@ReportFrom, '2099-02-28', @Login, 0)
    WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;

    IF @Opening <> 0 OR @Received <> 105 OR @Issued <> 25 OR @Closing <> 80
        THROW 52408, N'Inventory report no longer matches the posted daily aggregate.', 1;

    /* Queue and invalidation writes must follow the caller transaction. */
    INSERT dbo.tbl_XNK_Nhap_Kho
    (So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu)
    VALUES (CONCAT(N'__movement_rollback_', @Tag), @WarehouseId, @SupplierId, '2099-02-13', 0, N'Movement aggregate integration test');
    SET @RollbackReceiptId = SCOPE_IDENTITY();
    INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
    VALUES (@RollbackReceiptId, @ProductId, 7, 1);

    SAVE TRANSACTION BeforeRollbackPost;
    EXEC dbo.sp_XNK_Document_Post @Is_Receipt = 1, @Document_ID = @RollbackReceiptId, @Ma_Dang_Nhap = @Login;
    ROLLBACK TRANSACTION BeforeRollbackPost;

    IF EXISTS
    (
        SELECT 1 FROM dbo.InventoryMovement_RebuildQueue
        WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
          AND From_Date = '2099-02-13' AND Status IN (N'WAITING', N'PROCESSING')
    )
        THROW 52409, N'Rollback of Post did not roll back the movement rebuild request.', 1;

    ROLLBACK TRANSACTION;
    SELECT N'PASS: movement aggregate post, issue, back-date lifecycle, report and rollback.' AS Result;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
