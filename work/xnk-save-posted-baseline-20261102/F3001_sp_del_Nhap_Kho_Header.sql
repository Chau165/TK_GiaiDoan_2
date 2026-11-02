
CREATE   PROCEDURE dbo.F3001_sp_del_Nhap_Kho_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Auto_ID IS NULL OR @Auto_ID <= 0
        THROW 51446, N'Receipt document identifier is invalid.', 1;
    DECLARE @Candidate_Kho_ID BIGINT;
    SELECT @Candidate_Kho_ID = Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID;
    IF @Candidate_Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;

    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    BEGIN TRY
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        INSERT @GroupSet(Kho_ID) VALUES (@Candidate_Kho_ID);
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';
        EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set
            @GroupSet = @GroupSet,
            @Mode = N'Exclusive';
        EXEC dbo.sp_Inventory_Fence_Require_Context
            @GroupSet = @GroupSet,
            @ScopeSet = @ScopeSet,
            @RequiredRootMode = N'Shared',
            @RequiredGroupMode = N'Exclusive',
            @RequiredScopeMode = N'Shared';

        DECLARE @Kho_ID BIGINT, @Is_Posted BIT;
        SELECT @Kho_ID = Kho_ID, @Is_Posted = Is_Posted
        FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK)
        WHERE Auto_ID = @Auto_ID;
        IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        IF @Kho_ID <> @Candidate_Kho_ID
            THROW 51331, N'Phiếu nhập đã thay đổi sau khi khóa Inventory Fence; vui lòng thử lại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Is_Posted = 1 THROW 51163, N'Không được xóa phiếu đã Post.', 1;
        DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
