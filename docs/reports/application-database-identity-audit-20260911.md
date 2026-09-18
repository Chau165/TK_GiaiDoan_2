# Application Database Identity Audit

Date: 2026-09-11  
Scope: Read-only audit of the ASP.NET Core application's SQL Server Windows identity  
Database in configuration: `TKS_Thuc_Tap_V11_GiaiDoan2`  
Server in configuration: `localhost`

## Conclusion

```text
DETECTED_APPLICATION_IDENTITY: UNKNOWN
RUNTIME_OBSERVATION: NOT_AVAILABLE - application was not running during the audit
DATABASE_MAPPING_STATUS: NOT_VERIFIED
DATABASE_OR_SECURITY_CHANGED: NO
```

The source proves that the application delegates SQL authentication to the
Windows account of its hosting process. It does not contain a fixed SQL login,
database user, IIS application-pool account, Windows-service account, or
container identity. No application process was running on the audited host, so
the exact runtime account cannot be established from the current evidence.

## Hosting Model

Observed source/configuration:

- `TKS_Thuc_Tap_V11_Web/Program.cs` uses `WebApplication.CreateBuilder`,
  `AddServerSideBlazor`, `MapBlazorHub`, and `MapFallbackToPage`; this is an
  ASP.NET Core server-side Blazor web application.
- `TKS_Thuc_Tap_V11_Web/Properties/launchSettings.json` contains two local
  profiles:
  - `TKS_Thuc_Tap_V11_Web` with `commandName: Project`, which uses the normal
    ASP.NET Core process/Kestrel path.
  - `IIS Express`, with `windowsAuthentication: false` and
    `anonymousAuthentication: true` in the IIS settings.
- The project file specifies `AspNetCoreHostingModel=OutOfProcess`, which is
  relevant if the application is published behind full IIS; it does not name
  an application-pool account.
- No `web.config`, Dockerfile/compose file, Windows-service file, or
  deployment service configuration was found in the repository.
- The current IIS Express `applicationhost.config` contains only the default
  `WebSite1`; it has no TKS application/site entry.
- `appcmd.exe` was not available on the audited host, and no related `w3wp`,
  `iisexpress`, or `dotnet` process was running. The configured application
  ports `15073`, `15074`, `50001`, and `50002` were not listening.

Therefore the hosting model supported by source is known, but the selected
runtime profile and process account are not currently observable.

## Connection Authentication

The base configuration contains:

```text
Server=localhost;
Database=TKS_Thuc_Tap_V11_GiaiDoan2;
Integrated Security=True;
TrustServerCertificate=True;
```

Implications:

- SQL Server authentication is Windows Integrated Security.
- There is no `User ID`, SQL password, `SqlCredential`, access token, or
  explicit impersonation configuration in the audited application path.
- `TrustServerCertificate=True` changes certificate validation; it does not
  select the Windows login.
- `Program.cs` copies the configured connection string into
  `CConfig.TKS_Thuc_Tap_V11_Conn_String`.
- `CSqlHelper` creates `Microsoft.Data.SqlClient.SqlConnection` from that
  string, and `PrepareCommand` opens it. Parameter discovery in
  `CSqlHelperParameterCache` also opens a connection from the same string.

The effective SQL Server login is therefore the Windows access token of the
process that opens the connection:

```text
Kestrel/Project profile -> account running dotnet/Visual Studio
IIS Express             -> account running IIS Express
Full IIS OutOfProcess   -> configured IIS application-pool process identity
Windows service         -> service Log On As account
Docker                  -> container process identity/configuration
```

The source does not distinguish which of these runtime cases is active now.

## Detected Application Identity

### Application identity

```text
UNKNOWN
```

At audit time:

- No TKS web process was running.
- No TKS SQL session was visible in `sys.dm_exec_sessions` for the target
  database.
- No IIS Express TKS site or full-IIS app-pool configuration for this
  application was available.
- No Windows service or Docker configuration identified an application
  account.

### Identity observed by the audit connection

The following values came from the separate administrator `sqlcmd` audit
connection, not from the ASP.NET application:

