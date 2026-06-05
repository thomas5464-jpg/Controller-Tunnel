Controller Tunnel (Windows -> Windows)

MVP that forwards controller input from a Windows client to a Windows server using UDP.

Prerequisites
- .NET SDK (6 or later)
- On the server machine: install ViGEmBus driver and the ViGEm client library (https://vigem.org/)

Quick run
- Build both projects: `dotnet build` in each folder
- Start server (needs ViGEmBus installed and usually admin privileges):

```powershell
dotnet run --project server
```

-- Start client, pointing to the server IP and port (default 5555):

```powershell
dotnet run --project client -- 192.0.2.5 5555
```

- To enable per-packet encryption with a pre-shared passphrase (PSK):

```powershell
dotnet run --project server -- 5555 "my-secret-passphrase"
dotnet run --project client -- 192.0.2.5 5555 "my-secret-passphrase"
```

The PSK is hashed with SHA-256 to derive an AES-256-GCM key.

- To use the simple GUI client (Windows):

```powershell
dotnet run --project client-ui
```

Enter the server IP/port and optional PSK, then click Start.

Notes
- This MVP uses XInput on the client (Xbox-style controllers). Gyro data is not included in this first pass.
- Later: add STUN/UPnP, encryption (AES-GCM), and a relay option.
