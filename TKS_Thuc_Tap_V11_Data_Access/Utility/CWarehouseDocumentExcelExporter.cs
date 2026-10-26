using ClosedXML.Excel;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;

namespace TKS_Thuc_Tap_V11_Data_Access.Utility;
public static class CWarehouseDocumentExcelExporter
{
    public static List<CWarehouseDocumentExcelExportRow> BuildRows(IEnumerable<CWarehouseDocument> p_arrDocuments, Func<long, IReadOnlyCollection<CWarehouseDocumentDetail>> p_funcGetDetails)
    {
        ArgumentNullException.ThrowIfNull(p_arrDocuments);
        ArgumentNullException.ThrowIfNull(p_funcGetDetails);
        var v_arrRows = new List<CWarehouseDocumentExcelExportRow>();
        foreach (var v_objDocument in p_arrDocuments)
        {
            var v_arrDetails = p_funcGetDetails(v_objDocument.Auto_ID) ?? Array.Empty<CWarehouseDocumentDetail>();
            if (v_arrDetails.Count == 0)
            {
                v_arrRows.Add(CreateRow(v_objDocument, null));
                continue;
            }

            foreach (CWarehouseDocumentDetail v_objDetail in v_arrDetails)
            {
                v_arrRows.Add(CreateRow(v_objDocument, v_objDetail));
            }
        }

        return v_arrRows;
    }

    public static byte[] CreateWorkbook(IEnumerable<CWarehouseDocumentExcelExportRow> p_arrRows, bool isReceipt)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        var v_arrRows = p_arrRows.ToList();
        string[] v_arrHeaders;
        if (isReceipt)
        {
            v_arrHeaders = new[]
        {
            "Ngày",
            "Số phiếu",
            "Kho",
            "NCC",
            "Trạng thái",
            "Ghi chú",
            "Mã SP",
            "Tên sản phẩm",
            "ĐVT",
            "SL nhập",
            "Đơn giá",
            "Trị giá"
        };

        }
        else
        {
            v_arrHeaders = new[]
        {
            "Ngày",
            "Số phiếu",
            "Kho",
            "Trạng thái",
            "Ghi chú",
            "Mã SP",
            "Tên sản phẩm",
            "ĐVT",
            "SL xuất",
            "Đơn giá",
            "Trị giá"
        };
        }
        using var v_objWorkbook = new XLWorkbook();
        string v_strSheet_Name = "PhieuXuat";
        if (isReceipt) { v_strSheet_Name = "PhieuNhap"; }
        var v_objWorksheet = v_objWorkbook.Worksheets.Add(v_strSheet_Name);
        v_objWorksheet.Style.Font.FontName = "Calibri";
        for (var v_iColumn = 0; v_iColumn < v_arrHeaders.Length; v_iColumn++)
        {
            v_objWorksheet.Cell(1, v_iColumn + 1).Value = v_arrHeaders[v_iColumn];
        }

        for (var v_iRow = 0; v_iRow < v_arrRows.Count; v_iRow++)
        {
            var v_objRow = v_arrRows[v_iRow];
            var v_iExcelRow = v_iRow + 2;
            var v_iColumn = 1;
            v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = v_objRow.Ngay_Chung_Tu;
            v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = v_objRow.So_Phieu;
            v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = v_objRow.Ten_Kho;
            if (isReceipt)
            {
                v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = v_objRow.Ten_NCC;
            }

            if (v_objRow.Is_Posted)
            {
                v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = "Đã Post";
            }
            else
            {
                v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = "Nháp";
            }

            v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = v_objRow.Ghi_Chu;
            v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = v_objRow.Ma_San_Pham;
            v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = v_objRow.Ten_San_Pham;
            v_objWorksheet.Cell(v_iExcelRow, v_iColumn++).Value = v_objRow.Ten_Don_Vi_Tinh;
            if (v_objRow.So_Luong.HasValue)
            {
                v_objWorksheet.Cell(v_iExcelRow, v_iColumn).Value = v_objRow.So_Luong.Value;
            }

            v_iColumn++;
            if (v_objRow.Don_Gia.HasValue)
            {
                v_objWorksheet.Cell(v_iExcelRow, v_iColumn).Value = v_objRow.Don_Gia.Value;
            }

            v_iColumn++;
            if (v_objRow.Tri_Gia.HasValue)
            {
                v_objWorksheet.Cell(v_iExcelRow, v_iColumn).Value = v_objRow.Tri_Gia.Value;
            }
        }

