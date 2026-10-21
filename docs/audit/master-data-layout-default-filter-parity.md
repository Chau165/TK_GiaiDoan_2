# Báo cáo parity `Layout_Default_Filter` cho năm Warehouse Master Data

Ngày kiểm tra: 2026-10-21. Repository: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`.

## Kết quả

Đã bật bộ lọc ngày tạo và toolbar dùng chung của `FBase` cho Kho, Đơn vị tính, Loại sản phẩm, Sản phẩm và Nhà cung cấp. Mỗi List gọi Stored Procedure `*_By_Created` với `r_objFilter.Date_From/Date_To`. Sản phẩm vẫn giữ lookup danh mục/đơn vị và mapping `CProductGridRow`. Không sửa `FBase`, Chủ Hàng, procedure, quyền, import handler, export config hoặc dữ liệu database trong lượt này.

| Module | `Layout_Default_Filter` Active | `By_Created` Active | SQL Exists | Permission Verified | Date Filter Test | Status |
|---|---|---|---|---|---|---|
| Kho (2009) | Có; `Kho_Import_Template.xlsx` | Có; `F2009_sp_sel_List_Kho_By_Created` | dbo; 2 `datetime2(7)` params; SELECT OK | FBase gates preserved; actual user UI pending | 5 rows in range; 0 out-of-range; 2027: 0 | Source + DB verified; `UI_E2E_PENDING` |
| Đơn vị tính (2016) | Có; `Don_Vi_Tinh_Import_Template.xlsx` | Có; `F2016_sp_sel_List_Don_Vi_Tinh_By_Created` | dbo; 2 `datetime2(7)` params; SELECT OK | FBase gates preserved; actual user UI pending | 10 rows in range; 0 out-of-range; 2027: 0 | Source + DB verified; `UI_E2E_PENDING` |
| Loại sản phẩm (2017) | Có; `Loai_San_Pham_Import_Template.xlsx` | Có; `F2017_sp_sel_List_Loai_San_Pham_By_Created` | dbo; 2 `datetime2(7)` params; SELECT OK | FBase gates preserved; actual user UI pending | 6 rows in range; 0 out-of-range; 2027: 0 | Source + DB verified; `UI_E2E_PENDING` |
| Sản phẩm (2018) | Có; `San_Pham_Import_Template.xlsx` | Có; `F2018_sp_sel_List_San_Pham_By_Created` | dbo; 2 `datetime2(7)` params; SELECT OK | FBase gates preserved; actual user UI pending | 15 rows in range; 0 out-of-range; 2027: 0 | Source + DB verified; `UI_E2E_PENDING` |
| Nhà cung cấp (2019) | Có; `Nha_Cung_Cap_Import_Template.xlsx` | Có; `F2019_sp_sel_List_NCC_By_Created` | dbo; 2 `datetime2(7)` params; SELECT OK | FBase gates preserved; actual user UI pending | 5 rows in range; 0 out-of-range; 2027: 0 | Source + DB verified; `UI_E2E_PENDING` |

## Baseline và giữ nguyên worktree

- Branch `main`, HEAD `fd3235f` (`Fix/ refactor UI`). Lúc bắt đầu lượt này, có 27 tracked modifications và 6 untracked files từ các việc trước. Không reset, clean, stash, revert, commit hoặc push.
- Đã dùng CodeGraph để lần theo quan hệ giữa `FBase`, List Chủ Hàng và năm List mục tiêu. Công cụ hiện có chỉ cung cấp `codegraph_explore`, không có lệnh sync/status để làm mới và xác nhận index sau chỉnh sửa; không tuyên bố index đã được sync hậu sửa.
- Không có thay đổi database trong lượt này: không deploy lại procedure, không cấp GRANT và không ghi business data.

## Source changes và đối chiếu Chu_Hang

`F2003_1_Chu_Hang_List.razor` dùng `@Layout_Default_Filter(grdData, "Chu_Hang_Template.xlsx")`, rồi `Load_Data()` gọi `FQ_104_CH_sp_sel_List_By_Created(Date_From, Date_To)`. Năm List nay theo cùng mẫu: khai báo đúng một `Layout_Default_Filter` với template module và truyền đúng hai ngày vào By_Created.

| File | Thay đổi |
|---|---|
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_1_Kho_List.razor` | Dòng 3 bật helper; dòng 78–82 giữ `Format_Grid` và đổi `Load_Data()` sang Kho By_Created; bỏ toolbar Add/Import riêng và handler Import riêng. |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_List.razor` | Dòng 3 bật helper; dòng 78–82 đổi `Load_Data()` sang Đơn vị tính By_Created; bỏ toolbar/handler Import riêng. |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_1_Loai_San_Pham_List.razor` | Dòng 3 bật helper; dòng 79–83 đổi `Load_Data()` sang Loại sản phẩm By_Created; bỏ toolbar/handler Import riêng. |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_1_San_Pham_List.razor` | Dòng 3 bật helper; dòng 81–87 đổi nguồn danh sách sang Sản phẩm By_Created; vẫn giữ lookup Loại sản phẩm/Đơn vị tính và mapping `CProductGridRow`; bỏ toolbar/handler Import riêng. |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_1_Nha_Cung_Cap_List.razor` | Dòng 3 bật helper; dòng 79–83 đổi `Load_Data()` sang NCC By_Created; bỏ toolbar/handler Import riêng. |

