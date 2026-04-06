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

For winget distribution, you need a code signing certificate from a trusted CA.
Since 2023, all OV/EV code signing certificates require hardware-backed private key storage (HSM).
This means you can't just download a `.pfx` and use it in CI — you need a cloud HSM solution.

- **OV (Organization Validation)** is sufficient for winget distribution, but new apps
  may trigger SmartScreen "Windows protected your PC" warnings until the app builds
  download reputation over time
- **EV (Extended Validation)** costs more but establishes immediate SmartScreen trust —
  users won't see the "Windows protected your PC" warning on first install

#### Option 1: Azure Trusted Signing (Recommended)

[Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/overview) is a fully managed
Microsoft signing service. It handles certificate lifecycle, HSM storage, and timestamping — no need
to purchase or manage certificates yourself. Certificates chain to the Microsoft Identity Verification
root CA, which is trusted by Windows.

**Pricing:** Basic tier (~$9.99/mo) includes 5,000 signatures/month — more than enough for CI.

**Setup:**

1. **Register the resource provider** — In your Azure subscription, go to **Settings > Resource providers**
   and register `Microsoft.CodeSigning`

2. **Create a Trusted Signing account** — In the Azure portal, search for "Trusted Signing Accounts"
   and create one in a [supported region](https://learn.microsoft.com/azure/trusted-signing/quickstart)

3. **Complete identity validation** — Create an identity validation request (Public Trust, Organization
   or Individual). This requires business details and takes 1–15 business days to process

4. **Create a certificate profile** — Once identity validation is complete, create a "Public Trust"
   certificate profile linked to your validated identity

5. **Create a service principal for CI** — This is the identity your GitHub Actions workflow uses
   to authenticate with Azure and sign packages:

   a. In the Azure portal, go to **Entra ID > App registrations > New registration**
   b. Name it something like `heartit-signing`, leave defaults, and click **Register**
   c. Note the **Application (client) ID** and your **Directory (tenant) ID** from the overview page
   d. Go to **Certificates & secrets > Client secrets > New client secret**, add one, and copy the value

   For OIDC (recommended — no secret to rotate):

   a. Instead of a client secret, go to **Certificates & secrets > Federated credentials > Add credential**
   b. Select **GitHub Actions deploying Azure resources**
   c. Fill in your org (`alvinashcraft`), repo (`heart-it`), entity type **Tag**, and tag pattern `v*`
   d. This lets the workflow authenticate without storing a secret

6. **Assign roles** — On your Trusted Signing account in the Azure portal:

   a. Go to **Access control (IAM) > Add role assignment**
   b. Search for `Artifact Signing Certificate Profile Signer`
   c. On the **Members** tab, select **User, group, or service principal**
   d. Click **Select members**, search for your app registration name (e.g. `heartit-signing`), and select it
   e. Review and assign

7. **Configure GitHub secrets:**
   - `AZURE_TENANT_ID` — Directory (tenant) ID from the app registration
   - `AZURE_CLIENT_ID` — Application (client) ID from the app registration
   - `AZURE_CLIENT_SECRET` — Client secret value (skip if using OIDC)
   - `AZURE_SUBSCRIPTION_ID` — Azure subscription ID

8. **Update `Package.appxmanifest`** — Set `Publisher` to match the certificate subject from your
   identity validation (visible in the certificate profile details)

**CI workflow signing step** (replaces the self-signed cert steps):

```yaml
- name: Azure login
  uses: azure/login@v1
  with:
    client-id: ${{ secrets.AZURE_CLIENT_ID }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

- name: Sign MSIX with Trusted Signing
  uses: azure/trusted-signing-action@v1
  with:
    endpoint: https://eus.codesigning.azure.net/
    signing-account-name: <your-account-name>
    certificate-profile-name: <your-profile-name>
    files-folder: ${{ github.workspace }}\AppPackages
    files-folder-filter: msix
    file-digest: SHA256
    timestamp-rfc3161: http://timestamp.acs.microsoft.com
    timestamp-digest: SHA256
```

> For OIDC (recommended), configure federated credentials on the app registration and add
> `permissions: id-token: write` to the workflow job. See the
> [action docs](https://github.com/Azure/trusted-signing-action/blob/main/docs/OIDC.md).

#### Option 2: Azure Key Vault + AzureSignTool

If you already have a certificate from a third-party CA (SSL.com, Sectigo, DigiCert, etc.),
have the CA deliver it into an Azure Key Vault HSM and sign remotely using
[AzureSignTool](https://github.com/vcsjones/AzureSignTool) in CI.

**Recommended CA providers:**

| Provider | OV (~$/yr) | Notes |
|----------|-----------|-------|
| SSL.com | ~$129 | Budget-friendly, fast validation |
| FastSSL | ~$129 | Cheapest option |
| Sectigo/Comodo | ~$166–226 | Most widely used, offers KeyStorage cloud HSM |
| DigiCert | ~$434+ | Premium, offers KeyLocker cloud HSM |

**Setup:**

1. Create an Azure Key Vault with HSM-backed keys
2. Import or have the CA deliver the certificate to the vault
3. Create a service principal (app registration) with `Key Vault Crypto User` and
   `Key Vault Certificate User` roles on the vault
4. Add these GitHub repository secrets:
   - `AZURE_TENANT_ID` — Entra ID tenant
   - `AZURE_CLIENT_ID` — Service principal app ID
   - `AZURE_CLIENT_SECRET` — Service principal secret
   - `AZURE_KEY_VAULT_URI` — Vault URI (e.g. `https://myvault.vault.azure.net`)
   - `AZURE_KEY_VAULT_CERT_NAME` — Certificate name in the vault

**CI workflow signing step** (replaces the self-signed cert steps):

```yaml
- name: Install AzureSignTool
  run: dotnet tool install --global AzureSignTool

- name: Sign MSIX package
  shell: pwsh
  run: |
    $msix = Get-ChildItem -Path AppPackages -Filter *.msix -Recurse | Select-Object -First 1
    AzureSignTool sign `
      --azure-key-vault-url "${{ secrets.AZURE_KEY_VAULT_URI }}" `
      --azure-key-vault-client-id "${{ secrets.AZURE_CLIENT_ID }}" `
      --azure-key-vault-client-secret "${{ secrets.AZURE_CLIENT_SECRET }}" `
      --azure-key-vault-tenant-id "${{ secrets.AZURE_TENANT_ID }}" `
      --azure-key-vault-certificate "${{ secrets.AZURE_KEY_VAULT_CERT_NAME }}" `
      --timestamp-rfc3161 http://timestamp.digicert.com `
      --verbose `
      $msix.FullName
```

> **Important:** Update `Publisher` in `Package.appxmanifest` to match the certificate's
> subject (e.g. `CN=Your Name, O=Your Org, L=City, S=State, C=US`).

#### Option 3: CA-Provided Cloud HSM

Some CAs offer their own cloud HSM services that integrate with CI:

- **DigiCert KeyLocker** — provides a signing client and API keys for CI
- **Sectigo KeyStorage** — similar cloud HSM with CLI-based signing

These avoid the need for Azure infrastructure but add a vendor-specific dependency.
Refer to the CA's documentation for GitHub Actions integration steps.

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
