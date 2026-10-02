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
    public class CCache_Token
    {
        public static List<CSys_Token> Arr_Data = new List<CSys_Token>();

        private static Dictionary<string, CSys_Token> g_dicData_Ma_Dang_Nhap = new Dictionary<string, CSys_Token>();
        private static Dictionary<string, CSys_Token> g_dicData_Token = new Dictionary<string, CSys_Token>();
        private static Dictionary<long, CSys_Token> g_dicData_ID = new Dictionary<long, CSys_Token>();

        public static void Load_Cache_Token()
        {
			Arr_Data.Clear();
			g_dicData_Ma_Dang_Nhap.Clear();
			g_dicData_Token.Clear();
			g_dicData_ID.Clear();

			CSys_Token_Controller v_objCtrData = new CSys_Token_Controller();
            List<CSys_Token> v_arrTemp_Data = v_objCtrData.FQ_532_T_sp_sel_List_For_Cache();

            foreach (CSys_Token v_objData in v_arrTemp_Data)
                Add_Data(v_objData);
        }

        public static void Add_Data(CSys_Token p_objData)
        {
            if (g_dicData_ID.ContainsKey(p_objData.Auto_ID) == true || p_objData.Auto_ID == 0)
                return;

            g_dicData_ID.Add(p_objData.Auto_ID, p_objData);
            Arr_Data.Add(p_objData);

            if (g_dicData_Ma_Dang_Nhap.ContainsKey(p_objData.Ma_Dang_Nhap.ToLower()) == false)
                g_dicData_Ma_Dang_Nhap.Add(p_objData.Ma_Dang_Nhap.ToLower(), p_objData);

            if (g_dicData_Token.ContainsKey(p_objData.Token_ID.ToLower()) == false)
                g_dicData_Token.Add(p_objData.Token_ID.ToLower(), p_objData);
        }

        public static void Update_Data(CSys_Token p_objData)
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

            CSys_Token v_objData = g_dicData_ID[p_iAuto_ID];

            Arr_Data.Remove(v_objData);
            g_dicData_ID.Remove(p_iAuto_ID);

            g_dicData_Ma_Dang_Nhap.Remove(v_objData.Ma_Dang_Nhap.ToLower());
            g_dicData_Token.Remove(v_objData.Token_ID.ToLower());
        }

        public static CSys_Token Get_Data_By_ID(long p_iID)
        {
            if (g_dicData_ID.ContainsKey(p_iID) == true)
                return g_dicData_ID[p_iID];

            return null;
        }

        public static CSys_Token Get_Data_By_Ma_Dang_Nhap(string p_strMa_Dang_Nhap)
        {
            if (g_dicData_Ma_Dang_Nhap.ContainsKey(p_strMa_Dang_Nhap.ToLower()) == true)
                return g_dicData_Ma_Dang_Nhap[p_strMa_Dang_Nhap.ToLower()];

            return null;
        }

        public static CSys_Token Get_Data_By_Token(string p_strToken)
        {
            if (g_dicData_Token.ContainsKey(p_strToken.ToLower()) == true)
                return g_dicData_Token[p_strToken.ToLower()];

            return null;
        }

        public static void Set_Token(string p_strMa_Dang_Nhap, string p_strToken)
        {
            bool v_bInsert_Successfully = false;

            CSys_Token_Controller v_objCtrToken = new CSys_Token_Controller();

            if (Get_Data_By_Token(p_strToken) != null)
                throw new Exception("Token generate bị trùng, sự cố rất hi hữu mới xảy ra, xin vui lòng đăng nhập lại một lần nữa là hết.");

            if (Get_Data_By_Ma_Dang_Nhap(p_strMa_Dang_Nhap) == null)
            {
                CSys_Token v_objToken_Temp = new CSys_Token()
                {
                    Ma_Dang_Nhap = p_strMa_Dang_Nhap,
                    Token_ID = p_strToken,
                    Last_Updated_By = p_strMa_Dang_Nhap,
                    Last_Updated_By_Function = "Sign In"
                };

                v_objToken_Temp.Auto_ID = v_objCtrToken.FQ_532_T_sp_ins_Insert(v_objToken_Temp);
                v_bInsert_Successfully = true;

                g_dicData_Ma_Dang_Nhap.Add(p_strMa_Dang_Nhap.ToLower(), v_objToken_Temp);
                g_dicData_Token.Add(p_strToken.ToLower(), v_objToken_Temp);
            }

            CSys_Token v_objToken = Get_Data_By_Ma_Dang_Nhap(p_strMa_Dang_Nhap);

            string v_strOld_Token = v_objToken.Token_ID;

            v_objToken.Token_ID = p_strToken;

            // Cập nhật token mới xuống database
            if (v_bInsert_Successfully == false)
                v_objCtrToken.FQ_532_T_sp_upd_Update(v_objToken);

            g_dicData_Token.Remove(v_strOld_Token.ToLower());
            g_dicData_Token.Add(p_strToken.ToLower(), v_objToken);
        }
    }
}
