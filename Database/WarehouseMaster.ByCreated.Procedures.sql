/*
    Prepared migration for the five Warehouse Master Data date filters.
    Target database: TKS_Thuc_Tap_V11_GiaiDoan2.
    Do not execute against a business database until deployment is approved.
*/
IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
    THROW 51000, N'Wrong database. Expected TKS_Thuc_Tap_V11_GiaiDoan2.', 1;
GO

CREATE OR ALTER PROCEDURE dbo.F2009_sp_sel_List_Kho_By_Created
    @Date_From_Utc DATETIME2(7) = NULL,
    @Date_To_Utc_Exclusive DATETIME2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Date_From_Utc IS NOT NULL
       AND @Date_To_Utc_Exclusive IS NOT NULL
       AND @Date_From_Utc >= @Date_To_Utc_Exclusive
        THROW 51001, N'Ngày bắt đầu không được lớn hơn ngày kết thúc.', 1;

    SELECT Auto_ID, Ten_Kho, Ghi_Chu, Created
    FROM dbo.tbl_DM_Kho
    WHERE (@Date_From_Utc IS NULL OR Created >= @Date_From_Utc)
      AND (@Date_To_Utc_Exclusive IS NULL OR Created < @Date_To_Utc_Exclusive)
    ORDER BY Ten_Kho;
END;
GO

CREATE OR ALTER PROCEDURE dbo.F2016_sp_sel_List_Don_Vi_Tinh_By_Created
    @Date_From_Utc DATETIME2(7) = NULL,
    @Date_To_Utc_Exclusive DATETIME2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Date_From_Utc IS NOT NULL
       AND @Date_To_Utc_Exclusive IS NOT NULL
       AND @Date_From_Utc >= @Date_To_Utc_Exclusive
        THROW 51001, N'Ngày bắt đầu không được lớn hơn ngày kết thúc.', 1;

    SELECT Auto_ID, Ten_Don_Vi_Tinh, Ghi_Chu, Created
    FROM dbo.tbl_DM_Don_Vi_Tinh
    WHERE (@Date_From_Utc IS NULL OR Created >= @Date_From_Utc)
      AND (@Date_To_Utc_Exclusive IS NULL OR Created < @Date_To_Utc_Exclusive)
    ORDER BY Ten_Don_Vi_Tinh;
END;
GO

CREATE OR ALTER PROCEDURE dbo.F2017_sp_sel_List_Loai_San_Pham_By_Created
    @Date_From_Utc DATETIME2(7) = NULL,
    @Date_To_Utc_Exclusive DATETIME2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Date_From_Utc IS NOT NULL
       AND @Date_To_Utc_Exclusive IS NOT NULL
       AND @Date_From_Utc >= @Date_To_Utc_Exclusive
        THROW 51001, N'Ngày bắt đầu không được lớn hơn ngày kết thúc.', 1;

    SELECT Auto_ID, Ma_LSP, Ten_LSP, Ghi_Chu, Created
    FROM dbo.tbl_DM_Loai_San_Pham
    WHERE (@Date_From_Utc IS NULL OR Created >= @Date_From_Utc)
      AND (@Date_To_Utc_Exclusive IS NULL OR Created < @Date_To_Utc_Exclusive)
    ORDER BY Ma_LSP;
END;
GO

CREATE OR ALTER PROCEDURE dbo.F2018_sp_sel_List_San_Pham_By_Created
    @Date_From_Utc DATETIME2(7) = NULL,
    @Date_To_Utc_Exclusive DATETIME2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Date_From_Utc IS NOT NULL
       AND @Date_To_Utc_Exclusive IS NOT NULL
       AND @Date_From_Utc >= @Date_To_Utc_Exclusive
        THROW 51001, N'Ngày bắt đầu không được lớn hơn ngày kết thúc.', 1;

    SELECT Auto_ID, Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu, Created
    FROM dbo.tbl_DM_San_Pham
    WHERE (@Date_From_Utc IS NULL OR Created >= @Date_From_Utc)
      AND (@Date_To_Utc_Exclusive IS NULL OR Created < @Date_To_Utc_Exclusive)
    ORDER BY Ma_San_Pham;
END;
GO

CREATE OR ALTER PROCEDURE dbo.F2019_sp_sel_List_NCC_By_Created
    @Date_From_Utc DATETIME2(7) = NULL,
    @Date_To_Utc_Exclusive DATETIME2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Date_From_Utc IS NOT NULL
       AND @Date_To_Utc_Exclusive IS NOT NULL
       AND @Date_From_Utc >= @Date_To_Utc_Exclusive
        THROW 51001, N'Ngày bắt đầu không được lớn hơn ngày kết thúc.', 1;

    SELECT Auto_ID, Ma_NCC, Ten_NCC, Ghi_Chu, Created
    FROM dbo.tbl_DM_NCC
    WHERE (@Date_From_Utc IS NULL OR Created >= @Date_From_Utc)
      AND (@Date_To_Utc_Exclusive IS NULL OR Created < @Date_To_Utc_Exclusive)
    ORDER BY Ma_NCC;
END;
GO
