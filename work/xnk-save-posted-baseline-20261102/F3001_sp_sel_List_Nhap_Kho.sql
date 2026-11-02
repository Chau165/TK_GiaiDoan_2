
CREATE   PROCEDURE dbo.F3001_sp_sel_List_Nhap_Kho
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu_Nhap_Kho, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Nhap_Kho, h.Is_Posted, h.Ghi_Chu
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
    WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC;
END
