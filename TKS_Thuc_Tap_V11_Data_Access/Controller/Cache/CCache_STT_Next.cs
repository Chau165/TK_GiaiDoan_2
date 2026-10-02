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
    public class CCache_STT_Next
    {
        private static List<CSys_STT_Next> g_arrData = new();
        private static Dictionary<string, CSys_STT_Next> g_dicData_Code = new();
        private static Dictionary<long, CSys_STT_Next> g_dicData_ID = new();
        private static Dictionary<long, List<CSys_STT_Next>> g_dicData_Chu_Hang = new();

        public static void Load_Cache_STT_Next()
        {
            g_arrData.Clear();
            g_dicData_ID.Clear();
            g_dicData_Code.Clear();
            g_dicData_Chu_Hang.Clear();

            CSys_STT_Next_Controller v_objCtrData = new();
            List<CSys_STT_Next> v_arrTemp_Data = v_objCtrData.FQ_529_SN_sp_sel_List_For_Cache();

            foreach (CSys_STT_Next v_objData in v_arrTemp_Data)
                Add_Data(v_objData);
        }

        public static void Add_Data(CSys_STT_Next p_objData)
        {
            if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            g_arrData.Add(p_objData);

            string v_strMa_Key = CUtility.Tao_Key(p_objData.Chu_Hang_ID, p_objData.Type_ID);

            if (g_dicData_Code.ContainsKey(v_strMa_Key) == false)
                g_dicData_Code.Add(v_strMa_Key, p_objData);

            if (g_dicData_Chu_Hang.ContainsKey(p_objData.Chu_Hang_ID) == false)
                g_dicData_Chu_Hang.Add(p_objData.Chu_Hang_ID, new List<CSys_STT_Next>());

            g_dicData_Chu_Hang[p_objData.Chu_Hang_ID].Add(p_objData);
        }

        public static void Update_Data(CSys_STT_Next p_objData)
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

            CSys_STT_Next v_objData = g_dicData_ID[p_iAuto_ID];

            g_arrData.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            string v_strMa_Key = CUtility.Tao_Key(v_objData.Chu_Hang_ID, v_objData.Type_ID);
            g_dicData_Code.Remove(v_strMa_Key);

            g_dicData_Chu_Hang[v_objData.Chu_Hang_ID].Remove(v_objData);
        }

        public static CSys_STT_Next Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
                return g_dicData_ID[p_iID];

            return null;
        }

        public static CSys_STT_Next Get_Data_By_Code(long p_iChu_Hang_ID, int p_iType_ID)
        {
            string v_strMa_Key = CUtility.Tao_Key(p_iChu_Hang_ID, p_iType_ID);
            if (g_dicData_Code.ContainsKey(v_strMa_Key) == true)
                return g_dicData_Code[v_strMa_Key];

            return null;
        }

        public static List<CSys_STT_Next> List_Data_Chu_Hang(long p_iChu_Hang_ID)
        {
            if (g_dicData_Chu_Hang.ContainsKey(p_iChu_Hang_ID) == true)
                return g_dicData_Chu_Hang[p_iChu_Hang_ID].ToList();

            return new List<CSys_STT_Next>();
        }

        public static List<CSys_STT_Next> List_Data()
        {
            return g_arrData.ToList();
        }
    }
}
