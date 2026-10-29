using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using TKS_Thuc_Tap_V11_Data_Access.Entity.DM;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class WarehouseDmCacheContractTests
{
    private const string m_strCacheNamespace = "TKS_Thuc_Tap_V11_Data_Access.Controller.Cache.";
    private static long m_lNextAuto_ID = 900_000_000_000;

    public static IEnumerable<object[]> CacheContracts
    {
        get
        {
            yield return new object[] { "CCache_Kho", typeof(CDM_Kho), "CDM_Kho_Controller", "Load_Cache_Kho", "F2009_sp_sel_List_Kho_For_Cache", "view_DM_Kho", "Ten_Kho", new[] { "Ten_Kho" }, new[] { "UX_tbl_DM_Kho_Ten" } };
            yield return new object[] { "CCache_Don_Vi_Tinh", typeof(CDM_Don_Vi_Tinh), "CDM_Don_Vi_Tinh_Controller", "Load_Cache_Don_Vi_Tinh", "F2016_sp_sel_List_Don_Vi_Tinh_For_Cache", "view_DM_Don_Vi_Tinh", "Ten_Don_Vi_Tinh", new[] { "Ten_Don_Vi_Tinh" }, new[] { "UX_tbl_DM_Don_Vi_Tinh_Ten" } };
            yield return new object[] { "CCache_Loai_San_Pham", typeof(CDM_Loai_San_Pham), "CDM_Loai_San_Pham_Controller", "Load_Cache_Loai_San_Pham", "F2017_sp_sel_List_Loai_San_Pham_For_Cache", "view_DM_Loai_San_Pham", "Ma_LSP", new[] { "Ma_LSP", "Ten_LSP" }, new[] { "UX_tbl_DM_Loai_San_Pham_Ma", "UX_tbl_DM_Loai_San_Pham_Ten" } };
            yield return new object[] { "CCache_San_Pham", typeof(CDM_San_Pham), "CDM_San_Pham_Controller", "Load_Cache_San_Pham", "F2018_sp_sel_List_San_Pham_For_Cache", "view_DM_San_Pham", "Ma_San_Pham", new[] { "Ma_San_Pham" }, new[] { "UX_tbl_DM_San_Pham_Ma" } };
            yield return new object[] { "CCache_NCC", typeof(CDM_NCC), "CDM_NCC_Controller", "Load_Cache_NCC", "F2019_sp_sel_List_NCC_For_Cache", "view_DM_NCC", "Ma_NCC", new[] { "Ma_NCC", "Ten_NCC" }, new[] { "UX_tbl_DM_NCC_Ma", "UX_tbl_DM_NCC_Ten" } };
        }
    }

    public static IEnumerable<object[]> CacheLifecycleContracts
    {
        get
        {
            yield return new object[] { "CCache_Kho", typeof(CDM_Kho), "Ten_Kho", new[] { "Ten_Kho" } };
            yield return new object[] { "CCache_Don_Vi_Tinh", typeof(CDM_Don_Vi_Tinh), "Ten_Don_Vi_Tinh", new[] { "Ten_Don_Vi_Tinh" } };
            yield return new object[] { "CCache_Loai_San_Pham", typeof(CDM_Loai_San_Pham), "Ma_LSP", new[] { "Ma_LSP", "Ten_LSP" } };
            yield return new object[] { "CCache_San_Pham", typeof(CDM_San_Pham), "Ma_San_Pham", new[] { "Ma_San_Pham" } };
            yield return new object[] { "CCache_NCC", typeof(CDM_NCC), "Ma_NCC", new[] { "Ma_NCC", "Ten_NCC" } };
        }
    }

    [Theory]
    [MemberData(nameof(CacheContracts))]
    public void Cache_class_uses_its_entity_and_complete_ordered_list_store(
        string p_strCache_Name,
        Type p_objEntity_Type,
        string p_strController_Name,
        string p_strLoad_Method,
        string p_strStore_Name,
        string p_strTable_Name,
        string p_strSort_Field,
        string[] p_arrBusiness_Keys,
        string[] p_arrUnique_Index_Names)
    {
        Type v_objCache_Type = GetCacheType(p_strCache_Name);
        Type v_objExpected_List_Type = typeof(List<>).MakeGenericType(p_objEntity_Type);
        Type v_objExpected_ID_Dictionary_Type = typeof(Dictionary<,>).MakeGenericType(typeof(long), p_objEntity_Type);
        Type v_objExpected_Key_Dictionary_Type = typeof(Dictionary<,>).MakeGenericType(typeof(string), p_objEntity_Type);

        Assert.Equal(p_objEntity_Type, v_objCache_Type.GetMethod("Get_Data_By_ID")?.ReturnType);
        Assert.Equal(v_objExpected_List_Type, v_objCache_Type.GetMethod("List_Data")?.ReturnType);
        Assert.Equal(typeof(void), v_objCache_Type.GetMethod(p_strLoad_Method)?.ReturnType);
        Assert.Equal(v_objExpected_List_Type, v_objCache_Type.GetField("g_arrData", BindingFlags.NonPublic | BindingFlags.Static)?.FieldType);
        Assert.Equal(v_objExpected_ID_Dictionary_Type, v_objCache_Type.GetField("g_dicData_ID", BindingFlags.NonPublic | BindingFlags.Static)?.FieldType);

        foreach (string v_strKey in p_arrBusiness_Keys)
        {
            Assert.Equal(v_objExpected_Key_Dictionary_Type, v_objCache_Type.GetField("g_dicData_" + v_strKey, BindingFlags.NonPublic | BindingFlags.Static)?.FieldType);
            Assert.Equal(p_objEntity_Type, v_objCache_Type.GetMethod("Get_Data_By_" + v_strKey)?.ReturnType);
        }

        string v_strCache_Source = File.ReadAllText(FindRepositoryPath(
            "TKS_Thuc_Tap_V11_Data_Access", "Controller", "Cache", p_strCache_Name + ".cs"));
        string v_strStartup_Source = File.ReadAllText(FindRepositoryPath("TKS_Thuc_Tap_V11_Web", "Program.cs"));
        string v_strProcedure_Source = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Procedures.sql"));
        string v_strSchema_Source = File.ReadAllText(FindRepositoryPath("Database", "WarehouseModule.Schema.sql"));
        string v_strProcedure_Body = GetProcedureBody(v_strProcedure_Source, p_strStore_Name);

        Assert.Contains("new " + p_strController_Name + "()", v_strCache_Source);
        Assert.Contains("." + p_strStore_Name + "()", v_strCache_Source);
        Assert.Contains(p_strCache_Name + "." + p_strLoad_Method + "();", v_strStartup_Source);
        foreach (string v_strIndex_Name in p_arrUnique_Index_Names)
            Assert.Contains("CREATE UNIQUE INDEX " + v_strIndex_Name, v_strSchema_Source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CWarehouseMaster", v_strCache_Source);
        Assert.DoesNotContain("CWarehouseLookup", v_strCache_Source);
        Assert.Contains("FROM dbo." + p_strTable_Name, v_strProcedure_Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY " + p_strSort_Field, v_strProcedure_Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE", v_strProcedure_Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(CacheLifecycleContracts))]
    public void Cache_add_update_delete_and_list_keep_ID_and_business_indexes_consistent(
        string p_strCache_Name,
        Type p_objEntity_Type,
        string p_strSort_Field,
        string[] p_arrBusiness_Keys)
    {
        Type v_objCache_Type = GetCacheType(p_strCache_Name);
        MethodInfo v_objAdd_Method = GetMethod(v_objCache_Type, "Add_Data", p_objEntity_Type);
        MethodInfo v_objUpdate_Method = GetMethod(v_objCache_Type, "Update_Data", p_objEntity_Type);
        MethodInfo v_objDelete_Method = GetMethod(v_objCache_Type, "Delete_Data", typeof(long));
        MethodInfo v_objGet_ID_Method = GetMethod(v_objCache_Type, "Get_Data_By_ID", typeof(long));
        MethodInfo v_objList_Method = GetMethod(v_objCache_Type, "List_Data");
        MethodInfo[] v_arrGet_Key_Methods = p_arrBusiness_Keys
            .Select(v_strKey => GetMethod(v_objCache_Type, "Get_Data_By_" + v_strKey, typeof(string)))
            .ToArray();

        long v_lFirst_ID = Interlocked.Increment(ref m_lNextAuto_ID);
        long v_lSecond_ID = Interlocked.Increment(ref m_lNextAuto_ID);
        long v_lNull_Key_ID = Interlocked.Increment(ref m_lNextAuto_ID);
        long v_lEmpty_Key_ID = Interlocked.Increment(ref m_lNextAuto_ID);
        string v_strPrefix = "dm_cache_" + v_lFirst_ID;

        try
        {
            object v_objFirst = CreateEntity(p_objEntity_Type, v_lFirst_ID, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_z", "first");
            object v_objSecond = CreateEntity(p_objEntity_Type, v_lSecond_ID, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_a", "second");
            object v_objDuplicate_ID = CreateEntity(p_objEntity_Type, v_lFirst_ID, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_duplicate", "duplicate");
            object v_objZero_ID = CreateEntity(p_objEntity_Type, 0, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_zero", "zero");

            v_objAdd_Method.Invoke(null, new[] { v_objFirst });
            v_objAdd_Method.Invoke(null, new[] { v_objDuplicate_ID });
            v_objAdd_Method.Invoke(null, new[] { v_objZero_ID });
            v_objAdd_Method.Invoke(null, new[] { v_objSecond });

            Assert.Same(v_objFirst, v_objGet_ID_Method.Invoke(null, new object[] { v_lFirst_ID }));
            Assert.Null(v_objGet_ID_Method.Invoke(null, new object[] { 0L }));
            Assert.Null(v_arrGet_Key_Methods[0].Invoke(null, new object[] { v_strPrefix + "_zero" }));
            AssertBusinessKeysResolve(v_arrGet_Key_Methods, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_z", v_objFirst, "first");
            Assert.Equal(new[] { v_lSecond_ID, v_lFirst_ID }, GetListedIDs(v_objList_Method, p_objEntity_Type, v_lFirst_ID, v_lSecond_ID));

            object v_objUpdated = CreateEntity(p_objEntity_Type, v_lFirst_ID, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_0", "updated");
            v_objUpdate_Method.Invoke(null, new[] { v_objUpdated });

            Assert.Same(v_objUpdated, v_objGet_ID_Method.Invoke(null, new object[] { v_lFirst_ID }));
            AssertBusinessKeysDoNotResolve(v_arrGet_Key_Methods, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_z", "first");
            AssertBusinessKeysResolve(v_arrGet_Key_Methods, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_0", v_objUpdated, "updated");
            Assert.Equal(new[] { v_lFirst_ID, v_lSecond_ID }, GetListedIDs(v_objList_Method, p_objEntity_Type, v_lFirst_ID, v_lSecond_ID));

            object v_objNull_Key = CreateEntity(p_objEntity_Type, v_lNull_Key_ID, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_null", "null");
            object v_objEmpty_Key = CreateEntity(p_objEntity_Type, v_lEmpty_Key_ID, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_empty", "empty");
            foreach (string v_strKey in p_arrBusiness_Keys)
            {
                p_objEntity_Type.GetProperty(v_strKey)!.SetValue(v_objNull_Key, null);
                p_objEntity_Type.GetProperty(v_strKey)!.SetValue(v_objEmpty_Key, "");
            }
            v_objAdd_Method.Invoke(null, new[] { v_objNull_Key });
            v_objAdd_Method.Invoke(null, new[] { v_objEmpty_Key });
            foreach (MethodInfo v_objGet_Method in v_arrGet_Key_Methods)
            {
                Assert.Null(v_objGet_Method.Invoke(null, new object?[] { null }));
                Assert.Null(v_objGet_Method.Invoke(null, new object[] { "" }));
            }

            v_objDelete_Method.Invoke(null, new object[] { v_lFirst_ID });
            Assert.Null(v_objGet_ID_Method.Invoke(null, new object[] { v_lFirst_ID }));
            AssertBusinessKeysDoNotResolve(v_arrGet_Key_Methods, p_arrBusiness_Keys, p_strSort_Field, v_strPrefix + "_0", "updated");
            Assert.DoesNotContain(v_lFirst_ID, GetListedIDs(v_objList_Method, p_objEntity_Type, v_lFirst_ID));
        }
        finally
        {
            foreach (long v_lID in new[] { v_lFirst_ID, v_lSecond_ID, v_lNull_Key_ID, v_lEmpty_Key_ID })
                v_objDelete_Method.Invoke(null, new object[] { v_lID });
        }
    }

    private static Type GetCacheType(string p_strCache_Name)
    {
        Type? v_objCache_Type = typeof(CDM_Kho).Assembly.GetType(m_strCacheNamespace + p_strCache_Name);
        Assert.NotNull(v_objCache_Type);
        return v_objCache_Type!;
    }

    private static MethodInfo GetMethod(Type p_objType, string p_strName, params Type[] p_arrParameter_Types)
    {
        MethodInfo? v_objMethod = p_objType.GetMethod(
            p_strName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: p_arrParameter_Types,
            modifiers: null);
        Assert.NotNull(v_objMethod);
        return v_objMethod!;
    }

    private static object CreateEntity(Type p_objEntity_Type, long p_lAuto_ID, string[] p_arrBusiness_Keys, string p_strSort_Field, string p_strSort_Value, string p_strTag)
    {
        object v_objData = Activator.CreateInstance(p_objEntity_Type)!;
        p_objEntity_Type.GetProperty("Auto_ID")!.SetValue(v_objData, p_lAuto_ID);
        foreach (string v_strKey in p_arrBusiness_Keys)
        {
            string v_strValue = v_strKey == p_strSort_Field ? p_strSort_Value : p_strTag + "_" + v_strKey;
            p_objEntity_Type.GetProperty(v_strKey)!.SetValue(v_objData, v_strValue);
        }
        return v_objData;
    }

    private static void AssertBusinessKeysResolve(MethodInfo[] p_arrGet_Methods, string[] p_arrKeys, string p_strSort_Field, string p_strSort_Value, object p_objExpected, string p_strTag)
    {
        for (int v_i = 0; v_i < p_arrGet_Methods.Length; v_i++)
        {
            string v_strExpected_Key = p_arrKeys[v_i] == p_strSort_Field
                ? p_strSort_Value
                : p_strTag + "_" + p_arrKeys[v_i];
            Assert.Same(p_objExpected, p_arrGet_Methods[v_i].Invoke(null, new object[] { v_strExpected_Key.ToUpperInvariant() }));
        }
    }

    private static void AssertBusinessKeysDoNotResolve(MethodInfo[] p_arrGet_Methods, string[] p_arrKeys, string p_strSort_Field, string p_strSort_Value, string p_strTag)
    {
        for (int v_i = 0; v_i < p_arrGet_Methods.Length; v_i++)
        {
            string v_strRemoved_Key = p_arrKeys[v_i] == p_strSort_Field
                ? p_strSort_Value
                : p_strTag + "_" + p_arrKeys[v_i];
            Assert.Null(p_arrGet_Methods[v_i].Invoke(null, new object[] { v_strRemoved_Key }));
        }
    }

    private static long[] GetListedIDs(MethodInfo p_objList_Method, Type p_objEntity_Type, params long[] p_arrIDs)
    {
        var v_objIDs = new HashSet<long>(p_arrIDs);
        var v_objList = (IEnumerable)p_objList_Method.Invoke(null, null)!;
        PropertyInfo v_objID_Property = p_objEntity_Type.GetProperty("Auto_ID")!;
        return v_objList.Cast<object>()
            .Where(v_objData => v_objIDs.Contains((long)v_objID_Property.GetValue(v_objData)!))
            .Select(v_objData => (long)v_objID_Property.GetValue(v_objData)!)
            .ToArray();
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

    private static string FindRepositoryPath(params string[] p_arrParts)
    {
        for (var v_objDirectory = new DirectoryInfo(AppContext.BaseDirectory); v_objDirectory is not null; v_objDirectory = v_objDirectory.Parent)
        {
            string v_strCandidate = Path.Combine(new[] { v_objDirectory.FullName }.Concat(p_arrParts).ToArray());
            if (File.Exists(v_strCandidate))
                return v_strCandidate;
        }
        throw new FileNotFoundException("Repository file was not found: " + Path.Combine(p_arrParts));
    }
}
