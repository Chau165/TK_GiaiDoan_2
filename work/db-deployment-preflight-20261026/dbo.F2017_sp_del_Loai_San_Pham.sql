CREATE   PROCEDURE dbo.F2017_sp_del_Loai_San_Pham @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
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
        /* Warehouse/product identity changes can alter future scope
           derivation.  Treat all master deletes as root-exclusive
           maintenance, even when the selected entity is not inventory data. */
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive';

        DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID=@Auto_ID;

        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END