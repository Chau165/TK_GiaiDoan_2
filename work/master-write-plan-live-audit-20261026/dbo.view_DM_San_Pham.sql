
CREATE   VIEW dbo.view_DM_San_Pham
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
