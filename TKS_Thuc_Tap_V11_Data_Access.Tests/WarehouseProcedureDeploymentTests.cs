using System.Text.RegularExpressions;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseProcedureDeploymentTests
{
    private static readonly Regex ProcedureDeclaration = new(
        @"^\s*CREATE\s+OR\s+ALTER\s+PROCEDURE\s+(?:\[?dbo\]?\.)?\[?(?<name>[A-Za-z0-9_]+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void Warehouse_module_script_defines_each_procedure_once()
    {
        var declarations = ReadProcedureNames("Database/WarehouseModule.Procedures.sql");
        var duplicates = declarations
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ({group.Count()})")
            .OrderBy(value => value)
            .ToArray();

        Assert.True(duplicates.Length == 0,
            $"Duplicate procedure definitions: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Legacy_document_posting_script_defines_no_procedures()
    {
        var declarations = ReadProcedureNames("Database/WarehouseDocumentPosting.Procedures.sql");

        Assert.Empty(declarations);
    }

    private static IReadOnlyList<string> ReadProcedureNames(string relativePath)
    {
        var path = FindRepositoryFile(relativePath);
        Assert.True(File.Exists(path), $"File not found: {path}");
        return ProcedureDeclaration.Matches(File.ReadAllText(path))
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
