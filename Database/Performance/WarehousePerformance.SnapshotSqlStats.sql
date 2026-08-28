/* Run against TKS_Thuc_Tap_V11_Perf_<row-count>; PageSize is a sqlcmd variable. */
SET NOCOUNT ON;
SET STATISTICS IO ON;
SET STATISTICS TIME ON;

EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay = '$(FromDate)',
    @Den_Ngay = '$(ToDate)',
    @Page_Number = 1,
    @Page_Size = $(PageSize),
    @Ma_Dang_Nhap = N'PERF_USER';
