
/*
   Canonical Warehouse read/write contract.
   These definitions intentionally stay in the existing DM/XNK/BC procedure
   groups so CSqlHelper callers do not carry business SQL.
*/
CREATE   PROCEDURE dbo.F2009_sp_sel_List_Kho
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ten_Kho, Ghi_Chu FROM dbo.view_DM_Kho ORDER BY Ten_Kho;
END
