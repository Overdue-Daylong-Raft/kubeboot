#!/usr/bin/env bash
set -euo pipefail

# Pulumi
curl -fsSL https://get.pulumi.com | sh

# Kubectl
KUBECTL_VERSION=$(curl -L -s https://dl.k8s.io/release/stable.txt)
curl -LO "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/amd64/kubectl"
chmod +x kubectl
mkdir -p ~/.local/bin
mv ./kubectl ~/.local/bin/kubectl

# Helm
curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-4 | bash

# az CLI
curl -fsSL 'https://azurecliprod.blob.core.windows.net/$root/deb_install.sh' | sudo bash

# dotnet
sudo apt-get update && sudo apt-get install -y \
  dotnet-sdk-10.0

# agents
pnpm install -g @openai/codex oh-my-codex