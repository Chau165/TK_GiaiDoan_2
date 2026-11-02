


CREATE PROCEDURE [dbo].[FQ_104_CH_sp_del_Delete_By_ID]
	@Auto_ID bigint,
	@Last_Updated_By nvarchar(50),
	@Last_Updated_By_Function nvarchar(50)
	with recompile
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE tbl_DM_Chu_Hang SET
		deleted = 1,
		Last_Updated = getdate(),
		Last_Updated_By = @Last_Updated_By,
		Last_Updated_By_Function = @Last_Updated_By_Function
	WHERE
		Auto_ID = @Auto_ID
END
