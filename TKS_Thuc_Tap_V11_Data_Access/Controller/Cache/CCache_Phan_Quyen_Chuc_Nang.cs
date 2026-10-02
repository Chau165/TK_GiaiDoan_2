using Azure.Core;
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
	public class CCache_Phan_Quyen_Chuc_Nang
	{
		static List<CSys_Phan_Quyen_Chuc_Nang> g_arrData = new List<CSys_Phan_Quyen_Chuc_Nang>();

		private static Dictionary<long, CSys_Phan_Quyen_Chuc_Nang> g_dicData_ID = new Dictionary<long, CSys_Phan_Quyen_Chuc_Nang>();
		private static Dictionary<long, List<CSys_Phan_Quyen_Chuc_Nang>> g_dicData_Nhom_Quyen = new Dictionary<long, List<CSys_Phan_Quyen_Chuc_Nang>>();
		private static Dictionary<string, CSys_Phan_Quyen_Chuc_Nang> g_dicData_MCN_NQ = new Dictionary<string, CSys_Phan_Quyen_Chuc_Nang>();

		public static void Load_Cache_Phan_Quyen_Chuc_Nang()
		{
			g_arrData.Clear();
			g_dicData_ID.Clear();
			g_dicData_Nhom_Quyen.Clear();
			g_dicData_MCN_NQ.Clear();

			CSys_Phan_Quyen_Chuc_Nang_Controller v_objCtrData = new CSys_Phan_Quyen_Chuc_Nang_Controller();
            List<CSys_Phan_Quyen_Chuc_Nang> v_arrTemp_Data = v_objCtrData.FQ_527_PQCN_sp_sel_List_For_Cache();

			foreach (CSys_Phan_Quyen_Chuc_Nang v_objData in v_arrTemp_Data)
                Add_Data(v_objData);
		}

        public static void Add_Data(CSys_Phan_Quyen_Chuc_Nang p_objData)
        {
            if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            g_arrData.Add(p_objData);

            if (g_dicData_Nhom_Quyen.ContainsKey(p_objData.Nhom_Thanh_Vien_ID) == false)
                g_dicData_Nhom_Quyen.Add(p_objData.Nhom_Thanh_Vien_ID, new List<CSys_Phan_Quyen_Chuc_Nang>());

            g_dicData_Nhom_Quyen[p_objData.Nhom_Thanh_Vien_ID].Add(p_objData);

            string v_strKey = p_objData.Nhom_Thanh_Vien_ID.ToString() + ";" + p_objData.Chuc_Nang_ID.ToString();
            if (g_dicData_MCN_NQ.ContainsKey(v_strKey.ToLower()) == false)
                g_dicData_MCN_NQ.Add(v_strKey.ToLower(), p_objData);
        }

        public static void Update_Data(CSys_Phan_Quyen_Chuc_Nang p_objData)
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

            CSys_Phan_Quyen_Chuc_Nang v_objData = g_dicData_ID[p_iAuto_ID];

            g_arrData.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            g_dicData_Nhom_Quyen[v_objData.Nhom_Thanh_Vien_ID].Remove(v_objData);

            string v_strKey = v_objData.Nhom_Thanh_Vien_ID.ToString() + ";" + v_objData.Chuc_Nang_ID.ToString();
            g_dicData_MCN_NQ.Remove(v_strKey.ToLower());
        }


        public static CSys_Phan_Quyen_Chuc_Nang Get_Data_By_ID(long p_iID)
		{
			if (g_dicData_ID.ContainsKey(p_iID) == true)
				return g_dicData_ID[p_iID];

			return null;
		}

		public static List<CSys_Phan_Quyen_Chuc_Nang> List_Data_By_Nhom_Quyen_ID(long p_iNhom_Quyen_ID)
		{
			if (g_dicData_Nhom_Quyen.ContainsKey(p_iNhom_Quyen_ID) == true)
				return g_dicData_Nhom_Quyen[p_iNhom_Quyen_ID].ToList();

			return new List<CSys_Phan_Quyen_Chuc_Nang>();
		}

		public static CSys_Phan_Quyen_Chuc_Nang Get_Data_By_NQ_MCN_ID(long p_iNhom_Quyen_ID, long p_iChuc_Nang_ID)
		{
			string v_strKey = p_iNhom_Quyen_ID.ToString() + ";" + p_iChuc_Nang_ID.ToString();
			if (g_dicData_MCN_NQ.ContainsKey(v_strKey.ToLower()) == true)
				return g_dicData_MCN_NQ[v_strKey.ToLower()];

			return null;
		}

	}
}
