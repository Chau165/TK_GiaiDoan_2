using System.ComponentModel.DataAnnotations;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.XNK;

public class CXNK_Nhap_Kho
{
    public long Auto_ID { get; set; }
    public bool Is_Receipt { get; set; } = true;
    [Required, StringLength(100)]
    public string So_Phieu_Nhap_Kho { get; set; } = "";
    [Range(1, long.MaxValue)]
    public long Kho_ID { get; set; }
    public string Ten_Kho { get; set; } = "";
    public long NCC_ID { get; set; }
    public string Ten_NCC { get; set; } = "";
    public DateTime Ngay_Nhap_Kho { get; set; } = DateTime.Today;
    public bool Is_Posted { get; set; }
    public DateTime? Posted_At { get; set; }
    public string Ghi_Chu { get; set; } = "";
    public string Ma_Dang_Nhap { get; set; } = "";
    public DateTime? Created { get; set; }
    public string Created_By { get; set; } = "";
    public string Created_By_Function { get; set; } = "";
    public DateTime? Last_Updated { get; set; }
    public string Last_Updated_By { get; set; } = "";
    public string Last_Updated_By_Function { get; set; } = "";
}
