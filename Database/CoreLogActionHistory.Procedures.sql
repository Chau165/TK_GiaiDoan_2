/*
    Shared history lookup used by system information dialogs.
    The caller provides the Auto_ID of the displayed entity as Ref_ID.
*/
CREATE OR ALTER PROCEDURE dbo.FCommon_Sys_sp_sel_List_Log_Record_Action_History
    @Ref_ID BIGINT
WITH RECOMPILE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.view_Log_Record_Action_History WITH (NOLOCK)
    WHERE Ref_ID = @Ref_ID
    ORDER BY Created DESC, Auto_ID DESC;
END
GO
