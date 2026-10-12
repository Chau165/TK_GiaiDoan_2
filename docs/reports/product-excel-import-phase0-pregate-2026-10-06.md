# Product Excel Import Phase 0 Pre-Gate

Ngày kiểm tra: 2026-10-06  
Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`  
HEAD: `9f361e4`

## 1. Executive Summary

**PRODUCT_IMPORT_PHASE0_BLOCKED**

Phase 0 xác nhận Telerik `SpreadImporter` chạy được trên XLSX do Telerik streaming exporter tạo, nhưng cùng importer ném exception khi enumerate cell của XLSX do EPPlus 7.0.5 tạo. Chưa đủ bằng chứng để cho phép Phase 1 xử lý workbook XLSX bên ngoài.

| Gate | Kết quả |
|---|---|
| SpreadImporter runtime | BLOCKED cho khả năng tương thích XLSX tổng quát; Telerik round-trip PASS, EPPlus reproduction FAIL |
| Telerik package/license kỹ thuật | PASS; build warnings được liệt kê riêng, runtime banner chưa xác minh |
| Commercial entitlement | NEED_OWNER_CONFIRMATION |
| Stream copy | FIXED_AND_TESTED |
| Add permission | READY trong server-side Blazor circuit, theo contract bên dưới |
| Unit source contract | READY; live data check NOT_EXECUTED |
| Upload 1 MiB | READY theo cấu hình source; host triển khai NOT_VERIFIED |

Không tạo Product Import service/DTO/UI/template, không sửa SQL, không kết nối DB và không bắt đầu Phase 1.

## 2. Repository / Worktree State

`git status` và diff được ghi nhận trước khi sửa. Các thay đổi đã có trước task này được giữ nguyên:

`AGENTS.md` áp dụng trong repo/parent không được tìm thấy; nội dung AGENTS người dùng cung cấp được áp dụng.

- `Database/WarehouseModule.Procedures.sql`
- `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseUiWorkflowTests.cs`
- `TKS_Thuc_Tap_V11_Web/Program.cs`
- `TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj`
- `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor`
- `TKS_Thuc_Tap_V11_Web_Common/Common/CGrid_DataSource_Utility.cs`
- `docs/reports/excel-import-readiness-audit-2026-10-05.md`

Trong `Program.cs`, `TelerikLicensing.Register()` và package reference `Telerik.Licensing` là thay đổi có sẵn. Phần Phase 0 chỉ thêm cấu hình Hub ở dòng 31-35. Không commit; không sửa procedure/schema nghiệp vụ; không chạy app vì startup có thể truy cập DB.

## 3. SpreadImporter Dependency Evidence

- `Telerik.UI.for.Blazor` `14.0.0` được reference trực tiếp trong Web, Web_Common và Web_Danh_Muc.
- `Telerik.Documents.SpreadsheetStreaming` `2026.2.519` được resolve trong `obj/project.assets.json` theo dependency graph, không phải package reference trực tiếp của test project hoặc Data_Access.
- Runtime test xác nhận assembly được load: `Telerik.Documents.SpreadsheetStreaming.dll`, version `2026.2.519.20`.
- API/namespace thực tế: `Telerik.Documents.SpreadsheetStreaming.SpreadImporter.CreateWorkbookImporter(SpreadDocumentFormat.Xlsx, Stream)`.
- Fixture interoperability dùng EPPlus `7.0.5`, cũng đã resolve trong assets.

Runtime code path bám theo [Telerik RadSpreadStreamProcessing Import](https://www.telerik.com/document-processing-libraries/documentation/libraries/radspreadstreamprocessing/import): workbook → worksheet → rows → cells.

## 4. SpreadImporter Runtime Proof

Test source: `TKS_Thuc_Tap_V11_Data_Access.Tests/TelerikSpreadImporterRuntimeTests.cs`.

1. Test tạo `.xlsx` thật bằng `SpreadExporter`, mở file stream đó bằng `SpreadImporter`, rồi enumerate worksheet `SanPham`, rows và cells.
2. Đọc đúng headers `Ma_San_Pham`, `Ten_San_Pham`, `Ma_LSP`, `Ten_Don_Vi_Tinh`, `Ghi_Chu`; đọc đúng `SP001`, `Coca Cola`, `SP002`, `Sữa hộp`, `Sản phẩm`, `Đơn vị tính`, `Hộp`.
3. Ô trống cuối dòng không được biến thành dữ liệu; hàng rỗng được kiểm tra.
4. Mã text `00123` được giữ nguyên và đọc `NumberFormat="@"`.
5. Formula trả `FormulaValue="1+1"`, `Value=null`, `ValueType=Number` trên fixture này. API mô tả `Value` là cached result và `FormulaValue` là expression; Phase 1 không nên coi formula là literal data. Xem [ICellImporter API](https://www.telerik.com/document-processing-libraries/documentation/api/telerik.documents.spreadsheetstreaming.icellimporter).

Đối chứng interoperability: test thứ hai tạo workbook đơn giản bằng EPPlus 7.0.5. `SpreadImporter` ném `InvalidOperationException: Nullable object must have a value.` khi enumerate cell, stack kết thúc tại `Telerik.Documents.SpreadsheetStreaming.Importers.XlsxCellImporter.ReadCellStyles()` trước khi đọc header. Một fixture EPPlus đầy đủ hơn có `s="0"` trên cells; `xl/styles.xml` bỏ qua một số style-ID mặc định. **Inference, chưa xác định được nullable field nội bộ cụ thể:** parser có thể dereference style metadata vắng mặt. Không khẳng định mọi workbook Excel đều lỗi; workbook được tạo trực tiếp bởi Microsoft Excel chưa được kiểm thử.

Kết luận gate: chứng minh được parser assembly chạy trên XLSX tương thích, nhưng không chứng minh được file XLSX bên ngoài; EPPlus reproduction là blocker cần giải quyết trước Phase 1.

## 5. Telerik License / Package Result

- Web source có `TelerikLicensing.Register()` và `Telerik.Licensing` `1.8.2`; đây là thay đổi pre-existing, không do task này thêm.
- Build phát `TKL002` (không tìm thấy Telerik license file), `TKL101` (Telerik UI for Blazor và Document Processing Libraries không có trong license hiện tại) và `TKL004` (không xác định được license cho tất cả sản phẩm).
- Không đọc/in license key hoặc token. Không có bằng chứng entitlement thương mại hợp lệ từ source/environment.
- Parser test không báo lỗi licensing khi chạy; app UI không được khởi động nên runtime banner/watermark chưa được xác minh. Warning build phù hợp với license notice đã thấy trong workbook trước đây, nhưng chưa coi đó là runtime verification.

**TECHNICAL_LICENSE_GATE: PASS** (build/runtime usable on the supported fixture; warnings are recorded above)  
**BUSINESS_ENTITLEMENT_GATE: NEED_OWNER_CONFIRMATION**  
**LICENSE_ENTITLEMENT_NOT_VERIFIABLE_FROM_SOURCE**

Telerik ghi nhận thiếu/không hợp lệ license có thể tạo warning hoặc runtime notice: [activation errors and warnings](https://www.telerik.com/document-processing-libraries/documentation/distribution-and-licensing/license-key/activation-errors-and-warnings).

## 6. Stream Copy Audit

Hai component `FCommon_Import_Excel.razor` và `FCommon_Import_File.razor` có cùng lỗi: `ReadAsync` trả số byte thực đọc vào `toCopyLength`, nhưng vòng lặp ghi `eachReadLength`; nhánh cuối cũng ghi `left` thay vì số byte thực đọc. Vòng lặp còn dựa vào `Length`/`Position`, flush từng chunk và tạo CTS không được cancel/dispose. Với partial read, nội dung đích có thể bị sai dù độ dài nhìn có vẻ đúng.

Đã thay bằng `CStream_Copy_Utility.Copy_Async` dùng `Stream.CopyToAsync`, gọi từ cả hai component. Cả source và destination được `await using`; không còn giả định seek/length hay ghi số byte yêu cầu thay vì số byte nhận. Cancellation token có thể truyền qua utility; hai picker hiện không cấp token riêng.

Test `StreamCopyBehaviorTests.Partial_reads_copy_exact_bytes` dùng source 10,123 byte, giới hạn mỗi read 4,096 byte; kiểm tra độ dài và SHA-256 giống nhau. Test đỏ ban đầu trên loop cũ do hash khác; sau sửa test pass.

**STREAM_COPY: FIXED_AND_TESTED**

## 7. Server-side Add Permission Contract

- Trang sản phẩm là route `/Kho/San_Pham`; source menu SQL khai báo function code `2018` (không truy vấn live DB trong task này).
- User hiện tại lấy qua `CSession.Get_Active_User_Name(ProtectedLocalStorage)`; `FBase` resolve route hiện tại bằng `NavigationManager.Uri`, gọi `CCache_Chuc_Nang.Get_Data_By_Func_URL`, lấy `Ma_Chuc_Nang`, rồi gọi `CCommonFunction.Get_Chuc_Nang_By_User(user, code)`.
- Permission object deny-by-default và tổng hợp quyền nhóm; property cần kiểm tra cho INSERT-only là `Is_Have_Add_Permission`, không phải Add OR Edit.

Contract Phase 1:

```text
Preview:
  resolve current user from ProtectedLocalStorage on the server circuit
  resolve active function from NavigationManager route -> cache function code
  permission = CCommonFunction.Get_Chuc_Nang_By_User(user, functionCode)
  require permission.Is_Have_Add_Permission
  parse/validate only; no writes

