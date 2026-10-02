using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Specialized;
using System.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using System.Collections.ObjectModel;
using System.Reflection;
using Telerik.Blazor.Components;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Collections;
using System.Runtime.InteropServices;
using System.Net;
using Newtonsoft.Json;
//using iTextSharp.text;
using System.Formats.Asn1;
using System.Security.Permissions;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.ComponentModel;
using System.Linq;
using Telerik.Blazor;
using System.Runtime.CompilerServices;
using Telerik.DataSource;
using System.Runtime.Versioning;
using TKS_Thuc_Tap_V11_Data_Access.Utility;
using TKS_Thuc_Tap_V11_Data_Access.Entity.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Cache;
using TKS_Thuc_Tap_V11_Data_Access.Controller.Sys;
using TKS_Thuc_Tap_V11_Data_Access.Entity.API;

namespace TKS_Thuc_Tap_V11_Web_Common.Common
{
    [SupportedOSPlatform("windows")]
    public class CCommonFunction
    {
        public static string Get_QueryString(NavigationManager p_objNav_Manager, string p_strPara_Name)
        {
            string v_strRes = "";

            var v_uri = p_objNav_Manager.ToAbsoluteUri(p_objNav_Manager.Uri);
            if (QueryHelpers.ParseQuery(v_uri.Query).TryGetValue(p_strPara_Name, out var v_param))
                v_strRes = v_param.First();

            return v_strRes;
        }
        public static List<string> Get_All_Columns<T>(TelerikGrid<T> p_grid)
        {
            List<string> v_arrRes = new List<string>();
            var v_State = p_grid.GetState();
            var v_arrCol = v_State.ColumnStates;
            //var columnsState2 = p_grid.GetState();
            foreach (var v_objCol in v_arrCol)
            {
                if (CUtility.Convert_To_String(v_objCol.Field) != "")
                    v_arrRes.Add(v_objCol.Field);
            }
            return v_arrRes;
        }

        public static List<T> SearchHandler<T>(List<T> p_arrObjectPar, TelerikGrid<T> p_grid, string p_strKey_Tim_Kiem, string p_strSep = " ")
        {
            List<T> v_arrRes = new List<T>();

            if (p_arrObjectPar.Count > 0)
            {
                //Step 1
                // Lấy type của object
                Type v_type = p_arrObjectPar.FirstOrDefault().GetType();

                // Lấy tất cả thuộc của object
                PropertyInfo[] v_arrPropertyInfos = v_type.GetProperties();

                //Step 2
                //Xử lý key tìm kiếm (tách ra thành nhiều phần tử theo "p_strSep")
                p_strKey_Tim_Kiem = p_strKey_Tim_Kiem.Trim().Replace("  ", " ");
                string[] v_arrKey_Tim_Kiem = p_strKey_Tim_Kiem.Split(p_strSep);

                //Step 3
                //Lấy những column mà grid đang hiển thị
                List<string> v_arrAll_Columns = Get_All_Columns<T>(p_grid);

                List<T> v_arrTemp; // bảng tạm
                int v_iCount = p_arrObjectPar.Count();

                //var asSpan = CollectionsMarshal.AsSpan(p_arrObjectPar);

                //Step 4
                //Duyệt các phần tử trong key tìm kiếm ở Step 3
                //Với mỗi thuộc tính của object sẽ duyệt và lấy ra object có data tương ứng đồng thời lưu vào bảng tạm
                //Sau mỗi vòng lặp trong key tìm kiếm thì cập nhật lại grid mới với data là grid tạm đã xử lý
                if (v_arrKey_Tim_Kiem.Length > 0 && v_arrKey_Tim_Kiem[0].Trim() != "")
                {
                    foreach (string v_item in v_arrKey_Tim_Kiem)
                    {
                        v_arrTemp = new List<T>();
                        for (int v_iIndex = 0; v_iIndex < v_iCount; v_iIndex++)
                        {
                            var v_obj = p_arrObjectPar[v_iIndex];
                            foreach (var v_prop in v_arrPropertyInfos)
                            {
                                //chỉ xét những field đang hiển thị trên grid
                                if (v_arrAll_Columns.Contains(v_prop.Name))
                                {
                                    //Lấy giá trị ngay tại field đó
                                    bool v_bIsContains = CUtility.Convert_To_String(v_prop.GetValue(v_obj)).ToUpper().Contains(v_item.ToUpper());

                                    //Bước này phải kiểm tra xem object đó đã tồn tại trong bảng tạm hay chưa để tránh object bị thêm nhiều lần
                                    if (v_bIsContains && !v_arrTemp.Contains(v_obj))
                                    {
                                        v_arrTemp.Add(v_obj);
                                        break;
                                    }
                                }
                            }
                        }
                        //cập nhật lại grid kết quả
                        p_arrObjectPar = v_arrTemp;
                        //giảm vòng lặp
                        v_iCount = p_arrObjectPar.Count;
                    }
                }

                //Step 5
                //Lưu lại kết quả
                v_arrRes = p_arrObjectPar;
            }
            return v_arrRes;
        }

