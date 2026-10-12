namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed class CProfiledWorkbook_Xlsx_Reader
{
    private readonly CWorkbook_Xlsx_Reader m_objWorkbookReader = new();
    private readonly CWorkbookStructureDetector m_objDetector;
    private readonly CImportProfileProvider m_objProfileProvider;

    public CProfiledWorkbook_Xlsx_Reader()
        : this(new CImportProfileProvider())
    {
    }

    public CProfiledWorkbook_Xlsx_Reader(CImportProfileProvider p_objProfileProvider)
    {
        ArgumentNullException.ThrowIfNull(p_objProfileProvider);
        m_objProfileProvider = p_objProfileProvider;
        m_objDetector = new CWorkbookStructureDetector(p_objProfileProvider);
    }

    public async Task<CWorkbookStructureDetectionResult> ReadAsync(
        Stream p_objSource,
        string p_strProfileCode,
        CancellationToken p_objCancellationToken = default)
    {
        var v_objProfile = m_objProfileProvider.GetByCode(p_strProfileCode);
        var v_objWorkbook = await m_objWorkbookReader.ReadAsync(
            p_objSource,
            new CWorkbook_Xlsx_Limits(
                v_objProfile.MaxFileBytes,
                v_objProfile.MaxWorksheets,
                checked(v_objProfile.MaxRows + 20),
                v_objProfile.MaxColumns),
            p_objCancellationToken);
        return m_objDetector.Detect(v_objWorkbook, v_objProfile);
    }

    public async Task<CImportDetectedSheet> ReadSingleSheetAsync(
        Stream p_objSource,
        string p_strProfileCode,
        CancellationToken p_objCancellationToken = default)
    {
        var v_objResult = await ReadAsync(p_objSource, p_strProfileCode, p_objCancellationToken);
        if (!v_objResult.IsValid)
            throw new InvalidDataException(string.Join(Environment.NewLine, v_objResult.Errors.Select(v_objError => v_objError.Message)));
        if (v_objResult.SheetMappings.Count != 1)
            throw new InvalidDataException($"Profile '{p_strProfileCode}' did not identify exactly one data sheet.");
        return v_objResult.SheetMappings[0];
    }
}
