$ErrorActionPreference = 'Continue'
# Build the Chinese folder name "can-kao-yuan-ma" from codepoints to keep this script pure ASCII
$refDir = -join ([char[]](0x53C2,0x8003,0x6E90,0x7801))
$dllPath = 'C:\Users\lyt\Documents\GitHub\TsWeb\' + $refDir + '\TShock-general-devel\prebuilts\HttpServer.dll'
if (-not (Test-Path $dllPath)) { Write-Host 'DLL NOT FOUND'; exit 1 }
Copy-Item $dllPath 'C:\Users\lyt\Documents\GitHub\TsWeb\plugin\HttpServer.dll' -Force
Write-Host 'COPIED'
$out = 'C:\Users\lyt\Documents\GitHub\TsWeb\plugin\httpserver_types.txt'
$sb = New-Object System.Text.StringBuilder
try {
  $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom('C:\Users\lyt\Documents\GitHub\TsWeb\plugin\HttpServer.dll')
  [void]$sb.AppendLine('=== Assembly: ' + $asm.FullName)
  foreach ($t in $asm.GetExportedTypes() | Sort-Object FullName) {
    [void]$sb.AppendLine('TYPE ' + $t.FullName + '  interface=' + $t.IsInterface + '  abstract=' + $t.IsAbstract)
    if ($t.IsInterface) {
      foreach ($m in $t.GetMethods() | Sort-Object Name) {
        $params = ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
        [void]$sb.AppendLine('   ' + $m.ReturnType.Name + ' ' + $m.Name + '(' + $params + ')')
      }
      foreach ($p in $t.GetProperties() | Sort-Object Name) {
        [void]$sb.AppendLine('   prop ' + $p.PropertyType.Name + ' ' + $p.Name)
      }
    }
  }
  [System.IO.File]::WriteAllText($out, $sb.ToString(), [System.Text.Encoding]::UTF8)
  Write-Host 'DONE'
} catch {
  [System.IO.File]::WriteAllText($out, 'ERROR: ' + $_.Exception.ToString(), [System.Text.Encoding]::UTF8)
  Write-Host 'ERROR'
}
