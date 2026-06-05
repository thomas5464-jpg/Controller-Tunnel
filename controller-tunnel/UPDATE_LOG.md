# Update Log

## v0.1.0 - Initial Release

- Added a Windows controller tunneling MVP that forwards controller input from a Windows client to a Windows server over UDP.
- Implemented an optional pre-shared key (PSK) mode using SHA-256 + AES-256-GCM for packet encryption.
- Included both a console client and a Windows GUI client.
- Server integration requires ViGEmBus and the ViGEm client library.
- Client uses XInput for Xbox-style controller input.

## v0.1.1 - Notes and Roadmap

- Added documentation for usage and prerequisites.
- Planned future improvements:
  - STUN/UPnP support for easier NAT traversal.
  - Relay mode for multi-hop or internet-based forwarding.
  - Support for additional controller APIs beyond XInput.
  - Sensor and gyro data transport.

## v0.1.2 - Combined UI and Controller Detection 6/4/2026

- Added a combined client/server Windows UI in `client-ui`.
- Added XInput controller detection and selection in the client UI.
- Added server mode status and remote endpoint feedback in the UI.
- Documented that DualShock/DualSense controllers require DS4Windows or another XInput wrapper to be detected.

## v0.1.3 - Gyro HID Capture and Calibration 6/5/2026

- Added Sony DualShock 4 / DualSense HID motion report capture for real gyro data instead of reusing joystick axes.
- Forwarded optional gyro values with controller packets and displayed them in both client and server status.
- Added slower, averaged gyro UI updates so values are readable while testing.
- Added startup gyro calibration that asks the user to keep the controller still, subtracts stationary bias, converts raw counts to degrees/sec, and applies a small deadzone.
- Updated the console client to reset gyro calibration on startup.
