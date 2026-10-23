using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseDmCacheSyncTests
{
    public static IEnumerable<object[]> StoreContracts
    {
        get
        {
            yield return new object[] { "CCache_Kho", typeof(CDM_Kho), typeof(CDM_Kho_Controller), "Load_Cache_Kho", "F2009_sp_sel_List_Kho_For_Cache", "tbl_DM_Kho", new[] { "Auto_ID", "Ten_Kho", "Ghi_Chu" }, "Ten_Kho" };
            yield return new object[] { "CCache_Don_Vi_Tinh", typeof(CDM_Don_Vi_Tinh), typeof(CDM_Don_Vi_Tinh_Controller), "Load_Cache_Don_Vi_Tinh", "F2016_sp_sel_List_Don_Vi_Tinh_For_Cache", "tbl_DM_Don_Vi_Tinh", new[] { "Auto_ID", "Ten_Don_Vi_Tinh", "Ghi_Chu" }, "Ten_Don_Vi_Tinh" };
            yield return new object[] { "CCache_Loai_San_Pham", typeof(CDM_Loai_San_Pham), typeof(CDM_Loai_San_Pham_Controller), "Load_Cache_Loai_San_Pham", "F2017_sp_sel_List_Loai_San_Pham_For_Cache", "tbl_DM_Loai_San_Pham", new[] { "Auto_ID", "Ma_LSP", "Ten_LSP", "Ghi_Chu" }, "Ma_LSP" };
            yield return new object[] { "CCache_San_Pham", typeof(CDM_San_Pham), typeof(CDM_San_Pham_Controller), "Load_Cache_San_Pham", "F2018_sp_sel_List_San_Pham_For_Cache", "tbl_DM_San_Pham", new[] { "Auto_ID", "Ma_San_Pham", "Ten_San_Pham", "Loai_San_Pham_ID", "Don_Vi_Tinh_ID", "Ghi_Chu" }, "Ma_San_Pham" };
            yield return new object[] { "CCache_NCC", typeof(CDM_NCC), typeof(CDM_NCC_Controller), "Load_Cache_NCC", "F2019_sp_sel_List_NCC_For_Cache", "tbl_DM_NCC", new[] { "Auto_ID", "Ma_NCC", "Ten_NCC", "Ghi_Chu" }, "Ma_NCC" };
        }
    }

    public static IEnumerable<object[]> CrudContracts
    {
        get
        {
            yield return new object[] { "Kho", "F2009_sp_ins_Kho", "F2009_sp_upd_Kho", "F2009_sp_del_Kho", "CCache_Kho" };
            yield return new object[] { "Don_Vi_Tinh", "F2016_sp_ins_Don_Vi_Tinh", "F2016_sp_upd_Don_Vi_Tinh", "F2016_sp_del_Don_Vi_Tinh", "CCache_Don_Vi_Tinh" };
            yield return new object[] { "Loai_San_Pham", "F2017_sp_ins_Loai_San_Pham", "F2017_sp_upd_Loai_San_Pham", "F2017_sp_del_Loai_San_Pham", "CCache_Loai_San_Pham" };
            yield return new object[] { "San_Pham", "F2018_sp_ins_San_Pham", "F2018_sp_upd_San_Pham", "F2018_sp_del_San_Pham", "CCache_San_Pham" };
            yield return new object[] { "NCC", "F2019_sp_ins_NCC", "F2019_sp_upd_NCC", "F2019_sp_del_NCC", "CCache_NCC" };
        }
    }

    public static IEnumerable<object[]> ImportContracts
    {
        get
        {
            yield return new object[] { "F2018_1_San_Pham_List.razor", "Import_Excel_Entry", "CCache_San_Pham", "Load_Cache_San_Pham" };
            yield return new object[] { "FWarehouse_Unit_List.razor", "Import_Excel_Entry", "CCache_Don_Vi_Tinh", "Load_Cache_Don_Vi_Tinh" };
            yield return new object[] { "F2017_1_Loai_San_Pham_List.razor", "Import_Excel_Entry", "CCache_Loai_San_Pham", "Load_Cache_Loai_San_Pham" };
            yield return new object[] { "F2019_1_Nha_Cung_Cap_List.razor", "Import_Excel_Entry", "CCache_NCC", "Load_Cache_NCC" };
            yield return new object[] { "F2009_1_Kho_List.razor", "Import_Excel_Entry", "CCache_Kho", "Load_Cache_Kho" };
        }
    }

    [Theory]
    [MemberData(nameof(StoreContracts))]
    public void Dedicated_cache_store_maps_full_entity_and_has_matching_controller_method(
        string p_strCache_Name,
        Type p_objEntity_Type,
        Type p_objController_Type,
        string p_strLoad_Method,
        string p_strStore_Name,
        string p_strTable_Name,
        string[] p_arrColumns,
        string p_strOrder_Column)
    {
        string v_strProcedure_Source = ReadRepositoryFile("Database", "WarehouseModule.Procedures.sql");
        string v_strProcedure_Body = GetProcedureBody(v_strProcedure_Source, p_strStore_Name);
        string v_strController_Source = ReadRepositoryFile(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "DM", p_objController_Type.Name + ".cs");
        string v_strMethod_Body = GetMethodBody(v_strController_Source, p_strStore_Name + "(");
        string v_strCache_Source = ReadRepositoryFile(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Cache", p_strCache_Name + ".cs");
        string v_strLoad_Body = GetMethodBody(v_strCache_Source, p_strLoad_Method + "(");

        MethodInfo[] v_arrMatching_Methods = p_objController_Type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(v_objMethod => v_objMethod.Name == p_strStore_Name)
            .ToArray();
        Assert.Single(v_arrMatching_Methods);
        Assert.Equal(typeof(List<>).MakeGenericType(p_objEntity_Type), v_arrMatching_Methods[0].ReturnType);
        Assert.Contains("CSqlHelper.FillDataTable", v_strMethod_Body);
        Assert.Contains("\"" + p_strStore_Name + "\"", v_strMethod_Body);
        Assert.Contains("Map_Row_To_Entity<" + p_objEntity_Type.Name + ">", v_strMethod_Body);
        Assert.Contains("finally", v_strMethod_Body);
        Assert.Contains("v_dt.Dispose()", v_strMethod_Body);
        Assert.DoesNotContain("Task.FromResult", v_strMethod_Body);

        Assert.Contains("SELECT " + string.Join(", ", p_arrColumns), v_strProcedure_Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM dbo." + p_strTable_Name, v_strProcedure_Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY " + p_strOrder_Column, v_strProcedure_Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE", v_strProcedure_Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OFFSET", v_strProcedure_Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("." + p_strStore_Name + "()", v_strLoad_Body);
        Assert.DoesNotContain("." + p_strStore_Name.Replace("_For_Cache", "") + "()", v_strLoad_Body);
        Assert.Contains(p_strCache_Name + "." + p_strLoad_Method + "();", ReadRepositoryFile("TKS_Thuc_Tap_V11_Web", "Program.cs"));
    }

    [Theory]
    [MemberData(nameof(CrudContracts))]
    public void Crud_actions_mutate_cache_only_after_the_matching_store_succeeds(
        string p_strEntity_Name,
        string p_strInsert_Store,
        string p_strUpdate_Store,
        string p_strDelete_Store,
        string p_strCache_Name)
    {
        string v_strPage_Source = ReadRepositoryFile(
            "TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", "FWarehouse_1_Warehouse_List.razor");
        string v_strAdd_Method = GetRegion(
            v_strPage_Source,
            "private Task Add_" + p_strEntity_Name + "_Async",
            "private Task Update_" + p_strEntity_Name + "_Async");
        string v_strUpdate_Method = GetRegion(
            v_strPage_Source,
            "private Task Update_" + p_strEntity_Name + "_Async",
            p_strEntity_Name == "NCC" ? "private async Task Save_Master_Operation_Async" : "private Task Add_");
        string v_strDelete_Method = GetMethodBody(v_strPage_Source, "private async Task Delete_Master_Async(");

        AssertOperationPrecedesCacheMutation(v_strAdd_Method, p_strInsert_Store + "(", p_strCache_Name + ".Add_Data(p_objData)");
        AssertOperationPrecedesCacheMutation(v_strUpdate_Method, p_strUpdate_Store + "(", p_strCache_Name + ".Update_Data(p_objData)");
        AssertOperationPrecedesCacheMutation(v_strDelete_Method, p_strDelete_Store + "(", p_strCache_Name + ".Delete_Data(p_iAuto_ID)");

        string v_strSave_Method = GetMethodBody(v_strPage_Source, "private async Task Save_Master_Operation_Async(");
        Assert.True(v_strSave_Method.IndexOf("p_actSave();", StringComparison.Ordinal) <
            v_strSave_Method.IndexOf("catch (Exception", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ImportContracts))]
    public void Import_reloads_cache_only_after_commit_and_never_rolls_back_a_committed_batch(
        string p_strService_File,
        string p_strImport_Method,
        string p_strCache_Name,
        string p_strLoad_Method)
    {
        string v_strSource = ReadRepositoryFile("TKS_Thuc_Tap_V11_Web_Danh_Muc", "Pages", "Danh_Muc", "Components", p_strService_File);
        string v_strImport_Body = GetMethodBody(v_strSource, "protected override void " + p_strImport_Method + "(");
        int v_iCommit = v_strImport_Body.IndexOf("v_trans.Commit();", StringComparison.Ordinal);
        int v_iRefresh = v_strImport_Body.IndexOf(p_strCache_Name + ".Add_Data(v_objData)", StringComparison.Ordinal);
        int v_iTransaction_Catch = v_strImport_Body.IndexOf("catch", v_iCommit, StringComparison.Ordinal);
        int v_iCatch_Open = v_strImport_Body.IndexOf('{', v_iTransaction_Catch);
        int v_iCatch_Close = FindMatchingBrace(v_strImport_Body, v_iCatch_Open);

        Assert.DoesNotContain(p_strLoad_Method + "(", v_strImport_Body);
        Assert.True(v_iCommit >= 0, "Import transaction commit was not found.");
        Assert.True(v_iRefresh > v_iCatch_Close, "Cache refresh must follow the transaction catch/rollback path.");
        Assert.DoesNotContain("CCache_", v_strImport_Body[..v_iCommit]);
        Assert.DoesNotContain(".Add_Data(", v_strImport_Body[..v_iCommit]);
        Assert.DoesNotContain(".Update_Data(", v_strImport_Body[..v_iCommit]);
        Assert.DoesNotContain(".Delete_Data(", v_strImport_Body[..v_iCommit]);

        string v_strTransaction_Catch = v_strImport_Body[v_iTransaction_Catch..(v_iCatch_Close + 1)];
        Assert.Contains("v_trans.Rollback();", v_strTransaction_Catch);
        Assert.Contains("throw;", v_strTransaction_Catch);
        Assert.Contains("Đã lưu nhưng", v_strImport_Body[v_iRefresh..], StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertOperationPrecedesCacheMutation(string p_strSource, string p_strStore_Call, string p_strCache_Call)
    {
        int v_iStore_Call = p_strSource.IndexOf(p_strStore_Call, StringComparison.Ordinal);
        int v_iCache_Call = p_strSource.IndexOf(p_strCache_Call, StringComparison.Ordinal);
        Assert.True(v_iStore_Call >= 0, "Store call was not found: " + p_strStore_Call);
        Assert.True(v_iCache_Call > v_iStore_Call, "Cache mutation must follow Store success: " + p_strCache_Call);
    }

    private static string ReadRepositoryFile(params string[] p_arrParts)
    {
        for (DirectoryInfo? v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory != null; v_objDirectory = v_objDirectory.Parent)
        {
            string v_strPath = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strPath))
                return File.ReadAllText(v_strPath);
        }

        throw new FileNotFoundException("Repository source file was not found: " + Path.Combine(p_arrParts));
    }

    private static string GetProcedureBody(string p_strSource, string p_strProcedure_Name)
    {
        string v_strDeclaration = "CREATE OR ALTER PROCEDURE dbo." + p_strProcedure_Name;
        int v_iStart = p_strSource.IndexOf(v_strDeclaration, StringComparison.OrdinalIgnoreCase);
        Assert.True(v_iStart >= 0, "Procedure definition was not found: " + p_strProcedure_Name);
        int v_iEnd = p_strSource.IndexOf("\nGO", v_iStart, StringComparison.OrdinalIgnoreCase);
        Assert.True(v_iEnd > v_iStart, "Procedure terminator was not found: " + p_strProcedure_Name);
        return p_strSource.Substring(v_iStart, v_iEnd - v_iStart);
    }

    private static string GetMethodBody(string p_strSource, string p_strSignature)
    {
        int v_iSignature = p_strSource.IndexOf(p_strSignature, StringComparison.Ordinal);
        Assert.True(v_iSignature >= 0, "Method was not found: " + p_strSignature);
        int v_iOpen = p_strSource.IndexOf('{', v_iSignature);
        Assert.True(v_iOpen >= 0, "Method body was not found: " + p_strSignature);
        return p_strSource[v_iSignature..(FindMatchingBrace(p_strSource, v_iOpen) + 1)];
    }

    private static string GetRegion(string p_strSource, string p_strStart, string p_strEnd)
    {
        int v_iStart = p_strSource.IndexOf(p_strStart, StringComparison.Ordinal);
        Assert.True(v_iStart >= 0, "Source region start was not found: " + p_strStart);
        int v_iEnd = p_strSource.IndexOf(p_strEnd, v_iStart + p_strStart.Length, StringComparison.Ordinal);
        Assert.True(v_iEnd > v_iStart, "Source region end was not found: " + p_strEnd);
        return p_strSource[v_iStart..v_iEnd];
    }

    private static int FindMatchingBrace(string p_strSource, int p_iOpen)
    {
        int v_iDepth = 0;
        for (int v_i = p_iOpen; v_i < p_strSource.Length; v_i++)
        {
            if (p_strSource[v_i] == '{')
                v_iDepth++;
            else if (p_strSource[v_i] == '}' && --v_iDepth == 0)
                return v_i;
        }

        Assert.Fail("Unmatched opening brace.");
        return -1;
    }
}
