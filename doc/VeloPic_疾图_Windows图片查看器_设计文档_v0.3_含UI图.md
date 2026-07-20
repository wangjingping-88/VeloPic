# VeloPic（疾图）Windows 图片查看器设计文档

> 文档版本：v0.3  
> 日期：2026-07-07  
> 平台：Windows 10 / Windows 11  
> 软件暂定名：**VeloPic**  
> 中文名：**疾图**  
> 标语：**十万图片，瞬间抵达。**
> 本版更新：将 4K 主界面 UI 效果图加入设计文档；主题功能支持“跟随系统 / 深色 / 浅色”，默认跟随系统。

---

## 0. 主界面 UI 效果图

![VeloPic 疾图主界面 UI 效果图：包含递归扫描、GPU 加速状态、主题切换、缩略图网格、右侧图片详情与一键设为壁纸功能](./VeloPic_疾图_主界面_UI_主题功能_4K.png)

**图 1：VeloPic / 疾图主界面 4K UI 方案。** 该界面展示了应用的核心信息架构：左侧导航与图库目录树、中央虚拟化缩略图网格、顶部搜索与扫描工具栏、右侧图片详情与壁纸操作面板，以及底部扫描进度、GPU 加速和缩略图缓存状态。

图中包含本版新增的主题入口：

```text
主题：跟随系统
  ├─ 跟随系统，默认选中
  ├─ 深色
  └─ 浅色
```

视觉方向采用 Windows 11 Fluent / Mica 风格，默认跟随系统主题，同时支持手动切换深色与浅色模式。

## 1. 命名方案

### 1.1 主推名称

- **英文名：VeloPic**
- **中文名：疾图**
- **含义：**
  - `Velo` 来自 velocity，强调速度、流畅、极速浏览。
  - `Pic` 表示图片。
  - 中文名“疾图”短、快、直接，适合作为 Windows 工具类软件名称。

### 1.2 为什么不用“影梭”

“影梭”语义很好，速度感强，但已能检索到其他同名或近似用途的软件、应用、项目结果，不适合作为主品牌名。本文档因此采用 **VeloPic / 疾图** 作为暂定名称。

### 1.3 品牌调性

VeloPic 的品牌关键词：

- 极速
- 本地优先
- 低资源占用
- 美观
- Windows 原生体验
- 大规模图片库友好
- 壁纸能力强

---

## 2. 产品定位

VeloPic 是一个面向 Windows 平台的高性能本地图片查看、浏览、管理和壁纸应用。

它的核心目标不是做一个功能堆砌型相册，而是解决以下痛点：

1. 大目录打开慢。
2. 万级、十万级图片滚动卡顿。
3. 缩略图生成慢、缓存不稳定。
4. 图片切换有明显延迟。
5. Windows 自带看图工具对大图库管理能力不足。
6. 现有第三方工具界面老旧或性能不可控。
7. 设置壁纸、多显示器壁纸、壁纸轮播体验割裂。

### 2.1 一句话定位

> **一个能流畅浏览十万级本地图片库的 Windows 原生极速图片查看器。**

### 2.2 核心卖点

- 递归扫描大目录。
- 十万级图片库秒级进入可浏览状态。
- 可见区域优先生成缩略图。
- GPU 加速显示、缩放、平移、旋转、模糊背景和动效。
- JPEG 热路径使用 CPU SIMD 加速。
- 支持一键设为桌面壁纸。
- 支持多显示器壁纸和壁纸轮播。
- Windows 11 风格界面，支持 Mica、深色模式、浅色模式、跟随系统主题、高 DPI。
- 本地索引、本地缓存、本地数据，不依赖云端。

---

## 3. 设计原则

### 3.1 性能原则

1. UI 线程不做文件 IO。
2. UI 线程不做图片解码。
3. UI 线程不做数据库写入。
4. 大列表必须虚拟化。
5. 缩略图必须缓存。
6. 当前图片永远最高优先级。
7. 可见区域缩略图优先于后台全库索引。
8. 用户快速滚动时，旧视口任务必须取消或降级。
9. 扫描、解码、缩略图、数据库写入分离线程池。
10. 所有硬件加速路径必须有软件 fallback。

### 3.2 产品原则

1. 首屏体验优先于后台完整性。
2. 快速可用优先于一次性完整扫描。
3. 本地性能优先于云功能。
4. 壁纸功能作为一等公民，而不是附属小功能。
5. UI 美观但不能牺牲流畅度。
6. 默认设置适合普通用户，高级设置留给重度用户。

---

## 4. 目标用户

### 4.1 核心用户

- 摄影爱好者。
- 设计师。
- 插画、壁纸、CG、游戏截图收藏用户。
- 程序员、游戏玩家、模型素材管理用户。
- 本地图片量巨大、目录结构复杂的 Windows 用户。

### 4.2 典型场景

1. 打开一个包含 10 万张图片的壁纸目录。
2. 快速滚动查找某类图片。
3. 从大量游戏截图中快速挑选图片。
4. 查看超高分辨率图片。
5. 给不同显示器设置不同壁纸。
6. 按文件名、尺寸、日期、收藏状态搜索。
7. 递归扫描 NAS 或移动硬盘图片目录。

---

## 5. 非目标范围

MVP 阶段不做以下功能：

