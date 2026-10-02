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
	public class CCache_Grid_UI_Global
	{
		private static List<CSys_Grid_UI_Global> g_arrData = new();
		private static Dictionary<long, CSys_Grid_UI_Global> g_dicData_ID = new();
		private static Dictionary<string, List<CSys_Grid_UI_Global>> g_dicData_Code = new ();
        private static Dictionary<string, List<CSys_Grid_UI_Global>> g_dicData_Ma_CN = new();
		private static Dictionary<string, CSys_Grid_UI_Global> g_dicData_Key = new();

		public static void Load_Cache_Grid_UI_Global()
		{
			g_arrData.Clear();
			g_dicData_ID.Clear();
			g_dicData_Code.Clear();
			g_dicData_Ma_CN.Clear();
			g_dicData_Key.Clear();

			CSys_Grid_UI_Global_Controller v_objCtrData = new ();
			List<CSys_Grid_UI_Global> v_arrTemp_Data = v_objCtrData.FQ_516_GUG_sp_sel_List_For_Cache(); //
			foreach (CSys_Grid_UI_Global v_objData in v_arrTemp_Data)
				Add_Data(v_objData);
		}

		public static void Add_Data(CSys_Grid_UI_Global p_objData)
		{
			if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
				return;

			g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
			g_arrData.Add(p_objData);

			string v_strKey_Code = CUtility.Tao_Key(p_objData.Ma_Chuc_Nang, p_objData.Ten_Grid);

			if (g_dicData_Code.ContainsKey(v_strKey_Code) == false)
				g_dicData_Code.Add(v_strKey_Code, new List<CSys_Grid_UI_Global>());

            g_dicData_Code[v_strKey_Code].Add(p_objData);

            string v_strMa_CN = CUtility.Tao_Key(p_objData.Ma_Chuc_Nang);

            if (g_dicData_Ma_CN.ContainsKey(v_strMa_CN) == false)
                g_dicData_Ma_CN.Add(v_strMa_CN, new List<CSys_Grid_UI_Global>());

            g_dicData_Ma_CN[v_strMa_CN].Add(p_objData);

			string v_strKey = CUtility.Tao_Key(p_objData.Ma_Chuc_Nang, p_objData.Ten_Grid, p_objData.Field_Name);

			if (g_dicData_Key.ContainsKey(v_strKey) == false)
				g_dicData_Key.Add(v_strKey, p_objData);
		}

		public static void Update_Data(CSys_Grid_UI_Global p_objData)
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

			CSys_Grid_UI_Global v_objData = g_dicData_ID[p_iAuto_ID];

			g_arrData.Remove(v_objData);
			g_dicData_ID.Remove(p_iAuto_ID);

			string v_strKey_Code = CUtility.Tao_Key(v_objData.Ma_Chuc_Nang, v_objData.Ten_Grid);
			g_dicData_Code[v_strKey_Code].Remove(v_objData);

            string v_strMa_CN = CUtility.Tao_Key(v_objData.Ma_Chuc_Nang);
			g_dicData_Ma_CN[v_strMa_CN].Remove(v_objData);

			string v_strKey = CUtility.Tao_Key(v_objData.Ma_Chuc_Nang, v_objData.Ten_Grid, v_objData.Field_Name);
			g_dicData_Key.Remove(v_strKey);
		}

        public static void Delete_Data_By_Grid_Field_ID(long p_iGrid_Field_ID)
        {
            List<CSys_Grid_UI_Global> v_arrData = g_arrData.Where(it => it.Grid_Field_ID == p_iGrid_Field_ID).ToList();
            foreach (CSys_Grid_UI_Global v_objGrid_UI_Global in v_arrData)
                Delete_Data(v_objGrid_UI_Global.Auto_ID);
        }
        public static CSys_Grid_UI_Global Get_Data_By_ID(long p_iID)
		{
			if (g_dicData_ID.ContainsKey(p_iID) == true)
				return g_dicData_ID[p_iID];

			return null;
		}

		public static CSys_Grid_UI_Global Get_Data_By_Key(string p_strMa_Chuc_Nang, string p_strTen_Grid, string p_strField_Name)
		{
			string v_strKey = CUtility.Tao_Key(p_strMa_Chuc_Nang, p_strTen_Grid, p_strField_Name);
			if (g_dicData_Key.ContainsKey(v_strKey) == true)
				return g_dicData_Key[v_strKey];

			return null;
		}

		public static List<CSys_Grid_UI_Global> List_Data_By_Code(string p_strMa_Chuc_Nang, string p_strTen_Grid)
		{
			string v_strKey = CUtility.Tao_Key(p_strMa_Chuc_Nang, p_strTen_Grid);

			if (g_dicData_Code.ContainsKey(v_strKey) == true)
				return g_dicData_Code[v_strKey].OrderBy(it=>it.Ten_Grid == p_strTen_Grid).ThenBy(it => it.Sort_Priority).ToList();

			return new List<CSys_Grid_UI_Global>();
		}

        public static List<CSys_Grid_UI_Global> List_Data_By_Ma_Chuc_Nang(string p_strMa_Chuc_Nang)
        {
            if (g_dicData_Ma_CN.ContainsKey(p_strMa_Chuc_Nang) == true)
                return g_dicData_Ma_CN[p_strMa_Chuc_Nang].OrderBy(it=>it.Sort_Priority).ToList();

            return new List<CSys_Grid_UI_Global>();
        }
        public static List<CSys_Grid_UI_Global> List_Data_All()
		{
			return g_arrData.ToList();
		}
	}
}
