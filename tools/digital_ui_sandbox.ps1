<#
.SYNOPSIS
Build, start or remove an OWNED disposable Digital UI QA instance on port 5399.
.EXAMPLE
.\tools\digital_ui_sandbox.ps1 -Start
.\tools\digital_ui_sandbox.ps1 -Stop
.NOTES
Requires Windows PowerShell 5.1+, .NET SDK, restored project packages, local
appsettings.Local.json seed users, and SQL permission to create/drop test databases.
No existing application or database is stopped, reset or adopted by this script.
#>
[CmdletBinding(DefaultParameterSetName = 'Start')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Start')][switch]$Start,
    [Parameter(Mandatory = $true, ParameterSetName = 'Stop')][switch]$Stop,
    [Parameter(ParameterSetName = 'Start')][ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [Parameter(ParameterSetName = 'Start')][ValidateRange(15, 180)][int]$StartupTimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sandboxWorkspace = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$sandboxArtifacts = [IO.Path]::GetFullPath((Join-Path $sandboxWorkspace 'artifacts'))
$sandboxInstances = [IO.Path]::GetFullPath((Join-Path $sandboxArtifacts 'digital-ui-instance'))
$sandboxStatePath = Join-Path $sandboxArtifacts 'digital-ui-sandbox.json'
$sandboxMarker = 'EduLibrary.DigitalUiSandbox/v1'
$sandboxNamePattern = '\AEduLibraryDigitalUi_([a-f0-9]{32})\z'
$sandboxPort = 5399

function Assert-SandboxPath([string]$Path, [string]$Expected) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not [string]::Equals($absolute, [IO.Path]::GetFullPath($Expected), [StringComparison]::OrdinalIgnoreCase) -or
        -not $absolute.StartsWith($sandboxWorkspace + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The sandbox path is outside its exact owned workspace target.'
    }
    # Reject an existing junction/symlink in every ancestor, before creating or deleting.
    $cursor = $absolute
    while ($cursor.StartsWith($sandboxWorkspace, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'A sandbox path contains a reparse point.' }
        }
        if ([string]::Equals($cursor, $sandboxWorkspace, [StringComparison]::OrdinalIgnoreCase)) { break }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $absolute
}

function Assert-NoReparseTree([string]$Root) {
    $pendingDirectories = New-Object 'System.Collections.Generic.Stack[string]'
    $pendingDirectories.Push($Root)
    while ($pendingDirectories.Count -gt 0) {
        $directory = $pendingDirectories.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'The private sandbox tree contains a reparse point; cleanup was refused.' }
            if ($item.PSIsContainer) { $pendingDirectories.Push($item.FullName) }
        }
    }
}

function Write-PrivateJson([string]$Path, [object]$Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 30), (New-Object Text.UTF8Encoding($false)))
}

function Write-SandboxState([object]$State) {
    # The in-root marker is canonical if execution stops between these writes.
    Write-PrivateJson (Join-Path $State.Root '.digital-ui-owner.json') $State
    Write-PrivateJson $sandboxStatePath $State
}

