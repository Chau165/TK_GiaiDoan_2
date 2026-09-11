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
    private static string BaseConnectionString =>
        Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "TKS_INTEGRATION_CONNECTION_STRING must point to a disposable test database.");

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
        var fixture = m_objFixture!;
        var controller = new CWarehouseDocument_Controller();
        var createdBy = fixture.LoginA;
        var updatedBy = fixture.LoginB;
        const string functionCode = "Warehouse.Phase6";

        var receipt = new CWarehouseDocument
        {
            Is_Receipt = true,
            So_Phieu = $"{fixture.Tag}-receipt",
            Kho_ID = fixture.WarehouseId,
            NCC_ID = fixture.SupplierId,
            Ngay_Chung_Tu = new DateTime(2099, 7, 1),
            Ghi_Chu = "phase 6 actor test"
        };

        await controller.Save_Document_Async(receipt, createdBy, functionCode, fixture.LoginA);
        var createdReceipt = await ReadHeaderAsync(receipt.Auto_ID, true);
        Assert.Equal(createdBy, createdReceipt.CreatedBy);
        Assert.Equal(createdBy, createdReceipt.LastUpdatedBy);
        Assert.Equal(functionCode, createdReceipt.CreatedByFunction);
        Assert.Equal(functionCode, createdReceipt.LastUpdatedByFunction);
        Assert.NotNull(createdReceipt.CreatedAt);
        Assert.NotNull(createdReceipt.LastUpdatedAt);

        Exception? saveHistoryFailure = null;
        Assert.False(CWarehouseActionHistory_Recorder.TryRecord(
            () => throw new InvalidOperationException("deterministic save history failure"),
            exception => saveHistoryFailure = exception));
        Assert.NotNull(saveHistoryFailure);
        Assert.Equal(createdBy, (await ReadHeaderAsync(receipt.Auto_ID, true)).CreatedBy);

        receipt.So_Phieu += "-updated";
        await controller.Save_Document_Async(receipt, updatedBy, functionCode, fixture.LoginB);
        var updatedReceipt = await ReadHeaderAsync(receipt.Auto_ID, true);
        Assert.Equal(createdBy, updatedReceipt.CreatedBy);
        Assert.Equal(updatedBy, updatedReceipt.LastUpdatedBy);
        Assert.Equal(functionCode, updatedReceipt.CreatedByFunction);
        Assert.Equal(functionCode, updatedReceipt.LastUpdatedByFunction);

        var receiptDetail = new CWarehouseDocumentDetail
        {
            Document_ID = receipt.Auto_ID,
            San_Pham_ID = fixture.ProductId,
            So_Luong = 20m,
            Don_Gia = 1m
        };
        await controller.Save_Document_Detail_Async(true, receiptDetail, createdBy, functionCode, fixture.LoginA);
        var createdReceiptDetail = await ReadDetailAsync(receiptDetail.Auto_ID, true);
        Assert.Equal(createdBy, createdReceiptDetail.CreatedBy);
        Assert.Equal(createdBy, createdReceiptDetail.LastUpdatedBy);
        Assert.NotNull(createdReceiptDetail.CreatedAt);
        Assert.NotNull(createdReceiptDetail.LastUpdatedAt);

        receiptDetail.So_Luong = 21m;
        await controller.Save_Document_Detail_Async(true, receiptDetail, updatedBy, functionCode, fixture.LoginB);
        var updatedReceiptDetail = await ReadDetailAsync(receiptDetail.Auto_ID, true);
        Assert.Equal(createdBy, updatedReceiptDetail.CreatedBy);
        Assert.Equal(updatedBy, updatedReceiptDetail.LastUpdatedBy);

        await controller.Post_Document_Async(true, receipt.Auto_ID, updatedBy, functionCode, fixture.LoginB);
        var postedReceipt = await ReadHeaderAsync(receipt.Auto_ID, true);
        Assert.True(postedReceipt.IsPosted);
        Assert.NotNull(postedReceipt.PostedAt);
        Assert.Equal(createdBy, postedReceipt.CreatedBy);
        Assert.Equal(updatedBy, postedReceipt.LastUpdatedBy);

        Exception? postHistoryFailure = null;
        Assert.False(CWarehouseActionHistory_Recorder.TryRecord(
            () => throw new InvalidOperationException("deterministic post history failure"),
            exception => postHistoryFailure = exception));
        Assert.NotNull(postHistoryFailure);
        Assert.True((await ReadHeaderAsync(receipt.Auto_ID, true)).IsPosted);

        await Assert.ThrowsAsync<SqlException>(() =>
            controller.Save_Document_Async(receipt, "rejected-actor", functionCode, fixture.LoginB));
        var afterRejectedUpdate = await ReadHeaderAsync(receipt.Auto_ID, true);
        Assert.Equal(updatedBy, afterRejectedUpdate.LastUpdatedBy);

        var issue = new CWarehouseDocument
        {
            Is_Receipt = false,
            So_Phieu = $"{fixture.Tag}-issue",
            Kho_ID = fixture.WarehouseId,
            Ngay_Chung_Tu = new DateTime(2099, 7, 2),
            Ghi_Chu = "phase 6 actor test"
        };

        await controller.Save_Document_Async(issue, createdBy, functionCode, fixture.LoginA);
        var createdIssue = await ReadHeaderAsync(issue.Auto_ID, false);
        Assert.Equal(createdBy, createdIssue.CreatedBy);
        Assert.Equal(createdBy, createdIssue.LastUpdatedBy);

        issue.So_Phieu += "-updated";
        await controller.Save_Document_Async(issue, updatedBy, functionCode, fixture.LoginB);
        var updatedIssue = await ReadHeaderAsync(issue.Auto_ID, false);
        Assert.Equal(createdBy, updatedIssue.CreatedBy);
        Assert.Equal(updatedBy, updatedIssue.LastUpdatedBy);

        var issueDetail = new CWarehouseDocumentDetail
        {
            Document_ID = issue.Auto_ID,
            San_Pham_ID = fixture.ProductId,
            So_Luong = 2m,
            Don_Gia = 1m
        };
        await controller.Save_Document_Detail_Async(false, issueDetail, createdBy, functionCode, fixture.LoginA);
        var createdIssueDetail = await ReadDetailAsync(issueDetail.Auto_ID, false);
        Assert.Equal(createdBy, createdIssueDetail.CreatedBy);
        Assert.Equal(createdBy, createdIssueDetail.LastUpdatedBy);

        issueDetail.So_Luong = 3m;
        await controller.Save_Document_Detail_Async(false, issueDetail, updatedBy, functionCode, fixture.LoginB);
        var updatedIssueDetail = await ReadDetailAsync(issueDetail.Auto_ID, false);
        Assert.Equal(createdBy, updatedIssueDetail.CreatedBy);
        Assert.Equal(updatedBy, updatedIssueDetail.LastUpdatedBy);
    }

    [Fact]
    public void Action_history_failure_is_a_secondary_outcome_and_does_not_throw_to_business_caller()
    {
        Exception? captured = null;
        var historySucceeded = CWarehouseActionHistory_Recorder.TryRecord(
            () => throw new InvalidOperationException("deterministic phase 6 history failure"),
            exception => captured = exception);

        Assert.False(historySucceeded);
        Assert.IsType<InvalidOperationException>(captured);
    }

    [Fact]
    public void Action_history_success_is_reported_separately_from_business_success()
    {
        var historyCalls = 0;
        var historySucceeded = CWarehouseActionHistory_Recorder.TryRecord(
            () => historyCalls++,
            _ => throw new InvalidOperationException("history failure callback must not run on success"));

        Assert.True(historySucceeded);
        Assert.Equal(1, historyCalls);
    }

    [Fact]
    public void Warehouse_ui_uses_secondary_action_history_outcome_contract()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor"));

        Assert.Contains("CWarehouseActionHistory_Recorder.TryRecord", source, StringComparison.Ordinal);
        Assert.Contains("m_strAuditWarning", source, StringComparison.Ordinal);
        Assert.Contains("Nghiệp vụ đã thành công nhưng chưa ghi được lịch sử thao tác.", source, StringComparison.Ordinal);
    }

    private static async Task<HeaderAudit> ReadHeaderAsync(long documentId, bool isReceipt)
    {
        await using var connection = new SqlConnection(BaseConnectionString);
        await connection.OpenAsync();
        var table = isReceipt ? "tbl_XNK_Nhap_Kho" : "tbl_XNK_Xuat_Kho";
        var sql = $"SELECT Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function, Is_Posted, Posted_At FROM dbo.{table} WHERE Auto_ID = @Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(BigInt("@Id", documentId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new HeaderAudit(
            ReadDateTime(reader, 0), ReadString(reader, 1), ReadString(reader, 2),
            ReadDateTime(reader, 3), ReadString(reader, 4), ReadString(reader, 5),
            reader.GetBoolean(6), ReadDateTime(reader, 7));
    }

    private static async Task<DetailAudit> ReadDetailAsync(long detailId, bool isReceipt)
    {
        await using var connection = new SqlConnection(BaseConnectionString);
        await connection.OpenAsync();
        var table = isReceipt ? "tbl_XNK_Nhap_Kho_Raw_Data" : "tbl_XNK_Xuat_Kho_Raw_Data";
        var sql = $"SELECT Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function FROM dbo.{table} WHERE Auto_ID = @Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(BigInt("@Id", detailId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DetailAudit(
            ReadDateTime(reader, 0), ReadString(reader, 1), ReadString(reader, 2),
            ReadDateTime(reader, 3), ReadString(reader, 4), ReadString(reader, 5));
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var tag = $"P6-{Guid.NewGuid():N}"[..18];
        var loginA = $"{tag}-a";
        var loginB = $"{tag}-b";

        await using var connection = new SqlConnection(BaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var productId = await LongScalarAsync(connection, transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await LongScalarAsync(connection, transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 6 actor test');",
                Text("@Name", $"{tag}-warehouse", 255));
            var memberId = await LongScalarAsync(connection, transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, Trang_Thai_ID, deleted) VALUES (@MemberA, @LoginA, @NameA, 1, 0), (@MemberB, @LoginB, @NameB, 1, 0);",
                BigInt("@MemberA", memberId), BigInt("@MemberB", memberId + 1),
                Text("@LoginA", loginA, 100), Text("@LoginB", loginB, 100),
                Text("@NameA", $"{tag}-member-a", 200), Text("@NameB", $"{tag}-member-b", 200));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@LoginA, @WarehouseId), (@LoginB, @WarehouseId);",
                Text("@LoginA", loginA, 100), Text("@LoginB", loginB, 100), BigInt("@WarehouseId", warehouseId));
            await transaction.CommitAsync();
            return new Fixture(tag, loginA, loginB, productId, supplierId, warehouseId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CleanupFixtureAsync(Fixture fixture)
    {
        await using var connection = new SqlConnection(BaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await ExecuteAsync(connection, transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(connection, transaction,
                "DELETE r FROM dbo.InventoryReservation_Current r JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.Kho_ID = @WarehouseId; DELETE d FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE h.Kho_ID = @WarehouseId; DELETE d FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Kho_ID = @WarehouseId; DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId; DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID = @WarehouseId AND q.San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB); DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB); DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId),
                Text("@LoginA", fixture.LoginA, 100), Text("@LoginB", fixture.LoginB, 100));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        var fallback = Path.Combine("P:", "Giao đoạn 2 TK", "Giao đoạn 2 TK", "TKS_Thuc_Tap_11", relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fallback))
            return fallback;
        throw new FileNotFoundException(relativePath);
    }

    private static async Task<long> InsertIdAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> LongScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) =>
        Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static string? ReadString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTime? ReadDateTime(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };

    private sealed record Fixture(string Tag, string LoginA, string LoginB, long ProductId, long SupplierId, long WarehouseId);
    private sealed record HeaderAudit(DateTime? CreatedAt, string? CreatedBy, string? CreatedByFunction, DateTime? LastUpdatedAt, string? LastUpdatedBy, string? LastUpdatedByFunction, bool IsPosted, DateTime? PostedAt);
    private sealed record DetailAudit(DateTime? CreatedAt, string? CreatedBy, string? CreatedByFunction, DateTime? LastUpdatedAt, string? LastUpdatedBy, string? LastUpdatedByFunction);
}
