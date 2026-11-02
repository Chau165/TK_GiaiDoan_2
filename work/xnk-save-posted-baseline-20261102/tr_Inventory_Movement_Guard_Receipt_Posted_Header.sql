
/* Posted ledger data is immutable at the application database boundary.
   Draft CRUD remains unchanged.  The canonical Post procedure only changes a
   header from Draft to Posted; it does not need a detail-row bypass.  Database
   owners/sysadmins remain an explicit privileged DBA exception because they
   can intentionally override application permissions. */
CREATE   TRIGGER dbo.tr_Inventory_Movement_Guard_Receipt_Posted_Header
ON dbo.tbl_XNK_Nhap_Kho
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF IS_SRVROLEMEMBER(N'sysadmin') = 1 OR IS_MEMBER(N'db_owner') = 1 RETURN;
    IF EXISTS (SELECT 1 FROM deleted WHERE Is_Posted = 1)
       OR EXISTS
       (
           SELECT 1
           FROM inserted i
           LEFT JOIN deleted d ON d.Auto_ID = i.Auto_ID
           WHERE i.Is_Posted = 1
             AND (d.Auto_ID IS NULL OR d.Is_Posted = 1)
       )
        THROW 51228, N'Không được INSERT, UPDATE hoặc DELETE phiếu nhập đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
