namespace TKS_Thuc_Tap_V11_Web_Common.Common;

public static class CStream_Copy_Utility
{
    public static Task Copy_Async(Stream p_objSource, Stream p_objDestination, CancellationToken p_objCancellation_Token = default)
    {
        ArgumentNullException.ThrowIfNull(p_objSource);
        ArgumentNullException.ThrowIfNull(p_objDestination);

        return p_objSource.CopyToAsync(p_objDestination, 81_920, p_objCancellation_Token);
    }
}
