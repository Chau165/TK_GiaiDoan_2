/* Run after Database/WarehouseModule.Schema.sql and before the master procedures. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER VIEW dbo.view_DM_Don_Vi_Tinh
AS
SELECT
    A.Auto_ID,
    A.Ten_Don_Vi_Tinh,
    A.Ghi_Chu,
    A.deleted,
    A.Created,
    A.Created_By,
    A.Created_By_Function,
    A.Last_Updated,
    A.Last_Updated_By,
    A.Last_Updated_By_Function
FROM dbo.tbl_DM_Don_Vi_Tinh AS A
WHERE (A.deleted = 0);
GO

CREATE OR ALTER VIEW dbo.view_DM_Kho
AS
SELECT
    A.Auto_ID,
    A.Ten_Kho,
    A.Ghi_Chu,
    A.deleted,
    A.Created,
    A.Created_By,
    A.Created_By_Function,
    A.Last_Updated,
    A.Last_Updated_By,
    A.Last_Updated_By_Function
FROM dbo.tbl_DM_Kho AS A
WHERE (A.deleted = 0);
GO

CREATE OR ALTER VIEW dbo.view_DM_NCC
AS
SELECT
    A.Auto_ID,
    A.Ma_NCC,
    A.Ten_NCC,
    A.Ghi_Chu,
    A.deleted,
    A.Created,
    A.Created_By,
    A.Created_By_Function,
    A.Last_Updated,
    A.Last_Updated_By,
    A.Last_Updated_By_Function
FROM dbo.tbl_DM_NCC AS A
WHERE (A.deleted = 0);
GO

CREATE OR ALTER VIEW dbo.view_DM_Loai_San_Pham
AS
SELECT
    A.Auto_ID,
    A.Ma_LSP,
    A.Ten_LSP,
    A.Ghi_Chu,
    A.deleted,
    A.Created,
    A.Created_By,
    A.Created_By_Function,
    A.Last_Updated,
    A.Last_Updated_By,
    A.Last_Updated_By_Function
FROM dbo.tbl_DM_Loai_San_Pham AS A
WHERE (A.deleted = 0);
GO

CREATE OR ALTER VIEW dbo.view_DM_San_Pham
AS
SELECT
    A.Auto_ID,
    A.Ma_San_Pham,
    A.Ten_San_Pham,
    A.Loai_San_Pham_ID,
    A.Don_Vi_Tinh_ID,
    A.Ghi_Chu,
    A.deleted,
    A.Created,
    A.Created_By,
    A.Created_By_Function,
    A.Last_Updated,
    A.Last_Updated_By,
    A.Last_Updated_By_Function
FROM dbo.tbl_DM_San_Pham AS A
WHERE (A.deleted = 0);
GO
