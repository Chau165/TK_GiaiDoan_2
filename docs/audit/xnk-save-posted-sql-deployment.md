# Báo cáo triển khai SQL — Nhập kho 3001 / Xuất kho 3002

**Trạng thái SQL:** DEPLOYED & VERIFIED  
**Thời điểm:** 2026-11-02 11:55:20–11:55:21, Asia/Saigon (catalog timestamps; runner xác nhận COMMIT)  
**SQL Server / database:** `DESKTOP-NHQ7QPL\MSSQLSERVER19` / `TKS_Thuc_Tap_V11_GiaiDoan2`

## 1. Kết quả

Đã áp dụng đúng migration XNK vào database nghiệp vụ. Lần chạy đầu bị chặn do SQL Server chưa chạy; sau khi người dùng khởi động service, kết nối xác nhận đúng server/database và các bước preflight, backup, migration, postflight đều hoàn tất.

Migration được chạy trong một transaction duy nhất qua `sqlcmd`; chỉ COMMIT sau khi toàn bộ batch `GO` hoàn tất. Không chạy câu lệnh tạo/sửa chứng từ, tồn kho hoặc dữ liệu lịch sử. Không deploy Web, không commit/push.

## 2. Target, trạng thái và quyền

- `@@SERVERNAME`: `DESKTOP-NHQ7QPL\MSSQLSERVER19`; `DB_NAME()`: `TKS_Thuc_Tap_V11_GiaiDoan2`.
- SQL Server `15.0.2200.2`; database `ONLINE`, `MULTI_USER`, `SIMPLE`, không read-only.
- Tài khoản phiên triển khai có quyền `sysadmin`/`db_owner`, `CREATE TABLE`, `CREATE PROCEDURE`, `ALTER` schema `dbo` và backup. Database owner login tồn tại, đang bật.
- `EXECUTE AS USER='dbo'` trả `IS_MEMBER('db_owner')=1`; ngữ cảnh owner thỏa ngoại lệ guard Posted đang có.
- Local Web config đọc `TKS_Thuc_Tap_V11_Conn_String` từ `appsettings.json`, trỏ `localhost` + đúng database với `Integrated Security=True`; không có SQL username/password trong cấu hình này. Không ghi credential vào báo cáo.
- Role `Warehouse_Application` tồn tại nhưng hiện có **0 thành viên**. Migration chỉ cấp đúng GRANT/DENY trên các object mới cho role này; không thêm thành viên, không cấp `db_owner` và không thay quyền tài khoản hiện hữu. Phiên hiện tại là sysadmin. Nếu Web chạy dưới Windows identity khác không có quyền hiện hành, identity đó cần được đưa vào role/quyền phù hợp trước khi vận hành.

## 3. Audit source và phạm vi SQL

| File | SHA-256 | Xử lý |
|---|---|---|
| `Database/WarehouseDocumentSavePosted.sql` | `67E55AF8FFDB65C33553DC9C60F3154040A7AA1B09113CEA64301E4DFD156FA5` | Đã áp dụng |
| `Database/WarehouseDocumentSavePosted.Preflight.sql` | `6DB915D28D7455A1D744C8C625BCA9205112D06B56AD78B09DFFB7E2DF40CCD5` | Chỉ đọc; PASS trước và sau migration |
| `Database/Initialize-XnkTestDatabase.ps1` | `D766B92CEEC47D53790525C2D1D6BA8B2957F76671811ED52407284F4211671D` | Chỉ dành cho database test; không chạy |
| `work/test-post-definition.sql` | `8E43DCBAB66435EB97DA57E212B7C5384C65FC94B533B6FDF438CEE069A66928` | Tệp chẩn đoán tạm; không chạy |

Database không có sẵn object nào trong bốn tên migration trước khi chạy. Vì vậy các `CREATE OR ALTER` trong migration đã tạo đúng ba SP mới, không ghi đè phiên bản cũ. Migration không sửa SP XNK/Post, Inventory Ledger/Projection/Report, trigger hoặc function cũ; không sửa Master Data.

Object đã tạo:

