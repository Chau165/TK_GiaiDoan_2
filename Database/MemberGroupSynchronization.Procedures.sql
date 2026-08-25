CREATE OR ALTER PROCEDURE [dbo].[FQ_526_NTVU_sp_del_Delete_By_ID]
    @Auto_ID bigint,
    @Last_Updated_By nvarchar(50),
    @Last_Updated_By_Function nvarchar(50)
WITH RECOMPILE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Ma_Dang_Nhap nvarchar(50);

    SELECT @Ma_Dang_Nhap = Ma_Dang_Nhap
    FROM dbo.tbl_Sys_Nhom_Thanh_Vien_User
    WHERE Auto_ID = @Auto_ID;

    UPDATE dbo.tbl_Sys_Nhom_Thanh_Vien_User
    SET
        deleted = 1,
        Last_Updated = GETDATE(),
        Last_Updated_By = @Last_Updated_By,
        Last_Updated_By_Function = @Last_Updated_By_Function
    WHERE Auto_ID = @Auto_ID;

    IF @Ma_Dang_Nhap IS NOT NULL
        EXEC dbo.FTotal_sp_upd_Thanh_Vien @Ma_Dang_Nhap;
END
