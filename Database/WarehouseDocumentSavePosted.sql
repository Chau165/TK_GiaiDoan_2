/* Reviewed against live modules exported to work/xnk-save-posted-baseline-20261102.
   Deployment is explicit. No existing procedure or trigger is replaced.
   Save owns its transaction: ambient transactions are rejected before any write.
   Root Exclusive serializes this correction path with all existing fence writers.
   EXECUTE AS OWNER uses the existing DBA exception in the immutable-ledger guards;
   application callers receive EXECUTE only, never table write privileges.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.XNK_Document_Revision',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.XNK_Document_Revision
    (
        Request_ID UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Is_Receipt BIT NOT NULL,
        Document_ID BIGINT NOT NULL,
        Document_Version BIGINT NOT NULL,
        Request_Hash VARBINARY(32) NOT NULL,
        Before_State NVARCHAR(MAX) NULL,
        After_State NVARCHAR(MAX) NOT NULL,
        Created DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        Created_By NVARCHAR(100) NOT NULL,
        Created_By_Function NVARCHAR(100) NOT NULL,
        CONSTRAINT UX_XNK_Document_Revision UNIQUE(Is_Receipt,Document_ID,Document_Version)
    );
END;
GO
/* Internal serializer. Only call while holding the inventory fence. */
CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Read_State
    @Is_Receipt BIT, @Document_ID BIGINT, @State NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Is_Receipt=1
        SELECT @State=(SELECT h.Auto_ID,CAST(1 AS BIT) Is_Receipt,h.So_Phieu_Nhap_Kho So_Phieu,h.Kho_ID,h.NCC_ID,
            h.Ngay_Nhap_Kho Ngay_Chung_Tu,h.Ghi_Chu,h.Is_Posted,h.Posted_At,h.Created,h.Created_By,h.Created_By_Function,
            h.Last_Updated,h.Last_Updated_By,h.Last_Updated_By_Function,k.Ten_Kho,n.Ten_NCC,
            JSON_QUERY((SELECT d.Auto_ID,d.Nhap_Kho_ID Document_ID,d.San_Pham_ID,d.SL_Nhap So_Luong,d.Don_Gia_Nhap Don_Gia,
                p.Ma_San_Pham,p.Ten_San_Pham,u.Ten_Don_Vi_Tinh,d.Created,d.Created_By,d.Created_By_Function,
                d.Last_Updated,d.Last_Updated_By,d.Last_Updated_By_Function
                FROM dbo.tbl_XNK_Nhap_Kho_Raw_Data d
                LEFT JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID
                LEFT JOIN dbo.tbl_DM_Don_Vi_Tinh u ON u.Auto_ID=p.Don_Vi_Tinh_ID
                WHERE d.Nhap_Kho_ID=h.Auto_ID ORDER BY d.Auto_ID FOR JSON PATH)) Details
            FROM dbo.tbl_XNK_Nhap_Kho h LEFT JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID
            LEFT JOIN dbo.tbl_DM_NCC n ON n.Auto_ID=h.NCC_ID WHERE h.Auto_ID=@Document_ID FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
    ELSE
        SELECT @State=(SELECT h.Auto_ID,CAST(0 AS BIT) Is_Receipt,h.So_Phieu_Xuat_Kho So_Phieu,h.Kho_ID,
            h.Ngay_Xuat_Kho Ngay_Chung_Tu,h.Ghi_Chu,h.Is_Posted,h.Posted_At,h.Created,h.Created_By,h.Created_By_Function,
            h.Last_Updated,h.Last_Updated_By,h.Last_Updated_By_Function,k.Ten_Kho,
            JSON_QUERY((SELECT d.Auto_ID,d.Xuat_Kho_ID Document_ID,d.San_Pham_ID,d.SL_Xuat So_Luong,d.Don_Gia_Xuat Don_Gia,
                p.Ma_San_Pham,p.Ten_San_Pham,u.Ten_Don_Vi_Tinh,d.Created,d.Created_By,d.Created_By_Function,
                d.Last_Updated,d.Last_Updated_By,d.Last_Updated_By_Function
                FROM dbo.tbl_XNK_Xuat_Kho_Raw_Data d
                LEFT JOIN dbo.tbl_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID
                LEFT JOIN dbo.tbl_DM_Don_Vi_Tinh u ON u.Auto_ID=p.Don_Vi_Tinh_ID
                WHERE d.Xuat_Kho_ID=h.Auto_ID ORDER BY d.Auto_ID FOR JSON PATH)) Details
            FROM dbo.tbl_XNK_Xuat_Kho h LEFT JOIN dbo.tbl_DM_Kho k ON k.Auto_ID=h.Kho_ID
            WHERE h.Auto_ID=@Document_ID FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Edit_Get
    @Is_Receipt BIT,@Document_ID BIGINT,@Ma_Dang_Nhap NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT<>0 THROW 51500,N'Đọc chứng từ phải dùng kết nối không có transaction bên ngoài.',1;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode=N'Exclusive';
        DECLARE @State NVARCHAR(MAX),@Kho BIGINT;
        EXEC dbo.sp_XNK_Document_Read_State @Is_Receipt,@Document_ID,@State OUTPUT;
        IF NULLIF(@State,N'') IS NULL THROW 51501,N'Chứng từ không tồn tại.',1;
        SET @Kho=TRY_CONVERT(BIGINT,JSON_VALUE(@State,'$.Kho_ID'));
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap,@Kho;
        IF NOT EXISTS
        (
            SELECT 1 FROM dbo.view_Sys_Thanh_Vien tv
            JOIN dbo.tbl_Sys_Nhom_Thanh_Vien_User gu ON gu.Ma_Dang_Nhap=tv.Ma_Dang_Nhap AND gu.deleted=0
            JOIN dbo.tbl_Sys_Nhom_Thanh_Vien g ON g.Auto_ID=gu.Nhom_Thanh_Vien_ID AND g.deleted=0
            JOIN dbo.view_Sys_Phan_Quyen_Chuc_Nang q ON q.Nhom_Thanh_Vien_ID=g.Auto_ID
            JOIN dbo.view_Sys_Chuc_Nang f ON f.Auto_ID=q.Chuc_Nang_ID
            WHERE tv.Ma_Dang_Nhap=@Ma_Dang_Nhap AND tv.Trang_Thai_ID=1 AND q.Is_Have_View_Permission=1
                AND f.Ma_Chuc_Nang=CASE WHEN @Is_Receipt=1 THEN N'3001' ELSE N'3002' END
        ) THROW 51506,N'Bạn không có quyền xem chức năng này.',1;
        SELECT @State AS Document_State,
            CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',@State),2) State_Token,
            ISNULL((SELECT MAX(Document_Version) FROM dbo.XNK_Document_Revision WHERE Is_Receipt=@Is_Receipt AND Document_ID=@Document_ID),0) Document_Version;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_XNK_Document_Save_Posted
    @Is_Receipt BIT,@Document_ID BIGINT,@Expected_Version BIGINT,@Request_ID UNIQUEIDENTIFIER,
    @So_Phieu NVARCHAR(100),@Kho_ID BIGINT,@NCC_ID BIGINT,@Ngay_Chung_Tu DATE,@Ghi_Chu NVARCHAR(1000),
    @Details NVARCHAR(MAX),@Ma_Dang_Nhap NVARCHAR(100),@Expected_State VARCHAR(64)=NULL
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @@TRANCOUNT<>0 THROW 51500,N'Lưu chứng từ phải sở hữu transaction riêng.',1;
    SET XACT_ABORT ON;
    SET @So_Phieu=LTRIM(RTRIM(ISNULL(@So_Phieu,N'')));
    SET @Ghi_Chu=LTRIM(RTRIM(ISNULL(@Ghi_Chu,N'')));
    SET @Ma_Dang_Nhap=LTRIM(RTRIM(ISNULL(@Ma_Dang_Nhap,N'')));
    IF @Is_Receipt IS NULL OR @Document_ID IS NULL OR @Document_ID<0 OR @Expected_Version IS NULL OR @Expected_Version<0
        OR @Request_ID IS NULL OR @Request_ID='00000000-0000-0000-0000-000000000000'
        OR @So_Phieu=N'' OR @Ngay_Chung_Tu IS NULL OR @Ma_Dang_Nhap=N''
        THROW 51501,N'Thông tin phiếu hoặc mã yêu cầu không hợp lệ.',1;
    IF ISJSON(@Details)<>1 OR LEFT(LTRIM(@Details),1)<>N'[' THROW 51501,N'Danh sách chi tiết không hợp lệ.',1;
    CREATE TABLE #New(Auto_ID BIGINT NOT NULL,San_Pham_ID BIGINT NOT NULL,So_Luong DECIMAL(18,3) NOT NULL,Don_Gia DECIMAL(18,2) NOT NULL);
    /* Reject malformed, missing and rounded numbers before converting to storage precision. */
    IF NOT EXISTS(SELECT 1 FROM OPENJSON(@Details)) OR EXISTS
    (
        SELECT 1 FROM OPENJSON(@Details) j
        OUTER APPLY OPENJSON(j.value) WITH(Auto_ID BIGINT,San_Pham_ID BIGINT,So_Luong DECIMAL(38,18),Don_Gia DECIMAL(38,18)) d
        WHERE j.type<>5 OR d.Auto_ID IS NULL OR d.Auto_ID<0 OR d.San_Pham_ID IS NULL OR d.San_Pham_ID<=0
            OR d.So_Luong IS NULL OR d.So_Luong<=0 OR TRY_CONVERT(DECIMAL(18,3),d.So_Luong) IS NULL
            OR d.So_Luong<>TRY_CONVERT(DECIMAL(18,3),d.So_Luong)
            OR d.Don_Gia IS NULL OR d.Don_Gia<=0 OR TRY_CONVERT(DECIMAL(18,2),d.Don_Gia) IS NULL
            OR d.Don_Gia<>TRY_CONVERT(DECIMAL(18,2),d.Don_Gia)
    ) THROW 51501,N'Chi tiết cần sản phẩm, số lượng dương (3 số lẻ), đơn giá dương (2 số lẻ).',1;
    INSERT #New SELECT Auto_ID,San_Pham_ID,So_Luong,Don_Gia FROM OPENJSON(@Details)
        WITH(Auto_ID BIGINT,San_Pham_ID BIGINT,So_Luong DECIMAL(18,3),Don_Gia DECIMAL(18,2));
    IF EXISTS(SELECT Auto_ID FROM #New WHERE Auto_ID<>0 GROUP BY Auto_ID HAVING COUNT(*)>1)
        THROW 51503,N'Chi tiết bị trùng ID.',1;
    DECLARE @Function NVARCHAR(100)=CASE WHEN @Is_Receipt=1 THEN N'3001' ELSE N'3002' END;
    DECLARE @Payload NVARCHAR(MAX)=(SELECT @Is_Receipt Is_Receipt,@Document_ID Document_ID,@Expected_Version Expected_Version,
        @So_Phieu So_Phieu,@Kho_ID Kho_ID,@NCC_ID NCC_ID,@Ngay_Chung_Tu Ngay_Chung_Tu,@Ghi_Chu Ghi_Chu,
        @Details Details,@Ma_Dang_Nhap Ma_Dang_Nhap,@Expected_State Expected_State FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
    DECLARE @Hash VARBINARY(32)=HASHBYTES('SHA2_256',@Payload),@Before NVARCHAR(MAX),@After NVARCHAR(MAX),@Version BIGINT;
    BEGIN TRY
        BEGIN TRANSACTION;
        /* Root is always acquired before document, reservation or projection row locks. */
        EXEC dbo.sp_Inventory_Fence_Acquire_Root @Mode=N'Exclusive';
        EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap,@Kho_ID;
        IF NOT EXISTS
        (
            SELECT 1 FROM dbo.view_Sys_Thanh_Vien tv
            JOIN dbo.tbl_Sys_Nhom_Thanh_Vien_User gu ON gu.Ma_Dang_Nhap=tv.Ma_Dang_Nhap AND gu.deleted=0
            JOIN dbo.tbl_Sys_Nhom_Thanh_Vien g ON g.Auto_ID=gu.Nhom_Thanh_Vien_ID AND g.deleted=0
            JOIN dbo.view_Sys_Phan_Quyen_Chuc_Nang q ON q.Nhom_Thanh_Vien_ID=g.Auto_ID
            JOIN dbo.view_Sys_Chuc_Nang f ON f.Auto_ID=q.Chuc_Nang_ID
            WHERE tv.Ma_Dang_Nhap=@Ma_Dang_Nhap AND tv.Trang_Thai_ID=1 AND f.Ma_Chuc_Nang=@Function
                AND ((@Document_ID=0 AND q.Is_Have_Add_Permission=1) OR (@Document_ID>0 AND q.Is_Have_Edit_Permission=1))
        ) THROW 51506,N'Bạn không có quyền thêm/hiệu chỉnh chức năng này.',1;
        IF EXISTS(SELECT 1 FROM dbo.XNK_Document_Revision WITH(UPDLOCK,HOLDLOCK) WHERE Request_ID=@Request_ID)
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM dbo.XNK_Document_Revision WHERE Request_ID=@Request_ID AND Request_Hash=@Hash AND Created_By=@Ma_Dang_Nhap)
                THROW 51505,N'Mã yêu cầu đã được dùng cho nội dung khác.',1;
            SELECT Document_ID Auto_ID,Document_Version FROM dbo.XNK_Document_Revision WHERE Request_ID=@Request_ID;
            COMMIT;
            RETURN;
        END;
        IF EXISTS(SELECT 1 FROM #New d LEFT JOIN dbo.view_DM_San_Pham p ON p.Auto_ID=d.San_Pham_ID WHERE p.Auto_ID IS NULL)
            THROW 51501,N'Sản phẩm không hợp lệ.',1;
        IF @Is_Receipt=1 AND NOT EXISTS(SELECT 1 FROM dbo.view_DM_NCC WHERE Auto_ID=@NCC_ID)
            THROW 51501,N'Nhà cung cấp không hợp lệ.',1;
        CREATE TABLE #Old(Auto_ID BIGINT PRIMARY KEY,San_Pham_ID BIGINT,So_Luong DECIMAL(18,3),Don_Gia DECIMAL(18,2));
        DECLARE @WasPosted BIT=0,@OldKho BIGINT,@OldDate DATE;
        SET @Version=ISNULL((SELECT MAX(Document_Version) FROM dbo.XNK_Document_Revision WITH(UPDLOCK,HOLDLOCK)
            WHERE Is_Receipt=@Is_Receipt AND Document_ID=@Document_ID),0);
        IF @Version<>@Expected_Version THROW 51502,N'Phiếu đã được người khác cập nhật. Vui lòng tải lại.',1;
        IF @Document_ID<>0
        BEGIN
            EXEC dbo.sp_XNK_Document_Read_State @Is_Receipt,@Document_ID,@Before OUTPUT;
            IF NULLIF(@Before,N'') IS NULL THROW 51501,N'Chứng từ không tồn tại.',1;
            IF @Expected_State IS NULL OR @Expected_State<>CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',@Before),2)
                THROW 51502,N'Phiếu hoặc chi tiết đã thay đổi. Vui lòng tải lại.',1;
            SELECT @OldKho=Kho_ID,@OldDate=Ngay_Chung_Tu,@WasPosted=Is_Posted
                FROM OPENJSON(@Before) WITH(Kho_ID BIGINT,Ngay_Chung_Tu DATE,Is_Posted BIT);
            EXEC dbo.sp_DM_Kho_User_Ensure_Access @Ma_Dang_Nhap,@OldKho;
            IF @OldKho<>@Kho_ID OR @OldDate<>@Ngay_Chung_Tu
                THROW 51504,N'Không hỗ trợ đổi kho/ngày của chứng từ đã lưu. Vui lòng giữ nguyên kho/ngày.',1;
            INSERT #Old SELECT Auto_ID,San_Pham_ID,So_Luong,Don_Gia FROM OPENJSON(@Before,'$.Details')
                WITH(Auto_ID BIGINT,San_Pham_ID BIGINT,So_Luong DECIMAL(18,3),Don_Gia DECIMAL(18,2));
        END;
        IF EXISTS(SELECT 1 FROM #New n LEFT JOIN #Old o ON o.Auto_ID=n.Auto_ID
            WHERE n.Auto_ID<>0 AND (o.Auto_ID IS NULL OR o.San_Pham_ID<>n.San_Pham_ID))
            THROW 51503,N'Không được đổi sản phẩm hoặc dùng ID chi tiết của phiếu khác.',1;

        DECLARE @GroupSet dbo.InventoryFenceGroupSetType,@ScopeSet dbo.InventoryFenceScopeSetType;
        INSERT @GroupSet(Kho_ID) VALUES(@Kho_ID);
        INSERT @ScopeSet(Kho_ID,San_Pham_ID)
            SELECT @Kho_ID,San_Pham_ID FROM #Old UNION SELECT @Kho_ID,San_Pham_ID FROM #New;
        EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Movement_Bootstrap @Mode=N'Shared';
        EXEC dbo.sp_Inventory_Fence_Acquire_Group_Set @GroupSet=@GroupSet,@Mode=N'Exclusive';
        EXEC dbo.sp_Inventory_Fence_Acquire_Legacy_Scope_Set @ScopeSet=@ScopeSet,@Mode=N'Exclusive';
        EXEC dbo.sp_Inventory_Fence_Require_Context @GroupSet=@GroupSet,@ScopeSet=@ScopeSet,
            @RequiredRootMode=N'Shared',@RequiredGroupMode=N'Exclusive',@RequiredScopeMode=N'Exclusive';

        /* Whitelisted identifiers only; all business values remain SQL parameters. */
        DECLARE @Header SYSNAME=CASE WHEN @Is_Receipt=1 THEN N'tbl_XNK_Nhap_Kho' ELSE N'tbl_XNK_Xuat_Kho' END,
            @Detail SYSNAME=CASE WHEN @Is_Receipt=1 THEN N'tbl_XNK_Nhap_Kho_Raw_Data' ELSE N'tbl_XNK_Xuat_Kho_Raw_Data' END,
            @Number SYSNAME=CASE WHEN @Is_Receipt=1 THEN N'So_Phieu_Nhap_Kho' ELSE N'So_Phieu_Xuat_Kho' END,
            @Date SYSNAME=CASE WHEN @Is_Receipt=1 THEN N'Ngay_Nhap_Kho' ELSE N'Ngay_Xuat_Kho' END,
            @Parent SYSNAME=CASE WHEN @Is_Receipt=1 THEN N'Nhap_Kho_ID' ELSE N'Xuat_Kho_ID' END,
            @Quantity SYSNAME=CASE WHEN @Is_Receipt=1 THEN N'SL_Nhap' ELSE N'SL_Xuat' END,
            @Price SYSNAME=CASE WHEN @Is_Receipt=1 THEN N'Don_Gia_Nhap' ELSE N'Don_Gia_Xuat' END,@Sql NVARCHAR(MAX);
        SET @Sql=N'IF EXISTS(SELECT 1 FROM dbo.'+QUOTENAME(@Header)+N' WITH(UPDLOCK,HOLDLOCK) WHERE '+QUOTENAME(@Number)+N'=@Number AND Auto_ID<>@ID)
            THROW 51507,N''Số phiếu đã tồn tại.'',1;
            IF @ID=0 BEGIN
                INSERT dbo.'+QUOTENAME(@Header)+N'('+QUOTENAME(@Number)+N',Kho_ID,'+QUOTENAME(@Date)+N',Ghi_Chu,Is_Posted,Created,Created_By,Created_By_Function,Last_Updated,Last_Updated_By,Last_Updated_By_Function'+CASE WHEN @Is_Receipt=1 THEN N',NCC_ID' ELSE N'' END+N')
                VALUES(@Number,@Kho,@Day,@Note,0,SYSUTCDATETIME(),@Actor,@Function,SYSUTCDATETIME(),@Actor,@Function'+CASE WHEN @Is_Receipt=1 THEN N',@Supplier' ELSE N'' END+N');
                SET @ID=SCOPE_IDENTITY();
            END ELSE UPDATE dbo.'+QUOTENAME(@Header)+N' SET '+QUOTENAME(@Number)+N'=@Number,Ghi_Chu=@Note,
                Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Actor,Last_Updated_By_Function=@Function'+CASE WHEN @Is_Receipt=1 THEN N',NCC_ID=@Supplier' ELSE N'' END+N' WHERE Auto_ID=@ID;';
        EXEC sys.sp_executesql @Sql,N'@ID BIGINT OUTPUT,@Number NVARCHAR(100),@Kho BIGINT,@Day DATE,@Note NVARCHAR(1000),@Actor NVARCHAR(100),@Function NVARCHAR(100),@Supplier BIGINT',
            @Document_ID OUTPUT,@So_Phieu,@Kho_ID,@Ngay_Chung_Tu,@Ghi_Chu,@Ma_Dang_Nhap,@Function,@NCC_ID;

        /* Existing draft issue allocations are released and replaced by the atomic Post.
           Root Exclusive prevents any competing reservation from observing the gap. */
        IF @Is_Receipt=0 AND @WasPosted=0 EXEC dbo.sp_XNK_Reservation_Release_Document @Document_ID;
        SET @Sql=N'DELETE d FROM dbo.'+QUOTENAME(@Detail)+N' d WHERE d.'+QUOTENAME(@Parent)+N'=@ID AND NOT EXISTS(SELECT 1 FROM #New n WHERE n.Auto_ID=d.Auto_ID);
            UPDATE d SET '+QUOTENAME(@Quantity)+N'=n.So_Luong,'+QUOTENAME(@Price)+N'=n.Don_Gia,Last_Updated=SYSUTCDATETIME(),Last_Updated_By=@Actor,Last_Updated_By_Function=@Function
            FROM dbo.'+QUOTENAME(@Detail)+N' d JOIN #New n ON n.Auto_ID=d.Auto_ID WHERE d.'+QUOTENAME(@Parent)+N'=@ID
                AND (d.'+QUOTENAME(@Quantity)+N'<>n.So_Luong OR d.'+QUOTENAME(@Price)+N'<>n.Don_Gia);
            INSERT dbo.'+QUOTENAME(@Detail)+N'('+QUOTENAME(@Parent)+N',San_Pham_ID,'+QUOTENAME(@Quantity)+N','+QUOTENAME(@Price)+N',Created,Created_By,Created_By_Function,Last_Updated,Last_Updated_By,Last_Updated_By_Function)
            SELECT @ID,San_Pham_ID,So_Luong,Don_Gia,SYSUTCDATETIME(),@Actor,@Function,SYSUTCDATETIME(),@Actor,@Function FROM #New WHERE Auto_ID=0;';
        EXEC sys.sp_executesql @Sql,N'@ID BIGINT,@Actor NVARCHAR(100),@Function NVARCHAR(100)',@Document_ID,@Ma_Dang_Nhap,@Function;
        IF @WasPosted=0
            EXEC dbo.sp_XNK_Document_Post @Is_Receipt,@Document_ID,@Ma_Dang_Nhap,@Ma_Dang_Nhap,@Function;
        ELSE
        BEGIN
            CREATE TABLE #Correction(San_Pham_ID BIGINT PRIMARY KEY,Delta DECIMAL(18,3));
            INSERT #Correction SELECT San_Pham_ID,SUM(Delta) FROM
            (
                SELECT San_Pham_ID,CASE WHEN @Is_Receipt=1 THEN So_Luong ELSE -So_Luong END Delta FROM #New
                UNION ALL SELECT San_Pham_ID,CASE WHEN @Is_Receipt=1 THEN -So_Luong ELSE So_Luong END FROM #Old
            ) x GROUP BY San_Pham_ID;
            IF EXISTS(SELECT 1 FROM #Correction d LEFT JOIN dbo.InventoryBalance_Current b WITH(UPDLOCK,HOLDLOCK)
                ON b.Kho_ID=@Kho_ID AND b.San_Pham_ID=d.San_Pham_ID
                WHERE ISNULL(b.CurrentQuantity,0)+d.Delta<ISNULL(b.ReservedQuantity,0))
                THROW 51120,N'Hiệu chỉnh làm tồn kho thấp hơn lượng đã giữ chỗ hoặc âm.',1;
            UPDATE b SET CurrentQuantity=b.CurrentQuantity+d.Delta,UpdatedAt=SYSUTCDATETIME()
                FROM dbo.InventoryBalance_Current b WITH(UPDLOCK,HOLDLOCK) JOIN #Correction d ON d.San_Pham_ID=b.San_Pham_ID
                WHERE b.Kho_ID=@Kho_ID AND d.Delta<>0;
            INSERT dbo.InventoryBalance_Current(Kho_ID,San_Pham_ID,CurrentQuantity,ReservedQuantity)
                SELECT @Kho_ID,d.San_Pham_ID,d.Delta,0 FROM #Correction d WHERE NOT EXISTS
                (SELECT 1 FROM dbo.InventoryBalance_Current b WITH(UPDLOCK,HOLDLOCK) WHERE b.Kho_ID=@Kho_ID AND b.San_Pham_ID=d.San_Pham_ID);
            /* Validate every daily closing balance in the affected ledger suffix. */
            DECLARE @Negative BIT=0;
            ;WITH Movements AS
            (
                SELECT d.San_Pham_ID,h.Ngay_Nhap_Kho MovementDate,d.SL_Nhap Delta FROM dbo.tbl_XNK_Nhap_Kho h
                JOIN dbo.tbl_XNK_Nhap_Kho_Raw_Data d ON d.Nhap_Kho_ID=h.Auto_ID JOIN #Correction c ON c.San_Pham_ID=d.San_Pham_ID
                WHERE h.Kho_ID=@Kho_ID AND h.Is_Posted=1
                UNION ALL SELECT d.San_Pham_ID,h.Ngay_Xuat_Kho,-d.SL_Xuat FROM dbo.tbl_XNK_Xuat_Kho h
                JOIN dbo.tbl_XNK_Xuat_Kho_Raw_Data d ON d.Xuat_Kho_ID=h.Auto_ID JOIN #Correction c ON c.San_Pham_ID=d.San_Pham_ID
                WHERE h.Kho_ID=@Kho_ID AND h.Is_Posted=1
            ), Daily AS (SELECT San_Pham_ID,MovementDate,SUM(Delta) Delta FROM Movements GROUP BY San_Pham_ID,MovementDate),
            Running AS (SELECT MovementDate,SUM(Delta) OVER(PARTITION BY San_Pham_ID ORDER BY MovementDate ROWS UNBOUNDED PRECEDING) Quantity FROM Daily)
            SELECT @Negative=1 WHERE EXISTS(SELECT 1 FROM Running WHERE MovementDate>=@Ngay_Chung_Tu AND Quantity<0);
            IF @Negative=1 THROW 51120,N'Hiệu chỉnh làm tồn kho âm trong lịch sử.',1;
            DECLARE @Affected dbo.InventoryMovementAffectedType;
            INSERT @Affected(Kho_ID,San_Pham_ID,Movement_Date,InvalidReason) SELECT @Kho_ID,San_Pham_ID,@Ngay_Chung_Tu,N'DOCUMENT_CORRECTION' FROM #Correction;
            EXEC dbo.sp_Inventory_Movement_Apply_Invalidation @Affected;
            /* Detail triggers already invalidate snapshots; also cover price-only/header edits. */
            DECLARE @Snapshots dbo.InventorySnapshotAffectedType;
            INSERT @Snapshots(Kho_ID,San_Pham_ID,From_Date,InvalidReason) SELECT @Kho_ID,San_Pham_ID,@Ngay_Chung_Tu,N'DOCUMENT_CORRECTION' FROM #Correction;
            EXEC dbo.sp_Inventory_Snapshot_Apply_Invalidation @Snapshots;
            UPDATE c SET First_Posted_Date=CASE WHEN c.First_Posted_Date IS NULL OR c.First_Posted_Date>@Ngay_Chung_Tu THEN @Ngay_Chung_Tu ELSE c.First_Posted_Date END,
                Last_Posted_Date=CASE WHEN c.Last_Posted_Date IS NULL OR c.Last_Posted_Date<@Ngay_Chung_Tu THEN @Ngay_Chung_Tu ELSE c.Last_Posted_Date END,
                Is_Current=1,[Version]=c.[Version]+1,UpdatedAt=SYSUTCDATETIME()
                FROM dbo.Inventory_Report_Scope_Catalog c JOIN #Correction d ON d.San_Pham_ID=c.San_Pham_ID WHERE c.Kho_ID=@Kho_ID;
            INSERT dbo.Inventory_Report_Scope_Catalog(Kho_ID,San_Pham_ID,First_Posted_Date,Last_Posted_Date,Is_Current)
                SELECT @Kho_ID,d.San_Pham_ID,@Ngay_Chung_Tu,@Ngay_Chung_Tu,1 FROM #Correction d WHERE NOT EXISTS
                (SELECT 1 FROM dbo.Inventory_Report_Scope_Catalog c WITH(UPDLOCK,HOLDLOCK) WHERE c.Kho_ID=@Kho_ID AND c.San_Pham_ID=d.San_Pham_ID);
        END;
        EXEC dbo.sp_XNK_Document_Read_State @Is_Receipt,@Document_ID,@After OUTPUT;
        SET @Version=@Version+1;
        INSERT dbo.XNK_Document_Revision(Request_ID,Is_Receipt,Document_ID,Document_Version,Request_Hash,Before_State,After_State,Created_By,Created_By_Function)
            VALUES(@Request_ID,@Is_Receipt,@Document_ID,@Version,@Hash,@Before,@After,@Ma_Dang_Nhap,@Function);
        COMMIT;
        SELECT @Document_ID Auto_ID,@Version Document_Version;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
IF DATABASE_PRINCIPAL_ID(N'Warehouse_Application') IS NOT NULL
BEGIN
    GRANT EXECUTE ON dbo.sp_XNK_Document_Save_Posted TO Warehouse_Application;
    GRANT EXECUTE ON dbo.sp_XNK_Document_Edit_Get TO Warehouse_Application;
    DENY EXECUTE ON dbo.sp_XNK_Document_Read_State TO Warehouse_Application;
    DENY SELECT,INSERT,UPDATE,DELETE ON dbo.XNK_Document_Revision TO Warehouse_Application;
END;
GO
