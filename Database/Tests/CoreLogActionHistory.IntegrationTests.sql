/*
    Integration test for the common action-history lookup used by the
    information dialogs. The transaction is always rolled back.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Expected_Log_ID BIGINT;
    DECLARE @Other_Log_ID BIGINT;
    DECLARE @Ref_ID BIGINT;
    DECLARE @Other_Ref_ID BIGINT;

    SELECT @Expected_Log_ID = ISNULL(MIN(Auto_ID), 0) - 1
    FROM dbo.tbl_Log_Record_Action_History;

    SET @Other_Log_ID = @Expected_Log_ID - 1;
    SET @Ref_ID = @Expected_Log_ID;
    SET @Other_Ref_ID = @Other_Log_ID;

    INSERT INTO dbo.tbl_Log_Record_Action_History
    (
        Auto_ID,
        Ref_ID,
        Ten_Hanh_Dong,
        Ten_Moi_Truong,
        Ma_Chuc_Nang,
        Ten_Chuc_Nang,
        Noi_Dung_Action,
        deleted,
        Created,
        Created_By
    )
    VALUES
    (
        @Expected_Log_ID,
        @Ref_ID,
        N'TDD expected action',
        N'TDD test environment',
        N'TDD-LOG',
        N'TDD action-history function',
        N'The selected reference must be returned.',
        0,
        '2026-08-17T09:00:00',
        N'tdd-test'
    ),
    (
        @Other_Log_ID,
        @Other_Ref_ID,
        N'TDD other action',
        N'TDD test environment',
        N'TDD-LOG',
        N'TDD action-history function',
        N'A different reference must not be returned.',
        0,
        '2026-08-17T09:01:00',
        N'tdd-test'
    );

    SELECT TOP (0) *
    INTO #Actual
    FROM dbo.view_Log_Record_Action_History;

    INSERT INTO #Actual
    EXEC dbo.FCommon_Sys_sp_sel_List_Log_Record_Action_History @Ref_ID = @Ref_ID;

    IF NOT EXISTS (SELECT 1 FROM #Actual WHERE Auto_ID = @Expected_Log_ID)
        THROW 51000, 'Expected action-history record was not returned.', 1;

    IF EXISTS (SELECT 1 FROM #Actual WHERE Ref_ID <> @Ref_ID)
        THROW 51001, 'Action-history lookup returned a record for another reference.', 1;

    ROLLBACK TRANSACTION;
    PRINT 'Core action-history integration tests passed.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
