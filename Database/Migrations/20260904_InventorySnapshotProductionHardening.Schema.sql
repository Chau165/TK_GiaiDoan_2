/*
   Inventory Snapshot production hardening migration.
   This migration is additive: it adds columns, constraints, tables and indexes
   without dropping an existing table, column, constraint or index.
*/

SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'RequestType') IS NULL
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD RequestType NVARCHAR(20) NOT NULL
        CONSTRAINT DF_InventorySnapshot_RebuildQueue_RequestType DEFAULT (N'REBUILD') WITH VALUES;
IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'LifecycleStatus') IS NULL
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD LifecycleStatus NVARCHAR(24) NOT NULL
        CONSTRAINT DF_InventorySnapshot_RebuildQueue_LifecycleStatus DEFAULT (N'WAITING') WITH VALUES;
IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'AttemptCount') IS NULL
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD AttemptCount INT NOT NULL
        CONSTRAINT DF_InventorySnapshot_RebuildQueue_AttemptCount DEFAULT (0) WITH VALUES;
IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'LastAttemptAt') IS NULL
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD LastAttemptAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'NextAttemptAt') IS NULL
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD NextAttemptAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'LeaseUntil') IS NULL
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD LeaseUntil DATETIME2 NULL;
IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'ClaimedBy') IS NULL
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD ClaimedBy NVARCHAR(128) NULL;
IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'ClaimedAt') IS NULL
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD ClaimedAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.InventorySnapshot_RebuildQueue', N'LastError') IS NULL
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD LastError NVARCHAR(4000) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.InventorySnapshot_RebuildQueue') AND name = N'CK_InventorySnapshot_RebuildQueue_RequestType')
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD CONSTRAINT CK_InventorySnapshot_RebuildQueue_RequestType CHECK (RequestType IN (N'REBUILD', N'INITIALIZE'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.InventorySnapshot_RebuildQueue') AND name = N'CK_InventorySnapshot_RebuildQueue_LifecycleStatus')
    ALTER TABLE dbo.InventorySnapshot_RebuildQueue ADD CONSTRAINT CK_InventorySnapshot_RebuildQueue_LifecycleStatus CHECK (LifecycleStatus IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'INITIALIZE_REQUIRED', N'COMPLETED', N'FAILED_FINAL'));
GO

UPDATE q
SET RequestType = CASE WHEN q.Status = N'FAILED' THEN N'REBUILD' WHEN EXISTS (SELECT 1 FROM dbo.InventoryBalance_Snapshot_Daily s WHERE s.Kho_ID = q.Kho_ID AND s.San_Pham_ID = q.San_Pham_ID) THEN N'REBUILD' ELSE N'INITIALIZE' END,
    LifecycleStatus = CASE WHEN q.Status = N'FAILED' THEN N'FAILED_FINAL' WHEN q.Status = N'COMPLETED' THEN N'COMPLETED' WHEN q.Status = N'PROCESSING' THEN N'RETRY_WAITING' WHEN NOT EXISTS (SELECT 1 FROM dbo.InventoryBalance_Snapshot_Daily s WHERE s.Kho_ID = q.Kho_ID AND s.San_Pham_ID = q.San_Pham_ID) THEN N'INITIALIZE_REQUIRED' ELSE N'WAITING' END,
    LastError = CASE WHEN q.Status = N'FAILED' THEN COALESCE(q.LastError, q.ErrorMessage, N'Legacy snapshot worker failure.') WHEN q.Status = N'PROCESSING' THEN COALESCE(q.LastError, q.ErrorMessage, N'Legacy processing claim recovered by lifecycle migration.') ELSE q.LastError END,
    NextAttemptAt = CASE WHEN q.Status = N'PROCESSING' THEN SYSUTCDATETIME() ELSE q.NextAttemptAt END
FROM dbo.InventorySnapshot_RebuildQueue q;

UPDATE dbo.InventorySnapshot_RebuildQueue
SET Status = N'WAITING', ErrorMessage = NULL
WHERE LifecycleStatus IN (N'RETRY_WAITING', N'INITIALIZE_REQUIRED') AND Status = N'PROCESSING';
GO

IF OBJECT_ID(N'dbo.InventorySnapshot_RebuildDeadLetter', N'U') IS NULL
CREATE TABLE dbo.InventorySnapshot_RebuildDeadLetter
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventorySnapshot_RebuildDeadLetter PRIMARY KEY,
    Queue_ID BIGINT NOT NULL CONSTRAINT UQ_InventorySnapshot_RebuildDeadLetter_Queue UNIQUE,
    Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    From_Date DATE NOT NULL,
    RequestType NVARCHAR(20) NOT NULL,
    AttemptCount INT NOT NULL,
    LastError NVARCHAR(4000) NOT NULL,
    FailedAt DATETIME2 NOT NULL CONSTRAINT DF_InventorySnapshot_RebuildDeadLetter_FailedAt DEFAULT SYSUTCDATETIME(),
    ResolvedAt DATETIME2 NULL,
    ResolutionNote NVARCHAR(4000) NULL,
    CONSTRAINT FK_InventorySnapshot_RebuildDeadLetter_Queue FOREIGN KEY (Queue_ID) REFERENCES dbo.InventorySnapshot_RebuildQueue(ID),
    CONSTRAINT FK_InventorySnapshot_RebuildDeadLetter_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID),
    CONSTRAINT FK_InventorySnapshot_RebuildDeadLetter_SanPham FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

