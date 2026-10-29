# Phân tích Master Data Chủ Hàng (DM_Chu_Hang)

- Ngày audit: 2026-10-26
- Phạm vi: đọc source hiện tại của checkout TKS_Thuc_Tap_V11 và các file SQL/tài liệu có trong repository.
- Chế độ: READ-ONLY ANALYSIS.
- Ký hiệu kết luận:
  - **Đã xác minh**: có bằng chứng trực tiếp trong source hoặc file SQL đã đọc.
  - **Suy luận hợp lý**: hệ quả kỹ thuật nhìn thấy từ source, nhưng chưa được chứng minh bằng runtime hoặc SQL body.
  - **Chưa đủ bằng chứng**: checkout không có định nghĩa hoặc call site cần thiết để kết luận.

## A. Executive Summary

### A.1. Chủ Hàng đại diện cho gì?

**Đã xác minh:** Chủ Hàng là bản ghi master chứa danh tính và thông tin trình bày của một đơn vị sở hữu hàng trong WMS. Entity **CDM_Chu_Hang** có các nhóm dữ liệu:

- Nhận diện: **Auto_ID**, **Ma_Chu_Hang**, **Ten_Viet_Tat**, **Ten_Chu_Hang**.
- Liên hệ: **Dia_Chi**, **Dien_Thoai**, **Email**, **MST**.
- Trình bày chứng từ: **Header_Title**, **Image_URL_Thumb**, **Image_URL**, **Ghi_Chu**.
- Audit: **Created**, **Created_By**, **Created_By_Function**, **Last_Updated**, **Last_Updated_By**, **Last_Updated_By_Function**, **Last_Updated_API**.
- Trạng thái có thuộc tính **deleted**, nhưng ý nghĩa cập nhật trong database chưa thể xác minh từ checkout.

Bằng chứng: TKS_Thuc_Tap_V11_Data_Access/Entity/DM/CDM_Chu_Hang.cs:10-63,210-314.

### A.2. Vai trò trong hệ thống hiện tại

Chủ Hàng có bốn vai trò đã thấy trong source:

1. **Master CRUD:** màn hình chức năng 2003 xem, thêm, sửa, xóa và import danh sách Chủ Hàng.
2. **Lookup dùng chung:** cache cung cấp danh sách cho combo và tra cứu theo ID, mã, tên viết tắt.
3. **Nhận diện đầu ra:** báo cáo và quy tắc số phiếu có thể lấy tên, địa chỉ, email, điện thoại, logo hoặc mã Chủ Hàng.
4. **Khóa cấu hình theo chủ hàng:** API Source, Webhook và một số cấu hình hệ thống lưu Chu_Hang_ID.

Chủ Hàng vì vậy có vai trò rộng hơn một màn hình danh mục, nhưng **FQ_104_CH được gọi trực tiếp trong source hiện tại chủ yếu bởi chính module F2003 và cache Chủ Hàng**.

### A.3. Kết luận về tái sử dụng FQ_104_CH

**Đã xác minh:**

- Có 9 call site trực tiếp ngoài phần khai báo/chuỗi tên trong Controller.
- Có 8 file runtime có tham chiếu trực tiếp đến **CCache_Chu_Hang** ở các thành phần khác.
- Không thấy màn hình nghiệp vụ khác gọi trực tiếp các Store **FQ_104_CH_***.
- Không có định nghĩa **FQ_104_CH_***, **tbl_DM_Chu_Hang**, **view_DM_Chu_Hang** hoặc **Chu_Hang_ID** trong thư mục Database của checkout.

Do đó cần tách hai khái niệm:

- **Khả năng tái sử dụng:** Controller tách CRUD theo bảng và đặt tên Store ổn định, phù hợp để nhiều caller dùng chung.
- **Tái sử dụng thực tế:** bằng chứng hiện có cho thấy việc gọi trực tiếp vẫn tập trung ở F2003 và cache; các module khác chủ yếu dùng dữ liệu qua cache hoặc qua khóa cấu hình.

Không thể dùng việc chỉ có một module gọi FQ làm bằng chứng rằng thiết kế FQ sai convention. Số lượng caller không phải là điều kiện duy nhất để một CRUD theo bảng được thiết kế thành FQ.

## B. Architecture Map

### B.1. Luồng đã xác minh

