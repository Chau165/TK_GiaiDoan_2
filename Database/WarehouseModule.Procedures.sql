SET NOCOUNT ON;
GO

/* ========================================================================
   Inventory snapshot lifecycle (ERP production flow)

   Posting only invalidates affected snapshot rows and enqueues a rebuild.
   Rebuild is intentionally outside the posting transaction and is driven by
   sp_Inventory_Snapshot_Process_RebuildQueue from SQL Agent or an equivalent
   worker.
   ======================================================================== */

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Apply_Invalidation
    @Affected dbo.InventorySnapshotAffectedType READONLY
AS
BEGIN
    SET NOCOUNT ON;

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
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Rebuild
    @Kho_ID BIGINT,
    @San_Pham_ID BIGINT,
    @From_Date DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Kho_ID IS NULL OR @San_Pham_ID IS NULL OR @From_Date IS NULL
        THROW 51302, N'Kho, sản phẩm và ngày rebuild là bắt buộc.', 1;

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @ScopeLockResult INT;
        DECLARE @ScopeLockResource NVARCHAR(255) = CONCAT(N'InventorySnapshot:', @Kho_ID, N':', @San_Pham_ID);
        EXEC @ScopeLockResult = sys.sp_getapplock
            @Resource = @ScopeLockResource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @ScopeLockResult < 0
            THROW 51305, N'Snapshot scope đang được rebuild bởi worker khác.', 1;

        DECLARE @To_Date DATE;
        DECLARE @BaseSnapshot_Date DATE;
        DECLARE @OpeningQuantity DECIMAL(18,3);

        SELECT @To_Date = MAX(s.Snapshot_Date)
        FROM dbo.InventoryBalance_Snapshot_Daily s WITH (UPDLOCK, HOLDLOCK)
        WHERE s.Kho_ID = @Kho_ID
          AND s.San_Pham_ID = @San_Pham_ID
          AND s.Snapshot_Date >= @From_Date
          AND s.IsValid = 0;

        /* No invalid snapshot means there is nothing for this queue row to
           materialize. This is normal when two back-dated events coalesce. */
        IF @To_Date IS NULL
        BEGIN
            COMMIT TRANSACTION;
            RETURN;
        END

        SELECT TOP (1)
               @BaseSnapshot_Date = s.Snapshot_Date,
               @OpeningQuantity = s.ClosingQuantity
        FROM dbo.InventoryBalance_Snapshot_Daily s WITH (UPDLOCK, HOLDLOCK)
        WHERE s.Kho_ID = @Kho_ID
          AND s.San_Pham_ID = @San_Pham_ID
          AND s.Snapshot_Date < @From_Date
          AND s.IsValid = 1
        ORDER BY s.Snapshot_Date DESC;

        /* If no valid snapshot exists, the ledger is the authoritative base.
           Sites with an opening balance must seed that opening balance as a
           valid snapshot before using historical rebuilds. */
        IF @BaseSnapshot_Date IS NULL
        BEGIN
            SELECT @OpeningQuantity = COALESCE(SUM(m.Quantity), 0)
            FROM
            (
                SELECT CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Quantity
                FROM dbo.tbl_XNK_Nhap_Kho h
                JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
                WHERE h.Is_Posted = 1
                  AND h.Kho_ID = @Kho_ID
                  AND d.San_Pham_ID = @San_Pham_ID
                  AND h.Ngay_Nhap_Kho < @From_Date
                UNION ALL
                SELECT CAST(-d.SL_Xuat AS DECIMAL(18,3))
                FROM dbo.tbl_XNK_Xuat_Kho h
                JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
                WHERE h.Is_Posted = 1
                  AND h.Kho_ID = @Kho_ID
                  AND d.San_Pham_ID = @San_Pham_ID
                  AND h.Ngay_Xuat_Kho < @From_Date
            ) m;
        END
        ELSE
            SET @OpeningQuantity = ISNULL(@OpeningQuantity, 0);

        /* The first persisted date may be later than the anchor.  Carry all
           posted movement in that gap into the state used for @From_Date;
           otherwise a rebuild starting on 05/01 would omit movement posted on
           03/01 and produce a false closing balance. */
        IF @BaseSnapshot_Date IS NOT NULL
        BEGIN
            SELECT @OpeningQuantity = @OpeningQuantity + COALESCE(SUM(m.Quantity), 0)
            FROM
            (
                SELECT CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Quantity
                FROM dbo.tbl_XNK_Nhap_Kho h
                JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
                WHERE h.Is_Posted = 1
                  AND h.Kho_ID = @Kho_ID
                  AND d.San_Pham_ID = @San_Pham_ID
                  AND h.Ngay_Nhap_Kho > @BaseSnapshot_Date
                  AND h.Ngay_Nhap_Kho < @From_Date
                UNION ALL
                SELECT CAST(-d.SL_Xuat AS DECIMAL(18,3))
                FROM dbo.tbl_XNK_Xuat_Kho h
                JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
                WHERE h.Is_Posted = 1
                  AND h.Kho_ID = @Kho_ID
                  AND d.San_Pham_ID = @San_Pham_ID
                  AND h.Ngay_Xuat_Kho > @BaseSnapshot_Date
                  AND h.Ngay_Xuat_Kho < @From_Date
            ) m;
        END

        CREATE TABLE #Rebuilt
        (
            Snapshot_Date DATE NOT NULL PRIMARY KEY,
            ClosingQuantity DECIMAL(18,3) NOT NULL
        );

        ;WITH DateRange AS
        (
            SELECT @From_Date AS Snapshot_Date
            UNION ALL
            SELECT DATEADD(DAY, 1, Snapshot_Date)
            FROM DateRange
            WHERE Snapshot_Date < @To_Date
        ), DailyMovement AS
        (
            SELECT m.MovementDate,
                   SUM(m.Quantity) AS NetQuantity
            FROM
            (
                SELECT h.Ngay_Nhap_Kho AS MovementDate,
                       CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Quantity
                FROM dbo.tbl_XNK_Nhap_Kho h
                JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
                WHERE h.Is_Posted = 1
                  AND h.Kho_ID = @Kho_ID
                  AND d.San_Pham_ID = @San_Pham_ID
                  AND h.Ngay_Nhap_Kho BETWEEN @From_Date AND @To_Date
                UNION ALL
                SELECT h.Ngay_Xuat_Kho,
                       CAST(-d.SL_Xuat AS DECIMAL(18,3))
                FROM dbo.tbl_XNK_Xuat_Kho h
                JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
                WHERE h.Is_Posted = 1
                  AND h.Kho_ID = @Kho_ID
                  AND d.San_Pham_ID = @San_Pham_ID
                  AND h.Ngay_Xuat_Kho BETWEEN @From_Date AND @To_Date
            ) m
            GROUP BY m.MovementDate
        ), RunningBalance AS
        (
            SELECT d.Snapshot_Date,
                   CAST
                   (
                       @OpeningQuantity
                       + SUM(ISNULL(dm.NetQuantity, 0)) OVER
                         (ORDER BY d.Snapshot_Date ROWS UNBOUNDED PRECEDING)
                       AS DECIMAL(18,3)
                   ) AS ClosingQuantity
            FROM DateRange d
            LEFT JOIN DailyMovement dm ON dm.MovementDate = d.Snapshot_Date
        )
        INSERT #Rebuilt(Snapshot_Date, ClosingQuantity)
        SELECT Snapshot_Date, ClosingQuantity
        FROM RunningBalance
        OPTION (MAXRECURSION 0);

        UPDATE s
           SET ClosingQuantity = r.ClosingQuantity,
               IsValid = 1,
               InvalidatedAt = NULL,
               InvalidReason = NULL,
               [Version] = ISNULL(s.[Version], 0) + 1
        FROM dbo.InventoryBalance_Snapshot_Daily s
        JOIN #Rebuilt r ON r.Snapshot_Date = s.Snapshot_Date
        WHERE s.Kho_ID = @Kho_ID
          AND s.San_Pham_ID = @San_Pham_ID;

        INSERT dbo.InventoryBalance_Snapshot_Daily
        (
            Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity,
            IsValid, InvalidatedAt, InvalidReason, [Version]
        )
        SELECT r.Snapshot_Date, @Kho_ID, @San_Pham_ID, r.ClosingQuantity,
               1, NULL, NULL, 1
        FROM #Rebuilt r
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.InventoryBalance_Snapshot_Daily s
            WHERE s.Snapshot_Date = r.Snapshot_Date
              AND s.Kho_ID = @Kho_ID
              AND s.San_Pham_ID = @San_Pham_ID
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* Writes a minimal, non-secret heartbeat for the SQL Agent workers.  A
   heartbeat is deliberately separate from queue state so monitoring can also
   detect a worker that has nothing to process. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Record_Heartbeat
    @Worker_Name NVARCHAR(128),
    @Queue_ID BIGINT = NULL,
    @Succeeded BIT = NULL,
    @LastError NVARCHAR(4000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NULLIF(LTRIM(RTRIM(@Worker_Name)), N'') IS NULL
        THROW 51307, N'Tên worker snapshot là bắt buộc.', 1;

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();

    UPDATE dbo.InventorySnapshot_WorkerHeartbeat WITH (UPDLOCK, SERIALIZABLE)
       SET LastHeartbeatAt = @Now,
           LastSuccessAt = CASE WHEN @Succeeded = 1 THEN @Now ELSE LastSuccessAt END,
           LastFailureAt = CASE WHEN @Succeeded = 0 THEN @Now ELSE LastFailureAt END,
           LastQueue_ID = COALESCE(@Queue_ID, LastQueue_ID),
           LastError = CASE WHEN @Succeeded = 0 THEN LEFT(COALESCE(@LastError, N'Unknown snapshot worker failure.'), 4000)
                            WHEN @Succeeded = 1 THEN NULL
                            ELSE LastError END,
           UpdatedAt = @Now
     WHERE Worker_Name = @Worker_Name;

    IF @@ROWCOUNT = 0
        INSERT dbo.InventorySnapshot_WorkerHeartbeat
        (
            Worker_Name, LastHeartbeatAt, LastSuccessAt, LastFailureAt,
            LastQueue_ID, LastError, UpdatedAt
        )
        VALUES
        (
            @Worker_Name, @Now,
            CASE WHEN @Succeeded = 1 THEN @Now END,
            CASE WHEN @Succeeded = 0 THEN @Now END,
            @Queue_ID,
            CASE WHEN @Succeeded = 0 THEN LEFT(COALESCE(@LastError, N'Unknown snapshot worker failure.'), 4000) END,
            @Now
        );
END
GO

/* A baseline is explicit because an opening balance that is not represented
   in the posted ledger cannot be inferred safely.  The confirmation parameter
   records the operator's assertion rather than fabricating a quantity from
   InventoryBalance_Current. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Bootstrap_From_Ledger
    @Baseline_Date DATE,
    @Opening_Balance_Confirmed BIT,
    @Kho_ID BIGINT = NULL,
    @San_Pham_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Baseline_Date IS NULL
        THROW 51308, N'Baseline_Date là bắt buộc.', 1;
    IF @Opening_Balance_Confirmed <> 1
        THROW 51310, N'OPENING_BALANCE_REQUIRED: phải xác nhận opening balance đã có trong Posted Ledger trước khi bootstrap snapshot.', 1;

    DECLARE @Audit_ID BIGINT;
    DECLARE @Now DATETIME2 = SYSUTCDATETIME();
    DECLARE @Rows INT = 0;
    DECLARE @ScopeCount INT = 0;

    INSERT dbo.InventorySnapshot_BootstrapAudit
    (
        Baseline_Date, Kho_ID, San_Pham_ID, Opening_Balance_Confirmed,
        Source_Name, Status, StartedAt
    )
    VALUES
    (
        @Baseline_Date, @Kho_ID, @San_Pham_ID, @Opening_Balance_Confirmed,
        N'POSTED_LEDGER', N'RUNNING', @Now
    );
    SET @Audit_ID = SCOPE_IDENTITY();

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @LockResult INT;
        EXEC @LockResult = sys.sp_getapplock
            @Resource = N'InventorySnapshotBootstrap',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @LockResult < 0
            THROW 51309, N'Bootstrap snapshot đang được thực hiện bởi worker khác.', 1;

        CREATE TABLE #Scope
        (
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            PRIMARY KEY (Kho_ID, San_Pham_ID)
        );

        INSERT #Scope(Kho_ID, San_Pham_ID)
        SELECT h.Kho_ID, d.San_Pham_ID
        FROM dbo.tbl_XNK_Nhap_Kho h
        JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
        WHERE h.Is_Posted = 1
          AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
          AND (@San_Pham_ID IS NULL OR d.San_Pham_ID = @San_Pham_ID)
        UNION
        SELECT h.Kho_ID, d.San_Pham_ID
        FROM dbo.tbl_XNK_Xuat_Kho h
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        WHERE h.Is_Posted = 1
          AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
          AND (@San_Pham_ID IS NULL OR d.San_Pham_ID = @San_Pham_ID);

        SELECT @ScopeCount = COUNT(*) FROM #Scope;

        CREATE TABLE #Balance
        (
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            ClosingQuantity DECIMAL(18,3) NOT NULL,
            PRIMARY KEY (Kho_ID, San_Pham_ID)
        );

        INSERT #Balance(Kho_ID, San_Pham_ID, ClosingQuantity)
        SELECT s.Kho_ID,
               s.San_Pham_ID,
               CAST(COALESCE(SUM(m.Quantity), 0) AS DECIMAL(18,3))
        FROM #Scope s
        LEFT JOIN
        (
            SELECT h.Kho_ID, d.San_Pham_ID, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Quantity
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1 AND h.Ngay_Nhap_Kho <= @Baseline_Date
            UNION ALL
            SELECT h.Kho_ID, d.San_Pham_ID, CAST(-d.SL_Xuat AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1 AND h.Ngay_Xuat_Kho <= @Baseline_Date
        ) m ON m.Kho_ID = s.Kho_ID AND m.San_Pham_ID = s.San_Pham_ID
        GROUP BY s.Kho_ID, s.San_Pham_ID;

        UPDATE snapshot
           SET ClosingQuantity = b.ClosingQuantity,
               IsValid = 1,
               InvalidatedAt = NULL,
               InvalidReason = NULL,
               [Version] = ISNULL(snapshot.[Version], 0) + 1
        FROM dbo.InventoryBalance_Snapshot_Daily snapshot
        JOIN #Balance b ON b.Kho_ID = snapshot.Kho_ID AND b.San_Pham_ID = snapshot.San_Pham_ID
        WHERE snapshot.Snapshot_Date = @Baseline_Date;
        SET @Rows = @@ROWCOUNT;

        INSERT dbo.InventoryBalance_Snapshot_Daily
        (
            Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity,
            IsValid, InvalidatedAt, InvalidReason, [Version]
        )
        SELECT @Baseline_Date, b.Kho_ID, b.San_Pham_ID, b.ClosingQuantity,
               1, NULL, NULL, 1
        FROM #Balance b
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.InventoryBalance_Snapshot_Daily snapshot WITH (UPDLOCK, HOLDLOCK)
            WHERE snapshot.Snapshot_Date = @Baseline_Date
              AND snapshot.Kho_ID = b.Kho_ID
              AND snapshot.San_Pham_ID = b.San_Pham_ID
        );
        SET @Rows += @@ROWCOUNT;

        UPDATE dbo.InventorySnapshot_BootstrapAudit
           SET Status = N'COMPLETED',
               CompletedAt = SYSUTCDATETIME(),
               ScopeCount = @ScopeCount,
               SnapshotRowCount = @Rows,
               ErrorMessage = NULL
         WHERE ID = @Audit_ID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        UPDATE dbo.InventorySnapshot_BootstrapAudit
           SET Status = N'FAILED',
               CompletedAt = SYSUTCDATETIME(),
               ErrorMessage = LEFT(ERROR_MESSAGE(), 4000)
         WHERE ID = @Audit_ID;
        THROW;
    END CATCH
END
GO

/* INITIALIZE is intentionally not delegated to Rebuild.  It creates the
   missing checkpoint directly from posted ledger after bootstrap has recorded
   a trustworthy baseline for this scope. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Initialize_From_Ledger
    @Kho_ID BIGINT,
    @San_Pham_ID BIGINT,
    @Snapshot_Date DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Kho_ID IS NULL OR @San_Pham_ID IS NULL OR @Snapshot_Date IS NULL
        THROW 51302, N'Kho, sản phẩm và ngày initialize snapshot là bắt buộc.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventorySnapshot_BootstrapAudit audit
        WHERE audit.Status = N'COMPLETED'
          AND audit.Baseline_Date <= @Snapshot_Date
          AND (audit.Kho_ID IS NULL OR audit.Kho_ID = @Kho_ID)
          AND (audit.San_Pham_ID IS NULL OR audit.San_Pham_ID = @San_Pham_ID)
    )
        THROW 51311, N'INITIALIZE_REQUIRED: chưa có bootstrap Posted Ledger được xác nhận cho scope snapshot này.', 1;

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @LockResult INT;
        DECLARE @LockResource NVARCHAR(255) = CONCAT(N'InventorySnapshot:', @Kho_ID, N':', @San_Pham_ID);
        EXEC @LockResult = sys.sp_getapplock
            @Resource = @LockResource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @LockResult < 0
            THROW 51305, N'Snapshot scope đang được rebuild bởi worker khác.', 1;

        DECLARE @ClosingQuantity DECIMAL(18,3);
        SELECT @ClosingQuantity = CAST(COALESCE(SUM(m.Quantity), 0) AS DECIMAL(18,3))
        FROM
        (
            SELECT CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Quantity
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
              AND h.Kho_ID = @Kho_ID
              AND d.San_Pham_ID = @San_Pham_ID
              AND h.Ngay_Nhap_Kho <= @Snapshot_Date
            UNION ALL
            SELECT CAST(-d.SL_Xuat AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
              AND h.Kho_ID = @Kho_ID
              AND d.San_Pham_ID = @San_Pham_ID
              AND h.Ngay_Xuat_Kho <= @Snapshot_Date
        ) m;

        UPDATE dbo.InventoryBalance_Snapshot_Daily
           SET ClosingQuantity = @ClosingQuantity,
               IsValid = 1,
               InvalidatedAt = NULL,
               InvalidReason = NULL,
               [Version] = ISNULL([Version], 0) + 1
         WHERE Snapshot_Date = @Snapshot_Date
           AND Kho_ID = @Kho_ID
           AND San_Pham_ID = @San_Pham_ID;

        IF @@ROWCOUNT = 0
            INSERT dbo.InventoryBalance_Snapshot_Daily
            (
                Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity,
                IsValid, InvalidatedAt, InvalidReason, [Version]
            )
            VALUES
            (
                @Snapshot_Date, @Kho_ID, @San_Pham_ID, @ClosingQuantity,
                1, NULL, NULL, 1
            );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* Finalization deliberately consumes Daily.  The Current projection is never
   a historical snapshot source. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Finalize_Daily
    @Snapshot_Date DATE,
    @Kho_ID BIGINT = NULL,
    @San_Pham_ID BIGINT = NULL,
    @Worker_Name NVARCHAR(128) = N'SQLAgent:InventorySnapshotFinalize'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Snapshot_Date IS NULL
        THROW 51312, N'Snapshot_Date là bắt buộc.', 1;

    BEGIN TRY
        /* Do not publish a final checkpoint while the Daily projection is
           known to be behind a posted invalidation for the requested date. */
        IF EXISTS
        (
            SELECT 1
            FROM dbo.InventoryMovement_RebuildQueue q
            WHERE q.From_Date <= @Snapshot_Date
              AND
              (
                  q.Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL')
                  OR EXISTS
                     (
                         SELECT 1
                         FROM dbo.InventoryMovement_RebuildDeadLetter dl
                         WHERE dl.Queue_ID = q.ID
                           AND dl.ResolvedAt IS NULL
                     )
              )
              AND (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
              AND (@San_Pham_ID IS NULL OR q.San_Pham_ID = @San_Pham_ID)
        )
            THROW 51320, N'DAILY_PROJECTION_NOT_READY: còn movement rebuild chưa hoàn tất trước ngày finalize.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.InventorySnapshot_RebuildQueue q
            WHERE q.From_Date <= @Snapshot_Date
              AND
              (
                  q.LifecycleStatus = N'FAILED_FINAL'
                  OR EXISTS
                     (
                         SELECT 1
                         FROM dbo.InventorySnapshot_RebuildDeadLetter dl
                         WHERE dl.Queue_ID = q.ID
                           AND dl.ResolvedAt IS NULL
                     )
              )
              AND (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
              AND (@San_Pham_ID IS NULL OR q.San_Pham_ID = @San_Pham_ID)
        )
            THROW 51321, N'SNAPSHOT_REBUILD_FAILED_FINAL: checkpoint bị chặn bởi failure chưa được recovery.', 1;

        BEGIN TRANSACTION;
        DECLARE @LockResult INT;
        EXEC @LockResult = sys.sp_getapplock
            @Resource = N'InventorySnapshotFinalize',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @LockResult < 0
            THROW 51313, N'Finalize snapshot đang được thực hiện bởi worker khác.', 1;

        /* Repeat terminal-failure guards after taking the finalize lock so a
           failure committed while the initial freshness check was running
           cannot be published as a valid checkpoint. */
        IF EXISTS
        (
            SELECT 1
            FROM dbo.InventoryMovement_RebuildQueue q
            WHERE q.From_Date <= @Snapshot_Date
              AND
              (
                  q.Status = N'FAILED_FINAL'
                  OR EXISTS
                     (
                         SELECT 1
                         FROM dbo.InventoryMovement_RebuildDeadLetter dl
                         WHERE dl.Queue_ID = q.ID
                           AND dl.ResolvedAt IS NULL
                     )
              )
              AND (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
              AND (@San_Pham_ID IS NULL OR q.San_Pham_ID = @San_Pham_ID)
        )
            THROW 51320, N'DAILY_PROJECTION_NOT_READY: movement rebuild failure chưa được recovery trước ngày finalize.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.InventorySnapshot_RebuildQueue q
            WHERE q.From_Date <= @Snapshot_Date
              AND
              (
                  q.LifecycleStatus = N'FAILED_FINAL'
                  OR EXISTS
                     (
                         SELECT 1
                         FROM dbo.InventorySnapshot_RebuildDeadLetter dl
                         WHERE dl.Queue_ID = q.ID
                           AND dl.ResolvedAt IS NULL
                     )
              )
              AND (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
              AND (@San_Pham_ID IS NULL OR q.San_Pham_ID = @San_Pham_ID)
        )
            THROW 51321, N'SNAPSHOT_REBUILD_FAILED_FINAL: checkpoint bị chặn bởi failure chưa được recovery.', 1;

        /* Finalize and Post share the same per-scope fence used by Daily
           rebuild. A shared lock lets Finalize complete before a Post, while
           a committed Post cannot pass this point without excluding the
           publication read. Sorted acquisition keeps multi-scope operations
           in one lock order. */
        DECLARE @FinalizeScopeLockResult INT;
        DECLARE @FinalizeScopeLockResource NVARCHAR(255);
        DECLARE @FinalizeScopeKho_ID BIGINT, @FinalizeScopeSan_Pham_ID BIGINT;
        DECLARE finalize_scope_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT DISTINCT b.Kho_ID, b.San_Pham_ID
            FROM dbo.Inventory_Balance_Daily b
            WHERE b.IsValid = 1
              AND b.Balance_Date <= @Snapshot_Date
              AND (@Kho_ID IS NULL OR b.Kho_ID = @Kho_ID)
              AND (@San_Pham_ID IS NULL OR b.San_Pham_ID = @San_Pham_ID)
            ORDER BY b.Kho_ID, b.San_Pham_ID;
        OPEN finalize_scope_cursor;
        FETCH NEXT FROM finalize_scope_cursor INTO @FinalizeScopeKho_ID, @FinalizeScopeSan_Pham_ID;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @FinalizeScopeLockResource = CONCAT(N'InventoryMovement:', @FinalizeScopeKho_ID, N':', @FinalizeScopeSan_Pham_ID);
            EXEC @FinalizeScopeLockResult = sys.sp_getapplock
                @Resource = @FinalizeScopeLockResource,
                @LockMode = N'Shared',
                @LockOwner = N'Transaction',
                @LockTimeout = 0;
            IF @FinalizeScopeLockResult < 0
            BEGIN
                CLOSE finalize_scope_cursor;
                DEALLOCATE finalize_scope_cursor;
                THROW 51322, N'Projection scope đang được Post hoặc rebuild; Finalize phải chạy lại sau khi scope ổn định.', 1;
            END
            FETCH NEXT FROM finalize_scope_cursor INTO @FinalizeScopeKho_ID, @FinalizeScopeSan_Pham_ID;
        END
        CLOSE finalize_scope_cursor;
        DEALLOCATE finalize_scope_cursor;

        CREATE TABLE #Finalized
        (
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            ClosingQuantity DECIMAL(18,3) NOT NULL,
            PRIMARY KEY (Kho_ID, San_Pham_ID)
        );

        ;WITH DailyRanked AS
        (
            SELECT b.Kho_ID,
                   b.San_Pham_ID,
                   b.ClosingQuantity,
                   ROW_NUMBER() OVER
                   (
                       PARTITION BY b.Kho_ID, b.San_Pham_ID
                       ORDER BY b.Balance_Date DESC
                   ) AS RowNo
            FROM dbo.Inventory_Balance_Daily b
            WHERE b.IsValid = 1
              AND b.Balance_Date <= @Snapshot_Date
              AND (@Kho_ID IS NULL OR b.Kho_ID = @Kho_ID)
              AND (@San_Pham_ID IS NULL OR b.San_Pham_ID = @San_Pham_ID)
        )
        INSERT #Finalized(Kho_ID, San_Pham_ID, ClosingQuantity)
        SELECT Kho_ID, San_Pham_ID, ClosingQuantity
        FROM DailyRanked
        WHERE RowNo = 1;

        UPDATE snapshot
           SET ClosingQuantity = final.ClosingQuantity,
               IsValid = 1,
               InvalidatedAt = NULL,
               InvalidReason = NULL,
               [Version] = ISNULL(snapshot.[Version], 0) + 1
        FROM dbo.InventoryBalance_Snapshot_Daily snapshot
        JOIN #Finalized final
          ON final.Kho_ID = snapshot.Kho_ID
         AND final.San_Pham_ID = snapshot.San_Pham_ID
        WHERE snapshot.Snapshot_Date = @Snapshot_Date;

        INSERT dbo.InventoryBalance_Snapshot_Daily
        (
            Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity,
            IsValid, InvalidatedAt, InvalidReason, [Version]
        )
        SELECT @Snapshot_Date, final.Kho_ID, final.San_Pham_ID, final.ClosingQuantity,
               1, NULL, NULL, 1
        FROM #Finalized final
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.InventoryBalance_Snapshot_Daily snapshot WITH (UPDLOCK, HOLDLOCK)
            WHERE snapshot.Snapshot_Date = @Snapshot_Date
              AND snapshot.Kho_ID = final.Kho_ID
              AND snapshot.San_Pham_ID = final.San_Pham_ID
        );

        /* A queue means a post/back-date has already invalidated history.
           Preserve that signal rather than declaring the newly finalised row
           valid ahead of its repair. */
        UPDATE snapshot
           SET IsValid = 0,
               InvalidatedAt = SYSUTCDATETIME(),
               InvalidReason = N'PENDING_SNAPSHOT_REPAIR'
        FROM dbo.InventoryBalance_Snapshot_Daily snapshot
        WHERE snapshot.Snapshot_Date = @Snapshot_Date
          AND EXISTS
          (
              SELECT 1
              FROM dbo.InventorySnapshot_RebuildQueue q
              WHERE q.Kho_ID = snapshot.Kho_ID
                AND q.San_Pham_ID = snapshot.San_Pham_ID
                AND q.From_Date <= @Snapshot_Date
                 AND q.LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED', N'FAILED_FINAL')
           );

        COMMIT TRANSACTION;
        EXEC dbo.sp_Inventory_Snapshot_Record_Heartbeat
            @Worker_Name = @Worker_Name,
            @Succeeded = 1;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        DECLARE @FinalizeError NVARCHAR(4000) = ERROR_MESSAGE();
        EXEC dbo.sp_Inventory_Snapshot_Record_Heartbeat
            @Worker_Name = @Worker_Name,
            @Succeeded = 0,
            @LastError = @FinalizeError;
        THROW;
    END CATCH
END
GO

/* A worker may complete only the generation it claimed.  A newer invalidation
   leaves the same row WAITING so the next worker can claim the latest version. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Complete_Claim
    @Queue_ID BIGINT,
    @Claimed_Version INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Queue_ID IS NULL OR @Claimed_Version IS NULL
        THROW 51316, N'Queue claim snapshot không hợp lệ.', 1;

    UPDATE q
       SET Status = CASE WHEN q.Requested_Version = @Claimed_Version THEN N'COMPLETED' ELSE N'WAITING' END,
           LifecycleStatus = CASE WHEN q.Requested_Version = @Claimed_Version THEN N'COMPLETED' ELSE N'WAITING' END,
           AttemptCount = CASE WHEN q.Requested_Version = @Claimed_Version THEN q.AttemptCount ELSE 0 END,
           CompletedAt = CASE WHEN q.Requested_Version = @Claimed_Version THEN SYSUTCDATETIME() ELSE NULL END,
           NextAttemptAt = NULL,
           LeaseUntil = NULL,
           ClaimedBy = NULL,
           ClaimedAt = NULL,
           ErrorMessage = NULL,
           LastError = NULL
    FROM dbo.InventorySnapshot_RebuildQueue q WITH (UPDLOCK, HOLDLOCK)
    WHERE q.ID = @Queue_ID
      AND q.LifecycleStatus = N'PROCESSING'
      AND q.Claimed_Version = @Claimed_Version;

    IF @@ROWCOUNT <> 1
        THROW 51317, N'Queue claim snapshot không còn hợp lệ hoặc đã được worker khác hoàn tất.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Process_RebuildQueue
    @Batch_Size INT = 100,
    @Max_Retry_Count INT = 5,
    @Processing_Lease_Seconds INT = 300,
    @Worker_Name NVARCHAR(128) = N'SQLAgent:InventorySnapshotRepair',
    @Kho_ID BIGINT = NULL,
    @San_Pham_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Batch_Size IS NULL OR @Batch_Size < 1
        THROW 51303, N'Batch size rebuild phải lớn hơn 0.', 1;
    IF @Max_Retry_Count IS NULL OR @Max_Retry_Count < 1
        THROW 51314, N'Max retry snapshot phải lớn hơn 0.', 1;
    IF @Processing_Lease_Seconds IS NULL OR @Processing_Lease_Seconds < 1
        THROW 51315, N'Processing lease snapshot phải lớn hơn 0.', 1;
    IF NULLIF(LTRIM(RTRIM(@Worker_Name)), N'') IS NULL
        THROW 51307, N'Tên worker snapshot là bắt buộc.', 1;

    DECLARE @LockResult INT;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'InventorySnapshotRebuildWorker',
        @LockMode = N'Exclusive',
        @LockOwner = N'Session',
        @LockTimeout = 0;
    IF @LockResult < 0
        THROW 51304, N'Worker rebuild snapshot đang được xử lý bởi tiến trình khác.', 1;

    BEGIN TRY
        /* A crashed worker leaves a lease rather than being silently reset.
           Expired claims consume a retry and retain their error history. */
        DECLARE @Now DATETIME2 = SYSUTCDATETIME();
        DECLARE @Processed INT = 0;
        DECLARE @TickHadFailure BIT = 0;
        DECLARE @TickLastError NVARCHAR(4000) = NULL;
        DECLARE @ExpiredLeaseCount INT = 0;
        UPDATE q
           SET AttemptCount = q.AttemptCount + 1,
               LastAttemptAt = @Now,
               LastError = N'LEASE_EXPIRED: snapshot worker claim was not completed before LeaseUntil.',
               ErrorMessage = N'LEASE_EXPIRED: snapshot worker claim was not completed before LeaseUntil.',
               LifecycleStatus = CASE WHEN q.AttemptCount + 1 >= @Max_Retry_Count THEN N'FAILED_FINAL' ELSE N'RETRY_WAITING' END,
               Status = CASE WHEN q.AttemptCount + 1 >= @Max_Retry_Count THEN N'FAILED' ELSE N'WAITING' END,
               NextAttemptAt = CASE WHEN q.AttemptCount + 1 >= @Max_Retry_Count THEN NULL
                                    ELSE DATEADD(MINUTE, CASE q.AttemptCount + 1 WHEN 1 THEN 1 WHEN 2 THEN 5 WHEN 3 THEN 15 ELSE 60 END, @Now) END,
               LeaseUntil = NULL,
               ClaimedBy = NULL,
               ClaimedAt = NULL,
               Claimed_Version = NULL,
               CompletedAt = CASE WHEN q.AttemptCount + 1 >= @Max_Retry_Count THEN @Now ELSE NULL END
        FROM dbo.InventorySnapshot_RebuildQueue q
        WHERE q.LifecycleStatus = N'PROCESSING'
          AND q.LeaseUntil < @Now
          AND (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
          AND (@San_Pham_ID IS NULL OR q.San_Pham_ID = @San_Pham_ID);
        SET @ExpiredLeaseCount = @@ROWCOUNT;
        IF @ExpiredLeaseCount > 0
        BEGIN
            SET @TickHadFailure = 1;
            SET @TickLastError = N'LEASE_EXPIRED: one or more snapshot worker claims required recovery.';
        END

        INSERT dbo.InventorySnapshot_RebuildDeadLetter
        (
            Queue_ID, Kho_ID, San_Pham_ID, From_Date, RequestType,
            AttemptCount, LastError, FailedAt
        )
        SELECT q.ID, q.Kho_ID, q.San_Pham_ID, q.From_Date, q.RequestType,
               q.AttemptCount, q.LastError, @Now
        FROM dbo.InventorySnapshot_RebuildQueue q
        WHERE q.LifecycleStatus = N'FAILED_FINAL'
          AND q.LastError LIKE N'LEASE_EXPIRED:%'
          AND NOT EXISTS (SELECT 1 FROM dbo.InventorySnapshot_RebuildDeadLetter d WHERE d.Queue_ID = q.ID);

        CREATE TABLE #Claimed
        (
            ID BIGINT NOT NULL PRIMARY KEY,
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            From_Date DATE NOT NULL,
            RequestType NVARCHAR(20) NOT NULL,
            Claimed_Version INT NOT NULL
        );

        WHILE @Processed < @Batch_Size
        BEGIN
            DECLARE @QueueId BIGINT = NULL;
            DECLARE @QueueKho_ID BIGINT;
            DECLARE @QueueSan_Pham_ID BIGINT;
            DECLARE @From_Date DATE;
            DECLARE @RequestType NVARCHAR(20);
            DECLARE @Claimed_Version INT;

            BEGIN TRANSACTION;
            ;WITH NextItem AS
            (
                SELECT TOP (1)
                       q.ID, q.Kho_ID, q.San_Pham_ID, q.From_Date, q.RequestType, q.Requested_Version, q.Claimed_Version,
                       q.Status, q.LifecycleStatus, q.LastAttemptAt, q.LeaseUntil,
                       q.ClaimedBy, q.ClaimedAt, q.ErrorMessage, q.CompletedAt
                FROM dbo.InventorySnapshot_RebuildQueue q WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
                  AND (@San_Pham_ID IS NULL OR q.San_Pham_ID = @San_Pham_ID)
                  AND
                  (
                      q.LifecycleStatus = N'WAITING'
                      OR (q.LifecycleStatus = N'RETRY_WAITING' AND q.NextAttemptAt <= @Now)
                      OR
                      (
                          q.LifecycleStatus = N'INITIALIZE_REQUIRED'
                          AND EXISTS
                          (
                              SELECT 1
                              FROM dbo.InventorySnapshot_BootstrapAudit audit
                              WHERE audit.Status = N'COMPLETED'
                                AND audit.Baseline_Date <= q.From_Date
                                AND (audit.Kho_ID IS NULL OR audit.Kho_ID = q.Kho_ID)
                                AND (audit.San_Pham_ID IS NULL OR audit.San_Pham_ID = q.San_Pham_ID)
                          )
                      )
                  )
                ORDER BY CASE WHEN q.LifecycleStatus = N'INITIALIZE_REQUIRED' THEN 0 ELSE 1 END,
                         q.CreatedAt, q.ID
            )
            UPDATE NextItem
               SET Status = N'PROCESSING',
                   LifecycleStatus = N'PROCESSING',
                   LastAttemptAt = @Now,
                   LeaseUntil = DATEADD(SECOND, @Processing_Lease_Seconds, @Now),
                   ClaimedBy = @Worker_Name,
                   ClaimedAt = @Now,
                   Claimed_Version = Requested_Version,
                   ErrorMessage = NULL,
                   CompletedAt = NULL
             OUTPUT inserted.ID, inserted.Kho_ID, inserted.San_Pham_ID, inserted.From_Date, inserted.RequestType, inserted.Claimed_Version
             INTO #Claimed(ID, Kho_ID, San_Pham_ID, From_Date, RequestType, Claimed_Version);

            SELECT TOP (1)
                   @QueueId = ID,
                   @QueueKho_ID = Kho_ID,
                   @QueueSan_Pham_ID = San_Pham_ID,
                   @From_Date = From_Date,
                   @RequestType = RequestType,
                   @Claimed_Version = Claimed_Version
            FROM #Claimed;
            DELETE FROM #Claimed;
            COMMIT TRANSACTION;

            IF @QueueId IS NULL BREAK;

            BEGIN TRY
                IF @RequestType = N'INITIALIZE'
                    EXEC dbo.sp_Inventory_Snapshot_Initialize_From_Ledger
                        @Kho_ID = @QueueKho_ID,
                        @San_Pham_ID = @QueueSan_Pham_ID,
                        @Snapshot_Date = @From_Date;
                ELSE
                    EXEC dbo.sp_Inventory_Snapshot_Rebuild
                        @Kho_ID = @QueueKho_ID,
                        @San_Pham_ID = @QueueSan_Pham_ID,
                        @From_Date = @From_Date;

                EXEC dbo.sp_Inventory_Snapshot_Complete_Claim
                    @Queue_ID = @QueueId,
                    @Claimed_Version = @Claimed_Version;
                EXEC dbo.sp_Inventory_Snapshot_Record_Heartbeat
                    @Worker_Name = @Worker_Name,
                    @Queue_ID = @QueueId,
                    @Succeeded = 1;
            END TRY
            BEGIN CATCH
                DECLARE @ErrorNumber INT = ERROR_NUMBER();
                DECLARE @ErrorMessage NVARCHAR(4000) = LEFT(ERROR_MESSAGE(), 4000);
                SET @TickHadFailure = 1;
                SET @TickLastError = @ErrorMessage;
                DECLARE @NextAttemptCount INT;
                SELECT @NextAttemptCount = AttemptCount + 1
                FROM dbo.InventorySnapshot_RebuildQueue
                WHERE ID = @QueueId
                  AND LifecycleStatus = N'PROCESSING'
                  AND Claimed_Version = @Claimed_Version
                  AND Requested_Version = @Claimed_Version;

                DECLARE @IsTransient BIT = CASE WHEN @ErrorNumber IN (1205, 1222, 51224, 51226, 51305) THEN 1 ELSE 0 END;
                DECLARE @HasCurrentClaim BIT = CASE WHEN @NextAttemptCount IS NULL THEN 0 ELSE 1 END;
                DECLARE @IsFinal BIT = CASE WHEN @HasCurrentClaim = 1 AND (@IsTransient = 0 OR @NextAttemptCount >= @Max_Retry_Count) THEN 1 ELSE 0 END;
                DECLARE @FailureAt DATETIME2 = SYSUTCDATETIME();
                DECLARE @FailureUpdateCount INT = 0;

                IF @HasCurrentClaim = 1
                BEGIN
                    UPDATE dbo.InventorySnapshot_RebuildQueue
                       SET AttemptCount = @NextAttemptCount,
                           LastAttemptAt = @FailureAt,
                           LastError = @ErrorMessage,
                           ErrorMessage = @ErrorMessage,
                           LifecycleStatus = CASE WHEN @IsFinal = 1 THEN N'FAILED_FINAL' ELSE N'RETRY_WAITING' END,
                           Status = CASE WHEN @IsFinal = 1 THEN N'FAILED' ELSE N'WAITING' END,
                           NextAttemptAt = CASE WHEN @IsFinal = 1 THEN NULL
                                                ELSE DATEADD(MINUTE, CASE @NextAttemptCount WHEN 1 THEN 1 WHEN 2 THEN 5 WHEN 3 THEN 15 ELSE 60 END, @FailureAt) END,
                           LeaseUntil = NULL,
                           ClaimedBy = NULL,
                           ClaimedAt = NULL,
                           CompletedAt = CASE WHEN @IsFinal = 1 THEN @FailureAt ELSE NULL END
                     WHERE ID = @QueueId
                       AND LifecycleStatus = N'PROCESSING'
                       AND Claimed_Version = @Claimed_Version
                       AND Requested_Version = @Claimed_Version;
                    SET @FailureUpdateCount = @@ROWCOUNT;
                END

                /* If a newer invalidation arrived while the old build was
                   failing, preserve that newer generation as pending work and
                   do not create a dead-letter record for the obsolete claim. */
                IF @FailureUpdateCount = 0
                BEGIN
                    UPDATE dbo.InventorySnapshot_RebuildQueue
                       SET AttemptCount = 0,
                           LastAttemptAt = @FailureAt,
                           LastError = NULL,
                           ErrorMessage = NULL,
                           LifecycleStatus = N'WAITING',
                           Status = N'WAITING',
                           NextAttemptAt = NULL,
                           LeaseUntil = NULL,
                           ClaimedBy = NULL,
                           ClaimedAt = NULL,
                           CompletedAt = NULL
                     WHERE ID = @QueueId
                       AND LifecycleStatus = N'PROCESSING'
                       AND Claimed_Version = @Claimed_Version
                       AND Requested_Version <> @Claimed_Version;
                    SET @FailureUpdateCount = @@ROWCOUNT;
                END

                IF @IsFinal = 1 AND @FailureUpdateCount = 1
                    INSERT dbo.InventorySnapshot_RebuildDeadLetter
                    (
                        Queue_ID, Kho_ID, San_Pham_ID, From_Date, RequestType,
                        AttemptCount, LastError, FailedAt
                    )
                    SELECT q.ID, q.Kho_ID, q.San_Pham_ID, q.From_Date, q.RequestType,
                           q.AttemptCount, q.LastError, @FailureAt
                    FROM dbo.InventorySnapshot_RebuildQueue q
                    WHERE q.ID = @QueueId
                      AND q.LifecycleStatus = N'FAILED_FINAL'
                      AND q.Requested_Version = @Claimed_Version
                      AND NOT EXISTS (SELECT 1 FROM dbo.InventorySnapshot_RebuildDeadLetter d WHERE d.Queue_ID = q.ID);

                EXEC dbo.sp_Inventory_Snapshot_Record_Heartbeat
                    @Worker_Name = @Worker_Name,
                    @Queue_ID = @QueueId,
                    @Succeeded = 0,
                    @LastError = @ErrorMessage;
            END CATCH;

            SET @Processed += 1;
        END

        DECLARE @TickSucceeded BIT = CASE WHEN @TickHadFailure = 1 THEN 0 ELSE 1 END;
        EXEC dbo.sp_Inventory_Snapshot_Record_Heartbeat
            @Worker_Name = @Worker_Name,
            @Succeeded = @TickSucceeded,
            @LastError = @TickLastError;

        EXEC sys.sp_releaseapplock
            @Resource = N'InventorySnapshotRebuildWorker',
            @LockOwner = N'Session';
    END TRY
    BEGIN CATCH
        EXEC sys.sp_releaseapplock
            @Resource = N'InventorySnapshotRebuildWorker',
            @LockOwner = N'Session';
        THROW;
    END CATCH
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Receipt_Delete
ON dbo.tbl_XNK_Nhap_Kho
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Affected dbo.InventorySnapshotAffectedType;
    INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT d.Kho_ID, r.San_Pham_ID, d.Ngay_Nhap_Kho, N'DOCUMENT_DELETE'
    FROM deleted d
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data r ON r.Nhap_Kho_ID = d.Auto_ID
    WHERE d.Is_Posted = 1;
    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Issue_Delete
ON dbo.tbl_XNK_Xuat_Kho
AFTER DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Affected dbo.InventorySnapshotAffectedType;
    INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT d.Kho_ID, r.San_Pham_ID, d.Ngay_Xuat_Kho, N'DOCUMENT_DELETE'
    FROM deleted d
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data r ON r.Xuat_Kho_ID = d.Auto_ID
    WHERE d.Is_Posted = 1;
    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Receipt_Detail
ON dbo.tbl_XNK_Nhap_Kho_Raw_Data
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Affected dbo.InventorySnapshotAffectedType;

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Nhap_Kho, N'DOCUMENT_UPDATE'
        FROM inserted x JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = x.Nhap_Kho_ID
        WHERE h.Is_Posted = 1
        UNION
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Nhap_Kho, N'DOCUMENT_UPDATE'
        FROM deleted x JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = x.Nhap_Kho_ID
        WHERE h.Is_Posted = 1;
    END
    ELSE IF EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Nhap_Kho, N'DOCUMENT_DELETE'
        FROM deleted x JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = x.Nhap_Kho_ID
        WHERE h.Is_Posted = 1;
    END
    ELSE
    BEGIN
        INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Nhap_Kho, N'DOCUMENT_UPDATE'
        FROM inserted x JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = x.Nhap_Kho_ID
        WHERE h.Is_Posted = 1;
    END

    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Issue_Detail
ON dbo.tbl_XNK_Xuat_Kho_Raw_Data
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Affected dbo.InventorySnapshotAffectedType;

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Xuat_Kho, N'DOCUMENT_UPDATE'
        FROM inserted x JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = x.Xuat_Kho_ID
        WHERE h.Is_Posted = 1
        UNION
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Xuat_Kho, N'DOCUMENT_UPDATE'
        FROM deleted x JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = x.Xuat_Kho_ID
        WHERE h.Is_Posted = 1;
    END
    ELSE IF EXISTS (SELECT 1 FROM deleted)
    BEGIN
        INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Xuat_Kho, N'DOCUMENT_DELETE'
        FROM deleted x JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = x.Xuat_Kho_ID
        WHERE h.Is_Posted = 1;
    END
    ELSE
    BEGIN
        INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
        SELECT DISTINCT h.Kho_ID, x.San_Pham_ID, h.Ngay_Xuat_Kho, N'DOCUMENT_UPDATE'
        FROM inserted x JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = x.Xuat_Kho_ID
        WHERE h.Is_Posted = 1;
    END

    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
GO

/* Draft issue reservation procedures.
   CurrentQuantity is posted/on-hand stock. ReservedQuantity is held by active
   draft issue details; AvailableQuantity is derived as CurrentQuantity - ReservedQuantity. */

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Adjust
    @Kho_ID BIGINT, @San_Pham_ID BIGINT, @Delta DECIMAL(18,3)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Delta = 0 RETURN;

    DECLARE @CurrentQuantity DECIMAL(18,3), @ReservedQuantity DECIMAL(18,3);
    SELECT @CurrentQuantity = CurrentQuantity, @ReservedQuantity = ReservedQuantity
    FROM dbo.InventoryBalance_Current WITH (UPDLOCK, HOLDLOCK)
    WHERE Kho_ID = @Kho_ID AND San_Pham_ID = @San_Pham_ID;

    IF @CurrentQuantity IS NULL OR @Delta > 0 AND @CurrentQuantity - @ReservedQuantity < @Delta
        THROW 51140, N'Tồn khả dụng không đủ để giữ cho phiếu xuất nháp.', 1;
    IF @Delta < 0 AND @ReservedQuantity < -@Delta
        THROW 51141, N'Dữ liệu giữ chỗ tồn kho không hợp lệ.', 1;

    UPDATE dbo.InventoryBalance_Current
    SET ReservedQuantity = ReservedQuantity + @Delta, UpdatedAt = SYSUTCDATETIME()
    WHERE Kho_ID = @Kho_ID AND San_Pham_ID = @San_Pham_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Move_Document
    @Xuat_Kho_ID BIGINT, @Old_Kho_ID BIGINT, @New_Kho_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Old_Kho_ID = @New_Kho_ID RETURN;

    DECLARE @San_Pham_ID BIGINT, @ReservedQuantity DECIMAL(18,3), @Delta DECIMAL(18,3);
    DECLARE reservation_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT r.San_Pham_ID, SUM(r.ReservedQuantity)
        FROM dbo.InventoryReservation_Current r
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
        WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID AND r.Kho_ID = @Old_Kho_ID
        GROUP BY r.San_Pham_ID;

    OPEN reservation_cursor;
    FETCH NEXT FROM reservation_cursor INTO @San_Pham_ID, @ReservedQuantity;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Delta = -@ReservedQuantity;
        EXEC dbo.sp_XNK_Reservation_Adjust @Old_Kho_ID, @San_Pham_ID, @Delta;
        EXEC dbo.sp_XNK_Reservation_Adjust @New_Kho_ID, @San_Pham_ID, @ReservedQuantity;
        UPDATE r SET Kho_ID = @New_Kho_ID, UpdatedAt = SYSUTCDATETIME()
        FROM dbo.InventoryReservation_Current r
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
        WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID AND r.Kho_ID = @Old_Kho_ID AND r.San_Pham_ID = @San_Pham_ID;
        FETCH NEXT FROM reservation_cursor INTO @San_Pham_ID, @ReservedQuantity;
    END
    CLOSE reservation_cursor;
    DEALLOCATE reservation_cursor;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Release_Detail
    @Xuat_Kho_Detail_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT, @San_Pham_ID BIGINT, @ReservedQuantity DECIMAL(18,3), @Delta DECIMAL(18,3);
    SELECT @Kho_ID = Kho_ID, @San_Pham_ID = San_Pham_ID, @ReservedQuantity = ReservedQuantity
    FROM dbo.InventoryReservation_Current WITH (UPDLOCK, HOLDLOCK)
    WHERE Xuat_Kho_Detail_ID = @Xuat_Kho_Detail_ID;
    IF @ReservedQuantity IS NULL RETURN;

    SET @Delta = -@ReservedQuantity;
    EXEC dbo.sp_XNK_Reservation_Adjust @Kho_ID, @San_Pham_ID, @Delta;
    DELETE dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @Xuat_Kho_Detail_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Release_Document
    @Xuat_Kho_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @San_Pham_ID BIGINT, @ReservedQuantity DECIMAL(18,3), @Delta DECIMAL(18,3);
    DECLARE reservation_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT r.San_Pham_ID, SUM(r.ReservedQuantity)
        FROM dbo.InventoryReservation_Current r
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
        WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID
        GROUP BY r.San_Pham_ID;

    OPEN reservation_cursor;
    FETCH NEXT FROM reservation_cursor INTO @San_Pham_ID, @ReservedQuantity;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SELECT TOP (1) @Delta = -@ReservedQuantity;
        DECLARE @Kho_ID BIGINT = (SELECT TOP (1) r.Kho_ID
                                  FROM dbo.InventoryReservation_Current r
                                  JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
                                  WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID AND r.San_Pham_ID = @San_Pham_ID);
        EXEC dbo.sp_XNK_Reservation_Adjust @Kho_ID, @San_Pham_ID, @Delta;
        DELETE r
        FROM dbo.InventoryReservation_Current r
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Auto_ID = r.Xuat_Kho_Detail_ID
        WHERE d.Xuat_Kho_ID = @Xuat_Kho_ID AND r.San_Pham_ID = @San_Pham_ID;
        FETCH NEXT FROM reservation_cursor INTO @San_Pham_ID, @ReservedQuantity;
    END
    CLOSE reservation_cursor;
    DEALLOCATE reservation_cursor;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Reservation_Rebuild
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    BEGIN TRY
        DELETE FROM dbo.InventoryReservation_Current;
        UPDATE dbo.InventoryBalance_Current SET ReservedQuantity = 0, UpdatedAt = SYSUTCDATETIME();
        INSERT dbo.InventoryReservation_Current(Xuat_Kho_Detail_ID, Kho_ID, San_Pham_ID, ReservedQuantity)
        SELECT d.Auto_ID, h.Kho_ID, d.San_Pham_ID, d.SL_Xuat
        FROM dbo.tbl_XNK_Xuat_Kho h
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        WHERE h.Is_Posted = 0;

        IF EXISTS
        (
            SELECT 1
            FROM (SELECT Kho_ID, San_Pham_ID, SUM(ReservedQuantity) AS ReservedQuantity
                  FROM dbo.InventoryReservation_Current GROUP BY Kho_ID, San_Pham_ID) r
            LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK)
                ON b.Kho_ID = r.Kho_ID AND b.San_Pham_ID = r.San_Pham_ID
            WHERE b.CurrentQuantity IS NULL OR b.CurrentQuantity < r.ReservedQuantity
        ) THROW 51140, N'Tồn khả dụng không đủ cho các phiếu xuất nháp hiện có.', 1;

        UPDATE b SET ReservedQuantity = r.ReservedQuantity, UpdatedAt = SYSUTCDATETIME()
        FROM dbo.InventoryBalance_Current b
        JOIN (SELECT Kho_ID, San_Pham_ID, SUM(ReservedQuantity) AS ReservedQuantity
              FROM dbo.InventoryReservation_Current GROUP BY Kho_ID, San_Pham_ID) r
          ON r.Kho_ID = b.Kho_ID AND r.San_Pham_ID = b.San_Pham_ID;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* The current balance is a materialized projection, so its generation must
   advance in the same transaction as every insert/update/delete.  This
   trigger covers Post, reservation writers, rebuilds, and any future writer
   without relying on a caller-controlled marker. */
CREATE OR ALTER TRIGGER dbo.tr_Inventory_Current_Report_Generation
ON dbo.InventoryBalance_Current
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    IF EXISTS
    (
        SELECT 1
        FROM dbo.Inventory_Current_Report_State
        WHERE State_ID = 1 AND Generation = 9223372036854775807
    )
        THROW 51325, N'Generation báo cáo tồn kho hiện tại đã đạt giới hạn.', 1;

    UPDATE dbo.Inventory_Current_Report_State
    SET Generation = Generation + 1,
        UpdatedAt = SYSUTCDATETIME()
    WHERE State_ID = 1;

    IF @@ROWCOUNT <> 1
        THROW 51325, N'Generation báo cáo tồn kho hiện tại chưa được khởi tạo.', 1;
END
GO

/* Posted ledger data is immutable at the application database boundary.
   Draft CRUD remains unchanged.  The canonical Post procedure only changes a
   header from Draft to Posted; it does not need a detail-row bypass.  Database
   owners/sysadmins remain an explicit privileged DBA exception because they
   can intentionally override application permissions. */
CREATE OR ALTER TRIGGER dbo.tr_Inventory_Movement_Guard_Receipt_Posted_Header
ON dbo.tbl_XNK_Nhap_Kho
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF IS_SRVROLEMEMBER(N'sysadmin') = 1 OR IS_MEMBER(N'db_owner') = 1 RETURN;
    IF EXISTS (SELECT 1 FROM deleted WHERE Is_Posted = 1)
       OR EXISTS
       (
           SELECT 1
           FROM inserted i
           LEFT JOIN deleted d ON d.Auto_ID = i.Auto_ID
           WHERE i.Is_Posted = 1
             AND (d.Auto_ID IS NULL OR d.Is_Posted = 1)
       )
        THROW 51228, N'Không được INSERT, UPDATE hoặc DELETE phiếu nhập đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Movement_Guard_Issue_Posted_Header
ON dbo.tbl_XNK_Xuat_Kho
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF IS_SRVROLEMEMBER(N'sysadmin') = 1 OR IS_MEMBER(N'db_owner') = 1 RETURN;
    IF EXISTS (SELECT 1 FROM deleted WHERE Is_Posted = 1)
       OR EXISTS
       (
           SELECT 1
           FROM inserted i
           LEFT JOIN deleted d ON d.Auto_ID = i.Auto_ID
           WHERE i.Is_Posted = 1
             AND (d.Auto_ID IS NULL OR d.Is_Posted = 1)
       )
        THROW 51228, N'Không được INSERT, UPDATE hoặc DELETE phiếu xuất đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Movement_Guard_Receipt_Posted_Detail
ON dbo.tbl_XNK_Nhap_Kho_Raw_Data
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF IS_SRVROLEMEMBER(N'sysadmin') = 1 OR IS_MEMBER(N'db_owner') = 1 RETURN;
    IF EXISTS
    (
        SELECT 1
        FROM inserted d
        JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID
        WHERE h.Is_Posted = 1
    )
       OR EXISTS
    (
        SELECT 1
        FROM deleted d
        JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID
        WHERE h.Is_Posted = 1
    )
        THROW 51228, N'Không được INSERT, UPDATE hoặc DELETE chi tiết phiếu nhập đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Movement_Guard_Issue_Posted_Detail
ON dbo.tbl_XNK_Xuat_Kho_Raw_Data
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF IS_SRVROLEMEMBER(N'sysadmin') = 1 OR IS_MEMBER(N'db_owner') = 1 RETURN;
    IF EXISTS
    (
        SELECT 1
        FROM inserted d
        JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID
        WHERE h.Is_Posted = 1
    )
       OR EXISTS
    (
        SELECT 1
        FROM deleted d
        JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID
        WHERE h.Is_Posted = 1
    )
         THROW 51228, N'Không được INSERT, UPDATE hoặc DELETE chi tiết phiếu xuất đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
GO

/* Snapshot lifecycle procedures are defined once in this canonical deployment
   bundle. Keep future changes in this definition instead of appending overrides. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Create_Daily
    @Snapshot_Date DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @Snapshot_Date IS NULL
        THROW 51300, N'Ngày snapshot không được để trống.', 1;

    /* Preserve the legacy public entrypoint while enforcing the production
       rule: historical snapshots are finalised from Balance_Daily, never
       from the present-day Current projection. */
    EXEC dbo.sp_Inventory_Snapshot_Finalize_Daily
        @Snapshot_Date = @Snapshot_Date,
        @Worker_Name = N'Legacy:sp_Inventory_Snapshot_Create_Daily';
END
GO

/* Reconciliation is observational only.  It writes the comparison evidence
   but never updates a ledger or projection. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Reconciliation_Run
    @As_Of_Date DATE = NULL,
    @Kho_ID BIGINT = NULL,
    @San_Pham_ID BIGINT = NULL,
    @Run_ID BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* NULL requests current-state reconciliation.  A supplied date is a
       historical cutoff and must use the historical Daily projection. */
    DECLARE @Is_Current_Mode BIT = CASE WHEN @As_Of_Date IS NULL THEN 1 ELSE 0 END;
    DECLARE @Effective_As_Of_Date DATE = COALESCE(@As_Of_Date, CONVERT(DATE, SYSDATETIME()));

    INSERT dbo.InventoryReconciliation_Run(As_Of_Date, Kho_ID, San_Pham_ID, Status, StartedAt)
    VALUES (@Effective_As_Of_Date, @Kho_ID, @San_Pham_ID, N'RUNNING', SYSUTCDATETIME());
    SET @Run_ID = SCOPE_IDENTITY();

    BEGIN TRY
        CREATE TABLE #LedgerDaily
        (
            Movement_Date DATE NOT NULL,
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            TotalReceipt DECIMAL(18,3) NOT NULL,
            TotalIssue DECIMAL(18,3) NOT NULL,
            PRIMARY KEY (Movement_Date, Kho_ID, San_Pham_ID)
        );

        INSERT #LedgerDaily(Movement_Date, Kho_ID, San_Pham_ID, TotalReceipt, TotalIssue)
        SELECT movement.Movement_Date,
               movement.Kho_ID,
               movement.San_Pham_ID,
               CAST(SUM(movement.Receipt) AS DECIMAL(18,3)),
               CAST(SUM(movement.Issue) AS DECIMAL(18,3))
        FROM
        (
            SELECT h.Ngay_Nhap_Kho AS Movement_Date,
                   h.Kho_ID,
                   d.San_Pham_ID,
                   CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Receipt,
                   CAST(0 AS DECIMAL(18,3)) AS Issue
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
              AND h.Ngay_Nhap_Kho <= @Effective_As_Of_Date
              AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
              AND (@San_Pham_ID IS NULL OR d.San_Pham_ID = @San_Pham_ID)
            UNION ALL
            SELECT h.Ngay_Xuat_Kho,
                   h.Kho_ID,
                   d.San_Pham_ID,
                   CAST(0 AS DECIMAL(18,3)),
                   CAST(d.SL_Xuat AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
              AND h.Ngay_Xuat_Kho <= @Effective_As_Of_Date
              AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
              AND (@San_Pham_ID IS NULL OR d.San_Pham_ID = @San_Pham_ID)
        ) movement
        GROUP BY movement.Movement_Date, movement.Kho_ID, movement.San_Pham_ID;

        CREATE TABLE #CurrentClosing
        (
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            ClosingQuantity DECIMAL(18,3) NOT NULL,
            PRIMARY KEY (Kho_ID, San_Pham_ID)
        );

        IF @Is_Current_Mode = 1
        BEGIN
            INSERT #CurrentClosing(Kho_ID, San_Pham_ID, ClosingQuantity)
            SELECT movement.Kho_ID,
                   movement.San_Pham_ID,
                   CAST(SUM(movement.Delta) AS DECIMAL(18,3))
            FROM
            (
                SELECT h.Kho_ID,
                       d.San_Pham_ID,
                       CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Delta
                FROM dbo.tbl_XNK_Nhap_Kho h
                JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
                WHERE h.Is_Posted = 1
                  AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
                  AND (@San_Pham_ID IS NULL OR d.San_Pham_ID = @San_Pham_ID)
                UNION ALL
                SELECT h.Kho_ID,
                       d.San_Pham_ID,
                       CAST(-d.SL_Xuat AS DECIMAL(18,3))
                FROM dbo.tbl_XNK_Xuat_Kho h
                JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
                WHERE h.Is_Posted = 1
                  AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
                  AND (@San_Pham_ID IS NULL OR d.San_Pham_ID = @San_Pham_ID)
            ) movement
            GROUP BY movement.Kho_ID, movement.San_Pham_ID;
        END

        CREATE TABLE #Scope
        (
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            PRIMARY KEY (Kho_ID, San_Pham_ID)
        );

        INSERT #Scope(Kho_ID, San_Pham_ID)
        SELECT Kho_ID, San_Pham_ID FROM #LedgerDaily
        UNION
        SELECT Kho_ID, San_Pham_ID
        FROM dbo.InventoryBalance_Current
        WHERE (@Kho_ID IS NULL OR Kho_ID = @Kho_ID)
          AND (@San_Pham_ID IS NULL OR San_Pham_ID = @San_Pham_ID)
        UNION
        /* Current mode's authoritative ledger is all Posted history,
           including future business-date documents.  A future-only ledger
           scope still must be compared with a missing Current row. */
        SELECT Kho_ID, San_Pham_ID
        FROM #CurrentClosing
        WHERE @Is_Current_Mode = 1
        UNION
        SELECT Kho_ID, San_Pham_ID
        FROM dbo.Inventory_Movement_Daily
        WHERE Movement_Date <= @Effective_As_Of_Date
          AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID)
          AND (@San_Pham_ID IS NULL OR San_Pham_ID = @San_Pham_ID)
        UNION
        SELECT Kho_ID, San_Pham_ID
        FROM dbo.Inventory_Balance_Daily
        WHERE Balance_Date <= @Effective_As_Of_Date
          AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID)
          AND (@San_Pham_ID IS NULL OR San_Pham_ID = @San_Pham_ID)
        UNION
        SELECT Kho_ID, San_Pham_ID
        FROM dbo.InventoryBalance_Snapshot_Daily
        WHERE Snapshot_Date <= @Effective_As_Of_Date
          AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID)
          AND (@San_Pham_ID IS NULL OR San_Pham_ID = @San_Pham_ID);

        IF @Is_Current_Mode = 0 AND EXISTS
        (
            SELECT 1
            FROM #Scope scope
            OUTER APPLY
            (
                SELECT TOP (1) b.Balance_Date
                FROM dbo.Inventory_Balance_Daily b
                WHERE b.Kho_ID = scope.Kho_ID
                  AND b.San_Pham_ID = scope.San_Pham_ID
                  AND b.Balance_Date <= @Effective_As_Of_Date
                  AND b.IsValid = 1
                ORDER BY b.Balance_Date DESC
            ) ready
            WHERE ready.Balance_Date IS NULL
              AND NOT EXISTS
              (
                  /* An initialized projection may legitimately have no Daily
                     row before the scope's first movement.  That is a valid
                     zero-history cutoff, not a stale projection. */
                  SELECT 1
                  FROM dbo.InventoryBalance_Daily_AggregateState state
                  JOIN dbo.Inventory_Balance_Daily_Scope scopeMetadata
                    ON scopeMetadata.Kho_ID = scope.Kho_ID
                   AND scopeMetadata.San_Pham_ID = scope.San_Pham_ID
                   AND scopeMetadata.First_Balance_Date > @Effective_As_Of_Date
                  WHERE state.State_ID = 1
                    AND state.IsInitialized = 1
              )
        )
            THROW 51332, N'Inventory_Balance_Daily chưa sẵn sàng cho cutoff lịch sử được yêu cầu.', 1;

        IF @Is_Current_Mode = 1
        BEGIN
            /* Current mode compares all Posted Ledger movements with Current. */
            INSERT dbo.InventoryReconciliation_Result
            (
                Run_ID, Kho_ID, San_Pham_ID, Check_Date, Check_Type,
                ExpectedQuantity, ActualQuantity, Difference, Severity, Status, Details
            )
            SELECT @Run_ID,
                   scope.Kho_ID,
                   scope.San_Pham_ID,
                   @Effective_As_Of_Date,
                   N'CURRENT_CLOSING',
                   COALESCE(expected.ClosingQuantity, 0),
                   COALESCE(currentBalance.CurrentQuantity, 0),
                   COALESCE(expected.ClosingQuantity, 0) - COALESCE(currentBalance.CurrentQuantity, 0),
                   CASE WHEN COALESCE(expected.ClosingQuantity, 0) = COALESCE(currentBalance.CurrentQuantity, 0) THEN N'INFO' ELSE N'CRITICAL' END,
                   CASE WHEN COALESCE(expected.ClosingQuantity, 0) = COALESCE(currentBalance.CurrentQuantity, 0) THEN N'PASS' ELSE N'FAIL' END,
                   N'All Posted Ledger movements vs InventoryBalance_Current.'
            FROM #Scope scope
            LEFT JOIN #CurrentClosing expected
              ON expected.Kho_ID = scope.Kho_ID
             AND expected.San_Pham_ID = scope.San_Pham_ID
            LEFT JOIN dbo.InventoryBalance_Current currentBalance
              ON currentBalance.Kho_ID = scope.Kho_ID
             AND currentBalance.San_Pham_ID = scope.San_Pham_ID;
        END
        ELSE
        BEGIN
            /* Historical mode compares the requested ledger cutoff with the
               latest valid Daily row at or before that same cutoff. */
            INSERT dbo.InventoryReconciliation_Result
            (
                Run_ID, Kho_ID, San_Pham_ID, Check_Date, Check_Type,
                ExpectedQuantity, ActualQuantity, Difference, Severity, Status, Details
            )
            SELECT @Run_ID,
                   scope.Kho_ID,
                   scope.San_Pham_ID,
                   @Effective_As_Of_Date,
                    N'DAILY_CLOSING',
                    expected.ClosingQuantity,
                    COALESCE(actual.ClosingQuantity, 0),
                    expected.ClosingQuantity - COALESCE(actual.ClosingQuantity, 0),
                    CASE WHEN expected.ClosingQuantity = COALESCE(actual.ClosingQuantity, 0) THEN N'INFO' ELSE N'CRITICAL' END,
                    CASE WHEN expected.ClosingQuantity = COALESCE(actual.ClosingQuantity, 0) THEN N'PASS' ELSE N'FAIL' END,
                   N'Posted Ledger <= As_Of_Date vs latest valid Inventory_Balance_Daily <= As_Of_Date.'
            FROM #Scope scope
            OUTER APPLY
            (
                SELECT CAST(COALESCE(SUM(d.TotalReceipt - d.TotalIssue), 0) AS DECIMAL(18,3)) AS ClosingQuantity
                FROM #LedgerDaily d
                WHERE d.Kho_ID = scope.Kho_ID AND d.San_Pham_ID = scope.San_Pham_ID
            ) expected
            OUTER APPLY
            (
                SELECT TOP (1) CAST(b.ClosingQuantity AS DECIMAL(18,3)) AS ClosingQuantity
                FROM dbo.Inventory_Balance_Daily b
                WHERE b.Kho_ID = scope.Kho_ID
                  AND b.San_Pham_ID = scope.San_Pham_ID
                  AND b.Balance_Date <= @Effective_As_Of_Date
                  AND b.IsValid = 1
                ORDER BY b.Balance_Date DESC
            ) actual;
        END

        IF @Is_Current_Mode = 0
        BEGIN
        /* Materialized Movement Daily must be a faithful receipt/issue split
           of the posted ledger for every business date. */
        INSERT dbo.InventoryReconciliation_Result
        (
            Run_ID, Kho_ID, San_Pham_ID, Check_Date, Check_Type,
            ExpectedQuantity, ActualQuantity, Difference, Severity, Status, Details
        )
        SELECT @Run_ID,
               COALESCE(expected.Kho_ID, actual.Kho_ID),
               COALESCE(expected.San_Pham_ID, actual.San_Pham_ID),
               COALESCE(expected.Movement_Date, actual.Movement_Date),
               checkItem.Check_Type,
               checkItem.ExpectedQuantity,
               checkItem.ActualQuantity,
               checkItem.ExpectedQuantity - checkItem.ActualQuantity,
               CASE WHEN checkItem.ExpectedQuantity = checkItem.ActualQuantity THEN N'INFO' ELSE N'CRITICAL' END,
               CASE WHEN checkItem.ExpectedQuantity = checkItem.ActualQuantity THEN N'PASS' ELSE N'FAIL' END,
               N'Posted Ledger vs Inventory_Movement_Daily.'
        FROM #LedgerDaily expected
        FULL OUTER JOIN dbo.Inventory_Movement_Daily actual
          ON actual.Movement_Date = expected.Movement_Date
         AND actual.Kho_ID = expected.Kho_ID
         AND actual.San_Pham_ID = expected.San_Pham_ID
         AND actual.IsValid = 1
        CROSS APPLY
        (
            VALUES
            (N'MOVEMENT_RECEIPT', CAST(COALESCE(expected.TotalReceipt, 0) AS DECIMAL(18,3)), CAST(COALESCE(actual.Total_Receipt, 0) AS DECIMAL(18,3))),
            (N'MOVEMENT_ISSUE', CAST(COALESCE(expected.TotalIssue, 0) AS DECIMAL(18,3)), CAST(COALESCE(actual.Total_Issue, 0) AS DECIMAL(18,3)))
        ) checkItem(Check_Type, ExpectedQuantity, ActualQuantity)
        WHERE COALESCE(expected.Movement_Date, actual.Movement_Date) <= @Effective_As_Of_Date
          AND (@Kho_ID IS NULL OR COALESCE(expected.Kho_ID, actual.Kho_ID) = @Kho_ID)
          AND (@San_Pham_ID IS NULL OR COALESCE(expected.San_Pham_ID, actual.San_Pham_ID) = @San_Pham_ID);

        CREATE TABLE #DailyExpected
        (
            Balance_Date DATE NOT NULL,
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            OpeningQuantity DECIMAL(18,3) NOT NULL,
            TotalReceived DECIMAL(18,3) NOT NULL,
            TotalIssued DECIMAL(18,3) NOT NULL,
            ClosingQuantity DECIMAL(18,3) NOT NULL,
            PRIMARY KEY (Balance_Date, Kho_ID, San_Pham_ID)
        );

        INSERT #DailyExpected
        (
            Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity,
            TotalReceived, TotalIssued, ClosingQuantity
        )
        SELECT d.Movement_Date,
               d.Kho_ID,
               d.San_Pham_ID,
               CAST(COALESCE(SUM(d.TotalReceipt - d.TotalIssue) OVER
                    (PARTITION BY d.Kho_ID, d.San_Pham_ID ORDER BY d.Movement_Date ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS DECIMAL(18,3)),
               d.TotalReceipt,
               d.TotalIssue,
               CAST(SUM(d.TotalReceipt - d.TotalIssue) OVER
                    (PARTITION BY d.Kho_ID, d.San_Pham_ID ORDER BY d.Movement_Date ROWS UNBOUNDED PRECEDING) AS DECIMAL(18,3))
        FROM #LedgerDaily d;

        INSERT dbo.InventoryReconciliation_Result
        (
            Run_ID, Kho_ID, San_Pham_ID, Check_Date, Check_Type,
            ExpectedQuantity, ActualQuantity, Difference, Severity, Status, Details
        )
        SELECT @Run_ID,
               COALESCE(expected.Kho_ID, actual.Kho_ID),
               COALESCE(expected.San_Pham_ID, actual.San_Pham_ID),
               COALESCE(expected.Balance_Date, actual.Balance_Date),
               checkItem.Check_Type,
               checkItem.ExpectedQuantity,
               checkItem.ActualQuantity,
               checkItem.ExpectedQuantity - checkItem.ActualQuantity,
               CASE WHEN checkItem.ExpectedQuantity = checkItem.ActualQuantity THEN N'INFO' ELSE N'CRITICAL' END,
               CASE WHEN checkItem.ExpectedQuantity = checkItem.ActualQuantity THEN N'PASS' ELSE N'FAIL' END,
               N'Posted Ledger vs Inventory_Balance_Daily.'
        FROM #DailyExpected expected
        FULL OUTER JOIN dbo.Inventory_Balance_Daily actual
          ON actual.Balance_Date = expected.Balance_Date
         AND actual.Kho_ID = expected.Kho_ID
         AND actual.San_Pham_ID = expected.San_Pham_ID
         AND actual.IsValid = 1
        CROSS APPLY
        (
            VALUES
            (N'DAILY_OPENING', CAST(COALESCE(expected.OpeningQuantity, 0) AS DECIMAL(18,3)), CAST(COALESCE(actual.OpeningQuantity, 0) AS DECIMAL(18,3))),
            (N'DAILY_RECEIPT', CAST(COALESCE(expected.TotalReceived, 0) AS DECIMAL(18,3)), CAST(COALESCE(actual.TotalReceived, 0) AS DECIMAL(18,3))),
            (N'DAILY_ISSUE', CAST(COALESCE(expected.TotalIssued, 0) AS DECIMAL(18,3)), CAST(COALESCE(actual.TotalIssued, 0) AS DECIMAL(18,3))),
            (N'DAILY_CLOSING', CAST(COALESCE(expected.ClosingQuantity, 0) AS DECIMAL(18,3)), CAST(COALESCE(actual.ClosingQuantity, 0) AS DECIMAL(18,3)))
        ) checkItem(Check_Type, ExpectedQuantity, ActualQuantity)
        WHERE COALESCE(expected.Balance_Date, actual.Balance_Date) <= @Effective_As_Of_Date
          AND (@Kho_ID IS NULL OR COALESCE(expected.Kho_ID, actual.Kho_ID) = @Kho_ID)
          AND (@San_Pham_ID IS NULL OR COALESCE(expected.San_Pham_ID, actual.San_Pham_ID) = @San_Pham_ID);

        /* Snapshot is compared at the requested checkpoint.  Absence of a
           valid row is visible as actual zero, never repaired by this run. */
        INSERT dbo.InventoryReconciliation_Result
        (
            Run_ID, Kho_ID, San_Pham_ID, Check_Date, Check_Type,
            ExpectedQuantity, ActualQuantity, Difference, Severity, Status, Details
        )
        SELECT @Run_ID,
               scope.Kho_ID,
               scope.San_Pham_ID,
               @Effective_As_Of_Date,
               N'SNAPSHOT_CLOSING',
               expected.ClosingQuantity,
               COALESCE(snapshot.ClosingQuantity, 0),
               expected.ClosingQuantity - COALESCE(snapshot.ClosingQuantity, 0),
               CASE WHEN expected.ClosingQuantity = COALESCE(snapshot.ClosingQuantity, 0) THEN N'INFO' ELSE N'CRITICAL' END,
               CASE WHEN expected.ClosingQuantity = COALESCE(snapshot.ClosingQuantity, 0) THEN N'PASS' ELSE N'FAIL' END,
               N'Posted Ledger <= As_Of_Date vs valid InventoryBalance_Snapshot_Daily.'
        FROM #Scope scope
        OUTER APPLY
        (
            SELECT CAST(COALESCE(SUM(d.TotalReceipt - d.TotalIssue), 0) AS DECIMAL(18,3)) AS ClosingQuantity
            FROM #LedgerDaily d
            WHERE d.Kho_ID = scope.Kho_ID AND d.San_Pham_ID = scope.San_Pham_ID
        ) expected
        LEFT JOIN dbo.InventoryBalance_Snapshot_Daily snapshot
          ON snapshot.Snapshot_Date = @Effective_As_Of_Date
         AND snapshot.Kho_ID = scope.Kho_ID
         AND snapshot.San_Pham_ID = scope.San_Pham_ID
         AND snapshot.IsValid = 1;

        END

        UPDATE dbo.InventoryReconciliation_Run
           SET Status = N'COMPLETED',
               CompletedAt = SYSUTCDATETIME(),
               ErrorMessage = NULL
         WHERE ID = @Run_ID;
    END TRY
    BEGIN CATCH
        UPDATE dbo.InventoryReconciliation_Run
           SET Status = N'FAILED',
               CompletedAt = SYSUTCDATETIME(),
               ErrorMessage = LEFT(ERROR_MESSAGE(), 4000)
         WHERE ID = @Run_ID;
        THROW;
    END CATCH
END
GO

/* SQL Agent jobs call this procedure to expose backlog and stale-projection
   conditions in a queryable result set.  Throw_On_Critical is opt-in so the
   same procedure works for dashboards and alerting job steps. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Monitor
    @Snapshot_Backlog_Minutes INT = 60,
    @Processing_Lease_Seconds INT = 300,
    @Daily_Stale_Days INT = 1,
    @Throw_On_Critical BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF @Snapshot_Backlog_Minutes < 1 OR @Processing_Lease_Seconds < 1 OR @Daily_Stale_Days < 1
        THROW 51331, N'Ngưỡng monitoring snapshot phải lớn hơn 0.', 1;

    DECLARE @Now DATETIME2 = SYSUTCDATETIME();
    DECLARE @Today DATE = CONVERT(DATE, @Now);
    CREATE TABLE #Health
    (
        Check_Name NVARCHAR(80) NOT NULL,
        Severity NVARCHAR(20) NOT NULL,
        MetricValue BIGINT NOT NULL,
        Details NVARCHAR(1000) NOT NULL
    );

    INSERT #Health
    SELECT N'SNAPSHOT_BACKLOG', CASE WHEN COUNT_BIG(*) > 0 THEN N'CRITICAL' ELSE N'INFO' END, COUNT_BIG(*),
           N'Active snapshot queue rows older than configured backlog threshold.'
    FROM dbo.InventorySnapshot_RebuildQueue
    WHERE LifecycleStatus IN (N'WAITING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED')
      AND CreatedAt < DATEADD(MINUTE, -@Snapshot_Backlog_Minutes, @Now);

    INSERT #Health
    SELECT N'SNAPSHOT_FAILED_FINAL', CASE WHEN COUNT_BIG(*) > 0 THEN N'CRITICAL' ELSE N'INFO' END, COUNT_BIG(*),
           N'Snapshot queue rows that require dead-letter recovery.'
    FROM dbo.InventorySnapshot_RebuildQueue
    WHERE LifecycleStatus = N'FAILED_FINAL';

    INSERT #Health
    SELECT N'SNAPSHOT_LEASE_EXPIRED', CASE WHEN COUNT_BIG(*) > 0 THEN N'CRITICAL' ELSE N'INFO' END, COUNT_BIG(*),
           N'PROCESSING queue rows whose lease has expired.'
    FROM dbo.InventorySnapshot_RebuildQueue
    WHERE LifecycleStatus = N'PROCESSING' AND LeaseUntil < @Now;

    INSERT #Health
    SELECT N'SNAPSHOT_WORKER_STALE', CASE WHEN COUNT_BIG(*) > 0 THEN N'CRITICAL' ELSE N'INFO' END, COUNT_BIG(*),
           N'Repair worker heartbeat missing or older than the lease threshold.'
    FROM (SELECT 1 AS MissingHeartbeat WHERE NOT EXISTS
          (
              SELECT 1 FROM dbo.InventorySnapshot_WorkerHeartbeat
              WHERE Worker_Name = N'SQLAgent:InventorySnapshotRepair'
                AND LastHeartbeatAt >= DATEADD(SECOND, -@Processing_Lease_Seconds, @Now)
                AND (LastFailureAt IS NULL OR (LastSuccessAt IS NOT NULL AND LastSuccessAt >= LastFailureAt))
          )) stale;

    DECLARE @DailyProjectionIssueCount BIGINT =
        (SELECT CASE WHEN EXISTS
                    (
                        SELECT 1
                        FROM dbo.InventoryMovement_AggregateState
                        WHERE State_ID = 1 AND IsInitialized = 1
                    )
                    AND EXISTS
                    (
                        SELECT 1
                        FROM dbo.InventoryBalance_Daily_AggregateState
                        WHERE State_ID = 1 AND IsInitialized = 1
                    )
                    THEN 0 ELSE 1 END)
        + (SELECT COUNT_BIG(*)
           FROM dbo.InventoryMovement_RebuildQueue
           WHERE Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL'))
        + (SELECT COUNT_BIG(*)
           FROM dbo.InventoryMovement_RebuildDeadLetter
           WHERE ResolvedAt IS NULL);

    INSERT #Health
    SELECT N'DAILY_STALE', CASE WHEN @DailyProjectionIssueCount > 0 THEN N'CRITICAL' ELSE N'INFO' END,
           @DailyProjectionIssueCount,
           N'Daily readiness is based on aggregate initialization and pending/unresolved movement work; sparse dates alone are not stale.';

    INSERT #Health
    SELECT N'SNAPSHOT_NOT_FINALIZED', CASE WHEN COUNT_BIG(*) > 0 THEN N'CRITICAL' ELSE N'INFO' END, COUNT_BIG(*),
           N'Balance Daily scopes have no valid end-of-day snapshot on or after their latest valid balance date.'
    FROM
    (
        SELECT b.Kho_ID, b.San_Pham_ID, MAX(b.Balance_Date) AS Balance_Date
        FROM dbo.Inventory_Balance_Daily b
        WHERE b.IsValid = 1
        GROUP BY b.Kho_ID, b.San_Pham_ID
    ) latest
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.InventoryBalance_Snapshot_Daily snapshot
        WHERE snapshot.Kho_ID = latest.Kho_ID
          AND snapshot.San_Pham_ID = latest.San_Pham_ID
          AND snapshot.Snapshot_Date >= latest.Balance_Date
          AND snapshot.Snapshot_Date <= @Today
          AND snapshot.IsValid = 1
    );

    SELECT Check_Name, Severity, MetricValue, Details
    FROM #Health
    ORDER BY CASE Severity WHEN N'CRITICAL' THEN 0 ELSE 1 END, Check_Name;

    IF @Throw_On_Critical = 1 AND EXISTS (SELECT 1 FROM #Health WHERE Severity = N'CRITICAL')
        THROW 51332, N'INVENTORY_SNAPSHOT_MONITOR_CRITICAL: kiểm tra result set để biết metric lỗi.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Invalidate_From
    @From_Date DATE,
    @Kho_ID BIGINT = NULL,
    @San_Pham_ID BIGINT = NULL,
    @InvalidReason NVARCHAR(100) = N'BACK_DATE_POST'
AS
BEGIN
    SET NOCOUNT ON;
    IF @From_Date IS NULL
        THROW 51301, N'Ngày bắt đầu invalidate không được để trống.', 1;
    SET @InvalidReason = COALESCE(NULLIF(LTRIM(RTRIM(@InvalidReason)), N''), N'BACK_DATE_POST');

    DECLARE @Affected dbo.InventorySnapshotAffectedType;
    INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT DISTINCT Kho_ID, San_Pham_ID, @From_Date, @InvalidReason
    FROM dbo.InventoryBalance_Snapshot_Daily
    WHERE Snapshot_Date >= @From_Date
      AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID)
      AND (@San_Pham_ID IS NULL OR San_Pham_ID = @San_Pham_ID);

    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
GO

/* Draft/Post lifecycle procedures are defined once in this canonical bundle. */
CREATE OR ALTER PROCEDURE dbo.sp_XNK_Validate_All_Balances
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @HasNegative BIT=0;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Nhap_Kho AS MovementDate,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Delta FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
        UNION ALL
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Xuat_Kho,CAST(-d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
    ), Daily AS (SELECT Kho_ID,San_Pham_ID,MovementDate,SUM(Delta) AS Delta FROM Movements GROUP BY Kho_ID,San_Pham_ID,MovementDate),
    Running AS (SELECT SUM(Delta) OVER(PARTITION BY Kho_ID,San_Pham_ID ORDER BY MovementDate ROWS UNBOUNDED PRECEDING) AS Balance FROM Daily)
    SELECT @HasNegative=CASE WHEN MIN(Balance)<0 THEN 1 ELSE 0 END FROM Running;
    IF @HasNegative=1 THROW 51120,N'Không thể Post vì tồn kho sẽ âm tại một thời điểm trong lịch sử.',1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Delete @Entity NVARCHAR(30), @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Entity=N'DonViTinh' DELETE FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'LoaiSanPham' DELETE FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'SanPham' DELETE FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'NCC' DELETE FROM dbo.tbl_DM_NCC WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'Kho' DELETE FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Auto_ID;
    ELSE IF @Entity=N'KhoUser' DELETE FROM dbo.tbl_DM_Kho_User WHERE Auto_ID=@Auto_ID;
    ELSE THROW 51060,N'Loại danh mục không hợp lệ.',1;
END
GO

/*
   Canonical Warehouse read/write contract.
   These definitions intentionally stay in the existing DM/XNK/BC procedure
   groups so CSqlHelper callers do not carry business SQL.
*/
CREATE OR ALTER PROCEDURE dbo.sp_DM_Master_List @Entity NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Entity=N'DonViTinh'
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh;
    ELSE IF @Entity=N'LoaiSanPham'
        SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP;
    ELSE IF @Entity=N'SanPham'
        SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name, Loai_San_Pham_ID AS Related_ID, Don_Vi_Tinh_ID AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_San_Pham ORDER BY Ma_San_Pham;
    ELSE IF @Entity=N'NCC'
        SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_NCC ORDER BY Ma_NCC;
    ELSE IF @Entity=N'Kho'
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Kho ORDER BY Ten_Kho;
    ELSE IF @Entity=N'KhoUser'
        SELECT u.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, CAST(N'' AS NVARCHAR(255)) AS Name, u.Kho_ID AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, u.Ma_Dang_Nhap AS Login_Name, CAST(N'' AS NVARCHAR(1000)) AS Ghi_Chu FROM dbo.tbl_DM_Kho_User u ORDER BY u.Ma_Dang_Nhap, u.Kho_ID;
    ELSE THROW 51060, N'Loại danh mục không hợp lệ.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Lookup_List @Entity NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Entity=N'DonViTinh' SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name FROM dbo.tbl_DM_Don_Vi_Tinh ORDER BY Ten_Don_Vi_Tinh;
    ELSE IF @Entity=N'LoaiSanPham' SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name FROM dbo.tbl_DM_Loai_San_Pham ORDER BY Ma_LSP;
    ELSE IF @Entity=N'SanPham' SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name FROM dbo.tbl_DM_San_Pham ORDER BY Ma_San_Pham;
    ELSE IF @Entity=N'NCC' SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name FROM dbo.tbl_DM_NCC ORDER BY Ma_NCC;
    ELSE IF @Entity=N'Kho' SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name FROM dbo.tbl_DM_Kho ORDER BY Ten_Kho;
    ELSE THROW 51060, N'Loại danh mục không hợp lệ.', 1;
END
GO

/* Server-side paging variants of the canonical lists/reports. Each returns two result
   sets: Total_Count first, then the requested page (OFFSET/FETCH). Page_From_Procedure
   in CWarehouse_Controller_Base consumes them; TelerikGrid OnRead drives the page number. */
CREATE OR ALTER PROCEDURE dbo.sp_DM_Master_Page
    @Entity NVARCHAR(30), @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100)=N''
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number<1 SET @Page_Number=1;
    IF @Page_Size<1 SET @Page_Size=10;
    DECLARE @Filter NVARCHAR(260)=N'%' + @Search_Text + N'%';
    IF @Entity=N'DonViTinh'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter ORDER BY Ten_Don_Vi_Tinh OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'LoaiSanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter;
        SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter ORDER BY Ma_LSP OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'SanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter;
        SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name, Loai_San_Pham_ID AS Related_ID, Don_Vi_Tinh_ID AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter ORDER BY Ma_San_Pham OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'NCC'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter;
        SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter ORDER BY Ma_NCC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'Kho'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name, CAST(0 AS BIGINT) AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, CAST(N'' AS NVARCHAR(100)) AS Login_Name, Ghi_Chu FROM dbo.tbl_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter ORDER BY Ten_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'KhoUser'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Kho_User u WHERE @Search_Text=N'' OR u.Ma_Dang_Nhap LIKE @Filter;
        SELECT u.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, CAST(N'' AS NVARCHAR(255)) AS Name, u.Kho_ID AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2, u.Ma_Dang_Nhap AS Login_Name, CAST(N'' AS NVARCHAR(1000)) AS Ghi_Chu FROM dbo.tbl_DM_Kho_User u WHERE @Search_Text=N'' OR u.Ma_Dang_Nhap LIKE @Filter ORDER BY u.Ma_Dang_Nhap, u.Kho_ID OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE THROW 51060, N'Loại danh mục không hợp lệ.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Lookup_Page
    @Entity NVARCHAR(30), @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100)=N''
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number<1 SET @Page_Number=1;
    IF @Page_Size<1 SET @Page_Size=10;
    DECLARE @Filter NVARCHAR(260)=N'%' + @Search_Text + N'%';
    IF @Entity=N'DonViTinh'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Don_Vi_Tinh AS Name FROM dbo.tbl_DM_Don_Vi_Tinh WHERE @Search_Text=N'' OR Ten_Don_Vi_Tinh LIKE @Filter ORDER BY Ten_Don_Vi_Tinh OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'LoaiSanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter;
        SELECT Auto_ID, Ma_LSP AS Code, Ten_LSP AS Name FROM dbo.tbl_DM_Loai_San_Pham WHERE @Search_Text=N'' OR Ma_LSP LIKE @Filter OR Ten_LSP LIKE @Filter ORDER BY Ma_LSP OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'SanPham'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter;
        SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name FROM dbo.tbl_DM_San_Pham WHERE @Search_Text=N'' OR Ma_San_Pham LIKE @Filter OR Ten_San_Pham LIKE @Filter ORDER BY Ma_San_Pham OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'NCC'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter;
        SELECT Auto_ID, Ma_NCC AS Code, Ten_NCC AS Name FROM dbo.tbl_DM_NCC WHERE @Search_Text=N'' OR Ma_NCC LIKE @Filter OR Ten_NCC LIKE @Filter ORDER BY Ma_NCC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE IF @Entity=N'Kho'
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter;
        SELECT Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, Ten_Kho AS Name FROM dbo.tbl_DM_Kho WHERE @Search_Text=N'' OR Ten_Kho LIKE @Filter ORDER BY Ten_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE THROW 51060, N'Loại danh mục không hợp lệ.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Don_Vi_Tinh_Save
    @Auto_ID BIGINT OUTPUT, @Ten_Don_Vi_Tinh NVARCHAR(200), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ten_Don_Vi_Tinh=LTRIM(RTRIM(ISNULL(@Ten_Don_Vi_Tinh,N'')));
    IF @Ten_Don_Vi_Tinh=N'' THROW 51001,N'Tên đơn vị tính không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI=@Ten_Don_Vi_Tinh COLLATE Latin1_General_CI_AI AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51002,N'Tên đơn vị tính đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ten_Don_Vi_Tinh,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_Don_Vi_Tinh SET Ten_Don_Vi_Tinh=@Ten_Don_Vi_Tinh,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Loai_San_Pham_Save
    @Auto_ID BIGINT OUTPUT, @Ma_LSP NVARCHAR(100), @Ten_LSP NVARCHAR(200), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ma_LSP=LTRIM(RTRIM(ISNULL(@Ma_LSP,N''))); SET @Ten_LSP=LTRIM(RTRIM(ISNULL(@Ten_LSP,N'')));
    IF @Ma_LSP=N'' THROW 51010,N'Mã loại sản phẩm không được để trống.',1;
    IF @Ten_LSP=N'' THROW 51012,N'Tên loại sản phẩm không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE (Ma_LSP=@Ma_LSP OR Ten_LSP=@Ten_LSP) AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51011,N'Mã hoặc tên loại sản phẩm đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_Loai_San_Pham(Ma_LSP,Ten_LSP,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ma_LSP,@Ten_LSP,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_Loai_San_Pham SET Ma_LSP=@Ma_LSP,Ten_LSP=@Ten_LSP,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
END
GO

/* Warehouse authorization and report procedures are defined once in this
   canonical bundle. */

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Ngay_Nhap_Kho AS Ngay, h.So_Phieu_Nhap_Kho AS So_Phieu, n.Ten_NCC AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia, CAST(d.SL_Nhap * d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    SELECT h.Ngay_Xuat_Kho AS Ngay, h.So_Phieu_Xuat_Kho AS So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia, CAST(d.SL_Xuat * d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia
    FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay AND h.Is_Posted = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
    ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    DECLARE @Offset BIGINT = CONVERT(BIGINT, @Page_Number - 1) * CONVERT(BIGINT, @Page_Size);

    CREATE TABLE #AuthorizedWarehouse
    (
        Kho_ID BIGINT NOT NULL PRIMARY KEY
    );

    INSERT #AuthorizedWarehouse(Kho_ID)
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID);

    SELECT COUNT(*) AS Total_Count
    FROM dbo.tbl_XNK_Nhap_Kho h
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
    WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
      AND h.Is_Posted = 1;

    ;WITH PageScope AS
    (
        SELECT d.Auto_ID AS Detail_ID,
               h.Auto_ID AS Document_ID,
               h.Ngay_Nhap_Kho AS Ngay,
               h.So_Phieu_Nhap_Kho AS So_Phieu,
               h.NCC_ID,
               d.San_Pham_ID,
               d.SL_Nhap AS So_Luong,
               d.Don_Gia_Nhap AS Don_Gia
        FROM dbo.tbl_XNK_Nhap_Kho h
        JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
        JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
          AND h.Is_Posted = 1
        ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho, h.Auto_ID, d.Auto_ID
        OFFSET @Offset ROWS FETCH NEXT @Page_Size ROWS ONLY
    )
    SELECT ps.Ngay, ps.So_Phieu, n.Ten_NCC AS Nha_Cung_Cap,
           p.Ma_San_Pham, p.Ten_San_Pham, ps.So_Luong, ps.Don_Gia,
           CAST(ps.So_Luong * ps.Don_Gia AS DECIMAL(18,2)) AS Tri_Gia
    FROM PageScope ps
    JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = ps.NCC_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = ps.San_Pham_ID
    ORDER BY ps.Ngay, ps.So_Phieu, ps.Document_ID, ps.Detail_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1; IF @Page_Size < 1 SET @Page_Size = 10;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    DECLARE @Offset BIGINT = CONVERT(BIGINT, @Page_Number - 1) * CONVERT(BIGINT, @Page_Size);

    CREATE TABLE #AuthorizedWarehouse
    (
        Kho_ID BIGINT NOT NULL PRIMARY KEY
    );

    INSERT #AuthorizedWarehouse(Kho_ID)
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID);

    SELECT COUNT(*) AS Total_Count
    FROM dbo.tbl_XNK_Xuat_Kho h
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
    WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
      AND h.Is_Posted = 1;

    ;WITH PageScope AS
    (
        SELECT d.Auto_ID AS Detail_ID,
               h.Auto_ID AS Document_ID,
               h.Ngay_Xuat_Kho AS Ngay,
               h.So_Phieu_Xuat_Kho AS So_Phieu,
               d.San_Pham_ID,
               d.SL_Xuat AS So_Luong,
               d.Don_Gia_Xuat AS Don_Gia
        FROM dbo.tbl_XNK_Xuat_Kho h
        JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        WHERE h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay
          AND h.Is_Posted = 1
        ORDER BY h.Ngay_Xuat_Kho, h.So_Phieu_Xuat_Kho, h.Auto_ID, d.Auto_ID
        OFFSET @Offset ROWS FETCH NEXT @Page_Size ROWS ONLY
    )
    SELECT ps.Ngay, ps.So_Phieu, CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap,
           p.Ma_San_Pham, p.Ten_San_Pham, ps.So_Luong, ps.Don_Gia,
           CAST(ps.So_Luong * ps.Don_Gia AS DECIMAL(18,2)) AS Tri_Gia
    FROM PageScope ps
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = ps.San_Pham_ID
    ORDER BY ps.Ngay, ps.So_Phieu, ps.Document_ID, ps.Detail_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF;
    DECLARE @OwnTransaction BIT = 0, @SavepointCreated BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    ELSE IF XACT_STATE() = 1
    BEGIN
        SAVE TRANSACTION SaveReceiptHeader;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        SET @So_Phieu_Nhap_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho, N'')));
        IF @So_Phieu_Nhap_Kho = N'' THROW 51100, N'Số phiếu nhập không được để trống.', 1;
        IF @Ngay_Nhap_Kho IS NULL THROW 51104, N'Ngày nhập kho không được để trống.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID = @NCC_ID) THROW 51103, N'Nhà cung cấp không hợp lệ.', 1;

        DECLARE @Old_Kho_ID BIGINT, @Is_Posted BIT;
        IF ISNULL(@Auto_ID, 0) = 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho) THROW 51101, N'Số phiếu nhập đã tồn tại.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51102, N'Kho không hợp lệ.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
            INSERT dbo.tbl_XNK_Nhap_Kho
            (
                So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu,
                Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function
            )
            VALUES
            (
                @So_Phieu_Nhap_Kho, @Kho_ID, @NCC_ID, @Ngay_Nhap_Kho, 0, @Ghi_Chu,
                COALESCE(NULLIF(@Created_By, N''), NULLIF(@Last_Updated_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Created_By_Function, N''), NULLIF(@Last_Updated_By_Function, N'')),
                COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            );
        END
        ELSE
        BEGIN
            SELECT @Old_Kho_ID = Kho_ID, @Is_Posted = Is_Posted
            FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK)
            WHERE Auto_ID = @Auto_ID;
            IF @Old_Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
            IF @Is_Posted = 1 THROW 51163, N'Không được sửa phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51102, N'Kho không hợp lệ.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Old_Kho_ID;
            IF @Old_Kho_ID <> @Kho_ID EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho AND Auto_ID <> @Auto_ID) THROW 51101, N'Số phiếu nhập đã tồn tại.', 1;
            UPDATE dbo.tbl_XNK_Nhap_Kho
            SET So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho,
                Kho_ID = @Kho_ID,
                NCC_ID = @NCC_ID,
                Ngay_Nhap_Kho = @Ngay_Nhap_Kho,
                Ghi_Chu = @Ghi_Chu,
                Last_Updated = SYSUTCDATETIME(),
                Last_Updated_By = COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                Last_Updated_By_Function = COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            WHERE Auto_ID = @Auto_ID;
        END
        IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveReceiptHeader;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF;
    DECLARE @OwnTransaction BIT = 0, @SavepointCreated BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    ELSE IF XACT_STATE() = 1
    BEGIN
        SAVE TRANSACTION SaveReceiptDetail;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        DECLARE @Kho_ID BIGINT, @Is_Posted BIT;
        SELECT @Kho_ID = Kho_ID, @Is_Posted = Is_Posted
        FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK)
        WHERE Auto_ID = @Nhap_Kho_ID;
        IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Is_Posted = 1 THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51106, N'Sản phẩm không hợp lệ.', 1;
        IF @SL_Nhap <= 0 THROW 51107, N'Số lượng nhập phải lớn hơn 0.', 1;
        IF @Don_Gia_Nhap <= 0 THROW 51108, N'Đơn giá nhập phải lớn hơn 0.', 1;
        IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data
        (
            Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap,
            Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function
        )
        VALUES
        (
            @Nhap_Kho_ID, @San_Pham_ID, @SL_Nhap, @Don_Gia_Nhap,
            SYSUTCDATETIME(),
            COALESCE(NULLIF(@Created_By, N''), NULLIF(@Last_Updated_By, N''), @Ma_Dang_Nhap),
            COALESCE(NULLIF(@Created_By_Function, N''), NULLIF(@Last_Updated_By_Function, N'')),
            SYSUTCDATETIME(),
            COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
            COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
        );
        ELSE
        BEGIN
            DECLARE @Existing_Nhap_Kho_ID BIGINT, @Existing_San_Pham_ID BIGINT;
            SELECT @Existing_Nhap_Kho_ID = Nhap_Kho_ID, @Existing_San_Pham_ID = San_Pham_ID
            FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WITH (UPDLOCK, HOLDLOCK)
            WHERE Auto_ID = @Auto_ID;
            IF @Existing_Nhap_Kho_ID IS NULL THROW 51109, N'Chi tiết phiếu nhập không tồn tại.', 1;
            IF @Existing_Nhap_Kho_ID <> @Nhap_Kho_ID OR @Existing_San_Pham_ID <> @San_Pham_ID THROW 51110, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
            UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data
            SET SL_Nhap = @SL_Nhap,
                Don_Gia_Nhap = @Don_Gia_Nhap,
                Last_Updated = SYSUTCDATETIME(),
                Last_Updated_By = COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                Last_Updated_By_Function = COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            WHERE Auto_ID = @Auto_ID;
            IF @@ROWCOUNT <> 1 THROW 51109, N'Chi tiết phiếu nhập không tồn tại.', 1;
        END
        IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveReceiptDetail;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID);
    IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa phiếu đã Post.', 1;
    DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Ensure_Access
    @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    IF @Kho_ID IS NULL OR @Kho_ID = 0 OR NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51051, N'Kho không hợp lệ.', 1;
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.tbl_DM_Kho_User ku
        JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = ku.Ma_Dang_Nhap
        WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = @Kho_ID
    ) THROW 51054, N'User không có quyền thao tác trên kho này.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_List_Allowed
    @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' OR NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap) THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    SELECT k.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, k.Ten_Kho AS Name
    FROM dbo.tbl_DM_Kho_User ku JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = ku.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap ORDER BY k.Ten_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_User_List
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Auto_ID, Ma_Dang_Nhap AS Code, Ho_Ten AS Name FROM dbo.tbl_Sys_Thanh_Vien ORDER BY Ma_Dang_Nhap;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Post
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100),
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @BootstrapPostLockResult INT;
        EXEC @BootstrapPostLockResult = sys.sp_getapplock
            @Resource = N'InventoryMovement:Bootstrap',
            @LockMode = N'Shared',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @BootstrapPostLockResult < 0
            THROW 51226, N'Movement Aggregate đang bảo trì bootstrap. Không thể Post chứng từ lúc này.', 1;

        CREATE TABLE #Delta(Kho_ID BIGINT NOT NULL, San_Pham_ID BIGINT NOT NULL, Delta DECIMAL(18,3) NOT NULL, PRIMARY KEY(Kho_ID, San_Pham_ID));
        DECLARE @Movement_Date DATE;
        IF @Is_Receipt = 1
        BEGIN
            DECLARE @ReceiptWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            SELECT @Movement_Date = Ngay_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID;
            IF @ReceiptWarehouse IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @ReceiptWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(d.SL_Nhap AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
        END
        ELSE
        BEGIN
            DECLARE @IssueWarehouse BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Document_ID);
            SELECT @Movement_Date = Ngay_Xuat_Kho FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID;
            IF @IssueWarehouse IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @IssueWarehouse;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51162, N'Phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Document_ID) THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
            INSERT #Delta SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(-d.SL_Xuat AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Auto_ID = @Document_ID GROUP BY h.Kho_ID, d.San_Pham_ID;
        END
        /* Post is the exclusive side of the same projection fence used by
           Finalize/report reads and Daily rebuild. */
        DECLARE @PostScopeLockResult INT;
        DECLARE @PostScopeLockResource NVARCHAR(255);
        DECLARE @PostScopeKho_ID BIGINT, @PostScopeSan_Pham_ID BIGINT;
        DECLARE post_scope_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT Kho_ID, San_Pham_ID FROM #Delta ORDER BY Kho_ID, San_Pham_ID;
        OPEN post_scope_cursor;
        FETCH NEXT FROM post_scope_cursor INTO @PostScopeKho_ID, @PostScopeSan_Pham_ID;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @PostScopeLockResource = CONCAT(N'InventoryMovement:', @PostScopeKho_ID, N':', @PostScopeSan_Pham_ID);
            EXEC @PostScopeLockResult = sys.sp_getapplock
                @Resource = @PostScopeLockResource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 0;
            IF @PostScopeLockResult < 0
            BEGIN
                CLOSE post_scope_cursor;
                DEALLOCATE post_scope_cursor;
                THROW 51322, N'Projection scope đang được Finalize hoặc rebuild; Post phải chạy lại.', 1;
            END
            FETCH NEXT FROM post_scope_cursor INTO @PostScopeKho_ID, @PostScopeSan_Pham_ID;
        END
        CLOSE post_scope_cursor;
        DEALLOCATE post_scope_cursor;

        /* Scope catalog is the optimistic report boundary.  A new scope is
           recorded in this transaction before the Posted header/Current
           transition becomes visible.  Historical reports filter the catalog
           by @Den_Ngay, so a future-only Post does not invalidate them. */
        UPDATE catalog
        SET First_Posted_Date = CASE
                                    WHEN catalog.First_Posted_Date IS NULL OR @Movement_Date < catalog.First_Posted_Date
                                        THEN @Movement_Date
                                    ELSE catalog.First_Posted_Date
                                END,
            Last_Posted_Date = CASE
                                   WHEN catalog.Last_Posted_Date IS NULL OR @Movement_Date > catalog.Last_Posted_Date
                                       THEN @Movement_Date
                                   ELSE catalog.Last_Posted_Date
                               END,
            Is_Current = 1,
            [Version] = catalog.[Version] + 1,
            UpdatedAt = SYSUTCDATETIME()
        FROM dbo.Inventory_Report_Scope_Catalog catalog
        JOIN #Delta delta ON delta.Kho_ID = catalog.Kho_ID
                         AND delta.San_Pham_ID = catalog.San_Pham_ID;

        INSERT dbo.Inventory_Report_Scope_Catalog
        (Kho_ID, San_Pham_ID, First_Posted_Date, Last_Posted_Date, Is_Current)
        SELECT delta.Kho_ID, delta.San_Pham_ID, @Movement_Date, @Movement_Date, 1
        FROM #Delta delta
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.Inventory_Report_Scope_Catalog catalog WITH (UPDLOCK, HOLDLOCK)
            WHERE catalog.Kho_ID = delta.Kho_ID
              AND catalog.San_Pham_ID = delta.San_Pham_ID
        );

        /* The parent transition and reservation release follow the same
           scope fence.  This keeps Post, report reads and projection workers
           in one lock order: scope fence first, then document/projection
           rows. */
        IF @Is_Receipt = 1
        BEGIN
            UPDATE dbo.tbl_XNK_Nhap_Kho
            SET Is_Posted = 1,
                Posted_At = SYSUTCDATETIME(),
                Last_Updated = SYSUTCDATETIME(),
                Last_Updated_By = COALESCE(NULLIF(@Last_Updated_By, N''), @Ma_Dang_Nhap),
                Last_Updated_By_Function = @Last_Updated_By_Function
            WHERE Auto_ID = @Document_ID;
        END
        ELSE
        BEGIN
            EXEC dbo.sp_XNK_Reservation_Release_Document @Document_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho
            SET Is_Posted = 1,
                Posted_At = SYSUTCDATETIME(),
                Last_Updated = SYSUTCDATETIME(),
                Last_Updated_By = COALESCE(NULLIF(@Last_Updated_By, N''), @Ma_Dang_Nhap),
                Last_Updated_By_Function = @Last_Updated_By_Function
            WHERE Auto_ID = @Document_ID;
            IF EXISTS
            (
                SELECT 1 FROM #Delta d
                LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) ON b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID
                WHERE ISNULL(b.CurrentQuantity, 0) - ISNULL(b.ReservedQuantity, 0) < -d.Delta
            ) THROW 51120, N'Không thể Post vì tồn khả dụng không đủ.', 1;
        END

        IF EXISTS (SELECT 1 FROM #Delta d LEFT JOIN dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) ON b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID WHERE ISNULL(b.CurrentQuantity, 0) + d.Delta < 0) THROW 51120, N'Không thể Post vì tồn kho không đủ.', 1;
        UPDATE b SET CurrentQuantity = b.CurrentQuantity + d.Delta, UpdatedAt = SYSUTCDATETIME()
        FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) JOIN #Delta d ON d.Kho_ID = b.Kho_ID AND d.San_Pham_ID = b.San_Pham_ID;
        INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity)
        SELECT d.Kho_ID, d.San_Pham_ID, d.Delta, 0 FROM #Delta d
        WHERE NOT EXISTS (SELECT 1 FROM dbo.InventoryBalance_Current b WITH (UPDLOCK, HOLDLOCK) WHERE b.Kho_ID = d.Kho_ID AND b.San_Pham_ID = d.San_Pham_ID);

        /* Validate only the scopes changed by this Post.  The opening balance
           is the posted ledger before the earliest affected date; the running
           suffix includes the newly posted document and every later movement
           in the same warehouse/product scope. */
        CREATE TABLE #AffectedScope
        (
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            EarliestAffectedDate DATE NOT NULL,
            PRIMARY KEY (Kho_ID, San_Pham_ID)
        );
        INSERT #AffectedScope(Kho_ID, San_Pham_ID, EarliestAffectedDate)
        SELECT Kho_ID, San_Pham_ID, @Movement_Date
        FROM #Delta;

        DECLARE @HasAffectedNegative BIT = 0;
        ;WITH ScopeMovements AS
        (
            SELECT h.Kho_ID,
                   d.San_Pham_ID,
                   h.Ngay_Nhap_Kho AS MovementDate,
                   CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Delta
            FROM dbo.tbl_XNK_Nhap_Kho h WITH (UPDLOCK, HOLDLOCK)
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d WITH (UPDLOCK, HOLDLOCK)
              ON d.Nhap_Kho_ID = h.Auto_ID
            JOIN #AffectedScope scope
              ON scope.Kho_ID = h.Kho_ID
             AND scope.San_Pham_ID = d.San_Pham_ID
            WHERE h.Is_Posted = 1
            UNION ALL
            SELECT h.Kho_ID,
                   d.San_Pham_ID,
                   h.Ngay_Xuat_Kho,
                   CAST(-d.SL_Xuat AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h WITH (UPDLOCK, HOLDLOCK)
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d WITH (UPDLOCK, HOLDLOCK)
              ON d.Xuat_Kho_ID = h.Auto_ID
            JOIN #AffectedScope scope
              ON scope.Kho_ID = h.Kho_ID
             AND scope.San_Pham_ID = d.San_Pham_ID
            WHERE h.Is_Posted = 1
        ), DailyScopeMovements AS
        (
            SELECT Kho_ID, San_Pham_ID, MovementDate, CAST(SUM(Delta) AS DECIMAL(18,3)) AS Delta
            FROM ScopeMovements
            GROUP BY Kho_ID, San_Pham_ID, MovementDate
        ), OpeningBalances AS
        (
            SELECT scope.Kho_ID,
                   scope.San_Pham_ID,
                   CAST(COALESCE(SUM(movement.Delta), 0) AS DECIMAL(18,3)) AS OpeningQuantity
            FROM #AffectedScope scope
            LEFT JOIN DailyScopeMovements movement
              ON movement.Kho_ID = scope.Kho_ID
             AND movement.San_Pham_ID = scope.San_Pham_ID
             AND movement.MovementDate < scope.EarliestAffectedDate
            GROUP BY scope.Kho_ID, scope.San_Pham_ID
        ), RunningAffectedBalances AS
        (
            SELECT movement.Kho_ID,
                   movement.San_Pham_ID,
                   movement.MovementDate,
                   CAST(opening.OpeningQuantity + SUM(movement.Delta) OVER
                       (PARTITION BY movement.Kho_ID, movement.San_Pham_ID
                        ORDER BY movement.MovementDate ROWS UNBOUNDED PRECEDING) AS DECIMAL(18,3)) AS RunningQuantity
            FROM DailyScopeMovements movement
            JOIN #AffectedScope scope
              ON scope.Kho_ID = movement.Kho_ID
             AND scope.San_Pham_ID = movement.San_Pham_ID
             AND movement.MovementDate >= scope.EarliestAffectedDate
            JOIN OpeningBalances opening
              ON opening.Kho_ID = movement.Kho_ID
             AND opening.San_Pham_ID = movement.San_Pham_ID
        )
        SELECT @HasAffectedNegative = CASE WHEN EXISTS
        (
            SELECT 1 FROM RunningAffectedBalances WHERE RunningQuantity < 0
        ) THEN 1 ELSE 0 END;

        IF @HasAffectedNegative = 1
            THROW 51120, N'Không thể Post vì tồn kho sẽ âm tại một thời điểm trong lịch sử.', 1;

        DECLARE @MovementAffected dbo.InventoryMovementAffectedType;
        INSERT @MovementAffected(Kho_ID, San_Pham_ID, Movement_Date, InvalidReason)
        SELECT Kho_ID, San_Pham_ID, @Movement_Date, N'POSTED_DOCUMENT'
        FROM #Delta;
        EXEC dbo.sp_Inventory_Movement_Apply_Invalidation @Affected = @MovementAffected;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_InventoryBalance_Rebuild
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    BEGIN TRY
        DELETE FROM dbo.InventoryBalance_Current;
        ;WITH Delta AS
        (
            SELECT h.Kho_ID, d.San_Pham_ID, CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Amount FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID WHERE h.Is_Posted = 1
            UNION ALL
            SELECT h.Kho_ID, d.San_Pham_ID, CAST(-d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID WHERE h.Is_Posted = 1
        )
        INSERT dbo.InventoryBalance_Current(Kho_ID, San_Pham_ID, CurrentQuantity, ReservedQuantity)
        SELECT Kho_ID, San_Pham_ID, SUM(Amount), 0 FROM Delta GROUP BY Kho_ID, San_Pham_ID;
        EXEC dbo.sp_XNK_Reservation_Rebuild;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Save
    @Auto_ID BIGINT OUTPUT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N'' THROW 51050, N'Mã đăng nhập không được để trống.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap) THROW 51055, N'Mã đăng nhập không tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51051, N'Kho không hợp lệ.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap AND Kho_ID = @Kho_ID AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51052, N'User đã được phân quyền kho này.', 1;
    IF ISNULL(@Auto_ID, 0) = 0
        INSERT dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function)
        VALUES(@Ma_Dang_Nhap, @Kho_ID, COALESCE(@Created_By, @Last_Updated_By), COALESCE(@Created_By_Function, @Last_Updated_By_Function), SYSUTCDATETIME(), @Last_Updated_By, @Last_Updated_By_Function);
    ELSE
        UPDATE dbo.tbl_DM_Kho_User SET Ma_Dang_Nhap = @Ma_Dang_Nhap, Kho_ID = @Kho_ID, Last_Updated = SYSUTCDATETIME(), Last_Updated_By = @Last_Updated_By, Last_Updated_By_Function = @Last_Updated_By_Function WHERE Auto_ID = @Auto_ID;
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_List
    @Search_Text NVARCHAR(100) = N''
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    SELECT u.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, tv.Ho_Ten AS Name, u.Ma_Dang_Nhap AS Login_Name, k.Ten_Kho AS Ghi_Chu, u.Kho_ID AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2
    FROM dbo.tbl_DM_Kho_User u LEFT JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = u.Ma_Dang_Nhap JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = u.Kho_ID
    WHERE ISNULL(@Search_Text, N'') = N'' OR u.Ma_Dang_Nhap LIKE @Filter OR tv.Ho_Ten LIKE @Filter OR k.Ten_Kho LIKE @Filter
    ORDER BY u.Ma_Dang_Nhap, k.Ten_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_User_Page
    @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100) = N''
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    SELECT COUNT(*) AS Total_Count
    FROM dbo.tbl_DM_Kho_User u LEFT JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = u.Ma_Dang_Nhap JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = u.Kho_ID
    WHERE ISNULL(@Search_Text, N'') = N'' OR u.Ma_Dang_Nhap LIKE @Filter OR tv.Ho_Ten LIKE @Filter OR k.Ten_Kho LIKE @Filter;
    SELECT u.Auto_ID, CAST(N'' AS NVARCHAR(100)) AS Code, tv.Ho_Ten AS Name, u.Ma_Dang_Nhap AS Login_Name, k.Ten_Kho AS Ghi_Chu, u.Kho_ID AS Related_ID, CAST(0 AS BIGINT) AS Related_ID_2
    FROM dbo.tbl_DM_Kho_User u LEFT JOIN dbo.tbl_Sys_Thanh_Vien tv ON tv.Ma_Dang_Nhap = u.Ma_Dang_Nhap JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = u.Kho_ID
    WHERE ISNULL(@Search_Text, N'') = N'' OR u.Ma_Dang_Nhap LIKE @Filter OR tv.Ho_Ten LIKE @Filter OR k.Ten_Kho LIKE @Filter
    ORDER BY u.Ma_Dang_Nhap, k.Ten_Kho OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_List
    @Is_Receipt BIT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF @Is_Receipt = 1
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
        FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
        ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC;
    ELSE
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
        ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Detail_List
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT;
    IF @Is_Receipt = 1
    BEGIN
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID;
        IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        SELECT d.Auto_ID, d.Nhap_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Nhap AS So_Luong, d.Don_Gia_Nhap AS Don_Gia
        FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
        WHERE d.Nhap_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
    END
    ELSE
    BEGIN
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        SELECT d.Auto_ID, d.Xuat_Kho_ID AS Document_ID, d.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham, dv.Ten_Don_Vi_Tinh, d.SL_Xuat AS So_Luong, d.Don_Gia_Xuat AS Don_Gia
        FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = d.San_Pham_ID JOIN dbo.tbl_DM_Don_Vi_Tinh dv ON dv.Auto_ID = p.Don_Vi_Tinh_ID
        WHERE d.Xuat_Kho_ID = @Document_ID ORDER BY d.Auto_ID;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Page
    @Is_Receipt BIT, @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100) = N'', @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    IF @Is_Receipt = 1
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter);
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter)
        ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter);
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter)
        ORDER BY h.Ngay_Xuat_Kho DESC, h.Auto_ID DESC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_San_Pham_Save
    @Auto_ID BIGINT OUTPUT, @Ma_San_Pham NVARCHAR(100), @Ten_San_Pham NVARCHAR(255), @Loai_San_Pham_ID BIGINT, @Don_Vi_Tinh_ID BIGINT, @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ma_San_Pham=LTRIM(RTRIM(ISNULL(@Ma_San_Pham,N''))); SET @Ten_San_Pham=LTRIM(RTRIM(ISNULL(@Ten_San_Pham,N'')));
    IF @Ma_San_Pham=N'' THROW 51020,N'Mã sản phẩm không được để trống.',1; IF @Ten_San_Pham=N'' THROW 51022,N'Tên sản phẩm không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Ma_San_Pham=@Ma_San_Pham AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51021,N'Mã sản phẩm đã tồn tại.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Loai_San_Pham WHERE Auto_ID=@Loai_San_Pham_ID) THROW 51023,N'Loại sản phẩm không hợp lệ.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Don_Vi_Tinh WHERE Auto_ID=@Don_Vi_Tinh_ID) THROW 51024,N'Đơn vị tính không hợp lệ.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_San_Pham(Ma_San_Pham,Ten_San_Pham,Loai_San_Pham_ID,Don_Vi_Tinh_ID,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ma_San_Pham,@Ten_San_Pham,@Loai_San_Pham_ID,@Don_Vi_Tinh_ID,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_San_Pham SET Ma_San_Pham=@Ma_San_Pham,Ten_San_Pham=@Ten_San_Pham,Loai_San_Pham_ID=@Loai_San_Pham_ID,Don_Vi_Tinh_ID=@Don_Vi_Tinh_ID,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_NCC_Save
    @Auto_ID BIGINT OUTPUT, @Ma_NCC NVARCHAR(100), @Ten_NCC NVARCHAR(255), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ma_NCC=LTRIM(RTRIM(ISNULL(@Ma_NCC,N''))); SET @Ten_NCC=LTRIM(RTRIM(ISNULL(@Ten_NCC,N'')));
    IF @Ma_NCC=N'' THROW 51030,N'Mã nhà cung cấp không được để trống.',1; IF @Ten_NCC=N'' THROW 51032,N'Tên nhà cung cấp không được để trống.',1;
    IF EXISTS(SELECT 1 FROM dbo.tbl_DM_NCC WHERE (Ma_NCC=@Ma_NCC OR Ten_NCC=@Ten_NCC) AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51031,N'Mã hoặc tên nhà cung cấp đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_NCC(Ma_NCC,Ten_NCC,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ma_NCC,@Ten_NCC,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_NCC SET Ma_NCC=@Ma_NCC,Ten_NCC=@Ten_NCC,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DM_Kho_Save
    @Auto_ID BIGINT OUTPUT, @Ten_Kho NVARCHAR(255), @Ghi_Chu NVARCHAR(1000)=NULL,
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @Ten_Kho=LTRIM(RTRIM(ISNULL(@Ten_Kho,N'')));
    IF @Ten_Kho=N'' THROW 51040,N'Tên kho không được để trống.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Ten_Kho=@Ten_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51041,N'Tên kho đã tồn tại.',1;
    IF ISNULL(@Auto_ID,0)=0 INSERT dbo.tbl_DM_Kho(Ten_Kho,Ghi_Chu,Created_By,Created_By_Function,Last_Updated_By,Last_Updated_By_Function) VALUES(@Ten_Kho,@Ghi_Chu,COALESCE(@Created_By,@Last_Updated_By),COALESCE(@Created_By_Function,@Last_Updated_By_Function),@Last_Updated_By,@Last_Updated_By_Function);
    ELSE UPDATE dbo.tbl_DM_Kho SET Ten_Kho=@Ten_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Last_Updated_By,Last_Updated_By_Function=@Last_Updated_By_Function WHERE Auto_ID=@Auto_ID;
    IF ISNULL(@Auto_ID,0)=0 SET @Auto_ID=SCOPE_IDENTITY(); SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE,
    @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF;
    DECLARE @OwnTransaction BIT = 0, @SavepointCreated BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    ELSE IF XACT_STATE() = 1
    BEGIN
        SAVE TRANSACTION SaveIssueHeader;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        SET @So_Phieu_Xuat_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho, N'')));
        IF @So_Phieu_Xuat_Kho = N'' THROW 51130, N'Số phiếu xuất không được để trống.', 1;
        IF @Ngay_Xuat_Kho IS NULL THROW 51133, N'Ngày xuất kho không được để trống.', 1;

        DECLARE @Old_Kho_ID BIGINT, @Is_Posted BIT;
        IF ISNULL(@Auto_ID, 0) = 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho) THROW 51131, N'Số phiếu xuất đã tồn tại.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
            INSERT dbo.tbl_XNK_Xuat_Kho
            (
                So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu,
                Created_By, Created_By_Function, Last_Updated_By, Last_Updated_By_Function
            )
            VALUES
            (
                @So_Phieu_Xuat_Kho, @Kho_ID, @Ngay_Xuat_Kho, 0, @Ghi_Chu,
                COALESCE(NULLIF(@Created_By, N''), NULLIF(@Last_Updated_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Created_By_Function, N''), NULLIF(@Last_Updated_By_Function, N'')),
                COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            );
        END
        ELSE
        BEGIN
            SELECT @Old_Kho_ID = Kho_ID, @Is_Posted = Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Auto_ID;
            IF @Old_Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            IF @Is_Posted = 1 THROW 51163, N'Không được sửa phiếu đã Post.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Old_Kho_ID;
            IF @Old_Kho_ID <> @Kho_ID EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
            IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho AND Auto_ID <> @Auto_ID) THROW 51131, N'Số phiếu xuất đã tồn tại.', 1;
            IF @Old_Kho_ID <> @Kho_ID EXEC dbo.sp_XNK_Reservation_Move_Document @Auto_ID, @Old_Kho_ID, @Kho_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho
            SET So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho,
                Kho_ID = @Kho_ID,
                Ngay_Xuat_Kho = @Ngay_Xuat_Kho,
                Ghi_Chu = @Ghi_Chu,
                Last_Updated = SYSUTCDATETIME(),
                Last_Updated_By = COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                Last_Updated_By_Function = COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            WHERE Auto_ID = @Auto_ID;
        END
        IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveIssueHeader;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100),
    @Created_By NVARCHAR(100)=NULL, @Created_By_Function NVARCHAR(100)=NULL,
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF;
    DECLARE @OwnTransaction BIT = 0, @SavepointCreated BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    ELSE IF XACT_STATE() = 1
    BEGIN
        SAVE TRANSACTION SaveIssueDetail;
        SET @SavepointCreated = 1;
    END
    BEGIN TRY
        DECLARE @Kho_ID BIGINT, @Old_San_Pham_ID BIGINT, @Old_ReservedQuantity DECIMAL(18,3) = 0, @Reservation_Kho_ID BIGINT;
        SELECT @Kho_ID = Kho_ID FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Xuat_Kho_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Xuat_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51135, N'Sản phẩm không hợp lệ.', 1;
        IF @SL_Xuat <= 0 THROW 51136, N'Số lượng xuất phải lớn hơn 0.', 1;
        IF @Don_Gia_Xuat <= 0 THROW 51137, N'Đơn giá xuất phải lớn hơn 0.', 1;

        IF ISNULL(@Auto_ID, 0) <> 0
        BEGIN
            SELECT @Old_San_Pham_ID = San_Pham_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Auto_ID AND Xuat_Kho_ID = @Xuat_Kho_ID;
            IF @Old_San_Pham_ID IS NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID) THROW 51138, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
                THROW 51139, N'Chi tiết phiếu xuất không tồn tại.', 1;
            END
            IF @Old_San_Pham_ID <> @San_Pham_ID THROW 51138, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
            SELECT @Old_ReservedQuantity = ReservedQuantity, @Reservation_Kho_ID = Kho_ID FROM dbo.InventoryReservation_Current WITH (UPDLOCK, HOLDLOCK) WHERE Xuat_Kho_Detail_ID = @Auto_ID;
            IF @Reservation_Kho_ID IS NOT NULL AND @Reservation_Kho_ID <> @Kho_ID
            BEGIN
                DECLARE @ReleaseDelta DECIMAL(18,3) = -@Old_ReservedQuantity;
                EXEC dbo.sp_XNK_Reservation_Adjust @Reservation_Kho_ID, @San_Pham_ID, @ReleaseDelta;
                SET @Old_ReservedQuantity = 0;
            END
        END

        DECLARE @ReservationDelta DECIMAL(18,3) = @SL_Xuat - @Old_ReservedQuantity;
        EXEC dbo.sp_XNK_Reservation_Adjust @Kho_ID, @San_Pham_ID, @ReservationDelta;
        IF ISNULL(@Auto_ID, 0) = 0
        BEGIN
            INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data
            (
                Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat,
                Created, Created_By, Created_By_Function, Last_Updated, Last_Updated_By, Last_Updated_By_Function
            )
            VALUES
            (
                @Xuat_Kho_ID, @San_Pham_ID, @SL_Xuat, @Don_Gia_Xuat,
                SYSUTCDATETIME(),
                COALESCE(NULLIF(@Created_By, N''), NULLIF(@Last_Updated_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Created_By_Function, N''), NULLIF(@Last_Updated_By_Function, N'')),
                SYSUTCDATETIME(),
                COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            );
            SET @Auto_ID = SCOPE_IDENTITY();
            INSERT dbo.InventoryReservation_Current(Xuat_Kho_Detail_ID, Kho_ID, San_Pham_ID, ReservedQuantity) VALUES(@Auto_ID, @Kho_ID, @San_Pham_ID, @SL_Xuat);
        END
        ELSE
        BEGIN
            UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data
            SET SL_Xuat = @SL_Xuat,
                Don_Gia_Xuat = @Don_Gia_Xuat,
                Last_Updated = SYSUTCDATETIME(),
                Last_Updated_By = COALESCE(NULLIF(@Last_Updated_By, N''), NULLIF(@Created_By, N''), @Ma_Dang_Nhap),
                Last_Updated_By_Function = COALESCE(NULLIF(@Last_Updated_By_Function, N''), NULLIF(@Created_By_Function, N''))
            WHERE Auto_ID = @Auto_ID;
            IF EXISTS (SELECT 1 FROM dbo.InventoryReservation_Current WHERE Xuat_Kho_Detail_ID = @Auto_ID)
                UPDATE dbo.InventoryReservation_Current SET Kho_ID = @Kho_ID, San_Pham_ID = @San_Pham_ID, ReservedQuantity = @SL_Xuat, UpdatedAt = SYSUTCDATETIME() WHERE Xuat_Kho_Detail_ID = @Auto_ID;
            ELSE
                INSERT dbo.InventoryReservation_Current(Xuat_Kho_Detail_ID, Kho_ID, San_Pham_ID, ReservedQuantity) VALUES(@Auto_ID, @Kho_ID, @San_Pham_ID, @SL_Xuat);
        END
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION SaveIssueDetail;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT, @Document_ID BIGINT;
    SELECT @Kho_ID = h.Kho_ID, @Document_ID = h.Auto_ID FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID = d.Nhap_Kho_ID WHERE d.Auto_ID = @Auto_ID;
    IF @Kho_ID IS NULL THROW 51105, N'Chi tiết phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
    DELETE dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Detail
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF;
    DECLARE @OwnTransaction BIT = 0, @SavepointCreated BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    ELSE IF XACT_STATE() = 1
    BEGIN
        SAVE TRANSACTION DeleteIssueDetail;
        SET @SavepointCreated = 1;
    END

    BEGIN TRY
        DECLARE @Kho_ID BIGINT, @Document_ID BIGINT, @Is_Posted BIT;
        SELECT @Document_ID = Xuat_Kho_ID
        FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data
        WHERE Auto_ID = @Auto_ID;
        IF @Document_ID IS NULL THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;

        /* Match Save_Detail/Post/Save_Header: parent first, then detail. */
        SELECT @Kho_ID = Kho_ID, @Is_Posted = Is_Posted
        FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK)
        WHERE Auto_ID = @Document_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Is_Posted = 1 THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WITH (UPDLOCK, HOLDLOCK)
            WHERE Auto_ID = @Auto_ID AND Xuat_Kho_ID = @Document_ID
        )
            THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;

        EXEC dbo.sp_XNK_Reservation_Release_Detail @Auto_ID;
        DELETE dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
        IF @@ROWCOUNT <> 1 THROW 51139, N'Chi tiết phiếu xuất không còn tồn tại.', 1;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION DeleteIssueDetail;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT OFF;
    DECLARE @OwnTransaction BIT = 0, @SavepointCreated BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    ELSE IF XACT_STATE() = 1
    BEGIN
        SAVE TRANSACTION DeleteIssueHeader;
        SET @SavepointCreated = 1;
    END

    BEGIN TRY
        DECLARE @Kho_ID BIGINT, @Is_Posted BIT;
        /* The parent lock covers validation, reservation release and cascade. */
        SELECT @Kho_ID = Kho_ID, @Is_Posted = Is_Posted
        FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK)
        WHERE Auto_ID = @Auto_ID;
        IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Is_Posted = 1 THROW 51163, N'Không được xóa phiếu đã Post.', 1;

        EXEC dbo.sp_XNK_Reservation_Release_Document @Auto_ID;
        DELETE dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID;
        IF @@ROWCOUNT <> 1 THROW 51134, N'Phiếu xuất không còn tồn tại.', 1;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @SavepointCreated = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION DeleteIssueHeader;
        THROW;
    END CATCH
