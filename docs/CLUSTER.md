# Cluster Living Document

_Last updated: 2026-05-21._

This document records the current intended topology and configuration contract for the `kubeboot` Pulumi-managed control cluster.

## Current intended stack shape

- Pulumi project: `kubeboot`
- Active stack: `k3s`
- First-pass topology: **single control cluster**
- Production control plane responsibilities:
  - Azure resource group, network, and private k3s VM nodes
  - k3s bootstrap over Tailscale
  - tailnet policy + tailnet settings managed through the Pulumi Tailscale provider
  - Tailscale Kubernetes Operator with API-server proxy enabled
  - private Argo CD exposed through a Tailscale HA ingress `ProxyGroup`
  - Argo CD root `Application` bootstrapping repo-defined app-of-apps manifests

## Component model

The Pulumi program is split into reusable components:

- `AzureK3sInfrastructure` — Azure RG/VNet/subnet/NSG/VMs plus bootstrap secrets and node metadata
- `K3sClusterAccess` — kubeconfig export and Kubernetes provider creation
- `TailnetPolicy` — `tailscale.TailnetSettings` + authoritative `tailscale.Acl`
- `TailscaleOperatorComponent` — TKO Helm release, API-server proxy config, shared ingress `ProxyClass` / `ProxyGroup`
- `ArgoCdBootstrap` — Argo CD Helm release, private ingress, root project, and root application

## Tailnet policy model

The stack is now intended to manage the tailnet policy file authoritatively.

Managed policy responsibilities:

- enable tailnet HTTPS
- mark ACLs as externally managed
- manage tag ownership for:
  - node tag
  - operator tag
  - shared proxy tag
  - Argo CD service tag
- allow API server access through the TKO proxy using `tailscale.com/cap/kubernetes`
- allow control-plane node-to-node communication for the tagged k3s servers
- allow HA Tailscale Service access for the Argo CD ingress tag
- allow ICMP to the HA ingress proxy devices
- validate the policy with top-level Tailscale `tests`

## Argo CD bootstrap model

Pulumi installs Argo CD only far enough to let Argo CD bootstrap from Git.

Pulumi-owned resources:

- Argo CD Helm release in `argocd`
- private Tailscale ingress for `argocd.<tailnet>`
- root `AppProject` `control-root`
- root `Application` `control-root`

Git-owned resources under `gitops/`:

- `gitops/clusters/control/` — app-of-apps entrypoint for the control cluster
- `gitops/platform/control/bootstrap/` — initial repo-managed bootstrap payload

The repo currently ships a placeholder child application manifest that must have its `repoURL` replaced with the real Git remote URL used for this repository.

## Stack config contract

Existing config still used:

- `clusterName`, `serverCount`, `vmSize`, `osImage`, `adminUsername`
- `vnetCidr`, `subnetCidr`, `podCidrs`, `serviceCidrs`, `k3sChannel`
- `tailnetDnsDomain`, `tailscaleAuthKey`, `tailscaleNodeTag`
- `tailscaleOperatorClientSecret`, optional `tailscaleOperatorClientId`
- `tailscaleOperatorTag`
- `azure-native:location`

New project config:

- `kubeboot:gitopsRepoUrl`
- `kubeboot:gitopsRevision`
- `kubeboot:gitopsBootstrapPath`
- `kubeboot:tailscaleProxyTag`
- `kubeboot:argocdServiceTag`
- `kubeboot:argocdHost`
- `kubeboot:tailscaleKubernetesApiAccessSrcs`
- `kubeboot:argocdAccessSrcs`
- `kubeboot:tailscalePolicyExternalLink`

Tailscale provider config:

- `tailscale:tailnet`
- one supported provider auth method, such as `tailscale:oauthClientId` + `tailscale:oauthClientSecret`

## Outputs

The stack now exposes:

- `kubeconfig`
- `kubeApiUrl`
- `k8sOperatorHostname`
- `argocdUrl`
- `argocdRootApplication`
- `nodes`

## Validation status

This refactor changes the architecture from a single-file stack with a Pulumi-owned hello-world smoke app to a componentized control-cluster bootstrap stack.

After deployment, validate at minimum:

```bash
pulumi stack output kubeconfig --show-secrets > kubeconfig
KUBECONFIG=./kubeconfig kubectl get nodes -o wide
KUBECONFIG=./kubeconfig kubectl -n tailscale get pods
KUBECONFIG=./kubeconfig kubectl -n argocd get pods
KUBECONFIG=./kubeconfig kubectl get proxygroup shared-ingress-proxies
```

Then confirm:

- `https://argocd.<tailnet>` is reachable over Tailscale
- `tailscale configure kubeconfig <cluster>-k8s-operator.<tailnet>` works for an authorized admin device
- Argo CD root application `control-root` syncs successfully
