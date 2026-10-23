# Company original Excel import — full source migration

Ngày xác minh: 2026-10-23 (Asia/Saigon).

Trạng thái: **IMPLEMENTED; SOURCE + BUILD + FOCUSED TESTS VERIFIED. XNK SQL integration, browser UAT và log/history qua trình duyệt: NOT VERIFIED.**

## Baseline và phạm vi bảo toàn

- Source A chỉ đọc: `P:\Giao đoạn 2 TK_root\Giao đoạn 2 TK\TKS_Thuc_Tap_11`.
- Source B: `P:\Giao đoạn 2 TK\Giao đoạn 2 TK\TKS_Thuc_Tap_11`.
- Branch `main`; HEAD `98389f3509a6d120f16f57e27d9c9ef2573be9d3`. Baseline có 27 tracked modified và 7 untracked. Đã lưu status/diff/hash trước khi sửa. Không có 12 benchmark log deletions trong baseline này.
- Đọc hai báo cáo phase trước; chúng là bằng chứng lịch sử và được thay thế về quyết định Preview/Confirm/SKIP bởi yêu cầu phase này.
- RTK không được cài trên PATH; dùng Git/rg với phạm vi và output giới hạn.
- CodeGraph được dùng trước khi sửa để xác định shared List Nhập/Xuất, caller wrappers 3001/3002 và service cũ; source thực tế là cơ sở quyết định.
- 1.135 file Source A trong manifest source/config/document đã được so SHA-256 trước/sau: không đổi. Manifest loại bin/obj/.git/.vs/node_modules.
- Không chỉnh FBase, FCommon_Import_Excel, Chủ Hàng, Stored Procedures hoặc schema. Không deploy SQL, stage, commit hoặc push.
- Các diff có sẵn sau đây giữ nguyên từng file so với baseline:
  - `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseUiWorkflowTests.cs`.
  - `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F3001_3_Nhap_Kho_Edit.razor`.
  - `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F3002_3_Xuat_Kho_Edit.razor`.
  - `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_1_Warehouse_List.razor`.
  - `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_3_Warehouse_Edit.razor`.

Protected Source B hashes (không đổi trong phase):

