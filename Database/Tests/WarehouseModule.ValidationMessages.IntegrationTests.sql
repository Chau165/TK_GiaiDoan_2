/*
  Run:
  sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ValidationMessages.IntegrationTests.sql

  Validation-only regression checks. All calls are rejected before mutation and
  the outer transaction is rolled back for isolation.
*/
SET NOCOUNT ON;
SET XACT_ABORT OFF;

BEGIN TRANSACTION;

BEGIN TRY
    DECLARE @Id BIGINT = 0;

    /* The deployed procedure text itself must not contain UTF-8/ANSI mojibake. */
    IF EXISTS
    (
        SELECT 1
        FROM sys.sql_modules m
        JOIN sys.objects o ON o.object_id = m.object_id
        WHERE o.type = 'P'
          AND (o.name LIKE 'sp_DM_%' OR o.name LIKE 'sp_XNK_%' OR o.name LIKE 'sp_BC_%')
          AND (
              m.definition COLLATE Latin1_General_100_BIN2 LIKE N'%Ã%' COLLATE Latin1_General_100_BIN2
              OR m.definition COLLATE Latin1_General_100_BIN2 LIKE N'%Ä%' COLLATE Latin1_General_100_BIN2
              OR m.definition COLLATE Latin1_General_100_BIN2 LIKE N'%Â%' COLLATE Latin1_General_100_BIN2
              OR m.definition COLLATE Latin1_General_100_BIN2 LIKE N'%á»%' COLLATE Latin1_General_100_BIN2
          )
    )
        THROW 53090, 'Warehouse procedure definitions contain mojibake.', 1;

    /* Danh mục: unit, category, product, supplier, warehouse, warehouse-user. */
    BEGIN TRY
        EXEC dbo.sp_DM_Don_Vi_Tinh_Save @Auto_ID = @Id OUTPUT, @Ten_Don_Vi_Tinh = N'', @Ghi_Chu = N'';
        THROW 53000, 'Expected empty unit validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51001 OR ERROR_MESSAGE() <> N'Tên đơn vị tính không được để trống.'
            THROW 53001, 'Unit validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_DM_Don_Vi_Tinh_Save @Auto_ID = @Id OUTPUT, @Ten_Don_Vi_Tinh = N'Cái', @Ghi_Chu = N'';
        THROW 53002, 'Expected duplicate unit validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51002 OR ERROR_MESSAGE() <> N'Tên đơn vị tính đã tồn tại.'
            THROW 53003, 'Duplicate unit message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_DM_Loai_San_Pham_Save @Auto_ID = @Id OUTPUT, @Ma_LSP = N'', @Ten_LSP = N'X', @Ghi_Chu = N'';
        THROW 53004, 'Expected empty category-code validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51010 OR ERROR_MESSAGE() <> N'Mã loại sản phẩm không được để trống.'
            THROW 53005, 'Category validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_DM_San_Pham_Save @Auto_ID = @Id OUTPUT, @Ma_San_Pham = N'UT-ERR', @Ten_San_Pham = N'X', @Loai_San_Pham_ID = 0, @Don_Vi_Tinh_ID = 6, @Ghi_Chu = N'';
        THROW 53006, 'Expected invalid category validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51023 OR ERROR_MESSAGE() <> N'Loại sản phẩm không hợp lệ.'
            THROW 53007, 'Product validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_DM_NCC_Save @Auto_ID = @Id OUTPUT, @Ma_NCC = N'', @Ten_NCC = N'X', @Ghi_Chu = N'';
        THROW 53008, 'Expected empty supplier-code validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51030 OR ERROR_MESSAGE() <> N'Mã nhà cung cấp không được để trống.'
            THROW 53009, 'Supplier validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_DM_Kho_Save @Auto_ID = @Id OUTPUT, @Ten_Kho = N'', @Ghi_Chu = N'';
        THROW 53010, 'Expected empty warehouse validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51040 OR ERROR_MESSAGE() <> N'Tên kho không được để trống.'
            THROW 53011, 'Warehouse validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_DM_Kho_User_Save @Auto_ID = @Id OUTPUT, @Ma_Dang_Nhap = N'', @Kho_ID = 6;
        THROW 53012, 'Expected empty warehouse-user validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51050 OR ERROR_MESSAGE() <> N'Mã đăng nhập không được để trống.'
            THROW 53013, 'Warehouse-user validation message is not Unicode-safe.', 1;
    END CATCH;

    /* Phiếu nhập: missing/duplicate header and invalid detail data. */
    BEGIN TRY
        EXEC dbo.sp_XNK_Nhap_Kho_Save_Header @Auto_ID = @Id OUTPUT, @So_Phieu_Nhap_Kho = N'', @Kho_ID = 6, @NCC_ID = 6, @Ngay_Nhap_Kho = '2026-08-17', @Ghi_Chu = N'';
        THROW 53014, 'Expected empty receipt-number validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51100 OR ERROR_MESSAGE() <> N'Số phiếu nhập không được để trống.'
            THROW 53015, 'Receipt validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_XNK_Nhap_Kho_Save_Header @Auto_ID = @Id OUTPUT, @So_Phieu_Nhap_Kho = N'SEED-PNK-0003', @Kho_ID = 7, @NCC_ID = 6, @Ngay_Nhap_Kho = '2026-08-17', @Ghi_Chu = N'';
        THROW 53016, 'Expected duplicate receipt-number validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51101 OR ERROR_MESSAGE() <> N'Số phiếu nhập đã tồn tại.'
            THROW 53017, 'Duplicate receipt message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_XNK_Nhap_Kho_Save_Detail @Auto_ID = 0, @Nhap_Kho_ID = 10, @San_Pham_ID = 0, @SL_Nhap = 1, @Don_Gia_Nhap = 1;
        THROW 53018, 'Expected invalid receipt-product validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51106 OR ERROR_MESSAGE() <> N'Sản phẩm không hợp lệ.'
            THROW 53019, 'Receipt-detail validation message is not Unicode-safe.', 1;
    END CATCH;

    /* Phiếu xuất: missing/duplicate header and invalid detail data. */
    BEGIN TRY
        EXEC dbo.sp_XNK_Xuat_Kho_Save_Header @Auto_ID = @Id OUTPUT, @So_Phieu_Xuat_Kho = N'', @Kho_ID = 6, @Ngay_Xuat_Kho = '2026-08-17', @Ghi_Chu = N'';
        THROW 53020, 'Expected empty issue-number validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51130 OR ERROR_MESSAGE() <> N'Số phiếu xuất không được để trống.'
            THROW 53021, 'Issue validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_XNK_Xuat_Kho_Save_Header @Auto_ID = @Id OUTPUT, @So_Phieu_Xuat_Kho = N'SEED-PXK-0002', @Kho_ID = 7, @Ngay_Xuat_Kho = '2026-08-17', @Ghi_Chu = N'';
        THROW 53022, 'Expected duplicate issue-number validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51131 OR ERROR_MESSAGE() <> N'Số phiếu xuất đã tồn tại.'
            THROW 53023, 'Duplicate issue message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_XNK_Xuat_Kho_Save_Detail @Auto_ID = 0, @Xuat_Kho_ID = 10, @San_Pham_ID = 0, @SL_Xuat = 1, @Don_Gia_Xuat = 1;
        THROW 53024, 'Expected invalid issue-product validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51135 OR ERROR_MESSAGE() <> N'Sản phẩm không hợp lệ.'
            THROW 53025, 'Issue-detail validation message is not Unicode-safe.', 1;
    END CATCH;

    /* Báo cáo: invalid date range. */
    BEGIN TRY
        EXEC dbo.sp_BC_Xuat_Nhap_Ton @Tu_Ngay = '2026-08-18', @Den_Ngay = '2026-08-17';
        THROW 53026, 'Expected invalid report-date validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51200 OR ERROR_MESSAGE() <> N'Khoảng ngày báo cáo không hợp lệ.'
            THROW 53027, 'Report validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_BC_Chi_Tiet_Nhap @Tu_Ngay = '2026-08-18', @Den_Ngay = '2026-08-17';
        THROW 53030, 'Expected invalid receipt-report date validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51200 OR ERROR_MESSAGE() <> N'Khoảng ngày báo cáo không hợp lệ.'
            THROW 53031, 'Receipt-report validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_BC_Chi_Tiet_Xuat @Tu_Ngay = '2026-08-18', @Den_Ngay = '2026-08-17';
        THROW 53032, 'Expected invalid issue-report date validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51200 OR ERROR_MESSAGE() <> N'Khoảng ngày báo cáo không hợp lệ.'
            THROW 53033, 'Issue-report validation message is not Unicode-safe.', 1;
    END CATCH;

    BEGIN TRY
        EXEC dbo.sp_DM_Delete @Entity = N'Unknown', @Auto_ID = 0;
        THROW 53028, 'Expected invalid master-type validation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 51060 OR ERROR_MESSAGE() <> N'Loại danh mục không hợp lệ.'
            THROW 53029, 'Delete validation message is not Unicode-safe.', 1;
    END CATCH;

    ROLLBACK TRANSACTION;
    PRINT 'PASS: Warehouse validation messages preserve Unicode';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
