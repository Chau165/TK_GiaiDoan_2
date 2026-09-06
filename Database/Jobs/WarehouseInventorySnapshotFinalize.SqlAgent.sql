/* Finalizes the previous business day from Inventory_Balance_Daily at 00:15.
   The procedure fails loudly when Movement/Daily still has active rebuild work,
   so SQL Agent can alert instead of writing an untrustworthy checkpoint. */
USE msdb;
GO

DECLARE @JobName SYSNAME = N'TKS Warehouse - Inventory Snapshot Finalize Daily';
DECLARE @ScheduleName SYSNAME = N'TKS Warehouse - Inventory Snapshot Finalize Daily - 0015';
DECLARE @JobId UNIQUEIDENTIFIER;

IF EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = @JobName)
    SELECT @JobId = job_id FROM dbo.sysjobs WHERE name = @JobName;
ELSE
BEGIN
    EXEC dbo.sp_add_job
        @job_name = @JobName,
        @enabled = 1,
        @description = N'Finalizes previous-day warehouse snapshots from Inventory_Balance_Daily only.',
        @job_id = @JobId OUTPUT;
END

DECLARE @Command NVARCHAR(MAX) =
N'DECLARE @SnapshotDate DATE = DATEADD(DAY, -1, CONVERT(DATE, SYSUTCDATETIME()));
  EXEC dbo.sp_Inventory_Snapshot_Finalize_Daily
       @Snapshot_Date = @SnapshotDate,
       @Worker_Name = N''SQLAgent:InventorySnapshotFinalize'';';

IF EXISTS (SELECT 1 FROM dbo.sysjobsteps WHERE job_id = @JobId AND step_id = 1)
    EXEC dbo.sp_update_jobstep @job_id = @JobId, @step_id = 1,
        @step_name = N'Finalize previous-day snapshot from Daily', @subsystem = N'TSQL',
        @database_name = N'TKS_Thuc_Tap_V11_GiaiDoan2', @command = @Command,
        @on_success_action = 1, @on_fail_action = 2;
ELSE
    EXEC dbo.sp_add_jobstep @job_id = @JobId,
        @step_name = N'Finalize previous-day snapshot from Daily', @subsystem = N'TSQL',
        @database_name = N'TKS_Thuc_Tap_V11_GiaiDoan2', @command = @Command,
        @on_success_action = 1, @on_fail_action = 2;

IF NOT EXISTS (SELECT 1 FROM dbo.sysschedules WHERE name = @ScheduleName)
    EXEC dbo.sp_add_schedule @schedule_name = @ScheduleName, @enabled = 1,
        @freq_type = 4, @freq_interval = 1, @active_start_time = 001500;

IF NOT EXISTS
(
    SELECT 1 FROM dbo.sysjobschedules js JOIN dbo.sysschedules s ON s.schedule_id = js.schedule_id
    WHERE js.job_id = @JobId AND s.name = @ScheduleName
)
    EXEC dbo.sp_attach_schedule @job_id = @JobId, @schedule_name = @ScheduleName;

IF NOT EXISTS (SELECT 1 FROM dbo.sysjobservers WHERE job_id = @JobId)
    EXEC dbo.sp_add_jobserver @job_id = @JobId;
GO
