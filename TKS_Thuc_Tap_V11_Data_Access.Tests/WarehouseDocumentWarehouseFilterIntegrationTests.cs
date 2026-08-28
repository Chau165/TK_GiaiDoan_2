using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseDocumentWarehouseFilterIntegrationTests
{
    private const string ConnectionString = "Server=localhost;Database=TKS_Thuc_Tap_V11_GiaiDoan2;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public async Task Document_pages_filter_receipts_and_issues_by_selected_authorized_warehouse()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var tag = $"TDD-DF-{Guid.NewGuid():N}";
            var login = $"{tag}-login";
            await InsertIdAsync(connection, transaction,
                "DECLARE @UserId BIGINT; SELECT @UserId = ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) OUTPUT INSERTED.Auto_ID VALUES (@UserId, @Login, @Name, 0);",
                Text("@Login", login, 100), Text("@Name", $"{tag}-user", 200));
            var supplierId = await InsertIdAsync(connection, transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-a", 255));
            var warehouseB = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-b", 255));
            var warehouseC = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-c", 255));

            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", login, 100), BigInt("@WarehouseId", warehouseA));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", login, 100), BigInt("@WarehouseId", warehouseB));

            await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 0, N'');",
                Text("@Number", $"{tag}-receipt-a", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId));
            await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 0, N'');",
                Text("@Number", $"{tag}-receipt-b", 100), BigInt("@WarehouseId", warehouseB), BigInt("@SupplierId", supplierId));
            await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Number, @WarehouseId, '2026-09-10', 0, N'');",
                Text("@Number", $"{tag}-issue-b", 100), BigInt("@WarehouseId", warehouseB));

            var allReceipts = await ReadPagedDocumentsAsync(connection, transaction, true, login);
            Assert.Equal(2, allReceipts.TotalCount);
            Assert.Equal(2, allReceipts.Rows.Count);

            var selectedReceipt = await ReadPagedDocumentsAsync(connection, transaction, true, login, warehouseA);
            Assert.Equal(1, selectedReceipt.TotalCount);
            Assert.Equal($"{tag}-receipt-a", Assert.Single(selectedReceipt.Rows).DocumentNumber);

            var selectedIssue = await ReadPagedDocumentsAsync(connection, transaction, false, login, warehouseB);
            Assert.Equal(1, selectedIssue.TotalCount);
            Assert.Equal($"{tag}-issue-b", Assert.Single(selectedIssue.Rows).DocumentNumber);

            var error = await Assert.ThrowsAsync<SqlException>(() =>
                ReadPagedDocumentsAsync(connection, transaction, true, login, warehouseC));
            Assert.Equal(51054, error.Number);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<(int TotalCount, List<DocumentRow> Rows)> ReadPagedDocumentsAsync(
        SqlConnection connection, SqlTransaction transaction, bool isReceipt, string login, long? warehouseId = null)
    {
        await using var command = new SqlCommand("sp_XNK_Document_Page", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = isReceipt });
        command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        command.Parameters.Add(Text("@Search_Text", "", 100));
        command.Parameters.Add(Text("@Ma_Dang_Nhap", login, 100));
        command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = warehouseId ?? (object)DBNull.Value });

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        var rows = new List<DocumentRow>();
        while (await reader.ReadAsync())
            rows.Add(new DocumentRow(reader.GetString(2), reader.GetInt64(3)));
        return (totalCount, rows);
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

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };

    private sealed record DocumentRow(string DocumentNumber, long WarehouseId);
}
