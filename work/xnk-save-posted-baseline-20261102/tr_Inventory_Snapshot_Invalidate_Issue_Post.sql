
CREATE   TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Issue_Post
ON dbo.tbl_XNK_Xuat_Kho
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(Is_Posted) AND NOT UPDATE(Ngay_Xuat_Kho) RETURN;

    DECLARE @Affected dbo.InventorySnapshotAffectedType;
    INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT i.Kho_ID,
           r.San_Pham_ID,
           CASE WHEN i.Ngay_Xuat_Kho <= d.Ngay_Xuat_Kho THEN i.Ngay_Xuat_Kho ELSE d.Ngay_Xuat_Kho END,
           CASE WHEN d.Is_Posted = 0 AND i.Is_Posted = 1 THEN N'BACK_DATE_POST' ELSE N'DOCUMENT_UPDATE' END
    FROM inserted i
    JOIN deleted d ON d.Auto_ID = i.Auto_ID
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data r ON r.Xuat_Kho_ID = i.Auto_ID
    WHERE (d.Is_Posted = 0 AND i.Is_Posted = 1)
       OR (d.Is_Posted = 1 AND i.Is_Posted = 1 AND i.Ngay_Xuat_Kho <> d.Ngay_Xuat_Kho);

    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
