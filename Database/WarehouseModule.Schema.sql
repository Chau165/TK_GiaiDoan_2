/* Run against TKS_Thuc_Tap_V11_GiaiDoan2. */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.tbl_DM_Don_Vi_Tinh', N'U') IS NULL
CREATE TABLE dbo.tbl_DM_Don_Vi_Tinh
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_DM_Don_Vi_Tinh PRIMARY KEY,
    Ten_Don_Vi_Tinh NVARCHAR(200) NOT NULL,
    Ghi_Chu NVARCHAR(1000) NULL,
    Created DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_Don_Vi_Tinh_Created DEFAULT SYSUTCDATETIME(),
    Last_Updated DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_Don_Vi_Tinh_Last_Updated DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID(N'dbo.tbl_DM_Loai_San_Pham', N'U') IS NULL
CREATE TABLE dbo.tbl_DM_Loai_San_Pham
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_DM_Loai_San_Pham PRIMARY KEY,
    Ma_LSP NVARCHAR(100) NOT NULL,
    Ten_LSP NVARCHAR(200) NOT NULL,
    Ghi_Chu NVARCHAR(1000) NULL,
    Created DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_Loai_San_Pham_Created DEFAULT SYSUTCDATETIME(),
    Last_Updated DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_Loai_San_Pham_Last_Updated DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID(N'dbo.tbl_DM_San_Pham', N'U') IS NULL
CREATE TABLE dbo.tbl_DM_San_Pham
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_DM_San_Pham PRIMARY KEY,
    Ma_San_Pham NVARCHAR(100) NOT NULL,
    Ten_San_Pham NVARCHAR(255) NOT NULL,
    Loai_San_Pham_ID BIGINT NOT NULL,
    Don_Vi_Tinh_ID BIGINT NOT NULL,
    Ghi_Chu NVARCHAR(1000) NULL,
    Created DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_San_Pham_Created DEFAULT SYSUTCDATETIME(),
    Last_Updated DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_San_Pham_Last_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_tbl_DM_San_Pham_Loai FOREIGN KEY (Loai_San_Pham_ID) REFERENCES dbo.tbl_DM_Loai_San_Pham(Auto_ID),
    CONSTRAINT FK_tbl_DM_San_Pham_Don_Vi FOREIGN KEY (Don_Vi_Tinh_ID) REFERENCES dbo.tbl_DM_Don_Vi_Tinh(Auto_ID)
);
GO

IF OBJECT_ID(N'dbo.tbl_DM_NCC', N'U') IS NULL
CREATE TABLE dbo.tbl_DM_NCC
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_DM_NCC PRIMARY KEY,
    Ma_NCC NVARCHAR(100) NOT NULL,
    Ten_NCC NVARCHAR(255) NOT NULL,
    Ghi_Chu NVARCHAR(1000) NULL,
    Created DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_NCC_Created DEFAULT SYSUTCDATETIME(),
    Last_Updated DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_NCC_Last_Updated DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID(N'dbo.tbl_DM_Kho', N'U') IS NULL
CREATE TABLE dbo.tbl_DM_Kho
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_DM_Kho PRIMARY KEY,
    Ten_Kho NVARCHAR(255) NOT NULL,
    Ghi_Chu NVARCHAR(1000) NULL,
    Created DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_Kho_Created DEFAULT SYSUTCDATETIME(),
    Last_Updated DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_Kho_Last_Updated DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID(N'dbo.tbl_DM_Kho_User', N'U') IS NULL
CREATE TABLE dbo.tbl_DM_Kho_User
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_DM_Kho_User PRIMARY KEY,
    Ma_Dang_Nhap NVARCHAR(100) NOT NULL,
    Kho_ID BIGINT NOT NULL,
    Created DATETIME2 NOT NULL CONSTRAINT DF_tbl_DM_Kho_User_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_tbl_DM_Kho_User_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID)
);
GO

IF OBJECT_ID(N'dbo.tbl_XNK_Nhap_Kho', N'U') IS NULL
CREATE TABLE dbo.tbl_XNK_Nhap_Kho
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_XNK_Nhap_Kho PRIMARY KEY,
    So_Phieu_Nhap_Kho NVARCHAR(100) NOT NULL,
    Kho_ID BIGINT NOT NULL,
    NCC_ID BIGINT NOT NULL,
    Ngay_Nhap_Kho DATE NOT NULL,
    Is_Posted BIT NOT NULL CONSTRAINT DF_tbl_XNK_Nhap_Kho_Is_Posted DEFAULT 0,
    Posted_At DATETIME2 NULL,
    Ghi_Chu NVARCHAR(1000) NULL,
    Created DATETIME2 NOT NULL CONSTRAINT DF_tbl_XNK_Nhap_Kho_Created DEFAULT SYSUTCDATETIME(),
    Last_Updated DATETIME2 NOT NULL CONSTRAINT DF_tbl_XNK_Nhap_Kho_Last_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_tbl_XNK_Nhap_Kho_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID),
    CONSTRAINT FK_tbl_XNK_Nhap_Kho_NCC FOREIGN KEY (NCC_ID) REFERENCES dbo.tbl_DM_NCC(Auto_ID)
);
GO

