
CREATE   PROCEDURE dbo.F3001_sp_del_Nhap_Kho_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Auto_ID IS NULL OR @Auto_ID <= 0
        THROW 51447, N'Receipt detail identifier is invalid.', 1;
    DECLARE @Candidate_Kho_ID BIGINT, @Candidate_Document_ID BIGINT;
    SELECT @Candidate_Kho_ID = h.Kho_ID, @Candidate_Document_ID = h.Auto_ID
    FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d
    JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID
    WHERE d.Auto_ID = @Auto_ID;
    IF @Candidate_Kho_ID IS NULL THROW 51105, N'Chi tiết phiếu nhập không tồn tại.', 1;

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

        DECLARE @Kho_ID BIGINT, @Document_ID BIGINT, @Is_Posted BIT;
        SELECT @Kho_ID = h.Kho_ID, @Document_ID = h.Auto_ID, @Is_Posted = h.Is_Posted
        FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d WITH (UPDLOCK, HOLDLOCK)
        JOIN dbo.tbl_XNK_Nhap_Kho h WITH (UPDLOCK, HOLDLOCK) ON h.Auto_ID = d.Nhap_Kho_ID
        WHERE d.Auto_ID = @Auto_ID;
        IF @Kho_ID IS NULL THROW 51105, N'Chi tiết phiếu nhập không tồn tại.', 1;
        IF @Kho_ID <> @Candidate_Kho_ID OR @Document_ID <> @Candidate_Document_ID
            THROW 51331, N'Chi tiết phiếu nhập đã thay đổi sau khi khóa Inventory Fence; vui lòng thử lại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Is_Posted = 1 THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
        DELETE dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
