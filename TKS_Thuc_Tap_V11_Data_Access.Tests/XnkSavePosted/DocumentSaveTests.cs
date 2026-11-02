using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
using TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using OfficeOpenXml;
using TKS_Thuc_Tap_V11_Web_Danh_Muc.Pages.Danh_Muc.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TKS_Thuc_Tap_V11_Data_Access.Tests.XnkSavePosted;

public sealed class DocumentSaveTests
{
    private const string Database = "TKS_Thuc_Tap_V11_XNK_Test_20261102";
    private const string Server = @"DESKTOP-NHQ7QPL\MSSQLSERVER19";

    private static async Task<SqlConnection> Open_Async()
    {
        var v_strConnection = Environment.GetEnvironmentVariable("TKS_INTEGRATION_CONNECTION_STRING")
            ?? throw new InvalidOperationException("An explicitly configured test database is required.");
        var v_objBuilder = new SqlConnectionStringBuilder(v_strConnection);
        if (v_objBuilder.InitialCatalog != Database || v_objBuilder.DataSource != Server)
            throw new InvalidOperationException("Only the approved disposable test database is allowed.");
        var v_conn = new SqlConnection(v_strConnection);
        await v_conn.OpenAsync();
        Assert.Equal(Database, await Scalar_Async<string>(v_conn, "SELECT DB_NAME()"));
        return v_conn;
    }

    private static async Task<T> Scalar_Async<T>(SqlConnection p_conn, string p_strSql)
    {
        using var v_cmd = new SqlCommand(p_strSql, p_conn);
        return (T)Convert.ChangeType((await v_cmd.ExecuteScalarAsync())!, typeof(T));
    }

    private sealed record CLine(long Auto_ID, long San_Pham_ID, decimal So_Luong, decimal Don_Gia);
    private sealed record CScope(long Warehouse, long Supplier, long[] Products, string Login, string Tag);

