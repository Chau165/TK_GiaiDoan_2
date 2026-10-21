using System;
using System.Collections.Generic;
using System.Linq;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Cache
{
    public class CCache_Kho
    {
        private static List<CDM_Kho> g_arrData = new List<CDM_Kho>();
        private static Dictionary<long, CDM_Kho> g_dicData_ID = new Dictionary<long, CDM_Kho>();
        private static Dictionary<string, CDM_Kho> g_dicData_Ten_Kho = new Dictionary<string, CDM_Kho>();

        public static void Load_Cache_Kho()
        {
            g_arrData.Clear();
            g_dicData_ID.Clear();
            g_dicData_Ten_Kho.Clear();

            CDM_Kho_Controller v_objCtrl = new CDM_Kho_Controller();
            List<CDM_Kho> v_arrTemp_Data = v_objCtrl.F2009_sp_sel_List_Kho_For_Cache();

            foreach (CDM_Kho v_objData in v_arrTemp_Data)
                Add_Data(v_objData);
        }

        public static void Add_Data(CDM_Kho p_objData)
        {
            if (p_objData == null || g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            g_arrData.Add(p_objData);

            if (string.IsNullOrWhiteSpace(p_objData.Ten_Kho) == false)
            {
                string v_strKey = p_objData.Ten_Kho.ToLower();
                if (g_dicData_Ten_Kho.ContainsKey(v_strKey) == false)
                    g_dicData_Ten_Kho.Add(v_strKey, p_objData);
            }
        }

        public static void Update_Data(CDM_Kho p_objData)
        {
            if (p_objData == null || g_dicData_ID.ContainsKey(p_objData.Auto_ID) == false || p_objData.Auto_ID == 0)
                return;

            CDM_Kho v_objOldData = g_dicData_ID[p_objData.Auto_ID];
            p_objData.Created = v_objOldData.Created;
            p_objData.Created_By = v_objOldData.Created_By;
            p_objData.Created_By_Function = v_objOldData.Created_By_Function;

            Delete_Data(p_objData.Auto_ID);
            Add_Data(p_objData);
        }

        public static void Delete_Data(long p_iAuto_ID)
        {
            if (g_dicData_ID.ContainsKey(p_iAuto_ID) == false || p_iAuto_ID == 0)
                return;

            CDM_Kho v_objData = g_dicData_ID[p_iAuto_ID];
            g_arrData.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            if (string.IsNullOrWhiteSpace(v_objData.Ten_Kho) == false)
            {
                string v_strKey = v_objData.Ten_Kho.ToLower();
                if (g_dicData_Ten_Kho.ContainsKey(v_strKey) == true && g_dicData_Ten_Kho[v_strKey].Auto_ID == p_iAuto_ID)
                    g_dicData_Ten_Kho.Remove(v_strKey);
            }
        }

        public static CDM_Kho Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
                return g_dicData_ID[p_iID];

            return null;
        }

        public static CDM_Kho Get_Data_By_Ten_Kho(string p_strTen_Kho)
        {
            if (string.IsNullOrWhiteSpace(p_strTen_Kho) == false && g_dicData_Ten_Kho.ContainsKey(p_strTen_Kho.ToLower()) == true)
                return g_dicData_Ten_Kho[p_strTen_Kho.ToLower()];

            return null;
        }

        public static List<CDM_Kho> List_Data()
        {
            return g_arrData.OrderBy(it => it.Ten_Kho).ToList();
        }
    }
}
