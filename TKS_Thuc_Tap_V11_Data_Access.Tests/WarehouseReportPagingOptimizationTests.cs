using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseReportPagingOptimizationTests
{
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Fact]
    public async Task Effective_report_procedures_use_narrow_paging_scopes_and_reuse_authorization()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();

        var v_InventoryDefinition = await ReadDefinitionAsync(v_Connection, "sp_BC_Xuat_Nhap_Ton_Page");
        var v_ReceiptDefinition = await ReadDefinitionAsync(v_Connection, "sp_BC_Chi_Tiet_Nhap_Page");
        var v_IssueDefinition = await ReadDefinitionAsync(v_Connection, "sp_BC_Chi_Tiet_Xuat_Page");

        Assert.Contains("AuthorizedScope", v_InventoryDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CROSS APPLY", v_InventoryDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#WarehouseScopeResult_Snapshot", v_InventoryDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#ReportKeys", v_InventoryDefinition, StringComparison.OrdinalIgnoreCase);

        foreach (var v_Definition in new[] { v_ReceiptDefinition, v_IssueDefinition })
        {
            Assert.Contains("#AuthorizedWarehouse", v_Definition, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("#DetailScope", v_Definition, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SELECT COUNT(*) AS Total_Count", v_Definition, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OFFSET", v_Definition, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EXISTS", v_Definition, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Historical_scope_fence_uses_catalog_driven_materialization_with_projection_fallbacks()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();

        var v_Definition = await ReadDefinitionAsync(v_Connection, "sp_Inventory_Report_Acquire_Scope_Fence");
        var v_iMaterializationStart = v_Definition.IndexOf("INSERT #ReportFenceStateCandidate", StringComparison.OrdinalIgnoreCase);
        var v_iScopeMaterialization = v_Definition.IndexOf("INSERT #ReportScopeCandidate", v_iMaterializationStart, StringComparison.OrdinalIgnoreCase);

        Assert.True(v_iMaterializationStart >= 0);
        Assert.True(v_iScopeMaterialization > v_iMaterializationStart);

        var v_Materialization = v_Definition[v_iMaterializationStart..v_iScopeMaterialization];
        Assert.Contains("Inventory_Report_Scope_Catalog", v_Materialization, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Inventory_Balance_Daily_Scope", v_Materialization, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InventoryBalance_Snapshot_Daily", v_Materialization, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InventoryBalance_Current", v_Materialization, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tbl_XNK_Nhap_Kho h", v_Materialization, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tbl_XNK_Xuat_Kho h", v_Materialization, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_report_procedures_page_from_covering_indexes_without_full_scope_materialization()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();

        var v_ReceiptDefinition = await ReadDefinitionAsync(v_Connection, "sp_BC_Chi_Tiet_Nhap_Page");
        var v_IssueDefinition = await ReadDefinitionAsync(v_Connection, "sp_BC_Chi_Tiet_Xuat_Page");

        Assert.DoesNotContain("#DetailScope", v_ReceiptDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#DetailScope", v_IssueDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", v_ReceiptDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", v_IssueDefinition, StringComparison.OrdinalIgnoreCase);

        Assert.True(await IndexExistsAsync(v_Connection, "IX_tbl_XNK_Nhap_Kho_Report_Page"));
        Assert.True(await IndexExistsAsync(v_Connection, "IX_tbl_XNK_Xuat_Kho_Report_Page"));
    }

    [Fact]
    public async Task Paged_inventory_and_detail_reports_preserve_count_rows_and_warehouse_scope()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            var v_Tag = $"TDD-PAGING-{Guid.NewGuid():N}";
            var v_LoginA = $"{v_Tag}-A";
            var v_LoginB = $"{v_Tag}-B";
            var productId = await InsertIdAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await InsertIdAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-a", 255));
            var warehouseB = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-b", 255));

            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", v_LoginA, 100), BigInt("@WarehouseId", warehouseA));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", v_LoginB, 100), BigInt("@WarehouseId", warehouseB));

            var receiptA = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt-a", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId));
            var receiptB = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt-b", 100), BigInt("@WarehouseId", warehouseB), BigInt("@SupplierId", supplierId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 11, 1);",
                BigInt("@DocumentId", receiptA), BigInt("@ProductId", productId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 22, 1);",
                BigInt("@DocumentId", receiptB), BigInt("@ProductId", productId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES ('2026-09-10', @WarehouseA, @ProductId, 11, 0, 1), ('2026-09-10', @WarehouseB, @ProductId, 22, 0, 1); INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES ('2026-09-10', @WarehouseA, @ProductId, 0, 11, 0, 11, 11, 0, 1), ('2026-09-10', @WarehouseB, @ProductId, 0, 22, 0, 22, 22, 0, 1); INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseA, @ProductId, '2026-09-10', '2026-09-10'), (@WarehouseB, @ProductId, '2026-09-10', '2026-09-10');",
                BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId));

            var v_InventoryA = await ReadPagedInventoryAsync(v_Connection, v_Transaction, v_LoginA);
            Assert.Equal(1, v_InventoryA.TotalCount);
            Assert.Single(v_InventoryA.Rows);
            Assert.Equal(warehouseA, v_InventoryA.Rows[0].WarehouseId);
            Assert.Equal(11m, v_InventoryA.Rows[0].Received);

            var v_InventoryB = await ReadPagedInventoryAsync(v_Connection, v_Transaction, v_LoginB);
            Assert.Equal(1, v_InventoryB.TotalCount);
            Assert.Single(v_InventoryB.Rows);
            Assert.Equal(warehouseB, v_InventoryB.Rows[0].WarehouseId);
            Assert.Equal(22m, v_InventoryB.Rows[0].Received);

            var v_DetailA = await ReadPagedDetailAsync(v_Connection, v_Transaction, "sp_BC_Chi_Tiet_Nhap_Page", v_LoginA);
            Assert.Equal(1, v_DetailA.TotalCount);
            Assert.Single(v_DetailA.Rows);
            Assert.Equal($"{v_Tag}-receipt-a", v_DetailA.Rows[0].DocumentNumber);

            var v_DetailB = await ReadPagedDetailAsync(v_Connection, v_Transaction, "sp_BC_Chi_Tiet_Nhap_Page", v_LoginB);
            Assert.Equal(1, v_DetailB.TotalCount);
            Assert.Single(v_DetailB.Rows);
            Assert.Equal($"{v_Tag}-receipt-b", v_DetailB.Rows[0].DocumentNumber);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Selecting_a_warehouse_restricts_reports_and_rejects_unassigned_warehouse()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            await ExecuteAsync(v_Connection, v_Transaction,
                "EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;");
            var v_Tag = $"TDD-RF-{Guid.NewGuid():N}";
            var v_Login = $"{v_Tag}-login";
            var v_dtmReportDate = DateTime.Today;
            await InsertIdAsync(v_Connection, v_Transaction,
                "DECLARE @UserId BIGINT; SELECT @UserId = ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) OUTPUT INSERTED.Auto_ID VALUES (@UserId, @Login, @Name, 0);",
                Text("@Login", v_Login, 100), Text("@Name", $"{v_Tag}-user", 200));
            var productId = await InsertIdAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_San_Pham ORDER BY Auto_ID;");
            var supplierId = await InsertIdAsync(v_Connection, v_Transaction,
                "SELECT TOP (1) Auto_ID FROM dbo.tbl_DM_NCC ORDER BY Auto_ID;");
            var warehouseA = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-a", 255));
            var warehouseB = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-b", 255));
            var warehouseC = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho(Ten_Kho, Ghi_Chu) OUTPUT INSERTED.Auto_ID VALUES (@Name, N'');",
                Text("@Name", $"{v_Tag}-warehouse-c", 255));

            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", v_Login, 100), BigInt("@WarehouseId", warehouseA));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID) VALUES (@Login, @WarehouseId);",
                Text("@Login", v_Login, 100), BigInt("@WarehouseId", warehouseB));

            var receiptA = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt-a", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId), Date("@Date", v_dtmReportDate));
            var receiptB = await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, @Date, 1, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt-b", 100), BigInt("@WarehouseId", warehouseB), BigInt("@SupplierId", supplierId), Date("@Date", v_dtmReportDate));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 11, 1);",
                BigInt("@DocumentId", receiptA), BigInt("@ProductId", productId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES (@DocumentId, @ProductId, 22, 1);",
                BigInt("@DocumentId", receiptB), BigInt("@ProductId", productId));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, @Quantity, 0);",
                BigInt("@WarehouseId", warehouseA), BigInt("@ProductId", productId), Decimal("@Quantity", 11));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity) VALUES (@WarehouseId, @ProductId, @Quantity, 0);",
                BigInt("@WarehouseId", warehouseB), BigInt("@ProductId", productId), Decimal("@Quantity", 22));
            await ExecuteAsync(v_Connection, v_Transaction,
                "INSERT dbo.Inventory_Movement_Daily(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid) VALUES (@Date, @WarehouseA, @ProductId, 11, 0, 1), (@Date, @WarehouseB, @ProductId, 22, 0, 1); INSERT dbo.Inventory_Balance_Daily(Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity, TotalReceived, TotalIssued, ClosingQuantity, CumulativeReceived, CumulativeIssued, IsValid) VALUES (@Date, @WarehouseA, @ProductId, 0, 11, 0, 11, 11, 0, 1), (@Date, @WarehouseB, @ProductId, 0, 22, 0, 22, 22, 0, 1); INSERT dbo.Inventory_Balance_Daily_Scope(Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date) VALUES (@WarehouseA, @ProductId, @Date, @Date), (@WarehouseB, @ProductId, @Date, @Date);",
                Date("@Date", v_dtmReportDate), BigInt("@WarehouseA", warehouseA), BigInt("@WarehouseB", warehouseB), BigInt("@ProductId", productId));

            var v_Inventory = await ReadPagedInventoryAsync(v_Connection, v_Transaction, v_Login, warehouseA, v_dtmReportDate, v_dtmReportDate);
            Assert.Equal(1, v_Inventory.TotalCount);
            Assert.Equal(warehouseA, Assert.Single(v_Inventory.Rows).WarehouseId);

            var v_Detail = await ReadPagedDetailAsync(v_Connection, v_Transaction, "sp_BC_Chi_Tiet_Nhap_Page", v_Login, warehouseB, v_dtmReportDate, v_dtmReportDate);
            Assert.Equal(1, v_Detail.TotalCount);
            Assert.Equal($"{v_Tag}-receipt-b", Assert.Single(v_Detail.Rows).DocumentNumber);

            var v_Error = await Assert.ThrowsAsync<SqlException>(() =>
                ReadPagedInventoryAsync(v_Connection, v_Transaction, v_Login, warehouseC));
            Assert.Equal(51054, v_Error.Number);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    private static async Task<string> ReadDefinitionAsync(SqlConnection p_Connection, string p_ProcedureName)
    {
        await using var v_Command = new SqlCommand(
            "SELECT OBJECT_DEFINITION(OBJECT_ID(@ProcedureName));", p_Connection);
        v_Command.Parameters.Add(Text("@ProcedureName", $"dbo.{p_ProcedureName}", 256));
        return Convert.ToString(await v_Command.ExecuteScalarAsync()) ?? "";
    }

    private static async Task<bool> IndexExistsAsync(SqlConnection p_Connection, string p_IndexName)
    {
        await using var v_Command = new SqlCommand(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE name = @IndexName) THEN 1 ELSE 0 END;", p_Connection);
        v_Command.Parameters.Add(Text("@IndexName", p_IndexName, 256));
        return Convert.ToInt32(await v_Command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<(int TotalCount, List<InventoryRow> Rows)> ReadPagedInventoryAsync(
        SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Login, long? warehouseId = null, DateTime? p_dtmFrom = null, DateTime? p_dtmTo = null)
    {
        await using var v_Command = ReportCommand(p_Connection, p_Transaction, "sp_BC_Xuat_Nhap_Ton_Page", p_Login, warehouseId, p_dtmFrom, p_dtmTo);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var v_iTotalCount = v_Reader.GetInt32(0);
        Assert.True(await v_Reader.NextResultAsync());
        var v_arrRows = new List<InventoryRow>();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new InventoryRow(v_Reader.GetInt64(0), v_Reader.GetDecimal(6)));
        return (v_iTotalCount, v_arrRows);
    }

    private static async Task<(int TotalCount, List<DetailRow> Rows)> ReadPagedDetailAsync(
        SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Procedure, string p_Login, long? warehouseId = null, DateTime? p_dtmFrom = null, DateTime? p_dtmTo = null)
    {
        await using var v_Command = ReportCommand(p_Connection, p_Transaction, p_Procedure, p_Login, warehouseId, p_dtmFrom, p_dtmTo);
        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var v_iTotalCount = v_Reader.GetInt32(0);
        Assert.True(await v_Reader.NextResultAsync());
        var v_arrRows = new List<DetailRow>();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new DetailRow(v_Reader.GetString(1), v_Reader.GetDecimal(5)));
        return (v_iTotalCount, v_arrRows);
    }

    private static SqlCommand ReportCommand(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Procedure, string p_Login, long? warehouseId = null, DateTime? p_dtmFrom = null, DateTime? p_dtmTo = null)
    {
        var v_Command = new SqlCommand(p_Procedure, p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        v_Command.Parameters.Add(Date("@Tu_Ngay", p_dtmFrom ?? new DateTime(2026, 9, 1)));
        v_Command.Parameters.Add(Date("@Den_Ngay", p_dtmTo ?? new DateTime(2026, 9, 30)));
        v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Login, 100));
        v_Command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = warehouseId ?? (object)DBNull.Value });
        return v_Command;
    }

    private static async Task<long> InsertIdAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        return Convert.ToInt64(await v_Command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqlConnection p_Connection, SqlTransaction p_Transaction, string p_Sql, params SqlParameter[] p_arrParameters)
    {
        await using var v_Command = new SqlCommand(p_Sql, p_Connection, p_Transaction);
        v_Command.Parameters.AddRange(p_arrParameters);
        await v_Command.ExecuteNonQueryAsync();
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

    private sealed record DetailRow(string DocumentNumber, decimal Quantity);
    private sealed record InventoryRow(long WarehouseId, decimal Received);
}
