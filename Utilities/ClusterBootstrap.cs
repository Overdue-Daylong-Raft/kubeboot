using System.Text;

internal static class ClusterBootstrap
{
    public static (string Publisher, string Offer, string Sku, string Version) ParseImage(string image)
    {
        var parts = image.Split(':');
        if (parts.Length != 4 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("osImage must have the form publisher:offer:sku:version.", nameof(image));
        }

        return (parts[0], parts[1], parts[2], parts[3]);
    }

    public static string NodeName(string clusterName, int nodeIndex) => $"{clusterName}-server-{nodeIndex}";

    public static string BuildCloudInit(
        string nodeName,
        int nodeIndex,
        string primaryTailnetHost,
        string tailnetDnsDomain,
        string tailscaleAuthKey,
        string k3sToken,
        string k3sChannel,
        string podCidrs,
        string serviceCidrs,
        string nodeTag)
    {
        var serverMode = nodeIndex == 0
            ? "--cluster-init"
            : $"--server https://{primaryTailnetHost}:6443";

        var configureCoreDns = nodeIndex == 0 ? "true" : "false";

        return $$"""
#cloud-config
package_update: true
packages:
  - ca-certificates
  - curl
write_files:
  - path: /usr/local/sbin/bootstrap-k3s-tailscale.sh
    owner: root:root
    permissions: '0700'
    content: |
      #!/usr/bin/env bash
      set -euo pipefail

      NODE_NAME='{{EscapeShell(nodeName)}}'
      NODE_TAG='{{EscapeShell(nodeTag)}}'
      TAILNET_DNS_DOMAIN='{{EscapeShell(tailnetDnsDomain)}}'
      PRIMARY_TAILNET_HOST='{{EscapeShell(primaryTailnetHost)}}'
      TS_AUTHKEY='{{EscapeShell(tailscaleAuthKey)}}'
      K3S_TOKEN='{{EscapeShell(k3sToken)}}'
      K3S_CHANNEL='{{EscapeShell(k3sChannel)}}'
      POD_CIDRS='{{EscapeShell(podCidrs)}}'
      SERVICE_CIDRS='{{EscapeShell(serviceCidrs)}}'
      SERVER_MODE='{{EscapeShell(serverMode)}}'
      CONFIGURE_COREDNS='{{configureCoreDns}}'

      install_tailscale() {
        if ! command -v tailscale >/dev/null 2>&1; then
          curl -fsSL https://tailscale.com/install.sh | sh
        fi
        systemctl enable --now tailscaled
        tailscale up \
          --auth-key="${TS_AUTHKEY}" \
          --hostname="${NODE_NAME}" \
          --advertise-tags="${NODE_TAG}" \
          --accept-dns=true
      }

      wait_for_tailscale_ip() {
        for _ in $(seq 1 120); do
          TS_IP="$(tailscale ip -4 2>/dev/null | head -n1 || true)"
          if [ -n "${TS_IP}" ]; then
            printf '%s\n' "${TS_IP}"
            return 0
          fi
          sleep 5
        done
        echo 'Timed out waiting for Tailscale IPv4 address' >&2
        return 1
      }

      wait_for_primary_server() {
        if [ "${CONFIGURE_COREDNS}" = 'true' ]; then
          return 0
        fi
        for _ in $(seq 1 180); do
          if curl -kfsS "https://${PRIMARY_TAILNET_HOST}:6443/ping" >/dev/null 2>&1; then
            return 0
          fi
          sleep 10
        done
        echo "Timed out waiting for k3s API at ${PRIMARY_TAILNET_HOST}" >&2
        return 1
      }

      install_k3s() {
        TS_IP="$(wait_for_tailscale_ip)"
        wait_for_primary_server
        curl -sfL https://get.k3s.io | \
          INSTALL_K3S_CHANNEL="${K3S_CHANNEL}" \
          INSTALL_K3S_EXEC="server ${SERVER_MODE} --token ${K3S_TOKEN} --node-name ${NODE_NAME} --node-ip ${TS_IP} --node-external-ip ${TS_IP} --cluster-cidr ${POD_CIDRS} --service-cidr ${SERVICE_CIDRS} --vpn-auth name=tailscale,joinKey=${TS_AUTHKEY}" \
          sh -
      }

      configure_coredns_for_tailnet() {
        if [ "${CONFIGURE_COREDNS}" != 'true' ]; then
          return 0
        fi

        export KUBECONFIG=/etc/rancher/k3s/k3s.yaml
        for _ in $(seq 1 120); do
          if kubectl -n kube-system get configmap coredns >/dev/null 2>&1; then
            break
          fi
          sleep 5
        done

        cat > /tmp/coredns-custom.yaml <<EOF_COREDNS
apiVersion: v1
kind: ConfigMap
metadata:
  name: coredns-custom
  namespace: kube-system
data:
  tailnet.server: |
    ${TAILNET_DNS_DOMAIN}:53 {
        errors
        cache 30
        forward . 100.100.100.100
    }
EOF_COREDNS
        kubectl apply -f /tmp/coredns-custom.yaml
        kubectl -n kube-system rollout restart deployment/coredns
        kubectl -n kube-system rollout status deployment/coredns --timeout=120s
      }

      install_tailscale
      install_k3s
      configure_coredns_for_tailnet
runcmd:
  - [ bash, /usr/local/sbin/bootstrap-k3s-tailscale.sh ]
""";
    }

    public static string BuildKubeconfigExportScript(string primaryTailnetHost) => $$"""
set -euo pipefail
for _ in $(seq 1 180); do
  if [ -s /etc/rancher/k3s/k3s.yaml ]; then
    break
  fi
  sleep 5
done
if [ ! -s /etc/rancher/k3s/k3s.yaml ]; then
  echo 'Timed out waiting for /etc/rancher/k3s/k3s.yaml' >&2
  exit 1
fi
sed 's#server: https://127.0.0.1:6443#server: https://{{EscapeSedReplacement(primaryTailnetHost)}}:6443#' /etc/rancher/k3s/k3s.yaml
""";

    public static string ExtractRunCommandOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        const string kubeconfigStart = "apiVersion: v1";
        var start = output.IndexOf(kubeconfigStart, StringComparison.Ordinal);
        if (start < 0)
        {
            return output.Trim();
        }

        var kubeconfig = output[start..];
        foreach (var marker in new[] { "\n[stderr]", "\nEnable succeeded:", "\nProvisioningState" })
        {
            var markerIndex = kubeconfig.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                kubeconfig = kubeconfig[..markerIndex];
            }
        }

        return kubeconfig.TrimEnd() + "\n";
    }

    public static string DeriveTailscaleOAuthClientId(string clientSecret)
    {
        const string prefix = "tskey-client-";
        var trimmed = clientSecret.Trim();
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("tailscaleOperatorClientSecret must be a Tailscale OAuth client secret or tailscaleOperatorClientId must be configured explicitly.");
        }

        var remainder = trimmed[prefix.Length..];
        var separator = remainder.IndexOf('-', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new ArgumentException("Unable to derive OAuth client ID from tailscaleOperatorClientSecret; set tailscaleOperatorClientId explicitly.");
        }

        return remainder[..separator];
    }

    private static string EscapeShell(string value) => value.Replace("'", "'\\''");

    private static string EscapeSedReplacement(string value) => value.Replace("\\", "\\\\").Replace("&", "\\&");
}
