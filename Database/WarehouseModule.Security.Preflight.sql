/* Read-only deployment gate.  Run this script as a deployment administrator
   after WarehouseModule.Security.sql, supplying the exact application login
   and database user.  It never creates users, grants permissions, or prints
   credentials.  For an unresolved identity pass both SQLCMD variables as
   __APPLICATION_PRINCIPAL_NOT_VERIFIED__; the gate must then fail closed. */
SET NOCOUNT ON;

DECLARE @ApplicationLogin sysname = NULLIF(N'$(ApplicationLogin)', N'__APPLICATION_PRINCIPAL_NOT_VERIFIED__');
DECLARE @ApplicationUser sysname = NULLIF(N'$(ApplicationUser)', N'__APPLICATION_PRINCIPAL_NOT_VERIFIED__');

IF @ApplicationUser IS NULL AND @ApplicationLogin IS NOT NULL
    SELECT @ApplicationUser = dp.name
    FROM sys.database_principals dp
    WHERE dp.sid = SUSER_SID(@ApplicationLogin);

DECLARE @ApplicationUserId int = DATABASE_PRINCIPAL_ID(@ApplicationUser);
DECLARE @ApplicationRoleId int = DATABASE_PRINCIPAL_ID(N'Warehouse_Application');
DECLARE @IsContainedUser bit = CASE WHEN @ApplicationUserId IS NOT NULL AND EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE principal_id = @ApplicationUserId
      AND authentication_type_desc IN (N'NONE', N'DATABASE')
) THEN 1 ELSE 0 END;
DECLARE @ServerSysadmin int = NULL;
IF @ApplicationLogin IS NOT NULL
    SET @ServerSysadmin = IS_SRVROLEMEMBER(N'sysadmin', @ApplicationLogin);
ELSE IF @IsContainedUser = 1
    SET @ServerSysadmin = 0;

CREATE TABLE #PrincipalScope
(
    Principal_ID int NOT NULL PRIMARY KEY,
    Principal_Name sysname NOT NULL
);

IF @ApplicationUserId IS NOT NULL
BEGIN
    INSERT #PrincipalScope(Principal_ID, Principal_Name)
    SELECT principal_id, name
    FROM sys.database_principals
    WHERE principal_id = @ApplicationUserId;

    ;WITH RoleClosure AS
    (
        SELECT drm.member_principal_id, drm.role_principal_id
        FROM sys.database_role_members drm
        WHERE drm.member_principal_id = @ApplicationUserId
        UNION ALL
        SELECT drm.member_principal_id, drm.role_principal_id
        FROM sys.database_role_members drm
        JOIN RoleClosure rc ON rc.role_principal_id = drm.member_principal_id
    )
    INSERT #PrincipalScope(Principal_ID, Principal_Name)
    SELECT DISTINCT rc.role_principal_id, rolePrincipal.name
    FROM RoleClosure rc
    JOIN sys.database_principals rolePrincipal ON rolePrincipal.principal_id = rc.role_principal_id
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM #PrincipalScope existing
        WHERE existing.Principal_ID = rc.role_principal_id
    )
    OPTION (MAXRECURSION 32);
END

DECLARE @HasWarehouseRole bit = CASE WHEN @ApplicationRoleId IS NOT NULL AND EXISTS
(
    SELECT 1 FROM #PrincipalScope WHERE Principal_ID = @ApplicationRoleId
) THEN 1 ELSE 0 END;
DECLARE @IsDatabaseOwner bit = CASE WHEN @ApplicationUserId = 1 OR @ApplicationUser = N'dbo' THEN 1 ELSE 0 END;
DECLARE @IsDbOwner bit = CASE WHEN @IsDatabaseOwner = 1 OR (@ApplicationUserId IS NOT NULL AND EXISTS
(
    SELECT 1 FROM #PrincipalScope WHERE Principal_Name = N'db_owner'
)) THEN 1 ELSE 0 END;
DECLARE @IsDbDataWriter bit = CASE WHEN @ApplicationUserId IS NOT NULL AND EXISTS
(
    SELECT 1 FROM #PrincipalScope WHERE Principal_Name = N'db_datawriter'
) THEN 1 ELSE 0 END;
DECLARE @IsDbDdlAdmin bit = CASE WHEN @ApplicationUserId IS NOT NULL AND EXISTS
(
    SELECT 1 FROM #PrincipalScope WHERE Principal_Name = N'db_ddladmin'
) THEN 1 ELSE 0 END;
DECLARE @IsDbSecurityAdmin bit = CASE WHEN @ApplicationUserId IS NOT NULL AND EXISTS
(
    SELECT 1 FROM #PrincipalScope WHERE Principal_Name = N'db_securityadmin'
) THEN 1 ELSE 0 END;
DECLARE @CanImpersonate bit = 0;

