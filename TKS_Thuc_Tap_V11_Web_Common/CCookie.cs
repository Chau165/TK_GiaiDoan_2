using Microsoft.JSInterop;

namespace TKS_Thuc_Tap_V11_Web_Common.Common
{
    public class CCookie
    {
        readonly IJSRuntime m_JSRuntime;
        string m_expires = "";

        public CCookie(IJSRuntime jsRuntime)
        {
            m_JSRuntime = jsRuntime;
            ExpireDays = 300;
        }

        public async Task SetValue(string key, string value, int? days = null)
        {
            var v_curExp = "";
            if (days != null)
            {
                if (days > 0)
                {
                    v_curExp = DateToUTC(days.Value);
                }
                else
                {
                    v_curExp = "";
                }
            }
            else
            {
                v_curExp = m_expires;
            }
            await SetCookie($"{key}={value}; expires={v_curExp}; path=/");
        }

        public async Task<string> GetValue(string key, string def = "")
        {
            var v_cValue = await GetCookie();
            if (string.IsNullOrEmpty(v_cValue)) return def;

            var v_arrVals = v_cValue.Split(';');
            foreach (var v_val in v_arrVals)
                if (!string.IsNullOrEmpty(v_val) && v_val.IndexOf('=') > 0)
                    if (v_val.Substring(0, v_val.IndexOf('=')).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        return v_val.Substring(v_val.IndexOf('=') + 1);
            return def;
        }

        private async Task SetCookie(string p_value)
        {
            await m_JSRuntime.InvokeVoidAsync("eval", $"document.cookie = \"{p_value}\"");
        }

        private async Task<string> GetCookie()
        {
            return await m_JSRuntime.InvokeAsync<string>("eval", $"document.cookie");
        }

        public int ExpireDays
        {
            set
            {
                m_expires = DateToUTC(value);
            }
        }

        private static string DateToUTC(int p_iDays)
        {
            return DateTime.Now.AddDays(p_iDays).ToUniversalTime().ToString("R");
        }
    }
}
