
CREATE   PROCEDURE dbo.F3002_sp_upd_Xuat_Kho_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE,
    @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100),
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
        SAVE TRANSACTION SaveIssueHeader;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        DECLARE @Old_Kho_ID BIGINT, @Is_Posted BIT;
        IF ISNULL(@Auto_ID, 0) <> 0
            SELECT @Old_Kho_ID = Kho_ID
            FROM dbo.tbl_XNK_Xuat_Kho
            WHERE Auto_ID = @Auto_ID;
        DECLARE @Candidate_Old_Kho_ID BIGINT = @Old_Kho_ID;
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        IF @Kho_ID IS NOT NULL AND @Kho_ID > 0 INSERT @GroupSet(Kho_ID) VALUES (@Kho_ID);
        IF @Old_Kho_ID IS NOT NULL AND @Old_Kho_ID > 0 INSERT @GroupSet(Kho_ID) VALUES (@Old_Kho_ID);
        IF @Old_Kho_ID IS NOT NULL AND @Old_Kho_ID > 0 AND @Kho_ID IS NOT NULL AND @Kho_ID > 0
        BEGIN
            INSERT @ScopeSet(Kho_ID, San_Pham_ID)
            SELECT @Old_Kho_ID, d.San_Pham_ID
            FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d
            WHERE d.Xuat_Kho_ID = @Auto_ID
            UNION
            SELECT @Kho_ID, d.San_Pham_ID
            FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d
            WHERE d.Xuat_Kho_ID = @Auto_ID;
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

        SET @So_Phieu_Xuat_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho, N'')));
        IF @So_Phieu_Xuat_Kho = N'' THROW 51130, N'Số phiếu xuất không được để trống.', 1;
        IF @Ngay_Xuat_Kho IS NULL THROW 51133, N'Ngày xuất kho không được để trống.', 1;

        
            SELECT @Old_Kho_ID = Kho_ID, @Is_Posted = Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Auto_ID;
            IF @Old_Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            IF @Candidate_Old_Kho_ID <> @Old_Kho_ID
                THROW 51331, N'Phiếu xuất đã thay đổi sau khi khóa Inventory Fence; vui lòng thử lại.', 1;
            IF @Is_Posted = 1 THROW 51163, N'Không được sửa phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.view_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Old_Kho_ID;
            IF @Old_Kho_ID <> @Kho_ID EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho AND Auto_ID <> @Auto_ID) THROW 51131, N'Số phiếu xuất đã tồn tại.', 1;
            IF @Old_Kho_ID <> @Kho_ID EXEC dbo.sp_XNK_Reservation_Move_Document @Auto_ID, @Old_Kho_ID, @Kho_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho
            SET So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho,
                Kho_ID = @Kho_ID,
                Ngay_Xuat_Kho = @Ngay_Xuat_Kho,
                Ghi_Chu = @Ghi_Chu,
                Last_Updated = SYSUTCDATETIME(),
                Last_Updated_By = COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                Last_Updated_By_Function = COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            WHERE Auto_ID = @Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveIssueHeader;
        THROW;
    END CATCH
END

