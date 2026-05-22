using Pulumi;
using AzureNative = Pulumi.AzureNative;
using K8s = Pulumi.Kubernetes;

internal sealed class K3sClusterAccessArgs
{
    public required string ClusterName { get; init; }
    public required string Location { get; init; }
    public required Input<string> ResourceGroupName { get; init; }
    public required Input<string> PrimaryVmName { get; init; }
    public required Input<string> PrimaryTailnetHost { get; init; }
}

internal sealed class K3sClusterAccess : ComponentResource
{
    public Output<string> Kubeconfig { get; }
    public Output<string> KubeApiUrl { get; }
    public K8s.Provider KubernetesProvider { get; }

    public K3sClusterAccess(string name, K3sClusterAccessArgs args, ComponentResourceOptions? options = null)
        : base("kubeboot:components:K3sClusterAccess", name, options)
    {
        var kubeconfigCommand = new AzureNative.Compute.VirtualMachineRunCommandByVirtualMachine($"{name}-kubeconfig", new()
        {
            ResourceGroupName = args.ResourceGroupName,
            VmName = args.PrimaryVmName,
            RunCommandName = "export-kubeconfig",
            Location = args.Location,
            Source = new AzureNative.Compute.Inputs.VirtualMachineRunCommandScriptSourceArgs
            {
                Script = args.PrimaryTailnetHost.ToOutput().Apply(ClusterBootstrap.BuildKubeconfigExportScript),
            },
            TimeoutInSeconds = 900,
            TreatFailureAsDeploymentFailure = true,
        }, ChildOptions());

        Kubeconfig = Output.CreateSecret(Output.Tuple(kubeconfigCommand.Id, args.ResourceGroupName.ToOutput(), args.PrimaryVmName.ToOutput()).Apply(async values =>
        {
            var runCommand = await AzureNative.Compute.GetVirtualMachineRunCommandByVirtualMachine.InvokeAsync(new()
            {
                ResourceGroupName = values.Item2,
                VmName = values.Item3,
                RunCommandName = "export-kubeconfig",
                Expand = "instanceView",
            });

            return ClusterBootstrap.ExtractRunCommandOutput(runCommand.InstanceView?.Output);
        }));

        KubeApiUrl = args.PrimaryTailnetHost.ToOutput().Apply(host => $"https://{host}:6443");

        KubernetesProvider = new K8s.Provider($"{name}-provider", new()
        {
            KubeConfig = Kubeconfig,
            ClusterIdentifier = args.ClusterName,
            EnableServerSideApply = true,
        }, ChildOptions(new CustomResourceOptions
        {
            DependsOn = { kubeconfigCommand },
        }));

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["kubeconfig"] = Kubeconfig,
            ["kubeApiUrl"] = KubeApiUrl,
        });
    }

    private CustomResourceOptions ChildOptions(CustomResourceOptions? options = null)
    {
        options ??= new CustomResourceOptions();
        options.Parent = this;
        return options;
    }
}