IF @ApplicationUserId IS NOT NULL AND
   (
       @IsDbOwner = 1 OR @IsDbSecurityAdmin = 1 OR EXISTS
       (
           SELECT 1
           FROM sys.database_permissions permissionEntry
           JOIN #PrincipalScope principalScope
             ON principalScope.Principal_ID = permissionEntry.grantee_principal_id
           WHERE permissionEntry.permission_name = N'IMPERSONATE'
             AND permissionEntry.state IN ('G', 'W')
       )
   )
    SET @CanImpersonate = 1;

IF @ApplicationLogin IS NOT NULL AND EXISTS
(
    SELECT 1
    FROM sys.server_permissions permissionEntry
    WHERE permissionEntry.grantee_principal_id = SUSER_ID(@ApplicationLogin)
      AND permissionEntry.permission_name = N'IMPERSONATE'
      AND permissionEntry.state IN ('G', 'W')
)
    SET @CanImpersonate = 1;

CREATE TABLE #Checks
(
    Check_Name NVARCHAR(160) NOT NULL,
    Expected_Value NVARCHAR(4000) NOT NULL,
    Actual_Value NVARCHAR(4000) NOT NULL,
    Status NVARCHAR(16) NOT NULL
);

INSERT #Checks VALUES
(
    N'APPLICATION_PRINCIPAL',
    N'Exact login and database user are supplied and resolve',
    COALESCE(@ApplicationLogin + N' -> ' + @ApplicationUser, @ApplicationUser, N'APPLICATION_PRINCIPAL_NOT_VERIFIED'),
    CASE WHEN @ApplicationUserId IS NULL THEN N'FAIL' ELSE N'PASS' END
),
(
    N'Warehouse_Application role exists',
    N'Role exists',
    CASE WHEN @ApplicationRoleId IS NULL THEN N'MISSING' ELSE N'PRESENT' END,
    CASE WHEN @ApplicationRoleId IS NULL THEN N'FAIL' ELSE N'PASS' END
),
(
    N'Warehouse_Application membership',
    N'Application user is a member',
    CASE WHEN @HasWarehouseRole = 1 THEN N'MEMBER' ELSE N'NOT_VERIFIED_OR_NOT_MEMBER' END,
    CASE WHEN @HasWarehouseRole = 1 THEN N'PASS' ELSE N'FAIL' END
),
(
    N'sysadmin membership',
    N'0',
    COALESCE(CONVERT(nvarchar(30), @ServerSysadmin), N'NOT_VERIFIED'),
    CASE WHEN @ServerSysadmin = 0 THEN N'PASS' ELSE N'FAIL' END
),
(
    N'db_owner membership',
    N'0',
    CASE WHEN @ApplicationUserId IS NULL THEN N'NOT_VERIFIED' ELSE CONVERT(nvarchar(5), @IsDbOwner) END,
    CASE WHEN @ApplicationUserId IS NOT NULL AND @IsDbOwner = 0 THEN N'PASS' ELSE N'FAIL' END
),
(
    N'db_datawriter membership',
    N'0',
    CASE WHEN @ApplicationUserId IS NULL THEN N'NOT_VERIFIED' ELSE CONVERT(nvarchar(5), @IsDbDataWriter) END,
    CASE WHEN @ApplicationUserId IS NOT NULL AND @IsDbDataWriter = 0 THEN N'PASS' ELSE N'FAIL' END
),
(
    N'db_ddladmin membership',
    N'0',
    CASE WHEN @ApplicationUserId IS NULL THEN N'NOT_VERIFIED' ELSE CONVERT(nvarchar(5), @IsDbDdlAdmin) END,
    CASE WHEN @ApplicationUserId IS NOT NULL AND @IsDbDdlAdmin = 0 THEN N'PASS' ELSE N'FAIL' END
),
(
    N'Can impersonate trusted principal',
    N'0',
    CASE WHEN @ApplicationUserId IS NULL THEN N'NOT_VERIFIED' ELSE CONVERT(nvarchar(5), @CanImpersonate) END,
    CASE WHEN @ApplicationUserId IS NOT NULL AND @CanImpersonate = 0 THEN N'PASS' ELSE N'FAIL' END
);

