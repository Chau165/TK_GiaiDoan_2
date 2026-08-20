/*
  Run:
  sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseDetailUnitColumn.IntegrationTests.sql

  This test intentionally uses one transaction and always rolls it back.
  Guards the detail-view contract: sp_XNK_Document_Detail_List must return
  the product unit name (Ten_Don_Vi_Tinh) so the UI can render "60.00 (Chai)".
*/
SET NOCOUNT ON;
SET XACT_ABORT OFF;

BEGIN TRANSACTION;

BEGIN TRY
    /* The deployed stored procedure must contain the unit lookup. */
    IF OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_XNK_Document_Detail_List')) NOT LIKE N'%dv.Ten_Don_Vi_Tinh%'
        THROW 52101, N'Deployed sp_XNK_Document_Detail_List does not return Ten_Don_Vi_Tinh. Re-run Database\WarehouseModule.Procedures.sql.', 1;

    DECLARE @UnitId BIGINT = 0;
    DECLARE @CategoryId BIGINT = 0;
    DECLARE @ProductId BIGINT = 0;
    DECLARE @SupplierId BIGINT = 0;
    DECLARE @WarehouseId BIGINT = 0;
    DECLARE @ReceiptId BIGINT = 0;

    EXEC dbo.sp_DM_Don_Vi_Tinh_Save @Auto_ID = @UnitId OUTPUT, @Ten_Don_Vi_Tinh = N'UT-Chai', @Ghi_Chu = N'';
    EXEC dbo.sp_DM_Loai_San_Pham_Save @Auto_ID = @CategoryId OUTPUT, @Ma_LSP = N'UT-LSP-U', @Ten_LSP = N'UT Loai U', @Ghi_Chu = N'';
    EXEC dbo.sp_DM_San_Pham_Save @Auto_ID = @ProductId OUTPUT, @Ma_San_Pham = N'UT-SP-U', @Ten_San_Pham = N'UT San Pham U', @Loai_San_Pham_ID = @CategoryId, @Don_Vi_Tinh_ID = @UnitId, @Ghi_Chu = N'';
    EXEC dbo.sp_DM_NCC_Save @Auto_ID = @SupplierId OUTPUT, @Ma_NCC = N'UT-NCC-U', @Ten_NCC = N'UT Nha Cung Cap U', @Ghi_Chu = N'';
    EXEC dbo.sp_DM_Kho_Save @Auto_ID = @WarehouseId OUTPUT, @Ten_Kho = N'UT Kho U', @Ghi_Chu = N'';

    EXEC dbo.sp_XNK_Nhap_Kho_Save_Header @Auto_ID = @ReceiptId OUTPUT, @So_Phieu_Nhap_Kho = N'UT-PN-UNIT', @Kho_ID = @WarehouseId, @NCC_ID = @SupplierId, @Ngay_Nhap_Kho = '2026-03-01', @Ghi_Chu = N'';
    EXEC dbo.sp_XNK_Nhap_Kho_Save_Detail @Auto_ID = 0, @Nhap_Kho_ID = @ReceiptId, @San_Pham_ID = @ProductId, @SL_Nhap = 10, @Don_Gia_Nhap = 100;

    /* The detail list must expose the unit name next to the product. */
    DECLARE @DetailList TABLE (Auto_ID BIGINT, Document_ID BIGINT, San_Pham_ID BIGINT, Ma_San_Pham NVARCHAR(100), Ten_San_Pham NVARCHAR(255), Ten_Don_Vi_Tinh NVARCHAR(200), So_Luong DECIMAL(18, 3), Don_Gia DECIMAL(18, 2));
    INSERT INTO @DetailList EXEC dbo.sp_XNK_Document_Detail_List @Is_Receipt = 1, @Document_ID = @ReceiptId;

    IF NOT EXISTS (SELECT 1 FROM @DetailList WHERE Ten_Don_Vi_Tinh = N'UT-Chai')
        THROW 52102, N'sp_XNK_Document_Detail_List did not return the product unit name.', 1;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

ROLLBACK TRANSACTION;
