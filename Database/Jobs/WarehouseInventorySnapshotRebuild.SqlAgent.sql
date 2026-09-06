/*
   Idempotent SQL Server Agent deployment for snapshot repair.
   INITIALIZE rows stay observable until a confirmed ledger bootstrap exists;
   REBUILD and due RETRY_WAITING rows are processed every five minutes.
*/
USE msdb;
GO

DECLARE @JobName SYSNAME = N'TKS Warehouse - Inventory Snapshot Repair';
DECLARE @ScheduleName SYSNAME = N'TKS Warehouse - Inventory Snapshot Repair - 5min';
DECLARE @JobId UNIQUEIDENTIFIER;

/* The earlier 02:00 job has no retry/lease lifecycle.  Preserve its history
   but disable it so it cannot race the repair worker after deployment. */
IF EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = N'TKS Warehouse - Inventory Snapshot Rebuild')
    EXEC dbo.sp_update_job
        @job_name = N'TKS Warehouse - Inventory Snapshot Rebuild',
        @enabled = 0,
        @description = N'Superseded by TKS Warehouse - Inventory Snapshot Repair; retained disabled for rollback.';

IF EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = @JobName)
    SELECT @JobId = job_id FROM dbo.sysjobs WHERE name = @JobName;
ELSE
BEGIN
    EXEC dbo.sp_add_job
        @job_name = @JobName,
        @enabled = 1,
        @description = N'Repairs and initializes warehouse inventory snapshots from the durable lifecycle queue.',
        @job_id = @JobId OUTPUT;
END

IF EXISTS (SELECT 1 FROM dbo.sysjobsteps WHERE job_id = @JobId AND step_id = 1)
    EXEC dbo.sp_update_jobstep
        @job_id = @JobId, @step_id = 1,
        @step_name = N'Process snapshot repair queue', @subsystem = N'TSQL',
        @database_name = N'TKS_Thuc_Tap_V11_GiaiDoan2',
        @command = N'EXEC dbo.sp_Inventory_Snapshot_Process_RebuildQueue @Batch_Size = 100, @Max_Retry_Count = 5, @Processing_Lease_Seconds = 300, @Worker_Name = N''SQLAgent:InventorySnapshotRepair'';',
        @on_success_action = 1, @on_fail_action = 2;
ELSE
    EXEC dbo.sp_add_jobstep
        @job_id = @JobId,
        @step_name = N'Process snapshot repair queue', @subsystem = N'TSQL',
        @database_name = N'TKS_Thuc_Tap_V11_GiaiDoan2',
        @command = N'EXEC dbo.sp_Inventory_Snapshot_Process_RebuildQueue @Batch_Size = 100, @Max_Retry_Count = 5, @Processing_Lease_Seconds = 300, @Worker_Name = N''SQLAgent:InventorySnapshotRepair'';',
        @on_success_action = 1, @on_fail_action = 2;

EXEC dbo.sp_add_schedule
    @schedule_name = @ScheduleName,
    @enabled = 1,
    @freq_type = 4,
    @freq_interval = 1,
    @freq_subday_type = 4,
    @freq_subday_interval = 5,
    @active_start_time = 000000;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.sysjobschedules js
    JOIN dbo.sysschedules s ON s.schedule_id = js.schedule_id
    WHERE js.job_id = @JobId AND s.name = @ScheduleName
)
    EXEC dbo.sp_attach_schedule @job_id = @JobId, @schedule_name = @ScheduleName;

IF NOT EXISTS (SELECT 1 FROM dbo.sysjobservers WHERE job_id = @JobId)
    EXEC dbo.sp_add_jobserver @job_id = @JobId;
GO