        public static void Format_Grid<T>(TelerikGrid<T> p_grid, string p_strLanguage, string p_strFunc_Code)
        {
            var v_objState = p_grid.GetState(); // giữ state để sau khi thay đổi thì set lại

            var v_arrColState = v_objState.ColumnStates;// lấy tất các column hiện có của Grid

            var v_arrGrid_Props = p_grid.GetType().GetProperties(); //lấy các thuộc tính của grid

            //set FilterMode cho grid
            v_arrGrid_Props.FirstOrDefault(x => x.Name == "FilterMode").SetValue(p_grid, GridFilterMode.FilterRow);
            v_arrGrid_Props.FirstOrDefault(x => x.Name == "PageSize").SetValue(p_grid, CConfig.Page_Size);
            v_arrGrid_Props.FirstOrDefault(x => x.Name == "Pageable").SetValue(p_grid, true);
            v_arrGrid_Props.FirstOrDefault(x => x.Name == "Reorderable").SetValue(p_grid, true);
            v_arrGrid_Props.FirstOrDefault(x => x.Name == "Resizable").SetValue(p_grid, true);
            v_arrGrid_Props.FirstOrDefault(x => x.Name == "Sortable").SetValue(p_grid, true);
            v_arrGrid_Props.FirstOrDefault(x => x.Name == "SelectionMode").SetValue(p_grid, GridSelectionMode.Multiple);

            int v_iSTT_Col = 0;

            foreach (var v_objCol in v_arrColState)
            {
                //Set Title cho Grid
                var v_arrProps = v_objCol.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).ToList();//get các object ẩn
                if (v_arrProps.Count > 0)
                {
                    var v_prop = v_arrProps.FirstOrDefault(x => x.Name == "Column"); //lấy object Column
                    if (v_prop != null)
                    {
                        var v_data = v_prop.GetValue(v_objCol) as Telerik.Blazor.Common.Columns.IColumn;
                        //data.Title = "Title Test"; //set Title cho Column của Grid

                        //ẩn các filter icon
                        var v_blnIsShowFilterCellButton = v_data.GetType().GetProperties().FirstOrDefault(x => x.Name == "ShowFilterCellButtons");
                        if (v_blnIsShowFilterCellButton != null)
                        {
                            object v_objNew_Value_Data = false;
                            v_blnIsShowFilterCellButton.SetValue(v_data, v_objNew_Value_Data);
                        }

                        var v_bCheck_Box_Only_Select = v_data.GetType().GetProperties().FirstOrDefault(x => x.Name == "CheckBoxOnlySelection");
                        if (v_bCheck_Box_Only_Select != null)
                        {
                            object v_objNew_Value_Data = true;
                            v_bCheck_Box_Only_Select.SetValue(v_data, v_objNew_Value_Data);
                        }

                        if (CUtility.Convert_To_String(v_objCol.Field) != "")
                        {
                            v_iSTT_Col++;

                            var v_objDataType = v_data.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(x => x.Name == "PropertyType").GetValue(v_data); //lấy kiểu dữ liệu của column
                            string v_strType_Name = v_objDataType.ToString(); // lấy tên của kiểu dữ liệu ("System.String", "System.Int32", ...)

                            // Xử lý format
                            if (v_strType_Name.ToLower() == "system.datetime")
                            {
                                if (v_objCol.Field.ToLower().StartsWith("ngay_gio") || v_objCol.Field.ToLower() == "created" || v_objCol.Field.ToLower() == "last_updated")
                                    v_data.GetType().GetProperties().FirstOrDefault(x => x.Name == "DisplayFormat").SetValue(v_data, "{0: " + CConfig.FullTime_Format_String + "}");
                                else
                                    v_data.GetType().GetProperties().FirstOrDefault(x => x.Name == "DisplayFormat").SetValue(v_data, "{0: " + CConfig.Date_Format_String + "}");
                            }

                            if (v_strType_Name.ToLower() == "system.int32" || v_strType_Name.ToLower() == "system.int64" || v_strType_Name.ToLower() == "system.double")
                            {
                                if (v_objCol.Field.ToLower() != "auto_id")
                                {
                                    v_data.GetType().GetProperties().FirstOrDefault(x => x.Name == "DisplayFormat").SetValue(v_data, "{0: " + CConfig.Number_Format_String + "}");
                                    v_data.GetType().GetProperties().FirstOrDefault(x => x.Name == "TextAlign").SetValue(v_data, ColumnTextAlign.Right);
                                }
                            }

                            // Setup multilanguage
                            string v_strTitle = CCache_Language.Get_String_Label_By_Field(v_data.Title, p_strLanguage);
                            if (v_strTitle != v_data.Title)
                                v_data.Title = v_strTitle;

                            // Setup hiện / ẩn column
                            // Lấy tất cả
                            CSys_Hien_An_Column v_objH1 = CCache_Hien_An_Column.Get_Data_By_Code(-5, v_objCol.Field, "");
                            if (v_objH1 != null)
                            {
                                if (v_objH1.Option_ID == (int)EHien_An_Option_ID.Hide)
                                    v_data.Visible = false;
                                else
                                    v_data.Visible = true;
                            }

                            // Lấy theo chức năng
                            v_objH1 = CCache_Hien_An_Column.Get_Data_By_Code(-5, v_objCol.Field, p_strFunc_Code);
                            if (v_objH1 != null)
                            {
                                if (v_objH1.Option_ID == (int)EHien_An_Option_ID.Hide)
                                    v_data.Visible = false;
                                else
                                    v_data.Visible = true;
                            }

                            // Setup độ rộng cột
                            CSys_Column_Width v_objCol_Width = CCache_Column_Width.Get_Data_By_Code(v_objCol.Field);
                            if (v_objCol_Width != null)
                                v_objCol.Width = v_objCol_Width.Do_Rong + "px";

                            // Frozen column
                            CSys_Frozen_Column v_objFrozen = CCache_Frozen_Column.Get_Data_By_Code(p_strFunc_Code);
                            if (v_objFrozen != null)
                            {
                                if (v_objFrozen.SL_Cot_Frozen == v_iSTT_Col)
                                {
                                    v_objCol.Locked = true;
                                }
                            }
                        }
                        else
                        {
                            if (v_objCol.Locked == true)
                            {
                                var v_objCellRender = v_data.GetType().GetProperties().FirstOrDefault(x => x.Name == "OnCellRender");
                                v_objCellRender.SetValue(v_data, (GridCellRenderEventArgs args) => { OnCellRenderHandler(args); });
                            }
                        }
                    }
                }
            }

