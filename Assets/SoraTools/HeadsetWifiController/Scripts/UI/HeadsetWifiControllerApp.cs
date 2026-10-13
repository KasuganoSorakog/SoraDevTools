using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace SoraTools.HeadsetWifi
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class HeadsetWifiControllerApp : MonoBehaviour
    {
        private readonly List<UsbAdbDeviceInfo> devices = new List<UsbAdbDeviceInfo>();
        private readonly List<WifiProfile> profiles = new List<WifiProfile>();

        private UIDocument uiDocument;
        private AndroidUsbAdbSession session;
        private ISecretStore profileStore;
        private IWifiMigrationService migrationService;
        private CancellationTokenSource migrationCancellation;

        private DropdownField deviceDropdown;
        private DropdownField profileDropdown;
        private Toggle deviceConfirmedToggle;
        private Label connectionLabel;
        private Label deviceDetailLabel;
        private Label capabilityLabel;
        private TextField ssidField;
        private DropdownField securityDropdown;
        private TextField passwordField;
        private Toggle persistPasswordToggle;
        private TextField businessAddressField;
        private TextField wifiInterfaceField;
        private Label progressLabel;
        private Label resultLabel;
        private Button connectButton;
        private Button migrateButton;
        private Button cancelButton;

        private void Awake()
        {
            uiDocument = GetComponent<UIDocument>();
            BuildInterface();
        }

        private async void Start()
        {
            try
            {
                session = await AndroidUsbAdbSession.CreateAsync();
                session.DeviceEventReceived += OnUsbDeviceEvent;
                var ciphertextStore = new FileWifiProfileStore(
                    FileWifiProfileStore.GetDefaultFilePath(Application.persistentDataPath));
                profileStore = new KeystoreBackedWifiProfileStore(ciphertextStore, session);
                migrationService = new HeadsetWifiMigrationService(
                    session,
                    new WifiCapabilityProbe(),
                    profileStore,
                    new FileOperationStore(FileOperationStore.GetDefaultFilePath(Application.persistentDataPath)));
                SetStatus("等待 USB 设备连接", StatusKind.Neutral);
                await RefreshDevicesAsync();
                await RefreshProfilesAsync();
            }
            catch (Exception exception)
            {
                SetResult("初始化失败：" + ToSafeMessage(exception), true);
            }
        }

        private void OnDestroy()
        {
            if (session != null)
                session.DeviceEventReceived -= OnUsbDeviceEvent;
            migrationCancellation?.Cancel();
            migrationCancellation?.Dispose();
            session?.Dispose();
        }

        private void BuildInterface()
        {
            VisualElement root = uiDocument.rootVisualElement;
            root.Clear();
            root.AddToClassList("app-root");
            StyleSheet stylesheet = Resources.Load<StyleSheet>("HeadsetWifiController");
            if (stylesheet != null)
                root.styleSheets.Add(stylesheet);

            root.Add(CreateHeader());
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            root.Add(scroll);

            var grid = new VisualElement();
            grid.AddToClassList("content-grid");
            scroll.Add(grid);
            grid.Add(CreateDeviceSection());
            grid.Add(CreateWifiSection());
            grid.Add(CreateVerificationSection());
        }

        private VisualElement CreateHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("app-header");
            var text = new VisualElement();
            text.Add(new Label("头显 WiFi 控制器") { name = "app-title" });
            text.Q<Label>("app-title").AddToClassList("app-title");
            var subtitle = new Label("USB 连接期间完成授权、配置与验证");
            subtitle.AddToClassList("app-subtitle");
            text.Add(subtitle);
            header.Add(text);
            return header;
        }

        private VisualElement CreateDeviceSection()
        {
            var section = CreateSection("PICO 连接");
            deviceDropdown = new DropdownField("检测到的设备", new List<string> { "未检测到 ADB USB 设备" }, 0);
            deviceDropdown.AddToClassList("form-row");
            deviceDropdown.RegisterValueChangedCallback(_ => UpdateDeviceControls());
            section.Add(deviceDropdown);

            connectionLabel = CreateValueLabel("连接状态", "未连接");
            deviceDetailLabel = CreateValueLabel("设备信息", "等待连接");
            capabilityLabel = CreateValueLabel("WiFi 能力", "等待探测");
            section.Add(connectionLabel.parent);
            section.Add(deviceDetailLabel.parent);
            section.Add(capabilityLabel.parent);

            deviceConfirmedToggle = new Toggle("已确认当前头显") { value = false };
            deviceConfirmedToggle.AddToClassList("form-row");
            deviceConfirmedToggle.RegisterValueChangedCallback(_ => UpdateDeviceControls());
            section.Add(deviceConfirmedToggle);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("toolbar-row");
            var scanButton = new Button(async () => await RefreshDevicesAsync()) { text = "扫描设备" };
            scanButton.AddToClassList("secondary-command");
            connectButton = new Button(async () => await ConnectSelectedDeviceAsync()) { text = "授权并连接" };
            connectButton.AddToClassList("primary-command");
            toolbar.Add(scanButton);
            toolbar.Add(connectButton);
            section.Add(toolbar);
            return section;
        }

        private VisualElement CreateWifiSection()
        {
            var section = CreateSection("目标 WiFi");
            profileDropdown = new DropdownField("已保存配置", new List<string> { "新建配置" }, 0);
            profileDropdown.AddToClassList("form-row");
            profileDropdown.RegisterValueChangedCallback(async _ => await ApplySelectedProfileAsync());
            section.Add(profileDropdown);

            ssidField = new TextField("SSID");
            ssidField.AddToClassList("form-row");
            ssidField.RegisterValueChangedCallback(_ => ClearResult());
            section.Add(ssidField);

            securityDropdown = new DropdownField("安全类型", new List<string> { "WPA2-PSK", "WPA3-SAE" }, 0);
            securityDropdown.AddToClassList("form-row");
            section.Add(securityDropdown);

            passwordField = new TextField("密码") { isPasswordField = true };
            passwordField.AddToClassList("form-row");
            passwordField.RegisterValueChangedCallback(_ => ClearResult());
            section.Add(passwordField);

            persistPasswordToggle = new Toggle("保存密码") { value = true };
            persistPasswordToggle.AddToClassList("form-row");
            section.Add(persistPasswordToggle);

            businessAddressField = new TextField("业务地址（可选）");
            businessAddressField.AddToClassList("form-row");
            section.Add(businessAddressField);

            wifiInterfaceField = new TextField("WiFi 接口") { value = "wlan0" };
            wifiInterfaceField.AddToClassList("form-row");
            section.Add(wifiInterfaceField);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("toolbar-row");
            migrateButton = new Button(async () => await StartMigrationAsync()) { text = "设置 WiFi" };
            migrateButton.AddToClassList("primary-command");
            cancelButton = new Button(CancelMigration) { text = "取消" };
            cancelButton.AddToClassList("cancel-command");
            cancelButton.SetEnabled(false);
            toolbar.Add(migrateButton);
            toolbar.Add(cancelButton);
            section.Add(toolbar);
            return section;
        }

        private VisualElement CreateVerificationSection()
        {
            var section = CreateSection("迁移结果");
            progressLabel = new Label("尚未开始") { name = "migration-progress" };
            progressLabel.AddToClassList("progress-label");
            section.Add(progressLabel);
            resultLabel = new Label { name = "migration-result" };
            resultLabel.AddToClassList("hidden");
            section.Add(resultLabel);
            return section;
        }

        private static VisualElement CreateSection(string heading)
        {
            var section = new VisualElement();
            section.AddToClassList("panel-section");
            var label = new Label(heading);
            label.AddToClassList("section-heading");
            section.Add(label);
            return section;
        }

        private static Label CreateValueLabel(string label, string initialValue)
        {
            var row = new VisualElement();
            row.AddToClassList("status-row");
            var title = new Label(label);
            title.AddToClassList("field-label");
            var value = new Label(initialValue);
            value.AddToClassList("field-value");
            row.Add(title);
            row.Add(value);
            value.parent.userData = row;
            return value;
        }

        private async Task RefreshDevicesAsync()
        {
            if (session == null)
                return;

            try
            {
                IReadOnlyList<UsbAdbDeviceInfo> found = await session.ScanDevicesAsync(CancellationToken.None);
                devices.Clear();
                for (int index = 0; index < found.Count; index++)
                    devices.Add(found[index]);
                UpdateDeviceDropdown();
                SetStatus(devices.Count == 0 ? "未检测到 ADB USB 设备" : "已检测到 USB ADB 设备", devices.Count == 0 ? StatusKind.Warning : StatusKind.Good);
            }
            catch (Exception exception)
            {
                SetResult("扫描设备失败：" + ToSafeMessage(exception), true);
            }
        }

        private async Task ConnectSelectedDeviceAsync()
        {
            UsbAdbDeviceInfo device = GetSelectedDevice();
            if (session == null || device == null)
            {
                SetResult("请先连接并扫描 PICO 设备", true);
                return;
            }

            try
            {
                SetStatus("正在请求 USB 访问权限", StatusKind.Neutral);
                if (!device.HasPermission)
                {
                    UsbAdbOperationResult permission = await session.RequestUsbPermissionAsync(device.DeviceName, CancellationToken.None);
                    if (!permission.Succeeded)
                    {
                        SetResult("USB 授权失败：" + ToDisplayCode(permission.Code), true);
                        return;
                    }
                    device.HasPermission = true;
                }

                SetStatus("正在等待头显 USB 调试授权", StatusKind.Neutral);
                UsbAdbOperationResult connection = await session.ConnectAsync(device.DeviceName, CancellationToken.None);
                if (!connection.Succeeded)
                {
                    SetResult("连接失败：" + ToDisplayCode(connection.Code), true);
                    return;
                }

                deviceConfirmedToggle.SetValueWithoutNotify(false);
                await ProbeCapabilitiesAsync();
                SetStatus("已连接，确认设备后可设置 WiFi", StatusKind.Good);
            }
            catch (Exception exception)
            {
                SetResult("连接失败：" + ToSafeMessage(exception), true);
            }
            finally
            {
                UpdateDeviceControls();
            }
        }

        private async Task ProbeCapabilitiesAsync()
        {
            if (migrationService == null)
                return;

            WifiCapabilities capabilities = await migrationService.ProbeCapabilitiesAsync(CancellationToken.None);
            AndroidDeviceProperties device = capabilities.Device != null ? capabilities.Device.Properties : null;
            string model = device != null && !string.IsNullOrEmpty(device.Model) ? device.Model : "未知型号";
            string api = device != null && device.AndroidApiLevel > 0 ? "API " + device.AndroidApiLevel : "API 未知";
            string serial = device != null ? device.SerialNumber : "";
            string serialText = string.IsNullOrEmpty(serial) ? "序列号未读取" : "序列号 " + serial;
            deviceDetailLabel.text = model + " / " + api + " / " + serialText;
            capabilityLabel.text = capabilities.CanMigrate
                ? "支持 cmd wifi connect-network"
                : "不支持：" + (capabilities.ProbeFailureCode ?? "能力未知");
        }

        private async Task RefreshProfilesAsync()
        {
            if (profileStore == null)
                return;

            try
            {
                IReadOnlyList<WifiProfile> saved = await profileStore.ListProfilesAsync(CancellationToken.None);
                profiles.Clear();
                for (int index = 0; index < saved.Count; index++)
                    profiles.Add(saved[index]);
                var choices = new List<string> { "新建配置" };
                for (int index = 0; index < profiles.Count; index++)
                    choices.Add(profiles[index].DisplayName);
                profileDropdown.choices = choices;
                profileDropdown.SetValueWithoutNotify(choices[0]);
            }
            catch (Exception exception)
            {
                SetResult("读取已保存配置失败：" + ToSafeMessage(exception), true);
            }
        }

        private async Task ApplySelectedProfileAsync()
        {
            if (profileStore == null || profileDropdown.index <= 0)
                return;

            int profileIndex = profileDropdown.index - 1;
            if (profileIndex < 0 || profileIndex >= profiles.Count)
                return;

            try
            {
                WifiCredential credential = await profileStore.GetCredentialAsync(
                    profiles[profileIndex].ProfileId,
                    CancellationToken.None);
                if (credential == null)
                    return;
                ssidField.SetValueWithoutNotify(credential.Ssid);
                securityDropdown.SetValueWithoutNotify(ToSecurityDisplay(credential.SecurityMode));
                passwordField.SetValueWithoutNotify(credential.Password);
            }
            catch (Exception exception)
            {
                SetResult("读取密码失败：" + ToSafeMessage(exception), true);
            }
        }

        private async Task StartMigrationAsync()
        {
            if (migrationService == null || session == null || string.IsNullOrEmpty(session.DeviceSessionId))
            {
                SetResult("请先完成 USB ADB 连接", true);
                return;
            }
            if (!deviceConfirmedToggle.value)
            {
                SetResult("请先确认当前连接的是目标头显", true);
                return;
            }

            var credential = new WifiCredential
            {
                Ssid = ssidField.value ?? "",
                SecurityMode = ParseSecurityMode(securityDropdown.value),
                Password = passwordField.value ?? ""
            };
            if (!WifiCredentialValidator.TryValidate(credential, out string failureCode))
            {
                SetResult("WiFi 配置无效：" + failureCode, true);
                return;
            }

            migrationCancellation?.Cancel();
            migrationCancellation?.Dispose();
            var activeCancellation = new CancellationTokenSource();
            migrationCancellation = activeCancellation;
            migrateButton.SetEnabled(false);
            cancelButton.SetEnabled(true);
            ClearResult();
            progressLabel.text = "正在预检";

            try
            {
                WifiMigrationResult result = await migrationService.MigrateAsync(
                    new WifiMigrationRequest
                    {
                        Credential = credential,
                        PersistCredential = persistPasswordToggle.value,
                        ProfileDisplayName = ssidField.value,
                        BusinessAddress = businessAddressField.value ?? "",
                        WifiInterfaceName = wifiInterfaceField.value ?? "",
                        ProgressChanged = UpdateMigrationProgress
                    },
                    activeCancellation.Token);
                ShowMigrationResult(result);
                if (result.CredentialPersisted)
                    await RefreshProfilesAsync();
            }
            catch (Exception exception)
            {
                SetResult("切换任务异常：" + ToSafeMessage(exception), true);
            }
            finally
            {
                cancelButton.SetEnabled(false);
                if (migrationCancellation == activeCancellation)
                {
                    migrationCancellation = null;
                    activeCancellation.Dispose();
                }
                UpdateDeviceControls();
            }
        }

        private void CancelMigration()
        {
            migrationCancellation?.Cancel();
            progressLabel.text = "正在中断当前 ADB 操作，随后停止验证";
        }

        private void UpdateMigrationProgress(WifiMigrationProgress progress)
        {
            HeadsetWifiMainThread.Enqueue(() =>
            {
                progressLabel.text = ToDisplayState(progress.State);
            });
        }

        private void ShowMigrationResult(WifiMigrationResult result)
        {
            if (result != null && result.Succeeded)
            {
                SetResult("验证完成，可拔线", false);
                SetStatus("WiFi 已连接并完成稳定观察", StatusKind.Good);
                return;
            }

            if (result != null && result.FailureKind == WifiMigrationFailureKind.Cancelled)
            {
                SetResult("任务已取消；已提交的 WiFi 命令可能仍已生效，请保持 USB 数据线并重新连接后确认状态", true);
                SetStatus("已中断 ADB 会话，需要重新连接头显", StatusKind.Warning);
                return;
            }

            string code = result == null ? "Unexpected" : result.MessageCode;
            SetResult("未完成切换：" + code, true);
            SetStatus("保留 USB 连接以便恢复", StatusKind.Warning);
        }

        private void OnUsbDeviceEvent(UsbAdbDeviceEvent deviceEvent)
        {
            if (deviceEvent.EventName == "device_attached")
                _ = RefreshDevicesAsync();
            if (deviceEvent.EventName == "device_detached" || deviceEvent.EventName == "disconnected")
            {
                deviceConfirmedToggle.SetValueWithoutNotify(false);
                SetStatus("USB 连接已断开", StatusKind.Warning);
                UpdateDeviceControls();
            }
        }

        private void UpdateDeviceDropdown()
        {
            var choices = new List<string>();
            for (int index = 0; index < devices.Count; index++)
                choices.Add(GetDeviceDisplayName(devices[index]));
            if (choices.Count == 0)
                choices.Add("未检测到 ADB USB 设备");
            deviceDropdown.choices = choices;
            deviceDropdown.SetValueWithoutNotify(choices[0]);
            UpdateDeviceControls();
        }

        private void UpdateDeviceControls()
        {
            bool hasDevice = GetSelectedDevice() != null;
            connectButton?.SetEnabled(hasDevice);
            bool readyToMigrate = session != null
                && !string.IsNullOrEmpty(session.DeviceSessionId)
                && deviceConfirmedToggle != null
                && deviceConfirmedToggle.value;
            migrateButton?.SetEnabled(readyToMigrate && migrationCancellation == null);
        }

        private UsbAdbDeviceInfo GetSelectedDevice()
        {
            if (deviceDropdown == null || devices.Count == 0)
                return null;
            for (int index = 0; index < devices.Count; index++)
            {
                if (GetDeviceDisplayName(devices[index]) == deviceDropdown.value)
                    return devices[index];
            }
            return null;
        }

        private void SetStatus(string message, StatusKind kind)
        {
            connectionLabel.text = message ?? "";
            connectionLabel.RemoveFromClassList("status-good");
            connectionLabel.RemoveFromClassList("status-warn");
            connectionLabel.RemoveFromClassList("status-error");
            connectionLabel.RemoveFromClassList("status-neutral");
            connectionLabel.AddToClassList(ToStatusClass(kind));
        }

        private void SetResult(string message, bool isError)
        {
            resultLabel.text = message ?? "";
            resultLabel.RemoveFromClassList("hidden");
            if (isError)
                resultLabel.AddToClassList("error-message");
            else
                resultLabel.RemoveFromClassList("error-message");
        }

        private void ClearResult()
        {
            resultLabel.text = "";
            resultLabel.AddToClassList("hidden");
        }

        private static string GetDeviceDisplayName(UsbAdbDeviceInfo device)
        {
            return device.DeviceName + " (" + device.VendorId.ToString("X4") + ":" + device.ProductId.ToString("X4") + ")";
        }

        private static string ToSecurityDisplay(WifiSecurityMode mode)
        {
            return mode == WifiSecurityMode.Wpa3Sae ? "WPA3-SAE" : "WPA2-PSK";
        }

        private static WifiSecurityMode ParseSecurityMode(string displayValue)
        {
            return string.Equals(displayValue, "WPA3-SAE", StringComparison.Ordinal)
                ? WifiSecurityMode.Wpa3Sae
                : WifiSecurityMode.Wpa2Psk;
        }

        private static string ToDisplayState(WifiMigrationState state)
        {
            switch (state)
            {
                case WifiMigrationState.Prechecking: return "正在执行设备与 WiFi 预检";
                case WifiMigrationState.Applying: return "正在提交 WiFi 配置";
                case WifiMigrationState.Verifying: return "正在验证 SSID、IP 与默认路由";
                case WifiMigrationState.ObservingStability: return "正在进行 60 秒稳定观察";
                case WifiMigrationState.Persisting: return "正在保存 WiFi 配置";
                case WifiMigrationState.Succeeded: return "验证完成";
                case WifiMigrationState.Cancelled: return "已停止后续验证";
                case WifiMigrationState.Failed: return "任务未完成";
                default: return "正在处理";
            }
        }

        private static string ToDisplayCode(string code)
        {
            if (string.Equals(code, "AUTHORIZATION_TIMEOUT", StringComparison.OrdinalIgnoreCase))
                return "请在头显中确认 USB 调试授权";
            if (string.Equals(code, "USB_PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
                return "平板未获得 USB 访问权限";
            return string.IsNullOrEmpty(code) ? "未知错误" : code;
        }

        private static string ToSafeMessage(Exception exception)
        {
            return exception == null ? "未知错误" : exception.GetType().Name;
        }

        private static string ToStatusClass(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.Good: return "status-good";
                case StatusKind.Warning: return "status-warn";
                case StatusKind.Error: return "status-error";
                default: return "status-neutral";
            }
        }

        private enum StatusKind
        {
            Neutral,
            Good,
            Warning,
            Error
        }
    }
}
