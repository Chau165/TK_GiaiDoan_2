/* Run after WarehouseModule.Schema.sql and WarehouseModule.Procedures.sql. */
SET NOCOUNT ON;
GO

/* The canonical document-posting procedure is defined in WarehouseModule.Procedures.sql. */

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Validate_All_Balances
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @HasNegative BIT=0;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Nhap_Kho AS MovementDate,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Delta FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
        UNION ALL
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Xuat_Kho,CAST(-d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
    ), Daily AS (SELECT Kho_ID,San_Pham_ID,MovementDate,SUM(Delta) AS Delta FROM Movements GROUP BY Kho_ID,San_Pham_ID,MovementDate),
    Running AS (SELECT SUM(Delta) OVER(PARTITION BY Kho_ID,San_Pham_ID ORDER BY MovementDate ROWS UNBOUNDED PRECEDING) AS Balance FROM Daily)
    SELECT @HasNegative=CASE WHEN MIN(Balance)<0 THEN 1 ELSE 0 END FROM Running;
    IF @HasNegative=1 THROW 51120,N'Không thể Post vì tồn kho sẽ âm tại một thời điểm trong lịch sử.',1;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Nhap_Kho NVARCHAR(100), @Kho_ID BIGINT, @NCC_ID BIGINT, @Ngay_Nhap_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @So_Phieu_Nhap_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Nhap_Kho,N'')));
    IF @So_Phieu_Nhap_Kho=N'' THROW 51100,N'Số phiếu nhập không được để trống.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51101,N'Số phiếu nhập đã tồn tại.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51102,N'Kho không hợp lệ.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_NCC WHERE Auto_ID=@NCC_ID) THROW 51103,N'Nhà cung cấp không hợp lệ.',1; IF @Ngay_Nhap_Kho IS NULL THROW 51104,N'Ngày nhập kho không được để trống.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho,Kho_ID,NCC_ID,Ngay_Nhap_Kho,Is_Posted,Ghi_Chu) VALUES(@So_Phieu_Nhap_Kho,@Kho_ID,@NCC_ID,@Ngay_Nhap_Kho,0,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE BEGIN IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID) THROW 51105,N'Phiếu nhập không tồn tại.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID AND Is_Posted=1) THROW 51163,N'Không được sửa phiếu đã Post.',1; UPDATE dbo.tbl_XNK_Nhap_Kho SET So_Phieu_Nhap_Kho=@So_Phieu_Nhap_Kho,Kho_ID=@Kho_ID,NCC_ID=@NCC_ID,Ngay_Nhap_Kho=@Ngay_Nhap_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Header
    @Auto_ID BIGINT OUTPUT, @So_Phieu_Xuat_Kho NVARCHAR(100), @Kho_ID BIGINT, @Ngay_Xuat_Kho DATE, @Ghi_Chu NVARCHAR(1000)=NULL
