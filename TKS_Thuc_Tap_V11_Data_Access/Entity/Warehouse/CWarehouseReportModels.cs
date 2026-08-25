namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

public class CWarehouseDetailReport
{
    public DateTime Ngay { get; set; }
    public string So_Phieu { get; set; } = "";
    public string Nha_Cung_Cap { get; set; } = "";
    public string Ma_San_Pham { get; set; } = "";
    public string Ten_San_Pham { get; set; } = "";
    public decimal So_Luong { get; set; }
    public decimal Don_Gia { get; set; }
    public decimal Tri_Gia { get; set; }
}

public class CWarehouseInventoryReport
{
    public long Kho_ID { get; set; }
    public string Ten_Kho { get; set; } = "";
    public long San_Pham_ID { get; set; }
    public string Ma_San_Pham { get; set; } = "";
    public string Ten_San_Pham { get; set; } = "";
    public decimal SL_Dau_Ky { get; set; }
    public decimal SL_Nhap { get; set; }
    public decimal SL_Xuat { get; set; }
    public decimal SL_Cuoi_Ky { get; set; }
    public decimal SL_Ton_Thuc_Te { get; set; }
    public decimal SL_Dang_Giu { get; set; }
    public decimal SL_Kha_Dung { get; set; }
}