~~~mermaid
flowchart TD
    P["/Danh_Muc/Chu_Hang"] --> L["F2003_1_Chu_Hang_List.razor"]
    L --> C["CDM_Chu_Hang_Controller"]
    I["F2003_2_Chu_Hang_Info.razor"] --> C
    E["F2003_3_Chu_Hang_Edit.razor"] --> C
    C --> H["CSqlHelper"]
    H --> S["FQ_104_CH_* Stored Procedure"]
    S -. "SQL body chưa có trong checkout" .-> D["tbl_DM_Chu_Hang hoặc view_DM_Chu_Hang: chưa xác minh"]
    S --> M["CDM_Chu_Hang mapping"]
    M --> L
    C --> CC["CCache_Chu_Hang"]
    CC --> CB["Combo, báo cáo, placeholder, cấu hình"]
~~~

Bằng chứng cho đường đi C#:

- Route: TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Chu_Hang.razor:1-3.
- Razor gọi Controller: các vị trí FQ_104_CH_* trong TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:101,109,115,161; TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_2_Chu_Hang_Info.razor:189; TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:126,160,194.
- Controller gọi SQL helper với CommandType.StoredProcedure: TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs:17-189 và TKS_Thuc_Tap_V11_Data_Access/DataLayer/CSqlHelper.cs:451-537.

### B.2. Quan hệ với module khác

~~~mermaid
flowchart LR
    CH["DM_Chu_Hang / CCache_Chu_Hang"]
    CH --> R["CReport_Common: header báo cáo"]
    CH --> Q["CCommon_Controller: placeholder và số phiếu"]
    CH --> H["CSys_Hien_An_Column: tên Chủ Hàng trong cấu hình"]
    CH --> Combo["FControl_Chu_Hang_Combo"]
    CH --> Config["API Source / Webhook / cấu hình theo Chu_Hang_ID"]
    CH --> Log["Log export Excel có Chu_Hang_ID"]
    X["Kho, nhập, xuất, tồn, sản phẩm, NCC"] -. "chưa có bằng chứng liên kết trực tiếp trong checkout" .-> CH
~~~

Sơ đồ sau phản ánh **bằng chứng static hiện có**, không khẳng định rằng database nền không có quan hệ ngoài repository.

## C. CRUD Flow Analysis

### C.1. Khởi tạo cache

**Đã xác minh:**

1. Khi ứng dụng web khởi động, Program.cs gọi CCache_Chu_Hang.Load_Cache_Chu_Hang() tại TKS_Thuc_Tap_V11_Web/Program.cs:84-94.
2. Cache xóa list và ba dictionary tại TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Chu_Hang.cs:21-26.
3. Cache tạo CDM_Chu_Hang_Controller, gọi FQ_104_CH_sp_sel_List_For_Cache() và thêm từng bản ghi tại CCache_Chu_Hang.cs:27-31.
4. Add_Data lập chỉ mục theo Auto_ID, Ma_Chu_Hang.ToLower() và Ten_Viet_Tat.ToLower() tại CCache_Chu_Hang.cs:34-47.
5. List_Data trả về danh sách sắp theo Ten_Viet_Tat tại CCache_Chu_Hang.cs:96-99.

**Ví dụ nghiệp vụ:** khi mở combo Chủ Hàng trong một form cấu hình, form không cần chạy lại Store danh sách mỗi lần; nó đọc dữ liệu đã nạp trong memory.

**Vai trò của FQ:** FQ_104_CH_sp_sel_List_For_Cache là điểm đọc toàn bộ master cho cache. Store này không nhận Chu_Hang_ID.

**Giới hạn:** Cache_Timer_Service.cs:13-44 không có lệnh reload CCache_Chu_Hang. Source chỉ chứng minh load lúc startup và cập nhật cục bộ sau CRUD; chưa chứng minh hành vi giữa nhiều process hoặc sau một thay đổi từ process khác.

### C.2. Xem danh sách

**Đã xác minh:**

