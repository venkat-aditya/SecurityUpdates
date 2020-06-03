// <copyright file="Deployments.cs" company="3M">
// Copyright (c) 3M. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Extensions.Logging;
using Mmm.Iot.Common.Services.Config;
using Mmm.Iot.Common.Services.Exceptions;
using Mmm.Iot.Common.Services.Helpers;
using Mmm.Iot.Common.Services.Models;
using Mmm.Iot.Config.Services.Models;
using Mmm.Iot.IoTHubManager.Services.External;
using Mmm.Iot.IoTHubManager.Services.Helpers;
using Mmm.Iot.IoTHubManager.Services.Models;
using Newtonsoft.Json.Linq;
using static Mmm.Iot.Config.Services.Models.DeviceStatusQueries;

namespace Mmm.Iot.IoTHubManager.Services
{
    public class Deployments : IDeployments
    {
        private const int MaxDeployments = 100;
        private const string DeploymentNameLabel = "Name";
        private const string DeploymentGroupIdLabel = "DeviceGroupId";
        private const string DeploymentGroupNameLabel = "DeviceGroupName";
        private const string DeploymentPackageNameLabel = "PackageName";
        private const string RmCreatedLabel = "RMDeployment";
        private const string DeviceGroupIdParameter = "deviceGroupId";
        private const string DeviceGroupQueryParameter = "deviceGroupQuery";
        private const string NameParameter = "name";
        private const string PackageContentParameter = "packageContent";
        private const string ConfigurationTypeParameter = "configType";
        private const string PriorityParameter = "priority";
        private const string DeviceIdKey = "deviceId";
        private const string EdgeManifestSchema = "schemaVersion";
        private const string FailedQueryName = "error";
        private const string SuccessQueryName = "current";
        private readonly ILogger logger;
        private readonly IDeploymentEventLog deploymentLog;
        private readonly ITenantConnectionHelper tenantHelper;
        private readonly IConfigClient configClient;

        public Deployments(
            AppConfig config,
            ILogger<Deployments> logger,
            IDeploymentEventLog deploymentLog,
            ITenantConnectionHelper tenantConnectionHelper,
            IConfigClient packagesConfigClient)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            this.tenantHelper = tenantConnectionHelper;
            this.deploymentLog = deploymentLog;
            this.logger = logger;
            this.configClient = packagesConfigClient;
        }

        public Deployments(ITenantConnectionHelper tenantHelper)
        {
            this.tenantHelper = tenantHelper ?? throw new ArgumentNullException("tenantHelper");
        }

        public async Task<DeploymentServiceModel> CreateAsync(DeploymentServiceModel model, string userId, string tenantId)
        {
            if (string.IsNullOrEmpty(model.DeviceGroupId))
            {
                throw new ArgumentNullException(DeviceGroupIdParameter);
            }

            if (string.IsNullOrEmpty(model.DeviceGroupQuery) && (model.DeviceIds == null || (model.DeviceIds != null && model.DeviceIds.Count() == 0)))
            {
                throw new ArgumentNullException(DeviceGroupQueryParameter);
            }

            if (string.IsNullOrEmpty(model.Name))
            {
                throw new ArgumentNullException(NameParameter);
            }

            if (string.IsNullOrEmpty(model.PackageContent))
            {
                throw new ArgumentNullException(PackageContentParameter);
            }

            if (model.PackageType.Equals(PackageType.DeviceConfiguration)
                && string.IsNullOrEmpty(model.ConfigType))
            {
                throw new ArgumentNullException(ConfigurationTypeParameter);
            }

            if (model.Priority < 0)
            {
                throw new ArgumentOutOfRangeException(
                    PriorityParameter,
                    model.Priority,
                    "The priority provided should be 0 or greater");
            }

            var configuration = ConfigurationsHelper.ToHubConfiguration(model);

            // TODO: Add specific exception handling when exception types are exposed
            // https://github.com/Azure/azure-iot-sdk-csharp/issues/649
            var result = new DeploymentServiceModel(await this.tenantHelper.GetRegistry().AddConfigurationAsync(configuration));

            // Setting the id so that deployment id is populated
            model.Id = result.Id;

            // Log a custom event to Application Insights
            this.deploymentLog.LogDeploymentCreate(model, tenantId, userId);

            return result;
        }

        public async Task<DeploymentServiceListModel> ListAsync()
        {
            // TODO: Currently they only support 100 deployments
            var deployments = await this.tenantHelper.GetRegistry().GetConfigurationsAsync(MaxDeployments);

            if (deployments == null)
            {
                throw new ResourceNotFoundException($"No deployments found for {this.tenantHelper.GetIotHubName()} hub.");
            }

            List<DeploymentServiceModel> serviceModelDeployments =
                deployments.Where(this.CheckIfDeploymentWasMadeByRM)
                           .Select(config => new DeploymentServiceModel(config))
                           .OrderByDescending(conf => conf.CreatedDateTimeUtc)
                           .ToList();

            return new DeploymentServiceListModel(serviceModelDeployments);
        }

