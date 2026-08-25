/*
  Run:
  sqlcmd -S localhost -E -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 -i Database\Tests\WarehouseModule.UnicodeDataTests.sql

  This is a read-only regression test for Unicode data loaded by the warehouse
  sample/menu scripts. It fails when UTF-8 text has been imported through an
  ANSI code page and stored as mojibake.
*/
SET NOCOUNT ON;

DECLARE @SampleNote NVARCHAR(200) = N'D' + NCHAR(7919) + N' li' + NCHAR(7879) + N'u m' + NCHAR(7851) + N'u';
DECLARE @ManageName NVARCHAR(200) = N'Qu' + NCHAR(7843) + N'n l' + NCHAR(253) + N' kho';
DECLARE @ManageNote NVARCHAR(200) = N'B' + NCHAR(224) + N'i t' + NCHAR(7853) + N'p th' + NCHAR(7921) + N'c t' + NCHAR(7853) + N'p giai ' + NCHAR(273) + N'o' + NCHAR(7841) + N'n 2';
DECLARE @ParentNote NVARCHAR(200) = N'Module th' + NCHAR(7921) + N'c t' + NCHAR(7853) + N'p qu' + NCHAR(7843) + N'n l' + NCHAR(253) + N' kho';

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.tbl_DM_Don_Vi_Tinh
    WHERE Ten_Don_Vi_Tinh = N'Chai' AND Ghi_Chu = @SampleNote
)
    THROW 52200, 'Unicode regression: unit Chai/note is not stored correctly.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.tbl_Sys_Chuc_Nang
    WHERE Auto_ID = 110000100 AND Ten_Chuc_Nang = N'Kho' AND Ghi_Chu = @ParentNote
)
    THROW 52201, 'Unicode regression: warehouse menu parent is not stored correctly.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.tbl_Sys_Chuc_Nang
    WHERE Auto_ID = 110000101 AND Ten_Chuc_Nang = @ManageName AND Ghi_Chu = @ManageNote
)
    THROW 52202, 'Unicode regression: warehouse menu item is not stored correctly.', 1;

PRINT 'PASS: Warehouse module Unicode data tests';
