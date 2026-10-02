using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase2H01H02M04IntegrationTests : IAsyncLifetime
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
        m_objFixture = await CreateFixtureAsync();
    }

    public async Task DisposeAsync()
    {
        if (m_objFixture is not null)
            await CleanupFixtureAsync(m_objFixture);
    }

    [Fact]
    public async Task Issue_save_header_and_detail_without_ambient_transaction_close_their_own_transactions()
    {
        var v_Fixture = m_objFixture!;
        await CreatePostedStockAsync(v_Fixture, 20m, "H01-standalone-stock");

        await using var v_Connection = OpenConnection($"H01-standalone-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        var issueId = await SaveIssueHeaderAsync(
            v_Fixture,
            v_Fixture.WarehouseAId,
            v_Fixture.LoginBoth,
            "H01-standalone",
            p_Connection: v_Connection);
        Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT @@TRANCOUNT;"));

        var detailId = await SaveIssueDetailAsync(
            v_Fixture,
            issueId,
            5m,
            v_Fixture.LoginBoth,
            p_Connection: v_Connection);

        Assert.True(detailId > 0);
        Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT @@TRANCOUNT;"));
        Assert.Equal(1, await IntScalarAsync(
            v_Connection,
            null,
            "SELECT COUNT(*) FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
    }

    [Fact]
    public async Task Issue_save_inside_ambient_transaction_preserves_caller_and_rolls_back_with_it()
    {
        var v_Fixture = m_objFixture!;
        await CreatePostedStockAsync(v_Fixture, 20m, "H01-ambient-stock");

        await using var v_Connection = OpenConnection($"H01-ambient-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null, "CREATE TABLE #AmbientMarker(Value INT NOT NULL);");
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        await ExecuteAsync(v_Connection, v_Transaction, "INSERT #AmbientMarker(Value) VALUES (1);");

        var issueId = await SaveIssueHeaderAsync(
            v_Fixture,
            v_Fixture.WarehouseAId,
            v_Fixture.LoginBoth,
            "H01-ambient",
            p_Connection: v_Connection,
            p_Transaction: v_Transaction);
        var detailId = await SaveIssueDetailAsync(
            v_Fixture,
            issueId,
            5m,
            v_Fixture.LoginBoth,
            p_Connection: v_Connection,
            p_Transaction: v_Transaction);
        await ExecuteAsync(v_Connection, v_Transaction, "INSERT #AmbientMarker(Value) VALUES (2);");

        Assert.True(detailId > 0);
        Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction, "SELECT @@TRANCOUNT;"));
        Assert.Equal(2, await IntScalarAsync(v_Connection, v_Transaction, "SELECT COUNT(*) FROM #AmbientMarker;"));
        Assert.Equal(1, await IntScalarAsync(
            v_Connection,
            v_Transaction,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId;",
            BigInt("@IssueId", issueId)));

        await v_Transaction.RollbackAsync();

        Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT @@TRANCOUNT;"));
        Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT COUNT(*) FROM #AmbientMarker;"));
        Assert.Equal(0, await IntScalarAsync(
            v_Connection,
            null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId;",
            BigInt("@IssueId", issueId)));
        Assert.Equal(0, await IntScalarAsync(
            v_Connection,
            null,
            "SELECT COUNT(*) FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
    }

    [Fact]
    public async Task Issue_save_error_inside_ambient_transaction_does_not_commit_or_destroy_caller_work()
    {
        var v_Fixture = m_objFixture!;
        await CreatePostedStockAsync(v_Fixture, 20m, "H01-error-stock");
        var issueId = await SaveIssueHeaderAsync(v_Fixture, v_Fixture.WarehouseAId, v_Fixture.LoginBoth, "H01-error");

        await using var v_Connection = OpenConnection($"H01-error-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null, "CREATE TABLE #AmbientMarker(Value INT NOT NULL);");
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        await ExecuteAsync(v_Connection, v_Transaction, "INSERT #AmbientMarker(Value) VALUES (1);");

        await AssertSqlNumberAsync(
            51136,
            () => SaveIssueDetailAsync(
                v_Fixture,
                issueId,
                0m,
                v_Fixture.LoginBoth,
                p_Connection: v_Connection,
                p_Transaction: v_Transaction));

        Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction, "SELECT @@TRANCOUNT;"));
        Assert.Contains(await IntScalarAsync(v_Connection, v_Transaction, "SELECT XACT_STATE();"), new[] { 1, -1 });
        Assert.Equal(1, await IntScalarAsync(v_Connection, v_Transaction, "SELECT COUNT(*) FROM #AmbientMarker;"));

        await v_Transaction.RollbackAsync();
        Assert.Equal(0, await IntScalarAsync(v_Connection, null, "SELECT COUNT(*) FROM #AmbientMarker;"));
    }

    [Fact]
    public async Task Receipt_update_requires_access_to_old_and_new_warehouse_and_allows_both()
    {
        var v_Fixture = m_objFixture!;
        var receiptId = await SaveReceiptHeaderAsync(v_Fixture, v_Fixture.WarehouseAId, v_Fixture.LoginAOnly, "H02-receipt");

        await AssertSqlNumberAsync(
            51054,
            () => SaveReceiptHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginBOnly, "H02-receipt", receiptId));
        Assert.Equal(v_Fixture.WarehouseAId, await WarehouseOfReceiptAsync(v_Fixture, receiptId));

        await AssertSqlNumberAsync(
            51054,
            () => SaveReceiptHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginAOnly, "H02-receipt", receiptId));
        Assert.Equal(v_Fixture.WarehouseAId, await WarehouseOfReceiptAsync(v_Fixture, receiptId));

        await SaveReceiptHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginBoth, "H02-receipt", receiptId);
        Assert.Equal(v_Fixture.WarehouseBId, await WarehouseOfReceiptAsync(v_Fixture, receiptId));

        await SaveReceiptHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginBOnly, "H02-receipt-same-warehouse", receiptId);
        await AssertSqlNumberAsync(
            51054,
            () => SaveReceiptHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginAOnly, "H02-receipt-same-warehouse", receiptId));
        Assert.Equal(v_Fixture.WarehouseBId, await WarehouseOfReceiptAsync(v_Fixture, receiptId));
    }

    [Fact]
    public async Task Issue_move_requires_old_and_new_warehouse_access_and_moves_reservation_only_when_authorized()
    {
        var v_Fixture = m_objFixture!;
        await CreatePostedStockAsync(v_Fixture, 20m, "H02-issue-stock");
        await CreatePostedStockAsync(v_Fixture, 20m, "H02-issue-stock-b", v_Fixture.WarehouseBId);
        var issueId = await SaveIssueHeaderAsync(v_Fixture, v_Fixture.WarehouseAId, v_Fixture.LoginBoth, "H02-issue");
        var detailId = await SaveIssueDetailAsync(v_Fixture, issueId, 5m, v_Fixture.LoginBoth);

        await AssertSqlNumberAsync(
            51054,
            () => SaveIssueHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginBOnly, "H02-issue", issueId));
        await AssertIssueReservationAsync(v_Fixture, issueId, detailId, v_Fixture.WarehouseAId, 5m, 0m);

        await AssertSqlNumberAsync(
            51054,
            () => SaveIssueHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginAOnly, "H02-issue", issueId));
        await AssertIssueReservationAsync(v_Fixture, issueId, detailId, v_Fixture.WarehouseAId, 5m, 0m);

        await SaveIssueHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginBoth, "H02-issue", issueId);
        Assert.Equal(v_Fixture.WarehouseBId, await WarehouseOfIssueAsync(v_Fixture, issueId));
        await AssertIssueReservationAsync(v_Fixture, issueId, detailId, v_Fixture.WarehouseBId, 0m, 5m);

        await SaveIssueHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginBOnly, "H02-issue-same-warehouse", issueId);
        await AssertSqlNumberAsync(
            51054,
            () => SaveIssueHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_Fixture.LoginAOnly, "H02-issue-same-warehouse", issueId));
    }

    [Fact]
    public async Task Issue_draft_reservation_edit_delete_post_and_posted_mutations_preserve_stock()
    {
        var v_Fixture = m_objFixture!;
        await CreatePostedStockAsync(v_Fixture, 20m, "issue-lifecycle-stock");
        var issueId = await SaveIssueHeaderAsync(v_Fixture, v_Fixture.WarehouseAId, v_Fixture.LoginBoth, "issue-lifecycle");
        var detailId = await SaveIssueDetailAsync(v_Fixture, issueId, 5m, v_Fixture.LoginBoth);

        await AssertIssueReservationAsync(v_Fixture, issueId, detailId, v_Fixture.WarehouseAId, 5m, 0m);
        await SaveIssueDetailAsync(v_Fixture, issueId, 7m, v_Fixture.LoginBoth, detailId);
        await AssertIssueReservationAsync(v_Fixture, issueId, detailId, v_Fixture.WarehouseAId, 7m, 0m);

        await DeleteIssueDetailAsync(v_Fixture, detailId, v_Fixture.LoginBoth);
        Assert.Equal(0, await IntScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
        Assert.Equal(0m, await DecimalScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COALESCE((SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId), 0);",
            BigInt("@WarehouseId", v_Fixture.WarehouseAId),
            BigInt("@ProductId", v_Fixture.ProductId)));

        detailId = await SaveIssueDetailAsync(v_Fixture, issueId, 5m, v_Fixture.LoginBoth);
        await PostIssueAsync(v_Fixture, issueId, v_Fixture.LoginBoth);

        Assert.Equal(1, await IntScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId AND Is_Posted = 1;",
            BigInt("@IssueId", issueId)));
        Assert.Equal(15m, await DecimalScalarAsync(
            BaseConnectionString,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", v_Fixture.WarehouseAId),
            BigInt("@ProductId", v_Fixture.ProductId)));
        Assert.Equal(0, await IntScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COUNT(*) FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));

        await AssertSqlNumberAsync(51162, () => PostIssueAsync(v_Fixture, issueId, v_Fixture.LoginBoth));
        await AssertSqlNumberAsync(51163, () => SaveIssueDetailAsync(v_Fixture, issueId, 6m, v_Fixture.LoginBoth, detailId));
        await AssertSqlNumberAsync(51163, () => DeleteIssueDetailAsync(v_Fixture, detailId, v_Fixture.LoginBoth));
        await AssertSqlNumberAsync(51163, () => SaveIssueDetailAsync(v_Fixture, issueId, 1m, v_Fixture.LoginBoth));
    }

    [Fact]
    public async Task Receipt_detail_update_persists_exactly_one_existing_row()
    {
        var v_Fixture = m_objFixture!;
        var receiptId = await SaveReceiptHeaderAsync(v_Fixture, v_Fixture.WarehouseAId, v_Fixture.LoginAOnly, "M04-normal");
        var detailId = await SaveReceiptDetailAsync(v_Fixture, receiptId, 10m, v_Fixture.LoginAOnly);

        await SaveReceiptDetailAsync(v_Fixture, receiptId, 12m, v_Fixture.LoginAOnly, detailId);

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
        var v_Fixture = m_objFixture!;
        var receiptId = await SaveReceiptHeaderAsync(v_Fixture, v_Fixture.WarehouseAId, v_Fixture.LoginAOnly, "M04-race");
        var detailId = await SaveReceiptDetailAsync(v_Fixture, receiptId, 10m, v_Fixture.LoginAOnly);

        await using var v_DeleteConnection = OpenConnection($"M04-delete-{v_Fixture.Tag}");
        await v_DeleteConnection.OpenAsync();
        await using var v_DeleteTransaction = (SqlTransaction)await v_DeleteConnection.BeginTransactionAsync();
        await ExecuteAsync(
            v_DeleteConnection,
            v_DeleteTransaction,
            "DELETE dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @DetailId;",
            BigInt("@DetailId", detailId));

        await using var v_UpdateConnection = OpenConnection($"M04-update-{v_Fixture.Tag}");
        await v_UpdateConnection.OpenAsync();
        var v_iUpdateSessionId = await IntScalarAsync(v_UpdateConnection, null, "SELECT @@SPID;");
        var v_UpdateOutcomeTask = CaptureAsync(() => SaveReceiptDetailAsync(
            v_Fixture,
            receiptId,
            12m,
            v_Fixture.LoginAOnly,
            detailId,
            v_UpdateConnection));

        await WaitForSqlLockAsync(v_UpdateConnection, v_iUpdateSessionId, v_UpdateOutcomeTask);
        await v_DeleteTransaction.CommitAsync();
        var v_UpdateOutcome = await v_UpdateOutcomeTask;

        Assert.False(v_UpdateOutcome.Succeeded, FormatFailure("Receipt detail update", v_UpdateOutcome.Error));
        Assert.Equal(51109, (v_UpdateOutcome.Error as SqlException)?.Number);
        Assert.Equal(0, await IntScalarAsync(
            v_UpdateConnection,
            null,
            "SELECT COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var v_Tag = $"H01H02M04-{Guid.NewGuid():N}"[..24];
        var v_LoginAOnly = $"{v_Tag}-a";
        var v_LoginBOnly = $"{v_Tag}-b";
        var v_LoginBoth = $"{v_Tag}-both";

        await using var v_Connection = OpenConnection($"H01H02M04-setup-{v_Tag}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        try
        {
            var unitId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-unit", 200));
            var categoryId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{v_Tag}-category-code", 100),
                Text("@Name", $"{v_Tag}-category", 200));
            var productId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{v_Tag}-product-1", 100),
                Text("@Name", $"{v_Tag}-product-1", 255),
                BigInt("@CategoryId", categoryId),
                BigInt("@UnitId", unitId));
            var secondProductId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{v_Tag}-product-2", 100),
                Text("@Name", $"{v_Tag}-product-2", 255),
                BigInt("@CategoryId", categoryId),
                BigInt("@UnitId", unitId));
            var supplierId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{v_Tag}-supplier-code", 100),
                Text("@Name", $"{v_Tag}-supplier", 255));
            var warehouseAId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-a", 255));
            var warehouseBId = await InsertIdAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-b", 255));

            await InsertMemberAsync(v_Connection, v_Transaction, v_LoginAOnly, $"{v_Tag}-member-a");
            await InsertMemberAsync(v_Connection, v_Transaction, v_LoginBOnly, $"{v_Tag}-member-b");
            await InsertMemberAsync(v_Connection, v_Transaction, v_LoginBoth, $"{v_Tag}-member-both");
            await ExecuteAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@LoginA, @WarehouseA), (@LoginB, @WarehouseB), (@LoginBoth, @WarehouseA), (@LoginBoth, @WarehouseB);",
                Text("@LoginA", v_LoginAOnly, 100),
                Text("@LoginB", v_LoginBOnly, 100),
                Text("@LoginBoth", v_LoginBoth, 100),
                BigInt("@WarehouseA", warehouseAId),
                BigInt("@WarehouseB", warehouseBId));

            await v_Transaction.CommitAsync();
            return new Fixture(
                v_Tag,
                v_LoginAOnly,
                v_LoginBOnly,
                v_LoginBoth,
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
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long> SaveReceiptHeaderAsync(
        Fixture p_Fixture,
        long warehouseId,
        string p_Login,
        string p_Suffix,
        long autoId = 0,
        SqlConnection? p_Connection = null,
        SqlTransaction? p_Transaction = null)
    {
        var v_bOwnsConnection = p_Connection is null;
        p_Connection ??= OpenConnection($"H02-receipt-header-{p_Fixture.Tag}");
        if (v_bOwnsConnection)
            await p_Connection.OpenAsync();
        try
        {
            string v_strProcedure;
            if (autoId == 0)
            {
                v_strProcedure = "dbo.F2011_sp_ins_Nhap_Kho_Header";
            }
            else
            {
                v_strProcedure = "dbo.F2011_sp_upd_Nhap_Kho_Header";
            }

            return await ExecuteStoredWithOutputAsync(
                p_Connection,
                p_Transaction,
                v_strProcedure,
                BigInt("@Auto_ID", autoId),
                Text("@So_Phieu_Nhap_Kho", $"{p_Fixture.Tag}-{p_Suffix}-{Guid.NewGuid():N}", 100),
                BigInt("@Kho_ID", warehouseId),
                BigInt("@NCC_ID", p_Fixture.SupplierId),
                Date("@Ngay_Nhap_Kho", new DateTime(2026, 2, 1)),
                Text("@Ghi_Chu", "phase 2 test", 1000),
                Text("@Ma_Dang_Nhap", p_Login, 100));
        }
        finally
        {
            if (v_bOwnsConnection)
                await p_Connection.DisposeAsync();
        }
    }

    private static async Task<long> SaveIssueHeaderAsync(
        Fixture p_Fixture,
        long warehouseId,
        string p_Login,
        string p_Suffix,
        long autoId = 0,
        SqlConnection? p_Connection = null,
        SqlTransaction? p_Transaction = null)
    {
        var v_bOwnsConnection = p_Connection is null;
        p_Connection ??= OpenConnection($"H01H02-issue-header-{p_Fixture.Tag}");
        if (v_bOwnsConnection)
            await p_Connection.OpenAsync();
        try
        {
            string v_strProcedure;
            if (autoId == 0)
            {
                v_strProcedure = "dbo.F2012_sp_ins_Xuat_Kho_Header";
            }
            else
            {
                v_strProcedure = "dbo.F2012_sp_upd_Xuat_Kho_Header";
            }

            return await ExecuteStoredWithOutputAsync(
                p_Connection,
                p_Transaction,
                v_strProcedure,
                BigInt("@Auto_ID", autoId),
                Text("@So_Phieu_Xuat_Kho", $"{p_Fixture.Tag}-{p_Suffix}-{Guid.NewGuid():N}", 100),
                BigInt("@Kho_ID", warehouseId),
                Date("@Ngay_Xuat_Kho", new DateTime(2026, 2, 2)),
                Text("@Ghi_Chu", "phase 2 test", 1000),
                Text("@Ma_Dang_Nhap", p_Login, 100));
        }
        finally
        {
            if (v_bOwnsConnection)
                await p_Connection.DisposeAsync();
        }
    }

    private static async Task<long> SaveReceiptDetailAsync(
        Fixture p_Fixture,
        long receiptId,
        decimal p_Quantity,
        string p_Login,
        long detailId = 0,
        SqlConnection? p_Connection = null,
        SqlTransaction? p_Transaction = null)
    {
        var v_bOwnsConnection = p_Connection is null;
        p_Connection ??= OpenConnection($"M04-receipt-detail-{p_Fixture.Tag}");
        if (v_bOwnsConnection)
            await p_Connection.OpenAsync();
        try
        {
            string v_strProcedure;
            if (detailId == 0)
            {
                v_strProcedure = "dbo.F2011_sp_ins_Nhap_Kho_Detail";
            }
            else
            {
                v_strProcedure = "dbo.F2011_sp_upd_Nhap_Kho_Detail";
            }

            return await ExecuteStoredWithOutputAsync(
                p_Connection,
                p_Transaction,
                v_strProcedure,
                BigInt("@Auto_ID", detailId),
                BigInt("@Nhap_Kho_ID", receiptId),
                BigInt("@San_Pham_ID", p_Fixture.ProductId),
                Decimal("@SL_Nhap", p_Quantity),
                Decimal("@Don_Gia_Nhap", 1m),
                Text("@Ma_Dang_Nhap", p_Login, 100));
        }
        finally
        {
            if (v_bOwnsConnection)
                await p_Connection.DisposeAsync();
        }
    }

    private static async Task<long> SaveIssueDetailAsync(
        Fixture p_Fixture,
        long issueId,
        decimal p_Quantity,
        string p_Login,
        long detailId = 0,
        SqlConnection? p_Connection = null,
        SqlTransaction? p_Transaction = null)
    {
        var v_bOwnsConnection = p_Connection is null;
        p_Connection ??= OpenConnection($"H01H02-issue-detail-{p_Fixture.Tag}");
        if (v_bOwnsConnection)
            await p_Connection.OpenAsync();
        try
        {
            string v_strProcedure;
            if (detailId == 0)
            {
                v_strProcedure = "dbo.F2012_sp_ins_Xuat_Kho_Detail";
            }
            else
            {
                v_strProcedure = "dbo.F2012_sp_upd_Xuat_Kho_Detail";
            }

            return await ExecuteStoredWithOutputAsync(
                p_Connection,
                p_Transaction,
                v_strProcedure,
                BigInt("@Auto_ID", detailId),
                BigInt("@Xuat_Kho_ID", issueId),
                BigInt("@San_Pham_ID", p_Fixture.ProductId),
                Decimal("@SL_Xuat", p_Quantity),
                Decimal("@Don_Gia_Xuat", 1m),
                Text("@Ma_Dang_Nhap", p_Login, 100));
        }
        finally
        {
            if (v_bOwnsConnection)
                await p_Connection.DisposeAsync();
        }
    }

    private static async Task CreatePostedStockAsync(Fixture p_Fixture, decimal p_Quantity, string p_Suffix, long? warehouseId = null)
    {
        var receiptId = await SaveReceiptHeaderAsync(p_Fixture, warehouseId ?? p_Fixture.WarehouseAId, p_Fixture.LoginBoth, p_Suffix);
        await SaveReceiptDetailAsync(p_Fixture, receiptId, p_Quantity, p_Fixture.LoginBoth);
        await ExecuteStoredAsync(
            "dbo.sp_XNK_Document_Post",
            new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = true },
            BigInt("@Document_ID", receiptId),
            Text("@Ma_Dang_Nhap", p_Fixture.LoginBoth, 100));
    }

    private static Task PostIssueAsync(Fixture p_Fixture, long issueId, string p_Login)
    {
        return ExecuteStoredAsync("dbo.sp_XNK_Document_Post", new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = false }, BigInt("@Document_ID", issueId), Text("@Ma_Dang_Nhap", p_Login, 100));
    }

    private static Task DeleteIssueDetailAsync(Fixture p_Fixture, long detailId, string p_Login)
    {
        return ExecuteStoredAsync("dbo.F2012_sp_del_Xuat_Kho_Detail", BigInt("@Auto_ID", detailId), Text("@Ma_Dang_Nhap", p_Login, 100));
    }

    private static async Task<long> WarehouseOfReceiptAsync(Fixture p_Fixture, long receiptId)
    {
        return await Int64ScalarAsync(BaseConnectionString, null, "SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @DocumentId;", BigInt("@DocumentId", receiptId));
    }

    private static async Task<long> WarehouseOfIssueAsync(Fixture p_Fixture, long issueId)
    {
        return await Int64ScalarAsync(BaseConnectionString, null, "SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @DocumentId;", BigInt("@DocumentId", issueId));
    }

    private static async Task AssertIssueReservationAsync(
        Fixture p_Fixture,
        long issueId,
        long detailId,
        long expectedWarehouseId,
        decimal p_ExpectedAReserved,
        decimal p_ExpectedBReserved)
    {
        Assert.Equal(expectedWarehouseId, await WarehouseOfIssueAsync(p_Fixture, issueId));
        Assert.Equal(expectedWarehouseId, await Int64ScalarAsync(
            BaseConnectionString,
            null,
            "SELECT Kho_ID FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
        Assert.Equal(p_ExpectedAReserved, await DecimalScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COALESCE((SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId), 0);",
            BigInt("@WarehouseId", p_Fixture.WarehouseAId),
            BigInt("@ProductId", p_Fixture.ProductId)));
        Assert.Equal(p_ExpectedBReserved, await DecimalScalarAsync(
            BaseConnectionString,
            null,
            "SELECT COALESCE((SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId), 0);",
            BigInt("@WarehouseId", p_Fixture.WarehouseBId),
            BigInt("@ProductId", p_Fixture.ProductId)));
    }

    private static async Task WaitForSqlLockAsync(SqlConnection p_Connection, int p_iSessionId, Task<OperationOutcome> p_Operation)
    {
        await using var v_Monitor = OpenConnection("H01H02M04-monitor");
        await v_Monitor.OpenAsync();
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (p_Operation.IsCompleted)
                break;
            await using var v_Command = new SqlCommand(
                "SELECT TOP (1) wait_type, blocking_session_id FROM sys.dm_exec_requests WHERE session_id = @SessionId;",
                v_Monitor);
            v_Command.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = p_iSessionId });
            await using var v_Reader = await v_Command.ExecuteReaderAsync();
            if (await v_Reader.ReadAsync())
            {
                string? v_WaitType;
                if (v_Reader.IsDBNull(0))
                {
                    v_WaitType = null;
                }
                else
                {
                    v_WaitType = v_Reader.GetString(0);
                }

                int v_iBlockingSessionId;
                if (v_Reader.IsDBNull(1))
                {
                    v_iBlockingSessionId = 0;
                }
                else
                {
                    v_iBlockingSessionId = Convert.ToInt32(v_Reader.GetValue(1));
                }

                if (v_iBlockingSessionId > 0 && v_WaitType?.StartsWith("LCK_", StringComparison.OrdinalIgnoreCase) == true)
                    return;
            }
            await Task.Delay(50);
        }

        if (!p_Operation.IsCompleted)
            throw new Xunit.Sdk.XunitException("The concurrent operation did not reach a SQL lock wait.");
    }

    private static async Task<OperationOutcome> CaptureAsync(Func<Task> p_Operation)
    {
        try
        {
            await p_Operation();
            return new OperationOutcome(true, null);
        }
        catch (Exception v_Exception)
        {
            return new OperationOutcome(false, v_Exception);
        }
    }

    private static string FormatFailure(string p_Operation, Exception? p_Exception)
    {
        if (p_Exception is null)
        {
            return $"{p_Operation} unexpectedly succeeded.";
        }
        else
        {
            return $"{p_Operation} failed: {p_Exception.Message}";
        }
    }

    private static async Task AssertSqlNumberAsync(int p_iExpectedNumber, Func<Task> p_Operation)
    {
        var v_Exception = await Assert.ThrowsAsync<SqlException>(p_Operation);
        Assert.Equal(p_iExpectedNumber, v_Exception.Number);
    }

    private static async Task InsertMemberAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Login, string p_Name)
    {
        await ExecuteAsync(p_Connection, p_Transaction, "DECLARE @MemberId BIGINT = CONVERT(BIGINT, ABS(CHECKSUM(NEWID()))); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, Trang_Thai_ID, deleted) VALUES (@MemberId, @Login, @Name, 1, 0);", Text("@Login", p_Login, 100), Text("@Name", p_Name, 200));
    }

    private static async Task CleanupFixtureAsync(Fixture p_Fixture)
    {
        await using var v_Connection = OpenConnection($"H01H02M04-cleanup-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction, "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID IN (@WarehouseA, @WarehouseB) AND q.San_Pham_ID IN (@ProductId, @SecondProductId);", BigInt("@WarehouseA", p_Fixture.WarehouseAId), BigInt("@WarehouseB", p_Fixture.WarehouseBId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@SecondProductId", p_Fixture.SecondProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID IN (@WarehouseA, @WarehouseB) AND q.San_Pham_ID IN (@ProductId, @SecondProductId);", BigInt("@WarehouseA", p_Fixture.WarehouseAId), BigInt("@WarehouseB", p_Fixture.WarehouseBId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@SecondProductId", p_Fixture.SecondProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId);", BigInt("@WarehouseA", p_Fixture.WarehouseAId), BigInt("@WarehouseB", p_Fixture.WarehouseBId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@SecondProductId", p_Fixture.SecondProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId);", BigInt("@WarehouseA", p_Fixture.WarehouseAId), BigInt("@WarehouseB", p_Fixture.WarehouseBId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@SecondProductId", p_Fixture.SecondProductId));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE @TagPrefix; DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho LIKE @TagPrefix;", Text("@TagPrefix", $"{p_Fixture.Tag}%", 100));
            await ExecuteAsync(v_Connection, v_Transaction, "DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB, @LoginBoth); DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap IN (@LoginA, @LoginB, @LoginBoth); DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId; DELETE FROM dbo.tbl_DM_San_Pham WHERE Auto_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId; DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId;", Text("@LoginA", p_Fixture.LoginAOnly, 100), Text("@LoginB", p_Fixture.LoginBOnly, 100), Text("@LoginBoth", p_Fixture.LoginBoth, 100), BigInt("@WarehouseA", p_Fixture.WarehouseAId), BigInt("@WarehouseB", p_Fixture.WarehouseBId), BigInt("@SupplierId", p_Fixture.SupplierId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@SecondProductId", p_Fixture.SecondProductId), BigInt("@CategoryId", p_Fixture.CategoryId), BigInt("@UnitId", p_Fixture.UnitId));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static SqlConnection OpenConnection(string p_ApplicationName)
    {
        var v_Builder = new SqlConnectionStringBuilder(BaseConnectionString) { ApplicationName = p_ApplicationName };
        return new SqlConnection(v_Builder.ConnectionString);
    }

    private static async Task<long> InsertIdAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return Convert.ToInt64(await v_Command.ExecuteScalarAsync());
    }

    private static async Task<long> ExecuteStoredWithOutputAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        var v_Output = p_arrParameters.SingleOrDefault(parameter => parameter.ParameterName == "@Auto_ID") ?? BigInt("@Auto_ID", 0);
        v_Output.Direction = ParameterDirection.InputOutput;
        v_Command.Parameters.Add(v_Output);
        v_Command.Parameters.AddRange(p_arrParameters.Where(parameter => parameter != v_Output).ToArray());
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_Output.Value);
    }

    private static async Task ExecuteStoredAsync(string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Connection = OpenConnection($"H01H02M04-{p_Procedure}");
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand(p_Procedure, v_Connection) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
    }

    private static async Task<int> IntScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt32(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<int> IntScalarAsync(string p_ConnectionString, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Connection = OpenConnection("H01H02M04-int");
        await v_Connection.OpenAsync();
        return await IntScalarAsync(v_Connection, p_Transaction, p_Sql, p_arrParameters);
    }

    private static async Task<long> Int64ScalarAsync(string p_ConnectionString, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Connection = OpenConnection("H01H02M04-int64");
        await v_Connection.OpenAsync();
        return Convert.ToInt64(await ScalarAsync(v_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static async Task<decimal> DecimalScalarAsync(string p_ConnectionString, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Connection = OpenConnection("H01H02M04-decimal");
        await v_Connection.OpenAsync();
        return Convert.ToDecimal(await ScalarAsync(v_Connection, p_Transaction, p_Sql, p_arrParameters));
    }

    private static SqlParameter BigInt(string p_Name, long value)
    {
        return new(p_Name, SqlDbType.BigInt)
        {
            Value = value
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

    private static SqlParameter Date(string p_Name, DateTime p_dtmValue)
    {
        return new(p_Name, SqlDbType.Date)
        {
            Value = p_dtmValue.Date
        };
    }

    private static SqlParameter Text(string p_Name, string p_Value, int p_iSize)
    {
        return new(p_Name, SqlDbType.NVarChar, p_iSize)
        {
            Value = p_Value
        };
    }

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