AS
BEGIN
    SET NOCOUNT ON; SET @So_Phieu_Xuat_Kho=LTRIM(RTRIM(ISNULL(@So_Phieu_Xuat_Kho,N'')));
    IF @So_Phieu_Xuat_Kho=N'' THROW 51130,N'Số phiếu xuất không được để trống.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho AND Auto_ID<>ISNULL(@Auto_ID,0)) THROW 51131,N'Số phiếu xuất đã tồn tại.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_Kho WHERE Auto_ID=@Kho_ID) THROW 51132,N'Kho không hợp lệ.',1; IF @Ngay_Xuat_Kho IS NULL THROW 51133,N'Ngày xuất kho không được để trống.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho,Kho_ID,Ngay_Xuat_Kho,Is_Posted,Ghi_Chu) VALUES(@So_Phieu_Xuat_Kho,@Kho_ID,@Ngay_Xuat_Kho,0,@Ghi_Chu); SET @Auto_ID=SCOPE_IDENTITY(); END
    ELSE BEGIN IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID) THROW 51134,N'Phiếu xuất không tồn tại.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID AND Is_Posted=1) THROW 51163,N'Không được sửa phiếu đã Post.',1; UPDATE dbo.tbl_XNK_Xuat_Kho SET So_Phieu_Xuat_Kho=@So_Phieu_Xuat_Kho,Kho_ID=@Kho_ID,Ngay_Xuat_Kho=@Ngay_Xuat_Kho,Ghi_Chu=@Ghi_Chu,Last_Updated=SYSUTCDATETIME() WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Save_Detail @Auto_ID BIGINT OUTPUT, @Nhap_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Nhap DECIMAL(18,3), @Don_Gia_Nhap DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Nhap_Kho_ID) THROW 51105,N'Phiếu nhập không tồn tại.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Nhap_Kho_ID AND Is_Posted=1) THROW 51163,N'Không được sửa chi tiết của phiếu đã Post.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51106,N'Sản phẩm không hợp lệ.',1; IF @SL_Nhap<=0 THROW 51107,N'Số lượng nhập phải lớn hơn 0.',1; IF @Don_Gia_Nhap<=0 THROW 51108,N'Đơn giá nhập phải lớn hơn 0.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID,San_Pham_ID,SL_Nhap,Don_Gia_Nhap) VALUES(@Nhap_Kho_ID,@San_Pham_ID,@SL_Nhap,@Don_Gia_Nhap); SET @Auto_ID=SCOPE_IDENTITY(); END ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Nhap_Kho_ID<>@Nhap_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51110,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Nhap_Kho_Raw_Data SET SL_Nhap=@SL_Nhap,Don_Gia_Nhap=@Don_Gia_Nhap WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Save_Detail @Auto_ID BIGINT OUTPUT, @Xuat_Kho_ID BIGINT, @San_Pham_ID BIGINT, @SL_Xuat DECIMAL(18,3), @Don_Gia_Xuat DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Xuat_Kho_ID) THROW 51134,N'Phiếu xuất không tồn tại.',1; IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Xuat_Kho_ID AND Is_Posted=1) THROW 51163,N'Không được sửa chi tiết của phiếu đã Post.',1; IF NOT EXISTS(SELECT 1 FROM dbo.tbl_DM_San_Pham WHERE Auto_ID=@San_Pham_ID) THROW 51135,N'Sản phẩm không hợp lệ.',1; IF @SL_Xuat<=0 THROW 51136,N'Số lượng xuất phải lớn hơn 0.',1; IF @Don_Gia_Xuat<=0 THROW 51137,N'Đơn giá xuất phải lớn hơn 0.',1;
    IF ISNULL(@Auto_ID,0)=0 BEGIN INSERT dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID,San_Pham_ID,SL_Xuat,Don_Gia_Xuat) VALUES(@Xuat_Kho_ID,@San_Pham_ID,@SL_Xuat,@Don_Gia_Xuat); SET @Auto_ID=SCOPE_IDENTITY(); END ELSE BEGIN IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID=@Auto_ID AND (Xuat_Kho_ID<>@Xuat_Kho_ID OR San_Pham_ID<>@San_Pham_ID)) THROW 51138,N'Không được phép sửa phiếu hoặc sản phẩm của chi tiết.',1; UPDATE dbo.tbl_XNK_Xuat_Kho_Raw_Data SET SL_Xuat=@SL_Xuat,Don_Gia_Xuat=@Don_Gia_Xuat WHERE Auto_ID=@Auto_ID; END
    SELECT @Auto_ID AS Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_InventoryBalance_Rebuild
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION;
    DELETE FROM dbo.InventoryBalance_Current;
    ;WITH Delta AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS Amount FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
        UNION ALL SELECT h.Kho_ID,d.San_Pham_ID,CAST(-d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
    ) INSERT dbo.InventoryBalance_Current(Kho_ID,San_Pham_ID,CurrentQuantity) SELECT Kho_ID,San_Pham_ID,SUM(Amount) FROM Delta GROUP BY Kho_ID,San_Pham_ID;
    COMMIT TRANSACTION;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Header @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID AND Is_Posted=1) THROW 51163,N'Không được xóa phiếu đã Post.',1;
    DELETE FROM dbo.tbl_XNK_Nhap_Kho WHERE Auto_ID=@Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Header @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID AND Is_Posted=1) THROW 51163,N'Không được xóa phiếu đã Post.',1;
    DELETE FROM dbo.tbl_XNK_Xuat_Kho WHERE Auto_ID=@Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Nhap_Kho_Delete_Detail @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d JOIN dbo.tbl_XNK_Nhap_Kho h ON h.Auto_ID=d.Nhap_Kho_ID WHERE d.Auto_ID=@Auto_ID AND h.Is_Posted=1) THROW 51163,N'Không được xóa chi tiết của phiếu đã Post.',1;
    DELETE FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data WHERE Auto_ID=@Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Xuat_Kho_Delete_Detail @Auto_ID BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d JOIN dbo.tbl_XNK_Xuat_Kho h ON h.Auto_ID=d.Xuat_Kho_ID WHERE d.Auto_ID=@Auto_ID AND h.Is_Posted=1) THROW 51163,N'Không được xóa chi tiết của phiếu đã Post.',1;
    DELETE FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data WHERE Auto_ID=@Auto_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_List @Is_Receipt BIT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Is_Receipt=1 SELECT h.Auto_ID,CAST(1 AS BIT) AS Is_Receipt,h.So_Phieu_Nhap_Kho AS So_Phieu,h.Kho_ID,k.Ten_Kho,h.NCC_ID,n.Ten_NCC,h.Ngay_Nhap_Kho AS Ngay_Chung_Tu,h.Is_Posted,h.Posted_At,h.Ghi_Chu FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID ORDER BY h.Ngay_Nhap_Kho DESC,h.Auto_ID DESC;
    ELSE SELECT h.Auto_ID,CAST(0 AS BIT) AS Is_Receipt,h.So_Phieu_Xuat_Kho AS So_Phieu,h.Kho_ID,k.Ten_Kho,CAST(0 AS BIGINT) AS NCC_ID,CAST(N'' AS NVARCHAR(255)) AS Ten_NCC,h.Ngay_Xuat_Kho AS Ngay_Chung_Tu,h.Is_Posted,h.Posted_At,h.Ghi_Chu FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID ORDER BY h.Ngay_Xuat_Kho DESC,h.Auto_ID DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Page @Is_Receipt BIT, @Page_Number INT, @Page_Size INT, @Search_Text NVARCHAR(100)=N''
