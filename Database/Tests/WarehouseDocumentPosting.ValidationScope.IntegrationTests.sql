/*
  Run:
  sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseDocumentPosting.ValidationScope.IntegrationTests.sql

  This test intentionally uses one transaction and always rolls it back.
  It proves that posting a valid affected bucket is not blocked by an
  unrelated historical negative bucket.
*/
SET NOCOUNT ON;
SET XACT_ABORT OFF;

BEGIN TRANSACTION;

BEGIN TRY
    DECLARE @Tag NVARCHAR(100) = N'UT-VALIDATION-SCOPE-' + REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N'');
    DECLARE @UnitId BIGINT;
    DECLARE @CategoryId BIGINT;
    DECLARE @ProductId BIGINT;
    DECLARE @UnrelatedProductId BIGINT;
    DECLARE @SupplierId BIGINT;
    DECLARE @WarehouseId BIGINT;
    DECLARE @UnrelatedWarehouseId BIGINT;
    DECLARE @UnrelatedIssueId BIGINT;
    DECLARE @ReceiptId BIGINT;

    INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu)
    VALUES (@Tag + N'-UNIT', N'');
    SET @UnitId = SCOPE_IDENTITY();

    INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu)
    VALUES (@Tag + N'-CAT', @Tag + N'-CATEGORY', N'');
    SET @CategoryId = SCOPE_IDENTITY();

    INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu)
    VALUES (@Tag + N'-P1', @Tag + N'-PRODUCT-1', @CategoryId, @UnitId, N'');
    SET @ProductId = SCOPE_IDENTITY();

    INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu)
    VALUES (@Tag + N'-P2', @Tag + N'-PRODUCT-2', @CategoryId, @UnitId, N'');
    SET @UnrelatedProductId = SCOPE_IDENTITY();

    INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu)
    VALUES (@Tag + N'-SUPPLIER', @Tag + N'-SUPPLIER', N'');
    SET @SupplierId = SCOPE_IDENTITY();

    INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu)
    VALUES (@Tag + N'-WAREHOUSE', N'');
    SET @WarehouseId = SCOPE_IDENTITY();

    INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu)
    VALUES (@Tag + N'-UNRELATED-WAREHOUSE', N'');
    SET @UnrelatedWarehouseId = SCOPE_IDENTITY();

    /* Existing unrelated posted negative movement: another Kho/Product bucket. */
    INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu)
    VALUES (@Tag + N'-UNRELATED-ISSUE', @UnrelatedWarehouseId, '2026-01-01', 1, N'');
    SET @UnrelatedIssueId = SCOPE_IDENTITY();

    INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat)
    VALUES (@UnrelatedIssueId, @UnrelatedProductId, 100, 1);

    /* Valid draft receipt in the affected bucket. */
    INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu)
    VALUES (@Tag + N'-RECEIPT', @WarehouseId, @SupplierId, '2026-02-01', 0, N'');
    SET @ReceiptId = SCOPE_IDENTITY();

    INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
    VALUES (@ReceiptId, @ProductId, 10, 1);

    EXEC dbo.sp_XNK_Document_Post @Is_Receipt = 1, @Document_ID = @ReceiptId;

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId AND Is_Posted = 1)
        THROW 52010, 'A valid affected bucket was incorrectly rejected by unrelated history.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND CurrentQuantity = 10)
        THROW 52011, 'The affected balance was not updated after posting.', 1;

    ROLLBACK TRANSACTION;
    PRINT 'PASS: Validation is scoped to affected warehouse/product and date.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
