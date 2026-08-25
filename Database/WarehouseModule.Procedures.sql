SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Don_Vi_Tinh_Save
    @Auto_ID BIGINT OUTPUT, @Ten_Don_Vi_Tinh NVARCHAR(200), @Ghi_Chu NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ten_Don_Vi_Tinh = LTRIM(RTRIM(ISNULL(@Ten_Don_Vi_Tinh, N'')));
    IF @Ten_Don_Vi_Tinh = N'' THROW 51001, N'Tên đơn vị tính không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI = @Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51002, N'Tên đơn vị tính đã tồn tại.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
    BEGIN INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) VALUES (@Ten_Don_Vi_Tinh, @Ghi_Chu); SET @Auto_ID = SCOPE_IDENTITY(); END
    ELSE UPDATE dbo.tbl_DM_Don_Vi_Tinh SET Ten_Don_Vi_Tinh = @Ten_Don_Vi_Tinh, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Auto_ID;
END
GO

/* FINAL DRAFT ISSUE RESERVATION OVERRIDES.
   CurrentQuantity is posted/on-hand stock. ReservedQuantity is held by active
   draft issue details; AvailableQuantity is derived as CurrentQuantity - ReservedQuantity. */

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Adjust
    @Kho_ID BIGINT, @San_Pham_ID BIGINT, @Delta DECIMAL(18,3)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Delta = 0 RETURN;

    DECLARE @CurrentQuantity DECIMAL(18,3), @ReservedQuantity DECIMAL(18,3);
    SELECT @CurrentQuantity = CurrentQuantity, @ReservedQuantity = ReservedQuantity
    FROM dbo.InventoryBalance_Current WITH (UPDLOCK, HOLDLOCK)
    WHERE Kho_ID = @Kho_ID AND San_Pham_ID = @San_Pham_ID;

    IF @CurrentQuantity IS NULL OR @Delta > 0 AND @CurrentQuantity - @ReservedQuantity < @Delta
        THROW 51140, N'Tồn khả dụng không đủ để giữ cho phiếu xuất nháp.', 1;
    IF @Delta < 0 AND @ReservedQuantity < -@Delta
        THROW 51141, N'Dữ liệu giữ chỗ tồn kho không hợp lệ.', 1;

    UPDATE dbo.InventoryBalance_Current
    SET ReservedQuantity = ReservedQuantity + @Delta, UpdatedAt = SYSUTCDATETIME()
    WHERE Kho_ID = @Kho_ID AND San_Pham_ID = @San_Pham_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Move_Document
    @Xuat_Kho_ID BIGINT, @Old_Kho_ID BIGINT, @New_Kho_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Old_Kho_ID = @New_Kho_ID RETURN;

    DECLARE @San_Pham_ID BIGINT, @ReservedQuantity DECIMAL(18,3), @Delta DECIMAL(18,3);
    DECLARE reservation_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT r.San_Pham_ID, SUM(r.ReservedQuantity)
        FROM dbo.InventoryReservation_Current r
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
        WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID AND r.Kho_ID = @Old_Kho_ID
        GROUP BY r.San_Pham_ID;

    OPEN reservation_cursor;
    FETCH NEXT FROM reservation_cursor INTO @San_Pham_ID, @ReservedQuantity;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Delta = -@ReservedQuantity;
        EXEC dbo.sp_XNK_Reservation_Adjust @Old_Kho_ID, @San_Pham_ID, @Delta;
        EXEC dbo.sp_XNK_Reservation_Adjust @New_Kho_ID, @San_Pham_ID, @ReservedQuantity;
        UPDATE r SET Kho_ID = @New_Kho_ID, UpdatedAt = SYSUTCDATETIME()
        FROM dbo.InventoryReservation_Current r
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
        WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID AND r.Kho_ID = @Old_Kho_ID AND r.San_Pham_ID = @San_Pham_ID;
        FETCH NEXT FROM reservation_cursor INTO @San_Pham_ID, @ReservedQuantity;
    END
    CLOSE reservation_cursor;
    DEALLOCATE reservation_cursor;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Release_Detail
    @Xuat_Kho_Detail_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT, @San_Pham_ID BIGINT, @ReservedQuantity DECIMAL(18,3), @Delta DECIMAL(18,3);
    SELECT @Kho_ID = Kho_ID, @San_Pham_ID = San_Pham_ID, @ReservedQuantity = ReservedQuantity
    FROM dbo.InventoryReservation_Current WITH (UPDLOCK, HOLDLOCK)
    WHERE Xuat_Kho_Detail_ID = @Xuat_Kho_Detail_ID;
    IF @ReservedQuantity IS NULL RETURN;

    SET @Delta = -@ReservedQuantity;
    EXEC dbo.sp_XNK_Reservation_Adjust @Kho_ID, @San_Pham_ID, @Delta;
    DELETE dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @Xuat_Kho_Detail_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Release_Document
    @Xuat_Kho_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @San_Pham_ID BIGINT, @ReservedQuantity DECIMAL(18,3), @Delta DECIMAL(18,3);
    DECLARE reservation_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT r.San_Pham_ID, SUM(r.ReservedQuantity)
        FROM dbo.InventoryReservation_Current r
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
        WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID
        GROUP BY r.San_Pham_ID;

    OPEN reservation_cursor;
    FETCH NEXT FROM reservation_cursor INTO @San_Pham_ID, @ReservedQuantity;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SELECT TOP (1) @Delta = -@ReservedQuantity;
        DECLARE @Kho_ID BIGINT = (SELECT TOP (1) r.Kho_ID
                                  FROM dbo.InventoryReservation_Current r
                                  JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
                                  WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID AND r.San_Pham_ID = @San_Pham_ID);
        EXEC dbo.sp_XNK_Reservation_Adjust @Kho_ID, @San_Pham_ID, @Delta;
        DELETE r
        FROM dbo.InventoryReservation_Current r
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
        WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID AND r.San_Pham_ID = @San_Pham_ID;
        FETCH NEXT FROM reservation_cursor INTO @San_Pham_ID, @ReservedQuantity;
    END
    CLOSE reservation_cursor;
    DEALLOCATE reservation_cursor;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Rebuild
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0 BEGIN TRANSACTION; SET @OwnTransaction = 1;
    BEGIN TRY
        DELETE FROM dbo.InventoryReservation_Current;
        UPDATE dbo.InventoryBalance_Current SET ReservedQuantity = 0, UpdatedAt = SYSUTCDATETIME();
        INSERT dbo.InventoryReservation_Current(Xuat_Kho_Detail_ID, Kho_ID, San_Pham_ID, ReservedQuantity)
        SELECT d.Auto_ID, h.Kho_ID, d.San_Pham_ID, d.SL_Xuat
        FROM dbo.tbl_XNK_Xuat_Kho h
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        WHERE h.Is_Posted = 0;

        IF EXISTS
        (
            SELECT 1
            FROM (SELECT Kho_ID, San_Pham_ID, SUM(ReservedQuantity) AS ReservedQuantity
                  FROM dbo.InventoryReservation_Current GROUP BY Kho_ID, San_Pham_ID) r
            LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK)
                ON b.Kho_ID = r.Kho_ID AND b.San_Pham_ID = r.San_Pham_ID
            WHERE b.CurrentQuantity IS NULL OR b.CurrentQuantity < r.ReservedQuantity
        ) THROW 51140, N'Tồn khả dụng không đủ cho các phiếu xuất nháp hiện có.', 1;

        UPDATE b SET ReservedQuantity = r.ReservedQuantity, UpdatedAt = SYSUTCDATETIME()
        FROM dbo.InventoryBalance_Current b
        JOIN (SELECT Kho_ID, San_Pham_ID, SUM(ReservedQuantity) AS ReservedQuantity
              FROM dbo.InventoryReservation_Current GROUP BY Kho_ID, San_Pham_ID) r
          ON r.Kho_ID = b.Kho_ID AND r.San_Pham_ID = b.San_Pham_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* Draft/Post lifecycle. Raw-data rows are document details; only posted headers
   contribute movements. Posting is the sole transition that updates the current
   balance projection, and it owns one SQL Server transaction. */
