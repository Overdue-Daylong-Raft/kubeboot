using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pulumi;
using AzureNative = Pulumi.AzureNative;
using Random = Pulumi.Random;
using Tls = Pulumi.Tls;

internal sealed class AzureK3sInfrastructureArgs
{
    public required string ClusterName { get; init; }
    public required string Location { get; init; }
    public required string VmSize { get; init; }
    public required string OsImage { get; init; }
    public required string AdminUsername { get; init; }
    public required int ServerCount { get; init; }
    public required string VnetCidr { get; init; }
    public required string SubnetCidr { get; init; }
    public required string PodCidrs { get; init; }
    public required string ServiceCidrs { get; init; }
    public required string K3sChannel { get; init; }
    public required string TailnetDnsDomain { get; init; }
    public required Input<string> TailscaleAuthKey { get; init; }
    public required string TailscaleNodeTag { get; init; }
}

internal sealed class AzureK3sInfrastructure : ComponentResource
{
    public Output<string> ResourceGroupName { get; }
    public Output<string> PrimaryVmName { get; }
    public Output<string> PrimaryTailnetHost { get; }
    public Output<string> KubeApiUrl { get; }
    public Output<string> PublicSshKey { get; }
    public Output<Dictionary<string, object?>[]> NodeSummaries { get; }

