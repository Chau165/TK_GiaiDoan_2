using System.ComponentModel.DataAnnotations;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

public class CDM_Kho_User
{
    public long Auto_ID { get; set; }

    [StringLength(100)]
    public string Ma_Dang_Nhap { get; set; } = "";

    [StringLength(255)]
    public string Ho_Ten { get; set; } = "";

    public long Kho_ID { get; set; }

    [StringLength(255)]
    public string Ten_Kho { get; set; } = "";
}