- 云相册。
- 账号系统。
- 在线同步。
- 复杂图片编辑器。
- 全格式 RAW 专业工作流。
- AI 人脸聚类。
- 跨平台支持。
- 视频素材管理。

这些可以作为后续高级版本或插件功能。

---

## 6. 推荐技术栈

### 6.1 主技术栈

| 模块 | 推荐方案 |
|---|---|
| UI 框架 | WinUI 3 + Windows App SDK |
| 语言 | C++20/23 + C++/WinRT，或 C# UI + C++ Native Core |
| 高性能核心 | C++ Native Core |
| 渲染 | Direct3D 11/12 + Direct2D |
| XAML 与 GPU 集成 | SwapChainPanel |
| 图片解码 | WIC + libjpeg-turbo |
| 数据库 | SQLite + FTS5 |
| 文件扫描 | FindFirstFileExW + FindNextFileW |
| 文件监听 | ReadDirectoryChangesW，后续可选 USN Journal |
| 壁纸 | IDesktopWallpaper |
| 性能分析 | ETW / Windows Performance Analyzer / PIX |

### 6.2 不推荐方案

| 方案 | 不推荐原因 |
|---|---|
| Electron | 内存占用高，启动体积大，极致本地性能难控制 |
| 纯 WPF | 现代 UI 可做，但高性能图像渲染和 GPU 管线控制不如原生 DirectX |
| 纯 WinForms | UI 老旧，不适合现代 Windows 体验 |
| 纯 Avalonia | 跨平台优势明显，但 Windows 极致性能优先时不如原生栈 |

---

## 7. 总体架构

```text
VeloPic
├─ App Shell / UI Layer
│  ├─ WinUI 3 主窗口
│  ├─ 虚拟化图片网格
│  ├─ 沉浸式查看器
│  ├─ 搜索栏 / 筛选器 / 排序器
│  ├─ 壁纸面板
│  └─ 设置页面
│
├─ Core Engine
│  ├─ DirectoryCrawler
│  ├─ FileIndexService
│  ├─ FileWatchService
│  ├─ ImageDecodeService
│  ├─ ThumbnailService
│  ├─ GpuRenderService
│  ├─ WallpaperService
│  ├─ TaskScheduler
│  ├─ CacheManager
│  └─ PerformanceTelemetry
│
├─ Storage Layer
│  ├─ SQLite 图片索引
│  ├─ FTS5 搜索索引
│  ├─ 缩略图 pack cache
│  ├─ 设置数据库
│  └─ 最近访问 / 收藏 / 标签数据
│
└─ Windows Integration Layer
   ├─ WIC
   ├─ Direct2D / Direct3D / DXGI
   ├─ Shell Thumbnail APIs
   ├─ ReadDirectoryChangesW
   ├─ IDesktopWallpaper
   ├─ IFileOperation
   └─ ETW
```

### 7.1 架构核心思想

UI 层只做展示和用户输入。所有重任务都进入 Core Engine 的任务系统：

- 文件枚举。
- 元数据读取。
- 图片解码。
- 缩略图生成。
- 数据库写入。
- GPU 纹理上传。
- 缓存清理。

---

## 8. 递归扫描设计

### 8.1 递归扫描目标

VeloPic 必须支持：

- 对任意目录进行递归扫描。
- 支持万级、十万级图片目录。
- 支持扫描中立即显示结果。
- 支持取消扫描。
- 支持暂停、恢复。
- 支持隐藏目录、系统目录过滤。
- 默认不跟随符号链接和 junction，避免循环。
- 支持网络盘、移动硬盘、NAS 的保守扫描模式。

### 8.2 扫描基本流程

```text
用户选择目录
  ↓
DirectoryCrawler 开始递归枚举
  ↓
只读取轻量文件信息：路径、扩展名、文件大小、修改时间、属性
  ↓
批量写入 SQLite
  ↓
UI 立即增量显示占位卡片
  ↓
可见区域优先生成缩略图
  ↓
后台继续读取图片宽高、EXIF、ICC、hash
  ↓
扫描完成后进入文件监听状态
```

### 8.3 扫描策略

#### 推荐 API

- `FindFirstFileExW`
- `FindNextFileW`
- `FIND_FIRST_EX_LARGE_FETCH`

使用 `FIND_FIRST_EX_LARGE_FETCH` 可以让目录枚举使用更大的内部缓冲区，适合大目录扫描。

#### 不建议一开始使用

- `std::filesystem::recursive_directory_iterator` 作为核心扫描器。

原因是它虽然方便，但对取消、分批、错误处理、长路径、符号链接、网络盘限流、优先级调度的控制不够细。

### 8.4 扫描配置

```cpp
struct ScanOptions {
    bool recursive = true;
    bool followSymlinks = false;
    bool includeHidden = true;
    bool includeSystem = false;
    bool includeCloudPlaceholders = false;
    int maxDepth = -1;
    int batchSize = 1000;
    std::vector<std::wstring> extensions = {
        L".jpg", L".jpeg", L".png", L".webp", L".bmp",
        L".gif", L".tif", L".tiff", L".heic", L".heif", L".avif"
    };
};
```

### 8.5 文件记录结构

```cpp
struct FileRecord {
    std::wstring path;
    std::wstring parentFolder;
    std::wstring filename;
    std::wstring ext;
    uint64_t sizeBytes;
    uint64_t mtimeNs;
    uint32_t attributes;
    std::optional<std::wstring> fileId;
};
```