IF OBJECT_ID(N'dbo.tbl_XNK_Nhap_Kho_Raw_Data', N'U') IS NULL
CREATE TABLE dbo.tbl_XNK_Nhap_Kho_Raw_Data
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_XNK_Nhap_Kho_Raw_Data PRIMARY KEY,
    Nhap_Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    SL_Nhap DECIMAL(18,3) NOT NULL,
    Don_Gia_Nhap DECIMAL(18,2) NOT NULL,
    CONSTRAINT CK_tbl_XNK_Nhap_Kho_Raw_Data_Quantity CHECK (SL_Nhap > 0),
    CONSTRAINT CK_tbl_XNK_Nhap_Kho_Raw_Data_Price CHECK (Don_Gia_Nhap > 0),
    CONSTRAINT FK_tbl_XNK_Nhap_Kho_Raw_Data_Header FOREIGN KEY (Nhap_Kho_ID) REFERENCES dbo.tbl_XNK_Nhap_Kho(Auto_ID) ON DELETE CASCADE,
    CONSTRAINT FK_tbl_XNK_Nhap_Kho_Raw_Data_Product FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

IF OBJECT_ID(N'dbo.tbl_XNK_Xuat_Kho', N'U') IS NULL
CREATE TABLE dbo.tbl_XNK_Xuat_Kho
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_XNK_Xuat_Kho PRIMARY KEY,
    So_Phieu_Xuat_Kho NVARCHAR(100) NOT NULL,
    Kho_ID BIGINT NOT NULL,
    Ngay_Xuat_Kho DATE NOT NULL,
    Is_Posted BIT NOT NULL CONSTRAINT DF_tbl_XNK_Xuat_Kho_Is_Posted DEFAULT 0,
    Posted_At DATETIME2 NULL,
    Ghi_Chu NVARCHAR(1000) NULL,
    Created DATETIME2 NOT NULL CONSTRAINT DF_tbl_XNK_Xuat_Kho_Created DEFAULT SYSUTCDATETIME(),
    Last_Updated DATETIME2 NOT NULL CONSTRAINT DF_tbl_XNK_Xuat_Kho_Last_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_tbl_XNK_Xuat_Kho_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID)
);
GO

IF OBJECT_ID(N'dbo.tbl_XNK_Xuat_Kho_Raw_Data', N'U') IS NULL
CREATE TABLE dbo.tbl_XNK_Xuat_Kho_Raw_Data
(
    Auto_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_XNK_Xuat_Kho_Raw_Data PRIMARY KEY,
    Xuat_Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    SL_Xuat DECIMAL(18,3) NOT NULL,
    Don_Gia_Xuat DECIMAL(18,2) NOT NULL,
    CONSTRAINT CK_tbl_XNK_Xuat_Kho_Raw_Data_Quantity CHECK (SL_Xuat > 0),
    CONSTRAINT CK_tbl_XNK_Xuat_Kho_Raw_Data_Price CHECK (Don_Gia_Xuat > 0),
    CONSTRAINT FK_tbl_XNK_Xuat_Kho_Raw_Data_Header FOREIGN KEY (Xuat_Kho_ID) REFERENCES dbo.tbl_XNK_Xuat_Kho(Auto_ID) ON DELETE CASCADE,
    CONSTRAINT FK_tbl_XNK_Xuat_Kho_Raw_Data_Product FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_DM_Don_Vi_Tinh_Ten') CREATE UNIQUE INDEX UX_tbl_DM_Don_Vi_Tinh_Ten ON dbo.tbl_DM_Don_Vi_Tinh(Ten_Don_Vi_Tinh);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_DM_Loai_San_Pham_Ma') CREATE UNIQUE INDEX UX_tbl_DM_Loai_San_Pham_Ma ON dbo.tbl_DM_Loai_San_Pham(Ma_LSP);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_DM_Loai_San_Pham_Ten') CREATE UNIQUE INDEX UX_tbl_DM_Loai_San_Pham_Ten ON dbo.tbl_DM_Loai_San_Pham(Ten_LSP);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_DM_San_Pham_Ma') CREATE UNIQUE INDEX UX_tbl_DM_San_Pham_Ma ON dbo.tbl_DM_San_Pham(Ma_San_Pham);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_DM_NCC_Ma') CREATE UNIQUE INDEX UX_tbl_DM_NCC_Ma ON dbo.tbl_DM_NCC(Ma_NCC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_DM_NCC_Ten') CREATE UNIQUE INDEX UX_tbl_DM_NCC_Ten ON dbo.tbl_DM_NCC(Ten_NCC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_DM_Kho_Ten') CREATE UNIQUE INDEX UX_tbl_DM_Kho_Ten ON dbo.tbl_DM_Kho(Ten_Kho);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_DM_Kho_User_Login_Kho') CREATE UNIQUE INDEX UX_tbl_DM_Kho_User_Login_Kho ON dbo.tbl_DM_Kho_User(Ma_Dang_Nhap, Kho_ID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_XNK_Nhap_Kho_So_Phieu') CREATE UNIQUE INDEX UX_tbl_XNK_Nhap_Kho_So_Phieu ON dbo.tbl_XNK_Nhap_Kho(So_Phieu_Nhap_Kho);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tbl_XNK_Xuat_Kho_So_Phieu') CREATE UNIQUE INDEX UX_tbl_XNK_Xuat_Kho_So_Phieu ON dbo.tbl_XNK_Xuat_Kho(So_Phieu_Xuat_Kho);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Kho_Ngay') CREATE INDEX IX_tbl_XNK_Nhap_Kho_Kho_Ngay ON dbo.tbl_XNK_Nhap_Kho(Kho_ID, Ngay_Nhap_Kho);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Kho_Ngay') CREATE INDEX IX_tbl_XNK_Xuat_Kho_Kho_Ngay ON dbo.tbl_XNK_Xuat_Kho(Kho_ID, Ngay_Xuat_Kho);

