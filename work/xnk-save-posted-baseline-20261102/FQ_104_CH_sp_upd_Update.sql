


CREATE PROCEDURE [dbo].[FQ_104_CH_sp_upd_Update]
	@Auto_ID bigint,
	@Ma_Chu_Hang nvarchar(50),
	@Ten_Viet_Tat nvarchar(200),
	@Ten_Chu_Hang nvarchar(200),
	@Dia_Chi nvarchar(200),
	@Dien_Thoai nvarchar(200),
	@Email nvarchar(200),
	@MST nvarchar(50),
	@Header_Title nvarchar(50),
	@Ghi_Chu nvarchar(200),
	@Image_URL_Thumb nvarchar(200),
	@Image_URL nvarchar(200),
	@Last_Updated_By nvarchar(50),
	@Last_Updated_By_Function nvarchar(50)
	with recompile
AS
BEGIN
	SET NOCOUNT ON;

	set @Ma_Chu_Hang = LTRIM(RTRIM(@Ma_Chu_Hang))
	set @Ten_Viet_Tat = LTRIM(RTRIM(@Ten_Viet_Tat))
	set @Ten_Chu_Hang = LTRIM(RTRIM(@Ten_Chu_Hang))
	set @Dia_Chi = LTRIM(RTRIM(@Dia_Chi))
	set @Dien_Thoai = LTRIM(RTRIM(@Dien_Thoai))
	set @Email = LTRIM(RTRIM(@Email))
	set @MST = LTRIM(RTRIM(@MST))
	set @Header_Title = LTRIM(RTRIM(@Header_Title))
	set @Ghi_Chu = LTRIM(RTRIM(@Ghi_Chu))
	set @Image_URL_Thumb = LTRIM(RTRIM(@Image_URL_Thumb))
	set @Image_URL = LTRIM(RTRIM(@Image_URL))

	if (isnull(@Ma_Chu_Hang, '') = '')
	begin
		RAISERROR(N'Vui lòng nhập Mã chủ hàng.', 11, 1)
		return
	end

	if (isnull(@Ten_Viet_Tat, '') = '')
	begin
		RAISERROR(N'Vui lòng nhập Tên viết tắt.', 11, 1)
		return
	end

	if (isnull(@Ten_Chu_Hang, '') = '')
	begin
		RAISERROR(N'Vui lòng nhập Tên chủ hàng.', 11, 1)
		return
	end

	declare @Check_ID_1 bigint = (select top 1 Auto_ID from view_DM_Chu_Hang with (nolock) where Ma_Chu_Hang = @Ma_Chu_Hang and Auto_ID <> @Auto_ID)
	if(isnull(@Check_ID_1, 0) > 0)
	begin
		RAISERROR(N'Mã chủ hàng đã tồn tại.', 11, 1)
		return
	end

	declare @Check_ID_2 bigint = (select top 1 Auto_ID from view_DM_Chu_Hang with (nolock) where Ten_Viet_Tat = @Ten_Viet_Tat and Auto_ID <> @Auto_ID)
	if(isnull(@Check_ID_2, 0) > 0)
	begin
		RAISERROR(N'Tên viết tắt đã tồn tại.', 11, 1)
		return
	end

	declare @Check_ID_3 bigint = (select top 1 Auto_ID from view_DM_Chu_Hang with (nolock) where Ten_Chu_Hang = @Ten_Chu_Hang and Auto_ID <> @Auto_ID)
	if(isnull(@Check_ID_3, 0) > 0)
	begin
		RAISERROR(N'Tên chủ hàng đã tồn tại.', 11, 1)
		return
	end

	if (isnull(@MST, '') <> '')
	begin
		declare @Auto_ID_Exits int
		set @Auto_ID_Exits = (select Auto_ID from view_DM_Chu_Hang  where MST = @MST and Auto_ID <> @Auto_ID)

		if(isnull(@Auto_ID_Exits, 0) > 0)
		begin
			RAISERROR(N'MST đã tồn tại, xin vui lòng nhập lại tên khác.', 11, 1)
			return
		end 
	end
	UPDATE tbl_DM_Chu_Hang SET
		Ma_Chu_Hang = @Ma_Chu_Hang,
		Ten_Viet_Tat = @Ten_Viet_Tat,
		Ten_Chu_Hang = @Ten_Chu_Hang,
		Dia_Chi = @Dia_Chi,
		Dien_Thoai = @Dien_Thoai,
		Email = @Email,
		MST = @MST,
		Header_Title = @Header_Title,
		Ghi_Chu = @Ghi_Chu,
		Image_URL_Thumb = @Image_URL_Thumb,
		Image_URL = @Image_URL,
		Last_Updated = getdate(),
		Last_Updated_By = @Last_Updated_By,
		Last_Updated_By_Function = @Last_Updated_By_Function
	WHERE
		Auto_ID = @Auto_ID
END