        public async Task<DeploymentServiceModel> GetAsync(string deploymentId, bool includeDeviceStatus = false)
        {
            if (string.IsNullOrEmpty(deploymentId))
            {
                throw new ArgumentNullException(nameof(deploymentId));
            }

            var deployment = await this.tenantHelper.GetRegistry().GetConfigurationAsync(deploymentId);

            if (deployment == null)
            {
                throw new ResourceNotFoundException($"Deployment with id {deploymentId} not found.");
            }

            if (!this.CheckIfDeploymentWasMadeByRM(deployment))
            {
                throw new ResourceNotSupportedException($"Deployment with id {deploymentId}" + @" was
                                                        created externally and therefore not supported");
            }

            IDictionary<string, DeploymentStatus> deviceStatuses = this.GetDeviceStatuses(deployment);

            return new DeploymentServiceModel(deployment)
            {
                DeploymentMetrics =
                {
                    DeviceMetrics = this.CalculateDeviceMetrics(deviceStatuses),
                    DeviceStatuses = includeDeviceStatus ? deviceStatuses : null,
                },
            };
        }

        public async Task DeleteAsync(string deploymentId, string userId, string tenantId)
        {
            if (string.IsNullOrEmpty(deploymentId))
            {
                throw new ArgumentNullException(nameof(deploymentId));
            }

            await this.tenantHelper.GetRegistry().RemoveConfigurationAsync(deploymentId);

            // Log a custom event to Application Insights
            this.deploymentLog.LogDeploymentDelete(deploymentId, tenantId, userId);
        }

        public async Task<PackageApiModel> GetPackageAsync(string packageId)
        {
            return await this.configClient.GetPackageAsync(packageId);
        }

        public async Task<DeviceGroup> GetDeviceGroupAsync(string deviceGroupId)
        {
            return await this.configClient.GetDeviceGroupAsync(deviceGroupId);
        }

        private bool CheckIfDeploymentWasMadeByRM(Configuration conf)
        {
            return conf.Labels != null &&
                   conf.Labels.ContainsKey(RmCreatedLabel) &&
                   bool.TryParse(conf.Labels[RmCreatedLabel], out var res) && res;
        }

        private IDictionary<string, DeploymentStatus> GetDeviceStatuses(Configuration deployment)
        {
            string deploymentType = null;
            if (ConfigurationsHelper.IsEdgeDeployment(deployment))
            {
                deploymentType = PackageType.EdgeManifest.ToString();
            }
            else
            {
                deploymentType = PackageType.DeviceConfiguration.ToString();
            }

            deployment.Labels.TryGetValue(ConfigurationsHelper.ConfigTypeLabel, out string configType);
            var queries = GetQueries(deploymentType, configType);

            string deploymentId = deployment.Id;
            var appliedDevices = this.GetDevicesInQuery(queries[QueryType.APPLIED], deploymentId);

            var deviceWithStatus = new Dictionary<string, DeploymentStatus>();

            if (!ConfigurationsHelper.IsEdgeDeployment(deployment) && !configType.Equals(ConfigType.Firmware.ToString()))
            {
                foreach (var devices in appliedDevices)
                {
                    deviceWithStatus.Add(devices, DeploymentStatus.Unknown);
                }

                return deviceWithStatus;
            }

            // Get reported status from custom Metrics if available otherwise use default queries
            var successfulDevices = this.GetDevicesInQuery(deployment.Metrics.Queries.ContainsKey(SuccessQueryName) ? deployment.Metrics.Queries[SuccessQueryName] : queries[QueryType.SUCCESSFUL], deploymentId);
            var failedDevices = this.GetDevicesInQuery(deployment.Metrics.Queries.ContainsKey(FailedQueryName) ? deployment.Metrics.Queries[FailedQueryName] : queries[QueryType.FAILED], deploymentId);

            foreach (var device in appliedDevices)
            {
                if (successfulDevices.Contains(device))
                {
                    deviceWithStatus.Add(device, DeploymentStatus.Succeeded);
                }
                else if (failedDevices.Contains(device))
                {
                    deviceWithStatus.Add(device, DeploymentStatus.Failed);
                }
                else
                {
                    deviceWithStatus.Add(device, DeploymentStatus.Pending);
                }
            }

            return deviceWithStatus;
        }

        private HashSet<string> GetDevicesInQuery(string hubQuery, string deploymentId)
        {
            var query = string.Format(hubQuery, deploymentId);
            var queryResponse = this.tenantHelper.GetRegistry().CreateQuery(query);
            var deviceIds = new HashSet<string>();

            try
            {
                while (queryResponse.HasMoreResults)
                {
                    // TODO: Add pagination with queryOptions
                    var resultSet = queryResponse.GetNextAsJsonAsync();
                    foreach (var result in resultSet.Result)
                    {
                        var deviceId = JToken.Parse(result)[DeviceIdKey];
                        deviceIds.Add(deviceId.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error getting status of devices in query {query}", query);
            }

            return deviceIds;
        }

        private IDictionary<DeploymentStatus, long> CalculateDeviceMetrics(
            IDictionary<string,
            DeploymentStatus> deviceStatuses)
        {
            if (deviceStatuses == null)
            {
                return null;
            }

            IDictionary<DeploymentStatus, long> deviceMetrics = new Dictionary<DeploymentStatus, long>();

            deviceMetrics[DeploymentStatus.Succeeded] = deviceStatuses.Where(item =>
                                                            item.Value == DeploymentStatus.Succeeded).LongCount();

            deviceMetrics[DeploymentStatus.Failed] = deviceStatuses.Where(item =>
                                                            item.Value == DeploymentStatus.Failed).LongCount();

            deviceMetrics[DeploymentStatus.Pending] = deviceStatuses.Where(item =>
                                                            item.Value == DeploymentStatus.Pending).LongCount();

            return deviceMetrics;
        }
    }
}