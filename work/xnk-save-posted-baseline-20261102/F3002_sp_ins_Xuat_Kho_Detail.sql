
CREATE   PROCEDURE dbo.F3002_sp_ins_Xuat_Kho_Detail
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
        IF NOT EXISTS (SELECT 1 FROM dbo.view_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51135, N'Sản phẩm không hợp lệ.', 1;
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

