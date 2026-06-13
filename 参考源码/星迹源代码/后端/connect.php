<?php
require 'vendor/autoload.php';
use Medoo\Medoo;
$db = new Medoo([
    'database_type' => 'mysql',
    'database_name' => '你的数据库名',
    'server' => 'localhost',
    'username' => '你的数据库用户名',
    'password' => '数据库密码',
    'charset' => 'utf8',
    'port' => 3306,
    'prefix' => 'xj_',
    'logging' => true,
    'option' => [
        PDO::ATTR_CASE => PDO::CASE_NATURAL,
    ],
    'command' => [
        'SET SQL_MODE=ANSI_QUOTES',
    ],
]);
