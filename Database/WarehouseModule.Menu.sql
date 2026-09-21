/* Adds the warehouse master-data and operations groups to the dynamic menu. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Master_Data_ID BIGINT = 10000028;
DECLARE @Master_Warehouse_ID BIGINT = 110000100;
DECLARE @Warehouse_Management_ID BIGINT = 110000101;
DECLARE @Receipt_ID BIGINT = 110000102;
DECLARE @Issue_ID BIGINT = 110000103;
DECLARE @Inventory_ID BIGINT = 110000104;
DECLARE @Report_ID BIGINT = 110000105;
DECLARE @Warehouse_Permission_ID BIGINT = 110000106;
DECLARE @Master_Unit_ID BIGINT = 110000107;
DECLARE @Master_Category_ID BIGINT = 110000108;
DECLARE @Master_Product_ID BIGINT = 110000109;
DECLARE @Master_Supplier_ID BIGINT = 110000110;
DECLARE @Administration_ID BIGINT =
(
    SELECT TOP (1) Auto_ID
    FROM dbo.tbl_Sys_Chuc_Nang
    WHERE Chuc_Nang_Parent_ID = 0
      AND ISNULL(deleted, 0) = 0
      AND Ten_Chuc_Nang COLLATE DATABASE_DEFAULT LIKE N'Quản trị%'
    ORDER BY Sort_Priority, Auto_ID
);
DECLARE @System_ID BIGINT =
(
    SELECT TOP (1) Auto_ID
    FROM dbo.tbl_Sys_Chuc_Nang
    WHERE Chuc_Nang_Parent_ID = @Administration_ID
      AND ISNULL(deleted, 0) = 0
      AND Ten_Chuc_Nang COLLATE DATABASE_DEFAULT LIKE N'Hệ thống%'
    ORDER BY Sort_Priority, Auto_ID
);

IF @System_ID IS NULL
    THROW 52105, 'Warehouse menu deployment requires the Hệ thống menu group.', 1;

DECLARE @MenuDefinition TABLE
(
    Auto_ID BIGINT NOT NULL PRIMARY KEY,
    Ma_Chuc_Nang NVARCHAR(100) NOT NULL,
    Ten_Chuc_Nang NVARCHAR(255) NOT NULL,
    Sort_Priority INT NOT NULL,
    Chuc_Nang_Parent_ID BIGINT NOT NULL,
    Nhom_Chuc_Nang_ID INT NOT NULL,
    Func_URL NVARCHAR(500) NOT NULL,
    Image_URL NVARCHAR(255) NOT NULL,
    Is_View BIT NOT NULL,
    Is_New BIT NOT NULL,
    Is_Edit BIT NOT NULL,
    Is_Delete BIT NOT NULL,
    Is_Export BIT NOT NULL,
    Ghi_Chu NVARCHAR(500) NOT NULL
);

INSERT @MenuDefinition
    (Auto_ID, Ma_Chuc_Nang, Ten_Chuc_Nang, Sort_Priority, Chuc_Nang_Parent_ID, Nhom_Chuc_Nang_ID, Func_URL, Image_URL, Is_View, Is_New, Is_Edit, Is_Delete, Is_Export, Ghi_Chu)
VALUES
    (@Master_Warehouse_ID, N'2009', N'Kho', 4, @Master_Data_ID, 1, N'/Kho/Quan_Ly', N'', 1, 1, 1, 1, 1, N'Module thực tập quản lý kho'),
    (@Master_Unit_ID, N'2016', N'Đơn vị tính', 5, @Master_Data_ID, 1, N'/Kho/Don_Vi_Tinh', N'', 1, 1, 1, 1, 1, N'Quản lý đơn vị tính'),
    (@Master_Category_ID, N'2017', N'Loại sản phẩm', 6, @Master_Data_ID, 1, N'/Kho/Loai_San_Pham', N'', 1, 1, 1, 1, 1, N'Quản lý loại sản phẩm'),
    (@Master_Product_ID, N'2018', N'Sản phẩm', 7, @Master_Data_ID, 1, N'/Kho/San_Pham', N'', 1, 1, 1, 1, 1, N'Quản lý sản phẩm'),
    (@Master_Supplier_ID, N'2019', N'Nhà cung cấp', 8, @Master_Data_ID, 1, N'/Kho/Nha_Cung_Cap', N'', 1, 1, 1, 1, 1, N'Quản lý nhà cung cấp'),
    (@Warehouse_Management_ID, N'2010', N'Quản lý kho', 5, 0, 1, N'#', N'ri-archive-line', 1, 0, 0, 0, 0, N'Bài tập thực tập giai đoạn 2'),
    (@Receipt_ID, N'2011', N'Nhập kho', 1, @Warehouse_Management_ID, 1, N'/Kho/Nhap_Kho', N'', 1, 1, 1, 1, 1, N'Quản lý phiếu nhập kho'),
    (@Issue_ID, N'2012', N'Xuất kho', 2, @Warehouse_Management_ID, 1, N'/Kho/Xuat_Kho', N'', 1, 1, 1, 1, 1, N'Quản lý phiếu xuất kho'),
    (@Inventory_ID, N'2013', N'Tồn kho', 3, @Warehouse_Management_ID, 1, N'/Kho/Ton_Kho', N'', 1, 0, 0, 0, 1, N'Xem tồn kho hiện tại'),
    (@Report_ID, N'2014', N'Báo cáo', 4, @Warehouse_Management_ID, 1, N'/Kho/Bao_Cao', N'', 1, 0, 0, 0, 1, N'Báo cáo nhập, xuất và tồn theo kỳ');

INSERT @MenuDefinition
    (Auto_ID, Ma_Chuc_Nang, Ten_Chuc_Nang, Sort_Priority, Chuc_Nang_Parent_ID, Nhom_Chuc_Nang_ID, Func_URL, Image_URL, Is_View, Is_New, Is_Edit, Is_Delete, Is_Export, Ghi_Chu)
VALUES
    (@Warehouse_Permission_ID, N'2015', N'Phân quyền kho-user', 6, @System_ID, 1, N'/Kho/Phan_Quyen', N'', 1, 1, 1, 1, 1, N'Phân quyền user theo kho');

UPDATE target
SET Ma_Chuc_Nang = source.Ma_Chuc_Nang,
    Ten_Chuc_Nang = source.Ten_Chuc_Nang,
    Sort_Priority = source.Sort_Priority,
    Chuc_Nang_Parent_ID = source.Chuc_Nang_Parent_ID,
    Nhom_Chuc_Nang_ID = source.Nhom_Chuc_Nang_ID,
    Func_URL = source.Func_URL,
    Image_URL = source.Image_URL,
    Is_View = source.Is_View,
    Is_New = source.Is_New,
    Is_Edit = source.Is_Edit,
    Is_Delete = source.Is_Delete,
    Is_Export = source.Is_Export,
    Ghi_Chu = source.Ghi_Chu,
    deleted = 0,
    Last_Updated = GETDATE(),
    Last_Updated_By = N'admin',
    Last_Updated_By_Function = N'1001'
FROM dbo.tbl_Sys_Chuc_Nang target
INNER JOIN @MenuDefinition source ON source.Auto_ID = target.Auto_ID;

INSERT dbo.tbl_Sys_Chuc_Nang
    (Auto_ID, Ma_Chuc_Nang, Ten_Chuc_Nang, Sort_Priority, Chuc_Nang_Parent_ID, Nhom_Chuc_Nang_ID, Func_URL, Image_URL, Is_View, Is_New, Is_Edit, Is_Delete, Is_Export, Ghi_Chu, deleted, Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function)
SELECT source.Auto_ID, source.Ma_Chuc_Nang, source.Ten_Chuc_Nang, source.Sort_Priority, source.Chuc_Nang_Parent_ID, source.Nhom_Chuc_Nang_ID, source.Func_URL, source.Image_URL, source.Is_View, source.Is_New, source.Is_Edit, source.Is_Delete, source.Is_Export, source.Ghi_Chu, 0, GETDATE(), N'admin', N'1001', GETDATE(), N'admin', N'1001'
FROM @MenuDefinition source
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.tbl_Sys_Chuc_Nang target
    WHERE target.Auto_ID = source.Auto_ID
);

DECLARE @NextPermissionId BIGINT = ISNULL((SELECT MAX(Auto_ID) FROM dbo.tbl_Sys_Phan_Quyen_Chuc_Nang), 0);
;WITH ActiveGroups AS
(
    SELECT Auto_ID
    FROM dbo.tbl_Sys_Nhom_Thanh_Vien
    WHERE ISNULL(deleted, 0) = 0
), MissingPermissions AS
(
    SELECT g.Auto_ID AS Group_ID,
           f.Auto_ID AS Function_ID,
           f.Is_New,
           f.Is_Edit,
           f.Is_Delete,
           f.Is_Export,
           ROW_NUMBER() OVER (ORDER BY g.Auto_ID, f.Auto_ID) AS Number
    FROM ActiveGroups g
    CROSS JOIN @MenuDefinition f
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.tbl_Sys_Phan_Quyen_Chuc_Nang p
        WHERE p.Nhom_Thanh_Vien_ID = g.Auto_ID
          AND p.Chuc_Nang_ID = f.Auto_ID
          AND ISNULL(p.deleted, 0) = 0
    )
)
INSERT dbo.tbl_Sys_Phan_Quyen_Chuc_Nang
    (Auto_ID, Nhom_Thanh_Vien_ID, Chuc_Nang_ID, Is_Have_View_Permission, Is_Have_Add_Permission, Is_Have_Edit_Permission, Is_Have_Delete_Permission, Is_Have_Export_Permission, deleted, Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function)
SELECT @NextPermissionId + Number,
       Group_ID,
       Function_ID,
       1,
       Is_New,
       Is_Edit,
       Is_Delete,
       Is_Export,
       0,
       GETDATE(),
       N'admin',
       N'1005',
       GETDATE(),
       N'admin',
       N'1005'
FROM MissingPermissions;

/* The original script created 110000100 as a container and 110000101 as its only page.
   Keep existing permission rows usable after turning 110000100 into the master-data page
   and 110000101 into the operations group. */
UPDATE p
SET Is_Have_View_Permission = 1,
    Is_Have_Add_Permission = source.Is_New,
    Is_Have_Edit_Permission = source.Is_Edit,
    Is_Have_Delete_Permission = source.Is_Delete,
    Is_Have_Export_Permission = source.Is_Export,
    deleted = 0,
    Last_Updated = GETDATE(),
    Last_Updated_By = N'admin',
    Last_Updated_By_Function = N'1005'
FROM dbo.tbl_Sys_Phan_Quyen_Chuc_Nang p
INNER JOIN @MenuDefinition source ON source.Auto_ID = p.Chuc_Nang_ID
WHERE EXISTS
(
    SELECT 1
    FROM dbo.tbl_Sys_Nhom_Thanh_Vien g
    WHERE g.Auto_ID = p.Nhom_Thanh_Vien_ID
      AND ISNULL(g.deleted, 0) = 0
);

COMMIT TRANSACTION;
