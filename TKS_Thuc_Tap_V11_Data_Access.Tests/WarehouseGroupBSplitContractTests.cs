using System.Text.RegularExpressions;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseGroupBSplitContractTests
{
    [Fact]
    public void Don_Vi_Tinh_insert_and_update_stores_preserve_the_shared_contract_and_one_write_path_each()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2016_sp_ins_Don_Vi_Tinh");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2016_sp_upd_Don_Vi_Tinh");

        Assert.Contains("@Auto_ID BIGINT OUTPUT", v_strInsert);
        Assert.Contains("@Auto_ID BIGINT OUTPUT", v_strUpdate);
        Assert.Contains("SET XACT_ABORT ON", v_strInsert);
        Assert.Contains("SET XACT_ABORT ON", v_strUpdate);
        Assert.Contains("EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive'", v_strInsert);
        Assert.Contains("EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive'", v_strUpdate);
        Assert.Contains("THROW 51001", v_strInsert);
        Assert.Contains("THROW 51001", v_strUpdate);
        Assert.Contains("THROW 51002", v_strInsert);
        Assert.Contains("THROW 51002", v_strUpdate);
        Assert.Contains("SELECT @Auto_ID AS Auto_ID", v_strInsert);
        Assert.Contains("SELECT @Auto_ID AS Auto_ID", v_strUpdate);
        Assert.Contains("INSERT dbo.tbl_DM_Don_Vi_Tinh", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_DM_Don_Vi_Tinh", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_DM_Don_Vi_Tinh", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_DM_Don_Vi_Tinh", v_strUpdate);
        Assert.Contains("SET @Auto_ID=SCOPE_IDENTITY()", v_strInsert);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
    }

    [Fact]
    public void Don_Vi_Tinh_controller_dispatches_by_zero_or_nonzero_Auto_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var v_strExpectedCase = @"case\s+""DonViTinh""\s*:\s*if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2016_sp_ins_Don_Vi_Tinh"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2016_sp_upd_Don_Vi_Tinh"";\s*\}";

        Assert.Contains("p_objData.Auto_ID == 0", v_strController);
        Assert.Matches(new Regex(v_strExpectedCase, RegexOptions.Singleline), v_strController);
    }

    [Fact]
    public void Kho_insert_and_update_stores_preserve_the_shared_contract_and_one_write_path_each()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2009_sp_ins_Kho");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2009_sp_upd_Kho");

        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ten_Kho NVARCHAR(255)", v_strInsert);
        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ten_Kho NVARCHAR(255)", v_strUpdate);
        Assert.Contains("SET XACT_ABORT ON", v_strInsert);
        Assert.Contains("SET XACT_ABORT ON", v_strUpdate);
        Assert.Contains("sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive'", v_strInsert);
        Assert.Contains("sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive'", v_strUpdate);
        Assert.Contains("THROW 51040", v_strInsert);
        Assert.Contains("THROW 51040", v_strUpdate);
        Assert.Contains("THROW 51041", v_strInsert);
        Assert.Contains("THROW 51041", v_strUpdate);
        Assert.Contains("SELECT @Auto_ID AS Auto_ID", v_strInsert);
        Assert.Contains("SELECT @Auto_ID AS Auto_ID", v_strUpdate);
        Assert.Contains("INSERT dbo.tbl_DM_Kho", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_DM_Kho", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_DM_Kho", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_DM_Kho", v_strUpdate);
        Assert.Contains("SET @Auto_ID=SCOPE_IDENTITY()", v_strInsert);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
    }

    [Fact]
    public void Kho_controller_dispatches_by_zero_or_nonzero_Auto_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var v_strExpectedCase = @"case\s+""Kho""\s*:\s*if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2009_sp_ins_Kho"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2009_sp_upd_Kho"";\s*\}";

        Assert.Matches(new Regex(v_strExpectedCase, RegexOptions.Singleline), v_strController);
    }
    [Fact]
    public void Loai_San_Pham_insert_and_update_stores_preserve_validation_and_one_write_path_each()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2017_sp_ins_Loai_San_Pham");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2017_sp_upd_Loai_San_Pham");

        foreach (var v_strSharedRule in new[]
        {
            "SET XACT_ABORT ON",
            "sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive'",
            "THROW 51010",
            "THROW 51012",
            "THROW 51011",
            "SELECT @Auto_ID AS Auto_ID"
        })
        {
            Assert.Contains(v_strSharedRule, v_strInsert);
            Assert.Contains(v_strSharedRule, v_strUpdate);
        }

        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ma_LSP NVARCHAR(100), @Ten_LSP NVARCHAR(200)", v_strInsert);
        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ma_LSP NVARCHAR(100), @Ten_LSP NVARCHAR(200)", v_strUpdate);
        Assert.Contains("INSERT dbo.tbl_DM_Loai_San_Pham", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_DM_Loai_San_Pham", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_DM_Loai_San_Pham", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_DM_Loai_San_Pham", v_strUpdate);
        Assert.Contains("SET @Auto_ID=SCOPE_IDENTITY()", v_strInsert);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
    }

    [Fact]
    public void Loai_San_Pham_controller_dispatches_by_zero_or_nonzero_Auto_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var v_strExpectedCase = @"case\s+""LoaiSanPham""\s*:\s*if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2017_sp_ins_Loai_San_Pham"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2017_sp_upd_Loai_San_Pham"";\s*\}";

        Assert.Matches(new Regex(v_strExpectedCase, RegexOptions.Singleline), v_strController);
    }
    [Fact]
    public void San_Pham_insert_and_update_stores_preserve_validation_foreign_keys_and_one_write_path_each()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2018_sp_ins_San_Pham");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2018_sp_upd_San_Pham");

        foreach (var v_strSharedRule in new[]
        {
            "SET XACT_ABORT ON",
            "sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive'",
            "THROW 51020",
            "THROW 51022",
            "THROW 51021",
            "THROW 51023",
            "THROW 51024",
            "SELECT @Auto_ID AS Auto_ID"
        })
        {
            Assert.Contains(v_strSharedRule, v_strInsert);
            Assert.Contains(v_strSharedRule, v_strUpdate);
        }

        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ma_San_Pham NVARCHAR(100)", v_strInsert);
        Assert.Contains("@Loai_San_Pham_ID BIGINT, @Don_Vi_Tinh_ID BIGINT", v_strInsert);
        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ma_San_Pham NVARCHAR(100)", v_strUpdate);
        Assert.Contains("@Loai_San_Pham_ID BIGINT, @Don_Vi_Tinh_ID BIGINT", v_strUpdate);
        Assert.Contains("INSERT dbo.tbl_DM_San_Pham", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_DM_San_Pham", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_DM_San_Pham", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_DM_San_Pham", v_strUpdate);
        Assert.Contains("SET @Auto_ID=SCOPE_IDENTITY()", v_strInsert);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
    }

    [Fact]
    public void San_Pham_controller_dispatches_by_zero_or_nonzero_Auto_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var v_strExpectedCase = @"case\s+""SanPham""\s*:\s*if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2018_sp_ins_San_Pham"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2018_sp_upd_San_Pham"";\s*\}";

        Assert.Matches(new Regex(v_strExpectedCase, RegexOptions.Singleline), v_strController);
    }
    [Fact]
    public void NCC_insert_and_update_stores_preserve_validation_and_one_write_path_each()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2019_sp_ins_NCC");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2019_sp_upd_NCC");

        foreach (var v_strSharedRule in new[]
        {
            "SET XACT_ABORT ON",
            "sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive'",
            "THROW 51030",
            "THROW 51032",
            "THROW 51031",
            "SELECT @Auto_ID AS Auto_ID"
        })
        {
            Assert.Contains(v_strSharedRule, v_strInsert);
            Assert.Contains(v_strSharedRule, v_strUpdate);
        }

        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ma_NCC NVARCHAR(100), @Ten_NCC NVARCHAR(255)", v_strInsert);
        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ma_NCC NVARCHAR(100), @Ten_NCC NVARCHAR(255)", v_strUpdate);
        Assert.Contains("INSERT dbo.tbl_DM_NCC", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_DM_NCC", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_DM_NCC", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_DM_NCC", v_strUpdate);
        Assert.Contains("SET @Auto_ID=SCOPE_IDENTITY()", v_strInsert);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
    }

    [Fact]
    public void NCC_controller_dispatches_by_zero_or_nonzero_Auto_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseMaster_Controller.cs"));
        var v_strExpectedCase = @"case\s+""NCC""\s*:\s*if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2019_sp_ins_NCC"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2019_sp_upd_NCC"";\s*\}";

        Assert.Matches(new Regex(v_strExpectedCase, RegexOptions.Singleline), v_strController);
    }
    [Fact]
    public void Kho_User_insert_and_update_stores_preserve_validation_and_one_write_path_each()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2015_sp_ins_Kho_User");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2015_sp_upd_Kho_User");

        foreach (var v_strSharedRule in new[]
        {
            "THROW 51050",
            "THROW 51055",
            "THROW 51051",
            "THROW 51052",
            "SELECT @Auto_ID AS Auto_ID"
        })
        {
            Assert.Contains(v_strSharedRule, v_strInsert);
            Assert.Contains(v_strSharedRule, v_strUpdate);
        }

        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT", v_strInsert);
        Assert.Contains("@Auto_ID BIGINT OUTPUT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT", v_strUpdate);
        Assert.Contains("INSERT dbo.tbl_DM_Kho_User", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_DM_Kho_User", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_DM_Kho_User", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_DM_Kho_User", v_strUpdate);
        Assert.Contains("SET @Auto_ID = SCOPE_IDENTITY()", v_strInsert);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
    }

    [Fact]
    public void Kho_User_controller_dispatches_by_zero_or_nonzero_Permission_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehousePermission_Controller.cs"));
        var v_strExpectedDispatch = @"var\s+v_bIsCreate\s*=\s*p_objData\.Permission_ID\s*==\s*0\s*;\s*string\s+v_strProcedure\s*;\s*if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2015_sp_ins_Kho_User"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2015_sp_upd_Kho_User"";\s*\}\s*p_objData\.Permission_ID\s*=\s*Scalar_ID\s*\(\s*v_strProcedure";

        Assert.Matches(new Regex(v_strExpectedDispatch, RegexOptions.Singleline), v_strController);
    }
    [Fact]
    public void Receipt_Header_split_preserves_fence_savepoint_posted_guard_and_result_contract()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2011_sp_ins_Nhap_Kho_Header");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2011_sp_upd_Nhap_Kho_Header");

        foreach (var v_strSharedRule in new[]
        {
            "@Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100)",
            "SET XACT_ABORT OFF",
            "SAVE TRANSACTION SaveReceiptHeader",
            "sp_Inventory_Fence_Acquire_Root @Mode = N'Shared'",
            "sp_Inventory_Fence_Acquire_Group_Set",
            "sp_Inventory_Fence_Require_Context",
            "SELECT @Auto_ID AS Auto_ID",
            "ROLLBACK TRANSACTION SaveReceiptHeader"
        })
        {
            Assert.Contains(v_strSharedRule, v_strInsert);
            Assert.Contains(v_strSharedRule, v_strUpdate);
        }

        Assert.Contains("INSERT dbo.tbl_XNK_Nhap_Kho", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_XNK_Nhap_Kho", v_strInsert);
        Assert.DoesNotContain("THROW 51163", v_strInsert);
        Assert.Contains("SET @Auto_ID = SCOPE_IDENTITY()", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_XNK_Nhap_Kho", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_XNK_Nhap_Kho", v_strUpdate);
        Assert.Contains("THROW 51163", v_strUpdate);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
    }

    [Fact]
    public void Receipt_Header_controller_dispatches_by_Auto_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));
        var v_strExpectedDispatch = @"if\s*\(\s*p_objData\.Is_Receipt\s*\)\s*\{[\s\S]*?string\s+v_strProcedure\s*;\s*if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2011_sp_ins_Nhap_Kho_Header"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2011_sp_upd_Nhap_Kho_Header"";\s*\}[\s\S]*?Scalar_ID\s*\(\s*v_strProcedure";

        Assert.Matches(new Regex(v_strExpectedDispatch, RegexOptions.Singleline), v_strController);
    }
    [Fact]
    public void Receipt_Detail_split_preserves_fence_savepoint_posted_guard_and_write_contracts()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2011_sp_ins_Nhap_Kho_Detail");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2011_sp_upd_Nhap_Kho_Detail");

        foreach (var v_strSharedRule in new[]
        {
            "@Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT",
            "SET XACT_ABORT OFF",
            "SAVE TRANSACTION SaveReceiptDetail",
            "sp_Inventory_Fence_Acquire_Root @Mode = N'Shared'",
            "sp_Inventory_Fence_Acquire_Group_Set",
            "sp_Inventory_Fence_Require_Context",
            "THROW 51105",
            "THROW 51163",
            "THROW 51106",
            "THROW 51107",
            "THROW 51108",
            "SELECT @Auto_ID AS Auto_ID",
            "ROLLBACK TRANSACTION SaveReceiptDetail"
        })
        {
            Assert.Contains(v_strSharedRule, v_strInsert);
            Assert.Contains(v_strSharedRule, v_strUpdate);
        }

        Assert.Contains("INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data", v_strInsert);
        Assert.Contains("Created, Created_By, Created_By_Function", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data", v_strInsert);
        Assert.Contains("SET @Auto_ID = SCOPE_IDENTITY()", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data", v_strUpdate);
        Assert.Contains("THROW 51110", v_strUpdate);
        Assert.Contains("IF @@ROWCOUNT <> 1 THROW 51109", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data", v_strUpdate);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
    }

    [Fact]
    public void Receipt_Detail_controller_dispatches_by_Auto_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));
        var v_strExpectedDispatch = @"if\s*\(\s*p_bIs_Receipt\s*\)\s*\{[\s\S]*?string\s+v_strProcedure\s*;\s*if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2011_sp_ins_Nhap_Kho_Detail"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2011_sp_upd_Nhap_Kho_Detail"";\s*\}[\s\S]*?Scalar_ID\s*\(\s*v_strProcedure";

        Assert.Matches(new Regex(v_strExpectedDispatch, RegexOptions.Singleline), v_strController);
    }

    [Fact]
    public void Issue_Header_split_preserves_fences_move_reservation_and_single_write_paths()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2012_sp_ins_Xuat_Kho_Header");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2012_sp_upd_Xuat_Kho_Header");

        foreach (var v_strSharedRule in new[]
        {
            "@Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100)",
            "SET XACT_ABORT OFF",
            "SAVE TRANSACTION SaveIssueHeader",
            "sp_Inventory_Fence_Acquire_Root @Mode = N'Shared'",
            "sp_Inventory_Fence_Acquire_Group_Set",
            "sp_Inventory_Fence_Acquire_Legacy_Scope_Set",
            "sp_Inventory_Fence_Require_Context",
            "THROW 51130",
            "THROW 51133",
            "SELECT @Auto_ID AS Auto_ID",
            "ROLLBACK TRANSACTION SaveIssueHeader"
        })
        {
            Assert.Contains(v_strSharedRule, v_strInsert);
            Assert.Contains(v_strSharedRule, v_strUpdate);
        }

        Assert.Contains("INSERT dbo.tbl_XNK_Xuat_Kho", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_XNK_Xuat_Kho", v_strInsert);
        Assert.Contains("SET @Auto_ID = SCOPE_IDENTITY()", v_strInsert);
        Assert.DoesNotContain("sp_XNK_Reservation_Move_Document", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_XNK_Xuat_Kho", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_XNK_Xuat_Kho", v_strUpdate);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
        Assert.Contains("THROW 51163", v_strUpdate);
        Assert.Contains("sp_XNK_Reservation_Move_Document @Auto_ID, @Old_Kho_ID, @Kho_ID", v_strUpdate);
    }

    [Fact]
    public void Issue_Header_controller_dispatches_by_Auto_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));
        var v_strExpectedDispatch = @"else\s*\{\s*string\?\s+v_strCreatedBy;[\s\S]*?string\s+v_strProcedure\s*;\s*if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2012_sp_ins_Xuat_Kho_Header"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2012_sp_upd_Xuat_Kho_Header"";\s*\}[\s\S]*?Scalar_ID\s*\(\s*v_strProcedure";

        Assert.Matches(new Regex(v_strExpectedDispatch, RegexOptions.Singleline), v_strController);
    }

    [Fact]
    public void Issue_Detail_split_preserves_reservation_and_single_business_write_paths()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F2012_sp_ins_Xuat_Kho_Detail");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F2012_sp_upd_Xuat_Kho_Detail");

        foreach (var v_strSharedRule in new[]
        {
            "@Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2)",
            "SET XACT_ABORT OFF",
            "SAVE TRANSACTION SaveIssueDetail",
            "sp_Inventory_Fence_Acquire_Root @Mode = N'Shared'",
            "sp_Inventory_Fence_Acquire_Group_Set",
            "sp_Inventory_Fence_Acquire_Legacy_Scope_Set",
            "sp_Inventory_Fence_Require_Context",
            "sp_XNK_Reservation_Adjust @Kho_ID, @San_Pham_ID, @ReservationDelta",
            "THROW 51134",
            "THROW 51163",
            "THROW 51135",
            "THROW 51136",
            "THROW 51137",
            "SELECT @Auto_ID AS Auto_ID",
            "ROLLBACK TRANSACTION SaveIssueDetail"
        })
        {
            Assert.Contains(v_strSharedRule, v_strInsert);
            Assert.Contains(v_strSharedRule, v_strUpdate);
        }

        Assert.Contains("INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data", v_strInsert);
        Assert.Contains("INSERT dbo.InventoryReservation_Current", v_strInsert);
        Assert.Contains("SET @Auto_ID = SCOPE_IDENTITY()", v_strInsert);
        Assert.DoesNotContain("UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data", v_strInsert);
        Assert.Contains("UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data", v_strUpdate);
        Assert.Contains("UPDATE dbo.InventoryReservation_Current", v_strUpdate);
        Assert.Contains("INSERT dbo.InventoryReservation_Current", v_strUpdate);
        Assert.Contains("DECLARE @ReleaseDelta DECIMAL(18,3) = -@Old_ReservedQuantity", v_strUpdate);
        Assert.Contains("sp_XNK_Reservation_Adjust @Reservation_Kho_ID, @San_Pham_ID, @ReleaseDelta", v_strUpdate);
        Assert.DoesNotContain("INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data", v_strUpdate);
        Assert.DoesNotContain("SCOPE_IDENTITY()", v_strUpdate);
    }

    [Fact]
    public void Issue_Detail_controller_dispatches_by_Auto_ID()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Warehouse", "CWarehouseDocument_Controller.cs"));
        var v_strExpectedDispatch = @"if\s*\(\s*p_bIs_Receipt\s*\)\s*\{[\s\S]*?F2011_sp_ins_Nhap_Kho_Detail[\s\S]*?\}\s*else\s*\{[\s\S]*?if\s*\(\s*v_bIsCreate\s*\)\s*\{\s*v_strProcedure\s*=\s*""F2012_sp_ins_Xuat_Kho_Detail"";\s*\}\s*else\s*\{\s*v_strProcedure\s*=\s*""F2012_sp_upd_Xuat_Kho_Detail"";\s*\}[\s\S]*?Scalar_ID\s*\(\s*v_strProcedure";

        Assert.Matches(new Regex(v_strExpectedDispatch, RegexOptions.Singleline), v_strController);
    }

    [Fact]
    public void Retired_Save_stores_are_absent_from_deployable_SQL_manifests()
    {
        var v_arrLegacyNames = new[]
        {
            "sp_DM_Don_Vi_Tinh_Save",
            "sp_DM_Kho_Save",
            "sp_DM_Kho_User_Save",
            "sp_DM_Loai_San_Pham_Save",
            "sp_DM_NCC_Save",
            "sp_DM_San_Pham_Save",
            "sp_XNK_Nhap_Kho_Save_Header",
            "sp_XNK_Nhap_Kho_Save_Detail",
            "sp_XNK_Xuat_Kho_Save_Header",
            "sp_XNK_Xuat_Kho_Save_Detail"
        };
        var v_arrDeployableSqlPaths = new[]
        {
            FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"),
            FindRepositoryPath("Database", "WarehouseModule.Security.sql"),
            FindRepositoryPath("Database", "WarehouseModule.Security.Preflight.sql")
        };

        foreach (var v_strDeployableSqlPath in v_arrDeployableSqlPaths)
        {
            var v_strDeployableSql = File.ReadAllText(v_strDeployableSqlPath);
            foreach (var v_strLegacyName in v_arrLegacyNames)
            {
                Assert.DoesNotContain(v_strLegacyName, v_strDeployableSql);
            }
        }
    }

    private static string ExtractProcedure(string p_strSource, string p_strProcedureName)
    {
        var v_strPattern = $@"(?ims)^CREATE\s+OR\s+ALTER\s+PROCEDURE\s+dbo\.{Regex.Escape(p_strProcedureName)}\b(?<body>.*?)^GO\s*$";
        var v_objMatch = Regex.Match(p_strSource, v_strPattern);
        Assert.True(v_objMatch.Success, $"Procedure definition not found: {p_strProcedureName}");
        return v_objMatch.Value;
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            var v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
            {
                return v_strCandidate;
            }
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