- Bảng `dbo.XNK_Document_Revision`, với PK trên `Request_ID` và unique constraint `(Is_Receipt, Document_ID, Document_Version)`.
- `dbo.sp_XNK_Document_Read_State`.
- `dbo.sp_XNK_Document_Edit_Get`.
- `dbo.sp_XNK_Document_Save_Posted` (`EXECUTE AS OWNER`).
- Với role `Warehouse_Application`: GRANT EXECUTE cho `Edit_Get` và `Save_Posted`; DENY EXECUTE cho serializer nội bộ; DENY SELECT/INSERT/UPDATE/DELETE trực tiếp trên bảng revision.

Không có index độc lập ngoài các index của PK/unique constraint mới. Không có trigger/function mới.

## 4. Preflight database

`WarehouseDocumentSavePosted.Preflight.sql` chạy thành công **trước và sau** migration:

- 83 hash module SQL phụ thuộc khớp baseline; không đổi expected hash.
- Hai unique index số phiếu vẫn unique, enabled, không filtered, đúng duy nhất cột số phiếu.
- Schema và precision cột Header/Detail/Inventory khớp migration; quantity `decimal(18,3)`, giá `decimal(18,2)`.
- Bảy FK XNK đều bật và trusted; quan hệ Header/Detail CASCADE và FK master NO_ACTION được giữ nguyên.
- Đủ 10 trigger XNK, không trigger nào bị tắt: bốn Posted guards và sáu snapshot invalidation triggers.
- `InventoryBalance_Current`, `InventoryReservation_Current`, report scope catalog, các Inventory Fence procedures/functions/table types, Post, reservation release, invalidation procedures và lookup/permission dependencies tồn tại với signature cần dùng.
- Bảng/procedure revision chưa từng tồn tại tại preflight trước deploy; sau deploy đủ bốn object.

Các module mới production cũng được so hash với ba definition trên clone test `TKS_Thuc_Tap_V11_XNK_Test_20261102`; cả ba **EXACT MATCH** sau chuẩn hóa line ending/whitespace:

| Procedure | SHA-256 định nghĩa production và clone |
|---|---|
| `sp_XNK_Document_Edit_Get` | `61AC4B3BF809E85A5F305E4569EE61F06E0185CAB5CC681B86D97848B87B4EA6` |
| `sp_XNK_Document_Read_State` | `61D1A69627952355554E7A8099A4DC92D2B80E0A1EE4E834DA1A13A264C0FAB3` |
| `sp_XNK_Document_Save_Posted` | `0F005D88D92D54AEF4EDD2719F9B4C3946B142AF6CADC60A425921A1202B0430` |

## 5. Backup trước migration

Database recovery model `SIMPLE`; trước backup còn khoảng 7,288 MB trống trên `C:`. Backup history có full backup ngày 2026-10-29; `RESTORE HEADERONLY` xác nhận đúng server/database và `RESTORE VERIFYONLY WITH CHECKSUM` thành công.

Đã tạo backup mới ngay trước deploy:

`C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER19\MSSQL\Backup\TKS_Thuc_Tap_V11_GiaiDoan2_XNK_20261102_115348_10f095f7.bak`

- Kiểu `COPY_ONLY` full backup, có `COMPRESSION` và `CHECKSUM`.
- `BACKUP DATABASE` hoàn tất; header ghi thời gian 11:53:48–11:53:49, đúng database/server, `IsCopyOnly=1`, `HasBackupChecksums=1`, `IsDamaged=0`, position 1.
- `RESTORE VERIFYONLY ... WITH CHECKSUM`: **passed**, backup set hợp lệ và đọc được.
- Không thực hiện full restore rehearsal sang database khác; VERIFYONLY không thay thế một lần restore thực tế.

## 6. Thực thi migration

Đã chạy đúng `Database/WarehouseDocumentSavePosted.sql` với SHA-256 nêu trên, UTF-8, SQLCMD `-b`. Runner tạm ở:

`C:\Users\Surface\AppData\Local\Temp\xnk-sql-deploy-c436f9f86a09440083783d32070c5fc0.sql`

Runner kiểm tra đích server/database và object names, đặt `XACT_ABORT ON`, mở transaction, include migration qua `:r`, rồi chỉ COMMIT khi các batch hoàn tất và transaction còn committable. `sqlcmd` trả `MIGRATION_TRANSACTION_COMMITTED`, exit code 0. Runner dùng cú pháp SQLCMD `:r`, `GO` và `:ON ERROR EXIT` theo [Microsoft Learn — sqlcmd commands](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-commands?view=sql-server-ver17).

