# sillon

> This is a modified version of slskd.  It is not maintained by, endorsed by, or affiliated with the slskd project or its author(s).

**sillon** is a Soulseek client daemon based on [slskd](https://github.com/slskd/slskd) 0.26.0. It is used as the
network engine of a desktop app and runs headless, driven through its REST API.

## Changes from slskd

- **Locked (buddies-only) shares.** A share whose path is prefixed with `~` (for example `~/Users/me/Music/Private`)
  is announced as *locked* to peers who are not buddies: its files go to the locked list of search responses, its
  directories to the locked list of browse responses, directory listings are not detailed, and download requests are
  rejected. Buddies see and download it normally. The buddy list is read from the file named by the
  `SILLON_BUDDIES_FILE` environment variable (one username per line) and reloaded automatically when it changes.
  See [`src/slskd/Sillon/Locking.cs`](src/slskd/Sillon/Locking.cs). Related upstream request:
  [slskd#937](https://github.com/slskd/slskd/issues/937).
- **Listening previews.** `POST /api/v0/sillon/preview` downloads a portion of a remote file starting at a given
  offset, then stops the transfer (one preview at a time), so that a few seconds can be heard before downloading.
  See [`src/slskd/Sillon/PreviewController.cs`](src/slskd/Sillon/PreviewController.cs).
- **Identity.** Distinct network client version identifier (`NetworkMinorVersion = 4113`) and startup banner.

Each modified source file carries a notice describing the change in its header.

## Building

Requires the .NET 10 SDK.

```bash
dotnet test tests/slskd.Tests.Unit --filter "FullyQualifiedName~Sillon"
dotnet publish src/slskd/slskd.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

## License

Licensed under the GNU Affero General Public License v3.0 with Additional Terms pursuant to Section 7 of the AGPLv3,
inherited from slskd. See [LICENSE](LICENSE) and [NOTICE](NOTICE). slskd is Copyright (c) JP Dillingham;
modifications are Copyright (c) 2026 b0w3rb1rd.
