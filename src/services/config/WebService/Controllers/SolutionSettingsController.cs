// <copyright file="SolutionSettingsController.cs" company="3M">
// Copyright (c) 3M. All rights reserved.
// </copyright>

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Mmm.Iot.Common.Services.Filters;
using Mmm.Iot.Config.Services;
using Mmm.Iot.Config.Services.Models;
using Mmm.Iot.Config.WebService.Models;

namespace Mmm.Iot.Config.WebService.Controllers
{
    [Route("v1")]
    [TypeFilter(typeof(ExceptionsFilterAttribute))]
    public class SolutionSettingsController : Controller
    {
        private static readonly string AccessControlExposeHeaders = "Access-Control-Expose-Headers";
        private readonly IStorage storage;
        private readonly IActions actions;

        public SolutionSettingsController(IStorage storage, IActions actions)
        {
            this.storage = storage;
            this.actions = actions;
        }

        [HttpGet("solution-settings/theme")]
        [Authorize("ReadAll")]
        public async Task<object> GetThemeAsync()
        {
            return await this.storage.GetThemeAsync();
        }

        [HttpPut("solution-settings/theme")]
        [Authorize("ReadAll")]
        public async Task<object> SetThemeAsync([FromBody] object theme)
        {
            return await this.storage.SetThemeAsync(theme);
        }

        [HttpGet("solution-settings/logo")]
        [Authorize("ReadAll")]
        public async Task GetLogoAsync()
        {
            var syncIOFeature = this.HttpContext.Features.Get<IHttpBodyControlFeature>();
            if (syncIOFeature != null)
            {
                syncIOFeature.AllowSynchronousIO = true;
            }

            var model = await this.storage.GetLogoAsync();
            this.SetImageResponse(model);
        }

        [HttpPut("solution-settings/logo")]
        [Authorize("ReadAll")]
        public async Task SetLogoAsync()
        {
            var syncIOFeature = this.HttpContext.Features.Get<IHttpBodyControlFeature>();
            if (syncIOFeature != null)
            {
                syncIOFeature.AllowSynchronousIO = true;
            }

            MemoryStream memoryStream = new MemoryStream();
            this.Request.Body.CopyTo(memoryStream);
            byte[] bytes = memoryStream.ToArray();

            var model = new Logo
            {
                IsDefault = false,
            };

            if (bytes.Length > 0)
            {
                model.SetImageFromBytes(bytes);
                model.Type = this.Request.ContentType;
            }

            if (this.Request.Headers[Logo.NameHeader] != StringValues.Empty)
            {
                model.Name = this.Request.Headers[Logo.NameHeader];
            }

            var response = await this.storage.SetLogoAsync(model);
            this.SetImageResponse(response);
        }

        [HttpGet("solution-settings/actions")]
        public async Task<ActionSettingsListApiModel> GetActionsSettingsAsync()
        {
            var actions = await this.actions.GetListAsync();
            return new ActionSettingsListApiModel(actions);
        }

        private void SetImageResponse(Logo model)
        {
            if (model.Name != null)
            {
                this.Response.Headers.Add(Logo.NameHeader, model.Name);
            }

            this.Response.Headers.Add(Logo.IsDefaultHeader, model.IsDefault.ToString());
            this.Response.Headers.Add(
                SolutionSettingsController.AccessControlExposeHeaders,
                Logo.NameHeader + "," + Logo.IsDefaultHeader);
            if (model.Image != null)
            {
                var bytes = model.ConvertImageToBytes();
                this.Response.ContentType = model.Type;
                this.Response.Body.Write(bytes, 0, bytes.Length);
            }
        }
    }
}