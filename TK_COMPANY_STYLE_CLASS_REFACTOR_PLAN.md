# Kế hoạch phân rã Data Access Warehouse theo chuẩn công ty

**Trạng thái:** `COMPANY_STYLE_CLASS_AND_METHOD_REFACTOR_PLAN_COMPLETE`  
**Phạm vi:** Phân tích và lập kế hoạch (plan-only). Không sửa production source, SQL/database, không đổi tên class/file.  
**Ngày kiểm tra:** 2026-10-12 (Asia/Saigon)

## Tóm tắt

`CWarehouseMaster_Controller` trộn 5 entity, routing theo string, Add/Update và import transaction. `CWarehouseDocument_Controller` trộn Nhập/Xuất Kho, routing `p_bIs_Receipt`, Page/Post dùng chung và import atomic. Các Store CRUD/List Fxxxx đã có tên riêng nên có thể tách ngay. Support Page/Post/shared chưa có naming rule phải giữ nguyên tên tạm thời và không được chặn decomposition CRUD/List.

Đã rà 7 class/file liên quan, 54 method khai báo, 59 Store duy nhất (64 literal references), và 225 invocation expressions từ 4 API controllers trên 28 file; 218 là caller ngoài controller, 7 là call nội bộ. 59 tên Store có khai báo trong hai SQL script đã check-in. Chữ ký DB live chưa được xác minh.

## Quy tắc support chưa có naming rule

Áp dụng `TEMPORARY_SHARED_SUPPORT` và/hoặc `BLOCKED_BY_NAMING_RULE` cho support method/Store chưa có quy ước công ty. Không suy đoán Fxxxx, không thêm `sp_page`, `sp_post`, `sp_process`, `sp_execute`, không tách SQL chỉ vì style. C# method hiện có được giữ tên tạm thời. Tên class owner tạm thời chưa được đặt; trong map dùng `TEMPORARY_SHARED_SUPPORT_OWNER (class name TBD by company precedent)`.

Page/Post Store tìm thấy:

- `sp_DM_Master_Page`, `sp_DM_Lookup_Page`
- `sp_XNK_Document_Page`, `sp_XNK_Document_Post`
- `sp_DM_Kho_User_Page`
- `sp_BC_Chi_Tiet_Nhap_Page`, `sp_BC_Chi_Tiet_Xuat_Page`
- `sp_BC_Ton_Kho_Hien_Tai_Page`, `sp_BC_Xuat_Nhap_Ton_Page`

`sp_DM_Kho_User_List_Allowed` được đánh dấu shared-support naming/ownership unresolved vì đang được dùng giữa Permission và Report. Nó không phải technical inventory Store. Tất cả các ngoại lệ support trên không chặn tách CRUD/List chuẩn.

## 1. Inventory hiện tại

| Class | Phân loại | Trách nhiệm | Store duy nhất | Caller expressions | Hành động cuối |
|---|---|---|---:|---:|---|
| `CWarehouseMaster_Controller` | `MULTI_ENTITY_CONTROLLER_TO_SPLIT` | Kho, ĐVT, Loại SP, SP, NCC; paging; imports | 27 | 74 (67 ngoài lớp) | `SPLIT_AND_RETIRE` |
| `CWarehouseDocument_Controller` | `MULTI_ENTITY_CONTROLLER_TO_SPLIT` | Nhập/Xuất header/detail, Page/Post, import atomic | 18 | 112 | `SPLIT_AND_RETIRE` |
| `CWarehousePermission_Controller` | `FEATURE_CONTROLLER` | Kho_User, lookup, quyền | 7 | 11 | `REVIEW` tên/đường dẫn; giữ feature cohesion |
| `CWarehouseReport_Controller` | `FEATURE_CONTROLLER` | BC chi tiết/tồn kho, compose lookup quyền | 8 (1 Store trùng Permission) | 28 | `KEEP` feature, tách Store access khỏi composition |
| `CWarehouse_Controller_Base` | `GENERIC_BASE_HELPER` | CSqlHelper list/page/scalar/execute wrappers | 0 cố định | shared | `KEEP` |
| `CWarehouseActionHistory_Recorder` | `ORCHESTRATION_CLASS` | Audit/error callback helper | 0 | utility | `KEEP` |
| `CWarehousePermissionFilter` | `PURE_HELPER_CLASS` | Lọc lookup trong bộ nhớ | 0 | utility | `KEEP` |

