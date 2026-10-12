using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public class CWarehouseDocumentSupport_Controller : CWarehouse_Controller_Base
{
    public CWarehousePagedResult<CWarehouseDocument> sp_XNK_Document_Page(
        bool p_bIs_Receipt,
        int p_iPage_Number,
        int p_iPage_Size,
        string p_strSearch_Text = "",
        string p_strCurrent_Login = "",
        long? p_iWarehouse_ID = null)
    {
        return Page_From_Procedure<CWarehouseDocument>("sp_XNK_Document_Page", p_bIs_Receipt,
            p_iPage_Number, p_iPage_Size, p_strSearch_Text, p_strCurrent_Login, p_iWarehouse_ID);
    }

    public void sp_XNK_Document_Post(
        bool p_bIs_Receipt,
        long p_iDocument_ID,
        string p_strCurrent_Login,
        string p_strLast_Updated_By,
        string p_strLast_Updated_By_Function)
    {
        Execute_Procedure("sp_XNK_Document_Post",
            new SqlParameter("@Is_Receipt", System.Data.SqlDbType.Bit) { Value = p_bIs_Receipt },
            new SqlParameter("@Document_ID", System.Data.SqlDbType.BigInt) { Value = p_iDocument_ID },
            new SqlParameter("@Ma_Dang_Nhap", System.Data.SqlDbType.NVarChar, 100) { Value = p_strCurrent_Login },
            new SqlParameter("@Last_Updated_By", System.Data.SqlDbType.NVarChar, 100) { Value = p_strLast_Updated_By },
            new SqlParameter("@Last_Updated_By_Function", System.Data.SqlDbType.NVarChar, 100) { Value = p_strLast_Updated_By_Function });
    }
}
