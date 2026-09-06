/* Monitoring job: fail the Agent step on a critical condition so the existing
   SQL Agent notification/operator policy can deliver an alert. */
USE msdb;
GO

DECLARE @JobName SYSNAME = N'TKS Warehouse - Inventory Snapshot Monitor';
DECLARE @ScheduleName SYSNAME = N'TKS Warehouse - Inventory Snapshot Monitor - 15min';
DECLARE @JobId UNIQUEIDENTIFIER;

IF EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = @JobName)
    SELECT @JobId = job_id FROM dbo.sysjobs WHERE name = @JobName;
ELSE
BEGIN
    EXEC dbo.sp_add_job
        @job_name = @JobName,
        @enabled = 1,
        @description = N'Detects inventory snapshot backlog, failed final work, expired leases, stale workers and stale projections.',
        @job_id = @JobId OUTPUT;
END

IF EXISTS (SELECT 1 FROM dbo.sysjobsteps WHERE job_id = @JobId AND step_id = 1)
    EXEC dbo.sp_update_jobstep @job_id = @JobId, @step_id = 1,
        @step_name = N'Monitor inventory snapshot health', @subsystem = N'TSQL',
        @database_name = N'TKS_Thuc_Tap_V11_GiaiDoan2',
        @command = N'EXEC dbo.sp_Inventory_Snapshot_Monitor @Snapshot_Backlog_Minutes = 60, @Processing_Lease_Seconds = 300, @Daily_Stale_Days = 1, @Throw_On_Critical = 1;',
        @on_success_action = 1, @on_fail_action = 2;
ELSE
    EXEC dbo.sp_add_jobstep @job_id = @JobId,
        @step_name = N'Monitor inventory snapshot health', @subsystem = N'TSQL',
        @database_name = N'TKS_Thuc_Tap_V11_GiaiDoan2',
        @command = N'EXEC dbo.sp_Inventory_Snapshot_Monitor @Snapshot_Backlog_Minutes = 60, @Processing_Lease_Seconds = 300, @Daily_Stale_Days = 1, @Throw_On_Critical = 1;',
        @on_success_action = 1, @on_fail_action = 2;

IF NOT EXISTS (SELECT 1 FROM dbo.sysschedules WHERE name = @ScheduleName)
    EXEC dbo.sp_add_schedule @schedule_name = @ScheduleName, @enabled = 1,
        @freq_type = 4, @freq_interval = 1, @freq_subday_type = 4,
        @freq_subday_interval = 15, @active_start_time = 000000;

IF NOT EXISTS
(
    SELECT 1 FROM dbo.sysjobschedules js JOIN dbo.sysschedules s ON s.schedule_id = js.schedule_id
    WHERE js.job_id = @JobId AND s.name = @ScheduleName
)
    EXEC dbo.sp_attach_schedule @job_id = @JobId, @schedule_name = @ScheduleName;

IF NOT EXISTS (SELECT 1 FROM dbo.sysjobservers WHERE job_id = @JobId)
    EXEC dbo.sp_add_jobserver @job_id = @JobId;
GO