### 8.6 递归扫描伪代码

```cpp
void DirectoryCrawler::Scan(const std::wstring& root, const ScanOptions& opt) {
    std::deque<DirTask> dirs;
    dirs.push_back({ root, 0 });

    std::vector<FileRecord> batch;
    batch.reserve(opt.batchSize);

    while (!dirs.empty() && !cancelled_) {
        auto dir = dirs.front();
        dirs.pop_front();

        std::wstring pattern = JoinPath(dir.path, L"*");

        WIN32_FIND_DATAW fd{};
        HANDLE h = FindFirstFileExW(
            pattern.c_str(),
            FindExInfoBasic,
            &fd,
            FindExSearchNameMatch,
            nullptr,
            FIND_FIRST_EX_LARGE_FETCH
        );

        if (h == INVALID_HANDLE_VALUE) {
            ReportScanError(dir.path, GetLastError());
            continue;
        }

        do {
            if (IsDotOrDotDot(fd.cFileName)) {
                continue;
            }

            auto fullPath = JoinPath(dir.path, fd.cFileName);
            bool isDir = (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
            bool isReparse = (fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;

            if (isDir) {
                if (ShouldEnterDirectory(fd, opt, isReparse, dir.depth)) {
                    dirs.push_back({ fullPath, dir.depth + 1 });
                }
                continue;
            }

            if (!IsSupportedImageExtension(fd.cFileName, opt.extensions)) {
                continue;
            }

            batch.push_back(MakeFileRecord(fullPath, fd));

            if (batch.size() >= opt.batchSize) {
                EmitBatch(batch);
                batch.clear();
            }

        } while (FindNextFileW(h, &fd));

        FindClose(h);
    }

    if (!batch.empty()) {
        EmitBatch(batch);
    }
}
```

### 8.7 扫描注意事项

1. **不要扫描时读取所有 EXIF。** 这会显著拖慢首轮扫描。
2. **不要每发现一个文件就通知 UI。** 应批量通知。
3. **不要默认跟随 reparse point。** 可能造成循环。
4. **不要默认触发 OneDrive 云占位文件下载。**
5. **网络盘应降低并发。** SMB/NAS 场景过高并发可能更慢。
6. **长路径必须处理。** 建议内部统一支持 Windows 长路径。
7. **扫描任务必须可取消。** 用户切换目录时应立即停止旧任务。

---

## 9. 文件变更监听

### 9.1 基础方案

首次扫描完成后，启动 `FileWatchService`：

```text
ReadDirectoryChangesW
  ↓
监听新增 / 删除 / 修改 / 重命名
  ↓
事件合并与防抖
  ↓
更新 SQLite
  ↓
刷新 UI
  ↓
使旧缩略图缓存失效
```

### 9.2 监听策略

- 当前打开目录：立即监听。
- 用户收藏的图库目录：后台监听。
- 大量目录：按需监听，避免句柄过多。
- 网络盘：监听失败时回退为定期轻量扫描。

### 9.3 USN Journal 进阶方案

对于 NTFS 本地卷，可以在高级版本中引入 USN Journal 增量同步：

- 优点：适合超大图库增量变更追踪。
- 缺点：实现复杂，需要处理卷、权限、文件引用号映射。
- 建议：Phase 3 或 Phase 4 再做。

---

## 10. 硬件加速设计

### 10.1 基本判断

GPU 不用于目录扫描。目录扫描主要受文件系统和 IO 限制。

GPU 主要用于：

- 图片显示。
- 大图缩放。
- 平移。
- 旋转。
- 模糊背景。
- 颜色转换。
- UI 动效。
- 后续 AI 推理或图像增强。

JPEG 解码主要使用 CPU SIMD；PNG、HEIC、AVIF 等使用 WIC 或专用 codec。

### 10.2 硬件加速模式

设置项：

```text
硬件加速
├─ 自动，推荐
├─ 高性能
├─ 省电
└─ 关闭
```

#### 自动模式

- 台式机或接电状态：优先高性能 GPU。
- 笔记本电池状态：优先集成 GPU，后台缩略图降速。
- 远程桌面、虚拟机、WARP：降低动画和纹理缓存。
- GPU device removed：自动重建设备，失败则进入软件渲染。

#### 高性能模式

- 优先独显。
- 增大 GPU texture cache。
- 更积极预加载前后图。
- 可见缩略图更高质量 downscale。

#### 省电模式

- 优先核显。
- 降低后台并发。
- 减少动画。
- 减少大图预加载。

#### 关闭

- 使用 WIC / CPU 路径渲染。
- 仅保留基础 UI。
- 作为兼容性 fallback。

### 10.3 AccelerationManager

```text
HardwareAccelerationManager
├─ DetectCpuFeatures
│  ├─ SSE2
│  ├─ AVX2
│  └─ AVX-512，可选
│
├─ DetectGpuAdapters
│  ├─ DXGI adapter enumeration
│  ├─ High performance GPU
│  ├─ Integrated GPU
│  └─ WARP fallback
│
├─ CreateGraphicsDevice
│  ├─ D3D11 / D3D12 device
│  ├─ Direct2D device
│  └─ WIC imaging factory
│
├─ RuntimePolicy
│  ├─ AC / Battery
│  ├─ foreground / background
│  ├─ thermal / memory pressure
│  └─ GPU reset handling
│
└─ TelemetryFeedback
   ├─ decode latency
   ├─ upload latency
   ├─ frame time
   └─ cache hit rate
```

