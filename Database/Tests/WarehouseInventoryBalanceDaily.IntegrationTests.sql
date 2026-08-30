/*
   TDD integration test for the precomputed Inventory_Balance_Daily report read model.

   RED before deployment:
   - dbo.Inventory_Balance_Daily and its scope/state tables do not exist.
   - dbo.sp_Inventory_Balance_Daily_Rebuild does not exist.
   - dbo.sp_BC_Xuat_Nhap_Ton_Page still reads Inventory_Movement_Daily and builds #MovementAggregate.

   Run after WarehouseModule.Schema.sql and WarehouseModule.Procedures.sql:
     sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventoryBalanceDaily.IntegrationTests.sql
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Inventory_Balance_Daily', N'U') IS NULL
    THROW 52500, N'RED: Inventory_Balance_Daily has not been deployed.', 1;
IF OBJECT_ID(N'dbo.Inventory_Balance_Daily_Scope', N'U') IS NULL
    THROW 52501, N'RED: Inventory_Balance_Daily_Scope has not been deployed.', 1;
IF OBJECT_ID(N'dbo.sp_Inventory_Balance_Daily_Rebuild', N'P') IS NULL
    THROW 52502, N'RED: Inventory_Balance_Daily worker procedure has not been deployed.', 1;

DECLARE @Tag NVARCHAR(20) = LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 20);
DECLARE @Login NVARCHAR(100) = CONCAT(N'__balance_daily_', @Tag);
DECLARE @UnauthorizedLogin NVARCHAR(100) = CONCAT(N'__balance_daily_no_access_', @Tag);
DECLARE @WarehouseId BIGINT;
DECLARE @ProductId BIGINT;
DECLARE @SupplierId BIGINT;
DECLARE @ReceiptId BIGINT;
DECLARE @IssueId BIGINT;
DECLARE @BackDateReceiptId BIGINT;
DECLARE @MemberId BIGINT;
DECLARE @UnauthorizedMemberId BIGINT;
DECLARE @ReceiptDate DATE = '2099-02-10';
DECLARE @BackDate DATE = '2099-02-11';
DECLARE @IssueDate DATE = '2099-02-12';
DECLARE @FirstBalanceVersion INT;
DECLARE @ErrorNumber INT;

BEGIN TRY
    SELECT TOP (1) @ProductId = Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;
    SELECT TOP (1) @SupplierId = Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;
    IF @ProductId IS NULL OR @SupplierId IS NULL
        THROW 52503, N'Balance daily test requires one product and one supplier.', 1;

    BEGIN TRANSACTION;

    INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu)
    VALUES (CONCAT(N'__balance_daily_wh_', @Tag), N'Inventory balance daily integration test');
    SET @WarehouseId = SCOPE_IDENTITY();

    SELECT @MemberId = ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);
    INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted)
    VALUES (@MemberId, @Login, N'Inventory balance daily integration test', 0);
    INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);

    SELECT @UnauthorizedMemberId = @MemberId + 1;
    INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted)
    VALUES (@UnauthorizedMemberId, @UnauthorizedLogin, N'Inventory balance daily no access', 0);

    INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity)
    VALUES (@WarehouseId, @ProductId, 0, 0);
    INSERT dbo.InventoryBalance_Snapshot_Daily
    (Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version])
    VALUES ('2099-01-31', @WarehouseId, @ProductId, 0, 1, 1);

    /* The production bootstrap is an explicit controlled operation.  This test
       scopes state only to its rollback transaction after seeding its own row. */
    UPDATE dbo.InventoryBalance_Daily_AggregateState
    SET IsInitialized = 1, InitializedAt = SYSUTCDATETIME(), LastReconciledAt = SYSUTCDATETIME()
    WHERE State_ID = 1;

    /* 1. Post receipt -> movement daily and balance daily are both materialized by the worker. */
    INSERT dbo.tbl_XNK_Nhap_Kho
    (So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu)
    VALUES (CONCAT(N'__balance_receipt_', @Tag), @WarehouseId, @SupplierId, @ReceiptDate, 0, N'Balance daily integration test');
    SET @ReceiptId = SCOPE_IDENTITY();
    INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
    VALUES (@ReceiptId, @ProductId, 100, 1);
    EXEC dbo.sp_XNK_Document_Post @Is_Receipt = 1, @Document_ID = @ReceiptId, @Ma_Dang_Nhap = @Login;
    EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size = 10;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Inventory_Movement_Daily
        WHERE Movement_Date = @ReceiptDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
          AND Total_Receipt = 100 AND Total_Issue = 0 AND IsValid = 1
    )
        THROW 52504, N'Receipt was not materialized in Inventory_Movement_Daily.', 1;

    SELECT @FirstBalanceVersion = [Version]
    FROM dbo.Inventory_Balance_Daily
    WHERE Balance_Date = @ReceiptDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
      AND OpeningQuantity = 0 AND TotalReceived = 100 AND TotalIssued = 0
      AND ClosingQuantity = 100 AND CumulativeReceived = 100 AND CumulativeIssued = 0 AND IsValid = 1;
    IF @FirstBalanceVersion IS NULL
        THROW 52505, N'Receipt was not materialized as the expected balance daily row.', 1;

    /* 2. Post issue -> closing balance decreases while cumulative movement remains correct. */
    INSERT dbo.tbl_XNK_Xuat_Kho
    (So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu)
    VALUES (CONCAT(N'__balance_issue_', @Tag), @WarehouseId, @IssueDate, 0, N'Balance daily integration test');
    SET @IssueId = SCOPE_IDENTITY();
    INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat)
    VALUES (@IssueId, @ProductId, 25, 1);
    EXEC dbo.sp_XNK_Document_Post @Is_Receipt = 0, @Document_ID = @IssueId, @Ma_Dang_Nhap = @Login;
    EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size = 10;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Inventory_Balance_Daily
        WHERE Balance_Date = @IssueDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
          AND OpeningQuantity = 100 AND TotalReceived = 0 AND TotalIssued = 25
          AND ClosingQuantity = 75 AND CumulativeReceived = 100 AND CumulativeIssued = 25 AND IsValid = 1
    )
        THROW 52506, N'Issue did not reduce the materialized daily balance.', 1;

    /* 3. Back-date rebuilds only this scope from the affected date forward. */
    INSERT dbo.tbl_XNK_Nhap_Kho
    (So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu)
    VALUES (CONCAT(N'__balance_backdate_', @Tag), @WarehouseId, @SupplierId, @BackDate, 0, N'Balance daily integration test');
    SET @BackDateReceiptId = SCOPE_IDENTITY();
    INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
    VALUES (@BackDateReceiptId, @ProductId, 5, 1);
    EXEC dbo.sp_XNK_Document_Post @Is_Receipt = 1, @Document_ID = @BackDateReceiptId, @Ma_Dang_Nhap = @Login;
    EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size = 10;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Inventory_Balance_Daily
        WHERE Balance_Date = @ReceiptDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
          AND [Version] = @FirstBalanceVersion AND ClosingQuantity = 100
    )
        THROW 52507, N'Back-date rebuilt a balance row before the affected date.', 1;
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Inventory_Balance_Daily
        WHERE Balance_Date = @BackDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
          AND OpeningQuantity = 100 AND TotalReceived = 5 AND TotalIssued = 0
          AND ClosingQuantity = 105 AND CumulativeReceived = 105 AND CumulativeIssued = 0 AND IsValid = 1
    )
        THROW 52508, N'Back-date balance row was not rebuilt from the affected date.', 1;
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Inventory_Balance_Daily
        WHERE Balance_Date = @IssueDate AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId
          AND OpeningQuantity = 105 AND TotalReceived = 0 AND TotalIssued = 25
          AND ClosingQuantity = 80 AND CumulativeReceived = 105 AND CumulativeIssued = 25 AND IsValid = 1
    )
        THROW 52509, N'Back-date did not rebuild later balance dates in the same scope.', 1;

    /* 4. Permission scope is still enforced by the report contract. */
    SET @ErrorNumber = NULL;
    BEGIN TRY
        EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page
            @Tu_Ngay = '2099-02-01', @Den_Ngay = '2099-02-28',
            @Page_Number = 1, @Page_Size = 10,
            @Ma_Dang_Nhap = @UnauthorizedLogin, @Kho_ID = @WarehouseId;
    END TRY
    BEGIN CATCH
        SET @ErrorNumber = ERROR_NUMBER();
    END CATCH;
    IF @ErrorNumber <> 51054
        THROW 52510, N'Report did not reject an explicit warehouse outside the user permission scope.', 1;

    EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page
        @Tu_Ngay = '2099-02-01', @Den_Ngay = '2099-02-28',
        @Page_Number = 1, @Page_Size = 10,
        @Ma_Dang_Nhap = @Login, @Kho_ID = @WarehouseId;

    /* 5. The report must only read materialized balance data; no movement scan,
       SUM/GROUP BY or #MovementAggregate may return to the request path. */
    DECLARE @PageDefinition NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_BC_Xuat_Nhap_Ton_Page'));
    IF CHARINDEX(N'Inventory_Balance_Daily', @PageDefinition) = 0
       OR CHARINDEX(N'Inventory_Movement_Daily', @PageDefinition) > 0
       OR CHARINDEX(N'#MovementAggregate', @PageDefinition) > 0
       OR CHARINDEX(N'GROUP BY', @PageDefinition) > 0
       OR CHARINDEX(N'SUM(', @PageDefinition) > 0
        THROW 52511, N'InventoryReportPaged still aggregates movement rows in the request path.', 1;

    ROLLBACK TRANSACTION;
    SELECT N'PASS: Inventory_Balance_Daily receipt, issue, back-date, permission and no-request-aggregate contracts.' AS Result;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
