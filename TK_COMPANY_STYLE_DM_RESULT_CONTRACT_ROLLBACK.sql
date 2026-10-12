-- Restores only the previous result projections of the ten DM List/Lookup procedures.
-- Does not touch cache Stores, shared Page Stores, CRUD Stores, or table data.
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @sql nvarchar(max);

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2009_sp_sel_List_Kho.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2009_sp_sel_List_Kho
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, CAST(N'''' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'''' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Kho ORDER BY Ten_Kho;
END
';
    EXEC sys.sp_executesql @sql;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2009_sp_sel_List_Kho_Lookup.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2009_sp_sel_List_Kho_Lookup
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, CAST(N'''' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name FROM dbo.tbl_DM_Kho ORDER BY Ten_Kho;
END
';
    EXEC sys.sp_executesql @sql;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2016_sp_sel_List_Don_Vi_Tinh.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2016_sp_sel_List_Don_Vi_Tinh
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, CAST(N'''' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'''' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh;
END
';
    EXEC sys.sp_executesql @sql;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2016_sp_sel_List_Don_Vi_Tinh_Lookup.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2016_sp_sel_List_Don_Vi_Tinh_Lookup
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, CAST(N'''' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh;
END
';
    EXEC sys.sp_executesql @sql;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2017_sp_sel_List_Loai_San_Pham.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2017_sp_sel_List_Loai_San_Pham
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'''' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP;
END
';
    EXEC sys.sp_executesql @sql;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2017_sp_sel_List_Loai_San_Pham_Lookup.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2017_sp_sel_List_Loai_San_Pham_Lookup
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP;
END
';
    EXEC sys.sp_executesql @sql;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2018_sp_sel_List_San_Pham.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2018_sp_sel_List_San_Pham
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name, Loai_San_Pham_ID AS Related_ID, Don_Vi_Tinh_ID AS Related_ID_2, CAST(N'''' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_San_Pham ORDER BY Ma_San_Pham;
END
';
    EXEC sys.sp_executesql @sql;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2018_sp_sel_List_San_Pham_Lookup.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2018_sp_sel_List_San_Pham_Lookup
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name FROM dbo.tbl_DM_San_Pham ORDER BY Ma_San_Pham;
END
';
    EXEC sys.sp_executesql @sql;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2019_sp_sel_List_NCC.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2019_sp_sel_List_NCC
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'''' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_NCC ORDER BY Ma_NCC;
END
';
    EXEC sys.sp_executesql @sql;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before restoring F2019_sp_sel_List_NCC_Lookup.', 1;
    SET @sql = N'
CREATE OR ALTER PROCEDURE dbo.F2019_sp_sel_List_NCC_Lookup
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name FROM dbo.tbl_DM_NCC ORDER BY Ma_NCC;
END
';
    EXEC sys.sp_executesql @sql;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
