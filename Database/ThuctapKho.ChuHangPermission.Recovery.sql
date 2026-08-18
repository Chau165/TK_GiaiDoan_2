SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @NhomThanhVienId bigint = (
    SELECT Auto_ID FROM dbo.view_Sys_Nhom_Thanh_Vien WHERE Ten_Nhom_Thanh_Vien = N'Chủ hàng'
);
DECLARE @ChucNangId bigint = (
    SELECT Auto_ID FROM dbo.view_Sys_Chuc_Nang WHERE Ma_Chuc_Nang = N'2003'
);

IF @NhomThanhVienId IS NULL OR @ChucNangId IS NULL
    THROW 51000, N'Không tìm thấy nhóm Chủ hàng hoặc chức năng 2003.', 1;

BEGIN TRANSACTION;

IF EXISTS (
    SELECT 1
    FROM dbo.tbl_Sys_Nhom_Thanh_Vien_User
    WHERE Ma_Dang_Nhap = N'thuctap_kho' AND Nhom_Thanh_Vien_ID = @NhomThanhVienId
)
BEGIN
    UPDATE dbo.tbl_Sys_Nhom_Thanh_Vien_User
    SET deleted = 0,
        Last_Updated = GETDATE(),
        Last_Updated_By = N'recovery',
        Last_Updated_By_Function = N'ChuHangPermissionRecovery'
    WHERE Ma_Dang_Nhap = N'thuctap_kho' AND Nhom_Thanh_Vien_ID = @NhomThanhVienId;
END
ELSE
BEGIN
    EXEC dbo.FQ_526_NTVU_sp_ins_Insert
        @Nhom_Thanh_Vien_ID = @NhomThanhVienId,
        @Ma_Dang_Nhap = N'thuctap_kho',
        @Last_Updated_By = N'recovery',
        @Last_Updated_By_Function = N'ChuHangPermissionRecovery';
END;

IF EXISTS (
    SELECT 1
    FROM dbo.tbl_Sys_Phan_Quyen_Chuc_Nang
    WHERE Nhom_Thanh_Vien_ID = @NhomThanhVienId AND Chuc_Nang_ID = @ChucNangId
)
BEGIN
    UPDATE dbo.tbl_Sys_Phan_Quyen_Chuc_Nang
    SET Is_Have_View_Permission = 1,
        Is_Have_Add_Permission = 1,
        Is_Have_Edit_Permission = 1,
        Is_Have_Delete_Permission = 1,
        Is_Have_Export_Permission = 1,
        deleted = 0,
        Last_Updated = GETDATE(),
        Last_Updated_By = N'recovery',
        Last_Updated_By_Function = N'ChuHangPermissionRecovery'
    WHERE Nhom_Thanh_Vien_ID = @NhomThanhVienId AND Chuc_Nang_ID = @ChucNangId;
END
ELSE
BEGIN
    EXEC dbo.FQ_527_PQCN_sp_ins_Insert
        @Nhom_Thanh_Vien_ID = @NhomThanhVienId,
        @Chuc_Nang_ID = @ChucNangId,
        @Is_Have_View_Permission = 1,
        @Is_Have_Add_Permission = 1,
        @Is_Have_Edit_Permission = 1,
        @Is_Have_Delete_Permission = 1,
        @Is_Have_Export_Permission = 1,
        @Last_Updated_By = N'recovery',
        @Last_Updated_By_Function = N'ChuHangPermissionRecovery';
END;

EXEC dbo.FTotal_sp_upd_Thanh_Vien N'thuctap_kho';
COMMIT TRANSACTION;