Có 32 method public trong bốn controller nghiệp vụ; tổng 54 method khai báo nếu tính private/protected helpers. Chi tiết ở class/method map.

CWarehousePermissionFilter được ghi PURE_HELPER_CLASS như một category bổ sung vì taxonomy class được yêu cầu không có loại thuần utility; method của nó vẫn là PURE_HELPER.

## 2. Company precedent và target method rule

`CDM_Chu_Hang_Controller` trong `Controller/DM` là precedent: một class cho một entity, method trùng tên Store, transaction overload cùng tên với Store insert. `CSys_Thanh_Vien_Controller` cũng có method đồng bộ và transaction overload. Chưa tìm thấy precedent `CXNK_*`, folder `XNK`/`BC`, hoặc report controller theo prefix BC.

Target cho Store chuẩn đã có tên: một public low-level method gọi đúng một Store và tên C# trùng nguyên văn Store. Caller chọn entity, Add/Update và Nhập/Xuất; không giữ routing string/boolean trong low-level Store method. Các Page/Post/shared naming exceptions tạm thời giữ method/Store name hiện hành tới khi công ty ban hành quy ước.

## 3. Master controller decomposition

Tách thành năm controller theo precedent DM:

| Entity | Controller | List | Lookup | Insert / Update / Delete |
|---|---|---|---|---|
| Kho | `CDM_Kho_Controller` | `F2009_sp_sel_List_Kho` | `F2009_sp_sel_List_Kho_Lookup` | `F2009_sp_ins_Kho`, `F2009_sp_upd_Kho`, `F2009_sp_del_Kho` |
| Đơn vị tính | `CDM_Don_Vi_Tinh_Controller` | `F2016_sp_sel_List_Don_Vi_Tinh` | `F2016_sp_sel_List_Don_Vi_Tinh_Lookup` | `F2016_sp_ins_Don_Vi_Tinh`, `F2016_sp_upd_Don_Vi_Tinh`, `F2016_sp_del_Don_Vi_Tinh` |
| Loại sản phẩm | `CDM_Loai_San_Pham_Controller` | `F2017_sp_sel_List_Loai_San_Pham` | `F2017_sp_sel_List_Loai_San_Pham_Lookup` | `F2017_sp_ins_Loai_San_Pham`, `F2017_sp_upd_Loai_San_Pham`, `F2017_sp_del_Loai_San_Pham` |
| Sản phẩm | `CDM_San_Pham_Controller` | `F2018_sp_sel_List_San_Pham` | `F2018_sp_sel_List_San_Pham_Lookup` | `F2018_sp_ins_San_Pham`, `F2018_sp_upd_San_Pham`, `F2018_sp_del_San_Pham` |
| NCC | `CDM_NCC_Controller` | `F2019_sp_sel_List_NCC` | `F2019_sp_sel_List_NCC_Lookup` | `F2019_sp_ins_NCC`, `F2019_sp_upd_NCC`, `F2019_sp_del_NCC` |

`List_Master_Async`, `List_Lookup_Async`, `Save_Master_Async`, `Delete_Master_Async`: chuyển entity và Add/Update routing lên callers. `sp_DM_Master_Page` và `sp_DM_Lookup_Page` ở temporary shared support owner chưa đặt tên; không giữ generic CRUD router chỉ vì Page chưa có naming rule.

`List_Product_Codes_Async` là helper tạo mã; `List_Product_Masters_Async` lặp Page Store. Phân loại riêng, không ép thành Store method. Chuyển về product workflow/caller hiện có sau khi xác định owner; hiện chưa có product query service precedent để đặt tên class mới.

## 4. Document decomposition

