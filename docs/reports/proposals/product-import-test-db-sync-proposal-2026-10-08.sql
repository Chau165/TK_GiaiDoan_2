-- PROPOSAL ONLY. Do not execute without explicit TEST DB DDL approval.
-- Run only with sqlcmd in SQLCMD mode; :ON ERROR EXIT makes the identity gate fail closed.
:ON ERROR EXIT
SET NOCOUNT ON;
GO
USE [TKS_Thuc_Tap_V11_C01C02_Test_20260906];
GO
IF @@SERVERNAME <> N'DESKTOP-NHQ7QPL\MSSQLSERVER19'
   OR DB_NAME() <> N'TKS_Thuc_Tap_V11_C01C02_Test_20260906'
BEGIN
    THROW 51990, N'Wrong SQL server or database. TEST DB sync proposal aborted.', 1;
END;
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- Fence dependencies used by F2016 Unit writes and F2018 Product insert.
CREATE OR ALTER FUNCTION dbo.fn_Inventory_Fence_Root_Resource()
RETURNS NVARCHAR(255)
AS
BEGIN
    RETURN N'InventoryMovementGroup:Root';
END;
GO

CREATE OR ALTER FUNCTION dbo.fn_Inventory_Fence_Mode_Satisfies
(
    @ActualMode NVARCHAR(32),
    @RequiredMode NVARCHAR(12)
)
RETURNS BIT
AS
BEGIN
    DECLARE @Result BIT = 0;

    IF @RequiredMode = N'Shared'
       AND @ActualMode IN (N'Shared', N'Exclusive')
        SET @Result = 1;
    ELSE IF @RequiredMode = N'Exclusive'
        AND @ActualMode = N'Exclusive'
        SET @Result = 1;

    RETURN @Result;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Fence_Require_Transaction
AS
BEGIN
    SET NOCOUNT ON;

    IF @@TRANCOUNT = 0
        THROW 51401, N'Inventory fence requires an existing caller transaction.', 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Fence_Acquire_Root
    @Mode NVARCHAR(12)
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.sp_Inventory_Fence_Require_Transaction;

    IF @Mode IS NULL
       OR @Mode COLLATE Latin1_General_100_BIN2 NOT IN (N'Shared', N'Exclusive')
        THROW 51402, N'Inventory fence mode must be exactly Shared or Exclusive.', 1;

    DECLARE @Resource NVARCHAR(255) = dbo.fn_Inventory_Fence_Root_Resource();
    DECLARE @ActualMode NVARCHAR(32) = APPLOCK_MODE(N'public', @Resource, N'Transaction');

    IF dbo.fn_Inventory_Fence_Mode_Satisfies(@ActualMode, @Mode) = 0
    BEGIN
        DECLARE @LockResult INT;

        EXEC @LockResult = sys.sp_getapplock
            @Resource = @Resource,
            @LockMode = @Mode,
            @LockOwner = N'Transaction',
            @LockTimeout = 0,
            @DbPrincipal = N'public';

        IF @LockResult < 0
        BEGIN
            DECLARE @AcquireError NVARCHAR(2048) =
                N'Inventory fence Root acquisition failed. Resource='
                + @Resource
                + N'; Result='
                + CONVERT(NVARCHAR(20), @LockResult);
            THROW 51403, @AcquireError, 1;
        END;
    END;

    SET @ActualMode = APPLOCK_MODE(N'public', @Resource, N'Transaction');

    IF dbo.fn_Inventory_Fence_Mode_Satisfies(@ActualMode, @Mode) = 0
    BEGIN
        DECLARE @VerifyError NVARCHAR(2048) =
            N'Inventory fence Root verification failed. Resource='
            + @Resource
            + N'; EffectiveMode='
            + ISNULL(@ActualMode, N'NULL')
            + N'; RequiredMode='
            + @Mode;
        THROW 51404, @VerifyError, 1;
    END;
END;
GO

