using System.Text.RegularExpressions;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseProcedureDeploymentTests
{
    private static readonly Regex m_ProcedureDeclaration = new(
        @"^\s*CREATE\s+OR\s+ALTER\s+PROCEDURE\s+(?:\[?dbo\]?\.)?\[?(?<name>[A-Za-z0-9_]+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void Warehouse_module_script_defines_each_procedure_once()
    {
        var v_arrDeclarations = ReadProcedureNames("Database/WarehouseModule.Procedures.sql");
        var v_arrDuplicates = v_arrDeclarations
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ({group.Count()})")
            .OrderBy(value => value)
            .ToArray();

        Assert.True(v_arrDuplicates.Length == 0,
            $"Duplicate procedure definitions: {string.Join(", ", v_arrDuplicates)}");
    }

    [Fact]
    public void Legacy_document_posting_script_defines_no_procedures()
    {
        var v_arrDeclarations = ReadProcedureNames("Database/WarehouseDocumentPosting.Procedures.sql");

        Assert.Empty(v_arrDeclarations);
    }

    private static IReadOnlyList<string> ReadProcedureNames(string p_RelativePath)
    {
        var v_Path = FindRepositoryFile(p_RelativePath);
        Assert.True(File.Exists(v_Path), $"File not found: {v_Path}");
        return m_ProcedureDeclaration.Matches(File.ReadAllText(v_Path))
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    private static string FindRepositoryFile(string p_RelativePath)
    {
        for (var v_Directory = new DirectoryInfo(AppContext.BaseDirectory); v_Directory is not null; v_Directory = v_Directory.Parent)
        {
            var v_Candidate = Path.Combine(v_Directory.FullName, p_RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(v_Candidate))
                return v_Candidate;
        }

        return Path.Combine(AppContext.BaseDirectory, p_RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
