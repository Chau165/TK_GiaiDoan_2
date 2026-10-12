using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public sealed class CImportProfileProvider
{
    private const string m_strResourceName = "TKS_Thuc_Tap_V11_Data_Access.excel-import-profiles.json";
    private readonly Lazy<CImportProfileCatalog> m_objCatalog;

    public CImportProfileProvider()
    {
        m_objCatalog = new Lazy<CImportProfileCatalog>(LoadCatalog);
    }

    public CImportProfileProvider(CImportProfileConfiguration p_objConfiguration)
    {
        var v_objCatalog = BuildCatalog(p_objConfiguration);
        m_objCatalog = new Lazy<CImportProfileCatalog>(() => v_objCatalog);
    }

    public CImportProfile GetByCode(string p_strCode)
    {
        if (string.IsNullOrWhiteSpace(p_strCode))
            throw new ArgumentException("An import profile code is required.", nameof(p_strCode));
        if (!m_objCatalog.Value.Profiles.TryGetValue(p_strCode, out var v_objProfile))
            throw new KeyNotFoundException($"Import profile '{p_strCode}' was not found.");
        return v_objProfile;
    }

    public CImportReferenceMap GetReferenceMap(string p_strName)
    {
        if (!m_objCatalog.Value.ReferenceMaps.TryGetValue(p_strName, out var v_objMap))
            throw new KeyNotFoundException($"Import reference map '{p_strName}' was not found.");
        return v_objMap;
    }

    public CImportFieldProfile GetField(string p_strProfileCode, string p_strCanonicalField)
    {
        var v_objProfile = GetByCode(p_strProfileCode);
        return v_objProfile.Sheets
            .SelectMany(v_objSheet => v_objSheet.Fields)
            .FirstOrDefault(v_objField => string.Equals(v_objField.Field, p_strCanonicalField, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Field '{p_strCanonicalField}' was not found in import profile '{p_strProfileCode}'.");
    }

    private static CImportProfileCatalog LoadCatalog()
    {
        using var v_objStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(m_strResourceName)
            ?? throw new InvalidOperationException($"Embedded import profile resource '{m_strResourceName}' was not found.");
        var v_objOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        v_objOptions.Converters.Add(new JsonStringEnumConverter());
        var v_objConfiguration = JsonSerializer.Deserialize<CImportProfileConfiguration>(v_objStream, v_objOptions)
            ?? throw new InvalidDataException("The embedded Excel import profiles are empty.");
        return BuildCatalog(v_objConfiguration);
    }

    private static CImportProfileCatalog BuildCatalog(CImportProfileConfiguration p_objConfiguration)
    {
        ArgumentNullException.ThrowIfNull(p_objConfiguration);
        var v_dicReferenceMaps = new Dictionary<string, CImportReferenceMap>(StringComparer.OrdinalIgnoreCase);
        foreach (var v_objMap in p_objConfiguration.ReferenceMaps)
        {
            ValidateReferenceMap(v_objMap);
            if (!v_dicReferenceMaps.TryAdd(v_objMap.Name, v_objMap))
                throw new InvalidDataException($"Duplicate import reference map '{v_objMap.Name}'.");
        }

        var v_dicProfiles = new Dictionary<string, CImportProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var v_objProfile in p_objConfiguration.Profiles)
        {
            ValidateProfile(v_objProfile, v_dicReferenceMaps);
            if (!v_dicProfiles.TryAdd(v_objProfile.Code, v_objProfile))
                throw new InvalidDataException($"Duplicate Excel import profile code '{v_objProfile.Code}'.");
        }

        return new CImportProfileCatalog(v_dicProfiles, v_dicReferenceMaps);
    }

    private static void ValidateReferenceMap(CImportReferenceMap p_objMap)
    {
        if (string.IsNullOrWhiteSpace(p_objMap.Name) || string.IsNullOrWhiteSpace(p_objMap.EntityType))
            throw new InvalidDataException("Each import reference map needs a name and entity type.");

        var v_dicTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v_objMapping in p_objMap.Mappings)
        {
            var v_strSource = CImportBusinessValueComparer.Normalize(v_objMapping.SourceValue);
            var v_strTarget = CImportBusinessValueComparer.Normalize(v_objMapping.TargetValue);
            if (v_strSource.Length == 0 || v_strTarget.Length == 0)
                throw new InvalidDataException($"Reference map '{p_objMap.Name}' contains an empty source or target value.");

            var v_strKey = v_strSource.ToUpperInvariant();
            if (v_dicTargets.TryGetValue(v_strKey, out var v_strExistingTarget) &&
                !string.Equals(v_strExistingTarget, v_strTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"VALUE_MAPPING_CONFLICT: source '{v_objMapping.SourceValue}' maps to multiple targets in reference map '{p_objMap.Name}'.");
            }

            v_dicTargets[v_strKey] = v_strTarget;
        }
    }

    private static void ValidateProfile(
        CImportProfile p_objProfile,
        IReadOnlyDictionary<string, CImportReferenceMap> p_dicReferenceMaps)
    {
        if (string.IsNullOrWhiteSpace(p_objProfile.Code) || p_objProfile.Sheets.Count == 0)
            throw new InvalidDataException("Each Excel import profile needs a code and at least one sheet role.");
        if (p_objProfile.MaxFileBytes <= 0 || p_objProfile.MaxRows <= 0 ||
            p_objProfile.MaxWorksheets <= 0 || p_objProfile.MaxColumns <= 0)
        {
            throw new InvalidDataException($"Excel import profile '{p_objProfile.Code}' has invalid resource limits.");
        }

        var v_setRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v_objSheet in p_objProfile.Sheets)
        {
            if (string.IsNullOrWhiteSpace(v_objSheet.Role) || !v_setRoles.Add(v_objSheet.Role))
                throw new InvalidDataException($"Excel import profile '{p_objProfile.Code}' has an empty or duplicate sheet role.");

            var v_dicAliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var v_objField in v_objSheet.Fields)
            {
                if (string.IsNullOrWhiteSpace(v_objField.Field) || v_objField.Aliases.Count == 0)
                    throw new InvalidDataException($"Field in Excel import profile '{p_objProfile.Code}' is incomplete.");

                if (v_objField.ReferenceMap is not null)
                {
                    if (!p_dicReferenceMaps.TryGetValue(v_objField.ReferenceMap, out var v_objMap))
                        throw new InvalidDataException($"Profile '{p_objProfile.Code}' field '{v_objField.Field}' references unknown map '{v_objField.ReferenceMap}'.");
                    if (!string.Equals(v_objMap.EntityType, v_objField.ReferenceEntityType, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Profile '{p_objProfile.Code}' field '{v_objField.Field}' has a mismatched reference-map entity type.");
                }
                else if (v_objField.ReferenceEntityType is not null)
                {
                    throw new InvalidDataException($"Profile '{p_objProfile.Code}' field '{v_objField.Field}' specifies a reference entity without a map.");
                }

                var v_ePolicy = v_objField.Policy ??
                    (string.IsNullOrWhiteSpace(v_objField.UnsupportedReason)
                        ? v_objField.Required ? EImportFieldPolicy.Required : EImportFieldPolicy.Map
                        : EImportFieldPolicy.BlockIfNonEmpty);
                if (v_ePolicy == EImportFieldPolicy.BlockIfNonEmpty && string.IsNullOrWhiteSpace(v_objField.UnsupportedReason))
                    throw new InvalidDataException($"Blocked field '{v_objField.Field}' in profile '{p_objProfile.Code}' needs an unsupported-capability reason.");
                if (v_ePolicy == EImportFieldPolicy.SafeIgnore && string.IsNullOrWhiteSpace(v_objField.PolicyReason))
                    throw new InvalidDataException($"Safe-ignored field '{v_objField.Field}' in profile '{p_objProfile.Code}' needs a reason.");
                if (v_ePolicy == EImportFieldPolicy.IgnoreSourceOnly && string.IsNullOrWhiteSpace(v_objField.PolicyReason))
                    throw new InvalidDataException($"Source-only field '{v_objField.Field}' in profile '{p_objProfile.Code}' needs a reason.");
                if (v_objField.SafeValues.Count > 0 &&
                    (v_ePolicy != EImportFieldPolicy.BlockIfNonEmpty ||
                     string.IsNullOrWhiteSpace(v_objField.UnsupportedReason) ||
                     string.IsNullOrWhiteSpace(v_objField.SafeValueReason)))
                {
                    throw new InvalidDataException($"Field '{v_objField.Field}' in profile '{p_objProfile.Code}' has incomplete conditional safe-value policy.");
                }
                if (v_objField.SafeWhenEqualToField is not null &&
                    (v_ePolicy != EImportFieldPolicy.BlockIfNonEmpty ||
                     string.IsNullOrWhiteSpace(v_objField.UnsupportedReason) ||
                     string.IsNullOrWhiteSpace(v_objField.SafeValueReason) ||
                     !v_objSheet.Fields.Any(v_objTarget => string.Equals(v_objTarget.Field, v_objField.SafeWhenEqualToField, StringComparison.OrdinalIgnoreCase))))
                {
                    throw new InvalidDataException($"Field '{v_objField.Field}' in profile '{p_objProfile.Code}' has an invalid safe-equality policy.");
                }
                if (v_objField.MapToField is not null &&
                    (v_objField.ReferenceMap is null ||
                     string.IsNullOrWhiteSpace(v_objField.PolicyReason) ||
                     !v_objSheet.Fields.Any(v_objTarget => string.Equals(v_objTarget.Field, v_objField.MapToField, StringComparison.OrdinalIgnoreCase))))
                {
                    throw new InvalidDataException($"Field '{v_objField.Field}' in profile '{p_objProfile.Code}' has an incomplete map-to-field policy.");
                }

                foreach (var v_strAlias in v_objField.Aliases)
                {
                    var v_strNormalized = CImportHeaderNormalizer.Normalize(v_strAlias);
                    if (v_strNormalized.Length == 0)
                        throw new InvalidDataException($"Excel import profile '{p_objProfile.Code}' contains an empty header alias.");
                    if (v_dicAliases.TryGetValue(v_strNormalized, out var v_strExistingField) &&
                        !string.Equals(v_strExistingField, v_objField.Field, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Header alias '{v_strAlias}' maps to multiple fields in profile '{p_objProfile.Code}'.");
                    }

                    v_dicAliases[v_strNormalized] = v_objField.Field;
                }
            }

            var v_setSafeIgnoreHeaders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v_objSafeIgnore in v_objSheet.SafeIgnoreHeaders)
            {
                var v_strHeader = CImportHeaderNormalizer.Normalize(v_objSafeIgnore.Header);
                if (v_strHeader.Length == 0 || string.IsNullOrWhiteSpace(v_objSafeIgnore.Reason))
                    throw new InvalidDataException($"Profile '{p_objProfile.Code}' has an incomplete safe-ignore header policy.");
                if (v_dicAliases.ContainsKey(v_strHeader) || !v_setSafeIgnoreHeaders.Add(v_strHeader))
                    throw new InvalidDataException($"Profile '{p_objProfile.Code}' maps safe-ignore header '{v_objSafeIgnore.Header}' more than once.");
            }
        }
    }

    private sealed record CImportProfileCatalog(
        IReadOnlyDictionary<string, CImportProfile> Profiles,
        IReadOnlyDictionary<string, CImportReferenceMap> ReferenceMaps);
}
