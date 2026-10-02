using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase3H03H08M06IntegrationTests : IAsyncLifetime
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
    public async Task Today_period_report_keeps_daily_closing_when_future_posted_document_changes_current()
    {
        var v_Fixture = m_objFixture!;
        var v_dtmToday = DateTime.Today;
        var v_dtmTomorrow = v_dtmToday.AddDays(1);

        await using var v_Connection = OpenConnection($"P3-H03-today-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();

        await SetManagedPostContextAsync(v_Connection, v_Transaction, true);
        await InsertPostedReceiptAsync(v_Connection, v_Transaction, v_Fixture, v_dtmToday, 100m, "H03-today");
        await InsertPostedReceiptAsync(v_Connection, v_Transaction, v_Fixture, v_dtmTomorrow, 50m, "H03-future");
        await InsertCurrentAsync(v_Connection, v_Transaction, v_Fixture.WarehouseAId, v_Fixture.ProductId, 150m);
        await InsertMovementAsync(v_Connection, v_Transaction, v_Fixture, v_dtmToday, 100m, 0m);
        await InsertMovementAsync(v_Connection, v_Transaction, v_Fixture, v_dtmTomorrow, 50m, 0m);
        await InsertBalanceAsync(v_Connection, v_Transaction, v_Fixture, v_dtmToday, 0m, 100m, 0m, 100m, 100m, 0m);
        await InsertBalanceScopeAsync(v_Connection, v_Transaction, v_Fixture, v_dtmToday, v_dtmToday);
        await ClearQueuesAsync(v_Connection, v_Transaction, v_Fixture);
        await SetManagedPostContextAsync(v_Connection, v_Transaction, false);

        var v_Report = await ReadInventoryReportAsync(v_Connection, v_Transaction, v_Fixture, v_dtmToday, v_dtmToday);

        Assert.Equal(0m, v_Report.Opening);
        Assert.Equal(100m, v_Report.Received);
        Assert.Equal(0m, v_Report.Issued);
        Assert.Equal(100m, v_Report.Closing);
        Assert.Equal(v_Report.Opening + v_Report.Received - v_Report.Issued, v_Report.Closing);
    }

    [Fact]
    public async Task Period_report_uses_daily_carry_forward_for_opening_and_multi_day_arithmetic()
    {
        var v_Fixture = m_objFixture!;
        var v_dtmPrior = new DateTime(2099, 3, 1);
        var v_dtmFrom = v_dtmPrior.AddDays(1);
        var v_dtmTo = v_dtmFrom.AddDays(1);

        await using var v_Connection = OpenConnection($"P3-H03-period-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();

        await SetManagedPostContextAsync(v_Connection, v_Transaction, true);
        await InsertPostedReceiptAsync(v_Connection, v_Transaction, v_Fixture, v_dtmPrior, 100m, "H03-prior");
        await InsertPostedReceiptAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFrom, 20m, "H03-receipt");
        await InsertPostedIssueAsync(v_Connection, v_Transaction, v_Fixture, v_dtmTo, 10m, "H03-issue");
        await InsertCurrentAsync(v_Connection, v_Transaction, v_Fixture.WarehouseAId, v_Fixture.ProductId, 110m);
        await InsertMovementAsync(v_Connection, v_Transaction, v_Fixture, v_dtmPrior, 100m, 0m);
        await InsertMovementAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFrom, 20m, 0m);
        await InsertMovementAsync(v_Connection, v_Transaction, v_Fixture, v_dtmTo, 0m, 10m);
        await InsertBalanceAsync(v_Connection, v_Transaction, v_Fixture, v_dtmPrior, 0m, 100m, 0m, 100m, 100m, 0m);
        await InsertBalanceAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFrom, 100m, 20m, 0m, 120m, 120m, 0m);
        await InsertBalanceAsync(v_Connection, v_Transaction, v_Fixture, v_dtmTo, 120m, 0m, 10m, 110m, 120m, 10m);
        await InsertBalanceScopeAsync(v_Connection, v_Transaction, v_Fixture, v_dtmPrior, v_dtmTo);
        await ClearQueuesAsync(v_Connection, v_Transaction, v_Fixture);
        await SetManagedPostContextAsync(v_Connection, v_Transaction, false);

        var v_Report = await ReadInventoryReportAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFrom, v_dtmTo);

        Assert.Equal(100m, v_Report.Opening);
        Assert.Equal(20m, v_Report.Received);
        Assert.Equal(10m, v_Report.Issued);
        Assert.Equal(110m, v_Report.Closing);
        Assert.Equal(v_Report.Opening + v_Report.Received - v_Report.Issued, v_Report.Closing);
    }

    [Fact]
    public async Task Post_ignores_unrelated_negative_scope_for_valid_receipt()
    {
        var v_Fixture = m_objFixture!;
        var v_dtmBadDate = new DateTime(2099, 1, 1);
        var v_dtmGoodDate = new DateTime(2099, 1, 2);

        await using (var v_SetupConnection = OpenConnection($"P3-H08-bad-scope-{v_Fixture.Tag}"))
        {
            await v_SetupConnection.OpenAsync();
            await using var v_SetupTransaction = (SqlTransaction)await v_SetupConnection.BeginTransactionAsync();
            await SetManagedPostContextAsync(v_SetupConnection, v_SetupTransaction, true);
            await InsertPostedIssueAsync(v_SetupConnection, v_SetupTransaction, v_Fixture, v_dtmBadDate, 10m, "H08-unrelated-bad");
            await SetManagedPostContextAsync(v_SetupConnection, v_SetupTransaction, false);
            await v_SetupTransaction.CommitAsync();
        }

        var receiptId = await SaveReceiptHeaderAsync(v_Fixture, v_Fixture.WarehouseBId, v_dtmGoodDate, "H08-valid-receipt");
        await SaveReceiptDetailAsync(v_Fixture, receiptId, v_Fixture.ProductId, 100m);
        await PostDocumentAsync(v_Fixture, receiptId, true);

        await using var v_VerifyConnection = OpenConnection($"P3-H08-valid-verify-{v_Fixture.Tag}");
        await v_VerifyConnection.OpenAsync();
        Assert.Equal(1, await IntScalarAsync(
            v_VerifyConnection,
            null,
            "SELECT Is_Posted FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @DocumentId;",
            BigInt("@DocumentId", receiptId)));
        Assert.Equal(100m, await DecimalScalarAsync(
            v_VerifyConnection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", v_Fixture.WarehouseBId),
            BigInt("@ProductId", v_Fixture.ProductId)));
    }

    [Fact]
    public async Task Backdated_issue_rejects_when_affected_scope_suffix_goes_negative()
    {
        var v_Fixture = m_objFixture!;
        await SeedHistoryAsync(v_Fixture, 100m, 80m, 100m, productId: v_Fixture.ProductId, p_CurrentQuantity: 120m, p_Suffix: "H08-negative");

        var issueId = await SaveIssueHeaderAsync(v_Fixture, new DateTime(2099, 1, 3), "H08-negative-issue");
        var detailId = await SaveIssueDetailAsync(v_Fixture, issueId, v_Fixture.ProductId, 30m);

        await AssertSqlNumberAsync(51120, () => PostDocumentAsync(v_Fixture, issueId, false));

        await using var v_Connection = OpenConnection($"P3-H08-negative-verify-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        Assert.Equal(0, await IntScalarAsync(
            v_Connection,
            null,
            "SELECT Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @DocumentId;",
            BigInt("@DocumentId", issueId)));
        Assert.Equal(120m, await DecimalScalarAsync(
            v_Connection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", v_Fixture.WarehouseAId),
            BigInt("@ProductId", v_Fixture.ProductId)));
        Assert.Equal(30m, await DecimalScalarAsync(
            v_Connection,
            null,
            "SELECT ReservedQuantity FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailId)));
    }

    [Fact]
    public async Task Valid_backdated_issue_succeeds_when_affected_scope_suffix_stays_non_negative()
    {
        var v_Fixture = m_objFixture!;
        await SeedHistoryAsync(v_Fixture, 100m, 20m, 100m, productId: v_Fixture.ProductId, p_CurrentQuantity: 180m, p_Suffix: "H08-valid");

        var issueId = await SaveIssueHeaderAsync(v_Fixture, new DateTime(2099, 1, 3), "H08-valid-issue");
        await SaveIssueDetailAsync(v_Fixture, issueId, v_Fixture.ProductId, 30m);
        await PostDocumentAsync(v_Fixture, issueId, false);

        await using var v_Connection = OpenConnection($"P3-H08-valid-verify-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        Assert.Equal(1, await IntScalarAsync(
            v_Connection,
            null,
            "SELECT Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @DocumentId;",
            BigInt("@DocumentId", issueId)));
        Assert.Equal(150m, await DecimalScalarAsync(
            v_Connection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", v_Fixture.WarehouseAId),
            BigInt("@ProductId", v_Fixture.ProductId)));
    }

    [Fact]
    public async Task Multi_product_backdated_issue_rolls_back_when_one_affected_scope_is_invalid()
    {
        var v_Fixture = m_objFixture!;
        await SeedHistoryAsync(v_Fixture, 100m, 20m, 100m, v_Fixture.ProductId, 180m, "H08-multi-product-1");
        await SeedHistoryAsync(v_Fixture, 10m, 10m, 100m, v_Fixture.SecondProductId, 100m, "H08-multi-product-2");

        var issueId = await SaveIssueHeaderAsync(v_Fixture, new DateTime(2099, 1, 3), "H08-multi-product-issue");
        var detailOne = await SaveIssueDetailAsync(v_Fixture, issueId, v_Fixture.ProductId, 30m);
        var detailTwo = await SaveIssueDetailAsync(v_Fixture, issueId, v_Fixture.SecondProductId, 20m);

        await AssertSqlNumberAsync(51120, () => PostDocumentAsync(v_Fixture, issueId, false));

        await using var v_Connection = OpenConnection($"P3-H08-multi-product-verify-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        Assert.Equal(0, await IntScalarAsync(
            v_Connection,
            null,
            "SELECT Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @DocumentId;",
            BigInt("@DocumentId", issueId)));
        Assert.Equal(180m, await DecimalScalarAsync(
            v_Connection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", v_Fixture.WarehouseAId),
            BigInt("@ProductId", v_Fixture.ProductId)));
        Assert.Equal(100m, await DecimalScalarAsync(
            v_Connection,
            null,
            "SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
            BigInt("@WarehouseId", v_Fixture.WarehouseAId),
            BigInt("@ProductId", v_Fixture.SecondProductId)));
        Assert.Equal(30m, await DecimalScalarAsync(
            v_Connection,
            null,
            "SELECT ReservedQuantity FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailOne)));
        Assert.Equal(20m, await DecimalScalarAsync(
            v_Connection,
            null,
            "SELECT ReservedQuantity FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @DetailId;",
            BigInt("@DetailId", detailTwo)));
    }

    [Fact]
    public async Task Document_post_definition_uses_affected_scope_validation_instead_of_global_validator()
    {
        await using var v_Connection = OpenConnection($"P3-H08-definition-{m_objFixture!.Tag}");
        await v_Connection.OpenAsync();
        var v_Definition = Convert.ToString(await ScalarAsync(
            v_Connection,
            null,
            "SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_XNK_Document_Post'));")) ?? "";

        Assert.DoesNotContain("sp_XNK_Validate_All_Balances", v_Definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#AffectedScope", v_Definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EarliestAffectedDate", v_Definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MovementDate >=", v_Definition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reconciliation_uses_historical_daily_closing_and_explicit_current_mode()
    {
        var v_Fixture = m_objFixture!;
        var v_dtmFirst = new DateTime(2099, 1, 1);
        var v_dtmSecond = v_dtmFirst.AddDays(1);
        var v_dtmCarryForward = v_dtmFirst.AddDays(2);

        await using var v_Connection = OpenConnection($"P3-M06-history-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        await SetManagedPostContextAsync(v_Connection, v_Transaction, true);
        await InsertPostedReceiptAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFirst, 100m, "M06-receipt");
        await InsertPostedIssueAsync(v_Connection, v_Transaction, v_Fixture, v_dtmSecond, 30m, "M06-issue");
        await InsertCurrentAsync(v_Connection, v_Transaction, v_Fixture.WarehouseAId, v_Fixture.ProductId, 70m);
        await InsertMovementAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFirst, 100m, 0m);
        await InsertMovementAsync(v_Connection, v_Transaction, v_Fixture, v_dtmSecond, 0m, 30m);
        await InsertBalanceAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFirst, 0m, 100m, 0m, 100m, 100m, 0m);
        await InsertBalanceAsync(v_Connection, v_Transaction, v_Fixture, v_dtmSecond, 100m, 0m, 30m, 70m, 100m, 30m);
        await InsertBalanceScopeAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFirst, v_dtmSecond);
        await InsertSnapshotAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFirst, 100m);
        await InsertSnapshotAsync(v_Connection, v_Transaction, v_Fixture, v_dtmSecond, 70m);
        await InsertSnapshotAsync(v_Connection, v_Transaction, v_Fixture, v_dtmCarryForward, 70m);
        await ClearQueuesAsync(v_Connection, v_Transaction, v_Fixture);
        await SetManagedPostContextAsync(v_Connection, v_Transaction, false);

        var firstRun = await RunReconciliationAsync(v_Connection, v_Transaction, v_Fixture, v_dtmFirst);
        var v_arrFirstResults = await ReadReconciliationResultsAsync(v_Connection, v_Transaction, firstRun);
        Assert.Contains(v_arrFirstResults, row => row.CheckType == "DAILY_CLOSING" && row.Status == "PASS" && row.Expected == 100m && row.Actual == 100m);
        Assert.DoesNotContain(v_arrFirstResults, row => row.CheckType == "CURRENT_CLOSING" && row.Status == "FAIL");

        var carryRun = await RunReconciliationAsync(v_Connection, v_Transaction, v_Fixture, v_dtmCarryForward);
        var v_arrCarryResults = await ReadReconciliationResultsAsync(v_Connection, v_Transaction, carryRun);
        Assert.Contains(v_arrCarryResults, row => row.CheckType == "DAILY_CLOSING" && row.Status == "PASS" && row.Expected == 70m && row.Actual == 70m);

        var currentRun = await RunReconciliationAsync(v_Connection, v_Transaction, v_Fixture, null);
        var v_arrCurrentResults = await ReadReconciliationResultsAsync(v_Connection, v_Transaction, currentRun);
        Assert.Contains(v_arrCurrentResults, row => row.CheckType == "CURRENT_CLOSING" && row.Status == "PASS" && row.Expected == 70m && row.Actual == 70m);
    }

    [Fact]
    public async Task Reconciliation_rejects_historical_cutoff_without_ready_daily_projection()
    {
        var v_Fixture = m_objFixture!;
        var v_dtmMovementDate = new DateTime(2099, 2, 1);
        await using var v_SetupConnection = OpenConnection($"P3-M06-not-ready-setup-{v_Fixture.Tag}");
        await v_SetupConnection.OpenAsync();
        await using (var v_SetupTransaction = (SqlTransaction)await v_SetupConnection.BeginTransactionAsync())
        {
            await SetManagedPostContextAsync(v_SetupConnection, v_SetupTransaction, true);
            await InsertPostedReceiptAsync(v_SetupConnection, v_SetupTransaction, v_Fixture, v_dtmMovementDate, 100m, "M06-not-ready");
            await InsertCurrentAsync(v_SetupConnection, v_SetupTransaction, v_Fixture.WarehouseAId, v_Fixture.ProductId, 100m);
            await SetManagedPostContextAsync(v_SetupConnection, v_SetupTransaction, false);
            await v_SetupTransaction.CommitAsync();
        }

        await using var v_Connection = OpenConnection($"P3-M06-not-ready-call-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await AssertSqlNumberAsync(51332, () => RunReconciliationAsync(v_Connection, null, v_Fixture, new DateTime(2099, 1, 1)));
    }

    [Fact]
    public async Task Reconciliation_accepts_zero_history_before_initialized_scope_first_date()
    {
        var v_Fixture = m_objFixture!;
        var v_dtmFirstBalanceDate = new DateTime(2099, 4, 10);
        var v_dtmCutoff = v_dtmFirstBalanceDate.AddDays(-1);

        await using (var v_SetupConnection = OpenConnection($"P3-M06-zero-history-setup-{v_Fixture.Tag}"))
        {
            await v_SetupConnection.OpenAsync();
            await using var v_SetupTransaction = (SqlTransaction)await v_SetupConnection.BeginTransactionAsync();
            await ExecuteAsync(
                v_SetupConnection,
                v_SetupTransaction,
                "UPDATE dbo.InventoryBalance_Daily_AggregateState SET IsInitialized = 1, InitializedAt = COALESCE(InitializedAt, SYSUTCDATETIME()) WHERE State_ID = 1;");
            await InsertCurrentAsync(v_SetupConnection, v_SetupTransaction, v_Fixture.WarehouseAId, v_Fixture.ProductId, 100m);
            await InsertBalanceScopeAsync(v_SetupConnection, v_SetupTransaction, v_Fixture, v_dtmFirstBalanceDate, v_dtmFirstBalanceDate);
            await v_SetupTransaction.CommitAsync();
        }

        await using var v_Connection = OpenConnection($"P3-M06-zero-history-{v_Fixture.Tag}");
        await v_Connection.OpenAsync();
        var runId = await RunReconciliationAsync(v_Connection, null, v_Fixture, v_dtmCutoff);
        await using var v_ReadTransaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        var v_arrResults = await ReadReconciliationResultsAsync(v_Connection, v_ReadTransaction, runId);

        Assert.Contains(v_arrResults, row => row.CheckType == "DAILY_CLOSING" && row.Status == "PASS" && row.Expected == 0m && row.Actual == 0m);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var v_Tag = $"P3-{Guid.NewGuid():N}"[..18];
        var v_Login = $"{v_Tag}-login";
        await using var v_Connection = OpenConnection($"P3-setup-{v_Tag}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        try
        {
            var unitId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-unit", 200));
            var categoryId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP, Ten_LSP, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{v_Tag}-category-code", 100), Text("@Name", $"{v_Tag}-category", 200));
            var productId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{v_Tag}-product-1", 100), Text("@Name", $"{v_Tag}-product-1", 255), BigInt("@CategoryId", categoryId), BigInt("@UnitId", unitId));
            var secondProductId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, @CategoryId, @UnitId, N'');",
                Text("@Code", $"{v_Tag}-product-2", 100), Text("@Name", $"{v_Tag}-product-2", 255), BigInt("@CategoryId", categoryId), BigInt("@UnitId", unitId));
            var supplierId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_NCC(Ma_NCC, Ten_NCC, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Code, @Name, N'');",
                Text("@Code", $"{v_Tag}-supplier-code", 100), Text("@Name", $"{v_Tag}-supplier", 255));
            var warehouseAId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-a", 255));
            var warehouseBId = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-b", 255));
            var memberId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, Trang_Thai_ID, deleted) VALUES (@MemberId, @Login, @Name, 1, 0);",
                BigInt("@MemberId", memberId), Text("@Login", v_Login, 100), Text("@Name", $"{v_Tag}-member", 200));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseA), (@Login, @WarehouseB);",
                Text("@Login", v_Login, 100), BigInt("@WarehouseA", warehouseAId), BigInt("@WarehouseB", warehouseBId));
            await v_Transaction.CommitAsync();
            return new Fixture(v_Tag, v_Login, unitId, categoryId, productId, secondProductId, supplierId, warehouseAId, warehouseBId);
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task SeedHistoryAsync(Fixture p_Fixture, decimal p_ReceiptAtFirstDate, decimal p_IssueAtSecondDate, decimal p_ReceiptAtThirdDate, long productId, decimal p_CurrentQuantity, string p_Suffix)
    {
        await using var v_Connection = OpenConnection($"P3-H08-history-{p_Fixture.Tag}-{p_Suffix}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        await SetManagedPostContextAsync(v_Connection, v_Transaction, true);
        await InsertPostedReceiptAsync(v_Connection, v_Transaction, p_Fixture, new DateTime(2099, 1, 1), p_ReceiptAtFirstDate, $"{p_Suffix}-receipt-1", productId);
        await InsertPostedIssueAsync(v_Connection, v_Transaction, p_Fixture, new DateTime(2099, 1, 5), p_IssueAtSecondDate, $"{p_Suffix}-issue-2", productId);
        await InsertPostedReceiptAsync(v_Connection, v_Transaction, p_Fixture, new DateTime(2099, 1, 10), p_ReceiptAtThirdDate, $"{p_Suffix}-receipt-3", productId);
        await InsertCurrentAsync(v_Connection, v_Transaction, p_Fixture.WarehouseAId, productId, p_CurrentQuantity);
        await SetManagedPostContextAsync(v_Connection, v_Transaction, false);
        await v_Transaction.CommitAsync();
    }

    private static async Task<long> SaveReceiptHeaderAsync(Fixture p_Fixture, long warehouseId, DateTime p_dtmDate, string p_Suffix)
    {
        await using var v_Connection = OpenConnection($"P3-save-receipt-header-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(v_Connection, null, "dbo.F2011_sp_ins_Nhap_Kho_Header",
            BigInt("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{p_Fixture.Tag}-{p_Suffix}-{Guid.NewGuid():N}", 100),
            BigInt("@Kho_ID", warehouseId), BigInt("@NCC_ID", p_Fixture.SupplierId), Date("@Ngay_Nhap_Kho", p_dtmDate),
            Text("@Ghi_Chu", "phase 3 test", 1000), Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
    }

    private static async Task<long> SaveReceiptDetailAsync(Fixture p_Fixture, long receiptId, long productId, decimal p_Quantity)
    {
        await using var v_Connection = OpenConnection($"P3-save-receipt-detail-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(v_Connection, null, "dbo.F2011_sp_ins_Nhap_Kho_Detail",
            BigInt("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", productId),
            Decimal("@SL_Nhap", p_Quantity), Decimal("@Don_Gia_Nhap", 1m), Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
    }

    private static async Task<long> SaveIssueHeaderAsync(Fixture p_Fixture, DateTime p_dtmDate, string p_Suffix)
    {
        await using var v_Connection = OpenConnection($"P3-save-issue-header-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(v_Connection, null, "dbo.F2012_sp_ins_Xuat_Kho_Header",
            BigInt("@Auto_ID", 0), Text("@So_Phieu_Xuat_Kho", $"{p_Fixture.Tag}-{p_Suffix}-{Guid.NewGuid():N}", 100),
            BigInt("@Kho_ID", p_Fixture.WarehouseAId), Date("@Ngay_Xuat_Kho", p_dtmDate),
            Text("@Ghi_Chu", "phase 3 test", 1000), Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
    }

    private static async Task<long> SaveIssueDetailAsync(Fixture p_Fixture, long issueId, long productId, decimal p_Quantity)
    {
        await using var v_Connection = OpenConnection($"P3-save-issue-detail-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        return await ExecuteStoredWithOutputAsync(v_Connection, null, "dbo.F2012_sp_ins_Xuat_Kho_Detail",
            BigInt("@Auto_ID", 0), BigInt("@Xuat_Kho_ID", issueId), BigInt("@San_Pham_ID", productId),
            Decimal("@SL_Xuat", p_Quantity), Decimal("@Don_Gia_Xuat", 1m), Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
    }

    private static Task PostDocumentAsync(Fixture p_Fixture, long documentId, bool p_bIsReceipt)
    {
        return ExecuteStoredAsync("dbo.sp_XNK_Document_Post", new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = p_bIsReceipt }, BigInt("@Document_ID", documentId), Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
    }

    private static async Task InsertPostedReceiptAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Fixture p_Fixture, DateTime p_dtmDate, decimal p_Quantity, string p_Suffix, long? productId = null)
    {
        var documentId = await InsertIdAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N'phase 3 fixture'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{p_Fixture.Tag}-{p_Suffix}-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", p_Fixture.WarehouseAId),
            BigInt("@SupplierId", p_Fixture.SupplierId), Date("@Date", p_dtmDate));
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", productId ?? p_Fixture.ProductId), Decimal("@Quantity", p_Quantity));
    }

    private static async Task InsertPostedIssueAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Fixture p_Fixture, DateTime p_dtmDate, decimal p_Quantity, string p_Suffix, long? productId = null)
    {
        var documentId = await InsertIdAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @Date, 1, N'phase 3 fixture'); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
            Text("@Number", $"{p_Fixture.Tag}-{p_Suffix}-{Guid.NewGuid():N}", 100), BigInt("@WarehouseId", p_Fixture.WarehouseAId), Date("@Date", p_dtmDate));
        await ExecuteAsync(p_Connection, p_Transaction,
            "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@DocumentId, @ProductId, @Quantity, 1);",
            BigInt("@DocumentId", documentId), BigInt("@ProductId", productId ?? p_Fixture.ProductId), Decimal("@Quantity", p_Quantity));
    }

    private static Task InsertCurrentAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, long warehouseId, long productId, decimal p_Quantity)
    {
        return ExecuteAsync(p_Connection, p_Transaction, "IF EXISTS (SELECT 1 FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId) UPDATE dbo.InventoryBalance_Current SET CurrentQuantity = @Quantity, ReservedQuantity = 0 WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId; ELSE INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, @Quantity, 0);", BigInt("@WarehouseId", warehouseId), BigInt("@ProductId", productId), Decimal("@Quantity", p_Quantity));
    }

    private static Task InsertMovementAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Fixture p_Fixture, DateTime p_dtmDate, decimal p_Received, decimal p_Issued, long? productId = null)
    {
        return ExecuteAsync(p_Connection, p_Transaction, "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseId, @ProductId, @Received, @Issued, 1);", Date("@Date", p_dtmDate), BigInt("@WarehouseId", p_Fixture.WarehouseAId), BigInt("@ProductId", productId ?? p_Fixture.ProductId), Decimal("@Received", p_Received), Decimal("@Issued", p_Issued));
    }

    private static Task InsertBalanceAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Fixture p_Fixture, DateTime p_dtmDate, decimal p_Opening, decimal p_Received, decimal p_Issued, decimal p_Closing, decimal p_CumulativeReceived, decimal p_CumulativeIssued, long? productId = null)
    {
        return ExecuteAsync(p_Connection, p_Transaction, "INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseId, @ProductId, @Opening, @Received, @Issued, @Closing, @CumulativeReceived, @CumulativeIssued, 1);", Date("@Date", p_dtmDate), BigInt("@WarehouseId", p_Fixture.WarehouseAId), BigInt("@ProductId", productId ?? p_Fixture.ProductId), Decimal("@Opening", p_Opening), Decimal("@Received", p_Received), Decimal("@Issued", p_Issued), Decimal("@Closing", p_Closing), Decimal("@CumulativeReceived", p_CumulativeReceived), Decimal("@CumulativeIssued", p_CumulativeIssued));
    }

    private static Task InsertBalanceScopeAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Fixture p_Fixture, DateTime p_dtmFirstDate, DateTime p_dtmLastDate, long? productId = null)
    {
        return ExecuteAsync(p_Connection, p_Transaction, "INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseId, @ProductId, @FirstDate, @LastDate);", BigInt("@WarehouseId", p_Fixture.WarehouseAId), BigInt("@ProductId", productId ?? p_Fixture.ProductId), Date("@FirstDate", p_dtmFirstDate), Date("@LastDate", p_dtmLastDate));
    }

    private static Task InsertSnapshotAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Fixture p_Fixture, DateTime p_dtmDate, decimal p_Closing, long? productId = null)
    {
        return ExecuteAsync(p_Connection, p_Transaction, "INSERT dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity, IsValid, [Version]) VALUES (@Date, @WarehouseId, @ProductId, @Closing, 1, 1);", Date("@Date", p_dtmDate), BigInt("@WarehouseId", p_Fixture.WarehouseAId), BigInt("@ProductId", productId ?? p_Fixture.ProductId), Decimal("@Closing", p_Closing));
    }

    private static async Task ClearQueuesAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Fixture p_Fixture)
    {
        await ExecuteAsync(p_Connection, p_Transaction,
            "DELETE d FROM dbo.InventorySnapshot_RebuildDeadLetter d JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID IN (@WarehouseA, @WarehouseB) AND q.San_Pham_ID IN (@ProductId, @SecondProductId); DELETE d FROM dbo.InventoryMovement_RebuildDeadLetter d JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = d.Queue_ID WHERE q.Kho_ID IN (@WarehouseA, @WarehouseB) AND q.San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId);",
            BigInt("@WarehouseA", p_Fixture.WarehouseAId), BigInt("@WarehouseB", p_Fixture.WarehouseBId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@SecondProductId", p_Fixture.SecondProductId));
    }

    private static async Task<InventoryReportRow> ReadInventoryReportAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, Fixture p_Fixture, DateTime p_dtmFrom, DateTime p_dtmTo)
    {
        await using var v_Command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton_Page", p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.Add(Date("@Tu_Ngay", p_dtmFrom));
        v_Command.Parameters.Add(Date("@Den_Ngay", p_dtmTo));
        v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
        v_Command.Parameters.Add(BigInt("@Kho_ID", p_Fixture.WarehouseAId));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        Assert.True(await v_Reader.NextResultAsync());
        Assert.True(await v_Reader.ReadAsync());
        return new InventoryReportRow(v_Reader.GetDecimal(5), v_Reader.GetDecimal(6), v_Reader.GetDecimal(7), v_Reader.GetDecimal(8));
    }

    private static async Task<long> RunReconciliationAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, Fixture p_Fixture, DateTime? p_dtmAsOfDate)
    {
        await using var v_Command = new SqlCommand("dbo.sp_Inventory_Reconciliation_Run", p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        object v_objAsOfDate;
        if (p_dtmAsOfDate.HasValue)
        {
            v_objAsOfDate = p_dtmAsOfDate.Value.Date;
        }
        else
        {
            v_objAsOfDate = DBNull.Value;
        }

        v_Command.Parameters.Add(new SqlParameter("@As_Of_Date", SqlDbType.Date) { Value = v_objAsOfDate });
        v_Command.Parameters.Add(BigInt("@Kho_ID", p_Fixture.WarehouseAId));
        v_Command.Parameters.Add(BigInt("@San_Pham_ID", p_Fixture.ProductId));
        var v_RunId = new SqlParameter("@Run_ID", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
        v_Command.Parameters.Add(v_RunId);
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_RunId.Value);
    }

    private static async Task<List<ReconciliationRow>> ReadReconciliationResultsAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, long runId)
    {
        await using var v_Command = new SqlCommand(
            "SELECT Check_Type, ExpectedQuantity, ActualQuantity, Difference, Status FROM dbo.InventoryReconciliation_Result WHERE Run_ID = @RunId;",
            p_Connection,
            p_Transaction);
        v_Command.Parameters.Add(BigInt("@RunId", runId));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        var v_arrRows = new List<ReconciliationRow>();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new ReconciliationRow(v_Reader.GetString(0), v_Reader.GetDecimal(1), v_Reader.GetDecimal(2), v_Reader.GetDecimal(3), v_Reader.GetString(4)));
        return v_arrRows;
    }

    private static async Task SetManagedPostContextAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, bool p_bEnabled)
    {
        object v_objEnabledValue;
        if (p_bEnabled == true)
        {
            v_objEnabledValue = 1;
        }
        else
        {
            v_objEnabledValue = DBNull.Value;
        }

        await ExecuteAsync(p_Connection, p_Transaction,
            "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = @Value;",
            new SqlParameter("@Value", SqlDbType.Bit) { Value = v_objEnabledValue });
    }

    private static async Task CleanupFixtureAsync(Fixture p_Fixture)
    {
        await using var v_Connection = OpenConnection($"P3-cleanup-{p_Fixture.Tag}");
        await v_Connection.OpenAsync();
        await using var v_Transaction = (SqlTransaction)await v_Connection.BeginTransactionAsync();
        try
        {
            await SetManagedPostContextAsync(v_Connection, v_Transaction, true);
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE r FROM dbo.InventoryReconciliation_Result r JOIN dbo.InventoryReconciliation_Run run ON run.ID = r.Run_ID WHERE run.Kho_ID IN (@WarehouseA, @WarehouseB) AND (run.San_Pham_ID = @ProductId OR run.San_Pham_ID = @SecondProductId); DELETE FROM dbo.InventoryReconciliation_Run WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE d FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE h.Kho_ID IN (@WarehouseA, @WarehouseB); DELETE d FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE h.Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Kho_ID IN (@WarehouseA, @WarehouseB);",
                BigInt("@WarehouseA", p_Fixture.WarehouseAId), BigInt("@WarehouseB", p_Fixture.WarehouseBId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@SecondProductId", p_Fixture.SecondProductId));
            await ClearQueuesAsync(v_Connection, v_Transaction, p_Fixture);
            await ExecuteAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB) AND San_Pham_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_DM_NCC WHERE Auto_ID = @SupplierId; DELETE FROM dbo.tbl_DM_San_Pham WHERE Auto_ID IN (@ProductId, @SecondProductId); DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID = @CategoryId; DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID = @UnitId;",
                BigInt("@WarehouseA", p_Fixture.WarehouseAId), BigInt("@WarehouseB", p_Fixture.WarehouseBId), BigInt("@ProductId", p_Fixture.ProductId), BigInt("@SecondProductId", p_Fixture.SecondProductId), Text("@Login", p_Fixture.Login, 100), BigInt("@SupplierId", p_Fixture.SupplierId), BigInt("@CategoryId", p_Fixture.CategoryId), BigInt("@UnitId", p_Fixture.UnitId));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long> ExecuteStoredWithOutputAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure };
        var v_Output = p_arrParameters.Single(parameter => parameter.ParameterName == "@Auto_ID");
        v_Output.Direction = ParameterDirection.InputOutput;
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_Output.Value);
    }

    private static async Task ExecuteStoredAsync(string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Connection = OpenConnection($"P3-{p_Procedure}");
        await v_Connection.OpenAsync();
        await using var v_Command = new SqlCommand(p_Procedure, v_Connection) { CommandType = CommandType.StoredProcedure };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSqlNumberAsync(int p_iExpectedNumber, Func<Task> p_Operation)
    {
        var v_Exception = await Assert.ThrowsAsync<SqlException>(p_Operation);
        Assert.Equal(p_iExpectedNumber, v_Exception.Number);
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

    private static async Task<int> IntScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt32(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
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

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static SqlConnection OpenConnection(string p_ApplicationName)
    {
        var v_Builder = new SqlConnectionStringBuilder(BaseConnectionString) { ApplicationName = p_ApplicationName };
        return new SqlConnection(v_Builder.ConnectionString);
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

    private sealed record Fixture(string Tag, string Login, long UnitId, long CategoryId, long ProductId, long SecondProductId, long SupplierId, long WarehouseAId, long WarehouseBId);
    private sealed record InventoryReportRow(decimal Opening, decimal Received, decimal Issued, decimal Closing);
    private sealed record ReconciliationRow(string CheckType, decimal Expected, decimal Actual, decimal Difference, string Status);
}