### 10.4 GPU 渲染管线

```text
ImageDecodeService
  ↓
CPU bitmap / WIC bitmap
  ↓
EXIF orientation 修正
  ↓
颜色空间转换，必要时
  ↓
上传 GPU texture
  ↓
Direct2D / Direct3D 渲染
  ↓
SwapChainPanel 呈现
```

### 10.5 GPU 资源原则

- 只上传当前视图需要的图片。
- 不把 10 万张缩略图全部变成 GPU texture。
- 当前图、上一张、下一张保留 GPU texture。
- filmstrip 可以使用 texture atlas。
- 缩略图网格只保留可见区域和附近区域 texture。
- GPU 显存紧张时主动释放低优先级资源。

---

## 11. 图片解码设计

### 11.1 解码路径

| 格式 | 推荐解码路径 |
|---|---|
| JPEG / JPG | libjpeg-turbo 优先，WIC fallback |
| PNG | WIC |
| BMP | WIC |
| GIF | WIC，动图后续增强 |
| TIFF | WIC |
| WebP | WIC 或 libwebp |
| HEIC / HEIF | WIC + 系统扩展 |
| AVIF | WIC 扩展或 libavif |
| RAW | 后续插件化支持 |

### 11.2 解码原则

1. 缩略图不要全尺寸解码。
2. 查看器初次打开只解码屏幕适配尺寸。
3. 用户缩放到 100% 时再解码原始尺寸。
4. 超大图采用 tile 或多级金字塔。
5. 解码失败要缓存错误状态，避免重复卡队列。
6. 当前图解码可抢占后台扫描和缩略图任务。

### 11.3 JPEG 热路径

现实图片库中 JPEG 占比通常很高，因此 JPEG 要有专门优化路径：

```text
JPEG 文件
  ↓
libjpeg-turbo 解码
  ↓
EXIF orientation 修正
  ↓
输出 BGRA/RGBA buffer
  ↓
上传 GPU texture
```

### 11.4 WIC 通用路径

```text
非 JPEG 文件
  ↓
WIC decoder
  ↓
按目标尺寸缩放解码
  ↓
读取 metadata / orientation / ICC
  ↓
输出统一像素格式
  ↓
上传 GPU texture
```

---

## 12. 缩略图系统

### 12.1 缩略图目标

- 首屏缩略图优先生成。
- 滚动不卡顿。
- 缓存命中后目录秒开。
- 10 万张图也不会产生 10 万个散碎小文件。
- 缩略图失效准确。

### 12.2 缩略图来源层级

```text
L0：内存 LRU 缓存
L1：自研磁盘 pack cache
L2：Windows Shell Thumbnail Cache
L3：自行解码原图生成
```

### 12.3 缩略图尺寸

| 尺寸 | 用途 |
|---|---|
| 128 px | 密集网格、快速列表 |
| 256 px | 默认缩略图 |
| 512 px | 高 DPI、大缩略图、悬浮预览 |
| screen-fit | 查看器初始预览 |

### 12.4 pack cache 设计

```text
thumb_cache/
├─ index.sqlite
└─ packs/
   ├─ 000001.pack
   ├─ 000002.pack
   └─ 000003.pack
```

不建议每个缩略图一个文件。pack cache 可以减少 NTFS 小文件数量，提高缓存管理效率。

### 12.5 thumbs 表

```sql
CREATE TABLE thumbs (
    image_id INTEGER NOT NULL,
    size INTEGER NOT NULL,
    format TEXT NOT NULL,
    pack_id INTEGER NOT NULL,
    offset INTEGER NOT NULL,
    length INTEGER NOT NULL,
    mtime_ns INTEGER NOT NULL,
    file_size INTEGER NOT NULL,
    last_used INTEGER NOT NULL,
    PRIMARY KEY(image_id, size)
);
```

### 12.6 可见区域优先策略

```text
P0：当前打开图片
P1：当前视口缩略图
P2：视口前后 1～2 屏缩略图
P3：当前目录未缓存缩略图
P4：全库后台缩略图补全
```

### 12.7 CPU 与 GPU 取舍

可见区域缩略图：

- 优先快速出图。
- 可使用 GPU 辅助缩放和显示。

后台批量缩略图：

- 优先 CPU SIMD / WIC scale。
- 避免 GPU readback 导致额外成本。

---

## 13. 任务调度系统

### 13.1 为什么需要自研任务调度

大规模图片应用的主要性能问题不是“不会多线程”，而是“任务优先级错误”。

错误示例：

```text
后台正在给 10 万张图片生成缩略图
用户打开当前图片
当前图片排在队尾
用户感知为卡顿
```

正确设计：

```text
当前图片任务抢占后台任务
可见缩略图任务抢占后台索引任务
离开视口的任务取消或降级
```

### 13.2 任务优先级

| 优先级 | 任务 |
|---|---|
| P0 | 当前正在查看的图片解码和渲染 |
| P1 | 当前视口内缩略图 |
| P2 | 当前图片前后 2～5 张预加载 |
| P3 | 视口附近缩略图 |
| P4 | 元数据解析 |
| P5 | hash / 重复图片检测 |
| P6 | 缓存清理 |

### 13.3 线程池建议

