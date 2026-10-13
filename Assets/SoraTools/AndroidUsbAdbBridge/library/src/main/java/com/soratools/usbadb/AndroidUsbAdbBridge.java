package com.soratools.usbadb;

import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.hardware.usb.UsbDevice;
import android.hardware.usb.UsbManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.IOException;
import java.net.SocketTimeoutException;
import java.security.GeneralSecurityException;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Unity-facing Android USB Host ADB bridge. Public operations are asynchronous except scanDevices.
 * The bridge never logs or persists shell commands, shell output, or secrets.
 */
public final class AndroidUsbAdbBridge {
    private static final String ACTION_USB_PERMISSION = "com.soratools.usbadb.USB_PERMISSION";
    private static final int MIN_SHELL_TIMEOUT_MS = 1;
    private static final int MAX_SHELL_TIMEOUT_MS = 120_000;

    private final Context applicationContext;
    private final UsbManager usbManager;
    private final BridgeCallback callback;
    private final Handler callbackHandler;
    private final ExecutorService serialExecutor;
    private final Map<String, String> permissionOperations = new ConcurrentHashMap<>();
    private final Map<String, Boolean> cancelledOperations = new ConcurrentHashMap<>();
    private final AtomicBoolean disposed = new AtomicBoolean(false);
    private final AdbKeyStore keyStore = new AdbKeyStore();
    private final BroadcastReceiver usbReceiver = new UsbReceiver();

    private volatile AdbSession session;
    private volatile AdbUsbTransport connectingTransport;
    private volatile String activeOperationId;
    private boolean receiverRegistered;

    public AndroidUsbAdbBridge(Context context, BridgeCallback callback) {
        if (context == null || callback == null) {
            throw new IllegalArgumentException("Context and callback are required.");
        }

        Context appContext = context.getApplicationContext();
        applicationContext = appContext == null ? context : appContext;
        this.callback = callback;
        callbackHandler = new Handler(Looper.getMainLooper());
        Object service = applicationContext.getSystemService(Context.USB_SERVICE);
        if (!(service instanceof UsbManager)) {
            throw new IllegalStateException("USB Host is unavailable.");
        }
        usbManager = (UsbManager) service;
        serialExecutor = Executors.newSingleThreadExecutor(runnable -> {
            Thread thread = new Thread(runnable, "SoraUsbAdbBridge");
            thread.setDaemon(true);
            return thread;
        });

        IntentFilter filter = new IntentFilter();
        filter.addAction(ACTION_USB_PERMISSION);
        filter.addAction(UsbManager.ACTION_USB_DEVICE_ATTACHED);
        filter.addAction(UsbManager.ACTION_USB_DEVICE_DETACHED);
        applicationContext.registerReceiver(usbReceiver, filter);
        receiverRegistered = true;
    }

    /** Returns the currently attached USB devices that expose an ADB bulk interface. */
    public String scanDevices() {
        JSONArray devices = new JSONArray();
        for (UsbDevice device : usbManager.getDeviceList().values()) {
            AdbUsbTransport.Candidate candidate = AdbUsbTransport.findCandidate(device);
            if (candidate == null) {
                continue;
            }
            try {
                devices.put(toDeviceJson(device, candidate));
            } catch (JSONException ignored) {
                return "{\"devices\":[],\"errorCode\":\"JSON_ERROR\"}";
            }
        }

        try {
            JSONObject result = new JSONObject();
            result.put("devices", devices);
            return result.toString();
        } catch (JSONException ignored) {
            return "{\"devices\":[],\"errorCode\":\"JSON_ERROR\"}";
        }
    }

    /** Requests Android's per-device USB permission and reports the result through BridgeCallback. */
    public void requestUsbPermission(String deviceName, String operationId) {
        submit(operationId, () -> requestUsbPermissionInternal(deviceName, operationId));
    }

    /** Opens the selected ADB USB device and completes CNXN/AUTH before reporting connected. */
    public void connect(String deviceName, String operationId) {
        submit(operationId, () -> connectInternal(deviceName, operationId));
    }

    /** Executes on the remote PICO adbd shell stream, never through the tablet process. */
    public void executeShell(String command, int timeoutMs, String operationId) {
        if (timeoutMs < MIN_SHELL_TIMEOUT_MS || timeoutMs > MAX_SHELL_TIMEOUT_MS) {
            emitFailure(operationId, "INVALID_TIMEOUT");
            return;
        }
        submit(operationId, () -> executeShellInternal(command, timeoutMs, operationId));
    }

