using System;
using System.Collections.Generic;
using System.Linq;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Cache
{
    public class CCache_NCC
    {
        private static List<CDM_NCC> g_arrData = new List<CDM_NCC>();
        private static Dictionary<long, CDM_NCC> g_dicData_ID = new Dictionary<long, CDM_NCC>();
        private static Dictionary<string, CDM_NCC> g_dicData_Ma_NCC = new Dictionary<string, CDM_NCC>();
        private static Dictionary<string, CDM_NCC> g_dicData_Ten_NCC = new Dictionary<string, CDM_NCC>();

        public static void Load_Cache_NCC()
        {
            g_arrData.Clear();
            g_dicData_ID.Clear();
            g_dicData_Ma_NCC.Clear();
            g_dicData_Ten_NCC.Clear();

            CDM_NCC_Controller v_objCtrl = new CDM_NCC_Controller();
            List<CDM_NCC> v_arrTemp_Data = v_objCtrl.F2019_sp_sel_List_NCC_For_Cache();

            foreach (CDM_NCC v_objData in v_arrTemp_Data)
                Add_Data(v_objData);
        }

        public static void Add_Data(CDM_NCC p_objData)
        {
            if (p_objData == null || g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            g_arrData.Add(p_objData);

            if (string.IsNullOrWhiteSpace(p_objData.Ma_NCC) == false)
            {
                string v_strKey = p_objData.Ma_NCC.ToLower();
                if (g_dicData_Ma_NCC.ContainsKey(v_strKey) == false)
                    g_dicData_Ma_NCC.Add(v_strKey, p_objData);
            }

            if (string.IsNullOrWhiteSpace(p_objData.Ten_NCC) == false)
            {
                string v_strKey = p_objData.Ten_NCC.ToLower();
                if (g_dicData_Ten_NCC.ContainsKey(v_strKey) == false)
                    g_dicData_Ten_NCC.Add(v_strKey, p_objData);
            }
        }

        public static void Update_Data(CDM_NCC p_objData)
        {
            if (p_objData == null || g_dicData_ID.ContainsKey(p_objData.Auto_ID) == false || p_objData.Auto_ID == 0)
                return;

            Delete_Data(p_objData.Auto_ID);
            Add_Data(p_objData);
        }

        public static void Delete_Data(long p_iAuto_ID)
        {
            if (g_dicData_ID.ContainsKey(p_iAuto_ID) == false || p_iAuto_ID == 0)
                return;

            CDM_NCC v_objData = g_dicData_ID[p_iAuto_ID];
            g_arrData.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            if (string.IsNullOrWhiteSpace(v_objData.Ma_NCC) == false)
            {
                string v_strKey = v_objData.Ma_NCC.ToLower();
                if (g_dicData_Ma_NCC.ContainsKey(v_strKey) == true && g_dicData_Ma_NCC[v_strKey].Auto_ID == p_iAuto_ID)
                    g_dicData_Ma_NCC.Remove(v_strKey);
            }

            if (string.IsNullOrWhiteSpace(v_objData.Ten_NCC) == false)
            {
                string v_strKey = v_objData.Ten_NCC.ToLower();
                if (g_dicData_Ten_NCC.ContainsKey(v_strKey) == true && g_dicData_Ten_NCC[v_strKey].Auto_ID == p_iAuto_ID)
                    g_dicData_Ten_NCC.Remove(v_strKey);
            }
        }

        public static CDM_NCC Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
                return g_dicData_ID[p_iID];

            return null;
        }

        public static CDM_NCC Get_Data_By_Ma_NCC(string p_strMa_NCC)
        {
            if (string.IsNullOrWhiteSpace(p_strMa_NCC) == false && g_dicData_Ma_NCC.ContainsKey(p_strMa_NCC.ToLower()) == true)
                return g_dicData_Ma_NCC[p_strMa_NCC.ToLower()];

            return null;
        }

        public static CDM_NCC Get_Data_By_Ten_NCC(string p_strTen_NCC)
        {
            if (string.IsNullOrWhiteSpace(p_strTen_NCC) == false && g_dicData_Ten_NCC.ContainsKey(p_strTen_NCC.ToLower()) == true)
                return g_dicData_Ten_NCC[p_strTen_NCC.ToLower()];

            return null;
        }

        public static List<CDM_NCC> List_Data()
        {
            return g_arrData.OrderBy(it => it.Ma_NCC).ToList();
        }
    }
}