    public AzureK3sInfrastructure(string name, AzureK3sInfrastructureArgs args, ComponentResourceOptions? options = null)
        : base("kubeboot:components:AzureK3sInfrastructure", name, options)
    {
        var osImage = ClusterBootstrap.ParseImage(args.OsImage);
        var primaryNodeName = ClusterBootstrap.NodeName(args.ClusterName, 0);
        var primaryTailnetHost = $"{primaryNodeName}.{args.TailnetDnsDomain}";

        var sshKey = new Tls.PrivateKey($"{name}-ssh-key", new()
        {
            Algorithm = "ED25519",
        }, ChildOptions());

        var k3sToken = new Random.RandomPassword($"{name}-k3s-token", new()
        {
            Length = 48,
            Special = false,
        }, ChildOptions());

        var resourceGroup = new AzureNative.Resources.ResourceGroup($"{name}-rg", new()
        {
            Location = args.Location,
        }, ChildOptions());

        var virtualNetwork = new AzureNative.Network.VirtualNetwork($"{name}-vnet", new()
        {
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,
            AddressSpace = new AzureNative.Network.Inputs.AddressSpaceArgs
            {
                AddressPrefixes = new[] { args.VnetCidr },
            },
            Subnets = new[]
            {
                new AzureNative.Network.Inputs.SubnetArgs
                {
                    Name = $"{args.ClusterName}-subnet",
                    AddressPrefix = args.SubnetCidr,
                },
            },
        }, ChildOptions());

        var securityGroup = new AzureNative.Network.NetworkSecurityGroup($"{name}-nsg", new()
        {
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,
            SecurityRules = Array.Empty<AzureNative.Network.Inputs.SecurityRuleArgs>(),
        }, ChildOptions());

        var nodeSummaries = new List<Output<Dictionary<string, object?>>>();
        Output<string>? primaryVmName = null;

        for (var i = 0; i < args.ServerCount; i++)
        {
            var nodeIndex = i;
            var nodeName = ClusterBootstrap.NodeName(args.ClusterName, nodeIndex);
            var nodeTailnetHost = $"{nodeName}.{args.TailnetDnsDomain}";

            var nic = new AzureNative.Network.NetworkInterface($"{name}-node-{nodeIndex}-nic", new()
            {
                ResourceGroupName = resourceGroup.Name,
                Location = resourceGroup.Location,
                NetworkSecurityGroup = new AzureNative.Network.Inputs.NetworkSecurityGroupArgs
                {
                    Id = securityGroup.Id,
                },
                IpConfigurations = new[]
                {
                    new AzureNative.Network.Inputs.NetworkInterfaceIPConfigurationArgs
                    {
                        Name = $"{nodeName}-ipconfig",
                        PrivateIPAllocationMethod = AzureNative.Network.IPAllocationMethod.Dynamic,
                        Subnet = new AzureNative.Network.Inputs.SubnetArgs
                        {
                            Id = virtualNetwork.Subnets.GetAt(0).Apply(subnet => subnet.Id!),
                        },
                    },
                },
            }, ChildOptions());

            var cloudInit = Output.Tuple(args.TailscaleAuthKey.ToOutput(), k3sToken.Result).Apply(values =>
                Convert.ToBase64String(Encoding.UTF8.GetBytes(ClusterBootstrap.BuildCloudInit(
                    nodeName: nodeName,
                    nodeIndex: nodeIndex,
                    primaryTailnetHost: primaryTailnetHost,
                    tailnetDnsDomain: args.TailnetDnsDomain,
                    tailscaleAuthKey: values.Item1,
                    k3sToken: values.Item2,
                    k3sChannel: args.K3sChannel,
                    podCidrs: args.PodCidrs,
                    serviceCidrs: args.ServiceCidrs,
                    nodeTag: args.TailscaleNodeTag))));

            var vm = new AzureNative.Compute.VirtualMachine($"{name}-node-{nodeIndex}", new()
            {
                ResourceGroupName = resourceGroup.Name,
                Location = resourceGroup.Location,
                NetworkProfile = new AzureNative.Compute.Inputs.NetworkProfileArgs
                {
                    NetworkInterfaces = new[]
                    {
                        new AzureNative.Compute.Inputs.NetworkInterfaceReferenceArgs
                        {
                            Id = nic.Id,
                            Primary = true,
                        },
                    },
                },
                HardwareProfile = new AzureNative.Compute.Inputs.HardwareProfileArgs
                {
                    VmSize = args.VmSize,
                },
                OsProfile = new AzureNative.Compute.Inputs.OSProfileArgs
                {
                    ComputerName = nodeName,
                    AdminUsername = args.AdminUsername,
                    CustomData = cloudInit,
                    LinuxConfiguration = new AzureNative.Compute.Inputs.LinuxConfigurationArgs
                    {
                        DisablePasswordAuthentication = true,
                        Ssh = new AzureNative.Compute.Inputs.SshConfigurationArgs
                        {
                            PublicKeys = new[]
                            {
                                new AzureNative.Compute.Inputs.SshPublicKeyArgs
                                {
                                    KeyData = sshKey.PublicKeyOpenssh,
                                    Path = $"/home/{args.AdminUsername}/.ssh/authorized_keys",
                                },
                            },
                        },
                    },
                },
                StorageProfile = new AzureNative.Compute.Inputs.StorageProfileArgs
                {
                    OsDisk = new AzureNative.Compute.Inputs.OSDiskArgs
                    {
                        CreateOption = AzureNative.Compute.DiskCreateOptionTypes.FromImage,
                        DeleteOption = AzureNative.Compute.DiskDeleteOptionTypes.Delete,
                    },
                    ImageReference = new AzureNative.Compute.Inputs.ImageReferenceArgs
                    {
                        Publisher = osImage.Publisher,
                        Offer = osImage.Offer,
                        Sku = osImage.Sku,
                        Version = osImage.Version,
                    },
                },
            }, ChildOptions(new CustomResourceOptions
            {
                DeleteBeforeReplace = true,
                ReplaceOnChanges = { "osProfile", "networkProfile" },
            }));

            if (nodeIndex == 0)
            {
                primaryVmName = vm.Name;
            }

            nodeSummaries.Add(Output.Tuple(nic.IpConfigurations.GetAt(0).Apply(ip => ip.PrivateIPAddress), vm.Id).Apply(values =>
                new Dictionary<string, object?>
                {
                    ["name"] = nodeName,
                    ["tailnetHost"] = nodeTailnetHost,
                    ["privateIp"] = values.Item1,
                    ["vmId"] = values.Item2,
                }));
        }

        if (primaryVmName is null)
        {
            throw new InvalidOperationException("Primary VM was not created.");
        }

        ResourceGroupName = resourceGroup.Name;
        PrimaryVmName = primaryVmName;
        PrimaryTailnetHost = Output.Create(primaryTailnetHost);
        KubeApiUrl = Output.Format($"https://{primaryTailnetHost}:6443");
        PublicSshKey = sshKey.PublicKeyOpenssh;
        NodeSummaries = Output.All(nodeSummaries.ToArray()).Apply(values => values.Cast<Dictionary<string, object?>>().ToArray());

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["resourceGroupName"] = ResourceGroupName,
            ["primaryVmName"] = PrimaryVmName,
            ["primaryTailnetHost"] = PrimaryTailnetHost,
            ["kubeApiUrl"] = KubeApiUrl,
            ["publicSshKey"] = PublicSshKey,
            ["nodes"] = NodeSummaries,
        });
    }

    private CustomResourceOptions ChildOptions(CustomResourceOptions? options = null)
    {
        options ??= new CustomResourceOptions();
        options.Parent = this;
        return options;
    }
}
