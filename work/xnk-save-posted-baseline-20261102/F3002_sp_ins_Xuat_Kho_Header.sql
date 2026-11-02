CREATE   PROCEDURE dbo.F3002_sp_ins_Xuat_Kho_Header
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

        
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho) THROW 51131, N'Số phiếu xuất đã tồn tại.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.view_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
            INSERT dbo.tbl_XNK_Xuat_Kho
            (
                So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu,
                Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function
            )
            VALUES
            (
                @So_Phieu_Xuat_Kho, @Kho_ID, @Ngay_Xuat_Kho, 0, @Ghi_Chu,
                COALESCE(NULLIF(@Created_By, N''), NULLIF(@Last_Updated_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Created_By_Function, N''), NULLIF(@Last_Updated_By_Function, N'')),
                COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            );
        SET @Auto_ID = SCOPE_IDENTITY();
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveIssueHeader;
        THROW;
    END CATCH
END

