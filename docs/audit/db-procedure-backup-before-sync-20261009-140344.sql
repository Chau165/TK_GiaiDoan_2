/*
Current deployed procedure definitions captured before source-to-database sync.
Server: DESKTOP-NHQ7QPL\MSSQLSERVER19
Database: TKS_Thuc_Tap_V11_GiaiDoan2
Original login: MicrosoftAccount\kiriza165@gmail.com
Effective login: DESKTOP-NHQ7QPL\Surface
Captured local time: 2026-10-09 14:03:48 +07:00
Definitions below are exact OBJECT_DEFINITION text. SET option metadata is recorded per procedure.*/

-- BEGIN dbo.F2016_sp_ins_Don_Vi_Tinh; modify_date=2026-09-29T11:56:50; object_id=17591301; uses_ansi_nulls=True; uses_quoted_identifier=True
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE   PROCEDURE dbo.F2016_sp_ins_Don_Vi_Tinh
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
        IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI=@Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51002,N'Tên đơn vị tính đã tồn tại.',1;
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
-- END dbo.F2016_sp_ins_Don_Vi_Tinh

-- BEGIN dbo.F2016_sp_upd_Don_Vi_Tinh; modify_date=2026-09-29T11:56:50; object_id=33591358; uses_ansi_nulls=True; uses_quoted_identifier=True
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE   PROCEDURE dbo.F2016_sp_upd_Don_Vi_Tinh
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
        IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI=@Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51002,N'Tên đơn vị tính đã tồn tại.',1;
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
-- END dbo.F2016_sp_upd_Don_Vi_Tinh