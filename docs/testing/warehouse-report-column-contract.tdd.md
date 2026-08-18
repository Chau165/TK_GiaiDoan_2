# Warehouse report column-contract TDD evidence

## Symptom

The Warehouse report showed product codes and values, but `Ngày`, `Số phiếu`, `Số lượng`, and `Đơn giá` were blank or zero for receipt/issue detail reports.

## Root cause

The live database was executing the last `CREATE OR ALTER` definitions of `sp_BC_Chi_Tiet_Nhap` and `sp_BC_Chi_Tiet_Xuat`. Those definitions returned legacy column names such as `Ngay_Nhap_Kho`, `SL_Nhap`, and `Don_Gia_Nhap`, while `CWarehouseDetailReport` requires `Ngay`, `So_Luong`, and `Don_Gia`. `CUtility.Map_Row_To_Entity` maps by property name, so unmatched fields kept default values; `Tri_Gia` still appeared because its alias already matched.

## RED/GREEN evidence

- RED checkpoint: `435ad6b test: reproduce Warehouse report column contract mismatch`.
- RED command: `sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.ReportContract.IntegrationTests.sql`.
- RED result: failed with `Warehouse report result columns do not match report entities`; live metadata showed `Ngay_Nhap_Kho`, `SL_Nhap`, and `Don_Gia_Nhap`.
- GREEN checkpoint: `df8e554 fix: align Warehouse report result columns`.
- GREEN result: report contract test passed.
- Live receipt report verification returned quantities `50.000`, `120.000`, and `60.000` with matching unit prices.
- Live issue report verification returned quantities `30.000`, `20.000`, and `15.000` with matching unit prices.
- Live inventory report verification returned non-zero `SL_Nhap`, `SL_Xuat`, and `SL_Cuoi_Ky` values.
- Existing Warehouse integration, validation-message, and Unicode SQL tests passed.

## Fix

The final receipt/issue report procedure definitions now return the entity contract:

`Ngay, So_Phieu, Nha_Cung_Cap, Ma_San_Pham, Ten_San_Pham, So_Luong, Don_Gia, Tri_Gia`.

Joins, date filtering, quantity source columns, and inventory business rules were unchanged.