## 7. Postflight và compatibility

- Bốn object mới tồn tại đúng schema; revision table có đủ 10 cột, precision/type/NULLability đúng migration; revision row count = **0**.
- `Save_Posted` có `execute_as_principal_id=-2` (OWNER). Signature gồm 12 tham số; kiểu, chiều và độ dài khớp với controller. `Edit_Get` có ba tham số.
- Result contract khớp source C#: `Edit_Get` trả `Document_State`, `State_Token`, `Document_Version`; Save trả `Auto_ID`, `Document_Version`. Controller mới gửi đúng thứ tự 12 đối số của Save và đọc đúng các result column.
- Permission catalog xác nhận đúng các GRANT/DENY đã liệt kê; không cấp quyền ghi ledger trực tiếp.
- Sau migration, preflight lại PASS; hai unique indexes vẫn hoạt động; 10 trigger XNK vẫn bật; 0 FK disabled/untrusted.
- Không gọi procedure Save/Post/Edit trên phiếu thật để smoke test. Không chạy integration tests trên production. Web không được deploy/khởi động trong tác vụ này; vì vậy runtime end-to-end và identity của một Web host khác chưa được xác minh.

## 8. Bảo toàn dữ liệu và thay đổi ngoài phạm vi

Migration chỉ tạo bảng/procedure và quyền trên object mới. Các procedure body không được thực thi trong lúc deploy. Bảng revision rỗng; không tạo/sửa/xóa phiếu, không đổi tồn/reservation, không post Draft, không rebuild/reset Inventory, không tắt fence/trigger. Preflight sau deploy xác nhận các module cũ và chỉ mục bắt buộc vẫn khớp baseline.

Không ALTER procedure/function/trigger cũ; không deploy file trong `work/`; không thay đổi startup type của SQL Server; không commit/push.

## 9. Lỗi trong quá trình kiểm tra

Một truy vấn sizing ban đầu tính `max_size * 8` bằng kiểu `int` và nhận `Msg 8115` overflow. Đây là truy vấn chẩn đoán chỉ đọc, không phải migration; đã đổi phép tính sang `bigint` và chạy lại thành công. Không có lỗi backup, preflight, migration hoặc postflight.

## 10. Rollback và rủi ro còn lại

- Migration transaction đã commit. Trước khi Web rollout, revision row count đang 0; rollback schema vẫn khả thi bằng cách gỡ ba procedure và bảng mới cùng quyền role, sau khi xác minh lại bảng vẫn rỗng. Chưa thực hiện rollback.
- Nếu phải RESTORE backup, database sẽ quay về thời điểm 11:53:49 và mất mọi giao dịch nghiệp vụ phát sinh sau đó; cần dừng ghi và điều phối restore. Không dùng restore như cách rollback mặc định cho migration DDL này.
- `Warehouse_Application` chưa có thành viên. Nếu Web chạy bằng identity khác với identity có quyền hiện tại, cần cấp thành viên/quyền phù hợp trước vận hành; không cấp `db_owner` trực tiếp.
- Chưa chạy Web production hoặc kiểm thử runtime trên chứng từ thật. Đường đọc/ghi mới được kiểm tra bằng source/signature/object metadata và test clone; production Save không được gọi.

## 11. Kết luận trạng thái

| Hạng mục | Trạng thái |
|---|---|
| SQL migration | **DEPLOYED & VERIFIED** |
| Backup mới | **CREATED & VERIFYONLY PASSED** |
| Baseline / old inventory modules | **MATCHED trước và sau** |
| Object, permission, signature postflight | **VERIFIED** |
| Dữ liệu phiếu/tồn lịch sử | **Không bị migration thay đổi** |
| SQL sẵn sàng cho controller mới | **Có; contract/schema đã xác minh** |
| Web production rollout/runtime | **NOT DEPLOYED / NOT VERIFIED** |
| Quyền cho identity Web khác | **Cần xác nhận nếu khác tài khoản Integrated Security hiện hành** |