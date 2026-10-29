/*
  Run only against the approved isolated test database:
  sqlcmd -S DESKTOP-NHQ7QPL\MSSQLSERVER19 -E -d TKS_Thuc_Tap_V11_C01C02_Test_20260906 -b -f 65001 -i Database\Tests\WarehouseMaster.ViewSoftDelete.IntegrationTests.sql

  The script uses one transaction and always rolls it back. It covers active
  View visibility, list/cache/lookup/GetByID calls, insert validation/defaults,
  active update/audit, soft delete, and the no-update-after-delete guard.
*/
SET NOCOUNT ON;
SET XACT_ABORT OFF;

BEGIN TRANSACTION;

BEGIN TRY
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_C01C02_Test_20260906'
        THROW 52000, N'Wrong database. Use the approved isolated master-data test database.', 1;

    DECLARE @UnitId BIGINT = 0;
    DECLARE @CategoryId BIGINT = 0;
    DECLARE @ProductId BIGINT = 0;
    DECLARE @SupplierId BIGINT = 0;
    DECLARE @WarehouseId BIGINT = 0;
    DECLARE @Token NVARCHAR(12) = LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 12);
    DECLARE @UnitName NVARCHAR(200) = N'UT-DVT-' + @Token;
    DECLARE @CategoryCode NVARCHAR(100) = N'UT-LSP-' + @Token;
    DECLARE @CategoryName NVARCHAR(200) = N'UT-LSP-NAME-' + @Token;
    DECLARE @ProductCode NVARCHAR(100) = N'UT-SP-' + @Token;
    DECLARE @ProductName NVARCHAR(255) = N'UT-SP-NAME-' + @Token;
    DECLARE @SupplierCode NVARCHAR(100) = N'UT-NCC-' + @Token;
    DECLARE @SupplierName NVARCHAR(255) = N'UT-NCC-NAME-' + @Token;
    DECLARE @WarehouseName NVARCHAR(255) = N'UT-KHO-' + @Token;

    /* Test 5: required-field validation, valid inserts, and deleted=0 default. */
    DECLARE @RejectedId BIGINT = 0;
    BEGIN TRY
        EXEC dbo.F2016_sp_ins_Don_Vi_Tinh @Auto_ID = @RejectedId OUTPUT, @Ten_Don_Vi_Tinh = N'';
        THROW 52001, N'Expected empty unit name to be rejected.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51001 THROW;
    END CATCH;

    EXEC dbo.F2016_sp_ins_Don_Vi_Tinh @Auto_ID = @UnitId OUTPUT, @Ten_Don_Vi_Tinh = @UnitName, @Ghi_Chu = N'view-soft-delete-test';
    EXEC dbo.F2017_sp_ins_Loai_San_Pham @Auto_ID = @CategoryId OUTPUT, @Ma_LSP = @CategoryCode, @Ten_LSP = @CategoryName, @Ghi_Chu = N'view-soft-delete-test';
    EXEC dbo.F2018_sp_ins_San_Pham @Auto_ID = @ProductId OUTPUT, @Ma_San_Pham = @ProductCode, @Ten_San_Pham = @ProductName, @Loai_San_Pham_ID = @CategoryId, @Don_Vi_Tinh_ID = @UnitId, @Ghi_Chu = N'view-soft-delete-test';
    EXEC dbo.F2019_sp_ins_NCC @Auto_ID = @SupplierId OUTPUT, @Ma_NCC = @SupplierCode, @Ten_NCC = @SupplierName, @Ghi_Chu = N'view-soft-delete-test';
    EXEC dbo.F2009_sp_ins_Kho @Auto_ID = @WarehouseId OUTPUT, @Ten_Kho = @WarehouseName, @Ghi_Chu = N'view-soft-delete-test';

    IF @UnitId = 0 OR @CategoryId = 0 OR @ProductId = 0 OR @SupplierId = 0 OR @WarehouseId = 0
        THROW 52002, N'One of the master insert procedures did not return an Auto_ID.', 1;
    IF (SELECT deleted FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId) <> 0
        THROW 52003, N'Don_Vi_Tinh insert did not set deleted=0.', 1;
    IF (SELECT deleted FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId) <> 0
        THROW 52004, N'Loai_San_Pham insert did not set deleted=0.', 1;
    IF (SELECT deleted FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @ProductId) <> 0
        THROW 52005, N'San_Pham insert did not set deleted=0.', 1;
    IF (SELECT deleted FROM dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId) <> 0
        THROW 52006, N'NCC insert did not set deleted=0.', 1;
    IF (SELECT deleted FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId) <> 0
        THROW 52007, N'Kho insert did not set deleted=0.', 1;

    DECLARE @DuplicateId BIGINT = 0;
    BEGIN TRY
        EXEC dbo.F2016_sp_ins_Don_Vi_Tinh @Auto_ID = @DuplicateId OUTPUT, @Ten_Don_Vi_Tinh = @UnitName;
        THROW 52008, N'Expected duplicate unit name to be rejected.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51002 THROW;
    END CATCH;

    /* Test 1: every active View exposes the inserted row. */
    IF NOT EXISTS (SELECT 1 FROM dbo.view_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId AND Ten_Don_Vi_Tinh = @UnitName)
        THROW 52009, N'Don_Vi_Tinh active View did not expose the inserted row.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.view_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId AND Ma_LSP = @CategoryCode)
        THROW 52010, N'Loai_San_Pham active View did not expose the inserted row.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.view_DM_San_Pham WHERE Auto_ID = @ProductId AND Ma_San_Pham = @ProductCode)
        THROW 52011, N'San_Pham active View did not expose the inserted row.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.view_DM_NCC WHERE Auto_ID = @SupplierId AND Ma_NCC = @SupplierCode)
        THROW 52012, N'NCC active View did not expose the inserted row.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.view_DM_Kho WHERE Auto_ID = @WarehouseId AND Ten_Kho = @WarehouseName)
        THROW 52013, N'Kho active View did not expose the inserted row.', 1;

    /* Tests 2 and 4: list/cache/lookup result sets contain the active rows. */
    DECLARE @UnitRows TABLE (Auto_ID BIGINT, Ten_Don_Vi_Tinh NVARCHAR(200), Ghi_Chu NVARCHAR(1000));
    DECLARE @CategoryRows TABLE (Auto_ID BIGINT, Ma_LSP NVARCHAR(100), Ten_LSP NVARCHAR(200), Ghi_Chu NVARCHAR(1000));
    DECLARE @ProductRows TABLE (Auto_ID BIGINT, Ma_San_Pham NVARCHAR(100), Ten_San_Pham NVARCHAR(255), Loai_San_Pham_ID BIGINT, Don_Vi_Tinh_ID BIGINT, Ghi_Chu NVARCHAR(1000));
    DECLARE @SupplierRows TABLE (Auto_ID BIGINT, Ma_NCC NVARCHAR(100), Ten_NCC NVARCHAR(255), Ghi_Chu NVARCHAR(1000));
    DECLARE @WarehouseRows TABLE (Auto_ID BIGINT, Ten_Kho NVARCHAR(255), Ghi_Chu NVARCHAR(1000));
    DECLARE @UnitLookup TABLE (Auto_ID BIGINT, Ten_Don_Vi_Tinh NVARCHAR(200));
    DECLARE @CategoryLookup TABLE (Auto_ID BIGINT, Ma_LSP NVARCHAR(100), Ten_LSP NVARCHAR(200));
    DECLARE @ProductLookup TABLE (Auto_ID BIGINT, Ma_San_Pham NVARCHAR(100), Ten_San_Pham NVARCHAR(255));
    DECLARE @SupplierLookup TABLE (Auto_ID BIGINT, Ma_NCC NVARCHAR(100), Ten_NCC NVARCHAR(255));
    DECLARE @WarehouseLookup TABLE (Auto_ID BIGINT, Ten_Kho NVARCHAR(255));

    INSERT @UnitRows EXEC dbo.F2016_sp_sel_List_Don_Vi_Tinh;
    IF NOT EXISTS (SELECT 1 FROM @UnitRows WHERE Auto_ID = @UnitId) THROW 52014, N'Don_Vi_Tinh GetList missed the active row.', 1;
    DELETE @UnitRows; INSERT @UnitRows EXEC dbo.F2016_sp_sel_List_Don_Vi_Tinh_For_Cache;
    IF NOT EXISTS (SELECT 1 FROM @UnitRows WHERE Auto_ID = @UnitId) THROW 52015, N'Don_Vi_Tinh cache missed the active row.', 1;
    INSERT @UnitLookup EXEC dbo.F2016_sp_sel_List_Don_Vi_Tinh_Lookup;
    IF NOT EXISTS (SELECT 1 FROM @UnitLookup WHERE Auto_ID = @UnitId) THROW 52016, N'Don_Vi_Tinh lookup missed the active row.', 1;

    INSERT @CategoryRows EXEC dbo.F2017_sp_sel_List_Loai_San_Pham;
    IF NOT EXISTS (SELECT 1 FROM @CategoryRows WHERE Auto_ID = @CategoryId) THROW 52017, N'Loai_San_Pham GetList missed the active row.', 1;
    DELETE @CategoryRows; INSERT @CategoryRows EXEC dbo.F2017_sp_sel_List_Loai_San_Pham_For_Cache;
    IF NOT EXISTS (SELECT 1 FROM @CategoryRows WHERE Auto_ID = @CategoryId) THROW 52018, N'Loai_San_Pham cache missed the active row.', 1;
    INSERT @CategoryLookup EXEC dbo.F2017_sp_sel_List_Loai_San_Pham_Lookup;
    IF NOT EXISTS (SELECT 1 FROM @CategoryLookup WHERE Auto_ID = @CategoryId) THROW 52019, N'Loai_San_Pham lookup missed the active row.', 1;

    INSERT @ProductRows EXEC dbo.F2018_sp_sel_List_San_Pham;
    IF NOT EXISTS (SELECT 1 FROM @ProductRows WHERE Auto_ID = @ProductId) THROW 52020, N'San_Pham GetList missed the active row.', 1;
    DELETE @ProductRows; INSERT @ProductRows EXEC dbo.F2018_sp_sel_List_San_Pham_For_Cache;
    IF NOT EXISTS (SELECT 1 FROM @ProductRows WHERE Auto_ID = @ProductId) THROW 52021, N'San_Pham cache missed the active row.', 1;
    INSERT @ProductLookup EXEC dbo.F2018_sp_sel_List_San_Pham_Lookup;
    IF NOT EXISTS (SELECT 1 FROM @ProductLookup WHERE Auto_ID = @ProductId) THROW 52022, N'San_Pham lookup missed the active row.', 1;

    INSERT @SupplierRows EXEC dbo.F2019_sp_sel_List_NCC;
    IF NOT EXISTS (SELECT 1 FROM @SupplierRows WHERE Auto_ID = @SupplierId) THROW 52023, N'NCC GetList missed the active row.', 1;
    DELETE @SupplierRows; INSERT @SupplierRows EXEC dbo.F2019_sp_sel_List_NCC_For_Cache;
    IF NOT EXISTS (SELECT 1 FROM @SupplierRows WHERE Auto_ID = @SupplierId) THROW 52024, N'NCC cache missed the active row.', 1;
    INSERT @SupplierLookup EXEC dbo.F2019_sp_sel_List_NCC_Lookup;
    IF NOT EXISTS (SELECT 1 FROM @SupplierLookup WHERE Auto_ID = @SupplierId) THROW 52025, N'NCC lookup missed the active row.', 1;

    INSERT @WarehouseRows EXEC dbo.F2009_sp_sel_List_Kho;
    IF NOT EXISTS (SELECT 1 FROM @WarehouseRows WHERE Auto_ID = @WarehouseId) THROW 52026, N'Kho GetList missed the active row.', 1;
    DELETE @WarehouseRows; INSERT @WarehouseRows EXEC dbo.F2009_sp_sel_List_Kho_For_Cache;
    IF NOT EXISTS (SELECT 1 FROM @WarehouseRows WHERE Auto_ID = @WarehouseId) THROW 52027, N'Kho cache missed the active row.', 1;
    INSERT @WarehouseLookup EXEC dbo.F2009_sp_sel_List_Kho_Lookup;
    IF NOT EXISTS (SELECT 1 FROM @WarehouseLookup WHERE Auto_ID = @WarehouseId) THROW 52028, N'Kho lookup missed the active row.', 1;

    /* Test 3: GetByID and ByCreated execute through the active Views. */
    DECLARE @UnitById TABLE (Auto_ID BIGINT, Ten_Don_Vi_Tinh NVARCHAR(200), Ghi_Chu NVARCHAR(1000), Created DATETIME2, Created_By NVARCHAR(100), Created_By_Function NVARCHAR(100), Last_Updated DATETIME2, Last_Updated_By NVARCHAR(100), Last_Updated_By_Function NVARCHAR(100));
    DECLARE @CategoryById TABLE (Auto_ID BIGINT, Ma_LSP NVARCHAR(100), Ten_LSP NVARCHAR(200), Ghi_Chu NVARCHAR(1000), Created DATETIME2, Created_By NVARCHAR(100), Created_By_Function NVARCHAR(100), Last_Updated DATETIME2, Last_Updated_By NVARCHAR(100), Last_Updated_By_Function NVARCHAR(100));
    DECLARE @ProductById TABLE (Auto_ID BIGINT, Ma_San_Pham NVARCHAR(100), Ten_San_Pham NVARCHAR(255), Loai_San_Pham_ID BIGINT, Don_Vi_Tinh_ID BIGINT, Ghi_Chu NVARCHAR(1000), Created DATETIME2, Created_By NVARCHAR(100), Created_By_Function NVARCHAR(100), Last_Updated DATETIME2, Last_Updated_By NVARCHAR(100), Last_Updated_By_Function NVARCHAR(100));
    DECLARE @SupplierById TABLE (Auto_ID BIGINT, Ma_NCC NVARCHAR(100), Ten_NCC NVARCHAR(255), Ghi_Chu NVARCHAR(1000), Created DATETIME2, Created_By NVARCHAR(100), Created_By_Function NVARCHAR(100), Last_Updated DATETIME2, Last_Updated_By NVARCHAR(100), Last_Updated_By_Function NVARCHAR(100));
    DECLARE @WarehouseById TABLE (Auto_ID BIGINT, Ten_Kho NVARCHAR(255), Ghi_Chu NVARCHAR(1000), Created DATETIME2, Created_By NVARCHAR(100), Created_By_Function NVARCHAR(100), Last_Updated DATETIME2, Last_Updated_By NVARCHAR(100), Last_Updated_By_Function NVARCHAR(100));
    INSERT @UnitById EXEC dbo.F2016_sp_sel_Get_By_ID @Auto_ID = @UnitId;
    INSERT @CategoryById EXEC dbo.F2017_sp_sel_Get_By_ID @Auto_ID = @CategoryId;
    INSERT @ProductById EXEC dbo.F2018_sp_sel_Get_By_ID @Auto_ID = @ProductId;
    INSERT @SupplierById EXEC dbo.F2019_sp_sel_Get_By_ID @Auto_ID = @SupplierId;
    INSERT @WarehouseById EXEC dbo.F2009_sp_sel_Get_By_ID @Auto_ID = @WarehouseId;
    IF NOT EXISTS (SELECT 1 FROM @UnitById) OR NOT EXISTS (SELECT 1 FROM @CategoryById) OR NOT EXISTS (SELECT 1 FROM @ProductById) OR NOT EXISTS (SELECT 1 FROM @SupplierById) OR NOT EXISTS (SELECT 1 FROM @WarehouseById)
        THROW 52029, N'One of the GetByID procedures missed an active row.', 1;

    EXEC dbo.F2009_sp_sel_List_Kho_By_Created;
    EXEC dbo.F2016_sp_sel_List_Don_Vi_Tinh_By_Created;
    EXEC dbo.F2017_sp_sel_List_Loai_San_Pham_By_Created;
    EXEC dbo.F2018_sp_sel_List_San_Pham_By_Created;
    EXEC dbo.F2019_sp_sel_List_NCC_By_Created;
    EXEC dbo.sp_DM_Master_Page @Entity = N'Kho', @Page_Number = 1, @Page_Size = 50, @Search_Text = @WarehouseName;
    EXEC dbo.sp_DM_Master_Page @Entity = N'DonViTinh', @Page_Number = 1, @Page_Size = 50, @Search_Text = @UnitName;
    EXEC dbo.sp_DM_Master_Page @Entity = N'LoaiSanPham', @Page_Number = 1, @Page_Size = 50, @Search_Text = @CategoryCode;
    EXEC dbo.sp_DM_Master_Page @Entity = N'SanPham', @Page_Number = 1, @Page_Size = 50, @Search_Text = @ProductCode;
    EXEC dbo.sp_DM_Master_Page @Entity = N'NCC', @Page_Number = 1, @Page_Size = 50, @Search_Text = @SupplierCode;

    /* Test 6: active Update changes audit, then deleted rows reject Update. */
    EXEC dbo.F2016_sp_upd_Don_Vi_Tinh @Auto_ID = @UnitId OUTPUT, @Ten_Don_Vi_Tinh = @UnitName, @Ghi_Chu = N'updated', @Last_Updated_By = N'view-test-user', @Last_Updated_By_Function = N'view-test-function';
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId AND Last_Updated_By = N'view-test-user' AND Last_Updated_By_Function = N'view-test-function')
        THROW 52030, N'Active update did not persist audit fields.', 1;

    /* Test 7: all five deletes are soft deletes and preserve base rows. */
    EXEC dbo.F2018_sp_del_San_Pham @Auto_ID = @ProductId, @Last_Updated_By = N'view-test', @Last_Updated_By_Function = N'view-test';
    EXEC dbo.F2017_sp_del_Loai_San_Pham @Auto_ID = @CategoryId, @Last_Updated_By = N'view-test', @Last_Updated_By_Function = N'view-test';
    EXEC dbo.F2016_sp_del_Don_Vi_Tinh @Auto_ID = @UnitId, @Last_Updated_By = N'view-test', @Last_Updated_By_Function = N'view-test';
    EXEC dbo.F2019_sp_del_NCC @Auto_ID = @SupplierId, @Last_Updated_By = N'view-test', @Last_Updated_By_Function = N'view-test';
    EXEC dbo.F2009_sp_del_Kho @Auto_ID = @WarehouseId, @Last_Updated_By = N'view-test', @Last_Updated_By_Function = N'view-test';

    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId AND deleted = 1)
        THROW 52031, N'Don_Vi_Tinh soft delete did not preserve deleted=1.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId AND deleted = 1)
        THROW 52032, N'Loai_San_Pham soft delete did not preserve deleted=1.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @ProductId AND deleted = 1)
        THROW 52033, N'San_Pham soft delete did not preserve deleted=1.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId AND deleted = 1)
        THROW 52034, N'NCC soft delete did not preserve deleted=1.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId AND deleted = 1)
        THROW 52035, N'Kho soft delete did not preserve deleted=1.', 1;
    IF EXISTS (SELECT 1 FROM dbo.view_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId) OR EXISTS (SELECT 1 FROM dbo.view_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId) OR EXISTS (SELECT 1 FROM dbo.view_DM_San_Pham WHERE Auto_ID = @ProductId) OR EXISTS (SELECT 1 FROM dbo.view_DM_NCC WHERE Auto_ID = @SupplierId) OR EXISTS (SELECT 1 FROM dbo.view_DM_Kho WHERE Auto_ID = @WarehouseId)
        THROW 52036, N'An active master View returned a soft-deleted row.', 1;

    /* Deleted GetByID and lookup results must be empty. */
    DELETE @UnitById; DELETE @CategoryById; DELETE @ProductById; DELETE @SupplierById; DELETE @WarehouseById;
    INSERT @UnitById EXEC dbo.F2016_sp_sel_Get_By_ID @Auto_ID = @UnitId;
    INSERT @CategoryById EXEC dbo.F2017_sp_sel_Get_By_ID @Auto_ID = @CategoryId;
    INSERT @ProductById EXEC dbo.F2018_sp_sel_Get_By_ID @Auto_ID = @ProductId;
    INSERT @SupplierById EXEC dbo.F2019_sp_sel_Get_By_ID @Auto_ID = @SupplierId;
    INSERT @WarehouseById EXEC dbo.F2009_sp_sel_Get_By_ID @Auto_ID = @WarehouseId;
    IF EXISTS (SELECT 1 FROM @UnitById) OR EXISTS (SELECT 1 FROM @CategoryById) OR EXISTS (SELECT 1 FROM @ProductById) OR EXISTS (SELECT 1 FROM @SupplierById) OR EXISTS (SELECT 1 FROM @WarehouseById)
        THROW 52037, N'GetByID returned a soft-deleted row.', 1;

    DELETE @UnitLookup; DELETE @CategoryLookup; DELETE @ProductLookup; DELETE @SupplierLookup; DELETE @WarehouseLookup;
    INSERT @UnitLookup EXEC dbo.F2016_sp_sel_List_Don_Vi_Tinh_Lookup;
    INSERT @CategoryLookup EXEC dbo.F2017_sp_sel_List_Loai_San_Pham_Lookup;
    INSERT @ProductLookup EXEC dbo.F2018_sp_sel_List_San_Pham_Lookup;
    INSERT @SupplierLookup EXEC dbo.F2019_sp_sel_List_NCC_Lookup;
    INSERT @WarehouseLookup EXEC dbo.F2009_sp_sel_List_Kho_Lookup;
    IF EXISTS (SELECT 1 FROM @UnitLookup WHERE Auto_ID = @UnitId) OR EXISTS (SELECT 1 FROM @CategoryLookup WHERE Auto_ID = @CategoryId) OR EXISTS (SELECT 1 FROM @ProductLookup WHERE Auto_ID = @ProductId) OR EXISTS (SELECT 1 FROM @SupplierLookup WHERE Auto_ID = @SupplierId) OR EXISTS (SELECT 1 FROM @WarehouseLookup WHERE Auto_ID = @WarehouseId)
        THROW 52038, N'Lookup returned a soft-deleted row.', 1;

    DELETE @UnitRows; DELETE @CategoryRows; DELETE @ProductRows; DELETE @SupplierRows; DELETE @WarehouseRows;
    INSERT @UnitRows EXEC dbo.F2016_sp_sel_List_Don_Vi_Tinh;
    INSERT @CategoryRows EXEC dbo.F2017_sp_sel_List_Loai_San_Pham;
    INSERT @ProductRows EXEC dbo.F2018_sp_sel_List_San_Pham;
    INSERT @SupplierRows EXEC dbo.F2019_sp_sel_List_NCC;
    INSERT @WarehouseRows EXEC dbo.F2009_sp_sel_List_Kho;
    IF EXISTS (SELECT 1 FROM @UnitRows WHERE Auto_ID = @UnitId) OR EXISTS (SELECT 1 FROM @CategoryRows WHERE Auto_ID = @CategoryId) OR EXISTS (SELECT 1 FROM @ProductRows WHERE Auto_ID = @ProductId) OR EXISTS (SELECT 1 FROM @SupplierRows WHERE Auto_ID = @SupplierId) OR EXISTS (SELECT 1 FROM @WarehouseRows WHERE Auto_ID = @WarehouseId)
        THROW 52039, N'GetList returned a soft-deleted row.', 1;
    DELETE @UnitRows; DELETE @CategoryRows; DELETE @ProductRows; DELETE @SupplierRows; DELETE @WarehouseRows;
    INSERT @UnitRows EXEC dbo.F2016_sp_sel_List_Don_Vi_Tinh_For_Cache;
    INSERT @CategoryRows EXEC dbo.F2017_sp_sel_List_Loai_San_Pham_For_Cache;
    INSERT @ProductRows EXEC dbo.F2018_sp_sel_List_San_Pham_For_Cache;
    INSERT @SupplierRows EXEC dbo.F2019_sp_sel_List_NCC_For_Cache;
    INSERT @WarehouseRows EXEC dbo.F2009_sp_sel_List_Kho_For_Cache;
    IF EXISTS (SELECT 1 FROM @UnitRows WHERE Auto_ID = @UnitId) OR EXISTS (SELECT 1 FROM @CategoryRows WHERE Auto_ID = @CategoryId) OR EXISTS (SELECT 1 FROM @ProductRows WHERE Auto_ID = @ProductId) OR EXISTS (SELECT 1 FROM @SupplierRows WHERE Auto_ID = @SupplierId) OR EXISTS (SELECT 1 FROM @WarehouseRows WHERE Auto_ID = @WarehouseId)
        THROW 52041, N'Cache returned a soft-deleted row.', 1;

    /* The page/by-created calls must still execute after the View filters the rows. */
    EXEC dbo.F2009_sp_sel_List_Kho_By_Created;
    EXEC dbo.F2016_sp_sel_List_Don_Vi_Tinh_By_Created;
    EXEC dbo.F2017_sp_sel_List_Loai_San_Pham_By_Created;
    EXEC dbo.F2018_sp_sel_List_San_Pham_By_Created;
    EXEC dbo.F2019_sp_sel_List_NCC_By_Created;
    EXEC dbo.sp_DM_Lookup_Page @Entity = N'Kho', @Page_Number = 1, @Page_Size = 50, @Search_Text = @WarehouseName;
    EXEC dbo.sp_DM_Lookup_Page @Entity = N'DonViTinh', @Page_Number = 1, @Page_Size = 50, @Search_Text = @UnitName;
    EXEC dbo.sp_DM_Lookup_Page @Entity = N'LoaiSanPham', @Page_Number = 1, @Page_Size = 50, @Search_Text = @CategoryCode;
    EXEC dbo.sp_DM_Lookup_Page @Entity = N'SanPham', @Page_Number = 1, @Page_Size = 50, @Search_Text = @ProductCode;
    EXEC dbo.sp_DM_Lookup_Page @Entity = N'NCC', @Page_Number = 1, @Page_Size = 50, @Search_Text = @SupplierCode;

    BEGIN TRY
        EXEC dbo.F2016_sp_upd_Don_Vi_Tinh @Auto_ID = @UnitId OUTPUT, @Ten_Don_Vi_Tinh = @UnitName, @Ghi_Chu = N'should-fail';
        THROW 52040, N'Expected Update of a soft-deleted row to be rejected.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51003 THROW;
    END CATCH;

    ROLLBACK TRANSACTION;
    PRINT 'PASS: five master Views, active read contracts, and soft-delete procedures';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