function Get-CommandHash([string]$CommandLine) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($CommandLine)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function ConvertTo-NativeArgument([string]$Value) {
    # CommandLineToArgvW escaping, including embedded quotes and trailing slashes.
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $builder = New-Object Text.StringBuilder
    [void]$builder.Append('"')
    $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') { $slashes++; continue }
        if ($character -eq '"') {
            [void]$builder.Append(('\' * (2 * $slashes + 1)))
            [void]$builder.Append('"')
        }
        else {
            if ($slashes -gt 0) { [void]$builder.Append(('\' * $slashes)) }
            [void]$builder.Append($character)
        }
        $slashes = 0
    }
    if ($slashes -gt 0) { [void]$builder.Append(('\' * (2 * $slashes))) }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Get-PrivateConnection([string]$Root, [string]$ExpectedDatabase) {
    $local = Get-Content -LiteralPath (Join-Path $Root 'appsettings.Local.json') -Raw | ConvertFrom-Json
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder.set_ConnectionString([string]$local.ConnectionStrings.DefaultConnection)
    if ($builder.get_InitialCatalog() -cne $ExpectedDatabase) { throw 'The private connection does not target the exact owned database.' }
    $builder.set_Pooling($false)
    $builder.set_ConnectTimeout(15)
    return $builder
}

function Invoke-CreateOwnedDatabase([object]$State) {
    if ($State.Name -cnotmatch $sandboxNamePattern) { throw 'Invalid disposable database name.' }
    $builder = Get-PrivateConnection $State.Root $State.Name
    $builder.set_InitialCatalog('master')
    $connection = New-Object System.Data.SqlClient.SqlConnection($builder.get_ConnectionString())
    try {
        $connection.Open()
        $check = $connection.CreateCommand()
        try {
            $check.CommandText = 'SELECT DB_ID(@name)'
            [void]$check.Parameters.Add('@name', [Data.SqlDbType]::NVarChar, 128)
            $check.Parameters['@name'].Value = $State.Name
            if ($check.ExecuteScalar() -isnot [DBNull]) { throw 'The generated database name already exists; it will never be adopted.' }
        }
        finally { $check.Dispose() }
        $create = $connection.CreateCommand()
        try { $create.CommandText = 'CREATE DATABASE [' + $State.Name + ']'; $create.CommandTimeout = 30; [void]$create.ExecuteNonQuery() }
        finally { $create.Dispose() }
        $State.DatabaseCreated = $true
        Write-SandboxState $State
        # SQL itself records ownership, so a same-named catalog on another server
        # or a replacement catalog can never be dropped just from JSON metadata.
        $mark = $connection.CreateCommand()
        try {
            $mark.CommandText = 'USE [' + $State.Name + ']; EXEC sys.sp_addextendedproperty @name=N''EduLibraryDigitalUiSandboxOwner'', @value=@nonce;'
            [void]$mark.Parameters.Add('@nonce', [Data.SqlDbType]::NVarChar, 128)
            $mark.Parameters['@nonce'].Value = $State.Nonce
            [void]$mark.ExecuteNonQuery()
        }
        finally { $mark.Dispose() }
    }
    finally { $connection.Dispose() }
}

function Read-OwnedState {
    if (-not (Test-Path -LiteralPath $sandboxStatePath -PathType Leaf)) { throw 'No script-owned sandbox state exists. Manual instances are not adopted.' }
    $pointer = Get-Content -LiteralPath $sandboxStatePath -Raw | ConvertFrom-Json
    if ($pointer.Script -cne $sandboxMarker -or $pointer.Owned -ne $true -or $pointer.Name -cnotmatch $sandboxNamePattern) { throw 'The sandbox state has no valid script ownership marker.' }
    $instanceKey = [regex]::Match([string]$pointer.Name, $sandboxNamePattern).Groups[1].Value
    $expectedRoot = Join-Path $sandboxInstances $instanceKey
    [void](Assert-SandboxPath ([string]$pointer.Root) $expectedRoot)
    if (-not (Test-Path -LiteralPath $expectedRoot -PathType Container)) { throw 'The owned private content root is missing; automatic cleanup is refused.' }
    Assert-NoReparseTree $expectedRoot
    $owner = Get-Content -LiteralPath (Join-Path $expectedRoot '.digital-ui-owner.json') -Raw | ConvertFrom-Json
    if ($owner.Script -cne $sandboxMarker -or $owner.Owned -ne $true -or $owner.Name -cne $pointer.Name -or $owner.Nonce -cne $pointer.Nonce -or
        $owner.Root -cne $pointer.Root -or $owner.Runtime -cne (Join-Path $expectedRoot 'runtime')) { throw 'Private ownership metadata does not match the state pointer.' }
    [void](Assert-SandboxPath ([string]$owner.Runtime) (Join-Path $expectedRoot 'runtime'))
    return $owner
}

function Stop-OwnedSandbox {
    $state = Read-OwnedState
    $runtimeDll = Join-Path $state.Runtime 'EduLibrary.dll'
    if ([int]$state.ProcessId -gt 0) {
        $ownedProcess = $null
        try { $ownedProcess = [Diagnostics.Process]::GetProcessById([int]$state.ProcessId); [void]$ownedProcess.Handle }
        catch [ArgumentException] { } # The exact recorded process is already gone.
        if ($null -ne $ownedProcess) {
            try {
                if (-not $ownedProcess.HasExited) {
                    $process = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + [int]$state.ProcessId)
                    if ($null -eq $process -or $process.Name -ine 'dotnet.exe' -or $process.ExecutablePath -ine $state.Dotnet -or [string]::IsNullOrWhiteSpace($process.CommandLine) -or
                        $process.CommandLine.IndexOf($runtimeDll, [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
                        $process.CommandLine.IndexOf($state.Name, [StringComparison]::Ordinal) -lt 0 -or
                        $process.CommandLine -notmatch '--urls\s+"?http://localhost:5399(?:"|\s|$)' -or
                        (-not [string]::IsNullOrWhiteSpace($state.CommandLineHash) -and (Get-CommandHash $process.CommandLine) -cne $state.CommandLineHash) -or
                        $ownedProcess.StartTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture) -cne [string]$state.ProcessCreatedUtcTicks) {
                        throw 'The recorded PID no longer matches the exact owned dotnet/runtime/database command. No process or database was touched.'
                    }
                    # Kill through the already-open, verified handle; never resolve
                    # the PID again after validation (it could have been reused).
                    $ownedProcess.Kill()
                    if (-not $ownedProcess.WaitForExit(15000)) { throw 'The owned application did not exit; database cleanup was refused.' }
                }
            }
            finally { $ownedProcess.Dispose() }
        }
    }
    if ($state.DatabaseCreated -eq $true) {
        if ($state.Name -cnotmatch $sandboxNamePattern) { throw 'Owned database safety check failed.' }
        $builder = Get-PrivateConnection $state.Root $state.Name
        $builder.set_InitialCatalog('master')
        $connection = New-Object System.Data.SqlClient.SqlConnection($builder.get_ConnectionString())
        try {
            $connection.Open()
            $drop = $connection.CreateCommand()
            try {
                $drop.CommandTimeout = 30
                $drop.CommandText = 'IF DB_ID(@name) IS NOT NULL BEGIN DECLARE @owner nvarchar(128); SELECT @owner=CONVERT(nvarchar(128),value) FROM [' + $state.Name + '].sys.extended_properties WHERE class=0 AND name=N''EduLibraryDigitalUiSandboxOwner''; IF @owner IS NULL OR @owner<>@nonce THROW 51000, ''Disposable database ownership mismatch; cleanup refused.'', 1; ALTER DATABASE [' + $state.Name + '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [' + $state.Name + ']; END'
                [void]$drop.Parameters.Add('@name', [Data.SqlDbType]::NVarChar, 128)
                $drop.Parameters['@name'].Value = $state.Name
                [void]$drop.Parameters.Add('@nonce', [Data.SqlDbType]::NVarChar, 128)
                $drop.Parameters['@nonce'].Value = $state.Nonce
                [void]$drop.ExecuteNonQuery()
            }
            finally { $drop.Dispose() }
        }
        finally { $connection.Dispose() }
    }
    # Only after the verified process is gone and its owned SQL database is removed.
    [void](Assert-SandboxPath ([string]$state.Root) (Join-Path $sandboxInstances ([regex]::Match([string]$state.Name, $sandboxNamePattern).Groups[1].Value)))
    Assert-NoReparseTree $state.Root
    Remove-Item -LiteralPath $state.Root -Recurse -Force
    [void](Assert-SandboxPath $sandboxStatePath (Join-Path $sandboxArtifacts 'digital-ui-sandbox.json'))
    Remove-Item -LiteralPath $sandboxStatePath -Force
    Write-Host 'Removed only the script-owned Digital UI process, database and private files. Browser QA proofs in artifacts/digital-ui remain available.'
}

if ($Stop) { Stop-OwnedSandbox; return }

if (Test-Path -LiteralPath $sandboxStatePath) { throw 'A script-owned sandbox state already exists. Run -Stop before creating another; no existing state is overwritten.' }
if (@(Get-NetTCPConnection -LocalPort $sandboxPort -State Listen -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Port 5399 is occupied. The current application will not be stopped or adopted.' }
$localPath = Join-Path $sandboxWorkspace 'appsettings.Local.json'
if (-not (Test-Path -LiteralPath $localPath -PathType Leaf)) { throw 'Configure appsettings.Local.json seed users before starting browser QA.' }
if (-not (Test-Path -LiteralPath (Join-Path $sandboxWorkspace 'obj\project.assets.json'))) { throw 'Run dotnet restore first; the sandbox build uses --no-restore.' }
[void](Assert-SandboxPath $sandboxInstances (Join-Path $sandboxWorkspace 'artifacts\digital-ui-instance'))
[void][IO.Directory]::CreateDirectory($sandboxInstances)
$instanceKey = [Guid]::NewGuid().ToString('N')
$privateRoot = Join-Path $sandboxInstances $instanceKey
[void](Assert-SandboxPath $privateRoot (Join-Path $sandboxInstances $instanceKey))
if (Test-Path -LiteralPath $privateRoot) { throw 'The freshly generated private directory already exists; refusing to adopt it.' }
[void][IO.Directory]::CreateDirectory($privateRoot)
$runtime = Join-Path $privateRoot 'runtime'
$dotnetPath = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
$state = [pscustomobject]@{
    Script=$sandboxMarker; Owned=$true; Nonce=[Guid]::NewGuid().ToString('N'); Name=('EduLibraryDigitalUi_' + $instanceKey)
    Root=$privateRoot; Runtime=$runtime; Dotnet=$dotnetPath; DatabaseCreated=$false; ProcessId=0; ProcessCreatedUtcTicks=''; CommandLineHash=''; Ready=$false
}
Write-SandboxState $state
try {
    $logs = Join-Path $privateRoot 'logs'; [void][IO.Directory]::CreateDirectory($logs)
    $buildLog = Join-Path $logs 'build.log'
    & $dotnetPath build (Join-Path $sandboxWorkspace 'EduLibrary.csproj') --no-restore --configuration $Configuration --nologo *> $buildLog
    if ($LASTEXITCODE -ne 0) { throw 'The application build failed. Inspect only the private build log.' }
    $builtRoot = Join-Path $sandboxWorkspace ('bin\' + $Configuration + '\net10.0')
    $builtDll = Join-Path $builtRoot 'EduLibrary.dll'
    if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) { throw 'A successful build did not produce the expected application assembly.' }
    [void][IO.Directory]::CreateDirectory($runtime)
    foreach ($item in Get-ChildItem -LiteralPath $builtRoot -Force) { Copy-Item -LiteralPath $item.FullName -Destination $runtime -Recurse -Force }
    if ((Get-FileHash -LiteralPath $builtDll -Algorithm SHA256).Hash -cne (Get-FileHash -LiteralPath (Join-Path $runtime 'EduLibrary.dll') -Algorithm SHA256).Hash) { throw 'The private runtime copy does not match the verified build.' }
    Copy-Item -LiteralPath (Join-Path $sandboxWorkspace 'appsettings.json') -Destination $privateRoot
    $developmentSettings = Join-Path $sandboxWorkspace 'appsettings.Development.json'
    if (Test-Path -LiteralPath $developmentSettings) { Copy-Item -LiteralPath $developmentSettings -Destination $privateRoot }
    [void][IO.Directory]::CreateDirectory((Join-Path $privateRoot 'Data'))
    Copy-Item -LiteralPath (Join-Path $sandboxWorkspace 'Data\DigitalDemo') -Destination (Join-Path $privateRoot 'Data') -Recurse
    $local = Get-Content -LiteralPath $localPath -Raw | ConvertFrom-Json
    foreach ($role in @('Student', 'Admin', 'Librarian')) {
        if ([string]::IsNullOrWhiteSpace($local.SeedUsers.$role.Email) -or [string]::IsNullOrWhiteSpace($local.SeedUsers.$role.Password)) { throw 'Configure all three local seed roles before browser QA.' }
    }
    $connectionSource = [Environment]::GetEnvironmentVariable('EDULIBRARY_TEST_CONNECTION_STRING')
    if ([string]::IsNullOrWhiteSpace($connectionSource)) {
        $connectionSource = 'Server=.;Trusted_Connection=True;TrustServerCertificate=True'
        if ($null -ne $local.PSObject.Properties['ConnectionStrings'] -and $null -ne $local.ConnectionStrings.PSObject.Properties['DefaultConnection']) { $connectionSource = [string]$local.ConnectionStrings.DefaultConnection }
    }
    $ownConnection = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connectionSource)
    $ownConnection.set_InitialCatalog($state.Name); $ownConnection.set_Pooling($false); $ownConnection.set_ConnectTimeout(15); $ownConnection.set_MultipleActiveResultSets($false)
    if ($null -eq $local.PSObject.Properties['ConnectionStrings']) { $local | Add-Member NoteProperty ConnectionStrings ([pscustomobject]@{}) }
    $local.ConnectionStrings | Add-Member NoteProperty DefaultConnection $ownConnection.get_ConnectionString() -Force
    # Even if command-line configuration is changed accidentally, Local never points at the main DB.
    Write-PrivateJson (Join-Path $privateRoot 'appsettings.Local.json') $local
    Invoke-CreateOwnedDatabase $state
    $arguments = @((Join-Path $runtime 'EduLibrary.dll'), '--environment', 'Development', '--contentRoot', $privateRoot,
        '--webroot', (Join-Path $sandboxWorkspace 'wwwroot'), '--urls', 'http://localhost:5399',
        '--ConnectionStrings:DefaultConnection', $ownConnection.get_ConnectionString(), '--Database:ApplyMigrations', 'true',
        '--DigitalLibrary:SeedDemoContent', 'true', '--DigitalLibrary:StorageRoot', (Join-Path $privateRoot 'App_Data\DigitalLibrary'),
        '--Recommendations:Llm:Enabled', 'false', '--Logging:LogLevel:Default', 'Warning')
    $nativeArguments = ($arguments | ForEach-Object { ConvertTo-NativeArgument ([string]$_) }) -join ' '
    $priorAspnetEnvironment = [Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Process')
    $priorDotnetEnvironment = [Environment]::GetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
        [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Development', 'Process')
        $started = Start-Process -FilePath $dotnetPath -ArgumentList $nativeArguments -WorkingDirectory $privateRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs 'server.stdout.log') -RedirectStandardError (Join-Path $logs 'server.stderr.log')
    }
    finally {
        [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', $priorAspnetEnvironment, 'Process')
        [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', $priorDotnetEnvironment, 'Process')
    }
    # Capture the returned process handle/start identity before a CIM lookup can
    # fail, so a partial startup remains safely recoverable by -Stop.
    [void]$started.Handle
    $state.ProcessId = $started.Id
    $state.ProcessCreatedUtcTicks = $started.StartTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
    Write-SandboxState $state
    $process = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $started.Id)
    if ($null -eq $process -or $process.Name -ine 'dotnet.exe' -or $process.ExecutablePath -ine $dotnetPath -or
        $process.CommandLine.IndexOf((Join-Path $runtime 'EduLibrary.dll'), [StringComparison]::OrdinalIgnoreCase) -lt 0 -or $process.CommandLine.IndexOf($state.Name, [StringComparison]::Ordinal) -lt 0) { throw 'The launched process identity could not be verified.' }
    # A numeric string survives JSON round-tripping in both PS5.1 and PS7 without
    # their different automatic ISO-date conversions or floating-point rounding.
    $state.CommandLineHash = Get-CommandHash $process.CommandLine
    Write-SandboxState $state
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $started.Refresh()
        if ($started.HasExited) { throw 'The private application exited during migration/seed. Inspect its private server log.' }
        $listeners = @(Get-NetTCPConnection -LocalPort $sandboxPort -State Listen -ErrorAction SilentlyContinue)
        if ($listeners.Count -gt 0 -and @($listeners | Where-Object { $_.OwningProcess -ne $started.Id }).Count -gt 0) { throw 'Port 5399 was taken by another process; that process will not be touched.' }
        if ($listeners.Count -gt 0) {
            try {
                $response = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5399/Account/Login' -TimeoutSec 2
                if ($response.StatusCode -eq 200) { $state.Ready = $true; Write-SandboxState $state; break }
            }
            catch { }
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $state.Ready) { throw 'The private application was not ready before the startup deadline.' }
    Write-Host 'Disposable Digital UI instance is ready at http://localhost:5399.'
    Write-Host 'Run: node tests/digital_ui_smoke.cjs http://localhost:5399'
    Write-Host 'Cleanup: .\tools\digital_ui_sandbox.ps1 -Stop'
    Write-Host 'Ownership and private logs are recorded under ignored artifacts/. Credentials and connection strings were not printed.'
}
catch {
    # Preserve the exact ownership marker for a deliberate, verified -Stop retry.
    [IO.File]::WriteAllText((Join-Path $privateRoot 'startup.failure.log'), ($_.Exception.ToString() + [Environment]::NewLine + $_.ScriptStackTrace), (New-Object Text.UTF8Encoding($false)))
    Write-SandboxState $state
    throw 'Digital UI startup did not complete. Read the private logs under artifacts/digital-ui-instance, then run this script with -Stop to remove only its owned fixture. Credentials were not printed.'
}
