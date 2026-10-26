using System;
using System.Data;
using System.Globalization;
using System.Reflection;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility
{
    // 23/10/2026 - Codex: chuyển xử lý decimal/UTC đặc thù Warehouse ra khỏi Utility gốc (yêu cầu P0).
    public static class CWMS_Utility
    {
        public static decimal Convert_To_Decimal(object p_objData)
        {
            if ((p_objData != System.DBNull.Value) && (CUtility.Convert_To_String(p_objData) != ""))
            {
                return Convert.ToDecimal(p_objData);
            }
            else
            {
                return 0m;
            }
        }

        public static string Format_So_Tien(decimal p_decGia_Tri)
        {
            // Hiển thị theo mệnh giá tiền Việt Nam, ví dụ 6200 -> 6.200, 372000 -> 372.000
            return p_decGia_Tri.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        }

        public static string Format_So_Luong(decimal p_decSo_Luong)
        {
            // Giữ tối đa 3 chữ số thập phân theo DECIMAL(18,3), không làm tròn mất phần nghìn.
            return p_decSo_Luong.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static T Map_Warehouse_Row<T>(DataRow p_Row)
            where T : new()
        {
            // create a new object
            T v_objItem = new T();
            foreach (DataColumn v_colValue in p_Row.Table.Columns)
            {
                // find the property for the column
                PropertyInfo v_objItem_Info = v_objItem.GetType().GetProperty(v_colValue.ColumnName);
                // if exists, set the value
                if (v_objItem_Info != null && p_Row[v_colValue] != DBNull.Value)
                {
                    //an sửa ngày 16/02/2022
                    MethodInfo v_objMethodInfo = v_objItem_Info.SetMethod;
                    if (v_objMethodInfo != null)
                    {
                        string v_strTypedata = Nullable.GetUnderlyingType(v_objItem_Info.PropertyType)?.Name ?? v_objItem_Info.PropertyType.Name;
                        switch (v_strTypedata)
                        {
                            case "String":
                                v_objItem_Info.SetValue(v_objItem, CUtility.Convert_To_String(p_Row[v_colValue]));
                                break;
                            case "Int16":
                                v_objItem_Info.SetValue(v_objItem, CUtility.Convert_To_Int32(p_Row[v_colValue]));
                                break;
                            case "Int32":
                                v_objItem_Info.SetValue(v_objItem, CUtility.Convert_To_Int32(p_Row[v_colValue]));
                                break;
                            case "Int64":
                                v_objItem_Info.SetValue(v_objItem, CUtility.Convert_To_Int64(p_Row[v_colValue]));
                                break;
                            case "DateTime":
                                v_objItem_Info.SetValue(v_objItem, CUtility.Convert_To_DateTime(p_Row[v_colValue]));
                                break;
                            case "DateTime?":
                                v_objItem_Info.SetValue(v_objItem, CUtility.Convert_To_DateTime(p_Row[v_colValue]));
                                break;
                            case "Double":
                                v_objItem_Info.SetValue(v_objItem, CUtility.Convert_To_Double(p_Row[v_colValue]), null);
                                break;
                            case "Decimal":
                                v_objItem_Info.SetValue(v_objItem, CWMS_Utility.Convert_To_Decimal(p_Row[v_colValue]), null);
                                break;
                            case "Boolean":
                                v_objItem_Info.SetValue(v_objItem, CUtility.Convert_To_Bool(p_Row[v_colValue]), null);
                                break;
                        }
                    }
                }
            }

            // return 
            return v_objItem;
        }

        public static DateTime? Convert_Local_Day_Start_To_Utc(DateTime? p_dtmDate)
        {
            if (p_dtmDate == null)
            {
                return null;
            }

            DateTime v_dtmLocal = DateTime.SpecifyKind(p_dtmDate.Value.Date, DateTimeKind.Local);
            return v_dtmLocal.ToUniversalTime();
        }

        public static DateTime? Convert_Local_Next_Day_Start_To_Utc(DateTime? p_dtmDate)
        {
            if (p_dtmDate == null)
            {
                return null;
            }

            DateTime v_dtmLocal = DateTime.SpecifyKind(p_dtmDate.Value.Date.AddDays(1), DateTimeKind.Local);
            return v_dtmLocal.ToUniversalTime();
        }
    }
}
