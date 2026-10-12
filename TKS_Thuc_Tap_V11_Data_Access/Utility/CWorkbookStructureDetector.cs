using System.Globalization;
namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed class CWorkbookStructureDetector
{
    private const int m_iHeaderScanLimit = 20;
    private readonly CImportValueMapper m_objValueMapper;

    public CWorkbookStructureDetector()
        : this(new CImportProfileProvider())
    {
    }

    public CWorkbookStructureDetector(CImportProfileProvider p_objProfiles)
    {
        ArgumentNullException.ThrowIfNull(p_objProfiles);
        m_objValueMapper = new CImportValueMapper(p_objProfiles);
    }

    public CWorkbookStructureDetectionResult Detect(CWorkbook_Xlsx_Workbook p_objWorkbook, CImportProfile p_objProfile)
    {
        ArgumentNullException.ThrowIfNull(p_objWorkbook);
        ArgumentNullException.ThrowIfNull(p_objProfile);

        var v_objResult = new CWorkbookStructureDetectionResult();
        if (p_objWorkbook.Worksheets.Count == 0)
        {
            v_objResult.AddError("UNSUPPORTED_FILE_STRUCTURE", "Workbook không có worksheet.");
            return v_objResult;
        }

        var v_arrCandidates = p_objWorkbook.Worksheets
            .SelectMany(v_objSheet => p_objProfile.Sheets
                .Select(v_objRole => FindCandidate(v_objSheet, p_objProfile.Code, v_objRole))
                .Where(v_objCandidate => v_objCandidate is not null))
            .Cast<Candidate>()
            .ToArray();

        if (p_objProfile.Structure == EImportWorkbookStructure.SingleTable)
            DetectSingleTable(p_objWorkbook, p_objProfile, v_arrCandidates, v_objResult);
        else
            DetectHeaderDetail(p_objWorkbook, p_objProfile, v_arrCandidates, v_objResult);

        foreach (var v_objSheet in v_objResult.SheetMappings.Where(v_objSheet =>
            v_objSheet.DataRows.Count > p_objProfile.MaxRows ||
            v_objSheet.DataRows.Any() && v_objSheet.DataRows.Max(v_objRow => v_objRow.RowNumber) - v_objSheet.HeaderRowNumber > p_objProfile.MaxRows))
        {
            v_objResult.AddError(
                "RESOURCE_LIMIT",
                $"Sheet '{v_objSheet.Name}' vượt quá giới hạn {p_objProfile.MaxRows} dòng dữ liệu.",
                v_objSheet.Name);
        }

        return v_objResult;
    }

    private void DetectSingleTable(
        CWorkbook_Xlsx_Workbook p_objWorkbook,
        CImportProfile p_objProfile,
        IReadOnlyList<Candidate> p_arrCandidates,
        CWorkbookStructureDetectionResult p_objResult)
    {
        if (p_arrCandidates.Count != 1 || p_objProfile.Sheets.Count != 1)
        {
            p_objResult.AddError(
                "UNSUPPORTED_FILE_STRUCTURE",
                "Cần đúng một bảng dữ liệu phù hợp với profile; workbook hiện có cấu trúc không xác định.");
            return;
        }

        var v_objCandidate = p_arrCandidates[0];
        if (HasUnmappedNonEmptySheets(p_objWorkbook, new[] { v_objCandidate.Sheet }))
        {
            p_objResult.AddError("UNSUPPORTED_FILE_STRUCTURE", "Workbook chứa worksheet dữ liệu không thuộc profile.");
            return;
        }

        AddMapping(v_objCandidate, p_objResult);
    }

    private void DetectHeaderDetail(
        CWorkbook_Xlsx_Workbook p_objWorkbook,
        CImportProfile p_objProfile,
        IReadOnlyList<Candidate> p_arrCandidates,
        CWorkbookStructureDetectionResult p_objResult)
    {
        var v_arrSelected = new List<Candidate>();
        foreach (var v_objRole in p_objProfile.Sheets)
        {
            var v_arrRoleCandidates = p_arrCandidates
                .Where(v_objCandidate => ReferenceEquals(v_objCandidate.Profile, v_objRole))
                .OrderByDescending(v_objCandidate => v_objCandidate.Score)
                .ThenByDescending(v_objCandidate => v_objCandidate.NameHintMatch)
                .ToArray();

            if (v_arrRoleCandidates.Length == 0)
            {
                if (v_objRole.Required)
                    p_objResult.AddError("UNSUPPORTED_FILE_STRUCTURE", $"Không tìm thấy sheet cho vai trò '{v_objRole.Role}'.");
                continue;
            }

            if (v_arrRoleCandidates.Length > 1 &&
                v_arrRoleCandidates[0].Score == v_arrRoleCandidates[1].Score &&
                v_arrRoleCandidates[0].NameHintMatch == v_arrRoleCandidates[1].NameHintMatch)
            {
                p_objResult.AddError("UNSUPPORTED_FILE_STRUCTURE", $"Có nhiều sheet cùng phù hợp vai trò '{v_objRole.Role}'.");
                return;
            }

            v_arrSelected.Add(v_arrRoleCandidates[0]);
        }

        if (p_objResult.Errors.Count > 0)
            return;
        if (v_arrSelected.Select(v_objCandidate => v_objCandidate.Sheet).Distinct().Count() != v_arrSelected.Count ||
            HasUnmappedNonEmptySheets(p_objWorkbook, v_arrSelected.Select(v_objCandidate => v_objCandidate.Sheet)))
        {
            p_objResult.AddError("UNSUPPORTED_FILE_STRUCTURE", "Các sheet không tạo thành đúng topology header/detail.");
            return;
        }

        foreach (var v_objCandidate in v_arrSelected)
            AddMapping(v_objCandidate, p_objResult);
    }

    private static Candidate? FindCandidate(CWorkbook_Xlsx_Sheet p_objSheet, string p_strProfileCode, CImportSheetProfile p_objRole)
    {
        Candidate? v_objBest = null;
        foreach (var v_objRow in p_objSheet.Rows.Where(v_objRow => v_objRow.RowNumber <= m_iHeaderScanLimit))
        {
            var v_setFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v_objCell in v_objRow.Cells)
            {
                var v_strHeader = HeaderText(v_objCell);
                if (TryGetField(p_objRole, v_strHeader, out var v_objField))
                    v_setFields.Add(v_objField.Field);
            }

            if (v_setFields.Count == 0)
                continue;

            var v_bNameHint = p_objRole.NameHints.Any(v_strHint =>
                CImportHeaderNormalizer.Normalize(p_objSheet.Name).Contains(
                    CImportHeaderNormalizer.Normalize(v_strHint), StringComparison.Ordinal));
            var v_objCandidate = new Candidate(p_objSheet, p_strProfileCode, p_objRole, v_objRow, v_setFields.Count, v_bNameHint);
            if (v_objBest is null || v_objCandidate.Score > v_objBest.Score ||
                (v_objCandidate.Score == v_objBest.Score && v_objCandidate.NameHintMatch && !v_objBest.NameHintMatch))
            {
                v_objBest = v_objCandidate;
            }
        }

        return v_objBest;
    }

    private void AddMapping(Candidate p_objCandidate, CWorkbookStructureDetectionResult p_objResult)
    {
        var v_arrColumns = new List<CImportColumnMapping>();
        var v_dicMappedColumns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var v_bAmbiguous = false;

        foreach (var v_objCell in p_objCandidate.HeaderRow.Cells)
        {
            var v_strHeader = HeaderText(v_objCell);
            if (v_strHeader.Length == 0)
                continue;

            if (TryGetField(p_objCandidate.Profile, v_strHeader, out var v_objField))
            {
                var v_strField = v_objField.Field;
                if (!v_dicMappedColumns.TryAdd(v_strField, v_objCell.ColumnNumber))
                {
                    p_objResult.AddError(
                        "AMBIGUOUS_MAPPING",
                        $"Nhiều cột trong sheet '{p_objCandidate.Sheet.Name}' cùng ánh xạ đến '{v_strField}'.",
                        p_objCandidate.Sheet.Name,
                        p_objCandidate.HeaderRow.RowNumber);
                    v_bAmbiguous = true;
                }
                v_arrColumns.Add(new CImportColumnMapping(
                    v_objCell.ColumnNumber,
                    v_strHeader,
                    v_strField,
                    GetFieldPolicy(v_objField) switch
                    {
                        EImportFieldPolicy.SafeIgnore => EImportColumnDisposition.Ignored,
                        EImportFieldPolicy.IgnoreSourceOnly => EImportColumnDisposition.Ignored,
                        EImportFieldPolicy.BlockIfNonEmpty => EImportColumnDisposition.Unsupported,
                        _ => EImportColumnDisposition.Mapped
                    }));
            }
            else if (TryGetSafeIgnoreHeader(p_objCandidate.Profile.SafeIgnoreHeaders, v_strHeader, out _))
            {
                v_arrColumns.Add(new CImportColumnMapping(v_objCell.ColumnNumber, v_strHeader, null, EImportColumnDisposition.Ignored));
            }
            else
            {
                v_arrColumns.Add(new CImportColumnMapping(v_objCell.ColumnNumber, v_strHeader, null, EImportColumnDisposition.Unsupported));
            }
        }

        var v_setPresentFields = v_dicMappedColumns.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var v_objField in p_objCandidate.Profile.Fields.Where(v_objField => v_objField.Required))
        {
            if (!v_setPresentFields.Contains(v_objField.Field))
            {
                p_objResult.AddError(
                    "MISSING_REQUIRED_MAPPING",
                    $"Sheet '{p_objCandidate.Sheet.Name}' thiếu ánh xạ bắt buộc '{v_objField.Field}'.",
                    p_objCandidate.Sheet.Name,
                    p_objCandidate.HeaderRow.RowNumber);
            }
        }

        if (v_bAmbiguous)
            return;

        var v_arrDataRows = p_objCandidate.Sheet.Rows
            .Where(v_objRow => v_objRow.RowNumber > p_objCandidate.HeaderRow.RowNumber)
            .Select(v_objRow => MapDataRow(v_objRow, v_arrColumns))
            .ToArray();
        var v_dicSourceRows = p_objCandidate.Sheet.Rows.ToDictionary(v_objRow => v_objRow.RowNumber);
        var v_objCapabilityIssues = new List<CImportCapabilityIssue>();
        var v_objPolicyDiagnostics = new List<CImportFieldPolicyDiagnostic>();
        for (var v_iColumnIndex = 0; v_iColumnIndex < v_arrColumns.Count; v_iColumnIndex++)
        {
            var v_objColumn = v_arrColumns[v_iColumnIndex];
            var v_objField = v_objColumn.CanonicalField is null
                ? null
                : p_objCandidate.Profile.Fields.FirstOrDefault(v_objItem =>
                    string.Equals(v_objItem.Field, v_objColumn.CanonicalField, StringComparison.OrdinalIgnoreCase));
            if (v_objField is not null && GetFieldPolicy(v_objField) == EImportFieldPolicy.IgnoreSourceOnly)
            {
                foreach (var v_objRow in v_arrDataRows)
                {
                    var v_bFormula = v_objRow.FormulaFields.Contains(v_objField.Field);
                    var v_strValue = v_bFormula
                        ? "[formula]"
                        : v_objRow.Values.TryGetValue(v_objField.Field, out var v_objValue)
                            ? Convert.ToString(v_objValue, CultureInfo.InvariantCulture) ?? ""
                            : "";
                    if (string.IsNullOrWhiteSpace(v_strValue))
                        continue;

                    v_objPolicyDiagnostics.Add(new CImportFieldPolicyDiagnostic(
                        v_objRow.RowNumber,
                        p_objCandidate.Sheet.Name,
                        p_objCandidate.Profile.Role,
                        v_objColumn.Header,
                        v_objField.Field,
                        v_strValue,
                        "SOURCE_FIELD_IGNORED",
                        "IGNORED",
                        v_objField.PolicyReason!));
                }
                continue;
            }

            if (v_objField is not null && GetFieldPolicy(v_objField) == EImportFieldPolicy.SafeIgnore)
            {
                foreach (var v_objRow in v_arrDataRows)
                {
                    var v_bFormula = v_objRow.FormulaFields.Contains(v_objField.Field);
                    var v_strValue = v_bFormula
                        ? "[formula]"
                        : v_objRow.Values.TryGetValue(v_objField.Field, out var v_objValue)
                            ? Convert.ToString(v_objValue, CultureInfo.InvariantCulture) ?? ""
                            : "";
                    if (string.IsNullOrWhiteSpace(v_strValue))
                        continue;

                    v_objPolicyDiagnostics.Add(new CImportFieldPolicyDiagnostic(
                        v_objRow.RowNumber,
                        p_objCandidate.Sheet.Name,
                        p_objCandidate.Profile.Role,
                        v_objColumn.Header,
                        v_objField.Field,
                        v_strValue,
                        "SAFE_IGNORE",
                        "IGNORED",
                        v_objField.PolicyReason!));
                }
                continue;
            }

            if (v_objField is not null && GetFieldPolicy(v_objField) == EImportFieldPolicy.BlockIfNonEmpty)
            {
                var v_bHasCapabilityIssue = false;
                foreach (var v_objRow in v_arrDataRows)
                {
                    var v_bFormula = v_objRow.FormulaFields.Contains(v_objField.Field);
                    var v_strValue = v_bFormula
                        ? "[formula]"
                        : v_objRow.Values.TryGetValue(v_objField.Field, out var v_objValue)
                            ? Convert.ToString(v_objValue, CultureInfo.InvariantCulture) ?? ""
                            : "";
                    if (string.IsNullOrWhiteSpace(v_strValue))
                        continue;

                    if (!v_bFormula && IsSafeValue(v_strValue, v_objField.SafeValues))
                    {
                        v_objPolicyDiagnostics.Add(new CImportFieldPolicyDiagnostic(
                            v_objRow.RowNumber,
                            p_objCandidate.Sheet.Name,
                            p_objCandidate.Profile.Role,
                            v_objColumn.Header,
                            v_objField.Field,
                            v_strValue,
                            "SAFE_IGNORE",
                            "IGNORED",
                            v_objField.SafeValueReason!));
                        continue;
                    }

                    if (!v_bFormula && v_objField.SafeWhenEqualToField is not null &&
                        AreRowValuesEqual(v_objRow, v_objField.Field, v_objField.SafeWhenEqualToField))
                    {
                        v_objPolicyDiagnostics.Add(new CImportFieldPolicyDiagnostic(
                            v_objRow.RowNumber,
                            p_objCandidate.Sheet.Name,
                            p_objCandidate.Profile.Role,
                            v_objColumn.Header,
                            v_objField.Field,
                            v_strValue,
                            "SAFE_IGNORE",
                            "IGNORED",
                            v_objField.SafeValueReason!));
                        continue;
                    }

                    v_bHasCapabilityIssue = true;
                    v_objCapabilityIssues.Add(new CImportCapabilityIssue(
                        v_objRow.RowNumber,
                        p_objCandidate.Sheet.Name,
                        p_objCandidate.Profile.Role,
                        v_objColumn.Header,
                        v_objField.Field,
                        v_strValue,
                        "UNSUPPORTED_DOMAIN_CAPABILITY",
                        v_objField.UnsupportedReason!));
                    v_objPolicyDiagnostics.Add(new CImportFieldPolicyDiagnostic(
                        v_objRow.RowNumber,
                        p_objCandidate.Sheet.Name,
                        p_objCandidate.Profile.Role,
                        v_objColumn.Header,
                        v_objField.Field,
                        v_strValue,
                        "BLOCK_IF_NONEMPTY",
                        "BLOCKED",
                        v_objField.UnsupportedReason!));
                }

                if (!v_bHasCapabilityIssue)
                {
                    v_arrColumns[v_iColumnIndex] = v_objColumn with { Disposition = EImportColumnDisposition.Ignored };
                }
                continue;
            }

            if (v_objField is null && v_objColumn.Disposition == EImportColumnDisposition.Unsupported)
            {
                var v_bHasCapabilityIssue = false;
                foreach (var v_objRow in v_arrDataRows)
                {
                    var v_objCell = v_dicSourceRows[v_objRow.RowNumber].Cells
                        .FirstOrDefault(v_objCell => v_objCell.ColumnNumber == v_objColumn.ColumnNumber);
                    var v_bFormula = v_objCell?.Formula is not null || v_objCell?.CellType == "Formula";
                    var v_strValue = v_bFormula ? "[formula]" : Convert.ToString(v_objCell?.Value, CultureInfo.InvariantCulture) ?? "";
                    if (string.IsNullOrWhiteSpace(v_strValue))
                        continue;

                    v_bHasCapabilityIssue = true;
                    v_objCapabilityIssues.Add(new CImportCapabilityIssue(
                        v_objRow.RowNumber,
                        p_objCandidate.Sheet.Name,
                        p_objCandidate.Profile.Role,
                        v_objColumn.Header,
                        "",
                        v_strValue,
                        "UNSUPPORTED_DOMAIN_CAPABILITY",
                        $"Cột '{v_objColumn.Header}' không có ánh xạ/capability được hỗ trợ."));
                }

                if (!v_bHasCapabilityIssue)
                {
                    v_arrColumns[v_iColumnIndex] = v_objColumn with { Disposition = EImportColumnDisposition.Ignored };
                }
            }
            else if (v_objField is null && v_objColumn.Disposition == EImportColumnDisposition.Ignored &&
                     TryGetSafeIgnoreHeader(p_objCandidate.Profile.SafeIgnoreHeaders, v_objColumn.Header, out var v_objSafeIgnore))
            {
                foreach (var v_objRow in v_arrDataRows)
                {
                    var v_objCell = v_dicSourceRows[v_objRow.RowNumber].Cells
                        .FirstOrDefault(v_objCell => v_objCell.ColumnNumber == v_objColumn.ColumnNumber);
                    var v_bFormula = v_objCell?.Formula is not null || v_objCell?.CellType == "Formula";
                    var v_strValue = v_bFormula ? "[formula]" : Convert.ToString(v_objCell?.Value, CultureInfo.InvariantCulture) ?? "";
                    if (string.IsNullOrWhiteSpace(v_strValue))
                        continue;

                    v_objPolicyDiagnostics.Add(new CImportFieldPolicyDiagnostic(
                        v_objRow.RowNumber,
                        p_objCandidate.Sheet.Name,
                        p_objCandidate.Profile.Role,
                        v_objColumn.Header,
                        "",
                        v_strValue,
                        "SOURCE_FIELD_IGNORED",
                        "IGNORED",
                        v_objSafeIgnore.Reason));
                }
            }
        }

        AddMapToFieldDiagnostics(
            p_objCandidate,
            v_arrColumns,
            v_arrDataRows,
            v_objCapabilityIssues,
            v_objPolicyDiagnostics);

        var v_arrMappedHeaders = v_arrColumns
            .Where(v_objColumn => v_objColumn.Disposition == EImportColumnDisposition.Mapped)
            .Select(v_objColumn => v_objColumn.Header)
            .ToArray();
        var v_arrIgnoredHeaders = v_arrColumns
            .Where(v_objColumn => v_objColumn.Disposition == EImportColumnDisposition.Ignored)
            .Select(v_objColumn => v_objColumn.Header)
            .ToArray();
        var v_arrUnsupportedHeaders = v_arrColumns
            .Where(v_objColumn => v_objColumn.Disposition == EImportColumnDisposition.Unsupported)
            .Select(v_objColumn => v_objColumn.Header)
            .ToArray();
        p_objResult.SheetMappings.Add(new CImportDetectedSheet(
            p_objCandidate.Sheet.Name,
            p_objCandidate.Profile.Role,
            p_objCandidate.HeaderRow.RowNumber,
            v_arrColumns,
            v_arrDataRows)
        {
            CapabilityIssues = v_objCapabilityIssues,
            PolicyDiagnostics = v_objPolicyDiagnostics,
            MappedColumns = v_arrMappedHeaders,
            IgnoredColumns = v_arrIgnoredHeaders,
            UnsupportedColumns = v_arrUnsupportedHeaders
        });
    }

    private static CImportDetectedRow MapDataRow(CWorkbook_Xlsx_Row p_objRow, IReadOnlyList<CImportColumnMapping> p_arrColumns)
    {
        var v_dicValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var v_setFormulaFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var v_dicFormulas = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var v_dicCellTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var v_dicNumberFormats = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var v_objColumn in p_arrColumns.Where(v_objColumn =>
            v_objColumn.CanonicalField is not null))
        {
            var v_objCell = p_objRow.Cells.FirstOrDefault(v_objCell => v_objCell.ColumnNumber == v_objColumn.ColumnNumber);
            var v_strField = v_objColumn.CanonicalField!;
            if (v_objCell?.Formula is not null || v_objCell?.CellType == "Formula")
            {
                v_setFormulaFields.Add(v_strField);
                v_dicValues[v_strField] = null;
                v_dicFormulas[v_strField] = v_objCell.Formula;
            }
            else
            {
                v_dicValues[v_strField] = v_objCell?.Value;
            }
            v_dicCellTypes[v_strField] = v_objCell?.CellType ?? "Blank";
            v_dicNumberFormats[v_strField] = v_objCell?.NumberFormat;
        }

        return new CImportDetectedRow(
            p_objRow.RowNumber,
            p_objRow.IsHidden,
            v_dicValues,
            v_setFormulaFields,
            v_dicFormulas,
            v_dicCellTypes,
            v_dicNumberFormats);
    }

    private static bool TryGetField(CImportSheetProfile p_objProfile, string p_strHeader, out CImportFieldProfile p_objField)
    {
        var v_strNormalizedHeader = CImportHeaderNormalizer.Normalize(p_strHeader);
        foreach (var v_objField in p_objProfile.Fields)
        {
            if (v_objField.Aliases.Any(v_strAlias =>
                CImportHeaderNormalizer.Normalize(v_strAlias) == v_strNormalizedHeader))
            {
                p_objField = v_objField;
                return true;
            }
        }

        p_objField = null!;
        return false;
    }

    private void AddMapToFieldDiagnostics(
        Candidate p_objCandidate,
        List<CImportColumnMapping> p_arrColumns,
        IReadOnlyList<CImportDetectedRow> p_arrRows,
        List<CImportCapabilityIssue> p_arrCapabilityIssues,
        List<CImportFieldPolicyDiagnostic> p_arrPolicyDiagnostics)
    {
        foreach (var v_objField in p_objCandidate.Profile.Fields.Where(v_objField => v_objField.MapToField is not null))
        {
            var v_iSourceColumn = p_arrColumns.FindIndex(v_objColumn =>
                string.Equals(v_objColumn.CanonicalField, v_objField.Field, StringComparison.OrdinalIgnoreCase));
            var v_iTargetColumn = p_arrColumns.FindIndex(v_objColumn =>
                string.Equals(v_objColumn.CanonicalField, v_objField.MapToField, StringComparison.OrdinalIgnoreCase));
            if (v_iSourceColumn < 0 || v_iTargetColumn < 0)
                continue;

            var v_objSourceColumn = p_arrColumns[v_iSourceColumn];
            var v_bHasFailure = false;
            foreach (var v_objRow in p_arrRows)
            {
                var v_bSourceFormula = v_objRow.FormulaFields.Contains(v_objField.Field);
                var v_strSourceValue = v_bSourceFormula
                    ? "[formula]"
                    : v_objRow.Values.TryGetValue(v_objField.Field, out var v_objSourceValue)
                        ? Convert.ToString(v_objSourceValue, CultureInfo.InvariantCulture) ?? ""
                        : "";
                if (string.IsNullOrWhiteSpace(v_strSourceValue))
                    continue;

                var v_objMapping = v_bSourceFormula
                    ? new CImportValueMappingResult(v_strSourceValue, v_strSourceValue, false)
                    : m_objValueMapper.Map(p_objCandidate.ProfileCode, v_objField.Field, v_strSourceValue);
                var v_bTargetFormula = v_objRow.FormulaFields.Contains(v_objField.MapToField!);
                var v_strTargetValue = v_bTargetFormula
                    ? "[formula]"
                    : v_objRow.Values.TryGetValue(v_objField.MapToField!, out var v_objTargetValue)
                        ? Convert.ToString(v_objTargetValue, CultureInfo.InvariantCulture) ?? ""
                        : "";

                var v_bMappingFound = v_objMapping.WasMapped;
                var v_bTargetMatches = v_bMappingFound && !v_bTargetFormula &&
                    CImportBusinessValueComparer.AreEqual(v_objMapping.MappedValue, v_strTargetValue);
                if (v_bTargetMatches)
                {
                    p_arrPolicyDiagnostics.Add(new CImportFieldPolicyDiagnostic(
                        v_objRow.RowNumber,
                        p_objCandidate.Sheet.Name,
                        p_objCandidate.Profile.Role,
                        v_objSourceColumn.Header,
                        v_objField.Field,
                        v_strSourceValue,
                        "MAP",
                        "MAPPED_AND_MATCHED",
                        v_objField.PolicyReason!,
                        v_objMapping.MappedValue));
                    continue;
                }

                v_bHasFailure = true;
                var v_strErrorCode = v_bMappingFound ? "REFERENCE_MAPPING_TARGET_MISMATCH" : "REFERENCE_MAPPING_NOT_FOUND";
                var v_strMessage = v_bMappingFound
                    ? $"Mã '{v_strSourceValue}' ánh xạ thành '{v_objMapping.MappedValue}' nhưng không khớp '{v_objField.MapToField}'."
                    : $"Không có ánh xạ chính xác cho mã '{v_strSourceValue}'.";
                p_arrCapabilityIssues.Add(new CImportCapabilityIssue(
                    v_objRow.RowNumber,
                    p_objCandidate.Sheet.Name,
                    p_objCandidate.Profile.Role,
                    v_objSourceColumn.Header,
                    v_objField.Field,
                    v_strSourceValue,
                    v_strErrorCode,
                    v_strMessage));
                p_arrPolicyDiagnostics.Add(new CImportFieldPolicyDiagnostic(
                    v_objRow.RowNumber,
                    p_objCandidate.Sheet.Name,
                    p_objCandidate.Profile.Role,
                    v_objSourceColumn.Header,
                    v_objField.Field,
                    v_strSourceValue,
                    "MAP",
                    v_bMappingFound ? "BLOCKED_MISMATCH" : "BLOCKED_NO_MAPPING",
                    v_objField.PolicyReason!,
                    v_objMapping.MappedValue));
            }

            if (v_bHasFailure)
                p_arrColumns[v_iSourceColumn] = p_arrColumns[v_iSourceColumn] with { Disposition = EImportColumnDisposition.Unsupported };
        }
    }

    private static EImportFieldPolicy GetFieldPolicy(CImportFieldProfile p_objField) =>
        p_objField.Policy ??
        (string.IsNullOrWhiteSpace(p_objField.UnsupportedReason)
            ? p_objField.Required ? EImportFieldPolicy.Required : EImportFieldPolicy.Map
            : EImportFieldPolicy.BlockIfNonEmpty);

    private static bool TryGetSafeIgnoreHeader(
        IReadOnlyList<CImportSafeIgnoreHeader> p_arrPolicies,
        string p_strHeader,
        out CImportSafeIgnoreHeader p_objPolicy)
    {
        var v_strNormalizedHeader = CImportHeaderNormalizer.Normalize(p_strHeader);
        p_objPolicy = p_arrPolicies.FirstOrDefault(v_objPolicy =>
            CImportHeaderNormalizer.Normalize(v_objPolicy.Header) == v_strNormalizedHeader)!;
        return p_objPolicy is not null;
    }

    private static bool AreRowValuesEqual(CImportDetectedRow p_objRow, string p_strLeftField, string p_strRightField)
    {
        if (p_objRow.FormulaFields.Contains(p_strLeftField) || p_objRow.FormulaFields.Contains(p_strRightField) ||
            !p_objRow.Values.TryGetValue(p_strLeftField, out var v_objLeft) ||
            !p_objRow.Values.TryGetValue(p_strRightField, out var v_objRight) ||
            v_objLeft is null || v_objRight is null)
        {
            return false;
        }

        if (IsNumeric(v_objLeft) && IsNumeric(v_objRight))
            return Convert.ToDecimal(v_objLeft, CultureInfo.InvariantCulture) == Convert.ToDecimal(v_objRight, CultureInfo.InvariantCulture);
        if (v_objLeft is DateTime v_objLeftDate && v_objRight is DateTime v_objRightDate)
            return v_objLeftDate == v_objRightDate;

        return CImportBusinessValueComparer.AreEqual(
            Convert.ToString(v_objLeft, CultureInfo.InvariantCulture),
            Convert.ToString(v_objRight, CultureInfo.InvariantCulture));
    }

    private static bool IsNumeric(object p_objValue) => Type.GetTypeCode(p_objValue.GetType()) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or
        TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    private static bool IsSafeValue(string p_strValue, IReadOnlyList<string> p_arrSafeValues) =>
        p_arrSafeValues.Any(v_strSafeValue => CImportBusinessValueComparer.AreEqual(p_strValue, v_strSafeValue));

    private static string HeaderText(CWorkbook_Xlsx_Cell p_objCell)
    {
        if (p_objCell.Formula is not null || p_objCell.CellType == "Formula")
            return "";
        return Convert.ToString(p_objCell.Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
    }

    private static bool IsBlankRow(CWorkbook_Xlsx_Row p_objRow) => p_objRow.Cells.All(v_objCell =>
        v_objCell.Formula is null &&
        (v_objCell.Value is null || string.IsNullOrWhiteSpace(Convert.ToString(v_objCell.Value, CultureInfo.InvariantCulture))));

    private static bool HasUnmappedNonEmptySheets(
        CWorkbook_Xlsx_Workbook p_objWorkbook,
        IEnumerable<CWorkbook_Xlsx_Sheet> p_arrMappedSheets)
    {
        var v_setMappedNames = p_arrMappedSheets.Select(v_objSheet => v_objSheet.Name).ToHashSet(StringComparer.Ordinal);
        return p_objWorkbook.Worksheets.Any(v_objSheet =>
            !v_setMappedNames.Contains(v_objSheet.Name) && CountPopulatedCells(v_objSheet) > 1);
    }

    private static int CountPopulatedCells(CWorkbook_Xlsx_Sheet p_objSheet) => p_objSheet.Rows
        .SelectMany(v_objRow => v_objRow.Cells)
        .Count(v_objCell => v_objCell.Formula is not null || v_objCell.Value is not null &&
            !string.IsNullOrWhiteSpace(Convert.ToString(v_objCell.Value, CultureInfo.InvariantCulture)));

    private sealed record Candidate(
        CWorkbook_Xlsx_Sheet Sheet,
        string ProfileCode,
        CImportSheetProfile Profile,
        CWorkbook_Xlsx_Row HeaderRow,
        int Score,
        bool NameHintMatch);
}