Confirm:
  resolve user and function again; do not reuse client-supplied identity/CanAdd
  call Get_Chuc_Nang_By_User again; require Is_Have_Add_Permission
  validate then execute INSERT-only DB transaction
```

Có thể reuse helper hiện tại, không cần permission mới. Kết luận **SERVER_SIDE_ADD_AUTH: READY** khi service chạy trong Blazor Server circuit và tự đọc protected storage/route; không nhận `bool CanAdd`, username hoặc function code do client quyết định. App dùng custom `ProtectedLocalStorage`, không thấy `AddAuthentication`/`ClaimsPrincipal` trong source đã kiểm tra. Nếu Phase 1 chuyển sang API/background service ngoài circuit, cần bổ sung trusted server identity context trước.

## 8. DonViTinh Normalization

- `dbo.tbl_DM_Don_Vi_Tinh` có `Auto_ID`, `Ten_Don_Vi_Tinh NVARCHAR(200)`; schema tạo unique index trên raw `Ten_Don_Vi_Tinh`.
- F2016 lookup trả `Code=''`, `Name=Ten_Don_Vi_Tinh`. F2016 insert/update trim tham số bằng `LTRIM/RTRIM` và duplicate-check `Latin1_General_CI_AI`.
- Resolver nên trim giá trị nhập và giá trị DB, so sánh `COLLATE Latin1_General_CI_AI`, rồi yêu cầu đúng một match; 0 hoặc >1 phải báo validation, không tự chọn dòng.
- Không có DB DEV/TEST được xác nhận an toàn trong task; không kết nối/query DB.

**UNIT_LOOKUP_SOURCE_CONTRACT: READY**  
**LIVE_DB_UNIT_COLLISION_CHECK_NOT_EXECUTED**  
**UNIT_LOOKUP_DATA_CHECK: NOT_EXECUTED**

Read-only SQL để owner/DBA chạy sau khi xác nhận đúng DEV/TEST:

```sql
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;