/* Performance indexes (benchmark-driven, 2026): raw-detail lookups drive document detail and
   Chi Tiet Nhap/Xuat reports; date columns drive date-range report filtering; San_Pham_ID drives
   the Xuat Nhap Ton aggregation join. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Raw_NhapKho_ID') CREATE INDEX IX_tbl_XNK_Nhap_Kho_Raw_NhapKho_ID ON dbo.tbl_XNK_Nhap_Kho_Raw_Data(Nhap_Kho_ID) INCLUDE (San_Pham_ID, SL_Nhap, Don_Gia_Nhap);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Raw_XuatKho_ID') CREATE INDEX IX_tbl_XNK_Xuat_Kho_Raw_XuatKho_ID ON dbo.tbl_XNK_Xuat_Kho_Raw_Data(Xuat_Kho_ID) INCLUDE (San_Pham_ID, SL_Xuat, Don_Gia_Xuat);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Ngay') CREATE INDEX IX_tbl_XNK_Nhap_Kho_Ngay ON dbo.tbl_XNK_Nhap_Kho(Ngay_Nhap_Kho) INCLUDE (Kho_ID, NCC_ID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Ngay') CREATE INDEX IX_tbl_XNK_Xuat_Kho_Ngay ON dbo.tbl_XNK_Xuat_Kho(Ngay_Xuat_Kho) INCLUDE (Kho_ID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Raw_SanPham_ID') CREATE INDEX IX_tbl_XNK_Nhap_Kho_Raw_SanPham_ID ON dbo.tbl_XNK_Nhap_Kho_Raw_Data(San_Pham_ID) INCLUDE (SL_Nhap, Don_Gia_Nhap);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Raw_SanPham_ID') CREATE INDEX IX_tbl_XNK_Xuat_Kho_Raw_SanPham_ID ON dbo.tbl_XNK_Xuat_Kho_Raw_Data(San_Pham_ID) INCLUDE (SL_Xuat, Don_Gia_Xuat);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Report_Page') CREATE INDEX IX_tbl_XNK_Nhap_Kho_Report_Page ON dbo.tbl_XNK_Nhap_Kho(Ngay_Nhap_Kho, So_Phieu_Nhap_Kho, Auto_ID) INCLUDE (Kho_ID, NCC_ID) WHERE Is_Posted = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Report_Page') CREATE INDEX IX_tbl_XNK_Xuat_Kho_Report_Page ON dbo.tbl_XNK_Xuat_Kho(Ngay_Xuat_Kho, So_Phieu_Xuat_Kho, Auto_ID) INCLUDE (Kho_ID) WHERE Is_Posted = 1;
GO

/* Warehouse audit contract. Existing databases are upgraded idempotently. */
IF COL_LENGTH(N'dbo.tbl_DM_Don_Vi_Tinh', N'Created_By') IS NULL ALTER TABLE dbo.tbl_DM_Don_Vi_Tinh ADD Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tbl_DM_Loai_San_Pham', N'Created_By') IS NULL ALTER TABLE dbo.tbl_DM_Loai_San_Pham ADD Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tbl_DM_San_Pham', N'Created_By') IS NULL ALTER TABLE dbo.tbl_DM_San_Pham ADD Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tbl_DM_NCC', N'Created_By') IS NULL ALTER TABLE dbo.tbl_DM_NCC ADD Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tbl_DM_Kho', N'Created_By') IS NULL ALTER TABLE dbo.tbl_DM_Kho ADD Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tbl_DM_Kho_User', N'Created_By') IS NULL ALTER TABLE dbo.tbl_DM_Kho_User ADD Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated DATETIME2 NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tbl_XNK_Nhap_Kho', N'Created_By') IS NULL ALTER TABLE dbo.tbl_XNK_Nhap_Kho ADD Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tbl_XNK_Nhap_Kho_Raw_Data', N'Created') IS NULL ALTER TABLE dbo.tbl_XNK_Nhap_Kho_Raw_Data ADD Created DATETIME2 NULL, Last_Updated DATETIME2 NULL, Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tbl_XNK_Xuat_Kho', N'Created_By') IS NULL ALTER TABLE dbo.tbl_XNK_Xuat_Kho ADD Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.tbl_XNK_Xuat_Kho_Raw_Data', N'Created') IS NULL ALTER TABLE dbo.tbl_XNK_Xuat_Kho_Raw_Data ADD Created DATETIME2 NULL, Last_Updated DATETIME2 NULL, Created_By NVARCHAR(100) NULL, Created_By_Function NVARCHAR(100) NULL, Last_Updated_By NVARCHAR(100) NULL, Last_Updated_By_Function NVARCHAR(100) NULL;
GO

