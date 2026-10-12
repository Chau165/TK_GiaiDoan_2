using System;
using System.Collections.Generic;
using System.Linq;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Cache
{
    public class CCache_Don_Vi_Tinh
    {
        private static List<CDM_Don_Vi_Tinh> g_arrData = new List<CDM_Don_Vi_Tinh>();
        private static Dictionary<long, CDM_Don_Vi_Tinh> g_dicData_ID = new Dictionary<long, CDM_Don_Vi_Tinh>();
        private static Dictionary<string, CDM_Don_Vi_Tinh> g_dicData_Ten_Don_Vi_Tinh = new Dictionary<string, CDM_Don_Vi_Tinh>();

        public static void Load_Cache_Don_Vi_Tinh()
        {
            g_arrData.Clear();
            g_dicData_ID.Clear();
            g_dicData_Ten_Don_Vi_Tinh.Clear();

            CDM_Don_Vi_Tinh_Controller v_objCtrl = new CDM_Don_Vi_Tinh_Controller();
            List<CDM_Don_Vi_Tinh> v_arrTemp_Data = v_objCtrl.F2016_sp_sel_List_Don_Vi_Tinh();

            foreach (CDM_Don_Vi_Tinh v_objData in v_arrTemp_Data)
                Add_Data(v_objData);
        }

        public static void Add_Data(CDM_Don_Vi_Tinh p_objData)
        {
            if (p_objData == null || g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            g_arrData.Add(p_objData);

            if (string.IsNullOrWhiteSpace(p_objData.Ten_Don_Vi_Tinh) == false)
            {
                string v_strKey = p_objData.Ten_Don_Vi_Tinh.ToLower();
                if (g_dicData_Ten_Don_Vi_Tinh.ContainsKey(v_strKey) == false)
                    g_dicData_Ten_Don_Vi_Tinh.Add(v_strKey, p_objData);
            }
        }

        public static void Update_Data(CDM_Don_Vi_Tinh p_objData)
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

            CDM_Don_Vi_Tinh v_objData = g_dicData_ID[p_iAuto_ID];
            g_arrData.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            if (string.IsNullOrWhiteSpace(v_objData.Ten_Don_Vi_Tinh) == false)
            {
                string v_strKey = v_objData.Ten_Don_Vi_Tinh.ToLower();
                if (g_dicData_Ten_Don_Vi_Tinh.ContainsKey(v_strKey) == true && g_dicData_Ten_Don_Vi_Tinh[v_strKey].Auto_ID == p_iAuto_ID)
                    g_dicData_Ten_Don_Vi_Tinh.Remove(v_strKey);
            }
        }

        public static CDM_Don_Vi_Tinh Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
                return g_dicData_ID[p_iID];

            return null;
        }

        public static CDM_Don_Vi_Tinh Get_Data_By_Ten_Don_Vi_Tinh(string p_strTen_Don_Vi_Tinh)
        {
            if (string.IsNullOrWhiteSpace(p_strTen_Don_Vi_Tinh) == false && g_dicData_Ten_Don_Vi_Tinh.ContainsKey(p_strTen_Don_Vi_Tinh.ToLower()) == true)
                return g_dicData_Ten_Don_Vi_Tinh[p_strTen_Don_Vi_Tinh.ToLower()];

            return null;
        }

        public static List<CDM_Don_Vi_Tinh> List_Data()
        {
            return g_arrData.OrderBy(it => it.Ten_Don_Vi_Tinh).ToList();
        }
    }
}
