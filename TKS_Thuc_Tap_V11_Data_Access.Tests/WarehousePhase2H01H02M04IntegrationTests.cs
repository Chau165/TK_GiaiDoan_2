using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase2H01H02M04IntegrationTests : IAsyncLifetime
{
    private static string BaseConnectionString =>
        Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "TKS_INTEGRATION_CONNECTION_STRING must point to a disposable test database.");

    private Fixture? m_objFixture;

    public async Task InitializeAsync() => m_objFixture = await CreateFixtureAsync();

    public async Task DisposeAsync()
    {
        if (m_objFixture is not null)
            await CleanupFixtureAsync(m_objFixture);
    }

    [Fact]
    public async Task Issue_save_header_and_detail_without_ambient_transaction_close_their_own_transactions()
    {
        var fixture = m_objFixture!;
        await CreatePostedStockAsync(fixture, 20m, "H01-standalone-stock");

        await using var connection = OpenConnection($"H01-standalone-{fixture.Tag}");
        await connection.OpenAsync();
        var issueId = await SaveIssueHeaderAsync(
            fixture,
            fixture.WarehouseAId,
            fixture.LoginBoth,
            "H01-standalone",
            connection: connection);
        Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT @@TRANCOUNT;"));

        var detailId = await SaveIssueDetailAsync(
            fixture,
            issueId,
            5m,
            fixture.LoginBoth,
            connection: connection);

        Assert.True(detailId > 0);
        Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT @@TRANCOUNT;"));
        Assert.Equal(1, await IntScalarAsync(
            connection,
            null,
            "SELECT COUNT(*) FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
    }

    [Fact]
    public async Task Issue_save_inside_ambient_transaction_preserves_caller_and_rolls_back_with_it()
    {
        var fixture = m_objFixture!;
        await CreatePostedStockAsync(fixture, 20m, "H01-ambient-stock");

        await using var connection = OpenConnection($"H01-ambient-{fixture.Tag}");
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, "CREATE TABLE #AmbientMarker(Value INT NOT NULL);");
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, "INSERT #AmbientMarker(Value) VALUES (1);");

        var issueId = await SaveIssueHeaderAsync(
            fixture,
            fixture.WarehouseAId,
            fixture.LoginBoth,
            "H01-ambient",
            connection: connection,
            transaction: transaction);
        var detailId = await SaveIssueDetailAsync(
            fixture,
            issueId,
            5m,
            fixture.LoginBoth,
            connection: connection,
            transaction: transaction);
        await ExecuteAsync(connection, transaction, "INSERT #AmbientMarker(Value) VALUES (2);");

        Assert.True(detailId > 0);
        Assert.Equal(1, await IntScalarAsync(connection, transaction, "SELECT @@TRANCOUNT;"));
        Assert.Equal(2, await IntScalarAsync(connection, transaction, "SELECT COUNT(*) FROM #AmbientMarker;"));
        Assert.Equal(1, await IntScalarAsync(
            connection,
            transaction,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId;",
            BigInt("@IssueId", issueId)));

        await transaction.RollbackAsync();

        Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT @@TRANCOUNT;"));
        Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT COUNT(*) FROM #AmbientMarker;"));
        Assert.Equal(0, await IntScalarAsync(
            connection,
            null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId;",
            BigInt("@IssueId", issueId)));
        Assert.Equal(0, await IntScalarAsync(
            connection,
            null,
            "SELECT COUNT(*) FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
    }

    [Fact]
    public async Task Issue_save_error_inside_ambient_transaction_does_not_commit_or_destroy_caller_work()
    {
        var fixture = m_objFixture!;
        await CreatePostedStockAsync(fixture, 20m, "H01-error-stock");
        var issueId = await SaveIssueHeaderAsync(fixture, fixture.WarehouseAId, fixture.LoginBoth, "H01-error");

        await using var connection = OpenConnection($"H01-error-{fixture.Tag}");
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, "CREATE TABLE #AmbientMarker(Value INT NOT NULL);");
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, "INSERT #AmbientMarker(Value) VALUES (1);");

        await AssertSqlNumberAsync(
            51136,
            () => SaveIssueDetailAsync(
                fixture,
                issueId,
                0m,
                fixture.LoginBoth,
                connection: connection,
                transaction: transaction));

        Assert.Equal(1, await IntScalarAsync(connection, transaction, "SELECT @@TRANCOUNT;"));
        Assert.Contains(await IntScalarAsync(connection, transaction, "SELECT XACT_STATE();"), new[] { 1, -1 });
        Assert.Equal(1, await IntScalarAsync(connection, transaction, "SELECT COUNT(*) FROM #AmbientMarker;"));

        await transaction.RollbackAsync();
        Assert.Equal(0, await IntScalarAsync(connection, null, "SELECT COUNT(*) FROM #AmbientMarker;"));
    }

    [Fact]
    public async Task Receipt_update_requires_access_to_old_and_new_warehouse_and_allows_both()
    {
        var fixture = m_objFixture!;
        var receiptId = await SaveReceiptHeaderAsync(fixture, fixture.WarehouseAId, fixture.LoginAOnly, "H02-receipt");

        await AssertSqlNumberAsync(
            51054,
            () => SaveReceiptHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginBOnly, "H02-receipt", receiptId));
        Assert.Equal(fixture.WarehouseAId, await WarehouseOfReceiptAsync(fixture, receiptId));

        await AssertSqlNumberAsync(
            51054,
            () => SaveReceiptHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginAOnly, "H02-receipt", receiptId));
        Assert.Equal(fixture.WarehouseAId, await WarehouseOfReceiptAsync(fixture, receiptId));

        await SaveReceiptHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginBoth, "H02-receipt", receiptId);
        Assert.Equal(fixture.WarehouseBId, await WarehouseOfReceiptAsync(fixture, receiptId));

        await SaveReceiptHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginBOnly, "H02-receipt-same-warehouse", receiptId);
        await AssertSqlNumberAsync(
            51054,
            () => SaveReceiptHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginAOnly, "H02-receipt-same-warehouse", receiptId));
        Assert.Equal(fixture.WarehouseBId, await WarehouseOfReceiptAsync(fixture, receiptId));
    }

    [Fact]
    public async Task Issue_move_requires_old_and_new_warehouse_access_and_moves_reservation_only_when_authorized()
    {
        var fixture = m_objFixture!;
        await CreatePostedStockAsync(fixture, 20m, "H02-issue-stock");
        await CreatePostedStockAsync(fixture, 20m, "H02-issue-stock-b", fixture.WarehouseBId);
        var issueId = await SaveIssueHeaderAsync(fixture, fixture.WarehouseAId, fixture.LoginBoth, "H02-issue");
        var detailId = await SaveIssueDetailAsync(fixture, issueId, 5m, fixture.LoginBoth);

        await AssertSqlNumberAsync(
            51054,
            () => SaveIssueHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginBOnly, "H02-issue", issueId));
        await AssertIssueReservationAsync(fixture, issueId, detailId, fixture.WarehouseAId, 5m, 0m);

        await AssertSqlNumberAsync(
            51054,
            () => SaveIssueHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginAOnly, "H02-issue", issueId));
        await AssertIssueReservationAsync(fixture, issueId, detailId, fixture.WarehouseAId, 5m, 0m);

        await SaveIssueHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginBoth, "H02-issue", issueId);
        Assert.Equal(fixture.WarehouseBId, await WarehouseOfIssueAsync(fixture, issueId));
        await AssertIssueReservationAsync(fixture, issueId, detailId, fixture.WarehouseBId, 0m, 5m);

        await SaveIssueHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginBOnly, "H02-issue-same-warehouse", issueId);
        await AssertSqlNumberAsync(
            51054,
            () => SaveIssueHeaderAsync(fixture, fixture.WarehouseBId, fixture.LoginAOnly, "H02-issue-same-warehouse", issueId));
    }

    [Fact]
    public async Task Issue_draft_reservation_edit_delete_post_and_posted_mutations_preserve_stock()
    {
        var fixture = m_objFixture!;
        await CreatePostedStockAsync(fixture, 20m, "issue-lifecycle-stock");
        var issueId = await SaveIssueHeaderAsync(fixture, fixture.WarehouseAId, fixture.LoginBoth, "issue-lifecycle");
        var detailId = await SaveIssueDetailAsync(fixture, issueId, 5m, fixture.LoginBoth);

        await AssertIssueReservationAsync(fixture, issueId, detailId, fixture.WarehouseAId, 5m, 0m);
        await SaveIssueDetailAsync(fixture, issueId, 7m, fixture.LoginBoth, detailId);
        await AssertIssueReservationAsync(fixture, issueId, detailId, fixture.WarehouseAId, 7m, 0m);

        await DeleteIssueDetailAsync(fixture, detailId, fixture.LoginBoth);
        Assert.Equal(0, await IntScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
        Assert.Equal(0m, await DecimalScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COALESCE((SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId), 0);",
            BigInt("@WarehouseId", fixture.WarehouseAId),
            BigInt("@ProductId", fixture.ProductId)));

        detailId = await SaveIssueDetailAsync(fixture, issueId, 5m, fixture.LoginBoth);
        await PostIssueAsync(fixture, issueId, fixture.LoginBoth);

        Assert.Equal(1, await IntScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId AND Is_Posted = 1;",
            BigInt("@IssueId", issueId)));
        Assert.Equal(15m, await DecimalScalarAsync(
            BaseConnectionString,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", fixture.WarehouseAId),
            BigInt("@ProductId", fixture.ProductId)));
        Assert.Equal(0, await IntScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COUNT(*) FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));

        await AssertSqlNumberAsync(51162, () => PostIssueAsync(fixture, issueId, fixture.LoginBoth));
        await AssertSqlNumberAsync(51163, () => SaveIssueDetailAsync(fixture, issueId, 6m, fixture.LoginBoth, detailId));
        await AssertSqlNumberAsync(51163, () => DeleteIssueDetailAsync(fixture, detailId, fixture.LoginBoth));
        await AssertSqlNumberAsync(51163, () => SaveIssueDetailAsync(fixture, issueId, 1m, fixture.LoginBoth));
    }

    [Fact]
    public async Task Receipt_detail_update_persists_exactly_one_existing_row()
    {
        var fixture = m_objFixture!;
        var receiptId = await SaveReceiptHeaderAsync(fixture, fixture.WarehouseAId, fixture.LoginAOnly, "M04-normal");
        var detailId = await SaveReceiptDetailAsync(fixture, receiptId, 10m, fixture.LoginAOnly);

        await SaveReceiptDetailAsync(fixture, receiptId, 12m, fixture.LoginAOnly, detailId);

        Assert.Equal(1, await IntScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @DetailId AND Nhap_Kho_ID = @ReceiptId AND SL_Nhap = 12;",
            BigInt("@DetailId", detailId),
            BigInt("@ReceiptId", receiptId)));
    }

    [Fact]
    public async Task Receipt_detail_concurrent_delete_then_update_is_rejected_as_not_found()
    {
        var fixture = m_objFixture!;
        var receiptId = await SaveReceiptHeaderAsync(fixture, fixture.WarehouseAId, fixture.LoginAOnly, "M04-race");
        var detailId = await SaveReceiptDetailAsync(fixture, receiptId, 10m, fixture.LoginAOnly);

        await using var deleteConnection = OpenConnection($"M04-delete-{fixture.Tag}");
        await deleteConnection.OpenAsync();
        await using var deleteTransaction = (SqlTransaction)await deleteConnection.BeginTransactionAsync();
        await ExecuteAsync(
            deleteConnection,
            deleteTransaction,
            "DELETE dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @DetailId;",
            BigInt("@DetailId", detailId));

        await using var updateConnection = OpenConnection($"M04-update-{fixture.Tag}");
        await updateConnection.OpenAsync();
        var updateSessionId = await IntScalarAsync(updateConnection, null, "SELECT @@SPID;");
        var updateOutcomeTask = CaptureAsync(() => SaveReceiptDetailAsync(
            fixture,
            receiptId,
            12m,
            fixture.LoginAOnly,
            detailId,
            updateConnection));

        await WaitForSqlLockAsync(updateConnection, updateSessionId, updateOutcomeTask);
        await deleteTransaction.CommitAsync();
        var updateOutcome = await updateOutcomeTask;

        Assert.False(updateOutcome.Succeeded, FormatFailure("Receipt detail update", updateOutcome.Error));
        Assert.Equal(51109, (updateOutcome.Error as SqlException)?.Number);
        Assert.Equal(0, await IntScalarAsync(
            updateConnection,
            null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var tag = $"H01H02M04-{Guid.NewGuid():N}"[..24];
        var loginAOnly = $"{tag}-a";
        var loginBOnly = $"{tag}-b";
        var loginBoth = $"{tag}-both";

        await using var connection = OpenConnection($"H01H02M04-setup-{tag}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var unitId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-unit", 200));
            var categoryId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{tag}-category-code", 100),
                Text("@Name", $"{tag}-category", 200));
            var productId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{tag}-product-1", 100),
                Text("@Name", $"{tag}-product-1", 255),
                BigInt("@CategoryId", categoryId),
                BigInt("@UnitId", unitId));
            var secondProductId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{tag}-product-2", 100),
                Text("@Name", $"{tag}-product-2", 255),
                BigInt("@CategoryId", categoryId),
                BigInt("@UnitId", unitId));
            var supplierId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{tag}-supplier-code", 100),
                Text("@Name", $"{tag}-supplier", 255));
            var warehouseAId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-a", 255));
            var warehouseBId = await InsertIdAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-b", 255));

            await InsertMemberAsync(connection, transaction, loginAOnly, $"{tag}-member-a");
            await InsertMemberAsync(connection, transaction, loginBOnly, $"{tag}-member-b");
            await InsertMemberAsync(connection, transaction, loginBoth, $"{tag}-member-both");
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@LoginA, @WarehouseA), (@LoginB, @WarehouseB), (@LoginBoth, @WarehouseA), (@LoginBoth, @WarehouseB);",
                Text("@LoginA", loginAOnly, 100),
                Text("@LoginB", loginBOnly, 100),
                Text("@LoginBoth", loginBoth, 100),
                BigInt("@WarehouseA", warehouseAId),
                BigInt("@WarehouseB", warehouseBId));

            await transaction.CommitAsync();
            return new Fixture(
                tag,
                loginAOnly,
                loginBOnly,
                loginBoth,
                unitId,
                categoryId,
                productId,
                secondProductId,
                supplierId,
                warehouseAId,
                warehouseBId,
                new SqlConnectionStringBuilder(BaseConnectionString).ConnectionString);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long> SaveReceiptHeaderAsync(
        Fixture fixture,
        long warehouseId,
        string login,
        string suffix,
        long autoId = 0,
        SqlConnection? connection = null,
        SqlTransaction? transaction = null)
    {
        var ownsConnection = connection is null;
        connection ??= OpenConnection($"H02-receipt-header-{fixture.Tag}");
        if (ownsConnection)
            await connection.OpenAsync();
        try
        {
            return await ExecuteStoredWithOutputAsync(
                connection,
                transaction,
                "dbo.sp_XNK_Nhap_Kho_Save_Header",
                BigInt("@Auto_ID", autoId),
                Text("@So_Phieu_Nhap_Kho", $"{fixture.Tag}-{suffix}-{Guid.NewGuid():N}", 100),
                BigInt("@Kho_ID", warehouseId),
                BigInt("@NCC_ID", fixture.SupplierId),
                Date("@Ngay_Nhap_Kho", new DateTime(2026, 2, 1)),
                Text("@Ghi_Chu", "phase 2 test", 1000),
                Text("@Ma_Dang_Nhap", login, 100));
        }
        finally
        {
            if (ownsConnection)
                await connection.DisposeAsync();
        }
    }

    private static async Task<long> SaveIssueHeaderAsync(
        Fixture fixture,
        long warehouseId,
        string login,
        string suffix,
        long autoId = 0,
        SqlConnection? connection = null,
        SqlTransaction? transaction = null)
    {
        var ownsConnection = connection is null;
        connection ??= OpenConnection($"H01H02-issue-header-{fixture.Tag}");
        if (ownsConnection)
            await connection.OpenAsync();
        try
        {
            return await ExecuteStoredWithOutputAsync(
                connection,
                transaction,
                "dbo.sp_XNK_Xuat_Kho_Save_Header",
                BigInt("@Auto_ID", autoId),
                Text("@So_Phieu_Xuat_Kho", $"{fixture.Tag}-{suffix}-{Guid.NewGuid():N}", 100),
                BigInt("@Kho_ID", warehouseId),
                Date("@Ngay_Xuat_Kho", new DateTime(2026, 2, 2)),
                Text("@Ghi_Chu", "phase 2 test", 1000),
                Text("@Ma_Dang_Nhap", login, 100));
        }
        finally
        {
            if (ownsConnection)
                await connection.DisposeAsync();
        }
    }

    private static async Task<long> SaveReceiptDetailAsync(
        Fixture fixture,
        long receiptId,
        decimal quantity,
        string login,
        long detailId = 0,
        SqlConnection? connection = null,
        SqlTransaction? transaction = null)
    {
        var ownsConnection = connection is null;
        connection ??= OpenConnection($"M04-receipt-detail-{fixture.Tag}");
        if (ownsConnection)
            await connection.OpenAsync();
        try
        {
            return await ExecuteStoredWithOutputAsync(
                connection,
                transaction,
                "dbo.sp_XNK_Nhap_Kho_Save_Detail",
                BigInt("@Auto_ID", detailId),
                BigInt("@Nhap_Kho_ID", receiptId),
                BigInt("@San_Pham_ID", fixture.ProductId),
                Decimal("@SL_Nhap", quantity),
                Decimal("@Don_Gia_Nhap", 1m),
                Text("@Ma_Dang_Nhap", login, 100));
        }
        finally
        {
            if (ownsConnection)
                await connection.DisposeAsync();
        }
    }

    private static async Task<long> SaveIssueDetailAsync(
        Fixture fixture,
        long issueId,
        decimal quantity,
        string login,
        long detailId = 0,
        SqlConnection? connection = null,
        SqlTransaction? transaction = null)
    {
        var ownsConnection = connection is null;
        connection ??= OpenConnection($"H01H02-issue-detail-{fixture.Tag}");
        if (ownsConnection)
            await connection.OpenAsync();
        try
        {
            return await ExecuteStoredWithOutputAsync(
                connection,
                transaction,
                "dbo.sp_XNK_Xuat_Kho_Save_Detail",
                BigInt("@Auto_ID", detailId),
                BigInt("@Xuat_Kho_ID", issueId),
                BigInt("@San_Pham_ID", fixture.ProductId),
                Decimal("@SL_Xuat", quantity),
                Decimal("@Don_Gia_Xuat", 1m),
                Text("@Ma_Dang_Nhap", login, 100));
        }
        finally
        {
            if (ownsConnection)
                await connection.DisposeAsync();
        }
    }

    private static async Task CreatePostedStockAsync(Fixture fixture, decimal quantity, string suffix, long? warehouseId = null)
    {
        var receiptId = await SaveReceiptHeaderAsync(fixture, warehouseId ?? fixture.WarehouseAId, fixture.LoginBoth, suffix);
        await SaveReceiptDetailAsync(fixture, receiptId, quantity, fixture.LoginBoth);
        await ExecuteStoredAsync(
            "dbo.sp_XNK_Document_Post",
            new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = true },
            BigInt("@Document_ID", receiptId),
            Text("@Ma_Dang_Nhap", fixture.LoginBoth, 100));
    }

    private static Task PostIssueAsync(Fixture fixture, long issueId, string login) => ExecuteStoredAsync(
        "dbo.sp_XNK_Document_Post",
        new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = false },
        BigInt("@Document_ID", issueId),
        Text("@Ma_Dang_Nhap", login, 100));

    private static Task DeleteIssueDetailAsync(Fixture fixture, long detailId, string login) => ExecuteStoredAsync(
        "dbo.sp_XNK_Xuat_Kho_Delete_Detail",
        BigInt("@Auto_ID", detailId),
        Text("@Ma_Dang_Nhap", login, 100));

    private static async Task<long> WarehouseOfReceiptAsync(Fixture fixture, long receiptId) => await Int64ScalarAsync(
        BaseConnectionString,
        null,
        "SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @DocumentId;",
        BigInt("@DocumentId", receiptId));

    private static async Task<long> WarehouseOfIssueAsync(Fixture fixture, long issueId) => await Int64ScalarAsync(
        BaseConnectionString,
        null,
        "SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @DocumentId;",
        BigInt("@DocumentId", issueId));

    private static async Task AssertIssueReservationAsync(
        Fixture fixture,
        long issueId,
        long detailId,
        long expectedWarehouseId,
        decimal expectedAReserved,
        decimal expectedBReserved)
    {
        Assert.Equal(expectedWarehouseId, await WarehouseOfIssueAsync(fixture, issueId));
        Assert.Equal(expectedWarehouseId, await Int64ScalarAsync(
            BaseConnectionString,
            null,
            "SELECT Kho_ID FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
        Assert.Equal(expectedAReserved, await DecimalScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COALESCE((SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId), 0);",
            BigInt("@WarehouseId", fixture.WarehouseAId),
            BigInt("@ProductId", fixture.ProductId)));
        Assert.Equal(expectedBReserved, await DecimalScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COALESCE((SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId), 0);",
            BigInt("@WarehouseId", fixture.WarehouseBId),
            BigInt("@ProductId", fixture.ProductId)));
    }

    private static async Task WaitForSqlLockAsync(SqlConnection connection, int sessionId, Task<OperationOutcome> operation)
    {
        await using var monitor = OpenConnection("H01H02M04-monitor");
        await monitor.OpenAsync();
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (operation.IsCompleted)
                break;
            await using var command = new SqlCommand(
                "SELECT TOP (1) wait_type, blocking_session_id FROM sys.dm_exec_requests WHERE session_id = @SessionId;",
                monitor);
            command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var waitType = reader.IsDBNull(0) ? null : reader.GetString(0);
                var blockingSessionId = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                if (blockingSessionId > 0 && waitType?.StartsWith("LCK_", StringComparison.OrdinalIgnoreCase) == true)
                    return;
            }
            await Task.Delay(50);
        }

        if (!operation.IsCompleted)
            throw new Xunit.Sdk.XunitException("The concurrent operation did not reach a SQL lock wait.");
    }

    private static async Task<OperationOutcome> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return new OperationOutcome(true, null);
        }
        catch (Exception exception)
        {
            return new OperationOutcome(false, exception);
        }
    }

    private static string FormatFailure(string operation, Exception? exception) =>
        exception is null ? $"{operation} unexpectedly succeeded." : $"{operation} failed: {exception.Message}";

    private static async Task AssertSqlNumberAsync(int expectedNumber, Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(operation);
        Assert.Equal(expectedNumber, exception.Number);
    }

    private static async Task InsertMemberAsync(SqlConnection connection, SqlTransaction transaction, string login, string name) =>
        await ExecuteAsync(
            connection,
            transaction,
            "DECLARE @MemberId BIGINT = CONVERT(BIGINT, ABS(CHECKSUM(NEWID()))); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, Trang_Thai_ID, deleted) VALUES (@MemberId, @Login, @Name, 1, 0);",
            Text("@Login", login, 100),
            Text("@Name", name, 200));

    private static async Task CleanupFixtureAsync(Fixture fixture)
    {
        await using var connection = OpenConnection($"H01H02M04-cleanup-{fixture.Tag}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(connection, transaction, "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID IN (@WarehouseA, @WarehouseB) AND q.San_Pham_ID IN (@ProductId, @SecondProductId);", BigInt("@WarehouseA", fixture.WarehouseAId), BigInt("@WarehouseB", fixture.WarehouseBId), BigInt("@ProductId", fixture.ProductId), BigInt("@SecondProductId", fixture.SecondProductId));
            await ExecuteAsync(connection, transaction, "DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID IN (@WarehouseA, @WarehouseB) AND q.San_Pham_ID IN (@ProductId, @SecondProductId);", BigInt("@WarehouseA", fixture.WarehouseAId), BigInt("@WarehouseB", fixture.WarehouseBId), BigInt("@ProductId", fixture.ProductId), BigInt("@SecondProductId", fixture.SecondProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId);", BigInt("@WarehouseA", fixture.WarehouseAId), BigInt("@WarehouseB", fixture.WarehouseBId), BigInt("@ProductId", fixture.ProductId), BigInt("@SecondProductId", fixture.SecondProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId);", BigInt("@WarehouseA", fixture.WarehouseAId), BigInt("@WarehouseB", fixture.WarehouseBId), BigInt("@ProductId", fixture.ProductId), BigInt("@SecondProductId", fixture.SecondProductId));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE @TagPrefix; DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho LIKE @TagPrefix;", Text("@TagPrefix", $"{fixture.Tag}%", 100));
            await ExecuteAsync(connection, transaction, "DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB, @LoginBoth); DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB, @LoginBoth); DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId; DELETE FROM dbo.tbl_DM_San_Pham WHERE Auto_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId; DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId;", Text("@LoginA", fixture.LoginAOnly, 100), Text("@LoginB", fixture.LoginBOnly, 100), Text("@LoginBoth", fixture.LoginBoth, 100), BigInt("@WarehouseA", fixture.WarehouseAId), BigInt("@WarehouseB", fixture.WarehouseBId), BigInt("@SupplierId", fixture.SupplierId), BigInt("@ProductId", fixture.ProductId), BigInt("@SecondProductId", fixture.SecondProductId), BigInt("@CategoryId", fixture.CategoryId), BigInt("@UnitId", fixture.UnitId));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static SqlConnection OpenConnection(string applicationName)
    {
        var builder = new SqlConnectionStringBuilder(BaseConnectionString) { ApplicationName = applicationName };
        return new SqlConnection(builder.ConnectionString);
    }

    private static async Task<long> InsertIdAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> ExecuteStoredWithOutputAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure };
        var output = parameters.SingleOrDefault(parameter => parameter.ParameterName == "@Auto_ID") ?? BigInt("@Auto_ID", 0);
        output.Direction = ParameterDirection.InputOutput;
        command.Parameters.Add(output);
        command.Parameters.AddRange(parameters.Where(parameter => parameter != output).ToArray());
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(output.Value);
    }

    private static async Task ExecuteStoredAsync(string procedure, params SqlParameter[] parameters)
    {
        await using var connection = OpenConnection($"H01H02M04-{procedure}");
        await connection.OpenAsync();
        await using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<int> IntScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) =>
        Convert.ToInt32(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<int> IntScalarAsync(string connectionString, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var connection = OpenConnection("H01H02M04-int");
        await connection.OpenAsync();
        return await IntScalarAsync(connection, transaction, sql, parameters);
    }

    private static async Task<long> Int64ScalarAsync(string connectionString, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var connection = OpenConnection("H01H02M04-int64");
        await connection.OpenAsync();
        return Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));
    }

    private static async Task<decimal> DecimalScalarAsync(string connectionString, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var connection = OpenConnection("H01H02M04-decimal");
        await connection.OpenAsync();
        return Convert.ToDecimal(await ScalarAsync(connection, transaction, sql, parameters));
    }

    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };

    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal)
    {
        Precision = 18,
        Scale = 3,
        Value = value
    };

    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };

    private sealed record Fixture(
        string Tag,
        string LoginAOnly,
        string LoginBOnly,
        string LoginBoth,
        long UnitId,
        long CategoryId,
        long ProductId,
        long SecondProductId,
        long SupplierId,
        long WarehouseAId,
        long WarehouseBId,
        string ConnectionString);

    private sealed record OperationOutcome(bool Succeeded, Exception? Error);
}
