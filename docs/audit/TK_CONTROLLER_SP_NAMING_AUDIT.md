# Audit quy chuẩn đặt tên Controller và Stored Procedure

> Read-only audit. Không sửa source C#, SQL, database, migration, deployment hoặc test.

## 1. Thông tin audit

| Mục | Giá trị |
| --- | --- |
| Repository | `P:/Giao đoạn 2 TK/Giao đoạn 2 TK/TKS_Thuc_Tap_11` |
| Commit được audit | `9bfcf4c0d254c2352c7425def5c42e95e2b3ab5d` |
| Phạm vi Controller | 91 file `.cs`, 91 class declaration |
| Nguồn quy chuẩn | `C:\Users\Surface\Downloads\Cam Nang Code TK.docx` |
| Nguồn SQL | `Database/` trong repository; không truy cập live database |
| Ngày audit | 2026-10-26 |

### Quy tắc được dùng

- Tên function và tên Store phải giống nhau.
- Cú pháp: `[F quy định]_[prefix]_[ý nghĩa Store]`.
- Prefix thao tác chuẩn: `sp_sel_List`, `sp_sel_Get`, `sp_ins`, `sp_upd`, `sp_del`.
- `Fxxxx` dành cho phạm vi chức năng; `FQ_xxx_` dành cho bảng dùng chung. Việc chuyển `Fxxxx` sang `FQ` cần quyết định nghiệp vụ, không suy đoán từ tên bảng.
- Truy cập database qua Stored Procedure; không dùng `sp_executesql` trong thân Store.
- Helper dùng chung được tách khỏi function nghiệp vụ khi đánh giá tên.

### Cách đọc kết quả

- `Confirmed Violation`: bằng chứng tĩnh đủ để kết luận vi phạm quy tắc đã nêu.
- `Likely Non-compliance`: có dấu hiệu không khớp nhưng cần xác nhận exception hoặc chủ sở hữu.
- `Needs Verification`: thiếu DDL/metadata/ý định nghiệp vụ trong repository; không kết luận database đang thiếu.
- `Compliant`: khớp quy tắc trong phạm vi bằng chứng đã đọc.
- `Out of Scope/Exception`: helper kỹ thuật hoặc convention riêng cần giữ nguyên cho đến khi có quyết định.

## 2. Tóm tắt điều hành

| Chỉ số | Kết quả |
| --- | --- |
| Controller/class | 91 / 91 |
| Lệnh gọi `CSqlHelper` trực tiếp | 428; trong đó 427 có literal Store và 1 helper động |
| Route cụ thể trong inventory | 435; gồm 427 direct và 8 route qua page helper |
| Store unique được gọi bởi route | 355 |
| DDL Store trong `Database/` | 119 definition, 119 tên unique |
| Route có DDL hiện tại trong `Database/` | 85; route chưa có DDL tại đây: 350 |
| Method/Store không giống nhau | 9 direct confirmed + 2 wrapper cần xác nhận |
| Store unique route chưa thấy trong `Database/` | 279 |

Kết luận chính: phát hiện 9 cặp direct có method khác tên Store, 2 page wrapper có tên method mô tả khác tên Store, và khoảng trống source-only giữa Controller với DDL `Database/`. Chưa có cơ sở để đổi nhóm master từ `Fxxxx` sang `FQ`; các Store F3001/F3002 thể hiện workflow nhập/xuất nên giữ phạm vi feature cho đến khi có quyết định mới.

## 3. Inventory Controller → method → Stored Procedure → SQL definition

### 3.1 Inventory class

| # | Controller file | Class | Class line | Direct CSqlHelper calls | Concrete routes |
| --- | --- | --- | --- | --- | --- |
| 1 | TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs | CWarehouseReport_Controller | 7 | 3 | 7 |
| 2 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_API_Source.cs | CCache_API_Source | 14 | 0 | 0 |
| 3 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_API_Source_Chu_Hang.cs | CCache_API_Source_Chu_Hang | 12 | 0 | 0 |
| 4 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_API_Source_Chu_Hang_Function.cs | CCache_API_Source_Chu_Hang_Function | 12 | 0 | 0 |
| 5 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_API_Source_Function.cs | CCache_API_Source_Function | 12 | 0 | 0 |
| 6 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Auto_Thread.cs | CCache_Auto_Thread | 13 | 0 | 0 |
| 7 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Cau_Hinh_Component_App.cs | CCache_Cau_Hinh_Component_App | 14 | 0 | 0 |
| 8 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Chu_Hang.cs | CCache_Chu_Hang | 13 | 0 | 0 |
| 9 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Chuc_Nang.cs | CCache_Chuc_Nang | 13 | 0 | 0 |
| 10 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Chuc_Nang_App.cs | CCache_Chuc_Nang_App | 13 | 0 | 0 |
| 11 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Column_Width.cs | CCache_Column_Width | 12 | 0 | 0 |
| 12 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Common_Controller.cs | CCache_Common_Controller | 10 | 0 | 0 |
| 13 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Daily_Schedule_Job.cs | CCache_Daily_Schedule_Job | 11 | 0 | 0 |
| 14 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Don_Vi_Tinh.cs | CCache_Don_Vi_Tinh | 9 | 0 | 0 |
| 15 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Drill_Down.cs | CCache_Drill_Down | 12 | 0 | 0 |
| 16 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Filter_Date_Default.cs | CCache_Filter_Date_Default | 12 | 0 | 0 |
| 17 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Frozen_Column.cs | CCache_Frozen_Column | 12 | 0 | 0 |
| 18 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Grid_Field.cs | CCache_Grid_Field | 12 | 0 | 0 |
| 19 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Grid_UI_Global.cs | CCache_Grid_UI_Global | 12 | 0 | 0 |
| 20 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Hien_An_Column.cs | CCache_Hien_An_Column | 12 | 0 | 0 |
| 21 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Import_Excel_Template_Config.cs | CCache_Import_Excel_Template_Config | 11 | 0 | 0 |
| 22 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Kho.cs | CCache_Kho | 9 | 0 | 0 |
| 23 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Language.cs | CCache_Language | 13 | 0 | 0 |
| 24 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Loai_San_Pham.cs | CCache_Loai_San_Pham | 9 | 0 | 0 |
| 25 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Log_API.cs | CCache_Log_API | 14 | 0 | 0 |
| 26 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Mau_Column.cs | CCache_Mau_Column | 12 | 0 | 0 |
| 27 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_NCC.cs | CCache_NCC | 9 | 0 | 0 |
| 28 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Nhom_PDA.cs | CCache_Nhom_PDA | 14 | 0 | 0 |
| 29 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Nhom_PDA_User.cs | CCache_Nhom_PDA_User | 14 | 0 | 0 |
| 30 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Nhom_Thanh_Vien.cs | CCache_Nhom_Thanh_Vien | 12 | 0 | 0 |
| 31 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Nhom_Thanh_Vien_User.cs | CCache_Nhom_Thanh_Vien_User | 12 | 0 | 0 |
| 32 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Phan_Quyen_Chuc_Nang.cs | CCache_Phan_Quyen_Chuc_Nang | 13 | 0 | 0 |
| 33 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Report_Template_Config.cs | CCache_Report_Template_Config | 14 | 0 | 0 |
| 34 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_STT_Next.cs | CCache_STT_Next | 14 | 0 | 0 |
| 35 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_STT_Next_Detail.cs | CCache_STT_Next_Detail | 12 | 0 | 0 |
| 36 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_San_Pham.cs | CCache_San_Pham | 9 | 0 | 0 |
| 37 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Thanh_Vien.cs | CCache_Thanh_Vien | 12 | 0 | 0 |
| 38 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Token.cs | CCache_Token | 12 | 0 | 0 |
| 39 | TKS_Thuc_Tap_V11_Data_Access/Controller/Cache/CCache_Webhook.cs | CCache_Webhook | 12 | 0 | 0 |
| 40 | TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CCommon_Controller.cs | CCommon_Controller | 22 | 0 | 0 |
| 41 | TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CThread_Common_Controller.cs | CThread_Common_Controller | 14 | 0 | 0 |
| 42 | TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CWarehouse_Controller_Base.cs | CWarehouse_Controller_Base | 8 | 1 | 0 |
| 43 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs | CDM_Chu_Hang_Controller | 14 | 8 | 8 |
| 44 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs | CDM_Don_Vi_Tinh_Controller | 8 | 9 | 9 |
| 45 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs | CDM_Kho_Controller | 8 | 9 | 9 |
| 46 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_User_Controller.cs | CDM_Kho_User_Controller | 8 | 6 | 7 |
| 47 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs | CDM_Loai_San_Pham_Controller | 8 | 9 | 9 |
| 48 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs | CDM_NCC_Controller | 8 | 9 | 9 |
| 49 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs | CDM_San_Pham_Controller | 8 | 9 | 9 |
| 50 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CWarehouseMasterSupport_Controller.cs | CWarehouseMasterSupport_Controller | 5 | 0 | 2 |
| 51 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs | CLog_API_Controller | 14 | 10 | 10 |
| 52 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs | CLog_Import_Excel_Controller | 14 | 8 | 8 |
| 53 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs | CLog_Nhat_Ky_Dang_Nhap_Controller | 14 | 9 | 9 |
| 54 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Truy_Cap_Chuc_Nang_Controller.cs | CLog_Nhat_Ky_Truy_Cap_Chuc_Nang_Controller | 14 | 5 | 5 |
| 55 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Record_Action_History_Controller.cs | CLog_Record_Action_History_Controller | 14 | 5 | 5 |
| 56 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs | CLog_Report_File_Excel_Controller | 14 | 9 | 9 |
| 57 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Controller.cs | CSys_API_Source_Chu_Hang_Controller | 14 | 8 | 8 |
| 58 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs | CSys_API_Source_Chu_Hang_Function_Controller | 14 | 11 | 11 |
| 59 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs | CSys_API_Source_Controller | 14 | 11 | 11 |
| 60 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs | CSys_API_Source_Function_Controller | 14 | 9 | 9 |
| 61 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Auto_Thread_Controller.cs | CSys_Auto_Thread_Controller | 14 | 8 | 8 |
| 62 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs | CSys_Cau_Hinh_Component_App_Controller | 14 | 9 | 9 |
| 63 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_App_Controller.cs | CSys_Chuc_Nang_App_Controller | 14 | 7 | 7 |
| 64 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs | CSys_Chuc_Nang_Controller | 14 | 13 | 13 |
| 65 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Column_Width_Controller.cs | CSys_Column_Width_Controller | 14 | 8 | 8 |
| 66 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Daily_Schedule_Job_Controller.cs | CSys_Daily_Schedule_Job_Controller | 14 | 8 | 8 |
| 67 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Drill_Down_Controller.cs | CSys_Drill_Down_Controller | 14 | 8 | 8 |
| 68 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Filter_Date_Default_Controller.cs | CSys_Filter_Date_Default_Controller | 14 | 8 | 8 |
| 69 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Frozen_Column_Controller.cs | CSys_Frozen_Column_Controller | 14 | 8 | 8 |
| 70 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs | CSys_Grid_Field_Controller | 14 | 13 | 13 |
| 71 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs | CSys_Grid_UI_Global_Controller | 14 | 11 | 11 |
| 72 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs | CSys_Help_Guide_Controller | 14 | 9 | 9 |
| 73 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs | CSys_Hien_An_Column_Controller | 14 | 9 | 9 |
| 74 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs | CSys_Import_Excel_Template_Config_Controller | 14 | 9 | 9 |
| 75 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Language_Controller.cs | CSys_Language_Controller | 14 | 8 | 8 |
| 76 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Mau_Column_Controller.cs | CSys_Mau_Column_Controller | 14 | 8 | 8 |
| 77 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_Controller.cs | CSys_Nhom_PDA_Controller | 15 | 8 | 8 |
| 78 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs | CSys_Nhom_PDA_User_Controller | 15 | 10 | 10 |
| 79 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_Controller.cs | CSys_Nhom_Thanh_Vien_Controller | 14 | 8 | 8 |
| 80 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs | CSys_Nhom_Thanh_Vien_User_Controller | 15 | 9 | 9 |
| 81 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs | CSys_Phan_Quyen_Chuc_Nang_Controller | 14 | 13 | 13 |
| 82 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Report_Template_Config_Controller.cs | CSys_Report_Template_Config_Controller | 14 | 8 | 8 |
| 83 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Controller.cs | CSys_STT_Next_Controller | 14 | 8 | 8 |
| 84 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs | CSys_STT_Next_Detail_Controller | 14 | 10 | 10 |
| 85 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs | CSys_Thanh_Vien_Controller | 14 | 18 | 18 |
| 86 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Token_Controller.cs | CSys_Token_Controller | 14 | 8 | 8 |
| 87 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Chu_Hang_Controller.cs | CSys_Webhook_Chu_Hang_Controller | 14 | 7 | 7 |
| 88 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Controller.cs | CSys_Webhook_Controller | 14 | 8 | 8 |
| 89 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CWarehouseDocumentSupport_Controller.cs | CWarehouseDocumentSupport_Controller | 6 | 1 | 2 |
| 90 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs | CXNK_Nhap_Kho_Controller | 9 | 10 | 10 |
| 91 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs | CXNK_Xuat_Kho_Controller | 9 | 10 | 10 |

### 3.2 Full concrete route inventory

`SQL definition` là vị trí definition trong `Database/`; `not found` chỉ có nghĩa chưa thấy trong source hiện tại của thư mục đó.

