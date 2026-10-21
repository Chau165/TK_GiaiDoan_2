# Deep Audit & FE Standardization theo chuẩn Chủ Hàng

> **Cập nhật trạng thái 2026-10-20:** Báo cáo này là snapshot trước khi hoàn tất bốn module còn lại. Các kết luận về route generic và Preview/Confirm đã được supersede bởi [báo cáo migration hoàn tất](company-style-master-fe-migration-complete.md).

Ngày audit: 2026-10-20  
Repository: Chau165/TK_GiaiDoan_2  
Checkout: P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11

## Phạm vi và kết luận

Audit source tập trung vào Master Data của Warehouse, đối chiếu với Chủ Hàng: list/grid, Info/Edit, shared components, CRUD, data access, cache, validation, permission, Excel import/export, CSV, loading/queue, logging, labels, template và Generator/Robot. Bốn Master route (Kho, Loại sản phẩm, Sản phẩm, Nhà cung cấp) tiếp tục dùng FWarehouse_1_Warehouse_List. Route Đơn vị tính hiện dùng FWarehouse_Unit_List riêng; KhoUser giữ route riêng trên component chung.

Phần giao diện/CRUD chung được chuẩn hóa ở task trước; lần tiếp tục này hoàn tất Localize cho năm component import, chuyển route Đơn vị tính sang List/Info/Edit kế thừa FBase và thay import đang hoạt động bằng FCommon_Import_Excel → FBase.Import_Excel → Import_Excel_Entry. Bốn importer còn lại giữ Preview/Confirm. Không đổi Stored Procedure/schema và không sửa module Chủ Hàng.

Kết luận phân biệt source, build và test. Hai build cuối không lỗi; bộ test source chọn lọc cho trạng thái cuối đạt 251/251, không gồm integration cần database. Không chạy browser UAT hoặc import/export với SQL Server. Những khác biệt chưa thể xác minh runtime và quy tắc nghiệp vụ chưa có trong source được đánh dấu C.

## Quan hệ class theo CodeGraph và source

CDM_Chu_Hang_Controller không kế thừa class hoặc implement interface nào: declaration tại TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs:14 là class độc lập. Controller gọi CSqlHelper và các Stored Procedure FQ_104_CH_* (các method list/cache bắt đầu ở dòng 17 và 49). Đây là Data Access controller, không phải base class của Razor UI.

~~~mermaid
classDiagram
    F2003_1_Chu_Hang_List --|> FBase : inherits
    F2003_2_Chu_Hang_Info --|> FBase : inherits
    F2003_3_Chu_Hang_Edit --|> FBase : inherits
    F2003_1_Chu_Hang_List ..> F2003_2_Chu_Hang_Info : renders
    F2003_1_Chu_Hang_List ..> F2003_3_Chu_Hang_Edit : renders
    F2003_1_Chu_Hang_List ..> FCommon_Import_Excel : renders
    F2003_1_Chu_Hang_List ..> CDM_Chu_Hang_Controller : list/delete/import
    F2003_3_Chu_Hang_Edit ..> CDM_Chu_Hang_Controller : load/add/update
    F2003_3_Chu_Hang_Edit ..> CCache_Chu_Hang : add/update
    CCache_Chu_Hang ..> CDM_Chu_Hang_Controller : cache load
~~~

Các cạnh inherits ở trên là quan hệ kế thừa; renders và ..> là composition/call, không phải kế thừa. F2003_1_Chu_Hang_List dùng FBase và Layout_Default_Filter (F2003_1...razor:1-3), render Info/Edit/import ở các dòng 65, 75, 80. F2003_3_Chu_Hang_Edit dùng Save_Data, Add_Data, Update_Data, Check_Data, CUtility.Clone_Entity, r_objSource/r_objDes, và cập nhật CCache_Chu_Hang (F2003_3...razor:105, 125-226). Cache là class độc lập với list/dictionary theo mã, tên viết tắt và ID (CCache_Chu_Hang.cs:13-28).

