
CREATE   PROCEDURE dbo.F3002_sp_del_Xuat_Kho_Detail
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
        SAVE TRANSACTION DeleteIssueDetail;
        SET @SavepointCreated = 1;
    END

    BEGIN TRY
        DECLARE @Candidate_Document_ID BIGINT, @Candidate_Kho_ID BIGINT, @Candidate_San_Pham_ID BIGINT;
        SELECT @Candidate_Document_ID = Xuat_Kho_ID, @Candidate_San_Pham_ID = San_Pham_ID
        FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data
        WHERE Auto_ID = @Auto_ID;
        IF @Candidate_Document_ID IS NULL THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;
        SELECT @Candidate_Kho_ID = Kho_ID
        FROM dbo.tbl_XNK_Xuat_Kho
        WHERE Auto_ID = @Candidate_Document_ID;
        IF @Candidate_Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        DECLARE @Candidate_Reservation_Kho_ID BIGINT, @Candidate_Reservation_San_Pham_ID BIGINT;
        SELECT @Candidate_Reservation_Kho_ID = Kho_ID,
               @Candidate_Reservation_San_Pham_ID = San_Pham_ID
        FROM dbo.InventoryReservation_Current
        WHERE Xuat_Kho_Detail_ID = @Auto_ID;
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        INSERT @GroupSet(Kho_ID) VALUES (@Candidate_Kho_ID);
        INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@Candidate_Kho_ID, @Candidate_San_Pham_ID);
        IF @Candidate_Reservation_Kho_ID IS NOT NULL AND @Candidate_Reservation_Kho_ID > 0
        BEGIN
            INSERT @GroupSet(Kho_ID) VALUES (@Candidate_Reservation_Kho_ID);
            IF @Candidate_Reservation_San_Pham_ID IS NOT NULL AND @Candidate_Reservation_San_Pham_ID > 0
                INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@Candidate_Reservation_Kho_ID, @Candidate_Reservation_San_Pham_ID);
        END;
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

        DECLARE @Kho_ID BIGINT, @Document_ID BIGINT, @Is_Posted BIT;
        SELECT @Document_ID = Xuat_Kho_ID
        FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data
        WHERE Auto_ID = @Auto_ID;
        IF @Document_ID IS NULL THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;

        /* Match Save_Detail/Post/Save_Header: parent first, then detail. */
        SELECT @Kho_ID = Kho_ID, @Is_Posted = Is_Posted
        FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK)
        WHERE Auto_ID = @Document_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        IF @Document_ID <> @Candidate_Document_ID OR @Kho_ID <> @Candidate_Kho_ID
            THROW 51331, N'Chi tiết phiếu xuất đã thay đổi sau khi khóa Inventory Fence; vui lòng thử lại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Is_Posted = 1 THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WITH (UPDLOCK, HOLDLOCK)
            WHERE Auto_ID = @Auto_ID AND Xuat_Kho_ID = @Document_ID
        )
            THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;

        EXEC dbo.sp_XNK_Reservation_Release_Detail @Auto_ID;
        DELETE dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
        IF @@ROWCOUNT <> 1 THROW 51139, N'Chi tiết phiếu xuất không còn tồn tại.', 1;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION DeleteIssueDetail;
        THROW;
    END CATCH
END
