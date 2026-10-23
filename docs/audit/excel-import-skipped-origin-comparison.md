# Audit nguồn gốc `Skipped` trong luồng Import Excel

**Ngày audit:** 2026-10-23  
**Phạm vi:** đọc tĩnh Source A và Source B; chỉ tạo file báo cáo này trong Source B. Không sửa code/SQL, không kết nối ghi DB, không chạy import, build hay test.

## Kết luận ngắn

- **FBase và Import Chủ Hàng gốc của công ty không có trạng thái/counter `Skipped` cho bản ghi trùng database.** FBase chỉ nhận Success/Error; enum import có ba trạng thái Success, Partial, Failure. Chủ Hàng lọc dòng Excel trống, nhưng bản ghi không trống vẫn được gửi tới stored procedure và lỗi phát sinh được tính Error. (`TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:318-373`; `TKS_Thuc_Tap_V11_Data_Access/Utility/Enum_Trang_Thai.cs:15-20`; `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:119-193`)
- **Trong toàn bộ Source A đã tìm được 11 handler lọc dòng Excel trống; không tìm thấy cơ chế chủ động bỏ qua bản ghi trùng khớp database trong các handler import.** Đây là khác biệt quan trọng giữa “bỏ dòng rỗng” và “skip exact duplicate”. Bằng chứng cụ thể ở bảng Phase 1.
- **Master Data và Nhập/Xuất hiện tại của Source B có cơ chế skip là phần mở rộng của dự án.** Lịch sử Source B ghi nhận lần đầu các validator/skip XNK ở commit `48007f64191661e547ed04ed1deac013016b78a1` (`upload`, 2026-10-12); không có lịch sử Git khả dụng trong Source A để đối chiếu commit công ty. Không thể kết luận từ Git metadata rằng người ghi commit là tác giả thực sự hay rằng họ dùng Codex.
- **Khuyến nghị:** giữ exact-duplicate skip để duy trì import lặp an toàn và idempotency dữ liệu; xử lý riêng việc Master đang gộp skipped vào Error khiến log báo Failure/Partial. Chỉ loại bỏ nếu yêu cầu nghiệp vụ đã quyết định ưu tiên parity hành vi với Chủ Hàng hơn khả năng re-import không lỗi.

## Phạm vi và độ tin cậy

| Mục | Quan sát |
|---|---|
| Source A | `P:\Giao đoạn 2 TK_root\Giao đoạn 2 TK\TKS_Thuc_Tap_11`; tồn tại, chỉ đọc. Không có `.git` ở root/ancestor được kiểm tra, nên không lấy được `status`, remote, HEAD hoặc lịch sử Git. |
| Source B | `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`; remote `https://github.com/Chau165/TK_GiaiDoan_2.git`; HEAD audit `98389f3509a6d120f16f57e27d9c9ef2573be9d3`; repo không shallow. Trước khi tạo báo cáo, worktree đã có 27 file tracked sửa và 6 file untracked. |
| Cách đọc kết quả | Line reference trỏ tới nội dung source hiện tại trên đĩa. Các file Master và transaction service/page liên quan ở Source B đã có thay đổi chưa commit trước audit; vì vậy ghi riêng trạng thái worktree và không quy các thay đổi đó cho HEAD. |
| Giới hạn | Chỉ xác minh mã nguồn, Git history Source B và SQL script tĩnh trong repo. Không kiểm tra định nghĩa SP của Source A, DB đang triển khai, hoặc chạy import. Kết quả runtime/production: **NOT VERIFIED**. |

## Phase 1 — Tìm kiếm toàn bộ Source A

Đã dò các từ khóa trong yêu cầu (`Skipped`, `Is_Skipped`, `Skip`, `Skipped_Count`, `Skipped_Documents`, `SKIPPED_DUPLICATE_DOCUMENT`, duplicate/ignore/already-exists và cụm tiếng Việt), sau đó kiểm tra hành vi trong handler import. Có 11 override `Import_Excel_Entry`; cả 11 đều lọc dòng rỗng dựa trên cột đầu tiên trước vòng lặp import. Không handler nào có counter/log trạng thái riêng cho các dòng này.