CREATE OR ALTER PROCEDURE dbo.sp_XNK_Validate_All_Balances
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @HasNegative BIT=0;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Nhap_Kho AS MovementDate,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Delta
        FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
        UNION ALL
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Xuat_Kho,CAST(-d.SL_Xuat AS DECIMAL(18,3))
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
    ), Daily AS
    (
        SELECT Kho_ID,San_Pham_ID,MovementDate,SUM(Delta) AS Delta FROM Movements GROUP BY Kho_ID,San_Pham_ID,MovementDate
    ), Running AS
    (
        SELECT SUM(Delta) OVER(PARTITION BY Kho_ID,San_Pham_ID ORDER BY MovementDate ROWS UNBOUNDED PRECEDING) AS Balance FROM Daily
    )
    SELECT @HasNegative=CASE WHEN MIN(Balance)<0 THEN 1 ELSE 0 END FROM Running;
    IF @HasNegative=1 THROW 51120,N'Không thể Post vì tồn kho sẽ âm tại một thời điểm trong lịch sử.',1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @So_Phieu_Nhap_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho,N'')));
    IF @So_Phieu_Nhap_Kho=N'' THROW 51100,N'Số phiếu nhập không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51101,N'Số phiếu nhập đã tồn tại.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51102,N'Kho không hợp lệ.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID=@NCC_ID) THROW 51103,N'Nhà cung cấp không hợp lệ.',1;
    IF @Ngay_Nhap_Kho IS NULL THROW 51104,N'Ngày nhập kho không được để trống.',1;
    IF ISNULL(@Auto_ID,0)=0
    BEGIN
        INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho,Kho_ID,NCC_ID,Ngay_Nhap_Kho,Is_Posted,Ghi_Chu) VALUES(@So_Phieu_Nhap_Kho,@Kho_ID,@NCC_ID,@Ngay_Nhap_Kho,0,@Ghi_Chu);
        SET @Auto_ID=SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID) THROW 51105,N'Phiếu nhập không tồn tại.',1;
        IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID AND Is_Posted=1) THROW 51163,N'Không được sửa phiếu đã Post.',1;
        UPDATE dbo.tbl_XNK_Nhap_Kho SET So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho,Kho_ID=@Kho_ID,NCC_ID=@NCC_ID,Ngay_Nhap_Kho=@Ngay_Nhap_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID;
    END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @So_Phieu_Xuat_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho,N'')));
    IF @So_Phieu_Xuat_Kho=N'' THROW 51130,N'Số phiếu xuất không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51131,N'Số phiếu xuất đã tồn tại.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51132,N'Kho không hợp lệ.',1;
    IF @Ngay_Xuat_Kho IS NULL THROW 51133,N'Ngày xuất kho không được để trống.',1;
    IF ISNULL(@Auto_ID,0)=0
    BEGIN
        INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho,Kho_ID,Ngay_Xuat_Kho,Is_Posted,Ghi_Chu) VALUES(@So_Phieu_Xuat_Kho,@Kho_ID,@Ngay_Xuat_Kho,0,@Ghi_Chu);
        SET @Auto_ID=SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID) THROW 51134,N'Phiếu xuất không tồn tại.',1;
        IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID AND Is_Posted=1) THROW 51163,N'Không được sửa phiếu đã Post.',1;
        UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho,Kho_ID=@Kho_ID,Ngay_Xuat_Kho=@Ngay_Xuat_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID;
    END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Nhap_Kho_ID) THROW 51105,N'Phiếu nhập không tồn tại.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Nhap_Kho_ID AND Is_Posted=1) THROW 51163,N'Không được sửa chi tiết của phiếu đã Post.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51106,N'Sản phẩm không hợp lệ.',1;
    IF @SL_Nhap<=0 THROW 51107,N'Số lượng nhập phải lớn hơn 0.',1;
    IF @Don_Gia_Nhap<=0 THROW 51108,N'Đơn giá nhập phải lớn hơn 0.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID,San_Pham_ID,SL_Nhap,Don_Gia_Nhap) VALUES(@Nhap_Kho_ID,@San_Pham_ID,@SL_Nhap,@Don_Gia_Nhap); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Nhap_Kho_ID<>@Nhap_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51110,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap=@SL_Nhap,Don_Gia_Nhap=@Don_Gia_Nhap WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Xuat_Kho_ID) THROW 51134,N'Phiếu xuất không tồn tại.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Xuat_Kho_ID AND Is_Posted=1) THROW 51163,N'Không được sửa chi tiết của phiếu đã Post.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51135,N'Sản phẩm không hợp lệ.',1;
    IF @SL_Xuat<=0 THROW 51136,N'Số lượng xuất phải lớn hơn 0.',1;
    IF @Don_Gia_Xuat<=0 THROW 51137,N'Đơn giá xuất phải lớn hơn 0.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID,San_Pham_ID,SL_Xuat,Don_Gia_Xuat) VALUES(@Xuat_Kho_ID,@San_Pham_ID,@SL_Xuat,@Don_Gia_Xuat); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Xuat_Kho_ID<>@Xuat_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51138,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat=@SL_Xuat,Don_Gia_Xuat=@Don_Gia_Xuat WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;
    IF @OwnTransaction = 1 BEGIN TRANSACTION ELSE SAVE TRANSACTION WarehouseDelete;
    BEGIN TRY
        DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID);
        IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa phiếu đã Post.', 1;
        DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION ELSE IF XACT_STATE() = 1 ROLLBACK TRANSACTION WarehouseDelete;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;
    IF @OwnTransaction = 1 BEGIN TRANSACTION ELSE SAVE TRANSACTION WarehouseDelete;
    BEGIN TRY
        DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID);
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa phiếu đã Post.', 1;
        DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION ELSE IF XACT_STATE() = 1 ROLLBACK TRANSACTION WarehouseDelete;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT = (SELECT h.Kho_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE d.Auto_ID = @Auto_ID);
    IF @Kho_ID IS NULL THROW 51105, N'Chi tiết phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE d.Auto_ID = @Auto_ID AND h.Is_Posted = 1) THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
    DELETE FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
    EXEC dbo.sp_XNK_Validate_All_Balances;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT = (SELECT h.Kho_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE d.Auto_ID = @Auto_ID);
    IF @Kho_ID IS NULL THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE d.Auto_ID = @Auto_ID AND h.Is_Posted = 1) THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
    DELETE FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
    EXEC dbo.sp_XNK_Validate_All_Balances;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Post
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        CREATE TABLE #Delta(Kho_ID BIGINT NOT NULL, San_Pham_ID BIGINT NOT NULL, Delta DECIMAL(18,3) NOT NULL, PRIMARY KEY(Kho_ID, San_Pham_ID));
        IF @Is_Receipt = 1
        BEGIN
            DECLARE @ReceiptWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            IF @ReceiptWarehouse IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @ReceiptWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(d.SL_Nhap AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
            UPDATE dbo.tbl_XNK_Nhap_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
        END
        ELSE
        BEGIN
            DECLARE @IssueWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            IF @IssueWarehouse IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @IssueWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(-d.SL_Xuat AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
        END
        IF EXISTS (SELECT 1 FROM #Delta d LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) ON b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID WHERE ISNULL(b.CurrentQuantity, 0) + d.Delta < 0) THROW 51120, N'Không thể Post vì tồn kho không đủ.', 1;
        UPDATE b SET CurrentQuantity = b.CurrentQuantity + d.Delta, UpdatedAt = SYSUTCDATETIME() FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) JOIN #Delta d ON d.Kho_ID = b.Kho_ID AND d.San_Pham_ID = b.San_Pham_ID;
        INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity) SELECT d.Kho_ID, d.San_Pham_ID, d.Delta FROM #Delta d WHERE NOT EXISTS (SELECT 1 FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) WHERE b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID);
        EXEC dbo.sp_XNK_Validate_All_Balances;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    SELECT h.Ngay_Nhap_Kho AS Ngay, h.So_Phieu_Nhap_Kho AS So_Phieu, n.Ten_NCC AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia, CAST(d.SL_Nhap * d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    SELECT h.Ngay_Xuat_Kho AS Ngay, h.So_Phieu_Xuat_Kho AS So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia, CAST(d.SL_Xuat * d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Nhap_Kho AS MovementDate, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity, CAST(0 AS DECIMAL(18,3)) AS OutQuantity
        FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Nhap_Kho <= @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        UNION ALL
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Xuat_Kho, CAST(0 AS DECIMAL(18,3)), CAST(d.SL_Xuat AS DECIMAL(18,3))
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Xuat_Kho <= @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ), Aggregated AS
    (
        SELECT Kho_ID, San_Pham_ID, SUM(CASE WHEN MovementDate < @Tu_Ngay THEN InQuantity - OutQuantity ELSE 0 END) AS SL_Dau_Ky, SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN InQuantity ELSE 0 END) AS SL_Nhap, SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN OutQuantity ELSE 0 END) AS SL_Xuat
        FROM Movements GROUP BY Kho_ID, San_Pham_ID
    )
    SELECT a.Kho_ID, k.Ten_Kho, a.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, a.SL_Dau_Ky, a.SL_Nhap, a.SL_Xuat, a.SL_Dau_Ky + a.SL_Nhap - a.SL_Xuat AS SL_Cuoi_Ky
    FROM Aggregated a JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = a.Kho_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = a.San_Pham_ID
    ORDER BY k.Ten_Kho, p.Ma_San_Pham;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID);
    SELECT h.Ngay_Nhap_Kho AS Ngay, h.So_Phieu_Nhap_Kho AS So_Phieu, n.Ten_NCC AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia, CAST(d.SL_Nhap * d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID);
    SELECT h.Ngay_Xuat_Kho AS Ngay, h.So_Phieu_Xuat_Kho AS So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia, CAST(d.SL_Xuat * d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Nhap_Kho AS MovementDate, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity, CAST(0 AS DECIMAL(18,3)) AS OutQuantity FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Ngay_Nhap_Kho <= @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        UNION ALL
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Xuat_Kho, CAST(0 AS DECIMAL(18,3)), CAST(d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Ngay_Xuat_Kho <= @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ), Aggregated AS
    (
        SELECT Kho_ID, San_Pham_ID, SUM(CASE WHEN MovementDate < @Tu_Ngay THEN InQuantity - OutQuantity ELSE 0 END) AS SL_Dau_Ky, SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN InQuantity ELSE 0 END) AS SL_Nhap, SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN OutQuantity ELSE 0 END) AS SL_Xuat FROM Movements GROUP BY Kho_ID, San_Pham_ID
    )
    SELECT a.Kho_ID, k.Ten_Kho, a.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, a.SL_Dau_Ky, a.SL_Nhap, a.SL_Xuat, a.SL_Dau_Ky + a.SL_Nhap - a.SL_Xuat AS SL_Cuoi_Ky INTO #WarehouseScopeResult
    FROM Aggregated a JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = a.Kho_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = a.San_Pham_ID;
    SELECT COUNT(*) AS Total_Count FROM #WarehouseScopeResult;
    SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham, SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky FROM #WarehouseScopeResult ORDER BY Ten_Kho, Ma_San_Pham OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;
    IF @OwnTransaction = 1 BEGIN TRANSACTION;
    ELSE SAVE TRANSACTION WarehouseDelete;
    BEGIN TRY
        DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID);
        IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa phiếu đã Post.', 1;
        DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        ELSE IF XACT_STATE() = 1 ROLLBACK TRANSACTION WarehouseDelete;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;
    IF @OwnTransaction = 1 BEGIN TRANSACTION;
    ELSE SAVE TRANSACTION WarehouseDelete;
    BEGIN TRY
        DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID);
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa phiếu đã Post.', 1;
        DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        ELSE IF XACT_STATE() = 1 ROLLBACK TRANSACTION WarehouseDelete;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT =
    (
        SELECT h.Kho_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d
        JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID
        WHERE d.Auto_ID = @Auto_ID
    );
    IF @Kho_ID IS NULL THROW 51105, N'Chi tiết phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS
    (
        SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d
        JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID
        WHERE d.Auto_ID = @Auto_ID AND h.Is_Posted = 1
    ) THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
    DELETE FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
    EXEC dbo.sp_XNK_Validate_All_Balances;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT =
    (
        SELECT h.Kho_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d
        JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID
        WHERE d.Auto_ID = @Auto_ID
    );
    IF @Kho_ID IS NULL THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS
    (
        SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d
        JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID
        WHERE d.Auto_ID = @Auto_ID AND h.Is_Posted = 1
    ) THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
    DELETE FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
    EXEC dbo.sp_XNK_Validate_All_Balances;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Post
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        CREATE TABLE #Delta(Kho_ID BIGINT NOT NULL, San_Pham_ID BIGINT NOT NULL, Delta DECIMAL(18,3) NOT NULL, PRIMARY KEY(Kho_ID, San_Pham_ID));
        IF @Is_Receipt = 1
        BEGIN
            DECLARE @ReceiptWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            IF @ReceiptWarehouse IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @ReceiptWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(d.SL_Nhap AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
            UPDATE dbo.tbl_XNK_Nhap_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
        END
        ELSE
        BEGIN
            DECLARE @IssueWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            IF @IssueWarehouse IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @IssueWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(-d.SL_Xuat AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
        END
        IF EXISTS (SELECT 1 FROM #Delta d LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) ON b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID WHERE ISNULL(b.CurrentQuantity, 0) + d.Delta < 0) THROW 51120, N'Không thể Post vì tồn kho không đủ.', 1;
        UPDATE b SET CurrentQuantity = b.CurrentQuantity + d.Delta, UpdatedAt = SYSUTCDATETIME() FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) JOIN #Delta d ON d.Kho_ID = b.Kho_ID AND d.San_Pham_ID = b.San_Pham_ID;
        INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity) SELECT d.Kho_ID, d.San_Pham_ID, d.Delta FROM #Delta d WHERE NOT EXISTS (SELECT 1 FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) WHERE b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID);
        EXEC dbo.sp_XNK_Validate_All_Balances;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    SELECT h.Ngay_Nhap_Kho AS Ngay, h.So_Phieu_Nhap_Kho AS So_Phieu, n.Ten_NCC AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia, CAST(d.SL_Nhap * d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
    JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
      AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    SELECT h.Ngay_Xuat_Kho AS Ngay, h.So_Phieu_Xuat_Kho AS So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia, CAST(d.SL_Xuat * d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Xuat_Kho h
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
      AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Nhap_Kho AS MovementDate, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity, CAST(0 AS DECIMAL(18,3)) AS OutQuantity
        FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Nhap_Kho <= @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        UNION ALL
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Xuat_Kho, CAST(0 AS DECIMAL(18,3)), CAST(d.SL_Xuat AS DECIMAL(18,3))
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Xuat_Kho <= @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ), Aggregated AS
    (
        SELECT Kho_ID, San_Pham_ID, SUM(CASE WHEN MovementDate < @Tu_Ngay THEN InQuantity - OutQuantity ELSE 0 END) AS SL_Dau_Ky,
               SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN InQuantity ELSE 0 END) AS SL_Nhap,
               SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN OutQuantity ELSE 0 END) AS SL_Xuat
        FROM Movements GROUP BY Kho_ID, San_Pham_ID
    )
    SELECT a.Kho_ID, k.Ten_Kho, a.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, a.SL_Dau_Ky, a.SL_Nhap, a.SL_Xuat, a.SL_Dau_Ky + a.SL_Nhap - a.SL_Xuat AS SL_Cuoi_Ky
    FROM Aggregated a JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = a.Kho_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = a.San_Pham_ID
    ORDER BY k.Ten_Kho, p.Ma_San_Pham;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    SELECT COUNT(*) AS Total_Count
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID);
    SELECT h.Ngay_Nhap_Kho AS Ngay, h.So_Phieu_Nhap_Kho AS So_Phieu, n.Ten_NCC AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia, CAST(d.SL_Nhap * d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    SELECT COUNT(*) AS Total_Count
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID);
    SELECT h.Ngay_Xuat_Kho AS Ngay, h.So_Phieu_Xuat_Kho AS So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia, CAST(d.SL_Xuat * d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Nhap_Kho AS MovementDate, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity, CAST(0 AS DECIMAL(18,3)) AS OutQuantity
        FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Nhap_Kho <= @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        UNION ALL
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Xuat_Kho, CAST(0 AS DECIMAL(18,3)), CAST(d.SL_Xuat AS DECIMAL(18,3))
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Xuat_Kho <= @Den_Ngay AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ), Aggregated AS
    (
        SELECT Kho_ID, San_Pham_ID, SUM(CASE WHEN MovementDate < @Tu_Ngay THEN InQuantity - OutQuantity ELSE 0 END) AS SL_Dau_Ky,
               SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN InQuantity ELSE 0 END) AS SL_Nhap,
               SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN OutQuantity ELSE 0 END) AS SL_Xuat
        FROM Movements GROUP BY Kho_ID, San_Pham_ID
    )
    SELECT a.Kho_ID, k.Ten_Kho, a.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, a.SL_Dau_Ky, a.SL_Nhap, a.SL_Xuat, a.SL_Dau_Ky + a.SL_Nhap - a.SL_Xuat AS SL_Cuoi_Ky
    INTO #Result
    FROM Aggregated a JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = a.Kho_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = a.San_Pham_ID;
    SELECT COUNT(*) AS Total_Count FROM #Result;
    SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham, SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky
    FROM #Result ORDER BY Ten_Kho, Ma_San_Pham OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Post @Is_Receipt BIT, @Document_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        CREATE TABLE #Delta(Kho_ID BIGINT NOT NULL, San_Pham_ID BIGINT NOT NULL, Delta DECIMAL(18,3) NOT NULL, PRIMARY KEY(Kho_ID,San_Pham_ID));
        IF @Is_Receipt=1
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK,HOLDLOCK) WHERE Auto_ID=@Document_ID) THROW 51105,N'Phiếu nhập không tồn tại.',1;
            IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK,HOLDLOCK) WHERE Auto_ID=@Document_ID AND Is_Posted=1) THROW 51162,N'Phiếu đã Post.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID=@Document_ID) THROW 51161,N'Không thể Post phiếu không có chi tiết.',1;
            INSERT #Delta SELECT h.Kho_ID,d.San_Pham_ID,SUM(CAST(d.SL_Nhap AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Auto_ID=@Document_ID GROUP BY h.Kho_ID,d.San_Pham_ID;
            UPDATE dbo.tbl_XNK_Nhap_Kho SET Is_Posted=1,Posted_At=SYSUTCDATETIME(),Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Document_ID;
        END
        ELSE
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK,HOLDLOCK) WHERE Auto_ID=@Document_ID) THROW 51134,N'Phiếu xuất không tồn tại.',1;
            IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK,HOLDLOCK) WHERE Auto_ID=@Document_ID AND Is_Posted=1) THROW 51162,N'Phiếu đã Post.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID=@Document_ID) THROW 51161,N'Không thể Post phiếu không có chi tiết.',1;
            INSERT #Delta SELECT h.Kho_ID,d.San_Pham_ID,SUM(CAST(-d.SL_Xuat AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Auto_ID=@Document_ID GROUP BY h.Kho_ID,d.San_Pham_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho SET Is_Posted=1,Posted_At=SYSUTCDATETIME(),Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Document_ID;
        END
        IF EXISTS(SELECT 1 FROM #Delta d LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK,HOLDLOCK) ON b.Kho_ID=d.Kho_ID AND b.San_Pham_ID=d.San_Pham_ID WHERE ISNULL(b.CurrentQuantity,0)+d.Delta<0) THROW 51120,N'Không thể Post vì tồn kho không đủ.',1;
        UPDATE b SET CurrentQuantity=b.CurrentQuantity+d.Delta,UpdatedAt=SYSUTCDATETIME() FROM dbo.InventoryBalance_Current b WITH (UPDLOCK,HOLDLOCK) JOIN #Delta d ON d.Kho_ID=b.Kho_ID AND d.San_Pham_ID=b.San_Pham_ID;
        INSERT dbo.InventoryBalance_Current(Kho_ID,San_Pham_ID,CurrentQuantity) SELECT d.Kho_ID,d.San_Pham_ID,d.Delta FROM #Delta d WHERE NOT EXISTS(SELECT 1 FROM dbo.InventoryBalance_Current b WITH (UPDLOCK,HOLDLOCK) WHERE b.Kho_ID=d.Kho_ID AND b.San_Pham_ID=d.San_Pham_ID);
        EXEC dbo.sp_XNK_Validate_All_Balances;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_InventoryBalance_Rebuild
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DELETE FROM dbo.InventoryBalance_Current;
    ;WITH Delta AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Amount FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
        UNION ALL SELECT h.Kho_ID,d.San_Pham_ID,CAST(-d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
    )
    INSERT dbo.InventoryBalance_Current(Kho_ID,San_Pham_ID,CurrentQuantity) SELECT Kho_ID,San_Pham_ID,SUM(Amount) FROM Delta GROUP BY Kho_ID,San_Pham_ID;
    COMMIT TRANSACTION;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Loai_San_Pham_Save
    @Auto_ID BIGINT OUTPUT, @Ma_LSP NVARCHAR(100), @Ten_LSP NVARCHAR(200), @Ghi_Chu NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_LSP = LTRIM(RTRIM(ISNULL(@Ma_LSP, N''))); SET @Ten_LSP = LTRIM(RTRIM(ISNULL(@Ten_LSP, N'')));
    IF @Ma_LSP = N'' THROW 51010, N'Mã loại sản phẩm không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE Ma_LSP = @Ma_LSP AND Auto_ID <> ISNULL(@Auto_ID,0)) THROW 51011, N'Mã loại sản phẩm đã tồn tại.', 1;
    IF @Ten_LSP = N'' THROW 51012, N'Tên loại sản phẩm không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE Ten_LSP = @Ten_LSP AND Auto_ID <> ISNULL(@Auto_ID,0)) THROW 51013, N'Tên loại sản phẩm đã tồn tại.', 1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP,Ten_LSP,Ghi_Chu) VALUES(@Ma_LSP,@Ten_LSP,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE UPDATE dbo.tbl_DM_Loai_San_Pham SET Ma_LSP=@Ma_LSP,Ten_LSP=@Ten_LSP,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_San_Pham_Save
    @Auto_ID BIGINT OUTPUT, @Ma_San_Pham NVARCHAR(100), @Ten_San_Pham NVARCHAR(255), @Loai_San_Pham_ID BIGINT, @Don_Vi_Tinh_ID BIGINT, @Ghi_Chu NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_San_Pham=LTRIM(RTRIM(ISNULL(@Ma_San_Pham,N''))); SET @Ten_San_Pham=LTRIM(RTRIM(ISNULL(@Ten_San_Pham,N'')));
    IF @Ma_San_Pham=N'' THROW 51020,N'Mã sản phẩm không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham=@Ma_San_Pham AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51021,N'Mã sản phẩm đã tồn tại.',1;
    IF @Ten_San_Pham=N'' THROW 51022,N'Tên sản phẩm không được để trống.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID=@Loai_San_Pham_ID) THROW 51023,N'Loại sản phẩm không hợp lệ.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID=@Don_Vi_Tinh_ID) THROW 51024,N'Đơn vị tính không hợp lệ.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham,Ten_San_Pham,Loai_San_Pham_ID,Don_Vi_Tinh_ID,Ghi_Chu) VALUES(@Ma_San_Pham,@Ten_San_Pham,@Loai_San_Pham_ID,@Don_Vi_Tinh_ID,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE UPDATE dbo.tbl_DM_San_Pham SET Ma_San_Pham=@Ma_San_Pham,Ten_San_Pham=@Ten_San_Pham,Loai_San_Pham_ID=@Loai_San_Pham_ID,Don_Vi_Tinh_ID=@Don_Vi_Tinh_ID,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_NCC_Save
    @Auto_ID BIGINT OUTPUT, @Ma_NCC NVARCHAR(100), @Ten_NCC NVARCHAR(255), @Ghi_Chu NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_NCC=LTRIM(RTRIM(ISNULL(@Ma_NCC,N''))); SET @Ten_NCC=LTRIM(RTRIM(ISNULL(@Ten_NCC,N'')));
    IF @Ma_NCC=N'' THROW 51030,N'Mã nhà cung cấp không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_NCC WHERE Ma_NCC=@Ma_NCC AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51031,N'Mã nhà cung cấp đã tồn tại.',1;
    IF @Ten_NCC=N'' THROW 51032,N'Tên nhà cung cấp không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_NCC WHERE Ten_NCC=@Ten_NCC AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51033,N'Tên nhà cung cấp đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_DM_NCC(Ma_NCC,Ten_NCC,Ghi_Chu) VALUES(@Ma_NCC,@Ten_NCC,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE UPDATE dbo.tbl_DM_NCC SET Ma_NCC=@Ma_NCC,Ten_NCC=@Ten_NCC,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_Save
    @Auto_ID BIGINT OUTPUT, @Ten_Kho NVARCHAR(255), @Ghi_Chu NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ten_Kho=LTRIM(RTRIM(ISNULL(@Ten_Kho,N'')));
    IF @Ten_Kho=N'' THROW 51040,N'Tên kho không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Ten_Kho=@Ten_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51041,N'Tên kho đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_DM_Kho(Ten_Kho,Ghi_Chu) VALUES(@Ten_Kho,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE UPDATE dbo.tbl_DM_Kho SET Ten_Kho=@Ten_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Save
    @Auto_ID BIGINT OUTPUT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap=LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap,N'')));
    IF @Ma_Dang_Nhap=N'' THROW 51050,N'Mã đăng nhập không được để trống.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51051,N'Kho không hợp lệ.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap=@Ma_Dang_Nhap AND Kho_ID=@Kho_ID AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51052,N'User đã được phân quyền kho này.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap,Kho_ID) VALUES(@Ma_Dang_Nhap,@Kho_ID); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE UPDATE dbo.tbl_DM_Kho_User SET Ma_Dang_Nhap=@Ma_Dang_Nhap,Kho_ID=@Kho_ID WHERE Auto_ID=@Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_List
    @Is_Receipt BIT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Is_Receipt = 1
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Ghi_Chu
        FROM dbo.tbl_XNK_Nhap_Kho h
        JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC;
    ELSE
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Ghi_Chu
        FROM dbo.tbl_XNK_Xuat_Kho h
        JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Detail_List
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT;
    IF @Is_Receipt = 1
    BEGIN
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID;
        IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        SELECT d.Auto_ID, d.Nhap_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia
        FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d
        JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
        JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
        WHERE d.Nhap_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
    END
    ELSE
    BEGIN
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        SELECT d.Auto_ID, d.Xuat_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia
        FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d
        JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
        JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
        WHERE d.Xuat_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Page
    @Is_Receipt BIT, @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100) = N'', @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    IF @Is_Receipt = 1
    BEGIN
        SELECT COUNT(*) AS Total_Count
        FROM dbo.tbl_XNK_Nhap_Kho h
        JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
          AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter);
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Ghi_Chu
        FROM dbo.tbl_XNK_Nhap_Kho h
        JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
          AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter)
        ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE
    BEGIN
        SELECT COUNT(*) AS Total_Count
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
          AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter);
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Ghi_Chu
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
          AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter)
        ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL,
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @So_Phieu_Nhap_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho, N'')));
    IF @So_Phieu_Nhap_Kho = N'' THROW 51100, N'Số phiếu nhập không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51101, N'Số phiếu nhập đã tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51102, N'Kho không hợp lệ.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID = @NCC_ID) THROW 51103, N'Nhà cung cấp không hợp lệ.', 1;
    IF @Ngay_Nhap_Kho IS NULL THROW 51104, N'Ngày nhập kho không được để trống.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu, Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@So_Phieu_Nhap_Kho, @Kho_ID, @NCC_ID, @Ngay_Nhap_Kho, 0, @Ghi_Chu, COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID) THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa phiếu đã Post.', 1;
        UPDATE dbo.tbl_XNK_Nhap_Kho SET So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho, Kho_ID = @Kho_ID, NCC_ID = @NCC_ID, Ngay_Nhap_Kho = @Ngay_Nhap_Kho, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL,
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @So_Phieu_Xuat_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho, N'')));
    IF @So_Phieu_Xuat_Kho = N'' THROW 51130, N'Số phiếu xuất không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51131, N'Số phiếu xuất đã tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF @Ngay_Xuat_Kho IS NULL THROW 51133, N'Ngày xuất kho không được để trống.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu, Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@So_Phieu_Xuat_Kho, @Kho_ID, @Ngay_Xuat_Kho, 0, @Ghi_Chu, COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID) THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa phiếu đã Post.', 1;
        UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho, Kho_ID = @Kho_ID, Ngay_Xuat_Kho = @Ngay_Xuat_Kho, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL,
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Nhap_Kho_ID);
    IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Nhap_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51106, N'Sản phẩm không hợp lệ.', 1;
    IF @SL_Nhap <= 0 THROW 51107, N'Số lượng nhập phải lớn hơn 0.', 1;
    IF @Don_Gia_Nhap <= 0 THROW 51108, N'Đơn giá nhập phải lớn hơn 0.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap, Created, Last_Updated, Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@Nhap_Kho_ID, @San_Pham_ID, @SL_Nhap, @Don_Gia_Nhap, SYSUTCDATETIME(), SYSUTCDATETIME(), COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID AND (Nhap_Kho_ID <> @Nhap_Kho_ID OR San_Pham_ID <> @San_Pham_ID)) THROW 51110, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
        UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap = @SL_Nhap, Don_Gia_Nhap = @Don_Gia_Nhap, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    EXEC dbo.sp_XNK_Validate_All_Balances;
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL,
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Xuat_Kho_ID);
    IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Xuat_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51135, N'Sản phẩm không hợp lệ.', 1;
    IF @SL_Xuat <= 0 THROW 51136, N'Số lượng xuất phải lớn hơn 0.', 1;
    IF @Don_Gia_Xuat <= 0 THROW 51137, N'Đơn giá xuất phải lớn hơn 0.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat, Created, Last_Updated, Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@Xuat_Kho_ID, @San_Pham_ID, @SL_Xuat, @Don_Gia_Xuat, SYSUTCDATETIME(), SYSUTCDATETIME(), COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID AND (Xuat_Kho_ID <> @Xuat_Kho_ID OR San_Pham_ID <> @San_Pham_ID)) THROW 51138, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
        UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat = @SL_Xuat, Don_Gia_Xuat = @Don_Gia_Xuat, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    EXEC dbo.sp_XNK_Validate_All_Balances;
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL,
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @So_Phieu_Nhap_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho, N'')));
    IF @So_Phieu_Nhap_Kho = N'' THROW 51100, N'Số phiếu nhập không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51101, N'Số phiếu nhập đã tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51102, N'Kho không hợp lệ.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID = @NCC_ID) THROW 51103, N'Nhà cung cấp không hợp lệ.', 1;
    IF @Ngay_Nhap_Kho IS NULL THROW 51104, N'Ngày nhập kho không được để trống.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu, Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@So_Phieu_Nhap_Kho, @Kho_ID, @NCC_ID, @Ngay_Nhap_Kho, 0, @Ghi_Chu, COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID) THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa phiếu đã Post.', 1;
        UPDATE dbo.tbl_XNK_Nhap_Kho SET So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho, Kho_ID = @Kho_ID, NCC_ID = @NCC_ID, Ngay_Nhap_Kho = @Ngay_Nhap_Kho, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL,
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SET @So_Phieu_Xuat_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho, N'')));
    IF @So_Phieu_Xuat_Kho = N'' THROW 51130, N'Số phiếu xuất không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51131, N'Số phiếu xuất đã tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF @Ngay_Xuat_Kho IS NULL THROW 51133, N'Ngày xuất kho không được để trống.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu, Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@So_Phieu_Xuat_Kho, @Kho_ID, @Ngay_Xuat_Kho, 0, @Ghi_Chu, COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID) THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa phiếu đã Post.', 1;
        UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho, Kho_ID = @Kho_ID, Ngay_Xuat_Kho = @Ngay_Xuat_Kho, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL,
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Nhap_Kho_ID);
    IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Nhap_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51106, N'Sản phẩm không hợp lệ.', 1;
    IF @SL_Nhap <= 0 THROW 51107, N'Số lượng nhập phải lớn hơn 0.', 1;
    IF @Don_Gia_Nhap <= 0 THROW 51108, N'Đơn giá nhập phải lớn hơn 0.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap, Created, Last_Updated, Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@Nhap_Kho_ID, @San_Pham_ID, @SL_Nhap, @Don_Gia_Nhap, SYSUTCDATETIME(), SYSUTCDATETIME(), COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID AND (Nhap_Kho_ID <> @Nhap_Kho_ID OR San_Pham_ID <> @San_Pham_ID)) THROW 51110, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
        UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap = @SL_Nhap, Don_Gia_Nhap = @Don_Gia_Nhap, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    EXEC dbo.sp_XNK_Validate_All_Balances;
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL,
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Xuat_Kho_ID);
    IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Xuat_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51135, N'Sản phẩm không hợp lệ.', 1;
    IF @SL_Xuat <= 0 THROW 51136, N'Số lượng xuất phải lớn hơn 0.', 1;
    IF @Don_Gia_Xuat <= 0 THROW 51137, N'Đơn giá xuất phải lớn hơn 0.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat, Created, Last_Updated, Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@Xuat_Kho_ID, @San_Pham_ID, @SL_Xuat, @Don_Gia_Xuat, SYSUTCDATETIME(), SYSUTCDATETIME(), COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID AND (Xuat_Kho_ID <> @Xuat_Kho_ID OR San_Pham_ID <> @San_Pham_ID)) THROW 51138, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
        UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat = @SL_Xuat, Don_Gia_Xuat = @Don_Gia_Xuat, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    EXEC dbo.sp_XNK_Validate_All_Balances;
    SELECT @Auto_ID AS Auto_ID;
