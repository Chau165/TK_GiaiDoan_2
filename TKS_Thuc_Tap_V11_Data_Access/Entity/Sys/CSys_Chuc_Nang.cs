using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Entity.Sys
{
    public class CSys_Chuc_Nang
    {
        private long m_lngAuto_ID;
        private string m_strMa_Chuc_Nang;
        private string m_strTen_Chuc_Nang;
        private int m_iSort_Priority;
        private long m_lngChuc_Nang_Parent_ID;
        private int m_iNhom_Chuc_Nang_ID;
        private string m_strFunc_URL;
        private string m_strImage_URL;
        private bool m_bIs_View;
        private bool m_bIs_New;
        private bool m_bIs_Edit;
        private bool m_bIs_Delete;
        private bool m_bIs_Export;
        private string m_strGhi_Chu;
        private int m_iDeleted;
        private DateTime? m_dtmCreated;
        private string m_strCreated_By;
        private string m_strCreated_By_Function;
        private DateTime? m_dtmLast_Updated;
        private string m_strLast_Updated_By;
        private string m_strLast_Updated_By_Function;

		private bool m_bIs_Have_View_Permission;
		private bool m_bIs_Have_Add_Permission;
		private bool m_bIs_Have_Edit_Permission;
		private bool m_bIs_Have_Delete_Permission;
		private bool m_bIs_Have_Export_Permission;
        private string m_strKhach_Hang_ID;

        public CSys_Chuc_Nang()
        {
            ResetData();
        }

        public void ResetData()
        {
            m_lngAuto_ID = CConst.INT_VALUE_NULL;
            m_strMa_Chuc_Nang = CConst.STR_VALUE_NULL;
            m_strTen_Chuc_Nang = CConst.STR_VALUE_NULL;
            m_iSort_Priority = CConst.INT_VALUE_NULL;
            m_lngChuc_Nang_Parent_ID = CConst.INT_VALUE_NULL;
            m_iNhom_Chuc_Nang_ID = CConst.INT_VALUE_NULL;
            m_strFunc_URL = CConst.STR_VALUE_NULL;
            m_strImage_URL = CConst.STR_VALUE_NULL;
            m_bIs_View = CConst.BL_VALUE_NULL;
            m_bIs_New = CConst.BL_VALUE_NULL;
            m_bIs_Edit = CConst.BL_VALUE_NULL;
            m_bIs_Delete = CConst.BL_VALUE_NULL;
            m_bIs_Export = CConst.BL_VALUE_NULL;
            m_strGhi_Chu = CConst.STR_VALUE_NULL;
            m_iDeleted = CConst.INT_VALUE_NULL;
            m_dtmCreated = CConst.DTM_VALUE_NULL;
            m_strCreated_By = CConst.STR_VALUE_NULL;
            m_strCreated_By_Function = CConst.STR_VALUE_NULL;
            m_dtmLast_Updated = CConst.DTM_VALUE_NULL;
            m_strLast_Updated_By = CConst.STR_VALUE_NULL;
            m_strLast_Updated_By_Function = CConst.STR_VALUE_NULL;

			m_bIs_Have_View_Permission = CConst.BL_VALUE_NULL;
			m_bIs_Have_Add_Permission = CConst.BL_VALUE_NULL;
			m_bIs_Have_Edit_Permission = CConst.BL_VALUE_NULL;
			m_bIs_Have_Delete_Permission = CConst.BL_VALUE_NULL;
			m_bIs_Have_Export_Permission = CConst.BL_VALUE_NULL;
            m_strKhach_Hang_ID = CConst.STR_VALUE_NULL;
        }

        public long Auto_ID
        {
            get
            {
                return m_lngAuto_ID;
            }
            set
            {
                m_lngAuto_ID = value;
            }
        }

        public string Ma_Chuc_Nang
        {
            get
            {
                return m_strMa_Chuc_Nang;
            }
            set
            {
                m_strMa_Chuc_Nang = value.Trim();
            }
        }

        public string Ten_Chuc_Nang
        {
            get
            {
                return m_strTen_Chuc_Nang;
            }
            set
            {
                m_strTen_Chuc_Nang = value.Trim();
            }
        }

        public int Sort_Priority
        {
            get
            {
                return m_iSort_Priority;
            }
            set
            {
                m_iSort_Priority = value;
            }
        }

        public long Chuc_Nang_Parent_ID
        {
            get
            {
                return m_lngChuc_Nang_Parent_ID;
            }
            set
            {
                m_lngChuc_Nang_Parent_ID = value;
            }
        }

        public int Nhom_Chuc_Nang_ID
        {
            get
            {
                return m_iNhom_Chuc_Nang_ID;
            }
            set
            {
                m_iNhom_Chuc_Nang_ID = value;
            }
        }

        public string Func_URL
        {
            get
            {
                return m_strFunc_URL;
            }
            set
            {
                m_strFunc_URL = value.Trim();
            }
        }

        public string Image_URL
        {
            get
            {
                return m_strImage_URL;
            }
            set
            {
                m_strImage_URL = value.Trim();
            }
        }

        public bool Is_View
        {
            get
            {
                return m_bIs_View;
            }
            set
            {
                m_bIs_View = value;
            }
        }

        public bool Is_New
        {
            get
            {
                return m_bIs_New;
            }
            set
            {
                m_bIs_New = value;
            }
        }

        public bool Is_Edit
        {
            get
            {
                return m_bIs_Edit;
            }
            set
            {
                m_bIs_Edit = value;
            }
        }

        public bool Is_Delete
        {
            get
            {
                return m_bIs_Delete;
            }
            set
            {
                m_bIs_Delete = value;
            }
        }

        public bool Is_Export
        {
            get
            {
                return m_bIs_Export;
            }
            set
            {
                m_bIs_Export = value;
            }
        }

        public string Ghi_Chu
        {
            get
            {
                return m_strGhi_Chu;
            }
            set
            {
                m_strGhi_Chu = value.Trim();
            }
        }

        public int deleted
        {
            get
            {
                return m_iDeleted;
            }
            set
            {
                m_iDeleted = value;
            }
        }

        public DateTime? Created
        {
            get
            {
                return m_dtmCreated;
            }
            set
            {
                m_dtmCreated = value;
            }
        }

        public string Created_By
        {
            get
            {
                return m_strCreated_By;
            }
            set
            {
                m_strCreated_By = value.Trim();
            }
        }

        public string Created_By_Function
        {
            get
            {
                return m_strCreated_By_Function;
            }
            set
            {
                m_strCreated_By_Function = value.Trim();
            }
        }

        public DateTime? Last_Updated
        {
            get
            {
                return m_dtmLast_Updated;
            }
            set
            {
                m_dtmLast_Updated = value;
            }
        }

        public string Last_Updated_By
        {
            get
            {
                return m_strLast_Updated_By;
            }
            set
            {
                m_strLast_Updated_By = value.Trim();
            }
        }

        public string Last_Updated_By_Function
        {
            get
            {
                return m_strLast_Updated_By_Function;
            }
            set
            {
                m_strLast_Updated_By_Function = value.Trim();
            }
        }

        public string Nhom_Chuc_Nang_Text
        {
            get
            {
                string v_strRes = "";

                switch (Nhom_Chuc_Nang_ID)
                {
                    case (int)ENhom_Chuc_Nang_ID.Quan_Tri: v_strRes = "Quản trị"; break;
                    case (int)ENhom_Chuc_Nang_ID.PDA_App: v_strRes = "PDA"; break;
                    case (int)ENhom_Chuc_Nang_ID.API: v_strRes = "API"; break;
                    case (int)ENhom_Chuc_Nang_ID.Supplier_Portal: v_strRes = "Supplier Portal"; break;
                }

                return v_strRes;
            }
        }

		public bool Is_Have_View_Permission
		{
			get
			{
				return m_bIs_Have_View_Permission;
			}
			set
			{
				m_bIs_Have_View_Permission = value;
			}
		}

		public bool Is_Have_Add_Permission
		{
			get
			{
				return m_bIs_Have_Add_Permission;
			}
			set
			{
				m_bIs_Have_Add_Permission = value;
			}
		}

		public bool Is_Have_Edit_Permission
		{
			get
			{
				return m_bIs_Have_Edit_Permission;
			}
			set
			{
				m_bIs_Have_Edit_Permission = value;
			}
		}

		public bool Is_Have_Delete_Permission
		{
			get
			{
				return m_bIs_Have_Delete_Permission;
			}
			set
			{
				m_bIs_Have_Delete_Permission = value;
			}
		}

		public bool Is_Have_Export_Permission
		{
			get
			{
				return m_bIs_Have_Export_Permission;
			}
			set
			{
				m_bIs_Have_Export_Permission = value;
			}
		}

        public string Khach_Hang_ID
        {
            get
            {
                return m_strKhach_Hang_ID;
            }
            set
            {
                m_strKhach_Hang_ID = value.Trim();
            }
        }
    }
}
