using Telerik.DataSource;
using Telerik.DataSource.Extensions;

namespace TKS_Thuc_Tap_V11_Web_Common.Common;
public static class CGrid_DataSource_Utility
{
    public static bool Requires_Full_Source(DataSourceRequest p_objRequest, bool p_bIs_Excel_Export, bool p_bApplyGridOperations = true)
    {
        if (p_objRequest.PageSize == 0)
        {
            return p_bIs_Excel_Export;
        }

        return p_bApplyGridOperations && p_objRequest.PageSize > 0 && ((p_objRequest.Filters?.Count ?? 0) > 0 || (p_objRequest.Sorts?.Count ?? 0) > 0);
    }

    public static (List<T> Items, int Total_Count) Apply<T>(IEnumerable<T> p_arrSource, DataSourceRequest p_objRequest)
    {
        int v_iPage = 1;
        int v_iSkip = 0;
        int v_iPage_Size = int.MaxValue;
        if (p_objRequest.PageSize > 0)
        {
            v_iPage = Math.Max(1, p_objRequest.Page);
            v_iSkip = p_objRequest.Skip;
            v_iPage_Size = p_objRequest.PageSize;
        }
        var v_objRequest = new DataSourceRequest
        {
            Page = v_iPage,
            Skip = v_iSkip,
            PageSize = v_iPage_Size,
            Filters = p_objRequest.Filters?.ToList() ?? new List<IFilterDescriptor>(),
            Sorts = p_objRequest.Sorts?.ToList() ?? new List<SortDescriptor>()
        };
        var v_objResult = p_arrSource.ToDataSourceResult(v_objRequest);
        return (v_objResult.Data.Cast<T>().ToList(), v_objResult.Total);
    }
}
