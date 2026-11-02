# Refactor Nhập kho 3001 / Xuất kho 3002 — 02/11/2026

## Kết quả và phạm vi triển khai

Đã triển khai và kiểm thử local List–Edit–Info, Save nguyên phiếu = Posted, hiệu chỉnh Posted bằng delta có revision, và Import Excel dùng cùng nghiệp vụ. **Chưa deploy database chính.** Code web mới cần migration SQL trước khi vận hành với database chính.

Nguồn yêu cầu: tài liệu người dùng đính kèm “Refactor module Nhập kho (3001) và Xuất kho (3002) theo chuẩn Master Data Chủ hàng (2003)”. Không có `*.plan.md`. Không commit/push; bằng chứng RED/GREEN lưu tại báo cáo này.

## Audit và căn cứ thiết kế

- Git sạch khi bắt đầu. RTK không có trên PATH; dùng các lệnh Git/search giới hạn phạm vi.
- Nguồn SQL thực tế: `DESKTOP-NHQ7QPL\MSSQLSERVER19 / TKS_Thuc_Tap_V11_GiaiDoan2`, chỉ đọc. Trích 38 định nghĩa vào `work/xnk-save-posted-baseline-20261102/`, cùng schema-only `schema.dacpac`. Không sao chép dữ liệu nghiệp vụ sang clone.
- Đã đối chiếu ba file write-SP Chủ hàng trong `work/master-write-plan-live-audit-20261026/` với `OBJECT_DEFINITION` hiện tại: Insert, Update, Delete giống nhau sau trim. Định nghĩa hai read-SP cũng được trích trực tiếp từ DB hiện tại. Không dùng các file tham chiếu như bằng chứng duy nhất về production.
- Đã đọc List/Edit/Info Chủ hàng, controller/entity/cache và đường lưu/history của FBase. FBase gọi `Add_Data`/`Update_Data` đồng bộ, rồi ghi history riêng; do đó dùng coordinator riêng cho aggregate, giữ loading/queue, source/des/key và xử lý modal của module.
- 16 SP XNK hiện tại giữ nguyên: mỗi module có `sel_List_*`, `sel_List_*_Detail`, `ins_*_Header`, `upd_*_Header`, `del_*_Header`, `ins_*_Detail`, `upd_*_Detail`, `del_*_Detail`, với prefix F3001/F3002 và Nhap_Kho/Xuat_Kho tương ứng.
- Bốn bảng Header/Raw_Data dùng IDENTITY hiện hữu. Không đổi IDENTITY/sequence. Hai unique index toàn bảng là `UX_tbl_XNK_Nhap_Kho_So_Phieu` và `UX_tbl_XNK_Xuat_Kho_So_Phieu`, mỗi index chỉ khóa cột số phiếu tương ứng.
- FK Raw_Data → Header là CASCADE; FK kho, NCC và sản phẩm là NO_ACTION. Không sửa FK hoặc cơ chế xóa.
- Mười trigger XNK giữ nguyên: bốn `tr_Inventory_Movement_Guard_{Receipt|Issue}_Posted_{Header|Detail}` và sáu `tr_Inventory_Snapshot_Invalidate_{Receipt|Issue}_{Post|Delete|Detail}`. Trigger generation của Current cũng giữ nguyên.
- Guard Posted chỉ cho DBA/db_owner ngoại lệ; không có bypass an toàn dựa trên marker client. Nghiệp vụ correction mới chạy `WITH EXECUTE AS OWNER`, kiểm tra lại user/function/kho bên trong và chỉ cấp EXECUTE cho application role. Không cấp quyền DML mới lên ledger cho application role.
- Post hiện hành có nested BEGIN/COMMIT nhưng CATCH rollback toàn transaction. Coordinator mới **từ chối ambient transaction trước mọi write**, sở hữu transaction riêng rồi gọi Post bên trong; lỗi Post vì thế không làm rollback transaction của caller.
- Trước thay đổi, luồng thủ công là Header → Detail riêng → Draft → Post. Import gom Header/Detail trong transaction nhưng commit Draft. Save mới thống nhất hai đường này.
- Audit live ghi nhận 1 phiếu nhập Draft, 0 phiếu xuất Draft. Không tự Post/xóa các phiếu đó. Post/ledger/projection/report procedures cũ không bị ALTER.

