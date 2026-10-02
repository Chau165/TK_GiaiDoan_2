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
	public class CCache_API_Source
	{
		private static List<CSys_API_Source> g_arrData = new List<CSys_API_Source>();

		private static Dictionary<string, CSys_API_Source> g_dicData_Code = new Dictionary<string, CSys_API_Source>();
		private static Dictionary<long, CSys_API_Source> g_dicData_ID = new Dictionary<long, CSys_API_Source>();
        private static Dictionary<string, CSys_API_Source> g_dicData_Token_1 = new Dictionary<string, CSys_API_Source>();

        public static void Load_Cache_API_Source()
		{
			g_arrData.Clear();
			g_dicData_ID.Clear();
			g_dicData_Code.Clear();
			g_dicData_Token_1.Clear();

            CSys_API_Source_Controller v_objCtrData = new CSys_API_Source_Controller();
			//List<CSys_API_Source> v_arrTemp_Data = v_objCtrData.FCombo_List_Sys_API_Source(); //
			List<CSys_API_Source> v_arrTemp_Data = v_objCtrData.FQ_501_AS_sp_sel_List_For_Cache();

            foreach (CSys_API_Source v_objData in v_arrTemp_Data)
				Add_Data(v_objData);
		}

		public static void Add_Data(CSys_API_Source p_objData)
		{
			if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
				return;

			g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
			g_arrData.Add(p_objData);

			string v_strKey_Code = CUtility.Tao_Key(p_objData.Ma_API_Source);
			if (g_dicData_Code.ContainsKey(v_strKey_Code) == false)
				g_dicData_Code.Add(v_strKey_Code, p_objData);

            string v_strKey_Token = CUtility.Tao_Key(p_objData.Token_1);
            if (g_dicData_Token_1.ContainsKey(v_strKey_Token) == false)
                g_dicData_Token_1.Add(v_strKey_Token, p_objData);
        }

		public static void Update_Data(CSys_API_Source p_objData)
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

			CSys_API_Source v_objData = g_dicData_ID[p_iAuto_ID];

			g_arrData.Remove(v_objData);
			g_dicData_ID.Remove(p_iAuto_ID);

			string v_strKey_Code = CUtility.Tao_Key(v_objData.Ma_API_Source);
			g_dicData_Code.Remove(v_strKey_Code);

            string v_strKey_Token = CUtility.Tao_Key(v_objData.Token_1);
            g_dicData_Token_1.Remove(v_strKey_Token);
        }

		public static CSys_API_Source Get_Data_By_ID(long p_iID)
		{
			if (g_dicData_ID.ContainsKey(p_iID) == true)
				return g_dicData_ID[p_iID];

			return null;
		}

		public static CSys_API_Source Get_Data_By_Code(string p_strMa_API_Source)
		{
			string v_strKey = CUtility.Tao_Key(p_strMa_API_Source);

			if (g_dicData_Code.ContainsKey(v_strKey) == true)
				return g_dicData_Code[v_strKey];

			return null;
		}

        public static CSys_API_Source Get_Data_By_Token_1(string p_strToken_1)
        {
            string v_strKey = CUtility.Tao_Key(p_strToken_1);

            if (g_dicData_Token_1.ContainsKey(v_strKey) == true)
                return g_dicData_Token_1[v_strKey];

            return null;
        }

		public static List<CSys_API_Source> List_Data()
		{
			return g_arrData.ToList();
		}
	}
}
