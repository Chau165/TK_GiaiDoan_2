# Chuẩn hóa giao diện Grid List cho 5 Master Data

## Phạm vi

Đã chỉnh năm component Kho (2009), Đơn vị tính (2016), Loại sản phẩm (2017), Sản phẩm (2018) và Nhà cung cấp (2019). Ba form chuẩn Chủ hàng, Multi Language và Định nghĩa màu Column chỉ được dùng để đối chiếu.

## Thay đổi

- Checkbox vẫn đứng đầu lưới với độ rộng `30px`.
- Cột thao tác đổi từ `70px` thành `60px`. Khoảng cách giữa mắt và bánh răng dùng `<span>&nbsp;</span>` và chỉ xuất hiện trong nhánh có quyền Edit hoặc Delete.
- Bổ sung khoảng cách giữa icon và chữ trong hai mục Hiệu chỉnh/Xóa. Giữ nguyên icon, màu, tooltip, điều kiện quyền và handler hiện có; menu vẫn nằm trong điều kiện quyền nên không sinh menu rỗng.
- Chiều cao lưới đổi từ `calc(100vh - 190px)` thành `calc(100vh - 170px)`. Giữ nguyên Pageable, Sortable, Resizable và Reorderable.
- Thêm cột trống cuối lưới chỉ trong nhánh khai báo cột mặc định, sau cột Ghi chú. Không thêm cột vào entity. Nhánh Grid UI động vẫn dùng `FCommon_General_Col_Grid`, vốn đã tạo cột trống cuối lưới.
- Không đổi độ rộng cột dữ liệu. Export Excel vẫn dùng `Prepare_Excel_Export`, hàm loại các cột không có Field; CSV tiếp tục theo cấu hình Grid hiện hữu và mẫu công ty.

## Kiểm tra

- `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc/TKS_Thuc_Tap_V11_Web_Danh_Muc.csproj --no-restore --verbosity quiet`: thành công, 0 lỗi, 15 cảnh báo (gồm cảnh báo thiếu Telerik license).
- Kiểm tra cấu trúc từng component: 9/9 mục đạt trên cả 5 file; `git diff --check` không báo lỗi.
- Đã thử `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter 'FullyQualifiedName~MasterDataCompanyStyleMigrationTests' --verbosity quiet`. Test project không biên dịch được vì còn tham chiếu `TKS_Thuc_Tap_V11_Benchmarks`, trong khi project benchmark và các file của nó đang ở trạng thái bị xóa sẵn trong working tree. Không sửa hoặc khôi phục các file đó.
- Chưa xác minh ảnh giao diện thực tế. Chrome DevTools không kết nối; Computer Use dừng vì chính sách URL chưa được hỗ trợ cho trình duyệt Windows hiện tại. Vì vậy chiều cao, phân trang và cuộn chưa được xác nhận trực quan.

## File đã chỉnh

- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_1_Kho_List.razor`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_List.razor`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_1_Loai_San_Pham_List.razor`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_1_San_Pham_List.razor`
- `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_1_Nha_Cung_Cap_List.razor`