Ở Warehouse, wrapper Info/Edit lần lượt kế thừa FBase_Info và FBase_Edit; hai shared base này kế thừa FBase (FWarehouse_2_Warehouse_Master_Info.razor:1, FWarehouse_3_Warehouse_Master_Edit.razor:1, FBase_Info.razor:1-8, FBase_Edit.razor:1-8). Paging/list hỗ trợ dùng CWarehouseMasterSupport_Controller : CWarehouse_Controller_Base (CWarehouseMasterSupport_Controller.cs:7-17). CRUD từng loại vẫn đi qua các CDM_*_Controller trực tiếp; các controller CRUD này không kế thừa UI hoặc CWarehouseMasterSupport_Controller.

## Ma trận theo module

| Module | Files | Mismatch | Reference Pattern | Fix Applied | Verification | Remaining Risk |
|---|---|---|---|---|---|---|
| Kho | Warehouse.razor:5; FWarehouse_1_Warehouse_List.razor:89, 626-750, Delete_Master_Async:1063, Delete_Kho_User_Async:1131; FWarehouse_3_Warehouse_Master_Edit.razor:1-15; FWarehouse_Kho_Import.razor; CWarehouseKhoImportService.cs | Form/list cũ khác bố cục Chủ Hàng; thao tác xóa chưa guard quyền/xác nhận/queue; import dialog lặp markup; callback import chưa nạp lại lookup trong component | F2003_1 toolbar/shared base; F2003_3 form/Save_Data hooks; FBase tool/export helpers | Form 2 cột, modal modal-lg, labels localized, validation summary; handler xóa có permission/confirm; shared import shell; refresh lookup/grid sau import | Build và test workflow liên quan pass | SQL import thực tế và visual UAT chưa chạy; required fields cần nghiệp vụ xác nhận |
| Đơn vị tính | Warehouse_Don_Vi_Tinh.razor:1-5; FWarehouse_Unit_List.razor:1-264; FWarehouse_Unit_Info.razor:1-67; FWarehouse_Unit_Edit.razor:1-117; CDM_Don_Vi_Tinh_Controller.cs:11-174; CCache_Don_Vi_Tinh.cs:15-90 | Trước đây route dùng list/form/importer chung; main import dùng Preview/Confirm | F2003_1/2/3 kế thừa FBase; FCommon_Import_Excel + FBase.Import_Excel/Import_Excel_Entry; controller/cache theo entity | Route riêng List/Info/Edit; Add/Edit dùng FBase.Save_Data hooks; Import là nút riêng cạnh Thêm, bỏ heading danh sách; import gọi Store theo từng dòng; cache/history/reload; old service/component giữ lại nhưng không còn gắn vào route/parent import | Latest: build Web Danh Mục 0 lỗi; Unit tests 6/6. Earlier broader suite 251/251 | Per-row commit tạo partial success; chưa chạy SQL/browser; FBase local-grid export không áp sort/visibility theo source; controller không có GetByID |
| Loại sản phẩm | Warehouse_Loai_San_Pham.razor:5; FWarehouse_1_Warehouse_List.razor:89, 666-750, Delete_Master_Async:1063; FWarehouse_3_Warehouse_Edit.razor; FWarehouse_Category_Import.razor; CWarehouseCategoryImportService.cs | Cùng shared UI/CRUD mismatch; callback import chưa nạp lại lookup projections | Như Kho | Đã đồng bộ qua component dùng chung; giữ Preview/Confirm; refresh lookup/grid sau import | Build và test liên quan pass | Chưa xác nhận required rules; chưa chạy SQL import |
| Sản phẩm | Warehouse_San_Pham.razor:5; FWarehouse_1_Warehouse_List.razor:89, 686-750, Delete_Master_Async:1063; FWarehouse_3_Warehouse_Edit.razor; FWarehouse_Product_Import.razor; CWarehouseProductImportService.cs | Cùng shared UI/CRUD mismatch; form có lookup loại/đơn vị và callback import chưa nạp lại lookup projections | Như Kho; các dropdown dùng lookup và edit callbacks theo entity | Đã đồng bộ layout/localization/validation; import shell dùng chung; refresh lookup/grid sau import | Build và test liên quan pass | Chưa xác nhận required rules; runtime lookup/import chưa UAT |
| Nhà cung cấp | Warehouse_Nha_Cung_Cap.razor:5; FWarehouse_1_Warehouse_List.razor:89, 706-750, Delete_Master_Async:1063; FWarehouse_3_Warehouse_Edit.razor; FWarehouse_Supplier_Import.razor; CWarehouseSupplierImportService.cs | Cùng shared UI/CRUD mismatch; callback import chưa nạp lại lookup projections | Như Kho | Đã đồng bộ qua component dùng chung; giữ Preview/Confirm; refresh lookup/grid sau import | Build và test liên quan pass | Chưa xác nhận required rules; chưa chạy SQL import |
| Phân quyền kho (KhoUser) | Warehouse_Phan_Quyen.razor:5; FWarehouse_1_Warehouse_List.razor:46-83, 754-817, Delete_Kho_User_Async:1131; FWarehouse_3_Warehouse_Edit.razor:10-25; Info dùng chung | Grid/form có data shape và quyền riêng; trước đây handler xóa chưa tự guard/xác nhận/queue | Common toolbar + Info/Edit lifecycle; quyền kiểm tra cả ở handler | Form/labels/validation và Delete handler đồng bộ; không thêm importer vì đây không phải master import | Build; test workflow hiện tại pass | KhoUser không có CCache entity tương đương; SQL permission behavior chưa chạy live |