1. Người dùng vào /Danh_Muc/Chu_Hang.
2. F2003_1_Chu_Hang_List.razor thiết lập layout/filter và hiển thị các cột master tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:1-80.
3. Load_Data gọi CDM_Chu_Hang_Controller.FQ_104_CH_sp_sel_List_By_Created tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:97-102.
4. Controller chuẩn hóa ngày đầu/cuối ngày bằng CUtility_Date.Convert_To_Dau_Ngay/Cuoi_Ngay và truyền hai ngày vào CSqlHelper.FillDataTable tại CDM_Chu_Hang_Controller.cs:17-27.
5. Mỗi DataRow được map thành CDM_Chu_Hang tại CDM_Chu_Hang_Controller.cs:29-33.

**Input:** p_dtmFrom, p_dtmTo; Controller không truyền Chu_Hang_ID.

**Ví dụ nghiệp vụ:** người quản trị lọc các Chủ Hàng được tạo trong một khoảng ngày rồi mở bản ghi chi tiết.

**Chưa đủ bằng chứng:** Store thực tế đọc table hay view, có lọc deleted hay không, có join hoặc sắp xếp SQL nào, vì SQL body không có trong checkout.

### C.3. Xem chi tiết theo ID

TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_2_Chu_Hang_Info.razor:181-189 gọi FQ_104_CH_sp_sel_Get_By_ID. Controller truyền p_iID, gọi FillDataTable, map dòng đầu tiên nếu có và trả null khi không có dòng tại CDM_Chu_Hang_Controller.cs:78-104.

**Ví dụ nghiệp vụ:** màn hình thông tin cần một bản ghi cụ thể để hiển thị liên hệ, MST, logo và lịch sử thao tác.

**Giới hạn:** không biết Store có trả bản ghi đã xóa, có kiểm tra quyền theo owner, hay có join bảng khác.

### C.4. Thêm Chủ Hàng

**Đã xác minh:**

1. Form F2003_3_Chu_Hang_Edit.razor nhận các trường thông tin tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:20-105.
2. Form tải dữ liệu hiện có nếu đang sửa; khi thêm thì tạo object mới tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:111-138.
3. Check_Data hiện có kiểm tra liên quan file upload, gồm giới hạn khoảng 1 MB và phần mở rộng tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:205-256. Không thấy kiểm tra bắt buộc hoặc trùng mã/tên trong C#.
4. Form gán người dùng và function hiện tại vào Last_Updated_By, Last_Updated_By_Function, sau đó gọi Controller tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:140-168.
5. Controller gọi FQ_104_CH_sp_ins_Insert, truyền các trường Ma_Chu_Hang, Ten_Viet_Tat, Ten_Chu_Hang, địa chỉ, liên hệ, MST, header, ghi chú, URL ảnh và hai trường audit tại CDM_Chu_Hang_Controller.cs:106-124.
6. Sau khi thành công, UI thêm object vào CCache_Chu_Hang tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:163-166.

**Ví dụ nghiệp vụ:** tạo Chủ Hàng mới để dùng trong combo, header báo cáo hoặc cấu hình tích hợp.

**Auto_ID, Created và validation SQL:** chưa xác minh được. C# chỉ nhận một long từ ExecuteScalar; không có SQL body để chứng minh cơ chế identity, sequence, kiểm tra trùng hoặc side effect.

**Transaction:** form thêm thông thường gọi overload object; import gọi overload nhận transaction. C# không chứng minh Store có tự mở transaction hay gọi Store khác.

### C.5. Import Excel

**Đã xác minh:** TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:119-193.

1. Đọc vùng A2:I.
2. Bỏ các dòng trống ở cột đầu.
3. Mỗi dòng mở connection và transaction riêng.
4. Map chín cột A:I vào các trường nghiệp vụ của Chủ Hàng, gán người thao tác.
5. Gọi FQ_104_CH_sp_ins_Insert overload transaction.
6. Commit, tăng bộ đếm thành công, thêm dữ liệu vào cache và ghi Action History.
7. Nếu lỗi, ghi lỗi theo dòng và rollback transaction của dòng đó.

**Ví dụ nghiệp vụ:** nhập nhiều Chủ Hàng từ mẫu Chu_Hang_Template.xlsx.

**Giới hạn:** mẫu Excel chỉ chứng minh mapping ở UI; điều kiện trùng, constraint và hành vi SQL vẫn chưa biết. Việc commit từng dòng chứng minh transaction boundary ở C# cho từng dòng, không chứng minh logic bên trong Store.

### C.6. Sửa Chủ Hàng

**Đã xác minh:**

