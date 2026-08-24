using System.ComponentModel.DataAnnotations;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

public class CWarehouseDocument
{
    public long Auto_ID { get; set; }
    public bool Is_Receipt { get; set; }
    [Required, StringLength(100)]
    public string So_Phieu { get; set; } = "";
    [Range(1, long.MaxValue)]
    public long Kho_ID { get; set; }
    public string Ten_Kho { get; set; } = "";
    public long NCC_ID { get; set; }
    public string Ten_NCC { get; set; } = "";
    public DateTime Ngay_Chung_Tu { get; set; } = DateTime.Today;
    public bool Is_Posted { get; set; }
    public DateTime? Posted_At { get; set; }
    public string Ghi_Chu { get; set; } = "";
    public DateTime? Created { get; set; }
    public string Created_By { get; set; } = "";
    public string Created_By_Function { get; set; } = "";
    public DateTime? Last_Updated { get; set; }
    public string Last_Updated_By { get; set; } = "";
    public string Last_Updated_By_Function { get; set; } = "";
}

public class CWarehouseDocumentDetail
{
    public long Auto_ID { get; set; }
    public long Document_ID { get; set; }
    [Range(1, long.MaxValue)]
    public long San_Pham_ID { get; set; }
    public string Ma_San_Pham { get; set; } = "";
    public string Ten_San_Pham { get; set; } = "";
    public string Ten_Don_Vi_Tinh { get; set; } = "";
    [Range(typeof(decimal), "0.0001", "79228162514264337593543950335")]
    public decimal So_Luong { get; set; }
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Don_Gia { get; set; }
    public decimal Tri_Gia => So_Luong * Don_Gia;
    public DateTime? Created { get; set; }
    public string Created_By { get; set; } = "";
    public string Created_By_Function { get; set; } = "";
    public DateTime? Last_Updated { get; set; }
    public string Last_Updated_By { get; set; } = "";
    public string Last_Updated_By_Function { get; set; } = "";
}
