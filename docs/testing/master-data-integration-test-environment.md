# Cấu hình môi trường integration test cho Master Data

Ngày kiểm tra: 2026-10-20

## Trạng thái hiện tại

- `TKS_INTEGRATION_CONNECTION_STRING` không được cấu hình trong process hiện tại.
- Ứng dụng có connection setting trong `TKS_Thuc_Tap_V11_Web/appsettings.json`; integration test không được lấy hoặc tái sử dụng giá trị đó. Giá trị này không được đọc hoặc ghi vào báo cáo.
- Test guard trong source chỉ cho phép server `DESKTOP-NHQ7QPL\MSSQLSERVER19` và database `TKS_Thuc_Tap_V11_C01C02_Test_20260906`. Đây là allowlist trong code, chưa phải bằng chứng database đang tồn tại hoặc đã được cấp quyền ghi ở phiên hiện tại.
- `WarehouseTestDatabase` đã bỏ fallback sang database nghiệp vụ và fail closed khi thiếu biến môi trường.
- Không có kết nối, ghi dữ liệu, provision database, hay deploy SQL trong lượt này.

## Xác nhận cần có từ chủ sở hữu/DBA

Trước khi chạy test có ghi dữ liệu, xác nhận:

1. Database mục tiêu là test riêng, được chủ sở hữu cho phép ghi và không dùng chung/production.
2. Kết nối thực tế trả đúng server/database trong allowlist của `ProductImportTestDatabase`.
3. Database có schema, stored procedures và view cần thiết cho master CRUD, lookup, `FQ_422_IE_sp_ins_Insert`, `FQ_425_RAH_sp_ins_Insert`, và `FCommon_Sys_sp_sel_List_Log_Record_Action_History`.
4. Mỗi test có prefix riêng và cleanup chỉ xóa đúng các dòng do test đó tạo. Không chạy script deploy/provision để tự tạo hoặc thay đổi database trên server dùng chung.
5. Người chạy test được phép dùng chính tài khoản tích hợp đã được cấu hình ở máy chạy test.

## Cấu hình sau khi được xác nhận

Đặt `TKS_INTEGRATION_CONNECTION_STRING` trong environment của process/IDE/CI test bằng secret manager phù hợp. Không lưu connection string hoặc password trong source, `appsettings.json`, log, report hoặc chat. Guard sẽ từ chối server/database khác allowlist; không nới guard để nhận database bất kỳ.

Chạy trước các test thuần guard (không mở SQL connection):

```powershell
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductImportDatabaseSafetyTests|FullyQualifiedName~WarehouseTestDatabaseSafetyTests" -clp:ErrorsOnly --logger "console;verbosity=minimal"
```

Chỉ sau khi chủ sở hữu xác nhận môi trường và kiểm tra identity thực tế mới chọn từng integration-test class cụ thể. Không chạy toàn bộ test project khi chưa phân loại các test có ghi dữ liệu.

## Giới hạn bằng chứng hiện tại

- `UnitImportRuntimeIntegrationTests` gọi `CWarehouseUnitImportService.PreviewAsync/ConfirmAsync`, là importer legacy. Nó không kiểm tra active path `FCommon_Import_Excel → FBase.Import_Excel → FWarehouse_Unit_List.Import_Excel_Entry`.
- Các stored procedure ghi Import Log và Action History chưa có body trong source tree được kiểm tra; không thể xác nhận bảng lưu, timestamp, hay row runtime khi chưa có database được duyệt.
- Vì vậy cấu hình đúng chỉ mở khả năng chạy test an toàn; nó không tự tạo bằng chứng runtime PASS. Cần thêm test cho active Unit importer và đối chiếu SQL/cache/lookup/history/log riêng.
