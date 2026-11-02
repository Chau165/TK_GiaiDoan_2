


CREATE PROCEDURE [dbo].[FQ_104_CH_sp_sel_Get_By_ID]
	@Auto_ID bigint
	with recompile
AS
BEGIN
	SET NOCOUNT ON;

	SELECT * FROM view_DM_Chu_Hang with (nolock)
	WHERE
		Auto_ID = @Auto_ID
END
