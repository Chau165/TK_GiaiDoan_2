-- Rollback for the approved function-code renumber migration.
-- Restores the original F2011/F2012 procedure definitions and grants,
-- then restores function codes 2011-2014. Does not revert unrelated work.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
BEGIN TRANSACTION;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2011_sp_del_Nhap_Kho_Detail
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
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2011_sp_del_Nhap_Kho_Header
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
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2011_sp_ins_Nhap_Kho_Detail
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
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51106, N'Sản phẩm không hợp lệ.', 1;
        IF @SL_Nhap <= 0 THROW 51107, N'Số lượng nhập phải lớn hơn 0.', 1;
        IF @Don_Gia_Nhap <= 0 THROW 51108, N'Đơn giá nhập phải lớn hơn 0.', 1;
        INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data
        (
            Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap,
            Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function
        )
        VALUES
        (
            @Nhap_Kho_ID, @San_Pham_ID, @SL_Nhap, @Don_Gia_Nhap,
            SYSUTCDATETIME(),
            COALESCE(NULLIF(@Created_By, N''), NULLIF(@Last_Updated_By, N''), @Ma_Dang_Nhap),
            COALESCE(NULLIF(@Created_By_Function, N''), NULLIF(@Last_Updated_By_Function, N'')),
            SYSUTCDATETIME(),
            COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
            COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
        );
        SET @Auto_ID = SCOPE_IDENTITY();
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveReceiptDetail;
        THROW;
    END CATCH
END
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2011_sp_ins_Nhap_Kho_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100),
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
        SAVE TRANSACTION SaveReceiptHeader;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        DECLARE @Old_Kho_ID BIGINT, @Is_Posted BIT;
        IF ISNULL(@Auto_ID, 0) <> 0
            SELECT @Old_Kho_ID = Kho_ID
            FROM dbo.tbl_XNK_Nhap_Kho
            WHERE Auto_ID = @Auto_ID;
        DECLARE @Candidate_Old_Kho_ID BIGINT = @Old_Kho_ID;
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        IF @Kho_ID IS NOT NULL AND @Kho_ID > 0 INSERT @GroupSet(Kho_ID) VALUES (@Kho_ID);
        IF @Old_Kho_ID IS NOT NULL AND @Old_Kho_ID > 0 INSERT @GroupSet(Kho_ID) VALUES (@Old_Kho_ID);
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

        SET @So_Phieu_Nhap_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho, N'')));
        IF @So_Phieu_Nhap_Kho = N'' THROW 51100, N'Số phiếu nhập không được để trống.', 1;
        IF @Ngay_Nhap_Kho IS NULL THROW 51104, N'Ngày nhập kho không được để trống.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID = @NCC_ID) THROW 51103, N'Nhà cung cấp không hợp lệ.', 1;

            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho) THROW 51101, N'Số phiếu nhập đã tồn tại.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51102, N'Kho không hợp lệ.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
            INSERT dbo.tbl_XNK_Nhap_Kho
            (
                So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu,
                Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function
            )
            VALUES
            (
                @So_Phieu_Nhap_Kho, @Kho_ID, @NCC_ID, @Ngay_Nhap_Kho, 0, @Ghi_Chu,
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
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveReceiptHeader;
        THROW;
    END CATCH
END
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2011_sp_sel_List_Nhap_Kho
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
    WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC;
END
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2011_sp_sel_List_Nhap_Kho_Detail
    @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT;
    SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID;
    IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT d.Auto_ID, d.Nhap_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia
    FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
    WHERE d.Nhap_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
END
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2011_sp_upd_Nhap_Kho_Detail
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
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51106, N'Sản phẩm không hợp lệ.', 1;
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
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2011_sp_upd_Nhap_Kho_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100),
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
        SAVE TRANSACTION SaveReceiptHeader;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        DECLARE @Old_Kho_ID BIGINT, @Is_Posted BIT;
        IF ISNULL(@Auto_ID, 0) <> 0
            SELECT @Old_Kho_ID = Kho_ID
            FROM dbo.tbl_XNK_Nhap_Kho
            WHERE Auto_ID = @Auto_ID;
        DECLARE @Candidate_Old_Kho_ID BIGINT = @Old_Kho_ID;
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        IF @Kho_ID IS NOT NULL AND @Kho_ID > 0 INSERT @GroupSet(Kho_ID) VALUES (@Kho_ID);
        IF @Old_Kho_ID IS NOT NULL AND @Old_Kho_ID > 0 INSERT @GroupSet(Kho_ID) VALUES (@Old_Kho_ID);
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

        SET @So_Phieu_Nhap_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho, N'')));
        IF @So_Phieu_Nhap_Kho = N'' THROW 51100, N'Số phiếu nhập không được để trống.', 1;
        IF @Ngay_Nhap_Kho IS NULL THROW 51104, N'Ngày nhập kho không được để trống.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID = @NCC_ID) THROW 51103, N'Nhà cung cấp không hợp lệ.', 1;

            SELECT @Old_Kho_ID = Kho_ID, @Is_Posted = Is_Posted
            FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK)
            WHERE Auto_ID = @Auto_ID;
            IF @Old_Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
            IF @Candidate_Old_Kho_ID <> @Old_Kho_ID
                THROW 51331, N'Phiếu nhập đã thay đổi sau khi khóa Inventory Fence; vui lòng thử lại.', 1;
            IF @Is_Posted = 1 THROW 51163, N'Không được sửa phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51102, N'Kho không hợp lệ.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Old_Kho_ID;
            IF @Old_Kho_ID <> @Kho_ID EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho AND Auto_ID <> @Auto_ID) THROW 51101, N'Số phiếu nhập đã tồn tại.', 1;
            UPDATE dbo.tbl_XNK_Nhap_Kho
            SET So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho,
                Kho_ID = @Kho_ID,
                NCC_ID = @NCC_ID,
                Ngay_Nhap_Kho = @Ngay_Nhap_Kho,
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
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveReceiptHeader;
        THROW;
    END CATCH
