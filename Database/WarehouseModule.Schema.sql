/* Run against TKS_Thuc_Tap_V11_GiaiDoan2. */
SET NOCOUNT ON;

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

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Nhap_Kho_Posted_Kho_Ngay') CREATE INDEX IX_tbl_XNK_Nhap_Kho_Posted_Kho_Ngay ON dbo.tbl_XNK_Nhap_Kho(Is_Posted, Kho_ID, Ngay_Nhap_Kho);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tbl_XNK_Xuat_Kho_Posted_Kho_Ngay') CREATE INDEX IX_tbl_XNK_Xuat_Kho_Posted_Kho_Ngay ON dbo.tbl_XNK_Xuat_Kho(Is_Posted, Kho_ID, Ngay_Xuat_Kho);
GO

/* The assignment uses tbl_DM_* in Bài 7/11 and tbl_XNK_* in Bài 8/12. XNK is canonical; synonyms retain both documented names. */
IF OBJECT_ID(N'dbo.tbl_DM_Nhap_Kho', N'SN') IS NULL EXEC(N'CREATE SYNONYM dbo.tbl_DM_Nhap_Kho FOR dbo.tbl_XNK_Nhap_Kho;');
IF OBJECT_ID(N'dbo.tbl_DM_Nhap_Kho_Raw_Data', N'SN') IS NULL EXEC(N'CREATE SYNONYM dbo.tbl_DM_Nhap_Kho_Raw_Data FOR dbo.tbl_XNK_Nhap_Kho_Raw_Data;');
IF OBJECT_ID(N'dbo.tbl_DM_Xuat_Kho', N'SN') IS NULL EXEC(N'CREATE SYNONYM dbo.tbl_DM_Xuat_Kho FOR dbo.tbl_XNK_Xuat_Kho;');
IF OBJECT_ID(N'dbo.tbl_DM_Xuat_Kho_Raw_Data', N'SN') IS NULL EXEC(N'CREATE SYNONYM dbo.tbl_DM_Xuat_Kho_Raw_Data FOR dbo.tbl_XNK_Xuat_Kho_Raw_Data;');
GO
