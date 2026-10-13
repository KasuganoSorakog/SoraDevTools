package com.soratools.usbadb;

import android.hardware.usb.UsbConstants;
import android.hardware.usb.UsbDevice;
import android.hardware.usb.UsbDeviceConnection;
import android.hardware.usb.UsbEndpoint;
import android.hardware.usb.UsbInterface;
import android.hardware.usb.UsbManager;
import android.os.SystemClock;

import java.io.Closeable;
import java.io.IOException;
import java.net.SocketTimeoutException;

/** A closeable, exact-read/exact-write wrapper around Android's USB bulk API. */
final class AdbUsbTransport implements Closeable {
    private static final int ADB_INTERFACE_CLASS = 0xFF;
    private static final int ADB_INTERFACE_SUBCLASS = 0x42;
    private static final int ADB_INTERFACE_PROTOCOL = 0x01;
    private static final int MAX_TRANSFER_BYTES = 16 * 1024;
    private static final int MAX_SINGLE_TRANSFER_TIMEOUT_MS = 1000;

    private final Object ioLock = new Object();
    private final UsbDevice device;
    private final UsbDeviceConnection connection;
    private final UsbInterface adbInterface;
    private final UsbEndpoint inputEndpoint;
    private final UsbEndpoint outputEndpoint;
    private boolean closed;

    private AdbUsbTransport(
            UsbDevice device,
            UsbDeviceConnection connection,
            UsbInterface adbInterface,
            UsbEndpoint inputEndpoint,
            UsbEndpoint outputEndpoint) {
        this.device = device;
        this.connection = connection;
        this.adbInterface = adbInterface;
        this.inputEndpoint = inputEndpoint;
        this.outputEndpoint = outputEndpoint;
    }

    static Candidate findCandidate(UsbDevice device) {
        if (device == null) {
            return null;
        }

        for (int interfaceIndex = 0; interfaceIndex < device.getInterfaceCount(); interfaceIndex++) {
            UsbInterface usbInterface = device.getInterface(interfaceIndex);
            if (usbInterface.getInterfaceClass() != ADB_INTERFACE_CLASS
                    || usbInterface.getInterfaceSubclass() != ADB_INTERFACE_SUBCLASS
                    || usbInterface.getInterfaceProtocol() != ADB_INTERFACE_PROTOCOL) {
                continue;
            }

            UsbEndpoint input = null;
            UsbEndpoint output = null;
            for (int endpointIndex = 0; endpointIndex < usbInterface.getEndpointCount(); endpointIndex++) {
                UsbEndpoint endpoint = usbInterface.getEndpoint(endpointIndex);
                if (endpoint.getType() != UsbConstants.USB_ENDPOINT_XFER_BULK) {
                    continue;
                }
                if (endpoint.getDirection() == UsbConstants.USB_DIR_IN) {
                    input = endpoint;
                } else if (endpoint.getDirection() == UsbConstants.USB_DIR_OUT) {
                    output = endpoint;
                }
            }

            if (input != null && output != null) {
                return new Candidate(usbInterface, input, output, interfaceIndex);
            }
        }
        return null;
    }

    static AdbUsbTransport open(UsbManager usbManager, UsbDevice device) throws AdbProtocol.AdbFailure {
        Candidate candidate = findCandidate(device);
        if (candidate == null) {
            throw new AdbProtocol.AdbFailure("USB_INTERFACE_NOT_FOUND");
        }

        UsbDeviceConnection connection = usbManager.openDevice(device);
        if (connection == null) {
            throw new AdbProtocol.AdbFailure("USB_OPEN_FAILED");
        }
        if (!connection.claimInterface(candidate.usbInterface, true)) {
            connection.close();
            throw new AdbProtocol.AdbFailure("USB_CLAIM_FAILED");
        }

        return new AdbUsbTransport(
                device,
                connection,
                candidate.usbInterface,
                candidate.inputEndpoint,
                candidate.outputEndpoint);
    }

    String deviceName() {
        return device.getDeviceName();
    }

