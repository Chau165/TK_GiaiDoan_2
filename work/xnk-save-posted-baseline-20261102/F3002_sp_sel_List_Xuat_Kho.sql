
CREATE   PROCEDURE dbo.F3002_sp_sel_List_Xuat_Kho
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu_Xuat_Kho, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Xuat_Kho, h.Is_Posted, h.Ghi_Chu
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
    WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC;
END
