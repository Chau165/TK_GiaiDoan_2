using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehouseOpeningBalanceIntegrationTests
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public async Task Period_reports_use_the_last_balance_before_start_and_match_each_other()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var tag = $"TDD-OPEN-{Guid.NewGuid():N}";
            var login = $"{tag}-login";
            var productIds = await ReadProductIdsAsync(connection, transaction, 3);
            var supplierId = await ReadIdAsync(connection, transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Opening balance integration test');",
                Text("@Name", $"{tag}-warehouse", 255));
            await InsertIdAsync(connection, transaction,
                "DECLARE @MemberId BIGINT; SELECT @MemberId = ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) OUTPUT INSERTED.Auto_ID VALUES (@MemberId, @Login, @Name, 0);",
                Text("@Login", login, 100), Text("@Name", $"{tag}-member", 200));

            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", login, 100), BigInt("@WarehouseId", warehouseId));

            /* Case 1: no balance before 2026-01-01.  The latest row has
               OpeningQuantity=5, which must not become the report opening. */
            var noHistoryProduct = productIds[0];
            await InsertReceiptAsync(connection, transaction, warehouseId, supplierId, noHistoryProduct, new DateTime(2026, 1, 3), 15, tag);
            await InsertIssueAsync(connection, transaction, warehouseId, noHistoryProduct, new DateTime(2026, 2, 10), 3, tag);
            await InsertIssueAsync(connection, transaction, warehouseId, noHistoryProduct, new DateTime(2026, 2, 24), 2, tag);
            await InsertIssueAsync(connection, transaction, warehouseId, noHistoryProduct, new DateTime(2026, 3, 24), 3, tag);
            await InsertIssueAsync(connection, transaction, warehouseId, noHistoryProduct, new DateTime(2026, 5, 24), 2, tag);
            await InsertReceiptAsync(connection, transaction, warehouseId, supplierId, noHistoryProduct, new DateTime(2026, 6, 3), 17, tag);
            await InsertDailyAsync(connection, transaction, warehouseId, noHistoryProduct,
                new DateTime(2026, 6, 3), 5, 17, 0, 22, 32, 10);
            await InsertScopeAsync(connection, transaction, warehouseId, noHistoryProduct,
                new DateTime(2026, 6, 3), new DateTime(2026, 6, 3));

            var noHistoryPaged = await ReadPagedReportAsync(connection, transaction, login, warehouseId, noHistoryProduct,
                new DateTime(2026, 1, 1), new DateTime(2026, 9, 30));
            var noHistoryFull = await ReadFullReportAsync(connection, transaction, login, warehouseId, noHistoryProduct,
                new DateTime(2026, 1, 1), new DateTime(2026, 9, 30));
            AssertReport(noHistoryPaged, 0, 32, 10, 22);
            AssertReport(noHistoryFull, 0, 32, 10, 22);

            /* Case 2: a balance exists on 2025-12-31 and must seed 2026-01-01. */
            var priorPeriodProduct = productIds[1];
            await InsertSnapshotAsync(connection, transaction, warehouseId, priorPeriodProduct,
                new DateTime(2025, 12, 31), 100);
            await InsertDailyAsync(connection, transaction, warehouseId, priorPeriodProduct,
                new DateTime(2025, 12, 31), 100, 0, 0, 100, 0, 0);
            await InsertReceiptAsync(connection, transaction, warehouseId, supplierId, priorPeriodProduct,
                new DateTime(2026, 1, 10), 20, tag);
            await InsertDailyAsync(connection, transaction, warehouseId, priorPeriodProduct,
                new DateTime(2026, 1, 10), 100, 20, 0, 120, 20, 0);
            await InsertScopeAsync(connection, transaction, warehouseId, priorPeriodProduct,
                new DateTime(2025, 12, 31), new DateTime(2026, 1, 10));

            var priorPeriodPaged = await ReadPagedReportAsync(connection, transaction, login, warehouseId, priorPeriodProduct,
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
            var priorPeriodFull = await ReadFullReportAsync(connection, transaction, login, warehouseId, priorPeriodProduct,
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
            AssertReport(priorPeriodPaged, 100, 20, 0, 120);
            AssertReport(priorPeriodFull, 100, 20, 0, 120);

            /* Case 3: a mid-period snapshot on 2026-03-10 seeds a report
               beginning on 2026-03-15. */
            var midPeriodProduct = productIds[2];
            await InsertSnapshotAsync(connection, transaction, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 10), 100);
            await InsertDailyAsync(connection, transaction, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 10), 100, 0, 0, 100, 0, 0);
            await InsertReceiptAsync(connection, transaction, warehouseId, supplierId, midPeriodProduct,
                new DateTime(2026, 3, 20), 20, tag);
            await InsertDailyAsync(connection, transaction, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 20), 100, 20, 0, 120, 20, 0);
            await InsertScopeAsync(connection, transaction, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 10), new DateTime(2026, 3, 20));

            var midPeriodPaged = await ReadPagedReportAsync(connection, transaction, login, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 15), new DateTime(2026, 3, 31));
            var midPeriodFull = await ReadFullReportAsync(connection, transaction, login, warehouseId, midPeriodProduct,
                new DateTime(2026, 3, 15), new DateTime(2026, 3, 31));
            AssertReport(midPeriodPaged, 100, 20, 0, 120);
            AssertReport(midPeriodFull, 100, 20, 0, 120);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static void AssertReport(ReportRow row, decimal opening, decimal received, decimal issued, decimal closing)
    {
        Assert.Equal(opening, row.Opening);
        Assert.Equal(received, row.Received);
        Assert.Equal(issued, row.Issued);
        Assert.Equal(closing, row.Closing);
    }

    private static async Task<ReportRow> ReadPagedReportAsync(
        SqlConnection connection, SqlTransaction transaction, string login, long warehouseId, long productId,
        DateTime from, DateTime to)
    {
        await using var command = ReportCommand(connection, transaction, "sp_BC_Xuat_Nhap_Ton_Page", login, warehouseId, from, to);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(await reader.NextResultAsync());
        while (await reader.ReadAsync())
        {
            if (reader.GetInt64(2) == productId)
                return ReadReportRow(reader);
        }

        throw new Xunit.Sdk.XunitException($"Product {productId} was not returned by the paged report.");
    }

    private static async Task<ReportRow> ReadFullReportAsync(
        SqlConnection connection, SqlTransaction transaction, string login, long warehouseId, long productId,
        DateTime from, DateTime to)
    {
        await using var command = ReportCommand(connection, transaction, "sp_BC_Xuat_Nhap_Ton", login, warehouseId, from, to);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetInt64(2) == productId)
                return ReadReportRow(reader);
        }

        throw new Xunit.Sdk.XunitException($"Product {productId} was not returned by the full report.");
    }

    private static ReportRow ReadReportRow(SqlDataReader reader) => new(
        reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8));

    private static SqlCommand ReportCommand(
        SqlConnection connection, SqlTransaction transaction, string procedure, string login, long warehouseId,
        DateTime from, DateTime to)
    {
        var command = new SqlCommand(procedure, connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(Date("@Tu_Ngay", from));
        command.Parameters.Add(Date("@Den_Ngay", to));
        if (procedure.EndsWith("_Page", StringComparison.Ordinal))
        {
            command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
            command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 20 });
        }
        command.Parameters.Add(Text("@Ma_Dang_Nhap", login, 100));
        command.Parameters.Add(BigInt("@Kho_ID", warehouseId));
        return command;
    }

    private static async Task<List<long>> ReadProductIdsAsync(SqlConnection connection, SqlTransaction transaction, int count)
    {
        await using var command = new SqlCommand(
            "SELECT TOP (@Count) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;", connection, transaction);
        command.Parameters.Add(new SqlParameter("@Count", SqlDbType.Int) { Value = count });
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<long>();
        while (await reader.ReadAsync())
            result.Add(reader.GetInt64(0));
        Assert.Equal(count, result.Count);
        return result;
    }

    private static async Task<long> ReadIdAsync(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        var value = await command.ExecuteScalarAsync();
        Assert.NotNull(value);
        return Convert.ToInt64(value);
    }

    private static async Task<long> InsertIdAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertReceiptAsync(SqlConnection connection, SqlTransaction transaction, long warehouseId, long supplierId, long productId, DateTime date, decimal quantity, string tag)
    {
        var documentId = await InsertIdAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N'');",
            Text("@Number", $"{tag}-receipt-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", warehouseId), BigInt("@SupplierId", supplierId), Date("@Date", date));
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", productId), Decimal("@Quantity", quantity));
    }

    private static async Task InsertIssueAsync(SqlConnection connection, SqlTransaction transaction, long warehouseId, long productId, DateTime date, decimal quantity, string tag)
    {
        var documentId = await InsertIdAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @Date, 1, N'');",
            Text("@Number", $"{tag}-issue-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", warehouseId), Date("@Date", date));
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", productId), Decimal("@Quantity", quantity));
    }

    private static Task InsertSnapshotAsync(SqlConnection connection, SqlTransaction transaction, long warehouseId, long productId, DateTime date, decimal closing) =>
        ExecuteAsync(connection, transaction,
            "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, @Closing, 1, 1);",
            Date("@Date", date), BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Decimal("@Closing", closing));

    private static Task InsertDailyAsync(SqlConnection connection, SqlTransaction transaction, long warehouseId, long productId, DateTime date, decimal opening, decimal received, decimal issued, decimal closing, decimal cumulativeReceived, decimal cumulativeIssued) =>
        ExecuteAsync(connection, transaction,
            "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, @Opening, @Received, @Issued, @Closing, @CumulativeReceived, @CumulativeIssued, 1);",
            Date("@Date", date), BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Decimal("@Opening", opening), Decimal("@Received", received), Decimal("@Issued", issued), Decimal("@Closing", closing), Decimal("@CumulativeReceived", cumulativeReceived), Decimal("@CumulativeIssued", cumulativeIssued));

    private static Task InsertScopeAsync(SqlConnection connection, SqlTransaction transaction, long warehouseId, long productId, DateTime firstDate, DateTime lastDate) =>
        ExecuteAsync(connection, transaction,
            "INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseId, @ProductId, @FirstDate, @LastDate);",
            BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Date("@FirstDate", firstDate), Date("@LastDate", lastDate));

    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };

    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal)
    {
        Precision = 18,
        Scale = 3,
        Value = value
    };

    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };

    private sealed record ReportRow(decimal Opening, decimal Received, decimal Issued, decimal Closing);
}
