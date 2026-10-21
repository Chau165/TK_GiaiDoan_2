# Phase Next — Recovery và migration Đơn vị tính

Ngày: 2026-10-20  
Repository: `Chau165/TK_GiaiDoan_2`  
Checkout: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`  
Branch/HEAD: `main` / `30bdb4f`

## A. Recovery

- Tiếp tục trên worktree đang có; không stash, reset, checkout, clean, commit hoặc push.
- Trạng thái ban đầu của continuation đã có source/test changes từ task trước và 12 file benchmark log đã bị xóa. Cả 12 deletion vẫn giữ nguyên; không log nào khác bị xóa.
- Phần trước đã sửa layout/form/CRUD chung, import preview shell, validation, queue/loading, permission và refresh lookup. Localization mới hoàn tất một phần ở Product/Unit/Category và callback chung; Supplier/Kho còn dở.
- Trong phase này đã hoàn tất localization cho năm importer components, tách route Đơn vị tính thành List/Info/Edit kế thừa `FBase`, chuyển main import sang flow `FCommon_Import_Excel`/`FBase`, bỏ nhánh Unit Preview/Confirm khỏi list parent, và cập nhật test route/flow. Service/component Unit legacy vẫn còn trong repository.
- CodeGraph có pending changes nên đã chạy `codegraph sync`; lần sync cuối cập nhật 6 file trước khi tra dependency.

## B. Ma trận component

| Component | Before | After | Chu_Hang reference | Status |
|---|---|---|---|---|
| Route `/Kho/Don_Vi_Tinh` | `FWarehouse_1_Warehouse_List` cùng nhiều loại màn hình | Render `<FWarehouse_Unit_List />` tại `Warehouse_Don_Vi_Tinh.razor:1-5` | `Chu_Hang.razor` render `F2003_1_Chu_Hang_List` | Đã sửa; route source test pass; runtime route chưa mở bằng browser |
| List/toolbar/grid | Dùng list tổng hợp, type selector và handlers theo loại | `FWarehouse_Unit_List` kế thừa `FBase`; Import là nút riêng cạnh Thêm, Công Cụ dùng `Layout_Tool_Button`; bỏ heading “Danh sách đơn vị tính”; Grid local `Data`, `Format_Grid`, dynamic `FCommon_General_Col_Grid` và fallback cột (`:1-58,83-91`) | `F2003_1_Chu_Hang_List` kế thừa `FBase`, nút Import riêng theo `Layout_Default_Filter`, cột cấu hình theo function metadata | Đã sửa; build và source tests pass |
| Info | Component Info chung, phụ thuộc `m_strMaster_Type` | `FWarehouse_Unit_Info` kế thừa `FBase`, modal ba cột, tab action history (`:1-67`) | `F2003_2_Chu_Hang_Info` / `FBase` | Đã sửa; source test pass; visual UAT chưa chạy |
| Edit/CRUD | Form tổng hợp và callback typed của list chung | `FWarehouse_Unit_Edit` dùng `Load_Data`, `Check_Data`, `Add_Data`, `Update_Data`, `Save_Data`, source/destination clone và cache (`:1-117`) | `F2003_3_Chu_Hang_Edit` override hook của `FBase.Save_Data` | Đã sửa; source tests cover hook/permission/cache/history; CRUD SQL chưa chạy |
| Import | Unit route cũ gọi importer Preview/Confirm và batch service | Route dùng `FCommon_Import_Excel`; `FBase.Close_Import_Excel` → `FBase.Import_Excel` → override `Import_Excel_Entry`; per-row transaction, cache, action history, import log, grid reload | `FCommon_Import_Excel` + `FBase` + `F2003_1_Chu_Hang_List.Import_Excel_Entry` | Đã sửa; source/template tests pass; DB import chưa chạy |
| Controller/cache | Service import batch làm validation và cache refresh sau Confirm | Giữ `CDM_Don_Vi_Tinh_Controller` và `CCache_Don_Vi_Tinh`; dùng insert transaction overload và cache `Add_Data` | `CDM_Chu_Hang_Controller` / `CCache_Chu_Hang` | SP contract không đổi; cache source test pass |
| Export Excel/CSV | Chưa có list riêng để kiểm tra dispatcher | `Layout_Tool_Button`; local Grid, không `OnRead`; `AllPages=true`; common Excel dispatcher chọn `Export_Excel_Custom` | Owner cũng dùng FBase và common toolbar | Dispatcher/permission source test pass; sorting/hidden columns và download runtime chưa xác minh |
| Language/permission/history | Nhãn/permission rải trong list tổng hợp | Labels qua `Get_Language_Data_Field`; Add/Edit/Delete/Import guard theo quyền; Export do common toolbar gate; CRUD/import ghi action history | Labels và hooks từ `F2003_1/2/3` | Source test pass; translation dictionary/browser chưa kiểm tra |

### CRUD và Data Access

- `Add_Data` gọi `F2016_sp_ins_Don_Vi_Tinh`, đặt `r_strAdd_Key`, rồi cập nhật cache; `Update_Data` gọi Store hiện có, clone kết quả vào `r_objDes`, rồi cập nhật cache (`FWarehouse_Unit_Edit.razor:50-111`). `Load_Data` lưu bản clone ban đầu vào `r_objSource` để FBase ghi diff (`:50-69`).
- Delete gọi `F2016_sp_del_Don_Vi_Tinh`, cập nhật cache, rồi dùng `FBase.Delete_Data` để confirm, queue/loading, reload và ghi action history (`FWarehouse_Unit_List.razor:96-109,257-264`; `FBase.razor:603-643`). Handler kiểm tra quyền trước khi gọi base.
- Add/Edit permission được kiểm tra ở nút/list và Save; nút Import riêng cạnh Thêm chỉ hiện khi có Add permission. Export/CSV và Hướng dẫn nằm trong `Layout_Tool_Button`; FBase ẩn Export/CSV khi thiếu Export permission.
- `CDM_Don_Vi_Tinh_Controller` là class Data Access độc lập, gọi SP list/cache/lookup/insert/update/delete; không kế thừa UI/base controller và không có `GetByID` (`CDM_Don_Vi_Tinh_Controller.cs:9-174`). Info/Edit hiện gọi list SP rồi tìm theo ID; không đổi hợp đồng SP, nhưng có thể đọc dư nếu danh mục lớn.
- `Ten_Don_Vi_Tinh` có `[StringLength(200)]`; Edit kiểm tra tên bắt buộc/200 ký tự và ghi chú tối đa 1000 ký tự (`CDM_Don_Vi_Tinh.cs:8-10`; `FWarehouse_Unit_Edit.razor:98-106`). Không sửa entity, database hay Stored Procedure.
- Đơn vị tính không dùng `Layout_Default_Filter`: list SP không có tham số ngày. Toolbar có Import riêng theo pattern Owner, dùng `Layout_Tool_Button` cho Công Cụ và không thêm date filter giả.

## C. Import Excel — trace method-by-method

1. Toolbar render Import thành nút riêng cạnh Thêm khi có Add permission; handler kiểm tra lại quyền và gọi `Open_Import_Excel("Don_Vi_Tinh_Import_Template.xlsx")` (`FWarehouse_Unit_List.razor:7-17,238-244`; `FBase.razor:1708-1712`).
2. `FCommon_Import_Excel` localizes nhãn qua callback, cho tải template, nhận file và gọi `OnClose` với `CImport_Excel` có path file (`FCommon_Import_Excel.razor:6-30,72-114`).
3. `FBase.Close_Import_Excel` ẩn modal, nhận file đã tải và gọi `Import_Excel` (`FBase.razor:1723-1733`).
4. `FBase.Import_Excel` bật loading/queue, xác minh header với template qua `CExcel_Controller.Validate_Header_Matches`, gọi override, ghi `CLog_Import_Excel`, kết thúc queue/loading và gọi `Load_Data_Init` để tải lại list (`FBase.razor:341-429`).
5. `FWarehouse_Unit_List.Import_Excel_Entry` đọc `A2:B`, map `Ten_Don_Vi_Tinh`/`Ghi_Chu`, bỏ dòng trống và kiểm tra required/length (`:112-149`). Template kiểm thử có sheet `DonViTinh`, headers `Ten_Don_Vi_Tinh`, `Ghi_Chu` (`UnitExcelImportUiTests.cs:117-128`).
6. Mỗi dòng hợp lệ mở connection/transaction, gọi overload `F2016_sp_ins_Don_Vi_Tinh(SqlConnection, SqlTransaction, ...)`, commit thành công hoặc rollback riêng dòng lỗi (`FWarehouse_Unit_List.razor:149-176`; controller `:112-132`). Dòng lỗi được gom và tiếp tục xử lý các dòng tiếp theo.
7. Sau commit, hook gọi `CCache_Don_Vi_Tinh.Add_Data`, tăng số dòng thành công và ghi `FCommon_Insert_Action_History`; lỗi history được báo riêng vì dữ liệu đã commit (`FWarehouse_Unit_List.razor:178-205`). Hook tổng kết số dòng thành công/lỗi (`:207-235`).
8. Route hiện không render `FWarehouse_Unit_Import` và parent không còn nhánh mở importer đó. Không gọi `PreviewAsync`/`ConfirmAsync` từ main Unit route. Component/service legacy được giữ do tests/callers còn tham chiếu.

### Ảnh hưởng transaction

Service cũ có Preview/Confirm và batch transaction. Import active mới commit theo từng dòng, giống Owner. Nếu dòng 1–3 commit rồi dòng 4 lỗi, dữ liệu của ba dòng đầu vẫn tồn tại; lỗi ở dòng sau không rollback các dòng trước. Đây là thay đổi semantics có chủ đích theo yêu cầu phase này, không thay SP/schema. Action history ghi sau commit nên lỗi history không hủy dữ liệu; UI thêm warning riêng. Không chạy thử trên database dùng chung/production.

### Upload/template còn cần kiểm tra

- Template `.xlsx` và header chính thức được kiểm tra bằng test đọc file thật.
- `FCommon_Import_Excel` hiện cho phép `.xlsx` và `.xls` (`:50`), trong khi `CExcel_Controller` tạo `ExcelPackage` (`CExcel_Controller.cs:22-25`). Tương thích `.xls` chưa xác minh; không đổi danh sách extension của picker dùng chung vì nó được nhiều màn hình, gồm Owner, sử dụng.
- Các nhãn `Import Excel`, `Download template`, `Close` và lỗi upload trong `FCommon_Import_Excel` dùng callback `Localize`; Unit truyền `Get_Language_Data_Field`. Owner không truyền callback nên giữ nguyên chữ mặc định, bảo toàn hành vi của module tham chiếu. Năm importer Warehouse vẫn truyền `Localize` cho labels/status/diagnostics riêng.

## D. Export Excel/CSV

- Unit Grid bind `Data="@v_arrData"`, không khai báo `OnRead`; toolbar gọi `Layout_Tool_Button(grdData)` sau nút Import riêng (`FWarehouse_Unit_List.razor:7-18`). FBase dispatcher ưu tiên custom delegate, nếu không thì `OnRead.HasDelegate` chọn `Export_Excel_Telerik_Default`, còn Grid local chọn `Export_Excel_Custom` (`FBase.razor:1018-1030`). Nhánh thực tế của Unit là `Export_Excel_Custom`.
- Grid khai Excel/CSV `AllPages="true"`; `Prepare_Excel_Export` được gắn cho native Grid Excel export (`FWarehouse_Unit_List.razor:20-24`). Toolbar common gate quyền Export, Help, và CSV đi qua `FBase.Export_CSV` với queue/loading/access log (`FBase.razor:1032-1085,1841-1860`). `Export_Excel_Custom` tạo workbook và access/file log (`FBase.razor:776-955`).
- **Rủi ro source đã xác định:** `Export_Excel_Custom` chỉ đưa `FilterDescriptors` vào `DataSourceRequest`, không gán sort descriptors; khi lấy columns, source không loại các `ColumnStates` đang ẩn (`FBase.razor:795-833`). Vì vậy chưa thể khẳng định Excel theo sort/visible columns. Toolbar custom path cũng không dựa vào `GridExcelExport.OnBeforeExport`; `Prepare_Excel_Export` không sửa custom workbook. Đây là helper dùng chung cho Owner và các FBase local grids; chỉnh nó sẽ tác động nhiều màn hình. Để nguyên trong phase này và ghi nhận cần quyết định convention export sorted/hidden.
- CSV gọi `SaveAsCsvFileAsync`; cấu hình `AllPages=true` đã có nhưng chưa kiểm tra bằng download thực tế. Excel/CSV filtering, sorting, hidden columns, Unicode, logging và permissions chưa được browser/runtime UAT.

## E. Verification

| Hạng mục | Kết quả cuối source |
|---|---|
| Build Web Danh Mục | Latest toolbar follow-up: Web Danh Mục build — exit 0, 0 errors, 21 warnings. Earlier migration build: 0 errors, 24 warnings. |
| Build web host | Earlier migration build: web host build — exit 0, 0 errors, 24 warnings; not rerun after the toolbar-only follow-up. |
| Tests | Latest follow-up: UnitExcelImportUiTests — 6 passed, 0 failed, 0 skipped. Earlier broader filtered suite before the toolbar follow-up: 251 passed, 0 failed, 0 skipped; excludes RuntimeIntegration tests requiring a database. |
| Unit architecture | Source tests cover route, common import flow, field mapping, per-row commit/rollback, cache/history, FBase CRUD hooks, permissions, delete confirmation, export dispatcher/AllPages. |
| NOT VERIFIED | Browser/visual UAT; SQL import/CRUD; thực tải Excel/CSV; filtered/sorted/hidden-column output; Excel extension `.xls`. |
| Git | Không commit/push. 12 pre-existing benchmark log deletions được giữ nguyên. |

## F. Remaining work trước khi migrate bốn module

- Chỉ migrate Loại sản phẩm, Sản phẩm, Nhà cung cấp và Kho sau khi review Unit và chốt semantics per-row versus batch transaction.
- Xác minh export convention cho sort/hidden columns. Nếu cần sửa, đánh giá callers của `FBase.Export_Excel_Custom`, trong đó có Owner, và cập nhật test/export behavior dùng chung.
- Xác minh translation dictionary thực tế cho các chuỗi mới/dynamic; `FCommon_Import_Excel` hỗ trợ callback nhưng bản dịch phụ thuộc dữ liệu language hiện có.
- Chạy browser UAT và import/export integration bằng database test riêng trước khi tuyên bố end-to-end verified. Không chạy trên database dùng chung.
- Theo dõi chi phí list SP đầy đủ trong Info/Edit; hiện controller chưa có GetByID, không tự thêm Stored Procedure trong phase này.
