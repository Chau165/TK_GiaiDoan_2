
CREATE   PROCEDURE dbo.F2009_sp_sel_List_Kho_By_Created
    @Date_From_Utc DATETIME2(7) = NULL,
    @Date_To_Utc_Exclusive DATETIME2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Date_From_Utc IS NOT NULL
       AND @Date_To_Utc_Exclusive IS NOT NULL
       AND @Date_From_Utc >= @Date_To_Utc_Exclusive
        THROW 51001, N'Ngày bắt đầu không được lớn hơn ngày kết thúc.', 1;

    SELECT Auto_ID, Ten_Kho, Ghi_Chu, Created
    FROM dbo.tbl_DM_Kho
    WHERE (@Date_From_Utc IS NULL OR Created >= @Date_From_Utc)
      AND (@Date_To_Utc_Exclusive IS NULL OR Created < @Date_To_Utc_Exclusive)
    ORDER BY Ten_Kho;
END;
