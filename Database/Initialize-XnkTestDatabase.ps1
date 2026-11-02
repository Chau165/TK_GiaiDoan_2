param(
    [Parameter(Mandatory = $true)][string]$SchemaPath,
    [string]$Server = 'DESKTOP-NHQ7QPL\MSSQLSERVER19',
    [string]$Database = 'TKS_Thuc_Tap_V11_XNK_Test_20261102'
)
$ErrorActionPreference = 'Stop'
# This script can only publish to the task's disposable database.
if ($Server -ne 'DESKTOP-NHQ7QPL\MSSQLSERVER19' -or $Database -ne 'TKS_Thuc_Tap_V11_XNK_Test_20261102') {
    throw 'Only the isolated XNK test database is allowed.'
}
$v_schema = (Resolve-Path -LiteralPath $SchemaPath).Path
& sqlpackage /Action:Publish "/SourceFile:$v_schema" "/TargetServerName:$Server" "/TargetDatabaseName:$Database" /TargetTrustServerCertificate:True '/p:ExcludeObjectTypes=Users;Logins;RoleMembership;Permissions' /p:IgnorePermissions=True /p:ScriptDatabaseOptions=False
if ($LASTEXITCODE -ne 0) { throw 'Schema-only test database publication failed.' }

$v_sql = @'
SET NOCOUNT ON;
IF DB_NAME()<>N'TKS_Thuc_Tap_V11_XNK_Test_20261102' THROW 51599,N'Test database only.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.Inventory_Current_Report_State WHERE State_ID=1)
    INSERT dbo.Inventory_Current_Report_State(State_ID,Generation,UpdatedAt) VALUES(1,1,SYSUTCDATETIME());
IF NOT EXISTS(SELECT 1 FROM dbo.InventoryMovement_AggregateState WHERE State_ID=1)
    INSERT dbo.InventoryMovement_AggregateState(State_ID,IsInitialized) VALUES(1,0);
IF NOT EXISTS(SELECT 1 FROM dbo.InventoryBalance_Daily_AggregateState WHERE State_ID=1)
    INSERT dbo.InventoryBalance_Daily_AggregateState(State_ID,IsInitialized) VALUES(1,0);
IF NOT EXISTS(SELECT 1 FROM dbo.Inventory_Report_Fence_Config)
    INSERT dbo.Inventory_Report_Fence_Config(Config_ID,Historical_Report_Mode) VALUES(1,N'LEGACY');
EXEC dbo.sp_Inventory_Movement_Bootstrap_From_Ledger;
EXEC dbo.sp_Inventory_Balance_Daily_Bootstrap_From_Movement;
'@
& sqlcmd -S $Server -d $Database -E -C -b -Q $v_sql
if ($LASTEXITCODE -ne 0) { throw 'Test inventory initialization failed.' }
& sqlcmd -S $Server -d $Database -E -C -b -f 65001 -i (Join-Path $PSScriptRoot 'WarehouseDocumentSavePosted.Preflight.sql')
if ($LASTEXITCODE -ne 0) { throw 'Baseline mismatch: review schema before testing.' }
& sqlcmd -S $Server -d $Database -E -C -b -f 65001 -i (Join-Path $PSScriptRoot 'WarehouseDocumentSavePosted.sql')
if ($LASTEXITCODE -ne 0) { throw 'Test-only migration failed.' }
