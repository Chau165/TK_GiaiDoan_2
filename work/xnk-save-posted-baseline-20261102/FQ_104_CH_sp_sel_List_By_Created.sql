


CREATE PROCEDURE [dbo].[FQ_104_CH_sp_sel_List_By_Created]
	@Date_From datetime,
	@Date_To datetime
	with recompile
AS
BEGIN
	set @Date_From = dbo.fnConvert_To_Dau_Ngay(@Date_From)
	set @Date_To = dbo.fnConvert_To_Cuoi_Ngay(@Date_To)

	SELECT Auto_ID, Ma_Chu_Hang, Ten_Viet_Tat, Ten_Chu_Hang, Dia_Chi, Dien_Thoai, Email, MST, Header_Title, Ghi_Chu, Image_URL_Thumb, Image_URL
	FROM 
		view_DM_Chu_Hang with (nolock) 
	WHERE 
		Created between @Date_From and @Date_To
	ORDER BY Auto_ID DESC
END