## Luồng Create và Edit

**Create:** form giữ Header và toàn bộ Details trong bộ nhớ; Add/Delete dòng chưa ghi DB. Controller kiểm tra dữ liệu, gửi một request có GUID xuống `sp_XNK_Document_Save_Posted`. SP kiểm tra quyền thêm, scope kho, sản phẩm/NCC, số phiếu, precision số lượng/đơn giá; ghi Header + toàn bộ Details; gọi Post chính thức; ghi revision; commit. Thiếu tồn hoặc bất kỳ lỗi nào đều rollback Header/Details/Current/reservation/invalidation/revision.

**Edit:** đọc snapshot hiện tại từ DB dưới fence, kèm version và SHA-256 state token. Save đọc lại trạng thái DB, kiểm tra cả version lẫn token; không tin dữ liệu cũ do client gửi. ID Detail phải thuộc đúng phiếu và giữ nguyên San_Pham_ID. Thay sản phẩm phải xóa dòng cũ + thêm dòng mới. Created/Created_By/Posted_At được bảo toàn; update actor lấy từ session server.

Delta nhập = new − old; delta xuất = old − new. Xóa đảo tác động cũ, thêm áp dụng tác động mới; sửa giá/ghi chú/số phiếu không thay đổi quantity. Header/Details là ledger hiệu lực hiện tại; `XNK_Document_Revision.Before_State/After_State` giữ đầy đủ trạng thái cũ/mới, không bị cộng thêm vào movement.

Kho/ngày **của mọi phiếu đã lưu**, gồm Draft cũ, bị khóa ở UI và bị chặn ở SQL. Đây là giới hạn chủ động; không âm thầm chuyển phạm vi tồn hoặc lịch sử. Draft cũ được đọc và chỉ chuyển Posted khi người dùng Save rõ ràng.

Trị giá tính từ So_Luong × Don_Gia, không nhận tổng từ client. C# và SQL kiểm tra quantity > 0, tối đa 3 số lẻ; price > 0, tối đa 2 số lẻ. Một lần lưu cần ít nhất một Detail.

## Transaction, concurrency, projections và quyền

- Save lấy Root Exclusive trước mọi khóa hàng, sau đó bootstrap/group/scope fences theo thứ tự hiện hữu, trên hợp tất cả sản phẩm cũ + mới. Đây là lựa chọn đơn giản, an toàn cho correction, có đánh đổi throughput.
- Kiểm tra Current + delta ≥ Reserved và kiểm tra running balance theo ngày trong ledger từ ngày bị ảnh hưởng. Chặn sửa nhập cũ làm âm lịch sử và sửa xuất vượt khả dụng.
- Với Draft xuất cũ, release reservation trong cùng transaction rồi Post. Reservation của các phiếu khác vẫn được bảo vệ. Lỗi làm rollback cả release.
- Correction cập nhật Current, scope catalog/version, gọi movement invalidation và snapshot invalidation. Trigger snapshot vẫn chạy. Không unpost/repost phiếu Posted, không disable trigger, không dùng NOLOCK cho quyết định ghi dữ liệu.
- Rebuild daily/snapshot vẫn theo queue như hợp đồng Post hiện tại. Báo cáo có thể trả trạng thái “đang tái tạo” đến khi worker hoàn tất; không đọc projection đã invalid như thể còn đúng.
- Request_ID + Request_Hash lưu cùng revision trong transaction. Gửi lại cùng request/nội dung trả ID/version cũ; dùng lại GUID với nội dung khác bị chặn. Số phiếu còn được bảo vệ bởi unique index database.
- Hai editor cùng snapshot: chỉ một writer thành công; writer còn lại nhận lỗi bận fence hoặc stale version. Không tự ghi đè. Root hiện dùng timeout 0 nên có thể cần thử lại khi bận.
- Identity từ `r_strActive_User_Name` phía server; DTO không quyết định actor. DB kiểm tra user đang hoạt động, group/function permission và assignment kho. Đường đọc mới kiểm tra quyền View và scope kho. Internal serializer và bảng revision không mở quyền trực tiếp cho application role.

