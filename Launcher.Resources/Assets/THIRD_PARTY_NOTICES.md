# THIRD_PARTY_NOTICES

本项目名称：easykatago

easykatago 为“路线A启动器”：仓库与 Release 不包含、也不再分发第三方二进制/资源文件；启动器仅在用户机器上运行时下载，或由用户离线导入。

## 本项目可实现的功能
- 安装向导：初始化默认档案、设置与清单快照。
- 权重管理：下载最新/最强权重，离线导入本地权重，应用到默认档案。
- 档案管理：维护多档案，切换默认档案，自动同步关键参数。
- 启动联动：校验并修正关键路径后启动 KataGo + LizzieYzy。
- 性能调优：基准测试、线程推荐、配置写回。
- 诊断与日志：运行自检、导出诊断包、查看运行日志。

## 启动器依赖项目与网址
- KataGo: https://github.com/lightvector/KataGo
- KataGo Releases: https://github.com/lightvector/KataGo/releases
- LizzieYzy: https://github.com/yzyray/lizzieyzy
- LizzieYzy Releases: https://github.com/yzyray/lizzieyzy/releases
- KataGo Networks: https://katagotraining.org/networks/
- KataGo Training: https://katagotraining.org/

## 许可证与用途说明
- KataGo：MIT License；用于围棋 AI 引擎（二进制由启动器运行时下载）。
- LizzieYzy：GPL-3.0；用于围棋 GUI（由启动器运行时下载）。
- KataGo Networks：以网站公布的 Network License 为准（由启动器运行时下载）。

## 鸣谢
感谢 KataGo、LizzieYzy、KataGo Training 及其社区贡献者对开源围棋生态的持续投入与支持。
