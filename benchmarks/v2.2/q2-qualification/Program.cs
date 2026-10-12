using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Controller.DM;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Controller.XNK;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

internal static class Program
{
    private const string Login = "PERF_USER";
    private const long Warehouse = 4, Product = 40, Supplier = 1;
    private static readonly Guid PerfJob = Guid.Parse("7c8516ba-bb97-4ef2-9673-027a117a6f9f");
    private static readonly Guid GiaiDoan2Job = Guid.Parse("50d88015-5132-4805-9503-9463c37e085c");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly WarehouseDocumentQualificationHarness Docs = new();
    private static readonly CWarehouseReport_Controller Reports = new();
    private static readonly Dictionary<string, object?> State = new(StringComparer.Ordinal);
    private static string _root = "", _runId = "", _cs = "", _journal = "";
    private static string? _executeClaimSha256;
    private static string? _recoveryClaimSha256;
    private static string? _validatedRecoveryClaimSha256;
    private static string? _recoveryPreparationId;
    private static string? _recoveryPreparationSha256;
    private static JsonDocument? _stableBaseline;
    private static string _stableBaselineSha256 = "";
    private static long? _draftHeader, _draftDetail;
    private static bool _draftReceipt;

    internal sealed record Evidence(Dictionary<string, object?> Identity, Dictionary<string, object?> Scope,
        Dictionary<string, object?> Worker, Dictionary<string, object?> ExistingJob,
        Dictionary<string, object?> AppPath, string DataAccessAssemblySha256);
    internal sealed record CaptureStageTiming(string Stage,int Order,DateTimeOffset StartedUtc,DateTimeOffset FinishedUtc,long DurationMs,string Status,int? ResultCount,string? ErrorType,int? SqlErrorNumber);
    internal sealed record CaptureDmvSnapshot(int ProcessId,int PollCount,int SampleCount,string? Error,bool? NoBlockingObserved,bool BlockingObserved,IReadOnlyList<Dictionary<string,object?>> Samples);
    private sealed record CaptureComparison(bool Pass,int ExpectedFieldCount,int ObservedFieldCount,bool DataAccessAssemblyPass,List<object> Domains);
    private sealed class CaptureTimingContext(Action<string,bool>? stageBoundary=null)
    {
        internal readonly List<CaptureStageTiming> Stages=[];
        internal Action<string,bool>? StageBoundary=stageBoundary;
    }