            p_grid.SetStateAsync(v_objState); //sau khi thay đổi trong for thì phải SetState để thay đổi Grid
        }

        private static void OnCellRenderHandler(GridCellRenderEventArgs p_args)
        {
            p_args.Class = "myCustomCellFormatting";
        }
        public static string Set_Error_MessageBox(string p_strMessage)
        {
            StringBuilder v_sb = new StringBuilder();

            v_sb.AppendLine("<div class=\"alert alert-danger alert-styled-left\">");
            v_sb.AppendLine("<span>" + p_strMessage + "</span>");
            v_sb.AppendLine("</div>");

            return v_sb.ToString();
        }

        public static string Set_Success_MessageBox(string p_strMessage)
        {
            StringBuilder v_sb = new StringBuilder();

            v_sb.AppendLine("<div class=\"alert alert-success alert-styled-left\">");
            v_sb.AppendLine("<span>" + p_strMessage + "</span>");
            v_sb.AppendLine("</div>");

            return v_sb.ToString();
        }

        public static byte[] Resize_Dung_Luong_Image(byte[] p_byteArrayIn)
        {
            MemoryStream v_objMS = new MemoryStream(p_byteArrayIn, 0, p_byteArrayIn.Length);
            v_objMS.Write(p_byteArrayIn, 0, p_byteArrayIn.Length);
            System.Drawing.Image v_objImage = System.Drawing.Image.FromStream(v_objMS, true);//Exception occurs here

            // lấy chiều rộng và chiều cao ban đầu của ảnh
            int v_iOriginalWidth = v_objImage.Width;
            int v_iOriginalHeight = v_objImage.Height;

            float v_fTy_Le_Width_Height = (float)v_iOriginalWidth / v_iOriginalHeight;

            if (v_iOriginalWidth > 800)
            {
                v_iOriginalWidth = 800;
                v_iOriginalHeight = (int)Math.Round(v_iOriginalWidth / v_fTy_Le_Width_Height);
            }

            Bitmap v_objBitmap = Resize_And_Crop_Image((Bitmap)v_objImage, new Size(v_iOriginalWidth, v_iOriginalHeight));

            MemoryStream v_objStream = new MemoryStream();
            v_objBitmap.Save(v_objStream, ImageFormat.Jpeg);
            byte[] v_arrByte = v_objStream.ToArray();

            // trả về byte[] sau khi đã resize
            return v_arrByte;
        }

