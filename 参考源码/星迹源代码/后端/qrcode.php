<?php
$serverid=$_GET['id'];
$canvas = imagecreatetruecolor(300, 300);//创建画布
$bgColor = imagecolorallocate($canvas, 255, 255, 255);//设置白色底色
imagefill($canvas, 0, 0, $bgColor);//填充
// 加载图片
$source = imagecreatefrompng('https://minico.qq.com/qrcode/get?type=2&r=1&size=300&b=miniapp&file=jpg&text=https://m.q.qq.com/a/p/1112319434?s=pages/index/index?serverid='.$serverid);

// 获取源图片的尺寸
$source_width = imagesx($source);
$source_height = imagesy($source);
// 将源图片复制到画布上
imagecopy($canvas, $source, 0, 0, 0, 0, $source_width, $source_height);
// 可以在画布上进行其他绘制操作
// 输出画布
header('Content-type: image/jpeg');
imagejpeg($canvas);

// 销毁画布和源图片
imagedestroy($canvas);
imagedestroy($source);

