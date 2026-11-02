
CREATE   PROCEDURE dbo.sp_XNK_Document_Post
    @Is_Receipt BIT, @Document_ID BIGINT, @Ma_Dang_Nhap NVARCHAR(100),
    @Last_Updated_By NVARCHAR(100)=NULL, @Last_Updated_By_Function NVARCHAR(100)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    CREATE TABLE #Delta
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        Delta DECIMAL(18,3) NOT NULL,
        PRIMARY KEY (Kho_ID, San_Pham_ID)
    );

    /* Candidate discovery is intentionally lock-free.  It only derives the
       complete group/scope set.  The protected transaction re-reads and
       verifies the exact header/detail identity after the new fence is held. */
    DECLARE @Movement_Date DATE;
    DECLARE @Candidate_Kho_ID BIGINT;
    IF @Is_Receipt = 1
    BEGIN
        SELECT @Candidate_Kho_ID = Kho_ID, @Movement_Date = Ngay_Nhap_Kho
        FROM dbo.tbl_XNK_Nhap_Kho
        WHERE Auto_ID = @Document_ID;
        IF @Candidate_Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID = @Document_ID)
            THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
        INSERT #Delta
        SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(d.SL_Nhap AS DECIMAL(18,3)))
        FROM dbo.tbl_XNK_Nhap_Kho h
        JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
        WHERE h.Auto_ID = @Document_ID
        GROUP BY h.Kho_ID, d.San_Pham_ID;
    END
    ELSE
    BEGIN
        SELECT @Candidate_Kho_ID = Kho_ID, @Movement_Date = Ngay_Xuat_Kho
        FROM dbo.tbl_XNK_Xuat_Kho
        WHERE Auto_ID = @Document_ID;
        IF @Candidate_Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Xuat_Kho_ID = @Document_ID)
            THROW 51161, N'Không thể Post phiếu không có chi tiết.', 1;
        INSERT #Delta
        SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(-d.SL_Xuat AS DECIMAL(18,3)))
        FROM dbo.tbl_XNK_Xuat_Kho h
        JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID = h.Auto_ID
        WHERE h.Auto_ID = @Document_ID
        GROUP BY h.Kho_ID, d.San_Pham_ID;
    END;

    EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Candidate_Kho_ID;

    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode = N'Shared';
        EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Movement_Bootstrap @Mode = N'Shared';

        DECLARE @GroupSet dbo.InventoryFenceGroupSetType;
        DECLARE @ScopeSet dbo.InventoryFenceScopeSetType;
        INSERT @GroupSet(Kho_ID)
        SELECT DISTINCT Kho_ID FROM #Delta;
        INSERT @ScopeSet(Kho_ID, San_Pham_ID)
        SELECT Kho_ID, San_Pham_ID FROM #Delta;

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

        /* The row locks are deliberately taken only after Root/Group/Scope.
           Any change to the candidate identity fails closed and is retried by
           the caller instead of posting a mixed-generation document. */
        DECLARE @Verified_Kho_ID BIGINT;
        DECLARE @Verified_Movement_Date DATE;
        DECLARE @Verified_Is_Posted BIT;
        DECLARE @VerifiedDetailCount INT;
        DECLARE @CandidateDetailCount INT = (SELECT COUNT(*) FROM #Delta);
        CREATE TABLE #VerifiedDelta
        (
            Kho_ID BIGINT NOT NULL,
            San_Pham_ID BIGINT NOT NULL,
            Delta DECIMAL(18,3) NOT NULL,
            PRIMARY KEY (Kho_ID, San_Pham_ID)
        );

        IF @Is_Receipt = 1
        BEGIN
            SELECT @Verified_Kho_ID = Kho_ID,
                   @Verified_Movement_Date = Ngay_Nhap_Kho,
                   @Verified_Is_Posted = Is_Posted
            FROM dbo.tbl_XNK_Nhap_Kho WITH (UPDLOCK, HOLDLOCK)
            WHERE Auto_ID = @Document_ID;
            IF @Verified_Kho_ID IS NULL THROW 51105, N'Phiếu nhập không tồn tại.', 1;
            IF @Verified_Is_Posted = 1 THROW 51162, N'Phiếu đã Post.', 1;
            INSERT #VerifiedDelta
            SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(d.SL_Nhap AS DECIMAL(18,3)))
            FROM dbo.tbl_XNK_Nhap_Kho h WITH (UPDLOCK, HOLDLOCK)
            JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d WITH (UPDLOCK, HOLDLOCK) ON d.Nhap_Kho_ID = h.Auto_ID
            WHERE h.Auto_ID = @Document_ID
            GROUP BY h.Kho_ID, d.San_Pham_ID;
        END
        ELSE
        BEGIN
            SELECT @Verified_Kho_ID = Kho_ID,
                   @Verified_Movement_Date = Ngay_Xuat_Kho,
                   @Verified_Is_Posted = Is_Posted
            FROM dbo.tbl_XNK_Xuat_Kho WITH (UPDLOCK, HOLDLOCK)
            WHERE Auto_ID = @Document_ID;
            IF @Verified_Kho_ID IS NULL THROW 51134, N'Phiếu xuất không tồn tại.', 1;
            IF @Verified_Is_Posted = 1 THROW 51162, N'Phiếu đã Post.', 1;
            INSERT #VerifiedDelta
            SELECT h.Kho_ID, d.San_Pham_ID, SUM(CAST(-d.SL_Xuat AS DECIMAL(18,3)))
            FROM dbo.tbl_XNK_Xuat_Kho h WITH (UPDLOCK, HOLDLOCK)
            JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d WITH (UPDLOCK, HOLDLOCK) ON d.Xuat_Kho_ID = h.Auto_ID
            WHERE h.Auto_ID = @Document_ID
            GROUP BY h.Kho_ID, d.San_Pham_ID;
        END;

        SELECT @VerifiedDetailCount = COUNT(*) FROM #VerifiedDelta;
        IF @Verified_Kho_ID <> @Candidate_Kho_ID
           OR @Verified_Movement_Date <> @Movement_Date
           OR @VerifiedDetailCount <> @CandidateDetailCount
           OR EXISTS (SELECT Kho_ID, San_Pham_ID, Delta FROM #Delta EXCEPT SELECT Kho_ID, San_Pham_ID, Delta FROM #VerifiedDelta)
           OR EXISTS (SELECT Kho_ID, San_Pham_ID, Delta FROM #VerifiedDelta EXCEPT SELECT Kho_ID, San_Pham_ID, Delta FROM #Delta)
            THROW 51331, N'Chứng từ hoặc chi tiết đã thay đổi sau khi khóa Inventory Fence; vui lòng thử lại.', 1;

        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap, @Verified_Kho_ID;
        SET @Movement_Date = @Verified_Movement_Date;

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
