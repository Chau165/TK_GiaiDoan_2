namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

public class CWarehouseDocument
{
    public long Auto_ID { get; set; }
    public bool Is_Receipt { get; set; }
    public string So_Phieu { get; set; } = "";
    public long Kho_ID { get; set; }
    public string Ten_Kho { get; set; } = "";
    public long NCC_ID { get; set; }
    public string Ten_NCC { get; set; } = "";
    public DateTime Ngay_Chung_Tu { get; set; } = DateTime.Today;
    public string Ghi_Chu { get; set; } = "";
}

public class CWarehouseDocumentDetail
{
    public long Auto_ID { get; set; }
    public long Document_ID { get; set; }
    public long San_Pham_ID { get; set; }
    public string Ma_San_Pham { get; set; } = "";
    public string Ten_San_Pham { get; set; } = "";
    public decimal So_Luong { get; set; }
    public decimal Don_Gia { get; set; }
    public decimal Tri_Gia => So_Luong * Don_Gia;
}
