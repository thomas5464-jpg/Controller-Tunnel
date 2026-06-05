# Controller Tunnel (Windows -> Windows)

Controller Tunnel is a Windows-only MVP that forwards controller input from a Windows client to a Windows server over UDP.

## About

hopefully i dont forget about this project.
Controller Tunnel was created to enable remote controller input forwarding between Windows machines when direct controller sharing is not possible. It focuses on a low-latency, easy-to-deploy Windows-only path for controller input, even though it does not currently support gyro or advanced sensor transport.

## Why This Exists

This project addresses scenarios where a controller is connected to one PC but input is needed on another PC, such as remote gaming setups, testing, or development workflows. It provides a lightweight UDP-based transport with an optional encrypted mode for added privacy, offering a simpler alternative to more complex long-distance sharing services that may be harder to set up.
## Prerequisites

- .NET SDK 6.0 or later
- On the server machine: install ViGEmBus driver and the ViGEm client library (https://vigem.org/)

## Build & Run

1. Build both projects:

```powershell
dotnet build
```

2. Start the server (usually requires admin privileges):

```powershell
dotnet run --project server
```

3. Start the console client, pointing to the server IP and port (default 5555):

```powershell
dotnet run --project client -- 192.0.2.5 5555
```

## Optional Encryption

To enable per-packet encryption with a pre-shared passphrase (PSK):

```powershell
dotnet run --project server -- 5555 "my-secret-passphrase"
dotnet run --project client -- 192.0.2.5 5555 "my-secret-passphrase"
```

The PSK is hashed with SHA-256 to derive an AES-256-GCM key.

## GUI Client

To use the Windows GUI client:

```powershell
dotnet run --project client-ui
```

Enter the server IP, port, and optional PSK, then click **Start**.

## Notes

- This MVP uses XInput for Xbox-style controllers.
- Gyro and other advanced controller data are not included in this initial release.

## Roadmap

- Add STUN/UPnP support for NAT traversal
- Add relay mode for multi-hop or internet-based forwarding
- Support additional controller APIs beyond XInput
- Add sensor and gyro transport support

## Disclaimer

- This project is an early MVP and may not be fully stable in all network or controller environments.
- Use at your own risk; administrative privileges and the ViGEmBus driver are required on the server.
- Do not expose the service to untrusted networks without proper security controls.


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

 ## v0.1.2 - Combined UI and Controller Detection

- Added a combined client/server Windows UI in `client-ui`.
- Added XInput controller detection and selection in the client UI.
- Added server mode status and remote endpoint feedback in the UI.
- Documented that DualShock/DualSense controllers require DS4Windows or another XInput wrapper to be detected.

