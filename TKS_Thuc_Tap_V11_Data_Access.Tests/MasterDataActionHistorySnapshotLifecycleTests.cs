using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class MasterDataActionHistorySnapshotLifecycleTests
{
    [Fact]
    public void Save_Data_checks_source_before_update_and_destination_after_update()
    {
        string v_strBase = Read_Repository_File(
            "TKS_Thuc_Tap_V11_Web_Common",
            "Common",
            "FBase.razor");
        int v_iSave_Data = v_strBase.IndexOf(
            "public async Task Save_Data()",
            StringComparison.Ordinal);

        Assert.True(v_iSave_Data >= 0, "FBase.Save_Data was not found.");

        int v_iSource_Check = v_strBase.IndexOf("if (r_objSource == null)", v_iSave_Data, StringComparison.Ordinal);
        int v_iUpdate = v_strBase.IndexOf("Update_Data();", v_iSave_Data, StringComparison.Ordinal);
        int v_iDestination_Check = v_strBase.IndexOf("if (r_objDes == null)", v_iSave_Data, StringComparison.Ordinal);
        int v_iCompare = v_strBase.IndexOf(
            "CUtility.Compare_Entity(r_objSource, r_objDes)",
            v_iSave_Data,
            StringComparison.Ordinal);

        Assert.True(v_iSource_Check >= 0 && v_iSource_Check < v_iUpdate,
            "The original snapshot must be validated before Update_Data can persist changes.");
        Assert.True(v_iUpdate >= 0 && v_iUpdate < v_iDestination_Check,
            "Update_Data must produce the destination snapshot before FBase validates it.");
        Assert.True(v_iDestination_Check >= 0 && v_iDestination_Check < v_iCompare,
            "Both snapshots must be validated before Compare_Entity and action-history logging.");
        Assert.Contains("Chưa get object phục vụ cho việc log CRUD", v_strBase, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("F2003_3_Chu_Hang_Edit.razor", "FQ_104_CH_sp_upd_Update")]
    [InlineData("F2009_3_Kho_Edit.razor", "F2009_sp_upd_Kho")]
    [InlineData("FWarehouse_Unit_Edit.razor", "F2016_sp_upd_Don_Vi_Tinh")]
    [InlineData("F2017_3_Loai_San_Pham_Edit.razor", "F2017_sp_upd_Loai_San_Pham")]
    [InlineData("F2018_3_San_Pham_Edit.razor", "F2018_sp_upd_San_Pham")]
    [InlineData("F2019_3_Nha_Cung_Cap_Edit.razor", "F2019_sp_upd_NCC")]
    public void Master_data_editors_capture_source_on_load_and_destination_after_update(
        string p_strEdit_File,
        string p_strUpdate_Procedure)
    {
        string v_strEdit = Read_Repository_File(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc",
            "Pages",
            "Danh_Muc",
            "Components",
            p_strEdit_File);

        int v_iLoad_Data = v_strEdit.IndexOf("protected override void Load_Data()", StringComparison.Ordinal);
        int v_iUpdate_Data = v_strEdit.IndexOf("protected override void Update_Data()", StringComparison.Ordinal);
        int v_iSource_Snapshot = v_strEdit.IndexOf("r_objSource = v_objClone", v_iLoad_Data, StringComparison.Ordinal);
        int v_iUpdate_Procedure = v_strEdit.IndexOf(p_strUpdate_Procedure, v_iUpdate_Data, StringComparison.Ordinal);
        int v_iDestination_Snapshot = v_strEdit.IndexOf("r_objDes = v_objClone", v_iUpdate_Data, StringComparison.Ordinal);

        Assert.True(v_iLoad_Data >= 0 && v_iSource_Snapshot > v_iLoad_Data && v_iSource_Snapshot < v_iUpdate_Data,
            $"{p_strEdit_File} must capture the original entity in Load_Data.");
        Assert.True(v_iUpdate_Data >= 0 && v_iUpdate_Procedure > v_iUpdate_Data && v_iDestination_Snapshot > v_iUpdate_Procedure,
            $"{p_strEdit_File} must capture the destination entity after its update procedure succeeds.");
    }

    private static string Read_Repository_File(params string[] p_arrPath)
    {
        DirectoryInfo? v_objDirectory = new(AppContext.BaseDirectory);
        while (v_objDirectory is not null)
        {
            string v_strPath = Path.Combine([v_objDirectory.FullName, .. p_arrPath]);
            if (File.Exists(v_strPath))
                return File.ReadAllText(v_strPath);

            v_objDirectory = v_objDirectory.Parent;
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(p_arrPath)}");
    }
}