    private sealed class CaptureDmvObserver(string connectionString,int processId) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop=new();
        private readonly object _sync=new();
        private readonly List<Dictionary<string,object?>> _samples=[];
        private readonly TaskCompletionSource<bool> _ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string _activeStage="";
        private string? _error;
        private int _pollCount;
        private Task? _loop;
        internal void StageBoundary(string stage,bool entering)=>Volatile.Write(ref _activeStage,entering?stage:"");
        internal async Task StartAsync(){_loop=Task.Run(PollAsync);await _ready.Task;}
        internal async Task StopAsync()
        {
            _stop.Cancel();
            if(_loop is not null)try{await _loop;}catch(OperationCanceledException){}
        }
        internal CaptureDmvSnapshot Snapshot()
        {
            lock(_sync)
            {
                var blocking=_samples.Any(x=>x["BlockingSessionId"] is int id&&id!=0);
                return new CaptureDmvSnapshot(processId,_pollCount,_samples.Count,_error,_samples.Count==0?null:!blocking,blocking,_samples.ToArray());
            }
        }
        private async Task PollAsync()
        {
            try
            {
                var builder=new SqlConnectionStringBuilder(connectionString);builder.ConnectTimeout=Math.Min(builder.ConnectTimeout,3);
                await using var connection=new SqlConnection(builder.ConnectionString);await connection.OpenAsync(_stop.Token);
                _ready.TrySetResult(true);
                var elapsed=Stopwatch.StartNew();
                while(!_stop.IsCancellationRequested&&elapsed.Elapsed<TimeSpan.FromSeconds(38))
                {
                    if(Volatile.Read(ref _activeStage)=="DocumentPageApplicationRead")
                    {
                        Interlocked.Increment(ref _pollCount);
                        await SampleAsync(connection,_stop.Token);
                    }
                    await Task.Delay(200,_stop.Token);
                }
            }
            catch(OperationCanceledException){}
            catch(Exception ex){_error=Safe(ex);_ready.TrySetResult(true);}
        }
        private async Task SampleAsync(SqlConnection connection,CancellationToken token)
        {
            const string sql=@"
SELECT TOP (4) s.session_id,s.login_time,r.start_time,r.total_elapsed_time,r.blocking_session_id,
       r.wait_type,r.wait_time,r.wait_resource,s.open_transaction_count,t.transaction_state,
       r.database_id,r.command
FROM sys.dm_exec_sessions s
JOIN sys.dm_exec_requests r ON r.session_id=s.session_id
OUTER APPLY
(
    SELECT MAX(at.transaction_state) AS transaction_state
    FROM sys.dm_tran_session_transactions st
    JOIN sys.dm_tran_active_transactions at ON at.transaction_id=st.transaction_id
    WHERE st.session_id=s.session_id
) t
WHERE s.is_user_process=1 AND s.host_process_id=@pid AND s.session_id<>@@SPID AND r.database_id=DB_ID()
ORDER BY r.total_elapsed_time DESC;";
            try
            {
                await using var command=connection.CreateCommand();command.CommandText=sql;command.CommandTimeout=2;
                command.Parameters.Add("@pid",System.Data.SqlDbType.Int).Value=processId;
                await using var reader=await command.ExecuteReaderAsync(token);
                while(await reader.ReadAsync(token))
                {
                    object? V(int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetValue(ordinal);
                    var sample=new Dictionary<string,object?>(StringComparer.Ordinal)
                    {
                        ["ObservedUtc"]=DateTimeOffset.UtcNow,["Stage"]="DocumentPageApplicationRead",
                        ["SessionId"]=V(0),["LoginTime"]=V(1),["RequestStartUtc"]=V(2),["ElapsedMs"]=V(3),
                        ["BlockingSessionId"]=V(4),["WaitType"]=V(5),["WaitTimeMs"]=V(6),["RequestedResource"]=V(7),
                        ["OpenTransactionCount"]=V(8),["TransactionState"]=V(9),["DatabaseId"]=V(10),["Command"]=V(11)
                    };
                    lock(_sync){if(_samples.Count<160)_samples.Add(sample);}
                }
            }
            catch(Exception ex){_error=Safe(ex);_stop.Cancel();}
        }
        public async ValueTask DisposeAsync(){await StopAsync();_stop.Dispose();}
    }
    internal sealed record ComparatorFieldDiff(string Domain,string FieldName,string ExpectedRawValue,string ObservedRawValue,
        string ExpectedCLRType,string ObservedCLRType,string ExpectedNormalizedValue,string ObservedNormalizedValue,
        string ComparisonRule,string Reason);
    internal sealed record ComparatorResult(bool Matches,IReadOnlyList<ComparatorFieldDiff> Differences);

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "snapshot-guard-regression")
            return Q2SnapshotGuardRegressionTests.Run();
        if(args.Length==4&&args[0] is "recovery-capture-qualification" or "recovery-readonly-rehearsal")
        {
            _root=Path.GetFullPath(args[1]);_runId=args[2];_journal=Path.Combine(_root,"Batch4-Q2H-P-ReadOnly-Journal.jsonl");
            _cs=Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING")??"";
            if(string.IsNullOrWhiteSpace(_cs))
            {
                WriteNewDurable("Batch4-Q2H-P-ReadOnly-Setup-Failure.json",new{ObservedUtc=DateTimeOffset.UtcNow,Status="CONNECTION_ENV_ABSENT",DiagnosticReadOnly=true,RecoveryClaimCreated=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,ConnectionStringPersisted=false});
                return 3;
            }
            CConfig.TKS_Thuc_Tap_V11_Conn_String=_cs;CLogger.Enable_Trace=false;
            State["Batch4QualificationRunId"]=_runId;State["ApplicationIdentity"]=Login;State["ConnectionStringPersisted"]=false;State["CanonicalWorkloadStarted"]=false;
            return args[0]=="recovery-capture-qualification"?await RecoveryCaptureQualificationAsync(args[3]):await RecoveryReadOnlyRehearsalAsync(args[3]);
        }
        if (args.Length == 6 && args[0] == "recovery-offline-replay")
        {
            _root=Path.GetFullPath(args[1]);_runId=args[2];
            return RecoveryComparatorOfflineReplay(args[3],args[4],args[5]);
        }
        if (args.Length == 5 && args[0] == "recovery-diagnostic-followup")
        {
            _root=Path.GetFullPath(args[1]);_runId=args[2];_journal=Path.Combine(_root,"Q2H-D-ReadOnly-Diagnostic-Journal.jsonl");
            _cs=Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING")??"";
            if(string.IsNullOrWhiteSpace(_cs)){Console.Error.WriteLine("Connection environment is absent; no DB action.");return 3;}
            CConfig.TKS_Thuc_Tap_V11_Conn_String=_cs;CLogger.Enable_Trace=false;
            return await RecoveryComparatorReadOnlyFollowupAsync(args[3],args[4]);
        }
        if (args.Length == 4 && args[0] == "recovery-diagnostic")
        {
            _root=Path.GetFullPath(args[1]);_runId=args[2];_journal=Path.Combine(_root,"Q2H-D-ReadOnly-Diagnostic-Journal.jsonl");
            _cs=Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING")??"";
            if(string.IsNullOrWhiteSpace(_cs)){Console.Error.WriteLine("Connection environment is absent; no DB action.");return 3;}
            CConfig.TKS_Thuc_Tap_V11_Conn_String=_cs;CLogger.Enable_Trace=false;
            return await RecoveryComparatorReadOnlyDiagnosticAsync(args[3]);
        }
        if (args.Length != 3 || args[0] is not ("preflight" or "execute" or "catalog" or "baseline" or "recovery-prestate" or "recovery-worker")) return 2;
        _root = Path.GetFullPath(args[1]); _runId = args[2];
        _journal = Path.Combine(_root, "Q2-Operation-Journal.jsonl");
        if (args[0] == "execute")
        {
            try { _executeClaimSha256 = ConsumeExecuteClaim(_root, _runId, CurrentHarnessAssemblySha256()); }
            catch (Exception ex) { Console.Error.WriteLine("Q2 execute claim rejected before DB access: " + Safe(ex)); return 4; }
        }
        if (args[0] == "recovery-worker")
        {
            try
            {
                _validatedRecoveryClaimSha256 = ValidateRecoveryWorkerClaim(_root, _runId, CurrentHarnessAssemblySha256());
                _recoveryPreparationId = PrepareRecoveryWorkerAttempt(_root, _runId, _validatedRecoveryClaimSha256, CurrentHarnessAssemblySha256());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Q2H recovery worker claim rejected before DB access: " + Safe(ex)); return 4;
            }
        }
        _cs = Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING") ?? "";
        if (string.IsNullOrWhiteSpace(_cs))
        {
            Console.Error.WriteLine("Connection environment is absent; no DB action.");
            if (_executeClaimSha256 is not null) WriteExecuteResult(3, "CONNECTION_ENV_ABSENT");
            if (args[0] == "recovery-worker")
            {
                WriteRecoveryPreflightFailure("CONNECTION_VALIDATION_FAILED_NOT_CONSUMED", "CONNECTION_VALIDATION", null);
                return 3;
            }
            return 3;
        }
        CConfig.TKS_Thuc_Tap_V11_Conn_String = _cs;
        CLogger.Enable_Trace = false;
        State["Batch4QualificationRunId"] = _runId;
        State["ApplicationIdentity"] = Login;
        State["ConnectionStringPersisted"] = false;
        State["CanonicalWorkloadStarted"] = false;

        if (args[0] == "catalog")
        {
            await VerifyCatalogAndNoWritesAsync();
            return 0;
        }

        if (args[0] == "recovery-prestate")
            return await CaptureRecoveryPrestateAsync();

        if (args[0] == "recovery-worker")
            return await RecoverFreshQ2Queue11Async();

        if (args[0] == "baseline")
        {
            try
            {
                var e=await CaptureAsync();ValidateRecoveredBaseline(e);
                var currentReport=await CurrentReport((DateTime)e.Identity["BusinessDate"]!,D(e.Scope,"CurrentQuantity"),D(e.Scope,"BalanceReservedQuantity"));
                var receiptDate=DateTime.ParseExact(T(e.Scope,"Q2HReceipt500007DocumentDate"),"yyyy-MM-dd",CultureInfo.InvariantCulture);
                var historicalReport=await HistoricalReport(receiptDate,D(e.Scope,"Q2HReceiptPostedReceiptLedger"),D(e.Scope,"Q2HReceiptPostedIssueLedger"),D(e.Scope,"Q2HReceiptBalanceClosing"));
                var receipt=await VerifyPosted(true,500007,T(e.Scope,"Q2HReceipt500007Number"));
                WriteNew("Batch4-Q2H-Stable-Baseline.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-stable-baseline/2",ObservedUtc=DateTimeOffset.UtcNow,RecoveryRunId=_runId,RecoveryOfQ2RunId="WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621",Status="PASS",BATCH4_Q2H_PARTIAL_STATE_RECONCILED="YES",SnapshotQueueClassification="EXPECTED_PENDING_NOT_BLOCKING",DirectSqlUse="READ_ONLY_POST_RECOVERY_BASELINE_AND_LEDGER_ORACLES",Evidence=e,RetainedReceiptApplicationRead=receipt,CurrentReportApplicationRead=currentReport,HistoricalReportApplicationRead=historicalReport,WholeDatabaseNoUnexpectedMutation="NOT_VERIFIED",LeastPrivilegeBehavior="NOT_VERIFIED"});
                Console.WriteLine(JsonSerializer.Serialize(new{Status="PASS",RecoveryRunId=_runId,Current=e.Scope["CurrentQuantity"],SnapshotVersion=e.Scope["Q2RSnapshotQueueId4RequestedVersion"],MovementQueueMaxId=e.Scope["MovementQueueMaxId"]}));
                return 0;
            }
            catch(Exception ex)
            {
                try{WriteNew("Batch4-Q2H-Stable-Baseline-Failure.json",new{ObservedUtc=DateTimeOffset.UtcNow,RecoveryRunId=_runId,Status="FAIL",Error=Safe(ex),ConnectionStringPersisted=false});}catch{}
                Console.Error.WriteLine("Q2H stable baseline failed: "+Safe(ex));return 1;
            }
        }

        if (args[0] == "preflight")
        {
            try
            {
                LoadStableBaseline();
                var e = await CaptureAsync(); Validate(e);
                WriteNew("Q2-PreState.json", new { SchemaVersion = "warehouse-benchmark-v22-batch4-q2-prestate/3", ObservedUtc = DateTimeOffset.UtcNow, Status = "PASS_PREWRITE_GATE", Batch4QualificationRunId = _runId, StableBaselineSha256=_stableBaselineSha256, DirectSqlUse = "READ_ONLY_PRESTATE_AND_ORACLE_ONLY", Evidence = e });
                Console.WriteLine(JsonSerializer.Serialize(new { Status = "PASS_PREWRITE_GATE", RunId = _runId, Target = e.Identity["DatabaseName"], SqlPrincipal = e.Identity["SqlLogin"], DatabaseUser = e.Identity["DatabaseUser"], Current = e.Scope["CurrentQuantity"], Reserved = e.Scope["BalanceReservedQuantity"], MovementRows = e.Scope["MovementDailyRows"], BalanceRows = e.Scope["BalanceDailyRows"] }));
                return 0;
            }
            catch (Exception ex)
            {
                WriteNew("Q2-Preflight-Failure.json", new { ObservedUtc = DateTimeOffset.UtcNow, Status = "FAIL_BEFORE_FIRST_Q2_WRITE", Batch4QualificationRunId = _runId, Error = Safe(ex), ConnectionStringPersisted = false });
                Console.Error.WriteLine(JsonSerializer.Serialize(new { Status = "FAIL_BEFORE_FIRST_Q2_WRITE", Error = Safe(ex) }));
                return 1;
            }
        }

        var executeExit = await ExecuteWithFirstFailureBoundaryAsync(async () =>
        {
            LoadStableBaseline();
            var prePath = Path.Combine(_root, "Q2-PreState.json");
            using var preJson = JsonDocument.Parse(await File.ReadAllTextAsync(prePath));
            Need(preJson.RootElement.GetProperty("Status").GetString() == "PASS_PREWRITE_GATE" && preJson.RootElement.GetProperty("Batch4QualificationRunId").GetString() == _runId, "Prestate does not pass for this qualification ID.");
            var pre = await CaptureAsync(); Validate(pre);
            WriteNew("Q2-Execution-Recheck.json", new { ObservedUtc = DateTimeOffset.UtcNow, Status = "PASS_IMMEDIATELY_BEFORE_WRITES", Batch4QualificationRunId = _runId, StableBaselineSha256=_stableBaselineSha256, Evidence = pre });
            await RunQ2Async(pre);
            await FinalizeAsync(pre);
            Persist("Q2_WRITE_CORRECTNESS_PASS");
            Console.WriteLine(JsonSerializer.Serialize(new { Status = "Q2_WRITE_CORRECTNESS_PASS", RunId = _runId, ReceiptId = State["PostedReceiptDocumentId"], IssueId = State["PostedIssueDocumentId"], CanonicalWorkloadStarted = false }));
        }, async ex =>
        {
            await PersistFirstFailureAsync(ex,CleanupDraftAsync,()=>CaptureAsync());
            Console.Error.WriteLine(JsonSerializer.Serialize(new { Status = "Q2_PARTIAL_STOPPED_ON_FAILURE", Error = Safe(ex), ReceiptId = State.GetValueOrDefault("PostedReceiptDocumentId"), IssueId = State.GetValueOrDefault("PostedIssueDocumentId"), CanonicalWorkloadStarted = false }));
        });
        WriteExecuteResult(executeExit, executeExit == 0 ? "Q2_WRITE_CORRECTNESS_PASS" : "Q2_PARTIAL_STOPPED_ON_FAILURE");
        return executeExit;
    }

    internal static string CurrentHarnessAssemblySha256()
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location))).ToLowerInvariant();

    internal static string ConsumeExecuteClaim(string root, string runId, string expectedHarnessDllSha256)
    {
        return ConsumeBoundOneShotClaim(root,runId,expectedHarnessDllSha256,"Batch4-Q2-New-OneShot-Claim.json","Batch4-Q2-Execute-Invocation-Consumed.json","warehouse-benchmark-v22-batch4-q2-one-shot-claim/1","Q2-PreState.json","PASS_PREWRITE_GATE","Batch4QualificationRunId","MaximumExecuteInvocations","warehouse-benchmark-v22-batch4-q2-execute-consumed/1");
    }

    internal static string ValidateRecoveryWorkerClaim(string root,string runId,string expectedHarnessDllSha256)
    {
        var claimSha256=ValidateBoundOneShotClaim(root,runId,expectedHarnessDllSha256,"Batch4-Q2H-Recovery-Worker-Claim.json","Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json","warehouse-benchmark-v22-batch4-q2h-recovery-worker-claim/1","Batch4-Q2H-Recovery-Prestate.json","PASS_RECOVERY_PRESTATE","RecoveryRunId","MaximumWorkerInvocations");
        Need(!File.Exists(Path.Combine(root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json")),"Recovery worker one-shot claim is already consumed.");
        return claimSha256;
    }

    internal static string PrepareRecoveryWorkerAttempt(string root,string runId,string claimSha256,string harnessSha256)
    {
        Need(!string.IsNullOrWhiteSpace(claimSha256),"Recovery preparation requires a validated immutable claim.");
        Need(Path.GetFullPath(root).Equals(_root,StringComparison.OrdinalIgnoreCase)&&runId==_runId,"Recovery preparation must use the active run root and identity.");
        var id=Guid.NewGuid().ToString("N");
        var name=$"Batch4-Q2H-Recovery-Preparation-{id}.json";
        WriteNewDurable(name,new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-recovery-preparation/1",RecoveryPreparationId=id,PreparedUtc=DateTimeOffset.UtcNow,RecoveryRunId=runId,TargetQueueId=11,TargetReceiptId=500007,HarnessDllSha256=harnessSha256,RecoveryClaimSha256=claimSha256,Status="PREPARED_NOT_CONSUMED",ClaimConsumed=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,AutomaticRetryAllowed=false});
        _validatedRecoveryClaimSha256=claimSha256;_recoveryPreparationId=id;_recoveryPreparationSha256=HashFile(Path.Combine(_root,name));
        return id;
    }

    internal static string ConsumeRecoveryWorkerClaim(string root,string runId,string expectedHarnessDllSha256)
    {
        Need(!string.IsNullOrWhiteSpace(_recoveryPreparationId),"Recovery claim cannot be consumed without a durable preparation artifact.");
        var validatedClaimSha256=ValidateRecoveryWorkerClaim(root,runId,expectedHarnessDllSha256);
        var preparationPath=Path.Combine(root,$"Batch4-Q2H-Recovery-Preparation-{_recoveryPreparationId}.json");
        Need(File.Exists(preparationPath),"Recovery preparation artifact is missing.");
        Need(_recoveryPreparationSha256 is not null&&HashFile(preparationPath)==_recoveryPreparationSha256,"Recovery preparation artifact changed after durable creation.");
        using(var preparation=JsonDocument.Parse(File.ReadAllText(preparationPath)))
        {
            var p=preparation.RootElement;
            Need(p.GetProperty("Status").GetString()=="PREPARED_NOT_CONSUMED"&&p.GetProperty("ClaimConsumed").GetBoolean()==false,"Recovery preparation is already consumed or invalid.");
            Need(p.GetProperty("RecoveryRunId").GetString()==runId&&p.GetProperty("RecoveryClaimSha256").GetString()==validatedClaimSha256&&p.GetProperty("HarnessDllSha256").GetString()==expectedHarnessDllSha256,"Recovery preparation identity does not match the validated claim and harness.");
        }
        return ConsumeBoundOneShotClaim(root,runId,expectedHarnessDllSha256,"Batch4-Q2H-Recovery-Worker-Claim.json","Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json","warehouse-benchmark-v22-batch4-q2h-recovery-worker-claim/1","Batch4-Q2H-Recovery-Prestate.json","PASS_RECOVERY_PRESTATE","RecoveryRunId","MaximumWorkerInvocations","warehouse-benchmark-v22-batch4-q2h-recovery-worker-consumed/1");
    }

    private static void WriteRecoveryPreflightFailure(string status,string stage,Exception? error)
    {
        Need(!string.IsNullOrWhiteSpace(_recoveryPreparationId),"Preflight failure requires a durable preparation identity.");
        WriteNewDurable($"Batch4-Q2H-Recovery-Preflight-Failure-{_recoveryPreparationId}.json",new
        {
            SchemaVersion="warehouse-benchmark-v22-batch4-q2h-recovery-preflight-failure/1",ObservedUtc=DateTimeOffset.UtcNow,
            RecoveryPreparationId=_recoveryPreparationId,RecoveryRunId=_runId,Status=status,FailedStage=stage,
            Error=error is null?null:Safe(error),ErrorType=error?.GetType().FullName,SqlErrorNumber=error is SqlException sql?sql.Number:(int?)null,
            RecoveryAttemptCompleted=false,NewClaimCreated=false,ExistingClaimValidated=true,ClaimConsumed=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,
            AutomaticRetryAllowed=false,CanonicalPerformanceStarted=false,ConnectionStringPersisted=false
        });
    }


    private static string ConsumeBoundOneShotClaim(string root,string runId,string expectedHarnessDllSha256,string claimFile,string markerFile,string claimSchema,string prestateFile,string prestateStatus,string prestateRunIdProperty,string maximumField,string markerSchema)
    {
        var claimSha256=ValidateBoundOneShotClaim(root,runId,expectedHarnessDllSha256,claimFile,markerFile,claimSchema,prestateFile,prestateStatus,prestateRunIdProperty,maximumField);
        var markerPath=Path.Combine(root,markerFile);Directory.CreateDirectory(root);
        try
        {
            using var marker=new FileStream(markerPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            JsonSerializer.Serialize(marker,new{SchemaVersion=markerSchema,RunId=runId,AttemptNumber=1,InvocationCount=1,ClaimSha256=claimSha256,HarnessDllSha256=expectedHarnessDllSha256,ConsumedUtc=DateTimeOffset.UtcNow},JsonOptions);
            marker.Write(Encoding.UTF8.GetBytes(Environment.NewLine));marker.Flush(true);
        }
        catch(IOException ex){throw new InvalidDataException("One-shot invocation was already consumed or could not be durably claimed.",ex);}
        return claimSha256;
    }

    private static string ValidateBoundOneShotClaim(string root,string runId,string expectedHarnessDllSha256,string claimFile,string markerFile,string claimSchema,string prestateFile,string prestateStatus,string prestateRunIdProperty,string maximumField)
    {
        var claimPath=Path.Combine(root,claimFile);var prestatePath=Path.Combine(root,prestateFile);
        Need(File.Exists(claimPath)&&File.Exists(prestatePath),"One-shot claim or passing prestate is missing.");
        using var claim=JsonDocument.Parse(File.ReadAllText(claimPath));var c=claim.RootElement;
        Need(c.GetProperty("SchemaVersion").GetString()==claimSchema,"Unsupported one-shot claim schema.");
        Need(c.GetProperty("RunId").GetString()==runId&&c.GetProperty("AttemptNumber").GetInt32()==1&&c.GetProperty(maximumField).GetInt32()==1,"One-shot claim identity or invocation limit mismatch.");
        Need(!c.GetProperty("AutomaticRetryAllowed").GetBoolean(),"Automatic retry must remain disabled.");
        Need(string.Equals(Path.GetFullPath(c.GetProperty("PrewriteReadinessPath").GetString()??""),Path.GetFullPath(prestatePath),StringComparison.OrdinalIgnoreCase),"Claim does not reference this run's passing prestate.");
        Need(c.GetProperty("HarnessDllSha256").GetString()==expectedHarnessDllSha256,"Running harness assembly hash differs from the one-shot claim.");
        Need(HashFile(prestatePath)==c.GetProperty("PrewriteReadinessSha256").GetString(),"Prestate hash differs from the one-shot claim.");
        using(var pre=JsonDocument.Parse(File.ReadAllText(prestatePath)))
            Need(pre.RootElement.GetProperty("Status").GetString()==prestateStatus&&pre.RootElement.GetProperty(prestateRunIdProperty).GetString()==runId,"Claimed prestate is not a PASS for this run.");
        return HashFile(claimPath);
    }

    internal static async Task RunRecoveryPreflightAsync(Func<Task> validateConnection,Func<Task> capture,Func<Task> compareGuard)
    {
        await validateConnection();
        await capture();
        await compareGuard();
    }

    internal static async Task<T> RunRecoveryInvocationWithClaimAsync<T>(Func<Task> validateConnection,Func<Task> capture,Func<Task> compareGuard,Func<Action,Task<T>> invokeWorker,Action persistPreInvocation,Func<string> consumeClaim)
    {
        await RunRecoveryPreflightAsync(validateConnection,capture,compareGuard);
        var boundaryEntered=false;var claimConsumed=false;
        var result=await invokeWorker(()=>
        {
            Need(!boundaryEntered,"Recovery one-shot claim boundary may be entered only once.");
            boundaryEntered=true;
            persistPreInvocation();
            var claimSha256=consumeClaim();
            Need(!string.IsNullOrWhiteSpace(claimSha256),"Worker invocation requires a durably consumed one-shot claim.");
            claimConsumed=true;
        });
        Need(claimConsumed,"Worker path returned without consuming its one-shot claim at the invocation boundary.");
        return result;
    }

    internal static async Task<int> ExecuteWithFirstFailureBoundaryAsync(Func<Task> operation, Func<Exception, Task> onFirstFailure)
    {
        try { await operation(); return 0; }
        catch (Exception ex) { await onFirstFailure(ex); return 1; }
    }

    internal static async Task PersistFirstFailureAsync(Exception ex, Func<Task> cleanupDraft, Func<Task<Evidence>> capture)
    {
        Event("Q2_STOP_FIRST_FAILURE", new { Error = Safe(ex) });
        await cleanupDraft();
        try
        {
            var partial = await capture();
            WriteNew("Q2-PartialState.json", new { ObservedUtc = DateTimeOffset.UtcNow, Status = "PARTIAL_STOPPED", Batch4QualificationRunId = _runId, State, Scope = partial.Scope, Worker = partial.Worker, ExistingJob = partial.ExistingJob, CanonicalWorkloadStarted = false, ConnectionStringPersisted = false });
        }
        catch (Exception captureError) { Event("PARTIAL_STATE_CAPTURE_FAILED", new { Error = Safe(captureError) }); }
        Persist("Q2_PARTIAL_STOPPED_ON_FAILURE");
    }

    internal static void ResetForOfflineRegression(string root, string runId)
    {
        _root = Path.GetFullPath(root); _runId = runId; _cs = "OFFLINE_REGRESSION_SECRET";
        _journal = Path.Combine(_root, "Q2-Operation-Journal.jsonl"); _executeClaimSha256 = null; _recoveryClaimSha256=null; _validatedRecoveryClaimSha256=null;_recoveryPreparationId=null;_recoveryPreparationSha256=null;
        _draftHeader=null;_draftDetail=null;_draftReceipt=false;_stableBaseline?.Dispose();_stableBaseline=null;_stableBaselineSha256="";
        State.Clear(); State["Batch4QualificationRunId"] = runId; State["CanonicalWorkloadStarted"] = false;
        Directory.CreateDirectory(_root);
    }

    private static void WriteExecuteResult(int exitCode, string status)
        => WriteNew("Batch4-Q2-Execute-Result.json", new { SchemaVersion = "warehouse-benchmark-v22-batch4-q2-execute-result/1", RunId = _runId, AttemptNumber = 1, ClaimSha256 = _executeClaimSha256, InvocationCount = 1, ProcessExitCode = exitCode, Status = status, ExecuteAttemptConsumed = true, RetryAllowed = false, CanonicalPerformanceStarted = false, FinishedUtc = DateTimeOffset.UtcNow });

    private static void WriteRecoveryWorkerResult(int exitCode,string status,int workerProcedureInvocationCount)
        =>WriteNew("Batch4-Q2H-Recovery-Worker-Execute-Result.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-recovery-worker-result/1",RecoveryRunId=_runId,AttemptNumber=1,ClaimSha256=_recoveryClaimSha256,ExecuteAttemptConsumed=true,RecoveryAttemptInvocationCount=1,WorkerProcedureInvocationCount=workerProcedureInvocationCount,ProcessExitCode=exitCode,Status=status,RetryAllowed=false,CanonicalPerformanceStarted=false,FinishedUtc=DateTimeOffset.UtcNow});

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private const string JobSql = @"
SELECT CONVERT(nvarchar(36),j.job_id) JobId,j.name JobName,j.enabled JobEnabled,SUSER_SNAME(j.owner_sid) JobOwner,
 s.database_name StepDatabase,s.command StepCommand,sch.name ScheduleName,sch.enabled ScheduleEnabled,
 (SELECT COUNT_BIG(*) FROM msdb.dbo.sysjobhistory h WHERE h.job_id=j.job_id AND h.step_id=0) ExecutionHistoryCount
FROM msdb.dbo.sysjobs j LEFT JOIN msdb.dbo.sysjobsteps s ON s.job_id=j.job_id AND s.step_id=1
LEFT JOIN msdb.dbo.sysjobschedules js ON js.job_id=j.job_id LEFT JOIN msdb.dbo.sysschedules sch ON sch.schedule_id=js.schedule_id
WHERE j.job_id=@id;";

    private const string IdentitySql = @"
SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) ServerName, DB_NAME() DatabaseName, DB_ID() DatabaseId,
 (SELECT state_desc FROM sys.databases WHERE database_id=DB_ID()) DatabaseStatus,
 SUSER_SNAME() SqlLogin, ORIGINAL_LOGIN() OriginalLogin, USER_NAME() DatabaseUser,
 IS_SRVROLEMEMBER('sysadmin') IsSysadmin, IS_ROLEMEMBER('db_owner') IsDbOwner, CONVERT(date,SYSDATETIME()) BusinessDate,
 (SELECT COUNT(*) FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap=N'PERF_USER') AccountCount,
 (SELECT MIN(Auto_ID) FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap=N'PERF_USER') AccountId,
 (SELECT MIN(deleted) FROM dbo.tbl_Sys_Thanh_Vien WHERE Ma_Dang_Nhap=N'PERF_USER') AccountDeleted,
 (SELECT COUNT(*) FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap=N'PERF_USER') MappingCount,
 (SELECT COUNT(*) FROM dbo.tbl_DM_Kho_User WHERE Ma_Dang_Nhap=N'PERF_USER' AND Kho_ID=4) Warehouse4Mappings,
 (SELECT COUNT(*) FROM master.sys.server_principals WHERE name=N'PERF_USER') SqlPerfLoginCount,
 (SELECT COUNT(*) FROM sys.database_principals WHERE name=N'PERF_USER') DbPerfPrincipalCount,
 (SELECT COUNT(*) FROM dbo.tbl_DM_Kho WHERE Auto_ID=4) WarehouseExists,
 (SELECT COUNT(*) FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=40) ProductExists,
 (SELECT COUNT(*) FROM dbo.tbl_DM_NCC WHERE Auto_ID=1) SupplierExists;";

    private const string ScopeSql = @"
SELECT CONVERT(date,SYSDATETIME()) BusinessDate,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007) Q2HReceipt500007Rows,
 COALESCE((SELECT CONVERT(int,Is_Posted) FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007),0) Q2HReceipt500007IsPosted,
 COALESCE((SELECT So_Phieu_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007),N'') Q2HReceipt500007Number,
 COALESCE((SELECT CONVERT(char(10),Ngay_Nhap_Kho,23) FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007),N'') Q2HReceipt500007DocumentDate,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID=500007) Q2HReceipt500007DetailRows,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID=500007 AND San_Pham_ID=@p) Q2HReceipt500007ProductDetailRows,
 COALESCE((SELECT SUM(CAST(SL_Nhap AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Nhap_Kho_ID=500007 AND San_Pham_ID=@p),0) Q2HReceipt500007DetailQty,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE N'WHB22B4-WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621-%') Q2HFreshReceiptHeaders,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID=d.Nhap_Kho_ID WHERE h.So_Phieu_Nhap_Kho LIKE N'WHB22B4-WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621-%') Q2HFreshReceiptDetails,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE N'WHB22B4-WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621-%' AND Is_Posted=0) Q2HFreshReceiptDraftHeaders,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID=d.Nhap_Kho_ID WHERE h.So_Phieu_Nhap_Kho LIKE N'WHB22B4-WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621-%' AND h.Is_Posted=0) Q2HFreshReceiptDraftDetails,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho LIKE N'WHB22B4-WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621-%') Q2HFreshIssueHeaders,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID=d.Xuat_Kho_ID WHERE h.So_Phieu_Xuat_Kho LIKE N'WHB22B4-WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621-%') Q2HFreshIssueDetails,
 COALESCE((SELECT CurrentQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID=@w AND San_Pham_ID=@p),0) CurrentQuantity,
 COALESCE((SELECT ReservedQuantity FROM dbo.InventoryBalance_Current WHERE Kho_ID=@w AND San_Pham_ID=@p),0) BalanceReservedQuantity,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryReservation_Current WHERE Kho_ID=@w AND San_Pham_ID=@p) ReservationRowsScope,
 COALESCE((SELECT SUM(ReservedQuantity) FROM dbo.InventoryReservation_Current WHERE Kho_ID=@w AND San_Pham_ID=@p),0) ReservationQuantityScope,
 (SELECT COUNT_BIG(*) FROM dbo.Inventory_Movement_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p) MovementDailyRows,
 (SELECT COUNT_BIG(*) FROM dbo.Inventory_Movement_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND IsValid=1) ValidMovementDailyRows,
 COALESCE((SELECT SUM(Total_Receipt) FROM dbo.Inventory_Movement_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Movement_Date=CONVERT(date,SYSDATETIME())),0) TodayMovementReceipt,
 COALESCE((SELECT SUM(Total_Issue) FROM dbo.Inventory_Movement_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Movement_Date=CONVERT(date,SYSDATETIME())),0) TodayMovementIssue,
 COALESCE((SELECT SUM(CAST(d.SL_Nhap AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Kho_ID=@w AND h.Is_Posted=1 AND d.San_Pham_ID=@p AND h.Ngay_Nhap_Kho=CONVERT(date,SYSDATETIME())),0) TodayPostedReceiptLedger,
 COALESCE((SELECT SUM(CAST(d.SL_Xuat AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Kho_ID=@w AND h.Is_Posted=1 AND d.San_Pham_ID=@p AND h.Ngay_Xuat_Kho=CONVERT(date,SYSDATETIME())),0) TodayPostedIssueLedger,
 (SELECT COUNT_BIG(*) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p) BalanceDailyRows,
 (SELECT COUNT_BIG(*) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND IsValid=1) ValidBalanceDailyRows,
 COALESCE((SELECT MAX(OpeningQuantity) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Balance_Date=CONVERT(date,SYSDATETIME())),0) TodayBalanceOpening,
 COALESCE((SELECT MAX(TotalReceived) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Balance_Date=CONVERT(date,SYSDATETIME())),0) TodayBalanceReceived,
 COALESCE((SELECT MAX(TotalIssued) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Balance_Date=CONVERT(date,SYSDATETIME())),0) TodayBalanceIssued,
 COALESCE((SELECT MAX(ClosingQuantity) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Balance_Date=CONVERT(date,SYSDATETIME())),0) TodayBalanceClosing,
 COALESCE((SELECT SUM(Total_Receipt) FROM dbo.Inventory_Movement_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Movement_Date=(SELECT Ngay_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007)),0) Q2HReceiptMovementReceipt,
 COALESCE((SELECT SUM(Total_Issue) FROM dbo.Inventory_Movement_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Movement_Date=(SELECT Ngay_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007)),0) Q2HReceiptMovementIssue,
 COALESCE((SELECT SUM(CAST(d.SL_Nhap AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Kho_ID=@w AND h.Is_Posted=1 AND d.San_Pham_ID=@p AND h.Ngay_Nhap_Kho=(SELECT Ngay_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007)),0) Q2HReceiptPostedReceiptLedger,
 COALESCE((SELECT SUM(CAST(d.SL_Xuat AS DECIMAL(18,3))) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Kho_ID=@w AND h.Is_Posted=1 AND d.San_Pham_ID=@p AND h.Ngay_Xuat_Kho=(SELECT Ngay_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007)),0) Q2HReceiptPostedIssueLedger,
 COALESCE((SELECT MAX(OpeningQuantity) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Balance_Date=(SELECT Ngay_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007)),0) Q2HReceiptBalanceOpening,
 COALESCE((SELECT MAX(TotalReceived) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Balance_Date=(SELECT Ngay_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007)),0) Q2HReceiptBalanceReceived,
 COALESCE((SELECT MAX(TotalIssued) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Balance_Date=(SELECT Ngay_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007)),0) Q2HReceiptBalanceIssued,
 COALESCE((SELECT MAX(ClosingQuantity) FROM dbo.Inventory_Balance_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p AND Balance_Date=(SELECT Ngay_Nhap_Kho FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=500007)),0) Q2HReceiptBalanceClosing,
 (SELECT COUNT_BIG(*) FROM dbo.Inventory_Report_Scope_Catalog WHERE Kho_ID=@w AND San_Pham_ID=@p) ReportScopeRows,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryBalance_Snapshot_Daily WHERE Kho_ID=@w AND San_Pham_ID=@p) SnapshotRowsScope,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildQueue) MovementQueueRowsGlobal,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildQueue WHERE Status IN(N'WAITING',N'PROCESSING',N'RETRY_WAITING',N'FAILED_FINAL')) MovementQueueActiveGlobal,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID=@w AND San_Pham_ID=@p) MovementQueueRowsScope,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildQueue WHERE Kho_ID=@w AND San_Pham_ID=@p AND Status IN(N'WAITING',N'PROCESSING',N'RETRY_WAITING',N'FAILED_FINAL')) MovementQueueActiveScope,
 COALESCE((SELECT MAX(ID) FROM dbo.InventoryMovement_RebuildQueue),0) MovementQueueMaxId,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildDeadLetter) MovementDeadLettersGlobal,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildDeadLetter WHERE Kho_ID=@w AND San_Pham_ID=@p) MovementDeadLettersScope,
 (SELECT COUNT_BIG(*) FROM dbo.InventorySnapshot_RebuildQueue) SnapshotQueueRowsGlobal,
 (SELECT COUNT_BIG(*) FROM dbo.InventorySnapshot_RebuildQueue WHERE Status IN(N'WAITING',N'PROCESSING',N'FAILED')) SnapshotQueueActiveGlobal,
 (SELECT COUNT_BIG(*) FROM dbo.InventorySnapshot_RebuildDeadLetter) SnapshotDeadLettersGlobal,
 (SELECT COUNT_BIG(*) FROM dbo.InventorySnapshot_RebuildDeadLetter WHERE Kho_ID=@w AND San_Pham_ID=@p) SnapshotDeadLettersScope,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildQueue WHERE ID=10) Q2RMovementQueueId10Rows,
 COALESCE((SELECT Status FROM dbo.InventoryMovement_RebuildQueue WHERE ID=10),N'') Q2RMovementQueueId10Status,
 COALESCE((SELECT Retry_Count FROM dbo.InventoryMovement_RebuildQueue WHERE ID=10),-1) Q2RMovementQueueId10RetryCount,
 COALESCE((SELECT Requested_Version FROM dbo.InventoryMovement_RebuildQueue WHERE ID=10),-1) Q2RMovementQueueId10RequestedVersion,
 COALESCE((SELECT Claimed_Version FROM dbo.InventoryMovement_RebuildQueue WHERE ID=10),-1) Q2RMovementQueueId10ClaimedVersion,
 COALESCE((SELECT CONVERT(char(10),From_Date,23) FROM dbo.InventoryMovement_RebuildQueue WHERE ID=10),N'') Q2RMovementQueueId10FromDate,
 COALESCE((SELECT CONVERT(char(10),To_Date,23) FROM dbo.InventoryMovement_RebuildQueue WHERE ID=10),N'') Q2RMovementQueueId10ToDate,
 (SELECT ProcessedAt FROM dbo.InventoryMovement_RebuildQueue WHERE ID=10) Q2RMovementQueueId10ProcessedAt,
 (SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) Q2HMovementQueueId11Rows,
 COALESCE((SELECT Status FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11),N'') Q2HMovementQueueId11Status,
 COALESCE((SELECT Retry_Count FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11),-1) Q2HMovementQueueId11RetryCount,
 COALESCE((SELECT Requested_Version FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11),-1) Q2HMovementQueueId11RequestedVersion,
 COALESCE((SELECT Claimed_Version FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11),-1) Q2HMovementQueueId11ClaimedVersion,
 (SELECT LastAttemptAt FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) Q2HMovementQueueId11LastAttemptAt,
 (SELECT NextRetryAt FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) Q2HMovementQueueId11NextRetryAt,
 (SELECT LastError FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) Q2HMovementQueueId11LastError,
 (SELECT ErrorMessage FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) Q2HMovementQueueId11ErrorMessage,
 (SELECT CreatedAt FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) Q2HMovementQueueId11CreatedAt,
 COALESCE((SELECT CONVERT(char(10),From_Date,23) FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11),N'') Q2HMovementQueueId11FromDate,
 COALESCE((SELECT CONVERT(char(10),To_Date,23) FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11),N'') Q2HMovementQueueId11ToDate,
 (SELECT ProcessedAt FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) Q2HMovementQueueId11ProcessedAt,
 (SELECT COUNT_BIG(*) FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4Rows,
 COALESCE((SELECT Status FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4),N'') Q2RSnapshotQueueId4Status,
 COALESCE((SELECT AttemptCount FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4),-1) Q2RSnapshotQueueId4AttemptCount,
 COALESCE((SELECT RequestType FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4),N'') Q2RSnapshotQueueId4RequestType,
 COALESCE((SELECT LifecycleStatus FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4),N'') Q2RSnapshotQueueId4LifecycleStatus,
 COALESCE((SELECT Kho_ID FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4),-1) Q2RSnapshotQueueId4WarehouseId,
 COALESCE((SELECT San_Pham_ID FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4),-1) Q2RSnapshotQueueId4ProductId,
 COALESCE((SELECT CONVERT(char(10),From_Date,23) FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4),N'') Q2RSnapshotQueueId4FromDate,
 COALESCE((SELECT Requested_Version FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4),-1) Q2RSnapshotQueueId4RequestedVersion,
 (SELECT Claimed_Version FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4ClaimedVersion,
 (SELECT ClaimedBy FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4ClaimedBy,
 (SELECT ClaimedAt FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4ClaimedAt,
 (SELECT LeaseUntil FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4LeaseUntil,
 (SELECT NextAttemptAt FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4NextAttemptAt,
 (SELECT LastAttemptAt FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4LastAttemptAt,
 (SELECT CompletedAt FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4CompletedAt,
 (SELECT ErrorMessage FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4ErrorMessage,
 (SELECT LastError FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4LastError,
 (SELECT CreatedAt FROM dbo.InventorySnapshot_RebuildQueue WHERE ID=4) Q2RSnapshotQueueId4CreatedAt,
 (SELECT COUNT_BIG(*) FROM dbo.InventorySnapshot_BootstrapAudit a WHERE a.Status=N'COMPLETED' AND a.Baseline_Date<=(SELECT q.From_Date FROM dbo.InventorySnapshot_RebuildQueue q WHERE q.ID=4) AND (a.Kho_ID IS NULL OR a.Kho_ID=@w) AND (a.San_Pham_ID IS NULL OR a.San_Pham_ID=@p)) CompletedBootstrapAuditEligibleForScope;";

    private static async Task<Evidence> CaptureAsync(CaptureTimingContext? timing=null)
    {
        await MeasureCaptureStageAsync(timing,"ConnectionStringContract",()=>
        {
            var b=new SqlConnectionStringBuilder(_cs);
            Need(b.IntegratedSecurity&&string.IsNullOrWhiteSpace(b.UserID)&&string.IsNullOrWhiteSpace(b.Password),"Connection no longer matches Integrated Security contract.");
            return Task.FromResult(true);
        },_=>1);
        await using var c=new SqlConnection(_cs);
        await MeasureCaptureStageAsync(timing,"SqlConnectionOpen",()=>c.OpenAsync());
        var id=await MeasureCaptureStageAsync(timing,"DatabaseIdentityRead",()=>One(c,IdentitySql,_=>{}),x=>x.Count);
        var scope=await MeasureCaptureStageAsync(timing,"BoundedRecoveryScopeRead",()=>One(c,ScopeSql,q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);}),x=>x.Count);
        var worker=await MeasureCaptureStageAsync(timing,"DedicatedWorkerJobRead",()=>Job(c,PerfJob),x=>x.Count);
        var existing=await MeasureCaptureStageAsync(timing,"ExistingGiaiDoan2JobRead",()=>Job(c,GiaiDoan2Job),x=>x.Count);
        await MeasureCaptureStageAsync(timing,"TargetMovementProcedureJobCountRead",async()=>
        {
            var count=Convert.ToInt32(await Scalar(c,"SELECT COUNT(*) FROM msdb.dbo.sysjobsteps WHERE database_name=N'TKS_Thuc_Tap_V11_Perf_10000000' AND command LIKE N'%sp_Inventory_Movement_Process_RebuildQueue%';"));
            worker["TargetMovementJobsCount"]=count;return count;
        },_=>1);
        var maps=await MeasureCaptureStageAsync(timing,"WarehousePermissionApplicationRead",()=>Task.FromResult(new CDM_Kho_User_Controller().sp_DM_Kho_User_List_Allowed(Login)),x=>x.Count);
        var allowed = maps.Select(x => x.Auto_ID).ToHashSet();
        var docs=await MeasureCaptureStageAsync(timing,"DocumentPageApplicationRead",()=>Docs.List_Documents_Page_Async(true,1,10,"",Login,null),x=>x.Items.Count);
        var app=await MeasureCaptureStageAsync(timing,"ApplicationPathSemanticGuard",()=>Task.FromResult(new Dictionary<string,object?>
        {
            ["Status"] = maps.Count==100 && allowed.Contains(Warehouse) && docs.Total_Count>0 && docs.Items.Count>0 && docs.Items.All(x=>allowed.Contains(x.Kho_ID)) ? "PASS" : "FAIL",
            ["ApplicationIdentity"] = Login, ["WarehouseMappingCount"] = maps.Count,
            ["Warehouse4Allowed"] = allowed.Contains(Warehouse), ["DocumentTotalCount"] = docs.Total_Count,
            ["DocumentRows"] = docs.Items.Count, ["UnauthorizedRows"] = docs.Items.Count(x=>!allowed.Contains(x.Kho_ID))
        }),x=>x.Count);
        var assembly = typeof(CXNK_Nhap_Kho_Controller).Assembly.Location;
        var hash=await MeasureCaptureStageAsync(timing,"DataAccessAssemblySha256",async()=>Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assembly))).ToLowerInvariant(),x=>x.Length);
        return new Evidence(id,scope,worker,existing,app,hash);
    }

    private static async Task<T> MeasureCaptureStageAsync<T>(CaptureTimingContext? timing,string stage,Func<Task<T>> operation,Func<T,int>? resultCount=null)
    {
        var started=DateTimeOffset.UtcNow;var elapsed=Stopwatch.StartNew();timing?.StageBoundary?.Invoke(stage,true);
        try
        {
            var result=await operation();elapsed.Stop();timing?.Stages.Add(new(stage,timing.Stages.Count+1,started,DateTimeOffset.UtcNow,elapsed.ElapsedMilliseconds,"PASS",resultCount?.Invoke(result),null,null));return result;
        }
        catch(Exception ex)
        {
            elapsed.Stop();timing?.Stages.Add(new(stage,timing.Stages.Count+1,started,DateTimeOffset.UtcNow,elapsed.ElapsedMilliseconds,"FAIL",null,ex.GetType().FullName,ex is SqlException sql?sql.Number:null));throw;
        }
        finally{timing?.StageBoundary?.Invoke(stage,false);}
    }

    private static async Task MeasureCaptureStageAsync(CaptureTimingContext? timing,string stage,Func<Task> operation)
    {
        await MeasureCaptureStageAsync(timing,stage,async()=>{await operation();return true;},_=>0);
    }

    private static void ValidateReadOnlyDiagnosticRoot()
    {
        Need(!File.Exists(Path.Combine(_root,"Batch4-Q2-New-OneShot-Claim.json"))&&!File.Exists(Path.Combine(_root,"Batch4-Q2-Execute-Invocation-Consumed.json")),"Q2H-P read-only root must not contain a Q2 claim or consumed marker.");
        Need(!File.Exists(Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Claim.json"))&&!File.Exists(Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json")),"Q2H-P read-only root must not contain a recovery claim or consumed marker.");
        Need(!Directory.EnumerateFiles(_root,"Batch4-Q2H-Recovery-Preparation-*.json").Any(),"Q2H-P read-only root must not contain a recovery preparation from a different attempt.");
    }

    private static void ValidateImmutableRecoveryPrestate(string prestatePath,out JsonDocument prestate,out string claimSha256)
    {
        var fullPath=Path.GetFullPath(prestatePath);Need(File.Exists(fullPath),"Immutable Q2H recovery prestate is missing.");
        prestate=JsonDocument.Parse(File.ReadAllText(fullPath));var root=prestate.RootElement;
        Need(JsonString(root,"Status")=="PASS_RECOVERY_PRESTATE","Q2H qualification input is not a passing recovery prestate.");
        var referenceRoot=Path.GetDirectoryName(fullPath)??throw new InvalidDataException("Recovery prestate has no parent directory.");
        var claimPath=Path.Combine(referenceRoot,"Batch4-Q2H-Recovery-Worker-Claim.json");
        Need(File.Exists(claimPath),"Immutable Q2H recovery claim reference is missing.");
        using var claim=JsonDocument.Parse(File.ReadAllText(claimPath));var c=claim.RootElement;
        Need(c.GetProperty("SchemaVersion").GetString()=="warehouse-benchmark-v22-batch4-q2h-recovery-worker-claim/1"&&c.GetProperty("RunId").GetString()==JsonString(root,"RecoveryRunId"),"Q2H reference claim identity does not match the prestate.");
        Need(c.GetProperty("AutomaticRetryAllowed").GetBoolean()==false&&c.GetProperty("MaximumWorkerInvocations").GetInt32()==1,"Q2H reference claim violates the one-shot no-retry contract.");
        var prestateSha=HashFile(fullPath);Need(string.Equals(c.GetProperty("PrewriteReadinessSha256").GetString(),prestateSha,StringComparison.OrdinalIgnoreCase),"Immutable Q2H prestate no longer matches the hash bound into its claim.");
        var consumedPath=Path.Combine(referenceRoot,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json");
        Need(File.Exists(consumedPath),"Historical recovery reference is not proven consumed; refusing to use it as failed-attempt evidence.");
        claimSha256=HashFile(claimPath);
    }

    private static (bool Pass,int Expected,int Observed,List<object> Domains) CompareGuardDomains(Evidence actual,JsonElement expected,JsonElement? expectedTypes)
    {
        var domains=new List<object>();var expectedTotal=0;var observedTotal=0;var pass=true;
        foreach(var domain in new[]{"Scope","Identity","Worker","ExistingJob"})
        {
            var observed=domain switch{"Scope"=>actual.Scope,"Identity"=>actual.Identity,"Worker"=>actual.Worker,_=>actual.ExistingJob};
            var expectedDomain=expected.GetProperty(domain);var expectedCount=expectedDomain.EnumerateObject().Count();
            JsonElement? typeDomain=expectedTypes is {ValueKind:JsonValueKind.Object} td&&td.TryGetProperty(domain,out var found)?found:(JsonElement?)null;
            var result=CompareBaselineDetailed(domain,observed,expectedDomain,typeDomain);
            var fieldCountPass=observed.Count==expectedCount;var domainPass=result.Matches&&fieldCountPass;
            expectedTotal+=expectedCount;observedTotal+=observed.Count;pass&=domainPass;
            domains.Add(new{Domain=domain,ExpectedFieldCount=expectedCount,ObservedFieldCount=observed.Count,AllExpectedFieldsPresent=fieldCountPass,Status=domainPass?"PASS":"FAIL",Differences=result.Differences,StructuredDiffAvailable=true});
        }
        return(pass,expectedTotal,observedTotal,domains);
    }

    private static CaptureComparison CompareCaptureAgainstPrestate(Evidence actual,JsonElement prestateRoot)
    {
        var expected=prestateRoot.GetProperty("Evidence");
        var expectedTypes=prestateRoot.TryGetProperty("EvidenceClrTypes",out var types)?types:(JsonElement?)null;
        var guard=CompareGuardDomains(actual,expected,expectedTypes);
        var dataAccessPass=expected.TryGetProperty("DataAccessAssemblySha256",out var expectedDataAccessHash)&&string.Equals(expectedDataAccessHash.GetString(),actual.DataAccessAssemblySha256,StringComparison.OrdinalIgnoreCase);
        return new CaptureComparison(guard.Pass&&dataAccessPass,guard.Expected,guard.Observed,dataAccessPass,guard.Domains);
    }

    private static async Task<int> RecoveryCaptureQualificationAsync(string prestatePath)
    {
        ValidateReadOnlyDiagnosticRoot();
        ValidateImmutableRecoveryPrestate(prestatePath,out var prestate,out var claimSha256);
        using(prestate)
        {
            var attempts=new List<(string RunId,DateTimeOffset StartedUtc,DateTimeOffset FinishedUtc,long DurationMs,string Status,string? FailureClassification,CaptureStageTiming[] Stages,CaptureDmvSnapshot Dmv,CaptureComparison? Comparison,Evidence? Evidence,string? Error)>();
            for(var index=1;index<=5;index++)
            {
                var captureRunId=$"{_runId}-CAPTURE-{index:D2}";var started=DateTimeOffset.UtcNow;var stopwatch=Stopwatch.StartNew();
                await using var observer=new CaptureDmvObserver(_cs,Environment.ProcessId);await observer.StartAsync();
                var timing=new CaptureTimingContext(observer.StageBoundary);Evidence? evidence=null;CaptureComparison? comparison=null;Exception? failure=null;string status;
                try
                {
                    evidence=await CaptureAsync(timing);
                    var prestateGatePassed=true;string? prestateGateError=null;
                    try{ValidateRecoveryPrestate(evidence);}catch(Exception ex){prestateGatePassed=false;prestateGateError=Safe(ex);}
                    comparison=CompareCaptureAgainstPrestate(evidence,prestate.RootElement);
                    status=prestateGatePassed&&comparison.Pass?"PASS":"GUARD_FAILED";
                    if(!prestateGatePassed)failure=new InvalidDataException(prestateGateError);
                }
                catch(Exception ex){failure=ex;status="CAPTURE_FAILED";}
                await observer.StopAsync();stopwatch.Stop();var finished=DateTimeOffset.UtcNow;var dmv=observer.Snapshot();
                var failedStage=timing.Stages.LastOrDefault(x=>x.Status=="FAIL")?.Stage;
                var failureClass=failure is SqlException sql&&sql.Number==-2
                    ?failedStage=="SqlConnectionOpen"?"CONNECTION_TIMEOUT":failedStage=="DocumentPageApplicationRead"?"ACTUAL_QUERY_TIMEOUT":"ACTUAL_QUERY_TIMEOUT"
                    :failure is OperationCanceledException or TaskCanceledException?"CLIENT_CANCELLATION":failure is null?null:"GUARD_OR_HARNESS_FAILURE";
                var stageRows=timing.Stages.ToArray();
                var attempt=new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-p-capture-run/1",DiagnosticRunId=_runId,CaptureRunId=captureRunId,StartedUtc=started,FinishedUtc=finished,DurationMs=stopwatch.ElapsedMilliseconds,Status=status,FailedStage=failedStage,FailureClassification=failureClass,Error=failure is null?null:Safe(failure),ErrorType=failure?.GetType().FullName,SqlErrorNumber=failure is SqlException se?se.Number:(int?)null,StageCount=stageRows.Length,StageTimings=stageRows,FieldCounts=evidence is null?null:new{Identity=evidence.Identity.Count,Scope=evidence.Scope.Count,Worker=evidence.Worker.Count,ExistingJob=evidence.ExistingJob.Count,AppPath=evidence.AppPath.Count},Comparator=evidence is null?null:comparison,DataAccessAssemblySha256=evidence?.DataAccessAssemblySha256,Evidence=evidence,BlockingObserved=dmv.BlockingObserved,BlockingEvidence=dmv,DiagnosticReadOnly=true,NewClaimCreated=false,ClaimConsumed=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,AgentStarted=false,CanonicalPerformanceStarted=false,ConnectionStringPersisted=false};
                WriteNewDurable($"Batch4-Q2H-P-CaptureRun-{index:D2}.json",attempt);
                attempts.Add((captureRunId,started,finished,stopwatch.ElapsedMilliseconds,status,failureClass,stageRows,dmv,comparison,evidence,failure is null?null:Safe(failure)));
            }
            var passes=attempts.Count(x=>x.Status=="PASS");var failures=attempts.Count-passes;
            var worst=attempts.Where(x=>x.Status=="PASS").SelectMany(x=>x.Stages.Select(s=>new{ x.RunId,s.Stage,s.DurationMs })).OrderByDescending(x=>x.DurationMs).FirstOrDefault();
            var allPass=passes==5&&failures==0;
            WriteNewDurable("Batch4-Q2H-P-FullCapture-Qualification.json",new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2h-p-full-capture-qualification/1",ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,
                Status=allPass?"PASS_5_OF_5":"FAIL_BOUNDED_5_RUN_SET",DiagnosticReadOnly=true,AttemptLimit=5,Attempts=5,Passes=passes,Failures=failures,PerRequestRetry=false,
                ImmutablePrestatePath=Path.GetFullPath(prestatePath),ImmutablePrestateSha256=HashFile(prestatePath),HistoricalClaimSha256=claimSha256,
                CaptureAsyncStageCount=11,ExpectedComparatorDomainCount=4,Int16SupportedByRegression=true,ByteSupportedByRegression=true,StructuredDiffAvailable=true,
                WorstSuccessfulStage=worst,BlockingObserved=attempts.Any(x=>x.Dmv.BlockingObserved),DmvSamples=attempts.Sum(x=>x.Dmv.SampleCount),ObserverErrors=attempts.Count(x=>x.Dmv.Error is not null),
                AttemptsSummary=attempts.Select(x=>new{ x.RunId,x.StartedUtc,x.FinishedUtc,x.DurationMs,x.Status,x.FailureClassification,StageCount=x.Stages.Length,ComparatorStatus=x.Comparison?.Pass==true?"PASS":x.Comparison is null?"NOT_RUN":"FAIL",BlockingObserved=x.Dmv.BlockingObserved,DmvSampleCount=x.Dmv.SampleCount,DmvObserverError=x.Dmv.Error,FieldCounts=x.Evidence is null?null:new{Identity=x.Evidence.Identity.Count,Scope=x.Evidence.Scope.Count,Worker=x.Evidence.Worker.Count,ExistingJob=x.Evidence.ExistingJob.Count,AppPath=x.Evidence.AppPath.Count}}),
                RecoveryClaimCreated=false,RecoveryClaimConsumed=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,AgentStarted=false,CanonicalPerformanceStarted=false,ConnectionStringPersisted=false
            });
            WriteNewDurable("Batch4-Q2H-P-Capture-Stage-Timing.json",new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2h-p-capture-stage-timing/1",ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,
                CaptureAsyncStageCount=11,Attempts=attempts.Select(x=>new{CaptureRunId=x.RunId,Status=x.Status,StartedUtc=x.StartedUtc,FinishedUtc=x.FinishedUtc,DurationMs=x.DurationMs,Stages=x.Stages,DmvObserver=x.Dmv}),WorstSuccessfulStage=worst,
                ConnectionStringPersisted=false,RecoveryClaimCreated=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0
            });
            if(!allPass)
            {
                try
                {
                    var finalState=await CaptureRecoveryGuardStateAsync();
                    WriteNewDurable("Batch4-Q2H-P-Final-DB-State-ReadOnly.json",new{ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,Status="PASS_BOUNDED_DIRECT_READ",CaptureMethod="CaptureRecoveryGuardStateAsync",Identity=finalState.Identity,Scope=finalState.Scope,Worker=finalState.Worker,ExistingJob=finalState.ExistingJob,CaptureClrTypes=CaptureClrTypes(finalState),RecoveryClaimCreated=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0});
                }
                catch(Exception ex){WriteNewDurable("Batch4-Q2H-P-Final-DB-State-ReadOnly.json",new{ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,Status="FAILED_BOUNDED_DIRECT_READ",Error=Safe(ex),RecoveryClaimCreated=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0});}
            }
            Console.WriteLine(JsonSerializer.Serialize(new{Status=allPass?"PASS_5_OF_5":"FAIL_BOUNDED_5_RUN_SET",Attempts=5,Passes=passes,Failures=failures,BlockingObserved=attempts.Any(x=>x.Dmv.BlockingObserved),WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0}));
            return allPass?0:1;
        }
    }

    private static async Task<int> RecoveryReadOnlyRehearsalAsync(string prestatePath)
    {
        ValidateReadOnlyDiagnosticRoot();
        var captureGatePath=Path.Combine(_root,"Batch4-Q2H-P-FullCapture-Qualification.json");
        Need(File.Exists(captureGatePath),"Final read-only rehearsal requires the bounded full CaptureAsync qualification artifact.");
        using var captureGate=JsonDocument.Parse(File.ReadAllText(captureGatePath));
        Need(captureGate.RootElement.GetProperty("Status").GetString()=="PASS_5_OF_5","Final read-only rehearsal is blocked until all five full CaptureAsync runs pass.");
        ValidateImmutableRecoveryPrestate(prestatePath,out var prestate,out var claimSha256);
        using(prestate)
        {
            var rehearsalId=Guid.NewGuid().ToString("N");var started=DateTimeOffset.UtcNow;Evidence? before=null;Evidence? after=null;
            CaptureStageTiming[] stageTimings=[];CaptureDmvSnapshot? dmv=null;object? workerGate=null;string status="FAIL_READ_ONLY_REHEARSAL";string? error=null;bool prestatePass=false;bool finalStatePass=false;
            try
            {
                await ValidateRecoveryConnectionAsync();
                var observer=new CaptureDmvObserver(_cs,Environment.ProcessId);await observer.StartAsync();
                var timing=new CaptureTimingContext(observer.StageBoundary);
                try{before=await CaptureAsync(timing);}finally{await observer.StopAsync();dmv=observer.Snapshot();}
                stageTimings=timing.Stages.ToArray();
                ValidateRecoveryPrestate(before);
                var currentComparison=CompareCaptureAgainstPrestate(before,prestate.RootElement);prestatePass=currentComparison.Pass;
                Need(prestatePass,"Final rehearsal full CaptureAsync did not match the immutable recovery prestate.");
                var preInvocationPath=Path.Combine(_root,"Batch4-Q2H-P-Rehearsal-PreInvocation-Gate.json");
                workerGate=await DrainWorker(11,before.Scope,L(before.Worker,"ExecutionHistoryCount"),stopBeforeProcedure:true,onRehearsalGate:scope=>
                {
                    WriteNewDurable(Path.GetFileName(preInvocationPath),new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-p-preinvocation-gate/1",ObservedUtc=DateTimeOffset.UtcNow,RehearsalId=rehearsalId,DiagnosticRunId=_runId,Status="PASS_READ_ONLY_GATE",QueueId=11,Procedure="dbo.sp_Inventory_Movement_Process_RebuildQueue",WorkerPreInvocationScope=scope,ClaimCreated=false,ClaimConsumed=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,ConnectionStringPersisted=false});
                });
                Need(JsonSerializer.SerializeToElement(workerGate).GetProperty("Status").GetString()=="PASS_READ_ONLY_PREINVOCATION_REHEARSAL","Worker read-only pre-invocation gate did not pass.");
                after=await CaptureRecoveryGuardStateAsync();
                var guard=CompareGuardDomains(after,prestate.RootElement.GetProperty("Evidence"),prestate.RootElement.TryGetProperty("EvidenceClrTypes",out var expectedTypes)?expectedTypes:(JsonElement?)null);
                finalStatePass=guard.Pass;
                Need(finalStatePass,"Final bounded direct state read differs from the immutable recovery prestate.");
                Need(_recoveryClaimSha256 is null&&!File.Exists(Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Claim.json"))&&!File.Exists(Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json")),"Read-only rehearsal unexpectedly created or consumed a recovery claim.");
                status="PASS";
                WriteNewDurable("Batch4-Q2H-P-Final-ReadOnly-Rehearsal.json",new
                {
                    SchemaVersion="warehouse-benchmark-v22-batch4-q2h-p-final-readonly-rehearsal/1",ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,RehearsalId=rehearsalId,Status=status,
                    DiagnosticReadOnly=true,ConnectionValidation="PASS",FullCaptureAsyncStatus="PASS",FullCaptureStageCount=stageTimings.Length,FullCaptureStages=stageTimings,DmvObserver=dmv,
                    PrestateComparison=currentComparison,WorkerReadOnlyPreInvocationGate=workerGate,PreInvocationGateArtifact=preInvocationPath,
                    FinalBoundedStateComparison=guard,Receipt500007=new{Status=I(after.Scope,"Q2HReceipt500007IsPosted")==1?"POSTED":"NOT_POSTED",Quantity=after.Scope["Q2HReceipt500007DetailQty"]},
                    CurrentQuantity=after.Scope["CurrentQuantity"],ReservedQuantity=after.Scope["BalanceReservedQuantity"],Queue11=new{Status=after.Scope["Q2HMovementQueueId11Status"],RetryCount=after.Scope["Q2HMovementQueueId11RetryCount"],ClaimedVersion=after.Scope["Q2HMovementQueueId11ClaimedVersion"],RequestedVersion=after.Scope["Q2HMovementQueueId11RequestedVersion"]},
                    SnapshotQueue4=new{Status=after.Scope["Q2RSnapshotQueueId4Status"],LifecycleStatus=after.Scope["Q2RSnapshotQueueId4LifecycleStatus"],RequestedVersion=after.Scope["Q2RSnapshotQueueId4RequestedVersion"]},DeadLetters=after.Scope["MovementDeadLettersGlobal"],
                    HistoricalClaimSha256=claimSha256,RecoveryClaimCreated=false,RecoveryClaimConsumed=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,AgentStarted=false,CanonicalPerformanceStarted=false,ConnectionStringPersisted=false,
                    CaptureAtomicity="POTENTIAL_TORN_READ_NOT_OBSERVED_IN_BOUNDED_COMPARISON",WholeDatabaseNoUnexpectedMutation="NOT_VERIFIED"
                });
            }
            catch(Exception ex)
            {
                error=Safe(ex);status="FAIL_READ_ONLY_REHEARSAL";
                WriteNewDurable("Batch4-Q2H-P-Final-ReadOnly-Rehearsal.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-p-final-readonly-rehearsal/1",ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,RehearsalId=rehearsalId,Status=status,Error=error,PrestateComparisonStatus=prestatePass?"PASS":"FAIL_OR_NOT_RUN",FinalStateComparisonStatus=finalStatePass?"PASS":"FAIL_OR_NOT_RUN",FullCaptureStages=stageTimings,DmvObserver=dmv,WorkerReadOnlyPreInvocationGate=workerGate,FinalState=after is null?null:new{after.Identity,after.Scope,after.Worker,after.ExistingJob},RecoveryClaimCreated=false,RecoveryClaimConsumed=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,AgentStarted=false,CanonicalPerformanceStarted=false,ConnectionStringPersisted=false});
            }
            Console.WriteLine(JsonSerializer.Serialize(new{Status=status,RehearsalId=rehearsalId,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,Error=error}));
            return status=="PASS"?0:1;
        }
    }

    private static async Task VerifyCatalogAndNoWritesAsync()
    {
        var e=await CaptureAsync();
        await using var c=new SqlConnection(_cs);await c.OpenAsync();
        var procedures=await Rows(c,@"SELECT name,type_desc FROM sys.objects WHERE type IN('P','PC') AND name IN
 (N'F3001_sp_ins_Nhap_Kho_Header',N'F3001_sp_ins_Nhap_Kho_Detail',N'F3001_sp_del_Nhap_Kho_Header',N'F3001_sp_del_Nhap_Kho_Detail',N'F3001_sp_sel_List_Nhap_Kho_Detail',N'F3002_sp_ins_Xuat_Kho_Header',N'F3002_sp_ins_Xuat_Kho_Detail',N'F3002_sp_upd_Xuat_Kho_Detail',N'F3002_sp_del_Xuat_Kho_Header',N'F3002_sp_del_Xuat_Kho_Detail',N'F3002_sp_sel_List_Xuat_Kho_Detail',N'sp_DM_Kho_User_List_Allowed',N'sp_XNK_Document_Post',N'sp_XNK_Document_Page',N'sp_BC_Ton_Kho_Hien_Tai_Page',N'sp_BC_Xuat_Nhap_Ton_Page',N'sp_Inventory_Movement_Process_RebuildQueue') ORDER BY name;",_=>{});
        var similar=await Rows(c,"SELECT name,type_desc FROM sys.objects WHERE type IN('P','PC') AND (name LIKE N'%Nhap_Kho_Header%' OR name LIKE N'%F2011%') ORDER BY name;",_=>{});
        var workerDefinition=Convert.ToString(await Scalar(c,"SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_Inventory_Movement_Process_RebuildQueue'))"),CultureInfo.InvariantCulture)??"";
        var workerDefinitionSha256=workerDefinition.Length==0?"":Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(workerDefinition)));
        var workerCanExecute=Convert.ToInt32(await Scalar(c,"SELECT HAS_PERMS_BY_NAME(N'dbo.sp_Inventory_Movement_Process_RebuildQueue',N'OBJECT',N'EXECUTE')"),CultureInfo.InvariantCulture);
        var docs=await One(c,@"SELECT (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho LIKE @prefix) ReceiptHeaders,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID=d.Nhap_Kho_ID WHERE h.So_Phieu_Nhap_Kho LIKE @prefix) ReceiptDetails,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho LIKE @prefix) IssueHeaders,
 (SELECT COUNT_BIG(*) FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID=d.Xuat_Kho_ID WHERE h.So_Phieu_Xuat_Kho LIKE @prefix) IssueDetails;",q=>Param(q,"@prefix",System.Data.SqlDbType.NVarChar,"WHB22B4-"+_runId+"-%",100));
        string[] expectedNames={"F3001_sp_ins_Nhap_Kho_Header","F3001_sp_ins_Nhap_Kho_Detail","F3001_sp_del_Nhap_Kho_Header","F3001_sp_del_Nhap_Kho_Detail","F3001_sp_sel_List_Nhap_Kho_Detail","F3002_sp_ins_Xuat_Kho_Header","F3002_sp_ins_Xuat_Kho_Detail","F3002_sp_upd_Xuat_Kho_Detail","F3002_sp_del_Xuat_Kho_Header","F3002_sp_del_Xuat_Kho_Detail","F3002_sp_sel_List_Xuat_Kho_Detail","sp_DM_Kho_User_List_Allowed","sp_XNK_Document_Post","sp_XNK_Document_Page","sp_BC_Ton_Kho_Hien_Tai_Page","sp_BC_Xuat_Nhap_Ton_Page","sp_Inventory_Movement_Process_RebuildQueue"};
        var found=procedures.Select(x=>Convert.ToString(x["name"],CultureInfo.InvariantCulture)??"").ToHashSet(StringComparer.OrdinalIgnoreCase);var missing=expectedNames.Where(x=>!found.Contains(x)).ToArray();
        var noRunDocuments=Convert.ToInt64(docs["ReceiptHeaders"])==0&&Convert.ToInt64(docs["ReceiptDetails"])==0&&Convert.ToInt64(docs["IssueHeaders"])==0&&Convert.ToInt64(docs["IssueDetails"])==0;
        var status=missing.Length>0?"BLOCKED_MISSING_REQUIRED_APPLICATION_STORED_PROCEDURES":!noRunDocuments?"BLOCKED_RUN_SCOPED_DOCUMENTS_ALREADY_EXIST":T(e.AppPath,"Status")!="PASS"?"BLOCKED_READ_ONLY_APPLICATION_PROBE":"PASS_CATALOG_AND_NO_RUN_DOCUMENTS";
        var artifact=new{SchemaVersion="warehouse-benchmark-v22-batch4-q2-catalog-recheck/4",ObservedUtc=DateTimeOffset.UtcNow,Batch4QualificationRunId=_runId,Status= status,TargetIdentity=e.Identity,CurrentScope=e.Scope,DedicatedWorker=e.Worker,ExistingGiaiDoan2Job=e.ExistingJob,CurrentWorkerProcedure=new{Name="dbo.sp_Inventory_Movement_Process_RebuildQueue",DefinitionPresent=workerDefinition.Length>0,DefinitionSha256=workerDefinitionSha256,CanExecute=workerCanExecute==1,DirectExecutionCommand="EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size = 100;"},ExpectedQ2DirectProcedureRootCount=expectedNames.Length,PresentQ2DirectProcedureRootCount=procedures.Count,MissingQ2DirectProcedureRoots=missing,RequiredQ2ProceduresInTargetCatalog=procedures,RelatedReceiptHeaderProcedureNames=similar,RunDocumentCounts=docs,NoRunScopedDocuments=noRunDocuments,ApplicationPathProbe=e.AppPath,ApplicationWritesExecuted=0,CanonicalWorkloadStarted=false,DirectSqlUse="READ_ONLY_CATALOG_IDENTITY_CATALOG_AND_RUN_SCOPE_CHECKS",ConnectionStringPersisted=false};
        WriteNew("Q2-Catalog-Recheck.json",artifact);
        Console.WriteLine(JsonSerializer.Serialize(new{Status=artifact.Status,Missing=missing,RelatedNames=similar.Select(x=>x["name"]),RunDocumentCounts=docs,Current=e.Scope["CurrentQuantity"],Queues=e.Scope["MovementQueueRowsGlobal"],WorkerEnabled=e.Worker["JobEnabled"]}));
    }

    private static async Task<int> CaptureRecoveryPrestateAsync()
    {
        try
        {
            var e=await CaptureAsync();ValidateRecoveryPrestate(e);
            var receipt=await VerifyPosted(true,500007,T(e.Scope,"Q2HReceipt500007Number"));
            var currentReport=await CurrentReport((DateTime)e.Identity["BusinessDate"]!,D(e.Scope,"CurrentQuantity"),D(e.Scope,"BalanceReservedQuantity"));
            var parent=Directory.GetParent(_root)?.FullName??throw new InvalidDataException("Recovery root has no run-family parent.");
            var priorPath=Path.Combine(parent,"WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621","Q2-PreState.json");
            Need(File.Exists(priorPath),"Immutable failed Fresh-Q2 prestate is missing.");
            var priorBytes=await File.ReadAllBytesAsync(priorPath);using var prior=JsonDocument.Parse(priorBytes);
            Need(JsonString(prior.RootElement,"Status")=="PASS_PREWRITE_GATE"&&JsonString(prior.RootElement,"Batch4QualificationRunId")=="WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621","Failed Fresh-Q2 prestate lineage is invalid.");
            var priorScope=prior.RootElement.GetProperty("Evidence").GetProperty("Scope");var priorCurrent=JsonDecimal(priorScope,"CurrentQuantity");
            Need(priorCurrent==70000.001m&&D(e.Scope,"CurrentQuantity")-priorCurrent==.001m,"Retained Fresh-Q2 receipt delta does not reconcile to current inventory.");
            var priorBusinessDate=prior.RootElement.GetProperty("Evidence").GetProperty("Identity").GetProperty("BusinessDate").GetDateTime().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            Need(priorBusinessDate==T(e.Scope,"Q2HReceipt500007DocumentDate"),"Receipt 500007 date differs from the immutable Fresh-Q2 prestate date.");
            WriteNew("Batch4-Q2H-Recovery-Prestate.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-recovery-prestate/1",ObservedUtc=DateTimeOffset.UtcNow,RecoveryRunId=_runId,RecoveryOfQ2RunId="WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621",Status="PASS_RECOVERY_PRESTATE",DirectSqlUse="READ_ONLY_BOUNDED_PRESTATE_AND_LEDGER_ORACLE",Evidence=e,EvidenceClrTypes=CaptureClrTypes(e),PostedReceiptApplicationRead=receipt,CurrentReportApplicationRead=currentReport,PreviousFreshQ2PreState=new{Path=priorPath,Sha256=Convert.ToHexString(SHA256.HashData(priorBytes)).ToLowerInvariant(),CurrentQuantity=priorCurrent,BusinessDate=priorBusinessDate},ExpectedReceiptDelta=.001m,ObservedReceiptDelta=D(e.Scope,"CurrentQuantity")-priorCurrent,SnapshotQueueClassification="EXPECTED_PENDING_NOT_BLOCKING",RecoveryWorkerRoute="DIRECT_PROCEDURE_ROUTE_SOURCE_PROVEN",ApplicationWritesExecuted=0,CanonicalPerformanceStarted=false,ConnectionStringPersisted=false});
            Console.WriteLine(JsonSerializer.Serialize(new{Status="PASS_RECOVERY_PRESTATE",RecoveryRunId=_runId,Current=e.Scope["CurrentQuantity"],Queue11=e.Scope["Q2HMovementQueueId11Status"],Queue11FromDate=e.Scope["Q2HMovementQueueId11FromDate"],ReceiptDate=e.Scope["Q2HReceipt500007DocumentDate"],SnapshotVersion=e.Scope["Q2RSnapshotQueueId4RequestedVersion"]}));
            return 0;
        }
        catch(Exception ex)
        {
            try{WriteNew("Batch4-Q2H-Recovery-Prestate-Failure.json",new{ObservedUtc=DateTimeOffset.UtcNow,RecoveryRunId=_runId,Status="FAIL_BEFORE_RECOVERY_WORKER",Error=Safe(ex),ApplicationWritesExecuted=0,ConnectionStringPersisted=false});}catch{}
            Console.Error.WriteLine("Q2H recovery prestate failed: "+Safe(ex));return 1;
        }
    }

    private static async Task<int> RecoverFreshQ2Queue11Async()
    {
        var workerInvocationCount=0;var activeStage="RECOVERY_PRESTATE";Evidence? before=null;
        try
        {
            var prePath=Path.Combine(_root,"Batch4-Q2H-Recovery-Prestate.json");
            using var preDoc=JsonDocument.Parse(await File.ReadAllTextAsync(prePath));var preRoot=preDoc.RootElement;
            Need(JsonString(preRoot,"Status")=="PASS_RECOVERY_PRESTATE"&&JsonString(preRoot,"RecoveryRunId")==_runId,"Recovery prestate does not pass for this one-shot recovery run.");
            var expectedTypes=preRoot.TryGetProperty("EvidenceClrTypes",out var typeManifest)?typeManifest:(JsonElement?)null;
            Dictionary<string,object?>? workerPreInvocationScope=null;
            activeStage="WORKER_READ_ONLY_PREFLIGHT";
            var worker=await RunRecoveryInvocationWithClaimAsync(
                validateConnection:()=>{activeStage="CONNECTION_VALIDATION";return ValidateRecoveryConnectionAsync();},
                capture:async()=>
                {
                    activeStage="FULL_CAPTURE";before=await CaptureAsync();
                    activeStage="RECOVERY_PRESTATE_VALIDATION";ValidateRecoveryPrestate(before);
                },
                compareGuard:()=>
                {
                    activeStage="GUARD_COMPARISON";
                    var comparison=CompareRecoveryEvidence(before!,preRoot.GetProperty("Evidence"),expectedTypes);
                    if(!comparison.Matches)
                    {
                        var difference=comparison.Differences[0];
                        PersistRecoveryComparatorFailure(_root,_runId,preRoot.GetProperty("Evidence"),before!,difference,expectedTypes);
                        throw new InvalidDataException($"Recovery prestate mismatch at {difference.Domain}.{difference.FieldName}: {difference.Reason}.");
                    }
                    return Task.CompletedTask;
                },
                invokeWorker:beforeMutation=>DrainWorker(11,before!.Scope,L(before!.Worker,"ExecutionHistoryCount"),scopeAtInvocation=>{workerPreInvocationScope=scopeAtInvocation;beforeMutation();},requireDurableRecoveryClaim:true,onProcedureExecutionAttempt:()=>workerInvocationCount++),
                persistPreInvocation:()=>
                {
                    activeStage="PREINVOCATION_EVIDENCE";
                    Need(workerPreInvocationScope is not null,"Worker pre-invocation read-only scope was not captured.");
                    WriteNewDurable("Batch4-Q2H-Recovery-Execution-Recheck.json",new{ObservedUtc=DateTimeOffset.UtcNow,RecoveryRunId=_runId,Status="PASS_IMMEDIATELY_BEFORE_WORKER",PrestateSha256=HashFile(prePath),RecoveryClaimSha256=_validatedRecoveryClaimSha256,Evidence=before,WorkerPreInvocationScope=workerPreInvocationScope,ClaimConsumed=false,InvocationCount=0});
                    Event("Q2H_RECOVERY_WORKER_INTENT",new{QueueId=11,WorkerRoute="PROC_IS_WORKER_AGENT_IS_SCHEDULER",Procedure="dbo.sp_Inventory_Movement_Process_RebuildQueue",InvocationCount=1,ClaimConsumed=false,AutomaticRetryAllowed=false});
                },
                consumeClaim:()=>
                {
                    activeStage="CLAIM_CONSUMPTION";
                    var consumed=ConsumeRecoveryWorkerClaim(_root,_runId,CurrentHarnessAssemblySha256());
                    Need(consumed==_validatedRecoveryClaimSha256,"Consumed recovery claim differs from the claim validated at process entry.");
                    _recoveryClaimSha256=consumed;
                    activeStage="WORKER_PROCEDURE_INVOCATION";
                    return consumed;
                });
            var stableBefore=before??throw new InvalidDataException("Passing recovery capture was lost before the worker stage.");
            activeStage="POST_WORKER_CAPTURE";
            var after=await CaptureAsync();ValidateRecoveredBaseline(after);
            Need(D(after.Scope,"CurrentQuantity")==D(stableBefore.Scope,"CurrentQuantity")&&D(after.Scope,"BalanceReservedQuantity")==D(stableBefore.Scope,"BalanceReservedQuantity"),"Movement worker changed current or reserved inventory after receipt POST.");
            Need(D(after.Scope,"CurrentQuantity")-JsonDecimal(preRoot.GetProperty("PreviousFreshQ2PreState"),"CurrentQuantity")==.001m,"Receipt 500007 current-inventory delta differs from +0.001.");
            var receipt=await VerifyPosted(true,500007,T(after.Scope,"Q2HReceipt500007Number"));
            var businessDate=(DateTime)after.Identity["BusinessDate"]!;
            var currentReport=await CurrentReport(businessDate,D(after.Scope,"CurrentQuantity"),D(after.Scope,"BalanceReservedQuantity"));
            var receiptDate=DateTime.ParseExact(T(after.Scope,"Q2HReceipt500007DocumentDate"),"yyyy-MM-dd",CultureInfo.InvariantCulture);
            var historicalReport=await HistoricalReport(receiptDate,D(after.Scope,"Q2HReceiptPostedReceiptLedger"),D(after.Scope,"Q2HReceiptPostedIssueLedger"),D(after.Scope,"Q2HReceiptBalanceClosing"));
            var workerDoc=new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-recovery-worker-lifecycle/1",ObservedUtc=DateTimeOffset.UtcNow,RecoveryRunId=_runId,Status="PASS",WorkerRoute="PROC_IS_WORKER_AGENT_IS_SCHEDULER",Procedure="dbo.sp_Inventory_Movement_Process_RebuildQueue",InvocationCount=1,AgentStarted=false,QueueId=11,Worker=worker,CurrentBefore=D(before.Scope,"CurrentQuantity"),CurrentAfter=D(after.Scope,"CurrentQuantity"),CurrentQuantityUnchanged=D(before.Scope,"CurrentQuantity")==D(after.Scope,"CurrentQuantity"),RetryCount=after.Scope["Q2HMovementQueueId11RetryCount"],ClaimedVersion=after.Scope["Q2HMovementQueueId11ClaimedVersion"],RequestedVersion=after.Scope["Q2HMovementQueueId11RequestedVersion"],ProcessedAt=after.Scope["Q2HMovementQueueId11ProcessedAt"],DeadLetters=after.Scope["MovementDeadLettersGlobal"],SqlAgentState="STOPPED",DedicatedJobEnabled=after.Worker["JobEnabled"],DedicatedScheduleEnabled=after.Worker["ScheduleEnabled"],ExistingGiaiDoan2JobUnchanged=true,SnapshotQueueClassification="EXPECTED_PENDING_NOT_BLOCKING",SnapshotState=after.Scope,AutomaticRetryAllowed=false};
            WriteNew("Batch4-Q2H-Recovery-Worker-Lifecycle.json",workerDoc);
            WriteNew("Batch4-Q2H-Recovery-Receipt-Reconciliation.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-recovery-receipt-reconciliation/1",ObservedUtc=DateTimeOffset.UtcNow,RecoveryRunId=_runId,Status="PASS",Receipt=receipt,ReceiptDocumentDate=receiptDate,ExpectedReceiptQty=.001m,ObservedReceiptQty=after.Scope["Q2HReceipt500007DetailQty"],ExpectedReceiptDelta=.001m,ObservedReceiptDelta=D(after.Scope,"CurrentQuantity")-JsonDecimal(preRoot.GetProperty("PreviousFreshQ2PreState"),"CurrentQuantity"),RecoveryWorkerCurrentDelta=D(after.Scope,"CurrentQuantity")-D(before.Scope,"CurrentQuantity"),PostedReceiptLedgerForDate=after.Scope["Q2HReceiptPostedReceiptLedger"],PostedIssueLedgerForDate=after.Scope["Q2HReceiptPostedIssueLedger"],MovementDailyReceiptForDate=after.Scope["Q2HReceiptMovementReceipt"],MovementDailyIssueForDate=after.Scope["Q2HReceiptMovementIssue"],BalanceDailyOpening=after.Scope["Q2HReceiptBalanceOpening"],BalanceDailyReceived=after.Scope["Q2HReceiptBalanceReceived"],BalanceDailyIssued=after.Scope["Q2HReceiptBalanceIssued"],BalanceDailyClosing=after.Scope["Q2HReceiptBalanceClosing"],CurrentQuantity=after.Scope["CurrentQuantity"],ReservedQuantity=after.Scope["BalanceReservedQuantity"],CurrentReportApplicationRead=currentReport,HistoricalReportApplicationRead=historicalReport,MovementQueue11= new{Rows=after.Scope["Q2HMovementQueueId11Rows"],Status=after.Scope["Q2HMovementQueueId11Status"],RequestedVersion=after.Scope["Q2HMovementQueueId11RequestedVersion"],ClaimedVersion=after.Scope["Q2HMovementQueueId11ClaimedVersion"],RetryCount=after.Scope["Q2HMovementQueueId11RetryCount"],ProcessedAt=after.Scope["Q2HMovementQueueId11ProcessedAt"]},SnapshotQueue4=new{QueueId=4,RequestedVersion=after.Scope["Q2RSnapshotQueueId4RequestedVersion"],Status=after.Scope["Q2RSnapshotQueueId4Status"],RequestType=after.Scope["Q2RSnapshotQueueId4RequestType"],LifecycleStatus=after.Scope["Q2RSnapshotQueueId4LifecycleStatus"],Classification="EXPECTED_PENDING_NOT_BLOCKING"},DeadLetters=after.Scope["MovementDeadLettersGlobal"],DraftResidue=new{ReceiptHeaders=after.Scope["Q2HFreshReceiptDraftHeaders"],ReceiptDetails=after.Scope["Q2HFreshReceiptDraftDetails"],IssueHeaders=after.Scope["Q2HFreshIssueHeaders"],IssueDetails=after.Scope["Q2HFreshIssueDetails"]},BoundedPrePost="PASS",WholeDatabaseNoUnexpectedMutation="NOT_VERIFIED",LeastPrivilegeBehavior="NOT_VERIFIED"});
            WriteNew("Batch4-Q2H-Recovery-FinalState.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2h-recovery-final-state/1",ObservedUtc=DateTimeOffset.UtcNow,RecoveryRunId=_runId,RecoveryOfQ2RunId="WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621",Status="PASS",BATCH4_Q2H_PARTIAL_STATE_RECONCILED="YES",Prestate=preRoot.GetProperty("Evidence"),FinalState=after,WorkerLifecycle=workerDoc,ReceiptReconciliation="Batch4-Q2H-Recovery-Receipt-Reconciliation.json",SnapshotQueueClassification="EXPECTED_PENDING_NOT_BLOCKING",UnexplainedBoundedMutations=0,DeadLetters=0,CurrentQuantity=after.Scope["CurrentQuantity"],ReservedQuantity=after.Scope["BalanceReservedQuantity"],AvailableQuantity=D(after.Scope,"CurrentQuantity")-D(after.Scope,"BalanceReservedQuantity"),CanonicalPerformanceStarted=false});
            Event("Q2H_RECOVERY_PASS",new{QueueId=11,InvocationCount=1,CurrentQuantity=after.Scope["CurrentQuantity"],SnapshotQueueClassification="EXPECTED_PENDING_NOT_BLOCKING"});
            WriteRecoveryWorkerResult(0,"PASS_RECOVERED_QUEUE11",workerInvocationCount);
            Console.WriteLine(JsonSerializer.Serialize(new{Status="PASS_RECOVERED_QUEUE11",RecoveryRunId=_runId,QueueId=11,Current=after.Scope["CurrentQuantity"],SnapshotVersion=after.Scope["Q2RSnapshotQueueId4RequestedVersion"],CanonicalPerformanceStarted=false}));
            return 0;
        }
        catch(Exception ex)
        {
            var markerPath=Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json");
            var claimConsumed=_recoveryClaimSha256 is not null||File.Exists(markerPath);
            if(!claimConsumed)
            {
                var status=activeStage switch
                {
                    "CONNECTION_VALIDATION"=>"CONNECTION_VALIDATION_FAILED_NOT_CONSUMED",
                    "FULL_CAPTURE"=>"CAPTURE_FAILED_NOT_CONSUMED",
                    "RECOVERY_PRESTATE_VALIDATION" or "GUARD_COMPARISON"=>"GUARD_FAILED_NOT_CONSUMED",
                    "WORKER_READ_ONLY_PREFLIGHT"=>"WORKER_PREFLIGHT_FAILED_NOT_CONSUMED",
                    "CLAIM_CONSUMPTION"=>"CLAIM_CONSUMPTION_FAILED_NOT_CONSUMED",
                    _=>"PREFLIGHT_FAILED_NOT_CONSUMED"
                };
                try{Event("Q2H_RECOVERY_PREFLIGHT_FAILED_NOT_CONSUMED",new{FailedStage=activeStage,Error=Safe(ex),AutomaticRetryAllowed=false});}catch{}
                try{WriteRecoveryPreflightFailure(status,activeStage,ex);}catch{}
                Console.Error.WriteLine("Q2H recovery preflight stopped before claim consumption: "+Safe(ex));return 1;
            }
            try{Event("Q2H_RECOVERY_STOP_FIRST_FAILURE",new{Error=Safe(ex),InvocationClaimConsumed=_recoveryClaimSha256 is not null,AutomaticRetryAllowed=false});}catch{}
            try{var partial=await CaptureAsync();WriteNew("Batch4-Q2H-Recovery-Failure-State.json",new{ObservedUtc=DateTimeOffset.UtcNow,RecoveryRunId=_runId,Status="PARTIAL_OR_STOPPED",Error=Safe(ex),Identity=partial.Identity,Scope=partial.Scope,Worker=partial.Worker,ExistingJob=partial.ExistingJob,EvidenceClrTypes=CaptureClrTypes(partial),InvocationClaimSha256=_recoveryClaimSha256,AutomaticRetryAllowed=false,CanonicalPerformanceStarted=false});}catch{}
            try{WriteRecoveryWorkerResult(1,"Q2H_RECOVERY_STOPPED_FIRST_FAILURE",workerInvocationCount);}catch{}
            Console.Error.WriteLine("Q2H recovery stopped after first failure: "+Safe(ex));return 1;
        }
    }

    private static async Task ValidateRecoveryConnectionAsync()
    {
        var builder=new SqlConnectionStringBuilder(_cs);
        Need(builder.IntegratedSecurity&&string.IsNullOrWhiteSpace(builder.UserID)&&string.IsNullOrWhiteSpace(builder.Password),"Recovery connection no longer matches Integrated Security contract.");
        await using var connection=new SqlConnection(_cs);await connection.OpenAsync();
        Need(connection.Database=="TKS_Thuc_Tap_V11_Perf_10000000","Recovery connection opened the wrong target database.");
    }

    private static void Validate(Evidence e)
    {
        ValidateTargetAndRuntime(e);
        Need(_stableBaseline is not null,"Recovered Q2H stable baseline is missing.");
        var expected=_stableBaseline!.RootElement.GetProperty("Evidence").GetProperty("Scope");
        Need(ScopeMatchesBaseline(e.Scope,expected),"Current bounded state differs from the recovered stable baseline.");
    }

    private static void ValidateTargetAndRuntime(Evidence e)
    {
        var i=e.Identity;var j=e.Worker;var old=e.ExistingJob;var a=e.AppPath;
        Need(T(i,"ServerName")==@"DESKTOP-NHQ7QPL\MSSQLSERVER19" && T(i,"DatabaseName")=="TKS_Thuc_Tap_V11_Perf_10000000" && I(i,"DatabaseId")==5 && T(i,"DatabaseStatus")=="ONLINE","Target DB identity/state mismatch.");
        Need(T(i,"SqlLogin")==@"DESKTOP-NHQ7QPL\Surface" && T(i,"OriginalLogin")==@"MicrosoftAccount\kiriza165@gmail.com" && T(i,"DatabaseUser")=="dbo" && I(i,"IsSysadmin")==1 && I(i,"IsDbOwner")==1,"SQL connection identity mismatch.");
        Need(I(i,"AccountCount")==1 && L(i,"AccountId")==1 && I(i,"AccountDeleted")==0 && I(i,"MappingCount")==100 && I(i,"Warehouse4Mappings")==1,"PERF_USER app account or authorization mismatch.");
        Need(I(i,"SqlPerfLoginCount")==0 && I(i,"DbPerfPrincipalCount")==0,"Unexpected PERF_USER SQL principal appeared.");
        Need(L(i,"WarehouseExists")==1 && L(i,"ProductExists")==1 && L(i,"SupplierExists")==1,"Frozen master data missing.");
        Need(T(j,"JobName")=="WHB22 Batch4 Perf_10000000 Inventory Movement Rebuild Worker" && I(j,"JobEnabled")==0 && T(j,"StepDatabase")=="TKS_Thuc_Tap_V11_Perf_10000000" && T(j,"StepCommand").Contains("sp_Inventory_Movement_Process_RebuildQueue") && I(j,"ScheduleEnabled")==0 && L(j,"ExecutionHistoryCount")==0 && I(j,"TargetMovementJobsCount")==1,"Dedicated worker route not isolated and disabled.");
        Need(T(old,"JobName")=="TKS Warehouse - Inventory Movement Aggregate Rebuild" && I(old,"JobEnabled")==1 && T(old,"StepDatabase")=="TKS_Thuc_Tap_V11_GiaiDoan2" && I(old,"ScheduleEnabled")==1,"Existing GiaiDoan2 job differs from frozen state.");
        Need(T(a,"Status")=="PASS" && I(a,"WarehouseMappingCount")==100 && B(a,"Warehouse4Allowed") && I(a,"DocumentTotalCount")>0 && I(a,"UnauthorizedRows")==0,"Read-only application path probe failed.");
        Need(i["BusinessDate"] is DateTime dt && dt.Date==DateTime.Today,"SQL business date differs from machine date.");
    }

    private static void ValidateRecoveredBaseline(Evidence e)
    {
        ValidateTargetAndRuntime(e);var s=e.Scope;
        Need(D(s,"CurrentQuantity")==70000.002m&&D(s,"BalanceReservedQuantity")==0m&&L(s,"ReservationRowsScope")==0&&D(s,"ReservationQuantityScope")==0m,"Recovered baseline inventory/reservation differs from the required 70000.002/0 state.");
        Need(L(s,"Q2HReceipt500007Rows")==1&&L(s,"Q2HReceipt500007IsPosted")==1&&T(s,"Q2HReceipt500007Number").StartsWith("WHB22B4-WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621-",StringComparison.Ordinal)&&T(s,"Q2HReceipt500007Number").EndsWith("-RCPT-POST-01",StringComparison.Ordinal)&&L(s,"Q2HReceipt500007DetailRows")==1&&L(s,"Q2HReceipt500007ProductDetailRows")==1&&D(s,"Q2HReceipt500007DetailQty")==.001m,"Retained posted receipt 500007 document/detail contract mismatch.");
        Need(L(s,"Q2HFreshReceiptHeaders")==1&&L(s,"Q2HFreshReceiptDetails")==1&&L(s,"Q2HFreshReceiptDraftHeaders")==0&&L(s,"Q2HFreshReceiptDraftDetails")==0&&L(s,"Q2HFreshIssueHeaders")==0&&L(s,"Q2HFreshIssueDetails")==0,"Unexpected draft or unknown document residue remains from failed Fresh Q2.");
        ValidateRecoveryQueueDateContract(s);
        Need(L(s,"MovementDailyRows")==L(s,"ValidMovementDailyRows")&&L(s,"BalanceDailyRows")==L(s,"ValidBalanceDailyRows")&&L(s,"ReportScopeRows")==1,"Recovered movement/balance daily aggregates are not reconciled.");
        Need(D(s,"Q2HReceiptMovementReceipt")==D(s,"Q2HReceiptPostedReceiptLedger")&&D(s,"Q2HReceiptMovementIssue")==D(s,"Q2HReceiptPostedIssueLedger")&&D(s,"Q2HReceiptBalanceReceived")==D(s,"Q2HReceiptPostedReceiptLedger")&&D(s,"Q2HReceiptBalanceIssued")==D(s,"Q2HReceiptPostedIssueLedger")&&D(s,"Q2HReceiptBalanceOpening")+D(s,"Q2HReceiptBalanceReceived")-D(s,"Q2HReceiptBalanceIssued")==D(s,"Q2HReceiptBalanceClosing")&&D(s,"Q2HReceiptBalanceClosing")==D(s,"CurrentQuantity"),"Receipt-date posted ledger, movement daily, balance daily, and current quantity do not reconcile.");
        Need(L(s,"MovementQueueRowsGlobal")==2&&L(s,"MovementQueueRowsScope")==2&&L(s,"MovementQueueActiveGlobal")==0&&L(s,"MovementQueueActiveScope")==0&&L(s,"MovementQueueMaxId")==11&&L(s,"MovementDeadLettersGlobal")==0&&L(s,"MovementDeadLettersScope")==0,"Recovered movement queue state is not drained.");
        Need(L(s,"Q2RMovementQueueId10Rows")==1&&T(s,"Q2RMovementQueueId10Status")=="COMPLETED"&&L(s,"Q2RMovementQueueId10RetryCount")==0&&L(s,"Q2RMovementQueueId10RequestedVersion")==1&&L(s,"Q2RMovementQueueId10ClaimedVersion")==1&&T(s,"Q2RMovementQueueId10FromDate")=="2026-10-04"&&T(s,"Q2RMovementQueueId10ToDate")=="2026-10-04"&&s["Q2RMovementQueueId10ProcessedAt"] is DateTime,"Reconciled movement queue ID 10 contract mismatch.");
        Need(L(s,"Q2HMovementQueueId11Rows")==1&&T(s,"Q2HMovementQueueId11Status")=="COMPLETED"&&L(s,"Q2HMovementQueueId11RetryCount")==0&&L(s,"Q2HMovementQueueId11RequestedVersion")==1&&L(s,"Q2HMovementQueueId11ClaimedVersion")==1&&s["Q2HMovementQueueId11ProcessedAt"] is DateTime,"Recovered movement queue ID 11 contract mismatch.");
        Need(L(s,"SnapshotQueueRowsGlobal")==1&&L(s,"SnapshotQueueActiveGlobal")==1&&L(s,"SnapshotDeadLettersGlobal")==0&&L(s,"SnapshotDeadLettersScope")==0&&L(s,"SnapshotRowsScope")==0,"Recovered pending snapshot queue classification changed.");
        Need(L(s,"Q2RSnapshotQueueId4Rows")==1&&T(s,"Q2RSnapshotQueueId4Status")=="WAITING"&&L(s,"Q2RSnapshotQueueId4AttemptCount")==0&&T(s,"Q2RSnapshotQueueId4RequestType")=="INITIALIZE"&&T(s,"Q2RSnapshotQueueId4LifecycleStatus")=="INITIALIZE_REQUIRED"&&L(s,"Q2RSnapshotQueueId4WarehouseId")==Warehouse&&L(s,"Q2RSnapshotQueueId4ProductId")==Product&&T(s,"Q2RSnapshotQueueId4FromDate")=="2026-10-04"&&L(s,"Q2RSnapshotQueueId4RequestedVersion")==2&&s["Q2RSnapshotQueueId4ClaimedVersion"] is null&&s["Q2RSnapshotQueueId4ClaimedBy"] is null&&s["Q2RSnapshotQueueId4ClaimedAt"] is null&&s["Q2RSnapshotQueueId4LeaseUntil"] is null&&L(s,"CompletedBootstrapAuditEligibleForScope")==0,"Expected pending snapshot queue ID 4 version 2 contract mismatch.");
    }

    private static void ValidateRecoveryPrestate(Evidence e)
    {
        ValidateTargetAndRuntime(e);var s=e.Scope;
        Need(D(s,"CurrentQuantity")==70000.002m&&D(s,"BalanceReservedQuantity")==0m&&L(s,"ReservationRowsScope")==0&&D(s,"ReservationQuantityScope")==0m,"Fresh-Q2 recovery current/reserved prestate mismatch.");
        Need(L(s,"Q2HReceipt500007Rows")==1&&L(s,"Q2HReceipt500007IsPosted")==1&&L(s,"Q2HReceipt500007DetailRows")==1&&L(s,"Q2HReceipt500007ProductDetailRows")==1&&D(s,"Q2HReceipt500007DetailQty")==.001m,"Retained receipt 500007 document/detail is not exactly as expected.");
        Need(T(s,"Q2HReceipt500007Number").StartsWith("WHB22B4-WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621-",StringComparison.Ordinal)&&T(s,"Q2HReceipt500007Number").EndsWith("-RCPT-POST-01",StringComparison.Ordinal),"Receipt 500007 does not have the preserved failed Fresh-Q2 identity.");
        Need(L(s,"Q2HFreshReceiptHeaders")==1&&L(s,"Q2HFreshReceiptDetails")==1&&L(s,"Q2HFreshReceiptDraftHeaders")==0&&L(s,"Q2HFreshReceiptDraftDetails")==0&&L(s,"Q2HFreshIssueHeaders")==0&&L(s,"Q2HFreshIssueDetails")==0,"Fresh-Q2 draft/issue residue or unknown document detected.");
        ValidateRecoveryQueueDateContract(s);
        Need(L(s,"MovementDailyRows")==L(s,"ValidMovementDailyRows")&&L(s,"BalanceDailyRows")==L(s,"ValidBalanceDailyRows")&&L(s,"ReportScopeRows")==1,"Pre-recovery daily/report scope contains invalid rows.");
        Need(D(s,"TodayPostedReceiptLedger")-D(s,"TodayMovementReceipt")==.001m&&D(s,"TodayPostedIssueLedger")==D(s,"TodayMovementIssue"),"Pre-recovery movement daily gap does not exactly match the one pending receipt.");
        Need(L(s,"MovementQueueRowsGlobal")==2&&L(s,"MovementQueueRowsScope")==2&&L(s,"MovementQueueActiveGlobal")==1&&L(s,"MovementQueueActiveScope")==1&&L(s,"MovementQueueMaxId")==11&&L(s,"MovementDeadLettersGlobal")==0&&L(s,"MovementDeadLettersScope")==0,"Pre-recovery movement queue/dead-letter state mismatch.");
        Need(L(s,"Q2RMovementQueueId10Rows")==1&&T(s,"Q2RMovementQueueId10Status")=="COMPLETED"&&L(s,"Q2RMovementQueueId10RetryCount")==0&&L(s,"Q2RMovementQueueId10RequestedVersion")==1&&L(s,"Q2RMovementQueueId10ClaimedVersion")==1&&T(s,"Q2RMovementQueueId10FromDate")=="2026-10-04"&&T(s,"Q2RMovementQueueId10ToDate")=="2026-10-04"&&s["Q2RMovementQueueId10ProcessedAt"] is DateTime,"Previously reconciled queue #10 changed.");
        Need(L(s,"Q2HMovementQueueId11Rows")==1&&T(s,"Q2HMovementQueueId11Status")=="WAITING"&&L(s,"Q2HMovementQueueId11RetryCount")==0&&L(s,"Q2HMovementQueueId11RequestedVersion")==1&&L(s,"Q2HMovementQueueId11ClaimedVersion")==-1&&s["Q2HMovementQueueId11LastAttemptAt"] is null&&s["Q2HMovementQueueId11NextRetryAt"] is null&&s["Q2HMovementQueueId11LastError"] is null&&s["Q2HMovementQueueId11ErrorMessage"] is null&&s["Q2HMovementQueueId11ProcessedAt"] is null&&s["Q2HMovementQueueId11CreatedAt"] is DateTime,"Queue #11 is not the exact unclaimed WAITING recovery target.");
        Need(L(s,"SnapshotQueueRowsGlobal")==1&&L(s,"SnapshotQueueActiveGlobal")==1&&L(s,"SnapshotDeadLettersGlobal")==0&&L(s,"SnapshotDeadLettersScope")==0&&L(s,"SnapshotRowsScope")==0&&L(s,"Q2RSnapshotQueueId4Rows")==1&&T(s,"Q2RSnapshotQueueId4Status")=="WAITING"&&L(s,"Q2RSnapshotQueueId4AttemptCount")==0&&T(s,"Q2RSnapshotQueueId4RequestType")=="INITIALIZE"&&T(s,"Q2RSnapshotQueueId4LifecycleStatus")=="INITIALIZE_REQUIRED"&&L(s,"Q2RSnapshotQueueId4RequestedVersion")==2&&s["Q2RSnapshotQueueId4ClaimedVersion"] is null&&s["Q2RSnapshotQueueId4ClaimedBy"] is null&&s["Q2RSnapshotQueueId4ClaimedAt"] is null&&s["Q2RSnapshotQueueId4LeaseUntil"] is null&&L(s,"CompletedBootstrapAuditEligibleForScope")==0,"Pre-recovery snapshot #4 is not the expected nonblocking INITIALIZE_REQUIRED state.");
    }

    internal static void ValidateRecoveryQueueDateContract(Dictionary<string,object?> scope)
    {
        var receiptDate=T(scope,"Q2HReceipt500007DocumentDate");
        Need(DateTime.TryParseExact(receiptDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"Retained receipt 500007 document date is missing or invalid.");
        Need(T(scope,"Q2HMovementQueueId11FromDate")==receiptDate&&T(scope,"Q2HMovementQueueId11ToDate")==receiptDate,"Movement queue #11 date must match the retained receipt document date.");
    }

    private static void LoadStableBaseline()
    {
        var path=Path.Combine(_root,"Batch4-Q2H-Stable-Baseline.json");Need(File.Exists(path),"Recovered Q2H baseline file is required before Q2 preflight/execute.");
        var bytes=File.ReadAllBytes(path);_stableBaseline=JsonDocument.Parse(bytes);_stableBaselineSha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        ValidateStableBaselineDocument(_stableBaseline.RootElement);
    }

    private static void ValidateStableBaselineDocument(JsonElement root)
    {
        Need(JsonString(root,"Status")=="PASS"&&JsonString(root,"BATCH4_Q2H_PARTIAL_STATE_RECONCILED")=="YES"&&JsonString(root,"SnapshotQueueClassification")=="EXPECTED_PENDING_NOT_BLOCKING","Q2H stable baseline status is not PASS.");
        Need(JsonString(root,"RecoveryOfQ2RunId")=="WHB22-B4-Q2-FRESH-20261004T1724262409113Z-894DB621"&&JsonString(root,"RecoveryRunId").StartsWith("WHB22-B4-Q2H-RECOVERY-",StringComparison.Ordinal),"Q2H baseline lineage does not reference the consumed Fresh Q2 recovery.");
        var s=root.GetProperty("Evidence").GetProperty("Scope");
        Need(JsonDecimal(s,"CurrentQuantity")==70000.002m&&JsonDecimal(s,"BalanceReservedQuantity")==0m&&JsonLong(s,"ReservationRowsScope")==0,"Q2H baseline does not hold the exact recovered inventory state.");
        Need(JsonLong(s,"Q2HReceipt500007Rows")==1&&JsonLong(s,"Q2HReceipt500007IsPosted")==1&&JsonLong(s,"Q2HReceipt500007DetailRows")==1&&JsonLong(s,"Q2HReceipt500007ProductDetailRows")==1&&JsonDecimal(s,"Q2HReceipt500007DetailQty")==.001m,"Q2H baseline does not preserve the exact posted receipt 500007.");
        var receiptDate=JsonString(s,"Q2HReceipt500007DocumentDate");
        Need(receiptDate.Length==10&&JsonString(s,"Q2HMovementQueueId11FromDate")==receiptDate&&JsonString(s,"Q2HMovementQueueId11ToDate")==receiptDate,"Q2H baseline queue #11 date does not match receipt 500007.");
        Need(JsonLong(s,"Q2HFreshReceiptHeaders")==1&&JsonLong(s,"Q2HFreshReceiptDetails")==1&&JsonLong(s,"Q2HFreshReceiptDraftHeaders")==0&&JsonLong(s,"Q2HFreshReceiptDraftDetails")==0&&JsonLong(s,"Q2HFreshIssueHeaders")==0&&JsonLong(s,"Q2HFreshIssueDetails")==0,"Q2H baseline contains unexpected Fresh Q2 document residue.");
        Need(JsonDecimal(s,"Q2HReceiptMovementReceipt")==JsonDecimal(s,"Q2HReceiptPostedReceiptLedger")&&JsonDecimal(s,"Q2HReceiptMovementIssue")==JsonDecimal(s,"Q2HReceiptPostedIssueLedger")&&JsonDecimal(s,"Q2HReceiptBalanceReceived")==JsonDecimal(s,"Q2HReceiptPostedReceiptLedger")&&JsonDecimal(s,"Q2HReceiptBalanceIssued")==JsonDecimal(s,"Q2HReceiptPostedIssueLedger")&&JsonDecimal(s,"Q2HReceiptBalanceOpening")+JsonDecimal(s,"Q2HReceiptBalanceReceived")-JsonDecimal(s,"Q2HReceiptBalanceIssued")==JsonDecimal(s,"Q2HReceiptBalanceClosing"),"Q2H baseline ledger and daily aggregates do not reconcile.");
        Need(JsonLong(s,"MovementQueueMaxId")==11&&JsonLong(s,"MovementQueueRowsGlobal")==2&&JsonLong(s,"MovementQueueActiveGlobal")==0&&JsonString(s,"Q2HMovementQueueId11Status")=="COMPLETED"&&JsonLong(s,"Q2HMovementQueueId11ClaimedVersion")==1,"Q2H baseline does not contain completed movement queue #11.");
        Need(JsonLong(s,"Q2RSnapshotQueueId4RequestedVersion")==2&&JsonString(s,"Q2RSnapshotQueueId4Status")=="WAITING"&&JsonString(s,"Q2RSnapshotQueueId4RequestType")=="INITIALIZE"&&JsonString(s,"Q2RSnapshotQueueId4LifecycleStatus")=="INITIALIZE_REQUIRED"&&JsonLong(s,"SnapshotRowsScope")==0&&JsonLong(s,"CompletedBootstrapAuditEligibleForScope")==0,"Q2H baseline snapshot state is not source-valid pending initialization.");
    }

    internal static ComparatorResult CompareBaselineDetailed(string domain,Dictionary<string,object?> actual,JsonElement expected,JsonElement? expectedClrTypes=null)
    {
        var differences=new List<ComparatorFieldDiff>();
        if(expected.ValueKind!=JsonValueKind.Object)
        {
            differences.Add(CreateComparatorDiff(domain,"$object",null,expected,expectedClrTypes,"Expected JsonObject","Expected evidence domain is not a JSON object."));
            return new(false,differences);
        }
        var expectedCount=expected.EnumerateObject().Count();
        if(expectedCount!=actual.Count)
        {
            foreach(var item in actual)
                if(!expected.TryGetProperty(item.Key,out _))differences.Add(CreateComparatorDiff(domain,item.Key,item.Value,default,expectedClrTypes,"Expected object property by exact name","Observed field is absent from expected JSON."));
            foreach(var item in expected.EnumerateObject())
                if(!actual.ContainsKey(item.Name))differences.Add(CreateComparatorDiff(domain,item.Name,null,item.Value,expectedClrTypes,"Expected object property by exact name","Expected field is absent from observed capture."));
            if(differences.Count==0)differences.Add(CreateComparatorDiff(domain,"$field-count",actual.Count,expected,expectedClrTypes,"Expected property count == observed dictionary count","Compared field counts differ."));
            return new(false,differences);
        }
        foreach(var item in actual)
        {
            if(!expected.TryGetProperty(item.Key,out var expectedValue))
            {
                differences.Add(CreateComparatorDiff(domain,item.Key,item.Value,default,expectedClrTypes,"Exact property name lookup","Observed field is absent from expected JSON."));
                continue;
            }
            if(!JsonValueEquals(item.Value,expectedValue))
                differences.Add(CreateComparatorDiff(domain,item.Key,item.Value,expectedValue,expectedClrTypes,ComparisonRule(item.Value),ComparisonReason(item.Value,expectedValue)));
        }
        return new(differences.Count==0,differences);
    }

    private static bool ScopeMatchesBaseline(Dictionary<string,object?> actual,JsonElement expected)
        =>CompareBaselineDetailed("Scope",actual,expected).Matches;

    private static ComparatorResult CompareRecoveryEvidence(Evidence actual,JsonElement expected,JsonElement? expectedClrTypes)
    {
        foreach(var domain in new[]{"Scope","Identity","Worker","ExistingJob"})
        {
            var observed=domain switch{"Scope"=>actual.Scope,"Identity"=>actual.Identity,"Worker"=>actual.Worker,_=>actual.ExistingJob};
            var typeDomain=expectedClrTypes is {ValueKind:JsonValueKind.Object} types&&types.TryGetProperty(domain,out var foundTypes)?foundTypes:(JsonElement?)null;
            var result=CompareBaselineDetailed(domain,observed,expected.GetProperty(domain),typeDomain);
            if(!result.Matches)return result;
        }
        return new(true,Array.Empty<ComparatorFieldDiff>());
    }

    private static ComparatorFieldDiff CreateComparatorDiff(string domain,string field,object? actual,JsonElement expected,JsonElement? expectedClrTypes,string rule,string reason)
    {
        var expectedClr="NOT_CAPTURED";
        if(expectedClrTypes is {ValueKind:JsonValueKind.Object} types&&types.TryGetProperty(field,out var typeElement)&&typeElement.ValueKind==JsonValueKind.String)
            expectedClr=typeElement.GetString()??"NOT_CAPTURED";
        var expectedRaw=expected.ValueKind==JsonValueKind.Undefined?"<missing>":expected.GetRawText();
        var expectedNormalized=expected.ValueKind==JsonValueKind.Undefined?"<missing>":NormalizeExpected(actual,expected);
        return new(domain,field,expectedRaw,RawValue(actual),expectedClr,actual?.GetType().FullName??"null",expectedNormalized,NormalizeObserved(actual),rule,reason);
    }

    internal static void PersistRecoveryComparatorFailure(string root,string runId,JsonElement expectedEvidence,Evidence actual,ComparatorFieldDiff diff,JsonElement? expectedClrTypes=null)
    {
        var path=Path.Combine(root,"Batch4-Q2H-Recovery-Comparator-Failure.json");
        Directory.CreateDirectory(root);
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        JsonSerializer.Serialize(stream,new
        {
            SchemaVersion="warehouse-benchmark-v22-batch4-q2h-recovery-comparator-failure/1",ObservedUtc=DateTimeOffset.UtcNow,
            RecoveryRunId=runId,Status="FAIL_CLOSED_DETAILED_COMPARISON",FailingComparison=diff,
            ExpectedScope=expectedEvidence.GetProperty("Scope"),ExpectedIdentity=expectedEvidence.GetProperty("Identity"),
            ExpectedWorker=expectedEvidence.GetProperty("Worker"),ExpectedExistingJob=expectedEvidence.GetProperty("ExistingJob"),ExpectedClrTypeManifest=expectedClrTypes,
            ScopeAtComparison=actual.Scope,IdentityAtComparison=actual.Identity,WorkerAtComparison=actual.Worker,ExistingJobAtComparison=actual.ExistingJob,
            ScopeAtComparisonClrTypes=ClrTypes(actual.Scope),IdentityAtComparisonClrTypes=ClrTypes(actual.Identity),
            WorkerAtComparisonClrTypes=ClrTypes(actual.Worker),ExistingJobAtComparisonClrTypes=ClrTypes(actual.ExistingJob),
            ComparisonSemanticsChanged=false,GuardDecision="FAIL_CLOSED",WorkerProcedureInvocationCount=0
        },JsonOptions);
        stream.Write(Encoding.UTF8.GetBytes(Environment.NewLine));stream.Flush(true);
    }

    private static object CaptureClrTypes(Evidence e)=>new{Identity=ClrTypes(e.Identity),Scope=ClrTypes(e.Scope),Worker=ClrTypes(e.Worker),ExistingJob=ClrTypes(e.ExistingJob)};
    private static Dictionary<string,string> ClrTypes(Dictionary<string,object?> values)
        =>values.ToDictionary(x=>x.Key,x=>x.Value?.GetType().FullName??"null",StringComparer.Ordinal);

    private static string ComparisonRule(object? actual)=>actual switch
    {
        null=>"CLR null requires JsonValueKind.Null",
        string=>"String requires JsonValueKind.String and ordinal string equality",
        short=>"Int16 requires JsonValueKind.Number, TryGetInt16, and Int16 equality",
        byte=>"Byte requires JsonValueKind.Number, TryGetByte, and Byte equality",
        int=>"Int32 requires JsonValueKind.Number, TryGetInt32, and Int32 equality",
        long=>"Int64 requires JsonValueKind.Number, TryGetInt64, and Int64 equality",
        decimal=>"Decimal requires JsonValueKind.Number, TryGetDecimal, and Decimal equality",
        DateTime=>"DateTime requires JsonValueKind.String, TryGetDateTime, and DateTime equality",
        bool=>"Boolean requires JsonValueKind.True/False and Boolean equality",
        _=>"Unsupported CLR type fails closed"
    };

    private static string ComparisonReason(object? actual,JsonElement expected)=>actual switch
    {
        null=>"Observed CLR null does not match JSON null.",
        string when expected.ValueKind!=JsonValueKind.String=>"Observed string does not match JSON value kind.",
        string=>"Observed and expected strings differ ordinally.",
        short when expected.ValueKind!=JsonValueKind.Number=>"Observed Int16 does not match JSON value kind.",
        short=>"Expected JSON number is not an equal Int16 value.",
        byte when expected.ValueKind!=JsonValueKind.Number=>"Observed Byte does not match JSON value kind.",
        byte=>"Expected JSON number is not an equal Byte value.",
        int when expected.ValueKind!=JsonValueKind.Number=>"Observed Int32 does not match JSON value kind.",
        int=>"Expected JSON number is not an equal Int32 value.",
        long when expected.ValueKind!=JsonValueKind.Number=>"Observed Int64 does not match JSON value kind.",
        long=>"Expected JSON number is not an equal Int64 value.",
        decimal when expected.ValueKind!=JsonValueKind.Number=>"Observed Decimal does not match JSON value kind.",
        decimal=>"Expected JSON number is not an equal Decimal value.",
        DateTime when expected.ValueKind!=JsonValueKind.String=>"Observed DateTime does not match JSON value kind.",
        DateTime=>"Expected JSON timestamp is not an equal DateTime value.",
        bool when expected.ValueKind is not (JsonValueKind.True or JsonValueKind.False)=>"Observed Boolean does not match JSON value kind.",
        bool=>"Expected JSON Boolean differs.",
        _=>"Observed CLR type is not supported by the fail-closed comparator."
    };

    private static string NormalizeExpected(object? actual,JsonElement expected)
    {
        if(actual is null)return expected.ValueKind==JsonValueKind.Null?"null":expected.GetRawText();
        return actual switch
        {
            string when expected.ValueKind==JsonValueKind.String=>expected.GetString()??"",
            short when expected.ValueKind==JsonValueKind.Number&&expected.TryGetInt16(out var n)=>n.ToString(CultureInfo.InvariantCulture),
            byte when expected.ValueKind==JsonValueKind.Number&&expected.TryGetByte(out var n)=>n.ToString(CultureInfo.InvariantCulture),
            int when expected.ValueKind==JsonValueKind.Number&&expected.TryGetInt32(out var n)=>n.ToString(CultureInfo.InvariantCulture),
            long when expected.ValueKind==JsonValueKind.Number&&expected.TryGetInt64(out var n)=>n.ToString(CultureInfo.InvariantCulture),
            decimal when expected.ValueKind==JsonValueKind.Number&&expected.TryGetDecimal(out var n)=>n.ToString("G29",CultureInfo.InvariantCulture),
            DateTime when expected.TryGetDateTime(out var dt)=>dt.ToString("O",CultureInfo.InvariantCulture),
            bool when expected.ValueKind is JsonValueKind.True or JsonValueKind.False=>expected.GetBoolean().ToString(),
            _=>expected.GetRawText()
        };
    }

    private static string NormalizeObserved(object? value)=>value switch
    {
        null=>"null",string s=>s,short n=>n.ToString(CultureInfo.InvariantCulture),byte n=>n.ToString(CultureInfo.InvariantCulture),
        int n=>n.ToString(CultureInfo.InvariantCulture),long n=>n.ToString(CultureInfo.InvariantCulture),
        decimal n=>n.ToString("G29",CultureInfo.InvariantCulture),DateTime dt=>dt.ToString("O",CultureInfo.InvariantCulture),
        bool b=>b.ToString(),_=>"<unsupported>"
    };

    private static string RawValue(object? value)=>value switch
    {
        null=>"null",string s=>JsonSerializer.Serialize(s),short or byte or int or long or decimal or bool or DateTime=>JsonSerializer.Serialize(value,JsonOptions),
        _=>"<"+(value.GetType().FullName??value.GetType().Name)+">"
    };

    private static bool JsonValueEquals(object? actual,JsonElement expected)
    {
        if(actual is null)return expected.ValueKind==JsonValueKind.Null;
        return actual switch
        {
            string value=>expected.ValueKind==JsonValueKind.String&&expected.GetString()==value,
            short value=>expected.ValueKind==JsonValueKind.Number&&expected.TryGetInt16(out var parsed)&&parsed==value,
            byte value=>expected.ValueKind==JsonValueKind.Number&&expected.TryGetByte(out var parsed)&&parsed==value,
            int value=>expected.ValueKind==JsonValueKind.Number&&expected.TryGetInt32(out var parsed)&&parsed==value,
            long value=>expected.ValueKind==JsonValueKind.Number&&expected.TryGetInt64(out var parsed)&&parsed==value,
            decimal value=>expected.ValueKind==JsonValueKind.Number&&expected.TryGetDecimal(out var parsed)&&parsed==value,
            DateTime value=>expected.ValueKind==JsonValueKind.String&&expected.TryGetDateTime(out var parsed)&&parsed==value,
            bool value=>expected.ValueKind is JsonValueKind.True or JsonValueKind.False&&expected.GetBoolean()==value,
            _=>false
        };
    }

    private static string JsonString(JsonElement parent,string name)=>parent.GetProperty(name).GetString()??"";
    private static long JsonLong(JsonElement parent,string name)=>parent.GetProperty(name).GetInt64();
    private static decimal JsonDecimal(JsonElement parent,string name)=>parent.GetProperty(name).GetDecimal();

    private static int RecoveryComparatorOfflineReplay(string prestatePath,string catalogPath,string failureStatePath)
    {
        try
        {
            Need(File.Exists(prestatePath)&&File.Exists(catalogPath)&&File.Exists(failureStatePath),"Offline replay input is missing.");
            using var pre=JsonDocument.Parse(File.ReadAllText(prestatePath));using var catalog=JsonDocument.Parse(File.ReadAllText(catalogPath));using var failure=JsonDocument.Parse(File.ReadAllText(failureStatePath));
            var expected=pre.RootElement.GetProperty("Evidence");var observedDomains=new Dictionary<string,JsonElement>(StringComparer.Ordinal)
            {
                ["Identity"]=catalog.RootElement.GetProperty("TargetIdentity"),["Scope"]=catalog.RootElement.GetProperty("CurrentScope"),
                ["Worker"]=catalog.RootElement.GetProperty("DedicatedWorker"),["ExistingJob"]=catalog.RootElement.GetProperty("ExistingGiaiDoan2Job")
            };
            var catalogResults=new List<object>();var catalogPass=true;
            foreach(var domain in new[]{"Scope","Identity","Worker","ExistingJob"})
            {
                var actual=JsonObjectToClrDictionary(observedDomains[domain]);
                var result=CompareBaselineDetailed(domain,actual,expected.GetProperty(domain));
                catalogPass&=result.Matches;
                catalogResults.Add(new{Domain=domain,ExpectedFieldCount=expected.GetProperty(domain).EnumerateObject().Count(),ObservedFieldCount=actual.Count,ComparatorResult=result.Matches?"PASS":"FAIL",DifferenceCount=result.Differences.Count,Differences=result.Differences});
            }
            var failureDomains=new List<object>();var failureRoot=failure.RootElement;
            var failurePass=true;
            foreach(var domain in new[]{"Scope","Worker","ExistingJob"})
            {
                var actual=JsonObjectToClrDictionary(failureRoot.GetProperty(domain));
                var result=CompareBaselineDetailed(domain,actual,expected.GetProperty(domain));
                failurePass&=result.Matches;
                failureDomains.Add(new{Domain=domain,ExpectedFieldCount=expected.GetProperty(domain).EnumerateObject().Count(),ObservedFieldCount=actual.Count,ComparatorResult=result.Matches?"PASS":"FAIL",DifferenceCount=result.Differences.Count,Differences=result.Differences});
            }
            var identityPersisted=failureRoot.TryGetProperty("Identity",out _);
            var report=new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2h-d-offline-replay/1",ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,
                Status=catalogPass&&failurePass&&!identityPersisted?"PASS_STORED_PAIRS_ORIGINAL_FAILURE_NOT_REPRODUCED":"OFFLINE_REPLAY_FOUND_DIFFERENCE",
                ComparatorOfflineResult=catalogPass&&failurePass?"PASS_STORED_COMPARISONS":"FAIL_STORED_COMPARISON",
                OriginalFailureReproduced=false,ExactFailureReplayPossible=false,
                ExactReplayLimitation="Failure-State omitted IdentityAtFailure and no capture persisted historical CLR runtime types; stored catalog/prestate replay cannot identify the original rejecting predicate.",
                RuntimeTypeReconstruction="Stored JSON scalars were parsed into supported CLR primitive shapes to execute the current comparator code. This does not recover unpersisted historical GetValue runtime types; exact type evidence is unavailable.",
                ComparedDomains=new[]{"Scope","Identity","Worker","ExistingJob"},CatalogPairResults=catalogResults,FailureStateResults=failureDomains,
                IdentityAtFailure=identityPersisted?"PRESENT":"NOT_CAPTURED",FailingDomain=(string?)null,FailingField=(string?)null,
                Inputs=new{Prestate=new{Path=Path.GetFullPath(prestatePath),Sha256=HashFile(prestatePath)},PreInvocationCatalog=new{Path=Path.GetFullPath(catalogPath),Sha256=HashFile(catalogPath)},FailureState=new{Path=Path.GetFullPath(failureStatePath),Sha256=HashFile(failureStatePath)}},
                DirectDatabaseAccess=0,WorkerProcedureInvocationCount=0
            };
            WriteNew("Batch4-Q2H-D-Offline-Replay.json",report);
            Console.WriteLine(JsonSerializer.Serialize(new{report.Status,report.ComparatorOfflineResult,report.OriginalFailureReproduced,report.IdentityAtFailure}));
            return catalogPass&&failurePass?0:1;
        }
        catch(Exception ex)
        {
            try{WriteNew("Batch4-Q2H-D-Offline-Replay-Failure.json",new{ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,Status="OFFLINE_REPLAY_FAILED",Error=Safe(ex),DirectDatabaseAccess=0});}catch{}
            Console.Error.WriteLine("Q2H-D offline comparator replay failed: "+Safe(ex));return 1;
        }
    }

    private static Dictionary<string,object?> JsonObjectToClrDictionary(JsonElement value)
    {
        Need(value.ValueKind==JsonValueKind.Object,"Offline comparator input must be a JSON object.");
        var result=new Dictionary<string,object?>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())result[property.Name]=JsonValueToClr(property.Value);
        return result;
    }

    private static object? JsonValueToClr(JsonElement value)=>value.ValueKind switch
    {
        JsonValueKind.Null=>null,
        JsonValueKind.String=>DateTime.TryParse(value.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var dt)&&value.GetString()!.Contains('T')?dt:value.GetString(),
        JsonValueKind.Number=>JsonNumberToClr(value),
        JsonValueKind.True=>true,
        JsonValueKind.False=>false,
        _=>value.GetRawText()
    };

    private static object JsonNumberToClr(JsonElement value)
    {
        var raw=value.GetRawText();
        if(raw.Contains('.')||raw.Contains('e')||raw.Contains('E'))return value.GetDecimal();
        if(value.TryGetInt32(out var intValue))return intValue;
        if(value.TryGetInt64(out var longValue))return longValue;
        return value.GetDecimal();
    }

    private static async Task<int> RecoveryComparatorReadOnlyDiagnosticAsync(string prestatePath)
    {
        try
        {
            Need(!File.Exists(Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Claim.json"))&&!File.Exists(Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json")),"Read-only diagnostic root must not contain a recovery claim or consumed marker.");
            Need(File.Exists(prestatePath),"Read-only comparator diagnostic requires the immutable prior recovery prestate.");
            using var pre=JsonDocument.Parse(File.ReadAllText(prestatePath));var preRoot=pre.RootElement;
            Need(JsonString(preRoot,"Status")=="PASS_RECOVERY_PRESTATE","Read-only diagnostic baseline is not a passing recovery prestate.");
            var captureAStartedUtc=DateTimeOffset.UtcNow;var captureA=await CaptureRecoveryGuardStateAsync();var captureACompletedUtc=DateTimeOffset.UtcNow;
            var captureBStartedUtc=DateTimeOffset.UtcNow;var captureB=await CaptureRecoveryGuardStateAsync();var captureBCompletedUtc=DateTimeOffset.UtcNow;
            WriteNew("Batch4-Q2H-D-ReadOnly-Capture-Pair.json",new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2h-d-capture-pair/1",ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,
                CaptureCount=2,CaptureAStartedUtc=captureAStartedUtc,CaptureACompletedUtc=captureACompletedUtc,CaptureBStartedUtc=captureBStartedUtc,CaptureBCompletedUtc=captureBCompletedUtc,
                CaptureA=new{captureA.Identity,captureA.Scope,captureA.Worker,captureA.ExistingJob,CaptureClrTypes=CaptureClrTypes(captureA)},
                CaptureB=new{captureB.Identity,captureB.Scope,captureB.Worker,captureB.ExistingJob,CaptureClrTypes=CaptureClrTypes(captureB)},
                RecoveryClaimCreated=false,RecoveryClaimConsumed=false,WorkerProcedureInvocationCount=0
            });
            using var captureAJson=JsonDocument.Parse(JsonSerializer.Serialize(new{Scope=captureA.Scope,Identity=captureA.Identity,Worker=captureA.Worker,ExistingJob=captureA.ExistingJob},JsonOptions));
            var expectedPre=preRoot.GetProperty("Evidence");var expectedPreTypes=preRoot.TryGetProperty("EvidenceClrTypes",out var typeManifest)?typeManifest:(JsonElement?)null;
            var baselineResults=new List<object>();var pairResults=new List<object>();var allBaselinePass=true;var allPairPass=true;
            foreach(var domain in new[]{"Scope","Identity","Worker","ExistingJob"})
            {
                var observed=domain switch{"Scope"=>captureB.Scope,"Identity"=>captureB.Identity,"Worker"=>captureB.Worker,_=>captureB.ExistingJob};
                var typeDomain=expectedPreTypes is {ValueKind:JsonValueKind.Object} types&&types.TryGetProperty(domain,out var foundTypes)?foundTypes:(JsonElement?)null;
                var baselineResult=CompareBaselineDetailed(domain,observed,expectedPre.GetProperty(domain),typeDomain);
                allBaselinePass&=baselineResult.Matches;
                baselineResults.Add(new{Domain=domain,FieldCount=observed.Count,Status=baselineResult.Matches?"PASS":"FAIL",Differences=baselineResult.Differences});
                var expectedAClrTypes=JsonSerializer.SerializeToElement(ClrTypes(domain switch{"Scope"=>captureA.Scope,"Identity"=>captureA.Identity,"Worker"=>captureA.Worker,_=>captureA.ExistingJob}));
                var pairResult=CompareBaselineDetailed(domain,observed,captureAJson.RootElement.GetProperty(domain),expectedAClrTypes);
                allPairPass&=pairResult.Matches;
                pairResults.Add(new{Domain=domain,FieldCount=observed.Count,Status=pairResult.Matches?"PASS":"FAIL",Differences=pairResult.Differences});
            }
            var schema=await ReadComparisonSqlTypesAsync();
            var rawQueue=await ReadQueue11RawNullableAsync();
            var procedure=await ReadMovementWorkerProcedureIdentityAsync();
            var inventory=BuildComparatorFieldInventory(captureB,expectedPre,schema);
            WriteNew("Batch4-Q2H-D-Comparator-Field-Inventory.json",new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2h-d-comparator-field-inventory/1",ObservedUtc=DateTimeOffset.UtcNow,
                DiagnosticRunId=_runId,ComparedDomainCount=4,ComparedFieldCount=inventory.Count,PriorCaptureClrTypesPersisted=false,
                Fields=inventory
            });
            WriteNew("Batch4-Q2H-D-ReadOnly-Database-Probe.json",new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2h-d-readonly-database-probe/1",ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,
                Status=allPairPass?"PASS_TWO_CAPTURE_STABILITY":"MISMATCH_FAIL_CLOSED",DiagnosticReadOnly=true,CaptureCount=2,
                ComparatorOfflineBaselineResults=baselineResults,ComparatorCapturePairResults=pairResults,
                PrestatePath=Path.GetFullPath(prestatePath),PrestateSha256=HashFile(prestatePath),CurrentScope=captureB.Scope,
                CurrentIdentity=captureB.Identity,CurrentWorker=captureB.Worker,CurrentExistingJob=captureB.ExistingJob,
                CurrentCaptureClrTypes=CaptureClrTypes(captureB),RawQueue11NullableFields=rawQueue,MovementWorkerProcedure=procedure,
                SqlAgentServiceState="CAPTURED_BY_BOUNDED_EXTERNAL_READ_ONLY_CHECK",ReadOnlySqlSchemaQueries=3,
                RecoveryClaimCreated=false,RecoveryClaimConsumed=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,
                CanonicalPerformanceStarted=false,PrestateComparison=allBaselinePass?"PASS":"FAIL_CLOSED"
            });
            Console.WriteLine(JsonSerializer.Serialize(new{Status=allPairPass?"PASS_TWO_CAPTURE_STABILITY":"MISMATCH_FAIL_CLOSED",PrestateComparison=allBaselinePass?"PASS":"FAIL",FieldCount=inventory.Count,Current=captureB.Scope["CurrentQuantity"],Queue11=captureB.Scope["Q2HMovementQueueId11Status"],WorkerProcedureInvocationCount=0}));
            return allPairPass?0:1;
        }
        catch(Exception ex)
        {
            try{WriteNew("Batch4-Q2H-D-ReadOnly-Database-Probe-Failure.json",new{ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,Status="READ_ONLY_DIAGNOSTIC_FAILED",Error=Safe(ex),RecoveryClaimCreated=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0});}catch{}
            Console.Error.WriteLine("Q2H-D read-only diagnostic failed: "+Safe(ex));return 1;
        }
    }

    private static async Task<int> RecoveryComparatorReadOnlyFollowupAsync(string capturePairPath,string prestatePath)
    {
        try
        {
            Need(!File.Exists(Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Claim.json"))&&!File.Exists(Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json")),"Read-only follow-up root must not contain a recovery claim or consumed marker.");
            Need(File.Exists(capturePairPath)&&File.Exists(prestatePath),"Read-only follow-up inputs are missing.");
            using var pair=JsonDocument.Parse(File.ReadAllText(capturePairPath));using var pre=JsonDocument.Parse(File.ReadAllText(prestatePath));
            var expectedCapture=pair.RootElement.GetProperty("CaptureB");var expectedPre=pre.RootElement.GetProperty("Evidence");
            var expectedTypes=expectedCapture.GetProperty("CaptureClrTypes");var captureStarted=DateTimeOffset.UtcNow;var current=await CaptureRecoveryGuardStateAsync();var captureCompleted=DateTimeOffset.UtcNow;
            WriteNew("Batch4-Q2H-D-ReadOnly-PostFix-Capture.json",new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2h-d-postfix-capture/1",ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,
                CaptureStartedUtc=captureStarted,CaptureCompletedUtc=captureCompleted,Identity=current.Identity,Scope=current.Scope,
                Worker=current.Worker,ExistingJob=current.ExistingJob,CaptureClrTypes=CaptureClrTypes(current),
                RecoveryClaimCreated=false,RecoveryClaimConsumed=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0
            });
            var persistedPairResults=new List<object>();var prestateResults=new List<object>();var pairPass=true;var prestatePass=true;
            foreach(var domain in new[]{"Scope","Identity","Worker","ExistingJob"})
            {
                var observed=domain switch{"Scope"=>current.Scope,"Identity"=>current.Identity,"Worker"=>current.Worker,_=>current.ExistingJob};
                var expectedClrDomain=expectedTypes.GetProperty(domain);
                var fromPrior=CompareBaselineDetailed(domain,observed,expectedCapture.GetProperty(domain),expectedClrDomain);
                var fromPrestate=CompareBaselineDetailed(domain,observed,expectedPre.GetProperty(domain));
                pairPass&=fromPrior.Matches;prestatePass&=fromPrestate.Matches;
                persistedPairResults.Add(new{Domain=domain,Status=fromPrior.Matches?"PASS":"FAIL",Differences=fromPrior.Differences});
                prestateResults.Add(new{Domain=domain,Status=fromPrestate.Matches?"PASS":"FAIL",Differences=fromPrestate.Differences});
            }
            WriteNew("Batch4-Q2H-D-ReadOnly-PostFix-Probe.json",new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2h-d-postfix-probe/1",ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,
                Status=pairPass&&prestatePass?"PASS_READ_ONLY_POSTFIX_COMPARATOR":"MISMATCH_FAIL_CLOSED",DiagnosticReadOnly=true,
                CaptureCount=1,ComparedAgainstPersistedCaptureB=true,ComparedAgainstRecoveryPrestate=true,
                PersistedCaptureBResults=persistedPairResults,RecoveryPrestateResults=prestateResults,
                CurrentIdentity=current.Identity,CurrentScope=current.Scope,CurrentWorker=current.Worker,CurrentExistingJob=current.ExistingJob,
                CurrentCaptureClrTypes=CaptureClrTypes(current),RecoveryClaimCreated=false,RecoveryClaimConsumed=false,
                WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0,CanonicalPerformanceStarted=false
            });
            Console.WriteLine(JsonSerializer.Serialize(new{Status=pairPass&&prestatePass?"PASS_READ_ONLY_POSTFIX_COMPARATOR":"MISMATCH_FAIL_CLOSED",ComparedAgainstCaptureB=pairPass?"PASS":"FAIL",ComparedAgainstPrestate=prestatePass?"PASS":"FAIL",Current=current.Scope["CurrentQuantity"],Queue11=current.Scope["Q2HMovementQueueId11Status"],WorkerProcedureInvocationCount=0}));
            return pairPass&&prestatePass?0:1;
        }
        catch(Exception ex)
        {
            try{WriteNew("Batch4-Q2H-D-ReadOnly-PostFix-Probe-Failure.json",new{ObservedUtc=DateTimeOffset.UtcNow,DiagnosticRunId=_runId,Status="READ_ONLY_POSTFIX_PROBE_FAILED",Error=Safe(ex),RecoveryClaimCreated=false,WorkerProcedureInvocationCount=0,DatabaseWritesExecuted=0});}catch{}
            Console.Error.WriteLine("Q2H-D read-only post-fix probe failed: "+Safe(ex));return 1;
        }
    }

    private sealed record SqlFieldMetadata(string SqlType,string RuntimeClrType,int? ColumnSize,int? Precision,int? Scale,bool? AllowsNull);

    private static async Task<Evidence> CaptureRecoveryGuardStateAsync()
    {
        var builder=new SqlConnectionStringBuilder(_cs);
        Need(builder.IntegratedSecurity&&string.IsNullOrWhiteSpace(builder.UserID)&&string.IsNullOrWhiteSpace(builder.Password),"Read-only diagnostic connection no longer matches Integrated Security contract.");
        await using var connection=new SqlConnection(_cs);await connection.OpenAsync();
        var identity=await One(connection,IdentitySql,_=>{});
        var scope=await One(connection,ScopeSql,q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);});
        var worker=await Job(connection,PerfJob);var existing=await Job(connection,GiaiDoan2Job);
        worker["TargetMovementJobsCount"]=Convert.ToInt32(await Scalar(connection,"SELECT COUNT(*) FROM msdb.dbo.sysjobsteps WHERE database_name=N'TKS_Thuc_Tap_V11_Perf_10000000' AND command LIKE N'%sp_Inventory_Movement_Process_RebuildQueue%';"),CultureInfo.InvariantCulture);
        return new Evidence(identity,scope,worker,existing,new(StringComparer.Ordinal),"NOT_CAPTURED_READ_ONLY_DIAGNOSTIC");
    }

    private static async Task<Dictionary<string,SqlFieldMetadata>> DescribeSqlFieldsAsync(SqlConnection connection,string sql,Action<SqlCommand> bind)
    {
        await using var command=connection.CreateCommand();command.CommandText=sql;command.CommandTimeout=15;bind(command);
        await using var reader=await command.ExecuteReaderAsync(System.Data.CommandBehavior.SchemaOnly);
        var table=reader.GetSchemaTable();var result=new Dictionary<string,SqlFieldMetadata>(StringComparer.Ordinal);
        for(var index=0;index<reader.FieldCount;index++)
        {
            var row=table is not null&&index<table.Rows.Count?table.Rows[index]:null;
            int? ReadInt(string column)=>row is null||row[column] is DBNull?null:Convert.ToInt32(row[column],CultureInfo.InvariantCulture);
            bool? ReadBool(string column)=>row is null||row[column] is DBNull?null:Convert.ToBoolean(row[column],CultureInfo.InvariantCulture);
            result[reader.GetName(index)]=new(reader.GetDataTypeName(index),reader.GetFieldType(index).FullName??"UNKNOWN",ReadInt("ColumnSize"),ReadInt("NumericPrecision"),ReadInt("NumericScale"),ReadBool("AllowDBNull"));
        }
        return result;
    }

    private static async Task<Dictionary<string,Dictionary<string,SqlFieldMetadata>>> ReadComparisonSqlTypesAsync()
    {
        await using var connection=new SqlConnection(_cs);await connection.OpenAsync();
        var result=new Dictionary<string,Dictionary<string,SqlFieldMetadata>>(StringComparer.Ordinal)
        {
            ["Identity"]=await DescribeSqlFieldsAsync(connection,IdentitySql,_=>{}),
            ["Scope"]=await DescribeSqlFieldsAsync(connection,ScopeSql,q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);}),
            ["Worker"]=await DescribeSqlFieldsAsync(connection,JobSql,q=>Param(q,"@id",System.Data.SqlDbType.UniqueIdentifier,PerfJob)),
            ["ExistingJob"]=await DescribeSqlFieldsAsync(connection,JobSql,q=>Param(q,"@id",System.Data.SqlDbType.UniqueIdentifier,GiaiDoan2Job))
        };
        result["Worker"]["TargetMovementJobsCount"]=new("int","System.Int32",null,null,null,false);
        return result;
    }

    private static async Task<Dictionary<string,object?>> ReadQueue11RawNullableAsync()
    {
        await using var connection=new SqlConnection(_cs);await connection.OpenAsync();
        return await One(connection,"SELECT (SELECT Claimed_Version FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) ClaimedVersion,(SELECT ProcessedAt FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) ProcessedAt,(SELECT Status FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) Status,(SELECT Retry_Count FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) RetryCount,(SELECT Requested_Version FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) RequestedVersion,(SELECT COUNT_BIG(*) FROM dbo.InventoryMovement_RebuildQueue WHERE ID=11) Rows;",_=>{});
    }

    private static async Task<object> ReadMovementWorkerProcedureIdentityAsync()
    {
        await using var connection=new SqlConnection(_cs);await connection.OpenAsync();
        var definition=Convert.ToString(await Scalar(connection,"SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_Inventory_Movement_Process_RebuildQueue'))"),CultureInfo.InvariantCulture)??"";
        var canExecute=Convert.ToInt32(await Scalar(connection,"SELECT HAS_PERMS_BY_NAME(N'dbo.sp_Inventory_Movement_Process_RebuildQueue',N'OBJECT',N'EXECUTE')"),CultureInfo.InvariantCulture)==1;
        return new{Name="dbo.sp_Inventory_Movement_Process_RebuildQueue",DefinitionPresent=definition.Length>0,DefinitionSha256=definition.Length==0?"":Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(definition))),CanExecute=canExecute,DirectExecutionPerformed=false};
    }

    private static List<object> BuildComparatorFieldInventory(Evidence current,JsonElement expected,Dictionary<string,Dictionary<string,SqlFieldMetadata>> schemas)
    {
        var result=new List<object>();
        foreach(var domain in new[]{"Scope","Identity","Worker","ExistingJob"})
        {
            var values=domain switch{"Scope"=>current.Scope,"Identity"=>current.Identity,"Worker"=>current.Worker,_=>current.ExistingJob};
            var expectedDomain=expected.GetProperty(domain);
            foreach(var item in values)
            {
                var column=schemas[domain].GetValueOrDefault(item.Key);var currentJson=JsonSerializer.SerializeToElement(item.Value,JsonOptions);
                expectedDomain.TryGetProperty(item.Key,out var preValue);
                result.Add(new
                {
                    Domain=domain,FieldName=item.Key,SourceQueryColumn=(domain=="Identity"?"IdentitySql":domain=="Scope"?"ScopeSql":"JobSql")+"."+item.Key,
                    SourceSQLType=column?.SqlType??"UNKNOWN",SqlColumnSize=column?.ColumnSize,SqlPrecision=column?.Precision,SqlScale=column?.Scale,
                    ExpectedCLRType=column?.RuntimeClrType??"UNKNOWN",ExpectedCLRTypeEvidence="Same SQL projection contract; prior CLR runtime type was not persisted.",
                    PrestateJSONType=preValue.ValueKind.ToString(),CurrentCaptureCLRType=item.Value?.GetType().FullName??"null",CurrentJSONType=currentJson.ValueKind.ToString(),
                    SqlAllowsNull=column?.AllowsNull,PrestateValueIsNull=preValue.ValueKind==JsonValueKind.Null,CurrentValueIsNull=item.Value is null,
                    ComparisonExpression=ComparisonRule(item.Value),ComparisonOperator="typed equality (==)",NormalizationApplied="None; no ToString conversion; JSON is parsed through the observed CLR type branch.",
                    VolatilityClassification=ClassifyComparatorField(domain,item.Key),GuardRationale=ComparatorGuardRationale(domain,item.Key)
                });
            }
        }
        return result;
    }

    private static string ClassifyComparatorField(string domain,string field)
    {
        if(field=="BusinessDate")return "OBSERVATIONAL_VOLATILE";
        if(domain=="Identity")return "DATABASE_IDENTITY_MUST_BE_STABLE";
        if(domain=="Worker")return field=="ExecutionHistoryCount"?"OBSERVATIONAL_VOLATILE":"WORKER_DEFINITION_MUST_BE_STABLE";
        if(domain=="ExistingJob")return field=="ExecutionHistoryCount"?"OBSERVATIONAL_VOLATILE":"JOB_DEFINITION_MUST_BE_STABLE";
        if(field.Contains("Rows",StringComparison.Ordinal)||field.Contains("Count",StringComparison.Ordinal)||field.Contains("DeadLetters",StringComparison.Ordinal)||field.Contains("Quantity",StringComparison.Ordinal)||field.StartsWith("Today",StringComparison.Ordinal)||field.Contains("Ledger",StringComparison.Ordinal)||field.Contains("MovementReceipt",StringComparison.Ordinal)||field.Contains("MovementIssue",StringComparison.Ordinal)||field.Contains("BalanceOpening",StringComparison.Ordinal)||field.Contains("BalanceReceived",StringComparison.Ordinal)||field.Contains("BalanceIssued",StringComparison.Ordinal)||field.Contains("BalanceClosing",StringComparison.Ordinal))return "DERIVED_VALUE";
        return "BUSINESS_STATE_MUST_BE_STABLE";
    }

    private static string ComparatorGuardRationale(string domain,string field)=>domain switch
    {
        "Identity" when field=="BusinessDate"=>"Anchors the date-specific scope and must remain within the guarded business day.",
        "Identity"=>"Protects target database, login, role, account, and frozen master-data identity.",
        "Worker" when field=="ExecutionHistoryCount"=>"A change can indicate an overlapping dedicated worker execution; retain fail-closed guard.",
        "Worker"=>"Protects disabled dedicated worker routing and definition metadata.",
        "ExistingJob" when field=="ExecutionHistoryCount"=>"A change can indicate existing job execution; retain fail-closed guard.",
        "ExistingJob"=>"Protects the pre-existing GiaiDoan2 job definition and enablement.",
        _ when field=="BusinessDate"=>"Date-specific derived scope; a day rollover changes the meaning of Today fields.",
        _ when ClassifyComparatorField(domain,field)=="DERIVED_VALUE"=>"Derived count or quantity is a bounded safety oracle for the scoped business state.",
        _=>"Direct scoped document, queue, claim, error, or processing state must not change before worker admission."
    };

    private static async Task RunQ2Async(Evidence pre)
    {
        var date=(DateTime)pre.Identity["BusinessDate"]!;
        State["StartedUtc"]=DateTimeOffset.UtcNow;State["DataAccessAssemblySha256"]=pre.DataAccessAssemblySha256;
        State["SqlPrivilegeLimitation"]="SQL_PRIVILEGE_LIMITATION: dbo/sysadmin; least-privilege behavior NOT_VERIFIED.";
        State["WriteScope"]=new{Warehouse,Product,Supplier};
        State["SnapshotGuardTransitions"]=new List<object>();
        Persist("Q2_IN_PROGRESS");
        var app="WHB22-B4-Q2";
        var receiptDraft=No("RCPT-DRAFT-01");var issueDraft=No("ISSUE-DRAFT-01");
        var receiptPost=No("RCPT-POST-01");var issuePost=No("ISSUE-POST-01");

        await Step("receipt draft create/read/delete",async()=>{
            _draftReceipt=true;var h=Header(true,receiptDraft,date);await Docs.Save_Document_Async(h,Login,app,Login);_draftHeader=h.Auto_ID;State["ReceiptDraftHeaderId"]=h.Auto_ID;Persist("Q2_IN_PROGRESS");
            var d=Detail(h.Auto_ID,.001m);await Docs.Save_Document_Detail_Async(true,d,Login,app,Login);_draftDetail=d.Auto_ID;State["ReceiptDraftDetailId"]=d.Auto_ID;Persist("Q2_IN_PROGRESS");
            await VerifyDocument(true,receiptDraft,h.Auto_ID,d.Auto_ID,false,.001m);
            await Docs.Delete_Document_Detail_Async(true,d.Auto_ID,Login,app,Login);_draftDetail=null;await Docs.Delete_Document_Async(true,h.Auto_ID,Login,app,Login);_draftHeader=null;
            var p=await Docs.List_Documents_Page_Async(true,1,10,receiptDraft,Login,Warehouse);Need(p.Total_Count==0&&p.Items.Count==0,"Receipt draft remained after application cleanup.");
        });

        await Step("issue draft reservation create/update/release",async()=>{
            _draftReceipt=false;var h=Header(false,issueDraft,date);await Docs.Save_Document_Async(h,Login,app,Login);_draftHeader=h.Auto_ID;State["IssueDraftHeaderId"]=h.Auto_ID;Persist("Q2_IN_PROGRESS");
            var d=Detail(h.Auto_ID,.001m);await Docs.Save_Document_Detail_Async(false,d,Login,app,Login);_draftDetail=d.Auto_ID;State["IssueDraftDetailId"]=d.Auto_ID;Persist("Q2_IN_PROGRESS");
            var r=await Reservation();Need(r.Rows==1&&r.Qty==.001m,"Expected issue reservation .001.");State["IssueReservation001"]=r;
            d.Auto_ID=L(State,"IssueDraftDetailId");d.So_Luong=.002m;await Docs.Save_Document_Detail_Async(false,d,Login,app,Login);r=await Reservation();Need(r.Rows==1&&r.Qty==.002m,"Issue reservation update to .002 failed.");State["IssueReservation002"]=r;
            await Docs.Delete_Document_Detail_Async(false,d.Auto_ID,Login,app,Login);_draftDetail=null;await Docs.Delete_Document_Async(false,h.Auto_ID,Login,app,Login);_draftHeader=null;
            r=await Reservation();Need(r.Rows==0&&r.Qty==0m,"Issue draft cleanup did not release reservation.");State["IssueReservationReleased"]=r;
            var p=await Docs.List_Documents_Page_Async(false,1,10,issueDraft,Login,Warehouse);Need(p.Total_Count==0&&p.Items.Count==0,"Issue draft remained after cleanup.");
        });

        await Step("receipt post and application verification",async()=>{
            _draftReceipt=true;var h=Header(true,receiptPost,date);await Docs.Save_Document_Async(h,Login,app,Login);_draftHeader=h.Auto_ID;State["ReceiptPostDocumentId"]=h.Auto_ID;State["ReceiptPostDocumentNumber"]=receiptPost;Persist("Q2_IN_PROGRESS");
            var d=Detail(h.Auto_ID,.001m);await Docs.Save_Document_Detail_Async(true,d,Login,app,Login);_draftDetail=d.Auto_ID;State["ReceiptPostDetailId"]=d.Auto_ID;Persist("Q2_IN_PROGRESS");
            await VerifyDocument(true,receiptPost,h.Auto_ID,d.Auto_ID,false,.001m);
            var beforePost=await Scope();RequireNoSnapshotMutation(pre.Scope,beforePost,"Snapshot state changed before receipt POST.");State["BeforeReceiptPost"]=beforePost;
            Event("POST_INTENT",new{DocumentId=h.Auto_ID,DocumentNumber=receiptPost,Direction="RECEIPT",Quantity=".001"});
            await Docs.Post_Document_Async(true,h.Auto_ID,Login,app,Login);_draftHeader=null;_draftDetail=null;State["PostedReceiptDocumentId"]=h.Auto_ID;State["ReceiptPostStatus"]="PASS";Persist("Q2_IN_PROGRESS");
            var after=await Scope();Need(D(after,"CurrentQuantity")==D(pre.Scope,"CurrentQuantity")+.001m&&L(after,"MovementQueueActiveGlobal")==1&&L(after,"MovementQueueActiveScope")==1,"Receipt post delta or movement enqueue mismatch.");
            ValidateExpectedSnapshotTransition(beforePost,after,"RECEIPT_POST");State["AfterReceiptPost"]=after;RecordSnapshotTransition("RECEIPT_POST",beforePost,after);Persist("Q2_IN_PROGRESS");
            State["ReceiptDuplicatePost"]=await VerifyDuplicatePost(true,h.Auto_ID,"RECEIPT",receiptPost,app);
            var duplicateAfter=await Scope();Need(SameScope(after,duplicateAfter),"Receipt duplicate-post attempt changed bounded inventory/queue state.");State["ReceiptDuplicatePostNoMutation"]="PASS";Event("DUPLICATE_POST_NO_MUTATION",new{Direction="RECEIPT",DocumentId=h.Auto_ID,Status="PASS"});Persist("Q2_IN_PROGRESS");
        });
        var receiptQueue=await NewQueueRows(L(pre.Scope,"MovementQueueMaxId"));Need(receiptQueue.Count==1&&receiptQueue[0].W==Warehouse&&receiptQueue[0].P==Product,"Receipt post created unexpected queue rows.");
        await Step("receipt worker drain",async()=>State["ReceiptWorker"]=await DrainWorker(receiptQueue[0].Id,(Dictionary<string,object?>)State["AfterReceiptPost"]!,L(pre.Worker,"ExecutionHistoryCount")));
        await Step("receipt current/historical read-after-write",async()=>{
            State["ReceiptCurrentReport"]=await CurrentReport(date,D(pre.Scope,"CurrentQuantity")+.001m,0m);
            State["ReceiptHistoricalReport"]=await HistoricalReport(date,D(pre.Scope,"TodayMovementReceipt")+.001m,D(pre.Scope,"TodayMovementIssue"),D(pre.Scope,"CurrentQuantity")+.001m);
        });

        var maxBeforeIssue=L((await Scope()),"MovementQueueMaxId");
        await Step("issue post and application verification",async()=>{
            _draftReceipt=false;var h=Header(false,issuePost,date);await Docs.Save_Document_Async(h,Login,app,Login);_draftHeader=h.Auto_ID;State["IssuePostDocumentId"]=h.Auto_ID;State["IssuePostDocumentNumber"]=issuePost;Persist("Q2_IN_PROGRESS");
            var d=Detail(h.Auto_ID,.001m);await Docs.Save_Document_Detail_Async(false,d,Login,app,Login);_draftDetail=d.Auto_ID;State["IssuePostDetailId"]=d.Auto_ID;Persist("Q2_IN_PROGRESS");
            var r=await Reservation();Need(r.Rows==1&&r.Qty==.001m,"Issue post draft reservation missing.");await VerifyDocument(false,issuePost,h.Auto_ID,d.Auto_ID,false,.001m);
            var receiptPostScope=(Dictionary<string,object?>)State["AfterReceiptPost"]!;var beforePost=await Scope();RequireNoSnapshotMutation(receiptPostScope,beforePost,"Snapshot state changed before issue POST.");State["BeforeIssuePost"]=beforePost;
            Event("POST_INTENT",new{DocumentId=h.Auto_ID,DocumentNumber=issuePost,Direction="ISSUE",Quantity=".001"});
            await Docs.Post_Document_Async(false,h.Auto_ID,Login,app,Login);_draftHeader=null;_draftDetail=null;State["PostedIssueDocumentId"]=h.Auto_ID;State["IssuePostStatus"]="PASS";Persist("Q2_IN_PROGRESS");
            var after=await Scope();Need(D(after,"CurrentQuantity")==D(pre.Scope,"CurrentQuantity")&&D(after,"BalanceReservedQuantity")==0m&&L(after,"ReservationRowsScope")==0&&L(after,"MovementQueueActiveGlobal")==1,"Issue post net/current/reservation/queue state mismatch.");
            ValidateExpectedSnapshotTransition(beforePost,after,"ISSUE_POST");State["AfterIssuePost"]=after;RecordSnapshotTransition("ISSUE_POST",beforePost,after);Persist("Q2_IN_PROGRESS");
            State["IssueDuplicatePost"]=await VerifyDuplicatePost(false,h.Auto_ID,"ISSUE",issuePost,app);
            var duplicateAfter=await Scope();Need(SameScope(after,duplicateAfter),"Issue duplicate-post attempt changed bounded inventory/queue state.");State["IssueDuplicatePostNoMutation"]="PASS";Event("DUPLICATE_POST_NO_MUTATION",new{Direction="ISSUE",DocumentId=h.Auto_ID,Status="PASS"});Persist("Q2_IN_PROGRESS");
        });
        var issueQueue=await NewQueueRows(maxBeforeIssue);Need(issueQueue.Count==1&&issueQueue[0].W==Warehouse&&issueQueue[0].P==Product,"Issue post created unexpected queue rows.");
        await Step("issue worker drain",async()=>State["IssueWorker"]=await DrainWorker(issueQueue[0].Id,(Dictionary<string,object?>)State["AfterIssuePost"]!,L(pre.Worker,"ExecutionHistoryCount")));
        await Step("issue current/historical read-after-write",async()=>{
            State["IssueCurrentReport"]=await CurrentReport(date,D(pre.Scope,"CurrentQuantity"),0m);
            State["IssueHistoricalReport"]=await HistoricalReport(date,D(pre.Scope,"TodayMovementReceipt")+.001m,D(pre.Scope,"TodayMovementIssue")+.001m,D(pre.Scope,"CurrentQuantity"));
        });
    }

    private static CWarehouseDocument Header(bool receipt,string no,DateTime date)=>new(){Is_Receipt=receipt,So_Phieu=no,Kho_ID=Warehouse,NCC_ID=receipt?Supplier:0,Ngay_Chung_Tu=date.Date,Ghi_Chu=$"Bounded Q2 qualification {_runId}"};
    private static CWarehouseDocumentDetail Detail(long id,decimal qty)=>new(){Document_ID=id,San_Pham_ID=Product,So_Luong=qty,Don_Gia=100m};
    private static string No(string suffix)=>$"WHB22B4-{_runId}-{suffix}";

    private static async Task VerifyDocument(bool receipt,string no,long header,long detail,bool posted,decimal qty)
    {
        var p=await Docs.List_Documents_Page_Async(receipt,1,10,no,Login,Warehouse);
        Need(p.Total_Count==1&&p.Items.Count==1&&p.Items[0].Auto_ID==header&&p.Items[0].So_Phieu==no&&p.Items[0].Kho_ID==Warehouse&&p.Items[0].Is_Posted==posted,"Document app readback mismatch.");
        if(receipt)Need(p.Items[0].NCC_ID==Supplier,"Receipt supplier mismatch.");
        var ds=await Docs.List_Document_Details_Async(receipt,header,Login);
        Need(ds.Count==1&&ds[0].Auto_ID==detail&&ds[0].San_Pham_ID==Product&&ds[0].So_Luong==qty,"Document detail app readback mismatch.");
    }

    private sealed record ReservationResult(long Rows,decimal Qty);
    private static async Task<ReservationResult> Reservation()
    {
        await using var c=new SqlConnection(_cs);await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT COUNT_BIG(*),COALESCE(SUM(ReservedQuantity),0) FROM dbo.InventoryReservation_Current WHERE Kho_ID=@w AND San_Pham_ID=@p;";
        Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);await using var r=await q.ExecuteReaderAsync();await r.ReadAsync();return new ReservationResult(Convert.ToInt64(r[0]),Convert.ToDecimal(r[1]));
    }

    private sealed record QueueItem(long Id,long W,long P,string Status,long Retry,long RequestedVersion,long? ClaimedVersion,DateTime? ProcessedAt);
    private static async Task<List<QueueItem>> NewQueueRows(long after)
    {
        await using var c=new SqlConnection(_cs);await c.OpenAsync();return await NewQueueRows(c,after);
    }
    private static async Task<List<QueueItem>> NewQueueRows(SqlConnection c,long after)
    {
        await using var q=c.CreateCommand();q.CommandText="SELECT ID,Kho_ID,San_Pham_ID,Status,Retry_Count,Requested_Version,Claimed_Version,ProcessedAt FROM dbo.InventoryMovement_RebuildQueue WHERE ID>@id ORDER BY ID;";
        Param(q,"@id",System.Data.SqlDbType.BigInt,after);var rows=new List<QueueItem>();await using var r=await q.ExecuteReaderAsync();while(await r.ReadAsync())rows.Add(new QueueItem(Convert.ToInt64(r[0]),Convert.ToInt64(r[1]),Convert.ToInt64(r[2]),r.GetString(3),Convert.ToInt64(r[4]),Convert.ToInt64(r[5]),r.IsDBNull(6)?null:Convert.ToInt64(r[6]),r.IsDBNull(7)?null:Convert.ToDateTime(r[7],CultureInfo.InvariantCulture)));return rows;
    }

    private static async Task<object> DrainWorker(long queueId,Dictionary<string,object?> stableScope,long expectedJobHistoryCount,Action<Dictionary<string,object?>>? onInvocation=null,bool requireDurableRecoveryClaim=false,bool stopBeforeProcedure=false,Action? onProcedureExecutionAttempt=null,Action<Dictionary<string,object?>>? onRehearsalGate=null)
    {
        var before=await WorkerNow();Need(I(before,"JobEnabled")==0&&I(before,"ScheduleEnabled")==0&&I(before,"TargetMovementJobsCount")==1&&L(before,"ExecutionHistoryCount")==expectedJobHistoryCount,"Dedicated worker route precondition/history changed.");
        var started=DateTimeOffset.UtcNow;await using var c=new SqlConnection(_cs);await c.OpenAsync();
        var sessionId=Convert.ToInt32(await Scalar(c,"SELECT @@SPID;"));
        var scopeBefore=await One(c,ScopeSql,q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);});
        Need(L(scopeBefore,"MovementQueueActiveGlobal")==1&&L(scopeBefore,"MovementQueueActiveScope")==1&&L(scopeBefore,"MovementDeadLettersGlobal")==0&&L(scopeBefore,"SnapshotDeadLettersGlobal")==0,"Unexpected queue state before direct worker call.");
        var beforeRows=await NewQueueRows(c,queueId-1);Need(beforeRows.Count==1&&beforeRows[0].Id==queueId&&beforeRows[0].W==Warehouse&&beforeRows[0].P==Product&&beforeRows[0].Status=="WAITING"&&beforeRows[0].Retry==0&&beforeRows[0].RequestedVersion>0&&beforeRows[0].ClaimedVersion is null&&beforeRows[0].ProcessedAt is null,"Expected one fresh WAITING movement queue row before worker call.");
        var scopeAtWorker=await One(c,ScopeSql,q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);});
        Need(L(scopeAtWorker,"MovementQueueActiveGlobal")==1&&L(scopeAtWorker,"MovementQueueActiveScope")==1&&L(scopeAtWorker,"MovementDeadLettersGlobal")==0&&L(scopeAtWorker,"SnapshotDeadLettersGlobal")==0,"Queue state changed immediately before direct worker invocation.");
        await InvokeMovementWorkerAfterSnapshotGateAsync(stableScope,scopeAtWorker,async()=>{
            if(stopBeforeProcedure)
            {
                onRehearsalGate?.Invoke(scopeAtWorker);
                Event("READ_ONLY_WORKER_PREINVOCATION_GATE_REHEARSED",new{QueueId=queueId,SqlSessionId=sessionId,Procedure="dbo.sp_Inventory_Movement_Process_RebuildQueue",InvocationCount=0,DatabaseWritesExecuted=0});
                return;
            }
            onInvocation?.Invoke(scopeAtWorker);
            if(requireDurableRecoveryClaim)
                Need(_recoveryClaimSha256 is not null&&File.Exists(Path.Combine(_root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json")),"Recovery worker procedure cannot run without a durable consumed one-shot claim.");
            Event("DIRECT_WORKER_PROC_INTENT",new{WorkerRoute="PROC_IS_WORKER_AGENT_IS_SCHEDULER",Procedure="dbo.sp_Inventory_Movement_Process_RebuildQueue",QueueId=queueId,SqlSessionId=sessionId,InvocationNumber=1,BatchSize=100,AgentStarted=false,RecoveryClaimConsumed=_recoveryClaimSha256 is not null});
            await using var q=c.CreateCommand();q.CommandText="EXEC dbo.sp_Inventory_Movement_Process_RebuildQueue @Batch_Size = 100;";q.CommandTimeout=180;onProcedureExecutionAttempt?.Invoke();await q.ExecuteNonQueryAsync();
        });
        if(stopBeforeProcedure)
        {
            var rehearsalRows=await NewQueueRows(c,queueId-1);
            Need(rehearsalRows.Count==1&&rehearsalRows[0].Id==queueId&&rehearsalRows[0].Status=="WAITING"&&rehearsalRows[0].Retry==0&&rehearsalRows[0].ClaimedVersion is null&&rehearsalRows[0].ProcessedAt is null,"Read-only pre-invocation rehearsal changed the target queue row.");
            var rehearsalScope=await One(c,ScopeSql,q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);});
            RequireNoSnapshotMutation(stableScope,rehearsalScope,"Read-only pre-invocation rehearsal changed the pending snapshot state.");
            var rehearsalWorker=await WorkerNow();Need(I(rehearsalWorker,"JobEnabled")==0&&I(rehearsalWorker,"ScheduleEnabled")==0&&L(rehearsalWorker,"ExecutionHistoryCount")==expectedJobHistoryCount&&L(rehearsalWorker,"ExistingGiaiDoan2ExecutionHistoryCount")==L(before,"ExistingGiaiDoan2ExecutionHistoryCount"),"Read-only worker pre-invocation rehearsal changed job state/history.");
            var finished=DateTimeOffset.UtcNow;
            return new{Status="PASS_READ_ONLY_PREINVOCATION_REHEARSAL",WorkerRoute="PROC_IS_WORKER_AGENT_IS_SCHEDULER",Procedure="dbo.sp_Inventory_Movement_Process_RebuildQueue",InvocationCount=0,DatabaseWritesExecuted=0,QueueId=queueId,QueueStatusBefore=rehearsalRows[0].Status,RetryCount=rehearsalRows[0].Retry,RequestedVersion=rehearsalRows[0].RequestedVersion,ClaimedVersion=rehearsalRows[0].ClaimedVersion,SqlSessionId=sessionId,StartedUtc=started,FinishedUtc=finished,DurationMs=(long)(finished-started).TotalMilliseconds,AgentStarted=false,DedicatedJobEnabledAfter=I(rehearsalWorker,"JobEnabled"),DedicatedScheduleEnabledAfter=I(rehearsalWorker,"ScheduleEnabled"),SnapshotQueueUnchanged=true,GiaiDoan2JobUnchanged=true};
        }
        var end=DateTimeOffset.UtcNow;var rows=await NewQueueRows(c,queueId-1);Need(rows.Count==1&&rows[0].Id==queueId,"Direct worker call observed unexpected new movement queue rows.");
        var item=rows[0];Need(item.Status=="COMPLETED"&&item.Retry==0&&item.ClaimedVersion==item.RequestedVersion&&item.ProcessedAt is not null,"Expected queue item not COMPLETED with zero retries and matching claimed version/ProcessedAt.");
        var scopeAfter=await One(c,ScopeSql,q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);});
        Need(L(scopeAfter,"MovementQueueActiveGlobal")==0&&L(scopeAfter,"MovementQueueActiveScope")==0&&L(scopeAfter,"MovementDeadLettersGlobal")==0&&L(scopeAfter,"SnapshotDeadLettersGlobal")==0,"Direct worker queue/dead-letter gate failed.");
        RequireNoSnapshotMutation(stableScope,scopeAfter,"Direct movement worker changed the expected pending snapshot queue state.");
        var after=await WorkerNow();Need(I(after,"JobEnabled")==0&&I(after,"ScheduleEnabled")==0&&L(after,"ExecutionHistoryCount")==expectedJobHistoryCount,"SQL Agent dedicated job state/history changed during direct procedure invocation.");
        Need(L(after,"ExistingGiaiDoan2ExecutionHistoryCount")==L(before,"ExistingGiaiDoan2ExecutionHistoryCount"),"GiaiDoan2 Agent job execution history changed during direct procedure invocation.");
        return new{Status="PASS",WorkerRoute="PROC_IS_WORKER_AGENT_IS_SCHEDULER",Procedure="dbo.sp_Inventory_Movement_Process_RebuildQueue",InvocationCount=1,QueueId=queueId,QueueStatusBefore=beforeRows[0].Status,QueueStatusAfter=item.Status,RetryCount=item.Retry,RequestedVersion=item.RequestedVersion,ClaimedVersion=item.ClaimedVersion,ProcessedAt=item.ProcessedAt,SqlSessionId=sessionId,StartedUtc=started,FinishedUtc=end,DurationMs=(long)(end-started).TotalMilliseconds,AgentStarted=false,DedicatedJobEnabledAfter=I(after,"JobEnabled"),DedicatedScheduleEnabledAfter=I(after,"ScheduleEnabled"),DedicatedJobExecutionHistoryBefore=L(before,"ExecutionHistoryCount"),DedicatedJobExecutionHistoryAfter=L(after,"ExecutionHistoryCount"),GiaiDoan2JobUnchanged=true,SnapshotQueueUnchanged=true,MovementQueueActiveAfter=scopeAfter["MovementQueueActiveGlobal"],MovementDeadLetters=scopeAfter["MovementDeadLettersGlobal"]};
    }

    private static async Task<Dictionary<string,object?>> WorkerNow()
    {
        await using var c=new SqlConnection(_cs);await c.OpenAsync();var j=await Job(c,PerfJob);var old=await Job(c,GiaiDoan2Job);
        j["TargetMovementJobsCount"]=Convert.ToInt32(await Scalar(c,"SELECT COUNT(*) FROM msdb.dbo.sysjobsteps WHERE database_name=N'TKS_Thuc_Tap_V11_Perf_10000000' AND command LIKE N'%sp_Inventory_Movement_Process_RebuildQueue%';"));
        Need(T(old,"JobName")=="TKS Warehouse - Inventory Movement Aggregate Rebuild"&&I(old,"JobEnabled")==1&&T(old,"StepDatabase")=="TKS_Thuc_Tap_V11_GiaiDoan2"&&I(old,"ScheduleEnabled")==1,"GiaiDoan2 worker metadata changed.");
        j["ExistingGiaiDoan2ExecutionHistoryCount"]=L(old,"ExecutionHistoryCount");
        return j;
    }

    private static async Task<object> CurrentReport(DateTime date,decimal current,decimal reserved)
    {
        await using var c=new SqlConnection(_cs);await c.OpenAsync();
        var rank=Convert.ToInt64(await Scalar(c,"SELECT COUNT_BIG(*) FROM dbo.InventoryBalance_Current WHERE Kho_ID=@w AND San_Pham_ID<@p;",q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);}));
        const int size=50;var page=Reports.sp_BC_Ton_Kho_Hien_Tai_Page(checked((int)(rank/size)+1),size,Login,Warehouse);
        var row=page.Items.SingleOrDefault(x=>x.Kho_ID==Warehouse&&x.San_Pham_ID==Product);
        Need(row is not null&&page.Total_Count>0&&row.SL_Ton_Thuc_Te==current&&row.SL_Cuoi_Ky==current&&row.SL_Dang_Giu==reserved&&row.SL_Kha_Dung==current-reserved,"Current report app path/oracle mismatch.");
        return new{Status="PASS",Mode="CURRENT",PageSize=size,TotalCount=page.Total_Count,Row=new{row!.Kho_ID,row.San_Pham_ID,row.SL_Ton_Thuc_Te,row.SL_Dang_Giu,row.SL_Kha_Dung}};
    }
    private static async Task<object> HistoricalReport(DateTime date,decimal received,decimal issued,decimal closing)
    {
        await using var c=new SqlConnection(_cs);await c.OpenAsync();
        var target=await One(c,"SELECT k.Ten_Kho,p.Ma_San_Pham FROM dbo.tbl_DM_Kho k CROSS JOIN dbo.tbl_DM_San_Pham p WHERE k.Auto_ID=@w AND p.Auto_ID=@p;",q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);});
        var rank=Convert.ToInt64(await Scalar(c,@"
SELECT COUNT_BIG(*) FROM dbo.Inventory_Balance_Daily_Scope s
JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=s.Kho_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=s.San_Pham_ID
JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID=s.Kho_ID AND ku.Ma_Dang_Nhap=@login
WHERE s.Kho_ID=@w AND s.First_Balance_Date<=@d
AND EXISTS(SELECT 1 FROM dbo.Inventory_Balance_Daily b WHERE b.Kho_ID=s.Kho_ID AND b.San_Pham_ID=s.San_Pham_ID AND b.Balance_Date<=@d AND b.IsValid=1)
AND (k.Ten_Kho<@warehouseName OR (k.Ten_Kho=@warehouseName AND p.Ma_San_Pham<@productCode));",
            q=>{Param(q,"@login",System.Data.SqlDbType.NVarChar,Login,100);Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@d",System.Data.SqlDbType.Date,date.Date);Param(q,"@warehouseName",System.Data.SqlDbType.NVarChar,target["Ten_Kho"],200);Param(q,"@productCode",System.Data.SqlDbType.NVarChar,target["Ma_San_Pham"],100);}));
        const int size=50;var page=Reports.sp_BC_Xuat_Nhap_Ton_Page(date,date,checked((int)(rank/size)+1),size,Login,Warehouse);
        var row=page.Items.SingleOrDefault(x=>x.Kho_ID==Warehouse&&x.San_Pham_ID==Product);
        Need(row is not null&&page.Total_Count>0&&row.SL_Nhap==received&&row.SL_Xuat==issued&&row.SL_Cuoi_Ky==closing&&row.SL_Dau_Ky+row.SL_Nhap-row.SL_Xuat==row.SL_Cuoi_Ky,"Historical report app path/oracle mismatch.");
        return new{Status="PASS",Mode="HISTORICAL",Date=date.Date,PageSize=size,TotalCount=page.Total_Count,Row=new{row!.Kho_ID,row.San_Pham_ID,row.SL_Dau_Ky,row.SL_Nhap,row.SL_Xuat,row.SL_Cuoi_Ky}};
    }

    private static async Task FinalizeAsync(Evidence pre)
    {
        var post=await CaptureAsync();var a=pre.Scope;var b=post.Scope;
        var afterIssue=(Dictionary<string,object?>)State["AfterIssuePost"]!;RequireNoSnapshotMutation(afterIssue,b,"Snapshot state changed after issue POST validation.");
        Need(D(b,"CurrentQuantity")==D(a,"CurrentQuantity")&&D(b,"BalanceReservedQuantity")==0m&&L(b,"ReservationRowsScope")==0,"Final balance/reservation mismatch.");
        Need(D(b,"TodayMovementReceipt")==D(a,"TodayMovementReceipt")+.001m&&D(b,"TodayMovementIssue")==D(a,"TodayMovementIssue")+.001m,"Daily movement deltas mismatch.");
        Need(L(b,"MovementDailyRows")==L(b,"ValidMovementDailyRows")&&L(b,"BalanceDailyRows")==L(b,"ValidBalanceDailyRows"),"Daily aggregates contain invalid rows.");
        Need(L(b,"MovementQueueRowsGlobal")==L(a,"MovementQueueRowsGlobal")+2&&L(b,"MovementQueueActiveGlobal")==0&&L(b,"MovementDeadLettersGlobal")==0&&L(b,"SnapshotDeadLettersGlobal")==0,"Queue/drain/global movement record invariant failed.");
        Need(I(post.Worker,"JobEnabled")==0&&I(post.Worker,"ScheduleEnabled")==0&&L(post.Worker,"ExecutionHistoryCount")==L(pre.Worker,"ExecutionHistoryCount"),"Worker final state/history changed.");
        Need(T(post.ExistingJob,"JobName")==T(pre.ExistingJob,"JobName")&&I(post.ExistingJob,"JobEnabled")==I(pre.ExistingJob,"JobEnabled")&&T(post.ExistingJob,"StepDatabase")==T(pre.ExistingJob,"StepDatabase")&&T(post.ExistingJob,"StepCommand")==T(pre.ExistingJob,"StepCommand")&&I(post.ExistingJob,"ScheduleEnabled")==I(pre.ExistingJob,"ScheduleEnabled")&&L(post.ExistingJob,"ExecutionHistoryCount")==L(pre.ExistingJob,"ExecutionHistoryCount"),"Existing GiaiDoan2 job state/history changed.");
        var rid=L(State,"PostedReceiptDocumentId");var iid=L(State,"PostedIssueDocumentId");
        var rcp=await VerifyPosted(true,rid,T(State,"ReceiptPostDocumentNumber"));var iss=await VerifyPosted(false,iid,T(State,"IssuePostDocumentNumber"));
        WriteNew("Q2-FinalState.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2-final-state/2",ObservedUtc=DateTimeOffset.UtcNow,Q2Status="PASS",FinalQ2StateCaptured=true,Batch4QualificationRunId=_runId,PreState=a,PostState=b,PostedReceipt=rcp,PostedIssue=iss,Worker=post.Worker,ExistingGiaiDoan2JobUnchanged=true,ExpectedNetCurrentDelta=0m,ObservedNetCurrentDelta=D(b,"CurrentQuantity")-D(a,"CurrentQuantity"),CanonicalWorkloadStarted=false});
        var afterReceipt=(Dictionary<string,object?>)State["AfterReceiptPost"]!;
        WriteNew("Batch4-Q2-Snapshot-Guard-Trace.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2-snapshot-guard-trace/1",ObservedUtc=DateTimeOffset.UtcNow,Batch4QualificationRunId=_runId,Transitions=State["SnapshotGuardTransitions"],ReceiptWorkerPrecondition=afterReceipt["Q2RSnapshotQueueId4RequestedVersion"],IssueWorkerPrecondition=afterIssue["Q2RSnapshotQueueId4RequestedVersion"],FinalSnapshotState=b});
        WriteNew("Q2-Reconciliation.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2-reconciliation/2",ObservedUtc=DateTimeOffset.UtcNow,Status="PASS",Batch4QualificationRunId=_runId,ReceiptExpectedDelta=.001m,ReceiptObservedDelta=D(afterReceipt,"CurrentQuantity")-D(a,"CurrentQuantity"),IssueExpectedDelta=-.001m,IssueObservedDelta=D(b,"CurrentQuantity")-D(afterReceipt,"CurrentQuantity"),NetExpectedDelta=0m,NetObservedDelta=D(b,"CurrentQuantity")-D(a,"CurrentQuantity"),MovementReceiptDelta=D(b,"TodayMovementReceipt")-D(a,"TodayMovementReceipt"),MovementIssueDelta=D(b,"TodayMovementIssue")-D(a,"TodayMovementIssue"),MovementDailyValid=true,BalanceDailyValid=true,ReservationReleased=true,QueuesDrained=true,DeadLetters=0,ExistingGiaiDoan2JobUnchanged=true,BoundedDbPrePost="PASS"});
        WriteNew("Q2-Write-Inventory.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2-write-inventory/2",ObservedUtc=DateTimeOffset.UtcNow,Status="PASS",Batch4QualificationRunId=_runId,ApplicationWorkloadIdentity=Login,ApplicationWritesExecuted=15,PostedDocuments=new[]{rcp,iss},DraftDocuments=new[]{new{DocumentNumber=No("RCPT-DRAFT-01"),Cleanup="APPLICATION_DELETE_DETAIL_THEN_HEADER"},new{DocumentNumber=No("ISSUE-DRAFT-01"),Cleanup="APPLICATION_DELETE_DETAIL_THEN_HEADER_AND_RESERVATION_RELEASE"}},Q2Operations=new{ReceiptDraftCrud="PASS",IssueReservationLifecycle="PASS",ReceiptPost="PASS",ReceiptWorkerDrain="PASS",ReceiptReadAfterWrite="PASS",IssuePost="PASS",IssueWorkerDrain="PASS",IssueReadAfterWrite="PASS",FinalReconciliation="PASS"}});
        WriteNew("Q2-Worker-Lifecycle.json",new{SchemaVersion="warehouse-benchmark-v22-batch4-q2-worker-lifecycle/3",ObservedUtc=DateTimeOffset.UtcNow,Status="PASS_DIRECT_PROCEDURE_ROUTE_AGENT_UNCHANGED",WorkerRoute="PROC_IS_WORKER_AGENT_IS_SCHEDULER",Procedure="dbo.sp_Inventory_Movement_Process_RebuildQueue",InvocationCount=2,AgentStarted=false,DedicatedJobExecutionCountBefore=L(pre.Worker,"ExecutionHistoryCount"),DedicatedJobExecutionCountAfter=L(post.Worker,"ExecutionHistoryCount"),JobId=PerfJob,JobName=post.Worker["JobName"],JobEnabled=false,ScheduleEnabled=false,ReceiptDrain=State["ReceiptWorker"],IssueDrain=State["IssueWorker"],DeadLetters=0,ExistingGiaiDoan2JobUnchanged=true,ExistingGiaiDoan2ExecutionHistoryBefore=L(pre.ExistingJob,"ExecutionHistoryCount"),ExistingGiaiDoan2ExecutionHistoryAfter=L(post.ExistingJob,"ExecutionHistoryCount"),AgentServiceStateCapturedByWrapper=true});
        WriteNew("Q2-Gate.json",new{ObservedUtc=DateTimeOffset.UtcNow,Batch4QualificationRunId=_runId,BATCH4_WORKLOAD_CONTRACT_READY="YES",BATCH4_WRITE_CORRECTNESS_READY="YES",BATCH4_HARNESS_QUALIFIED="NO",BATCH4_CANONICAL_READY="NO",BATCH4_STARTED_CANONICAL="NO",CanonicalWorkloadStarted=false,Reason="Q2 bounded functional write correctness passed; no timed or canonical workload run."});
    }
    private static async Task<object> VerifyPosted(bool receipt,long id,string no)
    {
        var page=await Docs.List_Documents_Page_Async(receipt,1,10,no,Login,Warehouse);
        Need(page.Total_Count==1&&page.Items.Count==1&&page.Items[0].Auto_ID==id&&page.Items[0].Is_Posted,"Posted document not verified through app path.");
        var ds=await Docs.List_Document_Details_Async(receipt,id,Login);
        Need(ds.Count==1&&ds[0].San_Pham_ID==Product&&ds[0].So_Luong==.001m,"Posted details differ from frozen payload.");
        return new{DocumentId=id,DocumentNumber=no,IsReceipt=receipt,IsPosted=true,DetailId=ds[0].Auto_ID,ProductId=ds[0].San_Pham_ID,Quantity=ds[0].So_Luong};
    }

    internal static async Task Step(string name,Func<Task> f)
    {
        Event("STEP_STARTED",new{Name=name});Console.WriteLine("START "+name);
        try{await f();Event("STEP_PASS",new{Name=name});Console.WriteLine("PASS "+name);Persist("Q2_IN_PROGRESS");}
        catch(Exception e){Event("STEP_FAIL",new{Name=name,Error=Safe(e)});Console.Error.WriteLine("FAIL "+name+": "+Safe(e));throw new InvalidOperationException("First Q2 failure at "+name+": "+Safe(e),e);}
    }
    private static async Task CleanupDraftAsync()
    {
        try
        {
            if(_draftDetail is long d){Event("DRAFT_CLEANUP_INTENT",new{DetailId=d,Receipt=_draftReceipt});await Docs.Delete_Document_Detail_Async(_draftReceipt,d,Login,"WHB22-B4-Q2",Login);_draftDetail=null;}
            if(_draftHeader is long h){Event("DRAFT_CLEANUP_INTENT",new{HeaderId=h,Receipt=_draftReceipt});await Docs.Delete_Document_Async(_draftReceipt,h,Login,"WHB22-B4-Q2",Login);_draftHeader=null;}
            Event("DRAFT_CLEANUP",new{Status="PASS_OR_NOT_NEEDED"});
        }
        catch(Exception e){Event("DRAFT_CLEANUP",new{Status="FAIL",Error=Safe(e)});State["DraftCleanupStatus"]="FAIL";Persist("Q2_PARTIAL_STOPPED_ON_FAILURE");}
    }
    private static async Task<Dictionary<string,object?>> Scope()
    {await using var c=new SqlConnection(_cs);await c.OpenAsync();return await One(c,ScopeSql,q=>{Param(q,"@w",System.Data.SqlDbType.BigInt,Warehouse);Param(q,"@p",System.Data.SqlDbType.BigInt,Product);});}
    private static async Task<Dictionary<string,object?>> Job(SqlConnection c,Guid id)
    {
        var rows=await Rows(c,JobSql,q=>Param(q,"@id",System.Data.SqlDbType.UniqueIdentifier,id));
        if(rows.Count==0)throw new InvalidDataException("Expected SQL Agent job is absent.");return rows[0];
    }
    private static async Task<Dictionary<string,object?>> One(SqlConnection c,string sql,Action<SqlCommand> bind)
    {var rows=await Rows(c,sql,bind);if(rows.Count==0)throw new InvalidDataException("Read-only SQL returned no row.");return rows[0];}
    private static async Task<List<Dictionary<string,object?>>> Rows(SqlConnection c,string sql,Action<SqlCommand> bind)
    {
        await using var q=c.CreateCommand();q.CommandText=sql;q.CommandTimeout=60;bind(q);var list=new List<Dictionary<string,object?>>();
        await using var r=await q.ExecuteReaderAsync();while(await r.ReadAsync()){var d=new Dictionary<string,object?>(StringComparer.Ordinal);for(int k=0;k<r.FieldCount;k++)d[r.GetName(k)]=r.IsDBNull(k)?null:r.GetValue(k);list.Add(d);}return list;
    }
    private static async Task<object?> Scalar(SqlConnection c,string sql,Action<SqlCommand>? bind=null)
    {await using var q=c.CreateCommand();q.CommandText=sql;q.CommandTimeout=60;bind?.Invoke(q);return await q.ExecuteScalarAsync();}
    private static void Param(SqlCommand q,string name,System.Data.SqlDbType type,object? value,int size=0)
    {var p=size>0?q.Parameters.Add(name,type,size):q.Parameters.Add(name,type);p.Value=value??DBNull.Value;}
    private static string T(Dictionary<string,object?> x,string k)=>Convert.ToString(x[k],CultureInfo.InvariantCulture)??"";
    private static int I(Dictionary<string,object?> x,string k)=>Convert.ToInt32(x[k],CultureInfo.InvariantCulture);
    private static long L(Dictionary<string,object?> x,string k)=>Convert.ToInt64(x[k],CultureInfo.InvariantCulture);
    private static decimal D(Dictionary<string,object?> x,string k)=>Convert.ToDecimal(x[k],CultureInfo.InvariantCulture);
    private static bool B(Dictionary<string,object?> x,string k)=>Convert.ToBoolean(x[k],CultureInfo.InvariantCulture);
    private static async Task<object> VerifyDuplicatePost(bool receipt,long id,string direction,string documentNumber,string app)
    {
        Event("DUPLICATE_POST_INTENT",new{DocumentId=id,DocumentNumber=documentNumber,Direction=direction,ExpectedSqlErrorNumber=51162});
        try { await Docs.Post_Document_Async(receipt,id,Login,app,Login); }
        catch(SqlException ex) when(ex.Number==51162)
        {
            State[direction+"DuplicatePostStatus"]="PASS_REJECTED_ALREADY_POSTED";
            Event("DUPLICATE_POST_REJECTED",new{DocumentId=id,DocumentNumber=documentNumber,Direction=direction,SqlErrorNumber=ex.Number,ExpectedSqlErrorNumber=51162});
            return new{Status="PASS_REJECTED_ALREADY_POSTED",DocumentId=id,DocumentNumber=documentNumber,Direction=direction,SqlErrorNumber=ex.Number,DataAccessRoute="CWarehouseDocumentSupport_Controller.sp_XNK_Document_Post"};
        }
        throw new InvalidDataException(direction+" duplicate post unexpectedly returned success.");
    }
    private static bool SameScope(Dictionary<string,object?> before,Dictionary<string,object?> after)
    {
        if(before.Count!=after.Count)return false;
        foreach(var item in before)if(!after.TryGetValue(item.Key,out var value)||!Equals(item.Value,value))return false;
        return true;
    }
    private static readonly string[] SnapshotStateKeys={"SnapshotQueueRowsGlobal","SnapshotQueueActiveGlobal","SnapshotDeadLettersGlobal","SnapshotDeadLettersScope","SnapshotRowsScope","CompletedBootstrapAuditEligibleForScope","Q2RSnapshotQueueId4Rows","Q2RSnapshotQueueId4Status","Q2RSnapshotQueueId4AttemptCount","Q2RSnapshotQueueId4RequestType","Q2RSnapshotQueueId4LifecycleStatus","Q2RSnapshotQueueId4WarehouseId","Q2RSnapshotQueueId4ProductId","Q2RSnapshotQueueId4FromDate","Q2RSnapshotQueueId4RequestedVersion","Q2RSnapshotQueueId4ClaimedVersion","Q2RSnapshotQueueId4ClaimedBy","Q2RSnapshotQueueId4ClaimedAt","Q2RSnapshotQueueId4LeaseUntil","Q2RSnapshotQueueId4NextAttemptAt","Q2RSnapshotQueueId4LastAttemptAt","Q2RSnapshotQueueId4CompletedAt","Q2RSnapshotQueueId4ErrorMessage","Q2RSnapshotQueueId4LastError","Q2RSnapshotQueueId4CreatedAt"};

    internal static bool SameSnapshotQueueState(Dictionary<string,object?> before,Dictionary<string,object?> after)
    {
        foreach(var key in SnapshotStateKeys)if(!before.TryGetValue(key,out var oldValue)||!after.TryGetValue(key,out var newValue)||!Equals(oldValue,newValue))return false;
        return true;
    }

    internal static void ValidateExpectedSnapshotTransition(Dictionary<string,object?> before,Dictionary<string,object?> after,string operation)
    {
        Need(operation is "RECEIPT_POST" or "ISSUE_POST","Unsupported snapshot invalidation operation.");
        Need(SnapshotStateKeys.All(k=>before.ContainsKey(k)&&after.ContainsKey(k)),"Snapshot transition evidence is incomplete.");
        string[] stableKeys={"SnapshotQueueRowsGlobal","SnapshotQueueActiveGlobal","SnapshotDeadLettersGlobal","SnapshotDeadLettersScope","SnapshotRowsScope","CompletedBootstrapAuditEligibleForScope","Q2RSnapshotQueueId4Rows","Q2RSnapshotQueueId4WarehouseId","Q2RSnapshotQueueId4ProductId","Q2RSnapshotQueueId4FromDate","Q2RSnapshotQueueId4CreatedAt","Q2RSnapshotQueueId4LastAttemptAt"};
        foreach(var key in stableKeys)Need(Equals(before[key],after[key]),"Snapshot scope/global invariant changed during expected POST transition: "+key);
        Need(L(before,"SnapshotQueueRowsGlobal")==1&&L(before,"SnapshotQueueActiveGlobal")==1&&L(before,"SnapshotDeadLettersGlobal")==0&&L(before,"SnapshotDeadLettersScope")==0&&L(before,"SnapshotRowsScope")==0&&L(before,"CompletedBootstrapAuditEligibleForScope")==0,"Snapshot PRE state is not the expected pending initialization contract.");
        Need(L(before,"Q2RSnapshotQueueId4Rows")==1&&L(before,"Q2RSnapshotQueueId4WarehouseId")==Warehouse&&L(before,"Q2RSnapshotQueueId4ProductId")==Product&&T(before,"Q2RSnapshotQueueId4Status")=="WAITING"&&L(before,"Q2RSnapshotQueueId4AttemptCount")==0&&T(before,"Q2RSnapshotQueueId4RequestType")=="INITIALIZE"&&T(before,"Q2RSnapshotQueueId4LifecycleStatus")=="INITIALIZE_REQUIRED"&&L(before,"Q2RSnapshotQueueId4RequestedVersion")>0&&before["Q2RSnapshotQueueId4ClaimedVersion"] is null&&before["Q2RSnapshotQueueId4ClaimedBy"] is null&&before["Q2RSnapshotQueueId4ClaimedAt"] is null&&before["Q2RSnapshotQueueId4LeaseUntil"] is null,"Snapshot PRE queue row is not the expected unclaimed INITIALIZE_REQUIRED row.");
        Need(L(after,"Q2RSnapshotQueueId4Rows")==1&&T(after,"Q2RSnapshotQueueId4Status")=="WAITING"&&L(after,"Q2RSnapshotQueueId4AttemptCount")==0&&T(after,"Q2RSnapshotQueueId4RequestType")=="INITIALIZE"&&T(after,"Q2RSnapshotQueueId4LifecycleStatus")=="INITIALIZE_REQUIRED"&&L(after,"Q2RSnapshotQueueId4WarehouseId")==Warehouse&&L(after,"Q2RSnapshotQueueId4ProductId")==Product&&T(after,"Q2RSnapshotQueueId4FromDate")==T(before,"Q2RSnapshotQueueId4FromDate"),"Snapshot POST state differs from source-expected INITIALIZE_REQUIRED transition.");
        Need(L(before,"Q2RSnapshotQueueId4RequestedVersion")<long.MaxValue&&L(after,"Q2RSnapshotQueueId4RequestedVersion")==L(before,"Q2RSnapshotQueueId4RequestedVersion")+1,"Snapshot POST version must advance exactly once.");
        string[] clearedKeys={"Q2RSnapshotQueueId4ClaimedVersion","Q2RSnapshotQueueId4ClaimedBy","Q2RSnapshotQueueId4ClaimedAt","Q2RSnapshotQueueId4LeaseUntil","Q2RSnapshotQueueId4NextAttemptAt","Q2RSnapshotQueueId4CompletedAt","Q2RSnapshotQueueId4ErrorMessage","Q2RSnapshotQueueId4LastError"};
        foreach(var key in clearedKeys)Need(after[key] is null,"Snapshot POST did not clear expected unclaimed/completion/error state: "+key);
    }

    internal static void RequireNoSnapshotMutation(Dictionary<string,object?> stable,Dictionary<string,object?> current,string message)
        =>Need(SameSnapshotQueueState(stable,current),message);

    internal static async Task InvokeMovementWorkerAfterSnapshotGateAsync(Dictionary<string,object?> stable,Dictionary<string,object?> current,Func<Task> invoke)
    {
        RequireNoSnapshotMutation(stable,current,"Snapshot queue changed before direct movement worker call.");
        await invoke();
    }

    private static object SnapshotTransitionEvidence(string operation,Dictionary<string,object?> before,Dictionary<string,object?> after)
        =>new{Operation=operation,VersionBefore=before["Q2RSnapshotQueueId4RequestedVersion"],VersionAfter=after["Q2RSnapshotQueueId4RequestedVersion"],ExpectedVersionDelta=1,ExactSourceTransition="PASS",Before=SnapshotStateKeys.ToDictionary(k=>k,k=>before[k]),After=SnapshotStateKeys.ToDictionary(k=>k,k=>after[k])};

    private static void RecordSnapshotTransition(string operation,Dictionary<string,object?> before,Dictionary<string,object?> after)
    {
        var evidence=SnapshotTransitionEvidence(operation,before,after);
        ((List<object>)State["SnapshotGuardTransitions"]!).Add(evidence);
        Event("SNAPSHOT_EXPECTED_TRANSITION_PASS",evidence);
    }
    private static void Need(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    private static string Safe(Exception e)=>string.IsNullOrEmpty(_cs)?e.ToString():e.ToString().Replace(_cs,"[REDACTED_CONNECTION_STRING]",StringComparison.OrdinalIgnoreCase);
    private static void WriteNew(string name,object value)
    {var p=Path.Combine(_root,name);if(File.Exists(p))throw new IOException("Refusing to overwrite "+name);Directory.CreateDirectory(_root);File.WriteAllText(p,JsonSerializer.Serialize(value,JsonOptions)+Environment.NewLine);}
    private static void WriteNewDurable(string name,object value)
    {
        var p=Path.Combine(_root,name);Directory.CreateDirectory(_root);
        var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,JsonOptions)+Environment.NewLine);
        using var stream=new FileStream(p,FileMode.CreateNew,FileAccess.Write,FileShare.None);stream.Write(bytes);stream.Flush(true);
    }
    private static void Persist(string status)
    {State["Status"]=status;State["ObservedUtc"]=DateTimeOffset.UtcNow;var p=Path.Combine(_root,"Q2-Execution-State.json");var t=p+".tmp";File.WriteAllText(t,JsonSerializer.Serialize(State,JsonOptions)+Environment.NewLine);File.Move(t,p,true);}
    private static void Event(string type,object data)=>File.AppendAllText(_journal,JsonSerializer.Serialize(new{ObservedUtc=DateTimeOffset.UtcNow,Event=type,Data=data})+Environment.NewLine);
}
