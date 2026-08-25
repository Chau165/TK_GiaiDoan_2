/*
  Run:
  sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseInventorySnapshot.IntegrationTests.sql

  The transaction is always rolled back.  A posting dated before an existing
  snapshot must invalidate that snapshot so historical reports cannot read a
  stale baseline.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT OFF;

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @Tag NVARCHAR(40) = N'TDD-SNAP-' + LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 20);
    DECLARE @UnitId BIGINT, @CategoryId BIGINT, @ProductId BIGINT, @SupplierId BIGINT, @WarehouseId BIGINT, @ReceiptId BIGINT;

    INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) VALUES (@Tag + N'-UNIT', N'');
    SET @UnitId = SCOPE_IDENTITY();
    INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) VALUES (@Tag + N'-CAT', @Tag + N'-CATEGORY', N'');
    SET @CategoryId = SCOPE_IDENTITY();
    INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu)
    VALUES (@Tag + N'-PRODUCT', @Tag + N'-PRODUCT', @CategoryId, @UnitId, N'');
    SET @ProductId = SCOPE_IDENTITY();
    INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) VALUES (@Tag + N'-SUPPLIER', @Tag + N'-SUPPLIER', N'');
    SET @SupplierId = SCOPE_IDENTITY();
    INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) VALUES (@Tag + N'-WAREHOUSE', N'');
    SET @WarehouseId = SCOPE_IDENTITY();

    INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity)
    VALUES ('2026-02-28', @WarehouseId, @ProductId, 10);
    INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu)
    VALUES (@Tag + N'-RECEIPT', @WarehouseId, @SupplierId, '2026-02-15', 0, N'');
    SET @ReceiptId = SCOPE_IDENTITY();

    INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
    VALUES (@ReceiptId, @ProductId, 1, 1);

    UPDATE dbo.tbl_XNK_Nhap_Kho SET Is_Posted = 1 WHERE Auto_ID = @ReceiptId;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventoryBalance_Snapshot_Daily
        WHERE Snapshot_Date = '2026-02-28'
          AND Kho_ID = @WarehouseId
          AND San_Pham_ID = @ProductId
          AND IsValid = 0
          AND InvalidReason = N'BACK_DATE_POST'
    )
        THROW 52020, 'Posting a back-dated document did not invalidate its later inventory snapshot.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventorySnapshot_RebuildQueue
        WHERE Kho_ID = @WarehouseId
          AND San_Pham_ID = @ProductId
          AND From_Date = '2026-02-15'
          AND Status = N'WAITING'
    )
        THROW 52021, 'Posting a back-dated document did not enqueue a snapshot rebuild.', 1;

    ROLLBACK TRANSACTION;
    PRINT 'PASS: back-dated posting invalidates later inventory snapshots.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