DECLARE @ProtectedObjects TABLE(Object_Name sysname NOT NULL PRIMARY KEY);
INSERT @ProtectedObjects(Object_Name) VALUES
    (N'dbo.tbl_XNK_Nhap_Kho'),
    (N'dbo.tbl_XNK_Nhap_Kho_Raw_Data'),
    (N'dbo.tbl_XNK_Xuat_Kho'),
    (N'dbo.tbl_XNK_Xuat_Kho_Raw_Data'),
    (N'dbo.InventoryBalance_Current'),
    (N'dbo.InventoryReservation_Current'),
    (N'dbo.Inventory_Current_Report_State');

DECLARE @ObjectName sysname;
DECLARE protected_object_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT Object_Name FROM @ProtectedObjects ORDER BY Object_Name;
OPEN protected_object_cursor;
FETCH NEXT FROM protected_object_cursor INTO @ObjectName;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @HasDmlGrant bit = 0, @HasDmlDeny bit = 0;
    IF OBJECT_ID(@ObjectName, N'U') IS NOT NULL AND @ApplicationUserId IS NOT NULL
    BEGIN
        SET @HasDmlGrant = CASE WHEN EXISTS
        (
            SELECT 1
            FROM sys.database_permissions permissionEntry
            JOIN #PrincipalScope principalScope
              ON principalScope.Principal_ID = permissionEntry.grantee_principal_id
            WHERE permissionEntry.class IN (0, 1, 3)
              AND
              (
                  permissionEntry.class = 0
                  OR permissionEntry.class = 3
                     AND permissionEntry.major_id = SCHEMA_ID(OBJECT_SCHEMA_NAME(OBJECT_ID(@ObjectName, N'U')))
                  OR permissionEntry.class = 1
                     AND permissionEntry.major_id = OBJECT_ID(@ObjectName, N'U')
              )
              AND permissionEntry.permission_name IN (N'INSERT', N'UPDATE', N'DELETE', N'CONTROL', N'ALTER', N'TAKE OWNERSHIP')
              AND permissionEntry.state IN ('G', 'W')
        ) THEN 1 ELSE 0 END;
        SET @HasDmlDeny = CASE WHEN EXISTS
        (
            SELECT 1
            FROM sys.database_permissions permissionEntry
            JOIN #PrincipalScope principalScope
              ON principalScope.Principal_ID = permissionEntry.grantee_principal_id
            WHERE permissionEntry.class = 1
              AND permissionEntry.major_id = OBJECT_ID(@ObjectName, N'U')
              AND permissionEntry.permission_name IN (N'INSERT', N'UPDATE', N'DELETE')
              AND permissionEntry.state = 'D'
        ) THEN 1 ELSE 0 END;
    END

    INSERT #Checks
    VALUES
    (
        N'Direct DML: ' + @ObjectName,
        N'Explicit DENY and no effective INSERT/UPDATE/DELETE grant',
        CASE
            WHEN OBJECT_ID(@ObjectName, N'U') IS NULL THEN N'OBJECT_MISSING'
            WHEN @ApplicationUserId IS NULL THEN N'NOT_VERIFIED'
            WHEN @HasDmlGrant = 1 THEN N'GRANT_PRESENT'
            WHEN @HasDmlDeny = 1 THEN N'DENY_PRESENT'
            ELSE N'NO_EXPLICIT_DENY'
        END,
        CASE WHEN OBJECT_ID(@ObjectName, N'U') IS NOT NULL AND @ApplicationUserId IS NOT NULL AND @HasDmlGrant = 0 AND @HasDmlDeny = 1 THEN N'PASS' ELSE N'FAIL' END
    );

    FETCH NEXT FROM protected_object_cursor INTO @ObjectName;