- F3001 header/detail access → target `CXNK_Nhap_Kho_Controller`; F3002 → `CXNK_Xuat_Kho_Controller`. Đây là conceptual targets người dùng đưa ra, chưa phải convention đã quan sát.
- Tách list/insert/update/delete thành method đúng tên Store; caller chọn receipt/issue, Add/Update. Không giữ `p_bIs_Receipt` trong Store method.
- `sp_XNK_Document_Page`, `sp_XNK_Document_Post`: giữ SQL và tên C# hiện có tạm thời trong owner shared support chưa đặt tên. Không để Page/Post cản F3001/F3002 split.
- Chuyển `Save_Imported_Document_Atomically_Async` về `CWarehouseTransactionImportService` đang tồn tại; giữ transaction đơn cho header và details, thứ tự, rollback và lỗi.
- Retire `CWarehouseDocument_Controller` sau khi callers, support access và import orchestration đã migrate.

## 5. Permission, Report và technical subsystem

**Permission:** sáu public methods thuộc một feature Kho_User; không cần chia nhỏ. `CDM_Kho_User_Controller` là đề xuất theo CDM precedent, vị trí/namespace cần xác minh. Tách Save insert/update; Page giữ temporary. `sp_DM_Kho_User_List_Allowed` giữ làm shared permission lookup, naming/owner unresolved. Bỏ dynamic procedure-name helpers khi các Store access đã thành method cụ thể; giữ mapper thuần.

**Report:** giữ `CWarehouseReport_Controller` làm report feature. Tách access `sp_BC_Chi_Tiet_Nhap`, `sp_BC_Chi_Tiet_Xuat`, `sp_BC_Xuat_Nhap_Ton`; các Store `_Page` giữ tạm theo support rule. `Inventory_Report_Async` hiện đọc report Store + allowed-warehouse lookup rồi map tên kho; tách low-level Store calls, giữ composition ở workflow/caller hiện tại. Chưa có report-service precedent phù hợp để đặt class mới.

Không gộp user-facing report với `sp_Inventory_*`, Snapshot, Fence, Movement, Reservation, Reconciliation. Trong bốn controller nghiệp vụ đã rà không có literal call `sp_Inventory_*`; technical subsystem nằm ngoài decomposition này.

## 6. Import và transaction

Sáu orchestrator hiện tại: năm master batch imports, một document atomic import. Dùng services đã có: `CWarehouseProductImportService`, `CWarehouseUnitImportService`, `CWarehouseCategoryImportService`, `CWarehouseSupplierImportService`, `CWarehouseKhoImportService`, `CWarehouseTransactionImportService`; không tạo layer mới chỉ để chứa chúng.

Cần 9 transaction overload trùng tên Store insert: năm master insert và bốn document insert (receipt/issue header/detail). Mỗi low-level method gọi một Store. Service giữ connection/transaction, loop, rollback, error translation. Giữ nguyên Store, tham số/thứ tự, mapping, lỗi, audit và transaction behavior.

## 7. Base helper, async và entity model

Giữ 9 protected overloads của `CWarehouse_Controller_Base` (`List_From_Procedure`, `Page_From_Procedure`, `Scalar_ID`, `Execute_Procedure`, gồm transaction overloads). Đây là generic infrastructure, được miễn method-name=Store. Giữ `CWarehouseActionHistory_Recorder`, `CWarehousePermissionFilter`, parameter factories và mapper phù hợp; không mass rewrite.

32 public methods hiện trả `Task`; đa số bọc CSqlHelper đồng bộ bằng `Task.FromResult`/`Task.CompletedTask`. Không thấy interface/override ràng buộc ở bốn controller; company sample đồng bộ. Target cuối nên dùng method đồng bộ cho Store access nếu caller API cho phép; import/UI orchestration tiếp tục async. Chuyển `await`/Task chain cùng phase vì caller impact lớn; không giữ wrapper routing vĩnh viễn.

`Entity/DM` hiện chỉ có `CDM_Chu_Hang`; chưa có model riêng Kho/ĐVT/LSP/SP/NCC. `CWarehouseMaster` generic đang được dùng rộng qua UI/import/preview/tests. Đề xuất model riêng nhưng không thực hiện trong plan. Trước khi implement phải map field, SQL params, validation/import columns, audit và result mapping; không đoán schema.