1. Form tải bản ghi bằng FQ_104_CH_sp_sel_Get_By_ID tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:123-138.
2. Khi lưu, form kiểm tra file, cập nhật audit user/function và gọi FQ_104_CH_sp_upd_Update tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:171-203.
3. Controller truyền Auto_ID rồi truyền nhóm trường master và audit tại CDM_Chu_Hang_Controller.cs:146-176.
4. UI lấy bản sao đích và gọi CCache_Chu_Hang.Update_Data tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:197-202.

**Ví dụ nghiệp vụ:** đổi tên viết tắt hoặc thông tin header của một Chủ Hàng; các lookup trong cùng process có thể được cập nhật ngay.

**Chưa đủ bằng chứng:** kiểm tra trùng khi update, cập nhật Last_Updated ở SQL, ảnh hưởng đến chứng từ/tồn kho, hoặc việc Store cập nhật table/view nào.

### C.7. Xóa Chủ Hàng

**Đã xác minh:**

1. List lấy bản ghi theo key tại TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:104-112.
2. UI dùng Auto_ID để gọi FQ_104_CH_sp_del_Delete_By_ID cùng người dùng/function tại dòng 115.
3. Nếu thành công, UI gọi CCache_Chu_Hang.Delete_Data tại dòng 116.
4. Controller chỉ truyền Auto_ID, Last_Updated_By, Last_Updated_By_Function tại CDM_Chu_Hang_Controller.cs:178-189.

**Ví dụ nghiệp vụ:** ngừng một bản ghi Chủ Hàng khỏi lookup quản trị.

**Chưa đủ bằng chứng:** xóa vật lý hay soft delete, trường nào đổi, có chặn xóa khi đã dùng trong chứng từ hay không, dữ liệu lịch sử còn truy cập được không, và SQL có cascade hay kiểm tra FK hay không.

**Suy luận hợp lý:** nếu process khác chưa reload cache, lookup ở process đó có thể vẫn giữ bản ghi cũ; đây là rủi ro stale cache cần xác minh runtime, không phải kết luận có lỗi.

## D. FQ Reuse Inventory

### D.1. Gọi trực tiếp FQ_104_CH

| Chức năng | Store gọi | Cách gọi | Bằng chứng |
|---|---|---|---|
| Danh sách Chủ Hàng | FQ_104_CH_sp_sel_List_By_Created | Razor gọi Controller trực tiếp | TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:97-102 |
| Xóa: đọc key trước khi xóa | FQ_104_CH_sp_sel_Get_By_ID | Razor gọi Controller trực tiếp | TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:104-112 |
| Xóa Chủ Hàng | FQ_104_CH_sp_del_Delete_By_ID | Razor gọi Controller trực tiếp | TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:113-116 |
| Import Chủ Hàng | FQ_104_CH_sp_ins_Insert | Razor gọi overload transaction | TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor:141-165 |
| Xem chi tiết | FQ_104_CH_sp_sel_Get_By_ID | Razor gọi Controller | TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_2_Chu_Hang_Info.razor:181-189 |
| Form sửa: đọc dữ liệu | FQ_104_CH_sp_sel_Get_By_ID | Razor gọi Controller | TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:123-138 |
| Form thêm | FQ_104_CH_sp_ins_Insert | Razor gọi overload object | TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:140-168 |
| Form sửa | FQ_104_CH_sp_upd_Update | Razor gọi overload object | TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_3_Chu_Hang_Edit.razor:171-203 |
| Nạp cache | FQ_104_CH_sp_sel_List_For_Cache | Cache gọi Controller | CCache_Chu_Hang.cs:21-31 |

Bảng này cho thấy ba màn hình F2003 và cache là các caller trực tiếp đã xác minh. Không thấy caller trực tiếp khác trong static search toàn repository.

### D.2. Dùng gián tiếp qua CCache_Chu_Hang