    private static async Task<CScope> Create_Scope_Async(SqlConnection p_conn)
    {
        string v_strTag = "XNK-" + Guid.NewGuid().ToString("N")[..16];
        using var v_cmd = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.tbl_Sys_Chuc_Nang WHERE Ma_Chuc_Nang=N'3001')
                INSERT dbo.tbl_Sys_Chuc_Nang(Auto_ID,Ma_Chuc_Nang,Ten_Chuc_Nang,deleted) VALUES(ABS(CONVERT(BIGINT,CHECKSUM(NEWID()))),N'3001',N'Nhập kho',0);
            IF NOT EXISTS(SELECT 1 FROM dbo.tbl_Sys_Chuc_Nang WHERE Ma_Chuc_Nang=N'3002')
                INSERT dbo.tbl_Sys_Chuc_Nang(Auto_ID,Ma_Chuc_Nang,Ten_Chuc_Nang,deleted) VALUES(ABS(CONVERT(BIGINT,CHECKSUM(NEWID()))),N'3002',N'Xuất kho',0);
            DECLARE @User BIGINT = ABS(CONVERT(BIGINT,CHECKSUM(NEWID()))), @Group BIGINT = ABS(CONVERT(BIGINT,CHECKSUM(NEWID())));
            INSERT dbo.tbl_Sys_Thanh_Vien(Auto_ID,Ma_Dang_Nhap,Ho_Ten,Trang_Thai_ID,deleted) VALUES(@User,@Tag,@Tag,1,0);
            INSERT dbo.tbl_Sys_Nhom_Thanh_Vien(Auto_ID,Ten_Nhom_Thanh_Vien,deleted) VALUES(@Group,@Tag,0);
            INSERT dbo.tbl_Sys_Nhom_Thanh_Vien_User(Auto_ID,Nhom_Thanh_Vien_ID,Ma_Dang_Nhap,deleted) VALUES(@Group,@Group,@Tag,0);
            INSERT dbo.tbl_Sys_Phan_Quyen_Chuc_Nang(Auto_ID,Nhom_Thanh_Vien_ID,Chuc_Nang_ID,Is_Have_View_Permission,Is_Have_Add_Permission,Is_Have_Edit_Permission,deleted)
            SELECT @Group+ROW_NUMBER() OVER(ORDER BY Auto_ID),@Group,Auto_ID,1,1,1,0 FROM dbo.tbl_Sys_Chuc_Nang WHERE Ma_Chuc_Nang IN(N'3001',N'3002');
            INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh) VALUES(@Tag); DECLARE @Unit BIGINT=SCOPE_IDENTITY();
            INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP,Ten_LSP) VALUES(@Tag,@Tag); DECLARE @Category BIGINT=SCOPE_IDENTITY();
            INSERT dbo.tbl_DM_NCC(Ma_NCC,Ten_NCC) VALUES(@Tag,@Tag); DECLARE @Supplier BIGINT=SCOPE_IDENTITY();
            INSERT dbo.tbl_DM_Kho(Ten_Kho) VALUES(@Tag); DECLARE @Warehouse BIGINT=SCOPE_IDENTITY();
            INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap,Kho_ID) VALUES(@Tag,@Warehouse);
            INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham,Ten_San_Pham,Loai_San_Pham_ID,Don_Vi_Tinh_ID) VALUES(@Tag+N'-1',@Tag,@Category,@Unit); DECLARE @P1 BIGINT=SCOPE_IDENTITY();
            INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham,Ten_San_Pham,Loai_San_Pham_ID,Don_Vi_Tinh_ID) VALUES(@Tag+N'-2',@Tag,@Category,@Unit); DECLARE @P2 BIGINT=SCOPE_IDENTITY();
            INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham,Ten_San_Pham,Loai_San_Pham_ID,Don_Vi_Tinh_ID) VALUES(@Tag+N'-3',@Tag,@Category,@Unit);
            SELECT @Warehouse,@Supplier,@P1,@P2,CONVERT(BIGINT,SCOPE_IDENTITY());
            """, p_conn);
        v_cmd.Parameters.Add("@Tag", SqlDbType.NVarChar, 100).Value = v_strTag;
        using var v_reader = await v_cmd.ExecuteReaderAsync();
        Assert.True(await v_reader.ReadAsync());
        return new(v_reader.GetInt64(0), v_reader.GetInt64(1), [v_reader.GetInt64(2), v_reader.GetInt64(3), v_reader.GetInt64(4)], v_strTag, v_strTag);
    }

    private static async Task<long> Save_Async(SqlConnection p_conn, CScope p_scope, CLine[] p_arrLines,
        bool p_bReceipt = true, long p_iId = 0, long p_iVersion = 0, Guid? p_request = null,
        string? p_strLogin = null, string? p_strNumber = null, DateTime? p_date = null, string? p_strExpectedState = null)
    {
        using var v_cmd = new SqlCommand("dbo.sp_XNK_Document_Save_Posted", p_conn) { CommandType = CommandType.StoredProcedure };
        v_cmd.Parameters.AddWithValue("@Is_Receipt", p_bReceipt);
        v_cmd.Parameters.AddWithValue("@Document_ID", p_iId);
        v_cmd.Parameters.AddWithValue("@Expected_Version", p_iVersion);
        v_cmd.Parameters.AddWithValue("@Request_ID", p_request ?? Guid.NewGuid());
        v_cmd.Parameters.Add("@So_Phieu", SqlDbType.NVarChar, 100).Value = p_strNumber ?? p_scope.Tag + (p_bReceipt ? "-N" : "-X");
        v_cmd.Parameters.AddWithValue("@Kho_ID", p_scope.Warehouse);
        v_cmd.Parameters.AddWithValue("@NCC_ID", p_scope.Supplier);
        v_cmd.Parameters.Add("@Ngay_Chung_Tu", SqlDbType.Date).Value = p_date ?? new DateTime(2026, 1, 10);
        v_cmd.Parameters.Add("@Ghi_Chu", SqlDbType.NVarChar, 1000).Value = "test";
        v_cmd.Parameters.Add("@Details", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(p_arrLines);
        v_cmd.Parameters.Add("@Ma_Dang_Nhap", SqlDbType.NVarChar, 100).Value = p_strLogin ?? p_scope.Login;
        if (p_strExpectedState != null)
            v_cmd.Parameters.Add("@Expected_State",SqlDbType.VarChar,64).Value=p_strExpectedState;
        else if (p_iId != 0)
        {
            using var v_read = new SqlCommand("dbo.sp_XNK_Document_Edit_Get", p_conn) { CommandType = CommandType.StoredProcedure };
            v_read.Parameters.AddWithValue("@Is_Receipt",p_bReceipt);
            v_read.Parameters.AddWithValue("@Document_ID",p_iId);
            v_read.Parameters.AddWithValue("@Ma_Dang_Nhap",p_scope.Login);
            using var v_reader = await v_read.ExecuteReaderAsync();
            Assert.True(await v_reader.ReadAsync());
            v_cmd.Parameters.Add("@Expected_State",SqlDbType.VarChar,64).Value=v_reader.GetString(1);
        }
        return Convert.ToInt64(await v_cmd.ExecuteScalarAsync());
    }

    private static Task<decimal> Balance_Async(SqlConnection p_conn, CScope p_scope, int p_iProduct = 0) =>
        Scalar_Async<decimal>(p_conn, $"SELECT COALESCE((SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID={p_scope.Warehouse} AND San_Pham_ID={p_scope.Products[p_iProduct]}),0)");

    private static Task<long> Detail_ID_Async(SqlConnection p_conn, long p_iDocument, bool p_bReceipt = true) =>
        Scalar_Async<long>(p_conn, p_bReceipt
            ? $"SELECT MIN(Auto_ID) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID={p_iDocument}"
            : $"SELECT MIN(Auto_ID) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID={p_iDocument}");

    [Fact]
    public async Task Save_three_lines_posts_once_and_replay_does_not_duplicate_inventory()
    {
        using var v_conn = await Open_Async();
        var v_scope = await Create_Scope_Async(v_conn);
        var v_lines = v_scope.Products.Select(p_i => new CLine(0,p_i,10,2)).ToArray();
        var v_request = Guid.NewGuid();
        long v_iId = await Save_Async(v_conn,v_scope,v_lines,p_request:v_request);
        Assert.Equal(v_iId,await Save_Async(v_conn,v_scope,v_lines,p_request:v_request));
        Assert.Equal(10,await Balance_Async(v_conn,v_scope));
        Assert.Equal(3,await Scalar_Async<int>(v_conn,$"SELECT COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID={v_iId}"));
        Assert.True(await Scalar_Async<bool>(v_conn,$"SELECT Is_Posted FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID={v_iId}"));
        Assert.Equal(0,await Scalar_Async<int>(v_conn,"SELECT @@TRANCOUNT"));
    }

    [Fact]
    public async Task Invalid_third_line_rolls_back_header_details_and_current()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        var v_lines=v_scope.Products.Select(p_i=>new CLine(0,p_i,10,2)).ToArray();
        v_lines[2]=v_lines[2] with { So_Luong=-1 };
        await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,v_lines));
        Assert.Equal(0,await Scalar_Async<int>(v_conn,$"SELECT COUNT(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE Kho_ID={v_scope.Warehouse}"));
        Assert.Equal(0,await Balance_Async(v_conn,v_scope));
    }

    [Fact]
    public async Task Receipt_corrections_apply_delta_price_only_is_neutral_and_stale_edit_is_rejected()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        long v_id=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        long v_detail=await Detail_ID_Async(v_conn,v_id);
        await Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],15,2)],p_iId:v_id,p_iVersion:1);
        Assert.Equal(15,await Balance_Async(v_conn,v_scope));
        await Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],8,2)],p_iId:v_id,p_iVersion:2);
        Assert.Equal(8,await Balance_Async(v_conn,v_scope));
        await Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],8,5)],p_iId:v_id,p_iVersion:3);
        Assert.Equal(8,await Balance_Async(v_conn,v_scope));
        var v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],20,2)],p_iId:v_id,p_iVersion:3));
        Assert.Equal(51502,v_error.Number);
    }

    [Fact]
    public async Task Added_and_removed_details_reverse_old_effect_and_product_cannot_be_forged()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        long v_id=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        long v_detail=await Detail_ID_Async(v_conn,v_id);
        var v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[1],10,2)],p_iId:v_id,p_iVersion:1));
        Assert.Equal(51503,v_error.Number);
        await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[1],4,2)],p_iId:v_id,p_iVersion:1);
        Assert.Equal(0,await Balance_Async(v_conn,v_scope));
        Assert.Equal(4,await Balance_Async(v_conn,v_scope,1));
    }

    [Fact]
    public async Task Issue_quantity_sign_available_stock_and_historical_receipt_reduction_are_validated()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        long v_receipt=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        long v_issue=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],6,2)],false);
        long v_detail=await Detail_ID_Async(v_conn,v_issue,false);
        await Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],8,2)],false,v_issue,1);
        Assert.Equal(2,await Balance_Async(v_conn,v_scope));
        await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],11,2)],false,v_issue,2));
        long v_receiptDetail=await Detail_ID_Async(v_conn,v_receipt);
        await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(v_receiptDetail,v_scope.Products[0],7,2)],p_iId:v_receipt,p_iVersion:1));
        Assert.Equal(2,await Balance_Async(v_conn,v_scope));
    }

    [Fact]
    public async Task Header_only_is_quantity_neutral_and_date_change_is_blocked()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        long v_id=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        long v_detail=await Detail_ID_Async(v_conn,v_id);
        await Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],10,2)],p_iId:v_id,p_iVersion:1,p_strNumber:v_scope.Tag+"-renamed");
        Assert.Equal(10,await Balance_Async(v_conn,v_scope));
        var v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],10,2)],p_iId:v_id,p_iVersion:2,p_date:new DateTime(2026,1,11)));
        Assert.Equal(51504,v_error.Number);
    }

    [Fact]
    public async Task Unauthorized_actor_and_reused_request_with_different_payload_are_rejected()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)],p_strLogin:"unknown-user"));
        var v_request=Guid.NewGuid();
        await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)],p_request:v_request);
        var v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],11,2)],p_request:v_request));
        Assert.Equal(51505,v_error.Number);
    }

    [Fact]
    public async Task Failed_post_rolls_back_every_insert_and_leaves_connection_without_transaction()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        var v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,
            v_scope.Products.Select(p_i=>new CLine(0,p_i,10,2)).ToArray(),false));
        Assert.Equal(51120,v_error.Number);
        Assert.Equal(0,await Scalar_Async<int>(v_conn,$"SELECT COUNT(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE Kho_ID={v_scope.Warehouse}"));
        Assert.Equal(0,await Scalar_Async<int>(v_conn,"SELECT @@TRANCOUNT"));
    }

    [Fact]
    public async Task Competing_issues_cannot_consume_the_same_available_stock()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        async Task<bool> Try_Issue_Async(string p_strSuffix)
        {
            using var v_other=await Open_Async();
            try { await Save_Async(v_other,v_scope,[new(0,v_scope.Products[0],8,2)],false,p_strNumber:v_scope.Tag+p_strSuffix);return true; }
            catch(SqlException v_ex) when(v_ex.Number is 51403 or 51120) { return false; }
        }
        bool[] v_results=await Task.WhenAll(Try_Issue_Async("-a"),Try_Issue_Async("-b"));
        Assert.Single(v_results,p_b=>p_b);
        Assert.Equal(2,await Balance_Async(v_conn,v_scope));
    }

    [Fact]
    public async Task Two_editors_with_the_same_snapshot_cannot_overwrite_each_other()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        long v_id=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        long v_detail=await Detail_ID_Async(v_conn,v_id);
        CConfig.TKS_Thuc_Tap_V11_Conn_String=v_conn.ConnectionString;
        var v_objDocument=new CXNK_Document_Controller().sp_XNK_Document_Edit_Get(true,v_id,v_scope.Login);
        async Task<bool> Try_Edit_Async(decimal p_quantity)
        {
            using var v_other=await Open_Async();
            try { await Save_Async(v_other,v_scope,[new(v_detail,v_scope.Products[0],p_quantity,2)],p_iId:v_id,p_iVersion:1,p_strExpectedState:v_objDocument.State_Token);return true; }
            catch(SqlException v_ex) when(v_ex.Number is 51403 or 51502) { return false; }
        }
        bool[] v_results=await Task.WhenAll(Try_Edit_Async(12),Try_Edit_Async(15));
        Assert.Single(v_results,p_b=>p_b);
        Assert.Contains(await Balance_Async(v_conn,v_scope),new[]{12m,15m});
    }

    [Fact]
    public async Task CSharp_controller_roundtrip_preserves_audit_and_writes_revision_snapshots()
    {
        using var v_conn=await Open_Async(); var v_scope=await Create_Scope_Async(v_conn);
        CConfig.TKS_Thuc_Tap_V11_Conn_String=v_conn.ConnectionString;
        var v_controller=new CXNK_Document_Controller();
        var v_objDocument=new CWarehouseDocument { So_Phieu=v_scope.Tag,Is_Receipt=true,Kho_ID=v_scope.Warehouse,NCC_ID=v_scope.Supplier,
            Ngay_Chung_Tu=new DateTime(2026,1,10), Details=[new(){San_Pham_ID=v_scope.Products[0],So_Luong=10,Don_Gia=2}] };
        long v_id=v_controller.sp_XNK_Document_Save_Posted(v_objDocument,v_scope.Login);
        var v_objEdit=v_controller.sp_XNK_Document_Edit_Get(true,v_id,v_scope.Login);
        Assert.Equal(v_scope.Login,v_objEdit.Created_By);
        Assert.Single(v_objEdit.Details);
        v_objEdit.Details[0].So_Luong=15;
        v_controller.sp_XNK_Document_Save_Posted(v_objEdit,v_scope.Login);
        Assert.Equal(15,await Balance_Async(v_conn,v_scope));
        Assert.Equal(2,await Scalar_Async<int>(v_conn,$"SELECT COUNT(*) FROM dbo.XNK_Document_Revision WHERE Document_ID={v_id} AND Is_Receipt=1"));
        Assert.Equal(10,await Scalar_Async<decimal>(v_conn,$"SELECT CONVERT(DECIMAL(18,3),JSON_VALUE(Before_State,'$.Details[0].So_Luong')) FROM dbo.XNK_Document_Revision WHERE Document_ID={v_id} AND Is_Receipt=1 AND Document_Version=2"));
    }

    [Fact]
    public async Task Correction_rebuilds_daily_movement_and_period_report_from_the_same_ledger()
    {
        using var v_conn=await Open_Async();var v_scope=await Create_Scope_Async(v_conn);
        long v_id=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        long v_detail=await Detail_ID_Async(v_conn,v_id);
        await Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],15,3)],p_iId:v_id,p_iVersion:1);
        await Scalar_Async<int>(v_conn,$"EXEC dbo.sp_Inventory_Movement_Rebuild @Kho_ID={v_scope.Warehouse},@San_Pham_ID={v_scope.Products[0]},@From_Date='20260110',@To_Date='20260110'; SELECT 1;");
        Assert.Equal(15,await Scalar_Async<decimal>(v_conn,$"SELECT Total_Receipt FROM dbo.Inventory_Movement_Daily WHERE Kho_ID={v_scope.Warehouse} AND San_Pham_ID={v_scope.Products[0]} AND Movement_Date='20260110'"));
        await Scalar_Async<int>(v_conn,"EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size=1000; SELECT 1;");
        using var v_cmd=new SqlCommand("dbo.F9002_sp_Xuat_Nhap_Ton",v_conn) { CommandType=CommandType.StoredProcedure };
        v_cmd.Parameters.AddWithValue("@Tu_Ngay",new DateTime(2026,1,1));v_cmd.Parameters.AddWithValue("@Den_Ngay",new DateTime(2026,1,31));
        v_cmd.Parameters.AddWithValue("@Ma_Dang_Nhap",v_scope.Login);v_cmd.Parameters.AddWithValue("@Kho_ID",v_scope.Warehouse);
        using var v_reader=await v_cmd.ExecuteReaderAsync();
        Assert.True(await v_reader.ReadAsync());
        Assert.Equal(15,Convert.ToDecimal(v_reader["SL_Nhap"]));
    }

    private sealed class CImportList : FWarehouse_Transaction_List
    {
        public void Run_Import(CExcel_Controller p_objExcel, ref int p_iSuccess, ref int p_iErrors) => Import_Excel_Entry(p_objExcel,ref p_iSuccess,ref p_iErrors);
        protected override void BuildRenderTree(RenderTreeBuilder p_builder) { }
        protected override Task OnAfterRenderAsync(bool p_bFirstRender) => Task.CompletedTask;
    }

    private sealed class CTestRenderer : Renderer
    {
        public CTestRenderer() : base(new ServiceCollection().BuildServiceProvider(),NullLoggerFactory.Instance) { }
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public void Attach(IComponent p_component) => AssignRootComponentId(p_component);
        protected override Task UpdateDisplayAsync(in RenderBatch p_batch) => Task.CompletedTask;
        protected override void HandleException(Exception p_exception) => throw p_exception;
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task Real_excel_import_and_manual_save_share_the_posted_contract(bool p_bReceipt)
    {
        using var v_conn=await Open_Async();var v_scope=await Create_Scope_Async(v_conn);
        if(!p_bReceipt) await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        CConfig.TKS_Thuc_Tap_V11_Conn_String=v_conn.ConnectionString;
        string v_strRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
        string v_strTemplate=Path.Combine(v_strRoot,"TKS_Thuc_Tap_V11_Web/wwwroot/FileManagement/Template",p_bReceipt?"Nhap_Kho_Import_Template.xlsx":"Xuat_Kho_Import_Template.xlsx");
        string v_strFile=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
        ExcelPackage.LicenseContext=LicenseContext.NonCommercial;
        try
        {
            using(var v_package=new ExcelPackage(new FileInfo(v_strTemplate)))
            {
                var v_header=v_package.Workbook.Worksheets[0];var v_detail=v_package.Workbook.Worksheets[1];
                v_header.Cells[2,1].Value=v_scope.Tag+"-import";v_header.Cells[2,2].Value="10/01/2026";
                v_header.Cells[2,3].Value=v_scope.Tag;
                if(p_bReceipt)v_header.Cells[2,4].Value=v_scope.Tag;
                v_detail.Cells[2,1].Value=v_scope.Tag+"-import";v_detail.Cells[2,2].Value=v_scope.Tag+"-1";
                v_detail.Cells[2,3].Value=v_scope.Tag;v_detail.Cells[2,4].Value=3;v_detail.Cells[2,5].Value=2;
                v_package.SaveAs(new FileInfo(v_strFile));
            }
            var v_component=new CImportList { m_bIs_Receipt=p_bReceipt,r_strActive_User_Name=v_scope.Login,r_strActive_Function_Code=p_bReceipt?"3001":"3002" };
            v_component.r_objChuc_Nang.Is_Have_Add_Permission=true;
            var v_excel=new CExcel_Controller(new FileInfo(v_strFile));int v_success=0,v_errors=0;
            try
            {
                using var v_renderer=new CTestRenderer();
                await v_renderer.Dispatcher.InvokeAsync(()=>
                {
                    v_renderer.Attach(v_component);
                    v_component.Run_Import(v_excel,ref v_success,ref v_errors);
                });
            }
            finally { v_excel.Close(); }
            Assert.Equal(0,v_errors);Assert.Equal(1,v_success);
            Assert.Equal(p_bReceipt?3m:7m,await Balance_Async(v_conn,v_scope));
        }
        finally { File.Delete(v_strFile); }
    }

    [Fact]
    public async Task Missing_function_permission_is_rejected_even_with_warehouse_assignment()
    {
        using var v_conn=await Open_Async();var v_scope=await Create_Scope_Async(v_conn);
        await Scalar_Async<int>(v_conn,$"UPDATE q SET Is_Have_Add_Permission=0 FROM dbo.tbl_Sys_Phan_Quyen_Chuc_Nang q JOIN dbo.tbl_Sys_Nhom_Thanh_Vien_User u ON u.Nhom_Thanh_Vien_ID=q.Nhom_Thanh_Vien_ID WHERE u.Ma_Dang_Nhap=N'{v_scope.Login}';SELECT 1;");
        var v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]));
        Assert.Equal(51506,v_error.Number);
    }

    [Fact]
    public async Task Application_role_can_use_controlled_correction_but_cannot_update_posted_ledger_directly()
    {
        using var v_conn=await Open_Async();var v_scope=await Create_Scope_Async(v_conn);
        long v_id=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        long v_detail=await Detail_ID_Async(v_conn,v_id);
        await Scalar_Async<int>(v_conn,"IF USER_ID(N'XNK_Test_Application') IS NULL BEGIN CREATE USER XNK_Test_Application WITHOUT LOGIN;ALTER ROLE Warehouse_Application ADD MEMBER XNK_Test_Application;END; GRANT UPDATE ON dbo.tbl_XNK_Nhap_Kho_Raw_Data TO XNK_Test_Application;GRANT SELECT ON dbo.tbl_XNK_Nhap_Kho_Raw_Data TO XNK_Test_Application; SELECT 1;");
        await Scalar_Async<int>(v_conn,"EXECUTE AS USER=N'XNK_Test_Application';SELECT 1;");
        try
        {
            var v_error=await Assert.ThrowsAsync<SqlException>(()=>Scalar_Async<int>(v_conn,$"UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap=12 WHERE Auto_ID={v_detail};SELECT 1;"));
            Assert.Equal(51228,v_error.Number);
            await Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],12,2)],p_iId:v_id,p_iVersion:1);
        }
        finally { await Scalar_Async<int>(v_conn,"REVERT;SELECT 1;"); }
        Assert.Equal(12,await Balance_Async(v_conn,v_scope));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Legacy_documents_without_revision_are_readable_and_save_into_the_new_contract(bool p_bPosted)
    {
        using var v_conn=await Open_Async();var v_scope=await Create_Scope_Async(v_conn);
        CConfig.TKS_Thuc_Tap_V11_Conn_String=v_conn.ConnectionString;
        var v_old=new CXNK_Nhap_Kho_Controller();
        long v_id=v_old.F3001_sp_ins_Nhap_Kho_Header(new(){So_Phieu_Nhap_Kho=v_scope.Tag,Kho_ID=v_scope.Warehouse,NCC_ID=v_scope.Supplier,Ngay_Nhap_Kho=new DateTime(2026,1,10),Ma_Dang_Nhap=v_scope.Login});
        v_old.F3001_sp_ins_Nhap_Kho_Detail(new(){Nhap_Kho_ID=v_id,San_Pham_ID=v_scope.Products[0],SL_Nhap=10,Don_Gia_Nhap=2,Ma_Dang_Nhap=v_scope.Login});
        if(p_bPosted)new CWarehouseDocumentSupport_Controller().sp_XNK_Document_Post(true,v_id,v_scope.Login,v_scope.Login,"3001");
        var v_controller=new CXNK_Document_Controller();var v_edit=v_controller.sp_XNK_Document_Edit_Get(true,v_id,v_scope.Login);
        Assert.Equal(0,v_edit.Document_Version);Assert.Equal(p_bPosted,v_edit.Is_Posted);
        v_edit.Details[0].So_Luong=12;v_controller.sp_XNK_Document_Save_Posted(v_edit,v_scope.Login);
        Assert.Equal(12,await Balance_Async(v_conn,v_scope));
        Assert.Equal(1,v_edit.Document_Version);Assert.True(v_edit.Is_Posted);
    }

    [Fact]
    public async Task Active_draft_reservation_is_preserved_during_correction_and_released_when_that_draft_is_saved()
    {
        using var v_conn=await Open_Async();var v_scope=await Create_Scope_Async(v_conn);
        long v_receipt=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        CConfig.TKS_Thuc_Tap_V11_Conn_String=v_conn.ConnectionString;
        var v_old=new CXNK_Xuat_Kho_Controller();
        long v_issue=v_old.F3002_sp_ins_Xuat_Kho_Header(new(){So_Phieu_Xuat_Kho=v_scope.Tag+"-draft",Kho_ID=v_scope.Warehouse,Ngay_Xuat_Kho=new DateTime(2026,1,10),Ma_Dang_Nhap=v_scope.Login});
        v_old.F3002_sp_ins_Xuat_Kho_Detail(new(){Xuat_Kho_ID=v_issue,San_Pham_ID=v_scope.Products[0],SL_Xuat=6,Don_Gia_Xuat=2,Ma_Dang_Nhap=v_scope.Login});
        long v_detail=await Detail_ID_Async(v_conn,v_receipt);
        var v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(v_detail,v_scope.Products[0],5,2)],p_iId:v_receipt,p_iVersion:1));
        Assert.Equal(51120,v_error.Number);
        Assert.Equal(6,await Scalar_Async<decimal>(v_conn,$"SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID={v_scope.Warehouse} AND San_Pham_ID={v_scope.Products[0]}"));
        var v_controller=new CXNK_Document_Controller();var v_edit=v_controller.sp_XNK_Document_Edit_Get(false,v_issue,v_scope.Login);
        v_controller.sp_XNK_Document_Save_Posted(v_edit,v_scope.Login);
        Assert.Equal(4,await Balance_Async(v_conn,v_scope));
        Assert.Equal(0,await Scalar_Async<decimal>(v_conn,$"SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID={v_scope.Warehouse} AND San_Pham_ID={v_scope.Products[0]}"));
    }

    [Fact]
    public async Task Warehouse_scope_warehouse_change_and_duplicate_number_cannot_be_bypassed()
    {
        using var v_conn=await Open_Async();var v_scope=await Create_Scope_Async(v_conn);var v_other=await Create_Scope_Async(v_conn);
        long v_id=await Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]);
        long v_detail=await Detail_ID_Async(v_conn,v_id);
        var v_requestScope=v_scope with { Warehouse=v_other.Warehouse };
        var v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_requestScope,[new(v_detail,v_scope.Products[0],10,2)],p_iId:v_id,p_iVersion:1));
        Assert.Equal(51054,v_error.Number);
        await Scalar_Async<int>(v_conn,$"INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap,Kho_ID) VALUES(N'{v_scope.Login}',{v_other.Warehouse});SELECT 1;");
        v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_requestScope,[new(v_detail,v_scope.Products[0],10,2)],p_iId:v_id,p_iVersion:1));
        Assert.Equal(51504,v_error.Number);
        v_error=await Assert.ThrowsAsync<SqlException>(()=>Save_Async(v_conn,v_scope,[new(0,v_scope.Products[0],10,2)]));
        Assert.Equal(51507,v_error.Number);
        Assert.Equal(10,await Balance_Async(v_conn,v_scope));
    }

    [Fact]
    public async Task Ambient_transaction_is_rejected_without_rolling_back_caller_work()
    {
        using var v_conn=await Open_Async();
        await Scalar_Async<int>(v_conn,"CREATE TABLE #CallerMarker(Value INT);SELECT 1;");
        using var v_trans=v_conn.BeginTransaction();
        using var v_cmd=new SqlCommand("INSERT #CallerMarker VALUES(1);DECLARE @Request UNIQUEIDENTIFIER=NEWID(); EXEC dbo.sp_XNK_Document_Save_Posted @Is_Receipt=1,@Document_ID=0,@Expected_Version=0,@Request_ID=@Request,@So_Phieu=N'ambient',@Kho_ID=1,@NCC_ID=1,@Ngay_Chung_Tu='20260110',@Ghi_Chu=N'',@Details=N'[]',@Ma_Dang_Nhap=N'test';",v_conn,v_trans);
        var v_error=await Assert.ThrowsAsync<SqlException>(()=>v_cmd.ExecuteNonQueryAsync());
        Assert.Equal(51500,v_error.Number);
        v_cmd.CommandText="SELECT COUNT(*) FROM #CallerMarker WHERE Value=1 AND XACT_STATE()=1 AND @@TRANCOUNT=1";
        Assert.Equal(1,Convert.ToInt32(await v_cmd.ExecuteScalarAsync()));
        v_trans.Rollback();
    }

    [Fact]
    public void UI_has_one_document_form_with_in_memory_lines_and_no_post_action()
    {
        string v_strRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
        string v_strComponents=Path.Combine(v_strRoot,"TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components");
        string v_strList=File.ReadAllText(Path.Combine(v_strComponents,"FWarehouse_Transaction_List.razor"));
        string v_strEditor=File.ReadAllText(Path.Combine(v_strComponents,"FWarehouse_3_Warehouse_Edit.razor"));
        Assert.DoesNotContain("Post_Document_Async",v_strList);
        Assert.Contains("mdi mdi-eye",v_strList);
        Assert.Contains("mdi mdi-cog-outline",v_strList);
        Assert.Contains("m_arrEditDetail",v_strList);
        Assert.Contains("modal-dialog modal-xl",v_strEditor);
        Assert.Contains("m_arrDetail",v_strEditor);
        string v_strInfo=File.ReadAllText(Path.Combine(v_strComponents,"FWarehouse_2_Warehouse_Info.razor"));
        Assert.DoesNotContain("OnAdd_Detail",v_strInfo);
        Assert.Contains("Lịch sử xử lý",v_strInfo);
    }
}
