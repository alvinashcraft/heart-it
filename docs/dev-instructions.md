# Developer Instructions

## Prerequisites

- [Visual Studio 2022 17.12+](https://visualstudio.microsoft.com/) with the following workloads:
  - .NET desktop development
  - Universal Windows Platform development (for the packaging project)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Windows 10 SDK (10.0.26100.0)](https://developer.microsoft.com/windows/downloads/windows-sdk/)

## Solution Structure

```
HeartIt.slnx
├── HeartIt/                    # WPF application (.NET 10)
│   ├── HeartIt.csproj
│   ├── MainWindow.xaml         # Reaction toolbar UI
│   ├── App.xaml                # Application entry point and styles
│   └── Services/
│       └── TeamsReactionService.cs  # UI Automation logic for Teams
└── HeartIt.Package/            # Windows Application Packaging Project
    ├── HeartIt.Package.wapproj
    ├── Package.appxmanifest    # MSIX identity, capabilities, and visual assets
    └── Images/                 # Store logos, tiles, and splash screen
```

The solution targets two platforms: **x64** and **ARM64**.

## Building

### WPF Project Only

```powershell
dotnet build HeartIt/HeartIt.csproj -c Release
dotnet run --project HeartIt/HeartIt.csproj
```

### MSIX Package (requires MSBuild)

The packaging project uses the legacy `.wapproj` format which requires MSBuild (not `dotnet build`).
Open a Developer Command Prompt or ensure `msbuild` is on your PATH.

```powershell
# x64
msbuild HeartIt.Package/HeartIt.Package.wapproj /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly /restore

# ARM64
msbuild HeartIt.Package/HeartIt.Package.wapproj /p:Configuration=Release /p:Platform=ARM64 /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly /restore
```

Output is written to `HeartIt.Package/AppPackages/`.

Key MSBuild properties:

| Property | Description |
|----------|-------------|
| `Configuration` | `Debug` or `Release` |
| `Platform` | `x64` or `ARM64` |
| `AppxBundle` | `Never` for single-arch, `Always` to create a `.msixbundle` |
| `AppxBundlePlatforms` | Platforms to include in a bundle (e.g. `x64\|ARM64`) |
| `UapAppxPackageBuildMode` | `SideloadOnly` for local/winget distribution, `StoreUpload` for Store |
| `AppxPackageSigningEnabled` | `true` to sign during build (requires certificate properties) |
| `PackageCertificateKeyFile` | Path to `.pfx` certificate file |
| `PackageCertificatePassword` | Password for the `.pfx` file |
| `AppxPackageDir` | Override output directory |

## Code Signing

### Self-Signed Certificate (Local Development)

Create a self-signed certificate for local testing and sideloading using the `winapp` CLI:

```powershell
winapp cert generate --publisher "CN=HeartIt" --output HeartIt_DevCert.pfx
```

Or manually via PowerShell:

```powershell
$cert = New-SelfSignedCertificate `
  -Type Custom `
  -Subject "CN=HeartIt" `
  -KeyUsage DigitalSignature `
  -FriendlyName "HeartIt Dev Certificate" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

# Export to PFX
$password = ConvertTo-SecureString -String "YourPassword" -Force -AsPlainText
Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" `
  -FilePath "HeartIt_DevCert.pfx" `
  -Password $password
```

> **Important:** The `Subject` must match the `Publisher` attribute in `Package.appxmanifest`.
> Currently: `Publisher="CN=HeartIt"`.

To install the certificate for sideloading, import the `.cer` (public key) into `Trusted People` on the target machine.

### Production Certificate

For winget distribution, you need a code signing certificate from a trusted CA (e.g. SSL.com, SignPath, DigiCert).
Once obtained, update the CI workflow to use it via GitHub secrets:

1. Base64-encode the `.pfx` and store as a repository secret (`SIGNING_CERTIFICATE`)
2. Store the password as a secret (`SIGNING_CERTIFICATE_PASSWORD`)
3. Update `build-msix.yml` to decode the secret and pass it to MSBuild

## CI/CD Pipeline

The GitHub Actions workflow (`.github/workflows/build-msix.yml`) automates MSIX creation.
It uses the [`winapp` CLI](https://github.com/microsoft/WinAppCli) for certificate generation and SDK tool access.

### Triggers

| Trigger | Behavior |
|---------|----------|
| Push a tag matching `v*` | Builds both architectures, creates a bundle, and publishes a GitHub Release |
| Manual dispatch (Actions tab) | Builds with an optional version override; no release created |

### How It Works

1. **Build job** — runs in parallel for x64 and ARM64:
   - Installs .NET 10 SDK, MSBuild, and the `winapp` CLI
   - Derives version from the git tag (e.g. `v1.2.3` → `1.2.3.0`) or manual input
   - Patches `Package.appxmanifest` with the resolved version
   - Generates a self-signed certificate via `winapp cert generate`
   - Builds the MSIX via MSBuild
   - Uploads the `.msix` as a workflow artifact

2. **Bundle job** — runs after both builds complete:
   - Downloads the x64 and ARM64 `.msix` artifacts
   - Uses `winapp tool MakeAppx` to bundle them into a single `.msixbundle`
   - Uploads the bundle as a workflow artifact
   - If triggered by a version tag, creates a GitHub Release with the bundle attached

### Creating a Release

```bash
git tag v1.0.0
git push origin v1.0.0
```

The version must follow `major.minor.patch` or `major.minor.patch.revision` format.
Three-part versions are automatically padded (e.g. `v1.2.3` → `1.2.3.0`).

### Manual Workflow Run

1. Go to **Actions** → **Build MSIX** → **Run workflow**
2. Optionally enter a version number (e.g. `1.0.1.0`)
3. If left blank, defaults to `1.0.0.0`

Artifacts are available for download from the workflow run summary.

## Package Identity

The MSIX identity is defined in `HeartIt.Package/Package.appxmanifest`:

| Field | Value | Notes |
|-------|-------|-------|
| `Name` | `HeartIt` | Unique package identifier |
| `Publisher` | `CN=HeartIt` | Must match the signing certificate subject |
| `Version` | `1.0.0.0` | Overwritten by CI during builds |
| `ProcessorArchitecture` | `neutral` | Set per-package at build time |

### Updating the Publisher

If you switch to a production signing certificate, update `Publisher` in the manifest to match the
certificate's subject name. For example, if your certificate subject is `CN=My Company, O=My Company, L=City, S=State, C=US`,
the manifest must use the exact same value.

## Winget Distribution

To publish to [winget-pkgs](https://github.com/microsoft/winget-pkgs):

1. Build and sign the `.msixbundle` with a trusted certificate
2. Host the bundle at a stable URL (GitHub Releases works well)
3. Create a winget manifest (use [wingetcreate](https://github.com/microsoft/winget-create)):
   ```bash
   wingetcreate new https://github.com/youruser/heart-it/releases/download/v1.0.0/HeartIt.msixbundle
   ```
4. Submit the generated PR to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)

For subsequent updates, use `wingetcreate update` to bump the version.