        public static Bitmap Resize_And_Crop_Image(Bitmap imgToResize, Size destinationSize)
        {
            var v_iOriginalWidth = imgToResize.Width;
            var v_iOriginalHeight = imgToResize.Height;

            //how many units are there to make the original length
            var v_fHeightRatio = (float)v_iOriginalHeight / destinationSize.Height;
            var v_fWidthRatio = (float)v_iOriginalWidth / destinationSize.Width;

            //get the shorter side
            var v_fRatio = Math.Min(v_fHeightRatio, v_fWidthRatio);

            var v_iHScale = Convert.ToInt32(destinationSize.Height * v_fRatio);
            var v_iWScale = Convert.ToInt32(destinationSize.Width * v_fRatio);

            //start cropping from the center
            var v_iStartX = (v_iOriginalWidth - v_iWScale) / 2;
            var v_iStartY = (v_iOriginalHeight - v_iHScale) / 2;

            //crop the image from the specified location and size
            var v_sourceRectangle = new Rectangle(v_iStartX, v_iStartY, v_iWScale, v_iHScale);

            //the future size of the image
            var v_bitmap = new Bitmap(destinationSize.Width, destinationSize.Height);

            //fill-in the whole bitmap
            var v_destinationRectangle = new Rectangle(0, 0, v_bitmap.Width, v_bitmap.Height);

            //generate the new image
            using (var v_graphics = Graphics.FromImage(v_bitmap))
            {
                v_graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                v_graphics.DrawImage(imgToResize, v_destinationRectangle, v_sourceRectangle, GraphicsUnit.Pixel);
            }

            return v_bitmap;

        }

        public static byte[] Resize_Dung_Luong_Image_Thumbnail(byte[] p_byteArrayIn)
        {
            MemoryStream v_objMS = new MemoryStream(p_byteArrayIn, 0, p_byteArrayIn.Length);
            v_objMS.Write(p_byteArrayIn, 0, p_byteArrayIn.Length);
            System.Drawing.Image v_objImage = System.Drawing.Image.FromStream(v_objMS, true);//Exception occurs here

            // lấy chiều rộng và chiều cao ban đầu của ảnh
            int v_iOriginalWidth = v_objImage.Width;
            int v_iOriginalHeight = v_objImage.Height;

            float v_fTy_Le_Width_Height = (float)v_iOriginalWidth / v_iOriginalHeight;

            if (v_iOriginalWidth > 150)
            {
                v_iOriginalWidth = 150;
                v_iOriginalHeight = (int)Math.Round(v_iOriginalWidth / v_fTy_Le_Width_Height);
            }

            Bitmap v_objBitmap = Resize_And_Crop_Image((Bitmap)v_objImage, new Size(v_iOriginalWidth, v_iOriginalHeight));

            MemoryStream v_objStream = new MemoryStream();
            v_objBitmap.Save(v_objStream, ImageFormat.Jpeg);
            byte[] v_arrByte = v_objStream.ToArray();

            // trả về byte[] sau khi đã resize
            return v_arrByte;
        }

