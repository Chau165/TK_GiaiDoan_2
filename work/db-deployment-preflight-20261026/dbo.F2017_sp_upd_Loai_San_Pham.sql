CREATE   PROCEDURE dbo.F2017_sp_upd_Loai_San_Pham
    @Auto_ID BIGINT OUTPUT, @Ma_LSP NVARCHAR(100), @Ten_LSP NVARCHAR(200), @Ghi_Chu NVARCHAR(1000)=NULL,
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
        SET @Ma_LSP=LTRIM(RTRIM(ISNULL(@Ma_LSP,N''))); SET @Ten_LSP=LTRIM(RTRIM(ISNULL(@Ten_LSP,N'')));
        IF @Ma_LSP=N'' THROW 51010,N'Mã loại sản phẩm không được để trống.',1;
        IF @Ten_LSP=N'' THROW 51012,N'Tên loại sản phẩm không được để trống.',1;
        IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE (Ma_LSP=@Ma_LSP OR Ten_LSP=@Ten_LSP) AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51011,N'Mã hoặc tên loại sản phẩm đã tồn tại.',1;
        UPDATE dbo.tbl_DM_Loai_San_Pham SET Ma_LSP=@Ma_LSP,Ten_LSP=@Ten_LSP,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
 SELECT @Auto_ID AS Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END