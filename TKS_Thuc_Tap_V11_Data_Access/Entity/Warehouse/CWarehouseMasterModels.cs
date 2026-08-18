namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

public class CWarehouseMaster
{
    public long Auto_ID { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long Related_ID { get; set; }
    public long Related_ID_2 { get; set; }
    public string Login_Name { get; set; } = "";
    public string Ghi_Chu { get; set; } = "";
}
