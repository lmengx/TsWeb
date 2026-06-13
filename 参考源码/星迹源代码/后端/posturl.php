<?php
function posturl($url)
{
    $data_str='';
    $ch = curl_init();//初始化
    curl_setopt($ch, CURLOPT_CUSTOMREQUEST, "POST");//post方式
    curl_setopt($ch, CURLOPT_URL, $url);//接口地址
    curl_setopt($ch, CURLOPT_POSTFIELDS, $data_str);//输入参数
    //curl_setopt($ch, CURLOPT_TIMEOUT,3);
    curl_setopt($ch, CURLOPT_TIMEOUT_MS, 3000);
    curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);//是否有返回值
    $data = curl_exec($ch);//返回参数
    return $data;
}