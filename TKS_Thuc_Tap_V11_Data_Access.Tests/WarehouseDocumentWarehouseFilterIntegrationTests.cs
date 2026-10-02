using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseDocumentWarehouseFilterIntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            return WarehouseTestDatabase.ConnectionString;
        }
    }

    [Fact]
    public async Task Document_pages_filter_receipts_and_issues_by_selected_authorized_warehouse()
    {
        await using var v_Connection = new SqlConnection(ConnectionString);
        await v_Connection.OpenAsync();
        await using var v_Transaction = v_Connection.BeginTransaction();

        try
        {
            var v_Tag = $"TDD-DF-{Guid.NewGuid():N}";
            var v_Login = $"{v_Tag}-login";
            await InsertIdAsync(v_Connection, v_Transaction,
                "DECLARE @UserId BIGINT; SELECT @UserId = ISNULL(MAX(Auto_ID), 0) + 1 FROM dbo.tbl_Sys_Thanh_Vien WITH (TABLOCKX); INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID, Ma_Dang_Nhap, Ho_Ten, deleted) OUTPUT INSERTED.Auto_ID VALUES (@UserId, @Login, @Name, 0);",
                Text("@Login", v_Login, 100), Text("@Name", $"{v_Tag}-user", 200));
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

            await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 0, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt-a", 100), BigInt("@WarehouseId", warehouseA), BigInt("@SupplierId", supplierId));
            await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, @SupplierId, '2026-09-10', 0, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-receipt-b", 100), BigInt("@WarehouseId", warehouseB), BigInt("@SupplierId", supplierId));
            await InsertIdAsync(v_Connection, v_Transaction,
                "INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES (@Number, @WarehouseId, '2026-09-10', 0, N''); SELECT CONVERT(BIGINT, SCOPE_IDENTITY());",
                Text("@Number", $"{v_Tag}-issue-b", 100), BigInt("@WarehouseId", warehouseB));

            var v_AllReceipts = await ReadPagedDocumentsAsync(v_Connection, v_Transaction, true, v_Login);
            Assert.Equal(2, v_AllReceipts.TotalCount);
            Assert.Equal(2, v_AllReceipts.Rows.Count);

            var v_SelectedReceipt = await ReadPagedDocumentsAsync(v_Connection, v_Transaction, true, v_Login, warehouseA);
            Assert.Equal(1, v_SelectedReceipt.TotalCount);
            Assert.Equal($"{v_Tag}-receipt-a", Assert.Single(v_SelectedReceipt.Rows).DocumentNumber);

            var v_SelectedIssue = await ReadPagedDocumentsAsync(v_Connection, v_Transaction, false, v_Login, warehouseB);
            Assert.Equal(1, v_SelectedIssue.TotalCount);
            Assert.Equal($"{v_Tag}-issue-b", Assert.Single(v_SelectedIssue.Rows).DocumentNumber);

            var v_Error = await Assert.ThrowsAsync<SqlException>(() =>
                ReadPagedDocumentsAsync(v_Connection, v_Transaction, true, v_Login, warehouseC));
            Assert.Equal(51054, v_Error.Number);
        }
        finally
        {
            await v_Transaction.RollbackAsync();
        }
    }

    private static async Task<(int TotalCount, List<DocumentRow> Rows)> ReadPagedDocumentsAsync(
        SqlConnection p_Connection, SqlTransaction p_Transaction, bool p_bIsReceipt, string p_Login, long? warehouseId = null)
    {
        await using var v_Command = new SqlCommand("sp_XNK_Document_Page", p_Connection, p_Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        v_Command.Parameters.Add(new SqlParameter("@Is_Receipt", SqlDbType.Bit) { Value = p_bIsReceipt });
        v_Command.Parameters.Add(new SqlParameter("@Page_Number", SqlDbType.Int) { Value = 1 });
        v_Command.Parameters.Add(new SqlParameter("@Page_Size", SqlDbType.Int) { Value = 10 });
        v_Command.Parameters.Add(Text("@Search_Text", "", 100));
        v_Command.Parameters.Add(Text("@Ma_Dang_Nhap", p_Login, 100));
        v_Command.Parameters.Add(new SqlParameter("@Kho_ID", SqlDbType.BigInt) { Value = warehouseId ?? (object)DBNull.Value });

        await using var v_Reader = await v_Command.ExecuteReaderAsync();
        Assert.True(await v_Reader.ReadAsync());
        var v_iTotalCount = v_Reader.GetInt32(0);
        Assert.True(await v_Reader.NextResultAsync());
        var v_arrRows = new List<DocumentRow>();
        while (await v_Reader.ReadAsync())
            v_arrRows.Add(new DocumentRow(v_Reader.GetString(2), v_Reader.GetInt64(3)));
        return (v_iTotalCount, v_arrRows);
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

    private sealed record DocumentRow(string DocumentNumber, long WarehouseId);
}