| File (Source A, tính từ root) | Class / method | Dòng | Điều kiện | Hành vi |
|---|---|---:|---|---|
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor` | `F2003_1_Chu_Hang_List.Import_Excel_Entry` | 130-134 | Cột 0 sau `Trim()` rỗng | Xóa dòng khỏi DataTable trước khi import; không đếm/log là Skipped. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1003_1_Thanh_Vien_List.razor` | `F1003_1_Thanh_Vien_List.Import_Excel_Entry` | 132-134 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không phải duplicate skip. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1013_1_Dinh_Nghia_Mau_Column_List.razor` | `F1013_1_Dinh_Nghia_Mau_Column_List.Import_Excel_Entry` | 125-127 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không counter riêng. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1014_1_Multilanguage_List.razor` | `F1014_1_Multilanguage_List.Import_Excel_Entry` | 129-131 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không counter riêng. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1016_1_Webhook_Config_List.razor` | `F1016_1_Webhook_Config_List.Import_Excel_Entry` | 128-130 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không counter riêng. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1019_1_Drill_Down_List.razor` | `F1019_1_Drill_Down_List.Import_Excel_Entry` | 126-128 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không counter riêng. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1020_1_Hien_An_Column_List.razor` | `F1020_1_Hien_An_Column_List.Import_Excel_Entry` | 125-127 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không counter riêng. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1027_1_Dieu_Chinh_Do_Rong_Cot_List.razor` | `F1027_1_Dieu_Chinh_Do_Rong_Cot_List.Import_Excel_Entry` | 126-128 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không counter riêng. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1036_1_Nhom_PDA_List.razor` | `F1036_1_Nhom_PDA_List.Import_Excel_Entry` | 127-129 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không counter riêng. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1044_1_Cau_Hinh_Grid_Field_List.razor` | `F1044_1_Cau_Hinh_Grid_Field_List.Import_Excel_Entry` | 173-175 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không counter riêng. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1045_1_Cau_Hinh_Grid_UI_Global_List.razor` | `F1045_1_Cau_Hinh_Grid_UI_Global_List.Import_Excel_Entry` | 405-407 | Cột 0 sau `Trim()` rỗng | Xóa dòng rỗng; không counter riêng. |

Các kết quả từ khóa khác không tạo thành cơ chế skip dòng import:

| File / vị trí | Method hoặc ngữ cảnh | Điều kiện | Hành vi và phân loại |
|---|---|---|---|
| `TKS_Thuc_Tap_V11_Web_Common/CCommonFunction.cs:113` | helper chống thêm cùng object vào danh sách tạm | Object đã có trong `v_arrTemp` | Khử trùng lặp collection trong bộ nhớ; không đọc DB và không phải import skip. |
| `TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1022_A_API_Function_Edit.razor:98-120` | `CheckData` | Mã/tên API đã tồn tại | Thêm lỗi và ném exception; đây là từ chối dữ liệu, không phải bỏ qua bản ghi. |
| `TKS_Thuc_Tap_V11_Data_Access/Entity/API/CRequest_API.cs:19` | cờ “bỏ qua thông báo” | Cấu hình thông báo | Bỏ thông báo, không bỏ qua row/document. |
| `TKS_Thuc_Tap_V11_Web_Common/CCookie.cs:30` | so khớp key cookie | Key đã có | Xử lý cookie, không liên quan import. |
| `TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CCommon_Controller.cs:175`; `.../Controller/Cache/CCache_Token.cs:103` | comment tạo STT / kiểm tra token | STT/token trùng | Không thuộc import; token trùng thì ném lỗi, không skip. |
| `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor:1054,1304` | vòng lặp cột Telerik | `data == null` | Bỏ qua metadata cột null, không bỏ qua Excel row. |

## Phase 2 — Import Chủ Hàng gốc

Luồng nhận file bắt đầu tại `FCommon_Import_Excel.razor:69-130`: file được lưu vào thư mục import (`85`) rồi gọi `OnClose` (`129-130`). `FBase` nhận file và gọi handler của màn hình (`FBase.razor:318-331`). `CExcel_Controller.List_Range_Value_To_End` đọc vùng Excel (`CExcel_Controller.cs:91-129`). Các dòng sau đây mô tả static flow, không phải một lần import chạy thật.

| Câu hỏi | Kết quả và bằng chứng |
|---|---|
| Có kiểm tra bản ghi đã tồn tại trước Insert? | Không thấy pre-check hoặc truy vấn danh sách tồn tại trong `F2003_1_Chu_Hang_List.Import_Excel_Entry` (`:119-193`). Handler map các cột rồi gọi `FQ_104_CH_sp_ins_Insert` (`:148-163`). |
| Excel trùng DB hoặc có hai dòng cùng mã? | Mỗi dòng còn lại đều được gửi tới Insert; không có nhánh `continue`/skip theo mã (`:137-163`). Nếu SP từ chối dòng trùng thì exception đi vào `catch`, tăng nội dung lỗi và rollback row (`:170-185`); cuối handler `p_iCount_Error = row count - success` (`:188`). Hai dòng cùng mã cũng được xử lý lần lượt theo cùng luồng. |
| Nếu SP phát `RAISERROR`? | Controller gọi `FQ_104_CH_sp_ins_Insert` qua `CSqlHelper.ExecuteScalar` (`CDM_Chu_Hang_Controller.cs:126-143`) và rethrow lỗi (`:138-141`). Handler bắt exception và tính Error (`F2003...razor:170-188`), không có nhánh Skip. Định nghĩa SQL của SP không có trong Source A nên việc SP cụ thể có `RAISERROR`/constraint nào là **NOT VERIFIED**; kết luận Error áp dụng nếu lời gọi ném exception. |
| Dòng Excel rỗng? | Xóa khỏi DataTable nếu cột 0 rỗng sau Trim, trước vòng lặp (`F2003...razor:128-137`). Đây là blank-row filtering, không phải duplicate skip. |
| Counter/thông báo/log riêng? | Chỉ có Success/Error (`:119-193`); không có `Skipped`. UI báo success/error (`:188-193`). FBase ghi trạng thái + Ghi_Chu chung, không ghi số skip riêng (`FBase.razor:333-359`). |

## Phase 3 — FBase, enum và Import Log gốc

- Handler mặc định là virtual no-op `Import_Excel_Entry` (`FBase.razor:271-274`). Luồng import tạo đúng hai bộ đếm `v_iCount_Success` và `v_iCount_Error` (`:318-319`), gọi handler (`:331`), rồi phân loại (`:333-346`).
- Trạng thái: Success nếu có success và không có error; Partial nếu cả hai đều lớn hơn 0; Failure nếu success bằng 0 và error lớn hơn 0; trường hợp cả hai bằng 0 cũng là Failure (`FBase.razor:333-346`). Ghi_Chu cho Partial ghi “Thành công: X row. Thất bại: Y row.” (`:338-345`).
- Enum `ETrang_Thai_Import_Excel_ID` có đúng ba trạng thái `Thanh_Cong=5`, `Thanh_Cong_1_Phan=13`, `That_Bai=1067` (`TKS_Thuc_Tap_V11_Data_Access/Utility/Enum_Trang_Thai.cs:15-20`); không có Skipped.
- `CLog_Import_Excel` có metadata, `Trang_Thai_ID`, `Ghi_Chu`, actor/function và URL, không có property/cột model `Skipped_Count` (`TKS_Thuc_Tap_V11_Data_Access/Entity/Log/CLog_Import_Excel.cs:12-25,96-116`). Controller Insert chỉ gửi các trường này tới SP (`TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs:76-101`). Không có schema DB Source A để xác minh cột vật lý, nên kết luận về log giới hạn ở model/controller.
- Nếu module chủ động bỏ mọi row nhưng không tăng Error hoặc Success, FBase nhận `0/0` và ghi Failure. Nếu module cộng các row bỏ qua vào Error, chúng bị diễn giải thành lỗi; FBase không thể phân biệt skip với failure.
- `Close_Import_Excel` gọi `Import_Excel()` khi đã có đường dẫn file (`FBase.razor:1658-1669`).

## Phase 4 — Năm Master Data trong Source B

Các page hiện tại nạp danh sách DB rồi truyền vào validator trước khi hiển thị/confirm: Kho (`F2009_1_Kho_List.razor:114`), đơn vị (`FWarehouse_Unit_List.razor:113`), loại SP (`F2017_1_Loai_San_Pham_List.razor:115`), sản phẩm (load danh sách category/unit/product `F2018_1_San_Pham_List.razor:137-152`), nhà cung cấp (`F2019_1_Nha_Cung_Cap_List.razor:115`). Các model hiện có `Is_Skipped` và danh sách skipped rows (`Entity/Warehouse/CWarehouseImportModels.cs:10,12,28-29`; các model import còn lại trong cùng file).

| Module | Điều kiện exact duplicate đặt `Is_Skipped` | Page xử lý | Khóa nghiệp vụ và phép so khớp |
|---|---|---|---|
| Kho | `CWarehouseImportValidator.cs:81-96` | `F2009_1_Kho_List.razor:131-137,219` | Cùng `Ten_Kho` đã chuẩn hóa và `Ghi_Chu`; áp dụng cả DB hiện hữu và row trước đó trong cùng file. |
| Đơn vị | `CUnitImportValidator.cs:106-121` | `FWarehouse_Unit_List.razor:131-137,225` | Cùng `Ten_Don_Vi_Tinh` và `Ghi_Chu`; DB hoặc row trước đó trong file. |
| Loại sản phẩm | `CCategoryImportValidator.cs:88-115` | `F2017_1_Loai_San_Pham_List.razor:131-137,221` | Cùng `Ma_LSP`, `Ten_LSP`, `Ghi_Chu`; DB hoặc row trước đó trong file. |
| Sản phẩm | `CProductImportValidator.cs:179-201` | `F2018_1_San_Pham_List.razor:172-177,263` | Cùng `Ma_San_Pham`, tên, category, unit và ghi chú; so category/unit theo các giá trị tương ứng ở DB hoặc row trong file. |
| Nhà cung cấp | `CSupplierImportValidator.cs:86-113` | `F2019_1_Nha_Cung_Cap_List.razor:131-137,221` | Cùng `Ma_NCC`, tên và ghi chú; DB hoặc row trước đó trong file. |

Trong từng page, exact duplicate được báo “đã có dữ liệu giống hệt… đã bỏ qua”, tăng `v_iSkipped`, rồi `continue`; ngược lại row hợp lệ mới được Insert. Cuối cùng cả năm page gán `p_iCount_Error = v_iFailed + v_iSkipped` (Kho `:219`, đơn vị `:225`, loại `:221`, sản phẩm `:263`, NCC `:221`). Vì vậy:

- Tất cả row bị skip: Success=0, Error>0 → FBase ghi **Failure**.
- Có cả row insert thành công và row skip: Success>0, Error>0 → FBase ghi **Partial**.
- UI có thể thông báo riêng số skip, nhưng Import Log của FBase không có trạng thái/counter Skip riêng; ghi chú chung diễn giải skip như thất bại.

### SQL duplicate guard của Source B

Các procedure tĩnh trong `Database/WarehouseModule.Procedures.sql` có guard bằng `THROW`; đây là validation khi insert, không phải no-op skip:

| Entity | Khóa bị từ chối khi trùng | Dòng SQL |
|---|---|---:|
| Đơn vị | Tên đơn vị | 3943-3970 (51002) |
| Loại SP | Mã hoặc tên | 4003-4030 (51011) |
| Sản phẩm | Mã SP | 5096-5123 (51021) |
| Nhà cung cấp | Mã hoặc tên | 5157-5184 (51031) |
| Kho | Tên kho | 5218-5244 (51041) |

Đây chỉ là source SQL trong repo; trạng thái deploy/runtime không được xác minh. Các file validator/page hiện có thay đổi chưa commit. Diff sẵn có cho thấy phần xử lý conflict cùng khóa nhưng nội dung khác đã bị gỡ `AddRowError`; row đó không được đánh dấu `Is_Skipped` và tiếp tục tới insert, nơi guard SQL trong source dự kiến từ chối. Không gán thời điểm/tác giả cho thay đổi chưa commit.

### Đối chiếu với Source A

Không thấy `Is_Skipped`, `Skipped_Rows`, `v_iSkipped` hay predicate exact-duplicate trong Source A khi dò toàn bộ source và các 11 import handler nêu ở Phase 1. Do Source A không có Git history, kết luận “không có trong source tham chiếu hiện tại” được xác minh bằng source tree; không thể xác định commit lịch sử công ty.

## Phase 5 — Skip chứng từ Nhập/Xuất trong Source B

Cơ chế XNK khác với Master: skip **toàn chứng từ**, không skip từng dòng sản phẩm.

- `CTransactionImportPreviewService.ValidateExistingDocuments` tìm chứng từ hiện có theo số chứng từ đã chuẩn hóa. Nếu có đúng một chứng từ trùng số, không có lỗi validation trước đó và `ExistingDocumentMatches` trả true thì gán `SKIPPED_DUPLICATE_DOCUMENT` (`TKS_Thuc_Tap_V11_Data_Access/Utility/CTransactionImportPreviewService.cs:195-235`). Trùng số nhưng khác nội dung/nhiều kết quả thành `DUPLICATE_DOCUMENT_CONFLICT` và lỗi, không skip (`:226-234`).
- So khớp hiện hữu gồm loại phiếu, kho, nhà cung cấp khi nhập, ngày, ghi chú chuẩn hóa, mã chứng từ và số lượng detail; detail được so như multiset không phụ thuộc thứ tự theo Product_ID, quantity, unit price (`CTransactionImportPreviewService.cs:238-270`). Đây là so khớp các trường được lưu/định nghĩa trong contract hiện tại, không phải mọi cột workbook: profile inbound/outbound khai báo trường header/detail ở `TKS_Thuc_Tap_V11_Data_Access/Utility/Profiles/excel-import-profiles.json:187-285`.
- Trùng header trong cùng sheet là `DUPLICATE_KEY_CONFLICT`, không phải skip (`CTransactionImportPreviewService.cs:115-121`). Dòng header/detail rỗng bị bỏ qua riêng ở preview (`:108-109,140-143`), không phải exact duplicate.
- `CWarehouseTransactionImportService` yêu cầu quyền Add, xác nhận lại bằng preview mới, rồi gặp trạng thái skip thì tăng skipped và `continue` trước khi gọi lưu receipt/issue (`TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseTransactionImportService.cs:92-120,149,179`). Do đó không mở transaction lưu riêng cho document đã skip. Luồng tổng thể vẫn có thể lưu file import/log và queue; “không transaction” ở đây chỉ nói transaction lưu phiếu.
- Model kết quả có `Imported_Documents`, `Skipped_Documents`, `Saved_Documents` (`TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CTransactionImportPreviewModels.cs:250-253`).
- Trong **worktree Source B hiện tại**, `FWarehouse_Transaction_List.razor:228-305` có wrapper ghi log `CLog_Import_Excel`; ghi chú thành công chứa `Imported: N; skipped duplicates: M.`. Màn hình hiện kết quả tại `:308-324`. `CLog_Import_Excel` vẫn chỉ có trạng thái/Ghi_Chu, không có cột skipped riêng. Wrapper này là thay đổi chưa commit; không xem là hành vi ở HEAD hoặc là code gốc công ty. Khi tất cả phiếu bị skip, wrapper hiện tại có thể ghi log thành công với Imported=0 và Skipped=N.
- Source A không có module XNK Excel tương ứng hoặc nhãn `SKIPPED_DUPLICATE_DOCUMENT`; tìm kiếm Source A toàn cây không thấy cơ chế này. Vì Source A không có Git, provenance công ty chỉ được đối chiếu bằng tree hiện tại.

## Phase 6 — Git history và provenance

### Source B

- `git rev-parse HEAD`: `98389f3509a6d120f16f57e27d9c9ef2573be9d3`; remote origin trỏ tới repo đã nêu. Trước khi ghi report, `git status --short` có 27 tracked modified và 6 untracked; các thay đổi đó được giữ nguyên.
- `git log -S'Is_Skipped'`, `-S'Skipped_Documents'`, `-S'SKIPPED_DUPLICATE_DOCUMENT'`, `-S'v_iSkipped'` và blame theo file cho thấy commit đầu trong history hiện có:

| Thành phần | Commit đầu ghi nhận | File/ý nghĩa |
|---|---|---|
| Năm `C*ImportValidator`, `Is_Skipped`, skip exact duplicate | `48007f64191661e547ed04ed1deac013016b78a1`, `2026-10-12 09:03:14 +07:00`, message `upload` | Các validator Kho/đơn vị/category/product/supplier. |
| XNK `SKIPPED_DUPLICATE_DOCUMENT`, `Skipped_Documents` | Cùng commit `48007f...`, ngày và message trên | Ba file transaction importer được đưa vào lịch sử ở commit này. |
| `v_iSkipped` trên Kho/category/product/supplier | `fd3235f78bd3c17e596324265310b2be5df02512`, `2026-10-21 09:25:21 +07:00`, message `Fix/ refactor UI` | Page-level count/hiển thị skip. |
| `v_iSkipped` trên đơn vị | `214a11c1b948780a794e1062b0f7d15ce0ba3c16`, `2026-10-21 14:30:43 +07:00`, message `Fix/ info - filter by created master data` | Page-level count/hiển thị skip. |

Git ghi author của commit `48007f...` là `Nguyễn Võ Minh Châu <nguyenvominhchau165@gmail.com>`. Đây là metadata commit, không chứng minh ai trực tiếp viết code hoặc có sử dụng AI. Có thể xác minh cơ chế được ghi vào lịch sử Source B từ commit đó; **không thể xác minh tác giả thực tế** chỉ bằng metadata.

### Source A và thay đổi chưa commit

Source A không phải Git worktree trong đường dẫn đã cung cấp, nên không có SHA/date/author của “bản gốc công ty”. Các thay đổi Source B chưa commit (bao gồm validator/page conflict behavior và XNK logging wrapper) không có commit provenance: **ORIGIN_UNVERIFIED**. Ngày file hoặc tên commit khác không dùng làm bằng chứng tác giả.

## Phase 7 — Ma trận kết luận

| Cơ chế | Source công ty A | Source dự án B | Nguồn gốc | Bằng chứng |
|---|---|---|---|---|
| Success | Có | Có | `ORIGINAL_COMPANY_CODE` | FBase dùng `v_iCount_Success` và enum Success (`A:FBase.razor:318-346`, `A:Enum_Trang_Thai.cs:15-20`; B kế thừa framework). |
| Error | Có | Có | `ORIGINAL_COMPANY_CODE` | FBase có `v_iCount_Error`; F2003 bắt lỗi Insert thành Error (`A:FBase.razor:318-346`; `A:F2003...razor:170-188`). |
| Skip dòng Excel rỗng | Có, 11 handler | Có thể có tại preview | `ORIGINAL_COMPANY_CODE` cho filter trong A; tương đồng hành vi cần xét theo từng module | A lọc cột đầu tiên trước import; XNK B cũng loại blank header/detail (`CTransactionImportPreviewService.cs:108-109,140-143`). Không phải duplicate skip. |
| Skip bản ghi Master trùng chính xác | Không thấy | Có | `PROJECT_CUSTOM_EXTENSION` | Năm validator, page count; first commit `48007f...`. |
| `Is_Skipped` / `Skipped_Rows` | Không thấy | Có | `PROJECT_CUSTOM_EXTENSION` | `CWarehouseImportModels.cs:10,12,28-29`; validators nêu ở Phase 4. |
| `SKIPPED_DUPLICATE_DOCUMENT` / `Skipped_Documents` | Không thấy | Có, cấp chứng từ | `PROJECT_CUSTOM_EXTENSION` | Preview/service/models và commit `48007f...`. |
| Import Log status framework | Có, ba trạng thái; không có skip riêng | Có, kế thừa ba trạng thái | `ORIGINAL_COMPANY_CODE` | A enum và model log (`Enum_Trang_Thai.cs:15-20`, `CLog_Import_Excel.cs:96-116`); Source B dùng cùng contract tổng quát. |
| Đếm skip trong Master Import Log | Không có | Bị gộp vào Error; không có cột riêng | `PROJECT_CUSTOM_EXTENSION` cho phép gộp skip vào Error | Các page gán `Error = Failed + Skipped`; FBase phân loại theo hai counter. |
| Ghi số skip XNK vào ghi chú Import Log | Không có cơ chế XNK tương ứng | Có trong worktree hiện tại; chưa commit | `ORIGIN_UNVERIFIED` cho phần wrapper chưa commit | `FWarehouse_Transaction_List.razor:228-305`; model log chỉ có Ghi_Chu. |
| Khử trùng lặp collection trong bộ nhớ | Có | Không liên quan | `SAME_BEHAVIOR_DIFFERENT_IMPLEMENTATION` chỉ cho helper collection | `CCommonFunction.cs:113`; không phải import/DB duplicate no-op. |

## Phase 8 — Tác động nếu bỏ Skipped

1. **Master exact duplicate:** bản ghi đã có giống hệt hiện đi qua nhánh no-op. Nếu bỏ nhánh, row sẽ gọi SP Insert; SQL source B có uniqueness guard cho tên kho, tên đơn vị, mã/tên loại SP, mã SP, mã/tên NCC (bảng ở Phase 4). Nếu DB triển khai cùng định nghĩa, SP ném lỗi và caller tính row đó là Error. Kết quả import lại sẽ chuyển từ no-op sang lỗi trùng; không có dữ liệu duplicate được insert theo các khóa được guard.
2. **Import cùng file nhiều lần:** skip hiện giữ dữ liệu idempotent ở cấp kết quả/side effect (không insert lặp); bỏ skip làm lần chạy sau tạo lỗi cho từng exact duplicate, dù dữ liệu vẫn không nhân đôi nếu guard đang được triển khai. Ở Master hiện tại, idempotency dữ liệu không đồng nghĩa log thành công: nếu mọi row skip, FBase đang ghi Failure vì skip đã bị cộng vào Error.
3. **Success/Error và log:** bỏ skip biến các exact duplicate thành Error; tổng lỗi tăng và trạng thái thường Failure/Partial. Giữ skip nhưng đổi cách log/đếm để có số Skip riêng sẽ phản ánh chính xác hơn, nhưng đó là thay đổi thiết kế khác và nằm ngoài task audit.
4. **Conflict khác nội dung:** không đồng nhất với exact duplicate. Validator hiện tại không đánh dấu conflict là skip; SQL guard từ chối các khóa được liệt kê. Thay đổi chưa commit đã gỡ một số row-error validation trước đó, nên thông báo cụ thể phụ thuộc code đang chạy và cần được xem như trạng thái worktree hiện tại.
5. **XNK:** skip toàn chứng từ khi header và danh sách detail khớp các trường persisted đang so sánh. Bỏ logic này khiến chứng từ trùng được thử lưu; kết quả phụ thuộc kiểm tra trùng tại đường lưu/DB, và có nguy cơ tạo lỗi thay vì idempotent success. Các procedure/transaction runtime cho XNK không được chạy trong audit này. Cơ chế này phục vụ nghiệp vụ khác với skip Master từng record; không nên xóa chỉ để bắt chước F2003.
6. **Khuyến nghị coding:** giữ behavior exact-duplicate skip nếu mục tiêu là import lặp an toàn. Ghi nhận rõ đây là custom extension. Nếu yêu cầu chuẩn hóa bắt buộc parity hành vi với công ty, hãy quyết định riêng trên acceptance criteria, đồng thời chấp nhận re-import báo lỗi cho duplicates hoặc thiết kế lại trạng thái/log; audit này không thay đổi code.

## Trả lời cuối

**`Skipped` cho bản ghi trùng khớp không phải cơ chế có sẵn trong FBase/Import Chủ Hàng hoặc các import handler đã tìm thấy ở source công ty tham chiếu. Đây là phần mở rộng tùy chỉnh của dự án Warehouse: Master Data được ghi nhận lần đầu trong Git Source B ở commit `48007f...` ngày 2026-10-12; skip XNK cũng được ghi nhận ở commit đó.** Source A không có Git history, nên phần “không có trong source công ty” được kết luận từ source tree hiện tại, không phải phép so sánh lịch sử commit. Bộ lọc dòng Excel trống có trong source công ty nhưng là cơ chế khác. `ORIGIN_UNVERIFIED` vẫn áp dụng cho tác giả thực tế và các thay đổi Source B chưa commit.