        public static string Get_Image_Folder_URL(string p_strFolderName)
        {
            return "FileManagement/Upload_File/" + p_strFolderName + "/";
        }

        public static void RedirectLink(NavigationManager p_objNav_Manager, string p_strUrl)
        {
            p_objNav_Manager.NavigateTo(p_strUrl.Replace("~", "").Replace(".aspx", ""));
        }

        public static void ShowMessage_And_Redirect(IJSRuntime p_jsRuntime, NavigationManager p_objNav_Manager, string p_strMessage, string p_strURL)
        {
            p_jsRuntime.InvokeVoidAsync("alert", p_strMessage); // Alert
            CCommonFunction.RedirectLink(p_objNav_Manager, p_strURL);
        }

        public static string Save_Upload_File(List<IBrowserFile> loadedFiles, string p_strFile_Name_Full)
        {

            var v_objFile = loadedFiles[0];

            string v_strTextExl = Path.GetExtension(p_strFile_Name_Full).ToLower();
            string v_strFile_Name = p_strFile_Name_Full.Substring(0, p_strFile_Name_Full.Length - v_strTextExl.Length);
            string v_strFolder = CConfig.Upload_URL + DateTime.Now.ToString("yyyyMMdd") + "\\";
            if (Directory.Exists(v_strFolder) == false)
                Directory.CreateDirectory(v_strFolder);

            var v_strPath_File_New = v_strFolder + v_strFile_Name + "_" + DateTime.Now.ToString("ddMMyyHHmmss") + v_strTextExl;
            string v_strURL_Save = v_strPath_File_New.Substring(v_strPath_File_New.IndexOf("FileManagement"));

            //var v_objToken = new CancellationTokenSource();
            //await using FileStream fs = new FileStream(v_strPath_File_New, FileMode.Create, FileAccess.Write);

            //code cũ không lưu được file
            //await v_objFile.Stream.CopyToAsync(fs, v_objToken.Token);
            //using FileStream fs = new(v_strPath_File_New, FileMode.Create);
            //v_objFile.OpenReadStream(1024000).CopyToAsync(fs);
            //fs.Close();

            //code mới
            Task.Run(async () =>
            {
                await using FileStream v_fs = new(v_strPath_File_New, FileMode.Create);
                await v_objFile.OpenReadStream(1024000).CopyToAsync(v_fs);
            });

            return v_strURL_Save;
        }

        public static string Check_And_Get_File(List<IBrowserFile> loadedFiles)
        {
            string v_strRes = "";

            if (loadedFiles.Count > 0)
            {
                IBrowserFile v_objFile = loadedFiles[0];

                string v_strTextExl = Path.GetExtension(v_objFile.Name).ToLower();
                long v_longSize = v_objFile.Size;

                if (CUtility.Check_File_Type_Accept_Upload(v_strTextExl))
                    throw new Exception("File extension is not valid.");

                string v_strFile_Name_Full = Path.GetFileName(v_objFile.Name);
                string v_strFile_Name = v_strFile_Name_Full.Substring(0, v_strFile_Name_Full.Length - v_strTextExl.Length);

                v_strRes = CUtility.Fix_Handler_File_Name(v_strFile_Name_Full);
            }

            return v_strRes;
        }

