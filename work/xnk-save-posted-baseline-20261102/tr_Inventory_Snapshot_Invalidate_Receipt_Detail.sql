
CREATE   TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Receipt_Detail
ON dbo.tbl_XNK_Nhap_Kho_Raw_Data
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Affected dbo.InventorySnapshotAffectedType;

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Nhap_Kho, N'DOCUMENT_UPDATE'
        FROM inserted x JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = x.Nhap_Kho_ID
        WHERE h.Is_Posted = 1
        UNION
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Nhap_Kho, N'DOCUMENT_UPDATE'
        FROM deleted x JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = x.Nhap_Kho_ID
        WHERE h.Is_Posted = 1;
    END
    ELSE IF EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Nhap_Kho, N'DOCUMENT_DELETE'
        FROM deleted x JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = x.Nhap_Kho_ID
        WHERE h.Is_Posted = 1;
    END
    ELSE
    BEGIN
        INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Nhap_Kho, N'DOCUMENT_UPDATE'
        FROM inserted x JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = x.Nhap_Kho_ID
        WHERE h.Is_Posted = 1;
    END

    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