        var v_iLastRow = Math.Max(1, v_arrRows.Count + 1);
        var v_iLastColumn = v_arrHeaders.Length;
        var v_objExportRange = v_objWorksheet.Range(1, 1, v_iLastRow, v_iLastColumn);
        v_objExportRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        v_objWorksheet.Range(1, 1, 1, v_iLastColumn).Style.Font.Bold = true;
        v_objWorksheet.Range(1, 1, 1, v_iLastColumn).Style.Fill.BackgroundColor = XLColor.LightGray;
        v_objWorksheet.Range(1, 1, v_iLastRow, v_iLastColumn).SetAutoFilter();
        v_objWorksheet.SheetView.FreezeRows(1);
        v_objWorksheet.Column(1).Width = 22;
        v_objWorksheet.Column(1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm:ss";
        v_objWorksheet.Column(2).Width = 18;
        v_objWorksheet.Column(3).Width = 24;
        int v_iStatusColumn;
        if (isReceipt)
        {
            v_iStatusColumn = 5;
        }
        else
        {
            v_iStatusColumn = 4;
        }

        int v_iNoteColumn;
        if (isReceipt)
        {
            v_iNoteColumn = 6;
        }
        else
        {
            v_iNoteColumn = 5;
        }

        int v_iProductCodeColumn;
        if (isReceipt)
        {
            v_iProductCodeColumn = 7;
        }
        else
        {
            v_iProductCodeColumn = 6;
        }

        int v_iProductNameColumn;
        if (isReceipt)
        {
            v_iProductNameColumn = 8;
        }
        else
        {
            v_iProductNameColumn = 7;
        }

        int v_iUnitColumn;
        if (isReceipt)
        {
            v_iUnitColumn = 9;
        }
        else
        {
            v_iUnitColumn = 8;
        }

        int v_iQuantityColumn;
        if (isReceipt)
        {
            v_iQuantityColumn = 10;
        }
        else
        {
            v_iQuantityColumn = 9;
        }

        var v_iPriceColumn = v_iQuantityColumn + 1;
        var v_iValueColumn = v_iQuantityColumn + 2;
        if (isReceipt)
        {
            v_objWorksheet.Column(4).Width = 30;
        }

        v_objWorksheet.Column(v_iStatusColumn).Width = 14;
        v_objWorksheet.Column(v_iNoteColumn).Width = 32;
        v_objWorksheet.Column(v_iProductCodeColumn).Width = 16;
        v_objWorksheet.Column(v_iProductNameColumn).Width = 32;
        v_objWorksheet.Column(v_iUnitColumn).Width = 15;
        v_objWorksheet.Column(v_iQuantityColumn).Width = 16;
        v_objWorksheet.Column(v_iPriceColumn).Width = 18;
        v_objWorksheet.Column(v_iValueColumn).Width = 18;
        v_objWorksheet.Column(v_iQuantityColumn).Style.NumberFormat.Format = "#,##0.####";
        v_objWorksheet.Column(v_iPriceColumn).Style.NumberFormat.Format = "#,##0.####";
        v_objWorksheet.Column(v_iValueColumn).Style.NumberFormat.Format = "#,##0.####";
        using var v_objStream = new MemoryStream();
        v_objWorkbook.SaveAs(v_objStream);
        return v_objStream.ToArray();
    }

    private static CWarehouseDocumentExcelExportRow CreateRow(CWarehouseDocument p_objDocument, CWarehouseDocumentDetail? p_objDetail)
    {
        return new CWarehouseDocumentExcelExportRow
        {
            Ngay_Chung_Tu = p_objDocument.Ngay_Chung_Tu,
            So_Phieu = p_objDocument.So_Phieu,
            Ten_Kho = p_objDocument.Ten_Kho,
            Ten_NCC = p_objDocument.Ten_NCC,
            Is_Posted = p_objDocument.Is_Posted,
            Ghi_Chu = p_objDocument.Ghi_Chu,
            Ma_San_Pham = p_objDetail?.Ma_San_Pham ?? "",
            Ten_San_Pham = p_objDetail?.Ten_San_Pham ?? "",
            Ten_Don_Vi_Tinh = p_objDetail?.Ten_Don_Vi_Tinh ?? "",
            So_Luong = p_objDetail?.So_Luong,
            Don_Gia = p_objDetail?.Don_Gia,
            Tri_Gia = p_objDetail?.Tri_Gia
        };
    }
}

public sealed class CWarehouseDocumentExcelExportRow
{
    public DateTime Ngay_Chung_Tu { get; init; }
    public string So_Phieu { get; init; } = "";
    public string Ten_Kho { get; init; } = "";
    public string Ten_NCC { get; init; } = "";
    public bool Is_Posted { get; init; }
    public string Ghi_Chu { get; init; } = "";
    public string Ma_San_Pham { get; init; } = "";
    public string Ten_San_Pham { get; init; } = "";
    public string Ten_Don_Vi_Tinh { get; init; } = "";
    public decimal? So_Luong { get; init; }
    public decimal? Don_Gia { get; init; }
    public decimal? Tri_Gia { get; init; }
}
