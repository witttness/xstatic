# Azure deployment

The Bicep files deploy one isolated Extatic stack per resource group. Deploy `test` and `prod` separately. Each stack has a Container Apps environment, one Container App containing OAuth2 Proxy, the Angular dashboard, and the .NET API, a private PostgreSQL Flexible Server, a Blob Storage account, and Log Analytics. Only OAuth2 Proxy has external ingress. The API and dashboard use internal ports in the containers' shared network namespace.

The shared Basic Azure Container Registry is deployed once. The two Container Apps pull images using separate user-assigned managed identities with `AcrPull` access.

## Prerequisites

- Azure CLI, Bicep CLI, and an Azure subscription with permission to create resources and assign the `AcrPull` role on the registry. If Bicep was installed separately from Azure CLI, run `az config set bicep.use_binary_from_path=true` so Azure CLI uses the `bicep` command on your PATH.
- An OIDC provider and separate client credentials for test and prod. Register each deployment's `oauthCallbackUrl` output as a redirect URI after its first deployment. Set the allowed email domain deliberately; use `*` only if the provider itself restricts membership appropriately.
- Two distinct, high-entropy JWT keys, database passwords, and OAuth2 Proxy cookie secrets. Generate each cookie secret with `openssl rand -base64 32`.
- Ensure the region supports PostgreSQL 18 and the B1ms Flexible Server SKU.

## Build and deploy

Run these commands from the repository root. Set the subscription first with `az account set --subscription <subscription-id>`.

```bash
az group create --name rg-extatic-shared --location centralus
az deployment group create --name registry --resource-group rg-extatic-shared \
  --template-file infra/azure/registry.bicep

REGISTRY=$(az deployment group show --resource-group rg-extatic-shared \
  --name registry --query properties.outputs.registryName.value -o tsv)
TAG=$(date -u +%Y%m%d%H%M%S)

az acr build --registry "$REGISTRY" --image "extatic-api:$TAG" \
  --file src/Extatic.Api/Dockerfile .
az acr build --registry "$REGISTRY" --image "extatic-dashboard:$TAG" \
  --file src/extatic-dashboard/Dockerfile .
```

Create local secret parameter files from `secrets.example.json`, then replace every placeholder with a real value. The files matching `*.secrets.json` are ignored by Git.

```bash
cp infra/azure/secrets.example.json infra/azure/test.secrets.json
cp infra/azure/secrets.example.json infra/azure/prod.secrets.json
az group create --name rg-extatic-test --location centralus
az group create --name rg-extatic-prod --location centralus

az deployment group create --name extatic-test --resource-group rg-extatic-test \
  --template-file infra/azure/main.bicep \
  --parameters @infra/azure/test.parameters.json @infra/azure/test.secrets.json \
  registryName="$REGISTRY" registryResourceGroup=rg-extatic-shared imageTag="$TAG"

az deployment group create --name extatic-prod --resource-group rg-extatic-prod \
  --template-file infra/azure/main.bicep \
  --parameters @infra/azure/prod.parameters.json @infra/azure/prod.secrets.json \
  registryName="$REGISTRY" registryResourceGroup=rg-extatic-shared imageTag="$TAG"
```

The deployment output includes the app URL and OAuth callback URL. After registering the callbacks with the OIDC provider, open each app URL to verify sign-in. If your provider has different issuers or clients for test and prod, use the matching values in each secret file.

## Routing and operation

- External HTTPS goes to OAuth2 Proxy on port 4180. Authenticated requests go to the dashboard's nginx on port 8081. nginx forwards `/api/` to the API on port 8080.
- `/api/app_<16 hex digits>/...` bypasses the OAuth2 Proxy login so the API can enforce its own API key or JWT. Other API routes require an OAuth2 Proxy session. Browser CORS preflight requests also bypass the proxy login.
- OAuth2 Proxy strips caller-supplied identity headers before adding headers from the authenticated session. Do not add another ingress or public port to the API or dashboard.
- The API applies EF Core migrations at startup. The Container App is limited to one replica to avoid concurrent migrations and to keep the API's webhook workers running in one process. Production keeps one replica running; test may scale to zero, so its background workers pause while idle.
- PostgreSQL uses a delegated subnet and private DNS. Storage allows public blob reads because the current attachment service returns direct blob URLs. Database and storage credentials are stored as Container App secrets.
- The API currently uses the PostgreSQL administrator account. Create a narrower application role before treating this as a hardened production setup.
- This is a low-cost starting configuration: PostgreSQL B1ms has no high availability, and production's always-on Container App adds a continuous compute charge. Review capacity, backup retention, and resilience before production traffic grows.

Build both images with a new immutable tag for each release, then redeploy test before prod with that tag. Bicep provisions the infrastructure; it does not deploy to Azure until the `az deployment group create` commands run.
