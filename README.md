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