| Thành phần | Cách dùng | Mục đích | Bằng chứng |
|---|---|---|---|
| FControl_Chu_Hang_Combo | CCache_Chu_Hang.List_Data() | Combo chọn owner, value là ID, text là Ten_Viet_Tat | TKS_Thuc_Tap_V11_Web_Control/ComboBox_Control/FControl_Chu_Hang_Combo.razor:3-31 |
| CReport_Common | Get_Data_By_ID | Header báo cáo, tên, địa chỉ, liên hệ, logo | TKS_Thuc_Tap_V11_Report/CReport_Common.cs:35-60 |
| CCommon_Controller | Get_Data_By_ID | Thay placeholder [MA_CHU_HANG], [TEN_VIET_TAT], [TEN_CHU_HANG] | TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CCommon_Controller.cs:127-177 |
| CSys_Hien_An_Column | Get_Data_By_ID | Hiển thị tên viết tắt trong cấu hình cột | TKS_Thuc_Tap_V11_Data_Access/Entity/Sys/CSys_Hien_An_Column.cs:215-233 |
| F2003_1 và F2003_3 | Add_Data, Update_Data, Delete_Data | Đồng bộ cache ngay sau CRUD | các vị trí CRUD nêu tại mục C |
| App/config components | tra cứu theo owner | Hiển thị hoặc gắn cấu hình theo Chu_Hang_ID | TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1040_3_Cau_Hinh_Component_App_Edit.razor và các call site cache |
| Export/report support | owner ID trong log hoặc header | Gắn dữ liệu xuất với Chủ Hàng | FBase.razor:924 và CReport_Common.cs:35-60 |

Đây là tái sử dụng **dữ liệu/cache**, không phải tái sử dụng trực tiếp các Store FQ_104_CH.

### D.3. Số liệu static census

- File runtime có FQ_104_CH_: 5.
- File runtime có tham chiếu CCache_Chu_Hang.: 8.
- File runtime có Chu_Hang_ID: 46.
- File dùng combo Chủ Hàng: 8.
- Match FQ_104 trong Database/: 0.
- Match owner-specific SQL trong Database/: 0.

Các số này là kết quả tìm kiếm static tại thời điểm audit; chúng không thay thế metadata hoặc runtime tracing của SQL Server.

## E. Dependency Matrix

| Chức năng/module | Vai trò của Chủ Hàng | Cách lấy dữ liệu | FQ_104_CH trực tiếp? | Mức phụ thuộc | Ảnh hưởng khi đổi/xóa |
|---|---|---|---|---|---|
| F2003 Quản lý Chủ Hàng | CRUD master | CDM_Chu_Hang_Controller | Có | Cao | Thay đổi ngay form/list/cache cùng process |
| Combo Chủ Hàng | Lookup ID/tên viết tắt | CCache_Chu_Hang | Không | Trung bình | Cache stale hoặc lookup thiếu nếu cache không đồng bộ |
| Header báo cáo | Nhận diện owner trên báo cáo | CReport_Common + cache | Không | Cao với báo cáo dùng header | Tên, địa chỉ, liên hệ, logo có thể đổi theo cache |
| Quy tắc phiếu | Thay placeholder và cấp số theo owner | CCommon_Controller, CCache_STT_Next, FQ_529 | Không gọi FQ_104 trực tiếp | Cao ở nghiệp vụ số phiếu | Mã/tên hiển thị và cấu hình số phiếu có thể bị ảnh hưởng |
| API Source theo owner | Liên kết API Source với owner | FQ_502, entity/config, combo | Không | Trung bình | Mapping cấu hình theo owner có thể không còn tra cứu được |
| Webhook theo owner | Liên kết Webhook với owner | FQ_534, entity/config, combo | Không | Trung bình | Mapping cấu hình có thể bị thiếu; dispatch runtime chưa xác minh |
| Component/app config | Khóa cấu hình theo owner | controller/cache cấu hình | Không | Thấp đến trung bình | Cấu hình fallback hoặc owner-specific có thể thay đổi |
| Ẩn cột | Hiển thị owner trong cấu hình | CCache_Chu_Hang | Không | Thấp | Tên hiển thị thay đổi |
| Log export Excel | Gắn log với owner ID hiện tại | FBase, FQ_427 | Không | Trung bình | Log mới ghi owner ID; truy vấn lịch sử phụ thuộc SQL chưa có |
| Thành viên không thuộc owner | Có method lọc theo Chu_Hang_ID | F1011_sp_sel_List_Thanh_Vien_Khong_Thuoc_Chu_Hang | Không | Chưa xác minh caller | Method tồn tại nhưng không tìm thấy caller static |
| Kho, nhập, xuất, tồn, sản phẩm, NCC | Quan hệ nghiệp vụ phổ biến nhưng cần chứng cứ | Chưa tìm được FK/call site trong checkout | Không chứng minh | Chưa xác minh | Không được suy đoán ảnh hưởng |

