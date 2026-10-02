using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class PerformanceSeedTests
{
    [Fact]
    public void Seed_can_materialize_the_ten_million_row_upper_bound()
    {
        var v_Seed = File.ReadAllText(FindRepositoryPath("Database", "Performance", "WarehousePerformance.Seed.sql"));

        Assert.Contains("SELECT TOP (@RecordCount)", v_Seed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM E4 a CROSS JOIN E4 b", v_Seed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Seed_enables_the_session_options_required_by_the_warehouse_indexes()
    {
        var v_Seed = File.ReadAllText(FindRepositoryPath("Database", "Performance", "WarehousePerformance.Seed.sql"));

        Assert.Contains("SET QUOTED_IDENTIFIER ON", v_Seed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Seed_creates_the_authenticated_user_fixture_required_by_warehouse_crud()
    {
        var v_Seed = File.ReadAllText(FindRepositoryPath("Database", "Performance", "WarehousePerformance.Seed.sql"));

        Assert.Contains("CREATE TABLE dbo.tbl_Sys_Thanh_Vien", v_Seed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PERF_USER", v_Seed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bulk_detail_invalidation_deduplicates_the_affected_scope()
    {
        var v_Procedures = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));

        Assert.Contains(
            "SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Nhap_Kho, N'DOCUMENT_UPDATE'",
            v_Procedures,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Xuat_Kho, N'DOCUMENT_UPDATE'",
            v_Procedures,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_Directory = new DirectoryInfo(AppContext.BaseDirectory); v_Directory is not null; v_Directory = v_Directory.Parent)
        {
            var v_Candidate = Path.Combine(new[] { v_Directory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_Candidate))
                return v_Candidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
