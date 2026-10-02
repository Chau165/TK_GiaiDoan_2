using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10Gate2IntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Fact]
    public async Task H05_finalize_rejects_when_post_owns_scope_fence()
    {
        var v_Fixture = await CreateFixtureAsync("TDD-H05-POST-WINS");
        await using var v_PostConnection = new SqlConnection(ConnectionString);
        await v_PostConnection.OpenAsync();
        await using var v_PostTransaction = v_PostConnection.BeginTransaction();

        try
        {
            await AcquireScopeLockAsync(v_PostConnection, v_PostTransaction, v_Fixture);

            await using var v_FinalizeConnection = new SqlConnection(ConnectionString);
            await v_FinalizeConnection.OpenAsync();
            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(
                v_FinalizeConnection,
                null,
                "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                Date("@Snapshot_Date", v_Fixture.SnapshotDate),
                BigInt("@Kho_ID", v_Fixture.WarehouseId),
                BigInt("@San_Pham_ID", v_Fixture.ProductId),
                Text("@Worker_Name", v_Fixture.Tag, 128)));

            Assert.Equal(51407, v_Error.Number);
            Assert.Contains("Inventory fence Group acquisition failed", v_Error.Message, StringComparison.Ordinal);

            await ExecuteStoredAsync(
                v_PostConnection,
                v_PostTransaction,
                "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true),
                BigInt("@Document_ID", v_Fixture.ReceiptId),
                Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
                Text("@Last_Updated_By", v_Fixture.Login, 100),
                Text("@Last_Updated_By_Function", "TDD-H05", 100));
            await v_PostTransaction.CommitAsync();

            var v_Snapshot = await ReadSnapshotAsync(v_Fixture);
            Assert.False(v_Snapshot.IsValid);
            Assert.Equal(120m, await DecimalScalarAsync(
                "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", v_Fixture.WarehouseId), BigInt("@ProductId", v_Fixture.ProductId)));
        }
        finally
        {
            try { await v_PostTransaction.RollbackAsync(); } catch { }
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task H05_finalize_wins_then_post_invalidates_the_published_snapshot()
    {
        var v_Fixture = await CreateFixtureAsync("TDD-H05-FINALIZE-WINS");
        try
        {
            await using (var v_FinalizeConnection = new SqlConnection(ConnectionString))
            {
                await v_FinalizeConnection.OpenAsync();
                await ExecuteStoredAsync(
                    v_FinalizeConnection,
                    null,
                    "dbo.sp_Inventory_Snapshot_Finalize_Daily",
                    Date("@Snapshot_Date", v_Fixture.SnapshotDate),
                    BigInt("@Kho_ID", v_Fixture.WarehouseId),
                    BigInt("@San_Pham_ID", v_Fixture.ProductId),
                    Text("@Worker_Name", v_Fixture.Tag, 128));
            }

            Assert.True((await ReadSnapshotAsync(v_Fixture)).IsValid);

            await using (var v_PostConnection = new SqlConnection(ConnectionString))
            {
                await v_PostConnection.OpenAsync();
                await ExecuteStoredAsync(
                    v_PostConnection,
                    null,
                    "dbo.sp_XNK_Document_Post",
                    Bit("@Is_Receipt", true),
                    BigInt("@Document_ID", v_Fixture.ReceiptId),
                    Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
                    Text("@Last_Updated_By", v_Fixture.Login, 100),
                    Text("@Last_Updated_By_Function", "TDD-H05", 100));
            }

            Assert.False((await ReadSnapshotAsync(v_Fixture)).IsValid);
        }
        finally
        {
            await CleanupFixtureAsync(v_Fixture);
        }
    }

    private static async Task<Fixture> CreateFixtureAsync(string p_Prefix)
    {
        var v_Tag = $"{p_Prefix}-{Guid.NewGuid():N}"[..40];
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            var productId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseId = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10 Gate 2');",
                Text("@Name", v_Tag, 255));
            var v_Login = $"{v_Tag}-login";
            var memberId = await LongScalarAsync(v_Connection, v_Transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10 Gate 2', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId); INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 100, 0); INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 100, 0, 100, 100, 0, 1); INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 1, 1);",
                BigInt("@MemberId", memberId), Text("@Login", v_Login, 100), BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Date("@Date", new DateTime(2099, 6, 1)));

            var receiptId = await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2011_sp_ins_Nhap_Kho_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{v_Tag}-receipt", 100), BigInt("@Kho_ID", warehouseId),
                BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", new DateTime(2099, 6, 1)), Text("@Ghi_Chu", "", 1000),
                Text("@Ma_Dang_Nhap", v_Login, 100), Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "TDD-H05", 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "TDD-H05", 100));
            await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2011_sp_ins_Nhap_Kho_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", productId), Decimal("@SL_Nhap", 20), Decimal("@Don_Gia_Nhap", 1),
                Text("@Ma_Dang_Nhap", v_Login, 100), Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "TDD-H05", 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "TDD-H05", 100));
            await v_Transaction.CommitAsync();
            return new Fixture(v_Tag, v_Login, warehouseId, productId, receiptId, new DateTime(2099, 6, 1));
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task AcquireScopeLockAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Fixture p_Fixture)
    {
        await ExecuteAsync(p_Connection, p_Transaction,
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
            BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId));
    }

    private static async Task<SnapshotRow> ReadSnapshotAsync(Fixture p_Fixture)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand(
            "SELECT IsValid, ClosingQuantity FROM dbo.InventoryBalance_Snapshot_Daily WHERE Snapshot_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            v_Connection);
        v_Command.Parameters.Add(Date("@Date", p_Fixture.SnapshotDate));
        v_Command.Parameters.Add(BigInt("@WarehouseId", p_Fixture.WarehouseId));
        v_Command.Parameters.Add(BigInt("@ProductId", p_Fixture.ProductId));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        return new SnapshotRow(v_Reader.GetBoolean(0), v_Reader.GetDecimal(1));
    }

    private static async Task CleanupFixtureAsync(Fixture p_Fixture)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@ReceiptId", p_Fixture.ReceiptId), Text("@Login", p_Fixture.Login, 100));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task ExecuteStoredAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteStoredIdAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_Command.Parameters["@Auto_ID"].Value);
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction) { CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand(p_Sql, v_Connection);
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
    }

    private static async Task<decimal> DecimalScalarAsync(string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToDecimal(await ScalarAsync(p_Sql, p_arrParameters));
    }
    private static async Task<long> LongScalarAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction) { CommandTimeout = 10 };
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
    }

    private static SqlParameter Text(string p_Name, string p_Value, int p_iSize)
    {
        return new(p_Name, SqlDbType.NVarChar, p_iSize)
        {
            Value = p_Value
        };
    }
    private static SqlParameter BigInt(string p_Name, long value)
    {
        return new(p_Name, SqlDbType.BigInt)
        {
            Value = value
        };
    }
    private static SqlParameter BigIntOutput(string p_Name, long value)
    {
        return new(p_Name, SqlDbType.BigInt)
        {
            Direction = ParameterDirection.InputOutput,
            Value = value
        };
    }
    private static SqlParameter Bit(string p_Name, bool p_bValue)
    {
        return new(p_Name, SqlDbType.Bit)
        {
            Value = p_bValue
        };
    }
    private static SqlParameter Date(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.Date)
        {
            Value = p_dtmValue.Date
        };
    }
    private static SqlParameter Decimal(string p_Name, decimal p_Value)
    {
        return new(p_Name, SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 3,
            Value = p_Value
        };
    }

    private sealed record Fixture(string Tag, string Login, long WarehouseId, long ProductId, long ReceiptId, DateTime SnapshotDate);
    private sealed record SnapshotRow(bool IsValid, decimal ClosingQuantity);
}
