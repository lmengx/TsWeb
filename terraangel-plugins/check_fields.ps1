try {
    $a = [Reflection.Assembly]::LoadFrom('C:\Games\TerraAngel\src\TerraAngel\Terraria\bin\Release\net10.0\Terraria.dll')
    $t = $a.GetType('Terraria.Main')
    $f = $t.GetField('curRelease',[Reflection.BindingFlags]'Public,Static')
    Write-Output ('FOUND curRelease: ' + $f.FieldType.FullName + ' static=' + $f.IsStatic)
    $t2 = $a.GetType('Terraria.Netplay')
    $f2 = $t2.GetField('ServerPassword',[Reflection.BindingFlags]'Public,Static')
    if ($f2) { Write-Output ('FOUND Netplay.ServerPassword: ' + $f2.FieldType.FullName) }
} catch { Write-Output ('ERR: ' + $_.Exception.Message) }