`FBase.Layout_Default_Filter` tự render date pickers, Tìm kiếm, Add, Import và gọi `Layout_Tool_Button` cho Công cụ/Export/Hướng dẫn. Do đó List không còn gọi toolbar thêm lần nữa. Cơ chế quyền tiếp tục được kiểm tra ở shared helper: Add/Import theo `Is_Have_Add_Permission`, Export theo `Is_Have_Export_Permission`; quyền Edit/Delete trong từng Grid vẫn giữ nguyên.

`FCommon_Import_Excel`, `Import_Excel_Entry`, validators, transaction/cache/history và template vẫn được giữ. Cấu hình `GridExcelExport`/`GridCsvExport`, `AllPages="true"`, `Prepare_Excel_Export`, dynamic columns và CRUD callbacks không đổi. Không áp filter mới vào lookup/cache.

## SQL, cấu hình runtime và timezone

- `TKS_Thuc_Tap_V11_Web/appsettings.json` trỏ `localhost/TKS_Thuc_Tap_V11_GiaiDoan2` bằng Integrated Security; không có environment override `ConnectionStrings__TKS_Thuc_Tap_V11_Conn_String`. Kết nối thực tế phân giải thành `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_GiaiDoan2`, khớp database đích.
- Cả năm procedure tồn tại trong schema `dbo`. Thứ tự tham số là `@Date_From_Utc`, `@Date_To_Utc_Exclusive`, cả hai `datetime2(7)`. Definitions lọc `Created >= @Date_From_Utc AND Created < @Date_To_Utc_Exclusive`; projections khớp entity fields. Mỗi procedure được gọi read-only với khoảng ngày mẫu và trả đúng cột.
- Search 01/10/2026–21/10/2026 được chuyển từ UTC+07 sang UTC bounds; kết quả từng module nằm trong ngày yêu cầu. Search tháng 01/2027 trả 0 dòng cho cả năm module. Không có dòng ngoài biên trong kết quả kiểm tra.
- Web process đang lắng nghe cổng 15073 dưới `DESKTOP-NHQ7QPL\Surface`. App dùng Integrated Security; kết nối SQL cùng Windows login trả `dbo`, và thao tác EXECUTE read-only thành công dưới identity đó. Không cấp thêm quyền.
- Windows host timezone là `SE Asia Standard Time`, UTC+07. Bộ chuyển đổi hiện có dùng local timezone cho ngày bắt đầu và ngày kế tiếp; cách này khớp host chạy app hiện tại. Không hardcode timezone vào source.
- Process trên cổng 15073 đang chạy từ output chuẩn `bin\Debug`; không restart hoặc chiếm phiên đang mở của người dùng. Build lượt này đi vào thư mục tạm. Vì vậy chưa thực hiện browser/UI E2E trên đúng binary mới hoặc xác minh button với phiên/quyền người dùng thực tế: trạng thái là `UI_E2E_PENDING`, không phải `UI_PASS`.
- UI E2E còn chờ trên các route `/Kho/Quan_Ly`, `/Kho/Don_Vi_Tinh`, `/Kho/Loai_San_Pham`, `/Kho/San_Pham`, `/Kho/Nha_Cung_Cap`.

## Kiểm thử và xác minh

- TDD: trước khi sửa List, 7 focused tests đỏ do các component chưa dùng helper/SP mới; sau sửa, focused tests xanh.
- `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --verbosity quiet --filter "FullyQualifiedName~WarehouseMasterCreatedFilterContractTests|FullyQualifiedName~MasterDataCompanyStyleMigrationTests|FullyQualifiedName~UnitExcelImportUiTests"` — **20 passed, 0 failed, 0 skipped**. Kiểm tra template, đúng một helper mỗi List, không gọi toolbar/handler Add-Import riêng, đúng By_Created/date arguments, import/validation, CRUD và export contracts.
- `dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore --nologo --verbosity quiet` với `BaseOutputPath` trỏ thư mục tạm — **0 errors, 36 warnings**. Các warning gồm thiếu Telerik license và warning có sẵn trong source.
- Read-only SQL date checks: 10 lần gọi (5 procedures × 2 khoảng ngày); mọi cột `Created` nằm trong bounds; future range rỗng. Không gọi CRUD và không ghi dữ liệu.
- `git diff --check` — không có whitespace error. Git có cảnh báo line-ending LF/CRLF ở các file vốn đã modified.
- Không chạy browser E2E vì không khởi động lại process đang phục vụ cổng 15073 và không có browser session đã xác thực với build mới. Không báo UI/runtime pass.

## Trạng thái cuối

`Layout_Default_Filter` và `By_Created` đã hoạt động trong source của cả năm module; procedure và filter boundaries đã được xác minh bằng truy vấn read-only trên đúng database đích. Import, export, CRUD, lookup và permission gates dùng shared FBase/các cấu hình cũ. Còn chờ kiểm tra trực quan trên binary mới trong phiên đăng nhập phù hợp (`UI_E2E_PENDING`). Không commit/push.
