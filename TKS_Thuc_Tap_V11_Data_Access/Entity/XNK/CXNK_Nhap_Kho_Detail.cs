using System.ComponentModel.DataAnnotations;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.XNK;

public class CXNK_Nhap_Kho_Detail
{
    public long Auto_ID { get; set; }
    public long Nhap_Kho_ID { get; set; }
    [Range(1, long.MaxValue)]
    public long San_Pham_ID { get; set; }
    public string Ma_San_Pham { get; set; } = "";
    public string Ten_San_Pham { get; set; } = "";
    public string Ten_Don_Vi_Tinh { get; set; } = "";
    [Range(0.0001, double.MaxValue)]
    public decimal SL_Nhap { get; set; }
    [Range(0.01, double.MaxValue)]
    public decimal Don_Gia_Nhap { get; set; }
    public decimal Tri_Gia => SL_Nhap * Don_Gia_Nhap;
    public string Ma_Dang_Nhap { get; set; } = "";
    public DateTime? Created { get; set; }
    public string Created_By { get; set; } = "";
    public string Created_By_Function { get; set; } = "";
    public DateTime? Last_Updated { get; set; }
    public string Last_Updated_By { get; set; } = "";
    public string Last_Updated_By_Function { get; set; } = "";
}
