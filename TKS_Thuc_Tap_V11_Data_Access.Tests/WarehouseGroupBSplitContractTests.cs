using System.Text.RegularExpressions;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseGroupBSplitContractTests
{
    [Fact]
    public void Approved_function_codes_and_transaction_store_prefixes_are_used()
    {
        var v_strMenu = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Menu.sql"));
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strReceiptController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CXNK_Nhap_Kho_Controller.cs"));
        var v_strIssueController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CXNK_Xuat_Kho_Controller.cs"));
        var v_strSecurity = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Security.sql"));
        var v_strPreflight = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Security.Preflight.sql"));

        Assert.Matches(new Regex(@"(?m)^\s*\(@Receipt_ID,\s*N'3001',\s*N'Nhập kho'.*N'/Kho/Nhap_Kho'"), v_strMenu);
        Assert.Matches(new Regex(@"(?m)^\s*\(@Issue_ID,\s*N'3002',\s*N'Xuất kho'.*N'/Kho/Xuat_Kho'"), v_strMenu);
        Assert.Matches(new Regex(@"(?m)^\s*\(@Inventory_ID,\s*N'9001',\s*N'Tồn kho'.*N'/Kho/Ton_Kho'"), v_strMenu);
        Assert.Matches(new Regex(@"(?m)^\s*\(@Report_ID,\s*N'9002',\s*N'Báo cáo'.*N'/Kho/Bao_Cao'"), v_strMenu);
        Assert.Equal(8, Regex.Matches(v_strProcedures, @"(?im)^CREATE\s+OR\s+ALTER\s+PROCEDURE\s+dbo\.F3001_").Count);
        Assert.Equal(8, Regex.Matches(v_strProcedures, @"(?im)^CREATE\s+OR\s+ALTER\s+PROCEDURE\s+dbo\.F3002_").Count);
        Assert.DoesNotContain("F2011_sp_", v_strProcedures);
        Assert.DoesNotContain("F2012_sp_", v_strProcedures);
        Assert.Contains("F3001_sp_ins_Nhap_Kho_Header", v_strReceiptController);
        Assert.Contains("F3002_sp_ins_Xuat_Kho_Header", v_strIssueController);
        Assert.Contains("GRANT EXECUTE ON OBJECT::dbo.F3001_sp_ins_Nhap_Kho_Header", v_strSecurity);
        Assert.Contains("GRANT EXECUTE ON OBJECT::dbo.F3002_sp_ins_Xuat_Kho_Header", v_strSecurity);
        Assert.Contains("dbo.F3001_sp_ins_Nhap_Kho_Header", v_strPreflight);
        Assert.Contains("dbo.F3002_sp_ins_Xuat_Kho_Header", v_strPreflight);
    }

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
    public void Don_Vi_Tinh_caller_selects_insert_or_update_by_Auto_ID()
    {
        string v_strEditor = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_Unit_Edit.razor"));
        Assert.Contains("protected override void Add_Data()", v_strEditor);
        Assert.Contains("protected override void Update_Data()", v_strEditor);
        Assert.Contains("_sp_ins_", v_strEditor);
        Assert.Contains("_sp_upd_", v_strEditor);
        Assert.Contains("Save_Data()", v_strEditor);
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
    public void Kho_caller_selects_insert_or_update_by_Auto_ID()
    {
        string v_strEditor = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2009_3_Kho_Edit.razor"));
        Assert.Contains("protected override void Add_Data()", v_strEditor);
        Assert.Contains("protected override void Update_Data()", v_strEditor);
        Assert.Contains("_sp_ins_", v_strEditor);
        Assert.Contains("_sp_upd_", v_strEditor);
        Assert.Contains("Save_Data()", v_strEditor);
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
    public void Loai_San_Pham_caller_selects_insert_or_update_by_Auto_ID()
    {
        string v_strEditor = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2017_3_Loai_San_Pham_Edit.razor"));
        Assert.Contains("protected override void Add_Data()", v_strEditor);
        Assert.Contains("protected override void Update_Data()", v_strEditor);
        Assert.Contains("_sp_ins_", v_strEditor);
        Assert.Contains("_sp_upd_", v_strEditor);
        Assert.Contains("Save_Data()", v_strEditor);
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
    public void San_Pham_caller_selects_insert_or_update_by_Auto_ID()
    {
        string v_strEditor = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2018_3_San_Pham_Edit.razor"));
        Assert.Contains("protected override void Add_Data()", v_strEditor);
        Assert.Contains("protected override void Update_Data()", v_strEditor);
        Assert.Contains("_sp_ins_", v_strEditor);
        Assert.Contains("_sp_upd_", v_strEditor);
        Assert.Contains("Save_Data()", v_strEditor);
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
    public void NCC_caller_selects_insert_or_update_by_Auto_ID()
    {
        string v_strEditor = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "F2019_3_Nha_Cung_Cap_Edit.razor"));
        Assert.Contains("protected override void Add_Data()", v_strEditor);
        Assert.Contains("protected override void Update_Data()", v_strEditor);
        Assert.Contains("_sp_ins_", v_strEditor);
        Assert.Contains("_sp_upd_", v_strEditor);
        Assert.Contains("Save_Data()", v_strEditor);
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
    public void Kho_User_controller_exposes_separate_insert_and_update_stores()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", "CDM_Kho_User_Controller.cs"));

        Assert.Contains("F2015_sp_ins_Kho_User", v_strController);
        Assert.Contains("F2015_sp_upd_Kho_User", v_strController);
        Assert.DoesNotContain("v_strProcedure", v_strController);
        Assert.DoesNotContain("Save_Kho_User_Async", v_strController);
    }
    [Fact]
    public void Receipt_Header_split_preserves_fence_savepoint_posted_guard_and_result_contract()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F3001_sp_ins_Nhap_Kho_Header");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F3001_sp_upd_Nhap_Kho_Header");

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
    public void Receipt_Header_controller_exposes_separate_insert_and_update_store_methods()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CXNK_Nhap_Kho_Controller.cs"));
        Assert.Contains("\"F3001_sp_ins_Nhap_Kho_Header\"", v_strController);
        Assert.Contains("CSqlHelper.ExecuteScalar", v_strController);
        Assert.Contains("\"F3001_sp_upd_Nhap_Kho_Header\"", v_strController);
        Assert.Contains("CSqlHelper.ExecuteScalar", v_strController);
        Assert.DoesNotContain("p_bIs_Receipt", v_strController);
        Assert.DoesNotContain("Auto_ID == 0", v_strController);
    }
    [Fact]
    public void Receipt_Detail_split_preserves_fence_savepoint_posted_guard_and_write_contracts()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F3001_sp_ins_Nhap_Kho_Detail");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F3001_sp_upd_Nhap_Kho_Detail");

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
    public void Receipt_Detail_controller_exposes_separate_insert_and_update_store_methods()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CXNK_Nhap_Kho_Controller.cs"));
        Assert.Contains("\"F3001_sp_ins_Nhap_Kho_Detail\"", v_strController);
        Assert.Contains("CSqlHelper.ExecuteScalar", v_strController);
        Assert.Contains("\"F3001_sp_upd_Nhap_Kho_Detail\"", v_strController);
        Assert.Contains("CSqlHelper.ExecuteScalar", v_strController);
        Assert.DoesNotContain("p_bIs_Receipt", v_strController);
        Assert.DoesNotContain("Auto_ID == 0", v_strController);
    }

    [Fact]
    public void Issue_Header_split_preserves_fences_move_reservation_and_single_write_paths()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F3002_sp_ins_Xuat_Kho_Header");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F3002_sp_upd_Xuat_Kho_Header");

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
    public void Issue_Header_controller_exposes_separate_insert_and_update_store_methods()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CXNK_Xuat_Kho_Controller.cs"));
        Assert.Contains("\"F3002_sp_ins_Xuat_Kho_Header\"", v_strController);
        Assert.Contains("CSqlHelper.ExecuteScalar", v_strController);
        Assert.Contains("\"F3002_sp_upd_Xuat_Kho_Header\"", v_strController);
        Assert.Contains("CSqlHelper.ExecuteScalar", v_strController);
        Assert.DoesNotContain("p_bIs_Receipt", v_strController);
        Assert.DoesNotContain("Auto_ID == 0", v_strController);
    }

    [Fact]
    public void Issue_Detail_split_preserves_reservation_and_single_business_write_paths()
    {
        var v_strProcedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        var v_strInsert = ExtractProcedure(v_strProcedures, "F3002_sp_ins_Xuat_Kho_Detail");
        var v_strUpdate = ExtractProcedure(v_strProcedures, "F3002_sp_upd_Xuat_Kho_Detail");

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
    public void Issue_Detail_controller_exposes_separate_insert_and_update_store_methods()
    {
        var v_strController = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "XNK", "CXNK_Xuat_Kho_Controller.cs"));
        Assert.Contains("\"F3002_sp_ins_Xuat_Kho_Detail\"", v_strController);
        Assert.Contains("CSqlHelper.ExecuteScalar", v_strController);
        Assert.Contains("\"F3002_sp_upd_Xuat_Kho_Detail\"", v_strController);
        Assert.Contains("CSqlHelper.ExecuteScalar", v_strController);
        Assert.DoesNotContain("p_bIs_Receipt", v_strController);
        Assert.DoesNotContain("Auto_ID == 0", v_strController);
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
