namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public static class CWarehouseActionHistory_Recorder
{
    public static bool TryRecord(Action p_action, Action<Exception> p_onFailure)
    {
        try
        {
            p_action();
            return true;
        }
        catch (Exception v_objException)
        {
            try
            {
                p_onFailure(v_objException);
            }
            catch (Exception v_objFailureHandlerException)
            {
                Utility.CLogger.Warning(
                    nameof(CWarehouseActionHistory_Recorder),
                    nameof(TryRecord),
                    v_objFailureHandlerException.ToString());
            }

            return false;
        }
    }
}
