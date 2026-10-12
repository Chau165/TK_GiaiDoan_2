using Telerik.DataSource;
using Telerik.DataSource.Extensions;

namespace TKS_Thuc_Tap_V11_Web_Common.Common;

public static class CGrid_DataSource_Utility
{
    public static bool Requires_Full_Source(DataSourceRequest p_objRequest, bool p_bIs_Excel_Export, bool p_bApplyGridOperations = true)
    {
        if (p_objRequest.PageSize == 0)
            return p_bIs_Excel_Export;

        return p_bApplyGridOperations
            && p_objRequest.PageSize > 0
            && ((p_objRequest.Filters?.Count ?? 0) > 0 || (p_objRequest.Sorts?.Count ?? 0) > 0);
    }

    public static (List<T> Items, int Total_Count) Apply<T>(IEnumerable<T> p_arrSource, DataSourceRequest p_objRequest)
    {
        var v_objRequest = new DataSourceRequest
        {
            Page = p_objRequest.PageSize <= 0 ? 1 : Math.Max(1, p_objRequest.Page),
            Skip = p_objRequest.PageSize <= 0 ? 0 : p_objRequest.Skip,
            PageSize = p_objRequest.PageSize <= 0 ? int.MaxValue : p_objRequest.PageSize,
            Filters = p_objRequest.Filters?.ToList() ?? new List<IFilterDescriptor>(),
            Sorts = p_objRequest.Sorts?.ToList() ?? new List<SortDescriptor>()
        };

        var v_objResult = p_arrSource.ToDataSourceResult(v_objRequest);
        return (v_objResult.Data.Cast<T>().ToList(), v_objResult.Total);
    }
}