END
GO

/* Warehouse-scope authorization contract. The login is supplied by the
   authenticated server session; an empty or unmapped login is deny-by-default. */
CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Ensure_Access
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    IF @Kho_ID IS NULL OR @Kho_ID = 0 OR NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID)
        THROW 51051, N'Kho không hợp lệ.', 1;
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.tbl_DM_Kho_User ku
        JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = ku.Ma_Dang_Nhap
        WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = @Kho_ID
    )
        THROW 51054, N'User không có quyền thao tác trên kho này.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_List_Allowed
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap)
        THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    SELECT k.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, k.Ten_Kho AS Name
    FROM dbo.tbl_DM_Kho_User ku
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = ku.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
    ORDER BY k.Ten_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_User_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_Dang_Nhap AS Code, Ho_Ten AS Name
    FROM dbo.tbl_Sys_Thanh_Vien
    ORDER BY Ma_Dang_Nhap;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_List
    @Search_Text NVARCHAR(100) = N''
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    SELECT u.Auto_ID, u.Ma_Dang_Nhap AS Login_Name, tv.Ho_Ten AS Name, k.Ten_Kho AS Ghi_Chu,
           u.Kho_ID AS Related_ID, CAST(N'' AS NVARCHAR(100)) AS Code,
           CAST(0 AS BIGINT) AS Related_ID_2
    FROM dbo.tbl_DM_Kho_User u
    LEFT JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = u.Ma_Dang_Nhap
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = u.Kho_ID
    WHERE ISNULL(@Search_Text, N'') = N'' OR u.Ma_Dang_Nhap LIKE @Filter OR tv.Ho_Ten LIKE @Filter OR k.Ten_Kho LIKE @Filter
    ORDER BY u.Ma_Dang_Nhap, k.Ten_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Page
    @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100) = N''
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    SELECT COUNT(*) AS Total_Count
    FROM dbo.tbl_DM_Kho_User u
    LEFT JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = u.Ma_Dang_Nhap
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = u.Kho_ID
    WHERE ISNULL(@Search_Text, N'') = N'' OR u.Ma_Dang_Nhap LIKE @Filter OR tv.Ho_Ten LIKE @Filter OR k.Ten_Kho LIKE @Filter;
    SELECT u.Auto_ID, u.Ma_Dang_Nhap AS Login_Name, tv.Ho_Ten AS Name, k.Ten_Kho AS Ghi_Chu,
           u.Kho_ID AS Related_ID, CAST(N'' AS NVARCHAR(100)) AS Code,
           CAST(0 AS BIGINT) AS Related_ID_2
    FROM dbo.tbl_DM_Kho_User u
    LEFT JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = u.Ma_Dang_Nhap
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = u.Kho_ID
    WHERE ISNULL(@Search_Text, N'') = N'' OR u.Ma_Dang_Nhap LIKE @Filter OR tv.Ho_Ten LIKE @Filter OR k.Ten_Kho LIKE @Filter
    ORDER BY u.Ma_Dang_Nhap, k.Ten_Kho
    OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Save
    @Auto_ID BIGINT OUTPUT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' THROW 51050, N'Mã đăng nhập không được để trống.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap)
        THROW 51055, N'Mã đăng nhập không tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID)
        THROW 51051, N'Kho không hợp lệ.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap AND Kho_ID = @Kho_ID AND Auto_ID <> ISNULL(@Auto_ID, 0))
        THROW 51052, N'User đã được phân quyền kho này.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@Ma_Dang_Nhap, @Kho_ID, COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), SYSUTCDATETIME(), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
        UPDATE dbo.tbl_DM_Kho_User
        SET Ma_Dang_Nhap = @Ma_Dang_Nhap, Kho_ID = @Kho_ID, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function
        WHERE Auto_ID = @Auto_ID;
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

