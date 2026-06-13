<?php
include('connect.php');
include('posturl.php');
$servers = $db->select('servers', '*');
$countplayers = 0;
foreach ($servers as $server) {
    $serverinfo = $server['ServerIP'];
    $token = $server['ServerToken'];
    $url = "http://$serverinfo/v2/players/list?token=$token";
    $str = posturl($url);
    if ($str) {
        //echo $str . '<br>';
        $arr = json_decode($str, true);
        if ($arr['status'] == 200) {
            $time = date('H');
            $onlineplayers = json_decode($server['OnlinePlayers'], true);
            $onlines = count($arr['players']);
            $countplayers += $onlines;
            $onlineplayers["time$time"] = $onlines;
            $db->update('servers', array('OnlinePlayers' => json_encode($onlineplayers), 'Disconnect' => 0), array('ID' => $server['ID']));
        } else {
            cannotconnect($server);
        }
    } else {
        cannotconnect($server);
    }
}
echo "<br>全平台有".$countplayers;
function cannotconnect($server)
{
    echo "无法连接的服务器" . $server['ID'] . $server['ServerName'].'<br>';
    include('connect.php');
    $id = $server['ID'];
    if ($server['Disconnect'] < 168) {
        $times = $server['Disconnect'] + 1;
        $db->update('servers', array('Disconnect' => $times), array('ID' => $id));
    } else {
        $sql = "DROP TABLE `xj_z_server$id`";
        $db->query($sql);
        $db->delete('servers', array('ID' => $id));
    }
}
?>