AS
BEGIN
    SET NOCOUNT ON; IF @Page_Number<1 SET @Page_Number=1; IF @Page_Size<1 SET @Page_Size=10; DECLARE @Filter NVARCHAR(260)=N'%'+@Search_Text+N'%';
    IF @Is_Receipt=1
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID WHERE @Search_Text=N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter;
        SELECT h.Auto_ID,CAST(1 AS BIT) AS Is_Receipt,h.So_Phieu_Nhap_Kho AS So_Phieu,h.Kho_ID,k.Ten_Kho,h.NCC_ID,n.Ten_NCC,h.Ngay_Nhap_Kho AS Ngay_Chung_Tu,h.Is_Posted,h.Posted_At,h.Ghi_Chu FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID WHERE @Search_Text=N'' OR h.So_Phieu_Nhap_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter OR n.Ten_NCC LIKE @Filter ORDER BY h.Ngay_Nhap_Kho DESC,h.Auto_ID DESC OFFSET (@Page_Number-1)*@Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
    ELSE
    BEGIN
        SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID WHERE @Search_Text=N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter;
        SELECT h.Auto_ID,CAST(0 AS BIT) AS Is_Receipt,h.So_Phieu_Xuat_Kho AS So_Phieu,h.Kho_ID,k.Ten_Kho,CAST(0 AS BIGINT) AS NCC_ID,CAST(N'' AS NVARCHAR(255)) AS Ten_NCC,h.Ngay_Xuat_Kho AS Ngay_Chung_Tu,h.Is_Posted,h.Posted_At,h.Ghi_Chu FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID WHERE @Search_Text=N'' OR h.So_Phieu_Xuat_Kho LIKE @Filter OR k.Ten_Kho LIKE @Filter ORDER BY h.Ngay_Xuat_Kho DESC,h.Auto_ID DESC OFFSET (@Page_Number-1)*@Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
    END
END
GO

