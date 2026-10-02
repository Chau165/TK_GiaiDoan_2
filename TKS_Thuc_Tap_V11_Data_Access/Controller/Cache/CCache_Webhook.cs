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
	public class CCache_Webhook
	{
		private static List<CSys_Webhook> g_arrData = new List<CSys_Webhook>();
		private static Dictionary<string, CSys_Webhook> g_dicData_Code = new Dictionary<string, CSys_Webhook>();
		private static Dictionary<string, CSys_Webhook> g_dicData_Name = new Dictionary<string, CSys_Webhook>();
		private static Dictionary<long, CSys_Webhook> g_dicData_ID = new Dictionary<long, CSys_Webhook>();
		public static void Load_Cache_Webhook()
		{
			g_arrData.Clear();
			g_dicData_ID.Clear();
			g_dicData_Code.Clear();
			g_dicData_Name.Clear();

			CSys_Webhook_Controller v_objCtrData = new CSys_Webhook_Controller();
			List<CSys_Webhook> v_arrTemp_Data = v_objCtrData.FQ_533_W_sp_sel_List_For_Cache();
			foreach (CSys_Webhook v_objData in v_arrTemp_Data)
				Add_Data(v_objData);
		}

		public static void Add_Data(CSys_Webhook p_objData)
		{
			if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
				return;

			g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
			g_arrData.Add(p_objData);

			string v_strKey_Code = CUtility.Tao_Key(p_objData.Ma_Webhook);

			if (g_dicData_Code.ContainsKey(v_strKey_Code) == false)
				g_dicData_Code.Add(v_strKey_Code, p_objData);

			string v_strKey_Name = CUtility.Tao_Key(p_objData.Ten_Webhook);
			if (g_dicData_Name.ContainsKey(v_strKey_Name) == false)
				g_dicData_Name.Add(v_strKey_Name, p_objData);
		}

		public static void Update_Data(CSys_Webhook p_objData)
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

			CSys_Webhook v_objData = g_dicData_ID[p_iAuto_ID];

			g_arrData.Remove(v_objData);
			g_dicData_ID.Remove(p_iAuto_ID);

			string v_strKey_Code = CUtility.Tao_Key(v_objData.Ma_Webhook);
			string v_strKey_Name = CUtility.Tao_Key(v_objData.Ten_Webhook);
			g_dicData_Code.Remove(v_strKey_Code);
			g_dicData_Name.Remove(v_strKey_Name);
		}

		public static CSys_Webhook Get_Data_By_ID(long p_iID)
		{
			if (g_dicData_ID.ContainsKey(p_iID) == true)
				return g_dicData_ID[p_iID];

			return null;
		}

		public static CSys_Webhook Get_Data_By_Code(string p_strCode)
		{
			string v_strKey = CUtility.Tao_Key(p_strCode);

			if (g_dicData_Code.ContainsKey(v_strKey) == true)
				return g_dicData_Code[v_strKey];

			return null;
		}

		public static CSys_Webhook Get_Data_By_Name(string p_strName)
		{
			string v_strKey = CUtility.Tao_Key(p_strName);

			if (g_dicData_Name.ContainsKey(v_strKey) == true)
				return g_dicData_Name[v_strKey];

			return null;
		}

		public static List<CSys_Webhook> List_Data()
		{
			return g_arrData.ToList();
		}
	}
}
