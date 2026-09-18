/* Phase 10.1 application database boundary.
   This script defines a least-privilege role but intentionally does not map an
   unknown production login.  The deployment owner must add the real
   application database user to dbo.Warehouse_Application after verifying its
   identity.  No password, CONTROL DATABASE, db_owner, or IMPERSONATE grant is
   required by this contract. */
IF DATABASE_PRINCIPAL_ID(N'Warehouse_Application') IS NULL
    EXEC(N'CREATE ROLE [Warehouse_Application];');
GO

/* The four canonical ledger tables are procedure-owned.  A member of the
   application role cannot directly create, change, or delete ledger rows,
   including by first setting a client-controlled SESSION_CONTEXT value. */
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Nhap_Kho TO [Warehouse_Application];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Nhap_Kho_Raw_Data TO [Warehouse_Application];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Xuat_Kho TO [Warehouse_Application];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Xuat_Kho_Raw_Data TO [Warehouse_Application];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.InventoryBalance_Current TO [Warehouse_Application];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.InventoryReservation_Current TO [Warehouse_Application];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.Inventory_Current_Report_State TO [Warehouse_Application];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.Inventory_Report_Fence_Config TO [Warehouse_Application];
GO

/* Approved Warehouse entry points.  Stored procedures and their dbo-owned
   nested calls use ownership chaining; the role receives no direct table DML. */
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Nhap_Kho_Save_Header TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Nhap_Kho_Save_Detail TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Nhap_Kho_Delete_Header TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Nhap_Kho_Delete_Detail TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Xuat_Kho_Save_Header TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Xuat_Kho_Save_Detail TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Xuat_Kho_Delete_Header TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Xuat_Kho_Delete_Detail TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Document_Post TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Document_List TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Document_Detail_List TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_XNK_Document_Page TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_BC_Chi_Tiet_Nhap TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_BC_Chi_Tiet_Xuat TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_BC_Chi_Tiet_Nhap_Page TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_BC_Chi_Tiet_Xuat_Page TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_BC_Xuat_Nhap_Ton TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_BC_Xuat_Nhap_Ton_Page TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_BC_Ton_Kho_Hien_Tai_Page TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_DM_Kho_User_List_Allowed TO [Warehouse_Application];
GRANT EXECUTE ON OBJECT::dbo.sp_DM_Kho_User_Delete TO [Warehouse_Application];
GO
