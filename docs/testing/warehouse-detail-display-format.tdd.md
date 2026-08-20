# TDD Evidence Report — Warehouse Detail Display Format

## Source plan

Không có `*.plan.md`; user journey được viết trong phiên TDD này theo yêu cầu người dùng:
"sửa format hiển thị số lượng, đơn giá, trị giá" của phiếu Nhập/Xuất kho và "hiển thị thêm đơn vị tính kế bên số lượng".

## User journeys

- **J1** — Người dùng xem chi tiết phiếu Nhập/Xuất: thấy số lượng dạng `60.00` kèm đơn vị tính `(Chai)`, đơn giá dạng `6.200`, trị giá dạng `372.000` (mệnh giá tiền Việt Nam, bỏ phần thập phân thừa).
- **J2** — Người dùng xem tab Báo cáo (chi tiết nhập/xuất + xuất nhập tồn): số lượng/dơn giá/trị giá hiển thị cùng format, không còn `60,000` / `6200,00` / `372000,00000`.

## Task report

| # | Nhiệm vụ | Thực thi | Validation | Kết quả |
|---|----------|----------|------------|---------|
| 1 | Viết test RED: helpers format tiền/số lượng, property `Ten_Don_Vi_Tinh`, SP trả tên đơn vị, razor dùng helper | `WarehouseDetailDisplayFormatTests.cs` (13 test: 2 Theory + 5 Fact) | `dotnet test` | **RED** — compile fail CS0117/CS1061 đúng nguyên nhân thiếu implementation (commit `bb2b28c`) |
| 2 | Implement: `CUtility.Format_So_Tien`/`Format_So_Luong`, `CWarehouseDocumentDetail.Ten_Don_Vi_Tinh`, SP `sp_XNK_Document_Detail_List` join `tbl_DM_Don_Vi_Tinh`, template grid 3 razor | 7 file | `dotnet test` + `dotnet build TKS_Thuc_Tap_V11_Web_Danh_Muc` | **GREEN** — 28/28 test pass, build succeeded (commit `77bd280`) |

## Test specification

| # | What is guaranteed | Test file / test | Test type | Result | Evidence |
|---|--------------------|------------------|-----------|--------|----------|
| 1 | `Format_So_Tien(6200)=="6.200"`, `(372000)=="372.000"`, `(0)=="0"`, `(123456789)=="123.456.789"`, `(-6200)=="-6.200"` | WarehouseDetailDisplayFormatTests.Format_So_Tien_uses_vnd_thousands_separator_without_decimals | unit | PASS | `dotnet test` 28/28 |
| 2 | `Format_So_Luong(60)=="60.00"`, `(1.5)=="1.50"`, `(1234.567)=="1234.57"` | WarehouseDetailDisplayFormatTests.Format_So_Luong_shows_two_decimals_with_dot_separator | unit | PASS | `dotnet test` 28/28 |
| 3 | Entity `CWarehouseDocumentDetail` có `Ten_Don_Vi_Tinh` (set được, đọc lại đúng) | WarehouseDetailDisplayFormatTests.Warehouse_document_detail_exposes_the_product_unit_name | unit | PASS | `dotnet test` 28/28 |
| 4 | SP `sp_XNK_Document_Detail_List` join `tbl_DM_Don_Vi_Tinh` và select `dv.Ten_Don_Vi_Tinh` | WarehouseDetailDisplayFormatTests.Warehouse_detail_stored_procedure_returns_the_product_unit_name | source-contract | PASS | `dotnet test` 28/28 |
| 5 | Màn chi tiết phiếu: cột SL dùng `CUtility.Format_So_Luong` + `v_objDetail.Ten_Don_Vi_Tinh`, cột Đơn giá/Trị giá dùng `CUtility.Format_So_Tien` | WarehouseDetailDisplayFormatTests.Warehouse_detail_view_shows_unit_next_to_quantity_and_formats_money_with_vnd_style | source-contract | PASS | `dotnet test` 28/28 |
| 6 | Grid báo cáo chi tiết + xuất nhập tồn dùng `CUtility.Format_So_Luong`/`Format_So_Tien` | WarehouseDetailDisplayFormatTests.Warehouse_report_grids_format_quantity_and_money_with_shared_helpers | source-contract | PASS | `dotnet test` 28/28 |
| 7 | Form sửa chi tiết: ô Trị giá (disabled) dùng `CUtility.Format_So_Tien` | WarehouseDetailDisplayFormatTests.Warehouse_detail_editor_formats_the_total_value_with_vnd_style | source-contract | PASS | `dotnet test` 28/28 |

## Coverage and known gaps

- Không đo coverage (dự án test hiện không cấu hình coverage collector; theo convention repo hiện tại).
- **Gap:** test source-contract không kiểm tra render thực tế của TelerikGrid (không có bUnit); đây là convention đã có sẵn của repo (`WarehouseUiWorkflowTests`).
- **Gap:** SP `sp_XNK_Document_Detail_List` trong `Database/WarehouseModule.Procedures.sql` đã sửa nhưng **chưa chạy lại script trên DB thật** — cần `CREATE OR ALTER` lại SP trên máy người dùng để cột `Ten_Don_Vi_Tinh` có dữ liệu.
- **Không đụng** `sp_BC_*` (báo cáo) — giữ nguyên contract test `WarehouseModule.ReportContract.IntegrationTests.sql` (8 cột).

## Merge evidence

- RED: commit `bb2b28c` — `test: reproduce warehouse detail money/quantity format and unit column gaps` (compile fail CS0117/CS1061 do thiếu implementation).
- GREEN: commit `77bd280` — `fix: format warehouse detail and report money/quantity display, show product unit next to quantity` (28/28 test pass, build Web_Danh_Muc thành công).
- Refactor: không cần (thay đổi tối thiểu).
