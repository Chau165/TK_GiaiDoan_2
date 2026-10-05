using System.Security.Cryptography;
using System.Text.Json;

internal static class Q2SnapshotGuardRegressionTests
{
    private static int _passed;
    private static int _failed;

    public static int Run()
    {
        Case("Receipt POST accepts exact N->N+1 transition and freezes post state", () => ValidPostTransition("RECEIPT_POST", 17));
        Case("Issue POST accepts exact N->N+1 transition and freezes post state", () => ValidPostTransition("ISSUE_POST", 2));
        Reject("unexpected version jump N->N+2", (b,a) => a["Q2RSnapshotQueueId4RequestedVersion"] = 4L);
        Reject("version regression", (b,a) => a["Q2RSnapshotQueueId4RequestedVersion"] = 1L);
        Reject("wrong WarehouseId", (b,a) => a["Q2RSnapshotQueueId4WarehouseId"] = 5L);
        Reject("wrong ProductId", (b,a) => a["Q2RSnapshotQueueId4ProductId"] = 41L);
        Reject("wrong RequestType", (b,a) => a["Q2RSnapshotQueueId4RequestType"] = "REBUILD");
        Reject("wrong LifecycleStatus", (b,a) => a["Q2RSnapshotQueueId4LifecycleStatus"] = "WAITING");
        Reject("wrong Status", (b,a) => a["Q2RSnapshotQueueId4Status"] = "PROCESSING");
        Reject("unexpected claimed version", (b,a) => a["Q2RSnapshotQueueId4ClaimedVersion"] = 3L);
        Reject("unexpected claimed owner", (b,a) => a["Q2RSnapshotQueueId4ClaimedBy"] = "external-worker");
        Reject("unexpected lease", (b,a) => a["Q2RSnapshotQueueId4LeaseUntil"] = DateTime.UtcNow);
        Reject("unexpected extra queue row", (b,a) => a["SnapshotQueueRowsGlobal"] = 2L);
        Reject("unexpected active queue count", (b,a) => a["SnapshotQueueActiveGlobal"] = 2L);
        Reject("unexpected scoped snapshot rows", (b,a) => a["SnapshotRowsScope"] = 1L);
        Reject("unexpected bootstrap audit eligibility", (b,a) => a["CompletedBootstrapAuditEligibleForScope"] = 1L);
        Reject("changed queue FromDate", (b,a) => a["Q2RSnapshotQueueId4FromDate"] = "2026-10-05");
        Reject("changed CreatedAt", (b,a) => a["Q2RSnapshotQueueId4CreatedAt"] = new DateTime(2026,10,5));
        Reject("unexplained error state", (b,a) => a["Q2RSnapshotQueueId4LastError"] = "unexpected");
        Case("valid stable post state reaches the direct worker invocation point", WorkerInvokedAfterValidGate);
        Case("external 2->3 mutation after accepted post prevents worker invocation", WorkerFailClosedAfterPost);
        Case("external mutation with unchanged version still prevents worker invocation", WorkerFailClosedOnStateMutation);
        Case("movement queue version is independent from snapshot guard", MovementVersionIsIndependent);
        Case("new execute claim is consumed once and cannot be replayed", OneShotClaimConsumedOnce);
        Case("recovery connection validation failure leaves claim unconsumed", RecoveryConnectionFailureDoesNotConsumeClaim);
        Case("recovery capture timeout leaves claim unconsumed", RecoveryCaptureTimeoutDoesNotConsumeClaim);
        Case("recovery comparator mismatch leaves claim unconsumed", RecoveryComparatorMismatchDoesNotConsumeClaim);
        Case("recovery preflight pass consumes claim before exactly one worker invocation", RecoveryClaimConsumedAtWorkerBoundary);
        Case("crash after claim consumption keeps retry forbidden and rejects replay", RecoveryCrashAfterClaimConsumptionRejectsReplay);
        Case("first runtime failure prevents later Q2 operations", FirstFailureStopsLaterOperations);
        Case("first failure persists partial state and failure journal", FirstFailurePersistsPartialState);
        Case("recovery queue date follows the retained posted receipt date", RecoveryQueueDateFollowsReceipt);
        Case("recovery comparator accepts exact semantic state", RecoveryComparatorExactState);
        Case("recovery comparator reports changed Scope field", () => RecoveryComparatorReportsField("Scope", "CurrentQuantity", 70000.002m, 70000.003m));
        Case("recovery comparator reports changed Identity field", () => RecoveryComparatorReportsField("Identity", "DatabaseName", "TKS_Thuc_Tap_V11_Perf_10000000", "OtherDatabase"));
        Case("recovery comparator reports changed Worker field", () => RecoveryComparatorReportsField("Worker", "JobEnabled", 0, 1));
        Case("recovery comparator reports changed ExistingJob field", () => RecoveryComparatorReportsField("ExistingJob", "ScheduleEnabled", 1, 0));
        Case("recovery comparator keeps nullable queue fields null", RecoveryComparatorNullableFields);
        Case("recovery comparator rejects null-to-sentinel queue change", RecoveryComparatorNullSentinelFails);
        Case("recovery comparator rejects unexpected snapshot mutation", () => RecoveryComparatorReportsField("Scope", "Q2RSnapshotQueueId4RequestedVersion", 2L, 3L));
        Case("recovery comparator treats Int32 and Int64 semantic value as equal", () => RecoveryComparatorNumericEquivalent(1, 1L));
        Case("recovery comparator treats integer and decimal semantic value as equal", () => RecoveryComparatorNumericEquivalent(1L, 1m));
        Case("recovery comparator accepts SQL smallint System.Int16", () => RecoveryComparatorNumericEquivalent((short)5, 5));
        Case("recovery comparator accepts SQL tinyint System.Byte", () => RecoveryComparatorNumericEquivalent((byte)0, 0));
        Case("recovery comparator rejects unequal System.Int16", () => RecoveryComparatorRejectsNumeric((short)5, 6));
        Case("recovery comparator rejects unequal System.Byte", () => RecoveryComparatorRejectsNumeric((byte)0, 1));
        Case("recovery comparator treats decimal scale as equal without floating point", () => RecoveryComparatorNumericEquivalent(70000.002m, 70000.0020m));
        Case("recovery comparator preserves decimal value differences", () => RecoveryComparatorReportsField("Scope", "CurrentQuantity", 70000.002m, 70000.003m));
        Case("recovery comparator DateTime equality ignores Kind with equal ticks", RecoveryComparatorDateTimeKind);
        Case("recovery comparator rejects DateTime precision difference", RecoveryComparatorDateTimePrecision);
        Case("recovery comparator is independent of JSON property order", RecoveryComparatorPropertyOrder);
        Case("recovery comparator identifies a missing field", RecoveryComparatorMissingField);
        Case("recovery comparator rejects DBNull and sentinel as null", RecoveryComparatorRejectsNonNullRepresentations);
        Case("recovery comparator failure evidence persists all domains and exact diff", RecoveryComparatorFailurePersistsDetails);

        Console.WriteLine($"Q2 snapshot guard regression: {_passed}/{_passed+_failed} PASS, {_failed} FAIL.");
        return _failed == 0 ? 0 : 1;
    }

