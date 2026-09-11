using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase3H03H08M06IntegrationTests : IAsyncLifetime
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
    public async Task Today_period_report_keeps_daily_closing_when_future_posted_document_changes_current()
    {
        var fixture = m_objFixture!;
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        await using var connection = OpenConnection($"P3-H03-today-{fixture.Tag}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        await SetManagedPostContextAsync(connection, transaction, true);
        await InsertPostedReceiptAsync(connection, transaction, fixture, today, 100m, "H03-today");
        await InsertPostedReceiptAsync(connection, transaction, fixture, tomorrow, 50m, "H03-future");
        await InsertCurrentAsync(connection, transaction, fixture.WarehouseAId, fixture.ProductId, 150m);
        await InsertMovementAsync(connection, transaction, fixture, today, 100m, 0m);
        await InsertMovementAsync(connection, transaction, fixture, tomorrow, 50m, 0m);
        await InsertBalanceAsync(connection, transaction, fixture, today, 0m, 100m, 0m, 100m, 100m, 0m);
        await InsertBalanceScopeAsync(connection, transaction, fixture, today, today);
        await ClearQueuesAsync(connection, transaction, fixture);
        await SetManagedPostContextAsync(connection, transaction, false);

        var report = await ReadInventoryReportAsync(connection, transaction, fixture, today, today);

        Assert.Equal(0m, report.Opening);
        Assert.Equal(100m, report.Received);
        Assert.Equal(0m, report.Issued);
        Assert.Equal(100m, report.Closing);
        Assert.Equal(report.Opening + report.Received - report.Issued, report.Closing);
    }

    [Fact]
    public async Task Period_report_uses_daily_carry_forward_for_opening_and_multi_day_arithmetic()
    {
        var fixture = m_objFixture!;
        var prior = new DateTime(2099, 3, 1);
        var from = prior.AddDays(1);
        var to = from.AddDays(1);

        await using var connection = OpenConnection($"P3-H03-period-{fixture.Tag}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        await SetManagedPostContextAsync(connection, transaction, true);
        await InsertPostedReceiptAsync(connection, transaction, fixture, prior, 100m, "H03-prior");
        await InsertPostedReceiptAsync(connection, transaction, fixture, from, 20m, "H03-receipt");
        await InsertPostedIssueAsync(connection, transaction, fixture, to, 10m, "H03-issue");
        await InsertCurrentAsync(connection, transaction, fixture.WarehouseAId, fixture.ProductId, 110m);
        await InsertMovementAsync(connection, transaction, fixture, prior, 100m, 0m);
        await InsertMovementAsync(connection, transaction, fixture, from, 20m, 0m);
        await InsertMovementAsync(connection, transaction, fixture, to, 0m, 10m);
        await InsertBalanceAsync(connection, transaction, fixture, prior, 0m, 100m, 0m, 100m, 100m, 0m);
        await InsertBalanceAsync(connection, transaction, fixture, from, 100m, 20m, 0m, 120m, 120m, 0m);
        await InsertBalanceAsync(connection, transaction, fixture, to, 120m, 0m, 10m, 110m, 120m, 10m);
        await InsertBalanceScopeAsync(connection, transaction, fixture, prior, to);
        await ClearQueuesAsync(connection, transaction, fixture);
        await SetManagedPostContextAsync(connection, transaction, false);

        var report = await ReadInventoryReportAsync(connection, transaction, fixture, from, to);

        Assert.Equal(100m, report.Opening);
        Assert.Equal(20m, report.Received);
        Assert.Equal(10m, report.Issued);
        Assert.Equal(110m, report.Closing);
        Assert.Equal(report.Opening + report.Received - report.Issued, report.Closing);
    }

    [Fact]
    public async Task Post_ignores_unrelated_negative_scope_for_valid_receipt()
    {
        var fixture = m_objFixture!;
        var badDate = new DateTime(2099, 1, 1);
        var goodDate = new DateTime(2099, 1, 2);

        await using (var setupConnection = OpenConnection($"P3-H08-bad-scope-{fixture.Tag}"))
        {
            await setupConnection.OpenAsync();
            await using var setupTransaction = (SqlTransaction)await setupConnection.BeginTransactionAsync();
            await SetManagedPostContextAsync(setupConnection, setupTransaction, true);
            await InsertPostedIssueAsync(setupConnection, setupTransaction, fixture, badDate, 10m, "H08-unrelated-bad");
            await SetManagedPostContextAsync(setupConnection, setupTransaction, false);
            await setupTransaction.CommitAsync();
        }

        var receiptId = await SaveReceiptHeaderAsync(fixture, fixture.WarehouseBId, goodDate, "H08-valid-receipt");
        await SaveReceiptDetailAsync(fixture, receiptId, fixture.ProductId, 100m);
        await PostDocumentAsync(fixture, receiptId, true);

        await using var verifyConnection = OpenConnection($"P3-H08-valid-verify-{fixture.Tag}");
        await verifyConnection.OpenAsync();
        Assert.Equal(1, await IntScalarAsync(
            verifyConnection,
            null,
            "SELECT Is_Posted FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @DocumentId;",
            BigInt("@DocumentId", receiptId)));
        Assert.Equal(100m, await DecimalScalarAsync(
            verifyConnection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", fixture.WarehouseBId),
            BigInt("@ProductId", fixture.ProductId)));
    }

    [Fact]
    public async Task Backdated_issue_rejects_when_affected_scope_suffix_goes_negative()
    {
        var fixture = m_objFixture!;
        await SeedHistoryAsync(fixture, 100m, 80m, 100m, productId: fixture.ProductId, currentQuantity: 120m, suffix: "H08-negative");

        var issueId = await SaveIssueHeaderAsync(fixture, new DateTime(2099, 1, 3), "H08-negative-issue");
        var detailId = await SaveIssueDetailAsync(fixture, issueId, fixture.ProductId, 30m);

        await AssertSqlNumberAsync(51120, () => PostDocumentAsync(fixture, issueId, false));

        await using var connection = OpenConnection($"P3-H08-negative-verify-{fixture.Tag}");
        await connection.OpenAsync();
        Assert.Equal(0, await IntScalarAsync(
            connection,
            null,
            "SELECT Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @DocumentId;",
            BigInt("@DocumentId", issueId)));
        Assert.Equal(120m, await DecimalScalarAsync(
            connection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", fixture.WarehouseAId),
            BigInt("@ProductId", fixture.ProductId)));
        Assert.Equal(30m, await DecimalScalarAsync(
            connection,
            null,
            "SELECT ReservedQuantity FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
    }

    [Fact]
    public async Task Valid_backdated_issue_succeeds_when_affected_scope_suffix_stays_non_negative()
    {
        var fixture = m_objFixture!;
        await SeedHistoryAsync(fixture, 100m, 20m, 100m, productId: fixture.ProductId, currentQuantity: 180m, suffix: "H08-valid");

        var issueId = await SaveIssueHeaderAsync(fixture, new DateTime(2099, 1, 3), "H08-valid-issue");
        await SaveIssueDetailAsync(fixture, issueId, fixture.ProductId, 30m);
        await PostDocumentAsync(fixture, issueId, false);

        await using var connection = OpenConnection($"P3-H08-valid-verify-{fixture.Tag}");
        await connection.OpenAsync();
        Assert.Equal(1, await IntScalarAsync(
            connection,
            null,
            "SELECT Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @DocumentId;",
            BigInt("@DocumentId", issueId)));
        Assert.Equal(150m, await DecimalScalarAsync(
            connection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", fixture.WarehouseAId),
            BigInt("@ProductId", fixture.ProductId)));
    }

    [Fact]
    public async Task Multi_product_backdated_issue_rolls_back_when_one_affected_scope_is_invalid()
    {
        var fixture = m_objFixture!;
        await SeedHistoryAsync(fixture, 100m, 20m, 100m, fixture.ProductId, 180m, "H08-multi-product-1");
        await SeedHistoryAsync(fixture, 10m, 10m, 100m, fixture.SecondProductId, 100m, "H08-multi-product-2");

        var issueId = await SaveIssueHeaderAsync(fixture, new DateTime(2099, 1, 3), "H08-multi-product-issue");
        var detailOne = await SaveIssueDetailAsync(fixture, issueId, fixture.ProductId, 30m);
        var detailTwo = await SaveIssueDetailAsync(fixture, issueId, fixture.SecondProductId, 20m);

        await AssertSqlNumberAsync(51120, () => PostDocumentAsync(fixture, issueId, false));

        await using var connection = OpenConnection($"P3-H08-multi-product-verify-{fixture.Tag}");
        await connection.OpenAsync();
        Assert.Equal(0, await IntScalarAsync(
            connection,
            null,
            "SELECT Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @DocumentId;",
            BigInt("@DocumentId", issueId)));
        Assert.Equal(180m, await DecimalScalarAsync(
            connection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", fixture.WarehouseAId),
            BigInt("@ProductId", fixture.ProductId)));
        Assert.Equal(100m, await DecimalScalarAsync(
            connection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", fixture.WarehouseAId),
            BigInt("@ProductId", fixture.SecondProductId)));
        Assert.Equal(30m, await DecimalScalarAsync(
            connection,
            null,
            "SELECT ReservedQuantity FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailOne)));
        Assert.Equal(20m, await DecimalScalarAsync(
            connection,
            null,
            "SELECT ReservedQuantity FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailTwo)));
    }

    [Fact]
    public async Task Document_post_definition_uses_affected_scope_validation_instead_of_global_validator()
    {
        await using var connection = OpenConnection($"P3-H08-definition-{m_objFixture!.Tag}");
        await connection.OpenAsync();
        var definition = Convert.ToString(await ScalarAsync(
            connection,
            null,
            "SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_XNK_Document_Post'));")) ?? "";

        Assert.DoesNotContain("sp_XNK_Validate_All_Balances", definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#AffectedScope", definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EarliestAffectedDate", definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MovementDate >=", definition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reconciliation_uses_historical_daily_closing_and_explicit_current_mode()
    {
        var fixture = m_objFixture!;
        var first = new DateTime(2099, 1, 1);
        var second = first.AddDays(1);
        var carryForward = first.AddDays(2);

        await using var connection = OpenConnection($"P3-M06-history-{fixture.Tag}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await SetManagedPostContextAsync(connection, transaction, true);
        await InsertPostedReceiptAsync(connection, transaction, fixture, first, 100m, "M06-receipt");
        await InsertPostedIssueAsync(connection, transaction, fixture, second, 30m, "M06-issue");
        await InsertCurrentAsync(connection, transaction, fixture.WarehouseAId, fixture.ProductId, 70m);
        await InsertMovementAsync(connection, transaction, fixture, first, 100m, 0m);
        await InsertMovementAsync(connection, transaction, fixture, second, 0m, 30m);
        await InsertBalanceAsync(connection, transaction, fixture, first, 0m, 100m, 0m, 100m, 100m, 0m);
        await InsertBalanceAsync(connection, transaction, fixture, second, 100m, 0m, 30m, 70m, 100m, 30m);
        await InsertBalanceScopeAsync(connection, transaction, fixture, first, second);
        await InsertSnapshotAsync(connection, transaction, fixture, first, 100m);
        await InsertSnapshotAsync(connection, transaction, fixture, second, 70m);
        await InsertSnapshotAsync(connection, transaction, fixture, carryForward, 70m);
        await ClearQueuesAsync(connection, transaction, fixture);
        await SetManagedPostContextAsync(connection, transaction, false);

        var firstRun = await RunReconciliationAsync(connection, transaction, fixture, first);
        var firstResults = await ReadReconciliationResultsAsync(connection, transaction, firstRun);
        Assert.Contains(firstResults, row => row.CheckType == "DAILY_CLOSING" && row.Status == "PASS" && row.Expected == 100m && row.Actual == 100m);
        Assert.DoesNotContain(firstResults, row => row.CheckType == "CURRENT_CLOSING" && row.Status == "FAIL");

        var carryRun = await RunReconciliationAsync(connection, transaction, fixture, carryForward);
        var carryResults = await ReadReconciliationResultsAsync(connection, transaction, carryRun);
        Assert.Contains(carryResults, row => row.CheckType == "DAILY_CLOSING" && row.Status == "PASS" && row.Expected == 70m && row.Actual == 70m);

        var currentRun = await RunReconciliationAsync(connection, transaction, fixture, null);
        var currentResults = await ReadReconciliationResultsAsync(connection, transaction, currentRun);
        Assert.Contains(currentResults, row => row.CheckType == "CURRENT_CLOSING" && row.Status == "PASS" && row.Expected == 70m && row.Actual == 70m);
    }

    [Fact]
    public async Task Reconciliation_rejects_historical_cutoff_without_ready_daily_projection()
    {
        var fixture = m_objFixture!;
        var movementDate = new DateTime(2099, 2, 1);
        await using var setupConnection = OpenConnection($"P3-M06-not-ready-setup-{fixture.Tag}");
        await setupConnection.OpenAsync();
        await using (var setupTransaction = (SqlTransaction)await setupConnection.BeginTransactionAsync())
        {
            await SetManagedPostContextAsync(setupConnection, setupTransaction, true);
            await InsertPostedReceiptAsync(setupConnection, setupTransaction, fixture, movementDate, 100m, "M06-not-ready");
            await InsertCurrentAsync(setupConnection, setupTransaction, fixture.WarehouseAId, fixture.ProductId, 100m);
            await SetManagedPostContextAsync(setupConnection, setupTransaction, false);
            await setupTransaction.CommitAsync();
        }

        await using var connection = OpenConnection($"P3-M06-not-ready-call-{fixture.Tag}");
        await connection.OpenAsync();
        await AssertSqlNumberAsync(51332, () => RunReconciliationAsync(connection, null, fixture, new DateTime(2099, 1, 1)));
    }

    [Fact]
    public async Task Reconciliation_accepts_zero_history_before_initialized_scope_first_date()
    {
        var fixture = m_objFixture!;
        var firstBalanceDate = new DateTime(2099, 4, 10);
        var cutoff = firstBalanceDate.AddDays(-1);

        await using (var setupConnection = OpenConnection($"P3-M06-zero-history-setup-{fixture.Tag}"))
        {
            await setupConnection.OpenAsync();
            await using var setupTransaction = (SqlTransaction)await setupConnection.BeginTransactionAsync();
            await ExecuteAsync(
                setupConnection,
                setupTransaction,
                "UPDATE dbo.InventoryBalance_Daily_AggregateState SET IsInitialized = 1, InitializedAt = COALESCE(InitializedAt, SYSUTCDATETIME()) WHERE State_ID = 1;");
            await InsertCurrentAsync(setupConnection, setupTransaction, fixture.WarehouseAId, fixture.ProductId, 100m);
            await InsertBalanceScopeAsync(setupConnection, setupTransaction, fixture, firstBalanceDate, firstBalanceDate);
            await setupTransaction.CommitAsync();
        }

        await using var connection = OpenConnection($"P3-M06-zero-history-{fixture.Tag}");
        await connection.OpenAsync();
        var runId = await RunReconciliationAsync(connection, null, fixture, cutoff);
        await using var readTransaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var results = await ReadReconciliationResultsAsync(connection, readTransaction, runId);

        Assert.Contains(results, row => row.CheckType == "DAILY_CLOSING" && row.Status == "PASS" && row.Expected == 0m && row.Actual == 0m);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var tag = $"P3-{Guid.NewGuid():N}"[..18];
        var login = $"{tag}-login";
        await using var connection = OpenConnection($"P3-setup-{tag}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var unitId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-unit", 200));
            var categoryId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{tag}-category-code", 100), Text("@Name", $"{tag}-category", 200));
            var productId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{tag}-product-1", 100), Text("@Name", $"{tag}-product-1", 255), BigInt("@CategoryId", categoryId), BigInt("@UnitId", unitId));
            var secondProductId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{tag}-product-2", 100), Text("@Name", $"{tag}-product-2", 255), BigInt("@CategoryId", categoryId), BigInt("@UnitId", unitId));
            var supplierId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{tag}-supplier-code", 100), Text("@Name", $"{tag}-supplier", 255));
            var warehouseAId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-a", 255));
            var warehouseBId = await InsertIdAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{tag}-warehouse-b", 255));
            var memberId = await LongScalarAsync(connection, transaction, "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, Trang_Thai_ID, deleted) VALUES (@MemberId, @Login, @Name, 1, 0);",
                BigInt("@MemberId", memberId), Text("@Login", login, 100), Text("@Name", $"{tag}-member", 200));
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseA), (@Login, @WarehouseB);",
                Text("@Login", login, 100), BigInt("@WarehouseA", warehouseAId), BigInt("@WarehouseB", warehouseBId));
            await transaction.CommitAsync();
            return new Fixture(tag, login, unitId, categoryId, productId, secondProductId, supplierId, warehouseAId, warehouseBId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task SeedHistoryAsync(Fixture fixture, decimal receiptAtFirstDate, decimal issueAtSecondDate, decimal receiptAtThirdDate, long productId, decimal currentQuantity, string suffix)
    {
        await using var connection = OpenConnection($"P3-H08-history-{fixture.Tag}-{suffix}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await SetManagedPostContextAsync(connection, transaction, true);
        await InsertPostedReceiptAsync(connection, transaction, fixture, new DateTime(2099, 1, 1), receiptAtFirstDate, $"{suffix}-receipt-1", productId);
        await InsertPostedIssueAsync(connection, transaction, fixture, new DateTime(2099, 1, 5), issueAtSecondDate, $"{suffix}-issue-2", productId);
        await InsertPostedReceiptAsync(connection, transaction, fixture, new DateTime(2099, 1, 10), receiptAtThirdDate, $"{suffix}-receipt-3", productId);
        await InsertCurrentAsync(connection, transaction, fixture.WarehouseAId, productId, currentQuantity);
        await SetManagedPostContextAsync(connection, transaction, false);
        await transaction.CommitAsync();
    }

    private static async Task<long> SaveReceiptHeaderAsync(Fixture fixture, long warehouseId, DateTime date, string suffix)
    {
        await using var connection = OpenConnection($"P3-save-receipt-header-{fixture.Tag}");
        await connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(connection, null, "dbo.sp_XNK_Nhap_Kho_Save_Header",
            BigInt("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{fixture.Tag}-{suffix}-{Guid.NewGuid():N}", 100),
            BigInt("@Kho_ID", warehouseId), BigInt("@NCC_ID", fixture.SupplierId), Date("@Ngay_Nhap_Kho", date),
            Text("@Ghi_Chu", "phase 3 test", 1000), Text("@Ma_Dang_Nhap", fixture.Login, 100));
    }

    private static async Task<long> SaveReceiptDetailAsync(Fixture fixture, long receiptId, long productId, decimal quantity)
    {
        await using var connection = OpenConnection($"P3-save-receipt-detail-{fixture.Tag}");
        await connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(connection, null, "dbo.sp_XNK_Nhap_Kho_Save_Detail",
            BigInt("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", productId),
            Decimal("@SL_Nhap", quantity), Decimal("@Don_Gia_Nhap", 1m), Text("@Ma_Dang_Nhap", fixture.Login, 100));
    }

    private static async Task<long> SaveIssueHeaderAsync(Fixture fixture, DateTime date, string suffix)
    {
        await using var connection = OpenConnection($"P3-save-issue-header-{fixture.Tag}");
        await connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(connection, null, "dbo.sp_XNK_Xuat_Kho_Save_Header",
            BigInt("@Auto_ID", 0), Text("@So_Phieu_Xuat_Kho", $"{fixture.Tag}-{suffix}-{Guid.NewGuid():N}", 100),
            BigInt("@Kho_ID", fixture.WarehouseAId), Date("@Ngay_Xuat_Kho", date),
            Text("@Ghi_Chu", "phase 3 test", 1000), Text("@Ma_Dang_Nhap", fixture.Login, 100));
    }

    private static async Task<long> SaveIssueDetailAsync(Fixture fixture, long issueId, long productId, decimal quantity)
    {
        await using var connection = OpenConnection($"P3-save-issue-detail-{fixture.Tag}");
        await connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(connection, null, "dbo.sp_XNK_Xuat_Kho_Save_Detail",
            BigInt("@Auto_ID", 0), BigInt("@Xuat_Kho_ID", issueId), BigInt("@San_Pham_ID", productId),
            Decimal("@SL_Xuat", quantity), Decimal("@Don_Gia_Xuat", 1m), Text("@Ma_Dang_Nhap", fixture.Login, 100));
    }

    private static Task PostDocumentAsync(Fixture fixture, long documentId, bool isReceipt) => ExecuteStoredAsync(
        "dbo.sp_XNK_Document_Post",
        new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = isReceipt },
        BigInt("@Document_ID", documentId),
        Text("@Ma_Dang_Nhap", fixture.Login, 100));

    private static async Task InsertPostedReceiptAsync(SqlConnection connection, SqlTransaction transaction, Fixture fixture, DateTime date, decimal quantity, string suffix, long? productId = null)
    {
        var documentId = await InsertIdAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N'phase 3 fixture'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{fixture.Tag}-{suffix}-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", fixture.WarehouseAId),
            BigInt("@SupplierId", fixture.SupplierId), Date("@Date", date));
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", productId ?? fixture.ProductId), Decimal("@Quantity", quantity));
    }

    private static async Task InsertPostedIssueAsync(SqlConnection connection, SqlTransaction transaction, Fixture fixture, DateTime date, decimal quantity, string suffix, long? productId = null)
    {
        var documentId = await InsertIdAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @Date, 1, N'phase 3 fixture'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{fixture.Tag}-{suffix}-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", fixture.WarehouseAId), Date("@Date", date));
        await ExecuteAsync(connection, transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", productId ?? fixture.ProductId), Decimal("@Quantity", quantity));
    }

    private static Task InsertCurrentAsync(SqlConnection connection, SqlTransaction transaction, long warehouseId, long productId, decimal quantity) => ExecuteAsync(
        connection, transaction,
        "IF EXISTS (SELECT 1 FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId) UPDATE dbo.InventoryBalance_Current SET CurrentQuantity = @Quantity, ReservedQuantity = 0 WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; ELSE INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, @Quantity, 0);",
        BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Decimal("@Quantity", quantity));

    private static Task InsertMovementAsync(SqlConnection connection, SqlTransaction transaction, Fixture fixture, DateTime date, decimal received, decimal issued, long? productId = null) => ExecuteAsync(
        connection, transaction,
        "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseId, @ProductId, @Received, @Issued, 1);",
        Date("@Date", date), BigInt("@WarehouseId", fixture.WarehouseAId), BigInt("@ProductId", productId ?? fixture.ProductId), Decimal("@Received", received), Decimal("@Issued", issued));

    private static Task InsertBalanceAsync(SqlConnection connection, SqlTransaction transaction, Fixture fixture, DateTime date, decimal opening, decimal received, decimal issued, decimal closing, decimal cumulativeReceived, decimal cumulativeIssued, long? productId = null) => ExecuteAsync(
        connection, transaction,
        "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, @Opening, @Received, @Issued, @Closing, @CumulativeReceived, @CumulativeIssued, 1);",
        Date("@Date", date), BigInt("@WarehouseId", fixture.WarehouseAId), BigInt("@ProductId", productId ?? fixture.ProductId), Decimal("@Opening", opening), Decimal("@Received", received), Decimal("@Issued", issued), Decimal("@Closing", closing), Decimal("@CumulativeReceived", cumulativeReceived), Decimal("@CumulativeIssued", cumulativeIssued));

    private static Task InsertBalanceScopeAsync(SqlConnection connection, SqlTransaction transaction, Fixture fixture, DateTime firstDate, DateTime lastDate, long? productId = null) => ExecuteAsync(
        connection, transaction,
        "INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseId, @ProductId, @FirstDate, @LastDate);",
        BigInt("@WarehouseId", fixture.WarehouseAId), BigInt("@ProductId", productId ?? fixture.ProductId), Date("@FirstDate", firstDate), Date("@LastDate", lastDate));

    private static Task InsertSnapshotAsync(SqlConnection connection, SqlTransaction transaction, Fixture fixture, DateTime date, decimal closing, long? productId = null) => ExecuteAsync(
        connection, transaction,
        "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, @Closing, 1, 1);",
        Date("@Date", date), BigInt("@WarehouseId", fixture.WarehouseAId), BigInt("@ProductId", productId ?? fixture.ProductId), Decimal("@Closing", closing));

    private static async Task ClearQueuesAsync(SqlConnection connection, SqlTransaction transaction, Fixture fixture)
    {
        await ExecuteAsync(connection, transaction,
            "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID IN (@WarehouseA, @WarehouseB) AND q.San_Pham_ID IN (@ProductId, @SecondProductId); DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID IN (@WarehouseA, @WarehouseB) AND q.San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId);",
            BigInt("@WarehouseA", fixture.WarehouseAId), BigInt("@WarehouseB", fixture.WarehouseBId), BigInt("@ProductId", fixture.ProductId), BigInt("@SecondProductId", fixture.SecondProductId));
    }

    private static async Task<InventoryReportRow> ReadInventoryReportAsync(SqlConnection connection, SqlTransaction transaction, Fixture fixture, DateTime from, DateTime to)
    {
        await using var command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton_Page", connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(Date("@Tu_Ngay", from));
        command.Parameters.Add(Date("@Den_Ngay", to));
        command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        command.Parameters.Add(Text("@Ma_Dang_Nhap", fixture.Login, 100));
        command.Parameters.Add(BigInt("@Kho_ID", fixture.WarehouseAId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        return new InventoryReportRow(reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8));
    }

    private static async Task<long> RunReconciliationAsync(SqlConnection connection, SqlTransaction? transaction, Fixture fixture, DateTime? asOfDate)
    {
        await using var command = new SqlCommand("dbo.sp_Inventory_Reconciliation_Run", connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(new SqlParameter("@As_Of_Date", SqlDbType.Date) { Value = asOfDate.HasValue ? asOfDate.Value.Date : DBNull.Value });
        command.Parameters.Add(BigInt("@Kho_ID", fixture.WarehouseAId));
        command.Parameters.Add(BigInt("@San_Pham_ID", fixture.ProductId));
        var runId = new SqlParameter("@Run_ID", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
        command.Parameters.Add(runId);
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(runId.Value);
    }

    private static async Task<List<ReconciliationRow>> ReadReconciliationResultsAsync(SqlConnection connection, SqlTransaction transaction, long runId)
    {
        await using var command = new SqlCommand(
            "SELECT Check_Type, ExpectedQuantity, ActualQuantity, Difference, Status FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId;",
            connection,
            transaction);
        command.Parameters.Add(BigInt("@RunId", runId));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ReconciliationRow>();
        while (await reader.ReadAsync())
            rows.Add(new ReconciliationRow(reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetString(4)));
        return rows;
    }

    private static async Task SetManagedPostContextAsync(SqlConnection connection, SqlTransaction? transaction, bool enabled) =>
        await ExecuteAsync(connection, transaction,
            "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = @Value;",
            new SqlParameter("@Value", SqlDbType.Bit) { Value = enabled ? 1 : DBNull.Value });

    private static async Task CleanupFixtureAsync(Fixture fixture)
    {
        await using var connection = OpenConnection($"P3-cleanup-{fixture.Tag}");
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await SetManagedPostContextAsync(connection, transaction, true);
            await ExecuteAsync(connection, transaction,
                "DELETE r FROM dbo.InventoryReconciliation_Result r JOIN dbo.InventoryReconciliation_Run run ON run.ID = r.Run_ID WHERE run.Kho_ID IN (@WarehouseA, @WarehouseB) AND (run.San_Pham_ID = @ProductId OR run.San_Pham_ID = @SecondProductId); DELETE FROM dbo.InventoryReconciliation_Run WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE d FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE h.Kho_ID IN (@WarehouseA, @WarehouseB); DELETE d FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Kho_ID IN (@WarehouseA, @WarehouseB);",
                BigInt("@WarehouseA", fixture.WarehouseAId), BigInt("@WarehouseB", fixture.WarehouseBId), BigInt("@ProductId", fixture.ProductId), BigInt("@SecondProductId", fixture.SecondProductId));
            await ClearQueuesAsync(connection, transaction, fixture);
            await ExecuteAsync(connection, transaction,
                "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId; DELETE FROM dbo.tbl_DM_San_Pham WHERE Auto_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId; DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId;",
                BigInt("@WarehouseA", fixture.WarehouseAId), BigInt("@WarehouseB", fixture.WarehouseBId), BigInt("@ProductId", fixture.ProductId), BigInt("@SecondProductId", fixture.SecondProductId), Text("@Login", fixture.Login, 100), BigInt("@SupplierId", fixture.SupplierId), BigInt("@CategoryId", fixture.CategoryId), BigInt("@UnitId", fixture.UnitId));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long> ExecuteStoredWithOutputAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure };
        var output = parameters.Single(parameter => parameter.ParameterName == "@Auto_ID");
        output.Direction = ParameterDirection.InputOutput;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(output.Value);
    }

    private static async Task ExecuteStoredAsync(string procedure, params SqlParameter[] parameters)
    {
        await using var connection = OpenConnection($"P3-{procedure}");
        await connection.OpenAsync();
        await using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSqlNumberAsync(int expectedNumber, Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(operation);
        Assert.Equal(expectedNumber, exception.Number);
    }

    private static async Task<long> InsertIdAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> LongScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) =>
        Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<int> IntScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) =>
        Convert.ToInt32(await ScalarAsync(connection, transaction, sql, parameters));

    private static async Task<decimal> DecimalScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) =>
        Convert.ToDecimal(await ScalarAsync(connection, transaction, sql, parameters));

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

    private static SqlConnection OpenConnection(string applicationName)
    {
        var builder = new SqlConnectionStringBuilder(BaseConnectionString) { ApplicationName = applicationName };
        return new SqlConnection(builder.ConnectionString);
    }

    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };
    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };

    private sealed record Fixture(string Tag, string Login, long UnitId, long CategoryId, long ProductId, long SecondProductId, long SupplierId, long WarehouseAId, long WarehouseBId);
    private sealed record InventoryReportRow(decimal Opening, decimal Received, decimal Issued, decimal Closing);
    private sealed record ReconciliationRow(string CheckType, decimal Expected, decimal Actual, decimal Difference, string Status);
}
