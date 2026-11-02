
/* Queue only a narrow daily scope during Post. Recalculation from the ledger is
   intentionally deferred to the worker so document posting stays short. */
CREATE   PROCEDURE dbo.sp_Inventory_Movement_Apply_Invalidation
    @Affected dbo.InventoryMovementAffectedType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM @Affected) RETURN;

    IF EXISTS
    (
        SELECT 1
        FROM @Affected
        WHERE Kho_ID IS NULL OR Kho_ID <= 0
           OR San_Pham_ID IS NULL OR San_Pham_ID <= 0
           OR Movement_Date IS NULL
    )
        THROW 51440, N'Movement invalidation contains an invalid scope or date.', 1;

    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END

    BEGIN TRY
        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        INSERT @GroupSet(Kho_ID)
        SELECT DISTINCT Kho_ID FROM @Affected;
        INSERT @ScopeSet(Kho_ID, San_Pham_ID)
        SELECT DISTINCT Kho_ID, San_Pham_ID FROM @Affected;

        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';
        EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set
            @GroupSet = @GroupSet,
            @Mode = N'Exclusive';
        EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope_Set
            @ScopeSet = @ScopeSet,
            @Mode = N'Exclusive';
        EXEC dbo.sp_Inventory_Fence_Require_Context
            @GroupSet = @GroupSet,
            @ScopeSet = @ScopeSet,
            @RequiredRootMode = N'Shared',
            @RequiredGroupMode = N'Exclusive',
            @RequiredScopeMode = N'Exclusive';

        ;WITH Normalized AS
    (
        SELECT Kho_ID, San_Pham_ID, Movement_Date,
               MAX(InvalidReason) AS InvalidReason
        FROM @Affected
        GROUP BY Kho_ID, San_Pham_ID, Movement_Date
    )
        UPDATE d
    SET IsValid = 0,
        InvalidatedAt = SYSUTCDATETIME(),
        InvalidReason = n.InvalidReason,
        UpdatedAt = SYSUTCDATETIME()
    FROM dbo.Inventory_Movement_Daily d
    JOIN Normalized n ON n.Kho_ID = d.Kho_ID
                     AND n.San_Pham_ID = d.San_Pham_ID
                     AND n.Movement_Date = d.Movement_Date;

        CREATE TABLE #Scope
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        Movement_Date DATE NOT NULL,
        PRIMARY KEY (Kho_ID, San_Pham_ID, Movement_Date)
    );
        INSERT #Scope(Kho_ID, San_Pham_ID, Movement_Date)
    SELECT Kho_ID, San_Pham_ID, Movement_Date
    FROM @Affected
    GROUP BY Kho_ID, San_Pham_ID, Movement_Date;

    /* There is exactly one active daily queue row.  A repeated invalidation of
       a claimed row advances Requested_Version rather than creating a second
       worker.  The claimant must then return it to WAITING after its old build. */
        UPDATE q
    SET Status = CASE WHEN q.Status IN (N'WAITING', N'RETRY_WAITING', N'FAILED_FINAL') THEN N'WAITING' ELSE q.Status END,
        Requested_Version = q.Requested_Version + 1,
        NextRetryAt = NULL,
        LastError = NULL,
        ErrorMessage = NULL,
        ProcessedAt = CASE WHEN q.Status = N'FAILED_FINAL' THEN NULL ELSE q.ProcessedAt END
    FROM dbo.InventoryMovement_RebuildQueue q WITH (UPDLOCK, HOLDLOCK)
    JOIN #Scope s ON s.Kho_ID = q.Kho_ID
                 AND s.San_Pham_ID = q.San_Pham_ID
                 AND s.Movement_Date = q.From_Date
    WHERE q.Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL');

        UPDATE dl
    SET ResolvedAt = SYSUTCDATETIME(),
        ResolutionNote = N'Reactivated by a newer movement invalidation.'
    FROM dbo.InventoryMovement_RebuildDeadLetter dl
    JOIN dbo.InventoryMovement_RebuildQueue q ON q.ID = dl.Queue_ID
    JOIN #Scope s ON s.Kho_ID = q.Kho_ID
                 AND s.San_Pham_ID = q.San_Pham_ID
                 AND s.Movement_Date = q.From_Date
    WHERE dl.ResolvedAt IS NULL
      AND q.Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING');

        INSERT dbo.InventoryMovement_RebuildQueue
    (Kho_ID, San_Pham_ID, From_Date, To_Date, Status, Requested_Version)
    SELECT s.Kho_ID, s.San_Pham_ID, s.Movement_Date, s.Movement_Date, N'WAITING', 1
    FROM #Scope s
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.InventoryMovement_RebuildQueue q WITH (UPDLOCK, HOLDLOCK)
            WHERE q.Kho_ID = s.Kho_ID
              AND q.San_Pham_ID = s.San_Pham_ID
              AND q.From_Date = s.Movement_Date
              AND q.Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL')
        );

        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
