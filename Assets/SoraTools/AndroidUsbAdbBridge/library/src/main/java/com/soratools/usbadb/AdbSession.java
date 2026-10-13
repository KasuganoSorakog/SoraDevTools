package com.soratools.usbadb;

import android.os.SystemClock;

import java.io.ByteArrayOutputStream;
import java.io.Closeable;
import java.io.IOException;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;

/** One authenticated ADB connection. Calls are serialized by AndroidUsbAdbBridge. */
final class AdbSession implements Closeable {
    static final int CONNECT_TIMEOUT_MS = 30_000;
    static final int MAX_SHELL_OUTPUT_BYTES = 1024 * 1024;
    private static final int SHELL_V2_HEADER_LENGTH = 5;
    private static final int SHELL_V2_STDOUT = 1;
    private static final int SHELL_V2_STDERR = 2;
    private static final int SHELL_V2_EXIT = 3;
    private static final byte[] EMPTY_PAYLOAD = new byte[0];

    private final AdbUsbTransport transport;
    private final int peerMaxPayload;
    private final boolean supportsShellV2;
    private int nextLocalId = 1;
    private volatile boolean closed;

    private AdbSession(AdbUsbTransport transport, int peerMaxPayload, boolean supportsShellV2) {
        this.transport = transport;
        this.peerMaxPayload = peerMaxPayload;
        this.supportsShellV2 = supportsShellV2;
    }

    static AdbSession connect(AdbUsbTransport transport, AdbKeyStore keyStore) throws IOException {
        long deadlineMs = deadlineAfter(CONNECT_TIMEOUT_MS);
        // shell_v2 must be advertised by both peers before adbd accepts shell,v2,raw.
        byte[] banner = "host::features=shell_v2".getBytes(StandardCharsets.UTF_8);
        transport.writePacket(
                AdbProtocol.packet(
                        AdbProtocol.COMMAND_CNXN,
                        AdbProtocol.VERSION,
                        AdbProtocol.HOST_MAX_PAYLOAD,
                        banner),
                deadlineMs);

        boolean signatureSent = false;
        boolean publicKeySent = false;
        while (true) {
            try {
                AdbProtocol.Packet packet = transport.readPacket(deadlineMs);
                if (packet.command == AdbProtocol.COMMAND_CNXN) {
                    if (packet.arg1 <= 0 || packet.arg1 > AdbProtocol.ABSOLUTE_MAX_PAYLOAD) {
                        throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
                    }
                    return new AdbSession(transport, packet.arg1, supportsShellV2(packet.payload));
                }
                if (packet.command != AdbProtocol.COMMAND_AUTH
                        || packet.arg0 != AdbProtocol.AUTH_TOKEN
                        || packet.payload.length != 20) {
                    throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
                }

                if (!signatureSent) {
                    transport.writePacket(
                            AdbProtocol.packet(
                                    AdbProtocol.COMMAND_AUTH,
                                    AdbProtocol.AUTH_SIGNATURE,
                                    0,
                                    keyStore.signAdbToken(packet.payload)),
                            deadlineMs);
                    signatureSent = true;
                    continue;
                }
                if (!publicKeySent) {
                    transport.writePacket(
                            AdbProtocol.packet(
                                    AdbProtocol.COMMAND_AUTH,
                                    AdbProtocol.AUTH_RSAPUBLICKEY,
                                    0,
                                    keyStore.adbPublicKeyPayload()),
                            deadlineMs);
                    publicKeySent = true;
                    continue;
                }
                throw new AdbProtocol.AdbFailure("AUTH_FAILED");
            } catch (SocketTimeoutException exception) {
                if (publicKeySent) {
                    throw new AdbProtocol.AdbFailure("AUTHORIZATION_TIMEOUT");
                }
                throw exception;
            } catch (GeneralSecurityException exception) {
                throw new AdbProtocol.AdbFailure("KEYSTORE_FAILURE");
            }
        }
    }

