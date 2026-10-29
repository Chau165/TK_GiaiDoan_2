
CREATE   PROCEDURE dbo.sp_Inventory_Fence_Acquire_Root
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
