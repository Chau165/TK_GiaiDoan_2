
CREATE   PROCEDURE dbo.F2009_sp_upd_Kho
    @Auto_ID BIGINT OUTPUT, @Ten_Kho NVARCHAR(255), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END;
    BEGIN TRY
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive';
        SET @Ten_Kho=LTRIM(RTRIM(ISNULL(@Ten_Kho,N'')));
        IF @Ten_Kho=N'' THROW 51040,N'Tên kho không được để trống.',1; IF EXISTS(SELECT 1 FROM dbo.view_DM_Kho WHERE Ten_Kho=@Ten_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51041,N'Tên kho đã tồn tại.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.view_DM_Kho WHERE Auto_ID=@Auto_ID) THROW 51042,N'Kho không tồn tại hoặc đã bị xóa.',1;
        UPDATE dbo.tbl_DM_Kho SET Ten_Kho=@Ten_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
 SELECT @Auto_ID AS Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
