using System.Data;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase6M02M03IntegrationTests : IAsyncLifetime
{
    private static string BaseConnectionString
    {
        get
        {
            var v_ConnectionString = Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING");
            if (v_ConnectionString == null)
            {
                throw new InvalidOperationException("TKS_INTEGRATION_CONNECTION_STRING must point to a disposable test database.");
            }

            return v_ConnectionString;
        }
    }

    private Fixture? m_objFixture;

    public async Task InitializeAsync()
    {
        CConfig.TKS_Thuc_Tap_V11_Conn_String = BaseConnectionString;
        m_objFixture = await CreateFixtureAsync();
    }

    public async Task DisposeAsync()
    {
        if (m_objFixture is not null)
            await CleanupFixtureAsync(m_objFixture);
    }

    [Fact]
    public async Task Actor_fields_persist_for_receipt_issue_detail_and_post_without_overwriting_create_actor()
    {
        var v_Fixture = m_objFixture!;
        var v_Controller = new CWarehouseDocument_Controller();
        var v_CreatedBy = v_Fixture.LoginA;
        var v_UpdatedBy = v_Fixture.LoginB;
        const string v_FunctionCode = "Warehouse.Phase6";

        var v_objReceipt = new CWarehouseDocument
        {
            Is_Receipt = true,
            So_Phieu = $"{v_Fixture.Tag}-receipt",
            Kho_ID = v_Fixture.WarehouseId,
            NCC_ID = v_Fixture.SupplierId,
            Ngay_Chung_Tu = new DateTime(2099, 7, 1),
            Ghi_Chu = "phase 6 actor test"
        };

        await v_Controller.Save_Document_Async(v_objReceipt, v_CreatedBy, v_FunctionCode, v_Fixture.LoginA);
        var v_CreatedReceipt = await ReadHeaderAsync(v_objReceipt.Auto_ID, true);
        Assert.Equal(v_CreatedBy, v_CreatedReceipt.CreatedBy);
        Assert.Equal(v_CreatedBy, v_CreatedReceipt.LastUpdatedBy);
        Assert.Equal(v_FunctionCode, v_CreatedReceipt.CreatedByFunction);
        Assert.Equal(v_FunctionCode, v_CreatedReceipt.LastUpdatedByFunction);
        Assert.NotNull(v_CreatedReceipt.CreatedAt);
        Assert.NotNull(v_CreatedReceipt.LastUpdatedAt);

        Exception? v_SaveHistoryFailure = null;
        Assert.False(CWarehouseActionHistory_Recorder.TryRecord(
            () => throw new InvalidOperationException("deterministic save history failure"),
            exception => v_SaveHistoryFailure = exception));
        Assert.NotNull(v_SaveHistoryFailure);
        Assert.Equal(v_CreatedBy, (await ReadHeaderAsync(v_objReceipt.Auto_ID, true)).CreatedBy);

        v_objReceipt.So_Phieu += "-updated";
        await v_Controller.Save_Document_Async(v_objReceipt, v_UpdatedBy, v_FunctionCode, v_Fixture.LoginB);
        var v_UpdatedReceipt = await ReadHeaderAsync(v_objReceipt.Auto_ID, true);
        Assert.Equal(v_CreatedBy, v_UpdatedReceipt.CreatedBy);
        Assert.Equal(v_UpdatedBy, v_UpdatedReceipt.LastUpdatedBy);
        Assert.Equal(v_FunctionCode, v_UpdatedReceipt.CreatedByFunction);
        Assert.Equal(v_FunctionCode, v_UpdatedReceipt.LastUpdatedByFunction);

        var v_objReceiptDetail = new CWarehouseDocumentDetail
        {
            Document_ID = v_objReceipt.Auto_ID,
            San_Pham_ID = v_Fixture.ProductId,
            So_Luong = 20m,
            Don_Gia = 1m
        };
        await v_Controller.Save_Document_Detail_Async(true, v_objReceiptDetail, v_CreatedBy, v_FunctionCode, v_Fixture.LoginA);
        var v_CreatedReceiptDetail = await ReadDetailAsync(v_objReceiptDetail.Auto_ID, true);
        Assert.Equal(v_CreatedBy, v_CreatedReceiptDetail.CreatedBy);
        Assert.Equal(v_CreatedBy, v_CreatedReceiptDetail.LastUpdatedBy);
        Assert.NotNull(v_CreatedReceiptDetail.CreatedAt);
        Assert.NotNull(v_CreatedReceiptDetail.LastUpdatedAt);

        v_objReceiptDetail.So_Luong = 21m;
        await v_Controller.Save_Document_Detail_Async(true, v_objReceiptDetail, v_UpdatedBy, v_FunctionCode, v_Fixture.LoginB);
        var v_UpdatedReceiptDetail = await ReadDetailAsync(v_objReceiptDetail.Auto_ID, true);
        Assert.Equal(v_CreatedBy, v_UpdatedReceiptDetail.CreatedBy);
        Assert.Equal(v_UpdatedBy, v_UpdatedReceiptDetail.LastUpdatedBy);

        await v_Controller.Post_Document_Async(true, v_objReceipt.Auto_ID, v_UpdatedBy, v_FunctionCode, v_Fixture.LoginB);
        var v_PostedReceipt = await ReadHeaderAsync(v_objReceipt.Auto_ID, true);
        Assert.True(v_PostedReceipt.IsPosted);
        Assert.NotNull(v_PostedReceipt.PostedAt);
        Assert.Equal(v_CreatedBy, v_PostedReceipt.CreatedBy);
        Assert.Equal(v_UpdatedBy, v_PostedReceipt.LastUpdatedBy);

        Exception? v_PostHistoryFailure = null;
        Assert.False(CWarehouseActionHistory_Recorder.TryRecord(
            () => throw new InvalidOperationException("deterministic post history failure"),
            exception => v_PostHistoryFailure = exception));
        Assert.NotNull(v_PostHistoryFailure);
        Assert.True((await ReadHeaderAsync(v_objReceipt.Auto_ID, true)).IsPosted);

        await Assert.ThrowsAsync<SqlException>(() =>
            v_Controller.Save_Document_Async(v_objReceipt, "rejected-actor", v_FunctionCode, v_Fixture.LoginB));
        var v_AfterRejectedUpdate = await ReadHeaderAsync(v_objReceipt.Auto_ID, true);
        Assert.Equal(v_UpdatedBy, v_AfterRejectedUpdate.LastUpdatedBy);

        var v_objIssue = new CWarehouseDocument
        {
            Is_Receipt = false,
            So_Phieu = $"{v_Fixture.Tag}-issue",
            Kho_ID = v_Fixture.WarehouseId,
            Ngay_Chung_Tu = new DateTime(2099, 7, 2),
            Ghi_Chu = "phase 6 actor test"
        };

        await v_Controller.Save_Document_Async(v_objIssue, v_CreatedBy, v_FunctionCode, v_Fixture.LoginA);
        var v_CreatedIssue = await ReadHeaderAsync(v_objIssue.Auto_ID, false);
        Assert.Equal(v_CreatedBy, v_CreatedIssue.CreatedBy);
        Assert.Equal(v_CreatedBy, v_CreatedIssue.LastUpdatedBy);

        v_objIssue.So_Phieu += "-updated";
        await v_Controller.Save_Document_Async(v_objIssue, v_UpdatedBy, v_FunctionCode, v_Fixture.LoginB);
        var v_UpdatedIssue = await ReadHeaderAsync(v_objIssue.Auto_ID, false);
        Assert.Equal(v_CreatedBy, v_UpdatedIssue.CreatedBy);
        Assert.Equal(v_UpdatedBy, v_UpdatedIssue.LastUpdatedBy);

        var v_objIssueDetail = new CWarehouseDocumentDetail
        {
            Document_ID = v_objIssue.Auto_ID,
            San_Pham_ID = v_Fixture.ProductId,
            So_Luong = 2m,
            Don_Gia = 1m
        };
        await v_Controller.Save_Document_Detail_Async(false, v_objIssueDetail, v_CreatedBy, v_FunctionCode, v_Fixture.LoginA);
        var v_CreatedIssueDetail = await ReadDetailAsync(v_objIssueDetail.Auto_ID, false);
        Assert.Equal(v_CreatedBy, v_CreatedIssueDetail.CreatedBy);
        Assert.Equal(v_CreatedBy, v_CreatedIssueDetail.LastUpdatedBy);

        v_objIssueDetail.So_Luong = 3m;
        await v_Controller.Save_Document_Detail_Async(false, v_objIssueDetail, v_UpdatedBy, v_FunctionCode, v_Fixture.LoginB);
        var v_UpdatedIssueDetail = await ReadDetailAsync(v_objIssueDetail.Auto_ID, false);
        Assert.Equal(v_CreatedBy, v_UpdatedIssueDetail.CreatedBy);
        Assert.Equal(v_UpdatedBy, v_UpdatedIssueDetail.LastUpdatedBy);
    }

    [Fact]
    public void Action_history_failure_is_a_secondary_outcome_and_does_not_throw_to_business_caller()
    {
        Exception? v_Captured = null;
        var v_bHistorySucceeded = CWarehouseActionHistory_Recorder.TryRecord(
            () => throw new InvalidOperationException("deterministic phase 6 history failure"),
            exception => v_Captured = exception);

        Assert.False(v_bHistorySucceeded);
        Assert.IsType<InvalidOperationException>(v_Captured);
    }

    [Fact]
    public void Action_history_success_is_reported_separately_from_business_success()
    {
        var v_iHistoryCalls = 0;
        var v_bHistorySucceeded = CWarehouseActionHistory_Recorder.TryRecord(
            () => v_iHistoryCalls++,
            _ => throw new InvalidOperationException("history failure callback must not run on success"));

        Assert.True(v_bHistorySucceeded);
        Assert.Equal(1, v_iHistoryCalls);
    }

    [Fact]
    public void Warehouse_ui_uses_secondary_action_history_outcome_contract()
    {
        var v_Source = File.ReadAllText(FindRepositoryFile(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("CWarehouseActionHistory_Recorder.TryRecord", v_Source, StringComparison.Ordinal);
        Assert.Contains("m_strAuditWarning", v_Source, StringComparison.Ordinal);
        Assert.Contains("Nghiệp vụ đã thành công nhưng chưa ghi được lịch sử thao tác.", v_Source, StringComparison.Ordinal);
    }

    private static async Task<HeaderAudit> ReadHeaderAsync(long documentId, bool p_bIsReceipt)
    {
        await using var v_Connection = new SqlConnection(BaseConnectionString);
        await v_Connection.OpenAsync();
        string v_Table;
        if (p_bIsReceipt == true)
        {
            v_Table = "tbl_XNK_Nhap_Kho";
        }
        else
        {
            v_Table = "tbl_XNK_Xuat_Kho";
        }
        var v_Sql = $"SELECT Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function, Is_Posted, Posted_At FROM dbo.{v_Table} WHERE Auto_ID = @Id;";
        await using var v_Command = new SqlCommand(v_Sql, v_Connection);
        v_Command.Parameters.Add(BigInt("@Id", documentId));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        return new HeaderAudit(
            ReadDateTime(v_Reader, 0), ReadString(v_Reader, 1), ReadString(v_Reader, 2),
            ReadDateTime(v_Reader, 3), ReadString(v_Reader, 4), ReadString(v_Reader, 5),
            v_Reader.GetBoolean(6), ReadDateTime(v_Reader, 7));
    }

    private static async Task<DetailAudit> ReadDetailAsync(long detailId, bool p_bIsReceipt)
    {
        await using var v_Connection = new SqlConnection(BaseConnectionString);
        await v_Connection.OpenAsync();
        string v_Table;
        if (p_bIsReceipt == true)
        {
            v_Table = "tbl_XNK_Nhap_Kho_Raw_Data";
        }
        else
        {
            v_Table = "tbl_XNK_Xuat_Kho_Raw_Data";
        }
        var v_Sql = $"SELECT Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function FROM dbo.{v_Table} WHERE Auto_ID = @Id;";
        await using var v_Command = new SqlCommand(v_Sql, v_Connection);
        v_Command.Parameters.Add(BigInt("@Id", detailId));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        return new DetailAudit(
            ReadDateTime(v_Reader, 0), ReadString(v_Reader, 1), ReadString(v_Reader, 2),
            ReadDateTime(v_Reader, 3), ReadString(v_Reader, 4), ReadString(v_Reader, 5));
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var v_Tag = $"P6-{Guid.NewGuid():N}"[..18];
        var v_LoginA = $"{v_Tag}-a";
        var v_LoginB = $"{v_Tag}-b";

        await using var v_Connection = new SqlConnection(BaseConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        try
        {
            var productId = await LongScalarAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await LongScalarAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 6 actor test');",
                Text("@Name", $"{v_Tag}-warehouse", 255));
            var memberId = await LongScalarAsync(v_Connection, v_Transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, Trang_Thai_ID, deleted) VALUES (@MemberA, @LoginA, @NameA, 1, 0), (@MemberB, @LoginB, @NameB, 1, 0);",
                BigInt("@MemberA", memberId), BigInt("@MemberB", memberId + 1),
                Text("@LoginA", v_LoginA, 100), Text("@LoginB", v_LoginB, 100),
                Text("@NameA", $"{v_Tag}-member-a", 200), Text("@NameB", $"{v_Tag}-member-b", 200));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@LoginA, @WarehouseId), (@LoginB, @WarehouseId);",
                Text("@LoginA", v_LoginA, 100), Text("@LoginB", v_LoginB, 100), BigInt("@WarehouseId", warehouseId));
            await v_Transaction.CommitAsync();
            return new Fixture(v_Tag, v_LoginA, v_LoginB, productId, supplierId, warehouseId);
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CleanupFixtureAsync(Fixture p_Fixture)
    {
        await using var v_Connection = new SqlConnection(BaseConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE r FROM dbo.InventoryReservation_Current r JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.Kho_ID = @WarehouseId; DELETE d FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE h.Kho_ID = @WarehouseId; DELETE d FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Kho_ID = @WarehouseId; DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId; DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB); DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB); DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId),
                Text("@LoginA", p_Fixture.LoginA, 100), Text("@LoginB", p_Fixture.LoginB, 100));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static string FindRepositoryFile(string p_RelativePath)
    {
        var v_Directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (v_Directory is not null)
        {
            var v_Candidate = Path.Combine(v_Directory.FullName, p_RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(v_Candidate))
                return v_Candidate;
            v_Directory = v_Directory.Parent;
        }

        var v_Fallback = Path.Combine("P:", "Giao đoạn 2 TK", "Giao đoạn 2 TK", "TKS_Thuc_Tap_11", p_RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(v_Fallback))
            return v_Fallback;
        throw new FileNotFoundException(p_RelativePath);
    }

    private static async Task<long> InsertIdAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return Convert.ToInt64(await v_Command.ExecuteScalarAsync());
    }

    private static async Task<long> LongScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static string? ReadString(SqlDataReader p_Reader, int p_iOrdinal)
    {
        if (p_Reader.IsDBNull(p_iOrdinal))
        {
            return null;
        }
        else
        {
            return p_Reader.GetString(p_iOrdinal);
        }
    }

    private static DateTime? ReadDateTime(SqlDataReader p_Reader, int p_iOrdinal)
    {
        if (p_Reader.IsDBNull(p_iOrdinal))
        {
            return null;
        }
        else
        {
            return p_Reader.GetDateTime(p_iOrdinal);
        }
    }

    private static SqlParameter BigInt(string p_Name, long value)
    {
        return new(p_Name, SqlDbType.BigInt)
        {
            Value = value
        };
    }
    private static SqlParameter Text(string p_Name, string p_Value, int p_iSize)
    {
        return new(p_Name, SqlDbType.NVarChar, p_iSize)
        {
            Value = p_Value
        };
    }

    private sealed record Fixture(string Tag, string LoginA, string LoginB, long ProductId, long SupplierId, long WarehouseId);
    private sealed record HeaderAudit(DateTime? CreatedAt, string? CreatedBy, string? CreatedByFunction, DateTime? LastUpdatedAt, string? LastUpdatedBy, string? LastUpdatedByFunction, bool IsPosted, DateTime? PostedAt);
    private sealed record DetailAudit(DateTime? CreatedAt, string? CreatedBy, string? CreatedByFunction, DateTime? LastUpdatedAt, string? LastUpdatedBy, string? LastUpdatedByFunction);
}