    ShellResult executeShell(String command, int timeoutMs) throws IOException {
        if (closed) {
            throw new AdbProtocol.AdbFailure("NOT_CONNECTED");
        }
        if (command == null || command.indexOf('\u0000') >= 0) {
            throw new AdbProtocol.AdbFailure("INVALID_ARGUMENT");
        }

        long deadlineMs = deadlineAfter(timeoutMs);
        String service = supportsShellV2 ? "shell,v2,raw:" : "shell:";
        byte[] openPayload = (service + command + '\u0000').getBytes(StandardCharsets.UTF_8);
        if (openPayload.length > peerMaxPayload) {
            throw new AdbProtocol.AdbFailure("COMMAND_TOO_LARGE");
        }

        int localId = nextLocalId();
        int remoteId = 0;
        boolean completed = false;
        ShellOutputCollector output = supportsShellV2
                ? new ShellV2OutputCollector()
                : new LegacyShellOutputCollector();
        try {
            transport.writePacket(
                    AdbProtocol.packet(AdbProtocol.COMMAND_OPEN, localId, 0, openPayload),
                    deadlineMs);

            while (true) {
                AdbProtocol.Packet packet = transport.readPacket(deadlineMs);
                if (packet.arg1 != localId) {
                    throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
                }
                if (packet.command == AdbProtocol.COMMAND_OKAY) {
                    remoteId = acceptOkay(packet, remoteId);
                    continue;
                }
                if (packet.command == AdbProtocol.COMMAND_WRTE) {
                    remoteId = acceptWrite(packet, localId, remoteId, output, deadlineMs);
                    continue;
                }
                if (packet.command == AdbProtocol.COMMAND_CLSE) {
                    if (remoteId != 0 && packet.arg0 != remoteId) {
                        throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
                    }
                    if (packet.arg0 != 0) {
                        transport.writePacket(
                                AdbProtocol.packet(AdbProtocol.COMMAND_CLSE, localId, packet.arg0, EMPTY_PAYLOAD),
                                deadlineMs);
                    }
                    completed = true;
                    return output.complete();
                }
                throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
            }
        } finally {
            if (!completed && remoteId != 0 && !closed) {
                closeChannelQuietly(localId, remoteId);
            }
        }
    }

    String deviceName() {
        return transport.deviceName();
    }

    boolean isClosed() {
        return closed;
    }

    @Override
    public void close() {
        if (closed) {
            return;
        }
        closed = true;
        transport.close();
    }

    private int acceptOkay(AdbProtocol.Packet packet, int currentRemoteId) throws AdbProtocol.AdbFailure {
        if (packet.arg0 == 0 || (currentRemoteId != 0 && currentRemoteId != packet.arg0)) {
            throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
        }
        return packet.arg0;
    }

    private int acceptWrite(
            AdbProtocol.Packet packet,
            int localId,
            int currentRemoteId,
            ShellOutputCollector output,
            long deadlineMs) throws IOException {
        if (currentRemoteId == 0 || packet.arg0 != currentRemoteId
                || packet.payload.length > peerMaxPayload) {
            throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
        }

        output.append(packet.payload);
        transport.writePacket(
                AdbProtocol.packet(AdbProtocol.COMMAND_OKAY, localId, currentRemoteId, EMPTY_PAYLOAD),
                deadlineMs);
        return currentRemoteId;
    }

    private int nextLocalId() {
        int result = nextLocalId;
        nextLocalId = nextLocalId == Integer.MAX_VALUE ? 1 : nextLocalId + 1;
        return result;
    }

    private void closeChannelQuietly(int localId, int remoteId) {
        try {
            transport.writePacket(
                    AdbProtocol.packet(AdbProtocol.COMMAND_CLSE, localId, remoteId, EMPTY_PAYLOAD),
                    deadlineAfter(250));
        } catch (IOException ignored) {
            // The caller will report the original failure and close the session.
        }
    }

    private static boolean supportsShellV2(byte[] connectBanner) {
        String banner = new String(connectBanner, StandardCharsets.UTF_8);
        int featuresStart = banner.indexOf("features=");
        if (featuresStart < 0) {
            return false;
        }
        int featuresEnd = banner.indexOf(';', featuresStart);
        String features = banner.substring(
                featuresStart + "features=".length(),
                featuresEnd < 0 ? banner.length() : featuresEnd);
        for (String feature : features.split(",")) {
            if ("shell_v2".equals(feature)) {
                return true;
            }
        }
        return false;
    }

    private static long deadlineAfter(int timeoutMs) throws SocketTimeoutException {
        if (timeoutMs <= 0) {
            throw new SocketTimeoutException();
        }
        return SystemClock.elapsedRealtime() + timeoutMs;
    }

    private interface ShellOutputCollector {
        void append(byte[] payload) throws AdbProtocol.AdbFailure;

        ShellResult complete() throws AdbProtocol.AdbFailure;
    }

    private static final class LegacyShellOutputCollector implements ShellOutputCollector {
        private final ByteArrayOutputStream output = new ByteArrayOutputStream();

