


CREATE PROCEDURE [dbo].[FQ_104_CH_sp_sel_List_For_Cache]
	with recompile
AS
BEGIN

	SELECT Auto_ID, Ma_Chu_Hang, Ten_Viet_Tat, Ten_Chu_Hang, Dia_Chi, Dien_Thoai, Email, MST, 
		Header_Title, Ghi_Chu, Image_URL_Thumb, Image_URL
	FROM 
		view_DM_Chu_Hang with (nolock) 
END
