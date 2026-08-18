# Warehouse report decimal-mapping TDD evidence

## Symptom

The Warehouse report page displayed `Object of type 'System.Double' cannot be converted to type 'System.Decimal'` for detail receipt, detail issue, and inventory reports.

## Root cause

`CUtility.Map_Row_To_Entity` selected the converter from `DataColumn.DataType`. SQL aggregate/report values arrived as `Double`, while the destination report properties were `Decimal`. Reflection therefore tried to assign a `Double` directly to a `Decimal` property.

## RED/GREEN evidence

- RED checkpoint: `4005207 test: reproduce Warehouse report decimal mapping failure`.
- RED command: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter FullyQualifiedName~Warehouse_report_mapping_converts_double_columns_to_decimal_properties --verbosity=minimal`.
- RED result: failed with the exact user-visible exception and stack trace at `CUtility.Map_Row_To_Entity`.
- GREEN checkpoint: `6c71c10 fix: map Warehouse report doubles to decimals`.
- GREEN command: same focused test; result `Passed: 1, Failed: 0`.
- Full Data Access test result: `Passed: 12, Failed: 0`.
- Web build result: `Build succeeded`, `0 Error(s)`; existing Telerik license warnings remain non-fatal.
- Warehouse SQL integration, validation-message, and Unicode tests all passed.

## Fix

`CUtility` now:

1. Converts numeric input using the destination property type.
2. Uses `Convert_To_Decimal` for decimal properties.
3. Applies the same decimal conversion in `Clone_Entity`.

This fixes the shared mapper path used by all three Warehouse report modes without changing report stored procedures or business rules.

## Known gap

The regression test is a deterministic mapper-level test. An authenticated browser smoke test should still click all three report options after restarting the running Web process so the rebuilt DLL is loaded.
