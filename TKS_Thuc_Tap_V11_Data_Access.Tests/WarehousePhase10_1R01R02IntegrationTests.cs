using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

[Collection("Warehouse inventory database")]
public sealed class WarehousePhase10_1R01R02IntegrationTests
{
    private const string DmlProbeUser = "Phase101DmlProbe";
    private const string ApplicationUser = "Phase101Application";
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Fact]
    public async Task R01_session_context_cannot_bypass_any_posted_boundary()
    {
        var v_Fixture = await CreatePostedIssueFixtureAsync();
        await EnsureDmlProbeUserAsync();

        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        var v_bImpersonated = false;
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                $"EXECUTE AS USER = N'{DmlProbeUser}'; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            v_bImpersonated = true;

            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, 1, 1);",
                BigInt("@IssueId", v_Fixture.IssueId),
                BigInt("@ProductId", v_Fixture.ProductId));
            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat = SL_Xuat + 1 WHERE Auto_ID = @DetailId;",
                BigInt("@DetailId", v_Fixture.DetailId));
            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "DELETE dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @DetailId;",
                BigInt("@DetailId", v_Fixture.DetailId));

            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@ReceiptId, @ProductId, 1, 1);",
                BigInt("@ReceiptId", v_Fixture.ReceiptId), BigInt("@ProductId", v_Fixture.ProductId));
            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap = SL_Nhap + 1 WHERE Auto_ID = (SELECT TOP (1) Auto_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId);",
                BigInt("@ReceiptId", v_Fixture.ReceiptId));
            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "DELETE dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = (SELECT TOP (1) Auto_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @ReceiptId);",
                BigInt("@ReceiptId", v_Fixture.ReceiptId));

            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "UPDATE dbo.tbl_XNK_Xuat_Kho SET Ghi_Chu = N'blocked' WHERE Auto_ID = @IssueId;",
                BigInt("@IssueId", v_Fixture.IssueId));
            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "DELETE dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @IssueId;",
                BigInt("@IssueId", v_Fixture.IssueId));
            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @BusinessDate, 1, N'blocked');",
                Text("@Number", $"{v_Fixture.Tag}-blocked-issue", 100), BigInt("@WarehouseId", v_Fixture.WarehouseId), Date("@BusinessDate", DateTime.Today));

            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "UPDATE dbo.tbl_XNK_Nhap_Kho SET Ghi_Chu = N'blocked' WHERE Auto_ID = @ReceiptId;",
                BigInt("@ReceiptId", v_Fixture.ReceiptId));
            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "DELETE dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId;",
                BigInt("@ReceiptId", v_Fixture.ReceiptId));
            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @BusinessDate, 1, N'blocked');",
                Text("@Number", $"{v_Fixture.Tag}-blocked-receipt", 100), BigInt("@WarehouseId", v_Fixture.WarehouseId), BigInt("@SupplierId", v_Fixture.SupplierId), Date("@BusinessDate", DateTime.Today));
        }
        finally
        {
            if (v_bImpersonated)
            {
                try { await ExecuteAsync(v_Connection, v_Transaction, "REVERT;"); } catch { }
            }

            try { await v_Transaction.RollbackAsync(); } catch { }
            await CleanupPostedIssueFixtureAsync(v_Fixture);
            await DropDmlProbeUserAsync();
        }
    }

    [Fact]
    public async Task R01_application_role_denies_direct_dml_and_allows_canonical_save_post()
    {
        var v_Fixture = await CreatePostedIssueFixtureAsync();
        await EnsureApplicationUserAsync();

        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        var v_bImpersonated = false;
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                $"EXECUTE AS USER = N'{ApplicationUser}'; EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            v_bImpersonated = true;

            var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
                v_Connection,
                v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES (@IssueId, @ProductId, 1, 1);",
                BigInt("@IssueId", v_Fixture.IssueId), BigInt("@ProductId", v_Fixture.ProductId)));
            Assert.True(v_Error.Number is 229 or 51228, $"Unexpected direct-DML rejection number: {v_Error.Number}");

            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "UPDATE dbo.InventoryBalance_Current SET CurrentQuantity = CurrentQuantity WHERE Kho_ID = @WarehouseId AND San_Pham_ID = @ProductId;",
                BigInt("@WarehouseId", v_Fixture.WarehouseId), BigInt("@ProductId", v_Fixture.ProductId));
            await AssertDirectMutationRejectedAsync(v_Connection, v_Transaction,
                "DELETE FROM dbo.InventoryReservation_Current WHERE 1 = 0;");

            var receiptId = await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2011_sp_ins_Nhap_Kho_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{v_Fixture.Tag}-app-receipt", 100), BigInt("@Kho_ID", v_Fixture.WarehouseId),
                BigInt("@NCC_ID", v_Fixture.SupplierId), Date("@Ngay_Nhap_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000), Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
                Text("@Created_By", v_Fixture.Login, 100), Text("@Created_By_Function", "Phase10.1-App", 100), Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));
            await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2011_sp_ins_Nhap_Kho_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", v_Fixture.ProductId), Decimal("@SL_Nhap", 1m), Decimal("@Don_Gia_Nhap", 1m),
                Text("@Ma_Dang_Nhap", v_Fixture.Login, 100), Text("@Created_By", v_Fixture.Login, 100), Text("@Created_By_Function", "Phase10.1-App", 100),
                Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));
            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
                Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));

            var issueId = await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2012_sp_ins_Xuat_Kho_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Xuat_Kho", $"{v_Fixture.Tag}-app-issue", 100), BigInt("@Kho_ID", v_Fixture.WarehouseId),
                Date("@Ngay_Xuat_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000), Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
                Text("@Created_By", v_Fixture.Login, 100), Text("@Created_By_Function", "Phase10.1-App", 100), Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));
            await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2012_sp_ins_Xuat_Kho_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Xuat_Kho_ID", issueId), BigInt("@San_Pham_ID", v_Fixture.ProductId), Decimal("@SL_Xuat", 1m), Decimal("@Don_Gia_Xuat", 1m),
                Text("@Ma_Dang_Nhap", v_Fixture.Login, 100), Text("@Created_By", v_Fixture.Login, 100), Text("@Created_By_Function", "Phase10.1-App", 100),
                Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));
            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", false), BigInt("@Document_ID", issueId), Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
                Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100));

            var v_RepostError = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", false), BigInt("@Document_ID", issueId), Text("@Ma_Dang_Nhap", v_Fixture.Login, 100),
                Text("@Last_Updated_By", v_Fixture.Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-App", 100)));
            Assert.Equal(51162, v_RepostError.Number);
        }
        finally
        {
            if (v_bImpersonated)
            {
                try { await ExecuteAsync(v_Connection, v_Transaction, "REVERT;"); } catch { }
            }

            try { await v_Transaction.RollbackAsync(); } catch { }
            await CleanupPostedIssueFixtureAsync(v_Fixture);
            await DropApplicationUserAsync();
        }
    }

    [Fact]
    public async Task R01_session_context_does_not_leak_across_pooled_connections()
    {
        SqlConnection.ClearAllPools();
        var v_PooledConnectionString = new SqlConnectionStringBuilder(ConnectionString)
        {
            MaxPoolSize = 1,
            MinPoolSize = 0
        }.ConnectionString;

        try
        {
            await using (var v_First = new SqlConnection(v_PooledConnectionString))
            {
                await v_First.OpenAsync();
                await ExecuteAsync(v_First, null,
                    "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            }

            await using (var v_Second = new SqlConnection(v_PooledConnectionString))
            {
                await v_Second.OpenAsync();
                var v_objLeakedMarker = await ScalarAsync(v_Second, null,
                    "SELECT SESSION_CONTEXT(N'InventoryMovement:ManagedPost');");
                Assert.True(v_objLeakedMarker is null or DBNull,
                    $"Trusted marker leaked across pooled connections: {v_objLeakedMarker}");
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
        var v_Fixture = await CreateNewScopeReportFixtureAsync();
        await using var v_Locker = new SqlConnection(ConnectionString);
        await using var v_Report = new SqlConnection(ConnectionString);
        await using var v_Post = new SqlConnection(ConnectionString);
        await v_Locker.OpenAsync();
        await v_Report.OpenAsync();
        await v_Post.OpenAsync();
        await using var v_LockerTransaction = v_Locker.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Locker, v_LockerTransaction,
                "SELECT COUNT(*) FROM dbo.InventorySnapshot_ReportFallbackLog WITH (TABLOCKX, HOLDLOCK);");

            var v_iReportSpid = await IntScalarAsync(v_Report, null, "SELECT @@SPID;");
            var v_ReportTask = ReadNonPagedReportAsync(v_Report, v_Fixture);
            Assert.True(await WaitForLockWaitAsync(v_iReportSpid), "Report did not pause after scope discovery/readiness.");

            await ExecuteStoredAsync(v_Post, null, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", v_Fixture.DraftReceiptId),
                Text("@Ma_Dang_Nhap", v_Fixture.Login, 100), Text("@Last_Updated_By", v_Fixture.Login, 100),
                Text("@Last_Updated_By_Function", "Phase10.1-R02", 100));

            await v_LockerTransaction.RollbackAsync();
            var v_Error = await Assert.ThrowsAsync<SqlException>(async () => await v_ReportTask);
            Assert.Equal(51324, v_Error.Number);
        }
        finally
        {
            try { await v_LockerTransaction.RollbackAsync(); } catch { }
            await CleanupNewScopeReportFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task R02_paged_report_rejects_new_scope_after_scope_materialization_before_count()
    {
        var v_Fixture = await CreatePagedNewScopeReportFixtureAsync();
        await using var v_Locker = new SqlConnection(ConnectionString);
        await using var v_Report = new SqlConnection(ConnectionString);
        await using var v_Post = new SqlConnection(ConnectionString);
        await v_Locker.OpenAsync();
        await v_Report.OpenAsync();
        await v_Post.OpenAsync();
        await using var v_LockerTransaction = v_Locker.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Locker, v_LockerTransaction,
                "SELECT COUNT(*) FROM dbo.Inventory_Balance_Daily WITH (TABLOCKX, HOLDLOCK);");

            var v_iReportSpid = await IntScalarAsync(v_Report, null, "SELECT @@SPID;");
            var v_ReportTask = ReadPagedReportAsync(v_Report, v_Fixture);
            Assert.True(await WaitForLockWaitAsync(v_iReportSpid), "Paged report did not pause after scope materialization before COUNT.");

            await ExecuteStoredAsync(v_Post, null, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", v_Fixture.DraftReceiptId),
                Text("@Ma_Dang_Nhap", v_Fixture.Login, 100), Text("@Last_Updated_By", v_Fixture.Login, 100),
                Text("@Last_Updated_By_Function", "Phase10.1-R02-Paged", 100));

            await v_LockerTransaction.RollbackAsync();
            var v_Error = await Assert.ThrowsAsync<SqlException>(async () => await v_ReportTask);
            Assert.Equal(51324, v_Error.Number);
        }
        finally
        {
            try { await v_LockerTransaction.RollbackAsync(); } catch { }
            await CleanupNewScopeReportFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task R02_current_report_holds_group_fence_against_concurrent_post()
    {
        var v_Fixture = await CreateNewScopeReportFixtureAsync();
        await using var v_Locker = new SqlConnection(ConnectionString);
        await using var v_Report = new SqlConnection(ConnectionString);
        await using var v_Post = new SqlConnection(ConnectionString);
        await v_Locker.OpenAsync();
        await v_Report.OpenAsync();
        await v_Post.OpenAsync();
        await using var v_LockerTransaction = v_Locker.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Locker, v_LockerTransaction,
                "SELECT Auto_ID FROM dbo.tbl_DM_San_Pham WITH (PAGLOCK, XLOCK, HOLDLOCK) WHERE Auto_ID = @ProductId;",
                BigInt("@ProductId", v_Fixture.ProductId));

            var v_iReportSpid = await IntScalarAsync(v_Report, null, "SELECT @@SPID;");
            var v_ReportTask = ReadCurrentReportAsync(v_Report, v_Fixture);
            Assert.True(await WaitForLockWaitAsync(v_iReportSpid), "Current report did not pause during page materialization.");

            var v_PostError = await Assert.ThrowsAsync<SqlException>(() => ExecuteStoredAsync(v_Post, null, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", v_Fixture.DraftReceiptId),
                Text("@Ma_Dang_Nhap", v_Fixture.Login, 100), Text("@Last_Updated_By", v_Fixture.Login, 100),
                Text("@Last_Updated_By_Function", "Phase10.2-P101-A02", 100)));
            Assert.Equal(51407, v_PostError.Number);
            Assert.Contains("Inventory fence Group acquisition failed", v_PostError.Message, StringComparison.Ordinal);

            await v_LockerTransaction.RollbackAsync();
            _ = await v_ReportTask;

            Assert.Equal(0, await IntScalarAsync(v_Post, null,
                "SELECT CONVERT(INT, Is_Posted) FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @ReceiptId;",
                BigInt("@ReceiptId", v_Fixture.DraftReceiptId)));
        }
        finally
        {
            try { await v_LockerTransaction.RollbackAsync(); } catch { }
            await CleanupNewScopeReportFixtureAsync(v_Fixture);
        }
    }

    [Fact]
    public async Task R02_current_generation_advances_for_reservation_projection_writes()
    {
        var v_Fixture = await CreatePostedIssueFixtureAsync();
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();

        try
        {
            var before = await LongScalarAsync(v_Connection, null,
                "SELECT Generation FROM dbo.Inventory_Current_Report_State WHERE State_ID = 1;");
            await ExecuteStoredAsync(v_Connection, null, "dbo.sp_XNK_Reservation_Adjust",
                BigInt("@Kho_ID", v_Fixture.WarehouseId), BigInt("@San_Pham_ID", v_Fixture.ProductId), Decimal("@Delta", 1m));
            var afterReserve = await LongScalarAsync(v_Connection, null,
                "SELECT Generation FROM dbo.Inventory_Current_Report_State WHERE State_ID = 1;");
            await ExecuteStoredAsync(v_Connection, null, "dbo.sp_XNK_Reservation_Adjust",
                BigInt("@Kho_ID", v_Fixture.WarehouseId), BigInt("@San_Pham_ID", v_Fixture.ProductId), Decimal("@Delta", -1m));
            var afterRelease = await LongScalarAsync(v_Connection, null,
                "SELECT Generation FROM dbo.Inventory_Current_Report_State WHERE State_ID = 1;");

            Assert.True(afterReserve > before);
            Assert.True(afterRelease > afterReserve);
        }
        finally
        {
            await CleanupPostedIssueFixtureAsync(v_Fixture);
        }
    }

    private static async Task<PostedIssueFixture> CreatePostedIssueFixtureAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            var v_Tag = $"P101-R01-{Guid.NewGuid():N}"[..40];
            var productId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseId = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R01');",
                Text("@Name", v_Tag, 255));
            var v_Login = $"{v_Tag}-login";
            var memberId = await LongScalarAsync(v_Connection, v_Transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10.1 R01', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                BigInt("@MemberId", memberId), Text("@Login", v_Login, 100), BigInt("@WarehouseId", warehouseId));

            var receiptId = await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2011_sp_ins_Nhap_Kho_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{v_Tag}-receipt", 100), BigInt("@Kho_ID", warehouseId),
                BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000),
                Text("@Ma_Dang_Nhap", v_Login, 100), Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2011_sp_ins_Nhap_Kho_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", receiptId), BigInt("@San_Pham_ID", productId),
                Decimal("@SL_Nhap", 10m), Decimal("@Don_Gia_Nhap", 1m), Text("@Ma_Dang_Nhap", v_Login, 100),
                Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", true), BigInt("@Document_ID", receiptId), Text("@Ma_Dang_Nhap", v_Login, 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));

            var issueId = await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2012_sp_ins_Xuat_Kho_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Xuat_Kho", $"{v_Tag}-issue", 100), BigInt("@Kho_ID", warehouseId),
                Date("@Ngay_Xuat_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000), Text("@Ma_Dang_Nhap", v_Login, 100),
                Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            var detailId = await ExecuteStoredIdAsync(v_Connection, v_Transaction, "dbo.F2012_sp_ins_Xuat_Kho_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Xuat_Kho_ID", issueId), BigInt("@San_Pham_ID", productId),
                Decimal("@SL_Xuat", 2m), Decimal("@Don_Gia_Xuat", 1m), Text("@Ma_Dang_Nhap", v_Login, 100),
                Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            await ExecuteStoredAsync(v_Connection, v_Transaction, "dbo.sp_XNK_Document_Post",
                Bit("@Is_Receipt", false), BigInt("@Document_ID", issueId), Text("@Ma_Dang_Nhap", v_Login, 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));

            await v_Transaction.CommitAsync();
            return new PostedIssueFixture(v_Tag, v_Login, warehouseId, productId, supplierId, receiptId, issueId, detailId);
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<NewScopeReportFixture> CreateNewScopeReportFixtureAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            var v_Tag = $"P101-R02-{Guid.NewGuid():N}"[..40];
            var productId = await LongScalarAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) p.Auto_ID FROM dbo.tbl_DM_San_Pham p CROSS APPLY sys.fn_PhysLocCracker(p.%%physloc%%) loc ORDER BY loc.page_id, p.Auto_ID;");
            var postedProductId = await LongScalarAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) p.Auto_ID FROM dbo.tbl_DM_San_Pham p CROSS APPLY sys.fn_PhysLocCracker(p.%%physloc%%) loc WHERE loc.page_id <> (SELECT TOP (1) loc2.page_id FROM dbo.tbl_DM_San_Pham p2 CROSS APPLY sys.fn_PhysLocCracker(p2.%%physloc%%) loc2 WHERE p2.Auto_ID = @ReportProductId) ORDER BY loc.page_id, p.Auto_ID;",
                BigInt("@ReportProductId", productId));
            var supplierId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R02 A');",
                Text("@Name", $"{v_Tag}-A", 255));
            var warehouseB = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R02 B');",
                Text("@Name", $"{v_Tag}-B", 255));
            var v_Login = $"{v_Tag}-login";
            var memberId = await LongScalarAsync(v_Connection, v_Transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10.1 R02', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseA), (@Login, @WarehouseB); INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseA, @ProductId, 10, 0);",
                BigInt("@MemberId", memberId), Text("@Login", v_Login, 100), BigInt("@WarehouseA", warehouseA),
                BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId));

            await v_Transaction.CommitAsync();

            await using var v_DraftConnection = new SqlConnection(ConnectionString);
            await v_DraftConnection.OpenAsync();
            var draftReceiptId = await ExecuteStoredIdAsync(v_DraftConnection, null, "dbo.F2011_sp_ins_Nhap_Kho_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{v_Tag}-draft-receipt", 100), BigInt("@Kho_ID", warehouseB),
                BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", DateTime.Today), Text("@Ghi_Chu", "", 1000),
                Text("@Ma_Dang_Nhap", v_Login, 100), Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));
            await ExecuteStoredIdAsync(v_DraftConnection, null, "dbo.F2011_sp_ins_Nhap_Kho_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", draftReceiptId), BigInt("@San_Pham_ID", postedProductId),
                Decimal("@SL_Nhap", 10m), Decimal("@Don_Gia_Nhap", 1m), Text("@Ma_Dang_Nhap", v_Login, 100),
                Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "Phase10.1", 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1", 100));

            return new NewScopeReportFixture(v_Tag, v_Login, warehouseA, warehouseB, productId, postedProductId, draftReceiptId, DateTime.Today);
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<PagedNewScopeReportFixture> CreatePagedNewScopeReportFixtureAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            var v_Tag = $"P101-R02P-{Guid.NewGuid():N}"[..40];
            var productId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await LongScalarAsync(v_Connection, v_Transaction, "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R02 paged A');",
                Text("@Name", $"{v_Tag}-A", 255));
            var warehouseB = await LongScalarAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'Phase 10.1 R02 paged B');",
                Text("@Name", $"{v_Tag}-B", 255));
            var v_Login = $"{v_Tag}-login";
            var memberId = await LongScalarAsync(v_Connection, v_Transaction,
                "SELECT ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX);");
            var v_dtmBusinessDate = DateTime.Today.AddDays(-1);
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) VALUES (@MemberId, @Login, N'Phase 10.1 R02 paged', 0); INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseA), (@Login, @WarehouseB); INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseA, @ProductId, 100, 0), (@WarehouseB, @ProductId, 0, 0); INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@BusinessDate, @WarehouseA, @ProductId, 0, 100, 0, 100, 100, 0, 1); INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseA, @ProductId, @BusinessDate, @BusinessDate); INSERT dbo.Inventory_Report_Scope_Catalog(Kho_ID, San_Pham_ID, First_Posted_Date, Last_Posted_Date, Is_Current) VALUES (@WarehouseA, @ProductId, @BusinessDate, @BusinessDate, 1), (@WarehouseB, @ProductId, NULL, NULL, 0);",
                BigInt("@MemberId", memberId), Text("@Login", v_Login, 100), BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId), Date("@BusinessDate", v_dtmBusinessDate));
            await v_Transaction.CommitAsync();

            await using var v_DraftConnection = new SqlConnection(ConnectionString);
            await v_DraftConnection.OpenAsync();
            var draftReceiptId = await ExecuteStoredIdAsync(v_DraftConnection, null, "dbo.F2011_sp_ins_Nhap_Kho_Header",
                BigIntOutput("@Auto_ID", 0), Text("@So_Phieu_Nhap_Kho", $"{v_Tag}-draft-receipt", 100), BigInt("@Kho_ID", warehouseB),
                BigInt("@NCC_ID", supplierId), Date("@Ngay_Nhap_Kho", v_dtmBusinessDate), Text("@Ghi_Chu", "", 1000), Text("@Ma_Dang_Nhap", v_Login, 100),
                Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "Phase10.1-R02-Paged", 100), Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-R02-Paged", 100));
            await ExecuteStoredIdAsync(v_DraftConnection, null, "dbo.F2011_sp_ins_Nhap_Kho_Detail",
                BigIntOutput("@Auto_ID", 0), BigInt("@Nhap_Kho_ID", draftReceiptId), BigInt("@San_Pham_ID", productId), Decimal("@SL_Nhap", 10m), Decimal("@Don_Gia_Nhap", 1m),
                Text("@Ma_Dang_Nhap", v_Login, 100), Text("@Created_By", v_Login, 100), Text("@Created_By_Function", "Phase10.1-R02-Paged", 100),
                Text("@Last_Updated_By", v_Login, 100), Text("@Last_Updated_By_Function", "Phase10.1-R02-Paged", 100));

            return new PagedNewScopeReportFixture(v_Tag, v_Login, warehouseA, warehouseB, productId, draftReceiptId, v_dtmBusinessDate);
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task EnsureDmlProbeUserAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null,
                $"IF DATABASE_PRINCIPAL_ID(N'{DmlProbeUser}') IS NULL CREATE USER [{DmlProbeUser}] WITHOUT LOGIN; GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Nhap_Kho TO [{DmlProbeUser}]; GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Nhap_Kho_Raw_Data TO [{DmlProbeUser}]; GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Xuat_Kho TO [{DmlProbeUser}]; GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.tbl_XNK_Xuat_Kho_Raw_Data TO [{DmlProbeUser}];");
    }

    private static async Task EnsureApplicationUserAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null,
            $"IF DATABASE_PRINCIPAL_ID(N'{ApplicationUser}') IS NOT NULL BEGIN IF EXISTS (SELECT 1 FROM sys.database_role_members WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'Warehouse_Application') AND member_principal_id = DATABASE_PRINCIPAL_ID(N'{ApplicationUser}')) ALTER ROLE [Warehouse_Application] DROP MEMBER [{ApplicationUser}]; DROP USER [{ApplicationUser}]; END; CREATE USER [{ApplicationUser}] WITHOUT LOGIN; ALTER ROLE [Warehouse_Application] ADD MEMBER [{ApplicationUser}];");
    }

    private static async Task DropDmlProbeUserAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null, $"IF DATABASE_PRINCIPAL_ID(N'{DmlProbeUser}') IS NOT NULL DROP USER [{DmlProbeUser}];");
    }

    private static async Task DropApplicationUserAsync()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await ExecuteAsync(v_Connection, null,
            $"IF DATABASE_PRINCIPAL_ID(N'{ApplicationUser}') IS NOT NULL BEGIN IF EXISTS (SELECT 1 FROM sys.database_role_members WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'Warehouse_Application') AND member_principal_id = DATABASE_PRINCIPAL_ID(N'{ApplicationUser}')) ALTER ROLE [Warehouse_Application] DROP MEMBER [{ApplicationUser}]; DROP USER [{ApplicationUser}]; END;");
    }

    private static async Task AssertDirectMutationRejectedAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        var v_Error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
        Assert.True(v_Error.Number is 51228 or 547 or 229, $"Unexpected direct-DML rejection number: {v_Error.Number}");
    }

    private static async Task<List<long>> ReadNonPagedReportAsync(SqlConnection p_Connection, NewScopeReportFixture p_Fixture)
    {
        await using var v_Command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton", p_Connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        v_Command.Parameters.Add(Date("@Tu_Ngay", p_Fixture.BusinessDate));
        v_Command.Parameters.Add(Date("@Den_Ngay", p_Fixture.BusinessDate));
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        var v_arrRows = new List<long>();
        while (await v_Reader.ReadAsync()) v_arrRows.Add(v_Reader.GetInt64(v_Reader.GetOrdinal("San_Pham_ID")));
        return v_arrRows;
    }

    private static async Task<(int TotalCount, List<long> Rows)> ReadCurrentReportAsync(SqlConnection p_Connection, NewScopeReportFixture p_Fixture)
    {
        await using var v_Command = new SqlCommand("dbo.sp_BC_Ton_Kho_Hien_Tai_Page", p_Connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        v_Command.Parameters.Add(Int("@Page_Number", 1));
        v_Command.Parameters.Add(Int("@Page_Size", 10));
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var v_iTotalCount = v_Reader.GetInt32(0);
        Assert.True(await v_Reader.NextResultAsync());
        var v_arrRows = new List<long>();
        while (await v_Reader.ReadAsync()) v_arrRows.Add(v_Reader.GetInt64(v_Reader.GetOrdinal("San_Pham_ID")));
        return (v_iTotalCount, v_arrRows);
    }

    private static async Task<(int TotalCount, List<long> Rows)> ReadPagedReportAsync(SqlConnection p_Connection, PagedNewScopeReportFixture p_Fixture)
    {
        await using var v_Command = new SqlCommand("dbo.sp_BC_Xuat_Nhap_Ton_Page", p_Connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        v_Command.Parameters.Add(Date("@Tu_Ngay", p_Fixture.BusinessDate));
        v_Command.Parameters.Add(Date("@Den_Ngay", p_Fixture.BusinessDate));
        v_Command.Parameters.Add(Int("@Page_Number", 1));
        v_Command.Parameters.Add(Int("@Page_Size", 10));
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Fixture.Login, 100));
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var v_iTotalCount = v_Reader.GetInt32(0);
        Assert.True(await v_Reader.NextResultAsync());
        var v_arrRows = new List<long>();
        while (await v_Reader.ReadAsync()) v_arrRows.Add(v_Reader.GetInt64(v_Reader.GetOrdinal("San_Pham_ID")));
        return (v_iTotalCount, v_arrRows);
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
            if (v_objWaitType is string v_Text && v_Text.StartsWith("LCK_M_", StringComparison.Ordinal)) return true;
            await Task.Delay(25);
        }
        return false;
    }

    private static async Task CleanupPostedIssueFixtureAsync(PostedIssueFixture p_Fixture)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "SET QUOTED_IDENTIFIER ON; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryMovement_RebuildDeadLetter WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryReservation_Current WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID = @WarehouseId; IF OBJECT_ID(N'dbo.Inventory_Report_Scope_Catalog', N'U') IS NOT NULL DELETE FROM dbo.Inventory_Report_Scope_Catalog WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.InventorySnapshot_ReportFallbackLog WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID = @WarehouseId; DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID = @WarehouseId;",
                BigInt("@WarehouseId", p_Fixture.WarehouseId), Text("@Login", p_Fixture.Login, 100));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task CleanupNewScopeReportFixtureAsync(NewScopeReportFixture p_Fixture)
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();
        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "SET QUOTED_IDENTIFIER ON; DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryMovement_RebuildDeadLetter WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventorySnapshot_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.Inventory_Movement_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.Inventory_Balance_Daily WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.Inventory_Balance_Daily_Scope WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventoryBalance_Current WHERE Kho_ID IN (@WarehouseA, @WarehouseB); IF OBJECT_ID(N'dbo.Inventory_Report_Scope_Catalog', N'U') IS NOT NULL DELETE FROM dbo.Inventory_Report_Scope_Catalog WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.InventorySnapshot_ReportFallbackLog WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho_User WHERE Kho_ID IN (@WarehouseA, @WarehouseB); DELETE FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Login; DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID IN (@WarehouseA, @WarehouseB);",
                BigInt("@WarehouseA", p_Fixture.WarehouseA), BigInt("@WarehouseB", p_Fixture.WarehouseB), Text("@Login", p_Fixture.Login, 100));
            await v_Transaction.CommitAsync();
        }
        catch
        {
            await v_Transaction.RollbackAsync();
            throw;
        }
    }

    private static Task CleanupNewScopeReportFixtureAsync(PagedNewScopeReportFixture p_Fixture)
    {
        return CleanupNewScopeReportFixtureAsync(new NewScopeReportFixture(p_Fixture.Tag, p_Fixture.Login, p_Fixture.WarehouseA, p_Fixture.WarehouseB, p_Fixture.ProductId, p_Fixture.ProductId, p_Fixture.DraftReceiptId, p_Fixture.BusinessDate));
    }

    private static async Task ExecuteStoredAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteStoredIdAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Procedure, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
        return Convert.ToInt64(v_Command.Parameters["@Auto_ID"].Value);
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction) { CommandTimeout = 30 };
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction) { CommandTimeout = 30 };
        v_Command.Parameters.AddRange(p_arrParameters);
        return await v_Command.ExecuteScalarAsync();
    }

    private static async Task<long> LongScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt64(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
    }
    private static async Task<int> IntScalarAsync(SqlConnection p_Connection, SqlTransaction? p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        return Convert.ToInt32(await ScalarAsync(p_Connection, p_Transaction, p_Sql, p_arrParameters));
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
    private static SqlParameter Int(string p_Name, int p_iValue)
    {
        return new(p_Name, SqlDbType.Int)
        {
            Value = p_iValue
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

    private sealed record PostedIssueFixture(string Tag, string Login, long WarehouseId, long ProductId, long SupplierId, long ReceiptId, long IssueId, long DetailId);
    private sealed record NewScopeReportFixture(string Tag, string Login, long WarehouseA, long WarehouseB, long ProductId, long PostedProductId, long DraftReceiptId, DateTime BusinessDate);
    private sealed record PagedNewScopeReportFixture(string Tag, string Login, long WarehouseA, long WarehouseB, long ProductId, long DraftReceiptId, DateTime BusinessDate);
}
