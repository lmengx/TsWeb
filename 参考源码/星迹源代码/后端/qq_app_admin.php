<?php
declare(strict_types=1);
namespace app\api {
    require_once 'jwt.php';
    use app\jwt\CheckToken;

    $headers = array();
    foreach ($_SERVER as $key => $value) {
        if (strpos($key, 'HTTP_') === 0) {
            $headers[str_replace('_', ' ', substr($key, 5))] = $value;
        }
    }
    $token = CheckToken::verifyJwt($headers['AUTHTOKEN']);
    if ($token['status'] == 200) {
        include './connect.php';
        include './posturl.php';
        $user = $db->select('users', '*', ['OPENID' => $token['openid']])[0];
        $requestype = $_POST['requestype'];
        switch ($requestype) {
            case 'addserver': //添加服务器时
                $servername = $_POST['servername'];
                $ipaddress = $_POST['ipaddress'];
                $port = $_POST['port'];
                $servertoken = $_POST['token'];
                if ($servertoken && $port && $ipaddress && $servername) {
                    $url = "http://$ipaddress:$port/tokentest?token=$servertoken";
                    $res = @posturl($url);
                    if ($res) {
                        $arr = json_decode($res, true);
                        if ($arr['status'] == 200) {
                            $serverinfo = $ipaddress . ':' . $port;
                            $userip = $_SERVER["REMOTE_ADDR"];
                            $db->insert('servers', ['Master' => $user['ID'], 'AddTime' => date('Y-m-d H:i:s'), 'ServerName' => $servername, 'ServerIP' => $serverinfo, 'ServerToken' => $servertoken, 'Alive' => 1, 'Disconnect' => 0, 'OnlinePlayers' => '[]', 'EnableReg' => 0, 'Allowed' => 1]);
                            $id = $db->select('servers', 'ID', ['ServerIP' => $serverinfo])[0]; //取得ID
                            $sql = "CREATE TABLE `xj_z_server$id` ( `OPENID` VARCHAR(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL ,  `playername` VARCHAR(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL ,  `regdate` VARCHAR(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL ,  `signin` VARCHAR(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,  `continuous` VARCHAR(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL ,  `coin` VARCHAR(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL ,  `regIP` VARCHAR(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8;"; //创建表
                            $db->query($sql);
                            $json['status'] = 200;
                            $json['response'] = '添加成功';
                        } else if ($arr['status'] == 403) {
                            $json['status'] = 403;
                            $json['response'] = '秘钥错误';
                        }
                    } else {
                        $json['status'] = 403;
                        $json['response'] = '无法连接到服务器';
                    }
                } else {
                    $json['status'] = 403;
                    $json['response'] = '有空项';
                }
                break;
            case 'listmyservers': //列出自己的服务器包括管理员
                $servers = $db->select('servers', ['ID', 'ServerName', 'ServerIP', 'Disconnect', 'Master', 'Admins']);
                $myservers = [];
                foreach ($servers as $server) {
                    if ($server['Master'] == $user['ID']) {
                        $server['type'] = 'owner';
                        array_push($myservers, $server);
                        continue; //防止有憨批设置自己为管理员导致重复显示
                    }
                    if ($server['Admins']) {
                        $admins = explode(',', $server['Admins']);
                        foreach ($admins as $admin) {
                            if ($admin == $user['ID']) {
                                $server['type'] = 'admin';
                                array_push($myservers, $server);
                            }
                        }
                    }
                }
                $json['status'] = 200;
                $json['servers'] = $myservers;
                break;
            case 'loadPlayers': //列出全部玩家
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                if (CheckPermit($server, $user['ID'])) {
                    $players = $db->select('z_server' . $server['ID'], '*');
                    $i = 0;
                    foreach ($players as $player) {
                        $playerdata = $db->select('users', ['ID', 'avatar', 'nickname'], ['OPENID' => $player['OPENID']])[0];
                        if ($playerdata) {
                            $players[$i] += $playerdata;
                        }
                        $i++;
                    }
                    $json['status'] = 200;
                    $json['players'] = $players;
                } else {
                    $json['status'] = 403;
                    $json['response'] = '无权限访问';
                }
                break;
            case 'editserver': //获取服务器信息
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                if (CheckPermit($server, $user['ID']) === 1) {
                    $json['status'] = 200;
                    $json['server'] = $server;
                } else {
                    $json['status'] = 401;
                    $json['response'] = '仅允许超级管理员';
                }
                break;
            case 'saveserverinfo': //保存服务器设置
                $serverid = $_POST['serverid'];
                $servername = $_POST['servername'];
                $serverip = $_POST['serverip'];
                $port = $_POST['port'];
                $token = $_POST['token'];
                $EnableReg = ($_POST['EnableReg'] == 'true') ? 1 : 0;
                $Allowed = $_POST['Allowed'];
                if ($servername && $serverip && $port && $token) {
                    $db->update('servers', ['ServerName' => $servername, 'ServerIP' => $serverip . ':' . $port, 'ServerToken' => $token, 'EnableReg' => $EnableReg, 'Allowed' => $Allowed], ['ID' => $serverid]);
                    $json['status'] = 200;
                } else {
                    $json['status'] = 401;
                    $json['response'] = '有空项';
                }
                break;
            case 'editadmin': //获取管理员信息，只要ID
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                if (CheckPermit($server, $user['ID']) === 1) {
                    $json['status'] = 200;
                    $json['admins'] = $server['Admins'];
                } else {
                    $json['status'] = 401;
                    $json['response'] = '仅允许超级管理员';
                }
                break;
            case 'editban': //获取封禁信息，只要ID
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $json['status'] = 200;
                $json['bans'] = $server['Banned'];
                break;
            case 'deladmin': //取消管理员资格
                $serverid = $_POST['serverid'];
                $userid = $_POST['userid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $admins = explode(',', $server['Admins']);
                $admins = array_diff($admins, [$userid]);
                if ($db->update('servers', ['Admins' => implode(',', $admins)], ['ID' => $server['ID']])) {
                    $json['status'] = 200;
                } else {
                    $json['status'] = 400;
                }
                break;
            case 'delban': //取消封禁
                $serverid = $_POST['serverid'];
                $userid = $_POST['userid'];
                $user2 = $db->select('users', '*', ['ID' => $userid])[0];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $bans = explode(',', $server['Banned']);
                $userbans = explode(',', $user2['Banned']);
                $bans = array_diff($bans, [$userid]);
                $userbans = array_diff($userbans, [$serverid]);
                if ($db->update('servers', ['Banned' => implode(',', $bans)], ['ID' => $server['ID']])) {
                    $db->update('users', ['Banned' => implode(',', $userbans)], ['ID' => $userid]);
                    $json['status'] = 200;
                } else {
                    $json['status'] = 400;
                }
                break;
            case 'userinfo': //用户详细信息
                $userid = $_POST['userid'];
                $serverid = $_POST['serverid'];
                $user = $db->select('users', '*', ['ID' => $userid])[0];
                $player = $db->select('z_server' . $serverid, '*', ['OPENID' => $user['OPENID']])[0];
                if ($user) {
                    $json['status'] = 200;
                    $info['ID'] = $user['ID'];
                    $info['avatar'] = $user['avatar'];
                    $info['nickname'] = $user['nickname'];
                    $info['Joined'] = count(explode(',', $user['Joined']));
                    $info['Banned'] = count(explode(',', $user['Banned']));
                    $info['regdate'] = $player['regdate'];
                    $info['regIP'] = $player['regIP'];
                    $info['playername'] = $player['playername'];
                    $json['userinfo'] = $info;
                } else {
                    $json['status'] = 404;
                    $json['response'] = '用户不存在';
                }
                break;
            case 'setadmin': //设置为管理员
                $userid = $_POST['userid'];
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                if (CheckPermit($server, $userid)) {
                    $json['status'] = 402;
                } else {
                    $admins = explode(',', $server['Admins']);
                    ($admins[0]) ? array_push($admins, $userid) : $admins = [$userid];
                    $db->update('servers', ['Admins' => implode(',', $admins)], ['ID' => $serverid]);
                    $json['status'] = 200;
                }
                break;
            case 'banuser': //封禁用户
                $userid = $_POST['userid'];
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $bans = explode(',', $server['Banned']);
                $user2 = $db->select('users', '*', ['ID' => $userid])[0];
                $userbans = explode(',', $user2['Banned']);
                if (in_array($userid, $bans)) {
                    $json['status'] = 402;
                } else {
                    ($bans[0]) ? array_push($bans, $userid) : $bans = [$userid];
                    $player = $db->select('z_server' . $server['ID'], '*', ['OPENID' => $user2['OPENID']])[0];
                    $db->update('servers', ['Banned' => implode(',', $bans)], ['ID' => $serverid]);
                    @posturl('http://' . $server['ServerIP'] . '/bans/create?identifier=acc:' . $player['playername'] . '&token=' . $server['ServerToken']);

                    if (!in_array($serverid, $userbans)) {
                        ($userbans[0]) ? array_push($userbans, $serverid) : $userbans = [$serverid];
                        $db->update('users', ['Banned' => implode(',', $userbans)], ['ID' => $user2['ID']]);
                    }
                    $json['status'] = 200;
                }
                break;
            case 'unbind': //解绑用户
                $userid = $_POST['userid'];
                $user2 = $db->select('users', '*', ['ID' => $userid])[0];
                $serverid = $_POST['serverid'];
                if ($db->delete('z_server' . $serverid, ['OPENID' => $user2['OPENID']])) {
                    $json['status'] = 200;
                } else {
                    $json['status'] = 403;
                }
                break;
            case 'unbindall': //解绑全部
                $serverid = $_POST['serverid'];
                $sql = "TRUNCATE `xj_z_server$serverid`";
                if ($db->query($sql)) {
                    $json['status'] = 200;
                } else {
                    $json['status'] = 403;
                }
                break;
            case 'listplugin': //列出插件情况
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $url = 'http://' . $server['ServerIP'] . '/xjtool/listall?token=' . $server['ServerToken'];
                $arr = json_decode(@posturl($url), true);
                if ($arr['status'] == 200) {
                    $allplugins = ['RESTInvsee' => ['enabled' => false, 'describe' => '可以查看并编辑用户的背包'], 'BossLimit' => ['enabled' => false, 'describe' => '查看服务器内当前世界进度'], 'ZHIPlayerManager' => ['enabled' => false, 'describe' => '多种数据排行榜'], 'DataManger' => ['enabled' => false, 'describe' => '查看BOSS输出等数据']];
                    foreach ($allplugins as $plgname => $status) {
                        if (in_array($plgname, $arr['Plugins'])) {
                            $allplugins[$plgname]['enabled'] = true;
                        }
                    }
                    $json['status'] = 200;
                    $json['Plugins'] = $allplugins;
                } else if ($arr['status'] == 404) { //没安装插件
                    $json['status'] = 404;
                }
                break;
            case 'switchplugin': //禁用启用插件
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $enabled = $_POST['enabled'];
                $plgname = $_POST['plgname'];
                if ($server && $plgname) {
                    if ($enabled === 'true') {
                        $url = 'http://' . $server['ServerIP'] . '/xjtool/install?PluginName=' . $plgname . '&token=' . $server['ServerToken'];
                    } else {
                        $url = 'http://' . $server['ServerIP'] . '/xjtool/uninstall?PluginName=' . $plgname . '&token=' . $server['ServerToken'];
                    }
                    $arr = json_decode(@posturl($url), true);
                    if ($arr['status'] == 200) {
                        $json['status'] = 200;
                    } else {
                        $json['status'] = 201;
                    }
                } else {
                    $json['status'] = 403;
                }
                break;
            default:
                $json['status'] = 404;
                $json['response'] = '无效参数';
                break;
        }
    } else {
        $json['status'] = 403;
        $json['response'] = 'token无效';
    }
    echo json_encode($json);
    //检查用户权限，0是无权限，1是超级管理员，2是普通管理员
    function CheckPermit($server, $userID)
    {
        if ($server['Master'] == $userID) {
            return 1;
        }
        $admins = explode(',', $server['Admins']);
        foreach ($admins as $admin) {
            if ($admin == $userID) {
                return 2;
            }
        }
        return 0;
    }
}
