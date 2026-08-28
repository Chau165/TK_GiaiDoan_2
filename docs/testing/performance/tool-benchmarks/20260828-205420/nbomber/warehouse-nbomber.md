> test info



test suite: `TKS_Thuc_Tap_V11 warehouse read workload`

test name: `nbomber_default_test_name`

session id: `2026-08-28_13-56-30_d84840ba`

> scenario stats



scenario: `MasterPaged`

  - duration: `00:00:15`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:15`

|scenario and steps|ok stats|
|---|---|
|scenario name|`MasterPaged`|
|requests|total = `61970`, ok = `61970`, fail = `0`|
|RPS (req/sec)|total = `4131.33`/s, ok = `4131.33`/s, fail = `0`/s|
|latency (ms)|min = `2.21`, mean = `27.46`, max = `978.11`, StdDev = `16.14`|
|latency percentile (ms)|p50 = `25.01`, p75 = `32.43`, p95 = `53.6`, p99 = `86.85`|




> scenario stats



scenario: `LookupPaged`

  - duration: `00:00:15`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:15`

|scenario and steps|ok stats|
|---|---|
|scenario name|`LookupPaged`|
|requests|total = `94267`, ok = `94267`, fail = `0`|
|RPS (req/sec)|total = `6284.47`/s, ok = `6284.47`/s, fail = `0`/s|
|latency (ms)|min = `2.26`, mean = `31.57`, max = `956.54`, StdDev = `20.81`|
|latency percentile (ms)|p50 = `27.26`, p75 = `37.6`, p95 = `71.1`, p99 = `109.57`|




> scenario stats



scenario: `DocumentPaged`

  - duration: `00:00:15`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:15`

|scenario and steps|ok stats|
|---|---|
|scenario name|`DocumentPaged`|
|requests|total = `2703`, ok = `2703`, fail = `0`|
|RPS (req/sec)|total = `180.2`/s, ok = `180.2`/s, fail = `0`/s|
|latency (ms)|min = `218.28`, mean = `914.97`, max = `3594.05`, StdDev = `489.6`|
|latency percentile (ms)|p50 = `699.39`, p75 = `1158.14`, p95 = `1970.18`, p99 = `2437.12`|




> scenario stats



scenario: `DetailReportPaged`

  - duration: `00:00:15`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:15`

|scenario and steps|ok stats|
|---|---|
|scenario name|`DetailReportPaged`|
|requests|total = `892`, ok = `892`, fail = `0`|
|RPS (req/sec)|total = `59.47`/s, ok = `59.47`/s, fail = `0`/s|
|latency (ms)|min = `549.54`, mean = `2545.47`, max = `9231.86`, StdDev = `2052.43`|
|latency percentile (ms)|p50 = `1600.51`, p75 = `4243.46`, p95 = `6688.77`, p99 = `8269.82`|




> scenario stats



scenario: `InventoryReportPaged`

  - duration: `00:00:15`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:15`

|scenario and steps|ok stats|
|---|---|
|scenario name|`InventoryReportPaged`|
|requests|total = `71`, ok = `18`, fail = `53`|
|RPS (req/sec)|total = `4.73`/s, ok = `1.2`/s, fail = `3.53`/s|
|latency (ms)|min = `18815.25`, mean = `26019.88`, max = `29563.02`, StdDev = `3254.76`|
|latency percentile (ms)|p50 = `26624`, p75 = `28655.62`, p95 = `29343.74`, p99 = `29573.12`|


|scenario and steps|failures stats|
|---|---|
|scenario name|`InventoryReportPaged`|
|requests|total = `71`, ok = `18`, fail = `53`|
|RPS (req/sec)|total = `4.73`/s, ok = `1.2`/s, fail = `3.53`/s|
|latency (ms)|min = `30006.91`, mean = `30082.47`, max = `30582.45`, StdDev = `115.26`|
|latency percentile (ms)|p50 = `30048.26`, p75 = `30097.41`, p95 = `30294.02`, p99 = `30441.47`|


> status codes for scenario: `InventoryReportPaged`



|status code|count|message|
|---|---|---|
|-101|53|Execution Timeout Expired.  The timeout period elapsed prior to completion of the operation or the server is not responding.|


