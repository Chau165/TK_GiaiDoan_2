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

    /* Coalesce requests already covered by an earlier active request.  A new
       earlier request is retained so the worker rebuilds from the earliest
       affected date; the filtered unique index prevents duplicate active rows
       for the same scope/date under normal concurrent posting. */
    INSERT dbo.InventorySnapshot_RebuildQueue
    (
        Kho_ID, San_Pham_ID, From_Date, Status, CreatedAt, CompletedAt, ErrorMessage
    )
    SELECT a.Kho_ID, a.San_Pham_ID, a.From_Date, N'WAITING', @InvalidatedAt, NULL, NULL
    FROM #AffectedScope a
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventorySnapshot_RebuildQueue q WITH (UPDLOCK, HOLDLOCK)
        WHERE q.Kho_ID = a.Kho_ID
          AND q.San_Pham_ID = a.San_Pham_ID
          AND q.Status IN (N'WAITING', N'PROCESSING')
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

CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Process_RebuildQueue
    @Batch_Size INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    IF @Batch_Size IS NULL OR @Batch_Size < 1
        THROW 51303, N'Batch size rebuild phải lớn hơn 0.', 1;

    DECLARE @LockResult INT;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'InventorySnapshotRebuildWorker',
        @LockMode = N'Exclusive',
        @LockOwner = N'Session',
        @LockTimeout = 0;
    IF @LockResult < 0
        THROW 51304, N'Worker rebuild snapshot đang được xử lý bởi tiến trình khác.', 1;

    BEGIN TRY
        /* A worker crash can leave PROCESSING rows behind. The application
           lock guarantees no worker is active while these rows are recovered. */
        UPDATE dbo.InventorySnapshot_RebuildQueue
           SET Status = N'WAITING',
               ErrorMessage = N'Recovered by rebuild worker after an interrupted run.'
        WHERE Status = N'PROCESSING';

        CREATE TABLE #Claimed
        (
            ID BIGINT NOT NULL PRIMARY KEY,
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            From_Date DATE NOT NULL
        );

        ;WITH NextItems AS
        (
            SELECT TOP (@Batch_Size) ID, Kho_ID, San_Pham_ID, From_Date,
                   Status, ErrorMessage, CompletedAt
            FROM dbo.InventorySnapshot_RebuildQueue WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE Status = N'WAITING'
            ORDER BY CreatedAt, ID
        )
        UPDATE NextItems
           SET Status = N'PROCESSING',
               ErrorMessage = NULL,
               CompletedAt = NULL
        OUTPUT inserted.ID, inserted.Kho_ID, inserted.San_Pham_ID, inserted.From_Date
        INTO #Claimed(ID, Kho_ID, San_Pham_ID, From_Date);

        DECLARE @QueueId BIGINT;
        DECLARE @Kho_ID BIGINT;
        DECLARE @San_Pham_ID BIGINT;
        DECLARE @From_Date DATE;

        DECLARE QueueCursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT ID, Kho_ID, San_Pham_ID, From_Date
            FROM #Claimed
            ORDER BY ID;

        OPEN QueueCursor;
        FETCH NEXT FROM QueueCursor INTO @QueueId, @Kho_ID, @San_Pham_ID, @From_Date;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            BEGIN TRY
                EXEC dbo.sp_Inventory_Snapshot_Rebuild
                    @Kho_ID = @Kho_ID,
                    @San_Pham_ID = @San_Pham_ID,
                    @From_Date = @From_Date;

                UPDATE dbo.InventorySnapshot_RebuildQueue
                   SET Status = N'COMPLETED',
                       CompletedAt = SYSUTCDATETIME(),
                       ErrorMessage = NULL
                WHERE ID = @QueueId;
            END TRY
            BEGIN CATCH
                UPDATE dbo.InventorySnapshot_RebuildQueue
                   SET Status = N'FAILED',
                       CompletedAt = SYSUTCDATETIME(),
                       ErrorMessage = LEFT(ERROR_MESSAGE(), 4000)
                WHERE ID = @QueueId;
            END CATCH;

            FETCH NEXT FROM QueueCursor INTO @QueueId, @Kho_ID, @San_Pham_ID, @From_Date;
        END
        CLOSE QueueCursor;
        DEALLOCATE QueueCursor;

        EXEC sys.sp_releaseapplock
            @Resource = N'InventorySnapshotRebuildWorker',
            @LockOwner = N'Session';
    END TRY
    BEGIN CATCH
        IF CURSOR_STATUS('local', 'QueueCursor') >= 0 CLOSE QueueCursor;
        IF CURSOR_STATUS('local', 'QueueCursor') >= -1 DEALLOCATE QueueCursor;
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
    IF @@TRANCOUNT = 0 BEGIN TRANSACTION; SET @OwnTransaction = 1;
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

