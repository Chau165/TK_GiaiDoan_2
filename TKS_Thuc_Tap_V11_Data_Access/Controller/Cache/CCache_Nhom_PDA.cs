using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Cache
{
	public class CCache_Nhom_PDA
	{
        private static List<CSys_Nhom_PDA> g_arrData = new List<CSys_Nhom_PDA>();

        private static Dictionary<string, CSys_Nhom_PDA> g_dicData_Code = new Dictionary<string, CSys_Nhom_PDA>();
        private static Dictionary<long, CSys_Nhom_PDA> g_dicData_ID = new Dictionary<long, CSys_Nhom_PDA>();

        public static void Load_Cache_Nhom_PDA()
        {
            g_arrData.Clear();
            g_dicData_ID.Clear();
            g_dicData_Code.Clear();

            CSys_Nhom_PDA_Controller v_objCtrData = new CSys_Nhom_PDA_Controller();
            List<CSys_Nhom_PDA> v_arrTemp_Data = v_objCtrData.FQ_523_NP_sp_sel_List_For_Cache();

            foreach (CSys_Nhom_PDA v_objData in v_arrTemp_Data)
                Add_Data(v_objData);
        }

        public static void Add_Data(CSys_Nhom_PDA p_objData)
        {
            if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            g_arrData.Add(p_objData);

            string v_strKey = CUtility.Tao_Key(p_objData.Ten_Nhom_PDA);

            if (g_dicData_Code.ContainsKey(v_strKey) == false)
                g_dicData_Code.Add(v_strKey, p_objData);

        }

        public static void Update_Data(CSys_Nhom_PDA p_objData)
        {
            if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == false || p_objData.Auto_ID == 0)
                return;

            Delete_Data(p_objData.Auto_ID);
            Add_Data(p_objData);
        }

        public static void Delete_Data(long p_iAuto_ID)
        {
            if (g_dicData_ID.ContainsKey(p_iAuto_ID) == false || p_iAuto_ID == 0)
                return;

            CSys_Nhom_PDA v_objData = g_dicData_ID[p_iAuto_ID];

            g_arrData.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            string v_strKey = CUtility.Tao_Key(v_objData.Ten_Nhom_PDA);
            g_dicData_Code.Remove(v_strKey);

        }

        public static CSys_Nhom_PDA Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
                return g_dicData_ID[p_iID];

            return null;
        }

        public static CSys_Nhom_PDA Get_Data_By_Ten_Nhom_PDA(string p_strCode)
        {
            string v_strKey_Code = CUtility.Tao_Key(p_strCode);

            if (g_dicData_Code.ContainsKey(v_strKey_Code) == true)
                return g_dicData_Code[v_strKey_Code];

            return null;
        }

        public static List<CSys_Nhom_PDA> List_Data()
        {
            return g_arrData.OrderBy(it => it.Ten_Nhom_PDA).ToList();
        }
    }
}