END
GO

/* Keep this function as the single calculation contract for both report
   procedures.  Each warehouse/product scope starts from its own nearest
   valid end-of-day snapshot before @Tu_Ngay, then only reads later movements. */
CREATE OR ALTER FUNCTION dbo.fn_Inventory_Report_Snapshot
(
    @Tu_Ngay DATE,
    @Den_Ngay DATE,
    @Ma_Dang_Nhap NVARCHAR(100),
    @Is_Current_Report BIT
)
RETURNS TABLE
AS
RETURN
(
    WITH AuthorizedWarehouse AS
    (
        SELECT DISTINCT Kho_ID
        FROM dbo.tbl_DM_Kho_User
        WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap
    ), ScopeKeys AS
    (
        SELECT s.Kho_ID, s.San_Pham_ID
        FROM dbo.InventoryBalance_Snapshot_Daily s
        JOIN AuthorizedWarehouse aw ON aw.Kho_ID = s.Kho_ID
        WHERE s.Snapshot_Date < @Tu_Ngay
        UNION
        SELECT h.Kho_ID, d.San_Pham_ID
        FROM dbo.tbl_XNK_Nhap_Kho h
        JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
        JOIN AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
        WHERE h.Is_Posted = 1
          AND h.Ngay_Nhap_Kho <= @Den_Ngay
        UNION
        SELECT h.Kho_ID, d.San_Pham_ID
        FROM dbo.tbl_XNK_Xuat_Kho h
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        JOIN AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
        WHERE h.Is_Posted = 1
          AND h.Ngay_Xuat_Kho <= @Den_Ngay
        UNION
        SELECT b.Kho_ID, b.San_Pham_ID
        FROM dbo.InventoryBalance_Current b
        JOIN AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID
        WHERE @Is_Current_Report = 1
    ), SnapshotBalance AS
    (
        SELECT sk.Kho_ID, sk.San_Pham_ID,
               latest.Snapshot_Date,
               ISNULL(latest.ClosingQuantity, 0) AS ClosingQuantity
        FROM ScopeKeys sk
        OUTER APPLY
        (
            SELECT TOP (1) s.Snapshot_Date, s.ClosingQuantity
            FROM dbo.InventoryBalance_Snapshot_Daily s
            WHERE s.Kho_ID = sk.Kho_ID
              AND s.San_Pham_ID = sk.San_Pham_ID
              AND s.Snapshot_Date < @Tu_Ngay
              AND s.IsValid = 1
            ORDER BY s.Snapshot_Date DESC
        ) latest
    ), Movements AS
    (
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Nhap_Kho AS MovementDate,
               CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity,
               CAST(0 AS DECIMAL(18,3)) AS OutQuantity
        FROM dbo.tbl_XNK_Nhap_Kho h
        JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
        JOIN AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
        JOIN SnapshotBalance sb ON sb.Kho_ID = h.Kho_ID AND sb.San_Pham_ID = d.San_Pham_ID
        WHERE h.Is_Posted = 1
          AND h.Ngay_Nhap_Kho BETWEEN ISNULL(DATEADD(DAY, 1, sb.Snapshot_Date), CONVERT(DATE, '19000101')) AND @Den_Ngay
        UNION ALL
        SELECT h.Kho_ID, d.San_Pham_ID, h.Ngay_Xuat_Kho,
               CAST(0 AS DECIMAL(18,3)),
               CAST(d.SL_Xuat AS DECIMAL(18,3))
        FROM dbo.tbl_XNK_Xuat_Kho h
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        JOIN AuthorizedWarehouse aw ON aw.Kho_ID = h.Kho_ID
        JOIN SnapshotBalance sb ON sb.Kho_ID = h.Kho_ID AND sb.San_Pham_ID = d.San_Pham_ID
        WHERE h.Is_Posted = 1
          AND h.Ngay_Xuat_Kho BETWEEN ISNULL(DATEADD(DAY, 1, sb.Snapshot_Date), CONVERT(DATE, '19000101')) AND @Den_Ngay
    ), MovementAggregate AS
    (
        SELECT Kho_ID, San_Pham_ID,
               SUM(CASE WHEN MovementDate < @Tu_Ngay THEN InQuantity - OutQuantity ELSE 0 END) AS OpeningDelta,
               SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN InQuantity ELSE 0 END) AS Received,
               SUM(CASE WHEN MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN OutQuantity ELSE 0 END) AS Issued
        FROM Movements
        GROUP BY Kho_ID, San_Pham_ID
    ), ReportKeys AS
    (
        SELECT Kho_ID, San_Pham_ID FROM SnapshotBalance
        UNION
        SELECT Kho_ID, San_Pham_ID FROM MovementAggregate
        UNION
        SELECT b.Kho_ID, b.San_Pham_ID
        FROM dbo.InventoryBalance_Current b
        JOIN AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID
        WHERE @Is_Current_Report = 1
    ), Calculated AS
    (
        SELECT rk.Kho_ID, rk.San_Pham_ID,
               CAST(ISNULL(sb.ClosingQuantity, 0) + ISNULL(ma.OpeningDelta, 0) AS DECIMAL(18,3)) AS SL_Dau_Ky,
               CAST(ISNULL(ma.Received, 0) AS DECIMAL(18,3)) AS SL_Nhap,
               CAST(ISNULL(ma.Issued, 0) AS DECIMAL(18,3)) AS SL_Xuat,
               CAST(ISNULL(sb.ClosingQuantity, 0) + ISNULL(ma.OpeningDelta, 0) + ISNULL(ma.Received, 0) - ISNULL(ma.Issued, 0) AS DECIMAL(18,3)) AS HistoricalClosing,
               b.CurrentQuantity, b.ReservedQuantity
        FROM ReportKeys rk
        LEFT JOIN SnapshotBalance sb ON sb.Kho_ID = rk.Kho_ID AND sb.San_Pham_ID = rk.San_Pham_ID
        LEFT JOIN MovementAggregate ma ON ma.Kho_ID = rk.Kho_ID AND ma.San_Pham_ID = rk.San_Pham_ID
        LEFT JOIN dbo.InventoryBalance_Current b ON b.Kho_ID = rk.Kho_ID AND b.San_Pham_ID = rk.San_Pham_ID
    )
    SELECT c.Kho_ID, k.Ten_Kho, c.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham,
           c.SL_Dau_Ky, c.SL_Nhap, c.SL_Xuat,
           /* SL_Cuoi_Ky is always the period closing.  CurrentQuantity is a
              separate current-state metric and must not replace the period
              value when @Den_Ngay happens to be today. */
           CAST(c.HistoricalClosing AS DECIMAL(18,3)) AS SL_Cuoi_Ky,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(c.CurrentQuantity, 0) ELSE c.HistoricalClosing END AS DECIMAL(18,3)) AS SL_Ton_Thuc_Te,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(c.ReservedQuantity, 0) ELSE 0 END AS DECIMAL(18,3)) AS SL_Dang_Giu,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(c.CurrentQuantity, 0) - ISNULL(c.ReservedQuantity, 0) ELSE c.HistoricalClosing END AS DECIMAL(18,3)) AS SL_Kha_Dung
    FROM Calculated c
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = c.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = c.San_Pham_ID
);
GO

