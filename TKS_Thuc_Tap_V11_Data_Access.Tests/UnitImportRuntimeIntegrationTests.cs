using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Services;
using Xunit;
using Xunit.Abstractions;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class UnitImportRuntimeIntegrationTests
{
    private const string m_strTestUser = "UAT_UNIT_RUNTIME";
    private const string m_strTestFunction = "2016";
    private readonly ITestOutputHelper m_objOutput;

    public UnitImportRuntimeIntegrationTests(ITestOutputHelper p_objOutput)
    {
        m_objOutput = p_objOutput;
    }

    [Fact]
    public async Task Unit_import_runtime_proves_preview_insert_duplicate_atomic_rollback_and_cleanup()
    {
        var v_strConnectionString = ProductImportTestDatabase.ValidateConnectionString(
            Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING") ?? "");
        var v_strPreviousConnectionString = CConfig.TKS_Thuc_Tap_V11_Conn_String;
        var v_strRunId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var v_strPrefix = $"UAT_UNIT_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{v_strRunId}_";
        await using var v_objDatabase = await ProductImportTestDatabase.OpenVerifiedConnectionAsync();
        CConfig.TKS_Thuc_Tap_V11_Conn_String = v_strConnectionString;

        try
        {
            await EnsureTargetIdentityAsync(v_objDatabase);
            Assert.Equal(0L, await CountPrefixAsync(v_objDatabase, v_strPrefix));
            m_objOutput.WriteLine($"TEST_DB={await GetDatabaseIdentityAsync(v_objDatabase)}");
            m_objOutput.WriteLine($"TEST_PREFIX={v_strPrefix}");

            var v_arrNames = Enumerable.Range(1, 10)
                .Select(v_iIndex => $"{v_strPrefix}VALID_{v_iIndex:00}")
                .ToArray();
            var v_objService = new CWarehouseUnitImportService(new CAllowAddPermission());
            await using (var v_objPreviewStream = new MemoryStream(CreateWorkbook(v_arrNames), writable: false))
            {
                var v_objPreview = await v_objService.PreviewAsync("units.xlsx", v_objPreviewStream, m_strTestUser, m_strTestFunction);
                Assert.True(v_objPreview.Can_Confirm);
                Assert.Equal(10, v_objPreview.Valid_Rows);
                Assert.Equal(0L, await CountPrefixAsync(v_objDatabase, v_strPrefix));
                m_objOutput.WriteLine("PREVIEW_NO_WRITE=0");

                Assert.Equal(10, await v_objService.ConfirmAsync(v_objPreview, m_strTestUser, m_strTestFunction));
            }
            Assert.Equal(10L, await CountPrefixAsync(v_objDatabase, v_strPrefix));
            m_objOutput.WriteLine("VALID_BATCH_INSERTED=10");

            await using (var v_objDuplicateStream = new MemoryStream(CreateWorkbook(v_arrNames[0]), writable: false))
            {
                var v_objDuplicatePreview = await v_objService.PreviewAsync("units.xlsx", v_objDuplicateStream, m_strTestUser, m_strTestFunction);
                Assert.False(v_objDuplicatePreview.Can_Confirm);
                Assert.Contains(v_objDuplicatePreview.Errors, v_objError => v_objError.Message.Contains("đã tồn tại", StringComparison.Ordinal));
            }

            var v_objDuplicateException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CWarehouseUnitImportService().Import_Units_Async(
                    new[] { new CUnitImportRow { Row_Number = 2, Ten_Don_Vi_Tinh = v_arrNames[0] } },
                    m_strTestUser,
                    m_strTestFunction));
            Assert.Contains("đã tồn tại", v_objDuplicateException.Message, StringComparison.Ordinal);
            Assert.Equal(10L, await CountPrefixAsync(v_objDatabase, v_strPrefix));
            m_objOutput.WriteLine("DATABASE_DUPLICATE_REJECTED=PASS");

            var v_strRollbackPrefix = v_strPrefix + "ROLLBACK_";
            var v_objRollbackException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CWarehouseUnitImportService().Import_Units_Async(
                    new[]
                    {
                        new CUnitImportRow { Row_Number = 2, Ten_Don_Vi_Tinh = v_strRollbackPrefix + "FIRST" },
                        new CUnitImportRow { Row_Number = 3, Ten_Don_Vi_Tinh = "" }
                    },
                    m_strTestUser,
                    m_strTestFunction));
            Assert.Contains("toàn bộ lô đã được hủy", v_objRollbackException.Message, StringComparison.Ordinal);
            Assert.Equal(0L, await CountPrefixAsync(v_objDatabase, v_strRollbackPrefix));
            m_objOutput.WriteLine("MID_BATCH_FAILURE_ROW=3");
            m_objOutput.WriteLine("ROLLBACK_REMAINDER=0");
        }
        finally
        {
            try
            {
                await EnsureTargetIdentityAsync(v_objDatabase);
                await DeletePrefixAsync(v_objDatabase, v_strPrefix);
                Assert.Equal(0L, await CountPrefixAsync(v_objDatabase, v_strPrefix));
                m_objOutput.WriteLine("CLEANUP_PREFIX_ROWS=0");
            }
            finally
            {
                CConfig.TKS_Thuc_Tap_V11_Conn_String = v_strPreviousConnectionString;
            }
        }
    }

    private static byte[] CreateWorkbook(params string[] p_arrNames)
    {
        using var v_objWorkbook = new XLWorkbook();
        var v_objSheet = v_objWorkbook.Worksheets.Add("DonViTinh");
        v_objSheet.Cell(1, 1).Value = "Ten_Don_Vi_Tinh";
        v_objSheet.Cell(1, 2).Value = "Ghi_Chu";
        for (var v_iIndex = 0; v_iIndex < p_arrNames.Length; v_iIndex++)
            v_objSheet.Cell(v_iIndex + 2, 1).Value = p_arrNames[v_iIndex];
        using var v_objStream = new MemoryStream();
        v_objWorkbook.SaveAs(v_objStream);
        return v_objStream.ToArray();
    }

    private static async Task<long> CountPrefixAsync(SqlConnection p_objConnection, string p_strPrefix)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM dbo.tbl_DM_Don_Vi_Tinh WHERE LEFT(Ten_Don_Vi_Tinh,LEN(@Prefix))=@Prefix;",
            p_objConnection);
        v_objCommand.Parameters.Add("@Prefix", System.Data.SqlDbType.NVarChar, 200).Value = p_strPrefix;
        return Convert.ToInt64(await v_objCommand.ExecuteScalarAsync());
    }

    private static async Task DeletePrefixAsync(SqlConnection p_objConnection, string p_strPrefix)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        await using var v_objCommand = new SqlCommand(
            "DELETE dbo.tbl_DM_Don_Vi_Tinh WHERE LEFT(Ten_Don_Vi_Tinh,LEN(@Prefix))=@Prefix;",
            p_objConnection);
        v_objCommand.Parameters.Add("@Prefix", System.Data.SqlDbType.NVarChar, 200).Value = p_strPrefix;
        await v_objCommand.ExecuteNonQueryAsync();
    }

    private static async Task EnsureTargetIdentityAsync(SqlConnection p_objConnection)
    {
        await using var v_objCommand = new SqlCommand("SELECT @@SERVERNAME, DB_NAME();", p_objConnection);
        await using var v_objReader = await v_objCommand.ExecuteReaderAsync();
        Assert.True(await v_objReader.ReadAsync());
        Assert.Equal(@"DESKTOP-NHQ7QPL\MSSQLSERVER19", v_objReader.GetString(0), ignoreCase: true);
        Assert.Equal("TKS_Thuc_Tap_V11_C01C02_Test_20260906", v_objReader.GetString(1), ignoreCase: true);
    }

    private static async Task<string> GetDatabaseIdentityAsync(SqlConnection p_objConnection)
    {
        await EnsureTargetIdentityAsync(p_objConnection);
        return $"{p_objConnection.DataSource}/{p_objConnection.Database}";
    }

    private sealed class CAllowAddPermission : IUnitImportPermissionResolver
    {
        public bool Has_Add_Permission(string p_strUserName, string p_strFunctionCode) => true;
    }
}
