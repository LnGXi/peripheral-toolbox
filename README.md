# 外设工具箱

轻量 Windows 托盘应用，首个功能是查看外设电量。支持自动发现鼠标、键盘、手柄和已连接的蓝牙设备，点击托盘图标查看列表。

![外设工具箱界面](screenshot.png)

## 使用

下载 Release 中的 `PeripheralBattery.exe` 并双击。首次运行会安装到当前用户的 `%LOCALAPPDATA%\PeripheralBattery`，启用开机启动并显示托盘图标。安装使用当前用户权限。

左键打开设备列表，右键可以刷新、切换开机启动或退出。后台每 60 秒刷新一次，打开列表时也会刷新。需要调整托盘可见性时，可以在 Windows 的托盘设置中将图标设为常显。

主界面显示设备名称和大号电量，悬停查看连接方式及读取详情。“未读取”表示当前读取方式未获取到有效电量。窗口高度随设备数量调整，超过五台时可以滚动查看。

Windows“已安装的应用”中可卸载“外设工具箱 Peripheral Toolbox”。卸载前从托盘退出应用。

需要免安装运行时，使用 `PeripheralBattery.exe --portable`。诊断命令为 `PeripheralBattery.exe --diagnose report.txt`，报告包含设备名称、连接类型和电量结果。

## 跨品牌兼容

发现设备与读取电量分别处理。发现到的设备会列入列表，未获取电量时显示具体原因。

| 读取方式 | 适用范围 | 当前状态 |
| --- | --- | --- |
| Windows 设备电量 | 系统上报电量的蓝牙设备 | 已实现，按物理设备连接状态过滤 |
| Windows 蓝牙实时属性 | 已连接的经典蓝牙和 BLE 设备 | 已实现，作为系统属性的另一来源 |
| BLE 标准电池服务直读 | 提供 Battery Service / Battery Level 的已连接设备 | 已实现，直接读取设备，兼容设备待实测 |
| 漫步者厂商蓝牙服务 | 提供 EDF00000 控制服务的已连接耳机 | W820NB 双金标版已实测成功 |
| 标准 HID Battery Strength | 描述符中声明标准电池 Feature 报告的设备 | 已实现，待兼容设备实测 |
| XInput 电池接口 | Xbox 及兼容手柄 | 已实现，保留低、中、满等档位 |
| DualSense HID 报告 | 原装 DualSense、DualSense Edge，USB 或蓝牙 | 已实现，报文解析已测试，实机待验证 |
| ATK NK NANO | `373B:1216` 接收器，`FF05` 协议接口 | 本机已实测读取百分比 |
| 其他品牌私有接收器 | 自动发现输入设备，电量需要对应协议 | 按型号逐步适配 |
| 飞智黑武士 5 Pro | Flydigi V2 HID 查询 | 本机接收器实测成功，按 20% 档位显示 |

Windows 可能缓存蓝牙电量，因此显示系统最近上报值。DualSense 电量以约 10% 档位估算，界面会标注约值。接收器插着时外设可能休眠或关机，列表中的“接口已发现”只代表接口可访问。第三方转换接收器还需要确认其向 Windows 暴露的协议。

XInput 接口只提供玩家槽位，可能与同一手柄的 HID 项同时出现。设备电量是否可读取决于设备、固件、驱动和连接模式。标准接口与厂商协议适配共同扩展兼容范围。

## 构建与扩展

Windows 10/11，.NET Framework 4.8。使用系统自带 C# 编译器构建。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

源码采用 C# 与 WinForms，无 NuGet 包依赖。`IBatteryProvider` 接入系统级数据来源，`IHidBatteryReader` 接入设备协议。设备枚举按容器合并多个 HID 接口。添加品牌适配时应匹配具体协议接口并校验回复，仅发送已确认的电量查询。

增强蓝牙读取使用 Windows 自带的 WinRT 蓝牙、网络和流接口，构建脚本引用系统元数据和 .NET Framework 程序集。运行时持续保留系统属性读取作为回退。查询设有超时，单个设备失败后继续其他设备。漫步者查询匹配厂商服务 UUID 与物理地址，并校验返回帧。

发布时在本地构建 EXE，并将它与 SHA256 校验值上传到 GitHub Release。

## 第一版验证记录

2026-11-07，本机 ATK 接收器读取到 74%，飞智黑武士 5 Pro 接收器读取到 100%（20% 档位）。EXE 约 36 KB。初版界面的短时后台工作集约 42 MB，打开界面后约 59 MB。3 秒空闲采样的 CPU 时间增量为 0。测量结果随系统与设备变化。

v0.2.0 在 Windows 电量属性为空时，通过漫步者厂商服务实际读取 W820NB 双金标版电量 56%。本次增强的 EXE 约 44 KB。其他漫步者型号和 BLE 标准服务仍需设备实测。

已检查 USB/蓝牙 DualSense 报文解析、异常电池值、短报文、未知接收器匹配、本机设备扫描、安装和自启注册。DualSense 和其他品牌设备仍需实机验证。

卸载通过将运行中的 EXE 移到系统临时目录完成，可能留下一份约 36 KB 的临时副本，供系统临时文件清理。首次发布版本尚无代码签名。

## 协议资料

- Windows XInput 电量接口。[Microsoft 文档](https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetbatteryinformation)
- 标准 HID 值解析。[Microsoft 文档](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getusagevalue)
- ATK 报文说明与开源实现。[atk-tool-plus](https://github.com/papelblume/atk-tool-plus)
- DualSense 报文说明与开源实现。[SDL](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_ps5.c)
- 漫步者电量帧与服务 UUID 资料，C# 查询实现独立编写。[mEDIFIER](https://github.com/wh201906/mEDIFIER)
- BLE 标准电池服务与直接读取接口。[Microsoft 文档](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client)

应用以 MIT 许可证发布。协议参考项目的授权说明见 THIRD_PARTY_NOTICES.md。
