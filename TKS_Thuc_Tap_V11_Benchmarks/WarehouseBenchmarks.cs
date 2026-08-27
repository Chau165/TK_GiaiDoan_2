using System.Data;
using BenchmarkDotNet.Attributes;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Benchmarks;

[MemoryDiagnoser]
public class WarehouseSyntheticBenchmarks
{
    private DataTable m_table = null!;

    [ParamsSource(nameof(RecordCounts))]
    public int RecordCount { get; set; }

    public IEnumerable<int> RecordCounts => new[] { BenchmarkSettings.FromEnvironment().RecordCount };

    [GlobalSetup]
    public void Setup()
    {
        m_table = CreateSyntheticTable(RecordCount);
    }

    [Benchmark(Baseline = true)]
    public int MapRowsUsingApplicationReflection()
    {
        return MapRows(m_table);
    }

    [Benchmark]
    public DataTable MaterializeSyntheticDataTable()
    {
        return CreateSyntheticTable(RecordCount);
    }

    [Benchmark]
    public int MaterializeAndMapSyntheticRows()
    {
        return MapRows(CreateSyntheticTable(RecordCount));
    }

    private static int MapRows(DataTable p_table)
    {
        var v_count = 0;
        foreach (DataRow v_row in p_table.Rows)
        {
            var v_item = CUtility.Map_Row_To_Entity<CWarehouseMaster>(v_row);
            v_count += v_item.Auto_ID > 0 ? 1 : 0;
        }

        return v_count;
    }

    private static DataTable CreateSyntheticTable(int p_recordCount)
    {
        var v_table = new DataTable();
        v_table.Columns.Add("Auto_ID", typeof(long));
        v_table.Columns.Add("Code", typeof(string));
        v_table.Columns.Add("Name", typeof(string));
        v_table.Columns.Add("Related_ID", typeof(long));
        v_table.Columns.Add("Related_ID_2", typeof(long));
        v_table.Columns.Add("Login_Name", typeof(string));
        v_table.Columns.Add("Ghi_Chu", typeof(string));

        for (var v_index = 1; v_index <= p_recordCount; v_index++)
        {
            v_table.Rows.Add(
                (long)v_index,
                $"SP-{v_index:0000000}",
                $"Synthetic product {v_index}",
                (long)((v_index % 100) + 1),
                (long)((v_index % 50) + 1),
                "",
                "");
        }

        return v_table;
    }
}

[MemoryDiagnoser]
public class WarehouseDatabaseBenchmarks
{
    private WarehouseReadOperations m_operations = null!;

    [GlobalSetup]
    public void Setup()
    {
        var v_settings = BenchmarkSettings.FromEnvironment();
        v_settings.RequireDatabase();
        m_operations = new WarehouseReadOperations(v_settings);
        ValidateDatabaseAsync(v_settings.ConnectionString).GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public Task<int> MasterPaged() => m_operations.MasterPagedAsync();

    [Benchmark]
    public Task<int> LookupPaged() => m_operations.LookupPagedAsync();

    [Benchmark]
    public Task<int> DocumentPaged() => m_operations.DocumentPagedAsync();

    [Benchmark]
    public Task<int> DetailReportPaged() => m_operations.DetailReportPagedAsync();

    [Benchmark]
    public Task<int> InventoryReportPaged() => m_operations.InventoryReportPagedAsync();

    private static async Task ValidateDatabaseAsync(string p_connectionString)
    {
        await using var v_connection = new Microsoft.Data.SqlClient.SqlConnection(p_connectionString);
        await v_connection.OpenAsync();
        await using var v_command = v_connection.CreateCommand();
        v_command.CommandText = "SELECT DB_NAME();";
        _ = await v_command.ExecuteScalarAsync();
    }
}