/* A document remains a draft until it is explicitly posted. Existing documents
   predate this lifecycle and are therefore preserved as posted movements. */
IF COL_LENGTH(N'dbo.tbl_XNK_Nhap_Kho', N'Is_Posted') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_XNK_Nhap_Kho ADD Is_Posted BIT NOT NULL CONSTRAINT DF_tbl_XNK_Nhap_Kho_Is_Posted DEFAULT 1 WITH VALUES, Posted_At DATETIME2 NULL;
END
GO
IF COL_LENGTH(N'dbo.tbl_XNK_Xuat_Kho', N'Is_Posted') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_XNK_Xuat_Kho ADD Is_Posted BIT NOT NULL CONSTRAINT DF_tbl_XNK_Xuat_Kho_Is_Posted DEFAULT 1 WITH VALUES, Posted_At DATETIME2 NULL;
END
GO

IF OBJECT_ID(N'dbo.InventoryBalance_Current', N'U') IS NULL
CREATE TABLE dbo.InventoryBalance_Current
(
    Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    CurrentQuantity DECIMAL(18,3) NOT NULL,
    UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryBalance_Current_UpdatedAt DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT PK_InventoryBalance_Current PRIMARY KEY (Kho_ID, San_Pham_ID),
    CONSTRAINT CK_InventoryBalance_Current_NonNegative CHECK (CurrentQuantity >= 0),
    CONSTRAINT FK_InventoryBalance_Current_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID),
    CONSTRAINT FK_InventoryBalance_Current_San_Pham FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

/* End-of-day on-hand balance used as the starting point for historical inventory reports.
   The rows are immutable for a given business date; a later back-dated posting invalidates
   affected dates through dbo.sp_Inventory_Snapshot_Invalidate_From. */
IF OBJECT_ID(N'dbo.InventoryBalance_Snapshot_Daily', N'U') IS NULL
CREATE TABLE dbo.InventoryBalance_Snapshot_Daily
(
    ID BIGINT IDENTITY(1,1) NOT NULL,
    Snapshot_Date DATE NOT NULL,
    Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    ClosingQuantity DECIMAL(18,3) NOT NULL,
    IsValid BIT NOT NULL CONSTRAINT DF_InventoryBalance_Snapshot_Daily_IsValid DEFAULT (1),
    InvalidatedAt DATETIME2 NULL,
    InvalidReason NVARCHAR(100) NULL,
    [Version] INT NOT NULL CONSTRAINT DF_InventoryBalance_Snapshot_Daily_Version DEFAULT (1),
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryBalance_Snapshot_Daily_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_InventoryBalance_Snapshot_Daily PRIMARY KEY (Snapshot_Date, Kho_ID, San_Pham_ID),
    CONSTRAINT UQ_InventoryBalance_Snapshot_Daily_ID UNIQUE (ID),
    CONSTRAINT FK_InventoryBalance_Snapshot_Daily_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID),
    CONSTRAINT FK_InventoryBalance_Snapshot_Daily_San_Pham FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

/* Snapshot lifecycle migration. Existing rows remain usable snapshots and are
   upgraded as valid version 1 rows; later back-dated changes invalidate them
   in place instead of deleting historical evidence. */
IF COL_LENGTH(N'dbo.InventoryBalance_Snapshot_Daily', N'ID') IS NULL
    ALTER TABLE dbo.InventoryBalance_Snapshot_Daily ADD ID BIGINT IDENTITY(1,1) NOT NULL;
IF COL_LENGTH(N'dbo.InventoryBalance_Snapshot_Daily', N'IsValid') IS NULL
    ALTER TABLE dbo.InventoryBalance_Snapshot_Daily ADD IsValid BIT NOT NULL CONSTRAINT DF_InventoryBalance_Snapshot_Daily_IsValid DEFAULT (1) WITH VALUES;
IF COL_LENGTH(N'dbo.InventoryBalance_Snapshot_Daily', N'InvalidatedAt') IS NULL
    ALTER TABLE dbo.InventoryBalance_Snapshot_Daily ADD InvalidatedAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.InventoryBalance_Snapshot_Daily', N'InvalidReason') IS NULL
    ALTER TABLE dbo.InventoryBalance_Snapshot_Daily ADD InvalidReason NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.InventoryBalance_Snapshot_Daily', N'Version') IS NULL
    ALTER TABLE dbo.InventoryBalance_Snapshot_Daily ADD [Version] INT NOT NULL CONSTRAINT DF_InventoryBalance_Snapshot_Daily_Version DEFAULT (1) WITH VALUES;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.InventoryBalance_Snapshot_Daily')
      AND name = N'UQ_InventoryBalance_Snapshot_Daily_ID'
)
    ALTER TABLE dbo.InventoryBalance_Snapshot_Daily
        ADD CONSTRAINT UQ_InventoryBalance_Snapshot_Daily_ID UNIQUE (ID);