### E.1. API Source và Webhook

**Đã xác minh ở tầng cấu hình:**

- CSys_API_Source_Chu_Hang_Controller.cs:16-166 dùng các Store FQ_502 và truyền Chu_Hang_ID.
- CSys_Webhook_Chu_Hang_Controller.cs:16-135 dùng các Store FQ_534 và truyền Chu_Hang_ID.
- Các entity CSys_API_Source_Chu_Hang.cs và CSys_Webhook_Chu_Hang.cs có trường owner.

**Chưa đủ bằng chứng:** source hiện tại chưa chứng minh bộ phát API/Webhook runtime có lấy bản ghi DM_Chu_Hang trực tiếp bằng FQ_104_CH, cũng chưa chứng minh việc xóa owner bị chặn hay tự xóa cấu hình con.

### E.2. Phân quyền

**Đã xác minh:**

- FBase.razor:74,123,142-146 đọc owner cookie, user/function hiện tại và kiểm tra quyền xem chức năng.
- Database/ThuctapKho.ChuHangPermission.Recovery.sql:5-12,39-67 tham chiếu nhóm Chủ hàng, function code 2003 và cấp quyền chức năng.
- Đây là bằng chứng về **phân quyền theo chức năng 2003**.

**Chưa đủ bằng chứng:** list FQ_104 nhận ngày bắt đầu/kết thúc chứ không nhận Chu_Hang_ID; chưa có bằng chứng static rằng quyền được lọc theo từng Chủ Hàng hoặc từng dòng dữ liệu.

## F. Cache Analysis

### F.1. Dữ liệu và chỉ mục

CCache_Chu_Hang giữ:

- list các CDM_Chu_Hang;
- dictionary theo Auto_ID;
- dictionary theo mã Chủ Hàng viết thường;
- dictionary theo tên viết tắt viết thường.

Add_Data bỏ qua Auto_ID == 0 và ID đã tồn tại, sau đó cập nhật list và các dictionary. Source không có kiểm tra trùng mã hoặc trùng tên viết tắt ở lớp cache; điều kiện database chưa thể biết.

### F.2. Cache giúp gì?

Cache làm giảm nhu cầu gọi SQL cho các lookup lặp lại như combo, header, placeholder và tên owner trong cấu hình. Các caller đọc memory thay vì tự gọi FQ_104_CH_sp_sel_Get_By_ID.

### F.3. Đồng bộ sau CRUD

- Add: UI gọi CCache_Chu_Hang.Add_Data.
- Update: UI gọi Update_Data, hàm này xóa index cũ rồi thêm lại bản ghi mới.
- Delete: UI gọi Delete_Data.
- Startup: load toàn bộ từ FQ_104_CH_sp_sel_List_For_Cache.
- Timer: chưa thấy reload Chủ Hàng trong Cache_Timer_Service.cs.

**Suy luận hợp lý:** trong cùng process, UI F2003 cập nhật cache ngay. Trong nhiều process hoặc khi dữ liệu thay đổi ngoài UI này, cache có thể cũ cho tới khi process khởi động lại. Cần runtime test hoặc thiết kế invalidation tập trung để xác nhận mức độ ảnh hưởng.

### F.4. Cache miss trong báo cáo

CReport_Common.rptReport_Header_TC lấy object bằng ID rồi truy cập các thuộc tính của object tại CReport_Common.cs:35-60. Source không có nhánh null rõ ràng trước khi dùng object. Đây là điểm cần kiểm tra khi owner bị xóa hoặc cache không có ID, nhưng chưa đủ bằng chứng để gọi là lỗi runtime.

## G. FQ vs Fxxxx Conclusion

### G.1. Vì sao dùng FQ_104_CH?

Theo convention được cung cấp trong task và implementation đang có:

- FQ_104_CH gom thao tác chung theo master Chủ Hàng.
- Hậu tố CH làm rõ domain Chủ Hàng.
- sp_sel, sp_ins, sp_upd, sp_del mô tả loại thao tác.
- Tên method trong CDM_Chu_Hang_Controller trùng với tên Store mà helper gọi.

Mẫu này làm Controller có hợp đồng ổn định giữa C# và database. Nó phù hợp với CRUD theo bảng, dù hiện tại caller trực tiếp chủ yếu là F2003.

