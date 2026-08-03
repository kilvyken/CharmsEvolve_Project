# 验证记录

本项目在交付前完成了以下离线检查：

- `CharmsEvolve.csproj` XML 可解析。
- `manifest.json` 与 `Design/charms_source.json` 可解析。
- 设计数据包含 40 个原版槽位；代码按 X/Y/Z 三类生成 120 个复制护符。
- 全部 C# 文件完成字符串、注释和括号结构扫描，没有发现未闭合结构。
- 项目没有直接编译时依赖 `Assembly-CSharp.dll`、PlayMaker 或 Modding API。

当前生成环境没有 C#/.NET 编译器，也没有用户本机的 Hollow Knight、BepInEx 和 Unity Managed DLL，因此**没有执行真实编译或游戏内运行测试**。首次编译后请重点查看 `BepInEx/LogOutput.log` 中的补丁命中数量和反射候选失败信息。