    private static void ValidPostTransition(string operation,long beforeVersion)
    {
        var before=SnapshotState(beforeVersion);var after=SnapshotState(beforeVersion+1);
        Program.ValidateExpectedSnapshotTransition(before,after,operation);
        Program.RequireNoSnapshotMutation(after,SnapshotState(beforeVersion+1),"Expected stable worker precondition rejected.");
    }

    private static void Reject(string name,Action<Dictionary<string,object?>,Dictionary<string,object?>> mutate)
        =>Case(name,()=>
        {
            var before=SnapshotState(1);var after=SnapshotState(2);mutate(before,after);
            try{Program.ValidateExpectedSnapshotTransition(before,after,"RECEIPT_POST");}
            catch(InvalidDataException){return;}
            throw new InvalidOperationException("Source-invalid snapshot transition was accepted.");
        });

    private static void WorkerFailClosedAfterPost()
    {
        var acceptedPost=SnapshotState(2);var externallyChanged=SnapshotState(3);var invocations=0;
        try{Program.InvokeMovementWorkerAfterSnapshotGateAsync(acceptedPost,externallyChanged,()=>{invocations++;return Task.CompletedTask;}).GetAwaiter().GetResult();}
        catch(InvalidDataException){}
        Assert(invocations==0,"Worker invocation count must remain zero after an unexplained 2->3 transition.");
    }

