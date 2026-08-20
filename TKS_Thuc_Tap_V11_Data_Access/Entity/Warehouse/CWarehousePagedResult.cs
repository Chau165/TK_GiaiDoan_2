namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

public sealed class CWarehousePagedResult<T>
{
    public List<T> Items { get; } = new();
    public int Total_Count { get; init; }
}