### G.2. FQ có được nhiều chức năng gọi thật không?

**Kết luận:** FQ_104_CH chưa được nhiều chức năng gọi trực tiếp trong checkout hiện tại. Caller trực tiếp là F2003 và CCache_Chu_Hang. Dữ liệu Chủ Hàng được nhiều thành phần dùng qua cache hoặc qua khóa owner, nên phạm vi tái sử dụng dữ liệu rộng hơn phạm vi tái sử dụng trực tiếp Store.

### G.3. Nếu nhiều chức năng thêm/sửa/xóa thì có dùng chung Store không?

Thiết kế hiện tại cho phép dùng chung:

- thêm dùng FQ_104_CH_sp_ins_Insert;
- sửa dùng FQ_104_CH_sp_upd_Update;
- xóa dùng FQ_104_CH_sp_del_Delete_By_ID;
- đọc theo ID dùng FQ_104_CH_sp_sel_Get_By_ID.

Nhưng static audit chưa tìm thấy chức năng thứ hai cũng thực hiện CRUD Chủ Hàng. Vì vậy đây là **khả năng thiết kế**, chưa phải bằng chứng tái sử dụng đa module.

### G.4. FQ chỉ có một module gọi có trái convention không?

Không thể kết luận là trái. Convention phân loại Store theo trách nhiệm và khả năng dùng chung; không yêu cầu mọi Store phải có nhiều caller ngay từ ngày đầu. Dùng FQ cho CRUD chung giúp tránh tạo các Store F2003 trùng logic nếu sau này module khác cần cùng hợp đồng.

Điều cần tránh là gắn FQ cho một workflow nghiệp vụ đặc thù có nhiều bảng, nhiều bước hoặc quy tắc riêng. Workflow đó phù hợp với Store theo function code hoặc service riêng.

### G.5. Bài học cho năm Master Data còn lại

Với Kho, Đơn vị tính, Loại sản phẩm, Sản phẩm và Nhà cung cấp:

1. Tách CRUD cơ bản theo master thành nhóm FQ có tên phản ánh bảng/domain.
2. Giữ method Controller trùng Store để dễ truy vết.
3. Tách Get_By_ID, list theo điều kiện, list cho cache và CRUD mutation.
4. Không gọi một list cho màn hình lớn nếu khác nhu cầu filter/paging/cache.
5. Đặt validation domain ở nơi đã có convention; không suy đoán rằng tên FQ tự cung cấp validation.
6. Dùng cache chỉ cho lookup ổn định, có chiến lược cập nhật sau mutation và khi chạy nhiều process.
7. Tách Store nghiệp vụ đặc thù, liên bảng hoặc workflow chứng từ thành nhóm function-specific khi cần.
8. Đếm caller sau khi truy vết để phân biệt thiết kế có thể dùng chung với reuse thực tế.

### G.6. Phần chung và phần đặc thù của Chủ Hàng

| Phần | Phân loại | Lý do |
|---|---|---|
| List/Get/Insert/Update/Delete master | Xử lý chung theo bảng | Controller và Store có hợp đồng CRUD rõ |
| Load cache và tra cứu ID/mã/tên | Xử lý dùng chung | Cache phục vụ nhiều lookup |
| Header báo cáo, placeholder, cấp số | Nghiệp vụ dùng owner | Phụ thuộc ngữ cảnh report/quy tắc phiếu |
| API Source/Webhook mapping | Cấu hình tích hợp | Có Store/controller riêng FQ_502/FQ_534 |
| Import Excel | Workflow UI đặc thù | Có template, transaction từng dòng, Action History |
| Chặn xóa khi đã có chứng từ | Chưa xác minh | Cần SQL body hoặc DB metadata/runtime |

## H. Lessons for Other Master Data

Mẫu triển khai có thể dùng làm checklist:

