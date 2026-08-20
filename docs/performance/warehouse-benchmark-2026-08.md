# Performance Benchmark — Warehouse Module (tháng 8/2026)

## Môi trường benchmark

- Host: i7-8650U (4 lõi/8 luồng, 1.9GHz), 8GB RAM, SSD, Windows 10/11.
- DB benchmark riêng `TKS_Thuc_Tap_V11_Bench` — schema + stored procedures **giống hệt** DB thật (copy từ `TKS_Thuc_Tap_V11_GiaiDoan2`), không đụng dữ liệu thật.
- Dữ liệu tải hàng loạt (SQL set-based):

| Bảng | Số dòng |
|---|---|
| tbl_DM_San_Pham | 100.000 |
| tbl_DM_NCC | 5.000 |
| tbl_DM_Kho / Đơn vị tính / Loại SP | 50 / 20 / 100 |
| tbl_XNK_Nhap_Kho (phiếu nhập) | 250.000 |
| tbl_XNK_Nhap_Kho_Raw_Data (dòng nhập) | 2.500.000 |
| tbl_XNK_Xuat_Kho (phiếu xuất) | 150.000 |
| tbl_XNK_Xuat_Kho_Raw_Data (dòng xuất) | 1.500.000 |

Tổng ~4,4 triệu dòng, rải 2024-01-01 → 2026-08-19 (967 ngày). Đo bằng code path thật của app: `CSqlHelper.FillDataTable` + `CUtility.Map_Row_To_Entity` (harness console tham chiếu `TKS_Thuc_Tap_V11_Data_Access`), mỗi tác vụ warmup + 2 lần đo, lấy tốt nhất.

## Kết quả đo

### Trước khi thêm indexes

| Tác vụ (code path app) | SQL warm | End-to-end C# | Số dòng trả về | RAM tăng (heap) |
|---|---|---|---|---|
| Danh sách phiếu nhập | 2.4s | 1.4s | 250.000 | +126 MB |
| Chi tiết 1 phiếu | 0.04s | 0.04s | 10 | 0 |
| DM Sản phẩm (master) | 0.7s | 0.5s | 100.000 | +47 MB |
| Lookup Sản phẩm (combo) | 0.3s | 0.34s | 100.000 | +36 MB |
| BC Chi tiết Nhập (2025) | 9.3s | 5.8s | 943.190 | +517 MB |
| BC Chi tiết Xuất (2025) | 10.5s | 2.9s | 565.750 | +330 MB |
| BC Xuất-Nhập-Tồn (2025) | ~2s CPU | **23s** | **2.270.555** | **+1.234 MB** |

### Sau khi thêm 6 indexes

| Tác vụ | SQL warm trước→sau | End-to-end trước→sau | Ghi chú |
|---|---|---|---|
| Chi tiết 1 phiếu | 38ms → **0ms** | 39ms → **4ms** | Index Nhap_Kho_ID (missing-index impact 99%) |
| BC Chi tiết Nhập | 9.3s → **7.7s** | 5.8s → 7.2s* | *host load dao động |
| BC Chi tiết Xuất | 10.5s → **4.5s** (-57%) | 2.9s → 3.8s | Index Xuat_Kho_ID (impact 97%) |
| BC Xuất-Nhập-Tồn | ~2s CPU (không đổi) | 23s → **16.6s** | RAM vẫn **+1.2GB** — số dòng không giảm |

## Bottleneck — xếp hạng theo tác động

### 1. BC Xuất-Nhập-Tồn: 2,27 triệu dòng → 16-23s + 1,2 GB RAM (nghẽn nặng nhất)
- `sp_BC_Xuat_Nhap_Ton` trả **1 dòng cho mỗi cặp Kho × Sản phẩm** (GROUP BY Kho_ID, San_Pham_ID) → 1 năm = 2.270.555 dòng (output file text 890 MB).
- App materialize toàn bộ vào DataTable + map entity + `ToDictionary` (2,27M phần tử) + TelerikGrid render → chiếm ~1,2 GB heap, treo trình duyệt; máy 8GB RAM → tràn page file.
- **Nguyên nhân là kiến trúc, không phải query**: query chỉ ~2s CPU. Không index nào chữa được việc trả hàng triệu dòng về client.

