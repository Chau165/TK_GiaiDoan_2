using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class ImportValueMappingTests
{
    [Fact]
    public void Customer_unit_code_maps_to_the_configured_business_name()
    {
        var v_objMapper = new CImportValueMapper(new CImportProfileProvider());

        var v_objResult = v_objMapper.Map("Product", "UnitName", "  thung  ");

        Assert.True(v_objResult.WasMapped);
        Assert.Equal("  thung  ", v_objResult.SourceValue);
        Assert.Equal("Thùng", v_objResult.MappedValue);
    }

    [Fact]
    public void Customer_warehouse_code_maps_only_in_warehouse_reference_fields()
    {
        var v_objMapper = new CImportValueMapper(new CImportProfileProvider());

        var v_objWarehouse = v_objMapper.Map("Inbound", "WarehouseCode", " KHO-HCM-01 ");
        var v_objUnit = v_objMapper.Map("Inbound", "UnitReference", "KHO-HCM-01");

        Assert.Equal("Kho Bình Tân", v_objWarehouse.MappedValue);
        Assert.True(v_objWarehouse.WasMapped);
        Assert.Equal("KHO-HCM-01", v_objUnit.MappedValue);
        Assert.False(v_objUnit.WasMapped);
    }

    [Fact]
    public void Mapping_is_explicit_and_preserves_vietnamese_diacritics()
    {
        var v_objMapper = new CImportValueMapper(ProviderWithUnitMapping("CUON_EXT", "Cuốn"));

        var v_objExplicit = v_objMapper.Map("Test", "UnitReference", " cuon_ext ");
        var v_objUnmapped = v_objMapper.Map("Test", "UnitReference", "Cuon");

        Assert.Equal("Cuốn", v_objExplicit.MappedValue);
        Assert.True(v_objExplicit.WasMapped);
        Assert.Equal("Cuon", v_objUnmapped.MappedValue);
        Assert.False(v_objUnmapped.WasMapped);
    }

    [Fact]
    public void Conflicting_reference_map_values_fail_profile_loading()
    {
        var v_objConfiguration = ConfigurationWithUnitMappings(
            new("THUNG", "Thùng"),
            new(" thung ", "Bao"));

        var v_objException = Assert.Throws<InvalidDataException>(() => new CImportProfileProvider(v_objConfiguration));

        Assert.Contains("VALUE_MAPPING_CONFLICT", v_objException.Message, StringComparison.Ordinal);
    }

    private static CImportProfileProvider ProviderWithUnitMapping(string p_strSource, string p_strTarget) =>
        new(ConfigurationWithUnitMappings(new CImportValueMappingDefinition(p_strSource, p_strTarget)));

    private static CImportProfileConfiguration ConfigurationWithUnitMappings(params CImportValueMappingDefinition[] p_arrMappings) => new()
    {
        ReferenceMaps = new List<CImportReferenceMap>
        {
            new() { Name = "Unit", EntityType = "Unit", Mappings = p_arrMappings.ToList() }
        },
        Profiles = new List<CImportProfile>
        {
            new()
            {
                Code = "Test",
                Sheets = new List<CImportSheetProfile>
                {
                    new()
                    {
                        Role = "Data",
                        Fields = new List<CImportFieldProfile>
                        {
                            new() { Field = "UnitReference", Aliases = new List<string> { "UOM" }, ReferenceMap = "Unit", ReferenceEntityType = "Unit" }
                        }
                    }
                }
            }
        }
    };
}