END
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2012_sp_del_Xuat_Kho_Detail
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
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2012_sp_del_Xuat_Kho_Header
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
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2012_sp_ins_Xuat_Kho_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100),
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
        SAVE TRANSACTION SaveIssueDetail;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        DECLARE @Candidate_Kho_ID BIGINT;
        DECLARE @Candidate_Old_San_Pham_ID BIGINT;
        SELECT @Candidate_Kho_ID = Kho_ID
        FROM dbo.tbl_XNK_Xuat_Kho
        WHERE Auto_ID = @Xuat_Kho_ID;
        IF ISNULL(@Auto_ID, 0) <> 0
            SELECT @Candidate_Old_San_Pham_ID = San_Pham_ID
            FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data
            WHERE Auto_ID = @Auto_ID;
        DECLARE @Candidate_Reservation_Kho_ID BIGINT;
        IF ISNULL(@Auto_ID, 0) <> 0
            SELECT @Candidate_Reservation_Kho_ID = Kho_ID
            FROM dbo.InventoryReservation_Current
            WHERE Xuat_Kho_Detail_ID = @Auto_ID;
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        IF @Candidate_Kho_ID IS NOT NULL AND @Candidate_Kho_ID > 0
            INSERT @GroupSet(Kho_ID) VALUES (@Candidate_Kho_ID);
        IF @Candidate_Reservation_Kho_ID IS NOT NULL AND @Candidate_Reservation_Kho_ID > 0
            INSERT @GroupSet(Kho_ID) VALUES (@Candidate_Reservation_Kho_ID);
        IF @Candidate_Kho_ID IS NOT NULL AND @Candidate_Kho_ID > 0
        BEGIN
            IF @San_Pham_ID IS NOT NULL AND @San_Pham_ID > 0
                INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@Candidate_Kho_ID, @San_Pham_ID);
            IF @Candidate_Old_San_Pham_ID IS NOT NULL AND @Candidate_Old_San_Pham_ID > 0
                INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@Candidate_Kho_ID, @Candidate_Old_San_Pham_ID);
        END;
        IF @Candidate_Reservation_Kho_ID IS NOT NULL AND @Candidate_Reservation_Kho_ID > 0
           AND @Candidate_Old_San_Pham_ID IS NOT NULL AND @Candidate_Old_San_Pham_ID > 0
            INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@Candidate_Reservation_Kho_ID, @Candidate_Old_San_Pham_ID);
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

        DECLARE @Kho_ID BIGINT, @Old_San_Pham_ID BIGINT, @Old_ReservedQuantity DECIMAL(18,3) = 0, @Reservation_Kho_ID BIGINT;
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Xuat_Kho_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        IF @Candidate_Kho_ID IS NOT NULL AND @Candidate_Kho_ID <> @Kho_ID
            THROW 51331, N'Phiếu xuất đã thay đổi sau khi khóa Inventory Fence; vui lòng thử lại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Xuat_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51135, N'Sản phẩm không hợp lệ.', 1;
        IF @SL_Xuat <= 0 THROW 51136, N'Số lượng xuất phải lớn hơn 0.', 1;
        IF @Don_Gia_Xuat <= 0 THROW 51137, N'Đơn giá xuất phải lớn hơn 0.', 1;

        IF ISNULL(@Auto_ID, 0) <> 0
        BEGIN
            SELECT @Old_San_Pham_ID = San_Pham_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Auto_ID AND Xuat_Kho_ID = @Xuat_Kho_ID;
            IF @Old_San_Pham_ID IS NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID) THROW 51138, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
                THROW 51139, N'Chi tiết phiếu xuất không tồn tại.', 1;
            END
            IF @Old_San_Pham_ID <> @San_Pham_ID THROW 51138, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
            SELECT @Old_ReservedQuantity = ReservedQuantity, @Reservation_Kho_ID = Kho_ID FROM dbo.InventoryReservation_Current WITH (UPDLOCK, HOLDLOCK) WHERE Xuat_Kho_Detail_ID = @Auto_ID;
            IF @Reservation_Kho_ID IS NOT NULL AND @Reservation_Kho_ID <> @Kho_ID
            BEGIN
                DECLARE @ReleaseDelta DECIMAL(18,3) = -@Old_ReservedQuantity;
                EXEC dbo.sp_XNK_Reservation_Adjust @Reservation_Kho_ID, @San_Pham_ID, @ReleaseDelta;
                SET @Old_ReservedQuantity = 0;
            END
        END

        DECLARE @ReservationDelta DECIMAL(18,3) = @SL_Xuat - @Old_ReservedQuantity;
        EXEC dbo.sp_XNK_Reservation_Adjust @Kho_ID, @San_Pham_ID, @ReservationDelta;
        
            INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data
            (
                Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat,
                Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function
            )
            VALUES
            (
                @Xuat_Kho_ID, @San_Pham_ID, @SL_Xuat, @Don_Gia_Xuat,
                SYSUTCDATETIME(),
                COALESCE(NULLIF(@Created_By, N''), NULLIF(@Last_Updated_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Created_By_Function, N''), NULLIF(@Last_Updated_By_Function, N'')),
                SYSUTCDATETIME(),
                COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            );
            SET @Auto_ID = SCOPE_IDENTITY();
            INSERT dbo.InventoryReservation_Current(Xuat_Kho_Detail_ID, Kho_ID, San_Pham_ID, ReservedQuantity) VALUES(@Auto_ID, @Kho_ID, @San_Pham_ID, @SL_Xuat);
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveIssueDetail;
        THROW;
    END CATCH
