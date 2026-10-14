-- Rollback for the F9001/F9002 report and inventory Store rename.
-- Generated from the pre-rename canonical procedure source; bodies retain their prior logic.
-- Run only after verifying the target database and coordinating application rollback.
-- No SQLCMD directives are used, so this file can run in SSMS.
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
    THROW 51000, N'Wrong database for report Store rollback.', 1;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.sp_BC_Ton_Kho_Hien_Tai_Page
    @Page_Number INT,
    @Page_Size INT,
    @Ma_Dang_Nhap NVARCHAR(100),
    @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N''
       OR NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap)
        THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 0 SET @Page_Size = 10;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;

    CREATE TABLE #AuthorizedWarehouse
    (
        Kho_ID BIGINT NOT NULL PRIMARY KEY
    );

    INSERT #AuthorizedWarehouse(Kho_ID)
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID);

    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    BEGIN TRY
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        INSERT @GroupSet(Kho_ID)
        SELECT Kho_ID FROM #AuthorizedWarehouse;
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';
        EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set
            @GroupSet = @GroupSet,
            @Mode = N'Shared';
        EXEC dbo.sp_Inventory_Fence_Require_Context
            @GroupSet = @GroupSet,
            @ScopeSet = @ScopeSet,
            @RequiredRootMode = N'Shared',
            @RequiredGroupMode = N'Shared',
            @RequiredScopeMode = N'Shared';

        DECLARE @Current_Report_Generation_Start BIGINT,
            @Current_Report_Generation_End BIGINT;
        SELECT @Current_Report_Generation_Start = Generation
    FROM dbo.Inventory_Current_Report_State
    WHERE State_ID = 1;
        IF @Current_Report_Generation_Start IS NULL
            THROW 51325, N'Generation báo cáo tồn kho hiện tại chưa được khởi tạo.', 1;

        DECLARE @Total_Count INT;
        SELECT @Total_Count = COUNT(*)
    FROM dbo.InventoryBalance_Current b
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID;

        SELECT b.Kho_ID,
           k.Ten_Kho,
           b.San_Pham_ID,
           p.Ma_San_Pham,
           p.Ten_San_Pham,
           CAST(0 AS DECIMAL(18,3)) AS SL_Dau_Ky,
           CAST(0 AS DECIMAL(18,3)) AS SL_Nhap,
           CAST(0 AS DECIMAL(18,3)) AS SL_Xuat,
           b.CurrentQuantity AS SL_Cuoi_Ky,
           b.CurrentQuantity AS SL_Ton_Thuc_Te,
           b.ReservedQuantity AS SL_Dang_Giu,
           CAST(b.CurrentQuantity - b.ReservedQuantity AS DECIMAL(18,3)) AS SL_Kha_Dung
    INTO #CurrentReportPage
    FROM dbo.InventoryBalance_Current b
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = b.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = b.San_Pham_ID
    WHERE 1 = 0;

        IF @Page_Size = 0
        BEGIN
            INSERT INTO #CurrentReportPage
            SELECT b.Kho_ID,
                   k.Ten_Kho,
                   b.San_Pham_ID,
                   p.Ma_San_Pham,
                   p.Ten_San_Pham,
                   CAST(0 AS DECIMAL(18,3)),
                   CAST(0 AS DECIMAL(18,3)),
                   CAST(0 AS DECIMAL(18,3)),
                   b.CurrentQuantity,
                   b.CurrentQuantity,
                   b.ReservedQuantity,
                   CAST(b.CurrentQuantity - b.ReservedQuantity AS DECIMAL(18,3))
            FROM dbo.InventoryBalance_Current b
            JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID
            JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = b.Kho_ID
            JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = b.San_Pham_ID;
        END
        ELSE
        BEGIN
            /* The clustered balance key supplies deterministic, seek-friendly order.
               Sorting by display names would reintroduce a large workspace grant. */
            INSERT INTO #CurrentReportPage
            SELECT b.Kho_ID,
                   k.Ten_Kho,
                   b.San_Pham_ID,
                   p.Ma_San_Pham,
                   p.Ten_San_Pham,
                   CAST(0 AS DECIMAL(18,3)),
                   CAST(0 AS DECIMAL(18,3)),
                   CAST(0 AS DECIMAL(18,3)),
                   b.CurrentQuantity,
                   b.CurrentQuantity,
                   b.ReservedQuantity,
                   CAST(b.CurrentQuantity - b.ReservedQuantity AS DECIMAL(18,3))
            FROM dbo.InventoryBalance_Current b
            JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID
            JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = b.Kho_ID
            JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = b.San_Pham_ID
            ORDER BY b.Kho_ID, b.San_Pham_ID
            OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
        END;

        SELECT @Current_Report_Generation_End = Generation
    FROM dbo.Inventory_Current_Report_State
    WHERE State_ID = 1;
        IF @Current_Report_Generation_End IS NULL
            THROW 51325, N'Generation báo cáo tồn kho hiện tại chưa được khởi tạo.', 1;
        IF @Current_Report_Generation_End <> @Current_Report_Generation_Start
            THROW 51324, N'Báo cáo tồn kho hiện tại đã thay đổi trong lúc đọc; vui lòng thử lại.', 1;

        SELECT @Total_Count AS Total_Count;
        SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham,
               SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky, SL_Ton_Thuc_Te, SL_Dang_Giu, SL_Kha_Dung
        FROM #CurrentReportPage
        ORDER BY Kho_ID, San_Pham_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Ngay_Nhap_Kho AS Ngay, h.So_Phieu_Nhap_Kho AS So_Phieu, n.Ten_NCC AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia, CAST(d.SL_Nhap * d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho;
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Ngay_Xuat_Kho AS Ngay, h.So_Phieu_Xuat_Kho AS So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia, CAST(d.SL_Xuat * d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho;
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    DECLARE @Offset BIGINT = CONVERT(BIGINT, @Page_Number - 1) * CONVERT(BIGINT, @Page_Size);

    CREATE TABLE #AuthorizedWarehouse
    (
        Kho_ID BIGINT NOT NULL PRIMARY KEY
    );

    INSERT #AuthorizedWarehouse(Kho_ID)
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID);

    SELECT COUNT(*) AS Total_Count
    FROM dbo.tbl_XNK_Nhap_Kho h
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
      AND h.Is_Posted = 1;

    ;WITH PageScope AS
    (
        SELECT d.Auto_ID AS Detail_ID,
               h.Auto_ID AS Document_ID,
               h.Ngay_Nhap_Kho AS Ngay,
               h.So_Phieu_Nhap_Kho AS So_Phieu,
               h.NCC_ID,
               d.San_Pham_ID,
               d.SL_Nhap AS So_Luong,
               d.Don_Gia_Nhap AS Don_Gia
        FROM dbo.tbl_XNK_Nhap_Kho h
        JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
        JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
          AND h.Is_Posted = 1
        ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho, h.Auto_ID, d.Auto_ID
        OFFSET @Offset ROWS FETCH NEXT @Page_Size ROWS ONLY
    )
    SELECT ps.Ngay, ps.So_Phieu, n.Ten_NCC AS Nha_Cung_Cap,
           p.Ma_San_Pham, p.Ten_San_Pham, ps.So_Luong, ps.Don_Gia,
           CAST(ps.So_Luong * ps.Don_Gia AS DECIMAL(18,2)) AS Tri_Gia
    FROM PageScope ps
    JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = ps.NCC_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = ps.San_Pham_ID
    ORDER BY ps.Ngay, ps.So_Phieu, ps.Document_ID, ps.Detail_ID;
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    DECLARE @Offset BIGINT = CONVERT(BIGINT, @Page_Number - 1) * CONVERT(BIGINT, @Page_Size);

    CREATE TABLE #AuthorizedWarehouse
    (
        Kho_ID BIGINT NOT NULL PRIMARY KEY
    );

    INSERT #AuthorizedWarehouse(Kho_ID)
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID);

    SELECT COUNT(*) AS Total_Count
    FROM dbo.tbl_XNK_Xuat_Kho h
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
      AND h.Is_Posted = 1;

    ;WITH PageScope AS
    (
        SELECT d.Auto_ID AS Detail_ID,
               h.Auto_ID AS Document_ID,
               h.Ngay_Xuat_Kho AS Ngay,
               h.So_Phieu_Xuat_Kho AS So_Phieu,
               d.San_Pham_ID,
               d.SL_Xuat AS So_Luong,
               d.Don_Gia_Xuat AS Don_Gia
        FROM dbo.tbl_XNK_Xuat_Kho h
        JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
          AND h.Is_Posted = 1
        ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho, h.Auto_ID, d.Auto_ID
        OFFSET @Offset ROWS FETCH NEXT @Page_Size ROWS ONLY
    )
    SELECT ps.Ngay, ps.So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap,
           p.Ma_San_Pham, p.Ten_San_Pham, ps.So_Luong, ps.Don_Gia,
           CAST(ps.So_Luong * ps.Don_Gia AS DECIMAL(18,2)) AS Tri_Gia
    FROM PageScope ps
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = ps.San_Pham_ID
    ORDER BY ps.Ngay, ps.So_Phieu, ps.Document_ID, ps.Detail_ID;
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay
        THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    BEGIN TRY
        IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        DECLARE @Is_Current_Report BIT = IIF(@Den_Ngay = CONVERT(DATE, SYSDATETIME()), 1, 0);
        DECLARE @Catalog_Scope_Count BIGINT, @Catalog_Max_ID BIGINT;
        CREATE TABLE #ReportFenceExpectedState
        (
            Source_Name NVARCHAR(16) NOT NULL,
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            Boundary_Start DATE NULL,
            Boundary_End DATE NULL,
            Version_Value BIGINT NULL,
            Identity_Value BIGINT NULL,
            Is_Current_Value BIT NULL,
            Is_Valid_Value BIT NULL,
            Value_Decimal DECIMAL(18,3) NULL,
            Value_Decimal_2 DECIMAL(18,3) NULL
        );
        CREATE TABLE #ReportFenceMode
        (
            Fence_Mode NVARCHAR(8) NOT NULL
        );
        EXEC dbo.sp_Inventory_Report_Acquire_Scope_Fence
            @Tu_Ngay = @Tu_Ngay,
            @Den_Ngay = @Den_Ngay,
            @Ma_Dang_Nhap = @Ma_Dang_Nhap,
            @Kho_ID = @Kho_ID,
            @Is_Current_Report = @Is_Current_Report,
            @Catalog_Scope_Count = @Catalog_Scope_Count OUTPUT,
            @Catalog_Max_ID = @Catalog_Max_ID OUTPUT;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.InventoryMovement_RebuildQueue q
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = q.Kho_ID
            WHERE q.Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL')
              AND ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
              AND q.From_Date <= @Den_Ngay
        )
            THROW 51222, N'Movement Aggregate của báo cáo đang tái tạo. Vui lòng thử lại sau khi worker hoàn tất.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM
            (
            SELECT s.Kho_ID, s.San_Pham_ID
            FROM dbo.InventoryBalance_Snapshot_Daily s
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR s.Kho_ID = @Kho_ID)
              AND s.Snapshot_Date < @Tu_Ngay
            UNION
            SELECT h.Kho_ID, d.San_Pham_ID
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = h.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
              AND h.Is_Posted = 1
              AND h.Ngay_Nhap_Kho <= @Den_Ngay
            UNION
            SELECT h.Kho_ID, d.San_Pham_ID
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = h.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
              AND h.Is_Posted = 1
              AND h.Ngay_Xuat_Kho <= @Den_Ngay
            UNION
            SELECT b.Kho_ID, b.San_Pham_ID
            FROM dbo.InventoryBalance_Current b
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = b.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR b.Kho_ID = @Kho_ID)
              AND @Den_Ngay = CONVERT(DATE, SYSDATETIME())
            ) sk
        OUTER APPLY
        (
            SELECT TOP (1) s.Snapshot_Date
            FROM dbo.InventoryBalance_Snapshot_Daily s
            WHERE s.Kho_ID = sk.Kho_ID
              AND s.San_Pham_ID = sk.San_Pham_ID
              AND s.Snapshot_Date < @Tu_Ngay
              AND s.IsValid = 1
            ORDER BY s.Snapshot_Date DESC
        ) latest
        WHERE latest.Snapshot_Date IS NULL
    )
        INSERT dbo.InventorySnapshot_ReportFallbackLog
        (ReportFromDate, ReportToDate, Ma_Dang_Nhap, SnapshotMissingReason)
        VALUES (@Tu_Ngay, @Den_Ngay, @Ma_Dang_Nhap, N'NO_VALID_SNAPSHOT_FOR_ONE_OR_MORE_SCOPES');

    SELECT r.Kho_ID, r.Ten_Kho, r.San_Pham_ID, r.Ma_San_Pham, r.Ten_San_Pham,
           r.SL_Dau_Ky, r.SL_Nhap, r.SL_Xuat, r.SL_Cuoi_Ky, r.SL_Ton_Thuc_Te, r.SL_Dang_Giu, r.SL_Kha_Dung
    INTO #ReportRows
    FROM dbo.fn_Inventory_Report_Snapshot(@Tu_Ngay, @Den_Ngay, @Ma_Dang_Nhap, IIF(@Den_Ngay = CONVERT(DATE, SYSDATETIME()), 1, 0)) r
    WHERE @Kho_ID IS NULL OR r.Kho_ID = @Kho_ID;

    EXEC dbo.sp_Inventory_Report_Validate_Scope_Fence
        @Tu_Ngay = @Tu_Ngay,
        @Den_Ngay = @Den_Ngay,
        @Ma_Dang_Nhap = @Ma_Dang_Nhap,
        @Kho_ID = @Kho_ID,
        @Is_Current_Report = @Is_Current_Report;

    DECLARE @Current_Catalog_Scope_Count BIGINT, @Current_Catalog_Max_ID BIGINT;
    SELECT @Current_Catalog_Scope_Count = COUNT_BIG(*),
           @Current_Catalog_Max_ID = COALESCE(MAX(c.Catalog_ID), 0)
    FROM dbo.Inventory_Report_Scope_Catalog c
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = c.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR c.Kho_ID = @Kho_ID)
      AND (@Is_Current_Report = 1 OR c.First_Posted_Date <= @Den_Ngay);

    IF @Current_Catalog_Scope_Count <> @Catalog_Scope_Count
       OR @Current_Catalog_Max_ID <> @Catalog_Max_ID
        THROW 51324, N'Phạm vi báo cáo đã thay đổi trong lúc đọc; vui lòng thử lại.', 1;

    SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham,
           SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky, SL_Ton_Thuc_Te, SL_Dang_Giu, SL_Kha_Dung
    FROM #ReportRows
    ORDER BY Ten_Kho, Ma_San_Pham;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay
        THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    BEGIN TRY
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventoryMovement_AggregateState
        WHERE State_ID = 1 AND IsInitialized = 1
    )
        THROW 51221, N'Movement Aggregate chưa được khởi tạo. Hãy chạy sp_Inventory_Movement_Bootstrap_From_Ledger trước khi xem báo cáo.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventoryBalance_Daily_AggregateState
        WHERE State_ID = 1 AND IsInitialized = 1
    )
        THROW 51230, N'Balance Daily chưa được khởi tạo. Hãy chạy sp_Inventory_Balance_Daily_Bootstrap_From_Movement trước khi xem báo cáo.', 1;

    DECLARE @Is_Current_Report BIT = IIF(@Den_Ngay = CONVERT(DATE, SYSDATETIME()), 1, 0);

    DECLARE @Catalog_Scope_Count BIGINT, @Catalog_Max_ID BIGINT;
    CREATE TABLE #ReportFenceExpectedState
    (
        Source_Name NVARCHAR(16) NOT NULL,
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        Boundary_Start DATE NULL,
        Boundary_End DATE NULL,
        Version_Value BIGINT NULL,
        Identity_Value BIGINT NULL,
        Is_Current_Value BIT NULL,
        Is_Valid_Value BIT NULL,
        Value_Decimal DECIMAL(18,3) NULL,
        Value_Decimal_2 DECIMAL(18,3) NULL
    );
    CREATE TABLE #ReportFenceMode
    (
        Fence_Mode NVARCHAR(8) NOT NULL
    );
    EXEC dbo.sp_Inventory_Report_Acquire_Scope_Fence
        @Tu_Ngay = @Tu_Ngay,
        @Den_Ngay = @Den_Ngay,
        @Ma_Dang_Nhap = @Ma_Dang_Nhap,
        @Kho_ID = @Kho_ID,
        @Is_Current_Report = @Is_Current_Report,
        @Catalog_Scope_Count = @Catalog_Scope_Count OUTPUT,
        @Catalog_Max_ID = @Catalog_Max_ID OUTPUT;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.InventoryMovement_RebuildQueue q
        JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = q.Kho_ID
        WHERE q.Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL')
          AND ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
          AND (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
          AND q.From_Date <= @Den_Ngay
    )
        THROW 51222, N'Movement Aggregate của báo cáo đang tái tạo. Vui lòng thử lại sau khi worker hoàn tất.', 1;

    DECLARE @Total_Count INT;

    /* Candidate A: preserve the old ending-row eligibility exactly, but keep
       only the eligible scope keys before applying the public ordering/page.
       The intermediate tables intentionally have no uniqueness constraint so
       the existing authorization join's row semantics are not changed. */
    CREATE TABLE #ReportEligibleScope
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL
    );

    ;WITH AuthorizedScope AS
    (
        SELECT s.Kho_ID, s.San_Pham_ID
        FROM dbo.Inventory_Balance_Daily_Scope s
        JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
        WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
          AND (@Kho_ID IS NULL OR s.Kho_ID = @Kho_ID)
          AND s.First_Balance_Date <= @Den_Ngay
    )
    INSERT #ReportEligibleScope(Kho_ID, San_Pham_ID)
    SELECT s.Kho_ID, s.San_Pham_ID
    FROM AuthorizedScope s
    WHERE EXISTS
    (
        SELECT 1
        FROM dbo.Inventory_Balance_Daily b WITH (INDEX(IX_Inventory_Balance_Daily_Scope))
        WHERE b.Kho_ID = s.Kho_ID
          AND b.San_Pham_ID = s.San_Pham_ID
          AND b.Balance_Date <= @Den_Ngay
          AND b.IsValid = 1
    )
    OPTION (MAXDOP 1);

    SELECT @Total_Count = COUNT(*)
    FROM #ReportEligibleScope;

    CREATE TABLE #ReportPageKeys
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL
    );

    INSERT #ReportPageKeys(Kho_ID, San_Pham_ID)
    SELECT e.Kho_ID, e.San_Pham_ID
    FROM #ReportEligibleScope e
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = e.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = e.San_Pham_ID
    ORDER BY k.Ten_Kho, p.Ma_San_Pham
    OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;

    SELECT s.Kho_ID,
           k.Ten_Kho,
           s.San_Pham_ID,
           p.Ma_San_Pham,
           p.Ten_San_Pham,
           /* A missing pre-period row means there is no materialized balance
              before @Tu_Ngay.  ending.OpeningQuantity belongs to the latest
              movement day and must never seed the report opening. */
           CAST(COALESCE(opening.ClosingQuantity, 0) AS DECIMAL(18,3)) AS SL_Dau_Ky,
           CAST(ending.CumulativeReceived - ISNULL(opening.CumulativeReceived, 0) AS DECIMAL(18,3)) AS SL_Nhap,
           CAST(ending.CumulativeIssued - ISNULL(opening.CumulativeIssued, 0) AS DECIMAL(18,3)) AS SL_Xuat,
           /* SL_Cuoi_Ky is a period value.  CurrentQuantity remains available
              only through the separate current/on-hand fields below. */
           CAST(ending.ClosingQuantity AS DECIMAL(18,3)) AS SL_Cuoi_Ky,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(currentBalance.CurrentQuantity, 0) ELSE ending.ClosingQuantity END AS DECIMAL(18,3)) AS SL_Ton_Thuc_Te,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(currentBalance.ReservedQuantity, 0) ELSE 0 END AS DECIMAL(18,3)) AS SL_Dang_Giu,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(currentBalance.CurrentQuantity, 0) - ISNULL(currentBalance.ReservedQuantity, 0) ELSE ending.ClosingQuantity END AS DECIMAL(18,3)) AS SL_Kha_Dung
     INTO #ReportPage
     FROM #ReportPageKeys s
     CROSS APPLY
     (
        SELECT TOP (1)
               b.OpeningQuantity,
               b.ClosingQuantity,
               b.CumulativeReceived,
               b.CumulativeIssued
        FROM dbo.Inventory_Balance_Daily b WITH (INDEX(IX_Inventory_Balance_Daily_Scope))
        WHERE b.Kho_ID = s.Kho_ID
          AND b.San_Pham_ID = s.San_Pham_ID
          AND b.Balance_Date <= @Den_Ngay
          AND b.IsValid = 1
        ORDER BY b.Balance_Date DESC
    ) ending
    OUTER APPLY
    (
        SELECT TOP (1)
               b.ClosingQuantity,
               b.CumulativeReceived,
               b.CumulativeIssued
        FROM dbo.Inventory_Balance_Daily b WITH (INDEX(IX_Inventory_Balance_Daily_Scope))
        WHERE b.Kho_ID = s.Kho_ID
          AND b.San_Pham_ID = s.San_Pham_ID
          AND b.Balance_Date < @Tu_Ngay
          AND b.IsValid = 1
        ORDER BY b.Balance_Date DESC
    ) opening
    LEFT JOIN dbo.InventoryBalance_Current currentBalance
      ON currentBalance.Kho_ID = s.Kho_ID
     AND currentBalance.San_Pham_ID = s.San_Pham_ID
     AND @Is_Current_Report = 1
     JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = s.Kho_ID
     JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = s.San_Pham_ID;

    EXEC dbo.sp_Inventory_Report_Validate_Scope_Fence
        @Tu_Ngay = @Tu_Ngay,
        @Den_Ngay = @Den_Ngay,
        @Ma_Dang_Nhap = @Ma_Dang_Nhap,
        @Kho_ID = @Kho_ID,
        @Is_Current_Report = @Is_Current_Report;

    DECLARE @Current_Catalog_Scope_Count BIGINT, @Current_Catalog_Max_ID BIGINT;
    SELECT @Current_Catalog_Scope_Count = COUNT_BIG(*),
           @Current_Catalog_Max_ID = COALESCE(MAX(c.Catalog_ID), 0)
    FROM dbo.Inventory_Report_Scope_Catalog c
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = c.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR c.Kho_ID = @Kho_ID)
      AND (@Is_Current_Report = 1 OR c.First_Posted_Date <= @Den_Ngay);

    IF @Current_Catalog_Scope_Count <> @Catalog_Scope_Count
       OR @Current_Catalog_Max_ID <> @Catalog_Max_ID
        THROW 51324, N'Phạm vi báo cáo đã thay đổi trong lúc đọc; vui lòng thử lại.', 1;

    SELECT @Total_Count AS Total_Count;
    SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham,
           SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky, SL_Ton_Thuc_Te, SL_Dang_Giu, SL_Kha_Dung
    FROM #ReportPage
    ORDER BY Ten_Kho, Ma_San_Pham;
    IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'dbo.sp_BC_Ton_Kho_Hien_Tai_Page'),
        (N'dbo.sp_BC_Chi_Tiet_Nhap'),
        (N'dbo.sp_BC_Chi_Tiet_Xuat'),
        (N'dbo.sp_BC_Chi_Tiet_Nhap_Page'),
        (N'dbo.sp_BC_Chi_Tiet_Xuat_Page'),
        (N'dbo.sp_BC_Xuat_Nhap_Ton'),
        (N'dbo.sp_BC_Xuat_Nhap_Ton_Page')
    ) AS expected(ObjectName)
    WHERE OBJECT_ID(expected.ObjectName, N'P') IS NULL
)
    THROW 51001, N'Rollback did not restore every original report Store.', 1;
GO
-- Keep new names by default. Set this to 1 only after the application has been rolled back to old callers.
DECLARE @ConfirmApplicationCodeRolledBack BIT = 0;
IF @ConfirmApplicationCodeRolledBack = 1
BEGIN
    DROP PROCEDURE dbo.F9001_sp_Ton_Kho_Hien_Tai_Page;
    DROP PROCEDURE dbo.F9002_sp_Chi_Tiet_Nhap;
    DROP PROCEDURE dbo.F9002_sp_Chi_Tiet_Xuat;
    DROP PROCEDURE dbo.F9002_sp_Chi_Tiet_Nhap_Page;
    DROP PROCEDURE dbo.F9002_sp_Chi_Tiet_Xuat_Page;
    DROP PROCEDURE dbo.F9002_sp_Xuat_Nhap_Ton;
    DROP PROCEDURE dbo.F9002_sp_Xuat_Nhap_Ton_Page;
END
ELSE
    PRINT N'Original Stores restored. F9001/F9002 Stores are retained until application rollback is confirmed.';
