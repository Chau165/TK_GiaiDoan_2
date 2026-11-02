
CREATE   PROCEDURE dbo.sp_DM_Kho_User_Ensure_Access
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    IF @Kho_ID IS NULL OR @Kho_ID = 0 OR NOT EXISTS (SELECT 1 FROM dbo.view_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51051, N'Kho không hợp lệ.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.tbl_DM_Kho_User ku
        JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = ku.Ma_Dang_Nhap
        WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = @Kho_ID
    ) THROW 51054, N'User không có quyền thao tác trên kho này.', 1;
END