## UI và style Chủ hàng đã áp dụng

Giữ sáu component F3001/F3002 List–Info–Edit; wrapper mỏng dùng các component chung, `@inherits FBase`, Bootstrap modal-xl, TelerikGrid, Get_Language_Data_Field và naming C/m_/v_/p_. List dùng mắt tím + gear tím/Hiệu chỉnh theo quyền; bỏ UI Post và xóa chứng từ. Giữ toolbar, date filter, OnRead, sorting, pagination, export/import, Format_Grid; bổ sung nhánh grid động cùng key `grdData` của pattern Chủ hàng. Hiện DB không có cấu hình grid 3001/3002 để kiểm tra nhánh cấu hình thực tế.

Edit chỉ có một EditForm/Header/Detail và một Save. Dòng lưu sẵn hiển thị mã sản phẩm read-only; dòng mới có dropdown mã + tên + đơn vị. Lưới Rebind khi thêm/xóa hoặc đổi số lượng/đơn giá để không giữ UI cũ. Info chỉ hiển thị label và grid, tổng số lượng/trị giá, audit fields, tab lịch sử; giữ nguyên template in hiện hữu và quyền in/export. History lọc đúng mã chức năng để tránh trộn receipt/issue có cùng numeric ID.

Coordinator dùng Process_Queue/End_Queue/loading/finally, r_iAuto_ID/r_bIs_Show_Info/r_bIs_Show_Edit, r_objSource/r_objDes/r_strAdd_Key; đóng Edit và refresh List sau commit. History công ty vẫn ghi riêng, lỗi history chỉ cảnh báo; revision DB là audit bắt buộc trong transaction. Không sửa FBase, Master Data Chủ hàng hoặc shared CSS.

## File sửa/tạo

