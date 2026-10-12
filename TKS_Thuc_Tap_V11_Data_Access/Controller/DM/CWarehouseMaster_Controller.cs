using Microsoft.Data.SqlClient;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Warehouse;
using TKS_Thuc_Tap_V11_Data_Access.Utility;

namespace TKS_Thuc_Tap_V11_Data_Access.Controller.Warehouse;

public class CWarehouseMaster_Controller : CWarehouse_Controller_Base
{
    public Task<List<CWarehouseMaster>> List_Master_Async(string p_strMaster_Type)
    {
        string v_strProcedure;
        switch (p_strMaster_Type)
        {
            case "Kho":
                v_strProcedure = "F2009_sp_sel_List_Kho";
                break;

            case "DonViTinh":
                v_strProcedure = "F2016_sp_sel_List_Don_Vi_Tinh";
                break;

            case "LoaiSanPham":
                v_strProcedure = "F2017_sp_sel_List_Loai_San_Pham";
                break;

            case "SanPham":
                v_strProcedure = "F2018_sp_sel_List_San_Pham";
                break;

            case "NCC":
                v_strProcedure = "F2019_sp_sel_List_NCC";
                break;

            default:
                throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type));
        }

        return Task.FromResult(List_From_Procedure<CWarehouseMaster>(v_strProcedure));
    }

    public Task<CWarehousePagedResult<CWarehouseMaster>> List_Master_Page_Async(string p_strMaster_Type, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "")
    {
        return Task.FromResult(Page_From_Procedure<CWarehouseMaster>("sp_DM_Master_Page", p_strMaster_Type, p_iPage_Number, p_iPage_Size, p_strSearch_Text));
    }

    public async Task<HashSet<string>> List_Product_Codes_Async(CancellationToken p_objCancellationToken = default)
    {
        var v_arrProducts = await List_Product_Masters_Async(p_objCancellationToken);
        return v_arrProducts
            .Where(v_objProduct => !string.IsNullOrWhiteSpace(v_objProduct.Code))
            .Select(v_objProduct => v_objProduct.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<CWarehouseMaster>> List_Product_Masters_Async(CancellationToken p_objCancellationToken = default)
    {
        const int v_iPage_Size = 500;
        var v_arrProducts = new List<CWarehouseMaster>();
        var v_iFetched = 0;
        var v_iPage = 1;
        var v_iTotal = 0;

        do
        {
            p_objCancellationToken.ThrowIfCancellationRequested();
            var v_objPage = await List_Master_Page_Async("SanPham", v_iPage, v_iPage_Size);
            v_iTotal = v_objPage.Total_Count;
            v_arrProducts.AddRange(v_objPage.Items);

            v_iFetched += v_objPage.Items.Count;
            v_iPage++;
            if (v_objPage.Items.Count == 0)
                break;
        }
        while (v_iFetched < v_iTotal);

        return v_arrProducts;
    }

    public Task<List<CWarehouseLookup>> List_Lookup_Async(string p_strMaster_Type)
    {
        string v_strProcedure;
        switch (p_strMaster_Type)
        {
            case "Kho":
                v_strProcedure = "F2009_sp_sel_List_Kho_Lookup";
                break;

            case "DonViTinh":
                v_strProcedure = "F2016_sp_sel_List_Don_Vi_Tinh_Lookup";
                break;

            case "LoaiSanPham":
                v_strProcedure = "F2017_sp_sel_List_Loai_San_Pham_Lookup";
                break;

            case "SanPham":
                v_strProcedure = "F2018_sp_sel_List_San_Pham_Lookup";
                break;

            case "NCC":
                v_strProcedure = "F2019_sp_sel_List_NCC_Lookup";
                break;

            default:
                throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type));
        }

        return Task.FromResult(List_From_Procedure<CWarehouseLookup>(v_strProcedure));
    }

    public Task<CWarehousePagedResult<CWarehouseLookup>> List_Lookup_Page_Async(string p_strMaster_Type, int p_iPage_Number, int p_iPage_Size, string p_strSearch_Text = "")
    {
        return Task.FromResult(Page_From_Procedure<CWarehouseLookup>("sp_DM_Lookup_Page", p_strMaster_Type, p_iPage_Number, p_iPage_Size, p_strSearch_Text));
    }

    public Task Save_Master_Async(string p_strMaster_Type, CWarehouseMaster p_objData,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        var v_objCommand = Build_Master_Save_Command(p_strMaster_Type, p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        p_objData.Auto_ID = Scalar_ID(v_objCommand.Procedure, v_objCommand.Values);
        return Task.CompletedTask;
    }

    public async Task Import_Products_Async(
        IReadOnlyList<CProductImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The product import batch cannot be empty.", nameof(p_arrRows));

        await using var v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using var v_objTransaction = v_objConnection.BeginTransaction();

        try
        {
            foreach (var v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                var v_objProduct = new CWarehouseMaster
                {
                    Auto_ID = 0,
                    Code = v_objRow.Ma_San_Pham,
                    Name = v_objRow.Ten_San_Pham,
                    Related_ID = v_objRow.Loai_San_Pham_ID,
                    Related_ID_2 = v_objRow.Don_Vi_Tinh_ID,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };

                try
                {
                    await Save_Master_Async("SanPham", v_objProduct, p_strCurrent_User, p_strCurrent_Function, v_objConnection, v_objTransaction);
                }
                catch (SqlException p_objException)
                {
                    var v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51021 => $"Dòng {v_objRow.Row_Number}: Mã sản phẩm '{v_objRow.Ma_San_Pham}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51023 => $"Dòng {v_objRow.Row_Number}: Loại sản phẩm không còn tồn tại; toàn bộ lô đã được hủy.",
                        51024 => $"Dòng {v_objRow.Row_Number}: Đơn vị tính không còn tồn tại; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu sản phẩm; toàn bộ lô đã được hủy."
                    };
                    throw new InvalidOperationException(v_strMessage, p_objException);
                }
            }

            v_objTransaction.Commit();
        }
        catch
        {
            try
            {
                v_objTransaction.Rollback();
            }
            catch
            {
                // Preserve the original insert or commit failure.
            }

            throw;
        }
    }

    private Task Save_Master_Async(
        string p_strMaster_Type,
        CWarehouseMaster p_objData,
        string p_strLast_Updated_By,
        string p_strLast_Updated_By_Function,
        SqlConnection p_objConnection,
        SqlTransaction p_objTransaction)
    {
        var v_objCommand = Build_Master_Save_Command(p_strMaster_Type, p_objData, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        p_objData.Auto_ID = Scalar_ID(p_objConnection, p_objTransaction, v_objCommand.Procedure, v_objCommand.Values);
        return Task.CompletedTask;
    }

    public async Task Import_Units_Async(
        IReadOnlyList<CUnitImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The unit import batch cannot be empty.", nameof(p_arrRows));

        await using var v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using var v_objTransaction = v_objConnection.BeginTransaction();

        try
        {
            foreach (var v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                var v_objUnit = new CWarehouseMaster
                {
                    Auto_ID = 0,
                    Name = v_objRow.Ten_Don_Vi_Tinh,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };

                try
                {
                    await Save_Master_Async("DonViTinh", v_objUnit, p_strCurrent_User, p_strCurrent_Function, v_objConnection, v_objTransaction);
                }
                catch (SqlException p_objException)
                {
                    var v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51002 => $"Dòng {v_objRow.Row_Number}: Tên đơn vị tính '{v_objRow.Ten_Don_Vi_Tinh}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51001 => $"Dòng {v_objRow.Row_Number}: Tên đơn vị tính không được để trống; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu đơn vị tính; toàn bộ lô đã được hủy."
                    };
                    throw new InvalidOperationException(v_strMessage, p_objException);
                }
            }

            v_objTransaction.Commit();
        }
        catch
        {
            try
            {
                v_objTransaction.Rollback();
            }
            catch
            {
                // Preserve the original insert or commit failure.
            }

            throw;
        }
    }

    public async Task Import_Categories_Async(
        IReadOnlyList<CCategoryImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The category import batch cannot be empty.", nameof(p_arrRows));

        await using var v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using var v_objTransaction = v_objConnection.BeginTransaction();
        try
        {
            foreach (var v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                var v_objCategory = new CWarehouseMaster
                {
                    Auto_ID = 0,
                    Code = v_objRow.Ma_LSP,
                    Name = v_objRow.Ten_LSP,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };
                try
                {
                    await Save_Master_Async("LoaiSanPham", v_objCategory, p_strCurrent_User, p_strCurrent_Function, v_objConnection, v_objTransaction);
                }
                catch (SqlException p_objException)
                {
                    var v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51011 => $"Dòng {v_objRow.Row_Number}: Mã hoặc tên loại sản phẩm '{v_objRow.Ma_LSP} / {v_objRow.Ten_LSP}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51010 => $"Dòng {v_objRow.Row_Number}: Mã loại sản phẩm không được để trống; toàn bộ lô đã được hủy.",
                        51012 => $"Dòng {v_objRow.Row_Number}: Tên loại sản phẩm không được để trống; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu loại sản phẩm; toàn bộ lô đã được hủy."
                    };
                    throw new InvalidOperationException(v_strMessage, p_objException);
                }
            }
            v_objTransaction.Commit();
        }
        catch
        {
            try
            {
                v_objTransaction.Rollback();
            }
            catch
            {
                // Preserve the original insert or commit failure.
            }
            throw;
        }
    }

    public async Task Import_Suppliers_Async(
        IReadOnlyList<CSupplierImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The supplier import batch cannot be empty.", nameof(p_arrRows));

        await using var v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using var v_objTransaction = v_objConnection.BeginTransaction();
        try
        {
            foreach (var v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                var v_objSupplier = new CWarehouseMaster
                {
                    Auto_ID = 0,
                    Code = v_objRow.Ma_NCC,
                    Name = v_objRow.Ten_NCC,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };
                try
                {
                    await Save_Master_Async("NCC", v_objSupplier, p_strCurrent_User, p_strCurrent_Function, v_objConnection, v_objTransaction);
                }
                catch (SqlException p_objException)
                {
                    var v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51031 => $"Dòng {v_objRow.Row_Number}: Mã hoặc tên nhà cung cấp '{v_objRow.Ma_NCC} / {v_objRow.Ten_NCC}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51030 => $"Dòng {v_objRow.Row_Number}: Mã nhà cung cấp không được để trống; toàn bộ lô đã được hủy.",
                        51032 => $"Dòng {v_objRow.Row_Number}: Tên nhà cung cấp không được để trống; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu nhà cung cấp; toàn bộ lô đã được hủy."
                    };
                    throw new InvalidOperationException(v_strMessage, p_objException);
                }
            }
            v_objTransaction.Commit();
        }
        catch
        {
            try
            {
                v_objTransaction.Rollback();
            }
            catch
            {
                // Preserve the original insert or commit failure.
            }
            throw;
        }
    }

    public async Task Import_Warehouses_Async(
        IReadOnlyList<CWarehouseImportRow> p_arrRows,
        string p_strCurrent_User,
        string p_strCurrent_Function,
        CancellationToken p_objCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(p_arrRows);
        if (p_arrRows.Count == 0)
            throw new ArgumentException("The warehouse import batch cannot be empty.", nameof(p_arrRows));

        await using var v_objConnection = new SqlConnection(CConfig.TKS_Thuc_Tap_V11_Conn_String);
        await v_objConnection.OpenAsync(p_objCancellationToken);
        using var v_objTransaction = v_objConnection.BeginTransaction();
        try
        {
            foreach (var v_objRow in p_arrRows)
            {
                p_objCancellationToken.ThrowIfCancellationRequested();
                var v_objWarehouse = new CWarehouseMaster
                {
                    Auto_ID = 0,
                    Name = v_objRow.Ten_Kho,
                    Ghi_Chu = v_objRow.Ghi_Chu
                };
                try
                {
                    await Save_Master_Async("Kho", v_objWarehouse, p_strCurrent_User, p_strCurrent_Function, v_objConnection, v_objTransaction);
                }
                catch (SqlException p_objException)
                {
                    var v_strMessage = p_objException.Number switch
                    {
                        2601 or 2627 or 51041 => $"Dòng {v_objRow.Row_Number}: Tên kho '{v_objRow.Ten_Kho}' đã tồn tại; toàn bộ lô đã được hủy.",
                        51040 => $"Dòng {v_objRow.Row_Number}: Tên kho không được để trống; toàn bộ lô đã được hủy.",
                        _ => $"Dòng {v_objRow.Row_Number}: Không thể lưu kho; toàn bộ lô đã được hủy."
                    };
                    throw new InvalidOperationException(v_strMessage, p_objException);
                }
            }
            v_objTransaction.Commit();
        }
        catch
        {
            try
            {
                v_objTransaction.Rollback();
            }
            catch
            {
                // Preserve the original insert or commit failure.
            }
            throw;
        }
    }

    private static (string Procedure, object[] Values) Build_Master_Save_Command(
        string p_strMaster_Type,
        CWarehouseMaster p_objData,
        string p_strLast_Updated_By,
        string p_strLast_Updated_By_Function)
    {
        p_objData.Last_Updated_By = p_strLast_Updated_By;
        p_objData.Last_Updated_By_Function = p_strLast_Updated_By_Function;
        p_objData.Created_By = p_strLast_Updated_By;
        p_objData.Created_By_Function = p_strLast_Updated_By_Function;

        var v_bIsCreate = p_objData.Auto_ID == 0;
        var v_strProcedure = p_strMaster_Type switch
        {
            "DonViTinh" => v_bIsCreate ? "F2016_sp_ins_Don_Vi_Tinh" : "F2016_sp_upd_Don_Vi_Tinh",
            "LoaiSanPham" => v_bIsCreate ? "F2017_sp_ins_Loai_San_Pham" : "F2017_sp_upd_Loai_San_Pham",
            "SanPham" => v_bIsCreate ? "F2018_sp_ins_San_Pham" : "F2018_sp_upd_San_Pham",
            "NCC" => v_bIsCreate ? "F2019_sp_ins_NCC" : "F2019_sp_upd_NCC",
            "Kho" => v_bIsCreate ? "F2009_sp_ins_Kho" : "F2009_sp_upd_Kho",
            _ => throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type))
        };

        var v_arrValues = p_strMaster_Type switch
        {
            "DonViTinh" => new object[] { p_objData.Auto_ID, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            "LoaiSanPham" => new object[] { p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            "SanPham" => new object[] { p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Related_ID, p_objData.Related_ID_2, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            "NCC" => new object[] { p_objData.Auto_ID, p_objData.Code, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            "Kho" => new object[] { p_objData.Auto_ID, p_objData.Name, p_objData.Ghi_Chu, p_objData.Created_By, p_objData.Created_By_Function, p_objData.Last_Updated_By, p_objData.Last_Updated_By_Function },
            _ => throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type))
        };

        return (v_strProcedure, v_arrValues);
    }

    public Task Delete_Master_Async(string p_strMaster_Type, long p_iAuto_ID,
        string p_strLast_Updated_By = "", string p_strLast_Updated_By_Function = "")
    {
        string v_strProcedure;
        switch (p_strMaster_Type)
        {
            case "Kho":
                v_strProcedure = "F2009_sp_del_Kho";
                break;

            case "DonViTinh":
                v_strProcedure = "F2016_sp_del_Don_Vi_Tinh";
                break;

            case "LoaiSanPham":
                v_strProcedure = "F2017_sp_del_Loai_San_Pham";
                break;

            case "SanPham":
                v_strProcedure = "F2018_sp_del_San_Pham";
                break;

            case "NCC":
                v_strProcedure = "F2019_sp_del_NCC";
                break;

            default:
                throw new ArgumentException("Loại danh mục không hợp lệ.", nameof(p_strMaster_Type));
        }

        Execute_Procedure(v_strProcedure, p_iAuto_ID, p_strLast_Updated_By, p_strLast_Updated_By_Function);
        return Task.CompletedTask;
    }
}
