using Pulumi;

return await Deployment.RunAsync(() =>
{
    var settings = StackSettings.Load();

    var infrastructure = new AzureK3sInfrastructure("control-infra", new()
    {
        ClusterName = settings.ClusterName,
        Location = settings.AzureLocation,
        VmSize = settings.VmSize,
        OsImage = settings.OsImage,
        AdminUsername = settings.AdminUsername,
        ServerCount = settings.ServerCount,
        VnetCidr = settings.VnetCidr,
        SubnetCidr = settings.SubnetCidr,
        PodCidrs = settings.PodCidrs,
        ServiceCidrs = settings.ServiceCidrs,
        K3sChannel = settings.K3sChannel,
        TailnetDnsDomain = settings.TailnetDnsDomain,
        TailscaleAuthKey = settings.TailscaleAuthKey,
        TailscaleNodeTag = settings.TailscaleNodeTag,
    });

    var clusterAccess = new K3sClusterAccess("control-cluster", new()
    {
        ClusterName = settings.ClusterName,
        Location = settings.AzureLocation,
        ResourceGroupName = infrastructure.ResourceGroupName,
        PrimaryVmName = infrastructure.PrimaryVmName,
        PrimaryTailnetHost = infrastructure.PrimaryTailnetHost,
    }, new ComponentResourceOptions
    {
        DependsOn = { infrastructure },
    });

    // var tailnetPolicy = new TailnetPolicy("tailnet-policy", new()
    // {
    //     ClusterName = settings.ClusterName,
    //     TailnetDnsDomain = settings.TailnetDnsDomain,
    //     NodeTag = settings.TailscaleNodeTag,
    //     OperatorTag = settings.TailscaleOperatorTag,
    //     ProxyTag = settings.TailscaleProxyTag,
    //     ArgoCdServiceTag = settings.ArgoCdServiceTag,
    //     KubernetesApiAccessSources = settings.TailscaleKubernetesApiAccessSources,
    //     ArgoCdAccessSources = settings.ArgoCdAccessSources,
    //     ExternalPolicyLink = settings.TailscalePolicyExternalLink,
    // });

    var tailscaleOperator = new TailscaleOperatorComponent("tailscale-operator", new()
    {
        ClusterName = settings.ClusterName,
        TailnetDnsDomain = settings.TailnetDnsDomain,
        KubernetesProvider = clusterAccess.KubernetesProvider,
        OAuthClientId = settings.TailscaleOperatorClientId,
        OAuthClientSecret = settings.TailscaleOperatorClientSecret,
        OperatorTag = settings.TailscaleOperatorTag,
        ProxyTag = settings.TailscaleProxyTag,
    }, new ComponentResourceOptions
    {
        DependsOn = { clusterAccess /*, tailnetPolicy */ },
    });

    var argoCd = new ArgoCdBootstrap("argocd-bootstrap", new()
    {
        ClusterName = settings.ClusterName,
        TailnetDnsDomain = settings.TailnetDnsDomain,
        KubernetesProvider = clusterAccess.KubernetesProvider,
        SharedIngressProxyGroupName = tailscaleOperator.SharedIngressProxyGroupName,
        ArgoCdServiceTag = settings.ArgoCdServiceTag,
        HostLabel = settings.ArgoCdHost,
        GitOpsRepoUrl = settings.GitOpsRepoUrl,
        GitOpsRevision = settings.GitOpsRevision,
        GitOpsBootstrapPath = settings.GitOpsBootstrapPath,
    }, new ComponentResourceOptions
    {
        DependsOn = { tailscaleOperator },
    });

    return new Dictionary<string, object?>
    {
        ["clusterName"] = settings.ClusterName,
        ["nodeCount"] = settings.ServerCount,
        ["nodes"] = infrastructure.NodeSummaries,
        ["kubeApiUrl"] = infrastructure.KubeApiUrl,
        ["kubeconfig"] = clusterAccess.Kubeconfig,
        ["publicSshKey"] = infrastructure.PublicSshKey,
        ["k8sOperatorHostname"] = tailscaleOperator.OperatorFqdn,
        ["argocdUrl"] = argoCd.ArgoCdUrl,
        ["argocdRootApplication"] = argoCd.RootApplicationName,
        ["podCidrs"] = settings.PodCidrs,
        ["serviceCidrs"] = settings.ServiceCidrs,
    };
});
