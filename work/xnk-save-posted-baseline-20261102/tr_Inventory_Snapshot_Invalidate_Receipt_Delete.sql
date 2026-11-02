
CREATE   TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Receipt_Delete
ON dbo.tbl_XNK_Nhap_Kho
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Affected dbo.InventorySnapshotAffectedType;
    INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT d.Kho_ID, r.San_Pham_ID, d.Ngay_Nhap_Kho, N'DOCUMENT_DELETE'
    FROM deleted d
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data r ON r.Nhap_Kho_ID = d.Auto_ID
    WHERE d.Is_Posted = 1;
    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
