> **Cập nhật Action History (2026-10-20):** các kết luận ACTION_HISTORY_SCOPE_PENDING bên dưới là snapshot của lượt migration trước. Xem [báo cáo chuẩn hóa Action History](master-data-action-history-standardization.md) để biết scope mới, verification và các bước triển khai còn lại.

# Hoàn tất migration Master Data theo chuẩn Chủ Hàng

Ngày: 2026-10-20  
Repository: `Chau165/TK_GiaiDoan_2`  
Checkout: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`

## Kết luận

Bốn module Loại sản phẩm, Nhà cung cấp, Kho và Sản phẩm đã được chuyển sang route List/Info/Edit riêng kế thừa `FBase`. Import chính dùng `FCommon_Import_Excel → FBase.Import_Excel → Import_Excel_Entry`; không còn route Master Data nào trong bốn module trỏ tới List generic để mở importer Preview/Confirm. Module Đơn vị tính được giữ làm module tham chiếu, không sửa trong lượt này.

Source và focused tests đạt. Build Web Danh Mục thành công với 0 lỗi và 12 cảnh báo. SQL integration, thao tác Import/Export trên database và browser UAT không chạy; trạng thái tương ứng là `RUNTIME_PENDING`. Info không đọc Action History do chưa có query được chứng minh an toàn theo entity/function: `ACTION_HISTORY_SCOPE_PENDING`.

## A. Modules và kết quả

| Module | Route → List / Info / Edit | Import | Export | Cache / Lookup | Trạng thái |
|---|---|---|---|---|---|
| Kho (2009) | [`Warehouse.razor`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Warehouse.razor#L1) → [`F2009_1_Kho_List`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_1_Kho_List.razor#L1), [`F2009_2_Kho_Info`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_2_Kho_Info.razor#L1), [`F2009_3_Kho_Edit`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_3_Kho_Edit.razor#L1) | `Kho_Import_Template.xlsx`; reader + validator `CWarehouseImportValidator`; mỗi dòng một transaction | FBase toolbar, Excel/CSV AllPages; static Data grid | `CDM_Kho_Controller`, `CCache_Kho`; không thay đổi KhoUser/lookup chứng từ | `IMPLEMENTED`, `BUILD_PASS`, `SOURCE_TEST_PASS`, `RUNTIME_PENDING` |
| Loại sản phẩm (2017) | [`Warehouse_Loai_San_Pham.razor`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Warehouse_Loai_San_Pham.razor#L1) → [`F2017_1_Loai_San_Pham_List`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_1_Loai_San_Pham_List.razor#L1), [`F2017_2_Loai_San_Pham_Info`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_2_Loai_San_Pham_Info.razor#L1), [`F2017_3_Loai_San_Pham_Edit`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_3_Loai_San_Pham_Edit.razor#L1) | `Loai_San_Pham_Import_Template.xlsx`; reader + validator `CCategoryImportValidator`; mỗi dòng một transaction | FBase toolbar, Excel/CSV AllPages; static Data grid | `CDM_Loai_San_Pham_Controller`, `CCache_Loai_San_Pham` | `IMPLEMENTED`, `BUILD_PASS`, `SOURCE_TEST_PASS`, `RUNTIME_PENDING` |
| Nhà cung cấp (2019) | [`Warehouse_Nha_Cung_Cap.razor`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Warehouse_Nha_Cung_Cap.razor#L1) → [`F2019_1_Nha_Cung_Cap_List`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_1_Nha_Cung_Cap_List.razor#L1), [`F2019_2_Nha_Cung_Cap_Info`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_2_Nha_Cung_Cap_Info.razor#L1), [`F2019_3_Nha_Cung_Cap_Edit`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_3_Nha_Cung_Cap_Edit.razor#L1) | `Nha_Cung_Cap_Import_Template.xlsx`; reader + validator `CSupplierImportValidator`; mỗi dòng một transaction | FBase toolbar, Excel/CSV AllPages; static Data grid | `CDM_NCC_Controller`, `CCache_NCC` | `IMPLEMENTED`, `BUILD_PASS`, `SOURCE_TEST_PASS`, `RUNTIME_PENDING` |
| Sản phẩm (2018) | [`Warehouse_San_Pham.razor`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Warehouse_San_Pham.razor#L1) → [`F2018_1_San_Pham_List`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_1_San_Pham_List.razor#L1), [`F2018_2_San_Pham_Info`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_2_San_Pham_Info.razor#L1), [`F2018_3_San_Pham_Edit`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_3_San_Pham_Edit.razor#L1) | `San_Pham_Import_Template.xlsx`; reader + `CProductImportValidator`; giữ formula check, mapping đơn vị, FK và duplicate | FBase toolbar, Excel/CSV AllPages; DTO giữ alias Grid generic `Code/Name/Related_ID/Related_ID_2` | `CDM_San_Pham_Controller`, `CCache_San_Pham`; Category/Unit đọc từ lookup controllers | `IMPLEMENTED`, `BUILD_PASS`, `SOURCE_TEST_PASS`, `RUNTIME_PENDING` |
| Đơn vị tính (2016) | [`Warehouse_Don_Vi_Tinh.razor`](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Warehouse_Don_Vi_Tinh.razor:1) → `FWarehouse_Unit_List/Info/Edit` | Dùng làm reference FBase/common import hiện có | Dùng làm reference | `CDM_Don_Vi_Tinh_Controller`, `CCache_Don_Vi_Tinh` | Reference; không sửa trong lượt này |

## B. Ma trận khác biệt và xử lý

| Module | Pattern trước migration | Pattern Chủ Hàng / Unit | Khác biệt và cách sửa | Ảnh hưởng / rủi ro |
|---|---|---|---|---|
| Kho | Route gọi `FWarehouse_1_Warehouse_List`; CRUD/import dùng switch theo loại master; importer Preview/Confirm | Route → List/Info/Edit riêng kế thừa FBase; toolbar Import riêng; `Save_Data` hooks | Tạo `F2009_1/2/3`; route dùng List riêng; dùng validator Kho hiện có và transaction insert của `CDM_Kho_Controller` | Thay đổi đường UI/CRUD; giữ nguyên controller, Stored Procedure và liên kết KhoUser. Runtime lookup/chứng từ chưa xác minh. |
| Loại sản phẩm | Cùng generic List, importer Preview/Confirm | Component riêng theo convention F2003 và Unit | Tạo `F2017_1/2/3`; giữ `CCategoryImportValidator` cho required/length/duplicate/formula; active import dùng FBase | Không thay đổi quy tắc mã/tên hay SP. Runtime DB chưa xác minh. |
| Nhà cung cấp | Cùng generic List, importer Preview/Confirm | Component riêng theo convention F2003 và Unit | Tạo `F2019_1/2/3`; giữ `CSupplierImportValidator`; active import dùng FBase | Không thay đổi quy tắc mã/tên hay SP. Runtime DB chưa xác minh. |
| Sản phẩm | Cùng generic List; nhập qua `PreviewAsync → ConfirmAsync` theo batch transaction | Component riêng; importer FBase; entity controller/cache/lookup riêng | Tạo `F2018_1/2/3`; DataTable do `CExcel_Controller` đọc; parser/validator sản phẩm tiếp tục kiểm tra formula, mapping `Ma_LSP`/tên đơn vị sang FK và duplicate; ghi từng dòng/transaction | Lỗi ghi một dòng không rollback các dòng đã commit. Runtime FK, SP và cache chưa được xác minh trên DB. |

## C. Chi tiết thay đổi

### List, Info, Edit và quyền

- Bốn route Master Data nêu trên không còn render `FWarehouse_1_Warehouse_List`; mỗi route render List mới. Các route operation và KhoUser không bị đổi.
- List dùng `TelerikGrid`, `Format_Grid`, `FCommon_General_Col_Grid`, `Layout_Tool_Button`, Help Guide và common message component. Không thêm filter Ngày Tạo vì list SP không có hợp đồng filter ngày.
- Import button và Add button cùng tuân quyền Add; Edit/Delete theo quyền tương ứng; Export kiểm tra quyền tại FBase `Layout_Tool_Button`.
- Info/Edit dùng modal/localized labels. Không có History tab/query trong Info mới. Edit gọi `FBase.Save_Data()` qua Add/Update hook, có `Check_Data`, clone source/destination, và đồng bộ cache.
- Sản phẩm nạp lookup Category và Unit từ `F2017_sp_sel_List_Loai_San_Pham_Lookup` và `F2016_sp_sel_List_Don_Vi_Tinh_Lookup`. Add mới giữ ID mặc định bằng 0 cho tới khi người dùng chọn lookup hợp lệ; validation từ chối ID không tồn tại. Info báo ID lookup thiếu thay vì thay bằng ID khác.

### Import

- Các List render `FCommon_Import_Excel`, truyền `Close_Import_Excel`, gọi `Open_Import_Excel(template)` và override `Import_Excel_Entry`. FBase kiểm tra header với template, tạo queue/loading, ghi Import Log, rồi gọi `Load_Data_Init()` để refresh Grid/lookup.
- Kho, Loại sản phẩm và Nhà cung cấp đọc worksheet gốc bằng `CWarehouse_Xlsx_Reader`/`CCategory_Xlsx_Reader`/`CSupplier_Xlsx_Reader` rồi gọi `Preview` validator tương ứng. Cách này giữ formula/header/duplicate validation trong khi DataTable vẫn do `CExcel_Controller` cung cấp cho mapping entity.
- Mỗi dòng hợp lệ mở transaction riêng, gọi transaction overload của DM Controller, commit rồi mới thêm cache; lỗi rollback dòng đó và tiếp tục dòng sau. Action History insert chạy sau commit, nếu lỗi chỉ tạo cảnh báo lịch sử.
- Sản phẩm đọc 5 cột theo sheet `SanPham` qua `CExcel_Controller.List_Range_Value_To_End`, đồng thời đọc worksheet gốc qua `CProduct_Xlsx_Reader` để validator hiện có từ chối formula và bảo toàn mapping/lookup. Dùng `Row_Number` từ validator để ghép dữ liệu với DataTable.
- Các component/service Preview/Confirm cũ được giữ trong repository. Chúng không còn được render từ bốn route Master Data mới. Không sửa Import Nhập/Xuất.

### CRUD, cache và Action History

- Controller/Stored Procedure và cache của từng entity được dùng tại List/Edit. Không sửa Data Access controller, SQL, schema hoặc contract Stored Procedure.
- FBase vẫn phụ trách common Save_Data hooks, Action History CRUD, import queue/log, loading và message behavior. Import insert từng entity ghi Action History sau commit theo cơ chế hiện tại.
- Không bật Action History reader trong Info vì shared query chỉ có `Ref_ID`; không sửa shared SP/callers theo chỉ đạo. Trạng thái: `ACTION_HISTORY_SCOPE_PENDING`.

### Export

- Bốn List có `Layout_Tool_Button`, `GridExcelExport`/`GridCsvExport` với `AllPages="true"`, file name theo module và `Prepare_Excel_Export` cho Excel.
- FBase dispatch `OnRead.HasDelegate == true` sang Telerik Default; không có bốn List mới nào khai báo `OnRead`, nên nút toolbar dùng `Export_Excel_Custom`. CSV gọi `SaveAsCsvFileAsync`. Export permission, queue và function-access log theo FBase.
- Giới hạn helper được giữ nguyên: nhánh custom áp filter descriptors nhưng không đưa SortDescriptors vào request; vòng lặp column state không loại cột ẩn bằng trạng thái visibility; `Prepare_Excel_Export` chỉ bỏ cột không có Field. Vì vậy sorting/hidden-column semantics và tên/encoding file cuối chưa được browser/runtime xác minh. Không sửa FBase dùng chung trong migration này.

### Files và legacy

- Thêm 12 Razor component cho bốn module (List/Info/Edit mỗi module), thêm test source contract cho Product vào `MasterDataCompanyStyleMigrationTests.cs`; sửa 4 route thành phần riêng.
- Cập nhật `WarehouseNavigationTests` và test Import UI để xác nhận route/common import mới; giữ test template, validator và legacy preview/confirm để tiếp tục bảo vệ code còn được giữ.
- Legacy `FWarehouse_*_Import`, Preview/Confirm service và shared Warehouse List vẫn còn trong source. Không xóa vì shared component/service còn caller/test và có thể phục vụ luồng cũ; không có route chính nào của 4 Master Data mới render các importer này.
- Không tìm thấy generator/robot call trong bốn route/List/Info/Edit vừa migrate; không sửa generator/robot.

## D. Verification

| Kiểm tra | Kết quả | Phạm vi |
|---|---|---|
| TDD RED cho route Product | `Product_uses_company_style_components_and_preserves_lookup_import_validation` thất bại vì thiếu `F2018_1_San_Pham_List.razor` | Failure đúng với implementation còn thiếu |
| TDD GREEN cho Product | Test Product pass 1/1 sau khi thêm route/List/Info/Edit | Source contract, không gọi DB |
| TDD RED/GREEN cho field alias Grid | Test source fail vì thiếu `Code = v_objItem.Ma_San_Pham`; pass 1/1 sau khi giữ alias cũ | Kiểm tra tương thích metadata generic đã dùng `CWarehouseMaster.Code/Name/Related_ID/Related_ID_2` |
| Web Danh Mục build | `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc/TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore -clp:ErrorsOnly` — **PASS**, 0 lỗi, 12 cảnh báo | Build sau thay đổi cuối |
| Focused tests | **125 passed, 0 failed, 0 skipped** | Route/source migration, template/UI import, validators, workbook structure và value mapping; không gồm DB integration |
| CodeGraph index | `codegraph sync` đồng bộ 23 file; `codegraph status` báo index up to date | Đã cập nhật sau migration |
| SQL integration / browser UAT | **NOT RUN** | Không tuyên bố runtime PASS |

Trạng thái: `IMPLEMENTED` + `BUILD_PASS` + `SOURCE_TEST_PASS` + `RUNTIME_PENDING` + `ACTION_HISTORY_SCOPE_PENDING`.

## E. Rủi ro còn lại / quyết định nghiệp vụ

1. Action History reader vẫn có nguy cơ trộn entity khi các bảng có cùng `Auto_ID`; cần task riêng để thiết kế entity/function scope và cập nhật toàn bộ caller an toàn.
2. Chưa kiểm chứng runtime DB: SP trả khóa, FK, rollback, cache sau commit, lookup mới, Import Log/Action History, quyền theo user và tác động của Stored Procedure.
3. Export FBase nhánh static Grid bỏ qua sorting và chưa lọc hidden column trong source; quyết định sửa shared helper cần được đánh giá riêng vì ảnh hưởng các caller khác.
4. CSV/Excel Unicode, tên file thực tế, filter/sorting/hidden-column behavior cần browser/runtime verification. `GridExcelExport AllPages` là markup config; toolbar Export của static Data Grid đi qua nhánh custom FBase.
5. Không có quyết định nghiệp vụ mới về việc cho phép tên/mã trùng ngoài validator convention hiện có; transaction/SP lỗi vẫn được báo theo từng dòng.
