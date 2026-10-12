SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped: current database is not TKS_Thuc_Tap_V11_GiaiDoan2.', 1;

    BEGIN TRANSACTION;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before dropping F2009_sp_sel_List_Kho_For_Cache.', 1;
    IF OBJECT_ID(N'dbo.F2009_sp_sel_List_Kho_For_Cache', N'P') IS NOT NULL
        DROP PROCEDURE dbo.F2009_sp_sel_List_Kho_For_Cache;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before dropping F2016_sp_sel_List_Don_Vi_Tinh_For_Cache.', 1;
    IF OBJECT_ID(N'dbo.F2016_sp_sel_List_Don_Vi_Tinh_For_Cache', N'P') IS NOT NULL
        DROP PROCEDURE dbo.F2016_sp_sel_List_Don_Vi_Tinh_For_Cache;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before dropping F2017_sp_sel_List_Loai_San_Pham_For_Cache.', 1;
    IF OBJECT_ID(N'dbo.F2017_sp_sel_List_Loai_San_Pham_For_Cache', N'P') IS NOT NULL
        DROP PROCEDURE dbo.F2017_sp_sel_List_Loai_San_Pham_For_Cache;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before dropping F2018_sp_sel_List_San_Pham_For_Cache.', 1;
    IF OBJECT_ID(N'dbo.F2018_sp_sel_List_San_Pham_For_Cache', N'P') IS NOT NULL
        DROP PROCEDURE dbo.F2018_sp_sel_List_San_Pham_For_Cache;

    SELECT DB_NAME() AS CurrentDatabase;
    IF DB_NAME() <> N'TKS_Thuc_Tap_V11_GiaiDoan2'
        THROW 51000, 'Rollback stopped before dropping F2019_sp_sel_List_NCC_For_Cache.', 1;
    IF OBJECT_ID(N'dbo.F2019_sp_sel_List_NCC_For_Cache', N'P') IS NOT NULL
        DROP PROCEDURE dbo.F2019_sp_sel_List_NCC_For_Cache;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
