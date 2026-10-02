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
        var v_Dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (v_Dir != null && !File.Exists(Path.Combine(v_Dir.FullName, "TKS_Thuc_Tap_V11.sln")))
            v_Dir = v_Dir.Parent;
        return v_Dir?.FullName ?? AppContext.BaseDirectory;
    }

    [Fact]
    public void Procedures_file_must_be_utf8_bom_to_avoid_mojibake()
    {
        var v_Root = FindRepoRoot();
        var v_Path = Path.Combine(v_Root, "Database", "WarehouseModule.Procedures.sql");
        Assert.True(File.Exists(v_Path), $"File not found: {v_Path}");
        var v_arrBytes = File.ReadAllBytes(v_Path);
        // Must start with UTF-8 BOM EF BB BF so sqlcmd -f 65001 and SSMS detect UTF-8
        Assert.True(v_arrBytes.Length >= 3 && v_arrBytes[0] == 0xEF && v_arrBytes[1] == 0xBB && v_arrBytes[2] == 0xBF,
            "WarehouseModule.Procedures.sql must be saved as UTF-8 with BOM to prevent mojibake when deployed via sqlcmd/SSMS");
    }

    [Fact]
    public void Procedures_file_must_not_contain_mojibake_patterns()
    {
        var v_Root = FindRepoRoot();
        var v_Path = Path.Combine(v_Root, "Database", "WarehouseModule.Procedures.sql");
        var v_Text = File.ReadAllText(v_Path, Encoding.UTF8);
        // These fragments appear when UTF-8 Vietnamese is mis-interpreted as ANSI
        Assert.DoesNotContain("Ã", v_Text);
        Assert.DoesNotContain("Ä", v_Text);
        Assert.DoesNotContain("Â", v_Text);
        // Correct message must be present verbatim
        Assert.Contains("N'Tên đơn vị tính đã tồn tại.'", v_Text);
        Assert.Contains("N'Tên đơn vị tính không được để trống.'", v_Text);
    }

    [Fact]
    public void DonViTinh_duplicate_check_must_be_case_insensitive()
    {
        var v_Root = FindRepoRoot();
        var v_Path = Path.Combine(v_Root, "Database", "WarehouseModule.Procedures.sql");
        var v_Text = File.ReadAllText(v_Path, Encoding.UTF8);
        // Must use explicit COLLATE CI_AI so "Cái" vs "CÁI" is considered duplicate even if DB collation is CS or BIN
        // Require at least one occurrence of COLLATE with CI in the duplicate check
        Assert.Contains("COLLATE", v_Text);
        // Duplicate check line must reference Ten_Don_Vi_Tinh with COLLATE
        var v_bHasCiDuplicate = v_Text.Contains("Ten_Don_Vi_Tinh COLLATE") && v_Text.Contains("THROW 51002");
        Assert.True(v_bHasCiDuplicate, "F2016_sp_ins_Don_Vi_Tinh must use COLLATE Vietnamese_CI_AI or Latin1_General_CI_AI for case-insensitive duplicate detection (Cái vs CÁI)");
    }

    [Fact]
    public void Schema_sample_and_schema_files_must_be_unicode_safe()
    {
        var v_Root = FindRepoRoot();
        foreach (var v_Rel in new[] { "Database/WarehouseModule.Schema.sql", "Database/WarehouseModule.SampleData.sql" })
        {
            var v_Path = Path.Combine(v_Root, v_Rel.Replace('/', Path.DirectorySeparatorChar));
            var v_Text = File.ReadAllText(v_Path, Encoding.UTF8);
            // No mojibake markers
            Assert.DoesNotContain("Ã", v_Text);
            // Sample data must contain correct N'Cái' etc using N'' prefix
            if (v_Rel.Contains("SampleData"))
                Assert.Contains("N'Cái'", v_Text);
        }
    }
}