| File | SHA-256 |
|---|---|
| `TKS_Thuc_Tap_V11_Web_Common/Common/FBase.razor` | `083062b0e26a4fb0a7270b9b11d23c6cae42d69a601f519124aa7302ace6f361` |
| `TKS_Thuc_Tap_V11_Web_Common/Common/FCommon_Import_Excel.razor` | `a24613dc0848acfff208a1b010b7be3eab207ffe09e441a5c5c8779957996bac` |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2003_1_Chu_Hang_List.razor` | `72f33218c4e3dcce827393c789c4bc0e98e27d63c9e38de1b5f4657d9aaf1d36` |
| `Database/WarehouseModule.Procedures.sql` | `e5acf37a6fc858dc8d75c4f862d5f098aea0e31a29b20d0e619efb0070f65e32` |

## A. Flow Before / After

| Module | Old Entry Point | New Entry Point | FBase Used | Preview Removed | Skipped Removed |
|---|---|---|---|---|---|
| Kho (2009) | Shared modal → profile adapter + validator + positional adapter | FCommon_Import_Excel → FBase.Import_Excel → Import_Excel_Entry → CExcel_Controller A2:B → F2009_sp_ins_Kho | Yes | Không có Preview runtime; đã xóa component/service legacy | Yes |
| Đơn vị tính (2016) | Shared modal → profile adapter + validator + positional adapter | FCommon_Import_Excel → FBase.Import_Excel → Import_Excel_Entry → CExcel_Controller A2:B → F2016_sp_ins_Don_Vi_Tinh | Yes | Không có Preview runtime; đã xóa component/service legacy | Yes |
| Loại sản phẩm (2017) | Shared modal → profile adapter + validator + positional adapter | FCommon_Import_Excel → FBase.Import_Excel → Import_Excel_Entry → CExcel_Controller A2:C → F2017_sp_ins_Loai_San_Pham | Yes | Không có Preview runtime; đã xóa component/service legacy | Yes |
| Sản phẩm (2018) | Shared modal → profile adapter + validator + positional adapter | FCommon_Import_Excel → FBase.Import_Excel → Import_Excel_Entry → CExcel_Controller A2:E → F2018_sp_ins_San_Pham | Yes | Không có Preview runtime; đã xóa component/service legacy | Yes |
| Nhà cung cấp (2019) | Shared modal → profile adapter + validator + positional adapter | FCommon_Import_Excel → FBase.Import_Excel → Import_Excel_Entry → CExcel_Controller A2:C → F2019_sp_ins_NCC | Yes | Không có Preview runtime; đã xóa component/service legacy | Yes |
| Nhập kho (3001) | FWarehouse_Transaction_Import → PreviewAsync → ConfirmAsync → independent service + custom log | FCommon_Import_Excel → FBase.Import_Excel → FWarehouse_Transaction_List.Import_Excel_Entry → Header/Detail controller | Yes | Yes | Yes |
| Xuất kho (3002) | FWarehouse_Transaction_Import → PreviewAsync → ConfirmAsync → independent service + custom log | FCommon_Import_Excel → FBase.Import_Excel → FWarehouse_Transaction_List.Import_Excel_Entry → Header/Detail controller | Yes | Yes | Yes |

Mọi dòng Master không rỗng đều tới transaction Insert, không tải danh sách hiện hữu để pre-check trùng. Product chỉ tải category/unit để resolve FK. SQL từ chối duplicate → rollback → Error++; vòng lặp tiếp tục. Dòng có khóa rỗng nhưng ghi chú/business data vẫn được đưa vào xử lý, không bị bỏ qua theo cột khóa.

Mọi phiếu Nhập/Xuất dùng một transaction bao trùm Header và toàn bộ Detail. Số phiếu trùng DB không được pre-check; Insert Header sẽ trả lỗi SQL. Header trùng trong chính file hoặc Detail không xác định được Header làm từ chối cấu trúc file trước khi ghi để tránh ghép sai. Phiếu thiếu Detail/parse lỗi/lookup lỗi/SQL lỗi được tính một Error và tiếp tục phiếu kế tiếp.

### Evidence của entry points

- Kho (2009): `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2009_1_Kho_List.razor:99`; mapping `B` cột; cache/history sau commit.
- Đơn vị tính (2016): `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_List.razor:99`; mapping `B` cột; cache/history sau commit.
- Loại sản phẩm (2017): `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2017_1_Loai_San_Pham_List.razor:100`; mapping `C` cột; cache/history sau commit.
- Sản phẩm (2018): `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2018_1_San_Pham_List.razor:134`; mapping `E` cột; cache/history sau commit.
- Nhà cung cấp (2019): `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/F2019_1_Nha_Cung_Cap_List.razor:100`; mapping `C` cột; cache/history sau commit.
- Wiring Nhập/Xuất: `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor:220`; render shared modal `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor:121`.
- Callback và transaction chứng từ: `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor.cs:17`.
- FBase refresh: `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor:159`; gọi Rebind sau khi Grid đã được render/format. Giữ nguyên Load_Data_Init của FBase.

## B. Source A parity

| Feature | Source A | Source B after | Difference | Reason |
|---|---|---|---|---|
| Shared lifecycle | FBase Open → FCommon → Close → Import → Entry → log/queue | Dùng trực tiếp cho cả 7 module | Không có dispatcher thứ hai | Code công ty |
| Mapping Master | DataTable + foreach DataRow + CUtility.Convert_To_String | Cùng API, mapping vị trí theo template chính thức | Bỏ hàng chỉ khi toàn bộ business fields rỗng, giữ số hàng vật lý | Tránh mất row khóa rỗng và sai số dòng |
| Duplicate | Controller/SP Insert quyết định | Cùng nguyên tắc, không profile/validator pre-check/SKIP | None | Business rules ở SQL |
| Count | Reference tăng Success trước Commit | Success++ sau Commit | Sửa thứ tự kỹ thuật tại target, không sửa Chủ Hàng | Commit lỗi không được báo thành công |
| Cache/history | Sau commit nhưng trong catch transaction chung của reference | Sau commit, catch cảnh báo riêng | Không rollback/count Error nếu hậu xử lý lỗi | Dữ liệu đã lưu không được báo thất bại |
| Workbook | Một worksheet; A:I | Master A:B/C/E; XNK hai worksheet | XNK grouping cố định | Nghiệp vụ Header–Detail |
| File/header safety | Source A không có Validate_Header_Matches của Source B | Giữ nguyên check đã có trong FBase/CExcel | Không sửa shared implementation trong phase | Không tháo bỏ bảo vệ cấu trúc hiện có |
| Template/capabilities | Company CExcel.Value, template cố định | Cùng value API; không dynamic profiles/customer header aliases | Không còn diagnostics/alias maps/preview | Yêu cầu chuyển về template công ty |
| Log states | Thanh_Cong / Thanh_Cong_1_Phan / That_Bai | FBase tự chọn từ Success/Error | Không Skipped enum/counter/log phụ | Contract gốc |

### Source A đã đối chiếu

- `F2003_1_Chu_Hang_List.razor:119` Import_Excel_Entry; `:142` mở connection; `:149` positional mapping; `:161` Insert, `:162` count, `:163` Commit, `:165` cache, `:167` history (reference có Success trước Commit).
- `FBase.razor:309` Import_Excel; `:331` gọi Entry; `:348` tạo Import Log; `:372` End_Queue; `:375` Load_Data_Init; `:1643` Open; `:1658` Close callback.
- `FCommon_Import_Excel.razor:56` chọn file; `:69` thực hiện import/upload; `:85` folder ngày theo CConfig.Import_Excel_URL; callback OnClose truyền Path_File.
- `CExcel_Controller.cs:97` đọc sheet theo tên; `:129` đọc theo index; giữ mọi hàng vật lý và đọc cell.Value. Không dùng lại Read_Profiled_Import_Sheet.
- `CDM_Chu_Hang_Controller.cs:126` Insert nhận connection/transaction, qua CSqlHelper; `CCache_Chu_Hang.cs:34` Add_Data; `CCommon_Controller.cs:24` ghi Action History theo Ref_ID.
- `CLog_Import_Excel_Controller.cs:76` Insert log; `Entity/Log/CLog_Import_Excel.cs:212` nhãn ba trạng thái. Không thêm schema/enum trạng thái import.

## C. Deleted custom code

Đã xóa **41 file production + 25 file test lỗi thời**. Trước từng lần xóa đã xác định declarations/callers, đối chiếu manifest Source A và kiểm tra caller ngoài import. Bảng dưới ghi callers tại bước cleanup (sau khi chuyển active entry points). Tên file caller là tên chính xác; không có legacy compatibility wrappers.

| File/Class | Deleted | Replaced By | Previous Callers at cleanup | New Callers |
|---|---|---|---|---|
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CCategoryImportValidator.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `CategoryExcelImportValidationTests.cs`, `ImportStructureDetectionTests.cs`, `MasterDataCompanyStyleMigrationTests.cs`, `CWarehouseCategoryImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CProductImportValidator.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `ImportStructureDetectionTests.cs`, `MasterDataCompanyStyleMigrationTests.cs`, `ProductExcelImportUiTests.cs`, `ProductExcelImportValidationTests.cs`, `CWarehouseProductImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CSupplierImportValidator.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `ImportStructureDetectionTests.cs`, `MasterDataCompanyStyleMigrationTests.cs`, `SupplierExcelImportValidationTests.cs`, `CWarehouseSupplierImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CUnitImportValidator.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `ImportStructureDetectionTests.cs`, `UnitExcelImportUiTests.cs`, `UnitExcelImportValidationTests.cs`, `CWarehouseUnitImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseCategoryImportService.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `CategoryExcelImportAuthorizationTests.cs`, `CategoryImportRuntimeIntegrationTests.cs`, `WarehouseDmCacheSyncTests.cs`, `WarehouseMasterEntityDecompositionContractTests.cs`, `WarehouseMasterFinalizationContractTests.cs`, `FWarehouse_Category_Import.razor` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseImportValidator.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `ImportStructureDetectionTests.cs`, `MasterDataCompanyStyleMigrationTests.cs`, `WarehouseExcelImportValidationTests.cs`, `CWarehouseKhoImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseKhoImportService.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `WarehouseDmCacheSyncTests.cs`, `WarehouseExcelImportAuthorizationTests.cs`, `WarehouseImportRuntimeIntegrationTests.cs`, `WarehouseMasterEntityDecompositionContractTests.cs`, `WarehouseMasterFinalizationContractTests.cs`, `FWarehouse_Kho_Import.razor` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseProductImportService.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `ProductExcelImportAuthorizationTests.cs`, `ProductExcelImportUiTests.cs`, `ProductImportRuntimeIntegrationTests.cs`, `WarehouseDmCacheSyncTests.cs`, `WarehouseMasterEntityDecompositionContractTests.cs`, `WarehouseMasterFinalizationContractTests.cs`, `FWarehouse_Product_Import.razor` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseSupplierImportService.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `SupplierExcelImportAuthorizationTests.cs`, `SupplierImportRuntimeIntegrationTests.cs`, `WarehouseDmCacheSyncTests.cs`, `WarehouseMasterEntityDecompositionContractTests.cs`, `WarehouseMasterFinalizationContractTests.cs`, `FWarehouse_Supplier_Import.razor` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseTransactionImportService.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `TransactionImportPreviewTests.cs`, `WarehousePermissionRefactorTests.cs`, `WarehouseReportRefactorTests.cs`, `FWarehouse_Transaction_Import.razor` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Services/CWarehouseUnitImportService.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `UnitExcelImportAuthorizationTests.cs`, `UnitImportRuntimeIntegrationTests.cs`, `WarehouseDmCacheSyncTests.cs`, `WarehouseMasterEntityDecompositionContractTests.cs`, `WarehouseMasterFinalizationContractTests.cs`, `FWarehouse_Unit_Import.razor` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FImportFieldPolicyDiagnostics.razor` | Yes; Source A=không | FCommon_Import_Excel + FBase | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Category_Import.razor` | Yes; Source A=không | FCommon_Import_Excel + FBase | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Import_Preview_Shell.razor` | Yes; Source A=không | FCommon_Import_Excel + FBase | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Kho_Import.razor` | Yes; Source A=không | FCommon_Import_Excel + FBase | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Product_Import.razor` | Yes; Source A=không | FCommon_Import_Excel + FBase | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Supplier_Import.razor` | Yes; Source A=không | FCommon_Import_Excel + FBase | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_Import.razor` | Yes; Source A=không | FCommon_Import_Excel + FBase | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Unit_Import.razor` | Yes; Source A=không | FCommon_Import_Excel + FBase | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CCategory_Xlsx_Reader.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | `CImportLegacyWorksheetAdapter.cs`, `CategoryExcelImportUiTests.cs`, `CategoryExcelImportValidationTests.cs`, `CategoryXlsxReaderTests.cs`, `ImportStructureDetectionTests.cs`, `MasterDataCompanyStyleMigrationTests.cs`, `CCategoryImportValidator.cs`, `CWarehouseCategoryImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CProduct_Xlsx_Reader.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | `CImportLegacyWorksheetAdapter.cs`, `ImportStructureDetectionTests.cs`, `MasterDataCompanyStyleMigrationTests.cs`, `ProductExcelImportUiTests.cs`, `ProductExcelImportValidationTests.cs`, `ProductImportRuntimeIntegrationTests.cs`, `XlsxParserCompatibilityTests.cs`, `CProductImportValidator.cs`, `CWarehouseProductImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CSupplier_Xlsx_Reader.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | `CImportLegacyWorksheetAdapter.cs`, `ImportStructureDetectionTests.cs`, `MasterDataCompanyStyleMigrationTests.cs`, `SupplierExcelImportValidationTests.cs`, `SupplierXlsxReaderTests.cs`, `CSupplierImportValidator.cs`, `CWarehouseSupplierImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CUnit_Xlsx_Reader.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | `CImportLegacyWorksheetAdapter.cs`, `ImportStructureDetectionTests.cs`, `UnitExcelImportUiTests.cs`, `UnitExcelImportValidationTests.cs`, `UnitXlsxReaderTests.cs`, `CUnitImportValidator.cs`, `CWarehouseUnitImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CWarehouse_Xlsx_Reader.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | `CImportLegacyWorksheetAdapter.cs`, `ImportStructureDetectionTests.cs`, `MasterDataCompanyStyleMigrationTests.cs`, `WarehouseExcelImportValidationTests.cs`, `WarehouseXlsxReaderTests.cs`, `CWarehouseImportValidator.cs`, `CWarehouseKhoImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CMaster_Xlsx_Reader.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CWorkbook_Xlsx_Reader.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | `CProfiledWorkbook_Xlsx_Reader.cs`, `CTransactionImportPreviewService.cs`, `CWorkbookStructureDetector.cs`, `ImportStructureDetectionTests.cs`, `TransactionImportPreviewTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CWorkbookStructureDetector.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | `CProfiledWorkbook_Xlsx_Reader.cs`, `CTransactionImportPreviewService.cs`, `ImportStructureDetectionTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CProfiledWorkbook_Xlsx_Reader.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | `CCategory_Xlsx_Reader.cs`, `CProduct_Xlsx_Reader.cs`, `CSupplier_Xlsx_Reader.cs`, `CTransactionImportPreviewService.cs`, `CUnit_Xlsx_Reader.cs`, `CWarehouse_Xlsx_Reader.cs`, `ImportStructureDetectionTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CTransactionImportPreviewService.cs` | Yes; Source A=không | Import_Excel_Entry + Controller/SP | `ImportStructureDetectionTests.cs`, `TransactionImportPreviewTests.cs`, `WarehouseTransactionImportUiTests.cs`, `CWarehouseTransactionImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CImportProfileProvider.cs` | Yes; Source A=không | CExcel_Controller gốc + fixed template mapping | `CImportValueMapper.cs`, `CProfiledWorkbook_Xlsx_Reader.cs`, `CTransactionImportPreviewService.cs`, `CWorkbookStructureDetector.cs`, `ImportStructureDetectionTests.cs`, `ImportValueMappingTests.cs`, `TransactionImportPreviewTests.cs`, `CProductImportValidator.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CImportProfileModels.cs` | Yes; Source A=không | DataRow/entity; Normalize được giữ riêng nếu có CRUD caller | `CCategoryImportModels.cs`, `CProductImportModels.cs`, `CSupplierImportModels.cs`, `CTransactionImportPreviewModels.cs`, `CUnitImportModels.cs`, `CWarehouseImportModels.cs`, `CCategory_Xlsx_Reader.cs`, `CImportBusinessValueComparer.cs`, `CImportLegacyWorksheetAdapter.cs`, `CImportProfileProvider.cs`, `CImportValueMapper.cs`, `CProduct_Xlsx_Reader.cs`, `CProfiledWorkbook_Xlsx_Reader.cs`, `CSupplier_Xlsx_Reader.cs`, `CTransactionImportPreviewService.cs`, `CUnit_Xlsx_Reader.cs`, `CWarehouse_Xlsx_Reader.cs`, `CWorkbookStructureDetector.cs`, `ImportStructureDetectionTests.cs`, `ImportValueMappingTests.cs`, `F2018_3_San_Pham_Edit.razor`, `FImportFieldPolicyDiagnostics.razor`, `CCategoryImportValidator.cs`, `CProductImportValidator.cs`, `CSupplierImportValidator.cs`, `CUnitImportValidator.cs`, `CWarehouseImportValidator.cs`, `CWarehouseTransactionImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CImportValueMapper.cs` | Yes; Source A=không | FCommon_Import_Excel + FBase | `CTransactionImportPreviewService.cs`, `CWorkbookStructureDetector.cs`, `ImportValueMappingTests.cs`, `CProductImportValidator.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CImportLegacyWorksheetAdapter.cs` | Yes; Source A=không | FCommon_Import_Excel + FBase | `CCategory_Xlsx_Reader.cs`, `CProduct_Xlsx_Reader.cs`, `CSupplier_Xlsx_Reader.cs`, `CUnit_Xlsx_Reader.cs`, `CWarehouse_Xlsx_Reader.cs`, `ImportStructureDetectionTests.cs`, `MasterDataCompanyStyleMigrationTests.cs`, `UnitExcelImportUiTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/CExcel_Import_DataTable_Adapter.cs` | Yes; Source A=không | FCommon_Import_Excel + FBase | `MasterDataCompanyStyleMigrationTests.cs`, `MasterDataPositionalImportMappingTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CCategoryImportModels.cs` | Yes; Source A=không | DataRow/entity; Normalize được giữ riêng nếu có CRUD caller | `CategoryExcelImportAuthorizationTests.cs`, `CategoryExcelImportValidationTests.cs`, `CategoryImportRuntimeIntegrationTests.cs`, `FWarehouse_Category_Import.razor`, `CCategoryImportValidator.cs`, `CWarehouseCategoryImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CProductImportModels.cs` | Yes; Source A=không | DataRow/entity; Normalize được giữ riêng nếu có CRUD caller | `ProductExcelImportAuthorizationTests.cs`, `ProductExcelImportUiTests.cs`, `ProductExcelImportValidationTests.cs`, `ProductImportRuntimeIntegrationTests.cs`, `FWarehouse_Product_Import.razor`, `CProductImportValidator.cs`, `CWarehouseProductImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CSupplierImportModels.cs` | Yes; Source A=không | DataRow/entity; Normalize được giữ riêng nếu có CRUD caller | `SupplierExcelImportAuthorizationTests.cs`, `SupplierExcelImportValidationTests.cs`, `SupplierImportRuntimeIntegrationTests.cs`, `FWarehouse_Supplier_Import.razor`, `CSupplierImportValidator.cs`, `CWarehouseSupplierImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CUnitImportModels.cs` | Yes; Source A=không | DataRow/entity; Normalize được giữ riêng nếu có CRUD caller | `UnitExcelImportAuthorizationTests.cs`, `UnitExcelImportValidationTests.cs`, `UnitImportRuntimeIntegrationTests.cs`, `FWarehouse_Unit_Import.razor`, `CUnitImportValidator.cs`, `CWarehouseUnitImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CWarehouseImportModels.cs` | Yes; Source A=không | DataRow/entity; Normalize được giữ riêng nếu có CRUD caller | `WarehouseExcelImportAuthorizationTests.cs`, `WarehouseExcelImportValidationTests.cs`, `WarehouseImportRuntimeIntegrationTests.cs`, `FWarehouse_Kho_Import.razor`, `CWarehouseImportValidator.cs`, `CWarehouseKhoImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Entity/Warehouse/CTransactionImportPreviewModels.cs` | Yes; Source A=không | DataRow/entity; Normalize được giữ riêng nếu có CRUD caller | `CTransactionImportPreviewService.cs`, `TransactionImportPreviewTests.cs`, `WarehouseTransactionImportUiTests.cs`, `FWarehouse_Transaction_Import.razor`, `CWarehouseTransactionImportService.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access/Utility/Profiles/excel-import-profiles.json` | Yes; Source A=không | FCommon_Import_Excel + FBase | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/CategoryExcelImportAuthorizationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `ProductExcelImportAuthorizationTests.cs`, `SupplierExcelImportAuthorizationTests.cs`, `UnitExcelImportAuthorizationTests.cs`, `WarehouseExcelImportAuthorizationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/CategoryExcelImportValidationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/CategoryImportRuntimeIntegrationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `ProductImportRuntimeIntegrationTests.cs`, `SupplierImportRuntimeIntegrationTests.cs`, `UnitImportRuntimeIntegrationTests.cs`, `WarehouseImportRuntimeIntegrationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/CategoryXlsxReaderTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/ImportStructureDetectionTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/ImportValueMappingTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/MasterDataPositionalImportMappingTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/MasterImportFieldPolicyDiagnosticsUiTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductExcelImportAuthorizationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `CategoryExcelImportAuthorizationTests.cs`, `SupplierExcelImportAuthorizationTests.cs`, `UnitExcelImportAuthorizationTests.cs`, `WarehouseExcelImportAuthorizationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductExcelImportValidationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/ProductImportRuntimeIntegrationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `CategoryImportRuntimeIntegrationTests.cs`, `SupplierImportRuntimeIntegrationTests.cs`, `UnitImportRuntimeIntegrationTests.cs`, `WarehouseImportRuntimeIntegrationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/SupplierExcelImportAuthorizationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `CategoryExcelImportAuthorizationTests.cs`, `ProductExcelImportAuthorizationTests.cs`, `UnitExcelImportAuthorizationTests.cs`, `WarehouseExcelImportAuthorizationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/SupplierExcelImportValidationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/SupplierImportRuntimeIntegrationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `CategoryImportRuntimeIntegrationTests.cs`, `ProductImportRuntimeIntegrationTests.cs`, `UnitImportRuntimeIntegrationTests.cs`, `WarehouseImportRuntimeIntegrationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/SupplierXlsxReaderTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/TransactionImportPreviewTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/UnitExcelImportAuthorizationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `CategoryExcelImportAuthorizationTests.cs`, `ProductExcelImportAuthorizationTests.cs`, `SupplierExcelImportAuthorizationTests.cs`, `WarehouseExcelImportAuthorizationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/UnitExcelImportValidationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/UnitImportRuntimeIntegrationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `CategoryImportRuntimeIntegrationTests.cs`, `ProductImportRuntimeIntegrationTests.cs`, `SupplierImportRuntimeIntegrationTests.cs`, `WarehouseImportRuntimeIntegrationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/UnitXlsxReaderTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseExcelImportAuthorizationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `CategoryExcelImportAuthorizationTests.cs`, `ProductExcelImportAuthorizationTests.cs`, `SupplierExcelImportAuthorizationTests.cs`, `UnitExcelImportAuthorizationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseExcelImportValidationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseImportRuntimeIntegrationTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | `CategoryImportRuntimeIntegrationTests.cs`, `ProductImportRuntimeIntegrationTests.cs`, `SupplierImportRuntimeIntegrationTests.cs`, `UnitImportRuntimeIntegrationTests.cs` | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseTransactionImportUiTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |
| `TKS_Thuc_Tap_V11_Data_Access.Tests/WarehouseXlsxReaderTests.cs` | Yes; Source A=không | CompanyOriginalExcelImportTests / guarded SQL tests / retained regressions | Không còn caller | Active List/Entry; không gọi class đã xóa |

