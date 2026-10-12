using System.ComponentModel.DataAnnotations;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

public class CDM_San_Pham
{
    public long Auto_ID { get; set; }
    [StringLength(100)]
    public string Ma_San_Pham { get; set; } = "";
    [StringLength(255)]
    public string Ten_San_Pham { get; set; } = "";
    public long Loai_San_Pham_ID { get; set; }
    public long Don_Vi_Tinh_ID { get; set; }
    public string Ghi_Chu { get; set; } = "";
    public DateTime? Created { get; set; }
    [StringLength(100)]
    public string Created_By { get; set; } = "";
    [StringLength(100)]
    public string Created_By_Function { get; set; } = "";
    public DateTime? Last_Updated { get; set; }
    [StringLength(100)]
    public string Last_Updated_By { get; set; } = "";
    [StringLength(100)]
    public string Last_Updated_By_Function { get; set; } = "";
}