| 线程池 | 默认线程数 |
|---|---:|
| UI Thread | 1 |
| Directory Enumerator Pool | 1～4 |
| IO Pool | 2～4 |
| Decode Pool | min(CPU 核心数 - 2, 6) |
| Thumbnail Pool | 2～6 |
| DB Writer | 1 |
| Hash Worker | 1～2 |
| Render Thread | 1 |

### 13.4 取消机制

任务必须支持 cancellation token：

- 用户切换目录：取消旧目录扫描。
- 用户快速滚动：取消旧视口缩略图。
- 用户打开图片：提升当前图任务优先级。
- 内存压力变大：取消低优先级预加载。

---

## 14. 数据库设计

### 14.1 SQLite 配置

推荐：

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA temp_store = MEMORY;
PRAGMA mmap_size = 268435456;
```

说明：

- WAL 支持单写多读。
- 批量写入减少事务成本。
- DB Writer 单线程写入，其他模块读。

### 14.2 images 表

```sql
CREATE TABLE images (
    id INTEGER PRIMARY KEY,
    path TEXT NOT NULL UNIQUE,
    parent_folder TEXT NOT NULL,
    filename TEXT NOT NULL,
    ext TEXT NOT NULL,
    size_bytes INTEGER,
    mtime_ns INTEGER,
    file_id TEXT,
    width INTEGER,
    height INTEGER,
    orientation INTEGER,
    date_taken INTEGER,
    camera_make TEXT,
    camera_model TEXT,
    rating INTEGER DEFAULT 0,
    favorite INTEGER DEFAULT 0,
    phash TEXT,
    indexed_at INTEGER,
    missing INTEGER DEFAULT 0,
    error TEXT
);
```

### 14.3 folders 表

```sql
CREATE TABLE folders (
    id INTEGER PRIMARY KEY,
    path TEXT UNIQUE,
    recursive INTEGER,
    last_scan_at INTEGER,
    file_count INTEGER,
    watch_enabled INTEGER DEFAULT 1
);
```

### 14.4 tags 表

```sql
CREATE TABLE tags (
    id INTEGER PRIMARY KEY,
    name TEXT UNIQUE
);

CREATE TABLE image_tags (
    image_id INTEGER,
    tag_id INTEGER,
    PRIMARY KEY(image_id, tag_id)
);
```

### 14.5 albums 表

```sql
CREATE TABLE albums (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    created_at INTEGER NOT NULL
);

CREATE TABLE album_items (
    album_id INTEGER,
    image_id INTEGER,
    sort_order INTEGER,
    PRIMARY KEY(album_id, image_id)
);
```

### 14.6 搜索索引

```sql
CREATE VIRTUAL TABLE image_fts USING fts5(
    filename,
    path,
    tags,
    camera_model,
    content=''
);
```

### 14.7 常用索引

```sql
CREATE INDEX idx_images_parent ON images(parent_folder);
CREATE INDEX idx_images_filename ON images(filename);
CREATE INDEX idx_images_mtime ON images(mtime_ns);
CREATE INDEX idx_images_size ON images(size_bytes);
CREATE INDEX idx_images_favorite ON images(favorite);
CREATE INDEX idx_images_dimensions ON images(width, height);
```

---

## 15. UI 设计

本章对应的主界面 4K 效果图见文档前部 **“0. 主界面 UI 效果图”**。该图是当前 UI 信息架构、视觉风格、主题入口、右侧详情面板和壁纸操作面板的基准方案。

### 15.1 主界面

```text
┌─────────────────────────────────────────────┐
│ 顶部命令栏：路径 / 搜索 / 排序 / 视图 / 主题 / 设置 │
├───────────────┬─────────────────────────────┤
│ 左侧导航       │ 图片网格                     │
│ - 文件夹       │ [thumb][thumb][thumb]        │
│ - 收藏         │ [thumb][thumb][thumb]        │
│ - 相册         │ [thumb][thumb][thumb]        │
│ - 标签         │                             │
│ - 最近         │                             │
└───────────────┴─────────────────────────────┘
```

### 15.2 图片网格

必须满足：

- 虚拟化。
- 只创建可见区域 UI 元素。
- 支持不同缩略图尺寸。
- 支持键盘选择。
- 支持框选。
- 支持右键菜单。
- 支持排序和筛选。
- 支持滚动任务取消。

### 15.3 沉浸式查看器

```text
黑色 / 半透明背景
中央图片
底部 filmstrip
右侧信息面板，可隐藏
顶部工具栏，自动隐藏
```

功能：

- 上一张 / 下一张。
- 鼠标滚轮缩放。
- 拖拽平移。
- 双击适配窗口 / 100%。
- 全屏。
- 收藏。
- 删除。
- 设为壁纸。
- 查看 EXIF。

### 15.4 视觉风格

- Windows 11 Fluent 风格。
- 主窗口 Mica。
- 弹出菜单 Acrylic。
- 圆角缩略图。
- 轻量淡入，不使用过度动画。
- 主题模式：跟随系统 / 深色 / 浅色，默认跟随系统。
- 高 DPI 适配。
- 多显示器适配。

### 15.5 主题功能

主题功能作为基础设置项提供，默认值为 **跟随系统**。

#### 15.5.1 主题模式

| 模式 | 说明 | 默认状态 |
|---|---|---|
| 跟随系统 | 根据 Windows 系统应用主题自动切换深色或浅色 | 默认启用 |
| 深色 | 强制使用深色界面，适合夜间浏览和沉浸式看图 | 可手动选择 |
| 浅色 | 强制使用浅色界面，适合白天办公和高亮环境 | 可手动选择 |

#### 15.5.2 UI 入口

主题切换入口放在：

```text
设置
  └─ 外观
      ├─ 主题
      │   ├─ 跟随系统，默认
      │   ├─ 深色
      │   └─ 浅色
      ├─ 使用 Mica 背景
      ├─ 使用 Acrylic 弹出层
      ├─ 使用系统强调色
      └─ 缩略图圆角 / 间距