END
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2012_sp_ins_Xuat_Kho_Header
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
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
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
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2012_sp_sel_List_Xuat_Kho
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
    WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC;
END
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2012_sp_sel_List_Xuat_Kho_Detail
    @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT;
    SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID;
    IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT d.Auto_ID, d.Xuat_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia
    FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
    WHERE d.Xuat_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
END
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2012_sp_upd_Xuat_Kho_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100),
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
        SAVE TRANSACTION SaveIssueDetail;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        DECLARE @Candidate_Kho_ID BIGINT;
        DECLARE @Candidate_Old_San_Pham_ID BIGINT;
        SELECT @Candidate_Kho_ID = Kho_ID
        FROM dbo.tbl_XNK_Xuat_Kho
        WHERE Auto_ID = @Xuat_Kho_ID;
        IF ISNULL(@Auto_ID, 0) <> 0
            SELECT @Candidate_Old_San_Pham_ID = San_Pham_ID
            FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data
            WHERE Auto_ID = @Auto_ID;
        DECLARE @Candidate_Reservation_Kho_ID BIGINT;
        IF ISNULL(@Auto_ID, 0) <> 0
            SELECT @Candidate_Reservation_Kho_ID = Kho_ID
            FROM dbo.InventoryReservation_Current
            WHERE Xuat_Kho_Detail_ID = @Auto_ID;
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        IF @Candidate_Kho_ID IS NOT NULL AND @Candidate_Kho_ID > 0
            INSERT @GroupSet(Kho_ID) VALUES (@Candidate_Kho_ID);
        IF @Candidate_Reservation_Kho_ID IS NOT NULL AND @Candidate_Reservation_Kho_ID > 0
            INSERT @GroupSet(Kho_ID) VALUES (@Candidate_Reservation_Kho_ID);
        IF @Candidate_Kho_ID IS NOT NULL AND @Candidate_Kho_ID > 0
        BEGIN
            IF @San_Pham_ID IS NOT NULL AND @San_Pham_ID > 0
                INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@Candidate_Kho_ID, @San_Pham_ID);
            IF @Candidate_Old_San_Pham_ID IS NOT NULL AND @Candidate_Old_San_Pham_ID > 0
                INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@Candidate_Kho_ID, @Candidate_Old_San_Pham_ID);
        END;
        IF @Candidate_Reservation_Kho_ID IS NOT NULL AND @Candidate_Reservation_Kho_ID > 0
           AND @Candidate_Old_San_Pham_ID IS NOT NULL AND @Candidate_Old_San_Pham_ID > 0
            INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@Candidate_Reservation_Kho_ID, @Candidate_Old_San_Pham_ID);
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

        DECLARE @Kho_ID BIGINT, @Old_San_Pham_ID BIGINT, @Old_ReservedQuantity DECIMAL(18,3) = 0, @Reservation_Kho_ID BIGINT;
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Xuat_Kho_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        IF @Candidate_Kho_ID IS NOT NULL AND @Candidate_Kho_ID <> @Kho_ID
            THROW 51331, N'Phiếu xuất đã thay đổi sau khi khóa Inventory Fence; vui lòng thử lại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Xuat_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51135, N'Sản phẩm không hợp lệ.', 1;
        IF @SL_Xuat <= 0 THROW 51136, N'Số lượng xuất phải lớn hơn 0.', 1;
        IF @Don_Gia_Xuat <= 0 THROW 51137, N'Đơn giá xuất phải lớn hơn 0.', 1;

        IF ISNULL(@Auto_ID, 0) <> 0
        BEGIN
            SELECT @Old_San_Pham_ID = San_Pham_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Auto_ID AND Xuat_Kho_ID = @Xuat_Kho_ID;
            IF @Old_San_Pham_ID IS NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID) THROW 51138, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
                THROW 51139, N'Chi tiết phiếu xuất không tồn tại.', 1;
            END
            IF @Old_San_Pham_ID <> @San_Pham_ID THROW 51138, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
            SELECT @Old_ReservedQuantity = ReservedQuantity, @Reservation_Kho_ID = Kho_ID FROM dbo.InventoryReservation_Current WITH (UPDLOCK, HOLDLOCK) WHERE Xuat_Kho_Detail_ID = @Auto_ID;
            IF @Reservation_Kho_ID IS NOT NULL AND @Reservation_Kho_ID <> @Kho_ID
            BEGIN
                DECLARE @ReleaseDelta DECIMAL(18,3) = -@Old_ReservedQuantity;
                EXEC dbo.sp_XNK_Reservation_Adjust @Reservation_Kho_ID, @San_Pham_ID, @ReleaseDelta;
                SET @Old_ReservedQuantity = 0;
            END
        END

        DECLARE @ReservationDelta DECIMAL(18,3) = @SL_Xuat - @Old_ReservedQuantity;
        EXEC dbo.sp_XNK_Reservation_Adjust @Kho_ID, @San_Pham_ID, @ReservationDelta;
        
            UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data
            SET SL_Xuat = @SL_Xuat,
                Don_Gia_Xuat = @Don_Gia_Xuat,
                Last_Updated = SYSUTCDATETIME(),
                Last_Updated_By = COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                Last_Updated_By_Function = COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            WHERE Auto_ID = @Auto_ID;
            IF EXISTS (SELECT 1 FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @Auto_ID)
                UPDATE dbo.InventoryReservation_Current SET Kho_ID = @Kho_ID, San_Pham_ID = @San_Pham_ID, ReservedQuantity = @SL_Xuat, UpdatedAt = SYSUTCDATETIME() WHERE Xuat_Kho_Detail_ID = @Auto_ID;
            ELSE
                INSERT dbo.InventoryReservation_Current(Xuat_Kho_Detail_ID, Kho_ID, San_Pham_ID, ReservedQuantity) VALUES(@Auto_ID, @Kho_ID, @San_Pham_ID, @SL_Xuat);
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveIssueDetail;
        THROW;
    END CATCH
