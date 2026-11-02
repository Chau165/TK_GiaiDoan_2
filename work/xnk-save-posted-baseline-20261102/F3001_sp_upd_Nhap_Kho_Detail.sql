CREATE   PROCEDURE dbo.F3001_sp_upd_Nhap_Kho_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
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
        SAVE TRANSACTION SaveReceiptDetail;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        DECLARE @Candidate_Kho_ID BIGINT;
        SELECT @Candidate_Kho_ID = Kho_ID
        FROM dbo.tbl_XNK_Nhap_Kho
        WHERE Auto_ID = @Nhap_Kho_ID;
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        IF @Candidate_Kho_ID IS NOT NULL AND @Candidate_Kho_ID > 0
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
        WHERE Auto_ID = @Nhap_Kho_ID;
        IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Is_Posted = 1 THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.view_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51106, N'Sản phẩm không hợp lệ.', 1;
        IF @SL_Nhap <= 0 THROW 51107, N'Số lượng nhập phải lớn hơn 0.', 1;
        IF @Don_Gia_Nhap <= 0 THROW 51108, N'Đơn giá nhập phải lớn hơn 0.', 1;
            DECLARE @Existing_Nhap_Kho_ID BIGINT, @Existing_San_Pham_ID BIGINT;
            SELECT @Existing_Nhap_Kho_ID = Nhap_Kho_ID, @Existing_San_Pham_ID = San_Pham_ID
            FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WITH (UPDLOCK, HOLDLOCK)
            WHERE Auto_ID = @Auto_ID;
            IF @Existing_Nhap_Kho_ID IS NULL THROW 51109, N'Chi tiết phiếu nhập không tồn tại.', 1;
            IF @Existing_Nhap_Kho_ID <> @Nhap_Kho_ID OR @Existing_San_Pham_ID <> @San_Pham_ID THROW 51110, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
            UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data
            SET SL_Nhap = @SL_Nhap,
                Don_Gia_Nhap = @Don_Gia_Nhap,
                Last_Updated = SYSUTCDATETIME(),
                Last_Updated_By = COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                Last_Updated_By_Function = COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            WHERE Auto_ID = @Auto_ID;
            IF @@ROWCOUNT <> 1 THROW 51109, N'Chi tiết phiếu nhập không tồn tại.', 1;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveReceiptDetail;
        THROW;
    END CATCH
END
