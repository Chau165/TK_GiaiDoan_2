
CREATE   TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Issue_Delete
ON dbo.tbl_XNK_Xuat_Kho
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Affected dbo.InventorySnapshotAffectedType;
    INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT d.Kho_ID, r.San_Pham_ID, d.Ngay_Xuat_Kho, N'DOCUMENT_DELETE'
    FROM deleted d
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data r ON r.Xuat_Kho_ID = d.Auto_ID
    WHERE d.Is_Posted = 1;
    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