END
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
CREATE OR ALTER PROCEDURE dbo.F2012_sp_upd_Xuat_Kho_Header
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
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
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
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2011_sp_del_Nhap_Kho_Detail TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2011_sp_del_Nhap_Kho_Header TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2011_sp_ins_Nhap_Kho_Detail TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2011_sp_ins_Nhap_Kho_Header TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2011_sp_sel_List_Nhap_Kho TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2011_sp_sel_List_Nhap_Kho_Detail TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2011_sp_upd_Nhap_Kho_Detail TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2011_sp_upd_Nhap_Kho_Header TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2012_sp_del_Xuat_Kho_Detail TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2012_sp_del_Xuat_Kho_Header TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2012_sp_ins_Xuat_Kho_Detail TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2012_sp_ins_Xuat_Kho_Header TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2012_sp_sel_List_Xuat_Kho TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2012_sp_sel_List_Xuat_Kho_Detail TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2012_sp_upd_Xuat_Kho_Detail TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
GRANT EXECUTE ON OBJECT::dbo.F2012_sp_upd_Xuat_Kho_Header TO [Warehouse_Application];
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
UPDATE dbo.tbl_Sys_Chuc_Nang SET Ma_Chuc_Nang=N'2011' WHERE Auto_ID=110000102 AND Ma_Chuc_Nang=N'3001';
IF @@ROWCOUNT <> 1 THROW 52200, 'Rollback expected exactly one function row.', 1;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
UPDATE dbo.tbl_Sys_Chuc_Nang SET Ma_Chuc_Nang=N'2012' WHERE Auto_ID=110000103 AND Ma_Chuc_Nang=N'3002';
IF @@ROWCOUNT <> 1 THROW 52200, 'Rollback expected exactly one function row.', 1;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
UPDATE dbo.tbl_Sys_Chuc_Nang SET Ma_Chuc_Nang=N'2013' WHERE Auto_ID=110000104 AND Ma_Chuc_Nang=N'9001';
IF @@ROWCOUNT <> 1 THROW 52200, 'Rollback expected exactly one function row.', 1;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
UPDATE dbo.tbl_Sys_Chuc_Nang SET Ma_Chuc_Nang=N'2014' WHERE Auto_ID=110000105 AND Ma_Chuc_Nang=N'9002';
IF @@ROWCOUNT <> 1 THROW 52200, 'Rollback expected exactly one function row.', 1;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3001_sp_del_Nhap_Kho_Detail', N'P') IS NOT NULL DROP PROCEDURE dbo.F3001_sp_del_Nhap_Kho_Detail;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3001_sp_del_Nhap_Kho_Header', N'P') IS NOT NULL DROP PROCEDURE dbo.F3001_sp_del_Nhap_Kho_Header;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3001_sp_ins_Nhap_Kho_Detail', N'P') IS NOT NULL DROP PROCEDURE dbo.F3001_sp_ins_Nhap_Kho_Detail;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3001_sp_ins_Nhap_Kho_Header', N'P') IS NOT NULL DROP PROCEDURE dbo.F3001_sp_ins_Nhap_Kho_Header;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3001_sp_sel_List_Nhap_Kho', N'P') IS NOT NULL DROP PROCEDURE dbo.F3001_sp_sel_List_Nhap_Kho;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3001_sp_sel_List_Nhap_Kho_Detail', N'P') IS NOT NULL DROP PROCEDURE dbo.F3001_sp_sel_List_Nhap_Kho_Detail;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3001_sp_upd_Nhap_Kho_Detail', N'P') IS NOT NULL DROP PROCEDURE dbo.F3001_sp_upd_Nhap_Kho_Detail;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3001_sp_upd_Nhap_Kho_Header', N'P') IS NOT NULL DROP PROCEDURE dbo.F3001_sp_upd_Nhap_Kho_Header;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3002_sp_del_Xuat_Kho_Detail', N'P') IS NOT NULL DROP PROCEDURE dbo.F3002_sp_del_Xuat_Kho_Detail;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3002_sp_del_Xuat_Kho_Header', N'P') IS NOT NULL DROP PROCEDURE dbo.F3002_sp_del_Xuat_Kho_Header;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3002_sp_ins_Xuat_Kho_Detail', N'P') IS NOT NULL DROP PROCEDURE dbo.F3002_sp_ins_Xuat_Kho_Detail;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3002_sp_ins_Xuat_Kho_Header', N'P') IS NOT NULL DROP PROCEDURE dbo.F3002_sp_ins_Xuat_Kho_Header;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3002_sp_sel_List_Xuat_Kho', N'P') IS NOT NULL DROP PROCEDURE dbo.F3002_sp_sel_List_Xuat_Kho;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3002_sp_sel_List_Xuat_Kho_Detail', N'P') IS NOT NULL DROP PROCEDURE dbo.F3002_sp_sel_List_Xuat_Kho_Detail;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3002_sp_upd_Xuat_Kho_Detail', N'P') IS NOT NULL DROP PROCEDURE dbo.F3002_sp_upd_Xuat_Kho_Detail;
GO
SELECT DB_NAME() AS CurrentDatabase;
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2' THROW 52199, 'Rollback stopped: wrong database.', 1;
GO
IF OBJECT_ID(N'dbo.F3002_sp_upd_Xuat_Kho_Header', N'P') IS NOT NULL DROP PROCEDURE dbo.F3002_sp_upd_Xuat_Kho_Header;
GO
COMMIT TRANSACTION;
GO
-- Rebuild/redeploy application source separately if reverting the code migration.
