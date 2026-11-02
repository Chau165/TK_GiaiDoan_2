
/* The current balance is a materialized projection, so its generation must
   advance in the same transaction as every insert/update/delete.  This
   trigger covers Post, reservation writers, rebuilds, and any future writer
   without relying on a caller-controlled marker. */
CREATE   TRIGGER dbo.tr_Inventory_Current_Report_Generation
ON dbo.InventoryBalance_Current
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.Inventory_Current_Report_State
        WHERE State_ID = 1 AND Generation = 9223372036854775807
    )
        THROW 51325, N'Generation báo cáo tồn kho hiện tại đã đạt giới hạn.', 1;

    UPDATE dbo.Inventory_Current_Report_State
    SET Generation = Generation + 1,
        UpdatedAt = SYSUTCDATETIME()
    WHERE State_ID = 1;

    IF @@ROWCOUNT <> 1
        THROW 51325, N'Generation báo cáo tồn kho hiện tại chưa được khởi tạo.', 1;
END
