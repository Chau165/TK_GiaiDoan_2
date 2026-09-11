using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10_1R01R02IntegrationTests
{
    private const string DmlProbeUser = "Phase101DmlProbe";
    private const string ApplicationUser = "Phase101Application";
    private static string ConnectionString => WarehouseTestDatabase.ConnectionString;

    [Fact]
    public async Task R01_session_context_cannot_bypass_any_posted_boundary()
    {
        var fixture = await CreatePostedIssueFixtureAsync();
        await EnsureDmlProbeUserAsync();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        var impersonated = false;
        try
        {
            await ExecuteAsync(connection, transaction,
                $"EXECUTE AS USER = N'{DmlProbeUser}'; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            impersonated = true;

            await AssertDirectMutationRejectedAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, 1, 1);",
                BigInt("@IssueId", fixture.IssueId),
                BigInt("@ProductId", fixture.ProductId));
            await AssertDirectMutationRejectedAsync(connection, transaction,
                "UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat = SL_Xuat + 1 WHERE Auto_ID = @DetailId;",
                BigInt("@DetailId", fixture.DetailId));
            await AssertDirectMutationRejectedAsync(connection, transaction,
                "DELETE dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @DetailId;",
                BigInt("@DetailId", fixture.DetailId));

            await AssertDirectMutationRejectedAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, 1, 1);",
                BigInt("@ReceiptId", fixture.ReceiptId), BigInt("@ProductId", fixture.ProductId));
            await AssertDirectMutationRejectedAsync(connection, transaction,
                "UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap = SL_Nhap + 1 WHERE Auto_ID = (SELECT TOP (1) Auto_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId);",
                BigInt("@ReceiptId", fixture.ReceiptId));
            await AssertDirectMutationRejectedAsync(connection, transaction,
                "DELETE dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = (SELECT TOP (1) Auto_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId);",
                BigInt("@ReceiptId", fixture.ReceiptId));

            await AssertDirectMutationRejectedAsync(connection, transaction,
                "UPDATE dbo.tbl_XNK_Xuat_Kho SET Ghi_Chu = N'blocked' WHERE Auto_ID = @IssueId;",
                BigInt("@IssueId", fixture.IssueId));
            await AssertDirectMutationRejectedAsync(connection, transaction,
                "DELETE dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId;",
                BigInt("@IssueId", fixture.IssueId));
            await AssertDirectMutationRejectedAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @BusinessDate, 1, N'blocked');",
                Text("@Number", $"{fixture.Tag}-blocked-issue", 100), BigInt("@WarehouseId", fixture.WarehouseId), Date("@BusinessDate", DateTime.Today));

            await AssertDirectMutationRejectedAsync(connection, transaction,
                "UPDATE dbo.tbl_XNK_Nhap_Kho SET Ghi_Chu = N'blocked' WHERE Auto_ID = @ReceiptId;",
                BigInt("@ReceiptId", fixture.ReceiptId));
            await AssertDirectMutationRejectedAsync(connection, transaction,
                "DELETE dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId;",
                BigInt("@ReceiptId", fixture.ReceiptId));
            await AssertDirectMutationRejectedAsync(connection, transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @BusinessDate, 1, N'blocked');",
                Text("@Number", $"{fixture.Tag}-blocked-receipt", 100), BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@SupplierId", fixture.SupplierId), Date("@BusinessDate", DateTime.Today));
        }
        finally
        {
            if (impersonated)
            {
                try { await ExecuteAsync(connection, transaction, "REVERT;"); } catch { }
            }

            try { await transaction.RollbackAsync(); } catch { }
            await CleanupPostedIssueFixtureAsync(fixture);
            await DropDmlProbeUserAsync();
        }
    }

    [Fact]
    public async Task R01_application_role_denies_direct_dml_and_allows_canonical_save_post()
    {
        var fixture = await CreatePostedIssueFixtureAsync();
        await EnsureApplicationUserAsync();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        var impersonated = false;
        try
        {
            await ExecuteAsync(connection, transaction,
                $"EXECUTE AS USER = N'{ApplicationUser}'; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            impersonated = true;

            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                connection,
                transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, 1, 1);",
                BigInt("@IssueId", fixture.IssueId), BigInt("@ProductId", fixture.ProductId)));
            Assert.True(error.Number is 229 or 51228, $"Unexpected direct-DML rejection number: {error.Number}");

            await AssertDirectMutationRejectedAsync(connection, transaction,
                "UPDATE dbo.InventoryBalance_Current SET CurrentQuantity = CurrentQuantity WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", fixture.WarehouseId), BigInt("@ProductId", fixture.ProductId));
            await AssertDirectMutationRejectedAsync(connection, transaction,
                "DELETE FROM dbo.InventoryReservation_Current WHERE 1 = 0;");

            var receiptId = await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Nhap_Kho_Save_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{fixture.Tag}-app-receipt", 100), BigInt("@Kho_ID", fixture.WarehouseId),
                BigInt("@NCC_ID", fixture.SupplierId), Date("@Ngay_Nhap_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000), Text("@Ma_Dang_Nhap", fixture.Login, 100),
                Text("@Created_By", fixture.Login, 100), Text("@Created_By_Function", "Phase10.1-App", 100), Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));
            await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Nhap_Kho_Save_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", fixture.ProductId), Decimal("@SL_Nhap", 1m), Decimal("@Don_Gia_Nhap", 1m),
                Text("@Ma_Dang_Nhap", fixture.Login, 100), Text("@Created_By", fixture.Login, 100), Text("@Created_By_Function", "Phase10.1-App", 100),
                Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));
            await ExecuteStoredAsync(connection, transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", fixture.Login, 100),
                Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));

            var issueId = await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Xuat_Kho_Save_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Xuat_Kho", $"{fixture.Tag}-app-issue", 100), BigInt("@Kho_ID", fixture.WarehouseId),
                Date("@Ngay_Xuat_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000), Text("@Ma_Dang_Nhap", fixture.Login, 100),
                Text("@Created_By", fixture.Login, 100), Text("@Created_By_Function", "Phase10.1-App", 100), Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));
            await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Xuat_Kho_Save_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Xuat_Kho_ID", issueId), BigInt("@San_Pham_ID", fixture.ProductId), Decimal("@SL_Xuat", 1m), Decimal("@Don_Gia_Xuat", 1m),
                Text("@Ma_Dang_Nhap", fixture.Login, 100), Text("@Created_By", fixture.Login, 100), Text("@Created_By_Function", "Phase10.1-App", 100),
                Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));
            await ExecuteStoredAsync(connection, transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", false), BigInt("@Document_ID", issueId), Text("@Ma_Dang_Nhap", fixture.Login, 100),
                Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));

            var repostError = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(connection, transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", false), BigInt("@Document_ID", issueId), Text("@Ma_Dang_Nhap", fixture.Login, 100),
                Text("@Last_Updated_By", fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100)));
            Assert.Equal(51162, repostError.Number);
        }
        finally
        {
            if (impersonated)
            {
                try { await ExecuteAsync(connection, transaction, "REVERT;"); } catch { }
            }

            try { await transaction.RollbackAsync(); } catch { }
            await CleanupPostedIssueFixtureAsync(fixture);
            await DropApplicationUserAsync();
        }
    }

    [Fact]
    public async Task R01_session_context_does_not_leak_across_pooled_connections()
    {
        SqlConnection.ClearAllPools();
        var pooledConnectionString = new SqlConnectionStringBuilder(ConnectionString)
        {
            MaxPoolSize = 1,
            MinPoolSize = 0
        }.ConnectionString;

        try
        {
            await using (var first = new SqlConnection(pooledConnectionString))
            {
                await first.OpenAsync();
                await ExecuteAsync(first, null,
                    "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            }

            await using (var second = new SqlConnection(pooledConnectionString))
            {
                await second.OpenAsync();
                var leakedMarker = await ScalarAsync(second, null,
                    "SELECT SESSION_CONTEXT(N'InventoryMovement:ManagedPost');");
                Assert.True(leakedMarker is null or DBNull,
                    $"Trusted marker leaked across pooled connections: {leakedMarker}");
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task R02_nonpaged_report_rejects_new_scope_after_catalog_changes()
    {
        var fixture = await CreateNewScopeReportFixtureAsync();
        await using var locker = new SqlConnection(ConnectionString);
        await using var report = new SqlConnection(ConnectionString);
        await using var post = new SqlConnection(ConnectionString);
        await locker.OpenAsync();
        await report.OpenAsync();
        await post.OpenAsync();
        await using var lockerTransaction = locker.BeginTransaction();

        try
        {
            await ExecuteAsync(locker, lockerTransaction,
                "SELECT COUNT(*) FROM dbo.InventorySnapshot_ReportFallbackLog WITH (TABLOCKX, HOLDLOCK);");

            var reportSpid = await IntScalarAsync(report, null, "SELECT @@SPID;");
            var reportTask = ReadNonPagedReportAsync(report, fixture);
            Assert.True(await WaitForLockWaitAsync(reportSpid), "Report did not pause after scope discovery/readiness.");

            await ExecuteStoredAsync(post, null, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", fixture.DraftReceiptId),
                Text("@Ma_Dang_Nhap", fixture.Login, 100), Text("@Last_Updated_By", fixture.Login, 100),
                Text("@Last_Updated_By_Function", "Phase10.1-R02", 100));

            await lockerTransaction.RollbackAsync();
            var error = await Assert.ThrowsAsync<SqlException>(async () => await reportTask);
            Assert.Equal(51324, error.Number);
        }
        finally
        {
            try { await lockerTransaction.RollbackAsync(); } catch { }
            await CleanupNewScopeReportFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task R02_paged_report_rejects_new_scope_after_scope_materialization_before_count()
    {
        var fixture = await CreatePagedNewScopeReportFixtureAsync();
        await using var locker = new SqlConnection(ConnectionString);
        await using var report = new SqlConnection(ConnectionString);
        await using var post = new SqlConnection(ConnectionString);
        await locker.OpenAsync();
        await report.OpenAsync();
        await post.OpenAsync();
        await using var lockerTransaction = locker.BeginTransaction();

        try
        {
            await ExecuteAsync(locker, lockerTransaction,
                "SELECT COUNT(*) FROM dbo.Inventory_Balance_Daily WITH (TABLOCKX, HOLDLOCK);");

            var reportSpid = await IntScalarAsync(report, null, "SELECT @@SPID;");
            var reportTask = ReadPagedReportAsync(report, fixture);
            Assert.True(await WaitForLockWaitAsync(reportSpid), "Paged report did not pause after scope materialization before COUNT.");

            await ExecuteStoredAsync(post, null, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", fixture.DraftReceiptId),
                Text("@Ma_Dang_Nhap", fixture.Login, 100), Text("@Last_Updated_By", fixture.Login, 100),
                Text("@Last_Updated_By_Function", "Phase10.1-R02-Paged", 100));

            await lockerTransaction.RollbackAsync();
            var error = await Assert.ThrowsAsync<SqlException>(async () => await reportTask);
            Assert.Equal(51324, error.Number);
        }
        finally
        {
            try { await lockerTransaction.RollbackAsync(); } catch { }
            await CleanupNewScopeReportFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task R02_current_report_rejects_count_data_generation_change()
    {
        var fixture = await CreateNewScopeReportFixtureAsync();
        await using var locker = new SqlConnection(ConnectionString);
        await using var report = new SqlConnection(ConnectionString);
        await using var post = new SqlConnection(ConnectionString);
        await locker.OpenAsync();
        await report.OpenAsync();
        await post.OpenAsync();
        await using var lockerTransaction = locker.BeginTransaction();

        try
        {
            await ExecuteAsync(locker, lockerTransaction,
                "SELECT Auto_ID FROM dbo.tbl_DM_San_Pham WITH (PAGLOCK, XLOCK, HOLDLOCK) WHERE Auto_ID = @ProductId;",
                BigInt("@ProductId", fixture.ProductId));

            var reportSpid = await IntScalarAsync(report, null, "SELECT @@SPID;");
            var reportTask = ReadCurrentReportAsync(report, fixture);
            Assert.True(await WaitForLockWaitAsync(reportSpid), "Current report did not pause during page materialization.");

            await ExecuteStoredAsync(post, null, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", fixture.DraftReceiptId),
                Text("@Ma_Dang_Nhap", fixture.Login, 100), Text("@Last_Updated_By", fixture.Login, 100),
                Text("@Last_Updated_By_Function", "Phase10.2-P101-A02", 100));

            await lockerTransaction.RollbackAsync();
            var error = await Assert.ThrowsAsync<SqlException>(async () => await reportTask);
            Assert.Equal(51324, error.Number);
        }
        finally
        {
            try { await lockerTransaction.RollbackAsync(); } catch { }
            await CleanupNewScopeReportFixtureAsync(fixture);
        }
    }

    [Fact]
    public async Task R02_current_generation_advances_for_reservation_projection_writes()
    {
        var fixture = await CreatePostedIssueFixtureAsync();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        try
        {
            var before = await LongScalarAsync(connection, null,
                "SELECT Generation FROM dbo.Inventory_Current_Report_State WHERE State_ID = 1;");
            await ExecuteStoredAsync(connection, null, "dbo.sp_XNK_Reservation_Adjust",
                BigInt("@Kho_ID", fixture.WarehouseId), BigInt("@San_Pham_ID", fixture.ProductId), Decimal("@Delta", 1m));
            var afterReserve = await LongScalarAsync(connection, null,
                "SELECT Generation FROM dbo.Inventory_Current_Report_State WHERE State_ID = 1;");
            await ExecuteStoredAsync(connection, null, "dbo.sp_XNK_Reservation_Adjust",
                BigInt("@Kho_ID", fixture.WarehouseId), BigInt("@San_Pham_ID", fixture.ProductId), Decimal("@Delta", -1m));
            var afterRelease = await LongScalarAsync(connection, null,
                "SELECT Generation FROM dbo.Inventory_Current_Report_State WHERE State_ID = 1;");

            Assert.True(afterReserve > before);
            Assert.True(afterRelease > afterReserve);
        }
        finally
        {
            await CleanupPostedIssueFixtureAsync(fixture);
        }
    }

    private static async Task<PostedIssueFixture> CreatePostedIssueFixtureAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var tag = $"P101-R01-{Guid.NewGuid():N}"[..40];
            var productId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseId = await LongScalarAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R01');",
                Text("@Name", tag, 255));
            var login = $"{tag}-login";
            var memberId = await LongScalarAsync(connection, transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10.1 R01', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                BigInt("@MemberId", memberId), Text("@Login", login, 100), BigInt("@WarehouseId", warehouseId));

            var receiptId = await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Nhap_Kho_Save_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{tag}-receipt", 100), BigInt("@Kho_ID", warehouseId),
                BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000),
                Text("@Ma_Dang_Nhap", login, 100), Text("@Created_By", login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Nhap_Kho_Save_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", productId),
                Decimal("@SL_Nhap", 10m), Decimal("@Don_Gia_Nhap", 1m), Text("@Ma_Dang_Nhap", login, 100),
                Text("@Created_By", login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            await ExecuteStoredAsync(connection, transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", login, 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));

            var issueId = await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Xuat_Kho_Save_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Xuat_Kho", $"{tag}-issue", 100), BigInt("@Kho_ID", warehouseId),
                Date("@Ngay_Xuat_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000), Text("@Ma_Dang_Nhap", login, 100),
                Text("@Created_By", login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            var detailId = await ExecuteStoredIdAsync(connection, transaction, "dbo.sp_XNK_Xuat_Kho_Save_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Xuat_Kho_ID", issueId), BigInt("@San_Pham_ID", productId),
                Decimal("@SL_Xuat", 2m), Decimal("@Don_Gia_Xuat", 1m), Text("@Ma_Dang_Nhap", login, 100),
                Text("@Created_By", login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            await ExecuteStoredAsync(connection, transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", false), BigInt("@Document_ID", issueId), Text("@Ma_Dang_Nhap", login, 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));

            await transaction.CommitAsync();
            return new PostedIssueFixture(tag, login, warehouseId, productId, supplierId, receiptId, issueId, detailId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<NewScopeReportFixture> CreateNewScopeReportFixtureAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var tag = $"P101-R02-{Guid.NewGuid():N}"[..40];
            var productId = await LongScalarAsync(connection, transaction,
                "SELECT TOP (1) p.Auto_ID FROM dbo.tbl_DM_San_Pham p CROSS APPLY sys.fn_PhysLocCracker(p.%%physloc%%) loc ORDER BY loc.page_id, p.Auto_ID;");
            var postedProductId = await LongScalarAsync(connection, transaction,
                "SELECT TOP (1) p.Auto_ID FROM dbo.tbl_DM_San_Pham p CROSS APPLY sys.fn_PhysLocCracker(p.%%physloc%%) loc WHERE loc.page_id <> (SELECT TOP (1) loc2.page_id FROM dbo.tbl_DM_San_Pham p2 CROSS APPLY sys.fn_PhysLocCracker(p2.%%physloc%%) loc2 WHERE p2.Auto_ID = @ReportProductId) ORDER BY loc.page_id, p.Auto_ID;",
                BigInt("@ReportProductId", productId));
            var supplierId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await LongScalarAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R02 A');",
                Text("@Name", $"{tag}-A", 255));
            var warehouseB = await LongScalarAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R02 B');",
                Text("@Name", $"{tag}-B", 255));
            var login = $"{tag}-login";
            var memberId = await LongScalarAsync(connection, transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10.1 R02', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseA), (@Login, @WarehouseB); INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseA, @ProductId, 10, 0);",
                BigInt("@MemberId", memberId), Text("@Login", login, 100), BigInt("@WarehouseA", warehouseA),
                BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId));

            await transaction.CommitAsync();

            await using var draftConnection = new SqlConnection(ConnectionString);
            await draftConnection.OpenAsync();
            var draftReceiptId = await ExecuteStoredIdAsync(draftConnection, null, "dbo.sp_XNK_Nhap_Kho_Save_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{tag}-draft-receipt", 100), BigInt("@Kho_ID", warehouseB),
                BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000),
                Text("@Ma_Dang_Nhap", login, 100), Text("@Created_By", login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            await ExecuteStoredIdAsync(draftConnection, null, "dbo.sp_XNK_Nhap_Kho_Save_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", draftReceiptId), BigInt("@San_Pham_ID", postedProductId),
                Decimal("@SL_Nhap", 10m), Decimal("@Don_Gia_Nhap", 1m), Text("@Ma_Dang_Nhap", login, 100),
                Text("@Created_By", login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));

            return new NewScopeReportFixture(tag, login, warehouseA, warehouseB, productId, postedProductId, draftReceiptId, DateTime.Today);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<PagedNewScopeReportFixture> CreatePagedNewScopeReportFixtureAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var tag = $"P101-R02P-{Guid.NewGuid():N}"[..40];
            var productId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await LongScalarAsync(connection, transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await LongScalarAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R02 paged A');",
                Text("@Name", $"{tag}-A", 255));
            var warehouseB = await LongScalarAsync(connection, transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R02 paged B');",
                Text("@Name", $"{tag}-B", 255));
            var login = $"{tag}-login";
            var memberId = await LongScalarAsync(connection, transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            var businessDate = DateTime.Today.AddDays(-1);
            await ExecuteAsync(connection, transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10.1 R02 paged', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseA), (@Login, @WarehouseB); INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseA, @ProductId, 100, 0), (@WarehouseB, @ProductId, 0, 0); INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@BusinessDate, @WarehouseA, @ProductId, 0, 100, 0, 100, 100, 0, 1); INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseA, @ProductId, @BusinessDate, @BusinessDate); INSERT dbo.Inventory_Report_Scope_Catalog(Kho_ID, San_Pham_ID, First_Posted_Date, Last_Posted_Date, Is_Current) VALUES (@WarehouseA, @ProductId, @BusinessDate, @BusinessDate, 1), (@WarehouseB, @ProductId, NULL, NULL, 0);",
                BigInt("@MemberId", memberId), Text("@Login", login, 100), BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId), Date("@BusinessDate", businessDate));
            await transaction.CommitAsync();

            await using var draftConnection = new SqlConnection(ConnectionString);
            await draftConnection.OpenAsync();
            var draftReceiptId = await ExecuteStoredIdAsync(draftConnection, null, "dbo.sp_XNK_Nhap_Kho_Save_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{tag}-draft-receipt", 100), BigInt("@Kho_ID", warehouseB),
                BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", businessDate), Text("@Ghi_Chu", "", 1000), Text("@Ma_Dang_Nhap", login, 100),
                Text("@Created_By", login, 100), Text("@Created_By_Function", "Phase10.1-R02-Paged", 100), Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1-R02-Paged", 100));
            await ExecuteStoredIdAsync(draftConnection, null, "dbo.sp_XNK_Nhap_Kho_Save_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", draftReceiptId), BigInt("@San_Pham_ID", productId), Decimal("@SL_Nhap", 10m), Decimal("@Don_Gia_Nhap", 1m),
                Text("@Ma_Dang_Nhap", login, 100), Text("@Created_By", login, 100), Text("@Created_By_Function", "Phase10.1-R02-Paged", 100),
                Text("@Last_Updated_By", login, 100), Text("@Last_Updated_By_Function", "Phase10.1-R02-Paged", 100));

            return new PagedNewScopeReportFixture(tag, login, warehouseA, warehouseB, productId, draftReceiptId, businessDate);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task EnsureDmlProbeUserAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null,
                $"IF DATABASE_PRINCIPAL_ID(N'{DmlProbeUser}') IS NULL CREATE USER [{DmlProbeUser}] WITHOUT LOGIN; GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Nhap_Kho TO [{DmlProbeUser}]; GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Nhap_Kho_Raw_Data TO [{DmlProbeUser}]; GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Xuat_Kho TO [{DmlProbeUser}]; GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Xuat_Kho_Raw_Data TO [{DmlProbeUser}];");
    }

    private static async Task EnsureApplicationUserAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null,
            $"IF DATABASE_PRINCIPAL_ID(N'{ApplicationUser}') IS NOT NULL BEGIN IF EXISTS (SELECT 1 FROM sys.database_role_members WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'Warehouse_Application') AND member_principal_id = DATABASE_PRINCIPAL_ID(N'{ApplicationUser}')) ALTER ROLE [Warehouse_Application] DROP MEMBER [{ApplicationUser}]; DROP USER [{ApplicationUser}]; END; CREATE USER [{ApplicationUser}] WITHOUT LOGIN; ALTER ROLE [Warehouse_Application] ADD MEMBER [{ApplicationUser}];");
    }

    private static async Task DropDmlProbeUserAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, $"IF DATABASE_PRINCIPAL_ID(N'{DmlProbeUser}') IS NOT NULL DROP USER [{DmlProbeUser}];");
    }

    private static async Task DropApplicationUserAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null,
            $"IF DATABASE_PRINCIPAL_ID(N'{ApplicationUser}') IS NOT NULL BEGIN IF EXISTS (SELECT 1 FROM sys.database_role_members WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'Warehouse_Application') AND member_principal_id = DATABASE_PRINCIPAL_ID(N'{ApplicationUser}')) ALTER ROLE [Warehouse_Application] DROP MEMBER [{ApplicationUser}]; DROP USER [{ApplicationUser}]; END;");
    }

    private static async Task AssertDirectMutationRejectedAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, transaction, sql, parameters));
        Assert.True(error.Number is 51228 or 547 or 229, $"Unexpected direct-DML rejection number: {error.Number}");
    }

    private static async Task<List<long>> ReadNonPagedReportAsync(SqlConnection connection, NewScopeReportFixture fixture)
    {
        await using var command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton", connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        command.Parameters.Add(Date("@Tu_Ngay", fixture.BusinessDate));
        command.Parameters.Add(Date("@Den_Ngay", fixture.BusinessDate));
        command.Parameters.Add(Text("@Ma_Dang_Nhap", fixture.Login, 100));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<long>();
        while (await reader.ReadAsync()) rows.Add(reader.GetInt64(reader.GetOrdinal("San_Pham_ID")));
        return rows;
    }

    private static async Task<(int TotalCount, List<long> Rows)> ReadCurrentReportAsync(SqlConnection connection, NewScopeReportFixture fixture)
    {
        await using var command = new SqlCommand("dbo.sp_BC_Ton_Kho_Hien_Tai_Page", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        command.Parameters.Add(Int("@Page_Number", 1));
        command.Parameters.Add(Int("@Page_Size", 10));
        command.Parameters.Add(Text("@Ma_Dang_Nhap", fixture.Login, 100));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        var rows = new List<long>();
        while (await reader.ReadAsync()) rows.Add(reader.GetInt64(reader.GetOrdinal("San_Pham_ID")));
        return (totalCount, rows);
    }

    private static async Task<(int TotalCount, List<long> Rows)> ReadPagedReportAsync(SqlConnection connection, PagedNewScopeReportFixture fixture)
    {
        await using var command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton_Page", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        command.Parameters.Add(Date("@Tu_Ngay", fixture.BusinessDate));
        command.Parameters.Add(Date("@Den_Ngay", fixture.BusinessDate));
        command.Parameters.Add(Int("@Page_Number", 1));
        command.Parameters.Add(Int("@Page_Size", 10));
        command.Parameters.Add(Text("@Ma_Dang_Nhap", fixture.Login, 100));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var totalCount = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        var rows = new List<long>();
        while (await reader.ReadAsync()) rows.Add(reader.GetInt64(reader.GetOrdinal("San_Pham_ID")));
        return (totalCount, rows);
    }

    private static async Task<bool> WaitForLockWaitAsync(int sessionId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var waitType = await ScalarAsync(connection, null,
                "SELECT wait_type FROM sys.dm_exec_requests WHERE session_id = @SessionId AND wait_type LIKE N'LCK_M_%';",
                Int("@SessionId", sessionId));
            if (waitType is string text && text.StartsWith("LCK_M_", StringComparison.Ordinal)) return true;
            await Task.Delay(25);
        }
        return false;
    }

    private static async Task CleanupPostedIssueFixtureAsync(PostedIssueFixture fixture)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction,
                "SET QUOTED_IDENTIFIER ON; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryMovement_RebuildDeadLetter WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId; IF OBJECT_ID(N'dbo.Inventory_Report_Scope_Catalog', N'U') IS NOT NULL DELETE FROM dbo.Inventory_Report_Scope_Catalog WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventorySnapshot_ReportFallbackLog WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", fixture.WarehouseId), Text("@Login", fixture.Login, 100));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CleanupNewScopeReportFixtureAsync(NewScopeReportFixture fixture)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await ExecuteAsync(connection, transaction,
                "SET QUOTED_IDENTIFIER ON; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryMovement_RebuildDeadLetter WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB); IF OBJECT_ID(N'dbo.Inventory_Report_Scope_Catalog', N'U') IS NOT NULL DELETE FROM dbo.Inventory_Report_Scope_Catalog WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventorySnapshot_ReportFallbackLog WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID IN (@WarehouseA, @WarehouseB);",
                BigInt("@WarehouseA", fixture.WarehouseA), BigInt("@WarehouseB", fixture.WarehouseB), Text("@Login", fixture.Login, 100));
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static Task CleanupNewScopeReportFixtureAsync(PagedNewScopeReportFixture fixture) =>
        CleanupNewScopeReportFixtureAsync(new NewScopeReportFixture(
            fixture.Tag, fixture.Login, fixture.WarehouseA, fixture.WarehouseB, fixture.ProductId, fixture.ProductId, fixture.DraftReceiptId, fixture.BusinessDate));

    private static async Task ExecuteStoredAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteStoredIdAsync(SqlConnection connection, SqlTransaction? transaction, string procedure, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection, transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
        return Convert.ToInt64(command.Parameters["@Auto_ID"].Value);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<long> LongScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt64(await ScalarAsync(connection, transaction, sql, parameters));
    private static async Task<int> IntScalarAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters) => Convert.ToInt32(await ScalarAsync(connection, transaction, sql, parameters));

    private static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.NVarChar, size) { Value = value };
    private static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
    private static SqlParameter BigIntOutput(string name, long value) => new(name, SqlDbType.BigInt) { Direction = ParameterDirection.InputOutput, Value = value };
    private static SqlParameter Bit(string name, bool value) => new(name, SqlDbType.Bit) { Value = value };
    private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    private static SqlParameter Date(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };
    private static SqlParameter Decimal(string name, decimal value) => new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };

    private sealed record PostedIssueFixture(string Tag, string Login, long WarehouseId, long ProductId, long SupplierId, long ReceiptId, long IssueId, long DetailId);
    private sealed record NewScopeReportFixture(string Tag, string Login, long WarehouseA, long WarehouseB, long ProductId, long PostedProductId, long DraftReceiptId, DateTime BusinessDate);
    private sealed record PagedNewScopeReportFixture(string Tag, string Login, long WarehouseA, long WarehouseB, long ProductId, long DraftReceiptId, DateTime BusinessDate);
}
