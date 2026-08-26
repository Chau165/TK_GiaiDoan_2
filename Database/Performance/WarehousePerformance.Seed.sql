/*
   Seed only a dedicated performance database. The runner creates an isolated database
   and passes RecordCount through sqlcmd. Do not run this against the business database.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

DECLARE @RecordCount INT = TRY_CONVERT(INT, N'$(RecordCount)');
IF @RecordCount IS NULL OR @RecordCount < 1 OR @RecordCount > 10000000
    THROW 52100, N'RecordCount must be between 1 and 10000000.', 1;

IF EXISTS (SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh)
    OR EXISTS (SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham)
    OR EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham)
    OR EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC)
    OR EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho)
    OR EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho)
    OR EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data)
    OR EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho)
    OR EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data)
    THROW 52101, N'Performance database is not empty. Create a new isolated database or reset it explicitly.', 1;

DECLARE @UnitCount INT = 100;
DECLARE @ProductTypeCount INT = 100;
DECLARE @ProductCount INT = CASE WHEN @RecordCount / 10 < 1000 THEN 1000 WHEN @RecordCount / 10 > 10000 THEN 10000 ELSE @RecordCount / 10 END;
DECLARE @SupplierCount INT = 1000;
DECLARE @WarehouseCount INT = 100;
DECLARE @ReceiptLineCount INT = (@RecordCount + 1) / 2;
DECLARE @IssueLineCount INT = @RecordCount - @ReceiptLineCount;
DECLARE @ReceiptHeaderCount INT = (@ReceiptLineCount + 9) / 10;
DECLARE @IssueHeaderCount INT = (@IssueLineCount + 9) / 10;

/* The warehouse procedures validate the authenticated login through the
   system member table. The isolated benchmark database does not otherwise
   include the application's system schema, so create only the columns used
   by the warehouse contract and seed one non-production benchmark identity. */
IF OBJECT_ID(N'dbo.tbl_Sys_Thanh_Vien', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_Sys_Thanh_Vien
    (
        Auto_ID BIGINT NOT NULL CONSTRAINT PK_tbl_Sys_Thanh_Vien_Performance PRIMARY KEY,
        Ma_Dang_Nhap NVARCHAR(100) NOT NULL CONSTRAINT UQ_tbl_Sys_Thanh_Vien_Performance_Login UNIQUE,
        Ho_Ten NVARCHAR(255) NULL,
        deleted INT NOT NULL CONSTRAINT DF_tbl_Sys_Thanh_Vien_Performance_Deleted DEFAULT (0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = N'PERF_USER')
    INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted)
    VALUES (1, N'PERF_USER', N'Warehouse performance benchmark', 0);

/* 10 x 10 x 10000 x 100 = 100000000 possible numbers, capped by RecordCount. */
;WITH E1(n) AS
(
    SELECT n FROM (VALUES (1),(1),(1),(1),(1),(1),(1),(1),(1),(1)) AS v(n)
), E2(n) AS
(
    SELECT 1 FROM E1 a CROSS JOIN E1 b
), E4(n) AS
(
    SELECT 1 FROM E2 a CROSS JOIN E2 b
), N(n) AS
(
    SELECT TOP (@RecordCount) CONVERT(INT, ROW_NUMBER() OVER (ORDER BY (SELECT NULL))) FROM E4 a CROSS JOIN E4 b
)
SELECT n INTO #Numbers FROM N;
CREATE UNIQUE CLUSTERED INDEX IX_Perf_Numbers ON #Numbers(n);

INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu)
SELECT CONCAT(N'PERF-Unit-', n), N'Benchmark dimension'
FROM #Numbers WHERE n <= @UnitCount;

INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu)
SELECT CONCAT(N'PERF-TYPE-', n), CONCAT(N'Benchmark product type ', n), N'Benchmark dimension'
FROM #Numbers WHERE n <= @ProductTypeCount;

INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu)
SELECT CONCAT(N'PERF-SUP-', n), CONCAT(N'Benchmark supplier ', n), N'Benchmark dimension'
FROM #Numbers WHERE n <= @SupplierCount;

INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu)
SELECT CONCAT(N'PERF-WH-', n), N'Benchmark dimension'
FROM #Numbers WHERE n <= @WarehouseCount;

INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu)
SELECT CONCAT(N'PERF-SKU-', n), CONCAT(N'Benchmark product ', n), ((n - 1) % @ProductTypeCount) + 1, ((n - 1) % @UnitCount) + 1, N'Benchmark dimension'
FROM #Numbers WHERE n <= @ProductCount;

INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Ghi_Chu)
SELECT CONCAT(N'PERF-IN-', n), ((n - 1) % @WarehouseCount) + 1, ((n - 1) % @SupplierCount) + 1,
       DATEADD(DAY, (n - 1) % 730, CONVERT(date, '2025-01-01')), N'Benchmark header'
FROM #Numbers WHERE n <= @ReceiptHeaderCount;

INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Ghi_Chu)
SELECT CONCAT(N'PERF-OUT-', n), ((n - 1) % @WarehouseCount) + 1,
       DATEADD(DAY, (n - 1) % 730, CONVERT(date, '2025-01-01')), N'Benchmark header'
FROM #Numbers WHERE n <= @IssueHeaderCount;

INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap)
SELECT ((n - 1) / 10) + 1, ((n - 1) % @ProductCount) + 1,
       CONVERT(DECIMAL(18,3), 1 + (n % 50)), CONVERT(DECIMAL(18,2), 10 + (n % 1000))
FROM #Numbers WHERE n <= @ReceiptLineCount;

INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat)
SELECT ((n - 1) / 10) + 1, ((n - 1) % @ProductCount) + 1,
       CONVERT(DECIMAL(18,3), 1 + (n % 40)), CONVERT(DECIMAL(18,2), 12 + (n % 1000))
FROM #Numbers WHERE n <= @IssueLineCount;

UPDATE STATISTICS dbo.tbl_DM_Don_Vi_Tinh WITH FULLSCAN;
UPDATE STATISTICS dbo.tbl_DM_Loai_San_Pham WITH FULLSCAN;
UPDATE STATISTICS dbo.tbl_DM_San_Pham WITH FULLSCAN;
UPDATE STATISTICS dbo.tbl_DM_NCC WITH FULLSCAN;
UPDATE STATISTICS dbo.tbl_DM_Kho WITH FULLSCAN;
UPDATE STATISTICS dbo.tbl_XNK_Nhap_Kho WITH FULLSCAN;
UPDATE STATISTICS dbo.tbl_XNK_Nhap_Kho_Raw_Data WITH FULLSCAN;
UPDATE STATISTICS dbo.tbl_XNK_Xuat_Kho WITH FULLSCAN;
UPDATE STATISTICS dbo.tbl_XNK_Xuat_Kho_Raw_Data WITH FULLSCAN;

SELECT N'RecordCount' AS Metric, @RecordCount AS Value
UNION ALL SELECT N'Products', COUNT(*) FROM dbo.tbl_DM_San_Pham
UNION ALL SELECT N'ReceiptHeaders', COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho
UNION ALL SELECT N'ReceiptLines', COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data
UNION ALL SELECT N'IssueHeaders', COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho
UNION ALL SELECT N'IssueLines', COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data;
