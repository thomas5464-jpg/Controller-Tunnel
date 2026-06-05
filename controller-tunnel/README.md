# Controller Tunnel (Windows -> Windows)

Controller Tunnel is a Windows-only MVP that forwards controller input from a Windows client to a Windows server over UDP.

## About

Controller Tunnel was created to enable remote controller input forwarding between Windows machines when direct controller sharing is not possible. It focuses on a low-latency, easy-to-deploy Windows-only path for controller input, even though it does not currently support gyro or advanced sensor transport.

## Why This Exists

This project addresses scenarios where a controller is connected to one PC but input is needed on another PC, such as remote gaming setups, testing, or development workflows. It provides a lightweight UDP-based transport with an optional encrypted mode for added privacy, offering a simpler alternative to more complex long-distance sharing services that may be harder to set up.

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

3. Start the console client, pointing to the server host/IP and port (default 5555):

```powershell
dotnet run --project client -- 192.0.2.5 5555
```

The client accepts a public IP, LAN IP, or DNS name:

```powershell
dotnet run --project client -- my-controller-server.example.com 5555
```

## Internet Setup

Direct internet play uses UDP. For the client to reach the server from outside your local network:

- Run the server with listen IP `0.0.0.0` and port `5555`, or another port you choose.
- Allow inbound UDP on that port in Windows Firewall on the server PC.
- If the server is behind a home router, forward that UDP port from the router to the server PC's LAN IP.
- In the client, enter the server's public IP address or dynamic DNS hostname.
- If you use a PSK, the same PSK must be set on both the client and server.

Without a reachable public IP/port-forward or a relay server, UDP packets cannot cross most NAT routers reliably.

## Optional Encryption

To enable per-packet encryption with a pre-shared passphrase (PSK):

```powershell
dotnet run --project server -- 5555 "my-secret-passphrase"
dotnet run --project client -- my-controller-server.example.com 5555 "my-secret-passphrase"
```

The PSK is hashed with SHA-256 to derive an AES-256-GCM key.

## GUI Client

To use the Windows GUI client with combined client/server support:

```powershell
dotnet run --project client-ui
```

Use the built-in tabs to choose either the Client or Server mode. The Client tab includes controller detection and lets you select the active XInput controller before starting.

## Notes

- This MVP uses XInput for Xbox-style controllers.
- DualShock/DualSense controllers require an XInput wrapper such as DS4Windows to be detected by the client.
- The client can now detect and forward raw gyro axis data when a compatible controller exposes it.

## Disclaimer

- This project is an early MVP and may not be fully stable in all network or controller environments.
- Use at your own risk; administrative privileges and the ViGEmBus driver are required on the server.
- Do not expose the service to untrusted networks without proper security controls.

## Roadmap

- Add STUN/UPnP support for NAT traversal
- Add relay mode for multi-hop or internet-based forwarding
- Support additional controller APIs beyond XInput
- Add sensor and gyro transport support
