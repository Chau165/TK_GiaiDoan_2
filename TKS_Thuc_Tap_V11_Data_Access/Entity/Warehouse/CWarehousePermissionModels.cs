using System.ComponentModel.DataAnnotations;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

public class CWarehousePermission
{
    public long Permission_ID { get; set; }
    [StringLength(100)]
    public string Login_Name { get; set; } = "";
    [StringLength(255)]
    public string User_Name { get; set; } = "";
    public long Warehouse_ID { get; set; }
    [StringLength(255)]
    public string Warehouse_Name { get; set; } = "";
}
