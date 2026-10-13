package com.soratools.usbadb;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.io.IOException;
import java.math.BigInteger;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.Key;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.KeyStore;
import java.security.PrivateKey;
import java.security.PublicKey;
import java.security.Signature;
import java.security.cert.Certificate;
import java.security.interfaces.RSAPublicKey;
import java.util.Arrays;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * Owns non-exportable Android Keystore keys used for ADB authorization and optional local secrets.
 */
final class AdbKeyStore {
    private static final String ADB_KEY_ALIAS = "com.soratools.usbadb.adb-rsa-v1";
    private static final String SECRET_KEY_ALIAS = "com.soratools.usbadb.secret-aes-v1";
    private static final int RSA_KEY_SIZE = 2048;
    private static final int RSA_WORD_COUNT = RSA_KEY_SIZE / 32;
    private static final BigInteger TWO_TO_32 = BigInteger.ONE.shiftLeft(32);
    private static final byte[] SHA1_DIGEST_INFO_PREFIX = new byte[] {
            0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2B, 0x0E,
            0x03, 0x02, 0x1A, 0x05, 0x00, 0x04, 0x14
    };
    private static final byte SECRET_FORMAT_VERSION = 1;
    private static final int GCM_TAG_LENGTH_BITS = 128;

    synchronized byte[] signAdbToken(byte[] token) throws GeneralSecurityException {
        if (token == null || token.length != 20) {
            throw new GeneralSecurityException("Unexpected ADB authentication token.");
        }

        KeyPair keyPair = getOrCreateAdbKeyPair();
        byte[] digestInfo = new byte[SHA1_DIGEST_INFO_PREFIX.length + token.length];
        System.arraycopy(SHA1_DIGEST_INFO_PREFIX, 0, digestInfo, 0, SHA1_DIGEST_INFO_PREFIX.length);
        System.arraycopy(token, 0, digestInfo, SHA1_DIGEST_INFO_PREFIX.length, token.length);

        try {
            // adbd verifies RSA_sign(NID_sha1, token), which expects a SHA-1 DigestInfo around the token.
            Signature signature = Signature.getInstance("NONEwithRSA");
            signature.initSign(keyPair.getPrivate());
            signature.update(digestInfo);
            byte[] signedToken = signature.sign();
            if (signedToken.length != RSA_KEY_SIZE / 8) {
                throw new GeneralSecurityException("Unexpected ADB signature size.");
            }
            return signedToken;
        } finally {
            Arrays.fill(digestInfo, (byte) 0);
        }
    }

    synchronized byte[] adbPublicKeyPayload() throws GeneralSecurityException {
        KeyPair keyPair = getOrCreateAdbKeyPair();
        if (!(keyPair.getPublic() instanceof RSAPublicKey)) {
            throw new GeneralSecurityException("ADB key is not RSA.");
        }

        String publicKey = Base64.encodeToString(
                encodeAdbPublicKey((RSAPublicKey) keyPair.getPublic()),
                Base64.NO_WRAP);
        return (publicKey + " soratools-usb-adb@android\u0000").getBytes(StandardCharsets.UTF_8);
    }

