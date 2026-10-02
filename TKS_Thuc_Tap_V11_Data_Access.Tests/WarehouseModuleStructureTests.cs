using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public class WarehouseModuleStructureTests
{
    [Fact]
    public void Warehouse_module_exposes_separate_controllers_by_responsibility()
    {
        Assert.NotNull(typeof(CWarehouseMaster_Controller));
        Assert.NotNull(typeof(CWarehouseDocument_Controller));
        Assert.NotNull(typeof(CWarehouseReport_Controller));
    }

    [Fact]
    public void Document_detail_keeps_total_value_calculation()
    {
        var v_objDetail = new CWarehouseDocumentDetail
        {
            So_Luong = 2.5m,
            Don_Gia = 40_000m
        };

        Assert.Equal(100_000m, v_objDetail.Tri_Gia);
    }

    [Fact]
    public async Task Master_list_reads_bigint_ids_and_int_zero_placeholders()
    {
        CConfig.TKS_Thuc_Tap_V11_Conn_String = WarehouseTestDatabase.ConnectionString;

        var v_arrResult = await new CWarehouseMaster_Controller().List_Master_Async("DonViTinh");

        Assert.Contains(v_arrResult, item => item.Name == "Cái");
        Assert.All(v_arrResult, item => Assert.Equal(0L, item.Related_ID));
        Assert.All(v_arrResult, item => Assert.Equal(0L, item.Related_ID_2));
    }
}