-- Unit CRUD now follows the owner-approved CI_AS identity rule.
CREATE OR ALTER PROCEDURE dbo.F2016_sp_ins_Don_Vi_Tinh
    @Auto_ID BIGINT OUTPUT, @Ten_Don_Vi_Tinh NVARCHAR(200), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END;
    BEGIN TRY
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive';
        SET @Ten_Don_Vi_Tinh=LTRIM(RTRIM(ISNULL(@Ten_Don_Vi_Tinh,N'')));
        IF @Ten_Don_Vi_Tinh=N'' THROW 51001,N'Tên đơn vị tính không được để trống.',1;
        IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh COLLATE SQL_Latin1_General_CP1_CI_AS=@Ten_Don_Vi_Tinh COLLATE SQL_Latin1_General_CP1_CI_AS AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51002,N'Tên đơn vị tính đã tồn tại.',1;
        INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ten_Don_Vi_Tinh,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
        SET @Auto_ID=SCOPE_IDENTITY();
        SELECT @Auto_ID AS Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.F2016_sp_upd_Don_Vi_Tinh
    @Auto_ID BIGINT OUTPUT, @Ten_Don_Vi_Tinh NVARCHAR(200), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END;
    BEGIN TRY
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive';
        SET @Ten_Don_Vi_Tinh=LTRIM(RTRIM(ISNULL(@Ten_Don_Vi_Tinh,N'')));
        IF @Ten_Don_Vi_Tinh=N'' THROW 51001,N'Tên đơn vị tính không được để trống.',1;
        IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh COLLATE SQL_Latin1_General_CP1_CI_AS=@Ten_Don_Vi_Tinh COLLATE SQL_Latin1_General_CP1_CI_AS AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51002,N'Tên đơn vị tính đã tồn tại.',1;
        UPDATE dbo.tbl_DM_Don_Vi_Tinh SET Ten_Don_Vi_Tinh=@Ten_Don_Vi_Tinh,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
        SELECT @Auto_ID AS Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- Reference-list procedures used by Product Import Preview.
CREATE OR ALTER PROCEDURE dbo.F2016_sp_sel_List_Don_Vi_Tinh
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh;
END
GO

CREATE OR ALTER PROCEDURE dbo.F2017_sp_sel_List_Loai_San_Pham
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP;
END
GO

-- Related lookup procedures included in the parity audit.
CREATE OR ALTER PROCEDURE dbo.F2016_sp_sel_List_Don_Vi_Tinh_Lookup
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh;
END
GO

CREATE OR ALTER PROCEDURE dbo.F2017_sp_sel_List_Loai_San_Pham_Lookup
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP;
END
GO

CREATE OR ALTER PROCEDURE dbo.F2018_sp_sel_List_San_Pham_Lookup
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name FROM dbo.tbl_DM_San_Pham ORDER BY Ma_San_Pham;
END
GO

-- Product Import Confirm uses this existing source insert contract.
CREATE OR ALTER PROCEDURE dbo.F2018_sp_ins_San_Pham
    @Auto_ID BIGINT OUTPUT, @Ma_San_Pham NVARCHAR(100), @Ten_San_Pham NVARCHAR(255), @Loai_San_Pham_ID BIGINT, @Don_Vi_Tinh_ID BIGINT, @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END;
    BEGIN TRY
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Exclusive';
        SET @Ma_San_Pham=LTRIM(RTRIM(ISNULL(@Ma_San_Pham,N''))); SET @Ten_San_Pham=LTRIM(RTRIM(ISNULL(@Ten_San_Pham,N'')));
        IF @Ma_San_Pham=N'' THROW 51020,N'Mã sản phẩm không được để trống.',1; IF @Ten_San_Pham=N'' THROW 51022,N'Tên sản phẩm không được để trống.',1;
        IF EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham=@Ma_San_Pham AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51021,N'Mã sản phẩm đã tồn tại.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID=@Loai_San_Pham_ID) THROW 51023,N'Loại sản phẩm không hợp lệ.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID=@Don_Vi_Tinh_ID) THROW 51024,N'Đơn vị tính không hợp lệ.',1;
        INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham,Ten_San_Pham,Loai_San_Pham_ID,Don_Vi_Tinh_ID,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ma_San_Pham,@Ten_San_Pham,@Loai_San_Pham_ID,@Don_Vi_Tinh_ID,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
        SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
