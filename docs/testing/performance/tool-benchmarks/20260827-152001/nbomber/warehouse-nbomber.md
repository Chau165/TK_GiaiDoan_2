> test info



test suite: `TKS_Thuc_Tap_V11 warehouse read workload`

test name: `nbomber_default_test_name`

session id: `2026-08-27_08-24-19_f1a628ca`

> scenario stats



scenario: `MasterPaged`

  - duration: `00:00:10`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:10`

|scenario and steps|ok stats|
|---|---|
|scenario name|`MasterPaged`|
|requests|total = `207014`, ok = `207014`, fail = `0`|
|RPS (req/sec)|total = `20701.4`/s, ok = `20701.4`/s, fail = `0`/s|
|latency (ms)|min = `1.44`, mean = `13.83`, max = `786.62`, StdDev = `13.35`|
|latency percentile (ms)|p50 = `11.48`, p75 = `15.86`, p95 = `30.14`, p99 = `58.78`|




> scenario stats



scenario: `LookupPaged`

  - duration: `00:00:10`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:10`

|scenario and steps|ok stats|
|---|---|
|scenario name|`LookupPaged`|
|requests|total = `172163`, ok = `172163`, fail = `0`|
|RPS (req/sec)|total = `17216.3`/s, ok = `17216.3`/s, fail = `0`/s|
|latency (ms)|min = `1.47`, mean = `14.63`, max = `892.38`, StdDev = `14.83`|
|latency percentile (ms)|p50 = `11.86`, p75 = `16.62`, p95 = `31.55`, p99 = `61.63`|




> scenario stats



scenario: `DocumentPaged`

  - duration: `00:00:10`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:10`

|scenario and steps|ok stats|
|---|---|
|scenario name|`DocumentPaged`|
|requests|total = `8678`, ok = `8668`, fail = `10`|
|RPS (req/sec)|total = `867.8`/s, ok = `866.8`/s, fail = `1`/s|
|latency (ms)|min = `23.27`, mean = `207.18`, max = `29292.88`, StdDev = `642.11`|
|latency percentile (ms)|p50 = `167.55`, p75 = `225.15`, p95 = `364.29`, p99 = `721.41`|


|scenario and steps|failures stats|
|---|---|
|scenario name|`DocumentPaged`|
|requests|total = `8678`, ok = `8668`, fail = `10`|
|RPS (req/sec)|total = `867.8`/s, ok = `866.8`/s, fail = `1`/s|
|latency (ms)|min = `30005.31`, mean = `30669.87`, max = `34354.86`, StdDev = `1342.74`|
|latency percentile (ms)|p50 = `30031.87`, p75 = `30261.25`, p95 = `34373.63`, p99 = `34373.63`|


> status codes for scenario: `DocumentPaged`



|status code|count|message|
|---|---|---|
|-101|10|Execution Timeout Expired.  The timeout period elapsed prior to completion of the operation or the server is not responding.|


> scenario stats



scenario: `DetailReportPaged`

  - duration: `00:00:10`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:10`

|scenario and steps|ok stats|
|---|---|
|scenario name|`DetailReportPaged`|
|requests|total = `1767`, ok = `1761`, fail = `6`|
|RPS (req/sec)|total = `176.7`/s, ok = `176.1`/s, fail = `0.6`/s|
|latency (ms)|min = `146.49`, mean = `720.52`, max = `18063.28`, StdDev = `722.68`|
|latency percentile (ms)|p50 = `663.04`, p75 = `810.5`, p95 = `1276.93`, p99 = `2304`|


|scenario and steps|failures stats|
|---|---|
|scenario name|`DetailReportPaged`|
|requests|total = `1767`, ok = `1761`, fail = `6`|
|RPS (req/sec)|total = `176.7`/s, ok = `176.1`/s, fail = `0.6`/s|
|latency (ms)|min = `30006.06`, mean = `30035.26`, max = `30085.54`, StdDev = `29.1`|
|latency percentile (ms)|p50 = `30031.87`, p75 = `30064.64`, p95 = `30097.41`, p99 = `30097.41`|


> status codes for scenario: `DetailReportPaged`



|status code|count|message|
|---|---|---|
|-101|6|Execution Timeout Expired.  The timeout period elapsed prior to completion of the operation or the server is not responding.|


> scenario stats



scenario: `InventoryReportPaged`

  - duration: `00:00:10`

load simulations:

  - `keep_constant`, copies: `8`, during: `00:00:10`

|scenario and steps|ok stats|
|---|---|
|scenario name|`InventoryReportPaged`|
|requests|total = `87`, ok = `87`, fail = `0`|
|RPS (req/sec)|total = `8.7`/s, ok = `8.7`/s, fail = `0`/s|
|latency (ms)|min = `1885.38`, mean = `4342.92`, max = `7170.33`, StdDev = `1194.31`|
|latency percentile (ms)|p50 = `4505.6`, p75 = `5177.34`, p95 = `6197.25`, p99 = `7122.94`|