        @Override
        public void append(byte[] payload) throws AdbProtocol.AdbFailure {
            if (output.size() > MAX_SHELL_OUTPUT_BYTES - payload.length) {
                throw new AdbProtocol.AdbFailure("SHELL_OUTPUT_LIMIT");
            }
            output.write(payload, 0, payload.length);
        }

        @Override
        public ShellResult complete() {
            return new ShellResult(output.toByteArray(), EMPTY_PAYLOAD, -1, true, false);
        }
    }

    private static final class ShellV2OutputCollector implements ShellOutputCollector {
        private final ByteArrayOutputStream pending = new ByteArrayOutputStream();
        private final ByteArrayOutputStream stdout = new ByteArrayOutputStream();
        private final ByteArrayOutputStream stderr = new ByteArrayOutputStream();
        private Integer exitCode;

        @Override
        public void append(byte[] payload) throws AdbProtocol.AdbFailure {
            if ((long) pending.size() + payload.length > MAX_SHELL_OUTPUT_BYTES + SHELL_V2_HEADER_LENGTH) {
                throw new AdbProtocol.AdbFailure("SHELL_OUTPUT_LIMIT");
            }
            pending.write(payload, 0, payload.length);
            parseAvailableFrames();
        }

        @Override
        public ShellResult complete() throws AdbProtocol.AdbFailure {
            if (pending.size() != 0) {
                throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
            }
            int remoteExitCode = exitCode == null ? -1 : exitCode;
            return new ShellResult(
                    stdout.toByteArray(),
                    stderr.toByteArray(),
                    remoteExitCode,
                    exitCode != null && remoteExitCode == 0,
                    true);
        }

        private void parseAvailableFrames() throws AdbProtocol.AdbFailure {
            byte[] raw = pending.toByteArray();
            int consumed = 0;
            while (raw.length - consumed >= SHELL_V2_HEADER_LENGTH) {
                int streamId = raw[consumed] & 0xFF;
                int length = (raw[consumed + 1] & 0xFF)
                        | ((raw[consumed + 2] & 0xFF) << 8)
                        | ((raw[consumed + 3] & 0xFF) << 16)
                        | ((raw[consumed + 4] & 0xFF) << 24);
                if (length < 0 || length > MAX_SHELL_OUTPUT_BYTES) {
                    throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
                }
                if ((long) raw.length - consumed - SHELL_V2_HEADER_LENGTH < length) {
                    break;
                }

                acceptFrame(streamId, raw, consumed + SHELL_V2_HEADER_LENGTH, length);
                consumed += SHELL_V2_HEADER_LENGTH + length;
            }

            pending.reset();
            if (consumed < raw.length) {
                pending.write(raw, consumed, raw.length - consumed);
            }
        }

        private void acceptFrame(int streamId, byte[] source, int offset, int length)
                throws AdbProtocol.AdbFailure {
            if (streamId == SHELL_V2_STDOUT || streamId == SHELL_V2_STDERR) {
                if (exitCode != null || stdout.size() + stderr.size() > MAX_SHELL_OUTPUT_BYTES - length) {
                    throw new AdbProtocol.AdbFailure("SHELL_OUTPUT_LIMIT");
                }
                ByteArrayOutputStream destination = streamId == SHELL_V2_STDOUT ? stdout : stderr;
                destination.write(source, offset, length);
                return;
            }
            if (streamId == SHELL_V2_EXIT && length == 4 && exitCode == null) {
                exitCode = (source[offset] & 0xFF)
                        | ((source[offset + 1] & 0xFF) << 8)
                        | ((source[offset + 2] & 0xFF) << 16)
                        | ((source[offset + 3] & 0xFF) << 24);
                return;
            }
            throw new AdbProtocol.AdbFailure("PROTOCOL_ERROR");
        }
    }

    static final class ShellResult {
        private final byte[] stdout;
        private final byte[] stderr;
        private final int exitCode;
        private final boolean succeeded;
        private final boolean shellV2;

        ShellResult(byte[] stdout, byte[] stderr, int exitCode, boolean succeeded, boolean shellV2) {
            this.stdout = stdout;
            this.stderr = stderr;
            this.exitCode = exitCode;
            this.succeeded = succeeded;
            this.shellV2 = shellV2;
        }

        String stdoutText() {
            return new String(stdout, StandardCharsets.UTF_8);
        }

        String stderrText() {
            return new String(stderr, StandardCharsets.UTF_8);
        }

        int outputBytes() {
            return stdout.length + stderr.length;
        }

        int exitCode() {
            return exitCode;
        }

        boolean succeeded() {
            return succeeded;
        }

        boolean usedShellV2() {
            return shellV2;
        }
    }
}