GO

IF OBJECT_ID(N'dbo.InventorySnapshot_RebuildQueue', N'U') IS NULL
CREATE TABLE dbo.InventorySnapshot_RebuildQueue
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventorySnapshot_RebuildQueue PRIMARY KEY,
    Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    From_Date DATE NOT NULL,
    Status NVARCHAR(20) NOT NULL CONSTRAINT DF_InventorySnapshot_RebuildQueue_Status DEFAULT (N'WAITING'),
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventorySnapshot_RebuildQueue_CreatedAt DEFAULT SYSUTCDATETIME(),
    CompletedAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(4000) NULL,
    CONSTRAINT CK_InventorySnapshot_RebuildQueue_Status CHECK (Status IN (N'WAITING', N'PROCESSING', N'COMPLETED', N'FAILED')),
    CONSTRAINT FK_InventorySnapshot_RebuildQueue_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID),
    CONSTRAINT FK_InventorySnapshot_RebuildQueue_San_Pham FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

/* Report fallback is deliberately observable without changing the existing
   report result-set contract consumed by Blazor/Telerik. */
IF OBJECT_ID(N'dbo.InventorySnapshot_ReportFallbackLog', N'U') IS NULL
CREATE TABLE dbo.InventorySnapshot_ReportFallbackLog
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventorySnapshot_ReportFallbackLog PRIMARY KEY,
    ReportFromDate DATE NOT NULL,
    ReportToDate DATE NOT NULL,
    Ma_Dang_Nhap NVARCHAR(100) NULL,
    SnapshotMissingReason NVARCHAR(200) NOT NULL,
    LoggedAt DATETIME2 NOT NULL CONSTRAINT DF_InventorySnapshot_ReportFallbackLog_LoggedAt DEFAULT SYSUTCDATETIME()
);
GO

IF TYPE_ID(N'dbo.InventorySnapshotAffectedType') IS NULL
    EXEC(N'CREATE TYPE dbo.InventorySnapshotAffectedType AS TABLE
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        From_Date DATE NOT NULL,
        InvalidReason NVARCHAR(100) NOT NULL,
        PRIMARY KEY (Kho_ID, San_Pham_ID, From_Date, InvalidReason)
    );');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_InventorySnapshot_RebuildQueue_Active_Scope')
    CREATE UNIQUE INDEX UX_InventorySnapshot_RebuildQueue_Active_Scope
    ON dbo.InventorySnapshot_RebuildQueue(Kho_ID, San_Pham_ID, From_Date)
    WHERE Status IN (N'WAITING', N'PROCESSING');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventorySnapshot_RebuildQueue_Status_CreatedAt')
    CREATE INDEX IX_InventorySnapshot_RebuildQueue_Status_CreatedAt
    ON dbo.InventorySnapshot_RebuildQueue(Status, CreatedAt, ID)
    INCLUDE (Kho_ID, San_Pham_ID, From_Date, ErrorMessage);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryBalance_Snapshot_Daily_Scope')
    CREATE INDEX IX_InventoryBalance_Snapshot_Daily_Scope
    ON dbo.InventoryBalance_Snapshot_Daily(Kho_ID, San_Pham_ID, Snapshot_Date)
    INCLUDE (ClosingQuantity, IsValid, InvalidatedAt, InvalidReason, [Version]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryBalance_Snapshot_Daily_Valid_Date')
    CREATE INDEX IX_InventoryBalance_Snapshot_Daily_Valid_Date
    ON dbo.InventoryBalance_Snapshot_Daily(Snapshot_Date, Kho_ID, San_Pham_ID)
    INCLUDE (ClosingQuantity, [Version])
    WHERE IsValid = 1;
GO

/* A historical report can legitimately be negative when a document is posted
   later with an earlier accounting date.  Only the live current balance is
   constrained to be non-negative. */
IF OBJECT_ID(N'dbo.CK_InventoryBalance_Snapshot_Daily_NonNegative', N'C') IS NOT NULL
    ALTER TABLE dbo.InventoryBalance_Snapshot_Daily DROP CONSTRAINT CK_InventoryBalance_Snapshot_Daily_NonNegative;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Posted_Kho_Ngay') CREATE INDEX IX_tbl_XNK_Nhap_Kho_Posted_Kho_Ngay ON dbo.tbl_XNK_Nhap_Kho(Is_Posted, Kho_ID, Ngay_Nhap_Kho);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Posted_Kho_Ngay') CREATE INDEX IX_tbl_XNK_Xuat_Kho_Posted_Kho_Ngay ON dbo.tbl_XNK_Xuat_Kho(Is_Posted, Kho_ID, Ngay_Xuat_Kho);
GO

/* Daily receipt/issue materialization. The clustered business key is also the
   covering report access path, so a redundant nonclustered copy is avoided. */