/* Final Draft/Post overrides: this file contains legacy procedure definitions
   above, so lifecycle definitions must remain last. */
CREATE OR ALTER PROCEDURE dbo.sp_XNK_Validate_All_Balances
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @HasNegative BIT=0;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Nhap_Kho AS MovementDate,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Delta FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
        UNION ALL
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Xuat_Kho,CAST(-d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
    ), Daily AS (SELECT Kho_ID,San_Pham_ID,MovementDate,SUM(Delta) AS Delta FROM Movements GROUP BY Kho_ID,San_Pham_ID,MovementDate),
    Running AS (SELECT SUM(Delta) OVER(PARTITION BY Kho_ID,San_Pham_ID ORDER BY MovementDate ROWS UNBOUNDED PRECEDING) AS Balance FROM Daily)
    SELECT @HasNegative=CASE WHEN MIN(Balance)<0 THEN 1 ELSE 0 END FROM Running;
    IF @HasNegative=1 THROW 51120,N'Không thể Post vì tồn kho sẽ âm tại một thời điểm trong lịch sử.',1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @So_Phieu_Nhap_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho,N'')));
    IF @So_Phieu_Nhap_Kho=N'' THROW 51100,N'Số phiếu nhập không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51101,N'Số phiếu nhập đã tồn tại.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51102,N'Kho không hợp lệ.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID=@NCC_ID) THROW 51103,N'Nhà cung cấp không hợp lệ.',1;
    IF @Ngay_Nhap_Kho IS NULL THROW 51104,N'Ngày nhập kho không được để trống.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho,Kho_ID,NCC_ID,Ngay_Nhap_Kho,Is_Posted,Ghi_Chu) VALUES(@So_Phieu_Nhap_Kho,@Kho_ID,@NCC_ID,@Ngay_Nhap_Kho,0,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE BEGIN IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID) THROW 51105,N'Phiếu nhập không tồn tại.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID AND Is_Posted=1) THROW 51163,N'Không được sửa phiếu đã Post.',1; UPDATE dbo.tbl_XNK_Nhap_Kho SET So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho,Kho_ID=@Kho_ID,NCC_ID=@NCC_ID,Ngay_Nhap_Kho=@Ngay_Nhap_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @So_Phieu_Xuat_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho,N'')));
    IF @So_Phieu_Xuat_Kho=N'' THROW 51130,N'Số phiếu xuất không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51131,N'Số phiếu xuất đã tồn tại.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51132,N'Kho không hợp lệ.',1;
    IF @Ngay_Xuat_Kho IS NULL THROW 51133,N'Ngày xuất kho không được để trống.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho,Kho_ID,Ngay_Xuat_Kho,Is_Posted,Ghi_Chu) VALUES(@So_Phieu_Xuat_Kho,@Kho_ID,@Ngay_Xuat_Kho,0,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE BEGIN IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID) THROW 51134,N'Phiếu xuất không tồn tại.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID AND Is_Posted=1) THROW 51163,N'Không được sửa phiếu đã Post.',1; UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho,Kho_ID=@Kho_ID,Ngay_Xuat_Kho=@Ngay_Xuat_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Nhap_Kho_ID) THROW 51105,N'Phiếu nhập không tồn tại.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Nhap_Kho_ID AND Is_Posted=1) THROW 51163,N'Không được sửa chi tiết của phiếu đã Post.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51106,N'Sản phẩm không hợp lệ.',1;
    IF @SL_Nhap<=0 THROW 51107,N'Số lượng nhập phải lớn hơn 0.',1; IF @Don_Gia_Nhap<=0 THROW 51108,N'Đơn giá nhập phải lớn hơn 0.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID,San_Pham_ID,SL_Nhap,Don_Gia_Nhap) VALUES(@Nhap_Kho_ID,@San_Pham_ID,@SL_Nhap,@Don_Gia_Nhap); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Nhap_Kho_ID<>@Nhap_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51110,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap=@SL_Nhap,Don_Gia_Nhap=@Don_Gia_Nhap WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Xuat_Kho_ID) THROW 51134,N'Phiếu xuất không tồn tại.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Xuat_Kho_ID AND Is_Posted=1) THROW 51163,N'Không được sửa chi tiết của phiếu đã Post.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51135,N'Sản phẩm không hợp lệ.',1;
    IF @SL_Xuat<=0 THROW 51136,N'Số lượng xuất phải lớn hơn 0.',1; IF @Don_Gia_Xuat<=0 THROW 51137,N'Đơn giá xuất phải lớn hơn 0.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID,San_Pham_ID,SL_Xuat,Don_Gia_Xuat) VALUES(@Xuat_Kho_ID,@San_Pham_ID,@SL_Xuat,@Don_Gia_Xuat); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Xuat_Kho_ID<>@Xuat_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51138,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat=@SL_Xuat,Don_Gia_Xuat=@Don_Gia_Xuat WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END; IF @OwnTransaction=1 BEGIN TRANSACTION;
    BEGIN TRY
        SET @So_Phieu_Nhap_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho,N'')));
        IF @So_Phieu_Nhap_Kho=N'' THROW 51100,N'Số phiếu nhập không được để trống.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51101,N'Số phiếu nhập đã tồn tại.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51102,N'Kho không hợp lệ.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID=@NCC_ID) THROW 51103,N'Nhà cung cấp không hợp lệ.',1; IF @Ngay_Nhap_Kho IS NULL THROW 51104,N'Ngày nhập kho không được để trống.',1;
        IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho,Kho_ID,NCC_ID,Ngay_Nhap_Kho,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@So_Phieu_Nhap_Kho,@Kho_ID,@NCC_ID,@Ngay_Nhap_Kho,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
        ELSE UPDATE dbo.tbl_XNK_Nhap_Kho SET So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho,Kho_ID=@Kho_ID,NCC_ID=@NCC_ID,Ngay_Nhap_Kho=@Ngay_Nhap_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
        IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); EXEC dbo.sp_XNK_Validate_All_Balances; IF @OwnTransaction=1 COMMIT TRANSACTION; SELECT @Auto_ID AS Auto_ID;
    END TRY BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END; IF @OwnTransaction=1 BEGIN TRANSACTION;
    BEGIN TRY
        SET @So_Phieu_Xuat_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho,N'')));
        IF @So_Phieu_Xuat_Kho=N'' THROW 51130,N'Số phiếu xuất không được để trống.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51131,N'Số phiếu xuất đã tồn tại.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51132,N'Kho không hợp lệ.',1; IF @Ngay_Xuat_Kho IS NULL THROW 51133,N'Ngày xuất kho không được để trống.',1;
        IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho,Kho_ID,Ngay_Xuat_Kho,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@So_Phieu_Xuat_Kho,@Kho_ID,@Ngay_Xuat_Kho,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
        ELSE UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho,Kho_ID=@Kho_ID,Ngay_Xuat_Kho=@Ngay_Xuat_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
        IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); EXEC dbo.sp_XNK_Validate_All_Balances; IF @OwnTransaction=1 COMMIT TRANSACTION; SELECT @Auto_ID AS Auto_ID;
    END TRY BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END; IF @OwnTransaction=1 BEGIN TRANSACTION;
    BEGIN TRY
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Nhap_Kho_ID) THROW 51105,N'Phiếu nhập không tồn tại.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51106,N'Sản phẩm không hợp lệ.',1; IF @SL_Nhap<=0 THROW 51107,N'Số lượng nhập phải lớn hơn 0.',1; IF @Don_Gia_Nhap<=0 THROW 51108,N'Đơn giá nhập phải lớn hơn 0.',1;
        IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID,San_Pham_ID,SL_Nhap,Don_Gia_Nhap,Created,Last_Updated,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Nhap_Kho_ID,@San_Pham_ID,@SL_Nhap,@Don_Gia_Nhap,SYSUTCDATETIME(),SYSUTCDATETIME(),COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
        ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Nhap_Kho_ID<>@Nhap_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51110,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap=@SL_Nhap,Don_Gia_Nhap=@Don_Gia_Nhap,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID; END
        IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); EXEC dbo.sp_XNK_Validate_All_Balances; IF @OwnTransaction=1 COMMIT TRANSACTION; SELECT @Auto_ID AS Auto_ID;
    END TRY BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END; IF @OwnTransaction=1 BEGIN TRANSACTION;
    BEGIN TRY
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Xuat_Kho_ID) THROW 51134,N'Phiếu xuất không tồn tại.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51135,N'Sản phẩm không hợp lệ.',1; IF @SL_Xuat<=0 THROW 51136,N'Số lượng xuất phải lớn hơn 0.',1; IF @Don_Gia_Xuat<=0 THROW 51137,N'Đơn giá xuất phải lớn hơn 0.',1;
        IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID,San_Pham_ID,SL_Xuat,Don_Gia_Xuat,Created,Last_Updated,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Xuat_Kho_ID,@San_Pham_ID,@SL_Xuat,@Don_Gia_Xuat,SYSUTCDATETIME(),SYSUTCDATETIME(),COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
        ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Xuat_Kho_ID<>@Xuat_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51138,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat=@SL_Xuat,Don_Gia_Xuat=@Don_Gia_Xuat,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID; END
        IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); EXEC dbo.sp_XNK_Validate_All_Balances; IF @OwnTransaction=1 COMMIT TRANSACTION; SELECT @Auto_ID AS Auto_ID;
    END TRY BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Delete @Entity NVARCHAR(30), @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Entity=N'DonViTinh' DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'LoaiSanPham' DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'SanPham' DELETE FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'NCC' DELETE FROM dbo.tbl_DM_NCC WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'Kho' DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'KhoUser' DELETE FROM dbo.tbl_DM_Kho_User WHERE Auto_ID=@Auto_ID;
    ELSE THROW 51060,N'Loại danh mục không hợp lệ.',1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Header @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRANSACTION;
    BEGIN TRY DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID; EXEC dbo.sp_XNK_Validate_All_Balances; COMMIT TRANSACTION; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Header @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRANSACTION;
    BEGIN TRY DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID; EXEC dbo.sp_XNK_Validate_All_Balances; COMMIT TRANSACTION; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Detail @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRANSACTION;
    BEGIN TRY DELETE FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID=@Auto_ID; EXEC dbo.sp_XNK_Validate_All_Balances; COMMIT TRANSACTION; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Detail @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRANSACTION;
    BEGIN TRY DELETE FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID=@Auto_ID; EXEC dbo.sp_XNK_Validate_All_Balances; COMMIT TRANSACTION; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton @Tu_Ngay DATE, @Den_Ngay DATE
