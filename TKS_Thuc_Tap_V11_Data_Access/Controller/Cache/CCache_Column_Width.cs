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
    public class CCache_Column_Width
    {
        public static List<CSys_Column_Width> Arr_Data = new List<CSys_Column_Width>();

        private static Dictionary<string, CSys_Column_Width> g_dicData_Code = new Dictionary<string, CSys_Column_Width>();
        private static Dictionary<long, CSys_Column_Width> g_dicData_ID = new Dictionary<long, CSys_Column_Width>();

        public static void Load_Cache_Column_Width()
        {
			Arr_Data.Clear();
			g_dicData_Code.Clear();
			g_dicData_ID.Clear();

			CSys_Column_Width_Controller v_objCtrData = new CSys_Column_Width_Controller();
			//List<CSys_Column_Width> v_arrTemp = v_objCtrData.FCombo_sp_sel_List_Sys_Column_Width();
			List<CSys_Column_Width> v_arrTemp = v_objCtrData.FQ_509_CW_sp_sel_List_For_Cache();

			foreach (CSys_Column_Width v_objData in v_arrTemp)
                Add_Data(v_objData);
        }

        public static void Add_Data(CSys_Column_Width p_objData)
        {
            if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            Arr_Data.Add(p_objData);

            if (g_dicData_Code.ContainsKey(p_objData.Field_Name.ToLower()) == false)
                g_dicData_Code.Add(p_objData.Field_Name.ToLower(), p_objData);
        }

        public static void Update_Data(CSys_Column_Width p_objData)
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

            CSys_Column_Width v_objData = g_dicData_ID[p_iAuto_ID];

            Arr_Data.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            g_dicData_Code.Remove(v_objData.Field_Name.ToLower());
        }

        public static CSys_Column_Width Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
                return g_dicData_ID[p_iID];

            return null;
        }

        public static CSys_Column_Width Get_Data_By_Code(string p_strCode)
        {
            if (g_dicData_Code.ContainsKey(p_strCode.ToLower()) == true)
                return g_dicData_Code[p_strCode.ToLower()];

            return null;
        }
    }
}
