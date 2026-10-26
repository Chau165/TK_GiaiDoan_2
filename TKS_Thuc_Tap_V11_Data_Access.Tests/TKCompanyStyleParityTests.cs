using System.Data;
using System.Security.Cryptography;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public class TKCompanyStyleParityTests
{
    [Theory]
    [InlineData("TKS_Thuc_Tap_V11_Data_Access/DataLayer/CSqlHelper.cs", "af10feefcea4984a550b8fbf1453265b0b7daef678e59995ed53f7b44c9512f1")]
    [InlineData("TKS_Thuc_Tap_V11_Data_Access/Utility/CConfig.cs", "1fdb148dbecb51f26f5ad15e44767d444a3c0b5158dca04f92c66cdba8d7de2d")]
    [InlineData("TKS_Thuc_Tap_V11_Data_Access/Utility/CConst.cs", "8f5d9d932e82a52fd57541174e7292d3701c9bdaf6d08c0007e715190b9c17f6")]
    [InlineData("TKS_Thuc_Tap_V11_Data_Access/Utility/CExcel_Controller.cs", "8836234abd2e9b8074f8d5345ba8494a01e476ff726514fb30448b88c52b5bbe")]
    [InlineData("TKS_Thuc_Tap_V11_Data_Access/Utility/CNumber.cs", "d71255592256687e44c84ddf480b28cd1c0dc5fa5e94245050145f0c231ad16f")]
    [InlineData("TKS_Thuc_Tap_V11_Data_Access/Utility/CNumberToEnglish.cs", "f915f07026a8658ab88a3e78bfb7807c10d0a1f8d389403933427548aa0d29c9")]
    [InlineData("TKS_Thuc_Tap_V11_Data_Access/Utility/CUtility.cs", "847c53abe7dfc6a0bf3e2f259fdbabcbded376be51fd0a5d8d866f33a9b1a457")]
    [InlineData("TKS_Thuc_Tap_V11_Data_Access/Utility/CUtility_Date.cs", "1c80bf1a81f279bfc0a78237247310e3f6977751a98680b4063a63133b3f65e0")]
    public void Restored_core_bytes_match_original_company_snapshot(string p_strRelativePath, string p_strHash)
    {
        string v_strRoot = Find_Repository();
        Assert.Equal(p_strHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
            Path.Combine(v_strRoot, p_strRelativePath)))).ToLowerInvariant());
    }

    [Fact]
    public void Warehouse_decimal_mapping_preserves_precision_nullable_dates_and_database_nulls()
    {
        using DataTable v_dt = new DataTable();
        v_dt.Columns.Add("Amount", typeof(decimal));
        v_dt.Columns.Add("Count", typeof(string));
        v_dt.Columns.Add("Created", typeof(DateTime));
        v_dt.Columns.Add("Note", typeof(string));
        DateTime v_dtmCreated = new DateTime(2026, 10, 23, 12, 30, 0);
        DataRow v_row = v_dt.Rows.Add(1234567890123.456m, "42", v_dtmCreated, DBNull.Value);
        MappingEntity v_objData = CWMS_Utility.Map_Warehouse_Row<MappingEntity>(v_row);
        Assert.Equal(1234567890123.456m, v_objData.Amount);
        Assert.Equal(42, v_objData.Count);
        Assert.Equal(v_dtmCreated, v_objData.Created);
        Assert.Null(v_objData.Note);
    }

    [Fact]
    public void Company_master_snapshot_clone_still_preserves_creation_metadata()
    {
        var v_objSource = new TKS_Thuc_Tap_V11_Data_Access.Entity.DM.CDM_Don_Vi_Tinh
        {
            Auto_ID = 42, Ten_Don_Vi_Tinh = "Hộp", Created_By = "A", Last_Updated_By = "B"
        };
        var v_objTarget = new TKS_Thuc_Tap_V11_Data_Access.Entity.DM.CDM_Don_Vi_Tinh();
        CUtility.Clone_Entity(v_objSource, v_objTarget);
        Assert.Equal(42, v_objTarget.Auto_ID);
        Assert.Equal("A", v_objTarget.Created_By);
        Assert.Equal("B", v_objTarget.Last_Updated_By);
    }

    [Fact]
    public void Routing_host_no_longer_owns_master_crud_reports_or_permission_data_access()
    {
        string v_strDirectory = Path.Combine(Find_Repository(), "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components");
        string v_strHost = File.ReadAllText(Path.Combine(v_strDirectory, "FWarehouse_1_Warehouse_List.razor"));
        Assert.Contains("<FWarehouse_Report_List", v_strHost);
        Assert.Contains("<FWarehouse_Permission_List", v_strHost);
        Assert.Contains("<F2018_1_San_Pham_List", v_strHost);
        Assert.DoesNotContain("Controller", v_strHost);
        Assert.DoesNotContain("Save_Master_Operation_Async", v_strHost);
        Assert.DoesNotContain("<TelerikGrid", v_strHost);
        string v_strEditor = File.ReadAllText(Path.Combine(v_strDirectory, "FWarehouse_3_Warehouse_Edit.razor"));
        Assert.Contains("OnSave_Document.InvokeAsync", v_strEditor);
        Assert.Contains("OnSave_Detail.InvokeAsync", v_strEditor);
        Assert.DoesNotContain("Save_Kho_Async", v_strEditor);
        Assert.DoesNotContain("m_objSan_Pham", v_strEditor);
        string v_strPermission = File.ReadAllText(Path.Combine(v_strDirectory, "FWarehouse_Permission_List.razor"));
        Assert.Contains("F2015_sp_ins_Kho_User", v_strPermission);
        Assert.Contains("F2015_sp_upd_Kho_User", v_strPermission);
        Assert.Contains("F2015_sp_del_Kho_User", v_strPermission);
        string v_strReport = File.ReadAllText(Path.Combine(v_strDirectory, "FWarehouse_Report_List.razor"));
        Assert.Contains("Read_Inventory_Async", v_strReport);
        Assert.Contains("F9001_sp_Ton_Kho_Hien_Tai_Page", v_strReport);
        Assert.Contains("F9002_sp_Xuat_Nhap_Ton_Page", v_strReport);
        Assert.DoesNotContain("sp_ins", v_strReport);
        Assert.DoesNotContain("sp_upd", v_strReport);
    }

    [Fact]
    public void Original_sql_helper_keeps_decimal_metadata_input_output_and_database_null_values()
    {
        Microsoft.Data.SqlClient.SqlParameter[] v_arrParameters =
        {
            new Microsoft.Data.SqlClient.SqlParameter("@Auto_ID", SqlDbType.BigInt) { Direction = ParameterDirection.InputOutput },
            new Microsoft.Data.SqlClient.SqlParameter("@SL", SqlDbType.Decimal) { Precision = 18, Scale = 3 },
            new Microsoft.Data.SqlClient.SqlParameter("@Ghi_Chu", SqlDbType.NVarChar, 1000)
        };
        object[] v_arrValues = { 42L, 1234567890123.456m, null! };
        System.Reflection.MethodInfo v_objAssign = typeof(TKS_Thuc_Tap_V11_Data_Access.DataLayer.CSqlHelper).GetMethod(
            "AssignParameterValues", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        v_objAssign.Invoke(null, new object[] { v_arrParameters, v_arrValues, "test_metadata_only" });
        Assert.Equal(ParameterDirection.InputOutput, v_arrParameters[0].Direction);
        Assert.Equal(42L, v_arrParameters[0].Value);
        Assert.IsType<decimal>(v_arrParameters[1].Value);
        Assert.Equal(1234567890123.456m, v_arrParameters[1].Value);
        Assert.Equal(18, v_arrParameters[1].Precision);
        Assert.Equal(3, v_arrParameters[1].Scale);
        Assert.Equal(DBNull.Value, v_arrParameters[2].Value);
    }

    private static string Find_Repository()
    {
        DirectoryInfo? v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (v_objDirectory != null)
        {
            if (File.Exists(Path.Combine(v_objDirectory.FullName, "TKS_Thuc_Tap_V11.sln")))
            {
                return v_objDirectory.FullName;
            }
            v_objDirectory = v_objDirectory.Parent;
        }
        throw new DirectoryNotFoundException("Repository not found.");
    }

    public class MappingEntity
    {
        public decimal Amount { get; set; }
        public long Count { get; set; }
        public DateTime? Created { get; set; }
        public string? Note { get; set; }
    }
}
