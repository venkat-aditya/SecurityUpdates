// <copyright file="DeviceGroupsController.cs" company="3M">
// Copyright (c) 3M. All rights reserved.
// </copyright>

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Mmm.Iot.AsaManager.Services;
using Mmm.Iot.Common.Services;
using Mmm.Iot.Common.Services.External.AsaManager;
using Mmm.Iot.Common.Services.Filters;
using Mmm.Iot.Common.Services.Wrappers;

namespace Mmm.Iot.AsaManager.WebService.Controllers
{
    [Route("v1/[controller]")]
    [TypeFilter(typeof(ExceptionsFilterAttribute))]
    public class DeviceGroupsController : Controller
    {
        private readonly IConverter deviceGroupConverter;
        private readonly IKeyGenerator keyGenerator;
        private readonly ILogger logger;

        public DeviceGroupsController(
            DeviceGroupsConverter devicegroupConverter,
            IKeyGenerator keyGenerator,
            ILogger<DeviceGroupsController> logger)
        {
            this.deviceGroupConverter = devicegroupConverter;
            this.keyGenerator = keyGenerator;
            this.logger = logger;
        }

        [HttpPost("")]
        public BeginConversionApiModel BeginDeviceGroupConversion()
        {
            string tenantId = this.GetTenantId();
            string operationId = this.keyGenerator.Generate();

            // This can be a long running process due to querying of cosmos/iothub - don't wait for itsyn
            this.Forget(this.deviceGroupConverter.ConvertAsync(tenantId, operationId), operationId);

            // Return the operationId of the devicegroup conversion synchronous process
            return new BeginConversionApiModel
            {
                TenantId = tenantId,
                OperationId = operationId,
            };
        }

        private void Forget(Task task, string operationId)
        {
            task.ContinueWith(
                t => { this.logger.LogError(t.Exception, "An exception occurred during the background conversion. OperationId {operationId}", operationId); },
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}