Phần shared cần giữ: `CImportBusinessValueComparer.Normalize` được tách nguyên hành vi từ CImportProfileModels sang `Utility/CImportBusinessValueComparer.cs` vì `F2018_3_San_Pham_Edit.razor:129` còn sử dụng khi CRUD. Không sửa form. `CWarehouseProductSupport_Service` vẫn phục vụ nghiệp vụ product ở ngoài import nên được giữ.

Trong `CExcel_Controller.cs` chỉ xóa field `m_lFile_Length`, phép gán tương ứng và method `Read_Profiled_Import_Sheet` do Source B thêm. Giữ mọi API công ty và Validate_Header_Matches đã có từ phase trước. Xóa embedded resource profile khỏi Data Access csproj; giữ nguyên package dùng cho Export.

Các test hỗn hợp giữ phần CRUD, cache, export, routing và generic parser. Chỉ bỏ các test/method yêu cầu Preview/SKIP/readers cũ, đổi architecture assertions sang owner List/Entry đang hoạt động. Không giữ class cũ để làm các test cũ tiếp tục pass.

## D. Remaining XNK-specific code

| Method / file | Purpose | Why retained |
|---|---|---|
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor.cs:27` | DataTable Header A2:E/D, Detail A2:E; ghép bằng số phiếu; Row gốc | Một phiếu có nhiều dòng và cần phát hiện orphan/duplicate header trước ghi |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor.cs:177` | Kiểm tra cả Header/Detail và toàn bộ header qua API CExcel gốc | FBase chỉ kiểm tra worksheet đầu tiên |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor.cs:53` | Parse dd/MM/yyyy, ISO, date string do CExcel convert theo culture, Excel serial | Không mặc định ngày khi dữ liệu sai |
| `TKS_Thuc_Tap_V11_Web_Danh_Muc/Pages/Danh_Muc/Components/FWarehouse_Transaction_List.razor.cs:91` | Parse numeric, ngăn rounding/truncation decimal(18,3)/(18,2) | Kiểu SQL không được âm thầm làm sai dữ liệu |

FK: kho resolve duy nhất từ sp_DM_Kho_User_List_Allowed; NCC theo mã; sản phẩm theo mã và tên tùy chọn phải khớp; ĐVT lấy từ master sản phẩm và phải tồn tại. Không đặt ID/ngày/giá mặc định. Product dùng **full List_San_Pham**, vì Lookup SP chỉ SELECT Auto_ID/Ma/Ten và không có Don_Vi_Tinh_ID (`Database/WarehouseModule.Procedures.sql:3850`).

Transaction: tái sử dụng chính overload của controller nhận v_conn/v_trans, các adapter To_Nhap_Kho/To_Xuat_Kho/To_*_Detail của List. Không sửa controller, không trực tiếp INSERT SQL, không tự Post. Kho/current/reservation/ledger/report vẫn do SP hiện có điều khiển. Post-commit history dùng Header.Auto_ID thực, không Ref_ID=0.

CExcel Get_Cell_Value(int) và List_Range_Value(int) gốc tìm sheet theo tên SheetN; không dùng các overload này cho template có tên tiếng Việt. Validate_Import_Sheet dùng Get_Cell_Value(sheetName,address); data dùng List_Range_Value_To_End(sheetName,...). Không sửa các helper gốc để xử lý đặc thù module.

## E. Stored Procedure verification

Chỉ đọc sys.procedures/sys.sql_modules trên `DESKTOP-NHQ7QPL\MSSQLSERVER19 / TKS_Thuc_Tap_V11_GiaiDoan2`. DB_NAME được kiểm tra trước. Cả chín Insert SP có definition khớp Database/WarehouseModule.Procedures.sql sau normalization whitespace/CREATE OR ALTER; không thực thi business Insert trên DB nghiệp vụ.

| Module | Insert SP | Validation | Runtime checked | DB modified |
|---|---|---|---|---|
| Kho (2009) | `F2009_sp_ins_Kho` | Required/name unique; độ dài tham số giữ ở C# để ngăn cắt chuỗi trước SP | Yes; definition match | No |
| Đơn vị tính (2016) | `F2016_sp_ins_Don_Vi_Tinh` | Required/name unique; độ dài tham số giữ ở C# để ngăn cắt chuỗi trước SP | Yes; definition match | No |
| Loại sản phẩm (2017) | `F2017_sp_ins_Loai_San_Pham` | Required/code + name unique; độ dài tham số giữ ở C# để ngăn cắt chuỗi trước SP | Yes; definition match | No |
| Sản phẩm (2018) | `F2018_sp_ins_San_Pham` | Required/code unique/category + unit exist; độ dài tham số giữ ở C# để ngăn cắt chuỗi trước SP | Yes; definition match | No |
| Nhà cung cấp (2019) | `F2019_sp_ins_NCC` | Required/code + name unique; độ dài tham số giữ ở C# để ngăn cắt chuỗi trước SP | Yes; definition match | No |
| Nhập (3001) | F3001_sp_ins_Nhap_Kho_Header / Detail | Required/date/NCC/kho/access/duplicate; product/positive qty/price/draft state | Yes; definition match | No |
| Xuất (3002) | F3002_sp_ins_Xuat_Kho_Header / Detail | Required/date/kho/access/duplicate; product/positive qty/price/draft state; reservation | Yes; definition match | No |

Runtime `sp_XNK_Reservation_Adjust` được đọc riêng: khóa scope và kiểm tra CurrentQuantity - ReservedQuantity trước khi giữ chỗ; THROW 51140 khi không đủ khả dụng. Vì vậy bỏ C# preview stock pre-check không bỏ enforcement tồn kho. Quyền Add được giữ ở entry callback; quyền kho vẫn kiểm tra ở SQL. Required/unique business checks không còn bị duplicated bằng C# validators.

Gaps cần giữ ở C#: độ dài SQL parameter trước binding, parse date/decimal, FK từ mã/tên Excel, tên hàng khớp mã, quan hệ Header–Detail, ít nhất một detail, quyền Add. Chúng không được âm thầm bỏ chỉ vì không có cùng check trong Insert SP.

## F. Test results

| Check | Result | Evidence / scope |
|---|---|---|
| Data Access build | PASS; 0 errors, 0 warnings | dotnet build Data_Access.csproj --no-restore --verbosity quiet |
| Web host final build | PASS; 0 errors, 24 license warnings | dotnet build Web.csproj --no-restore --verbosity quiet; Telerik license warnings TKL002/TKL004/TKL101 |
| Focused + regression final | 181 passed, 0 failed, 0 skipped | company-original-import-final.trx |
| SQL + target guards | 9 passed, 0 failed, 0 skipped | 5 transactional Master Insert/duplicate/rollback cases + 4 database-target guard cases; company-original-import-sql.trx |
| Total final distinct executed tests | 190 passed | Không cộng thêm 147 test cũ vì đó là subset của 181 |
| XNK SQL integration | NOT VERIFIED | Approved test DB có 5 SP Master nhưng không có bốn SP F3001/F3002; không deploy sang test DB trong phase này |
| Browser smoke/UAT | NOT RUN | Không có host listen ở localhost:15073/5073 khi kiểm tra; không chạy import thật trên business DB |
| Import Log/Action History/Cache via browser | SOURCE VERIFIED, E2E NOT VERIFIED | FBase giữ owner log/queue; source tests kiểm tra hậu commit/ref ID. Không tạo log/history thật trên business DB |
| git diff --check | PASS | Không whitespace errors |
| CodeGraph | SYNCED | Sync sau khi bỏ pipeline; kiểm tra status cuối |

SQL tests chạy trên đúng database allowlist `TKS_Thuc_Tap_V11_C01C02_Test_20260906`; xác minh @@SERVERNAME/DB_NAME trước khi mở transaction. Tất cả fixture transaction được rollback; hậu kiểm COUNT=0 theo khóa UUID; không commit fixture và không đổi schema/SP. Test DB không được xem là bản sao đầy đủ của business DB. CConfig của test được đặt/khôi phục trong scope không chạy song song để CSqlHelper đọc parameter metadata đúng test DB.

Một test có sẵn `WarehouseModuleStructureTests.Master_list_maps_unit_names_to_the_entity_property` thất bại trong lượt mở rộng đầu vì thiếu TKS_INTEGRATION_CONNECTION_STRING và kỳ vọng fixture Cái. Test này được loại khỏi lượt source regression cuối và vẫn **NOT VERIFIED**, không sửa hay tính nó là PASS/skipped. Bộ SQL mới có guard độc lập và không phụ thuộc dữ liệu mẫu có sẵn.

### Mapping/flow evidence

- `CompanyOriginalExcelImportTests`: hai loại phiếu; nhiều Header/Detail; dòng trống giữa/cuối; giữ leading-zero/Unicode; exact row 4 sau blank row 3; orphan/duplicate header/blank key; sai Detail header, cột phụ thứ 100, sai sheet; header-only; cached formula; ngày serial/text; decimal precision/overflow; single FBase ownership, no post, commit trước count/cache/history.
- Master UI/architecture/cache/export regression tests cập nhật expectation theo fixed positional DataRow, không profile adapter hoặc Preview/SKIP. Product FK lookup vẫn được kiểm tra, không ID mặc định.
- SQL tests: mỗi loại Master Insert trả ID >0; exact duplicate bị SqlException đúng code 51002/51011/51021/51031/51041; rollback loại toàn bộ fixture. Đây là controller/SP integration, **không phải** browser import hoặc kiểm thử commit thật/cache thật.

Commands đã chạy (các log/TRX nằm trong `C:\Users\Surface\AppData\Local\Temp\company-import-migration`):

```powershell
dotnet build TKS_Thuc_Tap_V11_Data_Access/TKS_Thuc_Tap_V11_Data_Access.csproj --no-restore --verbosity quiet
dotnet build TKS_Thuc_Tap_V11_Web/TKS_Thuc_Tap_V11_Web.csproj --no-restore --verbosity quiet
# Filter exact và lệnh đầy đủ: xem Appendix ở cuối; không chạy full suite hoặc integration trên DB nghiệp vụ.
git -c core.safecrlf=false diff --check
codegraph status
codegraph sync
```

## G. Remaining deviations / limits

1. Header–Detail mapping, FK resolution, parse và transaction per-document nằm trong partial code-behind của **cùng FWarehouse_Transaction_List**, không phải service/engine/import wrapper mới.
2. Chỉ chấp nhận contract template công ty. Customer header aliases, dynamic profiles, unsupported-capability diagnostics và explicit reference aliases của pipeline cũ đã bị bỏ theo yêu cầu. File khách hàng phải đổi về template chính thức; không có compatibility fallback.
3. CExcel gốc đọc cached cell.Value; không chạy evaluator/profile/formula reader mới. Test chứng minh cached formula value được map. Workbook có formula chưa lưu cached value hoặc legacy binary .xls chưa được chứng minh bằng browser; không công bố hỗ trợ đầy đủ các producer.
4. FBase gốc giữ hành vi log lỗi/0 rows đã có: 0 Success / 0 Error được ghi That_Bai; structural exceptions trước Entry được shared catch xử lý. Không thêm logger riêng để thay đổi contract này.
5. Partial success và count/transaction/hậu commit được chứng minh ở source tests. SQL rollback Header–Detail, permissions app login, Import Log queue và Action History qua runtime UI còn cần test DB có đủ XNK/schema và browser UAT. Không công bố FULL RUNTIME PARITY.

## Final comparison

| Module | Dispatcher | Duplicate policy | Transaction | History/cache/log | Final status |
|---|---|---|---|---|
| Kho (2009) | FCommon/FBase/Entry/CExcel gốc | SQL error, no SKIP | Per-row | Cache/history sau Commit; FBase log | IMPLEMENTED; source + Master SQL guards verified; UI E2E pending |
| Đơn vị tính (2016) | FCommon/FBase/Entry/CExcel gốc | SQL error, no SKIP | Per-row | Cache/history sau Commit; FBase log | IMPLEMENTED; source + Master SQL guards verified; UI E2E pending |
| Loại sản phẩm (2017) | FCommon/FBase/Entry/CExcel gốc | SQL error, no SKIP | Per-row | Cache/history sau Commit; FBase log | IMPLEMENTED; source + Master SQL guards verified; UI E2E pending |
| Sản phẩm (2018) | FCommon/FBase/Entry/CExcel gốc | SQL error, no SKIP | Per-row | Cache/history sau Commit; FBase log | IMPLEMENTED; source + Master SQL guards verified; UI E2E pending |
| Nhà cung cấp (2019) | FCommon/FBase/Entry/CExcel gốc | SQL error, no SKIP | Per-row | Cache/history sau Commit; FBase log | IMPLEMENTED; source + Master SQL guards verified; UI E2E pending |
| Nhập/Xuất | FCommon/FBase/Entry/CExcel gốc | SQL error, no SKIP | Per Header + all Details | Header.Auto_ID history; FBase log | IMPLEMENTED; source/mapping verified; XNK SQL and UI UAT pending |

## Appendix — exact final test filters

Final pure/source run: `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-restore --filter "(FullyQualifiedName~CompanyOriginalExcelImportTests|FullyQualifiedName~LegacyExcelHeaderValidationTests|FullyQualifiedName~MasterDataCompanyStyleMigrationTests|FullyQualifiedName~UnitExcelImportUiTests|FullyQualifiedName~CategoryExcelImportUiTests|FullyQualifiedName~ProductExcelImportUiTests|FullyQualifiedName~SupplierExcelImportUiTests|FullyQualifiedName~WarehouseExcelImportUiTests|FullyQualifiedName~WarehouseDmCacheSyncTests|FullyQualifiedName~WarehouseMasterFinalizationContractTests|FullyQualifiedName~WarehouseMasterEntityDecompositionContractTests|FullyQualifiedName~WarehousePermissionRefactorTests|FullyQualifiedName~WarehouseReportRefactorTests|FullyQualifiedName~WarehouseUiWorkflowTests|FullyQualifiedName~WarehouseDocumentExcelExportTests|FullyQualifiedName~WarehouseMasterExcelExportTests|FullyQualifiedName~XlsxParserCompatibilityTests|FullyQualifiedName~WarehouseNavigationTests|FullyQualifiedName~WarehouseMasterGetByIdContractTests|FullyQualifiedName~WarehouseMasterCreatedFilterContractTests|FullyQualifiedName~WarehouseModuleStructureTests)&FullyQualifiedName!~Master_list_maps_unit_names_to_the_entity_property" --logger "trx;LogFileName=company-original-import-final.trx"`.

SQL run: đặt TKS_INTEGRATION_CONNECTION_STRING cho đúng DB test allowlist trong process hiện tại, sau đó `dotnet test TKS_Thuc_Tap_V11_Data_Access.Tests/TKS_Thuc_Tap_V11_Data_Access.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~CompanyOriginalImportSqlTests|FullyQualifiedName~ProductImportDatabaseSafetyTests" --logger "trx;LogFileName=company-original-import-sql.trx"`. Không đặt cấu hình này vào appsettings/production.
