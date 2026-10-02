using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Common;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility
{
	public class CUtility_Queue_DB
	{
		private static Dictionary<long, CQueue> g_dicQueue = new Dictionary<long, CQueue>();
		public static List<CQueue> Arr_Queue = new List<CQueue>();
		private static long g_Current_Queued = 1;

		private static bool g_bIs_Current_Used = false;

		public static long Add_Queue(string p_strMa_Chuc_Nang, string p_strTen_Chuc_Nang, string p_strFunction_Name)
		{
			while (g_bIs_Current_Used == true)
			{ }

			g_bIs_Current_Used = true;

            long v_iRes = g_Current_Queued;
			g_Current_Queued++;
			
			try
			{
				CQueue v_objQueue = new CQueue()
				{
					Auto_ID = v_iRes,
					Ngay_Gio_Bat_Dau = DateTime.Now,
					Ma_Chuc_Nang = p_strMa_Chuc_Nang,
					Ten_Chuc_Nang = p_strTen_Chuc_Nang,
					Ten_Function = p_strFunction_Name
				};

				if (g_dicQueue.ContainsKey(v_iRes) == false)
				{
					g_dicQueue.Add(v_iRes, v_objQueue);
					Arr_Queue.Add(v_objQueue);
				}
			}
			catch (Exception v_Ex)
			{
				CLogger.Error("Queue_DB", "Add_Queue", "[" + p_strFunction_Name + "], Current Queued [" + v_iRes.ToString() + "]:" + v_Ex.Message);
			}

            g_bIs_Current_Used = false;

            return v_iRes;

		}

		public static void Remove_Queue(long p_iQueue_ID)
		{
            while (g_bIs_Current_Used == true)
            { }

            g_bIs_Current_Used = true;

            try
			{
				if (g_dicQueue.ContainsKey(p_iQueue_ID) == false)
				{
                    g_bIs_Current_Used = false;
                    return;
				}

				CQueue v_objQueue = g_dicQueue[p_iQueue_ID];
				Arr_Queue.Remove(v_objQueue);
				g_dicQueue.Remove(p_iQueue_ID);
			}

			catch (Exception v_Ex)
			{
				CLogger.Error("Queue_DB", "Remove_Queue", "[" + p_iQueue_ID.ToString() + "]:" + v_Ex.Message);
			}

            g_bIs_Current_Used = false;

        }
	}
}