```text
SUSER_SNAME()   = DESKTOP-NHQ7QPL\Surface
ORIGINAL_LOGIN()= MicrosoftAccount\kiriza165@gmail.com
USER_NAME()     = dbo
sysadmin        = 1
```

This identity must not be reported or used as the application identity. Its
`dbo` result is an administrative/sysadmin context, not proof of the
`Warehouse_Application` role mapping.

## Database Mapping

Expected security chain:

```text
Windows Login -> Database User -> Warehouse_Application role
```

Read-only inspection of `TKS_Thuc_Tap_V11_GiaiDoan2` found:

- `Warehouse_Application` role: present.
- Membership of the observed audit account in `Warehouse_Application`: none.
- Database user mapped to `DESKTOP-NHQ7QPL\Surface`: none; the audit session
  resolves as `dbo` because the login is sysadmin.
- `DOMAIN\WarehouseApp`: not found as the verified application mapping.
- `IIS APPPOOL\TKS_Thuc_Tap_V11_Web`: not found as a verified server or
  database principal.
- No application SQL session was active from which `login_name`,
  `original_login_name`, `program_name`, and `host_name` could be observed.

The mapping needed by `WarehouseModule.Security.Preflight.sql` is therefore
not verified.

## Runtime Verification Procedure

The following query must execute through the **application's actual
connection**, not through SSMS or a developer `sqlcmd -E` session:

```sql
SELECT
    ORIGINAL_LOGIN() AS OriginalLogin,
    SUSER_SNAME() AS EffectiveLogin,
    USER_NAME() AS DatabaseUser,
    DB_NAME() AS DatabaseName,
    APP_NAME() AS ClientApplication,
    HOST_NAME() AS ClientHost;
```

Safe verification sequence:

1. Start the application using the intended hosting mechanism and intended
   Windows account.
2. Open a page that performs a normal database read so that the connection
   pool creates an active SQL session.
3. From a separate administrator connection, inspect only the active session:

   ```sql
   SELECT
       s.session_id,
       s.login_name,
       s.original_login_name,
       s.host_name,
       s.program_name,
       c.auth_scheme,
       DB_NAME(s.database_id) AS DatabaseName
   FROM sys.dm_exec_sessions AS s
   LEFT JOIN sys.dm_exec_connections AS c
       ON c.session_id = s.session_id
   WHERE s.database_id = DB_ID(N'TKS_Thuc_Tap_V11_GiaiDoan2')
     AND s.session_id <> @@SPID;
   ```

4. Match the observed login to `sys.server_principals`,
   `sys.database_principals`, and `sys.database_role_members`.
5. Do not use the administrator session values as application evidence.

If the application is run from Visual Studio/Kestrel locally, the observed
account will normally be the account that launched that process. If full IIS
is used, inspect the exact site's application pool `processModel` identity. If
the application is a service or container, inspect its configured process
identity instead.

## Preflight Recommendation

Do not run the security preflight with the current administrator identity or
with a guessed placeholder. Obtain these two verified values from the runtime
observation:

```text
ApplicationLogin = <exact Windows/server login used by the application>
ApplicationUser  = <exact database user mapped to that login>
```

Then run the unchanged read-only gate against the business database:

```powershell
sqlcmd -S localhost -E -C -d TKS_Thuc_Tap_V11_GiaiDoan2 -b -f 65001 `
  -i Database\WarehouseModule.Security.Preflight.sql `
  -v ApplicationLogin="<verified login>" ApplicationUser="<verified database user>"
```

The expected mapping is that the application database user is a member of
`Warehouse_Application`, while the application is not `sysadmin`, `db_owner`,
`db_datawriter`, `db_ddladmin`, or an equivalent privileged principal. A
preflight PASS cannot be certified until that real identity exists and is
verified.

## Additional Observations

- `appsettings.Production.json` was not present; no repository-level
  production connection override was found. External hosting environment
  variables or deployment settings could still override the base
  configuration and must be checked separately.
- `appsettings.json` contains a plaintext SMTP password property. Its value is
  intentionally omitted from this report. This is outside the database-identity
  question and was not changed, but it should be handled as a separate
  credential-rotation/security task.
- This audit made no source, database, role, login, user, grant, migration, or
  deployment changes.