    void writePacket(AdbProtocol.Packet packet, long deadlineMs) throws IOException {
        synchronized (ioLock) {
            ensureOpen();
            writeFully(AdbProtocol.headerFor(packet), deadlineMs);
            if (packet.payload.length > 0) {
                writeFully(packet.payload, deadlineMs);
            }
        }
    }

    AdbProtocol.Packet readPacket(long deadlineMs) throws IOException {
        synchronized (ioLock) {
            ensureOpen();
            byte[] rawHeader = new byte[AdbProtocol.HEADER_LENGTH];
            readFully(rawHeader, deadlineMs);
            AdbProtocol.Header header = AdbProtocol.parseHeader(rawHeader);
            byte[] payload = new byte[header.payloadLength];
            if (payload.length > 0) {
                readFully(payload, deadlineMs);
            }
            AdbProtocol.validatePayload(header, payload);
            return AdbProtocol.packet(header.command, header.arg0, header.arg1, payload);
        }
    }

    private void writeFully(byte[] source, long deadlineMs) throws IOException {
        int offset = 0;
        while (offset < source.length) {
            int length = Math.min(MAX_TRANSFER_BYTES, source.length - offset);
            int transferred = connection.bulkTransfer(
                    outputEndpoint,
                    source,
                    offset,
                    length,
                    nextTransferTimeout(deadlineMs));
            if (transferred < 0) {
                throw transferFailure(deadlineMs);
            }
            if (transferred == 0) {
                continue;
            }
            offset += transferred;
        }
    }

    private void readFully(byte[] destination, long deadlineMs) throws IOException {
        int offset = 0;
        while (offset < destination.length) {
            int length = Math.min(MAX_TRANSFER_BYTES, destination.length - offset);
            int transferred = connection.bulkTransfer(
                    inputEndpoint,
                    destination,
                    offset,
                    length,
                    nextTransferTimeout(deadlineMs));
            if (transferred < 0) {
                if (!closed && SystemClock.elapsedRealtime() < deadlineMs) {
                    // Android bulkTransfer reports a read timeout as -1. Keep reading until the ADB deadline.
                    continue;
                }
                throw transferFailure(deadlineMs);
            }
            if (transferred == 0) {
                continue;
            }
            offset += transferred;
        }
    }

    private int nextTransferTimeout(long deadlineMs) throws IOException {
        ensureOpen();
        long remainingMs = deadlineMs - SystemClock.elapsedRealtime();
        if (remainingMs <= 0) {
            throw new SocketTimeoutException();
        }
        return (int) Math.min(remainingMs, MAX_SINGLE_TRANSFER_TIMEOUT_MS);
    }

    private IOException transferFailure(long deadlineMs) {
        if (closed) {
            return new AdbProtocol.AdbFailure("USB_DISCONNECTED");
        }
        if (SystemClock.elapsedRealtime() >= deadlineMs) {
            return new SocketTimeoutException();
        }
        return new AdbProtocol.AdbFailure("USB_TRANSFER_FAILED");
    }

    private void ensureOpen() throws AdbProtocol.AdbFailure {
        if (closed) {
            throw new AdbProtocol.AdbFailure("USB_DISCONNECTED");
        }
    }

    @Override
    public void close() {
        synchronized (ioLock) {
            if (closed) {
                return;
            }
            closed = true;
            try {
                connection.releaseInterface(adbInterface);
            } catch (RuntimeException ignored) {
                // A disconnected device may reject releaseInterface; closing remains required.
            }
            connection.close();
        }
    }

    static final class Candidate {
        final UsbInterface usbInterface;
        final UsbEndpoint inputEndpoint;
        final UsbEndpoint outputEndpoint;
        final int interfaceIndex;

        Candidate(
                UsbInterface usbInterface,
                UsbEndpoint inputEndpoint,
                UsbEndpoint outputEndpoint,
                int interfaceIndex) {
            this.usbInterface = usbInterface;
            this.inputEndpoint = inputEndpoint;
            this.outputEndpoint = outputEndpoint;
            this.interfaceIndex = interfaceIndex;
        }
    }
}
