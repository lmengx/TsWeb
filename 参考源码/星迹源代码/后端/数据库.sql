-- phpMyAdmin SQL Dump
-- version 5.1.1
-- https://www.phpmyadmin.net/
--
-- 主机： localhost
-- 生成日期： 2025-08-03 19:37:55
-- 服务器版本： 5.7.44-log
-- PHP 版本： 7.2.33

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- 数据库： `miniapp_terraria`
--

-- --------------------------------------------------------

--
-- 表的结构 `xj_users`
--

CREATE TABLE `xj_users` (
  `ID` int(12) NOT NULL COMMENT 'ID',
  `OPENID` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT '腾讯OPENID',
  `TOKEN` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT '小程序登录秘钥',
  `avatar` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT 'QQ头像',
  `nickname` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT 'QQ昵称',
  `Joined` text CHARACTER SET utf8 NOT NULL COMMENT '已加入服务器',
  `Banned` text CHARACTER SET utf8 NOT NULL COMMENT '被封的服务器',
  `LastLogin` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT '上次登录时间',
  `RegTime` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT '注册时间',
  `NoAD` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT '免广告时间'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

--
-- 转储表的索引
--

--
-- 表的索引 `xj_users`
--
ALTER TABLE `xj_users`
  ADD PRIMARY KEY (`ID`);

--
-- 在导出的表使用AUTO_INCREMENT
--

--
-- 使用表AUTO_INCREMENT `xj_users`
--
ALTER TABLE `xj_users`
  MODIFY `ID` int(12) NOT NULL AUTO_INCREMENT COMMENT 'ID';
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
CREATE TABLE `xj_servers` (
  `ID` int(12) NOT NULL COMMENT '服务器ID',
  `Master` int(12) NOT NULL COMMENT '属于那个用户',
  `Admins` text CHARACTER SET utf8 NOT NULL COMMENT '管理员',
  `AddTime` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT '添加时间',
  `ServerName` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT '服务器名称',
  `ServerIP` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT '服务器IP和端口',
  `ServerToken` varchar(255) CHARACTER SET utf8 NOT NULL COMMENT 'REST秘钥',
  `EnableReg` int(1) NOT NULL COMMENT '是否启用注册',
  `Allowed` int(12) NOT NULL COMMENT '允许被封的注册的数量',
  `Alive` int(1) NOT NULL COMMENT '是否有效',
  `Disconnect` int(3) NOT NULL COMMENT '断联时长单位小时',
  `OnlinePlayers` text CHARACTER SET utf8 NOT NULL COMMENT '在线玩家数据',
  `Banned` text CHARACTER SET utf8 NOT NULL COMMENT '封禁的玩家'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

--
-- 转储表的索引
--

--
-- 表的索引 `xj_servers`
--
ALTER TABLE `xj_servers`
  ADD PRIMARY KEY (`ID`),
  ADD UNIQUE KEY `ServerIP` (`ServerIP`);

--
-- 在导出的表使用AUTO_INCREMENT
--

--
-- 使用表AUTO_INCREMENT `xj_servers`
--
ALTER TABLE `xj_servers`
  MODIFY `ID` int(12) NOT NULL AUTO_INCREMENT COMMENT '服务器ID', AUTO_INCREMENT=20;
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;