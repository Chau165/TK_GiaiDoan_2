using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace TKS_Thuc_Tap_V11_Benchmarks_V22;

internal sealed record V22OraclePage(
    string RootProcedure,
    string Confidence,
    long TotalCount,
    List<Dictionary<string, object?>> Rows);

internal static class V22CorrectnessOracle
{
    internal static readonly DateTime ReportFrom = new(2025, 1, 1);
    internal static readonly DateTime ReportTo = new(2026, 12, 31);
    internal const int PageNumber = 1;
    internal const int PageSize = 10;
    internal const string LoginName = "PERF_USER";

    internal static string SqlBundle => string.Join(
        Environment.NewLine + Environment.NewLine + "-- =====================================================================" + Environment.NewLine,
        MasterSql, LookupSql, DocumentSql, DetailSql, HistoricalSql, CurrentSql);

    internal static async Task<V22OraclePage> ReadAsync(SqlConnection connection, string scenario)
    {
        var (sql, root, confidence) = scenario switch
        {
            "MasterPaged" => (MasterSql, "dbo.sp_DM_Master_Page", "HIGH"),
            "LookupPaged" => (LookupSql, "dbo.sp_DM_Lookup_Page", "HIGH"),
            "DocumentPaged" => (DocumentSql, "dbo.sp_XNK_Document_Page", "MEDIUM"),
            "DetailReportPaged" => (DetailSql, "dbo.sp_BC_Chi_Tiet_Nhap_Page", "MEDIUM"),
            "InventoryHistoricalReportPaged" => (HistoricalSql, "dbo.sp_BC_Xuat_Nhap_Ton_Page", "MEDIUM"),
            "InventoryCurrentBalancePaged" => (CurrentSql, "dbo.sp_BC_Ton_Kho_Hien_Tai_Page", "MEDIUM"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown correctness scenario.")
        };

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = sql;
        AddParameters(command, scenario);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidDataException($"Independent oracle returned no count row for {scenario}.");

        var totalCount = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
        if (!await reader.NextResultAsync())
            throw new InvalidDataException($"Independent oracle returned no page result set for {scenario}.");

        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                row[reader.GetName(index)] = value is DBNull ? null : value;
            }
            rows.Add(row);
        }

