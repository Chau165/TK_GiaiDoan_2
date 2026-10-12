using System.ComponentModel.DataAnnotations;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

public class CDM_Loai_San_Pham
{
    public long Auto_ID { get; set; }
    [StringLength(100)]
    public string Ma_LSP { get; set; } = "";
    [StringLength(200)]
    public string Ten_LSP { get; set; } = "";
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