## Phân loại A/B/C và chi tiết

### A — Sai convention, cần sửa

| Finding và bằng chứng | Ảnh hưởng | Sửa đã áp dụng | Rủi ro còn lại |
|---|---|---|---|
| Wrapper Edit Master từng dùng modal-xl, khác form Chủ Hàng modal-lg (FWarehouse_3_Warehouse_Master_Edit.razor:5 trước sửa; tham chiếu F2003_3_Chu_Hang_Edit.razor:4). Info đã cùng cấu trúc ba cột với Chủ Hàng nhưng nhãn còn tĩnh và có field code/link tổng quát ngay cả khi không phù hợp loại entity (tham chiếu F2003_2_Chu_Hang_Info.razor:32,98,122; Warehouse Info trước sửa). | Modal Edit quá rộng; Info hiện các nhãn/field không sát ngữ cảnh. | Form Master chuyển sang grid responsive hai cột, modal Edit về modal-lg; Info giữ ba cột theo reference, localize label/tab/action và chỉ hiện field liên quan entity. Dùng FBase/FBase_Info/FBase_Edit, không chép logic CRUD/import. Bằng chứng hiện tại: FWarehouse_3_Warehouse_Master_Edit.razor:1-15, FWarehouse_3_Warehouse_Edit.razor:4-103, FWarehouse_2_Warehouse_Master_Info.razor:1-75. | Chưa visual UAT ở các viewport và với mọi module/lookup. |
| Năm importer ban đầu lặp markup modal. Sau migration, bốn importer đang hoạt động còn giữ Preview/Confirm; component Unit cũ được giữ làm legacy/review artifact. | UX file upload/preview dễ lệch và Unit có thể đi nhầm qua importer cũ nếu parent còn mount nhánh đó. | Tạo FWarehouse_Import_Preview_Shell.razor:1-62; bốn importer active truyền title/template/file events/busy states/Localize. Gỡ nhánh render Unit cũ khỏi FWarehouse_1_Warehouse_List; route Unit render FCommon_Import_Excel và không gọi PreviewAsync/ConfirmAsync. | Build/test source-level pass; upload thật chưa chạy. Service Unit preview cũ vẫn được giữ vì còn tests/callers. |
| Save_Master_Operation_Async, Save_Kho_User_Async, Delete_Master_Async, Delete_Kho_User_Async trước đây không bọc queue/loading; delete chỉ ẩn button theo quyền ở UI, không tự guard permission/xác nhận trong handler. | Có thể gọi handler thiếu quyền; không có xác nhận; giao diện không cho biết CRUD đang chạy và queue theo dõi thiếu. | Save/Delete hiện kiểm tra quyền phù hợp; Delete gọi confirm localized; các handler chạy Start_Loading, Process_Queue và luôn End_Queue/Stop_Loading trong finally (FWarehouse_1_Warehouse_List.razor:721-817, 1046-1159). | Confirm dùng browser window.confirm; chưa kiểm thử interactive/cancel trong browser. |
| Sáu EditForm Master có DataAnnotationsValidator nhưng không render message/summary; entities hiện chỉ khai báo StringLength, không có [Required] trong năm entity Master đã khảo sát (CDM_Kho.cs:8-20, các entity CDM_Don_Vi_Tinh, CDM_Loai_San_Pham, CDM_San_Pham, CDM_NCC). | Lỗi độ dài có thể chặn submit mà không hiện lý do. | Thêm ValidationSummary cho 6 form Master (FWarehouse_3_Warehouse_Edit.razor:10-100). Không tự đặt field bắt buộc. | Message của DataAnnotations chưa được kiểm thử localization; required rule cần chủ nghiệp vụ xác nhận. |
| Labels/status trong component Warehouse import và diagnostic component từng còn chuỗi tĩnh. | Người dùng có thể thấy một phần tiếng Việt không đi qua language lookup. | Năm component import và FImportFieldPolicyDiagnostics dùng `Localize` callback; parent truyền `Get_Language_Data_Field`. Unit active dùng FBase language helper cho status/import errors. | Source đã gọi helper; chưa xác minh dictionary có bản dịch cho mọi chuỗi động. FCommon_Import_Excel vẫn hiển thị một số label tiếng Anh theo implementation dùng chung. |
| Bốn callback importer còn trong parent cần nạp lại lookup projections sau Preview/Confirm service đã commit. | Dropdown khác trong component có thể giữ dữ liệu cũ. | Bốn callback gọi `Complete_Master_Import_Async`, tải lại lookup/rebind grid và báo riêng lỗi refresh sau commit. Unit active cập nhật `CCache_Don_Vi_Tinh` sau mỗi commit; FBase.Import_Excel gọi `Load_Data_Init` sau hook để tải lại grid. | Source tests cover callback và base reload; SQL/browser UAT chưa chạy. |