| # | Controller / method | Method line | Store | Call line / mechanism | SQL definition | Category | Assessment |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs / `F9002_sp_Chi_Tiet_Nhap` | 9 | `F9002_sp_Chi_Tiet_Nhap` | 15 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:4067 | Fxxxx | Likely Non-compliance / approved exception to confirm |
| 2 | TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs / `F9002_sp_Chi_Tiet_Xuat` | 34 | `F9002_sp_Chi_Tiet_Xuat` | 40 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:4081 | Fxxxx | Likely Non-compliance / approved exception to confirm |
| 3 | TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs / `F9002_sp_Chi_Tiet_Nhap_Page` | 59 | `F9002_sp_Chi_Tiet_Nhap_Page` | 65 / Fill_Page_Data | Database/WarehouseModule.Procedures.sql:4095 | Fxxxx | Likely Non-compliance / approved exception to confirm |
| 4 | TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs / `F9002_sp_Chi_Tiet_Xuat_Page` | 85 | `F9002_sp_Chi_Tiet_Xuat_Page` | 91 / Fill_Page_Data | Database/WarehouseModule.Procedures.sql:4153 | Fxxxx | Likely Non-compliance / approved exception to confirm |
| 5 | TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs / `F9002_sp_Xuat_Nhap_Ton` | 111 | `F9002_sp_Xuat_Nhap_Ton` | 117 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:6438 | Fxxxx | Likely Non-compliance / approved exception to confirm |
| 6 | TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs / `F9002_sp_Xuat_Nhap_Ton_Page` | 136 | `F9002_sp_Xuat_Nhap_Ton_Page` | 142 / Fill_Page_Data | Database/WarehouseModule.Procedures.sql:6795 | Fxxxx | Likely Non-compliance / approved exception to confirm |
| 7 | TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs / `F9001_sp_Ton_Kho_Hien_Tai_Page` | 162 | `F9001_sp_Ton_Kho_Hien_Tai_Page` | 168 / Fill_Page_Data | Database/WarehouseModule.Procedures.sql:6647 | Fxxxx | Likely Non-compliance / approved exception to confirm |
| 8 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs / `FQ_104_CH_sp_sel_List_By_Created` | 17 | `FQ_104_CH_sp_sel_List_By_Created` | 27 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 9 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs / `FQ_104_CH_sp_sel_List_For_Cache` | 49 | `FQ_104_CH_sp_sel_List_For_Cache` | 56 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 10 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs / `FQ_104_CH_sp_sel_Get_By_ID` | 78 | `FQ_104_CH_sp_sel_Get_By_ID` | 85 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 11 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs / `FQ_104_CH_sp_ins_Insert` | 106 | `FQ_104_CH_sp_ins_Insert` | 112 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 12 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs / `FQ_104_CH_sp_ins_Insert` | 126 | `FQ_104_CH_sp_ins_Insert` | 132 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 13 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs / `FQ_104_CH_sp_upd_Update` | 146 | `FQ_104_CH_sp_upd_Update` | 150 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 14 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs / `FQ_104_CH_sp_upd_Update` | 162 | `FQ_104_CH_sp_upd_Update` | 166 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 15 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Chu_Hang_Controller.cs / `FQ_104_CH_sp_del_Delete_By_ID` | 178 | `FQ_104_CH_sp_del_Delete_By_ID` | 182 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 16 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs / `F2016_sp_sel_Get_By_ID` | 10 | `F2016_sp_sel_Get_By_ID` | 16 / CSqlHelper.FillDataTable | Database/WarehouseMaster.GetById.Procedures.sql:13 | Fxxxx | Compliant |
| 17 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs / `F2016_sp_sel_List_Don_Vi_Tinh` | 34 | `F2016_sp_sel_List_Don_Vi_Tinh` | 40 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3754 | Fxxxx | Compliant |
| 18 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs / `F2016_sp_sel_List_Don_Vi_Tinh_By_Created` | 59 | `F2016_sp_sel_List_Don_Vi_Tinh_By_Created` | 72 / CSqlHelper.FillDataTable | Database/WarehouseMaster.ByCreated.Procedures.sql:30 | Fxxxx | Compliant |
| 19 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs / `F2016_sp_sel_List_Don_Vi_Tinh_For_Cache` | 90 | `F2016_sp_sel_List_Don_Vi_Tinh_For_Cache` | 96 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3794 | Fxxxx | Compliant |
| 20 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs / `F2016_sp_sel_List_Don_Vi_Tinh_Lookup` | 115 | `F2016_sp_sel_List_Don_Vi_Tinh_Lookup` | 121 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3834 | Fxxxx | Compliant |
| 21 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs / `F2016_sp_ins_Don_Vi_Tinh` | 140 | `F2016_sp_ins_Don_Vi_Tinh` | 146 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:3943 | Fxxxx | Compliant |
| 22 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs / `F2016_sp_ins_Don_Vi_Tinh` | 156 | `F2016_sp_ins_Don_Vi_Tinh` | 162 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:3943 | Fxxxx | Compliant |
| 23 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs / `F2016_sp_upd_Don_Vi_Tinh` | 172 | `F2016_sp_upd_Don_Vi_Tinh` | 177 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:3974 | Fxxxx | Compliant |
| 24 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Don_Vi_Tinh_Controller.cs / `F2016_sp_del_Don_Vi_Tinh` | 185 | `F2016_sp_del_Don_Vi_Tinh` | 189 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:3596 | Fxxxx | Compliant |
| 25 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs / `F2009_sp_sel_Get_By_ID` | 10 | `F2009_sp_sel_Get_By_ID` | 16 / CSqlHelper.FillDataTable | Database/WarehouseMaster.GetById.Procedures.sql:1 | Fxxxx | Compliant |
| 26 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs / `F2009_sp_sel_List_Kho` | 34 | `F2009_sp_sel_List_Kho` | 40 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3746 | Fxxxx | Compliant |
| 27 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs / `F2009_sp_sel_List_Kho_By_Created` | 59 | `F2009_sp_sel_List_Kho_By_Created` | 72 / CSqlHelper.FillDataTable | Database/WarehouseMaster.ByCreated.Procedures.sql:10 | Fxxxx | Compliant |
| 28 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs / `F2009_sp_sel_List_Kho_For_Cache` | 90 | `F2009_sp_sel_List_Kho_For_Cache` | 96 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3786 | Fxxxx | Compliant |
| 29 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs / `F2009_sp_sel_List_Kho_Lookup` | 115 | `F2009_sp_sel_List_Kho_Lookup` | 121 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3826 | Fxxxx | Compliant |
| 30 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs / `F2009_sp_ins_Kho` | 140 | `F2009_sp_ins_Kho` | 146 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5218 | Fxxxx | Compliant |
| 31 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs / `F2009_sp_ins_Kho` | 156 | `F2009_sp_ins_Kho` | 162 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5218 | Fxxxx | Compliant |
| 32 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs / `F2009_sp_upd_Kho` | 172 | `F2009_sp_upd_Kho` | 177 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5247 | Fxxxx | Compliant |
| 33 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_Controller.cs / `F2009_sp_del_Kho` | 185 | `F2009_sp_del_Kho` | 189 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:3712 | Fxxxx | Compliant |
| 34 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_User_Controller.cs / `F2015_sp_sel_List_Kho_User` | 10 | `F2015_sp_sel_List_Kho_User` | 16 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:4981 | Fxxxx | Compliant |
| 35 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_User_Controller.cs / `F2015_sp_sel_List_Sys_Thanh_Vien_Cho_Phan_Quyen_Kho` | 35 | `F2015_sp_sel_List_Sys_Thanh_Vien_Cho_Phan_Quyen_Kho` | 41 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:4589 | Fxxxx | Compliant |
| 36 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_User_Controller.cs / `F2015_sp_ins_Kho_User` | 60 | `F2015_sp_ins_Kho_User` | 65 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4935 | Fxxxx | Compliant |
| 37 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_User_Controller.cs / `F2015_sp_upd_Kho_User` | 76 | `F2015_sp_upd_Kho_User` | 81 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4954 | Fxxxx | Compliant |
| 38 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_User_Controller.cs / `F2015_sp_del_Kho_User` | 92 | `F2015_sp_del_Kho_User` | 96 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:4972 | Fxxxx | Compliant |
| 39 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_User_Controller.cs / `sp_DM_Kho_User_List_Allowed` | 104 | `sp_DM_Kho_User_List_Allowed` | 110 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:4576 | sp_ | Out of Scope/Exception candidate (technical/support prefix) |
| 40 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Kho_User_Controller.cs / `sp_DM_Kho_User_Page` | 129 | `sp_DM_Kho_User_Page` | 135 / Fill_Page_Data | Database/WarehouseModule.Procedures.sql:4994 | sp_ | Out of Scope/Exception candidate (technical/support prefix) |
| 41 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs / `F2017_sp_sel_Get_By_ID` | 10 | `F2017_sp_sel_Get_By_ID` | 16 / CSqlHelper.FillDataTable | Database/WarehouseMaster.GetById.Procedures.sql:25 | Fxxxx | Compliant |
| 42 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs / `F2017_sp_sel_List_Loai_San_Pham` | 34 | `F2017_sp_sel_List_Loai_San_Pham` | 40 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3762 | Fxxxx | Compliant |
| 43 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs / `F2017_sp_sel_List_Loai_San_Pham_By_Created` | 59 | `F2017_sp_sel_List_Loai_San_Pham_By_Created` | 72 / CSqlHelper.FillDataTable | Database/WarehouseMaster.ByCreated.Procedures.sql:50 | Fxxxx | Compliant |
| 44 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs / `F2017_sp_sel_List_Loai_San_Pham_For_Cache` | 90 | `F2017_sp_sel_List_Loai_San_Pham_For_Cache` | 96 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3802 | Fxxxx | Compliant |
| 45 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs / `F2017_sp_sel_List_Loai_San_Pham_Lookup` | 115 | `F2017_sp_sel_List_Loai_San_Pham_Lookup` | 121 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3842 | Fxxxx | Compliant |
| 46 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs / `F2017_sp_ins_Loai_San_Pham` | 140 | `F2017_sp_ins_Loai_San_Pham` | 146 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4003 | Fxxxx | Compliant |
| 47 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs / `F2017_sp_ins_Loai_San_Pham` | 156 | `F2017_sp_ins_Loai_San_Pham` | 162 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4003 | Fxxxx | Compliant |
| 48 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs / `F2017_sp_upd_Loai_San_Pham` | 172 | `F2017_sp_upd_Loai_San_Pham` | 177 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4034 | Fxxxx | Compliant |
| 49 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_Loai_San_Pham_Controller.cs / `F2017_sp_del_Loai_San_Pham` | 185 | `F2017_sp_del_Loai_San_Pham` | 189 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:3625 | Fxxxx | Compliant |
| 50 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs / `F2019_sp_sel_Get_By_ID` | 10 | `F2019_sp_sel_Get_By_ID` | 16 / CSqlHelper.FillDataTable | Database/WarehouseMaster.GetById.Procedures.sql:50 | Fxxxx | Compliant |
| 51 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs / `F2019_sp_sel_List_NCC` | 34 | `F2019_sp_sel_List_NCC` | 40 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3778 | Fxxxx | Compliant |
| 52 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs / `F2019_sp_sel_List_NCC_By_Created` | 59 | `F2019_sp_sel_List_NCC_By_Created` | 72 / CSqlHelper.FillDataTable | Database/WarehouseMaster.ByCreated.Procedures.sql:90 | Fxxxx | Compliant |
| 53 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs / `F2019_sp_sel_List_NCC_For_Cache` | 90 | `F2019_sp_sel_List_NCC_For_Cache` | 96 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3818 | Fxxxx | Compliant |
| 54 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs / `F2019_sp_sel_List_NCC_Lookup` | 115 | `F2019_sp_sel_List_NCC_Lookup` | 121 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3858 | Fxxxx | Compliant |
| 55 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs / `F2019_sp_ins_NCC` | 140 | `F2019_sp_ins_NCC` | 146 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5157 | Fxxxx | Compliant |
| 56 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs / `F2019_sp_ins_NCC` | 156 | `F2019_sp_ins_NCC` | 162 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5157 | Fxxxx | Compliant |
| 57 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs / `F2019_sp_upd_NCC` | 172 | `F2019_sp_upd_NCC` | 177 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5187 | Fxxxx | Compliant |
| 58 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_NCC_Controller.cs / `F2019_sp_del_NCC` | 185 | `F2019_sp_del_NCC` | 189 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:3683 | Fxxxx | Compliant |
| 59 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs / `F2018_sp_sel_Get_By_ID` | 10 | `F2018_sp_sel_Get_By_ID` | 16 / CSqlHelper.FillDataTable | Database/WarehouseMaster.GetById.Procedures.sql:37 | Fxxxx | Compliant |
| 60 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs / `F2018_sp_sel_List_San_Pham` | 34 | `F2018_sp_sel_List_San_Pham` | 40 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3770 | Fxxxx | Compliant |
| 61 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs / `F2018_sp_sel_List_San_Pham_By_Created` | 59 | `F2018_sp_sel_List_San_Pham_By_Created` | 72 / CSqlHelper.FillDataTable | Database/WarehouseMaster.ByCreated.Procedures.sql:70 | Fxxxx | Compliant |
| 62 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs / `F2018_sp_sel_List_San_Pham_For_Cache` | 90 | `F2018_sp_sel_List_San_Pham_For_Cache` | 96 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3810 | Fxxxx | Compliant |
| 63 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs / `F2018_sp_sel_List_San_Pham_Lookup` | 115 | `F2018_sp_sel_List_San_Pham_Lookup` | 121 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:3850 | Fxxxx | Compliant |
| 64 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs / `F2018_sp_ins_San_Pham` | 140 | `F2018_sp_ins_San_Pham` | 146 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5096 | Fxxxx | Compliant |
| 65 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs / `F2018_sp_ins_San_Pham` | 156 | `F2018_sp_ins_San_Pham` | 162 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5096 | Fxxxx | Compliant |
| 66 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs / `F2018_sp_upd_San_Pham` | 172 | `F2018_sp_upd_San_Pham` | 177 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5127 | Fxxxx | Compliant |
| 67 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CDM_San_Pham_Controller.cs / `F2018_sp_del_San_Pham` | 185 | `F2018_sp_del_San_Pham` | 189 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:3654 | Fxxxx | Compliant |
| 68 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CWarehouseMasterSupport_Controller.cs / `List_Master_Page_Async` | 9 | `sp_DM_Master_Page` | 11 / Page_From_Procedure | Database/WarehouseModule.Procedures.sql:3869 | sp_ | Likely Non-compliance (temporary wrapper) |
| 69 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CWarehouseMasterSupport_Controller.cs / `List_Lookup_Page_Async` | 14 | `sp_DM_Lookup_Page` | 16 / Page_From_Procedure | Database/WarehouseModule.Procedures.sql:3906 | sp_ | Likely Non-compliance (temporary wrapper) |
| 70 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `FQ_401_A_sp_sel_List_By_Created` | 16 | `FQ_401_A_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 71 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `FQ_401_A_sp_sel_List_For_Cache` | 48 | `FQ_401_A_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 72 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `FQ_401_A_sp_sel_Get_By_ID` | 77 | `FQ_401_A_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 73 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `FQ_401_A_sp_ins_Insert` | 105 | `FQ_401_A_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 74 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `FQ_401_A_sp_ins_Insert` | 124 | `FQ_401_A_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 75 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `FQ_401_A_sp_upd_Update` | 143 | `FQ_401_A_sp_upd_Update` | 147 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 76 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `FQ_401_A_sp_upd_Update` | 158 | `FQ_401_A_sp_upd_Update` | 162 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 77 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `FQ_401_A_sp_del_Delete_By_ID` | 173 | `FQ_401_A_sp_del_Delete_By_ID` | 177 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 78 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `FCommon_Insert_Log_API` | 187 | `FCommon_sp_ins_Log_API` | 193 / CSqlHelper.ExecuteScalar | not found in Database/ | Other F-prefix | Confirmed Violation |
| 79 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs / `F1041_1_sp_upd_Log_API_Trang_Thai_ID` | 206 | `F1041_1_sp_upd_Log_API_Trang_Thai_ID` | 210 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 80 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs / `FQ_422_IE_sp_sel_List_By_Created` | 16 | `FQ_422_IE_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 81 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs / `FQ_422_IE_sp_sel_Get_By_ID` | 48 | `FQ_422_IE_sp_sel_Get_By_ID` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 82 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs / `FQ_422_IE_sp_ins_Insert` | 76 | `FQ_422_IE_sp_ins_Insert` | 82 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 83 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs / `FQ_422_IE_sp_ins_Insert` | 94 | `FQ_422_IE_sp_ins_Insert` | 100 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 84 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs / `FQ_422_IE_sp_upd_Update` | 112 | `FQ_422_IE_sp_upd_Update` | 116 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 85 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs / `FQ_422_IE_sp_upd_Update` | 127 | `FQ_422_IE_sp_upd_Update` | 131 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 86 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs / `FQ_422_IE_sp_del_Delete_By_ID` | 142 | `FQ_422_IE_sp_del_Delete_By_ID` | 146 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 87 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Import_Excel_Controller.cs / `F1029_sp_sel_List_Nhat_Ky_Import_Excel` | 275 | `F1029_sp_sel_List_Nhat_Ky_Import_Excel` | 285 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 88 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs / `FQ_423_NKDN_sp_sel_List_By_Created` | 16 | `FQ_423_NKDN_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 89 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs / `FQ_423_NKDN_sp_sel_Get_By_ID` | 48 | `FQ_423_NKDN_sp_sel_Get_By_ID` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 90 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs / `FQ_423_NKDN_sp_ins_Insert` | 76 | `FQ_423_NKDN_sp_ins_Insert` | 82 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 91 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs / `FQ_423_NKDN_sp_upd_Update` | 94 | `FQ_423_NKDN_sp_upd_Update` | 98 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 92 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs / `FQ_423_NKDN_sp_upd_Update` | 108 | `FQ_423_NKDN_sp_upd_Update` | 112 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 93 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs / `FQ_423_NKDN_sp_del_Delete_By_ID` | 122 | `FQ_423_NKDN_sp_del_Delete_By_ID` | 126 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 94 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs / `F1006_sp_sel_List_Log_Nhat_Ky_Dang_Nhap_By_User` | 255 | `F1006_sp_sel_List_Log_Nhat_Ky_Dang_Nhap_By_User` | 262 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 95 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs / `F1009_sp_sel_List_Nhat_Ky_Dang_Nhap_Ca_Nhan` | 284 | `F1009_sp_sel_List_Nhat_Ky_Dang_Nhap_Ca_Nhan` | 294 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 96 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Dang_Nhap_Controller.cs / `F1010_sp_sel_List_Nhat_Ky_Dang_Nhap_All` | 317 | `F1010_sp_sel_List_Nhat_Ky_Dang_Nhap_All` | 327 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 97 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Truy_Cap_Chuc_Nang_Controller.cs / `FQ_424_NKTCCN_sp_sel_List_By_Created` | 16 | `FQ_424_NKTCCN_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 98 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Truy_Cap_Chuc_Nang_Controller.cs / `FQ_424_NKTCCN_sp_sel_Get_By_ID` | 48 | `FQ_424_NKTCCN_sp_sel_Get_By_ID` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 99 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Truy_Cap_Chuc_Nang_Controller.cs / `FQ_424_NKTCCN_sp_ins_Insert` | 76 | `FQ_424_NKTCCN_sp_ins_Insert` | 82 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 100 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Truy_Cap_Chuc_Nang_Controller.cs / `FQ_424_NKTCCN_sp_del_Delete_By_ID` | 94 | `FQ_424_NKTCCN_sp_del_Delete_By_ID` | 98 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 101 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Nhat_Ky_Truy_Cap_Chuc_Nang_Controller.cs / `F1006_sp_sel_List_Log_Nhat_Ky_Truy_Cap_Chuc_Nang_By_User` | 107 | `F1006_sp_sel_List_Log_Nhat_Ky_Truy_Cap_Chuc_Nang_By_User` | 114 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 102 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Record_Action_History_Controller.cs / `FQ_425_RAH_sp_sel_List_By_Created` | 16 | `FQ_425_RAH_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 103 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Record_Action_History_Controller.cs / `FQ_425_RAH_sp_sel_Get_By_ID` | 48 | `FQ_425_RAH_sp_sel_Get_By_ID` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 104 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Record_Action_History_Controller.cs / `FQ_425_RAH_sp_ins_Insert` | 76 | `FQ_425_RAH_sp_ins_Insert` | 82 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 105 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Record_Action_History_Controller.cs / `FQ_425_RAH_sp_ins_Insert` | 95 | `FQ_425_RAH_sp_ins_Insert` | 101 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 106 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Record_Action_History_Controller.cs / `FCommon_Sys_sp_sel_List_Log_Record_Action_History` | 114 | `FCommon_Sys_sp_sel_List_Log_Record_Action_History` | 121 / CSqlHelper.FillDataTable | Database/CoreLogActionHistory.Procedures.sql:5 | Other F-prefix | Compliant |
| 107 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs / `FQ_427_RFE_sp_sel_List_By_Created` | 16 | `FQ_427_RFE_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 108 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs / `FQ_427_RFE_sp_sel_Get_By_ID` | 48 | `FQ_427_RFE_sp_sel_Get_By_ID` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 109 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs / `FQ_427_RFE_sp_ins_Insert` | 76 | `FQ_427_RFE_sp_ins_Insert` | 82 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 110 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs / `FQ_427_RFE_sp_ins_Insert` | 95 | `FQ_427_RFE_sp_ins_Insert` | 101 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 111 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs / `FQ_427_RFE_sp_upd_Update` | 114 | `FQ_427_RFE_sp_upd_Update` | 118 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 112 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs / `FQ_427_RFE_sp_upd_Update` | 129 | `FQ_427_RFE_sp_upd_Update` | 133 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 113 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs / `FQ_427_RFE_sp_del_Delete_By_ID` | 144 | `FQ_427_RFE_sp_del_Delete_By_ID` | 148 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 114 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs / `FNLVJLA005_List_Log_Report_File_Excel_Export_By_Function_Code` | 157 | `FINLVJLA005_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code` | 164 / CSqlHelper.FillDataTable | not found in Database/ | Other F-prefix | Confirmed Violation |
| 115 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs / `FI048_List_Log_Report_File_Excel_Export_By_Function_Code` | 187 | `FI048_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code` | 194 / CSqlHelper.FillDataTable | not found in Database/ | Other F-prefix | Confirmed Violation |
| 116 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Controller.cs / `FQ_502_ASCH_sp_sel_List_By_Created` | 16 | `FQ_502_ASCH_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 117 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Controller.cs / `FQ_502_ASCH_sp_sel_List_For_Cache` | 48 | `FQ_502_ASCH_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 118 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Controller.cs / `FQ_502_ASCH_sp_sel_Get_By_ID` | 77 | `FQ_502_ASCH_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 119 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Controller.cs / `FQ_502_ASCH_sp_ins_Insert` | 105 | `FQ_502_ASCH_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 120 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Controller.cs / `FQ_502_ASCH_sp_ins_Insert` | 124 | `FQ_502_ASCH_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 121 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Controller.cs / `FQ_502_ASCH_sp_upd_Update` | 143 | `FQ_502_ASCH_sp_upd_Update` | 147 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 122 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Controller.cs / `FQ_502_ASCH_sp_upd_Update` | 158 | `FQ_502_ASCH_sp_upd_Update` | 162 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 123 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Controller.cs / `FQ_502_ASCH_sp_del_Delete_By_ID` | 173 | `FQ_502_ASCH_sp_del_Delete_By_ID` | 177 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 124 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `FQ_503_ASCHF_sp_sel_List_By_Created` | 16 | `FQ_503_ASCHF_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 125 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `FQ_503_ASCHF_sp_sel_List_By_API_Source_Chu_Hang_ID` | 48 | `FQ_503_ASCHF_sp_sel_List_By_API_Source_Chu_Hang_ID` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 126 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `FQ_503_ASCHF_sp_sel_List_For_Cache` | 77 | `FQ_503_ASCHF_sp_sel_List_For_Cache` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 127 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `FQ_503_ASCHF_sp_sel_Get_By_ID` | 106 | `FQ_503_ASCHF_sp_sel_Get_By_ID` | 113 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 128 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `FQ_503_ASCHF_sp_ins_Insert` | 134 | `FQ_503_ASCHF_sp_ins_Insert` | 140 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 129 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `FQ_503_ASCHF_sp_ins_Insert` | 152 | `FQ_503_ASCHF_sp_ins_Insert` | 158 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 130 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `F1038_sp_ins_API_Source_Chu_Hang_Function` | 178 | `F1038_sp_ins_API_Source_Chu_Hang_Function` | 184 / CSqlHelper.ExecuteScalar | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 131 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `FQ_503_ASCHF_sp_upd_Update` | 196 | `FQ_503_ASCHF_sp_upd_Update` | 200 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 132 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `FQ_503_ASCHF_sp_upd_Update` | 211 | `FQ_503_ASCHF_sp_upd_Update` | 215 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 133 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `F1038_sp_upd_API_Source_Chu_Hang_Function` | 234 | `F1038_sp_upd_API_Source_Chu_Hang_Function` | 238 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 134 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Chu_Hang_Function_Controller.cs / `FQ_503_ASCHF_sp_del_Delete_By_ID` | 248 | `FQ_503_ASCHF_sp_del_Delete_By_ID` | 252 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 135 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_sel_List_By_Created` | 16 | `FQ_501_AS_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 136 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_sel_List_For_Cache` | 48 | `FQ_501_AS_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 137 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_sel_List` | 76 | `FQ_501_AS_sp_sel_List` | 83 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 138 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_sel_Get_By_ID` | 106 | `FQ_501_AS_sp_sel_Get_By_ID` | 113 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 139 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_ins_Insert` | 134 | `FQ_501_AS_sp_ins_Insert` | 140 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 140 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_ins_Insert` | 154 | `FQ_501_AS_sp_ins_Insert` | 160 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 141 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_upd_Update` | 174 | `FQ_501_AS_sp_upd_Update` | 178 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 142 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_upd_Update` | 190 | `FQ_501_AS_sp_upd_Update` | 194 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 143 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_del_Delete_By_ID` | 206 | `FQ_501_AS_sp_del_Delete_By_ID` | 210 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 144 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FAD_Common_Get_Sys_API_Source_AD_Top_1` | 225 | `FAD_Common_sp_sel_Get_Sys_API_Source_Top_1` | 232 / CSqlHelper.FillDataTable | not found in Database/ | Other F-prefix | Confirmed Violation |
| 145 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs / `FQ_501_AS_sp_sel_Get_By_By_Ma_API_Source` | 253 | `FQ_501_AS_sp_sel_Get_By_Ma_API_Source` | 260 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Confirmed Violation |
| 146 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs / `FQ_504_ASF_sp_sel_List_By_Created` | 16 | `FQ_504_ASF_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 147 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs / `FQ_504_ASF_sp_sel_List_By_API_Source_ID` | 48 | `FQ_504_ASF_sp_sel_List_By_API_Source_ID` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 148 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs / `FQ_504_ASF_sp_sel_List_For_Cache` | 77 | `FQ_504_ASF_sp_sel_List_For_Cache` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 149 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs / `FQ_504_ASF_sp_sel_Get_By_ID` | 106 | `FQ_504_ASF_sp_sel_Get_By_ID` | 113 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 150 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs / `FQ_504_ASF_sp_ins_Insert` | 134 | `FQ_504_ASF_sp_ins_Insert` | 140 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 151 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs / `FQ_504_ASF_sp_ins_Insert` | 153 | `FQ_504_ASF_sp_ins_Insert` | 159 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 152 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs / `FQ_504_ASF_sp_upd_Update` | 172 | `FQ_504_ASF_sp_upd_Update` | 176 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 153 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs / `FQ_504_ASF_sp_upd_Update` | 187 | `FQ_504_ASF_sp_upd_Update` | 191 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 154 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Function_Controller.cs / `FQ_504_ASF_sp_del_Delete_By_ID` | 202 | `FQ_504_ASF_sp_del_Delete_By_ID` | 206 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 155 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Auto_Thread_Controller.cs / `FQ_505_AT_sp_sel_List_By_Created` | 16 | `FQ_505_AT_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 156 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Auto_Thread_Controller.cs / `FQ_505_AT_sp_sel_List_For_Cache` | 48 | `FQ_505_AT_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 157 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Auto_Thread_Controller.cs / `FQ_505_AT_sp_sel_Get_By_ID` | 77 | `FQ_505_AT_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 158 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Auto_Thread_Controller.cs / `FQ_505_AT_sp_ins_Insert` | 105 | `FQ_505_AT_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 159 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Auto_Thread_Controller.cs / `FQ_505_AT_sp_ins_Insert` | 124 | `FQ_505_AT_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 160 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Auto_Thread_Controller.cs / `FQ_505_AT_sp_upd_Update` | 143 | `FQ_505_AT_sp_upd_Update` | 147 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 161 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Auto_Thread_Controller.cs / `FQ_505_AT_sp_upd_Update` | 158 | `FQ_505_AT_sp_upd_Update` | 162 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 162 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Auto_Thread_Controller.cs / `FQ_505_AT_sp_del_Delete_By_ID` | 173 | `FQ_505_AT_sp_del_Delete_By_ID` | 177 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 163 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs / `FQ_506_CHCA_sp_sel_List` | 16 | `FQ_506_CHCA_sp_sel_List` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 164 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs / `FQ_506_CHCA_sp_sel_List_By_Created` | 48 | `FQ_506_CHCA_sp_sel_List_By_Created` | 58 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 165 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs / `FQ_506_CHCA_sp_sel_List_For_Cache` | 80 | `FQ_506_CHCA_sp_sel_List_For_Cache` | 87 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 166 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs / `FQ_506_CHCA_sp_sel_Get_By_ID` | 109 | `FQ_506_CHCA_sp_sel_Get_By_ID` | 116 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 167 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs / `FQ_506_CHCA_sp_ins_Insert` | 137 | `FQ_506_CHCA_sp_ins_Insert` | 143 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 168 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs / `FQ_506_CHCA_sp_ins_Insert` | 156 | `FQ_506_CHCA_sp_ins_Insert` | 162 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 169 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs / `FQ_506_CHCA_sp_upd_Update` | 175 | `FQ_506_CHCA_sp_upd_Update` | 179 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 170 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs / `FQ_506_CHCA_sp_upd_Update` | 190 | `FQ_506_CHCA_sp_upd_Update` | 194 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 171 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Cau_Hinh_Component_App_Controller.cs / `FQ_506_CHCA_sp_del_Delete_By_ID` | 205 | `FQ_506_CHCA_sp_del_Delete_By_ID` | 209 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 172 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_App_Controller.cs / `FQ_508_CNA_sp_sel_List_By_Created` | 17 | `FQ_508_CNA_sp_sel_List_By_Created` | 27 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 173 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_App_Controller.cs / `FQ_508_CNA_sp_sel_List_For_Cache` | 49 | `FQ_508_CNA_sp_sel_List_For_Cache` | 56 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 174 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_App_Controller.cs / `FQ_508_CNA_sp_sel_Get_By_ID` | 78 | `FQ_508_CNA_sp_sel_Get_By_ID` | 85 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 175 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_App_Controller.cs / `FQ_508_CNA_sp_ins_Insert` | 106 | `FQ_508_CNA_sp_ins_Insert` | 112 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 176 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_App_Controller.cs / `FQ_508_CNA_sp_upd_Update` | 139 | `FQ_508_CNA_sp_upd_Update` | 143 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 177 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_App_Controller.cs / `FQ_508_CNA_sp_del_Delete_By_ID` | 167 | `FQ_508_CNA_sp_del_Delete_By_ID` | 171 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 178 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_App_Controller.cs / `F1023_sp_sel_List_FApp_Func` | 185 | `F1023_sp_sel_List_FApp_Func` | 192 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 179 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_sel_List_By_Created` | 17 | `FQ_507_CN_sp_sel_List_By_Created` | 27 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 180 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_sel_List_By_Nhom_Chuc_Nang_A_Parent_Func_ID` | 49 | `FQ_507_CN_sp_sel_List_By_Nhom_Chuc_Nang_A_Parent_Func_ID` | 56 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 181 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_sel_List_By_Nhom_CN_A_Parent_Func_A_Nhom_TV_ID` | 77 | `FQ_507_CN_sp_sel_List_By_Nhom_CN_A_Parent_Func_A_Nhom_TV_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 182 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_sel_List_For_Cache` | 108 | `FQ_507_CN_sp_sel_List_For_Cache` | 115 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 183 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_sel_Get_By_ID` | 137 | `FQ_507_CN_sp_sel_Get_By_ID` | 144 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 184 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_ins_Insert` | 165 | `FQ_507_CN_sp_ins_Insert` | 171 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 185 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_ins_Insert` | 186 | `FQ_507_CN_sp_ins_Insert` | 192 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 186 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_upd_Update` | 206 | `FQ_507_CN_sp_upd_Update` | 210 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 187 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_upd_Update` | 223 | `FQ_507_CN_sp_upd_Update` | 227 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 188 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FQ_507_CN_sp_del_Delete_By_ID` | 240 | `FQ_507_CN_sp_del_Delete_By_ID` | 244 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 189 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FDev_004_Truncate_Sys_Chuc_Nang_All` | 254 | `FDev_004_sp_Truncate_Sys_Chuc_Nang_All` | 258 / CSqlHelper.ExecuteNonquery | not found in Database/ | Other F-prefix | Confirmed Violation |
| 190 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FDev_004_sp_ins_Sys_Chuc_Nang_From_API` | 267 | `FDev_004_sp_ins_Sys_Chuc_Nang_From_API` | 273 / CSqlHelper.ExecuteScalar | not found in Database/ | Other F-prefix | Needs Verification (DDL not in Database/) |
| 191 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs / `FAPI_1044_List_Sys_Chuc_Nang` | 286 | `FAPI_1044_sp_sel_List_Chuc_Nang` | 293 / CSqlHelper.FillDataTable | not found in Database/ | Other F-prefix | Confirmed Violation |
| 192 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Column_Width_Controller.cs / `FQ_509_CW_sp_sel_List_By_Created` | 16 | `FQ_509_CW_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 193 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Column_Width_Controller.cs / `FQ_509_CW_sp_sel_List_For_Cache` | 48 | `FQ_509_CW_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 194 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Column_Width_Controller.cs / `FQ_509_CW_sp_sel_Get_By_ID` | 77 | `FQ_509_CW_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 195 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Column_Width_Controller.cs / `FQ_509_CW_sp_ins_Insert` | 105 | `FQ_509_CW_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 196 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Column_Width_Controller.cs / `FQ_509_CW_sp_ins_Insert` | 124 | `FQ_509_CW_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 197 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Column_Width_Controller.cs / `FQ_509_CW_sp_upd_Update` | 142 | `FQ_509_CW_sp_upd_Update` | 146 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 198 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Column_Width_Controller.cs / `FQ_509_CW_sp_upd_Update` | 156 | `FQ_509_CW_sp_upd_Update` | 160 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 199 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Column_Width_Controller.cs / `FQ_509_CW_sp_del_Delete_By_ID` | 170 | `FQ_509_CW_sp_del_Delete_By_ID` | 174 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 200 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Daily_Schedule_Job_Controller.cs / `FQ_510_DSJ_sp_sel_List_By_Created` | 16 | `FQ_510_DSJ_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 201 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Daily_Schedule_Job_Controller.cs / `FQ_510_DSJ_sp_sel_List_For_Cache` | 48 | `FQ_510_DSJ_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 202 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Daily_Schedule_Job_Controller.cs / `FQ_510_DSJ_sp_sel_Get_By_ID` | 77 | `FQ_510_DSJ_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 203 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Daily_Schedule_Job_Controller.cs / `FQ_510_DSJ_sp_ins_Insert` | 105 | `FQ_510_DSJ_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 204 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Daily_Schedule_Job_Controller.cs / `FQ_510_DSJ_sp_ins_Insert` | 125 | `FQ_510_DSJ_sp_ins_Insert` | 131 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 205 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Daily_Schedule_Job_Controller.cs / `FQ_510_DSJ_sp_upd_Update` | 144 | `FQ_510_DSJ_sp_upd_Update` | 148 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 206 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Daily_Schedule_Job_Controller.cs / `FQ_510_DSJ_sp_upd_Update` | 159 | `FQ_510_DSJ_sp_upd_Update` | 163 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 207 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Daily_Schedule_Job_Controller.cs / `FQ_510_DSJ_sp_del_Delete_By_ID` | 174 | `FQ_510_DSJ_sp_del_Delete_By_ID` | 178 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 208 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Drill_Down_Controller.cs / `FQ_511_DD_sp_sel_List_By_Created` | 16 | `FQ_511_DD_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 209 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Drill_Down_Controller.cs / `FQ_511_DD_sp_sel_List_For_Cache` | 48 | `FQ_511_DD_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 210 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Drill_Down_Controller.cs / `FQ_511_DD_sp_sel_Get_By_ID` | 77 | `FQ_511_DD_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 211 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Drill_Down_Controller.cs / `FQ_511_DD_sp_ins_Insert` | 105 | `FQ_511_DD_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 212 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Drill_Down_Controller.cs / `FQ_511_DD_sp_ins_Insert` | 124 | `FQ_511_DD_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 213 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Drill_Down_Controller.cs / `FQ_511_DD_sp_upd_Update` | 143 | `FQ_511_DD_sp_upd_Update` | 147 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 214 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Drill_Down_Controller.cs / `FQ_511_DD_sp_upd_Update` | 158 | `FQ_511_DD_sp_upd_Update` | 162 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 215 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Drill_Down_Controller.cs / `FQ_511_DD_sp_del_Delete_By_ID` | 173 | `FQ_511_DD_sp_del_Delete_By_ID` | 177 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 216 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Filter_Date_Default_Controller.cs / `FQ_512_FDD_sp_sel_List_By_Created` | 16 | `FQ_512_FDD_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 217 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Filter_Date_Default_Controller.cs / `FQ_512_FDD_sp_sel_List_For_Cache` | 48 | `FQ_512_FDD_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 218 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Filter_Date_Default_Controller.cs / `FQ_512_FDD_sp_sel_Get_By_ID` | 77 | `FQ_512_FDD_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 219 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Filter_Date_Default_Controller.cs / `FQ_512_FDD_sp_ins_Insert` | 105 | `FQ_512_FDD_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 220 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Filter_Date_Default_Controller.cs / `FQ_512_FDD_sp_ins_Insert` | 124 | `FQ_512_FDD_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 221 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Filter_Date_Default_Controller.cs / `FQ_512_FDD_sp_upd_Update` | 143 | `FQ_512_FDD_sp_upd_Update` | 147 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 222 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Filter_Date_Default_Controller.cs / `FQ_512_FDD_sp_upd_Update` | 158 | `FQ_512_FDD_sp_upd_Update` | 162 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 223 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Filter_Date_Default_Controller.cs / `FQ_512_FDD_sp_del_Delete_By_ID` | 173 | `FQ_512_FDD_sp_del_Delete_By_ID` | 177 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 224 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Frozen_Column_Controller.cs / `FQ_513_FC_sp_sel_List_By_Created` | 16 | `FQ_513_FC_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 225 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Frozen_Column_Controller.cs / `FQ_513_FC_sp_sel_List_For_Cache` | 48 | `FQ_513_FC_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 226 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Frozen_Column_Controller.cs / `FQ_513_FC_sp_sel_Get_By_ID` | 77 | `FQ_513_FC_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 227 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Frozen_Column_Controller.cs / `FQ_513_FC_sp_ins_Insert` | 105 | `FQ_513_FC_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 228 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Frozen_Column_Controller.cs / `FQ_513_FC_sp_ins_Insert` | 123 | `FQ_513_FC_sp_ins_Insert` | 129 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 229 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Frozen_Column_Controller.cs / `FQ_513_FC_sp_upd_Update` | 141 | `FQ_513_FC_sp_upd_Update` | 145 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 230 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Frozen_Column_Controller.cs / `FQ_513_FC_sp_upd_Update` | 155 | `FQ_513_FC_sp_upd_Update` | 159 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 231 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Frozen_Column_Controller.cs / `FQ_513_FC_sp_del_Delete_By_ID` | 169 | `FQ_513_FC_sp_del_Delete_By_ID` | 173 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 232 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_sel_List_By_Created` | 16 | `FQ_514_GF_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 233 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_sel_List_For_Cache` | 48 | `FQ_514_GF_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 234 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_sel_List_By_Ma_Chuc_Nang` | 77 | `FQ_514_GF_sp_sel_List_By_Ma_Chuc_Nang` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 235 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_sel_List_By_Ma_CN_A_Ten_Grid` | 107 | `FQ_514_GF_sp_sel_List_By_Ma_CN_A_Ten_Grid` | 114 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 236 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_sel_Get_By_ID` | 136 | `FQ_514_GF_sp_sel_Get_By_ID` | 143 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 237 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_ins_Insert` | 164 | `FQ_514_GF_sp_ins_Insert` | 170 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 238 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_ins_Insert` | 185 | `FQ_514_GF_sp_ins_Insert` | 191 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 239 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_upd_Update` | 204 | `FQ_514_GF_sp_upd_Update` | 208 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 240 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_upd_Update` | 219 | `FQ_514_GF_sp_upd_Update` | 223 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 241 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FQ_514_GF_sp_del_Delete_By_ID` | 234 | `FQ_514_GF_sp_del_Delete_By_ID` | 238 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 242 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FDev_004_Truncate_Sys_Grid_Field_All` | 247 | `FDev_004_sp_Truncate_Sys_Grid_Field_All` | 251 / CSqlHelper.ExecuteNonquery | not found in Database/ | Other F-prefix | Confirmed Violation |
| 243 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FDev_004_sp_ins_Cau_Hinh_Grid_Field_From_API` | 260 | `FDev_004_sp_ins_Cau_Hinh_Grid_Field_From_API` | 266 / CSqlHelper.ExecuteScalar | not found in Database/ | Other F-prefix | Needs Verification (DDL not in Database/) |
| 244 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs / `FAPI_1044_List_Sys_Grid_Field` | 278 | `FAPI_1044_sp_sel_List_Sys_Grid_Field` | 285 / CSqlHelper.FillDataTable | not found in Database/ | Other F-prefix | Confirmed Violation |
| 245 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_sel_List_By_Created` | 16 | `FQ_516_GUG_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 246 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_sel_List_For_Cache` | 48 | `FQ_516_GUG_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 247 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_sel_List_By_Ma_CN` | 77 | `FQ_516_GUG_sp_sel_List_By_Ma_CN` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 248 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_sel_List_By_Ma_CN` | 106 | `FQ_516_GUG_sp_sel_List_By_Ma_CN` | 113 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 249 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_sel_Get_By_ID` | 135 | `FQ_516_GUG_sp_sel_Get_By_ID` | 142 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 250 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_ins_Insert` | 163 | `FQ_516_GUG_sp_ins_Insert` | 169 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 251 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_ins_Insert` | 182 | `FQ_516_GUG_sp_ins_Insert` | 188 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 252 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_upd_Update` | 201 | `FQ_516_GUG_sp_upd_Update` | 205 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 253 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_upd_Update` | 216 | `FQ_516_GUG_sp_upd_Update` | 220 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 254 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_del_Delete_By_ID` | 231 | `FQ_516_GUG_sp_del_Delete_By_ID` | 235 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 255 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_UI_Global_Controller.cs / `FQ_516_GUG_sp_del_Delete_By_Ma_CN` | 244 | `FQ_516_GUG_sp_del_Delete_By_Ma_CN` | 248 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 256 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs / `FQ_518_HG_sp_sel_List_By_Created` | 16 | `FQ_518_HG_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 257 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs / `FQ_518_HG_sp_sel_List_For_Cache` | 48 | `FQ_518_HG_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 258 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs / `FQ_518_HG_sp_sel_Get_By_ID` | 77 | `FQ_518_HG_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 259 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs / `FQ_518_HG_sp_sel_Get_By_Data` | 105 | `FQ_518_HG_sp_sel_Get_By_Data` | 112 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 260 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs / `FQ_518_HG_sp_ins_Insert` | 133 | `FQ_518_HG_sp_ins_Insert` | 139 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 261 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs / `FQ_518_HG_sp_ins_Insert` | 152 | `FQ_518_HG_sp_ins_Insert` | 158 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 262 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs / `FQ_518_HG_sp_upd_Update` | 171 | `FQ_518_HG_sp_upd_Update` | 175 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 263 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs / `FQ_518_HG_sp_upd_Update` | 186 | `FQ_518_HG_sp_upd_Update` | 190 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 264 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Help_Guide_Controller.cs / `FQ_518_HG_sp_del_Delete_By_ID` | 201 | `FQ_518_HG_sp_del_Delete_By_ID` | 205 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 265 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs / `FQ_519_HAC_sp_sel_List` | 16 | `FQ_519_HAC_sp_sel_List` | 23 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 266 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs / `FQ_519_HAC_sp_sel_List_By_Created` | 45 | `FQ_519_HAC_sp_sel_List_By_Created` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 267 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs / `FQ_519_HAC_sp_sel_List_For_Cache` | 77 | `FQ_519_HAC_sp_sel_List_For_Cache` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 268 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs / `FQ_519_HAC_sp_sel_Get_By_ID` | 106 | `FQ_519_HAC_sp_sel_Get_By_ID` | 113 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 269 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs / `FQ_519_HAC_sp_ins_Insert` | 134 | `FQ_519_HAC_sp_ins_Insert` | 140 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 270 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs / `FQ_519_HAC_sp_ins_Insert` | 153 | `FQ_519_HAC_sp_ins_Insert` | 159 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 271 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs / `FQ_519_HAC_sp_upd_Update` | 172 | `FQ_519_HAC_sp_upd_Update` | 176 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 272 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs / `FQ_519_HAC_sp_upd_Update` | 187 | `FQ_519_HAC_sp_upd_Update` | 191 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 273 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Hien_An_Column_Controller.cs / `FQ_519_HAC_sp_del_Delete_By_ID` | 202 | `FQ_519_HAC_sp_del_Delete_By_ID` | 206 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 274 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs / `FQ_520_IETC_sp_sel_List` | 16 | `FQ_520_IETC_sp_sel_List` | 23 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 275 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs / `FQ_520_IETC_sp_sel_List_By_Created` | 45 | `FQ_520_IETC_sp_sel_List_By_Created` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 276 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs / `FQ_520_IETC_sp_sel_List_For_Cache` | 77 | `FQ_520_IETC_sp_sel_List_For_Cache` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 277 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs / `FQ_520_IETC_sp_sel_Get_By_ID` | 106 | `FQ_520_IETC_sp_sel_Get_By_ID` | 113 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 278 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs / `FQ_520_IETC_sp_ins_Insert` | 134 | `FQ_520_IETC_sp_ins_Insert` | 140 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 279 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs / `FQ_520_IETC_sp_ins_Insert` | 152 | `FQ_520_IETC_sp_ins_Insert` | 158 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 280 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs / `FQ_520_IETC_sp_upd_Update` | 170 | `FQ_520_IETC_sp_upd_Update` | 174 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 281 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs / `FQ_520_IETC_sp_upd_Update` | 184 | `FQ_520_IETC_sp_upd_Update` | 188 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 282 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Import_Excel_Template_Config_Controller.cs / `FQ_520_IETC_sp_del_Delete_By_ID` | 198 | `FQ_520_IETC_sp_del_Delete_By_ID` | 202 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 283 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Language_Controller.cs / `FQ_521_L_sp_sel_List_By_Created` | 16 | `FQ_521_L_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 284 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Language_Controller.cs / `FQ_521_L_sp_sel_List_For_Cache` | 48 | `FQ_521_L_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 285 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Language_Controller.cs / `FQ_521_L_sp_sel_Get_By_ID` | 77 | `FQ_521_L_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 286 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Language_Controller.cs / `FQ_521_L_sp_ins_Insert` | 105 | `FQ_521_L_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 287 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Language_Controller.cs / `FQ_521_L_sp_ins_Insert` | 124 | `FQ_521_L_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 288 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Language_Controller.cs / `FQ_521_L_sp_upd_Update` | 143 | `FQ_521_L_sp_upd_Update` | 147 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 289 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Language_Controller.cs / `FQ_521_L_sp_upd_Update` | 158 | `FQ_521_L_sp_upd_Update` | 162 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 290 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Language_Controller.cs / `FQ_521_L_sp_del_Delete_By_ID` | 173 | `FQ_521_L_sp_del_Delete_By_ID` | 177 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 291 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Mau_Column_Controller.cs / `FQ_522_MC_sp_sel_List_By_Created` | 16 | `FQ_522_MC_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 292 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Mau_Column_Controller.cs / `FQ_522_MC_sp_sel_List_For_Cache` | 48 | `FQ_522_MC_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 293 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Mau_Column_Controller.cs / `FQ_522_MC_sp_sel_Get_By_ID` | 77 | `FQ_522_MC_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 294 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Mau_Column_Controller.cs / `FQ_522_MC_sp_ins_Insert` | 105 | `FQ_522_MC_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 295 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Mau_Column_Controller.cs / `FQ_522_MC_sp_ins_Insert` | 123 | `FQ_522_MC_sp_ins_Insert` | 129 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 296 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Mau_Column_Controller.cs / `FQ_522_MC_sp_upd_Update` | 141 | `FQ_522_MC_sp_upd_Update` | 145 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 297 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Mau_Column_Controller.cs / `FQ_522_MC_sp_upd_Update` | 155 | `FQ_522_MC_sp_upd_Update` | 159 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 298 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Mau_Column_Controller.cs / `FQ_522_MC_sp_del_Delete_By_ID` | 169 | `FQ_522_MC_sp_del_Delete_By_ID` | 173 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 299 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_Controller.cs / `FQ_523_NP_sp_sel_List_By_Created` | 17 | `FQ_523_NP_sp_sel_List_By_Created` | 27 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 300 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_Controller.cs / `FQ_523_NP_sp_sel_List_For_Cache` | 49 | `FQ_523_NP_sp_sel_List_For_Cache` | 56 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 301 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_Controller.cs / `FQ_523_NP_sp_sel_Get_By_ID` | 78 | `FQ_523_NP_sp_sel_Get_By_ID` | 85 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 302 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_Controller.cs / `FQ_523_NP_sp_ins_Insert` | 106 | `FQ_523_NP_sp_ins_Insert` | 112 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 303 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_Controller.cs / `FQ_523_NP_sp_ins_Insert` | 124 | `FQ_523_NP_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 304 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_Controller.cs / `FQ_523_NP_sp_upd_Update` | 142 | `FQ_523_NP_sp_upd_Update` | 146 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 305 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_Controller.cs / `FQ_523_NP_sp_upd_Update` | 156 | `FQ_523_NP_sp_upd_Update` | 160 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 306 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_Controller.cs / `FQ_523_NP_sp_del_Delete_By_ID` | 170 | `FQ_523_NP_sp_del_Delete_By_ID` | 174 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 307 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_sel_List_By_Created` | 17 | `FQ_524_NPU_sp_sel_List_By_Created` | 27 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 308 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_sel_List_For_Cache` | 49 | `FQ_524_NPU_sp_sel_List_For_Cache` | 56 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 309 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_sel_List_By_Ma_Dang_Nhap` | 78 | `FQ_524_NPU_sp_sel_List_By_Ma_Dang_Nhap` | 85 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 310 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_sel_Get_By_ID` | 106 | `FQ_524_NPU_sp_sel_Get_By_ID` | 113 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 311 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_ins_Insert` | 134 | `FQ_524_NPU_sp_ins_Insert` | 140 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 312 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_ins_Insert` | 152 | `FQ_524_NPU_sp_ins_Insert` | 158 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 313 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_upd_Update` | 170 | `FQ_524_NPU_sp_upd_Update` | 174 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 314 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_upd_Update` | 184 | `FQ_524_NPU_sp_upd_Update` | 188 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 315 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_del_Delete_By_ID` | 198 | `FQ_524_NPU_sp_del_Delete_By_ID` | 202 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 316 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_PDA_User_Controller.cs / `FQ_524_NPU_sp_sel_List_By_Nhom_PDA_ID` | 213 | `FQ_524_NPU_sp_sel_List_By_Nhom_PDA_ID` | 220 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 317 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_Controller.cs / `FQ_525_NTV_sp_sel_List_By_Created` | 17 | `FQ_525_NTV_sp_sel_List_By_Created` | 27 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 318 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_Controller.cs / `FQ_525_NTV_sp_sel_List_For_Cache` | 49 | `FQ_525_NTV_sp_sel_List_For_Cache` | 56 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 319 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_Controller.cs / `FQ_525_NTV_sp_sel_Get_By_ID` | 78 | `FQ_525_NTV_sp_sel_Get_By_ID` | 85 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 320 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_Controller.cs / `FQ_525_NTV_sp_ins_Insert` | 106 | `FQ_525_NTV_sp_ins_Insert` | 112 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 321 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_Controller.cs / `FQ_525_NTV_sp_ins_Insert` | 124 | `FQ_525_NTV_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 322 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_Controller.cs / `FQ_525_NTV_sp_upd_Update` | 142 | `FQ_525_NTV_sp_upd_Update` | 146 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 323 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_Controller.cs / `FQ_525_NTV_sp_upd_Update` | 156 | `FQ_525_NTV_sp_upd_Update` | 160 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 324 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_Controller.cs / `FQ_525_NTV_sp_del_Delete_By_ID` | 170 | `FQ_525_NTV_sp_del_Delete_By_ID` | 174 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 325 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs / `FQ_526_NTVU_sp_sel_List_By_Created` | 17 | `FQ_526_NTVU_sp_sel_List_By_Created` | 27 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 326 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs / `FQ_526_NTVU_sp_sel_List_For_Cache` | 49 | `FQ_526_NTVU_sp_sel_List_For_Cache` | 56 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 327 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs / `FQ_526_NTVU_sp_sel_Get_By_ID` | 78 | `FQ_526_NTVU_sp_sel_Get_By_ID` | 85 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 328 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs / `FQ_526_NTVU_sp_ins_Insert` | 106 | `FQ_526_NTVU_sp_ins_Insert` | 112 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 329 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs / `FQ_526_NTVU_sp_ins_Insert` | 124 | `FQ_526_NTVU_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 330 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs / `FQ_526_NTVU_sp_upd_Update` | 142 | `FQ_526_NTVU_sp_upd_Update` | 146 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 331 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs / `FQ_526_NTVU_sp_upd_Update` | 156 | `FQ_526_NTVU_sp_upd_Update` | 160 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 332 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs / `FQ_526_NTVU_sp_del_Delete_By_ID` | 170 | `FQ_526_NTVU_sp_del_Delete_By_ID` | 174 / CSqlHelper.ExecuteNonquery | Database/MemberGroupSynchronization.Procedures.sql:1 | FQ | Compliant |
| 333 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Nhom_Thanh_Vien_User_Controller.cs / `FQ_526_NTVU_sp_sel_List_By_Nhom_TV_ID` | 190 | `FQ_526_NTVU_sp_sel_List_By_Nhom_TV_ID` | 197 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 334 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `FQ_527_PQCN_sp_sel_List_By_Created` | 16 | `FQ_527_PQCN_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 335 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `FQ_527_PQCN_sp_sel_List_For_Cache` | 48 | `FQ_527_PQCN_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 336 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `FQ_527_PQCN_sp_sel_Get_By_ID` | 77 | `FQ_527_PQCN_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 337 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `FQ_527_PQCN_sp_ins_Insert` | 105 | `FQ_527_PQCN_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 338 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `FQ_527_PQCN_sp_ins_Insert` | 124 | `FQ_527_PQCN_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 339 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `FQ_527_PQCN_sp_upd_Update` | 143 | `FQ_527_PQCN_sp_upd_Update` | 147 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 340 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `FQ_527_PQCN_sp_upd_Update` | 158 | `FQ_527_PQCN_sp_upd_Update` | 162 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 341 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `FQ_527_PQCN_sp_del_Delete_By_ID` | 173 | `FQ_527_PQCN_sp_del_Delete_By_ID` | 177 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 342 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `F1005_sp_upd_Phan_Quyen_Chuc_Nang_View` | 188 | `F1005_sp_upd_Phan_Quyen_Chuc_Nang_View` | 192 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 343 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `F1005_sp_upd_Phan_Quyen_Chuc_Nang_Add` | 203 | `F1005_sp_upd_Phan_Quyen_Chuc_Nang_Add` | 207 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 344 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `F1005_sp_upd_Phan_Quyen_Chuc_Nang_Edit` | 218 | `F1005_sp_upd_Phan_Quyen_Chuc_Nang_Edit` | 222 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 345 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `F1005_sp_upd_Phan_Quyen_Chuc_Nang_Delete` | 233 | `F1005_sp_upd_Phan_Quyen_Chuc_Nang_Delete` | 237 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 346 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Phan_Quyen_Chuc_Nang_Controller.cs / `F1005_sp_upd_Phan_Quyen_Chuc_Nang_Export` | 248 | `F1005_sp_upd_Phan_Quyen_Chuc_Nang_Export` | 252 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 347 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Report_Template_Config_Controller.cs / `FQ_528_RTC_sp_sel_List_By_Created` | 16 | `FQ_528_RTC_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 348 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Report_Template_Config_Controller.cs / `FQ_528_RTC_sp_sel_List_For_Cache` | 48 | `FQ_528_RTC_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 349 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Report_Template_Config_Controller.cs / `FQ_528_RTC_sp_sel_Get_By_ID` | 77 | `FQ_528_RTC_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 350 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Report_Template_Config_Controller.cs / `FQ_528_RTC_sp_ins_Insert` | 105 | `FQ_528_RTC_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 351 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Report_Template_Config_Controller.cs / `FQ_528_RTC_sp_ins_Insert` | 123 | `FQ_528_RTC_sp_ins_Insert` | 129 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 352 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Report_Template_Config_Controller.cs / `FQ_528_RTC_sp_upd_Update` | 141 | `FQ_528_RTC_sp_upd_Update` | 145 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 353 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Report_Template_Config_Controller.cs / `FQ_528_RTC_sp_upd_Update` | 155 | `FQ_528_RTC_sp_upd_Update` | 159 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 354 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Report_Template_Config_Controller.cs / `FQ_528_RTC_sp_del_Delete_By_ID` | 169 | `FQ_528_RTC_sp_del_Delete_By_ID` | 173 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 355 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Controller.cs / `FQ_529_SN_sp_sel_List_By_Created` | 16 | `FQ_529_SN_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 356 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Controller.cs / `FQ_529_SN_sp_sel_List_For_Cache` | 48 | `FQ_529_SN_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 357 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Controller.cs / `FQ_529_SN_sp_sel_Get_By_ID` | 77 | `FQ_529_SN_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 358 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Controller.cs / `FQ_529_SN_sp_ins_Insert` | 105 | `FQ_529_SN_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 359 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Controller.cs / `FQ_529_SN_sp_ins_Insert` | 124 | `FQ_529_SN_sp_ins_Insert` | 130 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 360 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Controller.cs / `FQ_529_SN_sp_upd_Update` | 143 | `FQ_529_SN_sp_upd_Update` | 147 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 361 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Controller.cs / `FQ_529_SN_sp_upd_Update` | 158 | `FQ_529_SN_sp_upd_Update` | 162 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 362 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Controller.cs / `FQ_529_SN_sp_del_Delete_By_ID` | 173 | `FQ_529_SN_sp_del_Delete_By_ID` | 177 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 363 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_sel_List_By_Created` | 16 | `FQ_530_SND_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 364 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_sel_List_For_Cache` | 48 | `FQ_530_SND_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 365 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_sel_Get_By_ID` | 77 | `FQ_530_SND_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 366 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_sel_Get_By_ID` | 105 | `FQ_530_SND_sp_sel_Get_By_ID` | 112 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 367 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_ins_Insert` | 133 | `FQ_530_SND_sp_ins_Insert` | 139 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 368 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_ins_Insert` | 151 | `FQ_530_SND_sp_ins_Insert` | 157 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 369 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_upd_Update` | 169 | `FQ_530_SND_sp_upd_Update` | 173 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 370 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_upd_Update` | 183 | `FQ_530_SND_sp_upd_Update` | 187 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 371 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_del_Delete_By_ID` | 197 | `FQ_530_SND_sp_del_Delete_By_ID` | 201 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 372 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_STT_Next_Detail_Controller.cs / `FQ_530_SND_sp_sel_List_By_STT_ID` | 211 | `FQ_530_SND_sp_sel_List_By_STT_ID` | 218 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 373 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `FQ_531_TV_sp_sel_List_By_Created` | 16 | `FQ_531_TV_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 374 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `FQ_531_TV_sp_sel_List` | 47 | `FQ_531_TV_sp_sel_List` | 54 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 375 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `FQ_531_TV_sp_sel_List_For_Cache` | 77 | `FQ_531_TV_sp_sel_List_For_Cache` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 376 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `FQ_531_TV_sp_sel_Get_By_ID` | 106 | `FQ_531_TV_sp_sel_Get_By_ID` | 113 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 377 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `FQ_531_TV_sp_ins_Insert` | 134 | `FQ_531_TV_sp_ins_Insert` | 140 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 378 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `FQ_531_TV_sp_ins_Insert` | 153 | `FQ_531_TV_sp_ins_Insert` | 159 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 379 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `FQ_531_TV_sp_upd_Update` | 173 | `FQ_531_TV_sp_upd_Update` | 177 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 380 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `FQ_531_TV_sp_del_Delete_By_ID` | 189 | `FQ_531_TV_sp_del_Delete_By_ID` | 193 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 381 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `FQ_531_TV_sp_sel_Get_By_Ma_Dang_Nhap` | 203 | `FQ_531_TV_sp_sel_Get_By_Ma_Dang_Nhap` | 210 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 382 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `F1004_sp_sel_List_Thanh_Vien_Khong_Thuoc_Nhom` | 232 | `F1004_sp_sel_List_Thanh_Vien_Khong_Thuoc_Nhom` | 239 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 383 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `F1007_sp_upd_Doi_Mat_Khau` | 261 | `F1007_sp_upd_Doi_Mat_Khau` | 265 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 384 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `F1011_sp_sel_List_Thanh_Vien_Khong_Thuoc_Chu_Hang` | 275 | `F1011_sp_sel_List_Thanh_Vien_Khong_Thuoc_Chu_Hang` | 282 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 385 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `F1012_sp_sel_List_Thanh_Vien_Khong_Thuoc_Kho` | 304 | `F1012_sp_sel_List_Thanh_Vien_Khong_Thuoc_Kho` | 311 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 386 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `F1024_sp_sel_List_Thanh_Vien_Khong_Thuoc_Du_An` | 333 | `F1024_sp_sel_List_Thanh_Vien_Khong_Thuoc_Du_An` | 340 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 387 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `F1037_sp_sel_List_Thanh_Vien_Khong_Thuoc_Nhom_PDA` | 362 | `F1037_sp_sel_List_Thanh_Vien_Khong_Thuoc_Nhom_PDA` | 369 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 388 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `F1025_sp_sel_List_Sys_Thanh_Vien` | 392 | `F1025_sp_sel_List_Sys_Thanh_Vien` | 400 / CSqlHelper.FillDataTable | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 389 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `F1025_sp_upd_Reset_Mat_Khau_Thanh_Vien` | 422 | `F1025_sp_upd_Reset_Mat_Khau_Thanh_Vien` | 426 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 390 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Thanh_Vien_Controller.cs / `F1005_sp_upd_Doi_Mat_Khau_Thanh_Vien` | 436 | `F1005_sp_upd_Doi_Mat_Khau_Thanh_Vien` | 440 / CSqlHelper.ExecuteNonquery | not found in Database/ | Fxxxx | Needs Verification (DDL not in Database/) |
| 391 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Token_Controller.cs / `FQ_532_T_sp_sel_List_By_Created` | 16 | `FQ_532_T_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 392 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Token_Controller.cs / `FQ_532_T_sp_sel_List_For_Cache` | 48 | `FQ_532_T_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 393 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Token_Controller.cs / `FQ_532_T_sp_sel_Get_By_ID` | 77 | `FQ_532_T_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 394 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Token_Controller.cs / `FQ_532_T_sp_ins_Insert` | 105 | `FQ_532_T_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 395 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Token_Controller.cs / `FQ_532_T_sp_upd_Update` | 124 | `FQ_532_T_sp_upd_Update` | 128 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 396 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Token_Controller.cs / `FQ_532_T_sp_del_Delete_By_ID` | 139 | `FQ_532_T_sp_del_Delete_By_ID` | 143 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 397 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Token_Controller.cs / `FQ_532_T_sp_sel_Get_By_Token_ID` | 152 | `FQ_532_T_sp_sel_Get_By_Token_ID` | 159 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 398 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Token_Controller.cs / `FQ_532_T_sp_sel_Get_By_Ma_Dang_Nhap` | 181 | `FQ_532_T_sp_sel_Get_By_Ma_Dang_Nhap` | 188 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 399 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Chu_Hang_Controller.cs / `FQ_534_WCH_sp_sel_List_By_Created` | 16 | `FQ_534_WCH_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 400 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Chu_Hang_Controller.cs / `FQ_534_WCH_sp_sel_Get_By_ID` | 49 | `FQ_534_WCH_sp_sel_Get_By_ID` | 56 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 401 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Chu_Hang_Controller.cs / `FQ_534_WCH_sp_ins_Insert` | 77 | `FQ_534_WCH_sp_ins_Insert` | 83 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 402 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Chu_Hang_Controller.cs / `FQ_534_WCH_sp_ins_Insert` | 95 | `FQ_534_WCH_sp_ins_Insert` | 101 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 403 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Chu_Hang_Controller.cs / `FQ_534_WCH_sp_upd_Update` | 113 | `FQ_534_WCH_sp_upd_Update` | 117 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 404 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Chu_Hang_Controller.cs / `FQ_534_WCH_sp_upd_Update` | 127 | `FQ_534_WCH_sp_upd_Update` | 131 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 405 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Chu_Hang_Controller.cs / `FQ_534_WCH_sp_del_Delete_By_ID` | 141 | `FQ_534_WCH_sp_del_Delete_By_ID` | 145 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 406 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Controller.cs / `FQ_533_W_sp_sel_List_By_Created` | 16 | `FQ_533_W_sp_sel_List_By_Created` | 26 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 407 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Controller.cs / `FQ_533_W_sp_sel_List_For_Cache` | 48 | `FQ_533_W_sp_sel_List_For_Cache` | 55 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 408 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Controller.cs / `FQ_533_W_sp_sel_Get_By_ID` | 77 | `FQ_533_W_sp_sel_Get_By_ID` | 84 / CSqlHelper.FillDataTable | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 409 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Controller.cs / `FQ_533_W_sp_ins_Insert` | 105 | `FQ_533_W_sp_ins_Insert` | 111 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 410 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Controller.cs / `FQ_533_W_sp_ins_Insert` | 123 | `FQ_533_W_sp_ins_Insert` | 129 / CSqlHelper.ExecuteScalar | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 411 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Controller.cs / `FQ_533_W_sp_upd_Update` | 141 | `FQ_533_W_sp_upd_Update` | 145 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 412 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Controller.cs / `FQ_533_W_sp_upd_Update` | 155 | `FQ_533_W_sp_upd_Update` | 159 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 413 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Webhook_Controller.cs / `FQ_533_W_sp_del_Delete_By_ID` | 169 | `FQ_533_W_sp_del_Delete_By_ID` | 173 / CSqlHelper.ExecuteNonquery | not found in Database/ | FQ | Needs Verification (DDL not in Database/) |
| 414 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CWarehouseDocumentSupport_Controller.cs / `sp_XNK_Document_Page` | 9 | `sp_XNK_Document_Page` | 17 / Page_From_Procedure | Database/WarehouseModule.Procedures.sql:5068 | sp_ | Out of Scope/Exception candidate (technical/support prefix) |
| 415 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CWarehouseDocumentSupport_Controller.cs / `sp_XNK_Document_Post` | 21 | `sp_XNK_Document_Post` | 29 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:4597 | sp_ | Out of Scope/Exception candidate (technical/support prefix) |
| 416 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_sel_List_Nhap_Kho` | 12 | `F3001_sp_sel_List_Nhap_Kho` | 18 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:5012 | Fxxxx | Compliant |
| 417 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_sel_List_Nhap_Kho_Detail` | 37 | `F3001_sp_sel_List_Nhap_Kho_Detail` | 43 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:5038 | Fxxxx | Compliant |
| 418 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_ins_Nhap_Kho_Header` | 62 | `F3001_sp_ins_Nhap_Kho_Header` | 64 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4211 | Fxxxx | Compliant |
| 419 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_ins_Nhap_Kho_Header` | 77 | `F3001_sp_ins_Nhap_Kho_Header` | 79 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4211 | Fxxxx | Compliant |
| 420 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_upd_Nhap_Kho_Header` | 92 | `F3001_sp_upd_Nhap_Kho_Header` | 94 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4283 | Fxxxx | Compliant |
| 421 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_del_Nhap_Kho_Header` | 106 | `F3001_sp_del_Nhap_Kho_Header` | 108 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:4507 | Fxxxx | Compliant |
| 422 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_ins_Nhap_Kho_Detail` | 113 | `F3001_sp_ins_Nhap_Kho_Detail` | 115 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4360 | Fxxxx | Compliant |
| 423 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_ins_Nhap_Kho_Detail` | 124 | `F3001_sp_ins_Nhap_Kho_Detail` | 126 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4360 | Fxxxx | Compliant |
| 424 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_upd_Nhap_Kho_Detail` | 135 | `F3001_sp_upd_Nhap_Kho_Detail` | 137 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:4434 | Fxxxx | Compliant |
| 425 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Nhap_Kho_Controller.cs / `F3001_sp_del_Nhap_Kho_Detail` | 146 | `F3001_sp_del_Nhap_Kho_Detail` | 148 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:5697 | Fxxxx | Compliant |
| 426 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_sel_List_Xuat_Kho` | 12 | `F3002_sp_sel_List_Xuat_Kho` | 18 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:5025 | Fxxxx | Compliant |
| 427 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_sel_List_Xuat_Kho_Detail` | 37 | `F3002_sp_sel_List_Xuat_Kho_Detail` | 43 / CSqlHelper.FillDataTable | Database/WarehouseModule.Procedures.sql:5053 | Fxxxx | Compliant |
| 428 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_ins_Xuat_Kho_Header` | 62 | `F3002_sp_ins_Xuat_Kho_Header` | 64 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5275 | Fxxxx | Compliant |
| 429 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_ins_Xuat_Kho_Header` | 74 | `F3002_sp_ins_Xuat_Kho_Header` | 76 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5275 | Fxxxx | Compliant |
| 430 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_upd_Xuat_Kho_Header` | 86 | `F3002_sp_upd_Xuat_Kho_Header` | 88 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5364 | Fxxxx | Compliant |
| 431 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_del_Xuat_Kho_Header` | 98 | `F3002_sp_del_Xuat_Kho_Header` | 100 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:5845 | Fxxxx | Compliant |
| 432 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_ins_Xuat_Kho_Detail` | 105 | `F3002_sp_ins_Xuat_Kho_Detail` | 107 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5455 | Fxxxx | Compliant |
| 433 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_ins_Xuat_Kho_Detail` | 116 | `F3002_sp_ins_Xuat_Kho_Detail` | 118 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5455 | Fxxxx | Compliant |
| 434 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_upd_Xuat_Kho_Detail` | 127 | `F3002_sp_upd_Xuat_Kho_Detail` | 129 / CSqlHelper.ExecuteScalar | Database/WarehouseModule.Procedures.sql:5579 | Fxxxx | Compliant |
| 435 | TKS_Thuc_Tap_V11_Data_Access/Controller/XNK/CXNK_Xuat_Kho_Controller.cs / `F3002_sp_del_Xuat_Kho_Detail` | 138 | `F3002_sp_del_Xuat_Kho_Detail` | 140 / CSqlHelper.ExecuteNonquery | Database/WarehouseModule.Procedures.sql:5753 | Fxxxx | Compliant |

### 3.3 Dynamic helper outside concrete route inventory

| Controller / method | Method line | Observed call | Assessment |
| --- | --- | --- | --- |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CWarehouse_Controller_Base.cs / `Fill_Page_Data` | 34 | 53 / CSqlHelper.CreateConnection with `p_strProcedure` | Out of Scope/Exception: generic page helper; the concrete Store is supplied by its callers |

## 4. SQL definition inventory and source coverage

### 4.1 Canonical definitions in `Database/`

| # | Definition | Path:line | Category |
| --- | --- | --- | --- |
| 1 | F2009_sp_del_Kho | Database/WarehouseModule.Procedures.sql:3712 | Fxxxx |
| 2 | F2009_sp_ins_Kho | Database/WarehouseModule.Procedures.sql:5218 | Fxxxx |
| 3 | F2009_sp_sel_Get_By_ID | Database/WarehouseMaster.GetById.Procedures.sql:1 | Fxxxx |
| 4 | F2009_sp_sel_List_Kho | Database/WarehouseModule.Procedures.sql:3746 | Fxxxx |
| 5 | F2009_sp_sel_List_Kho_By_Created | Database/WarehouseMaster.ByCreated.Procedures.sql:10 | Fxxxx |
| 6 | F2009_sp_sel_List_Kho_For_Cache | Database/WarehouseModule.Procedures.sql:3786 | Fxxxx |
| 7 | F2009_sp_sel_List_Kho_Lookup | Database/WarehouseModule.Procedures.sql:3826 | Fxxxx |
| 8 | F2009_sp_upd_Kho | Database/WarehouseModule.Procedures.sql:5247 | Fxxxx |
| 9 | F2015_sp_del_Kho_User | Database/WarehouseModule.Procedures.sql:4972 | Fxxxx |
| 10 | F2015_sp_ins_Kho_User | Database/WarehouseModule.Procedures.sql:4935 | Fxxxx |
| 11 | F2015_sp_sel_List_Kho_User | Database/WarehouseModule.Procedures.sql:4981 | Fxxxx |
| 12 | F2015_sp_sel_List_Sys_Thanh_Vien_Cho_Phan_Quyen_Kho | Database/WarehouseModule.Procedures.sql:4589 | Fxxxx |
| 13 | F2015_sp_upd_Kho_User | Database/WarehouseModule.Procedures.sql:4954 | Fxxxx |
| 14 | F2016_sp_del_Don_Vi_Tinh | Database/WarehouseModule.Procedures.sql:3596 | Fxxxx |
| 15 | F2016_sp_ins_Don_Vi_Tinh | Database/WarehouseModule.Procedures.sql:3943 | Fxxxx |
| 16 | F2016_sp_sel_Get_By_ID | Database/WarehouseMaster.GetById.Procedures.sql:13 | Fxxxx |
| 17 | F2016_sp_sel_List_Don_Vi_Tinh | Database/WarehouseModule.Procedures.sql:3754 | Fxxxx |
| 18 | F2016_sp_sel_List_Don_Vi_Tinh_By_Created | Database/WarehouseMaster.ByCreated.Procedures.sql:30 | Fxxxx |
| 19 | F2016_sp_sel_List_Don_Vi_Tinh_For_Cache | Database/WarehouseModule.Procedures.sql:3794 | Fxxxx |
| 20 | F2016_sp_sel_List_Don_Vi_Tinh_Lookup | Database/WarehouseModule.Procedures.sql:3834 | Fxxxx |
| 21 | F2016_sp_upd_Don_Vi_Tinh | Database/WarehouseModule.Procedures.sql:3974 | Fxxxx |
| 22 | F2017_sp_del_Loai_San_Pham | Database/WarehouseModule.Procedures.sql:3625 | Fxxxx |
| 23 | F2017_sp_ins_Loai_San_Pham | Database/WarehouseModule.Procedures.sql:4003 | Fxxxx |
| 24 | F2017_sp_sel_Get_By_ID | Database/WarehouseMaster.GetById.Procedures.sql:25 | Fxxxx |
| 25 | F2017_sp_sel_List_Loai_San_Pham | Database/WarehouseModule.Procedures.sql:3762 | Fxxxx |
| 26 | F2017_sp_sel_List_Loai_San_Pham_By_Created | Database/WarehouseMaster.ByCreated.Procedures.sql:50 | Fxxxx |
| 27 | F2017_sp_sel_List_Loai_San_Pham_For_Cache | Database/WarehouseModule.Procedures.sql:3802 | Fxxxx |
| 28 | F2017_sp_sel_List_Loai_San_Pham_Lookup | Database/WarehouseModule.Procedures.sql:3842 | Fxxxx |
| 29 | F2017_sp_upd_Loai_San_Pham | Database/WarehouseModule.Procedures.sql:4034 | Fxxxx |
| 30 | F2018_sp_del_San_Pham | Database/WarehouseModule.Procedures.sql:3654 | Fxxxx |
| 31 | F2018_sp_ins_San_Pham | Database/WarehouseModule.Procedures.sql:5096 | Fxxxx |
| 32 | F2018_sp_sel_Get_By_ID | Database/WarehouseMaster.GetById.Procedures.sql:37 | Fxxxx |
| 33 | F2018_sp_sel_List_San_Pham | Database/WarehouseModule.Procedures.sql:3770 | Fxxxx |
| 34 | F2018_sp_sel_List_San_Pham_By_Created | Database/WarehouseMaster.ByCreated.Procedures.sql:70 | Fxxxx |
| 35 | F2018_sp_sel_List_San_Pham_For_Cache | Database/WarehouseModule.Procedures.sql:3810 | Fxxxx |
| 36 | F2018_sp_sel_List_San_Pham_Lookup | Database/WarehouseModule.Procedures.sql:3850 | Fxxxx |
| 37 | F2018_sp_upd_San_Pham | Database/WarehouseModule.Procedures.sql:5127 | Fxxxx |
| 38 | F2019_sp_del_NCC | Database/WarehouseModule.Procedures.sql:3683 | Fxxxx |
| 39 | F2019_sp_ins_NCC | Database/WarehouseModule.Procedures.sql:5157 | Fxxxx |
| 40 | F2019_sp_sel_Get_By_ID | Database/WarehouseMaster.GetById.Procedures.sql:50 | Fxxxx |
| 41 | F2019_sp_sel_List_NCC | Database/WarehouseModule.Procedures.sql:3778 | Fxxxx |
| 42 | F2019_sp_sel_List_NCC_By_Created | Database/WarehouseMaster.ByCreated.Procedures.sql:90 | Fxxxx |
| 43 | F2019_sp_sel_List_NCC_For_Cache | Database/WarehouseModule.Procedures.sql:3818 | Fxxxx |
| 44 | F2019_sp_sel_List_NCC_Lookup | Database/WarehouseModule.Procedures.sql:3858 | Fxxxx |
| 45 | F2019_sp_upd_NCC | Database/WarehouseModule.Procedures.sql:5187 | Fxxxx |
| 46 | F3001_sp_del_Nhap_Kho_Detail | Database/WarehouseModule.Procedures.sql:5697 | Fxxxx |
| 47 | F3001_sp_del_Nhap_Kho_Header | Database/WarehouseModule.Procedures.sql:4507 | Fxxxx |
| 48 | F3001_sp_ins_Nhap_Kho_Detail | Database/WarehouseModule.Procedures.sql:4360 | Fxxxx |
| 49 | F3001_sp_ins_Nhap_Kho_Header | Database/WarehouseModule.Procedures.sql:4211 | Fxxxx |
| 50 | F3001_sp_sel_List_Nhap_Kho | Database/WarehouseModule.Procedures.sql:5012 | Fxxxx |
| 51 | F3001_sp_sel_List_Nhap_Kho_Detail | Database/WarehouseModule.Procedures.sql:5038 | Fxxxx |
| 52 | F3001_sp_upd_Nhap_Kho_Detail | Database/WarehouseModule.Procedures.sql:4434 | Fxxxx |
| 53 | F3001_sp_upd_Nhap_Kho_Header | Database/WarehouseModule.Procedures.sql:4283 | Fxxxx |
| 54 | F3002_sp_del_Xuat_Kho_Detail | Database/WarehouseModule.Procedures.sql:5753 | Fxxxx |
| 55 | F3002_sp_del_Xuat_Kho_Header | Database/WarehouseModule.Procedures.sql:5845 | Fxxxx |
| 56 | F3002_sp_ins_Xuat_Kho_Detail | Database/WarehouseModule.Procedures.sql:5455 | Fxxxx |
| 57 | F3002_sp_ins_Xuat_Kho_Header | Database/WarehouseModule.Procedures.sql:5275 | Fxxxx |
| 58 | F3002_sp_sel_List_Xuat_Kho | Database/WarehouseModule.Procedures.sql:5025 | Fxxxx |
| 59 | F3002_sp_sel_List_Xuat_Kho_Detail | Database/WarehouseModule.Procedures.sql:5053 | Fxxxx |
| 60 | F3002_sp_upd_Xuat_Kho_Detail | Database/WarehouseModule.Procedures.sql:5579 | Fxxxx |
| 61 | F3002_sp_upd_Xuat_Kho_Header | Database/WarehouseModule.Procedures.sql:5364 | Fxxxx |
| 62 | F9001_sp_Ton_Kho_Hien_Tai_Page | Database/WarehouseModule.Procedures.sql:6647 | Fxxxx |
| 63 | F9002_sp_Chi_Tiet_Nhap | Database/WarehouseModule.Procedures.sql:4067 | Fxxxx |
| 64 | F9002_sp_Chi_Tiet_Nhap_Page | Database/WarehouseModule.Procedures.sql:4095 | Fxxxx |
| 65 | F9002_sp_Chi_Tiet_Xuat | Database/WarehouseModule.Procedures.sql:4081 | Fxxxx |
| 66 | F9002_sp_Chi_Tiet_Xuat_Page | Database/WarehouseModule.Procedures.sql:4153 | Fxxxx |
| 67 | F9002_sp_Xuat_Nhap_Ton | Database/WarehouseModule.Procedures.sql:6438 | Fxxxx |
| 68 | F9002_sp_Xuat_Nhap_Ton_Page | Database/WarehouseModule.Procedures.sql:6795 | Fxxxx |
| 69 | FCommon_Sys_sp_sel_List_Log_Record_Action_History | Database/CoreLogActionHistory.Procedures.sql:5 | Other F-prefix |
| 70 | FQ_526_NTVU_sp_del_Delete_By_ID | Database/MemberGroupSynchronization.Procedures.sql:1 | FQ |
| 71 | sp_DM_Kho_User_Ensure_Access | Database/WarehouseModule.Procedures.sql:4559 | sp_ |
| 72 | sp_DM_Kho_User_List_Allowed | Database/WarehouseModule.Procedures.sql:4576 | sp_ |
| 73 | sp_DM_Kho_User_Page | Database/WarehouseModule.Procedures.sql:4994 | sp_ |
| 74 | sp_DM_Lookup_Page | Database/WarehouseModule.Procedures.sql:3906 | sp_ |
| 75 | sp_DM_Master_Page | Database/WarehouseModule.Procedures.sql:3869 | sp_ |
| 76 | sp_Inventory_Balance_Daily_Bootstrap_From_Movement | Database/WarehouseModule.Procedures.sql:7481 | sp_ |
| 77 | sp_Inventory_Balance_Daily_Rebuild | Database/WarehouseModule.Procedures.sql:7211 | sp_ |
| 78 | sp_Inventory_Fence_Acquire_Group | Database/WarehouseModule.Procedures.sql:242 | sp_ |
| 79 | sp_Inventory_Fence_Acquire_Group_Set | Database/WarehouseModule.Procedures.sql:310 | sp_ |
| 80 | sp_Inventory_Fence_Acquire_Legacy_Movement_Bootstrap | Database/WarehouseModule.Procedures.sql:603 | sp_ |
| 81 | sp_Inventory_Fence_Acquire_Legacy_Scope | Database/WarehouseModule.Procedures.sql:350 | sp_ |
| 82 | sp_Inventory_Fence_Acquire_Legacy_Scope_Set | Database/WarehouseModule.Procedures.sql:416 | sp_ |
| 83 | sp_Inventory_Fence_Acquire_Legacy_Snapshot | Database/WarehouseModule.Procedures.sql:477 | sp_ |
| 84 | sp_Inventory_Fence_Acquire_Legacy_Snapshot_Bootstrap | Database/WarehouseModule.Procedures.sql:638 | sp_ |
| 85 | sp_Inventory_Fence_Acquire_Legacy_Snapshot_Set | Database/WarehouseModule.Procedures.sql:544 | sp_ |
| 86 | sp_Inventory_Fence_Acquire_Root | Database/WarehouseModule.Procedures.sql:189 | sp_ |
| 87 | sp_Inventory_Fence_Acquire_Snapshot_Finalize_Singleton | Database/WarehouseModule.Procedures.sql:668 | sp_ |
| 88 | sp_Inventory_Fence_Acquire_Snapshot_Worker_Singleton | Database/WarehouseModule.Procedures.sql:694 | sp_ |
| 89 | sp_Inventory_Fence_Release_Snapshot_Worker_Singleton | Database/WarehouseModule.Procedures.sql:712 | sp_ |
| 90 | sp_Inventory_Fence_Require_Context | Database/WarehouseModule.Procedures.sql:726 | sp_ |
| 91 | sp_Inventory_Fence_Require_Transaction | Database/WarehouseModule.Procedures.sql:179 | sp_ |
| 92 | sp_Inventory_Movement_Apply_Invalidation | Database/WarehouseModule.Procedures.sql:7016 | sp_ |
| 93 | sp_Inventory_Movement_Bootstrap_From_Ledger | Database/WarehouseModule.Procedures.sql:8070 | sp_ |
| 94 | sp_Inventory_Movement_Complete_Claim | Database/WarehouseModule.Procedures.sql:7143 | sp_ |
| 95 | sp_Inventory_Movement_Process_RebuildQueue | Database/WarehouseModule.Procedures.sql:7751 | sp_ |
| 96 | sp_Inventory_Movement_Rebuild | Database/WarehouseModule.Procedures.sql:7632 | sp_ |
| 97 | sp_Inventory_Reconciliation_Run | Database/WarehouseModule.Procedures.sql:2970 | sp_ |
| 98 | sp_Inventory_Report_Acquire_Scope_Fence | Database/WarehouseModule.Procedures.sql:6153 | sp_ |
| 99 | sp_Inventory_Report_Validate_Scope_Fence | Database/WarehouseModule.Procedures.sql:6052 | sp_ |
| 100 | sp_Inventory_Snapshot_Apply_Invalidation | Database/WarehouseModule.Procedures.sql:865 | sp_ |
| 101 | sp_Inventory_Snapshot_Bootstrap_From_Ledger | Database/WarehouseModule.Procedures.sql:1319 | sp_ |
| 102 | sp_Inventory_Snapshot_Complete_Claim | Database/WarehouseModule.Procedures.sql:1851 | sp_ |
| 103 | sp_Inventory_Snapshot_Create_Daily | Database/WarehouseModule.Procedures.sql:2951 | sp_ |
| 104 | sp_Inventory_Snapshot_Finalize_Daily | Database/WarehouseModule.Procedures.sql:1568 | sp_ |
| 105 | sp_Inventory_Snapshot_Initialize_From_Ledger | Database/WarehouseModule.Procedures.sql:1462 | sp_ |
| 106 | sp_Inventory_Snapshot_Invalidate_From | Database/WarehouseModule.Procedures.sql:3502 | sp_ |
| 107 | sp_Inventory_Snapshot_Monitor | Database/WarehouseModule.Procedures.sql:3396 | sp_ |
| 108 | sp_Inventory_Snapshot_Process_RebuildQueue | Database/WarehouseModule.Procedures.sql:1922 | sp_ |
| 109 | sp_Inventory_Snapshot_Rebuild | Database/WarehouseModule.Procedures.sql:1049 | sp_ |
| 110 | sp_Inventory_Snapshot_Record_Heartbeat | Database/WarehouseModule.Procedures.sql:1272 | sp_ |
| 111 | sp_XNK_Document_Page | Database/WarehouseModule.Procedures.sql:5068 | sp_ |
| 112 | sp_XNK_Document_Post | Database/WarehouseModule.Procedures.sql:4597 | sp_ |
| 113 | sp_XNK_InventoryBalance_Rebuild | Database/WarehouseModule.Procedures.sql:4903 | sp_ |
| 114 | sp_XNK_Reservation_Adjust | Database/WarehouseModule.Procedures.sql:2459 | sp_ |
| 115 | sp_XNK_Reservation_Move_Document | Database/WarehouseModule.Procedures.sql:2519 | sp_ |
| 116 | sp_XNK_Reservation_Rebuild | Database/WarehouseModule.Procedures.sql:2781 | sp_ |
| 117 | sp_XNK_Reservation_Release_Detail | Database/WarehouseModule.Procedures.sql:2616 | sp_ |
| 118 | sp_XNK_Reservation_Release_Document | Database/WarehouseModule.Procedures.sql:2689 | sp_ |
| 119 | sp_XNK_Validate_All_Balances | Database/WarehouseModule.Procedures.sql:3579 | sp_ |

### 4.2 Definition counts

| Source scope | Definition occurrences | Unique names | FQ | Fxxxx | sp_ | Other |
| --- | --- | --- | --- | --- | --- | --- |
| `Database/` | 119 | 119 | 1 | 68 | 49 | 1 |
| All repository `.sql` files | 168 | 142 | 1 | 108 | 58 | 1 |

All repository SQL artifacts contain 168 procedure occurrences and 142 unique names; `Database/` contains 119 occurrences and no duplicate procedure name. The remaining duplicates are in historical, rollback, backup, proposal, or report artifacts and must not be treated as the current canonical definition without a deployment decision.

## 5. Detailed findings

### F-001 — Controller method name differs from Store name

**Classification:** `Confirmed Violation` for the 9 direct pairs; `Likely Non-compliance` for the 2 temporary page wrappers.

| # | Method path:line | Current method | Store call (call line) | Recommended method name | Impact evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs:187 | FCommon_Insert_Log_API | `FCommon_sp_ins_Log_API` (193) | FCommon_sp_ins_Log_API | Controller/Common/CCommon_Controller.cs:110 |
| 2 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs:157 | FNLVJLA005_List_Log_Report_File_Excel_Export_By_Function_Code | `FINLVJLA005_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code` (164) | FINLVJLA005_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code | No static caller found in scanned C# |
| 3 | TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs:187 | FI048_List_Log_Report_File_Excel_Export_By_Function_Code | `FI048_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code` (194) | FI048_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code | No static caller found in scanned C# |
| 4 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs:225 | FAD_Common_Get_Sys_API_Source_AD_Top_1 | `FAD_Common_sp_sel_Get_Sys_API_Source_Top_1` (232) | FAD_Common_sp_sel_Get_Sys_API_Source_Top_1 | No static caller found in scanned C# |
| 5 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs:253 | FQ_501_AS_sp_sel_Get_By_By_Ma_API_Source | `FQ_501_AS_sp_sel_Get_By_Ma_API_Source` (260) | FQ_501_AS_sp_sel_Get_By_Ma_API_Source | No static caller found in scanned C# |
| 6 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs:254 | FDev_004_Truncate_Sys_Chuc_Nang_All | `FDev_004_sp_Truncate_Sys_Chuc_Nang_All` (258) | FDev_004_sp_Truncate_Sys_Chuc_Nang_All | No static caller found in scanned C# |
| 7 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs:286 | FAPI_1044_List_Sys_Chuc_Nang | `FAPI_1044_sp_sel_List_Chuc_Nang` (293) | FAPI_1044_sp_sel_List_Chuc_Nang | No static caller found in scanned C# |
| 8 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs:247 | FDev_004_Truncate_Sys_Grid_Field_All | `FDev_004_sp_Truncate_Sys_Grid_Field_All` (251) | FDev_004_sp_Truncate_Sys_Grid_Field_All | No static caller found in scanned C# |
| 9 | TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs:278 | FAPI_1044_List_Sys_Grid_Field | `FAPI_1044_sp_sel_List_Sys_Grid_Field` (285) | FAPI_1044_sp_sel_List_Sys_Grid_Field | No static caller found in scanned C# |
| W1 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CWarehouseMasterSupport_Controller.cs:9 | List_Master_Page_Async | `sp_DM_Master_Page` (11) | sp_DM_Master_Page | File comment says temporary owner; caller/reflection usage still needs confirmation |
| W2 | TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CWarehouseMasterSupport_Controller.cs:14 | List_Lookup_Page_Async | `sp_DM_Lookup_Page` (16) | sp_DM_Lookup_Page | File comment says temporary owner; caller/reflection usage still needs confirmation |

For the direct cases, the least-risk mapping is to rename the C# method to the existing Store name and leave the Store unchanged. This report does not apply that change. Static search found one in-repository caller for `FCommon_Insert_Log_API` at `Controller/Common/CCommon_Controller.cs:110`; external consumers, reflection, configuration, and binary callers were not available.

### F-002 — Master data: `Fxxxx` versus `FQ`

| Controller | Observed group | Unique Store count | Observed names | Assessment |
| --- | --- | --- | --- | --- |
| Controller/DM/CDM_Chu_Hang_Controller.cs | FQ_104_CH | 6 | FQ_104_CH_sp_del_Delete_By_ID, FQ_104_CH_sp_ins_Insert, FQ_104_CH_sp_sel_Get_By_ID, FQ_104_CH_sp_sel_List_By_Created, FQ_104_CH_sp_sel_List_For_Cache, FQ_104_CH_sp_upd_Update | Needs Verification |
| Controller/DM/CDM_Kho_Controller.cs | F2009 | 8 | F2009_sp_del_Kho, F2009_sp_ins_Kho, F2009_sp_sel_Get_By_ID, F2009_sp_sel_List_Kho, F2009_sp_sel_List_Kho_By_Created, F2009_sp_sel_List_Kho_For_Cache, F2009_sp_sel_List_Kho_Lookup, F2009_sp_upd_Kho | Needs Verification |
| Controller/DM/CDM_Don_Vi_Tinh_Controller.cs | F2016 | 8 | F2016_sp_del_Don_Vi_Tinh, F2016_sp_ins_Don_Vi_Tinh, F2016_sp_sel_Get_By_ID, F2016_sp_sel_List_Don_Vi_Tinh, F2016_sp_sel_List_Don_Vi_Tinh_By_Created, F2016_sp_sel_List_Don_Vi_Tinh_For_Cache, F2016_sp_sel_List_Don_Vi_Tinh_Lookup, F2016_sp_upd_Don_Vi_Tinh | Needs Verification |
| Controller/DM/CDM_Loai_San_Pham_Controller.cs | F2017 | 8 | F2017_sp_del_Loai_San_Pham, F2017_sp_ins_Loai_San_Pham, F2017_sp_sel_Get_By_ID, F2017_sp_sel_List_Loai_San_Pham, F2017_sp_sel_List_Loai_San_Pham_By_Created, F2017_sp_sel_List_Loai_San_Pham_For_Cache, F2017_sp_sel_List_Loai_San_Pham_Lookup, F2017_sp_upd_Loai_San_Pham | Needs Verification |
| Controller/DM/CDM_San_Pham_Controller.cs | F2018 | 8 | F2018_sp_del_San_Pham, F2018_sp_ins_San_Pham, F2018_sp_sel_Get_By_ID, F2018_sp_sel_List_San_Pham, F2018_sp_sel_List_San_Pham_By_Created, F2018_sp_sel_List_San_Pham_For_Cache, F2018_sp_sel_List_San_Pham_Lookup, F2018_sp_upd_San_Pham | Needs Verification |
| Controller/DM/CDM_NCC_Controller.cs | F2019 | 8 | F2019_sp_del_NCC, F2019_sp_ins_NCC, F2019_sp_sel_Get_By_ID, F2019_sp_sel_List_NCC, F2019_sp_sel_List_NCC_By_Created, F2019_sp_sel_List_NCC_For_Cache, F2019_sp_sel_List_NCC_Lookup, F2019_sp_upd_NCC | Needs Verification |

No approved table-to-function mapping was found in the audited source that would justify converting `F2009`, `F2016`, `F2017`, `F2018`, or `F2019` to a new `FQ` group. `FQ_104_CH` is already an FQ group but its current DDL was not found in `Database/`. Classification remains `Needs Verification`; do not invent FQ names or rename callers in this phase.

### F-003 — XNK F3001/F3002 and posting workflow

| Controller | Feature group | Unique Stores | SQL definition evidence | Assessment |
| --- | --- | --- | --- | --- |
| Controller/XNK/CXNK_Nhap_Kho_Controller.cs | F3001 | 8 | Database/WarehouseModule.Procedures.sql:4211, Database/WarehouseModule.Procedures.sql:4283, Database/WarehouseModule.Procedures.sql:4360, Database/WarehouseModule.Procedures.sql:4434, Database/WarehouseModule.Procedures.sql:4507, Database/WarehouseModule.Procedures.sql:5012, Database/WarehouseModule.Procedures.sql:5038, Database/WarehouseModule.Procedures.sql:5697 | Compliant |
| Controller/XNK/CXNK_Xuat_Kho_Controller.cs | F3002 | 8 | Database/WarehouseModule.Procedures.sql:5025, Database/WarehouseModule.Procedures.sql:5053, Database/WarehouseModule.Procedures.sql:5275, Database/WarehouseModule.Procedures.sql:5364, Database/WarehouseModule.Procedures.sql:5455, Database/WarehouseModule.Procedures.sql:5579, Database/WarehouseModule.Procedures.sql:5753, Database/WarehouseModule.Procedures.sql:5845 | Compliant |

- `Controller/XNK/CWarehouseDocumentSupport_Controller.cs:21` calls `sp_XNK_Document_Post`; definition: `Database/WarehouseModule.Procedures.sql:4597`. The procedure coordinates permission/fence/reservation/movement invalidation work, so `sp_XNK_` is an explicit technical orchestration exception candidate.
- F3001/F3002 procedures are feature-specific receipt/issue workflows. Keep the `Fxxxx` group unless the company approves a common-table ownership change and an atomic C#/SQL mapping.

### F-004 — F9001/F9002 report naming

| Controller method path:line | Store | Definition | Assessment |
| --- | --- | --- | --- |
| TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs:9 / `F9002_sp_Chi_Tiet_Nhap` | `F9002_sp_Chi_Tiet_Nhap` | Database/WarehouseModule.Procedures.sql:4067 | Likely Non-compliance / approved report exception to confirm |
| TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs:34 / `F9002_sp_Chi_Tiet_Xuat` | `F9002_sp_Chi_Tiet_Xuat` | Database/WarehouseModule.Procedures.sql:4081 | Likely Non-compliance / approved report exception to confirm |
| TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs:59 / `F9002_sp_Chi_Tiet_Nhap_Page` | `F9002_sp_Chi_Tiet_Nhap_Page` | Database/WarehouseModule.Procedures.sql:4095 | Likely Non-compliance / approved report exception to confirm |
| TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs:85 / `F9002_sp_Chi_Tiet_Xuat_Page` | `F9002_sp_Chi_Tiet_Xuat_Page` | Database/WarehouseModule.Procedures.sql:4153 | Likely Non-compliance / approved report exception to confirm |
| TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs:111 / `F9002_sp_Xuat_Nhap_Ton` | `F9002_sp_Xuat_Nhap_Ton` | Database/WarehouseModule.Procedures.sql:6438 | Likely Non-compliance / approved report exception to confirm |
| TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs:136 / `F9002_sp_Xuat_Nhap_Ton_Page` | `F9002_sp_Xuat_Nhap_Ton_Page` | Database/WarehouseModule.Procedures.sql:6795 | Likely Non-compliance / approved report exception to confirm |
| TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs:162 / `F9001_sp_Ton_Kho_Hien_Tai_Page` | `F9001_sp_Ton_Kho_Hien_Tai_Page` | Database/WarehouseModule.Procedures.sql:6647 | Likely Non-compliance / approved report exception to confirm |

Existing repository context in `TK_F9001_F9002_REPORT_STORE_RENAME_REPORT.md` records a prior approved report Store/method rename. Because the current names still omit the handbook operation token (`sp_sel_*`), retain them pending confirmation of that exception instead of renaming the report API speculatively.

### F-005 — Direct database access and generic page helper

- `TKS_Thuc_Tap_V11_Data_Access/Controller/Common/CWarehouse_Controller_Base.cs:54` creates `SqlCommand(p_strProcedure, ...)` and `:57` sets `CommandType.StoredProcedure`; this is the shared path used by the page wrappers.
- `TKS_Thuc_Tap_V11_Data_Access/DataLayer/CSqlHelper.cs:202,217,236,252` defines `*_Cmd` helpers using `CommandType.Text`. No production caller to those methods was found in the targeted Controller/DataLayer scan. Their existence is a review item for external consumers, not proof of an active violation.
- Classification for current Controller call paths: `Compliant`; helper API exposure: `Needs Verification` if external consumers exist.

### F-006 — `sp_executesql`

No `sp_executesql` occurrence was found under `Database/`. The repository-wide SQL occurrences are outside current procedure bodies:

| Artifact | Lines found | Classification |
| --- | --- | --- |
| TK_COMPANY_STYLE_DM_RESULT_CONTRACT_ROLLBACK.sql | 21, 34, 47, 60, 73, 86, 99, 112, 125, 138 | Out of Scope/Exception: deployment or rollback script |
| TK_COMPANY_STYLE_XNK_RESULT_CONTRACT_DEPLOY.sql | 7, 21, 37, 51 | Out of Scope/Exception: deployment or rollback script |

### F-007 — Controller-to-DDL coverage

**Classification:** `Needs Verification`.

| Route category | Unique route Stores | With canonical DDL | Without canonical DDL |
| --- | --- | --- | --- |
| FQ | 247 | 1 | 246 |
| Fxxxx | 91 | 68 | 23 |
| sp_ | 6 | 6 | 0 |
| Other F-prefix | 11 | 1 | 10 |

`Not found in Database/` is a source coverage gap only. It may represent a source file outside this checkout, a generated/deployed Store, a historical contract, or an unresolved route. Resolve against the approved database inventory before any rename.

### F-008 — Historical and duplicate SQL artifacts

**Classification:** `Needs Verification` for provenance; no duplicate exists in the canonical `Database/` folder.

| Duplicate-name count | Examples |
| --- | --- |
| 19 | F2009_sp_sel_List_Kho (2), F2009_sp_sel_List_Kho_Lookup (2), F2016_sp_ins_Don_Vi_Tinh (3), F2016_sp_sel_List_Don_Vi_Tinh (3), F2016_sp_sel_List_Don_Vi_Tinh_Lookup (3), F2016_sp_upd_Don_Vi_Tinh (3), F2017_sp_sel_List_Loai_San_Pham (3), F2017_sp_sel_List_Loai_San_Pham_Lookup (3), F2018_sp_ins_San_Pham (2), F2018_sp_sel_List_San_Pham (2), F2018_sp_sel_List_San_Pham_Lookup (3), F2019_sp_sel_List_NCC (2), F2019_sp_sel_List_NCC_Lookup (2), F3001_sp_sel_List_Nhap_Kho (2), F3001_sp_sel_List_Nhap_Kho_Detail (2), F3002_sp_sel_List_Xuat_Kho (2), F3002_sp_sel_List_Xuat_Kho_Detail (2), sp_Inventory_Fence_Acquire_Root (2), sp_Inventory_Fence_Require_Transaction (2) |

When a Store is remediated, use the canonical `Database/` file and deployment history to decide which historical/rollback copy is authoritative. Do not edit every duplicate artifact as part of this naming audit.

### F-009 — Stale report exception messages

**Classification:** `Out of Scope/Exception` for runtime naming, with a low-risk cleanup candidate.

- `TKS_Thuc_Tap_V11_Data_Access/Controller/BC/CWarehouseReport_Controller.cs:68,94,121,145,171` contains `sp_BC_...` text in exception messages while the called Stores are `F9001/F9002_...`. This does not change the actual Stored Procedure call but can mislead diagnostics. Fix only after the report exception mapping is confirmed.

## 6. Dependency and impact analysis

| Change surface | Evidence | Impact |
| --- | --- | --- |
| Direct method renames | 9 method/Store mismatches listed in F-001 | C# callers, reflection, configuration, public API consumers, tests, and binary callers may break; static scan found one caller for `FCommon_Insert_Log_API` |
| Page wrapper names | 2 support methods differ from Store names | Possible public API or UI caller impact; file comment marks temporary owner |
| Fxxxx → FQ conversion | No approved mapping for five master groups | Requires business ownership decision plus C# and SQL change set; wrong mapping can cross table/permission/cache boundaries |
| XNK F3001/F3002 | Definitions contain workflow validation and inventory side effects | Renaming or regrouping can affect posting, reservation, movement, and invalidation paths |
| SQL source coverage | 279 unique route Store names not in `Database/` | Cannot verify exact DDL contract or safely propose a rename from this checkout |

## 7. Prioritized remediation plan

| Priority | Action | Exit evidence | Scope guard |
| --- | --- | --- | --- |
| P0 | Approve the naming exception register: 9 direct mismatches, 2 page wrappers, report F9001/F9002, technical `sp_XNK_`/`sp_DM_` helpers | Owner decision recorded with exact method/Store pairs | No source or database mutation |
| P1 | Rename only confirmed C# method mismatches to the existing Store names after caller audit | Compiler plus targeted caller/reference scan after approval | Do not rename Stores in this step |
| P1 | Resolve Fxxxx/FQ ownership for master groups | Approved mapping table with table ownership and Store names | Do not invent `FQ` names |
| P2 | Reconcile the 279 route names without canonical DDL against the approved database inventory | Each route has one authoritative DDL path and version | Live database access is a separate approval |
| P2 | Confirm report naming exception and update stale diagnostics if approved | Report contract and error text agree | Keep current report names until approval |
| P3 | Review external callers of `*_Cmd` text helpers | Caller list and security decision | No helper deletion in this audit |

## 8. Proposed mapping for the next approved implementation

### 8.1 Confirmed direct method mappings

| Current C# method path:line | Current Store | Proposed C# method | SQL action | Status |
| --- | --- | --- | --- | --- |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_API_Controller.cs:187 | FCommon_sp_ins_Log_API | FCommon_sp_ins_Log_API | Keep existing Store name | Prepare after caller approval |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs:157 | FINLVJLA005_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code | FINLVJLA005_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code | Keep existing Store name | Prepare after caller approval |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Log/CLog_Report_File_Excel_Controller.cs:187 | FI048_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code | FI048_sp_sel_List_Log_Report_File_Excel_Export_By_Function_Code | Keep existing Store name | Prepare after caller approval |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs:225 | FAD_Common_sp_sel_Get_Sys_API_Source_Top_1 | FAD_Common_sp_sel_Get_Sys_API_Source_Top_1 | Keep existing Store name | Prepare after caller approval |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_API_Source_Controller.cs:253 | FQ_501_AS_sp_sel_Get_By_Ma_API_Source | FQ_501_AS_sp_sel_Get_By_Ma_API_Source | Keep existing Store name | Prepare after caller approval |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs:254 | FDev_004_sp_Truncate_Sys_Chuc_Nang_All | FDev_004_sp_Truncate_Sys_Chuc_Nang_All | Keep existing Store name | Prepare after caller approval |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Chuc_Nang_Controller.cs:286 | FAPI_1044_sp_sel_List_Chuc_Nang | FAPI_1044_sp_sel_List_Chuc_Nang | Keep existing Store name | Prepare after caller approval |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs:247 | FDev_004_sp_Truncate_Sys_Grid_Field_All | FDev_004_sp_Truncate_Sys_Grid_Field_All | Keep existing Store name | Prepare after caller approval |
| TKS_Thuc_Tap_V11_Data_Access/Controller/Sys/CSys_Grid_Field_Controller.cs:278 | FAPI_1044_sp_sel_List_Sys_Grid_Field | FAPI_1044_sp_sel_List_Sys_Grid_Field | Keep existing Store name | Prepare after caller approval |

### 8.2 Wrapper and prefix decisions requiring owner input

| Current method path:line | Current Store/group | Proposed mapping | Decision needed |
| --- | --- | --- | --- |
| TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CWarehouseMasterSupport_Controller.cs:9 | sp_DM_Master_Page | sp_DM_Master_Page | Confirm whether wrapper method must equal Store or is an approved façade |
| TKS_Thuc_Tap_V11_Data_Access/Controller/DM/CWarehouseMasterSupport_Controller.cs:14 | sp_DM_Lookup_Page | sp_DM_Lookup_Page | Confirm whether wrapper method must equal Store or is an approved façade |
| Controller/DM/CDM_Kho_Controller.cs and related master controllers | F2009/F2016/F2017/F2018/F2019 | No speculative FQ mapping | Confirm table ownership and common-table rule |
| Controller/BC/CWarehouseReport_Controller.cs:9-168 | F9001/F9002 report Stores | Preserve current names unless exception is revoked | Confirm report contract exception |
| Controller/XNK/CWarehouseDocumentSupport_Controller.cs:21 | sp_XNK_Document_Post | Preserve technical name | Confirm technical orchestration exception |

## 9. Future implementation and rollback plan

1. Freeze the approved exception/mapping list and capture the current commit as the change baseline.
2. Resolve static, reflection, configuration, and external callers for each approved method rename.
3. Apply the smallest C# rename set first; keep existing SQL names when the SQL name already satisfies the approved contract.
4. For an approved Fxxxx → FQ conversion, change the C# and SQL contract together, preserve permissions/transactions/cache behavior, and verify every caller before deployment.
5. Run the affected compile/tests only after implementation authorization; those checks were intentionally not run for this audit.
6. Roll back by reverting the single change set or restoring the pre-change commit. Do not use live database rollback scripts without a separate approval and preflight.

## 10. Unresolved questions

- Which source or database inventory is authoritative for the 279 route Store names absent from `Database/`?
- Are the 9 direct method mismatches public API contracts, reflection targets, or internal-only methods?
- Should the two `CWarehouseMasterSupport_Controller` façade names remain descriptive, or must they equal `sp_DM_Master_Page` and `sp_DM_Lookup_Page`?
- Which master tables are genuinely common (`FQ`) and which are feature-owned (`Fxxxx`)?
- Is the current F9001/F9002 report naming exception still approved?
- Are `CSqlHelper.FillDataTable_Cmd` and `ExecuteNonquery_Cmd` called by projects outside this repository?

## 11. Audit limitations

- This is a static source audit at one Git commit. No live SQL Server metadata, deployed assembly, runtime route, reflection registry, or external consumer was inspected.
- A Store absent from `Database/` is classified `Needs Verification`, not declared missing from the live database.
- Build, test, deployment, and database mutation were intentionally not performed.
- CodeGraph was used for targeted navigation of the Controller/data-access relationship; source files remain the authority for the line-level evidence in this report.

## 12. Reproducible read-only evidence commands

```powershell
git rev-parse --show-toplevel
git rev-parse HEAD
rg --files TKS_Thuc_Tap_V11_Data_Access/Controller -g "*.cs"
rg -n --glob "*.cs" "CSqlHelper\.|Fill_Page_Data|Page_From_Procedure|CommandType\.StoredProcedure" TKS_Thuc_Tap_V11_Data_Access/Controller
rg -n --glob "*.sql" "CREATE( OR ALTER)? PROCEDURE|sp_executesql" Database
```

— End of read-only audit —