/* Report reads share the same per-scope fence as Post and projection rebuilds.
   The helper captures a cutoff-aware catalog watermark before materializing
   and locking scopes.  Per-scope applocks protect scopes already present;
   final watermark validation catches a newly introduced relevant scope. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Report_Acquire_Scope_Fence
    @Tu_Ngay DATE,
    @Den_Ngay DATE,
    @Ma_Dang_Nhap NVARCHAR(100),
    @Kho_ID BIGINT = NULL,
    @Is_Current_Report BIT = 0,
    @Catalog_Scope_Count BIGINT OUTPUT,
    @Catalog_Max_ID BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @Catalog_Scope_Count = COUNT_BIG(*),
           @Catalog_Max_ID = COALESCE(MAX(c.Catalog_ID), 0)
    FROM dbo.Inventory_Report_Scope_Catalog c
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = c.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR c.Kho_ID = @Kho_ID)
      AND (@Is_Current_Report = 1 OR c.First_Posted_Date <= @Den_Ngay);

    CREATE TABLE #ReportScope
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        PRIMARY KEY (Kho_ID, San_Pham_ID)
    );

    INSERT #ReportScope(Kho_ID, San_Pham_ID)
    SELECT s.Kho_ID, s.San_Pham_ID
    FROM dbo.InventoryBalance_Snapshot_Daily s
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR s.Kho_ID = @Kho_ID)
      AND s.Snapshot_Date < @Tu_Ngay
    UNION
    SELECT s.Kho_ID, s.San_Pham_ID
    FROM dbo.Inventory_Balance_Daily_Scope s
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR s.Kho_ID = @Kho_ID)
      AND s.First_Balance_Date <= @Den_Ngay
    UNION
    SELECT h.Kho_ID, d.San_Pham_ID
    FROM dbo.tbl_XNK_Nhap_Kho h
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = h.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
      AND h.Is_Posted = 1
      AND h.Ngay_Nhap_Kho <= @Den_Ngay
    UNION
    SELECT h.Kho_ID, d.San_Pham_ID
    FROM dbo.tbl_XNK_Xuat_Kho h
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = h.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
      AND h.Is_Posted = 1
      AND h.Ngay_Xuat_Kho <= @Den_Ngay
    UNION
    SELECT b.Kho_ID, b.San_Pham_ID
    FROM dbo.InventoryBalance_Current b
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = b.Kho_ID
    WHERE @Is_Current_Report = 1
      AND ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR b.Kho_ID = @Kho_ID);

    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255);
    DECLARE @ScopeKho_ID BIGINT, @ScopeSan_Pham_ID BIGINT;
    DECLARE report_scope_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT Kho_ID, San_Pham_ID FROM #ReportScope ORDER BY Kho_ID, San_Pham_ID;
    OPEN report_scope_cursor;
    FETCH NEXT FROM report_scope_cursor INTO @ScopeKho_ID, @ScopeSan_Pham_ID;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @LockResource = CONCAT(N'InventoryMovement:', @ScopeKho_ID, N':', @ScopeSan_Pham_ID);
        EXEC @LockResult = sys.sp_getapplock
            @Resource = @LockResource,
            @LockMode = N'Shared',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @LockResult < 0
        BEGIN
            CLOSE report_scope_cursor;
            DEALLOCATE report_scope_cursor;
            THROW 51323, N'Báo cáo đang chờ Post hoặc projection rebuild cùng scope; vui lòng thử lại.', 1;
        END
        FETCH NEXT FROM report_scope_cursor INTO @ScopeKho_ID, @ScopeSan_Pham_ID;
    END
    CLOSE report_scope_cursor;
    DEALLOCATE report_scope_cursor;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay
        THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    BEGIN TRY
        IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        DECLARE @Is_Current_Report BIT = IIF(@Den_Ngay = CONVERT(DATE, SYSDATETIME()), 1, 0);
        DECLARE @Catalog_Scope_Count BIGINT, @Catalog_Max_ID BIGINT;
        EXEC dbo.sp_Inventory_Report_Acquire_Scope_Fence
            @Tu_Ngay = @Tu_Ngay,
            @Den_Ngay = @Den_Ngay,
            @Ma_Dang_Nhap = @Ma_Dang_Nhap,
            @Kho_ID = @Kho_ID,
            @Is_Current_Report = @Is_Current_Report,
            @Catalog_Scope_Count = @Catalog_Scope_Count OUTPUT,
            @Catalog_Max_ID = @Catalog_Max_ID OUTPUT;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.InventoryMovement_RebuildQueue q
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = q.Kho_ID
            WHERE q.Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL')
              AND ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
              AND q.From_Date <= @Den_Ngay
        )
            THROW 51222, N'Movement Aggregate của báo cáo đang tái tạo. Vui lòng thử lại sau khi worker hoàn tất.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM
            (
            SELECT s.Kho_ID, s.San_Pham_ID
            FROM dbo.InventoryBalance_Snapshot_Daily s
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR s.Kho_ID = @Kho_ID)
              AND s.Snapshot_Date < @Tu_Ngay
            UNION
            SELECT h.Kho_ID, d.San_Pham_ID
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = h.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
              AND h.Is_Posted = 1
              AND h.Ngay_Nhap_Kho <= @Den_Ngay
            UNION
            SELECT h.Kho_ID, d.San_Pham_ID
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = h.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR h.Kho_ID = @Kho_ID)
              AND h.Is_Posted = 1
              AND h.Ngay_Xuat_Kho <= @Den_Ngay
            UNION
            SELECT b.Kho_ID, b.San_Pham_ID
            FROM dbo.InventoryBalance_Current b
            JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = b.Kho_ID
            WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
              AND (@Kho_ID IS NULL OR b.Kho_ID = @Kho_ID)
              AND @Den_Ngay = CONVERT(DATE, SYSDATETIME())
            ) sk
        OUTER APPLY
        (
            SELECT TOP (1) s.Snapshot_Date
            FROM dbo.InventoryBalance_Snapshot_Daily s
            WHERE s.Kho_ID = sk.Kho_ID
              AND s.San_Pham_ID = sk.San_Pham_ID
              AND s.Snapshot_Date < @Tu_Ngay
              AND s.IsValid = 1
            ORDER BY s.Snapshot_Date DESC
        ) latest
        WHERE latest.Snapshot_Date IS NULL
    )
        INSERT dbo.InventorySnapshot_ReportFallbackLog
        (ReportFromDate, ReportToDate, Ma_Dang_Nhap, SnapshotMissingReason)
        VALUES (@Tu_Ngay, @Den_Ngay, @Ma_Dang_Nhap, N'NO_VALID_SNAPSHOT_FOR_ONE_OR_MORE_SCOPES');

    SELECT r.Kho_ID, r.Ten_Kho, r.San_Pham_ID, r.Ma_San_Pham, r.Ten_San_Pham,
           r.SL_Dau_Ky, r.SL_Nhap, r.SL_Xuat, r.SL_Cuoi_Ky, r.SL_Ton_Thuc_Te, r.SL_Dang_Giu, r.SL_Kha_Dung
    INTO #ReportRows
    FROM dbo.fn_Inventory_Report_Snapshot(@Tu_Ngay, @Den_Ngay, @Ma_Dang_Nhap, IIF(@Den_Ngay = CONVERT(DATE, SYSDATETIME()), 1, 0)) r
    WHERE @Kho_ID IS NULL OR r.Kho_ID = @Kho_ID;

    DECLARE @Current_Catalog_Scope_Count BIGINT, @Current_Catalog_Max_ID BIGINT;
    SELECT @Current_Catalog_Scope_Count = COUNT_BIG(*),
           @Current_Catalog_Max_ID = COALESCE(MAX(c.Catalog_ID), 0)
    FROM dbo.Inventory_Report_Scope_Catalog c
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = c.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR c.Kho_ID = @Kho_ID)
      AND (@Is_Current_Report = 1 OR c.First_Posted_Date <= @Den_Ngay);

    IF @Current_Catalog_Scope_Count <> @Catalog_Scope_Count
       OR @Current_Catalog_Max_ID <> @Catalog_Max_ID
        THROW 51324, N'Phạm vi báo cáo đã thay đổi trong lúc đọc; vui lòng thử lại.', 1;

    SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham,
           SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky, SL_Ton_Thuc_Te, SL_Dang_Giu, SL_Kha_Dung
    FROM #ReportRows
    ORDER BY Ten_Kho, Ma_San_Pham;
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* Header triggers: invalidate and enqueue only the affected
   warehouse/product scope. No snapshot row is deleted. */