    synchronized String encryptSecret(String plaintext) throws GeneralSecurityException {
        if (plaintext == null) {
            throw new GeneralSecurityException("Secret is required.");
        }

        byte[] cleartext = plaintext.getBytes(StandardCharsets.UTF_8);
        try {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, getOrCreateSecretKey());
            byte[] ciphertext = cipher.doFinal(cleartext);
            byte[] iv = cipher.getIV();
            if (iv == null || iv.length == 0 || iv.length > 255) {
                throw new GeneralSecurityException("Secret encryption did not provide an IV.");
            }

            ByteBuffer packed = ByteBuffer.allocate(2 + iv.length + ciphertext.length);
            packed.put(SECRET_FORMAT_VERSION);
            packed.put((byte) iv.length);
            packed.put(iv);
            packed.put(ciphertext);
            Arrays.fill(ciphertext, (byte) 0);
            return Base64.encodeToString(packed.array(), Base64.NO_WRAP);
        } finally {
            Arrays.fill(cleartext, (byte) 0);
        }
    }

    synchronized String decryptSecret(String encoded) throws GeneralSecurityException {
        if (encoded == null || encoded.isEmpty()) {
            throw new GeneralSecurityException("Encrypted secret is required.");
        }

        byte[] packed;
        try {
            packed = Base64.decode(encoded, Base64.NO_WRAP);
        } catch (IllegalArgumentException exception) {
            throw new GeneralSecurityException("Invalid encrypted secret.", exception);
        }

        try {
            if (packed.length < 3 || packed[0] != SECRET_FORMAT_VERSION) {
                throw new GeneralSecurityException("Invalid encrypted secret.");
            }
            int ivLength = packed[1] & 0xFF;
            if (ivLength == 0 || packed.length <= 2 + ivLength) {
                throw new GeneralSecurityException("Invalid encrypted secret.");
            }

            byte[] iv = Arrays.copyOfRange(packed, 2, 2 + ivLength);
            byte[] ciphertext = Arrays.copyOfRange(packed, 2 + ivLength, packed.length);
            try {
                Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
                cipher.init(Cipher.DECRYPT_MODE, getOrCreateSecretKey(), new GCMParameterSpec(GCM_TAG_LENGTH_BITS, iv));
                byte[] cleartext = cipher.doFinal(ciphertext);
                try {
                    return new String(cleartext, StandardCharsets.UTF_8);
                } finally {
                    Arrays.fill(cleartext, (byte) 0);
                }
            } finally {
                Arrays.fill(iv, (byte) 0);
                Arrays.fill(ciphertext, (byte) 0);
            }
        } finally {
            Arrays.fill(packed, (byte) 0);
        }
    }

    private KeyPair getOrCreateAdbKeyPair() throws GeneralSecurityException {
        KeyStore keyStore = loadKeyStore();
        Key key = keyStore.getKey(ADB_KEY_ALIAS, null);
        Certificate certificate = keyStore.getCertificate(ADB_KEY_ALIAS);
        if (key instanceof PrivateKey && certificate != null
                && certificate.getPublicKey() instanceof RSAPublicKey
                && ((RSAPublicKey) certificate.getPublicKey()).getModulus().bitLength() == RSA_KEY_SIZE) {
            return new KeyPair(certificate.getPublicKey(), (PrivateKey) key);
        }

        if (keyStore.containsAlias(ADB_KEY_ALIAS)) {
            keyStore.deleteEntry(ADB_KEY_ALIAS);
        }

        KeyPairGenerator generator = KeyPairGenerator.getInstance(
                KeyProperties.KEY_ALGORITHM_RSA,
                "AndroidKeyStore");
        KeyGenParameterSpec specification = new KeyGenParameterSpec.Builder(
                ADB_KEY_ALIAS,
                KeyProperties.PURPOSE_SIGN)
                .setKeySize(RSA_KEY_SIZE)
                .setDigests(KeyProperties.DIGEST_NONE)
                .setSignaturePaddings(KeyProperties.SIGNATURE_PADDING_RSA_PKCS1)
                .build();
        generator.initialize(specification);
        return generator.generateKeyPair();
    }

    private SecretKey getOrCreateSecretKey() throws GeneralSecurityException {
        KeyStore keyStore = loadKeyStore();
        Key key = keyStore.getKey(SECRET_KEY_ALIAS, null);
        if (key instanceof SecretKey) {
            return (SecretKey) key;
        }
        if (keyStore.containsAlias(SECRET_KEY_ALIAS)) {
            keyStore.deleteEntry(SECRET_KEY_ALIAS);
        }

        KeyGenerator generator = KeyGenerator.getInstance(
                KeyProperties.KEY_ALGORITHM_AES,
                "AndroidKeyStore");
        KeyGenParameterSpec specification = new KeyGenParameterSpec.Builder(
                SECRET_KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setKeySize(256)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build();
        generator.init(specification);
        return generator.generateKey();
    }

    private KeyStore loadKeyStore() throws GeneralSecurityException {
        KeyStore keyStore = KeyStore.getInstance("AndroidKeyStore");
        try {
            keyStore.load(null);
        } catch (IOException exception) {
            throw new GeneralSecurityException("Unable to initialize Android Keystore.", exception);
        }
        return keyStore;
    }

    private byte[] encodeAdbPublicKey(RSAPublicKey publicKey) throws GeneralSecurityException {
        BigInteger modulus = publicKey.getModulus();
        if (modulus.bitLength() != RSA_KEY_SIZE
                || !BigInteger.valueOf(65537).equals(publicKey.getPublicExponent())) {
            throw new GeneralSecurityException("Unexpected RSA public key parameters.");
        }

        ByteBuffer result = ByteBuffer.allocate(4 + RSA_WORD_COUNT * 4 + 4 + RSA_WORD_COUNT * 4 + 4)
                .order(ByteOrder.LITTLE_ENDIAN);
        result.putInt(RSA_WORD_COUNT);
        BigInteger leastSignificantWord = modulus.and(TWO_TO_32.subtract(BigInteger.ONE));
        BigInteger n0Inverse = leastSignificantWord.modInverse(TWO_TO_32).negate().mod(TWO_TO_32);
        result.putInt(n0Inverse.intValue());

        putLittleEndianWords(result, modulus);
        BigInteger rSquared = BigInteger.ONE.shiftLeft(RSA_WORD_COUNT * 64).mod(modulus);
        putLittleEndianWords(result, rSquared);
        result.putInt(publicKey.getPublicExponent().intValue());
        return result.array();
    }

    private void putLittleEndianWords(ByteBuffer target, BigInteger value) {
        byte[] fixedLength = toFixedLength(value, RSA_KEY_SIZE / 8);
        for (int wordIndex = 0; wordIndex < RSA_WORD_COUNT; wordIndex++) {
            int byteIndex = fixedLength.length - ((wordIndex + 1) * 4);
            int word = ((fixedLength[byteIndex] & 0xFF) << 24)
                    | ((fixedLength[byteIndex + 1] & 0xFF) << 16)
                    | ((fixedLength[byteIndex + 2] & 0xFF) << 8)
                    | (fixedLength[byteIndex + 3] & 0xFF);
            target.putInt(word);
        }
        Arrays.fill(fixedLength, (byte) 0);
    }

    private byte[] toFixedLength(BigInteger value, int length) {
        byte[] source = value.toByteArray();
        byte[] result = new byte[length];
        int copyLength = Math.min(source.length, length);
        System.arraycopy(source, source.length - copyLength, result, length - copyLength, copyLength);
        Arrays.fill(source, (byte) 0);
        return result;
    }
}
