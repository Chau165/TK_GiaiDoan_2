CREATE   PROCEDURE dbo.F2017_sp_sel_List_Loai_San_Pham_Lookup
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_LSP, Ten_LSP FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP;
END