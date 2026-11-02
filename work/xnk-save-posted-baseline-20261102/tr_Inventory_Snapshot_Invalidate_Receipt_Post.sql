
/* Header triggers: invalidate and enqueue only the affected
   warehouse/product scope. No snapshot row is deleted. */
CREATE   TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Receipt_Post
ON dbo.tbl_XNK_Nhap_Kho
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(Is_Posted) AND NOT UPDATE(Ngay_Nhap_Kho) RETURN;

    DECLARE @Affected dbo.InventorySnapshotAffectedType;
    INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT DISTINCT i.Kho_ID,
           r.San_Pham_ID,
           CASE WHEN i.Ngay_Nhap_Kho <= d.Ngay_Nhap_Kho THEN i.Ngay_Nhap_Kho ELSE d.Ngay_Nhap_Kho END,
           CASE WHEN d.Is_Posted = 0 AND i.Is_Posted = 1 THEN N'BACK_DATE_POST' ELSE N'DOCUMENT_UPDATE' END
    FROM inserted i
    JOIN deleted d ON d.Auto_ID = i.Auto_ID
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data r ON r.Nhap_Kho_ID = i.Auto_ID
    WHERE (d.Is_Posted = 0 AND i.Is_Posted = 1)
       OR (d.Is_Posted = 1 AND i.Is_Posted = 1 AND i.Ngay_Nhap_Kho <> d.Ngay_Nhap_Kho);

    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