IF OBJECT_ID(N'dbo.Inventory_Movement_Daily', N'U') IS NULL
CREATE TABLE dbo.Inventory_Movement_Daily
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT UQ_Inventory_Movement_Daily_ID UNIQUE,
    Movement_Date DATE NOT NULL,
    Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    Total_Receipt DECIMAL(18,3) NOT NULL CONSTRAINT DF_Inventory_Movement_Daily_Total_Receipt DEFAULT (0),
    Total_Issue DECIMAL(18,3) NOT NULL CONSTRAINT DF_Inventory_Movement_Daily_Total_Issue DEFAULT (0),
    IsValid BIT NOT NULL CONSTRAINT DF_Inventory_Movement_Daily_IsValid DEFAULT (1),
    InvalidatedAt DATETIME2 NULL,
    InvalidReason NVARCHAR(100) NULL,
    [Version] INT NOT NULL CONSTRAINT DF_Inventory_Movement_Daily_Version DEFAULT (1),
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Inventory_Movement_Daily_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_Inventory_Movement_Daily_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Inventory_Movement_Daily PRIMARY KEY CLUSTERED (Movement_Date, Kho_ID, San_Pham_ID),
    CONSTRAINT CK_Inventory_Movement_Daily_NonNegative CHECK (Total_Receipt >= 0 AND Total_Issue >= 0),
    CONSTRAINT FK_Inventory_Movement_Daily_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID),
    CONSTRAINT FK_Inventory_Movement_Daily_San_Pham FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

/* Queue ranges coalesce rapid edits for a scope. A normal posted document has
   From_Date = To_Date; a range is retained for future correction workflows. */
IF OBJECT_ID(N'dbo.InventoryMovement_RebuildQueue', N'U') IS NULL
CREATE TABLE dbo.InventoryMovement_RebuildQueue
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryMovement_RebuildQueue PRIMARY KEY,
    Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    From_Date DATE NOT NULL,
    To_Date DATE NOT NULL,
    Status NVARCHAR(20) NOT NULL CONSTRAINT DF_InventoryMovement_RebuildQueue_Status DEFAULT (N'WAITING'),
    Retry_Count INT NOT NULL CONSTRAINT DF_InventoryMovement_RebuildQueue_Retry_Count DEFAULT (0),
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryMovement_RebuildQueue_CreatedAt DEFAULT SYSUTCDATETIME(),
    ProcessedAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(4000) NULL,
    CONSTRAINT CK_InventoryMovement_RebuildQueue_DateRange CHECK (From_Date <= To_Date),
    CONSTRAINT CK_InventoryMovement_RebuildQueue_Status CHECK (Status IN (N'WAITING', N'PROCESSING', N'COMPLETED', N'FAILED')),
    CONSTRAINT FK_InventoryMovement_RebuildQueue_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID),
    CONSTRAINT FK_InventoryMovement_RebuildQueue_San_Pham FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

IF TYPE_ID(N'dbo.InventoryMovementAffectedType') IS NULL
    EXEC(N'CREATE TYPE dbo.InventoryMovementAffectedType AS TABLE
    (
        Kho_ID BIGINT NOT NULL,
        San_Pham_ID BIGINT NOT NULL,
        Movement_Date DATE NOT NULL,
        InvalidReason NVARCHAR(100) NOT NULL,
        PRIMARY KEY (Kho_ID, San_Pham_ID, Movement_Date, InvalidReason)
    );');
GO

/* This state prevents a newly deployed but unseeded aggregate from silently
   returning incomplete reports. Bootstrap must complete once before cutover. */
IF OBJECT_ID(N'dbo.InventoryMovement_AggregateState', N'U') IS NULL
CREATE TABLE dbo.InventoryMovement_AggregateState
(
    State_ID TINYINT NOT NULL CONSTRAINT PK_InventoryMovement_AggregateState PRIMARY KEY,
    IsInitialized BIT NOT NULL CONSTRAINT DF_InventoryMovement_AggregateState_IsInitialized DEFAULT (0),
    InitializedAt DATETIME2 NULL,
    LastReconciledAt DATETIME2 NULL,
    CONSTRAINT CK_InventoryMovement_AggregateState_Singleton CHECK (State_ID = 1)
);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.InventoryMovement_AggregateState WHERE State_ID = 1)
    INSERT dbo.InventoryMovement_AggregateState(State_ID, IsInitialized) VALUES (1, 0);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryMovement_RebuildQueue_Status_CreatedAt')
    CREATE INDEX IX_InventoryMovement_RebuildQueue_Status_CreatedAt
    ON dbo.InventoryMovement_RebuildQueue(Status, CreatedAt, ID)
    INCLUDE (Kho_ID, San_Pham_ID, From_Date, To_Date, Retry_Count, ErrorMessage);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryMovement_RebuildQueue_ActiveScope')
    CREATE INDEX IX_InventoryMovement_RebuildQueue_ActiveScope
    ON dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, Status, From_Date, To_Date, ID);
GO

/* Phase 2 reliability migration.  A queue row is the durable claim token for
   one daily warehouse/product rebuild.  Request_Version prevents an older
   worker from completing work that a newer invalidation has superseded. */
