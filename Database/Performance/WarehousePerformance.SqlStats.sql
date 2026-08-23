/* Read-only SQL Server probe for the same isolated benchmark database. */
SET NOCOUNT ON;
SET STATISTICS IO ON;
SET STATISTICS TIME ON;

EXEC dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay = '2025-01-01',
    @Den_Ngay = '2026-12-31',
    @Page_Number = 1,
    @Page_Size = $(PageSize);

EXEC dbo.sp_BC_Chi_Tiet_Nhap_Page
    @Tu_Ngay = '2025-01-01',
    @Den_Ngay = '2026-12-31',
    @Page_Number = 1,
    @Page_Size = $(PageSize);

EXEC dbo.sp_DM_Master_Page
    @Entity = N'SanPham',
    @Page_Number = 1,
    @Page_Size = $(PageSize),
    @Search_Text = N'';
