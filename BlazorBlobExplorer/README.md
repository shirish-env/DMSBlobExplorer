# Blazor WASM Azure Blob Explorer

A **fully client-side** Blazor WebAssembly app that authenticates users via **Microsoft Entra ID**
and provides a file explorer UI for a single **Azure Blob Storage** container (ADLS Gen2).

No server code. No connection strings in the client. All access is gated by your Azure RBAC assignments.

---

## How it works (security model)

```
User logs in with Entra ID (MSAL in browser)
        ↓
App requests token scoped to https://storage.azure.com/user_impersonation
        ↓
App calls "Get User Delegation Key" on Azure Blob REST API
        ↓
App self-signs short-lived User Delegation SAS tokens
        ↓
All blob operations (list, upload, download, delete) use SAS — no secrets ever leave Azure
```

The effective permissions are the **intersection** of what's in the SAS AND what's on the user's
RBAC role assignments — users can never escalate beyond their own permissions.

---

## Azure Setup (one-time, portal only)

### 1. Register an app in Entra ID

1. Go to **Azure Portal → Entra ID → App registrations → New registration**
2. Name it (e.g. `BlobExplorer`)
3. Supported account types: **Single tenant**
4. Redirect URI: **Single-page application (SPA)** → `https://localhost:5001/authentication/login-callback`
   - Add your production URL too when deploying
5. After creation, note the **Application (client) ID** and **Directory (tenant) ID**
6. Go to **API permissions → Add permission → Azure Storage → Delegated → user_impersonation**
7. Click **Grant admin consent**

### 2. Assign RBAC roles on the Storage Account

Each user (or group) needs **two** role assignments:

| Role | Scope | Purpose |
|---|---|---|
| `Storage Blob Delegator` | Storage account | Allows generating user delegation keys |
| `Storage Blob Data Contributor` | Container (or storage account) | Allows read/write/delete on blobs |

Go to: **Storage Account → Access Control (IAM) → Add role assignment**

### 3. Enable CORS on the Storage Account

Go to: **Storage Account → Resource sharing (CORS) → Blob service**

Add a rule:
| Field | Value |
|---|---|
| Allowed origins | `https://localhost:5001` (add prod URL too) |
| Allowed methods | `DELETE, GET, HEAD, MERGE, POST, OPTIONS, PUT` |
| Allowed headers | `*` |
| Exposed headers | `*` |
| Max age | `3600` |

---

## App Configuration

Edit `wwwroot/appsettings.json`:

```json
{
  "AzureAd": {
    "Authority": "https://login.microsoftonline.com/YOUR_TENANT_ID",
    "ClientId": "YOUR_CLIENT_ID",
    "ValidateAuthority": true
  },
  "AzureStorage": {
    "AccountName": "YOUR_STORAGE_ACCOUNT_NAME",
    "ContainerName": "YOUR_CONTAINER_NAME"
  }
}
```

---

## Running locally

```bash
dotnet restore
dotnet run
```

Navigate to `https://localhost:5001` — you'll be redirected to Entra ID login automatically.

---

## Features

- ✅ Entra ID login (MSAL, redirect flow)
- ✅ Browse folders (ADLS Gen2 hierarchical namespace with `/` delimiter)
- ✅ Select multiple files to upload (no limit, up to 500MB per file)
- ✅ Create new folders
- ✅ Download files (via short-lived SAS URL)
- ✅ Delete single files
- ✅ Delete multiple selected files
- ✅ Filter/search by filename
- ✅ Sort by name, size, or modified date
- ✅ Upload progress bars per file
- ✅ Breadcrumb navigation
- ✅ User delegation SAS (token never leaves browser, respects RBAC)

---

## Deployment

Build and deploy the `wwwroot` output to any static host (Azure Static Web Apps, Azure Blob
static website hosting, etc.):

```bash
dotnet publish -c Release
```

Output will be in `bin/Release/net8.0/publish/wwwroot`.

Remember to add your production URL to:
- The SPA redirect URIs in Entra ID app registration
- The CORS allowed origins on the storage account
