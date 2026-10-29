
CREATE   PROCEDURE dbo.F2018_sp_sel_List_San_Pham_For_Cache
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID, Ghi_Chu FROM dbo.view_DM_San_Pham ORDER BY Ma_San_Pham;
END
