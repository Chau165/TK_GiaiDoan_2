using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10Gate1IntegrationTests
{
    private const string DirectDmlProbeUser = "Phase10Gate1DmlProbe";
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public async Task N02_posted_issue_detail_is_immutable_at_database_boundary(string p_Mutation)
    {
        await EnsureDirectDmlProbeUserAsync();
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        var v_Scope = await CreatePostedIssueScopeAsync(v_Connection, v_Transaction);
        var v_bImpersonated = false;

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                $"EXECUTE AS USER = N'{DirectDmlProbeUser}'; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            v_bImpersonated = true;

            string v_Sql;
            switch (p_Mutation)
            {
                case "INSERT":
                    v_Sql = "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, 1, 1);";
                    break;
                case "UPDATE":
                    v_Sql = "UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat = SL_Xuat + 1 WHERE Auto_ID = @DetailId;";
                    break;
                case "DELETE":
                    v_Sql = "DELETE dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @DetailId;";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(p_Mutation));
            }

            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(v_Connection, v_Transaction, v_Sql,
                BigInt("@IssueId", v_Scope.IssueId), BigInt("@ProductId", v_Scope.ProductId), BigInt("@DetailId", v_Scope.DetailId)));

            Assert.Equal(51228, v_Error.Number);
        }
        finally
        {
            if (v_bImpersonated)
                await ExecuteAsync(v_Connection, v_Transaction, "REVERT;");

            await v_Transaction.RollbackAsync();
            await DropDirectDmlProbeUserAsync();
        }
    }

    [Fact]
    public async Task N03_daily_rebuild_includes_snapshot_to_from_date_bridge()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        var v_Scope = await CreateBasicScopeAsync(v_Connection, v_Transaction, "TDD-N03-RECEIPT");
        var v_dtmAnchorDate = new DateTime(2099, 1, 1);
        var v_dtmBridgeDate = new DateTime(2099, 1, 3);
        var v_dtmFromDate = new DateTime(2099, 1, 5);

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 1, 1);",
                Date("@Date", v_dtmAnchorDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 20, 0, 1), (@FromDate, @WarehouseId, @ProductId, 30, 0, 1);",
                Date("@Date", v_dtmBridgeDate), Date("@FromDate", v_dtmFromDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Balance_Daily_Rebuild",
                BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId), Date("@From_Date", v_dtmFromDate));

            var v_Row = await ReadDailyAsync(v_Connection, v_Transaction, v_Scope, v_dtmFromDate);
            Assert.Equal(120m, v_Row.OpeningQuantity);
            Assert.Equal(30m, v_Row.TotalReceived);
            Assert.Equal(0m, v_Row.TotalIssued);
            Assert.Equal(150m, v_Row.ClosingQuantity);
            Assert.Equal(50m, v_Row.CumulativeReceived);
            Assert.Equal(0m, v_Row.CumulativeIssued);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task N03_daily_rebuild_includes_issue_bridge_without_double_counting_from_date()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        var v_Scope = await CreateBasicScopeAsync(v_Connection, v_Transaction, "TDD-N03-ISSUE");
        var v_dtmAnchorDate = new DateTime(2099, 2, 1);
        var v_dtmBridgeDate = new DateTime(2099, 2, 3);
        var v_dtmFromDate = new DateTime(2099, 2, 5);

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, 100, 1, 1);",
                Date("@Date", v_dtmAnchorDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseId, @ProductId, 0, 20, 1), (@FromDate, @WarehouseId, @ProductId, 30, 0, 1);",
                Date("@Date", v_dtmBridgeDate), Date("@FromDate", v_dtmFromDate), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_Inventory_Balance_Daily_Rebuild",
                BigInt("@Kho_ID", v_Scope.WarehouseId), BigInt("@San_Pham_ID", v_Scope.ProductId), Date("@From_Date", v_dtmFromDate));

            var v_Row = await ReadDailyAsync(v_Connection, v_Transaction, v_Scope, v_dtmFromDate);
            Assert.Equal(80m, v_Row.OpeningQuantity);
            Assert.Equal(30m, v_Row.TotalReceived);
            Assert.Equal(0m, v_Row.TotalIssued);
            Assert.Equal(110m, v_Row.ClosingQuantity);
            Assert.Equal(30m, v_Row.CumulativeReceived);
            Assert.Equal(20m, v_Row.CumulativeIssued);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task N05_delete_fence_wins_first_and_save_detail_fails_closed_at_group_fence()
    {
        var v_Scope = await CreatePersistentIssueScopeAsync("TDD-N05-DELETE-WINS");
        await using (var v_SetupConnection = new SqlConnection(ConnectionString))
        {
            await v_SetupConnection.OpenAsync();
            await using var v_SetupTransaction = v_SetupConnection.BeginTransaction();
            v_Scope = v_Scope with
            {
                DetailId = await SaveIssueDetailAsync(v_SetupConnection, v_SetupTransaction, v_Scope, p_Quantity: 1m)
            };
            await v_SetupTransaction.CommitAsync();
        }

        await using var v_SaveConnection = new SqlConnection(ConnectionString);
        await using var v_DeleteConnection = new SqlConnection(ConnectionString);
        await v_SaveConnection.OpenAsync();
        await v_DeleteConnection.OpenAsync();
        await using var v_SaveTransaction = v_SaveConnection.BeginTransaction();
        await using var v_DeleteTransaction = v_DeleteConnection.BeginTransaction();

        try
        {
            await AcquireIssueFenceAsync(v_DeleteConnection, v_DeleteTransaction, v_Scope);

            var v_SaveError = await Assert.ThrowsAsync<SqlException>(() => SaveIssueDetailAsync(
                v_SaveConnection, v_SaveTransaction, v_Scope, p_Quantity: 1m, autoId: v_Scope.DetailId));
            Assert.Equal(51407, v_SaveError.Number);
            await v_SaveTransaction.RollbackAsync();

            await ExecuteStoredAsync(v_DeleteConnection, v_DeleteTransaction, "dbo.F2012_sp_del_Xuat_Kho_Header",
                BigInt("@Auto_ID", v_Scope.IssueId), Text("@Last_Updated_By", v_Scope.Login, 100),
                Text("@Last_Updated_By_Function", "TDD", 100), Text("@Ma_Dang_Nhap", v_Scope.Login, 100));
            await v_DeleteTransaction.CommitAsync();

            await AssertReservationInvariantAsync(v_Scope, p_bHeaderShouldExist: false);
        }
        finally
        {
            if (v_SaveTransaction.Connection is not null)
            {
                try { await v_SaveTransaction.RollbackAsync(); } catch { }
            }
            if (v_DeleteTransaction.Connection is not null)
            {
                try { await v_DeleteTransaction.RollbackAsync(); } catch { }
            }
            await CleanupPersistentIssueScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task N05_save_fence_wins_first_and_delete_fails_closed_at_group_fence()
    {
        var v_Scope = await CreatePersistentIssueScopeAsync("TDD-N05-SAVE-FIRST");
        await using (var v_SetupConnection = new SqlConnection(ConnectionString))
        {
            await v_SetupConnection.OpenAsync();
            await using var v_SetupTransaction = v_SetupConnection.BeginTransaction();
            v_Scope = v_Scope with
            {
                DetailId = await SaveIssueDetailAsync(v_SetupConnection, v_SetupTransaction, v_Scope, p_Quantity: 1m)
            };
            await v_SetupTransaction.CommitAsync();
        }

        await using var v_SaveConnection = new SqlConnection(ConnectionString);
        await using var v_DeleteConnection = new SqlConnection(ConnectionString);
        await v_SaveConnection.OpenAsync();
        await v_DeleteConnection.OpenAsync();
        await using var v_SaveTransaction = v_SaveConnection.BeginTransaction();

        try
        {
            // Model the approved Root -> Group -> Scope -> Row order without
            // holding the parent row before the canonical group fence.
            await AcquireIssueFenceAsync(v_SaveConnection, v_SaveTransaction, v_Scope);
            await ExecuteAsync(v_SaveConnection, v_SaveTransaction,
                "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WITH (UPDLOCK, HOLDLOCK) WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));

            var v_DeleteError = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(
                v_DeleteConnection,
                null,
                "dbo.F2012_sp_del_Xuat_Kho_Header",
                BigInt("@Auto_ID", v_Scope.IssueId), Text("@Last_Updated_By", v_Scope.Login, 100),
                Text("@Last_Updated_By_Function", "TDD", 100), Text("@Ma_Dang_Nhap", v_Scope.Login, 100)));

            Assert.Equal(51407, v_DeleteError.Number);
            await v_SaveTransaction.RollbackAsync();

            await ExecuteStoredAsync(v_DeleteConnection, null, "dbo.F2012_sp_del_Xuat_Kho_Header",
                BigInt("@Auto_ID", v_Scope.IssueId), Text("@Last_Updated_By", v_Scope.Login, 100),
                Text("@Last_Updated_By_Function", "TDD", 100), Text("@Ma_Dang_Nhap", v_Scope.Login, 100));

            await AssertReservationInvariantAsync(v_Scope, p_bHeaderShouldExist: false);
        }
        finally
        {
            try { await v_SaveTransaction.RollbackAsync(); } catch { }
            await CleanupPersistentIssueScopeAsync(v_Scope);
        }
    }

    [Fact]
    public async Task N05_save_detail_wins_before_delete_and_delete_releases_all_reservations()
    {
        var v_Scope = await CreatePersistentIssueScopeAsync("TDD-N05-SAVE-WINS");
        try
        {
            await using (var v_Connection = new SqlConnection(ConnectionString))
            {
                await v_Connection.OpenAsync();
                await using var v_Transaction = v_Connection.BeginTransaction();
                await SaveIssueDetailAsync(v_Connection, v_Transaction, v_Scope, p_Quantity: 1m);
                await v_Transaction.CommitAsync();
            }

            await using (var v_Connection = new SqlConnection(ConnectionString))
            {
                await v_Connection.OpenAsync();
                await ExecuteStoredAsync(v_Connection, null, "dbo.F2012_sp_del_Xuat_Kho_Header",
                    BigInt("@Auto_ID", v_Scope.IssueId), Text("@Last_Updated_By", v_Scope.Login, 100),
                    Text("@Last_Updated_By_Function", "TDD", 100), Text("@Ma_Dang_Nhap", v_Scope.Login, 100));
            }

            await AssertReservationInvariantAsync(v_Scope, p_bHeaderShouldExist: false);
        }
        finally
        {
            await CleanupPersistentIssueScopeAsync(v_Scope);
        }
    }

    private static async Task<Scope> CreatePostedIssueScopeAsync(SqlConnection p_Connection, SqlTransaction p_Transaction)
    {
        var v_Scope = await CreateBasicScopeAsync(p_Connection, p_Transaction, "TDD-N02");
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 8, 0);",
            BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
        await ExecuteAsync(p_Connection, p_Transaction,
            "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1; INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted) VALUES (@ReceiptNumber, @WarehouseId, @SupplierId, '2099-01-01', 1); DECLARE @ReceiptId BIGINT = SCOPE_IDENTITY(); INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, 10, 1); INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted) VALUES (@IssueNumber, @WarehouseId, '2099-01-02', 1); DECLARE @IssueId BIGINT = SCOPE_IDENTITY(); INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, 2, 1); EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;",
            Text("@ReceiptNumber", $"{v_Scope.Tag}-receipt", 100), Text("@IssueNumber", $"{v_Scope.Tag}-issue", 100),
            BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@SupplierId", v_Scope.SupplierId), BigInt("@ProductId", v_Scope.ProductId));
        return v_Scope with { IssueId = await LongScalarAsync(p_Connection, p_Transaction,
            "SELECT TOP (1) Auto_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @Number;",
            Text("@Number", $"{v_Scope.Tag}-issue", 100)), DetailId = await LongScalarAsync(p_Connection, p_Transaction,
            "SELECT TOP (1) d.Auto_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.So_Phieu_Xuat_Kho = @Number;",
            Text("@Number", $"{v_Scope.Tag}-issue", 100))};
    }

    private static async Task<Scope> CreatePersistentIssueScopeAsync(string p_Prefix)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        var v_Scope = await CreateBasicScopeAsync(v_Connection, v_Transaction, p_Prefix);
        await ExecuteAsync(v_Connection, v_Transaction,
            "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, 10, 0); INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted) VALUES (@Number, @WarehouseId, '2099-03-01', 0);",
            Text("@Number", $"{v_Scope.Tag}-issue", 100), BigInt("@WarehouseId", v_Scope.WarehouseId), BigInt("@ProductId", v_Scope.ProductId));
        var issueId = await LongScalarAsync(v_Connection, v_Transaction,
            "SELECT Auto_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @Number;",
            Text("@Number", $"{v_Scope.Tag}-issue", 100));
        await v_Transaction.CommitAsync();
        return v_Scope with { IssueId = issueId, DetailId = 0 };
    }

    private static async Task<long> SaveIssueDetailAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Scope p_Scope, decimal p_Quantity, long autoId = 0)
    {
        string v_strProcedure;
        if (autoId == 0)
        {
            v_strProcedure = "dbo.F2012_sp_ins_Xuat_Kho_Detail";
        }
        else
        {
            v_strProcedure = "dbo.F2012_sp_upd_Xuat_Kho_Detail";
        }

        return await ExecuteStoredIdAsync(p_Connection, p_Transaction, v_strProcedure,
            BigIntOutput("@Auto_ID", autoId), BigInt("@Xuat_Kho_ID", p_Scope.IssueId), BigInt("@San_Pham_ID", p_Scope.ProductId),
            Decimal("@SL_Xuat", p_Quantity), Decimal("@Don_Gia_Xuat", 1), Text("@Ma_Dang_Nhap", p_Scope.Login, 100),
            Text("@Created_By", p_Scope.Login, 100), Text("@Created_By_Function", "TDD", 100),
            Text("@Last_Updated_By", p_Scope.Login, 100), Text("@Last_Updated_By_Function", "TDD", 100));
    }

    private static async Task AcquireIssueFenceAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Scope p_Scope)
    {
        await ExecuteAsync(p_Connection, p_Transaction,
            """
            DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
            DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
            INSERT @GroupSet(Kho_ID) VALUES (@WarehouseId);
            INSERT @ScopeSet(Kho_ID, San_Pham_ID) VALUES (@WarehouseId, @ProductId);
            EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';
            EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set @GroupSet = @GroupSet, @Mode = N'Exclusive';
            EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope_Set @ScopeSet = @ScopeSet, @Mode = N'Exclusive';
            """,
            BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId));
    }

    private static async Task<Scope> CreateBasicScopeAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Prefix)
    {
        var productId = await LongScalarAsync(p_Connection, p_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
        var supplierId = await LongScalarAsync(p_Connection, p_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
        var v_Tag = $"{p_Prefix}-{Guid.NewGuid():N}"[..40];
        var warehouseId = await LongScalarAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10 Gate 1');",
            Text("@Name", v_Tag, 255));
        var v_Login = $"{v_Tag}-login";
        var memberId = await LongScalarAsync(p_Connection, p_Transaction,
            "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10 Gate 1', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
            BigInt("@MemberId", memberId), Text("@Login", v_Login, 100), BigInt("@WarehouseId", warehouseId));
        return new Scope(warehouseId, productId, supplierId, v_Tag, v_Login, 0, 0);
    }

    private static async Task EnsureDirectDmlProbeUserAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(
            v_Connection,
            null,
            $"""
            SET QUOTED_IDENTIFIER ON;
            IF DATABASE_PRINCIPAL_ID(N'{DirectDmlProbeUser}') IS NULL
                CREATE USER [{DirectDmlProbeUser}] WITHOUT LOGIN;
            GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Xuat_Kho_Raw_Data TO [{DirectDmlProbeUser}];
            GRANT SELECT ON OBJECT::dbo.tbl_XNK_Xuat_Kho TO [{DirectDmlProbeUser}];
            """);
    }

    private static async Task DropDirectDmlProbeUserAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null, $"IF DATABASE_PRINCIPAL_ID(N'{DirectDmlProbeUser}') IS NOT NULL DROP USER [{DirectDmlProbeUser}];");
    }

    private static async Task AssertReservationInvariantAsync(Scope p_Scope, bool p_bHeaderShouldExist)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        var v_iHeaderCount = await IntScalarAsync(v_Connection, null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId;", BigInt("@IssueId", p_Scope.IssueId));
        var v_ReservationSum = await DecimalScalarAsync(v_Connection, null,
            "SELECT COALESCE(SUM(ReservedQuantity), 0) FROM dbo.InventoryReservation_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId));
        var v_CurrentReserved = await DecimalScalarAsync(v_Connection, null,
            "SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId));
        int v_iExpectedHeaderCount;
        if (p_bHeaderShouldExist == true)
        {
            v_iExpectedHeaderCount = 1;
        }
        else
        {
            v_iExpectedHeaderCount = 0;
        }

        Assert.Equal(v_iExpectedHeaderCount, v_iHeaderCount);
        Assert.Equal(v_ReservationSum, v_CurrentReserved);
        Assert.Equal(0m, v_ReservationSum);
    }

    private static async Task CleanupPersistentIssueScopeAsync(Scope p_Scope)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE r FROM dbo.InventoryReservation_Current r JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.Auto_ID = @IssueId; DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@IssueId", p_Scope.IssueId), BigInt("@WarehouseId", p_Scope.WarehouseId), BigInt("@ProductId", p_Scope.ProductId), Text("@Login", p_Scope.Login, 100));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<bool> WaitForLockWaitAsync(int p_iSessionId)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        for (var v_iAttempt = 0; v_iAttempt < 200; v_iAttempt++)
        {
            var v_objWaitType = await ScalarAsync(v_Connection, null,
                "SELECT wait_type FROM sys.dm_exec_requests WHERE session_id = @SessionId AND wait_type LIKE N'LCK_M_%';",
                Int("@SessionId", p_iSessionId));
            if (v_objWaitType is string v_Text && v_Text.StartsWith("LCK_M_", StringComparison.Ordinal))
                return true;
            await Task.Delay(25);
        }
        return false;
    }

    private static async Task<DailyRow> ReadDailyAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Scope p_Scope, DateTime p_dtmDate)
    {
        await using var v_Command = new SqlCommand(
            "SELECT OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued FROM dbo.Inventory_Balance_Daily WHERE Balance_Date = @Date AND Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            p_Connection, p_Transaction);
        v_Command.Parameters.Add(Date("@Date", p_dtmDate));
        v_Command.Parameters.Add(BigInt("@WarehouseId", p_Scope.WarehouseId));
        v_Command.Parameters.Add(BigInt("@ProductId", p_Scope.ProductId));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        return new DailyRow(v_Reader.GetDecimal(0), v_Reader.GetDecimal(1), v_Reader.GetDecimal(2), v_Reader.GetDecimal(3), v_Reader.GetDecimal(4), v_Reader.GetDecimal(5));
    }

    private static async Task ExecuteStoredAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteStoredIdAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_Command.Parameters["@Auto_ID"].Value);
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<int> IntScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt32(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }
    private static async Task<long> LongScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }
    private static async Task<decimal> DecimalScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToDecimal(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
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
    private static SqlParameter Int(string p_Name, int p_iValue)
    {
        return new(p_Name, SqlDbType.Int)
        {
            Value = p_iValue
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

    private sealed record Scope(long WarehouseId, long ProductId, long SupplierId, string Tag, string Login, long IssueId, long DetailId);
    private sealed record DailyRow(decimal OpeningQuantity, decimal TotalReceived, decimal TotalIssued, decimal ClosingQuantity, decimal CumulativeReceived, decimal CumulativeIssued);
}
