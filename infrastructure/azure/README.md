# Azure deployment

GitHub Actions is the primary deployment path. Follow [Azure deployment setup](../../README.customization.md#5-azure-deployment-setup) for the one-time OIDC bootstrap and GitHub Environment configuration.

Booking uses GitHub's immutable OIDC subject prefix `repo:ucdavis@573450/booking@1380597438`. The bootstrap defaults to this prefix for `ucdavis/booking`. Before bootstrapping a different repository, read `gh api repos/OWNER/REPO/actions/oidc/customization/sub` and pass its `sub_claim_prefix` as the `repositorySubjectPrefix` Bicep parameter. A name-only subject will not match Booking's tokens.

Both GitHub environments use `APP_NAME=booking`. Test targets `rg-booking-test` and production targets `rg-booking-prod`. Shared plans remain `DefaultPlan2` and `Nibbler`. Public URLs are https://booking-test.ucdavis.edu and https://booking.ucdavis.edu.

1. Run **Configure Azure** before the first deployment and whenever infrastructure changes. It also applies runtime settings and can update those settings without deploying a package.
2. Run **CI/CD** to validate and apply generated runtime settings from the selected GitHub Environment, then deploy the app package. Pushes to `main` deploy to test when `AZURE_TEST_READY` is `true`; manual runs can select test or prod.
3. Check the deployment's application health result and verify sign-in at the app's hostname.

Changing GitHub Environment variables or secrets alone does not trigger a deployment. The next automatic or manual **CI/CD** deployment applies the current generated runtime settings. Infrastructure and platform-derived settings still require **Configure Azure**.

Local `deploy_test.sh` and `deploy_prod.sh` are secondary operator tools. They apply runtime settings as part of deployment and do not perform the GitHub workflow's explicit application health check. Verify `/health` and sign-in after using them.

## Production SQL connectivity

Fresh production provisioning does not establish an application network access path to SQL. The template creates SQL with public network access enabled, but its broad Azure-services firewall rule is enabled only in test. It does not create private endpoints or App Service VNet integration.

Before the first production package deployment, configure a suitable SQL firewall rule for the App Service's outbound addresses, or configure private connectivity with routing and DNS. If using firewall rules, account for the outbound addresses that may change with the hosting plan. See [App Service outbound addresses](https://learn.microsoft.com/azure/app-service/overview-inbound-outbound-ips) and [Azure SQL firewall rules](https://learn.microsoft.com/azure/azure-sql/database/firewall-configure).

The application applies EF migrations during startup and `/health` checks SQL connectivity. Confirm `/health` returns 200 after startup; a successful infrastructure deployment alone does not prove database access. Then verify sign-in separately.

The broader documentation rewrite is tracked in [issue #39](https://github.com/ucdavis/web-app-template/issues/39).

## Booking rename

The repository is `ucdavis/booking`; the solution and deployment package are `booking.sln` and `booking.zip`. Azure resources are rebuilt with Booking names and fresh `booking` databases. Historical EF migration IDs retain `Grove` because they identify existing migrations.

The Entra application is `CAES Booking App`. Its Web callbacks include both Booking domains, the replacement Azure hostnames, and localhost. Production SQL allows the App Service’s 31 possible outbound addresses individually; revisit those rules if the hosting plan or outbound addresses change.

For the local rename, clone into a `booking` directory and preserve any uncommitted files and `server/.env` before removing the old checkout. Local development now uses `booking_devcontainer` and database `Booking`. Existing Grove Docker volumes are not migrated or deleted automatically.

## Azure rebuild and DNS cutover

The September 30, 2026 cutover replaced the Grove resource groups with `rg-booking-test` and `rg-booking-prod`. Both public hostnames are bound to the Booking apps. The Grove groups and their deployment identities' shared-plan role assignments have been removed. Automatic test deployment is enabled again.

| Environment | Public hostname | App Service |
| --- | --- | --- |
| Test | `booking-test.ucdavis.edu` | `web-booking-test-rpeipu` |
| Production | `booking.ucdavis.edu` | `web-booking-prod-wngjis` |

Test retains Cloudflare proxying and its Origin CA certificate, stored in `rg-booking-test`. Production uses an App Service managed certificate in `rg-booking-prod`. The existing `asuid` TXT values remain valid. Custom-domain and certificate bindings are managed separately from the base Bicep template.

Public HTTPS, `/health`, unauthenticated API protection, and Entra login redirects were checked with the old apps stopped. A fresh authenticated UC Davis browser sign-in remains a manual verification step.
