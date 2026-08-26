using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class PerformanceSeedTests
{
    [Fact]
    public void Seed_can_materialize_the_ten_million_row_upper_bound()
    {
        var seed = File.ReadAllText(FindRepositoryPath("Database", "Performance", "WarehousePerformance.Seed.sql"));

        Assert.Contains("SELECT TOP (@RecordCount)", seed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM E4 a CROSS JOIN E4 b", seed, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryPath(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(parts)}");
    }
}