        public static List<CSys_Chuc_Nang> List_Chuc_Nang_By_User(string p_strUser)
        {
            // Lấy toàn bộ cây chức năng
            List<CSys_Chuc_Nang> v_arrRes = CCache_Chuc_Nang.Clone_Data_By_Nhom_Chuc_Nang_ID((int)ENhom_Chuc_Nang_ID.Quan_Tri).OrderBy(it => it.Sort_Priority).ToList();

            // Chức năng định nghĩa khả năng hỗ trợ; quyền người dùng phải bắt đầu từ deny.
            foreach (CSys_Chuc_Nang v_objRes in v_arrRes)
            {
                v_objRes.Is_Have_Add_Permission = false;
                v_objRes.Is_Have_Edit_Permission = false;
                v_objRes.Is_Have_Delete_Permission = false;
                v_objRes.Is_Have_View_Permission = false;
                v_objRes.Is_Have_Export_Permission = false;
            }

            // Lấy danh sách nhóm quyền
            List<CSys_Nhom_Thanh_Vien_User> v_arrNhom_TV = CCache_Nhom_Thanh_Vien_User.List_Data_By_User(p_strUser);

            // List danh sách quyền của nhóm quyền map vô list
            Dictionary<long, CSys_Phan_Quyen_Chuc_Nang> v_dicAll = new Dictionary<long, CSys_Phan_Quyen_Chuc_Nang>();

            foreach (CSys_Nhom_Thanh_Vien_User v_objNhom_TV in v_arrNhom_TV)
            {
                List<CSys_Phan_Quyen_Chuc_Nang> v_arrPQ_Temp = CCache_Phan_Quyen_Chuc_Nang.List_Data_By_Nhom_Quyen_ID(v_objNhom_TV.Nhom_Thanh_Vien_ID);

                foreach (CSys_Phan_Quyen_Chuc_Nang v_objPQ_Temp in v_arrPQ_Temp)
                {
                    if (v_dicAll.ContainsKey(v_objPQ_Temp.Chuc_Nang_ID) == false)
                        v_dicAll.Add(v_objPQ_Temp.Chuc_Nang_ID, v_objPQ_Temp);

                    CSys_Phan_Quyen_Chuc_Nang v_objData = v_dicAll[v_objPQ_Temp.Chuc_Nang_ID];

                    v_objData.Is_Have_Add_Permission = v_objData.Is_Have_Add_Permission || v_objPQ_Temp.Is_Have_Add_Permission;
                    v_objData.Is_Have_Edit_Permission = v_objData.Is_Have_Edit_Permission || v_objPQ_Temp.Is_Have_Edit_Permission;
                    v_objData.Is_Have_Delete_Permission = v_objData.Is_Have_Delete_Permission || v_objPQ_Temp.Is_Have_Delete_Permission;
                    v_objData.Is_Have_View_Permission = v_objData.Is_Have_View_Permission || v_objPQ_Temp.Is_Have_View_Permission;
                    v_objData.Is_Have_Export_Permission = v_objData.Is_Have_Export_Permission || v_objPQ_Temp.Is_Have_Export_Permission;
                }
            }

            // Map thông tin quyền vô list chức năng
            foreach (CSys_Chuc_Nang v_objRes in v_arrRes)
            {
                if (v_dicAll.ContainsKey(v_objRes.Auto_ID) == true)
                {
                    v_objRes.Is_Have_Add_Permission = v_dicAll[v_objRes.Auto_ID].Is_Have_Add_Permission;
                    v_objRes.Is_Have_Edit_Permission = v_dicAll[v_objRes.Auto_ID].Is_Have_Edit_Permission;
                    v_objRes.Is_Have_Delete_Permission = v_dicAll[v_objRes.Auto_ID].Is_Have_Delete_Permission;
                    v_objRes.Is_Have_View_Permission = v_dicAll[v_objRes.Auto_ID].Is_Have_View_Permission;
                    v_objRes.Is_Have_Export_Permission = v_dicAll[v_objRes.Auto_ID].Is_Have_Export_Permission;
                }
            }

            return v_arrRes;
        }

