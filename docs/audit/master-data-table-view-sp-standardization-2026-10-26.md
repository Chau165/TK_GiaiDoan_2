# Chuẩn hóa 5 Master Data theo Table + View + Stored Procedure

## Phạm vi và quyết định

Đã chuẩn hóa các Master Data `Đơn vị tính`, `Kho`, `NCC`, `Loại sản phẩm` và
`Sản phẩm` theo mẫu `DM_Chu_Hang`. Theo lựa chọn đã xác nhận trong phiên làm
việc, bổ sung cột `deleted` cho 5 bảng đích. Không sửa bảng `tbl_DM_Chu_Hang`,
không đổi unique index/constraint hiện hữu và không triển khai vào database
nghiệp vụ.

Tên View dùng convention thực tế của repository là `view_DM_NCC`, không dùng
`tên định hướng` `view_DM_Nha_Cung_Cap`, vì table, entity, controller và SP hiện
đều dùng viết tắt `NCC`.

## SQL đã thay đổi

| Nhóm | Thay đổi |
|---|---|
| Schema | `Database/WarehouseModule.Schema.sql` khai báo `deleted INT NULL DEFAULT 0` cho 5 bảng và có nhánh nâng cấp idempotent `WITH VALUES` cho database đã tồn tại. |
| View | `Database/WarehouseMaster.Views.sql` tạo 5 View, lấy đủ các cột business và audit từ đúng Table, không JOIN, điều kiện duy nhất là `WHERE (A.deleted = 0)`. |
| Read SP | List, cache, lookup, page, GetByID và ByCreated đọc từ View. Các kiểm tra active master, duplicate, FK và quyền kho cũng đọc từ View. |
| Write SP | Insert ghi Table với `deleted = 0`; Update ghi Table và giữ nguyên `deleted`; Delete đổi thành `UPDATE ... SET deleted = 1` và cập nhật audit. Parameter, tên SP và projection C# hiện hữu được giữ nguyên. |
| Lịch sử nghiệp vụ | Các join tên master trong luồng chứng từ/báo cáo lịch sử tiếp tục đọc Table để không làm mất tên của chứng từ cũ sau soft delete. |
| C# | Controller, entity và Data Access contract không cần đổi. Contract test được cập nhật từ Table sang View và từ physical delete sang soft delete. |

Unique index vẫn giữ nguyên. Vì vậy mã/tên của bản ghi đã soft delete chưa thể
được tái sử dụng; đây là giới hạn được ghi nhận thay vì tự ý đổi constraint.

## Kiểm chứng hiện có

- Đã audit metadata/definition bằng truy vấn chỉ đọc trên
  `DESKTOP-NHQ7QPL\MSSQLSERVER19 / TKS_Thuc_Tap_V11_GiaiDoan2`: 5 bảng đích
  trước thay đổi chưa có `deleted`, chưa có 5 View đích; mẫu View và nhóm SP
  `DM_Chu_Hang` hiện hữu.
- Contract/static tests: `117 passed, 0 failed, 0 skipped` với nhóm test liên
  quan đến View, list/cache/lookup/page, GetByID, ByCreated, delete và UI
  workflow.
- Web project build: `dotnet build ... --no-restore -v:q` thành công, `0 Error`
  và `12 Warning` hiện hữu (chủ yếu cảnh báo license Telerik và analyzer).
- `git diff --check`: passed.
- SQL integration rollback-only đã cung cấp tại
  `Database/Tests/WarehouseMaster.ViewSoftDelete.IntegrationTests.sql`. Script
  có guard database, tạo dữ liệu qua SP, kiểm tra View/Table trước và sau
  soft delete rồi luôn rollback.

## Deployment database nghiệp vụ

Đã triển khai vào `DESKTOP-NHQ7QPL\\MSSQLSERVER19` /
`TKS_Thuc_Tap_V11_GiaiDoan2` bằng Windows Integrated Security. Đã chụp
preflight definition/row-count trước khi ghi. Schema delta thêm 5 cột
`deleted` trong transaction; sau đó chạy View, `WarehouseModule.Procedures.sql`,
`WarehouseMaster.GetById.Procedures.sql` và
`WarehouseMaster.ByCreated.Procedures.sql` theo đúng thứ tự.

Postflight read-only xác nhận database `READ_WRITE/ONLINE`, row count không đổi
và tất cả dòng hiện hữu có `deleted = 0`: DVT 10, Kho 4, LSP 5, NCC 5,
Sản phẩm 15. Smoke runtime cho list/cache/lookup/GetByID/ByCreated đều khớp
với View active; GetByID trả đúng 1 dòng cho mỗi master.

Không chạy insert/update/delete dữ liệu nghiệp vụ trên database này. SQL
integration rollback-only vẫn để dành cho database test cô lập.

Khi triển khai lại ở môi trường khác, chạy theo thứ tự:

1. `WarehouseModule.Schema.sql`
2. `WarehouseMaster.Views.sql`
3. `WarehouseModule.Procedures.sql`
4. `WarehouseMaster.GetById.Procedures.sql`
5. `WarehouseMaster.ByCreated.Procedures.sql`
6. Các script security và integration test theo `Database/README.md`

Sau khi deploy vào test DB được phê duyệt, chạy script rollback-only nêu trên
để xác nhận runtime View, giữ row trong Table và `deleted = 1`.
