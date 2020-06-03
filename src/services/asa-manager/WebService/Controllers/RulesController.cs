// <copyright file="RulesController.cs" company="3M">
// Copyright (c) 3M. All rights reserved.
// </copyright>

using System.Threading.Tasks;
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
    public class RulesController : Controller
    {
        private readonly IConverter ruleConverter;
        private readonly IKeyGenerator keyGenerator;
        private readonly ILogger logger;

        public RulesController(
            RulesConverter ruleConverter,
            IKeyGenerator keyGenerator,
            ILogger<RulesController> logger)
        {
            this.ruleConverter = ruleConverter;
            this.keyGenerator = keyGenerator;
            this.logger = logger;
        }

        [HttpPost("")]
        public BeginConversionApiModel BeginRuleConversion()
        {
            string tenantId = this.GetTenantId();
            string operationId = this.keyGenerator.Generate();

            // This can be a long running process due to querying of cosmos/iothub - don't wait for it
            this.Forget(this.ruleConverter.ConvertAsync(tenantId, operationId), operationId);

            // Return the operationId of the rule conversion synchronous process
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