        public static CSys_Chuc_Nang Get_Chuc_Nang_By_User(string p_strUser, string p_strMa_Chuc_Nang)
        {
            // Lấy chức năng
            CSys_Chuc_Nang v_objRes = new CSys_Chuc_Nang();

            if (CCache_Chuc_Nang.Get_Data_By_Code(p_strMa_Chuc_Nang) != null)
                CUtility.Clone_Entity(CCache_Chuc_Nang.Get_Data_By_Code(p_strMa_Chuc_Nang), v_objRes);

            // Chức năng định nghĩa khả năng hỗ trợ; quyền người dùng phải bắt đầu từ deny.
            v_objRes.Is_Have_Add_Permission = false;
            v_objRes.Is_Have_Edit_Permission = false;
            v_objRes.Is_Have_Delete_Permission = false;
            v_objRes.Is_Have_View_Permission = false;
            v_objRes.Is_Have_Export_Permission = false;

            // Lấy danh sách nhóm quyền
            List<CSys_Nhom_Thanh_Vien_User> v_arrNhom_TV = CCache_Nhom_Thanh_Vien_User.List_Data_By_User(p_strUser);

            // List danh sách quyền của nhóm quyền map vô list
           // Dictionary<long, CSys_Phan_Quyen_Chuc_Nang> v_dicAll = new Dictionary<long, CSys_Phan_Quyen_Chuc_Nang>();

            foreach (CSys_Nhom_Thanh_Vien_User v_objNhom_TV in v_arrNhom_TV)
            {
                CSys_Phan_Quyen_Chuc_Nang v_objPQ_Temp = CCache_Phan_Quyen_Chuc_Nang.Get_Data_By_NQ_MCN_ID(v_objNhom_TV.Nhom_Thanh_Vien_ID, v_objRes.Auto_ID);

                if (v_objPQ_Temp != null)
                {
                    v_objRes.Is_Have_Add_Permission = v_objRes.Is_Have_Add_Permission || v_objPQ_Temp.Is_Have_Add_Permission;
                    v_objRes.Is_Have_Edit_Permission = v_objRes.Is_Have_Edit_Permission || v_objPQ_Temp.Is_Have_Edit_Permission;
                    v_objRes.Is_Have_Delete_Permission = v_objRes.Is_Have_Delete_Permission || v_objPQ_Temp.Is_Have_Delete_Permission;
                    v_objRes.Is_Have_View_Permission = v_objRes.Is_Have_View_Permission || v_objPQ_Temp.Is_Have_View_Permission;
                    v_objRes.Is_Have_Export_Permission = v_objRes.Is_Have_Export_Permission || v_objPQ_Temp.Is_Have_Export_Permission;
                }
            }

            return v_objRes;
        }
        public static CSys_Token FSys_Kiem_Tra_Token(CRequest_API p_objRequest, CResponse_API v_objResponse)
        {
            CSys_Token_Controller v_objCtrlToken = new CSys_Token_Controller();
            CSys_Token v_objToken = v_objCtrlToken.FQ_532_T_sp_sel_Get_By_Token_ID(p_objRequest.Auth.Token);

            if (v_objToken == null)
            {
                v_objResponse.Message.Message_Code = (int)EResponse_Status.Page_Login;
                v_objResponse.Message.Message_Desc = "Token does not exist or expired.";
                throw new Exception("Token does not exist or expired.");
            }

            return v_objToken;
        }
        
        public static CSys_Token FSys_Kiem_Tra_Token_API(CRequest_API p_objRequest, CResponse_API v_objResponse)
        {
            CSys_Token_Controller v_objCtrlToken = new CSys_Token_Controller();
            CSys_Token v_objToken = v_objCtrlToken.FQ_532_T_sp_sel_Get_By_Token_ID(p_objRequest.ToKen);

            if (v_objToken == null)
            {
                v_objResponse.Message.Message_Code = (int)EResponse_Status.Page_Login;
                v_objResponse.Message.Message_Desc = "Token does not exist or expired.";
                throw new Exception("Token does not exist or expired.");
            }

            if (v_objToken.Token_Expired <= CUtility_Date.Convert_To_Cuoi_Ngay(DateTime.Now))
            {
                v_objResponse.Message.Message_Code = (int)EResponse_Status.Page_Login;
                v_objResponse.Message.Message_Desc = "Token out of date";
				throw new Exception("");
			}
            return v_objToken;
        }


    }
}
