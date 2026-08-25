/*
   Session-level tempdb allocation probe for the isolated report benchmark.
   The values are cumulative pages allocated by the current SQL session during
   each procedure call, not a global tempdb peak; concurrent workload tests
   should use the server's wait/space telemetry for peak attribution.
*/
SET NOCOUNT ON;

DECLARE @UserBefore BIGINT;
DECLARE @InternalBefore BIGINT;

SELECT @UserBefore = user_objects_alloc_page_count,
       @InternalBefore = internal_objects_alloc_page_count
FROM sys.dm_db_session_space_usage
WHERE session_id = @@SPID;
EXEC dbo.sp_Benchmark_Before_Detail_Nhap
    @Tu_Ngay = '2025-01-01', @Den_Ngay = '2026-12-31',
    @Page_Number = 1, @Page_Size = 10, @Ma_Dang_Nhap = N'PERF_USER';
SELECT N'BEFORE_DETAIL_RECEIPT_TEMPDB' AS Metric,
       (user_objects_alloc_page_count - @UserBefore) * 8 AS UserObjectAllocatedKB,
       (internal_objects_alloc_page_count - @InternalBefore) * 8 AS InternalObjectAllocatedKB
FROM sys.dm_db_session_space_usage
WHERE session_id = @@SPID;

SELECT @UserBefore = user_objects_alloc_page_count,
       @InternalBefore = internal_objects_alloc_page_count
FROM sys.dm_db_session_space_usage
WHERE session_id = @@SPID;
EXEC dbo.sp_BC_Chi_Tiet_Nhap_Page
    @Tu_Ngay = '2025-01-01', @Den_Ngay = '2026-12-31',
    @Page_Number = 1, @Page_Size = 10, @Ma_Dang_Nhap = N'PERF_USER';
SELECT N'AFTER_DETAIL_RECEIPT_TEMPDB' AS Metric,
       (user_objects_alloc_page_count - @UserBefore) * 8 AS UserObjectAllocatedKB,
       (internal_objects_alloc_page_count - @InternalBefore) * 8 AS InternalObjectAllocatedKB
FROM sys.dm_db_session_space_usage
WHERE session_id = @@SPID;

SELECT @UserBefore = user_objects_alloc_page_count,
       @InternalBefore = internal_objects_alloc_page_count
FROM sys.dm_db_session_space_usage
WHERE session_id = @@SPID;
EXEC dbo.sp_Benchmark_Before_Inventory
    @Tu_Ngay = '2025-01-01', @Den_Ngay = '2026-12-31',
    @Page_Number = 1, @Page_Size = 10, @Ma_Dang_Nhap = N'PERF_USER';
SELECT N'BEFORE_INVENTORY_TEMPDB' AS Metric,
       (user_objects_alloc_page_count - @UserBefore) * 8 AS UserObjectAllocatedKB,
       (internal_objects_alloc_page_count - @InternalBefore) * 8 AS InternalObjectAllocatedKB
FROM sys.dm_db_session_space_usage
WHERE session_id = @@SPID;

SELECT @UserBefore = user_objects_alloc_page_count,
       @InternalBefore = internal_objects_alloc_page_count
FROM sys.dm_db_session_space_usage
WHERE session_id = @@SPID;
EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay = '2025-01-01', @Den_Ngay = '2026-12-31',
    @Page_Number = 1, @Page_Size = 10, @Ma_Dang_Nhap = N'PERF_USER';
SELECT N'AFTER_INVENTORY_TEMPDB' AS Metric,
       (user_objects_alloc_page_count - @UserBefore) * 8 AS UserObjectAllocatedKB,
       (internal_objects_alloc_page_count - @InternalBefore) * 8 AS InternalObjectAllocatedKB
FROM sys.dm_db_session_space_usage
WHERE session_id = @@SPID;