    private static void WorkerFailClosedOnStateMutation()
    {
        var acceptedPost=SnapshotState(2);var externallyChanged=SnapshotState(2);externallyChanged["Q2RSnapshotQueueId4LifecycleStatus"]="WAITING";var invocations=0;
        try{Program.InvokeMovementWorkerAfterSnapshotGateAsync(acceptedPost,externallyChanged,()=>{invocations++;return Task.CompletedTask;}).GetAwaiter().GetResult();}
        catch(InvalidDataException){}
        Assert(invocations==0,"Worker invocation count must remain zero after a state mutation with unchanged version.");
    }

    private static void WorkerInvokedAfterValidGate()
    {
        var acceptedPost=SnapshotState(2);var current=SnapshotState(2);var invocations=0;
        Program.InvokeMovementWorkerAfterSnapshotGateAsync(acceptedPost,current,()=>{invocations++;return Task.CompletedTask;}).GetAwaiter().GetResult();
        Assert(invocations==1,"Valid stable post state must reach the worker invocation point exactly once.");
    }

    private static void MovementVersionIsIndependent()
    {
        var before=SnapshotState(1);var after=SnapshotState(2);
        before["MovementQueueRequestedVersion"]=1L;after["MovementQueueRequestedVersion"]=99L;
        Program.ValidateExpectedSnapshotTransition(before,after,"RECEIPT_POST");
    }