```

也可以在顶部命令栏的“设置”按钮中增加轻量快捷菜单：

```text
设置 ▾
  ├─ 外观主题：跟随系统 / 深色 / 浅色
  ├─ 缩略图大小
  ├─ 硬件加速
  └─ 打开完整设置
```

#### 15.5.3 行为要求

- 首次启动默认读取系统应用主题。
- 用户未手动选择主题时，系统主题变化后应用自动切换。
- 用户手动选择“深色”或“浅色”后，不再跟随系统变化。
- 切换主题不需要重启应用。
- 主题切换时保留当前目录、选中图片、滚动位置、缩放状态和右侧面板状态。
- 沉浸式查看器默认使用深色背景，但在浅色主题下仍允许用户选择“深色看图背景”。
- Mica / Acrylic 可独立关闭，低性能设备或远程桌面环境下可自动降级为纯色背景。

#### 15.5.4 配置字段

```json
{
  "appearance": {
    "theme": "system",
    "useMica": true,
    "useAcrylic": true,
    "useSystemAccentColor": true,
    "viewerBackground": "auto",
    "thumbnailCornerRadius": 8,
    "thumbnailSpacing": 8
  }
}
```

字段说明：

| 字段 | 可选值 | 说明 |
|---|---|---|
| `theme` | `system` / `dark` / `light` | 应用主题，默认 `system` |
| `useMica` | `true` / `false` | 是否启用主窗口 Mica 背景 |
| `useAcrylic` | `true` / `false` | 是否启用弹出层 Acrylic 效果 |
| `useSystemAccentColor` | `true` / `false` | 是否跟随 Windows 强调色 |
| `viewerBackground` | `auto` / `dark` / `light` / `blur` | 查看器背景策略 |
| `thumbnailCornerRadius` | 数值 | 缩略图圆角半径 |
| `thumbnailSpacing` | 数值 | 缩略图间距 |

#### 15.5.5 实现建议

WinUI 3 中建议通过应用级主题状态统一驱动资源字典，不要在每个控件中硬编码颜色。推荐使用语义化 token：

```text
Background/App
Background/Panel
Background/Card
Text/Primary
Text/Secondary
Border/Subtle
Accent/Primary
Accent/Hover
Accent/Selected
Status/Success
Status/Warning
Status/Error
```

主题切换流程：

```text
用户切换主题
  ↓
写入设置 theme
  ↓
更新 AppThemeService 当前状态
  ↓
替换资源字典 / 更新 RequestedTheme
  ↓
刷新窗口背景材质 Mica/Acrylic
  ↓
通知渲染层更新查看器背景、缩略图选中态、图标颜色
```

### 15.6 快捷键

| 快捷键 | 功能 |
|---|---|
| Enter / 双击 | 打开查看器 |
| Esc | 返回网格 / 退出全屏 |
| ← / → | 上一张 / 下一张 |
| Space | 下一张 |
| Ctrl + 鼠标滚轮 | 缩放 |
| 0 | 适配窗口 |
| 1 | 100% 显示 |
| F | 收藏 |
| W | 设为壁纸 |
| Delete | 删除到回收站 |
| Shift + Delete | 永久删除 |
| Ctrl + F | 搜索 |
| I | 信息面板 |
| F11 | 全屏 |

---

## 16. 壁纸功能设计

### 16.1 壁纸功能定位

VeloPic 的壁纸功能不是简单调用系统设置，而是面向壁纸收藏用户提供完整体验：

- 一键设壁纸。
- 多显示器壁纸。
- 裁切预览。
- 适配方式选择。
- 壁纸收藏夹。
- 自动轮播。
- 撤销上一次设置。

### 16.2 右键菜单

```text
设为壁纸
├─ 当前显示器
├─ 所有显示器
├─ 显示器 1
├─ 显示器 2
├─ 适配方式
│  ├─ 填充
│  ├─ 适应
│  ├─ 拉伸
│  ├─ 居中
│  └─ 平铺
├─ 裁切预览
└─ 添加到壁纸轮播
```

### 16.3 壁纸设置流程

```text
用户点击设为壁纸
  ↓
读取显示器分辨率、DPI、方向
  ↓
根据适配方式生成预览
  ↓
导出到 AppData/Local/VeloPic/Wallpapers/
  ↓
调用 IDesktopWallpaper::SetWallpaper
  ↓
保存旧壁纸配置
  ↓
