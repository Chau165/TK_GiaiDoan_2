using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Cache
{
	public class CCache_Auto_Thread
	{
		public static List<CSys_Auto_Thread> Arr_Data = new List<CSys_Auto_Thread>();
		private static Dictionary<long, CSys_Auto_Thread> g_dicData_ID = new Dictionary<long, CSys_Auto_Thread>();
		private static Dictionary<int, List<CSys_Auto_Thread>> g_dicData_Thread = new Dictionary<int, List<CSys_Auto_Thread>>();
		
		public static void Load_Cache_Auto_Thread()
		{
			Arr_Data.Clear();
			g_dicData_ID.Clear();
			g_dicData_Thread.Clear();

			CSys_Auto_Thread_Controller v_objCtrData = new CSys_Auto_Thread_Controller();
            //List<CSys_Auto_Thread> v_arrTemp_Data = v_objCtrData.FCombo_sp_sel_List_Sys_Auto_Thread();
            List<CSys_Auto_Thread> v_arrTemp_Data = v_objCtrData.FQ_505_AT_sp_sel_List_For_Cache();

            foreach (CSys_Auto_Thread v_objData in v_arrTemp_Data)
				Add_Data(v_objData);
		}

		public static void Add_Data(CSys_Auto_Thread p_objData)
		{
			if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
				return;

			g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
			Arr_Data.Add(p_objData);

			if (g_dicData_Thread.ContainsKey(p_objData.STT_Thread_ID) == false)
				g_dicData_Thread.Add(p_objData.STT_Thread_ID, new List<CSys_Auto_Thread>());

			List<CSys_Auto_Thread> v_arrTemp = g_dicData_Thread[p_objData.STT_Thread_ID];
			v_arrTemp.Add(p_objData);
		}

		public static void Update_Data(CSys_Auto_Thread p_objData)
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

			CSys_Auto_Thread v_objData = g_dicData_ID[p_iAuto_ID];

			Arr_Data.Remove(v_objData);
			g_dicData_ID.Remove(p_iAuto_ID);

			g_dicData_Thread[v_objData.STT_Thread_ID].Remove(v_objData);
		}

		public static CSys_Auto_Thread Get_Data_By_ID(long p_iID)
		{
			if (g_dicData_ID.ContainsKey(p_iID) == true)
				return g_dicData_ID[p_iID];

			return null;
		}

		public static List<CSys_Auto_Thread> List_Data_By_STT_Thread(int p_iSTT_Thread)
		{
			if (g_dicData_Thread.ContainsKey(p_iSTT_Thread) == true)
				return g_dicData_Thread[p_iSTT_Thread].ToList();

			return new List<CSys_Auto_Thread>();
		}
	}
}
