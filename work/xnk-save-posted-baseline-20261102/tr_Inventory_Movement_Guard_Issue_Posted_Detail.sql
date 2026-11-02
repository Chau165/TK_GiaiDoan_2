
CREATE   TRIGGER dbo.tr_Inventory_Movement_Guard_Issue_Posted_Detail
ON dbo.tbl_XNK_Xuat_Kho_Raw_Data
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF IS_SRVROLEMEMBER(N'sysadmin') = 1 OR IS_MEMBER(N'db_owner') = 1 RETURN;
    IF EXISTS
    (
        SELECT 1
        FROM inserted d
        JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID
        WHERE h.Is_Posted = 1
    )
       OR EXISTS
    (
        SELECT 1
        FROM deleted d
        JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID
        WHERE h.Is_Posted = 1
    )
         THROW 51228, N'Không được INSERT, UPDATE hoặc DELETE chi tiết phiếu xuất đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
