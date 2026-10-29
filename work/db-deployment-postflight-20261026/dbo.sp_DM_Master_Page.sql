
/* Server-side paging variants of the canonical lists/reports. Each returns two result
   sets: Total_Count first, then the requested page (OFFSET/FETCH). Page_From_Procedure
   in CWarehouse_Controller_Base consumes them; TelerikGrid OnRead drives the page number. */
CREATE   PROCEDURE dbo.sp_DM_Master_Page
    @Entity NVARCHAR(30), @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100)=N''
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number<1 SET @Page_Number=1;
    IF @Page_Size<1 SET @Page_Size=10;
    DECLARE @Filter NVARCHAR(260)=N'%' + @Search_Text + N'%';
    IF @Entity=N'DonViTinh'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.view_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.view_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter ORDER BY Ten_Don_Vi_Tinh OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'LoaiSanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.view_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter;
        SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.view_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter ORDER BY Ma_LSP OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'SanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.view_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter;
        SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name, Loai_San_Pham_ID AS Related_ID, Don_Vi_Tinh_ID AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.view_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter ORDER BY Ma_San_Pham OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'NCC'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.view_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter;
        SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.view_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter ORDER BY Ma_NCC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'Kho'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.view_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.view_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter ORDER BY Ten_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE THROW 51060, N'Loại danh mục không hợp lệ.', 1;
END
