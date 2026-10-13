# Android USB ADB Bridge

This is a standalone Android library for a Unity-installed tablet acting as a USB Host and
connecting to a PICO device's `adbd` through the Android USB ADB interface
(`0xFF/0x42/0x01`). It does not invoke `Runtime.exec`, `adb.exe`, or any local `cmd wifi`
command.

## Build

Requirements: JDK 11, Gradle 7.5 or later, Android SDK Platform 30. The project uses Android
Gradle Plugin 7.4.2 and compiles/minimally targets API 30.

```powershell
gradle :library:copyReleaseAarToUnity
```

The task builds the release AAR and copies it to
`Assets/SoraTools/Plugins/Android/AndroidUsbAdbBridge.aar`, which is the artifact consumed by
the Unity controller. Gradle intermediates are written under Unity's `Library` cache instead of
the imported `Assets` tree. Re-run the task after changing native-library source files.

## Unity API

Create `com.soratools.usbadb.AndroidUsbAdbBridge` with:

```text
AndroidUsbAdbBridge(android.content.Context context, BridgeCallback callback)
```

`BridgeCallback` has one method:

```text
void onBridgeEvent(String eventJson)
```

Public bridge methods:

```text
String scanDevices()
void requestUsbPermission(String deviceName, String operationId)
void connect(String deviceName, String operationId)
void executeShell(String command, int timeoutMs, String operationId)
void cancel(String operationId)
void disconnect()
void dispose()
String encryptSecret(String plaintext)
String decryptSecret(String encrypted)
```

All methods except `scanDevices`, `encryptSecret`, and `decryptSecret` are asynchronous. The
callback is posted on Android's main Looper. `encryptSecret` and `decryptSecret` return `null`
when Android Keystore cannot perform the requested operation.

## Callback JSON

Events include `operationId` when one was supplied. Event types are:

```text
device_attached, device_detached, usb_permission, connected, disconnected,
shell_complete, operation_failed
```

`operation_failed` carries only a stable `errorCode`; exception messages, commands, passwords,
and key material are never logged or put into events. `shell_complete` contains:

```text
output, stderr, outputBytes, exitCode, succeeded, transportSucceeded, shellV2
```

When the PICO advertises `shell_v2` in its CNXN features, the bridge opens
`shell,v2,raw:<command>` and decodes stdout, stderr, and the remote exit code. In that mode,
`succeeded` is true only when the remote exit code is zero. For older devices, the bridge opens
`shell:<command>`, returns mixed output in `output`, sets `exitCode` to `-1`, and uses
`succeeded=true` only to mean the ADB stream itself completed. Consumers must inspect legacy
output when remote command success matters.

## Protocol and Lifetime

The library performs USB bulk exact reads/writes, including partial transfers, with deadlines.
It sends ADB CNXN/AUTH packets, signs the daemon token using a non-exportable 2048-bit Android
Keystore RSA key, and sends the corresponding ADB-compatible public key when device approval is
needed. It claims and releases the selected interface, handles attach/detach broadcasts, and
closes an active session on I/O failure, timeout, disconnect, or disposal.

The implementation is intentionally self-contained rather than importing `cgutman/AdbLib` or
`wuxudong/flashbot UsbChannel`: those projects do not provide this Unity-specific Android USB
Host transport and Android Keystore ownership boundary as a drop-in API. The complete raw ADB
handshake and stream state machine live in this module, with no third-party runtime dependency.