### B — Khác implementation nhưng phù hợp nghiệp vụ, có thể giữ

| Finding và source | Quyết định | Lý do / rủi ro |
|---|---|---|
| Layout_Default_Filter của Chủ Hàng được dùng tại F2003_1_Chu_Hang_List.razor:3; Warehouse Master dùng type selector, OnRead paging, dynamic grid configuration và Layout_Tool_Button(m_grdMaster, ..., Export_Master_Excel_Async) (FWarehouse_1_Warehouse_List.razor:31-54, 89). | Giữ Layout_Tool_Button cho Warehouse; không thay bằng Layout_Default_Filter. | Master route cần lựa chọn data type và custom export theo server-read state; thay toolbar mặc định có thể làm lệch contract paging/filter/export. Common menu, quyền Export, CSV, Help guide vẫn dùng FBase. |
| Chủ Hàng gọi FBase.Save_Data và override Add_Data/Update_Data, Check_Data, tracking clone; Warehouse dùng EditForm OnValidSubmit → typed callbacks → Save_Master_Operation_Async và per-type CDM_*_Controller (F2003_3...razor:105-226; FWarehouse_3_Warehouse_Edit.razor:10-103, 211-215; FWarehouse_1...razor:621-817). | Giữ typed save flow sau khi bổ sung permission, queue/loading, cache refresh và history. | Có sáu model/SP flow và Warehouse dùng CWarehouseActionHistory_Recorder; chuyển thẳng sang FBase.Save_Data sẽ đổi cách log field diff/general action history và có nguy cơ log trùng hoặc gọi hook sai. Nếu convention công ty bắt buộc generic Save_Data, cần thiết kế adapter/hook riêng và chốt quy tắc action-history trước. Không đổi Stored Procedure. |
| Bốn Warehouse Master dùng Preview/Confirm service; Owner dùng FCommon_Import_Excel → FBase.Close_Import_Excel → FBase.Import_Excel → Import_Excel_Entry. | Chuyển Unit theo xác nhận mới của người dùng; giữ bốn module còn lại nguyên flow đến khi Unit được review. | Unit route dùng template/header validation của FBase, override `Import_Excel_Entry`, transaction theo từng dòng, cache + action history. Các method `PreviewAsync/ConfirmAsync` và batch transaction cũ còn trong service/component nhưng không còn ở route/parent Unit. | Commit theo từng dòng thay atomic batch: dòng trước commit vẫn tồn tại nếu dòng sau lỗi; đây là semantics Owner, đã được người dùng yêu cầu cho module mẫu. Service cũ giữ lại do tests/callers. Không chạy import DB. |
| FWarehouse_Transaction_Import.razor và CWarehouseTransactionImportService là luồng nhập phiếu nhập/xuất riêng; service có Preview/Confirm và transaction cho chứng từ/chi tiết (CWarehouseTransactionImportService.cs:62-88,326,374). | Không đưa transaction importer vào shell Master ở lần này. | Receipt/Issue có semantics và file limits khác master import; cần audit riêng trước khi đổi. |
| FBase_Edit và FBase_Info là Razor base components mỏng: bật edit/info tracking rồi gọi FBase (FBase_Edit.razor:1-8, FBase_Info.razor:1-8). | Giữ wrapper Master tùy biến, kế thừa base hiện có; không duplicate queue/log/data-access code của base. | Modal wrapper cần điều phối sáu loại model, còn CRUD queue/cache/permission nằm ở parent handler. |