END
CLOSE protected_object_cursor;
DEALLOCATE protected_object_cursor;

DECLARE @ApprovedProcedures TABLE(Object_Name sysname NOT NULL PRIMARY KEY);
INSERT @ApprovedProcedures(Object_Name) VALUES
    (N'dbo.sp_XNK_Nhap_Kho_Save_Header'),
    (N'dbo.sp_XNK_Nhap_Kho_Save_Detail'),
    (N'dbo.sp_XNK_Nhap_Kho_Delete_Header'),
    (N'dbo.sp_XNK_Nhap_Kho_Delete_Detail'),
    (N'dbo.sp_XNK_Xuat_Kho_Save_Header'),
    (N'dbo.sp_XNK_Xuat_Kho_Save_Detail'),
    (N'dbo.sp_XNK_Xuat_Kho_Delete_Header'),
    (N'dbo.sp_XNK_Xuat_Kho_Delete_Detail'),
    (N'dbo.sp_XNK_Document_Post'),
    (N'dbo.sp_BC_Xuat_Nhap_Ton'),
    (N'dbo.sp_BC_Xuat_Nhap_Ton_Page'),
    (N'dbo.sp_BC_Ton_Kho_Hien_Tai_Page'),
    (N'dbo.sp_DM_Kho_User_List_Allowed');

DECLARE approved_procedure_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT Object_Name FROM @ApprovedProcedures ORDER BY Object_Name;
OPEN approved_procedure_cursor;
FETCH NEXT FROM approved_procedure_cursor INTO @ObjectName;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @HasExecuteGrant bit = CASE WHEN @ApplicationRoleId IS NOT NULL AND OBJECT_ID(@ObjectName, N'P') IS NOT NULL AND EXISTS
    (
        SELECT 1
        FROM sys.database_permissions permissionEntry
        WHERE permissionEntry.grantee_principal_id = @ApplicationRoleId
          AND permissionEntry.major_id = OBJECT_ID(@ObjectName, N'P')
          AND permissionEntry.class = 1
          AND permissionEntry.permission_name = N'EXECUTE'
          AND permissionEntry.state IN ('G', 'W')
    ) THEN 1 ELSE 0 END;

    INSERT #Checks
    VALUES
    (
        N'EXECUTE: ' + @ObjectName,
        N'Warehouse_Application has EXECUTE',
        CASE WHEN OBJECT_ID(@ObjectName, N'P') IS NULL THEN N'OBJECT_MISSING' WHEN @HasExecuteGrant = 1 THEN N'GRANT_PRESENT' ELSE N'GRANT_MISSING' END,
        CASE WHEN OBJECT_ID(@ObjectName, N'P') IS NOT NULL AND @HasExecuteGrant = 1 AND @HasWarehouseRole = 1 THEN N'PASS' ELSE N'FAIL' END
    );

    FETCH NEXT FROM approved_procedure_cursor INTO @ObjectName;
END
CLOSE approved_procedure_cursor;
DEALLOCATE approved_procedure_cursor;

DECLARE @FailureCount int;
SELECT @FailureCount = COUNT(*) FROM #Checks WHERE Status = N'FAIL';
SELECT Check_Name, Expected_Value, Actual_Value, Status
FROM #Checks
ORDER BY CASE WHEN Status = N'FAIL' THEN 0 ELSE 1 END, Check_Name;
SELECT N'FINAL' AS Check_Name,
       N'All checks PASS' AS Expected_Value,
       CONVERT(nvarchar(30), @FailureCount) + N' failed check(s)' AS Actual_Value,
       CASE WHEN @FailureCount = 0 THEN N'PASS' ELSE N'FAIL' END AS Status;

IF @FailureCount > 0
BEGIN
    THROW 51326, N'Warehouse security preflight failed; production application identity is not deployment-ready.', 1;
END