IF OBJECT_ID(N'dbo.InventorySnapshot_BootstrapAudit', N'U') IS NULL
CREATE TABLE dbo.InventorySnapshot_BootstrapAudit
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventorySnapshot_BootstrapAudit PRIMARY KEY,
    Baseline_Date DATE NOT NULL,
    Kho_ID BIGINT NULL,
    San_Pham_ID BIGINT NULL,
    Opening_Balance_Confirmed BIT NOT NULL,
    Source_Name NVARCHAR(100) NOT NULL CONSTRAINT DF_InventorySnapshot_BootstrapAudit_Source DEFAULT (N'POSTED_LEDGER'),
    Status NVARCHAR(20) NOT NULL,
    StartedAt DATETIME2 NOT NULL CONSTRAINT DF_InventorySnapshot_BootstrapAudit_StartedAt DEFAULT SYSUTCDATETIME(),
    CompletedAt DATETIME2 NULL,
    ScopeCount INT NULL,
    SnapshotRowCount INT NULL,
    ErrorMessage NVARCHAR(4000) NULL,
    CONSTRAINT CK_InventorySnapshot_BootstrapAudit_Status CHECK (Status IN (N'RUNNING', N'COMPLETED', N'FAILED'))
);
GO

IF OBJECT_ID(N'dbo.InventoryReconciliation_Run', N'U') IS NULL
CREATE TABLE dbo.InventoryReconciliation_Run
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryReconciliation_Run PRIMARY KEY,
    As_Of_Date DATE NOT NULL,
    Kho_ID BIGINT NULL,
    San_Pham_ID BIGINT NULL,
    Status NVARCHAR(20) NOT NULL,
    StartedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryReconciliation_Run_StartedAt DEFAULT SYSUTCDATETIME(),
    CompletedAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(4000) NULL,
    CONSTRAINT CK_InventoryReconciliation_Run_Status CHECK (Status IN (N'RUNNING', N'COMPLETED', N'FAILED'))
);
GO

IF OBJECT_ID(N'dbo.InventoryReconciliation_Result', N'U') IS NULL
CREATE TABLE dbo.InventoryReconciliation_Result
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryReconciliation_Result PRIMARY KEY,
    Run_ID BIGINT NOT NULL,
    Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    Check_Date DATE NULL,
    Check_Type NVARCHAR(50) NOT NULL,
    ExpectedQuantity DECIMAL(18,3) NOT NULL,
    ActualQuantity DECIMAL(18,3) NOT NULL,
    Difference DECIMAL(18,3) NOT NULL,
    Severity NVARCHAR(20) NOT NULL,
    Status NVARCHAR(20) NOT NULL,
    Details NVARCHAR(1000) NULL,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryReconciliation_Result_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_InventoryReconciliation_Result_Run FOREIGN KEY (Run_ID) REFERENCES dbo.InventoryReconciliation_Run(ID),
    CONSTRAINT CK_InventoryReconciliation_Result_Severity CHECK (Severity IN (N'INFO', N'WARNING', N'CRITICAL')),
    CONSTRAINT CK_InventoryReconciliation_Result_Status CHECK (Status IN (N'PASS', N'FAIL'))
);
GO

IF OBJECT_ID(N'dbo.InventorySnapshot_WorkerHeartbeat', N'U') IS NULL
CREATE TABLE dbo.InventorySnapshot_WorkerHeartbeat
(
    Worker_Name NVARCHAR(128) NOT NULL CONSTRAINT PK_InventorySnapshot_WorkerHeartbeat PRIMARY KEY,
    LastHeartbeatAt DATETIME2 NOT NULL,
    LastSuccessAt DATETIME2 NULL,
    LastFailureAt DATETIME2 NULL,
    LastQueue_ID BIGINT NULL,
    LastError NVARCHAR(4000) NULL,
    UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventorySnapshot_WorkerHeartbeat_UpdatedAt DEFAULT SYSUTCDATETIME()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventorySnapshot_RebuildQueue_LifecycleReady')
    CREATE INDEX IX_InventorySnapshot_RebuildQueue_LifecycleReady ON dbo.InventorySnapshot_RebuildQueue(LifecycleStatus, NextAttemptAt, CreatedAt, ID) INCLUDE (Kho_ID, San_Pham_ID, From_Date, RequestType, AttemptCount, LeaseUntil);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryReconciliation_Result_Run_Status')
    CREATE INDEX IX_InventoryReconciliation_Result_Run_Status ON dbo.InventoryReconciliation_Result(Run_ID, Status, Check_Type, Kho_ID, San_Pham_ID);
GO
