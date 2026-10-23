using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Approved company import SQL")]
public sealed class CompanyOriginalImportSqlTests
{
    [ApprovedImportDatabaseTheory]
    [InlineData("Unit", "tbl_DM_Don_Vi_Tinh", "Ten_Don_Vi_Tinh", 51002)]
    [InlineData("Category", "tbl_DM_Loai_San_Pham", "Ma_LSP", 51011)]
    [InlineData("Product", "tbl_DM_San_Pham", "Ma_San_Pham", 51021)]
    [InlineData("Supplier", "tbl_DM_NCC", "Ma_NCC", 51031)]
    [InlineData("Warehouse", "tbl_DM_Kho", "Ten_Kho", 51041)]
    public async Task Insert_procedure_rejects_duplicates_and_rollback_removes_fixture(
        string p_strModule, string p_strTable, string p_strKeyColumn, int p_iExpectedError)
    {
        await using SqlConnection v_conn = await ProductImportTestDatabase.OpenVerifiedConnectionAsync();
        using var v_objConfiguration = new CTestConnectionScope(v_conn.ConnectionString);
        string v_strKey = "import_test_" + Guid.NewGuid().ToString("N");
        using (SqlTransaction v_trans = v_conn.BeginTransaction())
        {
            Func<long> v_funcInsert = p_strModule switch
            {
                "Unit" => () => new CDM_Don_Vi_Tinh_Controller().F2016_sp_ins_Don_Vi_Tinh(v_conn, v_trans,
                    new CDM_Don_Vi_Tinh { Ten_Don_Vi_Tinh = v_strKey }, "test_A", "2016"),
                "Category" => () => new CDM_Loai_San_Pham_Controller().F2017_sp_ins_Loai_San_Pham(v_conn, v_trans,
                    new CDM_Loai_San_Pham { Ma_LSP = v_strKey, Ten_LSP = v_strKey }, "test_A", "2017"),
                "Supplier" => () => new CDM_NCC_Controller().F2019_sp_ins_NCC(v_conn, v_trans,
                    new CDM_NCC { Ma_NCC = v_strKey, Ten_NCC = v_strKey }, "test_A", "2019"),
                "Warehouse" => () => new CDM_Kho_Controller().F2009_sp_ins_Kho(v_conn, v_trans,
                    new CDM_Kho { Ten_Kho = v_strKey }, "test_A", "2009"),
                "Product" => ProductInsert(v_conn, v_trans, v_strKey),
                _ => throw new ArgumentOutOfRangeException(nameof(p_strModule))
            };
            try
            {
                Assert.True(v_funcInsert() > 0);
                SqlException v_objError = Assert.Throws<SqlException>(() => v_funcInsert());
                Assert.Contains(v_objError.Errors.Cast<SqlError>(), v_objItem => v_objItem.Number == p_iExpectedError);
            }
            finally
            {
                // Only the approved test database; never commit these test rows.
                v_trans.Rollback();
            }
        }
        await using var v_cmd = new SqlCommand($"SELECT COUNT_BIG(*) FROM dbo.[{p_strTable}] WHERE [{p_strKeyColumn}] = @Key", v_conn);
        v_cmd.Parameters.AddWithValue("@Key", v_strKey);
        Assert.Equal(0L, (long)(await v_cmd.ExecuteScalarAsync())!);
    }

    private static Func<long> ProductInsert(SqlConnection p_conn, SqlTransaction p_trans, string p_strKey)
    {
        long v_iUnit = new CDM_Don_Vi_Tinh_Controller().F2016_sp_ins_Don_Vi_Tinh(p_conn, p_trans,
            new CDM_Don_Vi_Tinh { Ten_Don_Vi_Tinh = p_strKey }, "test_A", "2016");
        long v_iCategory = new CDM_Loai_San_Pham_Controller().F2017_sp_ins_Loai_San_Pham(p_conn, p_trans,
            new CDM_Loai_San_Pham { Ma_LSP = p_strKey, Ten_LSP = p_strKey }, "test_A", "2017");
        return () => new CDM_San_Pham_Controller().F2018_sp_ins_San_Pham(p_conn, p_trans,
            new CDM_San_Pham { Ma_San_Pham = p_strKey, Ten_San_Pham = p_strKey, Loai_San_Pham_ID = v_iCategory, Don_Vi_Tinh_ID = v_iUnit }, "test_A", "2018");
    }

    private sealed class CTestConnectionScope : IDisposable
    {
        private readonly string m_strPrevious = CConfig.TKS_Thuc_Tap_V11_Conn_String;
        public CTestConnectionScope(string p_strConnection) =>
            CConfig.TKS_Thuc_Tap_V11_Conn_String = ProductImportTestDatabase.ValidateConnectionString(p_strConnection);
        public void Dispose() => CConfig.TKS_Thuc_Tap_V11_Conn_String = m_strPrevious;
    }
}

[CollectionDefinition("Approved company import SQL", DisableParallelization = true)]
public sealed class ApprovedCompanyImportSqlCollection { }

internal sealed class ApprovedImportDatabaseTheoryAttribute : TheoryAttribute
{
    public ApprovedImportDatabaseTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING")))
            Skip = "Requires the owner-approved test database connection; business targets are rejected by ProductImportTestDatabase.";
    }
}
