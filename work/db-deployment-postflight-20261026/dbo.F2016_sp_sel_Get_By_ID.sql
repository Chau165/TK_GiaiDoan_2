
CREATE   PROCEDURE dbo.F2016_sp_sel_Get_By_ID
    @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ten_Don_Vi_Tinh, Ghi_Chu, Created, Created_By, Created_By_Function,
           Last_Updated, Last_Updated_By, Last_Updated_By_Function
    FROM dbo.view_DM_Don_Vi_Tinh
    WHERE Auto_ID = @Auto_ID;
END
