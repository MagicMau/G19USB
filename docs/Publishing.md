# Publishing G19USB to GitHub and NuGet

Step-by-step guide for publishing the G19USB library for the first time and for subsequent releases.

---

## Section 1 — Create the GitHub repository

In your browser:

1. Go to <https://github.com/new>
2. **Repository name:** `G19USB`
3. **Description:** `A .NET library for controlling the Logitech G19/G19s keyboard LCD and keys over direct USB`
4. **Visibility:** Public (required for free NuGet hosting via GitHub Actions)
5. **Do NOT** tick "Add a README file", "Add .gitignore", or "Choose a license" — the local repo already has these
6. Click **Create repository**

---

## Section 2 — Push the local repo to GitHub

```powershell
cd C:\Source\Personal\G19USB
git remote add origin https://github.com/<YOUR_USERNAME>/G19USB.git
git branch -M main
git push -u origin main
```

Replace `<YOUR_USERNAME>` with your GitHub username.

---

## Section 3 — Verify CI passes

1. Open the repo on GitHub and click the **Actions** tab
2. The **CI** workflow should have triggered automatically on the push to `main`
3. Confirm the **Build** and **Test** steps are green

> **Note:** The **Pack** and **Upload artifact** steps only run on `v*` tags — they will be skipped on a plain branch push.

---

## Section 4 — Create a release (triggers NuGet pack)

Tagging a commit triggers the pack step in CI. Either use the command line:

```powershell
cd C:\Source\Personal\G19USB
git tag v1.0.0
git push origin v1.0.0
```

Or via the GitHub UI: **Releases → Create a new release → Tag: `v1.0.0` → Publish release**.

The CI workflow then:

1. Builds the library
2. MinVer reads `v1.0.0` from the tag → sets `PackageVersion = 1.0.0`
3. Packs `G19USB.1.0.0.nupkg` and `G19USB.1.0.0.snupkg`
4. Uploads both as a GitHub Actions artifact named **nuget**

---

## Section 5 — Publish to NuGet.org

1. Go to <https://www.nuget.org> and sign in (create an account if needed)
2. Navigate to **Account → API Keys → Create new key**:
   - **Key name:** `G19USB publish`
   - **Scope:** Select "Push new packages and package versions"
   - **Glob pattern:** `G19USB`
   - Click **Create**, then copy the key immediately — it is shown only once
3. Download the `nuget` artifact from the GitHub Actions run:
   **Actions → CI run → Artifacts → nuget → Download zip**
   Extract the zip to get the `.nupkg` and `.snupkg` files.
4. Publish:

   ```powershell
   dotnet nuget push G19USB.1.0.0.nupkg  --api-key <YOUR_API_KEY> --source https://api.nuget.org/v3/index.json
   dotnet nuget push G19USB.1.0.0.snupkg --api-key <YOUR_API_KEY> --source https://api.nuget.org/v3/index.json
   ```

5. The package becomes searchable on nuget.org within a few minutes.

---

## Section 6 — Automate NuGet publish via GitHub Actions (optional but recommended)

Avoid downloading the artifact on every release by having CI push directly to NuGet.

### 6.1 — Add the secret

In your GitHub repo: **Settings → Secrets and variables → Actions → New repository secret**

| Field | Value |
|---|---|
| Name | `NUGET_API_KEY` |
| Value | your NuGet.org API key |

### 6.2 — Update the workflow

Add a publish step to `.github/workflows/ci.yml` after the `upload-artifact` step:

```yaml
      - name: Publish to NuGet
        if: startsWith(github.ref, 'refs/tags/v')
        run: |
          dotnet nuget push ./artifacts/*.nupkg  --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
          dotnet nuget push ./artifacts/*.snupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
```

### 6.3 — Commit and push

```powershell
git add .github/workflows/ci.yml
git commit -m "ci: auto-publish to NuGet on version tag"
git push
```

Future releases will publish automatically whenever a `v*` tag is pushed.

---

## Section 7 — Future releases

For each new version, make your code changes and commit them, then:

```powershell
git tag v1.1.0
git push origin v1.1.0
```

The CI pipeline handles building, packing, and (if Section 6 is set up) publishing.

---

## Section 8 — Update the README badges

Once the package is live on NuGet and the repo is on GitHub, replace the badge placeholder comments in `README.md` with:

```markdown
[![NuGet](https://img.shields.io/nuget/v/G19USB.svg)](https://www.nuget.org/packages/G19USB)
[![CI](https://github.com/<YOUR_USERNAME>/G19USB/actions/workflows/ci.yml/badge.svg)](https://github.com/<YOUR_USERNAME>/G19USB/actions/workflows/ci.yml)
```

Replace `<YOUR_USERNAME>` with your GitHub username.
