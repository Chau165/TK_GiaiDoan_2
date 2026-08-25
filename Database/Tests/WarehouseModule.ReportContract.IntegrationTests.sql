SET NOCOUNT ON;

DECLARE @Expected TABLE
(
    Procedure_Name SYSNAME NOT NULL,
    Column_Ordinal INT NOT NULL,
    Column_Name SYSNAME NOT NULL
);

INSERT INTO @Expected(Procedure_Name, Column_Ordinal, Column_Name)
VALUES
    (N'sp_BC_Chi_Tiet_Nhap', 1, N'Ngay'),
    (N'sp_BC_Chi_Tiet_Nhap', 2, N'So_Phieu'),
    (N'sp_BC_Chi_Tiet_Nhap', 3, N'Nha_Cung_Cap'),
    (N'sp_BC_Chi_Tiet_Nhap', 4, N'Ma_San_Pham'),
    (N'sp_BC_Chi_Tiet_Nhap', 5, N'Ten_San_Pham'),
    (N'sp_BC_Chi_Tiet_Nhap', 6, N'So_Luong'),
    (N'sp_BC_Chi_Tiet_Nhap', 7, N'Don_Gia'),
    (N'sp_BC_Chi_Tiet_Nhap', 8, N'Tri_Gia'),
    (N'sp_BC_Chi_Tiet_Xuat', 1, N'Ngay'),
    (N'sp_BC_Chi_Tiet_Xuat', 2, N'So_Phieu'),
    (N'sp_BC_Chi_Tiet_Xuat', 3, N'Nha_Cung_Cap'),
    (N'sp_BC_Chi_Tiet_Xuat', 4, N'Ma_San_Pham'),
    (N'sp_BC_Chi_Tiet_Xuat', 5, N'Ten_San_Pham'),
    (N'sp_BC_Chi_Tiet_Xuat', 6, N'So_Luong'),
    (N'sp_BC_Chi_Tiet_Xuat', 7, N'Don_Gia'),
    (N'sp_BC_Chi_Tiet_Xuat', 8, N'Tri_Gia'),
    (N'sp_BC_Xuat_Nhap_Ton', 1, N'Kho_ID'),
    (N'sp_BC_Xuat_Nhap_Ton', 2, N'San_Pham_ID'),
    (N'sp_BC_Xuat_Nhap_Ton', 3, N'Ma_San_Pham'),
    (N'sp_BC_Xuat_Nhap_Ton', 4, N'Ten_San_Pham'),
    (N'sp_BC_Xuat_Nhap_Ton', 5, N'SL_Dau_Ky'),
    (N'sp_BC_Xuat_Nhap_Ton', 6, N'SL_Nhap'),
    (N'sp_BC_Xuat_Nhap_Ton', 7, N'SL_Xuat'),
    (N'sp_BC_Xuat_Nhap_Ton', 8, N'SL_Cuoi_Ky');

DECLARE @Actual TABLE
(
    Procedure_Name SYSNAME NOT NULL,
    Column_Ordinal INT NOT NULL,
    Column_Name SYSNAME NULL
);

INSERT INTO @Actual
SELECT N'sp_BC_Chi_Tiet_Nhap', column_ordinal, name
FROM sys.dm_exec_describe_first_result_set_for_object(OBJECT_ID(N'dbo.sp_BC_Chi_Tiet_Nhap'), NULL)
WHERE is_hidden = 0;

INSERT INTO @Actual
SELECT N'sp_BC_Chi_Tiet_Xuat', column_ordinal, name
FROM sys.dm_exec_describe_first_result_set_for_object(OBJECT_ID(N'dbo.sp_BC_Chi_Tiet_Xuat'), NULL)
WHERE is_hidden = 0;

INSERT INTO @Actual
SELECT N'sp_BC_Xuat_Nhap_Ton', column_ordinal, name
FROM sys.dm_exec_describe_first_result_set_for_object(OBJECT_ID(N'dbo.sp_BC_Xuat_Nhap_Ton'), NULL)
WHERE is_hidden = 0;

IF EXISTS
(
    SELECT 1
    FROM @Expected e
    LEFT JOIN @Actual a ON a.Procedure_Name=e.Procedure_Name
        AND a.Column_Ordinal=e.Column_Ordinal
        AND a.Column_Name=e.Column_Name
    WHERE a.Procedure_Name IS NULL
)
BEGIN
    SELECT e.Procedure_Name, e.Column_Ordinal, e.Column_Name AS Expected_Column
    FROM @Expected e
    LEFT JOIN @Actual a ON a.Procedure_Name=e.Procedure_Name
        AND a.Column_Ordinal=e.Column_Ordinal
        AND a.Column_Name=e.Column_Name
    WHERE a.Procedure_Name IS NULL;
    THROW 51300, N'Warehouse report result columns do not match report entities.', 1;
END;

PRINT N'PASS: Warehouse report result contracts match report entities';
