# Warehouse Benchmark protocol 2.2

Protocol identity: `warehouse-benchmark/2.2`. Candidate, runner names, manifest schema, tests, evidence and output roots use the v2.2 namespace. The 2.1 runners, candidate packages and historical results are read-only references.

Batch 1 established the isolated protocol and Batch 2 completed SQL-contract and semantic-correctness gates. Batch 3 adds explicit `LegacyIsolated`, `MixedRegression` and full Batch 3 execution through a separate performance entry point. Historical 2.1 runners and artifacts remain read-only comparators. Unsupported legacy switches, including `Bdn`, `Mixed`, `Resume`, `NoBuild` and `SoftWaitSeconds`, remain rejected by the 2.2 PowerShell entry points. Resume is unsupported; every run uses a new, nonexistent root.

A future read-only performance result cannot pass unless both dataset and Current fingerprints match before and after. BDN evidence needs a parseable summary, finite Mean/Error/StdDev with a recognized time unit, and at least one finite Actual measurement row; the measured row count comes from the artifact. NBomber metrics use the normalized schema enforced by the harness, with explicit units for request counts, RPS, latencies and configured/observed windows. Missing or malformed fields invalidate evidence.

Telemetry rows count only when Status is VALID and target database, run ID and block ID match exactly. TELEMETRY_ERROR rows never count. At least one valid target sample is the minimum evidence required for telemetry validity; this is not a statistical coverage threshold. Any telemetry error, rejected target, or zero valid sample marks telemetry invalid. A performance classification that requires telemetry must be blocked by invalid telemetry.

Post-integrity failure has precedence over validation success. Failed cooldown prevents the next block. Child and telemetry process IDs are tracked as `ChildProcessId`; cleanup runs after success, failure or exception. Fatal evidence is written before rethrowing a failure, with secrets redacted and cleanup outcome included.

The Batch 2 candidate remains `CorrectnessFoundationCandidate` and is not relabeled for performance. Batch 3 builds a separate performance candidate, runs exact-candidate bounded correctness before load, and records measurements as descriptive evidence. It does not publish a baseline or infer causality from a 2.1/2.2 delta. No extended CRUD/write, posting, reservation, worker or contention workload is included.

## Batch 3 measured profiles

The six legacy read scenarios run one at a time in historical order: BDN supplemental, then NBomber C1, C2 and C4 for each scenario. NBomber uses KeepConstant, 3 seconds warmup and a 15-second configured measurement. One copy is one worker. C1/C2 are the 12 core rows; C4 is additional isolated evidence. The BDN profile is InProcessNoEmit with launch=1, warmup=2, iterations=5, invocation=1 and unroll=1; its actual measured rows are read from the generated artifacts.

Mixed levels run six scenario-specific NBomber child processes concurrently: L1/L2/L4/L8 configure 1/2/4/8 copies per scenario, or 6/12/24/48 total workers. Each level requires at least 10 seconds of common measured-window overlap. L8 is standalone mixed evidence. Individual timed requests are not retried; failed block evidence is retained and any whole-block rerun requires a new identity.

Host admission follows the verified 2.1 policy: isolated blocks require two consecutive one-second samples with available RAM and predicted minimum above 640 MB, using the prior same-class peak drop; wait is capped at 60 seconds. Mixed admission predicts from the prior mixed-level drop using max(previous drop × 1.25, previous drop + 64 MB), and the predicted minimum must remain above 640 MB. All owned workloads hard-stop at or below 512 MB. Cooldown waits at least 5 seconds and at most 60 seconds for RAM above 640 MB, clean target-database residue and exited owned children. A failed admission/cooldown blocks the next block.

Telemetry must contain at least one valid row for the exact run, block and target database, and any error/rejected row invalidates that block's telemetry. Host CPU/RAM and target-database identity are recorded; SQL DMV counters whose sampling interpretation is not established are diagnostic only. Bounded PRE/POST correctness evidence does not prove full 10-million-row value equality. Historical DocumentPaged/DetailReportPaged timeout causes remain NOT VERIFIED. No regression threshold is defined unless a separate authoritative SLA is supplied.

## Canonical source inventory

The inventory root is the repository root. Include all source, scripts, tests and protocol documents under `benchmarks/v2.2`, plus evaluated MSBuild compile inputs and project files for the v2.2 benchmark project and its transitive project references. Include build-affecting props/targets/config files and content/resource inputs reported by MSBuild. Package references and project edges are recorded as project-input metadata and resolved package identities are captured in build attestation.

Each file entry records normalized repository-relative path, role, size, lowercase SHA-256, existence, and project/item relationship. The MSBuild evaluated item list is the authority for compile inputs: newly added compile files are discovered; a missing listed input remains an explicit `Exists=false` entry and blocks candidate build. The count is descriptive only; the ordered entry list and its canonical digest are authoritative.

Exclude `.git`, `.codegraph`, `bin`, `obj`, `.vs`, generated restore/build outputs, run evidence, logs, frozen runtime, package caches, and historical 2.1 trees from the source inventory. Generated compile inputs and restored packages are recorded in build attestation with their own hashes/identities. Normalize paths to Unicode NFC with `/` separators and repository-relative spelling; preserve actual path case while treating duplicate paths case-insensitively on Windows. Hash file bytes with SHA-256. Sort entries by ordinal normalized path before hashing the canonical tab-separated fields.

Dirty source is allowed only as an identified snapshot: capture Git commit, complete dirty/untracked path list and hashes; never commit, reset, stash or discard the user's changes. Build only from the copied source snapshot. Any source/build-input change after snapshot invalidates the candidate and requires a fresh snapshot and both builds.

Build A and Build B use separate clean artifact roots. The C# compiler path map maps each build root to the same virtual path and the source snapshot to a stable virtual source path, so workspace-specific `obj`/`bin` locations do not create false differences in deterministic PE/PDB output. The comma between mappings is passed to MSBuild as `%2C` so its property parser preserves the complete `PathMap` value. The exact path-map arguments are recorded in each build attestation command. All project outputs and generated compile-input hashes are still compared; any remaining difference blocks runtime freeze.

Runtime freeze writes one row per copied file with source and frozen SHA-256 values; its log hash is linked by the candidate manifest and checked by the post-freeze guard. The guard writes a separate per-check log and a machine-readable result, both outside the frozen runtime.
