/* Adds one warehouse module item to the dynamic left menu and grants active groups access. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Parent_ID BIGINT = 110000100;
DECLARE @Function_ID BIGINT = 110000101;

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Chuc_Nang WHERE Auto_ID = @Parent_ID)
INSERT dbo.tbl_Sys_Chuc_Nang
    (Auto_ID, Ma_Chuc_Nang, Ten_Chuc_Nang, Sort_Priority, Chuc_Nang_Parent_ID, Nhom_Chuc_Nang_ID, Func_URL, Image_URL, Is_View, Is_New, Is_Edit, Is_Delete, Is_Export, Ghi_Chu, deleted, Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function)
VALUES
    (@Parent_ID, N'2009', N'Kho', 4, 10000028, 1, N'#', N'ri-archive-line', 1, 0, 0, 0, 0, N'Module thực tập quản lý kho', 0, GETDATE(), N'admin', N'1001', GETDATE(), N'admin', N'1001');

IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Chuc_Nang WHERE Auto_ID = @Function_ID)
INSERT dbo.tbl_Sys_Chuc_Nang
    (Auto_ID, Ma_Chuc_Nang, Ten_Chuc_Nang, Sort_Priority, Chuc_Nang_Parent_ID, Nhom_Chuc_Nang_ID, Func_URL, Image_URL, Is_View, Is_New, Is_Edit, Is_Delete, Is_Export, Ghi_Chu, deleted, Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function)
VALUES
    (@Function_ID, N'2010', N'Quản lý kho', 1, @Parent_ID, 1, N'/Kho/Quan_Ly', N'', 1, 1, 1, 1, 1, N'Bài tập thực tập giai đoạn 2', 0, GETDATE(), N'admin', N'1001', GETDATE(), N'admin', N'1001');

DECLARE @NextPermissionId BIGINT = ISNULL((SELECT MAX(Auto_ID) FROM dbo.tbl_Sys_Phan_Quyen_Chuc_Nang), 0);
;WITH ActiveGroups AS
(
    SELECT Auto_ID, ROW_NUMBER() OVER (ORDER BY Auto_ID) AS Number FROM dbo.tbl_Sys_Nhom_Thanh_Vien WHERE ISNULL(deleted, 0) = 0
), MissingPermissions AS
(
    SELECT g.Auto_ID AS Group_ID, f.Function_ID, ROW_NUMBER() OVER (ORDER BY g.Auto_ID, f.Function_ID) AS Number
    FROM ActiveGroups g CROSS JOIN (VALUES (@Parent_ID), (@Function_ID)) f(Function_ID)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Phan_Quyen_Chuc_Nang p WHERE p.Nhom_Thanh_Vien_ID = g.Auto_ID AND p.Chuc_Nang_ID = f.Function_ID AND ISNULL(p.deleted, 0) = 0)
)
INSERT dbo.tbl_Sys_Phan_Quyen_Chuc_Nang
    (Auto_ID, Nhom_Thanh_Vien_ID, Chuc_Nang_ID, Is_Have_View_Permission, Is_Have_Add_Permission, Is_Have_Edit_Permission, Is_Have_Delete_Permission, Is_Have_Export_Permission, deleted, Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function)
SELECT @NextPermissionId + Number, Group_ID, Function_ID, 1,
       CASE WHEN Function_ID = @Function_ID THEN 1 ELSE NULL END,
       CASE WHEN Function_ID = @Function_ID THEN 1 ELSE NULL END,
       CASE WHEN Function_ID = @Function_ID THEN 1 ELSE NULL END,
       CASE WHEN Function_ID = @Function_ID THEN 1 ELSE NULL END,
       0, GETDATE(), N'admin', N'1005', GETDATE(), N'admin', N'1005'
FROM MissingPermissions;

COMMIT TRANSACTION;
