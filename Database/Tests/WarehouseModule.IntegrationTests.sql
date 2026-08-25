/*
  Run:
  sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.IntegrationTests.sql

  This test intentionally uses one transaction and always rolls it back.
*/
SET NOCOUNT ON;
/* Expected validation errors are caught below, so this test transaction must remain usable. */
SET XACT_ABORT OFF;

BEGIN TRANSACTION;

BEGIN TRY
    DECLARE @UnitId BIGINT = 0;
    DECLARE @CategoryId BIGINT = 0;
    DECLARE @ProductId BIGINT = 0;
    DECLARE @SupplierId BIGINT = 0;
    DECLARE @WarehouseId BIGINT = 0;
    DECLARE @ReceiptId BIGINT = 0;
    DECLARE @PeriodReceiptId BIGINT = 0;
    DECLARE @IssueId BIGINT = 0;
    DECLARE @NegativeIssueId BIGINT = 0;

    /* Required name is rejected. */
    BEGIN TRY
        EXEC dbo.sp_DM_Don_Vi_Tinh_Save @Auto_ID = @UnitId OUTPUT, @Ten_Don_Vi_Tinh = N'', @Ghi_Chu = N'';
        THROW 52000, 'Expected empty unit name to be rejected.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (51001)
            THROW;
        IF ERROR_MESSAGE() <> N'Tên đơn vị tính không được để trống.'
            THROW 52004, 'Empty unit validation message is not Unicode-safe.', 1;
    END CATCH;

    EXEC dbo.sp_DM_Don_Vi_Tinh_Save @Auto_ID = @UnitId OUTPUT, @Ten_Don_Vi_Tinh = N'UT_DVT', @Ghi_Chu = N'';

    /* The unique business key is enforced by the stored procedure. */
    DECLARE @DuplicateUnitId BIGINT = 0;
    BEGIN TRY
        EXEC dbo.sp_DM_Don_Vi_Tinh_Save @Auto_ID = @DuplicateUnitId OUTPUT, @Ten_Don_Vi_Tinh = N'UT_DVT', @Ghi_Chu = N'';
        THROW 52001, 'Expected duplicate unit name to be rejected.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (51002)
            THROW;
        IF ERROR_MESSAGE() <> N'Tên đơn vị tính đã tồn tại.'
            THROW 52005, 'Duplicate unit validation message is not Unicode-safe.', 1;
    END CATCH;

    EXEC dbo.sp_DM_Loai_San_Pham_Save @Auto_ID = @CategoryId OUTPUT, @Ma_LSP = N'UT-LSP', @Ten_LSP = N'UT Loai', @Ghi_Chu = N'';
    EXEC dbo.sp_DM_San_Pham_Save @Auto_ID = @ProductId OUTPUT, @Ma_San_Pham = N'UT-SP', @Ten_San_Pham = N'UT San Pham', @Loai_San_Pham_ID = @CategoryId, @Don_Vi_Tinh_ID = @UnitId, @Ghi_Chu = N'';
    EXEC dbo.sp_DM_NCC_Save @Auto_ID = @SupplierId OUTPUT, @Ma_NCC = N'UT-NCC', @Ten_NCC = N'UT Nha Cung Cap', @Ghi_Chu = N'';
    EXEC dbo.sp_DM_Kho_Save @Auto_ID = @WarehouseId OUTPUT, @Ten_Kho = N'UT Kho', @Ghi_Chu = N'';

    EXEC dbo.sp_XNK_Nhap_Kho_Save_Header @Auto_ID = @ReceiptId OUTPUT, @So_Phieu_Nhap_Kho = N'UT-PN-OPEN', @Kho_ID = @WarehouseId, @NCC_ID = @SupplierId, @Ngay_Nhap_Kho = '2026-01-05', @Ghi_Chu = N'';
    EXEC dbo.sp_XNK_Nhap_Kho_Save_Detail @Auto_ID = 0, @Nhap_Kho_ID = @ReceiptId, @San_Pham_ID = @ProductId, @SL_Nhap = 10, @Don_Gia_Nhap = 100;

    EXEC dbo.sp_XNK_Nhap_Kho_Save_Header @Auto_ID = @PeriodReceiptId OUTPUT, @So_Phieu_Nhap_Kho = N'UT-PN-PERIOD', @Kho_ID = @WarehouseId, @NCC_ID = @SupplierId, @Ngay_Nhap_Kho = '2026-02-10', @Ghi_Chu = N'';
    EXEC dbo.sp_XNK_Nhap_Kho_Save_Detail @Auto_ID = 0, @Nhap_Kho_ID = @PeriodReceiptId, @San_Pham_ID = @ProductId, @SL_Nhap = 5, @Don_Gia_Nhap = 120;

    EXEC dbo.sp_XNK_Xuat_Kho_Save_Header @Auto_ID = @IssueId OUTPUT, @So_Phieu_Xuat_Kho = N'UT-PX-PERIOD', @Kho_ID = @WarehouseId, @Ngay_Xuat_Kho = '2026-02-15', @Ghi_Chu = N'';
    EXEC dbo.sp_XNK_Xuat_Kho_Save_Detail @Auto_ID = 0, @Xuat_Kho_ID = @IssueId, @San_Pham_ID = @ProductId, @SL_Xuat = 4, @Don_Gia_Xuat = 150;

    /* Opening = 10; period receipt = 5; period issue = 4; closing = 11. */
    DECLARE @Inventory TABLE (Kho_ID BIGINT, San_Pham_ID BIGINT, Ma_San_Pham NVARCHAR(100), Ten_San_Pham NVARCHAR(255), SL_Dau_Ky DECIMAL(18,3), SL_Nhap DECIMAL(18,3), SL_Xuat DECIMAL(18,3), SL_Cuoi_Ky DECIMAL(18,3));
    INSERT INTO @Inventory EXEC dbo.sp_BC_Xuat_Nhap_Ton @Tu_Ngay = '2026-02-01', @Den_Ngay = '2026-02-28';
    IF NOT EXISTS (SELECT 1 FROM @Inventory WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId AND SL_Dau_Ky = 10 AND SL_Nhap = 5 AND SL_Xuat = 4 AND SL_Cuoi_Ky = 11)
        THROW 52002, 'Inventory formula is incorrect.', 1;

    /* No movement is allowed to make the historical warehouse-product balance negative. */
    BEGIN TRY
        EXEC dbo.sp_XNK_Xuat_Kho_Save_Header @Auto_ID = @NegativeIssueId OUTPUT, @So_Phieu_Xuat_Kho = N'UT-PX-NEGATIVE', @Kho_ID = @WarehouseId, @Ngay_Xuat_Kho = '2026-02-20', @Ghi_Chu = N'';
        EXEC dbo.sp_XNK_Xuat_Kho_Save_Detail @Auto_ID = 0, @Xuat_Kho_ID = @NegativeIssueId, @San_Pham_ID = @ProductId, @SL_Xuat = 12, @Don_Gia_Xuat = 150;
        THROW 52003, 'Expected negative inventory to be rejected.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (51120)
            THROW;
    END CATCH;

    ROLLBACK TRANSACTION;
    PRINT 'PASS: Warehouse module integration tests';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