;WITH UnitNames AS
(
    SELECT Auto_ID,
           Ten_Don_Vi_Tinh,
           LTRIM(RTRIM(Ten_Don_Vi_Tinh)) COLLATE Latin1_General_CI_AI AS NormalizedName
    FROM dbo.tbl_DM_Don_Vi_Tinh
), CollisionNames AS
(
    SELECT NormalizedName
    FROM UnitNames
    GROUP BY NormalizedName
    HAVING COUNT(*) > 1
)
SELECT n.Auto_ID, n.Ten_Don_Vi_Tinh, n.NormalizedName
FROM UnitNames AS n
JOIN CollisionNames AS c ON c.NormalizedName = n.NormalizedName
ORDER BY n.NormalizedName, n.Auto_ID;
```

## 9. Upload / SignalR 1 MiB Gate

- Hai shared FileSelect hiện đặt `MaxFileSize="20480000"` byte.
- `Program.cs` đặt `MaximumReceiveMessageSize = 22 * 1024 * 1024` = 23,068,672 byte; hữu hạn, đủ cho shared limit 20,480,000 byte và khoảng đệm 2,588,672 byte. Giữ nguyên `CircuitOptions.DetailedErrors`.
- Phase 1 component cần đặt client `MaxFileSize=1_048_576`; importer/service bắt buộc kiểm tra lại `file.Length <= 1_048_576` ở server, không dựa vào client.
- Telerik xác nhận Blazor Server FileSelect truyền qua SignalR và file lớn hơn 32 KiB cần `MaximumReceiveMessageSize`; tài liệu cũng yêu cầu kiểm tra size phía server: [FileSelect overview](https://www.telerik.com/blazor-ui/documentation/components/fileselect/overview), [FileSelect validation](https://www.telerik.com/blazor-ui/documentation/components/fileselect/validation).
- Không tìm thấy `web.config` trong Web project; cấu hình IIS/reverse proxy/máy triển khai thực tế chưa được xác minh.

**UPLOAD_1MIB_TRANSPORT: READY (source configuration)**  
**DEPLOYED_HOST_CONFIGURATION: NOT_VERIFIED**

## 10. Files Changed

Phase 0 edits:

- `TKS_Thuc_Tap_V11_Web_Common/Common/CStream_Copy_Utility.cs` (new)
- `TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Import_Excel.razor`
- `TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Import_File.razor`
- `TKS_Thuc_Tap_V11_Web/Program.cs` (Hub limit/comment only; pre-existing Telerik license edits retained)
- `TKS_Thuc_Tap_V11_Data_Access.Tests/StreamCopyBehaviorTests.cs` (new)
- `TKS_Thuc_Tap_V11_Data_Access.Tests/TelerikSpreadImporterRuntimeTests.cs` (new; positive parser proof and EPPlus failure reproduction)
- `docs/reports/product-excel-import-phase0-pregate-2026-10-06.md` (this report)

Không tạo production DTO/service/UI/template/import batch/procedure.

## 11. Tests / Build

- `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests\TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~TelerikSpreadImporterRuntimeTests|FullyQualifiedName~StreamCopyBehaviorTests" --logger 'console;verbosity=minimal' --verbosity quiet` — PASS, 3 passed, 0 failed. Một parser test xác nhận round-trip thành công; test parser còn lại PASS bằng cách xác nhận EPPlus workbook tái hiện exception đã biết.
- `dotnet build TKS_Thuc_Tap_V11_Web\TKS_Thuc_Tap_V11_Web.csproj --no-restore --verbosity quiet` — PASS, 0 errors, 24 warnings; có `TKL002`, `TKL101`, `TKL004`.
- Không chạy full suite, web runtime, DB query, import integration hoặc production/deployment test.

