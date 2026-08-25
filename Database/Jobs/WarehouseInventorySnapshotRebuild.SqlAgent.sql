/*
   Idempotent SQL Server Agent deployment for the asynchronous snapshot worker.
   Run against the instance with an account allowed to create SQL Agent jobs.
   The job processes a bounded batch so posting transactions never wait for it.
*/
USE msdb;
GO

DECLARE @JobName SYSNAME = N'TKS Warehouse - Inventory Snapshot Rebuild';
DECLARE @ScheduleName SYSNAME = N'TKS Warehouse - Inventory Snapshot Rebuild - 0200';
DECLARE @JobId UNIQUEIDENTIFIER;

IF EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = @JobName)
BEGIN
    SELECT @JobId = job_id FROM dbo.sysjobs WHERE name = @JobName;
    IF EXISTS (SELECT 1 FROM dbo.sysjobsteps WHERE job_id = @JobId AND step_name = N'Process snapshot rebuild queue')
        EXEC dbo.sp_delete_jobstep @job_id = @JobId, @step_id = 1;
    EXEC dbo.sp_delete_job @job_id = @JobId, @delete_unused_schedule = 1;
END

EXEC dbo.sp_add_job
    @job_name = @JobName,
    @enabled = 1,
    @description = N'Rebuilds invalid warehouse inventory snapshots asynchronously from the rebuild queue.',
    @job_id = @JobId OUTPUT;

EXEC dbo.sp_add_jobstep
    @job_id = @JobId,
    @step_name = N'Process snapshot rebuild queue',
    @subsystem = N'TSQL',
    @database_name = N'TKS_Thuc_Tap_V11_GiaiDoan2',
    @command = N'EXEC dbo.sp_Inventory_Snapshot_Process_RebuildQueue @Batch_Size = 100;',
    @on_success_action = 1,
    @on_fail_action = 2;

EXEC dbo.sp_add_schedule
    @schedule_name = @ScheduleName,
    @enabled = 1,
    @freq_type = 4,
    @freq_interval = 1,
    @active_start_time = 020000;

EXEC dbo.sp_attach_schedule
    @job_id = @JobId,
    @schedule_name = @ScheduleName;

EXEC dbo.sp_add_jobserver
    @job_id = @JobId;
GO