Toast 提示：已设为壁纸 / 撤销
```

### 16.4 为什么要导出适配后的壁纸

直接把原图交给系统会遇到以下问题：

- 竖图用于横屏效果不可控。
- 超宽图、多屏图裁切不稳定。
- 透明 PNG 可能显示异常。
- HEIC/AVIF 依赖系统支持。
- 多显示器分辨率不同。

导出适配图可以确保最终显示效果可控。

### 16.5 壁纸轮播

```text
壁纸收藏夹
├─ 每 15 分钟更换
├─ 每 30 分钟更换
├─ 每 1 小时更换
├─ 每天更换
├─ 随机顺序
├─ 按标签过滤
├─ 只使用横图
├─ 只使用高分辨率图片
└─ 每个显示器独立轮播
```

---

## 17. 文件管理设计

### 17.1 支持操作

- 删除到回收站。
- 永久删除。
- 重命名。
- 移动到文件夹。
- 复制到文件夹。
- 打开所在文件夹。
- 复制文件路径。
- 批量操作。

### 17.2 实现建议

文件删除、移动、复制、重命名建议使用 Windows Shell 的 `IFileOperation`，这样可以更好地符合系统行为，并支持回收站、权限提示和系统进度体验。

### 17.3 数据一致性

- 文件操作成功后再更新数据库。
- 文件操作失败时保留原状态。
- 批量操作需要逐项记录错误。
- 删除到回收站后，应从当前视图移除，但可支持撤销。

---

## 18. 性能目标

### 18.1 测试环境

至少覆盖：

- Windows 10。
- Windows 11。
- 16GB 内存中端机器。
- NVMe SSD。
- SATA SSD。
- HDD。
- 网络盘 / NAS。
- 1 万、5 万、10 万、20 万图片集。

### 18.2 目标指标

| 场景 | 目标 |
|---|---:|
| 冷启动到窗口可交互 | < 500 ms |
| 已索引 10 万图目录打开 | < 1 s 显示网格 |
| 未索引目录打开 | < 1 s 显示占位 |
| 首屏缩略图缓存命中 | < 300 ms |
| 首屏缩略图无缓存 | 0.3～1.5 s 逐步出现 |
| 快速滚动 | 60 FPS，理想 120 FPS |
| 普通 JPEG 打开 | < 100 ms 显示屏幕适配预览 |
| 预加载命中切图 | < 30 ms |
| 文件名搜索 | < 50 ms |
| 常规浏览内存占用 | < 500 MB，可配置 |
| 常用目录缩略图命中率 | > 95% |

### 18.3 遥测指标

- 扫描速度，files/s。
- 文件枚举耗时。
- 元数据解析耗时。
- 图片解码耗时。
- 缩略图生成耗时。
- GPU 上传耗时。
- frame time。
- cache hit / miss。
- DB 写入耗时。
- 任务取消次数。
- 内存使用。
- GPU 显存使用。

---

## 19. 错误处理与边界情况

### 19.1 文件系统

需要处理：

- 权限不足。
- 文件被占用。
- 文件路径过长。
- 文件不存在。
- 移动硬盘断开。
- 网络盘断开。
- 符号链接循环。
- OneDrive 云占位文件。
- 文件名非法字符边界。

### 19.2 图片文件

需要处理：

- 损坏图片。
- 扩展名与实际格式不一致。
- 超大图片。
- CMYK JPEG。
- 奇怪的 ICC profile。
- 错误 EXIF orientation。
- 动图第一帧。
- 透明通道。
- 16-bit / HDR 图片。

### 19.3 GPU

需要处理：

- GPU device removed。
- 驱动崩溃。
- WARP fallback。
- 显存不足。
- 远程桌面环境。
- 多 GPU 切换。

---

## 20. 开发路线图

### Phase 0：技术验证，1～2 周

目标：证明关键技术链路可行。

任务：

- WinUI 3 空壳。
- D3D11 + Direct2D + SwapChainPanel 显示图片。
- WIC 解码 PNG/JPEG。
- libjpeg-turbo 解码 JPEG。
- FindFirstFileExW 递归扫描 demo。
- SQLite 写入 10 万条路径。
- 虚拟化网格 demo。
- IDesktopWallpaper 设壁纸 demo。

### Phase 1：MVP，4～6 周

目标：可日常使用。

功能：

- 文件夹浏览。
- 递归扫描。
- 基础索引。
- 缩略图网格。
- 单图查看。
- 上一张 / 下一张。
- 缩放 / 平移。
- 收藏。
- 文件名搜索。
- 一键设壁纸。
- 基础设置。
- 主题切换：跟随系统 / 深色 / 浅色，默认跟随系统。
- 基础缓存。
- EXIF orientation。

### Phase 2：性能强化，4～8 周

目标：十万级稳定。

功能：

- 缩略图 pack cache。
- 任务优先级调度。
- 快速滚动任务取消。
- 前后图片预加载。
- 内存上限控制。
- SQLite FTS5。
- ReadDirectoryChangesW 文件监听。
- 多线程 decode pool。
- 大图降级预览。
- ETW 埋点。

### Phase 3：体验打磨，4～6 周

目标：比市面常见工具更顺手。

功能：

- Mica / Acrylic。
- 外观细节设置：系统强调色、查看器背景、缩略图圆角和间距。
- 完整右键菜单。
- 快捷键体系。
- 信息面板。
- 批量删除 / 移动 / 复制。
- 相册。
- 标签。
- 壁纸收藏夹。
- 多显示器壁纸管理。
- Toast 撤销。

### Phase 4：高级功能

候选功能：

- 重复图片检测。
- 感知 hash。
- 相似图片搜索。
- RAW 插件。
- HDR / wide gamut 支持。
- OCR 搜图。
- AI 标签。
- 人脸聚类。
- 视频缩略图。
- NAS 优化。
- 便携版。
- 插件系统。

---

## 21. MVP 功能清单

### 必须有

- Windows 原生窗口。
- 文件夹选择。
- 递归扫描。
- 扫描中增量显示。
- 缩略图网格。
- 单图查看器。
- 上一张 / 下一张。
- 鼠标滚轮缩放。
- 拖拽平移。
- 收藏。
- 搜索文件名。
- 一键设为壁纸。
- 删除到回收站。
- 打开所在文件夹。
- 设置页。
- 主题设置：跟随系统 / 深色 / 浅色，默认跟随系统。

### 应该有

- GPU 加速渲染。
- JPEG 快速解码。
- 缩略图磁盘缓存。
- 多显示器壁纸。
- 主题模式：跟随系统 / 深色 / 浅色。
- 快捷键。
- 基础 EXIF 信息。

### 可以后置

- 相似图片搜索。
- AI 分类。
- RAW。
- 视频。
- 地图。
- 云同步。

---

## 22. 推荐目录结构

```text
VeloPic/
├─ src/
│  ├─ app/
│  │  ├─ MainWindow.xaml
│  │  ├─ Views/
│  │  └─ ViewModels/
│  │
│  ├─ core/
│  │  ├─ scan/
│  │  ├─ index/
│  │  ├─ decode/
│  │  ├─ thumbnail/
│  │  ├─ render/
│  │  ├─ wallpaper/
│  │  ├─ scheduler/
│  │  ├─ cache/
│  │  └─ telemetry/
│  │
│  ├─ platform/
│  │  ├─ windows/
│  │  ├─ directx/
│  │  ├─ wic/
│  │  └─ shell/
│  │
│  └─ third_party/
│     ├─ sqlite/
│     ├─ libjpeg-turbo/
│     └─ fmt/
│
├─ tests/
│  ├─ unit/
│  ├─ integration/
│  └─ performance/
│
├─ tools/
│  ├─ dataset-generator/
│  └─ benchmark-runner/
│
└─ docs/
   ├─ architecture.md
   ├─ performance.md
   └─ roadmap.md