    /** Interrupts a queued or active ADB operation without disconnecting the physical USB cable. */
    public void cancel(String operationId) {
        if (operationId == null || operationId.isEmpty()) {
            return;
        }

        cancelledOperations.put(operationId, Boolean.TRUE);
        if (!operationId.equals(activeOperationId)) {
            return;
        }

        closeConnectingTransport(null);
        disconnectInternal(null, "OPERATION_CANCELLED", true);
    }

    public void disconnect() {
        submit(null, () -> disconnectInternal(null, "MANUAL_DISCONNECT", true));
    }

    /** Releases broadcasts, the current USB interface, and any pending I/O without retaining secrets. */
    public void dispose() {
        if (!disposed.compareAndSet(false, true)) {
            return;
        }

        if (receiverRegistered) {
            try {
                applicationContext.unregisterReceiver(usbReceiver);
            } catch (IllegalArgumentException ignored) {
                // The receiver was already removed by the Android framework.
            }
            receiverRegistered = false;
        }

        permissionOperations.clear();
        cancelledOperations.clear();
        closeConnectingTransport(null);
        closeActiveSession(null);
        serialExecutor.shutdownNow();
    }

    /** Returns null when Android Keystore cannot encrypt the provided secret. */
    public String encryptSecret(String plaintext) {
        try {
            return keyStore.encryptSecret(plaintext);
        } catch (GeneralSecurityException ignored) {
            return null;
        }
    }

    /** Returns null when the value is malformed or cannot be decrypted by this installation. */
    public String decryptSecret(String encrypted) {
        try {
            return keyStore.decryptSecret(encrypted);
        } catch (GeneralSecurityException ignored) {
            return null;
        }
    }

