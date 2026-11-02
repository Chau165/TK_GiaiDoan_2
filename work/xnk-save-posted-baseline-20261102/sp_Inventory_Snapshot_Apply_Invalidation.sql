
/* PERF-06A FOUNDATION END */

CREATE   PROCEDURE dbo.sp_Inventory_Snapshot_Apply_Invalidation
    @Affected dbo.InventorySnapshotAffectedType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM @Affected) RETURN;
    IF EXISTS
    (
        SELECT 1
        FROM @Affected
        WHERE Kho_ID IS NULL OR Kho_ID <= 0
           OR San_Pham_ID IS NULL OR San_Pham_ID <= 0
           OR From_Date IS NULL
    )
        THROW 51441, N'Snapshot invalidation contains an invalid scope or date.', 1;

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
        EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Snapshot_Set
            @ScopeSet = @ScopeSet,
            @Mode = N'Exclusive';
        EXEC dbo.sp_Inventory_Fence_Require_Context
            @GroupSet = @GroupSet,
            @ScopeSet = @ScopeSet,
            @RequiredRootMode = N'Shared',
            @RequiredGroupMode = N'Exclusive',
            @RequiredScopeMode = N'Exclusive';

        CREATE TABLE #AffectedScope
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        From_Date DATE NOT NULL,
        InvalidReason NVARCHAR(100) NOT NULL,
        PRIMARY KEY (Kho_ID, San_Pham_ID)
    );

        INSERT #AffectedScope(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT Kho_ID,
           San_Pham_ID,
           MIN(From_Date),
           CASE WHEN MAX(CASE WHEN InvalidReason = N'BACK_DATE_POST' THEN 1 ELSE 0 END) = 1
                THEN N'BACK_DATE_POST'
                ELSE MAX(InvalidReason)
           END
    FROM @Affected
    GROUP BY Kho_ID, San_Pham_ID;

        DECLARE @InvalidatedAt DATETIME2 = SYSUTCDATETIME();

        UPDATE s
       SET IsValid = 0,
           InvalidatedAt = @InvalidatedAt,
           InvalidReason = a.InvalidReason
    FROM dbo.InventoryBalance_Snapshot_Daily s
    JOIN #AffectedScope a
      ON a.Kho_ID = s.Kho_ID
     AND a.San_Pham_ID = s.San_Pham_ID
     AND s.Snapshot_Date >= a.From_Date;

    /* A new invalidation advances the durable generation even when a worker
       already owns the queue row.  The old claim remains PROCESSING, but its
       completion can no longer publish COMPLETED for the newer generation. */
        UPDATE q
       SET RequestType = CASE WHEN EXISTS
                                   (
                                       SELECT 1
                                       FROM dbo.InventoryBalance_Snapshot_Daily s
                                       WHERE s.Kho_ID = q.Kho_ID
                                         AND s.San_Pham_ID = q.San_Pham_ID
                                   ) THEN N'REBUILD' ELSE N'INITIALIZE' END,
           LifecycleStatus = CASE
                                 WHEN q.LifecycleStatus = N'PROCESSING' THEN N'PROCESSING'
                                 WHEN EXISTS
                                      (
                                          SELECT 1
                                          FROM dbo.InventoryBalance_Snapshot_Daily s
                                          WHERE s.Kho_ID = q.Kho_ID
                                            AND s.San_Pham_ID = q.San_Pham_ID
                                      ) THEN N'WAITING'
                                 ELSE N'INITIALIZE_REQUIRED'
                             END,
           Status = CASE WHEN q.LifecycleStatus = N'PROCESSING' THEN N'PROCESSING' ELSE N'WAITING' END,
           Requested_Version = q.Requested_Version + 1,
           AttemptCount = CASE WHEN q.LifecycleStatus = N'PROCESSING' THEN q.AttemptCount ELSE 0 END,
           NextAttemptAt = NULL,
           LeaseUntil = CASE WHEN q.LifecycleStatus = N'PROCESSING' THEN q.LeaseUntil ELSE NULL END,
           ClaimedBy = CASE WHEN q.LifecycleStatus = N'PROCESSING' THEN q.ClaimedBy ELSE NULL END,
           ClaimedAt = CASE WHEN q.LifecycleStatus = N'PROCESSING' THEN q.ClaimedAt ELSE NULL END,
           Claimed_Version = CASE WHEN q.LifecycleStatus = N'PROCESSING' THEN q.Claimed_Version ELSE NULL END,
           CompletedAt = NULL,
           ErrorMessage = NULL,
           LastError = NULL
    FROM dbo.InventorySnapshot_RebuildQueue q WITH (UPDLOCK, HOLDLOCK)
    JOIN #AffectedScope a
      ON a.Kho_ID = q.Kho_ID
     AND a.San_Pham_ID = q.San_Pham_ID
     AND q.From_Date <= a.From_Date
    WHERE q.LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED', N'FAILED_FINAL');

        UPDATE dl
       SET ResolvedAt = SYSUTCDATETIME(),
           ResolutionNote = N'Reactivated by a newer snapshot invalidation.'
    FROM dbo.InventorySnapshot_RebuildDeadLetter dl
    JOIN dbo.InventorySnapshot_RebuildQueue q ON q.ID = dl.Queue_ID
    JOIN #AffectedScope a
      ON a.Kho_ID = q.Kho_ID
     AND a.San_Pham_ID = q.San_Pham_ID
     AND q.From_Date <= a.From_Date
    WHERE dl.ResolvedAt IS NULL
      AND q.LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED');

    /* The legacy Status column stays backward compatible.  RequestType and
       LifecycleStatus distinguish a missing-snapshot initialization from a
       normal invalid-snapshot rebuild without changing the Post contract. */
        INSERT dbo.InventorySnapshot_RebuildQueue
    (
        Kho_ID, San_Pham_ID, From_Date,
        Status, RequestType, LifecycleStatus,
        CreatedAt, CompletedAt, ErrorMessage, LastError,
        AttemptCount, LastAttemptAt, NextAttemptAt, LeaseUntil, ClaimedBy, ClaimedAt,
        Requested_Version, Claimed_Version
    )
    SELECT a.Kho_ID,
           a.San_Pham_ID,
           a.From_Date,
           N'WAITING',
           CASE WHEN EXISTS
                     (
                         SELECT 1
                         FROM dbo.InventoryBalance_Snapshot_Daily s
                         WHERE s.Kho_ID = a.Kho_ID
                           AND s.San_Pham_ID = a.San_Pham_ID
                     ) THEN N'REBUILD' ELSE N'INITIALIZE' END,
           CASE WHEN EXISTS
                     (
                         SELECT 1
                         FROM dbo.InventoryBalance_Snapshot_Daily s
                         WHERE s.Kho_ID = a.Kho_ID
                           AND s.San_Pham_ID = a.San_Pham_ID
                     ) THEN N'WAITING' ELSE N'INITIALIZE_REQUIRED' END,
           @InvalidatedAt, NULL, NULL, NULL, 0, NULL, NULL, NULL, NULL, NULL, 1, NULL
    FROM #AffectedScope a
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.InventorySnapshot_RebuildQueue q WITH (UPDLOCK, HOLDLOCK)
            WHERE q.Kho_ID = a.Kho_ID
              AND q.San_Pham_ID = a.San_Pham_ID
              AND q.LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED', N'FAILED_FINAL')
              AND q.From_Date <= a.From_Date
        );

        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