- **Contract:** entity, Controller method và Store phải cùng biểu diễn một domain.
- **Read paths:** tách list cho màn hình, get theo ID và list cho cache.
- **Mutation paths:** ghi rõ audit user/function và transaction boundary.
- **Cache contract:** xác định key, hành vi duplicate, reload và invalidation.
- **Caller audit:** tìm cả direct Store call, cache call và chỉ sử dụng foreign key.
- **Security:** tách quyền function khỏi row-level ownership; chỉ kết luận row security khi có query/filter/permission evidence.
- **Database evidence:** xác minh table/view, soft delete, unique constraint, FK, cascade và Store body từ SQL Server hoặc DDL tương ứng.
- **Scope:** không dùng prefix FQ để suy ra rằng mọi nghiệp vụ liên quan master đều phải đi qua một Store duy nhất.

## I. Unverified Findings và giới hạn bằng chứng

### I.1. Chưa thể xác minh từ checkout

1. Nội dung SQL thật của sáu Store FQ_104_CH.
2. tbl_DM_Chu_Hang và view_DM_Chu_Hang có tồn tại ở database nền hay không.
3. Store đọc table hay view, các cột SELECT, JOIN, WHERE, ORDER BY.
4. Cơ chế tạo Auto_ID, thiết lập Created, Last_Updated.
5. Validation trùng Ma_Chu_Hang, Ten_Viet_Tat, MST hoặc tên.
6. Xóa vật lý hay soft delete; ý nghĩa thực tế của thuộc tính deleted.
7. FK/cascade và việc chặn xóa owner đã được dùng trong chứng từ.
8. Quan hệ thật với Kho, Sản phẩm, Nhà cung cấp, Phiếu nhập, Phiếu xuất và Tồn kho.
9. Job, service hoặc database trigger cập nhật/cache owner ngoài source đã đọc.
10. Runtime dispatch của API Source/Webhook và mức độ ảnh hưởng khi owner thay đổi.
11. Row-level authorization theo Chu_Hang_ID.
12. Hành vi cache giữa nhiều instance web.

### I.2. Điều có thể xác minh bằng read-only SQL khi có đúng database

Các truy vấn sau chỉ là khung kiểm tra, không được chạy trong audit này:

- Tìm procedure và module definition trong sys.procedures, sys.sql_modules.
- Tìm object phụ thuộc bằng sys.sql_expression_dependencies.
- Tìm table/view và cột Chu_Hang_ID trong sys.tables, sys.views, sys.columns.
- Tìm FK bằng sys.foreign_keys, sys.foreign_key_columns.
- Tìm unique index/constraint trên mã và tên viết tắt.
- Kiểm tra trigger, identity và default constraint của bảng Chủ Hàng.
- Đếm dữ liệu trước/sau chỉ khi có một mục tiêu read-only rõ ràng; không chạy mutation để kiểm chứng.

Không ghi credential, secret hoặc token vào báo cáo.

## J. Kết luận trả lời sáu câu hỏi chính

1. **Công dụng và vai trò:** Chủ Hàng là master identity của owner, dùng cho CRUD, lookup/cache, header/placeholder/số phiếu và gắn owner vào một số cấu hình.
2. **Chức năng đang sử dụng:** F2003 quản lý trực tiếp; combo, báo cáo, common placeholder, cấu hình component/ẩn cột, export log và các cấu hình API/Webhook dùng gián tiếp hoặc lưu owner ID.
3. **Chức năng thực sự gọi FQ_104_CH:** F2003_1_Chu_Hang_List, F2003_2_Chu_Hang_Info, F2003_3_Chu_Hang_Edit và CCache_Chu_Hang.
4. **Mức tái sử dụng:** direct Store reuse hẹp; cache/data reuse rộng hơn. Chưa có bằng chứng module nghiệp vụ khác gọi trực tiếp FQ_104_CH.
5. **Chưa thể xác minh:** SQL body, table/view, validation, soft delete, FK/cascade, quan hệ chứng từ/tồn kho, row security và runtime cache invalidation.
6. **Báo cáo:** file này trong docs/audit/DM_CHU_HANG_BUSINESS_FLOW_ANALYSIS.md.

## K. Reproducible static evidence

Các kiểm tra đã dùng trong checkout:

- Tìm FQ_104_CH_ trên runtime và Database/.
- Tìm CCache_Chu_Hang, CDM_Chu_Hang_Controller, CDM_Chu_Hang, Chu_Hang_ID.
- Đọc các file path trong các mục B-F và đối chiếu line number.
- Kiểm tra git status --short trước khi ghi báo cáo để bảo toàn thay đổi có sẵn.

Không chạy build, test, migration, SQL mutation, deploy, commit hoặc push.
