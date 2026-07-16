# DemulShooter · xShooterGun DIY 改动说明

- **文档版本**：v1
- **日期**：2026-07-16
- **本 fork 基线**：`upstream = argonlefou/DemulShooter`
- **我方仓库**：`origin = drsonic192/DemulShooter`，分支 `diy-main`
- **说明范围**：记录我方 fork 相对上游的改动。后续新增改动请在 `docs/` 下按「日期+版本号+说明」追加文档。

---

## 本次改动

### `UnityPlugins/UnityPlugin_BepInEx_WWS/UnityLibs/BepInEx.dll`（重编译/替换）

- **起因**：目标游戏自带的 BepInEx 版本与上游仓库内附带的插件依赖版本不一致，导致 WWS（对应游戏）的 Unity 注入插件无法正常挂载。
- **处理**：用与游戏实际匹配的版本重新编译/替换 `UnityLibs/BepInEx.dll`，使 `UnityPlugin_BepInEx_WWS` 能正确加载。
- **性质**：二进制依赖库替换，不涉及 DemulShooter 主程序逻辑源码改动。

> 注：该文件为二进制依赖，纳入我方 fork 管理仅为保证本项目环境下插件版本可复现；不回推 upstream。