## 12. Remaining Blockers

**BLOCKER-01**  
Area: SpreadImporter XLSX interoperability.  
Evidence: Telerik runtime `2026.2.519.20` đọc fixture Telerik-generated, nhưng EPPlus 7.0.5 workbook ném `Nullable object must have a value` trong `XlsxCellImporter.ReadCellStyles()` trước khi đọc dữ liệu.  
Impact: Chưa thể đảm bảo file XLSX người dùng chọn được parser xử lý.  
Required action: Kiểm thử workbook tạo bởi Microsoft Excel; xác nhận với Telerik hoặc nâng/chọn parser sau khi có fixture chứng minh. Không bắt đầu Phase 1 trước khi test external XLSX pass.

**BLOCKER-02**  
Area: Telerik commercial entitlement.  
Evidence: build warnings `TKL002/TKL101/TKL004`; không có bằng chứng entitlement từ source/environment.  
Impact: Build/runtime license notice có thể ảnh hưởng bản phát hành; runtime UI chưa được xác minh.  
Required action: Owner cung cấp và cấu hình license hợp lệ cho Telerik UI for Blazor và Document Processing; chạy UI smoke test mà không in key ra log/report.

Owner follow-up, không chạy trong task này: xác nhận DB DEV/TEST rồi chạy SQL ở mục 8; xác minh giới hạn WebSocket/IIS/reverse proxy tại host triển khai.

## 13. Final Verdict

**PRODUCT_IMPORT_PHASE0_BLOCKED**

Blocker-01 làm pre-gate parser chưa đạt cho XLSX bên ngoài; Blocker-02 là owner/deployment gate. Stream copy đã an toàn theo test, permission contract và Unit lookup source contract đã chốt, upload limit đã cấu hình hữu hạn. Không bắt đầu Product Import Phase 1 cho đến khi parser external-XLSX và commercial entitlement được xác nhận.
