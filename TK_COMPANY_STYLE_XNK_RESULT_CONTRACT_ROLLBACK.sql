-- Rollback for the XNK typed result-column contract only.
-- Verify the target and current procedure definitions before execution.
-- Expected database: TKS_Thuc_Tap_V11_GiaiDoan2.
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
    THROW 51000, N'Wrong database for XNK result-contract rollback.', 1;
GO

CREATE OR ALTER PROCEDURE dbo.F3001_sp_sel_List_Nhap_Kho
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
    WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.F3001_sp_sel_List_Nhap_Kho_Detail
    @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT;
    SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID;
    IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT d.Auto_ID, d.Nhap_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia
    FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
    WHERE d.Nhap_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.F3002_sp_sel_List_Xuat_Kho
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
    WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.F3002_sp_sel_List_Xuat_Kho_Detail
    @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT;
    SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID;
    IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT d.Auto_ID, d.Xuat_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia
    FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
    WHERE d.Xuat_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
END
GO