### C — Chưa đủ bằng chứng, cần xác minh

| Câu hỏi | Bằng chứng hiện tại | Xác minh tiếp theo |
|---|---|---|
| Những field nào bắt buộc theo nghiệp vụ? | Các model Master có [StringLength]; không có [Required] trong nhóm model đã kiểm tra. | Chủ nghiệp vụ xác nhận danh sách bắt buộc và thông điệp/đa ngôn ngữ rồi mới thêm attributes/validation. |
| CSV có xuất toàn bộ trang theo filter/sort và bỏ cột ẩn giống custom Excel không? | Toolbar gọi FBase.Export_CSV (FBase.razor:1037-1081); các Grid khai GridCsvExport AllPages="true" (FWarehouse_1...razor:58,90). Export Excel Master đi nhánh custom, dựng request PageSize=0 từ current filters/sorts, đọc visible column states và ghi access log (FWarehouse_1...razor:1498-1575). | Chạy UI thật với nhiều trang, filter, sort, hidden columns; so sánh CSV và XLSX sau khi chọn/ẩn cột. |
| Telerik ColumnStates reflection còn tương thích phiên bản đang deploy không? | Custom Excel lấy Column và Visible bằng reflection (FWarehouse_1...razor:1520-1545); exporter unit tests kiểm tra workbook/visible fields và empty result (WarehouseMasterExcelExportTests.cs:11-53). | Browser/runtime test trên đúng Telerik build; nếu API reflection thay đổi, chuyển sang public grid column state nếu khả dụng. |
| Translation của field tùy chỉnh/dynamic grid và DataAnnotations có đầy đủ không? | Source gọi Get_Language_Data_Field; Master có cấu hình FCommon_General_Col_Grid (FWarehouse_1...razor:115-117). | Xác minh từng ngôn ngữ/cấu hình cột đang lưu trên môi trường người dùng; quyết định localization cho validation messages. |
| Có Robot/Generator Code dành cho Warehouse Master không? | Tìm Robot Code, code generator, generator code, robot generator trong Web Danh Mục và Data Access không có kết quả; các Template được xác nhận trong luồng Excel. | Không có bằng chứng về generator trong scope đã tìm; nếu generator nằm ngoài hai project này, cần chỉ rõ path/tool. |

## Kiểm tra các yêu cầu cụ thể

### Import Excel