- `TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Document_Controller.cs` — controller aggregate mới, CSqlHelper + DataTable, parameter metadata hiện hữu; không ORM/dependency production mới.
- `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CWarehouseDocumentModels.cs` — Details, Request_ID, Document_Version, State_Token và giới hạn ghi chú.
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/`: sáu file F3001/F3002 List/Info/Edit; `FWarehouse_Transaction_List.razor`, `.razor.cs`; `FWarehouse_2_Warehouse_Info.razor`; `FWarehouse_3_Warehouse_Edit.razor`.
- `Database/WarehouseDocumentSavePosted.sql` — ba SP mới `sp_XNK_Document_Read_State`, `sp_XNK_Document_Edit_Get`, `sp_XNK_Document_Save_Posted`; bảng `XNK_Document_Revision` và grants/denies.
- `Database/WarehouseDocumentSavePosted.Preflight.sql` — hash 83 module phụ thuộc và kiểm tra hai unique index/đúng key. Chỉ chuẩn hóa line endings/whitespace đầu-cuối do DacFx; không bỏ qua thay đổi nội dung SQL.
- `Database/Initialize-XnkTestDatabase.ps1` — chỉ cho phép target test cố định, dựng schema từ DACPAC và khởi tạo singleton/aggregate bằng procedure thật. Đã kiểm tra cú pháp; các thao tác tương đương đã thực hiện khi dựng clone.
- `TKS_Thuc_Tap_V11_Data_Access.Tests/XnkSavePosted/`: project xUnit riêng, DocumentSaveTests, DocumentValidationTests, coverage.runsettings.
- `CompanyOriginalExcelImportTests.cs`, `WarehouseUiWorkflowTests.cs` — cập nhật contract thực sự thay đổi. Sửa helper reflection Import vốn gọi thiếu hai tham số; kiểm tra lỗi gom theo dòng và validator chung, không bỏ các ca sai template.
- Project test cũ chỉ thêm Compile Remove cho thư mục test con để tránh compile lặp. Không sửa/xóa các benchmark test bị thiếu dependency.
- `CONTEXT.md` — cập nhật thuật ngữ Save/Posted/correction; báo cáo này là evidence index.

## RED → GREEN và xác minh

RED ban đầu: project mới compile và chạy thành công test target, nhưng test Save ba dòng thất bại SQL 2812 `Could not find stored procedure 'dbo.sp_XNK_Document_Save_Posted'`. UI contract thất bại do còn Post và editor tách rời. Đây là thiếu nghiệp vụ được yêu cầu, không phải lỗi setup.

Trong E2E còn có RED thực tế: bấm Thêm dòng, List trong memory tăng nhưng Telerik vẫn hiển thị “No records available”. Sau bổ sung Rebind, dòng xuất hiện và trị giá/tổng cập nhật đúng khi nhập số.

Lệnh build đã chạy:

```powershell
dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore --verbosity quiet
```

Kết quả cuối: **0 errors, 26 warnings** (Telerik licensing/framework và warning có sẵn ở file ngoài phạm vi). Không khẳng định solution gốc build đầy đủ: solution/test project gốc tham chiếu `TKS_Thuc_Tap_V11_Benchmarks.csproj` không có trong checkout. Project test riêng chạy đúng các test bị tác động, không che dependency thiếu đó.

Lệnh GREEN/coverage đã chạy:

```powershell
$env:TKS_INTEGRATION_CONNECTION_STRING='Server=DESKTOP-NHQ7QPL\MSSQLSERVER19;Database=TKS_Thuc_Tap_V11_XNK_Test_20261102;Integrated Security=True;TrustServerCertificate=True'
dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/XnkSavePosted/XnkSavePosted.Tests.csproj --filter 'FullyQualifiedName~XnkSavePosted|FullyQualifiedName~CompanyOriginalExcelImportTests|FullyQualifiedName~Warehouse_document_uses_owner|FullyQualifiedName~Warehouse_page_only_renders' --settings TKS_Thuc_Tap_V11_Data_Access.Tests/XnkSavePosted/coverage.runsettings --collect 'Code Coverage' --logger 'console;verbosity=normal'
```

Kết quả cuối: **79 passed, 0 failed, 0 skipped**. Log: `work/xnk-save-posted-tests.txt`. Coverage controller C# mới: **97.01% lines (65/67), 92.11% branches**. Phạm vi này không đại diện coverage toàn repository, Razor hoặc SQL. Hai guard “SP trả result sai shape” chưa có fault-injection test. Cobertura: `TKS_Thuc_Tap_V11_Data_Access.Tests/XnkSavePosted/TestResults/84d2543e-ab62-465f-9784-c656b49e6182/Surface_DESKTOP-NHQ7QPL_2026-11-02.10_15_20.cobertura.xml`.

| Bảo đảm | Test/evidence | Loại | Kết quả |
|---|---|---|---|
| Header + 3 Details, Posted, current đúng, request replay không cộng lặp | Save_three_lines_posts_once_and_replay_does_not_duplicate_inventory | SQL integration | PASS |
| Detail thứ ba sai không tạo phiếu; lỗi Post sau write rollback toàn bộ và @@TRANCOUNT=0 | Invalid_third_line…; Failed_post… | SQL integration | PASS |
| Nhập 10→15→8, sửa giá quantity trung tính; stale editor không ghi đè | Receipt_corrections…; Two_editors… | SQL/concurrency | PASS |
| Xóa dòng đảo tác động, thêm dòng tạo tác động mới; không đổi San_Pham_ID của ID đã tồn tại | Added_and_removed_details… | SQL integration | PASS |
| Xuất đúng dấu delta, không vượt khả dụng; không sửa nhập cũ xuống dưới tồn đã dùng | Issue_quantity_sign…; Competing_issues… | SQL/concurrency | PASS |
| Header text không đổi stock; kho/ngày bị chặn; số phiếu trùng bị chặn | Header_only…; Warehouse_scope_warehouse_change… | SQL integration | PASS |
| Function permission, kho, actor và request hash được kiểm tra; direct Posted DML vẫn bị guard chặn | Missing_function…; Unauthorized_actor…; Application_role… | SQL/security | PASS |
| Draft/Posted cũ không có revision vẫn đọc và save đúng; reservation Draft được bảo toàn/release đúng | Legacy_documents… (2 ca); Active_draft_reservation… | SQL integration | PASS |
| Không rollback transaction của caller khi caller dùng sai contract | Ambient_transaction_is_rejected… | SQL integration | PASS |
| Snapshot trước/sau và actor được ghi đúng qua controller thật | CSharp_controller_roundtrip… | C# + SQL | PASS |
| Daily movement rebuild và period report khớp ledger sau correction | Correction_rebuilds_daily_movement… | SQL/report | PASS |
| Import workbook thật cho cả nhập/xuất ghi stock ngay cùng contract thủ công | Real_excel_import… (2 ca); CompanyOriginalExcelImportTests | Component + SQL | PASS |
| Header/detail validation và precision, null/empty/boundary được chặn | DocumentValidationTests | Unit | PASS |
| Một Edit form, không Post, mắt/gear, Info không CRUD Detail | UI_has_one…; hai UI contract cũ được cập nhật | Contract | PASS |

## E2E trình duyệt thực tế

Chạy bản web Development riêng tại `127.0.0.1:5301` với connection string override tới clone, cookie riêng, Edge headless/profile test riêng và browser context độc lập. Dùng tài khoản fixture, không dùng session/dữ liệu production. Đã dừng web và browser test sau kiểm tra. Không bypass Telerik licensing; có cảnh báo license hiện hữu.

- Receipt `XNK-E2E-20261102`: trước Save, Header count = 0. Sau một Save: Header Posted = 1, 3 Details × 10, tổng SL 30, trị giá 60; sản phẩm đầu Current 15→25. Info hiển thị label/grid và record lịch sử Thêm mới/Revision 1; không có CRUD Detail trong Info.
- Edit receipt: mã sản phẩm dòng cũ read-only, kho/ngày disabled; đổi dòng đầu 10→15, tổng SL 35/trị giá 70. Sau Save Current sản phẩm đầu 25→30, chỉ tăng 5; DB có 2 revision.
- Issue `XNK-E2E-XUAT-20261102`: Save quantity 4 ⇒ Posted = 1, Current 30→26. Edit 4→6 ⇒ Current 26→24, chỉ giảm thêm 2.
- Mắt mở Info, gear mở Hiệu chỉnh; không còn nút Post. Đã kiểm tra lỗi refresh dòng được sửa ở trên. Chưa kiểm tra thao tác in ra máy in hoặc mọi template export trong browser; template hiện hữu được giữ và build thành công.

## Database test và triển khai tiếp theo

Clone chính: `TKS_Thuc_Tap_V11_XNK_Test_20261102`, schema-only từ DB live, không copy dữ liệu nghiệp vụ. Các singleton generation/fence config được khởi tạo theo schema thực và bootstrap procedures thật. Test tạo fixture riêng có prefix XNK. Ban đầu đã thử migration bản đầu và tạo fixture trên DB test cũ `TKS_Thuc_Tap_V11_C01C02_Test_20260906`; phát hiện thiếu view/SQL lệch phiên bản rồi chuyển toàn bộ xác minh cuối sang clone mới. Các artifact/fixture test được giữ để review; không tự xóa DB test cũ.

Preflight đã PASS trên cả DB live (chỉ đọc) và clone. Postflight live xác nhận **0/4 object mới** có mặt: chưa cài table/ba SP mới vào database chính. Không deploy/push/commit.

Trước vận hành: review grants/EXECUTE AS OWNER, xác nhận đúng server/DB, chạy preflight rồi migration bằng UTF-8 (`sqlcmd -b -f 65001`), sau đó triển khai web. Nếu baseline lệch, dừng và audit lại; không thay expected hash để bỏ qua lỗi. Kiểm tra worker queue/report health và smoke test theo user/kho được phép sau deployment.

Giới hạn còn lại: Root Exclusive giảm concurrency cho toàn inventory; bận fence có thể yêu cầu retry. Kho/ngày đã lưu chưa hỗ trợ di chuyển. Full solution/test suite còn dependency Benchmark/SQL artifact thiếu sẵn; chưa có số coverage SQL/Razor hoặc benchmark tải lớn. Rebuild snapshot/daily balance vẫn cần worker hiện hữu; không tuyên bố đã kiểm thử mọi worker interleaving. Không cần sửa SP/trigger production cũ để cài đường nghiệp vụ mới.