IF COL_LENGTH(N'dbo.InventoryMovement_RebuildQueue', N'LastAttemptAt') IS NULL
    ALTER TABLE dbo.InventoryMovement_RebuildQueue ADD LastAttemptAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.InventoryMovement_RebuildQueue', N'NextRetryAt') IS NULL
    ALTER TABLE dbo.InventoryMovement_RebuildQueue ADD NextRetryAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.InventoryMovement_RebuildQueue', N'LastError') IS NULL
    ALTER TABLE dbo.InventoryMovement_RebuildQueue ADD LastError NVARCHAR(4000) NULL;
IF COL_LENGTH(N'dbo.InventoryMovement_RebuildQueue', N'Requested_Version') IS NULL
    ALTER TABLE dbo.InventoryMovement_RebuildQueue ADD Requested_Version INT NOT NULL CONSTRAINT DF_InventoryMovement_RebuildQueue_RequestedVersion DEFAULT (1);
IF COL_LENGTH(N'dbo.InventoryMovement_RebuildQueue', N'Claimed_Version') IS NULL
    ALTER TABLE dbo.InventoryMovement_RebuildQueue ADD Claimed_Version INT NULL;
GO

/* Legacy FAILED rows are made actionable again or preserved as a visible final
   failure.  No old failure is silently discarded during migration. */
UPDATE dbo.InventoryMovement_RebuildQueue
SET Status = N'RETRY_WAITING',
    NextRetryAt = COALESCE(ProcessedAt, CreatedAt, SYSUTCDATETIME()),
    LastError = COALESCE(LastError, ErrorMessage, N'Legacy worker failure awaiting retry.'),
    ErrorMessage = NULL
WHERE Status = N'FAILED'
  AND Retry_Count < 3;

UPDATE dbo.InventoryMovement_RebuildQueue
SET Status = N'FAILED_FINAL',
    LastError = COALESCE(LastError, ErrorMessage, N'Legacy worker failure reached retry limit.'),
    ErrorMessage = NULL
WHERE Status = N'FAILED'
  AND Retry_Count >= 3;
GO

/* A legacy range queue can have duplicate active rows for its first day. Keep
   the oldest in-flight row, mark the others superseded, and advance its request
   version so the active worker must requeue before it can declare completion. */
;WITH ActiveDuplicates AS
(
    SELECT ID,
           Kho_ID,
           San_Pham_ID,
           From_Date,
           ROW_NUMBER() OVER
           (
               PARTITION BY Kho_ID, San_Pham_ID, From_Date
               ORDER BY CASE WHEN Status = N'PROCESSING' THEN 0 ELSE 1 END, ID
           ) AS Row_Number,
           COUNT(*) OVER (PARTITION BY Kho_ID, San_Pham_ID, From_Date) AS Duplicate_Count
    FROM dbo.InventoryMovement_RebuildQueue
    WHERE Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING')
)
UPDATE q
SET Requested_Version = q.Requested_Version + d.Duplicate_Count - 1
FROM dbo.InventoryMovement_RebuildQueue q
JOIN ActiveDuplicates d ON d.ID = q.ID
WHERE d.Row_Number = 1
  AND d.Duplicate_Count > 1;

;WITH ActiveDuplicates AS
(
    SELECT ID,
           ROW_NUMBER() OVER
           (
               PARTITION BY Kho_ID, San_Pham_ID, From_Date
               ORDER BY CASE WHEN Status = N'PROCESSING' THEN 0 ELSE 1 END, ID
           ) AS Row_Number
    FROM dbo.InventoryMovement_RebuildQueue
    WHERE Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING')
)
UPDATE q
SET Status = N'SUPERSEDED',
    ProcessedAt = SYSUTCDATETIME(),
    LastError = N'Superseded during queue lifecycle migration.',
    ErrorMessage = NULL,
    NextRetryAt = NULL
FROM dbo.InventoryMovement_RebuildQueue q
JOIN ActiveDuplicates d ON d.ID = q.ID
WHERE d.Row_Number > 1;
GO

IF OBJECT_ID(N'dbo.CK_InventoryMovement_RebuildQueue_Status', N'C') IS NOT NULL
    ALTER TABLE dbo.InventoryMovement_RebuildQueue DROP CONSTRAINT CK_InventoryMovement_RebuildQueue_Status;
