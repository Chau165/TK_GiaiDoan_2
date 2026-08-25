/*
   Isolated report paging benchmark.

   The sp_Benchmark_Before_* procedures are loaded only into the two
   TKS_Thuc_Tap_V11_Perf_<rows> databases from the previous revision. The
   effective report procedures are the current after-path.

   Movement aggregation is not changed. Its IO/time appears in the inventory
   procedure output, but the comparison is used only to inspect the paging
   materialization after the shared aggregate is built.
*/
SET NOCOUNT ON;
SET STATISTICS IO ON;
SET STATISTICS TIME ON;

DECLARE @FromDate DATE = CONVERT(DATE, '2025-01-01');
DECLARE @ToDate DATE = CONVERT(DATE, '2026-12-31');

PRINT N'BEFORE_DETAIL_RECEIPT';
EXEC dbo.sp_Benchmark_Before_Detail_Nhap
    @Tu_Ngay = @FromDate,
    @Den_Ngay = @ToDate,
    @Page_Number = 1,
    @Page_Size = $(PageSize),
    @Ma_Dang_Nhap = N'PERF_USER';

PRINT N'AFTER_DETAIL_RECEIPT';
EXEC dbo.sp_BC_Chi_Tiet_Nhap_Page
    @Tu_Ngay = @FromDate,
    @Den_Ngay = @ToDate,
    @Page_Number = 1,
    @Page_Size = $(PageSize),
    @Ma_Dang_Nhap = N'PERF_USER';

PRINT N'BEFORE_DETAIL_ISSUE';
EXEC dbo.sp_Benchmark_Before_Detail_Xuat
    @Tu_Ngay = @FromDate,
    @Den_Ngay = @ToDate,
    @Page_Number = 1,
    @Page_Size = $(PageSize),
    @Ma_Dang_Nhap = N'PERF_USER';

PRINT N'AFTER_DETAIL_ISSUE';
EXEC dbo.sp_BC_Chi_Tiet_Xuat_Page
    @Tu_Ngay = @FromDate,
    @Den_Ngay = @ToDate,
    @Page_Number = 1,
    @Page_Size = $(PageSize),
    @Ma_Dang_Nhap = N'PERF_USER';

PRINT N'BEFORE_INVENTORY';
EXEC dbo.sp_Benchmark_Before_Inventory
    @Tu_Ngay = @FromDate,
    @Den_Ngay = @ToDate,
    @Page_Number = 1,
    @Page_Size = $(PageSize),
    @Ma_Dang_Nhap = N'PERF_USER';

PRINT N'AFTER_INVENTORY';
EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay = @FromDate,
    @Den_Ngay = @ToDate,
    @Page_Number = 1,
    @Page_Size = $(PageSize),
    @Ma_Dang_Nhap = N'PERF_USER';
