CREATE   PROCEDURE dbo.F2019_sp_upd_NCC
    @Auto_ID BIGINT OUTPUT, @Ma_NCC NVARCHAR(100), @Ten_NCC NVARCHAR(255), @Ghi_Chu NVARCHAR(1000)=NULL,
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
        SET @Ma_NCC=LTRIM(RTRIM(ISNULL(@Ma_NCC,N''))); SET @Ten_NCC=LTRIM(RTRIM(ISNULL(@Ten_NCC,N'')));
        IF @Ma_NCC=N'' THROW 51030,N'Mã nhà cung cấp không được để trống.',1; IF @Ten_NCC=N'' THROW 51032,N'Tên nhà cung cấp không được để trống.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.view_DM_NCC WHERE Auto_ID=@Auto_ID) THROW 51033,N'Nhà cung cấp không tồn tại hoặc đã bị xóa.',1;
        IF EXISTS(SELECT 1 FROM dbo.view_DM_NCC WHERE (Ma_NCC=@Ma_NCC OR Ten_NCC=@Ten_NCC) AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51031,N'Mã hoặc tên nhà cung cấp đã tồn tại.',1;
        UPDATE dbo.tbl_DM_NCC SET Ma_NCC=@Ma_NCC,Ten_NCC=@Ten_NCC,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
        SELECT @Auto_ID AS Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
