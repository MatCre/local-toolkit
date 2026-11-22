<#
.SYNOPSIS
    Checks every template in the pack: generates a project from each and runs its tests.

.DESCRIPTION
    Packs the templates, installs them into a private template folder in a temp directory
    (dotnet new --debug:custom-hive), so your own `dotnet new` list isn't touched, then creates a
    project from each template, with and without options, and runs `dotnet test` on it.
    Exits non-zero if any template fails to generate, build or pass.

.EXAMPLE
    ./src/Toolkit.Templates/test-templates.ps1
#>
param(
    # The dotnet to use. Must be a .NET 11 SDK.
    [string]$Dotnet = "dotnet"
)

$ErrorActionPreference = "Stop"
$work = Join-Path ([IO.Path]::GetTempPath()) "toolkit-templates-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$hive = Join-Path $work "hive"
$package = Join-Path $work "package"

function Invoke-Dotnet {
    & $Dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

try {
    Write-Host "Packing templates..."
    Invoke-Dotnet pack (Join-Path $PSScriptRoot "Toolkit.Templates.csproj") -o $package --nologo -v quiet
    $nupkg = Get-ChildItem $package -Filter "*.nupkg" | Select-Object -First 1
    Invoke-Dotnet new install $nupkg.FullName --debug:custom-hive $hive | Out-Null

    # Each case: template, project name, extra options. Defaults are tested too.
    $cases = @(
        @{ Template = "tk-endpoint"; Name = "Orders";  Options = @("--item", "Order") },
        @{ Template = "tk-endpoint"; Name = "Things";  Options = @() },
        @{ Template = "tk-store";    Name = "Notes";   Options = @("--item", "Note") },
        @{ Template = "tk-cli";      Name = "Greeter"; Options = @() }
    )

    $failed = @()
    foreach ($case in $cases) {
        $label = "$($case.Template) -n $($case.Name) $($case.Options -join ' ')".Trim()
        $output = Join-Path $work $case.Name
        try {
            Invoke-Dotnet new $case.Template -n $case.Name -o $output @($case.Options) --debug:custom-hive $hive | Out-Null
            Invoke-Dotnet test $output --nologo -v quiet | Out-Null
            Write-Host "  passed  $label" -ForegroundColor Green
        }
        catch {
            Write-Host "  FAILED  $label`n          $_" -ForegroundColor Red
            $failed += $label
        }
    }

    if ($failed.Count -gt 0) {
        Write-Host "`n$($failed.Count) of $($cases.Count) failed. Generated projects kept in $work for inspection." -ForegroundColor Red
        exit 1
    }

    Write-Host "`nAll $($cases.Count) generated projects built and passed their tests."
    Remove-Item $work -Recurse -Force
}
catch {
    Write-Host "Template check failed: $_" -ForegroundColor Red
    Write-Host "Work folder: $work"
    exit 1
}
