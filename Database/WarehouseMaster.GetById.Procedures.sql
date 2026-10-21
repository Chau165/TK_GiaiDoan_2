CREATE OR ALTER PROCEDURE dbo.F2009_sp_sel_Get_By_ID
    @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ten_Kho, Ghi_Chu, Created, Created_By, Created_By_Function,
           Last_Updated, Last_Updated_By, Last_Updated_By_Function
    FROM dbo.tbl_DM_Kho
    WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.F2016_sp_sel_Get_By_ID
    @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ten_Don_Vi_Tinh, Ghi_Chu, Created, Created_By, Created_By_Function,
           Last_Updated, Last_Updated_By, Last_Updated_By_Function
    FROM dbo.tbl_DM_Don_Vi_Tinh
    WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.F2017_sp_sel_Get_By_ID
    @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_LSP, Ten_LSP, Ghi_Chu, Created, Created_By, Created_By_Function,
           Last_Updated, Last_Updated_By, Last_Updated_By_Function
    FROM dbo.tbl_DM_Loai_San_Pham
    WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.F2018_sp_sel_Get_By_ID
    @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_San_Pham, Ten_San_Pham, Loai_San_Pham_ID, Don_Vi_Tinh_ID,
           Ghi_Chu, Created, Created_By, Created_By_Function, Last_Updated,
           Last_Updated_By, Last_Updated_By_Function
    FROM dbo.tbl_DM_San_Pham
    WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.F2019_sp_sel_Get_By_ID
    @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_NCC, Ten_NCC, Ghi_Chu, Created, Created_By, Created_By_Function,
           Last_Updated, Last_Updated_By, Last_Updated_By_Function
    FROM dbo.tbl_DM_NCC
    WHERE Auto_ID = @Auto_ID;
END
GO
