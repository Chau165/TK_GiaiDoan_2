using System.IO;
using System.Text;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

/// <summary>
/// TDD reproducer for Đơn vị tính duplicate message mojibake.
/// Scenario: Add "Cái" then add "CÁI" -> should throw duplicate with correct Unicode message "Tên đơn vị tính đã tồn tại."
/// Root cause: WarehouseModule.Procedures.sql was UTF-8 without BOM, so sqlcmd without -f 65001 stores mojibake.
/// Also duplicate check must be case-insensitive regardless of DB collation.
/// </summary>
public class WarehouseUnitDuplicateEncodingTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TKS_Thuc_Tap_V11.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    [Fact]
    public void Procedures_file_must_be_utf8_bom_to_avoid_mojibake()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "Database", "WarehouseModule.Procedures.sql");
        Assert.True(File.Exists(path), $"File not found: {path}");
        var bytes = File.ReadAllBytes(path);
        // Must start with UTF-8 BOM EF BB BF so sqlcmd -f 65001 and SSMS detect UTF-8
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "WarehouseModule.Procedures.sql must be saved as UTF-8 with BOM to prevent mojibake when deployed via sqlcmd/SSMS");
    }

    [Fact]
    public void Procedures_file_must_not_contain_mojibake_patterns()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "Database", "WarehouseModule.Procedures.sql");
        var text = File.ReadAllText(path, Encoding.UTF8);
        // These fragments appear when UTF-8 Vietnamese is mis-interpreted as ANSI
        Assert.DoesNotContain("Ã", text);
        Assert.DoesNotContain("Ä", text);
        Assert.DoesNotContain("Â", text);
        // Correct message must be present verbatim
        Assert.Contains("N'Tên đơn vị tính đã tồn tại.'", text);
        Assert.Contains("N'Tên đơn vị tính không được để trống.'", text);
    }

    [Fact]
    public void DonViTinh_duplicate_check_must_be_case_insensitive()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "Database", "WarehouseModule.Procedures.sql");
        var text = File.ReadAllText(path, Encoding.UTF8);
        // Must use explicit COLLATE CI_AI so "Cái" vs "CÁI" is considered duplicate even if DB collation is CS or BIN
        // Require at least one occurrence of COLLATE with CI in the duplicate check
        Assert.Contains("COLLATE", text);
        // Duplicate check line must reference Ten_Don_Vi_Tinh with COLLATE
        var hasCiDuplicate = text.Contains("Ten_Don_Vi_Tinh COLLATE") && text.Contains("THROW 51002");
        Assert.True(hasCiDuplicate, "sp_DM_Don_Vi_Tinh_Save must use COLLATE Vietnamese_CI_AI or Latin1_General_CI_AI for case-insensitive duplicate detection (Cái vs CÁI)");
    }

    [Fact]
    public void Schema_sample_and_schema_files_must_be_unicode_safe()
    {
        var root = FindRepoRoot();
        foreach (var rel in new[] { "Database/WarehouseModule.Schema.sql", "Database/WarehouseModule.SampleData.sql" })
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(path, Encoding.UTF8);
            // No mojibake markers
            Assert.DoesNotContain("Ã", text);
            // Sample data must contain correct N'Cái' etc using N'' prefix
            if (rel.Contains("SampleData"))
                Assert.Contains("N'Cái'", text);
        }
    }
}
