using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Common;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;

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
        // 23/10/2026 - Codex: gọi helper công ty trực tiếp; metadata SP giữ type/size của tham số.
        TKS_Thuc_Tap_V11_Data_Access.DataLayer.CSqlHelper.ExecuteNonquery(
            TKS_Thuc_Tap_V11_Data_Access.Utility.CConfig.TKS_Thuc_Tap_V11_Conn_String,
            "sp_XNK_Document_Post", p_bIs_Receipt, p_iDocument_ID, p_strCurrent_Login,
            p_strLast_Updated_By, p_strLast_Updated_By_Function);
    }
}
