using System.ComponentModel.DataAnnotations;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

public class CWarehouseMaster
{
    public long Auto_ID { get; set; }
    [StringLength(100)]
    public string Code { get; set; } = "";
    [StringLength(255)]
    public string Name { get; set; } = "";
    public long Related_ID { get; set; }
    public long Related_ID_2 { get; set; }
    [StringLength(100)]
    public string Created_By { get; set; } = "";
    [StringLength(100)]
    public string Created_By_Function { get; set; } = "";
    [StringLength(100)]
    public string Last_Updated_By { get; set; } = "";
    [StringLength(100)]
    public string Last_Updated_By_Function { get; set; } = "";
    public DateTime? Created { get; set; }
    public DateTime? Last_Updated { get; set; }
    public string Ghi_Chu { get; set; } = "";
}
