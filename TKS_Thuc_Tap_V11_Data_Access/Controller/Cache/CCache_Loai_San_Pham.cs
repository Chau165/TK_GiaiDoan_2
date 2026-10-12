using System;
using System.Collections.Generic;
using System.Linq;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Cache
{
    public class CCache_Loai_San_Pham
    {
        private static List<CDM_Loai_San_Pham> g_arrData = new List<CDM_Loai_San_Pham>();
        private static Dictionary<long, CDM_Loai_San_Pham> g_dicData_ID = new Dictionary<long, CDM_Loai_San_Pham>();
        private static Dictionary<string, CDM_Loai_San_Pham> g_dicData_Ma_LSP = new Dictionary<string, CDM_Loai_San_Pham>();
        private static Dictionary<string, CDM_Loai_San_Pham> g_dicData_Ten_LSP = new Dictionary<string, CDM_Loai_San_Pham>();

        public static void Load_Cache_Loai_San_Pham()
        {
            g_arrData.Clear();
            g_dicData_ID.Clear();
            g_dicData_Ma_LSP.Clear();
            g_dicData_Ten_LSP.Clear();

            CDM_Loai_San_Pham_Controller v_objCtrl = new CDM_Loai_San_Pham_Controller();
            List<CDM_Loai_San_Pham> v_arrTemp_Data = v_objCtrl.F2017_sp_sel_List_Loai_San_Pham();

            foreach (CDM_Loai_San_Pham v_objData in v_arrTemp_Data)
                Add_Data(v_objData);
        }

        public static void Add_Data(CDM_Loai_San_Pham p_objData)
        {
            if (p_objData == null || g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            g_arrData.Add(p_objData);

            if (string.IsNullOrWhiteSpace(p_objData.Ma_LSP) == false)
            {
                string v_strKey = p_objData.Ma_LSP.ToLower();
                if (g_dicData_Ma_LSP.ContainsKey(v_strKey) == false)
                    g_dicData_Ma_LSP.Add(v_strKey, p_objData);
            }

            if (string.IsNullOrWhiteSpace(p_objData.Ten_LSP) == false)
            {
                string v_strKey = p_objData.Ten_LSP.ToLower();
                if (g_dicData_Ten_LSP.ContainsKey(v_strKey) == false)
                    g_dicData_Ten_LSP.Add(v_strKey, p_objData);
            }
        }

        public static void Update_Data(CDM_Loai_San_Pham p_objData)
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

            CDM_Loai_San_Pham v_objData = g_dicData_ID[p_iAuto_ID];
            g_arrData.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            if (string.IsNullOrWhiteSpace(v_objData.Ma_LSP) == false)
            {
                string v_strKey = v_objData.Ma_LSP.ToLower();
                if (g_dicData_Ma_LSP.ContainsKey(v_strKey) == true && g_dicData_Ma_LSP[v_strKey].Auto_ID == p_iAuto_ID)
                    g_dicData_Ma_LSP.Remove(v_strKey);
            }

            if (string.IsNullOrWhiteSpace(v_objData.Ten_LSP) == false)
            {
                string v_strKey = v_objData.Ten_LSP.ToLower();
                if (g_dicData_Ten_LSP.ContainsKey(v_strKey) == true && g_dicData_Ten_LSP[v_strKey].Auto_ID == p_iAuto_ID)
                    g_dicData_Ten_LSP.Remove(v_strKey);
            }
        }

        public static CDM_Loai_San_Pham Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
                return g_dicData_ID[p_iID];

            return null;
        }

        public static CDM_Loai_San_Pham Get_Data_By_Ma_LSP(string p_strMa_LSP)
        {
            if (string.IsNullOrWhiteSpace(p_strMa_LSP) == false && g_dicData_Ma_LSP.ContainsKey(p_strMa_LSP.ToLower()) == true)
                return g_dicData_Ma_LSP[p_strMa_LSP.ToLower()];

            return null;
        }

        public static CDM_Loai_San_Pham Get_Data_By_Ten_LSP(string p_strTen_LSP)
        {
            if (string.IsNullOrWhiteSpace(p_strTen_LSP) == false && g_dicData_Ten_LSP.ContainsKey(p_strTen_LSP.ToLower()) == true)
                return g_dicData_Ten_LSP[p_strTen_LSP.ToLower()];

            return null;
        }

        public static List<CDM_Loai_San_Pham> List_Data()
        {
            return g_arrData.OrderBy(it => it.Ma_LSP).ToList();
        }
    }
}
