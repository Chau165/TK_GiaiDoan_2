using System.Text.RegularExpressions;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class ActionHistoryScopeTests
{
    [Fact]
    public void History_reader_uses_the_company_legacy_procedure()
    {
        var v_strController = ReadRepositoryFile("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Log", "CLog_Record_Action_History_Controller.cs");

        Assert.Contains("FCommon_Sys_sp_sel_List_Log_Record_Action_History(long p_iRef_ID)", v_strController);
        Assert.Contains("\"FCommon_Sys_sp_sel_List_Log_Record_Action_History\", p_iRef_ID", v_strController);
        Assert.DoesNotContain("FCommon_Sys_sp_sel_List_Log_Record_Action_History_By_Scope", v_strController);
        Assert.DoesNotContain("FCommon_Sys_sp_sel_List_Log_Record_Action_History(p_iRef_ID)", v_strController);
    }

    [Fact]
    public void Company_and_all_five_master_info_components_load_legacy_history_with_shared_ui()
    {
        var v_arrFiles = new[]
        {
            "F2003_2_Chu_Hang_Info.razor",
            "F2009_2_Kho_Info.razor",
            "FWarehouse_Unit_Info.razor",
            "F2017_2_Loai_San_Pham_Info.razor",
            "F2018_2_San_Pham_Info.razor",
            "F2019_2_Nha_Cung_Cap_Info.razor"
        };

        foreach (var v_strFile in v_arrFiles)
        {
            var v_strInfo = ReadRepositoryFile(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", v_strFile);

            Assert.Contains("FCommon_Sys_sp_sel_List_Log_Record_Action_History(r_iAuto_ID)", v_strInfo);
            Assert.Contains("FCommon_Record_Action_History_List v_arrData=\"@m_arrRAH\"", v_strInfo);
            Assert.Contains("Get_Language_Data_Field(\"Thông tin\")", v_strInfo);
            Assert.Contains("Get_Language_Data_Field(\"Lịch sử xử lý\")", v_strInfo);
            Assert.Contains("Modal_Cancel()", v_strInfo);
        }
    }

    [Fact]
    public void Imports_write_one_history_record_only_after_the_row_transaction_commits()
    {
        var v_arrFiles = new[]
        {
            "F2009_1_Kho_List.razor",
            "FWarehouse_Unit_List.razor",
            "F2017_1_Loai_San_Pham_List.razor",
            "F2018_1_San_Pham_List.razor",
            "F2019_1_Nha_Cung_Cap_List.razor"
        };

        foreach (var v_strFile in v_arrFiles)
        {
            var v_strList = ReadRepositoryFile(
                "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", v_strFile);
            var v_iHistory = v_strList.IndexOf("CCommon_Controller.FCommon_Insert_Action_History(", StringComparison.Ordinal);
            var v_iCommit = v_strList.LastIndexOf("v_trans.Commit()", v_iHistory, StringComparison.Ordinal);

            Assert.True(v_iCommit >= 0, $"No commit found before the history write in {v_strFile}.");
            Assert.True(v_iHistory > v_iCommit, $"History must follow commit in {v_strFile}.");
            Assert.Equal(1, Regex.Matches(v_strList, "CCommon_Controller\\.FCommon_Insert_Action_History\\(").Count);
        }
    }

    [Fact]
    public void FBase_records_each_crud_action_once_and_reports_post_commit_history_failure_as_saved()
    {
        var v_strBase = ReadRepositoryFile("TKS_Thuc_Tap_V11_Web_Common", "Common", "FBase.razor");
        var v_strEvent = ReadRepositoryFile("TKS_Thuc_Tap_V11_Data_Access", "Entity", "Common", "CEvent_Call_Back.cs");
        var v_strWriter = ReadRepositoryFile("TKS_Thuc_Tap_V11_Data_Access", "Controller", "Common", "CCommon_Controller.cs");

        Assert.Equal(3, Regex.Matches(v_strBase, "CCommon_Controller\\.FCommon_Insert_Action_History\\(").Count);
        Assert.Contains("FCommon_Insert_Action_History(r_iAuto_ID, Get_Language_Data_Field(\"Hiệu chỉnh\")", v_strBase);
        Assert.Contains("FCommon_Insert_Action_History(r_iAuto_ID, Get_Language_Data_Field(\"Thêm mới\")", v_strBase);
        Assert.Contains("FCommon_Insert_Action_History(p_iAuto_ID, Get_Language_Data_Field(\"Xoá\")", v_strBase);
        Assert.Contains("r_strActive_User_Name, r_strActive_Function_Code", v_strBase);
        Assert.Contains("Ref_ID = p_iRef_ID", v_strWriter);
        Assert.Contains("Ma_Chuc_Nang = p_strChuc_Nang", v_strWriter);
        Assert.Contains("Created_By = p_strCreated_By", v_strWriter);
        Assert.Contains("FQ_425_RAH_sp_ins_Insert(p_Conn, p_Tran, v_objAH)", v_strWriter);
        Assert.Contains("p_objEvent.Message_Description", v_strBase);
        Assert.Contains("r_strError = p_objEvent.Message_Description;", v_strBase);
        Assert.Contains("dữ liệu đã được lưu nhưng không ghi được lịch sử", v_strBase, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("đã xóa dữ liệu nhưng không ghi được lịch sử", v_strBase, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public string Message_Description = \"\";", v_strEvent);
    }

    private static string ReadRepositoryFile(params string[] p_arrParts)
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            var v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
                return File.ReadAllText(v_strCandidate);
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrParts)}");
    }
}
