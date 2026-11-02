
CREATE   PROCEDURE dbo.F3002_sp_del_Xuat_Kho_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF;
    DECLARE @OwnTransaction BIT = 0, @SavepointCreated BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    ELSE IF XACT_STATE() = 1
    BEGIN
        SAVE TRANSACTION DeleteIssueHeader;
        SET @SavepointCreated = 1;
    END

    BEGIN TRY
        DECLARE @Candidate_Kho_ID BIGINT;
        SELECT @Candidate_Kho_ID = Kho_ID
        FROM dbo.tbl_XNK_Xuat_Kho
        WHERE Auto_ID = @Auto_ID;
        IF @Candidate_Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        INSERT @GroupSet(Kho_ID) VALUES (@Candidate_Kho_ID);
        INSERT @ScopeSet(Kho_ID, San_Pham_ID)
        SELECT r.Kho_ID, r.San_Pham_ID
        FROM dbo.InventoryReservation_Current r
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
        WHERE d.Xuat_Kho_ID = @Auto_ID;
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';
        EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set
            @GroupSet = @GroupSet,
            @Mode = N'Exclusive';
        EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope_Set
            @ScopeSet = @ScopeSet,
            @Mode = N'Exclusive';
        EXEC dbo.sp_Inventory_Fence_Require_Context
            @GroupSet = @GroupSet,
            @ScopeSet = @ScopeSet,
            @RequiredRootMode = N'Shared',
            @RequiredGroupMode = N'Exclusive',
            @RequiredScopeMode = N'Exclusive';

        DECLARE @Kho_ID BIGINT, @Is_Posted BIT;
        /* The parent lock covers validation, reservation release and cascade. */
        SELECT @Kho_ID = Kho_ID, @Is_Posted = Is_Posted
        FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK)
        WHERE Auto_ID = @Auto_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        IF @Kho_ID <> @Candidate_Kho_ID
            THROW 51331, N'Phiếu xuất đã thay đổi sau khi khóa Inventory Fence; vui lòng thử lại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Is_Posted = 1 THROW 51163, N'Không được xóa phiếu đã Post.', 1;

        EXEC dbo.sp_XNK_Reservation_Release_Document @Auto_ID;
        DELETE dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID;
        IF @@ROWCOUNT <> 1 THROW 51134, N'Phiếu xuất không còn tồn tại.', 1;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION DeleteIssueHeader;
        THROW;
    END CATCH
END