- Unit route: `FCommon_Import_Excel` lưu file và gọi `OnClose` (FCommon_Import_Excel.razor:69-100) → `FBase.Close_Import_Excel` ẩn modal, chuyển `CImport_Excel` rồi gọi `Import_Excel` (FBase.razor:1723-1733) → `FBase.Import_Excel` xác minh template/header, khởi tạo queue/log, gọi override và `Load_Data_Init` (FBase.razor:341-429) → `FWarehouse_Unit_List.Import_Excel_Entry` đọc `A2:B`, map `Ten_Don_Vi_Tinh/Ghi_Chu`, validate, gọi overload insert với `SqlConnection/SqlTransaction`, commit/rollback từng dòng, update cache và action history (FWarehouse_Unit_List.razor:111-236; CDM_Don_Vi_Tinh_Controller.cs:112-132).
- Import log được ghi bởi FBase (`CLog_Import_Excel`, FBase.razor:390-401); sau hook, FBase gọi `Load_Data_Init` (dòng 417) để reload list. `CCache_Don_Vi_Tinh.Add_Data` cập nhật ID/name lookup cache sau commit (CCache_Don_Vi_Tinh.cs:28-42). Số dòng thành công/lỗi và cảnh báo action history được gom tại hook.
- Chỉ Đơn vị tính đã chuyển sang flow này. Bốn importer khác vẫn dùng shell preview, validation và Confirm qua service, gồm permission check/transaction riêng; không sửa behavior của chúng ở phase này. Unit component/service Preview/Confirm cũ được giữ, nhưng không được route hoặc list chung mount làm flow chính.
- Picker chung cho phép `.xlsx` và `.xls` (FCommon_Import_Excel.razor:50); parser tạo `ExcelPackage` trong `CExcel_Controller.cs:22-25`. Template chính thức của Unit là `.xlsx` với sheet `DonViTinh` và hai header đúng thứ tự (UnitExcelImportUiTests.cs:117-128). Tương thích thực tế với `.xls` chưa kiểm tra. Modal common nhận `Localize` tùy chọn; Unit truyền `Get_Language_Data_Field`, còn Owner giữ nguyên nhãn mặc định.
- FWarehouse_Transaction_Import giữ workflow/file-size/transaction riêng. Không thay thế bằng legacy common import và cũng chưa migrate phần UI ở lượt này.

### Export Excel / CSV / Grid

- FBase.Export_Excel ưu tiên custom delegate; nếu không, OnRead dùng Export_Excel_Telerik_Default, grid local dùng Export_Excel_Custom (FBase.razor:1018-1034). Unit grid bind local `Data` và không có OnRead nên dispatch sang Export_Excel_Custom. Toolbar common gate quyền export; CSV đi `Export_CSV`, có queue/loading và access log (FBase.razor:1037-1085,1841-1860). Grid khai AllPages Excel/CSV; UI runtime chưa thử.
- `Export_Excel_Custom` tạo request từ `FilterDescriptors` nhưng source hiện không gán `SortDescriptors`; quá trình gom `ColumnStates` không lọc `Visible`. Vì vậy export Excel Unit theo helper chung chưa chứng minh giữ sort/ẩn cột như màn hình. Helper cũng được Owner và các grid local khác dùng; sửa hành vi cần đánh giá blast radius toàn bộ FBase custom-export callers. `Prepare_Excel_Export` chỉ áp dụng Telerik native export và không thay thế custom path đang được toolbar dispatch. Đây là rủi ro C cần quyết định, không được xem là đã xác minh runtime.
- Custom Master Excel tái áp dụng filter/sort với PageSize=0, lấy cột visible và title/width, tạo workbook, queue/loading và access log (FWarehouse_1_Warehouse_List.razor:1498-1575). Hai Grid Master vẫn khai GridExcelExport AllPages=true OnBeforeExport="Prepare_Excel_Export" và GridCsvExport AllPages=true; Prepare_Excel_Export loại cột không có Field (FBase.razor:1032-1035).
- Layout_Tool_Button ẩn/hiện Excel/CSV theo Is_Have_Export_Permission và giữ menu Help guide; Master thêm import tool item khi có Add permission (FBase.razor:1841-1861; FWarehouse_1...razor:50,823-828). Export Master ghi CLog_Nhat_Ky_Truy_Cap_Chuc_Nang; các thao tác ghi action history qua CWarehouseActionHistory_Recorder.TryRecord và warning khi ghi history lỗi (FWarehouse_1...razor:1498-1575,1808-1822). CSV helper ghi access log, nhưng end-to-end behavior chưa được runtime UAT.