    private void requestUsbPermissionInternal(String deviceName, String operationId) {
        UsbDevice device = findAdbDevice(deviceName, operationId);
        if (device == null) {
            return;
        }
        if (usbManager.hasPermission(device)) {
            emitUsbPermission(operationId, device, true);
            return;
        }

        permissionOperations.put(device.getDeviceName(), normalizedOperationId(operationId));
        Intent intent = new Intent(ACTION_USB_PERMISSION).setPackage(applicationContext.getPackageName());
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }
        PendingIntent pendingIntent = PendingIntent.getBroadcast(applicationContext, 0, intent, flags);
        try {
            usbManager.requestPermission(device, pendingIntent);
        } catch (RuntimeException ignored) {
            permissionOperations.remove(device.getDeviceName());
            emitFailure(operationId, "USB_PERMISSION_REQUEST_FAILED");
        }
    }

    private void connectInternal(String deviceName, String operationId) {
        disconnectInternal(null, "RECONNECT", false);
        if (isOperationCancelled(operationId)) {
            emitFailure(operationId, "CANCELLED");
            return;
        }
        UsbDevice device = findAdbDevice(deviceName, operationId);
        if (device == null || disposed.get()) {
            return;
        }
        if (!usbManager.hasPermission(device)) {
            emitFailure(operationId, "USB_PERMISSION_REQUIRED");
            return;
        }

        AdbUsbTransport transport = null;
        boolean adoptedBySession = false;
        try {
            transport = AdbUsbTransport.open(usbManager, device);
            connectingTransport = transport;
            if (disposed.get() || isOperationCancelled(operationId)) {
                return;
            }

            AdbSession connectedSession = AdbSession.connect(transport, keyStore);
            if (disposed.get() || isOperationCancelled(operationId)) {
                connectedSession.close();
                return;
            }
            session = connectedSession;
            adoptedBySession = true;
            emitConnected(operationId, connectedSession.deviceName());
        } catch (IOException exception) {
            emitFailure(operationId, failureCode(exception, "CONNECT_TIMEOUT"));
        } finally {
            if (connectingTransport == transport) {
                connectingTransport = null;
            }
            if (!adoptedBySession && transport != null) {
                transport.close();
            }
        }
    }

    private void executeShellInternal(String command, int timeoutMs, String operationId) {
        AdbSession activeSession = session;
        if (activeSession == null || activeSession.isClosed()) {
            emitFailure(operationId, "NOT_CONNECTED");
            return;
        }

        try {
            AdbSession.ShellResult result = activeSession.executeShell(command, timeoutMs);
            emitShellComplete(operationId, result);
        } catch (IOException exception) {
            String errorCode = failureCode(exception, "SHELL_TIMEOUT");
            if (!"INVALID_ARGUMENT".equals(errorCode) && !"COMMAND_TOO_LARGE".equals(errorCode)) {
                disconnectIfCurrent(activeSession, errorCode);
            }
            emitFailure(operationId, errorCode);
        }
    }

    private void disconnectInternal(String operationId, String reason, boolean report) {
        AdbSession activeSession = session;
        session = null;
        if (activeSession == null) {
            return;
        }
        activeSession.close();
        if (report) {
            emitDisconnected(operationId, reason, activeSession.deviceName());
        }
    }

    private void disconnectIfCurrent(AdbSession expectedSession, String reason) {
        if (session != expectedSession) {
            return;
        }
        session = null;
        expectedSession.close();
        emitDisconnected(null, reason, expectedSession.deviceName());
    }

    private boolean isOperationCancelled(String operationId) {
        return operationId != null && cancelledOperations.containsKey(operationId);
    }

    private void closeConnectingTransport(String deviceName) {
        AdbUsbTransport transport = connectingTransport;
        if (transport != null
                && (deviceName == null || deviceName.equals(transport.deviceName()))) {
            transport.close();
        }
    }

    private void closeActiveSession(String deviceName) {
        AdbSession activeSession = session;
        if (activeSession == null
                || (deviceName != null && !deviceName.equals(activeSession.deviceName()))) {
            return;
        }

        session = null;
        activeSession.close();
    }

    private UsbDevice findAdbDevice(String deviceName, String operationId) {
        if (deviceName == null || deviceName.isEmpty()) {
            emitFailure(operationId, "INVALID_DEVICE_NAME");
            return null;
        }
        UsbDevice device = usbManager.getDeviceList().get(deviceName);
        if (device == null) {
            emitFailure(operationId, "USB_DEVICE_NOT_FOUND");
            return null;
        }
        if (AdbUsbTransport.findCandidate(device) == null) {
            emitFailure(operationId, "USB_INTERFACE_NOT_FOUND");
            return null;
        }
        return device;
    }

    private void submit(String operationId, Runnable work) {
        if (disposed.get()) {
            emitFailure(operationId, "DISPOSED");
            return;
        }
        try {
            serialExecutor.execute(() -> {
                if (disposed.get()) {
                    emitFailure(operationId, "DISPOSED");
                    return;
                }
                if (isOperationCancelled(operationId)) {
                    cancelledOperations.remove(operationId);
                    emitFailure(operationId, "CANCELLED");
                    return;
                }

                activeOperationId = operationId;
                try {
                    work.run();
                } catch (RuntimeException ignored) {
                    closeConnectingTransport(null);
                    disconnectInternal(null, "BRIDGE_INTERNAL_ERROR", true);
                    emitFailure(operationId, "BRIDGE_INTERNAL_ERROR");
                } finally {
                    if (operationId != null && operationId.equals(activeOperationId)) {
                        activeOperationId = null;
                    }
                    if (operationId != null) {
                        cancelledOperations.remove(operationId);
                    }
                }
            });
        } catch (RuntimeException ignored) {
            emitFailure(operationId, "DISPOSED");
        }
    }

    private void emitConnected(String operationId, String deviceName) {
        JSONObject event = newEvent("connected", operationId);
        put(event, "deviceName", deviceName);
        emit(event);
    }

    private void emitDisconnected(String operationId, String reason, String deviceName) {
        JSONObject event = newEvent("disconnected", operationId);
        put(event, "reason", reason);
        put(event, "deviceName", deviceName);
        emit(event);
    }

    private void emitUsbPermission(String operationId, UsbDevice device, boolean granted) {
        JSONObject event = newEvent("usb_permission", operationId);
        put(event, "deviceName", device.getDeviceName());
        put(event, "granted", granted);
        emit(event);
    }

    private void emitShellComplete(String operationId, AdbSession.ShellResult result) {
        JSONObject event = newEvent("shell_complete", operationId);
        put(event, "output", result.stdoutText());
        put(event, "stderr", result.stderrText());
        put(event, "outputBytes", result.outputBytes());
        put(event, "exitCode", result.exitCode());
        put(event, "succeeded", result.succeeded());
        put(event, "transportSucceeded", true);
        put(event, "shellV2", result.usedShellV2());
        emit(event);
    }

    private void emitFailure(String operationId, String errorCode) {
        JSONObject event = newEvent("operation_failed", operationId);
        put(event, "errorCode", errorCode);
        emit(event);
    }

    private JSONObject newEvent(String eventName, String operationId) {
        JSONObject event = new JSONObject();
        put(event, "event", eventName);
        if (operationId != null) {
            put(event, "operationId", normalizedOperationId(operationId));
        }
        return event;
    }

    private void emit(JSONObject event) {
        if (event == null || disposed.get()) {
            return;
        }
        callbackHandler.post(() -> {
            if (disposed.get()) {
                return;
            }
            try {
                callback.onBridgeEvent(event.toString());
            } catch (RuntimeException ignored) {
                // Unity callback failures must not leak a USB connection or crash the bridge worker.
            }
        });
    }

    private JSONObject toDeviceJson(UsbDevice device, AdbUsbTransport.Candidate candidate) throws JSONException {
        JSONObject json = new JSONObject();
        json.put("deviceName", device.getDeviceName());
        json.put("vendorId", device.getVendorId());
        json.put("productId", device.getProductId());
        json.put("hasPermission", usbManager.hasPermission(device));
        json.put("interfaceIndex", candidate.interfaceIndex);
        return json;
    }

    private static void put(JSONObject object, String key, Object value) {
        if (object == null) {
            return;
        }
        try {
            object.put(key, value);
        } catch (JSONException ignored) {
            // All event fields are primitive values; this only protects the Unity boundary.
        }
    }

    private static String normalizedOperationId(String operationId) {
        return operationId == null ? "" : operationId;
    }

    private static String failureCode(IOException exception, String timeoutCode) {
        if (exception instanceof AdbProtocol.AdbFailure) {
            return ((AdbProtocol.AdbFailure) exception).code();
        }
        if (exception instanceof SocketTimeoutException) {
            return timeoutCode;
        }
        return "USB_IO_FAILURE";
    }

    private final class UsbReceiver extends BroadcastReceiver {
        @Override
        public void onReceive(Context context, Intent intent) {
            if (intent == null || disposed.get()) {
                return;
            }
            String action = intent.getAction();
            UsbDevice device = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE);
            if (ACTION_USB_PERMISSION.equals(action)) {
                if (device == null) {
                    return;
                }
                String operationId = permissionOperations.remove(device.getDeviceName());
                if (operationId == null) {
                    return;
                }
                boolean granted = intent.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED, false);
                submit(operationId, () -> {
                    if (granted) {
                        emitUsbPermission(operationId, device, true);
                    } else {
                        emitFailure(operationId, "USB_PERMISSION_DENIED");
                    }
                });
                return;
            }
            if (UsbManager.ACTION_USB_DEVICE_ATTACHED.equals(action) && device != null) {
                submit(null, () -> {
                    AdbUsbTransport.Candidate candidate = AdbUsbTransport.findCandidate(device);
                    if (candidate == null) {
                        return;
                    }
                    JSONObject event = newEvent("device_attached", null);
                    put(event, "deviceName", device.getDeviceName());
                    put(event, "vendorId", device.getVendorId());
                    put(event, "productId", device.getProductId());
                    put(event, "interfaceIndex", candidate.interfaceIndex);
                    emit(event);
                });
                return;
            }
            if (UsbManager.ACTION_USB_DEVICE_DETACHED.equals(action) && device != null) {
                closeConnectingTransport(device.getDeviceName());
                closeActiveSession(device.getDeviceName());
                submit(null, () -> handleDetachedDevice(device));
            }
        }
    }

    private void handleDetachedDevice(UsbDevice device) {
        permissionOperations.remove(device.getDeviceName());
        AdbSession activeSession = session;
        if (activeSession != null && device.getDeviceName().equals(activeSession.deviceName())) {
            disconnectInternal(null, "USB_DETACHED", true);
        }
        JSONObject event = newEvent("device_detached", null);
        put(event, "deviceName", device.getDeviceName());
        emit(event);
    }
}