CREATE OR ALTER TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Receipt_Post
ON dbo.tbl_XNK_Nhap_Kho
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(Is_Posted) AND NOT UPDATE(Ngay_Nhap_Kho) RETURN;

    DECLARE @Affected dbo.InventorySnapshotAffectedType;
    INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT DISTINCT i.Kho_ID,
           r.San_Pham_ID,
           CASE WHEN i.Ngay_Nhap_Kho <= d.Ngay_Nhap_Kho THEN i.Ngay_Nhap_Kho ELSE d.Ngay_Nhap_Kho END,
           CASE WHEN d.Is_Posted = 0 AND i.Is_Posted = 1 THEN N'BACK_DATE_POST' ELSE N'DOCUMENT_UPDATE' END
    FROM inserted i
    JOIN deleted d ON d.Auto_ID = i.Auto_ID
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data r ON r.Nhap_Kho_ID = i.Auto_ID
    WHERE (d.Is_Posted = 0 AND i.Is_Posted = 1)
       OR (d.Is_Posted = 1 AND i.Is_Posted = 1 AND i.Ngay_Nhap_Kho <> d.Ngay_Nhap_Kho);

    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Snapshot_Invalidate_Issue_Post
ON dbo.tbl_XNK_Xuat_Kho
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(Is_Posted) AND NOT UPDATE(Ngay_Xuat_Kho) RETURN;

    DECLARE @Affected dbo.InventorySnapshotAffectedType;
    INSERT @Affected(Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    SELECT i.Kho_ID,
           r.San_Pham_ID,
           CASE WHEN i.Ngay_Xuat_Kho <= d.Ngay_Xuat_Kho THEN i.Ngay_Xuat_Kho ELSE d.Ngay_Xuat_Kho END,
           CASE WHEN d.Is_Posted = 0 AND i.Is_Posted = 1 THEN N'BACK_DATE_POST' ELSE N'DOCUMENT_UPDATE' END
    FROM inserted i
    JOIN deleted d ON d.Auto_ID = i.Auto_ID
    JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data r ON r.Xuat_Kho_ID = i.Auto_ID
    WHERE (d.Is_Posted = 0 AND i.Is_Posted = 1)
       OR (d.Is_Posted = 1 AND i.Is_Posted = 1 AND i.Ngay_Xuat_Kho <> d.Ngay_Xuat_Kho);

    EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Affected = @Affected;
END
GO

/* Materialize each input once.  The inline function remains the non-paged
   contract; the UI hot path avoids its repeated CTE expansion. */
SET QUOTED_IDENTIFIER ON;
GO

/* The current-stock screen is deliberately a different contract from the
   historical Xuất nhập tồn report below. A current balance is already
   materialized by sp_XNK_Document_Post in the same transaction as Post, so
   readers must not rebuild period aggregates simply to display On Hand,
   Reserved and Available. */
CREATE OR ALTER PROCEDURE dbo.sp_BC_Ton_Kho_Hien_Tai_Page
    @Page_Number INT,
    @Page_Size INT,
    @Ma_Dang_Nhap NVARCHAR(100),
    @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Ma_Dang_Nhap = LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap, N'')));
    IF @Ma_Dang_Nhap = N''
       OR NOT EXISTS (SELECT 1 FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap)
        THROW 51053, N'Phiên đăng nhập không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;

    CREATE TABLE #AuthorizedWarehouse
    (
        Kho_ID BIGINT NOT NULL PRIMARY KEY
    );

    INSERT #AuthorizedWarehouse(Kho_ID)
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID);

    DECLARE @Current_Report_Generation_Start BIGINT,
            @Current_Report_Generation_End BIGINT;
    SELECT @Current_Report_Generation_Start = Generation
    FROM dbo.Inventory_Current_Report_State
    WHERE State_ID = 1;
    IF @Current_Report_Generation_Start IS NULL
        THROW 51325, N'Generation báo cáo tồn kho hiện tại chưa được khởi tạo.', 1;

    DECLARE @Total_Count INT;
    SELECT @Total_Count = COUNT(*)
    FROM dbo.InventoryBalance_Current b
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID;

    SELECT b.Kho_ID,
           k.Ten_Kho,
           b.San_Pham_ID,
           p.Ma_San_Pham,
           p.Ten_San_Pham,
           CAST(0 AS DECIMAL(18,3)) AS SL_Dau_Ky,
           CAST(0 AS DECIMAL(18,3)) AS SL_Nhap,
           CAST(0 AS DECIMAL(18,3)) AS SL_Xuat,
           b.CurrentQuantity AS SL_Cuoi_Ky,
           b.CurrentQuantity AS SL_Ton_Thuc_Te,
           b.ReservedQuantity AS SL_Dang_Giu,
           CAST(b.CurrentQuantity - b.ReservedQuantity AS DECIMAL(18,3)) AS SL_Kha_Dung
    INTO #CurrentReportPage
    FROM dbo.InventoryBalance_Current b
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = b.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = b.San_Pham_ID
    /* The clustered balance key supplies deterministic, seek-friendly order.
       Sorting by display names would reintroduce a large workspace grant. */
    ORDER BY b.Kho_ID, b.San_Pham_ID
    OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;

    SELECT @Current_Report_Generation_End = Generation
    FROM dbo.Inventory_Current_Report_State
    WHERE State_ID = 1;
    IF @Current_Report_Generation_End IS NULL
        THROW 51325, N'Generation báo cáo tồn kho hiện tại chưa được khởi tạo.', 1;
    IF @Current_Report_Generation_End <> @Current_Report_Generation_Start
        THROW 51324, N'Báo cáo tồn kho hiện tại đã thay đổi trong lúc đọc; vui lòng thử lại.', 1;

    SELECT @Total_Count AS Total_Count;
    SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham,
           SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky, SL_Ton_Thuc_Te, SL_Dang_Giu, SL_Kha_Dung
    FROM #CurrentReportPage
    ORDER BY Kho_ID, San_Pham_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay
        THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END
    BEGIN TRY
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventoryMovement_AggregateState
        WHERE State_ID = 1 AND IsInitialized = 1
    )
        THROW 51221, N'Movement Aggregate chưa được khởi tạo. Hãy chạy sp_Inventory_Movement_Bootstrap_From_Ledger trước khi xem báo cáo.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventoryBalance_Daily_AggregateState
        WHERE State_ID = 1 AND IsInitialized = 1
    )
        THROW 51230, N'Balance Daily chưa được khởi tạo. Hãy chạy sp_Inventory_Balance_Daily_Bootstrap_From_Movement trước khi xem báo cáo.', 1;

    DECLARE @Is_Current_Report BIT = IIF(@Den_Ngay = CONVERT(DATE, SYSDATETIME()), 1, 0);

    DECLARE @Catalog_Scope_Count BIGINT, @Catalog_Max_ID BIGINT;
    EXEC dbo.sp_Inventory_Report_Acquire_Scope_Fence
        @Tu_Ngay = @Tu_Ngay,
        @Den_Ngay = @Den_Ngay,
        @Ma_Dang_Nhap = @Ma_Dang_Nhap,
        @Kho_ID = @Kho_ID,
        @Is_Current_Report = @Is_Current_Report,
        @Catalog_Scope_Count = @Catalog_Scope_Count OUTPUT,
        @Catalog_Max_ID = @Catalog_Max_ID OUTPUT;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.InventoryMovement_RebuildQueue q
        JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = q.Kho_ID
        WHERE q.Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL')
          AND ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
          AND (@Kho_ID IS NULL OR q.Kho_ID = @Kho_ID)
          AND q.From_Date <= @Den_Ngay
    )
        THROW 51222, N'Movement Aggregate của báo cáo đang tái tạo. Vui lòng thử lại sau khi worker hoàn tất.', 1;

    DECLARE @Total_Count INT;
    ;WITH AuthorizedScope AS
    (
        SELECT s.Kho_ID, s.San_Pham_ID
        FROM dbo.Inventory_Balance_Daily_Scope s
        JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
        WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
          AND (@Kho_ID IS NULL OR s.Kho_ID = @Kho_ID)
          AND s.First_Balance_Date <= @Den_Ngay
    )
    SELECT @Total_Count = COUNT(*)
    FROM AuthorizedScope s
    CROSS APPLY
    (
        SELECT TOP (1) b.Balance_Date
        FROM dbo.Inventory_Balance_Daily b WITH (INDEX(IX_Inventory_Balance_Daily_Scope))
        WHERE b.Kho_ID = s.Kho_ID
          AND b.San_Pham_ID = s.San_Pham_ID
          AND b.Balance_Date <= @Den_Ngay
          AND b.IsValid = 1
        ORDER BY b.Balance_Date DESC
    ) ending;

    ;WITH AuthorizedScope AS
    (
        SELECT s.Kho_ID, s.San_Pham_ID
        FROM dbo.Inventory_Balance_Daily_Scope s
        JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
        WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
          AND (@Kho_ID IS NULL OR s.Kho_ID = @Kho_ID)
          AND s.First_Balance_Date <= @Den_Ngay
    )
    SELECT s.Kho_ID,
           k.Ten_Kho,
           s.San_Pham_ID,
           p.Ma_San_Pham,
           p.Ten_San_Pham,
           /* A missing pre-period row means there is no materialized balance
              before @Tu_Ngay.  ending.OpeningQuantity belongs to the latest
              movement day and must never seed the report opening. */
           CAST(COALESCE(opening.ClosingQuantity, 0) AS DECIMAL(18,3)) AS SL_Dau_Ky,
           CAST(ending.CumulativeReceived - ISNULL(opening.CumulativeReceived, 0) AS DECIMAL(18,3)) AS SL_Nhap,
           CAST(ending.CumulativeIssued - ISNULL(opening.CumulativeIssued, 0) AS DECIMAL(18,3)) AS SL_Xuat,
           /* SL_Cuoi_Ky is a period value.  CurrentQuantity remains available
              only through the separate current/on-hand fields below. */
           CAST(ending.ClosingQuantity AS DECIMAL(18,3)) AS SL_Cuoi_Ky,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(currentBalance.CurrentQuantity, 0) ELSE ending.ClosingQuantity END AS DECIMAL(18,3)) AS SL_Ton_Thuc_Te,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(currentBalance.ReservedQuantity, 0) ELSE 0 END AS DECIMAL(18,3)) AS SL_Dang_Giu,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(currentBalance.CurrentQuantity, 0) - ISNULL(currentBalance.ReservedQuantity, 0) ELSE ending.ClosingQuantity END AS DECIMAL(18,3)) AS SL_Kha_Dung
    INTO #ReportPage
    FROM AuthorizedScope s
    CROSS APPLY
    (
        SELECT TOP (1)
               b.OpeningQuantity,
               b.ClosingQuantity,
               b.CumulativeReceived,
               b.CumulativeIssued
        FROM dbo.Inventory_Balance_Daily b WITH (INDEX(IX_Inventory_Balance_Daily_Scope))
        WHERE b.Kho_ID = s.Kho_ID
          AND b.San_Pham_ID = s.San_Pham_ID
          AND b.Balance_Date <= @Den_Ngay
          AND b.IsValid = 1
        ORDER BY b.Balance_Date DESC
    ) ending
    OUTER APPLY
    (
        SELECT TOP (1)
               b.ClosingQuantity,
               b.CumulativeReceived,
               b.CumulativeIssued
        FROM dbo.Inventory_Balance_Daily b WITH (INDEX(IX_Inventory_Balance_Daily_Scope))
        WHERE b.Kho_ID = s.Kho_ID
          AND b.San_Pham_ID = s.San_Pham_ID
          AND b.Balance_Date < @Tu_Ngay
          AND b.IsValid = 1
        ORDER BY b.Balance_Date DESC
    ) opening
    LEFT JOIN dbo.InventoryBalance_Current currentBalance
      ON currentBalance.Kho_ID = s.Kho_ID
     AND currentBalance.San_Pham_ID = s.San_Pham_ID
     AND @Is_Current_Report = 1
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = s.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = s.San_Pham_ID
    ORDER BY k.Ten_Kho, p.Ma_San_Pham
    OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;

    DECLARE @Current_Catalog_Scope_Count BIGINT, @Current_Catalog_Max_ID BIGINT;
    SELECT @Current_Catalog_Scope_Count = COUNT_BIG(*),
           @Current_Catalog_Max_ID = COALESCE(MAX(c.Catalog_ID), 0)
    FROM dbo.Inventory_Report_Scope_Catalog c
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = c.Kho_ID
    WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR c.Kho_ID = @Kho_ID)
      AND (@Is_Current_Report = 1 OR c.First_Posted_Date <= @Den_Ngay);

    IF @Current_Catalog_Scope_Count <> @Catalog_Scope_Count
       OR @Current_Catalog_Max_ID <> @Catalog_Max_ID
        THROW 51324, N'Phạm vi báo cáo đã thay đổi trong lúc đọc; vui lòng thử lại.', 1;

    SELECT @Total_Count AS Total_Count;
    SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham,
           SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky, SL_Ton_Thuc_Te, SL_Dang_Giu, SL_Kha_Dung
    FROM #ReportPage
    ORDER BY Ten_Kho, Ma_San_Pham;
    IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

