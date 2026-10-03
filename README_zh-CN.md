<div align="center">

<img src="src/LiveCaptions-Translator.ico" width="128" height="128" alt="LiveCaptions Translator图标"/>

# LiveCaptions Translator

### *基于Windows实时字幕的实时音频/语音翻译工具*

**由 [Xiaoyiyi23](https://github.com/Xiaoyiyi23) 维护的分支**，基于 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 增强——[查看本分支的新特性](#本分支的新特性)。

[![Master Build](https://github.com/Xiaoyiyi23/LiveCaptions-Translator/actions/workflows/dotnet-build.yml/badge.svg?branch=master)](https://github.com/Xiaoyiyi23/LiveCaptions-Translator/actions/workflows/dotnet-build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/Xiaoyiyi23/LiveCaptions-Translator?label=Latest&color=yellow)](https://github.com/Xiaoyiyi23/LiveCaptions-Translator/releases/latest)
[![Windows 11](https://img.shields.io/badge/platform-Windows11-blue?logo=windows11&style=&color=1E9BFA)](https://www.microsoft.com/en-us/software-download/windows11)
[![GitHub License](https://img.shields.io/github/license/Xiaoyiyi23/LiveCaptions-Translator)](https://github.com/Xiaoyiyi23/LiveCaptions-Translator/blob/master/LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/Xiaoyiyi23/LiveCaptions-Translator)](https://github.com/Xiaoyiyi23/LiveCaptions-Translator/stargazers)

[English](README.md) | **中文**

</div>

## 概述

**✨ LiveCaptions Translator = Windows实时字幕 + 翻译API ✨**

这是一个无缝集成翻译API与Windows实时字幕的轻量级工具。它可以实现实时语音翻译，无需Copilot+ PC。

Windows内置的实时字幕简单易用，资源占用少，且识别准确率极高。如果为其赋能LLM强大的翻译能力，你将获得...可能是目前最好的实时翻译器！

**🚀 快速开始:** 从[发布页面](https://github.com/SakiRinn/LiveCaptions-Translator/releases)下载并一键启动！

<div align="center">
  <img src="images/preview.png" alt="LiveCaptions Translator预览" width="90%" />
  <br>
  <em style="font-size:80%">LiveCaptions Translator预览</em>
  <br>
</div>

## 功能特性

- **🔄 无缝集成**

  自动调用Windows实时字幕而无需打开单独窗口。为实时音频/语音翻译提供统一体验。

  首次使用后，Windows实时字幕默认将被隐藏。您可以在设置中再次显示它。

  <div align="center">
    <img src="images/show_livecaptions.png" alt="实时字幕显示/隐藏按钮" width="90%" />
    <br>
    <em style="font-size:80%">实时字幕显示/隐藏按钮</em>
    <br>
  </div>

  通过在Windows实时字幕设置中启用 ***包含麦克风音频*** 选项，您可以实现实时语音翻译！
  > ⚠️ **重要:** 您必须在Windows实时字幕中更改源语言！

- **🎨 现代化界面**

  易于使用且简洁的Fluent UI与现代Windows美学保持一致。

  它可以根据系统设置自动在浅色和深色主题🌓之间切换。

  界面提供 English 与简体中文两种语言，默认跟随系统语言，可在设置页实时切换。

- **🌐 多种翻译服务**

  支持各种翻译引擎，包括2个开箱即用的谷歌翻译。

  已实现的翻译引擎如下表所示：

  <div align="center">

  | API                                                 | 类型    | 托管方式 |
  |-----------------------------------------------------|-------|------|
  | [Ollama](https://ollama.com)                        | 基于LLM | 自托管  |
  | OpenAI兼容API                                         | 基于LLM | 在线   |
  | [OpenRouter](https://openrouter.ai)                 | 基于LLM | 在线   |
  | 谷歌翻译                                                | 传统翻译  | 在线   |
  | DeepL                                               | 传统翻译  | 在线   |
  | 有道翻译                                                | 传统翻译  | 在线   |
  | 百度翻译                                                | 传统翻译  | 在线   |
  | [MTranServer](https://github.com/xxnuo/MTranServer) | 传统翻译  | 自托管  |
  | [LibreTranslate](https://libretranslate.com/)       | 传统翻译  | 自托管  |

  </div>

  强烈推荐使用 **基于LLM** 的翻译引擎，因为LLM擅长处理不完整的句子并能很好地理解上下文。

  > **注意：**
  > - *Google2* 使用从 Google 词典 Chrome 扩展中提取的非官方接口，开箱即用但随时可能失效。
  > - API 密钥使用 Windows DPAPI 加密后存储在本机 `setting.json` 中，换电脑后需重新填写。

- **🪟 悬浮窗口**

  打开无边框、透明的悬浮窗口显示字幕，提供最沉浸式的体验。这对游戏、视频和直播等场景非常有用！

  您甚至可以使其完全嵌入到屏幕中，成为屏幕的一部分。这意味着它不会影响您的任何操作！这对游戏玩家来说再合适不过了。

  <div align="center">
    <img src="images/overlay_window.png" alt="悬浮窗口" width="80%" />
    <br>
    <em style="font-size:80%">悬浮窗口</em>
    <br>
  </div>

  您可以在任务栏上打开悬浮窗口，以及调整诸如窗口背景和字幕颜色、字体大小和透明度等参数。极高的可配置性使其能够完全符合您的偏好！

  您可以在设置页的 *Overlay Sentences* 选项调整同时显示的句子数量。

- **⚙️ 灵活控制**

  支持窗口置顶和便利的翻译暂停/恢复，并且您可以一键复制文本以便快速分享或保存。

- **🗃️ 后台常驻**

  关闭主窗口会最小化到系统托盘——翻译、历史记录与悬浮字幕窗在后台继续工作。

  托盘菜单提供快捷操作：显示主窗口、开关悬浮窗、暂停/恢复翻译和退出。支持开机自启（启动后直接进入托盘）与单实例（再次启动时唤起已有窗口）。

- **📒 历史记录管理**

  记录原文和翻译文本，非常适合会议、讲座和重要讨论。

  您可以将所有记录导出为CSV文件或SRT字幕文件。

  <div align="center">
    <img src="images/history.png" alt="翻译历史" width="90%" />
    <br>
    <em style="font-size:80%">翻译历史</em>
    <br>
  </div>

- **🎞️ 日志卡片**

  最近的转录记录可以显示为日志卡片，这有助于您更好地把握上下文。

  您可以在主页任务栏上启用它，并在设置页的 *Log Cards* 选项调整卡片数量。

  <div align="center">
    <img src="images/log_cards.png" alt="日志卡片" width="90%" />
    <br>
    <em style="font-size:80%">日志卡片</em>
    <br>
  </div>


## 系统要求

<div align="center">

| 要求                                                                                                                    | 详情          |
|-----------------------------------------------------------------------------------------------------------------------|-------------|
| <img src="https://img.shields.io/badge/Windows-11%20(22H2+)-0078D6?style=for-the-badge&logo=windows&logoColor=white"> | 支持实时字幕功能    |
| <img src="https://img.shields.io/badge/.NET-8.0+-512BD4?style=for-the-badge&logo=dotnet&logoColor=white">             | 推荐。未在之前版本测试 |

</div>

本工具基于Windows实时字幕，该功能自 **Windows 11 22H2** 起可用。

我们建议您安装 **.NET运行时8.0** 或更高版本。如果您无法安装，可以下载 ***with runtime*** 版本，但其文件较大。

<div align="center">
  <p align="center">
    <a href="https://github.com/SakiRinn/LiveCaptions-Translator/wiki">
      <img src="https://img.shields.io/badge/📚_查看我们的Wiki获取详细信息-2ea44f?style=for-the-badge" alt="查看我们的Wiki">
    </a>
  </p>
</div>

## 入门指南

> ⚠️ **重要:** 首次运行LiveCaptions Translator前，您必须完成以下步骤。
>
> 有关详细信息，请参阅Microsoft的[使用实时字幕](https://support.microsoft.com/zh-cn/windows/使用实时字幕更好地理解音频-b52da59c-14b8-4031-aeeb-f6a47e6055df)指南。

### 步骤1: 验证Windows实时字幕可用性

使用以下任一方法确认您的系统上可用实时字幕：

- 在快速设置中切换 **实时字幕**
- 按 **Win + Ctrl + L**
- 通过 **快速设置** > **辅助功能** > **实时字幕** 访问
- 打开 **开始** > **所有应用** > **辅助功能** > **实时字幕**
- 导航至 **设置** > **辅助功能** > **字幕** 并启用 **实时字幕**

### 步骤2: 配置Windows实时字幕

首次启动时，Windows实时字幕会请求您同意在设备上处理语音数据，并提示您下载用于设备上语音识别的语言文件。

启动Windows实时字幕后，点击 **⚙️齿轮** 图标打开设置菜单，然后选择 **位置** > **覆盖在屏幕上** 。

> ⚠️ **非常重要！** 否则隐藏Windows实时字幕后屏幕会出现显示BUG.

<div align="center">
  <img src="images/speech_recognition.png" alt="语音识别下的项目" width="80%" />
  <br>
  <em style="font-size:80%">需要下载的语音识别组件</em>
  <br>
</div>

配置完成后，关闭Windows实时字幕然后开始使用LiveCaptions Translator吧！🎉

## 本分支的新特性

本仓库是我们维护的 LiveCaptions Translator 版本，在原项目基础上增加了：

- **🌐 多语言界面** —— 支持英文与简体中文，默认跟随系统语言，可在设置页实时切换。
- **🗃️ 托盘常驻** —— 关闭主窗口即最小化到系统托盘，翻译、历史记录与悬浮字幕窗继续工作；支持开机自启与单实例。
- **🎬 SRT 导出** —— 历史记录可导出为 CSV 或 SRT 字幕文件。
- **🛠️ 健壮性与安全加固** —— 线程安全的处理流水线、设置防抖落盘、API 密钥使用 Windows DPAPI 加密存储、后台文件日志。
- **✅ 测试基建** —— 31 个单元测试，覆盖断句、文本工具、密钥保护与本地化。

## 致谢与许可

本项目是 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 的分支（fork）——原始创意与实现的全部荣誉属于 [SakiRinn](https://github.com/SakiRinn) 及原项目的[贡献者](https://github.com/SakiRinn/LiveCaptions-Translator/graphs/contributors)。

<div align="center">
  <a href="https://trendshift.io/repositories/14278" target="_blank"><img src="https://trendshift.io/api/badge/repositories/14278" alt="SakiRinn%2FLiveCaptions-Translator | Trendshift" style="width: 250px; height: 55px;" width="250" height="55"/></a>
</div>

### 原项目活动

<div align="center">
  <img src="https://img.shields.io/github/issues/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Issues&color=yellow" alt="GitHub Issues">
  <img src="https://img.shields.io/github/issues-pr/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Pull%20Requests&color=blue" alt="GitHub Pull Requests">
  <img src="https://img.shields.io/github/discussions/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Discussions&color=orange" alt="GitHub Discussions">
  <img src="https://img.shields.io/github/last-commit/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Last%20Commit&color=purple" alt="GitHub Last Commit">
</div>

### 原项目贡献者

<div align="center">
  <img src="https://img.shields.io/github/contributors/SakiRinn/LiveCaptions-Translator?style=for-the-badge&label=Contributors&color=success" alt="GitHub Contributors">
  <br>
  <a href="https://github.com/SakiRinn/LiveCaptions-Translator/graphs/contributors">
    <img src="https://contrib.rocks/image?repo=SakiRinn/LiveCaptions-Translator" />
  </a>
</div>

### 原项目Star历史

[![Stargazers over time](https://starchart.cc/SakiRinn/LiveCaptions-Translator.svg?variant=adaptive)](https://starchart.cc/SakiRinn/LiveCaptions-Translator)

### 许可证

本项目沿用原项目的 **Apache License 2.0** 许可证。

Copyright (c) 2024 SakiRinn 及其他贡献者。依据 [Apache License, Version 2.0](LICENSE) 授权；除非遵守许可证，否则不得使用这些文件。完整条款见 [LICENSE](LICENSE) 文件。
