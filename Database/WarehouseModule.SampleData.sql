/*
   Sample data for the warehouse assignment.
   Safe to run repeatedly: each row is inserted only when its business key is absent.
   The dates intentionally include an opening balance before August 2026 and
   movements during August 2026, so the default report range can demonstrate
   opening, receipt, issue, and closing quantities.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh = N'Cái')
    INSERT dbo.tbl_DM_Don_Vi_Tinh (Ten_Don_Vi_Tinh, Ghi_Chu) VALUES (N'Cái', N'Dữ liệu mẫu');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh = N'Hộp')
    INSERT dbo.tbl_DM_Don_Vi_Tinh (Ten_Don_Vi_Tinh, Ghi_Chu) VALUES (N'Hộp', N'Dữ liệu mẫu');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh = N'Chai')
    INSERT dbo.tbl_DM_Don_Vi_Tinh (Ten_Don_Vi_Tinh, Ghi_Chu) VALUES (N'Chai', N'Dữ liệu mẫu');

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE Ma_LSP = N'VP')
    INSERT dbo.tbl_DM_Loai_San_Pham (Ma_LSP, Ten_LSP, Ghi_Chu) VALUES (N'VP', N'Văn phòng phẩm', N'Dữ liệu mẫu');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE Ma_LSP = N'TP')
    INSERT dbo.tbl_DM_Loai_San_Pham (Ma_LSP, Ten_LSP, Ghi_Chu) VALUES (N'TP', N'Thực phẩm', N'Dữ liệu mẫu');

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Ma_NCC = N'NCC-ANPHAT')
    INSERT dbo.tbl_DM_NCC (Ma_NCC, Ten_NCC, Ghi_Chu) VALUES (N'NCC-ANPHAT', N'Công ty An Phát', N'Dữ liệu mẫu');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Ma_NCC = N'NCC-MINHNHAT')
    INSERT dbo.tbl_DM_NCC (Ma_NCC, Ten_NCC, Ghi_Chu) VALUES (N'NCC-MINHNHAT', N'Công ty Minh Nhật', N'Dữ liệu mẫu');

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Ten_Kho = N'Kho trung tâm')
    INSERT dbo.tbl_DM_Kho (Ten_Kho, Ghi_Chu) VALUES (N'Kho trung tâm', N'Dữ liệu mẫu');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Ten_Kho = N'Kho chi nhánh')
    INSERT dbo.tbl_DM_Kho (Ten_Kho, Ghi_Chu) VALUES (N'Kho chi nhánh', N'Dữ liệu mẫu');

DECLARE @DvtCai BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh = N'Cái');
DECLARE @DvtHop BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh = N'Hộp');
DECLARE @DvtChai BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh = N'Chai');
DECLARE @LoaiVp BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_Loai_San_Pham WHERE Ma_LSP = N'VP');
DECLARE @LoaiTp BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_Loai_San_Pham WHERE Ma_LSP = N'TP');

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham = N'SP-BUTBI')
    INSERT dbo.tbl_DM_San_Pham (Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) VALUES (N'SP-BUTBI', N'Bút bi Thiên Long', @LoaiVp, @DvtCai, N'Dữ liệu mẫu');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham = N'SP-VOTAP')
    INSERT dbo.tbl_DM_San_Pham (Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) VALUES (N'SP-VOTAP', N'Vở kẻ ngang 200 trang', @LoaiVp, @DvtCai, N'Dữ liệu mẫu');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham = N'SP-GIAY-A4')
    INSERT dbo.tbl_DM_San_Pham (Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) VALUES (N'SP-GIAY-A4', N'Giấy in A4', @LoaiVp, @DvtHop, N'Dữ liệu mẫu');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham = N'SP-NUOC')
    INSERT dbo.tbl_DM_San_Pham (Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) VALUES (N'SP-NUOC', N'Nước suối 500ml', @LoaiTp, @DvtChai, N'Dữ liệu mẫu');

DECLARE @KhoTrungTam BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_Kho WHERE Ten_Kho = N'Kho trung tâm');
DECLARE @KhoChiNhanh BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_Kho WHERE Ten_Kho = N'Kho chi nhánh');
DECLARE @NccAnPhat BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_NCC WHERE Ma_NCC = N'NCC-ANPHAT');
DECLARE @NccMinhNhat BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_NCC WHERE Ma_NCC = N'NCC-MINHNHAT');
DECLARE @ButBi BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham = N'SP-BUTBI');
DECLARE @VoTap BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham = N'SP-VOTAP');
DECLARE @GiayA4 BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham = N'SP-GIAY-A4');
DECLARE @Nuoc BIGINT = (SELECT Auto_ID FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham = N'SP-NUOC');

IF EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = N'thuctap_kho')
   AND NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = N'thuctap_kho' AND Kho_ID = @KhoTrungTam)
    INSERT dbo.tbl_DM_Kho_User (Ma_Dang_Nhap, Kho_ID) VALUES (N'thuctap_kho', @KhoTrungTam);

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = N'SEED-PNK-0001')
    INSERT dbo.tbl_XNK_Nhap_Kho (So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Ghi_Chu) VALUES (N'SEED-PNK-0001', @KhoTrungTam, @NccAnPhat, '20260725', N'Tồn đầu kỳ mẫu');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = N'SEED-PNK-0002')
    INSERT dbo.tbl_XNK_Nhap_Kho (So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Ghi_Chu) VALUES (N'SEED-PNK-0002', @KhoTrungTam, @NccMinhNhat, '20260805', N'Nhập kho mẫu tháng 08');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = N'SEED-PNK-0003')
    INSERT dbo.tbl_XNK_Nhap_Kho (So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Ghi_Chu) VALUES (N'SEED-PNK-0003', @KhoChiNhanh, @NccAnPhat, '20260808', N'Nhập kho chi nhánh mẫu');

DECLARE @Pnk1 BIGINT = (SELECT Auto_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = N'SEED-PNK-0001');
DECLARE @Pnk2 BIGINT = (SELECT Auto_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = N'SEED-PNK-0002');
DECLARE @Pnk3 BIGINT = (SELECT Auto_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = N'SEED-PNK-0003');

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Pnk1 AND San_Pham_ID = @ButBi) INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data VALUES (@Pnk1, @ButBi, 100, 4500);
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Pnk1 AND San_Pham_ID = @VoTap) INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data VALUES (@Pnk1, @VoTap, 80, 12000);
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Pnk1 AND San_Pham_ID = @GiayA4) INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data VALUES (@Pnk1, @GiayA4, 30, 65000);
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Pnk2 AND San_Pham_ID = @ButBi) INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data VALUES (@Pnk2, @ButBi, 50, 4700);
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Pnk2 AND San_Pham_ID = @Nuoc) INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data VALUES (@Pnk2, @Nuoc, 120, 6000);
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Pnk3 AND San_Pham_ID = @Nuoc) INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data VALUES (@Pnk3, @Nuoc, 60, 6200);

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = N'SEED-PXK-0001')
    INSERT dbo.tbl_XNK_Xuat_Kho (So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Ghi_Chu) VALUES (N'SEED-PXK-0001', @KhoTrungTam, '20260810', N'Xuất kho mẫu tháng 08');
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = N'SEED-PXK-0002')
    INSERT dbo.tbl_XNK_Xuat_Kho (So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Ghi_Chu) VALUES (N'SEED-PXK-0002', @KhoChiNhanh, '20260812', N'Xuất kho chi nhánh mẫu');

DECLARE @Pxk1 BIGINT = (SELECT Auto_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = N'SEED-PXK-0001');
DECLARE @Pxk2 BIGINT = (SELECT Auto_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = N'SEED-PXK-0002');

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Pxk1 AND San_Pham_ID = @ButBi) INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data VALUES (@Pxk1, @ButBi, 30, 5500);
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Pxk1 AND San_Pham_ID = @VoTap) INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data VALUES (@Pxk1, @VoTap, 20, 15000);
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Pxk2 AND San_Pham_ID = @Nuoc) INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data VALUES (@Pxk2, @Nuoc, 15, 7500);

EXEC dbo.sp_XNK_Validate_All_Balances;

COMMIT TRANSACTION;
GO
