using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Cache
{
    public class CCache_API_Source_Chu_Hang
    {
        private static List<CSys_API_Source_Chu_Hang> g_arrData = new List<CSys_API_Source_Chu_Hang>();

        private static Dictionary<string, CSys_API_Source_Chu_Hang> g_dicData_Code = new Dictionary<string, CSys_API_Source_Chu_Hang>();
        private static Dictionary<long, CSys_API_Source_Chu_Hang> g_dicData_ID = new Dictionary<long, CSys_API_Source_Chu_Hang>();

        public static void Load_Cache_API_Source_Chu_Hang()
        {
            g_arrData.Clear();
            g_dicData_ID.Clear();
            g_dicData_Code.Clear();

            CSys_API_Source_Chu_Hang_Controller v_objCtrData = new CSys_API_Source_Chu_Hang_Controller();
			//List<CSys_API_Source_Chu_Hang> v_arrTemp_Data = v_objCtrData.FCombo_List_Sys_API_Source_Chu_Hang(); //
			List<CSys_API_Source_Chu_Hang> v_arrTemp_Data = v_objCtrData.FQ_502_ASCH_sp_sel_List_For_Cache(); //

			foreach (CSys_API_Source_Chu_Hang v_objData in v_arrTemp_Data)
                Add_Data(v_objData);
        }

        public static void Add_Data(CSys_API_Source_Chu_Hang p_objData)
        {
            if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            g_arrData.Add(p_objData);

            string v_strKey_Code = CUtility.Tao_Key(p_objData.Chu_Hang_ID, p_objData.API_Source_ID);
            if (g_dicData_Code.ContainsKey(v_strKey_Code) == false)
                g_dicData_Code.Add(v_strKey_Code, p_objData);
        }

        public static void Update_Data(CSys_API_Source_Chu_Hang p_objData)
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

            CSys_API_Source_Chu_Hang v_objData = g_dicData_ID[p_iAuto_ID];

            g_arrData.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            string v_strKey_Code = CUtility.Tao_Key(v_objData.Chu_Hang_ID, v_objData.API_Source_ID);
            g_dicData_Code.Remove(v_strKey_Code);
        }

        public static CSys_API_Source_Chu_Hang Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
                return g_dicData_ID[p_iID];

            return null;
        }

        public static CSys_API_Source_Chu_Hang Get_Data_By_Code(long p_iChu_Hang_ID, long p_iAPI_Source_ID)
        {
            string v_strKey_Code = CUtility.Tao_Key(p_iChu_Hang_ID, p_iAPI_Source_ID);

            if (g_dicData_Code.ContainsKey(v_strKey_Code) == true)
                return g_dicData_Code[v_strKey_Code];

            return null;
        }
        public static List<CSys_API_Source_Chu_Hang> List_Data_By_API_Source(long p_iAPI_Source_ID)
        {
            return g_arrData.Where(t => t.API_Source_ID == p_iAPI_Source_ID).ToList();
        }
    }
}
