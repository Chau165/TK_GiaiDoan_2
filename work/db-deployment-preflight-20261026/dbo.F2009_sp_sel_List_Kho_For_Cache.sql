CREATE   PROCEDURE dbo.F2009_sp_sel_List_Kho_For_Cache
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ten_Kho, Ghi_Chu FROM dbo.tbl_DM_Kho ORDER BY Ten_Kho;
END