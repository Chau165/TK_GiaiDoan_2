
CREATE   VIEW dbo.view_DM_Don_Vi_Tinh
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
