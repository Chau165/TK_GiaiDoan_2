# Chuẩn hóa Action History cho Master Data

Ngày: 2026-10-20
Repository: Chau165/TK_GiaiDoan_2
Checkout: P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11

## Kết luận

- Đã thêm truy vấn Action History theo cặp Ref_ID + Ma_Chuc_Nang, giữ nguyên procedure và method một tham số để tương thích caller cũ.
- Đã bật tab History dùng component chung cho năm Warehouse Master Data và chuyển Info Chủ Hàng sang cùng truy vấn có scope.
- Add/Edit/Delete tiếp tục dùng hook của FBase. Import ghi history sau commit của từng dòng; lỗi ghi history được báo riêng sau khi dữ liệu đã lưu.
- Source và kiểm thử/build đạt. SQL chưa deploy; chưa xác minh database/view/history thực tế. Trạng thái runtime là DEPLOYMENT_PENDING và RUNTIME_PENDING.
- Còn 26 reader dùng Ref_ID-only: 25 trang Sys và một Info Warehouse generic. Không kết luận các caller này an toàn.

## A. Root cause

Procedure cũ chỉ có điều kiện WHERE Ref_ID = @Ref_ID; Auto_ID là khóa cục bộ theo từng bảng, nên các entity khác nhau có thể cùng Ref_ID. Định nghĩa cũ được giữ nguyên tại [CoreLogActionHistory.Procedures.sql](../../Database/CoreLogActionHistory.Procedures.sql#L5).

Procedure mới nhận thêm Ma_Chuc_Nang và lọc đồng thời hai trường tại [CoreLogActionHistory.Procedures.sql](../../Database/CoreLogActionHistory.Procedures.sql#L36). Scope trống bị từ chối. Controller không fallback về procedure cũ; nếu procedure/view/cột/quyền chưa sẵn sàng, lỗi cấu hình được trả về tại [CLog_Record_Action_History_Controller.cs](../../TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Record_Action_History_Controller.cs#L143).

## B. Quyết định scope và giới hạn bằng chứng

- Entity Action History có trường Ma_Chuc_Nang: [CLog_Record_Action_History.cs](../../TKS_Thuc_Tap_V11_Data_Access/Entity/Log/CLog_Record_Action_History.cs#L90).
- CCommon_Controller ánh xạ function code vào Ma_Chuc_Nang trước khi gọi history insert: [CCommon_Controller.cs](../../TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CCommon_Controller.cs#L24) và transaction overload tại [CCommon_Controller.cs](../../TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CCommon_Controller.cs#L51).
- FBase lấy function code từ metadata của chức năng hiện hành và kiểm tra quyền xem trước khi render: [FBase.razor](../../TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor#L128) và [FBase.razor](../../TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor#L142).
- SQL menu trong repository khai báo code riêng cho Kho 2009, Đơn vị tính 2016, Loại sản phẩm 2017, Sản phẩm 2018 và Nhà cung cấp 2019: [WarehouseModule.Menu.sql](../../Database/WarehouseModule.Menu.sql#L59). Source quyền Chủ Hàng tham chiếu code 2003 tại [ThuctapKho.ChuHangPermission.Recovery.sql](../../Database/ThuctapKho.ChuHangPermission.Recovery.sql#L8).
- Năm List Master Data ghi bằng function code của route đang hoạt động và chỉ thao tác entity tương ứng. Điều này hỗ trợ chọn Ref_ID + Ma_Chuc_Nang cho sáu Info đã migrate ở mức source.
- Chưa có bằng chứng live DB về metadata hiện hành, các giá trị Ma_Chuc_Nang đã ghi trong lịch sử cũ, hay uniqueness của dữ liệu thực tế. Định nghĩa SQL của FQ_425_RAH_sp_ins_Insert và view_Log_Record_Action_History không có trong các SQL source đã tìm thấy. Vì vậy không khẳng định các cột này đang được lưu/trả đúng trong database.
- Script mới có preflight kiểm tra view và cột Ma_Chuc_Nang trước khi tạo procedure. Trước khi deploy vẫn cần kiểm tra định nghĩa insert procedure, view projection, quyền SELECT/EXECUTE và dữ liệu lịch sử cũ. Dữ liệu lịch sử thiếu hoặc có function code sai sẽ không khớp scope mới.

## C. Hai luồng source

### WRITE

| Bước | Source evidence | Kết quả audit |
|---|---|---|
| Add/Edit | [FBase.razor](../../TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor#L535) gọi Add_Data/Update_Data rồi ghi history bằng Ref_ID, user, function code, tên chức năng và nội dung. Edit dùng CUtility.Compare_Entity trên r_objSource/r_objDes. | Add dùng Auto_ID/key do Edit component trả về; Edit log sau khi Update_Data thành công. |
| Delete | [FBase.razor](../../TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor#L642) xóa xong mới insert history bằng ID được truyền vào; sau đó reload Grid. | ID vẫn có sẵn sau khi bản ghi bị xóa; lỗi log hiển thị cảnh báo “đã xóa nhưng không ghi được lịch sử”. |
| Common insert | [CCommon_Controller.cs](../../TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CCommon_Controller.cs#L24) tạo CLog_Record_Action_History và gọi [controller insert](../../TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Record_Action_History_Controller.cs#L76); có thêm overload nhận SqlConnection/SqlTransaction tại dòng 95. | Source truyền Created_By, Ma_Chuc_Nang, hành động và nội dung. Định nghĩa SQL cuối cùng chưa được xác minh. |
| Import | Commit rồi gọi history trong từng List: [Kho](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_1_Kho_List.razor#L195), [Đơn vị tính](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_List.razor#L162), [Loại sản phẩm](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_1_Loai_San_Pham_List.razor#L197), [Sản phẩm](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_1_San_Pham_List.razor#L238), [Nhà cung cấp](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_1_Nha_Cung_Cap_List.razor#L197). | Mỗi file gọi history sau commit (Kho dòng 209, Unit 182, Category 211, Product 252, Supplier 211). Dòng rollback không đi tới success history. FBase không thêm success history thứ hai cho từng dòng. |

### READ

| Bước | Source evidence | Kết quả audit |
|---|---|---|
| Info | Sáu Info component gọi overload có Ref_ID và r_strActive_Function_Code. | Không tải toàn bộ history theo Ref_ID rồi lọc ở client. |
| Controller | [CLog_Record_Action_History_Controller.cs](../../TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Record_Action_History_Controller.cs#L143) chặn scope rỗng, gọi procedure mới, map các DataRow thành CLog_Record_Action_History. | SQL lỗi do thiếu procedure/cột/quyền thành lỗi cấu hình rõ ràng; không trả lỗi truy vấn thành danh sách rỗng. Cách hiển thị lỗi trên runtime chưa xác minh. |
| SQL | [CoreLogActionHistory.Procedures.sql](../../Database/CoreLogActionHistory.Procedures.sql#L23) kiểm tra view/cột; procedure lọc Ref_ID và Ma_Chuc_Nang, giữ thứ tự Created/Auto_ID. | Cần deploy và xác minh trên DB trước runtime. |
| Shared UI | [FCommon_Record_Action_History_List.razor](../../TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Record_Action_History_List.razor#L9) hiển thị danh sách chung; các Info mới bind danh sách qua v_arrData. | Không tạo Grid riêng; hiển thị Created, Created_By, Ma_Chuc_Nang, Ten_Chuc_Nang, Ten_Hanh_Dong, Ten_Moi_Truong, Noi_Dung_Action. |

## D. Caller impact

Tổng hiện tại: 32 reader callsite trong Razor source: 6 scoped và 26 legacy một tham số. Trước khi tạo bốn Info mới cho Kho, Category, Product và Supplier, source có 28 callsite; hiện 4 callsite mới dùng scope. Function code ở các hàng Sys dưới đây lấy từ mã F trong tên component, chỉ là nhận diện source; metadata route và dữ liệu live chưa được truy vấn.

| Caller | Entity | Function code | Query pattern | Migration status | Risk |
|---|---|---:|---|---|---|
| [F2003_2_Chu_Hang_Info.razor:191](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_2_Chu_Hang_Info.razor#L191) | Chủ Hàng | 2003 (source reference) | Ref_ID + Ma_Chuc_Nang | Scoped | Source phù hợp; runtime pending |
| [F2009_2_Kho_Info.razor:60](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_2_Kho_Info.razor#L60) | Kho | 2009 | Ref_ID + Ma_Chuc_Nang | Scoped | Source phù hợp; runtime pending |
| [FWarehouse_Unit_Info.razor:62](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_Info.razor#L62) | Đơn vị tính | 2016 | Ref_ID + Ma_Chuc_Nang | Scoped | Source phù hợp; runtime pending |
| [F2017_2_Loai_San_Pham_Info.razor:61](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_2_Loai_San_Pham_Info.razor#L61) | Loại sản phẩm | 2017 | Ref_ID + Ma_Chuc_Nang | Scoped | Source phù hợp; runtime pending |
| [F2018_2_San_Pham_Info.razor:77](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_2_San_Pham_Info.razor#L77) | Sản phẩm | 2018 | Ref_ID + Ma_Chuc_Nang | Scoped | Source phù hợp; runtime pending |
| [F2019_2_Nha_Cung_Cap_Info.razor:61](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_2_Nha_Cung_Cap_Info.razor#L61) | Nhà cung cấp | 2019 | Ref_ID + Ma_Chuc_Nang | Scoped | Source phù hợp; runtime pending |
| [FWarehouse_2_Warehouse_Master_Info.razor:99](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_2_Warehouse_Master_Info.razor#L99) | Warehouse Master generic | Dynamic/unknown | Ref_ID only | Legacy | Có thể trộn entity; chưa xác minh còn route sử dụng |
| [F1001_2_Chuc_Nang_Info.razor:234](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1001_2_Chuc_Nang_Info.razor#L234) | Chức năng | 1001* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1002_2_Nhom_Thanh_Vien_Info.razor:123](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1002_2_Nhom_Thanh_Vien_Info.razor#L123) | Nhóm thành viên | 1002* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1003_2_Thanh_Vien_Info.razor:160](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1003_2_Thanh_Vien_Info.razor#L160) | Thành viên | 1003* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1013_2_Dinh_Nghia_Mau_Column_Info.razor:132](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1013_2_Dinh_Nghia_Mau_Column_Info.razor#L132) | Định nghĩa mẫu Column | 1013* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1014_2_Multilanguage_Info.razor:163](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1014_2_Multilanguage_Info.razor#L163) | Multilanguage | 1014* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1015_2_Mau_In_Phieu_Config_Info.razor:117](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1015_2_Mau_In_Phieu_Config_Info.razor#L117) | Mẫu in phiếu config | 1015* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1016_2_Webhook_Config_Info.razor:132](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1016_2_Webhook_Config_Info.razor#L132) | Webhook config | 1016* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1018_2_Import_Excel_Config_Info.razor:117](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1018_2_Import_Excel_Config_Info.razor#L117) | Import Excel config | 1018* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1019_2_Drill_Down_Info.razor:139](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1019_2_Drill_Down_Info.razor#L139) | Drill Down | 1019* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1020_2_Hien_An_Column_Info.razor:139](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1020_2_Hien_An_Column_Info.razor#L139) | Hiện/Ẩn Column | 1020* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1022_2_API_Source_Info.razor:208](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1022_2_API_Source_Info.razor#L208) | API Source | 1022* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1023_2_Cau_Hinh_Chuc_Nang_App_Info.razor:778](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1023_2_Cau_Hinh_Chuc_Nang_App_Info.razor#L778) | Cấu hình chức năng App | 1023* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1027_2_Dieu_Chinh_Do_Rong_Cot_Info.razor:132](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1027_2_Dieu_Chinh_Do_Rong_Cot_Info.razor#L132) | Điều chỉnh độ rộng cột | 1027* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1028_2_Filter_Date_Default_Info.razor:139](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1028_2_Filter_Date_Default_Info.razor#L139) | Filter Date Default | 1028* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1031_2_Help_Guide_Info.razor:139](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1031_2_Help_Guide_Info.razor#L139) | Help Guide | 1031* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1032_2_Dong_Bang_Column_Info.razor:125](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1032_2_Dong_Bang_Column_Info.razor#L125) | Đóng băng Column | 1032* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1034_2_Background_Thread_Job_Info.razor:146](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1034_2_Background_Thread_Job_Info.razor#L146) | Background Thread Job | 1034* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1035_2_Daily_Schedule_Job_Info.razor:156](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1035_2_Daily_Schedule_Job_Info.razor#L156) | Daily Schedule Job | 1035* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1036_2_Nhom_PDA_Info.razor:125](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1036_2_Nhom_PDA_Info.razor#L125) | Nhóm PDA | 1036* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1038_2_Cau_Hinh_Su_Dung_API_Source_Info.razor:182](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1038_2_Cau_Hinh_Su_Dung_API_Source_Info.razor#L182) | Cấu hình sử dụng API Source | 1038* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1039_2_Cau_Hinh_Goi_Webhook_Info.razor:139](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1039_2_Cau_Hinh_Goi_Webhook_Info.razor#L139) | Cấu hình gọi Webhook | 1039* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1040_2_Cau_Hinh_Component_App_Info.razor:148](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1040_2_Cau_Hinh_Component_App_Info.razor#L148) | Cấu hình Component App | 1040* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1041_2_Log_API_Info.razor:66](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1041_2_Log_API_Info.razor#L66) | Log API | 1041* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1042_2_Cau_Hinh_So_Phieu_Info.razor:139](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1042_2_Cau_Hinh_So_Phieu_Info.razor#L139) | Cấu hình số phiếu | 1042* | Ref_ID only | Legacy | Scope entity chưa được audit |
| [F1044_2_Cau_Hinh_Grid_Field_Info.razor:153](../../TKS_Thuc_Tap_V11_Web_Sys/Pages/Sys/Components/F1044_2_Cau_Hinh_Grid_Field_Info.razor#L153) | Cấu hình Grid Field | 1044* | Ref_ID only | Legacy | Scope entity chưa được audit |

* Function code Sys chỉ suy ra từ mã trong component filename; chưa đối chiếu metadata database. Các caller legacy cần được audit riêng trước khi migrate. Danh sách được lấy từ direct callsite search trong Web_Sys và Web_Danh_Muc source.

## E. Verification matrix

| Module | Add History | Edit History | Delete History | Import History | Info History | Scope safe | Status |
|---|---|---|---|---|---|---|---|
| Kho 2009 | FBase source verified | FBase source verified | FBase source verified | Sau commit, một lần/dòng source verified | Shared UI + scoped reader | Có cơ sở source; DB chưa xác minh | IMPLEMENTED, SOURCE_VERIFIED, TEST_PASS, DEPLOYMENT_PENDING, RUNTIME_PENDING |
| Đơn vị tính 2016 | FBase source verified | FBase source verified | FBase source verified | Sau commit, một lần/dòng source verified | Shared UI + scoped reader | Có cơ sở source; DB chưa xác minh | IMPLEMENTED, SOURCE_VERIFIED, TEST_PASS, DEPLOYMENT_PENDING, RUNTIME_PENDING |
| Loại sản phẩm 2017 | FBase source verified | FBase source verified | FBase source verified | Sau commit, một lần/dòng source verified | Shared UI + scoped reader | Có cơ sở source; DB chưa xác minh | IMPLEMENTED, SOURCE_VERIFIED, TEST_PASS, DEPLOYMENT_PENDING, RUNTIME_PENDING |
| Sản phẩm 2018 | FBase source verified | FBase source verified | FBase source verified | Sau commit, một lần/dòng source verified | Shared UI + scoped reader | Có cơ sở source; DB chưa xác minh | IMPLEMENTED, SOURCE_VERIFIED, TEST_PASS, DEPLOYMENT_PENDING, RUNTIME_PENDING |
| Nhà cung cấp 2019 | FBase source verified | FBase source verified | FBase source verified | Sau commit, một lần/dòng source verified | Shared UI + scoped reader | Có cơ sở source; DB chưa xác minh | IMPLEMENTED, SOURCE_VERIFIED, TEST_PASS, DEPLOYMENT_PENDING, RUNTIME_PENDING |
| Chủ Hàng 2003 | FBase source pattern | FBase source pattern | FBase source pattern | Existing import source unchanged ([F2003_1_Chu_Hang_List.razor](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor#L167)) | Existing UI + scoped reader | Code source reference; live history chưa xác minh | SOURCE_VERIFIED, TEST_PASS, DEPLOYMENT_PENDING, RUNTIME_PENDING |

Kiểm tra source xác nhận Add_Data của năm module đặt Auto_ID vừa insert vào state FBase và gán add key tại [Kho](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_3_Kho_Edit.razor#L72), [Đơn vị tính](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_Edit.razor#L79), [Loại sản phẩm](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_3_Loai_San_Pham_Edit.razor#L76), [Sản phẩm](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_3_San_Pham_Edit.razor#L88) và [Nhà cung cấp](../../TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_3_Nha_Cung_Cap_Edit.razor#L76); các component cũng đặt add key. FBase dùng state đó ghi Add history. Edit tracking lấy diff giữa r_objSource/r_objDes. Import của năm module dùng transaction riêng mỗi dòng, gọi history sau commit. Không có DB-write test nên các semantics trên chưa được xác minh end-to-end.

## F. Files thay đổi trong phạm vi Action History

- Controller: thêm overload có scope, chặn scope rỗng và lỗi cấu hình rõ ràng trong CLog_Record_Action_History_Controller.cs. Method Ref_ID-only giữ nguyên.
- SQL: thêm procedure scoped và preflight view/cột trong CoreLogActionHistory.Procedures.sql; giữ procedure cũ.
- SQL test source: bổ sung tình huống cùng Ref_ID nhưng khác Ma_Chuc_Nang và scope rỗng trong CoreLogActionHistory.IntegrationTests.sql. Script test dùng transaction rollback; không chạy trên database dùng chung.
- UI: sửa F2003_2_Chu_Hang_Info.razor; bật shared History cho F2009_2_Kho_Info.razor, FWarehouse_Unit_Info.razor, F2017_2_Loai_San_Pham_Info.razor, F2018_2_San_Pham_Info.razor và F2019_2_Nha_Cung_Cap_Info.razor.
- Shared save/delete warning: FBase.razor báo rõ dữ liệu đã lưu/xóa nếu bước insert history lỗi, rồi vẫn thực hiện close/reload theo kết quả CRUD.
- Tests: thêm ActionHistoryScopeTests.cs; cập nhật assertion history trong MasterDataCompanyStyleMigrationTests.cs và route regression trong WarehouseUiWorkflowTests.cs.
- Ignore rule: .gitignore chỉ unignore CLog_Record_Action_History_Controller.cs trong Controller/Log để controller được đưa vào source control; các file log khác không được mở rộng rule.

## G. Verification và việc còn lại

| Kiểm tra | Kết quả |
|---|---|
| Focused tests: ActionHistoryScopeTests, MasterDataCompanyStyleMigrationTests, WarehouseNavigationTests, UnitExcelImportUiTests, WarehouseUiWorkflowTests | PASS: 64 passed, 0 failed, 0 skipped |
| Build Web Danh Mục | PASS: 0 errors, 12 warnings |
| Build Web host | PASS: 0 errors, 24 warnings |
| git diff --check | PASS; Git chỉ phát cảnh báo chuẩn hóa LF/CRLF |
| CodeGraph | Sync 2 file sau thay đổi cuối; status xác nhận index up to date |
| SQL integration / DB history data | NOT RUN; không kết nối hoặc ghi database |
| Browser UAT / runtime | NOT RUN |
| Deploy procedure | NOT DONE |

### Trạng thái cuối

- IMPLEMENTED: controller overload, SQL migration source, shared Info UI cho sáu module.
- SOURCE_VERIFIED: caller paths, FBase write hooks, function-code source seeds và import commit ordering.
- TEST_PASS: focused tests và hai build như trên.
- DEPLOYMENT_PENDING: cần deploy procedure mới sau khi xác minh view, cột Ma_Chuc_Nang, insert procedure, quyền database và function metadata.
- RUNTIME_PENDING: chưa đọc lịch sử thật, chưa xác minh cùng Auto_ID/different function, empty state hoặc thông báo cấu hình trên DB/browser.
- Remaining risk: 26 caller cũ chưa scope; history rows cũ có thể thiếu hoặc sai Ma_Chuc_Nang; định nghĩa insert procedure/view và dữ liệu live chưa có bằng chứng từ source.
