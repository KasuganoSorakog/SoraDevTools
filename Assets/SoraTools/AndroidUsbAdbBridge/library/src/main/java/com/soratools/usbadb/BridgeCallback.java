package com.soratools.usbadb;

/**
 * Unity implements this interface with AndroidJavaProxy. Every payload is a JSON object.
 */
public interface BridgeCallback {
    void onBridgeEvent(String eventJson);
}
