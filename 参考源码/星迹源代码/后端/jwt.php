<?php
namespace app\jwt {
    require './vendor/autoload.php';
    use Firebase\JWT\Key;
    class CheckToken
    {
        public static function verifyJwt($jwt = '')
        {
            $key = md5('这里设置你的JWT秘钥');
            $msg = [];
            try {
                $jwtAuth = json_encode(\Firebase\JWT\JWT::decode($jwt, new Key($key, 'HS256')));
                $authInfo = json_decode($jwtAuth, true);
                if (!empty($authInfo['user_id'])) {
                    $msg = [
                        'status' => 200,
                        'msg' => 'Token验证通过',
                        'openid' => $authInfo['user_id'],
                    ];
                } else {
                    $msg = [
                        'status' => 404,
                        'msg' => '用户不存在',
                    ];
                }
            } catch (\Firebase\JWT\ExpiredException $e) {
                $msg = [
                    'status' => 401,
                    'msg' => 'token过期',
                ];
            } catch (\Exception $e) {
                $msg = [
                    'status' => 403,
                    'msg' => 'token无效',
                ];
            }
            return $msg;
        }
        public static function createJwt($userId = 'zq')
        {
            $key = md5('这里设置你的JWT秘钥'); //jwt的签发**，验证token的时候需要用到
            $time = time(); //签发时间
            $expire = $time + 86400; //过期时间24个小时
            $token = array(
                "user_id" => $userId,
                "iat" => $time,
                "nbf" => $time,
                "exp" => $expire,
            );
            //        生成token
            $jwt = \Firebase\JWT\JWT::encode($token, $key, 'HS256');
            return $jwt;
        }
    }
}