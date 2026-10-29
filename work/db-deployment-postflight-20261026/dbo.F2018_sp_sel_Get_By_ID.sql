
CREATE   PROCEDURE dbo.F2018_sp_sel_Get_By_ID
    @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID,
           Ghi_Chu, Created, Created_By, Created_By_Function, Last_Updated,
           Last_Updated_By, Last_Updated_By_Function
    FROM dbo.view_DM_San_Pham
    WHERE Auto_ID = @Auto_ID;
END
