/*
  Run:
  sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehousePerformanceIndexes.IntegrationTests.sql

  Guards benchmark-driven performance indexes (2026). Fails on any database where the
  WarehouseModule.Schema.sql performance-index section was not deployed.
*/
SET NOCOUNT ON;
SET XACT_ABORT OFF;

BEGIN TRANSACTION;

BEGIN TRY
    DECLARE @Missing NVARCHAR(200) = N'';

    IF OBJECT_ID(N'dbo.tbl_XNK_Nhap_Kho_Raw_Data') IS NULL
        THROW 52201, N'tbl_XNK_Nhap_Kho_Raw_Data does not exist.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Raw_NhapKho_ID') SET @Missing = N'IX_tbl_XNK_Nhap_Kho_Raw_NhapKho_ID';
    ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Raw_XuatKho_ID') SET @Missing = N'IX_tbl_XNK_Xuat_Kho_Raw_XuatKho_ID';
    ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Ngay') SET @Missing = N'IX_tbl_XNK_Nhap_Kho_Ngay';
    ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Ngay') SET @Missing = N'IX_tbl_XNK_Xuat_Kho_Ngay';
    ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Raw_SanPham_ID') SET @Missing = N'IX_tbl_XNK_Nhap_Kho_Raw_SanPham_ID';
    ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Raw_SanPham_ID') SET @Missing = N'IX_tbl_XNK_Xuat_Kho_Raw_SanPham_ID';

    IF @Missing <> N''
    BEGIN
        DECLARE @Msg NVARCHAR(400) = N'Missing performance index: ' + @Missing + N'. Re-run Database\WarehouseModule.Schema.sql.';
        THROW 52202, @Msg, 1;
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

ROLLBACK TRANSACTION;