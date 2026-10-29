
CREATE   PROCEDURE dbo.F2017_sp_sel_List_Loai_San_Pham_For_Cache
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_LSP, Ten_LSP, Ghi_Chu FROM dbo.view_DM_Loai_San_Pham ORDER BY Ma_LSP;
END