### CRUD, cache, permission, message

- Chủ Hàng và Unit dùng `FBase.Save_Data` + hooks. Unit Edit clone record ban đầu vào `r_objSource`, gọi Add/Update Store trong hook, cập nhật cache và clone kết quả vào `r_objDes`; `r_strAdd_Key` cấp key cho lịch sử Add (FWarehouse_Unit_Edit.razor:50-111). Delete wrapper kiểm tra quyền; FBase hỏi xác nhận, chạy queue, gọi `Delete_Data_Entry`, reload list và ghi action history (FWarehouse_Unit_List.razor:96-109,257-264; FBase.razor:603-640). Info dùng layout ba cột và `FCommon_Record_Action_History_List` (FWarehouse_Unit_Info.razor:24-62).
- `CDM_Don_Vi_Tinh_Controller` là Data Access class độc lập, không kế thừa UI/controller base; gọi các Stored Procedure F2016. Nó có list/cache/lookup, insert (normal + transaction overload), update/delete, không có GetByID (CDM_Don_Vi_Tinh_Controller.cs:9-174). Info/Edit hiện load list đầy đủ rồi tìm theo ID; hợp đồng SP không đổi nhưng có chi phí đọc dư nếu danh mục lớn.
- Add/Edit permission được kiểm tra trong save handlers; Delete hiện được kiểm tra lại trong handler và yêu cầu confirm; import service kiểm tra Add ở Preview/Confirm; Export button được common toolbar gate.
- EditForm gọi OnValidSubmit; đã thêm ValidationSummary để hiện lỗi DataAnnotations. Source chưa xác định Required rules. Error CRUD đi qua Set_Error, success label đã localization.

### Loading, template, action history, Robot/Generator

- CRUD Master dùng Start_Loading/Stop_Loading cùng queue trong finally; Import shell nhận IsBusy, IsParsing, IsImporting, CanPreview, CanConfirm; FBase file read helper giới hạn và copy stream. Tên template vẫn riêng theo loại danh mục.
- CRUD/import ghi action history; Export Excel/CSV ghi access log. History Warehouse là best-effort và cảnh báo khi recorder lỗi, không rollback dữ liệu.
- Không tìm thấy Robot/Generator code trong hai project source được rà; kết luận này không mở rộng sang tool ngoài repo/project.

## Verification và worktree

| Trạng thái | Bằng chứng |
|---|---|
| Đã sửa | Shared Warehouse UI/CRUD improvements từ task trước; localization năm import components; Unit route/List/Info/Edit mới kế thừa FBase; Unit main import chuyển sang common Owner flow; nhánh legacy Unit bị gỡ khỏi parent; test architecture cập nhật. Không sửa Owner, DB/schema, Stored Procedure hoặc service legacy. |
| Build | Latest toolbar follow-up: Web Danh Mục build — exit 0, 0 errors, 21 warnings. Earlier migration builds: Web Danh Mục and web host — each exit 0, 0 errors, 24 warnings. Warnings include Telerik license and existing source warnings. |
| Test | Latest toolbar follow-up: UnitExcelImportUiTests 6 passed, 0 failed, 0 skipped. Earlier broader filtered suite before that follow-up: 251 passed, 0 failed, 0 skipped; excludes integration tests requiring a database. |
| Chưa test | Browser/visual UAT, download file thật, filtered/sorted/hidden-column export và upload workbook runtime, SQL import trên database dùng chung. Không chạy import DB. |
| Worktree lúc bắt đầu | HEAD 30bdb4f828905cf03a455effabcfdcd053d8da9b, branch main; có 12 file log benchmark dưới reports/2026-10-05_... đã bị xóa từ trước, không khôi phục và không đưa vào phần sửa. Không commit/push. |
