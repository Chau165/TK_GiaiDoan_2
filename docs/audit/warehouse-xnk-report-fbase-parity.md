# Warehouse XNK, Báo cáo và FBase parity

Ngày kiểm tra: 2026-10-21  
Phạm vi: source local hiện tại; không deploy SQL và không ghi dữ liệu nghiệp vụ.

## Trạng thái xác minh

- `SOURCE_VERIFIED`: toolbar, route, FilterRow, List/Info/Edit composition và callback được kiểm tra trong source.
- `UNIT_TEST_VERIFIED`: 49 focused tests thuộc `WarehouseUiWorkflowTests` và `WarehouseNavigationTests` đạt.
- `BUILD_VERIFIED`: project `TKS_Thuc_Tap_V11_Web_Danh_Muc` build thành công.
- `DB_READONLY_VERIFIED`: `NOT VERIFIED`; lần này không kết nối/đọc database.
- `UI_E2E_PENDING`: chưa mở ứng dụng trong browser; giao diện chưa được xác nhận bằng screenshot runtime.

## A. Toolbar

| Khu vực | Trước | Sau | Source |
|---|---|---|---|
| Nhập/Xuất | Nhánh Receipt/Issue nằm trong component tổng hợp `FWarehouse_1_Warehouse_List`; toolbar dùng overload FBase được thêm riêng cho task | Toolbar riêng `FWarehouse_Filter_Toolbar`, ngày chứng từ và Tìm kiếm bên trái; Thêm/Import/Công cụ bên phải | `FWarehouse_Filter_Toolbar.razor`, `FWarehouse_Transaction_List.razor` |
| Báo cáo theo kỳ | Toolbar callback nằm trong FBase extension | Cùng component toolbar Warehouse; có Từ ngày/Đến ngày và Tìm kiếm; Công cụ dùng `Layout_Tool_Button` | `FWarehouse_1_Warehouse_List.razor` |
| Tồn kho hiện tại | Toolbar không ngày | Cùng style toolbar, chỉ Công cụ; không thêm khoảng ngày | `FWarehouse_1_Warehouse_List.razor` |

Toolbar Warehouse dùng lại trực tiếp `FBase.Layout_Tool_Button`, `FBase.Format_Grid`, localization từ `Get_Language_Data_Field`, permission từ `r_objChuc_Nang`, và callback Export hiện hành. Kích thước DatePicker, Bootstrap columns, button classes, spacing và alignment được đặt theo markup overload gốc `Layout_Default_Filter`.

Markup đã loại khỏi luồng chứng từ: card heading/tiêu đề Nhập-Xuất, form chọn Kho phía trên grid và các nút nghiệp vụ trùng. Chọn Kho diễn ra bằng FilterRow; quyền truy cập kho vẫn được truyền qua username cho controller/SP.

## B. FilterRow và OnRead

- Chứng từ: `So_Phieu`, `Ten_Kho`, `Is_Posted`, `Ghi_Chu`; phiếu nhập có thêm `Ten_NCC`.
- OnRead lấy danh sách trong phạm vi user được phép từ controller, áp dụng ngày chứng từ, sau đó gọi `CGrid_DataSource_Utility.Apply` để lọc nhiều cột, sort và paging.
- Khi không có dropdown Kho, controller nhận warehouse ID `null`; authorization theo user vẫn được thực hiện trong data access/SP.
- Báo cáo chi tiết có thêm `Kho_ID` và `Ten_Kho` trong model/output source để FilterRow có field Kho. Bốn report query được cập nhật trong `Database/WarehouseModule.Procedures.sql`.
- Các thay đổi trong file SQL chỉ là source script; chưa deploy database.

## C. FBase reuse

Hai phần mở rộng được thêm trong lượt toolbar trước đã được gỡ khỏi `FBase.razor` sau khi thay các caller Warehouse:

- `Layout_Default_Filter<T>` nhận `RenderFragment` và callbacks.
- `Layout_Date_Range_Filter(...)`.

Không còn caller Warehouse nào phụ thuộc vào hai method này. Overload gốc `Layout_Default_Filter<T>(TelerikGrid<T>, string, string)` và `Layout_Tool_Button<T>` được giữ nguyên; so với HEAD, `FBase.razor` không còn diff. Toolbar date-range đặc thù nằm trong `FWarehouse_Filter_Toolbar.razor`, không mở rộng shared base.

## D. List–Info–Edit

| Route | List entry | Info | Edit | Trạng thái |
|---|---|---|---|---|
| `/Kho/Nhap_Kho` | `F3001_1_Nhap_Kho_List` | `F3001_2_Nhap_Kho_Info` | `F3001_3_Nhap_Kho_Edit` | Route đã tách khỏi `FWarehouse_1_Warehouse_List` |
| `/Kho/Xuat_Kho` | `F3002_1_Xuat_Kho_List` | `F3002_2_Xuat_Kho_Info` | `F3002_3_Xuat_Kho_Edit` | Route đã tách khỏi `FWarehouse_1_Warehouse_List` |

Hai List entry dùng chung `FWarehouse_Transaction_List` để tránh nhân đôi workflow Header–Detail. Các Info/Edit chuyên biệt chuyển đúng loại Receipt/Issue sang component dùng chung `FWarehouse_2_Warehouse_Info` và `FWarehouse_3_Warehouse_Edit`. `FWarehouse_1_Warehouse_List` giữ lại Master Data/Phân quyền và Báo cáo; nhánh chứng từ và các method chứng từ đã được chuyển khỏi file này.

## E. Nghiệp vụ giữ nguyên theo source

- Header/Detail Insert/Update/Delete tiếp tục gọi controller XNK hiện có.
- Post tiếp tục gọi `sp_XNK_Document_Post`; không thay bằng `FBase.Save_Data`.
- Import tiếp tục dùng `FWarehouse_Transaction_Import` với Preview/Confirm và callback hoàn tất.
- Export chứng từ tiếp tục dựng file có chi tiết, áp dụng ngày, FilterDescriptors và SortDescriptors, rồi ghi nhật ký export.
- Action History vẫn dùng `CWarehouseActionHistory_Recorder` và cơ chế SP lịch sử hiện có.
- Tồn kho hiện tại tiếp tục dùng luồng số dư đã Post; không thay inventory algorithm.

Đây là xác minh source và test, không phải kiểm thử thao tác ghi/Post trên database.

## F. Kiểm tra

- Build: `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc/TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity quiet` — thành công, 18 warnings, 0 errors.
- Focused tests: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "FullyQualifiedName~WarehouseUiWorkflowTests|FullyQualifiedName~WarehouseNavigationTests" --verbosity quiet` — 49 passed, 0 failed.
- `git diff --check` — sạch.
- CodeGraph đã sync 16 file thay đổi; trạng thái sau sync báo index up to date.
- UI/browser E2E và database runtime chưa chạy.

## G. Rủi ro còn lại

- Chưa xác minh bằng browser rằng spacing/chiều cao cuối cùng trùng screenshot Chủ Hàng trên runtime.
- Các field Kho mới cho report cần database nhận script `WarehouseModule.Procedures.sql` trước khi các output này có mặt ở runtime. Không có SQL deployment trong task này.
