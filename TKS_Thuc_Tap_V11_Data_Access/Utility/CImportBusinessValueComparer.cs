using System.Text;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;

public static class CImportBusinessValueComparer
{
    public static bool AreEqual(string? p_strLeft, string? p_strRight) =>
        string.Equals(Normalize(p_strLeft), Normalize(p_strRight), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? p_strValue)
    {
        if (string.IsNullOrWhiteSpace(p_strValue))
            return "";

        var v_strSource = p_strValue.Normalize(NormalizationForm.FormC);
        var v_objBuilder = new StringBuilder(v_strSource.Length);
        var v_bPendingSpace = false;
        foreach (var v_ch in v_strSource.Trim())
        {
            if (char.IsWhiteSpace(v_ch))
            {
                v_bPendingSpace = v_objBuilder.Length > 0;
                continue;
            }

            if (v_bPendingSpace)
                v_objBuilder.Append(' ');
            v_objBuilder.Append(v_ch);
            v_bPendingSpace = false;
        }

        return v_objBuilder.ToString();
    }
}
