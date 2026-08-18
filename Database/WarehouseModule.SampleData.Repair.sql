/* Repairs the sample rows that were previously imported through an ANSI code page. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @SampleNote NVARCHAR(200) = N'D' + NCHAR(7919) + N' li' + NCHAR(7879) + N'u m' + NCHAR(7851);
DECLARE @DvtCai NVARCHAR(200) = N'C' + NCHAR(225) + N'i';
DECLARE @DvtHop NVARCHAR(200) = N'H' + NCHAR(7897) + N'p';
DECLARE @DvtChai NVARCHAR(200) = N'Chai';
DECLARE @LoaiVp NVARCHAR(200) = N'V' + NCHAR(259) + N'n ph' + NCHAR(242) + N'ng ph' + NCHAR(7849) + N'm';
DECLARE @LoaiTp NVARCHAR(200) = N'Th' + NCHAR(7921) + N'c ph' + NCHAR(7849) + N'm';
DECLARE @NccAnPhat NVARCHAR(255) = N'C' + NCHAR(244) + N'ng ty An Ph' + NCHAR(225) + N't';
DECLARE @NccMinhNhat NVARCHAR(255) = N'C' + NCHAR(244) + N'ng ty Minh Nh' + NCHAR(7853) + N't';
DECLARE @KhoTrungTam NVARCHAR(255) = N'Kho trung t' + NCHAR(226) + N'm';
DECLARE @KhoChiNhanh NVARCHAR(255) = N'Kho chi nh' + NCHAR(225) + N'nh';
DECLARE @ButBi NVARCHAR(255) = N'B' + NCHAR(250) + N't bi Thi' + NCHAR(234) + N'n Long';
DECLARE @VoTap NVARCHAR(255) = N'V' + NCHAR(7903) + N' k' + NCHAR(7867) + N' ngang 200 trang';
DECLARE @GiayA4 NVARCHAR(255) = N'Gi' + NCHAR(7845) + N'y in A4';
DECLARE @Nuoc NVARCHAR(255) = N'N' + NCHAR(432) + NCHAR(7899) + N'c su' + NCHAR(7889) + N'i 500ml';

UPDATE dbo.tbl_DM_Don_Vi_Tinh SET Ten_Don_Vi_Tinh = @DvtCai, Ghi_Chu = @SampleNote
WHERE Ten_Don_Vi_Tinh LIKE N'C' + NCHAR(258) + N'%';
UPDATE dbo.tbl_DM_Don_Vi_Tinh SET Ten_Don_Vi_Tinh = @DvtHop, Ghi_Chu = @SampleNote
WHERE Ten_Don_Vi_Tinh LIKE N'H' + NCHAR(225) + N'%';
UPDATE dbo.tbl_DM_Don_Vi_Tinh SET Ten_Don_Vi_Tinh = @DvtChai, Ghi_Chu = @SampleNote
WHERE Ten_Don_Vi_Tinh = @DvtChai;

UPDATE dbo.tbl_DM_Don_Vi_Tinh SET Ghi_Chu = @SampleNote
WHERE Ten_Don_Vi_Tinh IN (@DvtCai, @DvtHop, @DvtChai);

UPDATE dbo.tbl_DM_Loai_San_Pham SET Ten_LSP = @LoaiVp, Ghi_Chu = @SampleNote WHERE Ma_LSP = N'VP';
UPDATE dbo.tbl_DM_Loai_San_Pham SET Ten_LSP = @LoaiTp, Ghi_Chu = @SampleNote WHERE Ma_LSP = N'TP';
UPDATE dbo.tbl_DM_NCC SET Ten_NCC = @NccAnPhat, Ghi_Chu = @SampleNote WHERE Ma_NCC = N'NCC-ANPHAT';
UPDATE dbo.tbl_DM_NCC SET Ten_NCC = @NccMinhNhat, Ghi_Chu = @SampleNote WHERE Ma_NCC = N'NCC-MINHNHAT';
UPDATE dbo.tbl_DM_Kho SET Ten_Kho = @KhoTrungTam, Ghi_Chu = @SampleNote
WHERE Ten_Kho LIKE N'Kho trung t' + NCHAR(258) + N'%';
UPDATE dbo.tbl_DM_Kho SET Ten_Kho = @KhoChiNhanh, Ghi_Chu = @SampleNote
WHERE Ten_Kho LIKE N'Kho chi nh' + NCHAR(258) + N'%';
UPDATE dbo.tbl_DM_San_Pham SET Ten_San_Pham = @ButBi, Ghi_Chu = @SampleNote WHERE Ma_San_Pham = N'SP-BUTBI';
UPDATE dbo.tbl_DM_San_Pham SET Ten_San_Pham = @VoTap, Ghi_Chu = @SampleNote WHERE Ma_San_Pham = N'SP-VOTAP';
UPDATE dbo.tbl_DM_San_Pham SET Ten_San_Pham = @GiayA4, Ghi_Chu = @SampleNote WHERE Ma_San_Pham = N'SP-GIAY-A4';
UPDATE dbo.tbl_DM_San_Pham SET Ten_San_Pham = @Nuoc, Ghi_Chu = @SampleNote WHERE Ma_San_Pham = N'SP-NUOC';

UPDATE dbo.tbl_XNK_Nhap_Kho SET Ghi_Chu = N'T' + NCHAR(7891) + N'n ' + NCHAR(273) + N'ầu k' + NCHAR(7923) + N' m' + NCHAR(7851) WHERE So_Phieu_Nhap_Kho = N'SEED-PNK-0001';
UPDATE dbo.tbl_XNK_Nhap_Kho SET Ghi_Chu = N'Nh' + NCHAR(7853) + N'p kho m' + NCHAR(7851) + N'u th' + NCHAR(225) + N'ng 08' WHERE So_Phieu_Nhap_Kho = N'SEED-PNK-0002';
UPDATE dbo.tbl_XNK_Nhap_Kho SET Ghi_Chu = N'Nh' + NCHAR(7853) + N'p kho chi nh' + NCHAR(225) + N'nh m' + NCHAR(7851) WHERE So_Phieu_Nhap_Kho = N'SEED-PNK-0003';
UPDATE dbo.tbl_XNK_Xuat_Kho SET Ghi_Chu = N'Xu' + NCHAR(7845) + N't kho m' + NCHAR(7851) + N'u th' + NCHAR(225) + N'ng 08' WHERE So_Phieu_Xuat_Kho = N'SEED-PXK-0001';
UPDATE dbo.tbl_XNK_Xuat_Kho SET Ghi_Chu = N'Xu' + NCHAR(7845) + N't kho chi nh' + NCHAR(225) + N'nh m' + NCHAR(7851) WHERE So_Phieu_Xuat_Kho = N'SEED-PXK-0002';

DECLARE @Parent_ID BIGINT = 110000100;
DECLARE @Function_ID BIGINT = 110000101;
DECLARE @ParentNote NVARCHAR(200) = N'Module th' + NCHAR(7921) + N'c t' + NCHAR(7853) + N'p qu' + NCHAR(7843) + N'n l' + NCHAR(253) + N' kho';
DECLARE @ManageName NVARCHAR(200) = N'Qu' + NCHAR(7843) + N'n l' + NCHAR(253) + N' kho';
DECLARE @ManageNote NVARCHAR(200) = N'B' + NCHAR(224) + N'i t' + NCHAR(7853) + N'p th' + NCHAR(7921) + N'c t' + NCHAR(7853) + N'p giai ' + NCHAR(273) + N'o' + NCHAR(7841) + N'n 2';
UPDATE dbo.tbl_Sys_Chuc_Nang SET Ten_Chuc_Nang = N'Kho', Ghi_Chu = @ParentNote WHERE Auto_ID = @Parent_ID;
UPDATE dbo.tbl_Sys_Chuc_Nang SET Ten_Chuc_Nang = @ManageName, Ghi_Chu = @ManageNote WHERE Auto_ID = @Function_ID;

COMMIT TRANSACTION;
GO
