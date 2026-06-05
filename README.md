# Controller Tunnel (Windows -> Windows)

Controller Tunnel is a Windows-only MVP that forwards controller input from a Windows client to a Windows server over UDP.

## Update Log

See the latest release notes and history in [UPDATE_LOG.md](UPDATE_LOG.md).

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
