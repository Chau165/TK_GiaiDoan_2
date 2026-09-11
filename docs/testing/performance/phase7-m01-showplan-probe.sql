/* Phase 7 M01 reproducible plan probe. Run only on an isolated perf database. */
SET QUOTED_IDENTIFIER ON;
SET SHOWPLAN_ALL ON;
GO
EXEC dbo.sp_Inventory_Balance_Daily_Rebuild
    @Kho_ID = 1,
    @San_Pham_ID = 1,
    @From_Date = '2025-01-01';
GO
EXEC dbo.sp_Inventory_Movement_Rebuild
    @Kho_ID = 1,
    @San_Pham_ID = 1,
    @From_Date = '2025-01-01',
    @To_Date = '2026-12-31';
GO
SET SHOWPLAN_ALL OFF;
GO
