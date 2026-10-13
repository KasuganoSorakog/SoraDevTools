package com.soratools.usbadb;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** Package framing for the transport protocol documented in adb/protocol.txt. */
final class AdbProtocol {
    static final int COMMAND_CNXN = command("CNXN");
    static final int COMMAND_AUTH = command("AUTH");
    static final int COMMAND_OPEN = command("OPEN");
    static final int COMMAND_OKAY = command("OKAY");
    static final int COMMAND_CLSE = command("CLSE");
    static final int COMMAND_WRTE = command("WRTE");

    static final int AUTH_TOKEN = 1;
    static final int AUTH_SIGNATURE = 2;
    static final int AUTH_RSAPUBLICKEY = 3;
    static final int VERSION = 0x01000001;
    static final int HOST_MAX_PAYLOAD = 4096;
    static final int ABSOLUTE_MAX_PAYLOAD = 1024 * 1024;
    static final int HEADER_LENGTH = 24;

    private AdbProtocol() {
    }

    static Packet packet(int command, int arg0, int arg1, byte[] payload) {
        return new Packet(command, arg0, arg1, payload == null ? new byte[0] : payload);
    }

    static byte[] headerFor(Packet packet) {
        ByteBuffer buffer = ByteBuffer.allocate(HEADER_LENGTH).order(ByteOrder.LITTLE_ENDIAN);
        buffer.putInt(packet.command);
        buffer.putInt(packet.arg0);
        buffer.putInt(packet.arg1);
        buffer.putInt(packet.payload.length);
        buffer.putInt(checksum(packet.payload));
        buffer.putInt(packet.command ^ 0xFFFFFFFF);
        return buffer.array();
    }

    static Header parseHeader(byte[] rawHeader) throws AdbFailure {
        if (rawHeader == null || rawHeader.length != HEADER_LENGTH) {
            throw new AdbFailure("PROTOCOL_ERROR");
        }

        ByteBuffer buffer = ByteBuffer.wrap(rawHeader).order(ByteOrder.LITTLE_ENDIAN);
        int command = buffer.getInt();
        int arg0 = buffer.getInt();
        int arg1 = buffer.getInt();
        int payloadLength = buffer.getInt();
        int payloadChecksum = buffer.getInt();
        int magic = buffer.getInt();

        if (magic != (command ^ 0xFFFFFFFF)
                || payloadLength < 0
                || payloadLength > ABSOLUTE_MAX_PAYLOAD) {
            throw new AdbFailure("PROTOCOL_ERROR");
        }

        return new Header(command, arg0, arg1, payloadLength, payloadChecksum);
    }

    static void validatePayload(Header header, byte[] payload) throws AdbFailure {
        if (payload == null || payload.length != header.payloadLength
                || checksum(payload) != header.payloadChecksum) {
            throw new AdbFailure("PROTOCOL_ERROR");
        }
    }

    static int command(String ascii) {
        if (ascii == null || ascii.length() != 4) {
            throw new IllegalArgumentException("ADB command must contain four ASCII characters.");
        }
        return (ascii.charAt(0) & 0xFF)
                | ((ascii.charAt(1) & 0xFF) << 8)
                | ((ascii.charAt(2) & 0xFF) << 16)
                | ((ascii.charAt(3) & 0xFF) << 24);
    }

    private static int checksum(byte[] payload) {
        int value = 0;
        for (byte item : payload) {
            value += item & 0xFF;
        }
        return value;
    }

    static final class Header {
        final int command;
        final int arg0;
        final int arg1;
        final int payloadLength;
        final int payloadChecksum;

        Header(int command, int arg0, int arg1, int payloadLength, int payloadChecksum) {
            this.command = command;
            this.arg0 = arg0;
            this.arg1 = arg1;
            this.payloadLength = payloadLength;
            this.payloadChecksum = payloadChecksum;
        }
    }

    static final class Packet {
        final int command;
        final int arg0;
        final int arg1;
        final byte[] payload;

        Packet(int command, int arg0, int arg1, byte[] payload) {
            this.command = command;
            this.arg0 = arg0;
            this.arg1 = arg1;
            this.payload = payload;
        }
    }

    static final class AdbFailure extends IOException {
        private final String code;

        AdbFailure(String code) {
            super(code);
            this.code = code;
        }

        String code() {
            return code;
        }
    }
}
