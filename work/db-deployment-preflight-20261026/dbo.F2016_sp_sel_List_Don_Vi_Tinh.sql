CREATE   PROCEDURE dbo.F2016_sp_sel_List_Don_Vi_Tinh
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ten_Don_Vi_Tinh, Ghi_Chu FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh;
END