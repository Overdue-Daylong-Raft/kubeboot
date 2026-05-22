# kubeboot

Pulumi-managed private k3s control cluster on Azure, using Tailscale for node connectivity, API-server access, and private Argo CD exposure.

## Layout

- `Components/` — reusable Pulumi components for Azure infra, cluster access, tailnet policy, TKO, and Argo CD bootstrap
- `gitops/` — Argo CD app-of-apps bootstrap layout
- `docs/CLUSTER.md` — cluster living document and config contract

## Required stack config

In addition to the existing Azure/k3s settings, set:

- `kubeboot:gitopsRepoUrl`
- `kubeboot:gitopsRevision` (optional, defaults to `master`)
- `kubeboot:gitopsBootstrapPath` (optional, defaults to `gitops/clusters/control`)
- `kubeboot:tailscaleProxyTag` (optional)
- `kubeboot:argocdServiceTag` (optional)
- `kubeboot:argocdHost` (optional)
- `kubeboot:tailscaleKubernetesApiAccessSrcs` (optional CSV)
- `kubeboot:argocdAccessSrcs` (optional CSV)
- `kubeboot:tailscalePolicyExternalLink` (optional)
- `tailscale:tailnet`
- one Tailscale provider credential method (for example `tailscale:oauthClientId` + `tailscale:oauthClientSecret`)

## Notes

- Pulumi installs Argo CD and creates the root `Application`; Argo CD then bootstraps the repo-defined app-of-apps tree.
- `gitops/clusters/control/10-bootstrap-app.yaml` intentionally contains a placeholder `repoURL`. Replace it with the same Git repository URL you set in Pulumi stack config.