SET QUOTED_IDENTIFIER ON;
GO

/* Queue only a narrow daily scope during Post. Recalculation from the ledger is
   intentionally deferred to the worker so document posting stays short. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Movement_Apply_Invalidation
    @Affected dbo.InventoryMovementAffectedType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM @Affected) RETURN;

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
END
GO

/* A worker may complete only the version it claimed.  A newer request keeps
   the same row live and prevents stale output from being considered current. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Movement_Complete_Claim
    @Queue_ID BIGINT,
    @Claimed_Version INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Queue_ID IS NULL OR @Claimed_Version IS NULL
        THROW 51227, N'Queue claim không hợp lệ.', 1;

    UPDATE q
    SET Status = CASE WHEN q.Requested_Version = @Claimed_Version THEN N'COMPLETED' ELSE N'WAITING' END,
        Retry_Count = CASE WHEN q.Requested_Version = @Claimed_Version THEN q.Retry_Count ELSE 0 END,
        ProcessedAt = CASE WHEN q.Requested_Version = @Claimed_Version THEN SYSUTCDATETIME() ELSE NULL END,
        NextRetryAt = NULL,
        LastError = NULL,
        ErrorMessage = NULL
    FROM dbo.InventoryMovement_RebuildQueue q WITH (UPDLOCK, HOLDLOCK)
    WHERE q.ID = @Queue_ID
      AND q.Status = N'PROCESSING'
      AND q.Claimed_Version = @Claimed_Version;

    IF @@ROWCOUNT <> 1
        THROW 51229, N'Queue claim không còn hợp lệ hoặc đã bị worker khác hoàn tất.', 1;
END
GO

/* Build the historical report read model only in the asynchronous worker.
   The optional lock flag is internal: movement rebuild already owns the same
   transaction-scoped applock for the complete movement-to-balance cutover. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Balance_Daily_Rebuild
    @Kho_ID BIGINT,
    @San_Pham_ID BIGINT,
    @From_Date DATE,
    @Scope_Lock_Held BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Kho_ID IS NULL OR @San_Pham_ID IS NULL OR @From_Date IS NULL
        THROW 51231, N'Kho, sản phẩm và ngày rebuild Balance Daily là bắt buộc.', 1;

    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END

    BEGIN TRY
        IF @Scope_Lock_Held = 0
        BEGIN
            DECLARE @StandaloneLockResult INT;
            DECLARE @StandaloneLockResource NVARCHAR(255) = CONCAT(N'InventoryMovement:', @Kho_ID, N':', @San_Pham_ID);
            EXEC @StandaloneLockResult = sys.sp_getapplock
                @Resource = @StandaloneLockResource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 0;
            IF @StandaloneLockResult < 0
                THROW 51224, N'Movement Aggregate scope đang được rebuild bởi worker khác.', 1;
        END

        DECLARE @Base_Balance_Date DATE = NULL;
        DECLARE @Base_Closing DECIMAL(18,3) = 0;
        DECLARE @Base_Cumulative_Received DECIMAL(18,3) = 0;
        DECLARE @Base_Cumulative_Issued DECIMAL(18,3) = 0;
        DECLARE @Anchor_Date DATE = NULL;
        DECLARE @Anchor_Closing DECIMAL(18,3) = 0;
        DECLARE @Anchor_Cumulative_Received DECIMAL(18,3) = 0;
        DECLARE @Anchor_Cumulative_Issued DECIMAL(18,3) = 0;

        SELECT TOP (1)
               @Base_Balance_Date = b.Balance_Date,
               @Base_Closing = b.ClosingQuantity,
               @Base_Cumulative_Received = b.CumulativeReceived,
               @Base_Cumulative_Issued = b.CumulativeIssued
        FROM dbo.Inventory_Balance_Daily b WITH (UPDLOCK, HOLDLOCK, INDEX(IX_Inventory_Balance_Daily_Scope))
        WHERE b.Kho_ID = @Kho_ID
          AND b.San_Pham_ID = @San_Pham_ID
          AND b.Balance_Date < @From_Date
          AND b.IsValid = 1
        ORDER BY b.Balance_Date DESC;

        /* A valid snapshot is only a bootstrap anchor.  Once a balance row is
           materialized, later report reads no longer need to inspect snapshots. */
        IF @Base_Balance_Date IS NULL
        BEGIN
            SELECT TOP (1)
                   @Anchor_Date = s.Snapshot_Date,
                   @Anchor_Closing = s.ClosingQuantity
            FROM dbo.InventoryBalance_Snapshot_Daily s WITH (UPDLOCK, HOLDLOCK)
            WHERE s.Kho_ID = @Kho_ID
              AND s.San_Pham_ID = @San_Pham_ID
              AND s.Snapshot_Date < @From_Date
              AND s.IsValid = 1
            ORDER BY s.Snapshot_Date DESC;

            IF @Anchor_Date IS NULL
                THROW 51232, N'Balance Daily chưa được bootstrap: không có Daily prefix hoặc Snapshot hợp lệ làm anchor.', 1;

            /* Snapshot stores closing only. Reconstruct cumulative totals at the
               anchor, then bridge every valid movement strictly between anchor
               and @From_Date before the first rebuilt day is calculated. */
            SELECT @Anchor_Cumulative_Received = COALESCE(SUM(d.Total_Receipt), 0),
                   @Anchor_Cumulative_Issued = COALESCE(SUM(d.Total_Issue), 0)
            FROM dbo.Inventory_Movement_Daily d
            WHERE d.Kho_ID = @Kho_ID
              AND d.San_Pham_ID = @San_Pham_ID
              AND d.Movement_Date <= @Anchor_Date
              AND d.IsValid = 1;

            SET @Base_Balance_Date = @Anchor_Date;
            SET @Base_Closing = @Anchor_Closing;
            SET @Base_Cumulative_Received = @Anchor_Cumulative_Received;
            SET @Base_Cumulative_Issued = @Anchor_Cumulative_Issued;
        END

        DECLARE @Bridge_Received DECIMAL(18,3) = 0;
        DECLARE @Bridge_Issued DECIMAL(18,3) = 0;
        SELECT @Bridge_Received = COALESCE(SUM(d.Total_Receipt), 0),
               @Bridge_Issued = COALESCE(SUM(d.Total_Issue), 0)
        FROM dbo.Inventory_Movement_Daily d
        WHERE d.Kho_ID = @Kho_ID
          AND d.San_Pham_ID = @San_Pham_ID
          AND d.Movement_Date > @Base_Balance_Date
          AND d.Movement_Date < @From_Date
          AND d.IsValid = 1;

        SET @Base_Closing = @Base_Closing + @Bridge_Received - @Bridge_Issued;
        SET @Base_Cumulative_Received = @Base_Cumulative_Received + @Bridge_Received;
        SET @Base_Cumulative_Issued = @Base_Cumulative_Issued + @Bridge_Issued;

        IF @Anchor_Date IS NOT NULL
        BEGIN
            INSERT dbo.Inventory_Balance_Daily
            (
                Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity,
                TotalReceived, TotalIssued, ClosingQuantity,
                CumulativeReceived, CumulativeIssued, IsValid
            )
            SELECT @Anchor_Date, @Kho_ID, @San_Pham_ID, @Anchor_Closing,
                   0, 0, @Anchor_Closing,
                   @Anchor_Cumulative_Received, @Anchor_Cumulative_Issued, 1
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.Inventory_Balance_Daily b WITH (UPDLOCK, HOLDLOCK)
                WHERE b.Balance_Date = @Anchor_Date
                  AND b.Kho_ID = @Kho_ID
                  AND b.San_Pham_ID = @San_Pham_ID
            );
        END

        CREATE TABLE #RebuiltBalance
        (
            Balance_Date DATE NOT NULL PRIMARY KEY,
            OpeningQuantity DECIMAL(18,3) NOT NULL,
            TotalReceived DECIMAL(18,3) NOT NULL,
            TotalIssued DECIMAL(18,3) NOT NULL,
            ClosingQuantity DECIMAL(18,3) NOT NULL,
            CumulativeReceived DECIMAL(18,3) NOT NULL,
            CumulativeIssued DECIMAL(18,3) NOT NULL
        );

        ;WITH MovementRows AS
        (
            SELECT d.Movement_Date,
                   d.Total_Receipt,
                   d.Total_Issue
            FROM dbo.Inventory_Movement_Daily d
            WHERE d.Kho_ID = @Kho_ID
              AND d.San_Pham_ID = @San_Pham_ID
              AND d.Movement_Date >= @From_Date
              AND d.IsValid = 1
        )
        INSERT #RebuiltBalance
        (
            Balance_Date, OpeningQuantity, TotalReceived, TotalIssued,
            ClosingQuantity, CumulativeReceived, CumulativeIssued
        )
        SELECT m.Movement_Date,
               CAST(@Base_Closing + ISNULL(SUM(m.Total_Receipt - m.Total_Issue) OVER (ORDER BY m.Movement_Date ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS DECIMAL(18,3)),
               m.Total_Receipt,
               m.Total_Issue,
               CAST(@Base_Closing + SUM(m.Total_Receipt - m.Total_Issue) OVER (ORDER BY m.Movement_Date ROWS UNBOUNDED PRECEDING) AS DECIMAL(18,3)),
               CAST(@Base_Cumulative_Received + SUM(m.Total_Receipt) OVER (ORDER BY m.Movement_Date ROWS UNBOUNDED PRECEDING) AS DECIMAL(18,3)),
               CAST(@Base_Cumulative_Issued + SUM(m.Total_Issue) OVER (ORDER BY m.Movement_Date ROWS UNBOUNDED PRECEDING) AS DECIMAL(18,3))
        FROM MovementRows m;

        /* Every later materialized day in this scope depends on this date.  The
           scope applock serializes overlapping workers, so replacing only this
           suffix cannot overwrite a newer concurrent rebuild. */
        DELETE b
        FROM dbo.Inventory_Balance_Daily b
        WHERE b.Kho_ID = @Kho_ID
          AND b.San_Pham_ID = @San_Pham_ID
          AND b.Balance_Date >= @From_Date;

        INSERT dbo.Inventory_Balance_Daily
        (
            Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity,
            TotalReceived, TotalIssued, ClosingQuantity,
            CumulativeReceived, CumulativeIssued, IsValid
        )
        SELECT r.Balance_Date, @Kho_ID, @San_Pham_ID, r.OpeningQuantity,
               r.TotalReceived, r.TotalIssued, r.ClosingQuantity,
               r.CumulativeReceived, r.CumulativeIssued, 1
        FROM #RebuiltBalance r;

        DECLARE @First_Balance_Date DATE;
        DECLARE @Last_Balance_Date DATE;
        SELECT @First_Balance_Date = MIN(b.Balance_Date),
               @Last_Balance_Date = MAX(b.Balance_Date)
        FROM dbo.Inventory_Balance_Daily b WITH (UPDLOCK, HOLDLOCK)
        WHERE b.Kho_ID = @Kho_ID
          AND b.San_Pham_ID = @San_Pham_ID
          AND b.IsValid = 1;

        IF @First_Balance_Date IS NULL
            DELETE FROM dbo.Inventory_Balance_Daily_Scope
            WHERE Kho_ID = @Kho_ID AND San_Pham_ID = @San_Pham_ID;
        ELSE
        BEGIN
            UPDATE dbo.Inventory_Balance_Daily_Scope
            SET First_Balance_Date = @First_Balance_Date,
                Last_Balance_Date = @Last_Balance_Date,
                [Version] = [Version] + 1,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Kho_ID = @Kho_ID AND San_Pham_ID = @San_Pham_ID;

            IF @@ROWCOUNT = 0
                INSERT dbo.Inventory_Balance_Daily_Scope
                (Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date)
                VALUES (@Kho_ID, @San_Pham_ID, @First_Balance_Date, @Last_Balance_Date);
        END

        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* Controlled initial cutover.  The procedure is intentionally separate from
   deployment so a production-sized backfill is scheduled explicitly. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Balance_Daily_Bootstrap_From_Movement
    @Bootstrap_Lock_Held BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Bootstrap_Lock_Held = 0 AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventoryMovement_AggregateState
        WHERE State_ID = 1 AND IsInitialized = 1
    )
        THROW 51232, N'Movement Aggregate phải được bootstrap trước Balance Daily.', 1;

    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @OwnTransaction = 1;
    END

    BEGIN TRY
        IF @Bootstrap_Lock_Held = 0
        BEGIN
            DECLARE @BootstrapLockResult INT;
            EXEC @BootstrapLockResult = sys.sp_getapplock
                @Resource = N'InventoryMovement:Bootstrap',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 0;
            IF @BootstrapLockResult < 0
                THROW 51225, N'Balance Daily đang được bootstrap bởi phiên khác.', 1;
        END

        DELETE FROM dbo.Inventory_Balance_Daily_Scope;
        DELETE FROM dbo.Inventory_Balance_Daily;

        ;WITH MovementScope AS
        (
            SELECT d.Kho_ID, d.San_Pham_ID, MIN(d.Movement_Date) AS First_Movement_Date
            FROM dbo.Inventory_Movement_Daily d
            WHERE d.IsValid = 1
            GROUP BY d.Kho_ID, d.San_Pham_ID
        ), Anchors AS
        (
            SELECT ms.Kho_ID,
                   ms.San_Pham_ID,
                   ms.First_Movement_Date,
                   a.Snapshot_Date,
                   a.ClosingQuantity
            FROM MovementScope ms
            OUTER APPLY
            (
                SELECT TOP (1) s.Snapshot_Date, s.ClosingQuantity
                FROM dbo.InventoryBalance_Snapshot_Daily s
                WHERE s.Kho_ID = ms.Kho_ID
                  AND s.San_Pham_ID = ms.San_Pham_ID
                  AND s.Snapshot_Date < ms.First_Movement_Date
                  AND s.IsValid = 1
                ORDER BY s.Snapshot_Date DESC
            ) a
        ), BalanceRows AS
        (
            SELECT d.Movement_Date AS Balance_Date,
                   d.Kho_ID,
                   d.San_Pham_ID,
                   CAST(ISNULL(a.ClosingQuantity, 0) + ISNULL(SUM(d.Total_Receipt - d.Total_Issue) OVER (PARTITION BY d.Kho_ID, d.San_Pham_ID ORDER BY d.Movement_Date ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS DECIMAL(18,3)) AS OpeningQuantity,
                   d.Total_Receipt AS TotalReceived,
                   d.Total_Issue AS TotalIssued,
                   CAST(ISNULL(a.ClosingQuantity, 0) + SUM(d.Total_Receipt - d.Total_Issue) OVER (PARTITION BY d.Kho_ID, d.San_Pham_ID ORDER BY d.Movement_Date ROWS UNBOUNDED PRECEDING) AS DECIMAL(18,3)) AS ClosingQuantity,
                   CAST(SUM(d.Total_Receipt) OVER (PARTITION BY d.Kho_ID, d.San_Pham_ID ORDER BY d.Movement_Date ROWS UNBOUNDED PRECEDING) AS DECIMAL(18,3)) AS CumulativeReceived,
                   CAST(SUM(d.Total_Issue) OVER (PARTITION BY d.Kho_ID, d.San_Pham_ID ORDER BY d.Movement_Date ROWS UNBOUNDED PRECEDING) AS DECIMAL(18,3)) AS CumulativeIssued
            FROM dbo.Inventory_Movement_Daily d
            JOIN Anchors a ON a.Kho_ID = d.Kho_ID AND a.San_Pham_ID = d.San_Pham_ID
            WHERE d.IsValid = 1
        ), AnchorRows AS
        (
            SELECT a.Snapshot_Date AS Balance_Date,
                   a.Kho_ID,
                   a.San_Pham_ID,
                   a.ClosingQuantity AS OpeningQuantity,
                   CAST(0 AS DECIMAL(18,3)) AS TotalReceived,
                   CAST(0 AS DECIMAL(18,3)) AS TotalIssued,
                   a.ClosingQuantity AS ClosingQuantity,
                   CAST(0 AS DECIMAL(18,3)) AS CumulativeReceived,
                   CAST(0 AS DECIMAL(18,3)) AS CumulativeIssued
            FROM Anchors a
            WHERE a.Snapshot_Date IS NOT NULL
        ), SnapshotOnly AS
        (
            SELECT s.Snapshot_Date AS Balance_Date,
                   s.Kho_ID,
                   s.San_Pham_ID,
                   s.ClosingQuantity AS OpeningQuantity,
                   CAST(0 AS DECIMAL(18,3)) AS TotalReceived,
                   CAST(0 AS DECIMAL(18,3)) AS TotalIssued,
                   s.ClosingQuantity AS ClosingQuantity,
                   CAST(0 AS DECIMAL(18,3)) AS CumulativeReceived,
                   CAST(0 AS DECIMAL(18,3)) AS CumulativeIssued
            FROM dbo.InventoryBalance_Snapshot_Daily s
            WHERE s.IsValid = 1
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM MovementScope ms
                  WHERE ms.Kho_ID = s.Kho_ID AND ms.San_Pham_ID = s.San_Pham_ID
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.InventoryBalance_Snapshot_Daily newer
                  WHERE newer.Kho_ID = s.Kho_ID
                    AND newer.San_Pham_ID = s.San_Pham_ID
                    AND newer.IsValid = 1
                    AND newer.Snapshot_Date > s.Snapshot_Date
              )
        )
        INSERT dbo.Inventory_Balance_Daily
        (
            Balance_Date, Kho_ID, San_Pham_ID, OpeningQuantity,
            TotalReceived, TotalIssued, ClosingQuantity,
            CumulativeReceived, CumulativeIssued, IsValid
        )
        SELECT ar.Balance_Date, ar.Kho_ID, ar.San_Pham_ID, ar.OpeningQuantity,
               ar.TotalReceived, ar.TotalIssued, ar.ClosingQuantity,
               ar.CumulativeReceived, ar.CumulativeIssued, 1
        FROM AnchorRows ar
        UNION ALL
        SELECT br.Balance_Date, br.Kho_ID, br.San_Pham_ID, br.OpeningQuantity,
               br.TotalReceived, br.TotalIssued, br.ClosingQuantity,
               br.CumulativeReceived, br.CumulativeIssued, 1
        FROM BalanceRows br
        UNION ALL
        SELECT so.Balance_Date, so.Kho_ID, so.San_Pham_ID, so.OpeningQuantity,
               so.TotalReceived, so.TotalIssued, so.ClosingQuantity,
               so.CumulativeReceived, so.CumulativeIssued, 1
        FROM SnapshotOnly so;

        INSERT dbo.Inventory_Balance_Daily_Scope
        (Kho_ID, San_Pham_ID, First_Balance_Date, Last_Balance_Date)
        SELECT b.Kho_ID, b.San_Pham_ID, MIN(b.Balance_Date), MAX(b.Balance_Date)
        FROM dbo.Inventory_Balance_Daily b
        WHERE b.IsValid = 1
        GROUP BY b.Kho_ID, b.San_Pham_ID;

        UPDATE dbo.InventoryBalance_Daily_AggregateState
        SET IsInitialized = 1,
            InitializedAt = COALESCE(InitializedAt, SYSUTCDATETIME()),
            LastReconciledAt = SYSUTCDATETIME()
        WHERE State_ID = 1;

        IF @OwnTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Movement_Rebuild
    @Kho_ID BIGINT,
    @San_Pham_ID BIGINT,
    @From_Date DATE,
    @To_Date DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @From_Date IS NULL OR @To_Date IS NULL OR @From_Date > @To_Date
        THROW 51223, N'Khoảng ngày rebuild Movement Aggregate không hợp lệ.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @BootstrapLockResult INT;
        EXEC @BootstrapLockResult = sys.sp_getapplock
            @Resource = N'InventoryMovement:Bootstrap',
            @LockMode = N'Shared',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @BootstrapLockResult < 0
            THROW 51226, N'Movement Aggregate đang bảo trì bootstrap. Vui lòng thử lại sau.', 1;

        DECLARE @AppLockResult INT;
        DECLARE @AppLockResource NVARCHAR(255) = CONCAT(N'InventoryMovement:', @Kho_ID, N':', @San_Pham_ID);
        EXEC @AppLockResult = sys.sp_getapplock
            @Resource = @AppLockResource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @AppLockResult < 0
            THROW 51224, N'Movement Aggregate scope đang được rebuild bởi worker khác.', 1;

        CREATE TABLE #Rebuilt
        (
            Movement_Date DATE NOT NULL PRIMARY KEY,
            Total_Receipt DECIMAL(18,3) NOT NULL,
            Total_Issue DECIMAL(18,3) NOT NULL
        );

        INSERT #Rebuilt(Movement_Date, Total_Receipt, Total_Issue)
        SELECT m.Movement_Date,
               SUM(m.Total_Receipt),
               SUM(m.Total_Issue)
        FROM
        (
            SELECT h.Ngay_Nhap_Kho AS Movement_Date,
                   CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Total_Receipt,
                   CAST(0 AS DECIMAL(18,3)) AS Total_Issue
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
              AND h.Kho_ID = @Kho_ID
              AND d.San_Pham_ID = @San_Pham_ID
              AND h.Ngay_Nhap_Kho BETWEEN @From_Date AND @To_Date
            UNION ALL
            SELECT h.Ngay_Xuat_Kho,
                   CAST(0 AS DECIMAL(18,3)),
                   CAST(d.SL_Xuat AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
              AND h.Kho_ID = @Kho_ID
              AND d.San_Pham_ID = @San_Pham_ID
              AND h.Ngay_Xuat_Kho BETWEEN @From_Date AND @To_Date
        ) m
        GROUP BY m.Movement_Date;

        UPDATE d
        SET Total_Receipt = r.Total_Receipt,
            Total_Issue = r.Total_Issue,
            IsValid = 1,
            InvalidatedAt = NULL,
            InvalidReason = NULL,
            [Version] = d.[Version] + 1,
            UpdatedAt = SYSUTCDATETIME()
        FROM dbo.Inventory_Movement_Daily d
        JOIN #Rebuilt r ON r.Movement_Date = d.Movement_Date
        WHERE d.Kho_ID = @Kho_ID AND d.San_Pham_ID = @San_Pham_ID;

        INSERT dbo.Inventory_Movement_Daily
        (Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid)
        SELECT r.Movement_Date, @Kho_ID, @San_Pham_ID, r.Total_Receipt, r.Total_Issue, 1
        FROM #Rebuilt r
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.Inventory_Movement_Daily d WITH (UPDLOCK, HOLDLOCK)
            WHERE d.Movement_Date = r.Movement_Date
              AND d.Kho_ID = @Kho_ID
              AND d.San_Pham_ID = @San_Pham_ID
        );

        /* Only stale days in this requested scope are removed; normal back-date
           rebuilds never clear an aggregate outside their day/range. */
        DELETE d
        FROM dbo.Inventory_Movement_Daily d
        WHERE d.Kho_ID = @Kho_ID
          AND d.San_Pham_ID = @San_Pham_ID
          AND d.Movement_Date BETWEEN @From_Date AND @To_Date
          AND NOT EXISTS (SELECT 1 FROM #Rebuilt r WHERE r.Movement_Date = d.Movement_Date);

        /* The report read model is rebuilt in the same worker transaction and
           under the same scope applock.  Post never writes this table. */
        EXEC dbo.sp_Inventory_Balance_Daily_Rebuild
            @Kho_ID = @Kho_ID,
            @San_Pham_ID = @San_Pham_ID,
            @From_Date = @From_Date,
            @Scope_Lock_Held = 1;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Movement_Process_RebuildQueue
    @Batch_Size INT = 100,
    @Max_Retry_Count INT = 3,
    @Base_Retry_Delay_Seconds INT = 5,
    @Processing_Lease_Seconds INT = 300
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Batch_Size < 1 SET @Batch_Size = 1;
    IF @Max_Retry_Count < 1 SET @Max_Retry_Count = 1;
    IF @Base_Retry_Delay_Seconds < 0 SET @Base_Retry_Delay_Seconds = 0;
    IF @Processing_Lease_Seconds < 1 SET @Processing_Lease_Seconds = 1;

    /* A process may die after claiming a row.  Recover only claims older than
       the lease; an active worker never has its claim stolen.  A crashed claim
       consumes retry budget and can itself become a dead-letter. */
    DECLARE @Recovery_At DATETIME2 = SYSUTCDATETIME();
    BEGIN TRANSACTION;

    UPDATE dbo.InventoryMovement_RebuildQueue
    SET Status = N'FAILED_FINAL',
        Retry_Count = Retry_Count + 1,
        ProcessedAt = @Recovery_At,
        NextRetryAt = NULL,
        LastError = N'Worker claim lease expired before rebuild completion.',
        ErrorMessage = NULL
    WHERE Status = N'PROCESSING'
      AND (LastAttemptAt IS NULL OR LastAttemptAt <= DATEADD(SECOND, -@Processing_Lease_Seconds, @Recovery_At))
      AND Retry_Count + 1 >= @Max_Retry_Count;

    INSERT dbo.InventoryMovement_RebuildDeadLetter
    (Queue_ID, Kho_ID, San_Pham_ID, From_Date, To_Date, Retry_Count, LastError)
    SELECT q.ID, q.Kho_ID, q.San_Pham_ID, q.From_Date, q.To_Date, q.Retry_Count, q.LastError
    FROM dbo.InventoryMovement_RebuildQueue q
    WHERE q.Status = N'FAILED_FINAL'
      AND q.LastError = N'Worker claim lease expired before rebuild completion.'
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.InventoryMovement_RebuildDeadLetter dl WITH (UPDLOCK, HOLDLOCK)
          WHERE dl.Queue_ID = q.ID
      );

    UPDATE dbo.InventoryMovement_RebuildQueue
    SET Status = N'RETRY_WAITING',
        Retry_Count = Retry_Count + 1,
        Claimed_Version = NULL,
        NextRetryAt = @Recovery_At,
        LastError = N'Worker claim lease expired before rebuild completion.',
        ErrorMessage = NULL
    WHERE Status = N'PROCESSING'
      AND (LastAttemptAt IS NULL OR LastAttemptAt <= DATEADD(SECOND, -@Processing_Lease_Seconds, @Recovery_At));

    COMMIT TRANSACTION;

    DECLARE @Processed INT = 0;
    DECLARE @Queue_ID BIGINT;
    DECLARE @Kho_ID BIGINT;
    DECLARE @San_Pham_ID BIGINT;
    DECLARE @From_Date DATE;
    DECLARE @To_Date DATE;
    DECLARE @Claimed_Version INT;

    WHILE @Processed < @Batch_Size
    BEGIN
        SET @Queue_ID = NULL;
        BEGIN TRANSACTION;
        DECLARE @Claimed TABLE
        (
            ID BIGINT NOT NULL,
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            From_Date DATE NOT NULL,
            To_Date DATE NOT NULL,
            Claimed_Version INT NOT NULL
        );

        ;WITH NextItem AS
        (
            SELECT TOP (1) q.*
            FROM dbo.InventoryMovement_RebuildQueue q WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE q.Status = N'WAITING'
               OR (q.Status = N'RETRY_WAITING' AND q.NextRetryAt <= SYSUTCDATETIME())
            ORDER BY CASE WHEN q.Status = N'WAITING' THEN 0 ELSE 1 END,
                     q.NextRetryAt,
                     q.CreatedAt,
                     q.ID
        )
        UPDATE NextItem
        SET Status = N'PROCESSING',
            Claimed_Version = Requested_Version,
            LastAttemptAt = SYSUTCDATETIME(),
            NextRetryAt = NULL,
            ProcessedAt = NULL,
            ErrorMessage = NULL
        OUTPUT inserted.ID,
               inserted.Kho_ID,
               inserted.San_Pham_ID,
               inserted.From_Date,
               inserted.To_Date,
               inserted.Claimed_Version
        INTO @Claimed;

        SELECT TOP (1)
               @Queue_ID = ID,
               @Kho_ID = Kho_ID,
               @San_Pham_ID = San_Pham_ID,
               @From_Date = From_Date,
               @To_Date = To_Date,
               @Claimed_Version = Claimed_Version
        FROM @Claimed;

        IF @Queue_ID IS NULL
        BEGIN
            COMMIT TRANSACTION;
            BREAK;
        END

        UPDATE dbo.InventoryMovement_RebuildQueue
        SET Status = N'PROCESSING', ErrorMessage = NULL
        WHERE ID = @Queue_ID;
        COMMIT TRANSACTION;

        BEGIN TRY
            EXEC dbo.sp_Inventory_Movement_Rebuild
                @Kho_ID = @Kho_ID,
                @San_Pham_ID = @San_Pham_ID,
                @From_Date = @From_Date,
                @To_Date = @To_Date;

            EXEC dbo.sp_Inventory_Movement_Complete_Claim
                @Queue_ID = @Queue_ID,
                @Claimed_Version = @Claimed_Version;
        END TRY
        BEGIN CATCH
            DECLARE @Error_Number INT = ERROR_NUMBER();
            DECLARE @Error_Message NVARCHAR(4000) = LEFT(ERROR_MESSAGE(), 4000);
            DECLARE @Retry_After INT;
            DECLARE @Is_Transient BIT = CASE WHEN @Error_Number IN (1205, 1222, 51224, 51226) THEN 1 ELSE 0 END;
            DECLARE @Next_Status NVARCHAR(20);
            DECLARE @Delay_Seconds INT;

            BEGIN TRANSACTION;
            SELECT @Retry_After = Retry_Count + 1
            FROM dbo.InventoryMovement_RebuildQueue WITH (UPDLOCK, HOLDLOCK)
            WHERE ID = @Queue_ID
              AND Status = N'PROCESSING'
              AND Claimed_Version = @Claimed_Version;

            IF @Retry_After IS NOT NULL
            BEGIN
                SET @Next_Status = CASE WHEN @Is_Transient = 1 AND @Retry_After < @Max_Retry_Count THEN N'RETRY_WAITING' ELSE N'FAILED_FINAL' END;
                SET @Delay_Seconds = CASE
                    WHEN @Next_Status <> N'RETRY_WAITING' THEN NULL
                    WHEN @Base_Retry_Delay_Seconds = 0 THEN 0
                    WHEN @Base_Retry_Delay_Seconds * POWER(CAST(2 AS FLOAT), @Retry_After - 1) > 3600 THEN 3600
                    ELSE CONVERT(INT, @Base_Retry_Delay_Seconds * POWER(CAST(2 AS FLOAT), @Retry_After - 1))
                END;

                UPDATE dbo.InventoryMovement_RebuildQueue
                SET Status = @Next_Status,
                    Retry_Count = @Retry_After,
                    ProcessedAt = CASE WHEN @Next_Status = N'FAILED_FINAL' THEN SYSUTCDATETIME() ELSE NULL END,
                    NextRetryAt = CASE WHEN @Next_Status = N'RETRY_WAITING' THEN DATEADD(SECOND, @Delay_Seconds, SYSUTCDATETIME()) ELSE NULL END,
                    LastError = @Error_Message,
                    ErrorMessage = NULL
                WHERE ID = @Queue_ID
                  AND Status = N'PROCESSING'
                  AND Claimed_Version = @Claimed_Version;

                IF @Next_Status = N'FAILED_FINAL'
                    INSERT dbo.InventoryMovement_RebuildDeadLetter
                    (Queue_ID, Kho_ID, San_Pham_ID, From_Date, To_Date, Retry_Count, LastError)
                    SELECT q.ID, q.Kho_ID, q.San_Pham_ID, q.From_Date, q.To_Date, q.Retry_Count, q.LastError
                    FROM dbo.InventoryMovement_RebuildQueue q
                    WHERE q.ID = @Queue_ID
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.InventoryMovement_RebuildDeadLetter dl WITH (UPDLOCK, HOLDLOCK)
                          WHERE dl.Queue_ID = q.ID
                      );
            END
            COMMIT TRANSACTION;
        END CATCH

        SET @Processed += 1;
    END
END
GO

/* One controlled cutover/reconciliation from the authoritative ledger. This is
   never called by Post or by the report path. Run it once after deployment. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Movement_Bootstrap_From_Ledger
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @AppLockResult INT;
        EXEC @AppLockResult = sys.sp_getapplock
            @Resource = N'InventoryMovement:Bootstrap',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @AppLockResult < 0
            THROW 51225, N'Movement Aggregate đang được bootstrap bởi phiên khác.', 1;

        CREATE TABLE #Rebuilt
        (
            Movement_Date DATE NOT NULL,
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            Total_Receipt DECIMAL(18,3) NOT NULL,
            Total_Issue DECIMAL(18,3) NOT NULL,
            PRIMARY KEY (Movement_Date, Kho_ID, San_Pham_ID)
        );

        INSERT #Rebuilt(Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue)
        SELECT m.Movement_Date, m.Kho_ID, m.San_Pham_ID,
               SUM(m.Total_Receipt), SUM(m.Total_Issue)
        FROM
        (
            SELECT h.Ngay_Nhap_Kho AS Movement_Date, h.Kho_ID, d.San_Pham_ID,
                   CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Total_Receipt,
                   CAST(0 AS DECIMAL(18,3)) AS Total_Issue
            FROM dbo.tbl_XNK_Nhap_Kho h
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
            UNION ALL
            SELECT h.Ngay_Xuat_Kho, h.Kho_ID, d.San_Pham_ID,
                   CAST(0 AS DECIMAL(18,3)), CAST(d.SL_Xuat AS DECIMAL(18,3))
            FROM dbo.tbl_XNK_Xuat_Kho h
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Is_Posted = 1
        ) m
        GROUP BY m.Movement_Date, m.Kho_ID, m.San_Pham_ID;

        UPDATE d
        SET Total_Receipt = r.Total_Receipt,
            Total_Issue = r.Total_Issue,
            IsValid = 1,
            InvalidatedAt = NULL,
            InvalidReason = NULL,
            [Version] = d.[Version] + 1,
            UpdatedAt = SYSUTCDATETIME()
        FROM dbo.Inventory_Movement_Daily d
        JOIN #Rebuilt r ON r.Movement_Date = d.Movement_Date
                       AND r.Kho_ID = d.Kho_ID
                       AND r.San_Pham_ID = d.San_Pham_ID;

        INSERT dbo.Inventory_Movement_Daily
        (Movement_Date, Kho_ID, San_Pham_ID, Total_Receipt, Total_Issue, IsValid)
        SELECT r.Movement_Date, r.Kho_ID, r.San_Pham_ID, r.Total_Receipt, r.Total_Issue, 1
        FROM #Rebuilt r
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.Inventory_Movement_Daily d WITH (UPDLOCK, HOLDLOCK)
            WHERE d.Movement_Date = r.Movement_Date
              AND d.Kho_ID = r.Kho_ID
              AND d.San_Pham_ID = r.San_Pham_ID
        );

        /* This full reconciliation is the only operation allowed to remove
           stale aggregate rows across all dates. */
        DELETE d
        FROM dbo.Inventory_Movement_Daily d
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM #Rebuilt r
            WHERE r.Movement_Date = d.Movement_Date
              AND r.Kho_ID = d.Kho_ID
               AND r.San_Pham_ID = d.San_Pham_ID
        );

        /* Bootstrap publishes the balance read model before marking the
           movement aggregate healthy, so report cutover cannot expose a
           partially backfilled balance table. */
        EXEC dbo.sp_Inventory_Balance_Daily_Bootstrap_From_Movement
            @Bootstrap_Lock_Held = 1;

        /* Bootstrap has rebuilt the authoritative ledger while the exclusive
           maintenance lock excluded Post and normal workers.  Any prior retry
           or dead-letter is therefore resolved by this reconciliation. */
        UPDATE dbo.InventoryMovement_RebuildQueue
        SET Status = N'COMPLETED',
            ProcessedAt = SYSUTCDATETIME(),
            NextRetryAt = NULL,
            LastError = NULL,
            ErrorMessage = NULL
        WHERE Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL');

        UPDATE dbo.InventoryMovement_RebuildDeadLetter
        SET ResolvedAt = SYSUTCDATETIME(),
            ResolutionNote = N'Resolved by full movement aggregate bootstrap.'
        WHERE ResolvedAt IS NULL;

        UPDATE dbo.InventoryMovement_AggregateState
        SET IsInitialized = 1,
            InitializedAt = COALESCE(InitializedAt, SYSUTCDATETIME()),
            LastReconciledAt = SYSUTCDATETIME()
        WHERE State_ID = 1;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
