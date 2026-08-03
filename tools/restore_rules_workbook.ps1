$ErrorActionPreference = 'Stop'
$workbook = Join-Path (Split-Path -Parent $PSScriptRoot) 'medieval_chess_updated_rules_and_stats (1).xlsx'
$temporary = "$workbook.restore-tmp"

$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = 'git'
$info.Arguments = 'show "HEAD:medieval_chess_updated_rules_and_stats (1).xlsx"'
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$info.UseShellExecute = $false
$process = [Diagnostics.Process]::Start($info)
$stream = [IO.File]::Open($temporary, [IO.FileMode]::Create, [IO.FileAccess]::Write)
try {
  $process.StandardOutput.BaseStream.CopyTo($stream)
}
finally {
  $stream.Dispose()
}
$processError = $process.StandardError.ReadToEnd()
$process.WaitForExit()
if ($process.ExitCode -ne 0) {
  Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
  throw $processError
}
[IO.File]::Copy($temporary, $workbook, $true)
[IO.File]::Delete($temporary)