### 2. BC Chi tiết Nhập/Xuất: 566k-943k dòng → 3-10s + 330-517 MB
- Cùng vấn đề kiến trúc: toàn bộ kết quả theo năm được đổ về client; đã giảm 20-57% nhờ indexes nhưng vẫn nặng.
- Cộng hưởng với điểm 1 khi user chạy nhiều tab báo cáo: 8GB RAM cạn → mọi query khác chậm theo.

### 3. Không phân trang server-side ở bất kỳ màn hình nào
- Danh sách phiếu: `sp_XNK_Document_List` load **250.000 dòng** mỗi lần mở màn hình (grid Telerik phân trang client-side).
- DM Sản phẩm: `sp_DM_Master_List`/`sp_DM_Lookup_List` trả **100.000 dòng** vào combo/grid (100k key-lookup + sort mỗi lần).
- `CConfig.Page_Size = 50` tồn tại nhưng không được dùng cho các load này.

### 4. Hạ tầng (nghẽn môi trường, khuếch đại mọi thứ)
- **RAM 8GB + C: đầy gần 100%** trong lúc benchmark → query treo 15 phút (không phải do query): tempdb không grow được, page file tràn.
- SQL Server `max server memory = 2147483647` (không giới hạn) nhưng máy chỉ 8GB.
- tempdb: 8 files x 8MB, **log 8MB tăng 8MB/lần** → báo cáo lớn làm tempdb log đầy (Msg 9002) — chính là lần treo 15 phút.

### 5. Lỗi code liên quan hiệu năng
- `CLogger.Write_To_File`: `Substring(0, LastIndexOf("\\"))` → **crash `ArgumentOutOfRangeException`** khi `File_Name` không chứa thư mục (LastIndexOf = -1); đã sửa.
- Trace ghi log (mở/đóng StreamWriter) **mỗi lần FillDataTable** — overhead file I/O trên mọi thao tác dữ liệu.

## Đã áp dụng (đã đo xác nhận)

1. **6 indexes** trong `Database/WarehouseModule.Schema.sql` (idempotent, đã deploy DB thật):
   - `IX_tbl_XNK_Nhap_Kho_Raw_NhapKho_ID` (Nhap_Kho_ID) INCLUDE (San_Pham_ID, SL_Nhap, Don_Gia_Nhap) — detail list + BC Chi tiết Nhập
   - `IX_tbl_XNK_Xuat_Kho_Raw_XuatKho_ID` (Xuat_Kho_ID) INCLUDE (San_Pham_ID, SL_Xuat, Don_Gia_Xuat)
   - `IX_tbl_XNK_Nhap_Kho_Ngay` / `IX_tbl_XNK_Xuat_Kho_Ngay` — lọc theo ngày cho báo cáo
   - `IX_tbl_XNK_Nhap_Kho_Raw_SanPham_ID` / `IX_tbl_XNK_Xuat_Kho_Raw_SanPham_ID` — join aggregation Xuất-Nhập-Tồn
2. **Fix crash `CLogger`** (`TKS_Thuc_Tap_V11_Data_Access/Utility/CLogger.cs`).
3. **Regression guard**: `Database/Tests/WarehousePerformanceIndexes.IntegrationTests.sql` — fail nếu DB thiếu indexes (phòng trường hợp deploy app mà quên deploy schema).

## Đề xuất tiếp theo (cần quyết định — chưa làm)

| Phương án | Tác động | Công sức |
|---|---|---|
| **A. Phân trang server-side** cho lưới báo cáo + danh sách phiếu + DM (TelerikGrid OnRead + SP trả `TOP N` + total) | Giảm 100-1000x lượng dữ liệu về client; XuatNhapTon từ 1,2GB RAM → vài MB | Lớn (đổi hợp đồng SP + controller + razor) |
| **B. Tổng hợp Xuất-Nhập-Tồn theo sản phẩm** (bỏ Kho khỏi GROUP BY, hoặc pivot Kho thành cột) | 2,27M → ~100k dòng, RAM ~50MB | Trung bình (sửa 1 SP + entity) |
| **C. Giới hạn RAM SQL Server** (max server memory ~5GB) + tăng tempdb (data 1GB/log 512MB, growth 256MB) + dọn C: | Giảm swap, hết cảnh treo 15 phút | Nhỏ (server config, không phải code) |
| **D. Tắt trace log khi production** (CLogger.Enable_Trace=false hoặc ghi log gộp) | Giảm file I/O mỗi thao tác | Nhỏ |

Khuyến nghị: làm **C ngay** (miễn phí, giảm đau tức thì), rồi **A hoặc B** cho báo cáo (giải quyết bottleneck số 1).