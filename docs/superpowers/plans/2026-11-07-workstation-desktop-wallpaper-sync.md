# Workstation Desktop Wallpaper Synchronization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centralize workstation Windows Desktop Wallpaper management in Server Admin App and automatically synchronize it in realtime to all running and newly connecting client workstations.

**Architecture:** Extend backend `pricing` client settings with desktop wallpaper properties (`enabled`, `url`, `style`) and a file upload endpoint. In Client Agent, implement a robust cached wallpaper downloader with Windows Registry and Win32 `SystemParametersInfo` integration triggered on startup, session state changes, and realtime socket events. In Server Admin App, add a modern UI card with image picker, live preview, style selector, and instant apply button in the "Cài đặt hình nền" tab.

**Tech Stack:** NestJS, Prisma, Socket.io, TypeScript, WPF (.NET 10), Win32 API (`user32.dll`, `SystemParametersInfo`), Windows Registry (`Microsoft.Win32`).

---

### Task 1: Backend Data Model & Settings API Extension

**Files:**
- Modify: `backend/src/pricing/dto/set-client-runtime-settings.dto.ts`
- Modify: `backend/src/pricing/pricing.service.ts`
- Modify: `backend/src/pricing/pricing.controller.ts`

- [ ] **Step 1: Update DTO with desktop wallpaper fields**

In `backend/src/pricing/dto/set-client-runtime-settings.dto.ts`, add:
```typescript
  @IsOptional()
  @IsBoolean()
  desktopWallpaperEnabled?: boolean;

  @IsOptional()
  @IsString()
  desktopWallpaperUrl?: string;

  @IsOptional()
  @IsIn(['fill', 'fit', 'stretch'])
  desktopWallpaperStyle?: 'fill' | 'fit' | 'stretch';
```

- [ ] **Step 2: Add setting constants and read/write logic in `pricing.service.ts`**

Define setting keys:
```typescript
const CLIENT_DESKTOP_WALLPAPER_ENABLED_KEY = '__CLIENT_DESKTOP_WALLPAPER_ENABLED__';
const CLIENT_DESKTOP_WALLPAPER_URL_KEY = '__CLIENT_DESKTOP_WALLPAPER_URL__';
const CLIENT_DESKTOP_WALLPAPER_STYLE_KEY = '__CLIENT_DESKTOP_WALLPAPER_STYLE__';
const DEFAULT_DESKTOP_WALLPAPER_STYLE = 'fill';
```

In `getClientRuntimeSettings()`:
Fetch `desktopWallpaperEnabledSetting`, `desktopWallpaperUrlSetting`, `desktopWallpaperStyleSetting` from `prisma.appSetting` and map them into the returned response payload.

In `setClientRuntimeSettings(payload)`:
Handle persisting each if defined, then emit `pricing.client_settings.changed` via `this.realtime.emitToAll`.

- [ ] **Step 3: Verify backend compilation**

Run: `npm run build` inside `backend/`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/pricing/
git commit -m "feat(backend): add desktop wallpaper fields to client runtime settings"
```

---

### Task 2: Backend Desktop Wallpaper File Upload & Serving

**Files:**
- Modify: `backend/src/pricing/pricing.service.ts`
- Modify: `backend/src/pricing/pricing.controller.ts`
- Modify: `backend/src/auth/api-key.guard.ts` (if path needs white-listing for static media download)

- [ ] **Step 1: Create upload handler in `pricing.service.ts`**

```typescript
const DESKTOP_WALLPAPER_DIR = path.join(process.cwd(), 'storage', 'desktop-wallpapers');
const MAX_WALLPAPER_SIZE_BYTES = 25 * 1024 * 1024; // 25MB