/* Posted ledger data is immutable outside the canonical Post transaction.
   Draft CRUD remains unchanged; direct UPDATE/DELETE of posted headers or
   details is rejected before it can desynchronize current balance, snapshots,
   and Movement Daily.  The Post procedure marks its own short transaction via
   SESSION_CONTEXT and clears it before returning a pooled connection. */
CREATE OR ALTER TRIGGER dbo.tr_Inventory_Movement_Guard_Receipt_Posted_Header
ON dbo.tbl_XNK_Nhap_Kho
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRY_CONVERT(BIT, SESSION_CONTEXT(N'InventoryMovement:ManagedPost')) = 1 RETURN;
    IF EXISTS (SELECT 1 FROM inserted WHERE Is_Posted = 1)
       OR EXISTS (SELECT 1 FROM deleted WHERE Is_Posted = 1)
        THROW 51228, N'Không được UPDATE hoặc DELETE phiếu nhập đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Movement_Guard_Issue_Posted_Header
ON dbo.tbl_XNK_Xuat_Kho
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRY_CONVERT(BIT, SESSION_CONTEXT(N'InventoryMovement:ManagedPost')) = 1 RETURN;
    IF EXISTS (SELECT 1 FROM inserted WHERE Is_Posted = 1)
       OR EXISTS (SELECT 1 FROM deleted WHERE Is_Posted = 1)
        THROW 51228, N'Không được UPDATE hoặc DELETE phiếu xuất đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Movement_Guard_Receipt_Posted_Detail
ON dbo.tbl_XNK_Nhap_Kho_Raw_Data
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRY_CONVERT(BIT, SESSION_CONTEXT(N'InventoryMovement:ManagedPost')) = 1 RETURN;
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
        THROW 51228, N'Không được UPDATE hoặc DELETE chi tiết phiếu nhập đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.tr_Inventory_Movement_Guard_Issue_Posted_Detail
ON dbo.tbl_XNK_Xuat_Kho_Raw_Data
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRY_CONVERT(BIT, SESSION_CONTEXT(N'InventoryMovement:ManagedPost')) = 1 RETURN;
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
        THROW 51228, N'Không được UPDATE hoặc DELETE chi tiết phiếu xuất đã Post trực tiếp. Hãy dùng stored procedure nghiệp vụ.', 1;
END
GO

/* Snapshot lifecycle procedures are defined once in this canonical deployment
   bundle. Keep future changes in this definition instead of appending overrides. */
