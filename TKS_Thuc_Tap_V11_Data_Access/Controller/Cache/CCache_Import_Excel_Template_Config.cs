using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Sys;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Cache
{
	public class CCache_Import_Excel_Template_Config
	{
		private static List<CSys_Import_Excel_Template_Config> g_arrData = new();
		private static Dictionary<long, CSys_Import_Excel_Template_Config> g_dicData_ID = new();
		private static Dictionary<long, CSys_Import_Excel_Template_Config> g_dicData_Chu_Hang = new();
		public static void Load_Cache_Import_Excel_Template_Config()
		{
			g_arrData.Clear();
			g_dicData_ID.Clear();
			g_dicData_Chu_Hang.Clear();

			CSys_Import_Excel_Template_Config_Controller v_objCtrData = new();
			List<CSys_Import_Excel_Template_Config> v_arrTemp_Data = v_objCtrData.FQ_520_IETC_sp_sel_List_For_Cache(); //
			foreach (CSys_Import_Excel_Template_Config v_objData in v_arrTemp_Data)
				Add_Data(v_objData);
		}

		public static void Add_Data(CSys_Import_Excel_Template_Config p_objData)
		{
			if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
				return;

			g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
			g_arrData.Add(p_objData);

			if (g_dicData_Chu_Hang.ContainsKey(p_objData.Chu_Hang_ID) == false)
				g_dicData_Chu_Hang.Add(p_objData.Chu_Hang_ID, p_objData);
		}

		public static void Update_Data(CSys_Import_Excel_Template_Config p_objData)
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

			CSys_Import_Excel_Template_Config v_objData = g_dicData_ID[p_iAuto_ID];

			g_arrData.Remove(v_objData);
			g_dicData_ID.Remove(p_iAuto_ID);

			g_dicData_Chu_Hang.Remove(v_objData.Chu_Hang_ID);
		}

		public static CSys_Import_Excel_Template_Config Get_Data_By_ID(long p_iID)
		{
			if (g_dicData_ID.ContainsKey(p_iID) == true)
				return g_dicData_ID[p_iID];

			return null;
		}

		public static CSys_Import_Excel_Template_Config Get_Data_By_Chu_Hang(long p_iChu_Hang_ID)
		{
			if (g_dicData_Chu_Hang.ContainsKey(p_iChu_Hang_ID) == true)
				return g_dicData_Chu_Hang[p_iChu_Hang_ID];

            if (g_dicData_Chu_Hang.ContainsKey(-5) == true)
                return g_dicData_Chu_Hang[-5];

            return null;
		}
	}
}