        return new V22OraclePage(root, confidence, totalCount, rows);
    }

    private static void AddParameters(SqlCommand command, string scenario)
    {
        Add(command, "@Page_Number", SqlDbType.Int, PageNumber);
        Add(command, "@Page_Size", SqlDbType.Int, PageSize);
        switch (scenario)
        {
            case "MasterPaged":
            case "LookupPaged":
                break;
            case "DocumentPaged":
                Add(command, "@LoginName", SqlDbType.NVarChar, LoginName, 100);
                Add(command, "@WarehouseId", SqlDbType.BigInt, DBNull.Value);
                break;
            case "DetailReportPaged":
            case "InventoryHistoricalReportPaged":
                Add(command, "@FromDate", SqlDbType.Date, ReportFrom.Date);
                Add(command, "@ToDate", SqlDbType.Date, ReportTo.Date);
                Add(command, "@LoginName", SqlDbType.NVarChar, LoginName, 100);
                break;
            case "InventoryCurrentBalancePaged":
                Add(command, "@LoginName", SqlDbType.NVarChar, LoginName, 100);
                break;
        }
    }

    private static void Add(SqlCommand command, string name, SqlDbType type, object value, int size = 0)
    {
        var parameter = command.Parameters.Add(name, type);
        if (size > 0) parameter.Size = size;
        parameter.Value = value;
    }

    private const string MasterSql = """
SELECT COUNT_BIG(*) AS Total_Count
FROM dbo.tbl_DM_San_Pham;

SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name,
       Loai_San_Pham_ID AS Related_ID, Don_Vi_Tinh_ID AS Related_ID_2, Ghi_Chu
FROM dbo.tbl_DM_San_Pham
ORDER BY Ma_San_Pham
OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
""";

    private const string LookupSql = """
SELECT COUNT_BIG(*) AS Total_Count
FROM dbo.tbl_DM_San_Pham;

SELECT Auto_ID, Ma_San_Pham AS Code, Ten_San_Pham AS Name
FROM dbo.tbl_DM_San_Pham
ORDER BY Ma_San_Pham
OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
""";

    private const string DocumentSql = """
SELECT COUNT_BIG(*) AS Total_Count
FROM dbo.tbl_XNK_Nhap_Kho h
JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
WHERE EXISTS
(
    SELECT 1 FROM dbo.tbl_DM_Kho_User ku
    WHERE ku.Ma_Dang_Nhap = @LoginName AND ku.Kho_ID = h.Kho_ID
)
  AND (@WarehouseId IS NULL OR h.Kho_ID = @WarehouseId);

SELECT h.Auto_ID, CAST(1 AS BIT) AS Is_Receipt,
       h.So_Phieu_Nhap_Kho AS So_Phieu, h.Kho_ID, k.Ten_Kho,
       h.NCC_ID, n.Ten_NCC, h.Ngay_Nhap_Kho AS Ngay_Chung_Tu,
       h.Is_Posted, h.Ghi_Chu
FROM dbo.tbl_XNK_Nhap_Kho h
JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = h.Kho_ID
JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = h.NCC_ID
WHERE EXISTS
(
    SELECT 1 FROM dbo.tbl_DM_Kho_User ku
    WHERE ku.Ma_Dang_Nhap = @LoginName AND ku.Kho_ID = h.Kho_ID
)
  AND (@WarehouseId IS NULL OR h.Kho_ID = @WarehouseId)
ORDER BY h.Ngay_Nhap_Kho DESC, h.Auto_ID DESC
OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
""";

    private const string DetailSql = """
SELECT COUNT_BIG(*) AS Total_Count
FROM dbo.tbl_XNK_Nhap_Kho h
JOIN
(
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @LoginName
) aw ON aw.Kho_ID = h.Kho_ID
JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
WHERE h.Ngay_Nhap_Kho >= @FromDate
  AND h.Ngay_Nhap_Kho <= @ToDate
  AND h.Is_Posted = 1;

;WITH PageScope AS
(
    SELECT d.Auto_ID AS __Detail_ID, h.Auto_ID AS __Document_ID,
           h.Ngay_Nhap_Kho AS Ngay, h.So_Phieu_Nhap_Kho AS So_Phieu,
           h.NCC_ID, d.San_Pham_ID, d.SL_Nhap AS So_Luong,
           d.Don_Gia_Nhap AS Don_Gia
    FROM dbo.tbl_XNK_Nhap_Kho h
    JOIN
    (
        SELECT DISTINCT Kho_ID
        FROM dbo.tbl_DM_Kho_User
        WHERE Ma_Dang_Nhap = @LoginName
    ) aw ON aw.Kho_ID = h.Kho_ID
    JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID = h.Auto_ID
    WHERE h.Ngay_Nhap_Kho >= @FromDate
      AND h.Ngay_Nhap_Kho <= @ToDate
      AND h.Is_Posted = 1
    ORDER BY h.Ngay_Nhap_Kho, h.So_Phieu_Nhap_Kho, h.Auto_ID, d.Auto_ID
    OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY
)
SELECT ps.__Detail_ID, ps.__Document_ID, ps.Ngay, ps.So_Phieu,
       n.Ten_NCC AS Nha_Cung_Cap, p.Ma_San_Pham, p.Ten_San_Pham,
       ps.So_Luong, ps.Don_Gia,
       CAST(ps.So_Luong * ps.Don_Gia AS DECIMAL(18,2)) AS Tri_Gia
FROM PageScope ps
JOIN dbo.tbl_DM_NCC n ON n.Auto_ID = ps.NCC_ID
JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = ps.San_Pham_ID
ORDER BY ps.Ngay, ps.So_Phieu, ps.__Document_ID, ps.__Detail_ID;
""";

    private const string HistoricalSql = """
DECLARE @IsCurrentReport BIT = IIF(@ToDate = CONVERT(DATE, SYSDATETIME()), 1, 0);

SELECT COUNT_BIG(*) AS Total_Count
FROM dbo.Inventory_Balance_Daily_Scope s
JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
WHERE ku.Ma_Dang_Nhap = @LoginName
  AND s.First_Balance_Date <= @ToDate
  AND EXISTS
  (
      SELECT 1 FROM dbo.Inventory_Balance_Daily b
      WHERE b.Kho_ID = s.Kho_ID AND b.San_Pham_ID = s.San_Pham_ID
        AND b.Balance_Date <= @ToDate AND b.IsValid = 1
  );

;WITH PageKeys AS
(
    SELECT s.Kho_ID, s.San_Pham_ID
    FROM dbo.Inventory_Balance_Daily_Scope s
    JOIN dbo.tbl_DM_Kho_User ku ON ku.Kho_ID = s.Kho_ID
    JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = s.Kho_ID
    JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = s.San_Pham_ID
    WHERE ku.Ma_Dang_Nhap = @LoginName
      AND s.First_Balance_Date <= @ToDate
      AND EXISTS
      (
          SELECT 1 FROM dbo.Inventory_Balance_Daily b
          WHERE b.Kho_ID = s.Kho_ID AND b.San_Pham_ID = s.San_Pham_ID
            AND b.Balance_Date <= @ToDate AND b.IsValid = 1
      )
    ORDER BY k.Ten_Kho, p.Ma_San_Pham
    OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY
)
SELECT s.Kho_ID, k.Ten_Kho, s.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham,
       CAST(COALESCE(opening.ClosingQuantity, 0) AS DECIMAL(18,3)) AS SL_Dau_Ky,
       CAST(ending.CumulativeReceived - ISNULL(opening.CumulativeReceived, 0) AS DECIMAL(18,3)) AS SL_Nhap,
       CAST(ending.CumulativeIssued - ISNULL(opening.CumulativeIssued, 0) AS DECIMAL(18,3)) AS SL_Xuat,
       CAST(ending.ClosingQuantity AS DECIMAL(18,3)) AS SL_Cuoi_Ky,
       CAST(CASE WHEN @IsCurrentReport = 1 THEN ISNULL(cb.CurrentQuantity, 0) ELSE ending.ClosingQuantity END AS DECIMAL(18,3)) AS SL_Ton_Thuc_Te,
       CAST(CASE WHEN @IsCurrentReport = 1 THEN ISNULL(cb.ReservedQuantity, 0) ELSE 0 END AS DECIMAL(18,3)) AS SL_Dang_Giu,
       CAST(CASE WHEN @IsCurrentReport = 1 THEN ISNULL(cb.CurrentQuantity, 0) - ISNULL(cb.ReservedQuantity, 0) ELSE ending.ClosingQuantity END AS DECIMAL(18,3)) AS SL_Kha_Dung
FROM PageKeys s
CROSS APPLY
(
    SELECT TOP (1) b.ClosingQuantity, b.CumulativeReceived, b.CumulativeIssued
    FROM dbo.Inventory_Balance_Daily b
    WHERE b.Kho_ID = s.Kho_ID AND b.San_Pham_ID = s.San_Pham_ID
      AND b.Balance_Date <= @ToDate AND b.IsValid = 1
    ORDER BY b.Balance_Date DESC
) ending
OUTER APPLY
(
    SELECT TOP (1) b.ClosingQuantity, b.CumulativeReceived, b.CumulativeIssued
    FROM dbo.Inventory_Balance_Daily b
    WHERE b.Kho_ID = s.Kho_ID AND b.San_Pham_ID = s.San_Pham_ID
      AND b.Balance_Date < @FromDate AND b.IsValid = 1
    ORDER BY b.Balance_Date DESC
) opening
LEFT JOIN dbo.InventoryBalance_Current cb
  ON cb.Kho_ID = s.Kho_ID AND cb.San_Pham_ID = s.San_Pham_ID
JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = s.Kho_ID
JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = s.San_Pham_ID
ORDER BY k.Ten_Kho, p.Ma_San_Pham;
""";

    private const string CurrentSql = """
SELECT COUNT_BIG(*) AS Total_Count
FROM dbo.InventoryBalance_Current b
JOIN
(
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @LoginName
) ku ON ku.Kho_ID = b.Kho_ID;

SELECT b.Kho_ID, k.Ten_Kho, b.San_Pham_ID, p.Ma_San_Pham, p.Ten_San_Pham,
       CAST(0 AS DECIMAL(18,3)) AS SL_Dau_Ky,
       CAST(0 AS DECIMAL(18,3)) AS SL_Nhap,
       CAST(0 AS DECIMAL(18,3)) AS SL_Xuat,
       b.CurrentQuantity AS SL_Cuoi_Ky,
       b.CurrentQuantity AS SL_Ton_Thuc_Te,
       b.ReservedQuantity AS SL_Dang_Giu,
       CAST(b.CurrentQuantity - b.ReservedQuantity AS DECIMAL(18,3)) AS SL_Kha_Dung
FROM dbo.InventoryBalance_Current b
JOIN
(
    SELECT DISTINCT Kho_ID
    FROM dbo.tbl_DM_Kho_User
    WHERE Ma_Dang_Nhap = @LoginName
) ku ON ku.Kho_ID = b.Kho_ID
JOIN dbo.tbl_DM_Kho k ON k.Auto_ID = b.Kho_ID
JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID = b.San_Pham_ID
ORDER BY b.Kho_ID, b.San_Pham_ID
OFFSET (@Page_Number - 1) * @Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
""";
}
