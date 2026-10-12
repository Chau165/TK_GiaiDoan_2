namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed class CImportValueMapper
{
    private readonly CImportProfileProvider m_objProfiles;

    public CImportValueMapper(CImportProfileProvider p_objProfiles)
    {
        ArgumentNullException.ThrowIfNull(p_objProfiles);
        m_objProfiles = p_objProfiles;
    }

    public CImportValueMappingResult Map(string p_strProfileCode, string p_strCanonicalField, string? p_strSourceValue)
    {
        var v_strNormalizedSource = CImportBusinessValueComparer.Normalize(p_strSourceValue);
        var v_objField = m_objProfiles.GetField(p_strProfileCode, p_strCanonicalField);
        if (v_strNormalizedSource.Length == 0 || v_objField.ReferenceMap is null)
            return new CImportValueMappingResult(p_strSourceValue ?? "", v_strNormalizedSource, false);

        var v_objMap = m_objProfiles.GetReferenceMap(v_objField.ReferenceMap);
        var v_strSourceKey = v_strNormalizedSource.ToUpperInvariant();
        var v_objMapping = v_objMap.Mappings.FirstOrDefault(v_objItem =>
            string.Equals(
                CImportBusinessValueComparer.Normalize(v_objItem.SourceValue).ToUpperInvariant(),
                v_strSourceKey,
                StringComparison.Ordinal));

        return v_objMapping is null
            ? new CImportValueMappingResult(p_strSourceValue ?? "", v_strNormalizedSource, false)
            : new CImportValueMappingResult(
                p_strSourceValue ?? "",
                CImportBusinessValueComparer.Normalize(v_objMapping.TargetValue),
                true);
    }
}