CREATE OR ALTER PROCEDURE dbo.sp_Inventory_Snapshot_Create_Daily
    @Snapshot_Date DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Snapshot_Date IS NULL
        THROW 51300, N'Ngày snapshot không được để trống.', 1;

    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE s
           SET ClosingQuantity = b.CurrentQuantity,
               IsValid = 1,
               InvalidatedAt = NULL,
               InvalidReason = NULL,
               [Version] = ISNULL(s.[Version], 0) + 1
        FROM dbo.InventoryBalance_Snapshot_Daily s
        JOIN dbo.InventoryBalance_Current b
          ON b.Kho_ID = s.Kho_ID
         AND b.San_Pham_ID = s.San_Pham_ID
        WHERE s.Snapshot_Date = @Snapshot_Date;

        INSERT dbo.InventoryBalance_Snapshot_Daily
        (
            Snapshot_Date, Kho_ID, San_Pham_ID, ClosingQuantity,
            IsValid, InvalidatedAt, InvalidReason, [Version]
        )
        SELECT @Snapshot_Date, b.Kho_ID, b.San_Pham_ID, b.CurrentQuantity,
               1, NULL, NULL, 1
        FROM dbo.InventoryBalance_Current b
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.InventoryBalance_Snapshot_Daily s
            WHERE s.Snapshot_Date = @Snapshot_Date
              AND s.Kho_ID = b.Kho_ID
              AND s.San_Pham_ID = b.San_Pham_ID
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
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
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET @So_Phieu_Nhap_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho, N'')));
    IF @So_Phieu_Nhap_Kho = N'' THROW 51100, N'Số phiếu nhập không được để trống.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51101, N'Số phiếu nhập đã tồn tại.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51102, N'Kho không hợp lệ.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID = @NCC_ID) THROW 51103, N'Nhà cung cấp không hợp lệ.', 1;
    IF @Ngay_Nhap_Kho IS NULL THROW 51104, N'Ngày nhập kho không được để trống.', 1;
    IF ISNULL(@Auto_ID, 0) = 0 INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho, Kho_ID, NCC_ID, Ngay_Nhap_Kho, Is_Posted, Ghi_Chu) VALUES(@So_Phieu_Nhap_Kho, @Kho_ID, @NCC_ID, @Ngay_Nhap_Kho, 0, @Ghi_Chu);
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID) THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Auto_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa phiếu đã Post.', 1;
        UPDATE dbo.tbl_XNK_Nhap_Kho SET So_Phieu_Nhap_Kho = @So_Phieu_Nhap_Kho, Kho_ID = @Kho_ID, NCC_ID = @NCC_ID, Ngay_Nhap_Kho = @Ngay_Nhap_Kho, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Kho_ID BIGINT = (SELECT Kho_ID FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Nhap_Kho_ID);
    IF @Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID = @Nhap_Kho_ID AND Is_Posted = 1) THROW 51163, N'Không được sửa chi tiết của phiếu đã Post.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID = @San_Pham_ID) THROW 51106, N'Sản phẩm không hợp lệ.', 1;
    IF @SL_Nhap <= 0 THROW 51107, N'Số lượng nhập phải lớn hơn 0.', 1;
    IF @Don_Gia_Nhap <= 0 THROW 51108, N'Đơn giá nhập phải lớn hơn 0.', 1;
    IF ISNULL(@Auto_ID, 0) = 0 INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID, San_Pham_ID, SL_Nhap, Don_Gia_Nhap) VALUES(@Nhap_Kho_ID, @San_Pham_ID, @SL_Nhap, @Don_Gia_Nhap);
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID = @Auto_ID AND (Nhap_Kho_ID <> @Nhap_Kho_ID OR San_Pham_ID <> @San_Pham_ID)) THROW 51110, N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.', 1;
        UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap = @SL_Nhap, Don_Gia_Nhap = @Don_Gia_Nhap WHERE Auto_ID = @Auto_ID;
    END
    IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
    SELECT @Auto_ID AS Auto_ID;
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
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    DECLARE @ManagedPostContextSet BIT = 0;
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

        EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = 1;
        SET @ManagedPostContextSet = 1;
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
            UPDATE dbo.tbl_XNK_Nhap_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
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
            EXEC dbo.sp_XNK_Reservation_Release_Document @Document_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho SET Is_Posted = 1, Posted_At = SYSUTCDATETIME(), Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Document_ID;
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
        DECLARE @MovementAffected dbo.InventoryMovementAffectedType;
        INSERT @MovementAffected(Kho_ID, San_Pham_ID, Movement_Date, InvalidReason)
        SELECT Kho_ID, San_Pham_ID, @Movement_Date, N'POSTED_DOCUMENT'
        FROM #Delta;
        EXEC dbo.sp_Inventory_Movement_Apply_Invalidation @Affected = @MovementAffected;
        EXEC dbo.sp_XNK_Validate_All_Balances;
        COMMIT TRANSACTION;
        EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;
        SET @ManagedPostContextSet = 0;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        IF @ManagedPostContextSet = 1
            EXEC sys.sp_set_session_context @key = N'InventoryMovement:ManagedPost', @value = NULL;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_InventoryBalance_Rebuild
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
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
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
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
    @Is_Receipt BIT, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Is_Receipt = 1
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
        FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
        ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC;
    ELSE
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu
        FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID)
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
    @Is_Receipt BIT, @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100) = N'', @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    DECLARE @Filter NVARCHAR(260) = N'%' + ISNULL(@Search_Text, N'') + N'%';
    IF @Is_Receipt = 1
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter);
        SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt, h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter)
        ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter);
        SELECT h.Auto_ID, CAST(0 AS BIT) AS Is_Receipt, h.So_Phieu_Xuat_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho, CAST(0 AS BIGINT) AS NCC_ID, CAST(N'' AS NVARCHAR(255)) AS Ten_NCC, h.Ngay_Xuat_Kho AS Ngay_Chung_Tu, h.Is_Posted, h.Ghi_Chu FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
        WHERE EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho_User ku WHERE ku.Ma_Dang_Nhap = @Ma_Dang_Nhap AND ku.Kho_ID = h.Kho_ID) AND (ISNULL(@Search_Text, N'') = N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter)
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
    @Ghi_Chu NVARCHAR(1000)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0 BEGIN TRANSACTION; SET @OwnTransaction = 1;
    BEGIN TRY
        SET @So_Phieu_Xuat_Kho = LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho, N'')));
        IF @So_Phieu_Xuat_Kho = N'' THROW 51130, N'Số phiếu xuất không được để trống.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho AND Auto_ID <> ISNULL(@Auto_ID, 0)) THROW 51131, N'Số phiếu xuất đã tồn tại.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID = @Kho_ID) THROW 51132, N'Kho không hợp lệ.', 1;
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
        IF @Ngay_Xuat_Kho IS NULL THROW 51133, N'Ngày xuất kho không được để trống.', 1;

        DECLARE @Old_Kho_ID BIGINT, @Is_Posted BIT;
        IF ISNULL(@Auto_ID, 0) <> 0
        BEGIN
            SELECT @Old_Kho_ID = Kho_ID, @Is_Posted = Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK) WHERE Auto_ID = @Auto_ID;
            IF @Old_Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            IF @Is_Posted = 1 THROW 51163, N'Không được sửa phiếu đã Post.', 1;
            IF @Old_Kho_ID <> @Kho_ID EXEC dbo.sp_XNK_Reservation_Move_Document @Auto_ID, @Old_Kho_ID, @Kho_ID;
            UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho = @So_Phieu_Xuat_Kho, Kho_ID = @Kho_ID, Ngay_Xuat_Kho = @Ngay_Xuat_Kho, Ghi_Chu = @Ghi_Chu, Last_Updated = SYSUTCDATETIME() WHERE Auto_ID = @Auto_ID;
        END
        ELSE
            INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho, Kho_ID, Ngay_Xuat_Kho, Is_Posted, Ghi_Chu) VALUES(@So_Phieu_Xuat_Kho, @Kho_ID, @Ngay_Xuat_Kho, 0, @Ghi_Chu);
        IF ISNULL(@Auto_ID, 0) = 0 SET @Auto_ID = SCOPE_IDENTITY();
        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        SELECT @Auto_ID AS Auto_ID;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail
    @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2), @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0 BEGIN TRANSACTION; SET @OwnTransaction = 1;
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
            INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID, San_Pham_ID, SL_Xuat, Don_Gia_Xuat) VALUES(@Xuat_Kho_ID, @San_Pham_ID, @SL_Xuat, @Don_Gia_Xuat);
            SET @Auto_ID = SCOPE_IDENTITY();
            INSERT dbo.InventoryReservation_Current(Xuat_Kho_Detail_ID, Kho_ID, San_Pham_ID, ReservedQuantity) VALUES(@Auto_ID, @Kho_ID, @San_Pham_ID, @SL_Xuat);
        END
        ELSE
        BEGIN
            UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat = @SL_Xuat, Don_Gia_Xuat = @Don_Gia_Xuat WHERE Auto_ID = @Auto_ID;
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
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT, @Document_ID BIGINT;
    SELECT @Kho_ID = h.Kho_ID, @Document_ID = h.Auto_ID FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID = d.Xuat_Kho_ID WHERE d.Auto_ID = @Auto_ID;
    IF @Kho_ID IS NULL THROW 51134, N'Chi tiết phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Document_ID AND Is_Posted = 1) THROW 51163, N'Không được xóa chi tiết của phiếu đã Post.', 1;
    BEGIN TRANSACTION;
    BEGIN TRY
        EXEC dbo.sp_XNK_Reservation_Release_Detail @Auto_ID;
        DELETE dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID = @Auto_ID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Header
    @Auto_ID BIGINT, @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL, @Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Kho_ID BIGINT, @Is_Posted BIT;
    SELECT @Kho_ID = Kho_ID, @Is_Posted = Is_Posted FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID;
    IF @Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;
    IF @Is_Posted = 1 THROW 51163, N'Không được xóa phiếu đã Post.', 1;
    BEGIN TRANSACTION;
    BEGIN TRY
        EXEC dbo.sp_XNK_Reservation_Release_Document @Auto_ID;
        DELETE dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID = @Auto_ID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
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
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(c.CurrentQuantity, 0) ELSE c.HistoricalClosing END AS DECIMAL(18,3)) AS SL_Cuoi_Ky,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(c.CurrentQuantity, 0) ELSE c.HistoricalClosing END AS DECIMAL(18,3)) AS SL_Ton_Thuc_Te,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(c.ReservedQuantity, 0) ELSE 0 END AS DECIMAL(18,3)) AS SL_Dang_Giu,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(c.CurrentQuantity, 0) - ISNULL(c.ReservedQuantity, 0) ELSE c.HistoricalClosing END AS DECIMAL(18,3)) AS SL_Kha_Dung
    FROM Calculated c
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = c.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = c.San_Pham_ID
);
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton
    @Tu_Ngay DATE, @Den_Ngay DATE, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay
        THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;

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

    SELECT Kho_ID, Ten_Kho, San_Pham_ID, Ma_San_Pham, Ten_San_Pham,
           SL_Dau_Ky, SL_Nhap, SL_Xuat, SL_Cuoi_Ky, SL_Ton_Thuc_Te, SL_Dang_Giu, SL_Kha_Dung
    FROM dbo.fn_Inventory_Report_Snapshot(@Tu_Ngay, @Den_Ngay, @Ma_Dang_Nhap, IIF(@Den_Ngay = CONVERT(DATE, SYSDATETIME()), 1, 0)) r
    WHERE @Kho_ID IS NULL OR r.Kho_ID = @Kho_ID
    ORDER BY Ten_Kho, Ma_San_Pham;
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
    SELECT i.Kho_ID,
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

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton_Page
    @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT, @Ma_Dang_Nhap NVARCHAR(100), @Kho_ID BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay > @Den_Ngay
        THROW 51200, N'Khoảng ngày báo cáo không hợp lệ.', 1;
    IF @Page_Number < 1 SET @Page_Number = 1;
    IF @Page_Size < 1 SET @Page_Size = 10;
    IF @Kho_ID IS NOT NULL EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Kho_ID;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.InventoryMovement_AggregateState
        WHERE State_ID = 1 AND IsInitialized = 1
    )
        THROW 51221, N'Movement Aggregate chưa được khởi tạo. Hãy chạy sp_Inventory_Movement_Bootstrap_From_Ledger trước khi xem báo cáo.', 1;

    DECLARE @Is_Current_Report BIT = IIF(@Den_Ngay = CONVERT(DATE, SYSDATETIME()), 1, 0);

    CREATE TABLE #AuthorizedWarehouse(Kho_ID BIGINT NOT NULL PRIMARY KEY);
    INSERT #AuthorizedWarehouse(Kho_ID)
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @Ma_Dang_Nhap
      AND (@Kho_ID IS NULL OR Kho_ID = @Kho_ID);

    CREATE TABLE #ReportScope
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        PRIMARY KEY(Kho_ID, San_Pham_ID)
    );
    INSERT #ReportScope(Kho_ID, San_Pham_ID)
    SELECT s.Kho_ID, s.San_Pham_ID
    FROM dbo.InventoryBalance_Snapshot_Daily s
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = s.Kho_ID
    WHERE s.Snapshot_Date < @Tu_Ngay
    UNION
    SELECT m.Kho_ID, m.San_Pham_ID
    FROM dbo.Inventory_Movement_Daily m
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = m.Kho_ID
    WHERE m.Movement_Date <= @Den_Ngay
    UNION
    SELECT b.Kho_ID, b.San_Pham_ID
    FROM dbo.InventoryBalance_Current b
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID
    WHERE @Is_Current_Report = 1;

    CREATE TABLE #SnapshotBalance
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        Snapshot_Date DATE NULL,
        ClosingQuantity DECIMAL(18,3) NOT NULL,
        PRIMARY KEY(Kho_ID, San_Pham_ID)
    );
    INSERT #SnapshotBalance(Kho_ID, San_Pham_ID, Snapshot_Date, ClosingQuantity)
    SELECT rs.Kho_ID,
           rs.San_Pham_ID,
           latest.Snapshot_Date,
           ISNULL(latest.ClosingQuantity, 0)
    FROM #ReportScope rs
    OUTER APPLY
    (
        SELECT TOP (1) s.Snapshot_Date, s.ClosingQuantity
        FROM dbo.InventoryBalance_Snapshot_Daily s
        WHERE s.Kho_ID = rs.Kho_ID
          AND s.San_Pham_ID = rs.San_Pham_ID
          AND s.Snapshot_Date < @Tu_Ngay
          AND s.IsValid = 1
        ORDER BY s.Snapshot_Date DESC
    ) latest;

    IF EXISTS (SELECT 1 FROM #SnapshotBalance WHERE Snapshot_Date IS NULL)
        INSERT dbo.InventorySnapshot_ReportFallbackLog
        (ReportFromDate, ReportToDate, Ma_Dang_Nhap, SnapshotMissingReason)
        VALUES (@Tu_Ngay, @Den_Ngay, @Ma_Dang_Nhap, N'NO_VALID_SNAPSHOT_FOR_ONE_OR_MORE_SCOPES');

    IF EXISTS
    (
        SELECT 1
        FROM dbo.InventoryMovement_RebuildQueue q
        JOIN #SnapshotBalance sb ON sb.Kho_ID = q.Kho_ID AND sb.San_Pham_ID = q.San_Pham_ID
        WHERE q.Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'FAILED_FINAL')
          AND q.From_Date <= @Den_Ngay
          AND q.To_Date >= ISNULL(DATEADD(DAY, 1, sb.Snapshot_Date), CONVERT(DATE, '19000101'))
    )
        THROW 51222, N'Movement Aggregate của báo cáo đang tái tạo. Vui lòng thử lại sau khi worker hoàn tất.', 1;

    CREATE TABLE #MovementAggregate
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        OpeningDelta DECIMAL(18,3) NOT NULL,
        Received DECIMAL(18,3) NOT NULL,
        Issued DECIMAL(18,3) NOT NULL,
        PRIMARY KEY(Kho_ID, San_Pham_ID)
    );
    INSERT #MovementAggregate(Kho_ID, San_Pham_ID, OpeningDelta, Received, Issued)
    SELECT m.Kho_ID, m.San_Pham_ID,
           SUM(CASE WHEN m.Movement_Date < @Tu_Ngay THEN m.Total_Receipt - m.Total_Issue ELSE 0 END),
           SUM(CASE WHEN m.Movement_Date BETWEEN @Tu_Ngay AND @Den_Ngay THEN m.Total_Receipt ELSE 0 END),
           SUM(CASE WHEN m.Movement_Date BETWEEN @Tu_Ngay AND @Den_Ngay THEN m.Total_Issue ELSE 0 END)
    FROM dbo.Inventory_Movement_Daily m
    JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = m.Kho_ID
    JOIN #SnapshotBalance sb ON sb.Kho_ID = m.Kho_ID AND sb.San_Pham_ID = m.San_Pham_ID
    WHERE m.IsValid = 1
      AND m.Movement_Date BETWEEN ISNULL(DATEADD(DAY, 1, sb.Snapshot_Date), CONVERT(DATE, '19000101')) AND @Den_Ngay
    GROUP BY m.Kho_ID, m.San_Pham_ID;

    CREATE TABLE #ReportKeys
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        SL_Dau_Ky DECIMAL(18,3) NOT NULL,
        SL_Nhap DECIMAL(18,3) NOT NULL,
        SL_Xuat DECIMAL(18,3) NOT NULL,
        HistoricalClosing DECIMAL(18,3) NOT NULL,
        CurrentQuantity DECIMAL(18,3) NULL,
        ReservedQuantity DECIMAL(18,3) NULL,
        PRIMARY KEY(Kho_ID, San_Pham_ID)
    );

    ;WITH KeySet AS
    (
        SELECT Kho_ID, San_Pham_ID FROM #SnapshotBalance
        UNION
        SELECT Kho_ID, San_Pham_ID FROM #MovementAggregate
        UNION
        SELECT b.Kho_ID, b.San_Pham_ID
        FROM dbo.InventoryBalance_Current b
        JOIN #AuthorizedWarehouse aw ON aw.Kho_ID = b.Kho_ID
        WHERE @Is_Current_Report = 1
    )
    INSERT #ReportKeys
    (
        Kho_ID, San_Pham_ID, SL_Dau_Ky, SL_Nhap, SL_Xuat,
        HistoricalClosing, CurrentQuantity, ReservedQuantity
    )
    SELECT ks.Kho_ID,
           ks.San_Pham_ID,
           CAST(ISNULL(sb.ClosingQuantity, 0) + ISNULL(ma.OpeningDelta, 0) AS DECIMAL(18,3)),
           CAST(ISNULL(ma.Received, 0) AS DECIMAL(18,3)),
           CAST(ISNULL(ma.Issued, 0) AS DECIMAL(18,3)),
           CAST(ISNULL(sb.ClosingQuantity, 0) + ISNULL(ma.OpeningDelta, 0) + ISNULL(ma.Received, 0) - ISNULL(ma.Issued, 0) AS DECIMAL(18,3)),
           b.CurrentQuantity,
           b.ReservedQuantity
    FROM KeySet ks
    LEFT JOIN #SnapshotBalance sb ON sb.Kho_ID = ks.Kho_ID AND sb.San_Pham_ID = ks.San_Pham_ID
    LEFT JOIN #MovementAggregate ma ON ma.Kho_ID = ks.Kho_ID AND ma.San_Pham_ID = ks.San_Pham_ID
    LEFT JOIN dbo.InventoryBalance_Current b ON b.Kho_ID = ks.Kho_ID AND b.San_Pham_ID = ks.San_Pham_ID;

    SELECT COUNT(*) AS Total_Count FROM #ReportKeys;
    SELECT rk.Kho_ID,
           k.Ten_Kho,
           rk.San_Pham_ID,
           p.Ma_San_Pham,
           p.Ten_San_Pham,
           rk.SL_Dau_Ky,
           rk.SL_Nhap,
           rk.SL_Xuat,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(rk.CurrentQuantity, 0) ELSE rk.HistoricalClosing END AS DECIMAL(18,3)) AS SL_Cuoi_Ky,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(rk.CurrentQuantity, 0) ELSE rk.HistoricalClosing END AS DECIMAL(18,3)) AS SL_Ton_Thuc_Te,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(rk.ReservedQuantity, 0) ELSE 0 END AS DECIMAL(18,3)) AS SL_Dang_Giu,
           CAST(CASE WHEN @Is_Current_Report = 1 THEN ISNULL(rk.CurrentQuantity, 0) - ISNULL(rk.ReservedQuantity, 0) ELSE rk.HistoricalClosing END AS DECIMAL(18,3)) AS SL_Kha_Dung
    FROM #ReportKeys rk
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = rk.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = rk.San_Pham_ID
    ORDER BY k.Ten_Kho, p.Ma_San_Pham
    OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
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