AS
BEGIN
    SET NOCOUNT ON; IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Nhap_Kho AS MovementDate,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity,CAST(0 AS DECIMAL(18,3)) AS OutQuantity FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID
        UNION ALL SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Xuat_Kho,CAST(0 AS DECIMAL(18,3)),CAST(d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID
    )
    SELECT m.Kho_ID,m.San_Pham_ID,p.Ma_San_Pham,p.Ten_San_Pham,SUM(CASE WHEN m.MovementDate<@Tu_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Dau_Ky,SUM(CASE WHEN m.MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN m.InQuantity ELSE 0 END) AS SL_Nhap,SUM(CASE WHEN m.MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN m.OutQuantity ELSE 0 END) AS SL_Xuat,SUM(CASE WHEN m.MovementDate<=@Den_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Cuoi_Ky
    FROM Movements m JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=m.San_Pham_ID WHERE m.MovementDate<=@Den_Ngay GROUP BY m.Kho_ID,m.San_Pham_ID,p.Ma_San_Pham,p.Ten_San_Pham ORDER BY p.Ma_San_Pham,m.Kho_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap @Tu_Ngay DATE, @Den_Ngay DATE
AS
BEGIN
    SET NOCOUNT ON; IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    SELECT h.Ngay_Nhap_Kho AS Ngay,h.So_Phieu_Nhap_Kho AS So_Phieu,n.Ten_NCC AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Nhap AS So_Luong,d.Don_Gia_Nhap AS Don_Gia,CAST(d.SL_Nhap*d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Nhap_Kho,h.So_Phieu_Nhap_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat @Tu_Ngay DATE, @Den_Ngay DATE
AS
BEGIN
    SET NOCOUNT ON; IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    SELECT h.Ngay_Xuat_Kho AS Ngay,h.So_Phieu_Xuat_Kho AS So_Phieu,CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Xuat AS So_Luong,d.Don_Gia_Xuat AS Don_Gia,CAST(d.SL_Xuat*d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Xuat_Kho,h.So_Phieu_Xuat_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Validate_All_Balances
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @HasNegative BIT = 0;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Nhap_Kho AS MovementDate, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity, CAST(0 AS DECIMAL(18,3)) AS OutQuantity
        FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID
        UNION ALL
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Xuat_Kho, CAST(0 AS DECIMAL(18,3)), CAST(d.SL_Xuat AS DECIMAL(18,3))
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID
    ), Daily AS
    (
        SELECT Kho_ID, San_Pham_ID, MovementDate, SUM(InQuantity-OutQuantity) AS Delta FROM Movements GROUP BY Kho_ID,San_Pham_ID,MovementDate
    ), Running AS
    (
        SELECT SUM(Delta) OVER(PARTITION BY Kho_ID,San_Pham_ID ORDER BY MovementDate ROWS UNBOUNDED PRECEDING) AS Balance FROM Daily
    )
    SELECT @HasNegative=CASE WHEN MIN(Balance)<0 THEN 1 ELSE 0 END FROM Running;
    IF @HasNegative=1 THROW 51120,N'Không thể lưu vì tồn kho sẽ âm tại một thời điểm trong lịch sử.',1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
    IF @OwnTransaction=1 BEGIN TRANSACTION;
    BEGIN TRY
        SET @So_Phieu_Nhap_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho,N'')));
        IF @So_Phieu_Nhap_Kho=N'' THROW 51100,N'Số phiếu nhập không được để trống.',1;
        IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51101,N'Số phiếu nhập đã tồn tại.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51102,N'Kho không hợp lệ.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID=@NCC_ID) THROW 51103,N'Nhà cung cấp không hợp lệ.',1;
        IF @Ngay_Nhap_Kho IS NULL THROW 51104,N'Ngày nhập kho không được để trống.',1;
        IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho,Kho_ID,NCC_ID,Ngay_Nhap_Kho,Ghi_Chu) VALUES(@So_Phieu_Nhap_Kho,@Kho_ID,@NCC_ID,@Ngay_Nhap_Kho,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.tbl_XNK_Nhap_Kho SET So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho,Kho_ID=@Kho_ID,NCC_ID=@NCC_ID,Ngay_Nhap_Kho=@Ngay_Nhap_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        IF @OwnTransaction=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END; IF @OwnTransaction=1 BEGIN TRANSACTION;
    BEGIN TRY
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Nhap_Kho_ID) THROW 51105,N'Phiếu nhập không tồn tại.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51106,N'Sản phẩm không hợp lệ.',1;
        IF @SL_Nhap<=0 THROW 51107,N'Số lượng nhập phải lớn hơn 0.',1;
        IF @Don_Gia_Nhap<=0 THROW 51108,N'Đơn giá nhập phải lớn hơn 0.',1;
        IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID,San_Pham_ID,SL_Nhap,Don_Gia_Nhap) VALUES(@Nhap_Kho_ID,@San_Pham_ID,@SL_Nhap,@Don_Gia_Nhap); SET @Auto_ID=SCOPE_IDENTITY(); END
        ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Nhap_Kho_ID<>@Nhap_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51110,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap=@SL_Nhap,Don_Gia_Nhap=@Don_Gia_Nhap WHERE Auto_ID=@Auto_ID; END
        EXEC dbo.sp_XNK_Validate_All_Balances; IF @OwnTransaction=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END; IF @OwnTransaction=1 BEGIN TRANSACTION;
    BEGIN TRY
        SET @So_Phieu_Xuat_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho,N'')));
        IF @So_Phieu_Xuat_Kho=N'' THROW 51130,N'Số phiếu xuất không được để trống.',1;
        IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51131,N'Số phiếu xuất đã tồn tại.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51132,N'Kho không hợp lệ.',1;
        IF @Ngay_Xuat_Kho IS NULL THROW 51133,N'Ngày xuất kho không được để trống.',1;
        IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho,Kho_ID,Ngay_Xuat_Kho,Ghi_Chu) VALUES(@So_Phieu_Xuat_Kho,@Kho_ID,@Ngay_Xuat_Kho,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho,Kho_ID=@Kho_ID,Ngay_Xuat_Kho=@Ngay_Xuat_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances; IF @OwnTransaction=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END; IF @OwnTransaction=1 BEGIN TRANSACTION;
    BEGIN TRY
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Xuat_Kho_ID) THROW 51134,N'Phiếu xuất không tồn tại.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51135,N'Sản phẩm không hợp lệ.',1;
        IF @SL_Xuat<=0 THROW 51136,N'Số lượng xuất phải lớn hơn 0.',1;
        IF @Don_Gia_Xuat<=0 THROW 51137,N'Đơn giá xuất phải lớn hơn 0.',1;
        IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID,San_Pham_ID,SL_Xuat,Don_Gia_Xuat) VALUES(@Xuat_Kho_ID,@San_Pham_ID,@SL_Xuat,@Don_Gia_Xuat); SET @Auto_ID=SCOPE_IDENTITY(); END
        ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Xuat_Kho_ID<>@Xuat_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51138,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat=@SL_Xuat,Don_Gia_Xuat=@Don_Gia_Xuat WHERE Auto_ID=@Auto_ID; END
        EXEC dbo.sp_XNK_Validate_All_Balances; IF @OwnTransaction=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton @Tu_Ngay DATE, @Den_Ngay DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Nhap_Kho AS MovementDate,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity,CAST(0 AS DECIMAL(18,3)) AS OutQuantity FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID
        UNION ALL
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Xuat_Kho,CAST(0 AS DECIMAL(18,3)),CAST(d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID
    )
    SELECT m.Kho_ID,m.San_Pham_ID,p.Ma_San_Pham,p.Ten_San_Pham,
           SUM(CASE WHEN m.MovementDate<@Tu_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Dau_Ky,
           SUM(CASE WHEN m.MovementDate>=@Tu_Ngay AND m.MovementDate<=@Den_Ngay THEN m.InQuantity ELSE 0 END) AS SL_Nhap,
           SUM(CASE WHEN m.MovementDate>=@Tu_Ngay AND m.MovementDate<=@Den_Ngay THEN m.OutQuantity ELSE 0 END) AS SL_Xuat,
           SUM(CASE WHEN m.MovementDate<=@Den_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Cuoi_Ky
    FROM Movements m JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=m.San_Pham_ID
    WHERE m.MovementDate<=@Den_Ngay GROUP BY m.Kho_ID,m.San_Pham_ID,p.Ma_San_Pham,p.Ten_San_Pham ORDER BY p.Ma_San_Pham,m.Kho_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap @Tu_Ngay DATE, @Den_Ngay DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    SELECT h.Ngay_Nhap_Kho AS Ngay,h.So_Phieu_Nhap_Kho AS So_Phieu,n.Ten_NCC AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Nhap AS So_Luong,d.Don_Gia_Nhap AS Don_Gia,CAST(d.SL_Nhap*d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Nhap_Kho,h.So_Phieu_Nhap_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat @Tu_Ngay DATE, @Den_Ngay DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    SELECT h.Ngay_Xuat_Kho AS Ngay,h.So_Phieu_Xuat_Kho AS So_Phieu,CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Xuat AS So_Luong,d.Don_Gia_Xuat AS Don_Gia,CAST(d.SL_Xuat*d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Xuat_Kho,h.So_Phieu_Xuat_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Delete @Entity NVARCHAR(30), @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Entity=N'DonViTinh' DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'LoaiSanPham' DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'SanPham' DELETE FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'NCC' DELETE FROM dbo.tbl_DM_NCC WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'Kho' DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'KhoUser' DELETE FROM dbo.tbl_DM_Kho_User WHERE Auto_ID=@Auto_ID;
    ELSE THROW 51060,N'Loại danh mục không hợp lệ.',1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Header @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
    IF @OwnTransaction=1 BEGIN TRANSACTION;
    ELSE SAVE TRANSACTION WarehouseDelete;
    BEGIN TRY
        DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        IF @OwnTransaction=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        ELSE IF XACT_STATE()=1 ROLLBACK TRANSACTION WarehouseDelete;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Header @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
    IF @OwnTransaction=1 BEGIN TRANSACTION;
    ELSE SAVE TRANSACTION WarehouseDelete;
    BEGIN TRY
        DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        IF @OwnTransaction=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        ELSE IF XACT_STATE()=1 ROLLBACK TRANSACTION WarehouseDelete;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Detail @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
    IF @OwnTransaction=1 BEGIN TRANSACTION;
    ELSE SAVE TRANSACTION WarehouseDelete;
    BEGIN TRY
        DELETE FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID=@Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        IF @OwnTransaction=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        ELSE IF XACT_STATE()=1 ROLLBACK TRANSACTION WarehouseDelete;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Detail @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    DECLARE @OwnTransaction BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END;
    IF @OwnTransaction=1 BEGIN TRANSACTION;
    ELSE SAVE TRANSACTION WarehouseDelete;
    BEGIN TRY
        DELETE FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID=@Auto_ID;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        IF @OwnTransaction=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction=1 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION;
        ELSE IF XACT_STATE()=1 ROLLBACK TRANSACTION WarehouseDelete;
        THROW;
    END CATCH
END
GO

/*
   Canonical Warehouse read/write contract.
   These definitions intentionally stay in the existing DM/XNK/BC procedure
   groups so CSqlHelper callers do not carry business SQL.
*/
CREATE OR ALTER PROCEDURE dbo.sp_DM_Master_List @Entity NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Entity=N'DonViTinh'
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh;
    ELSE IF @Entity=N'LoaiSanPham'
        SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP;
    ELSE IF @Entity=N'SanPham'
        SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name, Loai_San_Pham_ID AS Related_ID, Don_Vi_Tinh_ID AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_San_Pham ORDER BY Ma_San_Pham;
    ELSE IF @Entity=N'NCC'
        SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_NCC ORDER BY Ma_NCC;
    ELSE IF @Entity=N'Kho'
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Kho ORDER BY Ten_Kho;
    ELSE IF @Entity=N'KhoUser'
        SELECT u.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, CAST(N'' AS NVARCHAR(255)) AS Name, u.Kho_ID AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, u.Ma_Dang_Nhap AS Login_Name, CAST(N'' AS NVARCHAR(1000)) AS Ghi_Chu FROM dbo.tbl_DM_Kho_User u ORDER BY u.Ma_Dang_Nhap, u.Kho_ID;
    ELSE THROW 51060, N'Loại danh mục không hợp lệ.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Lookup_List @Entity NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Entity=N'DonViTinh' SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh;
    ELSE IF @Entity=N'LoaiSanPham' SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP;
    ELSE IF @Entity=N'SanPham' SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name FROM dbo.tbl_DM_San_Pham ORDER BY Ma_San_Pham;
    ELSE IF @Entity=N'NCC' SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name FROM dbo.tbl_DM_NCC ORDER BY Ma_NCC;
    ELSE IF @Entity=N'Kho' SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name FROM dbo.tbl_DM_Kho ORDER BY Ten_Kho;
    ELSE THROW 51060, N'Loại danh mục không hợp lệ.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_List @Is_Receipt BIT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Is_Receipt=1
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Ghi_Chu FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC;
    ELSE
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Ghi_Chu FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Detail_List @Is_Receipt BIT, @Document_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Is_Receipt=1
        SELECT d.Auto_ID, d.Nhap_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID=p.Don_Vi_Tinh_ID WHERE d.Nhap_Kho_ID=@Document_ID ORDER BY d.Auto_ID;
    ELSE
        SELECT d.Auto_ID, d.Xuat_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID=p.Don_Vi_Tinh_ID WHERE d.Xuat_Kho_ID=@Document_ID ORDER BY d.Auto_ID;
END
GO

/* Server-side paging variants of the canonical lists/reports. Each returns two result
   sets: Total_Count first, then the requested page (OFFSET/FETCH). Page_From_Procedure
   in CWarehouse_Controller_Base consumes them; TelerikGrid OnRead drives the page number. */
CREATE OR ALTER PROCEDURE dbo.sp_DM_Master_Page
    @Entity NVARCHAR(30), @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100)=N''
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number<1 SET @Page_Number=1;
    IF @Page_Size<1 SET @Page_Size=10;
    DECLARE @Filter NVARCHAR(260)=N'%' + @Search_Text + N'%';
    IF @Entity=N'DonViTinh'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter ORDER BY Ten_Don_Vi_Tinh OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'LoaiSanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter;
        SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter ORDER BY Ma_LSP OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'SanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter;
        SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name, Loai_San_Pham_ID AS Related_ID, Don_Vi_Tinh_ID AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter ORDER BY Ma_San_Pham OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'NCC'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter;
        SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter ORDER BY Ma_NCC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'Kho'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter ORDER BY Ten_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'KhoUser'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Kho_User u WHERE @Search_Text=N'' OR u.Ma_Dang_Nhap LIKE @Filter;
        SELECT u.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, CAST(N'' AS NVARCHAR(255)) AS Name, u.Kho_ID AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, u.Ma_Dang_Nhap AS Login_Name, CAST(N'' AS NVARCHAR(1000)) AS Ghi_Chu FROM dbo.tbl_DM_Kho_User u WHERE @Search_Text=N'' OR u.Ma_Dang_Nhap LIKE @Filter ORDER BY u.Ma_Dang_Nhap, u.Kho_ID OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE THROW 51060, N'Loại danh mục không hợp lệ.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Lookup_Page
    @Entity NVARCHAR(30), @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100)=N''
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number<1 SET @Page_Number=1;
    IF @Page_Size<1 SET @Page_Size=10;
    DECLARE @Filter NVARCHAR(260)=N'%' + @Search_Text + N'%';
    IF @Entity=N'DonViTinh'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name FROM dbo.tbl_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter ORDER BY Ten_Don_Vi_Tinh OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'LoaiSanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter;
        SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name FROM dbo.tbl_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter ORDER BY Ma_LSP OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'SanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter;
        SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name FROM dbo.tbl_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter ORDER BY Ma_San_Pham OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'NCC'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter;
        SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name FROM dbo.tbl_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter ORDER BY Ma_NCC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'Kho'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name FROM dbo.tbl_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter ORDER BY Ten_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE THROW 51060, N'Loại danh mục không hợp lệ.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Page
    @Is_Receipt BIT, @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100)=N''
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number<1 SET @Page_Number=1;
    IF @Page_Size<1 SET @Page_Size=10;
    DECLARE @Filter NVARCHAR(260)=N'%' + @Search_Text + N'%';
    IF @Is_Receipt=1
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID WHERE @Search_Text=N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter;
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Ghi_Chu FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID WHERE @Search_Text=N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID WHERE @Search_Text=N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter;
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Ghi_Chu FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID WHERE @Search_Text=N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap_Page @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    IF @Page_Number<1 SET @Page_Number=1;
    IF @Page_Size<1 SET @Page_Size=10;
    SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay;
    SELECT h.Ngay_Nhap_Kho AS Ngay,h.So_Phieu_Nhap_Kho AS So_Phieu,n.Ten_NCC AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Nhap AS So_Luong,d.Don_Gia_Nhap AS Don_Gia,CAST(d.SL_Nhap*d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Nhap_Kho,h.So_Phieu_Nhap_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat_Page @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    IF @Page_Number<1 SET @Page_Number=1;
    IF @Page_Size<1 SET @Page_Size=10;
    SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay;
    SELECT h.Ngay_Xuat_Kho AS Ngay,h.So_Phieu_Xuat_Kho AS So_Phieu,CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Xuat AS So_Luong,d.Don_Gia_Xuat AS Don_Gia,CAST(d.SL_Xuat*d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Xuat_Kho,h.So_Phieu_Xuat_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton_Page @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    IF @Page_Number<1 SET @Page_Number=1;
    IF @Page_Size<1 SET @Page_Size=10;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Nhap_Kho AS MovementDate,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity,CAST(0 AS DECIMAL(18,3)) AS OutQuantity FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID
        UNION ALL
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Xuat_Kho,CAST(0 AS DECIMAL(18,3)),CAST(d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID
    )
    SELECT m.Kho_ID,m.San_Pham_ID,k.Ten_Kho AS Ten_Kho,p.Ma_San_Pham,p.Ten_San_Pham,
           SUM(CASE WHEN m.MovementDate<@Tu_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Dau_Ky,
           SUM(CASE WHEN m.MovementDate>=@Tu_Ngay AND m.MovementDate<=@Den_Ngay THEN m.InQuantity ELSE 0 END) AS SL_Nhap,
           SUM(CASE WHEN m.MovementDate>=@Tu_Ngay AND m.MovementDate<=@Den_Ngay THEN m.OutQuantity ELSE 0 END) AS SL_Xuat,
           SUM(CASE WHEN m.MovementDate<=@Den_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Cuoi_Ky
    INTO #Aggregated
    FROM Movements m JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=m.Kho_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=m.San_Pham_ID
    WHERE m.MovementDate<=@Den_Ngay GROUP BY m.Kho_ID,m.San_Pham_ID,k.Ten_Kho,p.Ma_San_Pham,p.Ten_San_Pham;

    SELECT COUNT(*) AS Total_Count FROM #Aggregated;
    SELECT Kho_ID,Ten_Kho,San_Pham_ID,Ma_San_Pham,Ten_San_Pham,SL_Dau_Ky,SL_Nhap,SL_Xuat,SL_Cuoi_Ky FROM #Aggregated ORDER BY Ma_San_Pham,Kho_ID OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    DROP TABLE #Aggregated;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Don_Vi_Tinh_Save
    @Auto_ID BIGINT OUTPUT, @Ten_Don_Vi_Tinh NVARCHAR(200), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ten_Don_Vi_Tinh=LTRIM(RTRIM(ISNULL(@Ten_Don_Vi_Tinh,N'')));
    IF @Ten_Don_Vi_Tinh=N'' THROW 51001,N'Tên đơn vị tính không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI=@Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51002,N'Tên đơn vị tính đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ten_Don_Vi_Tinh,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_Don_Vi_Tinh SET Ten_Don_Vi_Tinh=@Ten_Don_Vi_Tinh,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Loai_San_Pham_Save
    @Auto_ID BIGINT OUTPUT, @Ma_LSP NVARCHAR(100), @Ten_LSP NVARCHAR(200), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ma_LSP=LTRIM(RTRIM(ISNULL(@Ma_LSP,N''))); SET @Ten_LSP=LTRIM(RTRIM(ISNULL(@Ten_LSP,N'')));
    IF @Ma_LSP=N'' THROW 51010,N'Mã loại sản phẩm không được để trống.',1;
    IF @Ten_LSP=N'' THROW 51012,N'Tên loại sản phẩm không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE (Ma_LSP=@Ma_LSP OR Ten_LSP=@Ten_LSP) AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51011,N'Mã hoặc tên loại sản phẩm đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP,Ten_LSP,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ma_LSP,@Ten_LSP,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_Loai_San_Pham SET Ma_LSP=@Ma_LSP,Ten_LSP=@Ten_LSP,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
END
GO

/* FINAL WAREHOUSE AUTHORIZATION OVERRIDES. Keep this section last because this
   file contains legacy definitions for the same procedure names. */

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    SELECT h.Ngay_Nhap_Kho AS Ngay, h.So_Phieu_Nhap_Kho AS So_Phieu, n.Ten_NCC AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia, CAST(d.SL_Nhap * d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    SELECT h.Ngay_Xuat_Kho AS Ngay, h.So_Phieu_Xuat_Kho AS So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia, CAST(d.SL_Xuat * d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Nhap_Kho AS MovementDate, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity, CAST(0 AS DECIMAL(18,3)) AS OutQuantity FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Ngay_Nhap_Kho <= @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        UNION ALL
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Xuat_Kho, CAST(0 AS DECIMAL(18,3)), CAST(d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Ngay_Xuat_Kho <= @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ), Aggregated AS
    (
        SELECT Kho_ID, San_Pham_ID, SUM(CASE WHEN MovementDate < @Tu_Ngay THEN InQuantity - OutQuantity ELSE 0 END) AS SL_Dau_Ky, SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN InQuantity ELSE 0 END) AS SL_Nhap, SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN OutQuantity ELSE 0 END) AS SL_Xuat FROM Movements GROUP BY Kho_ID, San_Pham_ID
    )
    SELECT a.Kho_ID, k.Ten_Kho, a.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, a.SL_Dau_Ky, a.SL_Nhap, a.SL_Xuat, a.SL_Dau_Ky + a.SL_Nhap - a.SL_Xuat AS SL_Cuoi_Ky,
           ISNULL(b.CurrentQuantity, 0) AS SL_Ton_Thuc_Te, ISNULL(b.ReservedQuantity, 0) AS SL_Dang_Giu,
           ISNULL(b.CurrentQuantity, 0) - ISNULL(b.ReservedQuantity, 0) AS SL_Kha_Dung
    FROM Aggregated a
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = a.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = a.San_Pham_ID
    LEFT JOIN dbo.InventoryBalance_Current b ON b.Kho_ID = a.Kho_ID AND b.San_Pham_ID = a.San_Pham_ID
    ORDER BY k.Ten_Kho, p.Ma_San_Pham;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID);
    SELECT h.Ngay_Nhap_Kho AS Ngay, h.So_Phieu_Nhap_Kho AS So_Phieu, n.Ten_NCC AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia, CAST(d.SL_Nhap * d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID);
    SELECT h.Ngay_Xuat_Kho AS Ngay, h.So_Phieu_Xuat_Kho AS So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia, CAST(d.SL_Xuat * d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Nhap_Kho AS MovementDate, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity, CAST(0 AS DECIMAL(18,3)) AS OutQuantity FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Ngay_Nhap_Kho <= @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        UNION ALL
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Xuat_Kho, CAST(0 AS DECIMAL(18,3)), CAST(d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Ngay_Xuat_Kho <= @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
    ), Aggregated AS
    (
        SELECT Kho_ID, San_Pham_ID, SUM(CASE WHEN MovementDate < @Tu_Ngay THEN InQuantity - OutQuantity ELSE 0 END) AS SL_Dau_Ky, SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN InQuantity ELSE 0 END) AS SL_Nhap, SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN OutQuantity ELSE 0 END) AS SL_Xuat FROM Movements GROUP BY Kho_ID, San_Pham_ID
    )
    SELECT a.Kho_ID, k.Ten_Kho, a.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, a.SL_Dau_Ky, a.SL_Nhap, a.SL_Xuat, a.SL_Dau_Ky + a.SL_Nhap - a.SL_Xuat AS SL_Cuoi_Ky,
           ISNULL(b.CurrentQuantity, 0) AS SL_Ton_Thuc_Te, ISNULL(b.ReservedQuantity, 0) AS SL_Dang_Giu,
           ISNULL(b.CurrentQuantity, 0) - ISNULL(b.ReservedQuantity, 0) AS SL_Kha_Dung INTO #WarehouseScopeResult_Final
    FROM Aggregated a
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = a.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = a.San_Pham_ID
    LEFT JOIN dbo.InventoryBalance_Current b ON b.Kho_ID = a.Kho_ID AND b.San_Pham_ID = a.San_Pham_ID;
    SELECT COUNT(*) AS Total_Count FROM #WarehouseScopeResult_Final;
    SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham, SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky, SL_Ton_Thuc_Te, SL_Dang_Giu, SL_Kha_Dung
    FROM #WarehouseScopeResult_Final ORDER BY Ten_Kho, Ma_San_Pham OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET @So_Phieu_Nhap_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho, N'')));
    IF @So_Phieu_Nhap_Kho = N'' THROW 51100, N'Số phiếu nhập không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51101, N'Số phiếu nhập đã tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51102, N'Kho không hợp lệ.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID = @NCC_ID) THROW 51103, N'Nhà cung cấp không hợp lệ.', 1;
    IF @Ngay_Nhap_Kho IS NULL THROW 51104, N'Ngày nhập kho không được để trống.', 1;
    IF ISNULL(@Auto_ID, 0) = 0 INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES(@So_Phieu_Nhap_Kho, @Kho_ID, @NCC_ID, @Ngay_Nhap_Kho, 0, @Ghi_Chu);
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID) THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa phiếu đã Post.', 1;
        UPDATE dbo.tbl_XNK_Nhap_Kho SET So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho, Kho_ID = @Kho_ID, NCC_ID = @NCC_ID, Ngay_Nhap_Kho = @Ngay_Nhap_Kho, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET @So_Phieu_Xuat_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho, N'')));
    IF @So_Phieu_Xuat_Kho = N'' THROW 51130, N'Số phiếu xuất không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51131, N'Số phiếu xuất đã tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF @Ngay_Xuat_Kho IS NULL THROW 51133, N'Ngày xuất kho không được để trống.', 1;
    IF ISNULL(@Auto_ID, 0) = 0 INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES(@So_Phieu_Xuat_Kho, @Kho_ID, @Ngay_Xuat_Kho, 0, @Ghi_Chu);
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID) THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa phiếu đã Post.', 1;
        UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho, Kho_ID = @Kho_ID, Ngay_Xuat_Kho = @Ngay_Xuat_Kho, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Nhap_Kho_ID);
    IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Nhap_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51106, N'Sản phẩm không hợp lệ.', 1;
    IF @SL_Nhap <= 0 THROW 51107, N'Số lượng nhập phải lớn hơn 0.', 1;
    IF @Don_Gia_Nhap <= 0 THROW 51108, N'Đơn giá nhập phải lớn hơn 0.', 1;
    IF ISNULL(@Auto_ID, 0) = 0 INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES(@Nhap_Kho_ID, @San_Pham_ID, @SL_Nhap, @Don_Gia_Nhap);
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID AND (Nhap_Kho_ID <> @Nhap_Kho_ID OR San_Pham_ID <> @San_Pham_ID)) THROW 51110, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
        UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap = @SL_Nhap, Don_Gia_Nhap = @Don_Gia_Nhap WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Xuat_Kho_ID);
    IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Xuat_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51135, N'Sản phẩm không hợp lệ.', 1;
    IF @SL_Xuat <= 0 THROW 51136, N'Số lượng xuất phải lớn hơn 0.', 1;
    IF @Don_Gia_Xuat <= 0 THROW 51137, N'Đơn giá xuất phải lớn hơn 0.', 1;
    IF ISNULL(@Auto_ID, 0) = 0 INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES(@Xuat_Kho_ID, @San_Pham_ID, @SL_Xuat, @Don_Gia_Xuat);
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID AND (Xuat_Kho_ID <> @Xuat_Kho_ID OR San_Pham_ID <> @San_Pham_ID)) THROW 51138, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
        UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat = @SL_Xuat, Don_Gia_Xuat = @Don_Gia_Xuat WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID);
    IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa phiếu đã Post.', 1;
    DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID);
    IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa phiếu đã Post.', 1;
    DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT = (SELECT h.Kho_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE d.Auto_ID = @Auto_ID);
    IF @Kho_ID IS NULL THROW 51105, N'Chi tiết phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    DELETE FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT = (SELECT h.Kho_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE d.Auto_ID = @Auto_ID);
    IF @Kho_ID IS NULL THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    DELETE FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Post
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        CREATE TABLE #Delta(Kho_ID BIGINT NOT NULL, San_Pham_ID BIGINT NOT NULL, Delta DECIMAL(18,3) NOT NULL, PRIMARY KEY(Kho_ID, San_Pham_ID));
        IF @Is_Receipt = 1
        BEGIN
            DECLARE @ReceiptWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            IF @ReceiptWarehouse IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @ReceiptWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(d.SL_Nhap AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
            UPDATE dbo.tbl_XNK_Nhap_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
        END
        ELSE
        BEGIN
            DECLARE @IssueWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            IF @IssueWarehouse IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @IssueWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(-d.SL_Xuat AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
        END
        IF EXISTS (SELECT 1 FROM #Delta d LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) ON b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID WHERE ISNULL(b.CurrentQuantity, 0) + d.Delta < 0) THROW 51120, N'Không thể Post vì tồn kho không đủ.', 1;
        UPDATE b SET CurrentQuantity = b.CurrentQuantity + d.Delta, UpdatedAt = SYSUTCDATETIME() FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) JOIN #Delta d ON d.Kho_ID = b.Kho_ID AND d.San_Pham_ID = b.San_Pham_ID;
        INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity) SELECT d.Kho_ID, d.San_Pham_ID, d.Delta FROM #Delta d WHERE NOT EXISTS (SELECT 1 FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) WHERE b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID);
        EXEC dbo.sp_XNK_Validate_All_Balances;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Ensure_Access
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    IF @Kho_ID IS NULL OR @Kho_ID = 0 OR NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51051, N'Kho không hợp lệ.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.tbl_DM_Kho_User ku
        JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = ku.Ma_Dang_Nhap
        WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = @Kho_ID
    ) THROW 51054, N'User không có quyền thao tác trên kho này.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_List_Allowed
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' OR NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap) THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    SELECT k.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, k.Ten_Kho AS Name
    FROM dbo.tbl_DM_Kho_User ku JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = ku.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap ORDER BY k.Ten_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_User_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_Dang_Nhap AS Code, Ho_Ten AS Name FROM dbo.tbl_Sys_Thanh_Vien ORDER BY Ma_Dang_Nhap;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Post
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        CREATE TABLE #Delta(Kho_ID BIGINT NOT NULL, San_Pham_ID BIGINT NOT NULL, Delta DECIMAL(18,3) NOT NULL, PRIMARY KEY(Kho_ID, San_Pham_ID));
        IF @Is_Receipt = 1
        BEGIN
            DECLARE @ReceiptWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            IF @ReceiptWarehouse IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @ReceiptWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(d.SL_Nhap AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
            UPDATE dbo.tbl_XNK_Nhap_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
        END
        ELSE
        BEGIN
            DECLARE @IssueWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            IF @IssueWarehouse IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @IssueWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(-d.SL_Xuat AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
            EXEC dbo.sp_XNK_Reservation_Release_Document @Document_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
            IF EXISTS
            (
                SELECT 1 FROM #Delta d
                LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) ON b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID
                WHERE ISNULL(b.CurrentQuantity, 0) - ISNULL(b.ReservedQuantity, 0) < -d.Delta
            ) THROW 51120, N'Không thể Post vì tồn khả dụng không đủ.', 1;
        END
        IF EXISTS (SELECT 1 FROM #Delta d LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) ON b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID WHERE ISNULL(b.CurrentQuantity, 0) + d.Delta < 0) THROW 51120, N'Không thể Post vì tồn kho không đủ.', 1;
        UPDATE b SET CurrentQuantity = b.CurrentQuantity + d.Delta, UpdatedAt = SYSUTCDATETIME()
        FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) JOIN #Delta d ON d.Kho_ID = b.Kho_ID AND d.San_Pham_ID = b.San_Pham_ID;
        INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity)
        SELECT d.Kho_ID, d.San_Pham_ID, d.Delta, 0 FROM #Delta d
        WHERE NOT EXISTS (SELECT 1 FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) WHERE b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID);
        EXEC dbo.sp_XNK_Validate_All_Balances;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_InventoryBalance_Rebuild
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DELETE FROM dbo.InventoryBalance_Current;
        ;WITH Delta AS
        (
            SELECT h.Kho_ID, d.San_Pham_ID, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Amount FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Is_Posted = 1
            UNION ALL
            SELECT h.Kho_ID, d.San_Pham_ID, CAST(-d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Is_Posted = 1
        )
        INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity)
        SELECT Kho_ID, San_Pham_ID, SUM(Amount), 0 FROM Delta GROUP BY Kho_ID, San_Pham_ID;
        EXEC dbo.sp_XNK_Reservation_Rebuild;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Save
    @Auto_ID BIGINT OUTPUT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' THROW 51050, N'Mã đăng nhập không được để trống.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap) THROW 51055, N'Mã đăng nhập không tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51051, N'Kho không hợp lệ.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap AND Kho_ID = @Kho_ID AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51052, N'User đã được phân quyền kho này.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@Ma_Dang_Nhap, @Kho_ID, COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), SYSUTCDATETIME(), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
        UPDATE dbo.tbl_DM_Kho_User SET Ma_Dang_Nhap = @Ma_Dang_Nhap, Kho_ID = @Kho_ID, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_List
    @Search_Text NVARCHAR(100) = N''
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    SELECT u.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, tv.Ho_Ten AS Name, u.Ma_Dang_Nhap AS Login_Name, k.Ten_Kho AS Ghi_Chu, u.Kho_ID AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2
    FROM dbo.tbl_DM_Kho_User u LEFT JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = u.Ma_Dang_Nhap JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = u.Kho_ID
    WHERE ISNULL(@Search_Text, N'') = N'' OR u.Ma_Dang_Nhap LIKE @Filter OR tv.Ho_Ten LIKE @Filter OR k.Ten_Kho LIKE @Filter
    ORDER BY u.Ma_Dang_Nhap, k.Ten_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Page
    @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100) = N''
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    SELECT COUNT(*) AS Total_Count
    FROM dbo.tbl_DM_Kho_User u LEFT JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = u.Ma_Dang_Nhap JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = u.Kho_ID
    WHERE ISNULL(@Search_Text, N'') = N'' OR u.Ma_Dang_Nhap LIKE @Filter OR tv.Ho_Ten LIKE @Filter OR k.Ten_Kho LIKE @Filter;
    SELECT u.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, tv.Ho_Ten AS Name, u.Ma_Dang_Nhap AS Login_Name, k.Ten_Kho AS Ghi_Chu, u.Kho_ID AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2
    FROM dbo.tbl_DM_Kho_User u LEFT JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = u.Ma_Dang_Nhap JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = u.Kho_ID
    WHERE ISNULL(@Search_Text, N'') = N'' OR u.Ma_Dang_Nhap LIKE @Filter OR tv.Ho_Ten LIKE @Filter OR k.Ten_Kho LIKE @Filter
    ORDER BY u.Ma_Dang_Nhap, k.Ten_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_List
    @Is_Receipt BIT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Is_Receipt = 1
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
        FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC;
    ELSE
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Detail_List
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT;
    IF @Is_Receipt = 1
    BEGIN
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID;
        IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        SELECT d.Auto_ID, d.Nhap_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia
        FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
        WHERE d.Nhap_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
    END
    ELSE
    BEGIN
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        SELECT d.Auto_ID, d.Xuat_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia
        FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
        WHERE d.Xuat_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Page
    @Is_Receipt BIT, @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100) = N'', @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    IF @Is_Receipt = 1
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter);
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter)
        ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter);
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter)
        ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_San_Pham_Save
    @Auto_ID BIGINT OUTPUT, @Ma_San_Pham NVARCHAR(100), @Ten_San_Pham NVARCHAR(255), @Loai_San_Pham_ID BIGINT, @Don_Vi_Tinh_ID BIGINT, @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ma_San_Pham=LTRIM(RTRIM(ISNULL(@Ma_San_Pham,N''))); SET @Ten_San_Pham=LTRIM(RTRIM(ISNULL(@Ten_San_Pham,N'')));
    IF @Ma_San_Pham=N'' THROW 51020,N'Mã sản phẩm không được để trống.',1; IF @Ten_San_Pham=N'' THROW 51022,N'Tên sản phẩm không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham=@Ma_San_Pham AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51021,N'Mã sản phẩm đã tồn tại.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID=@Loai_San_Pham_ID) THROW 51023,N'Loại sản phẩm không hợp lệ.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID=@Don_Vi_Tinh_ID) THROW 51024,N'Đơn vị tính không hợp lệ.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham,Ten_San_Pham,Loai_San_Pham_ID,Don_Vi_Tinh_ID,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ma_San_Pham,@Ten_San_Pham,@Loai_San_Pham_ID,@Don_Vi_Tinh_ID,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_San_Pham SET Ma_San_Pham=@Ma_San_Pham,Ten_San_Pham=@Ten_San_Pham,Loai_San_Pham_ID=@Loai_San_Pham_ID,Don_Vi_Tinh_ID=@Don_Vi_Tinh_ID,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_NCC_Save
    @Auto_ID BIGINT OUTPUT, @Ma_NCC NVARCHAR(100), @Ten_NCC NVARCHAR(255), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ma_NCC=LTRIM(RTRIM(ISNULL(@Ma_NCC,N''))); SET @Ten_NCC=LTRIM(RTRIM(ISNULL(@Ten_NCC,N'')));
    IF @Ma_NCC=N'' THROW 51030,N'Mã nhà cung cấp không được để trống.',1; IF @Ten_NCC=N'' THROW 51032,N'Tên nhà cung cấp không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_NCC WHERE (Ma_NCC=@Ma_NCC OR Ten_NCC=@Ten_NCC) AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51031,N'Mã hoặc tên nhà cung cấp đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_NCC(Ma_NCC,Ten_NCC,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ma_NCC,@Ten_NCC,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_NCC SET Ma_NCC=@Ma_NCC,Ten_NCC=@Ten_NCC,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_Save
    @Auto_ID BIGINT OUTPUT, @Ten_Kho NVARCHAR(255), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ten_Kho=LTRIM(RTRIM(ISNULL(@Ten_Kho,N'')));
    IF @Ten_Kho=N'' THROW 51040,N'Tên kho không được để trống.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Ten_Kho=@Ten_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51041,N'Tên kho đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_Kho(Ten_Kho,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ten_Kho,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_Kho SET Ten_Kho=@Ten_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE,
    @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0 BEGIN TRANSACTION; SET @OwnTransaction = 1;
    BEGIN TRY
        SET @So_Phieu_Xuat_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho, N'')));
        IF @So_Phieu_Xuat_Kho = N'' THROW 51130, N'Số phiếu xuất không được để trống.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51131, N'Số phiếu xuất đã tồn tại.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Ngay_Xuat_Kho IS NULL THROW 51133, N'Ngày xuất kho không được để trống.', 1;

        DECLARE @Old_Kho_ID BIGINT, @Is_Posted BIT;
        IF ISNULL(@Auto_ID, 0) <> 0
        BEGIN
            SELECT @Old_Kho_ID = Kho_ID, @Is_Posted = Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Auto_ID;
            IF @Old_Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            IF @Is_Posted = 1 THROW 51163, N'Không được sửa phiếu đã Post.', 1;
            IF @Old_Kho_ID <> @Kho_ID EXEC dbo.sp_XNK_Reservation_Move_Document @Auto_ID, @Old_Kho_ID, @Kho_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho, Kho_ID = @Kho_ID, Ngay_Xuat_Kho = @Ngay_Xuat_Kho, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Auto_ID;
        END
        ELSE
            INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES(@So_Phieu_Xuat_Kho, @Kho_ID, @Ngay_Xuat_Kho, 0, @Ghi_Chu);
        IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0 BEGIN TRANSACTION; SET @OwnTransaction = 1;
    BEGIN TRY
        DECLARE @Kho_ID BIGINT, @Old_San_Pham_ID BIGINT, @Old_ReservedQuantity DECIMAL(18,3) = 0, @Reservation_Kho_ID BIGINT;
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Xuat_Kho_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
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
        IF ISNULL(@Auto_ID, 0) = 0
        BEGIN
            INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES(@Xuat_Kho_ID, @San_Pham_ID, @SL_Xuat, @Don_Gia_Xuat);
            SET @Auto_ID = SCOPE_IDENTITY();
            INSERT dbo.InventoryReservation_Current(Xuat_Kho_Detail_ID, Kho_ID, San_Pham_ID, ReservedQuantity) VALUES(@Auto_ID, @Kho_ID, @San_Pham_ID, @SL_Xuat);
        END
        ELSE
        BEGIN
            UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat = @SL_Xuat, Don_Gia_Xuat = @Don_Gia_Xuat WHERE Auto_ID = @Auto_ID;
            IF EXISTS (SELECT 1 FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @Auto_ID)
                UPDATE dbo.InventoryReservation_Current SET Kho_ID = @Kho_ID, San_Pham_ID = @San_Pham_ID, ReservedQuantity = @SL_Xuat, UpdatedAt = SYSUTCDATETIME() WHERE Xuat_Kho_Detail_ID = @Auto_ID;
            ELSE
                INSERT dbo.InventoryReservation_Current(Xuat_Kho_Detail_ID, Kho_ID, San_Pham_ID, ReservedQuantity) VALUES(@Auto_ID, @Kho_ID, @San_Pham_ID, @SL_Xuat);
        END
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT, @Document_ID BIGINT;
    SELECT @Kho_ID = h.Kho_ID, @Document_ID = h.Auto_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE d.Auto_ID = @Auto_ID;
    IF @Kho_ID IS NULL THROW 51105, N'Chi tiết phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
    DELETE dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT, @Document_ID BIGINT;
    SELECT @Kho_ID = h.Kho_ID, @Document_ID = h.Auto_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE d.Auto_ID = @Auto_ID;
    IF @Kho_ID IS NULL THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
    BEGIN TRANSACTION;
    BEGIN TRY
        EXEC dbo.sp_XNK_Reservation_Release_Detail @Auto_ID;
        DELETE dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT, @Is_Posted BIT;
    SELECT @Kho_ID = Kho_ID, @Is_Posted = Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID;
    IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF @Is_Posted = 1 THROW 51163, N'Không được xóa phiếu đã Post.', 1;
    BEGIN TRANSACTION;
    BEGIN TRY
        EXEC dbo.sp_XNK_Reservation_Release_Document @Auto_ID;
        DELETE dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Save
    @Auto_ID BIGINT OUTPUT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ma_Dang_Nhap=LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap,N'')));
    IF @Ma_Dang_Nhap=N'' THROW 51050,N'Mã đăng nhập không được để trống.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51051,N'Kho không hợp lệ.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap=@Ma_Dang_Nhap AND Kho_ID=@Kho_ID AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51052,N'User đã được phân quyền kho này.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap,Kho_ID,Created_By,Created_By_Function,Last_Updated,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ma_Dang_Nhap,@Kho_ID,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),SYSUTCDATETIME(),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_Kho_User SET Ma_Dang_Nhap=@Ma_Dang_Nhap,Kho_ID=@Kho_ID,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
END
GO
