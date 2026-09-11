using Microsoft.Data.SqlClient;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public static class CWarehouseReportReadiness
{
    public const int ScopeBusyErrorNumber = 51323;
    public const int GenerationChangedErrorNumber = 51324;
    public const string RetryMessage = "Dữ liệu tồn kho vừa thay đổi hoặc đang được xử lý. Báo cáo chưa sẵn sàng; vui lòng thử lại.";

    public static bool IsRetryable(Exception? p_objException)
    {
        for (var v_objCurrent = p_objException; v_objCurrent is not null; v_objCurrent = v_objCurrent.InnerException)
        {
            if (v_objCurrent is SqlException v_objSqlException && IsRetryableSqlErrorNumber(v_objSqlException.Number))
                return true;
        }

        return false;
    }

    public static bool IsRetryableSqlErrorNumber(int p_iErrorNumber) =>
        p_iErrorNumber is ScopeBusyErrorNumber or GenerationChangedErrorNumber;
}
