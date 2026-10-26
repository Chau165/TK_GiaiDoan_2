using System;
using System.Collections.Generic;
using System.Linq;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Cache
{
    public class CCache_San_Pham
    {
        private static List<CDM_San_Pham> g_arrData = new List<CDM_San_Pham>();
        private static Dictionary<long, CDM_San_Pham> g_dicData_ID = new Dictionary<long, CDM_San_Pham>();
        private static Dictionary<string, CDM_San_Pham> g_dicData_Ma_San_Pham = new Dictionary<string, CDM_San_Pham>();
        public static void Load_Cache_San_Pham()
        {
            g_arrData.Clear();
            g_dicData_ID.Clear();
            g_dicData_Ma_San_Pham.Clear();
            CDM_San_Pham_Controller v_objCtrl = new CDM_San_Pham_Controller();
            List<CDM_San_Pham> v_arrTemp_Data = v_objCtrl.F2018_sp_sel_List_San_Pham_For_Cache();
            foreach (CDM_San_Pham v_objData in v_arrTemp_Data)
            {
                Add_Data(v_objData);
            }
        }

        public static void Add_Data(CDM_San_Pham p_objData)
        {
            if (p_objData == null || g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
            {
                return;
            }

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            g_arrData.Add(p_objData);
            if (string.IsNullOrWhiteSpace(p_objData.Ma_San_Pham) == false)
            {
                string v_strKey = p_objData.Ma_San_Pham.ToLower();
                if (g_dicData_Ma_San_Pham.ContainsKey(v_strKey) == false)
                {
                    g_dicData_Ma_San_Pham.Add(v_strKey, p_objData);
                }
            }
        }

        public static void Update_Data(CDM_San_Pham p_objData)
        {
            if (p_objData == null || g_dicData_ID.ContainsKey(p_objData.Auto_ID) == false || p_objData.Auto_ID == 0)
            {
                return;
            }

            CDM_San_Pham v_objOldData = g_dicData_ID[p_objData.Auto_ID];
            p_objData.Created = v_objOldData.Created;
            p_objData.Created_By = v_objOldData.Created_By;
            p_objData.Created_By_Function = v_objOldData.Created_By_Function;
            Delete_Data(p_objData.Auto_ID);
            Add_Data(p_objData);
        }

        public static void Delete_Data(long p_iAuto_ID)
        {
            if (g_dicData_ID.ContainsKey(p_iAuto_ID) == false || p_iAuto_ID == 0)
            {
                return;
            }

            CDM_San_Pham v_objData = g_dicData_ID[p_iAuto_ID];
            g_arrData.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);
            if (string.IsNullOrWhiteSpace(v_objData.Ma_San_Pham) == false)
            {
                string v_strKey = v_objData.Ma_San_Pham.ToLower();
                if (g_dicData_Ma_San_Pham.ContainsKey(v_strKey) == true && g_dicData_Ma_San_Pham[v_strKey].Auto_ID == p_iAuto_ID)
                {
                    g_dicData_Ma_San_Pham.Remove(v_strKey);
                }
            }
        }

        public static CDM_San_Pham Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
            {
                return g_dicData_ID[p_iID];
            }

            return null;
        }

        public static CDM_San_Pham Get_Data_By_Ma_San_Pham(string p_strMa_San_Pham)
        {
            if (string.IsNullOrWhiteSpace(p_strMa_San_Pham) == false && g_dicData_Ma_San_Pham.ContainsKey(p_strMa_San_Pham.ToLower()) == true)
            {
                return g_dicData_Ma_San_Pham[p_strMa_San_Pham.ToLower()];
            }

            return null;
        }

        public static List<CDM_San_Pham> List_Data()
        {
            return g_arrData.OrderBy(it => it.Ma_San_Pham).ToList();
        }
    }
}