    private static void OneShotClaimConsumedOnce()
    {
        WithTempRoot((root,runId)=>
        {
            Program.ResetForOfflineRegression(root,runId);
            var prestatePath=Path.Combine(root,"Q2-PreState.json");
            File.WriteAllText(prestatePath,JsonSerializer.Serialize(new{Status="PASS_PREWRITE_GATE",Batch4QualificationRunId=runId}));
            var prestateHash=HashFile(prestatePath);var dllHash=Program.CurrentHarnessAssemblySha256();
            var claimPath=Path.Combine(root,"Batch4-Q2-New-OneShot-Claim.json");
            File.WriteAllText(claimPath,JsonSerializer.Serialize(new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2-one-shot-claim/1",RunId=runId,
                AttemptNumber=1,MaximumExecuteInvocations=1,AutomaticRetryAllowed=false,
                PrewriteReadinessPath=prestatePath,PrewriteReadinessSha256=prestateHash,HarnessDllSha256=dllHash
            }));
            var claimHash=HashFile(claimPath);
            var consumedHash=Program.ConsumeExecuteClaim(root,runId,dllHash);
            Assert(consumedHash==claimHash,"Consumed marker must bind the exact immutable claim hash.");
            Assert(HashFile(claimPath)==claimHash,"One-shot claim must remain immutable after consumption.");
            var markerPath=Path.Combine(root,"Batch4-Q2-Execute-Invocation-Consumed.json");
            Assert(File.Exists(markerPath),"Invocation marker must be durable before execution begins.");
            var rejected=false;
            try{Program.ConsumeExecuteClaim(root,runId,dllHash);}catch(IOException){rejected=true;}catch(InvalidDataException){rejected=true;}
            Assert(rejected,"A second execute invocation must be rejected.");
        });
    }

    private static void RecoveryConnectionFailureDoesNotConsumeClaim()
    {
        WithRecoveryClaimFixture((root,runId,dllHash)=>
        {
            var previous=Environment.GetEnvironmentVariable("TKS_V22_CONNECTION_STRING");
            try
            {
                Environment.SetEnvironmentVariable("TKS_V22_CONNECTION_STRING",null);
                var exit=Program.Main(new[]{"recovery-worker",root,runId}).GetAwaiter().GetResult();
                Assert(exit==3,"Missing connection must fail preflight without reaching worker invocation.");
                Assert(!File.Exists(Path.Combine(root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json")),"Connection validation failure must not consume the recovery claim.");
                Assert(!File.Exists(Path.Combine(root,"Batch4-Q2H-Recovery-Worker-Execute-Result.json")),"A pre-claim failure must not be persisted as a completed recovery attempt.");
                var preparations=Directory.GetFiles(root,"Batch4-Q2H-Recovery-Preparation-*.json");
                Assert(preparations.Length>=1&&JsonDocument.Parse(File.ReadAllText(preparations[^1])).RootElement.GetProperty("Status").GetString()=="PREPARED_NOT_CONSUMED","Connection validation failure must retain a non-consuming preparation artifact.");
                var failures=Directory.GetFiles(root,"Batch4-Q2H-Recovery-Preflight-Failure-*.json");
                Assert(failures.Length==1&&JsonDocument.Parse(File.ReadAllText(failures[0])).RootElement.GetProperty("Status").GetString()=="CONNECTION_VALIDATION_FAILED_NOT_CONSUMED","Connection validation failure must have a separate explicit pre-claim status.");
            }
            finally{Environment.SetEnvironmentVariable("TKS_V22_CONNECTION_STRING",previous);}
        });
    }

    private static void RecoveryCaptureTimeoutDoesNotConsumeClaim()
        =>RecoveryPreflightFailureDoesNotConsumeClaim("capture");

    private static void RecoveryComparatorMismatchDoesNotConsumeClaim()
        =>RecoveryPreflightFailureDoesNotConsumeClaim("comparator");

    private static void RecoveryPreflightFailureDoesNotConsumeClaim(string failingStage)
    {
        WithRecoveryClaimFixture((root,runId,dllHash)=>
        {
            var workerInvocations=0;var consumeCalls=0;
            Func<Task> Stage(string name)=>()=>
            {
                if(name==failingStage)
                    throw name=="capture"?new TimeoutException("injected read-only capture timeout"):new InvalidDataException("injected recovery comparator mismatch");
                return Task.CompletedTask;
            };
            var failed=false;
            try
            {
                Program.RunRecoveryInvocationWithClaimAsync<int>(
                    Stage("connection"),Stage("capture"),Stage("comparator"),
                    beforeMutation=>{beforeMutation();workerInvocations++;return Task.FromResult(workerInvocations);},
                    ()=>{},
                    ()=>{consumeCalls++;return Program.ConsumeRecoveryWorkerClaim(root,runId,dllHash);})
                    .GetAwaiter().GetResult();
            }
            catch(Exception){failed=true;}
            Assert(failed,"Injected preflight failure must stop the recovery path.");
            Assert(!File.Exists(Path.Combine(root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json")),"Capture/comparator failure must not consume the recovery claim.");
            Assert(consumeCalls==0&&workerInvocations==0,"No claim or worker invocation is allowed after a failed read-only preflight.");
        });
    }

    private static void RecoveryClaimConsumedAtWorkerBoundary()
    {
        WithRecoveryClaimFixture((root,runId,dllHash)=>
        {
            var order=new List<string>();var consumeCalls=0;var workerInvocations=0;
            var result=Program.RunRecoveryInvocationWithClaimAsync<int>(
                ()=>{order.Add("connection");return Task.CompletedTask;},
                ()=>{order.Add("capture");return Task.CompletedTask;},
                ()=>{order.Add("comparator");return Task.CompletedTask;},
                beforeMutation=>
                {
                    order.Add("worker-read-only-preflight");
                    beforeMutation();
                    order.Add("worker");workerInvocations++;
                    return Task.FromResult(workerInvocations);
                },
                ()=>order.Add("persist-pre-invocation"),
                ()=>{consumeCalls++;order.Add("consume");return Program.ConsumeRecoveryWorkerClaim(root,runId,dllHash);})
                .GetAwaiter().GetResult();
            Assert(result==1&&consumeCalls==1&&workerInvocations==1,"A passing preflight must consume once before one worker invocation.");
            Assert(order.SequenceEqual(new[]{"connection","capture","comparator","worker-read-only-preflight","persist-pre-invocation","consume","worker"}),"Claim boundary ordering must persist evidence and consume only after every read-only gate.");
            Assert(File.Exists(Path.Combine(root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json")),"Worker invocation requires the durable consumed marker.");
            Assert(workerInvocations==0||consumeCalls==1,"Worker invocation count must imply exactly one successful claim consumption.");
        });
    }

    private static void RecoveryCrashAfterClaimConsumptionRejectsReplay()
    {
        WithRecoveryClaimFixture((root,runId,dllHash)=>
        {
            var consumeCalls=0;var workerInvocations=0;
            try
            {
                Program.RunRecoveryInvocationWithClaimAsync<int>(
                    ()=>Task.CompletedTask,()=>Task.CompletedTask,()=>Task.CompletedTask,
                    beforeMutation=>{beforeMutation();throw new IOException("simulated process crash after durable claim consumption");},
                    ()=>{},
                    ()=>{var hash=Program.ConsumeRecoveryWorkerClaim(root,runId,dllHash);consumeCalls++;return hash;})
                    .GetAwaiter().GetResult();
            }
            catch(IOException){}
            var markerPath=Path.Combine(root,"Batch4-Q2H-Recovery-Worker-Invocation-Consumed.json");
            Assert(File.Exists(markerPath)&&consumeCalls==1&&workerInvocations==0,"Crash after consumption must leave the one-shot marker and forbid worker retry.");
            var replayRejected=false;
            try
            {
                Program.RunRecoveryInvocationWithClaimAsync<int>(
                    ()=>Task.CompletedTask,()=>Task.CompletedTask,()=>Task.CompletedTask,
                    beforeMutation=>{beforeMutation();workerInvocations++;return Task.FromResult(workerInvocations);},
                    ()=>{},
                    ()=>{var hash=Program.ConsumeRecoveryWorkerClaim(root,runId,dllHash);consumeCalls++;return hash;})
                    .GetAwaiter().GetResult();
            }
            catch(Exception){replayRejected=true;}
            Assert(replayRejected&&workerInvocations==0&&consumeCalls==1,"A second attempt with the consumed claim must be rejected before worker invocation.");
        });
    }

    private static void WithRecoveryClaimFixture(Action<string,string,string> action)
    {
        WithTempRoot((root,runId)=>
        {
            Program.ResetForOfflineRegression(root,runId);
            var prestatePath=Path.Combine(root,"Batch4-Q2H-Recovery-Prestate.json");
            File.WriteAllText(prestatePath,JsonSerializer.Serialize(new{Status="PASS_RECOVERY_PRESTATE",RecoveryRunId=runId}));
            var dllHash=Program.CurrentHarnessAssemblySha256();
            File.WriteAllText(Path.Combine(root,"Batch4-Q2H-Recovery-Worker-Claim.json"),JsonSerializer.Serialize(new
            {
                SchemaVersion="warehouse-benchmark-v22-batch4-q2h-recovery-worker-claim/1",RunId=runId,
                AttemptNumber=1,MaximumWorkerInvocations=1,AutomaticRetryAllowed=false,
                PrewriteReadinessPath=prestatePath,PrewriteReadinessSha256=HashFile(prestatePath),HarnessDllSha256=dllHash,
                ClaimCreationDoesNotInvokeWorker=true
            }));
            var claimSha256=Program.ValidateRecoveryWorkerClaim(root,runId,dllHash);
            Program.PrepareRecoveryWorkerAttempt(root,runId,claimSha256,dllHash);
            action(root,runId,dllHash);
        });
    }

    private static void FirstFailureStopsLaterOperations()
    {
        WithTempRoot((root,runId)=>
        {
            Program.ResetForOfflineRegression(root,runId);var first=0;var later=0;var handler=0;
            var originalError=Console.Error;Console.SetError(TextWriter.Null);
            int exit;
            try{exit=Program.ExecuteWithFirstFailureBoundaryAsync(async()=>
            {
                first++;
                await Program.Step("injected first failure",()=>Task.FromException(new InvalidOperationException("injected")));
                later++;
            },_=>{handler++;return Task.CompletedTask;}).GetAwaiter().GetResult();}
            finally{Console.SetError(originalError);}
            Assert(exit==1&&first==1&&later==0&&handler==1,"First failure must be handled once and stop the remaining Q2 sequence.");
        });
    }

    private static void FirstFailurePersistsPartialState()
    {
        WithTempRoot((root,runId)=>
        {
            Program.ResetForOfflineRegression(root,runId);var cleanup=0;var capture=0;
            var evidence=new Program.Evidence(new(StringComparer.Ordinal),new(StringComparer.Ordinal),new(StringComparer.Ordinal),new(StringComparer.Ordinal),new(StringComparer.Ordinal),"offline-test");
            var error=new InvalidOperationException("injected first runtime failure");
            Program.PersistFirstFailureAsync(error,()=>{cleanup++;return Task.CompletedTask;},()=>{capture++;return Task.FromResult(evidence);}).GetAwaiter().GetResult();
            var partial=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Q2-PartialState.json")));
            var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Q2-Execution-State.json")));
            var journal=File.ReadAllLines(Path.Combine(root,"Q2-Operation-Journal.jsonl"));
            Assert(partial.RootElement.GetProperty("Status").GetString()=="PARTIAL_STOPPED","Partial state status must be durable.");
            Assert(state.RootElement.GetProperty("Status").GetString()=="Q2_PARTIAL_STOPPED_ON_FAILURE","Execution state must persist first-failure status.");
            Assert(cleanup==1&&capture==1,"Failure handling must attempt cleanup and capture once.");
            Assert(journal.Any(x=>x.Contains("Q2_STOP_FIRST_FAILURE",StringComparison.Ordinal)),"Failure journal entry was not persisted.");
        });
    }

    private static void RecoveryQueueDateFollowsReceipt()
    {
        var scope=new Dictionary<string,object?>(StringComparer.Ordinal)
        {
            ["Q2HReceipt500007DocumentDate"]="2026-10-05",
            ["Q2HMovementQueueId11FromDate"]="2026-10-05",
            ["Q2HMovementQueueId11ToDate"]="2026-10-05"
        };
        Program.ValidateRecoveryQueueDateContract(scope);
        scope["Q2HMovementQueueId11FromDate"]="2026-10-04";
        var rejected=false;
        try{Program.ValidateRecoveryQueueDateContract(scope);}catch(InvalidDataException){rejected=true;}
        Assert(rejected,"A queue date not matching the immutable posted receipt date must fail closed.");
    }

    private static void RecoveryComparatorExactState()
    {
        var actual=new Dictionary<string,object?>(StringComparer.Ordinal)
        {
            ["CurrentQuantity"]=70000.002m,["BusinessDate"]=new DateTime(2026,10,5),
            ["ClaimedVersion"]=null,["ProcessedAt"]=null,["Status"]="WAITING"
        };
        using var expected=JsonDocument.Parse(JsonSerializer.Serialize(actual));
        var result=Program.CompareBaselineDetailed("Scope",actual,expected.RootElement);
        Assert(result.Matches&&result.Differences.Count==0,"Exact same scope must pass without differences.");
    }

    private static void RecoveryComparatorReportsField(string domain,string field,object? expectedValue,object? observedValue)
    {
        var expectedValues=new Dictionary<string,object?>(StringComparer.Ordinal){{field,expectedValue}};
        var actualValues=new Dictionary<string,object?>(StringComparer.Ordinal){{field,observedValue}};
        using var expected=JsonDocument.Parse(JsonSerializer.Serialize(expectedValues));
        var result=Program.CompareBaselineDetailed(domain,actualValues,expected.RootElement);
        Assert(!result.Matches&&result.Differences.Count>0,"Changed guarded value must fail closed.");
        var difference=result.Differences[0];
        Assert(difference.Domain==domain&&difference.FieldName==field,"Comparator must identify the exact domain and field.");
        Assert(!string.IsNullOrEmpty(difference.ExpectedRawValue)&&!string.IsNullOrEmpty(difference.ObservedRawValue),"Comparator must persist expected and observed raw values.");
        Assert(!string.IsNullOrEmpty(difference.ExpectedCLRType)&&!string.IsNullOrEmpty(difference.ObservedCLRType)&&!string.IsNullOrEmpty(difference.Reason),"Comparator must persist types and reason.");
    }

    private static void RecoveryComparatorNullableFields()
    {
        var actual=new Dictionary<string,object?>(StringComparer.Ordinal){{"ClaimedVersion",null},{"ProcessedAt",null}};
        using var expected=JsonDocument.Parse("{\"ClaimedVersion\":null,\"ProcessedAt\":null}");
        var result=Program.CompareBaselineDetailed("Scope",actual,expected.RootElement);
        Assert(result.Matches,"Stable SQL NULL projections must remain equal to null.");
    }

    private static void RecoveryComparatorNullSentinelFails()
    {
        var actual=new Dictionary<string,object?>(StringComparer.Ordinal){{"ClaimedVersion",null}};
        using var expected=JsonDocument.Parse("{\"ClaimedVersion\":-1}");
        var result=Program.CompareBaselineDetailed("Scope",actual,expected.RootElement);
        Assert(!result.Matches&&result.Differences[0].FieldName=="ClaimedVersion","NULL must not be normalized to the -1 projection sentinel.");
    }

    private static void RecoveryComparatorNumericEquivalent(object? actualValue,object expectedValue)
    {
        var actual=new Dictionary<string,object?>(StringComparer.Ordinal){{"Value",actualValue}};
        using var expected=JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string,object?>(StringComparer.Ordinal){{"Value",expectedValue}}));
        var result=Program.CompareBaselineDetailed("Scope",actual,expected.RootElement);
        Assert(result.Matches,"Typed numeric equality supported by the comparator contract must pass.");
    }

    private static void RecoveryComparatorRejectsNumeric(object? actualValue,object expectedValue)
    {
        var actual=new Dictionary<string,object?>(StringComparer.Ordinal){{"Value",actualValue}};
        using var expected=JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string,object?>(StringComparer.Ordinal){{"Value",expectedValue}}));
        var result=Program.CompareBaselineDetailed("Scope",actual,expected.RootElement);
        Assert(!result.Matches&&result.Differences[0].FieldName=="Value","Unequal checked integer values must fail closed with the exact field.");
    }

    private static void RecoveryComparatorDateTimeKind()
    {
        var actual=new Dictionary<string,object?>(StringComparer.Ordinal){{"ProcessedAt",new DateTime(2026,10,5,0,0,0,DateTimeKind.Unspecified)}};
        using var expected=JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string,object?>(StringComparer.Ordinal){{"ProcessedAt",new DateTime(2026,10,5,0,0,0,DateTimeKind.Utc)}}));
        var result=Program.CompareBaselineDetailed("Scope",actual,expected.RootElement);
        Assert(result.Matches,"Existing DateTime.Equals semantics compare ticks and ignore Kind.");
    }

    private static void RecoveryComparatorDateTimePrecision()
    {
        var actual=new Dictionary<string,object?>(StringComparer.Ordinal){{"ProcessedAt",new DateTime(2026,10,5,0,0,0,DateTimeKind.Unspecified)}};
        using var expected=JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string,object?>(StringComparer.Ordinal){{"ProcessedAt",new DateTime(2026,10,5,0,0,0,DateTimeKind.Unspecified).AddMilliseconds(1)}}));
        var result=Program.CompareBaselineDetailed("Scope",actual,expected.RootElement);
        Assert(!result.Matches&&result.Differences[0].FieldName=="ProcessedAt","DateTime precision differences must remain failures.");
    }

    private static void RecoveryComparatorPropertyOrder()
    {
        var actual=new Dictionary<string,object?>(StringComparer.Ordinal){{"First",1},{"Second",2L}};
        using var expected=JsonDocument.Parse("{\"Second\":2,\"First\":1}");
        var result=Program.CompareBaselineDetailed("Scope",actual,expected.RootElement);
        Assert(result.Matches,"JSON object property ordering must not change equality.");
    }

    private static void RecoveryComparatorMissingField()
    {
        var actual=new Dictionary<string,object?>(StringComparer.Ordinal){{"Present",1}};
        using var expected=JsonDocument.Parse("{\"Present\":1,\"Missing\":2}");
        var result=Program.CompareBaselineDetailed("Scope",actual,expected.RootElement);
        Assert(!result.Matches&&result.Differences.Any(x=>x.FieldName=="Missing"),"A missing compared property must be named in the failure.");
    }

    private static void RecoveryComparatorRejectsNonNullRepresentations()
    {
        using var expected=JsonDocument.Parse("{\"Value\":null}");
        var dbNull=Program.CompareBaselineDetailed("Scope",new Dictionary<string,object?>(StringComparer.Ordinal){{"Value",DBNull.Value}},expected.RootElement);
        var sentinel=Program.CompareBaselineDetailed("Scope",new Dictionary<string,object?>(StringComparer.Ordinal){{"Value",-1L}},expected.RootElement);
        Assert(!dbNull.Matches&&!sentinel.Matches,"DBNull and numeric sentinel must not be accepted as CLR null.");
    }

    private static void RecoveryComparatorFailurePersistsDetails()
    {
        WithTempRoot((root,runId)=>
        {
            var actual=new Program.Evidence(
                new(StringComparer.Ordinal){{"DatabaseName","PerfDb"}},
                new(StringComparer.Ordinal){{"CurrentQuantity",70000.003m}},
                new(StringComparer.Ordinal){{"JobEnabled",0}},
                new(StringComparer.Ordinal){{"ScheduleEnabled",1}},
                new(StringComparer.Ordinal),"offline-test");
            using var expected=JsonDocument.Parse("{\"Scope\":{\"CurrentQuantity\":70000.002},\"Identity\":{\"DatabaseName\":\"PerfDb\"},\"Worker\":{\"JobEnabled\":0},\"ExistingJob\":{\"ScheduleEnabled\":1}}");
            using var typeManifest=JsonDocument.Parse("{\"Scope\":{\"CurrentQuantity\":\"System.Decimal\"},\"Identity\":{\"DatabaseName\":\"System.String\"},\"Worker\":{\"JobEnabled\":\"System.Byte\"},\"ExistingJob\":{\"ScheduleEnabled\":\"System.Int32\"}}");
            var comparison=Program.CompareBaselineDetailed("Scope",actual.Scope,expected.RootElement.GetProperty("Scope"),typeManifest.RootElement.GetProperty("Scope"));
            Program.PersistRecoveryComparatorFailure(root,runId,expected.RootElement,actual,comparison.Differences[0],typeManifest.RootElement);
            using var persisted=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Batch4-Q2H-Recovery-Comparator-Failure.json")));
            var failure=persisted.RootElement.GetProperty("FailingComparison");
            Assert(failure.GetProperty("Domain").GetString()=="Scope"&&failure.GetProperty("FieldName").GetString()=="CurrentQuantity","Persisted diagnostic must name the rejecting domain and field.");
            Assert(failure.GetProperty("ExpectedRawValue").GetString()=="70000.002"&&failure.GetProperty("ObservedRawValue").GetString()=="70000.003","Persisted diagnostic must retain both values.");
            Assert(failure.GetProperty("ExpectedCLRType").GetString()==typeof(decimal).FullName&&failure.GetProperty("ObservedCLRType").GetString()==typeof(decimal).FullName,"Persisted diagnostic must retain expected and observed types.");
            Assert(!string.IsNullOrEmpty(failure.GetProperty("ComparisonRule").GetString())&&!string.IsNullOrEmpty(failure.GetProperty("Reason").GetString()),"Persisted diagnostic must retain the typed comparison rule and reason.");
            foreach(var name in new[]{"ScopeAtComparison","IdentityAtComparison","WorkerAtComparison","ExistingJobAtComparison"})
                Assert(persisted.RootElement.TryGetProperty(name,out _),"Persisted diagnostic is missing "+name+".");
        });
    }

    private static void WithTempRoot(Action<string,string> action)
    {
        var tempBase=Path.GetFullPath(Path.GetTempPath());var root=Path.Combine(tempBase,"WHB22-Q2H-regression-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try{action(root,"WHB22-B4-Q2H-REGRESSION-"+Guid.NewGuid().ToString("N"));}
        finally
        {
            var full=Path.GetFullPath(root);
            if(full.StartsWith(tempBase,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("WHB22-Q2H-regression-",StringComparison.Ordinal))Directory.Delete(full,true);
        }
    }

    private static string HashFile(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static Dictionary<string,object?> SnapshotState(long requestedVersion) => new(StringComparer.Ordinal)
    {
        ["SnapshotQueueRowsGlobal"]=1L,["SnapshotQueueActiveGlobal"]=1L,["SnapshotDeadLettersGlobal"]=0L,["SnapshotDeadLettersScope"]=0L,
        ["SnapshotRowsScope"]=0L,["CompletedBootstrapAuditEligibleForScope"]=0L,["Q2RSnapshotQueueId4Rows"]=1L,
        ["Q2RSnapshotQueueId4Status"]="WAITING",["Q2RSnapshotQueueId4AttemptCount"]=0L,["Q2RSnapshotQueueId4RequestType"]="INITIALIZE",
        ["Q2RSnapshotQueueId4LifecycleStatus"]="INITIALIZE_REQUIRED",["Q2RSnapshotQueueId4WarehouseId"]=4L,["Q2RSnapshotQueueId4ProductId"]=40L,
        ["Q2RSnapshotQueueId4FromDate"]="2026-10-04",["Q2RSnapshotQueueId4RequestedVersion"]=requestedVersion,["Q2RSnapshotQueueId4ClaimedVersion"]=null,
        ["Q2RSnapshotQueueId4ClaimedBy"]=null,["Q2RSnapshotQueueId4ClaimedAt"]=null,["Q2RSnapshotQueueId4LeaseUntil"]=null,
        ["Q2RSnapshotQueueId4NextAttemptAt"]=null,["Q2RSnapshotQueueId4LastAttemptAt"]=null,["Q2RSnapshotQueueId4CompletedAt"]=null,
        ["Q2RSnapshotQueueId4ErrorMessage"]=null,["Q2RSnapshotQueueId4LastError"]=null,["Q2RSnapshotQueueId4CreatedAt"]=new DateTime(2026,10,4)
    };

    private static void Case(string name,Action action)
    {
        try{action();_passed++;Console.WriteLine("PASS "+name);}
        catch(Exception ex){_failed++;Console.Error.WriteLine("FAIL "+name+": "+ex.Message);}
    }

    private static void Assert(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
}
