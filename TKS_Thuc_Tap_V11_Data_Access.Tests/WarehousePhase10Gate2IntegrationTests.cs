using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10Gate2IntegrationTests
{
    private static string ConnectionString => WarehouseTestDatabase.ConnectionString;

    [Fact]
    public async Task H05_finalize_rejects_when_post_owns_scope_fence()
    {
        var fixture = await CreateFixtureAsync("TDD-H05-POST-WINS");
        await using var postConnection = new SqlConnection(ConnectionString);
        await postConnection.OpenAsync();
        await using var postTransaction = postConnection.BeginTransaction();

        try
        {
            await AcquireScopeLockAsync(postConnection, postTransaction, fixture);

            await using var finalizeConnection = new SqlConnection(ConnectionString);
            await finalizeConnection.OpenAsync();
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(
                finalizeConnection,
                null,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", fixture.SnapshotDate),
                BigInt("@Kho_ID", fixture.WarehouseId),
                BigInt("@San_Pham_ID", fixture.ProductId),
                Text("@Worker_Name", fixture.Tag, 128)));

            Assert.Equal(51407, error.Number);
            Assert.Contains("Inventory fence Group acquisition failed", error.Message, StringComparison.Ordinal);

            await ExecuteStoredAsync(
                postConnection,
                postTransaction,
                "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true),
                BigInt("@Document_ID", fixture.ReceiptId),
                Text("@Ma_Dang_Nhap", fixture.Login, 100),
                Text("@Last_Updated_By", fixture.Login, 100),
                Text("@Last_Updated_By_Function", "TDD-H05", 100));
            await postTransaction.CommitAsync();

            var snapshot = await ReadSnapshotAsync(fixture);
            Assert.False(snapshot.IsValid);
            Assert.Equal(120m, await DecimalScalarAsync(
                "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId)));
        }
        finally
        {
            try { await postTransaction.RollbackAsync(); } catch { }
            await CleanupFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task H05_finalize_wins_then_post_invalidates_the_published_snapshot()
    {
        var fixture = await CreateFixtureAsync("TDD-H05-FINALIZE-WINS");
        try
        {
            await using (var finalizeConnection = new SqlConnection(ConnectionString))
            {
                await finalizeConnection.OpenAsync();
                await ExecuteStoredAsync(
                    finalizeConnection,
                    null,
                    "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                    Date("@Snapshot_Date", fixture.SnapshotDate),
                    BigInt("@Kho_ID", fixture.WarehouseId),
                    BigInt("@San_Pham_ID", fixture.ProductId),
                    Text("@Worker_Name", fixture.Tag, 128));
            }

            Assert.True((await ReadSnapshotAsync(fixture)).IsValid);

            await using (var postConnection = new SqlConnection(ConnectionString))
            {
                await postConnection.OpenAsync();
                await ExecuteStoredAsync(
                    postConnection,
                    null,
                    "dbo.sp_XNK_Document_Post",
                    Bit("@Is_Receipt", true),
                    BigInt("@Document_ID", fixture.ReceiptId),
                    Text("@Ma_Dang_Nhap", fixture.Login, 100),
                    Text("@Last_Updated_By", fixture.Login, 100),
                    Text("@Last_Updated_By_Function", "TDD-H05", 100));
            }

            Assert.False((await ReadSnapshotAsync(fixture)).IsValid);
        }
        finally
        {
            await CleanupFixtureAsync(fixture);
        }
    }

    private static async Task<Fixture> CreateFixtureAsync(string prefix)
    {
        var tag = $"{prefix}-{Guid.NewGuid():N}"[..40];
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var productId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseId = await LongScalarAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10 Gate 2');",
                Text("@Name", tag, 255));
            var login = $"{tag}-login";
            var memberId = await LongScalarAsync(connection, transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10 Gate 2', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId); INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 100, 0); INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1); INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 1, 1);",
                BigInt("@MemberId", memberId), Text("@Login", login, 100), BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Date("@Date", new DateTime(2099, 6, 1)));

            var receiptId = await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Nhap_Kho_Save_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{tag}-receipt", 100), BigInt("@Kho_ID", warehouseId),
                BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", new DateTime(2099, 6, 1)), Text("@Ghi_Chu", "", 1000),
                Text("@Ma_Dang_Nhap", login, 100), Text("@Created_By", login, 100), Text("@Created_By_Function", "TDD-H05", 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "TDD-H05", 100));
            await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Nhap_Kho_Save_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", productId), Decimal("@SL_Nhap", 20), Decimal("@Don_Gia_Nhap", 1),
                Text("@Ma_Dang_Nhap", login, 100), Text("@Created_By", login, 100), Text("@Created_By_Function", "TDD-H05", 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "TDD-H05", 100));
            await transaction.CommitAsync();
            return new Fixture(tag, login, warehouseId, productId, receiptId, new DateTime(2099, 6, 1));
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task AcquireScopeLockAsync(SqlConnection connection, SqlTransaction transaction, Fixture fixture)
    {
        await ExecuteAsync(connection, transaction,
            """
            DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
            DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
            INSERT @GroupSet(Kho_ID) VALUES (@WarehouseId);
            INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@WarehouseId, @ProductId);
            EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';
            EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Movement_Bootstrap @Mode = N'Shared';
            EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set @GroupSet = @GroupSet, @Mode = N'Exclusive';
            EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope_Set @ScopeSet = @ScopeSet, @Mode = N'Exclusive';
            """,
            BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
    }

    private static async Task<SnapshotRow> ReadSnapshotAsync(Fixture fixture)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT IsValid, ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            connection);
        command.Parameters.Add(Date("@Date", fixture.SnapshotDate));
        command.Parameters.Add(BigInt("@WarehouseId", fixture.WarehouseId));
        command.Parameters.Add(BigInt("@ProductId", fixture.ProductId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new SnapshotRow(reader.GetBoolean(0), reader.GetDecimal(1));
    }

    private static async Task CleanupFixtureAsync(Fixture fixture)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId), BigInt("@ReceiptId", fixture.ReceiptId), Text("@Login", fixture.Login, 100));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task ExecuteStoredAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteStoredIdAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(command.Parameters["@Auto_ID"].Value);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<decimal> DecimalScalarAsync(string sql, params SqlParameter[] parameters) => Convert.ToDecimal(await ScalarAsync(sql, parameters));
    private static async Task<long> LongScalarAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 10 };
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter BigIntOutput(string name, long value) => new(name, SqlDbType.BigInt) { Direction = ParameterDirection.InputOutput, Value = value };
    private static SqlParameter Bit(string name, bool value) => new(name, SqlDbType.Bit) { Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };

    private sealed record Fixture(string Tag, string Login, long WarehouseId, long ProductId, long ReceiptId, DateTime SnapshotDate);
    private sealed record SnapshotRow(bool IsValid, decimal ClosingQuantity);
}