async uploadDesktopWallpaper(file: Express.Multer.File, apiBaseUrl: string) {
  if (!file?.buffer?.length) throw new BadRequestException('File upload rỗng');
  if (file.size > MAX_WALLPAPER_SIZE_BYTES) {
    throw new BadRequestException('Kích thước ảnh vượt quá 25MB');
  }
  const ext = path.extname(file.originalname).toLowerCase() || '.jpg';
  const fileName = `wallpaper_${Date.now()}_${Math.random().toString(36).substring(2, 8)}${ext}`;
  await fs.promises.mkdir(DESKTOP_WALLPAPER_DIR, { recursive: true });
  await fs.promises.writeFile(path.join(DESKTOP_WALLPAPER_DIR, fileName), file.buffer);
  
  const normalizedBase = apiBaseUrl.replace(/\/+$/, '');
  return `${normalizedBase}/api/v1/pricing/client-settings/desktop-wallpaper-media/${fileName}`;
}
```

- [ ] **Step 2: Add controller routes in `pricing.controller.ts`**

Add endpoint to upload:
```typescript
@Post('client-settings/desktop-wallpaper')
@UseInterceptors(FileInterceptor('file', { limits: { fileSize: 25 * 1024 * 1024 } }))
async uploadDesktopWallpaper(
  @UploadedFile() file: Express.Multer.File,
  @Req() req: Request,
) {
  const baseUrl = this.resolveApiBaseUrl(req);
  const fileUrl = await this.pricingService.uploadDesktopWallpaper(file, baseUrl);
  return { success: true, url: fileUrl };
}
```

Add endpoint to serve media:
```typescript
@Get('client-settings/desktop-wallpaper-media/:fileName')
async getDesktopWallpaperMedia(
  @Param('fileName') fileName: string,
  @Res() res: Response,
) {
  return this.pricingService.writeDesktopWallpaperToResponse(fileName, res);
}
```

- [ ] **Step 3: Whitelist media path in `api-key.guard.ts`**

Ensure `client-settings/desktop-wallpaper-media/` is accessible publicly without requiring an API key so Windows clients can download wallpaper images cleanly.

- [ ] **Step 4: Test upload endpoint with curl/PowerShell**

Run a POST request with sample image to `/api/v1/pricing/client-settings/desktop-wallpaper` and verify that file is saved and URL is returned.

- [ ] **Step 5: Commit**

```bash
git add backend/src/pricing/ backend/src/auth/
git commit -m "feat(backend): implement desktop wallpaper upload and media serving"
```

---

### Task 3: Client Agent Wallpaper Service & Win32 Integration

**Files:**
- Modify: `client/src/Client.Agent.Wpf/App.ClientModels.cs`
- Modify: `client/src/Client.Agent.Wpf/App.xaml.cs`
- Modify: `client/src/Client.Agent.Wpf/MainWindow.xaml.cs`

- [ ] **Step 1: Add DTO properties in `App.ClientModels.cs`**

In `ClientRuntimeSettingsResponse`:
```csharp
public bool DesktopWallpaperEnabled { get; set; } = false;
public string DesktopWallpaperUrl { get; set; } = string.Empty;
public string DesktopWallpaperStyle { get; set; } = "fill";
```

- [ ] **Step 2: Implement `DesktopWallpaperHelper` in Client Agent**

Implement method in `App.xaml.cs` (or helper class):
```csharp
public static async Task ApplyDesktopWallpaperAsync(string url, string style)
{
    // 1. Download to %LocalAppData%\ServerManagerBilling\Cache\Wallpapers\<hash>.jpg
    // 2. Set Registry HKEY_CURRENT_USER\Control Panel\Desktop:
    //    WallpaperStyle = style == "fit" ? "6" : style == "stretch" ? "2" : "10";
    //    TileWallpaper = "0";
    // 3. SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, localPath, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
}
```

- [ ] **Step 3: Wire into `RefreshClientRuntimeSettingsAsync()`**

In `App.xaml.cs`:
When `payload.DesktopWallpaperEnabled == true` and `!string.IsNullOrWhiteSpace(payload.DesktopWallpaperUrl)`:
Call `_ = ApplyDesktopWallpaperAsync(payload.DesktopWallpaperUrl, payload.DesktopWallpaperStyle);`.

- [ ] **Step 4: Verify build of Client Agent**

Run: `dotnet build client/src/Client.Agent.Wpf/Client.Agent.Wpf.csproj`
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add client/src/Client.Agent.Wpf/
git commit -m "feat(client): implement cached desktop wallpaper downloader and Win32 application"
```

