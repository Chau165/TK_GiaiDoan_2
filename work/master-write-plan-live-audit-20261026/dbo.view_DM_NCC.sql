
CREATE   VIEW dbo.view_DM_NCC
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
