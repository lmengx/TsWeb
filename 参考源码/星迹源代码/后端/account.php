<?php
declare (strict_types = 1);
namespace app\login {

    require './vendor/autoload.php';
    require_once './jwt.php';
    use app\jwt\CheckToken;

    include './connect.php';
    $appid = '小程序ID';
    $appsecret = '小程序秘钥';
    $type = $_POST['type'];
    if ($type == 'login') {
        $code = $_POST['code'];
        $nickName = $_POST['nickName'];
        $avatarUrl = $_POST['avatarUrl'];
        if ($code && $nickName && $avatarUrl) { //既然是login这三个应该都不为空
            $url = "https://api.q.qq.com/sns/jscode2session?appid=$appid&secret=$appsecret&js_code=$code&grant_type=authorization_code";
            $result = file_get_contents($url);
            $data = json_decode($result, true);
            $openid = $data['openid'];
            $session_key = $data['session_key'];
            $token = CheckToken::createJwt($openid);
            $user = $db->select('users', '*', ['OPENID' => $openid])[0];
            if (!$user) { //如果为空则玩家没注册，需要注册
                $noad = date("Y-m-d", strtotime("+3 days", strtotime(date('Y-m-d')))); //新注册用户3天免广告
                $db->insert('users', ['OPENID' => $openid, 'TOKEN' => $token, 'avatar' => $avatarUrl, 'nickname' => $nickName, 'LastLogin' => date('Y-m-d H:i:s'), 'RegTime' => date('Y-m-d H:i:s'), 'NoAD' => $noad]);
                $json['status'] = 200;
                $json['token'] = $token;
                $json['nickName'] = $nickName;
                $json['avatarUrl'] = $avatarUrl;
                $json['noad'] = true;
            } else { //否则就是登录
                $db->update('users', ['TOKEN' => $token, 'avatar' => $avatarUrl, 'nickname' => $nickName, 'LastLogin' => date('Y-m-d H:i:s')], ['OPENID' => $openid]);
                $json['status'] = 200;
                $json['token'] = $token;
                $json['nickName'] = $nickName;
                $json['avatarUrl'] = $avatarUrl;
                $diff = date_diff(date_create(date("Y-m-d")), date_create($user['NoAD']));
                $days = $diff->format("%R%a");
                if ($days > 0) {
                    $json['noad'] = true;
                } else {
                    $json['noad'] = false;
                }
            }
        } else {
            $json['status'] = 403;
        }
    } else if ($type == 'verifytoken') {
        $headers = array();
        foreach ($_SERVER as $key => $value) {
            if (strpos($key, 'HTTP_') === 0) {
                $headers[str_replace('_', ' ', substr($key, 5))] = $value;
            }
        }
        if (empty($headers['AUTHTOKEN'])) {
            $json['status'] = 403;
        } else {
            $user = $db->select('users', '*', ['TOKEN' => $headers['AUTHTOKEN']])[0];
            if (empty($user)) {
                $json['status'] = 403;
            } else {
                $token = $headers['AUTHTOKEN'];
                $verify = CheckToken::verifyJwt($token);
                if ($verify['status'] == 200) {
                    if ($verify['openid'] == $user['OPENID']) {
                        $json['status'] = 200;
                        $json['nickName'] = $user['nickname'];
                        $json['avatarUrl'] = $user['avatar'];
                        $diff = date_diff(date_create(date("Y-m-d")), date_create($user['NoAD']));
                        $days = $diff->format("%R%a");
                        if ($days > 0) {
                            $json['noad'] = true;
                        } else {
                            $json['noad'] = false;
                        }
                    } else {
                        $json['status'] = 401;
                        $json['response'] = 'token非法';
                    }
                } else {
                    $json['status'] = 400;
                    $json['response'] = 'token无效';
                }
            }
        }

    } else {
        $json['status'] = 403;
    }
    echo json_encode($json);
}