---

### Task 4: Server Admin App UI & Settings Integration

**Files:**
- Modify: `server-admin-app/Models/AdminShellModels.cs`
- Modify: `server-admin-app/MainWindow.xaml`
- Modify: `server-admin-app/MainWindow/MainWindow.Settings.cs`

- [ ] **Step 1: Update `ClientRuntimeSettingsResponse` in `AdminShellModels.cs`**

Add:
```csharp
public bool DesktopWallpaperEnabled { get; set; }
public string? DesktopWallpaperUrl { get; set; }
public string? DesktopWallpaperStyle { get; set; }
```

- [ ] **Step 2: Add modern UI card to `MainWindow.xaml` in "Cài đặt hình nền" tab**

Add a styled card below Lock Screen media cards:
- Header: `🖼️ Hình nền Desktop Windows máy trạm (Desktop Wallpaper)`
- CheckBox: `x:Name="DesktopWallpaperEnabledCheckBox" Content="Bật đồng bộ hình nền Desktop cho máy trạm"`
- Image Picker: `ChooseDesktopWallpaperButton` (`📂 Chọn ảnh từ máy tính...`)
- Image Preview Box: `Border CornerRadius="8" Width="320" Height="180"` with `x:Name="DesktopWallpaperPreviewImage"`
- URL Text: `x:Name="DesktopWallpaperUrlTextBox"`
- ComboBox: `x:Name="DesktopWallpaperStyleComboBox"` (Fill, Fit, Stretch)
- Action Button: `x:Name="ApplyDesktopWallpaperNowButton" Content="⚡ Áp dụng ngay cho máy trạm online"`
- Status Text: `x:Name="DesktopWallpaperStatusTextBlock"`

- [ ] **Step 3: Implement loading and saving in `MainWindow.Settings.cs`**

- In `LoadClientRuntimeSettingsAsync()`:
  Read `response.DesktopWallpaperEnabled`, `DesktopWallpaperUrl`, `DesktopWallpaperStyle`, populate UI controls and load preview image.
- In `SaveClientRuntimeSettingsAsync()`:
  Include `desktopWallpaperEnabled`, `desktopWallpaperUrl`, `desktopWallpaperStyle` in JSON payload to `PATCH /pricing/client-settings`.
- Implement `ChooseDesktopWallpaperButton_Click`:
  Open `OpenFileDialog`, upload image via `POST /pricing/client-settings/desktop-wallpaper`, set URL textbox and update preview.
- Implement `ApplyDesktopWallpaperNowButton_Click`:
  Saves settings and broadcasts realtime update with visual confirmation.

- [ ] **Step 4: Verify build of Server Admin App**

Run: `dotnet build server-admin-app/Server.Admin.App.csproj`
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add server-admin-app/
git commit -m "feat(admin): add desktop wallpaper configuration card with preview and instant apply"
```

---

### Task 5: End-to-End Realtime Verification & Packaging

**Files:**
- Deploy to `releasePackage/Server/Server-Admin-App`
- Deploy to `releasePackage/Client/Client-Agent`

- [ ] **Step 1: Rebuild and sync binaries**

Build both `server-admin-app` and `client/src/Client.Agent.Wpf`, then copy compiled output into `releasePackage/Server/Server-Admin-App` and `releasePackage/Client/Client-Agent`.

- [ ] **Step 2: Launch and verify Server Admin App**

Launch `Server.Admin.App.exe` in interactive user session, open **Cài đặt > Cài đặt hình nền**, verify new Desktop Wallpaper card appears and controls function properly.

- [ ] **Step 3: Final verification and commit**

Verify backend responds cleanly to settings PATCH and GET.
Commit any final configuration adjustments.
