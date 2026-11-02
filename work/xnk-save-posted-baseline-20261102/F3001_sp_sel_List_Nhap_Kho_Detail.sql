
CREATE   PROCEDURE dbo.F3001_sp_sel_List_Nhap_Kho_Detail
    @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT;
    SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID;
    IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT d.Auto_ID, d.Nhap_Kho_ID AS Nhap_Kho_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Nhap AS SL_Nhap, d.Don_Gia_Nhap AS Don_Gia_Nhap
    FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
    WHERE d.Nhap_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
END
