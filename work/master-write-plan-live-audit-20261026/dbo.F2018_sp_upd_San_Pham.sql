
CREATE   PROCEDURE dbo.F2018_sp_upd_San_Pham
    @Auto_ID BIGINT OUTPUT, @Ma_San_Pham NVARCHAR(100), @Ten_San_Pham NVARCHAR(255), @Loai_San_Pham_ID BIGINT, @Don_Vi_Tinh_ID BIGINT, @Ghi_Chu NVARCHAR(1000)=NULL,
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
        SET @Ma_San_Pham=LTRIM(RTRIM(ISNULL(@Ma_San_Pham,N''))); SET @Ten_San_Pham=LTRIM(RTRIM(ISNULL(@Ten_San_Pham,N'')));
        IF @Ma_San_Pham=N'' THROW 51020,N'Mã sản phẩm không được để trống.',1; IF @Ten_San_Pham=N'' THROW 51022,N'Tên sản phẩm không được để trống.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.view_DM_San_Pham WHERE Auto_ID=@Auto_ID) THROW 51025,N'Sản phẩm không tồn tại hoặc đã bị xóa.',1;
        IF EXISTS(SELECT 1 FROM dbo.view_DM_San_Pham WHERE Ma_San_Pham=@Ma_San_Pham AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51021,N'Mã sản phẩm đã tồn tại.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.view_DM_Loai_San_Pham WHERE Auto_ID=@Loai_San_Pham_ID) THROW 51023,N'Loại sản phẩm không hợp lệ.',1; IF NOT EXISTS(SELECT 1 FROM dbo.view_DM_Don_Vi_Tinh WHERE Auto_ID=@Don_Vi_Tinh_ID) THROW 51024,N'Đơn vị tính không hợp lệ.',1;
        UPDATE dbo.tbl_DM_San_Pham SET Ma_San_Pham=@Ma_San_Pham,Ten_San_Pham=@Ten_San_Pham,Loai_San_Pham_ID=@Loai_San_Pham_ID,Don_Vi_Tinh_ID=@Don_Vi_Tinh_ID,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
 SELECT @Auto_ID AS Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