## 8. Folder, namespace, callers và retirement

`Controller/DM` + namespace `.Controller.DM` là precedent xác nhận. Master file hiện ở physical folder DM nhưng khai báo namespace `.Controller.Warehouse`; khi tách cần đồng bộ namespace/using/DI. Chưa có `XNK`/`BC` folder; `Controller/XNK` là proposal, không phải precedent. Permission có thể về DM sau khi xác nhận feature structure. Report và generic helpers giữ dưới `Controller/Warehouse` tạm thời.

Caller scan: 225 calls trên 28 file; 218 ngoài controllers, 7 nội bộ. Master 74 (67 ngoài class), Document 112, Permission 11, Report 28. Caller map có source/line/argument/candidate; bao gồm UI, service, test và benchmark. Static search không chứng minh không có reflection/external consumers.

Retire `CWarehouseMaster_Controller` sau CRUD/List callers, import services, helper owner và support isolation; retire `CWarehouseDocument_Controller` sau receipt/issue, import service, support migration. Permission class decision `REVIEW`; Report `KEEP`; base/utility `KEEP`.

## 9. Implementation phases đề xuất

Mỗi phase cần độc lập build/test được; không có build/test chạy trong plan-only này.

1. **CLASS-1:** Master read/list/lookup 5 entity controller; Page tạm ở support owner.
2. **CLASS-2:** Master insert/update/delete, caller Add/Update routing.
3. **CLASS-3:** Năm transaction insert overloads và chuyển master imports sang 5 service hiện có.
4. **CLASS-4:** Nhập Kho controller và callers.
5. **CLASS-5:** Xuất Kho controller và callers.
6. **CLASS-6:** Chuyển document transaction orchestration sang service hiện có; bốn insert overloads.
7. **CLASS-7:** Permission feature method split.
8. **CLASS-8:** Report Store access và composition.
9. **CLASS-9:** retire generic controllers sau khi không còn callers.
10. **SUPPORT-NAMING (độc lập, không chặn 1–9):** khi có company naming rule, chuyển Page/Post/shared support; giữ SQL hiện tại cho tới quyết định riêng.

## 10. Verification boundary và risks

Source/checked-in SQL evidence only; live DB signatures/deployment parity chưa xác minh. Caller scan là static direct invocation search, không bao gồm bằng chứng runtime/reflection. Các gate và mitigation ở `TK_COMPANY_STYLE_REFACTOR_RISK_MATRIX.md`. Khi implement phải giữ route, parameter order, result mapping, error, audit, import order/transaction, inventory/reservation behavior.

## 11. Artifacts và xác nhận

- `TK_COMPANY_STYLE_CLASS_REFACTOR_PLAN.md`
- `TK_COMPANY_STYLE_CLASS_MAP.csv`
- `TK_COMPANY_STYLE_METHOD_MAP.csv`
- `TK_COMPANY_STYLE_CALLER_MIGRATION_MAP.csv`
- `TK_COMPANY_STYLE_REFACTOR_RISK_MATRIX.md`

- `PLAN_ONLY_NO_SOURCE_CHANGE` — confirmed
- `PLAN_ONLY_NO_DATABASE_CHANGE` — confirmed
- `CLASS_LEVEL_DECOMPOSITION_INCLUDED` — confirmed
- `METHOD_LEVEL_DECOMPOSITION_INCLUDED` — confirmed
- `ONE_METHOD_ONE_STORE_TARGET` — confirmed for normalized Store methods; blocked support stays temporary
- `METHOD_NAME_EQUALS_STORE_TARGET` — confirmed for normalized Store methods; unresolved support keeps current names
- `NO_BUSINESS_LOGIC_CHANGE` — invariant for implementation
- `PREVIOUS_REFACTOR_WORK_PRESERVED` — confirmed; current Master physical location under DM is retained
- `COMPANY_STYLE_CLASS_AND_METHOD_REFACTOR_PLAN_COMPLETE`