```

---

## 23. 关键风险

### 23.1 技术风险

| 风险 | 应对 |
|---|---|
| WinUI 3 大量图片元素卡顿 | 使用虚拟化，必要时自绘网格 |
| GPU device removed | 自动重建设备，fallback 软件路径 |
| 缩略图生成拖慢 UI | 优先级调度 + 取消任务 |
| 十万小文件缓存拖慢 NTFS | 使用 pack cache |
| 网络盘扫描慢 | 限流 + 增量 + 可取消 |
| HEIC/AVIF codec 不稳定 | WIC fallback + 明确提示安装扩展 |
| 超大图内存爆炸 | screen-fit decode + tile 渲染 |

### 23.2 产品风险

| 风险 | 应对 |
|---|---|
| 功能范围膨胀 | MVP 聚焦浏览、看图、壁纸 |
| UI 好看但性能下降 | 动效可降级，性能指标优先 |
| 用户图库结构复杂 | 递归扫描、过滤、索引、错误报告完善 |
| 与成熟工具竞争 | 差异化：十万级性能 + 壁纸体验 + 原生美观 |

---

## 24. 官方参考链接

以下链接用于后续实现时查阅官方接口和技术细节：

- WinUI 3：
  - https://learn.microsoft.com/windows/apps/winui/winui3/
- SwapChainPanel：
  - https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swapchainpanel
- Direct2D：
  - https://learn.microsoft.com/windows/win32/direct2d/direct2d-overview
- Direct2D 性能建议：
  - https://learn.microsoft.com/windows/win32/direct2d/improving-direct2d-performance
- WIC：
  - https://learn.microsoft.com/windows/win32/wic/-wic-about-windows-imaging-codec
- WIC metadata：
  - https://learn.microsoft.com/windows/win32/wic/-wic-codec-metadataquerylanguage
- WIC color management：
  - https://learn.microsoft.com/windows/win32/wic/-wic-colormanagement
- FindFirstFileExW：
  - https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-findfirstfileexw
- ReadDirectoryChangesW：
  - https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-readdirectorychangesw
- IDesktopWallpaper：
  - https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-idesktopwallpaper
- IFileOperation：
  - https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation
- DXGI GPU preference：
  - https://learn.microsoft.com/windows/win32/api/dxgi1_6/nf-dxgi1_6-idxgifactory6-enumadapterbygpupreference
- SQLite FTS5：
  - https://www.sqlite.org/fts5.html
- libjpeg-turbo：
  - https://libjpeg-turbo.org/
- ETW：
  - https://learn.microsoft.com/windows/win32/etw/event-tracing-portal
- Windows Performance Analyzer：
  - https://learn.microsoft.com/windows-hardware/test/wpt/windows-performance-analyzer

---

## 25. 最终建议

VeloPic 的第一版应该极度聚焦：

```text
递归扫描
高速缩略图
GPU 加速查看
文件名搜索
收藏
一键壁纸
多显示器壁纸
Windows 11 美观界面
```

不要一开始做复杂编辑、AI、云同步、RAW 全格式。只要 VeloPic 能在 10 万张图片目录里做到：

- 打开快。
- 滚动快。
- 切图快。
- 搜索快。
- 设壁纸快。

它就已经具备非常清晰的差异化竞争力。
