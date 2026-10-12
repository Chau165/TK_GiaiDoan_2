using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
    public void DonViTinh_duplicate_checks_must_be_case_insensitive_and_accent_sensitive()
    {
        var v_Root = FindRepoRoot();
        var v_Path = Path.Combine(v_Root, "Database", "WarehouseModule.Procedures.sql");
        var v_Text = File.ReadAllText(v_Path, Encoding.UTF8);
        foreach (var v_strProcedure in new[] { "F2016_sp_ins_Don_Vi_Tinh", "F2016_sp_upd_Don_Vi_Tinh" })
        {
            var v_objMatch = Regex.Match(
                v_Text,
                $"(?ims)CREATE\\s+OR\\s+ALTER\\s+PROCEDURE\\s+dbo\\.{Regex.Escape(v_strProcedure)}(?<body>.*?)(?=^\\s*GO\\s*$|\\z)");

            Assert.True(v_objMatch.Success, $"Procedure definition not found: {v_strProcedure}");
            Assert.Contains("Ten_Don_Vi_Tinh COLLATE SQL_Latin1_General_CP1_CI_AS", v_objMatch.Groups["body"].Value);
            Assert.DoesNotContain("Latin1_General_CI_AI", v_objMatch.Groups["body"].Value);
        }
    }

    [Fact]
    public void DonViTinh_name_column_must_use_the_business_ci_as_collation()
    {
        var v_Root = FindRepoRoot();
        var v_Path = Path.Combine(v_Root, "Database", "WarehouseModule.Schema.sql");
        var v_Text = File.ReadAllText(v_Path, Encoding.UTF8);

        Assert.Contains("Ten_Don_Vi_Tinh NVARCHAR(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL", v_Text);
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
