/*
  Isolated performance database only.
  Creates the one-off 2025 closing snapshot used to measure a mature system's
  2026 inventory report.  Normal production operation uses
  sp_Inventory_Snapshot_Create_Daily at the end of each business day.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;
DELETE FROM dbo.InventoryBalance_Snapshot_Daily
WHERE Snapshot_Date = '$(SnapshotDate)';

;WITH Movement AS
(
    SELECT h.Kho_ID, d.San_Pham_ID, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Quantity
    FROM dbo.tbl_XNK_Nhap_Kho h
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
    WHERE h.Is_Posted = 1 AND h.Ngay_Nhap_Kho <= '$(SnapshotDate)'
    UNION ALL
    SELECT h.Kho_ID, d.San_Pham_ID, CAST(-d.SL_Xuat AS DECIMAL(18,3))
    FROM dbo.tbl_XNK_Xuat_Kho h
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
    WHERE h.Is_Posted = 1 AND h.Ngay_Xuat_Kho <= '$(SnapshotDate)'
)
INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity)
SELECT '$(SnapshotDate)', Kho_ID, San_Pham_ID, SUM(Quantity)
FROM Movement
GROUP BY Kho_ID, San_Pham_ID;
COMMIT TRANSACTION;
