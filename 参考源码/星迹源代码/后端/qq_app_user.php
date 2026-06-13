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
            case 'listmyservers': //列出自己的服务器
                if ($user['Joined']) {
                    $serid = explode(',', $user['Joined']);
                    $servers = [];
                    foreach ($serid as $id) {
                        $server = $db->select('servers', ['ID', 'ServerName'], ['ID' => $id])[0];
                        if ($server) {
                            array_push($servers, $server);
                        }
                    }
                    $json['status'] = 200;
                    $json['servers'] = $servers;
                } else {
                    $json['status'] = 200;
                    $json['servers'] = [];
                }
                break;
            case 'loadserver': //加载服务器
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                if ($server) {
                    if ($server['EnableReg'] == '1') {
                        $bans = explode(',', $server['Banned']);
                        if (in_array($user['ID'], $bans)) {
                            $json['status'] = 405; //已被服务器封禁，禁止加载
                        } else {
                            if (count(explode(',', $user['Banned'])) > $server['Allowed']) {
                                $json['status'] = 406; //被封太多，禁止加载
                            } else {
                                $player = $db->select('z_server' . $server['ID'], 'playername', ['OPENID' => $user['OPENID']])[0];
                                if ($player) {
                                    $ret = loadserver($server['ServerIP'], $server['ServerToken']);
                                    if ($ret['status'] == 200) {
                                        $json['status'] = 200;
                                        $json['response'] = $ret;
                                    } else {
                                        $json['status'] = 403;
                                        $json['response'] = '无法连接到服务器';
                                    }
                                } else {
                                    $json['status'] = 407; //未注册，先注册才能加载
                                }
                            }
                        }
                    } else { //无需注册，直接返回服务器信息
                        $ret = loadserver($server['ServerIP'], $server['ServerToken']);
                        if ($ret['status'] == 200) {
                            $json['status'] = 200;
                            $json['response'] = $ret;
                        } else {
                            $json['status'] = 403;
                            $json['response'] = '无法连接到服务器';
                        }
                    }
                } else {
                    $json['status'] = 404; //没这个服务器
                }
                break;
            case 'delserver': //移除服务器
                $serverid = $_POST['serverid'];
                $servers = explode(',', $user['Joined']);
                $servers = array_diff($servers, [$serverid]);
                if ($db->update('users', ['Joined' => implode(',', $servers)], ['ID' => $user['ID']])) {
                    $json['status'] = 200;
                } else {
                    $json['status'] = 400;
                }
                break;
            case 'addserver': //添加服务器
                $serverid = $_POST['serverid'];
                $servers = explode(',', $user['Joined']);
                if (in_array($serverid, $servers)) {
                    $json['status'] = 400; //已存在，无需添加
                    //$json['response'] = '已添加过该服务器，无需重复添加';
                } else {
                    $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                    if ($server['Master']) {
                        ($servers[0]) ? array_push($servers, $serverid) : $servers = [$serverid];
                        $db->update('users', ['Joined' => implode(',', $servers)], ['ID' => $user['ID']]);
                        $json['status'] = 200;
                    } else {
                        $json['status'] = 404; //不存在服务器，无法添加
                        $json['response'] = '不存在此服务器，无法添加';
                    }
                }
                break;
            case 'regser': //注册服务器
                $serverid = $_POST['serverid'];
                $password = $_POST['password'];
                $username = $_POST['username'];
                if ($password != '' && $username != '') {
                    $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                    if (!in_array($user['ID'], explode(',', $server['Banned']))) {
                        if (count(explode(',', $user['Banned'])) > $server['Allowed']) {
                            $json['status'] = 403;
                            $json['response'] = '已被其他服务器封禁';
                        } else {
                            $cmd = str_replace(' ', '%20', "/user add $username $password default");
                            $url = "http://" . $server['ServerIP'] . "/v3/server/rawcmd?cmd=$cmd" . "&token=" . $server['ServerToken'];
                            $res = json_decode(@posturl($url), true);
                            if ($res['status'] == 200) {
                                $db->insert('z_server' . $serverid, ['OPENID' => $user['OPENID'], 'playername' => $username, 'regdate' => date('Y-m-d H:i:s'), 'regIP' => getIp()]);
                                $json['status'] = 200;
                            } else {
                                $json['status'] = 403;
                                $json['response'] = '无法连接到服务器';
                            }
                        }
                    } else {
                        $json['status'] = 403;
                        $json['response'] = '已被封禁';
                    }
                } else {
                    $json['status'] = 403;
                    $json['response'] = '有空项';
                }
                break;
            case 'invsee':
                $name = urlencode($_POST['name']);
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $token = $server['ServerToken'];
                $ServerIP = $server['ServerIP'];
                $url = "http://$ServerIP/data/users/invsee?token=$token&player=$name";
                $invsee = json_decode(@posturl($url), true);
                if ($invsee['status'] == 200) {
                    $json['status'] = 200;
                    $json['invsee'] = $invsee;
                } else {
                    $json['status'] = 403;
                }
                break;
            case 'loadrank':
                $type = $_POST['type'];
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $token = $server['ServerToken'];
                $ServerIP = $server['ServerIP'];
                $url = "http://$ServerIP/rankdata?token=$token&type=$type";
                $data = json_decode(@posturl($url), true);
                $json = $data;
                break;
            case 'loadlinechart':
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $onlines = [];
                foreach (json_decode($server['OnlinePlayers']) as $time => $online) {

                    $thistime = array('time' => substr($time, 4), 'count' => $online);
                    array_push($onlines, $thistime);
                }
                $json['status'] = 200;
                $json['onlineplayers'] = $onlines;
                break;
            case 'getallbossinfo':
                $serverid = $_POST['serverid'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $token = $server['ServerToken'];
                $ServerIP = $server['ServerIP'];
                $url = "http://$ServerIP/getallbossinfo?token=$token";
                $json = json_decode(@posturl($url), true);
                break;
            case 'getbossinfo':
                $serverid = $_POST['serverid'];
                $bossid = $_POST['ID'];
                $server = $db->select('servers', '*', ['ID' => $serverid])[0];
                $token = $server['ServerToken'];
                $ServerIP = $server['ServerIP'];
                $url = "http://$ServerIP/getbossinfo?token=$token&ID=$bossid";
                $json = json_decode(@posturl($url), true);
                break;
            case 'noad':
                $newdate = date("Y-m-d", strtotime("+7 days", strtotime(date('Y-m-d'))));
                $db->update('users', ['NoAD' => $newdate], ['ID' => $user['ID']]);
                $json['status'] = 200;
                break;
            default:
                $json['status'] = 404;
                $json['response'] = '无效参数';
                break;
        }
    } else {
        $json['status'] = 403;
        $json['response'] = 'token无效';
        //$json['token'] = $token;
    }
    echo json_encode($json);
    function loadserver($address, $token)
    {
        $str = posturl("http://$address/v2/players/list?token=$token");
        if ($str) {
            //include('trie.php');//过滤违禁词
            //$str = $trie->replace($str);
            $players = json_decode($str, true);
            if ($players['status'] == 200) {
                $boss = [];
                $bossstatus = json_decode(@posturl("http://$address/boss/defeatboss?token=$token"), true);
                if ($bossstatus['status'] == 200) {
                    $bosslist = [0 => 4, 1 => 13, 2 => 35, 3 => 345, 4 => 346, 5 => 325, 7 => 636, 8 => 370, 11 => 246, 12 => 327, 13 => 344, 14 => 392, 15 => 134, 16 => 125, 17 => 127, 18 => 398, 19 => 491, 20 => 262, 21 => 222, 22 => 657, 23 => 50, 24 => 507, 25 => 113, 26 => 517, 27 => 493, 28 => 422, 29 => 439, 30 => 668];
                    foreach ($bosslist as $boss1 => $id) {
                        $boss[$id] = $bossstatus['downedboss'][$boss1];
                    }
                }
                $ret['status'] = 200;
                $ret['players'] = $players;
                $ret['bosslist'] = $boss;
            } else {
                $ret['status'] = 403; //秘钥无效
            }
        } else {
            $ret['status'] = 408; //无法连接到服务器
        }
        return $ret;
    }
    function getIp()
    {

        if (!empty($_SERVER["HTTP_CLIENT_IP"])) {
            $cip = $_SERVER["HTTP_CLIENT_IP"];
        } else if (!empty($_SERVER["HTTP_X_FORWARDED_FOR"])) {
            $cip = $_SERVER["HTTP_X_FORWARDED_FOR"];
        } else if (!empty($_SERVER["REMOTE_ADDR"])) {
            $cip = $_SERVER["REMOTE_ADDR"];
        } else {
            $cip = '';
        }
        preg_match("/[\d\.]{7,15}/", $cip, $cips);
        $cip = isset($cips[0]) ? $cips[0] : 'unknown';
        unset($cips);

        return $cip;
    }
}