ALTER TABLE dbo.InventoryMovement_RebuildQueue ADD CONSTRAINT CK_InventoryMovement_RebuildQueue_Status
    CHECK (Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING', N'COMPLETED', N'FAILED_FINAL', N'SUPERSEDED'));
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.InventoryMovement_RebuildQueue') AND name = N'UX_InventoryMovement_RebuildQueue_ActiveDailyScope')
    DROP INDEX UX_InventoryMovement_RebuildQueue_ActiveDailyScope ON dbo.InventoryMovement_RebuildQueue;
CREATE UNIQUE INDEX UX_InventoryMovement_RebuildQueue_ActiveDailyScope
    ON dbo.InventoryMovement_RebuildQueue(Kho_ID, San_Pham_ID, From_Date)
    WHERE Status IN (N'WAITING', N'PROCESSING', N'RETRY_WAITING');
GO

IF OBJECT_ID(N'dbo.InventoryMovement_RebuildDeadLetter', N'U') IS NULL
CREATE TABLE dbo.InventoryMovement_RebuildDeadLetter
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InventoryMovement_RebuildDeadLetter PRIMARY KEY,
    Queue_ID BIGINT NOT NULL CONSTRAINT UQ_InventoryMovement_RebuildDeadLetter_Queue UNIQUE,
    Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    From_Date DATE NOT NULL,
    To_Date DATE NOT NULL,
    Retry_Count INT NOT NULL,
    LastError NVARCHAR(4000) NOT NULL,
    FailedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryMovement_RebuildDeadLetter_FailedAt DEFAULT SYSUTCDATETIME(),
    ResolvedAt DATETIME2 NULL,
    ResolutionNote NVARCHAR(4000) NULL,
    CONSTRAINT FK_InventoryMovement_RebuildDeadLetter_Queue FOREIGN KEY (Queue_ID) REFERENCES dbo.InventoryMovement_RebuildQueue(ID),
    CONSTRAINT FK_InventoryMovement_RebuildDeadLetter_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID),
    CONSTRAINT FK_InventoryMovement_RebuildDeadLetter_San_Pham FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryMovement_RebuildQueue_Ready')
    CREATE INDEX IX_InventoryMovement_RebuildQueue_Ready
    ON dbo.InventoryMovement_RebuildQueue(Status, NextRetryAt, CreatedAt, ID)
    INCLUDE (Kho_ID, San_Pham_ID, From_Date, To_Date, Retry_Count, Requested_Version, Claimed_Version);
GO

/* The assignment uses tbl_DM_* in Bài 7/11 and tbl_XNK_* in Bài 8/12. XNK is canonical; synonyms retain both documented names. */
IF OBJECT_ID(N'dbo.tbl_DM_Nhap_Kho', N'SN') IS NULL EXEC(N'CREATE SYNONYM dbo.tbl_DM_Nhap_Kho FOR dbo.tbl_XNK_Nhap_Kho;');
IF OBJECT_ID(N'dbo.tbl_DM_Nhap_Kho_Raw_Data', N'SN') IS NULL EXEC(N'CREATE SYNONYM dbo.tbl_DM_Nhap_Kho_Raw_Data FOR dbo.tbl_XNK_Nhap_Kho_Raw_Data;');
IF OBJECT_ID(N'dbo.tbl_DM_Xuat_Kho', N'SN') IS NULL EXEC(N'CREATE SYNONYM dbo.tbl_DM_Xuat_Kho FOR dbo.tbl_XNK_Xuat_Kho;');
IF OBJECT_ID(N'dbo.tbl_DM_Xuat_Kho_Raw_Data', N'SN') IS NULL EXEC(N'CREATE SYNONYM dbo.tbl_DM_Xuat_Kho_Raw_Data FOR dbo.tbl_XNK_Xuat_Kho_Raw_Data;');
GO

/* Draft issue reservations. CurrentQuantity remains posted/on-hand stock;
   ReservedQuantity is the active quantity held by draft issue details. */
IF COL_LENGTH(N'dbo.InventoryBalance_Current', N'ReservedQuantity') IS NULL
BEGIN
    ALTER TABLE dbo.InventoryBalance_Current
        ADD ReservedQuantity DECIMAL(18,3) NOT NULL
            CONSTRAINT DF_InventoryBalance_Current_ReservedQuantity DEFAULT (0) WITH VALUES;
END
GO

IF OBJECT_ID(N'dbo.InventoryReservation_Current', N'U') IS NULL
CREATE TABLE dbo.InventoryReservation_Current
(
    Xuat_Kho_Detail_ID BIGINT NOT NULL,
    Kho_ID BIGINT NOT NULL,
    San_Pham_ID BIGINT NOT NULL,
    ReservedQuantity DECIMAL(18,3) NOT NULL,
    UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryReservation_Current_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_InventoryReservation_Current PRIMARY KEY (Xuat_Kho_Detail_ID),
    CONSTRAINT CK_InventoryReservation_Current_Positive CHECK (ReservedQuantity > 0),
    CONSTRAINT FK_InventoryReservation_Current_Detail FOREIGN KEY (Xuat_Kho_Detail_ID) REFERENCES dbo.tbl_XNK_Xuat_Kho_Raw_Data(Auto_ID) ON DELETE CASCADE,
    CONSTRAINT FK_InventoryReservation_Current_Kho FOREIGN KEY (Kho_ID) REFERENCES dbo.tbl_DM_Kho(Auto_ID),
    CONSTRAINT FK_InventoryReservation_Current_San_Pham FOREIGN KEY (San_Pham_ID) REFERENCES dbo.tbl_DM_San_Pham(Auto_ID)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InventoryReservation_Current_Kho_Product')
    CREATE INDEX IX_InventoryReservation_Current_Kho_Product ON dbo.InventoryReservation_Current(Kho_ID, San_Pham_ID) INCLUDE (ReservedQuantity);
GO