/* Historical reports read the ledger only after Post. */
CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton @Tu_Ngay DATE, @Den_Ngay DATE
AS
BEGIN
    SET NOCOUNT ON; IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Nhap_Kho AS MovementDate,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity,CAST(0 AS DECIMAL(18,3)) AS OutQuantity FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
        UNION ALL SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Xuat_Kho,CAST(0 AS DECIMAL(18,3)),CAST(d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
    )
    SELECT m.Kho_ID,m.San_Pham_ID,p.Ma_San_Pham,p.Ten_San_Pham,SUM(CASE WHEN m.MovementDate<@Tu_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Dau_Ky,SUM(CASE WHEN m.MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN m.InQuantity ELSE 0 END) AS SL_Nhap,SUM(CASE WHEN m.MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN m.OutQuantity ELSE 0 END) AS SL_Xuat,SUM(CASE WHEN m.MovementDate<=@Den_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Cuoi_Ky FROM Movements m JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=m.San_Pham_ID WHERE m.MovementDate<=@Den_Ngay GROUP BY m.Kho_ID,m.San_Pham_ID,p.Ma_San_Pham,p.Ten_San_Pham ORDER BY p.Ma_San_Pham,m.Kho_ID;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Xuat_Nhap_Ton_Page @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT
AS
BEGIN
    SET NOCOUNT ON; IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1; IF @Page_Number<1 SET @Page_Number=1; IF @Page_Size<1 SET @Page_Size=10;
    ;WITH Movements AS
    (
        SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Nhap_Kho AS MovementDate,CAST(d.SL_Nhap AS DECIMAL(18,3)) AS InQuantity,CAST(0 AS DECIMAL(18,3)) AS OutQuantity FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
        UNION ALL SELECT h.Kho_ID,d.San_Pham_ID,h.Ngay_Xuat_Kho,CAST(0 AS DECIMAL(18,3)),CAST(d.SL_Xuat AS DECIMAL(18,3)) FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1
    )
    SELECT m.Kho_ID,m.San_Pham_ID,k.Ten_Kho AS Ten_Kho,p.Ma_San_Pham,p.Ten_San_Pham,SUM(CASE WHEN m.MovementDate<@Tu_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Dau_Ky,SUM(CASE WHEN m.MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN m.InQuantity ELSE 0 END) AS SL_Nhap,SUM(CASE WHEN m.MovementDate BETWEEN @Tu_Ngay AND @Den_Ngay THEN m.OutQuantity ELSE 0 END) AS SL_Xuat,SUM(CASE WHEN m.MovementDate<=@Den_Ngay THEN m.InQuantity-m.OutQuantity ELSE 0 END) AS SL_Cuoi_Ky INTO #Aggregated FROM Movements m JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=m.Kho_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=m.San_Pham_ID WHERE m.MovementDate<=@Den_Ngay GROUP BY m.Kho_ID,m.San_Pham_ID,k.Ten_Kho,p.Ma_San_Pham,p.Ten_San_Pham;
    SELECT COUNT(*) AS Total_Count FROM #Aggregated; SELECT Kho_ID,Ten_Kho,San_Pham_ID,Ma_San_Pham,Ten_San_Pham,SL_Dau_Ky,SL_Nhap,SL_Xuat,SL_Cuoi_Ky FROM #Aggregated ORDER BY Ma_San_Pham,Kho_ID OFFSET (@Page_Number-1)*@Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap @Tu_Ngay DATE, @Den_Ngay DATE
AS
BEGIN
    SET NOCOUNT ON; IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    SELECT h.Ngay_Nhap_Kho AS Ngay,h.So_Phieu_Nhap_Kho AS So_Phieu,n.Ten_NCC AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Nhap AS So_Luong,d.Don_Gia_Nhap AS Don_Gia,CAST(d.SL_Nhap*d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID WHERE h.Is_Posted=1 AND h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Nhap_Kho,h.So_Phieu_Nhap_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat @Tu_Ngay DATE, @Den_Ngay DATE
AS
BEGIN
    SET NOCOUNT ON; IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1;
    SELECT h.Ngay_Xuat_Kho AS Ngay,h.So_Phieu_Xuat_Kho AS So_Phieu,CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Xuat AS So_Luong,d.Don_Gia_Xuat AS Don_Gia,CAST(d.SL_Xuat*d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID WHERE h.Is_Posted=1 AND h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Xuat_Kho,h.So_Phieu_Xuat_Kho;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Nhap_Page @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT
AS
BEGIN
    SET NOCOUNT ON; IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1; IF @Page_Number<1 SET @Page_Number=1; IF @Page_Size<1 SET @Page_Size=10;
    SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1 AND h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay;
    SELECT h.Ngay_Nhap_Kho AS Ngay,h.So_Phieu_Nhap_Kho AS So_Phieu,n.Ten_NCC AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Nhap AS So_Luong,d.Don_Gia_Nhap AS Don_Gia,CAST(d.SL_Nhap*d.Don_Gia_Nhap AS DECIMAL(18,2)) AS Tri_Gia FROM dbo.tbl_XNK_Nhap_Kho h JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID WHERE h.Is_Posted=1 AND h.Ngay_Nhap_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Nhap_Kho,h.So_Phieu_Nhap_Kho OFFSET (@Page_Number-1)*@Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_BC_Chi_Tiet_Xuat_Page @Tu_Ngay DATE, @Den_Ngay DATE, @Page_Number INT, @Page_Size INT
AS
BEGIN
    SET NOCOUNT ON; IF @Tu_Ngay IS NULL OR @Den_Ngay IS NULL OR @Tu_Ngay>@Den_Ngay THROW 51200,N'Khoảng ngày báo cáo không hợp lệ.',1; IF @Page_Number<1 SET @Page_Number=1; IF @Page_Size<1 SET @Page_Size=10;
    SELECT COUNT(*) AS Total_Count FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID WHERE h.Is_Posted=1 AND h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay;
    SELECT h.Ngay_Xuat_Kho AS Ngay,h.So_Phieu_Xuat_Kho AS So_Phieu,CAST(N'' AS NVARCHAR(255)) AS Nha_Cung_Cap,p.Ma_San_Pham,p.Ten_San_Pham,d.SL_Xuat AS So_Luong,d.Don_Gia_Xuat AS Don_Gia,CAST(d.SL_Xuat*d.Don_Gia_Xuat AS DECIMAL(18,2)) AS Tri_Gia FROM dbo.tbl_XNK_Xuat_Kho h JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID WHERE h.Is_Posted=1 AND h.Ngay_Xuat_Kho BETWEEN @Tu_Ngay AND @Den_Ngay ORDER BY h.Ngay_Xuat_Kho,h.So_Phieu_Xuat_Kho OFFSET (@Page_Number-1)*@Page_Size ROWS FETCH NEXT @Page_Size ROWS ONLY;
END
GO
